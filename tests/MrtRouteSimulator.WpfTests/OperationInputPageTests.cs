using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// 營運輸入頁的低成本回歸檢查。主 runner 可在 WPF dispatcher 上呼叫 Run，
/// 避免把原生桌面 DPI 驗收誤當成離屏測試。
/// </summary>
internal static class OperationInputPageTests
{
    public static void Run(string root)
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            root, "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var assembly = typeof(MainWindow).Assembly;
        var editorType = assembly.GetType("MrtRouteSimulator.App.TopologyEditorWindow")!;
        var pageType = assembly.GetType("MrtRouteSimulator.App.ProjectWorkspacePage")!;
        var editor = (Window)Activator.CreateInstance(editorType, sample, Enum.Parse(pageType, "Operations"))!;
        editor.Left = -10000; editor.Top = -10000; editor.WindowStartupLocation = WindowStartupLocation.Manual; editor.ShowInTaskbar = false;
        editor.Show();
        try
        {
            var navigate = editorType.GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            navigate.Invoke(editor, [Enum.Parse(pageType, "StopPatterns")]);
            Pump(editor);
            AssertPage(editorType, editor, "StopPatterns");
            var matrix = FindByName<DataGrid>(editor, "停站模式矩陣");
            Require(matrix.Columns.Count == sample.StopPatterns.Length + 1, "停站模式矩陣欄數必須是一個車站欄加全部模式欄。");
            var affected = FindByName<TextBlock>(editor, "停站模式受影響班次");
            Require(affected.Text.Contains("受影響班次", StringComparison.Ordinal), "矩陣 detail 必須提供受影響班次提示。");

            SelectMatrixCell(matrix, "O13", "FULL-LINE-O13-HOLD");
            var wait = FindByName<ComboBox>(editor, "停站模式待避車次");
            Require(wait.Items.OfType<object>().Any(item => item.ToString()?.Contains("EXPRESS-DOWN-01", StringComparison.OrdinalIgnoreCase) == true),
                "停站模式 detail 必須提供完整既有車次引用選單。");
            wait.SelectedItem = wait.Items.OfType<object>().First(item => item.GetType().GetProperty("Id")?.GetValue(item) is null);
            Invoke(editorType, editor, "CommitOperationsPageEditors");
            var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
            var draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            Require(draft.StopPatterns.Single(item => item.Id == "FULL-LINE-O13-HOLD").Instructions.Single(item => item.StationId == "O13").WaitForOvertakeServiceRunId is null,
                "矩陣清空指定待避後必須寫回同一 instruction 的 null 語意。");
            wait.SelectedItem = wait.Items.OfType<object>().First(item => item.GetType().GetProperty("Id")?.GetValue(item)?.ToString() == "EXPRESS-DOWN-01");
            Invoke(editorType, editor, "CommitOperationsPageEditors");
            draft = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            Require(draft.StopPatterns.Single(item => item.Id == "FULL-LINE-O13-HOLD").Instructions.Single(item => item.StationId == "O13").WaitForOvertakeServiceRunId == "EXPRESS-DOWN-01",
                "矩陣 detail 編輯指定待避後必須寫回同一 instruction。");
            var reloaded = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(draft));
            Require(reloaded.StopPatterns.Single(item => item.Id == "FULL-LINE-O13-HOLD").Instructions.Single(item => item.StationId == "O13").WaitForOvertakeServiceRunId == "EXPRESS-DOWN-01",
                "指定待避修改後序列化／重讀不得漂移。");

            var dwell = FindByName<TextBox>(editor, "停站模式停站秒數");
            dwell.Text = "invalid-number";
            navigate.Invoke(editor, [Enum.Parse(pageType, "Vehicle")]);
            Pump(editor);
            var preserved = FindByName<TextBox>(editor, "停站模式停站秒數");
            Require(preserved.Text == "invalid-number", "無效停站秒數切頁後必須保留原始文字。");
            var currentPage = editorType.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!.ToString();
            Require(currentPage == "StopPatterns", "無效數值切頁時必須留在停站模式頁。");
            preserved.Text = "10";

