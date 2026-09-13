namespace MrtRouteSimulator.Engine;

/// <summary>一條 ServiceRoute traversal 在 projection chainage 中的範圍。</summary>
public sealed record RouteProjectionSegment(
    int TraversalIndex,
    DirectedTrackTraversal Traversal,
    double StartChainageMeters,
    double EndChainageMeters);

/// <summary>
/// 把單一有序 ServiceRoute 投影成 legacy-compatible chainage。chainage 僅供顯示、統計及
/// 過渡相容；它不能反向成為列車的實體位置權威來源。
/// </summary>
public sealed class RouteProjection
{
    private const double ChainageToleranceMeters = TrackPosition.DefaultToleranceMeters;
    private readonly InfrastructureGraphV4 infrastructure;
    private readonly Dictionary<string, IReadOnlyList<int>> traversalIndicesByEdgeId;

    public RouteProjection(InfrastructureGraphV4 infrastructure, ServiceRouteDefinition serviceRoute)
    {
        this.infrastructure = infrastructure ?? throw new ArgumentNullException(nameof(infrastructure));
        ArgumentNullException.ThrowIfNull(serviceRoute);
        InfrastructureValidator.ValidateAndThrow(infrastructure, [serviceRoute]);
        ServiceRouteId = serviceRoute.ServiceRouteId;

        var segments = new List<RouteProjectionSegment>(serviceRoute.Traversals.Count);
        var position = 0d;
        for (var index = 0; index < serviceRoute.Traversals.Count; index++)
        {
            var traversal = serviceRoute.Traversals[index];
            var edge = infrastructure.GetRequiredEdge(traversal.TrackEdgeId);
            var end = position + edge.LengthMeters;
            segments.Add(new RouteProjectionSegment(index, traversal, position, end));
            position = end;
        }

        Segments = segments;
        TotalLengthMeters = position;
        traversalIndicesByEdgeId = segments
            .GroupBy(segment => segment.Traversal.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group.Select(segment => segment.TraversalIndex).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public string ServiceRouteId { get; }
    public IReadOnlyList<RouteProjectionSegment> Segments { get; }
    public double TotalLengthMeters { get; }

    /// <summary>
    /// 將唯一出現在本 projection 的 TrackPosition 轉成 chainage。重複 edge（loop）時，
    /// 呼叫端必須改用帶 traversalIndex 的 overload。
    /// </summary>
    public double ToChainage(TrackPosition position)
    {
        if (!traversalIndicesByEdgeId.TryGetValue(position.TrackEdgeId, out var indices))
        {
            throw new SimulationValidationException([$"位置「{position}」不屬於營運路線「{ServiceRouteId}」。"]);
        }

        if (indices.Count != 1)
        {
            throw new SimulationValidationException([$"位置「{position.TrackEdgeId}」在營運路線「{ServiceRouteId}」出現多次；請指定 traversal index。"]);
        }

        return ToChainage(indices[0], position);
    }

    /// <summary>以明確 traversal index 將 TrackPosition 轉成 chainage，可處理 loop。</summary>
    public double ToChainage(int traversalIndex, TrackPosition position)
    {
        if (traversalIndex < 0 || traversalIndex >= Segments.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(traversalIndex));
        }

        var segment = Segments[traversalIndex];
        if (!segment.Traversal.TrackEdgeId.Equals(position.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SimulationValidationException([$"traversal {traversalIndex} 不使用軌道 edge「{position.TrackEdgeId}」。"]);
        }

        var edge = infrastructure.GetRequiredEdge(position.TrackEdgeId);
        if (!double.IsFinite(position.OffsetMeters)
            || position.OffsetMeters < -ChainageToleranceMeters
            || position.OffsetMeters > edge.LengthMeters + ChainageToleranceMeters)
        {
            throw new SimulationValidationException([$"位置「{position}」的 offset 超出 edge「{edge.TrackEdgeId}」範圍。"]);
        }

        var offset = Math.Clamp(position.OffsetMeters, 0, edge.LengthMeters);
        var distanceAlongTraversal = segment.Traversal.Direction == TraversalDirection.Forward
            ? offset
            : edge.LengthMeters - offset;
        return segment.StartChainageMeters + distanceAlongTraversal;
    }

    /// <summary>將 route-local chainage 轉成 TrackPosition；連接邊界歸屬下一個 traversal。</summary>
    public TrackPosition FromChainage(double chainageMeters)
    {
        if (!double.IsFinite(chainageMeters)
            || chainageMeters < -ChainageToleranceMeters
            || chainageMeters > TotalLengthMeters + ChainageToleranceMeters)
        {
            throw new SimulationValidationException([$"chainage「{chainageMeters}」超出營運路線「{ServiceRouteId}」範圍。"]);
        }

        var chainage = Math.Clamp(chainageMeters, 0, TotalLengthMeters);
        foreach (var segment in Segments)
        {
            if (chainage < segment.EndChainageMeters - ChainageToleranceMeters
                || segment.TraversalIndex == Segments.Count - 1)
            {
                var edge = infrastructure.GetRequiredEdge(segment.Traversal.TrackEdgeId);
                var distanceAlongTraversal = Math.Clamp(chainage - segment.StartChainageMeters, 0, edge.LengthMeters);
                var offset = segment.Traversal.Direction == TraversalDirection.Forward
                    ? distanceAlongTraversal
                    : edge.LengthMeters - distanceAlongTraversal;
                return new TrackPosition(edge.TrackEdgeId, offset);
            }
        }

        throw new InvalidOperationException("找不到 chainage 對應的軌道 traversal。");
    }
}
