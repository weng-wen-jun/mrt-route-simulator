using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

internal static class NearestLeaderShadowTests
{
    private const string FullTopologySample = "11-小型-三站完整拓樸運行範例.mrtsim.json";
    private const string LargeFullSample = "14-大型-二十八站完整營運範例.mrtsim.json";
    private const double FullTopologyDurationSeconds = 3_000;
    private const double LargeFullDurationSeconds = 8_000;
    private const double RetainedSampleIntervalSeconds = 1;
    private static readonly JsonSerializerOptions HashJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static void FullTopologyShadowIsDeterministicAndMatchesOracle()
    {
        var document = LoadDocument(FullTopologySample);
        var fingerprints = new List<RunFingerprint>(capacity: 3);

        for (var run = 0; run < 3; run++)
        {
            var world = CreateWorld(document, shadow: true);
            world.AdvanceTo(FullTopologyDurationSeconds);

            AssertShadowDiagnostics(world, FullTopologyDurationSeconds, $"完整 topology 第 {run + 1} 次");
            AssertFullTopologyFacilityEvents(world);
            AssertNoUnsafeEvents(world, "完整 topology");
            True(world.IsComplete,
                "完整 topology 推進至 3,000 秒後所有車次都應完成並退出營運。 ");

            fingerprints.Add(CreateFingerprint(world));
        }

        var first = fingerprints[0];
        foreach (var fingerprint in fingerprints.Skip(1))
        {
            AssertSameOutput(first, fingerprint, "完整 topology shadow 重跑");
            Equal(first.FallbackHash, fingerprint.FallbackHash,
                "完整 topology shadow fallback 分布在 deterministic 重跑間必須一致。 ");
        }

        var oracleOnly = CreateWorld(document, shadow: false);
        oracleOnly.AdvanceTo(FullTopologyDurationSeconds);
        var oracleFingerprint = CreateFingerprint(oracleOnly);
        AssertSameOutput(first, oracleFingerprint, "完整 topology shadow 與 oracle-only");
        True(!oracleOnly.NearestLeaderShadowModeEnabled,
            "oracle-only control world 不得意外啟用 nearest-leader shadow。 ");
    }

    public static void LargeFullSampleShadowCompletesSafely()
    {
        var document = LoadDocument(LargeFullSample);
        var world = CreateWorld(document, shadow: true);
        world.AdvanceTo(LargeFullDurationSeconds);

        AssertShadowDiagnostics(world, LargeFullDurationSeconds, "大型 full sample");
        AssertLargeFullFacilityEvents(document, world);
        AssertNoUnsafeEvents(world, "大型 full sample");
        True(world.IsComplete,
            "大型 full sample 推進至 8,000 秒後所有車次都應完成並退出營運。 ");
        True(world.GetSnapshot().Trains.All(train => !train.IsActive),
            "大型 full sample 推進至 8,000 秒後不得殘留 active 列車。 ");
    }

