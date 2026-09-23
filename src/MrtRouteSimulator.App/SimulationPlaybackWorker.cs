using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Channels;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Read-only metadata captured before the world is handed to its single writer.
/// These references describe the fixed project model; they are never mutated by playback.
/// </summary>
public sealed record PlaybackWorldContext(
    InfrastructureGraph? Infrastructure,
    InfrastructureGraphV4? TopologyInfrastructure,
    SpeedLimitService SpeedLimits,
    ResolvedDispatchPlan? DispatchPlan,
    TopologyResultContext? TopologyResultContext,
    double BaselineCycleTimeSeconds,
    double? HeadwaySeconds)
{
    internal static PlaybackWorldContext Capture(SimulationWorld world)
    {
        InfrastructureGraph? infrastructure = null;
        try
        {
            infrastructure = world.Infrastructure;
        }
        catch (InvalidOperationException)
        {
            // Topology-native worlds intentionally do not expose the legacy graph.
        }

        TopologyResultContext? resultContext = null;
        try
        {
            resultContext = world.GetTopologyResultContext();
        }
        catch (InvalidOperationException)
        {
            // Legacy non-topology worlds do not expose a topology result context.
        }

        return new PlaybackWorldContext(
            infrastructure,
            world.TopologyInfrastructure,
            world.SpeedLimits,
            world.DispatchPlan,
            resultContext,
            world.BaselineCycleTimeSeconds,
            world.HeadwaySeconds);
    }
}

public sealed record PlaybackPerformanceSnapshot(
    double RequestedPlaybackRate,
    double EffectiveSimulationRate,
    double SimulationAdvanceMilliseconds,
    double FrameBuildMilliseconds,
    double FramePublishMilliseconds,
    int ActiveTrainCount,
    int TrajectoryCount,
    int SafetyHistoryCount,
    int EventCount,
    long FrameDropCount);

/// <summary>
/// Immutable presentation data. Histories are persistent immutable lists: appending new engine
/// output creates a new list root and shares the unchanged prefix with earlier frames.
/// </summary>
public sealed record PlaybackFrame(
    Guid GenerationId,
    long Sequence,
    double SimulationTimeSeconds,
    ImmutableArray<WorldTrainState> Trains,
    ImmutableArray<SafetyObservation> CurrentSafety,
    ImmutableArray<SimulationEvent> NewEvents,
    ImmutableList<SimulationEvent> Events,
    ImmutableList<TrajectorySample> Trajectory,
    ImmutableList<SafetyObservation> SafetyHistory,
    ImmutableDictionary<string, TrackPosition> TrainCenterPositions,
    bool IsComplete,
    MovingBlockMode MovingBlockMode,
    BrakingEstimationMode BrakingEstimationMode,
    PlaybackWorldContext Context,
    PlaybackPerformanceSnapshot Performance)
{
    public double CurrentTimeSeconds => SimulationTimeSeconds;

    public IReadOnlyList<WorldTrainState> TrainStates => Trains;

    public IReadOnlyList<SafetyObservation> SafetyObservations => CurrentSafety;

    public InfrastructureGraph? Infrastructure => Context.Infrastructure;

    public InfrastructureGraphV4? TopologyInfrastructure => Context.TopologyInfrastructure;

    public SpeedLimitService SpeedLimits => Context.SpeedLimits;

    public ResolvedDispatchPlan? DispatchPlan => Context.DispatchPlan;

    public double BaselineCycleTimeSeconds => Context.BaselineCycleTimeSeconds;

    public double? HeadwaySeconds => Context.HeadwaySeconds;

    public TopologyResultContext GetTopologyResultContext() => Context.TopologyResultContext
        ?? throw new InvalidOperationException("目前播放 frame 沒有 topology 結果內容。");

    public TrackPosition? GetTrainCenterPosition(string vehicleId) =>
        TrainCenterPositions.TryGetValue(vehicleId, out var position) ? position : null;

    public SimulationSnapshot GetSnapshot() => new(
        SimulationTimeSeconds,
        Trains,
        CurrentSafety,
        NewEvents);
}

/// <summary>
/// Owns and advances one actual SimulationWorld. Commands are reliable and ordered; published UI
/// frames are bounded and latest-frame-wins. The engine still performs every fixed 0.1 s tick.
/// </summary>
public sealed class SimulationPlaybackWorker : IAsyncDisposable
{
    private const double MaximumAdvancePerSliceSeconds = 0.5;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan IdlePlaybackDelay = TimeSpan.FromMilliseconds(2);

