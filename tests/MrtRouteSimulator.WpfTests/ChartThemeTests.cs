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
        VerifyThemeTokens();
        Console.WriteLine("PASS WPF chart theme");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyThemeTokens()
    {
        var tokens = new (string Name, SolidColorBrush Actual, SolidColorBrush Source)[]
        {
            ("Background", ChartTheme.Background, UiTheme.CanvasBackgroundBrush),
            ("Grid", ChartTheme.Grid, UiTheme.BorderBrush),
            ("Axis", ChartTheme.Axis, UiTheme.TextSubtleBrush),
            ("AxisLabel", ChartTheme.AxisLabel, UiTheme.TextMutedBrush),
            ("Title", ChartTheme.Title, UiTheme.TextStrongBrush),
            ("LegendText", ChartTheme.LegendText, UiTheme.TextMutedBrush),
            ("Message", ChartTheme.Message, UiTheme.TextMutedBrush),
            ("Annotation", ChartTheme.Annotation, UiTheme.TextSubtleBrush),
            ("PrimarySeries", ChartTheme.PrimarySeries, UiTheme.AccentBrush),
            ("LimitSeries", ChartTheme.LimitSeries, UiTheme.TextMutedBrush),
            ("ThresholdSeries", ChartTheme.ThresholdSeries, UiTheme.RailNeutralStrongBrush),
            ("DangerSeries", ChartTheme.DangerSeries, UiTheme.DangerBrush),
            ("NeutralSeries", ChartTheme.NeutralSeries, UiTheme.RailNeutralStrongBrush),
            ("EventStation", ChartTheme.EventStation, UiTheme.SuccessBrush),
            ("EventTerminal", ChartTheme.EventTerminal, UiTheme.RailNeutralStrongBrush),
            ("EventSafety", ChartTheme.EventSafety, UiTheme.DangerBrush),
            ("MarkerOutline", ChartTheme.MarkerOutline, UiTheme.SurfaceBrush),
            ("TailTrack", ChartTheme.TailTrack, UiTheme.RailDownBrush),
            ("ExportPage", ChartTheme.ExportPage, UiTheme.SurfaceBrush)
        };
        foreach (var (name, actual, source) in tokens)
            Require(ReferenceEquals(actual, source) && actual.IsFrozen, $"ChartTheme.{name} 必須直接引用對應的 UiTheme 凍結畫筆。");

        var zone = ChartTheme.LimitZone.Color;
        Require(ChartTheme.LimitZone.IsFrozen && zone.A == 0x2A
                && zone.R == UiTheme.TextSubtle.R && zone.G == UiTheme.TextSubtle.G && zone.B == UiTheme.TextSubtle.B,
            "LimitZone 必須是 TextSubtle 加 0x2A 透明度的凍結畫筆。");
        foreach (var (name, dash, expected) in new (string, DoubleCollection, double[])[]
                 {
                     ("LongDash", ChartTheme.LongDash, new double[] { 5, 3 }),
                     ("ShortDot", ChartTheme.ShortDot, new double[] { 2, 3 }),
                     ("TailDash", ChartTheme.TailDash, new double[] { 4, 3 }),
                     ("PlannedDash", ChartTheme.PlannedDash, new double[] { 6, 4 })
                 })
            Require(dash.IsFrozen && dash.SequenceEqual(expected), $"ChartTheme.{name} 應為凍結的 {string.Join(",", expected)}。");
        Require(ReferenceEquals(ChartTheme.Font, Application.Current.TryFindResource("AppFont")), "圖表字型必須是 App 字型（AppFont）。");
        Require(ChartTheme.GridThickness == 1 && ChartTheme.AxisThickness == 1.2 && ChartTheme.AxisLabelFontSize == 10
                && ChartTheme.TimeTickFontSize == 9 && ChartTheme.TitleFontSize == 13 && ChartTheme.LegendFontSize == 11
                && ChartTheme.MessageFontSize == 12, "圖表線寬與字級常數不符規格。");
        Console.WriteLine("[通過] ChartTheme 色票直接引用 UiTheme 且凍結");
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
