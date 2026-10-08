using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private bool _syncingShellRouteScrollbar;
    private bool _updatingShellContentHeight;
    private double _diagramShellExtraHeight;

    private void ShellScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateShellContentHeight();
        UpdateQuickBuilderWidth();
        UpdateShellRouteScrollbar();
    }

    private void ShellScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // SizeChanged may run before ScrollViewer's viewport is updated. Use the
        // settled viewport notification as well, including DPI/maximize changes.
        if (ReferenceEquals(e.OriginalSource, ShellScrollViewer))
        {
            if (e.ViewportWidthChange != 0) UpdateQuickBuilderWidth();
            if (e.ViewportHeightChange != 0)
            UpdateShellContentHeight();
            UpdateShellRouteScrollbar();
        }
    }

    private void UpdateShellContentHeight()
    {
        if (ShellContentGrid is null || ShellScrollViewer is null || _updatingShellContentHeight) return;
        _updatingShellContentHeight = true;
        try
        {
            var height = ShellScrollViewer.ViewportHeight;
            if (!IsFiniteLayoutDimension(height)) height = ShellScrollViewer.ActualHeight;
            if (!IsFiniteLayoutDimension(height)) return;

            var contentHeight = Math.Max(720, height);
            if (DiagramTabItem?.IsSelected == true && DiagramWorkspaceGrid is not null)
            {
                // The shell has an explicit height so that the other tabs retain their
                // existing 720-DIP frame.  When the Diagram tab needs more room, carry
                // the overflow into the shell itself; otherwise the tab content can be
                // painted below the ScrollViewer extent and the event table is unreachable.
                var actual = DiagramWorkspaceGrid.ActualHeight;
                if (IsFiniteLayoutDimension(actual))
                {
                    var baseAvailable = Math.Max(0, actual - _diagramShellExtraHeight);
                    var desired = GetDiagramWorkspaceDesiredHeight();
                    var extra = IsFiniteLayoutDimension(desired)
                        ? Math.Max(0, desired - baseAvailable)
                        : _diagramShellExtraHeight;
                    _diagramShellExtraHeight = extra;
                }
                contentHeight += _diagramShellExtraHeight;
            }
            else
            {
                _diagramShellExtraHeight = 0;
            }

            if (Math.Abs(ShellContentGrid.Height - contentHeight) > .01)
                ShellContentGrid.Height = contentHeight;
        }
        finally
        {
            _updatingShellContentHeight = false;
        }
    }

    private double GetDiagramWorkspaceDesiredHeight()
    {
        if (DiagramWorkspaceGrid is null) return double.NaN;

        var controls = GetDesiredHeight(DiagramControlsExpander, 34);
        // Use the explicit frame minimum instead of the Border's DesiredSize: the
        // latter can include a star-row stretch from the previous shell height.
        var viewport = DiagramViewportBorder is not null
            && double.IsFinite(DiagramViewportBorder.MinHeight)
            && DiagramViewportBorder.MinHeight > 0
            ? DiagramViewportBorder.MinHeight
            : 344;
        var events = GetDesiredHeight(DiagramEventsExpander, DiagramEventsExpander?.IsExpanded == true ? 182 : 28);

        // The child Grid's star row can be measured against the current (too-short)
        // shell height.  The explicit minimum pieces keep that clipped measurement
        // from hiding the amount that must be added to the outer extent.
        var minimum = controls + viewport + events + 16;
        return minimum;
    }

    private static double GetDesiredHeight(FrameworkElement? element, double fallback)
    {
        if (element is null) return fallback;
        var desired = element.DesiredSize.Height;
        return double.IsFinite(desired) && desired > 0 ? desired : fallback;
    }

    private void DiagramLayoutExpander_Changed(object sender, RoutedEventArgs e) => UpdateShellContentHeight();

    private void ShellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateShellContentHeight();
        UpdateShellRouteScrollbar();
    }

    private void ShellOverlayRoot_LayoutUpdated(object? sender, EventArgs e)
    {
        UpdateShellContentHeight();
        UpdateShellRouteScrollbar();
    }
    private void ShellRouteScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, RouteScrollViewer)) return;
        // SizeChanged can see the previous viewport during a resize. Settle the
        // empty placeholder against the newly measured viewport as well.
        if (e.ViewportWidthChange != 0 && _route is null
            && _latestPlaybackFrame?.TopologyInfrastructure is null)
            PrepareRouteCanvasWidth();
        UpdateShellRouteScrollbar();
    }

    private void UpdateShellRouteScrollbar()
    {
        if (ShellRouteHorizontalScrollBar is null || RouteScrollViewer is null || WorkspaceTabControl is null
            || ShellOverlayRoot is null || RouteViewportHost is null || ShellScrollViewer is null
            || SimulationViewTabControl is null || _syncingShellRouteScrollbar) return;
        _syncingShellRouteScrollbar = true;
        try
        {
            var visible = SimulationTabItem.IsSelected
                && (_route is not null || _latestPlaybackFrame?.TopologyInfrastructure is not null)
                && SimulationViewTabControl.SelectedIndex == 0 && RouteScrollViewer.ScrollableWidth > 0
                && RouteViewportHost.IsVisible;
            if (visible)
            {
                // Keep the proxy outside both scrolling visual trees. Anchor it to
                // the route's lower edge, clipped to the actual shell viewport.
                // LayoutUpdated also covers font scaling and summary reflow; only
                // changed coordinates are assigned, avoiding a layout feedback loop.
                var viewport = ShellScrollViewer.Template?.FindName(
                    "PART_ScrollContentPresenter", ShellScrollViewer) as FrameworkElement;
                var viewportElement = viewport ?? ShellScrollViewer;
                var viewportBounds = viewportElement.TransformToAncestor(ShellOverlayRoot)
                    .TransformBounds(new Rect(0, 0, viewportElement.ActualWidth, viewportElement.ActualHeight));
                var routeBounds = RouteViewportHost.TransformToAncestor(ShellOverlayRoot)
                    .TransformBounds(new Rect(0, 0, RouteViewportHost.ActualWidth, RouteViewportHost.ActualHeight));
                var intersection = Rect.Intersect(routeBounds, viewportBounds);
                visible = !intersection.IsEmpty && intersection.Width > 0 && intersection.Height > 0;
                if (visible)
                {
                    // A sliver of route must still expose a usable full-height
                    // scrollbar. Do not shrink it below ScrollBar's template
                    // minimum: that would arrange it outside the viewport.
                    var height = Math.Min(18, viewportBounds.Height);
                    SetRouteScrollbarCoordinate(Canvas.GetLeft(ShellRouteHorizontalScrollBar), intersection.Left,
                        Canvas.LeftProperty);
                    SetRouteScrollbarCoordinate(Canvas.GetTop(ShellRouteHorizontalScrollBar),
                        Math.Max(viewportBounds.Top, intersection.Bottom - height),
                        Canvas.TopProperty);
                    SetRouteScrollbarCoordinate(ShellRouteHorizontalScrollBar.Width, intersection.Width,
                        WidthProperty);
                    SetRouteScrollbarCoordinate(ShellRouteHorizontalScrollBar.Height, height,
                        HeightProperty);
                }
            }
            ShellRouteHorizontalScrollBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ShellRouteHorizontalScrollBar.Maximum = RouteScrollViewer.ScrollableWidth;
            ShellRouteHorizontalScrollBar.ViewportSize = RouteScrollViewer.ViewportWidth;
            ShellRouteHorizontalScrollBar.SmallChange = 32;
            ShellRouteHorizontalScrollBar.LargeChange = Math.Max(32, RouteScrollViewer.ViewportWidth * .9);
            ShellRouteHorizontalScrollBar.Value = RouteScrollViewer.HorizontalOffset;
        }
        finally { _syncingShellRouteScrollbar = false; }
    }

    private void SetRouteScrollbarCoordinate(double current, double desired, DependencyProperty property)
    {
        if (!double.IsFinite(current) || Math.Abs(current - desired) > .01)
            ShellRouteHorizontalScrollBar.SetCurrentValue(property, desired);
    }

    private void ShellRouteHorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_syncingShellRouteScrollbar && RouteScrollViewer is not null)
            RouteScrollViewer.ScrollToHorizontalOffset(e.NewValue);
    }
}
