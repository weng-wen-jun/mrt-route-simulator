using MrtRouteSimulator.Engine;

internal static class TurnbackFootprintTests
{
    public static void ReversalPreservesWholeVehicleForMultipleLengths()
    {
        var rear = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var invalid = rear with { Topology = rear.Topology with { TurnbackFacilities = rear.Topology.TurnbackFacilities
            .Select(f => f.FacilityId == "TURN" ? f with { TurnbackStopPosition = new TrackPosition("TU2", 0) } : f).ToArray() } };
        try
        {
            TopologyProjectFormat.CreateRuntime(invalid);
            throw new InvalidOperationException("尾軌停點未容納整車換端時必須在建置階段拒絕。");
        }
        catch (SimulationValidationException e) when (e.Message.Contains("TURN-005")) { }
        foreach (var length in new[] { 80d, 120d, 140d })
        foreach (var kind in new[] { StationLayoutTemplateKind.FrontTurnback, StationLayoutTemplateKind.RearTurnback })
        {
            var source = StationLayoutTemplateService.Build(kind);
            var document = source with { VehicleTypes = source.VehicleTypes.Select(v => v with { LengthMeters = length }).ToArray() };
            Verify(document, $"{kind}/{length}");
            var suffix = kind == StationLayoutTemplateKind.FrontTurnback ? ":PLATFORM2" : ":TAIL2";
            var alternative = document with { DirectionRouteBindings = document.DirectionRouteBindings
                .Select(b => b with { ServiceRouteId = b.ServiceRouteId + suffix }).ToArray(),
                Dispatch = document.Dispatch with { ManualTimetableRows = document.Dispatch.ManualTimetableRows!.Select(row =>
                    row.Direction == TrainDirection.Outbound && kind == StationLayoutTemplateKind.FrontTurnback
                        ? row with { OriginPlatformId = "A:U" } : row).ToArray() } };
            Verify(alternative, $"{kind}/{suffix}/{length}");
        }
    }

    public static void PocketReversalPreservesWholeVehicleForMultipleLengths()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("找不到範例專案根目錄。");
        var source = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(directory.FullName,
            "samples", "V4.0.0-完整拓撲執行驗證範例.mrtsim.json")));
        foreach (var length in new[] { 80d, 120d, 140d })
        {
            var document = source with { VehicleTypes = source.VehicleTypes.Select(v => v with { LengthMeters = length }).ToArray() };
            Verify(document, $"Pocket/{length}");
            var boundary = document with { Topology = document.Topology with { TurnbackFacilities = document.Topology.TurnbackFacilities
                .Select(f => f with { TurnbackStopPosition = new TrackPosition(f.TurnbackStopPosition!.Value.TrackEdgeId, length) }).ToArray() } };
            Verify(boundary, $"Pocket/車尾恰在入口節點/{length}");
        }
    }

    internal static void Verify(TopologyProjectDocument document, string label)
    {
        var r = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(Route: null, TrainParameters: r.TrainParameters,
            OperationalParameters: r.OperationalParameters, TrainCount: r.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: null, ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode, ServicePatterns: r.ServicePatterns,
            DispatchPlan: r.DispatchPlan, VehicleTypes: r.VehicleTypes, ServiceTypes: r.ServiceTypes, Topology: r.Topology).CreateWorld();
        var reversals = 0;
        for (var tick = 1; tick <= 36000; tick++)
        {
            var before = world.TopologyOccupancy.ToDictionary(p => p.Key, p => p.Value);
            var eventCount = world.Events.Count;
            world.AdvanceTo(tick * .1);
            foreach (var e in world.Events.Skip(eventCount).Where(e => e.EventType is SimulationEventType.TailTrackReturnStarted or SimulationEventType.TurnaroundStarted))
            {
                if (!before.TryGetValue(e.VehicleId!, out var old) || !world.TopologyOccupancy.TryGetValue(e.VehicleId!, out var current))
                    throw new InvalidOperationException($"{label}: 換端前後必須保有 footprint。");
                var first = Normalize(old);
                var second = Normalize(current);
                if (first.Length != second.Length || first.Zip(second).Any(p => p.First.Edge != p.Second.Edge
                    || Math.Abs(p.First.Start - p.Second.Start) > .001 || Math.Abs(p.First.End - p.Second.End) > .001))
                    throw new InvalidOperationException($"{label}: {e.EventType} 車體占用不連續：{string.Join(';', first)} → {string.Join(';', second)}。");
                if (e.EventType == SimulationEventType.TailTrackReturnStarted) reversals++;
            }
        }
        if (reversals == 0 || world.GetSnapshot().Trains.Any(t => t.IsActive))
            throw new InvalidOperationException($"{label}: 必須實際換端並完成全部車次。");
        if (world.Events.Any(e => e.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation))
            throw new InvalidOperationException($"{label}: 換端過程不得有碰撞或停站違規。");
    }

    private static (string Edge, double Start, double End)[] Normalize(TopologyMovementFootprint f) => f.OccupiedIntervals
        .Where(i => i.EndOffsetMeters - i.StartOffsetMeters > 1e-7).GroupBy(i => i.TrackEdgeId)
        .OrderBy(g => g.Key).Select(g => (g.Key, g.Min(i => i.StartOffsetMeters), g.Max(i => i.EndOffsetMeters))).ToArray();
}
