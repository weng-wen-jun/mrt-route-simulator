using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Explicit opt-in end-to-end profiler. It is intentionally not part of the default WPF test
/// run because seven 60x runs are a measurement, not a regression smoke test.
/// </summary>
internal static class PlaybackProfileTests
{
    private const double RequestedPlaybackRate = 60;
    private const double MinimumSimulationSeconds = 600;
    private const double ParityStartSeconds = 680;
    private static readonly TimeSpan MinimumWallTime = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions ParityJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private static readonly string[] ExpectedTimingNames =
    [
        PlaybackDiagnosticMetricNames.WorkerAdvance,
        PlaybackDiagnosticMetricNames.WorkerNoProgressAdvanceBatch,
        PlaybackDiagnosticMetricNames.WorkerFrameBuild,
        PlaybackDiagnosticMetricNames.WorkerFramePublish,
        PlaybackDiagnosticMetricNames.DispatcherApplyAge,
        PlaybackDiagnosticMetricNames.ApplyPlaybackFrame,
        PlaybackDiagnosticMetricNames.ResultAccumulator,
        PlaybackDiagnosticMetricNames.TrainRowUpdate,
        PlaybackDiagnosticMetricNames.SafetyRowUpdate,
        PlaybackDiagnosticMetricNames.EventRowUpdate,
        PlaybackDiagnosticMetricNames.RouteMarkerRender,
        PlaybackDiagnosticMetricNames.SpeedChartRender,
        PlaybackDiagnosticMetricNames.SafetyChartRender,
        PlaybackDiagnosticMetricNames.TimeDistanceRender,
        PlaybackDiagnosticMetricNames.TimetableUpdate,
        PlaybackDiagnosticMetricNames.SegmentStatistics,
        PlaybackDiagnosticMetricNames.ResourceOccupancy,
        PlaybackDiagnosticMetricNames.V1V2Comparison,
        PlaybackDiagnosticMetricNames.InputStall,
        PlaybackDiagnosticMetricNames.InputStallExcess
    ];

    private static readonly (string Name, string WorkspaceTab, bool SpeedView)[] Profiles =
    [
        ("Route", "SimulationTabItem", false),
        ("Speed", "SimulationTabItem", true),
        ("Safety", "SafetyTabItem", false),
        ("TimeDistance", "DiagramTabItem", false),
        ("Timetable", "ResultsTabItem", false),
        ("Segment", "SegmentTabItem", false),
        ("Resource", "ResourceTabItem", false),
        ("Comparison", "ComparisonTabItem", false)
    ];

