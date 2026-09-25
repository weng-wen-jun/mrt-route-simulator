using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Keeps the V1 theoretical comparison rows and applies only newly published V2
/// station events. The complete comparison remains the offline reference used
/// when the dispatch plan or the catalog changes.
/// </summary>
internal sealed class IncrementalV1V2Comparison
{
    private const double PositionToleranceMeters = 0.6;
    private const string NotArrivedStatus = "V2 尚未抵達";
    private const string ComparableStatus = "可比較";
    private const string PassedStatus = "跨站不比較";
    private const string TurnbackStatus = "折返節點不適用 V1";

    private readonly Route? _route;
    private readonly TopologyResultContext? _topology;
    private readonly ResolvedDispatchPlan _dispatchPlan;
    private readonly IReadOnlyList<VehicleTypeDefinition> _vehicleTypes;
    private readonly IReadOnlyList<StopPatternDefinition> _stopPatterns;
    private readonly TrainParameters _fallbackParameters;
    private readonly List<V1V2StationComparison> _entries = [];
    private readonly Dictionary<string, RunState> _runs = new(StringComparer.OrdinalIgnoreCase);

    public IncrementalV1V2Comparison(
        Route? route,
        TopologyResultContext? topology,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<VehicleTypeDefinition> vehicleTypes,
        IEnumerable<StopPatternDefinition> stopPatterns,
        TrainParameters fallbackParameters,
        IReadOnlyList<SimulationEvent> actualEvents)
    {
        if (route is null && topology is null)
        {
            throw new ArgumentException("V1/V2 比較需要路線或 topology 結果內容。");
        }

        _route = route;
        _topology = topology;
        _dispatchPlan = dispatchPlan ?? throw new ArgumentNullException(nameof(dispatchPlan));
        _vehicleTypes = vehicleTypes?.ToArray() ?? [];
        _stopPatterns = stopPatterns?.ToArray() ?? [];
        _fallbackParameters = fallbackParameters ?? throw new ArgumentNullException(nameof(fallbackParameters));
        Rebuild(actualEvents);
    }

    public IReadOnlyList<V1V2StationComparison> Entries => _entries;

    public void ResetActual() => Rebuild([]);

