namespace MrtRouteSimulator.Engine;

public enum OperationProfileMode
{
    BasicPhysics,
    RealisticOperations
}

public enum SpeedLimitDirection
{
    Both,
    Outbound,
    Inbound
}

public enum MovingBlockMode
{
    Independent,
    Monitoring,
    Control
}

public enum BrakingEstimationMode
{
    Service,
    Emergency
}

public enum StationServiceMode
{
    Stop,
    Pass
}

public enum OperationalPhase
{
    Pending,
    Dwelling,
    Accelerating,
    Cruising,
    Coasting,
    Braking,
    ApproachBraking,
    Arriving,
    Turning,
    EmergencyStopped,
    Collided,
    OutOfService
}

public enum SafetyStatus
{
    Safe,
    Caution,
    BrakingRequired,
    EnvelopeIntrusion
}

public enum SimulationEventType
{
    Departure,
    Arrival,
    DwellStarted,
    TurnaroundStarted,
    DirectionChanged,
    SafetyStatusChanged,
    ControlBraking,
    ObstacleEmergencyStop,
    PredictedCollision,
    Collision,
    BrakingModeChanged,
    StationPassed,
    StationStopViolation
}

public sealed record SpeedLimitSegment(
    double StartPositionMeters,
    double EndPositionMeters,
    double LimitMetersPerSecond,
    SpeedLimitDirection Direction = SpeedLimitDirection.Both,
    string Note = "");

public sealed record StationServiceInstruction(
    string StationId,
    StationServiceMode Mode,
    double? SpeedLimitMetersPerSecond = null);

public sealed record ServicePattern(
    string PatternId,
    string PatternName,
    IReadOnlyList<StationServiceInstruction> Instructions);

public sealed record ServiceRunPlan(
    string VehicleId,
    int ServiceNumber,
    TrainDirection Direction,
    string ServiceClassId,
    string PatternId);

public sealed class OperationalParameters
{
    public OperationalParameters(
        double jerkMetersPerSecondCubed,
        double coastingRatio,
        double approachDistanceMeters,
        double approachSpeedMetersPerSecond,
        double tractionFadeRatio,
        double trainLengthMeters,
        double serviceBrakingMetersPerSecondSquared,
        double emergencyBrakingMetersPerSecondSquared,
        double controlReactionTimeSeconds,
        double brakeBuildUpTimeSeconds,
        double positioningErrorMeters,
        double safetyMarginMeters,
        double absoluteMinimumGapMeters)
    {
        JerkMetersPerSecondCubed = jerkMetersPerSecondCubed;
        CoastingRatio = coastingRatio;
        ApproachDistanceMeters = approachDistanceMeters;
        ApproachSpeedMetersPerSecond = approachSpeedMetersPerSecond;
        TractionFadeRatio = tractionFadeRatio;
        TrainLengthMeters = trainLengthMeters;
        ServiceBrakingMetersPerSecondSquared = serviceBrakingMetersPerSecondSquared;
        EmergencyBrakingMetersPerSecondSquared = emergencyBrakingMetersPerSecondSquared;
        ControlReactionTimeSeconds = controlReactionTimeSeconds;
        BrakeBuildUpTimeSeconds = brakeBuildUpTimeSeconds;
        PositioningErrorMeters = positioningErrorMeters;
        SafetyMarginMeters = safetyMarginMeters;
        AbsoluteMinimumGapMeters = absoluteMinimumGapMeters;
        V2Validator.Validate(this);
    }

    public double JerkMetersPerSecondCubed { get; }

    public double CoastingRatio { get; }

    public double ApproachDistanceMeters { get; }

    public double ApproachSpeedMetersPerSecond { get; }

    public double TractionFadeRatio { get; }

    public double TrainLengthMeters { get; }

    public double ServiceBrakingMetersPerSecondSquared { get; }

    public double EmergencyBrakingMetersPerSecondSquared { get; }

    public double ControlReactionTimeSeconds { get; }

    public double BrakeBuildUpTimeSeconds { get; }

    public double PositioningErrorMeters { get; }

    public double SafetyMarginMeters { get; }

    public double AbsoluteMinimumGapMeters { get; }

    public static OperationalParameters CreateDefault() => new(
        jerkMetersPerSecondCubed: 0.65,
        coastingRatio: 0.15,
        approachDistanceMeters: 180,
        approachSpeedMetersPerSecond: 0,
        tractionFadeRatio: 0.45,
        trainLengthMeters: 92,
        serviceBrakingMetersPerSecondSquared: 0.9,
        emergencyBrakingMetersPerSecondSquared: 1.3,
        controlReactionTimeSeconds: 1.5,
        brakeBuildUpTimeSeconds: 0.8,
        positioningErrorMeters: 3,
        safetyMarginMeters: 25,
        absoluteMinimumGapMeters: 15);
}

public sealed record WorldTrainState(
    string VehicleId,
    string ServiceRunId,
    string ServiceClassId,
    string ServicePatternId,
    TrainDirection Direction,
    string TrackId,
    double FrontPositionMeters,
    double RearPositionMeters,
    double SpeedMetersPerSecond,
    double AccelerationMetersPerSecondSquared,
    OperationalPhase Phase,
    string CurrentStationId,
    string? NextStationId,
    bool IsActive,
    double SimulationTimeSeconds);

