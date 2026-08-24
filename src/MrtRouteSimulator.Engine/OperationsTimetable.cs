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
    string Status);

/// <summary>確保時刻表、動畫與運行圖共用 SimulationWorld 的結構化事件。</summary>
public static class OperationsTimetable
{
    private const double PositionToleranceMeters = 0.6;

    public static IReadOnlyList<OperationsTimetableEntry> Build(
        Route route,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<SimulationEvent>? plannedEvents,
        IEnumerable<SimulationEvent>? actualEvents)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(dispatchPlan);
        var planned = plannedEvents?.ToArray() ?? [];
        var actual = actualEvents?.ToArray() ?? [];
        var values = new List<OperationsTimetableEntry>();

        foreach (var run in dispatchPlan.Runs.OrderBy(item => item.Sequence))
        {
            var stations = run.Direction == TrainDirection.Outbound
                ? route.Stations.ToArray()
                : route.Stations.Reverse().ToArray();
            var runPlannedEvents = planned.Where(item => SameRun(item, run)).ToArray();
            var runActualEvents = actual.Where(item => SameRun(item, run)).ToArray();
            var scheduledOriginDeparture = RelativeSeconds(run.PlannedDepartureTime, dispatchPlan.ScheduleAnchorTime);

            for (var index = 0; index < stations.Length; index++)
            {
                var station = stations[index];
                var plannedArrival = Find(runPlannedEvents, station, SimulationEventType.Arrival, SimulationEventType.StationPassed);
                var plannedDeparture = index == 0
                    ? scheduledOriginDeparture
                    : Find(runPlannedEvents, station, SimulationEventType.Departure);
                var actualArrival = Find(runActualEvents, station, SimulationEventType.Arrival, SimulationEventType.StationPassed);
                var actualDeparture = Find(runActualEvents, station, SimulationEventType.Departure);
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
                    GetStatus(run, index, stations.Length, actualArrival, actualDeparture, runActualEvents)));
            }
        }

        return values;
    }

    private static bool SameRun(SimulationEvent item, PlannedServiceRun run) =>
        item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
        && item.Direction == run.Direction
        && (string.IsNullOrWhiteSpace(run.VehicleId)
            || item.VehicleId.Equals(run.VehicleId, StringComparison.OrdinalIgnoreCase));

    private static double? Find(IEnumerable<SimulationEvent> events, Station station, params SimulationEventType[] types) =>
        events.Where(item => types.Contains(item.EventType)
                && Math.Abs(item.PositionMeters - station.PositionMeters) <= PositionToleranceMeters)
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
