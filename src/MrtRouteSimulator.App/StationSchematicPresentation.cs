using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>主畫面與編輯器共用的股道及站體呈現，不參與進路或停站運算。</summary>
internal static class StationSchematicPresentation
{
    // Keep edge offsets as the single position authority. The same mapping is used
    // for the rail, platform faces and moving vehicles, including non-linear anchors.
    public static IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> ApplyChainage(
        IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> original,
        TopologyProjectDocument? document, double width)
    {
        var projection = document is null ? null : StationChainageProjection.TryCreate(document);
        if (projection is null) return original;
        var result = original.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var minimum = projection.MinimumChainageMeters;
        var span = Math.Max(1, projection.MaximumChainageMeters - minimum);
        var explicitLayout = document!.Topology.Nodes.All(n => n.SchematicPosition.HasValue && n.SchematicLane.HasValue);
        double X(double chainage) => 32 + (chainage - minimum) / span * Math.Max(1, width - 64);
        foreach (var edge in document!.Topology.Edges)
        {
            if (!original.TryGetValue(edge.TrackEdgeId, out var source)
                || projection.ToChainage(new(edge.TrackEdgeId, 0)) is null) continue;
            Point At(double ratio) => new(explicitLayout ? source.PointAt(ratio).X : X(projection.ToChainage(new(edge.TrackEdgeId, ratio * edge.LengthMeters))!.Value),
                source.PointAt(ratio).Y);
            result[edge.TrackEdgeId] = new(Enumerable.Range(0, 101).Select(i => At(i / 100d)).ToArray()) { PositionAt = At };
        }
        // Explicit crossover edges must join track endpoints, beyond the platform.
        // Smooth their grade in schematic space without changing physical lengths.
        foreach (var facility in document.Topology.TurnbackFacilities)
        {
            var path = facility.Traversals;
            if (path.Count == 2 && path[0].TrackEdgeId == path[1].TrackEdgeId
                && facility.Kind is TurnbackFacilityKind.TailTrack or TurnbackFacilityKind.PocketTrack
                && result.TryGetValue(facility.ArrivalTrackEdgeId, out var arriving)
                && result.TryGetValue(facility.DepartureTrackEdgeId, out var departing)
                && result.TryGetValue(path[0].TrackEdgeId, out var siding))
            {
                var track = document.Topology.Edges.Single(e => e.TrackEdgeId == path[0].TrackEdgeId);
                var arrivalTrack = document.Topology.Edges.Single(e => e.TrackEdgeId == facility.ArrivalTrackEdgeId);
                var departureTrack = document.Topology.Edges.Single(e => e.TrackEdgeId == facility.DepartureTrackEdgeId);
                var node = path[0].Direction == TraversalDirection.Forward ? track.FromNodeId : track.ToNodeId;
                var singleArrival = arrivalTrack.FromNodeId == node ? arriving.From : arriving.To;
                var singleDeparture = departureTrack.FromNodeId == node ? departing.From : departing.To;
                var sign = arrivalTrack.ToNodeId == node ? Math.Sign(arriving.To.X - arriving.From.X)
                    : Math.Sign(arriving.From.X - arriving.To.X);
                if (sign == 0) sign = 1;
                var throat = Math.Max(28, Math.Abs(singleArrival.Y - singleDeparture.Y) * 1.5);
                var start = (sign > 0 ? Math.Max(singleArrival.X, singleDeparture.X) : Math.Min(singleArrival.X, singleDeparture.X)) + sign * throat;
                var oldBuffer = path[0].Direction == TraversalDirection.Forward ? siding.To.X : siding.From.X;
                var end = sign > 0 ? Math.Max(oldBuffer, start + 24) : Math.Min(oldBuffer, start - 24);
                var y = facility.Kind == TurnbackFacilityKind.PocketTrack ? (singleArrival.Y + singleDeparture.Y) / 2 : singleArrival.Y;
                Point At(double ratio)
                {
                    var t = path[0].Direction == TraversalDirection.Forward ? ratio : 1 - ratio;
                    return new(start + (end - start) * t, y);
                }
                result[track.TrackEdgeId] = new([At(0), At(1)]) { PositionAt = At };
            }
            if (path.Count != 4 || path[1].TrackEdgeId != path[2].TrackEdgeId
                || !result.TryGetValue(facility.ArrivalTrackEdgeId, out var arrival)
                || !result.TryGetValue(facility.DepartureTrackEdgeId, out var departure)) continue;
            var entry = document.Topology.Edges.Single(e => e.TrackEdgeId == path[0].TrackEdgeId);
            var exit = document.Topology.Edges.Single(e => e.TrackEdgeId == path[3].TrackEdgeId);
            if (entry.Kind != TrackEdgeKind.Crossover || exit.Kind != TrackEdgeKind.Crossover) continue;
            var arrivalEdge = document.Topology.Edges.Single(e => e.TrackEdgeId == facility.ArrivalTrackEdgeId);
            var departureEdge = document.Topology.Edges.Single(e => e.TrackEdgeId == facility.DepartureTrackEdgeId);
            var entryStart = path[0].Direction == TraversalDirection.Forward ? entry.FromNodeId : entry.ToNodeId;
            var exitEnd = path[3].Direction == TraversalDirection.Forward ? exit.ToNodeId : exit.FromNodeId;
            var a = entryStart == arrivalEdge.FromNodeId ? arrival.From : arrival.To;
            var d = exitEnd == departureEdge.FromNodeId ? departure.From : departure.To;
            var tailY = facility.Kind == TurnbackFacilityKind.PocketTrack ? (a.Y + d.Y) / 2 : a.Y;
            if (!result.TryGetValue(path[1].TrackEdgeId, out var tail)) continue;
            var arrivalSign = entryStart == arrivalEdge.ToNodeId
                ? Math.Sign(arrival.To.X - arrival.From.X) : Math.Sign(arrival.From.X - arrival.To.X);
            if (arrivalSign == 0) arrivalSign = 1;
            var tailStart = path[1].Direction == TraversalDirection.Forward ? tail.From.X : tail.To.X;
            var tailEnd = path[1].Direction == TraversalDirection.Forward ? tail.To.X : tail.From.X;
            // Opposite route projections can assign different x values to a shared node.
            // Anchor the explicit entry/exit to their actual rails and keep the throat
            // beyond both endpoints in the arrival direction.
            tailStart = arrivalSign > 0 ? Math.Max(tailStart, Math.Max(a.X, d.X) + 28)
                : Math.Min(tailStart, Math.Min(a.X, d.X) - 28);
            tailEnd = arrivalSign > 0 ? Math.Max(tailEnd, tailStart + 24) : Math.Min(tailEnd, tailStart - 24);
            void Set(DirectedTrackTraversal traversal, double startX, double endX, double startY, double endY)
            {
                if (!result.TryGetValue(traversal.TrackEdgeId, out var geometry)) return;
                var forward = traversal.Direction == TraversalDirection.Forward;
                Point At(double ratio)
                {
                    var t = forward ? ratio : 1 - ratio;
                    var smooth = t * t * (3 - 2 * t);
                    return new(startX + (endX - startX) * t, startY + (endY - startY) * smooth);
                }
                result[traversal.TrackEdgeId] = new(Enumerable.Range(0, 101).Select(i => At(i / 100d)).ToArray()) { PositionAt = At };
            }
            Set(path[0], a.X, tailStart, a.Y, tailY);
            Set(path[1], tailStart, tailEnd, tailY, tailY);
            Set(path[3], tailStart, d.X, tailY, d.Y);
        }
        // Every sloping rail must enter and leave its adjoining track tangentially.
        // Keep a level platform section for loops whose ends share a mainline lane.
        foreach (var (id, geometry) in result.ToArray())
        {
            var firstY = geometry.From.Y;
            var lastY = geometry.To.Y;
            var middleY = geometry.PointAt(.5).Y;
            if (geometry.Points.All(p => Math.Abs(p.Y - firstY) < .001)) continue;
            static double Ease(double value) => value * value * (3 - 2 * value);
            Point At(double ratio)
            {
                var y = Math.Abs(firstY - lastY) < .001
                    ? ratio < .25 ? firstY + (middleY - firstY) * Ease(ratio / .25)
                    : ratio > .75 ? middleY + (lastY - middleY) * Ease((ratio - .75) / .25) : middleY
                    : firstY + (lastY - firstY) * Ease(ratio);
                return new(geometry.PointAt(ratio).X, y);
            }
            result[id] = new(Enumerable.Range(0, 201).Select(i => At(i / 200d)).ToArray()) { PositionAt = At };
        }
        // Reserve room for schematic throats without clipping the buffer stops.
        var minX = result.Values.SelectMany(g => g.Points).Min(p => p.X);
        var maxX = result.Values.SelectMany(g => g.Points).Max(p => p.X);
        if (minX < 24 || maxX > width - 24)
        {
            foreach (var (id, geometry) in result.ToArray())
            {
                Point At(double ratio)
                {
                    var p = geometry.PointAt(ratio);
                    return new(24 + (p.X - minX) / Math.Max(1, maxX - minX) * (width - 48), p.Y);
                }
                result[id] = new(Enumerable.Range(0, 201).Select(i => At(i / 200d)).ToArray()) { PositionAt = At };
            }
        }
        return result;
    }