    public static void BasicSingleAndDenseMixedLengthShadowParity()
    {
        var singleShadow = CreateBasicShadowWorld(dense: false, shadow: true);
        var singleOracle = CreateBasicShadowWorld(dense: false, shadow: false);
        singleShadow.AdvanceTo(600);
        singleOracle.AdvanceTo(600);
        AssertSameOutput(CreateFingerprint(singleOracle), CreateFingerprint(singleShadow),
            "基本三站單車 mixed-length shadow 與 oracle-only");
        AssertBasicShadowDiagnostics(singleShadow, requireIndexedSuccess: false, "基本三站單車");

        var denseShadow = CreateBasicShadowWorld(dense: true, shadow: true);
        var denseOracle = CreateBasicShadowWorld(dense: true, shadow: false);
        denseShadow.AdvanceTo(1200);
        denseOracle.AdvanceTo(1200);
        AssertSameOutput(CreateFingerprint(denseOracle), CreateFingerprint(denseShadow),
            "基本三站 dense mixed-length shadow 與 oracle-only");
        AssertBasicShadowDiagnostics(denseShadow, requireIndexedSuccess: true, "基本三站 dense");
        True(denseShadow.GetPerformanceDiagnostics().PeakActiveTrainCount >= 3,
            "基本三站 dense dispatch 必須同時涵蓋至少 3 列 active 列車。 ");
        // This is a bounded stress/parity fixture, not a calibrated completion scenario.
        // Its oracle also retains three active trains near S02 at 1200 s.
        Equal(8, denseShadow.Events.Where(item => item.EventType == SimulationEventType.Departure)
            .Select(item => item.VehicleId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "密集情境必須實際發出八列車，不能只驗證尚未啟用的排程。 ");
    }

    public static void ShadowResetRebuildsIndexAndPreservesParity()
    {
        var document = LoadDocument(FullTopologySample);
        var world = CreateWorld(document, shadow: true);

        world.AdvanceTo(180);
        world.Reset();
        world.ResetPerformanceDiagnostics();
        world.AdvanceTo(180);

        var control = CreateWorld(document, shadow: false);
        control.AdvanceTo(180);

        AssertShadowDiagnostics(world, 180, "Reset 後 shadow");
        AssertSameOutput(CreateFingerprint(control), CreateFingerprint(world),
            "Reset 後 shadow 與 oracle-only");
    }

    public static void IndexedOnlyMatchesOracleForFullAndLargeSamples()
    {
        foreach (var (sample, duration) in new[] { (FullTopologySample, 3000d), (LargeFullSample, 8000d) })
        {
            var document = LoadDocument(sample);
            var oracle = CreateWorld(document, false, fullRetention: true);
            var indexed = CreateWorld(document, false, indexedOnly: true, fullRetention: true);
            True(!oracle.BenchmarkIndexedLeaderLookupEnabled, "正常 world 預設必須使用 oracle。");
            oracle.AdvanceTo(duration);
            indexed.AdvanceTo(duration);
            AssertSameOutput(CreateFingerprint(oracle), CreateFingerprint(indexed), sample + " indexed-only full retention");
            AssertNoUnsafeEvents(indexed, sample);
            True(indexed.IsComplete, sample + " 必須完成運行。");
            var counters = indexed.GetPerformanceDiagnostics();
            True(counters.IndexedSuccessCount > 0 && counters.GraphDistanceCallCount > 0,
                "必須實際透過 index 與 graph narrow phase執行。");
            Equal(counters.ShadowLookupCount, counters.IndexedSuccessCount + counters.FullScanFallbackCount,
                "每次 indexed lookup 必須成功或保守 fallback。");
            Equal(counters.FullScanFallbackCount, indexed.NearestLeaderShadowFallbackReasons.Values.Sum(),
                "fallback 明細需一致。");
            if (sample == FullTopologySample) AssertFullTopologyFacilityEvents(indexed);
            else AssertLargeFullFacilityEvents(document, indexed);
        }
    }

    public static void IndexedOnlyMatchesDenseMixedLengthOracle()
    {
        foreach (var dense in new[] { false, true })
        {
            var oracle = CreateBasicShadowWorld(dense, false);
            var indexed = CreateBasicShadowWorld(dense, false);
            indexed.PerformanceDiagnosticsEnabled = true;
            indexed.BenchmarkIndexedLeaderLookupEnabled = true;
            indexed.ResetPerformanceDiagnostics();
            oracle.AdvanceTo(dense ? 1200 : 600);
            indexed.AdvanceTo(dense ? 1200 : 600);
            AssertSameOutput(CreateFingerprint(oracle), CreateFingerprint(indexed), "indexed-only basic/dense parity");
            AssertNoUnsafeEvents(indexed, "indexed-only basic/dense");
            True(indexed.GetPerformanceDiagnostics().IndexedSuccessCount > 0, "需使用 indexed lookup。");
            var rejected = false;
            try { indexed.NearestLeaderShadowModeEnabled = true; }
            catch (InvalidOperationException) { rejected = true; }
            True(rejected && indexed.BenchmarkIndexedLeaderLookupEnabled && !indexed.NearestLeaderShadowModeEnabled,
                "拒絕混用模式時不得污染既有 indexed-only 設定。");
            indexed.Reset();
            indexed.ResetPerformanceDiagnostics();
            oracle.Reset();
            indexed.AdvanceTo(180);
            oracle.AdvanceTo(180);
            AssertSameOutput(CreateFingerprint(oracle), CreateFingerprint(indexed), "indexed-only Reset parity");
            indexed.PerformanceDiagnosticsEnabled = false;
            indexed.AdvanceTo(210);
            oracle.AdvanceTo(210);
            AssertSameOutput(CreateFingerprint(oracle), CreateFingerprint(indexed), "indexed-only diagnostics 關閉 parity");
            indexed.BenchmarkIndexedLeaderLookupEnabled = false;
            indexed.NearestLeaderShadowModeEnabled = true;
            rejected = false;
            try { indexed.BenchmarkIndexedLeaderLookupEnabled = true; }
            catch (InvalidOperationException) { rejected = true; }
            True(rejected && indexed.NearestLeaderShadowModeEnabled && !indexed.BenchmarkIndexedLeaderLookupEnabled,
                "拒絕混用模式時不得污染既有 shadow 設定。");
            indexed.NearestLeaderShadowModeEnabled = false;
            indexed.AdvanceTo(240);
            oracle.AdvanceTo(240);
            AssertSameOutput(CreateFingerprint(oracle), CreateFingerprint(indexed), "indexed-only 關閉後 oracle parity");
        }
    }

    public static void ShadowToggleDuringRunPreservesParity()
    {
        var document = LoadDocument(FullTopologySample);
        var world = CreateWorld(document, shadow: false);
        world.PerformanceDiagnosticsEnabled = true;

        world.AdvanceTo(90);
        world.NearestLeaderShadowModeEnabled = true;
        world.ResetPerformanceDiagnostics();
        world.AdvanceTo(180);
        world.NearestLeaderShadowModeEnabled = false;
        world.AdvanceTo(270);
        world.NearestLeaderShadowModeEnabled = true;
        world.ResetPerformanceDiagnostics();
        world.AdvanceTo(360);

        var control = CreateWorld(document, shadow: false);
        control.AdvanceTo(360);

        AssertShadowDiagnostics(world, 90, "執行中 shadow toggle 最後一段");
        AssertSameOutput(CreateFingerprint(control), CreateFingerprint(world),
            "執行中 shadow toggle 與 oracle-only");
    }

    private static SimulationWorld CreateWorld(
        TopologyProjectDocument document,
        bool shadow,
        bool indexedOnly = false,
        bool fullRetention = false)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology,
            TraceRetentionPolicy: fullRetention ? SimulationTraceRetentionPolicy.Full
                : SimulationTraceRetentionPolicy.Decimated(RetainedSampleIntervalSeconds),
            SafetyObservationRetentionPolicy: fullRetention ? SafetyObservationRetentionPolicy.Full
                : SafetyObservationRetentionPolicy.Decimated(RetainedSampleIntervalSeconds)).CreateWorld();

