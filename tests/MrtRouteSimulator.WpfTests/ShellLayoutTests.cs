using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>主視窗外殼（子專案 C1）的版面與樣式回歸測試。</summary>
internal static class ShellLayoutTests
{
    public static void Run(string root)
    {
        VerifyShellTokens();
        VerifyImplicitStyles();
        Console.WriteLine("PASS WPF shell layout");
    }

    private static readonly string[] ShellTokens =
    [
        "AppBar", "AppBarRaised", "AppBarText", "AppBarMuted", "WindowBackground", "Surface", "Border", "Accent",
        "NavSelected", "NavHover", "Primary", "PrimaryHover", "TimeAccent", "Success", "WarningSurface",
        "WarningBorder", "WarningText", "StatusBar", "TooltipBackground", "TableHeader", "GridLine"
    ];

    private static void VerifyShellTokens()
    {
        var theme = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.UiTheme")!;
        Require(theme.IsPublic, "UiTheme 必須是 public，XAML 才能以 x:Static 引用。");
        foreach (var token in ShellTokens)
        {
            var color = theme.GetField(token)?.GetValue(null);
            var brush = theme.GetField(token + "Brush")?.GetValue(null) as SolidColorBrush;
            Require(color is Color value && brush is not null && brush.IsFrozen && brush.Color == value,
                $"UiTheme 缺少外殼色票 {token} 或其凍結畫筆。");
        }
        var legacy = new (string Key, string Token)[]
        {
            ("InkBrush", "TextStrong"), ("MutedBrush", "TextMuted"), ("PrimaryBrush", "Primary"),
            ("PrimaryDarkBrush", "PrimaryHover"), ("SurfaceBrush", "Surface"), ("BackgroundBrush", "WindowBackground"),
            ("BorderBrush", "Border"), ("SuccessBrush", "Success")
        };
        foreach (var (key, token) in legacy)
        {
            Require(Application.Current.TryFindResource(key) is SolidColorBrush brush
                    && brush.Color == (Color)theme.GetField(token)!.GetValue(null)!,
                $"App.xaml 的 {key} 必須取自 UiTheme.{token}。");
        }
        Console.WriteLine("[通過] UiTheme 外殼色票與相容資源鍵");
    }

