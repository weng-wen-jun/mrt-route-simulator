namespace MrtRouteSimulator.Engine;

/// <summary>月台定位錨點與實際車頭之間的換算；runtime cursor 始終代表行進車頭。</summary>
public static class PlatformStopPositionResolver
{
    public static TrackPosition ResolveHeadPosition(PlatformDefinitionV4 platform,
        TraversalDirection direction, double trainLengthMeters) => new(platform.TrackEdgeId,
        platform.StopPositionOffsetMeters + (platform.StopPositionReference == StopPositionReference.TrainCenter
            ? (direction == TraversalDirection.Forward ? 1 : -1) * trainLengthMeters / 2 : 0));

    public static TrackPosition ResolveCenterPosition(TrackPosition head,
        TraversalDirection direction, double trainLengthMeters) => new(head.TrackEdgeId,
        head.OffsetMeters - (direction == TraversalDirection.Forward ? 1 : -1) * trainLengthMeters / 2);
}
