using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>介面翻新第一輪（UiTheme + 軌道配線圖）的回歸測試。</summary>
internal static class TrackDiagramThemeTests
{
    private static readonly Assembly AppAssembly = typeof(MainWindow).Assembly;

    public static void Run(string root)
    {
        VerifyThemeTokens();
        VerifyTrainMarkers(root);
        VerifyRailStyles(root);
        VerifyPlatforms(root);
        VerifyStationLabels(root);
        VerifyTrackOccupancySnapshot(root);
        Console.WriteLine("PASS WPF track diagram theme");
    }

    private static void VerifyTrackOccupancySnapshot(string root)
    {
        var snapshotType = AppType("TrackOccupancySnapshot");
        var input = new Dictionary<string, IReadOnlyList<TrackOccupancyInterval>>(StringComparer.OrdinalIgnoreCase)
        {
            ["T2"] = [new TrackOccupancyInterval("E1", 300, 420)],
            ["T1"] = [new TrackOccupancyInterval("E1", 10, 150), new TrackOccupancyInterval("E2", 0, 0)],
            ["T3"] = [new TrackOccupancyInterval("E3", 5, 95)]
        };
        var result = (ImmutableDictionary<string, ImmutableArray<string>>)CallStatic(snapshotType, "Build", input)!;
        Require(result.Count == 2 && result.ContainsKey("E1") && result.ContainsKey("e3"),
            "占用彙整必須以 edge 為 key，且不分大小寫。");
        Require(!result.ContainsKey("E2"), "只在節點邊界接觸（零長度區間）的區段不可點亮。");
        Require(result["E1"].SequenceEqual(["T1", "T2"]), "同一區段的兩列車必須合併成一筆並依車號排序。");
        Require(((ImmutableDictionary<string, ImmutableArray<string>>)StaticValue(snapshotType, "Empty")!).IsEmpty,
            "Empty 必須是空字典。");

        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(
            System.IO.Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)WpfTestWait.Field(window, "_playbackTimer")!).Stop();
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(60));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView");
            var frame = WpfTestWait.LatestFrame(window);
            Require(frame.TrackEdgeOccupants.Count > 0, "運行中的 frame 必須帶有區段占用資料。");
            foreach (var train in frame.Trains.Where(item => item.IsActive
                         && item.Phase is not (OperationalPhase.Pending or OperationalPhase.OutOfService)
                         && frame.TrainCenterPositions.ContainsKey(item.VehicleId)))
            {
                var centerEdge = frame.TrainCenterPositions[train.VehicleId].TrackEdgeId;
                Require(frame.TrackEdgeOccupants.TryGetValue(centerEdge, out var occupants)
                        && occupants.Contains(train.VehicleId, StringComparer.OrdinalIgnoreCase),
                    $"{train.VehicleId}：車體中心所在區段 {centerEdge} 必須列為占用。");
            }
        }
        finally { WpfTestWait.Close(window); }
        Console.WriteLine("[通過] 區段占用彙整與 PlaybackFrame 傳遞");
    }

    private static void VerifyStationLabels(string root)
    {
        var presentation = AppType("StationSchematicPresentation");
        (string Primary, string Secondary) Lines(string id, string name, double? meters) =>
            ((string, string))CallStatic(presentation, "StationLabelLines", id, name, meters)!;
        Require(Lines("O03", "O03 站", 2197) == ("O03 站", "2.197K"), "站名已含 ID 時不可重複顯示 ID。");
        Require(Lines("BL12", "台北車站", 1234) == ("台北車站", "BL12 · 1.234K"), "站名不含 ID 時次要資訊必須帶 ID 與里程。");
        Require(Lines("W", "西站", null) == ("西站", "W"), "沒有里程時次要資訊只有 ID。");
        Require(Lines("X", "  ", null) == ("X", ""), "空白站名以 ID 為主要名稱。");
        Require(Lines("o03", "O03 站", null) == ("O03 站", ""), "ID 比對不分大小寫。");

        var badgeFill = StaticValue(AppType("UiTheme"), "StationBadgeFillBrush");
        var main = new MainWindow();
        try
        {
            var (small, _) = DrawSample(main, root, "10-小型-三站完整拓樸基準範例.mrtsim.json", 1200, 0);
            var labels = small.Children.OfType<FrameworkElement>().Where(item => item.Tag?.GetType().Name == "StationLabelAnchor").ToArray();
            Require(labels.Length == 3 && labels.All(item => item is Border border
                    && ReferenceEquals(border.Background, badgeFill) && border.Child is TextBlock),
                "範例 10 的三個站名都必須是淺灰圓角徽章。");
            var west = labels.Cast<Border>().Select(border => PlainText((TextBlock)border.Child)).Single(text => text.Contains("西站"));
            Require(west.Contains("W · "), $"西站徽章的次要資訊必須帶 ID 與里程，實際：{west}");

            var (large, _) = DrawSample(main, root, "14-大型-二十八站完整營運範例.mrtsim.json", 1370, 0);
            foreach (var label in large.Children.OfType<Border>().Where(item => item.Tag?.GetType().Name == "StationLabelAnchor"))
            {
                var stationId = (string)label.Tag.GetType().GetProperty("StationId")!.GetValue(label.Tag)!;
                var text = PlainText((TextBlock)label.Child);
                Require(text.Split(stationId).Length - 1 == 1, $"{stationId}：站名徽章不可重複顯示 ID（{text}）。");
            }
            var warnings = (IReadOnlyList<string>)CallStatic(presentation, "ValidateStationLabels", large)!;
            Require(warnings.Count == 0, $"範例 14：{string.Join("；", warnings)}");
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 站名徽章與 ID 去重");
    }

    private static void VerifyPlatforms(string root)
    {
        var theme = AppType("UiTheme");
        var platformFill = StaticValue(theme, "PlatformFillBrush");
        var main = new MainWindow();
        try
        {
            foreach (var (file, width) in new[]
            {
                ("10-小型-三站完整拓樸基準範例.mrtsim.json", 1200d),
                ("14-大型-二十八站完整營運範例.mrtsim.json", 1370d)
            })
            {
                var (canvas, sample) = DrawSample(main, root, file, width, 0);
                var bodies = canvas.Children.OfType<Rectangle>().Where(item => item.Tag?.GetType().Name == "PlatformBodyAnchor").ToArray();
                Require(bodies.Length > 0 && bodies.All(body => ReferenceEquals(body.Fill, platformFill)
                        && ReferenceEquals(body.Stroke, StaticValue(theme, "HairlineBrush")) && body.RadiusX == 3),
                    $"{file}：月台本體必須是白底、細框、圓角 3。");
                var badges = canvas.Children.OfType<Ellipse>().ToArray();
                var directions = sample.Topology.Platforms.ToDictionary(item => item.PlatformId, item => item.AllowedDirection,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var number in canvas.Children.OfType<TextBlock>().Where(item => item.Tag?.GetType().Name == "PlatformNumberAnchor"))
                {
                    var platformId = (string)number.Tag.GetType().GetProperty("PlatformId")!.GetValue(number.Tag)!;
                    number.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var textBox = new Rect(Canvas.GetLeft(number), Canvas.GetTop(number), number.DesiredSize.Width, number.DesiredSize.Height);
                    var textCenter = new Point(textBox.Left + textBox.Width / 2, textBox.Top + textBox.Height / 2);
                    var badge = badges.SingleOrDefault(item =>
                        new Rect(Canvas.GetLeft(item), Canvas.GetTop(item), item.Width, item.Height).Contains(textCenter));
                    Require(badge is not null, $"{file}/{platformId}：月台編號缺少對應的圓點。");
                    var toneName = directions[platformId] switch
                    {
                        TrackDirection.Outbound => "RailDownBrush",
                        TrackDirection.Inbound => "RailUpBrush",
                        _ => "PlatformBidirectionalBrush"
                    };
                    var tone = StaticValue(theme, toneName);
                    var hollow = ReferenceEquals(badge!.Fill, platformFill);
                    Require(number.Background is null, $"{file}/{platformId}：編號不可再有白底方塊。");
                    Require(hollow
                            ? ReferenceEquals(badge.Stroke, tone) && ReferenceEquals(number.Foreground, tone)
                            : ReferenceEquals(badge.Fill, tone) && number.Foreground is SolidColorBrush white && white.Color == Colors.White,
                        $"{file}/{platformId}：編號圓點顏色必須對應行車方向（{toneName}）。");
                    var badgeBox = new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), badge.Width, badge.Height);
                    Require(badgeBox.Contains(textBox), $"{file}/{platformId}：編號文字必須完整落在圓點內。");
                    Require(Panel.GetZIndex(badge) == 2 && Panel.GetZIndex(number) == 3, $"{file}/{platformId}：圓點與編號圖層順序錯誤。");
                }
                var warnings = (IReadOnlyList<string>)CallStatic(AppType("StationSchematicPresentation"), "ValidateStationLabels", canvas)!;
                Require(warnings.Count == 0, $"{file}：{string.Join("；", warnings)}");
            }
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 白色月台與方向色編號圓點");
    }

    private static void VerifyRailStyles(string root)
    {
        var theme = AppType("UiTheme");
        Color ThemeColor(string name) => (Color)StaticValue(theme, name)!;
        var main = new MainWindow();
        try
        {
            var routeCanvas = (Canvas)main.FindName("RouteCanvas");
            Require(ReferenceEquals(routeCanvas.Background, StaticValue(theme, "CanvasBackgroundBrush")),
                "配線圖底色必須取自 UiTheme.CanvasBackgroundBrush。");
            var (canvas, _) = DrawSample(main, root, "10-小型-三站完整拓樸基準範例.mrtsim.json", 1200, 0);
            var rails = canvas.Children.OfType<Polyline>()
                .Where(line => line.ToolTip is string tip && tip.Contains('\n'))
                .ToDictionary(line => ((string)line.ToolTip!).Split('\n', 2)[0], StringComparer.OrdinalIgnoreCase);
            var expected = new (string EdgeId, string Color, double Thickness)[]
            {
                ("DOWN-W-M", "RailDown", 6), ("DOWN-M-LOCAL", "RailDown", 6), ("DOWN-M-E", "RailDown", 6),
                ("UP-E-M", "RailUp", 6), ("UP-M-W", "RailUp", 6),
                ("PASS-LOOP-M", "RailDownSoft", 4),
                ("TAIL-OUT", "RailNeutral", 4), ("TAIL-W-OUT", "RailNeutral", 4), ("POCKET-OUT", "RailNeutral", 4),
                ("TAIL-OUT:ENTRY", "RailNeutral", 4), ("POCKET-OUT:EXIT", "RailNeutral", 4)
            };
            foreach (var (edgeId, colorName, thickness) in expected)
            {
                Require(rails.TryGetValue(edgeId, out var rail), $"缺少軌道 {edgeId}。");
                Require(rail!.Stroke is SolidColorBrush brush && brush.Color == ThemeColor(colorName) && rail.StrokeThickness == thickness,
                    $"{edgeId} 應為 {colorName}／{thickness}px，實際 {(rail.Stroke as SolidColorBrush)?.Color}／{rail.StrokeThickness}px。");
                Require(rail.Points.IsFrozen, $"{edgeId}：軌道點集合必須凍結以便亮燈圖層共用。");
            }

            var chevrons = canvas.Children.OfType<Polygon>().ToArray();
            Require(chevrons.Length > 0 && chevrons.All(arrow => arrow.Fill is SolidColorBrush fill && fill.Color == Colors.White),
                "單向軌道的方向箭頭必須是畫在軌道上的白色三角形。");
            var bufferStops = canvas.Children.OfType<Line>().Where(line => line.ToolTip is string tip && tip.Contains("止衝")).ToArray();
            Require(bufferStops.Length > 0 && bufferStops.All(line => line.Stroke is SolidColorBrush stroke
                    && stroke.Color == ThemeColor("RailNeutralStrong") && line.StrokeThickness == 4),
                "止衝擋必須使用 RailNeutralStrong、4px。");
            var allowed = new[] { "RailDown", "RailUp", "RailDownSoft", "RailUpSoft", "RailNeutral" }.Select(ThemeColor).ToArray();
            var connectors = canvas.Children.OfType<Polyline>()
                .Where(line => line.ToolTip is string tip && tip.StartsWith("合法轉向", StringComparison.Ordinal)).ToArray();
            Require(connectors.All(line => line.Stroke is SolidColorBrush stroke && allowed.Contains(stroke.Color)),
                "接軌曲線顏色必須取自軌道色票。");
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 雙色軌道、白色方向箭頭、接軌與止衝擋");
    }

    private static void VerifyTrainMarkers(string root)
    {
        var presentation = AppType("TrainMarkerPresentation");
        string Label(string id) => (string)CallStatic(presentation, "Label", id)!;
        var labelCases = new (string Input, string Expected)[]
        {
            ("Vehicle 3", "V03"),
            ("AUTO-007", "A07"),
            ("FULL-O04", "FULL-O04"),
            ("EXPRESS-01", "EXPRESS-01"),
            ("FULL-UP-01", "FULL-UP-01"),
            ("SECTION-VEHICLE-01", "SV-01"),
            ("FULL-SECTION-O04", "FS-O04"),
            ("普通車-區間快速-0001", "普區-0001"),
            ("LONG-VEHICLE-NAME-", "LV-NAME"),
            ("A--B-C-0123456789", "ABC-01234…"),
            ("ABCDEFGHIJKLMN", "ABCDEFGHI…")
        };
        var mainLabel = typeof(MainWindow).GetMethod("VehicleMarkerLabel", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var (input, expected) in labelCases)
        {
            var actual = Label(input);
            Require(actual == expected, $"車號 {input} 應顯示 {expected}，實際 {actual}。");
            Require(actual.Length is > 0 and <= 10, $"車號 {input} 的標籤長度必須介於 1～10。");
            Require((string)mainLabel.Invoke(null, [input])! == expected, $"MainWindow.VehicleMarkerLabel 必須委派給 TrainMarkerPresentation（{input}）。");
        }

        int Heading(Point? front, Point center, TrainDirection direction) =>
            (int)CallStatic(presentation, "Heading", front, center, direction)!;
        Require(Heading(new Point(110, 0), new Point(100, 0), TrainDirection.Inbound) == 1, "車頭在右側時箭頭必須朝右。");
        Require(Heading(new Point(90, 0), new Point(100, 0), TrainDirection.Outbound) == -1, "車頭在左側時箭頭必須朝左（例如折返後）。");
        Require(Heading(new Point(100.3, 0), new Point(100, 0), TrainDirection.Inbound) == -1, "車頭與車體中心幾乎重合時改依行車方向。");
        Require(Heading(null, new Point(100, 0), TrainDirection.Outbound) == 1, "缺少車頭位置時下行朝右。");

        var right = (Border)CallStatic(presentation, "Create", "SECTION-VEHICLE-01", Brushes.Teal, 1, "tip")!;
        var rightGrid = (Grid)right.Child;
        var rightText = rightGrid.Children.OfType<TextBlock>().Single();
        var rightChevron = rightGrid.Children.OfType<System.Windows.Shapes.Path>().Single();
        Require(right.Height == 18 && right.Width >= 34 && right.CornerRadius.TopLeft == 9, "列車膠囊應為高 18、圓角 9、最小寬 34。");
        Require(rightText.Text == "SV-01" && Grid.GetColumn(rightText) == 0 && Grid.GetColumn(rightChevron) == 1,
            "朝右膠囊的箭頭必須在右側（領先端）。");
        Require(Equals(right.ToolTip, "tip"), "膠囊必須保留呼叫端提供的提示文字。");
        var left = (Border)CallStatic(presentation, "Create", "E1", Brushes.Teal, -1, "tip")!;
        Require(Grid.GetColumn(((Grid)left.Child).Children.OfType<System.Windows.Shapes.Path>().Single()) == 0,
            "朝左膠囊的箭頭必須在左側（領先端）。");

        CallStatic(presentation, "Place", right, new Point(5, 5), 300d, 200d);
        Require(Canvas.GetLeft(right) == 0 && Canvas.GetTop(right) == 0 && Panel.GetZIndex(right) == 5,
            "左上角列車必須完整留在畫布內，且位於月台之上（ZIndex 5）。");
        CallStatic(presentation, "Place", right, new Point(298, 199), 300d, 200d);
        Require(Math.Abs(Canvas.GetLeft(right) + right.Width - 300) < 1e-9 && Math.Abs(Canvas.GetTop(right) - 182) < 1e-9,
            "右下角列車必須完整留在畫布內。");
        CallStatic(presentation, "Place", right, new Point(150, 100), 300d, 200d);
        Require(Math.Abs(Canvas.GetLeft(right) + right.Width / 2 - 150) < 1e-9 && Math.Abs(Canvas.GetTop(right) - 91) < 1e-9,
            "一般位置的列車必須以中心點置中。");

        var vehicleBrushes = (SolidColorBrush[])StaticValue(AppType("UiTheme"), "VehicleBrushes")!;
        var danger = StaticValue(AppType("UiTheme"), "DangerBrush");
        var main = new MainWindow();
        try
        {
            // 範例 11 的 LOCAL-01 於 0 秒停靠 P-W-D（VisualRulesTests 已驗證），可穩定取得列車標記。
            var (canvas, _) = DrawSample(main, root, "11-小型-三站完整拓樸運行範例.mrtsim.json", 1200, 0);
            var trains = canvas.Children.OfType<Border>().Where(border => border.Tag is string).ToArray();
            Require(trains.Length > 0, "範例 11 在 0 秒應至少顯示一列車。");
            foreach (var train in trains)
            {
                var text = ((Grid)train.Child).Children.OfType<TextBlock>().Single();
                Require(train.Height == 18 && Panel.GetZIndex(train) == 5, $"{train.Tag}：列車膠囊尺寸或圖層錯誤。");
                Require(vehicleBrushes.Any(brush => ReferenceEquals(brush, train.Background)) || ReferenceEquals(train.Background, danger),
                    $"{train.Tag}：列車顏色必須取自 UiTheme。");
                Require(text.Text == Label((string)train.Tag), $"{train.Tag}：列車標籤必須套用短號規則。");
                Require(Canvas.GetLeft(train) >= 0 && Canvas.GetLeft(train) + train.Width <= canvas.Width + 1e-9,
                    $"{train.Tag}：列車膠囊不可超出畫布。");
            }
        }
        finally { main.Close(); }
        Console.WriteLine("[通過] 膠囊列車標記：短號、方向、定位與配色");
    }

    private static (Canvas Canvas, TopologyProjectDocument Sample) DrawSample(
        MainWindow main, string root, string fileName, double width, double seconds)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(System.IO.Path.Combine(root, "samples", fileName)));
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, sample.Simulation.HeadwaySeconds,
            ProfileMode: sample.Simulation.ProfileMode, MovingBlockMode: sample.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan, VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology).CreateWorld();
        if (seconds > 0) world.AdvanceTo(seconds);
        ((MenuItem)main.FindName("ShowLockedRoutesMenuItem")).IsChecked = false;
        var canvas = (Canvas)main.FindName("RouteCanvas");
        canvas.Children.Clear();
        canvas.Width = width;
        canvas.Height = 400;
        var snapshot = world.GetSnapshot();
        var centers = snapshot.Trains
            .Select(state => (state.VehicleId, Center: world.GetTrainCenterPosition(state.VehicleId)))
            .Where(item => item.Center is not null)
            .ToDictionary(item => item.VehicleId, item => item.Center!.Value, StringComparer.OrdinalIgnoreCase);
        typeof(MainWindow).GetMethod("DrawTopologyGraphRoute", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(main, [runtime.Topology.Infrastructure, sample, snapshot, width, 400d, centers, world.GetActiveRouteLocks()]);
        return (canvas, sample);
    }

    private static void VerifyThemeTokens()
    {
        var theme = AppType("UiTheme");
        var brushes = theme.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(SolidColorBrush))
            .Select(field => (field.Name, Brush: (SolidColorBrush)field.GetValue(null)!))
            .ToArray();
        Require(brushes.Length >= 16, $"UiTheme 應提供至少 16 支具名畫筆，實際 {brushes.Length}。");
        Require(brushes.All(item => item.Brush.IsFrozen),
            $"UiTheme 畫筆必須全部凍結：{string.Join("、", brushes.Where(item => !item.Brush.IsFrozen).Select(item => item.Name))}");
        Require(((Color)StaticValue(theme, "OccupancyGlow")!).A == 0x80, "區段占用光帶必須是半透明（alpha 0x80）。");

        var vehicle = (Color[])StaticValue(theme, "VehiclePalette")!;
        Require(vehicle.Length == 8 && vehicle.Distinct().Count() == 8, "列車色盤必須是 8 個不重複顏色。");
        var avoid = new[] { "RailUp", "RailDown", "Danger" }.Select(name => Hsv((Color)StaticValue(theme, name)!).H).ToArray();
        foreach (var color in vehicle)
        {
            var (hue, saturation, _) = Hsv(color);
            Require(saturation < .35 || avoid.All(other => HueDistance(hue, other) >= 20),
                $"列車色 {color} 的色相太接近上下行軌道或危險紅。");
        }
        var vehicleBrushes = (SolidColorBrush[])StaticValue(theme, "VehicleBrushes")!;
        Require(vehicleBrushes.Length == vehicle.Length
            && vehicleBrushes.Select(brush => brush.Color).SequenceEqual(vehicle)
            && vehicleBrushes.All(brush => brush.IsFrozen), "VehicleBrushes 必須與 VehiclePalette 一一對應且凍結。");
        var negative = (SolidColorBrush)CallStatic(theme, "VehicleBrush", -3)!;
        Require(vehicleBrushes.Contains(negative), "VehicleBrush 對負索引也必須回傳色盤內的畫筆。");

        var locked = (Color[])StaticValue(theme, "LockedRoutePalette")!;
        Require(locked.Length == 5 && locked.All(color => color.R > color.B && color.G < 170),
            "鎖定進路色盤必須全部是 R > B 且 G < 170 的紅／洋紅系。");

        var trainColors = (Color[])typeof(MainWindow).GetField("TrainColors", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var lockColors = (Color[])typeof(MainWindow).GetField("LockedRouteColors", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Require(ReferenceEquals(trainColors, vehicle), "MainWindow.TrainColors 必須直接引用 UiTheme.VehiclePalette。");
        Require(ReferenceEquals(lockColors, locked), "MainWindow.LockedRouteColors 必須直接引用 UiTheme.LockedRoutePalette。");
        Console.WriteLine("[通過] UiTheme 色票、凍結畫筆與色盤規則");
    }

    // 站名徽章以 Inlines 組成，TextBlock.Text 不會反映其內容，需從 Run／LineBreak 取出純文字。
    private static string PlainText(TextBlock text) => string.Concat(text.Inlines.Select(inline => inline switch
    {
        System.Windows.Documents.Run run => run.Text,
        System.Windows.Documents.LineBreak => "\n",
        _ => ""
    }));

    private static (double H, double S, double V) Hsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = delta == 0 ? 0
            : max == r ? 60 * (((g - b) / delta) % 6)
            : max == g ? 60 * ((b - r) / delta + 2)
            : 60 * ((r - g) / delta + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    private static double HueDistance(double first, double second)
    {
        var distance = Math.Abs(first - second) % 360;
        return distance > 180 ? 360 - distance : distance;
    }

    private static Type AppType(string name) =>
        AppAssembly.GetType($"MrtRouteSimulator.App.{name}")
        ?? throw new InvalidOperationException($"找不到 App 型別 {name}。");

    private static object? StaticValue(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        if (type.GetField(name, flags) is { } field) return field.GetValue(null);
        if (type.GetProperty(name, flags) is { } property) return property.GetValue(null);
        throw new InvalidOperationException($"{type.Name} 找不到靜態成員 {name}。");
    }

    private static object? CallStatic(Type type, string method, params object?[] args)
    {
        var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{type.Name} 找不到靜態方法 {method}。");
        return target.Invoke(null, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
