namespace MrtRouteSimulator.Engine;

/// <summary>站場建置不變條件；匯入、編輯套用與 runtime 建立共用，不依賴示意座標。</summary>
internal static class StationConstructionRules
{
    public static void Validate(TopologyInfrastructureDefinition definition,
        IReadOnlyList<ServiceRouteDefinition> routes, ICollection<string> errors)
    {
        foreach (var p in definition.Platforms)
        {
            var span = p.PlatformEndOffsetMeters - p.PlatformStartOffsetMeters;
            // 零範圍保留 legacy 點狀月台相容；具有實體範圍時不得誇大有效長度。
            if (span > 0 && p.EffectiveLengthMeters > span + TrackPosition.DefaultToleranceMeters)
                errors.Add($"[STATION-002] 月台「{p.PlatformId}」有效長度 {p.EffectiveLengthMeters:0.###} m 超過實體範圍 {span:0.###} m。");
        }
        foreach (var group in definition.Platforms
            .Where(p => !string.IsNullOrWhiteSpace(p.PlatformNumber))
            .GroupBy(p => (p.StationId.ToUpperInvariant(), p.PlatformNumber.Trim().ToUpperInvariant())))
            if (group.Count() > 1)
                errors.Add($"[STATION-001] 車站「{group.First().StationId}」月台編號「{group.First().PlatformNumber}」重複，請使用不同編號。");

        foreach (var f in definition.TurnbackFacilities.Where(IsPlatformReversal))
        {
            if (!PlatformTurnback.IsStationary(f))
            {
                errors.Add($"[TURN-001] 站內折返設施「{f.FacilityId}」的到站、折返與出發定位錨點必須為同一軌道及中心 offset；換端車頭由車長解析。");
                continue;
            }
            var platforms = definition.Platforms.Where(p =>
                string.Equals(p.TrackEdgeId, f.ArrivalTrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(p.StopPositionOffsetMeters - f.ArrivalStopOffsetMeters) < 1e-7
                && p.PlatformStartOffsetMeters <= f.ArrivalStopOffsetMeters
                && p.PlatformEndOffsetMeters >= f.ArrivalStopOffsetMeters).ToArray();
            if (platforms.Length == 0)
                errors.Add($"[TURN-002] 站內折返設施「{f.FacilityId}」必須對齊同一月台範圍內的停車點；請同步修改月台與折返 offset。");
            else if (platforms.Any(p => p.StopPositionReference != StopPositionReference.TrainCenter))
                errors.Add($"[TURN-004] 站內原地折返月台必須使用車體中心定位；相同車頭 offset 換端會使車體占用瞬移。");

            foreach (var operation in definition.TurnbackOperations.Where(o =>
                string.Equals(o.FacilityId, f.FacilityId, StringComparison.OrdinalIgnoreCase)))
            {
                var arrival = routes.FirstOrDefault(r => string.Equals(r.ServiceRouteId, operation.ArrivalServiceRouteId, StringComparison.OrdinalIgnoreCase));
                var departure = routes.FirstOrDefault(r => string.Equals(r.ServiceRouteId, operation.DepartureServiceRouteId, StringComparison.OrdinalIgnoreCase));
                // 無 routes 的 graph-only 驗證不檢查營運綁定。
                if (arrival is null || departure is null) continue;
                var last = arrival.Stops.LastOrDefault();
                var first = departure.Stops.FirstOrDefault();
                if (!platforms.Any(p => last is not null && first is not null
                    && string.Equals(last.StationId, p.StationId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(first.StationId, p.StationId, StringComparison.OrdinalIgnoreCase)
                    && last.CandidatePlatformIds.Contains(p.PlatformId, StringComparer.OrdinalIgnoreCase)
                    && first.CandidatePlatformIds.Contains(p.PlatformId, StringComparer.OrdinalIgnoreCase)))
                    errors.Add($"[TURN-003] 折返作業「{operation.OperationId}」的到達末站與出發首站必須包含折返停點所在的同一月台。");
            }
        }
    }

    public static void ValidateBerthing(TopologyProjectDocument document, ResolvedDispatchPlan dispatch)
    {
        var errors = new HashSet<string>();
        foreach (var run in dispatch.Runs)
        {
            var vehicle = document.VehicleTypes.Single(v => v.Id.Equals(run.VehicleTypeId, StringComparison.OrdinalIgnoreCase));
            var routeId = document.DirectionRouteBindings.Single(b => b.Direction == run.Direction).ServiceRouteId;
            var route = document.ServiceRoutes.Single(r => r.ServiceRouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase));
            var pattern = document.StopPatterns.Single(p => p.Id.Equals(run.StopPatternId, StringComparison.OrdinalIgnoreCase));
            foreach (var stop in route.Stops)
            {
                if (pattern.Instructions.Any(i => i.StationId.Equals(stop.StationId, StringComparison.OrdinalIgnoreCase)
                    && i.Action == StopPatternAction.Pass)) continue;
                foreach (var id in stop.CandidatePlatformIds)
                {
                    var p = document.Topology.Platforms.Single(p => p.PlatformId.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (p.PlatformEndOffsetMeters <= p.PlatformStartOffsetMeters) continue;
                    foreach (var traversal in route.Traversals.Where(t => t.TrackEdgeId.Equals(p.TrackEdgeId, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Cursor 是車頭，停點後方必須容納整列車，不是僅比較月台總長。
                        var head = PlatformStopPositionResolver.ResolveHeadPosition(p, traversal.Direction, vehicle.LengthMeters).OffsetMeters;
                        var available = traversal.Direction == TraversalDirection.Forward
                            ? head - p.PlatformStartOffsetMeters
                            : p.PlatformEndOffsetMeters - head;
                        if (available + TrackPosition.DefaultToleranceMeters < vehicle.LengthMeters
                            || head < p.PlatformStartOffsetMeters - TrackPosition.DefaultToleranceMeters
                            || head > p.PlatformEndOffsetMeters + TrackPosition.DefaultToleranceMeters
                            || p.EffectiveLengthMeters + TrackPosition.DefaultToleranceMeters < vehicle.LengthMeters)
                            errors.Add($"[STATION-003] 月台「{id}」在營運路線「{routeId}」的停點後方僅 {available:0.###} m，無法容納車型「{vehicle.Id}」的 {vehicle.LengthMeters:0.###} m 車長；請調整停點或月台範圍。");
                    }
                }
            }
            foreach (var operation in document.Topology.TurnbackOperations.Where(o => o.ArrivalServiceRouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase)))
            {
                var facility = document.Topology.TurnbackFacilities.Single(f => f.FacilityId == operation.FacilityId);
                if (PlatformTurnback.IsStationary(facility)) continue;
                var index = facility.TurnbackStopPosition is { } position
                    ? facility.Traversals.ToList().FindIndex(t => t.TrackEdgeId == position.TrackEdgeId)
                    : facility.TurnbackStopAfterTraversalIndex ?? -1;
                if (index < 0) continue; // 基礎 topology 驗證負責失效參照。
                var stopTraversal = facility.Traversals[index];
                var stopEdge = document.Topology.Edges.Single(e => e.TrackEdgeId == stopTraversal.TrackEdgeId);
                var turnStop = facility.TurnbackStopPosition ?? new TrackPosition(stopEdge.TrackEdgeId,
                    stopTraversal.Direction == TraversalDirection.Forward ? stopEdge.LengthMeters : 0);
                var traversals = facility.Traversals;
                var capacity = 0d;
                for (int incoming = index, returning = index + 1; incoming >= 0 && returning < traversals.Count; incoming--, returning++)
                {
                    var a = traversals[incoming]; var b = traversals[returning];
                    if (a.TrackEdgeId != b.TrackEdgeId || a.Direction == b.Direction) break;
                    var edge = document.Topology.Edges.Single(e => e.TrackEdgeId == a.TrackEdgeId);
                    capacity += incoming == index
                        ? a.Direction == TraversalDirection.Forward ? turnStop.OffsetMeters : edge.LengthMeters - turnStop.OffsetMeters
                        : edge.LengthMeters;
                }
                if (capacity + TrackPosition.DefaultToleranceMeters < vehicle.LengthMeters)
                    errors.Add($"[TURN-005] 折返設施「{facility.FacilityId}」在停點前僅 {capacity:0.###} m 軌道具有反向返回進路，無法容納車型「{vehicle.Id}」{vehicle.LengthMeters:0.###} m 全車換端；請延長尾軌或調整折返停點。");
            }
        }
        RouteValidator.ThrowIfAny(errors);
    }

    private static bool IsPlatformReversal(TurnbackFacilityDefinition f) =>
        f.Kind == TurnbackFacilityKind.Crossover && f.Traversals.Count == 2
        && string.Equals(f.Traversals[0].TrackEdgeId, f.Traversals[1].TrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && f.Traversals[0].Direction != f.Traversals[1].Direction;
}