    private readonly SimulationWorld _world;
    private readonly PlaybackWorldContext _context;
    private readonly Guid _generationId = Guid.NewGuid();
    private readonly Channel<PlaybackCommand> _commands = Channel.CreateUnbounded<PlaybackCommand>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly Channel<PlaybackFrame> _frames = Channel.CreateBounded<PlaybackFrame>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.DropOldest,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource<PlaybackFrame> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _runTask;
    private readonly object _disposeLock = new();
    private ImmutableList<SimulationEvent> _events = ImmutableList<SimulationEvent>.Empty;
    private ImmutableList<TrajectorySample> _trajectory = ImmutableList<TrajectorySample>.Empty;
    private ImmutableList<SafetyObservation> _safetyHistory = ImmutableList<SafetyObservation>.Empty;
    private int _eventCursor;
    private int _trajectoryCursor;
    private int _safetyCursor;
    private long _sequence;
    private long _acknowledgedFrameSequence;
    private long _lastRateSampleTimestamp;
    private double _lastRateSampleSimulationTime;
    private double _effectiveSimulationRate;
    private double _requestedPlaybackRate = 1;
    private double _baseSimulationTimeSeconds;
    private long _playbackStartTimestamp;
    private long _lastFrameTimestamp;
    private double _lastAdvanceMilliseconds;
    private double _lastFramePublishMilliseconds;
    private bool _isPlaying;
    private bool _isStopping;
    private int _stopRequested;
    private Task? _disposeTask;

    public SimulationPlaybackWorker(SimulationWorld actualWorld)
    {
        _world = actualWorld ?? throw new ArgumentNullException(nameof(actualWorld));
        _context = PlaybackWorldContext.Capture(actualWorld);
        _runTask = Task.Run(RunAsync);
    }

    public Task<PlaybackFrame> Ready => _ready.Task;

    public Task Completion => _runTask;

    public Guid GenerationId => _generationId;

    public bool TryReadLatestFrame(out PlaybackFrame? frame)
    {
        frame = null;
        while (_frames.Reader.TryRead(out var next))
        {
            frame = next;
        }

        if (frame is not null)
        {
            Interlocked.Exchange(ref _acknowledgedFrameSequence, frame.Sequence);
        }

        return frame is not null;
    }

    public Task PlayAsync(double requestedRate) => EnqueueAsync(PlaybackCommand.Play(requestedRate));

    public Task PauseAsync() => EnqueueAsync(PlaybackCommand.Pause());

    public Task ResetAsync() => EnqueueAsync(PlaybackCommand.Reset());

    public Task SetPlaybackRateAsync(double requestedRate) => EnqueueAsync(PlaybackCommand.SetRate(requestedRate));

    public Task SetMovingBlockModeAsync(MovingBlockMode mode) => EnqueueAsync(PlaybackCommand.SetMovingBlock(mode));

    public Task SetBrakingEstimationModeAsync(BrakingEstimationMode mode) => EnqueueAsync(PlaybackCommand.SetBrakingMode(mode));

    /// <summary>
    /// Advances a paused world to a requested fixed-step time on its owning worker. Production
    /// playback uses the Stopwatch coordinator; this command also supports deterministic callers.
    /// </summary>
    public Task AdvanceToSimulationTimeAsync(double targetTimeSeconds) =>
        EnqueueAsync(PlaybackCommand.AdvanceTo(targetTimeSeconds));

    public Task ScheduleObstacleEmergencyStopAsync(string vehicleId, double delaySeconds) =>
        EnqueueAsync(PlaybackCommand.ScheduleObstacle(vehicleId, delaySeconds));