    public static void DrawConnection(Canvas canvas, Point from, Point to, Vector incoming, Vector outgoing, Brush brush, string tooltip)
    {
        var reverses = incoming.Length > .0001 && outgoing.Length > .0001
            && (Math.Abs(Vector.AngleBetween(incoming, outgoing)) >= 90
                || (to.X - from.X) * incoming.X < -.001 || (to.X - from.X) * outgoing.X < -.001);
        if (reverses || Math.Abs(to.X - from.X) < .5 && Math.Abs(to.Y - from.Y) > .5)
        {
            var warning = canvas.Children.OfType<TextBlock>().FirstOrDefault(t => Equals(t.Tag, "TrackConnectionIssue"));
            if (warning is null)
            {
                warning = new TextBlock { Text = "⚠ 配線待修：轉向反折或缺少渡線空間（詳見提示）", Foreground = Brushes.DarkOrange,
                    FontSize = 11, Tag = "TrackConnectionIssue", ToolTip = tooltip };
                Canvas.SetLeft(warning, 18); Canvas.SetTop(warning, 48); canvas.Children.Add(warning);
            }
            else warning.ToolTip += "\n" + tooltip;
            return;
        }
        if ((to - from).Length <= .5) return;
        var points = new PointCollection();
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100d;
            var smooth = t * t * (3 - 2 * t);
            points.Add(new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * smooth));
        }
        canvas.Children.Add(new Polyline { Points = points, Stroke = brush, StrokeThickness = 5,
            StrokeLineJoin = PenLineJoin.Round, ToolTip = tooltip });
    }
    public static void DrawChainageReference(Canvas canvas, StationChainageProjection? projection, double top)
    {
        if (projection is null) return;
        var label = new TextBlock { Text = $"起始站中心 0.000K · 配線里程 {projection.MinimumChainageMeters / 1000:0.000}K ～ {projection.MaximumChainageMeters / 1000:0.000}K",
            FontSize = 10, Foreground = Brushes.SlateGray };
        Canvas.SetLeft(label, 12); Canvas.SetTop(label, top); canvas.Children.Add(label);
    }
    // 標籤只能縮窄，不得為了避開邊界而移動月臺中心線。
    public static void PlaceStationLabel(FrameworkElement label, string stationId, double centerX,
        double top, double canvasWidth)
    {
        label.Width = Math.Max(1, Math.Min(160, 2 * Math.Max(0, Math.Min(centerX - 4, canvasWidth - 4 - centerX))));
        label.Tag = new StationLabelAnchor(stationId, centerX);
        Canvas.SetLeft(label, centerX - label.Width / 2);
        Canvas.SetTop(label, top);
    }

    internal sealed record StationLabelAnchor(string StationId, double PlatformCenterX, string? BodyId = null);
    internal sealed record PlatformBodyAnchor(string StationId, string BodyId);
    internal sealed record PlatformLayoutIssue(string Message);
    internal sealed record PlatformNumberAnchor(string StationId, string PlatformId);

    public static void DrawStationNames(Canvas canvas, IEnumerable<(string Id, string Name)> stations, double width)
    {
        var bodies = canvas.Children.OfType<Rectangle>().Where(r => r.Tag is PlatformBodyAnchor).ToArray();
        if (bodies.Length == 0) return;
        var upper = Math.Max(36, bodies.Min(r => Canvas.GetTop(r)) - 60);
        var lower = bodies.Max(r => Canvas.GetTop(r) + r.Height) + 32;
        var occupied = new List<Rect>();
        foreach (var station in stations)
        {
            var own = bodies.Where(r => ((PlatformBodyAnchor)r.Tag).StationId == station.Id).ToArray();
            // 相同中心線可共用名稱；錯列月臺各自標示，不把站名放到兩月臺之間的空白。
            foreach (var group in own.GroupBy(r => Math.Round(Canvas.GetLeft(r) + r.Width / 2, 1)))
            {
                var body = group.First();
                var center = Canvas.GetLeft(body) + body.Width / 2;
                var above = group.Average(r => Canvas.GetTop(r) + r.Height / 2) <= bodies.Average(r => Canvas.GetTop(r) + r.Height / 2);
                var label = new TextBlock { Text = $"{station.Id}\n{station.Name}", FontSize = 11,
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(30, 46, 64)),
                    Background = Brushes.White, ToolTip = $"{station.Id} · {station.Name}" };
                PlaceStationLabel(label, station.Id, center, above ? upper : lower, width);
                label.Width = Math.Min(label.Width, 110);
                Canvas.SetLeft(label, center - label.Width / 2);
                label.Tag = new StationLabelAnchor(station.Id, center, ((PlatformBodyAnchor)body.Tag).BodyId);
                label.Measure(new Size(label.Width, double.PositiveInfinity));
                var top = Canvas.GetTop(label);
                var box = new Rect(Canvas.GetLeft(label), top, label.Width, label.DesiredSize.Height);
                while (occupied.Any(r => r.IntersectsWith(box)))
                {
                    top += (above ? -1 : 1) * (label.DesiredSize.Height + 8);
                    if (top < 36) { above = false; top = lower; }
                    box.Y = top;
                }
                Canvas.SetTop(label, top); occupied.Add(box);
                canvas.Children.Add(new Line { X1 = center, X2 = center,
                    Y1 = above ? box.Bottom + 2 : box.Top - 2,
                    Y2 = above ? group.Min(r => Canvas.GetTop(r)) - 3 : group.Max(r => Canvas.GetTop(r) + r.Height) + 3,
                    Stroke = Brushes.SlateGray, StrokeThickness = .7, IsHitTestVisible = false });
                canvas.Children.Add(label);
                canvas.Height = Math.Max(canvas.Height, box.Bottom + 28);
            }
        }
    }

    // 示意圖警告不改寫 topology 或模擬位置；亦供 WPF 回歸測試使用。
    public static IReadOnlyList<string> ValidateStationLabels(Canvas canvas)
    {
        var warnings = new List<string>();
        warnings.AddRange(canvas.Children.OfType<FrameworkElement>().Where(e => e.Tag is PlatformLayoutIssue)
            .Select(e => ((PlatformLayoutIssue)e.Tag).Message));
        var numberBoxes = new List<(string Id, Rect Box)>();
        foreach (var number in canvas.Children.OfType<TextBlock>().Where(t => t.Tag is PlatformNumberAnchor))
        {
            number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var box = new Rect(Canvas.GetLeft(number), Canvas.GetTop(number), number.DesiredSize.Width, number.DesiredSize.Height);
            var anchor = (PlatformNumberAnchor)number.Tag;
            foreach (var previous in numberBoxes.Where(p => p.Box.IntersectsWith(box)))
                warnings.Add($"{anchor.PlatformId}／{previous.Id}：月臺編號重疊，請修正股道間距或月臺範圍。");
            numberBoxes.Add((anchor.PlatformId, box));
        }
        var boxes = new List<(string Id, Rect Bounds)>();
        var width = double.IsFinite(canvas.Width) ? canvas.Width : canvas.ActualWidth > 0 ? canvas.ActualWidth : 720;
        var platformBounds = canvas.Children.OfType<Rectangle>()
            .Where(body => body.Tag is PlatformBodyAnchor)
            .GroupBy(body => (((PlatformBodyAnchor)body.Tag).StationId, ((PlatformBodyAnchor)body.Tag).BodyId))
            .ToDictionary(group => group.Key, group => group.Select(body => new Rect(Canvas.GetLeft(body), Canvas.GetTop(body), body.Width, body.Height))
                .Aggregate((bounds, next) => { bounds.Union(next); return bounds; }));
        foreach (var label in canvas.Children.OfType<FrameworkElement>())
        {
            if (label.Tag is not StationLabelAnchor anchor) continue;
            label.Measure(new Size(label.Width, double.PositiveInfinity));
            var bounds = new Rect(Canvas.GetLeft(label), Canvas.GetTop(label), label.Width, label.DesiredSize.Height);
            var matching = platformBounds.Where(p => p.Key.StationId == anchor.StationId &&
                (anchor.BodyId is null || p.Key.BodyId == anchor.BodyId)).Select(p => p.Value).ToArray();
            var expectedCenter = matching.Length > 0 ? matching[0].Left + matching[0].Width / 2 : anchor.PlatformCenterX;
            if (matching.Length == 0) warnings.Add($"{anchor.StationId}：站名沒有對應月臺圖形。");
            if (Math.Abs(bounds.Left + bounds.Width / 2 - expectedCenter) > 0.5)
                warnings.Add($"{anchor.StationId}：站名未對齊月臺中心。");
            if (bounds.Left < 0 || bounds.Right > width || bounds.Top < 0 || bounds.Bottom > canvas.Height)
                warnings.Add($"{anchor.StationId}：站名超出圖面邊界。");
            if (platformBounds.Values.Any(body => body.IntersectsWith(bounds)))
                warnings.Add($"{anchor.StationId}：站名與月臺重疊。");
            foreach (var other in boxes.Where(other => other.Bounds.IntersectsWith(bounds)))
                warnings.Add($"{anchor.StationId}／{other.Id}：站名重疊，請調整站點示意位置或放大視窗。");
            boxes.Add((anchor.StationId, bounds));
            var text = label as TextBlock ?? (label as Border)?.Child as TextBlock;
            if (text is not null)
            {
                var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                    text.FontSize, Brushes.Black, 1);
                if (text.TextWrapping == TextWrapping.NoWrap && formatted.Width > label.Width + .5)
                    warnings.Add($"{anchor.StationId}：站名文字截斷。");
            }
        }
        return warnings;
    }

    public static void DrawLayoutWarnings(Canvas canvas)
    {
        var warnings = ValidateStationLabels(canvas);
        if (warnings.Count == 0) return;
        var label = new TextBlock { Text = $"⚠ 版面檢核：{warnings.Count} 項", FontSize = 11,
            Foreground = Brushes.DarkOrange, ToolTip = string.Join("\n", warnings) };
        Canvas.SetRight(label, 12); Canvas.SetTop(label, 12); canvas.Children.Add(label);
    }

    // 舊線性起稿以共用站節點連接兩股道；先展開實體折返路徑，避免
    // 兩條 crossover 在同一 X 座標被平行線排版畫成直立迴圈。
    public static void LayoutLinearTurnbacks(Dictionary<string, (Point From, Point To)> points,
        IEnumerable<TrackEdgeDefinition> edgeSource, IEnumerable<TurnbackFacilityDefinition> facilities,
        double width)
    {
        var edges = edgeSource.ToDictionary(e => e.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        foreach (var facility in facilities)
        {
            var path = facility.Traversals;
            if (path.Count == 2 && path[0].TrackEdgeId == path[1].TrackEdgeId
                && edges.TryGetValue(path[0].TrackEdgeId, out var single)
                && single.Kind is TrackEdgeKind.TailTrack or TrackEdgeKind.PocketTrack
                && !single.SchematicLane.HasValue
                && edges.TryGetValue(facility.ArrivalTrackEdgeId, out var arrivalSingle)
                && edges.TryGetValue(facility.DepartureTrackEdgeId, out var departureSingle)
                && points.TryGetValue(arrivalSingle.TrackEdgeId, out var arrivalPoints)
                && points.TryGetValue(departureSingle.TrackEdgeId, out var departurePoints))
            {
                var singleStart = arrivalPoints.From + (arrivalPoints.To - arrivalPoints.From) * (facility.ArrivalStopOffsetMeters / arrivalSingle.LengthMeters);
                var singleFinish = departurePoints.From + (departurePoints.To - departurePoints.From) * (facility.DepartureStartOffsetMeters / departureSingle.LengthMeters);
                var pocket = single.Kind == TrackEdgeKind.PocketTrack;
                var sign = pocket ? Math.Sign((arrivalPoints.To.X - arrivalPoints.From.X) *
                    (facility.ArrivalStopOffsetMeters > arrivalSingle.LengthMeters / 2 ? 1 : -1)) : singleStart.X < width / 2 ? -1 : 1;
                if (sign == 0) sign = 1;
                var singleAvailable = Math.Max(1, Math.Min(100, sign < 0 ? singleStart.X - 22 : width - 22 - singleStart.X));
                var y = pocket ? (singleStart.Y + singleFinish.Y) / 2 : singleStart.Y;
                var singleJunction = new Point(singleStart.X + sign * singleAvailable * .35, y);
                var singleBuffer = new Point(singleStart.X + sign * singleAvailable, y);
                points[single.TrackEdgeId] = path[0].Direction == TraversalDirection.Forward ? (singleJunction, singleBuffer) : (singleBuffer, singleJunction);
                continue;
            }
            if (path.Count != 4 || path[1].TrackEdgeId != path[2].TrackEdgeId
                || !edges.TryGetValue(path[0].TrackEdgeId, out var entry)
                || !edges.TryGetValue(path[1].TrackEdgeId, out var tail)
                || !edges.TryGetValue(path[3].TrackEdgeId, out var exit)
                || entry.Kind != TrackEdgeKind.Crossover || tail.Kind != TrackEdgeKind.TailTrack
                || exit.Kind != TrackEdgeKind.Crossover
                || entry.SchematicLane.HasValue || tail.SchematicLane.HasValue
                || !edges.TryGetValue(facility.ArrivalTrackEdgeId, out var arrival)
                || !edges.TryGetValue(facility.DepartureTrackEdgeId, out var departure)
                || !points.TryGetValue(arrival.TrackEdgeId, out var a)
                || !points.TryGetValue(departure.TrackEdgeId, out var d)) continue;
            Point At((Point From, Point To) p, double offset, double length) => p.From + (p.To - p.From) * (offset / length);
            var start = At(a, facility.ArrivalStopOffsetMeters, arrival.LengthMeters);
            var finish = At(d, facility.DepartureStartOffsetMeters, departure.LengthMeters);
            var direction = start.X < width / 2 ? -1 : 1;
            var available = direction < 0 ? start.X - 22 : width - 22 - start.X;
            var junction = new Point(start.X + direction * available * .45, start.Y);
            var buffer = new Point(start.X + direction * available, start.Y);
            void Set(DirectedTrackTraversal t, Point from, Point to) => points[t.TrackEdgeId] =
                t.Direction == TraversalDirection.Forward ? (from, to) : (to, from);
            Set(path[0], start, junction);
            Set(path[1], junction, buffer);
            Set(path[3], junction, finish);
        }
    }

    public static void DrawLegend(Canvas canvas)
    {
        var legend = new TextBlock { Text = "← 上行　　下行 →　　靠右行駛・里程向右增加", FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(25, 96, 125)) };
        Canvas.SetLeft(legend, 18); Canvas.SetTop(legend, 12); canvas.Children.Add(legend);
    }
    public static void ApplyNodeLayout(Dictionary<string, (Point From, Point To)> points,
        IEnumerable<TrackNodeDefinition> nodes, IEnumerable<TrackEdgeDefinition> edges,
        double middle, double unit, double left, double width)
    {
        var placed = nodes.Where(n => n.SchematicPosition.HasValue && n.SchematicLane.HasValue).ToArray();
        if (placed.Length == 0) return;
        var min = placed.Min(n => n.SchematicPosition!.Value);
        var span = Math.Max(1, placed.Max(n => n.SchematicPosition!.Value) - min);
        var positions = placed.ToDictionary(n => n.NodeId, n => new Point(
            left + (n.SchematicPosition!.Value - min) / span * width,
            middle + n.SchematicLane!.Value * unit), StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges)
        {
            if (!points.TryGetValue(edge.TrackEdgeId, out var value)) continue;
            points[edge.TrackEdgeId] = (positions.GetValueOrDefault(edge.FromNodeId, value.From),
                positions.GetValueOrDefault(edge.ToNodeId, value.To));
        }
    }
    public static IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> ApplyLanes(
        IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> original,
        IEnumerable<TrackEdgeDefinition> edgeSource, double middle, double unit)
    {
        var edges = edgeSource.ToArray();
        var result = original.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var edge in edges.Where(e => e.SchematicLane.HasValue))
        {
            if (!original.TryGetValue(edge.TrackEdgeId, out var geometry)) continue;
            var y = middle + edge.SchematicLane!.Value * unit;
            var from = new Point(geometry.From.X, y);
            var to = new Point(geometry.To.X, y);
            if (edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.PocketTrack or TrackEdgeKind.Siding)
            {
                double EndY(string node, double fallback)
                {
                    var main = edges.Where(e => e.Kind == TrackEdgeKind.Mainline && e.SchematicLane.HasValue
                        && (e.FromNodeId == node || e.ToNodeId == node))
                        .OrderBy(e => Math.Abs(e.SchematicLane!.Value - edge.SchematicLane!.Value)).FirstOrDefault();
                    return main is null ? fallback : middle + main.SchematicLane!.Value * unit;
                }
                from.Y = EndY(edge.FromNodeId, y);
                to.Y = EndY(edge.ToNodeId, y);
                var dx = to.X - from.X;
                result[edge.TrackEdgeId] = new([from, new(from.X + dx * .18, y), new(from.X + dx * .82, y), to]);
            }
            else result[edge.TrackEdgeId] = new([from, to]);
        }
        return result;
    }

    public static IReadOnlyDictionary<string, double> DrawPlatforms(Canvas canvas, IEnumerable<PlatformDefinitionV4> platforms,
        IEnumerable<TrackEdgeDefinition> edges,
        IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> geometry, double middle)
    {
        var index = edges.ToDictionary(e => e.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        var faces = new List<(PlatformDefinitionV4 Platform, Rect Box, Point Rail)>();
        foreach (var p in platforms)
        {
            if (!index.TryGetValue(p.TrackEdgeId, out var edge) || !geometry.TryGetValue(p.TrackEdgeId, out var rail)) continue;
            var start = rail.PointAt(p.PlatformStartOffsetMeters / edge.LengthMeters);
            var end = rail.PointAt(p.PlatformEndOffsetMeters / edge.LengthMeters);
            var center = rail.PointAt((p.PlatformStartOffsetMeters + p.PlatformEndOffsetMeters) / 2 / edge.LengthMeters);
            var side = p.DisplaySide == PlatformSide.Above ? -1 : p.DisplaySide == PlatformSide.Below ? 1 : center.Y <= middle ? 1 : -1;
            var length = Math.Max(24, Math.Abs(end.X - start.X));
            var left = center.X - length / 2;
            // Minimum visual width may expand around the physical center, but
            // endpoint padding must never translate the platform away from it.
            faces.Add((p, new Rect(left, center.Y + (side > 0 ? 7 : -21), length, 14), center));
        }
        var stationBounds = new Dictionary<string, Rect>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in faces.GroupBy(f => (f.Platform.StationId,
                     Body: string.IsNullOrWhiteSpace(f.Platform.PlatformBodyId) ? f.Platform.PlatformId : f.Platform.PlatformBodyId)))
        {
            var rect = group.First().Box;
            foreach (var face in group.Skip(1)) rect.Union(face.Box);
            if (group.Max(f => f.Box.Left) > group.Min(f => f.Box.Right))
                canvas.Children.Add(new FrameworkElement { Tag = new PlatformLayoutIssue(
                    $"{group.Key.StationId}／{group.Key.Body}：同一月臺本體各側錯位且沒有共同範圍，請拆分本體或修正月臺範圍。") });
            if (stationBounds.TryGetValue(group.Key.StationId, out var bounds))
            {
                bounds.Union(rect);
                stationBounds[group.Key.StationId] = bounds;
            }
            else stationBounds[group.Key.StationId] = rect;
            var body = new Rectangle { Width = rect.Width, Height = rect.Height,
                Tag = new PlatformBodyAnchor(group.Key.StationId, group.Key.Body),
                Fill = new SolidColorBrush(Color.FromRgb(25, 96, 125)),
                ToolTip = string.Join("\n", group.Select(f => $"{f.Platform.Name} · {f.Platform.TrackEdgeId}")) };
            Panel.SetZIndex(body, 1);
            Canvas.SetLeft(body, rect.Left); Canvas.SetTop(body, rect.Top); canvas.Children.Add(body);
            foreach (var face in group)
            {
                var number = string.IsNullOrWhiteSpace(face.Platform.PlatformNumber)
                    ? face.Platform.AllowedDirection == TrackDirection.Outbound ? "1" : face.Platform.AllowedDirection == TrackDirection.Inbound ? "2" : "•"
                    : face.Platform.PlatformNumber;
                var label = new TextBlock { Text = number, FontSize = 10, Foreground = Brushes.Black,
                    Tag = new PlatformNumberAnchor(face.Platform.StationId, face.Platform.PlatformId),
                    Background = Brushes.White, Padding = new Thickness(2, 0, 2, 0), IsHitTestVisible = false };
                Panel.SetZIndex(label, 2);
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, face.Box.Left + face.Box.Width / 2 - label.DesiredSize.Width / 2);
                Canvas.SetTop(label, face.Box.Top); canvas.Children.Add(label);
            }
        }
        return stationBounds.ToDictionary(p => p.Key, p => p.Value.Left + p.Value.Width / 2,
            StringComparer.OrdinalIgnoreCase);
    }
}