    public static void VerifyDiagnosticsContract()
    {
        var disabled = new PlaybackPerformanceDiagnostics();
        disabled.RecordTiming("disabled", 1);
        disabled.RecordCount("disabled");
        var disabledSnapshot = disabled.Snapshot();
        if (disabled.IsEnabled || disabledSnapshot.Timings.Count != 0 || disabledSnapshot.Counters.Count != 0)
        {
            throw new InvalidOperationException("playback diagnostics 預設必須關閉且不記錄樣本。");
        }

        var disabledRuntime = disabled.CaptureRuntimeSnapshot();
        if (disabledRuntime.AllocatedBytes != -1
            || disabledRuntime.ManagedBytes != -1
            || disabledRuntime.WorkingSetBytes != -1
            || disabledRuntime.Gen0Collections != -1
            || disabledRuntime.Gen1Collections != -1
            || disabledRuntime.Gen2Collections != -1)
        {
            throw new InvalidOperationException("disabled playback diagnostics 必須回傳 runtime sentinel，不能取樣。");
        }

        VerifyNativeProcessMemorySampler();

        var enabled = new PlaybackPerformanceDiagnostics(enabled: true, sampleCapacity: 3);
        enabled.RecordTiming("contract", 1);
        enabled.RecordTiming("contract", 2);
        enabled.RecordTiming("contract", 3);
        enabled.RecordTiming("contract", 4);
        enabled.RecordCount("counter", 2);
        var measured = enabled.Snapshot().GetTiming("contract")
            ?? throw new InvalidOperationException("playback diagnostics 未建立 timing summary。");
        if (measured.Count != 4
            || !double.IsFinite(measured.P50Milliseconds)
            || !double.IsFinite(measured.P95Milliseconds)
            || !double.IsFinite(measured.MaxMilliseconds)
            || measured.MaxMilliseconds != 4)
        {
            throw new InvalidOperationException("playback diagnostics p50/p95/max contract 無效。");
        }

        enabled.Reset();
        if (enabled.Snapshot().Timings.Count != 0 || enabled.Snapshot().Counters.Count != 0)
        {
            throw new InvalidOperationException("playback diagnostics Reset 未清空 aggregate counters。");
        }

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            if (window.PlaybackDiagnostics.IsEnabled)
            {
                throw new InvalidOperationException("MainWindow playback diagnostics 預設必須關閉。");
            }
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyNativeProcessMemorySampler()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var helperType = typeof(PlaybackPerformanceDiagnostics).Assembly.GetType(
            "MrtRouteSimulator.App.PlaybackProcessMemoryDiagnostics")
            ?? throw new InvalidOperationException("native process memory helper type is missing.");
        var capture = helperType.GetMethod(
            "TryCapture",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("native process memory helper entry point is missing.");
        var arguments = new object?[] { 0L, 0L };
        if (capture.Invoke(null, arguments) is not true
            || arguments[0] is not long privateBytes
            || arguments[1] is not long workingSetBytes
            || privateBytes <= 0
            || workingSetBytes <= 0)
        {
            throw new InvalidOperationException(
                "Windows native process memory helper must return positive private/working-set bytes.");
        }

        var countersType = helperType.GetNestedType(
            "ProcessMemoryCountersEx",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PROCESS_MEMORY_COUNTERS_EX mapping is missing.");
        var expectedSize = IntPtr.Size == 8 ? 80 : 44;
        var expectedWorkingSetOffset = IntPtr.Size == 8 ? 16 : 12;
        var expectedPrivateOffset = IntPtr.Size == 8 ? 72 : 40;
        if (Marshal.SizeOf(countersType) != expectedSize
            || Marshal.OffsetOf(countersType, "WorkingSetSize").ToInt64() != expectedWorkingSetOffset
            || Marshal.OffsetOf(countersType, "PrivateUsage").ToInt64() != expectedPrivateOffset)
        {
            throw new InvalidOperationException(
                $"PROCESS_MEMORY_COUNTERS_EX mapping is invalid for pointer size {IntPtr.Size}: "
                + $"size={Marshal.SizeOf(countersType)}, "
                + $"workingSetOffset={Marshal.OffsetOf(countersType, "WorkingSetSize")}, "
                + $"privateOffset={Marshal.OffsetOf(countersType, "PrivateUsage")}." );
        }
    }

    public static void Run(string samplePath, string? selectedProfile = null)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var window = new MainWindow();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.WaitForPlannedTimeline(window);
            window.Show();
            window.UpdateLayout();
            WpfTestWait.Wait(Task.Delay(100));

            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("大型樣本未建立播放工作者。"));
            window.EnablePlaybackDiagnostics();
            var profiles = Profiles.Where(profile => selectedProfile is null
                || profile.Name.Equals(selectedProfile, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (profiles.Length == 0 && selectedProfile != "Parity")
                throw new ArgumentException($"Unknown profile: {selectedProfile}");
            foreach (var profile in profiles)
            {
                SelectProfileTab(window, profile);
                var result = RunProfile(window, worker, profile.Name);
                PrintProfileResult(result);
                if (profile.Name == "TimeDistance")
                {
                    var actual = (TimeDistanceTrajectoryCache)WpfTestWait.Field(window, "_diagramActualCache")!;
                    var planned = (TimeDistanceTrajectoryCache)WpfTestWait.Field(window, "_diagramPlannedCache")!;
                    Console.WriteLine("DIAGRAM_CACHE_JSON=" + JsonSerializer.Serialize(new
                    {
                        ActualProcessed = actual.ProcessedCount,
                        PlannedProcessed = planned.ProcessedCount,
                        ActualGroups = actual.Groups.Count,
                        PlannedGroups = planned.Groups.Count,
                        MaxActualDisplay = actual.Groups.Select(s => s.DisplayPointCount).DefaultIfEmpty().Max(),
                        MaxPlannedDisplay = planned.Groups.Select(s => s.DisplayPointCount).DefaultIfEmpty().Max(),
                        MaxActualCritical = actual.Groups.Select(s => s.CriticalPointCount).DefaultIfEmpty().Max(),
                        MaxPlannedCritical = planned.Groups.Select(s => s.CriticalPointCount).DefaultIfEmpty().Max(),
                        MaxOrdinary = actual.Groups.Concat(planned.Groups).Select(s => s.OrdinaryPointCount).DefaultIfEmpty().Max()
                    }));
                }
            }

            RunDroppedFrameParity(window, worker);
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    public static void VerifyWorkspaceSelectionEventBoundary()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MrtRouteSimulator.slnx")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root not found.");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root.FullName,
            "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window,
                "ConfigureTopologyProjectForPlaybackAsync", document, true));
            window.Show();
            window.EnablePlaybackDiagnostics();
            var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
            workspace.SelectedItem = window.FindName("SafetyTabItem");
            window.ResetPlaybackDiagnostics();
            var pair = (ComboBox)window.FindName("SafetyPairComboBox")!;
            pair.RaiseEvent(new SelectionChangedEventArgs(
                System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                Array.Empty<object>(), Array.Empty<object>()));
            if (window.PlaybackDiagnostics.Snapshot().GetCount(PlaybackDiagnosticMetricNames.UiFrameAppliedCount) != 0)
                throw new InvalidOperationException("子控制項 SelectionChanged 不得觸發 workspace 完整刷新。");
            workspace.RaiseEvent(new SelectionChangedEventArgs(
                System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                Array.Empty<object>(), Array.Empty<object>()));
            if (window.PlaybackDiagnostics.Snapshot().GetCount(PlaybackDiagnosticMetricNames.UiFrameAppliedCount) == 0)
                throw new InvalidOperationException("真正 workspace SelectionChanged 仍必須刷新目前 frame。");
            Console.WriteLine("PASS WPF workspace selection event boundary");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static ProfileResult RunProfile(MainWindow window, SimulationPlaybackWorker worker, string profileName)
    {
        window.EnablePlaybackDiagnostics(false);
        window.ResetPlaybackDiagnostics();
        WpfTestWait.Wait(worker.ResetAsync());
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        window.EnablePlaybackDiagnostics(true);

        var diagnostics = window.PlaybackDiagnostics;
        ((ComboBox)window.FindName("PlaybackSpeedComboBox")!).SelectedIndex = 3;
        var measurement = diagnostics.BeginMeasurement();
        using var measuredProcess = Process.GetCurrentProcess();
        var cpuStart = measuredProcess.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        var previousInputTick = clock.Elapsed;
        var maxInputStall = TimeSpan.Zero;
        var maxInputTickGap = TimeSpan.Zero;
        var inputTicks = 0;
        var inputTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(20)
        };
        inputTimer.Tick += (_, _) =>
        {
            var now = clock.Elapsed;
            var gap = now - previousInputTick;
            previousInputTick = now;
            var expectedGap = TimeSpan.FromMilliseconds(20);
            var excess = gap > expectedGap ? gap - expectedGap : TimeSpan.Zero;
            maxInputTickGap = gap > maxInputTickGap ? gap : maxInputTickGap;
            maxInputStall = excess > maxInputStall ? excess : maxInputStall;
            inputTicks++;
            diagnostics.RecordTiming(PlaybackDiagnosticMetricNames.InputStall, gap.TotalMilliseconds);
            diagnostics.RecordTiming(PlaybackDiagnosticMetricNames.InputStallExcess, excess.TotalMilliseconds);
        };
        inputTimer.Start();

