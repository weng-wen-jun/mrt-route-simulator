namespace MrtRouteSimulator.UpdateCore;

public enum UpdateKind { Application, Data }
public sealed record UpdateFile(string Path, string Sha256, long Size);
public sealed record UpdateManifest(int FormatVersion, string Product, UpdateKind Kind, string Version, string MinimumAppVersion, int SchemaVersion, string PackageFileName, string PackageSha256, long PackageSize, UpdateFile[] Files);
public sealed record ReleaseAsset(string Name, Uri DownloadUri, long Size, string? Digest);
public sealed record UpdateOffer(UpdateManifest Manifest, ReleaseAsset Package, string Notes, Uri ReleasePage);
public sealed record UpdateCheckResult(UpdateOffer? Offer, string Message);
public sealed record UpdateProgress(long ReceivedBytes, long? TotalBytes, string Message);
public sealed record StagedUpdate(string RootPath, UpdateManifest Manifest);
public sealed record InstalledFiles(int FormatVersion, string Product, string Version, string[] Files);
public sealed record SoftwareUpdateJob(string StageDirectory, string InstallDirectory, string ExeName, int ParentProcessId, long ParentStartTimeUtcTicks, bool Restart);
public sealed record SoftwareUpdateResult(bool Success, string Message, string? BackupDirectory);
public static class UpdateConstants
{
    public const string Product = "MrtRouteSimulator";
    public const string Repository = "weng-wen-jun/mrt-route-simulator";
    public const string ApplicationExe = "MRT路線進出站時間模擬器.exe";
    public const string InstallationReceipt = "mrt-installed-files.json";
    public const string ManifestFile = "verified-manifest.json";
}
