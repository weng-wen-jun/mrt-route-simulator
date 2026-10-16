namespace MrtRouteSimulator.Engine;

/// <summary>
/// Edge-local longitudinal grade. Positive means uphill toward increasing edge offset;
/// a reverse traversal negates it. Missing intervals are level track. The 0.009
/// acceleration coefficient retains the project's documented grade convention.
/// </summary>
public sealed class TrackGradientService
{
    public const double AccelerationPerPermille = 0.009;
    private readonly Dictionary<string, TrackGradientSegment[]> byEdge;
    private readonly Dictionary<(TopologyMovementNavigator, int, int), (double Min, double Max)> suffixBounds = [];

    public TrackGradientService(InfrastructureGraphV4 infrastructure)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        byEdge = infrastructure.Gradients.Values.GroupBy(item => item.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.StartOffsetMeters).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        foreach (var intervals in byEdge.Values)
        for (var first = 0; first < intervals.Length; first++)
        for (var second = first + 1; second < intervals.Length; second++)
        {
            if (intervals[second].StartOffsetMeters >= intervals[first].EndOffsetMeters) break;
            if (intervals[first].GradePermille != intervals[second].GradePermille)
                throw new SimulationValidationException([
                    $"軌道 {intervals[first].TrackEdgeId} 的重疊坡度區間有不同坡度；請先明確劃分實體區間。"]);
        }
    }

    public bool HasGradients => byEdge.Count != 0;

    public double GetGradePermille(RuntimeTopologyCursor cursor)
    {
        if (!byEdge.TryGetValue(cursor.Position.TrackEdgeId, out var intervals)) return 0;
        var offset = cursor.Position.OffsetMeters;
        var interval = intervals.FirstOrDefault(item => cursor.Direction == TraversalDirection.Forward
            ? offset >= item.StartOffsetMeters && offset < item.EndOffsetMeters
            : offset > item.StartOffsetMeters && offset <= item.EndOffsetMeters);
        return (interval?.GradePermille ?? 0) * (cursor.Direction == TraversalDirection.Forward ? 1 : -1);
    }

    /// <summary>
    /// Bounds every grade still reachable in this movement plan, including the current
    /// interval. Including zero makes the bound conservative across ungraded gaps.
    /// Follower braking uses Min; leader stopping-distance credit uses Max.
    /// </summary>
    public (double Min, double Max) GetForwardGradeBounds(TopologyMovementNavigator navigator, RuntimeTopologyCursor cursor)
    {
        if (!HasGradients) return (0, 0);
        var key = (navigator, cursor.MovementLegIndex, cursor.TraversalIndex);
        if (!suffixBounds.TryGetValue(key, out var result))
        {
            result = (0, 0);
            for (var leg = cursor.MovementLegIndex; leg < navigator.Legs.Count; leg++)
            for (var index = leg == cursor.MovementLegIndex ? cursor.TraversalIndex + 1 : 0;
                 index < navigator.Legs[leg].Traversals.Count; index++)
            {
                var traversal = navigator.GetTraversal(leg, index);
                if (!byEdge.TryGetValue(traversal.Edge.TrackEdgeId, out var intervals)) continue;
                foreach (var item in intervals)
                    Include(ref result, item.GradePermille * (traversal.Direction == TraversalDirection.Forward ? 1 : -1));
            }
            suffixBounds.Add(key, result);
        }
        if (byEdge.TryGetValue(cursor.Position.TrackEdgeId, out var current))
        foreach (var item in current)
        {
            if (cursor.Direction == TraversalDirection.Forward ? item.EndOffsetMeters <= cursor.Position.OffsetMeters
                : item.StartOffsetMeters >= cursor.Position.OffsetMeters) continue;
            Include(ref result, item.GradePermille * (cursor.Direction == TraversalDirection.Forward ? 1 : -1));
        }
        return result;
    }

    private static void Include(ref (double Min, double Max) bounds, double grade) =>
        bounds = (Math.Min(bounds.Min, grade), Math.Max(bounds.Max, grade));
}
