namespace MrtRouteSimulator.App;

public sealed class StationInputRow
{
    public string StationId { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public double DistanceFromPreviousKm { get; set; }

    public double? DwellTimeSeconds { get; set; }
}

public sealed record TimetableRow(
    string TrainId,
    string Direction,
    string StationId,
    string StationName,
    string ArrivalTime,
    string DepartureTime,
    string DwellTime,
    string PositionKm);

public sealed record SegmentRow(
    string Segment,
    string DistanceKm,
    string Profile,
    string PeakSpeedKmh,
    string TravelTime,
    string AccelerationTime,
    string CruisingTime,
    string DecelerationTime);

public sealed record CurrentTrainRow(
    string TrainId,
    string Direction,
    string State,
    string PositionKm,
    string SpeedKmh,
    string CurrentStation,
    string NextStation);
