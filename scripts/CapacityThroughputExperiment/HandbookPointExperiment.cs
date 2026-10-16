using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

/// <summary>
/// Isolated physical runs for the intermediate points in handbook case 07.
///
/// Case 07 is a four-point capacity example, but S1 and S14 are turnbacks and
/// therefore belong to the physical turnback/full-network experiment.  This
/// runner intentionally executes only S7 and S10 on the maintained three
/// station carrier.  It maps each source point to the carrier's real B station
/// and uses CapacityRuntimeMapper before creating SimulationWorld.
/// </summary>
public static class HandbookPointExperiment
{
    public const string CarrierRelativePath = "samples/小型/01-小型-三站雙股島式站場範例.mrtsim.json";
    public const string CarrierMiddleStationId = "B";
    public const string PrimaryCaseId = "07";
    public const string UrcsCaseId = "07b";

    private const double OriginMeasurementCutoffTolerance = 1e-9;
    private const string FiniteGradeModelNote =
        "runtime finite-grade assumption: CapacityRuntimeMapper applies each point's finite in/out grade segments over the three-station carrier route boundaries; this is a synthetic physical fixture and does not assert an infinite real-world grade profile.";
    private const string CarrierMappingNote =
        "source handbook station IDs S7/S10 are explicitly mapped to carrier station B; the source S1/S14 turnbacks are not silently converted into station runs.";

    /// <summary>
    /// Runs both case 07 (Taiwan Handbook theory) and case 07b (URCS-compatible
    /// theory) by default.  <paramref name="caseKind"/> accepts "07", "main",
    /// "taiwan", "07b", "urcs", or "both".
    /// </summary>
    public static HandbookPointRunSummary PublicRun(
        string root,
        MeasurementPlan plan,
        string outputRoot,
        string? caseKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);

        var repositoryRoot = Path.GetFullPath(root);
        var targetRoot = Path.GetFullPath(outputRoot);
        if (!Directory.Exists(repositoryRoot))
            throw new DirectoryNotFoundException($"找不到實驗根目錄：{repositoryRoot}");

