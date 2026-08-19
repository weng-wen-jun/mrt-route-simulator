namespace MrtRouteSimulator.Engine;

public sealed class SimulationEngine
{
    private const double TimeEpsilon = 1e-8;
    private readonly CycleTimeResult _cycle;
    private readonly IReadOnlyList<double> _departureOffsets;

    public SimulationEngine(
        Route route,
        TrainParameters parameters,
        int trainCount,
        double? initialDepartureIntervalSeconds = null,
        double timeStepSeconds = 0.1)
    {
        if (!RouteValidator.IsFinite(timeStepSeconds) || timeStepSeconds <= 0)
        {
            throw new SimulationValidationException(["Simulation Tick 時間步長必須是有限且大於 0 的秒數。"]);
        }

        Route = route;
        Parameters = parameters;
        TimeStepSeconds = timeStepSeconds;
        _cycle = TripSimulator.CalculateCycleTime(route, parameters);
        var schedules = TripSimulator.SimulateMultipleTrains(
            route,
            parameters,
            trainCount,
            initialDepartureIntervalSeconds);
        HeadwaySeconds = schedules.HeadwaySeconds;
        _departureOffsets = schedules.Trains.Select(train => train.InitialDepartureTimeSeconds).ToArray();
    }

    public Route Route { get; }

    public TrainParameters Parameters { get; }

    public double CurrentTimeSeconds { get; private set; }

    public double TimeStepSeconds { get; }

    public double HeadwaySeconds { get; }

    public double CycleTimeSeconds => _cycle.CycleTimeSeconds;

    public int TrainCount => _departureOffsets.Count;

    public IReadOnlyList<double> InitialDepartureOffsetsSeconds => _departureOffsets;

    public void Tick() => CurrentTimeSeconds += TimeStepSeconds;

    public void Reset() => CurrentTimeSeconds = 0;

    public void SetCurrentTime(double simulationTimeSeconds)
    {
        if (!RouteValidator.IsFinite(simulationTimeSeconds) || simulationTimeSeconds < 0)
        {
            throw new SimulationValidationException(["模擬時間必須是有限的非負秒數。"]);
        }

        CurrentTimeSeconds = simulationTimeSeconds;
    }

    public IReadOnlyList<TrainState> GetTrainStates() => GetTrainStates(CurrentTimeSeconds);

    public IReadOnlyList<TrainState> GetTrainStates(double simulationTimeSeconds)
    {
        if (!RouteValidator.IsFinite(simulationTimeSeconds) || simulationTimeSeconds < 0)
        {
            throw new SimulationValidationException(["模擬時間必須是有限的非負秒數。"]);
        }

        var states = new List<TrainState>(TrainCount);
        for (var index = 0; index < TrainCount; index++)
        {
            states.Add(EvaluateTrain(index, simulationTimeSeconds));
        }

        return states;
    }

    private TrainState EvaluateTrain(int trainIndex, double globalTime)
    {
        var trainId = $"Train {trainIndex + 1:00}";
        var localTime = globalTime - _departureOffsets[trainIndex];
        if (localTime < 0)
        {
            return new TrainState(
                trainId,
                0,
                0,
                TrainMotionState.Dwelling,
                Route.Stations[0].StationId,
                Route.Stations[1].StationId,
                TrainDirection.Outbound,
                globalTime);
        }

        localTime %= _cycle.CycleTimeSeconds;
        var outboundEnd = _cycle.OutboundTrip.TerminalArrivalTimeSeconds;
        var terminalTurnaroundEnd = outboundEnd + Parameters.TerminalTurnaroundTimeSeconds;
        var inboundEnd = _cycle.InboundTrip.TerminalArrivalTimeSeconds;

        if (localTime <= outboundEnd + TimeEpsilon)
        {
            return EvaluateTrip(trainId, _cycle.OutboundTrip, localTime, globalTime);
        }

        if (localTime < terminalTurnaroundEnd - TimeEpsilon)
        {
            var terminal = Route.Stations[^1];
            return new TrainState(
                trainId,
                terminal.PositionMeters,
                0,
                TrainMotionState.Turning,
                terminal.StationId,
                Route.Stations[^2].StationId,
                TrainDirection.Inbound,
                globalTime);
        }

        if (localTime <= inboundEnd + TimeEpsilon)
        {
            return EvaluateTrip(trainId, _cycle.InboundTrip, localTime, globalTime);
        }

        var origin = Route.Stations[0];
        return new TrainState(
            trainId,
            origin.PositionMeters,
            0,
            TrainMotionState.Turning,
            origin.StationId,
            Route.Stations[1].StationId,
            TrainDirection.Outbound,
            globalTime);
    }

