namespace MrtRouteSimulator.Engine;

/// <summary>Controls only historical safety-observation storage, never safety calculation.</summary>
public enum SafetyObservationRetentionMode
{
    Full,
    Decimated
}

/// <summary>
/// Immutable retention policy for <see cref="SimulationWorld.SafetyHistory"/>. Current safety
/// observations and safety events are always calculated at every fixed tick regardless of policy.
/// </summary>
public sealed record SafetyObservationRetentionPolicy
{
    public SafetyObservationRetentionPolicy(
        SafetyObservationRetentionMode mode = SafetyObservationRetentionMode.Full,
        double minimumSampleIntervalSeconds = SimulationWorld.FixedTimeStepSeconds)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "安全觀測歷程留存模式無效。");
        }

        if (mode == SafetyObservationRetentionMode.Decimated
            && (!double.IsFinite(minimumSampleIntervalSeconds)
                || minimumSampleIntervalSeconds < SimulationWorld.FixedTimeStepSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSampleIntervalSeconds),
                $"安全觀測降採樣間隔必須是至少 {SimulationWorld.FixedTimeStepSeconds:0.0} 秒的有限數值。");
        }

        Mode = mode;
        MinimumSampleIntervalSeconds = minimumSampleIntervalSeconds;
    }

    public SafetyObservationRetentionMode Mode { get; }

    public double MinimumSampleIntervalSeconds { get; }

    public static SafetyObservationRetentionPolicy Full { get; } = new();

    public static SafetyObservationRetentionPolicy Decimated(double minimumSampleIntervalSeconds) =>
        new(SafetyObservationRetentionMode.Decimated, minimumSampleIntervalSeconds);
}
