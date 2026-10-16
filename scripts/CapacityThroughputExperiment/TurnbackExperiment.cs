using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

/// <summary>One physical turnback throughput run and its measurement window.</summary>
public sealed record TurnbackRunMetric(
    string Fixture,
    string HandbookCase,
    double TheoryDesignHeadwaySeconds,
    double InputHeadwaySeconds,
    bool Executed,
    string? NotExecutedReason,
    int PlannedInboundRuns,
    int ActualInboundArrivalsAtMeasuredStation,
    int ActualReturnOutboundDeparturesAtMeasuredStation,
    int[] InboundArrivalCountsByBucket,
    int[] ReturnDepartureCountsByBucket,
    int PendingInboundAtWarmup,
    int PendingInboundAtMeasurementEnd,
    int WaitingForResourceEpisodes,
    int CollisionEvents,
    int StationStopViolationEvents,
    int DirectionChangedEvents,
    int PredictedCollisionEvents,
    int SignalAuthorityViolationEvents,
    double MeanInputDepartureDelaySeconds,
    double MaxInputDepartureDelaySeconds,
    double MeanReturnCycleSeconds,
    double MaxReturnCycleSeconds,
    double SimulationTimeSeconds,
    double WallSeconds,
    int TailTrackReachedEventsWholeHorizon = 0,
    int TailTrackReturnStartedEventsWholeHorizon = 0,
    int TurnaroundStartedEventsWholeHorizon = 0,
    IReadOnlyDictionary<string,int>? ReturnCountsByFacilityCentralWindow = null,
    string MeasurementPlanKey = "");

/// <summary>
/// Runs the physical turnback fixtures independently from the legacy carrier
/// experiment.  Each inbound feed explicitly names the ServiceRoute that selects
/// its topology turnback operation; no engine-side facility alternation is assumed.
/// </summary>
public static class TurnbackExperiment
{
    // The paired return is due immediately after its inbound counterpart.  The
    // actual departure is still controlled by station dwell, facility traversal,
    // resource reservation and rear-clear; a large timetable offset would add an
    // artificial hold unrelated to the physical turnback.
    private const double TurnbackPairLeadSeconds = 1;

