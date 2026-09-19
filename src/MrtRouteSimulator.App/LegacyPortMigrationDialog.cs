using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 舊 Schema 8 檔案的接軌側別遷移引導。畫面只接受使用者明確輸入，
/// 不依 schematic position、畫面左右或 chainage 猜測 A/B。
/// </summary>
internal sealed class LegacyPortMigrationDialog : Window
{
    private readonly TopologyProjectDocument source;
    private readonly IReadOnlyList<LegacyPortMigrationItem> items;
    private readonly Dictionary<string, (ComboBox From, ComboBox To)> selectors = new(StringComparer.OrdinalIgnoreCase);

    public LegacyPortMigrationDialog(TopologyProjectDocument source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        items = LegacyPortMigration.FindUnspecifiedEdges(source);
        if (items.Count == 0)
            throw new ArgumentException("文件沒有需要接軌側別遷移的 edge。", nameof(source));

        Title = "舊檔接軌側別遷移";
        Width = 780;
        Height = Math.Min(760, 210 + items.Count * 42);
        MinWidth = 680;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
    }

    public TopologyProjectDocument? Result { get; private set; }

    public bool KeptCompatibility { get; private set; }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(18), Background = Brushes.White };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "此 Schema 8 舊檔有軌道兩端皆未指定實體接軌側別。A/B 只能由實體資料確認，程式不會從示意位置或里程猜測。請逐一填寫，或選擇保留相容讀取。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(60, 70, 90)),
            Margin = new Thickness(0, 0, 0, 14)
        });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        AddCell(table, "軌道區段", 0, 0, true);
        AddCell(table, "端點", 1, 0, true);
        AddCell(table, "起點側", 2, 0, true);
        AddCell(table, "終點側", 3, 0, true);

        for (var index = 0; index < items.Count; index++)
        {
            var row = index + 1;
            var item = items[index];
            AddCell(table, item.TrackEdgeId, 0, row);
            AddCell(table, $"{item.FromNodeId} → {item.ToNodeId}", 1, row);
            var from = SideSelector();
            var to = SideSelector();
            selectors[item.TrackEdgeId] = (from, to);
            AddCell(table, from, 2, row);
            AddCell(table, to, 3, row);
        }

        scroll.Content = table;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        actions.Children.Add(Button("套用明確側別", ApplyMigration, false));
        actions.Children.Add(Button("保留相容讀取", KeepLegacyCompatibility, true));
        actions.Children.Add(Button("取消", (_, _) => DialogResult = false, true));
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private void ApplyMigration(object? sender, RoutedEventArgs args)
    {
        var assignments = new Dictionary<string, (TrackPortSide From, TrackPortSide To)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var selector = selectors[item.TrackEdgeId];
            if (selector.From.SelectedItem is not TrackPortSide from || selector.To.SelectedItem is not TrackPortSide to)
            {
                MessageBox.Show(this, $"請先確認 edge「{item.TrackEdgeId}」的起點與終點側別。", "接軌側別尚未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            assignments[item.TrackEdgeId] = (from, to);
        }

        try
        {
            Result = LegacyPortMigration.ApplyExplicitSides(source, assignments);
            KeptCompatibility = false;
            DialogResult = true;
        }
        catch (SimulationValidationException exception)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, exception.Errors), "接軌側別遷移未通過", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void KeepLegacyCompatibility(object? sender, RoutedEventArgs args)
    {
        KeptCompatibility = true;
        Result = source;
        DialogResult = true;
    }

    private static ComboBox SideSelector() => new()
    {
        ItemsSource = Enum.GetValues<TrackPortSide>(),
        MinWidth = 110,
        Margin = new Thickness(4, 2, 4, 2),
        ToolTip = "請依實體建置資料選擇 A 或 B；不可依圖面位置猜測。"
    };

    private static Button Button(string text, RoutedEventHandler handler, bool secondary)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 118
        };
        if (secondary) button.Background = new SolidColorBrush(Color.FromRgb(238, 242, 248));
        button.Click += handler;
        return button;
    }

    private static void AddCell(Grid grid, object content, int column, int row, bool header = false)
    {
        var element = content as UIElement ?? new TextBlock { Text = content.ToString() ?? "" };
        if (element is TextBlock text)
        {
            text.Margin = new Thickness(6, 6, 6, 6);
            text.TextWrapping = TextWrapping.Wrap;
            if (header) text.FontWeight = FontWeights.SemiBold;
        }
        Grid.SetColumn(element, column);
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }
}
