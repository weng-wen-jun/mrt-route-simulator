namespace MrtRouteSimulator.Engine;

/// <summary>營運路線上一個邏輯停靠站及其可選月台。</summary>
public sealed record ServiceRouteStop
{
    public required string StationId { get; init; }
    public IReadOnlyList<string> CandidatePlatformIds { get; init; } = [];
}

/// <summary>
/// 一條營運方向的有序實體路徑。Traversals 是 list；不得以 set 取代，因為順序與重複
/// traversal 都具有營運語意。
/// </summary>
public sealed record ServiceRouteDefinition
{
    public required string ServiceRouteId { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<DirectedTrackTraversal> Traversals { get; init; } = [];
    public IReadOnlyList<ServiceRouteStop> Stops { get; init; } = [];
}

/// <summary>將一個邏輯停站解析為指定 ServiceRoute 上唯一可供列車煞停的實體位置。</summary>
public sealed record ResolvedStop(
    string StationId,
    string PlatformId,
    int TraversalIndex,
    TrackPosition Position,
    double ChainageMeters);

/// <summary>
/// 將 route stop 的 station/platform 語意解析成 ordered traversal 的 track-local stop。
/// 同一 station/edge 若在 loop 中重複，解析器依 traversal 的有序累積距離選擇下一個合法候選。
/// 此解析不需要 <see cref="RouteProjection"/>；所產生的 chainage 僅是供結果呈現的衍生值。
/// </summary>
public static class ResolvedStopResolver
{
    public static IReadOnlyList<ResolvedStop> Resolve(
        InfrastructureGraphV4 infrastructure,
        ServiceRouteDefinition serviceRoute)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(serviceRoute);
        InfrastructureValidator.ValidateAndThrow(infrastructure, [serviceRoute]);

        var traversalDistances = CalculateTraversalStartDistances(infrastructure, serviceRoute);
        var resolved = new List<ResolvedStop>(serviceRoute.Stops.Count);
        var minimumDistance = 0d;
        foreach (var stop in serviceRoute.Stops)
        {
            var candidates = stop.CandidatePlatformIds
                .Where(infrastructure.Platforms.ContainsKey)
                .SelectMany(platformId => ResolvePlatformCandidates(
                    infrastructure,
                    serviceRoute,
                    traversalDistances,
                    infrastructure.Platforms[platformId],
                    minimumDistance))
                .OrderBy(candidate => candidate.ChainageMeters)
                .ThenBy(candidate => candidate.TraversalIndex)
                .ThenBy(candidate => candidate.PlatformId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length == 0)
            {
                throw new SimulationValidationException([
                    $"營運路線「{serviceRoute.ServiceRouteId}」的停靠站「{stop.StationId}」找不到位於後續 traversal 的候選月台。"
                ]);
            }

            var selected = candidates[0];
            resolved.Add(selected with { StationId = stop.StationId });
            minimumDistance = selected.ChainageMeters - TrackPosition.DefaultToleranceMeters;
        }

        return resolved;
    }

    /// <summary>
    /// 保留既有呼叫端的相容 overload。停站解析完全以 ServiceRoute traversals 完成；
    /// projection 僅檢核 route identity，不能反向決定 physical stop。
    /// </summary>
    public static IReadOnlyList<ResolvedStop> Resolve(
        InfrastructureGraphV4 infrastructure,
        ServiceRouteDefinition serviceRoute,
        RouteProjection projection)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(serviceRoute);
        ArgumentNullException.ThrowIfNull(projection);
        if (!projection.ServiceRouteId.Equals(serviceRoute.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SimulationValidationException(["ResolvedStop 使用的 RouteProjection 與 ServiceRoute 不一致。"]);
        }

        return Resolve(infrastructure, serviceRoute);
    }

    private static IEnumerable<ResolvedStop> ResolvePlatformCandidates(
        InfrastructureGraphV4 infrastructure,
        ServiceRouteDefinition serviceRoute,
        IReadOnlyList<double> traversalStartDistances,
        PlatformDefinitionV4 platform,
        double minimumDistance)
    {
        for (var traversalIndex = 0; traversalIndex < serviceRoute.Traversals.Count; traversalIndex++)
        {
            var traversal = serviceRoute.Traversals[traversalIndex];
            if (!traversal.TrackEdgeId.Equals(platform.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var edge = infrastructure.GetRequiredEdge(platform.TrackEdgeId);
            var position = new TrackPosition(platform.TrackEdgeId, platform.StopPositionOffsetMeters);
            var distanceAlongTraversal = traversal.Direction == TraversalDirection.Forward
                ? position.OffsetMeters
                : edge.LengthMeters - position.OffsetMeters;
            var chainage = traversalStartDistances[traversalIndex] + distanceAlongTraversal;
            if (chainage + TrackPosition.DefaultToleranceMeters < minimumDistance)
            {
                continue;
            }

            yield return new ResolvedStop(
                platform.StationId,
                platform.PlatformId,
                traversalIndex,
                position,
                chainage);
        }
    }

    private static IReadOnlyList<double> CalculateTraversalStartDistances(
        InfrastructureGraphV4 infrastructure,
        ServiceRouteDefinition serviceRoute)
    {
        var result = new double[serviceRoute.Traversals.Count];
        var distance = 0d;
        for (var index = 0; index < serviceRoute.Traversals.Count; index++)
        {
            result[index] = distance;
            distance += infrastructure.GetRequiredEdge(serviceRoute.Traversals[index].TrackEdgeId).LengthMeters;
        }

        return result;
    }
}

/// <summary>
/// topology-first SimulationWorld 建構輸入。正常主線及所有 V2 runtime 皆以這些 topology
/// 資料為權威。<see cref="CreateCompatibilityRoute"/> 僅供 V1 analytical adapter／明確相容測試使用。
/// </summary>
public sealed record TopologySimulationDefinition(
    InfrastructureGraphV4 Infrastructure,
    ServiceRouteDefinition OutboundServiceRoute,
    ServiceRouteDefinition InboundServiceRoute)
{
    public LinearInfrastructureBuildResult ToBuildResult()
    {
        InfrastructureValidator.ValidateAndThrow(Infrastructure, [OutboundServiceRoute, InboundServiceRoute]);
        return new LinearInfrastructureBuildResult(Infrastructure, OutboundServiceRoute, InboundServiceRoute);
    }

    public Route CreateCompatibilityRoute()
    {
        var projection = new RouteProjection(Infrastructure, OutboundServiceRoute);
        var stops = ResolvedStopResolver.Resolve(Infrastructure, OutboundServiceRoute, projection);
        return new Route(
            OutboundServiceRoute.ServiceRouteId,
            OutboundServiceRoute.Name,
            stops.Select(stop =>
            {
                var station = Infrastructure.Stations[stop.StationId];
                return new Station(station.StationId, station.Name, stop.ChainageMeters, station.DefaultDwellTimeSeconds);
            }).ToArray());
    }
}
