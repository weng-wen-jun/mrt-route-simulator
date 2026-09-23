using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class PlaybackWorkerTests
{
    public static void Run(string root)
    {
        var path = Path.Combine(root, "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            Console.WriteLine("  worker setup 完成");
            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("拓撲載入後未建立播放工作者。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("播放 UI timer 尚未建立。"))).Stop();
            var initial = WpfTestWait.LatestFrame(window);
            Require(initial.SimulationTimeSeconds == 0, "初始 immutable frame 必須從模擬時間 0 開始。");
            Require(initial.GenerationId == worker.GenerationId, "frame 必須帶有目前 session generation。");
            Require(initial.Events is IList eventList && eventList.IsReadOnly,
                "frame 事件歷史必須是不可變集合。");
            var routeCanvas = (Canvas)window.FindName("RouteCanvas");
            routeCanvas.Width = 800;
            routeCanvas.Height = 300;
            routeCanvas.Measure(new Size(800, 300));
            routeCanvas.Arrange(new Rect(0, 0, 800, 300));
            WpfTestWait.Invoke(window, "DrawV2Route");
            Require(routeCanvas.Children.Count > 0, "拓撲路線圖應建立固定配線。");
            var staticRail = routeCanvas.Children[0];
            WpfTestWait.Invoke(window, "DrawV2Route");
            Require(ReferenceEquals(staticRail, routeCanvas.Children[0]),
                "同一專案與尺寸的路線圖刷新應重用固定配線，只更新列車標記。");
            routeCanvas.Width = 900;
            routeCanvas.Measure(new Size(900, 300));
            routeCanvas.Arrange(new Rect(0, 0, 900, 300));
            WpfTestWait.Invoke(window, "DrawV2Route");
            Require(!ReferenceEquals(staticRail, routeCanvas.Children[0]),
                "路線圖尺寸改變後應重新計算固定配線。");
            var tabs = (TabControl)window.FindName("WorkspaceTabControl");
            Require(window.TimetableRows.Count == 0,
                "隱藏的時刻表分頁在播放初始化時不可預先刷新。");
            tabs.SelectedItem = window.FindName("ResultsTabItem");
            Require(window.TimetableRows.Count > 0,
                "切到時刻表分頁時必須延遲刷新結果。");
            tabs.SelectedItem = window.FindName("SimulationTabItem");
            Console.WriteLine("  隱藏分頁延遲刷新完成");
            var collectionResetCount = 0;
            window.CurrentTrainRows.CollectionChanged += (_, change) =>
            {
                if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    collectionResetCount++;
                }
            };

            worker.TryReadLatestFrame(out _);
            for (var index = 0; index < 6; index++)
            {
                var mode = index % 2 == 0 ? BrakingEstimationMode.Emergency : BrakingEstimationMode.Service;
                WpfTestWait.Wait(worker.SetBrakingEstimationModeAsync(mode));
            }

            Require(worker.TryReadLatestFrame(out var latest) && latest is not null,
                "worker 必須可讀取最新 frame。");
            Require(latest!.Sequence >= initial.Sequence + 6, "frame sequence 必須單調遞增。");
            Require(latest.Performance.FrameDropCount > 0,
                $"capacity 1 的 latest-frame channel 必須丟棄舊 frame 並記錄 drop count；observed={latest.Performance.FrameDropCount}, sequence={latest.Sequence}, initial={initial.Sequence}。");
            Console.WriteLine("  latest-frame 丟棄與可靠命令完成");

            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(2));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView");
            latest = WpfTestWait.LatestFrame(window);
            Require(collectionResetCount == 0,
                "即時列車列應以差分異動更新，不應清空 ObservableCollection。");
            Require(latest.SimulationTimeSeconds >= 1.999,
                "worker 應在背景完整推進 0.1 秒 physics tick 至目標時間。");
            var accumulator = (SimulationResultAccumulator)(WpfTestWait.Field(window, "_resultAccumulator")
                ?? throw new InvalidOperationException("結果累積器未建立。"));
            RequireIntervalParity(accumulator, latest!, "baseline-2s");
            var incrementalResources = accumulator.BuildResourceOccupancy(latest.SimulationTimeSeconds);
            var fullResources = ResourceOccupancyAnalysis.Analyze(latest.Events, latest.SimulationTimeSeconds);
            Require(incrementalResources.Resources.SequenceEqual(fullResources.Resources),
                "增量資源占用結果必須與完整事件分析相同。");
            Require(incrementalResources.Intervals.OrderBy(item => item.ResourceId).ThenBy(item => item.StartTimeSeconds)
                    .SequenceEqual(fullResources.Intervals.OrderBy(item => item.ResourceId).ThenBy(item => item.StartTimeSeconds)),
                "增量資源占用區間必須與完整事件分析相同。");
            var noChange = accumulator.Advance(latest);
            Require(noChange.NewEvents.IsEmpty && noChange.NewTrajectorySamples.IsEmpty && noChange.NewSafetyObservations.IsEmpty,
                "相同 frame 再次累積不可重複處理既有歷史。");
            RequireTimetableParity(window, accumulator, latest!);
            RequireComparisonParity(window, accumulator, latest!);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(10));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView");
            latest = WpfTestWait.LatestFrame(window);
            RequireIntervalParity(accumulator, latest!, "baseline-10s");
            RequireTimetableParity(window, accumulator, latest!);
            RequireComparisonParity(window, accumulator, latest!);
            Console.WriteLine("  固定步進與增量資源結果完成");

            WpfTestWait.Wait(worker.PlayAsync(10));
            WpfTestWait.Wait(worker.PauseAsync());
            Require(worker.TryReadLatestFrame(out latest) && latest is not null,
                "Pause 命令處理後仍應發出最新 frame。");
            var pausedAt = latest!.SimulationTimeSeconds;
            Thread.Sleep(60);
            Require(!worker.TryReadLatestFrame(out latest) || latest!.SimulationTimeSeconds <= pausedAt + 0.5,
                "Pause 命令必須停止後續模擬推進。");
            Console.WriteLine("  Pause 命令完成");

            WpfTestWait.Wait(worker.ResetAsync());
            Require(worker.TryReadLatestFrame(out latest) && latest is not null && latest.SimulationTimeSeconds == 0,
                "Reset 必須由 worker 執行並發布時間 0 的新 frame。");
            accumulator.Reset();
            accumulator.Advance(latest!);
            RequireIntervalParity(accumulator, latest!, "baseline-reset");
            RequireTimetableParity(window, accumulator, latest!);
            RequireComparisonParity(window, accumulator, latest!);
            Console.WriteLine("  Reset 命令完成");
        }
        finally
        {
            Console.WriteLine("  worker cleanup 開始");
            WpfTestWait.Close(window);
            Console.WriteLine("  worker cleanup 完成");
        }
    }

    private static void RequireTimetableParity(MainWindow window,
        SimulationResultAccumulator accumulator, PlaybackFrame frame)
    {
        var planned = (IReadOnlyList<SimulationEvent>)(WpfTestWait.Field(window, "_plannedTimetableEvents")
            ?? Array.Empty<SimulationEvent>());
        var expected = OperationsTimetable.Build(frame.GetTopologyResultContext(),
            frame.DispatchPlan!, planned, frame.Events);
        var actual = accumulator.TimetableEntries;
        var mismatch = actual is null ? -1 : Enumerable.Range(0, Math.Min(actual.Count, expected.Count))
            .FirstOrDefault(index => !Equals(actual[index], expected[index]), -1);
        Require(actual is not null && actual.Count == expected.Count && mismatch < 0,
            $"增量時刻表必須與完整事件分析一致；模擬秒 {frame.SimulationTimeSeconds:0.0}；"
            + $"差異列 {mismatch}；實際 {((actual is not null && mismatch >= 0) ? actual[mismatch] : null)}；"
            + $"預期 {(mismatch >= 0 ? expected[mismatch] : null)}。");
    }

    private static void RequireIntervalParity(
        SimulationResultAccumulator accumulator,
        PlaybackFrame frame,
        string label)
    {
        var expected = IntervalStatistics.Analyze(frame.GetTopologyResultContext(),
            frame.Trajectory, frame.Events);
        var actual = accumulator.BuildIntervalStatistics()
            ?? throw new InvalidOperationException($"{label} 未建立增量區間統計。");
        OutputTests.VerifyIncrementalIntervalParity(expected, actual, label);
    }

    private static void RequireComparisonParity(MainWindow window,
        SimulationResultAccumulator accumulator, PlaybackFrame frame)
    {
        var document = (TopologyProjectDocument)(WpfTestWait.Field(window, "_activeTopologyProjectDocument")
            ?? throw new InvalidOperationException("缺少 topology 專案。"));
        var parameters = (TrainParameters)(WpfTestWait.Field(window, "_parameters")
            ?? throw new InvalidOperationException("缺少模擬參數。"));
        var expected = V1V2Comparison.Analyze(frame.GetTopologyResultContext(), frame.DispatchPlan!,
            document.VehicleTypes.Select(item => new VehicleTypeDefinition(
                item.Id, item.DisplayName, item.LengthMeters, item.MaxSpeedMetersPerSecond,
                item.AccelerationMetersPerSecondSquared, item.ServiceBrakeDecelerationMetersPerSecondSquared,
                item.EmergencyBrakeDecelerationMetersPerSecondSquared, item.JerkMetersPerSecondCubed,
                item.TractionDecayPerSecond, item.CoastingDecelerationMetersPerSecondSquared,
                item.DefaultStopPatternId)),
            document.StopPatterns.Select(pattern => new StopPatternDefinition(
                pattern.Id, pattern.DisplayName,
                pattern.Instructions.Select(instruction => new StopPatternInstruction(
                    instruction.StationId, instruction.Action, instruction.DwellTimeSeconds,
                    instruction.PassingSpeedLimitMetersPerSecond)))),
            parameters, frame.Events).Stations;
        var actual = accumulator.ComparisonEntries;
        var mismatch = actual is null ? -1 : Enumerable.Range(0, Math.Min(actual.Count, expected.Count))
            .FirstOrDefault(index => !Equals(actual[index], expected[index]), -1);
        Require(actual is not null && actual.Count == expected.Count && mismatch < 0,
            $"增量 V1/V2 比較必須與完整分析一致；模擬秒 {frame.SimulationTimeSeconds:0.0}；差異列 {mismatch}；"
            + $"實際 {((actual is not null && mismatch >= 0) ? actual[mismatch] : null)}；"
            + $"預期 {(mismatch >= 0 ? expected[mismatch] : null)}。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
