using System.IO;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using IoPath = System.IO.Path;

internal static class CapacityChartTests
{
    public static void Run(string root)
    {
        var samplePath = IoPath.Combine(root, "samples", "handbook-capacity", "07-範例七-四點瓶頸.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var settings = document.SpatialCapacity
            ?? throw new InvalidOperationException("例七必須包含 SpatialCapacity 設定。");
        Require(settings.CompareAsSingleScenario, "例七必須啟用同一容量情境瓶頸比較。");

        var output = IoPath.Combine(root, "artifacts", "capacity-chart-ui");
        Directory.CreateDirectory(output);

        VerifyConfiguredScenario(document, output);
        VerifySnapshotComparison(root, document, output);
        VerifyIndependentScenario(document with
        {
            SpatialCapacity = settings with { CompareAsSingleScenario = false }
        }, output);

        Console.WriteLine("[通過] 容量結果圖表方向、容量柱、情境瓶頸線、無快照比較與 PNG/PDF/CSV 匯出");
    }

    private static void VerifyConfiguredScenario(TopologyProjectDocument document, string output)
    {
        var settings = document.SpatialCapacity!;
        var window = CreateWindow(document);
        try
        {
            var direction = Named<ComboBox>(window, "CapacityChartDirection");
            var tabs = Named<TabControl>(window, "CapacityResultTabs");
            var chart = Named<Canvas>(window, "CapacityResultChart");
            var table = Named<DataGrid>(window, "CapacityResultTable");
            Require(direction.Items.Count == 2 && direction.Items[0] as string == "順行"
                && direction.Items[1] as string == "逆行", "容量圖表方向選擇器必須提供順行／逆行。");
            Require(tabs.Items.Count == 5, "容量結果必須有結果表、路線、設計、可達成及比較五個分頁。");
            Require(tabs.SelectedIndex == 0 && table.Visibility == Visibility.Visible,
                "容量結果視窗初始頁必須顯示結果表。");

            var forwardValues = Values(document, SpatialCapacityDirection.Forward);
            var reverseValues = Values(document, SpatialCapacityDirection.Reverse);
            Require(forwardValues.Length == settings.Points.Length && reverseValues.Length == settings.Points.Length,
                "容量圖表方向資料筆數必須與參考點一致。");

            tabs.SelectedIndex = 1;
            direction.SelectedIndex = 0;
            window.UpdateLayout();
            var forwardBars = CapacityBars(chart);
            VerifyBarRatios(forwardBars, forwardValues.Select(value => (double)value.LineCapacityPerHour).ToArray(), "順行路線容量");
            VerifyBottleneckLine(chart, forwardBars, forwardValues.Select(value => (double)value.LineCapacityPerHour).ToArray(),
                SpatialCapacityScenarioAnalysis.Calculate(settings).Single(result => result.Direction == SpatialCapacityDirection.Forward).Result.LineCapacityPerHour,
                "順行路線容量");
            SaveCanvas(chart, IoPath.Combine(output, "route-capacity-forward.png"));
            SaveCanvas(window, IoPath.Combine(output, "capacity-results-window.png"));

            var precision = Named<ComboBox>(window, "CapacityDisplayPrecision");
            precision.SelectedIndex = 1;
            window.UpdateLayout();
            var rawRates = forwardValues.Select(v => 3600 / v.DesignHeadwaySeconds).ToArray();
            VerifyBarRatios(CapacityBars(chart), rawRates, "未截斷路線容量");
            var rawBottleneck = 3600 / SpatialCapacityScenarioAnalysis.Calculate(settings).Single(r => r.Direction == SpatialCapacityDirection.Forward).Result.DesignHeadwaySeconds;
            VerifyBottleneckLine(chart, CapacityBars(chart), rawRates, rawBottleneck, "未截斷瓶頸");
            var rows = table.Items.Cast<CapacityResultsWindow.CapacityTableRow>().ToArray();
            for (var index = 0; index < rawRates.Length; index++)
            {
                var text = rawRates[index].ToString("0.000", System.Globalization.CultureInfo.CurrentCulture);
                Require(rows[index].Line == text, "未截斷表格必須顯示3位小數。");
                Require(chart.Children.OfType<TextBlock>().Any(t => t.Text == text), "未截斷柱標籤必須保留3位小數。");
            }
            SaveCanvas(chart, IoPath.Combine(output, "route-capacity-forward-decimal.png"));
            var decimalCsv = IoPath.Combine(output, "capacity-forward-decimal.csv");
            window.ExportTo(decimalCsv);
            Require(File.ReadAllText(decimalCsv).Contains("未截斷（3 位小數）"), "CSV須標示容量顯示模式。");
            precision.SelectedIndex = 0;
            window.UpdateLayout();
            VerifyBarRatios(CapacityBars(chart), forwardValues.Select(v => (double)v.LineCapacityPerHour).ToArray(), "切回截斷容量");
            direction.SelectedIndex = 1;
            window.UpdateLayout();
            var reverseBars = CapacityBars(chart);
            VerifyBarRatios(reverseBars, reverseValues.Select(value => (double)value.LineCapacityPerHour).ToArray(), "逆行路線容量");
            Require(forwardBars.Zip(reverseBars, (left, right) => Math.Abs(left.Height - right.Height))
                .Any(delta => delta > 0.01), "切換方向後容量柱高度必須反映 Engine 的順／逆行結果。");
            VerifyBottleneckLine(chart, reverseBars, reverseValues.Select(value => (double)value.LineCapacityPerHour).ToArray(),
                SpatialCapacityScenarioAnalysis.Calculate(settings).Single(result => result.Direction == SpatialCapacityDirection.Reverse).Result.LineCapacityPerHour,
                "逆行路線容量");

            tabs.SelectedIndex = 2;
            direction.SelectedIndex = 0;
            window.UpdateLayout();
            var designBars = CapacityBars(chart);
            VerifyBarRatios(designBars, forwardValues.Select(value => (double)value.DesignPassengerCapacityPerHour).ToArray(), "順行設計容量");
            VerifyBottleneckLine(chart, designBars, forwardValues.Select(value => (double)value.DesignPassengerCapacityPerHour).ToArray(),
                SpatialCapacityScenarioAnalysis.Calculate(settings).Single(result => result.Direction == SpatialCapacityDirection.Forward).Result.DesignPassengerCapacityPerHour,
                "順行設計容量");
            SaveCanvas(chart, IoPath.Combine(output, "design-capacity-forward.png"));

            tabs.SelectedIndex = 3;
            window.UpdateLayout();
            var achievableBars = CapacityBars(chart);
            VerifyBarRatios(achievableBars, forwardValues.Select(value => (double)value.AchievablePassengerCapacityPerHour).ToArray(), "順行可達成容量");
            VerifyBottleneckLine(chart, achievableBars, forwardValues.Select(value => (double)value.AchievablePassengerCapacityPerHour).ToArray(),
                SpatialCapacityScenarioAnalysis.Calculate(settings).Single(result => result.Direction == SpatialCapacityDirection.Forward).Result.AchievablePassengerCapacityPerHour,
                "順行可達成容量");

            tabs.SelectedIndex = 0;
            window.UpdateLayout();
            var csv = IoPath.Combine(output, "capacity-forward.csv");
            window.ExportTo(csv);
            VerifyCsv(csv, "順行");

            tabs.SelectedIndex = 1;
            window.UpdateLayout();
            var png = IoPath.Combine(output, "capacity-forward-export.png");
            window.ExportTo(png);
            VerifyPng(png, "容量 PNG");
            var pdf = IoPath.Combine(output, "capacity-forward-export.pdf");
            window.ExportTo(pdf);
            VerifyPdf(pdf, "容量 PDF");
        }
        finally
        {
            window.Close();
        }
    }

    private static void VerifySnapshotComparison(string root, TopologyProjectDocument sourceDocument, string output)
    {
        var playbackWindow = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                playbackWindow, "ConfigureTopologyProjectForPlaybackAsync", sourceDocument, true));
            WpfTestWait.WaitForPlannedTimeline(playbackWindow);
            var actualFrame = WpfTestWait.LatestFrame(playbackWindow);
            var stations = actualFrame.Context.TopologyInfrastructure?.Stations
                ?? throw new InvalidOperationException("容量 controlled fixture 找不到 topology station。");
            var stationId = stations.Keys.First();
            var sourceSettings = sourceDocument.SpatialCapacity!;
            var mappedSettings = sourceSettings with
            {
                Points = sourceSettings.Points
                    .Select(point => point with { StationId = stationId })
                    .ToArray()
            };
            var viewDocument = sourceDocument with { SpatialCapacity = mappedSettings };
            var controlledFrame = actualFrame with
            {
                SimulationTimeSeconds = 3600,
                NewEvents = ImmutableArray<SimulationEvent>.Empty,
                Events = ImmutableList.CreateRange(BuildControlledEvents(stationId))
            };

