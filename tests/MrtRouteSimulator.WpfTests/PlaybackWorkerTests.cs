using System.Collections;
using System.IO;
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
            Require(latest.Performance.FrameDropCount >= 5,
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
            Console.WriteLine("  Reset 命令完成");
        }
        finally
        {
            Console.WriteLine("  worker cleanup 開始");
            WpfTestWait.Close(window);
            Console.WriteLine("  worker cleanup 完成");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
