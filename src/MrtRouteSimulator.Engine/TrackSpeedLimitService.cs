namespace MrtRouteSimulator.Engine;

/// <summary>以 edge-local coordinate 評估正常主線的速限與提前煞車目標。</summary>
public sealed class TrackSpeedLimitService
{
    private const double Epsilon = TrackPosition.DefaultToleranceMeters;
    private readonly InfrastructureGraphV4 infrastructure;
    private readonly IReadOnlyList<TrackSpeedLimitDefinition> limits;

    public TrackSpeedLimitService(
        InfrastructureGraphV4 infrastructure,
        IEnumerable<TrackSpeedLimitDefinition>? limits = null)
    {
        this.infrastructure = infrastructure ?? throw new ArgumentNullException(nameof(infrastructure));
        this.limits = (limits ?? infrastructure.SpeedLimits.Values).ToArray();
        InfrastructureValidator.ValidateAndThrow(new TopologyInfrastructureDefinition
        {
            Nodes = infrastructure.Nodes.Values.ToArray(),
            Edges = infrastructure.Edges.Values.ToArray(),
            Stations = infrastructure.Stations.Values.ToArray(),
            Platforms = infrastructure.Platforms.Values.ToArray(),
            Resources = infrastructure.Resources.Values.ToArray(),
            SpeedLimits = this.limits,
            Gradients = infrastructure.Gradients.Values.ToArray(),
            Curves = infrastructure.Curves.Values.ToArray(),
            TurnbackFacilities = infrastructure.TurnbackFacilities.Values.ToArray(),
            TurnbackOperations = infrastructure.TurnbackOperations.Values.ToArray(),
            PassingFacilities = infrastructure.PassingFacilities.Values.ToArray(),
            PassingOperations = infrastructure.PassingOperations.Values.ToArray(),
            StationOperations = infrastructure.StationOperations.Values.ToArray()
        });
    }

    /// <summary>Schema 7 global-chainage 速限的過渡轉換；輸出已完全以 edge-local offset 表示。</summary>
    public static IReadOnlyList<TrackSpeedLimitDefinition> ProjectLegacyLimits(
        RouteProjection outbound,
        RouteProjection inbound,
        IEnumerable<SpeedLimitSegment> legacyLimits)
    {
        var projected = new List<TrackSpeedLimitDefinition>();
        foreach (var (limit, index) in legacyLimits.Select((limit, index) => (limit, index)))
        {
            if (limit.Direction is SpeedLimitDirection.Both or SpeedLimitDirection.Outbound)
            {
                AddProjected(limit, index, outbound, limit.StartPositionMeters, limit.EndPositionMeters, projected);
            }
            if (limit.Direction is SpeedLimitDirection.Both or SpeedLimitDirection.Inbound)
            {
                AddProjected(limit, index, inbound,
                    inbound.TotalLengthMeters - limit.EndPositionMeters,
                    inbound.TotalLengthMeters - limit.StartPositionMeters,
                    projected);
            }
        }
        return projected;
    }

    public double GetPermittedSpeedMetersPerSecond(
        RouteProjection projection,
        int traversalIndex,
        TrackPosition position,
        double trainMaximumMetersPerSecond,
        double brakingMetersPerSecondSquared,
        double jerkMetersPerSecondCubed,
        double currentSpeedMetersPerSecond)
    {
        var chainage = projection.ToChainage(traversalIndex, position);
        var permitted = GetCurrentLimit(projection, traversalIndex, position, trainMaximumMetersPerSecond);
        foreach (var target in GetRestrictionBoundaries(projection, chainage, trainMaximumMetersPerSecond))
        {
            var jerkAllowance = jerkMetersPerSecondCubed > 0
                ? currentSpeedMetersPerSecond * brakingMetersPerSecondSquared / jerkMetersPerSecondCubed
                    + brakingMetersPerSecondSquared * brakingMetersPerSecondSquared
                    / (2 * jerkMetersPerSecondCubed * jerkMetersPerSecondCubed)
                : 0;
            var usable = Math.Max(0, target.ChainageMeters - chainage - jerkAllowance);
            var curve = Math.Sqrt(Math.Max(0, target.TargetSpeedMetersPerSecond * target.TargetSpeedMetersPerSecond
                + 2 * brakingMetersPerSecondSquared * usable));
            permitted = Math.Min(permitted, curve);
        }

        return Math.Clamp(permitted, 0, trainMaximumMetersPerSecond);
    }

