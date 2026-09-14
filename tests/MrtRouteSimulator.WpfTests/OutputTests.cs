using System.Reflection;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// WPF result/output regression coverage, registered in the STA test runner.
/// </summary>
internal static class OutputTests
{
    public static void Run(string root)
    {
        var output = Path.Combine(root, "artifacts", "output-qa");
        Directory.CreateDirectory(output);

        var complete = Load(root, "V4.0.0-完整拓撲執行驗證範例.mrtsim.json");
        try
        {
            VerifyLoadedPlan(complete.Window, "完整拓撲");
            AdvanceAndVerify(complete, output, "complete");
        }
        finally
        {
            complete.Window.Close();
        }

        var rearTurnback = Load(root, "PDF-RearTurnback.mrtsim.json");
        try
        {
            VerifyLoadedPlan(rearTurnback.Window, "TrainCenter 尾軌折返");
            AdvanceAndVerify(rearTurnback, output, "rear-turnback");
            VerifyTrainCenterStationEvents(rearTurnback, "UP-1");
        }
        finally
        {
            rearTurnback.Window.Close();
        }

        Console.WriteLine("[通過] WPF 輸出頁：完整拓撲／TrainCenter 尾軌折返載入、3600 秒、V1/V2、統計、CSV、PNG、PDF");
    }

