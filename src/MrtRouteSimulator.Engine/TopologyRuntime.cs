using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MrtRouteSimulator.Engine;

/// <summary>列車車頭在已解析 ServiceRoute 上的權威 runtime cursor。</summary>
public readonly record struct TopologyTraversalCursor(
    string ServiceRouteId,
    int TraversalIndex,
    TrackPosition Position);

/// <summary>一個 runtime movement plan 中的路段用途；用途不取代列車的實體 cursor。</summary>
public enum MovementLegKind
{
    ServiceRoute,
    PlatformApproach,
    Passing,
    Overtake,
    Crossover,
    PocketTrack,
    TailTrack,
    Turnback,
    Depot,
    Other
}

/// <summary>已解析、可直接執行的 edge traversal。Edge 物件避免每個 tick 重新以字串查詢。</summary>
public sealed record ResolvedTraversal(TrackEdgeDefinition Edge, TraversalDirection Direction)
{
    public double LengthMeters => Edge.LengthMeters;
}

/// <summary>一段連續 movement 的有序 traversal 及其保護資源。</summary>
public sealed record ResolvedMovementLeg
{
    public required string LegId { get; init; }
    public MovementLegKind Kind { get; init; }
    public IReadOnlyList<ResolvedTraversal> Traversals { get; init; } = [];
    public IReadOnlySet<string> RequiredResourceIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>列車在一個 service run 或 facility operation 中可走的完整有序實體路徑。</summary>
public sealed record ResolvedMovementPlan
{
    public required string MovementPlanId { get; init; }
    public IReadOnlyList<ResolvedMovementLeg> Legs { get; init; } = [];
}

/// <summary>
/// V2 runtime 的權威列車位置。Movement leg／traversal 只用於路徑順序，實際座標永遠是
/// edge-local <see cref="TrackPosition"/>；ProjectedChainage 不在此型別中。
/// </summary>
public readonly record struct RuntimeTopologyCursor(
    string MovementPlanId,
    int MovementLegIndex,
    int TraversalIndex,
    TrackPosition Position,
    TraversalDirection Direction);

/// <summary>由新 cursor 計算出的車頭、車尾與跨 edge occupancy；可供資源與安全共用。</summary>
public sealed record TopologyMovementFootprint(
    RuntimeTopologyCursor Front,
    RuntimeTopologyCursor Rear,
    IReadOnlyList<TrackOccupancyInterval> OccupiedIntervals);

/// <summary>
/// 所有 mainline、crossover、tail、pocket、passing 與 turnback 共用的 traversal engine。
/// 它不使用 route chainage，也不會在 leg/edge 邊界丟失 distance。
/// </summary>
public sealed class TopologyMovementNavigator
{
    private readonly InfrastructureGraphV4 infrastructure;
    private readonly ResolvedMovementPlan plan;

    public TopologyMovementNavigator(InfrastructureGraphV4 infrastructure, ResolvedMovementPlan plan)
    {
        this.infrastructure = infrastructure ?? throw new ArgumentNullException(nameof(infrastructure));
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        ValidatePlan();
    }

    public string MovementPlanId => plan.MovementPlanId;
    public IReadOnlyList<ResolvedMovementLeg> Legs => plan.Legs;

    public RuntimeTopologyCursor CreateStartCursor() => CreateCursor(0, 0, 0);

    public RuntimeTopologyCursor CreateEndCursor()
    {
        var legIndex = plan.Legs.Count - 1;
        var traversalIndex = plan.Legs[legIndex].Traversals.Count - 1;
        return CreateCursor(legIndex, traversalIndex, GetTraversal(legIndex, traversalIndex).LengthMeters);
    }

    public RuntimeTopologyCursor CreateCursor(int legIndex, int traversalIndex, double distanceAlongTraversalMeters)
    {
        var traversal = GetTraversal(legIndex, traversalIndex);
        if (!double.IsFinite(distanceAlongTraversalMeters)
            || distanceAlongTraversalMeters < -TrackPosition.DefaultToleranceMeters
            || distanceAlongTraversalMeters > traversal.LengthMeters + TrackPosition.DefaultToleranceMeters)
        {
            throw new SimulationValidationException([
                $"movement plan「{plan.MovementPlanId}」的 leg {legIndex} traversal {traversalIndex} 局部距離超出 edge「{traversal.Edge.TrackEdgeId}」範圍。"
            ]);
        }

        var distance = Math.Clamp(distanceAlongTraversalMeters, 0, traversal.LengthMeters);
        var offset = traversal.Direction == TraversalDirection.Forward
            ? distance
            : traversal.LengthMeters - distance;
        return new RuntimeTopologyCursor(
            plan.MovementPlanId,
            legIndex,
            traversalIndex,
            new TrackPosition(traversal.Edge.TrackEdgeId, offset),
            traversal.Direction);
    }

    public double GetDistanceAlongTraversal(RuntimeTopologyCursor cursor)
    {
        ValidateCursor(cursor);
        var traversal = GetTraversal(cursor.MovementLegIndex, cursor.TraversalIndex);
        return traversal.Direction == TraversalDirection.Forward
            ? cursor.Position.OffsetMeters
            : traversal.LengthMeters - cursor.Position.OffsetMeters;
    }

