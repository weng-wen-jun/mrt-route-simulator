using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MrtRouteSimulator.UpdateCore;

public sealed class PackageStager(HttpClient httpClient)
{
    public async Task<StagedUpdate> StageAsync(UpdateOffer offer, string cacheRoot, IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        UpdateManifest manifest = offer.Manifest;
        UpdatePathPolicy.ValidateManifest(manifest);
        UpdateDownload.ValidateAssetUri(offer.Package.DownloadUri);
        if (offer.Package.Name != manifest.PackageFileName || offer.Package.Size != manifest.PackageSize || !UpdateDownload.RequireDigest(offer.Package.Digest).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("套件與更新清單不相符。");
        UpdatePathPolicy.RejectReparsePoints(cacheRoot);
        Directory.CreateDirectory(cacheRoot);
        string root = Path.Combine(Path.GetFullPath(cacheRoot), Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        string archivePath = Path.Combine(root, "download.partial");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, offer.Package.DownloadUri);
            request.Headers.UserAgent.ParseAdd("MrtRouteSimulator-Updater/1");
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != manifest.PackageSize) throw new InvalidDataException("套件 HTTP 大小與清單不一致。");
            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                long total = 0;
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    if (total > manifest.PackageSize) throw new InvalidDataException("套件下載超過清單大小。");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new(total, manifest.PackageSize, "下載更新套件"));
                }
                if (total != manifest.PackageSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.PackageSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("套件下載大小或 SHA256 驗證失敗。");
                await output.FlushAsync(cancellationToken);
            }
            await ExtractAsync(archivePath, payload, manifest, cancellationToken);
            File.Delete(archivePath);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateStaged(root, manifest);
            await File.WriteAllTextAsync(Path.Combine(root, UpdateConstants.ManifestFile), JsonSerializer.Serialize(manifest, UpdateJson.Options), cancellationToken);
            progress?.Report(new(manifest.PackageSize, manifest.PackageSize, "更新套件已驗證"));
            return new(root, manifest);
        }
        catch
        {
            // root was created by this operation, never supplied by an archive entry.
            if (Directory.Exists(root)) Directory.Delete(root, true);
            throw;
        }
    }

    private static async Task ExtractAsync(string archivePath, string payload, UpdateManifest manifest, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > UpdatePathPolicy.MaximumFiles * 2) throw new InvalidDataException("ZIP 項目過多。");
        var expected = manifest.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in expected.Keys)
        {
            string[] parts = name.Split('/');
            for (int i = 1; i < parts.Length; i++) directories.Add(string.Join('/', parts.Take(i)));
        }
        long expanded = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException("ZIP 不允許連結、reparse point 或特殊檔案。");
            bool directory = entry.FullName.EndsWith('/');
            string name = UpdatePathPolicy.NormalizeRelativePath(directory ? entry.FullName[..^1] : entry.FullName);
            if (!seen.Add(name)) throw new InvalidDataException("ZIP 含大小寫重複項目。");
            if (directory)
            {
                if (entry.Length != 0 || !directories.Contains(name)) throw new InvalidDataException("ZIP 含清單以外目錄。");
                continue;
            }
            if (unixType == 0x4000 || !expected.TryGetValue(name, out UpdateFile? file) || name != file.Path || entry.Length != file.Size)
                throw new InvalidDataException("ZIP 檔案與清單不相符。");
            expanded = checked(expanded + entry.Length);
            if (expanded > UpdatePathPolicy.MaximumExpandedBytes || (entry.Length > 1024 * 1024 && entry.Length / Math.Max(1, entry.CompressedLength) > 1000)) throw new InvalidDataException("ZIP 解壓大小或壓縮比超過上限。");
            string target = UpdatePathPolicy.GetContainedPath(payload, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using Stream input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                written += read;
                if (written > file.Size) throw new InvalidDataException("ZIP 解壓大小超過清單。");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (written != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP 個別檔案 SHA256 或大小驗證失敗。");
        }
        if (expected.Keys.Any(x => !seen.Contains(x))) throw new InvalidDataException("ZIP 缺少清單檔案。");
    }

    private static IEnumerable<string> EnumerateSafeEntries(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        int count = 0;
        while (pending.Count > 0)
        {
            string directory = pending.Dequeue();
            UpdatePathPolicy.RejectReparsePoints(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > UpdatePathPolicy.MaximumFiles * 25) throw new InvalidDataException("暫存更新目錄項目過多。");
                UpdatePathPolicy.RejectReparsePoints(entry);
                yield return entry;
                if (Directory.Exists(entry)) pending.Enqueue(entry);
            }
        }
    }

    public static void ValidateStaged(string stageRoot, UpdateManifest manifest)
    {
        UpdatePathPolicy.ValidateManifest(manifest);
        UpdatePathPolicy.RejectReparsePoints(stageRoot);
        string payload = Path.Combine(stageRoot, "payload");
        if (!Directory.Exists(payload)) throw new InvalidDataException("找不到已準備的更新內容。");
        var expected = manifest.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in EnumerateSafeEntries(payload))
        {
            UpdatePathPolicy.RejectReparsePoints(path);
            if (Directory.Exists(path)) continue;
            string relative = Path.GetRelativePath(payload, path).Replace('\\', '/');
            if (!seen.Add(relative) || !expected.TryGetValue(relative, out UpdateFile? file) || relative != file.Path) throw new InvalidDataException("已準備的更新含清單以外檔案。");
            using var stream = File.OpenRead(path);
            if (stream.Length != file.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("已準備的更新內容已變更。");
        }
        if (seen.Count != expected.Count) throw new InvalidDataException("已準備的更新缺少檔案。");
    }
}
