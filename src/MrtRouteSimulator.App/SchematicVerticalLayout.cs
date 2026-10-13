using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>圖面垂直單位；只補示意中繼資料，不改動實體接軌或營運資料。</summary>
internal static class SchematicVerticalLayout
{
    public const double Unit = 23;

    public static TopologyProjectDocument FillDefaults(TopologyProjectDocument document, bool reset = false)
    {
        var layout = SchematicLayoutService.AutoLayout(document);
        var directions = new Dictionary<string, TrainDirection>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in document.DirectionRouteBindings)
        {
            var route = document.ServiceRoutes.FirstOrDefault(r => r.ServiceRouteId == binding.ServiceRouteId);
            if (route is null) continue;
            foreach (var traversal in route.Traversals)
                directions.TryAdd(traversal.TrackEdgeId, binding.Direction);
        }
        // 快速通過正線可能不在預設全停路徑內；依有向主線接續繼承方向。
        var mainlineIds = document.Topology.Edges.Where(e => e.Kind == TrackEdgeKind.Mainline)
            .Select(e => e.TrackEdgeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var connection in document.Topology.DirectedConnections)
            {
                if (!mainlineIds.Contains(connection.FromTrackEdgeId) || !mainlineIds.Contains(connection.ToTrackEdgeId)) continue;
                if (directions.TryGetValue(connection.FromTrackEdgeId, out var fromDirection)
                    && directions.TryAdd(connection.ToTrackEdgeId, fromDirection)) changed = true;
                if (directions.TryGetValue(connection.ToTrackEdgeId, out var toDirection)
                    && directions.TryAdd(connection.FromTrackEdgeId, toDirection)) changed = true;
            }
        }
        var lanes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var occupied = new List<(double Start, double End, double Lane)>();
        bool IsBranch(TrackEdgeDefinition edge) => edge.Kind is TrackEdgeKind.PassingTrack
            or TrackEdgeKind.Siding or TrackEdgeKind.PocketTrack or TrackEdgeKind.TailTrack
            or TrackEdgeKind.DepotLead or TrackEdgeKind.PlatformTrack
            || edge.Kind == TrackEdgeKind.Mainline && !directions.ContainsKey(edge.TrackEdgeId);
        (double Start, double End) Span(TrackEdgeDefinition edge)
        {
            var from = layout.TryGetValue(edge.FromNodeId, out var a) ? a.X : 0;
            var to = layout.TryGetValue(edge.ToNodeId, out var b) ? b.X : from;
            return (Math.Min(from, to), Math.Max(from, to));
        }
        var branches = document.Topology.Edges.Where(IsBranch)
            .OrderBy(e => Span(e).Start).ThenBy(e => e.TrackEdgeId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!reset)
            foreach (var edge in branches.Where(e => e.SchematicLane.HasValue))
            {
                var span = Span(edge);
                occupied.Add((span.Start, span.End, edge.SchematicLane!.Value));
            }
        foreach (var edge in branches)
        {
            if (!reset && edge.SchematicLane.HasValue) continue;
            var span = Span(edge);
            var lane = 2d;
            while (occupied.Any(p => Math.Abs(p.Lane - lane) < .001
                && p.Start <= span.End && span.Start <= p.End)) lane++;
            if (lane > 8)
            {
                if (reset) throw new SimulationValidationException(["支線預設配置已超過 +8；請手動調整重疊區段的垂直位置。"]);
                lane = 2; // 自動預覽仍可顯示合法專案，密集重疊交由使用者調整。
            }
            lanes[edge.TrackEdgeId] = lane;
            occupied.Add((span.Start, span.End, lane));
        }
        return document with { Topology = document.Topology with
        {
            Edges = document.Topology.Edges.Select(edge =>
            {
                if (edge.Kind == TrackEdgeKind.Mainline && directions.TryGetValue(edge.TrackEdgeId, out var mainlineDirection))
                    return edge with { SchematicLane = mainlineDirection == TrainDirection.Outbound ? 1 : -1 };
                if (!reset && edge.SchematicLane.HasValue) return edge;
                if (lanes.TryGetValue(edge.TrackEdgeId, out var branchLane))
                    return edge with { SchematicLane = branchLane };
                if (edge.Kind == TrackEdgeKind.Mainline && directions.TryGetValue(edge.TrackEdgeId, out var direction))
                    return edge with { SchematicLane = direction == TrainDirection.Outbound ? 1 : -1 };
                return edge;
            }).ToArray()
        } };
    }
}