    /// <summary>由 movement plan 起點到 cursor 的累計實體距離；僅供輸出投影，非位置權威。</summary>
    public double GetDistanceFromStart(RuntimeTopologyCursor cursor)
    {
        ValidateCursor(cursor);
        var result = 0d;
        for (var legIndex = 0; legIndex <= cursor.MovementLegIndex; legIndex++)
        {
            var finalTraversal = legIndex == cursor.MovementLegIndex
                ? cursor.TraversalIndex
                : plan.Legs[legIndex].Traversals.Count;
            for (var traversalIndex = 0; traversalIndex < finalTraversal; traversalIndex++)
            {
                result += GetTraversal(legIndex, traversalIndex).LengthMeters;
            }
        }

        return result + GetDistanceAlongTraversal(cursor);
    }

    public RuntimeTopologyCursor Advance(RuntimeTopologyCursor cursor, double distanceMeters)
    {
        ValidateCursor(cursor);
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0) throw new ArgumentOutOfRangeException(nameof(distanceMeters));

        var legIndex = cursor.MovementLegIndex;
        var traversalIndex = cursor.TraversalIndex;
        var distanceAlong = GetDistanceAlongTraversal(cursor);
        var remaining = distanceMeters;
        while (remaining > TrackPosition.DefaultToleranceMeters)
        {
            var traversal = GetTraversal(legIndex, traversalIndex);
            var available = traversal.LengthMeters - distanceAlong;
            if (remaining < available - TrackPosition.DefaultToleranceMeters)
                return CreateCursor(legIndex, traversalIndex, distanceAlong + remaining);

            if (!TryMoveToNextTraversal(legIndex, traversalIndex, out var nextLeg, out var nextTraversal))
                return CreateCursor(legIndex, traversalIndex, traversal.LengthMeters);

            remaining -= Math.Max(available, 0);
            legIndex = nextLeg;
            traversalIndex = nextTraversal;
            distanceAlong = 0;
        }

        return CreateCursor(legIndex, traversalIndex, distanceAlong);
    }

    public RuntimeTopologyCursor Retreat(RuntimeTopologyCursor cursor, double distanceMeters)
    {
        ValidateCursor(cursor);
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0) throw new ArgumentOutOfRangeException(nameof(distanceMeters));

        var legIndex = cursor.MovementLegIndex;
        var traversalIndex = cursor.TraversalIndex;
        var distanceAlong = GetDistanceAlongTraversal(cursor);
        var remaining = distanceMeters;
        while (remaining > TrackPosition.DefaultToleranceMeters)
        {
            if (remaining < distanceAlong - TrackPosition.DefaultToleranceMeters)
                return CreateCursor(legIndex, traversalIndex, distanceAlong - remaining);

            if (!TryMoveToPreviousTraversal(legIndex, traversalIndex, out var previousLeg, out var previousTraversal))
                return CreateCursor(legIndex, traversalIndex, 0);

            remaining -= Math.Max(distanceAlong, 0);
            legIndex = previousLeg;
            traversalIndex = previousTraversal;
            distanceAlong = GetTraversal(legIndex, traversalIndex).LengthMeters;
        }

