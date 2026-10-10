using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 模擬設定工作頁。這個 partial 保留舊的 Ask 編輯方法作為相容入口，新的工作區則
/// 將同一組 Schema 8 欄位放在頁面表單中，避免連續跳出小對話框造成資料遺失。
/// </summary>
internal sealed partial class TopologyEditorWindow
{
    private readonly Dictionary<string, TextBox> simulationSettingsInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Display, double Value)> originalSpeedSettings = new(StringComparer.Ordinal);
    private ComboBox? simulationProfileCombo;
    private ComboBox? simulationMovingBlockCombo;
    private ComboBox? simulationBrakingCombo;

    private void ShowSimulationPage()
    {
        CommitTableDrafts();
        simulationSettingsInputs.Clear();
        originalSpeedSettings.Clear();

        var settings = state.Draft.Simulation;
        var train = state.Draft.Train;
        var operations = state.Draft.Operations;
        var panel = NewPage("模擬設定", "在同一頁調整起始時鐘、控制模式與安全參數；按套用後才送入主視窗。模擬引擎每 0.1 秒更新一次，播放倍率只影響畫面播放速度。 ");

        panel.Children.Add(SummaryGrid(
            ("執行引擎", UiDisplayText.Enum(settings.EngineKind)),
            ("更新頻率", "每 0.1 秒更新一次"),
            ("套用狀態", "目前為工作區草稿，按套用後才送入主視窗")));

        var basic = new GroupBox { Header = "常用設定", Margin = new Thickness(0, 12, 0, 8), Padding = new Thickness(10) };
        var basicGrid = CreateSettingsGrid();
        AddSettingField(basicGrid, "起始時鐘（秒）", "StartClockSeconds", settings.StartClockSeconds);
        AddSettingField(basicGrid, "播放速度", "PlaybackSpeed", settings.PlaybackSpeed);
        basic.Content = basicGrid;
        panel.Children.Add(basic);

        var advanced = new Expander
        {
            Header = "進階：全域基準與摘要（不改派車）",
            IsExpanded = false,
            Margin = new Thickness(0, 4, 0, 8)
        };
        var advancedGrid = CreateSettingsGrid();
        AddSettingField(advancedGrid, "存檔列車數摘要", "TrainCount", settings.TrainCount);
        AddSettingField(advancedGrid, "指定班距摘要（秒，可留空）", "HeadwaySeconds", settings.HeadwaySeconds);
        AddSpeedSetting(advancedGrid, "最高速度（km/h）", "Train.MaxSpeedMetersPerSecondKmh", train.MaxSpeedMetersPerSecond);
        AddSettingField(advancedGrid, "加速度（m/s²）", "Train.AccelerationMetersPerSecondSquared", train.AccelerationMetersPerSecondSquared);
        AddSettingField(advancedGrid, "基準減速度（m/s²）", "Train.DecelerationMetersPerSecondSquared", train.DecelerationMetersPerSecondSquared);
        AddSettingField(advancedGrid, "預設停站（秒）", "Train.DefaultDwellTimeSeconds", train.DefaultDwellTimeSeconds);
        AddSettingField(advancedGrid, "起點折返（秒）", "Train.OriginTurnaroundTimeSeconds", train.OriginTurnaroundTimeSeconds);
        AddSettingField(advancedGrid, "終點折返（秒）", "Train.TerminalTurnaroundTimeSeconds", train.TerminalTurnaroundTimeSeconds);
        advanced.Content = advancedGrid;
        panel.Children.Add(advanced);

        var control = new GroupBox
        {
            Header = "行車控制與進站",
            Margin = new Thickness(0, 4, 0, 8)
        };
        var controlGrid = CreateSettingsGrid();
        simulationProfileCombo = AddEnumSetting(controlGrid, "營運方式", "ProfileMode", settings.ProfileMode);
        simulationMovingBlockCombo = AddEnumSetting(controlGrid, "列車間距控制", "MovingBlockMode", settings.MovingBlockMode);
        simulationBrakingCombo = AddEnumSetting(controlGrid, "煞車計算方式", "BrakingEstimationMode", settings.BrakingEstimationMode);
        AddSettingField(controlGrid, "加加速度（m/s³）", "Operations.JerkMetersPerSecondCubed", operations.JerkMetersPerSecondCubed);
        AddSettingField(controlGrid, "惰行比例", "Operations.CoastingRatio", operations.CoastingRatio);
        AddSettingField(controlGrid, "進站控制距離（m）", "Operations.ApproachDistanceMeters", operations.ApproachDistanceMeters);
        AddSpeedSetting(controlGrid, "進站控制速度（km/h）", "Operations.ApproachSpeedMetersPerSecondKmh", operations.ApproachSpeedMetersPerSecond);
        AddSettingField(controlGrid, "牽引漸弱比例", "Operations.TractionFadeRatio", operations.TractionFadeRatio);
        control.Content = controlGrid;
        panel.Children.Add(control);

        var safety = new GroupBox
        {
            Header = "列車安全設定",
            Margin = new Thickness(0, 4, 0, 8)
        };
        var safetyGrid = CreateSettingsGrid();
        AddSettingField(safetyGrid, "列車長度（m）", "Operations.TrainLengthMeters", operations.TrainLengthMeters);
        AddSettingField(safetyGrid, "營運煞車減速度（m/s²）", "Operations.ServiceBrakingMetersPerSecondSquared", operations.ServiceBrakingMetersPerSecondSquared);
        AddSettingField(safetyGrid, "緊急煞車減速度（m/s²）", "Operations.EmergencyBrakingMetersPerSecondSquared", operations.EmergencyBrakingMetersPerSecondSquared);
        AddSettingField(safetyGrid, "控制反應時間（秒）", "Operations.ControlReactionTimeSeconds", operations.ControlReactionTimeSeconds);
        AddSettingField(safetyGrid, "煞車建立時間（秒）", "Operations.BrakeBuildUpTimeSeconds", operations.BrakeBuildUpTimeSeconds);
        AddSettingField(safetyGrid, "定位誤差（m）", "Operations.PositioningErrorMeters", operations.PositioningErrorMeters);
        AddSettingField(safetyGrid, "安全餘量（m）", "Operations.SafetyMarginMeters", operations.SafetyMarginMeters);
        AddSettingField(safetyGrid, "絕對最小間隔（m）", "Operations.AbsoluteMinimumGapMeters", operations.AbsoluteMinimumGapMeters);
        safety.Content = safetyGrid;
        panel.Children.Add(safety);
        panel.Children.Add(new TextBlock
        {
            Text = "車型頁是每種車輛性能的主要入口；這裡的車輛欄位是全域基準／相容預設值，未經確認不會自動覆寫車型或班次。列車數與班距摘要也保留在此，但不會取代班表頁的實際發車安排。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextMutedBrush,
            Margin = new Thickness(0, 4, 0, 0)
        });

        RegisterPageCommitHook("simulation-settings", CommitSimulationSettingsForm);
        workspace.Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = panel
        };
    }

    private Grid CreateSettingsGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private void AddSettingField(Grid grid, string label, string key, object? value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        var input = new TextBox
        {
            Text = value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            },
            Margin = new Thickness(0, 2, 0, 2),
            MinWidth = 170,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        input.ToolTip = $"儲存欄位：{key}";
        input.SetValue(AutomationProperties.NameProperty, label);
        grid.Children.Add(caption);
        grid.Children.Add(input);
        Grid.SetRow(caption, row);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        simulationSettingsInputs[key] = input;
    }

    private void AddSpeedSetting(Grid grid, string label, string key, double metersPerSecond)
    {
        AddSettingField(grid, label, key, metersPerSecond * 3.6);
        originalSpeedSettings[key] = (simulationSettingsInputs[key].Text, metersPerSecond);
    }

    private ComboBox AddEnumSetting<T>(Grid grid, string label, string key, T selected) where T : struct, Enum
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var caption = new TextBlock { Text = label, Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
        var input = new ComboBox { Margin = new Thickness(0, 2, 0, 2), MinWidth = 190 };
        input.ItemsSource = Enum.GetValues<T>().Select(value => new EnumOption<T>(value, UiDisplayText.Enum((Enum)(object)value))).ToArray();
        input.DisplayMemberPath = nameof(EnumOption<T>.Label);
        input.SelectedValuePath = nameof(EnumOption<T>.Value);
        input.SelectedValue = selected;
        input.ToolTip = $"儲存欄位：{key}";
        input.SetValue(AutomationProperties.NameProperty, label);
        grid.Children.Add(caption);
        grid.Children.Add(input);
        Grid.SetRow(caption, row);
        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        return input;
    }

    private void CommitSimulationSettingsForm()
    {
        // A page may be committed while the user is switching pages. Parse only the
        // page inputs here; full cross-reference validation remains the Apply gate.
        var train = state.Draft.Train with
        {
            MaxSpeedMetersPerSecond = ReadSpeedSetting("Train.MaxSpeedMetersPerSecondKmh", "最高速度"),
            AccelerationMetersPerSecondSquared = ReadSetting("Train.AccelerationMetersPerSecondSquared", "加速度"),
            DecelerationMetersPerSecondSquared = ReadSetting("Train.DecelerationMetersPerSecondSquared", "基準減速度"),
            DefaultDwellTimeSeconds = ReadSetting("Train.DefaultDwellTimeSeconds", "預設停站"),
            OriginTurnaroundTimeSeconds = ReadSetting("Train.OriginTurnaroundTimeSeconds", "起點折返"),
            TerminalTurnaroundTimeSeconds = ReadSetting("Train.TerminalTurnaroundTimeSeconds", "終點折返")
        };
        var operations = state.Draft.Operations with
        {
            JerkMetersPerSecondCubed = ReadSetting("Operations.JerkMetersPerSecondCubed", "加加速度"),
            CoastingRatio = ReadSetting("Operations.CoastingRatio", "惰行比例"),
            ApproachDistanceMeters = ReadSetting("Operations.ApproachDistanceMeters", "進站控制距離"),
            ApproachSpeedMetersPerSecond = ReadSpeedSetting("Operations.ApproachSpeedMetersPerSecondKmh", "進站控制速度"),
            TractionFadeRatio = ReadSetting("Operations.TractionFadeRatio", "牽引漸弱比例"),
            TrainLengthMeters = ReadSetting("Operations.TrainLengthMeters", "列車長度"),
            ServiceBrakingMetersPerSecondSquared = ReadSetting("Operations.ServiceBrakingMetersPerSecondSquared", "營運煞車減速度"),
            EmergencyBrakingMetersPerSecondSquared = ReadSetting("Operations.EmergencyBrakingMetersPerSecondSquared", "緊急煞車減速度"),
            ControlReactionTimeSeconds = ReadSetting("Operations.ControlReactionTimeSeconds", "控制反應時間"),
            BrakeBuildUpTimeSeconds = ReadSetting("Operations.BrakeBuildUpTimeSeconds", "煞車建立時間"),
            PositioningErrorMeters = ReadSetting("Operations.PositioningErrorMeters", "定位誤差"),
            SafetyMarginMeters = ReadSetting("Operations.SafetyMarginMeters", "安全餘量"),
            AbsoluteMinimumGapMeters = ReadSetting("Operations.AbsoluteMinimumGapMeters", "絕對最小間隔")
        };
        if (simulationProfileCombo?.SelectedValue is not OperationProfileMode profile
            || simulationMovingBlockCombo?.SelectedValue is not MovingBlockMode movingBlock
            || simulationBrakingCombo?.SelectedValue is not BrakingEstimationMode braking)
        {
            throw new SimulationValidationException(["模擬設定的列舉選項無效。"]);
        }
        var trainCount = ReadIntegerSetting("TrainCount", "存檔列車數摘要");
        if (trainCount < 1) throw new SimulationValidationException(["存檔列車數摘要必須是大於 0 的整數。"]);
        var headway = ReadNullableSetting("HeadwaySeconds", "指定班距");
        if (headway is <= 0) throw new SimulationValidationException(["指定班距必須大於 0 秒，或留空。"]);
        var simulation = state.Draft.Simulation with
        {
            StartClockSeconds = ReadSetting("StartClockSeconds", "起始時鐘"),
            PlaybackSpeed = ReadSetting("PlaybackSpeed", "播放倍率"),
            TrainCount = trainCount,
            HeadwaySeconds = headway,
            ProfileMode = profile,
            MovingBlockMode = movingBlock,
            BrakingEstimationMode = braking
        };
        state.Replace(state.Draft with { Train = train, Operations = operations, Simulation = simulation });
    }

    private double ReadSetting(string key, string label)
    {
        if (!simulationSettingsInputs.TryGetValue(key, out var input))
            throw new SimulationValidationException([$"模擬設定欄位「{label}」尚未建立，未套用變更。"]);
        return Parse(input.Text.Trim(), label);
    }

    private double ReadSpeedSetting(string key, string label)
    {
        if (originalSpeedSettings.TryGetValue(key, out var original)
            && simulationSettingsInputs.TryGetValue(key, out var input)
            && string.Equals(input.Text, original.Display, StringComparison.Ordinal))
            return original.Value;
        return ReadSetting(key, label) / 3.6;
    }

    private int ReadIntegerSetting(string key, string label)
    {
        if (!simulationSettingsInputs.TryGetValue(key, out var input)
            || !int.TryParse(input.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new SimulationValidationException([$"{label}必須是整數。"]);
        return value;
    }

    private double? ReadNullableSetting(string key, string label)
    {
        if (!simulationSettingsInputs.TryGetValue(key, out var input))
            throw new SimulationValidationException([$"模擬設定欄位「{label}」尚未建立，未套用變更。"]);
        if (string.IsNullOrWhiteSpace(input.Text)) return null;
        return Parse(input.Text.Trim(), label);
    }

    private sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum;
}