        var selected = SelectCases(caseKind).ToArray();
        var carrierPath = Path.Combine(repositoryRoot, CarrierRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var carrier = DeserializeProject(carrierPath);

        // Check both directions before loading point settings.  This keeps an
        // accidental route/platform mismatch from producing partial metrics.
        ValidateCarrierRoutes(carrier);

        var engineHash = ComputeEngineHash();
        var options = CreateJsonOptions();
        var allMetrics = new List<HandbookPointMetric>();
        var modeDirectories = new List<string>();

        foreach (var selectedCase in selected)
        {
            var sourcePath = ResolveCasePath(repositoryRoot, selectedCase);
            var source = DeserializeProject(sourcePath);
            ValidateSourceCase(selectedCase, source);

            // PublicRun's outputRoot is the caller-owned isolated point-run
            // directory.  Keep the mode folders directly beneath it so a
            // parent runner can pass .../handbook-completion-11h/points and
            // discover projects/metrics without an implicit extra segment.
            var modeDirectory = Path.Combine(targetRoot, selectedCase.ModeTag);
            modeDirectories.Add(modeDirectory);
            Directory.CreateDirectory(modeDirectory);
            Directory.CreateDirectory(Path.Combine(modeDirectory, "projects"));
            Directory.CreateDirectory(Path.Combine(modeDirectory, "metrics"));
            plan.WriteManifest(modeDirectory, options);
            File.WriteAllText(Path.Combine(modeDirectory, "engine-sha256.txt"), engineHash, new UTF8Encoding(false));

            var sourcePoints = source.SpatialCapacity!.Points
                .Where(point => point.Kind == SpatialReferencePointKind.IntermediateStation)
                .OrderBy(point => point.ReferencePointId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (sourcePoints.Length == 0)
                throw new SimulationValidationException(["Case 07/07b 沒有可執行的 IntermediateStation 參考點。"]);

            var skipped = source.SpatialCapacity.Points
                .Where(point => point.Kind != SpatialReferencePointKind.IntermediateStation)
                .Select(point => new SkippedHandbookPoint(
                    point.ReferencePointId,
                    point.Kind.ToString(),
                    "非 IntermediateStation；S1/S14 由折返或完整路網實體 runner 處理。"))
                .ToArray();

            var caseMetrics = new List<HandbookPointMetric>();
            foreach (var sourcePoint in sourcePoints)
            {
                var theoryPoint = SpatialCapacityProjectAnalysis.ToDefinition(sourcePoint);
                foreach (var direction in Enum.GetValues<SpatialCapacityDirection>())
                {
                    var theory = SpatialCapacityAnalysis.Calculate(
                        theoryPoint,
                        source.SpatialCapacity.Global,
                        source.SpatialCapacity.Train,
                        direction,
                        selectedCase.Mode);

                    // The source handbook point remains identifiable in theory
                    // output, while its runtime copy explicitly targets carrier B.
                    var mappedPoint = sourcePoint with
                    {
                        StationId = CarrierMiddleStationId,
                        Name = $"{sourcePoint.Name}（載體 B）"
                    };
                    var runtimeSettings = new SpatialCapacityProjectSettings(
                        source.SpatialCapacity.Global,
                        source.SpatialCapacity.Train,
                        [mappedPoint],
                        selectedCase.Mode,
                        CompareAsSingleScenario: false);
                    var runtimeSource = carrier with
                    {
                        ProjectId = $"HANDBOOK-POINT-{selectedCase.CaseId}-{sourcePoint.ReferencePointId}-{direction}",
                        ProjectName = $"{selectedCase.Label} {sourcePoint.ReferencePointId} {direction}（載體B實體點）",
                        SpatialCapacity = runtimeSettings
                    };

                    // Keep the direction physically isolated: only this mapped
                    // direction receives point profiles and only its carrier
                    // dispatch rows are populated.
                    var mapped = CapacityRuntimeMapper.ApplyStations(runtimeSource, ToTrainDirection(direction));
                    var document = BuildSingleDirectionSchedule(
                        mapped.Document,
                        ToTrainDirection(direction),
                        theory.DesignHeadwaySeconds,
                        plan,
                        selectedCase,
                        sourcePoint.ReferencePointId);
                    TopologyProjectFormat.Validate(document);

                    var physicsFingerprint = ComputeRuntimePhysicsFingerprint(document);
                    var projectStem = $"{selectedCase.CaseId}-{sourcePoint.ReferencePointId}-{DirectionTag(direction)}";
                    var projectPath = Path.Combine(modeDirectory, "projects", projectStem + ".mrtsim.json");
                    var serialized = TopologyProjectFormat.Serialize(document);
                    var hashPath = projectPath + ".engine-sha256";
                    var versionPath = projectPath + ".experiment-version";
                    var metricPath = Path.Combine(modeDirectory, "metrics", projectStem + ".json");
                    var cacheMatches = File.Exists(projectPath)
                        && File.ReadAllText(projectPath) == serialized
                        && File.Exists(hashPath)
                        && File.ReadAllText(hashPath).Trim() == engineHash
                        && File.Exists(versionPath)
                        && File.ReadAllText(versionPath).Trim() == plan.CacheKey
                        && File.Exists(metricPath);

                    WriteText(projectPath, serialized);
                    WriteText(hashPath, engineHash);
                    WriteText(versionPath, plan.CacheKey);

                    HandbookPointMetric metric = null!;
                    if (cacheMatches)
                    {
                        metric = JsonSerializer.Deserialize<HandbookPointMetric>(
                            File.ReadAllText(metricPath), options)
                            ?? throw new InvalidOperationException($"無法讀取快取指標：{metricPath}");
                        if (!metric.MatchesIdentity(
                                selectedCase.CaseId,
                                selectedCase.ModeTag,
                                sourcePoint.ReferencePointId,
                                direction.ToString(),
                                plan.CacheKey,
                                engineHash))
                        {
                            cacheMatches = false;
                        }
                    }

                    if (!cacheMatches)
                    {
                        metric = Execute(
                            document,
                            sourcePoint,
                            selectedCase,
                            direction,
                            theory,
                            plan,
                            engineHash,
                            projectPath,
                            physicsFingerprint,
                            options);
                        WriteJson(metricPath, metric, options);
                    }

                    caseMetrics.Add(metric);
                    Console.WriteLine(cacheMatches
                        ? $"HANDBOOK POINT RESUME {selectedCase.CaseId} {sourcePoint.ReferencePointId} {direction}"
                        : $"HANDBOOK POINT {selectedCase.CaseId} {sourcePoint.ReferencePointId} {direction}: actual={metric.ActualEventCount}, spanRate={metric.ActualSpanRatePerHour:F2}/h, p95={metric.ActualP95HeadwaySeconds:F2}s, pendingEnd={metric.PendingAtMeasurementEnd}");
                }
            }

            WriteModeOutputs(modeDirectory, selectedCase, sourcePath, carrierPath, sourcePoints, skipped, caseMetrics, plan, engineHash, options);
            allMetrics.AddRange(caseMetrics);
        }

        var aggregateDirectory = targetRoot;
        Directory.CreateDirectory(aggregateDirectory);
        WriteJson(Path.Combine(aggregateDirectory, "metrics.json"), allMetrics, options);
        WriteCsv(Path.Combine(aggregateDirectory, "metrics.csv"), allMetrics);
        WriteJson(Path.Combine(aggregateDirectory, "summary.json"), new
        {
            measurementPlan = plan,
            engineSha256 = engineHash,
            modes = selected.Select(item => new { item.CaseId, item.ModeTag, mode = item.Mode.ToString() }).ToArray(),
            sourcePointCount = allMetrics.Select(item => item.SourcePointId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            metricCount = allMetrics.Count,
            physicalRuntimeNote = FiniteGradeModelNote,
            mappingNote = CarrierMappingNote,
            outputDirectories = modeDirectories
        }, options);

        return new HandbookPointRunSummary(
            plan.CacheKey,
            engineHash,
            allMetrics.Count,
            allMetrics,
            modeDirectories,
            aggregateDirectory);
    }

    /// <summary>Convenience overload for the parent runner's default 07 + 07b call.</summary>
    public static HandbookPointRunSummary PublicRun(string root, MeasurementPlan plan, string outputRoot) =>
        PublicRun(root, plan, outputRoot, caseKind: null);

    /// <summary>
    /// Read-only structural/theory smoke check.  It validates both directional
    /// route mappings and the four intended source-point theory rows without
    /// advancing an 11-hour world.
    /// </summary>
    public static HandbookPointSmokeResult Smoke(string root, string? caseKind = null)
    {
        var repositoryRoot = Path.GetFullPath(root);
        var carrierPath = Path.Combine(repositoryRoot, CarrierRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var carrier = DeserializeProject(carrierPath);
        ValidateCarrierRoutes(carrier);

        var rows = new List<HandbookPointTheoryRow>();
        foreach (var selectedCase in SelectCases(caseKind))
        {
            var source = DeserializeProject(ResolveCasePath(repositoryRoot, selectedCase));
            ValidateSourceCase(selectedCase, source);
            var settings = source.SpatialCapacity!;
            foreach (var point in settings.Points.Where(item => item.Kind == SpatialReferencePointKind.IntermediateStation))
            {
                var definition = SpatialCapacityProjectAnalysis.ToDefinition(point);
                foreach (var direction in Enum.GetValues<SpatialCapacityDirection>())
                {
                    var result = SpatialCapacityAnalysis.Calculate(definition, settings.Global, settings.Train, direction, selectedCase.Mode);
                    rows.Add(new(selectedCase.CaseId, selectedCase.ModeTag, point.ReferencePointId,
                        direction.ToString(), result.SafetyHeadwaySeconds, result.DesignHeadwaySeconds,
                        result.LineCapacityPerHour));
                }
            }
        }

        return new HandbookPointSmokeResult(
            CarrierRelativePath,
            [TrainDirection.Outbound.ToString(), TrainDirection.Inbound.ToString()],
            rows,
            [FiniteGradeModelNote, CarrierMappingNote]);
    }

    private static HandbookPointMetric Execute(
        TopologyProjectDocument document,
        ProjectSpatialReferencePoint sourcePoint,
        SelectedHandbookCase selectedCase,
        SpatialCapacityDirection capacityDirection,
        SpatialCapacityResult theory,
        MeasurementPlan plan,
        string engineHash,
        string projectPath,
        string physicsFingerprint,
        JsonSerializerOptions options)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(
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

        foreach (var boundary in plan.ProgressBoundaries(900))
            world.AdvanceTo(boundary);
        if (Math.Abs(world.CurrentTimeSeconds - plan.TotalHorizonSeconds) > .11)
            throw new InvalidOperationException($"{sourcePoint.ReferencePointId} {capacityDirection} 未推進到 {plan.HorizonLabel}。實際 {world.CurrentTimeSeconds} 秒。");

        var direction = ToTrainDirection(capacityDirection);
        var context = world.GetTopologyResultContext();
        var rows = document.Dispatch.ManualTimetableRows?.Where(row => row.Direction == direction).ToArray() ?? [];
        var mappedStation = CarrierMiddleStationId;
        var originStation = document.ServiceRoutes
            .Single(route => route.ServiceRouteId == document.DirectionRouteBindings.Single(binding => binding.Direction == direction).ServiceRouteId)
            .Stops.First().StationId;

        var stationDepartures = world.Events
            .Where(item => item.EventType == SimulationEventType.Departure
                && item.Direction == direction
                && context.MatchesStationEvent(direction, mappedStation, item)
                && plan.IsInMeasurementWindow(item.SimulationTimeSeconds))
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();
        var stationArrivals = world.Events
            .Where(item => item.EventType == SimulationEventType.Arrival
                && item.Direction == direction
                && context.MatchesStationEvent(direction, mappedStation, item)
                && plan.IsInMeasurementWindow(item.SimulationTimeSeconds))
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();
        var intervals = stationDepartures
            .Zip(stationDepartures.Skip(1), (first, second) => second.SimulationTimeSeconds - first.SimulationTimeSeconds)
            .Where(item => item >= 0)
            .Order()
            .ToArray();
        var originDepartures = world.Events
            .Where(item => item.EventType == SimulationEventType.Departure
                && item.Direction == direction
                && context.MatchesStationEvent(direction, originStation, item))
            .GroupBy(item => item.ServiceRunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.SimulationTimeSeconds).First())
            .OrderBy(item => item.SimulationTimeSeconds)
            .ToArray();
        var plannedByRun = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ServiceRunId))
            .ToDictionary(row => row.ServiceRunId!, row => row.PlannedDepartureTimeSeconds, StringComparer.OrdinalIgnoreCase);

        var originDelays = originDepartures
            .Where(item => plannedByRun.ContainsKey(item.ServiceRunId))
            .Select(item => item.SimulationTimeSeconds - plannedByRun[item.ServiceRunId])
            .ToArray();
        var firstHalfDelays = originDepartures
            .Where(item => item.SimulationTimeSeconds >= plan.MeasurementStartSeconds - OriginMeasurementCutoffTolerance
                && item.SimulationTimeSeconds < plan.MeasurementMidpointSeconds - OriginMeasurementCutoffTolerance)
            .Where(item => plannedByRun.ContainsKey(item.ServiceRunId))
            .Select(item => item.SimulationTimeSeconds - plannedByRun[item.ServiceRunId])
            .ToArray();
        var lastHalfDelays = originDepartures
            .Where(item => item.SimulationTimeSeconds >= plan.MeasurementMidpointSeconds - OriginMeasurementCutoffTolerance
                && item.SimulationTimeSeconds < plan.MeasurementEndSeconds - OriginMeasurementCutoffTolerance)
            .Where(item => plannedByRun.ContainsKey(item.ServiceRunId))
            .Select(item => item.SimulationTimeSeconds - plannedByRun[item.ServiceRunId])
            .ToArray();

        int PendingAt(double cutoff)
        {
            var due = rows.Count(row => row.PlannedDepartureTimeSeconds < cutoff - OriginMeasurementCutoffTolerance);
            var departed = originDepartures.Count(item => item.SimulationTimeSeconds < cutoff - OriginMeasurementCutoffTolerance);
            return Math.Max(0, due - departed);
        }

        var totalDirectionEvents = world.Events.Count(item => item.Direction == direction);
        var spanSeconds = intervals.Length == 0
            ? 0
            : stationDepartures[^1].SimulationTimeSeconds - stationDepartures[0].SimulationTimeSeconds;
        var spanRate = spanSeconds > 0 ? (stationDepartures.Length - 1) * 3600d / spanSeconds : 0;
        var runtimeGradients = document.Topology.Gradients.Select(item => item.GradePermille).ToArray();
        var metric = new HandbookPointMetric(
            selectedCase.CaseId,
            selectedCase.SourceFileName,
            selectedCase.ModeTag,
            selectedCase.Mode.ToString(),
            sourcePoint.ReferencePointId,
            sourcePoint.StationId,
            mappedStation,
            capacityDirection.ToString(),
            theory.SafetyHeadwaySeconds,
            theory.DesignHeadwaySeconds,
            theory.LineCapacityPerHour,
            3600d / theory.DesignHeadwaySeconds,
            (double)theory.LineCapacityPerHour,
            theory.DesignHeadwaySeconds,
            stationDepartures.Length,
            spanSeconds,
            spanRate,
            Percentile(intervals, .50),
            Percentile(intervals, .95),
            intervals.Length == 0 ? 0 : intervals[0],
            stationArrivals.Length,
            originDepartures.Length,
            PendingAt(plan.MeasurementStartSeconds),
            PendingAt(plan.MeasurementEndSeconds),
            Mean(firstHalfDelays),
            Mean(lastHalfDelays),
            plan.CountBuckets(stationDepartures, item => item.SimulationTimeSeconds),
            totalDirectionEvents,
            world.Events.Count(item => item.Direction == direction && item.EventType == SimulationEventType.WaitingForResource),
            world.Events.Count(item => item.Direction == direction && item.EventType == SimulationEventType.Collision),
            world.Events.Count(item => item.Direction == direction && item.EventType == SimulationEventType.PredictedCollision),
            world.Events.Count(item => item.Direction == direction && item.EventType == SimulationEventType.StationStopViolation),
            world.Events.Count(item => item.Direction == direction && item.EventType == SimulationEventType.SignalAuthorityViolation),
            Mean(originDelays),
            originDelays.Length == 0 ? 0 : originDelays.Max(),
            world.CurrentTimeSeconds,
            clock.Elapsed.TotalSeconds,
            runtimeGradients.Length == 0 ? 0 : runtimeGradients.Min(),
            runtimeGradients.Length == 0 ? 0 : runtimeGradients.Max(),
            physicsFingerprint,
            engineHash,
            projectPath,
            plan.CacheKey,
            [
                FiniteGradeModelNote,
                CarrierMappingNote,
                "CapacityRuntimeMapper applied direction-specific station grade in/out, speed limits, dwell, effective service braking (nominal deceleration × BrakeEffect), synthetic entry signal and overlap protection before SimulationWorld creation.",
                $"runtime source point {sourcePoint.ReferencePointId} ({sourcePoint.Kind}) mapped to physical carrier station {mappedStation}; source station ID retained as the theory key."
            ]);
        return metric;
    }