        return CreateCursor(legIndex, traversalIndex, distanceAlong);
    }

    public double? TryGetForwardDistance(RuntimeTopologyCursor from, RuntimeTopologyCursor target)
    {
        ValidateCursor(from);
        ValidateCursor(target);
        if (Compare(target, from) < 0) return null;

        if (from.MovementLegIndex == target.MovementLegIndex && from.TraversalIndex == target.TraversalIndex)
        {
            var difference = GetDistanceAlongTraversal(target) - GetDistanceAlongTraversal(from);
            return difference < -TrackPosition.DefaultToleranceMeters ? null : Math.Max(0, difference);
        }

        var result = GetTraversal(from.MovementLegIndex, from.TraversalIndex).LengthMeters - GetDistanceAlongTraversal(from);
        var legIndex = from.MovementLegIndex;
        var traversalIndex = from.TraversalIndex;
        while (TryMoveToNextTraversal(legIndex, traversalIndex, out var nextLeg, out var nextTraversal))
        {
            legIndex = nextLeg;
            traversalIndex = nextTraversal;
            if (legIndex == target.MovementLegIndex && traversalIndex == target.TraversalIndex)
                return result + GetDistanceAlongTraversal(target);
            result += GetTraversal(legIndex, traversalIndex).LengthMeters;
        }
        return null;
    }

    public TopologyMovementFootprint CreateFootprint(RuntimeTopologyCursor front, double trainLengthMeters)
    {
        if (!double.IsFinite(trainLengthMeters) || trainLengthMeters <= 0) throw new ArgumentOutOfRangeException(nameof(trainLengthMeters));
        ValidateCursor(front);
        var rear = Retreat(front, trainLengthMeters);
        var intervals = new List<TrackOccupancyInterval>();
        var legIndex = rear.MovementLegIndex;
        var traversalIndex = rear.TraversalIndex;
        while (true)
        {
            var startDistance = legIndex == rear.MovementLegIndex && traversalIndex == rear.TraversalIndex
                ? GetDistanceAlongTraversal(rear)
                : 0;
            var endDistance = legIndex == front.MovementLegIndex && traversalIndex == front.TraversalIndex
                ? GetDistanceAlongTraversal(front)
                : GetTraversal(legIndex, traversalIndex).LengthMeters;
            var start = CreateCursor(legIndex, traversalIndex, startDistance);
            var end = CreateCursor(legIndex, traversalIndex, endDistance);
            intervals.Add(new TrackOccupancyInterval(
                start.Position.TrackEdgeId,
                Math.Min(start.Position.OffsetMeters, end.Position.OffsetMeters),
                Math.Max(start.Position.OffsetMeters, end.Position.OffsetMeters)));
            if (legIndex == front.MovementLegIndex && traversalIndex == front.TraversalIndex) break;
            if (!TryMoveToNextTraversal(legIndex, traversalIndex, out legIndex, out traversalIndex))
                throw new SimulationValidationException(["列車 footprint 超出 movement plan 終點。"]);
        }

        return new TopologyMovementFootprint(front, rear, intervals);
    }

    public ResolvedTraversal GetTraversal(int legIndex, int traversalIndex)
    {
        if (legIndex < 0 || legIndex >= plan.Legs.Count
            || traversalIndex < 0 || traversalIndex >= plan.Legs[legIndex].Traversals.Count)
        {
            throw new ArgumentOutOfRangeException($"{nameof(legIndex)}/{nameof(traversalIndex)}");
        }
        return plan.Legs[legIndex].Traversals[traversalIndex];
    }

    private bool TryMoveToNextTraversal(int legIndex, int traversalIndex, out int nextLeg, out int nextTraversal)
    {
        if (traversalIndex + 1 < plan.Legs[legIndex].Traversals.Count)
        {
            nextLeg = legIndex;
            nextTraversal = traversalIndex + 1;
            return true;
        }
        if (legIndex + 1 < plan.Legs.Count)
        {
            nextLeg = legIndex + 1;
            nextTraversal = 0;
            return true;
        }
        nextLeg = default;
        nextTraversal = default;
        return false;
    }

    private bool TryMoveToPreviousTraversal(int legIndex, int traversalIndex, out int previousLeg, out int previousTraversal)
    {
        if (traversalIndex > 0)
        {
            previousLeg = legIndex;
            previousTraversal = traversalIndex - 1;
            return true;
        }
        if (legIndex > 0)
        {
            previousLeg = legIndex - 1;
            previousTraversal = plan.Legs[previousLeg].Traversals.Count - 1;
            return true;
        }
        previousLeg = default;
        previousTraversal = default;
        return false;
    }

    private int Compare(RuntimeTopologyCursor left, RuntimeTopologyCursor right)
    {
        var legComparison = left.MovementLegIndex.CompareTo(right.MovementLegIndex);
        return legComparison != 0 ? legComparison : left.TraversalIndex.CompareTo(right.TraversalIndex);
    }

    private void ValidateCursor(RuntimeTopologyCursor cursor)
    {
        if (!cursor.MovementPlanId.Equals(plan.MovementPlanId, StringComparison.OrdinalIgnoreCase))
            throw new SimulationValidationException([$"cursor 的 movement plan「{cursor.MovementPlanId}」不屬於「{plan.MovementPlanId}」。"]);
        var traversal = GetTraversal(cursor.MovementLegIndex, cursor.TraversalIndex);
        if (cursor.Direction != traversal.Direction
            || !cursor.Position.TrackEdgeId.Equals(traversal.Edge.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            || !infrastructure.ContainsPosition(cursor.Position))
        {
            throw new SimulationValidationException([$"cursor「{cursor.Position}」不符合 movement plan「{plan.MovementPlanId}」。"]);
        }
    }

    private void ValidatePlan()
    {
        if (string.IsNullOrWhiteSpace(plan.MovementPlanId) || plan.Legs.Count == 0)
            throw new SimulationValidationException(["movement plan 必須有編號與至少一個 movement leg。"]);
        TrackEdgeDefinition? previousEdge = null;
        TraversalDirection previousDirection = default;
        for (var legIndex = 0; legIndex < plan.Legs.Count; legIndex++)
        {
            var leg = plan.Legs[legIndex];
            if (string.IsNullOrWhiteSpace(leg.LegId) || leg.Traversals.Count == 0)
                throw new SimulationValidationException([$"movement plan「{plan.MovementPlanId}」第 {legIndex + 1} 個 leg 必須有編號與 traversal。"]);
            foreach (var traversal in leg.Traversals)
            {
                if (!infrastructure.Edges.TryGetValue(traversal.Edge.TrackEdgeId, out var indexed)
                    || !ReferenceEquals(indexed, traversal.Edge)
                    || !InfrastructureValidator.AllowsTraversal(traversal.Edge, traversal.Direction))
                {
                    throw new SimulationValidationException([$"movement plan「{plan.MovementPlanId}」引用無效 traversal「{traversal.Edge.TrackEdgeId}」。"]);
                }
                if (previousEdge is not null
                    && !InfrastructureValidator.GetEndNode(previousEdge, previousDirection)
                        .Equals(InfrastructureValidator.GetStartNode(traversal.Edge, traversal.Direction), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SimulationValidationException([
                        $"movement plan「{plan.MovementPlanId}」在 {previousEdge.TrackEdgeId} 與 {traversal.Edge.TrackEdgeId} 間不連續，不能 runtime teleport。"
                    ]);
                }
                if (previousEdge is not null
                    && !infrastructure.AllowsTransition(
                        new DirectedTrackTraversal(previousEdge.TrackEdgeId, previousDirection),
                        new DirectedTrackTraversal(traversal.Edge.TrackEdgeId, traversal.Direction)))
                {
                    throw new SimulationValidationException([
                        $"movement plan「{plan.MovementPlanId}」在 {previousEdge.TrackEdgeId} 與 {traversal.Edge.TrackEdgeId} 間違反道岔有向轉向限制。"
                    ]);
                }
                previousEdge = traversal.Edge;
                previousDirection = traversal.Direction;
            }
        }
    }
}

/// <summary>將 ServiceRoute、facility 定義解析成共用 runtime movement plan。</summary>
public static class TopologyMovementPlanResolver
{
    public static ResolvedMovementPlan ResolveServiceRoute(
        InfrastructureGraphV4 infrastructure,
        ServiceRouteDefinition serviceRoute)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(serviceRoute);
        InfrastructureValidator.ValidateAndThrow(infrastructure, [serviceRoute]);
        return new ResolvedMovementPlan
        {
            MovementPlanId = serviceRoute.ServiceRouteId,
            Legs =
            [
                new ResolvedMovementLeg
                {
                    LegId = $"{serviceRoute.ServiceRouteId}:MAIN",
                    Kind = MovementLegKind.ServiceRoute,
                    Traversals = ResolveTraversals(infrastructure, serviceRoute.Traversals)
                }
            ]
        };
    }

    public static ResolvedMovementLeg ResolveFacilityLeg(
        InfrastructureGraphV4 infrastructure,
        TurnbackFacilityDefinition facility)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(facility);
        if (facility.Traversals.Count == 0)
        {
            throw new SimulationValidationException([
                $"折返設施「{facility.FacilityId}」尚未定義有序 Traversals；不能以 FacilityTrackEdgeIds 或 virtual position 執行。"
            ]);
        }
        return new ResolvedMovementLeg
        {
            LegId = facility.FacilityId,
            Kind = facility.Kind switch
            {
                TurnbackFacilityKind.TailTrack => MovementLegKind.TailTrack,
                TurnbackFacilityKind.PocketTrack => MovementLegKind.PocketTrack,
                TurnbackFacilityKind.Crossover => MovementLegKind.Crossover,
                TurnbackFacilityKind.Siding => MovementLegKind.Passing,
                _ => MovementLegKind.Turnback
            },
            Traversals = ResolveTraversals(infrastructure, facility.Traversals),
            RequiredResourceIds = facility.ConflictResourceIds
        };
    }

    public static ResolvedMovementLeg ResolvePassingLeg(
        InfrastructureGraphV4 infrastructure,
        PassingFacilityDefinition facility)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(facility);
        if (facility.Traversals.Count == 0)
        {
            throw new SimulationValidationException([
                $"越行設施「{facility.FacilityId}」尚未定義有序 Traversals；不能以 virtual track 或 global position 執行。"
            ]);
        }

        return new ResolvedMovementLeg
        {
            LegId = facility.FacilityId,
            Kind = MovementLegKind.Passing,
            Traversals = ResolveTraversals(infrastructure, facility.Traversals),
            RequiredResourceIds = facility.ConflictResourceIds
        };
    }

    public static IReadOnlyList<ResolvedTraversal> ResolveTraversals(
        InfrastructureGraphV4 infrastructure,
        IEnumerable<DirectedTrackTraversal> traversals) =>
        traversals.Select(traversal => new ResolvedTraversal(
            infrastructure.GetRequiredEdge(traversal.TrackEdgeId), traversal.Direction)).ToArray();
}

