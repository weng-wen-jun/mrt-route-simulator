namespace MrtRouteSimulator.Engine;

/// <summary>Track-first infrastructure 與有序 ServiceRoute 的跨參照驗證結果。</summary>
public sealed record InfrastructureValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Phase A topology 的集中驗證入口。</summary>
public static class InfrastructureValidator
{
    public static InfrastructureValidationResult Validate(
        TopologyInfrastructureDefinition definition,
        IEnumerable<ServiceRouteDefinition>? serviceRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();
        var nodes = Index(definition.Nodes, item => item.NodeId, "軌道節點", errors);
        var edges = Index(definition.Edges, item => item.TrackEdgeId, "軌道 edge", errors);
        var stations = Index(definition.Stations, item => item.StationId, "車站", errors);
        var platforms = Index(definition.Platforms, item => item.PlatformId, "月台", errors);
        var resources = Index(definition.Resources, item => item.ResourceId, "衝突資源", errors);
        _ = Index(definition.SpeedLimits, item => item.SpeedLimitId, "軌道速限", errors);
        _ = Index(definition.Gradients, item => item.GradientId, "坡度區間", errors);
        _ = Index(definition.Curves, item => item.CurveId, "曲線區間", errors);
        var facilities = Index(definition.TurnbackFacilities, item => item.FacilityId, "折返設施", errors);
        var operations = Index(definition.TurnbackOperations, item => item.OperationId, "折返作業", errors);
        var passingFacilities = Index(definition.PassingFacilities, item => item.FacilityId, "越行設施", errors);
        var passingOperations = Index(definition.PassingOperations, item => item.OperationId, "越行作業", errors);
        _ = Index(definition.StationOperations, item => item.StationOperationId, "車站作業", errors);
        var routeList = (serviceRoutes ?? []).ToArray();

        foreach (var node in definition.Nodes)
        {
            RequireText(node.Name, $"軌道節點「{node.NodeId}」名稱", errors);
            if (!Enum.IsDefined(node.Kind)) errors.Add($"軌道節點「{node.NodeId}」種類無效。");
            if (node.SchematicPosition is { } x && (!double.IsFinite(x) || Math.Abs(x) > 1000000)
                || node.SchematicLane is { } y && (!double.IsFinite(y) || Math.Abs(y) > 8))
                errors.Add($"軌道節點「{node.NodeId}」示意座標無效。");
        }

        foreach (var edge in definition.Edges)
        {
            if (edge.SchematicLane is { } lane && (!double.IsFinite(lane) || Math.Abs(lane) > 8))
                errors.Add($"軌道「{edge.TrackEdgeId}」示意股道必須是 -8 至 8 的有限數值。");
            if (!nodes.ContainsKey(edge.FromNodeId)) errors.Add($"軌道 edge「{edge.TrackEdgeId}」引用不存在的起點節點「{edge.FromNodeId}」。");
            if (!nodes.ContainsKey(edge.ToNodeId)) errors.Add($"軌道 edge「{edge.TrackEdgeId}」引用不存在的終點節點「{edge.ToNodeId}」。");
            RequirePositive(edge.LengthMeters, $"軌道 edge「{edge.TrackEdgeId}」長度", errors);
            RequirePositive(edge.DefaultSpeedLimitMetersPerSecond, $"軌道 edge「{edge.TrackEdgeId}」預設速限", errors);
            if (!Enum.IsDefined(edge.Directionality)) errors.Add($"軌道 edge「{edge.TrackEdgeId}」方向設定無效。");
            if (!Enum.IsDefined(edge.Kind)) errors.Add($"軌道 edge「{edge.TrackEdgeId}」類型無效。");
            foreach (var resourceId in edge.ConflictResourceIds)
            {
                if (!resources.ContainsKey(resourceId)) errors.Add($"軌道 edge「{edge.TrackEdgeId}」引用不存在的衝突資源「{resourceId}」。");
            }
        }

        ValidateDirectedConnections(definition.DirectedConnections, edges, errors);

        foreach (var station in definition.Stations)
        {
            RequireText(station.Name, $"車站「{station.StationId}」名稱", errors);
            RequireNonNegative(station.DefaultDwellTimeSeconds, $"車站「{station.StationId}」預設停站時間", errors);
            EnsureDistinctReferences(station.PlatformIds, $"車站「{station.StationId}」月台", errors);
            foreach (var platformId in station.PlatformIds ?? [])
            {
                if (!platforms.TryGetValue(platformId, out var platform))
                {
                    errors.Add($"車站「{station.StationId}」引用不存在的月台「{platformId}」。");
                }
                else if (!platform.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"車站「{station.StationId}」引用的月台「{platformId}」所屬車站不相符。");
                }
            }
        }

