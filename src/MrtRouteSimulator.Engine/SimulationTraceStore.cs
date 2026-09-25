namespace MrtRouteSimulator.Engine;

/// <summary>
/// Owns high-volume trajectory retention independently from the simulation state machine.
/// It never changes simulation time, events or safety observations.
/// </summary>
internal sealed class SimulationTraceStore
{
    private const double NumericalTolerance = 1e-7;
    private readonly List<TrajectorySample> _trajectory = [];
    private readonly Dictionary<TrajectorySampleKey, TrajectorySample> _lastRecordedSamples = [];
    private readonly HashSet<TrajectorySampleKey> _keysWithNewEvents = [];

    public SimulationTraceStore(SimulationTraceRetentionPolicy policy)
    {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public SimulationTraceRetentionPolicy Policy { get; }

    public IReadOnlyList<TrajectorySample> Trajectory => _trajectory;

    public void MarkEvent(string vehicleId, string serviceRunId)
    {
        if (!string.IsNullOrWhiteSpace(vehicleId))
        {
            _keysWithNewEvents.Add(new TrajectorySampleKey(vehicleId, serviceRunId));
        }
    }

    public void Record(TrajectorySample sample)
    {
        var key = new TrajectorySampleKey(sample.VehicleId, sample.ServiceRunId);
        if (!ShouldRecord(key, sample))
        {
            return;
        }

        _trajectory.Add(sample);
        _lastRecordedSamples[key] = sample;
    }

    public void Reset()
    {
        _trajectory.Clear();
        _lastRecordedSamples.Clear();
        _keysWithNewEvents.Clear();
    }

    private bool ShouldRecord(TrajectorySampleKey key, TrajectorySample sample)
    {
        if (Policy.Mode == SimulationTraceRetentionMode.EventsOnly)
        {
            _keysWithNewEvents.Remove(key);
            return false;
        }

        if (Policy.Mode == SimulationTraceRetentionMode.Full)
        {
            _keysWithNewEvents.Remove(key);
            return true;
        }

        var hasNewEvent = _keysWithNewEvents.Remove(key);
        if (!_lastRecordedSamples.TryGetValue(key, out var previous))
        {
            return true;
        }

        return hasNewEvent
            || sample.SimulationTimeSeconds - previous.SimulationTimeSeconds
                >= Policy.MinimumSampleIntervalSeconds - NumericalTolerance
            || HasStateChanged(previous, sample);
    }

    private static bool HasStateChanged(TrajectorySample previous, TrajectorySample current) =>
        previous.Phase != current.Phase
        || previous.Direction != current.Direction
        || !string.Equals(previous.TrackId, current.TrackId, StringComparison.Ordinal)
        || !string.Equals(previous.TrackEdgeId, current.TrackEdgeId, StringComparison.Ordinal)
        || previous.ServiceRouteTraversalIndex != current.ServiceRouteTraversalIndex
        || !string.Equals(previous.CurrentStationId, current.CurrentStationId, StringComparison.Ordinal)
        || !string.Equals(previous.NextStationId, current.NextStationId, StringComparison.Ordinal)
        || !string.Equals(previous.PlatformId, current.PlatformId, StringComparison.Ordinal)
        || previous.Constraints != current.Constraints;

    private readonly record struct TrajectorySampleKey(string VehicleId, string ServiceRunId);
}
