using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class RouteMotionGeometryTests
{
    public static void Run(string root)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,
            "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            window.Show();
            window.UpdateLayout();
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var cache = WpfTestWait.Field(window, "_topologyRouteVisualCache")!;
            var geometries = cache.GetType().GetProperty("EdgeGeometries")!.GetValue(cache)!;
            var item = geometries.GetType().GetProperty("Item")!;
            var edges = document.Topology.Edges.Where(edge => edge.Kind == TrackEdgeKind.Mainline
                && document.Topology.Platforms.Any(p => p.TrackEdgeId == edge.TrackEdgeId)
                && document.Topology.Platforms.Where(p => p.TrackEdgeId == edge.TrackEdgeId).All(p => !p.AllowsPassengerService))
                .OrderBy(edge => edge.TrackEdgeId.Contains("O12:O13") ? 0 : 1).ToArray();
            Require(edges.Length >= 2, "大型 sample 需要非載客快速通過正線作為定速重現案例。");
            foreach (var edge in edges)
            {
                var geometry = item.GetValue(geometries, [edge.TrackEdgeId])!;
                var pointAt = geometry.GetType().GetMethod("PointAt")!;
                var points = Enumerable.Range(0, 101).Select(i => (Point)pointAt.Invoke(geometry, [i / 100d])!).ToArray();
                // Equal physical offsets are equal travel times for a constant-speed train.
                // A passenger-station display anchor must not introduce acceleration on a through rail.
                var steps = points.Zip(points.Skip(1), (a, b) => Math.Abs(b.X - a.X)).ToArray();
                var min = steps.Min(); var max = steps.Max();
                Console.WriteLine($"route-motion geometry: {edge.TrackEdgeId}, length={edge.LengthMeters:0.0}m, equalOffsetXStep={min:0.000000}..{max:0.000000}px, ratio={max / Math.Max(min, 1e-12):0.000000}");
                Require(min > 0 && max / min < 1.001,
                    $"非載客通過正線 {edge.TrackEdgeId} 的等距位置仍出現假變速：{max / Math.Max(min, 1e-12):0.000}倍。");
                Require((points[0] - (Point)geometry.GetType().GetProperty("From")!.GetValue(geometry)!).Length < 1e-8
                    && (points[^1] - (Point)geometry.GetType().GetProperty("To")!.GetValue(geometry)!).Length < 1e-8,
                    "線性移動必須保留原有實體接軌端點。");
            }
            VerifyActualExpressSpeed(document);
            Console.WriteLine("PASS WPF through-route constant-speed geometry");
        }
        finally { WpfTestWait.Close(window); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static void VerifyActualExpressSpeed(TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(Route: null, TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters, TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds, ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode, ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        var samples = new List<(double Time, double Offset, double Speed)>();
        while (world.CurrentTimeSeconds < 2300)
        {
            world.Tick();
            var center = world.GetTrainCenterPosition("EXPRESS-01");
            if (center?.TrackEdgeId != "EDGE:DOWN:O12:O13:B-001")
            {
                if (samples.Count > 0) break;
                continue;
            }
            var train = world.GetSnapshot().Trains.Single(item => item.VehicleId == "EXPRESS-01");
            samples.Add((world.CurrentTimeSeconds, center.Value.OffsetMeters, train.SpeedMetersPerSecond));
        }
        Require(samples.Count > 20, "實際快速車必須通過 O13 B-001，以核對物理速度。");
        var throughPlatform = document.Topology.Platforms.Single(platform =>
            platform.StationId.Equals("O13", StringComparison.OrdinalIgnoreCase)
            && !platform.AllowsPassengerService
            && platform.TrackEdgeId.Equals("EDGE:DOWN:O12:O13:B-001", StringComparison.OrdinalIgnoreCase));
        var throughPlatformCenterOffset = (throughPlatform.PlatformStartOffsetMeters
            + throughPlatform.PlatformEndOffsetMeters) / 2;
        var crossing = samples.Zip(samples.Skip(1)).FirstOrDefault(pair =>
            pair.First.Offset < throughPlatformCenterOffset
            && pair.Second.Offset >= throughPlatformCenterOffset);
        Require(crossing.First.Speed > 0 && Math.Abs(crossing.First.Speed - crossing.Second.Speed) < 1e-6,
            "O13 顯示錨點兩側的實際快速車速度應連續且相同，此案例才能區分顯示假變速。");
        Console.WriteLine($"route-motion actual express: samples={samples.Count}, t={crossing.First.Time:0.0}->{crossing.Second.Time:0.0}s, offset={crossing.First.Offset:0.000}->{crossing.Second.Offset:0.000}m, speed={crossing.First.Speed * 3.6:0.000000}->{crossing.Second.Speed * 3.6:0.000000}km/h");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
