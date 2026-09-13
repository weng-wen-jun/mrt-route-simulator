namespace MrtRouteSimulator.Engine;

/// <summary>節點兩側的接軌契約；行進轉向必須由一側進入、另一側離開。</summary>
public static class TrackPortRules
{
    public static TrackPortSide Opposite(TrackPortSide side) => side == TrackPortSide.A ? TrackPortSide.B : TrackPortSide.A;

    public static bool Allows(TrackEdgeDefinition from, TraversalDirection incoming,
        TrackEdgeDefinition to, TraversalDirection outgoing)
    {
        // A stationary change of cab is validated by the existing turnback rules.
        if (from.TrackEdgeId.Equals(to.TrackEdgeId, StringComparison.OrdinalIgnoreCase) && incoming != outgoing) return true;
        var a = incoming == TraversalDirection.Forward ? from.ToPortSide : from.FromPortSide;
        var b = outgoing == TraversalDirection.Forward ? to.FromPortSide : to.ToPortSide;
        return a is null && b is null || a is not null && b is not null && a != b;
    }

    internal static void Validate(TopologyInfrastructureDefinition topology,
        IReadOnlyList<ServiceRouteDefinition> routes, ICollection<string> errors)
    {
        var edges = topology.Edges.GroupBy(e => e.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in topology.Edges)
            if (edge.FromPortSide.HasValue != edge.ToPortSide.HasValue
                || edge.FromPortSide is { } a && !Enum.IsDefined(a)
                || edge.ToPortSide is { } b && !Enum.IsDefined(b))
                errors.Add($"[CONNECTION-002] 軌道 edge「{edge.TrackEdgeId}」實體接軌側別必須兩端皆為 A/B，或兩端皆未指定。");
        var connections = topology.DirectedConnections.ToList();
        foreach (var path in routes.Select(r => r.Traversals)
            .Concat(topology.TurnbackFacilities.Select(f => f.Traversals))
            .Concat(topology.PassingFacilities.Select(f => f.Traversals)))
            for (var i = 1; i < path.Count; i++)
                connections.Add(new(path[i-1].TrackEdgeId, path[i-1].Direction, path[i].TrackEdgeId, path[i].Direction));
        foreach (var c in connections.Distinct())
        {
            if (!edges.TryGetValue(c.FromTrackEdgeId, out var from) || !edges.TryGetValue(c.ToTrackEdgeId, out var to)) continue;
            if (!Allows(from, c.FromDirection, to, c.ToDirection))
                errors.Add($"[CONNECTION-001] 軌道 edge「{from.TrackEdgeId}」({c.FromDirection}) → 軌道 edge「{to.TrackEdgeId}」({c.ToDirection}) 接軌側別不相容；同側不可直接回頭，已指定側別的接點不可接入未指定側別軌道。");
        }
    }
}
