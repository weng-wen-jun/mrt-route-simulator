using System.Windows;

namespace MrtRouteSimulator.App;

/// <summary>導覽列與膠囊切換鈕顯示用的短標籤與圖示；TabItem.Header 仍保留完整頁名。</summary>
public static class ShellNav
{
    public static readonly DependencyProperty ShortLabelProperty = DependencyProperty.RegisterAttached(
        "ShortLabel", typeof(string), typeof(ShellNav), new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(ShellNav), new FrameworkPropertyMetadata(string.Empty));

    public static string GetShortLabel(DependencyObject element) => (string)element.GetValue(ShortLabelProperty);

    public static void SetShortLabel(DependencyObject element, string value) => element.SetValue(ShortLabelProperty, value);

    public static string GetIcon(DependencyObject element) => (string)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string value) => element.SetValue(IconProperty, value);
}
