using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private TopologyProjectDocument? _activeTopologyProjectDocument;
    private StationChainageProjection? _stationChainageProjection;

    private void EditTopology_Click(object sender, System.Windows.RoutedEventArgs e)
        => OpenTopologyWorkspace(ProjectWorkspacePage.Project);

    /// <summary>
    /// 將已驗證的 Schema 8 topology 專案接入現有播放殼。V2 world、結果與預覽皆由
    /// topology service routes 建立；路線圖直接讀取 graph，不建立 compatibility Route。
    /// </summary>
    private async Task ConfigureTopologyProjectForPlaybackAsync(
        TopologyProjectDocument document,
        bool lockLegacyInputs = true)
    {
        TopologyProjectFormat.Validate(document);
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var infrastructure = new InfrastructureGraphV4(document.Topology);
        var candidateChainage = new StationChainageProjection(document);

        var actualOptions = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.Decimated(0.2),
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology);
        var plannedOptions = actualOptions with
        {
            ProfileMode = OperationProfileMode.BasicPhysics,
            MovingBlockMode = MovingBlockMode.Independent,
            InitialBrakingEstimationMode = BrakingEstimationMode.Service
        };
        // 候選專案的執行與預覽全部準備成功後，才取代目前播放狀態。
        // 驗證成功不代表計畫時間軸能執行；這些工作不可先寫入視窗欄位。
        var candidateActualWorld = actualOptions.CreateWorld();
        var latestDispatchOffsetSeconds = runtime.DispatchPlan.Runs.Max(run =>
        {
            var offset = (run.PlannedDepartureTime - runtime.DispatchPlan.ScheduleAnchorTime).TotalSeconds;
            return offset < 0 ? offset + TimeSpan.FromDays(1).TotalSeconds : offset;
        });
        // 手動班表的後段車次不一定有全域 headway；播放範圍必須以實際展開後的最後
        // 發車時間為準，否則較晚的接續車次會落在預先建立的計畫時間軸之外。
        var candidateDurationSeconds = latestDispatchOffsetSeconds
            + candidateActualWorld.BaselineCycleTimeSeconds * 1.5;
        var candidateStationRows = new List<StationInputRow>();
        var outboundStops = candidateActualWorld.GetTopologyResultContext().GetStops(TrainDirection.Outbound);
        for (var index = 0; index < outboundStops.Count; index++)
        {
            var station = outboundStops[index];
            candidateStationRows.Add(new StationInputRow
            {
                StationId = station.StationId,
                StationName = station.StationName,
                DistanceFromPreviousKm = index == 0
                    ? 0
                    : (station.ProjectedChainageMeters - outboundStops[index - 1].ProjectedChainageMeters) / 1000,
                DwellTimeSeconds = infrastructure.Stations[station.StationId].DefaultDwellTimeSeconds
            });
        }

        PausePlayback();
        await StopCurrentPlaybackResourcesAsync();
        var candidateWorker = new SimulationPlaybackWorker(candidateActualWorld);
        PlaybackFrame initialFrame;
        try
        {
            initialFrame = await candidateWorker.Ready;
            candidateWorker.TryReadLatestFrame(out _);
        }
        catch
        {
            await candidateWorker.DisposeAsync();
            throw;
        }

        ClearResults();
        _route = null;
        _parameters = runtime.TrainParameters;
        _simulationEngine = null;
        // Schema 8 document 是唯一可保存／可執行的編輯權威。
        _startClockSeconds = document.Simulation.StartClockSeconds;
        _playbackTimeSeconds = 0;
        _activeTopologyProjectDocument = document;
        _stationChainageProjection = candidateChainage;
        SetQuickBuilderState(locked: lockLegacyInputs, collapsed: lockLegacyInputs);
        _activeSimulationProjectDocument = null;
        _v2DispatchPlan = runtime.DispatchPlan;
        _v2Enabled = true;
        _playbackWorker = candidateWorker;
        _latestPlaybackFrame = initialFrame;
        _lastRenderedPlaybackFrameSequence = 0;
        _playbackDurationSeconds = candidateDurationSeconds;
        _plannedTimetableEvents = [];
        _v2PlannedMinimumIntervalSeconds = document.Simulation.HeadwaySeconds;
        _plannedTimelineCancellation = new CancellationTokenSource();
        _plannedTimelineTask = PlannedTimelineWorker.ComputeAsync(
            plannedOptions,
            candidateDurationSeconds,
            _plannedTimelineCancellation.Token);
        ObservePlannedTimelineCompletion(_plannedTimelineTask);
        PopulateFilterControls(runtime.DispatchPlan);
        RouteIdTextBox.Text = document.ProjectId;
        RouteNameTextBox.Text = document.ProjectName;
        StationRows.Clear();
        foreach (var row in candidateStationRows)
        {
            StationRows.Add(row);
        }

        RouteSummaryText.Text = $"{infrastructure.Nodes.Count} 個節點 · {infrastructure.Edges.Count} 個軌道區段";
        OneWaySummaryText.Text = "拓撲路線圖";
        CycleSummaryText.Text = "拓撲專案";
        HeadwaySummaryText.Text = document.Simulation.HeadwaySeconds is { } headway ? FormatDuration(headway) : "單一／同時發車";
        SpeedSummaryText.Text = $"{runtime.TrainParameters.MaxSpeedMetersPerSecond * 3.6:0.#} km/h";
        ObstacleStopButton.IsEnabled = true;
        PlayButton.IsEnabled = true;
        PlaybackStatusText.Text = "實際營運已就緒並可播放；計畫時間軸在背景計算中。";
        PopulateV2Results();
    }
}
