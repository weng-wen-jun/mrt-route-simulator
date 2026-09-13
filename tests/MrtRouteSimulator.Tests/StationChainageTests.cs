using MrtRouteSimulator.Engine;

internal static class StationChainageTests
{
    public static void StationCentersAnchorZeroAndExtendPastTerminals()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("找不到完整範例。");
        var comprehensive = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(directory.FullName,
            "samples", "V4.0.0-完整拓撲執行驗證範例.mrtsim.json")));
        var comprehensiveMap = new StationChainageProjection(comprehensive);
        foreach (var platform in comprehensive.Topology.Platforms)
        {
            var center = (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2;
            Equal(comprehensiveMap.StationCenters[platform.StationId],
                comprehensiveMap.ToChainage(new(platform.TrackEdgeId, center))!.Value);
            if (platform.StopPositionReference != StopPositionReference.TrainCenter)
                throw new InvalidOperationException("完整範例須以車體中心對齊月台。");
            Equal(center, platform.StopPositionOffsetMeters);
        }
        var document = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var map = new StationChainageProjection(document);
        Equal(0, map.StationCenters["A"]);
        Equal(770, map.StationCenters["B"]);
        Equal(1540, map.StationCenters["C"]);
        Equal(0, map.ToChainage(new("D0", 130))!.Value);
        Equal(0, map.ToChainage(new("U0", 470))!.Value);
        Equal(-530, map.ToChainage(new("TU2", 200))!.Value);
        if (map.ToChainage(new("D2", 600)) <= map.StationCenters["C"])
            throw new InvalidOperationException("終點站外側軌道應延伸到終點站中心里程之外。");
        foreach (var kind in Enum.GetValues<StationLayoutTemplateKind>())
        {
            var d = StationLayoutTemplateService.Build(kind);
            var projection = new StationChainageProjection(d);
            Equal(0, projection.StationCenters["A"]);
            foreach (var p in d.Topology.Platforms.Where(p => p.StationId is "A" or "C"))
            {
                var center = new TrackPosition(p.TrackEdgeId, (p.PlatformStartOffsetMeters + p.PlatformEndOffsetMeters) / 2);
                Equal(projection.StationCenters[p.StationId], projection.ToChainage(center)!.Value);
            }
        }
    }

    private static void Equal(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > .001)
            throw new InvalidOperationException($"中心里程預期 {expected}，實際 {actual}。");
    }
}
