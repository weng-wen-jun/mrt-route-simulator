using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Presentation-only, append-only source cache for the time-distance diagram.
/// It never changes the Engine trajectory; it only keeps a bounded display view
/// of the samples that have already arrived from an immutable source list.
/// </summary>
/// <remarks>
/// The nominal budget applies to ordinary points only.  Critical transition,
/// extrema, first and last points are deliberately never discarded.  Therefore
/// a pathological stream with more critical points than the nominal budget can
/// exceed the displayed point budget: a finite strict budget cannot preserve
/// every required critical point at the same time.
/// </remarks>
public sealed class TimeDistanceTrajectoryCache
{
    public const int DefaultOrdinaryPointBudget = 600;
    public const int DefaultOrdinarySamplingStride = 1;

    private readonly int _ordinaryPointBudget;
    private readonly int _ordinarySamplingStride;
    private readonly Dictionary<SeriesKey, SeriesState> _states = [];
    private readonly List<TimeDistanceDisplaySeries> _groups = [];

    private int _processedCount;
    private long _version;

    public TimeDistanceTrajectoryCache(
        int ordinaryPointBudget = DefaultOrdinaryPointBudget,
        int ordinarySamplingStride = DefaultOrdinarySamplingStride)
    {
        if (ordinaryPointBudget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinaryPointBudget));
        }

        if (ordinarySamplingStride < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinarySamplingStride));
        }

        _ordinaryPointBudget = ordinaryPointBudget;
        _ordinarySamplingStride = ordinarySamplingStride;
    }

    /// <summary>Groups in first-seen order, keyed by vehicle, service run and direction.</summary>
    public IReadOnlyList<TimeDistanceDisplaySeries> Groups => _groups;

    /// <summary>The cache generation. It changes after a successful append or reset.</summary>
    public long Version => _version;

    /// <summary>Number of source indices consumed by this cache generation.</summary>
    public int ProcessedCount => _processedCount;

    /// <summary>Alias useful to callers that describe the cursor as total processed samples.</summary>
    public int TotalProcessed => _processedCount;

    public bool HasData => _processedCount > 0;

    /// <summary>Minimum position observed in the processed source; zero when empty.</summary>
    public double MinPosition { get; private set; }

    /// <summary>Maximum position observed in the processed source; zero when empty.</summary>
    public double MaxPosition { get; private set; }

    /// <summary>Maximum simulation time observed in the processed source; zero when empty.</summary>
    public double MaxTime { get; private set; }

    /// <summary>Minimum simulation time observed in the processed source; zero when empty.</summary>
    public double MinTime { get; private set; }

    public int OrdinaryPointBudget => _ordinaryPointBudget;

    /// <summary>
    /// Appends only source indices after <see cref="ProcessedCount"/>. If the
    /// source shrinks, the old source artifact is invalid and the cache starts
    /// a new generation before consuming the replacement source.
    /// </summary>
    public void Append(IReadOnlyList<TrajectorySample> source) =>
        Append(source, CancellationToken.None);

    /// <summary>
    /// Appends source samples while allowing a background display-cache build to
    /// stop without touching the UI-owned cache.  Cancellation never changes
    /// the source list or the cache algorithm; a partially built cache is
    /// discarded by its caller.
    /// </summary>
    public void Append(
        IReadOnlyList<TrajectorySample> source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Count < _processedCount)
        {
            Reset();
        }

        if (source.Count == _processedCount)
        {
            return;
        }

        for (var index = _processedCount; index < source.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = source[index];
            if (_processedCount == 0 && index == 0)
            {
                MinPosition = sample.PositionMeters;
                MaxPosition = sample.PositionMeters;
                MinTime = sample.SimulationTimeSeconds;
                MaxTime = sample.SimulationTimeSeconds;
            }
            else
            {
                MinPosition = Math.Min(MinPosition, sample.PositionMeters);
                MaxPosition = Math.Max(MaxPosition, sample.PositionMeters);
                MinTime = Math.Min(MinTime, sample.SimulationTimeSeconds);
                MaxTime = Math.Max(MaxTime, sample.SimulationTimeSeconds);
            }

            var key = new SeriesKey(sample.VehicleId, sample.ServiceRunId, sample.Direction);
            if (!_states.TryGetValue(key, out var state))
            {
                var series = new TimeDistanceDisplaySeries(
                    sample.VehicleId,
                    sample.ServiceRunId,
                    sample.Direction);
                state = new SeriesState(series, _ordinaryPointBudget, _ordinarySamplingStride);
                _states.Add(key, state);
                _groups.Add(series);
            }

            state.Append(index, sample);
            _processedCount = index + 1;
        }

        foreach (var state in _states.Values)
        {
            state.Flush();
        }

        _version++;
    }

    /// <summary>Starts a new source-artifact generation and drops all old groups.</summary>
    public void Reset()
    {
        _states.Clear();
        _groups.Clear();
        _processedCount = 0;
        MinPosition = 0;
        MaxPosition = 0;
        MinTime = 0;
        MaxTime = 0;
        _version++;
    }

    private readonly record struct SeriesKey(
        string VehicleId,
        string ServiceRunId,
        TrainDirection Direction);

    private sealed class SeriesState
    {
        private readonly TimeDistanceDisplaySeries _series;
        private readonly int _ordinaryPointBudget;
        private long _ordinarySamplingStride;
        private readonly SortedDictionary<int, TrajectorySample> _critical = [];
        private readonly SortedDictionary<int, OrdinaryPoint> _ordinary = [];
        private readonly List<TrajectorySample> _displayPoints = [];

        private TrajectorySample? _previousPrevious;
        private TrajectorySample? _previous;
        private int _previousIndex = -1;
        private TrajectorySample? _first;
        private int _firstIndex = -1;
        private TrajectorySample? _last;
        private int _lastIndex = -1;
        private int _localSampleCount;
        private bool _dirty;

        public SeriesState(
            TimeDistanceDisplaySeries series,
            int ordinaryPointBudget,
            int ordinarySamplingStride)
        {
            _series = series;
            _ordinaryPointBudget = ordinaryPointBudget;
            _ordinarySamplingStride = ordinarySamplingStride;
        }

        public void Append(int sourceIndex, TrajectorySample sample)
        {
            if (_localSampleCount == 0)
            {
                _first = sample;
                _firstIndex = sourceIndex;
                MarkCritical(sourceIndex, sample);
            }
            else
            {
                if (HasCriticalTransition(_previous!, sample))
                {
                    // Keep both sides of a visual boundary. This is especially
                    // important for a short station/track/speed-limit segment.
                    MarkCritical(_previousIndex, _previous!);
                    MarkCritical(sourceIndex, sample);
                }

                if (_previousPrevious is not null
                    && (IsLocalExtremum(
                            _previousPrevious.PositionMeters,
                            _previous!.PositionMeters,
                            sample.PositionMeters)
                        || IsLocalExtremum(
                            _previousPrevious.SpeedMetersPerSecond,
                            _previous.SpeedMetersPerSecond,
                            sample.SpeedMetersPerSecond)))
                {
                    MarkCritical(_previousIndex, _previous!);
                }
            }

            // Keep ordinary points losslessly until the first overflow. After
            // that, the per-series hierarchical stride controls future points.
            // Overflow thinning only scans the already bounded ordinary set;
            // it never re-decimates the complete source history.
            if (!_critical.ContainsKey(sourceIndex)
                && _ordinaryPointBudget > 0
                && _localSampleCount % _ordinarySamplingStride == 0)
            {
                _ordinary[sourceIndex] = new OrdinaryPoint(sample, _localSampleCount);
                _dirty = true;
                ThinOrdinaryPointsIfNeeded();
            }

            _last = sample;
            _lastIndex = sourceIndex;
            _previousPrevious = _previous;
            _previous = sample;
            _previousIndex = sourceIndex;
            _localSampleCount++;
            _dirty = true;
        }

        public void Flush()
        {
            if (!_dirty)
            {
                return;
            }

            var points = new SortedDictionary<int, TrajectorySample>();
            foreach (var item in _ordinary)
            {
                points[item.Key] = item.Value.Sample;
            }

            foreach (var item in _critical)
            {
                points[item.Key] = item.Value;
            }

            if (_first is not null)
            {
                points[_firstIndex] = _first;
            }

            if (_last is not null)
            {
                // The last source point is pinned even when it is not a stride
                // or critical point. It can be demoted on the next append.
                points[_lastIndex] = _last;
            }

            _displayPoints.Clear();
            _displayPoints.AddRange(points.Values);
            _series.ReplaceSamples(
                Array.AsReadOnly(_displayPoints.ToArray()),
                _critical.Count,
                _ordinary.Count,
                _localSampleCount);
            _dirty = false;
        }

        private void MarkCritical(int index, TrajectorySample sample)
        {
            _critical[index] = sample;
            _ordinary.Remove(index);
            _dirty = true;
        }

        private void ThinOrdinaryPointsIfNeeded()
        {
            while (_ordinary.Count > _ordinaryPointBudget)
            {
                // Double the per-series stride and retain a deterministic
                // multiresolution skeleton. Only the already bounded ordinary
                // set is inspected; the source history is never re-decimated.
                _ordinarySamplingStride = Math.Min(
                    long.MaxValue / 2,
                    Math.Max(_ordinarySamplingStride + 1, _ordinarySamplingStride * 2));
                var remove = _ordinary
                    .Where(item => item.Value.LocalIndex % _ordinarySamplingStride != 0)
                    .Select(item => item.Key)
                    .ToArray();
                foreach (var sourceIndex in remove)
                {
                    _ordinary.Remove(sourceIndex);
                }

                // The source has at most Int32.MaxValue indices, so a stride
                // larger than that leaves at most one ordinary point. The
                // guard makes the loop explicit even for a custom budget.
                if (remove.Length == 0 && _ordinarySamplingStride >= int.MaxValue)
                {
                    break;
                }
            }
        }

        private static bool HasCriticalTransition(
            TrajectorySample previous,
            TrajectorySample current) =>
            previous.Phase != current.Phase
            || previous.Direction != current.Direction
            || !string.Equals(previous.CurrentStationId, current.CurrentStationId, StringComparison.Ordinal)
            || !string.Equals(previous.NextStationId, current.NextStationId, StringComparison.Ordinal)
            || !string.Equals(previous.TrackId, current.TrackId, StringComparison.Ordinal)
            || !string.Equals(previous.TrackEdgeId, current.TrackEdgeId, StringComparison.Ordinal)
            || previous.TrackSpeedLimitMetersPerSecond != current.TrackSpeedLimitMetersPerSecond
            || AccelerationRegime(previous.AccelerationMetersPerSecondSquared)
                != AccelerationRegime(current.AccelerationMetersPerSecondSquared);

        private static int AccelerationRegime(double acceleration) =>
            acceleration > 1e-9 ? 1 : acceleration < -1e-9 ? -1 : 0;

        private static bool IsLocalExtremum(double previous, double current, double next) =>
            current > previous && current >= next
            || current < previous && current <= next;

        private readonly record struct OrdinaryPoint(
            TrajectorySample Sample,
            int LocalIndex);
    }
}

