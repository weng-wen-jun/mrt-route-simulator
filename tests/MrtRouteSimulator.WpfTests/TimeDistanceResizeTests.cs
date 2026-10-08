using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class TimeDistanceResizeTests
{
    public static void Run(string root)
    {
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
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
            SelectWorkspaceTab(window, "DiagramTabItem");
            ConfigureDiagramFilters(window);

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            WpfTestWait.Wait(worker.PauseAsync());
            var frozenFrame = WpfTestWait.LatestFrame(window);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");

            var viewer = (ScrollViewer)window.FindName("DiagramScrollViewer")!;
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            var zoom = (Slider)window.FindName("DiagramZoomSlider")!;
            var initialClock = frozenFrame.SimulationTimeSeconds;
            var initialStationIds = GetStationLabelIds(canvas);
            var initialSeriesCount = canvas.Children.OfType<Polyline>()
                .Count(line => line.Points.Count > 0);
            Require(initialStationIds.Count > 0, "baseline time-distance 圖應建立站名標籤。");
            Require(initialSeriesCount > 0, "baseline time-distance 圖應建立列車 series。");

            ResizeAndPump(window, viewer, 1100, 500);
            var wideExpected = ExpectedCanvasWidth(viewer, zoom.Value);
            Require(Math.Abs(canvas.Width - wideExpected) < 1.5,
                $"寬版 viewport 變更後 canvas 寬度未同步；expected={wideExpected:0.0}, actual={canvas.Width:0.0}。");

            ResizeAndPump(window, viewer, 560, 300);
            var narrowExpected = ExpectedCanvasWidth(viewer, zoom.Value);
            Require(Math.Abs(canvas.Width - narrowExpected) < 1.5,
                $"窄版 viewport 變更後 canvas 寬度未同步；expected={narrowExpected:0.0}, actual={canvas.Width:0.0}。");
            Require(viewer.ViewportHeight > 0 && viewer.ActualHeight <= 301,
                "短版 layout 應實際縮短 DiagramScrollViewer viewport。");
            Require(Math.Abs(WpfTestWait.LatestFrame(window).SimulationTimeSeconds - initialClock) < 1e-7,
                "paused frozen frame 在窄／短版 layout 後不得推進 simulation clock。");
            Require(GetStationLabelIds(canvas).SetEquals(initialStationIds),
                "窄／短版 layout 後站名與 station mapping 必須保留。");
            Require(canvas.Children.OfType<Polyline>().Count(line => line.Points.Count > 0) == initialSeriesCount,
                "窄／短版 layout 後列車 series 必須保留。");

            var staticBuilds = (long)WpfTestWait.Field(window, "_diagramStaticBuilds")!;
            viewer.ScrollToVerticalOffset(32);
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            Require(viewer.VerticalOffset > 0, "短版 regression 必須實際捲動 viewport。");
            Require((long)WpfTestWait.Field(window, "_diagramStaticBuilds")! == staticBuilds,
                "純捲動 offset 變化不得重建 TimeDistance static visuals。");

            Console.WriteLine($"PASS WPF time-distance paused resize freshness: frozen={initialClock:0.0}s, "
                + $"viewport={viewer.ViewportWidth:0.0}x{viewer.ViewportHeight:0.0}, canvas={canvas.Width:0.0}");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        RunUnmeasuredViewportGeometryCheck(root);
    }

    private static void RunUnmeasuredViewportGeometryCheck(string root)
    {
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
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

            // Keep the visual tree unshown and unarranged for the first renderer calls.
            // This is the state in which an unconstrained ScrollViewer can expose an
            // unbounded (or otherwise not-yet-useful) ViewportWidth to a deferred draw.
            var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
            workspace.SelectedItem = window.FindName("DiagramTabItem");
            ConfigureDiagramFilters(window);

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);

            var viewer = (ScrollViewer)window.FindName("DiagramScrollViewer")!;
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            viewer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var unarrangedViewportWidth = viewer.ViewportWidth;

            // Incremental and full paths must both sanitize the width before it reaches
            // axis transforms and Polyline.Points. Invocation exceptions also fail this
            // regression, including layout exceptions raised while assigning geometry.
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            RequireFiniteTimeDistanceGeometry(canvas, "incremental renderer");
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            RequireFiniteTimeDistanceGeometry(canvas, "full renderer");

            // Exercise the actual deferred callback. Resetting Width to its unset value
            // makes a callback that is skipped observable instead of merely rechecking
            // the preceding full render.
            canvas.Width = double.NaN;
            WpfTestWait.Invoke(window, "ScheduleTimeDistanceViewportRefresh");
            window.Show();
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            RequireFiniteTimeDistanceGeometry(canvas, "deferred viewport callback");
            Require(!(bool)(WpfTestWait.Field(window, "_diagramViewportRefreshQueued") ?? true),
                "deferred viewport callback 應在 layout pump 後完成。");

            Console.WriteLine($"PASS WPF time-distance unmeasured viewport geometry: "
                + $"viewportBeforeArrange={unarrangedViewportWidth:0.0}, canvas={canvas.Width:0.0}");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void RequireFiniteTimeDistanceGeometry(Canvas canvas, string phase)
    {
        Require(double.IsFinite(canvas.Width) && canvas.Width >= 1,
            $"{phase} 後 TimeDistanceCanvas.Width 必須是有限正數；actual={canvas.Width}。");
        Require(canvas.Children.OfType<Polyline>().Any(line => line.Points.Count > 0),
            $"{phase} 必須實際產生非空軌跡，不能以空畫面通過 finite geometry 檢查。");
        foreach (var line in canvas.Children.OfType<Polyline>())
        {
            foreach (var point in line.Points)
            {
                Require(double.IsFinite(point.X) && double.IsFinite(point.Y),
                    $"{phase} 後 Polyline.Points 必須是 finite；point=({point.X},{point.Y})。");
            }
        }
    }

    private static void ResizeAndPump(MainWindow window, ScrollViewer viewer, double width, double height)
    {
        viewer.Width = width;
        viewer.Height = height;
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static double ExpectedCanvasWidth(ScrollViewer viewer, double zoom) =>
        Math.Max(760, Math.Max(1, viewer.ViewportWidth - 15) * zoom);

    private static HashSet<string> GetStationLabelIds(Canvas canvas) =>
        canvas.Children.OfType<TextBlock>()
            .Select(text => text.Tag)
            .OfType<TimeDistanceStationLabelTag>()
            .Select(tag => tag.StationId)
            .ToHashSet(StringComparer.Ordinal);

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
            ?? throw new InvalidOperationException("未建立播放工作者。"));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
