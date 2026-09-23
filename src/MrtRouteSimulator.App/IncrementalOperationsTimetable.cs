using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Keeps the timetable's planned rows and applies only newly published actual events.
/// OperationsTimetable.Build remains the offline reference used when the plan changes.
/// </summary>
internal sealed class IncrementalOperationsTimetable
{
    private const double PositionToleranceMeters = 0.6;
    private readonly Route? _route;
    private readonly TopologyResultContext? _topology;
    private readonly ResolvedDispatchPlan _dispatchPlan;
    private IReadOnlyList<SimulationEvent> _plannedEvents;
    private readonly List<OperationsTimetableEntry> _entries = [];
    private readonly Dictionary<string, RunState> _runs = new(StringComparer.OrdinalIgnoreCase);

    public IncrementalOperationsTimetable(
        Route? route,
        TopologyResultContext? topology,
        ResolvedDispatchPlan dispatchPlan,
        IReadOnlyList<SimulationEvent> plannedEvents,
        IReadOnlyList<SimulationEvent> actualEvents)
    {
        if (route is null && topology is null)
        {
            throw new ArgumentException("時刻表需要路線或 topology 結果內容。");
        }

        _route = route;
        _topology = topology;
        _dispatchPlan = dispatchPlan ?? throw new ArgumentNullException(nameof(dispatchPlan));
        _plannedEvents = plannedEvents ?? [];
        Rebuild(actualEvents);
    }

    public IReadOnlyList<OperationsTimetableEntry> Entries => _entries;

    public void ResetActual() => Rebuild([]);

    public void SetPlannedEvents(IReadOnlyList<SimulationEvent> plannedEvents,
        IReadOnlyList<SimulationEvent> actualEvents)
    {
        _plannedEvents = plannedEvents ?? [];
        Rebuild(actualEvents);
    }

    public void Append(IReadOnlyList<SimulationEvent> newEvents)
    {
        foreach (var item in newEvents)
        {
            if (!_runs.TryGetValue(RunKey(item.ServiceRunId, item.Direction), out var run)
                || (!string.IsNullOrWhiteSpace(run.Plan.VehicleId)
                    && !item.VehicleId.Equals(run.Plan.VehicleId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (item.EventType == SimulationEventType.ServiceEnded)
            {
                run.ServiceEnded = true;
                RefreshStatus(run, run.RowIndices[^1]);
                continue;
            }

            if (item.EventType is not (SimulationEventType.Arrival
                    or SimulationEventType.StationPassed or SimulationEventType.Departure))
            {
                continue;
            }

            foreach (var index in run.RowIndices)
            {
                var entry = _entries[index];
                if (!MatchesStation(entry, item))
                {
                    continue;
                }

                var arrival = entry.ActualArrivalTimeSeconds;
                var departure = entry.ActualDepartureTimeSeconds;
                if (item.EventType is SimulationEventType.Arrival or SimulationEventType.StationPassed)
                {
                    arrival ??= item.SimulationTimeSeconds;
                }
                else
                {
                    departure ??= item.SimulationTimeSeconds;
                }

                var dwell = arrival is { } arrived && departure is { } departed
                    ? Math.Max(0, departed - arrived)
                    : (double?)null;
                var delay = departure is { } actualDeparture && entry.PlannedDepartureTimeSeconds is { } plannedDeparture
                    ? Math.Max(0, actualDeparture - plannedDeparture)
                    : arrival is { } actualArrival && entry.PlannedArrivalTimeSeconds is { } plannedArrival
                        ? Math.Max(0, actualArrival - plannedArrival)
                        : (double?)null;
                _entries[index] = entry with
                {
                    ActualArrivalTimeSeconds = arrival,
                    ActualDepartureTimeSeconds = departure,
                    ActualDwellSeconds = dwell,
                    DelaySeconds = delay
                };
                RefreshStatus(run, index);
            }
        }
    }

    private void Rebuild(IReadOnlyList<SimulationEvent> actualEvents)
    {
        _entries.Clear();
        _entries.AddRange(_topology is { } topology
            ? OperationsTimetable.Build(topology, _dispatchPlan, _plannedEvents, [])
            : OperationsTimetable.Build(_route!, _dispatchPlan, _plannedEvents, []));
        _runs.Clear();
        foreach (var run in _dispatchPlan.Runs)
        {
            var indices = Enumerable.Range(0, _entries.Count)
                .Where(index => _entries[index].ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                    && _entries[index].Direction == run.Direction)
                .ToArray();
            if (indices.Length > 0)
            {
                _runs.Add(RunKey(run.ServiceRunId, run.Direction), new RunState(run, indices));
            }
        }

        Append(actualEvents);
    }

    private bool MatchesStation(OperationsTimetableEntry entry, SimulationEvent item) =>
        _topology is { } topology
            ? topology.MatchesStationEvent(entry.Direction, entry.StationId, item)
            : Math.Abs(item.PositionMeters - entry.PositionMeters) <= PositionToleranceMeters;

    private void RefreshStatus(RunState run, int index)
    {
        var entry = _entries[index];
        var isTerminal = index == run.RowIndices[^1];
        var status = isTerminal && run.ServiceEnded
            ? "退出營運"
            : entry.ActualArrivalTimeSeconds is not null && isTerminal && run.Plan.ContinueAfterTerminal
                ? "折返接續"
                : entry.ActualArrivalTimeSeconds is not null
                    ? entry.ActualDepartureTimeSeconds is null && !isTerminal ? "停站中" : "已抵達"
                    : entry.ActualDepartureTimeSeconds is not null ? "已發車" : "待發";
        _entries[index] = entry with { Status = status };
    }

    private static string RunKey(string serviceRunId, TrainDirection direction) =>
        $"{serviceRunId}|{(int)direction}";

    private sealed class RunState(PlannedServiceRun plan, int[] rowIndices)
    {
        public PlannedServiceRun Plan { get; } = plan;
        public int[] RowIndices { get; } = rowIndices;
        public bool ServiceEnded { get; set; }
    }
}
