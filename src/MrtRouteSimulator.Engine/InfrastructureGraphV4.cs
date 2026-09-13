using System.Collections.ObjectModel;

namespace MrtRouteSimulator.Engine;

/// <summary>
/// V4 的 track-first infrastructure aggregate。所有可行駛設施均以 edge 與有向
/// connection 表示；node adjacency 不會覆寫道岔的明確轉向限制。
/// </summary>
public sealed class InfrastructureGraphV4
{
    public InfrastructureGraphV4(TopologyInfrastructureDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        InfrastructureValidator.ValidateAndThrow(definition);

        Nodes = ToDictionary(definition.Nodes, item => item.NodeId);
        Edges = ToDictionary(definition.Edges, item => item.TrackEdgeId);
        Stations = ToDictionary(definition.Stations, item => item.StationId);
        Platforms = ToDictionary(definition.Platforms, item => item.PlatformId);
        Resources = ToDictionary(definition.Resources, item => item.ResourceId);
        SpeedLimits = ToDictionary(definition.SpeedLimits, item => item.SpeedLimitId);
        Gradients = ToDictionary(definition.Gradients, item => item.GradientId);
        Curves = ToDictionary(definition.Curves, item => item.CurveId);
        DirectedConnections = definition.DirectedConnections.ToArray();
        TurnbackFacilities = ToDictionary(definition.TurnbackFacilities, item => item.FacilityId);
        TurnbackOperations = ToDictionary(definition.TurnbackOperations, item => item.OperationId);
        PassingFacilities = ToDictionary(definition.PassingFacilities, item => item.FacilityId);
        PassingOperations = ToDictionary(definition.PassingOperations, item => item.OperationId);
        StationOperations = ToDictionary(definition.StationOperations, item => item.StationOperationId);
        OutgoingEdgesByNode = BuildEdgeIndex(Edges.Values, edge => edge.FromNodeId);
        IncomingEdgesByNode = BuildEdgeIndex(Edges.Values, edge => edge.ToNodeId);
        PlatformsByStation = BuildPlatformIndex(Platforms.Values, platform => platform.StationId);
        PlatformsByTrackEdge = BuildPlatformIndex(Platforms.Values, platform => platform.TrackEdgeId);
    }

    public IReadOnlyDictionary<string, TrackNodeDefinition> Nodes { get; }
    public IReadOnlyDictionary<string, TrackEdgeDefinition> Edges { get; }
    public IReadOnlyDictionary<string, StationDefinitionV4> Stations { get; }
    public IReadOnlyDictionary<string, PlatformDefinitionV4> Platforms { get; }
    public IReadOnlyDictionary<string, ConflictResourceDefinition> Resources { get; }
    public IReadOnlyDictionary<string, TrackSpeedLimitDefinition> SpeedLimits { get; }
    public IReadOnlyDictionary<string, TrackGradientSegment> Gradients { get; }
    public IReadOnlyDictionary<string, TrackCurveSegment> Curves { get; }
    public IReadOnlyList<DirectedTrackConnectionDefinition> DirectedConnections { get; }
    public IReadOnlyDictionary<string, TurnbackFacilityDefinition> TurnbackFacilities { get; }
    public IReadOnlyDictionary<string, TurnbackOperationDefinition> TurnbackOperations { get; }
    public IReadOnlyDictionary<string, PassingFacilityDefinition> PassingFacilities { get; }
    public IReadOnlyDictionary<string, PassingOperationDefinition> PassingOperations { get; }
    public IReadOnlyDictionary<string, StationOperationDefinition> StationOperations { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<TrackEdgeDefinition>> OutgoingEdgesByNode { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<TrackEdgeDefinition>> IncomingEdgesByNode { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<PlatformDefinitionV4>> PlatformsByStation { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<PlatformDefinitionV4>> PlatformsByTrackEdge { get; }

    public bool TryGetEdge(string? trackEdgeId, out TrackEdgeDefinition edge)
    {
        if (!string.IsNullOrWhiteSpace(trackEdgeId)
            && Edges.TryGetValue(trackEdgeId.Trim(), out var found))
        {
            edge = found;
            return true;
        }

        edge = null!;
        return false;
    }

    public TrackEdgeDefinition GetRequiredEdge(string trackEdgeId) =>
        TryGetEdge(trackEdgeId, out var edge)
            ? edge
            : throw new KeyNotFoundException($"找不到軌道 edge「{trackEdgeId}」。");

    public bool ContainsPosition(TrackPosition position) =>
        TryGetEdge(position.TrackEdgeId, out var edge)
        && double.IsFinite(position.OffsetMeters)
        && position.OffsetMeters >= -TrackPosition.DefaultToleranceMeters
        && position.OffsetMeters <= edge.LengthMeters + TrackPosition.DefaultToleranceMeters;

    /// <summary>
    /// 判斷列車穿越共同 node 時是否符合明確道岔轉向。沒有設定 connection 的 node
    /// 仍採一般 graph adjacency，避免既有線性 topology 被無故縮限。
    /// </summary>
    public bool AllowsTransition(DirectedTrackTraversal from, DirectedTrackTraversal to) =>
        InfrastructureValidator.AllowsTransition(Edges, DirectedConnections, from, to);

    private static IReadOnlyDictionary<string, T> ToDictionary<T>(
        IEnumerable<T> items,
        Func<T, string> keySelector)
        where T : notnull => new ReadOnlyDictionary<string, T>(items.ToDictionary(keySelector, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, IReadOnlyList<TrackEdgeDefinition>> BuildEdgeIndex(
        IEnumerable<TrackEdgeDefinition> edges,
        Func<TrackEdgeDefinition, string> keySelector) =>
        new ReadOnlyDictionary<string, IReadOnlyList<TrackEdgeDefinition>>(
            edges.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TrackEdgeDefinition>)group.OrderBy(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyDictionary<string, IReadOnlyList<PlatformDefinitionV4>> BuildPlatformIndex(
        IEnumerable<PlatformDefinitionV4> platforms,
        Func<PlatformDefinitionV4, string> keySelector) =>
        new ReadOnlyDictionary<string, IReadOnlyList<PlatformDefinitionV4>>(
            platforms.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase).ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PlatformDefinitionV4>)group.OrderBy(platform => platform.PlatformId, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase));
}
