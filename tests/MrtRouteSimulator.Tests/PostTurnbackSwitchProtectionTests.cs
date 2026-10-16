using MrtRouteSimulator.Engine;

internal static class PostTurnbackSwitchProtectionTests
{
    public static void DepartureSwitchRemainsLockedUntilRearClearsAfterStationaryTurnback()
    {
        var document = BuildDiamondFixture();
        TopologyProjectFormat.Validate(document);
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(Route: null, TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters, TrainCount: runtime.DispatchPlan.Runs.Count,
            ProfileMode: document.Simulation.ProfileMode, MovingBlockMode: document.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
        const string resource = "TOPOLOGY:SWITCH:DIAMOND";
        var sawHeadCrossingWhileRearInFacility = false;
        var sawLockSurviveFacilityClear = false;
        var sawRearClearRelease = false;
        var reserved = false;
        var nextEvent = 0;
        for (var tick = 0; tick < 18000 && !world.IsComplete; tick++)
        {
            world.Tick();
            foreach (var item in world.Events.Skip(nextEvent))
            {
                if (item.ServiceRunId != "DOWN-1"
                    || item.ResourceId != resource && item.ResourceIds?.Contains(resource) != true) continue;
                if (item.EventType == SimulationEventType.RouteReserved) reserved = true;
                if (item.EventType != SimulationEventType.RouteReleased) continue;
                True(reserved, "交叉點釋放前必須已取得進路。");
                True(world.TopologyOccupancy.TryGetValue("V1", out var releaseFootprint)
                    && (releaseFootprint.Rear.Position.TrackEdgeId == "X2:A"
                        || releaseFootprint.Rear.Position.TrackEdgeId is "D1" or "D2"),
                    "車尾尚未越過平面交叉點，不得釋放進路。");
                reserved = false;
                sawRearClearRelease = true;
            }
            nextEvent = world.Events.Count;
            if (!world.TopologyOccupancy.TryGetValue("V1", out var footprint)
                || !world.Events.Any(item => item.ServiceRunId == "DOWN-1"
                    && item.EventType == SimulationEventType.Departure)) continue;

            // X2 is traversed in reverse: B is before the diamond, A after it.
            if (footprint.Front.Position.TrackEdgeId != "X2:A") continue;
            if (footprint.OccupiedIntervals.Any(interval => interval.TrackEdgeId == "U0"))
            {
                sawHeadCrossingWhileRearInFacility = true;
                True(reserved, "折返 movement 尚待車尾淨空時，車頭通過下一交叉點前仍須鎖定道岔。");
            }
            if (footprint.Rear.Position.TrackEdgeId == "X2:B")
            {
                sawLockSurviveFacilityClear = true;
                True(reserved, "車尾離開折返 U0 後，交叉點進路仍須保留到車尾越過交叉點。");
            }
            // This also exercises route-lock rendering while the head uses the
            // departure leg and after it changes back to the mainline navigator.
            _ = world.GetActiveRouteLocks();
        }
        True(sawHeadCrossingWhileRearInFacility, "案例必須實際涵蓋車頭越過交叉點、車尾仍在折返軌的狀態。");
        True(sawLockSurviveFacilityClear, "案例必須涵蓋折返 movement 釋放後、交叉點車尾尚未淨空的狀態。");
        True(sawRearClearRelease && !reserved, "交叉點進路須於車尾淨空後釋放。");
        True(world.IsComplete, "折返接續車次須有界完成。");
        True(!world.Events.Any(item => item.EventType is SimulationEventType.Collision
            or SimulationEventType.PredictedCollision or SimulationEventType.StationStopViolation),
            "折返接續不得產生碰撞或停站違規。");
    }

    private static TopologyProjectDocument BuildDiamondFixture()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.FrontTurnback);
        var split = source.Topology.Edges.Where(edge => edge.TrackEdgeId is "X1" or "X2")
            .ToDictionary(edge => edge.TrackEdgeId);
        DirectedTrackTraversal[] Expand(IReadOnlyList<DirectedTrackTraversal> traversals) => traversals.SelectMany(t =>
            !split.ContainsKey(t.TrackEdgeId) ? new[] { t }
            : t.Direction == TraversalDirection.Forward
                ? new[] { t with { TrackEdgeId = t.TrackEdgeId + ":A" }, t with { TrackEdgeId = t.TrackEdgeId + ":B" } }
                : new[] { t with { TrackEdgeId = t.TrackEdgeId + ":B" }, t with { TrackEdgeId = t.TrackEdgeId + ":A" } }).ToArray();
        var routes = source.ServiceRoutes.Select(route => route with { Traversals = Expand(route.Traversals) }).ToArray();
        var topology = source.Topology with
        {
            Nodes = source.Topology.Nodes.Append(new TrackNodeDefinition("DIAMOND", "合成交叉點", TrackNodeKind.Crossing)).ToArray(),
            Edges = source.Topology.Edges.SelectMany(edge => !split.ContainsKey(edge.TrackEdgeId) ? new[] { edge } : new[]
            {
                edge with { TrackEdgeId = edge.TrackEdgeId + ":A", ToNodeId = "DIAMOND", LengthMeters = 85, ToPortSide = TrackPortSide.A },
                edge with { TrackEdgeId = edge.TrackEdgeId + ":B", FromNodeId = "DIAMOND", LengthMeters = 85, FromPortSide = TrackPortSide.B }
            }).ToArray(),
            DirectedConnections = [],
            TurnbackFacilities = source.Topology.TurnbackFacilities.Select(facility => facility with
                { Traversals = Expand(facility.Traversals) }).ToArray()
        };
        topology = topology with { DirectedConnections = DirectedTrackConnectionRules.Build(topology, routes) };
        return source with
        {
            Topology = topology,
            ServiceRoutes = routes,
            VehicleTypes = source.VehicleTypes.Select(vehicle => vehicle with { LengthMeters = 140 }).ToArray(),
            Operations = source.Operations with { TrainLengthMeters = 140 },
            DirectionRouteBindings = source.DirectionRouteBindings.Select(binding => binding with
                { ServiceRouteId = binding.ServiceRouteId + ":PLATFORM2" }).ToArray(),
            Dispatch = source.Dispatch with { ManualTimetableRows = source.Dispatch.ManualTimetableRows!.Select(row =>
                row.Direction == TrainDirection.Outbound ? row with { OriginPlatformId = "A:U", PlannedDepartureTimeSeconds = 0 } : row).ToArray() }
        };
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
