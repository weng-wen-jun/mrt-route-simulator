using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Schema 8 的 topology-first 工作區。所有 DataGrid 繫結的都是 Editor ViewModel
/// draft；按「套用」才完整驗證及 commit，因此 Cancel 不會污染目前 project。
/// </summary>
internal enum ProjectWorkspacePage
{
    Project, QuickBuilder, Infrastructure, Operations, Dispatch, Simulation, Schematic, Results, Validation
}

internal sealed class TopologyEditorWindow : Window
{
    private readonly ProjectEditorState state;
    private readonly ProjectViewModel project;
    private readonly InfrastructureViewModel infrastructure = new();
    private readonly ServiceNetworkViewModel serviceNetwork = new();
    private readonly DispatchViewModel dispatch = new();
    private readonly SimulationViewModel simulation;
    private readonly ObservableCollection<QuickStationEditorViewModel> quickStations = [];
    private readonly ObservableCollection<ProjectValidationMessageViewModel> validationMessages = [];
    private readonly ContentControl workspace = new();
    private readonly TextBlock selectionDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox validationList = new();
    private readonly ListBox navigation = new();
    private readonly Dictionary<ProjectValidationTargetKind, List<ValidationEditorTarget>> validationTargets = [];
    private DataGrid? routeGrid;
    private DataGrid? traversalGrid;
    private DataGrid? routeStopGrid;
    private DataGrid? stopPatternGrid;
    private DataGrid? stopPatternInstructionGrid;
    private ServiceRouteEditorViewModel? selectedRoute;
    private StopPatternEditorViewModel? selectedStopPattern;
    private bool advancedMode;
    private readonly ProjectWorkspacePage initialPage;
    private ProjectWorkspacePage currentPage;
    private bool changingNavigation;

    public TopologyEditorWindow(TopologyProjectDocument document, ProjectWorkspacePage initialPage = ProjectWorkspacePage.Project)
    {
        ArgumentNullException.ThrowIfNull(document);
        this.initialPage = Enum.IsDefined(initialPage) ? initialPage : ProjectWorkspacePage.Project;
        state = ProjectDocumentMapper.CreateEditorState(document);
        project = new ProjectViewModel(state);
        simulation = new SimulationViewModel(state);
        ReloadViewModels();

        Title = "專案工作區";
        Width = 1360;
        Height = 860;
        MinWidth = 980;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildShell();
    }

    public TopologyProjectDocument? Result { get; private set; }

    private UIElement BuildShell()
    {
        var root = new Grid { Background = Brushes.White };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(184) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

        navigation.Margin = new Thickness(10);
        navigation.BorderThickness = new Thickness(0);
        navigation.Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
        navigation.DisplayMemberPath = nameof(NavItem.Label);
        navigation.ItemsSource = new[]
        {
            new NavItem(ProjectWorkspacePage.Project, "專案", ShowProjectHome),
            new NavItem(ProjectWorkspacePage.QuickBuilder, "快速建立", ShowQuickBuilder),
            new NavItem(ProjectWorkspacePage.Infrastructure, "基礎設施", ShowInfrastructure),
            new NavItem(ProjectWorkspacePage.Operations, "營運", ShowOperations),
            new NavItem(ProjectWorkspacePage.Dispatch, "發車計畫", ShowDispatch),
            new NavItem(ProjectWorkspacePage.Simulation, "模擬設定", ShowSimulation),
            new NavItem(ProjectWorkspacePage.Schematic, "線路示意圖", ShowSchematic),
            new NavItem(ProjectWorkspacePage.Results, "結果", ShowResults),
            new NavItem(ProjectWorkspacePage.Validation, "驗證", ShowValidation)
        };
        navigation.SelectionChanged += (_, _) => OpenSelectedNavigationItem();
        navigation.SelectedIndex = (int)initialPage;
        Grid.SetColumn(navigation, 0);
        root.Children.Add(navigation);

        workspace.Margin = new Thickness(12, 12, 8, 12);
        Grid.SetColumn(workspace, 1);
        root.Children.Add(workspace);

        var right = new Grid { Margin = new Thickness(8, 12, 12, 12) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.Children.Add(new TextBlock { Text = "屬性", FontSize = 17, FontWeight = FontWeights.SemiBold });
        selectionDetails.Margin = new Thickness(0, 8, 0, 12);
        selectionDetails.Foreground = new SolidColorBrush(Color.FromRgb(65, 75, 95));
        Grid.SetRow(selectionDetails, 1);
        right.Children.Add(selectionDetails);
        var validationTitle = new TextBlock { Text = "驗證", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(validationTitle, 2);
        right.Children.Add(validationTitle);
        validationList.BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232));
        validationList.BorderThickness = new Thickness(1);
        validationList.ItemsSource = validationMessages;
        AttachValidationActivation(validationList);
        Grid.SetRow(validationList, 3);
        validationList.DisplayMemberPath = nameof(ProjectValidationMessageViewModel.DisplayMessage);
        right.Children.Add(validationList);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var advanced = new CheckBox { Content = "進階識別碼", IsChecked = advancedMode, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        advanced.Checked += (_, _) => advancedMode = true;
        advanced.Unchecked += (_, _) => advancedMode = false;
        actions.Children.Add(advanced);
        actions.Children.Add(CreateButton("驗證", (_, _) => RefreshValidation(), true));
        actions.Children.Add(CreateButton("套用", (_, _) => Apply(), false));
        actions.Children.Add(CreateButton("取消", (_, _) => DialogResult = false, true));
        Grid.SetRow(actions, 4);
        right.Children.Add(actions);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);
        return root;
    }

    private void Navigate(ProjectWorkspacePage page)
    {
        var item = navigation.Items.OfType<NavItem>().First(candidate => candidate.Page == page);
        if (ReferenceEquals(navigation.SelectedItem, item)) OpenSelectedNavigationItem();
        else navigation.SelectedItem = item;
    }

    private void OpenSelectedNavigationItem()
    {
        if (changingNavigation || navigation.SelectedItem is not NavItem item) return;
        try
        {
            item.Open();
            currentPage = item.Page;
        }
        catch (EditorValidationException exception)
        {
            SetValidationMessages(exception.Messages);
            RestoreNavigationSelection();
        }
        catch (SimulationValidationException exception)
        {
            ShowErrors(exception.Errors);
            RestoreNavigationSelection();
        }
        catch (Exception exception)
        {
            SetValidationMessages([ProjectEditorValidationService.CreateError(exception.Message, state.Draft)]);
            RestoreNavigationSelection();
        }
    }

    private void RestoreNavigationSelection()
    {
        changingNavigation = true;
        navigation.SelectedItem = navigation.Items.OfType<NavItem>().FirstOrDefault(candidate => candidate.Page == currentPage);
        changingNavigation = false;
    }

    private void ShowProjectHome()
    {
        CommitTableDrafts();
        var document = state.Draft;
        var topology = document.Topology;
        var validation = ProjectEditorValidationService.Validate(document);
        var panel = NewPage("專案", "首頁只顯示摘要；請由左側進入各個格式版本 8 工作區編輯。");
        panel.Children.Add(SummaryGrid(
            ("專案名稱", project.ProjectName), ("專案格式版本", document.SchemaVersion.ToString(CultureInfo.InvariantCulture)),
            ("模擬引擎", "V2 拓撲原生"), ("軌道節點數", topology.Nodes.Count.ToString(CultureInfo.InvariantCulture)),
            ("軌道區段數", topology.Edges.Count.ToString(CultureInfo.InvariantCulture)),
            ("車站／月台數", $"{topology.Stations.Count}／{topology.Platforms.Count}"),
            ("設施數", $"{topology.TurnbackFacilities.Count + topology.PassingFacilities.Count}"),
            ("服務路徑數", document.ServiceRoutes.Length.ToString(CultureInfo.InvariantCulture)),
            ("服務／車型數", $"{document.ServiceTypes.Length}／{document.VehicleTypes.Length}"),
            ("發車班次數", CalculateDispatchRunCount(document).ToString(CultureInfo.InvariantCulture)),
            ("拓撲驗證", validation.Any(item => item.Severity == ProjectValidationSeverity.Error) ? "有錯誤" : "通過")));
        workspace.Content = panel;
        RefreshValidation();
    }

