using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class TimeDistanceVisualTests
{
    private static readonly HashSet<SimulationEventType> DiagramMarkerEvents =
    [
        SimulationEventType.Departure,
        SimulationEventType.Arrival,
        SimulationEventType.StationPassed,
        SimulationEventType.TurnaroundStarted,
        SimulationEventType.TailTrackReached,
        SimulationEventType.TailTrackReturnStarted,
        SimulationEventType.DirectionChanged,
        SimulationEventType.ServiceEnded,
        SimulationEventType.DepartureDelayed,
        SimulationEventType.WaitingForResource,
        SimulationEventType.ObstacleEmergencyStop,
        SimulationEventType.PredictedCollision,
        SimulationEventType.Collision,
        SimulationEventType.SafetyStatusChanged
    ];

    public static void Run(string root)
    {
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        RunIncrementalVisualChecks(samplePath);
        Console.WriteLine("PASS WPF time-distance incremental visuals");
    }

    /// <summary>
    /// Runs the retained full renderer five times for the profile's explicit full-render subphase.
    /// The helper is public so the WPF harness can expose it as an optional profiling command
    /// without coupling the normal regression suite to a large sample.
    /// </summary>
    public static void RunFullProfile(string samplePath)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.GetFullPath(samplePath)));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.WaitForPlannedTimeline(window);
            window.Show();
            window.UpdateLayout();
            window.EnablePlaybackDiagnostics();
            SelectWorkspaceTab(window, "DiagramTabItem");
            PrepareDiagramWindow(window);
            SelectWorkspaceTab(window, "ResultsTabItem");

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(600));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);

            SelectWorkspaceTab(window, "DiagramTabItem");
            window.UpdateLayout();
            window.ResetPlaybackDiagnostics();
            for (var index = 0; index < 5; index++)
            {
                WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            }

            var snapshot = window.PlaybackDiagnostics.Snapshot();
            Console.WriteLine("TimeDistance full-render subphase:");
            Console.WriteLine(snapshot.FormatReport());
            Console.WriteLine("TIME_DISTANCE_FULL_PROFILE_JSON="
                + JsonSerializer.Serialize(snapshot));
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void RunIncrementalVisualChecks(string samplePath)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.GetFullPath(samplePath)));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.WaitForPlannedTimeline(window);
            window.Show();
            window.UpdateLayout();
            window.EnablePlaybackDiagnostics();
            SelectWorkspaceTab(window, "DiagramTabItem");
            PrepareDiagramWindow(window);
            ConfigureDiagramFilters(window);
            SelectWorkspaceTab(window, "ResultsTabItem");

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var initialFrame = WpfTestWait.LatestFrame(window);
            Require(initialFrame.Trajectory.Count > 0, "baseline 30 秒應產生 time-distance trajectory。");
            var initialTrajectoryCount = initialFrame.Trajectory.Count;

            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            var initialVisuals = CaptureVisuals(canvas, initialFrame);
            var showEvents = (CheckBox)window.FindName("ShowEventsCheckBox")!;
            Require(showEvents.IsChecked == true,
                "ShowEventsCheckBox 預設必須勾選，確保既有運行圖事件標記行為不變。");
            var staticLine = canvas.Children.OfType<Line>().FirstOrDefault()
                ?? throw new InvalidOperationException("incremental renderer 未建立 static line。");
            var staticText = canvas.Children.OfType<TextBlock>().FirstOrDefault()
                ?? throw new InvalidOperationException("incremental renderer 未建立 static text。");
            var staticBuilds = GetStaticBuilds(window);
            Require(staticBuilds > 0, "第一次 incremental render 必須建立 static layer。");
            Require(initialVisuals.SeriesEndpoints.Count > 0, "incremental renderer 未建立 trajectory series。");
            Require(initialVisuals.MarkerCount == CountMarkers(initialFrame),
                "incremental event marker 數量與 frame 事件語意不一致。");

            // A repeated draw over the same frame is a true no-op: no visual update and no object
            // replacement are allowed when neither data nor layout changed.
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var repeatedSnapshot = window.PlaybackDiagnostics.Snapshot();
            Require(repeatedSnapshot.GetCount("TimeDistance.VisualUpdateCount") == 0,
                "相同 frame 重畫不得產生 VisualUpdate。");
            Require(ReferenceEquals(staticLine, canvas.Children.OfType<Line>().First()),
                "相同 frame 重畫不得替換 static line。");
            Require(ReferenceEquals(staticText, canvas.Children.OfType<TextBlock>().First()),
                "相同 frame 重畫不得替換 static text。");

            // New trajectory samples with the same axis bucket append to the existing series and
            // reuse the static layer.
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(40));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var appendedFrame = WpfTestWait.LatestFrame(window);
            Require(appendedFrame.Trajectory.Count > initialTrajectoryCount,
                "baseline 40 秒應比 30 秒增加 trajectory samples。");
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var appendedSnapshot = window.PlaybackDiagnostics.Snapshot();
            Require(appendedSnapshot.GetCount("TimeDistance.StaticRebuildCount") == 0,
                "同一 axes bucket 的新 trajectory 不得重建 static layer。");
            Require(appendedSnapshot.GetCount("TimeDistance.VisualUpdateCount") == 1,
                "新增 trajectory 應只更新一次 dynamic visual。");
            Require(ReferenceEquals(staticLine, canvas.Children.OfType<Line>().First()),
                "新增 trajectory 不得替換 static line。");
            Require(ReferenceEquals(staticText, canvas.Children.OfType<TextBlock>().First()),
                "新增 trajectory 不得替換 static text。");

            // Event markers are presentation-only.  Toggling them on a paused frame must not
            // change the retained frame, trajectory source counts, or trajectory geometry.
            var eventOnFrame = appendedFrame;
            var eventOnVisuals = CaptureVisuals(canvas, eventOnFrame);
            var eventOnTrajectoryCount = eventOnFrame.Trajectory.Count;
            var eventOnSourceCount = eventOnFrame.Events.Count;
            Require(eventOnVisuals.MarkerCount > 0,
                "event toggle regression 需要 paused frame 至少有一個可顯示事件標記。");
            Require(HasEventLegend(canvas),
                "事件標記開啟時，incremental renderer 必須顯示圓點 legend 說明。");

            showEvents.IsChecked = false;
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var eventOffVisuals = CaptureVisuals(canvas, eventOnFrame);
            Require(eventOffVisuals.MarkerCount == 0,
                "取消事件標記後，incremental renderer 不得保留可見 Ellipse。");
            Require(!HasEventLegend(canvas),
                "取消事件標記後，incremental renderer 不得顯示圓點 legend 說明。");
            RequireSameSeriesEndpoints(eventOnVisuals, eventOffVisuals,
                "incremental event toggle off");
            Require(eventOnFrame.Trajectory.Count == eventOnTrajectoryCount
                    && eventOnFrame.Events.Count == eventOnSourceCount,
                "取消事件標記不得改變 paused frame 的 trajectory/event source counts。");

            showEvents.IsChecked = true;
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var eventOnAgainVisuals = CaptureVisuals(canvas, eventOnFrame);
            Require(eventOnAgainVisuals.MarkerCount == CountMarkers(eventOnFrame),
                "重新開啟事件標記後，incremental renderer 必須恢復 frame 事件標記。");
            Require(HasEventLegend(canvas),
                "重新開啟事件標記後，incremental renderer 必須恢復圓點 legend 說明。");
            RequireSameSeriesEndpoints(eventOnVisuals, eventOnAgainVisuals,
                "incremental event toggle on");
            Require(eventOnFrame.Trajectory.Count == eventOnTrajectoryCount
                    && eventOnFrame.Events.Count == eventOnSourceCount,
                "重新開啟事件標記不得改變 paused frame 的 trajectory/event source counts。");

            // The retained full renderer must observe the same presentation toggle while
            // retaining the exact trajectory source and geometry.
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var fullEventOnVisuals = CaptureVisuals(canvas, eventOnFrame);
            Require(fullEventOnVisuals.MarkerCount == CountMarkers(eventOnFrame),
                "事件標記開啟時，full renderer 必須顯示 frame 事件標記。");
            Require(HasEventLegend(canvas),
                "事件標記開啟時，full renderer 必須顯示圓點 legend 說明。");

            showEvents.IsChecked = false;
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var fullEventOffVisuals = CaptureVisuals(canvas, eventOnFrame);
            Require(fullEventOffVisuals.MarkerCount == 0,
                "取消事件標記後，full renderer 不得建立可見 Ellipse。");
            Require(!HasEventLegend(canvas),
                "取消事件標記後，full renderer 不得顯示圓點 legend 說明。");
            RequireSameSeriesEndpoints(fullEventOnVisuals, fullEventOffVisuals,
                "full event toggle off");
            Require(eventOnFrame.Trajectory.Count == eventOnTrajectoryCount
                    && eventOnFrame.Events.Count == eventOnSourceCount,
                "full renderer 事件標記切換不得改變 paused frame 的 trajectory/event source counts。");

            // Restore the default for the remaining renderer/cache regressions.
            showEvents.IsChecked = true;

            // Each presentation-layout input invalidates static geometry but never changes the
            // frame's retained source data.
            SelectWorkspaceTab(window, "DiagramTabItem");
            staticBuilds = GetStaticBuilds(window);
            var zoom = (Slider)window.FindName("DiagramZoomSlider")!;
            zoom.Value = Math.Min(zoom.Maximum, zoom.Value + 1);
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            Require(GetStaticBuilds(window) > staticBuilds,
                "zoom 變更必須使 static layer invalidate。");
            staticBuilds = GetStaticBuilds(window);

            var direction = (ComboBox)window.FindName("DiagramDirectionComboBox")!;
            direction.SelectedIndex = direction.SelectedIndex == 1 ? 2 : 1;
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            Require(GetStaticBuilds(window) > staticBuilds,
                "direction filter 變更必須使 static layer invalidate。");
            staticBuilds = GetStaticBuilds(window);

            var canvasHeight = Math.Max(420, canvas.ActualHeight + 80);
            canvas.Height = canvasHeight;
            canvas.Measure(new Size(Math.Max(900, canvas.ActualWidth), canvasHeight));
            canvas.Arrange(new Rect(0, 0, Math.Max(900, canvas.ActualWidth), canvasHeight));
            window.UpdateLayout();
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            Require(GetStaticBuilds(window) > staticBuilds,
                "canvas resize 必須使 static layer invalidate。");
            staticBuilds = GetStaticBuilds(window);

            var plannedCacheBeforeReset = GetPlannedCache(window);
            Require(plannedCacheBeforeReset.ProcessedCount > 0,
                "normal Reset 前必須已有 planned display cache，才能驗證其保留。");
            Require(GetActualCache(window).ProcessedCount > 0,
                "normal Reset 前必須已有 actual display cache，才能驗證其清空。");

            // Reset lowers simulation time and therefore starts a fresh presentation cache
            // generation even though the worker keeps its session id. Exercise the normal owner
            // path so the selected Diagram page is refreshed exactly by ResetPlaybackAsync's
            // post-worker UpdatePlaybackView call.
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ResetPlaybackAsync"));
            window.ResetPlaybackDiagnostics();
            var resetEmptyFrame = WpfTestWait.LatestFrame(window);
            var plannedCacheAfterReset = GetPlannedCache(window);
            Require(GetActualCache(window).ProcessedCount == 0
                    && ReferenceEquals(plannedCacheAfterReset, plannedCacheBeforeReset)
                    && plannedCacheAfterReset.ProcessedCount > 0
                    && GetDiagramGeneration(window) == resetEmptyFrame.GenerationId
                    && string.Equals(GetDiagramLayoutKey(window), "no-data", StringComparison.Ordinal),
                "normal Reset 的 actual-only 空 frame 必須清除 actual cache、保留 planned cache 並顯示 no-data。");
            Require(canvas.Children.Count > 0
                    && !canvas.Children.OfType<Polyline>().Any(line => line.Points.Count > 0),
                "normal Reset 後 Diagram 頁必須非空，且不可保留舊 actual polyline。");

            // A hidden Diagram page must also repaint from the reset frame when revisited, rather
            // than exposing the pre-reset actual trajectory.
            SelectWorkspaceTab(window, "ResultsTabItem");
            SelectWorkspaceTab(window, "DiagramTabItem");
            Require(canvas.Children.Count > 0
                    && !canvas.Children.OfType<Polyline>().Any(line => line.Points.Count > 0),
                "切回隱藏 Diagram 頁不可顯示 Reset 前的 actual trajectory。");

            var safetyCanvas = (Canvas)window.FindName("SafetyDistanceCanvas")!;
            safetyCanvas.Width = 900;
            safetyCanvas.Height = 300;
            safetyCanvas.Measure(new Size(900, 300));
            safetyCanvas.Arrange(new Rect(0, 0, 900, 300));
            SelectWorkspaceTab(window, "SafetyTabItem");
            Require(safetyCanvas.Children.Count > 0,
                "normal Reset 後 Safety 頁必須由 owner refresh 出非空提示／圖表。");

            // Refill the reset world, then replace the worker with a newly configured project so
            // the immutable frame generation change is also covered.
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var resetFrame = WpfTestWait.LatestFrame(window);
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            staticBuilds = GetStaticBuilds(window);
            Require(staticBuilds > 0, "Reset 後重新出現 trajectory 必須重建 static layer。");
            // Reset while Safety is selected and Diagram contains a hidden populated view.
            // The owner must refresh Safety now, and Diagram must discard old data on revisit.
            Require(GetActualCache(window).ProcessedCount > 0,
                "隱藏 Diagram Reset 回歸必須先有 actual trajectory。");
            safetyCanvas.Children.Add(new Polyline { Points = new PointCollection { new(1, 1), new(2, 2) } });
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ResetPlaybackAsync"));
            Require(WpfTestWait.LatestFrame(window).SimulationTimeSeconds == 0
                    && safetyCanvas.Children.OfType<TextBlock>().Any()
                    && !safetyCanvas.Children.OfType<Polyline>().Any(),
                "Safety 已選取時 normal Reset 必須清除舊曲線並顯示空資料提示。");
            SelectWorkspaceTab(window, "DiagramTabItem");
            Require(GetActualCache(window).ProcessedCount == 0
                    && ReferenceEquals(GetPlannedCache(window), plannedCacheBeforeReset)
                    && canvas.Children.OfType<TextBlock>().Any()
                    && !canvas.Children.OfType<Polyline>().Any(line => line.Points.Count > 0),
                "在隱藏 Diagram 時 Reset，切回必須清除 actual 並保留 planned cache。");
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.WaitForPlannedTimeline(window);
            var replacementWorker = GetWorker(window);
            WpfTestWait.Wait(replacementWorker.Ready);
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var replacementFrame = WpfTestWait.LatestFrame(window);
            Require(replacementFrame.GenerationId != resetFrame.GenerationId,
                "重新 Configure topology 必須建立新的 frame generation。");
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            Require(GetActualCache(window).ProcessedCount == 0
                    && GetDiagramGeneration(window) == replacementFrame.GenerationId
                    && string.Equals(GetDiagramLayoutKey(window), "no-data", StringComparison.Ordinal),
                "new Configure generation 的 actual-only 空 frame 必須清除 cache 並顯示 no-data。");

            // Produce a populated frame in the replacement generation for the full-render parity
            // check.  The source lists are captured before both renderers and must remain intact.
            WpfTestWait.Wait(replacementWorker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var parityFrame = WpfTestWait.LatestFrame(window);
            Require(parityFrame.Trajectory.Count > 0, "replacement generation 應產生 trajectory。");
            var parityTrajectoryCount = parityFrame.Trajectory.Count;
            var parityEventCount = parityFrame.Events.Count;
            ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = "0.5";

            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var fullVisuals = CaptureVisuals(canvas, parityFrame);
            SetPrivateField(window, "_diagramLayoutKey", null);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var incrementalVisuals = CaptureVisuals(canvas, parityFrame);
            RequireRenderSemantics(fullVisuals, incrementalVisuals, parityFrame);
            Require(parityFrame.Trajectory.Count == parityTrajectoryCount
                    && parityFrame.Events.Count == parityEventCount,
                "renderer 不得修改 retained trajectory/event source。");
            RunEventCoordinateAndDirectionChecks(window, canvas, parityFrame);
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void RunEventCoordinateAndDirectionChecks(MainWindow window, Canvas canvas, PlaybackFrame frame)
    {
        var flags = BindingFlags.Static | BindingFlags.NonPublic;
        var build = typeof(MainWindow).GetMethod("BuildDiagramEventStationPositions", flags)!;
        var resolve = typeof(MainWindow).GetMethod("GetDiagramEventPosition", flags)!;
        var positions = (IReadOnlyDictionary<(TrainDirection Direction, string StationId), double>)build.Invoke(null, [frame])!;
        var context = frame.GetTopologyResultContext();
        var inbound = context.GetDisplayStations(TrainDirection.Inbound)[0];
        var outbound = context.GetDisplayStations(TrainDirection.Outbound)[0];
        var rawInboundPosition = context.GetStops(TrainDirection.Inbound)[0].ProjectedChainageMeters;
        Require(Math.Abs(rawInboundPosition - inbound.PositionMeters) > 1, "測試站不可位於鏡射中心。");
        var up = frame.Events[0] with { EventType = SimulationEventType.StationPassed,
            Direction = TrainDirection.Inbound, StationId = inbound.StationId,
            PositionMeters = rawInboundPosition, SimulationTimeSeconds = 10, Message = "regression-UP", VehicleId = "regression-UP" };
        var down = up with { Direction = TrainDirection.Outbound, StationId = outbound.StationId,
            PositionMeters = outbound.PositionMeters, Message = "regression-DOWN", VehicleId = "regression-DOWN" };
        double Position(SimulationEvent item) => (double)resolve.Invoke(null, [item, positions])!;
        Require(Math.Abs(Position(up) - inbound.PositionMeters) < 1e-8, "上行跨站點必須使用共同車站座標。");
        Require(Math.Abs(Position(down) - outbound.PositionMeters) < 1e-8, "下行跨站點座標必須保留。");
        foreach (var type in DiagramMarkerEvents.Where(type => type != SimulationEventType.StationPassed))
            Require(Position(up with { EventType = type }) == rawInboundPosition, "其他事件不可盲目翻轉里程。");
        Require(Position(up with { StationId = "unknown" }) == rawInboundPosition, "未知車站必須保留原座標。");
        Require(Position(up with { StationId = null }) == rawInboundPosition, "缺車站必須保留原座標。");

        var upSamples = frame.Trajectory.Select(sample => sample with {
            Direction = TrainDirection.Inbound, VehicleId = "regression-UP", ServiceRunId = "regression-UP" });
        var synthetic = frame with { GenerationId = Guid.NewGuid(),
            Events = System.Collections.Immutable.ImmutableList.Create(down, up),
            Trajectory = frame.Trajectory.AddRange(upSamples) };
        SetPrivateField(window, "_latestPlaybackFrame", synthetic);
        ConfigureDiagramFilters(window);
        ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = "0.5";
        var direction = (ComboBox)window.FindName("DiagramDirectionComboBox")!;
        foreach (var selected in new[] { 0, 1, 2, 0 })
        {
            direction.SelectedIndex = selected;
            SetPrivateField(window, "_diagramLayoutKey", null);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var incremental = MarkerGeometry(canvas);
            Require(incremental.Length == (selected == 0 ? 2 : 1), "三種方向選擇必須同步篩選事件。");
            foreach (var line in canvas.Children.OfType<Polyline>().Where(line => line.Points.Count > 0))
            {
                var tooltip = line.ToolTip?.ToString() ?? "";
                Require(selected != 1 || !tooltip.Contains("上行"), "僅下行不得留下上行實線。");
                Require(selected != 2 || tooltip.Contains("上行"), "僅上行不得留下下行實線。");
            }
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var full = MarkerGeometry(canvas);
            Require(full.Length == incremental.Length, "完整 renderer 必須同樣篩選方向。");
            for (var index = 0; index < full.Length; index++)
                Require(Math.Abs(full[index].Y - incremental[index].Y) < 1.5, "兩種 renderer 的跨站點座標必須一致。");
        }
        // Compare against an independently positioned non-pass marker on the station line.
        direction.SelectedIndex = 2;
        WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
        var passY = MarkerGeometry(canvas).Single().Y;
        SetPrivateField(window, "_latestPlaybackFrame", synthetic with {
            Events = System.Collections.Immutable.ImmutableList.Create(up with {
                EventType = SimulationEventType.Arrival, PositionMeters = inbound.PositionMeters }) });
        WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
        Require(Math.Abs(MarkerGeometry(canvas).Single().Y - passY) < 1e-8, "上行跨站點必須與共同里程的站事件對齊。");
        Require(up.PositionMeters == rawInboundPosition && frame.Events.Count > 0, "顯示修正不可改寫原始事件。");
        SetPrivateField(window, "_latestPlaybackFrame", frame);
        ConfigureDiagramFilters(window);
    }

    private static (string Message, double Y)[] MarkerGeometry(Canvas canvas) =>
        canvas.Children.OfType<Ellipse>().Where(item => item.Visibility == Visibility.Visible)
            .Select(item => (item.ToolTip?.ToString() ?? "", Canvas.GetTop(item)))
            .OrderBy(item => item.Item1, StringComparer.Ordinal).ToArray();

    private static void PrepareDiagramWindow(MainWindow window)
    {
        var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
        canvas.Width = 900;
        canvas.Height = 420;
        canvas.Measure(new Size(900, 420));
        canvas.Arrange(new Rect(0, 0, 900, 420));
        window.UpdateLayout();
    }

    private static void ConfigureDiagramFilters(MainWindow window)
    {
        ((CheckBox)window.FindName("ShowActualCheckBox")!).IsChecked = true;
        ((CheckBox)window.FindName("ShowPlannedCheckBox")!).IsChecked = false;
        ((ComboBox)window.FindName("DiagramDirectionComboBox")!).SelectedIndex = 0;
        ((ComboBox)window.FindName("DiagramVehicleComboBox")!).SelectedItem = null;
        ((TextBox)window.FindName("DiagramStartMinuteTextBox")!).Text = "0";
        ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = string.Empty;
    }

    private static void SelectWorkspaceTab(MainWindow window, string name)
    {
        var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
        workspace.SelectedItem = window.FindName(name);
        window.UpdateLayout();
    }

    private static SimulationPlaybackWorker GetWorker(MainWindow window) =>
        (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("未建立播放 worker。"));

    private static long GetStaticBuilds(MainWindow window) =>
        (long)(WpfTestWait.Field(window, "_diagramStaticBuilds")
            ?? throw new InvalidOperationException("找不到 _diagramStaticBuilds。"));

    private static TimeDistanceTrajectoryCache GetActualCache(MainWindow window) =>
        (TimeDistanceTrajectoryCache)(WpfTestWait.Field(window, "_diagramActualCache")
            ?? throw new InvalidOperationException("找不到 _diagramActualCache。"));

    private static TimeDistanceTrajectoryCache GetPlannedCache(MainWindow window) =>
        (TimeDistanceTrajectoryCache)(WpfTestWait.Field(window, "_diagramPlannedCache")
            ?? throw new InvalidOperationException("找不到 _diagramPlannedCache。"));

    private static Guid GetDiagramGeneration(MainWindow window) =>
        (Guid)(WpfTestWait.Field(window, "_diagramGeneration")
            ?? throw new InvalidOperationException("找不到 _diagramGeneration。"));

    private static string? GetDiagramLayoutKey(MainWindow window) =>
        (string?)WpfTestWait.Field(window, "_diagramLayoutKey");

    private static int CountMarkers(PlaybackFrame frame) =>
        frame.Events.Count(item => DiagramMarkerEvents.Contains(item.EventType));

    private static VisualSnapshot CaptureVisuals(Canvas canvas, PlaybackFrame frame)
    {
        return new VisualSnapshot(
            canvas.Children.OfType<Polyline>()
                .Where(line => line.Points.Count > 0)
                .Select(line => (line.Points[0], line.Points[^1]))
                .ToArray(),
            canvas.Children.OfType<Line>()
                .Where(line => Math.Abs(line.Y1 - line.Y2) < .01 && line.X2 > line.X1 + 100)
                .Select(line => line.Y1)
                .OrderBy(value => value)
                .ToArray(),
            canvas.Children.OfType<Ellipse>().Count(marker => marker.Visibility == Visibility.Visible),
            canvas.Children.OfType<Ellipse>().All(marker => marker.ToolTip is string),
            CountMarkers(frame));
    }

    private static bool HasEventLegend(Canvas canvas) =>
        canvas.Children
            .OfType<StackPanel>()
            .Where(panel => Equals(panel.Tag, ChartPainter.LegendTag))
            .SelectMany(panel => panel.Children.OfType<TextBlock>())
            .Any(text => text.Text is "站點事件" or "端點事件" or "安全事件");

    private static void RequireSameSeriesEndpoints(
        VisualSnapshot expected,
        VisualSnapshot actual,
        string context)
    {
        Require(expected.SeriesEndpoints.Count == actual.SeriesEndpoints.Count,
            $"{context} 不得改變 trajectory series 數量。");
        for (var index = 0; index < expected.SeriesEndpoints.Count; index++)
        {
            var expectedEndpoint = expected.SeriesEndpoints[index];
            var actualEndpoint = actual.SeriesEndpoints[index];
            Require(Math.Abs(expectedEndpoint.First.X - actualEndpoint.First.X) < 1.5
                    && Math.Abs(expectedEndpoint.First.Y - actualEndpoint.First.Y) < 1.5
                    && Math.Abs(expectedEndpoint.Last.X - actualEndpoint.Last.X) < 1.5
                    && Math.Abs(expectedEndpoint.Last.Y - actualEndpoint.Last.Y) < 1.5,
                $"{context} 不得改變 trajectory series {index} 的 first/last geometry。");
        }
    }

    private static void RequireRenderSemantics(
        VisualSnapshot full,
        VisualSnapshot incremental,
        PlaybackFrame frame)
    {
        Require(full.SeriesEndpoints.Count == incremental.SeriesEndpoints.Count
                && full.SeriesEndpoints.Count > 0,
            "full/reference 與 incremental series 數量不一致。");
        for (var index = 0; index < full.SeriesEndpoints.Count; index++)
        {
            var expected = full.SeriesEndpoints[index];
            var actual = incremental.SeriesEndpoints[index];
            Require(Math.Abs(expected.First.Y - actual.First.Y) < 1.5
                    && Math.Abs(expected.Last.Y - actual.Last.Y) < 1.5
                    && Math.Abs(expected.First.X - actual.First.X) < 1.5
                    && Math.Abs(expected.Last.X - actual.Last.X) < 1.5,
                "full/reference 與 incremental series 的 first/last coordinate 語意不一致。");
            Require(actual.First.X <= actual.Last.X + .01,
                "incremental series first/last time 順序錯誤。");
            Require(actual.First.X >= 0 && actual.Last.X >= 0,
                "incremental series first/last coordinate 不得為負值。");
        }

        Require(full.HorizontalLineYs.Count == incremental.HorizontalLineYs.Count
                && full.HorizontalLineYs.Count > 0,
            "full/reference 與 incremental station grid line 數量不一致。");
        for (var index = 0; index < full.HorizontalLineYs.Count; index++)
        {
            Require(Math.Abs(full.HorizontalLineYs[index] - incremental.HorizontalLineYs[index]) < 1.5,
                "full/reference 與 incremental station grid 對齊不一致。");
        }

        Require(full.MarkerCount == incremental.MarkerCount
                && incremental.MarkerCount == incremental.ExpectedMarkerCount
                && incremental.MarkersHaveToolTips,
            $"full/reference 與 incremental event marker 語意不一致："
            + $"full={full.MarkerCount}, incremental={incremental.MarkerCount}, expected={incremental.ExpectedMarkerCount}。");
        Require(frame.Trajectory.Count > 0, "render semantics 需要非空 trajectory source。");
    }

    private static void SetPrivateField(MainWindow window, string name, object? value)
    {
        var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到 private field {name}。");
        field.SetValue(window, value);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record VisualSnapshot(
        IReadOnlyList<(Point First, Point Last)> SeriesEndpoints,
        IReadOnlyList<double> HorizontalLineYs,
        int MarkerCount,
        bool MarkersHaveToolTips,
        int ExpectedMarkerCount);
}
