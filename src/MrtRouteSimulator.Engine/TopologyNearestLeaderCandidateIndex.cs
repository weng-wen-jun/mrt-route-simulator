namespace MrtRouteSimulator.Engine;

/// <summary>
/// Read-only broad-phase view derived from the current physical occupancy snapshot. It only removes
/// owners whose directed front traversal is provably unreachable from the follower traversal;
/// graph distance remains the narrow-phase truth.
/// </summary>
public sealed class TopologyNearestLeaderCandidateIndex
{
    private readonly IReadOnlyDictionary<DirectedTrackTraversal, IReadOnlySet<DirectedTrackTraversal>> reachableTraversals;
    private readonly Dictionary<string, DirectedTrackTraversal> frontsByOwner = new(StringComparer.OrdinalIgnoreCase);

    public TopologyNearestLeaderCandidateIndex(InfrastructureGraphV4 infrastructure)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        reachableTraversals = BuildReachability(infrastructure);
    }

    public long Generation { get; private set; } = -1;

    public int Count => frontsByOwner.Count;

    public void Rebuild(
        long generation,
        IReadOnlyDictionary<string, TopologyMovementFootprint> footprintsByOwner)
    {
        ArgumentNullException.ThrowIfNull(footprintsByOwner);
        frontsByOwner.Clear();
        foreach (var item in footprintsByOwner)
        {
            var front = item.Value.Front;
            var traversal = new DirectedTrackTraversal(front.Position.TrackEdgeId, front.Direction);
            if (!reachableTraversals.ContainsKey(traversal))
            {
                throw new SimulationValidationException([
                    $"occupancy owner「{item.Key}」的車頭 traversal「{traversal.TrackEdgeId}:{traversal.Direction}」不屬於 topology。"
                ]);
            }

            frontsByOwner.Add(item.Key, traversal);
        }

        Generation = generation;
    }

    public bool TryGetPotentialCandidates(
        string followerOwnerId,
        long expectedGeneration,
        out IReadOnlyList<string> candidateOwnerIds,
        out string? fallbackReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(followerOwnerId);
        if (Generation != expectedGeneration)
        {
            candidateOwnerIds = [];
            fallbackReason = $"stale-generation:{Generation}->{expectedGeneration}";
            return false;
        }

        if (!frontsByOwner.TryGetValue(followerOwnerId, out var followerTraversal))
        {
            candidateOwnerIds = [];
            fallbackReason = "follower-not-indexed";
            return false;
        }

        if (!reachableTraversals.TryGetValue(followerTraversal, out var reachable))
        {
            candidateOwnerIds = [];
            fallbackReason = "follower-traversal-not-indexed";
            return false;
        }

        candidateOwnerIds = frontsByOwner
            .Where(item => !item.Key.Equals(followerOwnerId, StringComparison.OrdinalIgnoreCase)
                && reachable.Contains(item.Value))
            .Select(item => item.Key)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        fallbackReason = null;
        return true;
    }

    private static IReadOnlyDictionary<DirectedTrackTraversal, IReadOnlySet<DirectedTrackTraversal>> BuildReachability(
        InfrastructureGraphV4 infrastructure)
    {
        var traversals = infrastructure.Edges.Values
            .OrderBy(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(edge => Enum.GetValues<TraversalDirection>()
                .Where(direction => InfrastructureValidator.AllowsTraversal(edge, direction))
                .Select(direction => new DirectedTrackTraversal(edge.TrackEdgeId, direction)))
            .ToArray();
        var transitions = traversals.ToDictionary(
            traversal => traversal,
            traversal => traversals.Where(candidate => infrastructure.AllowsTransition(traversal, candidate)).ToArray());
        var result = new Dictionary<DirectedTrackTraversal, IReadOnlySet<DirectedTrackTraversal>>();

        foreach (var start in traversals)
        {
            var reachable = new HashSet<DirectedTrackTraversal> { start };
            var pending = new Queue<DirectedTrackTraversal>();
            pending.Enqueue(start);
            while (pending.TryDequeue(out var current))
            {
                foreach (var next in transitions[current])
                {
                    if (reachable.Add(next))
                    {
                        pending.Enqueue(next);
                    }
                }
            }

            result.Add(start, reachable);
        }

        return result;
    }
}
