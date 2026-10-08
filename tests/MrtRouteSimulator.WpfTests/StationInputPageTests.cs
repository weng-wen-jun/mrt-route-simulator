using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>車站中心頁的資料來源、引用篩選、導覽提交與無效輸入保留檢核。</summary>
internal static class StationInputPageTests
{
    public static void Run(string root)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        VerifyWholeJsonRoundTrip(sample);

        var assembly = typeof(MainWindow).Assembly;
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var editor = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, "Stations"))!;
        editor.Left = -10000;
        editor.Top = -10000;
        editor.WindowStartupLocation = WindowStartupLocation.Manual;
        editor.ShowInTaskbar = false;
        editor.Show();
        try
        {
            VerifyStationReferenceViews(editor, editorType, sample);
            VerifyO20TurnbackDwellRoundTrip(editor, editorType);
            VerifyInvalidNavigationPreservesDraft(editor, editorType, pageType);
            VerifyStationTrackAndAdvancedEntryPoints(editor, editorType);
            Console.WriteLine("[通過] 車站中心頁可依同一 VM 篩選 O20/O04/O13，保留無效草稿，O20 折返停留可存讀，軌道與進階頁具備增刪入口");
        }
        finally { editor.Close(); }
    }

    private static void VerifyWholeJsonRoundTrip(TopologyProjectDocument sample)
    {
        var encoded = TopologyProjectFormat.Serialize(sample);
        var reencoded = TopologyProjectFormat.Serialize(TopologyProjectFormat.Deserialize(encoded));
        Require(JsonNode.DeepEquals(JsonNode.Parse(encoded), JsonNode.Parse(reencoded)),
            "完整 Schema 8 JSON 序列化／反序列化後不得遺漏或漂移欄位。");
    }

    private static void VerifyStationReferenceViews(Window editor, Type editorType, TopologyProjectDocument sample)
    {
        var infrastructure = editorType.GetField("infrastructure", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var stations = (IList)infrastructure.GetType().GetProperty("Stations")!.GetValue(infrastructure)!;
        var list = (ListBox)editorType.GetField("stationPageStationList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var stationOperations = (IList)editorType.GetField("stationOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var turnbackOperations = (IList)editorType.GetField("turnbackOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var passingOperations = (IList)editorType.GetField("passingOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;

        var o20 = stations.Cast<object>().Single(item => (string)Get(item, "Id")! == "O20");
        list.SelectedItem = o20;
        editor.UpdateLayout();
        var turnbacks = (IEnumerable)editorType.GetField("stationPageTurnbacksView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var passings = (IEnumerable)editorType.GetField("stationPagePassingsView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var o20Turnback = turnbacks.Cast<object>().Single();
        Require(ReferenceEquals(o20Turnback, turnbackOperations.Cast<object>().Single(item => (string)Get(item, "Id")! == "TURNBACK-001")),
            "O20 折返清單必須直接引用原始折返 VM。");
        Require(stationOperations.Cast<object>().Any(item => (string)Get(item, "Station")! == "O20"),
            "O20 車站作業必須來自原始 station operation VM。");
        Require(!passings.Cast<object>().Any(), "O20 不應顯示 O04/O13 的待避作業。");

        var o04 = stations.Cast<object>().Single(item => (string)Get(item, "Id")! == "O04");
        list.SelectedItem = o04;
        editor.UpdateLayout();
        var o04Passings = (IEnumerable)editorType.GetField("stationPagePassingsView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var o04Ids = o04Passings.Cast<object>().Select(item => (string)Get(item, "Id")!).ToArray();
        Require(o04Ids.Contains("PASSING-001") && o04Ids.Contains("PASSING:O04:DOWN:SECTION"),
            "O04 待避頁面必須依 station operation 引用顯示兩筆同一 VM。");
        Require(o04Passings.Cast<object>().All(passingOperations.Contains),
            "O04 待避清單不可使用複製的 operation row。");

        var o13 = stations.Cast<object>().Single(item => (string)Get(item, "Id")! == "O13");
        list.SelectedItem = o13;
        editor.UpdateLayout();
        var o13Passings = (IEnumerable)editorType.GetField("stationPagePassingsView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var actualO13PassingIds = o13Passings.Cast<object>().Select(item => (string)Get(item, "Id")!).ToArray();
        var expectedO13PassingIds = sample.Topology.StationOperations
            .Where(item => item.StationId == "O13")
            .SelectMany(item => item.PassingOperationIds)
            .ToArray();
        Require(actualO13PassingIds.Length == expectedO13PassingIds.Length
            && actualO13PassingIds.All(id => expectedO13PassingIds.Contains(id, StringComparer.OrdinalIgnoreCase)),
            "O13 待避頁面必須精確顯示本站所有 station operation 引用。");

        Invoke(editorType, "CommitTableDrafts", editor);
        var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
        Require(draft.Topology.StationOperations.Count == sample.Topology.StationOperations.Count,
            "只切換車站不得改變 station operation 筆數。");
        Require(draft.Topology.TurnbackOperations.Count == sample.Topology.TurnbackOperations.Count
            && draft.Topology.PassingOperations.Count == sample.Topology.PassingOperations.Count,
            "只切換車站不得改變折返／待避 operation 筆數。");
    }

    private static void VerifyO20TurnbackDwellRoundTrip(Window editor, Type editorType)
    {
        var infrastructure = editorType.GetField("infrastructure", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var stations = (IList)infrastructure.GetType().GetProperty("Stations")!.GetValue(infrastructure)!;
        var list = (ListBox)editorType.GetField("stationPageStationList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var o20 = stations.Cast<object>().Single(item => (string)Get(item, "Id")! == "O20");
        list.SelectedItem = o20;
        editor.UpdateLayout();

        var turnbackList = FindByAutomationName<ListBox>(editor, "本站折返作業清單");
        var turnbackView = (IEnumerable)editorType.GetField("stationPageTurnbacksView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var turnback = turnbackView.Cast<object>().Single(item => (string)Get(item, "Id")! == "TURNBACK-001");
        turnbackList.SelectedItem = turnback;
        editor.UpdateLayout();
        var dwell = FindByAutomationName<TextBox>(editor, "折返作業最短折返停留秒數");
        dwell.Text = "321.5";
        // Exercise the same page hook plus table-to-draft serialization used by navigation/apply.
        Invoke(editorType, "CommitPageDrafts", editor);

        var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
        var changed = draft.Topology.TurnbackOperations.Single(item => item.OperationId == "TURNBACK-001").MinimumDwellTimeSeconds;
        Require(Math.Abs(changed - 321.5) < 0.000001, "O20 折返作業停留秒數必須由頁面寫入同一份 draft VM。");
        var reloaded = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(draft));
        var reloadedDwell = reloaded.Topology.TurnbackOperations.Single(item => item.OperationId == "TURNBACK-001").MinimumDwellTimeSeconds;
        Require(Math.Abs(reloadedDwell - 321.5) < 0.000001, "O20 折返作業停留秒數經 Schema 8 存檔／讀回不得漂移。");
    }

    private static void VerifyInvalidNavigationPreservesDraft(Window editor, Type editorType, Type pageType)
    {
        var dwell = FindByAutomationName<TextBox>(editor, "本站預設停站秒數");
        dwell.Text = "not-a-number";
        Invoke(editorType, "Navigate", editor, Enum.Parse(pageType, "Vehicle"));
        var currentPage = editorType.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!.ToString();
        Require(currentPage == "Stations", "無效車站停站秒數導覽時必須留在車站頁。");
        var validationMessages = (IEnumerable)editorType.GetField("validationMessages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Require(validationMessages.Cast<object>().Any(item => item.GetType().GetProperty("DisplayMessage")?.GetValue(item)?.ToString()?.Contains("有限數值", StringComparison.Ordinal) == true),
            "無效導覽被攔截時必須留下對應的數值驗證訊息。");
        Require(FindByAutomationName<TextBox>(editor, "本站預設停站秒數").Text == "not-a-number",
            "無效停站秒數遭導覽攔截後必須保留原始文字。");

        dwell = FindByAutomationName<TextBox>(editor, "本站預設停站秒數");
        dwell.Text = "12.5";
        Invoke(editorType, "Navigate", editor, Enum.Parse(pageType, "Vehicle"));
        currentPage = editorType.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!.ToString();
        Require(currentPage == "Vehicle", "修正無效輸入後導覽必須可以繼續。");

        var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
        Require(Math.Abs(draft.Topology.Stations.Single(item => item.StationId == "O20").DefaultDwellTimeSeconds - 12.5) < 0.000001,
            "修正後的 O13 停站秒數必須寫入 draft。");
        Invoke(editorType, "Navigate", editor, Enum.Parse(pageType, "Stations"));
        editor.UpdateLayout();
        currentPage = editorType.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!.ToString();
        Require(currentPage == "Stations", "修正後返回導覽必須回到車站頁。");
        var currentList = (ListBox)editorType.GetField("stationPageStationList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var detail = (StackPanel)editorType.GetField("stationPageDetail", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var names = string.Join(",", Descendants(editor).OfType<TextBox>().Select(AutomationProperties.GetName));
        Require(Descendants(editor).OfType<TextBox>().Any(box => AutomationProperties.GetName(box) == "本站預設停站秒數"),
            $"返回車站頁未建立基本欄位：selected={currentList.SelectedItem}，detailChildren={detail.Children.Count}，textBoxes={names}");
        Require(FindByAutomationName<TextBox>(editor, "本站預設停站秒數").Text == "12.5",
            "修正後返回車站頁必須顯示同一份 VM 值。");
    }

    private static void VerifyStationTrackAndAdvancedEntryPoints(Window editor, Type editorType)
    {
        Invoke(editorType, "ShowStationsPage", editor);
        editor.UpdateLayout();
        foreach (var name in new[]
        {
            "新增車站", "刪除選取車站", "新增本站月台", "刪除選取月台",
            "新增本站作業", "刪除選取本站作業", "新增本站折返作業", "刪除選取折返作業",
            "新增本站待避作業", "刪除選取待避作業"
        })
            Require(FindByAutomationName<Button>(editor, name).IsEnabled, $"車站頁缺少可操作的「{name}」入口。");

        Invoke(editorType, "ShowTracksPage", editor);
        editor.UpdateLayout();
        Require(FindNamed<DataGrid>(editor, "TrackEdgeGrid") is not null, "軌道頁必須保留軌道區段資料表。");
        Require(FindByAutomationName<Button>(editor, "新增軌道區段").IsEnabled
            && FindByAutomationName<Button>(editor, "刪除軌道區段").IsEnabled,
            "軌道區段頁必須保留實際新增／刪除入口。");
        Require(FindByAutomationName<Button>(editor, "新增設施").IsEnabled
            && FindByAutomationName<Button>(editor, "刪除設施").IsEnabled,
            "設施頁必須保留實際新增／刪除入口。");

        Invoke(editorType, "ShowAdvancedDataPage", editor);
        editor.UpdateLayout();
        Require(FindNamed<DataGrid>(editor, "AdvancedNodeGrid") is not null, "進階頁必須保留節點資料入口。");
        Require(FindNamed<DataGrid>(editor, "DirectedConnectionGrid") is not null, "進階頁必須保留有向接續資料入口。");
        Require(FindNamed<DataGrid>(editor, "ConflictResourceGrid") is not null, "進階頁必須保留衝突資源資料入口。");
        Require(FindNamed<DataGrid>(editor, "TrackCurveGrid") is not null && FindNamed<DataGrid>(editor, "TrackGradientGrid") is not null,
            "進階頁必須保留曲線與坡度資料入口。");
        Require(FindByAutomationName<Button>(editor, "新增軌道節點").IsEnabled
            && FindByAutomationName<Button>(editor, "刪除軌道節點").IsEnabled,
            "進階節點頁必須保留實際新增／刪除入口。");
    }

    private static T? FindNamed<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants(root).OfType<T>().FirstOrDefault(item => item is FrameworkElement element && element.Name == name);

    private static T FindByAutomationName<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static object? Invoke(Type type, string method, object target, params object[]? arguments) =>
        type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, arguments);

    private static object? Get(object source, string property) => source.GetType().GetProperty(property)!.GetValue(source);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