/// <summary>A bounded display series owned by <see cref="TimeDistanceTrajectoryCache"/>.</summary>
public sealed class TimeDistanceDisplaySeries
{
    private IReadOnlyList<TrajectorySample> _samples = Array.Empty<TrajectorySample>();

    internal TimeDistanceDisplaySeries(
        string vehicleId,
        string serviceRunId,
        TrainDirection direction)
    {
        VehicleId = vehicleId;
        ServiceRunId = serviceRunId;
        Direction = direction;
    }

    public string VehicleId { get; }

    public string ServiceRunId { get; }

    public TrainDirection Direction { get; }

    /// <summary>Changes whenever this group's display point collection is rebuilt.</summary>
    public long Version { get; private set; }

    public IReadOnlyList<TrajectorySample> Samples => _samples;

    /// <summary>Semantic alias for callers that refer to the polyline input as points.</summary>
    public IReadOnlyList<TrajectorySample> Points => _samples;

    public int CriticalPointCount { get; private set; }

    public int OrdinaryPointCount { get; private set; }

    public int SourceSampleCount { get; private set; }

    public int DisplayPointCount => _samples.Count;

    internal void ReplaceSamples(
        IReadOnlyList<TrajectorySample> samples,
        int criticalPointCount,
        int ordinaryPointCount,
        int sourceSampleCount)
    {
        _samples = samples;
        CriticalPointCount = criticalPointCount;
        OrdinaryPointCount = ordinaryPointCount;
        SourceSampleCount = sourceSampleCount;
        Version++;
    }
}
