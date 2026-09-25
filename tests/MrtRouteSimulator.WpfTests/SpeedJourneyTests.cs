using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class SpeedJourneyTests
{
    public static void Run(string root)
    {
        var window = new MainWindow();
        try
        {
            var path = Path.Combine(root, "samples", "V4.0.0-完整拓撲執行驗證範例.mrtsim.json");
            var document = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            Console.WriteLine("  長行程測試專案載入完成");
            // Schema 8 loads now prepare the complete planned timeline inside the
            // isolated candidate session before the worker is swapped in.  The
            // legacy background-task field is therefore legitimately null here;
            // keep the diagnostic compatible with both preparation paths.
            var plannedTask = Field(window, "_plannedTimelineTask") as Task;
            var plannedDuration = (double)Field(window, "_playbackDurationSeconds")!;
            Console.WriteLine($"  計畫範圍 {plannedDuration:0.0} 秒；背景工作狀態 {plannedTask?.Status.ToString() ?? "候選準備已完成"}");
            if (plannedTask is not null)
            {
                _ = plannedTask.ContinueWith(task => Console.WriteLine($"  計畫背景工作結束：{task.Status}"), TaskScheduler.Default);
            }
            WpfTestWait.WaitForPlannedTimeline(window);
            Console.WriteLine("  計畫時間軸完成");
            var worker = (SimulationPlaybackWorker)Field(window, "_playbackWorker")!;
            var plan = (PlannedTimelineArtifact)Field(window, "_plannedTimelineArtifact")!;
            var selector = (ComboBox)window.FindName("SpeedProfileRunComboBox");
            selector.SelectionChanged += (_, _) => Invoke(window, "DrawV2SpeedProfile");
            var source = (TextBlock)window.FindName("SpeedProfileSourceText");
            var canvas = new Canvas { Background = Brushes.White };
            typeof(MainWindow).GetField("SpeedCanvas", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .SetValue(window, canvas);
            Require(selector.Items.Contains("TAIL-01"), "完整行程選單必須使用實體列車 ID。");
            Require(window.FindName("InboundSpeedCanvas") is null, "不應保留上下行兩張速度圖。");
            selector.SelectedItem = "TAIL-01";
            Layout(canvas, 720);
            Invoke(window, "DrawV2SpeedProfile");
            Require(source.Text.StartsWith("計畫預覽"), $"未播放時應顯示同會話的計畫預覽：{source.Text}, {canvas.ActualWidth}x{canvas.ActualHeight}, {selector.SelectedItem}。");
            CheckJourney(canvas, plan.Trajectory.Where(s => s.VehicleId == "TAIL-01").ToArray());
            var planCount = plan.Trajectory.Length;
            WpfTestWait.Advance(window, 1201);
            Console.WriteLine("  實際推進至 1201 秒完成");
            Invoke(window, "DrawV2SpeedProfile");
            Require(source.Text == "實際（截至目前）", "剛發車的實際資料不可標示為已完成全程。");
            WpfTestWait.Advance(window, 3600);
            Console.WriteLine("  實際推進至 3600 秒完成");
            var frame = WpfTestWait.LatestFrame(window);
            Require(frame.Trajectory.Where(s => s.VehicleId == "TAIL-01")
                .Select(s => s.ServiceRunId).Distinct().Count() >= 2, "情境必須包含同車折返接續。");
            foreach (var width in new[] { 720, 1200 })
            {
                Layout(canvas, width);
                Invoke(window, "DrawV2SpeedProfile");
                Require(source.Text == "V2 實際", "播放後應使用實際軌跡。");
                CheckJourney(canvas, frame.Trajectory.Where(s => s.VehicleId == "TAIL-01").ToArray());
                Save(canvas, Path.Combine(root, "artifacts", "output-qa", $"tail-complete-speed-{width}.png"));
            }
            foreach (var id in selector.Items.Cast<string>().ToArray())
            {
                selector.SelectedItem = id;
                Invoke(window, "DrawV2SpeedProfile");
                Require(canvas.Children.OfType<Polyline>().Count() == 2, $"列車 {id} 的速度與速限線缺失。");
            }
            var directions = (ComboBox)window.FindName("SafetyDirectionComboBox");
            ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem = window.FindName("SafetyTabItem");
            var statuses = (ComboBox)window.FindName("SafetyStatusComboBox");
            statuses.SelectedIndex = 0;
            var observation = frame.SafetyHistory.First();
            directions.SelectedIndex = 0;
            Invoke(window, "UpdateV2PlaybackView", true);
            Require(((TextBlock)window.FindName("OneWaySummaryText")).Text.Contains("V2 實際平均"),
                "已完成實際車次不可顯示尚無完成行程。");
            Require(((TextBlock)window.FindName("CycleSummaryText")).Text.Contains("已完成列車平均"),
                "全程摘要應顯示列車從發車到退出的經過時間。");
            var pairs = (ComboBox)window.FindName("SafetyPairComboBox");
            Require(pairs.Items.Count > 0, "列車退出後仍須保留歷史閉塞配對。");
            Require(((TextBlock)window.FindName("SafetySummaryText")).Text.Contains("最低安全裕度"),
                "即時列車退出不應讓歷史安全摘要變成空白。");
            var safetyCanvas = new Canvas { Width = 720, Height = 260, Background = Brushes.White };
            typeof(MainWindow).GetField("SafetyDistanceCanvas", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
                .SetValue(window, safetyCanvas);
            Layout(safetyCanvas, 720);
            ((ComboBox)window.FindName("SafetyWindowComboBox")).SelectedIndex = 2;
            Invoke(window, "DrawSafetyDistanceChart");
            Require(safetyCanvas.Children.OfType<Polyline>().Count() == 3, "全部時間應可檢視已退出列車的歷史閉塞曲線。");
            Save(safetyCanvas, Path.Combine(root, "artifacts", "output-qa", "historical-safety.png"));
            foreach (var direction in new[] { TrainDirection.Outbound, TrainDirection.Inbound })
            {
                directions.SelectedItem = directions.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, direction.ToString()));
                Require((bool)Invoke(window, "MatchesSafetyFilters", observation with { Direction = direction })!, "閉塞同方向必須通過篩選。");
                Require(!(bool)Invoke(window, "MatchesSafetyFilters", observation with
                { Direction = direction == TrainDirection.Outbound ? TrainDirection.Inbound : TrainDirection.Outbound })!, "閉塞另一方向必須排除。");
                Invoke(window, "UpdateV2PlaybackView", true);
                Require(pairs.Items.Cast<string>().All(key => key.Contains(direction == TrainDirection.Outbound ? "下行" : "上行")),
                    "歷史配對選單不可混入反向資料。");
            }
            selector.SelectedItem = "TAIL-01";
            WpfTestWait.Wait(worker.ResetAsync());
            Invoke(window, "UpdateV2PlaybackView", true);
            Invoke(window, "DrawV2SpeedProfile");
            Require(source.Text.StartsWith("計畫預覽") && plan.Trajectory.Length == planCount,
                "重設必須保留計畫預覽且不混入上一輪實際軌跡。");
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", StationLayoutTemplateService.Build(StationLayoutTemplateKind.SideTwoTracks), true));
            Console.WriteLine("  換檔驗證完成");
            Require(!selector.Items.Contains("TAIL-01"), "換檔不可殘留上一專案列車。");
            Require(selector.Items.Cast<string>().Distinct().Count() == selector.Items.Count,
                "選車事件重入時不可重複加入列車。");
            Console.WriteLine("[通過] 全列車速度圖：計畫／實際、折返雙向接續、兩寬度、重設／換檔及閉塞方向獨立篩選");
        }
        finally
        {
            Console.WriteLine("  長行程 worker cleanup 開始");
            WpfTestWait.Close(window);
            Console.WriteLine("  長行程 worker cleanup 完成");
        }
    }

    private static void CheckJourney(Canvas canvas, TrajectorySample[] samples)
    {
        Require(samples.Select(s => s.Direction).Distinct().Count() == 2, "必須包含上下行完整行程。");
        var speed = canvas.Children.OfType<Polyline>().Last();
        Require(speed.Points.Count >= 4 && Math.Abs(speed.Points[0].X - 42) < .01
            && Math.Abs(speed.Points[^1].X - (canvas.ActualWidth - 15)) < .01, "速度曲線必須覆蓋首筆到末筆軌跡。");
        var changes = samples.Zip(samples.Skip(1)).Count(pair => pair.First.Direction != pair.Second.Direction);
        Require(canvas.Children.OfType<Line>().Count(l => l.ToolTip?.ToString()?.Contains("換向為") == true) == changes,
            "每次換向均應顯示標記。");
        Require(speed.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)
            && p.Y >= 0 && p.Y <= canvas.ActualHeight), "速度點不可超出畫布。");
    }

    private static void Layout(Canvas canvas, double width)
    {
        canvas.Width = width;
        canvas.Height = 260;
        canvas.Measure(new Size(width, 260));
        canvas.Arrange(new Rect(0, 0, width, 260));
    }

    private static void Save(Canvas canvas, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        canvas.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        png.Save(stream);
    }
    private static object? Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
