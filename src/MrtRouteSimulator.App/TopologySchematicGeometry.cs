using System.Windows;

namespace MrtRouteSimulator.App;

/// <summary>
/// WPF topology 圖的純呈現幾何。Engine 仍只認 edge-local position；此處只負責把
/// 共用端點的平行軌道錯開，並保留每條線直接接回 topology node 的視覺連續性。
/// </summary>
internal sealed record TopologySchematicEdgeGeometry(IReadOnlyList<Point> Points)
{
    public Func<double, Point>? PositionAt { get; init; }
    public Point From => Points[0];
    public Point To => Points[^1];

    public Point PointAt(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        if (PositionAt is not null) return PositionAt(ratio);
        if (Points.Count == 1)
        {
            return Points[0];
        }

        var segmentLengths = new double[Points.Count - 1];
        var totalLength = 0d;
        for (var index = 0; index < segmentLengths.Length; index++)
        {
            var length = (Points[index + 1] - Points[index]).Length;
            segmentLengths[index] = length;
            totalLength += length;
        }

        if (totalLength <= double.Epsilon)
        {
            return Points[0];
        }

        var target = totalLength * ratio;
        for (var index = 0; index < segmentLengths.Length; index++)
        {
            var length = segmentLengths[index];
            if (target <= length || index == segmentLengths.Length - 1)
            {
                var localRatio = length <= double.Epsilon ? 0 : target / length;
                return Points[index] + (Points[index + 1] - Points[index]) * localRatio;
            }

            target -= length;
        }

        return Points[^1];
    }
}

internal static class TopologySchematicGeometry
{
    private const double ParallelSpacing = 16;
    private const double BendRatio = 0.14;

    public static IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> Build(
        IEnumerable<(string EdgeId, Point From, Point To)> edges)
    {
        var values = edges.ToArray();
        var result = new Dictionary<string, TopologySchematicEdgeGeometry>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in values.GroupBy(item => CoordinatePairKey(item.From, item.To)))
        {
            var ordered = group.OrderBy(item => item.EdgeId, StringComparer.OrdinalIgnoreCase).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var edge = ordered[index];
                var offset = (index - (ordered.Length - 1) / 2d) * ParallelSpacing;
                result.Add(edge.EdgeId, new TopologySchematicEdgeGeometry(
                    RoundCorners(CreatePolyline(edge.From, edge.To, offset))));
            }
        }

        return result;
    }

    private static IReadOnlyList<Point> CreatePolyline(Point from, Point to, double offset)
    {
        var vector = to - from;
        if (Math.Abs(offset) <= double.Epsilon || vector.Length <= double.Epsilon)
        {
            return [from, to];
        }

        var canonicalVector = IsBefore(from, to) ? vector : -vector;
        canonicalVector.Normalize();
        var normal = new Vector(-canonicalVector.Y, canonicalVector.X) * offset;
        return
        [
            from,
            from + vector * BendRatio + normal,
            from + vector * (1 - BendRatio) + normal,
            to
        ];
    }

    // Sample rounded bends once; rails, platforms and train cursors consume the same points.
    private static IReadOnlyList<Point> RoundCorners(IReadOnlyList<Point> points)
    {
        if (points.Count < 3) return points;
        var rounded = new List<Point> { points[0] };
        for (var index = 1; index < points.Count - 1; index++)
        {
            var corner = points[index];
            var incoming = points[index - 1] - corner;
            var outgoing = points[index + 1] - corner;
            var trim = Math.Min(12, Math.Min(incoming.Length, outgoing.Length) * 0.25);
            if (trim < 0.01) { rounded.Add(corner); continue; }
            incoming.Normalize();
            outgoing.Normalize();
            var entry = corner + incoming * trim;
            var exit = corner + outgoing * trim;
            rounded.Add(entry);
            for (var step = 1; step <= 8; step++)
            {
                var t = step / 8d;
                rounded.Add(new Point(
                    (1 - t) * (1 - t) * entry.X + 2 * (1 - t) * t * corner.X + t * t * exit.X,
                    (1 - t) * (1 - t) * entry.Y + 2 * (1 - t) * t * corner.Y + t * t * exit.Y));
            }
        }
        rounded.Add(points[^1]);
        return rounded;
    }

    private static string CoordinatePairKey(Point first, Point second)
    {
        var firstKey = CoordinateKey(first);
        var secondKey = CoordinateKey(second);
        return string.Compare(firstKey, secondKey, StringComparison.Ordinal) <= 0
            ? $"{firstKey}|{secondKey}"
            : $"{secondKey}|{firstKey}";
    }

    private static string CoordinateKey(Point point) => $"{point.X:0.###},{point.Y:0.###}";

    private static bool IsBefore(Point first, Point second) =>
        first.X < second.X || (Math.Abs(first.X - second.X) <= double.Epsilon && first.Y <= second.Y);
}