            navigate.Invoke(editor, [Enum.Parse(pageType, "DispatchPlanning")]);
            Pump(editor);
            AssertPage(editorType, editor, "DispatchPlanning");
            var continuation = FindByName<ComboBox>(editor, "班表接續車次");
            Require(continuation.Items.Count >= (sample.Dispatch.ManualTimetableRows?.Length ?? 0), "接續選單不得只顯示目前列，必須包含既有班表車次。");
            var manualGrid = FindByName<DataGrid>(editor, "手動班表常用欄位");
            var firstManual = manualGrid.Items.Cast<object>().First();
            Require(DisplayColumn(manualGrid, firstManual, "方向") == "下行", "班表常用表方向必須顯示中文名稱。");
            Require(DisplayColumn(manualGrid, firstManual, "服務") == sample.ServiceTypes.Single(item => item.Id == "FULL-LINE").DisplayName,
                "班表常用表服務必須顯示目錄名稱，不可只顯示 ID。");
            Require(DisplayColumn(manualGrid, firstManual, "車型") == sample.VehicleTypes.Single(item => item.Id == "EMU-100").DisplayName,
                "班表常用表車型必須顯示目錄名稱，不可只顯示 ID。");
            Require(DisplayColumn(manualGrid, firstManual, "停站模式") == sample.StopPatterns.Single(item => item.Id == "FULL-LINE-O13-HOLD").DisplayName,
                "班表常用表停站模式必須顯示目錄名稱，不可只顯示 ID。");
            Require(DisplayColumn(manualGrid, firstManual, "起點月台").Contains("O01 站", StringComparison.Ordinal),
                "班表常用表起點必須顯示車站與月台名稱。");

            // Page-level selectors must commit through the same navigation hook as
            // row details; changing them and leaving immediately must be durable.
            var mode = FindByName<ComboBox>(editor, "啟用班表模式");
            var assignment = FindByName<ComboBox>(editor, "車輛分配模式");
            Require(mode.Items.Count > 1 && assignment.Items.Count > 1, "班表模式與車輛分配選單必須提供完整選項。");
            mode.SelectedIndex = (mode.SelectedIndex + 1) % mode.Items.Count;
            assignment.SelectedIndex = (assignment.SelectedIndex + 1) % assignment.Items.Count;
            var selectedMode = mode.SelectedItem?.GetType().GetProperty("Id")?.GetValue(mode.SelectedItem)?.ToString();
            var selectedAssignment = assignment.SelectedItem?.GetType().GetProperty("Id")?.GetValue(assignment.SelectedItem)?.ToString();

            // The first manual row starts on the outbound platform.  Switching
            // direction must refresh the origin candidates and keep the old
            // platform as an explicit missing reference when it is not valid for
            // the new route.
            var direction = FindByName<ComboBox>(editor, "班表方向");
            var origin = FindByName<ComboBox>(editor, "班表起點月台");
            var oldOrigin = origin.SelectedItem?.GetType().GetProperty("Id")?.GetValue(origin.SelectedItem)?.ToString();
            var oldDirectionIndex = direction.SelectedIndex;
            var newDirectionIndex = (direction.SelectedIndex + 1) % direction.Items.Count;
            direction.SelectedIndex = newDirectionIndex;
            var originIds = origin.Items.OfType<object>().Select(ItemId).Where(id => id is not null).Cast<string>().ToArray();
            Require(originIds.Any(id => id.Contains(":UP", StringComparison.OrdinalIgnoreCase)), "方向切換後起點月台候選必須來自新方向路徑。");
            Require(oldOrigin is null || origin.Items.OfType<object>().Any(item =>
                string.Equals(ItemId(item), oldOrigin, StringComparison.OrdinalIgnoreCase)
                && item.GetType().GetProperty("Label")?.GetValue(item)?.ToString()?.Contains("缺少：", StringComparison.Ordinal) == true),
                "方向切換後既有但不適用的起點月台必須保留缺少提示。");
            direction.SelectedIndex = oldDirectionIndex;
            if (oldOrigin is not null)
                origin.SelectedItem = origin.Items.OfType<object>().Single(item => string.Equals(ItemId(item), oldOrigin, StringComparison.OrdinalIgnoreCase));

