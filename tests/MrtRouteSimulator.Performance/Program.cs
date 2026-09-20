using System.Diagnostics;
using System.Text.Json;
using MrtRouteSimulator.Engine;

var root = FindRepositoryRoot();
var samplePath = args.Length > 0
    ? Path.GetFullPath(args[0], root)
    : Path.Combine(root, "samples", "V4.0.0-完整拓撲執行驗證範例.mrtsim.json");
var advanceSeconds = args.Length > 1 && double.TryParse(args[1], out var parsedSeconds)
    ? parsedSeconds
    : 8000;

if (!File.Exists(samplePath))
{
    throw new FileNotFoundException("找不到 benchmark sample。", samplePath);
}

var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
var runtime = TopologyProjectFormat.CreateRuntime(document);
var actualOptions = CreateOptions(document, runtime, SimulationTraceRetentionPolicy.Full);
var plannedOptions = actualOptions with
{
    ProfileMode = OperationProfileMode.BasicPhysics,
    MovingBlockMode = MovingBlockMode.Independent,
    InitialBrakingEstimationMode = BrakingEstimationMode.Service
};
var latestDispatchOffsetSeconds = runtime.DispatchPlan.Runs.Max(run =>
{
    var offset = (run.PlannedDepartureTime - runtime.DispatchPlan.ScheduleAnchorTime).TotalSeconds;
    return offset < 0 ? offset + TimeSpan.FromDays(1).TotalSeconds : offset;
});
var plannedRequestedSeconds = latestDispatchOffsetSeconds + actualOptions.CreateWorld().BaselineCycleTimeSeconds * 1.5;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine(JsonSerializer.Serialize(new
{
    sample = Path.GetFileName(samplePath),
    advanceSeconds,
    plannedRequestedSeconds,
    metrics = new
    {
        singleActual = MeasureWorld(actualOptions, advanceSeconds),
        dualSession = MeasureSession(actualOptions, plannedOptions, advanceSeconds),
        plannedTimeline = MeasurePlannedTimeline(actualOptions, plannedOptions, plannedRequestedSeconds)
    }
}, new JsonSerializerOptions { WriteIndented = true }));

static SimulationWorldOptions CreateOptions(
    TopologyProjectDocument document,
    TopologyProjectRuntime runtime,
    SimulationTraceRetentionPolicy retentionPolicy) =>
    new(
        Route: null,
        TrainParameters: runtime.TrainParameters,
        OperationalParameters: runtime.OperationalParameters,
        TrainCount: runtime.DispatchPlan.Runs.Count,
        InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
        ProfileMode: document.Simulation.ProfileMode,
        MovingBlockMode: document.Simulation.MovingBlockMode,
        InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode,
        TraceRetentionPolicy: retentionPolicy,
        ServicePatterns: runtime.ServicePatterns,
        DispatchPlan: runtime.DispatchPlan,
        VehicleTypes: runtime.VehicleTypes,
        ServiceTypes: runtime.ServiceTypes,
        Topology: runtime.Topology);

static object MeasureWorld(SimulationWorldOptions options, double durationSeconds)
{
    var world = options.CreateWorld();
    var before = CaptureMemory();
    var stopwatch = Stopwatch.StartNew();
    world.AdvanceTo(durationSeconds);
    stopwatch.Stop();
    return ToMetric(stopwatch, world, before, durationSeconds);
}

static object MeasureSession(
    SimulationWorldOptions actualOptions,
    SimulationWorldOptions plannedOptions,
    double durationSeconds)
{
    var session = new SimulationSession(actualOptions, plannedOptions);
    var before = CaptureMemory();
    var stopwatch = Stopwatch.StartNew();
    session.AdvanceTo(durationSeconds);
    stopwatch.Stop();
    return new
    {
        elapsedMs = stopwatch.Elapsed.TotalMilliseconds,
        fixedTicks = FixedTickCount(durationSeconds),
        millisecondsPerTick = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, FixedTickCount(durationSeconds)),
        actualTrajectoryCount = session.ActualWorld.Trajectory.Count,
        plannedTrajectoryCount = session.PlannedWorld.Trajectory.Count,
        actualSafetyHistoryCount = session.ActualWorld.SafetyHistory.Count,
        plannedSafetyHistoryCount = session.PlannedWorld.SafetyHistory.Count,
        actualEventCount = session.ActualWorld.Events.Count,
        plannedEventCount = session.PlannedWorld.Events.Count,
        workingSetDeltaBytes = Process.GetCurrentProcess().WorkingSet64 - before.WorkingSetBytes,
        managedMemoryDeltaBytes = GC.GetTotalMemory(false) - before.ManagedBytes
    };
}

static object MeasurePlannedTimeline(
    SimulationWorldOptions actualOptions,
    SimulationWorldOptions plannedOptions,
    double requestedDurationSeconds)
{
    var session = new SimulationSession(actualOptions, plannedOptions);
    var before = CaptureMemory();
    var stopwatch = Stopwatch.StartNew();
    session.PreparePlannedTimeline(requestedDurationSeconds);
    stopwatch.Stop();
    var lastEvent = session.PlannedEvents.Count == 0
        ? (double?)null
        : session.PlannedEvents.Max(item => item.SimulationTimeSeconds);
    return new
    {
        requestedDurationSeconds,
        elapsedMs = stopwatch.Elapsed.TotalMilliseconds,
        fixedTicks = FixedTickCount(requestedDurationSeconds),
        millisecondsPerTick = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, FixedTickCount(requestedDurationSeconds)),
        actualCompletionTimeSeconds = session.PlannedWorld.CurrentTimeSeconds,
        lastEventTimeSeconds = lastEvent,
        trajectoryCount = session.PlannedTrajectory.Count,
        safetyHistoryCount = session.PlannedWorld.SafetyHistory.Count,
        eventCount = session.PlannedEvents.Count,
        workingSetDeltaBytes = Process.GetCurrentProcess().WorkingSet64 - before.WorkingSetBytes,
        managedMemoryDeltaBytes = GC.GetTotalMemory(false) - before.ManagedBytes
    };
}

static object ToMetric(
    Stopwatch stopwatch,
    SimulationWorld world,
    MemorySnapshot before,
    double durationSeconds) => new
{
    elapsedMs = stopwatch.Elapsed.TotalMilliseconds,
    fixedTicks = FixedTickCount(durationSeconds),
    millisecondsPerTick = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, FixedTickCount(durationSeconds)),
    currentTimeSeconds = world.CurrentTimeSeconds,
    trajectoryCount = world.Trajectory.Count,
    safetyHistoryCount = world.SafetyHistory.Count,
    eventCount = world.Events.Count,
    workingSetDeltaBytes = Process.GetCurrentProcess().WorkingSet64 - before.WorkingSetBytes,
    managedMemoryDeltaBytes = GC.GetTotalMemory(false) - before.ManagedBytes
};

static long FixedTickCount(double durationSeconds) =>
    (long)Math.Floor((durationSeconds + 1e-7) / SimulationWorld.FixedTimeStepSeconds);

static MemorySnapshot CaptureMemory() =>
    new(Process.GetCurrentProcess().WorkingSet64, GC.GetTotalMemory(false));

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new InvalidOperationException("找不到 repository root。");
}

readonly record struct MemorySnapshot(long WorkingSetBytes, long ManagedBytes);