    private void ShowQuickBuilder()
    {
        CommitTableDrafts();
        if (quickStations.Count == 0) PopulateQuickStations();
        var panel = NewPage("快速建立線性路線", "這裡是一次性的快速建線輸入。按「建立格式版本 8 拓撲」後，後續編輯的唯一權威是軌道節點與軌道區段。 ");
        panel.Children.Add(new TextBlock
        {
            Text = "☑ 建立雙線     ☑ 建立上下行月台     ☑ 建立上下行服務路徑\n第一版固定輸出上述完整拓撲，避免留下不可執行的半成品。",
            Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap
        });
        var grid = CreateGrid(quickStations);
        panel.Children.Add(grid);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(CreateButton("新增車站", (_, _) => quickStations.Add(new QuickStationEditorViewModel { Id = NextId("ST"), Name = "新車站", DwellSeconds = 30 }), true));
        actions.Children.Add(CreateButton("刪除車站", (_, _) => { if (grid.SelectedItem is QuickStationEditorViewModel row) quickStations.Remove(row); }, true));
        actions.Children.Add(CreateButton("建立格式版本 8 拓撲", (_, _) => BuildQuickTopology(), false));
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "依參考圖建立站場", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "選擇站型後重新建立三站示範草稿，包含月台、實體進路與派車。會取代工作區目前草稿；按取消可保留原專案。尺寸、車型、停站及派車時間可在其他分頁調整。", TextWrapping = TextWrapping.Wrap });
        var stationKind = new ComboBox { ItemsSource = Enum.GetValues<StationLayoutTemplateKind>().Select(StationLayoutTemplateService.Name).ToArray(), SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 4), MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(stationKind);
        panel.Children.Add(CreateButton("以此站型重新起稿", (_, _) =>
        {
            RunEdit(() => StationLayoutTemplateService.Build(Enum.GetValues<StationLayoutTemplateKind>()[stationKind.SelectedIndex]));
        }, false));
        workspace.Content = panel;
    }

    private void ShowInfrastructure()
    {
        CommitTableDrafts();
        var panel = NewPage("基礎設施", "所有實體資料都由節點、軌道區段、設施、月台與區段內偏移量表達。\n接軌側 A/B 是同一節點的兩側，行進須由一側進、另一側出；調整示意位置不會改變側別。刪除被引用物件會列出相依項目。");
        var tabs = new TabControl();
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.Node, "軌道節點", infrastructure.Nodes, AddNode, DeleteSelectedNode));
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.Edge, "軌道區段", infrastructure.Edges, AddEdge, DeleteSelectedEdge));
        var facilityTab = EditorTab(ProjectValidationTargetKind.TurnbackFacility, "設施", infrastructure.Facilities, ShowFacilityMenu, DeleteSelectedFacility, true);
        tabs.Items.Add(facilityTab);
        RegisterValidationTarget(ProjectValidationTargetKind.PassingFacility, validationTargets[ProjectValidationTargetKind.TurnbackFacility][0]);
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.Station, "車站", infrastructure.Stations, AddStation, DeleteSelectedStation));
        tabs.Items.Add(StationAndPlatformTab());
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.SpeedLimit, "速限", infrastructure.SpeedLimits, AddSpeedLimit, DeleteSelectedSpeedLimit));
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.Gradient, "坡度", infrastructure.Gradients, AddGradient, DeleteSelectedGradient));
        tabs.Items.Add(ResourceTab());
        panel.Children.Add(tabs);
        workspace.Content = panel;
    }

    private void ShowOperations()
    {
        CommitTableDrafts();
        var panel = NewPage("營運", "車型、服務、服務路徑與停站模式都屬於格式版本 8 草稿；停站車站清單永遠來自服務路徑的停靠站，而非舊式里程排序。 ");
        var tabs = new TabControl();
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.VehicleType, "車型", serviceNetwork.VehicleTypes, AddVehicleType, DeleteSelectedVehicleType));
        tabs.Items.Add(EditorTab(ProjectValidationTargetKind.ServiceType, "服務類型", serviceNetwork.ServiceTypes, AddServiceType, DeleteSelectedServiceType));
        var routeTab = new TabItem { Header = "服務路徑", Content = BuildServiceRouteEditor() };
        tabs.Items.Add(routeTab);
        if (routeGrid is not null) RegisterValidationTarget(ProjectValidationTargetKind.ServiceRoute, new ValidationEditorTarget(routeGrid, [routeTab]));
        var stopPatternTab = new TabItem { Header = "停站模式", Content = BuildStopPatternEditor() };
        tabs.Items.Add(stopPatternTab);
        if (stopPatternGrid is not null) RegisterValidationTarget(ProjectValidationTargetKind.StopPattern, new ValidationEditorTarget(stopPatternGrid, [stopPatternTab]));
        panel.Children.Add(tabs);
        workspace.Content = panel;
    }

    private UIElement BuildServiceRouteEditor()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.48, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.52, GridUnitType.Star) });
        var top = new DockPanel { Margin = new Thickness(8) };
        var routeActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        routeActions.Children.Add(CreateButton("新增服務路徑", (_, _) => AddServiceRoute(), true));
        routeActions.Children.Add(CreateButton("刪除", (_, _) => DeleteSelectedServiceRoute(), true));
        routeActions.Children.Add(CreateButton("設為下行進路", (_, _) => BindSelectedRoute(TrainDirection.Outbound), true));
        routeActions.Children.Add(CreateButton("設為上行進路", (_, _) => BindSelectedRoute(TrainDirection.Inbound), true));
        DockPanel.SetDock(routeActions, Dock.Top);
        top.Children.Add(routeActions);
        routeGrid = CreateGrid(serviceNetwork.Routes);
        routeGrid.SelectionChanged += (_, _) => { selectedRoute = routeGrid.SelectedItem as ServiceRouteEditorViewModel; ShowRouteDetail(); };
        top.Children.Add(routeGrid);
        Grid.SetRow(top, 0);
        root.Children.Add(top);
        var detail = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        detail.Children.Add(new TextBlock { Text = "依序通過的軌道區段／路線停靠站", FontSize = 15, FontWeight = FontWeights.SemiBold });
        traversalGrid = CreateGrid(new ObservableCollection<TraversalEditorViewModel>());
        traversalGrid.Height = 180;
        detail.Children.Add(traversalGrid);
        var traversalActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 4) };
        traversalActions.Children.Add(CreateButton("新增通過區段", (_, _) => AddTraversal(), true));
        traversalActions.Children.Add(CreateButton("刪除", (_, _) => RemoveTraversal(), true));
        traversalActions.Children.Add(CreateButton("上移", (_, _) => MoveTraversal(-1), true));
        traversalActions.Children.Add(CreateButton("下移", (_, _) => MoveTraversal(1), true));
        traversalActions.Children.Add(CreateButton("自動建立路徑", (_, _) => BuildRoutePath(), false));
        detail.Children.Add(traversalActions);
        detail.Children.Add(new TextBlock { Text = "路線停靠站（候選月台只會列出這條路線通過區段上的實體月台）", Margin = new Thickness(0, 4, 0, 4), FontWeight = FontWeights.SemiBold });
        routeStopGrid = CreateGrid(new ObservableCollection<ServiceRouteStopEditorViewModel>());
        routeStopGrid.Height = 110;
        detail.Children.Add(routeStopGrid);
        var stopActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        stopActions.Children.Add(CreateButton("新增停靠站", (_, _) => AddRouteStop(), true));
        stopActions.Children.Add(CreateButton("刪除停靠站", (_, _) => RemoveRouteStop(), true));
        stopActions.Children.Add(CreateButton("檢查候選月台", (_, _) => ValidateRouteStopCandidates(), true));
        detail.Children.Add(stopActions);
        Grid.SetRow(detail, 1);
        root.Children.Add(detail);
        if (serviceNetwork.Routes.Count > 0) routeGrid.SelectedIndex = 0;
        return root;
    }

    private UIElement BuildStopPatternEditor()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.4, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.6, GridUnitType.Star) });
        var top = new DockPanel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateButton("從服務路徑建立", (_, _) => AddStopPattern(), true));
        actions.Children.Add(CreateButton("刪除", (_, _) => DeleteSelectedStopPattern(), true));
        DockPanel.SetDock(actions, Dock.Top);
        top.Children.Add(actions);
        stopPatternGrid = CreateGrid(serviceNetwork.StopPatterns);
        stopPatternGrid.SelectionChanged += (_, _) => { selectedStopPattern = stopPatternGrid.SelectedItem as StopPatternEditorViewModel; ShowStopPatternDetail(); };
        top.Children.Add(stopPatternGrid);
        Grid.SetRow(top, 0); root.Children.Add(top);
        var detail = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        detail.Children.Add(new TextBlock { Text = "停站指令（車站只能來自所選服務路徑的停靠站）", FontWeight = FontWeights.SemiBold });
        stopPatternInstructionGrid = CreateGrid(new ObservableCollection<StopPatternInstructionEditorViewModel>());
        stopPatternInstructionGrid.Height = 205; detail.Children.Add(stopPatternInstructionGrid);
        var instructionActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        instructionActions.Children.Add(CreateButton("新增車站指令", (_, _) => AddStopPatternInstruction(), true));
        instructionActions.Children.Add(CreateButton("刪除指令", (_, _) => { if (selectedStopPattern is not null && stopPatternInstructionGrid?.SelectedItem is StopPatternInstructionEditorViewModel row) selectedStopPattern.Instructions.Remove(row); }, true));
        detail.Children.Add(instructionActions);
        Grid.SetRow(detail, 1); root.Children.Add(detail);
        if (serviceNetwork.StopPatterns.Count > 0) stopPatternGrid.SelectedIndex = 0;
        return root;
    }

    private void ShowDispatch()
    {
        CommitTableDrafts();
        var panel = NewPage("發車計畫", "起點只能指定服務路徑上的實體月台。每筆班距或手動班表可選停站模式、車型與續行設定，不使用全線里程。 ");
        var tabs = new TabControl();
        tabs.Items.Add(DispatchTab(ProjectValidationTargetKind.HeadwayPlan, "班距計畫", dispatch.HeadwayPlans, AddHeadwayPlan, EditSelectedHeadwayPlan, DeleteSelectedHeadwayPlan));
        tabs.Items.Add(DispatchTab(ProjectValidationTargetKind.ManualTimetable, "手動班表", dispatch.ManualRows, AddManualTimetableRow, EditSelectedManualRow, DeleteSelectedManualRow));
        tabs.Items.Add(new TabItem { Header = "展開預覽", Content = CreateGrid(dispatch.Runs, true) });
        panel.Children.Add(tabs);
        workspace.Content = panel;
    }

    private void ShowSimulation()
    {
        CommitTableDrafts();
        var panel = NewPage("模擬設定", "格式版本 8 專案通過驗證後可直接送入 V2 模擬世界，不需建立相容路線。 ");
        panel.Children.Add(SummaryGrid(("專案格式", simulation.Schema), ("引擎", simulation.Engine), ("營運模式", simulation.Profile),
            ("移動閉塞", UiDisplayText.Enum(state.Draft.Simulation.MovingBlockMode)), ("起始時鐘", TimeSpan.FromSeconds(state.Draft.Simulation.StartClockSeconds).ToString("hh\\:mm\\:ss"))));
        panel.Children.Add(CreateButton("調整模擬與安全參數…", (_, _) => EditSimulationSettings(), false));
        workspace.Content = panel;
    }

    private void EditSimulationSettings()
    {
        var settings = state.Draft.Simulation;
        var operational = state.Draft.Operations;
        var answer = Ask("模擬與安全參數", ("起始時鐘（秒）", settings.StartClockSeconds.ToString(CultureInfo.InvariantCulture)),
            ("播放倍率", settings.PlaybackSpeed.ToString(CultureInfo.InvariantCulture)),
            ("控制反應時間（秒）", operational.ControlReactionTimeSeconds.ToString(CultureInfo.InvariantCulture)),
            ("安全餘量（m）", operational.SafetyMarginMeters.ToString(CultureInfo.InvariantCulture)),
            ("絕對最小間隔（m）", operational.AbsoluteMinimumGapMeters.ToString(CultureInfo.InvariantCulture)));
        if (answer is null) return;
        RunEdit(() =>
        {
            var candidate = state.Draft with
            {
                Simulation = settings with { StartClockSeconds = Parse(answer["起始時鐘（秒）"], "起始時鐘"), PlaybackSpeed = Parse(answer["播放倍率"], "播放倍率") },
                Operations = operational with { ControlReactionTimeSeconds = Parse(answer["控制反應時間（秒）"], "控制反應時間"),
                    SafetyMarginMeters = Parse(answer["安全餘量（m）"], "安全餘量"), AbsoluteMinimumGapMeters = Parse(answer["絕對最小間隔（m）"], "絕對最小間隔") }
            };
            TopologyProjectFormat.Validate(candidate);
            return candidate;
        });
        ShowSimulation();
    }

    private void BindSelectedRoute(TrainDirection direction)
    {
        if (selectedRoute is null) return;
        var routeId = selectedRoute.Id;
        RunEdit(() =>
        {
            var route = state.Draft.ServiceRoutes.Single(r => IdEquals(r.ServiceRouteId, routeId));
            var origin = route.Stops.FirstOrDefault()?.CandidatePlatformIds.FirstOrDefault()
                ?? throw new SimulationValidationException(["所選進路沒有起始月台。"]);
            var candidate = state.Draft with
            {
                DirectionRouteBindings = state.Draft.DirectionRouteBindings.Where(b => b.Direction != direction)
                    .Append(new TopologyDirectionRouteBinding(direction, routeId)).ToArray(),
                Dispatch = state.Draft.Dispatch with
                {
                    SimpleHeadwayPlans = state.Draft.Dispatch.SimpleHeadwayPlans?.Select(p => p.Direction == direction ? p with { OriginPlatformId = origin } : p).ToArray(),
                    ManualTimetableRows = state.Draft.Dispatch.ManualTimetableRows?.Select(p => p.Direction == direction ? p with { OriginPlatformId = origin } : p).ToArray()
                }
            };
            TopologyProjectFormat.CreateRuntime(candidate);
            return candidate;
        });
    }

    private void ShowSchematic()
    {
        CommitTableDrafts();
        var panel = NewPage("路線示意圖", "自動排版僅是介面中繼資料：正線水平、支線自動偏移；不會進入列車物理運算。 ");
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(248, 250, 253)), Height = 520, ClipToBounds = true };
        DrawSchematic(canvas);
        canvas.SizeChanged += (_, _) => DrawSchematic(canvas);
        panel.Children.Add(new ScrollViewer { Content = canvas, MaxHeight = 520,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        panel.Children.Add(CreateButton("自動排版", (_, _) => DrawSchematic(canvas), true));
        workspace.Content = panel;
    }

    private void ShowResults()
    {
        CommitTableDrafts();
        var panel = NewPage("結果", "此工作區只編輯格式版本 8 專案；套用後主視窗會以同一份拓撲直接建立 V2 模擬世界，並顯示時刻表、區間統計與時間－里程圖。 ");
        panel.Children.Add(SummaryGrid(
            ("可執行服務路徑", state.Draft.ServiceRoutes.Length.ToString(CultureInfo.InvariantCulture)),
            ("已設定發車班次", CalculateDispatchRunCount(state.Draft).ToString(CultureInfo.InvariantCulture)),
            ("執行前驗證", ProjectEditorValidationService.Validate(state.Draft).Any(item => item.Severity == ProjectValidationSeverity.Error) ? "有錯誤" : "通過"),
            ("列車位置權威", "軌道區段編號＋偏移量")));
        workspace.Content = panel;
    }

    private void ShowValidation()
    {
        CommitTableDrafts();
        var panel = NewPage("驗證", "編輯期間允許暫時無效；但儲存與執行模擬前必須沒有錯誤。按一下驗證訊息或使用 Enter／空白鍵可前往目標。 ");
        var list = new ListBox { ItemsSource = validationMessages, Height = 480, DisplayMemberPath = nameof(ProjectValidationMessageViewModel.DisplayMessage) };
        AttachValidationActivation(list);
        panel.Children.Add(list);
        workspace.Content = panel;
        RefreshValidation();
    }

    private void OpenValidationTarget(ProjectValidationMessage message)
    {
        var kind = message.TargetKind == ProjectValidationTargetKind.Unknown
            ? InferTargetKind(message.TargetId)
            : message.TargetKind;
        var page = PageFor(kind);
        Navigate(page);
        if (currentPage != page) return;

        if (!validationTargets.TryGetValue(kind, out var targets) || targets.Count == 0)
        {
            UpdateValidationDetails(message);
            return;
        }

        var target = string.IsNullOrWhiteSpace(message.TargetId)
            ? targets[0]
            : targets.FirstOrDefault(candidate => candidate.Grid.Items.Cast<object>().Any(row => RowMatchesTarget(row, message.TargetId))) ?? targets[0];
        SelectTabs(target.Tabs);

        var row = string.IsNullOrWhiteSpace(message.TargetId)
            ? null
            : target.Grid.Items.Cast<object>().FirstOrDefault(candidate => RowMatchesTarget(candidate, message.TargetId));
        if (row is not null)
        {
            target.Grid.SelectedItem = row;
            target.Grid.ScrollIntoView(row);
        }

        FocusValidationTarget(target.Grid, row, message.FieldName);
        UpdateValidationDetails(message);
    }

    private void AttachValidationActivation(ListBox list)
    {
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ProjectValidationMessageViewModel item) UpdateValidationDetails(item.Source);
        };
        list.PreviewMouseLeftButtonUp += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(list, source) is ListBoxItem
                && list.SelectedItem is ProjectValidationMessageViewModel item)
            {
                OpenValidationTarget(item.Source);
                args.Handled = true;
            }
        };
        list.KeyDown += (_, args) =>
        {
            if ((args.Key == Key.Enter || args.Key == Key.Space)
                && list.SelectedItem is ProjectValidationMessageViewModel item)
            {
                OpenValidationTarget(item.Source);
                args.Handled = true;
            }
        };
    }

    private void UpdateValidationDetails(ProjectValidationMessage message)
    {
        var details = new List<string> { UiDisplayText.ValidationMessage(message.Message) };
        if (!string.IsNullOrWhiteSpace(message.TargetId)) details.Add($"目標：{message.TargetId}");
        if (!string.IsNullOrWhiteSpace(message.FieldName)) details.Add($"欄位：{UiDisplayText.Header(message.FieldName)}");
        selectionDetails.Text = string.Join(Environment.NewLine + Environment.NewLine, details);
    }

    private void RegisterValidationTarget(ProjectValidationTargetKind kind, ValidationEditorTarget target, bool append = false)
    {
        if (!append || !validationTargets.TryGetValue(kind, out var values))
        {
            validationTargets[kind] = [target];
            return;
        }

        values.Add(target);
    }

    private static ProjectWorkspacePage PageFor(ProjectValidationTargetKind kind) => kind switch
    {
        ProjectValidationTargetKind.Node or ProjectValidationTargetKind.Edge or ProjectValidationTargetKind.Station
            or ProjectValidationTargetKind.Platform or ProjectValidationTargetKind.Resource or ProjectValidationTargetKind.SpeedLimit
            or ProjectValidationTargetKind.Gradient or ProjectValidationTargetKind.TurnbackFacility
            or ProjectValidationTargetKind.PassingFacility => ProjectWorkspacePage.Infrastructure,
        ProjectValidationTargetKind.ServiceRoute or ProjectValidationTargetKind.StopPattern
            or ProjectValidationTargetKind.VehicleType or ProjectValidationTargetKind.ServiceType => ProjectWorkspacePage.Operations,
        ProjectValidationTargetKind.HeadwayPlan or ProjectValidationTargetKind.ManualTimetable => ProjectWorkspacePage.Dispatch,
        ProjectValidationTargetKind.Simulation => ProjectWorkspacePage.Simulation,
        _ => ProjectWorkspacePage.Project
    };

    private ProjectValidationTargetKind InferTargetKind(string? targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return ProjectValidationTargetKind.Unknown;
        if (state.Draft.Topology.Nodes.Any(item => IdEquals(item.NodeId, targetId))) return ProjectValidationTargetKind.Node;
        if (state.Draft.Topology.Edges.Any(item => IdEquals(item.TrackEdgeId, targetId))) return ProjectValidationTargetKind.Edge;
        if (state.Draft.Topology.Stations.Any(item => IdEquals(item.StationId, targetId))) return ProjectValidationTargetKind.Station;
        if (state.Draft.Topology.Platforms.Any(item => IdEquals(item.PlatformId, targetId))) return ProjectValidationTargetKind.Platform;
        if (state.Draft.Topology.Resources.Any(item => IdEquals(item.ResourceId, targetId))) return ProjectValidationTargetKind.Resource;
        if (state.Draft.Topology.SpeedLimits.Any(item => IdEquals(item.SpeedLimitId, targetId))) return ProjectValidationTargetKind.SpeedLimit;
        if (state.Draft.Topology.Gradients.Any(item => IdEquals(item.GradientId, targetId))) return ProjectValidationTargetKind.Gradient;
        if (state.Draft.Topology.TurnbackFacilities.Any(item => IdEquals(item.FacilityId, targetId))) return ProjectValidationTargetKind.TurnbackFacility;
        if (state.Draft.Topology.PassingFacilities.Any(item => IdEquals(item.FacilityId, targetId))) return ProjectValidationTargetKind.PassingFacility;
        if (state.Draft.ServiceRoutes.Any(item => IdEquals(item.ServiceRouteId, targetId))) return ProjectValidationTargetKind.ServiceRoute;
        if (state.Draft.StopPatterns.Any(item => IdEquals(item.Id, targetId))) return ProjectValidationTargetKind.StopPattern;
        if (state.Draft.VehicleTypes.Any(item => IdEquals(item.Id, targetId))) return ProjectValidationTargetKind.VehicleType;
        if (state.Draft.ServiceTypes.Any(item => IdEquals(item.Id, targetId))) return ProjectValidationTargetKind.ServiceType;
        return ProjectValidationTargetKind.Unknown;
    }

    private static bool RowMatchesTarget(object row, string targetId) => row switch
    {
        TrackNodeEditorViewModel item => IdEquals(item.Id, targetId),
        TrackEdgeEditorViewModel item => IdEquals(item.Id, targetId),
        FacilityEditorViewModel item => IdEquals(item.Id, targetId),
        StationEditorViewModel item => IdEquals(item.Id, targetId),
        PlatformEditorViewModel item => IdEquals(item.Id, targetId),
        TrackSpeedLimitEditorViewModel item => IdEquals(item.Id, targetId),
        GradientEditorViewModel item => IdEquals(item.Id, targetId),
        ResourceEditorViewModel item => IdEquals(item.Id, targetId),
        ServiceRouteEditorViewModel item => IdEquals(item.Id, targetId),
        StopPatternEditorViewModel item => IdEquals(item.Id, targetId),
        VehicleTypeEditorViewModel item => IdEquals(item.Id, targetId),
        ServiceTypeEditorViewModel item => IdEquals(item.Id, targetId),
        _ => false
    };

    private static bool IdEquals(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void SelectTabs(IEnumerable<TabItem> tabs)
    {
        foreach (var tab in tabs) tab.IsSelected = true;
    }

    private void FocusValidationTarget(DataGrid grid, object? row, string? fieldName)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            grid.Focus();
            if (row is null || string.IsNullOrWhiteSpace(fieldName)) return;
            var column = grid.Columns.FirstOrDefault(candidate => ColumnMatchesField(candidate, fieldName));
            if (column is null) return;
            grid.CurrentCell = new DataGridCellInfo(row, column);
            grid.ScrollIntoView(row, column);
            grid.UpdateLayout();
            if (column.GetCellContent(row) is FrameworkElement content) content.Focus();
        });
    }

    private static bool ColumnMatchesField(DataGridColumn column, string fieldName)
    {
        if (string.Equals(column.SortMemberPath, fieldName, StringComparison.Ordinal)) return true;
        return column is DataGridBoundColumn bound
            && bound.Binding is Binding binding
            && string.Equals(binding.Path?.Path, fieldName, StringComparison.Ordinal);
    }

    private TabItem EditorTab<T>(ProjectValidationTargetKind targetKind, string title, ObservableCollection<T> source, Action add, Action? delete, bool readOnly = false)
    {
        var view = CollectionViewSource.GetDefaultView(source);
        view.Filter = null;
        var grid = CreateGrid(view, readOnly);
        grid.SelectionChanged += (_, _) => selectionDetails.Text = DescribeSelection(grid.SelectedItem);
        var panel = new DockPanel { Margin = new Thickness(8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateButton(readOnly ? "新增設施…" : "新增", (_, _) => add(), !readOnly));
        if (delete is not null) actions.Children.Add(CreateButton("刪除", (_, _) => delete(), true));
        var search = new TextBox { Width = 150, Margin = new Thickness(6, 0, 0, 0), ToolTip = "依名稱、ID、類型或關聯搜尋" };
        search.TextChanged += (_, _) =>
        {
            var query = search.Text.Trim();
            view.Filter = string.IsNullOrWhiteSpace(query) ? null : item => MatchesSearch(item, query);
            view.Refresh();
        };
        actions.Children.Add(search);
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);
        panel.Children.Add(grid);
        var tab = new TabItem { Header = title, Content = panel };
        RegisterValidationTarget(targetKind, new ValidationEditorTarget(grid, [tab]));
        return tab;
    }

    private TabItem StationAndPlatformTab()
    {
        var grid = CreateGrid(infrastructure.Platforms);
        grid.SelectionChanged += (_, _) => selectionDetails.Text = DescribeSelection(grid.SelectedItem, "選取月台後可查看屬性。");
        var panel = new DockPanel { Margin = new Thickness(8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateButton("新增月台", (_, _) => AddPlatform(), true));
        actions.Children.Add(CreateButton("新增車站到軌道…", (_, _) => AddStationOnTracks(), false));
        actions.Children.Add(CreateButton("刪除", (_, _) => DeleteSelectedPlatform(), true));
        DockPanel.SetDock(actions, Dock.Top); panel.Children.Add(actions); panel.Children.Add(grid);
        var tab = new TabItem { Header = "車站與月台", Content = panel };
        RegisterValidationTarget(ProjectValidationTargetKind.Platform, new ValidationEditorTarget(grid, [tab]));
        return tab;
    }

    private TabItem ResourceTab()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(new TextBlock { Text = "設施精靈建立的自動資源由系統管理；一般操作只需查看，手動資源才需要自行新增或刪除。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var tabs = new TabControl();
        var automatic = ResourceListTab("系統自動建立", item => item.IsAutoGenerated, () => selectionDetails.Text = "自動資源由設施精靈建立。若要變更，請調整或刪除對應設施。", DeleteSelectedResource, true);
        var manual = ResourceListTab("手動建立", item => !item.IsAutoGenerated, AddResource, DeleteSelectedResource, false);
        tabs.Items.Add(automatic.Tab);
        tabs.Items.Add(manual.Tab);
        panel.Children.Add(tabs);
        var outerTab = new TabItem { Header = "資源", Content = panel };
        RegisterValidationTarget(ProjectValidationTargetKind.Resource, new ValidationEditorTarget(automatic.Grid, [outerTab, automatic.Tab]));
        RegisterValidationTarget(ProjectValidationTargetKind.Resource, new ValidationEditorTarget(manual.Grid, [outerTab, manual.Tab]), append: true);
        return outerTab;
    }

    private (TabItem Tab, DataGrid Grid) ResourceListTab(string title, Predicate<ResourceEditorViewModel> filter, Action add, Action delete, bool readOnly)
    {
        var view = new ListCollectionView(infrastructure.Resources) { Filter = item => item is ResourceEditorViewModel resource && filter(resource) };
        var grid = CreateGrid(view, readOnly);
        var panel = new DockPanel { Margin = new Thickness(8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateButton(readOnly ? "查看設施" : "新增", (_, _) => add(), !readOnly));
        actions.Children.Add(CreateButton("刪除", (_, _) => delete(), true));
        DockPanel.SetDock(actions, Dock.Top); panel.Children.Add(actions); panel.Children.Add(grid);
        return (new TabItem { Header = title, Content = panel }, grid);
    }

    private TabItem DispatchTab<T>(ProjectValidationTargetKind targetKind, string title, ObservableCollection<T> source, Action add, Action edit, Action delete)
    {
        var grid = CreateGrid(source, true);
        grid.SelectionChanged += (_, _) => selectionDetails.Text = DescribeSelection(grid.SelectedItem, "選取班表列後可查看屬性。");
        var panel = new DockPanel { Margin = new Thickness(8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        actions.Children.Add(CreateButton("新增", (_, _) => add(), true));
        actions.Children.Add(CreateButton("編輯…", (_, _) => edit(), true));
        actions.Children.Add(CreateButton("刪除", (_, _) => delete(), true));
        DockPanel.SetDock(actions, Dock.Top); panel.Children.Add(actions); panel.Children.Add(grid);
        var tab = new TabItem { Header = title, Content = panel };
        RegisterValidationTarget(targetKind, new ValidationEditorTarget(grid, [tab]));
        return tab;
    }

    private static StackPanel NewPage(string title, string description)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106)), Margin = new Thickness(0, 6, 0, 14) });
        return panel;
    }

    private DataGrid CreateGrid(System.Collections.IEnumerable items, bool readOnly = false)
    {
        var enumConverter = new TraditionalChineseEnumConverter();
        var grid = new DataGrid
        {
            ItemsSource = items, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false,
            IsReadOnly = readOnly, RowHeaderWidth = 0, Margin = new Thickness(0),
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1)
        };
        grid.AutoGeneratingColumn += (_, args) =>
        {
            if (!advancedMode && (args.PropertyName.Equals("Id", StringComparison.Ordinal) || args.PropertyName.Equals("BackingKind", StringComparison.Ordinal)))
                args.Cancel = true;
            else
            {
                args.Column.Header = UiDisplayText.Header(args.PropertyName);
                if (args.PropertyType.IsEnum && args.Column is DataGridTextColumn column && column.Binding is Binding binding)
                    binding.Converter = enumConverter;
            }
        };
        return grid;
    }

    private static bool MatchesSearch(object? item, string query) => item is not null && item.GetType().GetProperties()
        .Where(property => property.CanRead && property.PropertyType != typeof(bool))
        .Select(property => property.GetValue(item)?.ToString())
        .Any(value => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

    private static string DescribeSelection(object? item, string emptyMessage = "選取物件後可查看屬性。")
    {
        if (item is null) return emptyMessage;
        return string.Join(Environment.NewLine, item.GetType().GetProperties()
            .Where(property => property.CanRead && property.Name != "BackingKind")
            .Select(property =>
            {
                var value = property.GetValue(item);
                var display = value switch
                {
                    Enum enumValue => UiDisplayText.Enum(enumValue),
                    bool boolean => UiDisplayText.Boolean(boolean),
                    _ => value?.ToString() ?? "—"
                };
                return $"{UiDisplayText.Header(property.Name)}：{display}";
            }));
    }

    private static Button CreateButton(string label, RoutedEventHandler handler, bool secondary)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 7, 0),
            Background = secondary ? Brushes.White : new SolidColorBrush(Color.FromRgb(34, 92, 175)),
            Foreground = secondary ? new SolidColorBrush(Color.FromRgb(38, 51, 73)) : Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(164, 178, 202))
        };
        button.Click += handler;
        return button;
    }

    private static Grid SummaryGrid(params (string Label, string Value)[] values)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < values.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = values[index].Label, Margin = new Thickness(0, 5, 12, 5), Foreground = new SolidColorBrush(Color.FromRgb(90, 101, 122)) };
            var value = new TextBlock { Text = values[index].Value, Margin = new Thickness(0, 5, 0, 5), FontWeight = FontWeights.SemiBold };
            Grid.SetRow(label, index); Grid.SetRow(value, index); Grid.SetColumn(value, 1);
            grid.Children.Add(label); grid.Children.Add(value);
        }
        return grid;
    }

    private void ReloadViewModels()
    {
        infrastructure.Nodes.Reset(state.Draft.Topology.Nodes.Select(item => new TrackNodeEditorViewModel(item)));
        infrastructure.Edges.Reset(state.Draft.Topology.Edges.Select(item => new TrackEdgeEditorViewModel(item)));
        infrastructure.Facilities.Reset(state.Draft.Topology.TurnbackFacilities.Select(item => new FacilityEditorViewModel(item.FacilityId, item.Name, UiDisplayText.Enum(item.Kind), string.Join(" → ", item.Traversals.Select(traversal => traversal.TrackEdgeId)), FacilityEditorKind.Turnback))
            .Concat(state.Draft.Topology.PassingFacilities.Select(item => new FacilityEditorViewModel(item.FacilityId, item.Name, UiDisplayText.FacilityKind(FacilityEditorKind.Passing), string.Join(" → ", item.Traversals.Select(traversal => traversal.TrackEdgeId)), FacilityEditorKind.Passing)))
            .Concat(state.Draft.Topology.Edges.Where(item => item.Kind == TrackEdgeKind.Crossover).Select(item => new FacilityEditorViewModel(item.TrackEdgeId, item.TrackEdgeId, UiDisplayText.FacilityKind(FacilityEditorKind.CrossoverEdge), $"{item.FromNodeId} → {item.ToNodeId}", FacilityEditorKind.CrossoverEdge))));
        infrastructure.Stations.Reset(state.Draft.Topology.Stations.Select(item => new StationEditorViewModel(item)));
        infrastructure.Platforms.Reset(state.Draft.Topology.Platforms.Select(item => new PlatformEditorViewModel(item)));
        infrastructure.SpeedLimits.Reset(state.Draft.Topology.SpeedLimits.Select(item => new TrackSpeedLimitEditorViewModel(item)));
        infrastructure.Gradients.Reset(state.Draft.Topology.Gradients.Select(item => new GradientEditorViewModel(item)));
        infrastructure.Resources.Reset(state.Draft.Topology.Resources.Select(item => new ResourceEditorViewModel(item)));
        serviceNetwork.Routes.Reset(state.Draft.ServiceRoutes.Select(item => new ServiceRouteEditorViewModel(item)));
        serviceNetwork.StopPatterns.Reset(state.Draft.StopPatterns.Select(item => new StopPatternEditorViewModel(item)));
        var routesByDirection = state.Draft.DirectionRouteBindings.ToDictionary(item => item.Direction, item => item.ServiceRouteId);
        serviceNetwork.ServiceTypes.Reset(state.Draft.ServiceTypes.Select(item => new ServiceTypeEditorViewModel(item)
        {
            ServiceRoutes = string.Join(", ", (state.Draft.Dispatch.SimpleHeadwayPlans ?? []).Where(plan => plan.ServiceTypeId.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).Select(plan => routesByDirection.GetValueOrDefault(plan.Direction, "—"))
                .Concat((state.Draft.Dispatch.ManualTimetableRows ?? []).Where(row => row.ServiceTypeId.Equals(item.Id, StringComparison.OrdinalIgnoreCase)).Select(row => routesByDirection.GetValueOrDefault(row.Direction, "—")))
                .Distinct(StringComparer.OrdinalIgnoreCase))
        }));
        serviceNetwork.VehicleTypes.Reset(state.Draft.VehicleTypes.Select(item => new VehicleTypeEditorViewModel(item)
        {
            CompatibleServiceTypes = string.Join(", ", state.Draft.ServiceTypes.Where(service => service.DefaultVehicleTypeId?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) == true).Select(service => service.Id))
        }));
        PopulateDispatchRows();
        dispatch.HeadwayPlans.Reset((state.Draft.Dispatch.SimpleHeadwayPlans ?? []).Select(item => new HeadwayPlanEditorViewModel(item)));
        dispatch.ManualRows.Reset((state.Draft.Dispatch.ManualTimetableRows ?? []).Select(item => new ManualTimetableEditorViewModel(item)));
    }

    private void CommitTableDrafts()
    {
        CommitVisibleGridEdits();
        if (infrastructure.Nodes.Count == 0 && infrastructure.Edges.Count == 0) return;
        var topology = state.Draft.Topology with
        {
            Nodes = infrastructure.Nodes.Select(item => item.ToDomain()).ToArray(), Edges = infrastructure.Edges.Select(item => item.ToDomain()).ToArray(),
            Stations = infrastructure.Stations.Select(item => item.ToDomain()).ToArray(), Platforms = infrastructure.Platforms.Select(item => item.ToDomain()).ToArray(),
            SpeedLimits = infrastructure.SpeedLimits.Select(item => item.ToDomain()).ToArray(), Gradients = infrastructure.Gradients.Select(item => item.ToDomain()).ToArray(),
            Resources = infrastructure.Resources.Select(item => item.ToDomain()).ToArray()
        };
        var dispatchPlan = state.Draft.Dispatch with
        {
            SimpleHeadwayPlans = dispatch.HeadwayPlans.Select(item => item.ToDomain()).ToArray(),
            ManualTimetableRows = dispatch.ManualRows.Select(item => item.ToDomain()).ToArray()
        };
        state.Replace(state.Draft with
        {
            Topology = topology,
            ServiceRoutes = serviceNetwork.Routes.Select(item => item.ToDomain()).ToArray(),
            StopPatterns = serviceNetwork.StopPatterns.Select(item => item.ToDomain()).ToArray(),
            ServiceTypes = serviceNetwork.ServiceTypes.Select(item => item.ToDomain()).ToArray(),
            VehicleTypes = serviceNetwork.VehicleTypes.Select(item => item.ToDomain()).ToArray(),
            Dispatch = dispatchPlan
        });
    }

    private void RefreshValidation()
    {
        try
        {
            CommitTableDrafts();
            SetValidationMessages(ProjectEditorValidationService.Validate(state.Draft));
        }
        catch (EditorValidationException exception) { SetValidationMessages(exception.Messages); }
        catch (SimulationValidationException exception) { ShowErrors(exception.Errors); }
        catch (Exception exception) { SetValidationMessages([ProjectEditorValidationService.CreateError(exception.Message, state.Draft)]); }
    }

    private void Apply()
    {
        try { CommitTableDrafts(); Result = state.Commit(); DialogResult = true; }
        catch (EditorValidationException exception)
        {
            SetValidationMessages(exception.Messages);
            MessageBox.Show(this, "目前仍有欄位輸入錯誤；未套用任何變更。", "格式版本 8", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (SimulationValidationException exception)
        {
            ShowErrors(exception.Errors);
            MessageBox.Show(this, "目前仍有驗證錯誤；未套用任何變更。", "格式版本 8", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CommitVisibleGridEdits()
    {
        var messages = new List<ProjectValidationMessage>();
        foreach (var grid in workspace.Descendants().OfType<DataGrid>().Where(candidate => !candidate.IsReadOnly))
        {
            var fieldName = GetColumnFieldName(grid.CurrentColumn);
            var row = grid.CurrentItem;
            var cellCommitted = grid.CommitEdit(DataGridEditingUnit.Cell, true);
            var rowCommitted = cellCommitted && grid.CommitEdit(DataGridEditingUnit.Row, true);
            var hasBindingError = Validation.GetHasError(grid)
                || grid.Descendants().Any(Validation.GetHasError);
            if (cellCommitted && rowCommitted && !hasBindingError) continue;

            // Clicking a toolbar button can clear CurrentCell before validation runs.
            // Recover the actual failed cell so navigation keeps its row and field.
            var errorCell = grid.Descendants().OfType<DataGridCell>()
                .FirstOrDefault(cell => Validation.GetHasError(cell) || cell.Descendants().Any(Validation.GetHasError));
            if (errorCell is not null)
            {
                fieldName = GetColumnFieldName(errorCell.Column);
                row = errorCell.DataContext;
            }
            var kind = KindForGrid(grid);
            var targetId = GetRowTargetId(row)
                ?? (kind == ProjectValidationTargetKind.ServiceRoute ? selectedRoute?.Id : null)
                ?? (kind == ProjectValidationTargetKind.StopPattern ? selectedStopPattern?.Id : null);
            var fieldLabel = string.IsNullOrWhiteSpace(fieldName) ? "目前欄位" : $"「{UiDisplayText.Header(fieldName)}」";
            messages.Add(new ProjectValidationMessage(
                ProjectValidationSeverity.Error,
                $"{fieldLabel}的內容無法套用；請輸入有效且不可缺少的值。",
                targetId,
                kind,
                fieldName));
        }

        if (messages.Count > 0) throw new EditorValidationException(messages);
    }

    private ProjectValidationTargetKind KindForGrid(DataGrid grid)
    {
        foreach (var entry in validationTargets)
        {
            if (entry.Value.Any(target => ReferenceEquals(target.Grid, grid))) return entry.Key;
        }

        return ProjectValidationTargetKind.Project;
    }

    private static string? GetColumnFieldName(DataGridColumn? column)
    {
        if (column is null) return null;
        if (!string.IsNullOrWhiteSpace(column.SortMemberPath)) return column.SortMemberPath;
        return column is DataGridBoundColumn bound && bound.Binding is Binding binding ? binding.Path?.Path : null;
    }

    private static string? GetRowTargetId(object? row) => row switch
    {
        TrackNodeEditorViewModel item => item.Id,
        TrackEdgeEditorViewModel item => item.Id,
        FacilityEditorViewModel item => item.Id,
        StationEditorViewModel item => item.Id,
        PlatformEditorViewModel item => item.Id,
        TrackSpeedLimitEditorViewModel item => item.Id,
        GradientEditorViewModel item => item.Id,
        ResourceEditorViewModel item => item.Id,
        ServiceRouteEditorViewModel item => item.Id,
        StopPatternEditorViewModel item => item.Id,
        VehicleTypeEditorViewModel item => item.Id,
        ServiceTypeEditorViewModel item => item.Id,
        _ => null
    };

    private void PopulateQuickStations()
    {
        var route = state.Draft.ServiceRoutes.FirstOrDefault();
        if (route is null) return;
        foreach (var stop in route.Stops)
        {
            var station = state.Draft.Topology.Stations.FirstOrDefault(item => item.StationId.Equals(stop.StationId, StringComparison.OrdinalIgnoreCase));
            quickStations.Add(new QuickStationEditorViewModel { Id = stop.StationId, Name = station?.Name ?? stop.StationId, DwellSeconds = station?.DefaultDwellTimeSeconds ?? 30 });
        }
    }

    private void BuildQuickTopology()
    {
        try
        {
            state.Replace(ProjectDocumentMapper.BuildQuickLinearProject(state.Draft, quickStations.Select(item => item.ToDomain())));
            ReloadViewModels(); RefreshValidation();
            selectionDetails.Text = "已由快速建立產生實際的軌道節點、軌道區段、月台與服務路徑。後續請在基礎設施與營運工作區編輯。";
        }
        catch (SimulationValidationException exception) { ShowErrors(exception.Errors); }
    }

    private void AddNode() => RunEdit(() => TopologyEditingService.AddTrackNode(state.Draft, "新節點"));

    private void AddEdge()
    {
        var answer = Ask("新增軌道區段", ("起點節點", infrastructure.Nodes.FirstOrDefault()?.Id ?? ""), ("終點節點", infrastructure.Nodes.Skip(1).FirstOrDefault()?.Id ?? ""), ("長度（m）", "100"), ("速限（km/h）", "60"));
        if (answer is null) return;
        RunEdit(() => TopologyEditingService.AddTrackEdge(state.Draft, answer["起點節點"], answer["終點節點"], Parse(answer["長度（m）"], "長度"), TrackEdgeKind.Mainline, TrackDirectionality.Bidirectional, Parse(answer["速限（km/h）"], "速限") / 3.6));
    }

    private void AddStation()
    {
        var id = NextId("STATION");
        RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { Stations = state.Draft.Topology.Stations.Append(new StationDefinitionV4 { StationId = id, Name = "新車站", DefaultDwellTimeSeconds = 30 }).ToArray() } });
    }

    private void AddPlatform()
    {
        var station = infrastructure.Stations.FirstOrDefault();
        var edge = infrastructure.Edges.FirstOrDefault();
        if (station is null || edge is null) { ShowErrors(["請先建立車站與軌道區段，才能附著月台。"]); return; }
        var answer = Ask("新增月台", ("名稱", "新月台"), ("車站", station.Id), ("軌道區段", edge.Id),
            ("起始偏移（m）", "0"), ("停車偏移（m）", "50"), ("終止偏移（m）", Math.Min(100, edge.LengthMeters).ToString(CultureInfo.InvariantCulture)),
            ("有效長度（m）", "100"), ("方向（下行／上行／雙向）", "雙向"));
        if (answer is null) return;
        if (!UiDisplayText.TryParseEnum(typeof(TrackDirection), answer["方向（下行／上行／雙向）"], out var parsedDirection) || parsedDirection is not TrackDirection direction)
        {
            ShowErrors(["方向必須是下行、上行或雙向。"]); return;
        }
        var id = NextId("PLATFORM");
        RunEdit(() =>
        {
            var attachedEdge = state.Draft.Topology.Edges.SingleOrDefault(item => item.TrackEdgeId.Equals(answer["軌道區段"], StringComparison.OrdinalIgnoreCase))
                ?? throw new SimulationValidationException([$"找不到要附著的軌道區段「{answer["軌道區段"]}」。"]);
            if (!state.Draft.Topology.Stations.Any(item => item.StationId.Equals(answer["車站"], StringComparison.OrdinalIgnoreCase)))
                throw new SimulationValidationException([$"找不到要附著的車站「{answer["車站"]}」。"]);
            var platform = new PlatformDefinitionV4
            {
                PlatformId = id, Name = answer["名稱"], StationId = answer["車站"], TrackEdgeId = answer["軌道區段"],
                PlatformStartOffsetMeters = Parse(answer["起始偏移（m）"], "月台起點"), StopPositionOffsetMeters = Parse(answer["停車偏移（m）"], "停止位置"),
                PlatformEndOffsetMeters = Parse(answer["終止偏移（m）"], "月台終點"), EffectiveLengthMeters = Parse(answer["有效長度（m）"], "有效長度"),
                AllowedDirection = direction
            };
            ValidatePlatformOffsets(platform, attachedEdge);
            var stations = state.Draft.Topology.Stations.Select(item => item.StationId.Equals(platform.StationId, StringComparison.OrdinalIgnoreCase)
                ? item with { PlatformIds = item.PlatformIds.Append(id).ToArray() } : item).ToArray();
            return state.Draft with { Topology = state.Draft.Topology with { Stations = stations, Platforms = state.Draft.Topology.Platforms.Append(platform).ToArray() } };
        });
    }

    private void AddStationOnTracks()
    {
        var down = infrastructure.Edges.FirstOrDefault();
        var up = infrastructure.Edges.Skip(1).FirstOrDefault() ?? down;
        if (down is null || up is null) { ShowErrors(["請先建立至少一條軌道區段，才能建立車站月台配置。"]); return; }
        var answer = Ask("新增車站到軌道", ("車站名稱", "新車站"), ("下行軌道", down.Id), ("上行軌道", up.Id),
            ("車站中心／停車位置（m）", "50"), ("月台長度（m）", "100"));
        if (answer is null) return;
        var stationId = NextId("STATION");
        var downPlatformId = NextId("PLATFORM");
        var upPlatformId = NextId("PLATFORM");
        RunEdit(() =>
        {
            var stop = Parse(answer["車站中心／停車位置（m）"], "停止位置");
            var length = Parse(answer["月台長度（m）"], "月台長度");
            PlatformDefinitionV4 Platform(string id, string name, string edgeId, TrackDirection direction) => new()
            {
                PlatformId = id, Name = name, StationId = stationId, TrackEdgeId = edgeId,
                PlatformStartOffsetMeters = Math.Max(0, stop - length / 2), StopPositionOffsetMeters = stop,
                PlatformEndOffsetMeters = stop + length / 2, EffectiveLengthMeters = length, AllowedDirection = direction
            };
            var downEdge = state.Draft.Topology.Edges.SingleOrDefault(item => item.TrackEdgeId.Equals(answer["下行軌道"], StringComparison.OrdinalIgnoreCase))
                ?? throw new SimulationValidationException([$"找不到下行軌道「{answer["下行軌道"]}」。"]);
            var upEdge = state.Draft.Topology.Edges.SingleOrDefault(item => item.TrackEdgeId.Equals(answer["上行軌道"], StringComparison.OrdinalIgnoreCase))
                ?? throw new SimulationValidationException([$"找不到上行軌道「{answer["上行軌道"]}」。"]);
            var downPlatform = Platform(downPlatformId, $"{answer["車站名稱"]} 下行月台", answer["下行軌道"], TrackDirection.Outbound);
            var upPlatform = Platform(upPlatformId, $"{answer["車站名稱"]} 上行月台", answer["上行軌道"], TrackDirection.Inbound);
            ValidatePlatformOffsets(downPlatform, downEdge);
            ValidatePlatformOffsets(upPlatform, upEdge);
            return state.Draft with
            {
                Topology = state.Draft.Topology with
                {
                    Stations = state.Draft.Topology.Stations.Append(new StationDefinitionV4 { StationId = stationId, Name = answer["車站名稱"], DefaultDwellTimeSeconds = 30, PlatformIds = [downPlatformId, upPlatformId] }).ToArray(),
                    Platforms = state.Draft.Topology.Platforms.Append(downPlatform).Append(upPlatform).ToArray()
                }
            };
        });
    }

    private void AddSpeedLimit()
    {
        var edge = infrastructure.Edges.FirstOrDefault();
        if (edge is null) { ShowErrors(["請先建立軌道區段。"]); return; }
        RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { SpeedLimits = state.Draft.Topology.SpeedLimits.Append(new TrackSpeedLimitDefinition { SpeedLimitId = NextId("SL"), TrackEdgeId = edge.Id, StartOffsetMeters = 0, EndOffsetMeters = edge.LengthMeters, LimitMetersPerSecond = edge.DefaultSpeedKmh / 3.6 }).ToArray() } });
    }

    private void AddGradient()
    {
        var edge = infrastructure.Edges.FirstOrDefault();
        if (edge is null) { ShowErrors(["請先建立軌道區段。"]); return; }
        RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { Gradients = state.Draft.Topology.Gradients.Append(new TrackGradientSegment(NextId("GRADE"), edge.Id, 0, edge.LengthMeters, 0)).ToArray() } });
    }

    private void AddResource() => RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { Resources = state.Draft.Topology.Resources.Append(new ConflictResourceDefinition(NextId("RESOURCE"), "手動資源", ConflictResourceKind.Other)).ToArray() } });

    private void DeleteSelectedNode() { if (Selected<TrackNodeEditorViewModel>() is { } row) RunEdit(() => TopologyEditingService.DeleteTrackNode(state.Draft, row.Id)); }
    private void DeleteSelectedEdge() { if (Selected<TrackEdgeEditorViewModel>() is { } row) RunEdit(() => TopologyEditingService.DeleteTrackEdge(state.Draft, row.Id)); }
    private void DeleteSelectedStation() { if (Selected<StationEditorViewModel>() is { } row) RunEdit(() => TopologyEditingService.DeleteStation(state.Draft, row.Id)); }
    private void DeleteSelectedPlatform() { if (Selected<PlatformEditorViewModel>() is { } row) RunEdit(() => TopologyEditingService.DeletePlatform(state.Draft, row.Id)); }
    private void DeleteSelectedFacility()
    {
        if (Selected<FacilityEditorViewModel>() is not { } row) return;
        RunEdit(() => row.BackingKind switch
        {
            FacilityEditorKind.Turnback => TopologyEditingService.DeleteTurnbackFacility(state.Draft, row.Id),
            FacilityEditorKind.Passing => TopologyEditingService.DeletePassingFacility(state.Draft, row.Id),
            FacilityEditorKind.CrossoverEdge => TopologyEditingService.DeleteTrackEdge(state.Draft, row.Id),
            _ => state.Draft
        });
    }
    private void DeleteSelectedSpeedLimit() { if (Selected<TrackSpeedLimitEditorViewModel>() is { } row) RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { SpeedLimits = state.Draft.Topology.SpeedLimits.Where(item => !item.SpeedLimitId.Equals(row.Id, StringComparison.OrdinalIgnoreCase)).ToArray() } }); }
    private void DeleteSelectedGradient() { if (Selected<GradientEditorViewModel>() is { } row) RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { Gradients = state.Draft.Topology.Gradients.Where(item => !item.GradientId.Equals(row.Id, StringComparison.OrdinalIgnoreCase)).ToArray() } }); }

    private void DeleteSelectedResource()
    {
        if (Selected<ResourceEditorViewModel>() is not { } row) return;
        var users = TopologyDependencyAnalyzer.GetResourceReferences(state.Draft, row.Id);
        if (users.Count > 0) { ShowErrors([$"無法刪除資源「{row.Id}」；仍被使用：{string.Join("、", users.Select(item => item.DisplayName))}。"]); return; }
        RunEdit(() => state.Draft with { Topology = state.Draft.Topology with { Resources = state.Draft.Topology.Resources.Where(item => !item.ResourceId.Equals(row.Id, StringComparison.OrdinalIgnoreCase)).ToArray() } });
    }

    private void ShowFacilityMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menu("在軌道區段插入接點…", SplitTrackEdge));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menu("新增橫渡線…", CreateCrossover)); menu.Items.Add(Menu("新增尾軌…", CreateTail));
        menu.Items.Add(Menu("新增袋狀軌…", CreatePocket)); menu.Items.Add(Menu("新增待避線…", CreatePassing));
        menu.IsOpen = true;
    }

    private static MenuItem Menu(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void CreateCrossover()
    {
        var answer = Ask("新增橫渡線", ("名稱", "新橫渡線"), ("起點節點", infrastructure.Nodes.FirstOrDefault()?.Id ?? ""), ("終點節點", infrastructure.Nodes.Skip(1).FirstOrDefault()?.Id ?? ""), ("長度（m）", "65"), ("速限（km/h）", "25"));
        if (answer is not null) RunEdit(() => FacilityCreationService.CreateCrossover(state.Draft, new CrossoverFacilityRequest(answer["名稱"], answer["起點節點"], answer["終點節點"], Parse(answer["長度（m）"], "長度"), Parse(answer["速限（km/h）"], "速限") / 3.6)));
    }

    private void SplitTrackEdge()
    {
        var edge = infrastructure.Edges.FirstOrDefault();
        if (edge is null) { ShowErrors(["請先建立軌道區段。"]); return; }
        var answer = Ask("在軌道區段插入接點", ("軌道區段", edge.Id), ("偏移量（m）", (edge.LengthMeters / 2).ToString(CultureInfo.InvariantCulture)));
        if (answer is not null) RunEdit(() => TopologyEditingService.SplitEdge(state.Draft, answer["軌道區段"], Parse(answer["偏移量（m）"], "分割位置")));
    }

    private void CreateTail()
    {
        var answer = Ask("新增尾軌", ("名稱", "新尾軌"), ("連接節點", infrastructure.Nodes.LastOrDefault()?.Id ?? ""), ("到達軌道區段", infrastructure.Edges.FirstOrDefault()?.Id ?? ""), ("出發軌道區段", infrastructure.Edges.Skip(1).FirstOrDefault()?.Id ?? ""), ("長度（m）", "120"), ("速限（km/h）", "25"), ("止衝安全距離（m）", "8"));
        if (answer is not null) RunEdit(() => FacilityCreationService.CreateTailTrack(state.Draft, new TailFacilityRequest(answer["名稱"], answer["連接節點"], answer["到達軌道區段"], answer["出發軌道區段"], Parse(answer["長度（m）"], "長度"), Parse(answer["速限（km/h）"], "速限") / 3.6, Parse(answer["止衝安全距離（m）"], "安全餘量"))));
    }

    private void CreatePocket()
    {
        var answer = Ask("新增袋狀軌", ("名稱", "新袋狀軌"), ("入口節點", infrastructure.Nodes.LastOrDefault()?.Id ?? ""), ("出口節點", infrastructure.Nodes.LastOrDefault()?.Id ?? ""), ("到達軌道區段", infrastructure.Edges.FirstOrDefault()?.Id ?? ""), ("出發軌道區段", infrastructure.Edges.Skip(1).FirstOrDefault()?.Id ?? ""), ("長度（m）", "160"), ("速限（km/h）", "25"), ("停車偏移（m）", "80"));
        if (answer is not null) RunEdit(() => FacilityCreationService.CreatePocketTrack(state.Draft, new PocketFacilityRequest(answer["名稱"], answer["入口節點"], answer["出口節點"], answer["到達軌道區段"], answer["出發軌道區段"], Parse(answer["長度（m）"], "長度"), Parse(answer["速限（km/h）"], "速限") / 3.6, Parse(answer["停車偏移（m）"], "停止位置"))));
    }

    private void CreatePassing()
    {
        var answer = Ask("新增待避線", ("名稱", "新待避線"), ("車站", infrastructure.Stations.FirstOrDefault()?.Id ?? ""), ("入口節點", infrastructure.Nodes.FirstOrDefault()?.Id ?? ""), ("出口節點", infrastructure.Nodes.LastOrDefault()?.Id ?? ""), ("到達軌道區段", infrastructure.Edges.FirstOrDefault()?.Id ?? ""), ("出發軌道區段", infrastructure.Edges.LastOrDefault()?.Id ?? ""), ("普通車月台", infrastructure.Platforms.FirstOrDefault()?.Id ?? ""), ("快速車月台", infrastructure.Platforms.Skip(1).FirstOrDefault()?.Id ?? ""), ("服務路徑", serviceNetwork.Routes.FirstOrDefault()?.Id ?? ""), ("長度（m）", "300"), ("速限（km/h）", "45"));
        if (answer is not null) RunEdit(() => FacilityCreationService.CreatePassingTrack(state.Draft, new PassingFacilityRequest(answer["名稱"], answer["車站"], answer["入口節點"], answer["出口節點"], answer["到達軌道區段"], answer["出發軌道區段"], answer["普通車月台"], answer["快速車月台"], Parse(answer["長度（m）"], "長度"), Parse(answer["速限（km/h）"], "速限") / 3.6, answer["服務路徑"])));
    }

    private void ShowRouteDetail()
    {
        if (traversalGrid is null) return;
        traversalGrid.ItemsSource = selectedRoute?.Traversals;
        if (routeStopGrid is not null) routeStopGrid.ItemsSource = selectedRoute?.Stops;
        selectionDetails.Text = selectedRoute is null ? "" : $"服務路徑：{selectedRoute.Name}\n通過區段依序排列，因此不使用核取方塊或集合式編輯。\n停靠站會繫結到這條路徑上的實體車站與月台。";
    }

    private void AddServiceRoute()
    {
        var id = NextDocumentId("ROUTE", state.Draft.ServiceRoutes.Select(item => item.ServiceRouteId));
        RunEdit(() => state.Draft with { ServiceRoutes = state.Draft.ServiceRoutes.Append(new ServiceRouteDefinition { ServiceRouteId = id, Name = "新營運路線", Traversals = [], Stops = [] }).ToArray() });
    }

    private void DeleteSelectedServiceRoute()
    {
        if (routeGrid?.SelectedItem is ServiceRouteEditorViewModel row)
            RunEdit(() => TopologyEditingService.DeleteServiceRoute(state.Draft, row.Id));
    }

    private void AddTraversal()
    {
        if (selectedRoute is null) return;
        var answer = Ask("新增通過區段", ("軌道區段", infrastructure.Edges.FirstOrDefault()?.Id ?? ""), ("方向（正向／反向）", "正向"));
        if (answer is null) return;
        if (!UiDisplayText.TryParseEnum(typeof(TraversalDirection), answer["方向（正向／反向）"], out var parsedDirection) || parsedDirection is not TraversalDirection direction) { ShowErrors(["方向必須是正向或反向。"]); return; }
        selectedRoute.Traversals.Add(new TraversalEditorViewModel(new DirectedTrackTraversal(answer["軌道區段"], direction)));
    }

    private void RemoveTraversal() { if (selectedRoute is not null && traversalGrid?.SelectedItem is TraversalEditorViewModel row) selectedRoute.Traversals.Remove(row); }

    private void MoveTraversal(int direction)
    {
        if (selectedRoute is null || traversalGrid?.SelectedItem is not TraversalEditorViewModel row) return;
        var index = selectedRoute.Traversals.IndexOf(row); var target = index + direction;
        if (target < 0 || target >= selectedRoute.Traversals.Count) return;
        selectedRoute.Traversals.Move(index, target); traversalGrid.SelectedItem = row;
    }

    private void BuildRoutePath()
    {
        if (selectedRoute is null) return;
        CommitTableDrafts();
        var answer = Ask("自動建立路徑", ("起始月台", infrastructure.Platforms.FirstOrDefault()?.Id ?? ""), ("終點月台", infrastructure.Platforms.LastOrDefault()?.Id ?? ""));
        if (answer is null) return;
        RunEdit(() => ServiceRouteEditingService.BuildShortestPath(state.Draft, selectedRoute.Id, answer["起始月台"], answer["終點月台"]));
    }

    private void AddRouteStop()
    {
        if (selectedRoute is null) return;
        var station = state.Draft.Topology.Stations.FirstOrDefault(item => !selectedRoute.Stops.Any(stop => stop.Station.Equals(item.StationId, StringComparison.OrdinalIgnoreCase)));
        if (station is null) { ShowErrors(["所有現有車站都已在服務路徑停靠站中；請先於對應通過區段上建立新的車站／月台。"]); return; }
        var candidates = ServiceRouteEditingService.GetCandidatePlatformIds(state.Draft, selectedRoute.ToDomain(), station.StationId);
        if (candidates.Count == 0) { ShowErrors([$"車站「{station.Name}」沒有位於這條服務路徑通過區段上的月台，不能加入停靠站。"]); return; }
        var answer = Ask("新增路線停靠站", ("車站", station.StationId), ("候選月台", string.Join(", ", candidates)));
        if (answer is null) return;
        var allowed = ServiceRouteEditingService.GetCandidatePlatformIds(state.Draft, selectedRoute.ToDomain(), answer["車站"]);
        var selected = answer["候選月台"].Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (selected.Length == 0 || selected.Any(id => !allowed.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            ShowErrors([$"候選月台必須是目前路線通過之車站「{answer["車站"]}」月台：{string.Join("、", allowed)}。"]); return;
        }
        selectedRoute.Stops.Add(new ServiceRouteStopEditorViewModel(new ServiceRouteStop { StationId = answer["車站"], CandidatePlatformIds = selected }));
    }

    private void RemoveRouteStop()
    {
        if (selectedRoute is not null && routeStopGrid?.SelectedItem is ServiceRouteStopEditorViewModel row)
            selectedRoute.Stops.Remove(row);
    }

    private void ValidateRouteStopCandidates()
    {
        if (selectedRoute is null) return;
        var errors = new List<string>();
        foreach (var stop in selectedRoute.Stops)
        {
            var allowed = ServiceRouteEditingService.GetCandidatePlatformIds(state.Draft, selectedRoute.ToDomain(), stop.Station);
            var actual = stop.CandidatePlatforms.Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (actual.Length == 0 || actual.Any(id => !allowed.Contains(id, StringComparer.OrdinalIgnoreCase)))
                errors.Add($"{stop.Station} 的候選月台必須為：{string.Join("、", allowed)}。");
        }
        if (errors.Count == 0) selectionDetails.Text = "路線停靠站的所有候選月台都位於目前服務路徑拓撲上。";
        else ShowErrors(errors);
    }

    private void ShowStopPatternDetail()
    {
        if (stopPatternInstructionGrid is not null) stopPatternInstructionGrid.ItemsSource = selectedStopPattern?.Instructions;
        selectionDetails.Text = selectedStopPattern is null ? "" : $"停站模式：{selectedStopPattern.Name}\n可加入的車站會從所選服務路徑的停靠站推導。";
    }

    private void AddStopPattern()
    {
        var route = selectedRoute ?? serviceNetwork.Routes.FirstOrDefault();
        if (route is null || route.Stops.Count == 0) { ShowErrors(["請先建立包含路線停靠站的服務路徑，才能建立停站模式。"]); return; }
        var id = NextDocumentId("STOP", state.Draft.StopPatterns.Select(item => item.Id));
        RunEdit(() => state.Draft with
        {
            StopPatterns = state.Draft.StopPatterns.Append(new ProjectStopPattern(id, "新停站模式",
                route.Stops.Select(stop => new ProjectStopPatternInstruction(stop.Station, StopPatternAction.Stop, null, null)).ToArray())).ToArray()
        });
    }

    private void DeleteSelectedStopPattern()
    {
        if (stopPatternGrid?.SelectedItem is StopPatternEditorViewModel row)
            RunEdit(() => TopologyEditingService.DeleteStopPattern(state.Draft, row.Id));
    }

    private void AddStopPatternInstruction()
    {
        if (selectedStopPattern is null) return;
        var route = selectedRoute ?? serviceNetwork.Routes.FirstOrDefault();
        var candidates = route?.Stops.Select(stop => stop.Station).Where(station => !selectedStopPattern.Instructions.Any(item => item.Station.Equals(station, StringComparison.OrdinalIgnoreCase))).ToArray() ?? [];
        if (candidates.Length == 0) { ShowErrors(["沒有可從目前服務路徑停靠站加入的車站。"]); return; }
        selectedStopPattern.Instructions.Add(new StopPatternInstructionEditorViewModel(new ProjectStopPatternInstruction(candidates[0], StopPatternAction.Stop)));
    }

    private void AddVehicleType()
    {
        var id = NextDocumentId("VEHICLE", state.Draft.VehicleTypes.Select(item => item.Id));
        var train = state.Draft.Train;
        RunEdit(() => state.Draft with
        {
            VehicleTypes = state.Draft.VehicleTypes.Append(new ProjectVehicleType(id, "新車型", state.Draft.Operations.TrainLengthMeters,
                train.MaxSpeedMetersPerSecond, train.AccelerationMetersPerSecondSquared, train.DecelerationMetersPerSecondSquared,
                state.Draft.Operations.EmergencyBrakingMetersPerSecondSquared, state.Draft.Operations.JerkMetersPerSecondCubed,
                0, 0, state.Draft.StopPatterns.FirstOrDefault()?.Id)).ToArray()
        });
    }

    private void DeleteSelectedVehicleType()
    {
        if (Selected<VehicleTypeEditorViewModel>() is { } row)
            RunEdit(() => TopologyEditingService.DeleteVehicleType(state.Draft, row.Id));
    }

    private void AddServiceType()
    {
        var id = NextDocumentId("SERVICE", state.Draft.ServiceTypes.Select(item => item.Id));
        RunEdit(() => state.Draft with
        {
            ServiceTypes = state.Draft.ServiceTypes.Append(new ProjectServiceType(id, "新服務", "#2166A5", "S",
                state.Draft.StopPatterns.FirstOrDefault()?.Id, state.Draft.VehicleTypes.FirstOrDefault()?.Id,
                0, false, [])).ToArray()
        });
    }

    private void DeleteSelectedServiceType()
    {
        if (Selected<ServiceTypeEditorViewModel>() is { } row)
            RunEdit(() => TopologyEditingService.DeleteServiceType(state.Draft, row.Id));
    }

    private void AddHeadwayPlan()
    {
        var route = state.Draft.DirectionRouteBindings.FirstOrDefault();
        var service = state.Draft.ServiceTypes.FirstOrDefault();
        if (route is null || service is null) { ShowErrors(["請先建立方向綁定與服務類型，才能建立班距計畫。"]); return; }
        var origin = GetRouteOriginPlatform(route.ServiceRouteId);
        RunEdit(() => state.Draft with
        {
            Dispatch = state.Draft.Dispatch with
            {
                SimpleHeadwayPlans = (state.Draft.Dispatch.SimpleHeadwayPlans ?? []).Append(new ProjectHeadwayPlan(route.Direction,
                    state.Draft.Simulation.StartClockSeconds, 300, 1, service.Id, service.DefaultVehicleTypeId,
                    service.DefaultStopPatternId, origin)).ToArray()
            }
        });
    }

    private void DeleteSelectedHeadwayPlan()
    {
        if (Selected<HeadwayPlanEditorViewModel>() is not { } row) return;
        var index = dispatch.HeadwayPlans.IndexOf(row);
        if (index >= 0) dispatch.HeadwayPlans.RemoveAt(index);
    }

    private void EditSelectedHeadwayPlan()
    {
        if (Selected<HeadwayPlanEditorViewModel>() is not { } row) return;
        var origins = GetAllowedDispatchOrigins(row.Direction);
        if (origins.Count == 0) { ShowErrors(["目前方向綁定的服務路徑尚未有可作為起點的實體停靠站。"]); return; }
        var answer = Ask("編輯班距計畫", ("首班發車秒數", row.FirstDepartureSeconds.ToString(CultureInfo.InvariantCulture)),
            ("班距秒數", row.HeadwaySeconds.ToString(CultureInfo.InvariantCulture)), ("班次數", row.RunCount.ToString(CultureInfo.InvariantCulture)),
            ("服務類型", row.ServiceType), ("車型", row.VehicleType), ("停站模式", row.StopPattern), ("終點後續行", UiDisplayText.Boolean(row.ContinueAfterTerminal)));
        if (answer is null) return;
        var origin = Choose("選擇實體起始月台", "起始月台", origins, row.OriginPlatform);
        if (origin is null) return;
        try
        {
            row.FirstDepartureSeconds = Parse(answer["首班發車秒數"], "首班時間");
            row.HeadwaySeconds = Parse(answer["班距秒數"], "班距");
            if (!int.TryParse(answer["班次數"], out var count) || count <= 0) throw new SimulationValidationException(["班次數必須是大於 0 的整數。"]);
            if (!UiDisplayText.TryParseBoolean(answer["終點後續行"], out var continuation)) throw new SimulationValidationException(["終點後續行必須填寫是或否。"]);
            row.RunCount = count; row.ServiceType = answer["服務類型"]; row.VehicleType = answer["車型"]; row.StopPattern = answer["停站模式"]; row.OriginPlatform = origin; row.ContinueAfterTerminal = continuation;
            CommitTableDrafts(); ReloadViewModels(); ShowDispatch();
        }
        catch (SimulationValidationException exception) { ShowErrors(exception.Errors); }
    }

    private void AddManualTimetableRow()
    {
        var route = state.Draft.DirectionRouteBindings.FirstOrDefault();
        var service = state.Draft.ServiceTypes.FirstOrDefault();
        if (route is null || service is null) { ShowErrors(["請先建立方向綁定與服務類型，才能建立手動班表。"]); return; }
        RunEdit(() => state.Draft with
        {
            Dispatch = state.Draft.Dispatch with
            {
                ManualTimetableRows = (state.Draft.Dispatch.ManualTimetableRows ?? []).Append(new ProjectManualTimetableRow(
                    state.Draft.Simulation.StartClockSeconds, route.Direction, service.Id, service.DefaultVehicleTypeId,
                    service.DefaultStopPatternId, GetRouteOriginPlatform(route.ServiceRouteId), null,
                    NextDocumentId("RUN", (state.Draft.Dispatch.ManualTimetableRows ?? []).Select(item => item.ServiceRunId ?? "")))).ToArray()
            }
        });
    }

    private void DeleteSelectedManualRow()
    {
        if (Selected<ManualTimetableEditorViewModel>() is not { } row) return;
        var index = dispatch.ManualRows.IndexOf(row);
        if (index >= 0) dispatch.ManualRows.RemoveAt(index);
    }

    private void EditSelectedManualRow()
    {
        if (Selected<ManualTimetableEditorViewModel>() is not { } row) return;
        var origins = GetAllowedDispatchOrigins(row.Direction);
        if (origins.Count == 0) { ShowErrors(["目前方向綁定的服務路徑尚未有可作為起點的實體停靠站。"]); return; }
        var answer = Ask("編輯手動班表", ("發車秒數", row.DepartureSeconds.ToString(CultureInfo.InvariantCulture)), ("服務類型", row.ServiceType),
            ("車型", row.VehicleType), ("停站模式", row.StopPattern), ("車輛編號", row.VehicleId), ("車次編號", row.ServiceRunId),
            ("終點後續行", UiDisplayText.Boolean(row.ContinueAfterTerminal)), ("接續車次編號", row.ContinuationServiceRunId));
        if (answer is null) return;
        var origin = Choose("選擇實體起始月台", "起始月台", origins, row.OriginPlatform);
        if (origin is null) return;
        try
        {
            row.DepartureSeconds = Parse(answer["發車秒數"], "出發時間");
            if (!UiDisplayText.TryParseBoolean(answer["終點後續行"], out var continuation)) throw new SimulationValidationException(["終點後續行必須填寫是或否。"]);
            row.ServiceType = answer["服務類型"]; row.VehicleType = answer["車型"]; row.StopPattern = answer["停站模式"]; row.VehicleId = answer["車輛編號"]; row.ServiceRunId = answer["車次編號"]; row.OriginPlatform = origin; row.ContinueAfterTerminal = continuation; row.ContinuationServiceRunId = answer["接續車次編號"];
            CommitTableDrafts(); ReloadViewModels(); ShowDispatch();
        }
        catch (SimulationValidationException exception) { ShowErrors(exception.Errors); }
    }

    private string? GetRouteOriginPlatform(string routeId) => state.Draft.ServiceRoutes
        .FirstOrDefault(item => item.ServiceRouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase))?.Stops.FirstOrDefault()?.CandidatePlatformIds.FirstOrDefault();

    private IReadOnlyList<string> GetAllowedDispatchOrigins(TrainDirection direction)
    {
        var routeId = state.Draft.DirectionRouteBindings.FirstOrDefault(item => item.Direction == direction)?.ServiceRouteId;
        return routeId is null ? [] : state.Draft.ServiceRoutes.FirstOrDefault(item => item.ServiceRouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase))?.Stops
            .SelectMany(stop => stop.CandidatePlatformIds).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    private void DrawSchematic(Canvas canvas)
    {
        canvas.Children.Clear();
        var layout = SchematicLayoutService.AutoLayout(state.Draft);
        var width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 720;
        var layoutMinX = layout.Count == 0 ? 0 : layout.Values.Min(point => point.X);
        var layoutMaxX = layout.Count == 0 ? 1 : layout.Values.Max(point => point.X);
        double MapX(double x) => 60 + (x - layoutMinX) / Math.Max(1, layoutMaxX - layoutMinX) * Math.Max(1, width - 120);
        const double mainlineY = 210;
        const double laneSpacing = 46;
        var railColor = Color.FromRgb(25, 96, 125);
        StationSchematicPresentation.DrawLegend(canvas);
        var outboundRouteId = state.Draft.DirectionRouteBindings
            .FirstOrDefault(binding => binding.Direction == TrainDirection.Outbound)?.ServiceRouteId;
        var inboundRouteId = state.Draft.DirectionRouteBindings
            .FirstOrDefault(binding => binding.Direction == TrainDirection.Inbound)?.ServiceRouteId;
        var outboundEdgeIds = state.Draft.ServiceRoutes
            .FirstOrDefault(route => route.ServiceRouteId.Equals(outboundRouteId, StringComparison.OrdinalIgnoreCase))?
            .Traversals.Select(traversal => traversal.TrackEdgeId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inboundEdgeIds = state.Draft.ServiceRoutes
            .FirstOrDefault(route => route.ServiceRouteId.Equals(inboundRouteId, StringComparison.OrdinalIgnoreCase))?
            .Traversals.Select(traversal => traversal.TrackEdgeId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawEdgePoints = new Dictionary<string, (Point From, Point To)>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in state.Draft.Topology.Edges
                     .Where(edge => layout.ContainsKey(edge.FromNodeId) && layout.ContainsKey(edge.ToNodeId)))
        {
            var from = layout[edge.FromNodeId];
            var to = layout[edge.ToNodeId];
            var fromY = from.Y;
            var toY = to.Y;
            if (outboundEdgeIds.Contains(edge.TrackEdgeId))
            {
                fromY = mainlineY + laneSpacing / 2;
                toY = mainlineY + laneSpacing / 2;
            }
            else if (inboundEdgeIds.Contains(edge.TrackEdgeId))
            {
                fromY = mainlineY - laneSpacing / 2;
                toY = mainlineY - laneSpacing / 2;
            }

            rawEdgePoints[edge.TrackEdgeId] = (
                new Point(MapX(from.X), fromY),
                new Point(MapX(to.X), toY));
        }

        foreach (var facility in state.Draft.Topology.TurnbackFacilities
                     .Where(item => item.Kind == TurnbackFacilityKind.TailTrack))
        {
            var arrivalEdge = state.Draft.Topology.Edges.FirstOrDefault(item =>
                item.TrackEdgeId.Equals(facility.ArrivalTrackEdgeId, StringComparison.OrdinalIgnoreCase));
            if (arrivalEdge is null || !rawEdgePoints.TryGetValue(arrivalEdge.TrackEdgeId, out var arrivalPoints)) continue;
            foreach (var traversal in facility.Traversals)
            {
                var tailEdge = state.Draft.Topology.Edges.FirstOrDefault(item =>
                    item.TrackEdgeId.Equals(traversal.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
                if (tailEdge is null || tailEdge.Kind != TrackEdgeKind.TailTrack
                    || !rawEdgePoints.TryGetValue(tailEdge.TrackEdgeId, out var tailPoints)) continue;
                var sharedNodeId = tailEdge.FromNodeId.Equals(arrivalEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                    || tailEdge.ToNodeId.Equals(arrivalEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                        ? arrivalEdge.FromNodeId
                        : arrivalEdge.ToNodeId;
                var arrivalY = sharedNodeId.Equals(arrivalEdge.FromNodeId, StringComparison.OrdinalIgnoreCase)
                    ? arrivalPoints.From.Y
                    : arrivalPoints.To.Y;
                rawEdgePoints[tailEdge.TrackEdgeId] = (
                    new Point(tailPoints.From.X, arrivalY),
                    new Point(tailPoints.To.X, arrivalY));
            }
        }

        StationSchematicPresentation.LayoutLinearTurnbacks(rawEdgePoints, state.Draft.Topology.Edges,
            state.Draft.Topology.TurnbackFacilities, width);
        StationSchematicPresentation.ApplyNodeLayout(rawEdgePoints, state.Draft.Topology.Nodes,
            state.Draft.Topology.Edges, mainlineY, laneSpacing / 2, 60, width - 120);
        var edgeGeometries = TopologySchematicGeometry.Build(state.Draft.Topology.Edges
            .Where(edge => rawEdgePoints.ContainsKey(edge.TrackEdgeId))
            .Select(edge =>
            {
                var points = rawEdgePoints[edge.TrackEdgeId];
                return (edge.TrackEdgeId, points.From, points.To);
            }));

        edgeGeometries = StationSchematicPresentation.ApplyLanes(edgeGeometries,
            state.Draft.Topology.Edges, mainlineY, laneSpacing / 2);
        edgeGeometries = StationSchematicPresentation.ApplyChainage(edgeGeometries, state.Draft, width);

        var platformVisuals = new List<(
            PlatformDefinitionV4 Platform,
            TrackEdgeDefinition Edge,
            TopologySchematicEdgeGeometry Geometry,
            Point Start,
            Point End,
            Point Stop)>();
        foreach (var platform in state.Draft.Topology.Platforms)
        {
            var edge = state.Draft.Topology.Edges.FirstOrDefault(item =>
                item.TrackEdgeId.Equals(platform.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
            if (edge is null || !edgeGeometries.TryGetValue(edge.TrackEdgeId, out var geometry)) continue;
            var startRatio = edge.LengthMeters <= 0 ? 0 : platform.PlatformStartOffsetMeters / edge.LengthMeters;
            var endRatio = edge.LengthMeters <= 0 ? 0 : platform.PlatformEndOffsetMeters / edge.LengthMeters;
            var stopRatio = edge.LengthMeters <= 0 ? 0 : platform.StopPositionOffsetMeters / edge.LengthMeters;
            platformVisuals.Add((platform, edge, geometry, geometry.PointAt(startRatio), geometry.PointAt(endRatio), geometry.PointAt(stopRatio)));
        }

        var stationCenters = StationSchematicPresentation.DrawPlatforms(canvas, state.Draft.Topology.Platforms,
            state.Draft.Topology.Edges, edgeGeometries, mainlineY);
        var stationVisuals = state.Draft.Topology.Stations
            .Select(station =>
            {
                var platforms = platformVisuals.Where(item =>
                        item.Platform.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Platform.PlatformId, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return (
                    Station: station,
                    Platforms: platforms,
                    AnchorX: stationCenters.GetValueOrDefault(station.StationId, double.NaN),
                    MinY: platforms.Length == 0 ? mainlineY : platforms.Min(item => item.Geometry.Points.Min(p => p.Y)),
                    MaxY: platforms.Length == 0 ? mainlineY : platforms.Max(item => item.Geometry.Points.Max(p => p.Y)));
            })
            .Where(item => double.IsFinite(item.AnchorX))
            .OrderBy(item => item.AnchorX)
            .ToArray();


        foreach (var connection in state.Draft.Topology.DirectedConnections)
        {
            if (!edgeGeometries.TryGetValue(connection.FromTrackEdgeId, out var fromGeometry)
                || !edgeGeometries.TryGetValue(connection.ToTrackEdgeId, out var toGeometry)) continue;
            var from = connection.FromDirection == TraversalDirection.Forward ? fromGeometry.To : fromGeometry.From;
            var to = connection.ToDirection == TraversalDirection.Forward ? toGeometry.From : toGeometry.To;

            if (connection.FromTrackEdgeId == connection.ToTrackEdgeId) continue;
            var incoming = connection.FromDirection == TraversalDirection.Forward
                ? fromGeometry.To - fromGeometry.PointAt(.99) : fromGeometry.From - fromGeometry.PointAt(.01);
            var outgoing = connection.ToDirection == TraversalDirection.Forward
                ? toGeometry.PointAt(.01) - toGeometry.From : toGeometry.PointAt(.99) - toGeometry.To;
            StationSchematicPresentation.DrawConnection(canvas, from, to, incoming, outgoing, new SolidColorBrush(railColor),
                $"合法轉向：{connection.FromTrackEdgeId} ({connection.FromDirection}) → {connection.ToTrackEdgeId} ({connection.ToDirection})");
        }

        foreach (var edge in state.Draft.Topology.Edges)
        {
            if (!edgeGeometries.TryGetValue(edge.TrackEdgeId, out var geometry)) continue;
            var line = new Polyline
            {
                Points = new PointCollection(geometry.Points),
                Stroke = new SolidColorBrush(railColor),
                StrokeThickness = 5,
                StrokeLineJoin = PenLineJoin.Round,
                ToolTip = $"{edge.TrackEdgeId}\n{UiDisplayText.Enum(edge.Kind)}\n{edge.LengthMeters:0.#} m"
            };
            line.MouseLeftButtonUp += (_, _) => selectionDetails.Text = $"軌道區段\n{edge.TrackEdgeId}\n{UiDisplayText.Enum(edge.Kind)}\n{edge.LengthMeters:0.#} m\n{edge.FromNodeId} → {edge.ToNodeId}";
            canvas.Children.Add(line);
            // 短渡線的雙向箭頭會蓋住 X 形交叉；方向仍可在軌道資料查看。
            if (edge.Kind == TrackEdgeKind.Crossover || (geometry.To - geometry.From).Length < 70) continue;
            if (edge.Directionality == TrackDirectionality.Bidirectional)
            {
                AddDirectionArrow(geometry, 0.38, false, edge.TrackEdgeId);
                AddDirectionArrow(geometry, 0.62, true, edge.TrackEdgeId);
            }
            else
            {
                AddDirectionArrow(geometry, 0.5, edge.Directionality == TrackDirectionality.ForwardOnly, edge.TrackEdgeId);
            }

        }

        foreach (var node in state.Draft.Topology.Nodes.Where(node => node.Kind == TrackNodeKind.BufferStop))
        {
            var edge = state.Draft.Topology.Edges.FirstOrDefault(item =>
                item.FromNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase)
                || item.ToNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase));
            if (edge is null || !edgeGeometries.TryGetValue(edge.TrackEdgeId, out var geometry)) continue;
            var atStart = edge.FromNodeId.Equals(node.NodeId, StringComparison.OrdinalIgnoreCase);
            var point = atStart ? geometry.Points[0] : geometry.Points[^1];
            var inner = atStart ? geometry.Points[Math.Min(1, geometry.Points.Count - 1)] : geometry.Points[Math.Max(0, geometry.Points.Count - 2)];
            var tangent = point - inner;
            if (tangent.Length <= double.Epsilon) continue;
            tangent.Normalize();
            var normal = new Vector(-tangent.Y, tangent.X) * 8;
            canvas.Children.Add(new Line
            {
                X1 = point.X - normal.X,
                Y1 = point.Y - normal.Y,
                X2 = point.X + normal.X,
                Y2 = point.Y + normal.Y,
                Stroke = new SolidColorBrush(railColor),
                StrokeThickness = 5,
                ToolTip = $"{node.NodeId} · {node.Name} · 止衝"
            });
        }

        var stationChainage = StationChainageProjection.TryCreate(state.Draft);
        StationSchematicPresentation.DrawStationNames(canvas, stationVisuals.Select(s => (s.Station.StationId,
            s.Station.Name + (stationChainage?.StationCenters.TryGetValue(s.Station.StationId, out var km) == true ? $"\n{km / 1000:0.000}K" : ""))), width);
        StationSchematicPresentation.DrawChainageReference(canvas, stationChainage, 29);
        var facilityLegendTop = Math.Max(380, canvas.Children.OfType<FrameworkElement>()
            .Where(item => item.Tag is StationSchematicPresentation.StationLabelAnchor)
            .Select(item => { item.Measure(new Size(item.Width, double.PositiveInfinity)); return Canvas.GetTop(item) + item.DesiredSize.Height + 22; })
            .DefaultIfEmpty(380).Max());
        var facilityLegendNextY = facilityLegendTop;
        foreach (var facility in state.Draft.Topology.TurnbackFacilities.Cast<object>().Concat(state.Draft.Topology.PassingFacilities))
        {
            var (name, edgeId, kind) = facility switch
            {
                TurnbackFacilityDefinition turnback => (turnback.Name, turnback.Traversals.Count > 0 ? turnback.Traversals[0].TrackEdgeId : null, UiDisplayText.Enum(turnback.Kind)),
                PassingFacilityDefinition passing => (passing.Name, passing.Traversals.Count > 0 ? passing.Traversals[0].TrackEdgeId : null, "待避設施"),
                _ => ("", null, "")
            };
            var edge = state.Draft.Topology.Edges.FirstOrDefault(item => item.TrackEdgeId.Equals(edgeId, StringComparison.OrdinalIgnoreCase));
            if (edge is null || !edgeGeometries.TryGetValue(edge.TrackEdgeId, out var geometry)) continue;
            var x = 22d; var y = facilityLegendNextY;
            var marker = new Rectangle { Width = 9, Height = 9, Fill = new SolidColorBrush(Color.FromRgb(244, 173, 70)), ToolTip = $"{kind}：{name}" };
            marker.MouseLeftButtonUp += (_, _) => selectionDetails.Text = $"設施\n{kind}\n{name}\n{edgeId}";
            Canvas.SetLeft(marker, x - 4.5); Canvas.SetTop(marker, y); canvas.Children.Add(marker);
            var label = new TextBlock { Text = name, Width = Math.Max(1, width - 60), TextWrapping = TextWrapping.Wrap, ToolTip = $"{kind}：{name}\n{edgeId}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(153, 75, 17)) };
            label.Measure(new Size(label.Width, double.PositiveInfinity));
            facilityLegendNextY += Math.Max(22, label.DesiredSize.Height + 8);
            Canvas.SetLeft(label, x + 8); Canvas.SetTop(label, y - 3); canvas.Children.Add(label);
        }

        canvas.Height = Math.Max(520, facilityLegendNextY + 16);
        StationSchematicPresentation.DrawLayoutWarnings(canvas);

        void AddDirectionArrow(TopologySchematicEdgeGeometry geometry, double ratio, bool forward, string edgeId)
        {
            var before = geometry.PointAt(Math.Max(0, ratio - 0.02));
            var after = geometry.PointAt(Math.Min(1, ratio + 0.02));
            var tangent = after - before;
            if (tangent.Length <= double.Epsilon) return;
            tangent.Normalize();
            if (!forward) tangent *= -1;
            var normal = new Vector(-tangent.Y, tangent.X);
            var center = geometry.PointAt(ratio);
            canvas.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    center + tangent * 8,
                    center - tangent * 5 + normal * 4.5,
                    center - tangent * 5 - normal * 4.5
                },
                Fill = new SolidColorBrush(railColor),
                Stroke = Brushes.White,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                ToolTip = $"{edgeId} · {(forward ? "正向" : "反向")}"
            });
        }
    }

    private void PopulateDispatchRows()
    {
        dispatch.Runs.Clear();
        var routes = state.Draft.DirectionRouteBindings.ToDictionary(item => item.Direction, item => item.ServiceRouteId);
        foreach (var plan in state.Draft.Dispatch.SimpleHeadwayPlans ?? [])
        {
            var route = routes.GetValueOrDefault(plan.Direction, "—");
            for (var index = 0; index < plan.RunCount; index++)
                dispatch.Runs.Add(new DispatchRunEditorViewModel($"{plan.ServiceTypeId}-{index + 1:000}", TimeSpan.FromSeconds(plan.FirstDepartureTimeSeconds + plan.HeadwaySeconds * index).ToString("hh\\:mm\\:ss"), plan.ServiceTypeId, route, DescribePlatform(plan.OriginPlatformId), plan.VehicleTypeId ?? "自動"));
        }
        foreach (var row in state.Draft.Dispatch.ManualTimetableRows ?? [])
            dispatch.Runs.Add(new DispatchRunEditorViewModel(row.ServiceRunId ?? "手動", TimeSpan.FromSeconds(row.PlannedDepartureTimeSeconds).ToString("hh\\:mm\\:ss"), row.ServiceTypeId, routes.GetValueOrDefault(row.Direction, "—"), DescribePlatform(row.OriginPlatformId), row.VehicleTypeId ?? "自動"));
    }

    private string DescribePlatform(string? platformId)
    {
        var platform = state.Draft.Topology.Platforms.FirstOrDefault(item => item.PlatformId.Equals(platformId, StringComparison.OrdinalIgnoreCase));
        if (platform is null) return "—";
        var station = state.Draft.Topology.Stations.FirstOrDefault(item => item.StationId.Equals(platform.StationId, StringComparison.OrdinalIgnoreCase));
        return $"{station?.Name ?? platform.StationId} / {platform.Name}";
    }

    private int CalculateDispatchRunCount(TopologyProjectDocument document) => (document.Dispatch.SimpleHeadwayPlans ?? []).Sum(item => item.RunCount) + (document.Dispatch.ManualTimetableRows?.Length ?? 0);
    private T? Selected<T>() where T : class => workspace.Descendants().OfType<DataGrid>().Select(grid => grid.SelectedItem).OfType<T>().FirstOrDefault();

    private void RunEdit(Func<TopologyProjectDocument> edit)
    {
        try { CommitTableDrafts(); state.Replace(edit()); ReloadViewModels(); RefreshValidation(); }
        catch (EditorValidationException exception) { SetValidationMessages(exception.Messages); }
        catch (SimulationValidationException exception) { ShowErrors(exception.Errors); }
    }

    private void ShowErrors(IEnumerable<string> errors)
    {
        SetValidationMessages(errors.Select(error => ProjectEditorValidationService.CreateError(error, state.Draft)));
    }

    private void SetValidationMessages(IEnumerable<ProjectValidationMessage> messages)
    {
        validationMessages.Reset(messages.Select(message => new ProjectValidationMessageViewModel(message)));
        selectionDetails.Text = string.Join(Environment.NewLine, validationMessages.Select(message => UiDisplayText.ValidationMessage(message.Source.Message)));
    }

    private string NextId(string prefix)
    {
        var existing = state.Draft.Topology.Nodes.Select(item => item.NodeId).Concat(state.Draft.Topology.Edges.Select(item => item.TrackEdgeId))
            .Concat(state.Draft.Topology.Stations.Select(item => item.StationId)).Concat(state.Draft.Topology.Platforms.Select(item => item.PlatformId))
            .Concat(state.Draft.Topology.Resources.Select(item => item.ResourceId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++) { var candidate = $"{prefix}-{index:000}"; if (!existing.Contains(candidate)) return candidate; }
    }

    private static string NextDocumentId(string prefix, IEnumerable<string> existingIds)
    {
        var existing = existingIds.Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}-{index:000}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static double Parse(string value, string field)
    {
        if ((!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) || !double.IsFinite(parsed))
            throw new SimulationValidationException([$"{field}必須是有限數值。"]);
        return parsed;
    }

    private static void ValidatePlatformOffsets(PlatformDefinitionV4 platform, TrackEdgeDefinition edge)
    {
        if (!double.IsFinite(platform.PlatformStartOffsetMeters) || !double.IsFinite(platform.StopPositionOffsetMeters)
            || !double.IsFinite(platform.PlatformEndOffsetMeters) || platform.PlatformStartOffsetMeters < 0
            || platform.PlatformStartOffsetMeters > platform.StopPositionOffsetMeters
            || platform.StopPositionOffsetMeters > platform.PlatformEndOffsetMeters
            || platform.PlatformEndOffsetMeters > edge.LengthMeters)
        {
            throw new SimulationValidationException([$"月台「{platform.Name}」的起始、停車與終止偏移量必須位於軌道區段「{edge.TrackEdgeId}」的 0 至 {edge.LengthMeters:0.###} m 範圍，且依序遞增。"]);
        }
    }

    private Dictionary<string, string>? Ask(string title, params (string Label, string Initial)[] fields)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 500, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        var inputs = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            panel.Children.Add(new TextBlock { Text = field.Label, Margin = new Thickness(0, 5, 0, 3) });
            var input = new TextBox { Text = field.Initial }; inputs.Add(field.Label, input); panel.Children.Add(input);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(CreateButton("建立", (_, _) => dialog.DialogResult = true, false));
        buttons.Children.Add(CreateButton("取消", (_, _) => dialog.DialogResult = false, true));
        panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? inputs.ToDictionary(item => item.Key, item => item.Value.Text, StringComparer.Ordinal) : null;
    }

    private string? Choose(string title, string label, IReadOnlyList<string> values, string? current)
    {
        var dialog = new Window { Title = title, Owner = this, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        var choices = new ComboBox { ItemsSource = values, SelectedItem = values.FirstOrDefault(value => value.Equals(current, StringComparison.OrdinalIgnoreCase)) ?? values.FirstOrDefault() };
        panel.Children.Add(choices);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(CreateButton("選取", (_, _) => dialog.DialogResult = true, false));
        buttons.Children.Add(CreateButton("取消", (_, _) => dialog.DialogResult = false, true));
        panel.Children.Add(buttons); dialog.Content = panel;
        return dialog.ShowDialog() == true ? choices.SelectedItem as string : null;
    }

    private sealed record NavItem(ProjectWorkspacePage Page, string Label, Action Open);
    private sealed record ValidationEditorTarget(DataGrid Grid, IReadOnlyList<TabItem> Tabs);

    private sealed class EditorValidationException(IReadOnlyList<ProjectValidationMessage> messages) : Exception
    {
        public IReadOnlyList<ProjectValidationMessage> Messages { get; } = messages;
    }
}

internal static class TopologyEditorVisualTreeExtensions
{
    public static IEnumerable<DependencyObject> Descendants(this DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in child.Descendants()) yield return descendant;
        }
    }

    public static void Reset<T>(this ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}
