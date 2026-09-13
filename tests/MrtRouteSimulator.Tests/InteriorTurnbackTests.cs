using MrtRouteSimulator.Engine;

internal static class InteriorTurnbackTests
{
    public static void NavigatorTraversesInteriorStopsWithoutSkippingEdges()
    {
        var doc = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var graph = new InfrastructureGraphV4(doc.Topology);
        var facility = doc.Topology.TurnbackFacilities[0];
        var navigator = new TopologyMovementNavigator(graph, new() {
            MovementPlanId = "MIDPOINT-CHECK", Legs = [
                new() { LegId = "ARR", Kind = MovementLegKind.ServiceRoute,
                    Traversals = TopologyMovementPlanResolver.ResolveTraversals(graph, [new("U0", TraversalDirection.Forward)]) },
                TopologyMovementPlanResolver.ResolveFacilityLeg(graph, facility),
                new() { LegId = "DEP", Kind = MovementLegKind.ServiceRoute,
                    Traversals = TopologyMovementPlanResolver.ResolveTraversals(graph, [new("D0", TraversalDirection.Forward)]) }
            ] });
        var start = navigator.CreateCursor(0, 0, 470);
        var end = navigator.CreateCursor(2, 0, 130);
        var length = navigator.TryGetForwardDistance(start, end)!.Value;
        var facilityLength = navigator.Legs[1].Traversals.Sum(t => t.LengthMeters);
        if (Math.Abs(length - (130 + facilityLength + 130)) > .001)
            throw new InvalidOperationException("中段停點間必須包含剩餘到達軌道、完整設施與出發軌道130公尺。");
        var cursor = navigator.Advance(start, length);
        if (!cursor.Position.ApproximatelyEquals(end.Position))
            throw new InvalidOperationException("連續 Advance 未到達出發停點。");
        var disconnected = doc.Topology with { DirectedConnections = doc.Topology.DirectedConnections
            .Where(c => !(c.FromTrackEdgeId == "TU0" && c.ToTrackEdgeId == "TU1")).ToArray() };
        if (InfrastructureValidator.Validate(disconnected, doc.ServiceRoutes).IsValid)
            throw new InvalidOperationException("不連續／禁止轉向的折返路徑必須拒絕。");
    }

    public static void WorldDrivesToInteriorDeparturePlatform()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var doc = source with { Topology = source.Topology with {
            Platforms = source.Topology.Platforms.Select(p => p.PlatformId == "A:D"
                ? p with { StopPositionOffsetMeters = 130, PlatformStartOffsetMeters = 0, PlatformEndOffsetMeters = 260, EffectiveLengthMeters = 260 }
                : p.PlatformId == "A:U" ? p with { StopPositionOffsetMeters = 470, PlatformStartOffsetMeters = 340, PlatformEndOffsetMeters = 600, EffectiveLengthMeters = 260 } : p).ToArray(),
            TurnbackFacilities = source.Topology.TurnbackFacilities.Select(f => f with {
                ArrivalStopOffsetMeters = 470, DepartureStartOffsetMeters = 130 }).ToArray() } };
        var runtime = TopologyProjectFormat.CreateRuntime(doc);
        var invalid = doc with { Topology = doc.Topology with { TurnbackFacilities = doc.Topology.TurnbackFacilities
            .Select(f => f with { ArrivalStopOffsetMeters = 490 }).ToArray() } };
        if (InfrastructureValidator.Validate(invalid.Topology, invalid.ServiceRoutes).IsValid)
            throw new InvalidOperationException("非月台停點的任意edge中段不可當折返銜接點。");
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, null, ProfileMode: doc.Simulation.ProfileMode,
            MovingBlockMode: doc.Simulation.MovingBlockMode, ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        world.AdvanceTo(1500);
        var returning = world.Trajectory.Where(t => t.Phase == OperationalPhase.TailTrackReturn && t.TrackEdgeId == "D0").ToArray();
        if (returning.Length < 10 || returning.Min(t => t.OffsetMeters) > 5 || returning.Max(t => t.OffsetMeters) < 125)
            throw new InvalidOperationException("返回列車必須逐步駛過 D0 的0至130公尺，不可瞬移到月台。");
        foreach (var pair in returning.Zip(returning.Skip(1)))
            if (Math.Abs(pair.Second.OffsetMeters!.Value - pair.First.OffsetMeters!.Value) > 3)
                throw new InvalidOperationException("返回軌跡在相鄰0.1秒內跳動過大。");
        var departureHead = PlatformStopPositionResolver.ResolveHeadPosition(
            doc.Topology.Platforms.Single(p => p.PlatformId == "A:D"), TraversalDirection.Forward,
            doc.VehicleTypes[0].LengthMeters).OffsetMeters;
        if (!world.Events.Any(e => e.EventType == SimulationEventType.Arrival && e.TrackEdgeId == "D0"
            && e.OffsetMeters is { } offset && Math.Abs(offset - departureHead) < .001))
            throw new InvalidOperationException("返回到站事件必須落在出發月台停點。");
        if (world.GetSnapshot().Trains.Any(t => t.IsActive))
            throw new InvalidOperationException("中段月台折返車次必須完成。");
    }
}
