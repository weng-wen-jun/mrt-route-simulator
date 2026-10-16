using System.Diagnostics;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MrtRouteSimulator.UpdateCore;

var tests = new (string Name, Action Run)[]
{
    ("clean receipt-scoped replacement keeps unknown data and removes only owned obsolete files", () =>
    {
        using var f = new Fixture();
        f.PutInstall("personal.mrtsim.json", "user project"); f.PutInstall("samples/user.txt", "user sample");
        f.PutInstall("MrtRouteSimulator.displaysettings.json", "preferences");
        SoftwareUpdateResult result = f.Apply();
        Check(result.Success, result.Message);
        Check(f.ReadInstall(UpdateConstants.ApplicationExe) == "new-app", "main binary");
        Check(!File.Exists(f.Installed("old.dll")), "owned obsolete file removed");
        Check(f.ReadInstall("personal.mrtsim.json") == "user project" && f.ReadInstall("samples/user.txt") == "user sample", "user data preserved");
        Check(f.ReadInstall("MrtRouteSimulator.displaysettings.json") == "preferences", "display settings preserved");
        InstalledFiles receipt = JsonSerializer.Deserialize<InstalledFiles>(File.ReadAllText(f.Installed(UpdateConstants.InstallationReceipt)), UpdateJson.Options)!;
        Check(receipt.Version == "4.2.0" && receipt.Files.Contains("new.dll") && !receipt.Files.Contains("old.dll"), "receipt ownership updated");
        Check(File.Exists(Path.Combine(result.BackupDirectory!, "files", "old.dll")), "obsolete file backed up");
    }),
    ("unknown colliding binary blocks all changes", () =>
    {
        using var f = new Fixture(); f.PutInstall("new.dll", "unknown ownership");
        Check(!f.Apply().Success, "reject unknown same-name file"); f.AssertOriginal();
        Check(f.ReadInstall("new.dll") == "unknown ownership", "unknown file preserved");
    }),
    ("legacy installation without receipt requires manual migration", () =>
    {
        using var f = new Fixture(); File.Delete(f.Installed(UpdateConstants.InstallationReceipt));
        var r = f.Apply(); Check(!r.Success && r.Message.Contains("手動"), r.Message);
        Check(f.ReadInstall(UpdateConstants.ApplicationExe) == "old-app", "legacy file preserved");
    }),
    ("staged hash changed before apply blocks replacement", () =>
    {
        using var f = new Fixture(); File.WriteAllText(f.Payload("new.dll"), "tampered");
        Check(!f.Apply().Success, "reject hash mismatch"); f.AssertOriginal();
    }),
    ("staged file changed between writes rolls back installed content", () =>
    {
        using var f = new Fixture();
        var r = f.Apply(path => { if (path == UpdateConstants.ApplicationExe) File.WriteAllText(f.Payload("new.dll"), "tampered during apply"); });
        Check(!r.Success && r.Message.Contains("已復原"), r.Message); f.AssertOriginal();
        Check(!File.Exists(f.Installed("new.dll")), "new files rolled back");
    }),
    ("failure after each mutation restores receipt and obsolete files", () =>
    {
        foreach (string failurePath in new[] { UpdateConstants.ApplicationExe, "new.dll", "old.dll", UpdateConstants.InstallationReceipt })
        {
            using var f = new Fixture();
            var r = f.Apply(path => { if (path == failurePath) throw new IOException("injected failure"); });
            Check(!r.Success && r.Message.Contains("已復原"), r.Message); f.AssertOriginal();
            Check(!File.Exists(f.Installed("new.dll")), "new file removed on rollback");
        }
    }),
    ("exclusive update lock blocks a concurrent transaction", () =>
    {
        using var f = new Fixture();
        using var locked = new FileStream(f.Installed("mrt-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Check(!f.Apply().Success, "concurrent apply denied"); f.AssertOriginal();
    }),
    ("locked application file fails without elevation or closing the locker", () =>
    {
        using var f = new Fixture();
        using var locked = new FileStream(f.Installed(UpdateConstants.ApplicationExe), FileMode.Open, FileAccess.Read, FileShare.Read);
        var r = f.Apply(); Check(!r.Success && r.Message.Contains("已復原"), r.Message); f.AssertOriginal();
        Check(locked.CanRead, "other process/file handle kept alive");
    }),
    ("manifest rejects traversal, user data, wrong kind, downgrade and unsupported minimum", () =>
    {
        foreach (Func<UpdateManifest, UpdateManifest> mutate in new Func<UpdateManifest, UpdateManifest>[]
        {
            m => m with { Files = [m.Files[0], m.Files[1] with { Path = "../outside.dll" }] },
            m => m with { Files = [m.Files[0], m.Files[1] with { Path = "personal.mrtsim.json" }] },
            m => m with { Kind = UpdateKind.Data }, m => m with { Product = "AnotherProduct" },
            m => m with { Version = "4.0.0" }, m => m with { MinimumAppVersion = "9.0.0" }
        })
        {
            using var f = new Fixture(); f.WriteManifest(mutate(f.Manifest));
            Check(!f.Apply().Success, "invalid manifest rejected"); f.AssertOriginal();
            Check(!File.Exists(Path.Combine(f.Root, "outside.dll")), "no out-of-root write");
        }
    }),
    ("installation may not be drive root, repository, stage or backup directory", () =>
    {
        using var f = new Fixture();
        Check(!SoftwareUpdateInstaller.Apply(f.Job with { InstallDirectory = Path.GetPathRoot(f.Root)! }, f.Backups).Success, "drive root denied");
        Directory.CreateDirectory(f.Installed(".git")); Check(!f.Apply().Success, "git checkout denied");
        Directory.Delete(f.Installed(".git"));
        Check(!SoftwareUpdateInstaller.Apply(f.Job with { StageDirectory = f.Install }, f.Backups).Success, "overlap denied");
        Check(!SoftwareUpdateInstaller.Apply(f.Job, f.Installed("backups")).Success, "backup under install denied");
        f.AssertOriginal();
    }),
    ("unlisted staged file and malicious receipt path are rejected", () =>
    {
        using var f = new Fixture(); File.WriteAllText(f.Payload("extra.dll"), "not in manifest");
        Check(!f.Apply().Success, "extra staged file denied"); f.AssertOriginal();
        File.Delete(f.Payload("extra.dll"));
        f.WriteReceipt(new InstalledFiles(1, UpdateConstants.Product, "4.1.1", [UpdateConstants.ApplicationExe, "../private.dll"]));
        Check(!f.Apply().Success, "receipt escape denied");
        Check(f.ReadInstall(UpdateConstants.ApplicationExe) == "old-app", "main unchanged");
    }),
    ("recovery entry restores interrupted journal and refuses subsequent edits", () =>
    {
        using var f = new Fixture(); var r = f.Apply(); Check(r.Success, r.Message);
        string journal = Path.Combine(r.BackupDirectory!, "journal.json");
        string completed = File.ReadAllText(journal); File.WriteAllText(journal, completed.Replace("\"complete\"", "\"prepared\"", StringComparison.Ordinal));
        f.PutInstall("mrt-update.pending.json", JsonSerializer.Serialize(new { backupDirectory = r.BackupDirectory }, UpdateJson.Options));
        Check(!f.Apply().Success, "pending transaction blocks a second update");
        f.PutInstall("new.dll", "later user edit");
        Check(!SoftwareUpdateInstaller.Recover(r.BackupDirectory!).Success, "later edit preserved");
        Check(f.ReadInstall("new.dll") == "later user edit", "recovery leaves later edit");
        f.PutInstall("new.dll", "new-library");
        var recovered = SoftwareUpdateInstaller.Recover(r.BackupDirectory!); Check(recovered.Success, recovered.Message);
        f.AssertOriginal(); Check(!File.Exists(f.Installed("new.dll")), "new file removed in recovery");
        Check(!File.Exists(f.Installed("mrt-update.pending.json")), "pending transaction cleared after recovery");
    }),
    ("live parent identity is checked and helper emits a persisted result", () =>
    {
        using var f = new Fixture();
        CheckThrows(() => UpdaterHost.WaitForParentAsync(f.Job with { ParentProcessId = Environment.ProcessId, ParentStartTimeUtcTicks = 1 }, TimeSpan.FromMilliseconds(1)).GetAwaiter().GetResult());
        string jobFile = Path.Combine(f.Root, "job.json");
        File.WriteAllText(jobFile, JsonSerializer.Serialize(f.Job with { ParentProcessId = Environment.ProcessId }, UpdateJson.Options));
        int code = UpdaterHost.RunAsync(["--job", jobFile]).GetAwaiter().GetResult();
        Check(code == 1 && File.Exists(Path.Combine(f.Root, "update-result.json")), "helper persisted failure");
        f.AssertOriginal();
    }),
    ("reparse point in installation or staging blocks changes", () =>
    {
        using var f = new Fixture();
        string outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        string junction = f.Installed("linked");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(junction); start.ArgumentList.Add(outside);
        using var process = Process.Start(start)!; process.WaitForExit();
        Check(process.ExitCode == 0, "create isolated junction: " + process.StandardError.ReadToEnd());
        try { Check(!f.Apply().Success, "reparse tree denied"); f.AssertOriginal(); }
        finally { Directory.Delete(junction); }
        Check(File.ReadAllText(Path.Combine(outside, "keep.txt")) == "keep", "junction target preserved");
    }),
    ("parent executable, start ticks, bounded wait and normal exit are verified", () =>
    {
        using var f = new Fixture();
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), f.Installed(UpdateConstants.ApplicationExe), true);
        var start = new ProcessStartInfo(f.Installed(UpdateConstants.ApplicationExe)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/d"); start.ArgumentList.Add("/q"); start.ArgumentList.Add("/k");
        using var parent = Process.Start(start)!;
        try
        {
            var job = f.Job with { ParentProcessId = parent.Id, ParentStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks };
            CheckThrows(() => UpdaterHost.WaitForParentAsync(job with { ParentStartTimeUtcTicks = 1 }, TimeSpan.FromMilliseconds(30)).GetAwaiter().GetResult());
            CheckThrows(() => UpdaterHost.WaitForParentAsync(job with { InstallDirectory = f.Stage }, TimeSpan.FromMilliseconds(30)).GetAwaiter().GetResult());
            bool timedOut = false;
            try { UpdaterHost.WaitForParentAsync(job, TimeSpan.FromMilliseconds(30)).GetAwaiter().GetResult(); }
            catch (TimeoutException) { timedOut = true; }
            Check(timedOut && !parent.HasExited, "timeout never kills the parent");
            Task wait = UpdaterHost.WaitForParentAsync(job, TimeSpan.FromSeconds(5));
            parent.StandardInput.WriteLine("exit"); parent.StandardInput.Flush();
            wait.GetAwaiter().GetResult(); Check(parent.HasExited, "normal parent exit observed");
        }
        finally
        {
            if (!parent.HasExited) { parent.StandardInput.WriteLine("exit"); parent.StandardInput.Flush(); parent.WaitForExit(5000); }
        }
    }),
    ("helper entry applies a complete isolated job and persists success", () =>
    {
        using var f = new Fixture();
        string jobFile = Path.Combine(f.Root, "job.json");
        File.WriteAllText(jobFile, JsonSerializer.Serialize(f.Job, UpdateJson.Options));
        int code = UpdaterHost.RunAsync(["--job", jobFile]).GetAwaiter().GetResult();
        var result = JsonSerializer.Deserialize<SoftwareUpdateResult>(File.ReadAllText(Path.Combine(f.Root, "update-result.json")), UpdateJson.Options)!;
        Check(code == 0 && result.Success, result.Message);
        Check(f.ReadInstall(UpdateConstants.ApplicationExe) == "new-app", "helper applied staged binary");
        Check(File.ReadAllText(Path.Combine(f.Root, "update.log")).Contains("4.2.0"), "helper logged result");
    }),
    ("copied bytes are verified before replacing an installed file", () =>
    {
        using var f = new Fixture();
        UpdateFile trusted = f.Manifest.Files.Single(file => file.Path == UpdateConstants.ApplicationExe);
        // Model a source change after Apply's preliminary ValidateFile has completed.
        File.WriteAllText(f.Payload(UpdateConstants.ApplicationExe), "tampered-copy");
        MethodInfo copy = typeof(SoftwareUpdateInstaller).GetMethod("AtomicCopy", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool rejected = false;
        try { copy.Invoke(null, [f.Payload(UpdateConstants.ApplicationExe), f.Installed(UpdateConstants.ApplicationExe), trusted]); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { rejected = true; }
        Check(rejected, "untrusted copied bytes rejected");
        f.AssertOriginal();
        Check(!Directory.EnumerateFiles(f.Install, "*.mrt-update-*.tmp", SearchOption.AllDirectories).Any(), "failed copy temp cleaned");
        // A later Apply failure still rolls back a previously replaced valid binary.
        File.WriteAllText(f.Payload(UpdateConstants.ApplicationExe), "new-app");
        var r = f.Apply(path => { if (path == UpdateConstants.ApplicationExe) File.WriteAllText(f.Payload("new.dll"), "tampered-later-copy"); });
        Check(!r.Success && r.Message.Contains("已復原"), r.Message); f.AssertOriginal();
    })
};

int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} updater tests passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void CheckThrows(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected InvalidDataException"); }

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "MrtUpdaterTests", Guid.NewGuid().ToString("N"));
    public string Install => Path.Combine(Root, "installation");
    public string Stage => Path.Combine(Root, "stage");
    public string Backups => Path.Combine(Root, "backups");
    public UpdateManifest Manifest { get; }
    public SoftwareUpdateJob Job => new(Stage, Install, UpdateConstants.ApplicationExe, int.MaxValue, 1, false);
    private readonly byte[] originalReceipt;
    public Fixture()
    {
        Directory.CreateDirectory(Install); Directory.CreateDirectory(Path.Combine(Stage, "payload"));
        PutInstall(UpdateConstants.ApplicationExe, "old-app"); PutInstall("old.dll", "old-library");
        WriteReceipt(new InstalledFiles(1, UpdateConstants.Product, "4.1.1", [UpdateConstants.ApplicationExe, "old.dll"]));
        originalReceipt = File.ReadAllBytes(Installed(UpdateConstants.InstallationReceipt));
        File.WriteAllText(Payload(UpdateConstants.ApplicationExe), "new-app"); File.WriteAllText(Payload("new.dll"), "new-library");
        UpdateFile[] files = new[] { UpdateConstants.ApplicationExe, "new.dll" }.Select(path => new UpdateFile(path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Payload(path)))), new FileInfo(Payload(path)).Length)).ToArray();
        Manifest = new(1, UpdateConstants.Product, UpdateKind.Application, "4.2.0", "4.1.0", 8, "application.zip", new string('a', 64), 128, files);
        WriteManifest(Manifest);
    }
    public string Installed(string path) => Path.Combine(Install, path.Replace('/', Path.DirectorySeparatorChar));
    public string Payload(string path) => Path.Combine(Stage, "payload", path.Replace('/', Path.DirectorySeparatorChar));
    public void PutInstall(string path, string value) { string destination = Installed(path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.WriteAllText(destination, value); }
    public string ReadInstall(string path) => File.ReadAllText(Installed(path));
    public void WriteManifest(UpdateManifest manifest) => File.WriteAllText(Path.Combine(Stage, UpdateConstants.ManifestFile), JsonSerializer.Serialize(manifest, UpdateJson.Options));
    public void WriteReceipt(InstalledFiles receipt) => File.WriteAllText(Installed(UpdateConstants.InstallationReceipt), JsonSerializer.Serialize(receipt, UpdateJson.Options));
    public SoftwareUpdateResult Apply(Action<string>? afterWrite = null) => SoftwareUpdateInstaller.Apply(Job, Backups, afterWrite);
    public void AssertOriginal()
    {
        if (ReadInstall(UpdateConstants.ApplicationExe) != "old-app" || ReadInstall("old.dll") != "old-library" || !File.ReadAllBytes(Installed(UpdateConstants.InstallationReceipt)).SequenceEqual(originalReceipt))
            throw new Exception("Original application/obsolete file/receipt were not preserved or restored exactly.");
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
