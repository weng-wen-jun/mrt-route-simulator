using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 拓樸編輯器與其對話框的共用元件。外觀全部來自 Themes/Controls.xaml 的樣式與 UiTheme 畫筆；
/// <see cref="ApplyWindowChrome"/> 只在該視窗的資源中把分段標籤、清單、群組框、進度條登記為隱含樣式，
/// 主視窗不受影響。
/// </summary>
public static class EditorChrome
{
    public const string ValidationMessageTemplateKey = "ValidationMessageTemplate";

    public static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    public static void ApplyWindowChrome(Window window)
    {
        // WPF 不會把 TargetType="Window" 的隱含樣式套到 Window 子類別，因此直接設定字型與底色。
        window.FontFamily = (FontFamily)Application.Current.FindResource("AppFont");
        window.Foreground = UiTheme.TextStrongBrush;
        window.Background = UiTheme.WindowBackgroundBrush;
        window.Resources[typeof(TabControl)] = StyleOf("EditorTabControl");
        window.Resources[typeof(ListBox)] = StyleOf("EditorList");
        window.Resources[typeof(GroupBox)] = StyleOf("EditorGroupBox");
        window.Resources[typeof(ProgressBar)] = StyleOf("ThinProgressBar");
    }

    public static StackPanel PageHeader(string title, string description)
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = title, Style = StyleOf("ResultPageTitle") });
        var hint = Hint(description);
        hint.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(hint);
        return header;
    }

    public static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = UiTheme.TextStrongBrush,
        Margin = new Thickness(0, 12, 0, 6)
    };

    public static TextBlock Hint(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UiTheme.TextMutedBrush
    };

    public static TextBlock FieldLabel(string text) => new() { Text = text, Style = StyleOf("FieldLabel") };

    /// <summary>白底圓角卡片；有標題時標題在左上，內容填滿其餘空間。</summary>
    public static Border Card(UIElement? child = null, string? title = null)
    {
        var card = new Border { Style = StyleOf("ResultCard"), Padding = new Thickness(10) };
        if (title is null)
        {
            card.Child = child;
            return card;
        }

        var panel = new DockPanel();
        var heading = SectionTitle(title);
        heading.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        if (child is not null) panel.Children.Add(child);
        card.Child = panel;
        return card;
    }

    /// <summary>主要／次要按鈕；外觀與互動狀態全部由共用樣式決定。</summary>
    public static Button Button(string label, RoutedEventHandler handler, bool primary)
    {
        var button = new Button { Content = label, Style = StyleOf(primary ? "PrimaryButton" : "SecondaryButton") };
        button.Click += handler;
        return button;
    }

    /// <summary>放大鏡圖示＋灰色提示字的搜尋框；可傳入既有文字框（保留其名稱與事件）。</summary>
    public static TextBox SearchBox(string placeholder, TextBox? box = null)
    {
        box ??= new TextBox();
        box.Style = StyleOf("SearchBox");
        box.Tag = placeholder;
        return box;
    }

    public static Ellipse StatusDot(Brush fill) => new()
    {
        Width = 8,
        Height = 8,
        Fill = fill,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>驗證訊息清單：每列一個嚴重度色點加換行文字；停用左右捲動，長訊息才會換行。</summary>
    public static void UseValidationTemplate(ListBox list)
    {
        list.ItemTemplate = (DataTemplate)Application.Current.FindResource(ValidationMessageTemplateKey);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
    }

    public static SolidColorBrush ValidationSeverityBrush(ProjectValidationSeverity severity) => severity switch
    {
        ProjectValidationSeverity.Error => UiTheme.DangerBrush,
        ProjectValidationSeverity.Warning => UiTheme.CautionBrush,
        _ => UiTheme.TextSubtleBrush
    };

    /// <summary>底部驗證摘要色點：未驗證灰、有錯誤紅、只有提醒黃、通過綠。</summary>
    public static SolidColorBrush ValidationSummaryBrush(bool validated, int errors, int warnings) =>
        !validated ? UiTheme.TextSubtleBrush
        : errors > 0 ? UiTheme.DangerBrush
        : warnings > 0 ? UiTheme.CautionBrush
        : UiTheme.SuccessBrush;
}

/// <summary>驗證訊息嚴重度 → 色點畫筆（驗證清單範本使用）。</summary>
public sealed class ValidationSeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ProjectValidationSeverity severity ? EditorChrome.ValidationSeverityBrush(severity) : UiTheme.TextSubtleBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("驗證色點只供顯示，不支援反向轉換。");
}