/// <summary>V2 station order 的 topology-native 執行內容；Route.Stations 不再是其來源。</summary>
public sealed record ResolvedRunRouteContext
{
    public required string ServiceRouteId { get; init; }
    public IReadOnlyList<ResolvedStop> Stops { get; init; } = [];
    public required ResolvedMovementPlan MainMovementPlan { get; init; }
    public int OriginStopIndex { get; init; }
    public int TerminalStopIndex { get; init; }

    public static ResolvedRunRouteContext Create(InfrastructureGraphV4 infrastructure, ServiceRouteDefinition serviceRoute)
    {
        var stops = ResolvedStopResolver.Resolve(infrastructure, serviceRoute);
        return new ResolvedRunRouteContext
        {
            ServiceRouteId = serviceRoute.ServiceRouteId,
            Stops = stops,
            MainMovementPlan = TopologyMovementPlanResolver.ResolveServiceRoute(infrastructure, serviceRoute),
            OriginStopIndex = 0,
            TerminalStopIndex = stops.Count - 1
        };
    }
}

/// <summary>由 physical traversal 推導 required resources；resource 不再由 virtual track state 手工補入。</summary>
public static class TraversalResourceResolver
{
    public static IReadOnlySet<string> GetRequiredResources(ResolvedMovementLeg leg)
    {
        ArgumentNullException.ThrowIfNull(leg);
        return leg.RequiredResourceIds
            .Concat(leg.Traversals.SelectMany(traversal => traversal.Edge.ConflictResourceIds))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>受保護資源僅在所有列車 footprint 都已 rear-clear 相連 edge 後才可釋放。</summary>
public static class TopologyResourceReleasePolicy
{
    public static bool CanReleaseResource(
        string resourceId,
        IEnumerable<TopologyMovementFootprint> activeFootprints,
        InfrastructureGraphV4 infrastructure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(activeFootprints);
        ArgumentNullException.ThrowIfNull(infrastructure);
        var protectedEdges = infrastructure.Edges.Values
            .Where(edge => edge.ConflictResourceIds.Contains(resourceId))
            .Select(edge => edge.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return activeFootprints.All(footprint => footprint.OccupiedIntervals.All(interval =>
            !protectedEdges.Contains(interval.TrackEdgeId)));
    }
}

/// <summary>單一 edge 上由列車 footprint 占用的 closed interval。</summary>
public readonly record struct TrackOccupancyInterval(
    string TrackEdgeId,
    double StartOffsetMeters,
    double EndOffsetMeters)
{
    public bool Overlaps(TrackOccupancyInterval other, double toleranceMeters = TrackPosition.DefaultToleranceMeters) =>
        TrackEdgeId.Equals(other.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && StartOffsetMeters <= other.EndOffsetMeters + toleranceMeters
        && other.StartOffsetMeters <= EndOffsetMeters + toleranceMeters;
}

/// <summary>列車車頭、車尾與跨 edge 區間的 topology-native footprint。</summary>
public sealed record TrainFootprint(
    TopologyTraversalCursor Front,
    TopologyTraversalCursor Rear,
    IReadOnlyList<TrackOccupancyInterval> OccupiedIntervals);

/// <summary>依有序 ServiceRoute 推進／回退 TrackEdge + Offset cursor，不依賴 global chainage。</summary>
public sealed class TopologyRouteNavigator
{
    private readonly InfrastructureGraphV4 infrastructure;
    private readonly ServiceRouteDefinition serviceRoute;

    public TopologyRouteNavigator(InfrastructureGraphV4 infrastructure, ServiceRouteDefinition serviceRoute)
    {
        this.infrastructure = infrastructure ?? throw new ArgumentNullException(nameof(infrastructure));
        this.serviceRoute = serviceRoute ?? throw new ArgumentNullException(nameof(serviceRoute));
        InfrastructureValidator.ValidateAndThrow(infrastructure, [serviceRoute]);
    }

    public string ServiceRouteId => serviceRoute.ServiceRouteId;
    public IReadOnlyList<DirectedTrackTraversal> Traversals => serviceRoute.Traversals;

    public TopologyTraversalCursor CreateStartCursor() => CreateCursor(0, 0);

    public TopologyTraversalCursor CreateEndCursor() => CreateCursor(serviceRoute.Traversals.Count - 1, GetTraversalLength(serviceRoute.Traversals.Count - 1));

    public TopologyTraversalCursor CreateCursor(int traversalIndex, double distanceAlongTraversalMeters)
    {
        var traversal = GetTraversal(traversalIndex);
        var edge = infrastructure.GetRequiredEdge(traversal.TrackEdgeId);
        if (!double.IsFinite(distanceAlongTraversalMeters)
            || distanceAlongTraversalMeters < -TrackPosition.DefaultToleranceMeters
            || distanceAlongTraversalMeters > edge.LengthMeters + TrackPosition.DefaultToleranceMeters)
        {
            throw new SimulationValidationException([$"traversal {traversalIndex} 的局部距離超出 edge「{edge.TrackEdgeId}」範圍。"]);
        }

        var distance = Math.Clamp(distanceAlongTraversalMeters, 0, edge.LengthMeters);
        var offset = traversal.Direction == TraversalDirection.Forward
            ? distance
            : edge.LengthMeters - distance;
        return new TopologyTraversalCursor(serviceRoute.ServiceRouteId, traversalIndex, new TrackPosition(edge.TrackEdgeId, offset));
    }

    public double GetDistanceAlongTraversal(TopologyTraversalCursor cursor)
    {
        ValidateCursor(cursor);
        var traversal = GetTraversal(cursor.TraversalIndex);
        var edge = infrastructure.GetRequiredEdge(traversal.TrackEdgeId);
        return traversal.Direction == TraversalDirection.Forward
            ? cursor.Position.OffsetMeters
            : edge.LengthMeters - cursor.Position.OffsetMeters;
    }

    public TopologyTraversalCursor Advance(TopologyTraversalCursor cursor, double distanceMeters)
    {
        ValidateCursor(cursor);
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));

        var index = cursor.TraversalIndex;
        var distanceAlong = GetDistanceAlongTraversal(cursor);
        var remaining = distanceMeters;
        while (remaining > TrackPosition.DefaultToleranceMeters)
        {
            var edgeLength = GetTraversalLength(index);
            var available = edgeLength - distanceAlong;
            if (remaining < available - TrackPosition.DefaultToleranceMeters)
                return CreateCursor(index, distanceAlong + remaining);

            if (index == serviceRoute.Traversals.Count - 1)
                return CreateCursor(index, edgeLength);

            remaining -= Math.Max(available, 0);
            index++;
            distanceAlong = 0;
        }

        return CreateCursor(index, distanceAlong);
    }

    public TopologyTraversalCursor Retreat(TopologyTraversalCursor cursor, double distanceMeters)
    {
        ValidateCursor(cursor);
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));

        var index = cursor.TraversalIndex;
        var distanceAlong = GetDistanceAlongTraversal(cursor);
        var remaining = distanceMeters;
        while (remaining > TrackPosition.DefaultToleranceMeters)
        {
            if (remaining < distanceAlong - TrackPosition.DefaultToleranceMeters)
                return CreateCursor(index, distanceAlong - remaining);

            if (index == 0)
                return CreateCursor(0, 0);

            remaining -= Math.Max(distanceAlong, 0);
            index--;
            distanceAlong = GetTraversalLength(index);
        }

        return CreateCursor(index, distanceAlong);
    }

