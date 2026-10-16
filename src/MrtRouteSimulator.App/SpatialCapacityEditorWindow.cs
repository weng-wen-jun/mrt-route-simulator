using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 容量設定的獨立交易式編輯器。所有控制項都只編輯本視窗內的 draft，按套用前不會
/// 寫回 MainWindow 或 Schema 8 文件；容量公式集中在計算核心。
/// </summary>
internal sealed class SpatialCapacityEditorWindow : Window
{
    private readonly TopologyProjectDocument document;
    private readonly ComboBox modeComboBox = new();
    private readonly CheckBox compareAsScenarioCheckBox = new();
    private readonly Dictionary<string, TextBox> globalEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBox> trainEditors = new(StringComparer.Ordinal);
    private readonly Dictionary<SpatialReferencePointKind, ObservableCollection<CapacityPointDraft>> pointGroups = [];
    private readonly Dictionary<SpatialReferencePointKind, ListBox> pointLists = [];
    private readonly Dictionary<SpatialReferencePointKind, StackPanel> pointDetails = [];
    private readonly Dictionary<string, FrameworkElement> activePointEditors = new(StringComparer.Ordinal);
    private CapacityPointDraft? activePoint;
    private StackPanel? activeDetailPanel;
    private ListBox? activePointList;
    private TabControl? pointTabs;
    private int activeTabIndex;
    private bool applying;
    private bool suppressSelectionEvents;

    public SpatialCapacityEditorWindow(TopologyProjectDocument document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        Title = "容量參考點設定";
        Width = 1180;
        Height = 820;
        MinWidth = 900;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        EditorChrome.ApplyWindowChrome(this);
        InitializeDraft(document.SpatialCapacity);
        Content = BuildContent();
    }

    public TopologyProjectDocument? Result { get; private set; }

