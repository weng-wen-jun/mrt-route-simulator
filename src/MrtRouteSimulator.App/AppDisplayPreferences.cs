using System.IO;
using System.Text.Json;

namespace MrtRouteSimulator.App;

/// <summary>只保存本機畫面偏好，不參與專案或範例的序列化。</summary>
internal static class AppDisplayPreferences
{
    private const double DefaultRouteMapHorizontalZoom = 1;
    private const double MinimumRouteMapHorizontalZoom = 1;
    private const double MaximumRouteMapHorizontalZoom = 2;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MrtRouteSimulator", "display-settings.json");

    public static bool LoadShowLockedRoutes() => Load().ShowLockedRoutes;

    public static double LoadRouteMapHorizontalZoom() => Load().RouteMapHorizontalZoom;

    public static void SaveShowLockedRoutes(bool show)
    {
        var settings = Load();
        Save(settings with { ShowLockedRoutes = show });
    }

    public static void SaveRouteMapHorizontalZoom(double zoom)
    {
        var settings = Load();
        Save(settings with { RouteMapHorizontalZoom = NormalizeRouteMapHorizontalZoom(zoom) });
    }

    private static DisplaySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new DisplaySettings();
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new DisplaySettings();
            }

            var showLockedRoutes = document.RootElement.TryGetProperty("ShowLockedRoutes", out var showValue)
                && showValue.ValueKind == JsonValueKind.True;
            var routeMapHorizontalZoom = document.RootElement.TryGetProperty("RouteMapHorizontalZoom", out var zoomValue)
                && zoomValue.TryGetDouble(out var zoom)
                    ? NormalizeRouteMapHorizontalZoom(zoom)
                    : DefaultRouteMapHorizontalZoom;
            return new DisplaySettings(showLockedRoutes, routeMapHorizontalZoom);
        }
        catch (IOException)
        {
            return new DisplaySettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new DisplaySettings();
        }
        catch (JsonException)
        {
            return new DisplaySettings();
        }
    }

    private static void Save(DisplaySettings settings)
    {
        var path = SettingsPath;
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"display-settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static double NormalizeRouteMapHorizontalZoom(double zoom) =>
        double.IsFinite(zoom)
            ? Math.Clamp(zoom, MinimumRouteMapHorizontalZoom, MaximumRouteMapHorizontalZoom)
            : DefaultRouteMapHorizontalZoom;

    private sealed record DisplaySettings(
        bool ShowLockedRoutes = false,
        double RouteMapHorizontalZoom = DefaultRouteMapHorizontalZoom);
}
