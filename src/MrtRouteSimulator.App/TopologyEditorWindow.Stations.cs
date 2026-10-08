using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 車站中心、軌道／設施與低頻 topology 資料頁。
/// 這些頁面只操作既有 editor VM；篩選 view 的 item 一律是原集合中的同一個 VM，
/// 不建立第二份 station 或 operation 來源。
/// </summary>
internal sealed partial class TopologyEditorWindow
{
    private StationEditorViewModel? selectedStationPageStation;
    private PlatformEditorViewModel? selectedStationPagePlatform;
    private StationOperationEditorViewModel? selectedStationPageOperation;
    private TurnbackOperationEditorViewModel? selectedStationPageTurnback;
    private PassingOperationEditorViewModel? selectedStationPagePassing;
    private ListCollectionView? stationPageStationsView;
    private ListCollectionView? stationPagePlatformsView;
    private ListCollectionView? stationPageStationOperationsView;
    private ListCollectionView? stationPageTurnbacksView;
    private ListCollectionView? stationPagePassingsView;
    private StackPanel? stationPageDetail;
    private TextBox? stationPageNameTextBox;
    private TextBox? stationPageDwellTextBox;
    private TextBox? stationPageSearchTextBox;
    private DataGrid? stationPagePlatformGrid;
    private StackPanel? stationPagePlatformDetail;
    private TextBox? stationPagePlatformNameTextBox;
    private TextBox? stationPagePlatformStartTextBox;
    private TextBox? stationPagePlatformStopTextBox;
    private TextBox? stationPagePlatformEndTextBox;
    private TextBox? stationPagePlatformLengthTextBox;
    private CheckBox? stationPagePlatformPassengerCheckBox;
    private DataGrid? stationPageStationOperationGrid;
    private ListBox? stationPageStationList;
    private TextBox? stationPageOperationIdTextBox;
    private TextBox? stationPageOperationDwellTextBox;
    private TextBox? stationPageTurnbackIdTextBox;
    private TextBox? stationPageTurnbackDwellTextBox;
    private TextBox? stationPagePassingIdTextBox;
    private Dictionary<string, string> stationPageRawFields = new(StringComparer.OrdinalIgnoreCase);
    private bool stationPageUpdating;

