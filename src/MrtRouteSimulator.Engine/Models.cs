namespace MrtRouteSimulator.Engine;

public enum SpeedProfileType
{
    Instantaneous,
    Triangular,
    Trapezoidal
}

public enum TrainMotionState
{
    Dwelling,
    Accelerating,
    Cruising,
    Decelerating,
    Arriving,
    Turning
}

public enum TrainDirection
{
    Outbound = 1,
    Inbound = -1
}

public sealed record Station(
    string StationId,
    string StationName,
    double PositionMeters,
    double DwellTimeSeconds);

public sealed class Route
{
    public Route(string routeId, string routeName, IEnumerable<Station> stations)
    {
        RouteId = routeId?.Trim() ?? string.Empty;
        RouteName = routeName?.Trim() ?? string.Empty;
        Stations = stations?.ToArray() ?? [];
        RouteValidator.Validate(this);
    }

    public string RouteId { get; }

    public string RouteName { get; }

    public IReadOnlyList<Station> Stations { get; }

    public double TotalLengthMeters => Stations.Count == 0 ? 0 : Stations[^1].PositionMeters;
}

public sealed class TrainParameters
{
    public TrainParameters(
        double maxSpeedMetersPerSecond,
        double accelerationMetersPerSecondSquared,
        double decelerationMetersPerSecondSquared,
        double defaultDwellTimeSeconds,
        double originTurnaroundTimeSeconds,
        double terminalTurnaroundTimeSeconds)
    {
        MaxSpeedMetersPerSecond = maxSpeedMetersPerSecond;
        AccelerationMetersPerSecondSquared = accelerationMetersPerSecondSquared;
        DecelerationMetersPerSecondSquared = decelerationMetersPerSecondSquared;
        DefaultDwellTimeSeconds = defaultDwellTimeSeconds;
        OriginTurnaroundTimeSeconds = originTurnaroundTimeSeconds;
        TerminalTurnaroundTimeSeconds = terminalTurnaroundTimeSeconds;
        RouteValidator.Validate(this);
    }

    public double MaxSpeedMetersPerSecond { get; }

    public double AccelerationMetersPerSecondSquared { get; }

    public double DecelerationMetersPerSecondSquared { get; }

    public double DefaultDwellTimeSeconds { get; }

    public double OriginTurnaroundTimeSeconds { get; }

    public double TerminalTurnaroundTimeSeconds { get; }
}

public sealed record SegmentTravelResult(
    double DistanceMeters,
    double TravelTimeSeconds,
    double PeakSpeedMetersPerSecond,
    SpeedProfileType ProfileType,
    double AccelerationTimeSeconds,
    double CruisingTimeSeconds,
    double DecelerationTimeSeconds,
    double AccelerationDistanceMeters,
    double CruisingDistanceMeters,
    double DecelerationDistanceMeters);

public sealed record StationEvent(
    string StationId,
    string StationName,
    double ArrivalTimeSeconds,
    double DepartureTimeSeconds,
    double DwellTimeSeconds,
    double CumulativePositionMeters,
    TrainDirection Direction);

public sealed record SegmentTripResult(
    Station FromStation,
    Station ToStation,
    double DepartureTimeSeconds,
    double ArrivalTimeSeconds,
    SegmentTravelResult Motion,
    TrainDirection Direction);

public sealed record TripResult(
    TrainDirection Direction,
    double DepartureFromOriginSeconds,
    IReadOnlyList<StationEvent> StationEvents,
    IReadOnlyList<SegmentTripResult> Segments,
    double TerminalArrivalTimeSeconds,
    double TotalRunTimeSeconds,
    double TotalDwellTimeSeconds,
    double TotalTravelTimeSeconds);

public sealed record CycleTimeResult(
    TripResult OutboundTrip,
    TripResult InboundTrip,
    double TerminalTurnaroundTimeSeconds,
    double OriginTurnaroundTimeSeconds,
    double CycleTimeSeconds);

public sealed record TrainScheduleResult(
    string TrainId,
    double InitialDepartureTimeSeconds,
    TripResult OutboundTrip,
    double CycleTimeSeconds);

public sealed record MultipleTrainResult(
    double HeadwaySeconds,
    double CycleTimeSeconds,
    IReadOnlyList<TrainScheduleResult> Trains);

public sealed record TrainState(
    string TrainId,
    double PositionMeters,
    double SpeedMetersPerSecond,
    TrainMotionState State,
    string CurrentStationId,
    string? NextStationId,
    TrainDirection Direction,
    double SimulationTimeSeconds);