    private void InitializeDraft(SpatialCapacityProjectSettings? settings)
    {
        var points = settings?.Points ?? [];
        foreach (var kind in Enum.GetValues<SpatialReferencePointKind>())
            pointGroups[kind] = new ObservableCollection<CapacityPointDraft>(
                points.Where(point => point.Kind == kind).Select(CapacityPointDraft.FromDomain));
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var note = new TextBlock
        {
            Text = "容量參考點是獨立的手動分析假設，不會自動帶入路線資料，也不改變列車模擬。速度單位為 km/h。坡度沿各自行駛方向解讀：正值為上坡、負值為下坡。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(note);

        var modelPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        modelPanel.Children.Add(EditorGroup("計算模式", BuildModeEditor(), 240));
        modelPanel.Children.Add(EditorGroup("全域容量參數", BuildGlobalEditors(), 330));
        modelPanel.Children.Add(EditorGroup("列車容量與動力參數", BuildTrainEditors(), 385));
        var modelScroll = new ScrollViewer
        {
            Content = modelPanel,
            MaxHeight = 270,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(modelScroll, 1);
        root.Children.Add(modelScroll);

        var pointsTabs = BuildPointTabs();
        Grid.SetRow(pointsTabs, 2);
        root.Children.Add(pointsTabs);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var clear = new Button { Content = "清除容量設定", MinWidth = 112, HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) =>
        {
            Result = document with { SpatialCapacity = null };
            DialogResult = true;
        };
        actions.Children.Add(clear);
        var apply = new Button { Content = "套用", MinWidth = 84, IsDefault = true };
        apply.Click += Apply_Click;
        var cancel = new Button { Content = "取消", MinWidth = 84, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        return root;
    }

    private UIElement BuildModeEditor()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(EditorChrome.FieldLabel("容量公式模式"));
        var choices = new[]
        {
            new ModeChoice(SpatialCapacityCalculationMode.TaiwanHandbook, "台灣手冊"),
            new ModeChoice(SpatialCapacityCalculationMode.UrcsCompatible, "URCS 相容")
        };
        modeComboBox.ItemsSource = choices;
        modeComboBox.DisplayMemberPath = nameof(ModeChoice.Label);
        modeComboBox.SelectedValuePath = nameof(ModeChoice.Value);
        modeComboBox.SelectedValue = document.SpatialCapacity?.Mode ?? SpatialCapacityCalculationMode.TaiwanHandbook;
        modeComboBox.MinWidth = 205;
        modeComboBox.Margin = new Thickness(0, 3, 0, 8);
        AutomationProperties.SetName(modeComboBox, "容量計算模式");
        panel.Children.Add(modeComboBox);
        panel.Children.Add(new TextBlock
        {
            Text = "新設定預設台灣手冊；既有 URCS 相容設定會原樣保留。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextMutedBrush,
            FontSize = 11,
            MaxWidth = 210
        });
        compareAsScenarioCheckBox.Content = new TextBlock
        {
            Text = "這些參考點屬同一容量分析情境，顯示瓶頸",
            TextWrapping = TextWrapping.Wrap
        };
        compareAsScenarioCheckBox.IsChecked = document.SpatialCapacity?.CompareAsSingleScenario == true;
        compareAsScenarioCheckBox.Margin = new Thickness(0, 6, 0, 0);
        compareAsScenarioCheckBox.MaxWidth = 220;
        AutomationProperties.SetName(compareAsScenarioCheckBox, "顯示同一容量分析情境瓶頸");
        panel.Children.Add(compareAsScenarioCheckBox);
        return panel;
    }

    private UIElement BuildGlobalEditors()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        var global = document.SpatialCapacity?.Global ?? new SpatialCapacityGlobalParameters();
        AddNumericEditor(panel, globalEditors, "號誌／轉換時間 (s)", nameof(SpatialCapacityGlobalParameters.InterchangeTimeSeconds), global.InterchangeTimeSeconds);
        AddNumericEditor(panel, globalEditors, "反應時間 (s)", nameof(SpatialCapacityGlobalParameters.ReactionTimeSeconds), global.ReactionTimeSeconds);
        AddNumericEditor(panel, globalEditors, "列車尖峰小時因子 PHF", nameof(SpatialCapacityGlobalParameters.TrainPeakHourFactor), global.TrainPeakHourFactor);
        AddNumericEditor(panel, globalEditors, "營運餘裕率", nameof(SpatialCapacityGlobalParameters.MarginRate), global.MarginRate);
        return panel;
    }

    private UIElement BuildTrainEditors()
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        var train = document.SpatialCapacity?.Train ?? new SpatialCapacityTrainParameters();
        AddNumericEditor(panel, trainEditors, "列車長度 (m)", nameof(SpatialCapacityTrainParameters.LengthMeters), train.LengthMeters);
        AddNumericEditor(panel, trainEditors, "加速度 (m/s²)", nameof(SpatialCapacityTrainParameters.AccelerationMetersPerSecondSquared), train.AccelerationMetersPerSecondSquared);
        AddNumericEditor(panel, trainEditors, "減速度 (m/s²)", nameof(SpatialCapacityTrainParameters.DecelerationMetersPerSecondSquared), train.DecelerationMetersPerSecondSquared);
        AddNumericEditor(panel, trainEditors, "煞車有效因子", nameof(SpatialCapacityTrainParameters.BrakeEffect), train.BrakeEffect);
        AddNumericEditor(panel, trainEditors, "座位數", nameof(SpatialCapacityTrainParameters.SeatCount), train.SeatCount);
        AddNumericEditor(panel, trainEditors, "站立面積 (m²)", nameof(SpatialCapacityTrainParameters.StandingAreaSquareMeters), train.StandingAreaSquareMeters);
        AddNumericEditor(panel, trainEditors, "站立密度 (人/m²)", nameof(SpatialCapacityTrainParameters.StandingDensityPerSquareMeter), train.StandingDensityPerSquareMeter);
        return panel;
    }