    private static TopologyProjectDocument BuildSingleDirectionSchedule(
        TopologyProjectDocument source,
        TrainDirection direction,
        double headway,
        MeasurementPlan plan,
        SelectedHandbookCase selectedCase,
        string sourcePointId)
    {
        if (!double.IsFinite(headway) || headway <= 0)
            throw new ArgumentOutOfRangeException(nameof(headway));
        var template = source.Dispatch.ManualTimetableRows?.FirstOrDefault(row => row.Direction == direction)
            ?? throw new SimulationValidationException([$"載體缺少{direction}方向手動派車模板。"]);
        var rows = new List<ProjectManualTimetableRow>();
        for (var ordinal = 0; ordinal * headway < plan.TotalHorizonSeconds - 1e-7; ordinal++)
        {
            var planned = ordinal * headway;
            rows.Add(template with
            {
                PlannedDepartureTimeSeconds = planned,
                VehicleId = $"HP-{selectedCase.CaseId}-{sourcePointId}-{direction}-{ordinal:0000}",
                ServiceRunId = $"HP-{selectedCase.CaseId}-{sourcePointId}-{direction}-{ordinal:0000}",
                ContinueAfterTerminal = false,
                ContinuationServiceRunId = null,
                StationTimetableEntries = null
            });
        }
        if (rows.Count == 0)
            throw new SimulationValidationException(["量測窗設定沒有產生任何派車列。"]);
        return source with
        {
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
    }

    private static void ValidateCarrierRoutes(TopologyProjectDocument carrier)
    {
        foreach (var direction in Enum.GetValues<TrainDirection>())
        {
            var binding = carrier.DirectionRouteBindings.SingleOrDefault(item => item.Direction == direction)
                ?? throw new SimulationValidationException([$"三站載體缺少{direction}方向 route binding。"]);
            var route = carrier.ServiceRoutes.SingleOrDefault(item => item.ServiceRouteId.Equals(binding.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
                ?? throw new SimulationValidationException([$"三站載體缺少{direction}方向 ServiceRoute「{binding.ServiceRouteId}」。"]);
            var expectedStations = direction == TrainDirection.Outbound
                ? new[] { "A", CarrierMiddleStationId, "C" }
                : new[] { "C", CarrierMiddleStationId, "A" };
            if (!route.Stops.Select(stop => stop.StationId).SequenceEqual(expectedStations, StringComparer.OrdinalIgnoreCase))
                throw new SimulationValidationException([$"三站載體{direction} route 必須明確是 {string.Join("→", expectedStations)}，實際為 {string.Join("→", route.Stops.Select(stop => stop.StationId))}。"]);
            var middleStop = route.Stops.SingleOrDefault(item => item.StationId.Equals(CarrierMiddleStationId, StringComparison.OrdinalIgnoreCase))
                ?? throw new SimulationValidationException([$"三站載體{direction} route 沒有明確的中間站 B。"]);
            if (middleStop.CandidatePlatformIds.Count != 1)
                throw new SimulationValidationException([$"三站載體{direction} route 的 B 停站未唯一指定月台。"]);
            var platformId = middleStop.CandidatePlatformIds[0];
            var platform = carrier.Topology.Platforms.SingleOrDefault(item => item.PlatformId.Equals(platformId, StringComparison.OrdinalIgnoreCase))
                ?? throw new SimulationValidationException([$"三站載體{direction} route 的 B 月台「{platformId}」不存在。"]);
            if (!platform.StationId.Equals(CarrierMiddleStationId, StringComparison.OrdinalIgnoreCase))
                throw new SimulationValidationException([$"三站載體{direction} route 的 B 月台參照錯誤：{platform.StationId}。"]);
            if (route.Traversals.Count == 0)
                throw new SimulationValidationException([$"三站載體{direction} route 沒有 physical traversal。"]);
        }
    }

    private static void ValidateSourceCase(SelectedHandbookCase selectedCase, TopologyProjectDocument source)
    {
        var settings = source.SpatialCapacity
            ?? throw new SimulationValidationException([$"{selectedCase.SourceFileName} 缺少 spatialCapacity。"]);
        var expectedMode = selectedCase.Mode;
        if (settings.Mode != expectedMode)
            throw new SimulationValidationException([$"{selectedCase.SourceFileName} 的容量模式是 {settings.Mode}，預期 {expectedMode}。"]);
        var points = settings.Points.Where(point => point.Kind == SpatialReferencePointKind.IntermediateStation).ToArray();
        var ids = points.Select(point => point.ReferencePointId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!ids.Contains("S7") || !ids.Contains("S10"))
            throw new SimulationValidationException([$"{selectedCase.SourceFileName} 應包含 IntermediateStation S7 與 S10。"]);
        // The handbook project intentionally stores S7/S10 as analysis points
        // while its Schema 8 carrier remains the maintained three-station
        // A/B/C topology.  Do not require source station IDs to exist in that
        // carrier; the explicit sourcePoint -> carrier B mapping below is the
        // purpose of this runner.
    }

    private static string ResolveCasePath(string root, SelectedHandbookCase selectedCase) =>
        Path.Combine(root, "samples", "handbook-capacity", selectedCase.SourceFileName);

    private static TopologyProjectDocument DeserializeProject(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到 handbook point project。", path);
        return TopologyProjectFormat.Deserialize(File.ReadAllText(path));
    }

    private static IReadOnlyList<SelectedHandbookCase> SelectCases(string? caseKind)
    {
        var normalized = string.IsNullOrWhiteSpace(caseKind) ? "both" : caseKind.Trim().ToLowerInvariant();
        return normalized switch
        {
            "07" or "main" or "taiwan" or "taiwanhandbook" => [CreateCase(PrimaryCaseId)],
            "07b" or "urcs" or "urcscompatible" => [CreateCase(UrcsCaseId)],
            "both" or "all" => [CreateCase(PrimaryCaseId), CreateCase(UrcsCaseId)],
            _ => throw new ArgumentException($"未知 handbook point case kind：{caseKind}。請使用 07、07b、main、urcs 或 both。", nameof(caseKind))
        };
    }

    private static SelectedHandbookCase CreateCase(string caseId) => caseId switch
    {
        PrimaryCaseId => new(caseId, "07-範例七-四點瓶頸.mrtsim.json", "taiwan-handbook", "台灣手冊", SpatialCapacityCalculationMode.TaiwanHandbook),
        UrcsCaseId => new(caseId, "07b-範例七-URCS相容對照.mrtsim.json", "urcs-compatible", "URCS相容對照", SpatialCapacityCalculationMode.UrcsCompatible),
        _ => throw new ArgumentOutOfRangeException(nameof(caseId))
    };

    private static TrainDirection ToTrainDirection(SpatialCapacityDirection direction) => direction switch
    {
        SpatialCapacityDirection.Forward => TrainDirection.Outbound,
        SpatialCapacityDirection.Reverse => TrainDirection.Inbound,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };

    private static string DirectionTag(SpatialCapacityDirection direction) => direction switch
    {
        SpatialCapacityDirection.Forward => "outbound",
        SpatialCapacityDirection.Reverse => "inbound",
        _ => direction.ToString().ToLowerInvariant()
    };

    private static string ComputeEngineHash() =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));

    private static string ComputeRuntimePhysicsFingerprint(TopologyProjectDocument document)
    {
        var value = new
        {
            document.Topology.Gradients,
            document.Topology.SpeedLimits,
            document.SignalBlocks,
            document.VehicleTypes,
            document.Operations,
            document.StopPatterns
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }

    private static double Mean(IReadOnlyCollection<double> values) => values.Count == 0 ? 0 : values.Average();

    private static double Percentile(IReadOnlyList<double> orderedValues, double percentile)
    {
        if (orderedValues.Count == 0) return 0;
        if (orderedValues.Count == 1) return orderedValues[0];
        var position = (orderedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return orderedValues[lower];
        var weight = position - lower;
        return orderedValues[lower] + (orderedValues[upper] - orderedValues[lower]) * weight;
    }

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

    private static void WriteModeOutputs(
        string modeDirectory,
        SelectedHandbookCase selectedCase,
        string sourcePath,
        string carrierPath,
        IReadOnlyList<ProjectSpatialReferencePoint> sourcePoints,
        IReadOnlyList<SkippedHandbookPoint> skipped,
        IReadOnlyList<HandbookPointMetric> metrics,
        MeasurementPlan plan,
        string engineHash,
        JsonSerializerOptions options)
    {
        WriteJson(Path.Combine(modeDirectory, "metrics.json"), metrics, options);
        WriteCsv(Path.Combine(modeDirectory, "metrics.csv"), metrics);
        WriteJson(Path.Combine(modeDirectory, "manifest.json"), new
        {
            caseId = selectedCase.CaseId,
            sourceCaseFile = Path.GetRelativePath(Path.GetDirectoryName(modeDirectory)!, sourcePath),
            carrier = CarrierRelativePath,
            theoryMode = selectedCase.Mode.ToString(),
            theoryModeLabel = selectedCase.Label,
            runtimeUsesSameGradePhysicsAcrossModes = true,
            finiteGradeModel = FiniteGradeModelNote,
            carrierMapping = CarrierMappingNote,
            pointsExecuted = sourcePoints.Where(point => point.Kind == SpatialReferencePointKind.IntermediateStation).Select(point => point.ReferencePointId).ToArray(),
            pointsSkipped = skipped,
            directionsExecuted = Enum.GetNames<SpatialCapacityDirection>(),
            measurementPlan = plan,
            engineSha256 = engineHash,
            metricCount = metrics.Count,
            runtimePhysicsFingerprints = metrics.Select(metric => metric.RuntimePhysicsFingerprint).Distinct().ToArray(),
            sourcePointValues = sourcePoints.Select(point => new
            {
                point.ReferencePointId,
                point.StationId,
                point.Kind,
                point.StationForwardGradeInPermille,
                point.StationForwardGradeOutPermille,
                point.StationReverseGradeInPermille,
                point.StationReverseGradeOutPermille,
                point.StationForwardDwellSeconds,
                point.StationReverseDwellSeconds,
                point.StationForwardOverlapMeters,
                point.StationReverseOverlapMeters
            }).ToArray()
        }, options);
    }

    private static void WriteJson<T>(string path, T value, JsonSerializerOptions options) =>
        WriteText(path, JsonSerializer.Serialize(value, options));

    private static void WriteText(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(false));

    private static void WriteCsv(string path, IReadOnlyList<HandbookPointMetric> metrics)
    {
        var lines = new List<string>
        {
            "caseId,modeTag,sourcePointId,sourceStationId,mappedStationId,direction,theorySafetyHeadwaySeconds,theoryDesignHeadwaySeconds,theoryLineCapacityPerHourTruncated,theoryLineCapacityPerHourUntruncated,inputHeadwaySeconds,actualEventCount,actualEventSpanSeconds,actualSpanRatePerHour,actualMedianHeadwaySeconds,actualP95HeadwaySeconds,actualMinimumHeadwaySeconds,actualArrivalCount,actualOriginEventCount,pendingAt1800Seconds,pendingAt37800Seconds,originDelayFirstHalfMeanSeconds,originDelayLastHalfMeanSeconds,measurementBucketCounts,totalDirectionEventCount,waitingForResourceEvents,collisionEvents,predictedCollisionEvents,stationStopViolationEvents,signalAuthorityViolationEvents,originDelayMeanSeconds,originDelayMaxSeconds,simulationTimeSeconds,wallSeconds,appliedGradientMinPermille,appliedGradientMaxPermille,runtimePhysicsFingerprint,engineSha256,measurementPlanKey,projectPath"
        };
        foreach (var metric in metrics)
        {
            var fields = new[]
            {
                metric.CaseId, metric.ModeTag, metric.SourcePointId, metric.SourceStationId, metric.MappedStationId, metric.Direction,
                F(metric.TheorySafetyHeadwaySeconds), F(metric.TheoryDesignHeadwaySeconds), metric.TheoryLineCapacityPerHourTruncated.ToString(CultureInfo.InvariantCulture),
                F(metric.TheoryLineCapacityPerHourUntruncated), F(metric.InputHeadwaySeconds), metric.ActualEventCount.ToString(CultureInfo.InvariantCulture), F(metric.ActualEventSpanSeconds),
                F(metric.ActualSpanRatePerHour), F(metric.ActualMedianHeadwaySeconds), F(metric.ActualP95HeadwaySeconds), F(metric.ActualMinimumHeadwaySeconds),
                metric.ActualArrivalCount.ToString(CultureInfo.InvariantCulture), metric.ActualOriginEventCount.ToString(CultureInfo.InvariantCulture), metric.PendingAtWarmup.ToString(CultureInfo.InvariantCulture),
                metric.PendingAtMeasurementEnd.ToString(CultureInfo.InvariantCulture), F(metric.OriginDelayFirstHalfMeanSeconds), F(metric.OriginDelayLastHalfMeanSeconds),
                string.Join(';', metric.MeasurementBucketCounts), metric.TotalDirectionEventCount.ToString(CultureInfo.InvariantCulture), metric.WaitingForResourceEvents.ToString(CultureInfo.InvariantCulture),
                metric.CollisionEvents.ToString(CultureInfo.InvariantCulture), metric.PredictedCollisionEvents.ToString(CultureInfo.InvariantCulture), metric.StationStopViolationEvents.ToString(CultureInfo.InvariantCulture),
                metric.SignalAuthorityViolationEvents.ToString(CultureInfo.InvariantCulture), F(metric.OriginDelayMeanSeconds), F(metric.OriginDelayMaxSeconds), F(metric.SimulationTimeSeconds),
                F(metric.WallSeconds), F(metric.AppliedGradientMinPermille), F(metric.AppliedGradientMaxPermille), metric.RuntimePhysicsFingerprint, metric.EngineSha256,
                metric.MeasurementPlanKey, metric.ProjectPath
            };
            lines.Add(string.Join(',', fields.Select(CsvQuote)));
        }
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string CsvQuote(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    private sealed record SelectedHandbookCase(
        string CaseId,
        string SourceFileName,
        string ModeTag,
        string Label,
        SpatialCapacityCalculationMode Mode);
}

public sealed record HandbookPointMetric(
    string CaseId,
    string SourceCaseFileName,
    string ModeTag,
    string TheoryMode,
    string SourcePointId,
    string SourceStationId,
    string MappedStationId,
    string Direction,
    double TheorySafetyHeadwaySeconds,
    double TheoryDesignHeadwaySeconds,
    int TheoryLineCapacityPerHourTruncated,
    double TheoryLineCapacityPerHourUntruncated,
    double TheoryLineCapacityPerHourReported,
    double InputHeadwaySeconds,
    int ActualEventCount,
    double ActualEventSpanSeconds,
    double ActualSpanRatePerHour,
    double ActualMedianHeadwaySeconds,
    double ActualP95HeadwaySeconds,
    double ActualMinimumHeadwaySeconds,
    int ActualArrivalCount,
    int ActualOriginEventCount,
    int PendingAtWarmup,
    int PendingAtMeasurementEnd,
    double OriginDelayFirstHalfMeanSeconds,
    double OriginDelayLastHalfMeanSeconds,
    int[] MeasurementBucketCounts,
    int TotalDirectionEventCount,
    int WaitingForResourceEvents,
    int CollisionEvents,
    int PredictedCollisionEvents,
    int StationStopViolationEvents,
    int SignalAuthorityViolationEvents,
    double OriginDelayMeanSeconds,
    double OriginDelayMaxSeconds,
    double SimulationTimeSeconds,
    double WallSeconds,
    double AppliedGradientMinPermille,
    double AppliedGradientMaxPermille,
    string RuntimePhysicsFingerprint,
    string EngineSha256,
    string ProjectPath,
    string MeasurementPlanKey,
    IReadOnlyList<string> RuntimeNotes)
{
    public double TheoryLineCapacityPerHour => TheoryLineCapacityPerHourReported;

    public int PendingAt1800 => PendingAtWarmup;

    public int PendingAt37800 => PendingAtMeasurementEnd;

    public bool MatchesIdentity(
        string caseId,
        string modeTag,
        string sourcePointId,
        string direction,
        string planKey,
        string engineSha256) =>
        CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)
        && ModeTag.Equals(modeTag, StringComparison.OrdinalIgnoreCase)
        && SourcePointId.Equals(sourcePointId, StringComparison.OrdinalIgnoreCase)
        && Direction.Equals(direction, StringComparison.OrdinalIgnoreCase)
        && MeasurementPlanKey.Equals(planKey, StringComparison.Ordinal)
        && EngineSha256.Equals(engineSha256, StringComparison.OrdinalIgnoreCase);
}

public sealed record HandbookPointRunSummary(
    string MeasurementPlanKey,
    string EngineSha256,
    int MetricCount,
    IReadOnlyList<HandbookPointMetric> Metrics,
    IReadOnlyList<string> ModeDirectories,
    string AggregateDirectory);

public sealed record HandbookPointSmokeResult(
    string CarrierRelativePath,
    IReadOnlyList<string> ValidatedDirections,
    IReadOnlyList<HandbookPointTheoryRow> TheoryRows,
    IReadOnlyList<string> Notes);

public sealed record HandbookPointTheoryRow(
    string CaseId,
    string ModeTag,
    string SourcePointId,
    string Direction,
    double SafetyHeadwaySeconds,
    double DesignHeadwaySeconds,
    int LineCapacityPerHour);

public sealed record SkippedHandbookPoint(string PointId, string Kind, string Reason);
