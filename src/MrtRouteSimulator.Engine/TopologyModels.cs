using System.Text.Json.Serialization;

namespace MrtRouteSimulator.Engine;

/// <summary>軌道圖的節點種類；節點本身不具有車站語意。</summary>
public enum TrackNodeKind
{
    Boundary,
    Ordinary,
    Junction,
    Switch,
    Crossing,
    BufferStop,
    DepotBoundary,
    Other
}

/// <summary>軌道 edge 相對於 FromNodeId → ToNodeId 的可通行方向。</summary>
public enum TrackDirectionality
{
    Bidirectional,
    ForwardOnly,
    ReverseOnly
}

/// <summary>軌道 edge 的實體用途。</summary>
public enum TrackEdgeKind
{
    Mainline,
    PlatformTrack,
    PassingTrack,
    Siding,
    Crossover,
    Turnback,
    PocketTrack,
    TailTrack,
    DepotLead,
    Approach,
    Other
}

/// <summary>一條 edge 在營運路徑中的行進方向。</summary>
public enum TraversalDirection
{
    Forward,
    Reverse
}

/// <summary>衝突資源的概念分類。</summary>
public enum ConflictResourceKind
{
    Other,
    Switch,
    Crossover,
    Block,
    Platform,
    PocketTrack,
    TailTrack
}

/// <summary>附著於實體 edge 的縱向幾何區間。</summary>
public sealed record TrackGradientSegment(
    string GradientId,
    string TrackEdgeId,
    double StartOffsetMeters,
    double EndOffsetMeters,
    double GradePermille);

/// <summary>附著於實體 edge 的平面曲線區間；半徑必須為有限正數。</summary>
public sealed record TrackCurveSegment(
    string CurveId,
    string TrackEdgeId,
    double StartOffsetMeters,
    double EndOffsetMeters,
    double RadiusMeters);

/// <summary>可由折返 operation 使用的實體設施類型。</summary>
public enum TurnbackFacilityKind
{
    TailTrack,
    PocketTrack,
    Crossover,
    Siding,
    Other
}

