namespace MrtRouteSimulator.Engine;

/// <summary>
/// Schema 8 結果層使用的停站描述。<see cref="Position"/> 是實體判定位置；
/// <see cref="ProjectedChainageMeters"/> 只供既有一維圖表、CSV 與統計呈現。
/// </summary>
public sealed record TopologyResultStop(
    string StationId,
    string StationName,
    string PlatformId,
    TrackPosition Position,
    int TraversalIndex,
    double ProjectedChainageMeters,
    double DefaultDwellTimeSeconds = 0);

/// <summary>
/// 將 topology-native V2 的 resolved service stops 提供給結果層，避免結果、時刻表或統計
/// 重新要求 compatibility Route。所有顯示里程皆由既有 cursor / service traversal 衍生。
/// </summary>
public sealed class TopologyResultContext
{
    private readonly IReadOnlyDictionary<TrainDirection, IReadOnlyList<TopologyResultStop>> stopsByDirection;
    private readonly IReadOnlyDictionary<TrainDirection, double> routeLengthsByDirection;
    private readonly IReadOnlyDictionary<string, string> platformStationIds;

    private TopologyResultContext(
        IReadOnlyList<TopologyResultStop> outboundStops,
        IReadOnlyList<TopologyResultStop> inboundStops,
        double outboundRouteLengthMeters,
        double inboundRouteLengthMeters,
        IReadOnlyDictionary<string, string> platformStationIds)
    {
        stopsByDirection = new Dictionary<TrainDirection, IReadOnlyList<TopologyResultStop>>
        {
            [TrainDirection.Outbound] = outboundStops,
            [TrainDirection.Inbound] = inboundStops
        };
        routeLengthsByDirection = new Dictionary<TrainDirection, double>
        {
            [TrainDirection.Outbound] = outboundRouteLengthMeters,
            [TrainDirection.Inbound] = inboundRouteLengthMeters
        };
        this.platformStationIds = platformStationIds;
    }

    public IReadOnlyList<TopologyResultStop> GetStops(TrainDirection direction) =>
        stopsByDirection.TryGetValue(direction, out var stops)
            ? stops
            : throw new ArgumentOutOfRangeException(nameof(direction));

    /// <summary>
    /// 以 topology 實體月台辨識站事件。TrainCenter 停靠時，事件 PositionMeters 是車頭位置，
    /// 因此不能用停點投影里程的固定容差比對；同一車次與方向已由呼叫端先行篩選。
    /// </summary>
    public bool MatchesStationEvent(
        TrainDirection direction,
        string stationId,
        SimulationEvent simulationEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentNullException.ThrowIfNull(simulationEvent);
        if (simulationEvent.Direction != direction)
        {
            return false;
        }

        var stop = GetStops(direction).FirstOrDefault(item =>
            item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase));
        if (stop is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(simulationEvent.StationId))
            return simulationEvent.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(simulationEvent.PlatformId)
            && simulationEvent.PlatformId.Equals(stop.PlatformId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(simulationEvent.PlatformId)
            && platformStationIds.TryGetValue(simulationEvent.PlatformId, out var eventStationId)
            && eventStationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 只供舊結果版面使用的顯示 adapter；不是 V2 physical runtime 輸入。
    /// 下行沿 route-local chainage 顯示，上行轉為與 SimulationWorld.PositionMeters
    /// 相同的全線方向座標，讓事件邊界比對仍能分開保留方向。
    /// </summary>
    public IReadOnlyList<Station> GetDisplayStations(TrainDirection direction) =>
        GetStops(direction)
            .Select(stop => new Station(
                stop.StationId,
                stop.StationName,
                direction == TrainDirection.Outbound
                    ? stop.ProjectedChainageMeters
                    : routeLengthsByDirection[direction] - stop.ProjectedChainageMeters,
                stop.DefaultDwellTimeSeconds))
            .ToArray();

    internal static TopologyResultContext Create(
        InfrastructureGraphV4 infrastructure,
        ResolvedRunRouteContext outbound,
        ResolvedRunRouteContext inbound) =>
        new(
            CreateStops(infrastructure, outbound),
            CreateStops(infrastructure, inbound),
            GetRouteLengthMeters(outbound),
            GetRouteLengthMeters(inbound),
            infrastructure.Platforms.Values.ToDictionary(
                platform => platform.PlatformId,
                platform => platform.StationId,
                StringComparer.OrdinalIgnoreCase));

    private static double GetRouteLengthMeters(ResolvedRunRouteContext context) =>
        context.MainMovementPlan.Legs
            .SelectMany(leg => leg.Traversals)
            .Sum(traversal => traversal.LengthMeters);

    private static IReadOnlyList<TopologyResultStop> CreateStops(
        InfrastructureGraphV4 infrastructure,
        ResolvedRunRouteContext context) =>
        context.Stops.Select(stop => new TopologyResultStop(
                stop.StationId,
                infrastructure.Stations[stop.StationId].Name,
                stop.PlatformId,
                stop.Position,
                stop.TraversalIndex,
                stop.ChainageMeters,
                infrastructure.Stations[stop.StationId].DefaultDwellTimeSeconds))
            .ToArray();
}