    public double GetCurrentLimit(
        RouteProjection projection,
        int traversalIndex,
        TrackPosition position,
        double trainMaximumMetersPerSecond)
    {
        var segment = projection.Segments[traversalIndex];
        var edge = infrastructure.GetRequiredEdge(position.TrackEdgeId);
        return limits.Where(limit => limit.TrackEdgeId.Equals(position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && limit.Direction == segment.Traversal.Direction
                && position.OffsetMeters >= limit.StartOffsetMeters - Epsilon
                && position.OffsetMeters <= limit.EndOffsetMeters + Epsilon)
            .Select(limit => limit.LimitMetersPerSecond)
            .Append(edge.DefaultSpeedLimitMetersPerSecond)
            .Append(trainMaximumMetersPerSecond)
            .Min();
    }

    /// <summary>topology-native 速限計算，不經 RouteProjection／global chainage。</summary>
    public double GetPermittedSpeedMetersPerSecond(
        TopologyRouteNavigator navigator,
        TopologyTraversalCursor cursor,
        double trainMaximumMetersPerSecond,
        double brakingMetersPerSecondSquared,
        double jerkMetersPerSecondCubed,
        double currentSpeedMetersPerSecond)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        var permitted = GetCurrentLimit(navigator, cursor, trainMaximumMetersPerSecond);
        foreach (var target in GetRestrictionBoundaries(navigator, cursor, trainMaximumMetersPerSecond))
        {
            var jerkAllowance = jerkMetersPerSecondCubed > 0
                ? currentSpeedMetersPerSecond * brakingMetersPerSecondSquared / jerkMetersPerSecondCubed
                    + brakingMetersPerSecondSquared * brakingMetersPerSecondSquared
                    / (2 * jerkMetersPerSecondCubed * jerkMetersPerSecondCubed)
                : 0;
            var usable = Math.Max(0, target.DistanceMeters - jerkAllowance);
            var curve = Math.Sqrt(Math.Max(0, target.TargetSpeedMetersPerSecond * target.TargetSpeedMetersPerSecond
                + 2 * brakingMetersPerSecondSquared * usable));
            permitted = Math.Min(permitted, curve);
        }

        return Math.Clamp(permitted, 0, trainMaximumMetersPerSecond);
    }

    /// <summary>topology-native 當前 edge-local 速限。</summary>
    public double GetCurrentLimit(
        TopologyRouteNavigator navigator,
        TopologyTraversalCursor cursor,
        double trainMaximumMetersPerSecond)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        var traversal = navigator.GetTraversal(cursor.TraversalIndex);
        var edge = infrastructure.GetRequiredEdge(cursor.Position.TrackEdgeId);
        return limits.Where(limit => limit.TrackEdgeId.Equals(cursor.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && limit.Direction == traversal.Direction
                && cursor.Position.OffsetMeters >= limit.StartOffsetMeters - Epsilon
                && cursor.Position.OffsetMeters <= limit.EndOffsetMeters + Epsilon)
            .Select(limit => limit.LimitMetersPerSecond)
            .Append(edge.DefaultSpeedLimitMetersPerSecond)
            .Append(trainMaximumMetersPerSecond)
            .Min();
    }