/// <summary>
/// 折返設施的實體 topology 定義。停等／返回位置都以 edge-local offset 表達，
/// 不以 station position 或 legacy virtual track 作為權威。
/// </summary>
public sealed record TurnbackFacilityDefinition
{
    public required string FacilityId { get; init; }
    public required string Name { get; init; }
    public TurnbackFacilityKind Kind { get; init; }
    public required string ArrivalTrackEdgeId { get; init; }
    public double ArrivalStopOffsetMeters { get; init; }
    public required string DepartureTrackEdgeId { get; init; }
    public double DepartureStartOffsetMeters { get; init; }
    /// <summary>
    /// 進入、停留與離開設施時實際走過的有序 traversal。這是 runtime 使用的權威物理路徑；
    /// 不得以投影里程或 virtual track 取代。舊檔尚未填寫時可由 <see cref="FacilityTrackEdgeIds"/>
    /// 建立一次性的 migration draft，但不可作為 topology-native runtime 的輸入。
    /// </summary>
    public IReadOnlyList<DirectedTrackTraversal> Traversals { get; init; } = [];
    /// <summary>
    /// 僅保留給 Schema 8 早期檔案的 migration/import 對照；新資料請使用
    /// <see cref="Traversals"/>，以保存方向與順序。
    /// </summary>
    public IReadOnlyList<string> FacilityTrackEdgeIds { get; init; } = [];
    /// <summary>
    /// 列車抵達折返／待避位置後所停在的 traversal 索引。指定的是「完整走完」該
    /// traversal 後的位置；例如尾軌 OUT、RETURN 兩段時通常是 0。未指定時 runtime
    /// 只會在可以由 BufferStop 節點唯一推導的位置停等。
    /// </summary>
    public int? TurnbackStopAfterTraversalIndex { get; init; }
    /// <summary>
    /// 折返停等的實體 edge-local 位置。指定後優先於舊的 traversal index，讓尾軌可在
    /// buffer stop 前的安全距離停車，而不是以 virtual terminal 或 edge endpoint 代替。
    /// </summary>
    public TrackPosition? TurnbackStopPosition { get; init; }
    public IReadOnlySet<string> ConflictResourceIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>將一條到達 ServiceRoute 接續至另一條出發 ServiceRoute 的折返作業。</summary>
public sealed record TurnbackOperationDefinition
{
    public required string OperationId { get; init; }
    public required string FacilityId { get; init; }
    public required string ArrivalServiceRouteId { get; init; }
    public required string DepartureServiceRouteId { get; init; }
    public double MinimumDwellTimeSeconds { get; init; }
}

/// <summary>
/// 站內待避／越行的正式實體分歧路徑。普通車依 ServiceRoute 駛入 LocalPlatformId 所在的側線，
/// 高等列車以同一個 topology cursor 經 Traversals 的正線通過，再接回同一條 ServiceRoute；絕不使用 virtual track。
/// </summary>
public sealed record PassingFacilityDefinition
{
    public required string FacilityId { get; init; }
    public required string Name { get; init; }
    public required string StationId { get; init; }
    public required string ArrivalTrackEdgeId { get; init; }
    public double ArrivalOffsetMeters { get; init; }
    public required string DepartureTrackEdgeId { get; init; }
    public double DepartureStartOffsetMeters { get; init; }
    public IReadOnlyList<DirectedTrackTraversal> Traversals { get; init; } = [];
    public required string LocalPlatformId { get; init; }
    public required string ExpressPlatformId { get; init; }
    public IReadOnlySet<string> ConflictResourceIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>將特定快速服務類型（或所有可越行服務）套用至一條 ServiceRoute 的 passing facility。</summary>
public sealed record PassingOperationDefinition
{
    public required string OperationId { get; init; }
    public required string FacilityId { get; init; }
    public required string ServiceRouteId { get; init; }
    public string? ExpressServiceTypeId { get; init; }
}

/// <summary>車站在 topology 上可使用的月台、停站與折返作業集合。</summary>
public sealed record StationOperationDefinition
{
    public required string StationOperationId { get; init; }
    public required string StationId { get; init; }
    public IReadOnlyList<string> ArrivalPlatformIds { get; init; } = [];
    public IReadOnlyList<string> DeparturePlatformIds { get; init; } = [];
    public IReadOnlyList<string> TurnbackOperationIds { get; init; } = [];
    public IReadOnlyList<string> PassingOperationIds { get; init; } = [];
    public double DefaultDwellTimeSeconds { get; init; }
}

/// <summary>不具車站語意的實體軌道端點或接續點。</summary>
public sealed record TrackNodeDefinition(
    string NodeId,
    string Name,
    TrackNodeKind Kind)
{
    public double? SchematicPosition { get; init; }
    public double? SchematicLane { get; init; }
}

/// <summary>同一節點的兩個實體接軌側；不同節點的 A/B 不代表共同方位。</summary>
public enum TrackPortSide { A, B }

/// <summary>實體軌道 edge；不得以 StationId 或全線里程作為其端點。</summary>
public sealed record TrackEdgeDefinition
{
    /// <summary>節點局部的實體接軌側別。A/B僅在同一節點比較，與示意座標無關。</summary>
    public TrackPortSide? FromPortSide { get; init; }
    public TrackPortSide? ToPortSide { get; init; }
    /// <summary>配線图的相對股道位置；僅供呈現，負值在上、正值在下。</summary>
    public double? SchematicLane { get; init; }
    public required string TrackEdgeId { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }
    public required double LengthMeters { get; init; }
    public TrackDirectionality Directionality { get; init; } = TrackDirectionality.Bidirectional;
    public TrackEdgeKind Kind { get; init; } = TrackEdgeKind.Mainline;
    public required double DefaultSpeedLimitMetersPerSecond { get; init; }
    public IReadOnlySet<string> ConflictResourceIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>邏輯車站；實體停靠位置由其月台指定，而非由車站本身指定。</summary>
public sealed record StationDefinitionV4
{
    public required string StationId { get; init; }
    public required string Name { get; init; }
    public double DefaultDwellTimeSeconds { get; init; }
    public IReadOnlyList<string> PlatformIds { get; init; } = [];
}

/// <summary>附著在單一 TrackEdge 的月台定義。</summary>
public enum PlatformSide { Auto, Above, Below }

public enum StopPositionReference { TrainFront, TrainCenter }

public sealed record PlatformDefinitionV4
{
    public StopPositionReference StopPositionReference { get; init; } = StopPositionReference.TrainFront;
    public string PlatformNumber { get; init; } = "";
    public string PlatformBodyId { get; init; } = "";
    public PlatformSide DisplaySide { get; init; } = PlatformSide.Auto;
    public required string PlatformId { get; init; }
    public required string StationId { get; init; }
    public required string Name { get; init; }
    public required string TrackEdgeId { get; init; }
    public double PlatformStartOffsetMeters { get; init; }
    public double PlatformEndOffsetMeters { get; init; }
    public double StopPositionOffsetMeters { get; init; }
    public TrackDirection AllowedDirection { get; init; } = TrackDirection.Both;
    public double EffectiveLengthMeters { get; init; }
    public bool AllowsPassengerService { get; init; } = true;
    public IReadOnlySet<string> AllowedVehicleTypeIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> AllowedServiceTypeIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>可被 edge、進路或折返設施共同引用的不可同時占用資源。</summary>
public sealed record ConflictResourceDefinition(
    string ResourceId,
    string Name,
    ConflictResourceKind Kind);

/// <summary>附著於單一實體 edge 的方向別速限區間。</summary>
public sealed record TrackSpeedLimitDefinition
{
    public required string SpeedLimitId { get; init; }
    public required string TrackEdgeId { get; init; }
    public double StartOffsetMeters { get; init; }
    public double EndOffsetMeters { get; init; }
    public required double LimitMetersPerSecond { get; init; }
    public TraversalDirection Direction { get; init; } = TraversalDirection.Forward;
}

/// <summary>列車在單一實體 edge 上的局部位置。這不是全線 chainage。</summary>
public readonly struct TrackPosition : IEquatable<TrackPosition>
{
    public const double DefaultToleranceMeters = 1e-6;

    [JsonConstructor]
    public TrackPosition(string trackEdgeId, double offsetMeters)
    {
        TrackEdgeId = trackEdgeId;
        OffsetMeters = offsetMeters;
    }

    public string TrackEdgeId { get; }
    public double OffsetMeters { get; }

    public bool Equals(TrackPosition other) => ApproximatelyEquals(other, DefaultToleranceMeters);

    public bool ApproximatelyEquals(TrackPosition other, double toleranceMeters = DefaultToleranceMeters) =>
        toleranceMeters >= 0
        && string.Equals(TrackEdgeId, other.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && double.IsFinite(OffsetMeters)
        && double.IsFinite(other.OffsetMeters)
        && Math.Abs(OffsetMeters - other.OffsetMeters) <= toleranceMeters;

    public override bool Equals(object? obj) => obj is TrackPosition other && Equals(other);

    // Offset is intentionally omitted: tolerance-based equality cannot safely derive a granular hash.
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(TrackEdgeId ?? string.Empty);

    public static bool operator ==(TrackPosition left, TrackPosition right) => left.Equals(right);
    public static bool operator !=(TrackPosition left, TrackPosition right) => !left.Equals(right);

    public override string ToString() => $"{TrackEdgeId}@{OffsetMeters:0.###}m";
}

/// <summary>ServiceRoute 的有序單一 edge traversal。</summary>
public readonly record struct DirectedTrackTraversal(
    string TrackEdgeId,
    TraversalDirection Direction);

/// <summary>
/// 道岔／交叉點的明確可行轉向。未列出任何 connection 的 node 維持 graph 預設連通；
/// 一旦某 node 有列出 connection，進入該 node 的 traversal 僅能接到列出的下一段。
/// </summary>
public sealed record DirectedTrackConnectionDefinition(
    string FromTrackEdgeId,
    TraversalDirection FromDirection,
    string ToTrackEdgeId,
    TraversalDirection ToDirection);

/// <summary>
/// 可先由 persistence/UI 建立的 topology 原始資料。建立 InfrastructureGraphV4 前必須經
/// InfrastructureValidator 驗證；graph 本身只保留已驗證的索引資料。
/// </summary>
public sealed record TopologyInfrastructureDefinition
{
    public IReadOnlyList<TrackNodeDefinition> Nodes { get; init; } = [];
    public IReadOnlyList<TrackEdgeDefinition> Edges { get; init; } = [];
    public IReadOnlyList<StationDefinitionV4> Stations { get; init; } = [];
    public IReadOnlyList<PlatformDefinitionV4> Platforms { get; init; } = [];
    public IReadOnlyList<ConflictResourceDefinition> Resources { get; init; } = [];
    public IReadOnlyList<TrackSpeedLimitDefinition> SpeedLimits { get; init; } = [];
    public IReadOnlyList<TrackGradientSegment> Gradients { get; init; } = [];
    public IReadOnlyList<TrackCurveSegment> Curves { get; init; } = [];
    public IReadOnlyList<DirectedTrackConnectionDefinition> DirectedConnections { get; init; } = [];
    public IReadOnlyList<TurnbackFacilityDefinition> TurnbackFacilities { get; init; } = [];
    public IReadOnlyList<TurnbackOperationDefinition> TurnbackOperations { get; init; } = [];
    public IReadOnlyList<PassingFacilityDefinition> PassingFacilities { get; init; } = [];
    public IReadOnlyList<PassingOperationDefinition> PassingOperations { get; init; } = [];
    public IReadOnlyList<StationOperationDefinition> StationOperations { get; init; } = [];
}
