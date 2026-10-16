using System.Security.Cryptography;
using System.Text.Json;

namespace MrtRouteSimulator.UpdateCore;

/// <summary>Receipt-scoped replacement of an application that has already exited.</summary>
public static class SoftwareUpdateInstaller
{
    private static JsonSerializerOptions Json => UpdateJson.Options;

    private sealed record JournalEntry(string Path, bool Existed, string? BackupSha256, string? NewSha256);
    private sealed record Journal(int FormatVersion, string InstallDirectory, string State, JournalEntry[] Entries);
    private sealed record PendingUpdate(string BackupDirectory);
    private const string PendingFile = "mrt-update.pending.json";

    public static SoftwareUpdateResult Apply(SoftwareUpdateJob job, string backupRoot, Action<string>? afterWrite = null)
    {
        string? backup = null;
        Journal? journal = null;
        FileStream? updateLock = null;
        try
        {
            if (!string.Equals(job.ExeName, UpdateConstants.ApplicationExe, StringComparison.Ordinal))
                throw new InvalidDataException("更新目標執行檔名稱不正確。");
            string install = ValidateInstallDirectory(job.InstallDirectory);
            string stage = ValidateDirectory(job.StageDirectory);
            string backups = Path.GetFullPath(backupRoot);
            RejectOverlap(install, stage);
            RejectOverlap(install, backups);
            RejectOverlap(stage, backups);
            EnsureNoReparseAncestors(backups);
            if (Directory.Exists(backups)) EnsureSafeTree(backups, false);
            updateLock = new FileStream(Path.Combine(install, "mrt-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            // Recheck after obtaining the exclusive application-update lock.
            ValidateInstallDirectory(install);
            if (File.Exists(Contained(install, PendingFile)))
                throw new InvalidDataException("安裝目錄有尚未處理的更新交易 mrt-update.pending.json。請先用更新輔助程式 --recover 及該檔指定的備份目錄復原，勿重複套用。");
            string receiptPath = Contained(install, UpdateConstants.InstallationReceipt);
            if (!File.Exists(receiptPath))
                throw new InvalidDataException("此安裝缺少 mrt-installed-files.json，不能自動辨識程式檔所有權；請手動移至支援更新的版本。");
            InstalledFiles receipt = Read<InstalledFiles>(receiptPath);
            string[] oldFiles = ValidateReceipt(receipt);
            UpdateManifest manifest = Read<UpdateManifest>(Contained(stage, UpdateConstants.ManifestFile));
            ValidateApplicationManifest(manifest, receipt);
            PackageStager.ValidateStaged(stage, manifest);
            string[] incoming = manifest.Files.Select(file => Normalize(file.Path)).ToArray();
            var oldSet = new HashSet<string>(oldFiles, StringComparer.OrdinalIgnoreCase);
            var newSet = new HashSet<string>(incoming, StringComparer.OrdinalIgnoreCase);
            byte[] receiptBytes = JsonSerializer.SerializeToUtf8Bytes(new InstalledFiles(1, UpdateConstants.Product, manifest.Version, incoming), Json);
            foreach (string path in incoming)
            {
                string target = Contained(install, path);
                EnsureNoReparseAncestors(target);
                if (Directory.Exists(target)) throw new IOException($"程式檔位置已有資料夾：{path}");
                if (File.Exists(target) && !oldSet.Contains(path))
                    throw new InvalidDataException($"拒絕覆寫未列管的既有檔案：{path}");
            }
            string[] changed = oldFiles.Union(incoming, StringComparer.OrdinalIgnoreCase)
                .Append(UpdateConstants.InstallationReceipt).ToArray();
            foreach (string path in changed)
            {
                string target = Contained(install, path);
                EnsureNoReparseAncestors(target);
                if (Directory.Exists(target)) throw new IOException($"列管檔案位置已有資料夾：{path}");
            }
            Directory.CreateDirectory(backups);
            backup = Path.Combine(backups, $"update-{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(backup);
            var newHashes = manifest.Files.ToDictionary(file => Normalize(file.Path), file => file.Sha256, StringComparer.OrdinalIgnoreCase);
            newHashes[UpdateConstants.InstallationReceipt] = Convert.ToHexString(SHA256.HashData(receiptBytes));
            var entries = new List<JournalEntry>();
            // No installed file is changed until every original file has a verified backup.
            foreach (string path in changed)
            {
                string target = Contained(install, path);
                bool existed = File.Exists(target);
                string? hash = null;
                if (existed)
                {
                    string original = Contained(Path.Combine(backup, "files"), path);
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    File.Copy(target, original, false);
                    hash = Hash(original);
                    if (!string.Equals(Hash(target), hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException($"備份期間檔案已變更：{path}");
                }
                entries.Add(new(path, existed, hash, newHashes.GetValueOrDefault(path)));
            }
            journal = new Journal(1, install, "prepared", entries.ToArray());
            WriteJournal(backup, journal);
            WriteAtomic(Contained(install, PendingFile), JsonSerializer.SerializeToUtf8Bytes(new PendingUpdate(backup), Json));
            // A durable journal precedes the first mutation, so an interrupted helper can be recovered.
            foreach (UpdateFile file in manifest.Files)
            {
                string path = Normalize(file.Path);
                string source = Contained(Path.Combine(stage, "payload"), path);
                EnsureNoReparseAncestors(source);
                ValidateFile(source, file);
                VerifyOriginalUnchanged(install, journal.Entries.Single(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)));
                AtomicCopy(source, Contained(install, path), file);
                ValidateFile(Contained(install, path), file);
                afterWrite?.Invoke(path);
            }
            foreach (string path in oldFiles.Where(path => !newSet.Contains(path)))
            {
                VerifyOriginalUnchanged(install, journal.Entries.Single(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)));
                string target = Contained(install, path);
                EnsureNoReparseAncestors(target);
                if (File.Exists(target)) File.Delete(target);
                afterWrite?.Invoke(path);
            }
            VerifyOriginalUnchanged(install, journal.Entries.Single(entry => entry.Path == UpdateConstants.InstallationReceipt));
            WriteAtomic(receiptPath, receiptBytes);
            afterWrite?.Invoke(UpdateConstants.InstallationReceipt);
            journal = journal with { State = "complete" };
            WriteJournal(backup, journal);
            ClearPending(install, backup);
            return new(true, $"程式已更新至 {manifest.Version}。", backup);
        }
        catch (Exception error)
        {
            if (journal is not null && backup is not null)
            {
                try
                {
                    Restore(backup, journal);
                    WriteJournal(backup, journal with { State = "rolledBack" });
                    ClearPending(journal.InstallDirectory, backup);
                    return new(false, $"更新失敗，原程式已復原：{error.Message}", backup);
                }
                catch (Exception rollbackError)
                {
                    return new(false, $"更新失敗：{error.Message}；自動復原未完成：{rollbackError.Message}。保留備份 {backup}，可用更新輔助程式 --recover 復原；請勿重試更新。", backup);
                }
            }
            return new(false, $"更新未套用：{error.Message}", backup);
        }
        finally { updateLock?.Dispose(); }
    }

    /// <summary>Explicit recovery after power loss or helper termination. Never starts the application.</summary>
    public static SoftwareUpdateResult Recover(string backupDirectory)
    {
        try
        {
            string backup = ValidateDirectory(backupDirectory);
            Journal journal = Read<Journal>(Contained(backup, "journal.json"));
            if (journal.FormatVersion != 1 || journal.Entries is null || journal.State is not ("prepared" or "complete" or "rolledBack"))
                throw new InvalidDataException("此備份沒有待復原的更新交易。");
            string install = ValidateInstallDirectory(journal.InstallDirectory);
            RejectOverlap(install, backup);
            using var updateLock = new FileStream(Path.Combine(install, "mrt-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ValidatePending(install, backup);
            if (journal.State is "complete" or "rolledBack")
            {
                ClearPending(install, backup);
                return new(true, journal.State == "complete" ? "此更新交易已完成，已清除殘留狀態。" : "此更新交易已復原，已清除殘留狀態。", backup);
            }
            // Refuse to overwrite files subsequently changed by the user or another installation.
            foreach (JournalEntry entry in journal.Entries)
            {
                ValidateJournalEntry(entry);
                string target = Contained(install, entry.Path);
                if (File.Exists(target))
                {
                    string hash = Hash(target);
                    bool original = entry.Existed && string.Equals(hash, entry.BackupSha256, StringComparison.OrdinalIgnoreCase);
                    bool updated = entry.NewSha256 is not null && string.Equals(hash, entry.NewSha256, StringComparison.OrdinalIgnoreCase);
                    // Receipt is handled separately below; only an original or the transaction's new receipt is safe.
                    if (!original && !updated) throw new IOException($"更新後檔案已有其他修改，須人工檢查：{entry.Path}");
                }
            }
            Restore(backup, journal);
            WriteJournal(backup, journal with { State = "rolledBack" });
            ClearPending(install, backup);
            return new(true, "已從更新交易備份復原原程式。", backup);
        }
        catch (Exception error) { return new(false, $"無法復原：{error.Message}", backupDirectory); }
    }

    private static void Restore(string backup, Journal journal)
    {
        string install = ValidateInstallDirectory(journal.InstallDirectory);
        var entries = journal.Entries;
        if (entries.Select(entry => Normalize(entry.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            throw new InvalidDataException("更新日誌有重複路徑。");
        // Validate every backup before restoring anything.
        foreach (JournalEntry entry in entries)
        {
            ValidateJournalEntry(entry);
            string target = Contained(install, entry.Path);
            EnsureNoReparseAncestors(target);
            if (Directory.Exists(target)) throw new IOException($"復原路徑已有資料夾：{entry.Path}");
            if (File.Exists(target))
            {
                string current = Hash(target);
                bool original = entry.Existed && string.Equals(current, entry.BackupSha256, StringComparison.OrdinalIgnoreCase);
                bool updated = entry.NewSha256 is not null && string.Equals(current, entry.NewSha256, StringComparison.OrdinalIgnoreCase);
                if (!original && !updated) throw new IOException($"檔案已有其他修改，拒絕覆蓋：{entry.Path}");
            }
            if (entry.Existed)
            {
                string original = Contained(Path.Combine(backup, "files"), entry.Path);
                EnsureNoReparseAncestors(original);
                if (!File.Exists(original) || !string.Equals(Hash(original), entry.BackupSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"備份驗證失敗：{entry.Path}");
            }
        }
        // Receipt is last in the journal and is restored after all binaries.
        foreach (JournalEntry entry in entries)
        {
            string target = Contained(install, entry.Path);
            EnsureNoReparseAncestors(target);
            if (entry.Existed)
            {
                if (File.Exists(target) && string.Equals(Hash(target), entry.BackupSha256, StringComparison.OrdinalIgnoreCase)) continue;
                string original = Contained(Path.Combine(backup, "files"), entry.Path);
                AtomicCopy(original, target, new UpdateFile(entry.Path, entry.BackupSha256!, new FileInfo(original).Length));
            }
            else if (File.Exists(target)) File.Delete(target);
        }
    }

    private static void ValidateJournalEntry(JournalEntry entry)
    {
        string path = Normalize(entry.Path);
        if (path != UpdateConstants.InstallationReceipt && !UpdatePathPolicy.IsAllowedApplicationPath(path))
            throw new InvalidDataException($"更新日誌有不允許的路徑：{path}");
        if (entry.Existed && (entry.BackupSha256 is null || entry.BackupSha256.Length != 64))
            throw new InvalidDataException("更新日誌缺少備份雜湊。");
    }

    private static void ValidatePending(string install, string backup)
    {
        string pending = Contained(install, PendingFile);
        EnsureNoReparseAncestors(pending);
        if (File.Exists(pending) && !string.Equals(Path.GetFullPath(Read<PendingUpdate>(pending).BackupDirectory), Path.GetFullPath(backup), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安裝目錄指向另一個待處理交易；不得套用不同交易的復原。");
    }
    private static void ClearPending(string install, string backup)
    {
        ValidatePending(install, backup);
        string pending = Contained(install, PendingFile);
        if (File.Exists(pending)) File.Delete(pending);
    }

    private static void VerifyOriginalUnchanged(string install, JournalEntry entry)
    {
        string target = Contained(install, entry.Path);
        EnsureNoReparseAncestors(target);
        if (File.Exists(target) != entry.Existed || (entry.Existed && !string.Equals(Hash(target), entry.BackupSha256, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"套用更新前，既有檔案已變更：{entry.Path}");
    }

    private static string[] ValidateReceipt(InstalledFiles receipt)
    {
        if (receipt.FormatVersion != 1 || receipt.Product != UpdateConstants.Product || receipt.Files is null || receipt.Files.Length == 0 || receipt.Files.Length > UpdatePathPolicy.MaximumFiles)
            throw new InvalidDataException("安裝檔案清單格式或版本無效。");
        _ = UpdatePathPolicy.ParseStableVersion(receipt.Version);
        string[] paths = receipt.Files.Select(Normalize).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length || !paths.Contains(UpdateConstants.ApplicationExe, StringComparer.Ordinal))
            throw new InvalidDataException("安裝檔案清單缺少主程式或包含重複路徑。");
        foreach (string path in paths)
            if (path == UpdateConstants.InstallationReceipt || !UpdatePathPolicy.IsAllowedApplicationPath(path))
                throw new InvalidDataException($"安裝清單包含不允許的檔案：{path}");
        return paths;
    }

    private static void ValidateApplicationManifest(UpdateManifest manifest, InstalledFiles receipt)
    {
        UpdatePathPolicy.ValidateManifest(manifest);
        if (manifest.Kind != UpdateKind.Application || manifest.Product != UpdateConstants.Product)
            throw new InvalidDataException("此更新套件不是本程式的應用程式更新。");
        if (!Version.TryParse(manifest.Version, out Version? next) || !Version.TryParse(receipt.Version, out Version? current) ||
            !Version.TryParse(manifest.MinimumAppVersion, out Version? minimum) || next <= current || current < minimum)
            throw new InvalidDataException("更新版本必須高於目前版本，且目前版本須符合最低版本要求。");
        if (!manifest.Files.Any(file => string.Equals(Normalize(file.Path), UpdateConstants.ApplicationExe, StringComparison.Ordinal)))
            throw new InvalidDataException("更新套件缺少主程式。");
        foreach (UpdateFile file in manifest.Files)
        {
            string path = Normalize(file.Path);
            if (path == UpdateConstants.InstallationReceipt || !UpdatePathPolicy.IsAllowedApplicationPath(path))
                throw new InvalidDataException($"應用程式更新不允許此檔案：{path}");
        }
    }

    private static string Normalize(string path) => UpdatePathPolicy.NormalizeRelativePath(path);
    private static string Contained(string root, string path) => UpdatePathPolicy.GetContainedPath(root, path);
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException($"無效的 JSON：{Path.GetFileName(path)}");
    private static string Hash(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void ValidateFile(string path, UpdateFile file)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size || !string.Equals(Hash(path), file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"暫存或安裝檔案的大小／SHA-256 驗證失敗：{file.Path}");
    }
    private static void WriteJournal(string backup, Journal journal) => WriteAtomic(Path.Combine(backup, "journal.json"), JsonSerializer.SerializeToUtf8Bytes(journal, Json));
    private static void AtomicCopy(string source, string target, UpdateFile expected)
    {
        EnsureNoReparseAncestors(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".mrt-update-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary, false);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.None)) stream.Flush(true);
            // The source can change after its preliminary validation. Only verified bytes
            // may replace an installed file; a failed copy must leave that file untouched.
            ValidateFile(temporary, expected);
            EnsureNoReparseAncestors(target);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void WriteAtomic(string target, byte[] bytes)
    {
        EnsureNoReparseAncestors(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".mrt-update-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            EnsureNoReparseAncestors(target);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string ValidateInstallDirectory(string path)
    {
        string root = ValidateDirectory(path);
        if (string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("不能更新磁碟根目錄。");
        EnsureSafeTree(root, true);
        for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                throw new InvalidDataException("不能更新 Git 工作目錄或其子目錄。");
        return root;
    }
    private static string ValidateDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"資料夾不存在：{full}");
        EnsureNoReparseAncestors(full);
        EnsureSafeTree(full, false);
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static void EnsureNoReparseAncestors(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"更新路徑不可包含 reparse point：{current}");
    }
    private static void EnsureSafeTree(string root, bool rejectGit)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(root))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"更新路徑不可包含 reparse point：{entry}");
            if (rejectGit && string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不能更新含有 .git 的資料夾。");
            if ((attributes & FileAttributes.Directory) != 0) EnsureSafeTree(entry, rejectGit);
        }
    }
    private static void RejectOverlap(string left, string right)
    {
        left = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        right = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase) || left.StartsWith(right + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || right.StartsWith(left + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安裝、暫存及備份資料夾必須彼此獨立。");
    }
}