    private static (MainWindow Window, SimulationSession Session, TopologyProjectDocument Document) Load(
        string root,
        string fileName)
    {
        var path = Path.Combine(root, "samples", fileName);
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
        var window = new MainWindow();
        try
        {
            Invoke(window, "ConfigureTopologyProjectForPlayback", document, true);
            var session = Field(window, "_v2Session") as SimulationSession
                ?? throw new InvalidOperationException($"{fileName} 未建立 SimulationSession。");
            return (window, session, document);
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static void VerifyLoadedPlan(MainWindow window, string label)
    {
        Require(window.TimetableRows.Count > 0, $"{label}載入後計畫時刻表不可空白。");
        Require(window.TimetableRows.Any(row => row.PlannedDepartureTime != "—"),
            $"{label}載入後計畫發車時刻不可全為空白。");
        Require(window.V1V2ComparisonRows.Count > 0,
            $"{label}載入後 V1/V2 比較頁不可因 legacy Route null guard 而空白。");
    }

    private static void AdvanceAndVerify(
        (MainWindow Window, SimulationSession Session, TopologyProjectDocument Document) scenario,
        string output,
        string label)
    {
        var window = scenario.Window;
        var session = scenario.Session;
        var document = scenario.Document;
        SetField(window, "_playbackTimeSeconds", 3600d);
        Invoke(window, "UpdateV2PlaybackView");

        Require(session.ActualWorld.CurrentTimeSeconds >= 3599.99,
            $"{label}實際世界未推進到 3600 秒。");
        Require(window.TimetableRows.Any(row => row.ArrivalTime != "—" || row.DepartureTime != "—"),
            $"{label}實際時刻表仍全為空白。");
        Require(window.SegmentRows.Count > 0, $"{label}區間明細不可空白。");
        Require(window.IntervalStatisticRows.Count > 0, $"{label}區間統計不可空白。");
        Require(window.JourneyStatisticRows.Count > 0, $"{label}起終站統計不可空白。");
        Require(window.ResourceOccupancyRows.Count > 0, $"{label}資源占用統計不可空白。");
        Require(window.V1V2ComparisonRows.Any(row => row.ActualArrival != "—"),
            $"{label} V1/V2 實際到站比較不可全為空白。");

        VerifyIntervalDirectionFilter(window, TrainDirection.Outbound, "下行", label);
        VerifyIntervalDirectionFilter(window, TrainDirection.Inbound, "上行", label);

        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = session.ActualWorld;
        var context = world.GetTopologyResultContext();
        var timetable = OperationsTimetable.Build(context, runtime.DispatchPlan, session.PlannedEvents, world.Events);
        var intervals = IntervalStatistics.Analyze(context, world.Trajectory, world.Events);
        var resource = ResourceOccupancyAnalysis.Analyze(world.Events, world.CurrentTimeSeconds);
        var v1v2 = V1V2Comparison.Analyze(
            context,
            runtime.DispatchPlan,
            runtime.VehicleTypes,
            document.StopPatterns.Select(pattern => new StopPatternDefinition(
                pattern.Id,
                pattern.DisplayName,
                pattern.Instructions.Select(instruction => new StopPatternInstruction(
                    instruction.StationId,
                    instruction.Action,
                    instruction.DwellTimeSeconds,
                    instruction.PassingSpeedLimitMetersPerSecond)))),
            runtime.TrainParameters,
            world.Events);

        Require(timetable.Any(item => item.ActualArrivalTimeSeconds is not null), $"{label} Engine 時刻表無實際到站。");
        Require(intervals.AllIntervals.Count > 0 && intervals.JourneyStatistics.Count > 0,
            $"{label} Engine 區間／全程統計無資料。");
        Require(v1v2.Stations.Count > 0, $"{label} Engine V1/V2 無資料。");
        Require(resource.Resources.Count > 0, $"{label} Engine 資源占用無資料。");

        if (label == "complete")
        {
            var passEvent = world.Events.FirstOrDefault(item => item.ServiceRunId == "EXPRESS-DOWN"
                && item.EventType == SimulationEventType.StationPassed)
                ?? throw new InvalidOperationException("完整範例快速車必須產生 StationPassed 事件。");
            var passRow = timetable.Single(item => item.ServiceRunId == "EXPRESS-DOWN"
                && item.StationId == "M");
            Require(passRow.ActualArrivalTimeSeconds is { } passTime
                && Math.Abs(passTime - passEvent.SimulationTimeSeconds) < 0.001,
                "StationPassed 通過時刻不可因 ExpectedDestinationPlatformId 為空而消失。");
            Require(intervals.AllIntervals.Any(item => item.ServiceRunId == "EXPRESS-DOWN"
                && item.FromStationId == "W" && item.ToStationId == "M"
                && item.IsComplete && item.IsScheduledStop == false),
                "快速跨站車的 through interval 不可因 StationPassed 比對失敗而消失。");
        }

        var csvChecks = new (string Name, string Content, string Marker)[]
        {
            ("timetable.csv", BuildTimetableCsv(timetable), "車次 ID"),
            ("interval.csv", IntervalStatistics.BuildCsv(intervals, 0), "車輛ID"),
            ("journey.csv", IntervalStatistics.BuildJourneyCsv(intervals, 0), "起終站平均速度"),
            ("summary.csv", IntervalStatistics.BuildSummaryCsv(intervals), "第95百分位"),
            ("resource.csv", ResourceOccupancyAnalysis.BuildCsv(resource), "資源 ID"),
            ("v1-v2.csv", v1v2.BuildCsv(), "理論到站"),
            ("trajectory.csv", TrajectoryAnalysis.BuildCsv(world.Trajectory, world.Events, 0), "track_id")
        };
        foreach (var check in csvChecks)
        {
            Require(check.Content.Contains(check.Marker, StringComparison.Ordinal),
                $"{label} {check.Name} 缺少欄位 {check.Marker}。");
            var path = Path.Combine(output, label + "-" + check.Name);
            File.WriteAllText(path, check.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Require(new FileInfo(path).Length > 20, $"{label} {check.Name} 匯出內容過短。");
        }

        VerifyDiagramExports(window, output, label);
    }

    private static void VerifyIntervalDirectionFilter(MainWindow window, TrainDirection direction, string label, string scenario)
    {
        var filter = (ComboBox)window.FindName("IntervalDirectionComboBox")!;
        var item = filter.Items.Cast<ComboBoxItem>().SingleOrDefault(value =>
            string.Equals(value.Tag?.ToString(), direction.ToString(), StringComparison.OrdinalIgnoreCase));
        Require(item is not null, $"{scenario} 缺少 {label} 區間方向選項。");
        filter.SelectedItem = item;
        Invoke(window, "PopulateIntervalStatistics", false);
        Require(window.IntervalStatisticRows.Count > 0
            && window.IntervalStatisticRows.All(row => row.Direction == label),
            $"{scenario} 區間統計方向篩選混入其他方向。");
    }

    private static void VerifyTrainCenterStationEvents(
        (MainWindow Window, SimulationSession Session, TopologyProjectDocument Document) scenario,
        string serviceRunId)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(scenario.Document);
        var world = scenario.Session.ActualWorld;
        var context = world.GetTopologyResultContext();
        var allTimetable = OperationsTimetable.Build(context, runtime.DispatchPlan, scenario.Session.PlannedEvents, world.Events);
        var timetable = allTimetable
            .Where(item => item.ServiceRunId.Equals(serviceRunId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Require(timetable.Length > 0, $"TrainCenter 測試找不到 {serviceRunId} 時刻表列。");
        Require(timetable.Any(item => item.ActualArrivalTimeSeconds is not null),
            $"TrainCenter 的 {serviceRunId} 到站事件不可因車頭／中心里程差異消失。");
        Require(timetable.Any(item => item.ActualDepartureTimeSeconds is not null),
            $"TrainCenter 的 {serviceRunId} 出站事件不可因車頭／中心里程差異消失。");

        var continuedOrigin = allTimetable.FirstOrDefault(item => item.ServiceRunId == "DOWN-1"
            && item.StationId == "A");
        Require(continuedOrigin?.ActualArrivalTimeSeconds is not null
            && continuedOrigin.ActualDepartureTimeSeconds is not null,
            "尾軌折返接續後的第一個車站必須正常產生到站與出站事件。");

        var intervals = IntervalStatistics.Analyze(context, world.Trajectory, world.Events)
            .AllIntervals.Where(item => item.ServiceRunId.Equals(serviceRunId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Require(intervals.Any(item => item.IsComplete),
            $"TrainCenter 的 {serviceRunId} 完成區間不可因事件比對失敗而消失。");
    }

    private static void VerifyDiagramExports(MainWindow window, string output, string label)
    {
        var canvas = (Canvas)window.FindName("TimeDistanceCanvas")!;
        canvas.Width = 1200;
        canvas.Height = 380;
        canvas.Measure(new Size(1200, 380));
        canvas.Arrange(new Rect(0, 0, 1200, 380));
        canvas.UpdateLayout();
        Invoke(window, "DrawTimeDistanceDiagram");
        canvas.UpdateLayout();
        Require(canvas.Children.Count > 0, $"{label}時間里程圖不可空白。");

        var exportType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.DiagramExportService")!;
        var png = Path.Combine(output, label + "-diagram.png");
        var pdf = Path.Combine(output, label + "-diagram.pdf");
        exportType.GetMethod("ExportPng")!.Invoke(null, [canvas, png, 1d]);
        var pdfEnum = exportType.Assembly.GetType("MrtRouteSimulator.App.PdfPageSize")!;
        var a4 = Enum.Parse(pdfEnum, "A4");
        exportType.GetMethod("ExportPdf")!.Invoke(null, [canvas, pdf, a4, true]);
        var pngBytes = File.ReadAllBytes(png);
        var pdfBytes = File.ReadAllBytes(pdf);
        Require(pngBytes.Length > 100 && pngBytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            $"{label} PNG 匯出格式無效。");
        Require(pdfBytes.Length > 100 && Encoding.ASCII.GetString(pdfBytes, 0, 8) == "%PDF-1.4",
            $"{label} PDF 匯出格式無效。");
    }

    private static string BuildTimetableCsv(IReadOnlyList<OperationsTimetableEntry> entries)
    {
        var lines = new List<string> { "車次 ID,方向,站號,車站,實際到站,實際出站" };
        lines.AddRange(entries.Select(item => string.Join(',',
            item.ServiceRunId,
            item.Direction == TrainDirection.Outbound ? "下行" : "上行",
            item.StationId,
            item.StationName,
            item.ActualArrivalTimeSeconds?.ToString("0.###") ?? string.Empty,
            item.ActualDepartureTimeSeconds?.ToString("0.###") ?? string.Empty)));
        return string.Join(Environment.NewLine, lines);
    }

    private static object? Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