            var window = CreateWindow(viewDocument, () => controlledFrame);
            try
            {
                var direction = Named<ComboBox>(window, "CapacityChartDirection");
                var eventKind = Named<ComboBox>(window, "CapacityEventKind");
                var start = Named<TextBox>(window, "CapacityWindowStart");
                var end = Named<TextBox>(window, "CapacityWindowEnd");
                var tabs = Named<TabControl>(window, "CapacityResultTabs");
                var chart = Named<Canvas>(window, "CapacityResultChart");
                var table = Named<DataGrid>(window, "CapacityResultTable");
                tabs.SelectedIndex = 4;

                SetObservationWindow(direction, eventKind, start, end, forward: true, arrivals: false, 0, 60);
                window.RefreshResults();
                window.UpdateLayout();
                VerifyObservedValues(table, 3, "順行離站事件（每小時）");
                Require(GreenBars(chart).Length == mappedSettings.Points.Length
                    && GreenBars(chart).All(bar => bar.Height > 0),
                    "有順行離站快照時比較圖必須繪製正值實測柱。");
                SaveCanvas(chart, IoPath.Combine(output, "comparison-with-data.png"));

                SetObservationWindow(direction, eventKind, start, end, forward: false, arrivals: false, 0, 60);
                window.RefreshResults();
                window.UpdateLayout();
                VerifyObservedValues(table, 1, "逆行離站事件（每小時）");
                Require(GreenBars(chart).Length == mappedSettings.Points.Length
                    && GreenBars(chart).All(bar => bar.Height > 0),
                    "切換逆行後比較圖必須反映逆行快照事件流率。");

                SetObservationWindow(direction, eventKind, start, end, forward: true, arrivals: true, 0, 60);
                window.RefreshResults();
                window.UpdateLayout();
                VerifyObservedValues(table, 2, "順行到站事件（每小時）");

                // The 3600-second boundary is half-open and must not inflate either event count.
                SetObservationWindow(direction, eventKind, start, end, forward: true, arrivals: false, 0, 60);
                window.RefreshResults();
                VerifyObservedValues(table, 3, "順行離站事件邊界排除");

                // A valid empty window is a measured zero and therefore still gets a green zero bar.
                SetObservationWindow(direction, eventKind, start, end, forward: true, arrivals: false, 10, 11);
                window.RefreshResults();
                window.UpdateLayout();
                VerifyObservedValues(table, 0, "合法空窗口");
                var zeroBars = GreenBars(chart);
                Require(zeroBars.Length == mappedSettings.Points.Length && zeroBars.All(bar => bar.Height == 0),
                    "合法空窗口必須保留實測綠柱且高度為 0，不能與缺資料混淆。");

                // An incomplete window must clear a previously observed value and remove its green bars.
                SetObservationWindow(direction, eventKind, start, end, forward: true, arrivals: false, 0, 61);
                window.RefreshResults();
                window.UpdateLayout();
                Require(ObservedValues(table).All(value => value is null),
                    "結束時間超過 snapshot 時必須清除舊的 ObservedValue。");
                Require(GreenBars(chart).Length == 0
                    && CapacityBars(chart).Length == mappedSettings.Points.Length,
                    "無效窗口必須移除舊實測綠柱，保留理論柱並顯示缺值。");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            WpfTestWait.Close(playbackWindow);
        }
    }

    private static IReadOnlyList<SimulationEvent> BuildControlledEvents(string stationId) =>
    [
        Event(60, SimulationEventType.Departure, TrainDirection.Outbound, stationId, "F-1"),
        Event(1800, SimulationEventType.Departure, TrainDirection.Outbound, stationId, "F-2"),
        Event(2000, SimulationEventType.Departure, TrainDirection.Outbound, stationId, "F-2"),
        Event(3599, SimulationEventType.Departure, TrainDirection.Outbound, stationId, "F-3"),
        Event(3600, SimulationEventType.Departure, TrainDirection.Outbound, stationId, "F-boundary"),
        Event(300, SimulationEventType.Arrival, TrainDirection.Outbound, stationId, "FA-1"),
        Event(2100, SimulationEventType.Arrival, TrainDirection.Outbound, stationId, "FA-2"),
        Event(3600, SimulationEventType.Arrival, TrainDirection.Outbound, stationId, "FA-boundary"),
        Event(900, SimulationEventType.Departure, TrainDirection.Inbound, stationId, "R-1"),
        Event(3600, SimulationEventType.Departure, TrainDirection.Inbound, stationId, "R-boundary"),
        Event(1500, SimulationEventType.Arrival, TrainDirection.Inbound, stationId, "RA-1"),
        Event(3600, SimulationEventType.Arrival, TrainDirection.Inbound, stationId, "RA-boundary")
    ];

    private static SimulationEvent Event(double time, SimulationEventType eventType,
        TrainDirection direction, string stationId, string serviceRunId) =>
        new(time, eventType, $"vehicle-{serviceRunId}", null, direction, "CAPACITY-FIXTURE",
            0, 0, $"controlled {serviceRunId}", ServiceRunId: serviceRunId, StationId: stationId);

    private static void SetObservationWindow(ComboBox direction, ComboBox eventKind,
        TextBox start, TextBox end, bool forward, bool arrivals, double fromMinutes, double toMinutes)
    {
        direction.SelectedIndex = forward ? 0 : 1;
        eventKind.SelectedIndex = arrivals ? 1 : 0;
        start.Text = fromMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        end.Text = toMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void VerifyObservedValues(DataGrid table, double expected, string label)
    {
        var values = ObservedValues(table);
        Require(values.Length > 0 && values.All(value => value is { } number && Math.Abs(number - expected) < 1e-9),
            $"{label} ObservedValue 不符：預期 {expected:R}，實際 [{string.Join(", ", values.Select(value => value?.ToString("R") ?? "null"))}]。");
    }

    private static double?[] ObservedValues(DataGrid table) => table.Items.Cast<object>()
        .Select(row => row.GetType().GetProperty("ObservedValue")?.GetValue(row) is double value ? value : (double?)null)
        .ToArray();

    private static Rectangle[] GreenBars(Canvas chart) => chart.Children.OfType<Rectangle>()
        .Where(rectangle => rectangle.Fill is SolidColorBrush brush && brush.Color == Colors.SeaGreen)
        .ToArray();

    private static void VerifyIndependentScenario(TopologyProjectDocument document, string output)
    {
        var window = CreateWindow(document);
        try
        {
            var tabs = Named<TabControl>(window, "CapacityResultTabs");
            var chart = Named<Canvas>(window, "CapacityResultChart");
            var table = Named<DataGrid>(window, "CapacityResultTable");
            tabs.SelectedIndex = 4;
            window.UpdateLayout();

            Require(!chart.Children.OfType<Line>().Any(line => Equals(line.Tag, "CapacityBottleneckLine")),
                "CompareAsSingleScenario=false 時比較圖不得繪製情境瓶頸線。");
            Require(chart.Children.OfType<Rectangle>().Count(rectangle => Equals(rectangle.Tag, "CapacityBar"))
                == document.SpatialCapacity!.Points.Length,
                "無快照比較圖每個參考點只能有一根理論容量柱，缺值不可當作零再畫實測柱。");
            Require(chart.Children.OfType<TextBlock>().Count(text => text.Text == "—")
                >= document.SpatialCapacity.Points.Length,
                "無快照比較圖必須以 — 顯示缺值，不得顯示為 0。");
            Require(table.Items.Cast<object>().All(row => row.GetType().GetProperty("ObservedValue")?.GetValue(row) is null),
                "無快照比較結果的 ObservedValue 必須是 null，不得以零填補缺值。");
            SaveCanvas(chart, IoPath.Combine(output, "comparison-no-data.png"));
        }
        finally
        {
            window.Close();
        }
    }

    private static CapacityResultsWindow CreateWindow(TopologyProjectDocument document, Func<PlaybackFrame?>? snapshotProvider = null)
    {
        var window = new CapacityResultsWindow(document, snapshotProvider);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static SpatialCapacityResult[] Values(TopologyProjectDocument document, SpatialCapacityDirection direction) =>
        document.SpatialCapacity!.Points
            .Select(point => SpatialCapacityAnalysis.Calculate(
                SpatialCapacityProjectAnalysis.ToDefinition(point),
                document.SpatialCapacity.Global,
                document.SpatialCapacity.Train,
                direction,
                document.SpatialCapacity.Mode))
            .ToArray();

    private static Rectangle[] CapacityBars(Canvas chart) =>
        chart.Children.OfType<Rectangle>().Where(rectangle => Equals(rectangle.Tag, "CapacityBar")).ToArray();

    private static void VerifyBarRatios(IReadOnlyList<Rectangle> bars, IReadOnlyList<double> expected, string label)
    {
        Require(bars.Count == expected.Count, $"{label}容量柱數量不符：{bars.Count}／{expected.Count}。");
        var actualMax = bars.Max(bar => bar.Height);
        var expectedMax = expected.Max();
        Require(actualMax > 0 && expectedMax > 0, $"{label}容量柱高度必須為正值。");
        for (var index = 0; index < bars.Count; index++)
        {
            var actualRatio = bars[index].Height / actualMax;
            var expectedRatio = expected[index] / expectedMax;
            Require(Math.Abs(actualRatio - expectedRatio) < 1e-6,
                $"{label}第 {index} 根容量柱高度未依 Engine 數值換算：實際比例={actualRatio:R}、預期比例={expectedRatio:R}。");
        }
    }

    private static void VerifyBottleneckLine(Canvas chart, IReadOnlyList<Rectangle> bars,
        IReadOnlyList<double> expected, double bottleneck, string label)
    {
        var lines = chart.Children.OfType<Line>().Where(line => Equals(line.Tag, "CapacityBottleneckLine")).ToArray();
        Require(lines.Length == 1, $"{label} CompareAsSingleScenario=true 時必須有一條瓶頸線。");
        var line = lines[0];
        Require(line.Stroke is SolidColorBrush brush && brush.Color == Colors.Firebrick,
            $"{label}瓶頸線必須使用 Firebrick 顏色。");
        var actualMaxHeight = bars.Max(bar => bar.Height);
        var expectedRatio = bottleneck / expected.Max();
        var actualRatio = (345 - line.Y1) / actualMaxHeight;
        Require(Math.Abs(actualRatio - expectedRatio) < 1e-6,
            $"{label}瓶頸線高度未對齊 Engine 瓶頸容量：實際比例={actualRatio:R}、預期比例={expectedRatio:R}。");
    }

    private static void SaveCanvas(FrameworkElement canvas, string path)
    {
        canvas.Measure(new Size(canvas.Width, canvas.Height));
        canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width), (int)Math.Ceiling(canvas.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void VerifyPng(string path, string label)
    {
        Require(File.Exists(path) && new FileInfo(path).Length > 100, $"{label}檔案不存在或內容過短。");
        using var stream = File.OpenRead(path);
        var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
        Require(frame.PixelWidth > 0 && frame.PixelHeight > 0, $"{label}必須有有效影像尺寸。");
    }

    private static void VerifyPdf(string path, string label)
    {
        Require(File.Exists(path) && new FileInfo(path).Length > 100, $"{label}檔案不存在或內容過短。");
        var bytes = File.ReadAllBytes(path);
        Require(bytes.Length >= 8 && System.Text.Encoding.ASCII.GetString(bytes, 0, 8) == "%PDF-1.4",
            $"{label}必須以 PDF-1.4 標頭開頭。");
        Require(System.Text.Encoding.ASCII.GetString(bytes).Contains("/Subtype /Image", StringComparison.Ordinal),
            $"{label}必須包含圖表影像物件。");
    }

    private static void VerifyCsv(string path, string direction)
    {
        Require(File.Exists(path) && new FileInfo(path).Length > 100, "容量 CSV 檔案不存在或內容過短。");
        var text = File.ReadAllText(path);
        Require(text.Contains($"方向：{direction}", StringComparison.Ordinal)
            && text.Contains("參考點", StringComparison.Ordinal)
            && text.Contains("實測列每小時", StringComparison.Ordinal),
            "容量 CSV 必須包含方向與結果欄位標題。");
    }

    private static T Named<T>(Window window, string name) where T : FrameworkElement =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"找不到容量結果元件 {name}。");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
