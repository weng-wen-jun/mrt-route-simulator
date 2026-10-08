using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.Automation;

/// <summary>One serialized headless session; the existing Engine remains the sole runtime.</summary>
public sealed class SimulationAutomationSession
{
    public TopologyProjectDocument? Document { get; private set; }
    public string? SourcePath { get; private set; }
    // SDK tool objects may be constructed per invocation; the mutable session owns its gate.
    public SemaphoreSlim OperationGate { get; } = new(1);
    private SimulationWorld? _world;
    public SimulationWorld World => _world ?? throw new InvalidOperationException("請先載入專案。");

    public void Load(TopologyProjectDocument document, string? sourcePath = null)
    {
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
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(1),
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology);
        var candidate = options.CreateWorld();
        // Do not replace a working session until validation and runtime construction succeed.
        _world = candidate;
        Document = document;
        SourcePath = sourcePath;
    }

    public object Snapshot() => new
    {
        mode = "headless",
        projectId = Document?.ProjectId,
        projectName = Document?.ProjectName,
        sourcePath = SourcePath,
        simulationTimeSeconds = World.CurrentTimeSeconds,
        isComplete = World.IsComplete,
        eventCount = World.Events.Count,
        // MCP snapshots are repeatable state reads; event history is paged separately.
        snapshot = World.GetSnapshot() with { NewEvents = Array.Empty<SimulationEvent>() }
    };

    public object Advance(double targetSeconds, CancellationToken cancellationToken)
    {
        ValidateAdvance(targetSeconds, World.CurrentTimeSeconds);
        while (World.CurrentTimeSeconds + SimulationWorld.FixedTimeStepSeconds <= targetSeconds + 1e-8)
        {
            cancellationToken.ThrowIfCancellationRequested();
            World.Tick();
            // Event history remains in World.Events; do not copy its entire prefix every tick.
            World.AcknowledgeSnapshotEvents();
        }
        return Snapshot();
    }

    public static void ValidateAdvance(double targetSeconds, double currentSeconds)
    {
        if (!double.IsFinite(targetSeconds) || targetSeconds < currentSeconds
            || targetSeconds > 86400 || targetSeconds - currentSeconds > 60)
            throw new ArgumentOutOfRangeException(nameof(targetSeconds),
                "目標時間須為有限數值、不可倒退、最多 86400 秒，每次最多推進 60 秒；倒退請先重設。");
    }

    public static object Page<T>(IReadOnlyList<T> values, int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "offset >= 0；limit 為 1～500。");
        return new { total = values.Count, offset, items = values.Skip(offset).Take(limit).ToArray() };
    }

    public object Timetable(int offset, int limit) => Page(
        OperationsTimetable.Build(World.GetTopologyResultContext(), World.DispatchPlan!, [], World.Events), offset, limit);
}
