using System.IO;
using System.Text.Json;

namespace MrtRouteSimulator.App;

/// <summary>只保存本機畫面偏好，不參與專案或範例的序列化。</summary>
internal static class AppDisplayPreferences
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MrtRouteSimulator", "display-settings.json");

    public static bool LoadShowLockedRoutes()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("ShowLockedRoutes", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static void SaveShowLockedRoutes(bool show)
    {
        var path = SettingsPath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"display-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new { ShowLockedRoutes = show }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
