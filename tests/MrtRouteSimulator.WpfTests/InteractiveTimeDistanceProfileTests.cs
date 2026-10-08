using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;
using Path = System.IO.Path;

/// <summary>
/// Opt-in, test-only profiler for the interactive time-distance renderer.
///
/// This deliberately calls the interactive draw seam directly while the Diagram tab is not
/// selected.  The explicit TimeDistanceRender scope is a test boundary; it is not a native input
/// owner or compositor measurement.  The normal application refresh path already owns a scope of
/// that name, so this runner must not invoke UpdateV2PlaybackView for its measured draws.
/// </summary>
internal static class InteractiveTimeDistanceProfileTests
{
    private const double TargetSimulationSeconds = 600;
    private const string TimeDistanceRenderBoundary =
        "test-only direct DrawTimeDistanceDiagram scope; not owner input or compositor timing";

    private static readonly string[] RequiredTimingNames =
    [
        PlaybackDiagnosticMetricNames.TimeDistanceRender,
        "TimeDistance.IncrementalData",
        "TimeDistance.StaticVisuals",
        "TimeDistance.SeriesVisuals",
        "TimeDistance.EventVisuals"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static void Run(string samplePath)
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.GetFullPath(samplePath)));
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));

            // Keep the Diagram tab unselected while both preparation stages and their deferred
            // publication callbacks settle.  The display-cache warmup is intentionally awaited as
            // a separate seam from the planned timeline task.
            SelectWorkspaceTab(window, "ResultsTabItem");
            WaitForPlannedTimelineOnly(window);
            WaitForPlannedTimeDistanceCacheWarmup(window);
            DrainDeferredDispatcherCallbacks(window);

            window.Show();
            PrepareDiagramWindow(window);
            ConfigureDiagramFilters(window);
            DrainDeferredDispatcherCallbacks(window);
            RequireCleanInteractivePresentation(window);

            var worker = GetWorker(window);
            WpfTestWait.Wait(worker.Ready);
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(TargetSimulationSeconds));
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var targetFrame = WpfTestWait.LatestFrame(window);
            Require(Math.Abs(targetFrame.SimulationTimeSeconds - TargetSimulationSeconds) < 1e-9,
                $"profile 必須鎖定 {TargetSimulationSeconds:0.0}s frame，實際為 {targetFrame.SimulationTimeSeconds:0.###}s。");

            // Prepare data outside the measured draw. Cold here means first visual creation
            // from the same already-prepared caches used by the three warm rebuilds.
            SetPrivateField(window, "_diagramGeneration", targetFrame.GenerationId);
            SetPrivateField(window, "_diagramLastTime", targetFrame.SimulationTimeSeconds);
            var actualCache = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramActualCache");
            actualCache.Append(targetFrame.Trajectory);
            Require(targetFrame.Trajectory.Count > 0 && targetFrame.Events.Count > 0
                    && actualCache.HasData
                    && GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache").HasData,
                "profile 必須有真實 actual/planned cache 與事件，不能以空資料通過。");
            RequireCleanInteractivePresentation(window);

            window.EnablePlaybackDiagnostics(true);
            var cold = MeasureDirectDraw(window, "cold-first-interactive", targetFrame);
            var warm = new List<MeasurementResult>();
            for (var index = 1; index <= 3; index++)
            {
                warm.Add(MeasureDirectDraw(window, $"warm-layout-rebuild-{index}", targetFrame));
            }

            foreach (var result in warm)
            {
                Console.WriteLine("INTERACTIVE_PROFILE_INPUT_CHECK=" + JsonSerializer.Serialize(
                    new { Phase = result.Phase, Cold = cold.Fingerprint.Input, Warm = result.Fingerprint.Input },
                    JsonOptions));
                RequireEquivalentData(cold, result);
            }

            var report = new
            {
                SamplePath = Path.GetFullPath(samplePath),
                TargetSimulationSeconds,
                TimeDistanceRenderBoundary,
                NoForcedGc = true,
                SameFrameAndCacheDataAcrossWarmSamples = true,
                Cold = cold,
                Warm = warm
            };
            Console.WriteLine("INTERACTIVE_TIME_DISTANCE_PROFILE_JSON="
                + JsonSerializer.Serialize(report, JsonOptions));
        }
        finally
        {
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static MeasurementResult MeasureDirectDraw(
        MainWindow window,
        string phase,
        PlaybackFrame expectedFrame)
    {
        SetPrivateField(window, "_diagramLayoutKey", null);
        var actualCache = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramActualCache");
        var plannedCache = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache");
        var actualDataBefore = CaptureCache(actualCache);
        var plannedDataBefore = CaptureCache(plannedCache);
        window.ResetPlaybackDiagnostics();
        var measurement = window.PlaybackDiagnostics.BeginMeasurement();
        using (window.PlaybackDiagnostics.Measure(PlaybackDiagnosticMetricNames.TimeDistanceRender))
        {
            WpfTestWait.Invoke(window, "DrawTimeDistanceDiagram");
        }

        // A publication callback must not turn one requested draw into several measured draws.
        DrainDeferredDispatcherCallbacks(window);
        var runtime = measurement.Complete();
        var snapshot = window.PlaybackDiagnostics.Snapshot(runtime);
        RequireSingleTimingSample(snapshot, phase);

        var actualFrame = WpfTestWait.LatestFrame(window);
        var fingerprint = CaptureFingerprint(window, actualFrame);
        RequireSameFrame(expectedFrame, actualFrame, phase);
        Require(ReferenceEquals(actualCache, WpfTestWait.Field(window, "_diagramActualCache"))
                && ReferenceEquals(plannedCache, WpfTestWait.Field(window, "_diagramPlannedCache"))
                && JsonSerializer.Serialize(actualDataBefore, JsonOptions)
                    == JsonSerializer.Serialize(fingerprint.ActualCache, JsonOptions)
                && JsonSerializer.Serialize(plannedDataBefore, JsonOptions)
                    == JsonSerializer.Serialize(fingerprint.PlannedCache, JsonOptions),
            $"{phase} 必須沿用量測前已預備好的相同 cache，不得重建或新增資料。");
        Require(fingerprint.DiagramGenerationId == expectedFrame.GenerationId,
            $"{phase} 的 diagram generation 與 target frame 不一致。");
        Require(fingerprint.VisualSeries.Count > 0, $"{phase} 必須產生非空列車曲線。");
        return new MeasurementResult(phase, snapshot, fingerprint);
    }

    private static void RequireSingleTimingSample(
        PlaybackDiagnosticsSnapshot snapshot,
        string phase)
    {
        foreach (var name in RequiredTimingNames)
        {
            var count = snapshot.GetTiming(name)?.Count ?? 0;
            Require(count == 1,
                $"{phase} 的 {name} timing count 必須為1，實際為{count}；不可把 deferred draw 合併為同一筆。");
        }
    }

    private static void RequireEquivalentData(
        MeasurementResult expected,
        MeasurementResult actual)
    {
        Require(JsonSerializer.Serialize(expected.Fingerprint.Input, JsonOptions)
                == JsonSerializer.Serialize(actual.Fingerprint.Input, JsonOptions),
            $"{actual.Phase} 改變了 viewport／filters／clock 或 frame source。");
        Require(expected.Fingerprint.GenerationId == actual.Fingerprint.GenerationId
                && expected.Fingerprint.FrameDigest == actual.Fingerprint.FrameDigest
                && expected.Fingerprint.TrajectoryCount == actual.Fingerprint.TrajectoryCount
                && expected.Fingerprint.EventCount == actual.Fingerprint.EventCount,
            $"{actual.Phase} 沒有使用與 cold 相同的 frame/generation/source。");
        Require(JsonSerializer.Serialize(expected.Fingerprint.ActualCache, JsonOptions)
                == JsonSerializer.Serialize(actual.Fingerprint.ActualCache, JsonOptions)
                && JsonSerializer.Serialize(expected.Fingerprint.PlannedCache, JsonOptions)
                    == JsonSerializer.Serialize(actual.Fingerprint.PlannedCache, JsonOptions),
            $"{actual.Phase} 改變了 actual/planned cache generation、counts 或 endpoints。");
        Require(JsonSerializer.Serialize(expected.Fingerprint.VisualSeries, JsonOptions)
                == JsonSerializer.Serialize(actual.Fingerprint.VisualSeries, JsonOptions),
            $"{actual.Phase} 改變了 interactive series first/last endpoints。");
    }

    private static void RequireSameFrame(
        PlaybackFrame expected,
        PlaybackFrame actual,
        string phase)
    {
        Require(ReferenceEquals(expected, actual)
                && expected.GenerationId == actual.GenerationId
                && expected.Sequence == actual.Sequence
                && Math.Abs(expected.SimulationTimeSeconds - actual.SimulationTimeSeconds) < 1e-9
                && expected.Trajectory.Count == actual.Trajectory.Count
                && expected.Events.Count == actual.Events.Count
                && Digest(new { expected.Trajectory, expected.Events })
                    == Digest(new { actual.Trajectory, actual.Events }),
            $"{phase} 的 frame source 已變更。");
    }

    private static ProfileFingerprint CaptureFingerprint(MainWindow window, PlaybackFrame frame)
    {
        var canvas = Find<Canvas>(window, "TimeDistanceCanvas");
        var viewer = Find<ScrollViewer>(window, "DiagramScrollViewer");
        var actual = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramActualCache");
        var planned = GetRequiredField<TimeDistanceTrajectoryCache>(window, "_diagramPlannedCache");
        var dpi = VisualTreeHelper.GetDpi(canvas);
        var input = new InputFingerprint(
            frame.GenerationId,
            frame.Sequence,
            frame.SimulationTimeSeconds,
            Digest(new { frame.Trajectory, frame.Events }),
            canvas.Width,
            canvas.Height,
            canvas.ActualWidth,
            canvas.ActualHeight,
            viewer.ActualWidth,
            viewer.ActualHeight,
            viewer.ViewportWidth,
            viewer.ViewportHeight,
            Find<Slider>(window, "DiagramZoomSlider").Value,
            Find<Slider>(window, "DiagramVerticalZoomSlider").Value,
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            ReadInterfaceScale(),
            Find<CheckBox>(window, "ShowActualCheckBox").IsChecked == true,
            Find<CheckBox>(window, "ShowPlannedCheckBox").IsChecked == true,
            Find<CheckBox>(window, "ShowEventsCheckBox").IsChecked != false,
            ReadCombo(window, "DiagramDirectionComboBox"),
            ReadCombo(window, "DiagramVehicleComboBox"),
            Find<TextBox>(window, "DiagramStartMinuteTextBox").Text,
            Find<TextBox>(window, "DiagramEndMinuteTextBox").Text,
            Find<ComboBox>(window, "DiagramTimeTickComboBox").Text,
            Find<CheckBox>(window, "DiagramShowEndTimeCheckBox").IsChecked == true,
            Convert.ToDouble(WpfTestWait.Field(window, "_startClockSeconds")),
            Convert.ToDouble(WpfTestWait.Field(window, "_diagramTimeTickIntervalMinutes")),
            frame.MovingBlockMode);

        var series = canvas.Children.OfType<Polyline>()
            .Where(line => line.Points.Count > 0)
            .Select(line => new VisualSeriesFingerprint(
                line.ToolTip?.ToString() ?? string.Empty,
                line.Points.Count,
                ToPoint(line.Points[0]),
                ToPoint(line.Points[^1])))
            .ToArray();
        return new ProfileFingerprint(
            frame.GenerationId,
            frame.Sequence,
            frame.SimulationTimeSeconds,
            frame.Trajectory.Count,
            frame.Events.Count,
            input.FrameDigest,
            ReadGuid(window, "_diagramGeneration"),
            CaptureCache(actual),
            CaptureCache(planned),
            series,
            input);
    }

    private static CacheFingerprint CaptureCache(TimeDistanceTrajectoryCache cache)
    {
        var groups = cache.Groups.Select(group => new
        {
            group.VehicleId,
            group.ServiceRunId,
            group.Direction,
            group.SourceSampleCount,
            group.DisplayPointCount,
            group.CriticalPointCount,
            group.OrdinaryPointCount,
            Samples = group.Samples
        }).ToArray();
        var endpoints = cache.Groups.Select(group => new CacheSeriesFingerprint(
            group.VehicleId,
            group.ServiceRunId,
            group.Direction,
            group.SourceSampleCount,
            group.DisplayPointCount,
            group.Samples.Count == 0 ? null : ToSample(group.Samples[0]),
            group.Samples.Count == 0 ? null : ToSample(group.Samples[^1]))).ToArray();
        return new CacheFingerprint(
            cache.Version,
            cache.ProcessedCount,
            cache.Groups.Count,
            cache.MinPosition,
            cache.MaxPosition,
            cache.MinTime,
            cache.MaxTime,
            Digest(groups),
            endpoints);
    }

    private static void WaitForPlannedTimelineOnly(MainWindow window)
    {
        if (WpfTestWait.Field(window, "_plannedTimelineTask") is Task task)
        {
            WpfTestWait.Wait(task);
        }

        WpfTestWait.Invoke(window, "ApplyCompletedPlannedTimeline");
    }

    private static void WaitForPlannedTimeDistanceCacheWarmup(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod(
            "WaitForPlannedTimeDistanceCacheWarmupAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 planned display-cache warmup diagnostic seam。");
        if (method.Invoke(window, null) is Task task)
        {
            WpfTestWait.Wait(task);
        }
    }

    private static void RequireCleanInteractivePresentation(MainWindow window)
    {
        var staticBuilds = Convert.ToInt64(WpfTestWait.Field(window, "_diagramStaticBuilds"));
        var series = WpfTestWait.Field(window, "_diagramSeries") as ICollection;
        Require(staticBuilds == 0 && series?.Count == 0,
            $"首次 interactive draw 前必須沒有既有 Diagram presentation：staticBuilds={staticBuilds}, series={series?.Count ?? -1}。");
    }

    private static void PrepareDiagramWindow(MainWindow window)
    {
        var canvas = Find<Canvas>(window, "TimeDistanceCanvas");
        var viewer = Find<ScrollViewer>(window, "DiagramScrollViewer");
        // This tab stays hidden to preserve first visual creation. Explicitly arrange
        // its viewport so width cannot fall back to (and shrink with) the canvas.
        viewer.Width = 900;
        viewer.Height = 420;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
        canvas.Width = 900;
        canvas.Height = 420;
        canvas.Measure(new Size(900, 420));
        canvas.Arrange(new Rect(0, 0, 900, 420));
        viewer.Measure(new Size(900, 420));
        viewer.Arrange(new Rect(0, 0, 900, 420));
        window.UpdateLayout();
    }

    private static void ConfigureDiagramFilters(MainWindow window)
    {
        Find<CheckBox>(window, "ShowActualCheckBox").IsChecked = true;
        Find<CheckBox>(window, "ShowPlannedCheckBox").IsChecked = true;
        Find<CheckBox>(window, "ShowEventsCheckBox").IsChecked = true;
        Find<ComboBox>(window, "DiagramDirectionComboBox").SelectedIndex = 0;
        Find<ComboBox>(window, "DiagramVehicleComboBox").SelectedItem = null;
        Find<TextBox>(window, "DiagramStartMinuteTextBox").Text = "0";
        Find<TextBox>(window, "DiagramEndMinuteTextBox").Text = string.Empty;
        Find<ComboBox>(window, "DiagramTimeTickComboBox").Text = "自動";
        Find<CheckBox>(window, "DiagramShowEndTimeCheckBox").IsChecked = true;
    }

    private static void SelectWorkspaceTab(MainWindow window, string name)
    {
        var workspace = Find<TabControl>(window, "WorkspaceTabControl");
        workspace.SelectedItem = window.FindName(name);
    }

    private static void DrainDeferredDispatcherCallbacks(MainWindow window)
    {
        for (var index = 0; index < 3; index++)
        {
            window.Dispatcher.Invoke(
                DispatcherPriority.ContextIdle,
                new Action(() => { }));
        }
    }

    private static SimulationPlaybackWorker GetWorker(MainWindow window) =>
        (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("未建立播放 worker。"));

    private static T Find<T>(MainWindow window, string name) where T : class =>
        (window.FindName(name) as T)
        ?? throw new InvalidOperationException($"找不到 WPF 元件 {name}。");

    private static string ReadCombo(MainWindow window, string name)
    {
        var combo = Find<ComboBox>(window, name);
        return $"index={combo.SelectedIndex};item={combo.SelectedItem};text={combo.Text}";
    }

    private static double ReadInterfaceScale() => Convert.ToDouble(
        typeof(MainWindow).Assembly.GetType("MrtRouteSimulator.App.InterfaceScaleService")!
            .GetProperty("CurrentScale", BindingFlags.Static | BindingFlags.Public)!.GetValue(null));

    private static T GetRequiredField<T>(MainWindow window, string name) where T : class =>
        (WpfTestWait.Field(window, name) as T)
        ?? throw new InvalidOperationException($"找不到 private field {name}。");

    private static Guid ReadGuid(MainWindow window, string name) =>
        (Guid)(WpfTestWait.Field(window, name)
            ?? throw new InvalidOperationException($"找不到 private field {name}。"));

    private static void SetPrivateField(MainWindow window, string name, object? value)
    {
        var field = typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到 private field {name}。");
        field.SetValue(window, value);
    }

    private static PointFingerprint ToPoint(Point point) => new(point.X, point.Y);

    private static SampleFingerprint ToSample(TrajectorySample sample) => new(
        sample.SimulationTimeSeconds,
        sample.VehicleId,
        sample.ServiceRunId,
        sample.Direction,
        sample.PositionMeters,
        sample.SpeedMetersPerSecond);

    private static string Digest<T>(T value) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions)));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record MeasurementResult(
        string Phase,
        PlaybackDiagnosticsSnapshot Diagnostics,
        ProfileFingerprint Fingerprint);

    private sealed record ProfileFingerprint(
        Guid GenerationId,
        long FrameSequence,
        double SimulationTimeSeconds,
        int TrajectoryCount,
        int EventCount,
        string FrameDigest,
        Guid DiagramGenerationId,
        CacheFingerprint ActualCache,
        CacheFingerprint PlannedCache,
        IReadOnlyList<VisualSeriesFingerprint> VisualSeries,
        InputFingerprint Input);

    private sealed record InputFingerprint(
        Guid GenerationId,
        long FrameSequence,
        double SimulationTimeSeconds,
        string FrameDigest,
        double CanvasWidth,
        double CanvasHeight,
        double CanvasActualWidth,
        double CanvasActualHeight,
        double ViewerActualWidth,
        double ViewerActualHeight,
        double ViewportWidth,
        double ViewportHeight,
        double HorizontalZoom,
        double VerticalZoom,
        double DpiScaleX,
        double DpiScaleY,
        double InterfaceScale,
        bool ShowActual,
        bool ShowPlanned,
        bool ShowEvents,
        string DirectionFilter,
        string VehicleFilter,
        string StartMinute,
        string EndMinute,
        string TickText,
        bool ShowEndTime,
        double StartClockSeconds,
        double TickIntervalMinutes,
        MovingBlockMode MovingBlockMode);

    private sealed record CacheFingerprint(
        long Version,
        int ProcessedCount,
        int GroupCount,
        double MinPosition,
        double MaxPosition,
        double MinTime,
        double MaxTime,
        string Digest,
        IReadOnlyList<CacheSeriesFingerprint> Endpoints);

    private sealed record CacheSeriesFingerprint(
        string VehicleId,
        string ServiceRunId,
        TrainDirection Direction,
        int SourceSampleCount,
        int DisplayPointCount,
        SampleFingerprint? First,
        SampleFingerprint? Last);

    private sealed record SampleFingerprint(
        double SimulationTimeSeconds,
        string VehicleId,
        string ServiceRunId,
        TrainDirection Direction,
        double PositionMeters,
        double SpeedMetersPerSecond);

    private sealed record VisualSeriesFingerprint(
        string ToolTip,
        int PointCount,
        PointFingerprint First,
        PointFingerprint Last);

    private sealed record PointFingerprint(double X, double Y);
}
