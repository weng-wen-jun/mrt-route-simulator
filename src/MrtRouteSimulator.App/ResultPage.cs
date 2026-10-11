using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

/// <summary>
/// 結果分頁的共用骨架：頁首（標題、說明、動作）、摘要、篩選卡、內容與附註。
/// 除 Content 外的各位置以邏輯子元素掛載，讓 FindName、資源與字型繼承照舊。
/// </summary>
public sealed class ResultPage : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ResultPage), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = RegisterSlot(nameof(Description));
    public static readonly DependencyProperty SummaryProperty = RegisterSlot(nameof(Summary));
    public static readonly DependencyProperty FiltersProperty = RegisterSlot(nameof(Filters));
    public static readonly DependencyProperty FilterActionsProperty = RegisterSlot(nameof(FilterActions));
    public static readonly DependencyProperty ActionsProperty = RegisterSlot(nameof(Actions));
    public static readonly DependencyProperty FootnoteProperty = RegisterSlot(nameof(Footnote));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Summary
    {
        get => GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public object? Filters
    {
        get => GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }

    public object? FilterActions
    {
        get => GetValue(FilterActionsProperty);
        set => SetValue(FilterActionsProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public object? Footnote
    {
        get => GetValue(FootnoteProperty);
        set => SetValue(FootnoteProperty, value);
    }

    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = new List<object>();
            var baseChildren = base.LogicalChildren;
            while (baseChildren?.MoveNext() == true) children.Add(baseChildren.Current);
            foreach (var slot in new[] { Description, Summary, Filters, FilterActions, Actions, Footnote })
                if (slot is not null) children.Add(slot);
            return children.GetEnumerator();
        }
    }

    // 右側群組並排時，與篩選之間的間距：外距 10 ＋分隔線 1 ＋內距 10。
    private const double SideBySideGap = 21;

    private Grid? _filterGrid;
    private Border? _filterActionsHost;
    private double _filterActionsWidth = double.NaN;
    private double _widestFilterWidth = double.NaN;

    public override void OnApplyTemplate()
    {
        if (_filterGrid is not null) _filterGrid.SizeChanged -= OnFilterGridSizeChanged;
        base.OnApplyTemplate();
        _filterGrid = GetTemplateChild("PART_FilterGrid") as Grid;
        _filterActionsHost = GetTemplateChild("FilterActionsHost") as Border;
        if (_filterGrid is not null) _filterGrid.SizeChanged += OnFilterGridSizeChanged;
    }

    private void OnFilterGridSizeChanged(object sender, SizeChangedEventArgs e) => UpdateFilterActionsPlacement();

    // 篩選卡放不下「最寬一組篩選＋右側群組」時，右側群組改排到篩選下方，避免控制項被版面裁切。
    private void UpdateFilterActionsPlacement()
    {
        if (_filterGrid is null || _filterActionsHost is null || FilterActions is not FrameworkElement actions) return;
        if (double.IsNaN(_filterActionsWidth)) MeasureNaturalWidths(actions);
        var stacked = _filterGrid.ActualWidth < _filterActionsWidth + SideBySideGap + _widestFilterWidth;
        if (stacked == (Grid.GetRow(_filterActionsHost) == 1)) return;
        Grid.SetRow(_filterActionsHost, stacked ? 1 : 0);
        Grid.SetColumn(_filterActionsHost, stacked ? 0 : 1);
        Grid.SetColumnSpan(_filterActionsHost, stacked ? 2 : 1);
        _filterActionsHost.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        _filterActionsHost.Padding = stacked ? new Thickness(0, 4, 0, 0) : new Thickness(10, 0, 0, 0);
        _filterActionsHost.Margin = stacked ? new Thickness(0, 4, 0, 0) : new Thickness(10, 0, 0, 0);
    }

    // 只量一次自然寬度（內容在載入後不變）；量完讓原本的父層依實際寬度重新量測。
    private void MeasureNaturalWidths(FrameworkElement actions)
    {
        _filterActionsWidth = NaturalWidth(actions);
        _widestFilterWidth = Filters switch
        {
            Panel panel => panel.Children.OfType<UIElement>().Select(NaturalWidth).DefaultIfEmpty(0).Max(),
            UIElement element => NaturalWidth(element),
            _ => 0
        };
        (Filters as UIElement)?.InvalidateMeasure();
        actions.InvalidateMeasure();
        (System.Windows.Media.VisualTreeHelper.GetParent(actions) as UIElement)?.InvalidateMeasure();
    }

    private static double NaturalWidth(UIElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize.Width;
    }

    private static DependencyProperty RegisterSlot(string name) => DependencyProperty.Register(
        name, typeof(object), typeof(ResultPage), new FrameworkPropertyMetadata(null, OnSlotChanged));

    private static void OnSlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (ResultPage)d;
        if (e.OldValue is not null) page.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) page.AddLogicalChild(e.NewValue);
    }
}
