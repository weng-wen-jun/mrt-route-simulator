using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private readonly TimeDistanceTrajectoryCache _diagramActualCache = new();
    // The planned cache is swapped only after a background build has completed.
    // The UI never appends to a cache that is owned by the background builder.
    private TimeDistanceTrajectoryCache _diagramPlannedCache = new();
    private PlannedTimelineArtifact? _diagramPlannedSource;
    private PlannedTimelineArtifact? _diagramPlannedCacheRequestSource;
    private Guid _diagramPlannedCacheRequestGeneration;
    private Task<TimeDistanceTrajectoryCache>? _diagramPlannedCacheTask;
    private CancellationTokenSource? _diagramPlannedCacheCancellation;
    private Exception? _diagramPlannedCacheError;
    private Guid _diagramGeneration;
    private double _diagramLastTime;
    private string? _diagramLayoutKey;
    private bool _diagramDrawing;
    private readonly Dictionary<(bool Planned, string Vehicle, string Run, TrainDirection Direction), DiagramSeriesVisual> _diagramSeries = new();
    private readonly List<Ellipse> _diagramEventVisuals = [];
    private static readonly Brush DiagramSafetyBrush = new SolidColorBrush(Color.FromRgb(196, 48, 48));
    private static readonly Brush DiagramTerminalBrush = new SolidColorBrush(Color.FromRgb(126, 87, 194));
    private static readonly Brush DiagramStationBrush = new SolidColorBrush(Color.FromRgb(22, 134, 107));
    private long _diagramStaticBuilds;
    private int _diagramLastActualCount = -1, _diagramLastPlannedCount = -1, _diagramLastEventCount = -1;
    private bool _diagramViewportRefreshQueued;
    private double _diagramTimeTickIntervalMinutes;
    private const int MaximumDiagramTimeTicks = 2001;

    private void DiagramTimeTicksApply_Click(object sender, RoutedEventArgs e)
    {
        var text = DiagramTimeTickComboBox.Text.Trim();
        var interval = 0d;
        if (text != "自動" && (!double.TryParse(text, out interval)
            || !double.IsFinite(interval) || interval < 1 || interval > 1440))
        {
            StatusTextBlock.Text = "時間刻度請輸入1～1440分鐘，或選擇「自動」；原設定未變更。";
            return;
        }
        var previous = _diagramTimeTickIntervalMinutes;
        _diagramTimeTickIntervalMinutes = interval;
        var available = Math.Max(1, Math.Max(_diagramActualCache.MaxTime, _diagramPlannedCache.MaxTime));
        var start = Math.Clamp(ParseDiagramMinute(DiagramStartMinuteTextBox, 0) * 60, 0, available);
        var end = string.IsNullOrWhiteSpace(DiagramEndMinuteTextBox.Text)
            ? Math.Ceiling(available / 600) * 600
            : Math.Clamp(ParseDiagramMinute(DiagramEndMinuteTextBox, available / 60) * 60,
                start + .1, Math.Max(start + .1, available));
        if (interval > 0 && (end - start) / (interval * 60) > MaximumDiagramTimeTicks - 1)
        {
            _diagramTimeTickIntervalMinutes = previous;
            StatusTextBlock.Text = "目前時間範圍超過2000格，請增加刻度間隔或縮小時間範圍；原設定未變更。";
            return;
        }
        _diagramLayoutKey = null;
        DrawTimeDistanceDiagramIfVisible();
        StatusTextBlock.Text = interval == 0 ? "時間刻度已改為自動六等分。"
            : $"時間刻度已設定為每{interval:0.##}分鐘一格。";
    }

    private double[] GetDiagramTimeTicks(double start, double duration)
    {
        var interval = _diagramTimeTickIntervalMinutes * 60;
        if (interval <= 0)
            return Enumerable.Range(0, 7).Select(i => start + duration * i / 6).ToArray();
        // Manual ticks align to clock multiples, not arbitrary visible-range starts.
        var first = Math.Ceiling((_startClockSeconds + start) / interval) * interval - _startClockSeconds;
        var count = Math.Max(0, Math.Floor((start + duration - first) / interval + 1e-9) + 1);
        if (count > MaximumDiagramTimeTicks)
        {
            StatusTextBlock.Text = "時間範圍過大，僅顯示前2000格；請增加刻度間隔或縮小範圍。";
            count = MaximumDiagramTimeTicks;
        }
        var ticks = Enumerable.Range(0, (int)count).Select(i => first + i * interval).ToList();
        var end = start + duration;
        if (DiagramShowEndTimeCheckBox?.IsChecked == true
            && (ticks.Count == 0 || Math.Abs(ticks[^1] - end) > 1e-7))
            ticks.Add(end);
        return ticks.ToArray();
    }
    private sealed class DiagramSeriesVisual(Polyline line, TextBlock label)
    {
        public Polyline Line { get; } = line;
        public TextBlock Label { get; } = label;
        public long Version { get; set; } = -1;
    }

    private void AddDiagramTimeTickLabel(Canvas canvas, double time, double x,
        double previousX, double end, double top)
    {
        // A short final interval must not obscure either the regular tick or end time.
        var endpoint = DiagramShowEndTimeCheckBox?.IsChecked == true
            && Math.Abs(time - end) < 1e-7;
        var label = new TextBlock
        {
            Text = TrajectoryAnalysis.FormatClock(_startClockSeconds + time),
            FontSize = 9,
            Width = 70,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(82, 93, 111))
        };
        Canvas.SetLeft(label, Math.Max(0, Math.Min(x - 35, canvas.Width - 70)));
        Canvas.SetTop(label, top + (endpoint && x - previousX < 75 ? 12 : 0));
        canvas.Children.Add(label);
    }

    /// <summary>
    /// Starts a planned display-cache build when a new planned artifact is
    /// published.  The source reference and playback generation are both part
    /// of the request identity, so a late result from a replaced/closed
    /// session can never become the visible cache.
    /// </summary>
    private void StartPlannedTimeDistanceCacheWarmup(
        PlannedTimelineArtifact source,
        Guid generation)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (ReferenceEquals(_diagramPlannedSource, source)
            && _diagramPlannedCacheRequestGeneration == generation)
        {
            return;
        }

        if (ReferenceEquals(_diagramPlannedCacheRequestSource, source)
            && _diagramPlannedCacheRequestGeneration == generation
            && (_diagramPlannedCacheTask is not null
                || _diagramPlannedCacheError is not null))
        {
            return;
        }

        CancelPlannedTimeDistanceCacheWarmup(clearPublishedCache: true);

        var cancellation = new CancellationTokenSource();
        var task = TimeDistanceTrajectoryCacheBuilder.BuildAsync(
            source.Trajectory,
            cancellation.Token);
        _diagramPlannedCacheRequestSource = source;
        _diagramPlannedCacheRequestGeneration = generation;
        _diagramPlannedCacheCancellation = cancellation;
        _diagramPlannedCacheTask = task;
        _diagramPlannedCacheError = null;
        ObservePlannedTimeDistanceCacheWarmup(task, source, generation, cancellation);
    }

    /// <summary>Stops a stale/closed planned display-cache build without waiting on the UI thread.</summary>
    private void CancelPlannedTimeDistanceCacheWarmup(bool clearPublishedCache)
    {
        var task = _diagramPlannedCacheTask;
        var cancellation = _diagramPlannedCacheCancellation;
        _diagramPlannedCacheTask = null;
        _diagramPlannedCacheCancellation = null;
        _diagramPlannedCacheRequestSource = null;
        _diagramPlannedCacheRequestGeneration = Guid.Empty;
        _diagramPlannedCacheError = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The dispatcher completion may have won the publication race;
            // cancellation is already complete in that case.
        }
        if (task is null)
        {
            cancellation?.Dispose();
        }

        if (!clearPublishedCache)
        {
            return;
        }

        _diagramPlannedCache = new TimeDistanceTrajectoryCache();
        _diagramPlannedSource = null;
        _diagramLayoutKey = null;
        _diagramLastPlannedCount = -1;
    }

    private void ObservePlannedTimeDistanceCacheWarmup(
        Task<TimeDistanceTrajectoryCache> task,
        PlannedTimelineArtifact source,
        Guid generation,
        CancellationTokenSource cancellation)
    {
        _ = ObservePlannedTimeDistanceCacheWarmupAsync(
            task, source, generation, cancellation);
    }

    private async Task ObservePlannedTimeDistanceCacheWarmupAsync(
        Task<TimeDistanceTrajectoryCache> task,
        PlannedTimelineArtifact source,
        Guid generation,
        CancellationTokenSource cancellation)
    {
        TimeDistanceTrajectoryCache? cache = null;
        Exception? error = null;
        var cancelled = false;
        try
        {
            cache = await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception exception)
        {
            // Observe worker faults before dispatching the presentation state.
            error = exception;
        }

        try
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    CompletePlannedTimeDistanceCacheWarmup(
                        task, source, generation, cache, error, cancelled, cancellation);
                }
                catch (Exception exception)
                {
                    // A stale/closing presentation callback must not become an
                    // unhandled Dispatcher exception.
                    if (ReferenceEquals(_diagramPlannedCacheTask, task)
                        && ReferenceEquals(_diagramPlannedCacheCancellation, cancellation))
                    {
                        _diagramPlannedCacheTask = null;
                        _diagramPlannedCacheCancellation = null;
                        _diagramPlannedCacheRequestSource = null;
                        _diagramPlannedCacheRequestGeneration = Guid.Empty;
                        _diagramPlannedCacheError = exception;
                    }
                    System.Diagnostics.Trace.WriteLine(
                        $"Planned time-distance cache publication failed: {exception}");
                }
            }).Task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Dispatcher shutdown/abort is an expected lifecycle outcome.
            System.Diagnostics.Trace.WriteLine(
                $"Planned time-distance cache dispatcher callback failed: {exception}");
        }
        finally
        {
            // CompletePlannedTimeDistanceCacheWarmup clears a current or stale
            // request on the dispatcher before this cleanup runs.  Close/reset
            // also clear the field before cancelling the task.
            cancellation.Dispose();
        }
    }

    private bool CompletePlannedTimeDistanceCacheWarmup(
        Task<TimeDistanceTrajectoryCache> task,
        PlannedTimelineArtifact source,
        Guid generation,
        TimeDistanceTrajectoryCache? cache,
        Exception? error,
        bool cancelled,
        CancellationTokenSource cancellation)
    {
        // Both identity checks are required.  Reference equality protects
        // against a replaced artifact with equal contents; generation protects
        // against a late completion from an old playback worker.
        var ownsCurrentRequest = ReferenceEquals(_diagramPlannedCacheTask, task)
            && ReferenceEquals(_diagramPlannedCacheCancellation, cancellation);
        if (!ownsCurrentRequest)
        {
            return false;
        }

        var isCurrentArtifact = ReferenceEquals(_plannedTimelineArtifact, source)
            && ReferenceEquals(_diagramPlannedCacheRequestSource, source)
            && _diagramPlannedCacheRequestGeneration == generation
            && _playbackWorker?.GenerationId == generation;
        if (!isCurrentArtifact)
        {
            _diagramPlannedCacheTask = null;
            _diagramPlannedCacheCancellation = null;
            _diagramPlannedCacheRequestSource = null;
            _diagramPlannedCacheRequestGeneration = Guid.Empty;
            _diagramPlannedCacheError = null;
            _diagramLayoutKey = null;
            _diagramLastPlannedCount = -1;
            return true;
        }

        _diagramPlannedCacheTask = null;
        _diagramPlannedCacheCancellation = null;
        if (cancelled)
        {
            return true;
        }
        if (error is not null)
        {
            _diagramPlannedCacheError = error;
            PlaybackStatusText.Text =
                $"實際營運可播放；計畫運行圖預熱失敗：{error.Message}";
            return true;
        }

        if (cache is null)
        {
            _diagramPlannedCacheError = new InvalidOperationException(
                "計畫運行圖預熱未產生快取。");
            PlaybackStatusText.Text =
                "實際營運可播放；計畫運行圖預熱未產生快取。";
            return true;
        }

        _diagramPlannedCache = cache;
        _diagramPlannedSource = source;
        _diagramPlannedCacheError = null;
        _diagramLayoutKey = null;
        _diagramLastPlannedCount = -1;
        if (_latestPlaybackFrame is not null)
        {
            DrawTimeDistanceDiagramIfVisible();
        }
        return true;
    }

    /// <summary>
    /// Test/diagnostic seam: awaits and publishes the current planned display
    /// cache without depending on a timing race between worker completion and
    /// the dispatcher continuation.
    /// </summary>
    private async Task WaitForPlannedTimeDistanceCacheWarmupAsync()
    {
        var task = _diagramPlannedCacheTask;
        var source = _diagramPlannedCacheRequestSource;
        var generation = _diagramPlannedCacheRequestGeneration;
        var cancellation = _diagramPlannedCacheCancellation;
        if (task is null || source is null || cancellation is null)
        {
            return;
        }

        TimeDistanceTrajectoryCache? cache = null;
        Exception? error = null;
        try
        {
            cache = await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            error = exception;
        }

        // Diagnostic callers do not necessarily install a WPF synchronization
        // context. Publication must still use the owning dispatcher.
        await Dispatcher.InvokeAsync(() => CompletePlannedTimeDistanceCacheWarmup(
            task, source, generation, cache, error, cancelled: false,
            cancellation)).Task;
        cancellation.Dispose();
    }

    private void DrawTimeDistanceDiagram() => DrawTimeDistanceDiagramCore(inputToken: 0);

    private void DrawTimeDistanceDiagramCore(long inputToken)
    {
        if (_diagramDrawing || TimeDistanceCanvas is null || DiagramScrollViewer is null) return;
        _diagramDrawing = true;
        try
        {
            using (NativeAcceptanceMeasurePhase(inputToken, "playback.diagram.viewportSync"))
            {
                UpdateTimeDistanceVerticalZoom();
                SynchronizeDiagramTimeAxisViewport();
            }
            DrawInteractiveTimeDistanceDiagramCore(inputToken);
        }
        finally { _diagramDrawing = false; }
    }

    private void DrawTimeDistanceDiagramIfVisible()
    {
        if (WorkspaceTabControl?.SelectedItem == DiagramTabItem) DrawTimeDistanceDiagram();
    }

    private void DiagramScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, DiagramScrollViewer)) return;
        ScheduleTimeDistanceViewportRefresh();
    }

    private void DiagramScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, DiagramScrollViewer)) return;
        SynchronizeDiagramTimeAxisViewport();
        if (Math.Abs(e.ViewportWidthChange) < .01 && Math.Abs(e.ViewportHeightChange) < .01)
        {
            return;
        }

        ScheduleTimeDistanceViewportRefresh();
    }

    private void UpdateTimeDistanceVerticalZoom()
    {
        if (TimeDistanceCanvas is null || DiagramScrollViewer is null) return;
        var zoom = DiagramVerticalZoomSlider?.Value ?? 1;
        if (!double.IsFinite(zoom) || zoom <= 1) return;
        // ActualHeight does not change when a horizontal scrollbar appears.
        // Keep the two zoom controls independent, including scrollbar layout.
        var viewportHeight = DiagramScrollViewer.ActualHeight;
        var height = Math.Max(380, IsFiniteLayoutDimension(viewportHeight) ? viewportHeight : 380)
            * Math.Clamp(zoom, 1, 4);
        if (!double.IsFinite(TimeDistanceCanvas.Height)
            || Math.Abs(TimeDistanceCanvas.Height - height) > .1)
            TimeDistanceCanvas.Height = height;
    }

    private void SynchronizeDiagramTimeAxisViewport()
    {
        if (DiagramTimeAxisViewport is null || DiagramTimeAxisCanvas is null
            || DiagramScrollViewer is null) return;
        var width = DiagramScrollViewer.ViewportWidth;
        if (!IsFiniteLayoutDimension(width)) width = DiagramScrollViewer.ActualWidth;
        if (IsFiniteLayoutDimension(width)) DiagramTimeAxisViewport.Width = width;
        var offset = DiagramScrollViewer.HorizontalOffset;
        DiagramTimeAxisCanvas.RenderTransform = new TranslateTransform(
            double.IsFinite(offset) ? -offset : 0, 0);
    }

    private void DrawFixedDiagramTimeAxis(double width, double left, double plotWidth,
        double start, double duration)
    {
        DiagramTimeAxisCanvas.Children.Clear();
        DiagramTimeAxisCanvas.Width = width;
        AddCanvasText(DiagramTimeAxisCanvas, "時間", 8, 8, 10, Color.FromRgb(82, 93, 111));
        DiagramTimeAxisCanvas.Children.Add(new Line
        {
            X1 = left, X2 = left + plotWidth, Y1 = 0, Y2 = 0,
            Stroke = Brushes.Gainsboro
        });
        var previousX = double.NegativeInfinity;
        foreach (var time in GetDiagramTimeTicks(start, duration))
        {
            var x = left + plotWidth * (time - start) / duration;
            DiagramTimeAxisCanvas.Children.Add(new Line
            {
                X1 = x, X2 = x, Y1 = 0, Y2 = 5, Stroke = Brushes.Gray
            });
            AddDiagramTimeTickLabel(DiagramTimeAxisCanvas, time, x, previousX, start + duration, 8);
            previousX = x;
        }
        SynchronizeDiagramTimeAxisViewport();
    }

    private void ScheduleTimeDistanceViewportRefresh()
    {
        if (_diagramViewportRefreshQueued || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _diagramViewportRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _diagramViewportRefreshQueued = false;
            DrawTimeDistanceDiagramIfVisible();
        }));
    }

    /// <summary>
    /// Returns a finite canvas width even while a deferred viewport callback runs
    /// before the ScrollViewer has completed its first measure pass. WPF may report
    /// PositiveInfinity or a very large finite sentinel for an unmeasured viewport;
    /// allowing either value into the axis transform can produce invalid Polyline
    /// layout bounds.
    /// </summary>
    private double GetTimeDistanceCanvasWidth()
    {
        var viewportWidth = DiagramScrollViewer?.ViewportWidth ?? double.NaN;
        if (!IsFiniteLayoutDimension(viewportWidth))
        {
            viewportWidth = DiagramScrollViewer?.ActualWidth ?? double.NaN;
        }

        if (!IsFiniteLayoutDimension(viewportWidth))
        {
            viewportWidth = TimeDistanceCanvas?.ActualWidth ?? double.NaN;
        }

        if (!IsFiniteLayoutDimension(viewportWidth))
        {
            viewportWidth = TimeDistanceCanvas?.MinWidth ?? 760;
        }

        if (!IsFiniteLayoutDimension(viewportWidth))
        {
            viewportWidth = 760;
        }

        var zoom = DiagramZoomSlider?.Value ?? 1;
        zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 1, 4) : 1;
        var width = Math.Max(760, Math.Max(1, viewportWidth - 15) * zoom);
        return double.IsFinite(width) ? width : 760;
    }

    private void DrawInteractiveTimeDistanceDiagram() =>
        DrawInteractiveTimeDistanceDiagramCore(inputToken: 0);

    private void DrawInteractiveTimeDistanceDiagramCore(long inputToken)
    {
        var frame = _latestPlaybackFrame;
        if (frame is null || (_route is null && _activeTopologyProjectDocument is null))
        {
            DiagramTimeAxisCanvas.Children.Clear();
            if (_diagramLayoutKey != "empty")
            {
                TimeDistanceCanvas.Children.Clear();
                AddCanvasText(TimeDistanceCanvas, "建立並播放 V2 模擬後顯示時間－里程運行圖。", 22, 22, 13, Color.FromRgb(102, 112, 133));
                _diagramLayoutKey = "empty";
            }
            return;
        }
        using (NativeAcceptanceMeasurePhase(inputToken, "playback.diagram.incrementalData"))
        using (_playbackDiagnostics.Measure("TimeDistance.IncrementalData"))
        {
            var generationChanged = _diagramGeneration != frame.GenerationId;
            var actualTimelineReset = generationChanged || frame.SimulationTimeSeconds < _diagramLastTime;
            if (actualTimelineReset)
            {
                _diagramGeneration = frame.GenerationId;
                _diagramActualCache.Reset();
                _diagramLayoutKey = null;
                _diagramLastActualCount = -1;
                _diagramLastEventCount = -1;
            }
            _diagramLastTime = frame.SimulationTimeSeconds;
            _diagramActualCache.Append(frame.Trajectory);
            if (_plannedTimelineArtifact is { } plannedArtifact)
            {
                StartPlannedTimeDistanceCacheWarmup(plannedArtifact, frame.GenerationId);
            }
            else if (_diagramPlannedSource is not null
                     || _diagramPlannedCacheTask is not null
                     || _diagramPlannedCache.HasData)
            {
                CancelPlannedTimeDistanceCacheWarmup(clearPublishedCache: true);
            }
        }
        var showActual = ShowActualCheckBox.IsChecked == true;
        var showPlanned = ShowPlannedCheckBox.IsChecked == true;
        var showEvents = ShowEventsCheckBox?.IsChecked != false;
        var plannedCachePending = showPlanned
            && _plannedTimelineArtifact is not null
            && !_diagramPlannedCache.HasData
            && _diagramPlannedCacheError is null;
        var plannedCacheFailed = showPlanned
            && _plannedTimelineArtifact is not null
            && _diagramPlannedCacheError is not null;
        if ((!showActual || !_diagramActualCache.HasData) && (!showPlanned || !_diagramPlannedCache.HasData))
        {
            DiagramTimeAxisCanvas.Children.Clear();
            if (_diagramLayoutKey != "no-data")
            {
                TimeDistanceCanvas.Children.Clear();
                var message = plannedCachePending
                    ? "計畫／理論運行圖正在背景整理；完成後會自動顯示。"
                    : plannedCacheFailed
                        ? "計畫／理論運行圖整理失敗；可繼續查看實際運行圖。"
                        : "播放後即時建立運行圖；空圖不會啟動零列車模擬引擎。";
                AddCanvasText(TimeDistanceCanvas, message, 22, 22, 13, Color.FromRgb(102, 112, 133));
                _diagramLayoutKey = "no-data";
            }
            return;
        }
        var stations = _activeTopologyProjectDocument is null
            ? _route!.Stations.Select(s => (Id: s.StationId, Position: s.PositionMeters)).ToArray()
            : frame.GetTopologyResultContext().GetStops(TrainDirection.Outbound)
                .Select(s => (Id: s.StationId, Position: s.ProjectedChainageMeters)).ToArray();
        var tails = _activeTopologyProjectDocument is null ? GetAfterStationTailTrackVisualLayouts() : [];
        var min = stations.Min(s => s.Position);
        var max = stations.Max(s => s.Position);
        foreach (var tail in tails) { min = Math.Min(min, tail.Layout.VirtualNodePositionMeters); max = Math.Max(max, tail.Layout.VirtualNodePositionMeters); }
        foreach (var cache in new[] { showActual ? _diagramActualCache : null, showPlanned ? _diagramPlannedCache : null })
        {
            if (cache is null || cache.ProcessedCount == 0) continue;
            min = Math.Min(min, cache.MinPosition); max = Math.Max(max, cache.MaxPosition);
        }
        var available = Math.Max(1, Math.Max(showActual ? _diagramActualCache.MaxTime : 0, showPlanned ? _diagramPlannedCache.MaxTime : 0));
        var start = Math.Clamp(ParseDiagramMinute(DiagramStartMinuteTextBox, 0) * 60, 0, available);
        // With actual-only playback, keep a stable forward axis bucket rather than rescaling every frame.
        var end = string.IsNullOrWhiteSpace(DiagramEndMinuteTextBox.Text)
            ? showPlanned && _diagramPlannedCache.ProcessedCount > 0 ? available : Math.Ceiling(available / 600) * 600
            : Math.Clamp(ParseDiagramMinute(DiagramEndMinuteTextBox, available / 60) * 60, start + .1, Math.Max(start + .1, available));
        var width = GetTimeDistanceCanvasWidth();
        if (!double.IsFinite(TimeDistanceCanvas.Width) || Math.Abs(TimeDistanceCanvas.Width - width) > 1)
            TimeDistanceCanvas.Width = width;
        var measuredHeight = double.IsFinite(TimeDistanceCanvas.Height)
            ? TimeDistanceCanvas.Height : TimeDistanceCanvas.ActualHeight;
        var height = double.IsFinite(measuredHeight) ? Math.Max(380, measuredHeight) : 380;
        const double left = 82, top = 48;
        var pw = width - left - 22; var ph = height - top - 42;
        var span = Math.Max(1, max - min); var duration = Math.Max(.1, end - start);
        double X(double time) => left + (time - start) / duration * pw;
        double Y(double position) => top + ph - (position - min) / span * ph;
        var diagramScale = Math.Max(1, VisualTreeHelper.GetDpi(TimeDistanceCanvas).DpiScaleY);
        var direction = GetSelectedTag(DiagramDirectionComboBox);
        var vehicle = DiagramVehicleComboBox.SelectedItem?.ToString() ?? "全部";
        var key = FormattableString.Invariant($"{width:R}|{height:R}|{min:R}|{max:R}|{start:R}|{end:R}|{diagramScale:R}|{direction}|{vehicle}|{showActual}|{showPlanned}|{showEvents}|{frame.MovingBlockMode}|{_startClockSeconds:R}|{_diagramTimeTickIntervalMinutes:R}|{DiagramShowEndTimeCheckBox?.IsChecked}");
        var layoutChanged = key != _diagramLayoutKey;
        var eventsChanged = layoutChanged || _diagramLastEventCount != frame.Events.Count;
        if (!layoutChanged && _diagramLastActualCount == frame.Trajectory.Count
            && _diagramLastPlannedCount == (_plannedTimelineArtifact?.Trajectory.Length ?? 0)
            && _diagramLastEventCount == frame.Events.Count) return;
        _diagramLastActualCount = frame.Trajectory.Count;
        _diagramLastPlannedCount = _plannedTimelineArtifact?.Trajectory.Length ?? 0;
        _diagramLastEventCount = frame.Events.Count;
        if (layoutChanged)
        {
            using (NativeAcceptanceMeasurePhase(inputToken, "playback.diagram.layout"))
            using (_playbackDiagnostics.Measure("TimeDistance.StaticVisuals"))
            {
                TimeDistanceCanvas.Children.Clear(); _diagramSeries.Clear(); _diagramEventVisuals.Clear();
                _diagramLayoutKey = key; _diagramStaticBuilds++;
                _playbackDiagnostics.RecordCount("TimeDistance.StaticRebuildCount");
                DrawAxes(TimeDistanceCanvas, left, top, pw, ph, tails.Count == 0 ? "累積里程" : "累積里程（含尾軌）", "");
                DrawFixedDiagramTimeAxis(width, left, pw, start, duration);
                AddCanvasText(TimeDistanceCanvas, $"{_activeTopologyProjectDocument?.ProjectName ?? _route!.RouteName}｜計畫／理論與 V2 模擬實際運行圖｜{UiDisplayText.Enum(frame.MovingBlockMode)}｜固定時間步進 0.1 秒", left, 8, 14, Color.FromRgb(34, 43, 60));
                var cacheStatus = plannedCachePending
                    ? "　（計畫／理論線背景整理中）"
                    : plannedCacheFailed
                        ? "　（計畫／理論線整理失敗）"
                        : string.Empty;
                AddCanvasText(TimeDistanceCanvas, GetDiagramLegend(showEvents) + cacheStatus, left, 28, 10, Color.FromRgb(82, 93, 111));
                var labelSpecs = stations
                    .Select(s => new TimeDistanceStationLabelSpec(
                        s.Id,
                        $"{s.Id}  {s.Position / 1000:0.00} km",
                        Y(s.Position),
                        Color.FromRgb(82, 93, 111)))
                    .Concat(tails.Select(t => new TimeDistanceStationLabelSpec(
                        t.Layout.VirtualNodeId,
                        $"{t.Layout.VirtualNodeId}  {t.Layout.VirtualNodePositionMeters / 1000:0.00} km",
                        Y(t.Layout.VirtualNodePositionMeters),
                        Color.FromRgb(188, 92, 52),
                        IsTail: true)))
                    .ToArray();
                var labelPlacements = TimeDistanceStationLabelLayout.Arrange(
                    labelSpecs,
                    width,
                    height,
                    left,
                    topBoundary: Math.Max(0, top - 8),
                    bottomBoundary: height - 2,
                    preferredFontSize: 10,
                    scaleFactor: diagramScale);
                foreach (var placement in labelPlacements)
                {
                    TimeDistanceCanvas.Children.Add(new Line
                    {
                        X1 = left,
                        X2 = left + pw,
                        Y1 = placement.AnchorY,
                        Y2 = placement.AnchorY,
                        Stroke = placement.IsTail ? Brushes.Sienna : Brushes.Gainsboro,
                        StrokeDashArray = placement.IsTail ? [4, 3] : null
                    });
                    TimeDistanceCanvas.Children.Add(TimeDistanceStationLabelLayout.CreateLeaderLine(placement));
                    TimeDistanceCanvas.Children.Add(TimeDistanceStationLabelLayout.CreateTextBlock(placement));
                }
                foreach (var time in GetDiagramTimeTicks(start, duration))
                {
                    var x = X(time);
                    TimeDistanceCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + ph, Stroke = Brushes.Gainsboro });
                }
            }
        }
        using (NativeAcceptanceMeasurePhase(inputToken, "playback.diagram.series"))
        using (_playbackDiagnostics.Measure("TimeDistance.SeriesVisuals"))
        {
            if (showPlanned) DrawCache(_diagramPlannedCache, true);
            if (showActual) DrawCache(_diagramActualCache, false);
        }
        if (eventsChanged)
        using (NativeAcceptanceMeasurePhase(inputToken, "playback.diagram.events"))
        using (_playbackDiagnostics.Measure("TimeDistance.EventVisuals"))
        {
            var eventStationPositions = BuildDiagramEventStationPositions(frame);
            var index = 0;
            foreach (var ev in frame.Events.Where(e =>
                         showEvents && e.SimulationTimeSeconds >= start
                         && e.SimulationTimeSeconds <= end
                         && (direction == "All"
                             || direction == "Outbound" && e.Direction == TrainDirection.Outbound
                             || direction == "Inbound" && e.Direction == TrainDirection.Inbound)
                         && (string.IsNullOrWhiteSpace(vehicle)
                             || vehicle == "全部"
                             || e.VehicleId == vehicle)
                         && IsDiagramMarkerEvent(e.EventType)))
            {
                if (index == _diagramEventVisuals.Count)
                {
                    var marker = new Ellipse { Width = 8, Height = 8, Stroke = Brushes.White, StrokeThickness = 1 };
                    _diagramEventVisuals.Add(marker); TimeDistanceCanvas.Children.Add(marker);
                }
                var m = _diagramEventVisuals[index++];
                m.Visibility = Visibility.Visible;
                m.Fill = IsDiagramSafetyEvent(ev.EventType) ? DiagramSafetyBrush : IsDiagramTerminalEvent(ev.EventType) ? DiagramTerminalBrush : DiagramStationBrush;
                m.ToolTip = $"{TrajectoryAnalysis.FormatClock(_startClockSeconds + ev.SimulationTimeSeconds)}｜{EventTypeToChinese(ev.EventType)}\n{ev.Message}";
                Canvas.SetLeft(m, X(ev.SimulationTimeSeconds) - 4); Canvas.SetTop(m, Y(GetDiagramEventPosition(ev, eventStationPositions)) - 4);
            }
            for (var i = index; i < _diagramEventVisuals.Count; i++) _diagramEventVisuals[i].Visibility = Visibility.Collapsed;
        }
        _playbackDiagnostics.RecordCount("TimeDistance.VisualUpdateCount");

        void DrawCache(TimeDistanceTrajectoryCache cache, bool planned)
        {
            foreach (var s in cache.Groups)
            {
                if (vehicle != "全部" && s.VehicleId != vehicle || direction == "Outbound" && s.Direction != TrainDirection.Outbound || direction == "Inbound" && s.Direction != TrainDirection.Inbound) continue;
                var id = (planned, s.VehicleId, s.ServiceRunId, s.Direction);
                if (!_diagramSeries.TryGetValue(id, out var visual))
                {
                    var color = TrainColors[ParseVehicleIndex(s.VehicleId) % TrainColors.Length];
                    var line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = vehicle != "全部" ? 3.1 : planned ? 1.4 : 2.2, StrokeDashArray = planned ? [6, 4] : null, Opacity = planned ? .55 : .95, ToolTip = $"{s.VehicleId}｜{s.ServiceRunId}｜{DirectionToChinese(s.Direction)}" };
                    var label = new TextBlock { Text = ShortVehicle(s.VehicleId), FontSize = 9, Foreground = new SolidColorBrush(color) };
                    visual = new DiagramSeriesVisual(line, label); _diagramSeries.Add(id, visual);
                    TimeDistanceCanvas.Children.Add(line); TimeDistanceCanvas.Children.Add(label);
                }
                if (visual.Version == s.Version && !layoutChanged) continue;
                visual.Version = s.Version;
                var points = new PointCollection(); TrajectorySample? first = null;
                foreach (var sample in s.Samples)
                {
                    if (sample.SimulationTimeSeconds < start || sample.SimulationTimeSeconds > end) continue;
                    first ??= sample; points.Add(new Point(X(sample.SimulationTimeSeconds), Y(sample.PositionMeters)));
                }
                visual.Line.Points = points;
                visual.Label.Visibility = first is null ? Visibility.Collapsed : Visibility.Visible;
                if (first is not null) { Canvas.SetLeft(visual.Label, X(first.SimulationTimeSeconds) + 3); Canvas.SetTop(visual.Label, Y(first.PositionMeters) - 15); }
            }
        }
    }

    private static bool IsDiagramSafetyEvent(SimulationEventType type) => type is SimulationEventType.ObstacleEmergencyStop or SimulationEventType.PredictedCollision or SimulationEventType.Collision or SimulationEventType.SafetyStatusChanged;
    private static IReadOnlyDictionary<(TrainDirection Direction, string StationId), double> BuildDiagramEventStationPositions(PlaybackFrame frame)
    {
        var positions = new Dictionary<(TrainDirection Direction, string StationId), double>();
        if (frame.Context.TopologyResultContext is not { } context) return positions;
        foreach (var direction in new[] { TrainDirection.Outbound, TrainDirection.Inbound })
            foreach (var station in context.GetDisplayStations(direction))
                positions[(direction, station.StationId)] = station.PositionMeters;
        return positions;
    }

    private static double GetDiagramEventPosition(SimulationEvent simulationEvent,
        IReadOnlyDictionary<(TrainDirection Direction, string StationId), double> stationPositions)
    {
        // StationPassed carries a direction-local station position, not the train's
        // common-axis position. Keep its station semantics using the result adapter;
        // do not mirror other events or change the raw event/CSV data.
        return simulationEvent.EventType == SimulationEventType.StationPassed
            && simulationEvent.StationId is { } stationId
            && stationPositions.TryGetValue((simulationEvent.Direction, stationId), out var position)
                ? position : simulationEvent.PositionMeters;
    }

    private static string GetDiagramLegend(bool showEvents) =>
        "實線：V2 模擬實際　虛線：無干擾計畫／理論" + (showEvents
            ? "　綠點：車站　紫點：折返／尾軌／退出　紅點：安全／障礙"
            : "　（事件觸發點已隱藏）");
    private static bool IsDiagramTerminalEvent(SimulationEventType type) => type is SimulationEventType.TurnaroundStarted or SimulationEventType.TailTrackReached or SimulationEventType.TailTrackReturnStarted or SimulationEventType.DirectionChanged or SimulationEventType.ServiceEnded;
    private static bool IsDiagramMarkerEvent(SimulationEventType type) => IsDiagramSafetyEvent(type) || IsDiagramTerminalEvent(type) || type is SimulationEventType.Departure or SimulationEventType.Arrival or SimulationEventType.StationPassed or SimulationEventType.DepartureDelayed or SimulationEventType.WaitingForResource;
}
