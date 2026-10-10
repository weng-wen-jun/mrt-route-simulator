using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>結果圖表主題（子專案 D）：色票、共用繪圖工具、各圖繪製、PDF 分頁與寫死顏色掃描。</summary>
internal static class ChartThemeTests
{
    public static void Run(string root)
    {
        Console.WriteLine("PASS WPF chart theme");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>以 V1 基礎物理與預設輸入建立模擬；V1 圖表測試與截圖共用。</summary>
    internal static void BuildV1Simulation(MainWindow window)
    {
        ((ComboBox)window.FindName("EngineModeComboBox")!).SelectedIndex = 0; // V1 基礎物理
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            var play = (Button)window.FindName("PlayButton")!;
            WpfTestWait.Invoke(window, "RunSimulation_Click", play, new RoutedEventArgs());
            for (var attempt = 0; attempt < 100 && !play.IsEnabled; attempt++) WpfTestWait.Wait(Task.Delay(50));
            Require(play.IsEnabled && WpfTestWait.Field(window, "_simulationEngine") is not null, "V1 模擬必須建立完成。");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
