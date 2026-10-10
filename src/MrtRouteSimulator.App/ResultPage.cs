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

    private static DependencyProperty RegisterSlot(string name) => DependencyProperty.Register(
        name, typeof(object), typeof(ResultPage), new FrameworkPropertyMetadata(null, OnSlotChanged));

    private static void OnSlotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = (ResultPage)d;
        if (e.OldValue is not null) page.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) page.AddLogicalChild(e.NewValue);
    }
}
