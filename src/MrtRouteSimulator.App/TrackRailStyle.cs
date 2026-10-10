using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

internal enum RailTone
{
    Neutral,
    Down,
    Up,
    DownSoft,
    UpSoft
}

/// <summary>配線圖軌道配色與粗細規則；只依服務路徑與 edge 種類決定外觀，不影響幾何。</summary>
internal static class TrackRailStyle
{
    public const double MainlineThickness = 6;
    public const double SideThickness = 4;

    public static IReadOnlyDictionary<string, RailTone> Classify(
        IEnumerable<TrackEdgeDefinition> edges,
        IReadOnlySet<string> outboundEdgeIds,
        IReadOnlySet<string> inboundEdgeIds)
    {
        var all = edges.ToArray();
        RailTone RouteTone(string edgeId)
        {
            var outbound = outboundEdgeIds.Contains(edgeId);
            var inbound = inboundEdgeIds.Contains(edgeId);
            return outbound && !inbound ? RailTone.Down : inbound && !outbound ? RailTone.Up : RailTone.Neutral;
        }

        static bool Same(string first, string second) => first.Equals(second, StringComparison.OrdinalIgnoreCase);

        static bool SharesNode(TrackEdgeDefinition first, TrackEdgeDefinition second) =>
            Same(first.FromNodeId, second.FromNodeId) || Same(first.FromNodeId, second.ToNodeId)
            || Same(first.ToNodeId, second.FromNodeId) || Same(first.ToNodeId, second.ToNodeId);

        static bool SharesBothNodes(TrackEdgeDefinition first, TrackEdgeDefinition second) =>
            Same(first.FromNodeId, second.FromNodeId) && Same(first.ToNodeId, second.ToNodeId)
            || Same(first.FromNodeId, second.ToNodeId) && Same(first.ToNodeId, second.FromNodeId);

        RailTone NeighbourTone(TrackEdgeDefinition edge, Func<TrackEdgeDefinition, bool> adjacent)
        {
            var tones = all
                .Where(other => !Same(other.TrackEdgeId, edge.TrackEdgeId) && adjacent(other))
                .Select(other => RouteTone(other.TrackEdgeId))
                .Where(other => other != RailTone.Neutral)
                .Distinct()
                .ToArray();
            return tones.Length == 1 ? tones[0] : RailTone.Neutral;
        }

        var result = new Dictionary<string, RailTone>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in all)
        {
            var tone = RouteTone(edge.TrackEdgeId);
            if (tone == RailTone.Neutral && edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.Siding)
            {
                // 側線不在任何方向路徑內時，跟隨並行（兩端共用）的單一方向正線；
                // 車站節點可能同時被上下行共用，因此只有找不到並行正線時才看單一共用端點。
                var neighbour = NeighbourTone(edge, other => SharesBothNodes(edge, other));
                if (neighbour == RailTone.Neutral) neighbour = NeighbourTone(edge, other => SharesNode(edge, other));
                if (neighbour != RailTone.Neutral)
                {
                    tone = neighbour == RailTone.Down ? RailTone.DownSoft : RailTone.UpSoft;
                }
            }

            result[edge.TrackEdgeId] = tone;
        }

        return result;
    }

    public static SolidColorBrush Brush(RailTone tone) => tone switch
    {
        RailTone.Down => UiTheme.RailDownBrush,
        RailTone.Up => UiTheme.RailUpBrush,
        RailTone.DownSoft => UiTheme.RailDownSoftBrush,
        RailTone.UpSoft => UiTheme.RailUpSoftBrush,
        _ => UiTheme.RailNeutralBrush
    };

    public static double Thickness(TrackEdgeDefinition edge) =>
        edge.Kind == TrackEdgeKind.Mainline ? MainlineThickness : SideThickness;
}
