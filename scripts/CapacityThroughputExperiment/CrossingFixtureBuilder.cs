using MrtRouteSimulator.Engine;

/// <summary>Explicit synthetic flat diamond; never infers physical connectivity from drawing coordinates.</summary>
public static class CrossingFixtureBuilder
{
    public const string DiamondNodeId = "TURN:DIAMOND";

    /// <summary>Bounded structural/two-pair operational gate; does not run the throughput experiment.</summary>
    public static void Smoke(string root)
    {
        var fixtures = TurnbackFixtures.Build(root);
        Console.WriteLine($"DIAMOND STRUCTURAL {fixtures.Count} fixtures validated");
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var prepare = typeof(TurnbackExperiment).GetMethod("PrepareDocument", flags)!;
        var createWorld = typeof(TurnbackExperiment).GetMethod("CreateWorld", flags)!;
        foreach (var fixture in fixtures.Where(item => item.Name is "case02-station-front" or "case08-s14-rear-double"))
        {
            var prepared = prepare.Invoke(null, [fixture, fixture.Theory.DesignHeadwaySeconds])!;
            var document = (TopologyProjectDocument)prepared.GetType().GetProperty("Document")!.GetValue(prepared)!;
            var inbound = document.Dispatch.ManualTimetableRows!.Where(row => row.Direction == fixture.MeasuredArrivalDirection)
                .OrderBy(row => row.PlannedDepartureTimeSeconds).Take(2).ToArray();
            var runIds = inbound.SelectMany(row => new[] { row.ServiceRunId!, row.ContinuationServiceRunId! })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = document.Dispatch.ManualTimetableRows!.Where(row => runIds.Contains(row.ServiceRunId!)).ToArray();
            document = document with { Dispatch = document.Dispatch with { ManualTimetableRows = rows },
                Simulation = document.Simulation with { TrainCount = rows.Length } };
            TopologyProjectFormat.Validate(document);
            var runtime = TopologyProjectFormat.CreateRuntime(document);
            var world = (SimulationWorld)createWorld.Invoke(null, [document, runtime])!;
            world.AdvanceTo(1800);
            var events = world.Events;
            var diamondResource = $"TOPOLOGY:SWITCH:{DiamondNodeId}";
            bool HasDiamond(SimulationEvent item) => item.ResourceId == diamondResource
                || item.ResourceIds?.Contains(diamondResource, StringComparer.OrdinalIgnoreCase) == true;
            var reserves = events.Where(item => item.EventType == SimulationEventType.RouteReserved && HasDiamond(item)).ToArray();
            var releases = events.Where(item => item.EventType == SimulationEventType.RouteReleased && HasDiamond(item)).ToArray();
            var returns = events.Where(item => item.EventType == SimulationEventType.Departure
                && item.Direction != fixture.MeasuredArrivalDirection && item.StationId == fixture.MeasuredStationId).ToArray();
            var errors = events.Where(item => item.EventType is SimulationEventType.Collision
                or SimulationEventType.PredictedCollision or SimulationEventType.StationStopViolation).ToArray();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { fixture = fixture.Name,
                simulationSeconds = world.CurrentTimeSeconds, returnedRoutes = returns.Select(item => item.ServiceRouteId),
                diamondReservations = reserves.Length, diamondRearClearReleases = releases.Length,
                safetyErrors = errors.Length, firstVehicleTiming = events.Where(item => item.VehicleId == inbound[0].VehicleId)
                    .Where(item => item.StationId == "A" || item.EventType is SimulationEventType.TurnaroundStarted
                        or SimulationEventType.TailTrackReached or SimulationEventType.TailTrackReturnStarted
                        or SimulationEventType.DirectionChanged)
                    .Select(item => new { type = item.EventType.ToString(), time = item.SimulationTimeSeconds,
                        item.StationId, item.ResourceId, item.ServiceRunId }),
                diamondEvents = events.Where(HasDiamond).Select(item => new { type = item.EventType.ToString(),
                    time = item.SimulationTimeSeconds, item.ServiceRunId }),
                allArrivals = events.Where(item => item.EventType == SimulationEventType.Arrival)
                    .Select(item => new { item.ServiceRunId, item.StationId, time = item.SimulationTimeSeconds }) }));
            if (returns.Select(item => item.ServiceRunId).Distinct().Count() != 2
                || fixture.TurnbackBindings.Any(binding => !returns.Any(item => item.ServiceRouteId == binding.DepartureServiceRouteId))
                || reserves.Length < 2 || releases.Length < 2 || errors.Length != 0)
                throw new InvalidOperationException($"{fixture.Name}: returns={returns.Length}, diamond reserves/releases={reserves.Length}/{releases.Length}, errors={errors.Length}");
        }
    }

    public static TopologyProjectDocument AddSharedDiamond(TopologyProjectDocument source)
    {
        var topology = source.Topology;
        var originals = topology.Edges.Where(edge => edge.TrackEdgeId is "X1" or "X2")
            .ToDictionary(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        if (originals.Count != 2 || topology.Nodes.Any(node => node.NodeId == DiamondNodeId))
            throw new InvalidOperationException("Diamond fixture requires exactly X1/X2 and no existing diamond.");
        bool IsSplit(string id) => originals.ContainsKey(id);
        if (topology.Platforms.Any(platform => IsSplit(platform.TrackEdgeId))
            || topology.SpeedLimits.Any(limit => IsSplit(limit.TrackEdgeId))
            || topology.Gradients.Any(segment => IsSplit(segment.TrackEdgeId))
            || topology.Curves.Any(segment => IsSplit(segment.TrackEdgeId))
            || topology.TurnbackFacilities.Any(facility => facility.TurnbackStopPosition is { } stop && IsSplit(stop.TrackEdgeId))
            || topology.PassingFacilities.Count != 0)
            throw new InvalidOperationException("Diamond split does not support stops, spatial segments or passing facilities on X1/X2.");

        DirectedTrackTraversal[] Expand(IReadOnlyList<DirectedTrackTraversal> path) => path.SelectMany(traversal =>
            !IsSplit(traversal.TrackEdgeId) ? new[] { traversal }
            : traversal.Direction == TraversalDirection.Forward
                ? new[] { new DirectedTrackTraversal(traversal.TrackEdgeId + ":A", traversal.Direction), new DirectedTrackTraversal(traversal.TrackEdgeId + ":B", traversal.Direction) }
                : new[] { new DirectedTrackTraversal(traversal.TrackEdgeId + ":B", traversal.Direction), new DirectedTrackTraversal(traversal.TrackEdgeId + ":A", traversal.Direction) }).ToArray();

        (string EdgeId, double Offset) Remap(string edgeId, double offset)
        {
            if (!originals.TryGetValue(edgeId, out var edge)) return (edgeId, offset);
            var half = edge.LengthMeters / 2;
            return offset <= half ? (edgeId + ":A", offset) : (edgeId + ":B", offset - half);
        }

        var edges = topology.Edges.SelectMany(edge => !IsSplit(edge.TrackEdgeId) ? new[] { edge } : new[]
        {
            edge with { TrackEdgeId = edge.TrackEdgeId + ":A", ToNodeId = DiamondNodeId,
                LengthMeters = edge.LengthMeters / 2, ToPortSide = TrackPortSide.A },
            edge with { TrackEdgeId = edge.TrackEdgeId + ":B", FromNodeId = DiamondNodeId,
                LengthMeters = edge.LengthMeters / 2, FromPortSide = TrackPortSide.B }
        }).ToArray();
        var routes = source.ServiceRoutes.Select(route => route with { Traversals = Expand(route.Traversals) }).ToArray();
        var facilities = topology.TurnbackFacilities.Select(facility =>
        {
            var arrival = Remap(facility.ArrivalTrackEdgeId, facility.ArrivalStopOffsetMeters);
            var departure = Remap(facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters);
            return facility with { Traversals = Expand(facility.Traversals),
                ArrivalTrackEdgeId = arrival.EdgeId, ArrivalStopOffsetMeters = arrival.Offset,
                DepartureTrackEdgeId = departure.EdgeId, DepartureStartOffsetMeters = departure.Offset };
        }).ToArray();
        topology = topology with { Nodes = topology.Nodes.Append(new TrackNodeDefinition(DiamondNodeId,
            "示範共用平面交叉點", TrackNodeKind.Crossing)).ToArray(), Edges = edges,
            TurnbackFacilities = facilities, DirectedConnections = [] };
        topology = topology with { DirectedConnections = DirectedTrackConnectionRules.Build(topology, routes) };

        // The four arms share a conflict node, but only the original through pairs
        // are legal. A/B ports permit continuity; the whitelist excludes cross-turns.
        var diamondConnections = topology.DirectedConnections.Where(connection =>
            edges.Single(edge => edge.TrackEdgeId == connection.FromTrackEdgeId) is { } from
            && (connection.FromDirection == TraversalDirection.Forward ? from.ToNodeId : from.FromNodeId) == DiamondNodeId);
        if (diamondConnections.Any(connection => connection.FromTrackEdgeId[..2] != connection.ToTrackEdgeId[..2]))
            throw new InvalidOperationException("Diamond contains an undeclared cross-turn.");
        var result = source with { Topology = topology, ServiceRoutes = routes };
        TopologyProjectFormat.Validate(result);
        return result;
    }
}
