namespace MrtRouteSimulator.Engine;

/// <summary>由 Schema 7 派車計畫及實際／計畫事件組成的單一進出站時刻列。</summary>
public sealed record OperationsTimetableEntry(
    string VehicleId,
    string ServiceRunId,
    TrainDirection Direction,
    string ServiceTypeId,
    string StopPatternId,
    string VehicleTypeId,
    string StationId,
    string StationName,
    double PositionMeters,
    double? PlannedArrivalTimeSeconds,
    double? PlannedDepartureTimeSeconds,
    double? ActualArrivalTimeSeconds,
    double? ActualDepartureTimeSeconds,
    double? ActualDwellSeconds,
    double? DelaySeconds,
    string Status,
    double? PlannedDwellSeconds = null,
    string? ServiceRouteId = null,
    string? OperatingLineId = null);

/// <summary>確保時刻表、動畫與運行圖共用 SimulationWorld 的結構化事件。</summary>
public static class OperationsTimetable
{
    private const double PositionToleranceMeters = 0.6;

    public static string BuildCsv(IEnumerable<OperationsTimetableEntry> entries, double displayClockStartSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("軟體版本,車輛ID,車次ID,營運路徑ID,營運路線ID,方向,車站ID,車站名稱,表定到站秒,表定發車秒,實際到站秒,實際發車秒,表定停站秒,實際停站秒,表定發車時刻,實際到站時刻,實際發車時刻,延誤秒,狀態");
        foreach (var row in entries)
        {
            var cells = new[] { ProductVersion.Current, row.VehicleId, row.ServiceRunId,
                row.ServiceRouteId ?? "", row.OperatingLineId ?? "", row.Direction.ToString(), row.StationId, row.StationName,
                Number(row.PlannedArrivalTimeSeconds), Number(row.PlannedDepartureTimeSeconds),
                Number(row.ActualArrivalTimeSeconds), Number(row.ActualDepartureTimeSeconds),
                Number(row.PlannedDwellSeconds), Number(row.ActualDwellSeconds), Clock(row.PlannedDepartureTimeSeconds),
                Clock(row.ActualArrivalTimeSeconds), Clock(row.ActualDepartureTimeSeconds), Number(row.DelaySeconds), row.Status };
            builder.AppendLine(string.Join(',', cells.Select(value => "\"" + value.Replace("\"", "\"\"") + "\"")));
        }
        return builder.ToString();
        static string Number(double? value) => value?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        string Clock(double? value) => value is { } seconds ? TrajectoryAnalysis.FormatClock(displayClockStartSeconds + seconds) : "";
    }


    public static IReadOnlyList<OperationsTimetableEntry> Build(
        Route route,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<SimulationEvent>? plannedEvents,
        IEnumerable<SimulationEvent>? actualEvents,
        IEnumerable<ServicePattern>? servicePatterns = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        return BuildCore(
            run => run.Direction == TrainDirection.Outbound
                ? route.Stations.ToArray()
                : route.Stations.Reverse().ToArray(),
            dispatchPlan,
            plannedEvents,
            actualEvents,
            stationEventMatcher: null,
            turnbackOriginResolver: null,
            serviceRouteIdResolver: run => run.ServiceRouteId,
            operatingLineIdResolver: null,
            servicePatterns: servicePatterns);
    }

    /// <summary>
    /// Schema 8 時刻表直接使用 topology resolved stops。顯示公里里程只是 context 提供的
    /// projection derivative；此結果層不建立或查詢 compatibility Route。
    /// </summary>
    public static IReadOnlyList<OperationsTimetableEntry> Build(
        TopologyResultContext topology,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<SimulationEvent>? plannedEvents,
        IEnumerable<SimulationEvent>? actualEvents,
        IEnumerable<ServicePattern>? servicePatterns = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return BuildCore(
            topology.GetDisplayStations,
            dispatchPlan,
            plannedEvents,
            actualEvents,
            (direction, stationId, simulationEvent) =>
                topology.MatchesStationEvent(direction, stationId, simulationEvent),
            topology.GetTurnbackOrigin,
            topology.GetServiceRouteId,
            topology.GetOperatingLineId,
            servicePatterns: servicePatterns);
    }

