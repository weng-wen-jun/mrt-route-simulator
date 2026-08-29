namespace MrtRouteSimulator.Engine;

/// <summary>
/// Defines how much trajectory detail a simulation keeps in memory. Events and safety observations
/// are always retained; this policy only controls the high-volume time-position samples.
/// </summary>
public enum SimulationTraceRetentionMode
{
    Full,
    Decimated,
    EventsOnly
}

/// <summary>
/// An immutable retention policy for <see cref="SimulationWorld.Trajectory"/>.
/// Full is the compatibility default. Decimated mode retains regular samples and state changes;
/// events remain available at their original simulation time.
/// </summary>
public sealed record SimulationTraceRetentionPolicy
{
    public SimulationTraceRetentionPolicy(
        SimulationTraceRetentionMode mode = SimulationTraceRetentionMode.Full,
        double minimumSampleIntervalSeconds = SimulationWorld.FixedTimeStepSeconds)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "軌跡留存模式無效。");
        }

        if (mode == SimulationTraceRetentionMode.Decimated
            && (!double.IsFinite(minimumSampleIntervalSeconds)
                || minimumSampleIntervalSeconds < SimulationWorld.FixedTimeStepSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSampleIntervalSeconds),
                $"降採樣間隔必須是至少 {SimulationWorld.FixedTimeStepSeconds:0.0} 秒的有限數值。");
        }

        Mode = mode;
        MinimumSampleIntervalSeconds = minimumSampleIntervalSeconds;
    }

    public SimulationTraceRetentionMode Mode { get; }

    public double MinimumSampleIntervalSeconds { get; }

    public static SimulationTraceRetentionPolicy Full { get; } = new();

    public static SimulationTraceRetentionPolicy EventsOnly { get; } =
        new(SimulationTraceRetentionMode.EventsOnly, 0);

    public static SimulationTraceRetentionPolicy Decimated(double minimumSampleIntervalSeconds) =>
        new(SimulationTraceRetentionMode.Decimated, minimumSampleIntervalSeconds);
}
