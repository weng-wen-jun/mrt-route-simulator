using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 營運規劃工作區。此檔只組合既有 Schema 8 editor view model；不建立第二套
/// route、stop pattern 或 dispatch runtime。頁面離開時由 <see cref="CommitOperationsPageEditors"/>
/// 將目前的 editor commit 回同一份草稿。
/// </summary>
internal sealed partial class TopologyEditorWindow
{
    private readonly Dictionary<string, string> operationRawValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProjectValidationMessage> operationEditorErrors = [];
    private bool? originalHeadwayWasNull;
    private bool? originalManualTimetableWasNull;
    private readonly List<TextBox> operationEditorTextBoxes = [];
    private StopPatternMatrixCell? selectedStopPatternCell;
    private DataGrid? stopPatternMatrix;
    private CheckBox? stopPatternDifferencesOnly;
    private ComboBox? stopPatternActionEditor;
    private TextBox? stopPatternDwellEditor;
    private TextBox? stopPatternPassingSpeedEditor;
    private ComboBox? stopPatternWaitEditor;
    private TextBlock? stopPatternAffectedRuns;
    private StopPatternEditorContext? stopPatternContext;
    private bool updatingOperationEditors;

    private ManualTimetableEditorViewModel? selectedDispatchRow;
    private DataGrid? dispatchManualGrid;
    private ComboBox? dispatchModeEditor;
    private ComboBox? dispatchAssignmentEditor;
    private ComboBox? dispatchServiceEditor;
    private ComboBox? dispatchVehicleEditor;
    private ComboBox? dispatchPatternEditor;
    private ComboBox? dispatchDirectionEditor;
    private ComboBox? dispatchOriginEditor;
    private ComboBox? dispatchContinuationEditor;
    private TextBox? dispatchDepartureEditor;
    private TextBox? dispatchVehicleIdEditor;
    private TextBox? dispatchServiceRunIdEditor;
    private CheckBox? dispatchContinueEditor;
    private TextBlock? dispatchReferences;

    /// <summary>由 shell 導覽呼叫的車型頁。</summary>
    private void ShowVehiclePage()
    {
        CommitOperationsPageEditors();
        selectedStopPatternCell = null;
        selectedDispatchRow = null;
        var panel = NewPage("車型", "每班列車使用的性能由車型目錄提供。常用停站模式使用名稱選單；原始 ID 仍保留在進階欄位。\n數值輸入若暫時無效會保留原文字並顯示驗證訊息，不會在切頁時靜默丟失。\n");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel();
        var vehicleReferencePanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        AutomationProperties.SetName(vehicleReferencePanel, "車型常用參照");
        var search = new TextBox { Width = 220, Margin = new Thickness(0, 0, 0, 8), ToolTip = "依車型名稱、ID 或相容服務搜尋" };
        AutomationProperties.SetName(search, "車型搜尋");
        content.Children.Add(search);
        var view = CollectionViewSource.GetDefaultView(serviceNetwork.VehicleTypes);
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            view.Filter = string.IsNullOrWhiteSpace(query) ? null : item => MatchesSearch(item, query);
            view.Refresh();
        };

