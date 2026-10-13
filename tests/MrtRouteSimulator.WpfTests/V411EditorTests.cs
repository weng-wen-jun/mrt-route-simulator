using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// V4.1.1 多營運路線與逐站時刻的 WPF 專項。這是離屏呈現與資料隔離檢查，
/// 不取代原生 Windows DPI、compositor 或滑鼠手感驗收。
/// </summary>
internal static class V411EditorTests
{
    public static void Run(string root)
    {
        var template = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var document = OperatingNetworkBuilder.Build(template,
        [
            Line("MAIN", "主線", 2, null, 0, 4_000, 5),
            Line("BRANCH", "支線", 1, "MAIN", 2_000, 1_800, 3)
        ]);
        var firstManual = document.Dispatch.ManualTimetableRows![0];
        var firstRoute = document.ServiceRoutes.Single(route => route.ServiceRouteId == firstManual.ServiceRouteId);
        var origin = firstRoute.Stops[0].StationId;
        document = document with
        {
            Dispatch = document.Dispatch with
            {
                ManualTimetableRows = document.Dispatch.ManualTimetableRows.Select((row, index) => index == 0
                    ? row with
                    {
                        StationTimetableEntries =
                        [
                            new ProjectStationTimetableEntry(origin, row.PlannedDepartureTimeSeconds, 0)
                        ]
                    }
                    : row).ToArray()
            }
        };
        TopologyProjectFormat.Validate(document);

        var assembly = typeof(MainWindow).Assembly;
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var editor = (Window)Activator.CreateInstance(editorType, document, Enum.Parse(pageType, "OperatingLines"))!;
        editor.Width = 1360;
        editor.Height = 860;
        editor.Left = -10000;
        editor.Top = -10000;
        editor.WindowStartupLocation = WindowStartupLocation.Manual;
        editor.ShowInTaskbar = false;
        editor.Show();
        try
        {
            Pump(editor);
            Require(ReferenceEquals(editor.Background, UiTheme.WindowBackgroundBrush),
                "營運路線頁必須沿用新版 EditorChrome 視窗底色。");
            var count = FindByName<ComboBox>(editor, "營運路線數量");
            Require(count.Items.Cast<int>().SequenceEqual(Enumerable.Range(1, 8)),
                "營運路線數量必須明確限制為 1 至 8。");
            var lineGrid = FindByName<DataGrid>(editor, "營運路線規劃表");
            Require(lineGrid.Items.Count == 2 && lineGrid.Columns.Count == 7,
                "營運路線表必須顯示線數、軌道數、父線、接軌里程、長度與站數。");
            Require(lineGrid.Columns.Any(column => Equals(column.Header, "本線區段長度 km"))
                && lineGrid.Columns.Any(column => Equals(column.Header, "本線站數")),
                "支線幾何欄位必須明示只描述本線新建區段，不含父線共享部分。");
            var firstLine = lineGrid.Items[0]!;
            Require((int)firstLine.GetType().GetProperty("TrackCount")!.GetValue(firstLine)! == 2,
                "主線雙線設定必須載入規劃表。");
            var secondLine = lineGrid.Items[1]!;
            Require((int)secondLine.GetType().GetProperty("TrackCount")!.GetValue(secondLine)! == 1
                && (string)secondLine.GetType().GetProperty("ParentLineId")!.GetValue(secondLine)! == "MAIN",
                "支線單線與父線設定必須載入規劃表。");

            var output = Path.Combine(root, "output", "v411-editor-visuals");
            Directory.CreateDirectory(output);
            Capture(editor, Path.Combine(output, "operating-lines-1360x860.png"));

            // Page rows are detached planning input. Leaving without pressing the
            // explicit rebuild button must not mutate the editor draft.
            firstLine.GetType().GetProperty("Name")!.SetValue(firstLine, "不應寫回");
            Navigate(editorType, pageType, editor, "DispatchPlanning");
            Pump(editor);
            var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            Require(draft.OperatingLines![0].Name == "主線", "未按建立網路起稿時不得污染 draft metadata。");

            var manualGrid = FindByName<DataGrid>(editor, "手動班表常用欄位");
            manualGrid.SelectedIndex = 0;
            Pump(editor);
            var route = FindByName<ComboBox>(editor, "班表營運路徑");
            Require(route.Items.Count >= 2 && route.SelectedItem is not null,
                "手動班表必須提供符合方向的營運路徑選單。");

            var before = TopologyProjectFormat.Serialize(draft).Contains("stationTimetableEntries", StringComparison.Ordinal);
            Require(before, "測試車次必須先含逐站時刻，才能檢查取消隔離。");
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<Window>()
                    .Single(window => window.Owner == editor && AutomationProperties.GetName(window) == "逐站發車時刻與最少停站時間編輯器");
                Require(ReferenceEquals(dialog.Background, UiTheme.WindowBackgroundBrush),
                    "逐站時刻對話框必須套用 EditorChrome 主題。");
                var timetable = FindByName<DataGrid>(dialog, "逐站時刻表");
                Require(timetable.Columns.Any(column => Equals(column.Header, "表定發車時刻"))
                    && timetable.Columns.All(column => !Equals(column.Header, "表定發車秒數")),
                    "逐站時刻的主要編輯欄必須使用 HH:mm:ss 表定發車時刻，而不是累計秒數。");
                var row = timetable.Items[0]!;
                row.GetType().GetProperty("PlannedDepartureClock")!.SetValue(row, "02:46:39");
                Require(Math.Abs((double)row.GetType().GetProperty("PlannedDepartureTimeSeconds")!.GetValue(row)! - 9999d) < 1e-9,
                    "HH:mm:ss 欄位必須正確橋接 Engine 使用的秒數。");
                Capture(dialog, Path.Combine(output, "station-timetable-dialog-700x520.png"));
                FindByName<Button>(dialog, "取消逐站發車時刻")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }), DispatcherPriority.Loaded);
            editorType.GetMethod("EditSelectedStationTimetable", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            editorType.GetMethod("CommitTableDrafts", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            var preserved = draft.Dispatch.ManualTimetableRows![0].StationTimetableEntries![0];
            Require(Math.Abs(preserved.PlannedDepartureTimeSeconds - firstManual.PlannedDepartureTimeSeconds) < 1e-9
                && Math.Abs(preserved.MinimumDwellSeconds) < 1e-9,
                "取消逐站時刻對話框不得改寫發車或起站 0 秒停站資料。");
            Console.WriteLine($"[通過] V4.1.1 編輯器多路線與逐站時刻；visuals={output}");
        }
        finally
        {
            editor.Close();
        }
    }

    private static OperatingLineDefinition Line(
        string id, string name, int tracks, string? parent, double junction, double length, int stations) => new()
    {
        LineId = id,
        Name = name,
        TrackCount = tracks,
        ParentLineId = parent,
        JunctionChainageMeters = junction,
        LengthMeters = length,
        StationCount = stations,
        OutboundServiceRouteId = $"ROUTE:{id}:OUTBOUND",
        InboundServiceRouteId = $"ROUTE:{id}:INBOUND"
    };

    private static void Navigate(Type editorType, Type pageType, Window editor, string page) =>
        editorType.GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(editor, [Enum.Parse(pageType, page)]);

    private static T FindByName<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(item => AutomationProperties.GetName(item) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        window.UpdateLayout();
    }

    private static void Capture(Window window, string path)
    {
        Pump(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine($"v411EditorSnapshot={path}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
