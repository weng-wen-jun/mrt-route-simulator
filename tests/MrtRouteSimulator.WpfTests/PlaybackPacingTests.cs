using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Phase A coordinator pacing regressions. These tests intentionally exercise the worker directly
/// so they can observe fixed-tick output and command latency without involving a WPF render loop.
/// Register <see cref="Run"/> from the WPF runner when the pacing gate is enabled.
/// </summary>
internal static class PlaybackPacingTests
{
    private static readonly double[] PlaybackRates = [1, 10, 30, 60];
    private static readonly JsonSerializerOptions ParityJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static void Run(string root)
    {
        foreach (var rate in PlaybackRates)
        {
            VerifyRatePacing(root, rate);
        }

        VerifyCommandFrameBoundaries(root);
        VerifyPauseResumeRateChange(root);
        VerifyResetWhileRunning(root);
        VerifyStopAndCleanup(root);
        Console.WriteLine("PASS WPF playback pacing");
    }

    private static void VerifyCommandFrameBoundaries(string root)
    {
        var worker = CreateWorker(root);
        try
        {
            _ = ReadLatest(worker);

            // Play changes coordinator state but intentionally has no immediate frame contract;
            // the first frame after it may still carry the prior performance snapshot until the
            // normal cadence. AdvanceTo is the deterministic worker hook for observing the new
            // requested rate without sleeping for a wall-clock period.
            Await(worker.PlayAsync(1e-9), "Play frame boundary");
            Await(worker.AdvanceToSimulationTimeAsync(SimulationWorld.FixedTimeStepSeconds),
                "AdvanceTo after Play");
            var advanced = ReadLatest(worker);
            Require(advanced.SimulationTimeSeconds == SimulationWorld.FixedTimeStepSeconds,
                $"Play 後 deterministic AdvanceTo 必須完成第一個 fixed tick；actual={advanced.SimulationTimeSeconds:R}。");
            Require(advanced.Performance.RequestedPlaybackRate == 1e-9,
                $"Play 後 frame 必須反映 worker requested rate；actual={advanced.Performance.RequestedPlaybackRate:R}。");

            Await(worker.ResetAsync(), "Reset frame boundary");
            var firstReset = ReadLatest(worker);
            Await(worker.ResetAsync(), "second Reset frame boundary");
            var reset = ReadLatest(worker);
            Require(reset.Sequence == firstReset.Sequence + 1,
                $"Reset 應只發布一個重設 frame；first={firstReset.Sequence}, second={reset.Sequence}。");
            Require(reset.SimulationTimeSeconds == 0,
                $"Reset frame 必須回到 0 秒；actual={reset.SimulationTimeSeconds:R}。");
            Console.WriteLine("  command frame boundaries: Play state via AdvanceTo, Reset one frame");
        }
        finally
        {
            DisposeWorker(worker);
        }
    }

    private static void VerifyRatePacing(string root, double rate)
    {
        var diagnostics = new PlaybackPerformanceDiagnostics(enabled: true);
        var worker = CreateWorker(root, diagnostics);
        try
        {
            var initial = ReadLatest(worker);
            Await(worker.PlayAsync(rate), "Play");

            // The first complete tick is due after 0.1 / rate seconds. Keep the observation
            // short enough for a regression test while allowing the 1x case to cross that edge.
            var runningFrames = new List<PlaybackFrame>();
            var playbackElapsed = ObserveRunningFrames(worker, runningFrames, TimeSpan.FromMilliseconds(150));

            var pauseClock = Stopwatch.StartNew();
            Await(worker.PauseAsync(), "Pause");
            pauseClock.Stop();
            var paused = ReadLatest(worker);
            runningFrames.Add(paused);

            Require(paused.GenerationId == initial.GenerationId,
                $"rate {rate:0.#}x 不應在同一 worker 內改變 generation。");
            Require(paused.SimulationTimeSeconds >= SimulationWorld.FixedTimeStepSeconds - 1e-7,
                $"rate {rate:0.#}x 未完成第一個固定 tick；actual={paused.SimulationTimeSeconds:0.########}。");
            Require(IsFixedTick(paused.SimulationTimeSeconds),
                $"rate {rate:0.#}x simulation time 必須落在 0.1 秒固定步進；actual={paused.SimulationTimeSeconds:R}。");
            Require(diagnostics.Snapshot().GetCount(PlaybackDiagnosticMetricNames.WorkerNoProgressAdvanceCount) == 0,
                $"rate {rate:0.#}x 不得在 deadline 前呼叫 no-progress AdvanceTo。");

            VerifyMonotonicFrames(runningFrames, initial.SimulationTimeSeconds);
            VerifyParity(root, paused);
            var observedRate = paused.SimulationTimeSeconds / Math.Max(playbackElapsed.TotalSeconds, 0.001);
            VerifyReasonableRate(rate, observedRate, paused.SimulationTimeSeconds);
            Console.WriteLine($"  pacing {rate:0.#}x: observed={observedRate:0.0}x, "
                + $"t={paused.SimulationTimeSeconds:0.0}s, pause={pauseClock.Elapsed.TotalMilliseconds:0.0}ms, no-progress=0");
        }
        finally
        {
            DisposeWorker(worker);
        }
    }

