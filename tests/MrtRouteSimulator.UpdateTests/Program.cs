using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MrtRouteSimulator.UpdateCore;

int passed = 0;
string root = Path.Combine(Path.GetTempPath(), "MrtUpdateTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (string path in new[] { "../evil.dll", "/evil.dll", "C:/evil.dll", "a\\evil.dll", "x:stream.dll", "a/../evil.dll", "CON.dll", "foo./bar.dll", "foo /bar.dll", "a//b.dll" })
        Test("unsafe path " + path, () => Throws(() => UpdatePathPolicy.NormalizeRelativePath(path)));
    foreach (string path in new[] { "samples/a.mrtsim.json", "settings.json", "README.exe/config.json", "mrt-installed-files.json", "foo/bar.dll", "zh-Hant/settings.json", "zh-Hant/project.mrtsim.json", "zh-Hant/tool.dll", "zh-Hant/nested/a.resources.dll", "ar/a.resources.dll", "ZH-HANT/a.resources.dll" })
        Test("app allowlist " + path, () => Assert(!UpdatePathPolicy.IsAllowedApplicationPath(path)));
    Test("app main and dependencies", () => Assert(UpdatePathPolicy.IsAllowedApplicationPath(UpdateConstants.ApplicationExe) && UpdatePathPolicy.IsAllowedApplicationPath("mcp/tool.dll") && UpdatePathPolicy.IsAllowedApplicationPath("runtimes/win-x64/native/core.dll")));
    foreach (string culture in new[] { "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant" })
        Test("Windows desktop runtime satellites " + culture, () => Assert(UpdatePathPolicy.IsAllowedApplicationPath(culture + "/PresentationFramework.resources.dll")));
    Test("dependency licenses update with application", () => Assert(UpdatePathPolicy.IsAllowedApplicationPath("third-party-licenses/LICENSE.txt") && UpdatePathPolicy.IsAllowedApplicationPath("third-party-licenses/NOTICE.md")));
    foreach (string path in new[] { "third-party-licenses/evil.dll", "third-party-licenses/settings.json", "third-party-licenses/user.mrtsim.json", "third-party-licenses/nested/NOTICE.txt" })
        Test("dependency license restrictions " + path, () => Assert(!UpdatePathPolicy.IsAllowedApplicationPath(path)));
    Test("stable versions", () => { foreach (string v in new[] { "1.0", "1.0.0-beta", "01.2.3", "1.0.0.1", "v1.0.0" }) Throws(() => UpdatePathPolicy.ParseStableVersion(v)); });
    var valid = Fixture(new Dictionary<string, byte[]> { ["小型/a.mrtsim.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":8}"), ["README.md"] = Encoding.UTF8.GetBytes("test") });
    Test("duplicate manifest paths", () => Throws(() => UpdatePathPolicy.ValidateManifest(valid.Offer.Manifest with { Files = [valid.Offer.Manifest.Files[0], valid.Offer.Manifest.Files[0] with { Path = "小型/A.mrtsim.json" }] })));
    Test("data rejects executable", () => Throws(() => UpdatePathPolicy.ValidateManifest(valid.Offer.Manifest with { Files = [valid.Offer.Manifest.Files[0] with { Path = "evil.exe" }] })));
    Test("expanded limit", () => Throws(() => UpdatePathPolicy.ValidateManifest(valid.Offer.Manifest with { Files = [valid.Offer.Manifest.Files[0] with { Size = UpdatePathPolicy.MaximumExpandedBytes + 1 }] })));
    await TestAsync("valid staging and resource activation", async () =>
    {
        using var client = Client(valid.Bytes);
        var stage = await new PackageStager(client).StageAsync(valid.Offer, Path.Combine(root, "valid-cache"));
        Assert(File.Exists(Path.Combine(stage.RootPath, UpdateConstants.ManifestFile)));
        int validated = 0;
        var store = new ResourceStore(Path.Combine(root, "store"), path => { Assert(File.ReadAllText(path).Contains("schemaVersion")); validated++; });
        await store.InstallAsync(stage);
        Assert(validated == 1 && store.CurrentVersion == "1.0.0" && File.Exists(Path.Combine(store.CurrentDirectory!, "README.md")));
        string old = store.CurrentDirectory!;
        var next = stage with { Manifest = stage.Manifest with { Version = "1.0.1" } };
        var failing = new ResourceStore(Path.Combine(root, "store"), _ => throw new InvalidDataException("Engine rejected"));
        await ThrowsAsync(() => failing.InstallAsync(next));
        Assert(store.CurrentVersion == "1.0.0" && Directory.Exists(old) && !Directory.Exists(Path.Combine(root, "store", "versions", "1.0.1")));
        await store.InstallAsync(next);
        Assert(store.CurrentVersion == "1.0.1" && Directory.Exists(old));
        File.WriteAllText(Path.Combine(stage.RootPath, "payload", "README.md"), "tampered");
        Throws(() => PackageStager.ValidateStaged(stage.RootPath, stage.Manifest));
    });
    await RejectZip("ZipSlip", new() { ["../outside.mrtsim.json"] = [1] }, valid.Offer.Manifest.Files);
    await RejectZip("ADS", new() { ["a:evil.mrtsim.json"] = [1] }, valid.Offer.Manifest.Files);
    await RejectZip("extra file", new() { ["小型/a.mrtsim.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":8}"), ["README.md"] = Encoding.UTF8.GetBytes("test"), ["extra.mrtsim.json"] = [2] }, valid.Offer.Manifest.Files);
    await RejectZip("missing file", new() { ["README.md"] = Encoding.UTF8.GetBytes("test") }, valid.Offer.Manifest.Files);
    await RejectZip("file hash", new() { ["小型/a.mrtsim.json"] = Encoding.UTF8.GetBytes("{\"schemaVersion\":9}"), ["README.md"] = Encoding.UTF8.GetBytes("test") }, valid.Offer.Manifest.Files);
    await RejectZip("case duplicate", new() { ["README.md"] = Encoding.UTF8.GetBytes("test"), ["readme.md"] = Encoding.UTF8.GetBytes("test") }, [new("README.md", Hash(Encoding.UTF8.GetBytes("test")), 4)]);
    await TestAsync("symbolic link entry", async () =>
    {
        using var mem = new MemoryStream();
        using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, true)) { var item = zip.CreateEntry("README.md"); item.ExternalAttributes = unchecked((int)0xA1FF0000); using var stream = item.Open(); stream.Write(Encoding.UTF8.GetBytes("test")); }
        await RejectBytes("symlink", mem.ToArray(), [new("README.md", Hash(Encoding.UTF8.GetBytes("test")), 4)]);
    });
    await TestAsync("download hash and cleanup", async () =>
    {
        using var client = Client(valid.Bytes.Select(x => (byte)(x ^ 1)).ToArray());
        string cache = Path.Combine(root, "bad-hash");
        await ThrowsAsync(() => new PackageStager(client).StageAsync(valid.Offer, cache));
        Assert(!Directory.EnumerateFileSystemEntries(cache).Any());
    });
    await TestAsync("cancellation cleanup", async () =>
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var client = Client(valid.Bytes);
        string cache = Path.Combine(root, "cancel-cache");
        await ThrowsAsync(() => new PackageStager(client).StageAsync(valid.Offer, cache, cancellationToken: cancellation.Token));
        Assert(!Directory.Exists(cache) || !Directory.EnumerateFileSystemEntries(cache).Any());
    });
    await TestAsync("wrong repository forbidden before HTTP", async () =>
    {
        using var client = new HttpClient(new Handler(_ => throw new Exception("Must not request")));
        var wrong = valid.Offer with { Package = valid.Offer.Package with { DownloadUri = new Uri("https://github.com/attacker/project/releases/download/data-v1.0.0/data.zip") } };
        await ThrowsAsync(() => new PackageStager(client).StageAsync(wrong, Path.Combine(root, "wrong")));
    });
    await TestAsync("HTTP transport selects version and verifies manifest digest", async () =>
    {
        using var client = ReleaseClient(valid, "data-v1.0.0");
        var result = await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(4, 1, 1), 8);
        Assert(result.Offer?.Manifest.Version == "1.0.0");
    });
    await TestAsync("missing published manifest is informative", async () =>
    {
        using var client = ReleaseClient(valid, "data-v1.0.0", missingManifest: true);
        var result = await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(4, 1, 1), 8);
        Assert(result.Offer is null && result.Message.Contains("清單"));
    });
    await TestAsync("missing GitHub digest rejected", async () =>
    {
        using var client = ReleaseClient(valid, "data-v1.0.0", missingDigest: true);
        await ThrowsAsync(() => new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(4, 1, 1), 8));
    });
    await TestAsync("schema and minimum app compatibility", async () =>
    {
        using var client = ReleaseClient(valid, "data-v1.0.0");
        Assert((await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(4, 1, 1), 9)).Offer is null);
        Assert((await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(0, 0, 1), 8)).Offer is null);
    });
    await TestAsync("current version no download", async () =>
    {
        using var client = ReleaseClient(valid, "data-v1.0.0");
        Assert((await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(1, 0, 0), new Version(4, 1, 1), 8)).Offer is null);
    });
    await TestAsync("minimum app version exact match", async () =>
    {
        using var client = ReleaseClient(valid, "data-v1.0.0");
        Assert((await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(1, 0, 0), 8)).Offer is not null);
    });
    await TestAsync("ZIP bomb ratio", async () =>
    {
        byte[] data = new byte[3 * 1024 * 1024];
        var fixture = Fixture(new() { ["bomb.mrtsim.json"] = data });
        await RejectBytes("bomb", fixture.Bytes, fixture.Offer.Manifest.Files);
    });
    await TestAsync("HTTP exceeds expected download length", async () =>
    {
        using var client = Client([.. valid.Bytes, 1]);
        string cache = Path.Combine(root, "oversize-http");
        await ThrowsAsync(() => new PackageStager(client).StageAsync(valid.Offer, cache));
        Assert(!Directory.EnumerateFileSystemEntries(cache).Any());
    });
    await TestAsync("truncated download", async () =>
    {
        using var client = Client(valid.Bytes[..^1]);
        await ThrowsAsync(() => new PackageStager(client).StageAsync(valid.Offer, Path.Combine(root, "truncated")));
    });
    await TestAsync("manifest GitHub digest mismatch", async () =>
    {
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(valid.Offer.Manifest, UpdateJson.Options);
        byte[] release = JsonSerializer.SerializeToUtf8Bytes(new[] { new { draft = false, prerelease = false, tag_name = "data-v1.0.0", assets = new[] { new { name = "mrt-data-update.json", browser_download_url = $"https://github.com/{UpdateConstants.Repository}/releases/download/data-v1.0.0/mrt-data-update.json", size = manifest.Length, digest = "sha256:" + new string('0', 64) } } } });
        using var client = new HttpClient(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.Host == "api.github.com" ? release : manifest) }));
        await ThrowsAsync(() => new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(4, 1, 1), 8));
    });
    await TestAsync("data store rejects downgrade and callback mutation", async () =>
    {
        using var client = Client(valid.Bytes);
        var stage = await new PackageStager(client).StageAsync(valid.Offer, Path.Combine(root, "store-regression-cache"));
        string storeRoot = Path.Combine(root, "store-regression");
        var store = new ResourceStore(storeRoot, _ => { });
        await store.InstallAsync(stage);
        await ThrowsAsync(() => store.InstallAsync(stage with { Manifest = stage.Manifest with { Version = "0.9.0" } }));
        var mutating = new ResourceStore(storeRoot, path => File.AppendAllText(path, "tamper"));
        await ThrowsAsync(() => mutating.InstallAsync(stage with { Manifest = stage.Manifest with { Version = "1.1.0" } }));
        Assert(store.CurrentVersion == "1.0.0");
    });
    await TestAsync("release pagination finds older data channel", async () =>
    {
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(valid.Offer.Manifest, UpdateJson.Options);
        byte[] firstPage = JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, 100).Select(i => new { draft = false, prerelease = false, tag_name = $"v10.0.{i}", assets = Array.Empty<object>() }));
        object Asset(string name, byte[] bytes) => new { name, browser_download_url = $"https://github.com/{UpdateConstants.Repository}/releases/download/data-v1.0.0/{name}", size = bytes.Length, digest = "sha256:" + Hash(bytes) };
        byte[] secondPage = JsonSerializer.SerializeToUtf8Bytes(new[] { new { draft = false, prerelease = false, tag_name = "data-v1.0.0", assets = new[] { Asset("mrt-data-update.json", manifest), Asset("data.zip", valid.Bytes) } } });
        int pages = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            bool api = request.RequestUri!.Host == "api.github.com";
            if (api) pages++;
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(api ? request.RequestUri.Query.Contains("page=2") ? secondPage : firstPage : manifest) };
        }));
        Assert((await new UpdateRepositoryClient(client).CheckAsync(UpdateKind.Data, new Version(0, 0, 0), new Version(4, 1, 1), 8)).Offer is not null && pages == 2);
    });
    Console.WriteLine($"PASS {passed} update transport/resource tests");
}
finally { Directory.Delete(root, true); }