    /// <summary>同一 ServiceRoute 上由 from 朝 target 前進的距離；target 在後方時回傳 null。</summary>
    public double? TryGetForwardDistance(TopologyTraversalCursor from, TopologyTraversalCursor target)
    {
        ValidateCursor(from);
        ValidateCursor(target);
        if (target.TraversalIndex < from.TraversalIndex) return null;
        var fromDistance = GetDistanceAlongTraversal(from);
        var targetDistance = GetDistanceAlongTraversal(target);
        if (target.TraversalIndex == from.TraversalIndex)
            return targetDistance + TrackPosition.DefaultToleranceMeters < fromDistance
                ? null
                : Math.Max(0, targetDistance - fromDistance);

        var result = GetTraversalLength(from.TraversalIndex) - fromDistance + targetDistance;
        for (var index = from.TraversalIndex + 1; index < target.TraversalIndex; index++)
            result += GetTraversalLength(index);
        return result;
    }

    public TrainFootprint CreateFootprint(TopologyTraversalCursor front, double trainLengthMeters)
    {
        if (!double.IsFinite(trainLengthMeters) || trainLengthMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(trainLengthMeters));
        ValidateCursor(front);
        var rear = Retreat(front, trainLengthMeters);
        var intervals = new List<TrackOccupancyInterval>();
        for (var index = rear.TraversalIndex; index <= front.TraversalIndex; index++)
        {
            var startDistance = index == rear.TraversalIndex ? GetDistanceAlongTraversal(rear) : 0;
            var endDistance = index == front.TraversalIndex ? GetDistanceAlongTraversal(front) : GetTraversalLength(index);
            var startCursor = CreateCursor(index, startDistance);
            var endCursor = CreateCursor(index, endDistance);
            intervals.Add(new TrackOccupancyInterval(
                startCursor.Position.TrackEdgeId,
                Math.Min(startCursor.Position.OffsetMeters, endCursor.Position.OffsetMeters),
                Math.Max(startCursor.Position.OffsetMeters, endCursor.Position.OffsetMeters)));
        }

        return new TrainFootprint(front, rear, intervals);
    }