    private TabControl BuildPointTabs()
    {
        var tabs = new TabControl { Margin = new Thickness(0, 10, 0, 0) };
        pointTabs = tabs;
        foreach (var kind in Enum.GetValues<SpatialReferencePointKind>())
        {
            var list = new ListBox
            {
                ItemsSource = pointGroups[kind],
                DisplayMemberPath = nameof(CapacityPointDraft.Display),
                MinWidth = 240,
                Margin = new Thickness(0, 0, 10, 0)
            };
            list.SelectionChanged += (_, _) =>
            {
                if (applying || suppressSelectionEvents) return;
                SelectPoint(kind, list);
            };
            pointLists[kind] = list;

            var detail = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            pointDetails[kind] = detail;
            var add = new Button { Content = "新增", MinWidth = 70, Margin = new Thickness(0, 0, 6, 0) };
            add.Click += (_, _) => AddPoint(kind, list);
            var remove = new Button { Content = "刪除", MinWidth = 70 };
            remove.Click += (_, _) => RemovePoint(kind, list);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            actions.Children.Add(add);
            actions.Children.Add(remove);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.35, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.65, GridUnitType.Star) });
            body.Children.Add(list);
            var detailScroll = new ScrollViewer
            {
                Content = detail,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetColumn(detailScroll, 1);
            body.Children.Add(detailScroll);
            var content = new DockPanel();
            DockPanel.SetDock(actions, Dock.Top);
            content.Children.Add(actions);
            content.Children.Add(body);
            tabs.Items.Add(new TabItem
            {
                Header = MainWindow.SpatialCapacityKindText(kind),
                Content = content,
                Tag = kind
            });
        }

        tabs.SelectionChanged += (_, _) =>
        {
            if (applying || suppressSelectionEvents || tabs.SelectedItem is not TabItem { Tag: SpatialReferencePointKind kind }) return;
            if (!CommitActivePoint(false))
            {
                suppressSelectionEvents = true;
                tabs.SelectedIndex = activeTabIndex;
                suppressSelectionEvents = false;
                return;
            }

            activeTabIndex = tabs.SelectedIndex;
            var list = pointLists[kind];
            if (list.SelectedItem is null && list.Items.Count > 0) list.SelectedIndex = 0;
            else SelectPoint(kind, list);
        };
        if (tabs.Items.Count > 0)
        {
            tabs.SelectedIndex = 0;
            activeTabIndex = 0;
        }
        return tabs;
    }

    private void AddPoint(SpatialReferencePointKind kind, ListBox list)
    {
        if (!CommitActivePoint(false)) return;
        var number = NextPointNumber();
        var point = CapacityPointDraft.Create(kind, number);
        pointGroups[kind].Add(point);
        list.SelectedItem = point;
        SelectPoint(kind, list);
    }

    private int NextPointNumber()
    {
        var usedIds = pointGroups.Values
            .SelectMany(group => group)
            .Select(point => point.ReferencePointId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        while (usedIds.Contains($"CAP-{number:000}")) number++;
        return number;
    }

    private void RemovePoint(SpatialReferencePointKind kind, ListBox list)
    {
        if (!CommitActivePoint(false)) return;
        if (list.SelectedItem is not CapacityPointDraft point) return;
        pointGroups[kind].Remove(point);
        if (ReferenceEquals(activePoint, point))
        {
            activePoint = null;
            activePointList = null;
            activeDetailPanel = null;
            activePointEditors.Clear();
        }
        if (list.Items.Count > 0)
        {
            list.SelectedIndex = Math.Min(Math.Max(0, list.SelectedIndex), list.Items.Count - 1);
        }
        else
        {
            activePoint = null;
            activePointEditors.Clear();
            pointDetails[kind].Children.Clear();
        }
    }

    private void SelectPoint(SpatialReferencePointKind kind, ListBox list)
    {
        if (ReferenceEquals(activePoint, list.SelectedItem)) return;
        var previous = activePoint;
        if (!CommitActivePoint(false))
        {
            suppressSelectionEvents = true;
            list.SelectedItem = previous;
            suppressSelectionEvents = false;
            return;
        }
        activePoint = list.SelectedItem as CapacityPointDraft;
        activePointList = list;
        activeDetailPanel = pointDetails[kind];
        activeDetailPanel.Children.Clear();
        activePointEditors.Clear();
        if (activePoint is null) return;
        BuildPointDetails(kind, activePoint, activeDetailPanel);
    }

    private void BuildPointDetails(SpatialReferencePointKind kind, CapacityPointDraft point, StackPanel panel)
    {
        AddTextEditor(panel, "參考點 ID", nameof(CapacityPointDraft.ReferencePointId), point.ReferencePointId);
        AddTextEditor(panel, "位置名稱", nameof(CapacityPointDraft.StationId), point.StationId);
        AddTextEditor(panel, "顯示名稱", nameof(CapacityPointDraft.Name), point.Name);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8) });

        switch (kind)
        {
            case SpatialReferencePointKind.IntermediateStation:
                AddTextEditor(panel, "順行進站坡度 ‰（正值上坡）", nameof(CapacityPointDraft.StationForwardGradeInPermille), point.StationForwardGradeInPermille);
                AddTextEditor(panel, "順行離站坡度 ‰（正值上坡）", nameof(CapacityPointDraft.StationForwardGradeOutPermille), point.StationForwardGradeOutPermille);
                AddStationDirectionFields(panel, "順行", point, forward: true);
                AddTextEditor(panel, "逆行進站坡度 ‰（正值上坡）", nameof(CapacityPointDraft.StationReverseGradeInPermille), point.StationReverseGradeInPermille);
                AddTextEditor(panel, "逆行離站坡度 ‰（正值上坡）", nameof(CapacityPointDraft.StationReverseGradeOutPermille), point.StationReverseGradeOutPermille);
                AddStationDirectionFields(panel, "逆行", point, forward: false);
                break;
            case SpatialReferencePointKind.Junction:
                AddTextEditor(panel, "主線坡度 ‰（正值上坡）", nameof(CapacityPointDraft.MainlineGradePermille), point.MainlineGradePermille);
                AddTextEditor(panel, "側線坡度 ‰（正值上坡）", nameof(CapacityPointDraft.BranchlineGradePermille), point.BranchlineGradePermille);
                AddTextEditor(panel, "橫渡線長度 (m)", nameof(CapacityPointDraft.CrossoverLengthMeters), point.CrossoverLengthMeters);
                AddTextEditor(panel, "道岔限速 (km/h)", nameof(CapacityPointDraft.SwitchSpeedLimitKmh), point.SwitchSpeedLimitKmh);
                AddTextEditor(panel, "主線接近巡航 (km/h)", nameof(CapacityPointDraft.MainlineApproachCruiseSpeedKmh), point.MainlineApproachCruiseSpeedKmh);
                AddTextEditor(panel, "側線接近巡航 (km/h)", nameof(CapacityPointDraft.BranchlineApproachCruiseSpeedKmh), point.BranchlineApproachCruiseSpeedKmh);
                AddTextEditor(panel, "主線安全係數", nameof(CapacityPointDraft.MainlineSafetyFactor), point.MainlineSafetyFactor);
                AddTextEditor(panel, "側線安全係數", nameof(CapacityPointDraft.BranchlineSafetyFactor), point.BranchlineSafetyFactor);
                AddTextEditor(panel, "主線通過比例", nameof(CapacityPointDraft.MainlineTrafficRatio), point.MainlineTrafficRatio);
                break;
            case SpatialReferencePointKind.BeforeStationTurnback:
                AddCheckEditor(panel, "交替停靠不同股道", nameof(CapacityPointDraft.AlternateBerthing), point.AlternateBerthing);
                AddTextEditor(panel, "主線／折返線坡度 ‰（正值上坡）", nameof(CapacityPointDraft.MainlineGradePermille), point.MainlineGradePermille);
                AddTextEditor(panel, "停車點至橫渡線 (m)", nameof(CapacityPointDraft.DistanceFromStopToCrossoverMeters), point.DistanceFromStopToCrossoverMeters);
                AddTextEditor(panel, "橫渡線長度 (m)", nameof(CapacityPointDraft.CrossoverLengthMeters), point.CrossoverLengthMeters);
                AddTextEditor(panel, "折返／尾軌停等 (s)", nameof(CapacityPointDraft.TurnbackDwellSeconds), point.TurnbackDwellSeconds);
                AddTextEditor(panel, "道岔限速 (km/h)", nameof(CapacityPointDraft.SwitchSpeedLimitKmh), point.SwitchSpeedLimitKmh);
                break;
            case SpatialReferencePointKind.AfterStationTurnback:
                AddCheckEditor(panel, "交替停靠不同股道", nameof(CapacityPointDraft.AlternateBerthing), point.AlternateBerthing);
                AddTextEditor(panel, "主線／折返線坡度 ‰（正值上坡）", nameof(CapacityPointDraft.MainlineGradePermille), point.MainlineGradePermille);
                AddTextEditor(panel, "停車點至橫渡線 (m)", nameof(CapacityPointDraft.DistanceFromStopToCrossoverMeters), point.DistanceFromStopToCrossoverMeters);
                AddTextEditor(panel, "橫渡線長度 (m)", nameof(CapacityPointDraft.CrossoverLengthMeters), point.CrossoverLengthMeters);
                AddTextEditor(panel, "橫渡線至停車區 (m)", nameof(CapacityPointDraft.DistanceFromCrossoverToTurnbackStopMeters), point.DistanceFromCrossoverToTurnbackStopMeters);
                AddTextEditor(panel, "折返／尾軌停等 (s)", nameof(CapacityPointDraft.TurnbackDwellSeconds), point.TurnbackDwellSeconds);
                AddTextEditor(panel, "道岔限速 (km/h)", nameof(CapacityPointDraft.SwitchSpeedLimitKmh), point.SwitchSpeedLimitKmh);
                break;
            case SpatialReferencePointKind.CentralSidingTurnback:
                AddTextEditor(panel, "主線／折返線坡度 ‰（正值上坡）", nameof(CapacityPointDraft.MainlineGradePermille), point.MainlineGradePermille);
                AddTextEditor(panel, "停車點至橫渡線 (m)", nameof(CapacityPointDraft.DistanceFromStopToCrossoverMeters), point.DistanceFromStopToCrossoverMeters);
                AddTextEditor(panel, "橫渡線長度 (m)", nameof(CapacityPointDraft.CrossoverLengthMeters), point.CrossoverLengthMeters);
                AddTextEditor(panel, "橫渡線至停車區 (m)", nameof(CapacityPointDraft.DistanceFromCrossoverToTurnbackStopMeters), point.DistanceFromCrossoverToTurnbackStopMeters);
                AddTextEditor(panel, "折返／尾軌停等 (s)", nameof(CapacityPointDraft.TurnbackDwellSeconds), point.TurnbackDwellSeconds);
                AddTextEditor(panel, "道岔限速 (km/h)", nameof(CapacityPointDraft.SwitchSpeedLimitKmh), point.SwitchSpeedLimitKmh);
                break;
        }
    }

    private void AddStationDirectionFields(StackPanel panel, string direction, CapacityPointDraft point, bool forward)
    {
        var prefix = forward ? "StationForward" : "StationReverse";
        AddTextEditor(panel, $"{direction}停車位置至號誌 (m)", prefix + "DistanceToSignalMeters", GetNumber(point, prefix + "DistanceToSignalMeters"));
        AddTextEditor(panel, $"{direction}安全重疊區 (m)", prefix + "OverlapMeters", GetNumber(point, prefix + "OverlapMeters"));
        AddTextEditor(panel, $"{direction}停站時間 (s)", prefix + "DwellSeconds", GetNumber(point, prefix + "DwellSeconds"));
        AddTextEditor(panel, $"{direction}先行列車離站巡航 (km/h)", prefix + "EarlierCruiseSpeedKmh", GetNumber(point, prefix + "EarlierCruiseSpeedKmh"));
        AddTextEditor(panel, $"{direction}續行列車進站巡航 (km/h)", prefix + "LaterCruiseSpeedKmh", GetNumber(point, prefix + "LaterCruiseSpeedKmh"));
        AddTextEditor(panel, $"{direction}安全係數", prefix + "SafetyFactor", GetNumber(point, prefix + "SafetyFactor"));
    }

    private static object? GetNumber(CapacityPointDraft point, string propertyName) =>
        point.GetType().GetProperty(propertyName)?.GetValue(point);

    private void AddNumericEditor(StackPanel panel, IDictionary<string, TextBox> target, string label, string key, object value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var text = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", Width = 110 };
        target[key] = text;
        row.Children.Add(new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        panel.Children.Add(row);
    }

    private void AddTextEditor(StackPanel panel, string label, string key, object? value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var text = new TextBox { Text = FormatEditorValue(value), Width = 170 };
        activePointEditors[key] = text;
        row.Children.Add(new TextBlock { Text = label, Width = 250, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);
        panel.Children.Add(row);
    }

    private void AddCheckEditor(StackPanel panel, string label, string key, bool value)
    {
        var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        activePointEditors[key] = check;
        panel.Children.Add(check);
    }

    private static string FormatEditorValue(object? value) => value switch
    {
        null => "",
        double number => number.ToString("G", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static Border EditorGroup(string title, UIElement content, double width) => new()
    {
        Width = width,
        Margin = new Thickness(0, 0, 10, 0),
        BorderBrush = UiTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Child = new GroupBox { Header = title, Content = content }
    };

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (TryApplyDraft()) DialogResult = true;
    }

    private bool TryApplyDraft()
    {
        try
        {
            applying = true;
            var settings = BuildSettings();
            SpatialCapacityProjectAnalysis.Validate(settings);
            Result = document with { SpatialCapacity = settings };
            return true;
        }
        catch (SimulationValidationException exception)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, exception.Errors), "容量設定有誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        catch (FormatException exception)
        {
            MessageBox.Show(this, exception.Message, "容量設定有誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            applying = false;
        }
    }

    private SpatialCapacityProjectSettings BuildSettings()
    {
        CommitActivePoint(true);
        var global = new SpatialCapacityGlobalParameters(
            ReadDouble(globalEditors, nameof(SpatialCapacityGlobalParameters.InterchangeTimeSeconds), "號誌／轉換時間"),
            ReadDouble(globalEditors, nameof(SpatialCapacityGlobalParameters.ReactionTimeSeconds), "反應時間"),
            ReadDouble(globalEditors, nameof(SpatialCapacityGlobalParameters.TrainPeakHourFactor), "列車尖峰小時因子"),
            ReadDouble(globalEditors, nameof(SpatialCapacityGlobalParameters.MarginRate), "營運餘裕率"));
        var train = new SpatialCapacityTrainParameters(
            ReadDouble(trainEditors, nameof(SpatialCapacityTrainParameters.LengthMeters), "列車長度"),
            ReadDouble(trainEditors, nameof(SpatialCapacityTrainParameters.AccelerationMetersPerSecondSquared), "加速度"),
            ReadDouble(trainEditors, nameof(SpatialCapacityTrainParameters.DecelerationMetersPerSecondSquared), "減速度"),
            ReadDouble(trainEditors, nameof(SpatialCapacityTrainParameters.BrakeEffect), "煞車有效因子"),
            ReadInt(trainEditors, nameof(SpatialCapacityTrainParameters.SeatCount), "座位數"),
            ReadDouble(trainEditors, nameof(SpatialCapacityTrainParameters.StandingAreaSquareMeters), "站立面積"),
            ReadInt(trainEditors, nameof(SpatialCapacityTrainParameters.StandingDensityPerSquareMeter), "站立密度"));
        var mode = modeComboBox.SelectedValue is SpatialCapacityCalculationMode selected
            ? selected
            : SpatialCapacityCalculationMode.TaiwanHandbook;
        return new SpatialCapacityProjectSettings(
            global,
            train,
            pointGroups.Values.SelectMany(group => group).Select(item => item.ToDomain()).ToArray(),
            mode,
            compareAsScenarioCheckBox.IsChecked == true);
    }

    private bool CommitActivePoint(bool strict)
    {
        if (activePoint is null) return true;
        foreach (var (key, editor) in activePointEditors)
        {
            var property = activePoint.GetType().GetProperty(key, BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException($"容量參考點欄位不存在：{key}");
            try
            {
                switch (editor)
                {
                    case TextBox text when property.PropertyType == typeof(string):
                        property.SetValue(activePoint, text.Text.Trim());
                        text.ToolTip = null;
                        break;
                    case TextBox text when property.PropertyType == typeof(double):
                        property.SetValue(activePoint, ParseDouble(text.Text, key));
                        text.ToolTip = null;
                        break;
                    case CheckBox check when property.PropertyType == typeof(bool):
                        property.SetValue(activePoint, check.IsChecked == true);
                        break;
                }
            }
            catch (FormatException exception) when (!strict)
            {
                // 交易草稿與目前控制項都保持原樣；呼叫端會復原選取項，
                // 因而不能在切換點、分頁或新增／刪除時默默丟掉輸入。
                if (editor is TextBox text)
                {
                    text.ToolTip = exception.Message;
                    text.Focus();
                }
                return false;
            }
        }
        activePointList?.Items.Refresh();
        return true;
    }

    private static double ParseDouble(string text, string field)
    {
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
             && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            || !double.IsFinite(value))
            throw new FormatException($"「{field}」必須是有限數值。 ");
        return value;
    }

    private static double ReadDouble(IReadOnlyDictionary<string, TextBox> editors, string key, string label) =>
        ParseDouble(editors[key].Text, label);

    private static int ReadInt(IReadOnlyDictionary<string, TextBox> editors, string key, string label)
    {
        if (!int.TryParse(editors[key].Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            && !int.TryParse(editors[key].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            throw new FormatException($"「{label}」必須是整數。 ");
        return value;
    }

    private sealed record ModeChoice(SpatialCapacityCalculationMode Value, string Label);

    private sealed class CapacityPointDraft
    {
        public string ReferencePointId { get; set; } = "";
        public string StationId { get; set; } = "";
        public string Name { get; set; } = "";
        public SpatialReferencePointKind Kind { get; init; }
        public bool AlternateBerthing { get; set; }
        public double MainlineGradePermille { get; set; }
        public double BranchlineGradePermille { get; set; }
        public double DistanceFromStopToCrossoverMeters { get; set; } = 100;
        public double CrossoverLengthMeters { get; set; } = 100;
        public double DistanceFromCrossoverToTurnbackStopMeters { get; set; } = 100;
        public double TurnbackDwellSeconds { get; set; } = 30;
        public double SwitchSpeedLimitKmh { get; set; } = 40;
        public double MainlineApproachCruiseSpeedKmh { get; set; } = 80;
        public double BranchlineApproachCruiseSpeedKmh { get; set; } = 80;
        public double MainlineSafetyFactor { get; set; } = 1.5;
        public double BranchlineSafetyFactor { get; set; } = 1.5;
        public double MainlineTrafficRatio { get; set; } = 0.5;
        public double StationForwardGradeInPermille { get; set; }
        public double StationForwardGradeOutPermille { get; set; }
        public double StationForwardDistanceToSignalMeters { get; set; } = 15;
        public double StationForwardOverlapMeters { get; set; } = 300;
        public double StationForwardDwellSeconds { get; set; } = 30;
        public double StationForwardEarlierCruiseSpeedKmh { get; set; } = 80;
        public double StationForwardLaterCruiseSpeedKmh { get; set; } = 80;
        public double StationForwardSafetyFactor { get; set; } = 1.5;
        public double StationReverseGradeInPermille { get; set; }
        public double StationReverseGradeOutPermille { get; set; }
        public double StationReverseDistanceToSignalMeters { get; set; } = 15;
        public double StationReverseOverlapMeters { get; set; } = 300;
        public double StationReverseDwellSeconds { get; set; } = 30;
        public double StationReverseEarlierCruiseSpeedKmh { get; set; } = 80;
        public double StationReverseLaterCruiseSpeedKmh { get; set; } = 80;
        public double StationReverseSafetyFactor { get; set; } = 1.5;

        public string Display => $"{Name} · {ReferencePointId} · {StationId}";

        public static CapacityPointDraft Create(SpatialReferencePointKind kind, int number) => new()
        {
            Kind = kind,
            ReferencePointId = $"CAP-{number:000}",
            StationId = $"手動位置-{number:000}",
            Name = MainWindow.SpatialCapacityKindText(kind)
        };

        public static CapacityPointDraft FromDomain(ProjectSpatialReferencePoint source) => new()
        {
            ReferencePointId = source.ReferencePointId,
            StationId = source.StationId,
            Name = source.Name,
            Kind = source.Kind,
            AlternateBerthing = source.AlternateBerthing,
            MainlineGradePermille = source.MainlineGradePermille,
            BranchlineGradePermille = source.BranchlineGradePermille,
            DistanceFromStopToCrossoverMeters = source.DistanceFromStopToCrossoverMeters,
            CrossoverLengthMeters = source.CrossoverLengthMeters,
            DistanceFromCrossoverToTurnbackStopMeters = source.DistanceFromCrossoverToTurnbackStopMeters,
            TurnbackDwellSeconds = source.TurnbackDwellSeconds,
            SwitchSpeedLimitKmh = source.SwitchSpeedLimitMetersPerSecond * 3.6,
            MainlineApproachCruiseSpeedKmh = source.MainlineApproachCruiseSpeedMetersPerSecond * 3.6,
            BranchlineApproachCruiseSpeedKmh = source.BranchlineApproachCruiseSpeedMetersPerSecond * 3.6,
            MainlineSafetyFactor = source.MainlineSafetyFactor,
            BranchlineSafetyFactor = source.BranchlineSafetyFactor,
            MainlineTrafficRatio = source.MainlineTrafficRatio,
            StationForwardGradeInPermille = source.StationForwardGradeInPermille,
            StationForwardGradeOutPermille = source.StationForwardGradeOutPermille,
            StationForwardDistanceToSignalMeters = source.StationForwardDistanceToSignalMeters,
            StationForwardOverlapMeters = source.StationForwardOverlapMeters,
            StationForwardDwellSeconds = source.StationForwardDwellSeconds,
            StationForwardEarlierCruiseSpeedKmh = source.StationForwardEarlierCruiseSpeedMetersPerSecond * 3.6,
            StationForwardLaterCruiseSpeedKmh = source.StationForwardLaterCruiseSpeedMetersPerSecond * 3.6,
            StationForwardSafetyFactor = source.StationForwardSafetyFactor,
            StationReverseGradeInPermille = source.StationReverseGradeInPermille,
            StationReverseGradeOutPermille = source.StationReverseGradeOutPermille,
            StationReverseDistanceToSignalMeters = source.StationReverseDistanceToSignalMeters,
            StationReverseOverlapMeters = source.StationReverseOverlapMeters,
            StationReverseDwellSeconds = source.StationReverseDwellSeconds,
            StationReverseEarlierCruiseSpeedKmh = source.StationReverseEarlierCruiseSpeedMetersPerSecond * 3.6,
            StationReverseLaterCruiseSpeedKmh = source.StationReverseLaterCruiseSpeedMetersPerSecond * 3.6,
            StationReverseSafetyFactor = source.StationReverseSafetyFactor
        };

        public ProjectSpatialReferencePoint ToDomain() => new(
            ReferencePointId.Trim(), StationId.Trim(), Name.Trim(), Kind,
            AlternateBerthing, MainlineGradePermille, BranchlineGradePermille,
            DistanceFromStopToCrossoverMeters, CrossoverLengthMeters,
            DistanceFromCrossoverToTurnbackStopMeters, TurnbackDwellSeconds,
            SwitchSpeedLimitKmh / 3.6, MainlineApproachCruiseSpeedKmh / 3.6,
            BranchlineApproachCruiseSpeedKmh / 3.6, MainlineSafetyFactor,
            BranchlineSafetyFactor, MainlineTrafficRatio,
            StationForwardGradeInPermille, StationForwardGradeOutPermille,
            StationForwardDistanceToSignalMeters, StationForwardOverlapMeters,
            StationForwardDwellSeconds, StationForwardEarlierCruiseSpeedKmh / 3.6,
            StationForwardLaterCruiseSpeedKmh / 3.6, StationForwardSafetyFactor,
            StationReverseGradeInPermille, StationReverseGradeOutPermille,
            StationReverseDistanceToSignalMeters, StationReverseOverlapMeters,
            StationReverseDwellSeconds, StationReverseEarlierCruiseSpeedKmh / 3.6,
            StationReverseLaterCruiseSpeedKmh / 3.6, StationReverseSafetyFactor);
    }
}