        foreach (var platform in definition.Platforms)
        {
            if (!Enum.IsDefined(platform.DisplaySide)) errors.Add($"月台「{platform.PlatformId}」呈現側別無效。");
            if (!Enum.IsDefined(platform.StopPositionReference)) errors.Add($"月台「{platform.PlatformId}」停車定位基準無效。");
            if (!stations.ContainsKey(platform.StationId)) errors.Add($"月台「{platform.PlatformId}」引用不存在的車站「{platform.StationId}」。");
            if (!edges.TryGetValue(platform.TrackEdgeId, out var edge))
            {
                errors.Add($"月台「{platform.PlatformId}」引用不存在的軌道 edge「{platform.TrackEdgeId}」。");
            }
            else
            {
                ValidatePlatformOffsets(platform, edge, errors);
            }

            RequireText(platform.Name, $"月台「{platform.PlatformId}」名稱", errors);
            RequirePositive(platform.EffectiveLengthMeters, $"月台「{platform.PlatformId}」有效長度", errors);
            if (platform.AllowedDirection is not TrackDirection.Both and not TrackDirection.Outbound and not TrackDirection.Inbound)
            {
                errors.Add($"月台「{platform.PlatformId}」允許方向無效。");
            }
        }

        foreach (var resource in definition.Resources)
        {
            RequireText(resource.Name, $"衝突資源「{resource.ResourceId}」名稱", errors);
            if (!Enum.IsDefined(resource.Kind)) errors.Add($"衝突資源「{resource.ResourceId}」類型無效。");
        }

        foreach (var limit in definition.SpeedLimits)
        {
            if (!edges.TryGetValue(limit.TrackEdgeId, out var edge))
            {
                errors.Add($"軌道速限「{limit.SpeedLimitId}」引用不存在的軌道 edge「{limit.TrackEdgeId}」。");
                continue;
            }

            if (!double.IsFinite(limit.StartOffsetMeters) || !double.IsFinite(limit.EndOffsetMeters)
                || limit.StartOffsetMeters < 0 || limit.EndOffsetMeters < limit.StartOffsetMeters
                || limit.EndOffsetMeters > edge.LengthMeters)
            {
                errors.Add($"軌道速限「{limit.SpeedLimitId}」offset 必須符合 0 <= start <= end <= edge 長度。");
            }
            RequirePositive(limit.LimitMetersPerSecond, $"軌道速限「{limit.SpeedLimitId}」速限", errors);
            if (!Enum.IsDefined(limit.Direction) || !AllowsTraversal(edge, limit.Direction))
            {
                errors.Add($"軌道速限「{limit.SpeedLimitId}」方向不符合 edge 設定。");
            }
        }

        foreach (var gradient in definition.Gradients)
        {
            ValidateEdgeInterval(gradient.GradientId, "坡度區間", gradient.TrackEdgeId,
                gradient.StartOffsetMeters, gradient.EndOffsetMeters, edges, errors);
            if (!double.IsFinite(gradient.GradePermille))
                errors.Add($"坡度區間「{gradient.GradientId}」坡度必須是有限數值。");
        }

        foreach (var curve in definition.Curves)
        {
            ValidateEdgeInterval(curve.CurveId, "曲線區間", curve.TrackEdgeId,
                curve.StartOffsetMeters, curve.EndOffsetMeters, edges, errors);
            RequirePositive(curve.RadiusMeters, $"曲線區間「{curve.CurveId}」半徑", errors);
        }

