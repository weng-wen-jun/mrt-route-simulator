using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

internal static class OuterShellScrollTests
{
    public static void Run(string root)
    {
        VerifyInitialWindowBoundsVectors();
        VerifyDefaultWindowFitsActualMonitor();
        VerifyUnbuiltStartupLayout();

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
            // This is deliberate: the initial-fit policy must not overwrite a
            // test-created compact window before the layout assertions run.
            Width = 800,
            Height = 520
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

            Require(Math.Abs(window.ActualWidth - 800) < 1 && Math.Abs(window.ActualHeight - 520) < 1,
                $"測試明確設定的 800x520 視窗不得被 initial-fit 覆寫；actual={window.ActualWidth:0.0}x{window.ActualHeight:0.0}。");

            var shell = (ScrollViewer)window.FindName("ShellScrollViewer")!;
            var shellGrid = (Grid)window.FindName("ShellContentGrid")!;
            var route = (ScrollViewer)window.FindName("RouteScrollViewer")!;
            var canvas = (Canvas)window.FindName("RouteCanvas")!;
            var proxy = (ScrollBar)window.FindName("ShellRouteHorizontalScrollBar")!;
            var clockBefore = ((TextBlock)window.FindName("SimulationClockText")!).Text;

            Require(Math.Abs(shellGrid.Height - 720) < 1,
                $"ShellContentGrid 應使用最多 720 DIP 的固定內容高度；height={shellGrid.Height:0.0}。");
            Require(shell.ExtentHeight > shell.ViewportHeight + 1 && shell.ScrollableHeight > 1,
                $"800x520 時外層 shell 必須可垂直捲動；extent={shell.ExtentHeight:0.0}, viewport={shell.ViewportHeight:0.0}, scrollable={shell.ScrollableHeight:0.0}。");

            // Force both axes to overflow without depending on the sample's
            // route geometry, while leaving the application responsible for
            // synchronizing the fixed proxy with the real RouteScrollViewer.
            canvas.MinWidth = Math.Max(canvas.MinWidth, route.ViewportWidth + 240);
            canvas.MinHeight = Math.Max(canvas.MinHeight, route.ViewportHeight + 180);
            PumpLayout(window);
            Require(route.ScrollableWidth > 1 && route.ScrollableHeight > 1,
                $"基準樣本在測試中必須同時有路線水平／垂直溢位；horizontal={route.ScrollableWidth:0.0}, vertical={route.ScrollableHeight:0.0}。");
            Require(proxy.Visibility == Visibility.Visible,
                $"選取 Simulation + Route 且有水平溢位時固定 proxy 必須可見；visibility={proxy.Visibility}。");

            shell.ScrollToVerticalOffset(0);
            route.ScrollToVerticalOffset(0);
            route.ScrollToHorizontalOffset(0);
            PumpLayout(window);
            VerifyProxyWithinWindow(window, proxy, "outer-top/route-top");
            var initialOffset = route.HorizontalOffset;
            var requestedOffset = Math.Min(proxy.Maximum,
                Math.Max(proxy.Minimum + 1, initialOffset + Math.Max(1, route.ScrollableWidth * .5)));
            Require(requestedOffset > initialOffset,
                $"固定 proxy 必須有可用的水平範圍；minimum={proxy.Minimum:0.0}, maximum={proxy.Maximum:0.0}, routeScrollable={route.ScrollableWidth:0.0}。");
            proxy.Value = requestedOffset;
            PumpLayout(window);
            Require(route.HorizontalOffset > initialOffset + .1,
                $"操作固定 proxy 必須改變實際 RouteScrollViewer.HorizontalOffset；before={initialOffset:0.0}, after={route.HorizontalOffset:0.0}, requested={requestedOffset:0.0}。");
            Require(Math.Abs(proxy.Value - route.HorizontalOffset) < 1,
                $"固定 proxy 操作後必須與實際 RouteScrollViewer.HorizontalOffset 同步；proxy={proxy.Value:0.0}, offset={route.HorizontalOffset:0.0}。");

            shell.ScrollToVerticalOffset(shell.ScrollableHeight);
            route.ScrollToVerticalOffset(0);
            PumpLayout(window);
            var shellGridAtBottom = shellGrid.TransformToAncestor(window).TransformBounds(
                new Rect(0, 0, shellGrid.ActualWidth, shellGrid.ActualHeight));
            Require(shellGridAtBottom.Top < -1,
                $"外層 shell 捲到底部時 ShellContentGrid 頂端應離開 viewport；bounds={shellGridAtBottom}, shellOffset={shell.VerticalOffset:0.0}。");
            VerifyProxyWithinWindow(window, proxy, "outer-bottom/route-top");

            route.ScrollToVerticalOffset(route.ScrollableHeight);
            PumpLayout(window);
            VerifyProxyWithinWindow(window, proxy, "outer-bottom/route-bottom");

            shell.ScrollToVerticalOffset(0);
            route.ScrollToVerticalOffset(route.ScrollableHeight);
            PumpLayout(window);
            VerifyProxyWithinWindow(window, proxy, "outer-top/route-bottom");

            Require(clockBefore == ((TextBlock)window.FindName("SimulationClockText")!).Text,
                "外層／路線捲動與固定水平 proxy 操作不得改變 paused simulation clock。");
            Console.WriteLine($"PASS WPF outer shell scroll: shell={shell.ViewportWidth:0.0}x{shell.ViewportHeight:0.0}, "
                + $"shellScrollable={shell.ScrollableHeight:0.0}, routeHorizontal={route.ScrollableWidth:0.0}");
        }
        finally
        {
            WpfTestWait.Close(window);
            scaleService.GetMethod("SetScale")!.Invoke(null, [previousScale]);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        VerifyLargeWindowShellFillAndRestore();
        VerifyStickyRouteScrollbarGeometryAndVisibility(root);
    }

