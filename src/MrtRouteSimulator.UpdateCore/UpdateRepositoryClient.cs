using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MrtRouteSimulator.UpdateCore;

public sealed class UpdateRepositoryClient(HttpClient httpClient)
{
    public async Task<UpdateCheckResult> CheckAsync(UpdateKind kind, Version currentVersion, Version appVersion, int schemaVersion, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        string prefix = kind == UpdateKind.Application ? "v" : "data-v";
        string manifestName = kind == UpdateKind.Application ? "mrt-application-update.json" : "mrt-data-update.json";
        var releases = new List<(Version Version, JsonElement Release, string Tag)>();
        // Search a bounded 1,000 recent releases so frequent application releases do not hide data releases.
        for (int page = 1; page <= 10; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{UpdateConstants.Repository}/releases?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd("MrtRouteSimulator-Updater/1");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return new(null, "官方儲存庫尚未發佈更新。");
            response.EnsureSuccessStatusCode();
            byte[] releaseBytes = await UpdateDownload.ReadBoundedAsync(response, 8 * 1024 * 1024, cancellationToken);
            using JsonDocument json = JsonDocument.Parse(releaseBytes);
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("GitHub Release 回應格式不正確。");
            foreach (JsonElement release in json.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                string tag = release.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith(prefix, StringComparison.Ordinal)) continue;
                try { releases.Add((UpdatePathPolicy.ParseStableVersion(tag[prefix.Length..]), release.Clone(), tag)); } catch (InvalidDataException) { }
            }
            if (json.RootElement.GetArrayLength() < 100) break;
        }
        if (releases.Count == 0) return new(null, "尚未發佈此類型的穩定更新。");
        var latest = releases.MaxBy(x => x.Version);
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build), Math.Max(0, currentVersion.Revision));
        if (latest.Version <= current) return new(null, "目前已是最新穩定版本。");
        JsonElement[] assets = latest.Release.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement[] metadata = assets.Where(x => x.GetProperty("name").GetString() == manifestName).ToArray();
        if (metadata.Length == 0) return new(null, $"已發佈 {latest.Tag}，但尚未提供可驗證的更新清單；請至官方 Release 手動下載。");
        if (metadata.Length != 1) throw new InvalidDataException("Release 更新清單重複。");
        ReleaseAsset manifestAsset = ReadAsset(metadata[0], latest.Tag);
        string digest = UpdateDownload.RequireDigest(manifestAsset.Digest);
        if (manifestAsset.Size <= 0 || manifestAsset.Size > UpdatePathPolicy.MaximumManifestBytes) throw new InvalidDataException("更新清單大小超過上限。");
        byte[] bytes = await UpdateDownload.DownloadBytesAsync(httpClient, manifestAsset.DownloadUri, manifestAsset.Size, cancellationToken);
        UpdateDownload.VerifyHash(bytes, digest);
        UpdateManifest manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, UpdateJson.Options) ?? throw new InvalidDataException("更新清單為空。");
        UpdatePathPolicy.ValidateManifest(manifest);
        if (manifest.Kind != kind || UpdatePathPolicy.ParseStableVersion(manifest.Version) != latest.Version)
            throw new InvalidDataException("Release 版本與更新清單不相符。");
        if (UpdatePathPolicy.ParseStableVersion(manifest.MinimumAppVersion) > new Version(appVersion.Major, appVersion.Minor, Math.Max(0, appVersion.Build), Math.Max(0, appVersion.Revision)))
            return new(null, $"更新 {manifest.Version} 需要程式 {manifest.MinimumAppVersion} 以上版本。");
        if (kind == UpdateKind.Data && manifest.SchemaVersion != schemaVersion)
            return new(null, $"資料更新使用 Schema {manifest.SchemaVersion}，目前程式使用 Schema {schemaVersion}；請先更新程式。");
        JsonElement[] packages = assets.Where(x => x.GetProperty("name").GetString() == manifest.PackageFileName).ToArray();
        if (packages.Length != 1) throw new InvalidDataException("更新清單指定的套件不存在或重複。");
        ReleaseAsset package = ReadAsset(packages[0], latest.Tag);
        if (package.Size != manifest.PackageSize || !UpdateDownload.RequireDigest(package.Digest).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub 套件雜湊或大小與清單不一致。");
        return new(new(manifest, package, latest.Release.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "", new Uri($"https://github.com/{UpdateConstants.Repository}/releases/tag/{latest.Tag}")), $"可更新至 {manifest.Version}。");
    }

    private static ReleaseAsset ReadAsset(JsonElement asset, string tag)
    {
        string name = asset.GetProperty("name").GetString() ?? throw new InvalidDataException("資產檔名遺失。");
        UpdatePathPolicy.NormalizeRelativePath(name);
        if (name.Contains('/')) throw new InvalidDataException("資產檔名不合法。");
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "", UriKind.Absolute);
        UpdateDownload.ValidateAssetUri(uri);
        string expected = $"https://github.com/{UpdateConstants.Repository}/releases/download/{tag}/{Uri.EscapeDataString(name)}";
        if (!uri.AbsoluteUri.Equals(new Uri(expected).AbsoluteUri, StringComparison.Ordinal)) throw new InvalidDataException("資產下載路徑與 Release 不一致。");
        return new(name, uri, asset.GetProperty("size").GetInt64(), asset.TryGetProperty("digest", out JsonElement digest) && digest.ValueKind == JsonValueKind.String ? digest.GetString() : null);
    }
}

internal static class UpdateDownload
{
    internal static void ValidateAssetUri(Uri uri)
    {
        string prefix = $"/{UpdateConstants.Repository}/releases/download/";
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("更新只能從固定官方 GitHub 儲存庫下載。");
        string[] tail = Uri.UnescapeDataString(uri.AbsolutePath[prefix.Length..]).Split('/');
        if (tail.Length != 2 || tail.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("GitHub 資產路徑不合法。");
        string tag = tail[0];
        _ = UpdatePathPolicy.ParseStableVersion(tag.StartsWith("data-v", StringComparison.Ordinal) ? tag[6..] : tag.StartsWith('v') ? tag[1..] : "");
        UpdatePathPolicy.NormalizeRelativePath(tail[1]);
    }
    internal static string RequireDigest(string? digest)
    {
        if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !UpdatePathPolicy.IsHash(digest[7..])) throw new InvalidDataException("GitHub 資產缺少 SHA256 digest，無法安全更新。");
        return digest[7..];
    }
    internal static void VerifyHash(ReadOnlySpan<byte> bytes, string hash)
    {
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新檔案 SHA256 驗證失敗。");
    }
    internal static async Task<byte[]> DownloadBytesAsync(HttpClient client, Uri uri, long expectedSize, CancellationToken ct)
    {
        ValidateAssetUri(uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("MrtRouteSimulator-Updater/1");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await ReadBoundedAsync(response, expectedSize, ct);
        if (bytes.LongLength != expectedSize) throw new InvalidDataException("下載大小與 GitHub 資產不符。");
        return bytes;
    }
    internal static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, long limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is long size && size > limit) throw new InvalidDataException("HTTP 下載大小超過上限。");
        await using Stream input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        byte[] buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("HTTP 下載大小超過上限。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