    private static IReadOnlyList<OperationsTimetableEntry> BuildCore(
        Func<PlannedServiceRun, IReadOnlyList<Station>> stationResolver,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<SimulationEvent>? plannedEvents,
        IEnumerable<SimulationEvent>? actualEvents,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher,
        Func<SimulationEvent, TopologyTurnbackOrigin?>? turnbackOriginResolver,
        Func<PlannedServiceRun, string?>? serviceRouteIdResolver,
        Func<PlannedServiceRun, string?>? operatingLineIdResolver,
        IEnumerable<ServicePattern>? servicePatterns)
    {
        ArgumentNullException.ThrowIfNull(dispatchPlan);
        var planned = plannedEvents?.ToArray() ?? [];
        var actual = actualEvents?.ToArray() ?? [];
        var values = new List<OperationsTimetableEntry>();
        var patterns = (servicePatterns ?? [])
            .ToDictionary(pattern => pattern.PatternId, StringComparer.OrdinalIgnoreCase);

        foreach (var run in dispatchPlan.Runs.OrderBy(item => item.Sequence))
        {
            var stations = stationResolver(run).ToArray();
            var serviceRouteId = serviceRouteIdResolver?.Invoke(run) ?? run.ServiceRouteId;
            var operatingLineId = operatingLineIdResolver?.Invoke(run);
            patterns.TryGetValue(run.StopPatternId, out var servicePattern);
            var runPlannedEvents = planned.Where(item => SameRun(item, run)).ToArray();
            var runActualEvents = actual.Where(item => SameRun(item, run)).ToArray();
            var scheduledOriginDeparture = RelativeSeconds(run.PlannedDepartureTime, dispatchPlan.ScheduleAnchorTime);

            var plannedFacilityDeparture = turnbackOriginResolver is null ? null
                : runPlannedEvents.FirstOrDefault(item => turnbackOriginResolver(item) is not null);
            var actualFacilityDeparture = turnbackOriginResolver is null ? null
                : runActualEvents.FirstOrDefault(item => turnbackOriginResolver(item) is not null);
            var facilityEvent = actualFacilityDeparture ?? plannedFacilityDeparture;
            var facilityOrigin = facilityEvent is null ? null : turnbackOriginResolver!(facilityEvent);
            var firstStationIndex = 0;
            if (facilityOrigin is not null)
            {
                firstStationIndex = Array.FindIndex(stations, station =>
                    station.StationId.Equals(facilityOrigin.StationId, StringComparison.OrdinalIgnoreCase));
                if (firstStationIndex < 0) firstStationIndex = 0;
                var actualDeparture = actualFacilityDeparture?.SimulationTimeSeconds;
                values.Add(new OperationsTimetableEntry(
                    run.VehicleId ?? string.Empty,
                    run.ServiceRunId,
                    run.Direction,
                    run.ServiceTypeId,
                    run.StopPatternId,
                    run.VehicleTypeId,
                    facilityOrigin.FacilityId,
                    facilityOrigin.Name,
                    facilityEvent!.PositionMeters,
                    null,
                    scheduledOriginDeparture,
                    null,
                    actualDeparture,
                    null,
                    actualDeparture is { } departure ? Math.Max(0, departure - scheduledOriginDeparture) : null,
                    actualDeparture is null ? "待發" : "已發車",
                    null,
                    serviceRouteId,
                    operatingLineId));
            }

            for (var index = firstStationIndex; index < stations.Length; index++)
            {
                var station = stations[index];
                var plannedArrival = Find(runPlannedEvents, station, run.Direction, stationEventMatcher,
                    SimulationEventType.Arrival, SimulationEventType.StationPassed);
                var stationTimetableEntry = run.StationTimetableEntries.FirstOrDefault(entry =>
                    entry.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
                var plannedDeparture = index == 0 && facilityOrigin is null
                    ? scheduledOriginDeparture
                    : stationTimetableEntry is not null && !(facilityOrigin is not null && index == firstStationIndex)
                        ? RelativeSeconds(stationTimetableEntry.PlannedDepartureTime, dispatchPlan.ScheduleAnchorTime)
                        : Find(runPlannedEvents, station, run.Direction, stationEventMatcher, SimulationEventType.Departure);
                var actualArrival = Find(runActualEvents, station, run.Direction, stationEventMatcher,
                    SimulationEventType.Arrival, SimulationEventType.StationPassed);
                var actualDeparture = Find(runActualEvents, station, run.Direction, stationEventMatcher,
                    SimulationEventType.Departure);
                var instruction = servicePattern?.Instructions.FirstOrDefault(item =>
                    item.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
                double? plannedDwell = index == 0 || index == stations.Length - 1
                    ? null
                    : instruction?.Mode == StationServiceMode.Pass
                        ? null
                        : stationTimetableEntry?.MinimumDwellSeconds
                            ?? instruction?.DwellTimeSeconds
                            ?? station.DwellTimeSeconds;
                double? actualDwell = actualArrival is { } arrival && actualDeparture is { } departure
                    ? (double?)Math.Max(0, departure - arrival)
                    : null;
                double? delay = actualDeparture is { } actualDepartureValue && plannedDeparture is { } plannedDepartureValue
                    ? (double?)Math.Max(0, actualDepartureValue - plannedDepartureValue)
                    : actualArrival is { } actualArrivalValue && plannedArrival is { } plannedArrivalValue
                        ? (double?)Math.Max(0, actualArrivalValue - plannedArrivalValue)
                        : null;
                values.Add(new OperationsTimetableEntry(
                    run.VehicleId ?? string.Empty,
                    run.ServiceRunId,
                    run.Direction,
                    run.ServiceTypeId,
                    run.StopPatternId,
                    run.VehicleTypeId,
                    station.StationId,
                    station.StationName,
                    station.PositionMeters,
                    plannedArrival,
                    plannedDeparture,
                    actualArrival,
                    actualDeparture,
                    actualDwell,
                    delay,
                    instruction?.Mode == StationServiceMode.Pass
                        ? actualArrival is null ? "待通過" : "已通過"
                        : GetStatus(run, index, stations.Length, actualArrival, actualDeparture, runActualEvents),
                    plannedDwell,
                    serviceRouteId,
                    operatingLineId));
            }
        }

        return values;
    }

    private static bool SameRun(SimulationEvent item, PlannedServiceRun run) =>
        item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
        && item.Direction == run.Direction
        && (string.IsNullOrWhiteSpace(run.VehicleId)
            || item.VehicleId.Equals(run.VehicleId, StringComparison.OrdinalIgnoreCase));

    private static double? Find(
        IEnumerable<SimulationEvent> events,
        Station station,
        TrainDirection direction,
        Func<TrainDirection, string, SimulationEvent, bool>? stationEventMatcher,
        params SimulationEventType[] types) =>
        events.Where(item => types.Contains(item.EventType)
                && (stationEventMatcher is not null
                    ? stationEventMatcher(direction, station.StationId, item)
                    : Math.Abs(item.PositionMeters - station.PositionMeters) <= PositionToleranceMeters))
            .OrderBy(item => item.SimulationTimeSeconds)
            .Select(item => (double?)item.SimulationTimeSeconds)
            .FirstOrDefault();

    private static string GetStatus(
        PlannedServiceRun run,
        int stationIndex,
        int stationCount,
        double? arrival,
        double? departure,
        IReadOnlyList<SimulationEvent> events)
    {
        var isTerminal = stationIndex == stationCount - 1;
        if (isTerminal && events.Any(item => item.EventType == SimulationEventType.ServiceEnded))
        {
            return "退出營運";
        }

        if (arrival is not null && isTerminal && run.ContinueAfterTerminal)
        {
            return "折返接續";
        }

        if (arrival is not null)
        {
            return departure is null && !isTerminal ? "停站中" : "已抵達";
        }

        return departure is not null ? "已發車" : "待發";
    }

    private static double RelativeSeconds(TimeSpan value, TimeSpan anchor)
    {
        var seconds = value.TotalSeconds - anchor.TotalSeconds;
        return seconds < 0 ? seconds + TimeSpan.FromDays(1).TotalSeconds : seconds;
    }
}
