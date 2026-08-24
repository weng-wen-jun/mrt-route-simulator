using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MrtRouteSimulator.Engine;
using EngineRoute = MrtRouteSimulator.Engine.Route;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private string _dispatchPlanningMode = "簡易班距";
    private string _vehicleAssignmentMode = "自動配置";

    public ObservableCollection<VehicleTypeInputRow> VehicleTypeRows { get; } = [];
    public ObservableCollection<ServiceTypeInputRow> ServiceTypeRows { get; } = [];
    public ObservableCollection<HeadwayPlanInputRow> HeadwayPlanRows { get; } = [];
    public ObservableCollection<ManualTimetableInputRow> ManualTimetableRows { get; } = [];
    public ObservableCollection<PlatformInputRow> PlatformRows { get; } = [];
    public ObservableCollection<TrackInputRow> TrackRows { get; } = [];
    public ObservableCollection<RoutePathInputRow> RoutePathRows { get; } = [];
    public ObservableCollection<TurnbackInputRow> TurnbackRows { get; } = [];
    public ObservableCollection<SpatialReferencePointInputRow> SpatialReferencePointRows { get; } = [];
    public ObservableCollection<IntervalStatisticRow> IntervalStatisticRows { get; } = [];

    public IReadOnlyList<string> StopModeOptions { get; } = ["停站", "跨站"];
    public IReadOnlyList<string> TrainDirectionOptions { get; } = ["下行", "上行"];
    public IReadOnlyList<string> InfrastructureDirectionOptions { get; } = ["雙向", "下行", "上行"];
    public IReadOnlyList<string> TrackKindOptions { get; } = ["正線", "月台線", "待避線", "側線", "橫渡線", "折返線", "尾軌", "進站線", "其他"];
    public IReadOnlyList<string> TurnbackKindOptions { get; } = ["抽象折返", "站前折返", "站後折返"];

    public IReadOnlyList<string> ServiceTypeIds =>
        ServiceTypeRows.Select(row => row.Id).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

    public IReadOnlyList<string> StopPatternIds =>
        ["ALL_STOP", .. ServicePatternRows.Select(row => row.PatternId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<CatalogOption> VehicleTypeOptions => VehicleTypeRows
        .Where(row => !string.IsNullOrWhiteSpace(row.Id))
        .Select(row => new CatalogOption(row.Id, row.Name)).ToArray();

    public IReadOnlyList<CatalogOption> ServiceTypeOptions => ServiceTypeRows
        .Where(row => !string.IsNullOrWhiteSpace(row.Id))
        .Select(row => new CatalogOption(row.Id, row.Name)).ToArray();

    public IReadOnlyList<CatalogOption> StopPatternOptions =>
        [new CatalogOption("ALL_STOP", "所有車站停靠"), .. ServicePatternRows
            .Where(row => !string.IsNullOrWhiteSpace(row.PatternId))
            .GroupBy(row => row.PatternId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CatalogOption(group.Key, group.First().PatternName))];

    private void LoadSampleInputCatalogs()
    {
        VehicleTypeRows.Clear();
        VehicleTypeRows.Add(new VehicleTypeInputRow());
        ServiceTypeRows.Clear();
        ServiceTypeRows.Add(new ServiceTypeInputRow());
        HeadwayPlanRows.Clear();
        HeadwayPlanRows.Add(new HeadwayPlanInputRow { Direction = "下行", FirstDeparture = "06:00:00", RunCount = 3 });
        HeadwayPlanRows.Add(new HeadwayPlanInputRow { Direction = "上行", FirstDeparture = "06:03:00", RunCount = 3 });
        ManualTimetableRows.Clear();
        _dispatchPlanningMode = "簡易班距";
        _vehicleAssignmentMode = "自動配置";
        PlatformRows.Clear();
        TrackRows.Clear();
        RoutePathRows.Clear();
        TurnbackRows.Clear();
        SpatialReferencePointRows.Clear();
    }

    private void FocusRouteInput_Click(object sender, RoutedEventArgs e)
    {
        StationDataGrid.BringIntoView();
        StationDataGrid.Focus();
    }

    private void FocusStopPatterns_Click(object sender, RoutedEventArgs e)
    {
        EditStopPatterns();
    }

    private void EditVehicleTypes_Click(object sender, RoutedEventArgs e)
    {
        var columns = new DataGridColumn[]
        {
            TextColumn("名稱", nameof(VehicleTypeInputRow.Name), 120),
            TextColumn("車長 m", nameof(VehicleTypeInputRow.LengthMeters), 80),
            TextColumn("最高 km/h", nameof(VehicleTypeInputRow.MaxSpeedKmh), 90),
            TextColumn("加速度", nameof(VehicleTypeInputRow.Acceleration), 75),
            TextColumn("營運煞車", nameof(VehicleTypeInputRow.ServiceBrake), 85),
            TextColumn("緊急煞車", nameof(VehicleTypeInputRow.EmergencyBrake), 85),
            TextColumn("Jerk", nameof(VehicleTypeInputRow.Jerk), 70),
            TextColumn("牽引衰減", nameof(VehicleTypeInputRow.TractionDecay), 85),
            TextColumn("惰行減速度", nameof(VehicleTypeInputRow.CoastingDeceleration), 95)
        };
        ShowTransactionalEditor("車型目錄", VehicleTypeRows, Clone,
            rows => new VehicleTypeInputRow { Id = CreateGeneratedId("VEHICLE"), Name = $"新車型 {rows.Count + 1}" },
            columns, 980, ValidateVehicleTypeCatalog);
    }

    private void EditServiceTypes_Click(object sender, RoutedEventArgs e)
    {
        var columns = new DataGridColumn[]
        {
            TextColumn("名稱", nameof(ServiceTypeInputRow.Name), 100),
            TextColumn("顏色", nameof(ServiceTypeInputRow.ColorHex), 85),
            TextColumn("車次前綴", nameof(ServiceTypeInputRow.RunPrefix), 80),
            OptionComboColumn("預設停站模式", nameof(ServiceTypeInputRow.DefaultStopPatternId), StopPatternOptions, 150),
            OptionComboColumn("預設車型", nameof(ServiceTypeInputRow.DefaultVehicleTypeId), VehicleTypeOptions, 160),
            TextColumn("優先序", nameof(ServiceTypeInputRow.Priority), 70),
            CheckColumn("可要求待避", nameof(ServiceTypeInputRow.CanRequestOvertake), 90),
            TextColumn("偏好月台 ID（逗號）", nameof(ServiceTypeInputRow.PreferredPlatformIds), 180)
        };
        ShowTransactionalEditor("服務類型目錄", ServiceTypeRows, Clone,
            rows => new ServiceTypeInputRow
            {
                Id = CreateGeneratedId("SERVICE"),
                Name = $"新服務 {rows.Count + 1}",
                DefaultVehicleTypeId = VehicleTypeRows.FirstOrDefault()?.Id ?? string.Empty
            },
            columns, 1020, ValidateServiceTypeCatalog);
    }

    private void EditStopPatterns()
    {
        var draft = new ObservableCollection<ServicePatternInputRow>(ServicePatternRows.Select(Clone));
        var grid = CreateGrid(draft,
            TextColumn("模式名稱", nameof(ServicePatternInputRow.PatternName), 150),
            ComboColumn("車站", nameof(ServicePatternInputRow.StationId), StationRows.Select(row => row.StationId), 100),
            ComboColumn("動作", nameof(ServicePatternInputRow.Mode), StopModeOptions, 80),
            TextColumn("停站秒數", nameof(ServicePatternInputRow.DwellTimeSeconds), 90),
            TextColumn("通過速限 km/h", nameof(ServicePatternInputRow.SpeedLimitKmh), 120));
        grid.CellEditEnding += (_, args) =>
        {
            if (!Equals(args.Column.Header, "模式名稱")
                || args.Row.Item is not ServicePatternInputRow selected
                || args.EditingElement is not TextBox editor)
            {
                return;
            }
            foreach (var row in draft.Where(item => item.PatternId.Equals(selected.PatternId, StringComparison.OrdinalIgnoreCase)))
                row.PatternName = editor.Text;
            grid.Dispatcher.BeginInvoke(grid.Items.Refresh);
        };
        var window = CreateEditorWindow("停站模式管理【V3.3】", 780, 600);
        window.Owner = this;
        var root = (DockPanel)window.Content;
        var panel = new DockPanel { Margin = new Thickness(10) };
        var note = new TextBlock
        {
            Text = "「所有車站停靠」由系統依路線自動維護。新增模式後，可用「新增站點設定」補上同一模式的其他車站。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(note, Dock.Top);
        panel.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var addPattern = new Button { Content = "新增模式", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0) };
        var addInstruction = new Button { Content = "新增站點設定", MinWidth = 104, Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "刪除選取", MinWidth = 88 };
        addPattern.Click += (_, _) =>
        {
            var row = new ServicePatternInputRow
            {
                PatternId = CreateGeneratedId("PATTERN"),
                PatternName = $"新停站模式 {draft.Select(item => item.PatternId).Distinct(StringComparer.OrdinalIgnoreCase).Count() + 1}",
                StationId = StationRows.FirstOrDefault()?.StationId ?? string.Empty,
                Mode = "停站"
            };
            draft.Add(row);
            grid.SelectedItem = row;
            grid.ScrollIntoView(row);
        };
        addInstruction.Click += (_, _) =>
        {
            if (grid.SelectedItem is not ServicePatternInputRow selected)
            {
                ShowValidation(["請先選取要增加站點設定的停站模式。"]);
                return;
            }
            var usedStations = draft.Where(item => item.PatternId.Equals(selected.PatternId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.StationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stationId = StationRows.Select(item => item.StationId).FirstOrDefault(item => !usedStations.Contains(item))
                ?? StationRows.FirstOrDefault()?.StationId ?? string.Empty;
            var row = new ServicePatternInputRow
            {
                PatternId = selected.PatternId,
                PatternName = selected.PatternName,
                StationId = stationId,
                Mode = "停站"
            };
            draft.Add(row);
            grid.SelectedItem = row;
            grid.ScrollIntoView(row);
        };
        remove.Click += (_, _) =>
        {
            if (grid.SelectedItem is ServicePatternInputRow selected) draft.Remove(selected);
        };
        buttons.Children.Add(addPattern);
        buttons.Children.Add(addInstruction);
        buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Top);
        panel.Children.Add(buttons);
        panel.Children.Add(grid);
        root.Children.Add(panel);
        AddOkCancel(window, () =>
        {
            try
            {
                CommitGrid(grid);
                var patterns = BuildStopPatternDefinitions(draft);
                ValidateStopPatternReferences(patterns);
                Replace(ServicePatternRows, draft.Select(Clone));
                MarkInputCommitted("停站模式已更新；請重新建立模擬。", window);
            }
            catch (Exception exception) when (exception is InvalidOperationException or SimulationValidationException)
            {
                ShowValidation(exception is SimulationValidationException validation ? validation.Errors : [exception.Message]);
            }
        });
        window.ShowDialog();
    }

    private void EditDispatchPlan_Click(object sender, RoutedEventArgs e)
    {
        var headways = new ObservableCollection<HeadwayPlanInputRow>(HeadwayPlanRows.Select(Clone));
        var manual = new ObservableCollection<ManualTimetableInputRow>(ManualTimetableRows.Select(Clone));
        var mode = new ComboBox { ItemsSource = new[] { "簡易班距", "手動班表" }, SelectedItem = _dispatchPlanningMode, Width = 130 };
        var assignment = new ComboBox { ItemsSource = new[] { "自動配置", "全部指定" }, SelectedItem = _vehicleAssignmentMode, Width = 130 };
        var tabs = new TabControl();
        var headwayGrid = CreateGrid(headways,
            ComboColumn("方向", nameof(HeadwayPlanInputRow.Direction), TrainDirectionOptions, 70),
            TextColumn("首班", nameof(HeadwayPlanInputRow.FirstDeparture), 95),
            TextColumn("班距 min", nameof(HeadwayPlanInputRow.HeadwayMinutes), 80),
            TextColumn("班次數", nameof(HeadwayPlanInputRow.RunCount), 70),
            OptionComboColumn("服務等級", nameof(HeadwayPlanInputRow.ServiceTypeId), ServiceTypeOptions, 140),
            OptionComboColumn("車型", nameof(HeadwayPlanInputRow.VehicleTypeId), VehicleTypeOptions, 150),
            OptionComboColumn("停站模式", nameof(HeadwayPlanInputRow.StopPatternId), StopPatternOptions, 145),
            TextColumn("起點月台", nameof(HeadwayPlanInputRow.OriginPlatformId), 110),
            TextColumn("指定車輛", nameof(HeadwayPlanInputRow.VehicleId), 100),
            CheckColumn("端點折返續行", nameof(HeadwayPlanInputRow.ContinueAfterTerminal), 105));
        var manualGrid = CreateGrid(manual,
            TextColumn("發車", nameof(ManualTimetableInputRow.PlannedDeparture), 95),
            ComboColumn("方向", nameof(ManualTimetableInputRow.Direction), TrainDirectionOptions, 70),
            OptionComboColumn("服務等級", nameof(ManualTimetableInputRow.ServiceTypeId), ServiceTypeOptions, 140),
            OptionComboColumn("車型", nameof(ManualTimetableInputRow.VehicleTypeId), VehicleTypeOptions, 150),
            OptionComboColumn("停站模式", nameof(ManualTimetableInputRow.StopPatternId), StopPatternOptions, 145),
            TextColumn("起點月台", nameof(ManualTimetableInputRow.OriginPlatformId), 105),
            TextColumn("車輛", nameof(ManualTimetableInputRow.VehicleId), 95),
            TextColumn("車次 ID", nameof(ManualTimetableInputRow.ServiceRunId), 110),
            CheckColumn("端點折返續行", nameof(ManualTimetableInputRow.ContinueAfterTerminal), 105),
            TextColumn("折返後接續車次 ID", nameof(ManualTimetableInputRow.ContinuationServiceRunId), 145));
        tabs.Items.Add(CreateEditableTab("簡易班距", headwayGrid, headways, () => new HeadwayPlanInputRow()));
        tabs.Items.Add(CreateEditableTab("手動班表", manualGrid, manual, () => new ManualTimetableInputRow()));
        tabs.SelectedIndex = _dispatchPlanningMode == "手動班表" ? 1 : 0;
        mode.SelectionChanged += (_, _) =>
            tabs.SelectedIndex = mode.SelectedItem?.ToString() == "手動班表" ? 1 : 0;

        var window = CreateEditorWindow("發車計畫【V3.3】", 1050, 620);
        var root = (DockPanel)window.Content;
        var settings = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        settings.Children.Add(new TextBlock { Text = "使用模式", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        settings.Children.Add(mode);
        settings.Children.Add(new TextBlock { Text = "車輛配置", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 6, 0) });
        settings.Children.Add(assignment);
        DockPanel.SetDock(settings, Dock.Top);
        root.Children.Add(settings);
        root.Children.Add(tabs);
        AddOkCancel(window, () =>
        {
            CommitGrid(headwayGrid);
            CommitGrid(manualGrid);
            Replace(HeadwayPlanRows, headways.Select(Clone));
            Replace(ManualTimetableRows, manual.Select(Clone));
            _dispatchPlanningMode = mode.SelectedItem?.ToString() ?? "簡易班距";
            _vehicleAssignmentMode = assignment.SelectedItem?.ToString() ?? "自動配置";
            MarkInputCommitted("發車計畫已更新；請重新建立模擬。", window);
        });
        window.ShowDialog();
    }

    private void EditInfrastructure_Click(object sender, RoutedEventArgs e)
    {
        EnsureInfrastructureDraft();
        var platforms = new ObservableCollection<PlatformInputRow>(PlatformRows.Select(Clone));
        var tracks = new ObservableCollection<TrackInputRow>(TrackRows.Select(Clone));
        var paths = new ObservableCollection<RoutePathInputRow>(RoutePathRows.Select(Clone));
        var turnbacks = new ObservableCollection<TurnbackInputRow>(TurnbackRows.Select(Clone));
        var tabs = new TabControl();
        var platformGrid = CreateGrid(platforms,
            TextColumn("月台 ID", nameof(PlatformInputRow.PlatformId), 115),
            TextColumn("車站", nameof(PlatformInputRow.StationId), 75),
            TextColumn("名稱", nameof(PlatformInputRow.Name), 140),
            ComboColumn("方向", nameof(PlatformInputRow.Direction), InfrastructureDirectionOptions, 70),
            TextColumn("有效長 m", nameof(PlatformInputRow.EffectiveLengthMeters), 85),
            CheckColumn("載客服務", nameof(PlatformInputRow.PassengerService), 80),
            TextColumn("允許車型", nameof(PlatformInputRow.AllowedVehicleTypeIds), 125),
            TextColumn("允許服務", nameof(PlatformInputRow.AllowedServiceTypeIds), 125),
            TextColumn("股道 ID", nameof(PlatformInputRow.TrackSegmentIds), 120));
        var trackGrid = CreateGrid(tracks,
            TextColumn("股道 ID", nameof(TrackInputRow.TrackId), 100),
            TextColumn("起站", nameof(TrackInputRow.FromStationId), 70),
            TextColumn("迄站", nameof(TrackInputRow.ToStationId), 70),
            TextColumn("起 km", nameof(TrackInputRow.StartKm), 70),
            TextColumn("迄 km", nameof(TrackInputRow.EndKm), 70),
            ComboColumn("方向", nameof(TrackInputRow.Direction), InfrastructureDirectionOptions, 70),
            ComboColumn("種類", nameof(TrackInputRow.Kind), TrackKindOptions, 85),
            TextColumn("有效長 m", nameof(TrackInputRow.EffectiveLengthMeters), 85),
            TextColumn("速限 km/h", nameof(TrackInputRow.SpeedLimitKmh), 90),
            TextColumn("衝突資源", nameof(TrackInputRow.ConflictResourceIds), 140));
        var pathGrid = CreateGrid(paths,
            TextColumn("進路 ID", nameof(RoutePathInputRow.PathId), 130),
            TextColumn("起月台", nameof(RoutePathInputRow.FromPlatformId), 125),
            TextColumn("迄月台", nameof(RoutePathInputRow.ToPlatformId), 125),
            ComboColumn("方向", nameof(RoutePathInputRow.Direction), InfrastructureDirectionOptions, 70),
            TextColumn("股道 ID", nameof(RoutePathInputRow.TrackSegmentIds), 180),
            TextColumn("資源 ID", nameof(RoutePathInputRow.ResourceIds), 180));
        var turnbackGrid = CreateGrid(turnbacks,
            TextColumn("折返 ID", nameof(TurnbackInputRow.TurnbackId), 125),
            TextColumn("名稱", nameof(TurnbackInputRow.Name), 130),
            TextColumn("車站", nameof(TurnbackInputRow.StationId), 75),
            ComboColumn("形式", nameof(TurnbackInputRow.Kind), TurnbackKindOptions, 95),
            TextColumn("到達月台", nameof(TurnbackInputRow.ArrivalPlatformId), 110),
            TextColumn("發車月台", nameof(TurnbackInputRow.DeparturePlatformId), 110),
            TextColumn("股道", nameof(TurnbackInputRow.TrackSegmentIds), 100),
            TextColumn("資源", nameof(TurnbackInputRow.ResourceIds), 120),
            TextColumn("秒", nameof(TurnbackInputRow.TurnbackTimeSeconds), 65));
        tabs.Items.Add(CreateEditableTab("月台", platformGrid, platforms, () => new PlatformInputRow()));
        tabs.Items.Add(CreateEditableTab("股道", trackGrid, tracks, () => new TrackInputRow()));
        tabs.Items.Add(CreateEditableTab("進路", pathGrid, paths, () => new RoutePathInputRow()));
        tabs.Items.Add(CreateEditableTab("折返", turnbackGrid, turnbacks, () => new TurnbackInputRow()));
        var window = CreateEditorWindow("月台／股道／進路／折返", 1100, 650);
        ((DockPanel)window.Content).Children.Add(tabs);
        AddOkCancel(window, () =>
        {
            CommitGrid(platformGrid); CommitGrid(trackGrid); CommitGrid(pathGrid); CommitGrid(turnbackGrid);
            Replace(PlatformRows, platforms.Select(Clone));
            Replace(TrackRows, tracks.Select(Clone));
            Replace(RoutePathRows, paths.Select(Clone));
            Replace(TurnbackRows, turnbacks.Select(Clone));
            MarkInputCommitted("基礎設施已更新；請重新建立模擬。", window);
        });
        window.ShowDialog();
    }

    private VehicleTypeDefinition[] BuildVehicleTypeDefinitions() => BuildVehicleTypeDefinitions(VehicleTypeRows);

    private static VehicleTypeDefinition[] BuildVehicleTypeDefinitions(IEnumerable<VehicleTypeInputRow> source) => source.Select(row =>
        new VehicleTypeDefinition(row.Id, row.Name, row.LengthMeters, row.MaxSpeedKmh / 3.6,
            row.Acceleration, row.ServiceBrake, row.EmergencyBrake, row.Jerk,
            row.TractionDecay, row.CoastingDeceleration)).ToArray();

    private ServiceTypeDefinition[] BuildServiceTypeDefinitions() => BuildServiceTypeDefinitions(ServiceTypeRows);

    private static ServiceTypeDefinition[] BuildServiceTypeDefinitions(IEnumerable<ServiceTypeInputRow> source) => source.Select(row =>
        new ServiceTypeDefinition(row.Id, row.Name, row.ColorHex, row.RunPrefix,
            EmptyToNull(row.DefaultStopPatternId), EmptyToNull(row.DefaultVehicleTypeId), row.Priority,
            row.CanRequestOvertake, SplitIds(row.PreferredPlatformIds))).ToArray();

    private void ValidateVehicleTypeCatalog(IEnumerable<VehicleTypeInputRow> source)
    {
        var values = BuildVehicleTypeDefinitions(source);
        if (values.Length == 0) throw new InvalidOperationException("車型目錄至少需要一筆車型。");
        var ids = values.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count != values.Length) throw new InvalidOperationException("車型目錄包含重複的內部識別碼，請刪除後重新新增重複項目。");
        var missing = ServiceTypeRows.Select(item => item.DefaultVehicleTypeId)
            .Concat(HeadwayPlanRows.Select(item => item.VehicleTypeId))
            .Concat(ManualTimetableRows.Select(item => item.VehicleTypeId))
            .Where(item => !string.IsNullOrWhiteSpace(item) && !ids.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"車型仍被服務類型或發車計畫引用，無法刪除：{string.Join("、", missing)}。");
    }

    private void ValidateServiceTypeCatalog(IEnumerable<ServiceTypeInputRow> source)
    {
        var values = BuildServiceTypeDefinitions(source);
        if (values.Length == 0) throw new InvalidOperationException("服務類型目錄至少需要一筆服務。");
        var ids = values.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Count != values.Length) throw new InvalidOperationException("服務類型目錄包含重複的內部識別碼，請刪除後重新新增重複項目。");
        var vehicleIds = VehicleTypeRows.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stopIds = BuildStopPatternDefinitions().Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalidDefault = values.FirstOrDefault(item =>
            item.DefaultVehicleTypeId is { } vehicleId && !vehicleIds.Contains(vehicleId)
            || item.DefaultStopPatternId is { } stopId && !stopIds.Contains(stopId));
        if (invalidDefault is not null)
            throw new InvalidOperationException($"服務類型「{invalidDefault.DisplayName}」引用不存在的預設車型或停站模式。");
        var missing = HeadwayPlanRows.Select(item => item.ServiceTypeId)
            .Concat(ManualTimetableRows.Select(item => item.ServiceTypeId))
            .Where(item => !string.IsNullOrWhiteSpace(item) && !ids.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"服務類型仍被發車計畫引用，無法刪除：{string.Join("、", missing)}。");
    }

    private StopPatternDefinition[] BuildStopPatternDefinitions() => BuildStopPatternDefinitions(ServicePatternRows);

    private StopPatternDefinition[] BuildStopPatternDefinitions(IEnumerable<ServicePatternInputRow> source)
    {
        var allStop = new StopPatternDefinition("ALL_STOP", "所有車站停靠",
            StationRows.Select(row => new StopPatternInstruction(row.StationId, StopPatternAction.Stop, row.DwellTimeSeconds)));
        var validStations = StationRows.Select(row => row.StationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patterns = source.Select((row, index) => new
            {
                Row = row,
                Number = index + 1,
                Id = row.PatternId?.Trim() ?? string.Empty,
                Name = row.PatternName?.Trim() ?? string.Empty
            })
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                if (string.IsNullOrWhiteSpace(group.Key) || group.Key.Equals("ALL_STOP", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("自訂停站模式識別碼無效，請刪除後重新新增該模式。");
                var names = group.Select(item => item.Name).Distinct(StringComparer.Ordinal).ToArray();
                if (names.Length != 1 || string.IsNullOrWhiteSpace(names[0]))
                    throw new InvalidOperationException("同一停站模式的名稱必須一致且不得空白。");
                var instructions = group.Select(item =>
                {
                    var stationId = item.Row.StationId?.Trim() ?? string.Empty;
                    if (!validStations.Contains(stationId))
                        throw new InvalidOperationException($"停站模式第 {item.Number} 列的車站不存在於目前路線。");
                    var action = item.Row.Mode?.Trim() switch
                    {
                        "停站" => StopPatternAction.Stop,
                        "跨站" => StopPatternAction.Pass,
                        _ => throw new InvalidOperationException($"停站模式第 {item.Number} 列請選擇「停站」或「跨站」。")
                    };
                    var dwell = action == StopPatternAction.Stop ? item.Row.DwellTimeSeconds : null;
                    var passing = action == StopPatternAction.Pass ? item.Row.SpeedLimitKmh / 3.6 : null;
                    return new StopPatternInstruction(stationId, action, dwell, passing);
                }).ToArray();
                return new StopPatternDefinition(group.Key, names[0], instructions);
            }).ToArray();
        return [allStop, .. patterns];
    }

    private void ValidateStopPatternReferences(IEnumerable<StopPatternDefinition> patterns)
    {
        var ids = patterns.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = ServiceTypeRows.Select(item => item.DefaultStopPatternId)
            .Concat(HeadwayPlanRows.Select(item => item.StopPatternId))
            .Concat(ManualTimetableRows.Select(item => item.StopPatternId))
            .Where(item => !string.IsNullOrWhiteSpace(item) && !ids.Contains(item))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"停站模式仍被服務類型或發車計畫引用，無法刪除：{string.Join("、", missing)}。");
    }

    private ResolvedDispatchPlan BuildResolvedDispatchPlan()
    {
        var definition = new DispatchPlanDefinition(
            HeadwayPlanRows.Select(row => new HeadwayDirectionPlan(
                ParseDirection(row.Direction), ParseTime(row.FirstDeparture, "首班時間"),
                TimeSpan.FromMinutes(row.HeadwayMinutes), row.RunCount, row.ServiceTypeId,
                EmptyToNull(row.VehicleTypeId), EmptyToNull(row.StopPatternId), EmptyToNull(row.OriginPlatformId),
                EmptyToNull(row.VehicleId), row.ContinueAfterTerminal)),
            ManualTimetableRows.Select(row => new ManualTimetableRow(
                ParseTime(row.PlannedDeparture, "發車時間"), ParseDirection(row.Direction), row.ServiceTypeId,
                EmptyToNull(row.VehicleTypeId), EmptyToNull(row.StopPatternId), EmptyToNull(row.OriginPlatformId),
                EmptyToNull(row.VehicleId), EmptyToNull(row.ServiceRunId), row.ContinueAfterTerminal,
                EmptyToNull(row.ContinuationServiceRunId))),
            _dispatchPlanningMode == "手動班表" ? DispatchPlanningMode.ManualTimetable : DispatchPlanningMode.SimpleHeadway,
            _vehicleAssignmentMode == "全部指定" ? VehicleAssignmentMode.ExplicitOnly : VehicleAssignmentMode.Automatic);
        return DispatchPlanExpander.Expand(definition, BuildVehicleTypeDefinitions(), BuildServiceTypeDefinitions(), BuildStopPatternDefinitions());
    }

    private VehicleTypeDefinition ResolveBaselineVehicle(ResolvedDispatchPlan dispatchPlan)
    {
        var vehicleTypeId = dispatchPlan.Runs.FirstOrDefault()?.VehicleTypeId
            ?? throw new InvalidOperationException("發車計畫沒有可用車次。");
        return BuildVehicleTypeDefinitions().FirstOrDefault(item =>
                item.Id.Equals(vehicleTypeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"發車計畫引用不存在的車型「{vehicleTypeId}」。");
    }

    private InfrastructureGraph BuildInfrastructureGraph(EngineRoute route)
    {
        var spatialReferencePoints = BuildSpatialReferencePointDefinitions(SpatialReferencePointRows);
        if (PlatformRows.Count == 0 || TrackRows.Count == 0 || RoutePathRows.Count == 0)
        {
            var legacy = InfrastructureGraph.CreateLegacy(route);
            return new InfrastructureGraph(route, legacy.StationYards, legacy.TrackSegments, legacy.Paths,
                legacy.TurnbackPlans, spatialReferencePoints);
        }

        var platforms = PlatformRows.Select(row => new PlatformDefinition(row.PlatformId, row.StationId, row.Name,
            ParseTrackDirection(row.Direction), row.EffectiveLengthMeters, row.StoppingPositionMeters,
            row.PassengerService, SplitIds(row.AllowedVehicleTypeIds), SplitIds(row.AllowedServiceTypeIds),
            SplitIds(row.TrackSegmentIds))).ToArray();
        var tracks = TrackRows.Select(row => new TrackSegmentDefinition(row.TrackId, row.FromStationId, row.ToStationId,
            row.StartKm * 1000, row.EndKm * 1000, ParseTrackDirection(row.Direction), ParseTrackKind(row.Kind),
            row.EffectiveLengthMeters > 0 ? row.EffectiveLengthMeters : Math.Abs(row.EndKm - row.StartKm) * 1000,
            row.SpeedLimitKmh / 3.6, SplitIds(row.ConflictResourceIds))).ToArray();
        var paths = RoutePathRows.Select(row => new RoutePathDefinition(row.PathId, row.FromPlatformId,
            row.ToPlatformId, ParseTrackDirection(row.Direction), SplitIds(row.TrackSegmentIds), SplitIds(row.ResourceIds))).ToArray();
        var turnbacks = TurnbackRows.Select(row => new TurnbackPlanDefinition(row.TurnbackId, row.Name, row.StationId,
            ParseTurnbackKind(row.Kind), EmptyToNull(row.ArrivalPlatformId), EmptyToNull(row.DeparturePlatformId),
            SplitIds(row.TrackSegmentIds), SplitIds(row.ResourceIds), row.TurnbackTimeSeconds)).ToArray();
        var yards = route.Stations.Select(station => new StationYardDefinition(station.StationId, station.StationName,
            platforms.Where(item => item.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)),
            tracks.Where(item => item.FromStationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)
                || item.ToStationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)).Select(item => item.TrackId),
            paths.Where(item => item.FromPlatformId.StartsWith(station.StationId + ":", StringComparison.OrdinalIgnoreCase)
                || item.ToPlatformId.StartsWith(station.StationId + ":", StringComparison.OrdinalIgnoreCase)).Select(item => item.PathId),
            turnbacks.Where(item => item.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)))).ToArray();
        return new InfrastructureGraph(route, yards, tracks, paths, turnbacks, spatialReferencePoints);
    }

    private static SpatialReferencePointDefinition[] BuildSpatialReferencePointDefinitions(
        IEnumerable<SpatialReferencePointInputRow> rows) => rows.Select(row => new SpatialReferencePointDefinition(
            row.ReferencePointId, row.StationId, row.Name, ParseSpatialReferencePointKind(row.Kind),
            row.AlternateBerthing, row.MainlineGradePermille, row.BranchlineGradePermille,
            row.DistanceFromStopToCrossoverMeters, row.CrossoverLengthMeters,
            row.DistanceFromCrossoverToTurnbackStopMeters, row.TurnbackDwellSeconds,
            row.SwitchSpeedLimitKmh / 3.6, row.MainlineApproachCruiseSpeedKmh / 3.6,
            row.BranchlineApproachCruiseSpeedKmh / 3.6, row.MainlineSafetyFactor,
            row.BranchlineSafetyFactor, row.MainlineTrafficRatio,
            row.StationForwardGradeInPermille, row.StationForwardGradeOutPermille,
            row.StationForwardDistanceToSignalMeters, row.StationForwardOverlapMeters,
            row.StationForwardDwellSeconds, row.StationForwardEarlierCruiseSpeedKmh / 3.6,
            row.StationForwardLaterCruiseSpeedKmh / 3.6, row.StationForwardSafetyFactor,
            row.StationReverseGradeInPermille, row.StationReverseGradeOutPermille,
            row.StationReverseDistanceToSignalMeters, row.StationReverseOverlapMeters,
            row.StationReverseDwellSeconds, row.StationReverseEarlierCruiseSpeedKmh / 3.6,
            row.StationReverseLaterCruiseSpeedKmh / 3.6, row.StationReverseSafetyFactor)).ToArray();

    private void EnsureInfrastructureDraft()
    {
        if (PlatformRows.Count > 0 || TrackRows.Count > 0 || RoutePathRows.Count > 0) return;
        var route = BuildRoutePreview();
        var graph = InfrastructureGraph.CreateLegacy(route);
        Replace(PlatformRows, graph.Platforms.Select(item => new PlatformInputRow
        {
            PlatformId = item.PlatformId, StationId = item.StationId, Name = item.Name,
            Direction = TrackDirectionToChinese(item.Direction), EffectiveLengthMeters = item.EffectiveLengthMeters,
            StoppingPositionMeters = item.StoppingPositionMeters, PassengerService = item.AllowsPassengerService,
            AllowedVehicleTypeIds = JoinIds(item.AllowedVehicleTypeIds), AllowedServiceTypeIds = JoinIds(item.AllowedServiceTypeIds),
            TrackSegmentIds = JoinIds(item.TrackSegmentIds)
        }));
        Replace(TrackRows, graph.TrackSegments.Select(item => new TrackInputRow
        {
            TrackId = item.TrackId, FromStationId = item.FromStationId, ToStationId = item.ToStationId,
            StartKm = item.StartPositionMeters / 1000, EndKm = item.EndPositionMeters / 1000,
            Direction = TrackDirectionToChinese(item.Direction), Kind = TrackKindToChinese(item.Kind),
            EffectiveLengthMeters = item.EffectiveLengthMeters, SpeedLimitKmh = item.SpeedLimitMetersPerSecond * 3.6,
            ConflictResourceIds = JoinIds(item.ConflictResourceIds)
        }));
        Replace(RoutePathRows, graph.Paths.Select(item => new RoutePathInputRow
        {
            PathId = item.PathId, FromPlatformId = item.FromPlatformId, ToPlatformId = item.ToPlatformId,
            Direction = TrackDirectionToChinese(item.Direction), TrackSegmentIds = JoinIds(item.TrackSegmentIds),
            ResourceIds = JoinIds(item.ResourceIds)
        }));
        Replace(TurnbackRows, graph.TurnbackPlans.Select(item => new TurnbackInputRow
        {
            TurnbackId = item.TurnbackId, Name = item.Name, StationId = item.StationId,
            Kind = TurnbackKindToChinese(item.Kind), ArrivalPlatformId = item.ArrivalPlatformId ?? string.Empty,
            DeparturePlatformId = item.DeparturePlatformId ?? string.Empty, TrackSegmentIds = JoinIds(item.TrackSegmentIds),
            ResourceIds = JoinIds(item.ResourceIds), TurnbackTimeSeconds = item.TurnbackTimeSeconds
        }));
    }

    private EngineRoute BuildRoutePreview()
    {
        var dwell = TryParseFlexible(DefaultDwellTextBox.Text, out var value) ? value : 30;
        return RouteFactory.FromSegmentDistances(RouteIdTextBox.Text, RouteNameTextBox.Text,
            StationRows.Select(row => new StationInput(row.StationId, row.StationName,
                row.DistanceFromPreviousKm * 1000, row.DwellTimeSeconds)), dwell);
    }

    private ProjectInfrastructure? CaptureProjectInfrastructure(ProjectStation[] stations)
    {
        if (PlatformRows.Count == 0 || TrackRows.Count == 0 || RoutePathRows.Count == 0)
        {
            if (SpatialReferencePointRows.Count == 0) return null;
            EnsureInfrastructureDraft();
        }
        var platforms = PlatformRows.Select(row => new ProjectPlatform(row.PlatformId, row.StationId, row.Name,
            ParseTrackDirection(row.Direction), row.EffectiveLengthMeters, row.StoppingPositionMeters,
            row.PassengerService, SplitIds(row.AllowedVehicleTypeIds), SplitIds(row.AllowedServiceTypeIds),
            SplitIds(row.TrackSegmentIds))).ToArray();
        var tracks = TrackRows.Select(row => new ProjectTrackSegment(row.TrackId, row.FromStationId, row.ToStationId,
            row.StartKm * 1000, row.EndKm * 1000, ParseTrackDirection(row.Direction), ParseTrackKind(row.Kind),
            row.EffectiveLengthMeters > 0 ? row.EffectiveLengthMeters : Math.Abs(row.EndKm - row.StartKm) * 1000,
            row.SpeedLimitKmh / 3.6, SplitIds(row.ConflictResourceIds))).ToArray();
        var paths = RoutePathRows.Select(row => new ProjectRoutePath(row.PathId, row.FromPlatformId, row.ToPlatformId,
            ParseTrackDirection(row.Direction), SplitIds(row.TrackSegmentIds), SplitIds(row.ResourceIds))).ToArray();
        var turnbacks = TurnbackRows.Select(row => new ProjectTurnbackPlan(row.TurnbackId, row.Name, row.StationId,
            ParseTurnbackKind(row.Kind), EmptyToNull(row.ArrivalPlatformId), EmptyToNull(row.DeparturePlatformId),
            SplitIds(row.TrackSegmentIds), SplitIds(row.ResourceIds), row.TurnbackTimeSeconds)).ToArray();
        var yards = stations.Select(station => new ProjectStationYard(
            station.StationId,
            station.StationName,
            platforms.Where(item => item.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)).ToArray(),
            tracks.Where(item => item.FromStationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)
                    || item.ToStationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.TrackId).ToArray(),
            paths.Where(item => item.FromPlatformId.StartsWith(station.StationId + ":", StringComparison.OrdinalIgnoreCase)
                    || item.ToPlatformId.StartsWith(station.StationId + ":", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.PathId).ToArray(),
            turnbacks.Where(item => item.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)).ToArray()))
            .ToArray();
        var spatialReferencePoints = SpatialReferencePointRows.Select(row => new ProjectSpatialReferencePoint(
            row.ReferencePointId, row.StationId, row.Name, ParseSpatialReferencePointKind(row.Kind),
            row.AlternateBerthing, row.MainlineGradePermille, row.BranchlineGradePermille,
            row.DistanceFromStopToCrossoverMeters, row.CrossoverLengthMeters,
            row.DistanceFromCrossoverToTurnbackStopMeters, row.TurnbackDwellSeconds,
            row.SwitchSpeedLimitKmh / 3.6, row.MainlineApproachCruiseSpeedKmh / 3.6,
            row.BranchlineApproachCruiseSpeedKmh / 3.6, row.MainlineSafetyFactor,
            row.BranchlineSafetyFactor, row.MainlineTrafficRatio,
            row.StationForwardGradeInPermille, row.StationForwardGradeOutPermille,
            row.StationForwardDistanceToSignalMeters, row.StationForwardOverlapMeters,
            row.StationForwardDwellSeconds, row.StationForwardEarlierCruiseSpeedKmh / 3.6,
            row.StationForwardLaterCruiseSpeedKmh / 3.6, row.StationForwardSafetyFactor,
            row.StationReverseGradeInPermille, row.StationReverseGradeOutPermille,
            row.StationReverseDistanceToSignalMeters, row.StationReverseOverlapMeters,
            row.StationReverseDwellSeconds, row.StationReverseEarlierCruiseSpeedKmh / 3.6,
            row.StationReverseLaterCruiseSpeedKmh / 3.6, row.StationReverseSafetyFactor)).ToArray();
        return new ProjectInfrastructure(yards, tracks, paths, turnbacks, spatialReferencePoints);
    }

    private void ApplyExtendedProjectInputs(SimulationProjectDocument document)
    {
        Replace(VehicleTypeRows, (document.VehicleTypes ?? []).Select(item => new VehicleTypeInputRow
        {
            Id = item.Id, Name = item.DisplayName, LengthMeters = item.LengthMeters,
            MaxSpeedKmh = item.MaxSpeedMetersPerSecond * 3.6,
            Acceleration = item.AccelerationMetersPerSecondSquared,
            ServiceBrake = item.ServiceBrakeDecelerationMetersPerSecondSquared,
            EmergencyBrake = item.EmergencyBrakeDecelerationMetersPerSecondSquared,
            Jerk = item.JerkMetersPerSecondCubed,
            TractionDecay = item.TractionDecayPerSecond,
            CoastingDeceleration = item.CoastingDecelerationMetersPerSecondSquared
        }));
        Replace(ServiceTypeRows, (document.ServiceTypes ?? []).Select(item => new ServiceTypeInputRow
        {
            Id = item.Id, Name = item.DisplayName, ColorHex = item.ColorHex, RunPrefix = item.RunPrefix,
            DefaultStopPatternId = item.DefaultStopPatternId ?? string.Empty,
            DefaultVehicleTypeId = item.DefaultVehicleTypeId ?? string.Empty,
            Priority = item.Priority, CanRequestOvertake = item.CanRequestOvertake,
            PreferredPlatformIds = JoinIds(item.PreferredPlatformIds ?? [])
        }));

        ServicePatternRows.Clear();
        foreach (var pattern in (document.StopPatterns ?? []).Where(item => !item.Id.Equals("ALL_STOP", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var instruction in pattern.Instructions)
            {
                ServicePatternRows.Add(new ServicePatternInputRow
                {
                    PatternId = pattern.Id,
                    PatternName = pattern.DisplayName,
                    StationId = instruction.StationId,
                    Mode = instruction.Action == StopPatternAction.Pass ? "跨站" : "停站",
                    DwellTimeSeconds = instruction.DwellTimeSeconds,
                    SpeedLimitKmh = instruction.PassingSpeedLimitMetersPerSecond * 3.6
                });
            }
        }

        var dispatch = document.Dispatch;
        HeadwayPlanRows.Clear();
        foreach (var plan in dispatch?.SimpleHeadwayPlans ?? [])
        {
            HeadwayPlanRows.Add(new HeadwayPlanInputRow
            {
                Direction = DirectionToChinese(plan.Direction),
                FirstDeparture = TimeSpan.FromSeconds(plan.FirstDepartureTimeSeconds).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture),
                HeadwayMinutes = plan.HeadwaySeconds / 60,
                RunCount = plan.RunCount,
                ServiceTypeId = plan.ServiceTypeId,
                VehicleTypeId = plan.VehicleTypeId ?? string.Empty,
                StopPatternId = plan.StopPatternId ?? string.Empty,
                OriginPlatformId = plan.OriginPlatformId ?? string.Empty,
                VehicleId = plan.VehicleId ?? string.Empty,
                ContinueAfterTerminal = plan.ContinueAfterTerminal
            });
        }
        ManualTimetableRows.Clear();
        foreach (var row in dispatch?.ManualTimetableRows ?? [])
        {
            ManualTimetableRows.Add(new ManualTimetableInputRow
            {
                PlannedDeparture = TimeSpan.FromSeconds(row.PlannedDepartureTimeSeconds).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture),
                Direction = DirectionToChinese(row.Direction),
                ServiceTypeId = row.ServiceTypeId,
                VehicleTypeId = row.VehicleTypeId ?? string.Empty,
                StopPatternId = row.StopPatternId ?? string.Empty,
                OriginPlatformId = row.OriginPlatformId ?? string.Empty,
                VehicleId = row.VehicleId ?? string.Empty,
                ServiceRunId = row.ServiceRunId ?? string.Empty,
                ContinueAfterTerminal = row.ContinueAfterTerminal,
                ContinuationServiceRunId = row.ContinuationServiceRunId ?? string.Empty
            });
        }
        _dispatchPlanningMode = dispatch?.ActiveMode == DispatchPlanningMode.ManualTimetable ? "手動班表" : "簡易班距";
        _vehicleAssignmentMode = dispatch?.VehicleAssignmentMode == VehicleAssignmentMode.ExplicitOnly ? "全部指定" : "自動配置";

        var infrastructure = document.Infrastructure;
        Replace(PlatformRows, (infrastructure?.StationYards ?? []).SelectMany(yard => yard.Platforms ?? [])
            .GroupBy(item => item.PlatformId, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .Select(item => new PlatformInputRow
            {
                PlatformId = item.PlatformId, StationId = item.StationId, Name = item.Name,
                Direction = TrackDirectionToChinese(item.Direction), EffectiveLengthMeters = item.EffectiveLengthMeters,
                StoppingPositionMeters = item.StoppingPositionMeters, PassengerService = item.AllowsPassengerService,
                AllowedVehicleTypeIds = JoinIds(item.AllowedVehicleTypeIds ?? []),
                AllowedServiceTypeIds = JoinIds(item.AllowedServiceTypeIds ?? []),
                TrackSegmentIds = JoinIds(item.TrackSegmentIds ?? [])
            }));
        Replace(TrackRows, (infrastructure?.TrackSegments ?? []).Select(item => new TrackInputRow
        {
            TrackId = item.TrackId, FromStationId = item.FromStationId, ToStationId = item.ToStationId,
            StartKm = item.StartPositionMeters / 1000, EndKm = item.EndPositionMeters / 1000,
            Direction = TrackDirectionToChinese(item.Direction), Kind = TrackKindToChinese(item.Kind),
            EffectiveLengthMeters = item.EffectiveLengthMeters,
            SpeedLimitKmh = item.SpeedLimitMetersPerSecond * 3.6,
            ConflictResourceIds = JoinIds(item.ConflictResourceIds ?? [])
        }));
        Replace(RoutePathRows, (infrastructure?.Paths ?? []).Select(item => new RoutePathInputRow
        {
            PathId = item.PathId, FromPlatformId = item.FromPlatformId, ToPlatformId = item.ToPlatformId,
            Direction = TrackDirectionToChinese(item.Direction), TrackSegmentIds = JoinIds(item.TrackSegmentIds),
            ResourceIds = JoinIds(item.ResourceIds ?? [])
        }));
        Replace(TurnbackRows, (infrastructure?.TurnbackPlans ?? []).Select(item => new TurnbackInputRow
        {
            TurnbackId = item.TurnbackId, Name = item.Name, StationId = item.StationId,
            Kind = TurnbackKindToChinese(item.Kind), ArrivalPlatformId = item.ArrivalPlatformId ?? string.Empty,
            DeparturePlatformId = item.DeparturePlatformId ?? string.Empty,
            TrackSegmentIds = JoinIds(item.TrackSegmentIds ?? []), ResourceIds = JoinIds(item.ResourceIds ?? []),
            TurnbackTimeSeconds = item.TurnbackTimeSeconds
        }));
        Replace(SpatialReferencePointRows, (infrastructure?.SpatialReferencePoints ?? []).Select(item => new SpatialReferencePointInputRow
        {
            ReferencePointId = item.ReferencePointId,
            StationId = item.StationId,
            Name = item.Name,
            Kind = SpatialReferencePointKindToChinese(item.Kind),
            AlternateBerthing = item.AlternateBerthing,
            MainlineGradePermille = item.MainlineGradePermille,
            BranchlineGradePermille = item.BranchlineGradePermille,
            DistanceFromStopToCrossoverMeters = item.DistanceFromStopToCrossoverMeters,
            CrossoverLengthMeters = item.CrossoverLengthMeters,
            DistanceFromCrossoverToTurnbackStopMeters = item.DistanceFromCrossoverToTurnbackStopMeters,
            TurnbackDwellSeconds = item.TurnbackDwellSeconds,
            SwitchSpeedLimitKmh = item.SwitchSpeedLimitMetersPerSecond * 3.6,
            MainlineApproachCruiseSpeedKmh = item.MainlineApproachCruiseSpeedMetersPerSecond * 3.6,
            BranchlineApproachCruiseSpeedKmh = item.BranchlineApproachCruiseSpeedMetersPerSecond * 3.6,
            MainlineSafetyFactor = item.MainlineSafetyFactor,
            BranchlineSafetyFactor = item.BranchlineSafetyFactor,
            MainlineTrafficRatio = item.MainlineTrafficRatio,
            StationForwardGradeInPermille = item.StationForwardGradeInPermille,
            StationForwardGradeOutPermille = item.StationForwardGradeOutPermille,
            StationForwardDistanceToSignalMeters = item.StationForwardDistanceToSignalMeters,
            StationForwardOverlapMeters = item.StationForwardOverlapMeters,
            StationForwardDwellSeconds = item.StationForwardDwellSeconds,
            StationForwardEarlierCruiseSpeedKmh = item.StationForwardEarlierCruiseSpeedMetersPerSecond * 3.6,
            StationForwardLaterCruiseSpeedKmh = item.StationForwardLaterCruiseSpeedMetersPerSecond * 3.6,
            StationForwardSafetyFactor = item.StationForwardSafetyFactor,
            StationReverseGradeInPermille = item.StationReverseGradeInPermille,
            StationReverseGradeOutPermille = item.StationReverseGradeOutPermille,
            StationReverseDistanceToSignalMeters = item.StationReverseDistanceToSignalMeters,
            StationReverseOverlapMeters = item.StationReverseOverlapMeters,
            StationReverseDwellSeconds = item.StationReverseDwellSeconds,
            StationReverseEarlierCruiseSpeedKmh = item.StationReverseEarlierCruiseSpeedMetersPerSecond * 3.6,
            StationReverseLaterCruiseSpeedKmh = item.StationReverseLaterCruiseSpeedMetersPerSecond * 3.6,
            StationReverseSafetyFactor = item.StationReverseSafetyFactor
        }));
    }

    private void MarkInputCommitted(string status, Window window)
    {
        PausePlayback();
        ClearResults();
        StatusTextBlock.Text = status;
        window.DialogResult = true;
    }

    private static Window CreateEditorWindow(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        MinWidth = 760,
        MinHeight = 460,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = new DockPanel()
    };

    private void ShowTransactionalEditor<T>(string title, ObservableCollection<T> source, Func<T, T> clone,
        Func<ObservableCollection<T>, T> create, IEnumerable<DataGridColumn> columns, double width,
        Action<IEnumerable<T>>? validate = null)
    {
        var draft = new ObservableCollection<T>(source.Select(clone));
        var grid = CreateGrid(draft, columns.ToArray());
        var window = CreateEditorWindow(title, width, 560);
        window.Owner = this;
        ((DockPanel)window.Content).Children.Add(CreateEditorPanel(grid, draft, () => create(draft)));
        AddOkCancel(window, () =>
        {
            try
            {
                CommitGrid(grid);
                validate?.Invoke(draft);
                Replace(source, draft.Select(clone));
                MarkInputCommitted($"{title}已更新；請重新建立模擬。", window);
            }
            catch (Exception exception) when (exception is InvalidOperationException or SimulationValidationException)
            {
                ShowValidation(exception is SimulationValidationException validation ? validation.Errors : [exception.Message]);
            }
        });
        window.ShowDialog();
    }

    private static TabItem CreateEditableTab<T>(string title, DataGrid grid, ObservableCollection<T> rows, Func<T> create) =>
        new() { Header = title, Content = CreateEditorPanel(grid, rows, create) };

    private static FrameworkElement CreateEditorPanel<T>(DataGrid grid, ObservableCollection<T> rows, Func<T> create)
    {
        var panel = new DockPanel { Margin = new Thickness(10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var add = new Button { Content = "新增", MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
        var remove = new Button { Content = "刪除選取", MinWidth = 88 };
        add.Click += (_, _) => rows.Add(create());
        remove.Click += (_, _) =>
        {
            if (grid.SelectedItem is T selected) rows.Remove(selected);
        };
        buttons.Children.Add(add); buttons.Children.Add(remove);
        DockPanel.SetDock(buttons, Dock.Top);
        panel.Children.Add(buttons); panel.Children.Add(grid);
        return panel;
    }

    private static DataGrid CreateGrid(object items, params DataGridColumn[] columns)
    {
        var grid = new DataGrid { ItemsSource = (System.Collections.IEnumerable)items, AutoGenerateColumns = false,
            CanUserAddRows = false, RowHeaderWidth = 0, SelectionMode = DataGridSelectionMode.Single };
        foreach (var column in columns) grid.Columns.Add(column);
        return grid;
    }

    private static void AddOkCancel(Window window, Action commit)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10) };
        var ok = new Button { Content = "確定", MinWidth = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", MinWidth = 88, IsCancel = true };
        ok.Click += (_, _) => commit();
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        ((DockPanel)window.Content).Children.Insert(0, buttons);
    }

    private static void CommitGrid(DataGrid grid)
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width) => new()
    {
        Header = header, Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width
    };

    private static DataGridCheckBoxColumn CheckColumn(string header, string property, double width) => new()
    {
        Header = header, Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width
    };

    private static DataGridComboBoxColumn ComboColumn(string header, string property, IEnumerable<string> items, double width) => new()
    {
        Header = header, SelectedItemBinding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        ItemsSource = items.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Width = width
    };

    private static DataGridComboBoxColumn OptionComboColumn(
        string header,
        string property,
        IEnumerable<CatalogOption> items,
        double width) => new()
    {
        Header = header,
        SelectedValueBinding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        SelectedValuePath = nameof(CatalogOption.Id),
        DisplayMemberPath = nameof(CatalogOption.DisplayName),
        ItemsSource = items.ToArray(),
        Width = width
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static string[] SplitIds(string? value) => (value ?? string.Empty)
        .Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static string JoinIds(IEnumerable<string> values) => string.Join(",", values);
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string CreateGeneratedId(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)].ToUpperInvariant();
    private static TimeSpan ParseTime(string text, string field) => TimeSpan.TryParseExact(text.Trim(), ["h\\:mm", "hh\\:mm", "h\\:mm\\:ss", "hh\\:mm\\:ss"],
        CultureInfo.InvariantCulture, out var value) ? value : throw new InvalidOperationException($"{field}格式必須是 HH:mm 或 HH:mm:ss。");
    private static TrainDirection ParseDirection(string value) => value == "上行" ? TrainDirection.Inbound : TrainDirection.Outbound;
    private static TrackDirection ParseTrackDirection(string value) => value switch { "下行" => TrackDirection.Outbound, "上行" => TrackDirection.Inbound, _ => TrackDirection.Both };
    private static string TrackDirectionToChinese(TrackDirection value) => value switch { TrackDirection.Outbound => "下行", TrackDirection.Inbound => "上行", _ => "雙向" };
    private static TrackKind ParseTrackKind(string value) => value switch { "月台線" => TrackKind.Platform, "待避線" => TrackKind.Passing, "側線" => TrackKind.Siding,
        "橫渡線" => TrackKind.Crossover, "折返線" => TrackKind.Turnback, "尾軌" => TrackKind.TailTrack, "進站線" => TrackKind.Approach, "其他" => TrackKind.Other, _ => TrackKind.Mainline };
    private static string TrackKindToChinese(TrackKind value) => value switch { TrackKind.Platform => "月台線", TrackKind.Passing => "待避線", TrackKind.Siding => "側線",
        TrackKind.Crossover => "橫渡線", TrackKind.Turnback => "折返線", TrackKind.TailTrack => "尾軌", TrackKind.Approach => "進站線", TrackKind.Other => "其他", _ => "正線" };
    private static TurnbackKind ParseTurnbackKind(string value) => value switch { "站前折返" => TurnbackKind.BeforeStation, "站後折返" => TurnbackKind.AfterStation, _ => TurnbackKind.LegacyAbstract };
    private static string TurnbackKindToChinese(TurnbackKind value) => value switch { TurnbackKind.BeforeStation => "站前折返", TurnbackKind.AfterStation => "站後折返", _ => "抽象折返" };
    private static SpatialReferencePointKind ParseSpatialReferencePointKind(string value) => value switch
    {
        "中間站" => SpatialReferencePointKind.IntermediateStation,
        "銜接點" => SpatialReferencePointKind.Junction,
        "站後折返" => SpatialReferencePointKind.AfterStationTurnback,
        "中央避車線折返" => SpatialReferencePointKind.CentralSidingTurnback,
        _ => SpatialReferencePointKind.BeforeStationTurnback
    };
    private static string SpatialReferencePointKindToChinese(SpatialReferencePointKind value) => value switch
    {
        SpatialReferencePointKind.IntermediateStation => "中間站",
        SpatialReferencePointKind.Junction => "銜接點",
        SpatialReferencePointKind.AfterStationTurnback => "站後折返",
        SpatialReferencePointKind.CentralSidingTurnback => "中央避車線折返",
        _ => "站前折返"
    };

    private static VehicleTypeInputRow Clone(VehicleTypeInputRow x) => new() { Id = x.Id, Name = x.Name, LengthMeters = x.LengthMeters, MaxSpeedKmh = x.MaxSpeedKmh,
        Acceleration = x.Acceleration, ServiceBrake = x.ServiceBrake, EmergencyBrake = x.EmergencyBrake, Jerk = x.Jerk, TractionDecay = x.TractionDecay, CoastingDeceleration = x.CoastingDeceleration };
    private static ServiceTypeInputRow Clone(ServiceTypeInputRow x) => new() { Id = x.Id, Name = x.Name, ColorHex = x.ColorHex, RunPrefix = x.RunPrefix,
        DefaultStopPatternId = x.DefaultStopPatternId, DefaultVehicleTypeId = x.DefaultVehicleTypeId, Priority = x.Priority, CanRequestOvertake = x.CanRequestOvertake,
        PreferredPlatformIds = x.PreferredPlatformIds };
    private static ServicePatternInputRow Clone(ServicePatternInputRow x) => new() { PatternId = x.PatternId, PatternName = x.PatternName,
        StationId = x.StationId, Mode = x.Mode, DwellTimeSeconds = x.DwellTimeSeconds, SpeedLimitKmh = x.SpeedLimitKmh };
    private static HeadwayPlanInputRow Clone(HeadwayPlanInputRow x) => new() { Direction = x.Direction, FirstDeparture = x.FirstDeparture, HeadwayMinutes = x.HeadwayMinutes,
        RunCount = x.RunCount, ServiceTypeId = x.ServiceTypeId, VehicleTypeId = x.VehicleTypeId, StopPatternId = x.StopPatternId, OriginPlatformId = x.OriginPlatformId,
        VehicleId = x.VehicleId, ContinueAfterTerminal = x.ContinueAfterTerminal };
    private static ManualTimetableInputRow Clone(ManualTimetableInputRow x) => new() { PlannedDeparture = x.PlannedDeparture, Direction = x.Direction,
        ServiceTypeId = x.ServiceTypeId, VehicleTypeId = x.VehicleTypeId, StopPatternId = x.StopPatternId, OriginPlatformId = x.OriginPlatformId,
        VehicleId = x.VehicleId, ServiceRunId = x.ServiceRunId, ContinueAfterTerminal = x.ContinueAfterTerminal,
        ContinuationServiceRunId = x.ContinuationServiceRunId };
    private static PlatformInputRow Clone(PlatformInputRow x) => new() { PlatformId = x.PlatformId, StationId = x.StationId, Name = x.Name, Direction = x.Direction,
        EffectiveLengthMeters = x.EffectiveLengthMeters, StoppingPositionMeters = x.StoppingPositionMeters, PassengerService = x.PassengerService,
        AllowedVehicleTypeIds = x.AllowedVehicleTypeIds, AllowedServiceTypeIds = x.AllowedServiceTypeIds, TrackSegmentIds = x.TrackSegmentIds };
    private static TrackInputRow Clone(TrackInputRow x) => new() { TrackId = x.TrackId, FromStationId = x.FromStationId, ToStationId = x.ToStationId,
        StartKm = x.StartKm, EndKm = x.EndKm, Direction = x.Direction, Kind = x.Kind, EffectiveLengthMeters = x.EffectiveLengthMeters,
        SpeedLimitKmh = x.SpeedLimitKmh, ConflictResourceIds = x.ConflictResourceIds };
    private static RoutePathInputRow Clone(RoutePathInputRow x) => new() { PathId = x.PathId, FromPlatformId = x.FromPlatformId, ToPlatformId = x.ToPlatformId,
        Direction = x.Direction, TrackSegmentIds = x.TrackSegmentIds, ResourceIds = x.ResourceIds };
    private static TurnbackInputRow Clone(TurnbackInputRow x) => new() { TurnbackId = x.TurnbackId, Name = x.Name, StationId = x.StationId, Kind = x.Kind,
        ArrivalPlatformId = x.ArrivalPlatformId, DeparturePlatformId = x.DeparturePlatformId, TrackSegmentIds = x.TrackSegmentIds,
        ResourceIds = x.ResourceIds, TurnbackTimeSeconds = x.TurnbackTimeSeconds };
    private static SpatialReferencePointInputRow Clone(SpatialReferencePointInputRow x) => new()
    {
        ReferencePointId = x.ReferencePointId, StationId = x.StationId, Name = x.Name, Kind = x.Kind,
        AlternateBerthing = x.AlternateBerthing, MainlineGradePermille = x.MainlineGradePermille,
        BranchlineGradePermille = x.BranchlineGradePermille,
        DistanceFromStopToCrossoverMeters = x.DistanceFromStopToCrossoverMeters,
        CrossoverLengthMeters = x.CrossoverLengthMeters,
        DistanceFromCrossoverToTurnbackStopMeters = x.DistanceFromCrossoverToTurnbackStopMeters,
        TurnbackDwellSeconds = x.TurnbackDwellSeconds, SwitchSpeedLimitKmh = x.SwitchSpeedLimitKmh,
        MainlineApproachCruiseSpeedKmh = x.MainlineApproachCruiseSpeedKmh,
        BranchlineApproachCruiseSpeedKmh = x.BranchlineApproachCruiseSpeedKmh,
        MainlineSafetyFactor = x.MainlineSafetyFactor, BranchlineSafetyFactor = x.BranchlineSafetyFactor,
        MainlineTrafficRatio = x.MainlineTrafficRatio,
        StationForwardGradeInPermille = x.StationForwardGradeInPermille,
        StationForwardGradeOutPermille = x.StationForwardGradeOutPermille,
        StationForwardDistanceToSignalMeters = x.StationForwardDistanceToSignalMeters,
        StationForwardOverlapMeters = x.StationForwardOverlapMeters,
        StationForwardDwellSeconds = x.StationForwardDwellSeconds,
        StationForwardEarlierCruiseSpeedKmh = x.StationForwardEarlierCruiseSpeedKmh,
        StationForwardLaterCruiseSpeedKmh = x.StationForwardLaterCruiseSpeedKmh,
        StationForwardSafetyFactor = x.StationForwardSafetyFactor,
        StationReverseGradeInPermille = x.StationReverseGradeInPermille,
        StationReverseGradeOutPermille = x.StationReverseGradeOutPermille,
        StationReverseDistanceToSignalMeters = x.StationReverseDistanceToSignalMeters,
        StationReverseOverlapMeters = x.StationReverseOverlapMeters,
        StationReverseDwellSeconds = x.StationReverseDwellSeconds,
        StationReverseEarlierCruiseSpeedKmh = x.StationReverseEarlierCruiseSpeedKmh,
        StationReverseLaterCruiseSpeedKmh = x.StationReverseLaterCruiseSpeedKmh,
        StationReverseSafetyFactor = x.StationReverseSafetyFactor
    };
}
