using MrtRouteSimulator.Engine;

internal static class SignalBlockTests
{
    public static TopologyProjectDocument Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MrtRouteSimulator.slnx"))) dir = dir.Parent;
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(dir!.FullName, "samples", "handbook-capacity", "01-範例一-最繁忙車站.mrtsim.json")));
        return document with { SpatialCapacity = document.SpatialCapacity! with { Points = document.SpatialCapacity.Points.Select(point => point with { StationId = "B" }).ToArray() } };
    }
    private static SimulationWorld World(TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        return new SimulationWorldOptions(Route: null, TrainParameters: runtime.TrainParameters, OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count, ProfileMode: OperationProfileMode.RealisticOperations, MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology, TraceRetentionPolicy: SimulationTraceRetentionPolicy.EventsOnly).CreateWorld();
    }
    public static void MappingAndRoundTrip()
    {
        var source = Source();
        var mapped = CapacityRuntimeMapper.ApplyStations(source).Document;
        if (mapped.SignalBlocks.Count != 2 || mapped.Topology.Gradients.Count == 0) throw new Exception("缺少實際號誌與坡度");
        if (Math.Abs(mapped.VehicleTypes[0].ServiceBrakeDecelerationMetersPerSecondSquared - .75) > 1e-8) throw new Exception("煞車有效因子未套用");
        var round = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(mapped));
        if (round.SignalBlocks.Count != 2) throw new Exception("閉塞設定未保存");
        if (CapacityRuntimeMapper.ApplyStations(round).Document.SignalBlocks.Count != 2) throw new Exception("重複套用增加號誌");
        var invalid = mapped with { SignalBlocks = [mapped.SignalBlocks[0] with { ClearanceDelaySeconds = -1 }] };
        try { TopologyProjectFormat.Validate(invalid); } catch (SimulationValidationException) { return; }
        throw new Exception("負轉換時間未拒絕");
    }
    public static void RearClearDelayAndReset()
    {
        var mapped = CapacityRuntimeMapper.ApplyStations(Source(), TrainDirection.Outbound).Document;
        mapped = mapped with { SignalBlocks = mapped.SignalBlocks.Select(signal => signal with { ClearanceDelaySeconds = 90 }).ToArray() };
        var template = mapped.Dispatch.ManualTimetableRows!.First(row => row.Direction == TrainDirection.Outbound);
        var rows = Enumerable.Range(0, 2).Select(i => template with { PlannedDepartureTimeSeconds = i * 10,
            VehicleId = "SIG-" + i, ServiceRunId = "SIG-" + i, ContinueAfterTerminal = false, ContinuationServiceRunId = null }).ToArray();
        mapped = mapped with { Dispatch = new(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [], rows) };
        var world = World(mapped); world.AdvanceTo(1200);
        var arrivals = world.Events.Where(e => e.StationId == "B" && e.EventType == SimulationEventType.Arrival).OrderBy(e => e.SimulationTimeSeconds).ToArray();
        if (arrivals.Length != 2 || arrivals[1].SimulationTimeSeconds - arrivals[0].SimulationTimeSeconds < 90) throw new Exception("閉塞未等待車尾淨空與90秒轉換：" + string.Join(",", arrivals.Select(e => e.SimulationTimeSeconds)));
        if (world.Events.Any(e => e.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation or SimulationEventType.SignalAuthorityViolation))
            throw new Exception("閉塞實測觸發安全fallback或違規");
        var times = arrivals.Select(e => e.SimulationTimeSeconds).ToArray();
        world.Reset(); world.AdvanceTo(1200);
        var after = world.Events.Where(e => e.StationId == "B" && e.EventType == SimulationEventType.Arrival).OrderBy(e => e.SimulationTimeSeconds).Select(e => e.SimulationTimeSeconds).ToArray();
        if (!times.SequenceEqual(after)) throw new Exception("重設後號誌狀態未恢復");
    }
    public static void SameRouteSkipsOnlyOptionalChangeoverDelay()
    {
        var mapped = CapacityRuntimeMapper.ApplyStations(Source(), TrainDirection.Outbound).Document;
        mapped = mapped with { SignalBlocks = mapped.SignalBlocks.Select(signal => signal with { ClearanceDelaySeconds = 180, DelayOnlyOnRouteChange = true }).ToArray() };
        var template = mapped.Dispatch.ManualTimetableRows!.First(row => row.Direction == TrainDirection.Outbound);
        var rows = Enumerable.Range(0, 2).Select(i => template with { PlannedDepartureTimeSeconds = i * 10,
            VehicleId = "ROUTE-" + i, ServiceRunId = "ROUTE-" + i, ContinueAfterTerminal = false, ContinuationServiceRunId = null }).ToArray();
        mapped = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(mapped with { Dispatch = new(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [], rows) }));
        var world = World(mapped); world.AdvanceTo(1200);
        double[] Arrivals() => world.Events.Where(e => e.StationId == "B" && e.EventType == SimulationEventType.Arrival).OrderBy(e => e.SimulationTimeSeconds).Select(e => e.SimulationTimeSeconds).ToArray();
        var times = Arrivals();
        if (times.Length != 2 || times[1] - times[0] >= 180) throw new Exception("同一路徑不應額外等待180秒轉轍時間。");
        if (world.Events.Any(e => e.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation or SimulationEventType.SignalAuthorityViolation)) throw new Exception("略過同路徑轉轍等待不得略過閉塞／安全保護。");
        world.Reset(); world.AdvanceTo(1200);
        if (!times.SequenceEqual(Arrivals())) throw new Exception("轉轍路徑記憶未隨Reset復歸。");
        var originalRouteId = mapped.DirectionRouteBindings.Single(binding => binding.Direction == TrainDirection.Outbound).ServiceRouteId;
        var alternate = mapped.ServiceRoutes.Single(route => route.ServiceRouteId == originalRouteId) with { ServiceRouteId = "CHANGE-ROUTE", Name = "切換進路" };
        var changed = mapped with { ServiceRoutes = mapped.ServiceRoutes.Append(alternate).ToArray(), Dispatch = mapped.Dispatch with {
            ManualTimetableRows = rows.Select((row,index) => row with { ServiceRouteId = index == 0 ? originalRouteId : alternate.ServiceRouteId }).ToArray() } };
        var changedWorld = World(changed); changedWorld.AdvanceTo(1200);
        var changedTimes = changedWorld.Events.Where(e => e.StationId == "B" && e.EventType == SimulationEventType.Arrival).OrderBy(e => e.SimulationTimeSeconds).Select(e => e.SimulationTimeSeconds).ToArray();
        if (changedTimes.Length != 2 || changedTimes[1] - changedTimes[0] < 180) throw new Exception("切換進路時仍須等待完整180秒轉轍時間。");
        if (changedWorld.Events.Any(e => e.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation or SimulationEventType.SignalAuthorityViolation)) throw new Exception("切換進路保護出現安全違規。");
    }}
