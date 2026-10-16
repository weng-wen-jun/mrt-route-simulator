using System.Diagnostics;
using System.Text.Json;
using MrtRouteSimulator.UpdateCore;

return await UpdaterHost.RunAsync(args);

/// <summary>Runs from a per-job runner outside the installation, after the parent exits.</summary>
public static class UpdaterHost
{
    public static JsonSerializerOptions JsonOptions => UpdateJson.Options;

    public static async Task<int> RunAsync(string[] args)
    {
        string? outputDirectory = null;
        SoftwareUpdateResult result;
        try
        {
            if (args.Length != 2 || (args[0] != "--job" && args[0] != "--recover"))
                throw new ArgumentException("用法：MrtRouteSimulator.Updater --job <job.json> 或 --recover <backup-directory>");
            if (args[0] == "--recover")
            {
                outputDirectory = SafePath(args[1]);
                result = SoftwareUpdateInstaller.Recover(outputDirectory);
            }
            else
            {
                string jobPath = SafePath(args[1]);
                outputDirectory = Path.GetDirectoryName(jobPath)!;
                if (new FileInfo(jobPath).Length > 64 * 1024) throw new InvalidDataException("更新工作檔過大。");
                SoftwareUpdateJob job = JsonSerializer.Deserialize<SoftwareUpdateJob>(await File.ReadAllBytesAsync(jobPath), JsonOptions)
                    ?? throw new InvalidDataException("更新工作檔無效。");
                string install = SafePath(job.InstallDirectory);
                if (job.ExeName != UpdateConstants.ApplicationExe) throw new InvalidDataException("更新目標執行檔不正確。");
                if (IsWithin(install, AppContext.BaseDirectory) || IsWithin(install, outputDirectory))
                    throw new InvalidDataException("更新輔助程式與工作檔必須位於安裝資料夾外的獨立工作目錄。");
                AppendLog(outputDirectory, "等待主程式結束；不會強制關閉任何程序。");
                await WaitForParentAsync(job, TimeSpan.FromSeconds(120));
                AppendLog(outputDirectory, "主程式已結束，開始驗證及套用更新。");
                result = SoftwareUpdateInstaller.Apply(job, Path.Combine(outputDirectory, "backups"), path => AppendLog(outputDirectory, $"已更新：{path}"));
                if (result.Success && job.Restart)
                {
                    try
                    {
                        string exe = SafePath(Path.Combine(install, UpdateConstants.ApplicationExe));
                        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = install };
                        // No shell command or manifest-supplied arguments are accepted.
                        using Process? restarted = Process.Start(start);
                        if (restarted is null) throw new IOException("無法建立主程式程序。");
                    }
                    catch (Exception error) { result = result with { Message = result.Message + $" 自動重新啟動失敗，請手動啟動：{error.Message}" }; }
                }
            }
        }
        catch (Exception error) { result = new(false, $"更新未完成：{error.Message}", null); }
        if (outputDirectory is not null)
        {
            try
            {
                SafePath(outputDirectory);
                AppendLog(outputDirectory, result.Message);
                string destination = SafePath(Path.Combine(outputDirectory, "update-result.json"));
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions));
                File.Move(temporary, destination, true);
            }
            catch (Exception error) { Console.Error.WriteLine($"更新結果無法寫入：{error.Message}"); }
        }
        Console.WriteLine(result.Message);
        return result.Success ? 0 : 1;
    }

    public static async Task WaitForParentAsync(SoftwareUpdateJob job, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (job.ParentProcessId <= 0 || job.ParentProcessId == Environment.ProcessId || job.ParentStartTimeUtcTicks <= 0)
            throw new InvalidDataException("主程式程序身分無效。");
        Process parent;
        try { parent = Process.GetProcessById(job.ParentProcessId); }
        catch (ArgumentException) { return; } // Original parent may already have exited during helper startup.
        using (parent)
        {
            try
            {
                if (parent.HasExited) return;
                if (parent.StartTime.ToUniversalTime().Ticks != job.ParentStartTimeUtcTicks)
                    throw new InvalidDataException("程序 ID 已被其他程序使用，拒絕更新。");
                string expected = Path.GetFullPath(Path.Combine(job.InstallDirectory, UpdateConstants.ApplicationExe));
                string? actual = parent.MainModule?.FileName;
                if (actual is null || !string.Equals(Path.GetFullPath(actual), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("主程式程序的執行檔路徑不符，拒絕更新。");
            }
            catch (InvalidOperationException) when (parent.HasExited) { return; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            try { await parent.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new TimeoutException("主程式在 120 秒內未結束；未套用更新，也未強制關閉程序。"); }
        }
    }

    private static string SafePath(string path)
    {
        string full = Path.GetFullPath(path);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"工作路徑不可包含 reparse point：{current}");
        return Path.TrimEndingDirectorySeparator(full);
    }
    private static bool IsWithin(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private static void AppendLog(string directory, string message)
    {
        string log = SafePath(Path.Combine(directory, "update.log"));
        File.AppendAllText(log, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}
