using System.IO;
using System.Reflection;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class ProjectLoadTests
{
    public static void Run(string root)
    {
        var window = new MainWindow();
        var setCurrentProjectFile = typeof(MainWindow).GetMethod("SetCurrentProjectFile", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            var displayedPath = Path.GetFullPath(Path.Combine(root, "samples", "目前專案.mrtsim.json"));
            setCurrentProjectFile.Invoke(window, [displayedPath]);
            var currentProjectFileText = (TextBlock)window.FindName("CurrentProjectFileTextBlock");
            if (currentProjectFileText.Text != "目前存檔：目前專案.mrtsim.json"
                || !Equals(currentProjectFileText.ToolTip, displayedPath)
                || !window.Title.EndsWith(" — 目前專案.mrtsim.json", StringComparison.Ordinal))
                throw new InvalidOperationException("目前存檔必須在頁首與視窗標題顯示檔名，並以提示顯示完整路徑。");
            setCurrentProjectFile.Invoke(window, [null]);
            if (!currentProjectFileText.Text.Contains("尚未讀取", StringComparison.Ordinal)
                || window.Title.Contains("目前專案.mrtsim.json", StringComparison.Ordinal))
                throw new InvalidOperationException("載入示範資料後不可繼續顯示舊存檔。");
            Console.WriteLine("[通過] 頁首與視窗標題顯示目前存檔，示範資料會清除舊檔名");

            var paths = Directory.GetFiles(Path.Combine(root, "samples"), "*.mrtsim.json", SearchOption.AllDirectories);
            if (paths.Length == 0) throw new InvalidOperationException("未找到範例，不能視為通過。");
            foreach (var path in paths)
            {
                var document = TopologyProjectFormat.Deserialize(File.ReadAllText(path));
                WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
                if (!ReferenceEquals(Field(window, "_activeTopologyProjectDocument"), document))
                    throw new InvalidOperationException("載入後專案不符。");
                Console.WriteLine($"[通過] WPF 完整載入／計畫時間軸／雙向預覽：{Path.GetFileName(path)}");
            }
            var before = Field(window, "_activeTopologyProjectDocument");
            var worker = Field(window, "_playbackWorker");
            setCurrentProjectFile.Invoke(window, [displayedPath]);
            var beforeTitle = window.Title;
            var beforeFileText = currentProjectFileText.Text;
            var invalid = ((TopologyProjectDocument)before!) with { ProjectId = "" };
            try
            {
                WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", invalid, true));
                throw new InvalidOperationException("無效專案未拒絕。");
            }
            catch (SimulationValidationException) { }
            if (!ReferenceEquals(before, Field(window, "_activeTopologyProjectDocument"))
                || !ReferenceEquals(worker, Field(window, "_playbackWorker")))
                throw new InvalidOperationException("失敗載入污染原專案或播放工作者。");
            Console.WriteLine("[通過] WPF 無效專案載入保留原專案及播放工作者");
            var pocket = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
            var editorType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.TrackEdgeEditorViewModel")!;
            foreach (var edge in pocket.Topology.Edges)
            {
                var editor = Activator.CreateInstance(editorType, [edge])!;
                var restored = (TrackEdgeDefinition)editorType.GetMethod("ToDomain")!.Invoke(editor, null)!;
                if (restored.FromPortSide != edge.FromPortSide || restored.ToPortSide != edge.ToPortSide)
                    throw new InvalidOperationException("軌道編輯器套用後遺失實體接軌側別。");
            }
            Console.WriteLine("[通過] WPF 軌道編輯器往返保存實體接軌側別");
            var invalidTurn = pocket with { Topology = pocket.Topology with
            {
                DirectedConnections = pocket.Topology.DirectedConnections.Append(
                    new DirectedTrackConnectionDefinition("PDIN", TraversalDirection.Forward,
                        "PUOUT", TraversalDirection.Forward)).ToArray()
            } };
            try
            {
                WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", invalidTurn, true));
                throw new InvalidOperationException("不合理道岔回頭轉向未在載入替換前拒絕。");
            }
            catch (SimulationValidationException) { }
            if (!ReferenceEquals(before, Field(window, "_activeTopologyProjectDocument"))
                || !ReferenceEquals(worker, Field(window, "_playbackWorker"))
                || window.Title != beforeTitle || currentProjectFileText.Text != beforeFileText
                || !Equals(currentProjectFileText.ToolTip, displayedPath))
                throw new InvalidOperationException("不合理道岔轉向載入污染原專案或模擬會話。");
            Console.WriteLine("[通過] WPF 不合理道岔轉向拒絕載入且保留既有播放工作者");
        }
        finally { WpfTestWait.Close(window); }
    }

    private static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);
}
