using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;
using MrtRouteSimulator.UpdateCore;

namespace MrtRouteSimulator.App;

public sealed record UpdateLibraryEntry(string Name, string Path);
public sealed class AppUpdateService : IDisposable
{
    private readonly HttpClient client;
    private readonly ResourceStore store;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public string StateRoot { get; }
    public string InstallDirectory { get; }
    public Version AppVersion { get; }
    public string DataVersion => store.CurrentVersion ?? "0.0.0";
    public string DataDirectory => store.CurrentDirectory ?? Path.Combine(InstallDirectory, "samples");
    public bool CheckAtStartup { get; private set; }
    public AppUpdateService(HttpClient? client = null, string? stateRoot = null, string? installDirectory = null)
    {
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        StateRoot = stateRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MrtRouteSimulator", "Updates");
        InstallDirectory = installDirectory ?? AppContext.BaseDirectory;
        var v = typeof(MainWindow).Assembly.GetName().Version!;
        AppVersion = new Version(v.Major, v.Minor, v.Build);
        store = new ResourceStore(Path.Combine(StateRoot, "Data"), path =>
        {
            var doc = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
            _ = TopologyProjectFormat.CreateRuntime(doc);
        });
        try { if (File.Exists(SettingsPath)) CheckAtStartup = JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(SettingsPath), JsonOptions)?.CheckAtStartup == true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { CheckAtStartup = false; }
    }
    private string SettingsPath => Path.Combine(StateRoot, "preferences.json");
    public void SetCheckAtStartup(bool enabled)
    {
        Directory.CreateDirectory(StateRoot);
        var temp = SettingsPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(new UpdatePreferences(enabled), JsonOptions));
        File.Move(temp, SettingsPath, true); CheckAtStartup = enabled;
    }
    public Task<UpdateCheckResult> CheckAsync(UpdateKind kind, CancellationToken cancellationToken) =>
        new UpdateRepositoryClient(client).CheckAsync(kind, kind == UpdateKind.Application ? AppVersion : Version.Parse(DataVersion), AppVersion, TopologyProjectFormat.CurrentSchemaVersion, cancellationToken);
    public Task<StagedUpdate> DownloadAsync(UpdateOffer offer, IProgress<UpdateProgress> progress, CancellationToken token) =>
        new PackageStager(client).StageAsync(offer, Path.Combine(StateRoot, "Downloads"), progress, token);
    public Task InstallDataAsync(StagedUpdate staged, CancellationToken token) => store.InstallAsync(staged, token);
    public IReadOnlyList<UpdateLibraryEntry> Library() => Directory.Exists(DataDirectory)
        ? Directory.GetFiles(DataDirectory, "*.mrtsim.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => new UpdateLibraryEntry(Path.GetRelativePath(DataDirectory, p), p)).ToArray()
        : [];
    public string? SoftwareUpdateUnavailableReason
    {
        get
        {
            if (!File.Exists(Path.Combine(InstallDirectory, UpdateConstants.InstallationReceipt)) || !File.Exists(Path.Combine(InstallDirectory, "updater", "MrtRouteSimulator.Updater.dll")))
                return "此建置尚無完整安裝索引；程式更新請先換用可更新的完整套件。";
            for (DirectoryInfo? directory = new(Path.GetFullPath(InstallDirectory)); directory is not null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                    return "此建置位於原始碼工作目錄；請將完整 portable 資料夾複製到專案外，再使用程式更新。";
            try { UpdatePathPolicy.RejectReparsePoints(InstallDirectory); }
            catch (InvalidDataException) { return "安裝位置含連結；請將完整套件放在一般資料夾再更新。"; }
            return null;
        }
    }
    public bool CanInstallSoftware => SoftwareUpdateUnavailableReason is null;
    public string PrepareSoftwareJob(StagedUpdate staged)
    {
        if (SoftwareUpdateUnavailableReason is string reason) throw new InvalidOperationException(reason);
        PackageStager.ValidateStaged(staged.RootPath, staged.Manifest);
        if (staged.Manifest.Kind != UpdateKind.Application) throw new InvalidOperationException("套件不是軟體更新。");
        UpdatePathPolicy.RejectReparsePoints(Path.Combine(InstallDirectory, "updater"));
        var folder = Path.Combine(StateRoot, "Jobs", Guid.NewGuid().ToString("N"));
        var runner = Path.Combine(folder, "runner"); Directory.CreateDirectory(runner);
        foreach (var file in Directory.GetFiles(Path.Combine(InstallDirectory, "updater")))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("更新器不可使用連結檔案。");
            File.Copy(file, Path.Combine(runner, Path.GetFileName(file)));
        }
        using var process = Process.GetCurrentProcess();
        var job = new SoftwareUpdateJob(staged.RootPath, Path.GetFullPath(InstallDirectory), UpdateConstants.ApplicationExe, process.Id, process.StartTime.ToUniversalTime().Ticks, true);
        var path = Path.Combine(folder, "job.json"); File.WriteAllText(path, JsonSerializer.Serialize(job, JsonOptions));
        return path;
    }
    public void StartSoftwareJob(string jobPath)
    {
        var runner = Path.Combine(Path.GetDirectoryName(jobPath)!, "runner");
        var exe = Path.Combine(runner, "MrtRouteSimulator.Updater.exe");
        var info = new ProcessStartInfo { FileName = File.Exists(exe) ? exe : "dotnet", WorkingDirectory = runner, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (!File.Exists(exe)) info.ArgumentList.Add(Path.Combine(runner, "MrtRouteSimulator.Updater.dll"));
        info.ArgumentList.Add("--job"); info.ArgumentList.Add(jobPath);
        _ = Process.Start(info) ?? throw new InvalidOperationException("無法啟動更新器。");
    }
    public string? LastResult()
    {
        var jobs = Path.Combine(StateRoot, "Jobs"); if (!Directory.Exists(jobs)) return null;
        var result = Directory.GetFiles(jobs, "update-result.json", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (result is null) return null;
        try { var value = JsonSerializer.Deserialize<SoftwareUpdateResult>(File.ReadAllText(result), JsonOptions); return value is null ? null : (value.Success ? "上次更新完成：" : "上次更新未完成：") + value.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return "上次更新結果無法讀取。"; }
    }
    public void Dispose() => client.Dispose();
    private sealed record UpdatePreferences(bool CheckAtStartup);
}
