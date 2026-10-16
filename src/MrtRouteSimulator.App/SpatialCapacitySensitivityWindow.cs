using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>以同一容量設定進行單一參數敏感度分析的交易式對話框。</summary>
internal sealed class SpatialCapacitySensitivityWindow : Window
{
    private readonly SpatialCapacityProjectSettings settings;
    private readonly ComboBox parameterComboBox = new();
    private readonly ComboBox pointComboBox = new();
    private readonly ComboBox directionComboBox = new();
    private readonly TextBox startTextBox = new();
    private readonly TextBox endTextBox = new();
    private readonly TextBox stepTextBox = new();
    private readonly TextBox targetHeadwayTextBox = new();
    private readonly TextBlock summaryText = new();
    private readonly DataGrid resultGrid = new();
    private readonly ObservableCollection<SpatialCapacitySensitivityResultRow> rows = [];
    private bool updatingParameter;

    public SpatialCapacitySensitivityWindow(SpatialCapacityProjectSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Title = "容量敏感度分析";
        Width = 900;
        Height = 700;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        EditorChrome.ApplyWindowChrome(this);
        Content = BuildContent();
    }

    public IReadOnlyList<SpatialCapacitySensitivityResultRow>? Result { get; private set; }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var note = new TextBlock
        {
            Text = "敏感度分析只改變選定參數，其他手動規劃參數維持原設定；結果由容量計算核心產生。中間站目前不提供敏感度分析。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextMutedBrush,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(note);

        var selectors = new WrapPanel { Orientation = Orientation.Horizontal };
        selectors.Children.Add(EditorGroup("參數", BuildParameterEditor(), 250));
        selectors.Children.Add(EditorGroup("參考點", BuildPointEditor(), 270));
        selectors.Children.Add(EditorGroup("方向", BuildDirectionEditor(), 150));

        var selectorsScroll = new ScrollViewer
        {
            Content = selectors,
            MaxHeight = 230,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(selectorsScroll, 1);
        root.Children.Add(selectorsScroll);

        var ranges = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        ranges.Children.Add(LabeledTextBox("起點", startTextBox, 100));
        ranges.Children.Add(LabeledTextBox("終點", endTextBox, 100));
        ranges.Children.Add(LabeledTextBox("步長", stepTextBox, 100));
        ranges.Children.Add(LabeledTextBox("目標班距 h（選填，秒）", targetHeadwayTextBox, 150));
        var run = new Button { Content = "執行分析", MinWidth = 92, Margin = new Thickness(12, 22, 0, 0), IsDefault = true };
        run.Click += Run_Click;
        ranges.Children.Add(run);
        Grid.SetRow(ranges, 2);
        root.Children.Add(ranges);

        var results = new DockPanel();
        summaryText.TextWrapping = TextWrapping.Wrap;
        summaryText.Foreground = UiTheme.TextMutedBrush;
        summaryText.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(summaryText, Dock.Top);
        results.Children.Add(summaryText);
        resultGrid.ItemsSource = rows;
        resultGrid.IsReadOnly = true;
        resultGrid.RowHeaderWidth = 0;
        resultGrid.BorderThickness = new Thickness(0);
        resultGrid.AutoGenerateColumns = false;
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "參數值", Binding = new System.Windows.Data.Binding(nameof(SpatialCapacitySensitivityResultRow.Value)), Width = 110 });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "安全時距 Ts (s)", Binding = new System.Windows.Data.Binding(nameof(SpatialCapacitySensitivityResultRow.SafetyHeadway)), Width = 125 });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "設計班距 h (s)", Binding = new System.Windows.Data.Binding(nameof(SpatialCapacitySensitivityResultRow.DesignHeadway)), Width = 125 });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "列／h", Binding = new System.Windows.Data.Binding(nameof(SpatialCapacitySensitivityResultRow.LineCapacity)), Width = 80 });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "設計乘位／h", Binding = new System.Windows.Data.Binding(nameof(SpatialCapacitySensitivityResultRow.DesignPassengerCapacity)), Width = 120 });
        resultGrid.Columns.Add(new DataGridTextColumn { Header = "可達成人／h", Binding = new System.Windows.Data.Binding(nameof(SpatialCapacitySensitivityResultRow.AchievablePassengerCapacity)), Width = 120 });
        results.Children.Add(new Border { Child = resultGrid, BorderBrush = UiTheme.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) });
        Grid.SetRow(results, 3);
        root.Children.Add(results);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var close = new Button { Content = "完成", MinWidth = 84, IsDefault = false };
        close.Click += (_, _) =>
        {
            if (Result is not null) DialogResult = true;
            else DialogResult = false;
        };
        var cancel = new Button { Content = "取消", MinWidth = 84, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(close);
        actions.Children.Add(cancel);
        Grid.SetRow(actions, 4);
        root.Children.Add(actions);
        return root;
    }

    private UIElement BuildParameterEditor()
    {
        parameterComboBox.ItemsSource = new[]
        {
            new ParameterChoice(SpatialCapacitySensitivityParameter.MainlineTrafficRatio, "主線連續通過比例"),
            new ParameterChoice(SpatialCapacitySensitivityParameter.TurnbackDwellSeconds, "折返／尾軌停靠時間")
        };
        parameterComboBox.DisplayMemberPath = nameof(ParameterChoice.Label);
        parameterComboBox.SelectedValuePath = nameof(ParameterChoice.Value);
        parameterComboBox.SelectedIndex = 0;
        parameterComboBox.MinWidth = 220;
        parameterComboBox.SelectionChanged += (_, _) =>
        {
            if (!updatingParameter) RefreshPointChoices();
        };
        AutomationProperties.SetName(parameterComboBox, "敏感度分析參數");
        return new StackPanel { Children = { EditorChrome.FieldLabel("適用參數"), parameterComboBox } };
    }

    private UIElement BuildPointEditor()
    {
        pointComboBox.DisplayMemberPath = nameof(PointChoice.Label);
        pointComboBox.SelectedValuePath = nameof(PointChoice.ReferencePointId);
        pointComboBox.MinWidth = 240;
        AutomationProperties.SetName(pointComboBox, "敏感度分析參考點");
        RefreshPointChoices();
        return new StackPanel { Children = { EditorChrome.FieldLabel("參考點"), pointComboBox } };
    }

    private UIElement BuildDirectionEditor()
    {
        directionComboBox.ItemsSource = new[]
        {
            new DirectionChoice(SpatialCapacityDirection.Forward, "順行"),
            new DirectionChoice(SpatialCapacityDirection.Reverse, "逆行")
        };
        directionComboBox.DisplayMemberPath = nameof(DirectionChoice.Label);
        directionComboBox.SelectedValuePath = nameof(DirectionChoice.Value);
        directionComboBox.SelectedIndex = 0;
        directionComboBox.MinWidth = 115;
        AutomationProperties.SetName(directionComboBox, "敏感度分析方向");
        return new StackPanel { Children = { EditorChrome.FieldLabel("方向"), directionComboBox } };
    }

    private void RefreshPointChoices()
    {
        if (parameterComboBox.SelectedValue is not SpatialCapacitySensitivityParameter parameter) return;
        var choices = settings.Points
            .Where(point => parameter == SpatialCapacitySensitivityParameter.MainlineTrafficRatio
                ? point.Kind == SpatialReferencePointKind.Junction
                : point.Kind is SpatialReferencePointKind.BeforeStationTurnback
                    or SpatialReferencePointKind.AfterStationTurnback
                    or SpatialReferencePointKind.CentralSidingTurnback)
            .Select(point => new PointChoice(point.ReferencePointId,
                $"{(string.IsNullOrWhiteSpace(point.Name) ? point.ReferencePointId : point.Name)} · {point.ReferencePointId}"))
            .ToArray();
        var previous = pointComboBox.SelectedValue as string;
        updatingParameter = true;
        pointComboBox.ItemsSource = choices;
        pointComboBox.SelectedValue = choices.Any(choice => choice.ReferencePointId.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ? previous
            : choices.FirstOrDefault()?.ReferencePointId;
        updatingParameter = false;

        if (parameter == SpatialCapacitySensitivityParameter.MainlineTrafficRatio)
        {
            startTextBox.Text = "0";
            endTextBox.Text = "1";
            stepTextBox.Text = "0.1";
        }
        else
        {
            startTextBox.Text = "10";
            endTextBox.Text = "120";
            stepTextBox.Text = "5";
        }
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (TryRunAnalysis()) return;
        MessageBox.Show(this, summaryText.Text, "敏感度設定有誤", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool TryRunAnalysis()
    {
        try
        {
            if (parameterComboBox.SelectedValue is not SpatialCapacitySensitivityParameter parameter)
                throw new FormatException("請選擇適用參數。");
            if (pointComboBox.SelectedValue is not string pointId || string.IsNullOrWhiteSpace(pointId))
                throw new FormatException("此參數沒有可用的容量參考點。");
            if (!TryReadFinite(startTextBox.Text, out var start))
            {
                summaryText.Text = "起點必須是有限數值。";
                return false;
            }
            if (!TryReadFinite(endTextBox.Text, out var end))
            {
                summaryText.Text = "終點必須是有限數值。";
                return false;
            }
            if (!TryReadFinite(stepTextBox.Text, out var step))
            {
                summaryText.Text = "步長必須是有限數值。";
                return false;
            }
            if (end < start || step <= 0)
                throw new FormatException("敏感度範圍須滿足終點大於等於起點，且步長大於 0。");
            double? target = null;
            if (!string.IsNullOrWhiteSpace(targetHeadwayTextBox.Text))
            {
                if (!TryReadFinite(targetHeadwayTextBox.Text, out var targetValue) || targetValue <= 0)
                {
                    summaryText.Text = "目標班距必須是大於 0 的有限數值。";
                    return false;
                }
                target = targetValue;
            }

            SpatialCapacityProjectAnalysis.Validate(settings);
            var direction = directionComboBox.SelectedValue is SpatialCapacityDirection selectedDirection
                ? selectedDirection
                : SpatialCapacityDirection.Forward;
            var source = SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(
                settings, pointId, parameter, start, end, step, direction);
            rows.Clear();
            foreach (var item in source)
            {
                rows.Add(new SpatialCapacitySensitivityResultRow(
                    FormatCapacityValue(item.Value),
                    FormatCapacityValue(item.Result.SafetyHeadwaySeconds),
                    FormatCapacityValue(item.Result.DesignHeadwaySeconds),
                    item.Result.LineCapacityPerHour.ToString("N0"),
                    item.Result.DesignPassengerCapacityPerHour.ToString("N0"),
                    item.Result.AchievablePassengerCapacityPerHour.ToString("N0")));
            }

            var targetRow = target is null
                ? null
                : source.FirstOrDefault(item => item.Result.DesignHeadwaySeconds <= target.Value);
            summaryText.Text = target is null
                ? $"已完成 {source.Count} 筆分析；表格顯示計算結果。"
                : targetRow is null
                    ? $"已完成 {source.Count} 筆分析；沒有達到目標班距 {FormatCapacityValue(target.Value)} 秒的參數值。"
                    : $"已完成 {source.Count} 筆分析；參數值 {FormatCapacityValue(targetRow.Value)} 首次達到目標班距 {FormatCapacityValue(target.Value)} 秒。";
            Result = rows.ToArray();
            return true;
        }
        catch (SimulationValidationException exception)
        {
            summaryText.Text = string.Join(Environment.NewLine, exception.Errors);
            return false;
        }
        catch (FormatException exception)
        {
            summaryText.Text = exception.Message;
            return false;
        }
    }

    private static bool TryReadFinite(string text, out double value)
    {
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
             && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            || !double.IsFinite(value))
        {
            value = 0;
            return false;
        }
        return true;
    }

    private static string FormatCapacityValue(double value) => value.ToString("0.###");

    private static Border EditorGroup(string title, UIElement content, double width) => new()
    {
        Width = width,
        Margin = new Thickness(0, 0, 10, 0),
        BorderBrush = UiTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Child = new GroupBox { Header = title, Content = content }
    };

    private static StackPanel LabeledTextBox(string label, TextBox textBox, double width)
    {
        textBox.Width = width;
        textBox.Margin = new Thickness(0, 3, 10, 0);
        textBox.VerticalContentAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Children =
            {
                EditorChrome.FieldLabel(label),
                textBox
            }
        };
    }

    private sealed record ParameterChoice(SpatialCapacitySensitivityParameter Value, string Label);
    private sealed record PointChoice(string ReferencePointId, string Label);
    private sealed record DirectionChoice(SpatialCapacityDirection Value, string Label);

    public sealed record SpatialCapacitySensitivityResultRow(
        string Value,
        string SafetyHeadway,
        string DesignHeadway,
        string LineCapacity,
        string DesignPassengerCapacity,
        string AchievablePassengerCapacity);
}
