using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MrtRouteSimulator.UpdateCore;

public static partial class UpdatePathPolicy
{
    public const int MaximumFiles = 10000;
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    public const long MaximumManifestBytes = 4L * 1024 * 1024;

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.Any(char.IsControl))
            throw new InvalidDataException("更新檔案路徑不合法。");
        string[] parts = path.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') || part.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
                throw new InvalidDataException("更新路徑含不安全的區段。");
            string stem = part.Split('.')[0];
            if (ReservedName().IsMatch(stem)) throw new InvalidDataException("更新路徑含 Windows 裝置名稱。");
        }
        return path;
    }

    public static bool IsAllowedApplicationPath(string path)
    {
        try { NormalizeRelativePath(path); } catch (InvalidDataException) { return false; }
        string[] parts = path.Split('/');
        if (parts.Length == 2 && parts[0] == "third-party-licenses")
            return parts[1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || parts[1].EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        if (parts.Length > 1 && parts[0] is not ("runtimes" or "mcp" or "updater"))
            return parts.Length == 2 && IsRuntimeCulture(parts[0]) && parts[1].EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
        string name = parts[^1];
        if (parts.Length == 1 && name is "README.md" or "LICENSE.txt") return true;
        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase);
    }

    // Cultures shipped by the Windows Desktop runtime. Only satellite assemblies belong here;
    // arbitrary culture-looking folders must never grant access to projects or user settings.
    private static bool IsRuntimeCulture(string name) => name is
        "cs" or "de" or "es" or "fr" or "it" or "ja" or "ko" or "pl" or "pt-BR" or "ru" or "tr" or "zh-Hans" or "zh-Hant";

    public static void ValidateManifest(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FormatVersion != 1 || manifest.Product != UpdateConstants.Product || !Enum.IsDefined(manifest.Kind))
            throw new InvalidDataException("不支援的更新清單格式或產品。");
        _ = ParseStableVersion(manifest.Version);
        _ = ParseStableVersion(manifest.MinimumAppVersion);
        if (manifest.SchemaVersion <= 0 || manifest.PackageSize <= 0 || manifest.PackageSize > MaximumPackageBytes || !IsHash(manifest.PackageSha256))
            throw new InvalidDataException("更新套件大小、雜湊或 Schema 不合法。");
        NormalizeRelativePath(manifest.PackageFileName);
        if (manifest.PackageFileName.Contains('/') || !manifest.PackageFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新套件必須是單一 ZIP 檔名。");
        if (manifest.Files is null || manifest.Files.Length == 0 || manifest.Files.Length > MaximumFiles)
            throw new InvalidDataException("更新清單檔案數量不合法。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (UpdateFile file in manifest.Files)
        {
            if (file is null) throw new InvalidDataException("更新檔案項目遺失。");
            string path = NormalizeRelativePath(file.Path);
            if (!names.Add(path) || file.Size < 0 || file.Size > MaximumExpandedBytes || !IsHash(file.Sha256))
                throw new InvalidDataException("更新清單有重複檔名、錯誤大小或雜湊。");
            total = checked(total + file.Size);
            if (total > MaximumExpandedBytes) throw new InvalidDataException("更新解壓大小超過上限。");
            if (manifest.Kind == UpdateKind.Application ? !IsAllowedApplicationPath(path) : !(path.EndsWith(".mrtsim.json", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase) || path.Equals("README.md", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("更新包含未允許的檔案類型或位置。");
        }
        foreach (string name in names)
        {
            string[] parts = name.Split('/');
            for (int i = 1; i < parts.Length; i++)
                if (names.Contains(string.Join('/', parts.Take(i)))) throw new InvalidDataException("更新清單檔案與目錄互相衝突。");
        }
    }

    public static Version ParseStableVersion(string value)
    {
        if (value is null || !StableVersion().IsMatch(value) || !Version.TryParse(value, out Version? parsed))
            throw new InvalidDataException("更新版本必須是三段式穩定版本。");
        return new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
    }

    public static bool IsHash(string? value) => value is not null && Sha256Pattern().IsMatch(value);
    public static string GetContainedPath(string root, string relative)
    {
        NormalizeRelativePath(relative);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新路徑超出目標目錄。");
        return full;
    }

    public static void RejectReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("更新目錄不可包含連結或 reparse point。");
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)] private static partial Regex StableVersion();
    [GeneratedRegex(@"^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)] private static partial Regex Sha256Pattern();
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ReservedName();
}

public static class UpdateJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        WriteIndented = true
    };
}
