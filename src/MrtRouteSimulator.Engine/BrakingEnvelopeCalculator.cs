namespace MrtRouteSimulator.Engine;

public sealed record BrakingEnvelopeResult(
    double DistanceMeters,
    double DurationSeconds,
    double FinalAccelerationMetersPerSecondSquared);

public static class BrakingEnvelopeCalculator
{
    private const int MaximumSteps = 100_000;
    private const double SpeedTolerance = 1e-9;

    public static BrakingEnvelopeResult CalculateStoppingEnvelope(
        double speedMetersPerSecond,
        double accelerationMetersPerSecondSquared,
        double brakingMetersPerSecondSquared,
        double jerkMetersPerSecondCubed,
        double timeStepSeconds,
        bool jerkLimited)
    {
        ValidateFiniteNonNegative(speedMetersPerSecond, "目前速度");
        ValidateFinite(accelerationMetersPerSecondSquared, "目前加速度");
        ValidatePositive(brakingMetersPerSecondSquared, "煞車減速度");
        ValidatePositive(timeStepSeconds, "時間步長");
        if (jerkLimited)
        {
            ValidatePositive(jerkMetersPerSecondCubed, "Jerk");
        }

        var speed = speedMetersPerSecond;
        var acceleration = accelerationMetersPerSecondSquared;
        var distance = 0d;
        var duration = 0d;
        var targetAcceleration = -brakingMetersPerSecondSquared;

        for (var step = 0; speed > SpeedTolerance && step < MaximumSteps; step++)
        {
            acceleration = jerkLimited
                ? MoveToward(
                    acceleration,
                    targetAcceleration,
                    jerkMetersPerSecondCubed * timeStepSeconds)
                : targetAcceleration;
            var nextSpeed = Math.Max(0, speed + acceleration * timeStepSeconds);
            distance += Math.Max(0, (speed + nextSpeed) * 0.5 * timeStepSeconds);
            duration += timeStepSeconds;
            speed = nextSpeed;
        }

        if (speed > SpeedTolerance)
        {
            throw new InvalidOperationException("動態煞車包絡線未能在合理步數內收斂至停止。");
        }

        return new BrakingEnvelopeResult(distance, duration, acceleration);
    }

    internal static double MoveToward(double current, double target, double maximumChange)
    {
        if (current < target)
        {
            return Math.Min(target, current + maximumChange);
        }

        return Math.Max(target, current - maximumChange);
    }

    private static void ValidateFinite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{name}必須是有限數值。");
        }
    }

    private static void ValidateFiniteNonNegative(double value, string name)
    {
        ValidateFinite(value, name);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{name}不得小於 0。");
        }
    }

    private static void ValidatePositive(double value, string name)
    {
        ValidateFinite(value, name);
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"{name}必須大於 0。");
        }
    }
}
