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
        VerifyMainWindowFitsAvailableWorkArea();
        VerifyRouteCanvasWidth();
        VerifyLayoutWarningButton();

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
            foreach (var width in System.IO.Path.GetFileName(path).Equals("大型機場線-完整營運示範範例.mrtsim.json", StringComparison.Ordinal)
                ? new[] { 720, 1200, 1370, 2512 }
                : new[] { 720, 1200 })
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

    private static void VerifyMainWindowFitsAvailableWorkArea()
    {
        var fit = typeof(MainWindow).GetMethod("FitWindowDimension", BindingFlags.NonPublic | BindingFlags.Static)!;
        var constrained = ((double Size, double Minimum))fit.Invoke(null, [1440d, 1180d, 1092d])!;
        Require(Math.Abs(constrained.Size - 1092d) < .001 && Math.Abs(constrained.Minimum - 1092d) < .001,
            "小於宣告最小寬度的工作區必須仍可容納主視窗。 ");

        var normal = ((double Size, double Minimum))fit.Invoke(null, [1440d, 1180d, 1600d])!;
        Require(Math.Abs(normal.Size - 1440d) < .001 && Math.Abs(normal.Minimum - 1180d) < .001,
            "足夠大的工作區必須保留原本舒適尺寸與最小尺寸。 ");

        var invalid = ((double Size, double Minimum))fit.Invoke(null, [1440d, 1180d, 0d])!;
        Require(Math.Abs(invalid.Size - 1440d) < .001 && Math.Abs(invalid.Minimum - 1180d) < .001,
            "無法取得工作區時必須保留 XAML 宣告尺寸。 ");
    }

    private static void VerifyRouteCanvasWidth()
    {
        var calculate = typeof(MainWindow).GetMethod("CalculateRouteCanvasWidth", BindingFlags.NonPublic | BindingFlags.Static)!;
        var fullLineAtNarrowViewport = (double)calculate.Invoke(null, [720d, 26])!;
        Require(Math.Abs(fullLineAtNarrowViewport - 2512d) < .001,
            "大型路線不得壓縮到窄視窗；應保留每站最小間距並由水平捲軸檢視。");

        var wideViewport = (double)calculate.Invoke(null, [3000d, 26])!;
        Require(Math.Abs(wideViewport - 3000d) < .001,
            "可視範圍較寬時，路線圖應填滿 viewport 而不產生不必要的水平捲動。");
    }

    private static void VerifyLayoutWarningButton()
    {
        var canvas = new Canvas { Width = 720, Height = 520 };
        canvas.Measure(new Size(720, 520)); canvas.Arrange(new Rect(0, 0, 720, 520));
        var label = new TextBlock { Text = "A\n測試站", TextAlignment = TextAlignment.Center };
        Invoke("PlaceStationLabel", label, "A", 24d, 60d, 720d);
        canvas.Children.Add(label);
        AddBody(canvas, "A", "A", 24);
        Canvas.SetLeft(label, Canvas.GetLeft(label) + 5);

        Invoke("DrawLayoutWarnings", canvas);
        var button = canvas.Children.OfType<Button>().SingleOrDefault();
        Require(button is not null, "版面檢核警告必須以可點擊按鈕呈現。");
        Require(button!.Content is string text && text.Contains("版面檢核"),
            "版面檢核按鈕必須保留警告標題與項目數量。");
        Require(button.Tag is IReadOnlyList<string> details && details.Any(detail => detail.Contains("未對齊")),
            "版面檢核按鈕必須攜帶既有檢核資料流產生的具體訊息。");
        Require(button.ToolTip is string toolTip && toolTip.Contains("未對齊"),
            "版面檢核按鈕的提示內容必須包含具體檢核訊息。");
    }

    private static void AddBody(Canvas canvas, string station, string body, double center)
    {
        var anchorType = Presentation.GetNestedType("PlatformBodyAnchor", BindingFlags.NonPublic)!;
        var rectangle = new Rectangle { Width = 24, Height = 14, Tag = Activator.CreateInstance(anchorType, station, body) };
        Canvas.SetLeft(rectangle, center - 12); Canvas.SetTop(rectangle, 200); canvas.Children.Add(rectangle);
    }

    private static void CheckAndSave(Canvas canvas, TopologyProjectDocument sample, string path, string? stoppedBody = null)
    {
        var verifiedSidings = sample.Topology.PassingFacilities
            .Select(facility => (
                Local: sample.Topology.Platforms.FirstOrDefault(p => p.PlatformId == facility.LocalPlatformId),
                Through: sample.Topology.Platforms.FirstOrDefault(p => p.PlatformId == facility.ExpressPlatformId)))
            .Where(pair => pair.Local is not null && pair.Through is not null
                && sample.Topology.Edges.Any(local => local.TrackEdgeId == pair.Local.TrackEdgeId
                    && local.Kind is TrackEdgeKind.Siding or TrackEdgeKind.PassingTrack
                    && sample.Topology.Edges.Any(through => through.TrackEdgeId == pair.Through.TrackEdgeId
                        && through.Kind == TrackEdgeKind.Mainline
                        && local.FromNodeId == through.FromNodeId && local.ToNodeId == through.ToNodeId)))
            .Select(pair => pair.Local!.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rail in canvas.Children.OfType<Polyline>())
        {
            var railId = (rail.ToolTip as string)?.Split('\n', 2)[0];
            for (var i = 1; i + 1 < rail.Points.Count; i++)
            {
                var incoming = rail.Points[i] - rail.Points[i - 1];
                var outgoing = rail.Points[i + 1] - rail.Points[i];
                if (incoming.Length < .0001 || outgoing.Length < .0001) continue;
                var angle = Math.Abs(Vector.AngleBetween(incoming, outgoing));
                Require(verifiedSidings.Contains(railId ?? "") ? angle <= 45 + 1e-6 : angle < 45,
                    $"{path}：{rail.ToolTip} 第 {i}/{rail.Points.Count} 點 {rail.Points[i]} 存在 {angle:0.0} 度突折；起點 {rail.Points[0]}，終點 {rail.Points[^1]}。");
            }
            Require(rail.Points.All(p => p.X >= 0 && p.X <= canvas.Width), $"{path}：軌道超出畫面。");
        }
        var connectionIssues = canvas.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "TrackConnectionIssue")).ToArray();
        var drawnRails = canvas.Children.OfType<Polyline>().Where(p => p.ToolTip is string).ToArray();
        var drawnRailById = drawnRails.ToDictionary(
            rail => ((string)rail.ToolTip!).Split('\n', 2)[0],
            StringComparer.OrdinalIgnoreCase);
        Require(connectionIssues.Length == 0, $"{path}：仍有待修配置：{string.Join("；", connectionIssues.Select(t => t.ToolTip))}");
        foreach (var mainline in sample.Topology.Edges.Where(edge => edge.Kind == TrackEdgeKind.Mainline))
        {
            if (!drawnRailById.TryGetValue(mainline.TrackEdgeId, out var rail)) continue;
            var ySpan = rail.Points.Count == 0 ? 0 : rail.Points.Max(point => point.Y) - rail.Points.Min(point => point.Y);
            Require(ySpan < .51,
                $"{path}：正線 {mainline.TrackEdgeId} 必須一路直線到底（Y 偏移 {ySpan:0.###} px）。");
        }
        // A service route may legally cross from one physical mainline to the
        // other through a crossover/turnback.  The visual contract therefore
        // applies to each physical mainline edge, not to the whole traversal
        // list across such a directed connection.
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
                var verifiedSidingJoin = verifiedSidings.Contains(connection.FromTrackEdgeId)
                    || verifiedSidings.Contains(connection.ToTrackEdgeId);
                Require(verifiedSidingJoin ? angle <= 45 + 1e-6 : angle < 45,
                    $"{path}：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection}) 接軌反折 {angle:0.0} 度。");
            }
        }
        var labels = canvas.Children.OfType<FrameworkElement>().Where(e => e.Tag?.GetType().Name == "StationLabelAnchor").ToArray();
        var bodies = canvas.Children.OfType<Rectangle>().Where(e => e.Tag?.GetType().Name == "PlatformBodyAnchor").ToArray();
        Require(bodies.Length > 0, "實際月臺圖形不可為空。");
        if (sample.ProjectId is "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME" or "LARGE-AIRPORT-LINE-FULL-DEMO")
        {
            if (sample.ProjectId == "LARGE-AIRPORT-LINE-FULL-DEMO")
            {
                foreach (var facility in sample.Topology.PassingFacilities)
                {
                    var localId = sample.Topology.Platforms.Single(p => p.PlatformId == facility.LocalPlatformId).TrackEdgeId;
                    var throughId = sample.Topology.Platforms.Single(p => p.PlatformId == facility.ExpressPlatformId).TrackEdgeId;
                    Require(verifiedSidings.Contains(localId), $"{path}：{facility.StationId} 側線與正線的實體關係尚未確認。");
                    var separation = Math.Abs(drawnRailById[localId].Points[drawnRailById[localId].Points.Count / 2].Y
                        - drawnRailById[throughId].Points[drawnRailById[throughId].Points.Count / 2].Y);
                    Require(separation >= 6, $"{path}：{facility.StationId} 側線與正線僅相距 {separation:0.0} px，無法辨識。");
                    var localPlatform = sample.Topology.Platforms.Single(p => p.PlatformId == facility.LocalPlatformId);
                    var sideY = drawnRailById[localId].Points[drawnRailById[localId].Points.Count / 2].Y;
                    var throughY = drawnRailById[throughId].Points[drawnRailById[throughId].Points.Count / 2].Y;
                    var bodyId = string.IsNullOrWhiteSpace(localPlatform.PlatformBodyId)
                        ? localPlatform.PlatformId : localPlatform.PlatformBodyId;
                    var body = bodies.Single(b => b.Tag?.GetType().GetProperty("StationId")?.GetValue(b.Tag)?.ToString() == facility.StationId
                        && b.Tag.GetType().GetProperty("BodyId")?.GetValue(b.Tag)?.ToString() == bodyId);
                    var bodyCenterY = Canvas.GetTop(body) + body.Height / 2;
                    Require(sideY < throughY ? bodyCenterY < sideY : bodyCenterY > sideY,
                        $"{path}：{facility.StationId} 月臺本體應位於待避線外側（側線 {sideY:0.0}、正線 {throughY:0.0}、月臺 {bodyCenterY:0.0}）。");
                    var expressPlatform = sample.Topology.Platforms.Single(p => p.PlatformId == facility.ExpressPlatformId);
                    if (!expressPlatform.AllowsPassengerService)
                    {
                        var markerBodyId = string.IsNullOrWhiteSpace(expressPlatform.PlatformBodyId)
                            ? expressPlatform.PlatformId : expressPlatform.PlatformBodyId;
                        var markerBody = bodies.Single(b => b.Tag?.GetType().GetProperty("StationId")?.GetValue(b.Tag)?.ToString() == facility.StationId
                            && b.Tag.GetType().GetProperty("BodyId")?.GetValue(b.Tag)?.ToString() == markerBodyId);
                        Require(markerBody.Opacity == 0,
                            $"{path}：{facility.StationId} 通過正線不可畫成第三、第四座月臺。");
                    }
                    if (path.Contains("-2512", StringComparison.Ordinal))
                    {
                        var siding = drawnRailById[localId];
                        Require(Math.Abs(siding.Points[^1].X - siding.Points[0].X) >= 36,
                            $"{path}：{facility.StationId} 側線沒有足夠的分岔至匯入長度。");
                        var sidingEdge = sample.Topology.Edges.Single(e => e.TrackEdgeId == localId);
                        var startIndex = (int)Math.Ceiling(localPlatform.PlatformStartOffsetMeters / sidingEdge.LengthMeters * 100);
                        var endIndex = (int)Math.Floor(localPlatform.PlatformEndOffsetMeters / sidingEdge.LengthMeters * 100);
                        Require(Math.Abs(siding.Points[endIndex].X - siding.Points[startIndex].X) >= 8,
                            $"{path}：{facility.StationId} 側線月臺旁缺少平行直線段。");
                        Require(Math.Abs(siding.Points[endIndex].Y - siding.Points[startIndex].Y) < .5,
                            $"{path}：{facility.StationId} 側線月臺旁未保持水平。");
                    }
                }
                foreach (var station in sample.Topology.PassingFacilities.GroupBy(f => f.StationId)
                    .Where(group => group.Count() == 2))
                {
                    var tracks = station.Select(f =>
                    {
                        var localId = sample.Topology.Platforms.Single(p => p.PlatformId == f.LocalPlatformId).TrackEdgeId;
                        var throughId = sample.Topology.Platforms.Single(p => p.PlatformId == f.ExpressPlatformId).TrackEdgeId;
                        return (LocalY: drawnRailById[localId].Points[drawnRailById[localId].Points.Count / 2].Y,
                            ThroughY: drawnRailById[throughId].Points[drawnRailById[throughId].Points.Count / 2].Y);
                    }).OrderBy(pair => pair.ThroughY).ToArray();
                    Require(tracks[0].LocalY < tracks[0].ThroughY
                        && tracks[0].ThroughY < tracks[1].ThroughY
                        && tracks[1].ThroughY < tracks[1].LocalY,
                        $"{path}：{station.Key} 應由上而下呈現待避線、通過正線、通過正線、待避線。");
                }
            }
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
            if (sample.ProjectId == "V4-TOPOLOGY-COMPREHENSIVE-RUNTIME")
            {
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
        }
        var numbers = canvas.Children.OfType<TextBlock>().Where(e => e.Tag?.GetType().Name == "PlatformNumberAnchor").ToArray();
        foreach (var platform in sample.Topology.Platforms)
            Require(numbers.Any(n => (string)n.Tag.GetType().GetProperty("PlatformId")!.GetValue(n.Tag)! == platform.PlatformId),
                $"{path}：月台 {platform.PlatformId} 未繪製，不能只檢查剩餘圖形就判定通過。");
        foreach (var body in bodies)
        {
            var station = (string)body.Tag.GetType().GetProperty("StationId")!.GetValue(body.Tag)!;
            var stationBodies = bodies.Where(candidate => (string)candidate.Tag.GetType().GetProperty("StationId")!.GetValue(candidate.Tag)! == station).ToArray();
            var left = stationBodies.Min(candidate => Canvas.GetLeft(candidate));
            var right = stationBodies.Max(candidate => Canvas.GetLeft(candidate) + candidate.Width);
            var matchingLabels = labels.Where(label => (string)label.Tag.GetType().GetProperty("StationId")!.GetValue(label.Tag)! == station).ToArray();
            Require(matchingLabels.Length == 1, $"{path}：{station} 應只有一個主站名，不能按停靠／通過股重複繪製。");
            Require(Math.Abs(Canvas.GetLeft(matchingLabels[0]) + matchingLabels[0].Width / 2 - (left + right) / 2) < .51,
                $"{path}：{station} 的主站名未對齊所有月臺本體的視覺中心。");
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
