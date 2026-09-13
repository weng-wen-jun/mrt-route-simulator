namespace MrtRouteSimulator.Engine;

/// <summary>
/// Schema 8 結果層使用的停站描述。<see cref="Position"/> 是實體判定位置；
/// <see cref="ProjectedChainageMeters"/> 只供既有一維圖表、CSV 與統計呈現。
/// </summary>
public sealed record TopologyResultStop(
    string StationId,
    string StationName,
    TrackPosition Position,
    int TraversalIndex,
    double ProjectedChainageMeters);

/// <summary>
/// 將 topology-native V2 的 resolved service stops 提供給結果層，避免結果、時刻表或統計
/// 重新要求 compatibility Route。所有顯示里程皆由既有 cursor / service traversal 衍生。
/// </summary>
public sealed class TopologyResultContext
{
    private readonly IReadOnlyDictionary<TrainDirection, IReadOnlyList<TopologyResultStop>> stopsByDirection;

    private TopologyResultContext(
        IReadOnlyList<TopologyResultStop> outboundStops,
        IReadOnlyList<TopologyResultStop> inboundStops)
    {
        stopsByDirection = new Dictionary<TrainDirection, IReadOnlyList<TopologyResultStop>>
        {
            [TrainDirection.Outbound] = outboundStops,
            [TrainDirection.Inbound] = inboundStops
        };
    }

    public IReadOnlyList<TopologyResultStop> GetStops(TrainDirection direction) =>
        stopsByDirection.TryGetValue(direction, out var stops)
            ? stops
            : throw new ArgumentOutOfRangeException(nameof(direction));

    /// <summary>只供舊結果版面使用的顯示 adapter；不是 V2 physical runtime 輸入。</summary>
    public IReadOnlyList<Station> GetDisplayStations(TrainDirection direction) =>
        GetStops(direction)
            .Select(stop => new Station(
                stop.StationId,
                stop.StationName,
                stop.ProjectedChainageMeters,
                0))
            .ToArray();

    internal static TopologyResultContext Create(
        InfrastructureGraphV4 infrastructure,
        ResolvedRunRouteContext outbound,
        ResolvedRunRouteContext inbound) =>
        new(CreateStops(infrastructure, outbound), CreateStops(infrastructure, inbound));

    private static IReadOnlyList<TopologyResultStop> CreateStops(
        InfrastructureGraphV4 infrastructure,
        ResolvedRunRouteContext context) =>
        context.Stops.Select(stop => new TopologyResultStop(
                stop.StationId,
                infrastructure.Stations[stop.StationId].Name,
                stop.Position,
                stop.TraversalIndex,
                stop.ChainageMeters))
            .ToArray();
}