    private TrainState EvaluateTrip(string trainId, TripResult trip, double localTime, double globalTime)
    {
        var first = trip.StationEvents[0];
        if (localTime <= first.DepartureTimeSeconds + TimeEpsilon)
        {
            var next = trip.Segments[0].ToStation;
            return new TrainState(
                trainId,
                first.CumulativePositionMeters,
                0,
                TrainMotionState.Arriving,
                first.StationId,
                next.StationId,
                trip.Direction,
                globalTime);
        }

        for (var index = 0; index < trip.Segments.Count; index++)
        {
            var segment = trip.Segments[index];
            var destinationEvent = trip.StationEvents[index + 1];

            if (localTime < segment.ArrivalTimeSeconds - TimeEpsilon)
            {
                return EvaluateMotion(trainId, segment, localTime, globalTime);
            }

            if (Math.Abs(localTime - segment.ArrivalTimeSeconds) <= TimeEpsilon)
            {
                return new TrainState(
                    trainId,
                    segment.ToStation.PositionMeters,
                    0,
                    TrainMotionState.Arriving,
                    segment.ToStation.StationId,
                    index + 1 < trip.Segments.Count ? trip.Segments[index + 1].ToStation.StationId : null,
                    trip.Direction,
                    globalTime);
            }

            if (localTime < destinationEvent.DepartureTimeSeconds - TimeEpsilon)
            {
                return new TrainState(
                    trainId,
                    destinationEvent.CumulativePositionMeters,
                    0,
                    TrainMotionState.Dwelling,
                    destinationEvent.StationId,
                    index + 1 < trip.Segments.Count ? trip.Segments[index + 1].ToStation.StationId : null,
                    trip.Direction,
                    globalTime);
            }
        }

        var terminal = trip.StationEvents[^1];
        return new TrainState(
            trainId,
            terminal.CumulativePositionMeters,
            0,
            TrainMotionState.Arriving,
            terminal.StationId,
            null,
            trip.Direction,
            globalTime);
    }

    private TrainState EvaluateMotion(
        string trainId,
        SegmentTripResult segment,
        double localTime,
        double globalTime)
    {
        var motionTime = Math.Max(0, localTime - segment.DepartureTimeSeconds);
        var motion = segment.Motion;
        double distanceFromStart;
        double speed;
        TrainMotionState state;

        if (motionTime < motion.AccelerationTimeSeconds - TimeEpsilon)
        {
            speed = Parameters.AccelerationMetersPerSecondSquared * motionTime;
            distanceFromStart = 0.5 * Parameters.AccelerationMetersPerSecondSquared * motionTime * motionTime;
            state = TrainMotionState.Accelerating;
        }
        else if (motionTime < motion.AccelerationTimeSeconds + motion.CruisingTimeSeconds - TimeEpsilon)
        {
            var cruisingElapsed = motionTime - motion.AccelerationTimeSeconds;
            speed = motion.PeakSpeedMetersPerSecond;
            distanceFromStart = motion.AccelerationDistanceMeters + speed * cruisingElapsed;
            state = TrainMotionState.Cruising;
        }
        else
        {
            var decelerationElapsed = Math.Min(
                motion.DecelerationTimeSeconds,
                Math.Max(0, motionTime - motion.AccelerationTimeSeconds - motion.CruisingTimeSeconds));
            speed = Math.Max(
                0,
                motion.PeakSpeedMetersPerSecond
                    - Parameters.DecelerationMetersPerSecondSquared * decelerationElapsed);
            distanceFromStart = motion.AccelerationDistanceMeters
                + motion.CruisingDistanceMeters
                + motion.PeakSpeedMetersPerSecond * decelerationElapsed
                - 0.5 * Parameters.DecelerationMetersPerSecondSquared * decelerationElapsed * decelerationElapsed;
            state = TrainMotionState.Decelerating;
        }

        distanceFromStart = Math.Clamp(distanceFromStart, 0, motion.DistanceMeters);
        var position = segment.Direction == TrainDirection.Outbound
            ? segment.FromStation.PositionMeters + distanceFromStart
            : segment.FromStation.PositionMeters - distanceFromStart;

        return new TrainState(
            trainId,
            position,
            speed,
            state,
            segment.FromStation.StationId,
            segment.ToStation.StationId,
            segment.Direction,
            globalTime);
    }
}
