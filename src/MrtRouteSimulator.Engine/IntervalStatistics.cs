using System.Globalization;
using System.Text;

namespace MrtRouteSimulator.Engine;

/// <summary>
/// V2 區間統計的查詢條件。未提供的條件不限制結果。
/// </summary>
public sealed record IntervalStatisticsFilter(
    TrainDirection? Direction = null,
    string? VehicleId = null,
    string? ServiceRunId = null,
    string? VehicleTypeId = null,
    string? ServiceClassId = null,
    string? ServicePatternId = null,
    double? StartSimulationTimeSeconds = null,
    double? EndSimulationTimeSeconds = null,
    bool IncludeInProgress = true)
{
    public bool Matches(IntervalStatistic item)
    {
        if (Direction is not null && item.Direction != Direction)
        {
            return false;
        }

        if (!MatchesText(VehicleId, item.VehicleId)
            || !MatchesText(ServiceRunId, item.ServiceRunId)
            || !MatchesText(VehicleTypeId, item.VehicleTypeId)
            || !MatchesText(ServiceClassId, item.ServiceClassId)
            || !MatchesText(ServicePatternId, item.ServicePatternId))
        {
            return false;
        }

        if (!IncludeInProgress && !item.IsComplete)
        {
            return false;
        }

        var itemStart = item.DepartureTimeSeconds ?? item.FirstObservedTimeSeconds;
        var itemEnd = item.ArrivalTimeSeconds ?? item.LastObservedTimeSeconds;
        if (StartSimulationTimeSeconds is { } start && itemEnd < start)
        {
            return false;
        }

        if (EndSimulationTimeSeconds is { } end && itemStart > end)
        {
            return false;
        }

        return true;
    }

    public bool Matches(JourneyStatistic item)
    {
        if (Direction is not null && item.Direction != Direction)
        {
            return false;
        }

        if (!MatchesText(VehicleId, item.VehicleId)
            || !MatchesText(ServiceRunId, item.ServiceRunId)
            || !MatchesText(VehicleTypeId, item.VehicleTypeId)
            || !MatchesText(ServiceClassId, item.ServiceClassId)
            || !MatchesText(ServicePatternId, item.ServicePatternId))
        {
            return false;
        }

        if (!IncludeInProgress && !item.IsComplete)
        {
            return false;
        }

        var itemStart = item.DepartureTimeSeconds ?? item.FirstObservedTimeSeconds;
        var itemEnd = item.ArrivalTimeSeconds ?? item.LastObservedTimeSeconds;
        if (StartSimulationTimeSeconds is { } start && itemEnd < start)
        {
            return false;
        }

        if (EndSimulationTimeSeconds is { } end && itemStart > end)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesText(string? filter, string? value) =>
        filter is null || string.Equals(filter.Trim(), value, StringComparison.Ordinal);
}

/// <summary>各 V2 區間受到的控制事件摘要；僅依事件型別，不解析中文訊息。</summary>
public sealed record IntervalControlEventSummary(
    int TotalCount,
    IReadOnlyDictionary<SimulationEventType, int> CountsByType,
    IReadOnlyList<SimulationEventType> EventTypes)
{
    public static IntervalControlEventSummary Empty { get; } =
        new(0, new Dictionary<SimulationEventType, int>(), Array.Empty<SimulationEventType>());

    public bool HasControlEvent => TotalCount > 0;
}

/// <summary>單一車次在相鄰車站之間的 V2 實際統計。</summary>
public sealed record IntervalStatistic
{
    public required string VehicleId { get; init; }
    public required string ServiceRunId { get; init; }
    public string? VehicleTypeId { get; init; }
    public required string ServiceClassId { get; init; }
    public required string ServicePatternId { get; init; }
    public required TrainDirection Direction { get; init; }
    public required string FromStationId { get; init; }
    public required string FromStationName { get; init; }
    public required string ToStationId { get; init; }
    public required string ToStationName { get; init; }
    public required double DistanceMeters { get; init; }
    public double? DepartureTimeSeconds { get; init; }
    public double? ArrivalTimeSeconds { get; init; }
    public double? TravelTimeSeconds { get; init; }
    public double? AverageSpeedMetersPerSecond { get; init; }
    public double? PeakSpeedMetersPerSecond { get; init; }
    public double? EntrySpeedMetersPerSecond { get; init; }
    public double? ExitSpeedMetersPerSecond { get; init; }
    public double? MinimumEffectiveSpeedLimitMetersPerSecond { get; init; }
    public bool? IsScheduledStop { get; init; }
    public bool IsComplete { get; init; }
    public double FirstObservedTimeSeconds { get; init; }
    public double LastObservedTimeSeconds { get; init; }
    public IReadOnlyDictionary<OperationalPhase, double> PhaseSeconds { get; init; } =
        new Dictionary<OperationalPhase, double>();
    public IntervalControlEventSummary ControlEvents { get; init; } = IntervalControlEventSummary.Empty;

    public string Status => IsComplete ? "完成" : "運行中";

    public double? ControlLimitedSeconds { get; init; }
}

/// <summary>單一車次從起站發車至終站抵達的 V2 實際平均速率。</summary>
public sealed record JourneyStatistic
{
    public required string VehicleId { get; init; }
    public required string ServiceRunId { get; init; }
    public string? VehicleTypeId { get; init; }
    public required string ServiceClassId { get; init; }
    public required string ServicePatternId { get; init; }
    public required TrainDirection Direction { get; init; }
    public required string OriginStationId { get; init; }
    public required string OriginStationName { get; init; }
    public required string TerminalStationId { get; init; }
    public required string TerminalStationName { get; init; }
    public required double DistanceMeters { get; init; }
    public double? DepartureTimeSeconds { get; init; }
    public double? ArrivalTimeSeconds { get; init; }
    public double? TravelTimeSeconds { get; init; }
    public double? AverageSpeedMetersPerSecond { get; init; }
    public bool IsComplete { get; init; }
    public double FirstObservedTimeSeconds { get; init; }
    public double LastObservedTimeSeconds { get; init; }

    public string Status => IsComplete ? "完成" : "運行中";
}

/// <summary>同一車站區間（僅使用完成樣本）的統計彙總。</summary>
public sealed record IntervalStatisticsSummary(
    TrainDirection Direction,
    string FromStationId,
    string FromStationName,
    string ToStationId,
    string ToStationName,
    int CompletedCount,
    double? AverageTravelTimeSeconds,
    double? MinimumTravelTimeSeconds,
    double? MaximumTravelTimeSeconds,
    double? P95TravelTimeSeconds)
{
    public bool HasEnoughSamples => CompletedCount > 0;
}

/// <summary>區間統計結果，將完成區間與運行中區間明確分離。</summary>
public sealed record IntervalStatisticsResult(
    IReadOnlyList<IntervalStatistic> CompletedIntervals,
    IReadOnlyList<IntervalStatistic> InProgressIntervals,
    IReadOnlyList<IntervalStatisticsSummary> Summaries,
    IntervalStatisticsFilter Filter)
{
    public IReadOnlyList<JourneyStatistic> JourneyStatistics { get; init; } = [];

    public IReadOnlyList<IntervalStatistic> Completed => CompletedIntervals;

    public IReadOnlyList<IntervalStatistic> InProgress => InProgressIntervals;

    public IReadOnlyList<IntervalStatistic> AllIntervals =>
        CompletedIntervals.Concat(InProgressIntervals).ToArray();

    public int CompletedCount => CompletedIntervals.Count;

    public int InProgressCount => InProgressIntervals.Count;
}

/// <summary>
/// 由 V2 軌跡取樣及事件建立相鄰車站區間統計。
/// </summary>
public static class IntervalStatistics
{
    private const double PositionEpsilon = 1e-5;
    private const double TimeEpsilon = 1e-7;

    private static readonly SimulationEventType[] ControlEventTypes =
    [
        SimulationEventType.ControlBraking,
        SimulationEventType.SafetyStatusChanged,
        SimulationEventType.PredictedCollision,
        SimulationEventType.ObstacleEmergencyStop,
        SimulationEventType.Collision,
        SimulationEventType.BrakingModeChanged
    ];

    public static IntervalStatisticsResult Analyze(
        Route route,
        IEnumerable<TrajectorySample> samples,
        IEnumerable<SimulationEvent>? events = null,
        IEnumerable<SpeedLimitSegment>? speedLimits = null,
        IntervalStatisticsFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        return AnalyzeCore(
            direction => direction == TrainDirection.Outbound
                ? route.Stations.ToArray()
                : route.Stations.Reverse().ToArray(),
            samples,
            events,
            speedLimits,
            filter,
            stationEventMatcher: null);
    }

    /// <summary>
    /// Schema 8 區間統計直接由 topology resolved stop context 建立。顯示座標是 cursor 對
    /// ServiceRoute 的衍生 chainage；本 overload 不建立 compatibility Route。
    /// </summary>
    public static IntervalStatisticsResult Analyze(
        TopologyResultContext topology,
        IEnumerable<TrajectorySample> samples,
        IEnumerable<SimulationEvent>? events = null,
        IntervalStatisticsFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return AnalyzeCore(
            topology.GetDisplayStations,
            samples,
            events,
            [],
            filter,
            (direction, stationId, simulationEvent) =>
                topology.MatchesStationEvent(direction, stationId, simulationEvent));
    }

    private static IntervalStatisticsResult AnalyzeCore(
        Func<TrainDirection, IReadOnlyList<Station>> stationResolver,
        IEnumerable<TrajectorySample> samples,
        IEnumerable<SimulationEvent>? events,
        IEnumerable<SpeedLimitSegment>? speedLimits,
        IntervalStatisticsFilter? filter,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher)
    {
        ArgumentNullException.ThrowIfNull(samples);
        filter ??= new IntervalStatisticsFilter();
        var allSamples = samples.ToArray();
        var allEvents = events?.ToArray() ?? [];
        var limits = speedLimits?.ToArray() ?? [];
        var raw = new List<IntervalStatistic>();

        foreach (var run in allSamples
                     .Where(item => !string.IsNullOrWhiteSpace(item.VehicleId)
                         && !string.IsNullOrWhiteSpace(item.ServiceRunId))
                     .GroupBy(item => (item.VehicleId, item.ServiceRunId, item.Direction)))
        {
            var ordered = run.OrderBy(item => item.SimulationTimeSeconds).ToArray();
            if (ordered.Length == 0)
            {
                continue;
            }

            var direction = run.Key.Direction;
            var stations = stationResolver(direction).ToArray();
            for (var index = 0; index + 1 < stations.Length; index++)
            {
                var from = stations[index];
                var to = stations[index + 1];
                var departure = FindDeparture(ordered, allEvents, from, direction, stationEventMatcher);
                var arrival = departure is not null
                    ? FindArrival(ordered, allEvents, to, direction, departure.TimeSeconds, stationEventMatcher)
                    : null;

                var currentSegment = ordered.Where(item =>
                        string.Equals(item.CurrentStationId, from.StationId, StringComparison.Ordinal)
                        && string.Equals(item.NextStationId, to.StationId, StringComparison.Ordinal))
                    .ToArray();
                var observedStart = currentSegment.Length > 0
                    ? currentSegment[0].SimulationTimeSeconds
                    : ordered[0].SimulationTimeSeconds;
                var observedEnd = currentSegment.Length > 0
                    ? currentSegment[^1].SimulationTimeSeconds
                    : ordered[^1].SimulationTimeSeconds;

                // 若取樣從區間中段開始，仍保留「運行中」，但不捏造未知的離站時間。
                if (departure is null && currentSegment.Length == 0)
                {
                    continue;
                }

                if (arrival is null)
                {
                    if (departure is null && currentSegment.Length == 0)
                    {
                        continue;
                    }

                    var partialSamples = currentSegment.Length > 0 ? currentSegment : ordered;
                    var startPosition = partialSamples.MinBy(item => item.SimulationTimeSeconds)?.PositionMeters
                        ?? from.PositionMeters;
                    var endPosition = partialSamples.MaxBy(item => item.SimulationTimeSeconds)?.PositionMeters
                        ?? startPosition;
                    var phaseSeconds = CalculatePhaseSeconds(
                        ordered,
                        departure?.TimeSeconds ?? observedStart,
                        null,
                        observedEnd);
                    raw.Add(CreateStatistic(
                        ordered,
                        from,
                        to,
                        direction,
                        departure,
                        null,
                        phaseSeconds,
                        allEvents,
                        limits,
                        stationEventMatcher,
                        startPosition,
                        endPosition,
                        isComplete: false,
                        observedStart,
                        observedEnd));
                    continue;
                }

                if (departure is null || arrival.TimeSeconds < departure.TimeSeconds - TimeEpsilon)
                {
                    continue;
                }

                var phases = CalculatePhaseSeconds(ordered, departure.TimeSeconds, arrival.TimeSeconds, observedEnd);
                raw.Add(CreateStatistic(
                    ordered,
                    from,
                    to,
                    direction,
                    departure,
                    arrival,
                    phases,
                    allEvents,
                    limits,
                    stationEventMatcher,
                    from.PositionMeters,
                    to.PositionMeters,
                    isComplete: true,
                    observedStart,
                    observedEnd));
            }
        }

        var journeys = allSamples
            .Where(item => !string.IsNullOrWhiteSpace(item.VehicleId)
                && !string.IsNullOrWhiteSpace(item.ServiceRunId))
            .GroupBy(item => (item.VehicleId, item.ServiceRunId, item.Direction))
            .Select(run => CreateJourneyStatistic(
                stationResolver,
                run.OrderBy(item => item.SimulationTimeSeconds).ToArray(),
                allEvents,
                stationEventMatcher))
            .Where(item => item is not null)
            .Cast<JourneyStatistic>()
            .Where(filter.Matches)
            .OrderBy(item => item.DepartureTimeSeconds ?? item.FirstObservedTimeSeconds)
            .ThenBy(item => item.VehicleId, StringComparer.Ordinal)
            .ToArray();
        var filtered = raw.Where(filter.Matches).ToArray();
        var completed = filtered.Where(item => item.IsComplete).ToArray();
        var inProgress = filtered.Where(item => !item.IsComplete).ToArray();
        var summaries = completed
            .GroupBy(item => (item.Direction, item.FromStationId, item.FromStationName, item.ToStationId, item.ToStationName))
            .Select(group => CreateSummary(group.Key, group))
            .OrderBy(item => item.Direction)
            .ThenBy(item => item.FromStationId, StringComparer.Ordinal)
            .ToArray();
        return new IntervalStatisticsResult(completed, inProgress, summaries, filter)
        {
            JourneyStatistics = journeys
        };
    }

    public static string BuildCsv(IntervalStatisticsResult result, double displayClockStartSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder();
        builder.AppendLine("軟體版本,模型版本,車輛ID,車次ID,車型,服務類型,停站模式,方向,前站,後站,狀態,是否停站,距離(m),離站模擬秒,抵達模擬秒,離站顯示時間,抵達顯示時間,旅行時間(s),平均速度(km/h),峰值速度(km/h),進入速度(km/h),離開速度(km/h),區間最低有效速限(km/h),加速(s),巡航(s),惰行(s),減速(s),進站煞車(s),抵達(s),折返(s),其他相位(s),移動閉塞受限(s),控制事件數,控制事件型別");
        foreach (var item in result.AllIntervals.OrderBy(item => item.DepartureTimeSeconds ?? item.FirstObservedTimeSeconds).ThenBy(item => item.VehicleId, StringComparer.Ordinal))
        {
            builder.Append(Csv(ProductVersion.Current)).Append(',')
                .Append(Csv("V2 SimulationWorld")).Append(',')
                .Append(Csv(item.VehicleId)).Append(',')
                .Append(Csv(item.ServiceRunId)).Append(',')
                .Append(Csv(item.VehicleTypeId ?? string.Empty)).Append(',')
                .Append(Csv(item.ServiceClassId)).Append(',')
                .Append(Csv(item.ServicePatternId)).Append(',')
                .Append(item.Direction).Append(',')
                .Append(Csv($"{item.FromStationId} {item.FromStationName}")).Append(',')
                .Append(Csv($"{item.ToStationId} {item.ToStationName}")).Append(',')
                .Append(item.Status).Append(',')
                .Append(item.IsScheduledStop is null ? string.Empty : item.IsScheduledStop.Value ? "停站" : "通過").Append(',')
                .Append(Number(item.DistanceMeters)).Append(',')
                .Append(item.DepartureTimeSeconds is { } departure ? Number(departure) : string.Empty).Append(',')
                .Append(item.ArrivalTimeSeconds is { } arrival ? Number(arrival) : string.Empty).Append(',')
                .Append(Csv(item.DepartureTimeSeconds is { } d ? TrajectoryAnalysis.FormatClock(displayClockStartSeconds + d) : string.Empty)).Append(',')
                .Append(Csv(item.ArrivalTimeSeconds is { } a ? TrajectoryAnalysis.FormatClock(displayClockStartSeconds + a) : string.Empty)).Append(',')
                .Append(Number(item.TravelTimeSeconds)).Append(',')
                .Append(Number(ToKmh(item.AverageSpeedMetersPerSecond))).Append(',')
                .Append(Number(ToKmh(item.PeakSpeedMetersPerSecond))).Append(',')
                .Append(Number(ToKmh(item.EntrySpeedMetersPerSecond))).Append(',')
                .Append(Number(ToKmh(item.ExitSpeedMetersPerSecond))).Append(',')
                .Append(Number(ToKmh(item.MinimumEffectiveSpeedLimitMetersPerSecond))).Append(',')
                .Append(Phase(item, OperationalPhase.Accelerating)).Append(',')
                .Append(Phase(item, OperationalPhase.Cruising)).Append(',')
                .Append(Phase(item, OperationalPhase.Coasting)).Append(',')
                .Append(Phase(item, OperationalPhase.Braking)).Append(',')
                .Append(Phase(item, OperationalPhase.ApproachBraking)).Append(',')
                .Append(Phase(item, OperationalPhase.Arriving)).Append(',')
                .Append(Phase(item, OperationalPhase.Turning)).Append(',')
                .Append(Number(OtherPhaseSeconds(item))).Append(',')
                .Append(Number(item.ControlLimitedSeconds)).Append(',')
                .Append(item.ControlEvents.TotalCount).Append(',')
                .Append(Csv(string.Join('|', item.ControlEvents.EventTypes)))
                .AppendLine();
        }

        return builder.ToString();
    }

    public static string BuildJourneyCsv(IntervalStatisticsResult result, double displayClockStartSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder();
        builder.AppendLine("軟體版本,模型版本,車輛ID,車次ID,車型,服務類型,停站模式,方向,起站,終站,狀態,距離(m),起站離站模擬秒,終站抵達模擬秒,起站離站顯示時間,終站抵達顯示時間,全程旅行時間(s),起終站平均速度(km/h)");
        foreach (var item in result.JourneyStatistics)
        {
            builder.Append(Csv(ProductVersion.Current)).Append(',')
                .Append(Csv("V2 SimulationWorld")).Append(',')
                .Append(Csv(item.VehicleId)).Append(',')
                .Append(Csv(item.ServiceRunId)).Append(',')
                .Append(Csv(item.VehicleTypeId ?? string.Empty)).Append(',')
                .Append(Csv(item.ServiceClassId)).Append(',')
                .Append(Csv(item.ServicePatternId)).Append(',')
                .Append(item.Direction).Append(',')
                .Append(Csv($"{item.OriginStationId} {item.OriginStationName}")).Append(',')
                .Append(Csv($"{item.TerminalStationId} {item.TerminalStationName}")).Append(',')
                .Append(item.Status).Append(',')
                .Append(Number(item.DistanceMeters)).Append(',')
                .Append(item.DepartureTimeSeconds is { } departure ? Number(departure) : string.Empty).Append(',')
                .Append(item.ArrivalTimeSeconds is { } arrival ? Number(arrival) : string.Empty).Append(',')
                .Append(Csv(item.DepartureTimeSeconds is { } d ? TrajectoryAnalysis.FormatClock(displayClockStartSeconds + d) : string.Empty)).Append(',')
                .Append(Csv(item.ArrivalTimeSeconds is { } a ? TrajectoryAnalysis.FormatClock(displayClockStartSeconds + a) : string.Empty)).Append(',')
                .Append(Number(item.TravelTimeSeconds)).Append(',')
                .Append(Number(ToKmh(item.AverageSpeedMetersPerSecond)))
                .AppendLine();
        }

        return builder.ToString();
    }

    public static string BuildSummaryCsv(IntervalStatisticsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder("軟體版本,模型版本,方向,前站,後站,完成樣本數,平均旅行時間(s),最小旅行時間(s),最大旅行時間(s),第95百分位旅行時間(s)\r\n");
        foreach (var item in result.Summaries)
        {
            builder.Append(Csv(ProductVersion.Current)).Append(',')
                .Append(Csv("V2 SimulationWorld")).Append(',')
                .Append(item.Direction).Append(',')
                .Append(Csv($"{item.FromStationId} {item.FromStationName}")).Append(',')
                .Append(Csv($"{item.ToStationId} {item.ToStationName}")).Append(',')
                .Append(item.CompletedCount).Append(',')
                .Append(Number(item.AverageTravelTimeSeconds)).Append(',')
                .Append(Number(item.MinimumTravelTimeSeconds)).Append(',')
                .Append(Number(item.MaximumTravelTimeSeconds)).Append(',')
                .Append(Number(item.P95TravelTimeSeconds)).AppendLine();
        }

        return builder.ToString();
    }

    private static IntervalStatistic CreateStatistic(
        IReadOnlyList<TrajectorySample> samples,
        Station from,
        Station to,
        TrainDirection direction,
        Boundary? departure,
        Boundary? arrival,
        IReadOnlyDictionary<OperationalPhase, double> phaseSeconds,
        IReadOnlyList<SimulationEvent> events,
        IReadOnlyList<SpeedLimitSegment> speedLimits,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher,
        double startPosition,
        double endPosition,
        bool isComplete,
        double observedStart,
        double observedEnd)
    {
        var startTime = departure?.TimeSeconds;
        var endTime = arrival?.TimeSeconds;
        double? travelTime = startTime is { } start && endTime is { } end
            ? Math.Max(0, end - start)
            : null;
        var intervalSamples = samples.Where(item =>
                item.SimulationTimeSeconds >= (startTime ?? observedStart) - TimeEpsilon
                && item.SimulationTimeSeconds <= (endTime ?? observedEnd) + TimeEpsilon)
            .ToArray();
        var speeds = intervalSamples.Select(item => item.SpeedMetersPerSecond)
            .Append(departure?.SpeedMetersPerSecond ?? 0)
            .Append(arrival?.SpeedMetersPerSecond ?? 0)
            .Where(double.IsFinite)
            .Select(item => Math.Max(0, item))
            .ToArray();
        var distance = Math.Abs(to.PositionMeters - from.PositionMeters);
        var minimumSpeedLimit = GetMinimumSpeedLimit(speedLimits, direction, from.PositionMeters, to.PositionMeters);
        var scheduledStop = arrival?.IsScheduledStop ?? InferStop(intervalSamples, to.StationId);
        return new IntervalStatistic
        {
            VehicleId = samples[0].VehicleId,
            ServiceRunId = samples[0].ServiceRunId,
            VehicleTypeId = samples[0].VehicleTypeId,
            ServiceClassId = samples[0].ServiceClassId,
            ServicePatternId = samples[0].ServicePatternId,
            Direction = direction,
            FromStationId = from.StationId,
            FromStationName = from.StationName,
            ToStationId = to.StationId,
            ToStationName = to.StationName,
            DistanceMeters = isComplete ? distance : Math.Abs(endPosition - startPosition),
            DepartureTimeSeconds = startTime,
            ArrivalTimeSeconds = endTime,
            TravelTimeSeconds = travelTime,
            AverageSpeedMetersPerSecond = travelTime is > TimeEpsilon
                ? Math.Abs(endPosition - startPosition) / travelTime.Value
                : null,
            PeakSpeedMetersPerSecond = speeds.Length == 0 ? null : speeds.Max(),
            EntrySpeedMetersPerSecond = departure?.SpeedMetersPerSecond,
            ExitSpeedMetersPerSecond = arrival?.SpeedMetersPerSecond,
            MinimumEffectiveSpeedLimitMetersPerSecond = minimumSpeedLimit,
            IsScheduledStop = scheduledStop,
            IsComplete = isComplete,
            FirstObservedTimeSeconds = observedStart,
            LastObservedTimeSeconds = observedEnd,
            PhaseSeconds = phaseSeconds,
            ControlLimitedSeconds = CalculateConstraintSeconds(
                samples,
                startTime ?? observedStart,
                endTime ?? observedEnd,
                OperationalConstraint.MovingBlock),
            ControlEvents = SummarizeControlEvents(events, samples[0].VehicleId, direction, startTime ?? observedStart, endTime ?? observedEnd)
        };
    }

    private static JourneyStatistic? CreateJourneyStatistic(
        Func<TrainDirection, IReadOnlyList<Station>> stationResolver,
        IReadOnlyList<TrajectorySample> samples,
        IReadOnlyList<SimulationEvent> events,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        var direction = samples[0].Direction;
        var stations = stationResolver(direction);
        if (stations.Count == 0)
        {
            return null;
        }

        var origin = stations[0];
        var terminal = stations[^1];
        var departure = FindDeparture(samples, events, origin, direction, stationEventMatcher);
        var arrival = departure is null
            ? null
            : FindArrival(samples, events, terminal, direction, departure.TimeSeconds, stationEventMatcher);
        var firstObserved = samples[0];
        var lastObserved = samples[^1];
        double? travelTime = departure is { } start && arrival is { } end
            ? Math.Max(0, end.TimeSeconds - start.TimeSeconds)
            : null;
        var isComplete = departure is not null && arrival is not null;
        var travelledDistance = isComplete
            ? Math.Abs(terminal.PositionMeters - origin.PositionMeters)
            : Math.Abs(lastObserved.PositionMeters - firstObserved.PositionMeters);
        return new JourneyStatistic
        {
            VehicleId = samples[0].VehicleId,
            ServiceRunId = samples[0].ServiceRunId,
            VehicleTypeId = samples[0].VehicleTypeId,
            ServiceClassId = samples[0].ServiceClassId,
            ServicePatternId = samples[0].ServicePatternId,
            Direction = direction,
            OriginStationId = origin.StationId,
            OriginStationName = origin.StationName,
            TerminalStationId = terminal.StationId,
            TerminalStationName = terminal.StationName,
            DistanceMeters = travelledDistance,
            DepartureTimeSeconds = departure?.TimeSeconds,
            ArrivalTimeSeconds = arrival?.TimeSeconds,
            TravelTimeSeconds = travelTime,
            AverageSpeedMetersPerSecond = travelTime is > TimeEpsilon
                ? travelledDistance / travelTime.Value
                : null,
            IsComplete = isComplete,
            FirstObservedTimeSeconds = firstObserved.SimulationTimeSeconds,
            LastObservedTimeSeconds = lastObserved.SimulationTimeSeconds
        };
    }

    private static double CalculateConstraintSeconds(
        IReadOnlyList<TrajectorySample> samples,
        double start,
        double end,
        OperationalConstraint constraint)
    {
        if (end <= start + TimeEpsilon || samples.Count < 2) return 0;
        var ordered = samples.OrderBy(item => item.SimulationTimeSeconds).ToArray();
        var total = 0d;
        for (var index = 0; index + 1 < ordered.Length; index++)
        {
            var left = ordered[index];
            var right = ordered[index + 1];
            var overlapStart = Math.Max(start, left.SimulationTimeSeconds);
            var overlapEnd = Math.Min(end, right.SimulationTimeSeconds);
            if (overlapEnd <= overlapStart + TimeEpsilon
                || (left.Constraints & constraint) == 0)
            {
                continue;
            }
            total += overlapEnd - overlapStart;
        }
        return Math.Clamp(total, 0, Math.Max(0, end - start));
    }

    private static Boundary? FindDeparture(
        IReadOnlyList<TrajectorySample> samples,
        IReadOnlyList<SimulationEvent> events,
        Station station,
        TrainDirection direction,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher)
    {
        var exact = events.Where(item => item.VehicleId == samples[0].VehicleId
                && item.ServiceRunId == samples[0].ServiceRunId
                && item.Direction == direction
                && item.EventType == SimulationEventType.Departure
                && (stationEventMatcher is not null
                    ? stationEventMatcher(direction, station.StationId, item)
                    : Math.Abs(item.PositionMeters - station.PositionMeters) <= PositionEpsilon)
                && item.SimulationTimeSeconds <= samples[^1].SimulationTimeSeconds + TimeEpsilon)
            .OrderBy(item => item.SimulationTimeSeconds)
            .Select(item => new Boundary(item.SimulationTimeSeconds, item.SpeedMetersPerSecond, null))
            .FirstOrDefault();
        // A topology result must be event-authoritative. The train head can pass
        // the projected center before a TrainCenter stop has actually generated
        // its Arrival/Departure event, so a positional fallback would mark an
        // incomplete interval as finished. Legacy Route results retain the
        // trajectory-only fallback for backward compatibility.
        return stationEventMatcher is not null
            ? exact
            : exact ?? FindBoundary(samples, station.PositionMeters, direction, departure: true);
    }

    private static Boundary? FindArrival(
        IReadOnlyList<TrajectorySample> samples,
        IReadOnlyList<SimulationEvent> events,
        Station station,
        TrainDirection direction,
        double departureTime,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher)
    {
        var exact = events.Where(item => item.VehicleId == samples[0].VehicleId
                && item.ServiceRunId == samples[0].ServiceRunId
                && item.Direction == direction
                && (item.EventType == SimulationEventType.Arrival || item.EventType == SimulationEventType.StationPassed)
                && (stationEventMatcher is not null
                    ? stationEventMatcher(direction, station.StationId, item)
                    : Math.Abs(item.PositionMeters - station.PositionMeters) <= PositionEpsilon)
                && item.SimulationTimeSeconds >= departureTime - TimeEpsilon)
            .OrderBy(item => item.SimulationTimeSeconds)
            .Select(item => new Boundary(
                item.SimulationTimeSeconds,
                item.SpeedMetersPerSecond,
                item.EventType == SimulationEventType.Arrival))
            .FirstOrDefault();
        return stationEventMatcher is not null
            ? exact
            : exact ?? FindBoundary(samples, station.PositionMeters, direction, departure: false, departureTime);
    }

    private static Boundary? FindBoundary(
        IReadOnlyList<TrajectorySample> samples,
        double stationPosition,
        TrainDirection direction,
        bool departure,
        double minimumTime = double.NegativeInfinity)
    {
        for (var index = 0; index < samples.Count; index++)
        {
            var current = samples[index];
            if (current.SimulationTimeSeconds < minimumTime - TimeEpsilon)
            {
                continue;
            }

            if (departure && IsAt(current.PositionMeters, stationPosition)
                && (index + 1 == samples.Count || IsAhead(samples[index + 1].PositionMeters, stationPosition, direction)))
            {
                return new Boundary(current.SimulationTimeSeconds, Math.Max(0, current.SpeedMetersPerSecond), InferStop(samples, current.CurrentStationId));
            }

            if (!departure && IsAt(current.PositionMeters, stationPosition))
            {
                return new Boundary(current.SimulationTimeSeconds, Math.Max(0, current.SpeedMetersPerSecond), InferStop(samples, current.CurrentStationId));
            }

            if (index + 1 >= samples.Count)
            {
                continue;
            }

            var next = samples[index + 1];
            if (!Crosses(current.PositionMeters, next.PositionMeters, stationPosition, direction))
            {
                continue;
            }

            var delta = next.PositionMeters - current.PositionMeters;
            var fraction = Math.Abs(delta) <= PositionEpsilon
                ? 0
                : (stationPosition - current.PositionMeters) / delta;
            fraction = Math.Clamp(fraction, 0, 1);
            return new Boundary(
                current.SimulationTimeSeconds + (next.SimulationTimeSeconds - current.SimulationTimeSeconds) * fraction,
                Math.Max(0, current.SpeedMetersPerSecond + (next.SpeedMetersPerSecond - current.SpeedMetersPerSecond) * fraction),
                null);
        }

        return null;
    }

    private static IReadOnlyDictionary<OperationalPhase, double> CalculatePhaseSeconds(
        IReadOnlyList<TrajectorySample> samples,
        double start,
        double? end,
        double observedEnd)
    {
        var finish = end ?? observedEnd;
        var result = Enum.GetValues<OperationalPhase>().ToDictionary(item => item, _ => 0d);
        if (finish <= start + TimeEpsilon)
        {
            return result;
        }

        for (var index = 0; index + 1 < samples.Count; index++)
        {
            var left = samples[index];
            var right = samples[index + 1];
            var overlap = Math.Min(finish, right.SimulationTimeSeconds)
                - Math.Max(start, left.SimulationTimeSeconds);
            if (overlap > TimeEpsilon)
            {
                result[left.Phase] += overlap;
            }
        }

        return result;
    }

    private static IntervalControlEventSummary SummarizeControlEvents(
        IReadOnlyList<SimulationEvent> events,
        string vehicleId,
        TrainDirection direction,
        double start,
        double end)
    {
        var selected = events.Where(item => item.VehicleId == vehicleId
                && item.Direction == direction
                && ControlEventTypes.Contains(item.EventType)
                && item.SimulationTimeSeconds >= start - TimeEpsilon
                && item.SimulationTimeSeconds <= end + TimeEpsilon)
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();
        var counts = selected.GroupBy(item => item.EventType)
            .ToDictionary(group => group.Key, group => group.Count());
        return selected.Length == 0
            ? IntervalControlEventSummary.Empty
            : new IntervalControlEventSummary(selected.Length, counts, counts.Keys.OrderBy(item => item).ToArray());
    }

    private static double? GetMinimumSpeedLimit(
        IReadOnlyList<SpeedLimitSegment> limits,
        TrainDirection direction,
        double firstPosition,
        double lastPosition)
    {
        if (limits.Count == 0)
        {
            return null;
        }

        var start = Math.Min(firstPosition, lastPosition);
        var end = Math.Max(firstPosition, lastPosition);
        var applicable = limits.Where(limit =>
                (limit.Direction == SpeedLimitDirection.Both
                    || limit.Direction == SpeedLimitDirection.Outbound && direction == TrainDirection.Outbound
                    || limit.Direction == SpeedLimitDirection.Inbound && direction == TrainDirection.Inbound)
                && limit.EndPositionMeters >= start - PositionEpsilon
                && limit.StartPositionMeters <= end + PositionEpsilon)
            .Select(item => item.LimitMetersPerSecond)
            .Where(double.IsFinite)
            .ToArray();
        return applicable.Length == 0 ? null : applicable.Min();
    }

    private static IntervalStatisticsSummary CreateSummary(
        (TrainDirection Direction, string FromStationId, string FromStationName, string ToStationId, string ToStationName) key,
        IEnumerable<IntervalStatistic> items)
    {
        var times = items.Select(item => item.TravelTimeSeconds)
            .Where(item => item is not null && double.IsFinite(item.Value))
            .Select(item => item!.Value)
            .OrderBy(item => item)
            .ToArray();
        return new IntervalStatisticsSummary(
            key.Direction,
            key.FromStationId,
            key.FromStationName,
            key.ToStationId,
            key.ToStationName,
            times.Length,
            times.Length == 0 ? null : times.Average(),
            times.Length == 0 ? null : times[0],
            times.Length == 0 ? null : times[^1],
            times.Length == 0 ? null : times[Math.Clamp((int)Math.Ceiling(times.Length * 0.95) - 1, 0, times.Length - 1)]);
    }

    private static bool? InferStop(IEnumerable<TrajectorySample> samples, string stationId)
    {
        var sample = samples.FirstOrDefault(item => string.Equals(item.CurrentStationId, stationId, StringComparison.Ordinal));
        return sample?.Phase is OperationalPhase.Arriving or OperationalPhase.Dwelling or OperationalPhase.Turning
            ? true
            : sample is null ? null : false;
    }

    private static bool IsAt(double position, double stationPosition) =>
        Math.Abs(position - stationPosition) <= PositionEpsilon;

    private static bool IsAhead(double position, double stationPosition, TrainDirection direction) =>
        direction == TrainDirection.Outbound
            ? position > stationPosition + PositionEpsilon
            : position < stationPosition - PositionEpsilon;

    private static bool Crosses(double first, double second, double stationPosition, TrainDirection direction) =>
        direction == TrainDirection.Outbound
            ? first < stationPosition - PositionEpsilon && second >= stationPosition - PositionEpsilon
            : first > stationPosition + PositionEpsilon && second <= stationPosition + PositionEpsilon;

    private static double? ToKmh(double? metersPerSecond) => metersPerSecond is null ? null : metersPerSecond.Value * 3.6;

    private static double Phase(IntervalStatistic item, OperationalPhase phase) =>
        item.PhaseSeconds.TryGetValue(phase, out var seconds) ? seconds : 0;

    private static double OtherPhaseSeconds(IntervalStatistic item) =>
        item.PhaseSeconds.Where(pair => pair.Key is not OperationalPhase.Accelerating
                and not OperationalPhase.Cruising
                and not OperationalPhase.Coasting
                and not OperationalPhase.Braking
                and not OperationalPhase.ApproachBraking
                and not OperationalPhase.Arriving
                and not OperationalPhase.Turning)
            .Sum(pair => pair.Value);

    private static string Number(double? value) =>
        value is null || !double.IsFinite(value.Value) ? string.Empty : value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record Boundary(double TimeSeconds, double SpeedMetersPerSecond, bool? IsScheduledStop);
}
