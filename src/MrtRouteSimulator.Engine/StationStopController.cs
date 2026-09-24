namespace MrtRouteSimulator.Engine;

/// <summary>
/// 依停車點剩餘距離產生進站速度目標，並以目前動態煞車包絡線預測是否必須立即施加營運煞車。
/// 這是概念模擬用的停車控制器，不代表 ATP／ATO 安全認證功能。
/// </summary>
public static class StationStopController
{
    // 曲線追蹤負責中高速段；最後 12 m 才進入低速精停，避免為精停而顯著拉長正常區間運轉時間。
    public const double PrecisionApproachDistanceMeters = 12;
    public const double PrecisionMaximumSpeedMetersPerSecond = 3;
    public const double PrecisionBrakingMetersPerSecondSquared = 0.5;

    private const double BrakeRequestToleranceMeters = 0.05;
    private const double StopViolationSpeedThresholdMetersPerSecond = 3.0 / 3.6;

    public static bool ShouldRecordStopViolation(double speedMetersPerSecond) =>
        speedMetersPerSecond >= StopViolationSpeedThresholdMetersPerSecond;

    public static StationStopControlOutput Calculate(StationStopControlInput input)
    {
        ValidateFiniteNonNegative(input.RemainingDistanceMeters, nameof(input.RemainingDistanceMeters));
        ValidateFiniteNonNegative(input.CurrentSpeedMetersPerSecond, nameof(input.CurrentSpeedMetersPerSecond));
        ValidateFinite(input.CurrentAccelerationMetersPerSecondSquared, nameof(input.CurrentAccelerationMetersPerSecondSquared));
        ValidatePositive(input.LinePermittedSpeedMetersPerSecond, nameof(input.LinePermittedSpeedMetersPerSecond));
        ValidatePositive(input.ServiceBrakingMetersPerSecondSquared, nameof(input.ServiceBrakingMetersPerSecondSquared));
        ValidatePositive(input.TimeStepSeconds, nameof(input.TimeStepSeconds));
        ValidateFiniteNonNegative(input.SnapDistanceMeters, nameof(input.SnapDistanceMeters));
        ValidateFiniteNonNegative(input.SnapSpeedMetersPerSecond, nameof(input.SnapSpeedMetersPerSecond));
        if (input.JerkLimited)
        {
            ValidatePositive(input.JerkMetersPerSecondCubed, nameof(input.JerkMetersPerSecondCubed));
        }

        var stoppingEnvelope = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
            input.CurrentSpeedMetersPerSecond,
            input.CurrentAccelerationMetersPerSecondSquared,
            input.ServiceBrakingMetersPerSecondSquared,
            input.JerkMetersPerSecondCubed,
            input.TimeStepSeconds,
            input.JerkLimited);
        var stopPositionError = stoppingEnvelope.DistanceMeters - input.RemainingDistanceMeters;
        var brakingCurveSpeed = CalculateBrakingCurveSpeed(input);
        var targetSpeed = Math.Min(input.LinePermittedSpeedMetersPerSecond, brakingCurveSpeed);
        var mode = StationStopControlMode.TrackingBrakingCurve;

        if (input.RemainingDistanceMeters <= PrecisionApproachDistanceMeters
            || targetSpeed <= PrecisionMaximumSpeedMetersPerSecond)
        {
            var terminalDistance = Math.Max(0, input.RemainingDistanceMeters - input.SnapDistanceMeters);
            var precisionSpeed = Math.Min(
                PrecisionMaximumSpeedMetersPerSecond,
                Math.Sqrt(2 * PrecisionBrakingMetersPerSecondSquared * terminalDistance));
            targetSpeed = Math.Min(targetSpeed, precisionSpeed);
            mode = StationStopControlMode.PrecisionApproach;
        }

        if (input.RemainingDistanceMeters <= input.SnapDistanceMeters
            && input.CurrentSpeedMetersPerSecond <= input.SnapSpeedMetersPerSecond)
        {
            targetSpeed = 0;
            mode = StationStopControlMode.StopSnap;
        }

        return new StationStopControlOutput(
            targetSpeed,
            stoppingEnvelope.DistanceMeters,
            stopPositionError,
            stopPositionError > BrakeRequestToleranceMeters,
            mode);
    }

    private static double CalculateBrakingCurveSpeed(StationStopControlInput input)
    {
        var jerkAllowance = input.JerkLimited
            ? input.CurrentSpeedMetersPerSecond * input.ServiceBrakingMetersPerSecondSquared
                / input.JerkMetersPerSecondCubed
            : 0;
        var usableDistance = Math.Max(0, input.RemainingDistanceMeters - jerkAllowance);
        return Math.Sqrt(2 * input.ServiceBrakingMetersPerSecondSquared * usableDistance);
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, $"{name}必須是有限數值。");
        }
    }

    private static void ValidateFiniteNonNegative(double value, string name)
    {
        ValidateFinite(value, name);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, $"{name}不得小於 0。");
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        ValidateFinite(value, name);
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, $"{name}必須大於 0。");
        }
    }
}

public enum StationStopControlMode
{
    TrackingBrakingCurve,
    PrecisionApproach,
    StopSnap
}

public sealed record StationStopControlInput(
    double RemainingDistanceMeters,
    double CurrentSpeedMetersPerSecond,
    double CurrentAccelerationMetersPerSecondSquared,
    double LinePermittedSpeedMetersPerSecond,
    double ServiceBrakingMetersPerSecondSquared,
    double JerkMetersPerSecondCubed,
    double TimeStepSeconds,
    bool JerkLimited,
    double SnapDistanceMeters,
    double SnapSpeedMetersPerSecond);

public sealed record StationStopControlOutput(
    double TargetSpeedMetersPerSecond,
    double PredictedStoppingDistanceMeters,
    double StopPositionErrorMeters,
    bool RequiresServiceBraking,
    StationStopControlMode Mode);
