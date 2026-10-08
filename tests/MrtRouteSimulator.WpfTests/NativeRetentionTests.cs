using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Test-only reachability probe for the normal V2 reset and NativeAcceptance lifecycle.
/// This deliberately does not show a window or modify production code. It distinguishes
/// actual-frame/cache references, which should be released after stop, from the
/// planned artifact and the reused worker/world, which are expected to survive reset.
/// </summary>
internal static class NativeRetentionTests
{
    public static void Run(string root)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        RetentionProbe? probe = null;
        try
        {
            probe = CaptureNormalCompletedResetAndStop(root);
            ForceCollectionInTestProcess();

            Require(WpfTestWait.Field(probe.Owner, "_playbackWorker") is not null
                && WpfTestWait.Field(probe.Owner, "_plannedTimelineArtifact") is not null
                && !probe.Owner.IsNativeAcceptanceMeasurementActive,
                "存活檢查必須保留視窗／worker／planned，而只停止量測，不能先關閉整個owner。");

            Require(!probe.CompletedFrame.IsAlive,
                "Stop且owner保留時 completed PlaybackFrame 仍可達；保留引用路徑需進一步隔離。\n"
                + "這不是 private bytes 或 GC 因果判定。");
            Require(!probe.TrajectoryHistory.IsAlive,
                "Stop且owner保留時 completed frame trajectory history 仍可達；不能據此宣稱無洩漏。");
            Require(probe.SafetyHistory is null || !probe.SafetyHistory.IsAlive,
                "Stop且owner保留時 completed frame safety history 仍可達；不能據此宣稱無洩漏。");
            Require(!probe.EventHistory.IsAlive,
                "Stop且owner保留時 completed frame event history 仍可達；collector/event writer 邊界未釋放。");
            Require(!probe.ActualDisplaySeries.IsAlive,
                "Stop且owner保留時舊 actual diagram display series 仍可達；cache reset邊界未釋放。");
            Require(!probe.ActualDisplaySamples.IsAlive,
                "Stop且owner保留時舊 actual diagram samples 仍可達；cache reset邊界未釋放。");
            Console.WriteLine(
                "[通過] Native retention：completed frame／trajectory／safety／events／actual cache series "
                + "在 normal reset + collector stop、視窗／worker／planned保留時均不可達；planned artifact 與同一 worker/world "
                + "在 reset 當下均保留為正向對照。");
        }
        finally
        {
            if (probe is not null) WpfTestWait.Close(probe.Owner);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static RetentionProbe CaptureNormalCompletedResetAndStop(string root)
    {
        var samplePath = Path.Combine(root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "MrtRouteSimulator.NativeRetentionTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var window = new MainWindow();
        var handedOff = false;
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.WaitForPlannedTimeline(window);

            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("retention test 未建立 playback worker。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("retention test 未建立 playback timer。"))).Stop();

            var world = worker.GetType().GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(worker)
                ?? throw new InvalidOperationException("retention test 找不到 worker world。");
            var plannedArtifact = WpfTestWait.Field(window, "_plannedTimelineArtifact")
                ?? throw new InvalidOperationException("retention test 未建立 planned artifact。");
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var initialEventsJson = System.Text.Json.JsonSerializer.Serialize(
                WpfTestWait.LatestFrame(window).Events);

            // Use the normal collector start and normal Play dispatch. The deterministic
            // worker command below only avoids wall-clock playback in this test-only probe.
            window.StartNativeAcceptanceMeasurement(outputDirectory);
            WpfTestWait.Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));

            var duration = (double)(typeof(MainWindow).GetField("_playbackDurationSeconds",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window)
                ?? throw new InvalidOperationException("retention test 找不到 playback duration。"));
            // The UI may extend its playback horizon while terminal outcomes are settling.
            // A bounded deterministic target avoids relying on that UI timer extension.
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(Math.Max(duration + 1, 10_000)));
            WpfTestWait.Invoke(window, "PlaybackTimer_TickCore");

            var completedFrame = WpfTestWait.LatestFrame(window);
            Require(completedFrame.IsComplete,
                $"retention test 未取得 completed frame（frameTime={completedFrame.SimulationTimeSeconds:0.###}, "
                + $"configuredDuration={duration:0.###}, trajectory={completedFrame.Trajectory.Count}, "
                + $"events={completedFrame.Events.Count}）。");
            Require(completedFrame.Trajectory.Count > 0,
                "completed frame 必須含有非空 trajectory history，才能執行 reachability check。");
            Require(completedFrame.Events.Count > 0,
                "completed frame 必須含有非空 event history，才能執行 collector writer 邊界 check。");
            Require(completedFrame.SafetyHistory.Count > 0,
                "sample14 completed frame 必須含有非空 safety history，不能以空singleton跳過存活檢查。");

            // Draw directly through the existing presentation path. No Show() is used; the
            // XAML-created canvas and cache are sufficient for the source-cache operation.
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var actualCache = WpfTestWait.Field(window, "_diagramActualCache")
                ?? throw new InvalidOperationException("retention test 找不到 actual diagram cache。");
            var groups = (System.Collections.IEnumerable)(actualCache.GetType().GetProperty("Groups")?.GetValue(actualCache)
                ?? throw new InvalidOperationException("retention test 找不到 actual cache groups。"));
            var actualSeries = groups.Cast<object>().FirstOrDefault()
                ?? throw new InvalidOperationException("completed actual cache 應至少有一個 display series。" );
            var actualSamples = actualSeries.GetType().GetProperty("Samples")?.GetValue(actualSeries)
                ?? throw new InvalidOperationException("actual display series 應有 samples collection。" );

            var probe = new RetentionProbe(
                window,
                new WeakReference(completedFrame),
                new WeakReference(completedFrame.Trajectory),
                completedFrame.SafetyHistory.Count > 0 ? new WeakReference(completedFrame.SafetyHistory) : null,
                new WeakReference(completedFrame.Events),
                new WeakReference(actualSeries),
                new WeakReference(actualSamples));

            // Reuse the exact worker/world and keep planned data as positive controls across
            // the normal reset. NativeAcceptance LastFrame may intentionally retain the old
            // frame until collector stop; therefore weak assertions happen only after stop/close.
            var workerBeforeReset = WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("reset 前 worker 不存在。" );
            var worldBeforeReset = worker.GetType().GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(worker);
            var plannedBeforeReset = WpfTestWait.Field(window, "_plannedTimelineArtifact");

            WpfTestWait.Invoke(window, "NativeAcceptanceAbortForLifecycle", "playback-reset");
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ResetV2PlaybackAsync"));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            window.NativeAcceptanceAfterResetApplied();

            Require(ReferenceEquals(workerBeforeReset, WpfTestWait.Field(window, "_playbackWorker")),
                "normal reset 應重用同一個 playback worker（正向對照）。");
            Require(ReferenceEquals(worldBeforeReset,
                    worker.GetType().GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(worker)),
                "normal reset 應重用同一個 SimulationWorld（正向對照）。");
            Require(ReferenceEquals(plannedBeforeReset, WpfTestWait.Field(window, "_plannedTimelineArtifact")),
                "normal reset 不應清除 planned timeline artifact（正向對照）。");

            var resetFrame = WpfTestWait.LatestFrame(window);
            Require(!resetFrame.IsComplete && resetFrame.SimulationTimeSeconds == 0
                    && resetFrame.Trajectory.Count == 0
                    && System.Text.Json.JsonSerializer.Serialize(resetFrame.Events) == initialEventsJson
                    && resetFrame.SafetyHistory.Count == 0,
                $"normal reset frame 應回到0秒、清空trajectory/safety，events與初始world全欄位一致（含零秒發車）：time={resetFrame.SimulationTimeSeconds}, trajectory={resetFrame.Trajectory.Count}, safety={resetFrame.SafetyHistory.Count}, events={resetFrame.Events.Count}。" );

            var actualGroupsAfterReset = (System.Collections.IEnumerable)(actualCache.GetType().GetProperty("Groups")!.GetValue(actualCache)!);
            Require(!actualGroupsAfterReset.Cast<object>().Any(),
                "normal reset 後 actual diagram cache 應清空舊 display series。" );
            var plannedCache = WpfTestWait.Field(window, "_diagramPlannedCache")
                ?? throw new InvalidOperationException("retention test 找不到 planned diagram cache。" );
            var plannedProcessed = (int)(plannedCache.GetType().GetProperty("ProcessedCount")!.GetValue(plannedCache)!);
            Require(plannedProcessed > 0,
                "normal reset 後 planned diagram cache 應保留為正向對照，不能誤判為 actual 清理目標。" );

            var stopTask = window.StopNativeAcceptanceMeasurementAsync();
            WpfTestWait.Wait(stopTask);
            DrainStopTaskContinuation(window);
            Require(WpfTestWait.Field(window, "_nativeAcceptanceStopTask") is null,
                "collector stop 完成後應清空 deferred stop task reference，避免測試自身誤判。");

            // Keep the normal window/worker/planned owner graph alive across the collection
            // probe. The caller closes it only after assertions, so old-root reclamation is
            // not explained by collecting the entire closed-window graph.
            handedOff = true;
            return probe;
        }
        finally
        {
            if (!handedOff && !window.Dispatcher.HasShutdownStarted)
            {
                try
                {
                    if (window.IsNativeAcceptanceMeasurementActive)
                    {
                        var stopTask = window.StopNativeAcceptanceMeasurementAsync();
                        WpfTestWait.Wait(stopTask);
                        DrainStopTaskContinuation(window);
                    }
                }
                catch
                {
                    // Preserve the original assertion/failure; final WPF cleanup follows.
                }

                try { WpfTestWait.Close(window); }
                catch { }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DrainStopTaskContinuation(MainWindow window)
    {
        // StopNativeAcceptanceMeasurementAsync clears its task field from a continuation posted
        // back to the dispatcher. Pump that callback before the capture method loses its window.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (WpfTestWait.Field(window, "_nativeAcceptanceStopTask") is null)
            {
                return;
            }

            WpfTestWait.Wait(window.Dispatcher.InvokeAsync(() => { }).Task);
            WpfTestWait.Wait(Task.Delay(10));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollectionInTestProcess()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed record RetentionProbe(
        MainWindow Owner,
        WeakReference CompletedFrame,
        WeakReference TrajectoryHistory,
        WeakReference? SafetyHistory,
        WeakReference EventHistory,
        WeakReference ActualDisplaySeries,
        WeakReference ActualDisplaySamples);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