    public void Append(IReadOnlyList<SimulationEvent> newEvents)
    {
        foreach (var item in newEvents ?? [])
        {
            if (!_runs.TryGetValue(RunKey(item.ServiceRunId, item.Direction), out var run)
                || (!string.IsNullOrWhiteSpace(run.Plan.VehicleId)
                    && !item.VehicleId.Equals(run.Plan.VehicleId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (item.EventType is not (SimulationEventType.Arrival
                    or SimulationEventType.StationPassed
                    or SimulationEventType.Departure))
            {
                continue;
            }

            // Each run has only its own station rows, so a new event touches
            // O(stations in that run) rows and never rescans old events.
            foreach (var index in run.RowIndices)
            {
                var entry = _entries[index];
                if (!MatchesStation(entry, item))
                {
                    continue;
                }

                var actualArrival = entry.ActualArrivalTimeSeconds;
                var actualDeparture = entry.ActualDepartureTimeSeconds;
                var acceptsArrival = item.EventType is SimulationEventType.Arrival or SimulationEventType.StationPassed;
                var acceptsDeparture = item.EventType == SimulationEventType.Departure;

                if (entry.Status == PassedStatus)
                {
                    acceptsArrival = item.EventType == SimulationEventType.StationPassed;
                    acceptsDeparture = false;
                }
                else if (entry.Status == TurnbackStatus)
                {
                    acceptsArrival = item.EventType == SimulationEventType.Arrival;
                    acceptsDeparture = false;
                }

                if (acceptsArrival)
                {
                    // V1V2Comparison.Find uses the first matching event. The
                    // playback worker publishes events in time order, so ??=
                    // preserves that first-match rule across incremental calls.
                    actualArrival ??= item.SimulationTimeSeconds;
                    if (entry.Status is not (PassedStatus or TurnbackStatus)
                        && actualArrival is { } arrivalTime
                        && actualDeparture is { } earlierDeparture
                        && earlierDeparture + 1e-7 < arrivalTime)
                    {
                        // The offline analyzer ignores departures before a known arrival.
                        actualDeparture = null;
                    }
                }
                else if (acceptsDeparture)
                {
                    actualDeparture ??= item.SimulationTimeSeconds;
                }
                else
                {
                    continue;
                }

                if (actualArrival == entry.ActualArrivalTimeSeconds
                    && actualDeparture == entry.ActualDepartureTimeSeconds)
                {
                    continue;
                }

                _entries[index] = UpdateActual(entry, actualArrival, actualDeparture);
            }
        }
    }

    private void Rebuild(IReadOnlyList<SimulationEvent> actualEvents)
    {
        // Construct the theoretical rows once. The Engine implementation remains
        // the sole owner of V1 timing, stop-pattern handling and topology station
        // resolution; this class only maintains the mutable V2 result fields.
        var result = _topology is { } topology
            ? V1V2Comparison.Analyze(
                topology,
                _dispatchPlan,
                _vehicleTypes,
                _stopPatterns,
                _fallbackParameters,
                [])
            : V1V2Comparison.Analyze(
                _route!,
                _dispatchPlan,
                _vehicleTypes,
                _stopPatterns,
                _fallbackParameters,
                []);

        _entries.Clear();
        _entries.AddRange(result.Stations);
        _runs.Clear();
        foreach (var run in _dispatchPlan.Runs)
        {
            var indices = Enumerable.Range(0, _entries.Count)
                .Where(index => _entries[index].ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                    && _entries[index].Direction == run.Direction)
                .ToArray();
            if (indices.Length > 0)
            {
                _runs[RunKey(run.ServiceRunId, run.Direction)] = new RunState(run, indices);
            }
        }

        // Analyze orders a run's actual events by simulation time. Apply the
        // initial snapshot in the same order so its result is identical to the
        // non-incremental reference for normal world event histories.
        Append((actualEvents ?? [])
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray());
    }

    private bool MatchesStation(V1V2StationComparison entry, SimulationEvent item) =>
        _topology is { } topology
            ? topology.MatchesStationEvent(entry.Direction, entry.StationId, item)
            : item.Direction == entry.Direction
                && Math.Abs(item.PositionMeters - entry.PositionMeters) <= PositionToleranceMeters;

    private static V1V2StationComparison UpdateActual(
        V1V2StationComparison entry,
        double? actualArrival,
        double? actualDeparture)
    {
        var actualDwell = actualArrival is { } arrival && actualDeparture is { } departure
            ? (double?)Math.Max(0, departure - arrival)
            : null;
        var arrivalDifference = entry.TheoreticalArrivalTimeSeconds is { } theoreticalArrival
                && actualArrival is { } arrivalValue
            ? (double?)(arrivalValue - theoreticalArrival)
            : null;
        var departureDifference = entry.TheoreticalDepartureTimeSeconds is { } theoreticalDeparture
                && actualDeparture is { } departureValue
            ? (double?)(departureValue - theoreticalDeparture)
            : null;
        var departurePercent = entry.TheoreticalDepartureTimeSeconds is { } theoretical
                && theoretical > 1e-7
                && departureDifference is { } difference
            ? (double?)(difference / theoretical * 100)
            : null;
        var status = entry.Status == NotArrivedStatus && actualArrival is not null
            ? ComparableStatus
            : entry.Status;
        return entry with
        {
            ActualArrivalTimeSeconds = actualArrival,
            ActualDepartureTimeSeconds = actualDeparture,
            ActualDwellSeconds = actualDwell,
            ArrivalDifferenceSeconds = arrivalDifference,
            DepartureDifferenceSeconds = departureDifference,
            DepartureDifferencePercent = departurePercent,
            Status = status
        };
    }

    private static string RunKey(string serviceRunId, TrainDirection direction) =>
        $"{serviceRunId}|{(int)direction}";

    private sealed class RunState(PlannedServiceRun plan, int[] rowIndices)
    {
        public PlannedServiceRun Plan { get; } = plan;

        public int[] RowIndices { get; } = rowIndices;
    }
}
