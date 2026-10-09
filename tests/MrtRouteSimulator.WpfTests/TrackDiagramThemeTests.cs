using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>介面翻新第一輪（UiTheme + 軌道配線圖）的回歸測試。</summary>
internal static class TrackDiagramThemeTests
{
    private static readonly Assembly AppAssembly = typeof(MainWindow).Assembly;

    public static void Run(string root)
    {
        VerifyThemeTokens();
        Console.WriteLine("PASS WPF track diagram theme");
    }

    private static void VerifyThemeTokens()
    {
        var theme = AppType("UiTheme");
        var brushes = theme.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SolidColorBrush))
            .Select(field => (field.Name, Brush: (SolidColorBrush)field.GetValue(null)!))
            .ToArray();
        Require(brushes.Length >= 16, $"UiTheme 應提供至少 16 支具名畫筆，實際 {brushes.Length}。");
        Require(brushes.All(item => item.Brush.IsFrozen),
            $"UiTheme 畫筆必須全部凍結：{string.Join("、", brushes.Where(item => !item.Brush.IsFrozen).Select(item => item.Name))}");
        Require(((Color)StaticValue(theme, "OccupancyGlow")!).A == 0x80, "區段占用光帶必須是半透明（alpha 0x80）。");

        var vehicle = (Color[])StaticValue(theme, "VehiclePalette")!;
        Require(vehicle.Length == 8 && vehicle.Distinct().Count() == 8, "列車色盤必須是 8 個不重複顏色。");
        var avoid = new[] { "RailUp", "RailDown", "Danger" }.Select(name => Hsv((Color)StaticValue(theme, name)!).H).ToArray();
        foreach (var color in vehicle)
        {
            var (hue, saturation, _) = Hsv(color);
            Require(saturation < .35 || avoid.All(other => HueDistance(hue, other) >= 20),
                $"列車色 {color} 的色相太接近上下行軌道或危險紅。");
        }
        var vehicleBrushes = (SolidColorBrush[])StaticValue(theme, "VehicleBrushes")!;
        Require(vehicleBrushes.Length == vehicle.Length
            && vehicleBrushes.Select(brush => brush.Color).SequenceEqual(vehicle)
            && vehicleBrushes.All(brush => brush.IsFrozen), "VehicleBrushes 必須與 VehiclePalette 一一對應且凍結。");
        var negative = (SolidColorBrush)CallStatic(theme, "VehicleBrush", -3)!;
        Require(vehicleBrushes.Contains(negative), "VehicleBrush 對負索引也必須回傳色盤內的畫筆。");

        var locked = (Color[])StaticValue(theme, "LockedRoutePalette")!;
        Require(locked.Length == 5 && locked.All(color => color.R > color.B && color.G < 170),
            "鎖定進路色盤必須全部是 R > B 且 G < 170 的紅／洋紅系。");

        var trainColors = (Color[])typeof(MainWindow).GetField("TrainColors", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var lockColors = (Color[])typeof(MainWindow).GetField("LockedRouteColors", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Require(ReferenceEquals(trainColors, vehicle), "MainWindow.TrainColors 必須直接引用 UiTheme.VehiclePalette。");
        Require(ReferenceEquals(lockColors, locked), "MainWindow.LockedRouteColors 必須直接引用 UiTheme.LockedRoutePalette。");
        Console.WriteLine("[通過] UiTheme 色票、凍結畫筆與色盤規則");
    }

    private static (double H, double S, double V) Hsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    private static double HueDistance(double first, double second)
    {
        var distance = Math.Abs(first - second) % 360;
        return distance > 180 ? 360 - distance : distance;
    }

    private static Type AppType(string name) =>
        AppAssembly.GetType($"MrtRouteSimulator.App.{name}")
        ?? throw new InvalidOperationException($"找不到 App 型別 {name}。");

    private static object? StaticValue(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        if (type.GetField(name, flags) is { } field) return field.GetValue(null);
        if (type.GetProperty(name, flags) is { } property) return property.GetValue(null);
        throw new InvalidOperationException($"{type.Name} 找不到靜態成員 {name}。");
    }

    private static object? CallStatic(Type type, string method, params object?[] args)
    {
        var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{type.Name} 找不到靜態方法 {method}。");
        return target.Invoke(null, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
