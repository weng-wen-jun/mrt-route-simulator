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
            CheckPassingFacilitySchematicGeometry(document, geometries);
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

            var stopViolations = frame.Events
                .Where(item => item.EventType == SimulationEventType.StationStopViolation)
                .Take(3)
                .Select(item => $"{item.SimulationTimeSeconds:0.0}s {item.ServiceRunId}: {item.Message}")
                .ToArray();
            if (stopViolations.Length > 0)
                throw new InvalidOperationException("大型樣本播放仍發生停站速度違規："
                    + string.Join("；", stopViolations));

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

    private static void CheckPassingFacilitySchematicGeometry(
        TopologyProjectDocument document,
        object geometries)
    {
        var edges = document.Topology.Edges.ToDictionary(edge => edge.TrackEdgeId,
            StringComparer.OrdinalIgnoreCase);
        var platforms = document.Topology.Platforms.ToDictionary(platform => platform.PlatformId,
            StringComparer.OrdinalIgnoreCase);
        var geometryItem = geometries.GetType().GetProperty("Item")!;

        Point At(string edgeId, double offsetMeters)
        {
            var edge = edges[edgeId];
            var geometry = geometryItem.GetValue(geometries, [edgeId])!;
            return (Point)geometry.GetType().GetMethod("PointAt")!
                .Invoke(geometry, [Math.Clamp(offsetMeters / edge.LengthMeters, 0, 1)])!;
        }

        Point AtNode(string edgeId, string nodeId)
        {
            var edge = edges[edgeId];
            return edge.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)
                ? At(edgeId, 0)
                : edge.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)
                    ? At(edgeId, edge.LengthMeters)
                    : throw new InvalidOperationException($"越行設施 {edgeId} 沒有節點 {nodeId}。");
        }

        foreach (var stationId in new[] { "O04", "O13" })
            if (document.Topology.PassingFacilities.Count(facility =>
                    facility.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase)) != 2)
                throw new InvalidOperationException($"{stationId} 必須有上下行各一座越行設施。");

        foreach (var facility in document.Topology.PassingFacilities
                     .Where(item => item.StationId.Equals("O04", StringComparison.OrdinalIgnoreCase)
                         || item.StationId.Equals("O13", StringComparison.OrdinalIgnoreCase)))
        {
            var local = platforms[facility.LocalPlatformId];
            var through = platforms[facility.ExpressPlatformId];
            var localEdge = edges[local.TrackEdgeId];
            var throughEdge = edges[through.TrackEdgeId];
            if (!localEdge.FromNodeId.Equals(throughEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                || !localEdge.ToNodeId.Equals(throughEdge.ToNodeId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{facility.FacilityId} 側線與正線沒有共用相同進出節點。");
            }

            var entry = AtNode(facility.ArrivalTrackEdgeId, localEdge.FromNodeId);
            var localEntry = AtNode(local.TrackEdgeId, localEdge.FromNodeId);
            var throughEntry = AtNode(through.TrackEdgeId, throughEdge.FromNodeId);
            if ((entry - localEntry).Length > .51 || (entry - throughEntry).Length > .51)
                throw new InvalidOperationException($"{facility.FacilityId} 入口接軌端點不連續。");

            var exit = AtNode(facility.DepartureTrackEdgeId, localEdge.ToNodeId);
            var localExit = AtNode(local.TrackEdgeId, localEdge.ToNodeId);
            var throughExit = AtNode(through.TrackEdgeId, throughEdge.ToNodeId);
            if ((exit - localExit).Length > .51 || (exit - throughExit).Length > .51)
                throw new InvalidOperationException($"{facility.FacilityId} 出口接軌端點不連續。");

            var localCenterOffset = (local.PlatformStartOffsetMeters + local.PlatformEndOffsetMeters) / 2;
            var throughCenterOffset = (through.PlatformStartOffsetMeters + through.PlatformEndOffsetMeters) / 2;
            var localCenter = At(local.TrackEdgeId, localCenterOffset);
            var throughCenter = At(through.TrackEdgeId, throughCenterOffset);
            if (Math.Abs(localCenter.X - throughCenter.X) > 8)
                throw new InvalidOperationException($"{facility.FacilityId} 側線／正線月臺中心未對位：X 相差 {localCenter.X - throughCenter.X:0.0}px。");
            if (Math.Abs(localCenter.Y - throughCenter.Y) < 8)
                throw new InvalidOperationException($"{facility.FacilityId} 側線／正線月臺沒有保持可辨識的多軌間距。");

            var localStop = At(local.TrackEdgeId, local.StopPositionOffsetMeters);
            var throughStop = At(through.TrackEdgeId, through.StopPositionOffsetMeters);
            if (Math.Abs(localStop.X - throughStop.X) > 8)
                throw new InvalidOperationException($"{facility.FacilityId} 側線／正線停靠位置未對位：X 相差 {localStop.X - throughStop.X:0.0}px。");

        }

        CheckOppositeDirectionSymmetry("O04", 1);
        // O13 的下／上行 edge 為 660／645 m；資料中的相對站心月臺 offset
        // 相差 15 m，保留這個 synthetic sample 的近似對稱，不把兩條 edge
        // 強行視為等長。
        CheckOppositeDirectionSymmetry("O13", 20);

        void CheckOppositeDirectionSymmetry(string stationId, double offsetToleranceMeters)
        {
            var down = document.Topology.PassingFacilities.SingleOrDefault(item =>
                item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase)
                && item.ArrivalTrackEdgeId.Contains(":DOWN:", StringComparison.OrdinalIgnoreCase));
            var up = document.Topology.PassingFacilities.SingleOrDefault(item =>
                item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase)
                && item.ArrivalTrackEdgeId.Contains(":UP:", StringComparison.OrdinalIgnoreCase));
            if (down is null || up is null) return;

            var downLocal = platforms[down.LocalPlatformId];
            var upLocal = platforms[up.LocalPlatformId];
            var downThrough = platforms[down.ExpressPlatformId];
            var upThrough = platforms[up.ExpressPlatformId];
            var downLocalCenterOffset = (downLocal.PlatformStartOffsetMeters
                + downLocal.PlatformEndOffsetMeters) / 2;
            var upLocalCenterOffset = (upLocal.PlatformStartOffsetMeters
                + upLocal.PlatformEndOffsetMeters) / 2;

            void CheckRelativeOffset(string name, double downValue, double upValue)
            {
                if (Math.Abs(downValue - upValue) > offsetToleranceMeters)
                    throw new InvalidOperationException(
                        $"{stationId} 上下行{name}未以站心鏡射：下行 {downValue:0.#}m、上行 {upValue:0.#}m。");
            }

            CheckRelativeOffset("月臺起點",
                downLocalCenterOffset - downLocal.PlatformStartOffsetMeters,
                upLocalCenterOffset - upLocal.PlatformStartOffsetMeters);
            CheckRelativeOffset("月臺終點",
                downLocalCenterOffset - downLocal.PlatformEndOffsetMeters,
                upLocalCenterOffset - upLocal.PlatformEndOffsetMeters);
            CheckRelativeOffset("停靠點",
                downLocalCenterOffset - downLocal.StopPositionOffsetMeters,
                upLocalCenterOffset - upLocal.StopPositionOffsetMeters);
            var downLocalCenter = At(downLocal.TrackEdgeId,
                (downLocal.PlatformStartOffsetMeters + downLocal.PlatformEndOffsetMeters) / 2);
            var downThroughCenter = At(downThrough.TrackEdgeId,
                (downThrough.PlatformStartOffsetMeters + downThrough.PlatformEndOffsetMeters) / 2);
            var upLocalCenter = At(upLocal.TrackEdgeId,
                (upLocal.PlatformStartOffsetMeters + upLocal.PlatformEndOffsetMeters) / 2);
            var upThroughCenter = At(upThrough.TrackEdgeId,
                (upThrough.PlatformStartOffsetMeters + upThrough.PlatformEndOffsetMeters) / 2);
            if (Math.Abs(downLocalCenter.X - upLocalCenter.X) > 8
                || Math.Abs(downThroughCenter.X - upThroughCenter.X) > 8)
            {
                throw new InvalidOperationException($"{stationId} 上下行月臺中心未以站心鏡射對位。");
            }
            CheckMirroredTrackGap("月臺中心", downLocalCenter, downThroughCenter,
                upLocalCenter, upThroughCenter);

            var downLocalStop = At(downLocal.TrackEdgeId, downLocal.StopPositionOffsetMeters);
            var downThroughStop = At(downThrough.TrackEdgeId, downThrough.StopPositionOffsetMeters);
            var upLocalStop = At(upLocal.TrackEdgeId, upLocal.StopPositionOffsetMeters);
            var upThroughStop = At(upThrough.TrackEdgeId, upThrough.StopPositionOffsetMeters);
            if (Math.Abs(downLocalStop.X - upLocalStop.X) > 8
                || Math.Abs(downThroughStop.X - upThroughStop.X) > 8)
            {
                throw new InvalidOperationException($"{stationId} 上下行停靠點未以站心鏡射對位。");
            }
            CheckMirroredTrackGap("停靠點", downLocalStop, downThroughStop,
                upLocalStop, upThroughStop);

            if (stationId.Equals("O04", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (direction, local, through, centerOffset) in new[]
                {
                    ("下行", downLocal, downThrough, downLocalCenterOffset),
                    ("上行", upLocal, upThrough, upLocalCenterOffset)
                })
                {
                    CheckO04Junction(direction, "岔出", local, through, centerOffset - 240);
                    CheckO04Junction(direction, "合併", local, through, centerOffset + 240);
                    var sampleOffsets = new[] { 0d, 120d, 240d, 360d, 480d };
                    var x = sampleOffsets.Select(offset => At(local.TrackEdgeId, offset).X).ToArray();
                    var sign = direction == "下行" ? 1 : -1;
                    var advances = Enumerable.Range(1, x.Length - 1)
                        .Select(index => sign * (x[index] - x[index - 1])).ToArray();
                    if (advances.Any(advance => advance <= 1)
                        || advances.Max() - advances.Min() > 4)
                        throw new InvalidOperationException(
                            $"O04 {direction}側線列車在岔出與匯入間的畫面 X 不連續或速度突變。");
                }
            }

            // Compare the actual turnout curve at equal distances from the
            // platform center, rather than merely checking the end points.
            foreach (var distance in new[] { 100d, 200d })
            {
                Point BeforeCenter(string edgeId, double centerOffset)
                {
                    var edge = edges[edgeId];
                    var offset = centerOffset - distance;
                    if (offset < 0 || offset > edge.LengthMeters) return new(double.NaN, double.NaN);
                    return At(edgeId, offset);
                }

                var downLocalPoint = BeforeCenter(downLocal.TrackEdgeId, downLocalCenterOffset);
                var downThroughPoint = BeforeCenter(downThrough.TrackEdgeId,
                    (downThrough.PlatformStartOffsetMeters + downThrough.PlatformEndOffsetMeters) / 2);
                var upLocalPoint = BeforeCenter(upLocal.TrackEdgeId, upLocalCenterOffset);
                var upThroughPoint = BeforeCenter(upThrough.TrackEdgeId,
                    (upThrough.PlatformStartOffsetMeters + upThrough.PlatformEndOffsetMeters) / 2);
                if (!double.IsFinite(downLocalPoint.Y) || !double.IsFinite(downThroughPoint.Y)
                    || !double.IsFinite(upLocalPoint.Y) || !double.IsFinite(upThroughPoint.Y)) continue;
                var downGap = downLocalPoint.Y - downThroughPoint.Y;
                var upGap = upLocalPoint.Y - upThroughPoint.Y;
                if (Math.Abs(downGap + upGap) > 4)
                    throw new InvalidOperationException(
                        $"{stationId} 距站心 {distance:0}m 的上下行轉向未鏡射："
                        + $"下行 {downGap:0.0}px、上行 {upGap:0.0}px。");
            }

            void CheckO04Junction(string direction, string name,
                PlatformDefinitionV4 local, PlatformDefinitionV4 through, double offset)
            {
                var localEdge = edges[local.TrackEdgeId];
                var throughEdge = edges[through.TrackEdgeId];
                if (offset < 0 || offset > localEdge.LengthMeters
                    || offset > throughEdge.LengthMeters)
                {
                    throw new InvalidOperationException(
                        $"O04 {direction}{name} offset {offset:0.#}m 超出側線／正線 edge 範圍。");
                }

                var localPoint = At(local.TrackEdgeId, offset);
                var throughPoint = At(through.TrackEdgeId, offset);
                if ((localPoint - throughPoint).Length > .51)
                {
                    throw new InvalidOperationException(
                        $"O04 {direction}{name}未在月臺中心 {(offset - (local.PlatformStartOffsetMeters + local.PlatformEndOffsetMeters) / 2):0.#}m 接軌："
                        + $"畫面相差 {(localPoint - throughPoint).Length:0.0}px。");
                }
            }
        }

        static void CheckMirroredTrackGap(string scope,
            Point downLocal, Point downThrough, Point upLocal, Point upThrough)
        {
            var downGap = downLocal.Y - downThrough.Y;
            var upGap = upLocal.Y - upThrough.Y;
            if (downGap <= 8 || upGap >= -8)
                throw new InvalidOperationException(
                    $"上下行{scope}沒有形成上下鏡射軌距：下行 {downGap:0.0}px、上行 {upGap:0.0}px。");
            if (Math.Abs(Math.Abs(downGap) - Math.Abs(upGap)) > 4)
                throw new InvalidOperationException(
                    $"上下行{scope}軌距未鏡射：下行 {downGap:0.0}px、上行 {upGap:0.0}px。");
        }
    }
}
