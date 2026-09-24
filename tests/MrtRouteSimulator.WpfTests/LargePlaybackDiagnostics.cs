using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class LargePlaybackDiagnostics
{
    public static void Run(string samplePath)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var projection = new StationChainageProjection(document);
        var origin = document.Topology.Nodes.Single(node => node.NodeId == "NODE:O01").SchematicPosition!.Value;
        foreach (var stationId in new[] { "O01", "O04", "O05", "O13" })
        {
            var source = document.Topology.Nodes.Single(node => node.NodeId == $"NODE:{stationId}")
                .SchematicPosition!.Value - origin;
            if (Math.Abs(projection.StationCenters[stationId] - source) > .001)
                throw new InvalidOperationException($"{stationId} 顯示里程未使用存檔車站中心。");
        }
        foreach (var platform in document.Topology.Platforms.Where(item => item.StationId == "O04"))
        {
            var center = (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2;
            var edgeLength = document.Topology.Edges.Single(edge => edge.TrackEdgeId == platform.TrackEdgeId)
                .LengthMeters;
            if (edgeLength - center > 100)
                throw new InvalidOperationException($"O04 月臺 {platform.PlatformId} 距離實體站點過遠。");
            if (Math.Abs(projection.ToChainage(new TrackPosition(platform.TrackEdgeId, center))!.Value
                - projection.StationCenters["O04"]) > .001)
                throw new InvalidOperationException($"O04 月臺 {platform.PlatformId} 未對齊顯示站心。");
        }
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window,
                "ConfigureTopologyProjectForPlaybackAsync", document, true));
            window.Height = 720;
            window.Show();
            WpfTestWait.Wait(Task.Delay(200));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            var canvas = (Canvas)window.FindName("RouteCanvas");
            ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem =
                window.FindName("SimulationTabItem");
            window.UpdateLayout();
            var viewTabs = (TabControl)window.FindName("SimulationViewTabControl");
            var routeViewport = (ScrollViewer)window.FindName("RouteScrollViewer");
            var simulationGrid = (Grid)viewTabs.Parent;
            var workspaceTabs = (TabControl)window.FindName("WorkspaceTabControl");
            Console.WriteLine($"layout viewTabs={viewTabs.ActualHeight:0.0} routeViewport={routeViewport.ActualHeight:0.0} "
                + $"simulationGrid={simulationGrid.ActualHeight:0.0} workspaceTabs={workspaceTabs.ActualHeight:0.0} "
                + $"row1={simulationGrid.RowDefinitions[1].ActualHeight:0.0} window={window.ActualHeight:0.0}");
            if (Math.Abs(viewTabs.ActualHeight - simulationGrid.RowDefinitions[1].ActualHeight) > 5
                || routeViewport.ActualHeight < viewTabs.ActualHeight - 65)
                throw new InvalidOperationException("路線圖分頁未撐滿模擬頁面剩餘高度。");
            typeof(MainWindow).GetMethod("DrawV2Route",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, [null]);
            var routeCache = WpfTestWait.Field(window, "_topologyRouteVisualCache")!;
            var geometries = routeCache.GetType().GetProperty("EdgeGeometries")!.GetValue(routeCache)!;
            var lookup = geometries.GetType().GetProperty("Item")!;
            System.Windows.Point Position(string edgeId, double ratio)
            {
                var geometry = lookup.GetValue(geometries, [edgeId])!;
                return (System.Windows.Point)geometry.GetType().GetMethod("PointAt")!.Invoke(geometry, [ratio])!;
            }
            var labels = canvas.Children.OfType<FrameworkElement>()
                .Where(element => element.Tag?.GetType().Name == "StationLabelAnchor")
                .ToArray();
            var labelCenters = labels.ToDictionary(
                label => label.Tag!.GetType().GetProperty("StationId")!.GetValue(label.Tag)!.ToString()!,
                label => (double)label.Tag!.GetType().GetProperty("PlatformCenterX")!.GetValue(label.Tag)!,
                StringComparer.OrdinalIgnoreCase);
            var bodyCenters = canvas.Children.OfType<System.Windows.Shapes.Rectangle>()
                .Where(element => element.Tag?.GetType().Name == "PlatformBodyAnchor")
                .GroupBy(element => element.Tag!.GetType().GetProperty("StationId")!.GetValue(element.Tag)!.ToString()!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key,
                    group => group.Select(element => Canvas.GetLeft(element) + element.Width / 2).Average(),
                    StringComparer.OrdinalIgnoreCase);
            CheckExplicitStationDisplayProjection(document, labelCenters, bodyCenters, "大型樣本");

            var outboundRouteId = document.DirectionRouteBindings
                .Single(binding => binding.Direction == TrainDirection.Outbound).ServiceRouteId;
            var outboundRoute = document.ServiceRoutes.Single(route => route.ServiceRouteId == outboundRouteId);
            CheckProjectedRouteEdges(document, projection, outboundRoute, geometries, bodyCenters);
            if ((Position("EDGE:PASS-001", 1) - Position("EDGE:DOWN:O04:O05", 0)).Length > .5)
                throw new InvalidOperationException("O04 側線列車位置在出站邊界不連續。");
            if (canvas.Height + .5 < routeViewport.ViewportHeight)
                throw new InvalidOperationException("路線圖畫布沒有填滿可視高度。");
            foreach (var stationId in new[] { "O04", "O13" })
            {
                var count = labels.Count(label => stationId.Equals(
                    label.Tag!.GetType().GetProperty("StationId")!.GetValue(label.Tag)?.ToString(),
                    StringComparison.Ordinal));
                if (count != 1) throw new InvalidOperationException($"{stationId} 站名繪製 {count} 次。");
            }
            if (canvas.Width < document.Topology.Stations.Count * 92 + 120)
                throw new InvalidOperationException("大型路線未保留每站最小水平間距。");
            if (canvas.Children.OfType<TextBlock>().Any(item => Equals(item.Tag, "TrackConnectionIssue")))
                throw new InvalidOperationException("大型路線仍顯示配線待修警告。");
            System.Windows.Shapes.Polyline Rail(string edgeId) => canvas.Children.OfType<System.Windows.Shapes.Polyline>()
                .Single(item => item.ToolTip?.ToString()?.StartsWith(edgeId + "\n", StringComparison.Ordinal) == true);
            foreach (var arrivalEdge in new[] { "EDGE:PASS-001", "EDGE:DOWN:O03:O04:B-001" })
            {
                var arrival = Rail(arrivalEdge);
                var departure = Rail("EDGE:DOWN:O04:O05");
                if ((arrival.Points[^1] - departure.Points[0]).Length > .5)
                    throw new InvalidOperationException($"O04 出站圖面不連續：{arrivalEdge}。");
            }
            var presentation = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.StationSchematicPresentation")!;
            var warnings = (IReadOnlyList<string>)presentation.GetMethod("ValidateStationLabels")!
                .Invoke(null, [canvas])!;
            if (warnings.Count != 0)
                throw new InvalidOperationException($"大型路線版面檢核：{string.Join("；", warnings)}");
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width),
                (int)Math.Ceiling(canvas.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var outputPath = Path.Combine(AppContext.BaseDirectory, "large-playback-route.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(outputPath)) encoder.Save(output);
            Console.WriteLine($"routeImage={outputPath}");
            window.UpdateLayout();
            var windowBitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            windowBitmap.Render(window);
            var windowPath = Path.Combine(AppContext.BaseDirectory, "large-playback-window.png");
            var windowEncoder = new PngBitmapEncoder();
            windowEncoder.Frames.Add(BitmapFrame.Create(windowBitmap));
            using (var output = File.Create(windowPath)) windowEncoder.Save(output);
            Console.WriteLine($"windowImage={windowPath}");
            var trainMarker = canvas.Children.OfType<Border>()
                .FirstOrDefault(item => item.Tag is string);
            if (trainMarker is null || trainMarker.Tag is not string selectedVehicleId)
                throw new InvalidOperationException("大型路線圖未顯示可點選的運行列車。");
            trainMarker.RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice,
                Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            });
            var speedTab = (TabItem)window.FindName("SpeedProfileTabItem");
            var speedSelector = (ComboBox)window.FindName("SpeedProfileRunComboBox");
            if (!ReferenceEquals(viewTabs.SelectedItem, speedTab)
                || !Equals(speedSelector.SelectedItem, selectedVehicleId))
                throw new InvalidOperationException("點選路線圖列車未跳至該車完整行程速度曲線。");
            viewTabs.SelectedIndex = 0;
            ((ComboBox)window.FindName("PlaybackSpeedComboBox")).SelectedIndex = 3;

            var timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            var clock = Stopwatch.StartNew();
            var last = clock.Elapsed;
            var maxInputGap = TimeSpan.Zero;
            var inputTicks = 0;
            var firstInputTick = TimeSpan.Zero;
            timer.Tick += (_, _) =>
            {
                var now = clock.Elapsed;
                if (++inputTicks == 1) firstInputTick = now;
                maxInputGap = TimeSpan.FromTicks(Math.Max(maxInputGap.Ticks, (now - last).Ticks));
                last = now;
            };
            timer.Start();
            var staticRebuildsBeforePlay = WpfTestWait.Field(window, "_routeStaticRebuildCount");
            var playInvokeStart = clock.Elapsed;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                WpfTestWait.Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            var playInvokeDuration = clock.Elapsed - playInvokeStart;
            WpfTestWait.Wait(Task.Delay(3000));
            timer.Stop();
            WpfTestWait.Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(50));

            var frame = WpfTestWait.LatestFrame(window);
            var status = (TextBlock)window.FindName("PlaybackStatusText");
            Console.WriteLine($"large-playback sample={samplePath}");
            var observedRate = frame.SimulationTimeSeconds / clock.Elapsed.TotalSeconds;
            Console.WriteLine($"sim={frame.SimulationTimeSeconds:0.0}s elapsed={clock.Elapsed.TotalSeconds:0.00}s "
                + $"observed={observedRate:0.0}x "
                + $"advance={frame.Performance.SimulationAdvanceMilliseconds:0.0}ms "
                + $"frame={frame.Performance.FrameBuildMilliseconds:0.0}ms "
                + $"maxInputGap={maxInputGap.TotalMilliseconds:0.0}ms "
                + $"inputTicks={inputTicks} firstInput={firstInputTick.TotalMilliseconds:0.0}ms "
                + $"playInvoke={playInvokeDuration.TotalMilliseconds:0.0}ms "
                + $"ui={WpfTestWait.Field(window, "_lastUiRenderMilliseconds"):0.0}ms "
                + $"route={WpfTestWait.Field(window, "_lastRouteRenderMilliseconds"):0.0}ms "
                + $"staticRebuilds={staticRebuildsBeforePlay}->{WpfTestWait.Field(window, "_routeStaticRebuildCount")} "
                + $"dropped={frame.Performance.FrameDropCount}");
            Console.WriteLine($"status={status.Text}");
            if (worker.Completion.IsFaulted)
            {
                throw worker.Completion.Exception!.GetBaseException();
            }

            if (observedRate < 50)
            {
                throw new InvalidOperationException($"大型樣本 60× 播放僅達 {observedRate:0.0}×。");
            }
            if (maxInputGap.TotalMilliseconds > 250)
                throw new InvalidOperationException($"播放期間 UI 輸入間隔達 {maxInputGap.TotalMilliseconds:0} ms。");
            if (!Equals(staticRebuildsBeforePlay, WpfTestWait.Field(window, "_routeStaticRebuildCount")))
                throw new InvalidOperationException("播放期間重複重建固定配線圖。");
        }
        finally
        {
            WpfTestWait.Close(window);
        }
    }

    private static void CheckExplicitStationDisplayProjection(
        TopologyProjectDocument document,
        IReadOnlyDictionary<string, double> labelCenters,
        IReadOnlyDictionary<string, double> bodyCenters,
        string scope)
    {
        var sourceStations = document.Topology.Stations
            .Select(station =>
            {
                var node = document.Topology.Nodes.SingleOrDefault(item =>
                    item.NodeId.Equals($"NODE:{station.StationId}", StringComparison.OrdinalIgnoreCase));
                return (station.StationId, Position: node?.SchematicPosition);
            })
            .Where(item => item.Position is not null && labelCenters.ContainsKey(item.StationId)
                && bodyCenters.ContainsKey(item.StationId))
            .Select(item => (item.StationId, Position: item.Position!.Value,
                LabelX: labelCenters[item.StationId], BodyX: bodyCenters[item.StationId]))
            .OrderBy(item => item.Position)
            .ToArray();
        if (sourceStations.Length < 3 || sourceStations[^1].Position - sourceStations[0].Position <= .001)
            return;

        var sourceStart = sourceStations[0];
        var sourceEnd = sourceStations[^1];
        var sourceSpan = sourceEnd.Position - sourceStart.Position;
        var screenSpan = sourceEnd.BodyX - sourceStart.BodyX;
        if (screenSpan <= 1)
            throw new InvalidOperationException($"{scope} 車站畫面中心沒有依來源里程向右排列。");

        foreach (var station in sourceStations)
        {
            var expected = sourceStart.BodyX + screenSpan * (station.Position - sourceStart.Position) / sourceSpan;
            if (Math.Abs(station.BodyX - expected) > 8)
                throw new InvalidOperationException(
                    $"{scope} 車站 {station.StationId} 畫面位置未符合來源里程：實際 {station.BodyX:0.0}px、預期 {expected:0.0}px。");
            if (Math.Abs(station.LabelX - station.BodyX) > .51)
                throw new InvalidOperationException(
                    $"{scope} 車站 {station.StationId} 站名與月臺中心錯位 {station.LabelX - station.BodyX:0.0}px。");
        }
    }

    private static void CheckProjectedRouteEdges(
        TopologyProjectDocument document,
        StationChainageProjection projection,
        ServiceRouteDefinition outboundRoute,
        object geometries,
        IReadOnlyDictionary<string, double> bodyCenters)
    {
        var sourceStations = document.Topology.Stations
            .Select(station =>
            {
                var node = document.Topology.Nodes.SingleOrDefault(item =>
                    item.NodeId.Equals($"NODE:{station.StationId}", StringComparison.OrdinalIgnoreCase));
                return (station.StationId, Position: node?.SchematicPosition);
            })
            .Where(item => item.Position is not null && bodyCenters.ContainsKey(item.StationId))
            .Select(item => (item.StationId, Position: item.Position!.Value, X: bodyCenters[item.StationId]))
            .OrderBy(item => item.Position)
            .ToArray();
        if (sourceStations.Length < 2) return;

        var sourceStart = sourceStations[0];
        var sourceEnd = sourceStations[^1];
        var sourceSpan = sourceEnd.Position - sourceStart.Position;
        var screenSpan = sourceEnd.X - sourceStart.X;
        if (sourceSpan <= .001 || screenSpan <= 1) return;
        var projectedStart = projection.StationCenters[sourceStart.StationId];
        double ExpectedX(double chainage) => sourceStart.X + screenSpan *
            (chainage - projectedStart) / sourceSpan;

        var geometryItem = geometries.GetType().GetProperty("Item")!;
        var edgeDefinitions = document.Topology.Edges.ToDictionary(edge => edge.TrackEdgeId,
            StringComparer.OrdinalIgnoreCase);
        foreach (var platform in document.Topology.Platforms)
        {
            if (!bodyCenters.TryGetValue(platform.StationId, out var stationX)) continue;
            var edge = edgeDefinitions[platform.TrackEdgeId];
            var geometry = geometryItem.GetValue(geometries, [edge.TrackEdgeId])!;
            var centerOffset = (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2;
            var center = (Point)geometry.GetType().GetMethod("PointAt")!
                .Invoke(geometry, [centerOffset / edge.LengthMeters])!;
            if (Math.Abs(center.X - stationX) > 8)
                throw new InvalidOperationException(
                    $"路線圖車站投影錯誤：{platform.StationId} 月臺 {platform.PlatformId} 中心偏離站心 {center.X - stationX:0.0}px。");
        }
        var routeEdges = outboundRoute.Traversals
            .Select(traversal => edgeDefinitions[traversal.TrackEdgeId])
            .ToArray();
        foreach (var traversal in outboundRoute.Traversals)
        {
            var edge = edgeDefinitions[traversal.TrackEdgeId];
            var geometry = geometryItem.GetValue(geometries, [edge.TrackEdgeId])!;
            var startOffset = traversal.Direction == TraversalDirection.Forward ? 0 : edge.LengthMeters;
            var endOffset = traversal.Direction == TraversalDirection.Forward ? edge.LengthMeters : 0;
            var startChainage = projection.ToChainage(new TrackPosition(edge.TrackEdgeId, startOffset));
            var endChainage = projection.ToChainage(new TrackPosition(edge.TrackEdgeId, endOffset));
            if (startChainage is null || endChainage is null) continue;

            var pointAt = geometry.GetType().GetMethod("PointAt")!;
            var startPoint = (Point)pointAt.Invoke(geometry, [traversal.Direction == TraversalDirection.Forward ? 0d : 1d])!;
            var endPoint = (Point)pointAt.Invoke(geometry, [traversal.Direction == TraversalDirection.Forward ? 1d : 0d])!;
            CheckEndpoint(edge.TrackEdgeId, startOffset, startPoint.X, startChainage.Value, ExpectedX, "起點");
            CheckEndpoint(edge.TrackEdgeId, endOffset, endPoint.X, endChainage.Value, ExpectedX, "終點");
        }

        for (var index = 1; index < routeEdges.Length; index++)
        {
            var previousTraversal = outboundRoute.Traversals[index - 1];
            var nextTraversal = outboundRoute.Traversals[index];
            var previousEdge = edgeDefinitions[previousTraversal.TrackEdgeId];
            var nextEdge = edgeDefinitions[nextTraversal.TrackEdgeId];
            var previousGeometry = geometryItem.GetValue(geometries, [previousEdge.TrackEdgeId])!;
            var nextGeometry = geometryItem.GetValue(geometries, [nextEdge.TrackEdgeId])!;
            var pointAtPrevious = previousGeometry.GetType().GetMethod("PointAt")!;
            var pointAtNext = nextGeometry.GetType().GetMethod("PointAt")!;
            var previousEnd = (Point)pointAtPrevious.Invoke(previousGeometry,
                [previousTraversal.Direction == TraversalDirection.Forward ? 1d : 0d])!;
            var nextStart = (Point)pointAtNext.Invoke(nextGeometry,
                [nextTraversal.Direction == TraversalDirection.Forward ? 0d : 1d])!;
            if ((previousEnd - nextStart).Length > .51)
            {
                var scope = previousEdge.TrackEdgeId.Contains("O03:O04", StringComparison.OrdinalIgnoreCase)
                    || nextEdge.TrackEdgeId.Contains("O03:O04", StringComparison.OrdinalIgnoreCase)
                    ? "O03→O04" : "下行路徑";
                throw new InvalidOperationException(
                    $"{scope} 軌道切換端點不連續：{previousEdge.TrackEdgeId} → {nextEdge.TrackEdgeId}，相差 {(previousEnd - nextStart).Length:0.0}px。");
            }
        }

        void CheckEndpoint(string edgeId, double offset, double actualX, double chainage,
            Func<double, double> expectedX, string endpoint)
        {
            var difference = actualX - expectedX(chainage);
            if (Math.Abs(difference) > 12)
                throw new InvalidOperationException(
                    $"路線圖車站投影錯誤：{edgeId} {endpoint} ({offset:0.#}m) 畫面 X 偏離來源里程 {difference:0.0}px。");
        }
    }
}
