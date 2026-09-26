using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

internal static class TopologyOperationEditorValues
{
    public static string[] SplitIds(string text) => text.Split(
        [',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

internal sealed class TurnbackOperationEditorViewModel(TurnbackOperationDefinition source)
{
    public string Id { get; set; } = source.OperationId;
    public string Facility { get; set; } = source.FacilityId;
    public string ArrivalRoute { get; set; } = source.ArrivalServiceRouteId;
    public string DepartureRoute { get; set; } = source.DepartureServiceRouteId;
    public double MinimumDwellSeconds { get; set; } = source.MinimumDwellTimeSeconds;

    public TurnbackOperationDefinition ToDomain() => new()
    {
        OperationId = Id.Trim(), FacilityId = Facility.Trim(), ArrivalServiceRouteId = ArrivalRoute.Trim(),
        DepartureServiceRouteId = DepartureRoute.Trim(), MinimumDwellTimeSeconds = MinimumDwellSeconds
    };
}

internal sealed class PassingOperationEditorViewModel(PassingOperationDefinition source)
{
    public string Id { get; set; } = source.OperationId;
    public string Facility { get; set; } = source.FacilityId;
    public string ServiceRoute { get; set; } = source.ServiceRouteId;
    public string ExpressServiceType { get; set; } = source.ExpressServiceTypeId ?? "";

    public PassingOperationDefinition ToDomain() => new()
    {
        OperationId = Id.Trim(), FacilityId = Facility.Trim(), ServiceRouteId = ServiceRoute.Trim(),
        ExpressServiceTypeId = EditorValue.EmptyToNull(ExpressServiceType)
    };
}

internal sealed class StationOperationEditorViewModel(StationOperationDefinition source)
{
    public string Id { get; set; } = source.StationOperationId;
    public string Station { get; set; } = source.StationId;
    public string ArrivalPlatforms { get; set; } = string.Join(", ", source.ArrivalPlatformIds);
    public string DeparturePlatforms { get; set; } = string.Join(", ", source.DeparturePlatformIds);
    public string TurnbackOperations { get; set; } = string.Join(", ", source.TurnbackOperationIds);
    public string PassingOperations { get; set; } = string.Join(", ", source.PassingOperationIds);
    public double DefaultDwellSeconds { get; set; } = source.DefaultDwellTimeSeconds;

    public StationOperationDefinition ToDomain() => new()
    {
        StationOperationId = Id.Trim(), StationId = Station.Trim(),
        ArrivalPlatformIds = TopologyOperationEditorValues.SplitIds(ArrivalPlatforms),
        DeparturePlatformIds = TopologyOperationEditorValues.SplitIds(DeparturePlatforms),
        TurnbackOperationIds = TopologyOperationEditorValues.SplitIds(TurnbackOperations),
        PassingOperationIds = TopologyOperationEditorValues.SplitIds(PassingOperations),
        DefaultDwellTimeSeconds = DefaultDwellSeconds
    };
}

internal sealed class DirectedConnectionEditorViewModel(DirectedTrackConnectionDefinition source)
{
    public string FromTrackEdge { get; set; } = source.FromTrackEdgeId;
    public TraversalDirection FromDirection { get; set; } = source.FromDirection;
    public string ToTrackEdge { get; set; } = source.ToTrackEdgeId;
    public TraversalDirection ToDirection { get; set; } = source.ToDirection;

    public DirectedTrackConnectionDefinition ToDomain() => new(
        FromTrackEdge.Trim(), FromDirection, ToTrackEdge.Trim(), ToDirection);
}

internal sealed class TrackCurveEditorViewModel(TrackCurveSegment source)
{
    public string Id { get; set; } = source.CurveId;
    public string TrackEdge { get; set; } = source.TrackEdgeId;
    public double StartOffsetMeters { get; set; } = source.StartOffsetMeters;
    public double EndOffsetMeters { get; set; } = source.EndOffsetMeters;
    public double RadiusMeters { get; set; } = source.RadiusMeters;

    public TrackCurveSegment ToDomain() => new(Id.Trim(), TrackEdge.Trim(), StartOffsetMeters, EndOffsetMeters, RadiusMeters);
}
