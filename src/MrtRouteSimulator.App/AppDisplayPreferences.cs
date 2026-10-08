using System.IO;
using System.Text.Json;

namespace MrtRouteSimulator.App;

/// <summary>只保存本機畫面偏好，不參與專案或範例的序列化。</summary>
internal static class AppDisplayPreferences
{
    private const double DefaultInterfaceScale = 1;
    private const double DefaultRouteMapHorizontalZoom = 1;
    private const double MinimumRouteMapHorizontalZoom = 1;
    private const double MaximumRouteMapHorizontalZoom = 2;
    private static readonly double[] InterfaceScaleOptions = [0.8, 0.9, 1, 1.1, 1.25];

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MrtRouteSimulator", "display-settings.json");

    public static bool LoadShowLockedRoutes() => Load().ShowLockedRoutes;

    public static double LoadRouteMapHorizontalZoom() => Load().RouteMapHorizontalZoom;

    public static double LoadInterfaceScale() => Load().InterfaceScale;

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

    public static void SaveInterfaceScale(double scale)
    {
        var settings = Load();
        Save(settings with { InterfaceScale = NormalizeInterfaceScale(scale) });
    }

    private static DisplaySettings Load() => Load(SettingsPath);

    internal static DisplaySettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new DisplaySettings();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new DisplaySettings();
            }

            var showLockedRoutes = document.RootElement.TryGetProperty("ShowLockedRoutes", out var showValue)
                && showValue.ValueKind == JsonValueKind.True;
            var routeMapHorizontalZoom = document.RootElement.TryGetProperty("RouteMapHorizontalZoom", out var zoomValue)
                && zoomValue.ValueKind == JsonValueKind.Number
                && zoomValue.TryGetDouble(out var zoom)
                    ? NormalizeRouteMapHorizontalZoom(zoom)
                    : DefaultRouteMapHorizontalZoom;
            var interfaceScale = document.RootElement.TryGetProperty("InterfaceScale", out var scaleValue)
                && scaleValue.ValueKind == JsonValueKind.Number
                && scaleValue.TryGetDouble(out var scale)
                    ? NormalizeInterfaceScale(scale)
                    : DefaultInterfaceScale;
            return new DisplaySettings(showLockedRoutes, routeMapHorizontalZoom, interfaceScale);
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

    private static void Save(DisplaySettings settings) => Save(SettingsPath, settings);

    internal static void Save(string path, DisplaySettings settings)
    {
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

    internal static double NormalizeInterfaceScale(double scale)
    {
        if (!double.IsFinite(scale)) return DefaultInterfaceScale;

        var nearest = InterfaceScaleOptions[0];
        var nearestDistance = Math.Abs(scale - nearest);
        for (var index = 1; index < InterfaceScaleOptions.Length; index++)
        {
            var candidate = InterfaceScaleOptions[index];
            var distance = Math.Abs(scale - candidate);
            if (distance >= nearestDistance) continue;
            nearest = candidate;
            nearestDistance = distance;
        }

        return nearest;
    }

    internal sealed record DisplaySettings(
        bool ShowLockedRoutes = false,
        double RouteMapHorizontalZoom = DefaultRouteMapHorizontalZoom,
        double InterfaceScale = DefaultInterfaceScale);
}
