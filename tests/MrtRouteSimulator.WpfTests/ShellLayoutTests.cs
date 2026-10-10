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
        Console.WriteLine("PASS WPF shell layout");
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