    private static void VerifyPauseResumeRateChange(string root)
    {
        var worker = CreateWorker(root);
        try
        {
            Await(worker.PlayAsync(1), "Play 1x");
            Thread.Sleep(140);
            var pauseClock = Stopwatch.StartNew();
            Await(worker.PauseAsync(), "Pause before resume");
            pauseClock.Stop();
            Require(pauseClock.Elapsed < TimeSpan.FromMilliseconds(250),
                $"Pause latency 過高；actual={pauseClock.Elapsed.TotalMilliseconds:0.0}ms。");
            var pausedFrame = ReadLatest(worker);
            var paused = pausedFrame.SimulationTimeSeconds;

            Thread.Sleep(80);
            var stillPaused = ReadLatestOr(worker, pausedFrame).SimulationTimeSeconds;
            Require(stillPaused == paused,
                $"Pause 後 simulation time 不得繼續前進；paused={paused:R}, observed={stillPaused:R}。");

            Await(worker.PlayAsync(10), "Resume 10x");
            Thread.Sleep(120);
            Await(worker.PauseAsync(), "Pause after resume");
            var resumed = ReadLatest(worker).SimulationTimeSeconds;
            Require(resumed > paused,
                $"Resume 後 simulation time 必須繼續前進；paused={paused:R}, resumed={resumed:R}。");

            var beforeRateChange = resumed;
            Await(worker.PlayAsync(1), "Play before rate change");
            Thread.Sleep(120);
            Await(worker.SetPlaybackRateAsync(60), "Set rate 60x");
            Thread.Sleep(120);
            Await(worker.PauseAsync(), "Pause after rate change");
            var afterRateChange = ReadLatest(worker).SimulationTimeSeconds;
            Require(afterRateChange > beforeRateChange,
                $"執行中改倍率不得重設 simulation time；before={beforeRateChange:R}, after={afterRateChange:R}。");
            Console.WriteLine($"  pause/resume/rate-change: first-pause={pauseClock.Elapsed.TotalMilliseconds:0.0}ms, "
                + $"paused={paused:0.0}s, resumed={resumed:0.0}s, final={afterRateChange:0.0}s");
        }
        finally
        {
            DisposeWorker(worker);
        }
    }

    private static void VerifyResetWhileRunning(string root)
    {
        var worker = CreateWorker(root);
        try
        {
            Await(worker.PlayAsync(30), "Play before reset");
            Thread.Sleep(100);
            var resetClock = Stopwatch.StartNew();
            Await(worker.ResetAsync(), "Reset while running");
            resetClock.Stop();
            Require(resetClock.Elapsed < TimeSpan.FromMilliseconds(250),
                $"Reset latency 過高；actual={resetClock.Elapsed.TotalMilliseconds:0.0}ms。");
            var reset = ReadLatest(worker);
            Require(reset.SimulationTimeSeconds == 0,
                $"Reset 必須由 worker 執行到時間 0；actual={reset.SimulationTimeSeconds:R}。");
            VerifyParity(root, reset);
            Require(reset.Events.All(item => item.SimulationTimeSeconds <= 1e-7),
                "Reset frame 不得保留時間 0 之後的舊事件。");
            Require(reset.Trajectory.All(item => item.SimulationTimeSeconds <= 1e-7),
                "Reset frame 不得保留時間 0 之後的舊軌跡。");

            Thread.Sleep(80);
            var stable = ReadLatestOr(worker, reset);
            Require(stable.SimulationTimeSeconds == 0,
                "Reset 後未重新 Play 前不得自動恢復播放。");
            Console.WriteLine($"  reset latency: {resetClock.Elapsed.TotalMilliseconds:0.0}ms");
        }
        finally
        {
            DisposeWorker(worker);
        }
    }

    private static void VerifyStopAndCleanup(string root)
    {
        var worker = CreateWorker(root);
        Await(worker.PlayAsync(60), "Play before stop");
        Thread.Sleep(40);

        var stopwatch = Stopwatch.StartNew();
        Await(worker.DisposeAsync().AsTask(), "Stop/dispose");
        stopwatch.Stop();
        Require(worker.Completion.IsCompleted, "Stop/dispose 完成時 worker task 必須已結束。");
        Require(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500),
            $"Stop/dispose latency 過高；actual={stopwatch.Elapsed.TotalMilliseconds:0.0}ms。");

