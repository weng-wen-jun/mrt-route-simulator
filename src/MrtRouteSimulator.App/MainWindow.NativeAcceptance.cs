using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Polyline = System.Windows.Shapes.Polyline;

namespace MrtRouteSimulator.App;

/// <summary>
/// Native desktop acceptance measurement entry point.  This is intentionally opt-in and keeps
/// all measurement state at the WPF boundary; the engine and its playback semantics are unchanged.
/// </summary>
public partial class MainWindow
{
    private const int NativeAcceptanceProbeIntervalMilliseconds = 20;
    private const double NativeAcceptanceCheckpointSeconds = 10;
    private const double NativeAcceptanceIdleAfterCompletionSeconds = 10;
    private const double NativeAcceptanceStallThresholdMilliseconds = 100;
    private const int NativeAcceptanceMaximumRuns = 5;
    private static readonly JsonSerializerOptions NativeAcceptanceJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = false
    };

    private DispatcherTimer? _nativeAcceptanceProbeTimer;
    private NativeAcceptanceSession? _nativeAcceptanceSession;
    private Task? _nativeAcceptanceStopTask;
    private string? _nativeAcceptanceLastReportPath;

    /// <summary>True while an acceptance session is armed or is collecting a run.</summary>
    public bool IsNativeAcceptanceMeasurementActive => _nativeAcceptanceSession is not null;

    /// <summary>The most recently allocated JSONL report path, including after a stop.</summary>
    public string? NativeAcceptanceReportPath =>
        _nativeAcceptanceSession?.ReportPath ?? _nativeAcceptanceLastReportPath;

    /// <summary>
    /// Arms native acceptance measurement.  It does not start playback or manipulate any control;
    /// the next normal Play command starts a run.  Up to five completed runs are retained in one
    /// session, allowing same-page repeats without an automatic playback path.
    /// </summary>
    public void StartNativeAcceptanceMeasurement(string? outputDirectory = null)
    {
        if (_nativeAcceptanceSession is not null)
        {
            return;
        }

        if (_nativeAcceptanceStopTask is { IsCompleted: false })
        {
            return;
        }

        _nativeAcceptanceStopTask = null;

        NativeAcceptanceJsonlWriter? reportWriter = null;
        NativeAcceptanceJsonlWriter? eventWriter = null;
        try
        {
            var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "NativeAcceptanceLogs")
                : outputDirectory);
            Directory.CreateDirectory(directory);

            var sessionId = Guid.NewGuid();
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            var stem = $"native-acceptance-{stamp}-{sessionId:N}";
            var reportPath = Path.Combine(directory, $"{stem}.jsonl");
            var eventPath = Path.Combine(directory, $"{stem}.events.jsonl");
            reportWriter = NativeAcceptanceJsonlWriter.Create(reportPath,
                exception => NativeAcceptanceOnIoError(sessionId, exception));
            eventWriter = NativeAcceptanceJsonlWriter.Create(eventPath,
                exception => NativeAcceptanceOnIoError(sessionId, exception));

            var previousUiDiagnosticsEnabled = PlaybackDiagnostics.IsEnabled;
            var previousWorkerDiagnosticsEnabled = _playbackWorker?.Diagnostics.IsEnabled;
            _nativeAcceptanceLastReportPath = reportPath;
            var session = new NativeAcceptanceSession(
                sessionId,
                reportPath,
                eventPath,
                reportWriter,
                eventWriter,
                previousUiDiagnosticsEnabled,
                previousWorkerDiagnosticsEnabled);
            _nativeAcceptanceSession = session;
            reportWriter = null;
            eventWriter = null;

            // Reset only at session/run boundaries. Checkpoints never reset diagnostics, so all
            // existing bounded-reservoir counts remain intact for the current run.
            ResetPlaybackDiagnostics();
            EnablePlaybackDiagnostics(true);
            StartNativeAcceptanceProbeTimer();
            NativeAcceptanceInstallInputHooks();
            NativeAcceptanceWrite(session, new
            {
                type = "sessionStarted",
                sessionId,
                utc = DateTimeOffset.UtcNow,
                reportPath,
                eventPath,
                maxRuns = NativeAcceptanceMaximumRuns,
                inputProbe = new
                {
                    intervalMilliseconds = NativeAcceptanceProbeIntervalMilliseconds,
                    method = "DispatcherTimer at DispatcherPriority.Input",
                    interpretation = "dispatcher surrogate; not native click latency"
                },
                inputInstrumentation = new
                {
                    enabledWhenArmedOnly = true,
                    routedHandlers = "PreviewMouseDown/MouseUp/MouseWheel, ButtonBase.Click, TabControl.SelectionChanged",
                    hwndMessages = "WM_ENTERSIZEMOVE/WM_EXITSIZEMOVE/WM_MOVE/WM_SIZE",
                    inputBoundary = "earliest WPF receipt/preview; not OS injection",
                    visualBoundary = "owner application update or DispatcherPriority.Loaded surrogate; not compositor present",
                    osInjectionMeasured = false,
                    compositorPresentMeasured = false
                },
                measurement = "armed; starts only after normal Play command acknowledgement",
                viewport = NativeAcceptanceCaptureViewport()
            });
            NativeAcceptanceSetStatus($"已啟用原生驗收量測（待正常播放）；報告：{reportPath}");
            UpdateNativeAcceptanceMenuState();
        }
        catch (Exception exception)
        {
            if (_nativeAcceptanceSession is { } activeSession)
            {
                _nativeAcceptanceSession = null;
                StopNativeAcceptanceProbeTimer();
                NativeAcceptanceUninstallInputHooks();
                NativeAcceptanceRestoreDiagnostics(activeSession);
                activeSession.DisposeWritersWithoutThrow();
            }
            else
            {
                reportWriter?.DisposeWithoutThrow();
                eventWriter?.DisposeWithoutThrow();
            }
            NativeAcceptanceSetStatus($"原生驗收量測無法開始：{exception.Message}");
        }
    }

    /// <summary>
    /// Stops the acceptance session without changing playback.  Any active run is marked aborted,
    /// the FIFO writers are flushed, and the diagnostics switches are restored to their prior state.
    /// </summary>
    public Task StopNativeAcceptanceMeasurementAsync()
    {
        if (_nativeAcceptanceStopTask is { } existingStop)
        {
            return existingStop;
        }

        var session = _nativeAcceptanceSession;
        if (session is null)
        {
            return Task.CompletedTask;
        }

        Task stopTask;
        try
        {
            stopTask = Dispatcher.InvokeAsync(
                    () => StopNativeAcceptanceMeasurementCoreAsync(session),
                    DispatcherPriority.Normal)
                .Task
                .Unwrap();
        }
        catch (Exception exception)
        {
            session.IoError = $"stop dispatch failed: {exception.Message}";
            return Task.CompletedTask;
        }

        _nativeAcceptanceStopTask = stopTask;
        _ = stopTask.ContinueWith(
            _ =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                try
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ReferenceEquals(_nativeAcceptanceStopTask, stopTask))
                        {
                            _nativeAcceptanceStopTask = null;
                        }
                    }));
                }
                catch
                {
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        return stopTask;
    }

    private async Task StopNativeAcceptanceMeasurementCoreAsync(NativeAcceptanceSession session)
    {
        _nativeAcceptanceSession = null;
        StopNativeAcceptanceProbeTimer();
        NativeAcceptanceUninstallInputHooks();
        Exception? stopException = null;
        try
        {
            if (session.Run is { Completed: false } run)
            {
                NativeAcceptanceFinishRun(session, run, completed: false, reason: "manual-stop", frame: _latestPlaybackFrame);
            }

            NativeAcceptanceWrite(session, new
            {
                type = "sessionStopped",
                sessionId = session.SessionId,
                utc = DateTimeOffset.UtcNow,
                reason = "manual-stop",
                completedRuns = session.CompletedRuns,
                active = false
            });
            await session.DisposeWritersAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            stopException = exception;
            NativeAcceptanceSetStatus($"原生驗收量測輸出收尾失敗：{exception.Message}");
        }
        finally
        {
            NativeAcceptanceRestoreDiagnostics(session);
            NativeAcceptanceSetStatus(stopException is null && session.IoError is null
                ? $"原生驗收量測已停止；報告：{session.ReportPath}"
                : $"原生驗收量測已停止但輸出有錯誤；報告：{session.ReportPath}");
            UpdateNativeAcceptanceMenuState();
        }
    }

    private void NativeAcceptanceMeasurement_Start_Click(object sender, RoutedEventArgs e) =>
        StartNativeAcceptanceMeasurement();

    private async void NativeAcceptanceMeasurement_Stop_Click(object sender, RoutedEventArgs e) =>
        await StopNativeAcceptanceMeasurementAsync();

    private void NativeAcceptanceBeforePlayDispatch(long inputToken = 0)
    {
        var session = _nativeAcceptanceSession;
        if (session is null || !_v2Enabled || _playbackWorker is null)
        {
            return;
        }

        try
        {
            NativeAcceptancePhaseStart(inputToken, "play.diagnosticsEnable");
            try
            {
                EnablePlaybackDiagnostics(true);
            }
            finally
            {
                NativeAcceptancePhaseEnd(inputToken, "play.diagnosticsEnable");
            }
            var now = Stopwatch.GetTimestamp();
            if (session.Run is null || session.Run.Completed)
            {
                if (session.CompletedRuns >= NativeAcceptanceMaximumRuns)
                {
                    if (session.Run?.IdleCheckpointWritten != true
                        && session.Run?.ResetCheckpointWritten != true)
                    {
                        // The fifth run remains open until its explicit 10 s idle checkpoint.
                        // Do not stop or start an alternate playback path here.
                        return;
                    }

                    _ = StopNativeAcceptanceMeasurementAsync();
                    return;
                }

                if (session.Run is { Completed: true } previousRun && previousRun.IdleCheckpointWritten)
                {
                    NativeAcceptanceWriteMemoryCheckpoint(session, previousRun, _latestPlaybackFrame, "nextPlay");
                }

                NativeAcceptancePhaseStart(inputToken, "play.diagnosticsReset");
                try
                {
                    ResetPlaybackDiagnostics();
                    EnablePlaybackDiagnostics(true);
                }
                finally
                {
                    NativeAcceptancePhaseEnd(inputToken, "play.diagnosticsReset");
                }
                session.Run = new NativeAcceptanceRun(++session.NextRunNumber, now, GetActiveTabMetadata());
                session.Run.PlayDispatchTimestamp = now;
                NativeAcceptancePhaseStart(inputToken, "play.beforePlayMemoryCheckpoint");
                try
                {
                    NativeAcceptanceWriteMemoryCheckpoint(session, session.Run, _latestPlaybackFrame, "beforePlay");
                }
                finally
                {
                    NativeAcceptancePhaseEnd(inputToken, "play.beforePlayMemoryCheckpoint");
                }

                NativeAcceptancePhaseStart(inputToken, "play.runArmedViewport");
                var runArmedViewport = NativeAcceptanceCaptureViewport();
                NativeAcceptancePhaseEnd(inputToken, "play.runArmedViewport");
                NativeAcceptancePhaseStart(inputToken, "play.runArmedWrite");
                try
                {
                    NativeAcceptanceWrite(session, new
                    {
                        type = "runArmed",
                        sessionId = session.SessionId,
                        run = session.Run.RunNumber,
                        utc = DateTimeOffset.UtcNow,
                        playDispatchStopwatchTimestamp = now,
                        stopwatchFrequency = Stopwatch.Frequency,
                        activeTab = GetActiveTabMetadata(),
                        viewport = runArmedViewport,
                        visibleProfileNote = "Route/TimeDistance/Speed tab selection is recorded per checkpoint; rendering paths differ."
                    });
                }
                finally
                {
                    NativeAcceptancePhaseEnd(inputToken, "play.runArmedWrite");
                }
            }
            else if (!session.Run.IsPlaying)
            {
                session.Run.ResumeDispatchTimestamp = now;
            }
        }
        catch (Exception exception)
        {
            NativeAcceptanceFailMeasurement(session, $"play-dispatch measurement failed: {exception.Message}");
        }
    }

    private void NativeAcceptanceAfterPlayAcknowledged(long inputToken = 0)
    {
        var session = _nativeAcceptanceSession;
        var run = session?.Run;
        if (session is null || run is null || run.Completed)
        {
            return;
        }

        try
        {
            var now = Stopwatch.GetTimestamp();
            if (run.PlayAcknowledgedTimestamp == 0)
            {
                run.PlayAcknowledgedTimestamp = now;
                run.IsPlaying = true;
                run.ActiveSegmentStartTimestamp = now;
                run.InitialSimulationTimeSeconds = _latestPlaybackFrame?.SimulationTimeSeconds ?? 0;
                NativeAcceptancePhaseStart(inputToken, "play.runtimeBaseline");
                run.RuntimeBaseline = NativeAcceptanceCaptureRuntimeBaseline();
                NativeAcceptancePhaseEnd(inputToken, "play.runtimeBaseline");
                run.WorkerAtStart = _playbackWorker;
                run.WorkerGenerationId = _playbackWorker?.GenerationId;
                run.LastFrame = _latestPlaybackFrame;
                session.LastProbeTimestamp = now;

                var runStartedActiveTab = GetActiveTabMetadata();
                NativeAcceptancePhaseStart(inputToken, "play.runStartedViewport");
                var runStartedViewport = NativeAcceptanceCaptureViewport();
                NativeAcceptancePhaseEnd(inputToken, "play.runStartedViewport");
                NativeAcceptancePhaseStart(inputToken, "play.runStartedWrite");
                try
                {
                    NativeAcceptanceWrite(session, new
                    {
                        type = "runStarted",
                        sessionId = session.SessionId,
                        run = run.RunNumber,
                        utc = DateTimeOffset.UtcNow,
                        playDispatchStopwatchTimestamp = run.PlayDispatchTimestamp,
                        playAcknowledgedStopwatchTimestamp = now,
                        playDispatchToAcknowledgementMilliseconds = Stopwatch.GetElapsedTime(run.PlayDispatchTimestamp, now).TotalMilliseconds,
                        stopwatchFrequency = Stopwatch.Frequency,
                        runtimeBaseline = run.RuntimeBaseline,
                        workerGenerationId = run.WorkerGenerationId,
                        activeTab = runStartedActiveTab,
                        viewport = runStartedViewport
                    });
                }
                finally
                {
                    NativeAcceptancePhaseEnd(inputToken, "play.runStartedWrite");
                }
            }
            else if (!run.IsPlaying)
            {
                var resumeDispatch = run.ResumeDispatchTimestamp;
                run.ResumeDispatchTimestamp = null;
                run.IsPlaying = true;
                run.ActiveSegmentStartTimestamp = now;
                session.LastProbeTimestamp = now;
                NativeAcceptanceWrite(session, new
                {
                    type = "runResumed",
                    sessionId = session.SessionId,
                    run = run.RunNumber,
                    utc = DateTimeOffset.UtcNow,
                    resumeDispatchStopwatchTimestamp = resumeDispatch,
                    resumeAcknowledgedStopwatchTimestamp = now,
                    acknowledgement = "worker command acknowledgement; active wall interval resumes here",
                    stopwatchFrequency = Stopwatch.Frequency,
                    activeTab = GetActiveTabMetadata()
                });
            }
        }
        catch (Exception exception)
        {
            NativeAcceptanceFailMeasurement(session, $"play acknowledgement measurement failed: {exception.Message}");
        }
    }

    private void NativeAcceptancePlayFailed(Exception exception)
    {
        if (_nativeAcceptanceSession is { } session)
        {
            NativeAcceptanceFailMeasurement(session, $"play command failed: {exception.Message}");
        }
    }

    private void NativeAcceptancePauseDispatch()
    {
        var session = _nativeAcceptanceSession;
        var run = session?.Run;
        if (session is null || run is null || run.Completed || !run.IsPlaying)
        {
            return;
        }

        try
        {
            var now = Stopwatch.GetTimestamp();
            run.IsPlaying = false;
            run.HasPause = true;
            run.PauseDispatchTimestamp = now;
            NativeAcceptanceCloseActiveSegment(run, now);
            NativeAcceptanceWrite(session, new
            {
                type = "pauseRequested",
                sessionId = session.SessionId,
                run = run.RunNumber,
                utc = DateTimeOffset.UtcNow,
                pauseCommandDispatchStopwatchTimestamp = now,
                acknowledgement = "not directly observable: PausePlayback dispatches worker command fire-and-forget",
                excludedInterval = "pause dispatch through next Play acknowledgement is excluded; exact worker pause boundary is unknown",
                stopwatchFrequency = Stopwatch.Frequency
            });
        }
        catch (Exception exception)
        {
            NativeAcceptanceFailMeasurement(session, $"pause measurement failed: {exception.Message}");
        }
    }

    private void NativeAcceptanceAbortForLifecycle(string reason)
    {
        var session = _nativeAcceptanceSession;
        if (session is null)
        {
            return;
        }

        try
        {
            if (string.Equals(reason, "playback-reset", StringComparison.Ordinal)
                && session.Run is { } resetRun)
            {
                // Reset starts at the lifecycle boundary, before the async worker reset and
                // the later afterReset presentation/cache notification. Do not let a due
                // completion-idle timer classify a frame from that reset interval as idle.
                resetRun.ResetLifecycleStarted = true;
            }

            if (session.Run is { Completed: false } run)
            {
                NativeAcceptanceFinishRun(session, run, completed: false, reason: reason, frame: _latestPlaybackFrame);
            }
            else
            {
                NativeAcceptanceWrite(session, new
                {
                    type = "lifecycleBoundary",
                    sessionId = session.SessionId,
                    utc = DateTimeOffset.UtcNow,
                    reason,
                    active = false
                });
            }
        }
        catch (Exception exception)
        {
            NativeAcceptanceFailMeasurement(session, $"lifecycle measurement failed: {exception.Message}");
        }
    }

    private void NativeAcceptanceObserveCompletedFrame(PlaybackFrame frame)
    {
        var session = _nativeAcceptanceSession;
        var run = session?.Run;
        if (session is null || run is null || run.Completed || !frame.IsComplete
            || _playbackWorker is null
            || frame.GenerationId != _playbackWorker.GenerationId
            || run.WorkerGenerationId != _playbackWorker.GenerationId)
        {
            return;
        }

        try
        {
            var observed = Stopwatch.GetTimestamp();
            NativeAcceptanceFinishRun(session, run, completed: true, reason: "simulation-complete", frame: frame, observedTimestamp: observed);
            // Keep the session alive for the explicit post-completion idle checkpoint. This is
            // intentionally a timer observation, not a worker/world poll or an automatic Play.
            run.IdleCheckpointDueTimestamp = observed +
                (long)(NativeAcceptanceIdleAfterCompletionSeconds * Stopwatch.Frequency);
        }
        catch (Exception exception)
        {
            NativeAcceptanceFailMeasurement(session, $"completion measurement failed: {exception.Message}");
        }
    }

    private void NativeAcceptanceProbeTick(object? sender, EventArgs e)
    {
        var session = _nativeAcceptanceSession;
        if (session is null)
        {
            return;
        }

        try
        {
            var now = Stopwatch.GetTimestamp();
            var run = session.Run;
            if (run is { Completed: true, IdleCheckpointWritten: false, ResetCheckpointWritten: false, ResetLifecycleStarted: false }
                && run.IdleCheckpointDueTimestamp is { } idleDue
                && now >= idleDue)
            {
                NativeAcceptanceWriteMemoryCheckpoint(session, run, _latestPlaybackFrame, "10sIdleAfterCompletion");
                run.IdleCheckpointWritten = true;

                return;
            }

            if (run is not null && !run.Completed && run.IsPlaying && session.LastProbeTimestamp != 0)
            {
                var gapMilliseconds = Stopwatch.GetElapsedTime(session.LastProbeTimestamp, now).TotalMilliseconds;
                PlaybackDiagnostics.RecordTiming(PlaybackDiagnosticMetricNames.InputStall, gapMilliseconds);
                PlaybackDiagnostics.RecordTiming(
                    PlaybackDiagnosticMetricNames.InputStallExcess,
                    Math.Max(0, gapMilliseconds - NativeAcceptanceProbeIntervalMilliseconds));
                if (gapMilliseconds > NativeAcceptanceStallThresholdMilliseconds)
                {
                    session.StallCount++;
                    NativeAcceptanceWrite(session, new
                    {
                        type = "dispatcherStall",
                        sessionId = session.SessionId,
                        occurrence = session.StallCount,
                        utc = DateTimeOffset.UtcNow,
                        startStopwatchTimestamp = session.LastProbeTimestamp,
                        endStopwatchTimestamp = now,
                        elapsedMilliseconds = gapMilliseconds,
                        thresholdMilliseconds = NativeAcceptanceStallThresholdMilliseconds,
                        activeTab = GetActiveTabMetadata(),
                        simulationTimeSeconds = _latestPlaybackFrame?.SimulationTimeSeconds,
                        recentInputAction = NativeAcceptanceRecentInputActionDescription(),
                        runtime = NativeAcceptanceCaptureRuntimeContext(),
                        interpretation = "DispatcherTimer gap; not an OS input latency or GC causality claim"
                    });
                }
            }
            session.LastProbeTimestamp = now;

            var worker = _playbackWorker;
            if (run is not null && _latestPlaybackFrame is { } observedFrame
                && observedFrame.GenerationId == run.WorkerGenerationId)
            {
                run.LastFrame = observedFrame;
            }
            // An armed run has no worker baseline until normal Play is acknowledged.
            // The input-priority probe can run while PlayAsync is still awaited;
            // comparing against that null baseline would falsely abort a valid run.
            if (run is not null && !run.Completed && run.PlayAcknowledgedTimestamp != 0
                && (worker is null
                    || !ReferenceEquals(worker, run.WorkerAtStart)
                    || worker.GenerationId != run.WorkerGenerationId))
            {
                NativeAcceptanceFinishRun(session, run, completed: false, reason: "playback-worker-replaced", frame: _latestPlaybackFrame);
                return;
            }

            if (run is null || run.Completed || _latestPlaybackFrame is not { } frame)
            {
                return;
            }

            if (!run.IsPlaying && !frame.IsComplete)
            {
                return;
            }

            if (worker is null || frame.GenerationId != worker.GenerationId)
            {
                NativeAcceptanceFinishRun(session, run, completed: false, reason: "stale-frame-generation", frame: frame);
                return;
            }

            var activeWallSeconds = NativeAcceptanceActiveWallSeconds(run, now);
            if (run.IsPlaying && activeWallSeconds >= run.NextCheckpointWallSeconds && !frame.IsComplete)
            {
                NativeAcceptanceWriteCheckpoint(session, run, frame, now);
                run.NextCheckpointWallSeconds = activeWallSeconds + NativeAcceptanceCheckpointSeconds;
            }
        }
        catch (Exception exception)
        {
            NativeAcceptanceFailMeasurement(session, $"probe measurement failed: {exception.Message}");
        }
    }

    private void NativeAcceptanceWriteCheckpoint(
        NativeAcceptanceSession session,
        NativeAcceptanceRun run,
        PlaybackFrame frame,
        long observedTimestamp)
    {
        var ui = PlaybackDiagnostics.Snapshot();
        var worker = run.WorkerAtStart?.Diagnostics.Snapshot();
        var merged = NativeAcceptanceMergeDiagnostics(ui, worker);
        NativeAcceptanceWrite(session, new
        {
            type = "checkpoint",
            sessionId = session.SessionId,
            run = run.RunNumber,
            utc = DateTimeOffset.UtcNow,
            simulationTimeSeconds = frame.SimulationTimeSeconds,
            frameSequence = frame.Sequence,
            framePublishedStopwatchTimestamp = frame.Performance.FramePublishedTimestamp,
            frameObservedStopwatchTimestamp = observedTimestamp,
            framePublishToObservationMilliseconds = frame.Performance.FramePublishedTimestamp > 0
                ? Stopwatch.GetElapsedTime(frame.Performance.FramePublishedTimestamp, observedTimestamp).TotalMilliseconds
                : (double?)null,
            diagnostics = merged,
            diagnosticsNote = "timing p50/p95 are from the existing bounded rolling reservoir (latest capacity), not this 10s interval or an exact full-run percentile",
            runtime = NativeAcceptanceCaptureRuntimeDelta(run.RuntimeBaseline),
            memory = NativeAcceptanceCaptureMemorySnapshot("checkpoint", frame),
            activeTab = GetActiveTabMetadata(),
            viewport = NativeAcceptanceCaptureViewport(),
            checkpointActiveWallSeconds = NativeAcceptanceActiveWallSeconds(run, observedTimestamp),
            throughput = new
            {
                simulationSeconds = Math.Max(0, frame.SimulationTimeSeconds - run.InitialSimulationTimeSeconds),
                activeWallSeconds = NativeAcceptanceActiveWallSeconds(run, frame.Performance.FramePublishedTimestamp > 0
                    ? frame.Performance.FramePublishedTimestamp
                    : observedTimestamp),
                boundaryDefined = !run.HasPause && frame.Performance.FramePublishedTimestamp > 0,
                exact = false,
                note = run.HasPause
                    ? "conservative active interval; pause dispatch/worker acknowledgement boundary is uncertain"
                    : "uses worker frame publication boundary; UI observation delay excluded"
            }
        });
    }

    /// <summary>
    /// Records a presentation memory checkpoint after the caller has completed its normal UI
    /// operation. This is deliberately a notification API: it never resets a worker, advances a
    /// world, or changes the selected tab. Call it from the normal Reset handler after the first
    /// reset frame has been applied and the visible diagram cache has had a chance to invalidate.
    /// </summary>
    public void NativeAcceptanceAfterResetApplied() =>
        NativeAcceptanceRecordMemoryCheckpoint("afterReset");

    /// <summary>Records a named, read-only memory/cache checkpoint for acceptance evidence.</summary>
    public void NativeAcceptanceRecordMemoryCheckpoint(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || _nativeAcceptanceSession is not { } session)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() => NativeAcceptanceRecordMemoryCheckpoint(reason)), DispatcherPriority.Loaded);
            }
            catch
            {
            }

            return;
        }

        var run = session.Run;
        if (string.Equals(reason, "afterReset", StringComparison.Ordinal)
            && run is not null)
        {
            // A reset checkpoint is authoritative evidence that the reset frame/cache was
            // applied. If it precedes the idle horizon, suppress the later idle checkpoint;
            // fabricating a post-reset idle snapshot would mislabel the frame/cache state.
            run.ResetCheckpointWritten = true;
        }

        NativeAcceptanceWriteMemoryCheckpoint(session, run, _latestPlaybackFrame, reason);

        if (string.Equals(reason, "afterReset", StringComparison.Ordinal)
            && session.CompletedRuns >= NativeAcceptanceMaximumRuns
            && run is { Completed: true }
            && (run.IdleCheckpointWritten || run.ResetCheckpointWritten))
        {
            // The fifth run is finalized only after afterReset is queued, preserving the
            // checkpoint while retaining the existing idempotent stop path and run cap.
            _ = StopNativeAcceptanceMeasurementAsync();
        }
    }

    private void NativeAcceptanceWriteMemoryCheckpoint(
        NativeAcceptanceSession session,
        NativeAcceptanceRun? run,
        PlaybackFrame? frame,
        string reason)
    {
        NativeAcceptanceWrite(session, new
        {
            type = "memoryCheckpoint",
            sessionId = session.SessionId,
            run = run?.RunNumber,
            utc = DateTimeOffset.UtcNow,
            reason,
            memory = NativeAcceptanceCaptureMemorySnapshot(reason, frame),
            activeTab = GetActiveTabMetadata(),
            viewport = NativeAcceptanceCaptureViewport()
        });
    }

    private object NativeAcceptanceCaptureMemorySnapshot(string phase, PlaybackFrame? frame)
    {
        try
        {
            long privateBytes;
            long workingSetBytes;
            if (!PlaybackProcessMemoryDiagnostics.TryCapture(out privateBytes, out workingSetBytes))
            {
                using var process = Process.GetCurrentProcess();
                privateBytes = process.PrivateMemorySize64;
                workingSetBytes = process.WorkingSet64;
            }

            var actualGroups = _diagramActualCache.Groups;
            var plannedGroups = _diagramPlannedCache.Groups;
            var actualOrdinary = actualGroups.Sum(group => group.OrdinaryPointCount);
            var actualCritical = actualGroups.Sum(group => group.CriticalPointCount);
            var plannedOrdinary = plannedGroups.Sum(group => group.OrdinaryPointCount);
            var plannedCritical = plannedGroups.Sum(group => group.CriticalPointCount);
            var children = TimeDistanceCanvas?.Children;

            return new
            {
                phase,
                privateBytes,
                workingSetBytes,
                managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
                heapBytes = GC.GetTotalMemory(forceFullCollection: false),
                allocatedBytes = GC.GetTotalAllocatedBytes(precise: false),
                allocGc = new
                {
                    totalAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false),
                    gen0 = GC.CollectionCount(0),
                    gen1 = GC.CollectionCount(1),
                    gen2 = GC.CollectionCount(2)
                },
                gc = new
                {
                    gen0 = GC.CollectionCount(0),
                    gen1 = GC.CollectionCount(1),
                    gen2 = GC.CollectionCount(2)
                },
                frameHistoryCounts = frame is null ? null : new
                {
                    trajectory = frame.Trajectory.Count,
                    safety = frame.SafetyHistory.Count,
                    events = frame.Events.Count,
                    trainStates = frame.Trains.Length,
                    sequence = frame.Sequence,
                    generationId = frame.GenerationId,
                    simulationTimeSeconds = frame.SimulationTimeSeconds
                },
                displayCache = new
                {
                    actualSeriesCount = actualGroups.Count,
                    plannedSeriesCount = plannedGroups.Count,
                    ordinaryPointTotal = actualOrdinary + plannedOrdinary,
                    criticalPointTotal = actualCritical + plannedCritical,
                    actualOrdinaryPointTotal = actualOrdinary,
                    actualCriticalPointTotal = actualCritical,
                    plannedOrdinaryPointTotal = plannedOrdinary,
                    plannedCriticalPointTotal = plannedCritical,
                    actualProcessedCount = _diagramActualCache.ProcessedCount,
                    plannedProcessedCount = _diagramPlannedCache.ProcessedCount,
                    actualVersion = _diagramActualCache.Version,
                    plannedVersion = _diagramPlannedCache.Version
                },
                wpfVisuals = new
                {
                    polylineCount = children?.OfType<Polyline>().Count() ?? 0,
                    textBlockCount = children?.OfType<System.Windows.Controls.TextBlock>().Count() ?? 0,
                    canvasChildCount = children?.Count ?? 0,
                    wpfPolylineCount = children?.OfType<Polyline>().Count() ?? 0,
                    wpfTextBlockCount = children?.OfType<System.Windows.Controls.TextBlock>().Count() ?? 0,
                    wpfCanvasChildCount = children?.Count ?? 0,
                    canvasAvailable = children is not null
                }
            };
        }
        catch (Exception exception)
        {
            return new { phase, unavailable = exception.Message };
        }
    }

    private object NativeAcceptanceCaptureRuntimeContext()
    {
        try
        {
            long privateBytes;
            long workingSetBytes;
            if (!PlaybackProcessMemoryDiagnostics.TryCapture(out privateBytes, out workingSetBytes))
            {
                using var process = Process.GetCurrentProcess();
                privateBytes = process.PrivateMemorySize64;
                workingSetBytes = process.WorkingSet64;
            }

            return new
            {
                privateBytes,
                workingSetBytes,
                managedHeapBytes = GC.GetTotalMemory(forceFullCollection: false),
                allocatedBytes = GC.GetTotalAllocatedBytes(precise: false),
                gen0 = GC.CollectionCount(0),
                gen1 = GC.CollectionCount(1),
                gen2 = GC.CollectionCount(2)
            };
        }
        catch (Exception exception)
        {
            return new { unavailable = exception.Message };
        }
    }

    private void NativeAcceptanceFinishRun(
        NativeAcceptanceSession session,
        NativeAcceptanceRun run,
        bool completed,
        string reason,
        PlaybackFrame? frame,
        long? observedTimestamp = null)
    {
        if (run.Completed)
        {
            return;
        }

        var observed = observedTimestamp ?? Stopwatch.GetTimestamp();
        // A reset/replacement may already have published a different generation. Never label
        // that new frame's events or clock as the aborted run's final state.
        if (frame?.GenerationId != run.WorkerGenerationId)
        {
            frame = run.LastFrame;
        }
        var published = frame?.Performance.FramePublishedTimestamp ?? 0;
        var endTimestamp = published > 0 ? published : observed;
        if (run.IsPlaying)
        {
            NativeAcceptanceCloseActiveSegment(run, endTimestamp);
        }

        run.Completed = true;
        run.IsPlaying = false;
        if (completed)
        {
            session.CompletedRuns++;
        }

        var simulationTime = frame?.SimulationTimeSeconds ?? run.InitialSimulationTimeSeconds;
        var simulationSeconds = Math.Max(0, simulationTime - run.InitialSimulationTimeSeconds);
        var conservativeWallSeconds = endTimestamp >= run.PlayDispatchTimestamp
            ? Stopwatch.GetElapsedTime(run.PlayDispatchTimestamp, endTimestamp).TotalSeconds
            : 0;
        var acknowledgedWallSeconds = NativeAcceptanceActiveWallSeconds(run, endTimestamp);
        var ui = PlaybackDiagnostics.Snapshot();
        var worker = run.WorkerAtStart?.Diagnostics.Snapshot();
        var merged = NativeAcceptanceMergeDiagnostics(ui, worker);
        var eventCount = frame?.Events.Count ?? 0;
        NativeAcceptanceWrite(session, new
        {
            type = completed ? "runCompleted" : "runAborted",
            sessionId = session.SessionId,
            run = run.RunNumber,
            utc = DateTimeOffset.UtcNow,
            reason,
            completed,
            playDispatchStopwatchTimestamp = run.PlayDispatchTimestamp,
            playAcknowledgedStopwatchTimestamp = run.PlayAcknowledgedTimestamp,
            completionFramePublishedStopwatchTimestamp = published > 0 ? published : (long?)null,
            completionObservedStopwatchTimestamp = observed,
            completionFramePublishToObservationMilliseconds = published > 0
                ? Stopwatch.GetElapsedTime(published, observed).TotalMilliseconds
                : (double?)null,
            stopwatchFrequency = Stopwatch.Frequency,
            simulationTimeSeconds = simulationTime,
            simulationSeconds,
            conservativeWallSecondsFromPlayDispatch = conservativeWallSeconds,
            activeWallSecondsFromPlayAcknowledgement = acknowledgedWallSeconds,
            hasPause = run.HasPause,
            throughputBoundaryDefined = completed && !run.HasPause && published > 0,
            throughputExact = false,
            throughputByConservativeDispatchWall = conservativeWallSeconds > 0
                ? simulationSeconds / conservativeWallSeconds
                : (double?)null,
            throughputByAcknowledgedActiveWall = acknowledgedWallSeconds > 0
                ? simulationSeconds / acknowledgedWallSeconds
                : (double?)null,
            timingBoundaryNote = "FramePublishedTimestamp is captured near worker frame publication; it is not a claim about the exact final engine tick timestamp.",
            throughputNote = !completed
                ? "aborted; no throughput claim"
                : run.HasPause
                    ? "pause dispatch/worker acknowledgement boundary is uncertain; interval is conservative and not an exact full-run multiplier"
                    : published > 0
                        ? "frame publication boundary used; observation delay is excluded"
                        : "worker publication timestamp unavailable; observation boundary is a conservative fallback",
            diagnostics = merged,
            diagnosticsNote = "timing p50/p95 are from the existing bounded rolling reservoir (latest capacity), not an exact run percentile",
            runtime = NativeAcceptanceCaptureRuntimeDelta(run.RuntimeBaseline),
            memory = NativeAcceptanceCaptureMemorySnapshot("completion", frame),
            dispatcherStallCount = session.StallCount,
            activeTab = GetActiveTabMetadata(),
            viewport = NativeAcceptanceCaptureViewport(),
            eventCount,
            finalFrameSummary = frame is null ? null : new
            {
                frame.GenerationId,
                frame.Sequence,
                frame.IsComplete,
                trajectoryCount = frame.Trajectory.Count,
                safetyCount = frame.SafetyHistory.Count,
                activeTrainCount = frame.Performance.ActiveTrainCount
            },
            eventsFile = session.EventPath
        });

        if (frame is not null)
        {
            NativeAcceptanceQueueEvents(session, run.RunNumber, frame.Events);
        }
    }

    private static double NativeAcceptanceActiveWallSeconds(NativeAcceptanceRun run, long endTimestamp)
    {
        var total = run.ActiveWallSeconds;
        if (run.IsPlaying && run.ActiveSegmentStartTimestamp > 0 && endTimestamp >= run.ActiveSegmentStartTimestamp)
        {
            total += Stopwatch.GetElapsedTime(run.ActiveSegmentStartTimestamp, endTimestamp).TotalSeconds;
        }

        return Math.Max(0, total);
    }

    private static void NativeAcceptanceCloseActiveSegment(NativeAcceptanceRun run, long endTimestamp)
    {
        if (run.ActiveSegmentStartTimestamp > 0 && endTimestamp >= run.ActiveSegmentStartTimestamp)
        {
            run.ActiveWallSeconds += Stopwatch.GetElapsedTime(run.ActiveSegmentStartTimestamp, endTimestamp).TotalSeconds;
        }

        run.ActiveSegmentStartTimestamp = 0;
    }

    private void NativeAcceptanceQueueEvents(
        NativeAcceptanceSession session,
        int runNumber,
        IReadOnlyList<MrtRouteSimulator.Engine.SimulationEvent> events)
    {
        // Events are intentionally written once per completed/aborted run and never per frame;
        // trajectory history is not copied into this acceptance artifact.
        session.EventWriteTasks.Add(Task.Run(() =>
        {
            try
            {
                var record = new
                {
                    type = "runEvents",
                    sessionId = session.SessionId,
                    run = runNumber,
                    utc = DateTimeOffset.UtcNow,
                    events
                };
                session.EventWriter.Enqueue(record);
            }
            catch (Exception exception)
            {
                session.IoError = $"event JSONL queue failed: {exception.Message}";
                NativeAcceptanceOnIoError(session.SessionId, exception);
                throw;
            }
        }));
    }

    private PlaybackDiagnosticsSnapshot NativeAcceptanceMergeDiagnostics(
        PlaybackDiagnosticsSnapshot ui,
        PlaybackDiagnosticsSnapshot? worker)
    {
        if (worker is null)
        {
            return ui;
        }

        try
        {
            return PlaybackDiagnosticsSnapshot.Merge(worker, ui);
        }
        catch (Exception exception)
        {
            NativeAcceptanceSetStatus($"原生驗收診斷合併失敗：{exception.Message}");
            return ui;
        }
    }

    private NativeAcceptanceRuntimeBaseline NativeAcceptanceCaptureRuntimeBaseline()
    {
        var ui = PlaybackDiagnostics.CaptureRuntimeSnapshot();
        var worker = _playbackWorker?.Diagnostics.CaptureRuntimeSnapshot();
        return new NativeAcceptanceRuntimeBaseline(ui, worker);
    }

    private NativeAcceptanceRuntimeDelta NativeAcceptanceCaptureRuntimeDelta(NativeAcceptanceRuntimeBaseline? baseline)
    {
        var ui = PlaybackDiagnostics.CaptureRuntimeSnapshot();
        var worker = _playbackWorker?.Diagnostics.CaptureRuntimeSnapshot();
        return NativeAcceptanceRuntimeDelta.Create(baseline, ui, worker);
    }

    private void StartNativeAcceptanceProbeTimer()
    {
        if (_nativeAcceptanceProbeTimer is null)
        {
            _nativeAcceptanceProbeTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(NativeAcceptanceProbeIntervalMilliseconds)
            };
            _nativeAcceptanceProbeTimer.Tick += NativeAcceptanceProbeTick;
        }

        _nativeAcceptanceProbeTimer.Start();
    }

    private void StopNativeAcceptanceProbeTimer()
    {
        _nativeAcceptanceProbeTimer?.Stop();
        if (_nativeAcceptanceProbeTimer is not null)
        {
            _nativeAcceptanceProbeTimer.Tick -= NativeAcceptanceProbeTick;
            _nativeAcceptanceProbeTimer = null;
        }
    }

    private void NativeAcceptanceRestoreDiagnostics(NativeAcceptanceSession session)
    {
        try
        {
            EnablePlaybackDiagnostics(session.PreviousUiDiagnosticsEnabled);
            _playbackWorker?.Diagnostics.Enable(session.PreviousWorkerDiagnosticsEnabled ?? false);
        }
        catch (Exception exception)
        {
            NativeAcceptanceSetStatus($"原生驗收診斷開關復原失敗：{exception.Message}");
        }
    }

    private void NativeAcceptanceFailMeasurement(NativeAcceptanceSession session, string message)
    {
        if (!ReferenceEquals(_nativeAcceptanceSession, session))
        {
            return;
        }

        session.IoError = message;
        NativeAcceptanceSetStatus($"原生驗收量測已停止（播放未受影響）：{message}");
        _ = StopNativeAcceptanceMeasurementAsync();
    }

    private void NativeAcceptanceOnIoError(Guid sessionId, Exception exception)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_nativeAcceptanceSession is { } session && session.SessionId == sessionId)
                {
                    NativeAcceptanceFailMeasurement(session, $"JSONL I/O error: {exception.Message}");
                }
            }));
        }
        catch
        {
            // Window shutdown can race a writer callback; never let an output error affect playback.
        }
    }

    private void NativeAcceptanceWrite(NativeAcceptanceSession session, object record)
    {
        if (session.IoError is not null)
        {
            return;
        }

        try
        {
            session.ReportWriter.Enqueue(record);
        }
        catch (Exception exception)
        {
            NativeAcceptanceOnIoError(session.SessionId, exception);
        }
    }

    private void NativeAcceptanceSetStatus(string message)
    {
        if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
        {
            StatusTextBlock.Text = message;
        }
    }

    private void UpdateNativeAcceptanceMenuState()
    {
        if (StartNativeAcceptanceMeasurementMenuItem is null || StopNativeAcceptanceMeasurementMenuItem is null)
        {
            return;
        }

        var active = _nativeAcceptanceSession is not null;
        StartNativeAcceptanceMeasurementMenuItem.IsEnabled = !active;
        StopNativeAcceptanceMeasurementMenuItem.IsEnabled = active;
    }

    private object GetActiveTabMetadata()
    {
        try
        {
            var workspaceItem = WorkspaceTabControl.SelectedItem as System.Windows.Controls.TabItem;
            var nestedItem = SimulationViewTabControl.SelectedItem as System.Windows.Controls.TabItem;
            return new
            {
                workspaceIndex = WorkspaceTabControl.SelectedIndex,
                workspaceHeader = workspaceItem?.Header?.ToString(),
                simulationViewIndex = SimulationViewTabControl.SelectedIndex,
                simulationViewHeader = nestedItem?.Header?.ToString()
            };
        }
        catch
        {
            return new { workspaceIndex = -1, workspaceHeader = (string?)null, simulationViewIndex = -1, simulationViewHeader = (string?)null };
        }
    }

    private object NativeAcceptanceCaptureViewport()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var handle = new WindowInteropHelper(this).Handle;
            var monitor = NativeAcceptanceGetMonitorInfo(handle);
            return new
            {
                dpi = new { dpi.PixelsPerInchX, dpi.PixelsPerInchY, dpi.DpiScaleX, dpi.DpiScaleY },
                interfaceScale = InterfaceScaleService.CurrentScale,
                windowBoundsDip = new { Left, Top, Width, Height },
                initialWindowFit = _initialWindowFitDiagnostic,
                workAreaDip = new { SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height },
                monitor
            };
        }
        catch (Exception exception)
        {
            return new { unavailable = exception.Message };
        }
    }

    private static object NativeAcceptanceGetMonitorInfo(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return new { available = false };
        }

        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            return new { available = false };
        }

        var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? new
            {
                available = true,
                device = info.DeviceName,
                boundsPixels = new { info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom },
                workAreaPixels = new { info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom },
                primary = (info.Flags & MonitorInfoPrimary) != 0
            }
            : new { available = false };
    }

    private const uint MonitorDefaultToNearest = 2;
    private const uint MonitorInfoPrimary = 1;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int CbSize;
        public NativeAcceptanceRect Monitor;
        public NativeAcceptanceRect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAcceptanceRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private sealed class NativeAcceptanceSession
    {
        public NativeAcceptanceSession(
            Guid sessionId,
            string reportPath,
            string eventPath,
            NativeAcceptanceJsonlWriter reportWriter,
            NativeAcceptanceJsonlWriter eventWriter,
            bool previousUiDiagnosticsEnabled,
            bool? previousWorkerDiagnosticsEnabled)
        {
            SessionId = sessionId;
            ReportPath = reportPath;
            EventPath = eventPath;
            ReportWriter = reportWriter;
            EventWriter = eventWriter;
            PreviousUiDiagnosticsEnabled = previousUiDiagnosticsEnabled;
            PreviousWorkerDiagnosticsEnabled = previousWorkerDiagnosticsEnabled;
        }

        public Guid SessionId { get; }
        public string ReportPath { get; }
        public string EventPath { get; }
        public NativeAcceptanceJsonlWriter ReportWriter { get; }
        public NativeAcceptanceJsonlWriter EventWriter { get; }
        public bool PreviousUiDiagnosticsEnabled { get; }
        public bool? PreviousWorkerDiagnosticsEnabled { get; }
        public NativeAcceptanceRun? Run { get; set; }
        public int NextRunNumber { get; set; }
        public int CompletedRuns { get; set; }
        public int InputActionCount { get; set; }
        public long LastProbeTimestamp { get; set; }
        public int StallCount { get; set; }
        public string? IoError { get; set; }
        public List<Task> EventWriteTasks { get; } = [];

        public async Task DisposeWritersAsync()
        {
            try
            {
                await Task.WhenAll(EventWriteTasks).ConfigureAwait(true);
            }
            catch
            {
                // The writer's callback reports the I/O failure; stopping remains non-throwing.
            }

            await EventWriter.DisposeAsync().ConfigureAwait(true);
            await ReportWriter.DisposeAsync().ConfigureAwait(true);
            var failure = EventWriter.Failure ?? ReportWriter.Failure;
            if (failure is not null && IoError is null)
            {
                IoError = $"JSONL writer failed: {failure.Message}";
            }
        }

        public void DisposeWritersWithoutThrow()
        {
            EventWriter.DisposeWithoutThrow();
            ReportWriter.DisposeWithoutThrow();
        }
    }

    private sealed class NativeAcceptanceRun
    {
        public NativeAcceptanceRun(int runNumber, long dispatchTimestamp, object initialTab)
        {
            RunNumber = runNumber;
            PlayDispatchTimestamp = dispatchTimestamp;
            InitialTab = initialTab;
        }

        public int RunNumber { get; }
        public object InitialTab { get; }
        public long PlayDispatchTimestamp { get; set; }
        public long PlayAcknowledgedTimestamp { get; set; }
        public long? ResumeDispatchTimestamp { get; set; }
        public long? PauseDispatchTimestamp { get; set; }
        public long ActiveSegmentStartTimestamp { get; set; }
        public double ActiveWallSeconds { get; set; }
        public double InitialSimulationTimeSeconds { get; set; }
        public double NextCheckpointWallSeconds { get; set; } = NativeAcceptanceCheckpointSeconds;
        public bool IsPlaying { get; set; }
        public bool HasPause { get; set; }
        public bool Completed { get; set; }
        public long? IdleCheckpointDueTimestamp { get; set; }
        public bool IdleCheckpointWritten { get; set; }
        public bool ResetCheckpointWritten { get; set; }
        public bool ResetLifecycleStarted { get; set; }
        public SimulationPlaybackWorker? WorkerAtStart { get; set; }
        public Guid? WorkerGenerationId { get; set; }
        public PlaybackFrame? LastFrame { get; set; }
        public NativeAcceptanceRuntimeBaseline? RuntimeBaseline { get; set; }
    }

    private sealed class NativeAcceptanceJsonlWriter
    {
        private readonly Channel<object> _queue = Channel.CreateBounded<object>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        private readonly FileStream _stream;
        private readonly Action<Exception> _onError;
        private readonly Task _drainTask;
        private int _failed;
        private Exception? _failure;

        private NativeAcceptanceJsonlWriter(FileStream stream, Action<Exception> onError)
        {
            _stream = stream;
            _onError = onError;
            _drainTask = Task.Run(DrainAsync);
        }

        public static NativeAcceptanceJsonlWriter Create(string path, Action<Exception> onError) =>
            new(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan), onError);

        public Exception? Failure => Volatile.Read(ref _failure);

        public void Enqueue(object value)
        {
            if (Volatile.Read(ref _failed) != 0)
            {
                return;
            }

            if (!_queue.Writer.TryWrite(value))
            {
                throw new IOException("native acceptance JSONL queue is full or closed");
            }
        }

        public async Task DisposeAsync()
        {
            _queue.Writer.TryComplete();
            try
            {
                await _drainTask.ConfigureAwait(true);
            }
            catch
            {
            }

            try
            {
                await _stream.FlushAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref _failure, exception, null);
                _onError(exception);
            }

            _stream.Dispose();
        }

        public void DisposeWithoutThrow()
        {
            try
            {
                _queue.Writer.TryComplete();
                _stream.Dispose();
            }
            catch
            {
            }
        }

        private async Task DrainAsync()
        {
            try
            {
                await foreach (var value in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
                {
                    var line = JsonSerializer.Serialize(value, NativeAcceptanceJsonOptions);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(line + Environment.NewLine);
                    await _stream.WriteAsync(bytes).ConfigureAwait(false);
                    await _stream.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                Interlocked.CompareExchange(ref _failure, exception, null);
                if (Interlocked.Exchange(ref _failed, 1) == 0)
                {
                    _onError(exception);
                }
            }
        }
    }

    private sealed record NativeAcceptanceRuntimeBaseline(
        PlaybackRuntimeSnapshot Ui,
        PlaybackRuntimeSnapshot? Worker);

    private sealed record NativeAcceptanceRuntimeDelta(
        object? Ui,
        object? Worker)
    {
        public static NativeAcceptanceRuntimeDelta Create(
            NativeAcceptanceRuntimeBaseline? baseline,
            PlaybackRuntimeSnapshot ui,
            PlaybackRuntimeSnapshot? worker)
        {
            return new NativeAcceptanceRuntimeDelta(
                CreateOne(baseline?.Ui, ui),
                worker is null ? null : CreateOne(baseline?.Worker, worker));
        }

        private static object CreateOne(PlaybackRuntimeSnapshot? start, PlaybackRuntimeSnapshot current) => new
        {
            start = start is null ? null : new
            {
                allocatedBytes = start.AllocatedBytes,
                managedBytes = start.ManagedBytes,
                workingSetBytes = start.WorkingSetBytes,
                gen0 = start.Gen0Collections,
                gen1 = start.Gen1Collections,
                gen2 = start.Gen2Collections
            },
            current = new
            {
                allocatedBytes = current.AllocatedBytes,
                managedBytes = current.ManagedBytes,
                workingSetBytes = current.WorkingSetBytes,
                gen0 = current.Gen0Collections,
                gen1 = current.Gen1Collections,
                gen2 = current.Gen2Collections
            },
            delta = start is null ? null : new
            {
                allocatedBytes = current.AllocatedBytes - start.AllocatedBytes,
                managedBytes = current.ManagedBytes - start.ManagedBytes,
                workingSetBytes = current.WorkingSetBytes - start.WorkingSetBytes,
                gen0 = current.Gen0Collections - start.Gen0Collections,
                gen1 = current.Gen1Collections - start.Gen1Collections,
                gen2 = current.Gen2Collections - start.Gen2Collections
            }
        };
    }
}
