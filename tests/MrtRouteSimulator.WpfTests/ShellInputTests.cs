using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class ShellInputTests
{
    private static readonly Type EditorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
    private static readonly Type PageType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;

    public static void Run(string root)
    {
        var source = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        VerifySettingsNavigationAndCancellation(source);
        VerifyUnmodifiedRoundTrip(source);
        Console.WriteLine("[通過] 工作區跨頁保留、無效輸入攔截、速度原值與取消隔離");
    }

    private static void VerifyUnmodifiedRoundTrip(TopologyProjectDocument source)
    {
        using var editor = Open(source, "Project");
        foreach (var page in new[] { "Stations", "Tracks", "Vehicle", "Services", "StopPatterns",
            "DispatchPlanning", "Simulation", "AdvancedData", "Validation", "Project" })
            Navigate(editor.Window, page);
        Invoke(editor.Window, "CommitPageDrafts");
        var expected = JsonNode.Parse(TopologyProjectFormat.Serialize(source));
        var actual = JsonNode.Parse(TopologyProjectFormat.Serialize(Draft(editor.Window)));
        Require(JsonNode.DeepEquals(expected, actual), "大型 Schema 8 專案逐頁檢視且未編輯後，完整 JSON 不可漂移。");
        TopologyProjectFormat.CreateRuntime(Draft(editor.Window));
    }

    private static void VerifySettingsNavigationAndCancellation(TopologyProjectDocument source)
    {
        var before = JsonNode.Parse(TopologyProjectFormat.Serialize(source));
        using var editor = Open(source, "Simulation");
        var window = editor.Window;
        var originalTrainSpeed = source.Train.MaxSpeedMetersPerSecond;
        var originalApproachSpeed = source.Operations.ApproachSpeedMetersPerSecond;

        var clock = Find<TextBox>(window, "起始時鐘（秒）");
        clock.Text = "bad-input";
        Invoke(window, "Navigate", Enum.Parse(PageType, "QuickBuilder"));
        window.UpdateLayout();
        Require(CurrentPage(window) == "Simulation", "快速建立導覽遇到無效設定時應留在原頁。");
        Require(Find<TextBox>(window, "起始時鐘（秒）").Text == "bad-input", "導覽攔截不可清除無效原文。");

        clock.Text = "1234.5";
        Navigate(window, "Vehicle");
        Require(CurrentPage(window) == "Vehicle", "修正欄位後應可離開模擬設定。");
        Require(Draft(window).Simulation.StartClockSeconds == 1234.5, "切頁前須提交設定表單。");
        Require(Draft(window).Train.MaxSpeedMetersPerSecond == originalTrainSpeed
            && Draft(window).Operations.ApproachSpeedMetersPerSecond == originalApproachSpeed,
            "未編輯 km/h 顯示值不得經乘除換算造成原始 m/s 漂移。");

        Navigate(window, "Simulation");
        Require(Find<TextBox>(window, "起始時鐘（秒）").Text == "1234.5", "返回設定頁應讀同一份最新草稿。");
        Find<TextBox>(window, "起始時鐘（秒）").Text = "2345.6";
        Navigate(window, "Stations");
        Require(Draft(window).Simulation.StartClockSeconds == 2345.6, "A→B→A 再修改後，不可由舊表單 hook 回退。");

        var search = Find<TextBox>(window, "搜尋工作區頁面");
        search.Text = "班表";
        Navigate(window, "Simulation");
        Require(CurrentPage(window) == "Simulation" && search.Text.Length == 0,
            "程式導覽須清除搜尋篩選並正確顯示目標頁。");
        var toggle = Descendants(window).OfType<Button>().Single(item => Equals(item.Content, "收合"));
        window.Width = 980;
        window.UpdateLayout();
        Require(Equals(toggle.Content, "展開"), "窄窗應自動收合右側摘要，保留中央編輯空間。");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Equals(toggle.Content, "收合"), "窄窗仍須可由使用者手動展開右側摘要。");
        window.Width = 1360;
        window.UpdateLayout();
        Require(Equals(toggle.Content, "收合"), "使用者手動設定不應被後續視窗尺寸覆蓋。");
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Equals(toggle.Content, "展開") && toggle.IsVisible, "右側收合後展開控制仍須可見。");

        window.Close();
        Require(EditorType.GetProperty("Result")!.GetValue(window) is null, "取消工作區不得產生套用結果。");
        Require(JsonNode.DeepEquals(before, JsonNode.Parse(TopologyProjectFormat.Serialize(source))),
            "取消工作區不可污染原始專案物件。");
    }

    private static EditorScope Open(TopologyProjectDocument source, string page)
    {
        var window = (Window)Activator.CreateInstance(EditorType, source, Enum.Parse(PageType, page))!;
        window.Left = -10000;
        window.Top = -10000;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.ShowInTaskbar = false;
        window.Show();
        window.UpdateLayout();
        return new EditorScope(window);
    }

    private static void Navigate(Window window, string page)
    {
        Invoke(window, "Navigate", Enum.Parse(PageType, page));
        window.UpdateLayout();
        var messages = (System.Collections.IEnumerable)EditorType.GetField("validationMessages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var details = string.Join(" | ", messages.Cast<object>().Select(item => item.GetType().GetProperty("DisplayMessage")?.GetValue(item)?.ToString()));
        Require(CurrentPage(window) == page, $"導覽至 {page} 失敗；實際頁面為 {CurrentPage(window)}。{details}");
    }

    private static string CurrentPage(Window window) =>
        EditorType.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!.ToString()!;

    private static TopologyProjectDocument Draft(Window window)
    {
        var state = EditorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
    }

    private static object? Invoke(Window window, string method, params object[] args) =>
        EditorType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);

    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(item => AutomationProperties.GetName(item) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class EditorScope(Window window) : IDisposable
    {
        public Window Window { get; } = window;
        public void Dispose() { if (Window.IsVisible) Window.Close(); }
    }
}
