using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class InterfaceScaleTests
{
    private static readonly Type Service = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
    private static double Current => (double)Service.GetProperty("CurrentScale")!.GetValue(null)!;
    private static void SetScale(double value) => Service.GetMethod("SetScale")!.Invoke(null, [value]);

    public static void Run(string root)
    {
        var previous = Current;
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        Window? dialog = null;
        try
        {
            SetScale(1);
            var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,
                "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
            var originalProject = TopologyProjectFormat.Serialize(document);
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            WpfTestWait.Advance(window, 20);
            var initial = WpfTestWait.LatestFrame(window);
            window.Width = 800; window.Height = 520;
            Require(Math.Abs(window.MinWidth - 800) < .1,
                $"InterfaceScale production MinWidth 必須是 800 DIP；actual={window.MinWidth:0.0}。");
            Require(Math.Abs(window.MinHeight - 520) < .1,
                $"InterfaceScale production MinHeight 必須是 520 DIP；actual={window.MinHeight:0.0}。");
            window.Show(); window.UpdateLayout();
            Require(Math.Abs(window.Width - 800) < 1 && Math.Abs(window.Height - 520) < 1,
                $"InterfaceScale regression 必須以實際 800x520 DIP 視窗執行；actual={window.Width:0.0}x{window.Height:0.0}。");
            var menu = (MenuItem)window.FindName("InterfaceScaleMenuItem");
            Require(menu.Parent is MenuItem settings && settings.Header.ToString()!.Contains("顯示設定"),
                "介面縮放必須位於存檔同一列的顯示設定選單。");
            Require(menu.Items.OfType<MenuItem>().Select(item => item.Tag?.ToString())
                .SequenceEqual(new[] { "0.8", "0.9", "1", "1.1", "1.25" }), "縮放選項必須包含預設與精簡模式。");

            ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem = window.FindName("SimulationTabItem");
            ((TabControl)window.FindName("SimulationViewTabControl")).SelectedItem = window.FindName("SpeedProfileTabItem");
            var viewer = (ScrollViewer)window.FindName("SpeedScrollViewer");
            var canvas = (Canvas)window.FindName("SpeedCanvas");
            viewer.Height = 110;
            foreach (var scale in new[] { .8, .9, 1d, 1.1, 1.25 })
            {
                SetScale(scale);
                WpfTestWait.Invoke(window, "UpdateInterfaceScaleMenu");
                window.UpdateLayout();
                WpfTestWait.Wait(Task.Delay(30));
                window.UpdateLayout();
                var content = (FrameworkElement)window.Content;
                Require(Math.Abs(content.LayoutTransform.Value.M11 - scale) < .001
                    && Math.Abs(content.LayoutTransform.Value.M22 - scale) < .001, "全介面必須使用一致的layout縮放。");
                Require(menu.Items.OfType<MenuItem>().Count(item => item.IsChecked) == 1,
                    "縮放選單必須互斥並反映目前值。");
                var viewport = WpfTestWait.Invoke(window, "NativeAcceptanceCaptureViewport")!;
                Require(viewport.GetType().GetProperty("interfaceScale")?.GetValue(viewport) is double recorded
                    && Math.Abs(recorded - scale) < .001,
                    "驗收telemetry須分開記錄App縮放與OS DPI，不能混為一談。");
                Require(viewer.ScrollableHeight > 0 && canvas.ActualHeight >= 220,
                    "短視窗不得裁掉速度圖，必須可垂直捲至完整內容。");
                viewer.ScrollToBottom(); window.UpdateLayout();
                WpfTestWait.Wait(Task.Delay(20)); window.UpdateLayout();
                Require(viewer.VerticalOffset > 0, "速度圖捲軸必須實際可捲動。");
                var canvasBottom = canvas.TransformToAncestor(viewer).Transform(new Point(0, canvas.ActualHeight));
                Require(canvasBottom.Y <= viewer.ActualHeight + 2, "速度圖底部／時間軸須可進入可見viewport。");
                var transform = canvas.TransformToAncestor(window);
                var point = new Point(40, 40);
                var inverse = transform.Inverse ?? throw new InvalidOperationException("縮放hit-test transform不可逆。");
                Require((inverse.Transform(transform.Transform(point)) - point).Length < .001,
                    "縮放後座標映射／hit-test proxy 必須可逆。");
                var frame = WpfTestWait.LatestFrame(window);
                Require(frame.SimulationTimeSeconds == initial.SimulationTimeSeconds
                    && frame.Events.Count == initial.Events.Count && frame.Trajectory.Count == initial.Trajectory.Count,
                    "調整介面不可推進或重算模擬。");
            }
            SetScale(.8);
            dialog = new Window { Width = 400, Height = 240, Content = new Grid(), ShowInTaskbar = false };
            dialog.Show(); dialog.UpdateLayout();
            Require(Math.Abs(((FrameworkElement)dialog.Content).LayoutTransform.Value.M11 - .8) < .001,
                "新開工作區／設定視窗必須沿用軟體縮放。");
            SetScale(1.25);
            Require(Math.Abs(((FrameworkElement)dialog.Content).LayoutTransform.Value.M11 - 1.25) < .001,
                "已開啟的程式視窗必須同步更新。");
            Require(TopologyProjectFormat.Serialize(document) == originalProject,
                "縮放偏好不得進入路線專案序列化。");
            Console.WriteLine("PASS WPF interface scale: menu, five sizes, dialog, scroll, coordinate proxy, project/clock unchanged");
        }
        finally
        {
            dialog?.Close();
            SetScale(previous);
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
