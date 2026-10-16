using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class V411ResultTests
{
    public static void Run(string root)
    {
        var template = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "小型/01-小型-三站雙股島式站場範例.mrtsim.json")));
        var main = new OperatingLineDefinition
        {
            LineId = "RESULT-MAIN",
            Name = "結果測試主線",
            TrackCount = 2,
            LengthMeters = 3_600,
            StationCount = 5,
            OutboundServiceRouteId = "RESULT-MAIN:DOWN",
            InboundServiceRouteId = "RESULT-MAIN:UP"
        };
        var branch = new OperatingLineDefinition
        {
            LineId = "RESULT-BRANCH",
            Name = "結果測試支線",
            TrackCount = 2,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 900,
            LengthMeters = 1_800,
            StationCount = 4,
            OutboundServiceRouteId = "RESULT-BRANCH:DOWN",
            InboundServiceRouteId = "RESULT-BRANCH:UP"
        };
        var document = OperatingNetworkBuilder.Build(template, [main, branch]);
        var outputDirectory = Path.Combine(root, "output", "v411-results-build", "screenshots");
        Directory.CreateDirectory(outputDirectory);

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow
        {
            Width = 1600,
            Height = 1000,
            Left = -10000,
            Top = -10000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false
        };
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.WaitForPlannedTimeline(window);
            window.Show();
            window.UpdateLayout();

            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("V4.1.1 結果測試未建立播放工作者。"));
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(300));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);

            VerifyTimetable(window, document, branch, outputDirectory);
            VerifyIncrementalPassParity(window, branch);
            VerifyIntervalFilters(window, branch);
            VerifyBranchDiagram(window, document, branch, outputDirectory);
            Console.WriteLine("PASS WPF V4.1.1 route-aware results");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyTimetable(
        MainWindow window,
        TopologyProjectDocument document,
        OperatingLineDefinition branch,
        string outputDirectory)
    {
        SelectTab(window, "ResultsTabItem");
        WpfTestWait.Invoke(window, "PopulateV3Timetable");
        var routeFilter = (ComboBox)window.FindName("TimetableRouteFilterComboBox");
        var lineFilter = (ComboBox)window.FindName("TimetableOperatingLineFilterComboBox");
        var grid = (DataGrid)window.FindName("TimetableDataGrid");
        Require(routeFilter.Items.Count == document.ServiceRoutes.Length + 1,
            "時刻表營運路徑篩選必須包含全部與每條 ServiceRoute。");
        Require(lineFilter.Items.Count == document.OperatingLines!.Length + 1,
            "時刻表營運路線篩選必須包含全部與每條 OperatingLine。");
        Require(grid.Columns.Cast<DataGridColumn>().Any(column => Equals(column.Header, "表定停站"))
                && grid.Columns.Cast<DataGridColumn>().Any(column => Equals(column.Header, "實際停站"))
                && grid.Columns.Cast<DataGridColumn>().Any(column => Equals(column.Header, "營運路徑"))
                && grid.Columns.Cast<DataGridColumn>().Any(column => Equals(column.Header, "營運路線")),
            "時刻表必須顯示表定／實際停站秒與 route／line 欄位。");

        SelectOption(routeFilter, branch.OutboundServiceRouteId);
        WpfTestWait.Invoke(window, "PopulateV3Timetable");
        var branchRouteName = document.ServiceRoutes.Single(route =>
            route.ServiceRouteId == branch.OutboundServiceRouteId).Name;
        var frame = WpfTestWait.LatestFrame(window);
        var branchRun = frame.DispatchPlan!.Runs.Single(run =>
            run.ServiceRouteId == branch.OutboundServiceRouteId);
        var expectedStationCount = frame.GetTopologyResultContext().GetDisplayStations(branchRun).Count;
        Require(window.TimetableRows.Count == expectedStationCount,
            "支線下行篩選後只應保留該 route 的逐站時刻列。");
        Require(window.TimetableRows.All(row => row.ServiceRoute == branchRouteName
                && row.OperatingLine == branch.Name),
            "支線時刻列必須顯示可辨識的 route／line 名稱。");
        Require(window.TimetableRows.Skip(1).SkipLast(1).All(row =>
                row.PlannedDwellTime.EndsWith(" s", StringComparison.Ordinal)),
            "支線中途停靠站必須顯示表定停站秒數。");

        SelectOption(routeFilter, string.Empty);
        WpfTestWait.Invoke(window, "PopulateV3Timetable");
        Require(window.TimetableRows.Count >= 15,
            "全路徑時刻表截圖至少需要 15 筆樣本列。");
        window.UpdateLayout();
        var compactColumns = grid.Columns.Take(8).Where(column => column.ActualWidth < 60).ToArray();
        Console.WriteLine("v411TimetableColumnWidths=" + string.Join(',', grid.Columns.Select(column =>
            $"{column.Header}:{column.ActualWidth:0.#}/{column.Width}")));
        Require(grid.ActualWidth >= 1000 && compactColumns.Length == 0,
            "時刻表主要識別欄不得在寬視窗壓縮至不可讀寬度。"
            + $" grid={grid.ActualWidth:0.#}; compact={string.Join(',', compactColumns.Select(column => column.Header))}。");
        Capture(window, Path.Combine(outputDirectory, "v411-timetable-route-filter-15rows.png"));
    }

    private static void VerifyIncrementalPassParity(
        MainWindow window,
        OperatingLineDefinition branch)
    {
        var frame = WpfTestWait.LatestFrame(window);
        var planned = (IReadOnlyList<SimulationEvent>)(WpfTestWait.Field(window, "_plannedTimetableEvents")
            ?? Array.Empty<SimulationEvent>());
        var branchRun = frame.DispatchPlan!.Runs.Single(run =>
            run.ServiceRouteId == branch.OutboundServiceRouteId);
        var stations = frame.GetTopologyResultContext().GetDisplayStations(branchRun);
        var passStation = stations[stations.Count / 2];
        var patterns = new[]
        {
            new ServicePattern(branchRun.StopPatternId, "跨站 parity",
                stations.Select(station => new StationServiceInstruction(
                    station.StationId,
                    station.StationId.Equals(passStation.StationId, StringComparison.OrdinalIgnoreCase)
                        ? StationServiceMode.Pass
                        : StationServiceMode.Stop,
                    null,
                    station.StationId.Equals(passStation.StationId, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : 30)).ToArray())
        };
        var passArrival = frame.Events.Where(item =>
                item.ServiceRunId.Equals(branchRun.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                && item.Direction == branchRun.Direction
                && item.EventType is SimulationEventType.Arrival or SimulationEventType.StationPassed
                && frame.GetTopologyResultContext().MatchesStationEvent(
                    branchRun.Direction, passStation.StationId, item))
            .OrderBy(item => item.SimulationTimeSeconds)
            .First();
        var partialActual = frame.Events.Where(item =>
            item.ServiceRunId.Equals(branchRun.ServiceRunId, StringComparison.OrdinalIgnoreCase)
            && item.Direction == branchRun.Direction
            && item.SimulationTimeSeconds <= passArrival.SimulationTimeSeconds).ToArray();

        VerifyParity(Array.Empty<SimulationEvent>(), "pass-baseline", "待通過");
        VerifyParity(partialActual, "pass-arrival", "已通過");
        Require((string?)WpfTestWait.Invoke(window, "FormatTimetableDwell", null, "待通過", false)
                    == "不適用"
                && (string?)WpfTestWait.Invoke(window, "FormatTimetableDwell", null, "已通過", true)
                    == "不適用"
                && (string?)WpfTestWait.Invoke(window, "FormatTimetableDwell", null, "停站中", true)
                    == "停站中",
            "結果頁 dwell 顯示必須區分跨站不適用與仍在停站中。");

        void VerifyParity(IReadOnlyList<SimulationEvent> actualEvents, string label, string expectedStatus)
        {
            var expected = OperationsTimetable.Build(frame.GetTopologyResultContext(), frame.DispatchPlan!,
                planned, actualEvents, patterns);
            var accumulator = new SimulationResultAccumulator();
            accumulator.ConfigureTimetable(null, frame.GetTopologyResultContext(), frame.DispatchPlan!,
                planned, actualEvents, patterns);
            var incremental = accumulator.TimetableEntries
                ?? throw new InvalidOperationException($"{label} 未建立增量時刻表。");
            var mismatch = Enumerable.Range(0, Math.Min(expected.Count, incremental.Count))
                .FirstOrDefault(index => !Equals(expected[index], incremental[index]), -1);
            Require(expected.Count == incremental.Count && mismatch < 0,
                $"{label} 增量時刻表必須與 reference Build 一致；差異列 {mismatch}。");

            var passRow = incremental.Single(row =>
                row.ServiceRunId.Equals(branchRun.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                && row.StationId.Equals(passStation.StationId, StringComparison.OrdinalIgnoreCase));
            Require(passRow.Status == expectedStatus && passRow.ActualDwellSeconds is null,
                $"{label} 跨站狀態必須維持待通過／已通過，不得改成停站中。");
        }
    }

    private static void VerifyIntervalFilters(MainWindow window, OperatingLineDefinition branch)
    {
        SelectTab(window, "IntervalStatisticsTabItem");
        WpfTestWait.Invoke(window, "PopulateIntervalStatistics", false);
        var routeFilter = (ComboBox)window.FindName("IntervalServiceRouteComboBox");
        var lineFilter = (ComboBox)window.FindName("IntervalOperatingLineComboBox");
        Require(routeFilter.Items.Cast<CatalogOption>().Any(option => option.Id == branch.OutboundServiceRouteId),
            "區間統計營運路徑篩選必須列出支線 route。");
        Require(lineFilter.Items.Cast<CatalogOption>().Any(option => option.Id == branch.LineId),
            "區間統計營運路線篩選必須列出支線 line。");
    }

    private static void VerifyBranchDiagram(
        MainWindow window,
        TopologyProjectDocument document,
        OperatingLineDefinition branch,
        string outputDirectory)
    {
        SelectTab(window, "DiagramTabItem");
        var routeFilter = (ComboBox)window.FindName("DiagramRouteComboBox");
        Require(routeFilter.Items.Count == document.ServiceRoutes.Length,
            "運行圖必須提供每條實際派車 ServiceRoute 的選項。");
        SelectOption(routeFilter, branch.OutboundServiceRouteId);
        ((ComboBox)window.FindName("DiagramDirectionComboBox")).SelectedIndex = 0;
        ((CheckBox)window.FindName("ShowActualCheckBox")).IsChecked = true;
        ((CheckBox)window.FindName("ShowPlannedCheckBox")).IsChecked = true;
        ((TextBox)window.FindName("DiagramStartMinuteTextBox")).Text = "0";
        ((TextBox)window.FindName("DiagramEndMinuteTextBox")).Text = string.Empty;

        var canvas = (Canvas)window.FindName("TimeDistanceCanvas");
        canvas.Width = 1180;
        canvas.Height = 620;
        canvas.Measure(new Size(1180, 620));
        canvas.Arrange(new Rect(0, 0, 1180, 620));
        window.UpdateLayout();
        WpfTestWait.Invoke(window, "DrawTimeDistanceDiagramFull");

        var frame = WpfTestWait.LatestFrame(window);
        var branchRun = frame.DispatchPlan!.Runs.Single(run =>
            run.ServiceRouteId == branch.OutboundServiceRouteId);
        var expectedStations = frame.GetTopologyResultContext().GetDisplayStations(branchRun)
            .Select(station => station.StationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mainOnlyStations = frame.DispatchPlan.Runs
            .Where(run => run.ServiceRouteId == "RESULT-MAIN:DOWN")
            .SelectMany(run => frame.GetTopologyResultContext().GetDisplayStations(run))
            .Select(station => station.StationId)
            .Where(stationId => !expectedStations.Contains(stationId))
            .ToArray();
        var labels = canvas.Children.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();
        Require(expectedStations.All(stationId => labels.Any(text =>
                    text.Contains(stationId, StringComparison.OrdinalIgnoreCase))),
            "支線運行圖必須使用該 ServiceRoute 的車站序列。");
        Require(mainOnlyStations.All(stationId => labels.All(text =>
                    !text.Contains(stationId, StringComparison.OrdinalIgnoreCase))),
            "支線運行圖不得把分歧後主線車站畫入支線里程軸。");
        Require(canvas.Children.OfType<Polyline>().Any(line => line.Points.Count > 1),
            "支線運行圖必須繪出 trajectory polyline。");

        var previousBackground = canvas.Background;
        canvas.Background = Brushes.White;
        Capture(canvas, Path.Combine(outputDirectory, "v411-time-distance-branch-route.png"));
        canvas.Background = previousBackground;
    }

    private static void SelectTab(MainWindow window, string name)
    {
        ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem = window.FindName(name);
        window.UpdateLayout();
    }

    private static void SelectOption(ComboBox comboBox, string id)
    {
        comboBox.SelectedItem = comboBox.Items.Cast<CatalogOption>().Single(option =>
            option.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        Console.WriteLine($"v411ResultImage={path}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