    private IEnumerable<(double ChainageMeters, double TargetSpeedMetersPerSecond)> GetRestrictionBoundaries(
        RouteProjection projection,
        double currentChainage,
        double trainMaximumMetersPerSecond)
    {
        var boundaries = projection.Segments
            .Select(segment => segment.StartChainageMeters)
            .Concat(limits.SelectMany(limit => projection.Segments
                .Where(segment => segment.Traversal.TrackEdgeId.Equals(limit.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                    && segment.Traversal.Direction == limit.Direction)
                .Select(segment => projection.ToChainage(segment.TraversalIndex,
                    new TrackPosition(limit.TrackEdgeId, limit.StartOffsetMeters)))))
            .Where(boundary => boundary > currentChainage + Epsilon)
            .Distinct()
            .OrderBy(boundary => boundary);
        foreach (var boundary in boundaries)
        {
            var next = Math.Min(projection.TotalLengthMeters, boundary + Epsilon * 10);
            var position = projection.FromChainage(next);
            var segment = projection.Segments.First(item => next < item.EndChainageMeters - Epsilon
                || item.TraversalIndex == projection.Segments.Count - 1);
            yield return (boundary, GetCurrentLimit(projection, segment.TraversalIndex, position, trainMaximumMetersPerSecond));
        }
    }

    private IEnumerable<(double DistanceMeters, double TargetSpeedMetersPerSecond)> GetRestrictionBoundaries(
        TopologyRouteNavigator navigator,
        TopologyTraversalCursor current,
        double trainMaximumMetersPerSecond)
    {
        var boundaries = new List<TopologyTraversalCursor>();
        for (var traversalIndex = current.TraversalIndex; traversalIndex < navigator.Traversals.Count; traversalIndex++)
        {
            var traversal = navigator.GetTraversal(traversalIndex);
            var edge = infrastructure.GetRequiredEdge(traversal.TrackEdgeId);
            boundaries.Add(navigator.CreateCursor(traversalIndex, 0));
            foreach (var limit in limits.Where(limit =>
                         limit.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                         && limit.Direction == traversal.Direction))
            {
                var startDistance = traversal.Direction == TraversalDirection.Forward
                    ? limit.StartOffsetMeters
                    : edge.LengthMeters - limit.EndOffsetMeters;
                boundaries.Add(navigator.CreateCursor(traversalIndex, startDistance));
            }
        }

        foreach (var boundary in boundaries
                     .Select(boundary => new { Boundary = boundary, Distance = navigator.TryGetForwardDistance(current, boundary) })
                     .Where(item => item.Distance is > Epsilon)
                     .OrderBy(item => item.Distance)
                     .ThenBy(item => item.Boundary.TraversalIndex)
                     .ThenBy(item => item.Boundary.Position.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
                     .GroupBy(item => (item.Boundary.TraversalIndex, Math.Round(item.Boundary.Position.OffsetMeters, 6)))
                     .Select(group => group.First()))
        {
            var next = navigator.Advance(boundary.Boundary, Epsilon * 10);
            yield return (boundary.Distance!.Value, GetCurrentLimit(navigator, next, trainMaximumMetersPerSecond));
        }
    }

    private static void AddProjected(
        SpeedLimitSegment limit,
        int legacyIndex,
        RouteProjection projection,
        double startChainage,
        double endChainage,
        ICollection<TrackSpeedLimitDefinition> output)
    {
        foreach (var segment in projection.Segments)
        {
            var start = Math.Max(startChainage, segment.StartChainageMeters);
            var end = Math.Min(endChainage, segment.EndChainageMeters);
            if (end < start + Epsilon)
            {
                continue;
            }
            var edgeLength = segment.EndChainageMeters - segment.StartChainageMeters;
            var startDistance = start - segment.StartChainageMeters;
            var endDistance = end - segment.StartChainageMeters;
            var first = segment.Traversal.Direction == TraversalDirection.Forward ? startDistance : edgeLength - startDistance;
            var last = segment.Traversal.Direction == TraversalDirection.Forward ? endDistance : edgeLength - endDistance;
            output.Add(new TrackSpeedLimitDefinition
            {
                SpeedLimitId = $"LEGACY:{projection.ServiceRouteId}:{legacyIndex}:{segment.TraversalIndex}",
                TrackEdgeId = segment.Traversal.TrackEdgeId,
                StartOffsetMeters = Math.Min(first, last),
                EndOffsetMeters = Math.Max(first, last),
                LimitMetersPerSecond = limit.LimitMetersPerSecond,
                Direction = segment.Traversal.Direction
            });
        }
    }
}
