using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class NestedPlaybackTabTests
{
    public static void Run(string root)
    {
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            window.Show();
            window.UpdateLayout();
            window.EnablePlaybackDiagnostics();

            var workspace = (TabControl)window.FindName("WorkspaceTabControl")!;
            workspace.SelectedItem = window.FindName("SimulationTabItem");
            var views = (TabControl)window.FindName("SimulationViewTabControl")!;
            var speedView = window.FindName("SpeedProfileTabItem");
            if (speedView is null)
            {
                throw new InvalidOperationException("找不到速度曲線 nested tab。");
            }

            var isPlaying = (bool)(WpfTestWait.Field(window, "_isV2PlaybackPlaying") ?? true);
            Require(!isPlaying, "nested-tab lazy refresh 測試必須從 paused 狀態開始。");

            // Route is the first (unnamed) nested tab.  A forced refresh while paused must render
            // only the selected route view and must not touch the hidden speed chart.
            views.SelectedIndex = 0;
            window.ResetPlaybackDiagnostics();
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            RequireNestedCounts(window, routeMinimum: 1, speedMinimum: 0,
                "Route visible");

            // The routed nested-tab event must force the newly selected view immediately, even
            // though the playback worker remains paused.
            window.ResetPlaybackDiagnostics();
            views.SelectedItem = speedView;
            RequireNestedCounts(window, routeMinimum: 0, speedMinimum: 1,
                "Speed visible");

            window.ResetPlaybackDiagnostics();
            views.SelectedIndex = 0;
            RequireNestedCounts(window, routeMinimum: 1, speedMinimum: 0,
                "Speed to Route switch");

            // The middle nested tab owns the train rows.  It should update its rows through the
            // normal dynamic-row path without drawing either expensive visualization.
            window.ResetPlaybackDiagnostics();
            views.SelectedIndex = 1;
            var trainSnapshot = window.PlaybackDiagnostics.Snapshot();
            Require((trainSnapshot.GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0) == 0
                    && (trainSnapshot.GetTiming(PlaybackDiagnosticMetricNames.SpeedChartRender)?.Count ?? 0) == 0,
                "TrainRows nested tab 不得繪製 Route 或 Speed。\n"
                + trainSnapshot.FormatReport());

            // A child SelectionChanged bubbles through the nested TabControl.  It must not force
            // a complete playback-view refresh, preserving the routed-event source boundary.
            window.ResetPlaybackDiagnostics();
            var trainGrid = (DataGrid)window.FindName("CurrentTrainDataGrid")!;
            trainGrid.RaiseEvent(new SelectionChangedEventArgs(
                Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
            var childSnapshot = window.PlaybackDiagnostics.Snapshot();
            Require(childSnapshot.GetCount(PlaybackDiagnosticMetricNames.UiFrameAppliedCount) == 0
                    && (childSnapshot.GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0) == 0
                    && (childSnapshot.GetTiming(PlaybackDiagnosticMetricNames.SpeedChartRender)?.Count ?? 0) == 0,
                "nested 子控制項 SelectionChanged 不得觸發 playback refresh。\n"
                + childSnapshot.FormatReport());

            Console.WriteLine("PASS WPF nested playback tab lazy refresh");
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void RequireNestedCounts(
        MainWindow window,
        long routeMinimum,
        long speedMinimum,
        string label)
    {
        var snapshot = window.PlaybackDiagnostics.Snapshot();
        var routeCount = snapshot.GetTiming(PlaybackDiagnosticMetricNames.RouteMarkerRender)?.Count ?? 0;
        var speedCount = snapshot.GetTiming(PlaybackDiagnosticMetricNames.SpeedChartRender)?.Count ?? 0;
        Require(routeCount >= routeMinimum && speedCount >= speedMinimum
                && (routeMinimum == 0 || speedCount == 0)
                && (speedMinimum == 0 || routeCount == 0),
            $"{label} nested render counts 不符預期：route={routeCount}, speed={speedCount}\n"
            + snapshot.FormatReport());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