        if (shadow || indexedOnly)
        {
            world.PerformanceDiagnosticsEnabled = true;
            world.NearestLeaderShadowModeEnabled = shadow;
            world.BenchmarkIndexedLeaderLookupEnabled = indexedOnly;
            world.ResetPerformanceDiagnostics();
        }

        return world;
    }

    private static SimulationWorld CreateBasicShadowWorld(bool dense, bool shadow)
    {
        var trainParameters = new TrainParameters(
            maxSpeedMetersPerSecond: 22.2222222,
            accelerationMetersPerSecondSquared: 1,
            decelerationMetersPerSecondSquared: 1,
            defaultDwellTimeSeconds: 0,
            originTurnaroundTimeSeconds: 0,
            terminalTurnaroundTimeSeconds: 0);
        var route = new Route(
            "SHADOW-BASIC",
            "nearest-leader shadow 三站測試線",
            [
                new Station("S01", "起點站", 0, 0),
                new Station("S02", "中央站", 1_000, 0),
                new Station("S03", "終點站", 2_000, 0)
            ]);
        var topology = TopologyProjectFactory.CreateLinearRuntimeTopology(route, trainParameters);
        var shortVehicle = new VehicleTypeDefinition(
            "SHADOW-SHORT",
            "shadow 短車",
            20,
            22.2222222,
            1,
            0.9,
            1.3,
            0.65,
            0.45,
            0);
        var longVehicle = new VehicleTypeDefinition(
            "SHADOW-LONG",
            "shadow 長車",
            80,
            22.2222222,
            1,
            0.9,
            1.3,
            0.65,
            0.45,
            0);
        var serviceType = new ServiceTypeDefinition(
            "SHADOW-LOCAL",
            "shadow mixed-length 普通車",
            "#4472C4",
            "SH",
            "SHADOW-ALL-STOP",
            "SHADOW-SHORT");
        var servicePattern = new ServicePattern(
            "SHADOW-ALL-STOP",
            "shadow 全停",
            [
                new StationServiceInstruction("S01", StationServiceMode.Stop),
                new StationServiceInstruction("S02", StationServiceMode.Stop),
                new StationServiceInstruction("S03", StationServiceMode.Stop)
            ]);
        var originPlatformId = topology.Infrastructure.Platforms.Values
            .Single(platform => platform.StationId == "S01"
                && platform.PlatformId.EndsWith(":DOWN", StringComparison.OrdinalIgnoreCase))
            .PlatformId;
        var runs = dense
            ? Enumerable.Range(0, 8)
                .Select(index => new PlannedServiceRun(
                    $"SHADOW-DENSE-{index + 1:00}",
                    TimeSpan.FromSeconds(index * 10),
                    TrainDirection.Outbound,
                    $"SHADOW-DENSE-VEH-{index + 1:00}",
                    index % 2 == 0 ? shortVehicle.Id : longVehicle.Id,
                    serviceType.Id,
                    servicePattern.PatternId,
                    originPlatformId,
                    index))
                .ToArray()
            : new[]
            {
                new PlannedServiceRun(
                "SHADOW-SINGLE-01",
                TimeSpan.Zero,
                TrainDirection.Outbound,
                "SHADOW-SINGLE-VEH-01",
                shortVehicle.Id,
                serviceType.Id,
                servicePattern.PatternId,
                originPlatformId,
                0)
            };
        var dispatch = new ResolvedDispatchPlan(
            DispatchPlanningMode.ManualTimetable,
            VehicleAssignmentMode.ExplicitOnly,
            TimeSpan.Zero,
            runs);
        var world = new SimulationWorldOptions(
            Route: null,
            TrainParameters: trainParameters,
            OperationalParameters: new OperationalParameters(
                jerkMetersPerSecondCubed: 0.65,
                coastingRatio: 0.15,
                approachDistanceMeters: 65,
                approachSpeedMetersPerSecond: 0,
                tractionFadeRatio: 0.45,
                trainLengthMeters: 80,
                serviceBrakingMetersPerSecondSquared: 0.9,
                emergencyBrakingMetersPerSecondSquared: 1.3,
                controlReactionTimeSeconds: 1.5,
                brakeBuildUpTimeSeconds: 0.8,
                positioningErrorMeters: 3,
                safetyMarginMeters: 25,
                absoluteMinimumGapMeters: 15),
            TrainCount: runs.Length,
            InitialDepartureIntervalSeconds: 10,
            ProfileMode: OperationProfileMode.RealisticOperations,
            MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: [servicePattern],
            DispatchPlan: dispatch,
            VehicleTypes: [shortVehicle, longVehicle],
            ServiceTypes: [serviceType],
            Topology: topology,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.Decimated(RetainedSampleIntervalSeconds),
            SafetyObservationRetentionPolicy: SafetyObservationRetentionPolicy.Decimated(
                RetainedSampleIntervalSeconds)).CreateWorld();

        if (shadow)
        {
            world.PerformanceDiagnosticsEnabled = true;
            world.NearestLeaderShadowModeEnabled = true;
            world.ResetPerformanceDiagnostics();
        }

        return world;
    }

    private static void AssertBasicShadowDiagnostics(
        SimulationWorld world,
        bool requireIndexedSuccess,
        string context)
    {
        var diagnostics = world.GetPerformanceDiagnostics();
        True(diagnostics.Enabled, $"{context} 必須啟用 performance diagnostics。 ");
        Equal(0L, diagnostics.ShadowMismatchCount,
            $"{context} candidate index 不得與 oracle 不一致。 ");
        Equal(0, world.NearestLeaderShadowMismatches.Count,
            $"{context} 不得留下 shadow mismatch detail。 ");
        True(diagnostics.ShadowLookupCount > 0 && diagnostics.IndexedSuccessCount > 0,
            $"{context} 必須實際完成 indexed shadow lookup。 ");
        if (requireIndexedSuccess)
        {
            True(diagnostics.CandidatePairCount > 0
                && diagnostics.ShadowIndexedCandidateCountSum > 0,
                $"{context} dense dispatch 必須涵蓋 full-scan candidate pair 與 indexed candidate。 ");
        }
        else
        {
            Equal(0L, diagnostics.CandidatePairCount,
                $"{context} 單車不應產生 full-scan candidate pair。 ");
            Equal(0L, diagnostics.ShadowIndexedCandidateCountSum,
                $"{context} 單車沒有前車，indexed candidate 數應為零。 ");
        }

        Equal(diagnostics.FullScanFallbackCount,
            world.NearestLeaderShadowFallbackReasons.Values.Sum(),
            $"{context} fallback counter 必須與 reason 分布一致。 ");
        True(!world.Events.Any(item => item.EventType is SimulationEventType.Collision
                or SimulationEventType.StationStopViolation),
            $"{context} 不得發生 Collision 或 StationStopViolation。 ");
    }

    private static void AssertShadowDiagnostics(
        SimulationWorld world,
        double expectedDurationSeconds,
        string context)
    {
        var diagnostics = world.GetPerformanceDiagnostics();
        True(diagnostics.Enabled, $"{context} 必須啟用 performance diagnostics。 ");
        True(world.NearestLeaderShadowModeEnabled,
            $"{context} 必須啟用 nearest-leader shadow。 ");
        Equal((long)Math.Round(expectedDurationSeconds / SimulationWorld.FixedTimeStepSeconds),
            diagnostics.TickCount,
            $"{context} 必須依固定 0.1 秒完整推進。 ");
        True(diagnostics.ShadowLookupCount > 0,
            $"{context} 必須實際執行 nearest-leader shadow lookup。 ");
        True(diagnostics.IndexedSuccessCount > 0,
            $"{context} 必須至少有一次 candidate-index lookup 成功。 ");
        Equal(0L, diagnostics.ShadowMismatchCount,
            $"{context} candidate index 不得與 full-scan oracle 不一致。 ");
        Equal(0, world.NearestLeaderShadowMismatches.Count,
            $"{context} 不得留下 shadow mismatch detail。 ");

        var fallbackCount = world.NearestLeaderShadowFallbackReasons.Values.Sum();
        Equal(diagnostics.FullScanFallbackCount, fallbackCount,
            $"{context} fallback counter 必須與 fallback reason 分布一致。 ");
        Equal(diagnostics.ShadowLookupCount,
            diagnostics.IndexedSuccessCount + diagnostics.FullScanFallbackCount,
            $"{context} 每次 shadow lookup 必須是 indexed success 或明確 fallback。 ");
        if (diagnostics.FullScanFallbackCount > 0)
        {
            True(world.NearestLeaderShadowFallbackReasons.Count > 0,
                $"{context} 發生 fallback 時必須留下可診斷原因。 ");
        }
    }

    private static void AssertFullTopologyFacilityEvents(SimulationWorld world)
    {
        var pocketEvents = world.Events.Where(item => item.VehicleId == "POCKET-02").ToArray();
        True(pocketEvents.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "POCKET-M")
            && pocketEvents.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId == "POCKET-M"),
            "完整 topology 必須實際完成 POCKET-02 袋狀軌抵達與返回。 ");
        True(world.Trajectory.Any(item => item.VehicleId == "POCKET-02"
                && item.TrackEdgeId == "UP-M-W"),
            "完整 topology 袋狀軌折返後必須進入上行實體 edge。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.OvertakeCompleted
                && item.ServiceRunId == "EXPRESS-DOWN"
                && item.ResourceId == "FAC-PASS-M"),
            "完整 topology 必須實際完成 PASS-M 越行。 ");

        foreach (var edgeId in new[] { "E-TAIL", "W-TAIL" })
        {
            True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                    && item.TrackEdgeId == edgeId),
                $"完整 topology 必須實際抵達 {edgeId} 尾軌。 ");
            True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                    && item.TrackEdgeId == edgeId),
                $"完整 topology 必須由 {edgeId} 尾軌實際返回。 ");
        }

        foreach (var resourceId in new[] { "RES-PASS-M", "RES-POCKET-M", "RES-E-TAIL", "RES-W-TAIL" })
        {
            True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                    && item.ResourceIds?.Contains(resourceId, StringComparer.OrdinalIgnoreCase) == true),
                $"完整 topology {resourceId} 必須在車尾淨空後留下釋放事件。 ");
        }
    }

    private static void AssertLargeFullFacilityEvents(
        TopologyProjectDocument document,
        SimulationWorld world)
    {
        foreach (var stationId in new[] { "O04", "O13" })
        {
            var facilityIds = document.Topology.PassingFacilities
                .Where(facility => facility.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))
                .Select(facility => facility.FacilityId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            True(facilityIds.Count > 0, $"大型 full sample {stationId} 必須有 passing facility。 ");
            True(world.Events.Any(item => item.EventType == SimulationEventType.OvertakeRequested
                    && item.ResourceId is not null
                    && facilityIds.Contains(item.ResourceId)),
                $"大型 full sample 必須在 {stationId} 實際提出 passing。 ");
            True(world.Events.Any(item => item.EventType == SimulationEventType.OvertakeCompleted
                    && item.ResourceId is not null
                    && facilityIds.Contains(item.ResourceId)),
                $"大型 full sample 必須在 {stationId} 實際完成 passing。 ");
            True(world.Trajectory.Any(item => item.TrackEdgeId is not null
                    && document.Topology.PassingFacilities
                        .Where(facility => facility.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(facility => facility.Traversals)
                        .Any(traversal => traversal.TrackEdgeId.Equals(
                            item.TrackEdgeId, StringComparison.OrdinalIgnoreCase))),
                $"大型 full sample 必須留下 {stationId} passing traversal 的實體軌跡。 ");
        }

        var pocket = document.Topology.TurnbackFacilities.Single(facility =>
            facility.Kind == TurnbackFacilityKind.PocketTrack);
        var turnbackStopEdge = pocket.TurnbackStopPosition?.TrackEdgeId
            ?? throw new InvalidOperationException("大型 full sample O20 pocket 缺少 edge-local turnback stop。 ");
        var pocketOperationIds = document.Topology.TurnbackOperations
            .Where(operation => operation.FacilityId.Equals(pocket.FacilityId, StringComparison.OrdinalIgnoreCase))
            .Select(operation => operation.OperationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pocketStationId = document.Topology.StationOperations
            .Where(operation => operation.TurnbackOperationIds.Any(pocketOperationIds.Contains))
            .Select(operation => operation.StationId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SingleOrDefault()
            ?? throw new InvalidOperationException("大型 full sample O20 pocket 缺少對應 station operation。 ");

        // Do not couple this oracle-parity gate to the old Taichung/Airport vehicle
        // identifiers.  The de-identified full sample keeps the same physical O20
        // turnback contract but its dispatch IDs are now synthetic (for example,
        // EXPRESS-01).  Derive the expected physical vehicles from the document's
        // turnback stop patterns and initial origins, excluding continuation rows
        // that start at O20 after the physical reversal.
        var pocketTurnbackPatternIds = document.StopPatterns
            .Where(pattern => pattern.Instructions.Any(instruction =>
                instruction.StationId.Equals(pocketStationId, StringComparison.OrdinalIgnoreCase)
                && instruction.Action == StopPatternAction.Turnback))
            .Select(pattern => pattern.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var platformStations = document.Topology.Platforms
            .ToDictionary(platform => platform.PlatformId, platform => platform.StationId,
                StringComparer.OrdinalIgnoreCase);
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var expectedVehicleIds = runtime.DispatchPlan.Runs
            .Where(run => pocketTurnbackPatternIds.Contains(run.StopPatternId))
            .Where(run => run.OriginPlatformId is { } originPlatformId
                && platformStations.TryGetValue(originPlatformId, out var originStation)
                && !originStation.Equals(pocketStationId, StringComparison.OrdinalIgnoreCase))
            .Select(run => run.VehicleId)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        True(expectedVehicleIds.Length == 2,
            "大型 full sample O20 pocket 必須保留兩輛起點不在 O20 的折返車，不能因車次改名縮減情境覆蓋。 ");

        foreach (var vehicleId in expectedVehicleIds)
        {
            var vehicleEvents = world.Events.Where(item => item.VehicleId == vehicleId).ToArray();
            True(vehicleEvents.Any(item => item.EventType == SimulationEventType.TailTrackReached
                    && item.TrackEdgeId == turnbackStopEdge),
                $"大型 full sample {vehicleId} 必須實際抵達 O20 pocket。 ");
            True(vehicleEvents.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                    && item.TrackEdgeId == turnbackStopEdge),
                $"大型 full sample {vehicleId} 必須由 O20 pocket 實際返回。 ");
            True(vehicleEvents.Any(item => item.EventType == SimulationEventType.DirectionChanged),
                $"大型 full sample {vehicleId} 必須留下 O20 實體換端事件。 ");
        }
    }

    private static void AssertNoUnsafeEvents(SimulationWorld world, string context)
    {
        var unsafeEvents = world.Events
            .Where(item => item.EventType is SimulationEventType.Collision
                or SimulationEventType.StationStopViolation)
            .Take(5)
            .Select(item => $"{item.SimulationTimeSeconds:0.0}s/{item.EventType}/{item.ServiceRunId}")
            .ToArray();
        True(unsafeEvents.Length == 0,
            $"{context} 不得發生 Collision 或 StationStopViolation：{string.Join("；", unsafeEvents)}");
    }

    private static RunFingerprint CreateFingerprint(SimulationWorld world) => new(
        world.Events.Count,
        HashSequence(world.Events),
        world.SafetyHistory.Count,
        HashSequence(world.SafetyHistory),
        world.Trajectory.Count,
        HashSequence(world.Trajectory),
        HashFallbackReasons(world.NearestLeaderShadowFallbackReasons),
        world.IsComplete,
        HashSequence(world.GetSnapshot().Trains));

    private static void AssertSameOutput(
        RunFingerprint expected,
        RunFingerprint actual,
        string context)
    {
        Equal(expected.EventCount, actual.EventCount, $"{context} event count 必須一致。 ");
        Equal(expected.EventHash, actual.EventHash, $"{context} event sequence hash 必須一致。 ");
        Equal(expected.SafetyCount, actual.SafetyCount, $"{context} safety count 必須一致。 ");
        Equal(expected.SafetyHash, actual.SafetyHash, $"{context} safety sequence hash 必須一致。 ");
        Equal(expected.TrajectoryCount, actual.TrajectoryCount,
            $"{context} trajectory count 必須一致。 ");
        Equal(expected.TrajectoryHash, actual.TrajectoryHash,
            $"{context} trajectory hash 必須一致。 ");
        Equal(expected.IsComplete, actual.IsComplete, $"{context} completion 狀態必須一致。 ");
        Equal(expected.FinalTrainHash, actual.FinalTrainHash,
            $"{context} final train state hash 必須一致。 ");
    }

    private static string HashFallbackReasons(IReadOnlyDictionary<string, long> reasons) =>
        HashSequence(reasons
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new FallbackReason(item.Key, item.Value)));

    private static string HashSequence<T>(IEnumerable<T> sequence)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0;
        Span<byte> length = stackalloc byte[4];
        foreach (var item in sequence)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(item, HashJsonOptions);
            BinaryPrimitives.WriteInt32LittleEndian(length, json.Length);
            hash.AppendData(length);
            hash.AppendData(json);
            count++;
        }

        Span<byte> itemCount = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(itemCount, count);
        hash.AppendData(itemCount);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static TopologyProjectDocument LoadDocument(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", fileName);
        True(File.Exists(path), $"找不到 sample：{fileName}。 ");
        return TopologyProjectFormat.Deserialize(File.ReadAllText(path));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到 repository root。 ");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} expected={expected}; actual={actual}");
        }
    }

    private sealed record RunFingerprint(
        int EventCount,
        string EventHash,
        int SafetyCount,
        string SafetyHash,
        int TrajectoryCount,
        string TrajectoryHash,
        string FallbackHash,
        bool IsComplete,
        string FinalTrainHash);

    private sealed record FallbackReason(string Reason, long Count);
}