        foreach (var facility in definition.TurnbackFacilities)
        {
            RequireText(facility.Name, $"折返設施「{facility.FacilityId}」名稱", errors);
            if (!Enum.IsDefined(facility.Kind)) errors.Add($"折返設施「{facility.FacilityId}」類型無效。");
            ValidateEdgeOffset(facility.FacilityId, "折返設施到達", facility.ArrivalTrackEdgeId,
                facility.ArrivalStopOffsetMeters, edges, errors);
            ValidateEdgeOffset(facility.FacilityId, "折返設施出發", facility.DepartureTrackEdgeId,
                facility.DepartureStartOffsetMeters, edges, errors);
            EnsureDistinctReferences(facility.FacilityTrackEdgeIds, $"折返設施「{facility.FacilityId}」edge", errors);
            foreach (var edgeId in facility.FacilityTrackEdgeIds)
                if (!edges.ContainsKey(edgeId)) errors.Add($"折返設施「{facility.FacilityId}」引用不存在的 edge「{edgeId}」。");
            ValidateFacilityTraversals(facility, edges, errors);
            ValidateFacilityTransitions(facility.FacilityId, "折返設施", facility.Traversals,
                edges, definition.DirectedConnections, errors);
            if (facility.TurnbackStopAfterTraversalIndex is { } turnbackIndex
                && (facility.Traversals.Count == 0 || turnbackIndex < 0 || turnbackIndex >= facility.Traversals.Count))
            {
                errors.Add($"折返設施「{facility.FacilityId}」的折返停等 traversal 索引必須指向既有 traversal。");
            }
            if (facility.TurnbackStopPosition is { } stopPosition)
            {
                ValidateEdgeOffset(facility.FacilityId, "折返設施停等", stopPosition.TrackEdgeId,
                    stopPosition.OffsetMeters, edges, errors);
                if (!facility.Traversals.Any(traversal => traversal.TrackEdgeId.Equals(
                        stopPosition.TrackEdgeId, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"折返設施「{facility.FacilityId}」的實體停等位置必須位於 facility traversal。 ");
                }
                ValidatePhysicalTurnbackReturn(facility, stopPosition, edges, errors);
            }
            foreach (var resourceId in facility.ConflictResourceIds)
                if (!resources.ContainsKey(resourceId)) errors.Add($"折返設施「{facility.FacilityId}」引用不存在的衝突資源「{resourceId}」。");
        }

        foreach (var operation in definition.TurnbackOperations)
        {
            if (!facilities.ContainsKey(operation.FacilityId))
                errors.Add($"折返作業「{operation.OperationId}」引用不存在的折返設施「{operation.FacilityId}」。");
            RequireText(operation.ArrivalServiceRouteId, $"折返作業「{operation.OperationId}」到達營運路線", errors);
            RequireText(operation.DepartureServiceRouteId, $"折返作業「{operation.OperationId}」出發營運路線", errors);
            RequireNonNegative(operation.MinimumDwellTimeSeconds, $"折返作業「{operation.OperationId}」最短停等時間", errors);
        }

        foreach (var facility in definition.PassingFacilities)
        {
            RequireText(facility.Name, $"越行設施「{facility.FacilityId}」名稱", errors);
            if (!stations.ContainsKey(facility.StationId))
                errors.Add($"越行設施「{facility.FacilityId}」引用不存在的車站「{facility.StationId}」。");
            ValidateEdgeOffset(facility.FacilityId, "越行設施進入", facility.ArrivalTrackEdgeId,
                facility.ArrivalOffsetMeters, edges, errors);
            ValidateEdgeOffset(facility.FacilityId, "越行設施離開", facility.DepartureTrackEdgeId,
                facility.DepartureStartOffsetMeters, edges, errors);
            ValidatePassingFacilityTraversals(facility, edges, errors);
            ValidateFacilityTransitions(facility.FacilityId, "越行設施", facility.Traversals,
                edges, definition.DirectedConnections, errors);
            foreach (var platformId in new[] { facility.LocalPlatformId, facility.ExpressPlatformId })
            {
                if (!platforms.TryGetValue(platformId, out var platform))
                {
                    errors.Add($"越行設施「{facility.FacilityId}」引用不存在的月台「{platformId}」。");
                }
                else if (!platform.StationId.Equals(facility.StationId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"越行設施「{facility.FacilityId}」的月台「{platformId}」不屬於車站「{facility.StationId}」。");
                }
            }
            if (facility.LocalPlatformId.Equals(facility.ExpressPlatformId, StringComparison.OrdinalIgnoreCase))
                errors.Add($"越行設施「{facility.FacilityId}」的普通與快速月台必須不同。");
            foreach (var resourceId in facility.ConflictResourceIds)
                if (!resources.ContainsKey(resourceId)) errors.Add($"越行設施「{facility.FacilityId}」引用不存在的衝突資源「{resourceId}」。");
        }

        foreach (var operation in definition.PassingOperations)
        {
            if (!passingFacilities.ContainsKey(operation.FacilityId))
                errors.Add($"越行作業「{operation.OperationId}」引用不存在的越行設施「{operation.FacilityId}」。");
            RequireText(operation.ServiceRouteId, $"越行作業「{operation.OperationId}」營運路線", errors);
        }

        foreach (var stationOperation in definition.StationOperations)
        {
            if (!stations.ContainsKey(stationOperation.StationId))
                errors.Add($"車站作業「{stationOperation.StationOperationId}」引用不存在的車站「{stationOperation.StationId}」。");
            ValidateStationOperationPlatforms(stationOperation, platforms, errors);
            EnsureDistinctReferences(stationOperation.TurnbackOperationIds, $"車站作業「{stationOperation.StationOperationId}」折返作業", errors);
            foreach (var operationId in stationOperation.TurnbackOperationIds)
                if (!operations.ContainsKey(operationId)) errors.Add($"車站作業「{stationOperation.StationOperationId}」引用不存在的折返作業「{operationId}」。");
            EnsureDistinctReferences(stationOperation.PassingOperationIds, $"車站作業「{stationOperation.StationOperationId}」越行作業", errors);
            foreach (var operationId in stationOperation.PassingOperationIds)
                if (!passingOperations.ContainsKey(operationId)) errors.Add($"車站作業「{stationOperation.StationOperationId}」引用不存在的越行作業「{operationId}」。");
            RequireNonNegative(stationOperation.DefaultDwellTimeSeconds, $"車站作業「{stationOperation.StationOperationId}」預設停站時間", errors);
        }

        ValidateServiceRoutes(routeList, edges, stations, platforms, definition.DirectedConnections, errors);
        TrackPortRules.Validate(definition, routeList, errors);
        StationConstructionRules.Validate(definition, routeList, errors);
        ValidateFacilityRouteBoundaries(definition, routeList, edges, errors);
        return new InfrastructureValidationResult(errors);
    }

