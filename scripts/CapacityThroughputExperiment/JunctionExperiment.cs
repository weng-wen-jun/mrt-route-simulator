using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

/// <summary>Handbook case 6: physical diamond crossing and branch-to-opposite-main merge.</summary>
public static class JunctionExperiment
{
    private const string Version = "junction-physical-v3-route-change";
    private const string CrossingResource = "TOPOLOGY:SWITCH:C";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() } };

    public static void Run(string root, MeasurementPlan plan, string outputRoot, double? onlyRatio = null)
    {
        var output = Path.Combine(outputRoot, "junctions");
        Directory.CreateDirectory(Path.Combine(output, "projects"));
        plan.WriteManifest(output);
        foreach (var ratio in Enumerable.Range(0, 11).Select(i => i / 10d).Where(r => onlyRatio is null || Math.Abs(r - onlyRatio.Value) < 1e-8))
            Execute(root, output, plan, ratio, smoke: false);
    }

    public static void Smoke(string root, string outputRoot)
    {
        var output = Path.Combine(outputRoot, "junction-smoke");
        Directory.CreateDirectory(Path.Combine(output, "projects"));
        var plan = MeasurementPlan.Parse(["--horizon-hours=0.5", "--discard-minutes=0", "--bucket-minutes=5"]);
        foreach (var ratio in new[] { 0d, .5, 1d }) Execute(root, output, plan, ratio, smoke: true);
    }

    private static void Execute(string root, string output, MeasurementPlan plan, double ratio, bool smoke)
    {
        var sourcePath = Directory.GetFiles(Path.Combine(root, "samples", "handbook-capacity"), "06-*.mrtsim.json").Single();
        var source = TopologyProjectFormat.Deserialize(File.ReadAllText(sourcePath));
        var point = source.SpatialCapacity!.Points.Single(p => p.Kind == SpatialReferencePointKind.Junction) with { MainlineTrafficRatio = ratio };
        var theory = SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(point), source.SpatialCapacity.Global,
            source.SpatialCapacity.Train, SpatialCapacityDirection.Forward, SpatialCapacityCalculationMode.TaiwanHandbook);
        var document = Build(source, point, theory.DesignHeadwaySeconds, plan.TotalHorizonSeconds);
        // Structural gate precedes all runtime creation; no relaxed connection/port validation.
        TopologyProjectFormat.Validate(document);
        var projectText = TopologyProjectFormat.Serialize(document);
        var name = "case06-r" + ratio.ToString("0.0", CultureInfo.InvariantCulture);
        var engineHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));
        var projectHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(projectText)));
        var cacheKey = $"{Version}|{plan.CacheKey}|{engineHash}|{projectHash}";
        var metricsPath = Path.Combine(output, name + ".metrics.json");
        if (File.Exists(metricsPath))
        {
            using var previous = JsonDocument.Parse(File.ReadAllText(metricsPath));
            if (previous.RootElement.GetProperty("cacheKey").GetString() == cacheKey
                && previous.RootElement.GetProperty("simulationTimeSeconds").GetDouble() >= plan.TotalHorizonSeconds - .11)
            { Console.WriteLine("RESUME JUNCTION " + name); return; }
        }
        File.WriteAllText(Path.Combine(output, "projects", name + ".mrtsim.json"), projectText);
        // 24 rows cover a full ten-main-gap pattern even at r=.9, so the gate
        // exercises the rare branch instead of validating only its main route.
        var gateRows = document.Dispatch.ManualTimetableRows!.Take(24).ToArray();
        var gateDocument = document with { Dispatch = document.Dispatch with { ManualTimetableRows = gateRows },
            Simulation = document.Simulation with { TrainCount = gateRows.Length } };
        TopologyProjectFormat.Validate(gateDocument);
        var gate = CreateWorld(gateDocument);
        gate.AdvanceTo(1800);
        var gateCrossings = gate.Events.Where(e => e.EventType == SimulationEventType.RouteReleased
            && (e.ResourceId == CrossingResource || e.ResourceIds?.Contains(CrossingResource) == true)).ToArray();
        var gateViolations = gate.Events.Where(e => e.EventType is SimulationEventType.Collision or SimulationEventType.PredictedCollision
            or SimulationEventType.StationStopViolation or SimulationEventType.SignalAuthorityViolation).ToArray();
        Write(Path.Combine(output, name + ".operational-gate.json"), new { structuralPassed = true, simulationTimeSeconds = gate.CurrentTimeSeconds,
            plannedRuns = gateRows.Length, crossingRuns = gateCrossings.Select(e => e.ServiceRunId).Distinct().Count(),
            mainCrossings = gateCrossings.Count(e => e.ServiceRouteId == "MAIN"), branchCrossings = gateCrossings.Count(e => e.ServiceRouteId == "BRANCH"), violations = gateViolations });
        if (gateCrossings.Select(e => e.ServiceRunId).Distinct().Count() != gateRows.Length || gateViolations.Length != 0)
            throw new InvalidOperationException($"CASE06 bounded operational gate failed for {name}; inspect operational-gate.json before running long horizon.");
        var mainTime = (point.MainlineSafetyFactor * point.MainlineApproachCruiseSpeedMetersPerSecond
            / (2 * source.SpatialCapacity.Train.BrakeEffect * source.SpatialCapacity.Train.DecelerationMetersPerSecondSquared)
            + (source.SpatialCapacity.Train.LengthMeters + point.CrossoverLengthMeters) / point.MainlineApproachCruiseSpeedMetersPerSecond);
        Write(Path.Combine(output, name + ".manifest.json"), new
        {
            name, sourcePath, version = Version, engineAssembly = typeof(SimulationWorld).Assembly.Location, engineHash, projectHash, cacheKey,
            ratioDefinition = "r = main-to-main gaps without a branch / all consecutive main gaps; not main traffic share (handbook p2-42/PDF518)",
            deterministicPattern = "Each 10 main gaps inserts one branch in floor cumulative (1-r) gaps; no branch-to-branch slots. A branch gap has planned main spacing h(r=0), a consecutive-main gap h(r=1); weighted mean equals h(r). Branch demand phase=tMain+ti. These are planned inputs, never runtime permission gates. Actual r measured from crossing rear-clear release sequence.",
            sourceEvidence = new[] { "PDF607 Fig6-24: measured eastbound main65 km/h, branch65, crossover100m at40 km/h; branch crosses eastbound then merges westbound", "PDF608 Fig6-25: main/branch grades0, Qmain=Qbranch=1.5, r=.5; sensitivity0..1", "PDF518 p2-42: conditional main-gap definition and examples r0,.5,1" },
            sourcePoint = point, theory, sourceGlobal = source.SpatialCapacity.Global, sourceTrain = source.SpatialCapacity.Train,
            separationOfEvidence = "Train141m,a=b1,brakeEffect.75; ti10,reaction1,PHF.8,margin.3 are inherited documented handbook/default assumptions from carrier sample, not stated again by Fig6-24/25.",
            runtime = new { mainCruise = 65 / 3.6, branchCruise = 65 / 3.6, crossoverCruise = 40 / 3.6,
                crossoverLength = 100, interchangeDelay = source.SpatialCapacity.Global.InterchangeTimeSeconds, brakingSafetyFactor = 1.5,
                nominalMainHeadway = theory.DesignHeadwaySeconds, branchPhaseAfterMain = mainTime + source.SpatialCapacity.Global.InterchangeTimeSeconds,
                noAnalyticalHeadwayGate = true, zeroSourceDwell = true, signalRearClearAndSwitchReservations = true },
            assumptionsAndLimits = new[] { "Approach2000m and exit2100m are synthetic isolation geometry; source/exit zero-dwell boundary platforms are not real stations.", "No opposing upper-main traffic is added: the handbook does not specify it. Upper main is represented by branch exit and inactive merge arm.", "Q expands actual signal braking envelope; it does not force the exact analytical D position or precomputed Ts hold.", "Signal interchange delay applies only when switching MAIN/BRANCH after rear-clear, using explicit route-change-only block policy.", "r is a planned deterministic gap pattern. Resource arbitration may reorder actual movements; achieved ratio is reported, never assumed.", "Origin acceleration, jerk1, brake build-up1, positioning2m, margin5m and min-gap10m are declared carrier operational assumptions. PHF and capacity margin are analytical only." },
            plan
        });
        var world = CreateWorld(document);
        var timer = Stopwatch.StartNew();
        foreach (var boundary in plan.ProgressBoundaries(900))
        {
            world.AdvanceTo(boundary);
            Write(Path.Combine(output, name + ".progress.json"), new { ratio, simulationTimeSeconds = world.CurrentTimeSeconds, wallSeconds = timer.Elapsed.TotalSeconds });
            Console.WriteLine($"JUNCTION {name} {world.CurrentTimeSeconds:F0}/{plan.TotalHorizonSeconds:F0}s wall={timer.Elapsed.TotalSeconds:F1}s");
        }
        var rows = document.Dispatch.ManualTimetableRows!;
        bool HasCrossing(SimulationEvent e) => e.ResourceId == CrossingResource || e.ResourceIds?.Contains(CrossingResource) == true;
        var crossings = world.Events.Where(e => e.EventType == SimulationEventType.RouteReleased && HasCrossing(e))
            .OrderBy(e => e.SimulationTimeSeconds).ToArray();
        var measured = crossings.Where(e => plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).ToArray();
        var main = measured.Where(e => e.ServiceRouteId == "MAIN").ToArray();
        var branch = measured.Where(e => e.ServiceRouteId == "BRANCH").ToArray();
        var mainGaps = new List<object>();
        var noBranchGaps = 0;
        var allMainGaps = 0;
        for (var index = 1; index < main.Length; index++)
        {
            var branchCount = measured.Count(e => e.ServiceRouteId == "BRANCH" && e.SimulationTimeSeconds > main[index - 1].SimulationTimeSeconds
                && e.SimulationTimeSeconds < main[index].SimulationTimeSeconds);
            allMainGaps++; if (branchCount == 0) noBranchGaps++;
            mainGaps.Add(new { previousMain = main[index - 1].ServiceRunId, main = main[index].ServiceRunId,
                previousRearClearSeconds = main[index - 1].SimulationTimeSeconds, rearClearSeconds = main[index].SimulationTimeSeconds,
                branchCount, seconds = main[index].SimulationTimeSeconds - main[index - 1].SimulationTimeSeconds });
        }
        var planned = rows.ToDictionary(r => r.ServiceRunId!, r => r.PlannedDepartureTimeSeconds);
        var departures = world.Events.Where(e => e.EventType == SimulationEventType.Departure && (e.StationId == "MO" || e.StationId == "BO"))
            .GroupBy(e => e.ServiceRunId).Select(g => g.First()).ToArray();
        var delays = departures.Where(e => plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).Select(e => e.SimulationTimeSeconds - planned[e.ServiceRunId]).ToArray();
        var crossingTravelTimeBuckets = Enumerable.Range(0, plan.BucketCount).Select(i => new
        {
            bucket = i,
            movements = new[] { "MAIN", "BRANCH" }.Select(route =>
            {
                var samples = measured.Where(e => e.ServiceRouteId == route && plan.IsInBucket(e.SimulationTimeSeconds, i))
                    .Select(e => e.SimulationTimeSeconds - planned[e.ServiceRunId]).ToArray();
                return new { route, count = samples.Length, meanSeconds = samples.Length == 0 ? (double?)null : samples.Average(),
                    maxSeconds = samples.Length == 0 ? (double?)null : samples.Max() };
            }).ToArray()
        }).ToArray();
        var crossingTravelTimeHalfTrend = new[] { "MAIN", "BRANCH" }.Select(route =>
        {
            var early = measured.Where(e => e.ServiceRouteId == route && e.SimulationTimeSeconds < plan.MeasurementMidpointSeconds)
                .Select(e => e.SimulationTimeSeconds - planned[e.ServiceRunId]).ToArray();
            var late = measured.Where(e => e.ServiceRouteId == route && e.SimulationTimeSeconds >= plan.MeasurementMidpointSeconds)
                .Select(e => e.SimulationTimeSeconds - planned[e.ServiceRunId]).ToArray();
            return new { route, firstHalfMeanSeconds = early.Length == 0 ? (double?)null : early.Average(),
                secondHalfMeanSeconds = late.Length == 0 ? (double?)null : late.Average(),
                changeSeconds = early.Length == 0 || late.Length == 0 ? (double?)null : late.Average() - early.Average() };
        }).ToArray();
        var delayBuckets = Enumerable.Range(0, plan.BucketCount).Select(i =>
        {
            var samples = departures.Where(e => plan.IsInBucket(e.SimulationTimeSeconds, i)).Select(e => e.SimulationTimeSeconds - planned[e.ServiceRunId]).ToArray();
            return new { bucket = i, count = samples.Length, meanSeconds = samples.Length == 0 ? (double?)null : samples.Average(), maxSeconds = samples.Length == 0 ? (double?)null : samples.Max() };
        }).ToArray();
        object Pending(double at) => new { timeSeconds = at,
            main = rows.Count(r => r.ServiceRouteId == "MAIN" && r.PlannedDepartureTimeSeconds < at) - crossings.Count(e => e.ServiceRouteId == "MAIN" && e.SimulationTimeSeconds < at),
            branch = rows.Count(r => r.ServiceRouteId == "BRANCH" && r.PlannedDepartureTimeSeconds < at) - crossings.Count(e => e.ServiceRouteId == "BRANCH" && e.SimulationTimeSeconds < at) };
        var collisions = world.Events.Count(e => e.EventType == SimulationEventType.Collision);
        var predicted = world.Events.Count(e => e.EventType == SimulationEventType.PredictedCollision);
        var stopViolations = world.Events.Count(e => e.EventType == SimulationEventType.StationStopViolation);
        var authorityViolations = world.Events.Count(e => e.EventType == SimulationEventType.SignalAuthorityViolation);
        var reserves = world.Events.Count(e => e.EventType == SimulationEventType.RouteReserved && HasCrossing(e));
        Write(metricsPath, new { name, ratio, cacheKey, engineHash, projectHash, plan, executed = true,
            simulationTimeSeconds = world.CurrentTimeSeconds, wallSeconds = timer.Elapsed.TotalSeconds, designHeadwaySeconds = theory.DesignHeadwaySeconds,
            theoryMainTrainsPerHour = theory.LineCapacityPerHour, actualMainTrainsPerHour = main.Length * 3600 / plan.MeasurementDurationSeconds,
            actualBranchTrainsPerHour = branch.Length * 3600 / plan.MeasurementDurationSeconds,
            plannedMainRuns = rows.Count(r => r.ServiceRouteId == "MAIN"), plannedBranchRuns = rows.Count(r => r.ServiceRouteId == "BRANCH"),
            actualMainCrossings = main.Length, actualBranchCrossings = branch.Length,
            mainCrossingsByBucket = plan.CountBuckets(main, e => e.SimulationTimeSeconds), branchCrossingsByBucket = plan.CountBuckets(branch, e => e.SimulationTimeSeconds),
            actualDirectionCounts = measured.GroupBy(e => e.Direction.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            pendingBefore = Pending(plan.MeasurementStartSeconds), pendingAfter = Pending(plan.MeasurementEndSeconds),
            achievedConsecutiveMainRatio = allMainGaps == 0 ? (double?)null : (double)noBranchGaps / allMainGaps,
            noBranchMainGaps = noBranchGaps, totalMainGaps = allMainGaps,
            meanSourceDepartureDelaySeconds = delays.Length == 0 ? (double?)null : delays.Average(), maxSourceDepartureDelaySeconds = delays.Length == 0 ? (double?)null : delays.Max(),
            delayBuckets, crossingTravelTimeBuckets, crossingTravelTimeHalfTrend,
            crossingDelayDefinition = "Crossing travel time = actual C rear-clear minus planned origin departure; includes acceleration and any queue delay. Half-to-half change detects growing queues without an assumed free-run time.",
            crossingReservations = reserves, crossingRearClearReleasesWholeHorizon = crossings.Length,
            collisionEvents = collisions, predictedCollisionEvents = predicted, stationStopViolationEvents = stopViolations, signalAuthorityViolationEvents = authorityViolations,
            waitingEvents = world.Events.Count(e => e.EventType == SimulationEventType.WaitingForResource) });
        Write(Path.Combine(output, name + ".crossings.json"), crossings.Select(e => new { e.ServiceRunId, e.ServiceRouteId, e.Direction, e.SimulationTimeSeconds, e.PositionMeters }));
        Write(Path.Combine(output, name + ".main-gaps.json"), mainGaps);
        Write(Path.Combine(output, name + ".violations.json"), world.Events.Where(e => e.EventType is SimulationEventType.Collision or SimulationEventType.PredictedCollision
            or SimulationEventType.StationStopViolation or SimulationEventType.SignalAuthorityViolation));
        Console.WriteLine($"JUNCTION DONE {name} main={main.Length} branch={branch.Length} rActual={(allMainGaps == 0 ? 0 : (double)noBranchGaps / allMainGaps):F4} collisions={collisions} predicted={predicted} stop={stopViolations} authority={authorityViolations}");
        if (smoke && (main.Length == 0 || (ratio < 1 && branch.Length == 0) || reserves == 0 || collisions + predicted + stopViolations + authorityViolations > 0))
            throw new InvalidOperationException($"CASE06 smoke failed at r={ratio}; inspect {metricsPath}");
    }

    private static SimulationWorld CreateWorld(TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        return new SimulationWorldOptions(Route: null, TrainParameters: runtime.TrainParameters, OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count, ProfileMode: document.Simulation.ProfileMode, MovingBlockMode: MovingBlockMode.Control,
            InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode, ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.EventsOnly,
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(10)).CreateWorld();
    }

    private static TopologyProjectDocument Build(TopologyProjectDocument source, ProjectSpatialReferencePoint point, double headway, double horizon)
    {
        var capacity = source.SpatialCapacity!;
        var speed = 65 / 3.6;
        var train = capacity.Train;
        TrackNodeDefinition N(string id, TrackNodeKind kind = TrackNodeKind.Boundary) => new(id, id, kind);
        TrackEdgeDefinition E(string id, string from, string to, double length, double limit, TrackEdgeKind kind = TrackEdgeKind.Mainline) => new()
        { TrackEdgeId = id, FromNodeId = from, ToNodeId = to, LengthMeters = length, DefaultSpeedLimitMetersPerSecond = limit,
            FromPortSide = TrackPortSide.B, ToPortSide = TrackPortSide.A, Directionality = TrackDirectionality.ForwardOnly, Kind = kind };
        var edges = new[] { E("MA", "MW", "C", 2000, speed), E("ME", "C", "ME-END", 2100, speed),
            E("BA", "BS", "C", 2000, speed), E("BX", "C", "J", 100, 40 / 3.6, TrackEdgeKind.Crossover),
            E("BE", "J", "NW", 2100, speed), E("UNUSED-UPPER", "NE", "J", 2000, speed) };
        PlatformDefinitionV4 P(string id, string edge, double at) => new() { PlatformId = id + ":P", StationId = id, Name = id,
            TrackEdgeId = edge, PlatformStartOffsetMeters = at - train.LengthMeters, PlatformEndOffsetMeters = at,
            StopPositionOffsetMeters = at, EffectiveLengthMeters = train.LengthMeters, AllowedDirection = id is "BO" or "BX-END" ? TrackDirection.Inbound : TrackDirection.Outbound };
        var platforms = new[] { P("MO", "MA", 150), P("MX", "ME", 1950), P("BO", "BA", 150), P("BX-END", "BE", 1950) };
        ServiceRouteDefinition R(string id, string[] path, string origin, string destination, TrainDirection direction) => new()
        { ServiceRouteId = id, Name = id, Direction = direction,
            Traversals = path.Select(e => new DirectedTrackTraversal(e, TraversalDirection.Forward)).ToArray(),
            Stops = new[] { origin, destination }.Select(s => new ServiceRouteStop { StationId = s, CandidatePlatformIds = [s + ":P"] }).ToArray() };
        var routes = new[] { R("MAIN", ["MA", "ME"], "MO", "MX", TrainDirection.Outbound), R("BRANCH", ["BA", "BX", "BE"], "BO", "BX-END", TrainDirection.Inbound) };
        var topology = new TopologyInfrastructureDefinition
        {
            Nodes = [N("MW"), N("C", TrackNodeKind.Crossing), N("ME-END"), N("BS"), N("J", TrackNodeKind.Switch), N("NW"), N("NE")],
            Edges = edges, Platforms = platforms,
            Stations = platforms.Select(p => new StationDefinitionV4 { StationId = p.StationId, Name = p.StationId, PlatformIds = [p.PlatformId], DefaultDwellTimeSeconds = 0 }).ToArray(),
            DirectedConnections = [new("MA", TraversalDirection.Forward, "ME", TraversalDirection.Forward), new("BA", TraversalDirection.Forward, "BX", TraversalDirection.Forward),
                new("BX", TraversalDirection.Forward, "BE", TraversalDirection.Forward), new("UNUSED-UPPER", TraversalDirection.Forward, "BE", TraversalDirection.Forward)]
        };
        var mainDuration = point.MainlineSafetyFactor * speed / (2 * train.BrakeEffect * train.DecelerationMetersPerSecondSquared)
            + (train.LengthMeters + point.CrossoverLengthMeters) / speed;
        var rows = new List<ProjectManualTimetableRow>();
        void Add(double planned, bool branch, int index)
        {
            if (planned >= horizon) return;
            var id = (branch ? "B" : "M") + index.ToString("00000");
            rows.Add(new(planned, branch ? TrainDirection.Inbound : TrainDirection.Outbound, "LOCAL", "EMU", branch ? "BRANCH-STOPS" : "MAIN-STOPS",
                branch ? "BO:P" : "MO:P", "VEH-" + id, id, ServiceRouteId: branch ? "BRANCH" : "MAIN"));
        }
        double EndpointHeadway(double ratio) => SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(point with { MainlineTrafficRatio = ratio }),
            capacity.Global, capacity.Train, SpatialCapacityDirection.Forward, SpatialCapacityCalculationMode.TaiwanHandbook).DesignHeadwaySeconds;
        var branchGap = EndpointHeadway(0);
        var consecutiveGap = EndpointHeadway(1);
        var plannedMain = 0d;
        for (var i = 0; plannedMain < horizon; i++)
        {
            Add(plannedMain, false, i);
            // Exact rational 10-gap pattern for the requested .1 grid. r is not a traffic fraction.
            var inserted = (int)Math.Round((1 - point.MainlineTrafficRatio) * 10);
            var insertBranch = ((i % 10 + 1) * inserted / 10) > ((i % 10) * inserted / 10);
            if (insertBranch) Add(plannedMain + mainDuration + capacity.Global.InterchangeTimeSeconds, true, i);
            plannedMain += insertBranch ? branchGap : consecutiveGap;
        }
        rows.Sort((a, b) => a.PlannedDepartureTimeSeconds.CompareTo(b.PlannedDepartureTimeSeconds));
        var protection = new[] { new TrackSignalSection("MA", 1999.9, 2000), new TrackSignalSection("BA", 1999.9, 2000),
            new TrackSignalSection("ME", 0, 100), new TrackSignalSection("BX", 0, 100) };
        return source with
        {
            ProjectId = "HANDBOOK-06-PHYSICAL", ProjectName = "範例六實體平面銜接點（示範幾何）", Topology = topology, ServiceRoutes = routes,
            DirectionRouteBindings = [new(TrainDirection.Outbound, "MAIN"), new(TrainDirection.Inbound, "BRANCH")],
            Train = source.Train with { MaxSpeedMetersPerSecond = speed, DefaultDwellTimeSeconds = 0, OriginTurnaroundTimeSeconds = 0, TerminalTurnaroundTimeSeconds = 0 },
            Operations = source.Operations with { TrainLengthMeters = train.LengthMeters, ApproachSpeedMetersPerSecond = speed,
                ServiceBrakingMetersPerSecondSquared = train.DecelerationMetersPerSecondSquared * train.BrakeEffect,
                ControlReactionTimeSeconds = capacity.Global.ReactionTimeSeconds },
            VehicleTypes = [new("EMU", "handbook default141m", train.LengthMeters, speed, train.AccelerationMetersPerSecondSquared,
                train.DecelerationMetersPerSecondSquared * train.BrakeEffect, train.DecelerationMetersPerSecondSquared, 1, 0, 0)],
            ServiceTypes = [new("LOCAL", "實體銜接點列車", "#19607D", "J", DefaultVehicleTypeId: "EMU")],
            StopPatterns = [new("MAIN-STOPS", "main", [new("MO", StopPatternAction.Stop, 0), new("MX", StopPatternAction.Stop, 0)]),
                new("BRANCH-STOPS", "branch", [new("BO", StopPatternAction.Stop, 0), new("BX-END", StopPatternAction.Stop, 0)])],
            Dispatch = new(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [], rows.ToArray()),
            Simulation = source.Simulation with { TrainCount = rows.Count, HeadwaySeconds = headway, MovingBlockMode = MovingBlockMode.Control },
            SpatialCapacity = capacity with { Points = [point], CompareAsSingleScenario = true },
            SignalBlocks = [new("CASE06:MAIN", new("MA", 2000), TraversalDirection.Forward, protection, capacity.Global.InterchangeTimeSeconds, point.MainlineSafetyFactor, DelayOnlyOnRouteChange: true),
                new("CASE06:BRANCH", new("BA", 2000), TraversalDirection.Forward, protection, capacity.Global.InterchangeTimeSeconds, point.BranchlineSafetyFactor, DelayOnlyOnRouteChange: true)]
        };
    }

    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
}