        try
        {
            WpfTestWait.Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            while (clock.Elapsed < MinimumWallTime
                   && WpfTestWait.LatestFrame(window).SimulationTimeSeconds < MinimumSimulationSeconds)
            {
                WpfTestWait.Wait(Task.Delay(100));
                if (worker.Completion.IsFaulted)
                {
                    throw worker.Completion.Exception!.GetBaseException();
                }
            }
        }
        finally
        {
            inputTimer.Stop();
            WpfTestWait.Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(50));
        }

        clock.Stop();
        measuredProcess.Refresh();
        diagnostics.RecordTiming("ProcessCpuTotal", (measuredProcess.TotalProcessorTime - cpuStart).TotalMilliseconds);
        var frame = WpfTestWait.LatestFrame(window);
        if (Math.Abs(frame.Performance.RequestedPlaybackRate - RequestedPlaybackRate) > 1e-9)
        {
            throw new InvalidOperationException(
                $"播放 profiler 未以 60× 執行：requested={frame.Performance.RequestedPlaybackRate:0.###}。");
        }
        var runtime = measurement.Complete();
        var snapshot = PlaybackDiagnosticsSnapshot.Merge(
            diagnostics.Snapshot(runtime),
            worker.Diagnostics.Snapshot());
        var elapsedSeconds = Math.Max(clock.Elapsed.TotalSeconds, double.Epsilon);
        var publishFps = snapshot.GetCount("WorkerFramePublishedCount") / elapsedSeconds;
        var applyFps = snapshot.GetCount(PlaybackDiagnosticMetricNames.UiFrameConsumedCount) / elapsedSeconds;
        if (applyFps <= 0)
        {
            applyFps = snapshot.GetCount(PlaybackDiagnosticMetricNames.UiFrameAppliedCount) / elapsedSeconds;
        }
        return new ProfileResult(
            profileName,
            frame.Performance.RequestedPlaybackRate,
            frame.SimulationTimeSeconds,
            frame.SimulationTimeSeconds / elapsedSeconds,
            elapsedSeconds,
            publishFps,
            applyFps,
            snapshot.GetCount(PlaybackDiagnosticMetricNames.DroppedFrameCount),
            maxInputTickGap.TotalMilliseconds,
            maxInputStall.TotalMilliseconds,
            inputTicks,
            snapshot);
    }

    private static void RunDroppedFrameParity(MainWindow window, SimulationPlaybackWorker worker)
    {
        window.EnablePlaybackDiagnostics(false);
        worker.Diagnostics.Enable(true);
        worker.Diagnostics.Reset();
        WpfTestWait.Wait(worker.ResetAsync());
        WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(ParityStartSeconds));
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        WpfTestWait.Wait(worker.PlayAsync(RequestedPlaybackRate));
        // Deliberately stop consuming frames on the UI thread. The worker still advances every
        // fixed tick; this makes the latest-frame channel drop old presentation frames.
        Thread.Sleep(1000);
        var pauseTask = worker.PauseAsync();
        pauseTask.GetAwaiter().GetResult();
        if (!worker.TryReadLatestFrame(out var droppedFrame) || droppedFrame is null)
        {
            throw new InvalidOperationException("刻意慢 UI 測試沒有取得最新播放 frame。");
        }

        var droppedCount = worker.Diagnostics.Snapshot().GetCount(
            PlaybackDiagnosticMetricNames.DroppedFrameCount);
        if (droppedCount <= 0)
        {
            throw new InvalidOperationException("刻意慢 UI 測試沒有觀察到 frame drop。");
        }

        var target = droppedFrame.SimulationTimeSeconds;
        WpfTestWait.Wait(worker.ResetAsync());
        WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(ParityStartSeconds));
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        WpfTestWait.Wait(worker.PlayAsync(RequestedPlaybackRate));
        var normalConsumeClock = Stopwatch.StartNew();
        while (normalConsumeClock.Elapsed < TimeSpan.FromSeconds(1))
        {
            WpfTestWait.Wait(Task.Delay(50));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView");
        }

        WpfTestWait.Wait(worker.PauseAsync());
        WpfTestWait.Wait(Task.Delay(50));
        var continuouslyConsumedFrame = WpfTestWait.LatestFrame(window);
        if (continuouslyConsumedFrame.SimulationTimeSeconds <= ParityStartSeconds)
        {
            throw new InvalidOperationException("正常持續 consume 測段未推進 simulation time。");
        }
        Console.WriteLine($"playback-profile parity normal-consume sim={continuouslyConsumedFrame.SimulationTimeSeconds:0.0}s "
            + $"frames={continuouslyConsumedFrame.Sequence}");

        WpfTestWait.Wait(worker.ResetAsync());
        WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(target));
        WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
        var normalFrame = WpfTestWait.LatestFrame(window);
        if (normalFrame.SimulationTimeSeconds + 1e-8 < target)
        {
            throw new InvalidOperationException(
                $"正常 UI parity frame 尚未到達 target：actual={normalFrame.SimulationTimeSeconds:0.###}，target={target:0.###}。");
        }

        var droppedAccumulator = new SimulationResultAccumulator();
        droppedAccumulator.Advance(droppedFrame);
        var normalAccumulator = new SimulationResultAccumulator();
        normalAccumulator.Advance(normalFrame);
        if (droppedAccumulator.BuildResourceOccupancy(target).Intervals.Count == 0)
            throw new InvalidOperationException("慢 UI parity 必須涵蓋實際資源預約，不可僅比對空集合。");

        RequireJsonParity("events", droppedFrame.Events, normalFrame.Events);
        RequireJsonParity("trajectory", droppedFrame.Trajectory, normalFrame.Trajectory);
        RequireJsonParity("safety-history", droppedFrame.SafetyHistory, normalFrame.SafetyHistory);
        RequireJsonParity("current-safety", droppedFrame.CurrentSafety, normalFrame.CurrentSafety);
        RequireJsonParity("trains", droppedFrame.Trains, normalFrame.Trains);
        RequireJsonParity(
            "resources",
            droppedAccumulator.BuildResourceOccupancy(target),
            normalAccumulator.BuildResourceOccupancy(target));

        static void RequireJsonParity<T>(string scope, T left, T right)
        {
            var leftJson = JsonSerializer.Serialize(left, ParityJsonOptions);
            var rightJson = JsonSerializer.Serialize(right, ParityJsonOptions);
            if (!string.Equals(leftJson, rightJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"慢 UI／frame drop 前後的 {scope} JSON 不一致。");
            }
        }

        Console.WriteLine($"playback-profile parity sim={target:0.0}s "
            + $"dropped={droppedCount} "
            + "physics=equal events=equal trajectory=equal safety=equal resources=equal "
            + "normal=deterministic-AdvanceTo-latest-frame; continuous-consume=observed");
    }

    private static void SelectProfileTab(
        MainWindow window,
        (string Name, string WorkspaceTab, bool SpeedView) profile)
    {
        var workspaceTabs = (TabControl)window.FindName("WorkspaceTabControl")!;
        workspaceTabs.SelectedItem = window.FindName(profile.WorkspaceTab);
        if (profile.WorkspaceTab == "SimulationTabItem")
        {
            var viewTabs = (TabControl)window.FindName("SimulationViewTabControl")!;
            viewTabs.SelectedItem = profile.SpeedView
                ? window.FindName("SpeedProfileTabItem")
                : viewTabs.Items[0];
        }

        window.UpdateLayout();
    }

    private static void PrintProfileResult(ProfileResult result)
    {
        Console.WriteLine($"playback-profile tab={result.Name} "
            + $"requested={result.RequestedRate:0.#}x effective={result.EffectiveRate:0.##}x "
            + $"sim={result.SimulationSeconds:0.0}s wall={result.WallSeconds:0.00}s "
            + $"publishFps={result.PublishFps:0.0} applyFps={result.ApplyFps:0.0} "
            + $"dropped={result.DroppedFrames} maxInputGap={result.MaxInputTickGapMilliseconds:0.0}ms "
            + $"maxInputStall={result.MaxInputStallMilliseconds:0.0}ms "
            + $"inputTicks={result.InputTicks}");
        Console.WriteLine(result.Diagnostics.FormatReport(ExpectedTimingNames));
        Console.WriteLine($"playback-profile noProgressAdvanceCount={result.Diagnostics.GetCount(
            PlaybackDiagnosticMetricNames.WorkerNoProgressAdvanceCount):N0}");
        Console.WriteLine("PROFILE_JSON=" + JsonSerializer.Serialize(result));

        var measured = result.Diagnostics.Timings.Keys.ToHashSet(StringComparer.Ordinal);
        var hidden = ExpectedTimingNames
            .Where(name => !GetVisibleTimingNames(result.Name).Contains(name, StringComparer.Ordinal)
                && measured.Contains(name))
            .ToArray();
        var unmeasurable = ExpectedTimingNames
            .Where(name => !measured.Contains(name))
            .ToArray();
        Console.WriteLine("playback-profile hidden-refresh-paths="
            + (hidden.Length == 0 ? "none" : string.Join(',', hidden)));
        Console.WriteLine("playback-profile unmeasurable="
            + (unmeasurable.Length == 0 ? "none" : string.Join(',', unmeasurable)));

        var runtime = result.Diagnostics.Runtime;
        if (runtime is not null && runtime.AllocatedBytes >= 0)
        {
            var allocationRate = runtime.AllocatedBytes / 1024d / 1024d
                / Math.Max(result.WallSeconds, double.Epsilon);
            Console.WriteLine($"playback-profile allocationRate={allocationRate:0.###}MiB/s "
                + $"managed={runtime.ManagedMiB}MiB workingSet={runtime.WorkingSetMiB}MiB "
                + $"gc={runtime.Gen0Collections}/{runtime.Gen1Collections}/{runtime.Gen2Collections}");
        }
    }

    private static IReadOnlySet<string> GetVisibleTimingNames(string profileName)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            PlaybackDiagnosticMetricNames.WorkerAdvance,
            PlaybackDiagnosticMetricNames.WorkerNoProgressAdvanceBatch,
            PlaybackDiagnosticMetricNames.WorkerFrameBuild,
            PlaybackDiagnosticMetricNames.WorkerFramePublish,
            PlaybackDiagnosticMetricNames.ApplyPlaybackFrame,
            PlaybackDiagnosticMetricNames.ResultAccumulator,
            PlaybackDiagnosticMetricNames.DispatcherApplyAge,
            PlaybackDiagnosticMetricNames.InputStall,
            PlaybackDiagnosticMetricNames.InputStallExcess
        };
        if (profileName == "Route")
        {
            names.Add(PlaybackDiagnosticMetricNames.RouteMarkerRender);
        }
        else if (profileName == "Speed")
        {
            names.Add(PlaybackDiagnosticMetricNames.SpeedChartRender);
        }
        else if (profileName == "Safety")
        {
            names.Add(PlaybackDiagnosticMetricNames.SafetyRowUpdate);
            names.Add(PlaybackDiagnosticMetricNames.EventRowUpdate);
            names.Add(PlaybackDiagnosticMetricNames.SafetyChartRender);
        }
        else if (profileName == "TimeDistance")
        {
            names.Add(PlaybackDiagnosticMetricNames.TimeDistanceRender);
        }
        else if (profileName == "Timetable")
        {
            names.Add(PlaybackDiagnosticMetricNames.TimetableUpdate);
        }
        else if (profileName == "Segment")
        {
            names.Add(PlaybackDiagnosticMetricNames.SegmentStatistics);
        }
        else if (profileName == "Resource")
        {
            names.Add(PlaybackDiagnosticMetricNames.ResourceOccupancy);
        }
        else if (profileName == "Comparison")
        {
            names.Add(PlaybackDiagnosticMetricNames.V1V2Comparison);
        }

        return names;
    }

    private sealed record ProfileResult(
        string Name,
        double RequestedRate,
        double SimulationSeconds,
        double EffectiveRate,
        double WallSeconds,
        double PublishFps,
        double ApplyFps,
        long DroppedFrames,
        double MaxInputTickGapMilliseconds,
        double MaxInputStallMilliseconds,
        int InputTicks,
        PlaybackDiagnosticsSnapshot Diagnostics);
}