    // 1280×800、100% 介面縮放、範例 10、模擬頁「配線圖」子分頁的 RouteScrollViewer 高度。
    internal static double MeasureRouteViewportHeight(string root)
    {
        var previousScale = SetInterfaceScale(1);
        var window = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            LoadSample(window, root, "10-小型-三站完整拓樸基準範例.mrtsim.json");
            window.Show();
            ((TabControl)window.FindName("WorkspaceTabControl")!).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")!).SelectedIndex = 0;
            PumpLayout(window);
            return ((ScrollViewer)window.FindName("RouteScrollViewer")!).ActualHeight;
        }
        finally
        {
            WpfTestWait.Close(window);
            SetInterfaceScale(previousScale);
        }
    }

    internal static void LoadSample(MainWindow window, string root, string fileName)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(System.IO.Path.Combine(root, "samples", fileName)));
        WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
    }

    internal static double SetInterfaceScale(double scale)
    {
        var service = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previous = (double)service.GetProperty("CurrentScale")!.GetValue(null)!;
        service.GetMethod("SetScale")!.Invoke(null, [scale]);
        return previous;
    }

    internal static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyImplicitStyles()
    {
        var resources = Application.Current.Resources;
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(ScrollBar),
                     typeof(Menu), typeof(MenuItem), typeof(ContextMenu), typeof(ToolTip), typeof(Expander), typeof(DataGrid), typeof(DataGridRow) })
            Require(AppDefines(type), $"缺少 {type.Name} 的隱含樣式。");
        foreach (var key in new[] { "PrimaryButton", "SecondaryButton", "GhostButton", "AppBarButton", "AppBarPrimaryButton",
                     "SectionTitle", "FieldLabel", "FocusRingVisual", "IconFont", "MonoFont", "AppFont" })
            Require(Application.Current.TryFindResource(key) is not null, $"缺少資源 {key}。");
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(DataGrid) })
            Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource(type), Control.BackgroundProperty), UiTheme.SurfaceBrush),
                $"{type.Name} 底色必須取自 UiTheme.SurfaceBrush。");
        Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource(typeof(ToolTip)), Control.ForegroundProperty), UiTheme.AppBarTextBrush),
            "提示框文字必須取自 UiTheme.AppBarTextBrush。");
        Require(SetterValue((Style)Application.Current.FindResource(typeof(MenuItem)), Control.TemplateProperty) is ControlTemplate
                && Application.Current.TryFindResource(MenuItem.SeparatorStyleKey) is Style,
            "MenuItem 必須使用自訂範本與分隔線樣式。");
        Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource("PrimaryButton"), Control.BackgroundProperty), UiTheme.PrimaryBrush),
            "主要按鈕必須使用 UiTheme.PrimaryBrush。");
        Require(ReferenceEquals(SetterValue((Style)Application.Current.FindResource(typeof(DataGrid)), DataGrid.HorizontalGridLinesBrushProperty), UiTheme.GridLineBrush),
            "DataGrid 格線必須取自 UiTheme.GridLineBrush。");

        var editable = new ComboBox { IsEditable = true, ItemsSource = new[] { "1", "2" } };
        var readOnly = new ComboBox { ItemsSource = new[] { "A", "B" }, SelectedIndex = 0 };
        var text = new TextBox { Text = "x" };
        var scroll = new ScrollViewer { Height = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, Content = new Border { Height = 200 } };
        var panel = new StackPanel();
        foreach (var child in new UIElement[] { editable, readOnly, text, scroll }) panel.Children.Add(child);
        var window = new Window { Width = 400, Height = 320, Content = panel };
        try
        {
            window.Show();
            PumpLayout(window);
            Require(editable.Template.FindName("PART_EditableTextBox", editable) is TextBox, "可編輯 ComboBox 範本必須提供 PART_EditableTextBox。");
            Require(readOnly.Template.FindName("PART_Popup", readOnly) is Popup, "ComboBox 範本必須提供 PART_Popup。");
            editable.Text = "15";
            PumpLayout(window);
            Require(((TextBox)editable.Template.FindName("PART_EditableTextBox", editable)).Text == "15"
                    && ((TextBox)editable.Template.FindName("PART_EditableTextBox", editable)).IsVisible,
                "可編輯 ComboBox 的文字必須顯示在可見的 PART_EditableTextBox。");
            readOnly.IsDropDownOpen = true;
            PumpLayout(window);
            Require(readOnly.IsDropDownOpen, "ComboBox 必須能展開下拉清單。");
            readOnly.IsDropDownOpen = false;
            Require(text.Template.FindName("PART_ContentHost", text) is ScrollViewer, "TextBox 範本必須提供 PART_ContentHost。");
            var bar = FindDescendant<ScrollBar>(scroll, item => item.Orientation == Orientation.Vertical);
            Require(bar is not null && bar.ActualWidth <= 10.5, $"垂直捲軸寬度應為 10，實際 {bar?.ActualWidth:0.0}。");
        }
        finally { window.Close(); }
        Console.WriteLine("[通過] 共用控制項隱含樣式");
    }

    // 只認 App 自己的資源字典；TryFindResource(typeof(X)) 會退回 WPF 系統佈景樣式，無法證明樣式存在。
    private static bool AppDefines(object key)
    {
        var resources = Application.Current.Resources;
        return resources.Contains(key) || resources.MergedDictionaries.Any(dictionary => dictionary.Contains(key));
    }

    private static object? SetterValue(Style style, DependencyProperty property)
    {
        for (var current = style; current is not null; current = current.BasedOn)
        {
            if (current.Setters.OfType<Setter>().FirstOrDefault(setter => setter.Property == property) is { } setter)
                return setter.Value;
        }
        return null;
    }

    internal static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && (predicate is null || predicate(match))) return match;
            if (FindDescendant(child, predicate) is { } nested) return nested;
        }
        return null;
    }
}
