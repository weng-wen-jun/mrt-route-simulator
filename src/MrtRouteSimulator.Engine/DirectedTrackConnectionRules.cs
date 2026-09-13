namespace MrtRouteSimulator.Engine;

/// <summary>
/// 建立 topology 的有向轉向白名單。
/// 共用節點只表示幾何端點相同，不表示所有進入股道都能轉往所有離開股道。
/// 規則只採用營運進路、設施 traversal 及設施作業邊界中明確宣告的轉向；
/// 不讀取 schematic lane 或其他呈現資料推導物理方向。
/// </summary>
public static class DirectedTrackConnectionRules
{
    public static IReadOnlyList<DirectedTrackConnectionDefinition> Build(
        TopologyProjectDocument document,
        IEnumerable<DirectedTrackConnectionDefinition>? additionalConnections = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Build(document.Topology, document.ServiceRoutes, additionalConnections);
    }

    public static IReadOnlyList<DirectedTrackConnectionDefinition> Build(
        TopologyInfrastructureDefinition topology,
        IEnumerable<ServiceRouteDefinition> serviceRoutes,
        IEnumerable<DirectedTrackConnectionDefinition>? additionalConnections = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(serviceRoutes);

        var edges = topology.Edges.ToDictionary(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        var routes = serviceRoutes.ToArray();
        var result = new List<DirectedTrackConnectionDefinition>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Existing explicit connections remain part of the document contract and
        // are validated separately. This method supplies only missing declarations.
        foreach (var connection in topology.DirectedConnections)
            Add(connection, validate: false);

        foreach (var route in routes)
            AddAdjacent(route.Traversals);
        foreach (var facility in topology.TurnbackFacilities)
            AddAdjacent(facility.Traversals);
        foreach (var facility in topology.PassingFacilities)
            AddAdjacent(facility.Traversals);

        var routesById = routes
            .Where(route => !string.IsNullOrWhiteSpace(route.ServiceRouteId))
            .GroupBy(route => route.ServiceRouteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var turnbacksById = topology.TurnbackFacilities
            .Where(facility => !string.IsNullOrWhiteSpace(facility.FacilityId))
            .GroupBy(facility => facility.FacilityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var operation in topology.TurnbackOperations)
        {
            if (!turnbacksById.TryGetValue(operation.FacilityId, out var facility)
                || facility.Traversals.Count == 0
                || !routesById.TryGetValue(operation.ArrivalServiceRouteId, out var arrivalRoute)
                || !routesById.TryGetValue(operation.DepartureServiceRouteId, out var departureRoute))
                continue;

            AddArrivalBoundary(arrivalRoute, facility.ArrivalTrackEdgeId, facility.ArrivalStopOffsetMeters, facility.Traversals[0]);
            AddDepartureBoundary(facility.Traversals[^1], departureRoute, facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters);
        }

        var passingsById = topology.PassingFacilities
            .Where(facility => !string.IsNullOrWhiteSpace(facility.FacilityId))
            .GroupBy(facility => facility.FacilityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var operation in topology.PassingOperations)
        {
            if (!passingsById.TryGetValue(operation.FacilityId, out var facility)
                || facility.Traversals.Count == 0
                || !routesById.TryGetValue(operation.ServiceRouteId, out var route))
                continue;

            AddArrivalBoundary(route, facility.ArrivalTrackEdgeId, facility.ArrivalOffsetMeters, facility.Traversals[0]);
            AddDepartureBoundary(facility.Traversals[^1], route, facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters);
        }

        foreach (var connection in additionalConnections ?? [])
            Add(connection, validate: true);

        return result;

        void AddAdjacent(IReadOnlyList<DirectedTrackTraversal> traversals)
        {
            for (var index = 1; index < traversals.Count; index++)
                Add(new DirectedTrackConnectionDefinition(
                    traversals[index - 1].TrackEdgeId, traversals[index - 1].Direction,
                    traversals[index].TrackEdgeId, traversals[index].Direction), validate: true);
        }

        void AddArrivalBoundary(
            ServiceRouteDefinition route,
            string arrivalEdgeId,
            double arrivalOffset,
            DirectedTrackTraversal facilityEntry)
        {
            if (!edges.TryGetValue(arrivalEdgeId, out var edge)) return;
            foreach (var traversal in route.Traversals.Where(item => Same(item.TrackEdgeId, arrivalEdgeId)))
            {
                var boundary = traversal.Direction == TraversalDirection.Forward ? edge.LengthMeters : 0;
                if (Math.Abs(boundary - arrivalOffset) <= TrackPosition.DefaultToleranceMeters)
                    Add(new DirectedTrackConnectionDefinition(
                        traversal.TrackEdgeId, traversal.Direction,
                        facilityEntry.TrackEdgeId, facilityEntry.Direction), validate: true);
            }
        }

        void AddDepartureBoundary(
            DirectedTrackTraversal facilityExit,
            ServiceRouteDefinition route,
            string departureEdgeId,
            double departureOffset)
        {
            if (!edges.TryGetValue(departureEdgeId, out var edge)) return;
            foreach (var traversal in route.Traversals.Where(item => Same(item.TrackEdgeId, departureEdgeId)))
            {
                var boundary = traversal.Direction == TraversalDirection.Forward ? 0 : edge.LengthMeters;
                if (Math.Abs(boundary - departureOffset) <= TrackPosition.DefaultToleranceMeters)
                    Add(new DirectedTrackConnectionDefinition(
                        facilityExit.TrackEdgeId, facilityExit.Direction,
                        traversal.TrackEdgeId, traversal.Direction), validate: true);
            }
        }

        void Add(DirectedTrackConnectionDefinition connection, bool validate)
        {
            if (validate && !IsValidConnection(connection, edges))
            {
                throw new SimulationValidationException([
                    $"有向轉向的進入軌道 edge「{connection.FromTrackEdgeId}」與離開軌道 edge「{connection.ToTrackEdgeId}」未在同一軌道節點以允許方向接續。"
                ]);
            }

            if (keys.Add(Key(connection))) result.Add(connection);
        }
    }

    /// <summary>
    /// 取得由 route、facility traversal 與 facility operation 邊界明確宣告的轉向，
    /// 不包含文件中既有的顯式 connection。
    /// </summary>
    public static IReadOnlySet<string> GetDeclaredConnectionKeys(
        TopologyInfrastructureDefinition topology,
        IEnumerable<ServiceRouteDefinition> serviceRoutes)
    {
        var withoutExisting = topology with { DirectedConnections = [] };
        return Build(withoutExisting, serviceRoutes).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static string Key(DirectedTrackConnectionDefinition connection) =>
        $"{connection.FromTrackEdgeId}|{connection.FromDirection}|{connection.ToTrackEdgeId}|{connection.ToDirection}";

    private static bool IsValidConnection(
        DirectedTrackConnectionDefinition connection,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges)
    {
        if (!edges.TryGetValue(connection.FromTrackEdgeId, out var fromEdge)
            || !edges.TryGetValue(connection.ToTrackEdgeId, out var toEdge)
            || !Enum.IsDefined(connection.FromDirection)
            || !Enum.IsDefined(connection.ToDirection)
            || !InfrastructureValidator.AllowsTraversal(fromEdge, connection.FromDirection)
            || !InfrastructureValidator.AllowsTraversal(toEdge, connection.ToDirection)
            || !TrackPortRules.Allows(fromEdge, connection.FromDirection, toEdge, connection.ToDirection))
            return false;

        return InfrastructureValidator.GetEndNode(fromEdge, connection.FromDirection)
            .Equals(InfrastructureValidator.GetStartNode(toEdge, connection.ToDirection), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
