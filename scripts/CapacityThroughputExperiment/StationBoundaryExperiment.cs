using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

/// <summary>
/// Finds the smallest tested integer headway that is clean and queue-stable for
/// one of the handbook intermediate-station cases.  The aligned projects in
/// after-fix/projects are the physical authority for this experiment; this
/// class only replaces the manual dispatch rows and the run metadata.
/// </summary>
public static class StationBoundaryExperiment
{
    private const int KnownOverloadedHeadwaySeconds = 40;
    private const string MiddleStationId = "B";

    private static readonly IReadOnlyDictionary<string, string> CaseSources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["01"] = "01-範例一-最繁忙車站.mrtsim.json",
            ["03"] = "03-範例三-S13雙方向.mrtsim.json",
            ["04"] = "04-範例四-圖6-19停站40秒.mrtsim.json",
            ["04b"] = "04b-範例四-承上題停站45秒.mrtsim.json"
        };

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>Runs both directions for one case prefix, for example <c>01-</c>.</summary>
    public static void Run(string root, string casePrefix, MeasurementPlan plan, string outputRoot)
    {
        var caseId = NormalizeCaseId(casePrefix);
        var rootPath = Path.GetFullPath(root);
        var afterFix = outputRoot;
        var output = Path.Combine(afterFix, "station-boundaries", caseId);
        var projectsOutput = Path.Combine(output, "projects");
        var runsOutput = Path.Combine(output, "runs");
        Directory.CreateDirectory(projectsOutput);
        Directory.CreateDirectory(runsOutput);
        plan.WriteManifest(output);

        var sourcePath = Path.Combine(rootPath, "samples", "handbook-capacity", CaseSources[caseId]);
        var source = LoadDocument(sourcePath);
        var measurementsPath = Path.Combine(afterFix, "measurements.json");
        var existingMeasurements = LoadExistingMeasurements(measurementsPath);
        var directionSummaries = new List<StationBoundaryDirectionSummary>();

        foreach (var direction in Enum.GetValues<TrainDirection>())
        {
            var nominal = LoadNominalAlignedProject(afterFix, caseId, direction);
            AssertCaseConfiguration(source, nominal.Document, caseId, direction, nominal.Path);

            var theoryPoint = source.SpatialCapacity!.Points
                .Single(point => point.Kind == SpatialReferencePointKind.IntermediateStation);
            var theory = SpatialCapacityAnalysis.Calculate(
                SpatialCapacityProjectAnalysis.ToDefinition(theoryPoint),
                source.SpatialCapacity.Global,
                source.SpatialCapacity.Train,
                direction == TrainDirection.Outbound ? SpatialCapacityDirection.Forward : SpatialCapacityDirection.Reverse,
                source.SpatialCapacity.Mode);

            var nominalHeadway = nominal.Document.Simulation.HeadwaySeconds ?? throw new InvalidOperationException("Nominal project missing headway");
            if (!double.IsFinite(nominalHeadway) || nominalHeadway <= KnownOverloadedHeadwaySeconds)
                throw new InvalidOperationException($"案例 {caseId} {direction} 的 nominal headway 無法建立 40 秒以上的搜尋區間：{nominalHeadway}。專案：{nominal.Path}");

            var upperHeadway = checked((int)Math.Ceiling(nominalHeadway));
            var runs = new List<StationBoundaryRunMetric>();

            // The upper endpoint is always tested first.  This makes an
            // unclean nominal endpoint an explicit no-clean result instead of
            // silently searching outside the requested bracket.
            var upper = EvaluateAndPersist(
                nominal.Document,
                source,
                caseId,
                direction,
                upperHeadway,
                nominalHeadway,
                theory,
                projectsOutput,
                runsOutput,
                runs,
                probeOffset: null,
                plan: plan);

            var baseline = LoadOrEvaluateKnownOverloaded(
                afterFix,
                existingMeasurements,
                nominal,
                source,
                caseId,
                direction,
                nominalHeadway,
                theory,
                projectsOutput,
                runsOutput,
                runs,
                plan);

            int? candidateHeadway = null;
            string? noCleanReason = null;
            if (!upper.Clean)
            {
                noCleanReason = $"上界 ceil(nominalTheoryHeadway)={upperHeadway}s 不符合 clean；依指定不宣稱存在 clean headway。";
            }
            else
            {
                var dirty = KnownOverloadedHeadwaySeconds;
                var clean = upperHeadway;
                while (clean - dirty > 1)
                {
                    var middle = dirty + (clean - dirty) / 2;
                    var result = EvaluateAndPersist(
                        nominal.Document,
                        source,
                        caseId,
                        direction,
                        middle,
                        nominalHeadway,
                        theory,
                        projectsOutput,
                        runsOutput,
                        runs,
                        probeOffset: null,
                        plan: plan);
                    if (result.Clean)
                        clean = middle;
                    else
                        dirty = middle;
                }

                candidateHeadway = clean;
                foreach (var offset in new[] { -1, -2 })
                {
                    var probe = candidateHeadway.Value + offset;
                    if (probe > KnownOverloadedHeadwaySeconds
                        && !runs.Any(item => item.InputHeadwaySeconds == probe))
                    {
                        // These are diagnostic probes only.  Their outcome is
                        // recorded without changing the selected candidate or
                        // inferring global monotonicity.
                        EvaluateAndPersist(
                            nominal.Document,
                            source,
                            caseId,
                            direction,
                            probe,
                            nominalHeadway,
                            theory,
                            projectsOutput,
                            runsOutput,
                            runs,
                            probeOffset: offset,
                            plan: plan);
                    }
                }
            }

            if(candidateHeadway is not null) candidateHeadway=runs.Where(item=>item.Clean).Min(item=>item.InputHeadwaySeconds);
            runs.Sort((left, right) => left.InputHeadwaySeconds.CompareTo(right.InputHeadwaySeconds));
            var candidate = candidateHeadway is null
                ? null
                : runs.Single(item => item.InputHeadwaySeconds == candidateHeadway.Value );
            directionSummaries.Add(new StationBoundaryDirectionSummary(
                caseId,
                direction,
                nominalHeadway,
                theory.DesignHeadwaySeconds,
                theory.LineCapacityPerHour,
                KnownOverloadedHeadwaySeconds,
                upperHeadway,
                upper.Clean,
                candidateHeadway,
                noCleanReason,
                candidate?.BDeparturesPerHour,
                candidate?.MeasurementBucketCounts,
                candidate is not null && candidate.Clean,
                runs.Select(item => item.InputHeadwaySeconds).ToArray(),
                "Integer bisection over the tested bracket; candidate-1 and candidate-2 are probe-only observations and do not establish global monotonicity.",
                runs.ToArray()));

            WriteJson(Path.Combine(output, $"{direction}.json"), directionSummaries[^1]);
        }

        var summary = new StationBoundarySummary(
            "station-boundary-v2",
            caseId,
            CaseSources[caseId],
            directionSummaries.ToArray());
        WriteJson(Path.Combine(output, "summary.json"), summary);
        WriteJson(Path.Combine(output, "metrics.json"), directionSummaries.SelectMany(item => item.Runs).ToArray());
        Console.WriteLine($"DONE station boundary {caseId}: {output}");
    }

    private static StationBoundaryRunMetric LoadOrEvaluateKnownOverloaded(
        string afterFix,
        IReadOnlyList<ExistingMeasurement> existingMeasurements,
        PreparedProject nominal,
        TopologyProjectDocument source,
        string caseId,
        TrainDirection direction,
        double nominalHeadway,
        SpatialCapacityResult theory,
        string projectsOutput,
        string runsOutput,
        List<StationBoundaryRunMetric> runs,
        MeasurementPlan plan)
    {
        var existingScenario = $"aligned-{caseId}-{direction}";
        var existing = existingMeasurements.FirstOrDefault(item =>
            item.Scenario.Equals(existingScenario, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(item.InputHeadwaySeconds - KnownOverloadedHeadwaySeconds) < 1e-9);
        var existingProject = FindProject(afterFix, $"aligned-{caseId}-{direction}-", KnownOverloadedHeadwaySeconds);
        var currentEngineHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));
        if (existing is not null && existingProject is not null && File.Exists(existingProject+".engine-sha256") && File.ReadAllText(existingProject+".engine-sha256")==currentEngineHash && File.Exists(existingProject+".experiment-version") && File.ReadAllText(existingProject+".experiment-version")==plan.CacheKey)
        {
            var document = LoadDocument(existingProject);
            AssertCaseConfiguration(source, document, caseId, direction, existingProject);
            var copiedPath = Path.Combine(projectsOutput, $"{direction}-{FormatSeconds(KnownOverloadedHeadwaySeconds)}s.mrtsim.json");
            File.Copy(existingProject, copiedPath, overwrite: true);
            var metric = FromExistingMeasurement(
                existing,
                caseId,
                direction,
                nominalHeadway,
                theory,
                RelativePath(Path.GetDirectoryName(projectsOutput)!, copiedPath),
                ExistingRunFileName(runsOutput, direction, KnownOverloadedHeadwaySeconds),
                isBaseline: true,
                measurementPlanKey: plan.CacheKey,
                simulationTimeSeconds: plan.TotalHorizonSeconds);
            runs.Add(metric);
            WriteJson(Path.Combine(runsOutput, ExistingRunFileName(runsOutput, direction, KnownOverloadedHeadwaySeconds)), metric);
            return metric;
        }

        return EvaluateAndPersist(
            nominal.Document,
            source,
            caseId,
            direction,
            KnownOverloadedHeadwaySeconds,
            nominalHeadway,
            theory,
            projectsOutput,
            runsOutput,
            runs,
            probeOffset: null,
            isBaseline: true,
            plan: plan);
    }

    private static StationBoundaryRunMetric EvaluateAndPersist(
        TopologyProjectDocument nominal,
        TopologyProjectDocument source,
        string caseId,
        TrainDirection direction,
        int headway,
        double nominalHeadway,
        SpatialCapacityResult theory,
        string projectsOutput,
        string runsOutput,
        List<StationBoundaryRunMetric> runs,
        int? probeOffset,
        bool isBaseline = false,
        MeasurementPlan? plan = null)
    {
        plan ??= MeasurementPlan.Parse([]);
        var existing = runs.FirstOrDefault(item => item.InputHeadwaySeconds == headway);
        if (existing is not null)
            return existing;

        var prepared = PrepareDocument(nominal, caseId, direction, headway, plan);
        AssertCaseConfiguration(source, prepared, caseId, direction, "prepared document");
        var serialized = TopologyProjectFormat.Serialize(prepared);
        var projectName = $"{direction}-{FormatSeconds(headway)}s.mrtsim.json";
        var projectPath = Path.Combine(projectsOutput, projectName);
        File.WriteAllText(projectPath, serialized, new UTF8Encoding(false));
        File.WriteAllText(projectPath + ".experiment-version", plan.CacheKey);

        var clock = Stopwatch.StartNew();
        var runtime = TopologyProjectFormat.CreateRuntime(prepared);
        var world = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            ProfileMode: prepared.Simulation.ProfileMode,
            MovingBlockMode: MovingBlockMode.Control,
            InitialBrakingEstimationMode: prepared.Simulation.BrakingEstimationMode,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.EventsOnly,
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(10)).CreateWorld();

        foreach (var boundary in plan.ProgressBoundaries(900))
        {
            world.AdvanceTo(boundary);
            WriteJson(Path.Combine(Path.GetDirectoryName(projectsOutput)!, "progress.json"),new {caseId,direction,headway,simulationTime=world.CurrentTimeSeconds,wallSeconds=clock.Elapsed.TotalSeconds});
        }

        if (Math.Abs(world.CurrentTimeSeconds - plan.TotalHorizonSeconds) > .11)
            throw new InvalidOperationException($"Station boundary {caseId} {direction} h={headway}s 未完成 {plan.HorizonLabel} 模擬。實際={world.CurrentTimeSeconds}");

        var metric = Measure(
            world,
            prepared,
            caseId,
            direction,
            headway,
            nominalHeadway,
            theory,
            RelativePath(Path.GetDirectoryName(projectsOutput)!, projectPath),
            RelativePath(Path.GetDirectoryName(runsOutput)!, Path.Combine(runsOutput, $"{direction}-{FormatSeconds(headway)}s.json")),
            plan,
            clock.Elapsed.TotalSeconds,
            probeOffset,
            isBaseline,
            plan.CacheKey);
        runs.Add(metric);
        WriteJson(Path.Combine(runsOutput, $"{direction}-{FormatSeconds(headway)}s.json"), metric);
        return metric;
    }

    private static StationBoundaryRunMetric Measure(
        SimulationWorld world,
        TopologyProjectDocument document,
        string caseId,
        TrainDirection direction,
        int headway,
        double nominalHeadway,
        SpatialCapacityResult theory,
        string projectPath,
        string runPath,
        MeasurementPlan plan,
        double wallSeconds,
        int? probeOffset,
        bool isBaseline,
        string measurementPlanKey)
    {
        var context = world.GetTopologyResultContext();
        var routeId = document.DirectionRouteBindings.Single(binding => binding.Direction == direction).ServiceRouteId;
        var route = document.ServiceRoutes.Single(item => item.ServiceRouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase));
        if (!route.Stops.Any(stop => stop.StationId.Equals(MiddleStationId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"案例 {caseId} {direction} 對齊專案缺少中央站 B，拒絕把其他站當成容量量測點。");

        var origin = route.Stops.First().StationId;
        var rows = document.Dispatch.ManualTimetableRows ?? throw new InvalidOperationException("Station boundary requires manual timetable rows.");
        var plannedByRun = rows.ToDictionary(item => item.ServiceRunId ?? throw new InvalidOperationException("Manual row missing ServiceRunId."), item => item.PlannedDepartureTimeSeconds, StringComparer.OrdinalIgnoreCase);
        var originDepartures = world.Events
            .Where(item => item.Direction == direction
                && item.EventType == SimulationEventType.Departure
                && context.MatchesStationEvent(direction, origin, item))
            .GroupBy(item => item.ServiceRunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.SimulationTimeSeconds).First())
            .Where(item => plannedByRun.ContainsKey(item.ServiceRunId))
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();
        var middleDepartures = world.Events
            .Where(item => item.Direction == direction
                && item.EventType == SimulationEventType.Departure
                && context.MatchesStationEvent(direction, MiddleStationId, item)
                && plan.IsInMeasurementWindow(item.SimulationTimeSeconds))
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();

        var delayValues = originDepartures
            .Select(item => item.SimulationTimeSeconds - plannedByRun[item.ServiceRunId])
            .ToArray();
        var firstHourDelay = MeanDelay(originDepartures, plannedByRun, plan.MeasurementStartSeconds, plan.MeasurementMidpointSeconds);
        var secondHourDelay = MeanDelay(originDepartures, plannedByRun, plan.MeasurementMidpointSeconds, plan.MeasurementEndSeconds);
        int PendingAt(double cutoff) => Math.Max(0,
            plannedByRun.Count(item => item.Value < cutoff)
            - originDepartures.Count(item => item.SimulationTimeSeconds < cutoff));

        var pendingAtWarmup = PendingAt(plan.MeasurementStartSeconds);
        var pendingAtMeasurementEnd = PendingAt(plan.MeasurementEndSeconds);
        var collisionEvents = world.Events.Count(item => item.EventType == SimulationEventType.Collision);
        var predictedCollisionEvents = world.Events.Count(item => item.EventType == SimulationEventType.PredictedCollision);
        var stopViolationEvents = world.Events.Count(item => item.EventType == SimulationEventType.StationStopViolation);
        var queueStable = pendingAtMeasurementEnd - pendingAtWarmup <= 1
            && secondHourDelay <= firstHourDelay + 1;
        var safetyClean = collisionEvents == 0 && predictedCollisionEvents == 0 && stopViolationEvents == 0;
        var clean = queueStable && safetyClean;
        var halfHourCounts = plan.CountBuckets(middleDepartures, item => item.SimulationTimeSeconds);

        return new StationBoundaryRunMetric(
            caseId,
            direction,
            headway,
            nominalHeadway,
            theory.DesignHeadwaySeconds,
            theory.LineCapacityPerHour,
            isBaseline,
            probeOffset,
            projectPath,
            runPath,
            middleDepartures.Length * 3600d / plan.MeasurementDurationSeconds,
            halfHourCounts,
            pendingAtWarmup,
            pendingAtMeasurementEnd,
            delayValues.Length == 0 ? 0 : delayValues.Average(),
            delayValues.Length == 0 ? 0 : delayValues.Max(),
            firstHourDelay,
            secondHourDelay,
            world.Events.Count(item => item.EventType == SimulationEventType.WaitingForResource),
            collisionEvents,
            predictedCollisionEvents,
            stopViolationEvents,
            queueStable,
            safetyClean,
            safetyClean,
            clean,
            world.CurrentTimeSeconds,
            wallSeconds,
            measurementPlanKey);
    }

    private static double MeanDelay(
        IReadOnlyList<SimulationEvent> departures,
        IReadOnlyDictionary<string, double> plannedByRun,
        double from,
        double to)
    {
        var values = departures
            .Where(item => item.SimulationTimeSeconds >= from && item.SimulationTimeSeconds < to)
            .Select(item => item.SimulationTimeSeconds - plannedByRun[item.ServiceRunId])
            .ToArray();
        return values.Length == 0 ? 0 : values.Average();
    }

    private static TopologyProjectDocument PrepareDocument(
        TopologyProjectDocument nominal,
        string caseId,
        TrainDirection direction,
        int headway,
        MeasurementPlan plan)
    {
        var template = (nominal.Dispatch.ManualTimetableRows ?? [])
            .FirstOrDefault(item => item.Direction == direction)
            ?? throw new InvalidOperationException($"nominal 專案沒有 {direction} 手動班表列。");
        var rows = new List<ProjectManualTimetableRow>();
        for (var ordinal = 0; ; ordinal++)
        {
            var planned = ordinal * (double)headway;
            if (planned >= plan.TotalHorizonSeconds - 1e-9)
                break;
            var runKey = $"STB-{caseId}-{direction}-{ordinal:0000}";
            rows.Add(template with
            {
                PlannedDepartureTimeSeconds = planned,
                VehicleId = $"{runKey}-VEH",
                ServiceRunId = $"{runKey}-RUN",
                ContinueAfterTerminal = false,
                ContinuationServiceRunId = null,
                StationTimetableEntries = null
            });
        }

        return nominal with
        {
            ProjectId = $"STATION-BOUNDARY-{caseId}-{direction}-{headway}",
            ProjectName = $"station-boundary {caseId} {direction} {headway}s",
            Dispatch = nominal.Dispatch with
            {
                ActiveMode = DispatchPlanningMode.ManualTimetable,
                ManualTimetableRows = rows.ToArray(),
                SimpleHeadwayPlans = []
            },
            Simulation = nominal.Simulation with
            {
                TrainCount = rows.Count,
                HeadwaySeconds = headway
            }
        };
    }

    private static PreparedProject LoadNominalAlignedProject(string afterFix, string caseId, TrainDirection direction)
    {
        var projects = Path.Combine(afterFix, "projects");
        var stem = $"aligned-{caseId}-{direction}-";
        var candidates = Directory.Exists(projects)
            ? Directory.GetFiles(projects, "*.mrtsim.json")
                .Where(path => Path.GetFileName(path).StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                .Select(path => new PreparedProject(path, LoadDocument(path)))
                .Where(item => item.Document.Simulation.HeadwaySeconds > 0)
                .OrderByDescending(item => item.Document.Simulation.HeadwaySeconds)
                .ToArray()
            : [];
        return candidates.FirstOrDefault()
            ?? throw new FileNotFoundException($"找不到案例 {caseId} {direction} 的 aligned Schema 8 nominal project。預期路徑：{Path.Combine(projects, stem + "*.mrtsim.json")}");
    }

    private static string? FindProject(string afterFix, string stem, int headway)
    {
        var projects = Path.Combine(afterFix, "projects");
        if (!Directory.Exists(projects))
            return null;
        var suffix = $"-{FormatSeconds(headway)}s.mrtsim.json";
        return Directory.GetFiles(projects, "*.mrtsim.json")
            .Where(path => Path.GetFileName(path).StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(path).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void AssertCaseConfiguration(
        TopologyProjectDocument source,
        TopologyProjectDocument aligned,
        string caseId,
        TrainDirection direction,
        string path)
    {
        if (aligned.SchemaVersion != TopologyProjectFormat.CurrentSchemaVersion)
            throw new InvalidOperationException($"案例 {caseId} {direction} 的 aligned project 不是 Schema 8：{path}");
        if (source.SpatialCapacity is null || aligned.SpatialCapacity is null)
            throw new InvalidOperationException($"案例 {caseId} {direction} 缺少 SpatialCapacity 設定，拒絕猜測來源 mapping：{path}");

        var sourceNode = JsonNode.Parse(JsonSerializer.Serialize(source.SpatialCapacity, JsonOptions));
        var alignedNode = JsonNode.Parse(JsonSerializer.Serialize(aligned.SpatialCapacity, JsonOptions));
        if (!JsonNode.DeepEquals(sourceNode, alignedNode))
            throw new InvalidOperationException($"案例 {caseId} {direction} 的 aligned project 與指定 handbook source config 不一致：{path}");

        var binding = aligned.DirectionRouteBindings.SingleOrDefault(item => item.Direction == direction)
            ?? throw new InvalidOperationException($"案例 {caseId} {direction} 缺少 direction route binding：{path}");
        var route = aligned.ServiceRoutes.SingleOrDefault(item => item.ServiceRouteId.Equals(binding.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"案例 {caseId} {direction} 的 route binding 找不到 route：{path}");
        if (!route.Stops.Any(stop => stop.StationId.Equals(MiddleStationId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"案例 {caseId} {direction} 的 aligned project 未以 B 作中央站，拒絕錯配：{path}");
    }

    private static IReadOnlyList<ExistingMeasurement> LoadExistingMeasurements(string path)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            return JsonSerializer.Deserialize<ExistingMeasurement[]>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static StationBoundaryRunMetric FromExistingMeasurement(
        ExistingMeasurement item,
        string caseId,
        TrainDirection direction,
        double nominalHeadway,
        SpatialCapacityResult theory,
        string projectPath,
        string runPath,
        bool isBaseline,
        string measurementPlanKey,
        double simulationTimeSeconds = 0)
    {
        var pendingAtWarmup = item.PendingDueRunsAtWarmup;
        var pendingAtEnd = item.PendingDueRuns;
        var firstHour = item.FirstHalfMeanOriginDelaySeconds;
        var secondHour = item.LastHalfMeanOriginDelaySeconds;
        var queueStable = pendingAtEnd - pendingAtWarmup <= 1 && secondHour <= firstHour + 1;
        var safetyClean = item.CollisionEvents == 0 && item.PredictedCollisionEvents == 0 && item.StopViolations == 0;
        return new StationBoundaryRunMetric(
            caseId,
            direction,
            KnownOverloadedHeadwaySeconds,
            nominalHeadway,
            theory.DesignHeadwaySeconds,
            theory.LineCapacityPerHour,
            isBaseline,
            null,
            projectPath,
            runPath,
            item.MiddleDeparturesPerHour,
            item.MeasurementBucketCounts ?? [],
            pendingAtWarmup,
            pendingAtEnd,
            item.MeanOriginDelaySeconds,
            item.MaxOriginDelaySeconds,
            firstHour,
            secondHour,
            item.ResourceWaitEvents,
            item.CollisionEvents,
            item.PredictedCollisionEvents,
            item.StopViolations,
            queueStable,
            safetyClean,
            safetyClean,
            queueStable && safetyClean,
            simulationTimeSeconds,
            item.WallSeconds,
            measurementPlanKey);
    }

    private static TopologyProjectDocument LoadDocument(string path) =>
        TopologyProjectFormat.Deserialize(File.ReadAllText(path));

    public static void RunMarginCheck(string root,string casePrefix,MeasurementPlan plan,string outputRoot)
    {
        var caseId=NormalizeCaseId(casePrefix);
        var afterFix=outputRoot;
        var output=Path.Combine(afterFix,"station-boundaries",caseId,"margin-check");
        Directory.CreateDirectory(Path.Combine(output,"projects"));
        Directory.CreateDirectory(Path.Combine(output,"runs"));
        plan.WriteManifest(output);
        var summary=JsonSerializer.Deserialize<StationBoundarySummary>(File.ReadAllText(Path.Combine(afterFix,"station-boundaries",caseId,"summary.json")),JsonOptions)!;
        var source=LoadDocument(Path.Combine(root,"samples","handbook-capacity",CaseSources[caseId]));
        var runs=new List<StationBoundaryRunMetric>();
        foreach(var directionSummary in summary.Directions)
        {
            if(!directionSummary.CandidateClean || directionSummary.SmallestTestedCleanHeadwaySeconds is not {} h) continue;
            var nominal=LoadNominalAlignedProject(afterFix,caseId,directionSummary.Direction);
            var theoretical=SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(source.SpatialCapacity!.Points.Single()),source.SpatialCapacity.Global,source.SpatialCapacity.Train,
                directionSummary.Direction==TrainDirection.Outbound ? SpatialCapacityDirection.Forward : SpatialCapacityDirection.Reverse,source.SpatialCapacity.Mode);
            var marginHeadway=(int)Math.Ceiling(h*(1+source.SpatialCapacity.Global.MarginRate));
            EvaluateAndPersist(nominal.Document,source,caseId,directionSummary.Direction,marginHeadway,directionSummary.NominalProjectHeadwaySeconds,theoretical,
                Path.Combine(output,"projects"),Path.Combine(output,"runs"),runs,null,plan: plan);
            WriteJson(Path.Combine(output,"metrics.json"),runs);
        }
    }

    private static string NormalizeCaseId(string prefix)
    {
        var value = (prefix ?? string.Empty).Trim();
        if (value.StartsWith("aligned-", StringComparison.OrdinalIgnoreCase))
            value = value["aligned-".Length..];
        value = value.TrimEnd('-', '_', ' ');
        if (value.Length == 1 && char.IsDigit(value[0]))
            value = "0" + value;
        if (!CaseSources.ContainsKey(value))
            throw new ArgumentException($"不支援的 station boundary casePrefix：{prefix}。可用：{string.Join(", ", CaseSources.Keys.Order())}", nameof(prefix));
        return value;
    }

    private static string ExistingRunFileName(string runsOutput, TrainDirection direction, int headway) =>
        $"{direction}-{FormatSeconds(headway)}s.json";

    private static string RelativePath(string basePath, string path) =>
        Path.GetRelativePath(basePath, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record PreparedProject(string Path, TopologyProjectDocument Document);

    private sealed record ExistingMeasurement(
        string Scenario,
        double InputHeadwaySeconds,
        TrainDirection Direction,
        double MiddleDeparturesPerHour,
        int PendingDueRuns,
        int PendingDueRunsAtWarmup,
        double MeanOriginDelaySeconds,
        double MaxOriginDelaySeconds,
        int ResourceWaitEvents,
        int CollisionEvents,
        int StopViolations,
        double WallSeconds,
        double FirstHalfMeanOriginDelaySeconds,
        double LastHalfMeanOriginDelaySeconds,
        int PredictedCollisionEvents,
        int[]? MeasurementBucketCounts);
}

public sealed record StationBoundaryRunMetric(
    string CasePrefix,
    TrainDirection Direction,
    int InputHeadwaySeconds,
    double NominalProjectHeadwaySeconds,
    double TheoryDesignHeadwaySeconds,
    int TheoryLineCapacityPerHour,
    bool IsKnownOverloadedBaseline,
    int? ProbeOffset,
    string ActualSchema8Project,
    string MetricFile,
    double BDeparturesPerHour,
    int[] MeasurementBucketCounts,
    int PendingOriginAtWarmup,
    int PendingOriginAtMeasurementEnd,
    double MeanOriginDelaySeconds,
    double MaxOriginDelaySeconds,
    double MeanOriginDelayFirstHourSeconds,
    double MeanOriginDelaySecondHourSeconds,
    int ResourceWaitEventsWholeHorizon,
    int CollisionEventsWholeHorizon,
    int PredictedCollisionEventsWholeHorizon,
    int StopViolationEventsWholeHorizon,
    bool QueueStable,
    bool SafetyClean,
    bool WarningFree,
    bool Clean,
    double SimulationTimeSeconds,
    double WallSeconds,
    string MeasurementPlanKey = "");

public sealed record StationBoundaryDirectionSummary(
    string CasePrefix,
    TrainDirection Direction,
    double NominalProjectHeadwaySeconds,
    double TheoryDesignHeadwaySeconds,
    int TheoryLineCapacityPerHour,
    int KnownOverloadedHeadwaySeconds,
    int UpperBoundHeadwaySeconds,
    bool UpperBoundClean,
    int? SmallestTestedCleanHeadwaySeconds,
    string? NoCleanReason,
    double? CandidateBDeparturesPerHour,
    int[]? CandidateMeasurementBucketCounts,
    bool CandidateClean,
    int[] TestedHeadways,
    string SearchInterpretation,
    StationBoundaryRunMetric[] Runs);

public sealed record StationBoundarySummary(
    string Definition,
    string CasePrefix,
    string SourceSample,
    StationBoundaryDirectionSummary[] Directions);
