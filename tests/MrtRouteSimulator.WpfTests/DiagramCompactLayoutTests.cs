using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class DiagramCompactLayoutTests
{
    public static void Run(string root)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,
            "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var scaleService = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previousScale = (double)scaleService.GetProperty("CurrentScale")!.GetValue(null)!;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            foreach (var scale in new[] { 1d, .8d, 1.25d })
            {
                scaleService.GetMethod("SetScale")!.Invoke(null, [scale]);
                var window = new MainWindow { Width = 800, Height = 520 };
                try
                {
                    WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window,
                        "ConfigureTopologyProjectForPlaybackAsync", document, true));
                    window.Show();
                    var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
                    workspace.SelectedItem = window.FindName("DiagramTabItem");
                    var controls = window.FindName("DiagramControlsExpander") as Expander
                        ?? throw new InvalidOperationException("缺少可收折運行圖控制區。");
                    var events = (Expander)window.FindName("DiagramEventsExpander")!;
                    var viewer = (ScrollViewer)window.FindName("DiagramScrollViewer")!;
                    var shell = (ScrollViewer)window.FindName("ShellScrollViewer")!;
                    var grid = (Grid)window.FindName("ShellContentGrid")!;
                    var plot = (Border)window.FindName("DiagramViewportBorder")!;
                    Require(controls.IsExpanded,
                        "運行圖操作區預設展開，既有設定控制仍應可用。");
                    var horizontalZoom = ((Slider)window.FindName("DiagramZoomSlider")!).Value;
                    var verticalZoom = ((Slider)window.FindName("DiagramVerticalZoomSlider")!).Value;
                    foreach (var controlsOpen in new[] { true, false })
                    foreach (var eventsOpen in new[] { true, false })
                    {
                        controls.IsExpanded = controlsOpen;
                        events.IsExpanded = eventsOpen;
                        Pump(window);
                        var condition = $"scale={scale}, controls={controlsOpen}, events={eventsOpen}";
                        Require(plot.ActualHeight >= 299,
                            $"圖框最小高度需至少300 DIP：{condition}, actual={plot.ActualHeight}。");
                        Require(viewer.ActualHeight >= 250,
                            $"圖內viewport不可被事件列表／控制區壓成薄片：{condition}, actual={viewer.ActualHeight}。");
                        Require(shell.ScrollableHeight > 0,
                            $"800x520應由外層垂直捲動承載完整內容：{condition}。");
                        shell.ScrollToEnd();
                        Pump(window);
                        var viewport = (FrameworkElement?)shell.Template.FindName("PART_ScrollContentPresenter", shell) ?? shell;
                        var viewportBounds = viewport.TransformToAncestor(window).TransformBounds(
                            new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
                        var bottomElement = eventsOpen ? (FrameworkElement)events : plot;
                        var bottom = bottomElement.TransformToAncestor(window).TransformBounds(
                            new Rect(0, 0, bottomElement.ActualWidth, bottomElement.ActualHeight)).Bottom;
                        Require(bottom <= viewportBounds.Bottom + 2,
                            $"外層捲到底必須可達完整圖區／事件表底端：{condition}, bottom={bottom}, viewport={viewportBounds}。");
                        Require(((Slider)window.FindName("DiagramZoomSlider")!).Value == horizontalZoom
                            && ((Slider)window.FindName("DiagramVerticalZoomSlider")!).Value == verticalZoom,
                            "收折控制區／事件列表不得改變兩軸縮放設定。");
                    }
                    controls.IsExpanded = true;
                    events.IsExpanded = true;
                    Pump(window);
                    var compactHeight = grid.Height;
                    window.Width = 1440;
                    window.Height = 1000;
                    Pump(window);
                    Require(grid.Height >= shell.ViewportHeight - 1,
                        "寬高視窗仍須填滿外層viewport。");
                    window.Width = 800;
                    window.Height = 520;
                    Pump(window);
                    Require(Math.Abs(grid.Height - compactHeight) < 2,
                        "寬窗復原後Diagram內容高度應回到窄窗需求，不持續累加。");
                    workspace.SelectedItem = window.FindName("SimulationTabItem");
                    Pump(window);
                    Require(Math.Abs(grid.Height - 720) < 1,
                        $"非Diagram分頁保留既有720 DIP compact政策：actual={grid.Height}。");
                    Console.WriteLine($"PASS WPF compact Diagram layout scale={scale}");
                }
                finally { WpfTestWait.Close(window); }
            }
        }
        finally
        {
            scaleService.GetMethod("SetScale")!.Invoke(null, [previousScale]);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Pump(MainWindow window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
