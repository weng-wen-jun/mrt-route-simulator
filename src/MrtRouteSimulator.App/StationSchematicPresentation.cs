using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>主畫面與編輯器共用的股道及站體呈現，不參與進路或停站運算。</summary>
internal static class StationSchematicPresentation
{
    public static bool UsesCompactLaneTransitions(TopologyProjectDocument? document, double width)
    {
        var projection = document is null ? null : StationChainageProjection.TryCreate(document);
        return projection is not null
            && (projection.MaximumChainageMeters - projection.MinimumChainageMeters) / Math.Max(1, width - 64) > 50;
    }

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
        var compactLaneTransitions = UsesCompactLaneTransitions(document, width);
        double X(double chainage) => 32 + (chainage - minimum) / span * Math.Max(1, width - 64);
        var chainageEdges = document.Topology.Edges.ToDictionary(
            edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        bool UsesMainlineLaneTransition(TrackEdgeDefinition edge) =>
            compactLaneTransitions && edge.Kind == TrackEdgeKind.Mainline && edge.SchematicLane.HasValue
            && document.Topology.DirectedConnections.Any(connection =>
            {
                var otherId = connection.FromTrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                    ? connection.ToTrackEdgeId
                    : connection.ToTrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                        ? connection.FromTrackEdgeId
                        : null;
                return otherId is not null && chainageEdges.TryGetValue(otherId, out var other)
                    && other.Kind == TrackEdgeKind.Mainline
                    && Math.Abs((other.SchematicLane ?? 0) - edge.SchematicLane.Value) > .001;
            });
        var anchoredNodes = document.Topology.Edges
            .Where(edge => compactLaneTransitions && edge.SchematicLane.HasValue
                && (edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.PocketTrack or TrackEdgeKind.Siding
                    || UsesMainlineLaneTransition(edge)))
            .SelectMany(edge => new[] { edge.FromNodeId, edge.ToNodeId })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        (double Start, double End) EdgeAnchors(TrackEdgeDefinition edge)
        {
            var platforms = document.Topology.Platforms
                .Where(platform => platform.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return platforms.Length == 0 || edge.LengthMeters <= TrackPosition.DefaultToleranceMeters
                ? (.25, .75)
                : (Math.Clamp(platforms.Min(platform => platform.PlatformStartOffsetMeters) / edge.LengthMeters - .03, .08, .45),
                    Math.Clamp(platforms.Max(platform => platform.PlatformEndOffsetMeters) / edge.LengthMeters + .03, .55, .92));
        }
        double ProjectedX(TrackEdgeDefinition edge, double ratio) =>
            X(projection.ToChainage(new(edge.TrackEdgeId, ratio * edge.LengthMeters))!.Value);
        bool IsFacilityLaneEdge(TrackEdgeDefinition edge) =>
            edge.SchematicLane.HasValue
            && edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.PocketTrack or TrackEdgeKind.Siding;
        bool HasCollapsedFacilityProjection(TrackEdgeDefinition edge) =>
            IsFacilityLaneEdge(edge)
            && (compactLaneTransitions
                || Math.Abs(ProjectedX(edge, 1) - ProjectedX(edge, 0)) < 24);
        var nodeAnchorX = anchoredNodes.ToDictionary(
            nodeId => nodeId,
            nodeId =>
            {
                var incident = document.Topology.Edges
                    .Where(edge => edge.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)
                        || edge.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                    .Where(edge => projection.ToChainage(new(edge.TrackEdgeId,
                        edge.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase) ? 0 : edge.LengthMeters)) is not null)
                    .ToArray();
                var preferred = incident
                    .OrderBy(edge => edge.Kind == TrackEdgeKind.Mainline && !edge.SchematicLane.HasValue ? 0
                        : edge.Kind == TrackEdgeKind.Mainline ? 1 : 2)
                    .ThenBy(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
                    .First();
                var preferredRatio = preferred.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase) ? 0d : 1d;
                var candidate = ProjectedX(preferred, preferredRatio);
                var lower = double.NegativeInfinity;
                var upper = double.PositiveInfinity;
                foreach (var edge in incident)
                {
                    var anchors = EdgeAnchors(edge);
                    var endpointRatio = edge.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase) ? 0d : 1d;
                    var interiorRatio = endpointRatio == 0 ? anchors.Start : anchors.End;
                    var endpointX = ProjectedX(edge, endpointRatio);
                    var interiorX = ProjectedX(edge, interiorRatio);
                    if (interiorX > endpointX + .001) upper = Math.Min(upper, interiorX);
                    else if (interiorX < endpointX - .001) lower = Math.Max(lower, interiorX);
                }
                return lower <= upper ? Math.Clamp(candidate, lower, upper) : candidate;
            },
            StringComparer.OrdinalIgnoreCase);
        var compactLaneEdges = document.Topology.Edges
            .Where(edge => HasCollapsedFacilityProjection(edge) || UsesMainlineLaneTransition(edge))
            .ToDictionary(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        var endpointX = document.Topology.Edges
            .SelectMany(edge => new[]
            {
                (Key: (edge.TrackEdgeId, IsFrom: true), Value: anchoredNodes.Contains(edge.FromNodeId)
                    ? nodeAnchorX[edge.FromNodeId] : ProjectedX(edge, 0)),
                (Key: (edge.TrackEdgeId, IsFrom: false), Value: anchoredNodes.Contains(edge.ToNodeId)
                    ? nodeAnchorX[edge.ToNodeId] : ProjectedX(edge, 1))
            })
            .ToDictionary(item => item.Key, item => item.Value);
        foreach (var edge in compactLaneEdges.Values)
        {
            var platforms = document.Topology.Platforms
                .Where(platform => platform.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (platforms.Length == 0) continue;
            var stationCenter = platforms
                .Select(platform => projection.StationCenters.GetValueOrDefault(platform.StationId, double.NaN))
                .Where(double.IsFinite)
                .DefaultIfEmpty(double.NaN)
                .Average();
            if (!double.IsFinite(stationCenter)) continue;
            var centerX = X(stationCenter);
            var direction = Math.Sign(ProjectedX(edge, .75) - ProjectedX(edge, .25));
            if (direction == 0) direction = Math.Sign(endpointX[(edge.TrackEdgeId, false)] - endpointX[(edge.TrackEdgeId, true)]);
            if (direction == 0 && original.TryGetValue(edge.TrackEdgeId, out var sourceGeometry))
                direction = Math.Sign(sourceGeometry.To.X - sourceGeometry.From.X);
            if (direction == 0) continue;
            // A short physical section can collapse to only a few pixels in a
            // full-line overview.  Reserve enough horizontal run for its lane
            // transition to stay visibly tangential at the shared node.
            const double throat = 24;
            endpointX[(edge.TrackEdgeId, true)] = direction > 0
                ? Math.Min(endpointX[(edge.TrackEdgeId, true)], centerX - throat)
                : Math.Max(endpointX[(edge.TrackEdgeId, true)], centerX + throat);
            endpointX[(edge.TrackEdgeId, false)] = direction > 0
                ? Math.Max(endpointX[(edge.TrackEdgeId, false)], centerX + throat)
                : Math.Min(endpointX[(edge.TrackEdgeId, false)], centerX - throat);
        }
        var widePassingEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A passing siding and its through edge share both physical junctions.
        // In a wide scrollable overview, spread those junctions around the
        // station so the siding reads as a parallel platform track rather than
        // a tiny bump. Bind only the facility's arrival/departure tracks: the
        // two directions may reuse a logical station node but need separate
        // left/right schematic junctions.
        foreach (var facility in document.Topology.PassingFacilities)
        {
            if (width < 2000 || document.Topology.Edges.Count < 32) continue;
            var localPlatform = document.Topology.Platforms.FirstOrDefault(p =>
                p.PlatformId.Equals(facility.LocalPlatformId, StringComparison.OrdinalIgnoreCase));
            var throughPlatform = document.Topology.Platforms.FirstOrDefault(p =>
                p.PlatformId.Equals(facility.ExpressPlatformId, StringComparison.OrdinalIgnoreCase));
            if (localPlatform is null || throughPlatform is null
                || !chainageEdges.TryGetValue(localPlatform.TrackEdgeId, out var siding)
                || !chainageEdges.TryGetValue(throughPlatform.TrackEdgeId, out var through)
                || siding.Kind is not (TrackEdgeKind.Siding or TrackEdgeKind.PassingTrack)
                || through.Kind != TrackEdgeKind.Mainline
                || !siding.FromNodeId.Equals(through.FromNodeId, StringComparison.OrdinalIgnoreCase)
                || !siding.ToNodeId.Equals(through.ToNodeId, StringComparison.OrdinalIgnoreCase)
                || !projection.StationCenters.TryGetValue(facility.StationId, out var stationChainage)) continue;
            var centerX = X(stationChainage);
            var nearestStationGap = projection.StationCenters
                .Where(pair => !pair.Key.Equals(facility.StationId, StringComparison.OrdinalIgnoreCase))
                .Select(pair => Math.Abs(X(pair.Value) - centerX))
                .DefaultIfEmpty(0).Min();
            var halfWidth = Math.Min(32, nearestStationGap * .44);
            if (halfWidth < 18) continue;
            var direction = Math.Sign(ProjectedX(through, .75) - ProjectedX(through, .25));
            if (direction == 0) continue;
            compactLaneEdges[through.TrackEdgeId] = through;
            widePassingEdges.Add(siding.TrackEdgeId);
            widePassingEdges.Add(through.TrackEdgeId);
            foreach (var (nodeId, x, adjacentEdgeId) in new[]
            {
                (siding.FromNodeId, centerX - direction * halfWidth, facility.ArrivalTrackEdgeId),
                (siding.ToNodeId, centerX + direction * halfWidth, facility.DepartureTrackEdgeId)
            })
            {
                anchoredNodes.Add(nodeId);
                foreach (var edge in new[] { siding, through, chainageEdges[adjacentEdgeId] })
                {
                    if (edge.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                        endpointX[(edge.TrackEdgeId, true)] = x;
                    if (edge.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase))
                        endpointX[(edge.TrackEdgeId, false)] = x;
                }
            }
        }
        var compactAdjacentEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connection in document.Topology.DirectedConnections)
        {
            var fromEdge = chainageEdges[connection.FromTrackEdgeId];
            var toEdge = chainageEdges[connection.ToTrackEdgeId];
            var fromKey = (fromEdge.TrackEdgeId, IsFrom: connection.FromDirection != TraversalDirection.Forward);
            var toKey = (toEdge.TrackEdgeId, IsFrom: connection.ToDirection == TraversalDirection.Forward);
            var fromIsCompact = compactLaneEdges.ContainsKey(fromEdge.TrackEdgeId);
            var toIsCompact = compactLaneEdges.ContainsKey(toEdge.TrackEdgeId);
            var fromIsFacility = IsFacilityLaneEdge(fromEdge);
            var toIsFacility = IsFacilityLaneEdge(toEdge);
            if ((fromIsCompact || fromIsFacility) && !(toIsCompact || toIsFacility))
            {
                // Preserve the projected chainage of the through edge.  A side
                // lane may have a station-centre anchor that is deliberately
                // longer/shorter than the mainline; moving the mainline endpoint
                // to that anchor reverses the next mainline segment in a compact
                // full-line view.  Adjust the compact edge to the authoritative
                // through-edge endpoint instead.
                endpointX[fromKey] = endpointX[toKey];
            }
            else if (!(fromIsCompact || fromIsFacility) && (toIsCompact || toIsFacility))
            {
                endpointX[toKey] = endpointX[fromKey];
            }
            else if ((fromIsCompact || fromIsFacility) && (toIsCompact || toIsFacility))
            {
                endpointX[toKey] = endpointX[fromKey];
            }
        }
        foreach (var edge in document!.Topology.Edges)
        {
            if (!original.TryGetValue(edge.TrackEdgeId, out var source)
                || projection.ToChainage(new(edge.TrackEdgeId, 0)) is null) continue;
            var (startAnchor, endAnchor) = EdgeAnchors(edge);
            var edgePlatforms = document.Topology.Platforms
                .Where(platform => platform.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            static double Ease(double value) => value * value * (3 - 2 * value);
            // Keep a small non-zero tangent through facility platform throats.
            // A pure smoothstep reaches zero horizontal speed at the platform
            // boundary; combined with a lane transition this becomes a false
            // near-vertical kink in a compressed overview.
            static double MonotoneX(double ratio, double platformStart, double platformCenter,
                double platformEnd, double fromX, double platformStartX,
                double desiredCenter, double platformEndX, double toX)
            {
                var times = new[] { 0d, platformStart, platformCenter, platformEnd, 1d };
                var values = new[] { fromX, platformStartX, desiredCenter, platformEndX, toX };
                var secants = Enumerable.Range(0, 4)
                    .Select(i => (values[i + 1] - values[i]) / Math.Max(.001, times[i + 1] - times[i]))
                    .ToArray();
                static double SharedSlope(double left, double right) => left * right <= 0
                    ? 0 : Math.Sign(left) * Math.Min(Math.Abs(left), Math.Abs(right));
                var slopes = new[] { secants[0], SharedSlope(secants[0], secants[1]),
                    SharedSlope(secants[1], secants[2]), SharedSlope(secants[2], secants[3]), secants[3] };
                var segment = ratio <= platformStart ? 0 : ratio <= platformCenter ? 1
                    : ratio <= platformEnd ? 2 : 3;
                var span = Math.Max(.001, times[segment + 1] - times[segment]);
                var t = Math.Clamp((ratio - times[segment]) / span, 0, 1);
                var t2Value = t * t;
                var t3Value = t2Value * t;
                var value = (2 * t3Value - 3 * t2Value + 1) * values[segment]
                    + (t3Value - 2 * t2Value + t) * span * slopes[segment]
                    + (-2 * t3Value + 3 * t2Value) * values[segment + 1]
                    + (t3Value - t2Value) * span * slopes[segment + 1];
                return Math.Clamp(value, Math.Min(values[segment], values[segment + 1]),
                    Math.Max(values[segment], values[segment + 1]));
            }
            Point At(double ratio)
            {
                var sourcePoint = source.PointAt(ratio);
                if (explicitLayout) return sourcePoint;
                var x = X(projection.ToChainage(new(edge.TrackEdgeId, ratio * edge.LengthMeters))!.Value);
                var linearizeFacilityLane = compactLaneEdges.ContainsKey(edge.TrackEdgeId)
                    || IsFacilityLaneEdge(edge) && edgePlatforms.Length > 0;
                if (linearizeFacilityLane || compactAdjacentEdges.Contains(edge.TrackEdgeId))
                {
                    var fromX = endpointX[(edge.TrackEdgeId, true)];
                    var toX = endpointX[(edge.TrackEdgeId, false)];
                    x = fromX + (toX - fromX) * ratio;
                    if (edgePlatforms.Length > 0 && edge.LengthMeters > TrackPosition.DefaultToleranceMeters)
                    {
                        var platformStart = edgePlatforms.Min(platform => platform.PlatformStartOffsetMeters) / edge.LengthMeters;
                        var platformEnd = edgePlatforms.Max(platform => platform.PlatformEndOffsetMeters) / edge.LengthMeters;
                        var platformCenter = (platformStart + platformEnd) / 2;
                        var direction = Math.Sign(toX - fromX);
                        if (direction != 0)
                        {
                            var lower = Math.Min(fromX, toX);
                            var upper = Math.Max(fromX, toX);
                            var stationCenter = edgePlatforms
                                .Select(platform => projection.StationCenters.GetValueOrDefault(platform.StationId, double.NaN))
                                .Where(double.IsFinite)
                                .DefaultIfEmpty(double.NaN)
                                .Average();
                            var projectedPlatformCenter = double.IsFinite(stationCenter)
                                ? X(stationCenter)
                                : (ProjectedX(edge, platformStart) + ProjectedX(edge, platformEnd)) / 2;
                            var desiredCenter = Math.Clamp(projectedPlatformCenter, lower, upper);
                            var availableHalf = Math.Min(Math.Abs(desiredCenter - fromX), Math.Abs(toX - desiredCenter)) * .45;
                            var nominalHalf = Math.Abs(toX - fromX) * (platformEnd - platformStart) / 2;
                            var half = widePassingEdges.Contains(edge.TrackEdgeId)
                                ? Math.Min(availableHalf, Math.Max(nominalHalf, 8))
                                : Math.Min(nominalHalf, availableHalf);
                            var platformStartX = desiredCenter - direction * half;
                            var platformEndX = desiredCenter + direction * half;
                            x = MonotoneX(ratio, platformStart, platformCenter, platformEnd,
                                fromX, platformStartX, desiredCenter, platformEndX, toX);
                        }
                    }
                }
                else if (anchoredNodes.Contains(edge.FromNodeId) && ratio < startAnchor)
                    x = endpointX[(edge.TrackEdgeId, true)]
                        + (ProjectedX(edge, startAnchor) - endpointX[(edge.TrackEdgeId, true)]) * Ease(ratio / startAnchor);
                else if (anchoredNodes.Contains(edge.ToNodeId) && ratio > endAnchor)
                    x = ProjectedX(edge, endAnchor)
                        + (endpointX[(edge.TrackEdgeId, false)] - ProjectedX(edge, endAnchor))
                        * Ease((ratio - endAnchor) / (1 - endAnchor));
                return new(x, sourcePoint.Y);
            }
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
            // Side-lane geometry already has its edge-local transition shape
            // (including facility/platform anchors). Re-smoothing it over the
            // whole edge can reintroduce a slope where projected chainage is flat.
            // Other facility geometry still uses the general turnback smoothing.
            if (geometry.PositionAt is not null
                && chainageEdges.TryGetValue(id, out var edge)
                && (edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.PocketTrack or TrackEdgeKind.Siding
                    || compactLaneTransitions && UsesMainlineLaneTransition(edge))) continue;
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

    public static void DrawConnection(Canvas canvas, Point from, Point to, Vector incoming, Vector outgoing, Brush brush, string tooltip,
        bool allowLaneTurn = false)
    {
        // Shared topology-node endpoints are already directly connected. Do not
        // manufacture a connector (or a missing-space warning) from the two edge
        // tangents; the visual regression still checks their actual join angle.
        if ((to - from).Length <= .5) return;
        var reverses = incoming.Length > .0001 && outgoing.Length > .0001
            && (Math.Abs(Vector.AngleBetween(incoming, outgoing)) >= 90
                || (to.X - from.X) * incoming.X < -.001 || (to.X - from.X) * outgoing.X < -.001);
        var nearVertical = Math.Abs(to.X - from.X) < .5 && Math.Abs(to.Y - from.Y) > .5;
        if ((reverses || nearVertical) && allowLaneTurn)
        {
            // A directed connection involving a schematic lane can legitimately
            // change running line at a shared node.  Use a Hermite S/U-curve so
            // the connector leaves and enters each rail tangentially; flagging a
            // vertical or reverse-looking join as a missing-space warning would
            // incorrectly reject a valid passing/crossover layout.
            var handle = Math.Max(24, Math.Min(96, Math.Max((to - from).Length * .55, 24)));
            var incomingUnit = incoming.Length > .0001 ? incoming / incoming.Length : new Vector(1, 0);
            var outgoingUnit = outgoing.Length > .0001 ? outgoing / outgoing.Length : incomingUnit;
            var firstHandle = incomingUnit * handle;
            var lastHandle = outgoingUnit * handle;
            var laneTurnPoints = new PointCollection();
            for (var i = 0; i <= 100; i++)
            {
                var t = i / 100d;
                var t2 = t * t;
                var t3 = t2 * t;
                var h00 = 2 * t3 - 3 * t2 + 1;
                var h10 = t3 - 2 * t2 + t;
                var h01 = -2 * t3 + 3 * t2;
                var h11 = t3 - t2;
                var point = new Point(
                    h00 * from.X + h10 * firstHandle.X + h01 * to.X + h11 * lastHandle.X,
                    h00 * from.Y + h10 * firstHandle.Y + h01 * to.Y + h11 * lastHandle.Y);
                laneTurnPoints.Add(point);
            }
            canvas.Children.Add(new Polyline { Points = laneTurnPoints, Stroke = brush, StrokeThickness = 5,
                StrokeLineJoin = PenLineJoin.Round, ToolTip = tooltip });
            return;
        }
        if (reverses || nearVertical)
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
        var stationIndex = 0;
        foreach (var station in stations)
        {
            var own = bodies.Where(r => ((PlatformBodyAnchor)r.Tag).StationId == station.Id).ToArray();
            if (own.Length == 0) continue;

            // 一座車站只畫一個主標籤。大型越行站的停靠股與通過股在圖上
            // 可能有不同的 X 座標；若各自畫完整站名，會把同一站重複標成兩次。
            // 月臺號碼仍留在每條股道，站名則錨定於全部月臺本體的視覺中心。
            var stationBounds = new Rect(Canvas.GetLeft(own[0]), Canvas.GetTop(own[0]), own[0].Width, own[0].Height);
            foreach (var body in own.Skip(1))
            {
                stationBounds.Union(new Rect(Canvas.GetLeft(body), Canvas.GetTop(body), body.Width, body.Height));
            }

            var center = stationBounds.Left + stationBounds.Width / 2;
            // 在總覽圖中交錯上下放置，可讓相鄰站名保有可讀間距；若真的衝突，
            // 仍沿同側垂直避讓，且不會改寫月臺／軌道位置。
            var above = stationIndex++ % 2 == 0;
            var label = new TextBlock { Text = $"{station.Id}\n{station.Name}", FontSize = 11,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(30, 46, 64)),
                Background = Brushes.White, ToolTip = $"{station.Id} · {station.Name}" };
            PlaceStationLabel(label, station.Id, center, above ? upper : lower, width);
            label.Width = Math.Min(label.Width, 110);
            Canvas.SetLeft(label, center - label.Width / 2);
            label.Tag = new StationLabelAnchor(station.Id, center);
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
                Y2 = above ? stationBounds.Top - 3 : stationBounds.Bottom + 3,
                Stroke = Brushes.SlateGray, StrokeThickness = .7, IsHitTestVisible = false });
            canvas.Children.Add(label);
            canvas.Height = Math.Max(canvas.Height, box.Bottom + 28);
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
            var matchingBounds = matching.Length == 0 ? default : matching.Aggregate((bounds, next) =>
            {
                bounds.Union(next);
                return bounds;
            });
            var expectedCenter = matching.Length > 0 ? matchingBounds.Left + matchingBounds.Width / 2 : anchor.PlatformCenterX;
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
        var details = string.Join(Environment.NewLine,
            warnings.Select((warning, index) => $"{index + 1}. {warning}"));
        var button = new Button
        {
            Content = $"⚠ 版面檢核：{warnings.Count} 項",
            FontSize = 11,
            Foreground = Brushes.DarkOrange,
            Background = Brushes.White,
            BorderBrush = Brushes.DarkOrange,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5, 2, 5, 2),
            ToolTip = $"點擊查看版面檢核詳細訊息\n{details}",
            Tag = warnings.ToArray()
        };
        AutomationProperties.SetName(button, $"版面檢核警告，共 {warnings.Count} 項；點擊查看詳細訊息");
        button.Click += (_, _) => MessageBox.Show(
            Window.GetWindow(button),
            details,
            "版面檢核詳細訊息",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        Canvas.SetRight(button, 12); Canvas.SetTop(button, 12);
        Panel.SetZIndex(button, 100);
        canvas.Children.Add(button);
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
        IEnumerable<TrackEdgeDefinition> edgeSource, double middle, double unit,
        IEnumerable<PlatformDefinitionV4>? platformSource = null,
        IEnumerable<DirectedTrackConnectionDefinition>? connectionSource = null,
        bool compactLaneTransitions = false,
        IEnumerable<PassingFacilityDefinition>? passingFacilitySource = null,
        bool widePassingThroats = false)
    {
        var edges = edgeSource.ToArray();
        var edgeIndex = edges.ToDictionary(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        var connections = connectionSource?.ToArray() ?? [];
        var denseFullLine = edges.Length >= 32;
        bool UsesMainlineLaneTransition(TrackEdgeDefinition edge) =>
            compactLaneTransitions && edge.Kind == TrackEdgeKind.Mainline && edge.SchematicLane.HasValue
            && connections.Any(connection =>
            {
                var otherId = connection.FromTrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                    ? connection.ToTrackEdgeId
                    : connection.ToTrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                        ? connection.FromTrackEdgeId
                        : null;
                return otherId is not null && edgeIndex.TryGetValue(otherId, out var other)
                    && other.Kind == TrackEdgeKind.Mainline
                    && Math.Abs((other.SchematicLane ?? 0) - edge.SchematicLane.Value) > .001;
            });
        var platforms = platformSource?.ToArray() ?? [];
        var verifiedSidings = (passingFacilitySource ?? [])
            .Select(facility => (
                Local: platforms.FirstOrDefault(p => p.PlatformId.Equals(facility.LocalPlatformId, StringComparison.OrdinalIgnoreCase)),
                Through: platforms.FirstOrDefault(p => p.PlatformId.Equals(facility.ExpressPlatformId, StringComparison.OrdinalIgnoreCase))))
            .Where(pair => pair.Local is not null && pair.Through is not null
                && edgeIndex.TryGetValue(pair.Local.TrackEdgeId, out var siding)
                && edgeIndex.TryGetValue(pair.Through.TrackEdgeId, out var through)
                && siding.Kind is TrackEdgeKind.Siding or TrackEdgeKind.PassingTrack
                && through.Kind == TrackEdgeKind.Mainline
                && siding.FromNodeId.Equals(through.FromNodeId, StringComparison.OrdinalIgnoreCase)
                && siding.ToNodeId.Equals(through.ToNodeId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Local!.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var platformRanges = platforms
            .GroupBy(platform => platform.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (
                    Start: group.Min(platform => platform.PlatformStartOffsetMeters),
                    End: group.Max(platform => platform.PlatformEndOffsetMeters)),
                StringComparer.OrdinalIgnoreCase);
        // A full-line overview compresses kilometres into a small number of
        // pixels.  Keep side-lane separation visible, but reduce the vertical
        // excursion before the chainage projection turns a short facility into
        // a steep visual kink.  Platform faces use this same geometry, so their
        // centre alignment remains exact.
        // Dense full-line diagrams need tighter lane separation even when their
        // horizontal width is intentionally preserved with a scroll viewer.
        // Small multi-platform templates keep their full spacing so platform
        // numbers remain individually readable.
        // Keep dense overview sidings visibly distinct from their running line.
        // A ±2 siding at half scale overlaps the ±1 mainline. Give a verified
        // passing-facility siding enough separation from its paired through edge.
        var result = original.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        // Mainline geometry is always normalised, including legacy/topology
        // documents that omit SchematicLane on a through edge.  Facility
        // lanes still require an explicit schematic lane before they are
        // offset from the mainline baseline.
        foreach (var edge in edges)
        {
            if (!original.TryGetValue(edge.TrackEdgeId, out var geometry)) continue;
            if (edge.Kind == TrackEdgeKind.Mainline)
            {
                // A mainline edge is part of the continuous running line even
                // when the topology uses a schematic lane to disambiguate a
                // split/merge.  Never draw that edge as a lane-change curve.
                // Keep the edge's own pre-lane baseline: borrowing an incident
                // siding's Y can move the next through edge onto a different
                // track and create a false vertical break at the station.
                var baselineY = denseFullLine && edge.SchematicLane is { } lane && Math.Abs(lane) > .001
                    ? middle + Math.Sign(lane) * unit
                    : double.IsFinite(geometry.From.Y)
                    ? geometry.From.Y
                    : edge.SchematicLane.HasValue
                        ? middle + edge.SchematicLane.Value * unit
                        : middle;
                result[edge.TrackEdgeId] = new([
                    new Point(geometry.From.X, baselineY),
                    new Point(geometry.To.X, baselineY)
                ]);
                continue;
            }
            if (!edge.SchematicLane.HasValue) continue;
            var compactLaneScale = edges.Length >= 32
                ? verifiedSidings.Contains(edge.TrackEdgeId) ? widePassingThroats ? .9 : .75 : .555
                : compactLaneTransitions ? .5 : 1d;
            var y = middle + edge.SchematicLane!.Value * unit * compactLaneScale;
            var from = new Point(geometry.From.X, y);
            var to = new Point(geometry.To.X, y);
            if (edge.Kind is TrackEdgeKind.PassingTrack or TrackEdgeKind.PocketTrack or TrackEdgeKind.Siding
                || UsesMainlineLaneTransition(edge) && platformRanges.ContainsKey(edge.TrackEdgeId))
            {
                double EndY(string node, double fallback)
                {
                    bool IsConnected(TrackEdgeDefinition candidate) => connections.Any(connection =>
                        connection.FromTrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                            && connection.ToTrackEdgeId.Equals(candidate.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                        || connection.ToTrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                            && connection.FromTrackEdgeId.Equals(candidate.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
                    double EndpointY(TrackEdgeDefinition candidate)
                    {
                        if (!original.TryGetValue(candidate.TrackEdgeId, out var candidateGeometry)) return fallback;
                        return candidate.FromNodeId.Equals(node, StringComparison.OrdinalIgnoreCase)
                            ? candidateGeometry.From.Y : candidateGeometry.To.Y;
                    }
                    var baseline = edges
                        .Where(e => e.Kind == TrackEdgeKind.Mainline && !e.SchematicLane.HasValue
                            && !e.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                            && (e.FromNodeId == node || e.ToNodeId == node))
                        .OrderBy(e => IsConnected(e) ? 0 : 1)
                        .ThenBy(e => e.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (baseline is not null) return EndpointY(baseline);
                    var main = edges.Where(e => e.Kind == TrackEdgeKind.Mainline && e.SchematicLane.HasValue
                        && !e.TrackEdgeId.Equals(edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                        && (e.FromNodeId == node || e.ToNodeId == node))
                        .OrderBy(e => IsConnected(e) ? 0 : 1)
                        .ThenBy(e => Math.Abs(e.SchematicLane!.Value - edge.SchematicLane!.Value)).FirstOrDefault();
                    return main is null ? fallback : EndpointY(main);
                }
                from.Y = EndY(edge.FromNodeId, y);
                to.Y = EndY(edge.ToNodeId, y);
                var laneStart = .25;
                var laneEnd = .75;
                if (platformRanges.TryGetValue(edge.TrackEdgeId, out var platformRange)
                    && edge.LengthMeters > TrackPosition.DefaultToleranceMeters)
                {
                    // Keep the whole visual platform on the side lane, then return
                    // to the adjoining mainline before a projected chainage plateau
                    // can turn the transition into a near-vertical segment.
                    // In a compact full-line overview, a short passing track's
                    // platform may begin near its throat.  Complete the lane
                    // transition before that platform so its face and number do
                    // not collapse onto the adjacent through track.
                    // A short platform can start immediately after a throat.  Do
                    // not let that data collapse the lane transition into a tiny
                    // ratio window in a full-line overview: the resulting
                    // projected polyline becomes an almost vertical visual kink.
                    // The physical platform offset remains unchanged; this is
                    // only the minimum schematic run used to reach the side lane.
                    laneStart = Math.Clamp(platformRange.Start / edge.LengthMeters - .08,
                        compactLaneTransitions ? .25 : .35, .45);
                    laneEnd = Math.Clamp(platformRange.End / edge.LengthMeters + .08, .65, .95);
                    if (laneEnd <= laneStart) laneEnd = Math.Min(.95, laneStart + .1);
                }
                if (denseFullLine && verifiedSidings.Contains(edge.TrackEdgeId) && widePassingThroats
                    && platformRanges.TryGetValue(edge.TrackEdgeId, out var sidingPlatform))
                {
                    laneStart = sidingPlatform.Start / edge.LengthMeters;
                    laneEnd = sidingPlatform.End / edge.LengthMeters;
                }
                else if (denseFullLine)
                {
                    // Full-line views have very short pixel spans at passing
                    // throats.  Spread the lane transition across the whole
                    // edge so endpoint joins stay tangential instead of making
                    // a near-vertical step beside the platform.
                    laneStart = .35;
                    laneEnd = .65;
                }
                Point At(double ratio)
                {
                    var source = geometry.PointAt(ratio);
                    var transitionStart = compactLaneTransitions ? .06 : 0;
                    var transitionEnd = compactLaneTransitions ? .94 : 1;
                    var laneY = ratio <= laneStart
                        ? Interpolate(from.Y, y,
                            SmoothStep((ratio - transitionStart) / Math.Max(.001, laneStart - transitionStart)))
                        : ratio >= laneEnd
                            ? Interpolate(y, to.Y,
                                SmoothStep((ratio - laneEnd) / Math.Max(.001, transitionEnd - laneEnd)))
                            : y;
                    return new(source.X, laneY);
                }

                result[edge.TrackEdgeId] = new(Enumerable.Range(0, 41).Select(index => At(index / 40d)).ToArray())
                {
                    PositionAt = At
                };
            }
            else result[edge.TrackEdgeId] = new([from, to]);
        }
        return result;

        static double Interpolate(double from, double to, double ratio) => from + (to - from) * Math.Clamp(ratio, 0, 1);
        static double SmoothStep(double ratio) =>
            ratio * ratio * ratio * (ratio * (ratio * 6 - 15) + 10);
    }

    public static IReadOnlyDictionary<string, double> DrawPlatforms(Canvas canvas, IEnumerable<PlatformDefinitionV4> platforms,
        IEnumerable<TrackEdgeDefinition> edges,
        IReadOnlyDictionary<string, TopologySchematicEdgeGeometry> geometry, double middle,
        IEnumerable<PassingFacilityDefinition>? passingFacilitySource = null)
    {
        var index = edges.ToDictionary(e => e.TrackEdgeId, StringComparer.OrdinalIgnoreCase);
        var platformItems = platforms.ToArray();
        var platformsById = platformItems.ToDictionary(p => p.PlatformId, StringComparer.OrdinalIgnoreCase);
        var displaySides = new Dictionary<string, PlatformSide>(StringComparer.OrdinalIgnoreCase);
        var throughMarkerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var facility in passingFacilitySource ?? [])
        {
            if (!platformsById.TryGetValue(facility.LocalPlatformId, out var local)
                || !platformsById.TryGetValue(facility.ExpressPlatformId, out var through)
                || !geometry.TryGetValue(local.TrackEdgeId, out var localRail)
                || !geometry.TryGetValue(through.TrackEdgeId, out var throughRail)) continue;
            var localY = localRail.PointAt(.5).Y;
            var throughY = throughRail.PointAt(.5).Y;
            if (Math.Abs(localY - throughY) < .5) continue;
            // Two side platforms sit outside the four tracks; the inner
            // through rails are operational markers, not passenger faces.
            displaySides[local.PlatformId] = localY < throughY ? PlatformSide.Above : PlatformSide.Below;
            displaySides[through.PlatformId] = localY < throughY ? PlatformSide.Above : PlatformSide.Below;
            if (!through.AllowsPassengerService) throughMarkerIds.Add(through.PlatformId);
        }
        var faces = new List<(PlatformDefinitionV4 Platform, Rect Box, Point Rail)>();
        var placedPlatformNumberBounds = new List<Rect>();
        foreach (var p in platformItems)
        {
            if (!index.TryGetValue(p.TrackEdgeId, out var edge) || !geometry.TryGetValue(p.TrackEdgeId, out var rail)) continue;
            var start = rail.PointAt(p.PlatformStartOffsetMeters / edge.LengthMeters);
            var end = rail.PointAt(p.PlatformEndOffsetMeters / edge.LengthMeters);
            var center = rail.PointAt((p.PlatformStartOffsetMeters + p.PlatformEndOffsetMeters) / 2 / edge.LengthMeters);
            var displaySide = displaySides.GetValueOrDefault(p.PlatformId, p.DisplaySide);
            var side = displaySide == PlatformSide.Above ? -1 : displaySide == PlatformSide.Below ? 1 : center.Y <= middle ? 1 : -1;
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
            var markerOnly = group.All(f => throughMarkerIds.Contains(f.Platform.PlatformId));
            var body = new Rectangle { Width = rect.Width, Height = rect.Height,
                Tag = new PlatformBodyAnchor(group.Key.StationId, group.Key.Body),
                Fill = new SolidColorBrush(Color.FromRgb(25, 96, 125)),
                Opacity = markerOnly ? 0 : 1,
                IsHitTestVisible = !markerOnly,
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
                var left = face.Box.Left + face.Box.Width / 2 - label.DesiredSize.Width / 2;
                var top = face.Box.Top;
                var step = label.DesiredSize.Height + 2;
                var direction = face.Box.Top + face.Box.Height / 2 <= middle ? -1 : 1;
                var numberBounds = new Rect(left, top, label.DesiredSize.Width, label.DesiredSize.Height);
                // A dense full-line overview can place adjacent platform centres
                // only a few pixels apart.  Preserve every platform marker, but
                // stagger its small number tag rather than rendering two tags on
                // top of one another.
                for (var attempt = 0; placedPlatformNumberBounds.Any(previous => previous.IntersectsWith(numberBounds)); attempt++)
                {
                    var offset = (attempt / 2 + 1) * step;
                    top = face.Box.Top + (attempt % 2 == 0 ? direction : -direction) * offset;
                    numberBounds.Y = top;
                }
                placedPlatformNumberBounds.Add(numberBounds);
                Canvas.SetLeft(label, left); Canvas.SetTop(label, top); canvas.Children.Add(label);
            }
        }
        return stationBounds.ToDictionary(p => p.Key, p => p.Value.Left + p.Value.Width / 2,
            StringComparer.OrdinalIgnoreCase);
    }
}
