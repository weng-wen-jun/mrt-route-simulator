using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>結果圖表主題（子專案 D）：色票、共用繪圖工具、各圖繪製、PDF 分頁與寫死顏色掃描。</summary>
internal static class ChartThemeTests
{
    public static void Run(string root)
    {
        VerifyThemeTokens();
        VerifyVehiclePaletteAvoidsGrays();
        VerifyNiceTicks();
        VerifyPainterElements();
        VerifyHeaderFitsNarrowCanvas();
        VerifySafetyStatusBrush();
        VerifyV2Charts(root);
        VerifyV1Charts();
        VerifySpatialReferencePointColors();
        VerifyNoHardCodedChartColors(root);
        Console.WriteLine("PASS WPF chart theme");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyThemeTokens()
    {
        var tokens = new (string Name, SolidColorBrush Actual, SolidColorBrush Source)[]
        {
            ("Background", ChartTheme.Background, UiTheme.CanvasBackgroundBrush),
            ("Grid", ChartTheme.Grid, UiTheme.BorderBrush),
            ("Axis", ChartTheme.Axis, UiTheme.TextSubtleBrush),
            ("AxisLabel", ChartTheme.AxisLabel, UiTheme.TextMutedBrush),
            ("Title", ChartTheme.Title, UiTheme.TextStrongBrush),
            ("LegendText", ChartTheme.LegendText, UiTheme.TextMutedBrush),
            ("Message", ChartTheme.Message, UiTheme.TextMutedBrush),
            ("Annotation", ChartTheme.Annotation, UiTheme.TextSubtleBrush),
            ("PrimarySeries", ChartTheme.PrimarySeries, UiTheme.AccentBrush),
            ("LimitSeries", ChartTheme.LimitSeries, UiTheme.TextMutedBrush),
            ("ThresholdSeries", ChartTheme.ThresholdSeries, UiTheme.RailNeutralStrongBrush),
            ("DangerSeries", ChartTheme.DangerSeries, UiTheme.DangerBrush),
            ("NeutralSeries", ChartTheme.NeutralSeries, UiTheme.RailNeutralStrongBrush),
            ("EventStation", ChartTheme.EventStation, UiTheme.SuccessBrush),
            ("EventTerminal", ChartTheme.EventTerminal, UiTheme.RailNeutralStrongBrush),
            ("EventSafety", ChartTheme.EventSafety, UiTheme.DangerBrush),
            ("MarkerOutline", ChartTheme.MarkerOutline, UiTheme.SurfaceBrush),
            ("TailTrack", ChartTheme.TailTrack, UiTheme.RailDownBrush),
            ("ExportPage", ChartTheme.ExportPage, UiTheme.SurfaceBrush)
        };
        foreach (var (name, actual, source) in tokens)
            Require(ReferenceEquals(actual, source) && actual.IsFrozen, $"ChartTheme.{name} 必須直接引用對應的 UiTheme 凍結畫筆。");

        var zone = ChartTheme.LimitZone.Color;
        Require(ChartTheme.LimitZone.IsFrozen && zone.A == 0x2A
                && zone.R == UiTheme.TextSubtle.R && zone.G == UiTheme.TextSubtle.G && zone.B == UiTheme.TextSubtle.B,
            "LimitZone 必須是 TextSubtle 加 0x2A 透明度的凍結畫筆。");
        foreach (var (name, dash, expected) in new (string, DoubleCollection, double[])[]
                 {
                     ("LongDash", ChartTheme.LongDash, new double[] { 5, 3 }),
                     ("ShortDot", ChartTheme.ShortDot, new double[] { 2, 3 }),
                     ("TailDash", ChartTheme.TailDash, new double[] { 4, 3 }),
                     ("PlannedDash", ChartTheme.PlannedDash, new double[] { 6, 4 }),
                     ("LegendDash", ChartTheme.LegendDash, new double[] { 3, 2 }),
                     ("LegendDot", ChartTheme.LegendDot, new double[] { 1, 1.5 })
                 })
            Require(dash.IsFrozen && dash.SequenceEqual(expected), $"ChartTheme.{name} 應為凍結的 {string.Join(",", expected)}。");
        Require(ReferenceEquals(ChartTheme.Font, Application.Current.TryFindResource("AppFont")), "圖表字型必須是 App 字型（AppFont）。");
        Require(ChartTheme.GridThickness == 1 && ChartTheme.AxisThickness == 1.2 && ChartTheme.AxisLabelFontSize == 10
                && ChartTheme.TimeTickFontSize == 9 && ChartTheme.TitleFontSize == 13 && ChartTheme.LegendFontSize == 11
                && ChartTheme.MessageFontSize == 12, "圖表線寬與字級常數不符規格。");
        Console.WriteLine("[通過] ChartTheme 色票直接引用 UiTheme 且凍結");
    }

    // 車輛色用於速度線與運行圖列車線；不得是接近灰階的顏色，否則會和灰色速限線、
    // 運行圖圖例線段、端點事件點混淆。以 RGB 最大與最小通道差（彩度）判斷。
    private static void VerifyVehiclePaletteAvoidsGrays()
    {
        foreach (var color in UiTheme.VehiclePalette)
        {
            var chroma = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
            Require(chroma >= 64, $"車輛色 {color} 太接近灰色（彩度 {chroma}），會和圖表灰色線混淆。");
        }
        Console.WriteLine("[通過] 車輛色盤避開圖表灰色");
    }

    private static void VerifyNiceTicks()
    {
        void Expect(double max, double[] expected)
        {
            var actual = ChartPainter.NiceTicks(max);
            Require(actual.Length == expected.Length && actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) < 1e-9),
                $"NiceTicks({max}) 應為 {string.Join("/", expected)}，實際 {string.Join("/", actual)}。");
        }
        Expect(80.1, [0, 20, 40, 60, 80, 100]);
        Expect(1767.7, [0, 500, 1000, 1500, 2000]);
        Expect(100, [0, 20, 40, 60, 80, 100]);
        Expect(50, [0, 10, 20, 30, 40, 50]);
        Expect(0.3, [0, 0.1, 0.2, 0.3]);
        foreach (var invalid in new[] { 0d, -5d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Expect(invalid, [0, 1]);
        foreach (var tiny in new[] { double.Epsilon, 1e-320, 4e-323 })
        {
            var ticks = ChartPainter.NiceTicks(tiny);
            Require(ticks.Length >= 2 && ticks[0] == 0 && ticks.All(double.IsFinite) && ticks[^1] >= tiny,
                $"極小正值 {tiny} 的刻度必須從 0 開始、有限且涵蓋最大值。");
        }
        var huge = ChartPainter.NiceTicks(double.MaxValue);
        Require(huge.All(double.IsFinite) && huge[^1] >= double.MaxValue * 0.999, "極大值的刻度不得溢位成無限大。");
        Console.WriteLine("[通過] ChartPainter 取整刻度（含 0、負值、NaN、無限大、極小與極大值防護）");
    }

    private static void VerifyPainterElements()
    {
        var canvas = new Canvas { Width = 600, Height = 300 };
        var area = new ChartPainter.ChartArea(42, 26, 540, 200);
        var top = ChartPainter.DrawValueAxis(canvas, area, 80.1, "km/h");
        Require(top == 100, $"DrawValueAxis 應回傳刻度最大值 100，實際 {top}。");
        var grid = canvas.Children.OfType<Line>().Where(line => Equals(line.Tag, ChartPainter.GridTag)).ToArray();
        Require(grid.Length == 5 && grid.All(line => ReferenceEquals(line.Stroke, ChartTheme.Grid) && line.StrokeThickness == 1)
                && Math.Abs(grid.Min(line => line.Y1) - area.Top) < 1e-9,
            "80.1 的數值軸應有 5 條格線（0 以外），最上面一條在繪圖區頂端，且用 Grid 畫筆。");
        var ticks = canvas.Children.OfType<TextBlock>().Where(text => Equals(text.Tag, ChartPainter.ValueTickTag)).ToArray();
        Require(ticks.Select(text => text.Text).SequenceEqual(["0", "20", "40", "60", "80", "100"])
                && ticks.All(text => ReferenceEquals(text.Foreground, ChartTheme.AxisLabel) && text.FontSize == 10
                    && Canvas.GetLeft(text) + text.DesiredSize.Width <= area.Left),
            "數值刻度文字應為 0～100、AxisLabel 色、10 px，且位於 Y 軸左側。");
        var axes = canvas.Children.OfType<Line>().Where(line => ReferenceEquals(line.Stroke, ChartTheme.Axis)).ToArray();
        Require(axes.Length == 2 && axes.All(line => line.StrokeThickness == 1.2), "兩條軸線必須用 Axis 畫筆、線寬 1.2。");
        Require(canvas.Children.OfType<TextBlock>().Any(text => text.Text == "km/h" && ReferenceEquals(text.Foreground, ChartTheme.AxisLabel)),
            "單位文字必須用 AxisLabel 色。");

        ChartPainter.DrawTimeAxis(canvas, area, [(42d, "06:00:00"), (582d, "07:00:00")]);
        var timeLabels = canvas.Children.OfType<TextBlock>().Where(text => Equals(text.Tag, ChartPainter.TimeTickTag)).ToArray();
        Require(timeLabels.Length == 2 && timeLabels.All(text => text.FontSize == 9 && ReferenceEquals(text.Foreground, ChartTheme.AxisLabel)
                    && Canvas.GetLeft(text) >= area.Left - 2 && Canvas.GetLeft(text) + text.DesiredSize.Width <= area.Right + 0.01
                    && Math.Abs(Canvas.GetTop(text) - (area.Bottom + 5)) < 1e-9),
            "時間刻度文字必須 9 px、AxisLabel 色，置中於刻度但不超出繪圖區左右界。");

        var legend = ChartPainter.DrawLegend(canvas, 50, 4,
        [
            new ChartLegendItem("實線", ChartTheme.PrimarySeries, ChartLegendMarker.Line),
            new ChartLegendItem("虛線", ChartTheme.ThresholdSeries, ChartLegendMarker.Dash),
            new ChartLegendItem("點線", ChartTheme.DangerSeries, ChartLegendMarker.Dot),
            new ChartLegendItem("色點", ChartTheme.EventStation, ChartLegendMarker.Point)
        ]);
        Require(Equals(legend.Tag, ChartPainter.LegendTag) && Canvas.GetLeft(legend) == 50 && Canvas.GetTop(legend) == 4
                && legend.Orientation == Orientation.Horizontal, "圖例必須是 Tag=ChartLegend 的水平 StackPanel，放在指定位置。");
        Require(legend.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(["實線", "虛線", "點線", "色點"])
                && legend.Children.OfType<TextBlock>().All(text => text.FontSize == 11 && ReferenceEquals(text.Foreground, ChartTheme.LegendText)),
            "圖例文字必須依序、11 px、LegendText 色。");
        var markers = legend.Children.OfType<Shape>().ToArray();
        Require(markers.Length == 4
                && markers[0] is Line { StrokeDashArray: var solid } && (solid?.Count ?? 0) == 0
                && markers[1] is Line { StrokeDashArray: var dash } && ReferenceEquals(dash, ChartTheme.LegendDash)
                && markers[2] is Line { StrokeDashArray: var dot } && ReferenceEquals(dot, ChartTheme.LegendDot)
                && markers[3] is Ellipse { Fill: var pointFill, Stroke: var pointStroke }
                && ReferenceEquals(pointFill, ChartTheme.EventStation) && ReferenceEquals(pointStroke, ChartTheme.MarkerOutline)
                && ReferenceEquals(markers[0].Stroke, ChartTheme.PrimarySeries),
            "圖例樣本必須依線型為實線／虛線／點線／白框色點，顏色與項目一致。");

        var statusDot = ChartPainter.CreateDot(ChartTheme.EventSafety);
        Require(statusDot.Width == 8 && statusDot.Height == 8 && ReferenceEquals(statusDot.Fill, ChartTheme.EventSafety)
                && ReferenceEquals(statusDot.Stroke, ChartTheme.MarkerOutline),
            "CreateDot 必須產生 8 DIP、白框的色點（圖例與最低裕度標籤共用）。");
        var title = ChartPainter.DrawTitle(canvas, "測試標題", 42, 4);
        Require(Equals(title.Tag, ChartPainter.TitleTag) && title.FontSize == 13 && title.FontWeight == FontWeights.SemiBold
                && ReferenceEquals(title.Foreground, ChartTheme.Title), "標題必須 13 px 半粗體、Title 色、Tag=ChartTitle。");
        var messageCanvas = new Canvas();
        ChartPainter.DrawMessage(messageCanvas, "尚無資料");
        var message = messageCanvas.Children.OfType<TextBlock>().Single();
        Require(messageCanvas.Children.Count == 1 && message.Text == "尚無資料" && message.FontSize == 12
                && Equals(message.Tag, ChartPainter.MessageTag) && ReferenceEquals(message.Foreground, ChartTheme.Message)
                && Canvas.GetLeft(message) == 18 && Canvas.GetTop(message) == 18,
            "空白提示必須 12 px、Message 色、Tag=ChartMessage，放在 (18, 18)。");
        var series = ChartPainter.CreateSeries(ChartTheme.DangerSeries, 1.8, ChartTheme.ShortDot);
        Require(ReferenceEquals(series.Stroke, ChartTheme.DangerSeries) && series.StrokeThickness == 1.8
                && ReferenceEquals(series.StrokeDashArray, ChartTheme.ShortDot), "CreateSeries 必須套用畫筆、線寬與線型。");
        Require(canvas.Children.OfType<TextBlock>().Concat(legend.Children.OfType<TextBlock>())
                .All(text => ReferenceEquals(text.FontFamily, ChartTheme.Font)), "所有圖表文字必須使用 App 字型。");
        Console.WriteLine("[通過] ChartPainter 軸線、刻度、圖例、標題與空白提示樣式");
    }

    private static void VerifyHeaderFitsNarrowCanvas()
    {
        ChartLegendItem[] items =
        [
            new("實際淨距", ChartTheme.PrimarySeries, ChartLegendMarker.Line),
            new("動態安全距離", ChartTheme.ThresholdSeries, ChartLegendMarker.Dash),
            new("障礙物煞車需求", ChartTheme.DangerSeries, ChartLegendMarker.Dot)
        ];
        var wide = new Canvas { Width = 900, Height = 200 };
        var (wideTitle, wideLegend) = ChartPainter.DrawHeader(wide, "FULL-O04 → FULL-O13", items, 52, 4);
        Require(wideTitle is not null && Canvas.GetLeft(wideTitle) == 52
                && Canvas.GetLeft(wideLegend) >= 52 + wideTitle.DesiredSize.Width + 16 - 0.01,
            "寬畫布：標題在左、圖例接在標題右側。");
        var narrow = new Canvas { Width = 420, Height = 200 };
        var (narrowTitle, narrowLegend) = ChartPainter.DrawHeader(narrow, "FULL-O04 → FULL-O13", items, 52, 4);
        Require(narrowTitle is null && !narrow.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.TitleTag)),
            "窄畫布放不下標題＋圖例時必須省略標題。");
        Require(Canvas.GetLeft(narrowLegend) == 52 && 52 + narrowLegend.DesiredSize.Width <= 420 - 4,
            $"窄畫布的圖例必須完整留在畫布內（寬 {narrowLegend.DesiredSize.Width:0.0}）。");
        Console.WriteLine("[通過] 標題列在窄畫布省略標題、保留完整圖例");
    }

    // 掃描範圍：方法清單為空表示整個檔案，否則只掃描列出的方法本體。後續 Task 逐步加入。
    private static readonly (string File, string[] Methods)[] ChartSources =
    [
        ("ChartTheme.cs", []),
        ("ChartPainter.cs", []),
        ("MainWindow.V2.cs", ["DrawV2SpeedProfile", "DrawSpeedLimitLabels", "DrawSpeedStopLabels", "DrawClockTimeAxis",
            "DrawSafetyDistanceChart", "AddSafetyMarginChip", "DrawTimeDistanceDiagramFull", "DrawV2Route"]),
        ("MainWindow.xaml.cs", ["DrawSpeedProfile", "DrawRoute"]),
        ("MainWindow.TimeDistance.cs", []),
        ("TimeDistanceStationLabelLayout.cs", []),
        ("DiagramExportService.cs", []),
        ("MainWindow.SpatialReferencePointDiagram.cs", [])
    ];

    private static readonly Regex HardCodedColor = new(
        @"\bColor\.From(Rgb|Argb)\b|\bBrushes\.(?!Transparent\b)[A-Z]\w*|\bColors\.(?!Transparent\b)[A-Z]\w*",
        RegexOptions.Compiled);

    private static void VerifyNoHardCodedChartColors(string root)
    {
        Require(HardCodedColor.IsMatch("Stroke = Brushes.SlateGray") && HardCodedColor.IsMatch("Color.FromRgb(1, 2, 3)")
                && HardCodedColor.IsMatch("Color.FromArgb(1, 2, 3, 4)") && HardCodedColor.IsMatch("Colors.White")
                && !HardCodedColor.IsMatch("Brushes.Transparent") && !HardCodedColor.IsMatch("UiTheme.VehicleBrushes[0]")
                && !HardCodedColor.IsMatch("SystemColors.ControlBrush"), "寫死顏色掃描規則自我檢查失敗。");
        var sample = "private void A()\n{\n    if (x) { y(); }\n}\nprivate void B() { z(); }";
        var body = MethodBody(sample, "A", "sample");
        Require(body.Contains("y();", StringComparison.Ordinal) && !body.Contains("z();", StringComparison.Ordinal),
            "方法本體擷取自我檢查失敗。");

        var findings = new List<string>();
        foreach (var (file, methods) in ChartSources)
        {
            var source = File.ReadAllText(System.IO.Path.Combine(root, "src", "MrtRouteSimulator.App", file));
            IEnumerable<(string Scope, string Text)> scopes = methods.Length == 0
                ? [(file, source)]
                : methods.Select(method => ($"{file} {method}", MethodBody(source, method, file)));
            foreach (var (scope, text) in scopes)
                findings.AddRange(HardCodedColor.Matches(text).Select(match => $"{scope}：{match.Value}"));
        }
        Require(findings.Count == 0, "圖表程式不得寫死顏色：" + string.Join("、", findings.Distinct()));
        Console.WriteLine($"[通過] 圖表程式無寫死顏色（{ChartSources.Length} 個來源檔）");
    }

    private static string MethodBody(string source, string method, string file)
    {
        var match = Regex.Match(source, @"(private|internal|public)[^;{=]*?\b" + Regex.Escape(method) + @"\s*\(");
        Require(match.Success, $"{file} 找不到方法 {method}。");
        var open = source.IndexOf('{', match.Index);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            else if (source[index] == '}' && --depth == 0) return source[open..(index + 1)];
        }
        throw new InvalidOperationException($"{file} 的 {method} 大括號不成對。");
    }

    private static void VerifyV2Charts(string root)
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            ShellLayoutTests.LoadSample(window, root, "14-大型-二十八站完整營運範例.mrtsim.json");
            window.Show();
            WpfTestWait.WaitForPlannedTimeline(window);
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawV2SpeedProfile");
            VerifySpeedChart(window, "計畫速度");
            WpfTestWait.Advance(window, 600);
            Require(WpfTestWait.LatestFrame(window).SafetyHistory.Count > 0, "範例 14 推進 600 秒後必須已有相鄰列車安全觀測。");
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawV2SpeedProfile");
            VerifySpeedChart(window, "實際速度");
            VerifySafetyChart(window);
            VerifyTimeDistanceCharts(window);
            VerifyPdfPagesKeepLegend(window);
        }
        finally { WpfTestWait.Close(window); }
    }

    private static void VerifySpeedChart(MainWindow window, string seriesLabel)
    {
        var canvas = (Canvas)window.FindName("SpeedCanvas")!;
        var vehicle = ((ComboBox)window.FindName("SpeedProfileRunComboBox")!).SelectedItem?.ToString();
        Require(vehicle is not null && canvas.ActualWidth >= 100, "速度曲線必須已選定列車且畫布已配置。");
        var parseIndex = typeof(MainWindow).GetMethod("ParseVehicleIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
        var vehicleBrush = UiTheme.VehicleBrush((int)parseIndex.Invoke(null, [vehicle])!);
        var lines = canvas.Children.OfType<Polyline>().ToArray();
        Require(lines.Length == 2, $"速度曲線應只有速限與速度兩條線，實際 {lines.Length}。");
        Require(ReferenceEquals(lines[1].Stroke, vehicleBrush) && lines[1].StrokeThickness == 2.4,
            $"{vehicle} 的速度線必須使用該列車的車輛色、線寬 2.4。");
        Require(ReferenceEquals(lines[0].Stroke, ChartTheme.LimitSeries) && ReferenceEquals(lines[0].StrokeDashArray, ChartTheme.LongDash),
            "速限線必須為 LimitSeries 灰色虛線（5,3）。");
        Require(lines.SelectMany(line => line.Points).All(point => point.Y >= 26 - 0.01),
            "速度與速限線不得高於繪圖區頂端。");
        var ticks = canvas.Children.OfType<TextBlock>().Where(text => Equals(text.Tag, ChartPainter.ValueTickTag)).ToArray();
        Require(ticks.Length >= 3 && ticks[0].Text == "0" && canvas.Children.OfType<TextBlock>().Any(text => text.Text == "km/h"),
            "速度曲線必須有從 0 開始的 km/h 數值刻度。");
        Require(canvas.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5,
            "速度曲線必須有五個時間刻度。");
        var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
        Require(legend.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual([seriesLabel, "軌道速限"]),
            $"速度曲線圖例應為「{seriesLabel}／軌道速限」。");
        Require(ReferenceEquals(((Line)legend.Children[0]).Stroke, vehicleBrush), "圖例的速度線樣本必須與速度線同色。");
        var title = canvas.Children.OfType<TextBlock>().SingleOrDefault(text => Equals(text.Tag, ChartPainter.TitleTag));
        Require(title?.Text == $"{vehicle} 速度", $"速度曲線標題應為「{vehicle} 速度」，實際「{title?.Text}」。");
        Require(canvas.Children.OfType<TextBlock>().Where(text => text.Tag is "SpeedLimitLabel" or "StopStationLabel")
                .All(text => ReferenceEquals(text.Foreground, ChartTheme.AxisLabel) && ReferenceEquals(text.FontFamily, ChartTheme.Font)),
            "速限與停站標籤必須用 AxisLabel 色與 App 字型。");
        Console.WriteLine($"[通過] 速度曲線主題（{seriesLabel}：車輛色、刻度、圖例、標題）");
    }

    private static void VerifyV1Charts()
    {
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            window.Show();
            ShellLayoutTests.PumpLayout(window);
            BuildV1Simulation(window);
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            var views = (TabControl)window.FindName("SimulationViewTabControl")!;
            views.SelectedIndex = 2;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawSpeedProfile");
            var speed = (Canvas)window.FindName("SpeedCanvas")!;
            var line = speed.Children.OfType<Polyline>().Single();
            Require(ReferenceEquals(line.Stroke, UiTheme.VehicleBrush(0)), "V1 速度線必須使用第一台車的車輛色。");
            Require(line.Points.All(point => point.Y >= 26 - 0.01), "V1 速度線不得高於繪圖區頂端。");
            Require(speed.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.ValueTickTag) && text.Text == "0")
                    && speed.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5
                    && speed.Children.OfType<StackPanel>().Any(panel => Equals(panel.Tag, ChartPainter.LegendTag)),
                "V1 速度曲線必須有數值刻度、五個時間刻度與圖例。");

            views.SelectedIndex = 0;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawRoute");
            var route = (Canvas)window.FindName("RouteCanvas")!;
            Require(ReferenceEquals(route.Children.OfType<Line>().First().Stroke, UiTheme.RailNeutralStrongBrush),
                "V1 路線軌道必須用 RailNeutralStrong。");
            var stations = route.Children.OfType<Ellipse>().ToArray();
            Require(stations.Length > 0 && stations.All(marker => ReferenceEquals(marker.Fill, UiTheme.SurfaceBrush)
                    && ReferenceEquals(marker.Stroke, UiTheme.RailNeutralStrongBrush)),
                "V1 車站點必須白底＋RailNeutralStrong 外框。");
            Require(route.Children.OfType<TextBlock>().Where(text => text.Text.Contains('\n'))
                    .All(text => ReferenceEquals(text.Foreground, UiTheme.TextStrongBrush)),
                "V1 車站名稱必須用 TextStrong。");

            // 非拓樸的線性 V2 路線圖只在沒有播放 frame 時出現；以 V1 建立的路線直接繪製。
            Require(WpfTestWait.Field(window, "_latestPlaybackFrame") is null, "V1 模式不得有 V2 播放 frame。");
            var v2Enabled = typeof(MainWindow).GetField("_v2Enabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
            v2Enabled.SetValue(window, true);
            try { WpfTestWait.Invoke(window, "DrawV2Route"); }
            finally { v2Enabled.SetValue(window, false); }
            var tracks = route.Children.OfType<Line>()
                .Where(item => item.StrokeThickness == 4 && Math.Abs(item.Y1 - item.Y2) < .01 && item.X2 - item.X1 > 100).ToArray();
            Require(tracks.Length == 2 && ReferenceEquals(tracks[0].Stroke, UiTheme.RailDownBrush)
                    && ReferenceEquals(tracks[1].Stroke, UiTheme.RailUpBrush),
                "線性路線圖的下行軌道必須用 RailDown、上行用 RailUp。");
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] V1 速度曲線、V1 路線圖與線性 V2 路線圖使用主題色");
    }

    private static void VerifySafetyStatusBrush()
    {
        var method = typeof(MainWindow).GetMethod("SafetyStatusBrush", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 SafetyStatusBrush。");
        foreach (var (status, brush) in new (SafetyStatus, SolidColorBrush)[]
                 {
                     (SafetyStatus.Safe, UiTheme.SuccessBrush),
                     (SafetyStatus.Caution, UiTheme.CautionBrush),
                     (SafetyStatus.BrakingRequired, UiTheme.CautionBrush),
                     (SafetyStatus.EnvelopeIntrusion, UiTheme.DangerBrush)
                 })
            Require(ReferenceEquals(method.Invoke(null, [status]), brush), $"安全狀態 {status} 的顏色必須與表格色點一致。");
        Console.WriteLine("[通過] 安全狀態顏色與表格色點一致（需要制動＝注意黃）");
    }

    private static void VerifySafetyChart(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SafetyTabItem");
        ((ComboBox)window.FindName("SafetyWindowComboBox")!).SelectedIndex = 2; // 全部時間
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        ShellLayoutTests.PumpLayout(window);
        WpfTestWait.Invoke(window, "DrawSafetyDistanceChart");
        var canvas = (Canvas)window.FindName("SafetyDistanceCanvas")!;
        var lines = canvas.Children.OfType<Polyline>().ToArray();
        Require(lines.Length == 3, $"距離圖應有三條線，實際 {lines.Length}。");
        Require(ReferenceEquals(lines[0].Stroke, ChartTheme.PrimarySeries) && lines[0].StrokeThickness == 2.4
                && (lines[0].StrokeDashArray?.Count ?? 0) == 0, "實際淨距必須為 PrimarySeries 實線、線寬 2.4。");
        Require(ReferenceEquals(lines[1].Stroke, ChartTheme.ThresholdSeries) && lines[1].StrokeThickness == 1.6
                && ReferenceEquals(lines[1].StrokeDashArray, ChartTheme.LongDash), "動態安全距離必須為 ThresholdSeries 虛線（5,3）、線寬 1.6。");
        Require(ReferenceEquals(lines[2].Stroke, ChartTheme.DangerSeries) && lines[2].StrokeThickness == 1.8
                && ReferenceEquals(lines[2].StrokeDashArray, ChartTheme.ShortDot), "障礙物煞車需求必須為 DangerSeries 點線（2,3）、線寬 1.8。");
        Require(lines.SelectMany(line => line.Points).All(point => point.Y >= 30 - 0.01), "距離圖的線不得高於繪圖區頂端。");
        Require(canvas.Children.OfType<TextBlock>().Any(text => Equals(text.Tag, ChartPainter.ValueTickTag) && text.Text == "0")
                && canvas.Children.OfType<TextBlock>().Any(text => text.Text == "m"), "距離圖必須有從 0 開始的 m 數值刻度。");
        Require(canvas.Children.OfType<TextBlock>().Count(text => Equals(text.Tag, ChartPainter.TimeTickTag)) == 5,
            "距離圖必須有五個時間刻度。");
        var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
        Require(legend.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(["實際淨距", "動態安全距離", "障礙物煞車需求"]),
            "距離圖圖例應為實際淨距、動態安全距離、障礙物煞車需求。");
        var chip = canvas.Children.OfType<Border>().Single(border => Equals(border.Tag, "SafetyMarginChip"));
        var chipPanel = (StackPanel)chip.Child;
        var dot = chipPanel.Children.OfType<Ellipse>().Single();
        var label = chipPanel.Children.OfType<TextBlock>().Single();
        var status = label.Text[(label.Text.LastIndexOf('｜') + 1)..];
        Require(label.Text.StartsWith("最低裕度", StringComparison.Ordinal)
                && ReferenceEquals(dot.Fill, StatusTones.Brush(StatusTones.Classify(status)))
                && ReferenceEquals(label.Foreground, ChartTheme.Title),
            $"最低裕度標籤「{label.Text}」的色點必須依狀態「{status}」取 StatusTones 色，文字用深色（Title）以便閱讀。");
        Console.WriteLine("[通過] 相鄰列車距離圖主題、刻度與最低裕度色點");
    }

    private static void VerifyTimeDistanceCharts(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("DiagramTabItem");
        ShellLayoutTests.PumpLayout(window);
        var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
        var showEvents = (CheckBox)window.FindName("ShowEventsCheckBox")!;
        try
        {
            foreach (var renderer in new[] { "DrawInteractiveTimeDistanceDiagram", "DrawTimeDistanceDiagramFull" })
            {
                foreach (var events in new[] { true, false })
                {
                    showEvents.IsChecked = events;
                    WpfTestWait.Invoke(window, renderer);
                    var context = $"{renderer}（事件點{(events ? "開" : "關")}）";
                    var title = canvas.Children.OfType<TextBlock>().Single(text => Equals(text.Tag, ChartPainter.TitleTag));
                    Require(title.Text.Contains("計畫／理論與 V2 模擬實際運行圖", StringComparison.Ordinal)
                            && ReferenceEquals(title.Foreground, ChartTheme.Title) && title.FontSize == 13 && Canvas.GetTop(title) == 8,
                        $"{context} 標題必須沿用原文字、圖表標題樣式、位於第 8 DIP。");
                    var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
                    string[] expected = events ? ["V2 實際", "計畫／理論", "站點事件", "端點事件", "安全事件"] : ["V2 實際", "計畫／理論"];
                    var legendTexts = legend.Children.OfType<TextBlock>().Select(text => text.Text).ToArray();
                    Require(legendTexts.Take(expected.Length).SequenceEqual(expected)
                            && legendTexts.Skip(expected.Length).All(text => text.StartsWith('（')),
                        $"{context} 圖例應為 {string.Join("／", expected)}，實際 {string.Join("／", legendTexts)}。");
                    var markers = canvas.Children.OfType<Ellipse>().Where(marker => marker.Visibility == Visibility.Visible).ToArray();
                    Require(events ? markers.Length > 0 : markers.Length == 0, $"{context} 事件點顯示必須跟隨勾選。");
                    Require(markers.All(marker => ReferenceEquals(marker.Stroke, ChartTheme.MarkerOutline)
                            && (ReferenceEquals(marker.Fill, ChartTheme.EventStation) || ReferenceEquals(marker.Fill, ChartTheme.EventTerminal)
                                || ReferenceEquals(marker.Fill, ChartTheme.EventSafety))),
                        $"{context} 事件點必須用 ChartTheme 事件色與白色外框。");
                    var stationLabels = canvas.Children.OfType<TextBlock>().Where(text => text.Tag is TimeDistanceStationLabelTag).ToArray();
                    Require(stationLabels.Length > 0 && stationLabels.All(text => ReferenceEquals(text.FontFamily, ChartTheme.Font)
                            && ((SolidColorBrush)text.Foreground).Color == ChartTheme.AxisLabel.Color),
                        $"{context} 車站標籤必須用 App 字型與 AxisLabel 色。");
                    var gridLines = canvas.Children.OfType<Line>().Where(line => Math.Abs(line.Y1 - line.Y2) < .01 && line.X2 > line.X1 + 100
                        && !ReferenceEquals(line.Stroke, ChartTheme.Axis)).ToArray();
                    Require(gridLines.Length > 0 && gridLines.All(line => ReferenceEquals(line.Stroke, ChartTheme.Grid)),
                        $"{context} 車站格線必須用 Grid 畫筆。");
                    var series = canvas.Children.OfType<Polyline>().Where(line => line.Points.Count > 0).ToArray();
                    Require(series.Length > 0 && series.All(line => UiTheme.VehicleBrushes.Any(brush => ReferenceEquals(brush, line.Stroke))),
                        $"{context} 列車線必須使用車輛色盤的共用畫筆。");
                }
            }

            typeof(MainWindow).GetField("_diagramLayoutKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
            WpfTestWait.Invoke(window, "DrawInteractiveTimeDistanceDiagram");
            var axis = (Canvas)window.FindName("DiagramTimeAxisCanvas")!;
            Require(axis.Children.OfType<Line>().Any() && axis.Children.OfType<Line>().All(line => ReferenceEquals(line.Stroke, ChartTheme.Axis)),
                "固定時間軸的軸線與刻度短線必須用 Axis 畫筆。");
            Require(axis.Children.OfType<TextBlock>().All(text => ReferenceEquals(text.Foreground, ChartTheme.AxisLabel)
                    && ReferenceEquals(text.FontFamily, ChartTheme.Font)),
                "固定時間軸文字必須用 AxisLabel 色與 App 字型。");
        }
        finally { showEvents.IsChecked = true; }
        Console.WriteLine("[通過] 運行圖兩條繪製路徑的標題、圖例、事件點、格線與標籤主題一致");
    }

    private static void VerifyPdfPagesKeepLegend(MainWindow window)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("DiagramTabItem");
        var zoom = (Slider)window.FindName("DiagramZoomSlider")!;
        var previousZoom = zoom.Value;
        try
        {
            zoom.Value = 3;
            ShellLayoutTests.PumpLayout(window);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            canvas.UpdateLayout();
            var legend = canvas.Children.OfType<StackPanel>().Single(panel => Equals(panel.Tag, ChartPainter.LegendTag));
            var exportType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.DiagramExportService")!;
            var render = exportType.GetMethod("Render", BindingFlags.Static | BindingFlags.NonPublic, binder: null,
                types: [typeof(FrameworkElement), typeof(double), typeof(bool)], modifiers: null)!;
            var createPages = exportType.GetMethod("CreatePdfPages", BindingFlags.Static | BindingFlags.NonPublic)!;
            var bitmap = (RenderTargetBitmap)render.Invoke(null, [canvas, 1.6d, true])!;
            var pages = (IReadOnlyList<BitmapSource>)createPages.Invoke(null, [bitmap, 794, 547, true, canvas, 1.6d])!;
            Require(pages.Count >= 2, $"放大 3 倍的運行圖應分成多頁，實際 {pages.Count} 頁。");
            var legendBounds = new Rect(Canvas.GetLeft(legend), Canvas.GetTop(legend), legend.ActualWidth, legend.ActualHeight);
            for (var index = 0; index < pages.Count; index++)
            {
                var pixels = CountPixels(pages[index], legendBounds, 1.6, ChartTheme.NeutralSeries.Color);
                Require(pixels > 10, $"PDF 第 {index + 1} 頁缺少圖例線段（圖例範圍內只有 {pixels} 個圖例色像素）。");
            }
            Console.WriteLine($"[通過] PDF 分頁每頁保留圖例（{pages.Count} 頁）");
        }
        finally
        {
            zoom.Value = previousZoom;
            typeof(MainWindow).GetField("_diagramLayoutKey", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, null);
        }
    }

    // 在頁面點陣圖的指定邏輯範圍內，計算與 color 相差不超過 4 的像素數。
    private static int CountPixels(BitmapSource page, Rect logical, double scale, Color color)
    {
        var bgra = new FormatConvertedBitmap(page, PixelFormats.Bgra32, null, 0);
        var x0 = Math.Max(0, (int)Math.Floor(logical.Left * scale));
        var y0 = Math.Max(0, (int)Math.Floor(logical.Top * scale));
        var x1 = Math.Min(bgra.PixelWidth, (int)Math.Ceiling(logical.Right * scale));
        var y1 = Math.Min(bgra.PixelHeight, (int)Math.Ceiling(logical.Bottom * scale));
        if (x1 <= x0 || y1 <= y0) return 0;
        var stride = (x1 - x0) * 4;
        var pixels = new byte[stride * (y1 - y0)];
        bgra.CopyPixels(new Int32Rect(x0, y0, x1 - x0, y1 - y0), pixels, stride, 0);
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (Math.Abs(pixels[offset] - color.B) <= 4 && Math.Abs(pixels[offset + 1] - color.G) <= 4
                && Math.Abs(pixels[offset + 2] - color.R) <= 4)
                count++;
        }
        return count;
    }

    private static void VerifySpatialReferencePointColors()
    {
        var method = typeof(MainWindow).GetMethod("SpatialReferencePointColor", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 SpatialReferencePointColor。");
        foreach (var (kind, color) in new[]
                 {
                     ("中間站", UiTheme.Success), ("站前折返", UiTheme.Accent), ("站後折返", UiTheme.RailDown),
                     ("中央避車線折返", UiTheme.VehiclePalette[0]), ("銜接點", UiTheme.RailNeutralStrong)
                 })
            Require((Color)method.Invoke(null, [kind])! == color, $"空間參考點「{kind}」必須用 {color}。");
        Console.WriteLine("[通過] 五類空間參考點幾何改用主題色");
    }

    /// <summary>以 V1 基礎物理與預設輸入建立模擬；V1 圖表測試與截圖共用。</summary>
    internal static void BuildV1Simulation(MainWindow window)
    {
        ((ComboBox)window.FindName("EngineModeComboBox")!).SelectedIndex = 0; // V1 基礎物理
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            var play = (Button)window.FindName("PlayButton")!;
            WpfTestWait.Invoke(window, "RunSimulation_Click", play, new RoutedEventArgs());
            for (var attempt = 0; attempt < 100 && !play.IsEnabled; attempt++) WpfTestWait.Wait(Task.Delay(50));
            Require(play.IsEnabled && WpfTestWait.Field(window, "_simulationEngine") is not null, "V1 模擬必須建立完成。");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
