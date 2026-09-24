namespace MrtRouteSimulator.Engine;

/// <summary>
/// 以起始站月台中心為0K的顯示里程。主線按車站中心校準，支線依實體銜接延伸；
/// 只提供呈現座標，負值不會寫入edge-local cursor或改變物理長度。
/// </summary>
public sealed class StationChainageProjection
{
    private sealed record RouteDisplayMap(
        IReadOnlyDictionary<string, Func<double, double>> EdgeMaps);

    private readonly Dictionary<string, (double From, double To, double Length)> edges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> stations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<double, double>> edgeMaps = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, double> StationCenters => stations;
    public double MinimumChainageMeters => edges.Values.SelectMany(e => new[] { e.From, e.To }).DefaultIfEmpty(0).Min();
    public double MaximumChainageMeters => edges.Values.SelectMany(e => new[] { e.From, e.To }).DefaultIfEmpty(0).Max();

    public static StationChainageProjection? TryCreate(TopologyProjectDocument document)
    {
        try { return new StationChainageProjection(document); }
        catch (SimulationValidationException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (ArgumentException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    public StationChainageProjection(TopologyProjectDocument document)
    {
        var graph = new InfrastructureGraphV4(document.Topology);
        var primaryId = document.DirectionRouteBindings.Single(b => b.Direction == TrainDirection.Outbound).ServiceRouteId;
        var primary = document.ServiceRoutes.Single(r => r.ServiceRouteId == primaryId);
        var primaryProjection = new RouteProjection(graph, primary);
        var primaryStops = Centers(primary, primaryProjection);
        if (primaryStops.Length == 0) throw new SimulationValidationException(["中心里程需要至少一個起始站。"]);
        var explicitStationCenters = primaryStops.Select(stop =>
        {
            var node = graph.Nodes.GetValueOrDefault($"NODE:{stop.Id}");
            return (stop.Id, Chainage: node?.SchematicPosition);
        }).ToArray();
        if (explicitStationCenters.All(item => item.Chainage is not null))
        {
            // An author may supply an explicit station-center display projection
            // (for example, source-backed chainage).  This remains presentation
            // data only: physical cursor, edge length and port validation continue
            // to use the topology graph.
            var origin = explicitStationCenters[0].Chainage!.Value;
            foreach (var station in explicitStationCenters)
                stations[station.Id] = station.Chainage!.Value - origin;
        }
        else
        {
            var origin = primaryStops[0].Raw;
            foreach (var stop in primaryStops) stations[stop.Id] = stop.Raw - origin;
        }

        var nodes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var routeMaps = new Dictionary<string, RouteDisplayMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in new[] { primary }.Concat(document.ServiceRoutes.Where(r => r.ServiceRouteId != primaryId)))
        {
            var projection = new RouteProjection(graph, route);
            var anchors = Centers(route, projection).Where(s => stations.ContainsKey(s.Id))
                .Select(s => (s.Raw, Value: stations[s.Id])).OrderBy(s => s.Raw).ToArray();
            if (anchors.Length == 0) continue;
            double Map(double raw)
            {
                var sign = anchors.Length > 1 && anchors[^1].Value < anchors[0].Value ? -1 : 1;
                if (raw <= anchors[0].Raw) return anchors[0].Value + sign * (raw - anchors[0].Raw);
                if (raw >= anchors[^1].Raw) return anchors[^1].Value + sign * (raw - anchors[^1].Raw);
                for (var i = 1; i < anchors.Length; i++)
                    if (raw <= anchors[i].Raw)
                        return anchors[i - 1].Value + (anchors[i].Value - anchors[i - 1].Value)
                            * (raw - anchors[i - 1].Raw) / (anchors[i].Raw - anchors[i - 1].Raw);
                return anchors[^1].Value;
            }

            var routeEdgeMaps = new Dictionary<string, Func<double, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in projection.Segments)
            {
                var edge = graph.Edges[segment.Traversal.TrackEdgeId];
                var from = Map(segment.StartChainageMeters); var to = Map(segment.EndChainageMeters);
                if (segment.Traversal.Direction == TraversalDirection.Reverse) (from, to) = (to, from);
                var edgeMap = (double offset) => Map(segment.StartChainageMeters
                    + (segment.Traversal.Direction == TraversalDirection.Forward ? offset : edge.LengthMeters - offset));
                routeEdgeMaps.TryAdd(edge.TrackEdgeId, edgeMap);
                edges.TryAdd(edge.TrackEdgeId, (from, to, edge.LengthMeters));
                edgeMaps.TryAdd(edge.TrackEdgeId, edgeMap);
                nodes.TryAdd(edge.FromNodeId, from); nodes.TryAdd(edge.ToNodeId, to);
            }

            routeMaps[route.ServiceRouteId] = new(routeEdgeMaps);
        }

        // Passing tracks are physical facility traversals rather than ServiceRoute
        // traversals.  Project them through the corresponding route's arrival and
        // departure edges, then anchor the express platform center to the station
        // center.  Falling back to shared-node interpolation here can mix the first
        // direction's node value with the opposite direction's edge map.
        foreach (var facility in document.Topology.PassingFacilities)
        {
            var expressPlatform = graph.Platforms.GetValueOrDefault(facility.ExpressPlatformId);
            if (expressPlatform is null
                || edges.ContainsKey(expressPlatform.TrackEdgeId)
                || edgeMaps.ContainsKey(expressPlatform.TrackEdgeId)
                || !facility.Traversals.Any(item => item.TrackEdgeId.Equals(expressPlatform.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
                || !stations.TryGetValue(facility.StationId, out var stationCenter))
            {
                continue;
            }

            var operationRouteIds = document.Topology.PassingOperations
                .Where(operation => operation.FacilityId.Equals(facility.FacilityId, StringComparison.OrdinalIgnoreCase))
                .Select(operation => operation.ServiceRouteId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var routeId in operationRouteIds)
            {
                if (!routeMaps.TryGetValue(routeId, out var routeMap)
                    || !routeMap.EdgeMaps.TryGetValue(facility.ArrivalTrackEdgeId, out var arrivalMap)
                    || !routeMap.EdgeMaps.TryGetValue(facility.DepartureTrackEdgeId, out var departureMap))
                {
                    continue;
                }

                var arrivalEdge = graph.Edges[facility.ArrivalTrackEdgeId];
                var departureEdge = graph.Edges[facility.DepartureTrackEdgeId];
                var arrivalOffset = Math.Clamp(facility.ArrivalOffsetMeters, 0, arrivalEdge.LengthMeters);
                var departureOffset = Math.Clamp(facility.DepartureStartOffsetMeters, 0, departureEdge.LengthMeters);
                var routeStart = arrivalMap(arrivalOffset);
                var routeEnd = departureMap(departureOffset);
                var traversal = facility.Traversals.First(item =>
                    item.TrackEdgeId.Equals(expressPlatform.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
                var expressEdge = graph.Edges[expressPlatform.TrackEdgeId];
                var centerOffset = Math.Clamp(
                    (expressPlatform.PlatformStartOffsetMeters + expressPlatform.PlatformEndOffsetMeters) / 2,
                    0,
                    expressEdge.LengthMeters);
                var map = CreateAnchoredFacilityMap(
                    expressEdge.LengthMeters,
                    traversal.Direction,
                    centerOffset,
                    routeStart,
                    stationCenter,
                    routeEnd);
                var from = map(0);
                var to = map(expressEdge.LengthMeters);
                if (!double.IsFinite(from) || !double.IsFinite(to))
                {
                    continue;
                }

                edges[expressEdge.TrackEdgeId] = (from, to, expressEdge.LengthMeters);
                edgeMaps[expressEdge.TrackEdgeId] = map;
                break;
            }
        }

        foreach (var operation in document.Topology.TurnbackOperations)
        {
            var facility = graph.TurnbackFacilities[operation.FacilityId];
            if (!edges.TryGetValue(facility.ArrivalTrackEdgeId, out var arrival)) continue;
            var route = document.ServiceRoutes.Single(r => r.ServiceRouteId == operation.ArrivalServiceRouteId);
            var traversal = route.Traversals.Last(t => t.TrackEdgeId == facility.ArrivalTrackEdgeId);
            var sign = Math.Sign(arrival.To - arrival.From) * (traversal.Direction == TraversalDirection.Forward ? 1 : -1);
            if (sign == 0) continue;
            DirectedTrackTraversal? previous = null;
            foreach (var t in facility.Traversals)
            {
                if (previous is { } prior && prior.TrackEdgeId == t.TrackEdgeId && prior.Direction != t.Direction) sign = -sign;
                previous = t;
                var edge = graph.Edges[t.TrackEdgeId];
                if (edges.ContainsKey(edge.TrackEdgeId)) continue;
                var start = InfrastructureValidator.GetStartNode(edge, t.Direction);
                var end = InfrastructureValidator.GetEndNode(edge, t.Direction);
                if (!nodes.TryGetValue(start, out var a)) continue;
                var b = nodes.GetValueOrDefault(end, a + sign * edge.LengthMeters);
                var forward = t.Direction == TraversalDirection.Forward;
                edges[edge.TrackEdgeId] = forward ? (a, b, edge.LengthMeters) : (b, a, edge.LengthMeters);
                nodes.TryAdd(end, b);
            }
        }
        // Remaining passing／crossover edges without a facility-specific map use
        // their already projected node endpoints.  Facility edges with a station
        // center anchor were handled above and must not be replaced here.
        foreach (var edge in graph.Edges.Values)
            if (!edges.ContainsKey(edge.TrackEdgeId) && nodes.TryGetValue(edge.FromNodeId, out var from)
                && nodes.TryGetValue(edge.ToNodeId, out var to))
                edges[edge.TrackEdgeId] = (from, to, edge.LengthMeters);

        (string Id, double Raw)[] Centers(ServiceRouteDefinition route, RouteProjection projection) =>
            ResolvedStopResolver.Resolve(graph, route).Select(s => {
                var platform = graph.Platforms[s.PlatformId];
                var center = (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2;
                return (s.StationId, projection.ToChainage(s.TraversalIndex, new(platform.TrackEdgeId, center)));
            }).ToArray();
    }

    private static Func<double, double> CreateAnchoredFacilityMap(
        double edgeLength,
        TraversalDirection traversalDirection,
        double centerOffset,
        double routeStart,
        double stationCenter,
        double routeEnd)
    {
        var routeCenterOffset = traversalDirection == TraversalDirection.Forward
            ? centerOffset
            : edgeLength - centerOffset;

        double AlongTraversal(double distance)
        {
            if (routeCenterOffset <= TrackPosition.DefaultToleranceMeters
                || routeCenterOffset >= edgeLength - TrackPosition.DefaultToleranceMeters)
            {
                return Linear(routeStart, routeEnd, edgeLength <= TrackPosition.DefaultToleranceMeters
                    ? 0
                    : distance / edgeLength);
            }

            return distance <= routeCenterOffset
                ? Linear(routeStart, stationCenter, distance / routeCenterOffset)
                : Linear(stationCenter, routeEnd, (distance - routeCenterOffset) / (edgeLength - routeCenterOffset));
        }

        return offset =>
        {
            var physicalOffset = Math.Clamp(offset, 0, edgeLength);
            var distance = traversalDirection == TraversalDirection.Forward
                ? physicalOffset
                : edgeLength - physicalOffset;
            return AlongTraversal(distance);
        };

        static double Linear(double start, double end, double ratio) => start + (end - start) * Math.Clamp(ratio, 0, 1);
    }

    public double? ToChainage(TrackPosition position) => edgeMaps.TryGetValue(position.TrackEdgeId, out var map)
        ? map(position.OffsetMeters) : edges.TryGetValue(position.TrackEdgeId, out var edge)
            ? edge.From + (edge.To - edge.From) * position.OffsetMeters / edge.Length : null;

    public double? ToCenterChainage(TopologyMovementFootprint footprint)
    {
        var front = ToChainage(footprint.Front.Position); var rear = ToChainage(footprint.Rear.Position);
        return front.HasValue && rear.HasValue ? (front.Value + rear.Value) / 2 : null;
    }
}