            navigate.Invoke(editor, [Enum.Parse(pageType, "Vehicle")]);
            Pump(editor);
            AssertPage(editorType, editor, "Vehicle");
            var dispatch = (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
            Require(dispatch.Dispatch.ActiveMode.ToString() == selectedMode, "離開班表頁時必須提交啟用模式選擇。");
            Require(dispatch.Dispatch.VehicleAssignmentMode.ToString() == selectedAssignment, "離開班表頁時必須提交車輛分配選擇。");

            // The sample's overtake references require its original manual timetable
            // to be active before a full Schema 8 serialize/reload check.
            navigate.Invoke(editor, [Enum.Parse(pageType, "DispatchPlanning")]);
            Pump(editor);
            AssertPage(editorType, editor, "DispatchPlanning");
            mode = FindByName<ComboBox>(editor, "啟用班表模式");
            assignment = FindByName<ComboBox>(editor, "車輛分配模式");
            mode.SelectedItem = mode.Items.OfType<object>().Single(item => ItemId(item) == sample.Dispatch.ActiveMode.ToString());
            assignment.SelectedItem = assignment.Items.OfType<object>().Single(item => ItemId(item) == sample.Dispatch.VehicleAssignmentMode.ToString());
            navigate.Invoke(editor, [Enum.Parse(pageType, "Vehicle")]);
            Pump(editor);
            AssertPage(editorType, editor, "Vehicle");
            dispatch = GetDraft(editorType, editor);
            Require(dispatch.Dispatch.ActiveMode == sample.Dispatch.ActiveMode
                && dispatch.Dispatch.VehicleAssignmentMode == sample.Dispatch.VehicleAssignmentMode,
                "班表模式測試後回復樣本狀態，供完整 Schema 8 往返驗證。");

            // CRUD entry points remain live on the redesigned pages.  Click the
            // real controls and inspect the same draft used by Apply, rather than
            // merely checking that the buttons exist.
            var vehicleCount = dispatch.VehicleTypes.Length;
            ClickNamed<Button>(editor, "新增車型");
            Require(GetDraft(editorType, editor).VehicleTypes.Length == vehicleCount + 1, "車型頁新增入口必須更新同一份草稿。");
            var vehicleGrid = FindByName<DataGrid>(editor, "車型目錄");
            vehicleGrid.SelectedIndex = vehicleGrid.Items.Count - 1;
            ClickNamed<Button>(editor, "刪除選取車型");
            Require(GetDraft(editorType, editor).VehicleTypes.Length == vehicleCount, "車型頁刪除入口必須更新同一份草稿。");

            navigate.Invoke(editor, [Enum.Parse(pageType, "Services")]);
            Pump(editor);
            AssertPage(editorType, editor, "Services");
            var serviceCount = GetDraft(editorType, editor).ServiceTypes.Length;
            Require(FindByName<ComboBox>(editor, "服務預設車型").Items.Count > 0, "服務頁必須提供名稱化車型參照選單。");
            ClickNamed<Button>(editor, "新增服務類型");
            Require(GetDraft(editorType, editor).ServiceTypes.Length == serviceCount + 1, "服務頁新增入口必須更新同一份草稿。");
            var serviceGrid = FindByName<DataGrid>(editor, "服務類型目錄");
            serviceGrid.SelectedIndex = serviceGrid.Items.Count - 1;
            ClickNamed<Button>(editor, "刪除選取服務類型");
            Require(GetDraft(editorType, editor).ServiceTypes.Length == serviceCount, "服務頁刪除入口必須更新同一份草稿。");
            var tabs = Descendants(editor).OfType<TabControl>().First(control => control.Items.OfType<TabItem>().Any(item => item.Header?.ToString() == "服務路徑"));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => item.Header?.ToString() == "服務路徑");
            Pump(editor);
            ClickNamed<Button>(editor, "新增服務路徑");
            Require(GetDraft(editorType, editor).ServiceRoutes.Length == sample.ServiceRoutes.Length + 1, "服務路徑新增入口必須更新同一份草稿。");
            var routeGrid = FindByName<DataGrid>(editor, "服務路徑目錄");
            routeGrid.SelectedIndex = routeGrid.Items.Count - 1;
            ClickNamed<Button>(editor, "刪除服務路徑");
            Require(GetDraft(editorType, editor).ServiceRoutes.Length == sample.ServiceRoutes.Length, "服務路徑刪除入口必須更新同一份草稿。");

