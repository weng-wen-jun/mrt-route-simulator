using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            window.Height = 720;
            window.Show();
            WpfTestWait.Wait(Task.Delay(200));
            var worker = (SimulationPlaybackWorker)WpfTestWait.Field(window, "_playbackWorker")!;
            var canvas = (Canvas)window.FindName("RouteCanvas");
            ((TabControl)window.FindName("WorkspaceTabControl")).SelectedItem =
                window.FindName("SimulationTabItem");
            window.UpdateLayout();
            var viewTabs = (TabControl)window.FindName("SimulationViewTabControl");
            var routeViewport = (ScrollViewer)window.FindName("RouteScrollViewer");
            var simulationGrid = (Grid)viewTabs.Parent;
            var workspaceTabs = (TabControl)window.FindName("WorkspaceTabControl");
            Console.WriteLine($"layout viewTabs={viewTabs.ActualHeight:0.0} routeViewport={routeViewport.ActualHeight:0.0} "
                + $"simulationGrid={simulationGrid.ActualHeight:0.0} workspaceTabs={workspaceTabs.ActualHeight:0.0} "
                + $"row1={simulationGrid.RowDefinitions[1].ActualHeight:0.0} window={window.ActualHeight:0.0}");
            if (Math.Abs(viewTabs.ActualHeight - simulationGrid.RowDefinitions[1].ActualHeight) > 5
                || routeViewport.ActualHeight < viewTabs.ActualHeight - 65)
                throw new InvalidOperationException("路線圖分頁未撐滿模擬頁面剩餘高度。");
            typeof(MainWindow).GetMethod("DrawV2Route",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(window, [null]);
            if (canvas.Height + .5 < routeViewport.ViewportHeight)
                throw new InvalidOperationException("路線圖畫布沒有填滿可視高度。");
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
            window.UpdateLayout();
            var windowBitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
                (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            windowBitmap.Render(window);
            var windowPath = Path.Combine(AppContext.BaseDirectory, "large-playback-window.png");
            var windowEncoder = new PngBitmapEncoder();
            windowEncoder.Frames.Add(BitmapFrame.Create(windowBitmap));
            using (var output = File.Create(windowPath)) windowEncoder.Save(output);
            Console.WriteLine($"windowImage={windowPath}");
            var trainMarker = canvas.Children.OfType<Border>()
                .FirstOrDefault(item => item.Tag is string);
            if (trainMarker is null || trainMarker.Tag is not string selectedVehicleId)
                throw new InvalidOperationException("大型路線圖未顯示可點選的運行列車。");
            trainMarker.RaiseEvent(new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice,
                Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            });
            var speedTab = (TabItem)window.FindName("SpeedProfileTabItem");
            var speedSelector = (ComboBox)window.FindName("SpeedProfileRunComboBox");
            if (!ReferenceEquals(viewTabs.SelectedItem, speedTab)
                || !Equals(speedSelector.SelectedItem, selectedVehicleId))
                throw new InvalidOperationException("點選路線圖列車未跳至該車完整行程速度曲線。");
            viewTabs.SelectedIndex = 0;
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
