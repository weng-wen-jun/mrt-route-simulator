using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

/// <summary>
/// Regression checks for the first time-distance render after topology setup.
/// These checks deliberately use dispatcher completion gates and source/cache
/// parity rather than wall-clock thresholds: a slow CI machine must not turn a
/// correctness test into a timing test.
/// </summary>
internal static class TimeDistancePreparationTests
{
    private const int Sample14PlannedTrajectoryCount = 178_992;

    public static void Run(string root)
    {
        VerifyCachePreparedAndSynchronousParity();
        VerifyPreparationDoesNotBlockDispatcherAndFirstPlannedDraw(root);
        VerifyStaleCompletionCancellationFaultAndClose(root);
        Console.WriteLine("PASS WPF time-distance background preparation");
    }

    private static void VerifyCachePreparedAndSynchronousParity()
    {
        var source = BuildSource();
        var synchronous = new TimeDistanceTrajectoryCache();
        synchronous.Append(source);

        // Exercise the production builder rather than duplicating its append
        // implementation in the test. The gate pauses source enumeration on
        // the worker, giving the WPF dispatcher a deterministic opportunity
        // to run while the cache is being prepared.
        var gatedSource = new GatedTrajectorySource(source);
        var preparation = TimeDistanceTrajectoryCacheBuilder.BuildAsync(gatedSource);
        WpfTestWait.Wait(gatedSource.Entered.Task);
        var dispatcherGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send,
            new Action(() => dispatcherGate.TrySetResult(true)));
        WpfTestWait.Wait(dispatcherGate.Task);
        Require(dispatcherGate.Task.IsCompletedSuccessfully,
            "cache builder blocked dispatcher while source gate was held。");
        gatedSource.Release();
        var prepared = preparation.GetAwaiter().GetResult();

