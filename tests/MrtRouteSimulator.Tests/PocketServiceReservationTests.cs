using MrtRouteSimulator.Engine;

internal static class PocketServiceReservationTests
{
    public static void TailAndPocketWaitWithoutReturnRouteLock()
    {
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json")));
        var graph = new InfrastructureGraphV4(document.Topology);
        var topology = new TopologySimulationDefinition(graph,
            document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
            document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP"));
        foreach (var (name, stopStation, stopMode, originPlatform, returnPlatform, stoppedEdge) in new[]
                 {
                     ("TAIL", "E", StationServiceMode.Stop, "P-W-D", "P-E-U", "TAIL-OUT"),
                     ("POCKET", "M", StationServiceMode.Turnback, "P-W-D", "P-M-U", "POCKET-OUT")
                 })
        {
            var vehicleId = $"{name}-WAIT-EMU";
            var dispatch = new ResolvedDispatchPlan(DispatchPlanningMode.ManualTimetable,
                VehicleAssignmentMode.ExplicitOnly, TimeSpan.Zero,
                [
                    new PlannedServiceRun($"{name}-DOWN", TimeSpan.Zero, TrainDirection.Outbound,
                        vehicleId, "DEFAULT_VEHICLE", "LOCAL", "TURN", originPlatform, 0,
                        continueAfterTerminal: true, continuationServiceRunId: $"{name}-UP"),
                    new PlannedServiceRun($"{name}-UP", TimeSpan.FromSeconds(600), TrainDirection.Inbound,
                        vehicleId, "DEFAULT_VEHICLE", "LOCAL", "NORMAL", returnPlatform, 1)
                ]);
            var world = new SimulationWorld(topology,
                new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
                OperationalParameters.CreateDefault(), 1,
                movingBlockMode: MovingBlockMode.Independent,
                servicePatterns:
                [
                    new ServicePattern("TURN", "折返", [new StationServiceInstruction(
                        stopStation, stopMode, DwellTimeSeconds: 0)]),
                    new ServicePattern("NORMAL", "正常上下客", [new StationServiceInstruction(
                        stopStation, StationServiceMode.Stop, DwellTimeSeconds: 25)])
                ],
                dispatchPlan: dispatch);

            var sawEntryRoute = false;
            while (world.CurrentTimeSeconds < 590 && !world.Events.Any(item =>
                       item.EventType == SimulationEventType.TailTrackReached
                       && item.VehicleId == vehicleId))
            {
                world.Tick();
                var entryLocks = world.GetActiveRouteLocks()
                    .Where(item => item.VehicleId == vehicleId).ToArray();
                if (!entryLocks.Any(item => item.TrackEdgeId == stoppedEdge
                        && item.Traversal.Direction == TraversalDirection.Forward)) continue;
                sawEntryRoute = true;
                False(entryLocks.Any(item => item.TrackEdgeId == stoppedEdge
                        && item.Traversal.Direction == TraversalDirection.Reverse
                        || item.TrackEdgeId == stoppedEdge + ":EXIT"),
                    $"{name} 駛入折返軌時，不得一併鎖定返程 traversal 或出口道岔。");
            }
            True(sawEntryRoute, $"{name} 駛入折返軌前應顯示入口進路。");
            True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                    && item.VehicleId == vehicleId && item.TrackEdgeId == stoppedEdge),
                $"{name} 必須先抵達實體折返停點。");
            world.AdvanceTo(Math.Min(590, world.CurrentTimeSeconds + 5));
            True(world.TopologyOccupancy.TryGetValue(vehicleId, out var footprint)
                    && footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == stoppedEdge),
                $"{name} 停等時仍須保留實體軌道占用。");
            False(world.GetActiveRouteLocks().Any(item => item.VehicleId == vehicleId),
                $"{name} 停等折返時不得顯示或維持返程進路鎖定。");
            world.AdvanceTo(590);
            False(world.GetActiveRouteLocks().Any(item => item.VehicleId == vehicleId),
                $"{name} 接續發車前的整段班表等待不得提前占用返程進路。");

