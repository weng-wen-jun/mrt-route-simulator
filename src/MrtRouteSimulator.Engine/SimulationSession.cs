namespace MrtRouteSimulator.Engine;

/// <summary>
/// Immutable construction data for a V2 <see cref="SimulationWorld"/>. It keeps the WPF layer
/// from owning the engine constructor's cross-cutting settings.
/// </summary>
public sealed record SimulationWorldOptions(
    Route? Route,
    TrainParameters TrainParameters,
    OperationalParameters OperationalParameters,
    int TrainCount,
    double? InitialDepartureIntervalSeconds = null,
    IReadOnlyList<SpeedLimitSegment>? SpeedLimits = null,
    OperationProfileMode ProfileMode = OperationProfileMode.RealisticOperations,
    MovingBlockMode MovingBlockMode = MovingBlockMode.Control,
    IReadOnlyList<ServicePattern>? ServicePatterns = null,
    IReadOnlyList<ServiceRunPlan>? ServiceRunPlans = null,
    ResolvedDispatchPlan? DispatchPlan = null,
    IReadOnlyList<VehicleTypeDefinition>? VehicleTypes = null,
    InfrastructureGraph? Infrastructure = null,
    BrakingEstimationMode InitialBrakingEstimationMode = BrakingEstimationMode.Service,
    SimulationTraceRetentionPolicy? TraceRetentionPolicy = null,
    IReadOnlyList<ServiceTypeDefinition>? ServiceTypes = null,
    TopologySimulationDefinition? Topology = null)
{
    public SimulationWorld CreateWorld()
    {
        var world = Topology is null
            ? new SimulationWorld(
                Route ?? throw new SimulationValidationException([
                    "非 topology V2 world 必須提供 Route；Schema 8 請提供 TopologySimulationDefinition。"
                ]),
                TrainParameters,
                OperationalParameters,
                TrainCount,
                InitialDepartureIntervalSeconds,
                SpeedLimits,
                ProfileMode,
                MovingBlockMode,
                ServicePatterns,
                ServiceRunPlans,
                DispatchPlan,
                VehicleTypes,
                Infrastructure,
                TraceRetentionPolicy,
                ServiceTypes)
            : new SimulationWorld(
                Topology,
                TrainParameters,
                OperationalParameters,
                TrainCount,
                InitialDepartureIntervalSeconds,
                SpeedLimits,
                ProfileMode,
                MovingBlockMode,
                ServicePatterns,
                ServiceRunPlans,
                DispatchPlan,
                VehicleTypes,
                Infrastructure,
                TraceRetentionPolicy,
                ServiceTypes);
        if (InitialBrakingEstimationMode != BrakingEstimationMode.Service)
        {
            world.SetBrakingEstimationMode(InitialBrakingEstimationMode);
        }

        return world;
    }
}

/// <summary>
/// Owns the paired actual and planned V2 worlds used by presentation, timetables and diagrams.
/// Both worlds advance through the same fixed-tick engine; WPF only renders the resulting snapshot.
/// </summary>
public sealed class SimulationSession
{
    public SimulationSession(SimulationWorldOptions actualOptions, SimulationWorldOptions plannedOptions)
    {
        ArgumentNullException.ThrowIfNull(actualOptions);
        ArgumentNullException.ThrowIfNull(plannedOptions);
        ActualWorld = actualOptions.CreateWorld();
        PlannedWorld = plannedOptions.CreateWorld();
    }

    public SimulationWorld ActualWorld { get; }

    public SimulationWorld PlannedWorld { get; }

    public IReadOnlyList<SimulationEvent> PlannedEvents { get; private set; } = [];

    public IReadOnlyList<TrajectorySample> PlannedTrajectory { get; private set; } = [];

    public SimulationSnapshot AdvanceTo(double targetTimeSeconds)
    {
        ActualWorld.AdvanceTo(targetTimeSeconds);
        PlannedWorld.AdvanceTo(targetTimeSeconds);
        return ActualWorld.GetSnapshot();
    }

    /// <summary>
    /// Advances only the interactive world. The planned world is prepared separately and its
    /// immutable timeline must not be recomputed during normal WPF playback.
    /// </summary>
    public SimulationSnapshot AdvanceActualTo(double targetTimeSeconds)
    {
        ActualWorld.AdvanceTo(targetTimeSeconds);
        return ActualWorld.GetSnapshot();
    }

    public void PreparePlannedTimeline(double durationSeconds)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "計畫時間軸長度必須是非負有限數值。");
        }

        PlannedWorld.AdvanceTo(durationSeconds);
        PlannedEvents = PlannedWorld.Events.ToArray();
        PlannedTrajectory = PlannedWorld.Trajectory.ToArray();
        PlannedWorld.Reset();
    }

    public void Reset()
    {
        ActualWorld.Reset();
        PlannedWorld.Reset();
    }
}