    /// <summary>由 shell 導覽使用；進入頁面前先提交目前頁面的 DataGrid 與文字草稿。</summary>
    private void ShowStationsPage()
    {
        CommitTableDrafts();
        var preferredStationId = selectedStationPageStation?.Id;
        ResetStationPageControlState();
        RegisterPageCommitHook("station-page", CommitStationPageDrafts);

        var panel = NewPage("車站與月台", "從車站開始編輯本站基本資料、月台與本站營運作業。篩選結果直接來自同一份 Schema 8 草稿；示意圖只作呈現，不改變實體接軌資料。");
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0), LastChildFill = true };
        var searchPanel = new StackPanel();
        searchPanel.Children.Add(new TextBlock { Text = "搜尋車站", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stationPageSearchTextBox = new TextBox { Name = "StationSearchTextBox", Height = 28, ToolTip = "輸入車站編號或名稱" };
        AutomationProperties.SetName(stationPageSearchTextBox, "車站搜尋");
        stationPageSearchTextBox.TextChanged += (_, _) =>
        {
            stationPageStationsView?.Refresh();
            if (stationPageStationList?.SelectedItem is null) SelectFirstStationPageItem();
        };
        searchPanel.Children.Add(stationPageSearchTextBox);
        DockPanel.SetDock(searchPanel, Dock.Top);
        left.Children.Add(searchPanel);

        var stationActions = new StackPanel { Orientation = Orientation.Horizontal, Height = 36, Margin = new Thickness(0, 8, 0, 0) };
        var addStation = CreateButton("新增車站", (_, _) => AddStationFromStationPage(), true);
        addStation.Name = "AddStationButton"; AutomationProperties.SetName(addStation, "新增車站"); stationActions.Children.Add(addStation);
        var removeStation = CreateButton("刪除", (_, _) => DeleteStationFromStationPage(), true);
        removeStation.Name = "DeleteStationButton"; AutomationProperties.SetName(removeStation, "刪除選取車站"); stationActions.Children.Add(removeStation);
        DockPanel.SetDock(stationActions, Dock.Bottom);
        left.Children.Add(stationActions);

        stationPageStationsView = new ListCollectionView(infrastructure.Stations);
        stationPageStationsView.Filter = item => item is StationEditorViewModel station
            && (string.IsNullOrWhiteSpace(stationPageSearchTextBox?.Text)
                || station.Id.Contains(stationPageSearchTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
                || station.Name.Contains(stationPageSearchTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        stationPageStationList = new ListBox
        {
            Name = "StationListBox", ItemsSource = stationPageStationsView, DisplayMemberPath = nameof(StationEditorViewModel.Name),
            MinHeight = 120, Margin = new Thickness(0, 8, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(stationPageStationList, "車站清單");
        stationPageStationList.SelectionChanged += StationPageStationSelectionChanged;
        left.Children.Add(stationPageStationList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        stationPageDetail = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var scroll = new ScrollViewer
        {
            Content = stationPageDetail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Name = "StationDetailScrollViewer"
        };
        AutomationProperties.SetName(scroll, "車站詳細編輯捲動區");
        Grid.SetColumn(scroll, 1);
        root.Children.Add(scroll);
        panel.Children.Add(root);
        workspace.Content = panel;

        // NewPage uses a StackPanel, so its child would otherwise be measured
        // with unlimited height. Give the station list and detail scroll the
        // remaining workspace height, including after a narrow-window resize.
        void SizeStationPage()
        {
            var headerHeight = ((FrameworkElement)panel.Children[0]).ActualHeight
                + ((FrameworkElement)panel.Children[1]).ActualHeight;
            // Reserve room for the list's margin and the page's lower inset;
            // DockPanel's fill child can otherwise push the bottom actions
            // slightly past the visible workspace at both tested sizes.
            root.Height = Math.Max(220, workspace.ActualHeight - headerHeight - 24);
        }
        SizeChangedEventHandler resize = (_, _) => SizeStationPage();
        workspace.SizeChanged += resize;
        root.Loaded += (_, _) => SizeStationPage();
        root.Unloaded += (_, _) => workspace.SizeChanged -= resize;

        // A newly constructed ListBox can adopt its collection view's current
        // item before SelectionChanged is attached. Rebuild the detail explicitly
        // when revisiting this page, while preserving the station the user had open.
        selectedStationPageStation = null;
        selectedStationPagePlatform = null;
        selectedStationPageOperation = null;
        selectedStationPageTurnback = null;
        selectedStationPagePassing = null;
        SelectFirstStationPageItem(preferredStationId);
    }

    private void ResetStationPageControlState()
    {
        // Navigation commits the live page hook before this method is reached. The
        // next page must begin with no stale selection or detached controls, while
        // stationPageRawFields remains so an invalid draft string can be rebuilt.
        selectedStationPageStation = null;
        selectedStationPagePlatform = null;
        selectedStationPageOperation = null;
        selectedStationPageTurnback = null;
        selectedStationPagePassing = null;
        stationPageStationsView = null;
        stationPagePlatformsView = null;
        stationPageStationOperationsView = null;
        stationPageTurnbacksView = null;
        stationPagePassingsView = null;
        stationPageDetail = null;
        stationPageNameTextBox = null;
        stationPageDwellTextBox = null;
        stationPageSearchTextBox = null;
        stationPagePlatformGrid = null;
        stationPagePlatformDetail = null;
        stationPagePlatformNameTextBox = null;
        stationPagePlatformStartTextBox = null;
        stationPagePlatformStopTextBox = null;
        stationPagePlatformEndTextBox = null;
        stationPagePlatformLengthTextBox = null;
        stationPagePlatformPassengerCheckBox = null;
        stationPageStationOperationGrid = null;
        stationPageStationOperationDetail = null;
        stationPageTurnbackOperationDetail = null;
        stationPagePassingOperationDetail = null;
        stationPageStationList = null;
        stationPageOperationIdTextBox = null;
        stationPageOperationDwellTextBox = null;
        stationPageTurnbackIdTextBox = null;
        stationPageTurnbackDwellTextBox = null;
        stationPagePassingIdTextBox = null;
        stationPageUpdating = false;
    }

    /// <summary>顯示 edge、facility、speed limit、坡度與曲線等常用實體資料。</summary>
    private void ShowTracksPage()
    {
        CommitTableDrafts();

        var panel = NewPage("軌道與設施", "軌道區段、設施與區段內資料維持 topology-native 權威。接軌側 A/B 只顯示既有值；本頁不從示意座標猜測 port-side。");
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Name = "TracksPageScrollViewer", MinHeight = 420, MaxHeight = 500 };
        AutomationProperties.SetName(scroll, "軌道與設施捲動區");
        var content = new StackPanel();
        content.Children.Add(TrackPageGroup("軌道區段", "edge 的端點、長度、方向、種類、速限、接軌側與衝突資源。", NamedEditorTabContent("TrackEdgeGrid", "軌道區段編輯表", ProjectValidationTargetKind.Edge, "軌道區段", infrastructure.Edges, AddEdge, DeleteSelectedEdge)));
        content.Children.Add(TrackPageGroup("設施", "可由設施詳細編輯修改折返與待避的實體 traversal、位置、月台及衝突資源。", NamedEditorTabContent("FacilityGrid", "設施編輯表", ProjectValidationTargetKind.TurnbackFacility, "設施", infrastructure.Facilities, ShowFacilityMenu, DeleteSelectedFacility, true, EditSelectedFacility)));
        content.Children.Add(TrackPageGroup("方向別速限", "速限區間以 edge-local offset 與 traversal direction 保存；顯示值使用 km/h。", NamedEditorTabContent("TrackSpeedLimitGrid", "區段速限編輯表", ProjectValidationTargetKind.SpeedLimit, "速限", infrastructure.SpeedLimits, AddSpeedLimit, DeleteSelectedSpeedLimit)));
        content.Children.Add(TrackPageGroup("坡度與曲線", "附著於 edge 的局部幾何資料；長度與偏移仍是原始資料。", TrackPageStack(
            NamedEditorTabContent("TrackGradientGrid", "坡度進階資料表", ProjectValidationTargetKind.Gradient, "坡度", infrastructure.Gradients, AddGradient, DeleteSelectedGradient),
            NamedEditorTabContent("TrackCurveGrid", "曲線進階資料表", ProjectValidationTargetKind.Edge, "曲線", curves, AddCurve, DeleteSelectedCurve))));
        content.Children.Add(TrackPageGroup("折返／待避作業", "作業引用維持原始 operation VM；本站如何使用設施請在車站頁以名稱與多選參照操作。", TrackPageStack(
            NamedEditorTabContent("TurnbackOperationGrid", "折返作業編輯表", ProjectValidationTargetKind.TurnbackFacility, "折返作業", turnbackOperations, AddTurnbackOperation, DeleteSelectedTurnbackOperation),
            NamedEditorTabContent("PassingOperationGrid", "待避作業編輯表", ProjectValidationTargetKind.PassingFacility, "待避作業", passingOperations, AddPassingOperation, DeleteSelectedPassingOperation))));

        scroll.Content = content;
        panel.Children.Add(scroll);
        workspace.Content = panel;
    }

    /// <summary>保留低頻與完整 Schema 8 欄位的入口，避免一般頁面整理時遺漏資料。</summary>
    private void ShowAdvancedDataPage()
    {
        CommitTableDrafts();

        var panel = NewPage("進階資料", "低頻 topology 欄位集中於此；每張表仍編輯既有 draft VM。節點、連接與示意位置不代表可由 UI 推導的實體接軌側。");
        var content = new StackPanel();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Name = "AdvancedDataPageScrollViewer", MinHeight = 420, MaxHeight = 500 };
        AutomationProperties.SetName(scroll, "進階資料捲動區");

        content.Children.Add(TrackPageGroup("節點", "節點種類與示意位置／lane；不從示意位置推導 port-side。", NamedEditorTabContent("AdvancedNodeGrid", "節點進階資料表", ProjectValidationTargetKind.Node, "軌道節點", infrastructure.Nodes, AddNode, DeleteSelectedNode)));
        content.Children.Add(TrackPageGroup("有向接續", "只保留明確列出的 edge／traversal 連接；方向是拓撲資料。", NamedEditorTabContent("DirectedConnectionGrid", "有向接續進階資料表", ProjectValidationTargetKind.Edge, "有向接續", directedConnections, AddDirectedConnection, DeleteSelectedDirectedConnection)));
        content.Children.Add(TrackPageGroup("衝突資源", "設施、edge 與進路可共同引用的資源。", NamedResourceContent()));
        content.Children.Add(TrackPageGroup("曲線與坡度", "附著於 edge 的局部區間資料，使用 m／permille。", TrackPageStack(
            NamedEditorTabContent("TrackCurveGrid", "曲線進階資料表", ProjectValidationTargetKind.Edge, "曲線", curves, AddCurve, DeleteSelectedCurve),
            NamedEditorTabContent("TrackGradientGrid", "坡度進階資料表", ProjectValidationTargetKind.Gradient, "坡度", infrastructure.Gradients, AddGradient, DeleteSelectedGradient))));

        var schematicBox = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 0)
        };
        var schematicPanel = new StackPanel();
        schematicPanel.Children.Add(new TextBlock { Text = "圖面配置", FontSize = 16, FontWeight = FontWeights.SemiBold });
        schematicPanel.Children.Add(new TextBlock { Text = "完整配線預覽沿用既有 StationSchematicPresentation；X/Y 與 lane 僅供呈現，實體 edge-local position、port-side 與 directed connection 仍由上方資料表維持。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 8) });
        var openSchematic = CreateButton("開啟完整配線預覽", (_, _) => ShowSchematic(), true);
        openSchematic.Name = "OpenSchematicPreviewButton";
        AutomationProperties.SetName(openSchematic, "開啟完整配線預覽");
        schematicPanel.Children.Add(openSchematic);
        schematicBox.Child = schematicPanel;
        content.Children.Add(schematicBox);
        scroll.Content = content;
        panel.Children.Add(scroll);
        workspace.Content = panel;
    }

    private static TextBlock SectionHeading(string title, string description) => new()
    {
        Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4), ToolTip = description
    };

    private static Expander TrackPageGroup(string title, string description, object? content)
    {
        var element = content as UIElement ?? new TextBlock { Text = "此資料表目前沒有可顯示內容。" };
        var group = new Expander
        {
            Header = title, IsExpanded = true, Content = element, Margin = new Thickness(0, 6, 0, 2), ToolTip = description
        };
        AutomationProperties.SetName(group, title);
        return group;
    }

    private static StackPanel TrackPageStack(params object?[] contents)
    {
        var panel = new StackPanel();
        foreach (var content in contents.OfType<UIElement>()) panel.Children.Add(content);
        return panel;
    }

    private UIElement? NamedEditorTabContent<T>(string gridName, string automationName, ProjectValidationTargetKind kind,
        string title, ObservableCollection<T> source, Action add, Action? delete, bool readOnly = false, Action? edit = null)
    {
        var tab = EditorTab(kind, title, source, add, delete, readOnly, edit);
        var content = tab.Content as UIElement;
        var root = content as DependencyObject;
        var grid = LogicalDescendants<DataGrid>(root).FirstOrDefault();
        if (grid is null) { tab.Content = null; return content; }
        grid.Name = gridName;
        AutomationProperties.SetName(grid, automationName);

        // EditorTab is shared by the older infrastructure page, so name its
        // controls here after construction. This keeps the toolbar intact and
        // gives UI tests a stable entry point for both add and remove actions.
        foreach (var button in LogicalDescendants<Button>(root))
        {
            var label = button.Content?.ToString();
            if (string.Equals(label, "新增", StringComparison.Ordinal)
                || string.Equals(label, "新增設施…", StringComparison.Ordinal))
            {
                button.Name = $"Add{gridName}Button";
                AutomationProperties.SetName(button, $"新增{title}");
            }
            else if (string.Equals(label, "刪除", StringComparison.Ordinal))
            {
                button.Name = $"Delete{gridName}Button";
                AutomationProperties.SetName(button, $"刪除{title}");
            }
        }
        tab.Content = null;
        return content;
    }

    private UIElement? NamedResourceContent()
    {
        var tab = ResourceTab();
        var content = tab.Content as UIElement;
        var root = content as DependencyObject;
        var grids = LogicalDescendants<DataGrid>(root).ToArray();
        if (grids.Length > 0) { grids[0].Name = "ConflictResourceGrid"; AutomationProperties.SetName(grids[0], "衝突資源自動資料表"); }
        if (grids.Length > 1) { grids[1].Name = "ManualResourceGrid"; AutomationProperties.SetName(grids[1], "手動衝突資源資料表"); }
        foreach (var button in LogicalDescendants<Button>(content))
        {
            var label = button.Content?.ToString();
            if (string.Equals(label, "新增", StringComparison.Ordinal))
            {
                button.Name = "AddManualResourceButton";
                AutomationProperties.SetName(button, "新增手動衝突資源");
            }
            else if (string.Equals(label, "刪除", StringComparison.Ordinal))
            {
                button.Name = "DeleteResourceButton";
                AutomationProperties.SetName(button, "刪除衝突資源");
            }
        }
        tab.Content = null;
        return content;
    }

    private static IEnumerable<T> LogicalDescendants<T>(object? root) where T : class
    {
        if (root is T match) yield return match;
        if (root is not DependencyObject dependencyObject) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject).OfType<object>())
        {
            foreach (var descendant in LogicalDescendants<T>(child)) yield return descendant;
        }
    }

    private DataGrid NamedAdvancedGrid<T>(string name, string automationName, ObservableCollection<T> source, bool readOnly, double minHeight)
    {
        var grid = CreateGrid(source, readOnly);
        grid.Name = name;
        grid.MinHeight = minHeight;
        AutomationProperties.SetName(grid, automationName);
        return grid;
    }

    private static void OrderStationPlatformColumns(DataGrid grid)
    {
        var preferred = new[]
        {
            nameof(PlatformEditorViewModel.Name),
            nameof(PlatformEditorViewModel.Direction),
            nameof(PlatformEditorViewModel.EffectiveLengthMeters),
            nameof(PlatformEditorViewModel.StopPositionReference),
            nameof(PlatformEditorViewModel.DisplaySide),
            nameof(PlatformEditorViewModel.PlatformNumber),
            nameof(PlatformEditorViewModel.StartOffsetMeters),
            nameof(PlatformEditorViewModel.StopOffsetMeters),
            nameof(PlatformEditorViewModel.EndOffsetMeters),
            nameof(PlatformEditorViewModel.PassengerService),
            nameof(PlatformEditorViewModel.PlatformBodyId),
            nameof(PlatformEditorViewModel.Station),
            nameof(PlatformEditorViewModel.TrackEdge),
            nameof(PlatformEditorViewModel.AllowedVehicleTypeIds),
            nameof(PlatformEditorViewModel.AllowedServiceTypeIds),
            nameof(PlatformEditorViewModel.Id)
        };
        var next = 0;
        foreach (var property in preferred)
        {
            var column = grid.Columns.FirstOrDefault(item => string.Equals(item.SortMemberPath, property, StringComparison.Ordinal));
            if (column is null) continue;
            column.DisplayIndex = next++;
        }
    }

    private void SelectFirstStationPageItem(string? preferredStationId = null)
    {
        if (stationPageStationList is null) return;
        var stations = stationPageStationsView?.Cast<object>().OfType<StationEditorViewModel>().ToArray() ?? [];
        var first = stations.FirstOrDefault(station => string.Equals(station.Id, preferredStationId, StringComparison.OrdinalIgnoreCase))
            ?? stations.FirstOrDefault();
        if (first is not null)
        {
            stationPageStationList.SelectedItem = first;
            if (!ReferenceEquals(selectedStationPageStation, first) || stationPageDetail?.Children.Count == 0)
            {
                selectedStationPageStation = first;
                BuildStationPageDetail(first);
            }
        }
        else { selectedStationPageStation = null; stationPageDetail?.Children.Clear(); }
    }

    private void StationPageStationSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (stationPageUpdating || stationPageStationList?.SelectedItem is not StationEditorViewModel station) return;
        var previous = selectedStationPageStation;
        try
        {
            if (previous is not null) CommitStationPageDrafts();
            selectedStationPageStation = station;
            selectedStationPagePlatform = null;
            selectedStationPageOperation = null;
            selectedStationPageTurnback = null;
            selectedStationPagePassing = null;
            BuildStationPageDetail(station);
        }
        catch (EditorValidationException exception)
        {
            stationPageUpdating = true;
            stationPageStationList.SelectedItem = previous;
            stationPageUpdating = false;
            SetValidationMessages(exception.Messages);
        }
    }

    private void BuildStationPageDetail(StationEditorViewModel station)
    {
        if (stationPageDetail is null) return;
        // The detail panels are nested children of the previous station container.
        // Drop those control instances before rebuilding so a station switch never
        // attempts to attach a WPF element that still belongs to the old container.
        stationPagePlatformDetail = null;
        stationPageStationOperationDetail = null;
        stationPageTurnbackOperationDetail = null;
        stationPagePassingOperationDetail = null;
        stationPageDetail.Children.Clear();
        stationPageRawFields.TryAdd(StationFieldKey(station, "Name"), station.Name);
        stationPageRawFields.TryAdd(StationFieldKey(station, "Dwell"), station.DefaultDwellSeconds.ToString(CultureInfo.InvariantCulture));

        var heading = new TextBlock { Text = $"{station.Name} 〔{station.Id}〕", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), Name = "SelectedStationHeading" };
        AutomationProperties.SetName(heading, $"目前車站 {station.Id}");
        stationPageDetail.Children.Add(heading);
        stationPageDetail.Children.Add(BuildStationBasicEditor(station));

        stationPageDetail.Children.Add(SectionHeading("月台配置", "本站的所有月台直接篩選自 infrastructure.Platforms；選取列後可查看完整欄位。"));
        stationPagePlatformsView = new ListCollectionView(infrastructure.Platforms);
        stationPagePlatformsView.Filter = item => item is PlatformEditorViewModel platform && IdEquals(platform.Station, station.Id);
        stationPagePlatformGrid = CreateGrid(stationPagePlatformsView, true);
        stationPagePlatformGrid.Name = "StationPlatformGrid";
        stationPagePlatformGrid.MinHeight = 150;
        stationPagePlatformGrid.AutoGeneratingColumn += (_, args) =>
        {
            if (args.PropertyName is nameof(PlatformEditorViewModel.Direction)
                or nameof(PlatformEditorViewModel.StopPositionReference)
                or nameof(PlatformEditorViewModel.DisplaySide))
            {
                args.Column = new DataGridTextColumn
                {
                    Header = UiDisplayText.Header(args.PropertyName),
                    Binding = new Binding(args.PropertyName) { Converter = new TraditionalChineseEnumConverter() },
                    IsReadOnly = true
                };
            }
        };
        stationPagePlatformGrid.AutoGeneratedColumns += (_, _) => OrderStationPlatformColumns(stationPagePlatformGrid);
        stationPagePlatformGrid.SelectionChanged += (_, _) =>
        {
            selectedStationPagePlatform = stationPagePlatformGrid.SelectedItem as PlatformEditorViewModel;
            selectionDetails.Text = DescribeSelection(selectedStationPagePlatform, "選取月台後可查看屬性。");
            BuildPlatformDetail(selectedStationPagePlatform);
        };
        AutomationProperties.SetName(stationPagePlatformGrid, $"{station.Id} 月台清單");
        var platformActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addPlatform = CreateButton("新增月台…", (_, _) => AddPlatformFromStationPage(), true);
        addPlatform.Name = "AddPlatformButton"; AutomationProperties.SetName(addPlatform, "新增本站月台"); platformActions.Children.Add(addPlatform);
        var deletePlatform = CreateButton("刪除月台", (_, _) => DeletePlatformFromStationPage(), true);
        deletePlatform.Name = "DeletePlatformButton"; AutomationProperties.SetName(deletePlatform, "刪除選取月台"); platformActions.Children.Add(deletePlatform);
        stationPageDetail.Children.Add(platformActions);
        stationPageDetail.Children.Add(stationPagePlatformGrid);
        stationPagePlatformDetail = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stationPageDetail.Children.Add(stationPagePlatformDetail);
        BuildPlatformDetail(selectedStationPagePlatform);
        stationPageDetail.Children.Add(new TextBlock { Text = "月台編號、PlatformBodyId、本站與附著區段保留為參照欄位；名稱、顯示側、停車位置、edge-local 起訖偏移、允許方向、有效長度、乘客服務及車型／服務集合可在下方詳細編輯。", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(75, 86, 106)), Margin = new Thickness(0, 5, 0, 0) });

        stationPageDetail.Children.Add(SectionHeading("本站車站作業", "同一 station operation VM 的 StationId、月台集合、折返／待避 operation 集合與預設停站秒數。"));
        stationPageStationOperationsView = new ListCollectionView(stationOperations);
        stationPageStationOperationsView.Filter = item => item is StationOperationEditorViewModel operation && IdEquals(operation.Station, station.Id);
        stationPageStationOperationGrid = CreateGrid(stationPageStationOperationsView, true);
        stationPageStationOperationGrid.Name = "StationOperationGrid";
        stationPageStationOperationGrid.MinHeight = 130;
        stationPageStationOperationGrid.SelectionChanged += (_, _) =>
        {
            selectedStationPageOperation = stationPageStationOperationGrid.SelectedItem as StationOperationEditorViewModel;
            selectedStationPageTurnback = null;
            selectedStationPagePassing = null;
            BuildStationOperationDetail(station, selectedStationPageOperation);
            RefreshStationOperationViews();
        };
        AutomationProperties.SetName(stationPageStationOperationGrid, $"{station.Id} 車站作業清單");
        var initialStationOperation = stationPageStationOperationsView.Cast<object>().OfType<StationOperationEditorViewModel>().FirstOrDefault();
        if (initialStationOperation is not null)
        {
            selectedStationPageOperation = initialStationOperation;
            stationPageStationOperationGrid.SelectedItem = initialStationOperation;
        }
        stationPageDetail.Children.Add(stationPageStationOperationGrid);
        var stationOperationActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addStationOperation = CreateButton("新增本站作業", (_, _) => AddStationOperationFromStationPage(), true);
        addStationOperation.Name = "AddStationOperationButton"; AutomationProperties.SetName(addStationOperation, "新增本站作業"); stationOperationActions.Children.Add(addStationOperation);
        var deleteStationOperation = CreateButton("刪除本站作業", (_, _) => DeleteStationOperationFromStationPage(), true);
        deleteStationOperation.Name = "DeleteStationOperationButton"; AutomationProperties.SetName(deleteStationOperation, "刪除選取本站作業"); stationOperationActions.Children.Add(deleteStationOperation);
        stationPageDetail.Children.Add(stationOperationActions);
        stationPageDetail.Children.Add(BuildStationOperationChooser(station));

        stationPageDetail.Children.Add(SectionHeading("本站折返作業", "只顯示被本站車站作業引用的同一份 TurnbackOperation VM；O20 的折返可在此選取並編輯。"));
        stationPageTurnbacksView = new ListCollectionView(turnbackOperations);
        stationPageTurnbacksView.Filter = item => item is TurnbackOperationEditorViewModel operation && IsTurnbackReferencedByStation(operation, station.Id);
        var turnbackList = CreateOperationList(stationPageTurnbacksView, "StationTurnbackList", "本站折返作業清單", id =>
        {
            selectedStationPageTurnback = id as TurnbackOperationEditorViewModel;
            BuildTurnbackOperationDetail(selectedStationPageTurnback);
        });
        var turnbackActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addTurnback = CreateButton("新增折返作業", (_, _) => AddTurnbackOperationFromStationPage(), true);
        addTurnback.Name = "AddTurnbackOperationButton"; AutomationProperties.SetName(addTurnback, "新增本站折返作業"); turnbackActions.Children.Add(addTurnback);
        var deleteTurnback = CreateButton("刪除折返作業", (_, _) => DeleteTurnbackOperationFromStationPage(), true);
        deleteTurnback.Name = "DeleteTurnbackOperationButton"; AutomationProperties.SetName(deleteTurnback, "刪除選取折返作業"); turnbackActions.Children.Add(deleteTurnback);
        stationPageDetail.Children.Add(turnbackActions);
        stationPageDetail.Children.Add(turnbackList);
        stationPageDetail.Children.Add(BuildTurnbackOperationDetail(selectedStationPageTurnback));

        stationPageDetail.Children.Add(SectionHeading("本站待避作業", "只顯示被本站車站作業引用的同一份 PassingOperation VM；O04／O13 的待避可在此選取並編輯。"));
        stationPagePassingsView = new ListCollectionView(passingOperations);
        stationPagePassingsView.Filter = item => item is PassingOperationEditorViewModel operation && IsPassingReferencedByStation(operation, station.Id);
        var passingList = CreateOperationList(stationPagePassingsView, "StationPassingList", "本站待避作業清單", id =>
        {
            selectedStationPagePassing = id as PassingOperationEditorViewModel;
            BuildPassingOperationDetail(selectedStationPagePassing);
        });
        var passingActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addPassing = CreateButton("新增待避作業", (_, _) => AddPassingOperationFromStationPage(), true);
        addPassing.Name = "AddPassingOperationButton"; AutomationProperties.SetName(addPassing, "新增本站待避作業"); passingActions.Children.Add(addPassing);
        var deletePassing = CreateButton("刪除待避作業", (_, _) => DeletePassingOperationFromStationPage(), true);
        deletePassing.Name = "DeletePassingOperationButton"; AutomationProperties.SetName(deletePassing, "刪除選取待避作業"); passingActions.Children.Add(deletePassing);
        stationPageDetail.Children.Add(passingActions);
        stationPageDetail.Children.Add(passingList);
        stationPageDetail.Children.Add(BuildPassingOperationDetail(selectedStationPagePassing));

        RefreshStationOperationViews();
    }

    private UIElement BuildStationBasicEditor(StationEditorViewModel station)
    {
        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = "車站編號", Margin = new Thickness(0, 5, 8, 5) });
        var id = new TextBox { Text = station.Id, IsReadOnly = true, Name = "StationIdTextBox" };
        AutomationProperties.SetName(id, "車站編號");
        Grid.SetColumn(id, 1); grid.Children.Add(id);
        var nameLabel = new TextBlock { Text = "站名", Margin = new Thickness(0, 5, 8, 5) };
        Grid.SetRow(nameLabel, 1);
        grid.Children.Add(nameLabel);
        stationPageNameTextBox = new TextBox { Text = stationPageRawFields[StationFieldKey(station, "Name")], Name = "StationNameTextBox" };
        AutomationProperties.SetName(stationPageNameTextBox, "車站名稱");
        stationPageNameTextBox.TextChanged += (_, _) =>
        {
            stationPageRawFields[StationFieldKey(station, "Name")] = stationPageNameTextBox.Text;
            if (!string.IsNullOrWhiteSpace(stationPageNameTextBox.Text)) station.Name = stationPageNameTextBox.Text.Trim();
        };
        Grid.SetRow(stationPageNameTextBox, 1); Grid.SetColumn(stationPageNameTextBox, 1); grid.Children.Add(stationPageNameTextBox);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dwellLabel = new TextBlock { Text = "預設停站秒數", Margin = new Thickness(0, 5, 8, 5) };
        Grid.SetRow(dwellLabel, 2);
        grid.Children.Add(dwellLabel);
        stationPageDwellTextBox = new TextBox { Text = stationPageRawFields[StationFieldKey(station, "Dwell")], Name = "StationDefaultDwellTextBox" };
        AutomationProperties.SetName(stationPageDwellTextBox, "本站預設停站秒數");
        stationPageDwellTextBox.TextChanged += (_, _) =>
        {
            stationPageRawFields[StationFieldKey(station, "Dwell")] = stationPageDwellTextBox.Text;
            if (TryParseStationPageFinite(stationPageDwellTextBox.Text, out var value)) station.DefaultDwellSeconds = value;
        };
        Grid.SetRow(stationPageDwellTextBox, 2); Grid.SetColumn(stationPageDwellTextBox, 1); grid.Children.Add(stationPageDwellTextBox);
        box.Child = grid;
        return box;
    }

    private UIElement BuildPlatformDetail(PlatformEditorViewModel? platform)
    {
        if (stationPagePlatformDetail is null) return new StackPanel();
        stationPagePlatformDetail.Children.Clear();
        if (platform is null)
        {
            stationPagePlatformDetail.Children.Add(new TextBlock { Text = "請先選取本站月台。", Foreground = new SolidColorBrush(Color.FromRgb(90, 101, 122)) });
            return stationPagePlatformDetail;
        }

        stationPageRawFields.TryAdd(PlatformFieldKey(platform, "Name"), platform.Name);
        stationPageRawFields.TryAdd(PlatformFieldKey(platform, "Start"), platform.StartOffsetMeters.ToString(CultureInfo.InvariantCulture));
        stationPageRawFields.TryAdd(PlatformFieldKey(platform, "Stop"), platform.StopOffsetMeters.ToString(CultureInfo.InvariantCulture));
        stationPageRawFields.TryAdd(PlatformFieldKey(platform, "End"), platform.EndOffsetMeters.ToString(CultureInfo.InvariantCulture));
        stationPageRawFields.TryAdd(PlatformFieldKey(platform, "Length"), platform.EffectiveLengthMeters.ToString(CultureInfo.InvariantCulture));
        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 238)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = $"月台：{platform.Name}（{platform.Id}）", FontWeight = FontWeights.SemiBold });
        var id = TextField(content, "月台編號", platform.Id, "StationPlatformIdTextBox", "月台編號", _ => { });
        id.IsReadOnly = true;
        var body = TextField(content, "PlatformBodyId", platform.PlatformBodyId, "StationPlatformBodyIdTextBox", "月台本體編號", _ => { });
        body.IsReadOnly = true;
        var station = TextField(content, "本站", platform.Station, "StationPlatformStationTextBox", "月台所屬車站", _ => { });
        station.IsReadOnly = true;
        var edge = TextField(content, "附著軌道區段", platform.TrackEdge, "StationPlatformTrackEdgeTextBox", "月台附著軌道區段", _ => { });
        edge.IsReadOnly = true;
        stationPagePlatformNameTextBox = TextField(content, "月台名稱", RawPlatform(platform, "Name", platform.Name), "StationPlatformNameTextBox", "月台名稱", value =>
        {
            stationPageRawFields[PlatformFieldKey(platform, "Name")] = value;
            if (!string.IsNullOrWhiteSpace(value)) platform.Name = value.Trim();
        });
        ComboField(content, "顯示側", "StationPlatformSideComboBox", "月台顯示側", Enum.GetValues<PlatformSide>().Select(item => (item.ToString(), UiDisplayText.Enum(item))), platform.DisplaySide.ToString(), value =>
        {
            if (Enum.TryParse<PlatformSide>(value, out var parsed)) platform.DisplaySide = parsed;
        });
        ComboField(content, "停車位置參照", "StationPlatformStopPositionComboBox", "月台停車位置參照", Enum.GetValues<StopPositionReference>().Select(item => (item.ToString(), UiDisplayText.Enum(item))), platform.StopPositionReference.ToString(), value =>
        {
            if (Enum.TryParse<StopPositionReference>(value, out var parsed)) platform.StopPositionReference = parsed;
        });
        stationPagePlatformStartTextBox = TextField(content, "月台起點偏移（m）", RawPlatform(platform, "Start", platform.StartOffsetMeters), "StationPlatformStartOffsetTextBox", "月台起點偏移", value =>
        {
            stationPageRawFields[PlatformFieldKey(platform, "Start")] = value;
            if (TryParseStationPageFinite(value, out var parsed)) platform.StartOffsetMeters = parsed;
        });
        stationPagePlatformStopTextBox = TextField(content, "停車位置偏移（m）", RawPlatform(platform, "Stop", platform.StopOffsetMeters), "StationPlatformStopOffsetTextBox", "月台停車位置偏移", value =>
        {
            stationPageRawFields[PlatformFieldKey(platform, "Stop")] = value;
            if (TryParseStationPageFinite(value, out var parsed)) platform.StopOffsetMeters = parsed;
        });
        stationPagePlatformEndTextBox = TextField(content, "月台終點偏移（m）", RawPlatform(platform, "End", platform.EndOffsetMeters), "StationPlatformEndOffsetTextBox", "月台終點偏移", value =>
        {
            stationPageRawFields[PlatformFieldKey(platform, "End")] = value;
            if (TryParseStationPageFinite(value, out var parsed)) platform.EndOffsetMeters = parsed;
        });
        stationPagePlatformLengthTextBox = TextField(content, "有效長度（m）", RawPlatform(platform, "Length", platform.EffectiveLengthMeters), "StationPlatformEffectiveLengthTextBox", "月台有效長度", value =>
        {
            stationPageRawFields[PlatformFieldKey(platform, "Length")] = value;
            if (TryParseStationPageFinite(value, out var parsed)) platform.EffectiveLengthMeters = parsed;
        });
        ComboField(content, "允許方向", "StationPlatformDirectionComboBox", "月台允許方向", Enum.GetValues<TrackDirection>().Select(item => (item.ToString(), UiDisplayText.Enum(item))), platform.Direction.ToString(), value =>
        {
            if (Enum.TryParse<TrackDirection>(value, out var parsed)) platform.Direction = parsed;
        });
        stationPagePlatformPassengerCheckBox = new CheckBox { Content = "提供乘客服務", IsChecked = platform.PassengerService, Name = "StationPlatformPassengerServiceCheckBox", Margin = new Thickness(0, 6, 0, 3) };
        AutomationProperties.SetName(stationPagePlatformPassengerCheckBox, "月台乘客服務");
        stationPagePlatformPassengerCheckBox.Checked += (_, _) => platform.PassengerService = true;
        stationPagePlatformPassengerCheckBox.Unchecked += (_, _) => platform.PassengerService = false;
        content.Children.Add(stationPagePlatformPassengerCheckBox);
        content.Children.Add(new TextBlock { Text = "允許車型", Margin = new Thickness(0, 8, 0, 3) });
        content.Children.Add(ReferenceMultiSelect("StationPlatformVehicleTypes", "月台允許車型", state.Draft.VehicleTypes.Select(item => (item.Id, $"{item.DisplayName} · {item.Id}")), platform.AllowedVehicleTypeIds, values => platform.AllowedVehicleTypeIds = string.Join(", ", values)));
        content.Children.Add(new TextBlock { Text = "允許服務", Margin = new Thickness(0, 8, 0, 3) });
        content.Children.Add(ReferenceMultiSelect("StationPlatformServiceTypes", "月台允許服務", state.Draft.ServiceTypes.Select(item => (item.Id, $"{item.DisplayName} · {item.Id}")), platform.AllowedServiceTypeIds, values => platform.AllowedServiceTypeIds = string.Join(", ", values)));
        box.Child = content;
        stationPagePlatformDetail.Children.Add(box);
        return stationPagePlatformDetail;
    }
    private UIElement BuildStationOperationChooser(StationEditorViewModel station)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var button = CreateButton("編輯選取的本站作業", (_, _) => BuildStationOperationDetail(station, selectedStationPageOperation), true);
        button.Name = "EditStationOperationButton";
        AutomationProperties.SetName(button, "編輯選取的本站作業");
        panel.Children.Add(button);
        panel.Children.Add(BuildStationOperationDetail(station, selectedStationPageOperation));
        return panel;
    }

    private StackPanel? stationPageStationOperationDetail;
    private StackPanel? stationPageTurnbackOperationDetail;
    private StackPanel? stationPagePassingOperationDetail;

    private UIElement BuildStationOperationDetail(StationEditorViewModel station, StationOperationEditorViewModel? operation)
    {
        stationPageStationOperationDetail ??= new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stationPageStationOperationDetail.Children.Clear();
        if (operation is null)
        {
            stationPageStationOperationDetail.Children.Add(new TextBlock { Text = "請先選取本站作業。", Foreground = new SolidColorBrush(Color.FromRgb(90, 101, 122)) });
            return stationPageStationOperationDetail;
        }
        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 238)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = $"本站作業：{operation.Id}", FontWeight = FontWeights.SemiBold });
        var id = TextField(content, "作業編號", operation.Id, "StationOperationIdTextBox", "本站作業編號", value => { if (!string.IsNullOrWhiteSpace(value)) operation.Id = value.Trim(); });
        stationPageOperationIdTextBox = id;
        id.IsReadOnly = true;
        var dwell = TextField(content, "預設停站秒數", Raw(operation.Id, "Dwell", operation.DefaultDwellSeconds), "StationOperationDwellTextBox", "本站作業預設停站秒數", value => { if (TryParseStationPageFinite(value, out var parsed)) operation.DefaultDwellSeconds = parsed; });
        stationPageOperationDwellTextBox = dwell;
        content.Children.Add(new TextBlock { Text = "抵達月台", Margin = new Thickness(0, 8, 0, 3) });
        content.Children.Add(ReferenceMultiSelect("StationOperationArrivalPlatforms", "抵達月台參照", infrastructure.Platforms.Where(p => IdEquals(p.Station, station.Id)).Select(p => (p.Id, $"{p.Id} · {p.Name}")), operation.ArrivalPlatforms, values => operation.ArrivalPlatforms = string.Join(", ", values)));
        content.Children.Add(new TextBlock { Text = "出發月台", Margin = new Thickness(0, 8, 0, 3) });
        content.Children.Add(ReferenceMultiSelect("StationOperationDeparturePlatforms", "出發月台參照", infrastructure.Platforms.Where(p => IdEquals(p.Station, station.Id)).Select(p => (p.Id, $"{p.Id} · {p.Name}")), operation.DeparturePlatforms, values => operation.DeparturePlatforms = string.Join(", ", values)));
        content.Children.Add(new TextBlock { Text = "折返作業參照", Margin = new Thickness(0, 8, 0, 3) });
        content.Children.Add(ReferenceMultiSelect("StationOperationTurnbacks", "折返作業參照", turnbackOperations.Select(p => (p.Id, p.Id)), operation.TurnbackOperations, values => operation.TurnbackOperations = string.Join(", ", values)));
        content.Children.Add(new TextBlock { Text = "待避作業參照", Margin = new Thickness(0, 8, 0, 3) });
        content.Children.Add(ReferenceMultiSelect("StationOperationPassings", "待避作業參照", passingOperations.Select(p => (p.Id, p.Id)), operation.PassingOperations, values => operation.PassingOperations = string.Join(", ", values)));
        box.Child = content;
        stationPageStationOperationDetail.Children.Add(box);
        return stationPageStationOperationDetail;
    }

    private UIElement BuildTurnbackOperationDetail(TurnbackOperationEditorViewModel? operation)
    {
        stationPageTurnbackOperationDetail ??= new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stationPageTurnbackOperationDetail.Children.Clear();
        if (operation is null) { stationPageTurnbackOperationDetail.Children.Add(new TextBlock { Text = "本站沒有被引用的折返作業，或尚未選取。" }); return stationPageTurnbackOperationDetail; }
        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 238)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = $"折返作業：{operation.Id}", FontWeight = FontWeights.SemiBold });
        stationPageTurnbackIdTextBox = TextField(content, "作業編號", operation.Id, "TurnbackOperationIdTextBox", "折返作業編號", value => { if (!string.IsNullOrWhiteSpace(value)) operation.Id = value.Trim(); });
        stationPageTurnbackIdTextBox.IsReadOnly = true;
        ComboField(content, "折返設施", "TurnbackOperationFacilityComboBox", "折返設施參照", state.Draft.Topology.TurnbackFacilities.Select(item => (item.FacilityId, $"{item.FacilityId} · {item.Name}")), operation.Facility, value => operation.Facility = value);
        ComboField(content, "抵達服務路徑", "TurnbackArrivalRouteComboBox", "折返抵達服務路徑參照", state.Draft.ServiceRoutes.Select(item => (item.ServiceRouteId, $"{item.ServiceRouteId} · {item.Name}")), operation.ArrivalRoute, value => operation.ArrivalRoute = value);
        ComboField(content, "出發服務路徑", "TurnbackDepartureRouteComboBox", "折返出發服務路徑參照", state.Draft.ServiceRoutes.Select(item => (item.ServiceRouteId, $"{item.ServiceRouteId} · {item.Name}")), operation.DepartureRoute, value => operation.DepartureRoute = value);
        stationPageTurnbackDwellTextBox = TextField(content, "最短折返停留秒數", Raw(operation.Id, "Dwell", operation.MinimumDwellSeconds), "TurnbackOperationDwellTextBox", "折返作業最短折返停留秒數", value => { if (TryParseStationPageFinite(value, out var parsed)) operation.MinimumDwellSeconds = parsed; });
        box.Child = content; stationPageTurnbackOperationDetail.Children.Add(box); return stationPageTurnbackOperationDetail;
    }

    private UIElement BuildPassingOperationDetail(PassingOperationEditorViewModel? operation)
    {
        stationPagePassingOperationDetail ??= new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stationPagePassingOperationDetail.Children.Clear();
        if (operation is null) { stationPagePassingOperationDetail.Children.Add(new TextBlock { Text = "本站沒有被引用的待避作業，或尚未選取。" }); return stationPagePassingOperationDetail; }
        var box = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(225, 230, 238)), BorderThickness = new Thickness(1), Padding = new Thickness(10) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = $"待避作業：{operation.Id}", FontWeight = FontWeights.SemiBold });
        stationPagePassingIdTextBox = TextField(content, "作業編號", operation.Id, "PassingOperationIdTextBox", "待避作業編號", value => { if (!string.IsNullOrWhiteSpace(value)) operation.Id = value.Trim(); });
        stationPagePassingIdTextBox.IsReadOnly = true;
        ComboField(content, "待避設施", "PassingOperationFacilityComboBox", "待避設施參照", state.Draft.Topology.PassingFacilities.Select(item => (item.FacilityId, $"{item.FacilityId} · {item.Name}")), operation.Facility, value => operation.Facility = value);
        ComboField(content, "服務路徑", "PassingServiceRouteComboBox", "待避服務路徑參照", state.Draft.ServiceRoutes.Select(item => (item.ServiceRouteId, $"{item.ServiceRouteId} · {item.Name}")), operation.ServiceRoute, value => operation.ServiceRoute = value);
        ComboField(content, "快速服務類型（可空白）", "PassingExpressServiceTypeComboBox", "待避快速服務類型參照", new[] { ("", "（所有可越行服務）") }.Concat(state.Draft.ServiceTypes.Select(item => (item.Id, $"{item.Id} · {item.DisplayName}"))), operation.ExpressServiceType, value => operation.ExpressServiceType = value);
        box.Child = content; stationPagePassingOperationDetail.Children.Add(box); return stationPagePassingOperationDetail;
    }

    private ListBox CreateOperationList(ICollectionView view, string name, string automationName, Action<object?> selected)
    {
        var list = new ListBox { Name = name, ItemsSource = view, DisplayMemberPath = "Id", Height = 100, BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 232)), BorderThickness = new Thickness(1) };
        AutomationProperties.SetName(list, automationName);
        list.SelectionChanged += (_, _) => selected(list.SelectedItem);
        return list;
    }

    private void RefreshStationOperationViews()
    {
        stationPageStationOperationsView?.Refresh(); stationPageTurnbacksView?.Refresh(); stationPagePassingsView?.Refresh();
    }

    private bool IsTurnbackReferencedByStation(TurnbackOperationEditorViewModel operation, string stationId) =>
        stationOperations.Any(item => IdEquals(item.Station, stationId) && SplitIds(item.TurnbackOperations).Contains(operation.Id, StringComparer.OrdinalIgnoreCase));

    private bool IsPassingReferencedByStation(PassingOperationEditorViewModel operation, string stationId) =>
        stationOperations.Any(item => IdEquals(item.Station, stationId) && SplitIds(item.PassingOperations).Contains(operation.Id, StringComparer.OrdinalIgnoreCase));

    private static string[] SplitIds(string value) => value.Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private TextBox TextField(StackPanel parent, string label, string initial, string name, string automationName, Action<string> changed)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        var input = new TextBox { Text = initial, Name = name };
        AutomationProperties.SetName(input, automationName);
        input.TextChanged += (_, _) => { if (!stationPageUpdating) changed(input.Text); };
        parent.Children.Add(input);
        return input;
    }

    private void ComboField(StackPanel parent, string label, string name, string automationName, IEnumerable<(string Id, string Label)> values, string current, Action<string> changed)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
        var options = values.ToArray();
        var combo = new ComboBox { Name = name, ItemsSource = options, DisplayMemberPath = nameof(ReferenceOption.Label), SelectedValuePath = nameof(ReferenceOption.Id) };
        AutomationProperties.SetName(combo, automationName);
        combo.ItemsSource = options.Select(item => new ReferenceOption(item.Id, item.Label)).ToArray();
        combo.SelectedValue = options.FirstOrDefault(item => IdEquals(item.Id, current)).Id;
        combo.SelectionChanged += (_, _) => { if (combo.SelectedValue is string value) changed(value); };
        parent.Children.Add(combo);
    }

    private ListBox ReferenceMultiSelect(string name, string automationName, IEnumerable<(string Id, string Label)> values, string current, Action<string[]> changed)
    {
        var options = values.Select(item => new ReferenceOption(item.Id, item.Label)).ToList();
        var selected = SplitIds(current);
        foreach (var id in selected.Where(id => options.All(item => !IdEquals(item.Id, id)))) options.Add(new ReferenceOption(id, $"缺少參照：{id}"));
        var list = new ListBox { Name = name, ItemsSource = options, DisplayMemberPath = nameof(ReferenceOption.Label), SelectionMode = SelectionMode.Multiple, Height = Math.Min(130, Math.Max(42, options.Count * 24)) };
        AutomationProperties.SetName(list, automationName);
        stationPageUpdating = true;
        foreach (var item in options.Where(item => selected.Contains(item.Id, StringComparer.OrdinalIgnoreCase))) list.SelectedItems.Add(item);
        stationPageUpdating = false;
        list.SelectionChanged += (_, _) => { if (!stationPageUpdating) changed(list.SelectedItems.OfType<ReferenceOption>().Select(item => item.Id).ToArray()); };
        return list;
    }

    private string Raw(string id, string field, double value)
    {
        var key = OperationFieldKey(id, field);
        return stationPageRawFields.TryGetValue(key, out var raw) ? raw : value.ToString(CultureInfo.InvariantCulture);
    }

    private string RawPlatform(PlatformEditorViewModel platform, string field, string value) =>
        stationPageRawFields.GetValueOrDefault(PlatformFieldKey(platform, field), value);

    private string RawPlatform(PlatformEditorViewModel platform, string field, double value) =>
        stationPageRawFields.GetValueOrDefault(PlatformFieldKey(platform, field), value.ToString(CultureInfo.InvariantCulture));

    private void RunStationPageAction(Action action)
    {
        try
        {
            CommitStationPageDrafts();
            CommitTableDrafts();
            action();
            CommitTableDrafts();
            ShowStationsPage();
        }
        catch (EditorValidationException exception) { SetValidationMessages(exception.Messages); }
        catch (SimulationValidationException exception) { ShowErrors(exception.Errors); }
    }

    private void AddStationFromStationPage() => RunStationPageAction(AddStation);

    private void DeleteStationFromStationPage()
    {
        var id = selectedStationPageStation?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        RunStationPageAction(() => RunEdit(() => TopologyEditingService.DeleteStation(state.Draft, id)));
    }

    private void AddPlatformFromStationPage()
    {
        var station = selectedStationPageStation;
        var edge = infrastructure.Edges.FirstOrDefault();
        if (station is null || edge is null) { ShowErrors(["請先建立車站與軌道區段，才能新增本站月台。"]); return; }
        var answer = Ask("新增本站月台", ("名稱", $"{station.Name} 月台"), ("軌道區段", edge.Id),
            ("起始偏移（m）", "0"), ("停車偏移（m）", Math.Min(50, edge.LengthMeters).ToString(CultureInfo.InvariantCulture)),
            ("終止偏移（m）", Math.Min(100, edge.LengthMeters).ToString(CultureInfo.InvariantCulture)), ("有效長度（m）", "100"),
            ("方向（下行／上行／雙向）", "雙向"));
        if (answer is null) return;
        if (!UiDisplayText.TryParseEnum(typeof(TrackDirection), answer["方向（下行／上行／雙向）"], out var parsedDirection) || parsedDirection is not TrackDirection direction)
        {
            ShowErrors(["方向必須是下行、上行或雙向。"]); return;
        }
        RunStationPageAction(() =>
        {
            var id = NextId("PLATFORM");
            RunEdit(() =>
            {
                var attachedEdge = state.Draft.Topology.Edges.SingleOrDefault(item => IdEquals(item.TrackEdgeId, answer["軌道區段"]))
                    ?? throw new SimulationValidationException([$"找不到要附著的軌道區段「{answer["軌道區段"]}」。"]);
                var platform = new PlatformDefinitionV4
                {
                    PlatformId = id, Name = answer["名稱"].Trim(), StationId = station.Id, TrackEdgeId = answer["軌道區段"].Trim(),
                    PlatformStartOffsetMeters = Parse(answer["起始偏移（m）"], "月台起點"), StopPositionOffsetMeters = Parse(answer["停車偏移（m）"], "停止位置"),
                    PlatformEndOffsetMeters = Parse(answer["終止偏移（m）"], "月台終點"), EffectiveLengthMeters = Parse(answer["有效長度（m）"], "有效長度"),
                    AllowedDirection = direction
                };
                ValidatePlatformOffsets(platform, attachedEdge);
                return state.Draft with
                {
                    Topology = state.Draft.Topology with
                    {
                        Platforms = state.Draft.Topology.Platforms.Append(platform).ToArray(),
                        Stations = state.Draft.Topology.Stations.Select(item => IdEquals(item.StationId, station.Id)
                            ? item with { PlatformIds = item.PlatformIds.Append(id).ToArray() } : item).ToArray()
                    }
                };
            });
        });
    }

    private void DeletePlatformFromStationPage()
    {
        var id = selectedStationPagePlatform?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        RunStationPageAction(() => RunEdit(() => TopologyEditingService.DeletePlatform(state.Draft, id)));
    }

    private void AddStationOperationFromStationPage()
    {
        var station = selectedStationPageStation;
        if (station is null) return;
        RunStationPageAction(() =>
        {
            var id = NextDocumentId("STATION-OP", stationOperations.Select(item => item.Id));
            stationOperations.Add(new StationOperationEditorViewModel(new StationOperationDefinition
            {
                StationOperationId = id, StationId = station.Id,
                ArrivalPlatformIds = SplitIds(station.Platforms), DeparturePlatformIds = SplitIds(station.Platforms),
                DefaultDwellTimeSeconds = station.DefaultDwellSeconds
            }));
        });
    }

    private void DeleteStationOperationFromStationPage()
    {
        if (selectedStationPageOperation is null) return;
        var id = selectedStationPageOperation.Id;
        RunStationPageAction(() => stationOperations.Remove(selectedStationPageOperation));
        stationPageRawFields.Remove(OperationFieldKey(id, "Id"));
        stationPageRawFields.Remove(OperationFieldKey(id, "Dwell"));
    }

    private void AddTurnbackOperationFromStationPage()
    {
        var station = selectedStationPageStation;
        var facility = state.Draft.Topology.TurnbackFacilities.FirstOrDefault();
        var arrival = state.Draft.DirectionRouteBindings.FirstOrDefault();
        var departure = state.Draft.DirectionRouteBindings.FirstOrDefault(item => item.Direction != arrival?.Direction);
        if (station is null || facility is null || arrival is null || departure is null)
        {
            ShowErrors(["請先建立折返設施與上下行服務路徑，才能新增本站折返作業。"]); return;
        }
        RunStationPageAction(() =>
        {
            var id = NextDocumentId("TURNBACK", turnbackOperations.Select(item => item.Id));
            turnbackOperations.Add(new TurnbackOperationEditorViewModel(new TurnbackOperationDefinition
            {
                OperationId = id, FacilityId = facility.FacilityId, ArrivalServiceRouteId = arrival.ServiceRouteId,
                DepartureServiceRouteId = departure.ServiceRouteId, MinimumDwellTimeSeconds = station.DefaultDwellSeconds
            }));
            var stationOperation = GetOrCreateStationOperation(station);
            stationOperation.TurnbackOperations = string.Join(", ", SplitIds(stationOperation.TurnbackOperations).Append(id));
        });
    }

    private void DeleteTurnbackOperationFromStationPage()
    {
        if (selectedStationPageTurnback is null) return;
        var id = selectedStationPageTurnback.Id;
        RunStationPageAction(() =>
        {
            turnbackOperations.Remove(selectedStationPageTurnback);
            foreach (var operation in stationOperations) operation.TurnbackOperations = string.Join(", ", SplitIds(operation.TurnbackOperations).Where(item => !IdEquals(item, id)));
        });
    }

    private void AddPassingOperationFromStationPage()
    {
        var station = selectedStationPageStation;
        var facility = state.Draft.Topology.PassingFacilities.FirstOrDefault(item => IdEquals(item.StationId, station?.Id));
        var route = state.Draft.ServiceRoutes.FirstOrDefault();
        if (station is null || facility is null || route is null)
        {
            ShowErrors(["請先建立本站待避設施與服務路徑，才能新增本站待避作業。"]); return;
        }
        RunStationPageAction(() =>
        {
            var id = NextDocumentId("PASSING", passingOperations.Select(item => item.Id));
            passingOperations.Add(new PassingOperationEditorViewModel(new PassingOperationDefinition
            {
                OperationId = id, FacilityId = facility.FacilityId, ServiceRouteId = route.ServiceRouteId
            }));
            var stationOperation = GetOrCreateStationOperation(station);
            stationOperation.PassingOperations = string.Join(", ", SplitIds(stationOperation.PassingOperations).Append(id));
        });
    }

    private void DeletePassingOperationFromStationPage()
    {
        if (selectedStationPagePassing is null) return;
        var id = selectedStationPagePassing.Id;
        RunStationPageAction(() =>
        {
            passingOperations.Remove(selectedStationPagePassing);
            foreach (var operation in stationOperations) operation.PassingOperations = string.Join(", ", SplitIds(operation.PassingOperations).Where(item => !IdEquals(item, id)));
        });
    }

    private StationOperationEditorViewModel GetOrCreateStationOperation(StationEditorViewModel station)
    {
        var existing = stationOperations.FirstOrDefault(item => IdEquals(item.Station, station.Id));
        if (existing is not null) return existing;
        var created = new StationOperationEditorViewModel(new StationOperationDefinition
        {
            StationOperationId = NextDocumentId("STATION-OP", stationOperations.Select(item => item.Id)), StationId = station.Id,
            ArrivalPlatformIds = SplitIds(station.Platforms), DeparturePlatformIds = SplitIds(station.Platforms), DefaultDwellTimeSeconds = station.DefaultDwellSeconds
        });
        stationOperations.Add(created);
        return created;
    }

    private void CommitPlatformDetail()
    {
        if (selectedStationPagePlatform is not { } platform) return;
        if (stationPagePlatformNameTextBox is not null) stationPageRawFields[PlatformFieldKey(platform, "Name")] = stationPagePlatformNameTextBox.Text;
        if (stationPagePlatformStartTextBox is not null) stationPageRawFields[PlatformFieldKey(platform, "Start")] = stationPagePlatformStartTextBox.Text;
        if (stationPagePlatformStopTextBox is not null) stationPageRawFields[PlatformFieldKey(platform, "Stop")] = stationPagePlatformStopTextBox.Text;
        if (stationPagePlatformEndTextBox is not null) stationPageRawFields[PlatformFieldKey(platform, "End")] = stationPagePlatformEndTextBox.Text;
        if (stationPagePlatformLengthTextBox is not null) stationPageRawFields[PlatformFieldKey(platform, "Length")] = stationPagePlatformLengthTextBox.Text;

        var messages = new List<ProjectValidationMessage>();
        var name = stationPageRawFields.GetValueOrDefault(PlatformFieldKey(platform, "Name"), platform.Name);
        if (string.IsNullOrWhiteSpace(name)) messages.Add(PageError("月台名稱不可空白。", platform.Id, "Name"));
        else platform.Name = name.Trim();
        CommitPlatformNumber(platform, "Start", "月台起點偏移", value => platform.StartOffsetMeters = value, messages);
        CommitPlatformNumber(platform, "Stop", "月台停車位置偏移", value => platform.StopOffsetMeters = value, messages);
        CommitPlatformNumber(platform, "End", "月台終點偏移", value => platform.EndOffsetMeters = value, messages);
        CommitPlatformNumber(platform, "Length", "月台有效長度", value => platform.EffectiveLengthMeters = value, messages);
        if (messages.Count > 0) throw new EditorValidationException(messages);
    }

    private void CommitPlatformNumber(PlatformEditorViewModel platform, string field, string label, Action<double> setter, List<ProjectValidationMessage> messages)
    {
        var text = stationPageRawFields.GetValueOrDefault(PlatformFieldKey(platform, field), "");
        if (TryParseStationPageFinite(text, out var value)) setter(value);
        else messages.Add(PageError($"{label}必須是有限數值。", platform.Id, field));
    }
    private void CommitStationPageDrafts()
    {
        if (selectedStationPageStation is null) return;
        CommitPlatformDetail();
        CommitVisibleGridEdits();
        var station = selectedStationPageStation;
        if (stationPageNameTextBox is not null) stationPageRawFields[StationFieldKey(station, "Name")] = stationPageNameTextBox.Text;
        if (stationPageDwellTextBox is not null) stationPageRawFields[StationFieldKey(station, "Dwell")] = stationPageDwellTextBox.Text;
        var messages = new List<ProjectValidationMessage>();
        var name = stationPageRawFields.GetValueOrDefault(StationFieldKey(station, "Name"), station.Name);
        if (string.IsNullOrWhiteSpace(name)) messages.Add(PageError("站名不可空白。", station.Id, "Name"));
        else station.Name = name.Trim();
        var dwell = stationPageRawFields.GetValueOrDefault(StationFieldKey(station, "Dwell"), station.DefaultDwellSeconds.ToString(CultureInfo.InvariantCulture));
        if (!TryParseStationPageFinite(dwell, out var dwellValue)) messages.Add(PageError("預設停站秒數必須是有限數值。", station.Id, "DefaultDwellSeconds"));
        else station.DefaultDwellSeconds = dwellValue;

        if (selectedStationPageOperation is not null)
        {
            if (stationPageOperationIdTextBox is not null) stationPageRawFields[OperationFieldKey(selectedStationPageOperation.Id, "Id")] = stationPageOperationIdTextBox.Text;
            if (stationPageOperationDwellTextBox is not null) stationPageRawFields[OperationFieldKey(selectedStationPageOperation.Id, "Dwell")] = stationPageOperationDwellTextBox.Text;
            var operationId = stationPageRawFields.GetValueOrDefault(OperationFieldKey(selectedStationPageOperation.Id, "Id"), selectedStationPageOperation.Id);
            if (string.IsNullOrWhiteSpace(operationId)) messages.Add(PageError("本站作業編號不可空白。", selectedStationPageOperation.Id, "Id")); else selectedStationPageOperation.Id = operationId.Trim();
            var operationDwell = stationPageRawFields.GetValueOrDefault(OperationFieldKey(selectedStationPageOperation.Id, "Dwell"), selectedStationPageOperation.DefaultDwellSeconds.ToString(CultureInfo.InvariantCulture));
            if (!TryParseStationPageFinite(operationDwell, out var operationDwellValue)) messages.Add(PageError("本站作業停站秒數必須是有限數值。", selectedStationPageOperation.Id, "DefaultDwellSeconds")); else selectedStationPageOperation.DefaultDwellSeconds = operationDwellValue;
        }
        if (selectedStationPageTurnback is not null)
        {
            if (stationPageTurnbackIdTextBox is not null) stationPageRawFields[OperationFieldKey(selectedStationPageTurnback.Id, "Id")] = stationPageTurnbackIdTextBox.Text;
            if (stationPageTurnbackDwellTextBox is not null) stationPageRawFields[OperationFieldKey(selectedStationPageTurnback.Id, "Dwell")] = stationPageTurnbackDwellTextBox.Text;
            var id = stationPageRawFields.GetValueOrDefault(OperationFieldKey(selectedStationPageTurnback.Id, "Id"), selectedStationPageTurnback.Id);
            if (string.IsNullOrWhiteSpace(id)) messages.Add(PageError("折返作業編號不可空白。", selectedStationPageTurnback.Id, "Id")); else selectedStationPageTurnback.Id = id.Trim();
            var turnbackDwellText = stationPageRawFields.GetValueOrDefault(OperationFieldKey(selectedStationPageTurnback.Id, "Dwell"), selectedStationPageTurnback.MinimumDwellSeconds.ToString(CultureInfo.InvariantCulture));
            if (!TryParseStationPageFinite(turnbackDwellText, out var value)) messages.Add(PageError("折返作業最短折返停留秒數必須是有限數值。", selectedStationPageTurnback.Id, "MinimumDwellSeconds")); else selectedStationPageTurnback.MinimumDwellSeconds = value;
        }
        if (selectedStationPagePassing is not null && stationPagePassingIdTextBox is not null)
        {
            stationPageRawFields[OperationFieldKey(selectedStationPagePassing.Id, "Id")] = stationPagePassingIdTextBox.Text;
            if (string.IsNullOrWhiteSpace(stationPagePassingIdTextBox.Text)) messages.Add(PageError("待避作業編號不可空白。", selectedStationPagePassing.Id, "Id")); else selectedStationPagePassing.Id = stationPagePassingIdTextBox.Text.Trim();
        }
        if (messages.Count > 0) throw new EditorValidationException(messages);
        RefreshStationOperationViews();
    }

    private static bool TryParseStationPageFinite(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && double.IsFinite(value);

    private static ProjectValidationMessage PageError(string message, string targetId, string field) =>
        new(ProjectValidationSeverity.Error, message, targetId, ProjectValidationTargetKind.Station, field);

    private static string StationFieldKey(StationEditorViewModel station, string field) => $"STATION:{station.Id}:{field}";
    private static string PlatformFieldKey(PlatformEditorViewModel platform, string field) => $"PLATFORM:{platform.Id}:{field}";
    private static string OperationFieldKey(string id, string field) => $"OPERATION:{id}:{field}";

    private sealed record ReferenceOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
