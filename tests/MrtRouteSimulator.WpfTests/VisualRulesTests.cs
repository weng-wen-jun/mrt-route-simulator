using System.Reflection;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class VisualRulesTests
{
    private static readonly Type Presentation = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.StationSchematicPresentation")!;
    private static object? Invoke(string method, params object[] args) => Presentation.GetMethod(method)!.Invoke(null, args);

    public static void Run()
    {
        VerifyMainWindowFitsAvailableWorkArea();
        VerifyRouteCanvasWidth();
        VerifyLayoutWarningButton();

        var canvas = new Canvas { Width = 720, Height = 520 };
        canvas.Measure(new Size(720, 520)); canvas.Arrange(new Rect(0, 0, 720, 520));
        var label = new TextBlock { Text = "A\n測試站", TextAlignment = TextAlignment.Center };
        Invoke("PlaceStationLabel", label, "A", 24d, 60d, 720d);
        canvas.Children.Add(label);
        AddBody(canvas, "A", "A", 24);
        Require(Math.Abs(Canvas.GetLeft(label) + label.Width / 2 - 24) < .001, "邊界標籤必須維持月臺中心線。");
        Require(Warnings(canvas).Count == 0, "合法站名不應產生警告。");
        Canvas.SetLeft(label, Canvas.GetLeft(label) + 5);
        Require(Warnings(canvas).Any(w => w.Contains("未對齊")), "必须偵測站名偏移。");
        Invoke("PlaceStationLabel", label, "A", 24d, 510d, 720d);
        Require(Warnings(canvas).Any(w => w.Contains("邊界")), "必须偵測文字垂直越界。");
        Invoke("PlaceStationLabel", label, "A", 100d, 60d, 720d);
        Canvas.SetLeft(canvas.Children.OfType<Rectangle>().Single(), 88);
        var second = new TextBlock { Text = "B\n重疊站" };
        Invoke("PlaceStationLabel", second, "B", 110d, 60d, 720d); canvas.Children.Add(second);
        Require(Warnings(canvas).Any(w => w.Contains("重疊")), "必须偵測站名重疊。");

        var assembly = typeof(MainWindow).Assembly;
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var root = System.IO.Path.GetFullPath(Environment.GetCommandLineArgs().Length > 1 ? Environment.GetCommandLineArgs()[1] : ".");
        var output = System.IO.Path.Combine(root, "artifacts", "station-rules-visual");
        Directory.CreateDirectory(output);
        var paths = Directory.GetFiles(System.IO.Path.Combine(root, "samples"), "*.mrtsim.json", SearchOption.AllDirectories);
        Require(paths.Length >= 12, "完整範例矩陣不可缺少檔案。");
        var main = new MainWindow();
        var showLockedRoutes = (MenuItem)main.FindName("ShowLockedRoutesMenuItem");
        showLockedRoutes.IsChecked = false;
        try
        {
            foreach (var path in paths)
            foreach (var width in System.IO.Path.GetFileName(path).Equals("14-大型-二十八站完整營運範例.mrtsim.json", StringComparison.Ordinal)
                ? new[] { 720, 1200, 1370, 2512 }
                : new[] { 720, 1200 })
            foreach (var stopTime in System.IO.Path.GetFileName(path).StartsWith("11-小型-三站完整拓樸", StringComparison.Ordinal)
                ? new[] { 0d, 120d, 311.5d } : new[] { 0d })
            {
                var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
                var sampleEditor = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, "Schematic"))!;
                try
                {
                    var sampleCanvas = new Canvas { Width = width, Height = 520, Background = Brushes.White };
                    sampleCanvas.Measure(new Size(width, 520)); sampleCanvas.Arrange(new Rect(0, 0, width, 520));
                    editorType.GetMethod("DrawSchematic", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sampleEditor, [sampleCanvas]);
                    CheckAndSave(sampleCanvas, sample, System.IO.Path.Combine(output, System.IO.Path.GetFileName(path) + $"-editor-{width}.png"));
                    var runtime = TopologyProjectFormat.CreateRuntime(sample);
                    var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
                        runtime.DispatchPlan.Runs.Count, sample.Simulation.HeadwaySeconds,
                        ProfileMode: sample.Simulation.ProfileMode, MovingBlockMode: sample.Simulation.MovingBlockMode,
                        ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes,
                        ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
                    if (sample.ProjectId == "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME")
                    {
                        world.AdvanceTo(stopTime);
                        var center = world.GetTrainCenterPosition("LOCAL-01");
                        var platform = sample.Topology.Platforms.Single(p => p.PlatformId ==
                            (stopTime == 0 ? "P-W-D" : stopTime == 120 ? "P-M-D" : "P-E-D"));
                        Require(center is { } c && c.TrackEdgeId == platform.TrackEdgeId
                            && Math.Abs(c.OffsetMeters - (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2) < .01,
                            $"{stopTime}s 停靠列車車體中心必須落在實體月台中心。");
                    }
                    var routeCanvas = (Canvas)main.FindName("RouteCanvas");
                    routeCanvas.Children.Clear(); routeCanvas.Width = width; routeCanvas.Height = 400;
                    var snapshot = world.GetSnapshot();
                    var trainCenterPositions = snapshot.Trains
                        .Select(state => (state.VehicleId, Center: world.GetTrainCenterPosition(state.VehicleId)))
                        .Where(item => item.Center is not null)
                        .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
                    typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, (double)width, 400d,
                            trainCenterPositions, world.GetActiveRouteLocks()]);
                    var capture = new Canvas { Width = width, Height = routeCanvas.Height, Background = Brushes.White };
                    foreach (var child in routeCanvas.Children.Cast<UIElement>().ToArray()) { routeCanvas.Children.Remove(child); capture.Children.Add(child); }
                    CheckAndSave(capture, sample, System.IO.Path.Combine(output, System.IO.Path.GetFileName(path) + $"-main-{width}-t{stopTime}.png"),
                        stopTime == 0 ? "P-W-D" : stopTime == 120 ? "M:PASSING-ISLAND" : "P-E-D");
                    Console.WriteLine($"[通過] 全範例主圖／編輯器實體月臺對位及版面：{System.IO.Path.GetFileName(path)}/{width}");
                }
                finally { sampleEditor.Close(); }
            }
            VerifyLockedRouteOverlay(main, showLockedRoutes);
            VerifyPassingDefaultRouteOverlay(main, showLockedRoutes);
            VerifyLargeSampleSidingEntryOverlay(main, showLockedRoutes, root);
        }
        finally { main.Close(); }
        var draft = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var facility = draft.Topology.TurnbackFacilities[0];
        draft = draft with { Topology = draft.Topology with { TurnbackFacilities = Enumerable.Range(0, 20)
            .Select(i => facility with { FacilityId = $"TEST-{i}", Name = $"測試設施{i}" }).ToArray() } };
        var editor = (Window)Activator.CreateInstance(editorType, draft, Enum.Parse(pageType, "Schematic"))!;
        try
        {
            canvas.Children.Clear();
            editorType.GetMethod("DrawSchematic", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [canvas]);
            Require(canvas.Height > 520, "大量設施必須擴大圖面，不能裁掉圖例。");
            Require(canvas.Children.OfType<TextBlock>().Count(t => t.Text.StartsWith("測試設施")) == 20, "設施圖例不可遺漏。");
            foreach (var item in canvas.Children.OfType<TextBlock>().Where(t => t.Text.StartsWith("測試設施")))
                Require(Canvas.GetTop(item) + 22 <= canvas.Height, "設施图例不可超出圖面。");
        }
        finally { editor.Close(); }
    }

    private static void VerifyLockedRouteOverlay(MainWindow main, MenuItem showLockedRoutes)
    {
        var sample = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        sample = sample with { Dispatch = sample.Dispatch with
        {
            ManualTimetableRows = sample.Dispatch.ManualTimetableRows!
                .Select(row => row with { PlannedDepartureTimeSeconds = 0 }).ToArray()
        } };
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            2, 1, MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        world.Tick();
        var locked = world.GetActiveRouteLocks();
        Require(locked.Count > 0, "共用袋狀軌應提供已預約的進路顯示區段。");
        var waitingVehicleId = world.Events.Single(item => item.EventType == SimulationEventType.WaitingForResource
            && item.ResourceId == "POCKET").VehicleId;
        var snapshot = world.GetSnapshot();
        var centers = snapshot.Trains
            .Select(train => (train.VehicleId, Center: world.GetTrainCenterPosition(train.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        var routeCanvas = (Canvas)main.FindName("RouteCanvas");
        routeCanvas.Children.Clear();
        routeCanvas.Width = 1200;
        routeCanvas.Height = 400;
        var draw = typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object[] arguments = [runtime.Topology.Infrastructure, sample, snapshot, 1200d, 400d, centers, locked];

        showLockedRoutes.IsChecked = true;
        draw.Invoke(main, arguments);
        var overlays = routeCanvas.Children.OfType<Polyline>()
            .Where(line => line.ToolTip?.ToString()?.Contains("已鎖定進路\n軌道", StringComparison.Ordinal) == true)
            .ToArray();
        Require(overlays.Length == locked.Count, "開啟設定後，每段已預約進路應沿既有軌道上色。");
        Require(overlays.All(line => line.Stroke is SolidColorBrush brush
            && brush.Color.R > brush.Color.B && brush.Color.G < 170),
            "鎖定進路需使用和藍色軌道明顯區分的暖色。");
        Require(locked.Any(item => item.PreviousTraversal is not null || item.NextTraversal is not null),
            "已鎖定進路應保留分岔與匯合的實體接軌方向。");
        Require(overlays.All(line => !line.ToolTip!.ToString()!.Contains(waitingVehicleId, StringComparison.Ordinal)),
            "等待資源的列車不得顯示鎖定線。");
        var capture = new Canvas { Width = 1200, Height = 400, Background = Brushes.White };
        foreach (var child in routeCanvas.Children.Cast<UIElement>().ToArray())
        {
            routeCanvas.Children.Remove(child);
            capture.Children.Add(child);
        }
        capture.Measure(new Size(1200, 400));
        capture.Arrange(new Rect(0, 0, 1200, 400));
        capture.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1200, 400, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(capture);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(System.IO.Path.Combine("artifacts", "station-rules-visual", "locked-route-pocket-main-1200.png")))
            encoder.Save(stream);

        showLockedRoutes.IsChecked = false;
        draw.Invoke(main, arguments);
        Require(!routeCanvas.Children.OfType<Polyline>().Any(line =>
                line.ToolTip?.ToString()?.Contains("已鎖定進路", StringComparison.Ordinal) == true),
            "關閉設定後不應顯示鎖定線。");
        Console.WriteLine("[通過] 已鎖定進路顯示開關與待避列車不標示");
    }

    private static void VerifyPassingDefaultRouteOverlay(MainWindow main, MenuItem showLockedRoutes)
    {
        var sample = StationLayoutTemplateService.Build(StationLayoutTemplateKind.DoubleIslandFourTracks);
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, null, MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        IReadOnlyList<LockedRouteSegment> locked = [];
        while (world.CurrentTimeSeconds < 300)
        {
            world.Tick();
            locked = world.GetActiveRouteLocks();
            if (locked.Any(item => item.TrackEdgeId == "U1" && item.ServiceRunId == "EXP-UP")) break;
        }
        Require(locked.Any(item => item.TrackEdgeId == "U1" && item.ServiceRunId == "EXP-UP"),
            "快速車直向通過側線待避站時，需取得並顯示正線進路。");
        var snapshot = world.GetSnapshot();
        var centers = snapshot.Trains
            .Select(train => (train.VehicleId, Center: world.GetTrainCenterPosition(train.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        var canvas = (Canvas)main.FindName("RouteCanvas");
        canvas.Children.Clear();
        canvas.Width = 1200;
        canvas.Height = 400;
        showLockedRoutes.IsChecked = true;
        typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, 1200d, 400d, centers, locked]);
        var overlays = canvas.Children.OfType<Polyline>()
            .Where(line => line.ToolTip?.ToString()?.Contains("已鎖定進路\n軌道", StringComparison.Ordinal) == true)
            .ToArray();
        Require(overlays.Any(line => line.ToolTip!.ToString()!.Contains("軌道 U1 ·", StringComparison.Ordinal)
            && line.ToolTip.ToString()!.Contains("EXP-UP", StringComparison.Ordinal)),
            "道岔未轉向時，正線 edge 仍應顯示暖色鎖定進路。");
        Require(overlays.All(line => !line.ToolTip!.ToString()!.Contains("UP-1", StringComparison.Ordinal)),
            "側線待避且沒有前方進路的普通車不得顯示鎖定線。");
        var capture = new Canvas { Width = 1200, Height = 400, Background = Brushes.White };
        foreach (var child in canvas.Children.Cast<UIElement>().ToArray())
        {
            canvas.Children.Remove(child);
            capture.Children.Add(child);
        }
        capture.Measure(new Size(1200, 400));
        capture.Arrange(new Rect(0, 0, 1200, 400));
        capture.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1200, 400, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(capture);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(System.IO.Path.Combine("artifacts", "station-rules-visual", "locked-route-passing-default-main-1200.png")))
            encoder.Save(stream);
        showLockedRoutes.IsChecked = false;
        Console.WriteLine("[通過] 側線待避站直向正線鎖定進路仍標色");
    }

    private static void VerifyLargeSampleSidingEntryOverlay(
        MainWindow main, MenuItem showLockedRoutes, string root)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(System.IO.Path.Combine(
            root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, null, MovingBlockMode: sample.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        IReadOnlyList<LockedRouteSegment> locked = [];
        while (world.CurrentTimeSeconds < 1800)
        {
            world.Tick();
            locked = world.GetActiveRouteLocks();
            if (locked.Any(item => item.VehicleId == "FULL-O04"
                    && item.TrackEdgeId == "EDGE:PASS-001"
                    && item.ResourceIds.Contains("TOPOLOGY:SWITCH:NODE:SPLIT-001", StringComparer.OrdinalIgnoreCase)))
                break;
        }
        Require(locked.Any(item => item.VehicleId == "FULL-O04"
                && item.TrackEdgeId == "EDGE:PASS-001"
                && item.ResourceIds.Contains("TOPOLOGY:SWITCH:NODE:SPLIT-001", StringComparer.OrdinalIgnoreCase)),
            "大存檔 FULL-O04 進 O04 側線前應提供入口鎖定區段。");
        var snapshot = world.GetSnapshot();
        var centers = snapshot.Trains
            .Select(train => (train.VehicleId, Center: world.GetTrainCenterPosition(train.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        var canvas = (Canvas)main.FindName("RouteCanvas");
        showLockedRoutes.IsChecked = true;
        foreach (var width in new[] { 1200, 2512 })
        {
            canvas.Children.Clear();
            canvas.Width = width;
            canvas.Height = 400;
            typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, (double)width, 400d, centers, locked]);
            Require(canvas.Children.OfType<Polyline>().Any(line =>
                    line.ToolTip?.ToString()?.Contains("FULL-O04-DOWN 已鎖定進路\n軌道 EDGE:PASS-001 ·", StringComparison.Ordinal) == true),
                $"大存檔 FULL-O04 的 O04 側線入口應在 {width}px 主路線圖的既有側線軌道上標色。");
            var capture = new Canvas { Width = width, Height = 400, Background = Brushes.White };
            foreach (var child in canvas.Children.Cast<UIElement>().ToArray())
            {
                canvas.Children.Remove(child);
                capture.Children.Add(child);
            }
            capture.Measure(new Size(width, 400));
            capture.Arrange(new Rect(0, 0, width, 400));
            capture.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, 400, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(capture);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(System.IO.Path.Combine("artifacts", "station-rules-visual",
                $"locked-route-large-siding-entry-main-{width}.png"));
            encoder.Save(stream);
        }
        while (world.CurrentTimeSeconds < 3000 && !locked.Any(item =>
                   item.VehicleId == "EXPRESS-01"
                   && item.TrackEdgeId == "EDGE:DOWN:O12:O13:B-001"))
        {
            world.Tick();
            locked = world.GetActiveRouteLocks();
        }
        Require(locked.Any(item => item.VehicleId == "EXPRESS-01"
                && item.TrackEdgeId == "EDGE:DOWN:O12:O13:B-001"),
            "大存檔 O13 通過車應在入口前鎖定正線進路。");
        snapshot = world.GetSnapshot();
        centers = snapshot.Trains
            .Select(train => (train.VehicleId, Center: world.GetTrainCenterPosition(train.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        canvas.Children.Clear();
        canvas.Width = 1200;
        canvas.Height = 400;
        typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, 1200d, 400d, centers, locked]);
        var expressLines = canvas.Children.OfType<Polyline>()
            .Where(line => line.ToolTip?.ToString()?.Contains("EXPRESS-01｜EXPRESS-DOWN-01 已鎖定進路",
                StringComparison.Ordinal) == true).ToArray();
        Require(expressLines.Any(line => line.ToolTip!.ToString()!.Contains(
                "軌道 EDGE:DOWN:O12:O13:B-001 ·", StringComparison.Ordinal))
                && expressLines.All(line => !line.ToolTip!.ToString()!.Contains(
                    "軌道 EDGE:PASS-003 ·", StringComparison.Ordinal)),
            "大存檔 O13 主路線圖應直接標示快速車正線通過進路，不閃側線。");
        showLockedRoutes.IsChecked = false;
        Console.WriteLine("[通過] 大存檔 O04 普通車進側線入口於主圖標色");
        Console.WriteLine("[通過] 大存檔 O13 快速車直接於主圖標示正線進路");
    }

    private static void VerifyMainWindowFitsAvailableWorkArea()
    {
        var fit = typeof(MainWindow).GetMethod("FitWindowDimension", BindingFlags.NonPublic | BindingFlags.Static)!;
        var constrained = ((double Size, double Minimum))fit.Invoke(null, [1440d, 1180d, 1092d])!;
        Require(Math.Abs(constrained.Size - 1092d) < .001 && Math.Abs(constrained.Minimum - 1092d) < .001,
            "小於宣告最小寬度的工作區必須仍可容納主視窗。 ");

        var normal = ((double Size, double Minimum))fit.Invoke(null, [1440d, 1180d, 1600d])!;
        Require(Math.Abs(normal.Size - 1440d) < .001 && Math.Abs(normal.Minimum - 1180d) < .001,
            "足夠大的工作區必須保留原本舒適尺寸與最小尺寸。 ");

        var invalid = ((double Size, double Minimum))fit.Invoke(null, [1440d, 1180d, 0d])!;
        Require(Math.Abs(invalid.Size - 1440d) < .001 && Math.Abs(invalid.Minimum - 1180d) < .001,
            "無法取得工作區時必須保留 XAML 宣告尺寸。 ");
    }

    private static void VerifyRouteCanvasWidth()
    {
        var calculate = typeof(MainWindow).GetMethod("CalculateRouteCanvasWidth", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(double), typeof(int)], null)!;
        var fullLineAtNarrowViewport = (double)calculate.Invoke(null, [720d, 26])!;
        Require(Math.Abs(fullLineAtNarrowViewport - 2512d) < .001,
            "大型路線不得壓縮到窄視窗；應保留每站最小間距並由水平捲軸檢視。");

        var wideViewport = (double)calculate.Invoke(null, [3000d, 26])!;
        Require(Math.Abs(wideViewport - 3000d) < .001,
            "可視範圍較寬時，路線圖應填滿 viewport 而不產生不必要的水平捲動。");

        var zoomedCalculate = typeof(MainWindow).GetMethod("CalculateRouteCanvasWidth", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(double), typeof(int), typeof(double)], null)!;
        var zoomed = (double)zoomedCalculate.Invoke(null, [720d, 26, 1.5d])!;
        Require(Math.Abs(zoomed - 3768d) < .001,
            "路線圖左右縮放應放大完整水平配置，且不改變垂直比例。");
    }

    private static void VerifyLayoutWarningButton()
    {
        var canvas = new Canvas { Width = 720, Height = 520 };
        canvas.Measure(new Size(720, 520)); canvas.Arrange(new Rect(0, 0, 720, 520));
        var label = new TextBlock { Text = "A\n測試站", TextAlignment = TextAlignment.Center };
        Invoke("PlaceStationLabel", label, "A", 24d, 60d, 720d);
        canvas.Children.Add(label);
        AddBody(canvas, "A", "A", 24);
        Canvas.SetLeft(label, Canvas.GetLeft(label) + 5);

        Invoke("DrawLayoutWarnings", canvas);
        var button = canvas.Children.OfType<Button>().SingleOrDefault();
        Require(button is not null, "版面檢核警告必須以可點擊按鈕呈現。");
        Require(button!.Content is string text && text.Contains("版面檢核"),
            "版面檢核按鈕必須保留警告標題與項目數量。");
        Require(button.Tag is IReadOnlyList<string> details && details.Any(detail => detail.Contains("未對齊")),
            "版面檢核按鈕必須攜帶既有檢核資料流產生的具體訊息。");
        Require(button.ToolTip is string toolTip && toolTip.Contains("未對齊"),
            "版面檢核按鈕的提示內容必須包含具體檢核訊息。");
    }

    private static void AddBody(Canvas canvas, string station, string body, double center)
    {
        var anchorType = Presentation.GetNestedType("PlatformBodyAnchor", BindingFlags.NonPublic)!;
        var rectangle = new Rectangle { Width = 24, Height = 14, Tag = Activator.CreateInstance(anchorType, station, body) };
        Canvas.SetLeft(rectangle, center - 12); Canvas.SetTop(rectangle, 200); canvas.Children.Add(rectangle);
    }

    private static void CheckAndSave(Canvas canvas, TopologyProjectDocument sample, string path, string? stoppedBody = null)
    {
        var verifiedSidings = sample.Topology.PassingFacilities
            .Select(facility => (
                Local: sample.Topology.Platforms.FirstOrDefault(p => p.PlatformId == facility.LocalPlatformId),
                Through: sample.Topology.Platforms.FirstOrDefault(p => p.PlatformId == facility.ExpressPlatformId)))
            .Where(pair => pair.Local is not null && pair.Through is not null
                && sample.Topology.Edges.Any(local => local.TrackEdgeId == pair.Local.TrackEdgeId
                    && local.Kind is TrackEdgeKind.Siding or TrackEdgeKind.PassingTrack
                    && sample.Topology.Edges.Any(through => through.TrackEdgeId == pair.Through.TrackEdgeId
                        && through.Kind == TrackEdgeKind.Mainline
                        && local.FromNodeId == through.FromNodeId && local.ToNodeId == through.ToNodeId)))
            .Select(pair => pair.Local!.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rail in canvas.Children.OfType<Polyline>())
        {
            var railId = (rail.ToolTip as string)?.Split('\n', 2)[0];
            for (var i = 1; i + 1 < rail.Points.Count; i++)
            {
                var incoming = rail.Points[i] - rail.Points[i - 1];
                var outgoing = rail.Points[i + 1] - rail.Points[i];
                if (incoming.Length < .0001 || outgoing.Length < .0001) continue;
                var angle = Math.Abs(Vector.AngleBetween(incoming, outgoing));
                Require(verifiedSidings.Contains(railId ?? "") ? angle <= 45 + 1e-6 : angle < 45,
                    $"{path}：{rail.ToolTip} 第 {i}/{rail.Points.Count} 點 {rail.Points[i]} 存在 {angle:0.0} 度突折；起點 {rail.Points[0]}，終點 {rail.Points[^1]}。");
            }
            Require(rail.Points.All(p => p.X >= 0 && p.X <= canvas.Width), $"{path}：軌道超出畫面。");
        }
        var connectionIssues = canvas.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "TrackConnectionIssue")).ToArray();
        var drawnRails = canvas.Children.OfType<Polyline>().Where(p => p.ToolTip is string).ToArray();
        var drawnRailById = drawnRails.ToDictionary(
            rail => ((string)rail.ToolTip!).Split('\n', 2)[0],
            StringComparer.OrdinalIgnoreCase);
        Require(connectionIssues.Length == 0, $"{path}：仍有待修配置：{string.Join("；", connectionIssues.Select(t => t.ToolTip))}");
        foreach (var mainline in sample.Topology.Edges.Where(edge => edge.Kind == TrackEdgeKind.Mainline))
        {
            if (!drawnRailById.TryGetValue(mainline.TrackEdgeId, out var rail)) continue;
            var ySpan = rail.Points.Count == 0 ? 0 : rail.Points.Max(point => point.Y) - rail.Points.Min(point => point.Y);
            Require(ySpan < .51,
                $"{path}：正線 {mainline.TrackEdgeId} 必須一路直線到底（Y 偏移 {ySpan:0.###} px）。");
        }
        // A service route may legally cross from one physical mainline to the
        // other through a crossover/turnback.  The visual contract therefore
        // applies to each physical mainline edge, not to the whole traversal
        // list across such a directed connection.
        foreach (var connection in sample.Topology.DirectedConnections)
        {
            if (connection.FromTrackEdgeId == connection.ToTrackEdgeId) continue; // stationary change of cab
            var from = drawnRails.Single(p => ((string)p.ToolTip).StartsWith(connection.FromTrackEdgeId + "\n", StringComparison.Ordinal));
            var to = drawnRails.Single(p => ((string)p.ToolTip).StartsWith(connection.ToTrackEdgeId + "\n", StringComparison.Ordinal));
            var a = connection.FromDirection == TraversalDirection.Forward ? from.Points[^1] - from.Points[^2] : from.Points[0] - from.Points[1];
            var b = connection.ToDirection == TraversalDirection.Forward ? to.Points[1] - to.Points[0] : to.Points[^2] - to.Points[^1];
            var connector = drawnRails.FirstOrDefault(p => Equals(p.ToolTip, $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})"));
            if (connector is not null)
            {
                CheckJoin(a, connector.Points[1] - connector.Points[0]);
                CheckJoin(connector.Points[^1] - connector.Points[^2], b);
            }
            else if (!canvas.Children.OfType<TextBlock>().Any(t => Equals(t.Tag, "TrackConnectionIssue")
                && t.ToolTip?.ToString()?.Contains($"{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})") == true)) CheckJoin(a, b);
            void CheckJoin(Vector first, Vector second)
            {
                if (first.Length < .0001 || second.Length < .0001) return;
                var angle = Math.Abs(Vector.AngleBetween(first, second));
                var verifiedSidingJoin = verifiedSidings.Contains(connection.FromTrackEdgeId)
                    || verifiedSidings.Contains(connection.ToTrackEdgeId);
                Require(verifiedSidingJoin ? angle <= 45 + 1e-6 : angle < 45,
                    $"{path}：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection}) 接軌反折 {angle:0.0} 度。");
            }
        }
        var labels = canvas.Children.OfType<FrameworkElement>().Where(e => e.Tag?.GetType().Name == "StationLabelAnchor").ToArray();
        var bodies = canvas.Children.OfType<Rectangle>().Where(e => e.Tag?.GetType().Name == "PlatformBodyAnchor").ToArray();
        Require(bodies.Length > 0, "實際月臺圖形不可為空。");
        if (sample.ProjectId is "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME" or "SYNTHETIC-LONG-ROUTE-FULL-DEMO")
        {
            if (sample.ProjectId == "SYNTHETIC-LONG-ROUTE-FULL-DEMO")
            {
                foreach (var facility in sample.Topology.PassingFacilities)
                {
                    var localId = sample.Topology.Platforms.Single(p => p.PlatformId == facility.LocalPlatformId).TrackEdgeId;
                    var throughId = sample.Topology.Platforms.Single(p => p.PlatformId == facility.ExpressPlatformId).TrackEdgeId;
                    Require(verifiedSidings.Contains(localId), $"{path}：{facility.StationId} 側線與正線的實體關係尚未確認。");
                    var separation = Math.Abs(drawnRailById[localId].Points[drawnRailById[localId].Points.Count / 2].Y
                        - drawnRailById[throughId].Points[drawnRailById[throughId].Points.Count / 2].Y);
                    Require(separation >= 6, $"{path}：{facility.StationId} 側線與正線僅相距 {separation:0.0} px，無法辨識。");
                    var localPlatform = sample.Topology.Platforms.Single(p => p.PlatformId == facility.LocalPlatformId);
                    var sideY = drawnRailById[localId].Points[drawnRailById[localId].Points.Count / 2].Y;
                    var throughY = drawnRailById[throughId].Points[drawnRailById[throughId].Points.Count / 2].Y;
                    var bodyId = string.IsNullOrWhiteSpace(localPlatform.PlatformBodyId)
                        ? localPlatform.PlatformId : localPlatform.PlatformBodyId;
                    var body = bodies.Single(b => b.Tag?.GetType().GetProperty("StationId")?.GetValue(b.Tag)?.ToString() == facility.StationId
                        && b.Tag.GetType().GetProperty("BodyId")?.GetValue(b.Tag)?.ToString() == bodyId);
                    var bodyCenterY = Canvas.GetTop(body) + body.Height / 2;
                    Require(sideY < throughY ? bodyCenterY < sideY : bodyCenterY > sideY,
                        $"{path}：{facility.StationId} 月臺本體應位於待避線外側（側線 {sideY:0.0}、正線 {throughY:0.0}、月臺 {bodyCenterY:0.0}）。");
                    var expressPlatform = sample.Topology.Platforms.Single(p => p.PlatformId == facility.ExpressPlatformId);
                    if (!expressPlatform.AllowsPassengerService)
                    {
                        var markerBodyId = string.IsNullOrWhiteSpace(expressPlatform.PlatformBodyId)
                            ? expressPlatform.PlatformId : expressPlatform.PlatformBodyId;
                        var markerBody = bodies.Single(b => b.Tag?.GetType().GetProperty("StationId")?.GetValue(b.Tag)?.ToString() == facility.StationId
                            && b.Tag.GetType().GetProperty("BodyId")?.GetValue(b.Tag)?.ToString() == markerBodyId);
                        Require(markerBody.Opacity == 0,
                            $"{path}：{facility.StationId} 通過正線不可畫成第三、第四座月臺。");
                    }
                    if (path.Contains("-2512", StringComparison.Ordinal))
                    {
                        var siding = drawnRailById[localId];
                        Require(Math.Abs(siding.Points[^1].X - siding.Points[0].X) >= 36,
                            $"{path}：{facility.StationId} 側線沒有足夠的分岔至匯入長度。");
                        var sidingEdge = sample.Topology.Edges.Single(e => e.TrackEdgeId == localId);
                        var startIndex = (int)Math.Ceiling(localPlatform.PlatformStartOffsetMeters / sidingEdge.LengthMeters * 100);
                        var endIndex = (int)Math.Floor(localPlatform.PlatformEndOffsetMeters / sidingEdge.LengthMeters * 100);
                        Require(Math.Abs(siding.Points[endIndex].X - siding.Points[startIndex].X) >= 8,
                            $"{path}：{facility.StationId} 側線月臺旁缺少平行直線段。");
                        Require(Math.Abs(siding.Points[endIndex].Y - siding.Points[startIndex].Y) < .5,
                            $"{path}：{facility.StationId} 側線月臺旁未保持水平。");
                    }
                }
                foreach (var station in sample.Topology.PassingFacilities.GroupBy(f => f.StationId)
                    .Where(group => group.Count() == 2))
                {
                    var tracks = station.Select(f =>
                    {
                        var localId = sample.Topology.Platforms.Single(p => p.PlatformId == f.LocalPlatformId).TrackEdgeId;
                        var throughId = sample.Topology.Platforms.Single(p => p.PlatformId == f.ExpressPlatformId).TrackEdgeId;
                        return (LocalY: drawnRailById[localId].Points[drawnRailById[localId].Points.Count / 2].Y,
                            ThroughY: drawnRailById[throughId].Points[drawnRailById[throughId].Points.Count / 2].Y);
                    }).OrderBy(pair => pair.ThroughY).ToArray();
                    Require(tracks[0].LocalY < tracks[0].ThroughY
                        && tracks[0].ThroughY < tracks[1].ThroughY
                        && tracks[1].ThroughY < tracks[1].LocalY,
                        $"{path}：{station.Key} 應由上而下呈現待避線、通過正線、通過正線、待避線。");
                }
            }
            foreach (var group in bodies.GroupBy(b => (string)b.Tag.GetType().GetProperty("StationId")!.GetValue(b.Tag)!))
            {
                var centers = group.Select(b => Canvas.GetLeft(b) + b.Width / 2).ToArray();
                Require(centers.Max() - centers.Min() < .51, $"{path}：同站各股道月台中心錯列。");
            }
            var rails = canvas.Children.OfType<Polyline>().Where(p => p.ToolTip is string).ToArray();
            foreach (var connection in sample.Topology.DirectedConnections)
            {
                var from = rails.Single(p => ((string)p.ToolTip).StartsWith(connection.FromTrackEdgeId + "\n", StringComparison.Ordinal));
                var to = rails.Single(p => ((string)p.ToolTip).StartsWith(connection.ToTrackEdgeId + "\n", StringComparison.Ordinal));
                var end = connection.FromDirection == TraversalDirection.Forward ? from.Points[^1] : from.Points[0];
                var start = connection.ToDirection == TraversalDirection.Forward ? to.Points[0] : to.Points[^1];
                Require((end - start).Length < .51, $"{path}：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection}) 必須直接接軌，不可用額外連線掩蓋斷點。");
            }
            if (sample.ProjectId == "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME")
            {
                foreach (var id in new[] { "POCKET-M", "P-CROSS-OUT", "P-CROSS-RETURN", "E-CROSS-OUT", "E-CROSS-RETURN", "W-CROSS-OUT", "W-CROSS-RETURN" })
                {
                    var rail = rails.Single(p => ((string)p.ToolTip).StartsWith(id + "\n", StringComparison.Ordinal));
                    var sign = Math.Sign(rail.Points[^1].X - rail.Points[0].X);
                    Require(sign != 0, $"{path}：{id} 不能畫成垂直接軌。");
                    for (var i = 1; i < rail.Points.Count; i++)
                        Require(sign * (rail.Points[i].X - rail.Points[i - 1].X) >= -.001, $"{path}：{id} 不可在行進中反折。");
                }
                if (path.Contains("-main-", StringComparison.Ordinal))
                {
                    var train = canvas.Children.OfType<Border>().Single(b => b.ToolTip is string tip && tip.StartsWith("LOCAL-01｜"));
                    var platform = bodies.Single(b => (string)b.Tag.GetType().GetProperty("BodyId")!.GetValue(b.Tag)! == stoppedBody);
                    canvas.Measure(new Size(canvas.Width, canvas.Height));
                    canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
                    var trainCenter = train.TranslatePoint(new Point(train.ActualWidth / 2, train.ActualHeight / 2), canvas);
                    var platformCenter = platform.TranslatePoint(new Point(platform.ActualWidth / 2, platform.ActualHeight / 2), canvas);
                    Require(Math.Abs(trainCenter.X - platformCenter.X) < .51,
                        $"{path}：停靠列車圖示未對齊月台中心，相差 {trainCenter.X - platformCenter.X:0.###} px。");
                }
            }
        }
        var numbers = canvas.Children.OfType<TextBlock>().Where(e => e.Tag?.GetType().Name == "PlatformNumberAnchor").ToArray();
        foreach (var platform in sample.Topology.Platforms)
            Require(numbers.Any(n => (string)n.Tag.GetType().GetProperty("PlatformId")!.GetValue(n.Tag)! == platform.PlatformId),
                $"{path}：月台 {platform.PlatformId} 未繪製，不能只檢查剩餘圖形就判定通過。");
        foreach (var body in bodies)
        {
            var station = (string)body.Tag.GetType().GetProperty("StationId")!.GetValue(body.Tag)!;
            var stationBodies = bodies.Where(candidate => (string)candidate.Tag.GetType().GetProperty("StationId")!.GetValue(candidate.Tag)! == station).ToArray();
            var left = stationBodies.Min(candidate => Canvas.GetLeft(candidate));
            var right = stationBodies.Max(candidate => Canvas.GetLeft(candidate) + candidate.Width);
            var matchingLabels = labels.Where(label => (string)label.Tag.GetType().GetProperty("StationId")!.GetValue(label.Tag)! == station).ToArray();
            Require(matchingLabels.Length == 1, $"{path}：{station} 應只有一個主站名，不能按停靠／通過股重複繪製。");
            Require(Math.Abs(Canvas.GetLeft(matchingLabels[0]) + matchingLabels[0].Width / 2 - (left + right) / 2) < .51,
                $"{path}：{station} 的主站名未對齊所有月臺本體的視覺中心。");
        }
        var warnings = Warnings(canvas);
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width), (int)Math.Ceiling(canvas.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        Require(warnings.Count == 0, $"{path}：{string.Join("；", warnings)}");
        // 移動真正的月臺圖形而保留標籤Tag，檢核仍須抓到，避免自我驗證。
        var moved = bodies[0]; var oldLeft = Canvas.GetLeft(moved); Canvas.SetLeft(moved, oldLeft + 9);
        Require(Warnings(canvas).Any(w => w.Contains("未對齊")), "移動實際月臺必須觸發站名未對齊。");
        Canvas.SetLeft(moved, oldLeft);
    }

    private static IReadOnlyList<string> Warnings(Canvas canvas) => (IReadOnlyList<string>)Invoke("ValidateStationLabels", canvas)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