    public DirectedTrackTraversal GetTraversal(int traversalIndex)
    {
        if (traversalIndex < 0 || traversalIndex >= serviceRoute.Traversals.Count)
            throw new ArgumentOutOfRangeException(nameof(traversalIndex));
        return serviceRoute.Traversals[traversalIndex];
    }

    private double GetTraversalLength(int traversalIndex) =>
        infrastructure.GetRequiredEdge(GetTraversal(traversalIndex).TrackEdgeId).LengthMeters;

    private void ValidateCursor(TopologyTraversalCursor cursor)
    {
        if (!cursor.ServiceRouteId.Equals(serviceRoute.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
            throw new SimulationValidationException([$"cursor 的 ServiceRoute「{cursor.ServiceRouteId}」不屬於 navigator「{serviceRoute.ServiceRouteId}」。"]);
        var traversal = GetTraversal(cursor.TraversalIndex);
        if (!traversal.TrackEdgeId.Equals(cursor.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            || !infrastructure.ContainsPosition(cursor.Position))
        {
            throw new SimulationValidationException([$"cursor「{cursor.Position}」不符合 ServiceRoute traversal {cursor.TraversalIndex}。"]);
        }
    }
}

/// <summary>以車輛為鍵的 edge-interval occupancy index。</summary>
public sealed class TrackOccupancyIndex
{
    private readonly Dictionary<string, TrainFootprint> footprintsByOwner = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, TrainFootprint> FootprintsByOwner => footprintsByOwner;

    public bool TryUpdate(string ownerId, TrainFootprint footprint, out string? blockingOwnerId)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("occupancy owner 不可空白。", nameof(ownerId));
        foreach (var existing in footprintsByOwner)
        {
            if (existing.Key.Equals(ownerId, StringComparison.OrdinalIgnoreCase)) continue;
            if (Overlaps(existing.Value, footprint))
            {
                blockingOwnerId = existing.Key;
                return false;
            }
        }

        footprintsByOwner[ownerId] = footprint;
        blockingOwnerId = null;
        return true;
    }

    public bool Remove(string ownerId) => footprintsByOwner.Remove(ownerId);

    /// <summary>
    /// 將目前 footprint 寫入實際占用圖，並回傳所有重疊車輛。不同於 <see cref="TryUpdate"/>，
    /// 此方法保留衝突中的 footprint，讓 runtime 能用它偵測碰撞而不是把衝突悄悄丟失。
    /// </summary>
    public IReadOnlyList<string> Set(string ownerId, TrainFootprint footprint)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("occupancy owner 不可空白。", nameof(ownerId));
        footprintsByOwner.Remove(ownerId);
        var blockingOwners = footprintsByOwner
            .Where(existing => Overlaps(existing.Value, footprint))
            .Select(existing => existing.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        footprintsByOwner[ownerId] = footprint;
        return blockingOwners;
    }

    public void Clear() => footprintsByOwner.Clear();

    public static bool Overlaps(TrainFootprint left, TrainFootprint right) =>
        left.OccupiedIntervals.Any(leftInterval => right.OccupiedIntervals.Any(rightInterval => leftInterval.Overlaps(rightInterval)));
}

/// <summary>以 unified movement cursor 計算的 runtime occupancy index。</summary>
public sealed class TopologyMovementOccupancyIndex
{
    private readonly Dictionary<string, TopologyMovementFootprint> footprintsByOwner = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, TopologyMovementFootprint> FootprintsByOwner => footprintsByOwner;

    public IReadOnlyList<string> Set(string ownerId, TopologyMovementFootprint footprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        footprintsByOwner.Remove(ownerId);
        var blockingOwners = footprintsByOwner
            .Where(item => Overlaps(item.Value, footprint))
            .Select(item => item.Key)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        footprintsByOwner[ownerId] = footprint;
        return blockingOwners;
    }

    public void Clear() => footprintsByOwner.Clear();

    public static bool Overlaps(TopologyMovementFootprint left, TopologyMovementFootprint right) =>
        left.OccupiedIntervals.Any(leftInterval => right.OccupiedIntervals.Any(rightInterval => leftInterval.Overlaps(rightInterval)));
}

/// <summary>platform interval 與列車 footprint 的重疊查詢。</summary>
public static class TopologyPlatformOccupancy
{
    public static IReadOnlyList<PlatformDefinitionV4> GetOverlappingPlatforms(
        InfrastructureGraphV4 infrastructure,
        TrainFootprint footprint) =>
        GetOverlappingPlatforms(infrastructure, footprint.OccupiedIntervals);

    public static IReadOnlyList<PlatformDefinitionV4> GetOverlappingPlatforms(
        InfrastructureGraphV4 infrastructure,
        TopologyMovementFootprint footprint) =>
        GetOverlappingPlatforms(infrastructure, footprint.OccupiedIntervals);

    private static IReadOnlyList<PlatformDefinitionV4> GetOverlappingPlatforms(
        InfrastructureGraphV4 infrastructure,
        IReadOnlyList<TrackOccupancyInterval> occupiedIntervals) =>
        infrastructure.Platforms.Values
            .Where(platform => occupiedIntervals.Any(interval =>
                interval.TrackEdgeId.Equals(platform.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && interval.StartOffsetMeters <= platform.PlatformEndOffsetMeters + TrackPosition.DefaultToleranceMeters
                && platform.PlatformStartOffsetMeters <= interval.EndOffsetMeters + TrackPosition.DefaultToleranceMeters))
            .OrderBy(platform => platform.PlatformId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

/// <summary>以 edge direction 與 graph path 計算列車間／障礙物間的前方距離。</summary>
public static class TopologyGraphDistance
{
    private readonly record struct BridgeKey(string FromNodeId, string ToNodeId,
        DirectedTrackTraversal Incoming, DirectedTrackTraversal Outgoing);
    private readonly record struct BridgeDistance(bool Found, double LengthMeters);
    // InfrastructureGraphV4 is immutable after construction. Keep each graph's
    // directed bridge distances weakly keyed so loading another project neither
    // reuses its paths nor retains the old infrastructure indefinitely.
    private static readonly ConditionalWeakTable<InfrastructureGraphV4,
        ConcurrentDictionary<BridgeKey, BridgeDistance>> BridgeDistances = new();

    private static double? GetBridgeDistance(InfrastructureGraphV4 infrastructure,
        string fromNodeId, string toNodeId,
        DirectedTrackTraversal incoming, DirectedTrackTraversal outgoing)
    {
        var distances = BridgeDistances.GetValue(infrastructure,
            static _ => new ConcurrentDictionary<BridgeKey, BridgeDistance>());
        var result = distances.GetOrAdd(new BridgeKey(fromNodeId, toNodeId, incoming, outgoing), key =>
        {
            try
            {
                var path = TopologyPathFinder.FindShortestPath(infrastructure,
                    key.FromNodeId, key.ToNodeId,
                    new TopologyPathConstraints
                    {
                        IncomingTraversal = key.Incoming,
                        OutgoingTraversal = key.Outgoing
                    });
                return new BridgeDistance(true, path.TotalLengthMeters);
            }
            catch (SimulationValidationException)
            {
                return new BridgeDistance(false, 0);
            }
        });
        return result.Found ? result.LengthMeters : null;
    }

    public static double? TryGetForwardDistance(
        InfrastructureGraphV4 infrastructure,
        TopologyMovementNavigator fromNavigator,
        RuntimeTopologyCursor from,
        TopologyMovementNavigator targetNavigator,
        RuntimeTopologyCursor target)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(fromNavigator);
        ArgumentNullException.ThrowIfNull(targetNavigator);

        if (from.MovementPlanId.Equals(target.MovementPlanId, StringComparison.OrdinalIgnoreCase))
            return fromNavigator.TryGetForwardDistance(from, target);

        var fromTraversal = fromNavigator.GetTraversal(from.MovementLegIndex, from.TraversalIndex);
        var targetTraversal = targetNavigator.GetTraversal(target.MovementLegIndex, target.TraversalIndex);
        var fromDistance = fromNavigator.GetDistanceAlongTraversal(from);
        var targetDistance = targetNavigator.GetDistanceAlongTraversal(target);
        if (from.Position.TrackEdgeId.Equals(target.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            && from.Direction == target.Direction
            && targetDistance + TrackPosition.DefaultToleranceMeters >= fromDistance)
        {
            return Math.Max(0, targetDistance - fromDistance);
        }

        var fromEndNode = InfrastructureValidator.GetEndNode(fromTraversal.Edge, from.Direction);
        var targetStartNode = InfrastructureValidator.GetStartNode(targetTraversal.Edge, target.Direction);
        var bridge = GetBridgeDistance(infrastructure, fromEndNode, targetStartNode,
            new(fromTraversal.Edge.TrackEdgeId, from.Direction),
            new(targetTraversal.Edge.TrackEdgeId, target.Direction));
        return bridge is null ? null
            : (fromTraversal.LengthMeters - fromDistance) + bridge.Value + targetDistance;
    }

    public static double? TryGetFootprintGap(
        InfrastructureGraphV4 infrastructure,
        TopologyMovementNavigator followerNavigator,
        TopologyMovementFootprint follower,
        TopologyMovementNavigator leaderNavigator,
        TopologyMovementFootprint leader)
    {
        var headDistance = TryGetForwardDistance(infrastructure, followerNavigator, follower.Front, leaderNavigator, leader.Front);
        if (headDistance is null) return null;
        var leaderLength = leaderNavigator.TryGetForwardDistance(leader.Rear, leader.Front);
        return leaderLength is null ? null : headDistance.Value - leaderLength.Value;
    }

    public static double? TryGetForwardDistance(
        InfrastructureGraphV4 infrastructure,
        TopologyRouteNavigator fromNavigator,
        TopologyTraversalCursor from,
        TopologyRouteNavigator targetNavigator,
        TopologyTraversalCursor target)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(fromNavigator);
        ArgumentNullException.ThrowIfNull(targetNavigator);

        if (from.ServiceRouteId.Equals(target.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
            return fromNavigator.TryGetForwardDistance(from, target);

        var fromTraversal = fromNavigator.GetTraversal(from.TraversalIndex);
        var targetTraversal = targetNavigator.GetTraversal(target.TraversalIndex);
        var fromEdge = infrastructure.GetRequiredEdge(fromTraversal.TrackEdgeId);
        var targetEdge = infrastructure.GetRequiredEdge(targetTraversal.TrackEdgeId);
        var fromDistance = fromNavigator.GetDistanceAlongTraversal(from);
        var targetDistance = targetNavigator.GetDistanceAlongTraversal(target);

        if (from.Position.TrackEdgeId.Equals(target.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            && fromTraversal.Direction == targetTraversal.Direction
            && targetDistance + TrackPosition.DefaultToleranceMeters >= fromDistance)
        {
            return Math.Max(0, targetDistance - fromDistance);
        }

        var fromEndNode = InfrastructureValidator.GetEndNode(fromEdge, fromTraversal.Direction);
        var targetStartNode = InfrastructureValidator.GetStartNode(targetEdge, targetTraversal.Direction);
        var bridge = GetBridgeDistance(infrastructure, fromEndNode, targetStartNode,
            fromTraversal, targetTraversal);
        return bridge is null ? null
            : (fromEdge.LengthMeters - fromDistance) + bridge.Value + targetDistance;
    }

    public static double? TryGetFootprintGap(
        InfrastructureGraphV4 infrastructure,
        TopologyRouteNavigator followerNavigator,
        TrainFootprint follower,
        TopologyRouteNavigator leaderNavigator,
        TrainFootprint leader)
    {
        var headDistance = TryGetForwardDistance(
            infrastructure,
            followerNavigator,
            follower.Front,
            leaderNavigator,
            leader.Front);
        if (headDistance is null)
        {
            return null;
        }

        var leaderLength = leaderNavigator.TryGetForwardDistance(leader.Rear, leader.Front);
        return leaderLength is null
            ? null
            : headDistance.Value - leaderLength.Value;
    }
}
