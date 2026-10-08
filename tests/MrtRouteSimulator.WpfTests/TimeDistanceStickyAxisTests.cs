using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

/// <summary>
/// Presentation regressions for the fixed time axis below the vertically
/// scrollable time-distance plot. These checks deliberately use visual-tree
/// coordinates and retained-frame identity; they do not replace native DPI or
/// desktop acceptance.
/// </summary>
internal static class TimeDistanceStickyAxisTests
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
            // This case verifies spare-height redistribution, not compact overflow.
            // The proportional native startup size depends on monitor DPI/work area;
            // at compact sizes collapsing events can correctly shrink the outer
            // extent while keeping the plot at its minimum. Give this logical
            // fixture a fixed, ample shell viewport without changing production
            // sizing. DiagramCompactLayoutTests covers compact reachability.
            var shell = (ScrollViewer)window.FindName("ShellScrollViewer")!;
            shell.Height = 1100;
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            Require(shell.ViewportHeight >= 1090,
                $"sticky-axis fixture 必須提供足夠邏輯高度：viewport={shell.ViewportHeight:0.0}。 ");
            SelectWorkspaceTab(window, "DiagramTabItem");
            ConfigureDiagramFilters(window);

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            WpfTestWait.Wait(worker.PauseAsync());
            var frozenFrame = WpfTestWait.LatestFrame(window);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            PumpDispatcher(window.Dispatcher);

            var viewer = (ScrollViewer)window.FindName("DiagramScrollViewer")!;
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            var axisViewport = (Border)window.FindName("DiagramTimeAxisViewport")!;
            var axis = (Canvas)window.FindName("DiagramTimeAxisCanvas")!;
            RequireAxis(axis, "baseline");
            Require(ReferenceEquals(frozenFrame, WpfTestWait.LatestFrame(window)),
                "sticky axis baseline 不得改變 paused frame source。 ");
            VerifyIndependentZoom(window, viewer, canvas, axis);

            var eventsExpander = (Expander)window.FindName("DiagramEventsExpander")!;
            var expandedViewerHeight = viewer.ActualHeight;
            eventsExpander.IsExpanded = false;
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            Require(viewer.ActualHeight > expandedViewerHeight + 20,
                $"收折事件列表後 DiagramScrollViewer 應增加可用高度："
                + $"expanded={expandedViewerHeight:0.0}, collapsed={viewer.ActualHeight:0.0}。 ");
            Console.WriteLine($"PASS WPF spare-height events collapse: shell={shell.ViewportHeight:0.0}, "
                + $"expanded={expandedViewerHeight:0.0}, collapsed={viewer.ActualHeight:0.0} (logical fixture)");
            RequireAxis(axis, "事件列表收折");
            Require(Math.Abs(frozenFrame.SimulationTimeSeconds
                             - WpfTestWait.LatestFrame(window).SimulationTimeSeconds) < 1e-7,
                "收折事件列表不得推進 paused frame clock。 ");
            eventsExpander.IsExpanded = true;
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            Require(viewer.ActualHeight < expandedViewerHeight + 20,
                "展開事件列表後 DiagramScrollViewer 應恢復原本的可用高度。 ");
            RequireAxis(axis, "事件列表展開");

            ResizeAndPump(window, viewer, 640, 270);
            RequireAxis(axis, "paused resize");
            Require(Math.Abs(axisViewport.ActualWidth - viewer.ViewportWidth) < 2,
                $"sticky axis viewport 寬度必須跟隨 ScrollViewer viewport："
                + $"axis={axisViewport.ActualWidth:0.0}, viewport={viewer.ViewportWidth:0.0}。 ");
            Require(ReferenceEquals(frozenFrame, WpfTestWait.LatestFrame(window)),
                "paused resize 不得推進或替換 retained frame。 ");

            var axisTop = axisViewport.TranslatePoint(new Point(0, 0), window).Y;
            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            var axisBottom = axisViewport.TranslatePoint(new Point(0, 0), window).Y;
            Require(Math.Abs(axisTop - axisBottom) < 1.5,
                $"上下捲動圖表內容時 sticky axis 必須維持相同絕對 screen Y："
                + $"top={axisTop:0.0}, bottom={axisBottom:0.0}。 ");
            Require(viewer.VerticalOffset > 0,
                "sticky axis regression 必須實際產生垂直捲動 offset。 ");

            var axisViewportX = axisViewport.TranslatePoint(new Point(0, 0), window).X;
            viewer.ScrollToHorizontalOffset(Math.Min(viewer.ScrollableWidth, 80));
            window.UpdateLayout();
            PumpDispatcher(window.Dispatcher);
            var axisTransform = axis.RenderTransform as TranslateTransform;
            Require(axisTransform is not null && Math.Abs(axisTransform.X + viewer.HorizontalOffset) < 1.5,
                $"水平捲動必須平移 sticky axis 內層 canvas：transformX={axisTransform?.X:0.0}, "
                + $"offset={viewer.HorizontalOffset:0.0}。 ");
            if (canvas.RenderTransform is TranslateTransform plotTransform)
            {
                Require(Math.Abs(plotTransform.X) < 1.5,
                    $"水平捲動不得再對 plot canvas 套用 double offset：plotTransformX={plotTransform.X:0.0}。 ");
            }
            Require(Math.Abs(axisViewport.TranslatePoint(new Point(0, 0), window).X - axisViewportX) < 1.5,
                "水平捲動不得把 sticky axis viewport 移出固定位置。 ");
            Require(Math.Abs(axisViewport.TranslatePoint(new Point(0, 0), window).Y - axisBottom) < 1.5,
                "水平捲動不得改變 sticky axis 的絕對 screen Y。 ");

            var embeddedAxisLabels = CountTimestampLabels(canvas);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            Require(CountTimestampLabels(canvas) >= 7 && CountTimestampLabels(canvas) >= embeddedAxisLabels,
                "full/export renderer 必須保留原始 canvas 內嵌的七個時間刻度。 ");

            WpfTestWait.Wait(worker.ResetAsync());
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            PumpDispatcher(window.Dispatcher);
            Require(axis.Children.Count == 0 || axis.Visibility != Visibility.Visible,
                "no-data frame 必須清除或隱藏 sticky axis，避免殘留舊時間刻度。 ");

            Console.WriteLine($"PASS WPF sticky time axis: ticks={CountTimestampLabels(axis)}, "
                + $"viewport={viewer.ViewportWidth:0.0}x{viewer.ViewportHeight:0.0}, "
                + $"verticalOffset={viewer.VerticalOffset:0.0}");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        RunManualTimeTickChecks(root);
    }

    private static void RunManualTimeTickChecks(string root)
    {
        var samplePath = Path.Combine(root, "samples", "12-中型-五站完整營運範例.mrtsim.json");
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
            ((CheckBox)window.FindName("ShowActualCheckBox")!).IsChecked = true;
            ((CheckBox)window.FindName("ShowPlannedCheckBox")!).IsChecked = true;
            ((CheckBox)window.FindName("ShowEventsCheckBox")!).IsChecked = true;
            ((ComboBox)window.FindName("DiagramDirectionComboBox")!).SelectedIndex = 0;
            ((ComboBox)window.FindName("DiagramVehicleComboBox")!).SelectedIndex = 0;
            ((TextBox)window.FindName("DiagramStartMinuteTextBox")!).Text = "0";
            ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = string.Empty;

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(30));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            WpfTestWait.Wait(worker.PauseAsync());
            var frozenFrame = WpfTestWait.LatestFrame(window);
            var frozenSimulationTime = frozenFrame.SimulationTimeSeconds;

            var plannedCache = (TimeDistanceTrajectoryCache)(WpfTestWait.Field(window, "_diagramPlannedCache")
                ?? throw new InvalidOperationException("找不到 planned time-distance cache。 "));
            Require(plannedCache.MaxTime >= 660,
                $"sample12 planned timeline 必須覆蓋手動 5 分鐘刻度測試範圍：max={plannedCache.MaxTime:0.0}。 ");

            var showEndTime = (CheckBox)window.FindName("DiagramShowEndTimeCheckBox")!;
            Require(showEndTime.IsChecked == true,
                "右端時間刻度 checkbox 預設必須開啟。 ");
            var originalStartClock = ReadPrivateField<double>(window, "_startClockSeconds");
            SetPrivateField(window, "_startClockSeconds", 0d);
            SetPrivateField(window, "_diagramTimeTickIntervalMinutes", 5d);
            showEndTime.IsChecked = false;
            PumpDispatcher(window.Dispatcher);
            var zeroClockTicks = InvokeTimeTicks(window, 60, 600);
            Require(zeroClockTicks.SequenceEqual(new[] { 300d, 600d }),
                $"右端時間關閉時，手動 5 分鐘、start=60、duration=600 應產生 300/600："
                + $"actual={string.Join('/', zeroClockTicks.Select(value => value.ToString("0.###")))}。 ");

            SetPrivateField(window, "_startClockSeconds", 90d);
            var nonZeroClockTicks = InvokeTimeTicks(window, 60, 600);
            Require(nonZeroClockTicks.SequenceEqual(new[] { 210d, 510d }),
                $"右端時間關閉時，非零 start clock 的相對刻度應為 210/510："
                + $"actual={string.Join('/', nonZeroClockTicks.Select(value => value.ToString("0.###")))}。 ");
            Require(nonZeroClockTicks.All(value =>
                    Math.Abs((90 + value) % 300) < .001),
                "非零 start clock 的手動刻度必須對齊絕對 clock 整 5 分鐘。 ");

            SetPrivateField(window, "_startClockSeconds", 0d);
            showEndTime.IsChecked = true;
            PumpDispatcher(window.Dispatcher);
            var unalignedEndTicks = InvokeTimeTicks(window, 60, 600);
            Require(unalignedEndTicks.SequenceEqual(new[] { 300d, 600d, 660d }),
                $"右端時間開啟時，未對齊端點 660 必須追加且不影響原刻度："
                + $"actual={string.Join('/', unalignedEndTicks.Select(value => value.ToString("0.###")))}。 ");

            var alignedEndTicks = InvokeTimeTicks(window, 60, 540);
            Require(alignedEndTicks.SequenceEqual(new[] { 300d, 600d }),
                $"右端時間與固定間隔精準重合時不得重複："
                + $"actual={string.Join('/', alignedEndTicks.Select(value => value.ToString("0.###")))}。 ");

            var shortRangeTicks = InvokeTimeTicks(window, 60, 10);
            Require(shortRangeTicks.SequenceEqual(new[] { 70d }),
                $"超短範圍仍須顯示右端時間 70："
                + $"actual={string.Join('/', shortRangeTicks.Select(value => value.ToString("0.###")))}。 ");

            var narrowAxis = new Canvas { Width = 200, Height = 32 };
            WpfTestWait.Invoke(window, "AddDiagramTimeTickLabel", narrowAxis,
                70d, 178d, 170d, 70d, 8d);
            var endpointLabel = narrowAxis.Children.OfType<TextBlock>().Single();
            endpointLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Require(Math.Abs(Canvas.GetTop(endpointLabel) - 20) < .001
                    && Canvas.GetLeft(endpointLabel) + endpointLabel.Width <= narrowAxis.Width
                    && Canvas.GetTop(endpointLabel) + endpointLabel.DesiredSize.Height <= narrowAxis.Height,
                "接近前一刻度的終點標籤須錯開且完整留在固定橫軸範圍內。 ");

            SetPrivateField(window, "_startClockSeconds", 90d);
            var nonZeroEndpointTicks = InvokeTimeTicks(window, 60, 600);
            Require(nonZeroEndpointTicks.SequenceEqual(new[] { 210d, 510d, 660d }),
                $"右端時間開啟且非零 start clock 的刻度不符："
                + $"actual={string.Join('/', nonZeroEndpointTicks.Select(value => value.ToString("0.###")))}。 ");
            SetPrivateField(window, "_startClockSeconds", originalStartClock);

            showEndTime.IsChecked = false;
            PumpDispatcher(window.Dispatcher);

            var tickCombo = (ComboBox)window.FindName("DiagramTimeTickComboBox")!;
            tickCombo.Text = "5";
            ApplyTimeTicks(window);
            Require(Math.Abs(ReadPrivateField<double>(window, "_diagramTimeTickIntervalMinutes") - 5) < .001,
                "套用合法 5 分鐘刻度後必須保存設定。 ");
            foreach (var invalid in new[] { "0", "1441", "NaN", "Infinity", "-Infinity", "not-a-number" })
            {
                tickCombo.Text = invalid;
                ApplyTimeTicks(window);
                Require(Math.Abs(ReadPrivateField<double>(window, "_diagramTimeTickIntervalMinutes") - 5) < .001,
                    $"非法時間刻度「{invalid}」不得覆寫既有 5 分鐘設定。 ");
            }

            ((TextBox)window.FindName("DiagramStartMinuteTextBox")!).Text = "1";
            ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = "11";
            showEndTime.IsChecked = true;
            PumpDispatcher(window.Dispatcher);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            PumpDispatcher(window.Dispatcher);
            var axis = (Canvas)window.FindName("DiagramTimeAxisCanvas")!;
            var interactiveTicks = GetTimestampLabels(axis);
            var expectedTicks = InvokeTimeTicks(window, 60, 600)
                .Select(value => TrajectoryAnalysis.FormatClock(originalStartClock + value))
                .ToArray();
            Require(interactiveTicks.SequenceEqual(expectedTicks),
                $"interactive renderer 的手動 timestamp 不符："
                + $"actual={string.Join('/', interactiveTicks)} expected={string.Join('/', expectedTicks)}。 ");

            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            var fullTicks = GetTimestampLabels(canvas);
            Require(fullTicks.SequenceEqual(interactiveTicks),
                $"full／interactive renderer 必須使用相同手動 timestamp："
                + $"full={string.Join('/', fullTicks)}, interactive={string.Join('/', interactiveTicks)}。 ");

            showEndTime.IsChecked = false;
            PumpDispatcher(window.Dispatcher);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            PumpDispatcher(window.Dispatcher);
            var interactiveTicksWithoutEnd = GetTimestampLabels(axis);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");
            var fullTicksWithoutEnd = GetTimestampLabels(canvas);
            var expectedTicksWithoutEnd = InvokeTimeTicks(window, 60, 600)
                .Select(value => TrajectoryAnalysis.FormatClock(originalStartClock + value))
                .ToArray();
            Require(interactiveTicksWithoutEnd.SequenceEqual(expectedTicksWithoutEnd)
                    && fullTicksWithoutEnd.SequenceEqual(interactiveTicksWithoutEnd),
                $"右端時間關閉時 full／interactive timestamp 必須一致："
                + $"full={string.Join('/', fullTicksWithoutEnd)}, "
                + $"interactive={string.Join('/', interactiveTicksWithoutEnd)}。 ");
            Require(ReferenceEquals(frozenFrame, WpfTestWait.LatestFrame(window))
                    && Math.Abs(frozenSimulationTime
                                - WpfTestWait.LatestFrame(window).SimulationTimeSeconds) < 1e-7,
                "切換右端時間與重繪 full／interactive 不得替換或推進 paused frame。 ");

            showEndTime.IsChecked = true;
            tickCombo.Text = "自動";
            ApplyTimeTicks(window);
            Require(Math.Abs(ReadPrivateField<double>(window, "_diagramTimeTickIntervalMinutes")) < .001,
                "套用「自動」後必須恢復 automatic tick interval。 ");
            ((TextBox)window.FindName("DiagramStartMinuteTextBox")!).Text = "0";
            ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = string.Empty;
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            PumpDispatcher(window.Dispatcher);
            Require(GetTimestampLabels(axis).Length == 7,
                "恢復自動刻度後 interactive renderer 必須恢復六等分、七個時間刻度。 ");
            Require(showEndTime.IsChecked == true
                    && ReferenceEquals(frozenFrame, WpfTestWait.LatestFrame(window)),
                "恢復預設右端時間與自動刻度後必須保留 paused frame。 ");

            Console.WriteLine($"PASS WPF manual time ticks: manual={string.Join('/', expectedTicks)}, auto=7");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static double[] InvokeTimeTicks(MainWindow window, double start, double duration) =>
        (double[])(WpfTestWait.Invoke(window, "GetDiagramTimeTicks", start, duration)
            ?? throw new InvalidOperationException("GetDiagramTimeTicks 回傳 null。 "));

    private static void ApplyTimeTicks(MainWindow window) =>
        WpfTestWait.Invoke(window, "DiagramTimeTicksApply_Click",
            new Button(), new RoutedEventArgs(Button.ClickEvent));

    private static string[] GetTimestampLabels(Panel panel) =>
        panel.Children.OfType<TextBlock>()
            .Select(item => item.Text)
            .Where(text => text.Contains(":", StringComparison.Ordinal))
            .ToArray();

    private static T ReadPrivateField<T>(MainWindow window, string name) =>
        (T)(WpfTestWait.Field(window, name)
            ?? throw new InvalidOperationException($"找不到 private field {name}。 "));

    private static void SetPrivateField(MainWindow window, string name, object value) =>
        (typeof(MainWindow).GetField(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到 private field {name}。 "))
            .SetValue(window, value);

    private static void RequireAxis(Canvas axis, string phase)
    {
        Require(axis.Visibility == Visibility.Visible && axis.IsVisible,
            $"{phase} sticky axis 必須可見。 ");
        Require(CountTimestampLabels(axis) >= 7,
            $"{phase} sticky axis 必須保留七個時間戳文字。 ");
    }

    private static int CountTimestampLabels(Panel panel) =>
        panel.Children.OfType<TextBlock>().Count(item =>
            item.Text.Contains(":", StringComparison.Ordinal));

    private static void ResizeAndPump(MainWindow window, ScrollViewer viewer, double width, double height)
    {
        viewer.Width = width;
        viewer.Height = height;
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
    }

    private static void VerifyIndependentZoom(
        MainWindow window,
        ScrollViewer viewer,
        Canvas canvas,
        Canvas axis)
    {
        var horizontal = (Slider)window.FindName("DiagramZoomSlider")!;
        var vertical = (Slider)window.FindName("DiagramVerticalZoomSlider")!;
        var initialWidth = canvas.Width;
        var initialHeight = GetRenderedCanvasHeight(canvas);
        Require(double.IsFinite(initialWidth) && double.IsFinite(initialHeight),
            "independent zoom baseline 必須有 finite canvas geometry。 ");

        vertical.Value = Math.Min(vertical.Maximum, Math.Max(vertical.Minimum, 2));
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
        var verticalWidth = canvas.Width;
        var verticalHeight = GetRenderedCanvasHeight(canvas);
        Require(verticalHeight > initialHeight + 1,
            $"垂直縮放必須增加 time-distance canvas 高度："
            + $"initial={initialHeight:0.0}, zoomed={verticalHeight:0.0}。 ");
        Require(Math.Abs(verticalWidth - initialWidth) <= 20,
            $"垂直縮放不得顯著改變 canvas 寬度（最多容許 scrollbar 影響 20 DIP）："
            + $"initial={initialWidth:0.0}, zoomed={verticalWidth:0.0}。 ");
        RequireAxis(axis, "垂直縮放");

        vertical.Value = vertical.Minimum;
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
        var horizontalBaselineWidth = canvas.Width;
        var horizontalBaselineHeight = GetRenderedCanvasHeight(canvas);
        horizontal.Value = Math.Min(horizontal.Maximum, Math.Max(horizontal.Minimum, 2));
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
        var horizontalWidth = canvas.Width;
        var horizontalHeight = GetRenderedCanvasHeight(canvas);
        Require(horizontalWidth > horizontalBaselineWidth + 1,
            $"水平縮放必須增加 time-distance canvas 寬度："
            + $"initial={horizontalBaselineWidth:0.0}, zoomed={horizontalWidth:0.0}。 ");
        Require(Math.Abs(horizontalHeight - horizontalBaselineHeight) <= 20,
            $"水平縮放不得顯著改變 canvas 高度（最多容許 scrollbar 影響 20 DIP）："
            + $"initial={horizontalBaselineHeight:0.0}, zoomed={horizontalHeight:0.0}。 ");
        RequireAxis(axis, "水平縮放");

        horizontal.Value = horizontal.Minimum;
        vertical.Value = vertical.Minimum;
        window.UpdateLayout();
        PumpDispatcher(window.Dispatcher);
        Require(viewer.ViewportWidth > 0 && viewer.ViewportHeight > 0,
            "恢復獨立縮放預設值後 viewport 必須維持有效尺寸。 ");
    }

    private static double GetRenderedCanvasHeight(Canvas canvas)
    {
        if (double.IsFinite(canvas.ActualHeight) && canvas.ActualHeight > 0)
        {
            return canvas.ActualHeight;
        }

        if (double.IsFinite(canvas.Height) && canvas.Height > 0)
        {
            return canvas.Height;
        }

        throw new InvalidOperationException("time-distance canvas 尚未取得有效高度。 ");
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void ConfigureDiagramFilters(MainWindow window)
    {
        ((CheckBox)window.FindName("ShowActualCheckBox")!).IsChecked = true;
        ((CheckBox)window.FindName("ShowPlannedCheckBox")!).IsChecked = false;
        ((CheckBox)window.FindName("ShowEventsCheckBox")!).IsChecked = true;
        ((ComboBox)window.FindName("DiagramDirectionComboBox")!).SelectedIndex = 0;
        ((ComboBox)window.FindName("DiagramVehicleComboBox")!).SelectedIndex = 0;
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
