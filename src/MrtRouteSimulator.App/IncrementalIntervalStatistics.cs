using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Incrementally maintains the V2 interval and end-to-end result rows.
///
/// The Engine <see cref="IntervalStatistics"/> implementation remains the
/// compatibility/reference implementation.  Playback only gives this class
/// the samples and events that arrived since the previous immutable frame;
/// building a view reads the accumulated state and never re-scans the frame
/// history.
/// </summary>
internal sealed class IncrementalIntervalStatistics
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

    private readonly Route? _route;
    private readonly TopologyResultContext? _topology;
    private readonly IReadOnlyList<SpeedLimitSegment> _speedLimits;
    private readonly IReadOnlyList<Station> _outboundStations;
    private readonly IReadOnlyList<Station> _inboundStations;
    private readonly Dictionary<RunKey, RunState> _runs = [];
    private readonly Dictionary<ControlEventKey, ControlEventSeries> _controlEventIndex = [];

    public IncrementalIntervalStatistics(
        Route? route,
        TopologyResultContext? topology,
        IReadOnlyList<SpeedLimitSegment>? speedLimits = null)
    {
        if (route is null && topology is null)
        {
            throw new ArgumentException("區間統計需要路線或 topology 結果內容。");
        }

        _route = route;
        _topology = topology;
        _speedLimits = speedLimits ?? [];
        _outboundStations = topology?.GetDisplayStations(TrainDirection.Outbound)
            ?? route!.Stations.ToArray();
        _inboundStations = topology?.GetDisplayStations(TrainDirection.Inbound)
            ?? route!.Stations.Reverse().ToArray();
    }

    public void Reset()
    {
        _runs.Clear();
        _controlEventIndex.Clear();
    }

    public void Append(
        IReadOnlyList<TrajectorySample>? newSamples,
        IReadOnlyList<SimulationEvent>? newEvents)
    {
        // Merge the two append-only streams by simulation time.  Events win
        // ties because SimulationWorld emits a boundary before recording the
        // trajectory sample for that fixed tick.  This also keeps a large
        // first append (for example a 3600-second playback) causal instead of
        // making every future boundary visible before old samples are read.
        var events = (newEvents ?? []).OrderBy(item => item.SimulationTimeSeconds).ToArray();
        var samples = (newSamples ?? []).OrderBy(item => item.SimulationTimeSeconds).ToArray();
        var eventIndex = 0;
        var sampleIndex = 0;
        while (eventIndex < events.Length || sampleIndex < samples.Length)
        {
            if (sampleIndex >= samples.Length
                || eventIndex < events.Length
                    && events[eventIndex].SimulationTimeSeconds
                        <= samples[sampleIndex].SimulationTimeSeconds + TimeEpsilon)
            {
                ApplyEvent(events[eventIndex++]);
            }
            else
            {
                ApplySample(samples[sampleIndex++]);
            }
        }
    }

    public IntervalStatisticsResult Build(IntervalStatisticsFilter? filter = null)
    {
        filter ??= new IntervalStatisticsFilter();
        var allIntervals = _runs.Values
            .SelectMany(run => run.Segments.Values)
            .Where(segment => segment.FirstSample is not null)
            .Select(CreateInterval)
            .Where(filter.Matches)
            .OrderBy(item => item.DepartureTimeSeconds ?? item.FirstObservedTimeSeconds)
            .ThenBy(item => item.VehicleId, StringComparer.Ordinal)
            .ThenBy(item => item.FromStationId, StringComparer.Ordinal)
            .ToArray();

        var completed = allIntervals.Where(item => item.IsComplete).ToArray();
        var inProgress = allIntervals.Where(item => !item.IsComplete).ToArray();
        var summaries = completed
            .GroupBy(item => (item.Direction, item.FromStationId, item.FromStationName,
                item.ToStationId, item.ToStationName))
            .Select(CreateSummary)
            .OrderBy(item => item.Direction)
            .ThenBy(item => item.FromStationId, StringComparer.Ordinal)
            .ToArray();

        var journeys = _runs.Values
            .Where(run => run.FirstSample is not null)
            .Select(CreateJourney)
            .Where(filter.Matches)
            .OrderBy(item => item.DepartureTimeSeconds ?? item.FirstObservedTimeSeconds)
            .ThenBy(item => item.VehicleId, StringComparer.Ordinal)
            .ToArray();

        return new IntervalStatisticsResult(completed, inProgress, summaries, filter)
        {
            JourneyStatistics = journeys
        };
    }

    private void ApplySample(TrajectorySample sample)
    {
        if (string.IsNullOrWhiteSpace(sample.VehicleId)
            || string.IsNullOrWhiteSpace(sample.ServiceRunId))
        {
            return;
        }

        var run = GetRun(sample.VehicleId, sample.ServiceRunId, sample.Direction,
            sample.ServiceClassId, sample.ServicePatternId, sample.VehicleTypeId);
        run.FirstSample ??= sample;
        var previous = run.LastSample;
        var previousSegment = run.PreviousSegment;
        run.LastSample = sample;

        var segment = ResolveSampleSegment(run, sample);
        if (segment is not null)
        {
            run.ActiveSegment = segment;
            RegisterControlEventSegment(segment);
            segment.CandidateSamples.Add(sample);
            segment.FirstSample ??= sample;
            segment.LastSample = sample;
            segment.ObservedStartTimeSeconds ??= sample.SimulationTimeSeconds;
            segment.ObservedEndTimeSeconds = sample.SimulationTimeSeconds;
            segment.StartPositionMeters ??= sample.PositionMeters;
            segment.EndPositionMeters = sample.PositionMeters;
            var start = segment.DepartureTimeSeconds ?? segment.ObservedStartTimeSeconds.Value;
            var end = segment.ArrivalTimeSeconds ?? sample.SimulationTimeSeconds;
            if (sample.SimulationTimeSeconds >= start - TimeEpsilon
                && sample.SimulationTimeSeconds <= end + TimeEpsilon)
            {
                segment.PeakSpeedMetersPerSecond = Math.Max(
                    segment.PeakSpeedMetersPerSecond ?? 0,
                    Math.Max(0, sample.SpeedMetersPerSecond));
            }
            RefreshSegmentControlEvents(segment);
        }

        if (previousSegment is not null && previousSegment != segment)
        {
            // The reference analyzer uses all adjacent run samples when a
            // boundary is clipped by an arrival event.  Retain only the
            // boundary candidate on the preceding segment, rather than the
            // entire run history.
            previousSegment.CandidateSamples.Add(sample);
        }

        // IntervalStatistics integrates every adjacent pair in the run.  The
        // pair at a station boundary therefore still contributes the final
        // fraction of the preceding completed interval, even though the right
        // sample already carries the next segment's station IDs.
        if (previous is null || previousSegment is null)
        {
            run.PreviousSegment = segment ?? previousSegment;
            return;
        }

        var left = previous;
        var right = sample;
        var segmentForPair = previousSegment;
        segmentForPair.Pairs.Add(new SamplePair(left, right));
        var overlapStart = Math.Max(
            segmentForPair.DepartureTimeSeconds ?? segmentForPair.ObservedStartTimeSeconds!.Value,
            left.SimulationTimeSeconds);
        var overlapEnd = Math.Min(
            segmentForPair.ArrivalTimeSeconds
                ?? segmentForPair.ObservedEndTimeSeconds
                ?? right.SimulationTimeSeconds,
            right.SimulationTimeSeconds);
        if (overlapEnd > overlapStart + TimeEpsilon)
        {
            var seconds = overlapEnd - overlapStart;
            segmentForPair.PhaseSeconds[left.Phase] =
                segmentForPair.PhaseSeconds.GetValueOrDefault(left.Phase) + seconds;
            if ((left.Constraints & OperationalConstraint.MovingBlock) != 0)
            {
                segmentForPair.ControlLimitedSeconds += seconds;
            }
        }

        run.PreviousSegment = segment ?? previousSegment;
    }

    private SegmentState? ResolveSampleSegment(RunState run, TrajectorySample sample)
    {
        var stations = GetStations(sample.Direction);
        var current = sample.CurrentStationId;
        var next = sample.NextStationId;
        if (!string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(next))
        {
            for (var index = 0; index + 1 < stations.Count; index++)
            {
                if (string.Equals(stations[index].StationId, current, StringComparison.Ordinal)
                    && string.Equals(stations[index + 1].StationId, next, StringComparison.Ordinal))
                {
                    return GetSegment(run, stations[index], stations[index + 1]);
                }
            }
        }

        // Match IntervalStatistics' topology-native grouping exactly: only a
        // sample carrying both resolved station IDs belongs to an interval.
        // Do not infer connectivity from chainage or an event-only fallback.
        return null;
    }

    private void ApplyEvent(SimulationEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.VehicleId)
            || string.IsNullOrWhiteSpace(item.ServiceRunId))
        {
            return;
        }

        if (ControlEventTypes.Contains(item.EventType))
        {
            var series = GetControlEventSeries(item.VehicleId, item.Direction);
            series.Append(item);
            foreach (var segment in series.Segments)
            {
                RefreshSegmentControlEvents(segment);
            }
        }

        var run = GetRun(item.VehicleId, item.ServiceRunId, item.Direction,
            item.ServiceClassId, item.ServicePatternId, item.VehicleTypeId);
        var stations = GetStations(item.Direction);
        var stationIndex = FindStationIndex(item, stations);

        if (stationIndex is { } index
            && item.EventType is SimulationEventType.Arrival or SimulationEventType.StationPassed)
        {
            if (index > 0)
            {
                var segment = GetSegment(run, stations[index - 1], stations[index]);
                // IntervalStatistics only searches an arrival after a known
                // departure.  Keep a pending boundary until that departure
                // exists so a passing station cannot complete an interval by
                // itself.
                if (segment.DepartureTimeSeconds is { } departure
                    && item.SimulationTimeSeconds >= departure - TimeEpsilon)
                {
                    segment.ArrivalTimeSeconds ??= item.SimulationTimeSeconds;
                    segment.ArrivalSpeedMetersPerSecond ??= Math.Max(0, item.SpeedMetersPerSecond);
                    segment.IsScheduledStop ??= item.EventType == SimulationEventType.Arrival;
                    RefreshSegmentMetrics(segment);
                }
                else
                {
                    segment.PendingArrivalTimeSeconds ??= item.SimulationTimeSeconds;
                    segment.PendingArrivalSpeedMetersPerSecond ??= Math.Max(0, item.SpeedMetersPerSecond);
                    segment.PendingIsScheduledStop ??= item.EventType == SimulationEventType.Arrival;
                }
                run.ActiveSegment = segment;
            }

            if (index == stations.Count - 1)
            {
                if (run.OriginDepartureTimeSeconds is { } originDeparture
                    && item.SimulationTimeSeconds >= originDeparture - TimeEpsilon)
                {
                    run.TerminalArrivalTimeSeconds ??= item.SimulationTimeSeconds;
                }
                else
                {
                    run.PendingTerminalArrivalTimeSeconds ??= item.SimulationTimeSeconds;
                }
            }
        }

        if (stationIndex is { } departureIndex
            && item.EventType == SimulationEventType.Departure
            && departureIndex < stations.Count - 1)
        {
            var segment = GetSegment(run, stations[departureIndex], stations[departureIndex + 1]);
            segment.DepartureTimeSeconds ??= item.SimulationTimeSeconds;
            segment.DepartureSpeedMetersPerSecond ??= Math.Max(0, item.SpeedMetersPerSecond);
            if (segment.PendingArrivalTimeSeconds is { } pendingArrival
                && pendingArrival >= segment.DepartureTimeSeconds.Value - TimeEpsilon)
            {
                segment.ArrivalTimeSeconds ??= pendingArrival;
                segment.ArrivalSpeedMetersPerSecond ??= segment.PendingArrivalSpeedMetersPerSecond;
                segment.IsScheduledStop ??= segment.PendingIsScheduledStop;
            }
            RefreshSegmentMetrics(segment);
            run.ActiveSegment = segment;
            if (departureIndex == 0)
            {
                run.OriginDepartureTimeSeconds ??= item.SimulationTimeSeconds;
                if (run.PendingTerminalArrivalTimeSeconds is { } pendingTerminal
                    && pendingTerminal >= run.OriginDepartureTimeSeconds.Value - TimeEpsilon)
                {
                    run.TerminalArrivalTimeSeconds ??= pendingTerminal;
                }
            }
        }

    }

    private SegmentState GetSegment(RunState run, Station from, Station to)
    {
        var key = new SegmentKey(from.StationId, to.StationId);
        if (!run.Segments.TryGetValue(key, out var segment))
        {
            segment = new SegmentState(run, from, to);
            run.Segments.Add(key, segment);
        }

        return segment;
    }

    private void RefreshSegmentMetrics(SegmentState segment)
    {
        if (segment.ObservedStartTimeSeconds is not { } observedStart)
        {
            return;
        }

        var start = segment.DepartureTimeSeconds ?? observedStart;
        var end = segment.ArrivalTimeSeconds ?? segment.ObservedEndTimeSeconds ?? start;
        if (end < start - TimeEpsilon)
        {
            return;
        }

        var phaseSeconds = Enum.GetValues<OperationalPhase>()
            .ToDictionary(item => item, _ => 0d);
        var controlSeconds = 0d;
        foreach (var pair in segment.Pairs)
        {
            var overlapStart = Math.Max(start, pair.Left.SimulationTimeSeconds);
            var overlapEnd = Math.Min(end, pair.Right.SimulationTimeSeconds);
            if (overlapEnd <= overlapStart + TimeEpsilon)
            {
                continue;
            }

            var seconds = overlapEnd - overlapStart;
            phaseSeconds[pair.Left.Phase] += seconds;
            if ((pair.Left.Constraints & OperationalConstraint.MovingBlock) != 0)
            {
                controlSeconds += seconds;
            }
        }

        segment.PhaseSeconds.Clear();
        foreach (var item in phaseSeconds)
        {
            segment.PhaseSeconds[item.Key] = item.Value;
        }

        segment.ControlLimitedSeconds = controlSeconds;
        var peak = segment.CandidateSamples
            .Where(item => item.SimulationTimeSeconds >= start - TimeEpsilon
                && item.SimulationTimeSeconds <= end + TimeEpsilon)
            .Select(item => Math.Max(0, item.SpeedMetersPerSecond))
            .Append(segment.DepartureSpeedMetersPerSecond ?? 0)
            .Append(segment.ArrivalSpeedMetersPerSecond ?? 0)
            .Max();
        segment.PeakSpeedMetersPerSecond = peak;
        RefreshSegmentControlEvents(segment);
    }

    private void RegisterControlEventSegment(SegmentState segment)
    {
        var series = segment.ControlEventSeries
            ??= GetControlEventSeries(segment.Run.Key.VehicleId, segment.Run.Key.Direction);
        series.Segments.Add(segment);
    }

    private void RefreshSegmentControlEvents(SegmentState segment)
    {
        if (segment.ObservedStartTimeSeconds is not { } observedStart)
        {
            return;
        }

        var start = segment.DepartureTimeSeconds ?? observedStart;
        var end = segment.ArrivalTimeSeconds ?? segment.ObservedEndTimeSeconds ?? start;
        segment.ControlEvents = segment.ControlEventSeries?.Build(start, end)
            ?? IntervalControlEventSummary.Empty;
    }

    private ControlEventSeries GetControlEventSeries(string vehicleId, TrainDirection direction)
    {
        var key = new ControlEventKey(vehicleId, direction);
        if (!_controlEventIndex.TryGetValue(key, out var series))
        {
            series = new ControlEventSeries();
            _controlEventIndex.Add(key, series);
        }

        return series;
    }

    private RunState GetRun(
        string vehicleId,
        string serviceRunId,
        TrainDirection direction,
        string serviceClassId,
        string servicePatternId,
        string vehicleTypeId)
    {
        var key = new RunKey(vehicleId, serviceRunId, direction);
        if (!_runs.TryGetValue(key, out var run))
        {
            run = new RunState(key, serviceClassId, servicePatternId, vehicleTypeId);
            _runs.Add(key, run);
        }
        else
        {
            run.ServiceClassId = string.IsNullOrWhiteSpace(run.ServiceClassId)
                ? serviceClassId : run.ServiceClassId;
            run.ServicePatternId = string.IsNullOrWhiteSpace(run.ServicePatternId)
                ? servicePatternId : run.ServicePatternId;
            run.VehicleTypeId = string.IsNullOrWhiteSpace(run.VehicleTypeId)
                ? vehicleTypeId : run.VehicleTypeId;
        }

        return run;
    }

    private int? FindStationIndex(SimulationEvent item, IReadOnlyList<Station> stations)
    {
        for (var index = 0; index < stations.Count; index++)
        {
            if (_topology is not null
                ? _topology.MatchesStationEvent(item.Direction, stations[index].StationId, item)
                : Math.Abs(item.PositionMeters - stations[index].PositionMeters) <= PositionEpsilon)
            {
                return index;
            }
        }

        return null;
    }

    private IReadOnlyList<Station> GetStations(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? _outboundStations : _inboundStations;

    private IntervalStatistic CreateInterval(SegmentState segment)
    {
        var run = segment.Run;
        var first = segment.FirstSample!;
        var last = segment.LastSample ?? first;
        var start = segment.DepartureTimeSeconds;
        var end = segment.ArrivalTimeSeconds;
        var complete = start is { } departure && end is { } arrival
            && arrival >= departure - TimeEpsilon;
        var observedStart = segment.ObservedStartTimeSeconds ?? first.SimulationTimeSeconds;
        var observedEnd = segment.ObservedEndTimeSeconds ?? last.SimulationTimeSeconds;
        var distance = complete
            ? Math.Abs(segment.To.PositionMeters - segment.From.PositionMeters)
            : Math.Abs(last.PositionMeters - first.PositionMeters);
        var travelTime = complete ? Math.Max(0, end!.Value - start!.Value) : (double?)null;
        var phaseSeconds = Enum.GetValues<OperationalPhase>()
            .ToDictionary(item => item, item => segment.PhaseSeconds.GetValueOrDefault(item));
        var controls = segment.ControlEvents;

        return new IntervalStatistic
        {
            VehicleId = run.Key.VehicleId,
            ServiceRunId = run.Key.ServiceRunId,
            VehicleTypeId = run.VehicleTypeId,
            ServiceClassId = run.ServiceClassId,
            ServicePatternId = run.ServicePatternId,
            Direction = run.Key.Direction,
            FromStationId = segment.From.StationId,
            FromStationName = segment.From.StationName,
            ToStationId = segment.To.StationId,
            ToStationName = segment.To.StationName,
            DistanceMeters = distance,
            DepartureTimeSeconds = start,
            ArrivalTimeSeconds = end,
            TravelTimeSeconds = travelTime,
            AverageSpeedMetersPerSecond = travelTime is > TimeEpsilon
                ? distance / travelTime.Value : null,
            PeakSpeedMetersPerSecond = segment.PeakSpeedMetersPerSecond,
            EntrySpeedMetersPerSecond = segment.DepartureSpeedMetersPerSecond,
            ExitSpeedMetersPerSecond = segment.ArrivalSpeedMetersPerSecond,
            MinimumEffectiveSpeedLimitMetersPerSecond = GetMinimumSpeedLimit(
                run.Key.Direction, segment.From.PositionMeters, segment.To.PositionMeters),
            IsScheduledStop = segment.IsScheduledStop,
            IsComplete = complete,
            FirstObservedTimeSeconds = observedStart,
            LastObservedTimeSeconds = observedEnd,
            PhaseSeconds = phaseSeconds,
            ControlLimitedSeconds = Math.Clamp(segment.ControlLimitedSeconds, 0,
                Math.Max(0, (end ?? observedEnd) - (start ?? observedStart))),
            ControlEvents = controls
        };
    }

    private JourneyStatistic CreateJourney(RunState run)
    {
        var first = run.FirstSample!;
        var last = run.LastSample ?? first;
        var stations = GetStations(run.Key.Direction);
        var origin = stations[0];
        var terminal = stations[^1];
        var complete = run.OriginDepartureTimeSeconds is { } departure
            && run.TerminalArrivalTimeSeconds is { } arrival
            && arrival >= departure - TimeEpsilon;
        var distance = complete
            ? Math.Abs(terminal.PositionMeters - origin.PositionMeters)
            : Math.Abs(last.PositionMeters - first.PositionMeters);
        var travelTime = complete
            ? Math.Max(0, run.TerminalArrivalTimeSeconds!.Value - run.OriginDepartureTimeSeconds!.Value)
            : (double?)null;
        return new JourneyStatistic
        {
            VehicleId = run.Key.VehicleId,
            ServiceRunId = run.Key.ServiceRunId,
            VehicleTypeId = run.VehicleTypeId,
            ServiceClassId = run.ServiceClassId,
            ServicePatternId = run.ServicePatternId,
            Direction = run.Key.Direction,
            OriginStationId = origin.StationId,
            OriginStationName = origin.StationName,
            TerminalStationId = terminal.StationId,
            TerminalStationName = terminal.StationName,
            DistanceMeters = distance,
            DepartureTimeSeconds = run.OriginDepartureTimeSeconds,
            ArrivalTimeSeconds = run.TerminalArrivalTimeSeconds,
            TravelTimeSeconds = travelTime,
            AverageSpeedMetersPerSecond = travelTime is > TimeEpsilon
                ? distance / travelTime.Value : null,
            IsComplete = complete,
            FirstObservedTimeSeconds = first.SimulationTimeSeconds,
            LastObservedTimeSeconds = last.SimulationTimeSeconds
        };
    }

    private double? GetMinimumSpeedLimit(TrainDirection direction, double first, double last)
    {
        if (_speedLimits.Count == 0)
        {
            return null;
        }

        var start = Math.Min(first, last);
        var end = Math.Max(first, last);
        var values = _speedLimits.Where(limit =>
                (limit.Direction == SpeedLimitDirection.Both
                    || limit.Direction == SpeedLimitDirection.Outbound && direction == TrainDirection.Outbound
                    || limit.Direction == SpeedLimitDirection.Inbound && direction == TrainDirection.Inbound)
                && limit.EndPositionMeters >= start - PositionEpsilon
                && limit.StartPositionMeters <= end + PositionEpsilon)
            .Select(item => item.LimitMetersPerSecond)
            .Where(double.IsFinite)
            .ToArray();
        return values.Length == 0 ? null : values.Min();
    }

    private static IntervalStatisticsSummary CreateSummary(
        IGrouping<(TrainDirection Direction, string FromStationId, string FromStationName,
            string ToStationId, string ToStationName), IntervalStatistic> group)
    {
        var times = group.Select(item => item.TravelTimeSeconds)
            .Where(item => item is not null && double.IsFinite(item.Value))
            .Select(item => item!.Value)
            .OrderBy(item => item)
            .ToArray();
        return new IntervalStatisticsSummary(
            group.Key.Direction,
            group.Key.FromStationId,
            group.Key.FromStationName,
            group.Key.ToStationId,
            group.Key.ToStationName,
            times.Length,
            times.Length == 0 ? null : times.Average(),
            times.Length == 0 ? null : times[0],
            times.Length == 0 ? null : times[^1],
            times.Length == 0 ? null : times[Math.Clamp(
                (int)Math.Ceiling(times.Length * 0.95) - 1, 0, times.Length - 1)]);
    }

    private readonly record struct RunKey(string VehicleId, string ServiceRunId, TrainDirection Direction);

    private readonly record struct SegmentKey(string FromStationId, string ToStationId);

    private readonly record struct ControlEventKey(string VehicleId, TrainDirection Direction);

    /// <summary>
    /// Append-only time index for control events.  Prefix counts make a
    /// segment summary a bounded range query instead of a scan of all events.
    /// </summary>
    private sealed class ControlEventSeries
    {
        private readonly List<double> _times = [];
        private readonly Dictionary<SimulationEventType, List<int>> _prefixCounts =
            ControlEventTypes.ToDictionary(item => item, _ => new List<int> { 0 });

        public HashSet<SegmentState> Segments { get; } = [];

        public void Append(SimulationEvent item)
        {
            _times.Add(item.SimulationTimeSeconds);
            foreach (var eventType in ControlEventTypes)
            {
                var prefix = _prefixCounts[eventType];
                prefix.Add(prefix[^1] + (eventType == item.EventType ? 1 : 0));
            }
        }

        public IntervalControlEventSummary Build(double start, double end)
        {
            if (_times.Count == 0 || end < start - TimeEpsilon)
            {
                return IntervalControlEventSummary.Empty;
            }

            var left = LowerBound(start - TimeEpsilon);
            var right = UpperBound(end + TimeEpsilon);
            if (left >= right)
            {
                return IntervalControlEventSummary.Empty;
            }

            var counts = new Dictionary<SimulationEventType, int>();
            foreach (var eventType in ControlEventTypes)
            {
                var prefix = _prefixCounts[eventType];
                var count = prefix[right] - prefix[left];
                if (count > 0)
                {
                    counts[eventType] = count;
                }
            }

            var total = counts.Values.Sum();
            return total == 0
                ? IntervalControlEventSummary.Empty
                : new IntervalControlEventSummary(total, counts,
                    counts.Keys.OrderBy(item => item).ToArray());
        }

        private int LowerBound(double value)
        {
            var left = 0;
            var right = _times.Count;
            while (left < right)
            {
                var middle = left + (right - left) / 2;
                if (_times[middle] < value)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }

            return left;
        }

        private int UpperBound(double value)
        {
            var left = 0;
            var right = _times.Count;
            while (left < right)
            {
                var middle = left + (right - left) / 2;
                if (_times[middle] <= value)
                {
                    left = middle + 1;
                }
                else
                {
                    right = middle;
                }
            }

            return left;
        }
    }

    private sealed class RunState(
        RunKey key,
        string serviceClassId,
        string servicePatternId,
        string vehicleTypeId)
    {
        public RunKey Key { get; } = key;
        public string ServiceClassId { get; set; } = serviceClassId;
        public string ServicePatternId { get; set; } = servicePatternId;
        public string VehicleTypeId { get; set; } = vehicleTypeId;
        public TrajectorySample? FirstSample { get; set; }
        public TrajectorySample? LastSample { get; set; }
        public SegmentState? ActiveSegment { get; set; }
        public SegmentState? PreviousSegment { get; set; }
        public double? OriginDepartureTimeSeconds { get; set; }
        public double? TerminalArrivalTimeSeconds { get; set; }
        public double? PendingTerminalArrivalTimeSeconds { get; set; }
        public Dictionary<SegmentKey, SegmentState> Segments { get; } = [];
    }

    private readonly record struct SamplePair(TrajectorySample Left, TrajectorySample Right);

    private sealed class SegmentState(RunState run, Station from, Station to)
    {
        public RunState Run { get; } = run;
        public Station From { get; } = from;
        public Station To { get; } = to;
        public TrajectorySample? FirstSample { get; set; }
        public TrajectorySample? LastSample { get; set; }
        public double? ObservedStartTimeSeconds { get; set; }
        public double? ObservedEndTimeSeconds { get; set; }
        public double? StartPositionMeters { get; set; }
        public double? EndPositionMeters { get; set; }
        public double? DepartureTimeSeconds { get; set; }
        public double? ArrivalTimeSeconds { get; set; }
        public double? PendingArrivalTimeSeconds { get; set; }
        public double? PendingArrivalSpeedMetersPerSecond { get; set; }
        public bool? PendingIsScheduledStop { get; set; }
        public double? DepartureSpeedMetersPerSecond { get; set; }
        public double? ArrivalSpeedMetersPerSecond { get; set; }
        public double? PeakSpeedMetersPerSecond { get; set; }
        public bool? IsScheduledStop { get; set; }
        public double ControlLimitedSeconds { get; set; }
        public Dictionary<OperationalPhase, double> PhaseSeconds { get; } = [];
        public List<TrajectorySample> CandidateSamples { get; } = [];
        public List<SamplePair> Pairs { get; } = [];
        public ControlEventSeries? ControlEventSeries { get; set; }
        public IntervalControlEventSummary ControlEvents { get; set; } = IntervalControlEventSummary.Empty;
    }
}
