using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class CompactRouteLayoutTests
{
    public static void Run(string root)
    {
        var scaleService = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previousScale = (double)scaleService.GetProperty("CurrentScale")!.GetValue(null)!;
        scaleService.GetMethod("SetScale")!.Invoke(null, [1d]);
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.GetFullPath(samplePath)));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow
        {
            Width = 1180,
            Height = 720
        };

        try
        {
            Require(Math.Abs(window.MinWidth - 800) < .1,
                $"MainWindow production MinWidth 必須是 800 DIP；actual={window.MinWidth:0.0}。");
            Require(Math.Abs(window.MinHeight - 520) < .1,
                $"MainWindow production MinHeight 必須是 520 DIP；actual={window.MinHeight:0.0}。");
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            window.Show();
            SelectWorkspaceTab(window, "SimulationTabItem");
            SelectRouteView(window);
            PumpLayout(window);

            EnsureShellReachable(window, FindMainMenu(window), "主選單");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("PlayButton")!, "播放控制");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("PlaybackSpeedComboBox")!, "播放倍率控制");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("WorkspaceTabControl")!, "工作區分頁");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("SimulationViewTabControl")!, "模擬檢視分頁");

            var summary = (Expander)window.FindName("RouteSummaryExpander")!;
            Require(summary.IsExpanded, "路線摘要預設必須展開，避免初次載入遺失摘要資訊。");
            var summaryValues = SummaryValues(window);
            Require(summaryValues.All(value => !string.IsNullOrWhiteSpace(value) && value != "—"),
                "拓撲專案載入後五個摘要卡不得是空白或佔位字元。");
            var clockBeforeCollapse = ((TextBlock)window.FindName("SimulationClockText")!).Text;

            var viewer = (ScrollViewer)window.FindName("RouteScrollViewer")!;
            var expandedViewerHeight = viewer.ActualHeight;
            Require(expandedViewerHeight > 0, $"模擬路線捲動區必須有可量測的高度。summary={summary.ActualHeight}, cap={((ScrollViewer)window.FindName("RouteSummaryScrollViewer")!).MaxHeight}, tabs={((TabControl)window.FindName("SimulationViewTabControl")!).ActualHeight}, scale={((FrameworkElement)window.Content).LayoutTransform.Value.M11}");

            var canvas = (Canvas)window.FindName("RouteCanvas")!;
            // Force both axes to overflow so this regression remains focused on
            // the compact layout even when the small baseline sample is loaded.
            canvas.MinWidth = Math.Max(canvas.MinWidth, viewer.ViewportWidth + 240);
            canvas.MinHeight = Math.Max(canvas.MinHeight, viewer.ViewportHeight + 180);
            PumpLayout(window);
            VerifyOverflow(viewer);

            viewer.ScrollToVerticalOffset(0);
            PumpLayout(window);
            var expandedTopBarY = VerifyScrollViewerVisible(window, viewer, "expanded/top");
            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
            PumpLayout(window);
            var expandedBottomBarY = VerifyScrollViewerVisible(window, viewer, "expanded/bottom");
            Require(Math.Abs(expandedBottomBarY - expandedTopBarY) < 1,
                $"expanded 狀態在 vertical offset 0/bottom 的水平捲軸 Y 必須固定；top={expandedTopBarY:0.0}, bottom={expandedBottomBarY:0.0}。");
            viewer.ScrollToVerticalOffset(0);
            PumpLayout(window);

            summary.IsExpanded = false;
            PumpLayout(window);
            var collapsedViewerHeight = viewer.ActualHeight;
            Require(collapsedViewerHeight > expandedViewerHeight + 10,
                $"摘要收合後路線捲動區應增加高度；expanded={expandedViewerHeight:0.0}, collapsed={collapsedViewerHeight:0.0}。");
            summary.IsExpanded = true;
            PumpLayout(window);
            window.Width = 800;
            window.Height = 520;
            PumpLayout(window);
            Require(Math.Abs(window.ActualWidth - 800) < 1 && Math.Abs(window.ActualHeight - 520) < 1,
                "正式視窗必須可縮到800x520，不能被舊minimum阻擋。");
            Require(!summary.IsExpanded, "短視窗應收合摘要以保留操作空間。");
            EnsureShellReachable(window, FindMainMenu(window), "主選單");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("PlayButton")!, "播放控制");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("PlaybackSpeedComboBox")!, "播放倍率控制");
            EnsureShellReachable(window, (FrameworkElement)window.FindName("SimulationViewTabControl")!, "模擬檢視分頁");
            Require(summaryValues.SequenceEqual(SummaryValues(window)),
                "摘要收合不得清除或改寫五個摘要值。");
            Require(clockBeforeCollapse == ((TextBlock)window.FindName("SimulationClockText")!).Text,
                "摘要收合不得改變模擬 clock。");
            PumpLayout(window);

            viewer.ScrollToVerticalOffset(0);
            PumpLayout(window);
            VerifyOverflow(viewer);
            var collapsedTopBarY = VerifyScrollViewerVisible(window, viewer, "collapsed/top");
            viewer.ScrollToVerticalOffset(viewer.ScrollableHeight);
            PumpLayout(window);
            var collapsedBottomBarY = VerifyScrollViewerVisible(window, viewer, "collapsed/bottom");
            Require(Math.Abs(collapsedBottomBarY - collapsedTopBarY) < 1,
                $"collapsed 狀態在 vertical offset 0/bottom 的水平捲軸 Y 必須固定；top={collapsedTopBarY:0.0}, bottom={collapsedBottomBarY:0.0}。");

            window.Width = 1180;
            window.Height = 720;
            PumpLayout(window);
            summary.IsExpanded = true;
            PumpLayout(window);
            Require(summaryValues.SequenceEqual(SummaryValues(window)),
                "摘要重新展開後必須保留五個摘要值。");
            Require(clockBeforeCollapse == ((TextBlock)window.FindName("SimulationClockText")!).Text,
                "摘要重新展開不得改變模擬 clock。");
            VerifyOverflow(viewer);
            viewer.ScrollToVerticalOffset(0);
            PumpLayout(window);
            VerifyScrollViewerVisible(window, viewer, "reexpanded/top");

            Console.WriteLine($"PASS WPF compact route layout: viewer={viewer.ActualWidth:0.0}x{viewer.ActualHeight:0.0}, "
                + $"extent={viewer.ExtentWidth:0.0}x{viewer.ExtentHeight:0.0}, summaryDelta={collapsedViewerHeight - expandedViewerHeight:0.0}");
        }
        finally
        {
            WpfTestWait.Close(window);
            scaleService.GetMethod("SetScale")!.Invoke(null, [previousScale]);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static string[] SummaryValues(MainWindow window) =>
    [
        ((TextBlock)window.FindName("RouteSummaryText")!).Text,
        ((TextBlock)window.FindName("OneWaySummaryText")!).Text,
        ((TextBlock)window.FindName("CycleSummaryText")!).Text,
        ((TextBlock)window.FindName("HeadwaySummaryText")!).Text,
        ((TextBlock)window.FindName("SpeedSummaryText")!).Text
    ];

    private static void SelectWorkspaceTab(MainWindow window, string name)
    {
        var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
        workspace.SelectedItem = window.FindName(name);
    }

    private static void SelectRouteView(MainWindow window)
    {
        var views = (TabControl)window.FindName("SimulationViewTabControl")!;
        views.SelectedIndex = 0;
    }

    private static void PumpLayout(MainWindow window)
    {
        window.UpdateLayout();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        window.UpdateLayout();
    }

    private static void VerifyOverflow(ScrollViewer viewer)
    {
        Require(viewer.ExtentWidth > viewer.ViewportWidth + 1,
            $"路線圖內容必須產生水平溢位；extent={viewer.ExtentWidth:0.0}, viewport={viewer.ViewportWidth:0.0}。");
        Require(viewer.ExtentHeight > viewer.ViewportHeight + 1,
            $"路線圖內容必須產生垂直溢位；extent={viewer.ExtentHeight:0.0}, viewport={viewer.ViewportHeight:0.0}。");
    }

    private static double VerifyScrollViewerVisible(Window window, ScrollViewer viewer, string position)
    {
        Require(viewer.ComputedHorizontalScrollBarVisibility != Visibility.Visible,
            $"RouteScrollViewer 在 {position} 不得顯示內嵌水平捲軸；visibility={viewer.ComputedHorizontalScrollBarVisibility}。");
        var horizontalBar = viewer.Template?.FindName("PART_HorizontalScrollBar", viewer) as ScrollBar;
        Require(horizontalBar is null || horizontalBar.Visibility != Visibility.Visible,
            $"RouteScrollViewer 在 {position} 的 PART_HorizontalScrollBar 必須隱藏，不得取代固定 proxy。");

        var proxy = (ScrollBar)window.FindName("ShellRouteHorizontalScrollBar")!;
        VerifyProxyWithinWindow(window, proxy, position);
        Require(!IsVisualDescendant(viewer, proxy),
            $"固定水平捲軸在 {position} 不得是 RouteScrollViewer template 的子項目。");
        Require(Math.Abs(proxy.Value - viewer.HorizontalOffset) < 1,
            $"固定水平捲軸在 {position} 必須同步 RouteScrollViewer.HorizontalOffset；proxy={proxy.Value:0.0}, offset={viewer.HorizontalOffset:0.0}。");
        return proxy.TransformToAncestor(window).TransformBounds(
            new Rect(0, 0, proxy.ActualWidth, proxy.ActualHeight)).Y;
    }

    private static Menu FindMainMenu(MainWindow window)
    {
        return FindVisualDescendant<Menu>((DependencyObject)window.Content)
            ?? throw new InvalidOperationException("找不到主選單；MainWindow 根節點可能已非 Grid。");
    }

    private static void VerifyReachable(Window window, FrameworkElement element, string name)
    {
        Require(element.Visibility == Visibility.Visible && element.IsVisible && element.IsHitTestVisible,
            $"{name} 在 compact 800x520 視窗內必須保持可達；visibility={element.Visibility}, isVisible={element.IsVisible}。");
        var bounds = element.TransformToAncestor(window).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        Require(bounds.Width > 0 && bounds.Height > 0
            && bounds.Right > 0 && bounds.Bottom > 0
            && bounds.Left < window.ActualWidth && bounds.Top < window.ActualHeight,
            $"{name} 在 compact 800x520 視窗內經外層捲動後必須可見；bounds={bounds}, "
            + $"window={window.ActualWidth:0.0}x{window.ActualHeight:0.0}。");
    }

    private static void EnsureShellReachable(MainWindow window, FrameworkElement element, string name)
    {
        var shell = (ScrollViewer)window.FindName("ShellScrollViewer")!;
        shell.ScrollToVerticalOffset(0);
        PumpLayout(window);

        var elementInShell = element.TransformToAncestor(shell).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var viewportHeight = shell.ViewportHeight > 0 ? shell.ViewportHeight : shell.ActualHeight;
        var desiredOffset = shell.VerticalOffset;
        if (elementInShell.Top < 0)
        {
            desiredOffset += elementInShell.Top;
        }
        else if (elementInShell.Bottom > viewportHeight)
        {
            desiredOffset += elementInShell.Bottom - viewportHeight;
        }

        shell.ScrollToVerticalOffset(Math.Clamp(desiredOffset, 0, shell.ScrollableHeight));
        PumpLayout(window);
        VerifyReachable(window, element, name);
    }

    private static void VerifyProxyWithinWindow(Window window, ScrollBar proxy, string position)
    {
        Require(proxy.Visibility == Visibility.Visible && proxy.IsVisible,
            $"固定水平捲軸在 {position} 必須可見；visibility={proxy.Visibility}。");
        var bounds = proxy.TransformToAncestor(window).TransformBounds(
            new Rect(0, 0, proxy.ActualWidth, proxy.ActualHeight));
        Require(bounds.Width > 0 && bounds.Height > 0
            && bounds.Left >= -1 && bounds.Top >= -1
            && bounds.Right <= window.ActualWidth + 1
            && bounds.Bottom <= window.ActualHeight + 1,
            $"固定水平捲軸在 {position} 必須完整位於 window 可見範圍；bounds={bounds}, "
            + $"window={window.ActualWidth:0.0}x{window.ActualHeight:0.0}。");
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match) return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var childMatch = FindVisualDescendant<T>(VisualTreeHelper.GetChild(root, index));
            if (childMatch is not null) return childMatch;
        }

        return null;
    }

    private static bool IsVisualDescendant(DependencyObject ancestor, DependencyObject candidate)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
