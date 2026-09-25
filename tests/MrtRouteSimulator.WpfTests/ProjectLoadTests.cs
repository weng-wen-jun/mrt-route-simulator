using System.IO;
using System.Reflection;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class ProjectLoadTests
{
    public static void Run(string root)
    {
        var calculateMaximumDuration = typeof(MainWindow).GetMethod(
            "CalculateMaximumCandidateDurationSeconds",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var maximumDuration = (double)calculateMaximumDuration.Invoke(null, [5600d, 4434.87d])!;
        if (Math.Abs(maximumDuration - 11034.87d) > 1e-9)
            throw new InvalidOperationException($"計畫時間軸安全上限公式錯誤：{maximumDuration:0.00} 秒。 ");
        var percentageMarginDuration = (double)calculateMaximumDuration.Invoke(null, [0d, 6000d])!;
        if (Math.Abs(percentageMarginDuration - 7200d) > 1e-9)
            throw new InvalidOperationException($"計畫時間軸百分比安全緩衝錯誤：{percentageMarginDuration:0.00} 秒。 ");
        Console.WriteLine("[通過] 計畫時間軸安全上限使用基準週期 20% 與 1000 秒取大值");

        var window = new MainWindow();
        var progressType = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.ProjectLoadProgressWindow")!;
        var progressWindow = (System.Windows.Window)Activator.CreateInstance(progressType)!;
        try
        {
            progressType.GetMethod("UpdateProgress")!.Invoke(progressWindow, [60d, "正在建立模擬與計畫時間軸…"]);
            if (progressWindow.Title != "正在讀取存檔")
                throw new InvalidOperationException("讀檔進度視窗必須清楚標示目前正在讀取存檔。");
            Console.WriteLine("[通過] 讀檔進度視窗可建立並更新階段文字");
        }
        finally { progressWindow.Close(); }
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
                WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                    window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
                if (!ReferenceEquals(Field(window, "_activeTopologyProjectDocument"), document))
                    throw new InvalidOperationException("載入後專案不符。");
                var engineMode = (ComboBox)window.FindName("EngineModeComboBox")!;
                var engineTag = (engineMode.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                if (!string.Equals(engineTag, SimulationEngineKind.V2RealisticOperations.ToString(), StringComparison.Ordinal))
                    throw new InvalidOperationException("載入 Schema 8 拓撲後，模擬引擎選擇必須同步為 V2 實際營運。");

                var vehicleRows = ((IEnumerable<VehicleTypeInputRow>)typeof(MainWindow)
                    .GetProperty("VehicleTypeRows")!.GetValue(window)!).ToArray();
                var serviceRows = ((IEnumerable<ServiceTypeInputRow>)typeof(MainWindow)
                    .GetProperty("ServiceTypeRows")!.GetValue(window)!).ToArray();
                if (vehicleRows.Length != document.VehicleTypes.Length
                    || serviceRows.Length != document.ServiceTypes.Length
                    || !document.VehicleTypes.All(item => vehicleRows.Any(row => row.Id == item.Id && row.Name == item.DisplayName))
                    || !document.ServiceTypes.All(item => serviceRows.Any(row => row.Id == item.Id && row.Name == item.DisplayName)))
                    throw new InvalidOperationException("載入 Schema 8 拓撲後，車型／服務目錄不得留在示範資料或只顯示內部 ID。");

                var serviceFilter = (ComboBox)window.FindName("IntervalServiceTypeComboBox")!;
                var patternFilter = (ComboBox)window.FindName("IntervalStopPatternComboBox")!;
                var serviceFilterIds = serviceFilter.Items.OfType<CatalogOption>().Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var patternFilterIds = patternFilter.Items.OfType<CatalogOption>().Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!document.ServiceTypes.All(item => serviceFilterIds.Contains(item.Id))
                    || !document.StopPatterns.All(item => patternFilterIds.Contains(item.Id)))
                    throw new InvalidOperationException("載入 Schema 8 拓撲後，區間統計服務／停站模式篩選不得為空或沿用舊專案。");

                var archiveMenu = (MenuItem)window.FindName("ExportFixedTimetableArchiveMenuItem")!;
                if (archiveMenu.IsEnabled)
                    throw new InvalidOperationException("Schema 8 拓撲專案不應啟用只支援 legacy Schema 7 的固定時刻表封存匯出。");
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
                WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                    window, "ConfigureTopologyProjectForPlaybackAsync", invalid, true));
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
                WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                    window, "ConfigureTopologyProjectForPlaybackAsync", invalidTurn, true));
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
