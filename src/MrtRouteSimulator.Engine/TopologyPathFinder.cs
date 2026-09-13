namespace MrtRouteSimulator.Engine;

/// <summary>Topology path finder 的可重現搜尋限制。</summary>
public sealed record TopologyPathConstraints
{
    public DirectedTrackTraversal? IncomingTraversal { get; init; }
    public DirectedTrackTraversal? OutgoingTraversal { get; init; }
    public IReadOnlySet<string> RequiredEdgeIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> ExcludedEdgeIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<TrackEdgeKind> AllowedEdgeKinds { get; init; } = new HashSet<TrackEdgeKind>();
    public IReadOnlySet<string> RequiredConflictResourceIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public int MaximumTraversalCount { get; init; } = 512;
}

/// <summary>可保存為 ServiceRoute 的有序 topology path 搜尋結果。</summary>
public sealed record TopologyPathResult(
    IReadOnlyList<DirectedTrackTraversal> Traversals,
    double TotalLengthMeters,
    IReadOnlySet<string> ConflictResourceIds);

/// <summary>
/// 依 edge 長度尋找 deterministic 最短有向路徑。輸出始終是 ordered traversal，
/// 所以結果可直接保存為 ServiceRoute，而不會退化成無順序的 edge 集合。
/// </summary>
public static class TopologyPathFinder
{
    public static TopologyPathResult FindShortestPath(
        InfrastructureGraphV4 infrastructure,
        string startNodeId,
        string endNodeId,
        TopologyPathConstraints? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        if (!infrastructure.Nodes.ContainsKey(startNodeId))
            throw new SimulationValidationException([$"找不到起始 topology node「{startNodeId}」。"]);
        if (!infrastructure.Nodes.ContainsKey(endNodeId))
            throw new SimulationValidationException([$"找不到終止 topology node「{endNodeId}」。"]);

        constraints ??= new TopologyPathConstraints();
        if (constraints.MaximumTraversalCount is < 1 or > 10_000)
            throw new SimulationValidationException(["Topology path 的最大 traversal 數必須介於 1 至 10000。"]);

        var queue = new PriorityQueue<PathState, PathPriority>();
        var start = new PathState(startNodeId, [], 0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        queue.Enqueue(start, new PathPriority(0, string.Empty));
        var bestCosts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [GetSearchStateKey(start, constraints)] = 0
        };

        while (queue.TryDequeue(out var state, out _))
        {
            if (state.NodeId.Equals(endNodeId, StringComparison.OrdinalIgnoreCase)
                && SatisfiesRequiredConstraints(state, constraints)
                && (constraints.OutgoingTraversal is not { } outgoing
                    || (state.Traversals.Count > 0 ? state.Traversals[^1] : constraints.IncomingTraversal) is not { } previous
                    || infrastructure.AllowsTransition(previous, outgoing)))
            {
                return new TopologyPathResult(state.Traversals, state.TotalLengthMeters, state.ConflictResourceIds);
            }

            if (state.Traversals.Count >= constraints.MaximumTraversalCount)
                continue;

            foreach (var candidate in GetCandidates(infrastructure, state.NodeId, constraints))
            {
                if ((state.Traversals.Count > 0 ? state.Traversals[^1] : constraints.IncomingTraversal) is { } incoming
                    && !infrastructure.AllowsTransition(incoming, candidate.Traversal))
                {
                    continue;
                }
                var nextLength = state.TotalLengthMeters + candidate.Edge.LengthMeters;
                var nextTraversals = state.Traversals.Concat([candidate.Traversal]).ToArray();
                var nextResources = new HashSet<string>(state.ConflictResourceIds, StringComparer.OrdinalIgnoreCase);
                nextResources.UnionWith(candidate.Edge.ConflictResourceIds);
                var nextState = new PathState(candidate.NextNodeId, nextTraversals, nextLength, nextResources);
                var nextStateKey = GetSearchStateKey(nextState, constraints);
                if (bestCosts.TryGetValue(nextStateKey, out var bestCost)
                    && nextLength > bestCost + TrackPosition.DefaultToleranceMeters)
                {
                    continue;
                }

                bestCosts[nextStateKey] = nextLength;
                queue.Enqueue(
                    nextState,
                    new PathPriority(nextLength, string.Join("|", nextTraversals.Select(item => $"{item.TrackEdgeId}:{item.Direction}"))));
            }
        }

        throw new SimulationValidationException([$"找不到由 topology node「{startNodeId}」通往「{endNodeId}」的可用路徑。"]);
    }

