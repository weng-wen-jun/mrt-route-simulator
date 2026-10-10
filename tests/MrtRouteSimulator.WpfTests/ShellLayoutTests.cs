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
}
