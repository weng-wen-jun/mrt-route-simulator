using System.Diagnostics;

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
    TopologySimulationDefinition? Topology = null,
    SafetyObservationRetentionPolicy? SafetyObservationRetentionPolicy = null)
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
                ServiceTypes,
                SafetyObservationRetentionPolicy)
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
                ServiceTypes,
                SafetyObservationRetentionPolicy);
        if (InitialBrakingEstimationMode != BrakingEstimationMode.Service)
        {
            world.SetBrakingEstimationMode(InitialBrakingEstimationMode);
        }

        return world;
    }
}

/// <summary>
/// Progress details emitted while the planned world is being advanced to completion.
/// <c>MaximumDurationSeconds</c> is a fail-safe upper bound, not a prediction of
/// the scenario's actual completion time.
/// </summary>
public sealed record SimulationTimelineProgress(
    double SimulationTimeSeconds,
    double MaximumDurationSeconds,
    int CompletedTrainCount,
    int TotalTrainCount,
    int EventCount,
    int TrajectorySampleCount,
    bool IsComplete = false)
{
    public double Ratio => MaximumDurationSeconds <= 0
        ? IsComplete ? 1 : 0
        : Math.Clamp(SimulationTimeSeconds / MaximumDurationSeconds, 0, 1);
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

    /// <summary>Actual completion time of the most recently prepared planned timeline.</summary>
    public double? PlannedTimelineCompletedAtSeconds { get; private set; }

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

        PlannedWorld.Reset();
        PlannedTimelineCompletedAtSeconds = null;
        PlannedWorld.AdvanceTo(durationSeconds);
        CapturePlannedTimeline();
        if (PlannedWorld.IsComplete)
        {
            PlannedTimelineCompletedAtSeconds = PlannedWorld.CurrentTimeSeconds;
        }

        PlannedWorld.Reset();
    }

    /// <summary>
    /// Builds a planned timeline until the scenario's operational state reports completion.
    /// The maximum duration is only a fail-safe and is never used as the planned end time.
    /// </summary>
    public void PreparePlannedTimelineUntilComplete(
        double maxDurationSeconds,
        IProgress<SimulationTimelineProgress>? progress = null,
        TimeSpan? progressInterval = null)
    {
        if (!double.IsFinite(maxDurationSeconds) || maxDurationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDurationSeconds), "計畫時間軸最大長度必須是非負有限數值。");
        }

        var reportEvery = progressInterval ?? TimeSpan.FromSeconds(2);
        if (reportEvery < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(progressInterval), "計畫時間軸進度回報間隔不可為負數。");
        }

        PlannedWorld.Reset();
        PlannedTimelineCompletedAtSeconds = null;
        var stopwatch = Stopwatch.StartNew();
        var nextProgressReport = reportEvery;
        ReportProgress(isComplete: false);
        while (!PlannedWorld.IsComplete
            && PlannedWorld.CurrentTimeSeconds + SimulationWorld.FixedTimeStepSeconds
                <= maxDurationSeconds + 1e-7)
        {
            PlannedWorld.Tick();
            if (reportEvery == TimeSpan.Zero || stopwatch.Elapsed >= nextProgressReport)
            {
                ReportProgress(isComplete: false);
                nextProgressReport = stopwatch.Elapsed + reportEvery;
            }
        }

        if (!PlannedWorld.IsComplete)
        {
            var pendingCount = PlannedWorld.GetSnapshot().Trains.Count(train =>
                train.Phase != OperationalPhase.OutOfService);
            PlannedWorld.Reset();
            throw new SimulationValidationException([
                $"計畫時間軸在 {maxDurationSeconds:0.0} 秒 fail-safe 上限內未完成；仍有 {pendingCount} 個車次尚未結束。"
            ]);
        }

        PlannedTimelineCompletedAtSeconds = PlannedWorld.CurrentTimeSeconds;
        CapturePlannedTimeline();
        ReportProgress(isComplete: true);
        PlannedWorld.Reset();

        void ReportProgress(bool isComplete)
        {
            if (progress is null)
            {
                return;
            }

            var snapshot = PlannedWorld.GetSnapshot();
            progress.Report(new SimulationTimelineProgress(
                PlannedWorld.CurrentTimeSeconds,
                maxDurationSeconds,
                snapshot.Trains.Count(train => train.Phase == OperationalPhase.OutOfService),
                snapshot.Trains.Count,
                PlannedWorld.Events.Count,
                PlannedWorld.Trajectory.Count,
                isComplete));
        }
    }

    private void CapturePlannedTimeline()
    {
        PlannedEvents = PlannedWorld.Events.ToArray();
        PlannedTrajectory = PlannedWorld.Trajectory.ToArray();
    }

    public void Reset()
    {
        ActualWorld.Reset();
        PlannedWorld.Reset();
    }
}