    private static IEnumerable<PathCandidate> GetCandidates(
        InfrastructureGraphV4 infrastructure,
        string nodeId,
        TopologyPathConstraints constraints)
    {
        foreach (var edge in infrastructure.OutgoingEdgesByNode.GetValueOrDefault(nodeId, []))
        {
            if (InfrastructureValidator.AllowsTraversal(edge, TraversalDirection.Forward)
                && IsAllowed(edge, constraints))
            {
                yield return new PathCandidate(edge, new DirectedTrackTraversal(edge.TrackEdgeId, TraversalDirection.Forward), edge.ToNodeId);
            }
        }

        foreach (var edge in infrastructure.IncomingEdgesByNode.GetValueOrDefault(nodeId, []))
        {
            if (InfrastructureValidator.AllowsTraversal(edge, TraversalDirection.Reverse)
                && IsAllowed(edge, constraints))
            {
                yield return new PathCandidate(edge, new DirectedTrackTraversal(edge.TrackEdgeId, TraversalDirection.Reverse), edge.FromNodeId);
            }
        }
    }

    private static bool IsAllowed(TrackEdgeDefinition edge, TopologyPathConstraints constraints) =>
        !constraints.ExcludedEdgeIds.Contains(edge.TrackEdgeId)
        && (constraints.AllowedEdgeKinds.Count == 0 || constraints.AllowedEdgeKinds.Contains(edge.Kind));

    private static bool SatisfiesRequiredConstraints(PathState state, TopologyPathConstraints constraints) =>
        constraints.RequiredEdgeIds.All(required => state.Traversals.Any(item => item.TrackEdgeId.Equals(required, StringComparison.OrdinalIgnoreCase)))
        && constraints.RequiredConflictResourceIds.All(state.ConflictResourceIds.Contains);

    private static string GetSearchStateKey(PathState state, TopologyPathConstraints constraints)
    {
        var traversedRequiredEdges = state.Traversals
            .Select(item => item.TrackEdgeId)
            .Where(constraints.RequiredEdgeIds.Contains)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
        var usedRequiredResources = state.ConflictResourceIds
            .Where(constraints.RequiredConflictResourceIds.Contains)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase);
        var previous = state.Traversals.Count > 0 ? state.Traversals[^1] : constraints.IncomingTraversal;
        return $"{state.NodeId}|{previous?.TrackEdgeId}:{previous?.Direction}|{string.Join(',', traversedRequiredEdges)}|{string.Join(',', usedRequiredResources)}";
    }

    private sealed record PathState(
        string NodeId,
        IReadOnlyList<DirectedTrackTraversal> Traversals,
        double TotalLengthMeters,
        IReadOnlySet<string> ConflictResourceIds);

    private readonly record struct PathPriority(double TotalLengthMeters, string TraversalKey) : IComparable<PathPriority>
    {
        public int CompareTo(PathPriority other)
        {
            var distanceComparison = TotalLengthMeters.CompareTo(other.TotalLengthMeters);
            return distanceComparison != 0
                ? distanceComparison
                : StringComparer.Ordinal.Compare(TraversalKey, other.TraversalKey);
        }
    }

    private sealed record PathCandidate(
        TrackEdgeDefinition Edge,
        DirectedTrackTraversal Traversal,
        string NextNodeId);
}
