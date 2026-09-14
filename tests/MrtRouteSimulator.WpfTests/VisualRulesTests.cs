using System.Reflection;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class VisualRulesTests
{
    private static readonly Type Presentation = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.StationSchematicPresentation")!;
    private static object? Invoke(string method, params object[] args) => Presentation.GetMethod(method)!.Invoke(null, args);

    public static void Run()
    {
        var canvas = new Canvas { Width = 720, Height = 520 };
        canvas.Measure(new Size(720, 520)); canvas.Arrange(new Rect(0, 0, 720, 520));
        var label = new TextBlock { Text = "A\n測試站", TextAlignment = TextAlignment.Center };
        Invoke("PlaceStationLabel", label, "A", 24d, 60d, 720d);
        canvas.Children.Add(label);
        AddBody(canvas, "A", "A", 24);
        Require(Math.Abs(Canvas.GetLeft(label) + label.Width / 2 - 24) < .001, "邊界標籤必須維持月臺中心線。");
        Require(Warnings(canvas).Count == 0, "合法站名不應產生警告。");
        Canvas.SetLeft(label, Canvas.GetLeft(label) + 5);
        Require(Warnings(canvas).Any(w => w.Contains("未對齊")), "必须偵測站名偏移。");
        Invoke("PlaceStationLabel", label, "A", 24d, 510d, 720d);
        Require(Warnings(canvas).Any(w => w.Contains("邊界")), "必须偵測文字垂直越界。");
        Invoke("PlaceStationLabel", label, "A", 100d, 60d, 720d);
        Canvas.SetLeft(canvas.Children.OfType<Rectangle>().Single(), 88);
        var second = new TextBlock { Text = "B\n重疊站" };
        Invoke("PlaceStationLabel", second, "B", 110d, 60d, 720d); canvas.Children.Add(second);
        Require(Warnings(canvas).Any(w => w.Contains("重疊")), "必须偵測站名重疊。");

        var assembly = typeof(MainWindow).Assembly;
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var root = System.IO.Path.GetFullPath(Environment.GetCommandLineArgs().Length > 1 ? Environment.GetCommandLineArgs()[1] : ".");
        var output = System.IO.Path.Combine(root, "artifacts", "station-rules-visual");
        Directory.CreateDirectory(output);
        var paths = Directory.GetFiles(System.IO.Path.Combine(root, "samples"), "*.mrtsim.json", SearchOption.AllDirectories);
        Require(paths.Length >= 12, "完整範例矩陣不可缺少檔案。");
        var main = new MainWindow();
        try
        {
            foreach (var path in paths)
            foreach (var width in new[] { 720, 1200 })
            foreach (var stopTime in System.IO.Path.GetFileName(path).StartsWith("V4.0.0-完整", StringComparison.Ordinal)
                ? new[] { 0d, 120d, 311.5d } : new[] { 0d })
            {
                var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
                var sampleEditor = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, "Schematic"))!;
                try
                {
                    var sampleCanvas = new Canvas { Width = width, Height = 520, Background = Brushes.White };
                    sampleCanvas.Measure(new Size(width, 520)); sampleCanvas.Arrange(new Rect(0, 0, width, 520));
                    editorType.GetMethod("DrawSchematic", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sampleEditor, [sampleCanvas]);
                    CheckAndSave(sampleCanvas, sample, System.IO.Path.Combine(output, System.IO.Path.GetFileName(path) + $"-editor-{width}.png"));
                    var runtime = TopologyProjectFormat.CreateRuntime(sample);
                    var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
                        runtime.DispatchPlan.Runs.Count, sample.Simulation.HeadwaySeconds,
                        ProfileMode: sample.Simulation.ProfileMode, MovingBlockMode: sample.Simulation.MovingBlockMode,
                        ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes,
                        ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
                    if (sample.ProjectId == "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME")
                    {
                        world.AdvanceTo(stopTime);
                        var center = world.GetTrainCenterPosition("LOCAL-01");
                        var platform = sample.Topology.Platforms.Single(p => p.PlatformId ==
                            (stopTime == 0 ? "P-W-D" : stopTime == 120 ? "P-M-D" : "P-E-D"));
                        Require(center is { } c && c.TrackEdgeId == platform.TrackEdgeId
                            && Math.Abs(c.OffsetMeters - (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2) < .01,
                            $"{stopTime}s 停靠列車車體中心必須落在實體月台中心。");
                    }
                    var routeCanvas = (Canvas)main.FindName("RouteCanvas");
                    routeCanvas.Children.Clear(); routeCanvas.Width = width; routeCanvas.Height = 400;
                    typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(main, [runtime.Topology.Infrastructure, sample, world.GetSnapshot(), (double)width, 400d, world]);
                    var capture = new Canvas { Width = width, Height = routeCanvas.Height, Background = Brushes.White };
                    foreach (var child in routeCanvas.Children.Cast<UIElement>().ToArray()) { routeCanvas.Children.Remove(child); capture.Children.Add(child); }
                    CheckAndSave(capture, sample, System.IO.Path.Combine(output, System.IO.Path.GetFileName(path) + $"-main-{width}-t{stopTime}.png"),
                        stopTime == 0 ? "P-W-D" : stopTime == 120 ? "M:PASSING-ISLAND" : "P-E-D");
                    Console.WriteLine($"[通過] 全範例主圖／編輯器實體月臺對位及版面：{System.IO.Path.GetFileName(path)}/{width}");
                }
                finally { sampleEditor.Close(); }
            }
        }
        finally { main.Close(); }
        var draft = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var facility = draft.Topology.TurnbackFacilities[0];
        draft = draft with { Topology = draft.Topology with { TurnbackFacilities = Enumerable.Range(0, 20)
            .Select(i => facility with { FacilityId = $"TEST-{i}", Name = $"測試設施{i}" }).ToArray() } };
        var editor = (Window)Activator.CreateInstance(editorType, draft, Enum.Parse(pageType, "Schematic"))!;
        try
        {
            canvas.Children.Clear();
            editorType.GetMethod("DrawSchematic", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, [canvas]);
            Require(canvas.Height > 520, "大量設施必須擴大圖面，不能裁掉圖例。");
            Require(canvas.Children.OfType<TextBlock>().Count(t => t.Text.StartsWith("測試設施")) == 20, "設施圖例不可遺漏。");
            foreach (var item in canvas.Children.OfType<TextBlock>().Where(t => t.Text.StartsWith("測試設施")))
                Require(Canvas.GetTop(item) + 22 <= canvas.Height, "設施图例不可超出圖面。");
        }
        finally { editor.Close(); }
    }

    private static void AddBody(Canvas canvas, string station, string body, double center)
    {
        var anchorType = Presentation.GetNestedType("PlatformBodyAnchor", BindingFlags.NonPublic)!;
        var rectangle = new Rectangle { Width = 24, Height = 14, Tag = Activator.CreateInstance(anchorType, station, body) };
        Canvas.SetLeft(rectangle, center - 12); Canvas.SetTop(rectangle, 200); canvas.Children.Add(rectangle);
    }

    private static void CheckAndSave(Canvas canvas, TopologyProjectDocument sample, string path, string? stoppedBody = null)
    {
        foreach (var rail in canvas.Children.OfType<Polyline>())
        {
            for (var i = 1; i + 1 < rail.Points.Count; i++)
            {
                var incoming = rail.Points[i] - rail.Points[i - 1];
                var outgoing = rail.Points[i + 1] - rail.Points[i];
                if (incoming.Length < .0001 || outgoing.Length < .0001) continue;
                var angle = Math.Abs(Vector.AngleBetween(incoming, outgoing));
                Require(angle < 45, $"{path}：{rail.ToolTip} 存在 {angle:0.0} 度突折。");
            }
            Require(rail.Points.All(p => p.X >= 0 && p.X <= canvas.Width), $"{path}：軌道超出畫面。");
        }
        var connectionIssues = canvas.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "TrackConnectionIssue")).ToArray();
        Require(connectionIssues.Length == 0, $"{path}：仍有待修配置：{string.Join("；", connectionIssues.Select(t => t.ToolTip))}");
        var drawnRails = canvas.Children.OfType<Polyline>().Where(p => p.ToolTip is string).ToArray();
        foreach (var connection in sample.Topology.DirectedConnections)
        {
            if (connection.FromTrackEdgeId == connection.ToTrackEdgeId) continue; // stationary change of cab
            var from = drawnRails.Single(p => ((string)p.ToolTip).StartsWith(connection.FromTrackEdgeId + "\n", StringComparison.Ordinal));
            var to = drawnRails.Single(p => ((string)p.ToolTip).StartsWith(connection.ToTrackEdgeId + "\n", StringComparison.Ordinal));
            var a = connection.FromDirection == TraversalDirection.Forward ? from.Points[^1] - from.Points[^2] : from.Points[0] - from.Points[1];
            var b = connection.ToDirection == TraversalDirection.Forward ? to.Points[1] - to.Points[0] : to.Points[^2] - to.Points[^1];
            var connector = drawnRails.FirstOrDefault(p => Equals(p.ToolTip, $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})"));
            if (connector is not null)
            {
                CheckJoin(a, connector.Points[1] - connector.Points[0]);
                CheckJoin(connector.Points[^1] - connector.Points[^2], b);
            }
            else if (!canvas.Children.OfType<TextBlock>().Any(t => Equals(t.Tag, "TrackConnectionIssue")
                && t.ToolTip?.ToString()?.Contains($"{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})") == true)) CheckJoin(a, b);
            void CheckJoin(Vector first, Vector second)
            {
                if (first.Length < .0001 || second.Length < .0001) return;
                var angle = Math.Abs(Vector.AngleBetween(first, second));
                Require(angle < 45, $"{path}：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection}) 接軌反折 {angle:0.0} 度。");
            }
        }
        var labels = canvas.Children.OfType<FrameworkElement>().Where(e => e.Tag?.GetType().Name == "StationLabelAnchor").ToArray();
        var bodies = canvas.Children.OfType<Rectangle>().Where(e => e.Tag?.GetType().Name == "PlatformBodyAnchor").ToArray();
        Require(bodies.Length > 0, "實際月臺圖形不可為空。");
        if (sample.ProjectId == "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME")
        {
            foreach (var group in bodies.GroupBy(b => (string)b.Tag.GetType().GetProperty("StationId")!.GetValue(b.Tag)!))
            {
                var centers = group.Select(b => Canvas.GetLeft(b) + b.Width / 2).ToArray();
                Require(centers.Max() - centers.Min() < .51, $"{path}：同站各股道月台中心錯列。");
            }
            var rails = canvas.Children.OfType<Polyline>().Where(p => p.ToolTip is string).ToArray();
            foreach (var connection in sample.Topology.DirectedConnections)
            {
                var from = rails.Single(p => ((string)p.ToolTip).StartsWith(connection.FromTrackEdgeId + "\n", StringComparison.Ordinal));
                var to = rails.Single(p => ((string)p.ToolTip).StartsWith(connection.ToTrackEdgeId + "\n", StringComparison.Ordinal));
                var end = connection.FromDirection == TraversalDirection.Forward ? from.Points[^1] : from.Points[0];
                var start = connection.ToDirection == TraversalDirection.Forward ? to.Points[0] : to.Points[^1];
                Require((end - start).Length < .51, $"{path}：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection}) 必須直接接軌，不可用額外連線掩蓋斷點。");
            }
            foreach (var id in new[] { "POCKET-M", "P-CROSS-OUT", "P-CROSS-RETURN", "E-CROSS-OUT", "E-CROSS-RETURN", "W-CROSS-OUT", "W-CROSS-RETURN" })
            {
                var rail = rails.Single(p => ((string)p.ToolTip).StartsWith(id + "\n", StringComparison.Ordinal));
                var sign = Math.Sign(rail.Points[^1].X - rail.Points[0].X);
                Require(sign != 0, $"{path}：{id} 不能畫成垂直接軌。");
                for (var i = 1; i < rail.Points.Count; i++)
                    Require(sign * (rail.Points[i].X - rail.Points[i - 1].X) >= -.001, $"{path}：{id} 不可在行進中反折。");
            }
            if (path.Contains("-main-", StringComparison.Ordinal))
            {
                var train = canvas.Children.OfType<Border>().Single(b => b.ToolTip is string tip && tip.StartsWith("LOCAL-01｜"));
                var platform = bodies.Single(b => (string)b.Tag.GetType().GetProperty("BodyId")!.GetValue(b.Tag)! == stoppedBody);
                canvas.Measure(new Size(canvas.Width, canvas.Height));
                canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
                var trainCenter = train.TranslatePoint(new Point(train.ActualWidth / 2, train.ActualHeight / 2), canvas);
                var platformCenter = platform.TranslatePoint(new Point(platform.ActualWidth / 2, platform.ActualHeight / 2), canvas);
                Require(Math.Abs(trainCenter.X - platformCenter.X) < .51,
                    $"{path}：停靠列車圖示未對齊月台中心，相差 {trainCenter.X - platformCenter.X:0.###} px。");
            }
        }
        var numbers = canvas.Children.OfType<TextBlock>().Where(e => e.Tag?.GetType().Name == "PlatformNumberAnchor").ToArray();
        foreach (var platform in sample.Topology.Platforms)
            Require(numbers.Any(n => (string)n.Tag.GetType().GetProperty("PlatformId")!.GetValue(n.Tag)! == platform.PlatformId),
                $"{path}：月台 {platform.PlatformId} 未繪製，不能只檢查剩餘圖形就判定通過。");
        foreach (var body in bodies)
        {
            var station = (string)body.Tag.GetType().GetProperty("StationId")!.GetValue(body.Tag)!;
            var center = Canvas.GetLeft(body) + body.Width / 2;
            Require(labels.Any(label => (string)label.Tag.GetType().GetProperty("StationId")!.GetValue(label.Tag)! == station
                && Math.Abs(Canvas.GetLeft(label) + label.Width / 2 - center) < .51), $"{path}：{station} 的月臺實體中心沒有站名對位。");
        }
        var warnings = Warnings(canvas);
        canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width), (int)Math.Ceiling(canvas.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        Require(warnings.Count == 0, $"{path}：{string.Join("；", warnings)}");
        // 移動真正的月臺圖形而保留標籤Tag，檢核仍須抓到，避免自我驗證。
        var moved = bodies[0]; var oldLeft = Canvas.GetLeft(moved); Canvas.SetLeft(moved, oldLeft + 9);
        Require(Warnings(canvas).Any(w => w.Contains("未對齊")), "移動實際月臺必須觸發站名未對齊。");
        Canvas.SetLeft(moved, oldLeft);
    }

    private static IReadOnlyList<string> Warnings(Canvas canvas) => (IReadOnlyList<string>)Invoke("ValidateStationLabels", canvas)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