    public Task TriggerObstacleEmergencyStopAsync(string vehicleId) =>
        EnqueueAsync(PlaybackCommand.TriggerObstacle(vehicleId));

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            Interlocked.Exchange(ref _stopRequested, 1);
            _disposeTask ??= StopCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task StopCoreAsync()
    {
        if (!_runTask.IsCompleted)
        {
            var completion = NewCompletion();
            if (_commands.Writer.TryWrite(PlaybackCommand.Stop(completion)))
            {
                await completion.Task.ConfigureAwait(false);
            }
        }

        try
        {
            await _runTask.ConfigureAwait(false);
        }
        finally
        {
            _commands.Writer.TryComplete();
            _cancellation.Cancel();
            _frames.Writer.TryComplete();
            _cancellation.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            PublishFrame(force: true);
            while (!_cancellation.IsCancellationRequested)
            {
                if (_commands.Reader.TryRead(out var command))
                {
                    Execute(command);
                    if (_isStopping)
                    {
                        break;
                    }

                    continue;
                }

                if (!_isPlaying)
                {
                    await _commands.Reader.WaitToReadAsync(_cancellation.Token).ConfigureAwait(false);
                    continue;
                }

                AdvancePlaybackSlice();
                var now = Stopwatch.GetTimestamp();
                if (now - _lastFrameTimestamp >= FrameInterval.TotalSeconds * Stopwatch.Frequency
                    || _world.IsComplete)
                {
                    PublishFrame(force: true);
                }

                if (!_isPlaying)
                {
                    continue;
                }

                if (GetTargetSimulationTime() <= _world.CurrentTimeSeconds + 1e-8)
                {
                    await Task.Delay(IdlePlaybackDelay, _cancellation.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            while (_commands.Reader.TryRead(out var pending))
            {
                pending.Completion?.TrySetException(exception);
            }

            throw;
        }
        finally
        {
            _isPlaying = false;
            _commands.Writer.TryComplete();
            _frames.Writer.TryComplete();
        }
    }

    private void Execute(PlaybackCommand command)
    {
        try
        {
            switch (command.Kind)
            {
                case PlaybackCommandKind.Play:
                    SetRate(command.Number);
                    _isPlaying = !_world.IsComplete;
                    ResetRateWindow();
                    break;
                case PlaybackCommandKind.Pause:
                    _isPlaying = false;
                    _effectiveSimulationRate = 0;
                    break;
                case PlaybackCommandKind.Reset:
                    _isPlaying = false;
                    _world.Reset();
                    _events = ImmutableList<SimulationEvent>.Empty;
                    _trajectory = ImmutableList<TrajectorySample>.Empty;
                    _safetyHistory = ImmutableList<SafetyObservation>.Empty;
                    _eventCursor = 0;
                    _trajectoryCursor = 0;
                    _safetyCursor = 0;
                    _effectiveSimulationRate = 0;
                    ResetRateWindow();
                    break;
                case PlaybackCommandKind.SetRate:
                    SetRate(command.Number);
                    if (_isPlaying)
                    {
                        ResetRateWindow();
                    }

                    break;
                case PlaybackCommandKind.SetMovingBlock:
                    _world.SetMovingBlockMode(command.MovingBlockMode);
                    break;
                case PlaybackCommandKind.SetBrakingMode:
                    _world.SetBrakingEstimationMode(command.BrakingEstimationMode);
                    break;
                case PlaybackCommandKind.AdvanceTo:
                    _isPlaying = false;
                    _world.AdvanceTo(command.Number);
                    _effectiveSimulationRate = 0;
                    ResetRateWindow();
                    break;
                case PlaybackCommandKind.ScheduleObstacle:
                    _world.ScheduleObstacleEmergencyStop(command.VehicleId!, _world.CurrentTimeSeconds + command.Number);
                    break;
                case PlaybackCommandKind.TriggerObstacle:
                    _world.TriggerObstacleEmergencyStop(command.VehicleId!);
                    break;
                case PlaybackCommandKind.Stop:
                    _isPlaying = false;
                    _isStopping = true;
                    break;
                default:
                    throw new InvalidOperationException("不支援的播放命令。");
            }

            if (command.Kind != PlaybackCommandKind.Stop)
            {
                PublishFrame(force: true);
            }

            command.Completion?.TrySetResult(true);
        }
        catch (Exception exception)
        {
            command.Completion?.TrySetException(exception);
            if (command.Completion is null)
            {
                throw;
            }
        }
    }

    private void SetRate(double requestedRate)
    {
        if (!double.IsFinite(requestedRate) || requestedRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedRate), "播放倍率必須是正的有限數字。");
        }

        _requestedPlaybackRate = requestedRate;
    }

    private void ResetRateWindow()
    {
        _baseSimulationTimeSeconds = _world.CurrentTimeSeconds;
        _playbackStartTimestamp = Stopwatch.GetTimestamp();
        _lastRateSampleTimestamp = _playbackStartTimestamp;
        _lastRateSampleSimulationTime = _world.CurrentTimeSeconds;
    }

    private double GetTargetSimulationTime()
    {
        var elapsedSeconds = Stopwatch.GetElapsedTime(_playbackStartTimestamp).TotalSeconds;
        return _baseSimulationTimeSeconds + elapsedSeconds * _requestedPlaybackRate;
    }

    private void AdvancePlaybackSlice()
    {
        if (_world.IsComplete)
        {
            _isPlaying = false;
            _effectiveSimulationRate = 0;
            return;
        }

        var target = GetTargetSimulationTime();
        var boundedTarget = Math.Min(target, _world.CurrentTimeSeconds + MaximumAdvancePerSliceSeconds);
        if (boundedTarget <= _world.CurrentTimeSeconds + 1e-8)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        _world.AdvanceTo(boundedTarget);
        stopwatch.Stop();
        _lastAdvanceMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

        if (_world.IsComplete)
        {
            _isPlaying = false;
            _effectiveSimulationRate = 0;
        }
        else
        {
            UpdateEffectiveRate();
        }
    }

    private void UpdateEffectiveRate()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = (now - _lastRateSampleTimestamp) / (double)Stopwatch.Frequency;
        if (elapsedSeconds < 0.5)
        {
            return;
        }

        _effectiveSimulationRate = Math.Max(0, (_world.CurrentTimeSeconds - _lastRateSampleSimulationTime) / elapsedSeconds);
        _lastRateSampleTimestamp = now;
        _lastRateSampleSimulationTime = _world.CurrentTimeSeconds;
    }