        var rejected = worker.PauseAsync();
        RequireThrows<ObjectDisposedException>(() => rejected.GetAwaiter().GetResult(),
            "worker dispose 後的 command 必須可靠拒絕。");
        Console.WriteLine($"  stop/dispose latency: {stopwatch.Elapsed.TotalMilliseconds:0.0}ms");
    }

    private static SimulationPlaybackWorker CreateWorker(
        string root,
        PlaybackPerformanceDiagnostics? diagnostics = null)
    {
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var options = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.Decimated(0.5),
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(1.0),
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology);
        var worker = new SimulationPlaybackWorker(options.CreateWorld(), diagnostics);
        Await(worker.Ready, "worker ready");
        return worker;
    }

    private static void VerifyMonotonicFrames(IReadOnlyList<PlaybackFrame> frames, double initialTime)
    {
        var previous = initialTime;
        foreach (var frame in frames)
        {
            Require(frame.SimulationTimeSeconds >= previous - 1e-7,
                $"播放中 frame simulation time 必須單調不減；previous={previous:R}, actual={frame.SimulationTimeSeconds:R}。");
            previous = frame.SimulationTimeSeconds;
        }
    }

    private static TimeSpan ObserveRunningFrames(
        SimulationPlaybackWorker worker,
        ICollection<PlaybackFrame> frames,
        TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            if (worker.TryReadLatestFrame(out var frame) && frame is not null)
            {
                frames.Add(frame);
            }

            Thread.Sleep(5);
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static void VerifyReasonableRate(double requestedRate, double observedRate, double simulationTime)
    {
        // Scheduler jitter, fixed-tick quantization, and a short 150 ms sample make exact-rate
        // assertions noisy. These broad bounds still catch a deadline gate that never advances
        // or a coordinator that runs several times faster than requested.
        var lowerBound = requestedRate == 1 ? 0.2 : requestedRate * 0.25;
        var upperBound = Math.Max(2.5, requestedRate * 2.25 + 2);
        Require(observedRate >= lowerBound && observedRate <= upperBound,
            $"requested={requestedRate:0.#}x 的 observed rate 不在合理範圍；observed={observedRate:0.0}x, "
            + $"range={lowerBound:0.0}..{upperBound:0.0}x, simulation={simulationTime:0.0}s。");
    }

    private static void VerifyParity(string root, PlaybackFrame frame)
    {
        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var options = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.Decimated(0.5),
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(1.0),
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology);
        var reference = options.CreateWorld();
        reference.AdvanceTo(frame.SimulationTimeSeconds);
        var referenceSnapshot = reference.GetSnapshot();

        Require(frame.SimulationTimeSeconds == referenceSnapshot.SimulationTimeSeconds,
            "frame 與 reference world 必須使用相同 fixed-tick target。");
        RequireJsonEqual(frame.Trajectory, reference.Trajectory, "trajectory");
        RequireJsonEqual(frame.Events, reference.Events, "events");
        RequireJsonEqual(frame.SafetyHistory, reference.SafetyHistory, "safety history");
        RequireJsonEqual(frame.Trains, referenceSnapshot.Trains, "final trains");
        RequireJsonEqual(frame.CurrentSafety, referenceSnapshot.SafetyObservations, "current safety");
    }

    private static void RequireJsonEqual<T>(IEnumerable<T> actual, IEnumerable<T> expected, string label)
    {
        var actualJson = JsonSerializer.Serialize(actual, ParityJsonOptions);
        var expectedJson = JsonSerializer.Serialize(expected, ParityJsonOptions);
        Require(actualJson == expectedJson,
            $"{label} parity 與相同 target 的 reference world 不一致；actual bytes={actualJson.Length}, "
            + $"expected bytes={expectedJson.Length}。");
    }

    private static PlaybackFrame ReadLatest(SimulationPlaybackWorker worker)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (worker.TryReadLatestFrame(out var frame) && frame is not null)
            {
                return frame;
            }

            Thread.Sleep(2);
        }

        throw new InvalidOperationException("worker 未在預期時間內發布 playback frame。");
    }

    private static PlaybackFrame ReadLatestOr(SimulationPlaybackWorker worker, PlaybackFrame fallback)
    {
        return worker.TryReadLatestFrame(out var frame) && frame is not null ? frame : fallback;
    }

    private static bool IsFixedTick(double timeSeconds)
    {
        var ticks = timeSeconds / SimulationWorld.FixedTimeStepSeconds;
        return Math.Abs(ticks - Math.Round(ticks)) <= 1e-7;
    }

    private static void DisposeWorker(SimulationPlaybackWorker worker)
    {
        Await(worker.DisposeAsync().AsTask(), "worker cleanup");
    }

    private static void Await(Task task, string operation)
    {
        if (!task.Wait(TimeSpan.FromSeconds(2)))
        {
            throw new TimeoutException($"{operation} 超過 2 秒未完成。");
        }

        task.GetAwaiter().GetResult();
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