void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
async Task TestAsync(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Throws(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected InvalidDataException"); }
static async Task ThrowsAsync(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is InvalidDataException or OperationCanceledException) { return; } throw new Exception("Expected validation/cancellation failure"); }
static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
static HttpClient Client(byte[] bytes) => new(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
static (UpdateOffer Offer, byte[] Bytes) Fixture(Dictionary<string, byte[]> files)
{
    using var mem = new MemoryStream();
    using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, true))
        foreach (var file in files) { var entry = zip.CreateEntry(file.Key); using var output = entry.Open(); output.Write(file.Value); }
    byte[] bytes = mem.ToArray();
    var manifest = new UpdateManifest(1, UpdateConstants.Product, UpdateKind.Data, "1.0.0", "1.0.0", 8, "data.zip", Hash(bytes), bytes.Length, files.Select(x => new UpdateFile(x.Key, Hash(x.Value), x.Value.Length)).ToArray());
    return (new(manifest, new("data.zip", new Uri($"https://github.com/{UpdateConstants.Repository}/releases/download/data-v1.0.0/data.zip"), bytes.Length, "sha256:" + Hash(bytes)), "", new Uri($"https://github.com/{UpdateConstants.Repository}/releases/tag/data-v1.0.0")), bytes);
}
async Task RejectZip(string name, Dictionary<string, byte[]> entries, UpdateFile[] files)
{
    var fixture = Fixture(entries);
    await TestAsync(name, () => RejectBytes(name, fixture.Bytes, files));
}
async Task RejectBytes(string name, byte[] bytes, UpdateFile[] files)
{
    var template = Fixture(new() { ["README.md"] = [1] });
    var offer = template.Offer with { Manifest = template.Offer.Manifest with { Files = files, PackageSize = bytes.Length, PackageSha256 = Hash(bytes) }, Package = template.Offer.Package with { Size = bytes.Length, Digest = "sha256:" + Hash(bytes) } };
    using var client = Client(bytes);
    string cache = Path.Combine(root, "reject-" + name);
    await ThrowsAsync(() => new PackageStager(client).StageAsync(offer, cache));
    Assert(!Directory.Exists(cache) || !Directory.EnumerateFileSystemEntries(cache).Any());
}
static HttpClient ReleaseClient((UpdateOffer Offer, byte[] Bytes) fixture, string tag, bool missingManifest = false, bool missingDigest = false)
{
    byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(fixture.Offer.Manifest, UpdateJson.Options);
    object Asset(string name, byte[] bytes) => new { name, browser_download_url = $"https://github.com/{UpdateConstants.Repository}/releases/download/{tag}/{name}", size = bytes.Length, digest = missingDigest ? null : "sha256:" + Hash(bytes) };
    object[] assets = missingManifest ? [Asset("data.zip", fixture.Bytes)] : [Asset("data.zip", fixture.Bytes), Asset("mrt-data-update.json", manifest)];
    byte[] release = JsonSerializer.SerializeToUtf8Bytes(new[] { new { draft = false, prerelease = false, tag_name = tag, body = "Test release", assets } });
    return new(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.Host == "api.github.com" ? release : manifest) }));
}
sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
