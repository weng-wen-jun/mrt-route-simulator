namespace MrtRouteSimulator.Engine;

public static class TripSimulator
{
    public static TripResult SimulateSingleTrip(
        Route route,
        TrainParameters parameters,
        double startTimeSeconds,
        TrainDirection direction = TrainDirection.Outbound)
    {
        ValidateStartTime(startTimeSeconds);
        var orderedStations = direction == TrainDirection.Outbound
            ? route.Stations.ToArray()
            : route.Stations.Reverse().ToArray();

        var events = new List<StationEvent>(orderedStations.Length);
        var segments = new List<SegmentTripResult>(orderedStations.Length - 1);
        var currentTime = startTimeSeconds;
        var totalTravelTime = 0d;
        var totalDwellTime = 0d;

        events.Add(new StationEvent(
            orderedStations[0].StationId,
            orderedStations[0].StationName,
            currentTime,
            currentTime,
            0,
            orderedStations[0].PositionMeters,
            direction));

        for (var index = 0; index < orderedStations.Length - 1; index++)
        {
            var from = orderedStations[index];
            var to = orderedStations[index + 1];
            var distance = Math.Abs(to.PositionMeters - from.PositionMeters);
            var motion = AnalyticalModel.CalculateSegmentTravelTime(
                distance,
                parameters.MaxSpeedMetersPerSecond,
                parameters.AccelerationMetersPerSecondSquared,
                parameters.DecelerationMetersPerSecondSquared);

            var departureTime = currentTime;
            var arrivalTime = departureTime + motion.TravelTimeSeconds;
            var isTerminal = index == orderedStations.Length - 2;
            var dwellTime = isTerminal ? 0 : to.DwellTimeSeconds;
            var nextDepartureTime = arrivalTime + dwellTime;

            segments.Add(new SegmentTripResult(
                from,
                to,
                departureTime,
                arrivalTime,
                motion,
                direction));
            events.Add(new StationEvent(
                to.StationId,
                to.StationName,
                arrivalTime,
                nextDepartureTime,
                dwellTime,
                to.PositionMeters,
                direction));

            totalTravelTime += motion.TravelTimeSeconds;
            totalDwellTime += dwellTime;
            currentTime = nextDepartureTime;
        }

        var terminalArrival = events[^1].ArrivalTimeSeconds;
        return new TripResult(
            direction,
            startTimeSeconds,
            events,
            segments,
            terminalArrival,
            terminalArrival - startTimeSeconds,
            totalDwellTime,
            totalTravelTime);
    }

    public static CycleTimeResult CalculateCycleTime(
        Route route,
        TrainParameters parameters,
        double startTimeSeconds = 0)
    {
        var outbound = SimulateSingleTrip(route, parameters, startTimeSeconds, TrainDirection.Outbound);
        var inboundStart = outbound.TerminalArrivalTimeSeconds + parameters.TerminalTurnaroundTimeSeconds;
        var inbound = SimulateSingleTrip(route, parameters, inboundStart, TrainDirection.Inbound);
        var cycleTime = outbound.TotalRunTimeSeconds
            + parameters.TerminalTurnaroundTimeSeconds
            + inbound.TotalRunTimeSeconds
            + parameters.OriginTurnaroundTimeSeconds;

        return new CycleTimeResult(
            outbound,
            inbound,
            parameters.TerminalTurnaroundTimeSeconds,
            parameters.OriginTurnaroundTimeSeconds,
            cycleTime);
    }

    public static MultipleTrainResult SimulateMultipleTrains(
        Route route,
        TrainParameters parameters,
        int trainCount,
        double? initialDepartureIntervalSeconds = null,
        double startTimeSeconds = 0)
    {
        var errors = new List<string>();
        if (trainCount <= 0)
        {
            errors.Add("列車數量必須大於 0。");
        }

        if (initialDepartureIntervalSeconds is not null
            && (!RouteValidator.IsFinite(initialDepartureIntervalSeconds.Value)
                || initialDepartureIntervalSeconds.Value <= 0))
        {
            errors.Add("指定班距必須是有限且大於 0 的秒數。");
        }

        if (!RouteValidator.IsFinite(startTimeSeconds) || startTimeSeconds < 0)
        {
            errors.Add("模擬開始時間必須是有限的非負秒數。");
        }

        RouteValidator.ThrowIfAny(errors);

        var cycle = CalculateCycleTime(route, parameters, startTimeSeconds);
        var headway = initialDepartureIntervalSeconds ?? cycle.CycleTimeSeconds / trainCount;
        var trains = new List<TrainScheduleResult>(trainCount);

        for (var index = 0; index < trainCount; index++)
        {
            var departureTime = startTimeSeconds + index * headway;
            var trip = SimulateSingleTrip(route, parameters, departureTime, TrainDirection.Outbound);
            trains.Add(new TrainScheduleResult(
                $"Train {index + 1:00}",
                departureTime,
                trip,
                cycle.CycleTimeSeconds));
        }

        return new MultipleTrainResult(headway, cycle.CycleTimeSeconds, trains);
    }

    private static void ValidateStartTime(double startTimeSeconds)
    {
        if (!RouteValidator.IsFinite(startTimeSeconds) || startTimeSeconds < 0)
        {
            throw new SimulationValidationException(["模擬開始時間必須是有限的非負秒數。"]);
        }
    }
}
