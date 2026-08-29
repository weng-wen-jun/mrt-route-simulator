using System.ComponentModel;

namespace MrtRouteSimulator.App;

public sealed record CatalogOption(string Id, string DisplayName);

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
    string PositionKm,
    string ServiceRunId = "—",
    string ServiceType = "—",
    string StopPattern = "—",
    string PlannedArrivalTime = "—",
    string PlannedDepartureTime = "—",
    string Delay = "—",
    string Status = "—");

public sealed record SegmentRow(
    string Segment,
    string DistanceKm,
    string Profile,
    string PeakSpeedKmh,
    string TravelTime,
    string AccelerationTime,
    string CruisingTime,
    string DecelerationTime,
    string VehicleId = "—",
    string ServiceRunId = "—",
    string Direction = "—",
    string Status = "V1 理論基準",
    string CoastingTime = "—",
    string ControlEvents = "—");

public sealed record V1V2ComparisonRow(
    string VehicleId,
    string ServiceRunId,
    string Direction,
    string VehicleType,
    string StopPattern,
    string Station,
    string TheoreticalArrival,
    string TheoreticalDeparture,
    string TheoreticalDwell,
    string ActualArrival,
    string ActualDeparture,
    string ActualDwell,
    string ArrivalDifference,
    string DepartureDifference,
    string DepartureDifferencePercent,
    string Status);

public sealed record ResourceOccupancyRow(
    string ResourceId,
    string OccupiedTime,
    string Utilization,
    string ReservationCount,
    string ObservedReservationsPerHour,
    string MinimumReleaseHeadway);

public sealed record CurrentTrainRow(
    string TrainId,
    string Direction,
    string State,
    string PositionKm,
    string SpeedKmh,
    string CurrentStation,
    string NextStation);

public sealed class SpeedLimitInputRow
{
    public double StartKm { get; set; }

    public double EndKm { get; set; }

    public double LimitKmh { get; set; }

    public string Direction { get; set; } = "雙向";

    public string Note { get; set; } = string.Empty;
}

public sealed class ServicePatternInputRow : INotifyPropertyChanged
{
    private string _patternName = "快速車";

    public string PatternId { get; set; } = "EXPRESS";

    public string PatternName
    {
        get => _patternName;
        set
        {
            if (_patternName == value) return;
            _patternName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternName)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StationId { get; set; } = string.Empty;

    public string Mode { get; set; } = "跨站";

    public double? DwellTimeSeconds { get; set; }

    public double? SpeedLimitKmh { get; set; }
}

public sealed class ServiceRunInputRow
{
    public string VehicleId { get; set; } = "Vehicle 01";

    public int ServiceNumber { get; set; } = 1;

    public string Direction { get; set; } = "下行";

    public string ServiceClassId { get; set; } = "普通車";

