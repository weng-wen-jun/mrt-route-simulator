using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Offscreen coverage for the opt-in App-boundary native-acceptance recorder. This does not
/// automate a control and does not claim native OS injection or compositor-presentation timing.
/// </summary>
internal static class NativeAcceptanceInputTests
{
    public static void Run(string root)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "MrtRouteSimulator.NativeAcceptanceInputTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var window = new MainWindow();
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));

            var initial = WpfTestWait.LatestFrame(window);
            var disabled = window.NativeAcceptanceReadInputDiagnostics();
            Require(!disabled.IsActive, "native acceptance input recorder must default off");
            Require(!disabled.HwndSourceHookInstalled, "default-off recorder must not install HwndSource hook");
            Require(WpfTestWait.Field(window, "_nativeAcceptancePreviewMouseDownHandler") is null,
                "default-off recorder must not install routed handlers");

            InvokeStart(window, outputDirectory);
            var active = window.NativeAcceptanceReadInputDiagnostics();
            Require(active.IsActive, "Start must arm native acceptance input recorder");
            Require(WpfTestWait.Field(window, "_nativeAcceptancePreviewMouseDownHandler") is not null,
                "armed recorder must register routed handlers");
            Require(SameFrame(initial, WpfTestWait.LatestFrame(window)),
                "arming diagnostics must not advance or replace the playback frame");

            var syntheticToken = window.NativeAcceptanceTriggerInputActionForTest("pause");
            Require(syntheticToken > 0, "diagnostics-only input trigger must return a token while armed");
            var afterSynthetic = window.NativeAcceptanceReadInputDiagnostics();
            Require(afterSynthetic.TotalActionCount >= 1
                    && afterSynthetic.HandlerEndedActionCount >= 1
                    && afterSynthetic.ApplicationVisualUpdateCount >= 1,
                "synthetic action must record handler and application-update boundaries");

            var explicitToken = window.NativeAcceptanceBeginInputAction("resume", "test-owner-boundary");
            window.NativeAcceptanceInputHandlerStarted(explicitToken);
            window.NativeAcceptanceInputHandlerEnded(explicitToken, "test-owner-end");
            window.NativeAcceptanceApplicationVisualUpdate(explicitToken, "test-owner-visual");

            var wheelCountBefore = window.NativeAcceptanceReadInputDiagnostics().TotalActionCount;
            WpfTestWait.Invoke(window, "NativeAcceptancePreviewMouseWheel",
                new object(), new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120));
            WpfTestWait.Invoke(window, "NativeAcceptancePreviewMouseWheel",
                new object(), new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120));
            Require(window.NativeAcceptanceReadInputDiagnostics().TotalActionCount >= wheelCountBefore + 2,
                "repeated physical scroll previews must create distinct action records");

            // Advance through the ordinary worker/frame path only to populate the existing
            // presentation cache. Leave the frame pending so the real WorkspaceTabControl owner
            // supplies the input token to UpdateV2PlaybackViewCore and records its phases.
            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("playback worker is unavailable."));
            WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(0.2));
            typeof(MainWindow).GetField("_actualSummaryDirty", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, true);
            var diagramTab = (TabItem)(window.FindName("DiagramTabItem")
                ?? throw new InvalidOperationException("DiagramTabItem field is unavailable."));
            var workspaceTabs = (TabControl)(window.FindName("WorkspaceTabControl")
                ?? throw new InvalidOperationException("WorkspaceTabControl field is unavailable."));
            workspaceTabs.SelectedItem = diagramTab;
            var simulationTab = (TabItem)(window.FindName("SimulationTabItem")
                ?? throw new InvalidOperationException("SimulationTabItem field is unavailable."));
            workspaceTabs.SelectedItem = simulationTab;
            WpfTestWait.Invoke(window, "UpdateV2PlaybackView", true);
            var actualCache = (TimeDistanceTrajectoryCache)(WpfTestWait.Field(window, "_diagramActualCache")
                ?? throw new InvalidOperationException("actual diagram cache is unavailable."));
            Require(actualCache.ProcessedCount > 0,
                "normal frame application should populate the existing diagram cache");

            // Restore the original visible-diagram Reset scenario after the clean-summary
            // boundary above; hidden diagram generations are refreshed lazily on revisit.
            workspaceTabs.SelectedItem = diagramTab;
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(window, "ResetPlaybackAsync"));
            Require(actualCache.ProcessedCount == 0 && actualCache.Groups.Count == 0,
                "reset must clear the existing presentation cache generation");

            // Exercise the normal Play path only to create a real active run for the existing
            // DispatcherTimer probe. Stop the probe timer and set its prior timestamp explicitly
            // so this test is deterministic and does not sleep for an unexplained stall. Play_Click
            // is async void, so wait for its actual worker acknowledgement rather than assuming a
            // fixed wall delay is sufficient on a busy dispatcher.
            WpfTestWait.Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            var session = WpfTestWait.Field(window, "_nativeAcceptanceSession")
                ?? throw new InvalidOperationException("armed session was lost during normal Play");
            WaitForNormalPlayAcknowledgement(window, session);
            var probeTimer = (System.Windows.Threading.DispatcherTimer?)WpfTestWait.Field(
                window, "_nativeAcceptanceProbeTimer");
            probeTimer?.Stop();
            var run = GetAcceptanceRun(session)
                ?? throw new InvalidOperationException("normal Play did not create an acceptance run");
            Require((bool)(run.GetType().GetProperty("IsPlaying")?.GetValue(run) ?? false),
                "normal Play must leave the acceptance run playing for stall probe coverage");
            var previousProbe = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * 0.2);
            session.GetType().GetProperty("LastProbeTimestamp", BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(session, previousProbe);
            WpfTestWait.Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
            var afterStall = window.NativeAcceptanceReadInputDiagnostics();
            Require(afterStall.StallCount >= 1,
                "probe gap over 100 ms must increment the per-occurrence stall counter");

            var stopTask = window.StopNativeAcceptanceMeasurementAsync();
            WpfTestWait.Wait(stopTask);
            var stopped = window.NativeAcceptanceReadInputDiagnostics();
            Require(!stopped.IsActive && !stopped.HwndSourceHookInstalled,
                "Stop must disable diagnostics and remove HwndSource hook");
            Require(WpfTestWait.Field(window, "_nativeAcceptancePreviewMouseDownHandler") is null,
                "Stop must remove routed input handlers");

            var reportPath = window.NativeAcceptanceReportPath
                ?? throw new InvalidOperationException("native acceptance report path is missing");
            VerifyReport(reportPath);
            Console.WriteLine("[通過] NativeAcceptanceInput：opt-in、physics parity、action boundaries、phase timings、stall context、cache reset");
        }
        finally
        {
            try
            {
                if (window.IsNativeAcceptanceMeasurementActive)
                {
                    WpfTestWait.Wait(window.StopNativeAcceptanceMeasurementAsync());
                }
            }
            finally
            {
                WpfTestWait.Close(window);
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }
    }

    private static void InvokeStart(MainWindow window, string outputDirectory)
    {
        window.StartNativeAcceptanceMeasurement(outputDirectory);
        Require(window.IsNativeAcceptanceMeasurementActive,
            "StartNativeAcceptanceMeasurement did not arm the session");
    }

    private static void WaitForNormalPlayAcknowledgement(MainWindow window, object session)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 5);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var run = GetAcceptanceRun(session);
            var runPlaying = run?.GetType().GetProperty("IsPlaying", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(run) is true;
            var appPlaying = WpfTestWait.Field(window, "_isV2PlaybackPlaying") is true;
            if (runPlaying && appPlaying)
            {
                return;
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        var timedOutRun = GetAcceptanceRun(session);
        var runState = timedOutRun is null
            ? "null"
            : $"IsPlaying={ReadBoolProperty(timedOutRun, "IsPlaying")}, "
                + $"Completed={ReadBoolProperty(timedOutRun, "Completed")}, "
                + $"HasPause={ReadBoolProperty(timedOutRun, "HasPause")}";
        var worker = WpfTestWait.Field(window, "_playbackWorker");
        var completion = worker?.GetType().GetProperty("Completion", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(worker) as Task;
        var status = (window.FindName("PlaybackStatusText") as TextBlock)?.Text
            ?? "<PlaybackStatusText unavailable>";
        var v2Playing = WpfTestWait.Field(window, "_isV2PlaybackPlaying") is true;
        var workerState = completion is null
            ? "worker=null/completion-unavailable"
            : $"completion={completion.Status}, fault={completion.Exception?.GetBaseException().Message ?? "none"}";
        throw new InvalidOperationException(
            "normal Play acknowledgement timed out after 5 s; "
            + $"_isV2PlaybackPlaying={v2Playing}, run={runState}, status='{status}', {workerState}");
    }

    private static object? GetAcceptanceRun(object session) =>
        session.GetType().GetProperty("Run", BindingFlags.Instance | BindingFlags.Public)?.GetValue(session);

    private static bool ReadBoolProperty(object instance, string name) =>
        instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance) is true;

    private static void VerifyReport(string path)
    {
        Require(File.Exists(path), $"native acceptance report does not exist: {path}");
        var records = File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            })
            .ToArray();

        Require(records.Any(record => GetString(record, "type") == "inputAction"
            && GetString(record, "phase") == "handlerStart"),
            "report must include handlerStart boundary");
        Require(records.Any(record => GetString(record, "type") == "inputAction"
            && GetString(record, "phase") == "handlerEnd"),
            "report must include handlerEnd boundary");
        Require(records.Any(record => GetString(record, "type") == "inputAction"
            && GetString(record, "phase") == "applicationVisualUpdate"),
            "report must include owner application visual-update boundary");

        var playEnd = records.FirstOrDefault(record => GetString(record, "type") == "inputAction"
            && GetString(record, "phase") == "handlerEnd"
            && GetString(record, "action") == "play");
        Require(playEnd.ValueKind == JsonValueKind.Object,
            "report must include the normal Play handler-end boundary");
        foreach (var phase in new[]
        {
            "play.beforeDispatch",
            "play.workerAwait",
            "play.afterAcknowledged",
            "play.diagnosticsEnable",
            "play.diagnosticsReset",
            "play.beforePlayMemoryCheckpoint",
            "play.runArmedViewport",
            "play.runArmedWrite",
            "play.runtimeBaseline",
            "play.runStartedViewport",
            "play.runStartedWrite",
            "play.statusUpdate"
        })
        {
            RequirePhaseTiming(playEnd, phase);
        }

        var resetEnd = records.FirstOrDefault(record => GetString(record, "type") == "inputAction"
            && GetString(record, "phase") == "handlerEnd"
            && GetString(record, "action") == "reset");
        Require(resetEnd.ValueKind == JsonValueKind.Object,
            "report must include the normal Reset handler-end boundary");
        foreach (var phase in new[]
        {
            "reset.lifecycleBoundary",
            "reset.workerAwait",
            "reset.uiApply",
            "reset.statusUpdate"
        })
        {
            RequirePhaseTiming(resetEnd, phase);
        }

        var playbackRefreshEnds = records.Where(record =>
            GetString(record, "type") == "inputAction"
            && GetString(record, "phase") == "handlerEnd"
            && FindObject(record, "phaseTimingsMilliseconds") is { } timings
            && FindValue(timings, "playback.frameRead") is not null)
            .ToArray();
        var dirtyPlaybackRefreshEnd = playbackRefreshEnds.FirstOrDefault(record =>
            FindObject(record, "phaseTimingsMilliseconds") is { } timings
            && FindValue(timings, "playback.summary") is not null);
        Require(dirtyPlaybackRefreshEnd.ValueKind == JsonValueKind.Object,
            "report must include a dirty WorkspaceTabControl playback-refresh handler-end boundary");
        foreach (var phase in new[]
        {
            "playback.frameRead",
            "playback.accumulator",
            "playback.trainRows",
            "playback.eventRows",
            "playback.selectedChart",
            "playback.summary"
        })
        {
            RequirePhaseTiming(dirtyPlaybackRefreshEnd, phase);
        }
        // These two phases always execute for this valid-frame Diagram owner scenario.
        RequirePhaseTiming(dirtyPlaybackRefreshEnd, "playback.diagram.viewportSync");
        RequirePhaseTiming(dirtyPlaybackRefreshEnd, "playback.diagram.incrementalData");
        foreach (var phase in new[]
        {
            "playback.diagram.layout",
            "playback.diagram.series",
            "playback.diagram.events"
        })
        {
            RequireOptionalPhaseTiming(dirtyPlaybackRefreshEnd, phase);
        }

        var cleanPlaybackRefreshEnd = playbackRefreshEnds.LastOrDefault(record =>
            FindObject(record, "phaseTimingsMilliseconds") is { } timings
            && FindValue(timings, "playback.summary") is null);
        Require(cleanPlaybackRefreshEnd.ValueKind == JsonValueKind.Object,
            "report must include a clean WorkspaceTabControl playback-refresh handler-end boundary");
        Require(FindObject(cleanPlaybackRefreshEnd, "phaseTimingsMilliseconds") is { } cleanTimings
                && FindValue(cleanTimings, "playback.summary") is null,
            "clean playback refresh must omit the summary phase");

        var scrollIds = records
            .Where(record => GetString(record, "type") == "inputAction"
                && GetString(record, "phase") == "inputReceived"
                && GetString(record, "action") == "scroll")
            .Select(record => FindValue(record, "actionId")?.GetInt64())
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        Require(scrollIds.Length >= 2,
            "report must preserve distinct action IDs for repeated scroll previews");

        var stall = records.FirstOrDefault(record => GetString(record, "type") == "dispatcherStall");
        Require(stall.ValueKind == JsonValueKind.Object,
            "report must include a dispatcherStall record");
        Require(FindValue(stall, "occurrence") is { ValueKind: JsonValueKind.Number }
                && FindValue(stall, "activeTab") is { ValueKind: JsonValueKind.Object }
                && FindValue(stall, "recentInputAction") is { ValueKind: JsonValueKind.String }
                && FindValue(stall, "runtime") is { ValueKind: JsonValueKind.Object },
            "dispatcherStall must include occurrence/page/recent-action/runtime context");

        var reset = records.FirstOrDefault(record => GetString(record, "type") == "memoryCheckpoint"
            && GetString(record, "reason") == "afterReset");
        Require(reset.ValueKind == JsonValueKind.Object,
            "report must include an afterReset memory checkpoint");
        var memory = FindObject(reset, "memory");
        Require(memory is { } memoryObject
                && FindValue(memoryObject, "privateBytes") is { ValueKind: JsonValueKind.Number }
                && FindValue(memoryObject, "heapBytes") is { ValueKind: JsonValueKind.Number }
                && FindValue(memoryObject, "allocGc") is { ValueKind: JsonValueKind.Object }
                && FindValue(memoryObject, "frameHistoryCounts") is not null
                && FindValue(memoryObject, "displayCache") is { ValueKind: JsonValueKind.Object }
                && FindValue(memoryObject, "wpfVisuals") is { ValueKind: JsonValueKind.Object },
            "memory checkpoint must include process/GC/frame/cache/WPF visual metrics");
    }

    private static bool SameFrame(PlaybackFrame first, PlaybackFrame second) =>
        first.GenerationId == second.GenerationId
        && first.Sequence == second.Sequence
        && first.SimulationTimeSeconds == second.SimulationTimeSeconds;

    private static string? GetString(JsonElement element, string name) =>
        FindValue(element, name) is { ValueKind: JsonValueKind.String } value
            ? value.GetString()
            : null;

    private static JsonElement? FindObject(JsonElement element, string name) =>
        FindValue(element, name) is { ValueKind: JsonValueKind.Object } value ? value : null;

    private static void RequirePhaseTiming(JsonElement boundary, string phase)
    {
        var timings = FindObject(boundary, "phaseTimingsMilliseconds");
        Require(timings is { } timingObject
                && FindValue(timingObject, phase) is { ValueKind: JsonValueKind.Number } value
                && value.GetDouble() >= 0
                && double.IsFinite(value.GetDouble()),
            $"handlerEnd must include a finite non-negative phase timing for '{phase}'");
    }

    private static bool RequireOptionalPhaseTiming(JsonElement boundary, string phase)
    {
        var timings = FindObject(boundary, "phaseTimingsMilliseconds");
        if (timings is not { } timingObject
            || FindValue(timingObject, phase) is not { } value)
        {
            return false;
        }

        Require(value.ValueKind == JsonValueKind.Number
                && value.GetDouble() >= 0
                && double.IsFinite(value.GetDouble()),
            $"handlerEnd must include a finite non-negative diagram phase timing for '{phase}'");
        return true;
    }

    private static JsonElement? FindValue(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                && FindValue(property.Value, name) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
