using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            window.Show();
            WpfTestWait.Wait(Task.Delay(200));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            var canvas = (Canvas)window.FindName("RouteCanvas");
            canvas.Width = 1200;
            canvas.Height = 520;
            canvas.Measure(new Size(1200, 520));
            canvas.Arrange(new Rect(0, 0, 1200, 520));
            ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem =
                window.FindName("SimulationTabItem");
            typeof(MainWindow).GetMethod("DrawV2Route",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, [null]);
            var labels = canvas.Children.OfType<FrameworkElement>()
                .Where(element => element.Tag?.GetType().Name == "StationLabelAnchor")
                .ToArray();
            foreach (var stationId in new[] { "O04", "O13" })
            {
                var count = labels.Count(label => stationId.Equals(
                    label.Tag!.GetType().GetProperty("StationId")!.GetValue(label.Tag)?.ToString(),
                    StringComparison.Ordinal));
                if (count != 1) throw new InvalidOperationException($"{stationId} 站名繪製 {count} 次。");
            }
            if (canvas.Width < document.Topology.Stations.Count * 92 + 120)
                throw new InvalidOperationException("大型路線未保留每站最小水平間距。");
            if (canvas.Children.OfType<TextBlock>().Any(item => Equals(item.Tag, "TrackConnectionIssue")))
                throw new InvalidOperationException("大型路線仍顯示配線待修警告。");
            var presentation = typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.StationSchematicPresentation")!;
            var warnings = (IReadOnlyList<string>)presentation.GetMethod("ValidateStationLabels")!
                .Invoke(null, [canvas])!;
            if (warnings.Count != 0)
                throw new InvalidOperationException($"大型路線版面檢核：{string.Join("；", warnings)}");
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.Width),
                (int)Math.Ceiling(canvas.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var outputPath = Path.Combine(AppContext.BaseDirectory, "large-playback-route.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(outputPath)) encoder.Save(output);
            Console.WriteLine($"routeImage={outputPath}");
            ((ComboBox)window.FindName("PlaybackSpeedComboBox")).SelectedIndex = 3;

            var timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            var clock = Stopwatch.StartNew();
            var last = clock.Elapsed;
            var maxInputGap = TimeSpan.Zero;
            var inputTicks = 0;
            var firstInputTick = TimeSpan.Zero;
            timer.Tick += (_, _) =>
            {
                var now = clock.Elapsed;
                if (++inputTicks == 1) firstInputTick = now;
                maxInputGap = TimeSpan.FromTicks(Math.Max(maxInputGap.Ticks, (now - last).Ticks));
                last = now;
            };
            timer.Start();
            var staticRebuildsBeforePlay = WpfTestWait.Field(window, "_routeStaticRebuildCount");
            var playInvokeStart = clock.Elapsed;
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
            var playInvokeDuration = clock.Elapsed - playInvokeStart;
            WpfTestWait.Wait(Task.Delay(3000));
            timer.Stop();
            WpfTestWait.Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(50));

            var frame = WpfTestWait.LatestFrame(window);
            var status = (TextBlock)window.FindName("PlaybackStatusText");
            Console.WriteLine($"large-playback sample={samplePath}");
            var observedRate = frame.SimulationTimeSeconds / clock.Elapsed.TotalSeconds;
            Console.WriteLine($"sim={frame.SimulationTimeSeconds:0.0}s elapsed={clock.Elapsed.TotalSeconds:0.00}s "
                + $"observed={observedRate:0.0}x "
                + $"advance={frame.Performance.SimulationAdvanceMilliseconds:0.0}ms "
                + $"frame={frame.Performance.FrameBuildMilliseconds:0.0}ms "
                + $"maxInputGap={maxInputGap.TotalMilliseconds:0.0}ms "
                + $"inputTicks={inputTicks} firstInput={firstInputTick.TotalMilliseconds:0.0}ms "
                + $"playInvoke={playInvokeDuration.TotalMilliseconds:0.0}ms "
                + $"ui={WpfTestWait.Field(window, "_lastUiRenderMilliseconds"):0.0}ms "
                + $"route={WpfTestWait.Field(window, "_lastRouteRenderMilliseconds"):0.0}ms "
                + $"staticRebuilds={staticRebuildsBeforePlay}->{WpfTestWait.Field(window, "_routeStaticRebuildCount")} "
                + $"dropped={frame.Performance.FrameDropCount}");
            Console.WriteLine($"status={status.Text}");
            if (worker.Completion.IsFaulted)
            {
                throw worker.Completion.Exception!.GetBaseException();
            }

            if (observedRate < 50)
            {
                throw new InvalidOperationException($"大型樣本 60× 播放僅達 {observedRate:0.0}×。");
            }
            if (maxInputGap.TotalMilliseconds > 250)
                throw new InvalidOperationException($"播放期間 UI 輸入間隔達 {maxInputGap.TotalMilliseconds:0} ms。");
            if (!Equals(staticRebuildsBeforePlay, WpfTestWait.Field(window, "_routeStaticRebuildCount")))
                throw new InvalidOperationException("播放期間重複重建固定配線圖。");
        }
        finally
        {
            WpfTestWait.Close(window);
        }
    }
}