    private void PublishFrame(bool force)
    {
        if (!force && Stopwatch.GetElapsedTime(_lastFrameTimestamp).TotalMilliseconds < FrameInterval.TotalMilliseconds)
        {
            return;
        }

        var frameBuild = Stopwatch.StartNew();
        var snapshot = _world.GetSnapshot();
        var newEvents = FreezeEvents(snapshot.NewEvents);
        AppendNewValues(_world.Events, ref _eventCursor, item => FreezeEvent(item), ref _events);
        AppendNewValues(_world.Trajectory, ref _trajectoryCursor, item => item, ref _trajectory);
        AppendNewValues(_world.SafetyHistory, ref _safetyCursor, item => item, ref _safetyHistory);
        _world.AcknowledgeSnapshotEvents();

        var centers = ImmutableDictionary.CreateBuilder<string, TrackPosition>(StringComparer.OrdinalIgnoreCase);
        foreach (var train in snapshot.Trains)
        {
            if (_world.GetTrainCenterPosition(train.VehicleId) is { } center)
            {
                centers[train.VehicleId] = center;
            }
        }

        var sequence = Interlocked.Increment(ref _sequence);
        var frameBuildMilliseconds = frameBuild.Elapsed.TotalMilliseconds;
        var performance = new PlaybackPerformanceSnapshot(
            _requestedPlaybackRate,
            _isPlaying ? _effectiveSimulationRate : 0,
            _lastAdvanceMilliseconds,
            frameBuildMilliseconds,
            _lastFramePublishMilliseconds,
            snapshot.Trains.Count(train => train.IsActive),
            _trajectory.Count,
            _safetyHistory.Count,
            _events.Count,
            Math.Max(0, sequence - Interlocked.Read(ref _acknowledgedFrameSequence) - 1));
        var frame = new PlaybackFrame(
            _generationId,
            sequence,
            snapshot.SimulationTimeSeconds,
            snapshot.Trains.ToImmutableArray(),
            snapshot.SafetyObservations.ToImmutableArray(),
            newEvents,
            _events,
            _trajectory,
            _safetyHistory,
            centers.ToImmutable(),
            _world.IsComplete,
            _world.MovingBlockMode,
            _world.BrakingEstimationMode,
            _context,
            performance);

        var publish = Stopwatch.StartNew();
        _frames.Writer.TryWrite(frame);
        publish.Stop();
        _lastFramePublishMilliseconds = publish.Elapsed.TotalMilliseconds;
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        _ready.TrySetResult(frame);
    }

    private void AppendNewValues<T>(
        IReadOnlyList<T> source,
        ref int cursor,
        Func<T, T> freeze,
        ref ImmutableList<T> destination)
    {
        var count = source.Count;
        if (count < cursor)
        {
            cursor = 0;
            destination = ImmutableList<T>.Empty;
        }

        if (count == cursor)
        {
            return;
        }

        var builder = ImmutableArray.CreateBuilder<T>(count - cursor);
        for (var index = cursor; index < count; index++)
        {
            builder.Add(freeze(source[index]));
        }

        destination = destination.AddRange(builder.MoveToImmutable());
        cursor = count;
    }