        var grid = CreateGrid(view);
        AutomationProperties.SetName(grid, "車型目錄");
        grid.MinHeight = 205;
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is VehicleTypeEditorViewModel row)
            {
                selectionDetails.Text = DescribeSelection(row, "選取車型後可查看性能與參照。");
                vehicleReferencePanel.Children.Clear();
                vehicleReferencePanel.Children.Add(new TextBlock { Text = $"車型：{row.Name}（{row.Id}）", FontWeight = FontWeights.SemiBold });
                vehicleReferencePanel.Children.Add(new TextBlock { Text = "預設停站模式", Margin = new Thickness(0, 8, 0, 3) });
                var combo = CreateReferenceCombo("車型預設停站模式", BuildPatternChoices(row.DefaultStopPattern, includeBlank: true), row.DefaultStopPattern);
                combo.SelectionChanged += (_, _) =>
                {
                    if (updatingOperationEditors) return;
                    row.DefaultStopPattern = (combo.SelectedItem as OperationReferenceChoice)?.Id ?? string.Empty;
                };
                vehicleReferencePanel.Children.Add(combo);
                vehicleReferencePanel.Children.Add(new TextBlock
                {
                    Text = $"相容服務：{(string.IsNullOrWhiteSpace(row.CompatibleServiceTypes) ? "未指定服務預設" : row.CompatibleServiceTypes)}",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 10, 0, 0)
                });
            }
        };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateNamedButton("新增車型", "新增車型", (_, _) => AddVehicleType(), true));
        actions.Children.Add(CreateNamedButton("刪除選取車型", "刪除選取車型", (_, _) => DeleteSelectedVehicleType(), true));
        content.Children.Add(actions);
        content.Children.Add(grid);
        content.Children.Add(vehicleReferencePanel);
        if (grid.Items.Count > 0) grid.SelectedIndex = 0;
        scroll.Content = content;
        panel.Children.Add(scroll);
        workspace.Content = WrapOperationsPage(panel);
        RegisterPageCommitHook("operations.vehicle", CommitOperationsPageHook);
    }

    /// <summary>由 shell 導覽呼叫的服務與行車路徑頁；路徑 detail 只使用本頁選取的 context。</summary>
    private void ShowServicesPage()
    {
        CommitOperationsPageEditors();
        selectedStopPatternCell = null;
        selectedDispatchRow = null;
        var panel = NewPage("服務與行車路徑", "服務分類、預設車型／停站模式與服務路徑分開編輯。路徑停靠站和候選月台由本頁目前選取的路徑明確提供。\n");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel();
        var tabs = new TabControl { MinHeight = 430 };

        var servicePanel = new DockPanel { Margin = new Thickness(4) };
        var serviceGrid = CreateGrid(serviceNetwork.ServiceTypes);
        AutomationProperties.SetName(serviceGrid, "服務類型目錄");
        var serviceActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        serviceActions.Children.Add(CreateNamedButton("新增服務", "新增服務類型", (_, _) => AddServiceType(), true));
        serviceActions.Children.Add(CreateNamedButton("刪除選取服務", "刪除選取服務類型", (_, _) => DeleteSelectedServiceType(), true));
        DockPanel.SetDock(serviceActions, Dock.Top);
        servicePanel.Children.Add(serviceActions);
        var serviceReferencePanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        AutomationProperties.SetName(serviceReferencePanel, "服務常用參照");
        serviceGrid.SelectionChanged += (_, _) =>
        {
            if (serviceGrid.SelectedItem is ServiceTypeEditorViewModel row)
            {
                selectionDetails.Text = DescribeSelection(row, "選取服務後可查看預設車型、停站模式與路徑。");
                ShowServiceReferences(row);
            }
        };
        serviceGrid.Height = 210;
        DockPanel.SetDock(serviceGrid, Dock.Top);
        servicePanel.Children.Add(serviceGrid);
        servicePanel.Children.Add(serviceReferencePanel);
        servicePanel.Children.Add(new TextBlock
        {
            Text = "服務表的預設停站模式／預設車型仍保存原始 ID；使用下方名稱選單查看與套用參照，避免直接編輯編號。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106))
        });
        tabs.Items.Add(new TabItem { Header = "服務類型", Content = servicePanel });
        if (serviceGrid.Items.Count > 0) serviceGrid.SelectedIndex = 0;

        var routeEditor = BuildServiceRouteEditor();
        AutomationProperties.SetName(routeEditor, "服務路徑編輯入口");
        if (routeEditor is FrameworkElement routeElement) routeElement.MinHeight = 520;
        SetButtonAutomationName(routeEditor, "新增服務路徑", "新增服務路徑");
        SetButtonAutomationName(routeEditor, "刪除", "刪除服務路徑");
        SetButtonAutomationName(routeEditor, "設為下行進路", "設為下行服務路徑");
        SetButtonAutomationName(routeEditor, "設為上行進路", "設為上行服務路徑");
        SetButtonAutomationName(routeEditor, "新增通過區段", "新增路徑通過區段");
        SetButtonAutomationName(routeEditor, "新增停靠站", "新增路徑停靠站");
        if (routeGrid is not null)
        {
            AutomationProperties.SetName(routeGrid, "服務路徑目錄");
            RegisterValidationTarget(ProjectValidationTargetKind.ServiceRoute, new ValidationEditorTarget(routeGrid, []), append: true);
        }
        tabs.Items.Add(new TabItem { Header = "服務路徑", Content = routeEditor });

        content.Children.Add(tabs);
        scroll.Content = content;
        panel.Children.Add(scroll);
        workspace.Content = WrapOperationsPage(panel);
        RegisterPageCommitHook("operations.services", CommitOperationsPageHook);

        void ShowServiceReferences(ServiceTypeEditorViewModel row)
        {
            serviceReferencePanel.Children.Clear();
            serviceReferencePanel.Children.Add(new TextBlock { Text = $"服務：{row.Name}（{row.Id}）", FontWeight = FontWeights.SemiBold });
            serviceReferencePanel.Children.Add(new TextBlock { Text = "預設車型", Margin = new Thickness(0, 6, 0, 2) });
            var vehicle = CreateReferenceCombo("服務預設車型", BuildVehicleChoices(row.DefaultVehicleType, includeBlank: true), row.DefaultVehicleType);
            vehicle.SelectionChanged += (_, _) =>
            {
                if (!updatingOperationEditors) row.DefaultVehicleType = (vehicle.SelectedItem as OperationReferenceChoice)?.Id ?? string.Empty;
            };
            serviceReferencePanel.Children.Add(vehicle);
            serviceReferencePanel.Children.Add(new TextBlock { Text = "預設停站模式", Margin = new Thickness(0, 6, 0, 2) });
            var pattern = CreateReferenceCombo("服務預設停站模式", BuildPatternChoices(row.DefaultStopPattern, includeBlank: true), row.DefaultStopPattern);
            pattern.SelectionChanged += (_, _) =>
            {
                if (!updatingOperationEditors) row.DefaultStopPattern = (pattern.SelectedItem as OperationReferenceChoice)?.Id ?? string.Empty;
            };
            serviceReferencePanel.Children.Add(pattern);
        }
    }

    /// <summary>由 shell 導覽呼叫的停站模式矩陣頁。</summary>
    private void ShowStopPatternsPage()
    {
        CommitOperationsPageEditors();
        selectedDispatchRow = null;
        stopPatternContext = new StopPatternEditorContext();
        var panel = NewPage("停站模式", "橫列是服務路徑可辨識的車站，直欄是停站模式。點選儲存格後在下方編輯同一筆停站設定；停靠＋待避使用既有停靠與指定待避欄位。\n「不適用」來自該模式是否有該站，不依賴畫面排序。\n");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var patternSelector = CreateReferenceCombo("停站模式操作來源", serviceNetwork.StopPatterns.Select(pattern => new OperationReferenceChoice(pattern.Id, $"{pattern.Name}（{pattern.Id}）")), null);
        patternSelector.Width = 310;
        actions.Children.Add(patternSelector);
        actions.Children.Add(CreateNamedButton("複製模式", "停站模式複製", (_, _) => CopySelectedStopPattern(patternSelector), true));
        actions.Children.Add(CreateNamedButton("批次設為停靠", "批次設定停靠", (_, _) => BatchSetStopPatternAction(patternSelector, StopPatternAction.Stop), true));
        actions.Children.Add(CreateNamedButton("批次設為通過", "批次設定通過", (_, _) => BatchSetStopPatternAction(patternSelector, StopPatternAction.Pass), true));
        stopPatternDifferencesOnly = new CheckBox { Content = "只顯示差異站", Margin = new Thickness(8, 6, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(stopPatternDifferencesOnly, "只顯示差異站");
        stopPatternDifferencesOnly.Checked += (_, _) => RefreshStopPatternMatrix();
        stopPatternDifferencesOnly.Unchecked += (_, _) => RefreshStopPatternMatrix();
        actions.Children.Add(stopPatternDifferencesOnly);
        content.Children.Add(actions);

        stopPatternMatrix = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            IsReadOnly = true, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            HeadersVisibility = DataGridHeadersVisibility.All, RowHeaderWidth = 0, MinHeight = 238,
            Height = 340,
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(stopPatternMatrix, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(stopPatternMatrix, "停站模式矩陣");
        stopPatternMatrix.SelectionChanged += (_, _) => SelectStopPatternMatrixCell();
        stopPatternMatrix.SelectedCellsChanged += (_, _) => SelectStopPatternMatrixCell();
        content.Children.Add(stopPatternMatrix);

        var detail = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddLabeledCombo(detail, 0, "動作", Enum.GetValues<StopPatternAction>().Select(item => item), out stopPatternActionEditor, "停站模式動作");
        stopPatternDwellEditor = AddLabeledTextBox(detail, 1, "停站秒數（可留空）", "停站模式停站秒數");
        stopPatternPassingSpeedEditor = AddLabeledTextBox(detail, 2, "通過速限（km/h，可留空）", "停站模式通過速限");
        AddLabeledCombo(detail, 3, "指定待避車次", Array.Empty<object>(), out stopPatternWaitEditor, "停站模式待避車次");
        stopPatternAffectedRuns = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(65, 75, 95)) };
        AutomationProperties.SetName(stopPatternAffectedRuns, "停站模式受影響班次");
        Grid.SetRow(stopPatternAffectedRuns, 4); Grid.SetColumn(stopPatternAffectedRuns, 1); detail.Children.Add(stopPatternAffectedRuns);
        content.Children.Add(detail);

        // The matrix is the fast station-centric view. Keep the complete
        // route-scoped editor below it so existing projects retain pattern
        // creation/deletion/renaming and instruction add/remove operations.
        var instructionRoute = CreateReferenceCombo(
            "停站模式指令來源路徑",
            serviceNetwork.Routes.Select(route => new OperationReferenceChoice(route.Id, $"{route.Name}（{route.Id}）")),
            serviceNetwork.Routes.FirstOrDefault()?.Id);
        instructionRoute.Margin = new Thickness(0, 12, 0, 6);
        instructionRoute.SelectionChanged += (_, _) =>
        {
            if (updatingOperationEditors) return;
            selectedRoute = serviceNetwork.Routes.FirstOrDefault(route => IdEquals(route.Id, (instructionRoute.SelectedItem as OperationReferenceChoice)?.Id));
        };
        content.Children.Add(instructionRoute);
        selectedRoute = serviceNetwork.Routes.FirstOrDefault();
        var completeEditor = BuildStopPatternEditor();
        AutomationProperties.SetName(completeEditor, "停站模式完整編輯入口");
        if (completeEditor is FrameworkElement patternElement) patternElement.MinHeight = 390;
        SetButtonAutomationName(completeEditor, "從服務路徑建立", "新增停站模式");
        SetButtonAutomationName(completeEditor, "刪除", "刪除停站模式");
        SetButtonAutomationName(completeEditor, "新增車站指令", "新增停站車站指令");
        SetButtonAutomationName(completeEditor, "刪除指令", "刪除停站車站指令");
        if (stopPatternGrid is not null)
        {
            AutomationProperties.SetName(stopPatternGrid, "停站模式目錄");
            RegisterValidationTarget(ProjectValidationTargetKind.StopPattern, new ValidationEditorTarget(stopPatternGrid, []), append: true);
        }
        content.Children.Add(completeEditor);
        scroll.Content = content;
        panel.Children.Add(scroll);
        stopPatternActionEditor.SelectionChanged += (_, _) =>
        {
            if (updatingOperationEditors || selectedStopPatternCell?.Instruction is not { } instruction || stopPatternActionEditor.SelectedItem is not StopPatternAction action) return;
            instruction.Action = action;
            TryCommitOperationsPageEditors();
            RefreshStopPatternMatrix();
        };
        stopPatternWaitEditor.SelectionChanged += (_, _) =>
        {
            if (updatingOperationEditors || selectedStopPatternCell?.Instruction is not { } instruction) return;
            instruction.WaitForOvertakeServiceRunId = (stopPatternWaitEditor.SelectedItem as OperationReferenceChoice)?.Id ?? string.Empty;
            TryCommitOperationsPageEditors();
            RefreshStopPatternMatrix();
        };
        stopPatternDwellEditor.LostFocus += (_, _) => { CommitStopPatternDetail(); RefreshStopPatternMatrix(); };
        stopPatternPassingSpeedEditor.LostFocus += (_, _) => { CommitStopPatternDetail(); RefreshStopPatternMatrix(); };
        patternSelector.SelectionChanged += (_, _) =>
        {
            if (updatingOperationEditors) return;
            selectedStopPatternCell = selectedStopPatternCell is null
                ? null
                : FindStopPatternCell(selectedStopPatternCell.StationId, (patternSelector.SelectedItem as OperationReferenceChoice)?.Id);
            SelectFirstCellForPattern(patternSelector.SelectedItem as OperationReferenceChoice);
        };

        RefreshStopPatternMatrix();
        if (patternSelector.Items.Count > 0) patternSelector.SelectedIndex = 0;
        workspace.Content = WrapOperationsPage(panel);
        RegisterPageCommitHook("operations.stop-patterns", CommitOperationsPageHook);
    }

    /// <summary>由 shell 導覽呼叫的班表與接續頁。</summary>
    private void ShowDispatchPlanningPage()
    {
        CommitOperationsPageEditors();
        selectedStopPatternCell = null;
        var panel = NewPage("班表與接續", "啟用模式與車輛分配和班表在同一頁。常用欄位先列在主表，車輛 ID、車次 ID 與指定接續車次在下方分開編輯；既有停用計畫與空值語意由原存檔保留。\n");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel();
        var modes = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        modes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddModeLabel(modes, 0, "啟用模式");
        dispatchModeEditor = CreateReferenceCombo("啟用班表模式", Enum.GetValues<DispatchPlanningMode>().Select(item => new OperationReferenceChoice(item.ToString(), UiDisplayText.Enum(item))), state.Draft.Dispatch.ActiveMode.ToString());
        Grid.SetColumn(dispatchModeEditor, 1); modes.Children.Add(dispatchModeEditor);
        AddModeLabel(modes, 2, "車輛分配");
        dispatchAssignmentEditor = CreateReferenceCombo("車輛分配模式", Enum.GetValues<VehicleAssignmentMode>().Distinct().Select(item => new OperationReferenceChoice(item.ToString(), UiDisplayText.Enum(item))), state.Draft.Dispatch.VehicleAssignmentMode.ToString());
        Grid.SetColumn(dispatchAssignmentEditor, 3); modes.Children.Add(dispatchAssignmentEditor);
        content.Children.Add(modes);
        content.Children.Add(CreateNamedButton("套用班表設定", "套用班表啟用模式與車輛分配", (_, _) => CommitDispatchModes(), false));

        var tabs = new TabControl { MinHeight = 365, Margin = new Thickness(0, 8, 0, 0) };
        tabs.Items.Add(DispatchTab(ProjectValidationTargetKind.HeadwayPlan, "班距計畫", dispatch.HeadwayPlans,
            AddHeadwayPlanForOperationsPage, EditSelectedHeadwayPlanForOperationsPage, DeleteSelectedHeadwayPlanForOperationsPage));
        dispatchManualGrid = CreateDispatchManualGrid();
        var manualRoot = new DockPanel { Margin = new Thickness(4) };
        var manualActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        manualActions.Children.Add(CreateNamedButton("新增班表", "新增手動班表", (_, _) => AddManualTimetableForOperationsPage(), true));
        manualActions.Children.Add(CreateNamedButton("編輯選取班表", "編輯手動班表", (_, _) => EditSelectedManualForOperationsPage(), true));
        manualActions.Children.Add(CreateNamedButton("刪除選取班表", "刪除手動班表", (_, _) => DeleteSelectedManualForOperationsPage(), true));
        DockPanel.SetDock(manualActions, Dock.Top); manualRoot.Children.Add(manualActions);
        DockPanel.SetDock(dispatchManualGrid, Dock.Top); manualRoot.Children.Add(dispatchManualGrid);
        var dispatchDetail = BuildDispatchDetailPanel();
        manualRoot.Children.Add(dispatchDetail);
        tabs.Items.Add(new TabItem
        {
            Header = "手動班表與接續",
            Content = new ScrollViewer
            {
                Content = manualRoot,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        });
        var preview = CreateGrid(dispatch.Runs, true);
        AutomationProperties.SetName(preview, "班表展開預覽");
        tabs.Items.Add(new TabItem { Header = "展開預覽", Content = new ScrollViewer { Content = preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.SelectedIndex = state.Draft.Dispatch.ActiveMode == DispatchPlanningMode.ManualTimetable ? 1 : 0;
        content.Children.Add(tabs);
        scroll.Content = content;
        panel.Children.Add(scroll);
        workspace.Content = WrapOperationsPage(panel);
        RegisterPageCommitHook("operations.dispatch", CommitOperationsPageHook);
        if (dispatchManualGrid.Items.Count > 0) dispatchManualGrid.SelectedIndex = 0;
    }

    /// <summary>
    /// Shell 的 Apply hook 可呼叫此方法。錯誤數值不會覆寫舊值，raw text 仍在
    /// <see cref="operationRawValues"/> 中，下一次切回頁面會重新顯示。
    /// </summary>
    private bool CommitOperationsPageEditors()
    {
        originalHeadwayWasNull ??= state.Draft.Dispatch.SimpleHeadwayPlans is null;
        originalManualTimetableWasNull ??= state.Draft.Dispatch.ManualTimetableRows is null;
        operationEditorErrors.Clear();
        var errors = new List<ProjectValidationMessage>();
        CommitStopPatternDetail();
        errors.AddRange(operationEditorErrors);
        operationEditorErrors.Clear();
        CommitDispatchDetail();
        errors.AddRange(operationEditorErrors);
        operationEditorErrors.Clear();
        CommitDispatchModeEditors();
        errors.AddRange(operationEditorErrors);
        if (errors.Count > 0)
        {
            SetValidationMessages(errors);
            throw new EditorValidationException(errors);
        }
        CommitTableDrafts();
        RestoreInactiveDispatchNullSemantics();
        return true;
    }

    private void RestoreInactiveDispatchNullSemantics()
    {
        var dispatchPlan = state.Draft.Dispatch;
        var simple = originalHeadwayWasNull == true && dispatch.HeadwayPlans.Count == 0 ? null : dispatchPlan.SimpleHeadwayPlans;
        var manual = originalManualTimetableWasNull == true && dispatch.ManualRows.Count == 0 ? null : dispatchPlan.ManualTimetableRows;
        if (!ReferenceEquals(simple, dispatchPlan.SimpleHeadwayPlans) || !ReferenceEquals(manual, dispatchPlan.ManualTimetableRows))
            state.Replace(state.Draft with { Dispatch = dispatchPlan with { SimpleHeadwayPlans = simple, ManualTimetableRows = manual } });
    }

    private void CommitOperationsPageHook() => CommitOperationsPageEditors();

    private bool TryCommitOperationsPageEditors()
    {
        try { CommitOperationsPageEditors(); return true; }
        catch (EditorValidationException) { return false; }
    }

    private void RefreshStopPatternMatrix()
    {
        if (stopPatternMatrix is null) return;
        var columns = serviceNetwork.StopPatterns.Select(pattern => new StopPatternMatrixColumn(pattern.Id, pattern.Name)).ToArray();
        stopPatternMatrix.Columns.Clear();
        var stationIds = state.Draft.ServiceRoutes.SelectMany(route => route.Stops.Select(stop => stop.StationId))
            .Concat(state.Draft.StopPatterns.SelectMany(pattern => pattern.Instructions.Select(instruction => instruction.StationId)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        stopPatternMatrix.Columns.Add(new DataGridTextColumn { Header = "車站", Binding = new Binding(nameof(StopPatternMatrixRow.StationName)), IsReadOnly = true });
        foreach (var column in columns)
        {
            var dataColumn = new DataGridTextColumn
            {
                Header = column,
                Binding = new Binding($"Values[{column.PatternId}]"),
                IsReadOnly = true
            };
            stopPatternMatrix.Columns.Add(dataColumn);
        }
        var rows = stationIds.Select(stationId =>
        {
            var station = state.Draft.Topology.Stations.FirstOrDefault(item => IdEquals(item.StationId, stationId));
            var cells = columns.ToDictionary(column => column.PatternId, column => BuildStopPatternCell(column.PatternId, stationId), StringComparer.OrdinalIgnoreCase);
            return new StopPatternMatrixRow(stationId, station?.Name ?? stationId, cells);
        }).ToArray();
        if (stopPatternDifferencesOnly?.IsChecked == true)
            rows = rows.Where(row => row.Cells.Values.Select(cell => cell.Display).Where(value => value != "不適用").Distinct(StringComparer.Ordinal).Count() > 1).ToArray();
        stopPatternMatrix.ItemsSource = rows;
        if (selectedStopPatternCell is not null)
        {
            var row = rows.FirstOrDefault(item => IdEquals(item.StationId, selectedStopPatternCell.StationId));
            if (row is not null) SelectMatrixCell(row, selectedStopPatternCell.PatternId);
        }
        stopPatternAffectedRuns!.Text = selectedStopPatternCell is null
            ? "請選取模式與車站；下方會列出修改該共用模式的受影響班次。"
            : DescribeAffectedDispatchRuns(selectedStopPatternCell.PatternId);
    }

    private StopPatternMatrixCell BuildStopPatternCell(string patternId, string stationId)
    {
        var pattern = serviceNetwork.StopPatterns.FirstOrDefault(item => IdEquals(item.Id, patternId));
        var instruction = pattern?.Instructions.FirstOrDefault(item => IdEquals(item.Station, stationId));
        if (pattern is null || instruction is null)
            return new StopPatternMatrixCell(patternId, stationId, null, "不適用");
        var wait = string.IsNullOrWhiteSpace(instruction.WaitForOvertakeServiceRunId) ? "" : $"＋待避 {instruction.WaitForOvertakeServiceRunId}";
        var action = instruction.Action switch
        {
            StopPatternAction.Stop => $"停靠 {(instruction.DwellSeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? "沿用預設")} 秒",
            StopPatternAction.Pass => $"通過{(instruction.PassingSpeedKmh is { } speed ? $" {speed:0.###} km/h" : "")}",
            StopPatternAction.Turnback => "折返",
            _ => UiDisplayText.Enum(instruction.Action)
        };
        return new StopPatternMatrixCell(patternId, stationId, instruction, string.IsNullOrWhiteSpace(wait) ? action : $"{action} {wait}");
    }

    private void SelectStopPatternMatrixCell()
    {
        if (stopPatternMatrix?.SelectedCells.Cast<DataGridCellInfo>().LastOrDefault() is not { Item: StopPatternMatrixRow row, Column.Header: StopPatternMatrixColumn column }) return;
        SelectMatrixCell(row, column.PatternId);
    }

    private void SelectMatrixCell(StopPatternMatrixRow row, string patternId)
    {
        CommitStopPatternDetail();
        selectedStopPatternCell = row.Cells.GetValueOrDefault(patternId);
        if (selectedStopPatternCell is null) return;
        SetStopPatternDetail(selectedStopPatternCell);
    }

    private void SelectFirstCellForPattern(OperationReferenceChoice? pattern)
    {
        if (pattern?.Id is not { } patternId || stopPatternMatrix is null) return;
        var row = stopPatternMatrix.Items.OfType<StopPatternMatrixRow>().FirstOrDefault(item => item.Cells.ContainsKey(patternId));
        if (row is not null) SelectMatrixCell(row, patternId);
    }

    private StopPatternMatrixCell? FindStopPatternCell(string stationId, string? patternId) =>
        patternId is null ? null : BuildStopPatternCell(patternId, stationId);

    private void SetStopPatternDetail(StopPatternMatrixCell cell)
    {
        updatingOperationEditors = true;
        try
        {
            stopPatternActionEditor!.ItemsSource = Enum.GetValues<StopPatternAction>();
            stopPatternActionEditor.SelectedItem = cell.Instruction?.Action;
            stopPatternDwellEditor!.Text = GetRawOrValue(RawKey(cell, "dwell"), cell.Instruction?.DwellSeconds?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            stopPatternPassingSpeedEditor!.Text = GetRawOrValue(RawKey(cell, "pass-speed"), cell.Instruction?.PassingSpeedKmh?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            var choices = BuildDispatchRunChoices(cell.Instruction?.WaitForOvertakeServiceRunId);
            stopPatternWaitEditor!.ItemsSource = choices;
            stopPatternWaitEditor.SelectedItem = choices.FirstOrDefault(choice => IdEquals(choice.Id, cell.Instruction?.WaitForOvertakeServiceRunId)) ?? choices.FirstOrDefault(choice => choice.Id is null);
            stopPatternAffectedRuns!.Text = DescribeAffectedDispatchRuns(cell.PatternId);
        }
        finally { updatingOperationEditors = false; }
    }

    private bool CommitStopPatternDetail()
    {
        if (selectedStopPatternCell?.Instruction is not { } instruction || stopPatternDwellEditor is null || stopPatternPassingSpeedEditor is null) return true;
        var errors = new List<string>();
        if (stopPatternActionEditor?.SelectedItem is StopPatternAction action) instruction.Action = action;
        if (!TryCommitNullableNumber(stopPatternDwellEditor, RawKey(selectedStopPatternCell, "dwell"), value => instruction.DwellSeconds = value, "停站秒數", errors)) { }
        if (!TryCommitNullableNumber(stopPatternPassingSpeedEditor, RawKey(selectedStopPatternCell, "pass-speed"), value => instruction.PassingSpeedKmh = value, "通過速限", errors)) { }
        if (stopPatternWaitEditor?.SelectedItem is OperationReferenceChoice choice) instruction.WaitForOvertakeServiceRunId = choice.Id ?? string.Empty;
        if (errors.Count == 0) return true;
        operationEditorErrors.AddRange(errors.Select(error => new ProjectValidationMessage(ProjectValidationSeverity.Error, error, selectedStopPatternCell.PatternId, ProjectValidationTargetKind.StopPattern)));
        return false;
    }

    private void CopySelectedStopPattern(ComboBox selector)
    {
        if (!TryCommitOperationsPageEditors()) return;
        var id = (selector.SelectedItem as OperationReferenceChoice)?.Id;
        var source = state.Draft.StopPatterns.FirstOrDefault(pattern => IdEquals(pattern.Id, id));
        if (source is null) return;
        var newId = NextDocumentId($"{source.Id}-COPY", state.Draft.StopPatterns.Select(pattern => pattern.Id));
        var copy = new ProjectStopPattern(newId, $"{source.DisplayName}（複製）", source.Instructions.ToArray());
        state.Replace(state.Draft with { StopPatterns = state.Draft.StopPatterns.Append(copy).ToArray() });
        ReloadViewModels();
        ShowStopPatternsPage();
    }

    private void BatchSetStopPatternAction(ComboBox selector, StopPatternAction action)
    {
        if (!TryCommitOperationsPageEditors()) return;
        var id = (selector.SelectedItem as OperationReferenceChoice)?.Id;
        var pattern = serviceNetwork.StopPatterns.FirstOrDefault(item => IdEquals(item.Id, id));
        if (pattern is null) return;
        foreach (var instruction in pattern.Instructions) instruction.Action = action;
        CommitTableDrafts();
        stopPatternAffectedRuns!.Text = DescribeAffectedDispatchRuns(pattern.Id) + $"\n本次批次修改：{pattern.Instructions.Count} 筆停站設定。待避引用保留，未建立另一份班次資料。";
        RefreshStopPatternMatrix();
    }

    private void AddHeadwayPlanForOperationsPage()
    {
        AddHeadwayPlan();
        ShowDispatchPlanningPage();
    }

    private void EditSelectedHeadwayPlanForOperationsPage()
    {
        EditSelectedHeadwayPlan();
        ShowDispatchPlanningPage();
    }

    private void DeleteSelectedHeadwayPlanForOperationsPage()
    {
        DeleteSelectedHeadwayPlan();
        CommitTableDrafts();
        RestoreInactiveDispatchNullSemantics();
        ReloadViewModels();
        ShowDispatchPlanningPage();
    }

    private void AddManualTimetableForOperationsPage()
    {
        AddManualTimetableRow();
        ShowDispatchPlanningPage();
    }

    private void EditSelectedManualForOperationsPage()
    {
        EditSelectedManualRow();
        ShowDispatchPlanningPage();
    }

    private void DeleteSelectedManualForOperationsPage()
    {
        DeleteSelectedManualRow();
        CommitTableDrafts();
        RestoreInactiveDispatchNullSemantics();
        ReloadViewModels();
        ShowDispatchPlanningPage();
    }

    private string DescribeAffectedDispatchRuns(string patternId)
    {
        var affected = new List<string>();
        foreach (var plan in state.Draft.Dispatch.SimpleHeadwayPlans ?? [])
            if (IdEquals(plan.StopPatternId, patternId)) affected.Add($"班距：{plan.ServiceTypeId}（{UiDisplayText.Enum(plan.Direction)}，{plan.RunCount} 班）");
        foreach (var row in state.Draft.Dispatch.ManualTimetableRows ?? [])
            if (IdEquals(row.StopPatternId, patternId)) affected.Add($"手動：{row.ServiceRunId ?? "未指定車次"}／車輛 {row.VehicleId ?? "未指定"}");
        return affected.Count == 0
            ? "受影響班次：目前沒有引用此模式（模式仍會保留，可稍後由班表引用）。"
            : "受影響班次：" + Environment.NewLine + string.Join(Environment.NewLine, affected);
    }

    private DataGrid CreateDispatchManualGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = dispatch.ManualRows, AutoGenerateColumns = false, IsReadOnly = true,
            CanUserAddRows = false, RowHeaderWidth = 0, Height = 185,
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1)
        };
        AutomationProperties.SetName(grid, "手動班表常用欄位");
        AddColumn(nameof(ManualTimetableEditorViewModel.DepartureSeconds), "發車秒數");
        AddColumn(nameof(ManualTimetableEditorViewModel.Direction), "方向", new TraditionalChineseEnumConverter());
        AddColumn(nameof(ManualTimetableEditorViewModel.ServiceType), "服務", new OperationValueDisplayConverter(value => DisplayCatalogReference(value, serviceNetwork.ServiceTypes.Select(item => (item.Id, item.Name)))));
        AddColumn(nameof(ManualTimetableEditorViewModel.VehicleType), "車型", new OperationValueDisplayConverter(value => DisplayCatalogReference(value, serviceNetwork.VehicleTypes.Select(item => (item.Id, item.Name)))));
        AddColumn(nameof(ManualTimetableEditorViewModel.StopPattern), "停站模式", new OperationValueDisplayConverter(value => DisplayCatalogReference(value, serviceNetwork.StopPatterns.Select(item => (item.Id, item.Name)))));
        AddColumn(nameof(ManualTimetableEditorViewModel.OriginPlatform), "起點月台", new OperationValueDisplayConverter(value => DisplayOriginReference(value)));
        AddColumn(nameof(ManualTimetableEditorViewModel.VehicleId), "車輛 ID");
        AddColumn(nameof(ManualTimetableEditorViewModel.ServiceRunId), "車次 ID");
        AddColumn(nameof(ManualTimetableEditorViewModel.ContinuationServiceRunId), "指定接續車次");
        grid.SelectionChanged += (_, _) =>
        {
            CommitDispatchDetail();
            selectedDispatchRow = grid.SelectedItem as ManualTimetableEditorViewModel;
            SetDispatchDetail(selectedDispatchRow);
        };
        return grid;

        void AddColumn(string path, string header, IValueConverter? converter = null) => grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { Converter = converter },
            IsReadOnly = true
        });
    }

    private static string DisplayCatalogReference(object? value, IEnumerable<(string Id, string Name)> choices)
    {
        var id = value?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return "（未指定）";
        var choice = choices.FirstOrDefault(item => IdEquals(item.Id, id));
        return string.IsNullOrWhiteSpace(choice.Name) ? $"缺少：{id}" : choice.Name;
    }

    private string DisplayOriginReference(object? value)
    {
        var id = value?.ToString();
        if (string.IsNullOrWhiteSpace(id)) return "（未指定）";
        var platform = infrastructure.Platforms.FirstOrDefault(item => IdEquals(item.Id, id));
        if (platform is null) return $"缺少：{id}";
        var station = infrastructure.Stations.FirstOrDefault(item => IdEquals(item.Id, platform.Station));
        return $"{station?.Name ?? platform.Station} / {platform.Name}";
    }

    private UIElement BuildDispatchDetailPanel()
    {
        var panel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        for (var index = 0; index < 11; index++) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dispatchDepartureEditor = AddLabeledTextBox(panel, 0, "發車秒數", "班表發車秒數");
        AddLabeledCombo(panel, 1, "方向", Enum.GetValues<TrainDirection>().Select(item => item), out dispatchDirectionEditor, "班表方向");
        AddLabeledCombo(panel, 2, "服務", Array.Empty<object>(), out dispatchServiceEditor, "班表服務");
        AddLabeledCombo(panel, 3, "車型", Array.Empty<object>(), out dispatchVehicleEditor, "班表車型");
        AddLabeledCombo(panel, 4, "停站模式", Array.Empty<object>(), out dispatchPatternEditor, "班表停站模式");
        dispatchVehicleIdEditor = AddLabeledTextBox(panel, 5, "車輛 ID（獨立身分）", "班表車輛 ID");
        dispatchServiceRunIdEditor = AddLabeledTextBox(panel, 6, "車次 ID（獨立身分）", "班表車次 ID");
        AddLabeledCombo(panel, 7, "起點月台", Array.Empty<object>(), out dispatchOriginEditor, "班表起點月台");
        var continueLabel = new TextBlock { Text = "終點後續行", Margin = new Thickness(0, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(continueLabel, 8); Grid.SetColumn(continueLabel, 0); panel.Children.Add(continueLabel);
        dispatchContinueEditor = new CheckBox { Content = "到達終點後續行", Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(dispatchContinueEditor, "班表終點後續行");
        Grid.SetRow(dispatchContinueEditor, 8); Grid.SetColumn(dispatchContinueEditor, 1); panel.Children.Add(dispatchContinueEditor);
        var continuationLabel = new TextBlock { Text = "指定接續車次", Margin = new Thickness(0, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(continuationLabel, 9); Grid.SetColumn(continuationLabel, 0); panel.Children.Add(continuationLabel);
        dispatchContinuationEditor = CreateReferenceCombo("班表接續車次", Array.Empty<OperationReferenceChoice>(), null);
        Grid.SetRow(dispatchContinuationEditor, 9); Grid.SetColumn(dispatchContinuationEditor, 1); panel.Children.Add(dispatchContinuationEditor);
        dispatchReferences = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(154, 59, 49)) };
        AutomationProperties.SetName(dispatchReferences, "班表既有引用鏈與缺漏");
        Grid.SetRow(dispatchReferences, 10); Grid.SetColumn(dispatchReferences, 1); panel.Children.Add(dispatchReferences);
        dispatchDepartureEditor.LostFocus += (_, _) => CommitDispatchDetail();
        // Changing direction changes the valid starting-platform list.  Keep the
        // old value as an explicit missing reference when it is not valid for the
        // new direction, so a direction change never silently clears user input.
        dispatchDirectionEditor.SelectionChanged += (_, _) =>
        {
            if (updatingOperationEditors || dispatchDirectionEditor.SelectedItem is not TrainDirection direction) return;
            var previousOrigin = (dispatchOriginEditor.SelectedItem as OperationReferenceChoice)?.Id ?? dispatchOriginEditor.Text;
            dispatchOriginEditor.ItemsSource = BuildOriginChoices(direction, previousOrigin);
            dispatchOriginEditor.SelectedItem = FindChoice(dispatchOriginEditor.ItemsSource, previousOrigin);
            CommitDispatchDetail();
        };
        foreach (var combo in new[] { dispatchServiceEditor, dispatchVehicleEditor, dispatchPatternEditor, dispatchOriginEditor, dispatchContinuationEditor })
            combo.SelectionChanged += (_, _) => { if (!updatingOperationEditors) CommitDispatchDetail(); };
        dispatchContinueEditor.Checked += (_, _) => { if (!updatingOperationEditors) CommitDispatchDetail(); };
        dispatchContinueEditor.Unchecked += (_, _) => { if (!updatingOperationEditors) CommitDispatchDetail(); };
        return panel;
    }

    private void SetDispatchDetail(ManualTimetableEditorViewModel? row)
    {
        updatingOperationEditors = true;
        try
        {
            var services = state.Draft.ServiceTypes.Select(item => new OperationReferenceChoice(item.Id, $"{item.DisplayName}（{item.Id}）")).ToArray();
            var vehicles = state.Draft.VehicleTypes.Select(item => new OperationReferenceChoice(item.Id, $"{item.DisplayName}（{item.Id}）")).ToArray();
            var patterns = state.Draft.StopPatterns.Select(item => new OperationReferenceChoice(item.Id, $"{item.DisplayName}（{item.Id}）")).ToArray();
            dispatchServiceEditor!.ItemsSource = IncludeMissing(services, row?.ServiceType);
            dispatchVehicleEditor!.ItemsSource = IncludeMissing(vehicles, row?.VehicleType);
            dispatchPatternEditor!.ItemsSource = IncludeMissing(patterns, row?.StopPattern);
            dispatchOriginEditor!.ItemsSource = BuildOriginChoices(row?.Direction ?? TrainDirection.Outbound, row?.OriginPlatform);
            dispatchContinuationEditor!.ItemsSource = BuildDispatchRunChoices(row?.ContinuationServiceRunId);
            dispatchDepartureEditor!.Text = row is null ? "" : GetRawOrValue(RawKey(row, "departure"), row.DepartureSeconds.ToString("R", CultureInfo.InvariantCulture));
            dispatchDirectionEditor!.ItemsSource = Enum.GetValues<TrainDirection>();
            dispatchDirectionEditor.SelectedItem = row?.Direction;
            dispatchServiceEditor.SelectedItem = FindChoice(dispatchServiceEditor.ItemsSource, row?.ServiceType);
            dispatchVehicleEditor.SelectedItem = FindChoice(dispatchVehicleEditor.ItemsSource, row?.VehicleType);
            dispatchPatternEditor.SelectedItem = FindChoice(dispatchPatternEditor.ItemsSource, row?.StopPattern);
            dispatchOriginEditor.SelectedItem = FindChoice(dispatchOriginEditor.ItemsSource, row?.OriginPlatform);
            dispatchContinuationEditor.SelectedItem = FindChoice(dispatchContinuationEditor.ItemsSource, row?.ContinuationServiceRunId) ?? dispatchContinuationEditor.Items.OfType<OperationReferenceChoice>().FirstOrDefault(item => item.Id is null);
            dispatchContinueEditor!.IsChecked = row?.ContinueAfterTerminal == true;
            dispatchVehicleIdEditor!.Text = row?.VehicleId ?? "";
            dispatchServiceRunIdEditor!.Text = row?.ServiceRunId ?? "";
            dispatchReferences!.Text = row is null ? "請選取班表列。" : DescribeDispatchReferences(row);
        }
        finally { updatingOperationEditors = false; }
    }

    private bool CommitDispatchDetail()
    {
        if (selectedDispatchRow is null || dispatchDepartureEditor is null) return true;
        var errors = new List<string>();
        var key = RawKey(selectedDispatchRow, "departure");
        if (!TryCommitNumber(dispatchDepartureEditor, key, value => selectedDispatchRow.DepartureSeconds = value, "發車秒數", errors)) { }
        if (dispatchDirectionEditor?.SelectedItem is TrainDirection direction) selectedDispatchRow.Direction = direction;
        selectedDispatchRow.ServiceType = (dispatchServiceEditor?.SelectedItem as OperationReferenceChoice)?.Id ?? dispatchServiceEditor?.Text ?? "";
        selectedDispatchRow.VehicleType = (dispatchVehicleEditor?.SelectedItem as OperationReferenceChoice)?.Id ?? dispatchVehicleEditor?.Text ?? "";
        selectedDispatchRow.StopPattern = (dispatchPatternEditor?.SelectedItem as OperationReferenceChoice)?.Id ?? dispatchPatternEditor?.Text ?? "";
        selectedDispatchRow.OriginPlatform = (dispatchOriginEditor?.SelectedItem as OperationReferenceChoice)?.Id ?? dispatchOriginEditor?.Text ?? "";
        selectedDispatchRow.VehicleId = dispatchVehicleIdEditor?.Text ?? "";
        selectedDispatchRow.ServiceRunId = dispatchServiceRunIdEditor?.Text ?? "";
        selectedDispatchRow.ContinueAfterTerminal = dispatchContinueEditor?.IsChecked == true;
        selectedDispatchRow.ContinuationServiceRunId = (dispatchContinuationEditor?.SelectedItem as OperationReferenceChoice)?.Id ?? "";
        if (errors.Count == 0)
        {
            if (dispatchReferences is not null) dispatchReferences.Text = DescribeDispatchReferences(selectedDispatchRow);
            return true;
        }
        operationEditorErrors.AddRange(errors.Select(error => new ProjectValidationMessage(ProjectValidationSeverity.Error, error, selectedDispatchRow.ServiceRunId, ProjectValidationTargetKind.ManualTimetable)));
        return false;
    }

    /// <summary>
    /// Commits the two page-level dispatch selectors together with the row
    /// editors.  This is deliberately separate from <see cref="CommitDispatchModes"/>
    /// so the page hook can persist a selector change without recursively
    /// rebuilding the page while a navigation commit is in progress.
    /// </summary>
    private bool CommitDispatchModeEditors()
    {
        if (dispatchModeEditor?.SelectedItem is not OperationReferenceChoice mode
            || dispatchAssignmentEditor?.SelectedItem is not OperationReferenceChoice assignment)
            return true;

        if (!Enum.TryParse<DispatchPlanningMode>(mode.Id, out var parsedMode)
            || !Enum.TryParse<VehicleAssignmentMode>(assignment.Id, out var parsedAssignment))
        {
            operationEditorErrors.Add(new ProjectValidationMessage(
                ProjectValidationSeverity.Error,
                "班表啟用模式或車輛分配模式無效，請重新選擇。",
                "Dispatch",
                ProjectValidationTargetKind.Unknown));
            return false;
        }

        var current = state.Draft.Dispatch;
        if (current.ActiveMode == parsedMode && current.VehicleAssignmentMode == parsedAssignment) return true;
        state.Replace(state.Draft with
        {
            Dispatch = current with { ActiveMode = parsedMode, VehicleAssignmentMode = parsedAssignment }
        });
        return true;
    }

    private void CommitDispatchModes()
    {
        if (!TryCommitOperationsPageEditors()) return;
        ReloadViewModels();
        ShowDispatchPlanningPage();
    }

    private string DescribeDispatchReferences(ManualTimetableEditorViewModel row)
    {
        var missing = new List<string>();
        var service = state.Draft.ServiceTypes.FirstOrDefault(item => IdEquals(item.Id, row.ServiceType));
        var vehicle = state.Draft.VehicleTypes.FirstOrDefault(item => IdEquals(item.Id, row.VehicleType));
        var pattern = state.Draft.StopPatterns.FirstOrDefault(item => IdEquals(item.Id, row.StopPattern));
        if (service is null && !string.IsNullOrWhiteSpace(row.ServiceType)) missing.Add($"缺少服務：{row.ServiceType}");
        if (vehicle is null && !string.IsNullOrWhiteSpace(row.VehicleType)) missing.Add($"缺少車型：{row.VehicleType}");
        if (pattern is null && !string.IsNullOrWhiteSpace(row.StopPattern)) missing.Add($"缺少停站模式：{row.StopPattern}");
        var route = state.Draft.DirectionRouteBindings.FirstOrDefault(item => item.Direction == row.Direction);
        if (route is null) missing.Add($"缺少{UiDisplayText.Enum(row.Direction)}服務路徑綁定");
        var originCandidates = route is null
            ? []
            : state.Draft.ServiceRoutes.FirstOrDefault(item => IdEquals(item.ServiceRouteId, route.ServiceRouteId))?
                .Stops.FirstOrDefault()?.CandidatePlatformIds ?? [];
        if (!string.IsNullOrWhiteSpace(row.OriginPlatform)
            && !originCandidates.Any(item => IdEquals(item, row.OriginPlatform)))
            missing.Add($"缺少{UiDisplayText.Enum(row.Direction)}起點月台：{row.OriginPlatform}");
        var continuation = string.IsNullOrWhiteSpace(row.ContinuationServiceRunId)
            ? null
            : (state.Draft.Dispatch.ManualTimetableRows ?? []).FirstOrDefault(item => IdEquals(item.ServiceRunId, row.ContinuationServiceRunId));
        if (!string.IsNullOrWhiteSpace(row.ContinuationServiceRunId) && continuation is null) missing.Add($"缺少接續車次：{row.ContinuationServiceRunId}");
        var chain = $"引用鏈：{service?.DisplayName ?? row.ServiceType} → {vehicle?.DisplayName ?? row.VehicleType} → {pattern?.DisplayName ?? row.StopPattern} → {route?.ServiceRouteId ?? "（未綁定）"}";
        return missing.Count == 0 ? chain : chain + Environment.NewLine + string.Join(Environment.NewLine, missing);
    }

    private ComboBox CreateReferenceCombo(string name, IEnumerable<OperationReferenceChoice> choices, string? selectedId)
    {
        var combo = new ComboBox { ItemsSource = choices.ToArray(), DisplayMemberPath = nameof(OperationReferenceChoice.Label), Margin = new Thickness(0, 0, 0, 3), MinWidth = 220 };
        AutomationProperties.SetName(combo, name);
        combo.SelectedItem = combo.Items.OfType<OperationReferenceChoice>().FirstOrDefault(item => IdEquals(item.Id, selectedId));
        return combo;
    }

    private static Button CreateNamedButton(string label, string name, RoutedEventHandler handler, bool secondary)
    {
        var button = CreateButton(label, handler, secondary);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static ScrollViewer WrapOperationsPage(UIElement content)
    {
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // The page root is a StackPanel and contains nested editors. Use
            // physical scrolling so the complete detail editor remains
            // reachable at 980x640 instead of being measured as one logical
            // item by the outer viewer.
            CanContentScroll = false
        };
        AutomationProperties.SetName(scroll, "營運頁整頁捲動區");
        return scroll;
    }

    private static void SetButtonAutomationName(DependencyObject root, string content, string name)
    {
        var button = root.Descendants().OfType<Button>().FirstOrDefault(item =>
            string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal));
        if (button is not null) AutomationProperties.SetName(button, name);
    }

    private TextBox AddLabeledTextBox(Grid parent, int row, string label, string automationName)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(caption, row); Grid.SetColumn(caption, 0); parent.Children.Add(caption);
        var box = new TextBox { Margin = new Thickness(0, 2, 0, 2) };
        AutomationProperties.SetName(box, automationName);
        Grid.SetRow(box, row); Grid.SetColumn(box, 1); parent.Children.Add(box);
        operationEditorTextBoxes.Add(box);
        return box;
    }

    private void AddLabeledCombo(Grid parent, int row, string label, System.Collections.IEnumerable choices, out ComboBox combo, string automationName)
    {
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(caption, row); Grid.SetColumn(caption, 0); parent.Children.Add(caption);
        combo = new ComboBox { ItemsSource = choices.Cast<object>().ToArray(), Margin = new Thickness(0, 2, 0, 2), MinWidth = 220 };
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new OperationChoiceDisplayConverter() });
        template.VisualTree = text;
        combo.ItemTemplate = template;
        AutomationProperties.SetName(combo, automationName);
        Grid.SetRow(combo, row); Grid.SetColumn(combo, 1); parent.Children.Add(combo);
    }

    private static void AddModeLabel(Grid parent, int column, string label)
    {
        var block = new TextBlock { Text = label, Margin = new Thickness(0, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(block, column); parent.Children.Add(block);
    }

    private IEnumerable<OperationReferenceChoice> BuildPatternChoices(string? selectedId, bool includeBlank)
    {
        var choices = state.Draft.StopPatterns.Select(item => new OperationReferenceChoice(item.Id, $"{item.DisplayName}（{item.Id}）")).ToList();
        if (includeBlank) choices.Insert(0, new OperationReferenceChoice(null, "（沿用服務／全域預設）"));
        return IncludeMissing(choices, selectedId);
    }

    private IEnumerable<OperationReferenceChoice> BuildVehicleChoices(string? selectedId, bool includeBlank)
    {
        var choices = state.Draft.VehicleTypes.Select(item => new OperationReferenceChoice(item.Id, $"{item.DisplayName}（{item.Id}）")).ToList();
        if (includeBlank) choices.Insert(0, new OperationReferenceChoice(null, "（沿用自動分配）"));
        return IncludeMissing(choices, selectedId);
    }

    private IEnumerable<OperationReferenceChoice> BuildDispatchRunChoices(string? selectedId)
    {
        var choices = new List<OperationReferenceChoice> { new(null, "（不指定）") };
        choices.AddRange((state.Draft.Dispatch.ManualTimetableRows ?? []).Select(row => new OperationReferenceChoice(row.ServiceRunId, $"{row.ServiceRunId ?? "（未指定車次）"} · {row.ServiceTypeId}")));
        return IncludeMissing(choices, selectedId);
    }

    private IEnumerable<OperationReferenceChoice> BuildOriginChoices(TrainDirection direction, string? selectedId)
    {
        var routeId = state.Draft.DirectionRouteBindings.FirstOrDefault(item => item.Direction == direction)?.ServiceRouteId;
        var route = state.Draft.ServiceRoutes.FirstOrDefault(item => IdEquals(item.ServiceRouteId, routeId));
        // A dispatch origin is an origin of the selected direction, so only
        // candidates on that route's first stop are valid.  Including every
        // route stop would make downstream platforms look like start choices.
        var platformIds = route?.Stops.FirstOrDefault()?.CandidatePlatformIds ?? [];
        var choices = platformIds.Select(id => new OperationReferenceChoice(id, DescribePlatform(id))).ToArray();
        return IncludeMissing(choices, selectedId);
    }

    private static IEnumerable<OperationReferenceChoice> IncludeMissing(IEnumerable<OperationReferenceChoice> choices, string? selectedId)
    {
        var list = choices.ToList();
        if (!string.IsNullOrWhiteSpace(selectedId) && !list.Any(item => IdEquals(item.Id, selectedId)))
            list.Add(new OperationReferenceChoice(selectedId, $"缺少：{selectedId}"));
        return list;
    }

    private static OperationReferenceChoice? FindChoice(object? itemsSource, string? id) =>
        itemsSource is IEnumerable<OperationReferenceChoice> choices ? choices.FirstOrDefault(item => IdEquals(item.Id, id)) : null;

    private string GetRawOrValue(string key, string value) => operationRawValues.GetValueOrDefault(key, value);

    private bool TryCommitNullableNumber(TextBox box, string key, Action<double?> setter, string field, List<string> errors)
    {
        var text = box.Text.Trim();
        if (text.Length == 0) { operationRawValues.Remove(key); setter(null); return true; }
        if (TryParseOperationFinite(text, out var value)) { operationRawValues.Remove(key); setter(value); return true; }
        operationRawValues[key] = box.Text;
        errors.Add($"{field}必須是有限數值，原輸入「{box.Text}」已保留。");
        return false;
    }

    private bool TryCommitNumber(TextBox box, string key, Action<double> setter, string field, List<string> errors)
    {
        if (TryParseOperationFinite(box.Text.Trim(), out var value)) { operationRawValues.Remove(key); setter(value); return true; }
        operationRawValues[key] = box.Text;
        errors.Add($"{field}必須是有限數值，原輸入「{box.Text}」已保留。");
        return false;
    }

    private static bool TryParseOperationFinite(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        && double.IsFinite(value);

    private static string RawKey(StopPatternMatrixCell cell, string field) => $"pattern:{cell.PatternId}:{cell.StationId}:{field}";
    private static string RawKey(ManualTimetableEditorViewModel row, string field) => $"dispatch:{row.ServiceRunId}:{field}";

    private sealed record OperationReferenceChoice(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class OperationChoiceDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            OperationReferenceChoice choice => choice.Label,
            Enum enumValue => UiDisplayText.Enum(enumValue),
            _ => value?.ToString() ?? string.Empty
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    private sealed class OperationValueDisplayConverter(Func<object?, string> display) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => display(value);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    private sealed record StopPatternMatrixColumn(string PatternId, string Name)
    {
        public override string ToString() => Name;
    }
    private sealed record StopPatternMatrixCell(string PatternId, string StationId, StopPatternInstructionEditorViewModel? Instruction, string Display);
    private sealed record StopPatternMatrixRow(string StationId, string StationName, IReadOnlyDictionary<string, StopPatternMatrixCell> Cells)
    {
        public IReadOnlyDictionary<string, string> Values => Cells.ToDictionary(item => item.Key, item => item.Value.Display, StringComparer.OrdinalIgnoreCase);
    }
    private sealed class StopPatternEditorContext { }
}
