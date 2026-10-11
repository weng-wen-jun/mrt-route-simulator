using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace MrtRouteSimulator.App;

/// <summary>
/// 主視窗外殼的頁面切換控制項。仍是 TabControl（既有切換程式、MCP 與測試照用），
/// 另以 PageHeader 承載所有頁面共用的頁首、以 NavFooter 承載導覽列底部按鈕。
/// </summary>
public sealed class ShellTabControl : TabControl
{
    public static readonly DependencyProperty PageHeaderProperty = DependencyProperty.Register(
        nameof(PageHeader), typeof(object), typeof(ShellTabControl),
        new FrameworkPropertyMetadata(null, OnLogicalContentChanged));

    public static readonly DependencyProperty NavFooterProperty = DependencyProperty.Register(
        nameof(NavFooter), typeof(object), typeof(ShellTabControl),
        new FrameworkPropertyMetadata(null, OnLogicalContentChanged));

    public object? PageHeader
    {
        get => GetValue(PageHeaderProperty);
        set => SetValue(PageHeaderProperty, value);
    }

    public object? NavFooter
    {
        get => GetValue(NavFooterProperty);
        set => SetValue(NavFooterProperty, value);
    }

    // 讓頁首與導覽列底部元件成為邏輯子元素，以繼承字型、資源與 DataContext。
    protected override IEnumerator LogicalChildren
    {
        get
        {
            var children = new List<object>();
            var baseChildren = base.LogicalChildren;
            while (baseChildren?.MoveNext() == true) children.Add(baseChildren.Current);
            if (PageHeader is not null) children.Add(PageHeader);
            if (NavFooter is not null) children.Add(NavFooter);
            return children.GetEnumerator();
        }
    }

    private static void OnLogicalContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ShellTabControl)d;
        if (e.OldValue is not null) control.RemoveLogicalChild(e.OldValue);
        if (e.NewValue is not null) control.AddLogicalChild(e.NewValue);
    }
}