public sealed record SafetyObservation(
    double SimulationTimeSeconds,
    string FollowerVehicleId,
    string LeaderVehicleId,
    TrainDirection Direction,
    string TrackId,
    double FollowerFrontPositionMeters,
    double LeaderRearPositionMeters,
    double HeadToHeadDistanceMeters,
    double ActualGapMeters,
    double TimeGapSeconds,
    double DynamicSafetyDistanceMeters,
    double ObstacleBrakingDemandMeters,
    double PredictedStopPositionMeters,
    double SafetyMarginMeters,
    double PredictedIntrusionMeters,
    double RemainingReactionTimeSeconds,
    SafetyStatus Status,
    BrakingEstimationMode BrakingMode);

public sealed record TrajectorySample(
    double SimulationTimeSeconds,
    string VehicleId,
    string ServiceRunId,
    string ServiceClassId,
    string ServicePatternId,
    TrainDirection Direction,
    string TrackId,
    double PositionMeters,
    double SpeedMetersPerSecond,
    double AccelerationMetersPerSecondSquared,
    OperationalPhase Phase,
    string CurrentStationId,
    string? NextStationId,
    bool IsPlanned);

public sealed record SimulationEvent(
    double SimulationTimeSeconds,
    SimulationEventType EventType,
    string VehicleId,
    string? RelatedVehicleId,
    TrainDirection Direction,
    string TrackId,
    double PositionMeters,
    double SpeedMetersPerSecond,
    string Message);

public sealed record SimulationSnapshot(
    double SimulationTimeSeconds,
    IReadOnlyList<WorldTrainState> Trains,
    IReadOnlyList<SafetyObservation> SafetyObservations,
    IReadOnlyList<SimulationEvent> NewEvents);

internal static class V2Validator
{
    internal static void Validate(OperationalParameters parameters)
    {
        var errors = new List<string>();
        RouteValidator.RequirePositiveFinite(parameters.JerkMetersPerSecondCubed, "Jerk", errors);
        RequireRatio(parameters.CoastingRatio, "惰行比例", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.ApproachDistanceMeters, "進站控制距離", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.ApproachSpeedMetersPerSecond, "進站控制速度", errors);
        RequireRatio(parameters.TractionFadeRatio, "牽引力遞減比例", errors);
        RouteValidator.RequirePositiveFinite(parameters.TrainLengthMeters, "車長", errors);
        RouteValidator.RequirePositiveFinite(parameters.ServiceBrakingMetersPerSecondSquared, "營運煞車減速度", errors);
        RouteValidator.RequirePositiveFinite(parameters.EmergencyBrakingMetersPerSecondSquared, "緊急煞車減速度", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.ControlReactionTimeSeconds, "控制反應時間", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.BrakeBuildUpTimeSeconds, "煞車建立時間", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.PositioningErrorMeters, "定位誤差", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.SafetyMarginMeters, "安全餘裕", errors);
        RouteValidator.RequireNonNegativeFinite(parameters.AbsoluteMinimumGapMeters, "絕對最小淨距", errors);

        if (RouteValidator.IsFinite(parameters.EmergencyBrakingMetersPerSecondSquared)
            && RouteValidator.IsFinite(parameters.ServiceBrakingMetersPerSecondSquared)
            && parameters.EmergencyBrakingMetersPerSecondSquared < parameters.ServiceBrakingMetersPerSecondSquared)
        {
            errors.Add("緊急煞車減速度不得小於營運煞車減速度。");
        }

        RouteValidator.ThrowIfAny(errors);
    }

    internal static void ValidateSpeedLimits(Route route, IEnumerable<SpeedLimitSegment> limits)
    {
        var errors = new List<string>();
        var index = 0;
        foreach (var limit in limits)
        {
            index++;
            var prefix = $"速限第 {index} 列";
            RouteValidator.RequireNonNegativeFinite(limit.StartPositionMeters, $"{prefix}起始里程", errors);
            RouteValidator.RequireNonNegativeFinite(limit.EndPositionMeters, $"{prefix}結束里程", errors);
            RouteValidator.RequirePositiveFinite(limit.LimitMetersPerSecond, $"{prefix}速限", errors);

            if (RouteValidator.IsFinite(limit.StartPositionMeters)
                && RouteValidator.IsFinite(limit.EndPositionMeters))
            {
                if (limit.StartPositionMeters >= limit.EndPositionMeters)
                {
                    errors.Add($"{prefix}起始里程必須小於結束里程。");
                }

                if (limit.EndPositionMeters > route.TotalLengthMeters + 1e-7)
                {
                    errors.Add($"{prefix}里程必須位於 0.00 km 至 {route.TotalLengthMeters / 1000:0.00} km 內。");
                }

                if (!IsTenMeterPrecision(limit.StartPositionMeters)
                    || !IsTenMeterPrecision(limit.EndPositionMeters))
                {
                    errors.Add($"{prefix}起訖里程必須使用 0.01 km（10 m）精度。");
                }
            }

            if (!Enum.IsDefined(limit.Direction))
            {
                errors.Add($"{prefix}方向無效。");
            }
        }

        RouteValidator.ThrowIfAny(errors);
    }

    private static void RequireRatio(double value, string name, ICollection<string> errors)
    {
        if (!RouteValidator.IsFinite(value) || value < 0 || value > 1)
        {
            errors.Add($"{name}必須是 0 至 1 之間的有限數值。");
        }
    }

    private static bool IsTenMeterPrecision(double value) => Math.Abs(value / 10 - Math.Round(value / 10)) <= 1e-7;
}
