using System.IO;
using System.Windows;
using System.Windows.Media;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

/// <summary>
/// Presentation-only regression checks for the time-distance station-label
/// layout. These are layout proxies: they do not replace native Windows DPI
/// and resize acceptance.
/// </summary>
internal static class TimeDistanceStationLabelTests
{
    public static void Run(string root = ".")
    {
        TimeDistanceStationLabelsDoNotOverlap();
        TimeDistanceLabelsRemainMappedToStations();
        TimeDistanceLabelsRemainValidAfterResize();
        TimeDistanceLabelsRemainValidAcrossSupportedScaleFactors();
        TimeDistanceLabelsUseLargestFittingFallbackAtBoundaries();
        TimeDistanceLabelsStayReadableForLargeCanvas();
        TimeDistanceRealSampleProjectionLabelsRemainValid(root);
        Console.WriteLine("[通過] Time-Distance station label layout");
    }

    public static void TimeDistanceStationLabelsDoNotOverlap()
    {
        var labels = BuildLabels(900, 420);
        var placements = TimeDistanceStationLabelLayout.Arrange(labels, 900, 420, 82);

        Require(placements.Count == 28, "大型 28 站 layout 應保留全部 station labels。");
        RequireNoOverlapAndNoClipping(placements, 900, 420);
        Require(placements.Any(item => item.StationId == "O08a" && item.IsDisplaced),
            "密集站群應透過通用 collision avoidance 位移 label，而不是覆蓋。");
        Require(placements.Any(item => item.StationId == "O15a" && item.IsDisplaced),
            "第二個密集站群應透過通用 collision avoidance 位移 label，而不是覆蓋。");
    }

