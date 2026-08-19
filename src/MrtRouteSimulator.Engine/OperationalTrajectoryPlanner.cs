namespace MrtRouteSimulator.Engine;

public sealed record OperationalTripResult(
    IReadOnlyList<TrajectorySample> Samples,
    IReadOnlyList<SimulationEvent> Events,
    double ArrivalTimeSeconds,
    double MaximumSpeedMetersPerSecond,
    double MaximumAbsoluteAccelerationMetersPerSecondSquared,
    double MaximumObservedJerkMetersPerSecondCubed);

public static class OperationalTrajectoryPlanner
{
    public static OperationalTripResult GenerateOutboundTrip(
        Route route,
        TrainParameters trainParameters,
        OperationalParameters operationalParameters,
        IEnumerable<SpeedLimitSegment>? speedLimits = null,
        OperationProfileMode profileMode = OperationProfileMode.RealisticOperations)
    {
        var world = new SimulationWorld(
            route,
            trainParameters,
            operationalParameters,
            trainCount: 1,
            speedLimits: speedLimits,
            profileMode: profileMode,
            movingBlockMode: MovingBlockMode.Independent);
        var baseline = TripSimulator.SimulateSingleTrip(route, trainParameters, 0, TrainDirection.Outbound);
        var maximumDuration = baseline.TotalRunTimeSeconds * 4 + 600;
        SimulationEvent? terminalArrival = null;

        while (world.CurrentTimeSeconds < maximumDuration)
        {
            world.Tick();
            terminalArrival = world.Events.LastOrDefault(item =>
                item.EventType == SimulationEventType.Arrival
                && item.PositionMeters >= route.TotalLengthMeters - 1e-5);
            if (terminalArrival is not null)
            {
                break;
            }
        }

        if (terminalArrival is null)
        {
            throw new InvalidOperationException("實際營運軌跡未能在合理時間內抵達終點。");
        }

        var samples = world.Trajectory
            .Where(sample => sample.SimulationTimeSeconds <= terminalArrival.SimulationTimeSeconds + 1e-7)
            .ToArray();
        var maximumJerk = 0d;
        for (var index = 1; index < samples.Length; index++)
        {
            var deltaTime = samples[index].SimulationTimeSeconds - samples[index - 1].SimulationTimeSeconds;
            if (deltaTime > 0)
            {
                maximumJerk = Math.Max(
                    maximumJerk,
                    Math.Abs(samples[index].AccelerationMetersPerSecondSquared
                        - samples[index - 1].AccelerationMetersPerSecondSquared) / deltaTime);
            }
        }

        return new OperationalTripResult(
            samples,
            world.Events.Where(item => item.SimulationTimeSeconds <= terminalArrival.SimulationTimeSeconds + 1e-7).ToArray(),
            terminalArrival.SimulationTimeSeconds,
            samples.Max(sample => sample.SpeedMetersPerSecond),
            samples.Max(sample => Math.Abs(sample.AccelerationMetersPerSecondSquared)),
            maximumJerk);
    }
}
