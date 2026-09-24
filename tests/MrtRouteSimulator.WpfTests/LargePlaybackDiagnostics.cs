using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

internal static class LargePlaybackDiagnostics
{
    public static void Run(string samplePath)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window,
                "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            var canvas = (Canvas)window.FindName("RouteCanvas");
            canvas.Width = 1200;
            canvas.Height = 380;
            canvas.Measure(new Size(1200, 380));
            canvas.Arrange(new Rect(0, 0, 1200, 380));
            ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem =
                window.FindName("SimulationTabItem");
            ((ComboBox)window.FindName("PlaybackSpeedComboBox")).SelectedIndex = 3;

            var timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            var clock = Stopwatch.StartNew();
            var last = clock.Elapsed;
            var maxInputGap = TimeSpan.Zero;
            timer.Tick += (_, _) =>
            {
                var now = clock.Elapsed;
                maxInputGap = TimeSpan.FromTicks(Math.Max(maxInputGap.Ticks, (now - last).Ticks));
                last = now;
            };
            timer.Start();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                WpfTestWait.Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
            WpfTestWait.Wait(Task.Delay(3000));
            timer.Stop();
            WpfTestWait.Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(50));

            var frame = WpfTestWait.LatestFrame(window);
            var status = (TextBlock)window.FindName("PlaybackStatusText");
            Console.WriteLine($"large-playback sample={samplePath}");
            Console.WriteLine($"sim={frame.SimulationTimeSeconds:0.0}s elapsed={clock.Elapsed.TotalSeconds:0.00}s "
                + $"effective={frame.Performance.EffectiveSimulationRate:0.0}x "
                + $"advance={frame.Performance.SimulationAdvanceMilliseconds:0.0}ms "
                + $"frame={frame.Performance.FrameBuildMilliseconds:0.0}ms "
                + $"maxInputGap={maxInputGap.TotalMilliseconds:0.0}ms "
                + $"ui={WpfTestWait.Field(window, "_lastUiRenderMilliseconds"):0.0}ms "
                + $"route={WpfTestWait.Field(window, "_lastRouteRenderMilliseconds"):0.0}ms "
                + $"dropped={frame.Performance.FrameDropCount}");
            Console.WriteLine($"status={status.Text}");
            if (worker.Completion.IsFaulted)
            {
                throw worker.Completion.Exception!.GetBaseException();
            }

            if (frame.SimulationTimeSeconds <= 0)
            {
                throw new InvalidOperationException("大型樣本未能開始播放。");
            }
        }
        finally
        {
            WpfTestWait.Close(window);
        }
    }
}