    public static void TimeDistanceLabelsRemainMappedToStations()
    {
        var labels = BuildLabels(900, 420);
        var placements = TimeDistanceStationLabelLayout.Arrange(labels, 900, 420, 82);
        var expectedAnchors = labels.ToDictionary(item => item.StationId, item => item.AnchorY,
            StringComparer.OrdinalIgnoreCase);

        Require(placements.Select(item => item.StationId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 28,
            "每個 station label 必須有唯一 station mapping。");
        foreach (var placement in placements)
        {
            Require(expectedAnchors.TryGetValue(placement.StationId, out var expected),
                $"找不到 station label mapping：{placement.StationId}。");
            Require(Math.Abs(placement.AnchorY - expected) < .001,
                $"{placement.StationId} 的 station grid anchor 不得被 label layout 改寫。");
            Require(Math.Abs(placement.LeaderEndX - 78) < .001,
                $"{placement.StationId} 必須以明確 leader line 指向 station grid。");

            var text = TimeDistanceStationLabelLayout.CreateTextBlock(placement);
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Require(text.DesiredSize.Width <= placement.Width + .01
                    && text.DesiredSize.Height <= placement.Height + .01,
                $"{placement.StationId} 的實測 TextBlock bounds 不得超過 placement bounds。");
            Require(text.Tag is TimeDistanceStationLabelTag tag
                    && string.Equals(tag.StationId, placement.StationId, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(tag.AnchorY - placement.AnchorY) < .001,
                $"{placement.StationId} 的 TextBlock 必須保留 station mapping tag。");
            var leader = TimeDistanceStationLabelLayout.CreateLeaderLine(placement);
            Require(leader.X2 - leader.X1 >= .5,
                $"{placement.StationId} 的 leader line 必須向 station grid 保留可見連接段。");
            Require(leader.Tag is TimeDistanceStationLeaderTag leaderTag
                    && string.Equals(leaderTag.StationId, placement.StationId, StringComparison.OrdinalIgnoreCase),
                $"{placement.StationId} 的 leader line 必須保留 station mapping tag。");
        }
    }

    public static void TimeDistanceLabelsRemainValidAfterResize()
    {
        foreach (var (width, height) in new[]
        {
            (900d, 420d),
            (1280d, 720d),
            (760d, 380d),
            (1400d, 380d)
        })
        {
            var labels = BuildLabels(width, height);
            var placements = TimeDistanceStationLabelLayout.Arrange(labels, width, height, 82);
            RequireNoOverlapAndNoClipping(placements, width, height);
        }
    }

    public static void TimeDistanceLabelsRemainValidAcrossSupportedScaleFactors()
    {
        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
        {
            var labels = BuildLabels(900, 420);
            var placements = TimeDistanceStationLabelLayout.Arrange(
                labels,
                900,
                420,
                82,
                scaleFactor: scale);
            RequireNoOverlapAndNoClipping(placements, 900, 420);
        }
    }

    public static void TimeDistanceLabelsUseLargestFittingFallbackAtBoundaries()
    {
        var labels = Enumerable.Range(0, 6)
            .Select(index => new TimeDistanceStationLabelSpec(
                $"BOUNDARY-{index}",
                $"BOUNDARY-{index}  0.00 km",
                index % 2 == 0 ? 0 : 48,
                Color.FromRgb(82, 93, 111)))
            .ToArray();
        var placements = TimeDistanceStationLabelLayout.Arrange(
            labels,
            900,
            50,
            82,
            minimumFontSize: 7,
            gap: 1.5);

        Require(placements.All(item => item.FontSize > 2 && item.FontSize < 7),
            "低於 minimum font 的 boundary fallback 應選擇可讀的最大可行字型，而非回傳未符合限制的原字型。");
        RequireNoOverlapAndNoClipping(placements, 900, 50);
    }

    public static void TimeDistanceLabelsStayReadableForLargeCanvas()
    {
        var labels = BuildLabels(900, 600);
        var placements = TimeDistanceStationLabelLayout.Arrange(labels, 900, 600, 82);

        Require(placements.Min(item => item.FontSize) >= 8,
            "28 站在 600px 高 layout 不應因 fallback 被縮成不可讀的小字型。");
        RequireNoOverlapAndNoClipping(placements, 900, 600);
    }

    public static void TimeDistanceRealSampleProjectionLabelsRemainValid(string root)
    {
        var samplePath = ResolveAirportSample(root);
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(
            null,
            runtime.TrainParameters,
            runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count,
            document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        var stops = world.GetTopologyResultContext().GetStops(TrainDirection.Outbound);

        Require(stops.Count == 28, $"real airport sample 應有 28 個 outbound stops，實際 {stops.Count}。");
        var min = stops.Min(stop => stop.ProjectedChainageMeters);
        var max = stops.Max(stop => stop.ProjectedChainageMeters);
        var span = Math.Max(1, max - min);
        const double top = 48;
        const double left = 82;
        foreach (var height in new[] { 380d, 600d })
        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
        {
            var plotHeight = height - top - 42;
            var labels = stops.Select(stop => new TimeDistanceStationLabelSpec(
                stop.StationId,
                $"{stop.StationId}  {stop.ProjectedChainageMeters / 1000:0.00} km",
                top + plotHeight - (stop.ProjectedChainageMeters - min) / span * plotHeight,
                Color.FromRgb(82, 93, 111))).ToArray();
            var placements = TimeDistanceStationLabelLayout.Arrange(
                labels,
                900,
                height,
                left,
                topBoundary: top - 8,
                bottomBoundary: height - 2,
                scaleFactor: scale);

            Require(placements.Count == stops.Count,
                $"real sample layout station count mismatch at height={height}, scale={scale}。");
            RequireNoOverlapAndNoClipping(placements, 900, height);
            foreach (var placement in placements)
            {
                Require(stops.Any(stop => stop.StationId.Equals(placement.StationId, StringComparison.OrdinalIgnoreCase)),
                    $"real sample label 缺少 station mapping：{placement.StationId}。");
                var label = TimeDistanceStationLabelLayout.CreateTextBlock(placement);
                Require(label.Tag is TimeDistanceStationLabelTag tag
                        && tag.StationId.Equals(placement.StationId, StringComparison.OrdinalIgnoreCase),
                    $"real sample label tag mapping 遺失：{placement.StationId}。");
                var leader = TimeDistanceStationLabelLayout.CreateLeaderLine(placement);
                Require(leader.Tag is TimeDistanceStationLeaderTag leaderTag
                        && leaderTag.StationId.Equals(placement.StationId, StringComparison.OrdinalIgnoreCase)
                        && leader.X2 > leader.X1,
                    $"real sample leader line mapping 無效：{placement.StationId}。");
            }
        }
    }

    private static IReadOnlyList<TimeDistanceStationLabelSpec> BuildLabels(double width, double height)
    {
        var stationIds = Enumerable.Range(1, 26)
            .Select(index => $"O{index:00}")
            .ToList();
        stationIds.Insert(8, "O08a");
        stationIds.Insert(16, "O15a");

        // Use a monotonic source projection with the two native failure
        // clusters intentionally compressed. The y values remain source truth;
        // only label boxes may move.
        var positions = stationIds.Select((id, index) =>
            index switch
            {
                7 => 157,
                8 => 163,
                9 => 170,
                15 => 246,
                16 => 252,
                17 => 260,
                _ => 50 + index * ((height - 100) / 27)
            }).ToArray();

        return stationIds.Select((id, index) => new TimeDistanceStationLabelSpec(
            id,
            $"{id}  {index + 1:0.00} km",
            positions[index],
            Color.FromRgb(82, 93, 111))).ToArray();
    }

    private static string ResolveAirportSample(string root)
    {
        var samplesDirectory = Path.Combine(Path.GetFullPath(root), "samples");
        var paths = Directory.GetFiles(samplesDirectory, "*.mrtsim.json", SearchOption.AllDirectories);
        return paths.FirstOrDefault(path => Path.GetFileName(path)
                       .Equals("14-大型-二十八站完整營運範例.mrtsim.json", StringComparison.Ordinal))
            ?? paths.FirstOrDefault(path => Path.GetFileName(path)
                       .Contains("14-大型-二十八站", StringComparison.Ordinal)
                    && Path.GetFileName(path).Contains("完整", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("找不到 14-大型-二十八站完整營運 real Schema 8 sample。");
    }

    private static void RequireNoOverlapAndNoClipping(
        IReadOnlyList<TimeDistanceStationLabelPlacement> placements,
        double width,
        double height)
    {
        var ordered = placements.OrderBy(item => item.Top).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var current = ordered[index];
            Require(current.Left >= -.001
                    && current.Top >= -.001
                    && current.Bounds.Right <= width + .001
                    && current.Bounds.Bottom <= height + .001,
                $"{current.StationId} label bounds 超出 canvas：{current.Bounds}。");
            if (index == 0) continue;
            Require(current.Top >= ordered[index - 1].Bounds.Bottom - .001,
                $"station label bounding boxes 重疊：{ordered[index - 1].StationId}/{current.StationId}。");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
