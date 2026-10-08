using MrtRouteSimulator.Engine;

/// <summary>
/// Direct tests for the topology nearest-leader broad phase. These deliberately keep
/// graph-distance as the oracle: the candidate index may only remove owners whose
/// directed front traversal is unreachable.
/// </summary>
public static class NearestLeaderCandidateIndexTests
{
    public static void SameAdjacentCrossEdgeAndTailCandidates()
    {
        var fixture = SyntheticTopology.Create();
        var occupants = new Dictionary<string, Occupant>(StringComparer.OrdinalIgnoreCase)
        {
            ["FOLLOWER"] = fixture.At("MAIN-0", TraversalDirection.Forward, 20),
            ["SAME-EDGE"] = fixture.At("MAIN-0", TraversalDirection.Forward, 70),
            ["ADJACENT-EDGE"] = fixture.At("LOCAL", TraversalDirection.Forward, 10),
            ["PASSING-BRANCH"] = fixture.At("PASS", TraversalDirection.Forward, 10),
            ["CROSS-EDGE-TAIL"] = fixture.AtCrossEdgeTail(),
            ["UNREACHABLE-OPPOSITE"] = fixture.At("MAIN-0", TraversalDirection.Reverse, 20)
        };

        var index = BuildIndex(fixture.Graph, occupants, generation: 11);
        var actual = GetCandidates(index, "FOLLOWER", 11);

        AssertSetContains(actual, "SAME-EDGE", "同一 edge 的前方車輛必須保留");
        AssertSetContains(actual, "ADJACENT-EDGE", "相鄰 edge 的前方車輛必須保留");
        AssertSetContains(actual, "PASSING-BRANCH", "分支 passing edge 的前方車輛必須保留");
        AssertSetContains(actual, "CROSS-EDGE-TAIL", "跨越多個 edge 的尾軌車輛必須保留");
        AssertSetExcludes(actual, "UNREACHABLE-OPPOSITE", "沒有合法反向進路的 opposite traversal 不得成為候選");
        AssertTrue(occupants["CROSS-EDGE-TAIL"].Footprint.OccupiedIntervals.Count >= 2,
            "cross-edge tail footprint 必須實際跨越至少兩個 physical edge");
        AssertTrue(occupants["CROSS-EDGE-TAIL"].Footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == "MAIN-1"),
            "cross-edge tail footprint 必須保留前一段 MAIN-1 占用");
        AssertTrue(occupants["CROSS-EDGE-TAIL"].Footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == "TAIL"),
            "cross-edge tail footprint 必須保留 TAIL 占用");

        // LOCAL@10 and PASS@10 are both 90 m of graph head distance from MAIN-0@20.
        // Compare a full-scan oracle with the same narrow phase over the indexed subset;
        // both must resolve the exact tie by VehicleId ordinal-ignore-case.
        var tieOccupants = new Dictionary<string, Occupant>(StringComparer.OrdinalIgnoreCase)
        {
            ["FOLLOWER"] = occupants["FOLLOWER"],
            ["ADJACENT-EDGE"] = occupants["ADJACENT-EDGE"],
            ["PASSING-BRANCH"] = occupants["PASSING-BRANCH"]
        };
        var tieIndex = BuildIndex(fixture.Graph, tieOccupants, generation: 111);
        var tieIndexedCandidates = GetCandidates(tieIndex, "FOLLOWER", 111);
        var tieAllScanWinner = SelectNearestByGraphDistance(fixture.Graph, tieOccupants, "FOLLOWER",
            tieOccupants.Keys.Where(item => !item.Equals("FOLLOWER", StringComparison.OrdinalIgnoreCase)));
        var tieIndexedWinner = SelectNearestByGraphDistance(fixture.Graph, tieOccupants, "FOLLOWER", tieIndexedCandidates);
        AssertEqual("ADJACENT-EDGE", tieAllScanWinner,
            "all-scan oracle 必須依 VehicleId ordinal-ignore-case 選出同距前車");
        AssertEqual(tieAllScanWinner, tieIndexedWinner,
            "indexed candidate subset 的 narrow phase 必須與 all-scan oracle 選出同一 VehicleId");

        AssertGraphDistanceReachableCandidatesAreIncluded(fixture.Graph, occupants, index, "FOLLOWER", 11);
    }

    public static void BranchPassingAndMergeRemainSeparated()
    {
        var fixture = SyntheticTopology.Create();
        var occupants = new Dictionary<string, Occupant>(StringComparer.OrdinalIgnoreCase)
        {
            ["PASSING-FOLLOWER"] = fixture.At("PASS", TraversalDirection.Forward, 20),
            ["PASSING-AHEAD"] = fixture.At("PASS-1", TraversalDirection.Forward, 20),
            ["LOCAL-AHEAD"] = fixture.At("LOCAL", TraversalDirection.Forward, 20),
            ["MERGED-AHEAD"] = fixture.At("TAIL", TraversalDirection.Forward, 20),
            ["PASSING-REVERSE"] = fixture.At("PASS", TraversalDirection.Reverse, 20)
        };

        var index = BuildIndex(fixture.Graph, occupants, generation: 12);
        var actual = GetCandidates(index, "PASSING-FOLLOWER", 12);

        AssertSetContains(actual, "PASSING-AHEAD", "passing branch 後續 edge 必須保留");
        AssertSetContains(actual, "MERGED-AHEAD", "passing branch 合流後的共同 edge 必須保留");
        AssertSetExcludes(actual, "LOCAL-AHEAD", "平行 local branch 尚未合流時不得互相配對");
        AssertSetContains(actual, "PASSING-REVERSE", "經由實體尾軌換端後的 passing 反向 traversal 必須保留");

        AssertGraphDistanceReachableCandidatesAreIncluded(fixture.Graph, occupants, index, "PASSING-FOLLOWER", 12);
    }

    public static void OppositeDirectionTurnbackAndRepeatedEdgeCandidates()
    {
        var fixture = SyntheticTopology.Create();
        var occupants = new Dictionary<string, Occupant>(StringComparer.OrdinalIgnoreCase)
        {
            ["TAIL-FOLLOWER"] = fixture.At("TAIL", TraversalDirection.Forward, 20),
            ["TAIL-TURNBACK"] = fixture.At("TAIL", TraversalDirection.Reverse, 20),
            ["TAIL-REPEATED"] = fixture.AtRepeatedTail(20),
            ["MAIN-OPPOSITE"] = fixture.At("MAIN-0", TraversalDirection.Reverse, 20),
            ["TAIL-FORWARD-AHEAD"] = fixture.At("TAIL", TraversalDirection.Forward, 45)
        };

        var index = BuildIndex(fixture.Graph, occupants, generation: 13);
        var actual = GetCandidates(index, "TAIL-FOLLOWER", 13);

        AssertSetContains(actual, "TAIL-TURNBACK", "尾軌的合法換端 traversal 必須保留");
        AssertSetContains(actual, "TAIL-REPEATED", "重複使用同一 edge 的 movement plan 不得被索引遺漏");
        AssertSetContains(actual, "TAIL-FORWARD-AHEAD", "換端後再次進入同一方向 edge 的車輛必須保留");
        AssertSetExcludes(actual, "MAIN-OPPOSITE", "主線沒有 turnback connection 時 opposite traversal 必須排除");

        AssertGraphDistanceReachableCandidatesAreIncluded(fixture.Graph, occupants, index, "TAIL-FOLLOWER", 13);
    }

    public static void StaleGenerationAndMissingFollowerFallBack()
    {
        var fixture = SyntheticTopology.Create();
        var occupants = new Dictionary<string, Occupant>(StringComparer.OrdinalIgnoreCase)
        {
            ["FOLLOWER"] = fixture.At("MAIN-0", TraversalDirection.Forward, 20),
            ["LEADER"] = fixture.At("LOCAL", TraversalDirection.Forward, 20)
        };
        var index = BuildIndex(fixture.Graph, occupants, generation: 14);

        var stale = index.TryGetPotentialCandidates("FOLLOWER", 13, out var staleCandidates, out var staleReason);
        AssertFalse(stale, "舊 generation 不得使用索引候選");
        AssertEqual(0, staleCandidates.Count, "stale generation fallback 不得回傳部分候選");
        AssertEqual("stale-generation:14->13", staleReason, "stale generation 必須保留可診斷原因");

        var missing = index.TryGetPotentialCandidates("UNKNOWN", 14, out var missingCandidates, out var missingReason);
        AssertFalse(missing, "未索引 follower 必須 fallback");
        AssertEqual(0, missingCandidates.Count, "missing follower fallback 不得回傳部分候選");
        AssertEqual("follower-not-indexed", missingReason, "missing follower 必須保留可診斷原因");
    }

    public static void CandidateSetContainsAllGraphDistanceReachableOracleCandidates()
    {
        var fixture = SyntheticTopology.Create();
        var occupants = new Dictionary<string, Occupant>(StringComparer.OrdinalIgnoreCase)
        {
            ["FOLLOWER-MAIN"] = fixture.At("MAIN-0", TraversalDirection.Forward, 20),
            ["SAME"] = fixture.At("MAIN-0", TraversalDirection.Forward, 70),
            ["LOCAL"] = fixture.At("LOCAL", TraversalDirection.Forward, 20),
            ["PASS"] = fixture.At("PASS", TraversalDirection.Forward, 20),
            ["LOCAL-AHEAD"] = fixture.At("MAIN-1", TraversalDirection.Forward, 20),
            ["PASS-AHEAD"] = fixture.At("PASS-1", TraversalDirection.Forward, 20),
            ["TAIL"] = fixture.At("TAIL", TraversalDirection.Forward, 20),
            ["TAIL-REVERSE"] = fixture.At("TAIL", TraversalDirection.Reverse, 20),
            ["MAIN-REVERSE"] = fixture.At("MAIN-0", TraversalDirection.Reverse, 20),
            ["REPEATED-TAIL"] = fixture.AtRepeatedTail(20)
        };
        var index = BuildIndex(fixture.Graph, occupants, generation: 15);

        foreach (var followerId in occupants.Keys)
        {
            var actual = GetCandidates(index, followerId, 15);
            AssertGraphDistanceReachableCandidatesAreIncluded(fixture.Graph, occupants, index, followerId, 15);
            AssertFalse(actual.Contains(followerId, StringComparer.OrdinalIgnoreCase),
                $"候選集合不可包含 follower 自己：{followerId}");
        }
    }

    private static TopologyNearestLeaderCandidateIndex BuildIndex(
        InfrastructureGraphV4 graph,
        IReadOnlyDictionary<string, Occupant> occupants,
        long generation)
    {
        var index = new TopologyNearestLeaderCandidateIndex(graph);
        index.Rebuild(generation, occupants.ToDictionary(
            item => item.Key,
            item => item.Value.Footprint,
            StringComparer.OrdinalIgnoreCase));
        return index;
    }

    private static IReadOnlyList<string> GetCandidates(
        TopologyNearestLeaderCandidateIndex index,
        string followerId,
        long generation)
    {
        if (!index.TryGetPotentialCandidates(followerId, generation, out var candidates, out var reason))
            throw new InvalidOperationException($"候選查詢不應 fallback：{followerId}; reason={reason}");
        return candidates;
    }

    private static void AssertGraphDistanceReachableCandidatesAreIncluded(
        InfrastructureGraphV4 graph,
        IReadOnlyDictionary<string, Occupant> occupants,
        TopologyNearestLeaderCandidateIndex index,
        string followerId,
        long generation)
    {
        var follower = occupants[followerId];
        var expected = occupants
            .Where(item => !item.Key.Equals(followerId, StringComparison.OrdinalIgnoreCase))
            .Where(item => TopologyGraphDistance.TryGetForwardDistance(
                graph,
                follower.Navigator,
                follower.Footprint.Front,
                item.Value.Navigator,
                item.Value.Footprint.Front) is not null)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = GetCandidates(index, followerId, generation).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!expected.IsSubsetOf(actual))
        {
            throw new InvalidOperationException(
                $"candidate index 遺漏 graph-distance 可達 owners；follower={followerId}; "
                + $"missing={string.Join(",", expected.Except(actual, StringComparer.OrdinalIgnoreCase))}");
        }
    }

    private static void AssertSetContains(IEnumerable<string> values, string expected, string message)
    {
        if (!values.Contains(expected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + $"；actual=[{string.Join(",", values)}]");
    }

    private static void AssertSetExcludes(IEnumerable<string> values, string unexpected, string message)
    {
        if (values.Contains(unexpected, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + $"；actual=[{string.Join(",", values)}]");
    }

    private static string SelectNearestByGraphDistance(
        InfrastructureGraphV4 graph,
        IReadOnlyDictionary<string, Occupant> occupants,
        string followerId,
        IEnumerable<string> candidateOwnerIds)
    {
        var follower = occupants[followerId];
        return candidateOwnerIds
            .Select(ownerId => new
            {
                OwnerId = ownerId,
                HeadDistance = TopologyGraphDistance.TryGetForwardDistance(
                    graph,
                    follower.Navigator,
                    follower.Footprint.Front,
                    occupants[ownerId].Navigator,
                    occupants[ownerId].Footprint.Front)
            })
            .Where(item => item.HeadDistance is not null)
            .OrderBy(item => item.HeadDistance)
            .ThenBy(item => item.OwnerId, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.OwnerId)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"找不到 follower「{followerId}」的 graph-distance 前車。");
    }

    private static void AssertTrue(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} expected={expected}; actual={actual}");
    }

    private sealed record Occupant(
        TopologyMovementNavigator Navigator,
        TopologyMovementFootprint Footprint);

    private sealed class SyntheticTopology
    {
        private SyntheticTopology(InfrastructureGraphV4 graph)
        {
            Graph = graph;
        }

        public InfrastructureGraphV4 Graph { get; }

        public static SyntheticTopology Create()
        {
            static TrackEdgeDefinition Edge(string id, string from, string to, double length = 100,
                TrackEdgeKind kind = TrackEdgeKind.Mainline) => new()
                {
                    TrackEdgeId = id,
                    FromNodeId = from,
                    ToNodeId = to,
                    LengthMeters = length,
                    Directionality = TrackDirectionality.Bidirectional,
                    Kind = kind,
                    DefaultSpeedLimitMetersPerSecond = 20,
                    FromPortSide = TrackPortSide.A,
                    ToPortSide = TrackPortSide.B
                };

            var definition = new TopologyInfrastructureDefinition
            {
                Nodes =
                [
                    new TrackNodeDefinition("A", "起點", TrackNodeKind.Boundary),
                    new TrackNodeDefinition("B", "分支", TrackNodeKind.Junction),
                    new TrackNodeDefinition("C", "local 合流前", TrackNodeKind.Junction),
                    new TrackNodeDefinition("D", "passing 合流前", TrackNodeKind.Junction),
                    new TrackNodeDefinition("E", "合流", TrackNodeKind.Junction),
                    new TrackNodeDefinition("F", "尾端", TrackNodeKind.BufferStop)
                ],
                Edges =
                [
                    Edge("MAIN-0", "A", "B"),
                    Edge("LOCAL", "B", "C"),
                    Edge("PASS", "B", "D", kind: TrackEdgeKind.PassingTrack),
                    Edge("MAIN-1", "C", "E"),
                    Edge("PASS-1", "D", "E", kind: TrackEdgeKind.PassingTrack),
                    Edge("TAIL", "E", "F", length: 60, kind: TrackEdgeKind.TailTrack)
                ],
                DirectedConnections =
                [
                    // Mainline branch. The reverse branch ends at B in this fixture;
                    // this leaves MAIN-0:Reverse genuinely unreachable from MAIN-0:Forward.
                    Connection("MAIN-0", TraversalDirection.Forward, "LOCAL", TraversalDirection.Forward),
                    Connection("MAIN-0", TraversalDirection.Forward, "PASS", TraversalDirection.Forward),

                    // Each branch remains separate until the common tail edge.
                    Connection("LOCAL", TraversalDirection.Forward, "MAIN-1", TraversalDirection.Forward),
                    Connection("MAIN-1", TraversalDirection.Reverse, "LOCAL", TraversalDirection.Reverse),
                    Connection("PASS", TraversalDirection.Forward, "PASS-1", TraversalDirection.Forward),
                    Connection("PASS-1", TraversalDirection.Reverse, "PASS", TraversalDirection.Reverse),

                    Connection("MAIN-1", TraversalDirection.Forward, "TAIL", TraversalDirection.Forward),
                    Connection("PASS-1", TraversalDirection.Forward, "TAIL", TraversalDirection.Forward),
                    Connection("TAIL", TraversalDirection.Reverse, "MAIN-1", TraversalDirection.Reverse),
                    Connection("TAIL", TraversalDirection.Reverse, "PASS-1", TraversalDirection.Reverse),

                    // Physical tail turnback, including a repeated traversal of the same edge.
                    Connection("TAIL", TraversalDirection.Reverse, "TAIL", TraversalDirection.Forward)
                ]
            };
            return new SyntheticTopology(new InfrastructureGraphV4(definition));
        }

        public Occupant At(string edgeId, TraversalDirection direction, double distanceAlong)
        {
            var navigator = Navigator(edgeId + ":" + direction, [new DirectedTrackTraversal(edgeId, direction)]);
            return new Occupant(navigator, navigator.CreateFootprint(
                navigator.CreateCursor(0, 0, distanceAlong), trainLengthMeters: 5));
        }

        public Occupant AtCrossEdgeTail()
        {
            var traversals = new[]
            {
                new DirectedTrackTraversal("MAIN-0", TraversalDirection.Forward),
                new DirectedTrackTraversal("LOCAL", TraversalDirection.Forward),
                new DirectedTrackTraversal("MAIN-1", TraversalDirection.Forward),
                new DirectedTrackTraversal("TAIL", TraversalDirection.Forward)
            };
            var navigator = Navigator("TAIL:CROSS-EDGE", traversals);
            return new Occupant(navigator, navigator.CreateFootprint(
                navigator.CreateCursor(0, 3, 20), trainLengthMeters: 150));
        }

        public Occupant AtRepeatedTail(double distanceAlong)
        {
            var traversals = new[]
            {
                new DirectedTrackTraversal("TAIL", TraversalDirection.Forward),
                new DirectedTrackTraversal("TAIL", TraversalDirection.Reverse),
                new DirectedTrackTraversal("TAIL", TraversalDirection.Forward)
            };
            var navigator = Navigator("TAIL:REPEATED", traversals);
            return new Occupant(navigator, navigator.CreateFootprint(
                navigator.CreateCursor(0, 2, distanceAlong), trainLengthMeters: 5));
        }

        private TopologyMovementNavigator Navigator(string id, IReadOnlyList<DirectedTrackTraversal> traversals) =>
            new(Graph, new ResolvedMovementPlan
            {
                MovementPlanId = id,
                Legs =
                [
                    new ResolvedMovementLeg
                    {
                        LegId = id + ":LEG",
                        Kind = MovementLegKind.ServiceRoute,
                        Traversals = TopologyMovementPlanResolver.ResolveTraversals(Graph, traversals)
                    }
                ]
            });

        private static DirectedTrackConnectionDefinition Connection(
            string fromEdge, TraversalDirection fromDirection,
            string toEdge, TraversalDirection toDirection) =>
            new(fromEdge, fromDirection, toEdge, toDirection);
    }
}