    public string PatternId { get; set; } = "ALL_STOP";
}

public sealed record SafetyRow(
    string Pair,
    string Track,
    string FollowerKm,
    string LeaderRearKm,
    string GapMeters,
    string SafetyMeters,
    string BrakeDemandMeters,
    string MarginMeters,
    string Status);

public sealed record EventRow(
    string Time,
    string Type,
    string Vehicle,
    string PositionKm,
    string Message);

public sealed class VehicleTypeInputRow
{
    public string Id { get; set; } = "DEFAULT_VEHICLE";
    public string Name { get; set; } = "標準列車";
    public double LengthMeters { get; set; } = 92;
    public double MaxSpeedKmh { get; set; } = 80;
    public double Acceleration { get; set; } = 1;
    public double ServiceBrake { get; set; } = 0.9;
    public double EmergencyBrake { get; set; } = 1.3;
    public double Jerk { get; set; } = 0.65;
    public double TractionDecay { get; set; } = 0.45;
    public double CoastingDeceleration { get; set; } = 0.05;
    public string DefaultStopPatternId { get; set; } = string.Empty;
}

public sealed class ServiceTypeInputRow
{
    public string Id { get; set; } = "普通車";
    public string Name { get; set; } = "普通車";
    public string ColorHex { get; set; } = "#227EAD";
    public string RunPrefix { get; set; } = "L";
    public string DefaultStopPatternId { get; set; } = "ALL_STOP";
    public string DefaultVehicleTypeId { get; set; } = "DEFAULT_VEHICLE";
    public int Priority { get; set; }
    public bool CanRequestOvertake { get; set; }
    public string PreferredPlatformIds { get; set; } = string.Empty;
}

public sealed class HeadwayPlanInputRow
{
    public string Direction { get; set; } = "下行";
    public string FirstDeparture { get; set; } = "06:00:00";
    public double HeadwayMinutes { get; set; } = 6;
    public int RunCount { get; set; } = 3;
    public string ServiceTypeId { get; set; } = "普通車";
    public string VehicleTypeId { get; set; } = "DEFAULT_VEHICLE";
    public string StopPatternId { get; set; } = "ALL_STOP";
    public string OriginPlatformId { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public bool ContinueAfterTerminal { get; set; }
}

public sealed class ManualTimetableInputRow
{
    public string PlannedDeparture { get; set; } = "06:00:00";
    public string Direction { get; set; } = "下行";
    public string ServiceTypeId { get; set; } = "普通車";
    public string VehicleTypeId { get; set; } = "DEFAULT_VEHICLE";
    public string StopPatternId { get; set; } = "ALL_STOP";
    public string OriginPlatformId { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string ServiceRunId { get; set; } = string.Empty;
    public bool ContinueAfterTerminal { get; set; }
    public string ContinuationServiceRunId { get; set; } = string.Empty;
}

public sealed class PlatformInputRow
{
    public string PlatformId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Direction { get; set; } = "雙向";
    public double EffectiveLengthMeters { get; set; } = 220;
    public double StoppingPositionMeters { get; set; }
    public bool PassengerService { get; set; } = true;
    public string AllowedVehicleTypeIds { get; set; } = string.Empty;
    public string AllowedServiceTypeIds { get; set; } = string.Empty;
    public string TrackSegmentIds { get; set; } = string.Empty;
}

public sealed class TrackInputRow
{
    public string TrackId { get; set; } = string.Empty;
    public string FromStationId { get; set; } = string.Empty;
    public string ToStationId { get; set; } = string.Empty;
    public double StartKm { get; set; }
    public double EndKm { get; set; }
    public string Direction { get; set; } = "雙向";
    public string Kind { get; set; } = "正線";
    public double EffectiveLengthMeters { get; set; }
    public double SpeedLimitKmh { get; set; } = 80;
    public string ConflictResourceIds { get; set; } = string.Empty;
}

public sealed class RoutePathInputRow
{
    public string PathId { get; set; } = string.Empty;
    public string FromPlatformId { get; set; } = string.Empty;
    public string ToPlatformId { get; set; } = string.Empty;
    public string Direction { get; set; } = "雙向";
    public string TrackSegmentIds { get; set; } = string.Empty;
    public string ResourceIds { get; set; } = string.Empty;
}

public sealed class TurnbackInputRow
{
    public string TurnbackId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string Kind { get; set; } = "抽象折返";
    public string ArrivalPlatformId { get; set; } = string.Empty;
    public string DeparturePlatformId { get; set; } = string.Empty;
    public string TrackSegmentIds { get; set; } = string.Empty;
    public string ResourceIds { get; set; } = string.Empty;
    public double TurnbackTimeSeconds { get; set; }
}

public sealed class StationOvertakeFacilityInputRow
{
    public string FacilityId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string Direction { get; set; } = "下行";
    public string MainlineTrackSegmentId { get; set; } = string.Empty;
    public string LocalPlatformId { get; set; } = string.Empty;
    public string ExpressPlatformId { get; set; } = string.Empty;
    public string LocalTrackSegmentId { get; set; } = string.Empty;
    public string ExpressTrackSegmentId { get; set; } = string.Empty;
    public double EntryKm { get; set; }
    public string ResourceIds { get; set; } = string.Empty;
}

public sealed class SpatialReferencePointInputRow
{
    public string ReferencePointId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "站前折返";
    public bool AlternateBerthing { get; set; }
    public double MainlineGradePermille { get; set; }
    public double BranchlineGradePermille { get; set; }
    public double DistanceFromStopToCrossoverMeters { get; set; } = 100;
    public double CrossoverLengthMeters { get; set; } = 100;
    public double DistanceFromCrossoverToTurnbackStopMeters { get; set; } = 100;
    public double TurnbackDwellSeconds { get; set; } = 30;
    public double SwitchSpeedLimitKmh { get; set; } = 40;
    public double MainlineApproachCruiseSpeedKmh { get; set; } = 60;
    public double BranchlineApproachCruiseSpeedKmh { get; set; } = 80;
    public double MainlineSafetyFactor { get; set; } = 1.5;
    public double BranchlineSafetyFactor { get; set; } = 1.5;
    public double MainlineTrafficRatio { get; set; } = 0.5;
    public double StationForwardGradeInPermille { get; set; }
    public double StationForwardGradeOutPermille { get; set; }
    public double StationForwardDistanceToSignalMeters { get; set; } = 15;
    public double StationForwardOverlapMeters { get; set; } = 300;
    public double StationForwardDwellSeconds { get; set; } = 30;
    public double StationForwardEarlierCruiseSpeedKmh { get; set; } = 80;
    public double StationForwardLaterCruiseSpeedKmh { get; set; } = 80;
    public double StationForwardSafetyFactor { get; set; } = 1.5;
    public double StationReverseGradeInPermille { get; set; }
    public double StationReverseGradeOutPermille { get; set; }
    public double StationReverseDistanceToSignalMeters { get; set; } = 15;
    public double StationReverseOverlapMeters { get; set; } = 300;
    public double StationReverseDwellSeconds { get; set; } = 30;
    public double StationReverseEarlierCruiseSpeedKmh { get; set; } = 80;
    public double StationReverseLaterCruiseSpeedKmh { get; set; } = 80;
    public double StationReverseSafetyFactor { get; set; } = 1.5;
}

public sealed record IntervalStatisticRow(
    string Vehicle,
    string ServiceRun,
    string Direction,
    string Interval,
    string Status,
    string Departure,
    string Arrival,
    string TravelTime,
    string AverageSpeed,
    string PeakSpeed,
    string ControlLimitedTime,
    string ControlEvents);

public sealed record JourneyStatisticRow(
    string Vehicle,
    string ServiceRun,
    string Direction,
    string Route,
    string Status,
    string Departure,
    string Arrival,
    string TravelTime,
    string AverageSpeed);
