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
        VerifyTrainMarkers(root);
        Console.WriteLine("PASS WPF track diagram theme");
    }

    private static void VerifyTrainMarkers(string root)
    {
        var presentation = AppType("TrainMarkerPresentation");
        string Label(string id) => (string)CallStatic(presentation, "Label", id)!;
        var labelCases = new (string Input, string Expected)[]
        {
            ("Vehicle 3", "V03"),
            ("AUTO-007", "A07"),
            ("FULL-O04", "FULL-O04"),
            ("EXPRESS-01", "EXPRESS-01"),
            ("FULL-UP-01", "FULL-UP-01"),
            ("SECTION-VEHICLE-01", "SV-01"),
            ("FULL-SECTION-O04", "FS-O04"),
            ("普通車-區間快速-0001", "普區-0001"),
            ("LONG-VEHICLE-NAME-", "LV-NAME"),
            ("A--B-C-0123456789", "ABC-01234…"),
            ("ABCDEFGHIJKLMN", "ABCDEFGHI…")
        };
        var mainLabel = typeof(MainWindow).GetMethod("VehicleMarkerLabel", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (input, expected) in labelCases)
        {
            var actual = Label(input);
            Require(actual == expected, $"車號 {input} 應顯示 {expected}，實際 {actual}。");
            Require(actual.Length is > 0 and <= 10, $"車號 {input} 的標籤長度必須介於 1～10。");
            Require((string)mainLabel.Invoke(null, [input])! == expected, $"MainWindow.VehicleMarkerLabel 必須委派給 TrainMarkerPresentation（{input}）。");
        }

        int Heading(Point? front, Point center, TrainDirection direction) =>
            (int)CallStatic(presentation, "Heading", front, center, direction)!;
        Require(Heading(new Point(110, 0), new Point(100, 0), TrainDirection.Inbound) == 1, "車頭在右側時箭頭必須朝右。");
        Require(Heading(new Point(90, 0), new Point(100, 0), TrainDirection.Outbound) == -1, "車頭在左側時箭頭必須朝左（例如折返後）。");
        Require(Heading(new Point(100.3, 0), new Point(100, 0), TrainDirection.Inbound) == -1, "車頭與車體中心幾乎重合時改依行車方向。");
        Require(Heading(null, new Point(100, 0), TrainDirection.Outbound) == 1, "缺少車頭位置時下行朝右。");

        var right = (Border)CallStatic(presentation, "Create", "SECTION-VEHICLE-01", Brushes.Teal, 1, "tip")!;
        var rightGrid = (Grid)right.Child;
        var rightText = rightGrid.Children.OfType<TextBlock>().Single();
        var rightChevron = rightGrid.Children.OfType<System.Windows.Shapes.Path>().Single();
        Require(right.Height == 18 && right.Width >= 34 && right.CornerRadius.TopLeft == 9, "列車膠囊應為高 18、圓角 9、最小寬 34。");
        Require(rightText.Text == "SV-01" && Grid.GetColumn(rightText) == 0 && Grid.GetColumn(rightChevron) == 1,
            "朝右膠囊的箭頭必須在右側（領先端）。");
        Require(Equals(right.ToolTip, "tip"), "膠囊必須保留呼叫端提供的提示文字。");
        var left = (Border)CallStatic(presentation, "Create", "E1", Brushes.Teal, -1, "tip")!;
        Require(Grid.GetColumn(((Grid)left.Child).Children.OfType<System.Windows.Shapes.Path>().Single()) == 0,
            "朝左膠囊的箭頭必須在左側（領先端）。");

        CallStatic(presentation, "Place", right, new Point(5, 5), 300d, 200d);
        Require(Canvas.GetLeft(right) == 0 && Canvas.GetTop(right) == 0 && Panel.GetZIndex(right) == 5,
            "左上角列車必須完整留在畫布內，且位於月台之上（ZIndex 5）。");
        CallStatic(presentation, "Place", right, new Point(298, 199), 300d, 200d);
        Require(Math.Abs(Canvas.GetLeft(right) + right.Width - 300) < 1e-9 && Math.Abs(Canvas.GetTop(right) - 182) < 1e-9,
            "右下角列車必須完整留在畫布內。");
        CallStatic(presentation, "Place", right, new Point(150, 100), 300d, 200d);
        Require(Math.Abs(Canvas.GetLeft(right) + right.Width / 2 - 150) < 1e-9 && Math.Abs(Canvas.GetTop(right) - 91) < 1e-9,
            "一般位置的列車必須以中心點置中。");

        var vehicleBrushes = (SolidColorBrush[])StaticValue(AppType("UiTheme"), "VehicleBrushes")!;
        var danger = StaticValue(AppType("UiTheme"), "DangerBrush");
        var main = new MainWindow();
        try
        {
            // 範例 11 的 LOCAL-01 於 0 秒停靠 P-W-D（VisualRulesTests 已驗證），可穩定取得列車標記。
            var (canvas, _) = DrawSample(main, root, "11-小型-三站完整拓樸運行範例.mrtsim.json", 1200, 0);
            var trains = canvas.Children.OfType<Border>().Where(border => border.Tag is string).ToArray();
            Require(trains.Length > 0, "範例 11 在 0 秒應至少顯示一列車。");
            foreach (var train in trains)
            {
                var text = ((Grid)train.Child).Children.OfType<TextBlock>().Single();
                Require(train.Height == 18 && Panel.GetZIndex(train) == 5, $"{train.Tag}：列車膠囊尺寸或圖層錯誤。");
                Require(vehicleBrushes.Any(brush => ReferenceEquals(brush, train.Background)) || ReferenceEquals(train.Background, danger),
                    $"{train.Tag}：列車顏色必須取自 UiTheme。");
                Require(text.Text == Label((string)train.Tag), $"{train.Tag}：列車標籤必須套用短號規則。");
                Require(Canvas.GetLeft(train) >= 0 && Canvas.GetLeft(train) + train.Width <= canvas.Width + 1e-9,
                    $"{train.Tag}：列車膠囊不可超出畫布。");
            }
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 膠囊列車標記：短號、方向、定位與配色");
    }

    private static (Canvas Canvas, TopologyProjectDocument Sample) DrawSample(
        MainWindow main, string root, string fileName, double width, double seconds)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(System.IO.Path.Combine(root, "samples", fileName)));
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, sample.Simulation.HeadwaySeconds,
            ProfileMode: sample.Simulation.ProfileMode, MovingBlockMode: sample.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
        if (seconds > 0) world.AdvanceTo(seconds);
        ((MenuItem)main.FindName("ShowLockedRoutesMenuItem")).IsChecked = false;
        var canvas = (Canvas)main.FindName("RouteCanvas");
        canvas.Children.Clear();
        canvas.Width = width;
        canvas.Height = 400;
        var snapshot = world.GetSnapshot();
        var centers = snapshot.Trains
            .Select(state => (state.VehicleId, Center: world.GetTrainCenterPosition(state.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, width, 400d, centers, world.GetActiveRouteLocks()]);
        return (canvas, sample);
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
