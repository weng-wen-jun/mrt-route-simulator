using System.Collections.Immutable;
using System.Diagnostics;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>只有自上一個 frame 以來新增的實際結果資料。</summary>
public sealed record SimulationResultDelta(
    bool WasReset,
    ImmutableArray<SimulationEvent> NewEvents,
    ImmutableArray<TrajectorySample> NewTrajectorySamples,
    ImmutableArray<SafetyObservation> NewSafetyObservations,
    double AccumulatorMilliseconds)
{
    public bool HasNewEvents => !NewEvents.IsEmpty;

    public bool HasNewTrajectory => !NewTrajectorySamples.IsEmpty;

    public bool HasNewSafety => !NewSafetyObservations.IsEmpty;
}

/// <summary>
/// Consumes persistent playback histories by cursor. It never rescans an already-consumed prefix;
/// derived views can use the returned deltas and refresh only when their source changes.
/// </summary>
public sealed class SimulationResultAccumulator
{
    private readonly ResourceOccupancyAccumulator _resourceOccupancy = new();
    private IncrementalOperationsTimetable? _timetable;
    private IncrementalV1V2Comparison? _comparison;
    private IncrementalIntervalStatistics? _intervalStatistics;
    private Guid? _generationId;
    private int _eventCursor;
    private int _trajectoryCursor;
    private int _safetyCursor;
    private double _lastSimulationTimeSeconds;

    public int LastProcessedEventIndex => _eventCursor;

    public int LastProcessedTrajectoryIndex => _trajectoryCursor;

    public int LastProcessedSafetyIndex => _safetyCursor;

    public IReadOnlyList<OperationsTimetableEntry>? TimetableEntries => _timetable?.Entries;

    public IReadOnlyList<V1V2StationComparison>? ComparisonEntries => _comparison?.Entries;

    public void ConfigureIntervalStatistics(
        Route? route,
        TopologyResultContext? topology,
        IReadOnlyList<SpeedLimitSegment>? speedLimits = null) =>
        _intervalStatistics = new IncrementalIntervalStatistics(route, topology, speedLimits);

    public IntervalStatisticsResult? BuildIntervalStatistics(IntervalStatisticsFilter? filter = null) =>
        _intervalStatistics?.Build(filter);

    public void ClearIntervalStatistics() => _intervalStatistics = null;

    public void ConfigureTimetable(
        Route? route,
        TopologyResultContext? topology,
        ResolvedDispatchPlan dispatchPlan,
        IReadOnlyList<SimulationEvent> plannedEvents,
        IReadOnlyList<SimulationEvent> actualEvents) =>
        _timetable = new IncrementalOperationsTimetable(route, topology, dispatchPlan,
            plannedEvents, actualEvents);

    public void SetPlannedTimetableEvents(IReadOnlyList<SimulationEvent> plannedEvents,
        IReadOnlyList<SimulationEvent> actualEvents) =>
        _timetable?.SetPlannedEvents(plannedEvents, actualEvents);

    public void ClearTimetable() => _timetable = null;

    public void ConfigureComparison(
        Route? route,
        TopologyResultContext? topology,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<VehicleTypeDefinition> vehicleTypes,
        IEnumerable<StopPatternDefinition> stopPatterns,
        TrainParameters fallbackParameters,
        IReadOnlyList<SimulationEvent> actualEvents) =>
        _comparison = new IncrementalV1V2Comparison(route, topology, dispatchPlan,
            vehicleTypes, stopPatterns, fallbackParameters, actualEvents);

    public void ClearComparison() => _comparison = null;

    public SimulationResultDelta Advance(PlaybackFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var stopwatch = Stopwatch.StartNew();
        var wasReset = _generationId != frame.GenerationId
            || frame.Events.Count < _eventCursor
            || frame.Trajectory.Count < _trajectoryCursor
            || frame.SafetyHistory.Count < _safetyCursor
            || frame.SimulationTimeSeconds + 1e-8 < _lastSimulationTimeSeconds;
        if (wasReset)
        {
            Reset(frame.GenerationId);
        }

        var newEvents = Slice(frame.Events, _eventCursor);
        var newTrajectory = Slice(frame.Trajectory, _trajectoryCursor);
        var newSafety = Slice(frame.SafetyHistory, _safetyCursor);
        _resourceOccupancy.Append(newEvents);
        _timetable?.Append(newEvents);
        _comparison?.Append(newEvents);
        _intervalStatistics?.Append(newTrajectory, newEvents);
        _eventCursor = frame.Events.Count;
        _trajectoryCursor = frame.Trajectory.Count;
        _safetyCursor = frame.SafetyHistory.Count;
        _lastSimulationTimeSeconds = frame.SimulationTimeSeconds;
        stopwatch.Stop();
        return new SimulationResultDelta(wasReset, newEvents, newTrajectory, newSafety, stopwatch.Elapsed.TotalMilliseconds);
    }

    public ResourceOccupancyAnalysisResult BuildResourceOccupancy(double currentTimeSeconds) =>
        _resourceOccupancy.Build(currentTimeSeconds);

    public void Reset() => Reset(null);

    private void Reset(Guid? generationId)
    {
        _generationId = generationId;
        _eventCursor = 0;
        _trajectoryCursor = 0;
        _safetyCursor = 0;
        _lastSimulationTimeSeconds = 0;
        _resourceOccupancy.Reset();
        _timetable?.ResetActual();
        _comparison?.ResetActual();
        _intervalStatistics?.Reset();
    }