    /// <summary>
    /// Regression coverage for the fixed route scrollbar overlay.  This is
    /// deliberately separate from the legacy compact-shell assertions above:
    /// the overlay has to follow the route viewport, while remaining outside
    /// both ScrollViewer visual trees and clamping to the shell viewport.
    /// </summary>
    private static void VerifyStickyRouteScrollbarGeometryAndVisibility(string root)
    {
        var scaleService = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previousScale = (double)scaleService.GetProperty("CurrentScale")!.GetValue(null)!;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.GetFullPath(samplePath)));

        try
        {
            foreach (var requestedScale in new[] { 1d, .8d, 1.25d })
            {
                scaleService.GetMethod("SetScale")!.Invoke(null, [requestedScale]);
                var window = new MainWindow
                {
                    Width = 1180,
                    Height = 720,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = 0,
                    Top = 0
                };

                try
                {
                    WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                        window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
                    window.Show();
                    SelectWorkspaceTab(window, "SimulationTabItem");
                    SelectRouteView(window);
                    PumpLayout(window);

                    var shellOverlayRoot = (Grid)window.FindName("ShellOverlayRoot")!;
                    var shell = (ScrollViewer)window.FindName("ShellScrollViewer")!;
                    var shellOverlay = (Canvas)window.FindName("ShellRouteScrollbarOverlay")!;
                    var shellGrid = (Grid)window.FindName("ShellContentGrid")!;
                    var routeHost = (Grid)window.FindName("RouteViewportHost")!;
                    var route = (ScrollViewer)window.FindName("RouteScrollViewer")!;
                    var canvas = (Canvas)window.FindName("RouteCanvas")!;
                    var proxy = (ScrollBar)window.FindName("ShellRouteHorizontalScrollBar")!;

                    VerifyOverlayVisualTree(window, shellOverlayRoot, shell, shellOverlay, route, proxy);
                    Require(Grid.GetRow(routeHost) == 1,
                        $"RouteViewportHost 在 scale={requestedScale:0.00} 必須位於 route 區域 Grid.Row=1；row={Grid.GetRow(routeHost)}。");
                    Require(route.Margin.Bottom >= 17.5 && route.Margin.Bottom <= 18.5,
                        $"RouteScrollViewer 在 scale={requestedScale:0.00} 必須保留 18 DIP bottom margin；margin={route.Margin}。");
                    Require(IsVisualDescendant(routeHost, route),
                        "RouteScrollViewer 必須位於 RouteViewportHost 內，不能繞過 host 直接掛在外層。");

                    // The three-station sample is intentionally small.  Force
                    // both axes here so this test exercises the real proxy
                    // synchronization rather than sample-size luck.
                    canvas.MinWidth = Math.Max(canvas.MinWidth, route.ViewportWidth + 240);
                    canvas.MinHeight = Math.Max(canvas.MinHeight, route.ViewportHeight + 180);
                    PumpLayout(window);
                    Require(route.ScrollableWidth > 1 && route.ScrollableHeight > 1,
                        $"scale={requestedScale:0.00} 強制溢位後路線必須同時可水平／垂直捲動；horizontal={route.ScrollableWidth:0.0}, vertical={route.ScrollableHeight:0.0}。");

                    shell.ScrollToVerticalOffset(0);
                    route.ScrollToVerticalOffset(0);
                    route.ScrollToHorizontalOffset(0);
                    PumpLayout(window);
                    VerifyStickyProxyGeometry(window, shell, shellGrid, routeHost, route, proxy,
                        $"scale={requestedScale:0.00}/normal/top");
                    VerifyProxyDoesNotOverlapStatusRow(window, shellGrid, proxy,
                        $"scale={requestedScale:0.00}/normal");

                    var normalBarY = ProxyBounds(window, proxy).Y;
                    route.ScrollToVerticalOffset(route.ScrollableHeight);
                    PumpLayout(window);
                    var routeBottomBarY = ProxyBounds(window, proxy).Y;
                    Require(Math.Abs(routeBottomBarY - normalBarY) < 1,
                        $"內層 RouteScrollViewer 垂直捲動不得改變固定 proxy Y；top={normalBarY:0.0}, bottom={routeBottomBarY:0.0}。");

                    var requestedOffset = Math.Min(proxy.Maximum,
                        Math.Max(proxy.Minimum + 1, route.HorizontalOffset + Math.Max(1, route.ScrollableWidth * .5)));
                    Require(requestedOffset > route.HorizontalOffset,
                        $"scale={requestedScale:0.00} proxy 必須有可用水平範圍；minimum={proxy.Minimum:0.0}, maximum={proxy.Maximum:0.0}, routeScrollable={route.ScrollableWidth:0.0}。");
                    proxy.Value = requestedOffset;
                    PumpLayout(window);
                    Require(route.HorizontalOffset > .1,
                        $"proxy.Value 在 scale={requestedScale:0.00} 必須實際推動 RouteScrollViewer；offset={route.HorizontalOffset:0.0}, requested={requestedOffset:0.0}。");
                    Require(Math.Abs(proxy.Value - route.HorizontalOffset) < 1,
                        $"proxy -> route 同步失敗；proxy={proxy.Value:0.0}, offset={route.HorizontalOffset:0.0}。");

                    var reverseOffset = Math.Min(route.ScrollableWidth,
                        Math.Max(0, route.HorizontalOffset * .25));
                    route.ScrollToHorizontalOffset(reverseOffset);
                    PumpLayout(window);
                    Require(Math.Abs(proxy.Value - route.HorizontalOffset) < 1,
                        $"route -> proxy 反向同步失敗；proxy={proxy.Value:0.0}, offset={route.HorizontalOffset:0.0}。");

                    // Compact outer scrolling must keep the route viewport and
                    // its proxy reachable at both ends of the shell scroll.
                    window.Width = 800;
                    window.Height = 520;
                    PumpLayout(window);
                    shell.ScrollToVerticalOffset(0);
                    PumpLayout(window);
                    VerifyStickyProxyGeometry(window, shell, shellGrid, routeHost, route, proxy,
                        $"scale={requestedScale:0.00}/compact/top");
                    shell.ScrollToVerticalOffset(shell.ScrollableHeight);
                    PumpLayout(window);
                    VerifyStickyProxyGeometry(window, shell, shellGrid, routeHost, route, proxy,
                        $"scale={requestedScale:0.00}/compact/bottom");

                    // Add a temporary top spacer to the route host.  This
                    // creates a genuine state where the entire host is below
                    // the shell viewport; the proxy must hide, then reappear
                    // when the spacer is removed.  It is test-only geometry.
                    var previousRouteMargin = routeHost.Margin;
                    var previousRouteHeight = routeHost.Height;
                    try
                    {
                        routeHost.Height = 240;
                        routeHost.Margin = new Thickness(
                            previousRouteMargin.Left,
                            previousRouteMargin.Top + 1200,
                            previousRouteMargin.Right,
                            previousRouteMargin.Bottom);
                        PumpLayout(window);
                        var hiddenHostBounds = Bounds(window, routeHost);
                        var hiddenViewportBounds = ShellViewportBounds(window, shell);
                        Require(hiddenHostBounds.Width > 0 && hiddenHostBounds.Height > 0,
                            "離開viewport的測試必須保留非零路線圖，不能僅因host被壓成零高度而通過。");
                        Require(!Intersects(hiddenHostBounds, hiddenViewportBounds),
                            $"加大 top spacer 後 RouteViewportHost 必須完全離開 shell viewport；host={hiddenHostBounds}, viewport={hiddenViewportBounds}。");
                        Require(proxy.Visibility == Visibility.Collapsed,
                            $"RouteViewportHost 完全離開 shell viewport 時 proxy 必須隱藏；host={hiddenHostBounds}, viewport={hiddenViewportBounds}, proxy={proxy.Visibility}。");
                    }
                    finally
                    {
                        routeHost.Margin = previousRouteMargin;
                        routeHost.Height = previousRouteHeight;
                    }

                    shell.ScrollToVerticalOffset(0);
                    PumpLayout(window);
                    VerifyStickyProxyGeometry(window, shell, shellGrid, routeHost, route, proxy,
                        $"scale={requestedScale:0.00}/restored");

                    // Switching away from the route view must hide the fixed
                    // bar even though the loaded simulation still exists.
                    var views = (TabControl)window.FindName("SimulationViewTabControl")!;
                    views.SelectedItem = window.FindName("SpeedProfileTabItem");
                    PumpLayout(window);
                    Require(proxy.Visibility == Visibility.Collapsed,
                        $"切到 nested speed view 時 proxy 必須隱藏；visibility={proxy.Visibility}。");

                    var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
                    workspace.SelectedItem = window.FindName("DiagramTabItem");
                    PumpLayout(window);
                    Require(proxy.Visibility == Visibility.Collapsed,
                        $"切到 main diagram view 時 proxy 必須隱藏；visibility={proxy.Visibility}。");

                    workspace.SelectedItem = window.FindName("SimulationTabItem");
                    views.SelectedIndex = 0;
                    PumpLayout(window);
                    VerifyStickyProxyGeometry(window, shell, shellGrid, routeHost, route, proxy,
                        $"scale={requestedScale:0.00}/route-restored");

                    Console.WriteLine($"PASS WPF sticky route scrollbar overlay scale={requestedScale:0.00} (effective={scaleService.GetProperty("CurrentScale")!.GetValue(null)})");
                }
                finally
                {
                    WpfTestWait.Close(window);
                }
            }
        }
        finally
        {
            scaleService.GetMethod("SetScale")!.Invoke(null, [previousScale]);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyOverlayVisualTree(
        Window window,
        Grid shellOverlayRoot,
        ScrollViewer shell,
        Canvas shellOverlay,
        ScrollViewer route,
        ScrollBar proxy)
    {
        Require(ReferenceEquals(shellOverlay.Parent, shellOverlayRoot),
            "ShellRouteScrollbarOverlay 必須是 ShellOverlayRoot 的直接子項目。");
        Require(ReferenceEquals(shell.Parent, shellOverlayRoot),
            "ShellScrollViewer 必須是 ShellOverlayRoot 的直接子項目。");
        Require(Panel.GetZIndex(shellOverlay) >= 10,
            $"ShellRouteScrollbarOverlay 必須位於最上層，Panel.ZIndex={Panel.GetZIndex(shellOverlay)}。");
        Require(IsVisualDescendant(shellOverlay, proxy),
            "ShellRouteHorizontalScrollBar 必須位於 ShellRouteScrollbarOverlay 內。");
        Require(!IsVisualDescendant(route, shellOverlay) && !IsVisualDescendant(shell, shellOverlay),
            "ShellRouteScrollbarOverlay 不得是 RouteScrollViewer 或 ShellScrollViewer 的子項目。");
        Require(!IsVisualDescendant(route, proxy) && !IsVisualDescendant(shell, proxy),
            "固定水平捲軸不得落入 RouteScrollViewer／ShellScrollViewer template。");
        Require(window.FindName("ShellOverlayRoot") is Grid,
            "ShellOverlayRoot 必須是可供測試定位的 Grid。");
    }

    private static void VerifyStickyProxyGeometry(
        Window window,
        ScrollViewer shell,
        Grid shellGrid,
        Grid routeHost,
        ScrollViewer route,
        ScrollBar proxy,
        string position)
    {
        Require(proxy.Visibility == Visibility.Visible && proxy.IsVisible,
            $"固定 proxy 在 {position} 必須可見；visibility={proxy.Visibility}, isVisible={proxy.IsVisible}。");
        var proxyBounds = ProxyBounds(window, proxy);
        var shellViewport = ShellViewportBounds(window, shell);
        var hostBounds = Bounds(window, routeHost);
        var routeBounds = Bounds(window, route);
        var expectedBottom = Math.Min(hostBounds.Bottom, shellViewport.Bottom);

        Require(proxy.ActualHeight >= 17 && proxy.ActualHeight <= 19,
            $"固定 proxy 在 {position} 必須約為 18 DIP 高；actual={proxy.ActualHeight:0.0}。");
        Require(Math.Abs(proxyBounds.Bottom - expectedBottom) < 2,
            $"固定 proxy 在 {position} 必須貼 RouteViewportHost 下緣或 shell viewport 底部；proxy={proxyBounds}, host={hostBounds}, shellViewport={shellViewport}。");
        Require(proxyBounds.Top >= shellViewport.Top - 1 && proxyBounds.Bottom <= shellViewport.Bottom + 1,
            $"固定 proxy 在 {position} 必須位於 shell 實際 viewport 內；proxy={proxyBounds}, viewport={shellViewport}。");
        Require(proxyBounds.Left >= routeBounds.Left - 2 && proxyBounds.Right <= routeBounds.Right + 2,
            $"固定 proxy 在 {position} 必須對齊 route 可見水平範圍；proxy={proxyBounds}, route={routeBounds}。");
        Require(proxyBounds.Width > 0 && route.ViewportWidth > 0,
            $"固定 proxy 在 {position} 必須有正寬度；proxyWidth={proxyBounds.Width:0.0}, routeViewportWidth={route.ViewportWidth:0.0}。");
        Require(shellGrid.ActualHeight > 0,
            $"ShellContentGrid 在 {position} 必須有有效 layout；height={shellGrid.ActualHeight:0.0}。");
    }

    private static void VerifyProxyDoesNotOverlapStatusRow(Window window, Grid shellGrid, ScrollBar proxy, string position)
    {
        var proxyBounds = ProxyBounds(window, proxy);
        for (var index = 0; index < shellGrid.Children.Count; index++)
        {
            var child = shellGrid.Children[index] as FrameworkElement;
            if (child is null || Grid.GetRow(child) != 3 || child.Visibility != Visibility.Visible
                || child.ActualWidth <= 0 || child.ActualHeight <= 0) continue;
            var statusBounds = Bounds(window, child);
            Require(!Intersects(proxyBounds, statusBounds),
                $"固定 proxy 在 {position} 不得壓在狀態列下方；proxy={proxyBounds}, status={statusBounds}。");
        }
    }

    private static Rect ShellViewportBounds(Window window, ScrollViewer shell)
    {
        // ScrollViewer.ActualHeight includes its template chrome.  The app's
        // overlay logic uses the content presenter as the real viewport, so
        // keep this regression on the same coordinate source.
        if (shell.Template?.FindName("PART_ScrollContentPresenter", shell) is FrameworkElement presenter
            && presenter.ActualWidth > 0 && presenter.ActualHeight > 0)
            return Bounds(window, presenter);

        return shell.TransformToAncestor(window).TransformBounds(
            new Rect(0, 0, shell.ViewportWidth, shell.ViewportHeight));
    }

    private static Rect ProxyBounds(Window window, ScrollBar proxy) => Bounds(window, proxy);

    private static Rect Bounds(Window window, FrameworkElement element) =>
        element.TransformToAncestor(window).TransformBounds(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool IsVisualDescendant(DependencyObject ancestor, DependencyObject candidate)
    {
        for (var current = candidate; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }

    private static bool Intersects(Rect first, Rect second) =>
        first.Right > second.Left + .5 && first.Left < second.Right - .5
        && first.Bottom > second.Top + .5 && first.Top < second.Bottom - .5;

    private static void VerifyLargeWindowShellFillAndRestore()
    {
        var scaleService = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previousScale = (double)scaleService.GetProperty("CurrentScale")!.GetValue(null)!;
        scaleService.GetMethod("SetScale")!.Invoke(null, [1d]);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow
        {
            // Keep the normal-state regression independent of the startup
            // proportional-fit policy. If the current monitor is too short,
            // ArrangeLargeShellLayout below still exercises the same WPF
            // measure/arrange contract at the requested 1100x900 DIP size.
            Width = 1100,
            Height = 900,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0
        };

        try
        {
            window.Show();
            PumpLayout(window);
            if (window.ActualWidth < 1099 || window.ActualHeight < 899)
                ArrangeLargeShellLayout(window);
            VerifyShellFillsViewport(window, "normal-1100x900");

            window.WindowState = WindowState.Maximized;
            PumpLayout(window);
            VerifyShellFillsViewport(window, "maximized");

            window.WindowState = WindowState.Normal;
            window.Width = 1100;
            window.Height = 900;
            PumpLayout(window);
            if (window.ActualWidth < 1099 || window.ActualHeight < 899)
                ArrangeLargeShellLayout(window);
            VerifyShellFillsViewport(window, "restored-1100x900");

            Console.WriteLine($"PASS WPF outer shell large-window fill: normal={window.Width:0.0}x{window.Height:0.0}, "
                + $"viewport={((ScrollViewer)window.FindName("ShellScrollViewer")!).ViewportHeight:0.0}");
        }
        finally
        {
            WpfTestWait.Close(window);
            scaleService.GetMethod("SetScale")!.Invoke(null, [previousScale]);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void ArrangeLargeShellLayout(MainWindow window)
    {
        // A small CI/remote monitor may clamp an actual Window below 1100x900.
        // Arrange the content root at the intended DIP size so the regression
        // still covers the no-overflow/top-aligned branch without relying on
        // native desktop geometry.
        if (window.Content is not FrameworkElement root)
            throw new InvalidOperationException("MainWindow content 必須是 FrameworkElement。");
        const double width = 1100;
        const double height = 900;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static void VerifyShellFillsViewport(MainWindow window, string position)
    {
        var shell = (ScrollViewer)window.FindName("ShellScrollViewer")!;
        var shellGrid = (Grid)window.FindName("ShellContentGrid")!;
        var viewportHeight = shell.ViewportHeight;
        Require(viewportHeight > 0,
            $"外層 shell 在 {position} 必須有可量測 viewport；actual={shell.ActualHeight:0.0}, viewport={viewportHeight:0.0}。");
        Require(shellGrid.ActualHeight + 1 >= viewportHeight,
            $"ShellContentGrid 在 {position} 的 ActualHeight 不得小於 viewport；grid={shellGrid.ActualHeight:0.0}, viewport={viewportHeight:0.0}。");

        if (shell.ScrollableHeight <= 1)
        {
            var gridBounds = shellGrid.TransformToAncestor(shell).TransformBounds(
                new Rect(0, 0, shellGrid.ActualWidth, shellGrid.ActualHeight));
            Require(Math.Abs(gridBounds.Top) <= 1,
                $"沒有垂直溢位時 ShellContentGrid 在 {position} 必須貼齊 ScrollViewer viewport 頂端；top={gridBounds.Top:0.0}, bounds={gridBounds}, viewport={viewportHeight:0.0}。");
            Require(gridBounds.Bottom + 1 >= viewportHeight,
                $"沒有垂直溢位時 ShellContentGrid 在 {position} 必須覆蓋完整 viewport；bottom={gridBounds.Bottom:0.0}, viewport={viewportHeight:0.0}。");
        }
    }

    private static void VerifyDefaultWindowFitsActualMonitor()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            // Deliberately omit Width/Height: this covers the production startup
            // path, while the compact test above covers explicit caller sizing.
            window.Show();
            PumpLayout(window);

            var viewport = WpfTestWait.Invoke(window, "NativeAcceptanceCaptureViewport")
                ?? throw new InvalidOperationException("NativeAcceptanceCaptureViewport 未回傳資料。");
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(viewport));
            var root = json.RootElement;
            var dpi = root.GetProperty("dpi");
            var dpiScaleX = dpi.GetProperty("DpiScaleX").GetDouble();
            var dpiScaleY = dpi.GetProperty("DpiScaleY").GetDouble();
            Require(dpiScaleX > 0 && dpiScaleY > 0,
                $"native viewport 必須提供正的 DPI scale；x={dpiScaleX:0.000}, y={dpiScaleY:0.000}。");

            var monitor = root.GetProperty("monitor");
            Require(monitor.GetProperty("available").GetBoolean(),
                "native viewport 必須提供目前視窗所在 monitor 的 workAreaPixels。");
            var workPixels = monitor.GetProperty("workAreaPixels");
            var pixelLeft = workPixels.GetProperty("Left").GetDouble();
            var pixelTop = workPixels.GetProperty("Top").GetDouble();
            var pixelRight = workPixels.GetProperty("Right").GetDouble();
            var pixelBottom = workPixels.GetProperty("Bottom").GetDouble();
            var workArea = new Rect(
                pixelLeft / dpiScaleX,
                pixelTop / dpiScaleY,
                (pixelRight - pixelLeft) / dpiScaleX,
                (pixelBottom - pixelTop) / dpiScaleY);

            var calculate = typeof(MainWindow).GetMethod(
                "CalculateInitialWindowBounds",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("MainWindow 缺少 CalculateInitialWindowBounds(Rect, double, double)。");
            var expected = InvokeBounds(calculate, workArea, 800, 520);
            const double tolerance = 1;
            Require(Math.Abs(window.Width - expected.Width) <= tolerance
                && Math.Abs(window.Height - expected.Height) <= tolerance
                && Math.Abs(window.ActualWidth - expected.Width) <= tolerance
                && Math.Abs(window.ActualHeight - expected.Height) <= tolerance,
                $"預設視窗 Width/Height 應符合 monitor workArea 的 90% bounds；expected={expected.Width:0.0}x{expected.Height:0.0}, "
                + $"actual={window.Width:0.0}x{window.Height:0.0}, actualLayout={window.ActualWidth:0.0}x{window.ActualHeight:0.0}, workArea={workArea}。");
            Require(Math.Abs(window.Left - expected.Left) <= tolerance
                && Math.Abs(window.Top - expected.Top) <= tolerance,
                $"預設視窗 Left/Top 應置中於 monitor workArea；expected=({expected.Left:0.0},{expected.Top:0.0}), "
                + $"actual=({window.Left:0.0},{window.Top:0.0}), workArea={workArea}。");
            Require(window.Left >= workArea.Left - tolerance
                && window.Top >= workArea.Top - tolerance
                && window.Left + window.Width <= workArea.Right + tolerance
                && window.Top + window.Height <= workArea.Bottom + tolerance,
                $"預設視窗不得超出 monitor workArea；window=({window.Left:0.0},{window.Top:0.0},{window.Width:0.0},{window.Height:0.0}), workArea={workArea}。");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyUnbuiltStartupLayout()
    {
        var scaleService = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!;
        var previousScale = (double)scaleService.GetProperty("CurrentScale")!.GetValue(null)!;
        scaleService.GetMethod("SetScale")!.Invoke(null, [1d]);
        var window = new MainWindow { Width = 800, Height = 520 };
        try
        {
            window.Show();
            PumpLayout(window);
            var column = (ColumnDefinition)window.FindName("QuickBuilderColumn")!;
            var shellGrid = (Grid)window.FindName("ShellContentGrid")!;
            var route = (ScrollViewer)window.FindName("RouteScrollViewer")!;
            var proxy = (ScrollBar)window.FindName("ShellRouteHorizontalScrollBar")!;
            var calculate = typeof(MainWindow).GetMethod("CalculateRouteCanvasWidth",
                BindingFlags.Static | BindingFlags.NonPublic, null,
                [typeof(double), typeof(int), typeof(double)], null)!;
            Require((double)calculate.Invoke(null, [400d, 0, 2d])! == 400d,
                "未建立路線的空白畫布不得套用 200% 路線縮放。");
            Require(column.ActualWidth >= 280 && column.ActualWidth < 310
                && column.ActualWidth < shellGrid.ActualWidth * .4,
                $"800 DIP 預設頁左欄應自適應縮小；left={column.ActualWidth}, total={shellGrid.ActualWidth}。");
            var stationGrid = (DataGrid)window.FindName("StationDataGrid")!;
            Require(stationGrid.Columns[1].ActualWidth >= 80,
                "小視窗車站表的站名欄不得被壓成不可讀的窄欄。");
            stationGrid.ApplyTemplate();
            var stationScroll = (ScrollViewer)stationGrid.Template.FindName("DG_ScrollViewer", stationGrid)!;
            Require(stationScroll.ScrollableWidth > 1
                && stationScroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible,
                $"小視窗車站表須有自己的水平滑桿；scrollable={stationScroll.ScrollableWidth}, visible={stationScroll.ComputedHorizontalScrollBarVisibility}, setting={stationScroll.HorizontalScrollBarVisibility}, grid={stationGrid.ActualWidth}, extent={stationScroll.ExtentWidth}, viewport={stationScroll.ViewportWidth}。");
            stationScroll.ScrollToRightEnd();
            PumpLayout(window);
            Require(stationScroll.HorizontalOffset > 1, "車站表水平滑桿須能實際到達右側欄位。");
            typeof(MainWindow).GetField("_routeMapHorizontalZoom", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, 2d);
            typeof(MainWindow).GetMethod("DrawRoute", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [null]);
            PumpLayout(window);
            Require(route.ScrollableWidth < 1 && proxy.Visibility == Visibility.Collapsed,
                $"未讀檔／未建立時不應有空白水平捲軸；overflow={route.ScrollableWidth}, proxy={proxy.Visibility}。");
            window.Width = 1400;
            PumpLayout(window);
            Require(Math.Abs(column.ActualWidth - 450) < 1,
                $"寬視窗左欄應保留450上限；actual={column.ActualWidth}。");
            window.Width = 800;
            PumpLayout(window);
            Require(route.ScrollableWidth < 1 && proxy.Visibility == Visibility.Collapsed,
                $"寬窗縮回小窗後空白畫布不得殘留溢位；overflow={route.ScrollableWidth}, proxy={proxy.Visibility}。");
            typeof(MainWindow).GetMethod("SetQuickBuilderState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [true, true]);
            window.Width = 800;
            PumpLayout(window);
            Require(column.ActualWidth < 1, "視窗縮放不得重新展開已收合左欄。");
        }
        finally
        {
            WpfTestWait.Close(window);
            scaleService.GetMethod("SetScale")!.Invoke(null, [previousScale]);
        }
    }

    private static void VerifyInitialWindowBoundsVectors()
    {
        var method = typeof(MainWindow).GetMethod(
            "CalculateInitialWindowBounds",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MainWindow 缺少 CalculateInitialWindowBounds(Rect, double, double)。");

        VerifyBounds(method, new Rect(100, 40, 1280, 752), 800, 520,
            new Rect(164, 77.6, 1152, 676.8), "1280x752 DIP");
        VerifyBounds(method, new Rect(0, 0, 1920, 1032), 800, 520,
            new Rect(96, 51.6, 1728, 928.8), "1920x1032 DIP");

        var smallArea = new Rect(300, 200, 760, 500);
        var small = InvokeBounds(method, smallArea, 800, 520);
        Require(small.Left >= smallArea.Left - .01 && small.Top >= smallArea.Top - .01
            && small.Right <= smallArea.Right + .01 && small.Bottom <= smallArea.Bottom + .01,
            $"可用工作區小於 minimum 時初始視窗不得超出 workArea；actual={small}, workArea={smallArea}。");
        Require(small.Width <= smallArea.Width + .01 && small.Height <= smallArea.Height + .01,
            $"可用工作區小於 minimum 時視窗尺寸不得超過 workArea；actual={small}, workArea={smallArea}。");
    }

    private static void VerifyBounds(
        MethodInfo method,
        Rect workArea,
        double minWidth,
        double minHeight,
        Rect expected,
        string label)
    {
        var actual = InvokeBounds(method, workArea, minWidth, minHeight);
        Require(Math.Abs(actual.Left - expected.Left) < .01
            && Math.Abs(actual.Top - expected.Top) < .01
            && Math.Abs(actual.Width - expected.Width) < .01
            && Math.Abs(actual.Height - expected.Height) < .01,
            $"{label} 初始視窗應為 90% 且置中；expected={expected}, actual={actual}。");
    }

    private static Rect InvokeBounds(MethodInfo method, Rect workArea, double minWidth, double minHeight) =>
        method.Invoke(null, [workArea, minWidth, minHeight]) is Rect result
            ? result
            : throw new InvalidOperationException("CalculateInitialWindowBounds 必須回傳 Rect。");

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

    private static void VerifyProxyWithinWindow(Window window, ScrollBar proxy, string position)
    {
        Require(proxy.Visibility == Visibility.Visible && proxy.IsVisible
            && proxy.Orientation == Orientation.Horizontal
            && proxy.ActualWidth > 0 && proxy.ActualHeight > 0,
            $"固定水平捲軸在 {position} 必須可見且為水平方向；visibility={proxy.Visibility}, orientation={proxy.Orientation}, size={proxy.ActualWidth:0.0}x{proxy.ActualHeight:0.0}。");
        var bounds = proxy.TransformToAncestor(window).TransformBounds(
            new Rect(0, 0, proxy.ActualWidth, proxy.ActualHeight));
        Require(bounds.Left >= -1 && bounds.Top >= -1
            && bounds.Right <= window.ActualWidth + 1
            && bounds.Bottom <= window.ActualHeight + 1,
            $"固定水平捲軸在 {position} 必須完整位於 window 可見範圍；bounds={bounds}, window={window.ActualWidth:0.0}x{window.ActualHeight:0.0}。");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