            while (world.CurrentTimeSeconds < 610 && !world.Events.Any(item =>
                       item.EventType == SimulationEventType.TailTrackReturnStarted
                       && item.VehicleId == vehicleId)) world.Tick();
            var returnEvent = world.Events.FirstOrDefault(item =>
                item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.VehicleId == vehicleId);
            True(returnEvent is not null && returnEvent.SimulationTimeSeconds >= 600,
                $"{name} 必須在班表時間後發車。");
            True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReserved
                    && item.VehicleId == vehicleId
                    && item.SimulationTimeSeconds >= returnEvent!.SimulationTimeSeconds - 0.1
                    && item.ResourceIds?.Any(resource => resource.StartsWith(
                        "TOPOLOGY:SWITCH:", StringComparison.OrdinalIgnoreCase)) == true),
                $"{name} 返程道岔必須在發車時重新取得進路。");
            True(world.GetActiveRouteLocks().Any(item => item.VehicleId == vehicleId
                    && item.ResourceIds.Count > 0),
                $"{name} 發車時才應顯示已鎖定的返程進路。");
        }
    }

    public static void LargeSampleEveryTrainLocksTraversedSwitches()
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(
            Path.Combine("samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = CreateWorld(runtime);
        var infrastructure = world.TopologyInfrastructure;
        var traversalsByVehicle = new Dictionary<string, int>(StringComparer.Ordinal);
        var reservationTimes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var previous = world.GetSnapshot().Trains.ToDictionary(item => item.VehicleId, StringComparer.Ordinal);
        IReadOnlyList<LockedRouteSegment> previousLocks = world.GetActiveRouteLocks();
        while (!world.IsComplete && world.CurrentTimeSeconds < 8000)
        {
            var snapshot = world.Tick();
            var locks = world.GetActiveRouteLocks();
            foreach (var routeEvent in snapshot.NewEvents.Where(item =>
                         item.EventType == SimulationEventType.RouteReserved && item.ResourceIds is not null))
            foreach (var resourceId in routeEvent.ResourceIds!)
                reservationTimes[$"{routeEvent.VehicleId}|{resourceId}"] = routeEvent.SimulationTimeSeconds;
            foreach (var train in snapshot.Trains.Where(item => item.IsActive && item.TrackEdgeId is not null))
            {
                if (!previous.TryGetValue(train.VehicleId, out var before)
                    || !before.IsActive || before.TrackEdgeId is null
                    || before.TrackEdgeId.Equals(train.TrackEdgeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var connections = infrastructure.DirectedConnections.Where(item =>
                    item.FromTrackEdgeId.Equals(before.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                    && item.ToTrackEdgeId.Equals(train.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
                foreach (var connection in connections)
                {
                    var fromEdge = infrastructure.GetRequiredEdge(connection.FromTrackEdgeId);
                    var nodeId = connection.FromDirection == TraversalDirection.Forward
                        ? fromEdge.ToNodeId : fromEdge.FromNodeId;
                    var degree = infrastructure.Edges.Values.Count(edge =>
                        edge.FromNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase)
                        || edge.ToNodeId.Equals(nodeId, StringComparison.OrdinalIgnoreCase));
                    if (degree < 3) continue;
                    var resourceId = $"TOPOLOGY:SWITCH:{nodeId}";
                    True(previousLocks.Concat(locks).Any(item => item.VehicleId == train.VehicleId
                            && item.ResourceIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase)),
                        $"{train.VehicleId} 在 {snapshot.SimulationTimeSeconds:0.0}s 越過 {nodeId} "
                        + $"({before.TrackEdgeId} → {train.TrackEdgeId}) 前未取得道岔進路。");
                    if (before.SpeedMetersPerSecond > 10)
                        True(reservationTimes.TryGetValue($"{train.VehicleId}|{resourceId}", out var reservedAt)
                                && reservedAt <= snapshot.SimulationTimeSeconds - 1,
                            $"{train.VehicleId} 高速越過 {nodeId} 時進路開啟過晚。");
                    traversalsByVehicle[train.VehicleId] = traversalsByVehicle.GetValueOrDefault(train.VehicleId) + 1;
                    break;
                }
            }
            previous = snapshot.Trains.ToDictionary(item => item.VehicleId, StringComparer.Ordinal);
            previousLocks = locks;
        }

        True(world.IsComplete, "逐車道岔進路檢核必須涵蓋大存檔完整營運。");
        foreach (var vehicleId in runtime.DispatchPlan.Runs.Select(item => item.VehicleId)
                     .Where(item => item is not null).Select(item => item!)
                     .Distinct(StringComparer.Ordinal))
            True(traversalsByVehicle.GetValueOrDefault(vehicleId) > 0,
                $"{vehicleId} 沒有納入實體道岔通過檢核。");
        var passingEntries = world.Events.Where(item => item.EventType == SimulationEventType.OvertakeRequested
            && item.ResourceId == "FACILITY:PASS-001").ToArray();
        True(passingEntries.Length >= 2, "O04 應有多班越行車供提前鎖定進路檢核。");
        foreach (var entry in passingEntries)
        {
            True(entry.SpeedMetersPerSecond > 10,
                $"{entry.ServiceRunId} 在 O04 道岔入口被迫停車；應於行進中提前取得進路。");
            True(world.Events.Any(item => item.VehicleId == entry.VehicleId
                    && item.EventType == SimulationEventType.RouteReserved
                    && item.ResourceIds?.Contains("TOPOLOGY:SWITCH:NODE:SPLIT-001", StringComparer.OrdinalIgnoreCase) == true
                    && item.SimulationTimeSeconds < entry.SimulationTimeSeconds - 1
                    && item.SpeedMetersPerSecond > 10),
                $"{entry.ServiceRunId} 應在高速進入 O04 前先鎖定道岔。");
        }
    }

    public static void LargeSampleO13PassUsesThroughRouteFromFirstLock()
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            "samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var world = CreateWorld(TopologyProjectFormat.CreateRuntime(sample));
        const string vehicleId = "EXPRESS-01";
        const string throughEdgeId = "EDGE:DOWN:O12:O13:B-001";
        const string sidingEdgeId = "EDGE:PASS-003";
        const string facilityId = "FACILITY:PASS-003";
        var sawThroughLock = false;
        while (world.CurrentTimeSeconds < 4000 && !world.Events.Any(item =>
                   item.VehicleId == vehicleId && item.EventType == SimulationEventType.StationPassed
                   && item.ResourceId == facilityId))
        {
            world.Tick();
            var locks = world.GetActiveRouteLocks().Where(item =>
                item.VehicleId == vehicleId && item.ServiceRunId == "EXPRESS-DOWN-01").ToArray();
            False(locks.Any(item => item.TrackEdgeId == sidingEdgeId),
                $"{world.CurrentTimeSeconds:0.0}s O13 快速車先亮側線再切正線，進路選擇不一致。");
            sawThroughLock |= locks.Any(item => item.TrackEdgeId == throughEdgeId);
        }

        True(sawThroughLock, "O13 快速車須在入口前直接鎖定正線通過進路。");
        True(world.Events.Any(item => item.VehicleId == vehicleId
                && item.EventType == SimulationEventType.StationPassed
                && item.ResourceId == facilityId),
            "O13 快速車須完成正線實體通過。");
    }

    public static void LargeSamplePlannedPhysicsCompletesWithSwitchLocks()
    {
        var sample = TopologyProjectFormat.Deserialize(File.ReadAllText(
            Path.Combine("samples", "14-大型-二十八站完整營運範例.mrtsim.json")));
        var runtime = TopologyProjectFormat.CreateRuntime(sample);
        var world = new SimulationWorldOptions(null, runtime.TrainParameters, runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count, null, ProfileMode: OperationProfileMode.BasicPhysics,
            MovingBlockMode: MovingBlockMode.Independent,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        while (!world.IsComplete && world.CurrentTimeSeconds < 11035) world.Tick();
        True(world.IsComplete, "大存檔的計畫時間軸不應因道岔進路預約而停滯："
            + string.Join("；", world.GetSnapshot().Trains
                .Where(item => item.Phase != OperationalPhase.OutOfService)
                .Select(item => $"{item.VehicleId}:{item.CurrentStationId}->{item.NextStationId}:{item.TrackEdgeId}:{item.Phase}"))
            + "｜最近事件：" + string.Join("；", world.Events.TakeLast(12)
                .Select(item => $"{item.SimulationTimeSeconds:0.0}:{item.VehicleId}:{item.EventType}:{item.ResourceId}")));
    }

    public static void LargeSampleLocalEntryLocksSidingAndClearsBeforeWaiting()
    {
        var path = Path.Combine("samples", "14-大型-二十八站完整營運範例.mrtsim.json");
        var runtime = TopologyProjectFormat.CreateRuntime(
            TopologyProjectFormat.Deserialize(File.ReadAllText(path)));
        var world = CreateWorld(runtime);
        const string sidingEdgeId = "EDGE:PASS-001";
        const string entryResourceId = "TOPOLOGY:SWITCH:NODE:SPLIT-001";
        const string vehicleId = "FULL-O04";
        while (world.CurrentTimeSeconds < 1800)
        {
            world.Tick();
            var entry = world.GetActiveRouteLocks().FirstOrDefault(item =>
                item.VehicleId == vehicleId
                && item.ServiceRunId == "FULL-O04-DOWN"
                && item.TrackEdgeId == sidingEdgeId
                && item.ResourceIds.Contains(entryResourceId, StringComparer.OrdinalIgnoreCase));
            if (entry is null) continue;
            True(entry.PreviousTraversal is not null,
                "FULL-O04 進側線前，應顯示入口道岔的有向接軌進路。");
            break;
        }
        True(world.GetActiveRouteLocks().Any(item => item.VehicleId == vehicleId
            && item.TrackEdgeId == sidingEdgeId
            && item.ResourceIds.Contains(entryResourceId, StringComparer.OrdinalIgnoreCase)),
            "FULL-O04 等待道岔淨空後，必須取得進路才可駛入 O04 側線。");
        while (world.CurrentTimeSeconds < 1800)
        {
            var snapshot = world.Tick();
            if (!snapshot.NewEvents.Any(item => item.VehicleId == vehicleId
                    && item.EventType == SimulationEventType.Arrival && item.StationId == "O04"))
                continue;
            False(world.GetActiveRouteLocks().Any(item => item.VehicleId == vehicleId
                && item.ResourceIds.Contains(entryResourceId, StringComparer.OrdinalIgnoreCase)),
                "普通車進側線停妥後，入口道岔鎖定線應消失，不能延伸至待避期間。");
            return;
        }
        throw new InvalidOperationException("大存檔普通車未在時間上限內抵達 O04 側線。");
    }

    public static void PassingDefaultMainlineShowsReservedRouteWhileSidingWaits()
    {
        var runtime = TopologyProjectFormat.CreateRuntime(
            StationLayoutTemplateService.Build(StationLayoutTemplateKind.DoubleIslandFourTracks));
        var world = CreateWorld(runtime);
        while (world.CurrentTimeSeconds < 300)
        {
            world.Tick();
            var locked = world.GetActiveRouteLocks();
            var through = locked.FirstOrDefault(item => item.TrackEdgeId == "U1"
                && item.ResourceIds.Contains("PASS:U", StringComparer.OrdinalIgnoreCase));
            if (through is null) continue;

            Equal("EXP-UP", through.ServiceRunId,
                "快速車走道岔直向預設路徑時，也要顯示已鎖定的正線進路。");
            True(through.NextTraversal is { TrackEdgeId: "U0" },
                "匯合至出站正線的接軌方向應隨鎖定進路提供。");
            var next = through.NextTraversal!.Value;
            True(runtime.Topology.Infrastructure.DirectedConnections.Any(connection =>
                    connection.FromTrackEdgeId == "U1" && connection.ToTrackEdgeId == "U0"
                    && connection.FromDirection == through.Traversal.Direction
                    && connection.ToDirection == next.Direction),
                "進路標色須依實體有向接軌，不能以畫面上的道岔預設方向推測。");
            False(locked.Any(item => item.ServiceRunId == "UP-1"),
                "普通車在側線待避且沒有取得前方進路時，不可顯示鎖定線。");
            return;
        }

        throw new InvalidOperationException("快速車未在時間上限內取得側線待避站的正線進路。"
            + string.Join("；", world.Events.TakeLast(12).Select(item =>
                $"{item.SimulationTimeSeconds:0.0}:{item.VehicleId}:{item.EventType}:{item.ResourceId}")));
    }

    public static void CentralPocketServiceRoutesReserveWaitAndReleaseSafely()
    {
        var runtime = TopologyProjectFormat.CreateRuntime(CreateCentralPocketDocument());
        var world = CreateWorld(runtime);
        world.Tick();
        var waitingVehicleId = world.Events.Single(item => item.EventType == SimulationEventType.WaitingForResource
            && item.ResourceId == "POCKET").VehicleId;
        var initialLocks = world.GetActiveRouteLocks();
        True(initialLocks.Count > 0 && initialLocks.All(item => item.ResourceIds.Contains("POCKET")),
            "路線圖只能取得已成功預約的共用袋狀軌區段。");
        False(initialLocks.Any(item => item.VehicleId == waitingVehicleId),
            "等待進路的列車不可顯示鎖定線。");
        var firstRun = RunToCompletion(world);

        var departures = firstRun.Events.Where(item => item.EventType == SimulationEventType.Departure)
            .GroupBy(e => e.ServiceRunId).Select(g => g.First()).ToArray();
        Equal(2, departures.Length, "上下行服務車次都應完成發車。");
        var waits = firstRun.Events.Where(item => item.EventType == SimulationEventType.WaitingForResource
            && item.ResourceId == "POCKET").ToArray();
        Equal(1, waits.Length, "同時發車時應只有一方等待 POCKET 資源。");
        True(departures.Any(item => item.VehicleId == waits[0].VehicleId
            && item.SimulationTimeSeconds > waits[0].SimulationTimeSeconds),
            "等待 POCKET 的車次應在資源釋放後才發車。");
        Equal(0, firstRun.Events.Count(item => item.EventType == SimulationEventType.Collision), "不應發生碰撞。");
        Equal(0, firstRun.Events.Count(item => item.EventType == SimulationEventType.StationStopViolation), "不應發生停站違規。");
        True(world.IsComplete, "雙向服務車次都應完成。");
        Equal(0, world.GetActiveRouteLocks().Count, "進路釋放後不可殘留鎖定線。");

        var pocketRelease = firstRun.Events
            .Where(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("POCKET", StringComparer.OrdinalIgnoreCase) == true)
            .ToArray();
        Equal(2, pocketRelease.Length, "兩個方向都應在車尾淨空後釋放 POCKET 資源。");
        Equal(pocketRelease.Length, firstRun.ReleaseObservations.Count,
            "每次 POCKET RouteReleased 都應有對應的 topology footprint rear-clear 觀察。");
        var pocketEdges = world.TopologyInfrastructure.Edges.Values
            .Where(edge => edge.ConflictResourceIds.Contains("POCKET"))
            .Select(edge => edge.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in firstRun.ReleaseObservations)
        {
            True(observation.OccupiedIntervals.All(interval => !pocketEdges.Contains(interval.TrackEdgeId)),
                "POCKET RouteReleased 時所有現存 footprint 都必須已離開受保護 edge。");
        }

        var firstDepartureTime = departures.Min(item => item.SimulationTimeSeconds);
        var laterDeparture = departures.Max(item => item.SimulationTimeSeconds);
        var earlierRelease = pocketRelease.Min(item => item.SimulationTimeSeconds);
        True(laterDeparture >= earlierRelease, "等待車次的 Departure 必須晚於或等於前車 POCKET RouteReleased。");
        True(firstDepartureTime < laterDeparture, "同時到期的兩車次應有一方先發車、另一方延後。");

        world.Reset();
        var secondRun = RunToCompletion(world);
        Equal(firstRun.EventSignatures.Count, secondRun.EventSignatures.Count, "Reset 後事件數應可重現。");
        for (var index = 0; index < firstRun.EventSignatures.Count; index++)
        {
            Equal(firstRun.EventSignatures[index], secondRun.EventSignatures[index], "Reset 後事件順序與內容應可重現。");
        }
    }

    private static SimulationWorld CreateWorld(TopologyProjectRuntime runtime) => new SimulationWorldOptions(
        Route: null,
        TrainParameters: runtime.TrainParameters,
        OperationalParameters: runtime.OperationalParameters,
        TrainCount: runtime.DispatchPlan.Runs.Count,
        InitialDepartureIntervalSeconds: 1,
        MovingBlockMode: MovingBlockMode.Control,
        ServicePatterns: runtime.ServicePatterns,
        DispatchPlan: runtime.DispatchPlan,
        VehicleTypes: runtime.VehicleTypes,
        ServiceTypes: runtime.ServiceTypes,
        Topology: runtime.Topology).CreateWorld();

    private static RunResult RunToCompletion(SimulationWorld world)
    {
        var releaseObservations = new List<ReleaseObservation>();
        while (!world.IsComplete && world.CurrentTimeSeconds < 1200)
        {
            var snapshot = world.Tick();
            foreach (var item in snapshot.NewEvents.Where(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("POCKET", StringComparer.OrdinalIgnoreCase) == true))
            {
                releaseObservations.Add(new ReleaseObservation(
                    item.SimulationTimeSeconds,
                    world.TopologyOccupancy.Values
                        .SelectMany(footprint => footprint.OccupiedIntervals)
                        .ToArray()));
            }
        }

        True(world.IsComplete, "中央袋狀軌測試應在時間上限內完成。");
        return new RunResult(
            world.Events.ToArray(),
            world.Events.Select(EventSignature).ToArray(),
            releaseObservations);
    }

    private static TopologyProjectDocument CreateCentralPocketDocument()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        return source with
        {
            Dispatch = source.Dispatch with
            {
                ManualTimetableRows = source.Dispatch.ManualTimetableRows!
                    .Select(row => row with { PlannedDepartureTimeSeconds = 0 })
                    .ToArray()
            }
        };
    }

    private static string EventSignature(SimulationEvent item) => string.Join("|",
        item.SimulationTimeSeconds.ToString("R"), item.EventType, item.VehicleId, item.ServiceRunId,
        item.TrackEdgeId, item.PlatformId, item.ResourceId,
        string.Join(",", item.ResourceIds ?? []));

    private sealed record ReleaseObservation(double SimulationTimeSeconds, IReadOnlyList<TrackOccupancyInterval> OccupiedIntervals);

    private sealed record RunResult(
        IReadOnlyList<SimulationEvent> Events,
        IReadOnlyList<string> EventSignatures,
        IReadOnlyList<ReleaseObservation> ReleaseObservations);

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} 預期 {expected}，實際 {actual}。");
    }
}