    private static ImmutableArray<T> Slice<T>(IReadOnlyList<T> source, int start)
    {
        if (start >= source.Count)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<T>(source.Count - start);
        for (var index = start; index < source.Count; index++)
        {
            builder.Add(source[index]);
        }

        return builder.MoveToImmutable();
    }

    private sealed class ResourceOccupancyAccumulator
    {
        private readonly Dictionary<string, OpenOccupancy> _open = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ResourceTotals> _totals = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ResourceOccupancyInterval> _intervals = [];
        private double? _windowStartSeconds;

        public void Reset()
        {
            _open.Clear();
            _totals.Clear();
            _intervals.Clear();
            _windowStartSeconds = null;
        }

        public void Append(IEnumerable<SimulationEvent> source)
        {
            foreach (var item in source
                         .Where(value => value.EventType is SimulationEventType.RouteReserved or SimulationEventType.RouteReleased)
                         .OrderBy(value => value.SimulationTimeSeconds)
                         .ThenBy(value => value.EventType == SimulationEventType.RouteReleased ? 0 : 1))
            {
                _windowStartSeconds ??= item.SimulationTimeSeconds;
                foreach (var resourceId in GetResources(item))
                {
                    if (item.EventType == SimulationEventType.RouteReserved)
                    {
                        if (_open.TryGetValue(resourceId, out var current))
                        {
                            if (current.Matches(item))
                            {
                                continue;
                            }

                            Close(current, item.SimulationTimeSeconds, isClosed: false);
                        }

                        _open[resourceId] = new OpenOccupancy(resourceId, item.VehicleId, item.ServiceRunId, item.SimulationTimeSeconds);
                        GetTotals(resourceId).ReservationCount++;
                    }
                    else if (_open.Remove(resourceId, out var current))
                    {
                        Close(current, item.SimulationTimeSeconds, isClosed: true);
                    }
                }
            }
        }

        public ResourceOccupancyAnalysisResult Build(double currentTimeSeconds)
        {
            var start = _windowStartSeconds ?? 0;
            var end = currentTimeSeconds;
            if (!double.IsFinite(end) || end < start)
            {
                throw new SimulationValidationException(["資源占用分析的觀測結束時間無效。"]);
            }

            var windowSeconds = Math.Max(0, end - start);
            var resources = _totals.Select(pair =>
                {
                    var openOccupied = _open.TryGetValue(pair.Key, out var open)
                        ? Math.Max(0, end - open.StartTimeSeconds)
                        : 0;
                    var occupied = pair.Value.ClosedOccupiedSeconds + openOccupied;
                    return new ResourceUtilization(
                        pair.Key,
                        occupied,
                        windowSeconds <= 0 ? 0 : occupied / windowSeconds * 100,
                        pair.Value.ReservationCount,
                        windowSeconds <= 0 ? 0 : pair.Value.ReservationCount * 3600 / windowSeconds,
                        pair.Value.MinimumReleaseHeadwaySeconds);
                })
                .OrderByDescending(item => item.UtilizationPercent)
                .ThenBy(item => item.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var intervals = _intervals.Concat(_open.Values.Select(item => item.Close(end, isClosed: false))).ToArray();
            return new ResourceOccupancyAnalysisResult(start, end, intervals, resources);
        }

        private void Close(OpenOccupancy item, double endTimeSeconds, bool isClosed)
        {
            var interval = item.Close(endTimeSeconds, isClosed);
            _intervals.Add(interval);
            var totals = GetTotals(item.ResourceId);
            totals.ClosedOccupiedSeconds += Math.Max(0, interval.EndTimeSeconds - interval.StartTimeSeconds);
            if (!isClosed)
            {
                return;
            }

            if (totals.LastReleaseTimeSeconds is { } previousRelease)
            {
                var headway = Math.Max(0, interval.EndTimeSeconds - previousRelease);
                totals.MinimumReleaseHeadwaySeconds = totals.MinimumReleaseHeadwaySeconds is { } minimum
                    ? Math.Min(minimum, headway)
                    : headway;
            }

            totals.LastReleaseTimeSeconds = interval.EndTimeSeconds;
        }

        private ResourceTotals GetTotals(string resourceId)
        {
            if (!_totals.TryGetValue(resourceId, out var totals))
            {
                totals = new ResourceTotals();
                _totals.Add(resourceId, totals);
            }

            return totals;
        }

        private static IReadOnlyList<string> GetResources(SimulationEvent item)
        {
            var values = item.ResourceIds is { Count: > 0 }
                ? item.ResourceIds
                : string.IsNullOrWhiteSpace(item.ResourceId) ? [] : [item.ResourceId];
            return values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private sealed class ResourceTotals
        {
            public double ClosedOccupiedSeconds { get; set; }

            public int ReservationCount { get; set; }

            public double? LastReleaseTimeSeconds { get; set; }

            public double? MinimumReleaseHeadwaySeconds { get; set; }
        }

        private sealed record OpenOccupancy(string ResourceId, string VehicleId, string ServiceRunId, double StartTimeSeconds)
        {
            public bool Matches(SimulationEvent item) =>
                VehicleId.Equals(item.VehicleId, StringComparison.OrdinalIgnoreCase)
                && ServiceRunId.Equals(item.ServiceRunId, StringComparison.OrdinalIgnoreCase);

            public ResourceOccupancyInterval Close(double endTimeSeconds, bool isClosed) => new(
                ResourceId,
                VehicleId,
                ServiceRunId,
                StartTimeSeconds,
                Math.Max(StartTimeSeconds, endTimeSeconds),
                isClosed);
        }
    }
}
