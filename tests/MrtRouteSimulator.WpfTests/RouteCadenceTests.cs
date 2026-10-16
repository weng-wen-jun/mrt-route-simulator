using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Route-view cadence regression: every accepted worker frame must reach the visible route,
/// even when it arrives less than 33 ms after the previous paint. Records live dispatcher cadence.
/// Register <see cref="Run"/> from the WPF runner when investigating route playback motion.
/// </summary>
internal static class RouteCadenceTests
{
    private static readonly TimeSpan LiveMeasurementDuration = TimeSpan.FromSeconds(1);

    public static void Run(string root)
    {
        var samplePath = Path.Combine(root, "samples", "大型/01-大型-二十八站完整營運範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
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

            SelectRouteView(window);
            var playbackTimer = (DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("播放 UI timer 尚未建立。"));
            playbackTimer.Stop();

            VerifyEveryConsumedFramePaintsRoute(window);
            MeasureLiveDispatcherCadence(window);
            Console.WriteLine("PASS WPF route cadence diagnostic");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyEveryConsumedFramePaintsRoute(MainWindow window)
    {
        window.EnablePlaybackDiagnostics();
        window.ResetPlaybackDiagnostics();
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);

        var baseline = WpfTestWait.LatestFrame(window);
        var baselineSequence = baseline.Sequence;
        var lastRouteTimestamp = typeof(MainWindow).GetField(
            "_lastRouteRenderTimestamp",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 route render timestamp 欄位。");
        var lastRenderedSequence = typeof(MainWindow).GetField(
            "_lastRenderedPlaybackFrameSequence",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 playback sequence 欄位。");

        var baselineSnapshot = window.PlaybackDiagnostics.Snapshot();
        var baselineRouteRenders = baselineSnapshot
            .GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0;
        Require(baselineRouteRenders == 1,
            $"deterministic case 需要先完成一次 route paint；actual={baselineRouteRenders}。\n"
            + baselineSnapshot.FormatReport());

        // Start the skipped-frame assertion from a zeroed aggregate. The baseline paint above is
        // intentionally verified first, but it must not be counted as a paint for the next frame.
        window.ResetPlaybackDiagnostics();

        var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("大型樣本未建立播放工作者。"));
        WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(
            baseline.SimulationTimeSeconds + SimulationWorld.FixedTimeStepSeconds));

        // Reproduce the former double-throttle condition independently of scheduling.
        // The frame itself is a real worker-produced frame.
        lastRouteTimestamp.SetValue(window, Stopwatch.GetTimestamp());
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", false);

        var consumedFrame = WpfTestWait.LatestFrame(window);
        Require(consumedFrame.Sequence > baselineSequence,
            $"worker 應發布下一個 frame；baseline={baselineSequence}, actual={consumedFrame.Sequence}。");
        Require((long)(lastRenderedSequence.GetValue(window)
            ?? throw new InvalidOperationException("未讀到已渲染 frame sequence。")) == consumedFrame.Sequence,
            "frame 已被 UI apply path 標記為 rendered/consumed。");

        var skippedSnapshot = window.PlaybackDiagnostics.Snapshot();
        var consumedCount = skippedSnapshot.GetCount(PlaybackDiagnosticMetricNames.UiFrameConsumedCount);
        var routeCount = skippedSnapshot.GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0;
        Require(consumedCount == 1,
            $"預期只消費一個新 frame；actual={consumedCount}。\n{skippedSnapshot.FormatReport()}");
        Require(routeCount == 1,
            "即使距上次畫面不足 33 ms，每個已接收 frame 都必須畫出 route。\n"
            + skippedSnapshot.FormatReport());

        // No newer frame exists: the accepted frame has already been painted exactly once.
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", false);
        var noNewFrameSnapshot = window.PlaybackDiagnostics.Snapshot();
        var noNewFrameRouteCount = noNewFrameSnapshot
            .GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0;
        Require(noNewFrameRouteCount == 1,
            "沒有新 frame 時不得重複畫同一個 route frame。\n"
            + noNewFrameSnapshot.FormatReport());

        Console.WriteLine($"route-cadence deterministic: consumed={consumedCount}, routePaint={routeCount}, "
            + $"sequence={baselineSequence}->{consumedFrame.Sequence}, noNewFrameRoutePaint={noNewFrameRouteCount}");
    }

    private static void MeasureLiveDispatcherCadence(MainWindow window)
    {
        // Reset the world and force a clean visible baseline before measuring the actual timer.
        var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("大型樣本未建立播放工作者。"));
        WpfTestWait.Wait(worker.ResetAsync());
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        window.ResetPlaybackDiagnostics();

        var gaps = new List<double>();
        var probe = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed;
        probe.Tick += (_, _) =>
        {
            var now = clock.Elapsed;
            gaps.Add((now - previous).TotalMilliseconds);
            previous = now;
        };

        probe.Start();
        try
        {
            WpfTestWait.Invoke(window, "Play_Click", new Button(),
                new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(LiveMeasurementDuration));
        }
        finally
        {
            probe.Stop();
            WpfTestWait.Invoke(window, "Pause_Click", new Button(),
                new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            clock.Stop();
        }

        var snapshot = window.PlaybackDiagnostics.Snapshot();
        var routeRenders = snapshot.GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0;
        var consumed = snapshot.GetCount(PlaybackDiagnosticMetricNames.UiFrameConsumedCount);
        var dropped = WpfTestWait.LatestFrame(window).Performance.FrameDropCount;
        var averageGap = gaps.Count == 0 ? 0 : gaps.Average();
        var maxGap = gaps.Count == 0 ? 0 : gaps.Max();
        Console.WriteLine($"route-cadence live: elapsed={clock.Elapsed.TotalMilliseconds:0.0}ms, "
            + $"timerTicks={gaps.Count}, avgGap={averageGap:0.0}ms, maxGap={maxGap:0.0}ms, "
            + $"uiConsumed={consumed}, routePaint={routeRenders}, dropped={dropped}");
    }

    private static void SelectRouteView(MainWindow window)
    {
        var workspace = (TabControl)(window.FindName("WorkspaceTabControl")
            ?? throw new InvalidOperationException("找不到 workspace tab。"));
        workspace.SelectedItem = window.FindName("SimulationTabItem");
        var views = (TabControl)(window.FindName("SimulationViewTabControl")
            ?? throw new InvalidOperationException("找不到 simulation view tab。"));
        views.SelectedIndex = 0;
        window.UpdateLayout();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