            navigate.Invoke(editor, [Enum.Parse(pageType, "StopPatterns")]);
            Pump(editor);
            AssertPage(editorType, editor, "StopPatterns");

            // Copy a real pattern, leave the page, serialize/reopen the same
            // draft, then edit the copied instruction.  This guards against a
            // stale page VM being submitted after the copy operation.
            var copySource = GetDraft(editorType, editor).StopPatterns.First(pattern => pattern.Instructions.Length > 0);
            var copyStationId = copySource.Instructions[0].StationId;
            var sourceSelector = FindByName<ComboBox>(editor, "停站模式操作來源");
            sourceSelector.SelectedItem = sourceSelector.Items.OfType<object>().Single(item =>
                string.Equals(ItemId(item), copySource.Id, StringComparison.OrdinalIgnoreCase));
            var patternCountBeforeCopy = GetDraft(editorType, editor).StopPatterns.Length;
            ClickNamed<Button>(editor, "停站模式複製");
            Pump(editor);
            AssertPage(editorType, editor, "StopPatterns");
            var copiedDraft = GetDraft(editorType, editor);
            Require(copiedDraft.StopPatterns.Length == patternCountBeforeCopy + 1, "複製停站模式必須更新同一份草稿。");
            var copiedPattern = copiedDraft.StopPatterns.Single(pattern =>
                pattern.Id.StartsWith(copySource.Id + "-COPY", StringComparison.OrdinalIgnoreCase));
            var reopenedDocument = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(copiedDraft));
            var reopened = (Window)Activator.CreateInstance(editorType, reopenedDocument, Enum.Parse(pageType, "Operations"))!;
            reopened.Left = -10000; reopened.Top = -10000; reopened.WindowStartupLocation = WindowStartupLocation.Manual; reopened.ShowInTaskbar = false;
            reopened.Show();
            try
            {
                navigate.Invoke(reopened, [Enum.Parse(pageType, "StopPatterns")]);
                Pump(reopened);
                AssertPage(editorType, reopened, "StopPatterns");
                var reopenedMatrix = FindByName<DataGrid>(reopened, "停站模式矩陣");
                SelectMatrixCell(reopenedMatrix, copyStationId, copiedPattern.Id);
                var reopenedDwell = FindByName<TextBox>(reopened, "停站模式停站秒數");
                reopenedDwell.Text = "12.3456789012";
                Invoke(editorType, reopened, "CommitOperationsPageEditors");
                var reopenedDraft = GetDraft(editorType, reopened);
                var editedInstruction = reopenedDraft.StopPatterns.Single(pattern => pattern.Id == copiedPattern.Id)
                    .Instructions.Single(instruction => instruction.StationId == copyStationId);
                Require(editedInstruction.DwellTimeSeconds is { } dwellValue && Math.Abs(dwellValue - 12.3456789012) < 1e-10,
                    "複製模式序列化／重開後再次編輯必須寫回同一 instruction 且保留精度。");
            }
            finally { reopened.Close(); }

            var firstPattern = GetDraft(editorType, editor).StopPatterns.First();
            var instructionCount = firstPattern.Instructions.Length;
            ClickNamed<Button>(editor, "新增停站車站指令");
            Invoke(editorType, editor, "CommitOperationsPageEditors");
            Require(GetDraft(editorType, editor).StopPatterns.First().Instructions.Length == instructionCount + 1,
                "停站模式完整編輯器的車站指令新增必須更新同一份草稿。");
            var patternCount = GetDraft(editorType, editor).StopPatterns.Length;
            ClickNamed<Button>(editor, "新增停站模式");
            Require(GetDraft(editorType, editor).StopPatterns.Length == patternCount + 1, "停站模式新增入口必須更新同一份草稿。");
            var patternGrid = FindByName<DataGrid>(editor, "停站模式目錄");
            patternGrid.SelectedIndex = patternGrid.Items.Count - 1;
            ClickNamed<Button>(editor, "刪除停站模式");
            Require(GetDraft(editorType, editor).StopPatterns.Length == patternCount, "停站模式刪除入口必須更新同一份草稿。");
            Console.WriteLine("[通過] 營運頁矩陣、待避引用、無效數值保留、完整接續選單、模式提交與方向月台更新");
        }
        finally { editor.Close(); }
    }

    private static void Pump(Window editor)
    {
        editor.UpdateLayout();
        editor.Dispatcher.Invoke(DispatcherPriority.Loaded, new Action(() => { }));
        editor.UpdateLayout();
    }

    private static void AssertPage(Type editorType, object editor, string expectedPage)
    {
        var currentPage = editorType.GetField("currentPage", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(editor)?.ToString();
        var validation = editorType.GetField("validationMessages", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(editor) as IEnumerable;
        var validationText = validation is null
            ? "（無）"
            : string.Join(" | ", validation.Cast<object>().Select(item => item?.ToString()));
        Require(string.Equals(currentPage, expectedPage, StringComparison.Ordinal),
            $"導覽後頁面不符：預期={expectedPage}，實際={currentPage}，validation={validationText}");
    }

    private static string DisplayColumn(DataGrid grid, object row, string header)
    {
        var column = (DataGridTextColumn)grid.Columns.Single(item => item.Header?.ToString() == header);
        var binding = (Binding)column.Binding;
        var value = row.GetType().GetProperty(binding.Path.Path)!.GetValue(row);
        return (binding.Converter?.Convert(value!, typeof(string), null!, CultureInfo.InvariantCulture) ?? value)?.ToString() ?? "";
    }

    private static void SelectMatrixCell(DataGrid grid, string stationId, string patternId)
    {
        var row = grid.Items.Cast<object>().Single(item => string.Equals(
            item.GetType().GetProperty("StationId")!.GetValue(item)?.ToString(), stationId, StringComparison.OrdinalIgnoreCase));
        var column = grid.Columns.Single(item => item.Header?.GetType().GetProperty("PatternId")?.GetValue(item.Header)?.ToString() == patternId);
        grid.SelectedCells.Clear();
        grid.SelectedCells.Add(new DataGridCellInfo(row, column));
    }

    private static T FindByName<T>(DependencyObject root, string name) where T : DependencyObject =>
        Descendants(root).OfType<T>().Single(control => AutomationProperties.GetName(control) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string? ItemId(object item) =>
        item.GetType().GetProperty("Id")?.GetValue(item)?.ToString();

    private static object? Invoke(Type type, object instance, string method) =>
        type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, null);

    private static T ClickNamed<T>(DependencyObject root, string name) where T : Button
    {
        var button = FindByName<T>(root, name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return button;
    }

    private static TopologyProjectDocument GetDraft(Type editorType, object editor)
    {
        var state = editorType.GetField("state", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        return (TopologyProjectDocument)state.GetType().GetProperty("Draft")!.GetValue(state)!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