    private static ImmutableArray<SimulationEvent> FreezeEvents(IEnumerable<SimulationEvent> events) =>
        events.Select(FreezeEvent).ToImmutableArray();

    private static SimulationEvent FreezeEvent(SimulationEvent simulationEvent) =>
        simulationEvent with
        {
            ResourceIds = simulationEvent.ResourceIds?.ToImmutableArray()
        };

    private Task EnqueueAsync(PlaybackCommand command)
    {
        lock (_disposeLock)
        {
            if (Volatile.Read(ref _stopRequested) != 0 || _runTask.IsCompleted || !_commands.Writer.TryWrite(command))
            {
                command.Completion?.TrySetException(new ObjectDisposedException(nameof(SimulationPlaybackWorker)));
            }
        }

        return command.Completion?.Task ?? Task.CompletedTask;
    }

    private static TaskCompletionSource<bool> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record PlaybackCommand(
        PlaybackCommandKind Kind,
        double Number = 0,
        string? VehicleId = null,
        MovingBlockMode MovingBlockMode = default,
        BrakingEstimationMode BrakingEstimationMode = default,
        TaskCompletionSource<bool>? Completion = null)
    {
        public static PlaybackCommand Play(double rate) => new(PlaybackCommandKind.Play, rate, Completion: NewCompletion());
        public static PlaybackCommand Pause() => new(PlaybackCommandKind.Pause, Completion: NewCompletion());
        public static PlaybackCommand Reset() => new(PlaybackCommandKind.Reset, Completion: NewCompletion());
        public static PlaybackCommand SetRate(double rate) => new(PlaybackCommandKind.SetRate, rate, Completion: NewCompletion());
        public static PlaybackCommand SetMovingBlock(MovingBlockMode mode) => new(PlaybackCommandKind.SetMovingBlock, MovingBlockMode: mode, Completion: NewCompletion());
        public static PlaybackCommand SetBrakingMode(BrakingEstimationMode mode) => new(PlaybackCommandKind.SetBrakingMode, BrakingEstimationMode: mode, Completion: NewCompletion());
        public static PlaybackCommand AdvanceTo(double targetTimeSeconds) => new(PlaybackCommandKind.AdvanceTo, targetTimeSeconds, Completion: NewCompletion());
        public static PlaybackCommand ScheduleObstacle(string vehicleId, double delay) => new(PlaybackCommandKind.ScheduleObstacle, delay, vehicleId, Completion: NewCompletion());
        public static PlaybackCommand TriggerObstacle(string vehicleId) => new(PlaybackCommandKind.TriggerObstacle, VehicleId: vehicleId, Completion: NewCompletion());
        public static PlaybackCommand Stop(TaskCompletionSource<bool> completion) => new(PlaybackCommandKind.Stop, Completion: completion);
    }

    private enum PlaybackCommandKind
    {
        Play,
        Pause,
        Reset,
        SetRate,
        SetMovingBlock,
        SetBrakingMode,
        AdvanceTo,
        ScheduleObstacle,
        TriggerObstacle,
        Stop
    }
}

public sealed record PlannedTimelineArtifact(
    ImmutableArray<SimulationEvent> Events,
    ImmutableArray<TrajectorySample> Trajectory,
    double CompletedAtSeconds);

public static class PlannedTimelineWorker
{
    private const double AdvanceSliceSeconds = 2;

    public static Task<PlannedTimelineArtifact> ComputeAsync(
        SimulationWorldOptions options,
        double durationSeconds,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Compute(options, durationSeconds, cancellationToken), cancellationToken);

    private static PlannedTimelineArtifact Compute(
        SimulationWorldOptions options,
        double durationSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "計畫時間軸長度必須是非負有限數值。");
        }

        var world = options.CreateWorld();
        while (!world.IsComplete && world.CurrentTimeSeconds < durationSeconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            world.AdvanceTo(Math.Min(durationSeconds, world.CurrentTimeSeconds + AdvanceSliceSeconds));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new PlannedTimelineArtifact(
            world.Events.Select(simulationEvent => simulationEvent with
            {
                ResourceIds = simulationEvent.ResourceIds?.ToImmutableArray()
            }).ToImmutableArray(),
            world.Trajectory.ToImmutableArray(),
            world.CurrentTimeSeconds);
    }
}