    public static void Run(string root, MeasurementPlan plan, string outputRoot, string? fixtureName = null, double? onlyHeadway = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var repositoryRoot = Path.GetFullPath(root);
        if (!Directory.Exists(repositoryRoot))
            throw new DirectoryNotFoundException($"找不到實驗根目錄：{repositoryRoot}");

        var output = Path.Combine(outputRoot, "turnbacks");
        var projects = Path.Combine(output, "projects");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(projects);

        var fixtures = TurnbackFixtures.Build(repositoryRoot).Where(fixture => fixtureName is null || fixture.Name.Equals(fixtureName, StringComparison.Ordinal)).ToArray();
        if (fixtures.Length == 0) throw new ArgumentException("Unknown turnback fixture: " + fixtureName);
        plan.WriteManifest(output);
        var options = CreateJsonOptions();
        var manifest = fixtures.Select(fixture => new
        {
            fixture.Name,
            fixture.HandbookCase,
            fixture.MeasuredStationId,
            measuredArrivalDirection = fixture.MeasuredArrivalDirection.ToString(),
            theory = new
            {
                fixture.Theory.SafetyHeadwaySeconds,
                fixture.Theory.DesignHeadwaySeconds,
                fixture.Theory.LineCapacityPerHour,
                fixture.Theory.DesignPassengerCapacityPerHour,
                fixture.Theory.AchievablePassengerCapacityPerHour
            },
            bindings = fixture.TurnbackBindings,
            inputAndRuntimeNotes = fixture.EnforcedParameters,
            runtimeEnforcedNotes = fixture.EnforcedParameters.Where(note=>note.StartsWith("runtime",StringComparison.OrdinalIgnoreCase) || note.StartsWith("實驗停等")).ToArray(),
            theoryInputNotes = fixture.EnforcedParameters.Where(note=>note.StartsWith("handbook",StringComparison.OrdinalIgnoreCase)).ToArray(),
            unsupportedParameters = fixture.UnsupportedParameters,
            schedule = new
            {
                independentInboundFeeds = true,
                explicitContinuationPair = true,
                returnPairLeadSeconds = TurnbackPairLeadSeconds,
                sameVehicleContinuesAcrossTurnback = true
            },
            measurementWindow = new
            {
                plan.Id,
                plan.TotalHorizonSeconds,
                plan.MeasurementStartSeconds,
                plan.MeasurementEndSeconds,
                plan.MeasurementDurationSeconds,
                plan.BucketSeconds,
                bucketCount = plan.BucketCount
            }
        }).ToArray();
        WriteJson(Path.Combine(output, "manifest.json"), manifest, options);

        var engineHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));
        var engineHashPath=Path.Combine(output,"engine-sha256.txt");
        var sameEngine=File.Exists(engineHashPath) && File.ReadAllText(engineHashPath).Trim()==engineHash;
        var results = sameEngine && File.Exists(Path.Combine(output,"metrics.json"))
            ? JsonSerializer.Deserialize<List<TurnbackRunMetric>>(File.ReadAllText(Path.Combine(output,"metrics.json")),options)!.Where(item => item.MeasurementPlanKey == plan.CacheKey).ToList()
            : new List<TurnbackRunMetric>();
        File.WriteAllText(engineHashPath,engineHash);
        foreach (var fixture in fixtures)
        {
            var headways=fixture.CapacityPoint.Kind==SpatialReferencePointKind.AfterStationTurnback ? new[]{fixture.Theory.DesignHeadwaySeconds,150d}.Distinct() : new[]{fixture.Theory.DesignHeadwaySeconds}.AsEnumerable();
            foreach (var headway in headways.Where(headway => onlyHeadway is null || Math.Abs(headway - onlyHeadway.Value) < 1e-7))
            {
                var cached=results.FirstOrDefault(m=>m.Fixture==fixture.Name && Math.Abs(m.InputHeadwaySeconds-headway)<1e-8 && m.Executed && m.MeasurementPlanKey==plan.CacheKey && Math.Abs(m.SimulationTimeSeconds-plan.TotalHorizonSeconds)<.11);
                var cachedProject=Path.Combine(projects,$"{fixture.Name}-{FormatSeconds(headway)}s.mrtsim.json");
                var versionPath=cachedProject+".experiment-version";
                if(cached is not null && File.Exists(cachedProject) && File.ReadAllText(cachedProject)==TopologyProjectFormat.Serialize(PrepareDocument(fixture,headway,plan).Document) && File.Exists(versionPath) && File.ReadAllText(versionPath)==plan.CacheKey) { Console.WriteLine($"RESUME TURNBACK {fixture.Name} {headway}"); continue; }
                var metric = RunOne(repositoryRoot, projects, fixture, headway, plan, options);
                results.Add(metric);
                WriteJson(Path.Combine(output, "metrics.json"), results, options);
                Console.WriteLine(metric.Executed
                    ? $"TURNBACK {fixture.Name} h={headway:F3}s arrivals={metric.ActualInboundArrivalsAtMeasuredStation} returns={metric.ActualReturnOutboundDeparturesAtMeasuredStation} queue={metric.PendingInboundAtMeasurementEnd} collisions={metric.CollisionEvents} wall={metric.WallSeconds:F1}s"
                    : $"TURNBACK {fixture.Name} h={headway:F3}s NOT EXECUTED: {metric.NotExecutedReason}");
            }
        }

        WriteJson(Path.Combine(output, "metrics.json"), results, options);
        Console.WriteLine($"TURNBACK DONE {results.Count} runs; {output}");
    }

    private static TurnbackRunMetric RunOne(
        string repositoryRoot,
        string projectsDirectory,
        TurnbackFixture fixture,
        double headway,
        MeasurementPlan plan,
        JsonSerializerOptions options)
    {
        if (!double.IsFinite(headway) || headway <= 0)
            throw new ArgumentOutOfRangeException(nameof(headway));

        var prepared = PrepareDocument(fixture, headway, plan);
        TopologyProjectFormat.Validate(prepared.Document);
        var projectName = $"{fixture.Name}-{FormatSeconds(headway)}s.mrtsim.json";
        var projectPath = Path.Combine(projectsDirectory, projectName);
        WriteText(projectPath, TopologyProjectFormat.Serialize(prepared.Document));
        WriteText(projectPath + ".experiment-version", plan.CacheKey);

        // A multi-facility fixture is only executable here when every inbound feed
        // has an explicit arrival route and its topology operation is present.  The
        // checks avoid silently measuring an alternation selected by the engine.
        var bindingErrors = ValidateBindings(fixture);
        if (fixture.TurnbackBindings.Count > 1 && !fixture.Document.Topology.Nodes.Any(node=>node.NodeId==CrossingFixtureBuilder.DiamondNodeId))
            return NotExecutedMetric(fixture, headway, plan, prepared.Rows.Count(r=>r.Direction==fixture.MeasuredArrivalDirection), "多設施實驗缺少明確共用交叉節點與互斥保護，不能驗算交替停靠容量。");
        if (bindingErrors.Count > 0)
        {
            return NotExecutedMetric(fixture, headway, plan, prepared.Rows.Count, string.Join("；", bindingErrors));
        }

        var clock = Stopwatch.StartNew();
        var runtime = TopologyProjectFormat.CreateRuntime(prepared.Document);
        var world = CreateWorld(prepared.Document, runtime);
        foreach (var boundary in plan.ProgressBoundaries(900))
        {
            world.AdvanceTo(boundary);
            WriteJson(Path.Combine(Path.GetDirectoryName(projectsDirectory)!, "progress.json"), new
            {
                fixture = fixture.Name,
                headway,
                simulationTimeSeconds = world.CurrentTimeSeconds,
                wallSeconds = clock.Elapsed.TotalSeconds
            }, options);
        }

        if (Math.Abs(world.CurrentTimeSeconds - plan.TotalHorizonSeconds) > .11)
            throw new InvalidOperationException($"{fixture.Name} h={headway} 未推進到 {plan.HorizonLabel}。實際 {world.CurrentTimeSeconds} 秒。");

        return Measure(fixture, headway, prepared.Rows, world, plan, clock.Elapsed.TotalSeconds);
    }

    private static PreparedDocument PrepareDocument(TurnbackFixture fixture, double headway, MeasurementPlan plan)
    {
        var source = fixture.Document;
        var template = source.Dispatch.ManualTimetableRows?.FirstOrDefault(row => row.Direction == fixture.MeasuredArrivalDirection)
            ?? throw new SimulationValidationException(["折返實驗 fixture 缺少上行／Inbound 基準派車列。"]);
        var returnTemplate = source.Dispatch.ManualTimetableRows?.FirstOrDefault(row => row.Direction != fixture.MeasuredArrivalDirection)
            ?? throw new SimulationValidationException(["折返實驗 fixture 缺少回程／Outbound 基準派車列。"]);
        var rows = new List<ProjectManualTimetableRow>();
        var bindingCount = fixture.TurnbackBindings.Count;
        if (bindingCount == 0)
            throw new SimulationValidationException([$"Fixture「{fixture.Name}」沒有 TurnbackServiceBinding。"]);

        // Single facility: one feed at h.  Double facility: each feed is explicit
        // at bindingCount*h, phase shifted by its binding index.  Thus the combined
        // planned inflow is h while route-to-facility selection remains explicit.
        for (var bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
        {
            var binding = fixture.TurnbackBindings[bindingIndex];
            var feedPeriod = headway * bindingCount;
            for (var ordinal = 0; ; ordinal++)
            {
                var planned = ordinal * feedPeriod + bindingIndex * headway;
                if (planned >= plan.TotalHorizonSeconds - 1e-7)
                    break;

                var inboundRunId = $"{fixture.Name}-IN-{bindingIndex:00}-{ordinal:0000}";
                var returnRunId = $"{fixture.Name}-OUT-{bindingIndex:00}-{ordinal:0000}";
                var vehicleId = $"{fixture.Name}-EMU-{bindingIndex:00}-{ordinal:0000}";
                rows.Add(template with
                {
                    PlannedDepartureTimeSeconds = planned,
                    Direction = fixture.MeasuredArrivalDirection,
                    OriginPlatformId = template.OriginPlatformId,
                    VehicleId = vehicleId,
                    ServiceRunId = inboundRunId,
                    ContinueAfterTerminal = true,
                    ContinuationServiceRunId = returnRunId,
                    StationTimetableEntries = null,
                    ServiceRouteId = binding.ArrivalServiceRouteId
                });
                rows.Add(returnTemplate with
                {
                    PlannedDepartureTimeSeconds = planned + TurnbackPairLeadSeconds,
                    Direction = fixture.MeasuredArrivalDirection == TrainDirection.Outbound
                        ? TrainDirection.Inbound
                        : TrainDirection.Outbound,
                    OriginPlatformId = returnTemplate.OriginPlatformId,
                    VehicleId = vehicleId,
                    ServiceRunId = returnRunId,
                    ContinueAfterTerminal = false,
                    ContinuationServiceRunId = null,
                    StationTimetableEntries = null,
                    ServiceRouteId = binding.DepartureServiceRouteId
                });
            }
        }

        rows.Sort((left, right) =>
        {
            var time = left.PlannedDepartureTimeSeconds.CompareTo(right.PlannedDepartureTimeSeconds);
            return time != 0 ? time : string.CompareOrdinal(left.ServiceRouteId, right.ServiceRouteId);
        });
        var document = source with
        {
            ProjectId = $"TURNBACK-DENSE-{fixture.Name}",
            ProjectName = $"{fixture.Name} dense {headway.ToString("0.###", CultureInfo.InvariantCulture)}s",
            Dispatch = new ProjectDispatchPlan(
                DispatchPlanningMode.ManualTimetable,
                VehicleAssignmentMode.Automatic,
                [],
                rows.ToArray()),
            Simulation = source.Simulation with
            {
                TrainCount = rows.Count,
                HeadwaySeconds = headway
            }
        };
        return new(document, rows);
    }

    private static IReadOnlyList<string> ValidateBindings(TurnbackFixture fixture)
    {
        var errors = new List<string>();
        var routes = fixture.Document.ServiceRoutes.ToDictionary(route => route.ServiceRouteId, StringComparer.OrdinalIgnoreCase);
        var operations = fixture.Document.Topology.TurnbackOperations.ToArray();
        foreach (var binding in fixture.TurnbackBindings)
        {
            if (!routes.ContainsKey(binding.ArrivalServiceRouteId))
                errors.Add($"{binding.FacilityId} arrival route {binding.ArrivalServiceRouteId} 不存在");
            if (!routes.ContainsKey(binding.DepartureServiceRouteId))
                errors.Add($"{binding.FacilityId} departure route {binding.DepartureServiceRouteId} 不存在");
            if (!operations.Any(operation => operation.FacilityId.Equals(binding.FacilityId, StringComparison.OrdinalIgnoreCase)
                && operation.ArrivalServiceRouteId.Equals(binding.ArrivalServiceRouteId, StringComparison.OrdinalIgnoreCase)
                && operation.DepartureServiceRouteId.Equals(binding.DepartureServiceRouteId, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"{binding.FacilityId} 沒有對應的 arrival/departure TurnbackOperation");
        }

        if (fixture.TurnbackBindings.Count > 1 && !fixture.Document.Topology.Nodes.Any(node=>node.NodeId==CrossingFixtureBuilder.DiamondNodeId))
        {
            var duplicateRoutes = fixture.TurnbackBindings
                .GroupBy(binding => binding.ArrivalServiceRouteId, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key);
            errors.AddRange(duplicateRoutes.Select(route => $"多設施 fixture 重複使用 arrival route {route}"));
        }

        return errors;
    }

    private static SimulationWorld CreateWorld(TopologyProjectDocument document, TopologyProjectRuntime runtime) =>
        new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            InitialBrakingEstimationMode: document.Simulation.BrakingEstimationMode,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.EventsOnly,
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(10)).CreateWorld();

    private static TurnbackRunMetric Measure(
        TurnbackFixture fixture,
        double headway,
        IReadOnlyList<ProjectManualTimetableRow> plannedRows,
        SimulationWorld world,
        MeasurementPlan plan,
        double wallSeconds)
    {
        var events = world.Events;
        var measuredStation = fixture.MeasuredStationId;
        var arrivalDirection = fixture.MeasuredArrivalDirection;
        var returnDirection = arrivalDirection == TrainDirection.Outbound
            ? TrainDirection.Inbound
            : TrainDirection.Outbound;

        var arrivals = events
            .Where(item => item.EventType == SimulationEventType.Arrival
                && item.Direction == arrivalDirection
                && item.StationId?.Equals(measuredStation, StringComparison.OrdinalIgnoreCase) == true
                && plan.IsInMeasurementWindow(item.SimulationTimeSeconds))
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();
        var returnDepartures = events
            .Where(item => item.EventType == SimulationEventType.Departure
                && item.Direction == returnDirection
                && item.StationId?.Equals(measuredStation, StringComparison.OrdinalIgnoreCase) == true
                && plan.IsInMeasurementWindow(item.SimulationTimeSeconds))
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();

        var inputDepartures = events
            .Where(item => item.EventType == SimulationEventType.Departure
                && item.Direction == arrivalDirection
                && item.StationId?.Equals(GetOriginStation(fixture, item), StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(item => item.ServiceRunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.SimulationTimeSeconds).First())
            .ToDictionary(item => item.ServiceRunId, StringComparer.OrdinalIgnoreCase);
        var inboundPlannedRows = plannedRows.Where(item => item.Direction == arrivalDirection).ToArray();
        var plannedByRun = inboundPlannedRows.ToDictionary(item => item.ServiceRunId!, item => item.PlannedDepartureTimeSeconds, StringComparer.OrdinalIgnoreCase);
        var inputDelays = inputDepartures
            .Where(pair => plannedByRun.ContainsKey(pair.Key))
            .Select(pair => pair.Value.SimulationTimeSeconds - plannedByRun[pair.Key])
            .ToArray();

        var arrivalsByVehicle = events
            .Where(item => item.EventType == SimulationEventType.Arrival
                && item.Direction == arrivalDirection
                && item.StationId?.Equals(measuredStation, StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(item => item.VehicleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.SimulationTimeSeconds).ToArray(), StringComparer.OrdinalIgnoreCase);
        var returnCycles = new List<double>();
        foreach (var departure in returnDepartures)
        {
            if (arrivalsByVehicle.TryGetValue(departure.VehicleId, out var vehicleArrivals))
            {
                var arrival = vehicleArrivals.LastOrDefault(item => item.SimulationTimeSeconds <= departure.SimulationTimeSeconds);
                if (arrival is not null)
                    returnCycles.Add(departure.SimulationTimeSeconds - arrival.SimulationTimeSeconds);
            }
        }

        var dueAtWarmup = inboundPlannedRows.Count(row => row.PlannedDepartureTimeSeconds < plan.MeasurementStartSeconds);
        var dueAtEnd = inboundPlannedRows.Count(row => row.PlannedDepartureTimeSeconds < plan.MeasurementEndSeconds);
        var arrivalsBeforeWarmup = events.Count(item => item.EventType == SimulationEventType.Arrival
            && item.Direction == arrivalDirection
            && item.StationId?.Equals(measuredStation, StringComparison.OrdinalIgnoreCase) == true
            && item.SimulationTimeSeconds < plan.MeasurementStartSeconds);
        var arrivalsBeforeEnd = events.Count(item => item.EventType == SimulationEventType.Arrival
            && item.Direction == arrivalDirection
            && item.StationId?.Equals(measuredStation, StringComparison.OrdinalIgnoreCase) == true
            && item.SimulationTimeSeconds < plan.MeasurementEndSeconds);

        var waitingEpisodes = CountWaitingEpisodes(events, plan);
        var inboundBucketCounts = plan.CountBuckets(arrivals, item => item.SimulationTimeSeconds);
        var returnBucketCounts = plan.CountBuckets(returnDepartures, item => item.SimulationTimeSeconds);
        var collisions = events.Count(item => item.EventType == SimulationEventType.Collision);
        var violations = events.Count(item => item.EventType == SimulationEventType.StationStopViolation);
        var directionChanges = events.Count(item => item.EventType == SimulationEventType.DirectionChanged
            && plan.IsInMeasurementWindow(item.SimulationTimeSeconds));
        var predictedCollisions = events.Count(item => item.EventType == SimulationEventType.PredictedCollision);
        var signalAuthorityViolations = events.Count(item => item.EventType == SimulationEventType.SignalAuthorityViolation);

        return new(
            fixture.Name,
            fixture.HandbookCase,
            fixture.Theory.DesignHeadwaySeconds,
            headway,
            true,
            null,
            inboundPlannedRows.Length,
            arrivals.Length,
            returnDepartures.Length,
            inboundBucketCounts,
            returnBucketCounts,
            Math.Max(0, dueAtWarmup - arrivalsBeforeWarmup),
            Math.Max(0, dueAtEnd - arrivalsBeforeEnd),
            waitingEpisodes,
            collisions,
            violations,
            directionChanges,
            predictedCollisions,
            signalAuthorityViolations,
            inputDelays.Length == 0 ? 0 : inputDelays.Average(),
            inputDelays.Length == 0 ? 0 : inputDelays.Max(),
            returnCycles.Count == 0 ? 0 : returnCycles.Average(),
            returnCycles.Count == 0 ? 0 : returnCycles.Max(),
            world.CurrentTimeSeconds,
            wallSeconds,
            events.Count(e=>e.EventType==SimulationEventType.TailTrackReached),
            events.Count(e=>e.EventType==SimulationEventType.TailTrackReturnStarted),
            events.Count(e=>e.EventType==SimulationEventType.TurnaroundStarted),
            fixture.TurnbackBindings.ToDictionary(b=>b.FacilityId,b=>returnDepartures.Count(e=>e.ServiceRouteId==b.DepartureServiceRouteId)),
            plan.CacheKey);
    }

    private static string GetOriginStation(TurnbackFixture fixture, SimulationEvent simulationEvent)
    {
        var route = fixture.Document.ServiceRoutes.FirstOrDefault(item => item.ServiceRouteId.Equals(simulationEvent.ServiceRouteId, StringComparison.OrdinalIgnoreCase));
        return route?.Stops.FirstOrDefault()?.StationId
            ?? fixture.Document.ServiceRoutes.First(route => route.ServiceRouteId.Equals(fixture.TurnbackBindings[0].ArrivalServiceRouteId, StringComparison.OrdinalIgnoreCase)).Stops[0].StationId;
    }

    private static int CountWaitingEpisodes(IReadOnlyList<SimulationEvent> events, MeasurementPlan plan)
    {
        // WaitingForResource is an episode marker in the normal path, but some
        // topology reservation paths can re-emit it after a short reset.  Coalesce
        // contiguous 0.1 s markers per service run/resource so the metric never
        // reports waiting seconds as episode count.
        return events
            .Where(item => item.EventType == SimulationEventType.WaitingForResource
                && plan.IsInMeasurementWindow(item.SimulationTimeSeconds))
            .GroupBy(item => $"{item.ServiceRunId}\u001f{item.ResourceId ?? string.Empty}", StringComparer.OrdinalIgnoreCase)
            .Sum(group =>
            {
                var ordered = group.OrderBy(item => item.SimulationTimeSeconds).ToArray();
                var episodes = 0;
                var previous = double.NegativeInfinity;
                foreach (var item in ordered)
                {
                    if (item.SimulationTimeSeconds - previous > .11)
                        episodes++;
                    previous = item.SimulationTimeSeconds;
                }

                return episodes;
            });
    }

    private static TurnbackRunMetric NotExecutedMetric(TurnbackFixture fixture, double headway, MeasurementPlan plan, int plannedRuns, string reason) =>
        new(Fixture: fixture.Name, HandbookCase: fixture.HandbookCase, TheoryDesignHeadwaySeconds: fixture.Theory.DesignHeadwaySeconds,
            InputHeadwaySeconds: headway, Executed: false, NotExecutedReason: reason, PlannedInboundRuns: plannedRuns,
            ActualInboundArrivalsAtMeasuredStation: 0, ActualReturnOutboundDeparturesAtMeasuredStation: 0,
            InboundArrivalCountsByBucket: [], ReturnDepartureCountsByBucket: [], PendingInboundAtWarmup: 0,
            PendingInboundAtMeasurementEnd: 0, WaitingForResourceEpisodes: 0, CollisionEvents: 0, StationStopViolationEvents: 0,
            DirectionChangedEvents: 0, PredictedCollisionEvents: 0, SignalAuthorityViolationEvents: 0,
            MeanInputDepartureDelaySeconds: 0, MaxInputDepartureDelaySeconds: 0, MeanReturnCycleSeconds: 0,
            MaxReturnCycleSeconds: 0, SimulationTimeSeconds: 0, WallSeconds: 0, MeasurementPlanKey: plan.CacheKey);
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void WriteJson<T>(string path, T value, JsonSerializerOptions options) =>
        WriteText(path, JsonSerializer.Serialize(value, options));

    private static void WriteText(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(false));

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record PreparedDocument(
        TopologyProjectDocument Document,
        IReadOnlyList<ProjectManualTimetableRow> Rows);
}
