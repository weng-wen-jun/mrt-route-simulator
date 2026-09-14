using MrtRouteSimulator.Engine;

/// <summary>
/// 結果層專用回歸案例。此檔刻意不加入共用 runner；由主測試 runner
/// 明確註冊 <see cref="InboundTopologyResultsUseGlobalDisplayPositions"/> 後執行。
/// </summary>
internal static class TopologyResultsOutputTests
{
    public static void InboundTopologyResultsUseGlobalDisplayPositions()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var dispatch = new ResolvedDispatchPlan(
            DispatchPlanningMode.ManualTimetable,
            VehicleAssignmentMode.ExplicitOnly,
            TimeSpan.Zero,
            [new PlannedServiceRun(
                "RESULT-UP",
                TimeSpan.Zero,
                TrainDirection.Inbound,
                "RESULT-UP-01",
                "DEFAULT_VEHICLE",
                "LOCAL",
                "ALL_STOP",
                "P-E-U",
                0)]);
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                graph,
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent,
            dispatchPlan: dispatch);

        world.AdvanceTo(600);

        var context = world.GetTopologyResultContext();
        var displayStops = context.GetDisplayStations(TrainDirection.Inbound);
        True(displayStops[0].PositionMeters > displayStops[^1].PositionMeters,
            "上行結果站位應沿全線顯示座標由終點側遞減至起點側。 ");

        var timetable = OperationsTimetable.Build(context, dispatch, [], world.Events);
        var origin = timetable.Single(item => item.ServiceRunId == "RESULT-UP" && item.StationId == "E");
        var terminal = timetable.Single(item => item.ServiceRunId == "RESULT-UP" && item.StationId == "W");
        True(origin.ActualDepartureTimeSeconds is not null,
            "上行起站離站事件應能與 topology 顯示站位對應。 ");
        True(terminal.ActualArrivalTimeSeconds is not null,
            "上行終站抵達事件應能與 topology 顯示站位對應。 ");

        var intervals = IntervalStatistics.Analyze(context, world.Trajectory, world.Events);
        True(intervals.CompletedIntervals.Any(item => item.ServiceRunId == "RESULT-UP"
                && item.Direction == TrainDirection.Inbound),
            "上行 topology 區間統計應能找到完成區間。 ");
        True(intervals.JourneyStatistics.Any(item => item.ServiceRunId == "RESULT-UP"
                && item.Direction == TrainDirection.Inbound
                && item.IsComplete),
            "上行 topology 全程統計應能找到完整車次。 ");
        var inboundOnly = IntervalStatistics.Analyze(
            context,
            world.Trajectory,
            world.Events,
            new IntervalStatisticsFilter(Direction: TrainDirection.Inbound));
        var outboundOnly = IntervalStatistics.Analyze(
            context,
            world.Trajectory,
            world.Events,
            new IntervalStatisticsFilter(Direction: TrainDirection.Outbound));
        True(inboundOnly.AllIntervals.All(item => item.Direction == TrainDirection.Inbound)
                && inboundOnly.JourneyStatistics.All(item => item.Direction == TrainDirection.Inbound),
            "區間統計的上行篩選應只保留上行樣本。 ");
        True(outboundOnly.AllIntervals.Count == 0 && outboundOnly.JourneyStatistics.Count == 0,
            "區間統計的下行篩選不應混入上行樣本。 ");

        var arrivalToRemove = world.Events.First(item => item.ServiceRunId == "RESULT-UP"
            && item.EventType == SimulationEventType.Arrival);
        var beforeArrival = IntervalStatistics.Analyze(
            context,
            world.Trajectory,
            world.Events.Where(item => !ReferenceEquals(item, arrivalToRemove)));
        True(beforeArrival.AllIntervals.Any(item => item.ServiceRunId == "RESULT-UP" && !item.IsComplete),
            "拓撲區間在缺少抵達事件時不可只因車頭越過投影中心就誤判完成。 ");

        var intervalCsv = IntervalStatistics.BuildCsv(intervals);
        var journeyCsv = IntervalStatistics.BuildJourneyCsv(intervals);
        var summaryCsv = IntervalStatistics.BuildSummaryCsv(intervals);
        var trajectoryCsv = TrajectoryAnalysis.BuildCsv(world.Trajectory, world.Events, 0);
        True(intervalCsv.Contains("RESULT-UP", StringComparison.Ordinal)
                && intervalCsv.Contains("Inbound", StringComparison.Ordinal),
            "上行區間 CSV 應輸出 topology 車次與方向。 ");
        True(journeyCsv.Contains("RESULT-UP", StringComparison.Ordinal)
                && journeyCsv.Contains("E 東站", StringComparison.Ordinal),
            "上行全程 CSV 應輸出 topology 起終站。 ");
        True(summaryCsv.Contains("Inbound", StringComparison.Ordinal),
            "上行區間摘要 CSV 應保留方向。 ");
        True(trajectoryCsv.Contains("RESULT-UP", StringComparison.Ordinal)
                && trajectoryCsv.Contains("track_id", StringComparison.Ordinal),
            "topology 軌跡 CSV 應保留車次與實體 edge 欄位。 ");
    }

    public static void TrainCenterResultsRequireStationEvents()
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "samples", "PDF-RearTurnback.mrtsim.json")));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
        world.AdvanceTo(3600);
        var context = world.GetTopologyResultContext();
        var run = runtime.DispatchPlan.Runs.First();
        var destination = context.GetStops(run.Direction)[1];
        var arrival = world.Events.First(item => item.ServiceRunId == run.ServiceRunId
            && item.EventType == SimulationEventType.Arrival && item.StationId == destination.StationId);
        var before = arrival.SimulationTimeSeconds - .05;
        var result = IntervalStatistics.Analyze(context,
            world.Trajectory.Where(sample => sample.SimulationTimeSeconds < before),
            world.Events.Where(item => item.SimulationTimeSeconds < before));
        True(!result.CompletedIntervals.Any(item => item.ServiceRunId == run.ServiceRunId
                && item.ToStationId == destination.StationId),
            "TrainCenter 真正到站事件前，不能以車頭通過中心投影提前標記區間完成。");
        True(!context.MatchesStationEvent(run.Direction, destination.StationId,
                arrival with { StationId = null, PlatformId = null, Message = $"車次名稱包含 {destination.StationId}" }),
            "不得從訊息或車次字串猜測站事件身分。");
        True(context.MatchesStationEvent(run.Direction, destination.StationId,
                arrival with { PlatformId = null, Message = "無站名的通過訊息", EventType = SimulationEventType.StationPassed }),
            "通過事件即使未分配月台，仍須依結構化站號辨識。");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx"))) return directory.FullName;
        }

        throw new InvalidOperationException("找不到專案根目錄。 ");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
