namespace MrtRouteSimulator.Engine;

internal static class PlatformTurnback
{
    public static bool IsStationary(TurnbackFacilityDefinition f) =>
        f.Kind == TurnbackFacilityKind.Crossover && f.Traversals.Count == 2
        && string.Equals(f.Traversals[0].TrackEdgeId, f.ArrivalTrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(f.Traversals[1].TrackEdgeId, f.ArrivalTrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && f.Traversals[0].Direction != f.Traversals[1].Direction
        && string.Equals(f.DepartureTrackEdgeId, f.ArrivalTrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && f.TurnbackStopPosition is { } stop && string.Equals(stop.TrackEdgeId, f.ArrivalTrackEdgeId, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(stop.OffsetMeters - f.ArrivalStopOffsetMeters) < 1e-7
        && Math.Abs(stop.OffsetMeters - f.DepartureStartOffsetMeters) < 1e-7;
}
