using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private const double MinimumPlannedTimelineSafetyMarginSeconds = 1000;
    private const double PlannedTimelineSafetyMarginRatio = 0.20;
    private TopologyProjectDocument? _activeTopologyProjectDocument;
    private StationChainageProjection? _stationChainageProjection;

    private sealed record PreparedTopologyProjectPlayback(
        TopologyProjectDocument Document,
        TopologyProjectRuntime Runtime,
        InfrastructureGraphV4 Infrastructure,
        StationChainageProjection StationChainage,
        SimulationSession Session,
        double DurationSeconds,
        IReadOnlyList<PreparedStationRow> OutboundStations);

    private sealed record PreparedStationRow(
        string StationId,
        string StationName,
        double DistanceFromPreviousKm,
        double DwellTimeSeconds);

    private void EditTopology_Click(object sender, System.Windows.RoutedEventArgs e)
        => OpenTopologyWorkspace(ProjectWorkspacePage.Project);

    /// <summary>
    /// 將已驗證的 Schema 8 topology 專案接入現有播放殼。V2 world、結果與預覽皆由
    /// topology service routes 建立；路線圖直接讀取 graph，不建立 compatibility Route。
    /// </summary>
    private void ConfigureTopologyProjectForPlayback(
        TopologyProjectDocument document,
        bool lockLegacyInputs = true)
    {
        ApplyPreparedTopologyProjectForPlayback(
            PrepareTopologyProjectForPlayback(document),
            lockLegacyInputs);
    }

    /// <summary>
    /// 建立候選播放工作階段，不接觸 WPF 視窗狀態；大型存檔讀取時可在背景執行。
    /// </summary>
    private static PreparedTopologyProjectPlayback PrepareTopologyProjectForPlayback(
        TopologyProjectDocument document,
        IProgress<(double Percentage, string Message)>? progress = null)
    {
        progress?.Report((61, "正在驗證 topology 結構…"));
        TopologyProjectFormat.Validate(document);
        progress?.Report((63, "正在建立 topology runtime…"));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        progress?.Report((65, $"正在建立基礎設施索引… {document.Topology.Nodes.Count} 個節點、{document.Topology.Edges.Count} 個軌道區段"));
        var infrastructure = new InfrastructureGraphV4(document.Topology);
        progress?.Report((67, "正在建立車站里程投影…"));
        var candidateChainage = new StationChainageProjection(document);

        progress?.Report((69, $"正在建立實際／計畫模擬世界… {runtime.DispatchPlan.Runs.Count} 個計畫車次"));
        var actualOptions = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.Decimated(0.5),
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(1.0),
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology);
        var plannedOptions = actualOptions with
        {
            ProfileMode = OperationProfileMode.BasicPhysics,
            MovingBlockMode = MovingBlockMode.Independent,
            InitialBrakingEstimationMode = BrakingEstimationMode.Service,
            TraceRetentionPolicy = SimulationTraceRetentionPolicy.Full,
            SafetyObservationRetentionPolicy = SafetyObservationRetentionPolicy.Full
        };
        // 候選專案的執行與預覽全部準備成功後，才取代目前播放狀態。
        // 驗證成功不代表計畫時間軸能執行；這些工作不可先寫入視窗欄位。
        var candidateSession = new SimulationSession(actualOptions, plannedOptions);
        var latestDispatchOffsetSeconds = runtime.DispatchPlan.Runs.Max(run =>
        {
            var offset = (run.PlannedDepartureTime - runtime.DispatchPlan.ScheduleAnchorTime).TotalSeconds;
            return offset < 0 ? offset + TimeSpan.FromDays(1).TotalSeconds : offset;
        });
        // 手動班表的後段車次不一定有全域 headway；播放範圍必須以實際展開後的最後
        // 發車時間為準，否則較晚的接續車次會落在預先建立的計畫時間軸之外。
        var maxCandidateDurationSeconds = CalculateMaximumCandidateDurationSeconds(
            latestDispatchOffsetSeconds,
            candidateSession.ActualWorld.BaselineCycleTimeSeconds);
        progress?.Report((70, $"正在計算計畫時間軸… 安全上限 {FormatSeconds(maxCandidateDurationSeconds)}"));
        var timelineProgress = progress is null
            ? null
            : new Progress<SimulationTimelineProgress>(update =>
            {
                var percentage = 70 + update.Ratio * 19;
                var phase = update.IsComplete ? "計畫時間軸已完成" : "正在計算計畫時間軸…";
                progress.Report((
                    percentage,
                    $"{phase} 模擬 {FormatSeconds(update.SimulationTimeSeconds)} / {FormatSeconds(update.MaximumDurationSeconds)}；"
                    + $"已完成 {update.CompletedTrainCount}/{update.TotalTrainCount} 列車；"
                    + $"事件 {update.EventCount:N0}、軌跡樣本 {update.TrajectorySampleCount:N0}"));
            });
        candidateSession.PreparePlannedTimelineUntilComplete(maxCandidateDurationSeconds, timelineProgress);
        progress?.Report((90, $"計畫時間軸完成（{FormatSeconds(candidateSession.PlannedTimelineCompletedAtSeconds ?? 0)}）；正在整理車站資料…"));
        var candidateDurationSeconds = candidateSession.PlannedTimelineCompletedAtSeconds!.Value
            + SimulationWorld.FixedTimeStepSeconds;
        var candidateStationRows = new List<PreparedStationRow>();
        var outboundStops = candidateSession.ActualWorld.GetTopologyResultContext().GetStops(TrainDirection.Outbound);
        for (var index = 0; index < outboundStops.Count; index++)
        {
            var station = outboundStops[index];
            candidateStationRows.Add(new PreparedStationRow(
                station.StationId,
                station.StationName,
                index == 0 ? 0 : (station.ProjectedChainageMeters - outboundStops[index - 1].ProjectedChainageMeters) / 1000,
                infrastructure.Stations[station.StationId].DefaultDwellTimeSeconds));
        }

        progress?.Report((91, $"已整理 {candidateStationRows.Count} 個車站；準備套用讀取結果…"));

        return new PreparedTopologyProjectPlayback(
            document,
            runtime,
            infrastructure,
            candidateChainage,
            candidateSession,
            candidateDurationSeconds,
            candidateStationRows);
    }

    private static double CalculateMaximumCandidateDurationSeconds(
        double latestDispatchOffsetSeconds,
        double baselineCycleTimeSeconds)
    {
        // 預估完成時間為最後發車後的一個基準週期；額外緩衝至少 1,000 秒，
        // 或基準週期的 20%，取兩者較大值。
        var estimatedCompletionSeconds = latestDispatchOffsetSeconds + baselineCycleTimeSeconds;
        var safetyMarginSeconds = Math.Max(
            MinimumPlannedTimelineSafetyMarginSeconds,
            baselineCycleTimeSeconds * PlannedTimelineSafetyMarginRatio);
        return estimatedCompletionSeconds + safetyMarginSeconds;
    }

    /// <summary>在候選專案完整準備成功後，才替換目前的 WPF 播放狀態。</summary>
    private void ApplyPreparedTopologyProjectForPlayback(
        PreparedTopologyProjectPlayback prepared,
        bool lockLegacyInputs)
    {
        var document = prepared.Document;
        var runtime = prepared.Runtime;
        var infrastructure = prepared.Infrastructure;

        PausePlayback();
        ClearResults();
        _route = null;
        _parameters = runtime.TrainParameters;
        _simulationEngine = null;
        // Schema 8 document 是唯一可保存／可執行的編輯權威。
        _startClockSeconds = document.Simulation.StartClockSeconds;
        _playbackTimeSeconds = 0;
        _activeTopologyProjectDocument = document;
        _stationChainageProjection = prepared.StationChainage;
        UpdateFixedTimetableArchiveExportState();
        SetQuickBuilderState(locked: lockLegacyInputs, collapsed: lockLegacyInputs);
        _activeSimulationProjectDocument = null;
        _v2DispatchPlan = runtime.DispatchPlan;
        _v2Enabled = true;
        _v2Session = prepared.Session;
        _playbackDurationSeconds = prepared.DurationSeconds;
        _plannedTimetableEvents = prepared.Session.PlannedEvents;
        _v2PlannedMinimumIntervalSeconds = document.Simulation.HeadwaySeconds;
        ApplyTopologyCatalogRows(document);
        _suppressEngineModeSelectionChanged = true;
        try
        {
            SelectComboBoxTag(EngineModeComboBox, SimulationEngineKind.V2RealisticOperations.ToString());
        }
        finally
        {
            _suppressEngineModeSelectionChanged = false;
        }
        ApplyEngineModeUiState();
        PopulateFilterControls(runtime.DispatchPlan);
        RouteIdTextBox.Text = document.ProjectId;
        RouteNameTextBox.Text = document.ProjectName;
        StationRows.Clear();
        foreach (var row in prepared.OutboundStations)
        {
            StationRows.Add(new StationInputRow
            {
                StationId = row.StationId,
                StationName = row.StationName,
                DistanceFromPreviousKm = row.DistanceFromPreviousKm,
                DwellTimeSeconds = row.DwellTimeSeconds
            });
        }

        RouteSummaryText.Text = $"{infrastructure.Nodes.Count} 個節點 · {infrastructure.Edges.Count} 個軌道區段";
        OneWaySummaryText.Text = "拓撲路線圖";
        CycleSummaryText.Text = "拓撲專案";
        HeadwaySummaryText.Text = document.Simulation.HeadwaySeconds is { } headway ? FormatDuration(headway) : "單一／同時發車";
        SpeedSummaryText.Text = $"{runtime.TrainParameters.MaxSpeedMetersPerSecond * 3.6:0.#} km/h";
        ObstacleStopButton.IsEnabled = true;
        PlayButton.IsEnabled = true;
        PlaybackStatusText.Text = "拓撲專案已就緒，按「播放」查看列車運行。";
        PopulateV2Results();
    }

    private void ApplyTopologyCatalogRows(TopologyProjectDocument document)
    {
        Replace(VehicleTypeRows, document.VehicleTypes.Select(item => new VehicleTypeInputRow
        {
            Id = item.Id,
            Name = item.DisplayName,
            LengthMeters = item.LengthMeters,
            MaxSpeedKmh = item.MaxSpeedMetersPerSecond * 3.6,
            Acceleration = item.AccelerationMetersPerSecondSquared,
            ServiceBrake = item.ServiceBrakeDecelerationMetersPerSecondSquared,
            EmergencyBrake = item.EmergencyBrakeDecelerationMetersPerSecondSquared,
            Jerk = item.JerkMetersPerSecondCubed,
            TractionDecay = item.TractionDecayPerSecond,
            CoastingDeceleration = item.CoastingDecelerationMetersPerSecondSquared,
            DefaultStopPatternId = item.DefaultStopPatternId ?? string.Empty
        }));
        Replace(ServiceTypeRows, document.ServiceTypes.Select(item => new ServiceTypeInputRow
        {
            Id = item.Id,
            Name = item.DisplayName,
            ColorHex = item.ColorHex,
            RunPrefix = item.RunPrefix,
            DefaultStopPatternId = item.DefaultStopPatternId ?? string.Empty,
            DefaultVehicleTypeId = item.DefaultVehicleTypeId ?? string.Empty,
            Priority = item.Priority,
            CanRequestOvertake = item.CanRequestOvertake,
            PreferredPlatformIds = string.Join(",", item.PreferredPlatformIds ?? [])
        }));
        ServicePatternRows.Clear();
        foreach (var pattern in document.StopPatterns.Where(item =>
                     !item.Id.Equals("ALL_STOP", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var instruction in pattern.Instructions)
            {
                ServicePatternRows.Add(new ServicePatternInputRow
                {
                    PatternId = pattern.Id,
                    PatternName = pattern.DisplayName,
                    StationId = instruction.StationId,
                    Mode = instruction.Action switch
                    {
                        StopPatternAction.Pass => "跨站",
                        StopPatternAction.Turnback => "折返",
                        _ => "停站"
                    },
                    DwellTimeSeconds = instruction.DwellTimeSeconds,
                    SpeedLimitKmh = instruction.PassingSpeedLimitMetersPerSecond * 3.6
                });
            }
        }
    }
}
