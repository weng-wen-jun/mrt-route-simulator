using System.Text.Json;

namespace MrtRouteSimulator.UpdateCore;

/// <summary>Versioned managed resources, separate from user project files.</summary>
public sealed class ResourceStore(string root, Action<string> validateProject)
{
    private string Root => Path.GetFullPath(root);
    public string? CurrentVersion
    {
        get
        {
            UpdatePathPolicy.RejectReparsePoints(Root);
            string active = Path.Combine(Root, "active.json");
            if (!File.Exists(active)) return null;
            UpdatePathPolicy.RejectReparsePoints(active);
            if (new FileInfo(active).Length > 1024) throw new InvalidDataException("資料版本指標過大。");
            ActiveVersion state = JsonSerializer.Deserialize<ActiveVersion>(File.ReadAllText(active), UpdateJson.Options) ?? throw new InvalidDataException("資料版本指標損毀。");
            _ = UpdatePathPolicy.ParseStableVersion(state.Version);
            string directory = Path.Combine(Root, "versions", state.Version, "payload");
            UpdatePathPolicy.RejectReparsePoints(directory);
            if (!Directory.Exists(directory)) throw new InvalidDataException("目前資料版本目錄遺失。");
            return state.Version;
        }
    }
    public string? CurrentDirectory => CurrentVersion is string version ? Path.Combine(Root, "versions", version, "payload") : null;
    public void Install(StagedUpdate staged) => InstallAsync(staged).GetAwaiter().GetResult();

    public async Task InstallAsync(StagedUpdate staged, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validateProject);
        if (staged.Manifest.Kind != UpdateKind.Data) throw new InvalidDataException("資料版本庫只接受資料更新。");
        PackageStager.ValidateStaged(staged.RootPath, staged.Manifest);
        UpdatePathPolicy.RejectReparsePoints(Root);
        Directory.CreateDirectory(Root);
        string lockPath = Path.Combine(Root, ".install.lock");
        UpdatePathPolicy.RejectReparsePoints(lockPath);
        await using var updateLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (CurrentVersion is string current && UpdatePathPolicy.ParseStableVersion(staged.Manifest.Version) <= UpdatePathPolicy.ParseStableVersion(current))
            throw new InvalidDataException("資料更新版本必須高於目前版本。");
        string versions = Path.Combine(Root, "versions");
        UpdatePathPolicy.RejectReparsePoints(versions);
        Directory.CreateDirectory(versions);
        string target = Path.Combine(versions, staged.Manifest.Version);
        if (Directory.Exists(target)) throw new InvalidDataException("此資料版本已存在，未覆寫既有版本。");
        string incoming = Path.Combine(versions, ".incoming-" + Guid.NewGuid().ToString("N"));
        string active = Path.Combine(Root, "active.json");
        string pointer = Path.Combine(Root, ".active-" + Guid.NewGuid().ToString("N") + ".tmp");
        bool moved = false;
        try
        {
            Directory.CreateDirectory(Path.Combine(incoming, "payload"));
            foreach (UpdateFile file in staged.Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string source = UpdatePathPolicy.GetContainedPath(Path.Combine(staged.RootPath, "payload"), file.Path);
                UpdatePathPolicy.RejectReparsePoints(source);
                string destination = UpdatePathPolicy.GetContainedPath(Path.Combine(incoming, "payload"), file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken);
            }
            PackageStager.ValidateStaged(incoming, staged.Manifest);
            foreach (UpdateFile file in staged.Manifest.Files.Where(x => x.Path.EndsWith(".mrtsim.json", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                validateProject(UpdatePathPolicy.GetContainedPath(Path.Combine(incoming, "payload"), file.Path));
            }
            PackageStager.ValidateStaged(incoming, staged.Manifest);
            await File.WriteAllTextAsync(Path.Combine(incoming, UpdateConstants.ManifestFile), JsonSerializer.Serialize(staged.Manifest, UpdateJson.Options), cancellationToken);
            await File.WriteAllTextAsync(pointer, JsonSerializer.Serialize(new ActiveVersion(staged.Manifest.Version), UpdateJson.Options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(incoming, target);
            moved = true;
            UpdatePathPolicy.RejectReparsePoints(active);
            if (File.Exists(active)) File.Replace(pointer, active, null); else File.Move(pointer, active);
        }
        catch
        {
            if (Directory.Exists(incoming)) Directory.Delete(incoming, true);
            if (moved && Directory.Exists(target)) Directory.Delete(target, true);
            throw;
        }
        finally { if (File.Exists(pointer)) File.Delete(pointer); }
    }
    private sealed record ActiveVersion(string Version);
}