        RequireCacheParity(synchronous, prepared, "production builder");
    }

    private static void VerifyPreparationDoesNotBlockDispatcherAndFirstPlannedDraw(string root)
    {
        var samplePath = Path.Combine(root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.GetFullPath(samplePath)));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            // ConfigureTopologyProjectForPlaybackAsync is intentionally invoked
            // without synchronously waiting on the call stack. A dispatcher
            // gate proves that the UI thread gets a turn while preparation is
            // in flight; it does not rely on a millisecond limit.
            var configureTask = WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true);
            var dispatcherGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            window.Dispatcher.BeginInvoke(DispatcherPriority.Send,
                new Action(() => dispatcherGate.TrySetResult(true)));
            WpfTestWait.Wait(dispatcherGate.Task);
            Require(dispatcherGate.Task.IsCompletedSuccessfully,
                "topology preparation 未讓 dispatcher gate 完成。");

            WpfTestWait.Wait(configureTask);
            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.WaitForPlannedTimeline(window);

            window.Show();
            window.UpdateLayout();
            SelectWorkspaceTab(window, "DiagramTabItem");
            PrepareDiagramWindow(window);
            ((CheckBox)window.FindName("ShowActualCheckBox")!).IsChecked = false;
            ((CheckBox)window.FindName("ShowPlannedCheckBox")!).IsChecked = true;
            ((ComboBox)window.FindName("DiagramDirectionComboBox")!).SelectedIndex = 0;
            ((TextBox)window.FindName("DiagramStartMinuteTextBox")!).Text = "0";
            ((TextBox)window.FindName("DiagramEndMinuteTextBox")!).Text = string.Empty;

            var artifact = ReadField<PlannedTimelineArtifact?>(window, "_plannedTimelineArtifact");
            Require(artifact is { Trajectory.Length: > 0 },
                "planned preparation 完成後必須有非空 planned trajectory。");
            Require(artifact!.Trajectory.Length == Sample14PlannedTrajectoryCount,
                $"sample14 planned trajectory 筆數不符：actual={artifact.Trajectory.Length} expected={Sample14PlannedTrajectoryCount}。");

            var plannedCache = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache");
            var synchronous = new TimeDistanceTrajectoryCache();
            synchronous.Append(artifact.Trajectory);
            RequireCacheParity(synchronous, plannedCache, "sample14 planned cache");

            var beforeVersion = plannedCache.Version;
            var beforeProcessed = plannedCache.ProcessedCount;
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
            Require(canvas.Children.OfType<Polyline>().Any(line => line.StrokeDashArray is not null
                                                                    && line.Points.Count > 0),
                "首次運行圖 Draw 後必須具備 planned trajectory polyline。");
            Require(plannedCache.Version == beforeVersion
                    && plannedCache.ProcessedCount == beforeProcessed,
                "首次 Draw 不得在 UI thread 重新 Append 整份 planned trajectory。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyStaleCompletionCancellationFaultAndClose(string root)
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

            var worker = GetWorker(window);
            var generation = worker.GenerationId;
            var currentSource = GetRequiredField<PlannedTimelineArtifact>(window, "_plannedTimelineArtifact");
            var staleSource = new PlannedTimelineArtifact(
                ImmutableArray<SimulationEvent>.Empty,
                ImmutableArray<TrajectorySample>.Empty,
                0);
            var oldCache = new TimeDistanceTrajectoryCache();
            var currentCache = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache");

            WpfTestWait.Advance(window, 30);
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ResetV2PlaybackAsync"));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
            Require(ReferenceEquals(currentCache, GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache")),
                "同source reset應重用已完成planned cache。");

            var wrongGenerationTask = Task.FromResult(oldCache);
            using var wrongGenerationCancellation = new CancellationTokenSource();
            SetField(window, "_diagramPlannedCacheTask", wrongGenerationTask);
            SetField(window, "_diagramPlannedCacheRequestSource", currentSource);
            SetField(window, "_diagramPlannedCacheRequestGeneration", generation);
            SetField(window, "_diagramPlannedCacheCancellation", wrongGenerationCancellation);
            WpfTestWait.Invoke(window, "CompletePlannedTimeDistanceCacheWarmup",
                wrongGenerationTask, currentSource, Guid.NewGuid(), oldCache, null, false, wrongGenerationCancellation);
            Require(ReadField<Task<TimeDistanceTrajectoryCache>?>(window, "_diagramPlannedCacheTask") is null
                    && ReadField<CancellationTokenSource?>(window, "_diagramPlannedCacheCancellation") is null
                    && ReferenceEquals(currentCache, GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache")),
                "owned stale generation必須清request，不得發布cache。");

            // A cache completion from a replaced source must not mutate the
            // current source. Invoke the production completion gate directly
            // with a stale task/source/cancellation tuple.
            var currentTask = new TaskCompletionSource<TimeDistanceTrajectoryCache>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var currentCancellation = new CancellationTokenSource();
            SetField(window, "_diagramPlannedCacheTask", currentTask.Task);
            SetField(window, "_diagramPlannedCacheRequestSource", currentSource);
            SetField(window, "_diagramPlannedCacheRequestGeneration", generation);
            SetField(window, "_diagramPlannedCacheCancellation", currentCancellation);
            SetField(window, "_diagramPlannedSource", currentSource);
            WpfTestWait.Invoke(
                window,
                "CompletePlannedTimeDistanceCacheWarmup",
                Task.FromResult(oldCache),
                staleSource,
                generation,
                oldCache,
                null,
                false,
                new CancellationTokenSource());
            Require(ReferenceEquals(ReadField<Task<TimeDistanceTrajectoryCache>?>(window, "_diagramPlannedCacheTask"), currentTask.Task),
                "stale cache completion 不得取代 current cache task。");
            Require(ReferenceEquals(GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache"), currentCache),
                "stale cache completion 不得發布舊 cache。");
            currentCancellation.Cancel();
            currentTask.TrySetCanceled(currentCancellation.Token);

            // Cancellation and builder fault are lifecycle outcomes, not
            // unhandled UI exceptions. The production cache completion gate
            // must consume both outcomes and clear only the current request.
            WpfTestWait.Invoke(window, "CancelPlannedTimeDistanceCacheWarmup", true);
            Require(ReadField<Task<TimeDistanceTrajectoryCache>?>(window, "_diagramPlannedCacheTask") is null
                    && ReadField<CancellationTokenSource?>(window, "_diagramPlannedCacheCancellation") is null,
                "cancelled cache warmup 應清除 current request。");
            currentCancellation.Dispose();

            // Build has completed, but its dispatcher publication is queued.
            // Cancel before pumping that callback: CTS must still be live and
            // the queued result must never restore the cleared display cache.
            var completedBeforePublication = Task.FromResult(currentCache);
            using var completedCancellation = new CancellationTokenSource();
            SetField(window, "_diagramPlannedCacheTask", completedBeforePublication);
            SetField(window, "_diagramPlannedCacheRequestSource", currentSource);
            SetField(window, "_diagramPlannedCacheRequestGeneration", generation);
            SetField(window, "_diagramPlannedCacheCancellation", completedCancellation);
            var observer = (Task)WpfTestWait.Invoke(window, "ObservePlannedTimeDistanceCacheWarmupAsync",
                completedBeforePublication, currentSource, generation, completedCancellation)!;
            WpfTestWait.Invoke(window, "CancelPlannedTimeDistanceCacheWarmup", true);
            WpfTestWait.Wait(observer);
            Require(ReadField<CancellationTokenSource?>(window, "_diagramPlannedCacheCancellation") is null
                    && !GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache").HasData,
                "完成但尚未publication的cancel不得洩漏CTS或發布已取消結果。");

            var faultTask = Task.FromException<TimeDistanceTrajectoryCache>(
                new InvalidOperationException("deterministic preparation fault"));
            var faultCancellation = new CancellationTokenSource();
            SetField(window, "_diagramPlannedCacheTask", faultTask);
            SetField(window, "_diagramPlannedCacheRequestSource", currentSource);
            SetField(window, "_diagramPlannedCacheRequestGeneration", generation);
            SetField(window, "_diagramPlannedCacheCancellation", faultCancellation);
            WpfTestWait.Invoke(
                window,
                "CompletePlannedTimeDistanceCacheWarmup",
                faultTask,
                currentSource,
                generation,
                null,
                new InvalidOperationException("deterministic preparation fault"),
                false,
                faultCancellation);
            var status = ((TextBlock)window.FindName("PlaybackStatusText")!).Text;
            Require(status.Contains("預熱失敗", StringComparison.Ordinal),
                "cache preparation fault 應轉為 UI status，不得成為 unhandled exception。");
            Require(ReadField<Exception?>(window, "_diagramPlannedCacheError") is not null,
                "cache preparation fault 應保留可診斷錯誤。");
            WpfTestWait.Invoke(window, "StartPlannedTimeDistanceCacheWarmup", currentSource, generation);
            Require(ReadField<Task<TimeDistanceTrajectoryCache>?>(window, "_diagramPlannedCacheTask") is null
                    && ReadField<Exception?>(window, "_diagramPlannedCacheError") is not null,
                "同source故障不得無限重新預熱。");
            _ = faultTask.Exception;
            faultCancellation.Dispose();

            // Stop/close clears the request before awaiting cleanup. Completing
            // the previously captured task afterwards must be rejected by the
            // source/generation/reference gate and never publish stale data.
            var lateTask = new TaskCompletionSource<TimeDistanceTrajectoryCache>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var lateCancellation = new CancellationTokenSource();
            var lateSource = currentSource;
            SetField(window, "_diagramPlannedCacheTask", lateTask.Task);
            SetField(window, "_diagramPlannedCacheRequestSource", lateSource);
            SetField(window, "_diagramPlannedCacheRequestGeneration", generation);
            SetField(window, "_diagramPlannedCacheCancellation", lateCancellation);
            var stopTask = WpfTestWait.Invoke(window, "StopCurrentPlaybackResourcesAsync") as Task
                ?? throw new InvalidOperationException("StopCurrentPlaybackResourcesAsync 未回傳 Task。");
            lateCancellation.Cancel();
            lateTask.TrySetCanceled(lateCancellation.Token);
            WpfTestWait.Wait(stopTask);
            WpfTestWait.Invoke(
                window,
                "CompletePlannedTimeDistanceCacheWarmup",
                lateTask.Task,
                lateSource,
                generation,
                currentCache,
                null,
                true,
                lateCancellation);
            Require(ReadField<Task<TimeDistanceTrajectoryCache>?>(window, "_diagramPlannedCacheTask") is null
                    && ReadField<PlannedTimelineArtifact?>(window, "_plannedTimelineArtifact") is null
                    && !GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache").HasData,
                "close/stop 後不得保留或發布 stale planned cache。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void PumpDispatcher(MainWindow window)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => gate.TrySetResult(true)));
        WpfTestWait.Wait(gate.Task);
    }

    private static void PrepareDiagramWindow(MainWindow window)
    {
        var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
        canvas.Width = 900;
        canvas.Height = 420;
        canvas.Measure(new Size(900, 420));
        canvas.Arrange(new Rect(0, 0, 900, 420));
        window.UpdateLayout();
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

    private static T GetRequiredField<T>(MainWindow window, string name) =>
        (T)(WpfTestWait.Field(window, name)
            ?? throw new InvalidOperationException($"找不到或值為 null 的欄位 {name}。"));

    private static T? ReadField<T>(MainWindow window, string name) =>
        (T?)WpfTestWait.Field(window, name);

    private static void SetField(MainWindow window, string name, object? value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(window, value);

    private static void RequireCacheParity(
        TimeDistanceTrajectoryCache expected,
        TimeDistanceTrajectoryCache actual,
        string label)
    {
        Require(expected.ProcessedCount == actual.ProcessedCount,
            $"{label} processed count 不一致。");
        Require(expected.MinPosition == actual.MinPosition
                && expected.MaxPosition == actual.MaxPosition
                && expected.MinTime == actual.MinTime
                && expected.MaxTime == actual.MaxTime,
            $"{label} bounds 不一致。");
        Require(expected.Groups.Count == actual.Groups.Count,
            $"{label} group count 不一致。");
        foreach (var expectedGroup in expected.Groups)
        {
            var actualGroup = actual.Groups.Single(item =>
                item.VehicleId == expectedGroup.VehicleId
                && item.ServiceRunId == expectedGroup.ServiceRunId
                && item.Direction == expectedGroup.Direction);
            Require(expectedGroup.CriticalPointCount == actualGroup.CriticalPointCount
                    && expectedGroup.OrdinaryPointCount == actualGroup.OrdinaryPointCount
                    && expectedGroup.SourceSampleCount == actualGroup.SourceSampleCount
                    && expectedGroup.Samples.SequenceEqual(actualGroup.Samples),
                $"{label} group {expectedGroup.VehicleId}/{expectedGroup.ServiceRunId} 不一致。");
            Require(expectedGroup.Samples[0].Equals(actualGroup.Samples[0])
                    && expectedGroup.Samples[^1].Equals(actualGroup.Samples[^1]),
                $"{label} group endpoint 不一致。");
        }
    }

    private static TrajectorySample[] BuildSource()
    {
        return Enumerable.Range(0, 240)
            .Select(index => new TrajectorySample(
                SimulationTimeSeconds: index * 0.1,
                VehicleId: index < 120 ? "V01" : "V02",
                ServiceRunId: index < 120 ? "R01" : "R02",
                ServiceClassId: "LOCAL",
                ServicePatternId: "ALL",
                Direction: index < 120 ? TrainDirection.Outbound : TrainDirection.Inbound,
                TrackId: index % 80 < 40 ? "DOWN" : "DOWN-ALT",
                PositionMeters: index < 120 ? index * 12d : (index - 120) * 11d,
                SpeedMetersPerSecond: index % 60 == 30 ? 24 : 12,
                AccelerationMetersPerSecondSquared: index switch
                {
                    20 or 60 or 140 or 200 => 1,
                    21 or 61 or 141 or 201 => 0,
                    _ => 0
                },
                Phase: index % 80 < 20 ? OperationalPhase.Accelerating : OperationalPhase.Cruising,
                CurrentStationId: index % 80 < 40 ? "A" : "B",
                NextStationId: index % 80 < 40 ? "B" : "C",
                IsPlanned: true,
                TrackSpeedLimitMetersPerSecond: index % 50 < 25 ? 20 : 10))
            .ToArray();
    }

    private sealed class GatedTrajectorySource(IReadOnlyList<TrajectorySample> source)
        : IReadOnlyList<TrajectorySample>
    {
        private readonly IReadOnlyList<TrajectorySample> _source = source;
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        public TaskCompletionSource<bool> Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count => _source.Count;

        public TrajectorySample this[int index]
        {
            get
            {
                if (index == 0 && Interlocked.Exchange(ref _entered, 1) == 0)
                {
                    Entered.TrySetResult(true);
                    _release.Task.GetAwaiter().GetResult();
                }

                return _source[index];
            }
        }

        public void Release() => _release.TrySetResult(true);

        public IEnumerator<TrajectorySample> GetEnumerator() => _source.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
