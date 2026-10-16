namespace MrtRouteSimulator.Engine;

public sealed partial class SimulationWorld
{
    private readonly List<SignalBlockState> _signalBlocks = [];
    private double _signalStateTime = double.NaN;

    private sealed class SignalBlockState(TrackSignalBlockDefinition definition)
    {
        public TrackSignalBlockDefinition Definition { get; } = definition;
        public HashSet<string> Occupants { get; } = new(StringComparer.Ordinal);
        public string? Owner { get; set; }
        public bool OwnerEntered { get; set; }
        public double ReadyAt { get; set; }
        public string? LastClearedServiceRouteId { get; set; }
    }

    private void InitializeSignalBlocks(IReadOnlyList<TrackSignalBlockDefinition>? signals)
    {
        TrackSignalBlockValidation.Validate(signals ?? [], TopologyInfrastructure);
        _signalBlocks.AddRange((signals ?? []).Select(signal => new SignalBlockState(signal)));
    }

    private void RefreshSignalBlockStates()
    {
        if (_signalStateTime == CurrentTimeSeconds) return;
        _signalStateTime = CurrentTimeSeconds;
        foreach (var state in _signalBlocks)
        {
            var wasOccupied = state.Occupants.Count > 0;
            var previousOccupants = state.Definition.DelayOnlyOnRouteChange ? state.Occupants.ToArray() : [];
            state.Occupants.Clear();
            foreach (var train in _trains.Where(train => train.Active && HasTopologyTraversalPosition(train)))
            {
                var footprint = GetMovementNavigator(train).CreateFootprint(train.RuntimeTopologyCursor!.Value, GetVehiclePerformance(train).LengthMeters);
                if (footprint.OccupiedIntervals.Any(interval => state.Definition.ProtectedSections.Any(section =>
                    section.TrackEdgeId.Equals(interval.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                    && Math.Min(section.EndOffsetMeters, interval.EndOffsetMeters) - Math.Max(section.StartOffsetMeters, interval.StartOffsetMeters) > NumericalTolerance)))
                    state.Occupants.Add(train.VehicleId);
            }
            if (wasOccupied && state.Occupants.Count == 0)
            {
                state.ReadyAt = CurrentTimeSeconds + state.Definition.ClearanceDelaySeconds;
                if (state.Definition.DelayOnlyOnRouteChange)
                {
                    var previous = previousOccupants.Length == 1 ? _trains.FirstOrDefault(train => train.VehicleId == previousOccupants[0]) : null;
                    state.LastClearedServiceRouteId = previous is null ? null : GetServiceRouteId(previous);
                }
            }
            if (state.Owner is { } owner)
            {
                if (state.Occupants.Contains(owner)) state.OwnerEntered = true;
                if (state.OwnerEntered && !state.Occupants.Contains(owner)
                    || !_trains.Any(train => train.Active && train.VehicleId == owner))
                {
                    state.Owner = null;
                    state.OwnerEntered = false;
                }
            }
        }
    }

    private void ResetSignalBlocks()
    {
        _signalStateTime = double.NaN;
        foreach (var state in _signalBlocks)
        {
            state.Occupants.Clear(); state.Owner = null; state.OwnerEntered = false; state.ReadyAt = 0; state.LastClearedServiceRouteId = null;
        }
    }

    private bool SignalClearanceIsPending(SignalBlockState state, MutableTrain train) =>
        CurrentTimeSeconds + NumericalTolerance < state.ReadyAt
        && (!state.Definition.DelayOnlyOnRouteChange || state.LastClearedServiceRouteId is null
            || !state.LastClearedServiceRouteId.Equals(GetServiceRouteId(train), StringComparison.OrdinalIgnoreCase));

    private bool HasConflictingSignalOwner(SignalBlockState state, string vehicleId) => _signalBlocks.Any(peer =>
        peer.Owner is { } owner && owner != vehicleId && peer.Definition.ProtectedSections.Any(a => state.Definition.ProtectedSections.Any(b =>
            a.TrackEdgeId.Equals(b.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
            && Math.Min(a.EndOffsetMeters, b.EndOffsetMeters) - Math.Max(a.StartOffsetMeters, b.StartOffsetMeters) > NumericalTolerance)));

    private double? GetSignalBlockMovementAuthority(MutableTrain train)
    {
        if (_signalBlocks.Count == 0 || !HasTopologyTraversalPosition(train)) return null;
        RefreshSignalBlockStates();
        var navigator = GetMovementNavigator(train);
        var cursor = train.RuntimeTopologyCursor!.Value;
        double? nearest = null;
        foreach (var state in _signalBlocks)
        {
            if (!state.Occupants.Any(owner => owner != train.VehicleId)
                && !HasConflictingSignalOwner(state, train.VehicleId)
                && !SignalClearanceIsPending(state, train)) continue;
            for (var li = cursor.MovementLegIndex; li < navigator.Legs.Count; li++)
                for (var ti = li == cursor.MovementLegIndex ? cursor.TraversalIndex : 0; ti < navigator.Legs[li].Traversals.Count; ti++)
                {
                    var traversal = navigator.Legs[li].Traversals[ti];
                    if (traversal.Direction != state.Definition.Direction
                        || !traversal.Edge.TrackEdgeId.Equals(state.Definition.StopPosition.TrackEdgeId, StringComparison.OrdinalIgnoreCase)) continue;
                    var target = navigator.CreateCursor(li, ti, traversal.Direction == TraversalDirection.Forward
                        ? state.Definition.StopPosition.OffsetMeters : traversal.LengthMeters - state.Definition.StopPosition.OffsetMeters);
                    var distance = navigator.TryGetForwardDistance(cursor, target);
                    if (distance is >= 0 && (nearest is null || distance < nearest)) nearest = distance;
                }
        }
        return nearest;
    }

    private double ApplySignalBlockSpeedLimit(MutableTrain train, double permittedSpeed)
    {
        if (_signalBlocks.Count == 0 || !HasTopologyTraversalPosition(train)) return permittedSpeed;
        RefreshSignalBlockStates();
        var navigator = GetMovementNavigator(train);
        var cursor = train.RuntimeTopologyCursor!.Value;
        foreach (var state in _signalBlocks)
        {
            double? distance = null;
            for (var legIndex = cursor.MovementLegIndex; legIndex < navigator.Legs.Count; legIndex++)
            {
                var leg = navigator.Legs[legIndex];
                for (var index = legIndex == cursor.MovementLegIndex ? cursor.TraversalIndex : 0; index < leg.Traversals.Count; index++)
                {
                    var traversal = leg.Traversals[index];
                    if (traversal.Direction != state.Definition.Direction
                        || !traversal.Edge.TrackEdgeId.Equals(state.Definition.StopPosition.TrackEdgeId, StringComparison.OrdinalIgnoreCase)) continue;
                    var target = navigator.CreateCursor(legIndex, index, traversal.Direction == TraversalDirection.Forward
                        ? state.Definition.StopPosition.OffsetMeters : traversal.LengthMeters - state.Definition.StopPosition.OffsetMeters);
                    var candidate = navigator.TryGetForwardDistance(cursor, target);
                    if (candidate is >= 0 && (distance is null || candidate < distance)) distance = candidate;
                }
            }
            if (distance is not { } ahead) continue;
            var blocked = state.Occupants.Any(owner => owner != train.VehicleId)
                || HasConflictingSignalOwner(state, train.VehicleId)
                || SignalClearanceIsPending(state, train);
            if (blocked)
            {
                permittedSpeed = Math.Min(permittedSpeed, CalculateStopCurveSpeed(train, ahead / state.Definition.BrakingSafetyFactor));
                train.Constraints |= OperationalConstraint.RouteResource;
            }
            else if (state.Owner is null && ahead <= Math.Max(5, train.Speed * FixedTimeStepSeconds + 2))
            {
                // Atomic admission before the head crosses; the lock survives until rear-clear.
                state.Owner = train.VehicleId;
                state.OwnerEntered = false;
            }
        }
        return permittedSpeed;
    }
}