    public static InfrastructureValidationResult Validate(
        InfrastructureGraphV4 infrastructure,
        IEnumerable<ServiceRouteDefinition>? serviceRoutes = null)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        return Validate(new TopologyInfrastructureDefinition
        {
            Nodes = infrastructure.Nodes.Values.ToArray(),
            Edges = infrastructure.Edges.Values.ToArray(),
            Stations = infrastructure.Stations.Values.ToArray(),
            Platforms = infrastructure.Platforms.Values.ToArray(),
            Resources = infrastructure.Resources.Values.ToArray(),
            SpeedLimits = infrastructure.SpeedLimits.Values.ToArray(),
            Gradients = infrastructure.Gradients.Values.ToArray(),
            Curves = infrastructure.Curves.Values.ToArray(),
            DirectedConnections = infrastructure.DirectedConnections,
            TurnbackFacilities = infrastructure.TurnbackFacilities.Values.ToArray(),
            TurnbackOperations = infrastructure.TurnbackOperations.Values.ToArray(),
            PassingFacilities = infrastructure.PassingFacilities.Values.ToArray(),
            PassingOperations = infrastructure.PassingOperations.Values.ToArray(),
            StationOperations = infrastructure.StationOperations.Values.ToArray()
        }, serviceRoutes);
    }

    public static void ValidateAndThrow(
        TopologyInfrastructureDefinition definition,
        IEnumerable<ServiceRouteDefinition>? serviceRoutes = null) =>
        ThrowIfInvalid(Validate(definition, serviceRoutes));

    public static void ValidateAndThrow(
        InfrastructureGraphV4 infrastructure,
        IEnumerable<ServiceRouteDefinition>? serviceRoutes = null) =>
        ThrowIfInvalid(Validate(infrastructure, serviceRoutes));

    private static void ValidateServiceRoutes(
        IEnumerable<ServiceRouteDefinition> routes,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        IReadOnlyDictionary<string, StationDefinitionV4> stations,
        IReadOnlyDictionary<string, PlatformDefinitionV4> platforms,
        IReadOnlyList<DirectedTrackConnectionDefinition> directedConnections,
        ICollection<string> errors)
    {
        var routeList = routes.ToArray();
        _ = Index(routeList, route => route.ServiceRouteId, "營運路線", errors);
        foreach (var route in routeList)
        {
            RequireText(route.Name, $"營運路線「{route.ServiceRouteId}」名稱", errors);
            if (route.Traversals.Count == 0)
            {
                errors.Add($"營運路線「{route.ServiceRouteId}」至少需要一個有序軌道 traversal。");
                continue;
            }

            TrackEdgeDefinition? previousEdge = null;
            TraversalDirection previousDirection = default;
            for (var index = 0; index < route.Traversals.Count; index++)
            {
                var traversal = route.Traversals[index];
                if (!edges.TryGetValue(traversal.TrackEdgeId, out var edge))
                {
                    errors.Add($"營運路線「{route.ServiceRouteId}」第 {index + 1} 個 traversal 引用不存在的軌道 edge「{traversal.TrackEdgeId}」。");
                    continue;
                }

                if (!Enum.IsDefined(traversal.Direction))
                {
                    errors.Add($"營運路線「{route.ServiceRouteId}」第 {index + 1} 個 traversal 方向無效。");
                }
                else if (!AllowsTraversal(edge, traversal.Direction))
                {
                    errors.Add($"營運路線「{route.ServiceRouteId}」第 {index + 1} 個 traversal「{edge.TrackEdgeId}」不符合 edge 方向設定。");
                }

                if (previousEdge is not null)
                {
                    var previousEnd = GetEndNode(previousEdge, previousDirection);
                    var currentStart = GetStartNode(edge, traversal.Direction);
                    if (!previousEnd.Equals(currentStart, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"營運路線「{route.ServiceRouteId}」第 {index} 與第 {index + 1} 個軌道路徑不連續：{previousEdge.TrackEdgeId} 結束於 {previousEnd}，但 {edge.TrackEdgeId} 起始於 {currentStart}。");
                    }
                    else if (!AllowsTransition(edges, directedConnections,
                                 new DirectedTrackTraversal(previousEdge.TrackEdgeId, previousDirection),
                                 traversal))
                    {
                        errors.Add($"營運路線「{route.ServiceRouteId}」第 {index} 與第 {index + 1} 個 traversal 違反道岔有向轉向限制。");
                    }
                }

                previousEdge = edge;
                previousDirection = traversal.Direction;
            }

            var routeEdgeIds = route.Traversals.Select(item => item.TrackEdgeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stop in route.Stops)
            {
                if (!stations.ContainsKey(stop.StationId))
                {
                    errors.Add($"營運路線「{route.ServiceRouteId}」停靠引用不存在的車站「{stop.StationId}」。");
                }

                var candidatePlatformIds = stop.CandidatePlatformIds ?? Array.Empty<string>();
                if (candidatePlatformIds.Count == 0)
                {
                    errors.Add($"營運路線「{route.ServiceRouteId}」車站「{stop.StationId}」至少需要一個候選月台。");
                }

                EnsureDistinctReferences(candidatePlatformIds, $"營運路線「{route.ServiceRouteId}」車站「{stop.StationId}」候選月台", errors);
                foreach (var platformId in candidatePlatformIds)
                {
                    if (!platforms.TryGetValue(platformId, out var platform))
                    {
                        errors.Add($"營運路線「{route.ServiceRouteId}」引用不存在的候選月台「{platformId}」。");
                    }
                    else
                    {
                        if (!platform.StationId.Equals(stop.StationId, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"營運路線「{route.ServiceRouteId}」候選月台「{platformId}」不屬於車站「{stop.StationId}」。");
                        }

                        if (!routeEdgeIds.Contains(platform.TrackEdgeId))
                        {
                            errors.Add($"營運路線「{route.ServiceRouteId}」候選月台「{platformId}」不位於該營運路線。");
                        }
                    }
                }
            }
        }
    }

    internal static bool AllowsTraversal(TrackEdgeDefinition edge, TraversalDirection direction) =>
        edge.Directionality == TrackDirectionality.Bidirectional
        || (edge.Directionality == TrackDirectionality.ForwardOnly && direction == TraversalDirection.Forward)
        || (edge.Directionality == TrackDirectionality.ReverseOnly && direction == TraversalDirection.Reverse);

    internal static bool AllowsTransition(
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        IReadOnlyList<DirectedTrackConnectionDefinition> directedConnections,
        DirectedTrackTraversal from,
        DirectedTrackTraversal to)
    {
        if (!edges.TryGetValue(from.TrackEdgeId, out var fromEdge)
            || !edges.TryGetValue(to.TrackEdgeId, out var toEdge)
            || !AllowsTraversal(fromEdge, from.Direction)
            || !AllowsTraversal(toEdge, to.Direction)
            || !TrackPortRules.Allows(fromEdge, from.Direction, toEdge, to.Direction))
        {
            return false;
        }

        var nodeId = GetEndNode(fromEdge, from.Direction);
        if (!nodeId.Equals(GetStartNode(toEdge, to.Direction), StringComparison.OrdinalIgnoreCase))
            return false;

        var constrainedAtNode = directedConnections.Any(connection =>
            edges.TryGetValue(connection.FromTrackEdgeId, out var connectionFrom)
            && Enum.IsDefined(connection.FromDirection)
            && GetEndNode(connectionFrom, connection.FromDirection)
                .Equals(nodeId, StringComparison.OrdinalIgnoreCase));
        return !constrainedAtNode || directedConnections.Any(connection =>
            connection.FromTrackEdgeId.Equals(from.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            && connection.FromDirection == from.Direction
            && connection.ToTrackEdgeId.Equals(to.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            && connection.ToDirection == to.Direction);
    }

    internal static string GetStartNode(TrackEdgeDefinition edge, TraversalDirection direction) =>
        direction == TraversalDirection.Forward ? edge.FromNodeId : edge.ToNodeId;

    internal static string GetEndNode(TrackEdgeDefinition edge, TraversalDirection direction) =>
        direction == TraversalDirection.Forward ? edge.ToNodeId : edge.FromNodeId;

    private static void ValidatePlatformOffsets(
        PlatformDefinitionV4 platform,
        TrackEdgeDefinition edge,
        ICollection<string> errors)
    {
        var values = new[]
        {
            platform.PlatformStartOffsetMeters,
            platform.StopPositionOffsetMeters,
            platform.PlatformEndOffsetMeters
        };
        if (values.Any(value => !double.IsFinite(value)))
        {
            errors.Add($"月台「{platform.PlatformId}」offset 必須是有限數值。");
            return;
        }

        if (platform.PlatformStartOffsetMeters < 0
            || platform.StopPositionOffsetMeters < platform.PlatformStartOffsetMeters
            || platform.PlatformEndOffsetMeters < platform.StopPositionOffsetMeters
            || platform.PlatformEndOffsetMeters > edge.LengthMeters)
        {
            errors.Add($"月台「{platform.PlatformId}」offset 必須符合 0 <= start <= stop <= end <= edge 長度。");
        }
    }

    private static void ValidateEdgeInterval(
        string itemId,
        string itemKind,
        string edgeId,
        double startOffsetMeters,
        double endOffsetMeters,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        if (!edges.TryGetValue(edgeId, out var edge))
        {
            errors.Add($"{itemKind}「{itemId}」引用不存在的軌道 edge「{edgeId}」。");
            return;
        }

        if (!double.IsFinite(startOffsetMeters) || !double.IsFinite(endOffsetMeters)
            || startOffsetMeters < 0 || endOffsetMeters <= startOffsetMeters || endOffsetMeters > edge.LengthMeters)
        {
            errors.Add($"{itemKind}「{itemId}」offset 必須符合 0 <= start < end <= edge 長度。");
        }
    }

    private static void ValidateEdgeOffset(
        string itemId,
        string itemKind,
        string edgeId,
        double offsetMeters,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        if (!edges.TryGetValue(edgeId, out var edge))
        {
            errors.Add($"{itemKind}「{itemId}」引用不存在的軌道 edge「{edgeId}」。");
            return;
        }

        if (!double.IsFinite(offsetMeters) || offsetMeters < 0 || offsetMeters > edge.LengthMeters)
            errors.Add($"{itemKind}「{itemId}」offset 必須位於 edge 範圍內。");
    }

    private static void ValidateStationOperationPlatforms(
        StationOperationDefinition stationOperation,
        IReadOnlyDictionary<string, PlatformDefinitionV4> platforms,
        ICollection<string> errors)
    {
        foreach (var platformId in stationOperation.ArrivalPlatformIds.Concat(stationOperation.DeparturePlatformIds))
        {
            if (!platforms.TryGetValue(platformId, out var platform))
            {
                errors.Add($"車站作業「{stationOperation.StationOperationId}」引用不存在的月台「{platformId}」。");
            }
            else if (!platform.StationId.Equals(stationOperation.StationId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"車站作業「{stationOperation.StationOperationId}」的月台「{platformId}」不屬於車站「{stationOperation.StationId}」。");
            }
        }

        EnsureDistinctReferences(stationOperation.ArrivalPlatformIds, $"車站作業「{stationOperation.StationOperationId}」到達月台", errors);
        EnsureDistinctReferences(stationOperation.DeparturePlatformIds, $"車站作業「{stationOperation.StationOperationId}」出發月台", errors);
    }

    private static void ValidateDirectedConnections(
        IEnumerable<DirectedTrackConnectionDefinition> connections,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in connections)
        {
            var key = $"{connection.FromTrackEdgeId}:{connection.FromDirection}>{connection.ToTrackEdgeId}:{connection.ToDirection}";
            if (!seen.Add(key))
                errors.Add($"有向道岔轉向「{key}」重複。");
            if (!edges.TryGetValue(connection.FromTrackEdgeId, out var fromEdge))
            {
                errors.Add($"有向道岔轉向引用不存在的進入 edge「{connection.FromTrackEdgeId}」。");
                continue;
            }
            if (!edges.TryGetValue(connection.ToTrackEdgeId, out var toEdge))
            {
                errors.Add($"有向道岔轉向引用不存在的離開 edge「{connection.ToTrackEdgeId}」。");
                continue;
            }
            if (!Enum.IsDefined(connection.FromDirection) || !AllowsTraversal(fromEdge, connection.FromDirection)
                || !Enum.IsDefined(connection.ToDirection) || !AllowsTraversal(toEdge, connection.ToDirection))
            {
                errors.Add($"有向道岔轉向「{key}」不符合 edge 方向設定。");
                continue;
            }
            if (!GetEndNode(fromEdge, connection.FromDirection)
                    .Equals(GetStartNode(toEdge, connection.ToDirection), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"有向道岔轉向「{key}」兩側未在同一 topology node 接續。");
            }
        }
    }

    private static void ValidateFacilityTransitions(
        string facilityId,
        string facilityKind,
        IReadOnlyList<DirectedTrackTraversal> traversals,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        IReadOnlyList<DirectedTrackConnectionDefinition> directedConnections,
        ICollection<string> errors)
    {
        for (var index = 1; index < traversals.Count; index++)
        {
            if (!AllowsTransition(edges, directedConnections, traversals[index - 1], traversals[index]))
            {
                errors.Add($"{facilityKind}「{facilityId}」第 {index} 與第 {index + 1} 個 traversal 違反道岔有向轉向限制。");
            }
        }
    }

    private static void ValidateFacilityTraversals(
        TurnbackFacilityDefinition facility,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        if (facility.Traversals.Count == 0)
        {
            return;
        }

        TrackEdgeDefinition? previousEdge = null;
        TraversalDirection previousDirection = default;
        for (var index = 0; index < facility.Traversals.Count; index++)
        {
            var traversal = facility.Traversals[index];
            if (!edges.TryGetValue(traversal.TrackEdgeId, out var edge))
            {
                errors.Add($"折返設施「{facility.FacilityId}」第 {index + 1} 個 traversal 引用不存在的 edge「{traversal.TrackEdgeId}」。");
                continue;
            }
            if (!Enum.IsDefined(traversal.Direction) || !AllowsTraversal(edge, traversal.Direction))
            {
                errors.Add($"折返設施「{facility.FacilityId}」第 {index + 1} 個 traversal 方向不符合 edge 設定。");
            }
            if (previousEdge is not null)
            {
                var previousEnd = GetEndNode(previousEdge, previousDirection);
                var currentStart = GetStartNode(edge, traversal.Direction);
                if (!previousEnd.Equals(currentStart, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"折返設施「{facility.FacilityId}」第 {index} 與第 {index + 1} 個 traversal 不連續：{previousEdge.TrackEdgeId} 結束於 {previousEnd}，但 {edge.TrackEdgeId} 起始於 {currentStart}。");
                }
            }
            previousEdge = edge;
            previousDirection = traversal.Direction;
        }
    }

    private static void ValidatePhysicalTurnbackReturn(
        TurnbackFacilityDefinition facility,
        TrackPosition stopPosition,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        if (!edges.TryGetValue(stopPosition.TrackEdgeId, out var stopEdge)
            || stopPosition.OffsetMeters <= TrackPosition.DefaultToleranceMeters
            || stopPosition.OffsetMeters >= stopEdge.LengthMeters - TrackPosition.DefaultToleranceMeters)
        {
            return;
        }

        var stopTraversalIndex = -1;
        for (var index = 0; index < facility.Traversals.Count; index++)
        {
            if (facility.Traversals[index].TrackEdgeId.Equals(
                    stopPosition.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
            {
                stopTraversalIndex = index;
                break;
            }
        }

        if (stopTraversalIndex < 0 || stopTraversalIndex + 1 >= facility.Traversals.Count)
        {
            errors.Add($"折返設施「{facility.FacilityId}」的中段停等位置後必須緊接同一實體 edge 的反向 traversal。 ");
            return;
        }

        var stopTraversal = facility.Traversals[stopTraversalIndex];
        var returnTraversal = facility.Traversals[stopTraversalIndex + 1];
        if (!returnTraversal.TrackEdgeId.Equals(stopTraversal.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            || returnTraversal.Direction == stopTraversal.Direction)
        {
            errors.Add($"折返設施「{facility.FacilityId}」的中段停等位置後必須緊接同一實體 edge 的反向 traversal。 ");
        }
    }

    private static void ValidateFacilityRouteBoundaries(
        TopologyInfrastructureDefinition definition,
        IReadOnlyList<ServiceRouteDefinition> routes,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        var routesById = routes
            .Where(route => !string.IsNullOrWhiteSpace(route.ServiceRouteId))
            .GroupBy(route => route.ServiceRouteId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var turnbackFacilities = definition.TurnbackFacilities
            .Where(facility => !string.IsNullOrWhiteSpace(facility.FacilityId))
            .GroupBy(facility => facility.FacilityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var operation in definition.TurnbackOperations)
        {
            if (!turnbackFacilities.TryGetValue(operation.FacilityId, out var facility)
                || facility.Traversals.Count == 0
                || !routesById.TryGetValue(operation.ArrivalServiceRouteId, out var arrivalRoute)
                || !routesById.TryGetValue(operation.DepartureServiceRouteId, out var departureRoute))
            {
                continue;
            }

            if (PlatformTurnback.IsStationary(facility)
                && arrivalRoute.Traversals.Any(t => t.TrackEdgeId == facility.ArrivalTrackEdgeId && t.Direction == facility.Traversals[1].Direction)
                && departureRoute.Traversals.Any(t => t.TrackEdgeId == facility.DepartureTrackEdgeId && t.Direction == facility.Traversals[0].Direction))
                continue;

            ValidateFacilityBoundary(
                operation.OperationId,
                "折返作業",
                facility.ArrivalTrackEdgeId,
                facility.ArrivalStopOffsetMeters,
                arrivalRoute,
                facility.Traversals[0],
                isArrival: true,
                edges,
                definition.DirectedConnections,
                errors,
                allowPlatformInterior: MatchesPlatformStop(definition, arrivalRoute, facility.ArrivalTrackEdgeId, facility.ArrivalStopOffsetMeters));
            ValidateFacilityBoundary(
                operation.OperationId,
                "折返作業",
                facility.DepartureTrackEdgeId,
                facility.DepartureStartOffsetMeters,
                departureRoute,
                facility.Traversals[^1],
                isArrival: false,
                edges,
                definition.DirectedConnections,
                errors,
                allowPlatformInterior: MatchesPlatformStop(definition, departureRoute, facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters));
        }

        var passingFacilities = definition.PassingFacilities
            .Where(facility => !string.IsNullOrWhiteSpace(facility.FacilityId))
            .GroupBy(facility => facility.FacilityId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var operation in definition.PassingOperations)
        {
            if (!passingFacilities.TryGetValue(operation.FacilityId, out var facility)
                || facility.Traversals.Count == 0
                || !routesById.TryGetValue(operation.ServiceRouteId, out var route))
            {
                continue;
            }

            ValidateFacilityBoundary(
                operation.OperationId,
                "越行作業",
                facility.ArrivalTrackEdgeId,
                facility.ArrivalOffsetMeters,
                route,
                facility.Traversals[0],
                isArrival: true,
                edges,
                definition.DirectedConnections,
                errors);
            ValidateFacilityBoundary(
                operation.OperationId,
                "越行作業",
                facility.DepartureTrackEdgeId,
                facility.DepartureStartOffsetMeters,
                route,
                facility.Traversals[^1],
                isArrival: false,
                edges,
                definition.DirectedConnections,
                errors);
        }
    }

    private static bool MatchesPlatformStop(TopologyInfrastructureDefinition definition, ServiceRouteDefinition route,
        string edgeId, double offset) => definition.Platforms.Any(p =>
        string.Equals(p.TrackEdgeId, edgeId, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(p.StopPositionOffsetMeters - offset) <= TrackPosition.DefaultToleranceMeters
        && p.PlatformStartOffsetMeters <= offset && p.PlatformEndOffsetMeters >= offset
        && route.Stops.Any(s => s.CandidatePlatformIds.Contains(p.PlatformId, StringComparer.OrdinalIgnoreCase)));

    private static void ValidateFacilityBoundary(
        string operationId,
        string operationKind,
        string routeEdgeId,
        double routeOffsetMeters,
        ServiceRouteDefinition route,
        DirectedTrackTraversal facilityTraversal,
        bool isArrival,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        IReadOnlyList<DirectedTrackConnectionDefinition> directedConnections,
        ICollection<string> errors,
        bool allowPlatformInterior = false)
    {
        if (!edges.TryGetValue(routeEdgeId, out var routeEdge)
            || !edges.TryGetValue(facilityTraversal.TrackEdgeId, out _))
        {
            return;
        }

        var candidates = route.Traversals.Where(traversal =>
                traversal.TrackEdgeId.Equals(routeEdgeId, StringComparison.OrdinalIgnoreCase))
            .Where(traversal =>
            {
                var boundaryOffset = isArrival
                    ? traversal.Direction == TraversalDirection.Forward ? routeEdge.LengthMeters : 0
                    : traversal.Direction == TraversalDirection.Forward ? 0 : routeEdge.LengthMeters;
                return Math.Abs(boundaryOffset - routeOffsetMeters) <= TrackPosition.DefaultToleranceMeters
                    || (allowPlatformInterior && double.IsFinite(routeOffsetMeters)
                        && routeOffsetMeters >= 0 && routeOffsetMeters <= routeEdge.LengthMeters);
            })
            .ToArray();
        var connected = candidates.Any(routeTraversal => isArrival
            ? AllowsTransition(edges, directedConnections, routeTraversal, facilityTraversal)
            : AllowsTransition(edges, directedConnections, facilityTraversal, routeTraversal));
        if (!connected)
        {
            errors.Add($"{operationKind}「{operationId}」未在營運路線「{route.ServiceRouteId}」的實體 edge 端點直接銜接設施 traversal。 ");
        }
    }

    private static void ValidatePassingFacilityTraversals(
        PassingFacilityDefinition facility,
        IReadOnlyDictionary<string, TrackEdgeDefinition> edges,
        ICollection<string> errors)
    {
        if (facility.Traversals.Count == 0)
        {
            errors.Add($"越行設施「{facility.FacilityId}」至少需要一個有序 traversal。 ");
            return;
        }

        TrackEdgeDefinition? previousEdge = null;
        TraversalDirection previousDirection = default;
        for (var index = 0; index < facility.Traversals.Count; index++)
        {
            var traversal = facility.Traversals[index];
            if (!edges.TryGetValue(traversal.TrackEdgeId, out var edge))
            {
                errors.Add($"越行設施「{facility.FacilityId}」第 {index + 1} 個 traversal 引用不存在的 edge「{traversal.TrackEdgeId}」。");
                continue;
            }
            if (!Enum.IsDefined(traversal.Direction) || !AllowsTraversal(edge, traversal.Direction))
                errors.Add($"越行設施「{facility.FacilityId}」第 {index + 1} 個 traversal 方向不符合 edge 設定。");
            if (previousEdge is not null)
            {
                var previousEnd = GetEndNode(previousEdge, previousDirection);
                var currentStart = GetStartNode(edge, traversal.Direction);
                if (!previousEnd.Equals(currentStart, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"越行設施「{facility.FacilityId}」第 {index} 與第 {index + 1} 個 traversal 不連續：{previousEdge.TrackEdgeId} 結束於 {previousEnd}，但 {edge.TrackEdgeId} 起始於 {currentStart}。");
            }
            previousEdge = edge;
            previousDirection = traversal.Direction;
        }
    }

    private static Dictionary<string, T> Index<T>(
        IEnumerable<T>? values,
        Func<T, string?> idSelector,
        string typeName,
        ICollection<string> errors)
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            var id = idSelector(value);
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"{typeName}編號不可空白。");
            }
            else if (!result.TryAdd(id.Trim(), value))
            {
                errors.Add($"{typeName}編號「{id.Trim()}」重複。");
            }
        }

        return result;
    }

    private static void EnsureDistinctReferences(
        IEnumerable<string>? values,
        string field,
        ICollection<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field}不可包含空白編號。");
            else if (!ids.Add(value.Trim())) errors.Add($"{field}包含重複編號「{value.Trim()}」。");
        }
    }

    private static void RequireText(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field}不可空白。");
    }

    private static void RequirePositive(double value, string field, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value <= 0) errors.Add($"{field}必須是有限且大於 0 的數值。");
    }

    private static void RequireNonNegative(double value, string field, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value < 0) errors.Add($"{field}必須是有限的非負數。");
    }

    private static void ThrowIfInvalid(InfrastructureValidationResult result)
    {
        if (!result.IsValid) throw new SimulationValidationException(result.Errors);
    }
}
