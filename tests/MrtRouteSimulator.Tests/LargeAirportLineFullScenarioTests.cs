using MrtRouteSimulator.Engine;

internal static class LargeAirportLineFullScenarioTests
{
    private static readonly Lazy<LargeAirportLineScenarioStages> Stages = new(
        LargeAirportLineFullScenarioBuilder.BuildStages);
    private static readonly Lazy<ScenarioExecution> Execution = new(RunFullScenario);

    public static void PrintKeyEventReport()
    {
        foreach (var item in Execution.Value.World.Events.Where(item => item.EventType is
                     SimulationEventType.OvertakeRequested
                     or SimulationEventType.OvertakeCompleted
                     or SimulationEventType.TailTrackReached
                     or SimulationEventType.TailTrackReturnStarted
                     or SimulationEventType.DirectionChanged
                     or SimulationEventType.ServiceEnded))
        {
            Console.WriteLine(
                $"{item.SimulationTimeSeconds:0.0}s\t{item.EventType}\t{item.ServiceRunId}\t{item.VehicleId}\t{item.StationId}\t{item.ResourceId}");
        }
    }

    public static void FullStationChainValidates()
    {
        var stages = Stages.Value;
        var document = stages.FullStationChain;
        Equal(28, document.Topology.Stations.Count, "完整站鏈必須有 28 個邏輯車站。 ");
        SequenceEqual(LargeAirportLineFullScenarioBuilder.StationIds,
            document.Topology.Stations.Select(station => station.StationId),
            "站序必須包含 O01～O26、O08a 與 O15a。 ");
        Equal(56, document.Topology.Platforms.Count, "每站必須各有上下行月台。 ");
        Equal(54, document.Topology.Edges.Count, "28 站雙線主線必須有 54 條實體 edge。 ");
        True(document.Topology.Edges.All(edge => edge.FromPortSide is not null && edge.ToPortSide is not null),
            "完整站鏈每條主線 edge 都必須明列 physical port sides。 ");
        Equal(52, document.Topology.DirectedConnections.Count,
            "上下行各 27 個 traversal 應各有 26 個明確有向接續。 ");
        Equal(27, document.ServiceRoutes.Single(route => route.ServiceRouteId == LargeAirportLineFullScenarioBuilder.DownRouteId).Traversals.Count,
            "下行 ServiceRoute 必須涵蓋完整站鏈。 ");
        Equal(27, document.ServiceRoutes.Single(route => route.ServiceRouteId == LargeAirportLineFullScenarioBuilder.UpRouteId).Traversals.Count,
            "上行 ServiceRoute 必須涵蓋完整站鏈。 ");
        Equal(LargeAirportLineFullScenarioBuilder.DownRouteId,
            document.DirectionRouteBindings.Single(binding => binding.Direction == TrainDirection.Outbound).ServiceRouteId,
            "下行 directionRouteBinding 必須指向完整站鏈。 ");
        Equal(LargeAirportLineFullScenarioBuilder.UpRouteId,
            document.DirectionRouteBindings.Single(binding => binding.Direction == TrainDirection.Inbound).ServiceRouteId,
            "上行 directionRouteBinding 必須指向完整站鏈。 ");
        Equal(7, stages.StageValidations.Count,
            "minimal、station chain、service、turnback、兩個 passing 與 timetable 都必須留下 gate 結果。 ");
        True(stages.StageValidations.All(validation => validation.StructuralPassed && validation.OperationalPassed),
            "每個 LargeAirportLine builder stage 都必須通過 Structural 與 Operational gate。 ");
        TopologyProjectFormat.Validate(document);
    }

    public static void FullSampleStopsUsePlatformCenters()
    {
        var document = Stages.Value.FullScenario;
        foreach (var platform in document.Topology.Platforms)
        {
            Equal(StopPositionReference.TrainCenter, platform.StopPositionReference,
                $"大型 full sample 月台 {platform.PlatformId} 必須以車體中心定位。 ");
            Close((platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2,
                platform.StopPositionOffsetMeters,
                0.001,
                $"大型 full sample 月台 {platform.PlatformId} 的停點必須是月台中心。 ");
        }
    }

    public static void FullSampleDwellCentersMatchPlatformCenters()
    {
        var execution = Execution.Value;
        var graph = new InfrastructureGraphV4(execution.Document.Topology);
        var routes = execution.Document.ServiceRoutes.ToDictionary(
            route => route.ServiceRouteId,
            StringComparer.OrdinalIgnoreCase);
        var samples = execution.World.Trajectory
            .Where(sample => sample.Phase == OperationalPhase.Dwelling
                && sample.PlatformId is not null
                && sample.TrackEdgeId is not null
                && sample.OffsetMeters is not null
                && sample.ServiceRouteTraversalIndex is not null
                && sample.ServiceRunId is "FULL-O04-DOWN" or "FULL-UP-01")
            .GroupBy(sample => (sample.ServiceRunId, sample.PlatformId))
            .Select(group => group.First())
            .ToArray();

        True(samples.Length >= LargeAirportLineFullScenarioBuilder.StationIds.Length * 2 - 2,
            "上下行全程車的停站軌跡必須留下足夠的中心停車樣本。 ");
        var halfTrainLength = execution.Document.Operations.TrainLengthMeters / 2;
        foreach (var sample in samples)
        {
            var platform = execution.Document.Topology.Platforms.Single(item => item.PlatformId == sample.PlatformId);
            var route = routes[sample.Direction == TrainDirection.Outbound
                ? LargeAirportLineFullScenarioBuilder.DownRouteId
                : LargeAirportLineFullScenarioBuilder.UpRouteId];
            var navigator = new TopologyRouteNavigator(graph, route);
            var head = new TopologyTraversalCursor(
                navigator.ServiceRouteId,
                sample.ServiceRouteTraversalIndex!.Value,
                new TrackPosition(sample.TrackEdgeId!, sample.OffsetMeters!.Value));
            var center = navigator.Retreat(head, halfTrainLength);
            var actualChainage = RouteChainage(graph, navigator, route, center);
            var platformTraversalIndex = route.Traversals
                .Select((traversal, index) => (traversal, index))
                .Single(item => item.traversal.TrackEdgeId.Equals(platform.TrackEdgeId,
                    StringComparison.OrdinalIgnoreCase))
                .index;
            var expectedCenter = navigator.CreateCursor(
                platformTraversalIndex,
                platform.PlatformStartOffsetMeters
                    + (platform.PlatformEndOffsetMeters - platform.PlatformStartOffsetMeters) / 2);
            var expectedChainage = RouteChainage(graph, navigator, route, expectedCenter);
            Close(expectedChainage, actualChainage, 0.001,
                $"{sample.ServiceRunId} 在 {platform.PlatformId} 停站時，車體中心必須落在月臺中心。 ");
        }
    }

    private static double RouteChainage(
        InfrastructureGraphV4 graph,
        TopologyRouteNavigator navigator,
        ServiceRouteDefinition route,
        TopologyTraversalCursor cursor)
    {
        var chainage = 0d;
        for (var index = 0; index < cursor.TraversalIndex; index++)
            chainage += graph.GetRequiredEdge(route.Traversals[index].TrackEdgeId).LengthMeters;

        return chainage + navigator.GetDistanceAlongTraversal(cursor);
    }

    public static void FullLineAllStopCompletes()
    {
        var execution = Execution.Value;
        var events = execution.World.Events.Where(item => item.ServiceRunId == "FULL-O04-DOWN").ToArray();
        var arrivals = events.Where(item => item.EventType == SimulationEventType.Arrival)
            .Select(item => item.StationId)
            .Where(stationId => stationId is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        True(events.Any(item => item.EventType == SimulationEventType.Departure && item.StationId == "O01")
            && LargeAirportLineFullScenarioBuilder.StationIds.Skip(1).All(arrivals.Contains),
            "O01 發車的全程車必須實際完成 28 站全停（起點以 Departure、其餘以 Arrival 證明）。 ");
        True(events.Any(item => item.EventType == SimulationEventType.ServiceEnded),
            "全程車必須完成 O26 並退出營運。 ");
    }

    public static void RouteLengthsDeriveFromSourceBackedChainages()
    {
        var document = Stages.Value.FullStationChain;
        var route = document.ServiceRoutes.Single(item =>
            item.ServiceRouteId == LargeAirportLineFullScenarioBuilder.DownRouteId);
        var edgeLengths = document.Topology.Edges.ToDictionary(
            item => item.TrackEdgeId,
            item => item.LengthMeters,
            StringComparer.OrdinalIgnoreCase);
        var projection = new StationChainageProjection(document);
        foreach (var stationId in LargeAirportLineFullScenarioBuilder.StationIds)
            Close(LargeAirportLineFullScenarioBuilder.SourceChainage(stationId), projection.StationCenters[stationId], 0.001,
                $"{stationId} 的 station-center projected chainage 必須等於 source-backed chainage。 ");
        var cumulativeChainage = LargeAirportLineFullScenarioBuilder.SourceChainage("O01");
        for (var index = 1; index < LargeAirportLineFullScenarioBuilder.StationIds.Length; index++)
        {
            var previous = LargeAirportLineFullScenarioBuilder.StationIds[index - 1];
            var stationId = LargeAirportLineFullScenarioBuilder.StationIds[index];
            var expectedDistance = LargeAirportLineFullScenarioBuilder.SourceChainage(stationId)
                - LargeAirportLineFullScenarioBuilder.SourceChainage(previous);
            var actualDistance = edgeLengths[route.Traversals[index - 1].TrackEdgeId];
            Close(expectedDistance, actualDistance, 0.001,
                $"{previous}→{stationId} 必須由相鄰 source-backed chainage 相減產生。 ");
            cumulativeChainage += actualDistance;
            Close(LargeAirportLineFullScenarioBuilder.SourceChainage(stationId), cumulativeChainage, 0.001,
                $"{stationId} 的主線投影中心必須對齊 source-backed chainage。 ");
        }

        var fullLength = route.Traversals.Sum(item => edgeLengths[item.TrackEdgeId]);
        var o20TraversalCount = Array.IndexOf(LargeAirportLineFullScenarioBuilder.StationIds, "O20");
        var o20Length = route.Traversals.Take(o20TraversalCount).Sum(item => edgeLengths[item.TrackEdgeId]);
        Close(LargeAirportLineFullScenarioBuilder.FullRouteLengthMeters, fullLength, 0.001,
            "O01-O26 必須等於 30,133m - 190m = 29,943m。 ");
        Close(LargeAirportLineFullScenarioBuilder.AirportSectionLengthMeters, o20Length, 0.001,
            "O01-O20 必須等於 24,023m - 190m = 23,833m。 ");
    }

    public static void FullStationChainBidirectionalAllStopCompletes()
    {
        var document = Stages.Value.FullStationChain;
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
            Topology: runtime.Topology).CreateWorld();
        world.AdvanceTo(8_000);

        AssertAllStopJourney(world, "MIN-DOWN-01", LargeAirportLineFullScenarioBuilder.StationIds);
        AssertAllStopJourney(world, "MIN-UP-01", LargeAirportLineFullScenarioBuilder.StationIds.Reverse().ToArray());
        True(world.Events.All(item => item.EventType is not (SimulationEventType.Collision or SimulationEventType.StationStopViolation)),
            "Full Station Chain 雙向全停 smoke test 不得發生 Collision 或 StationStopViolation。 ");
        True(world.GetSnapshot().Trains.All(train => !train.IsActive),
            "Full Station Chain 雙向全停列車必須都正常退出營運。 ");
    }

    public static void AirportDirectSkipStopWorks()
    {
        var execution = Execution.Value;
        var events = execution.World.Events
            .Where(item => item.ServiceRunId == LargeAirportLineFullScenarioBuilder.AirportDirectRunId)
            .ToArray();
        var stopped = events.Where(item => item.EventType == SimulationEventType.Arrival)
            .Select(item => item.StationId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var passed = events.Where(item => item.EventType == SimulationEventType.StationPassed)
            .Select(item => item.StationId)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        True(events.Any(item => item.EventType == SimulationEventType.Departure && item.StationId == "O01")
            && LargeAirportLineFullScenarioBuilder.AirportDirectStops.Where(id => id != "O01").All(stopped.Contains),
            "機場直達車必須由 O01 發車並停靠 O08/O11/O16/O20。 ");
        True(new[] { "O02", "O03", "O04", "O05", "O08a", "O15a", "O19" }.All(passed.Contains),
            "機場直達車必須以 StationPassed 事件證明 O01-O20 中間站 skip-stop。 ");
        var passInstructions = execution.Document.StopPatterns
            .Single(item => item.Id == LargeAirportLineFullScenarioBuilder.AirportDirectPatternId)
            .Instructions
            .Where(item => item.Action == StopPatternAction.Pass)
            .ToArray();
        True(passInstructions.Length > 0
            && passInstructions.All(item => item.PassingSpeedLimitMetersPerSecond is null),
            "大型機場線高等列車的 skip-stop 指令不應暗含 60 km/h 通過速限。 ");
        var nonFacilityPasses = events
            .Where(item => item.EventType == SimulationEventType.StationPassed
                && item.StationId is "O02" or "O03" or "O05" or "O08a" or "O15a" or "O19")
            .ToArray();
        True(nonFacilityPasses.Length > 0
            && nonFacilityPasses.All(item => item.SpeedMetersPerSecond * 3.6 > 70),
            "未設定通過速限的高等列車應以主線／車型速限通過，不得被示範資料暗中壓到 60 km/h。 ");
        True(!passed.Overlaps(LargeAirportLineFullScenarioBuilder.AirportDirectStops),
            "直達停靠站不可同時記為跨站。 ");
        True(events.All(item => item.StationId is not ("O21" or "O22" or "O23" or "O24" or "O25" or "O26")),
            "AIRPORT-DIRECT 的下行與折返上行事件都不得觸及 O21-O26。 ");
    }

    public static void O20TurnbackCompletes()
    {
        var execution = Execution.Value;
        var facility = execution.Document.Topology.TurnbackFacilities.Single();
        Equal(TurnbackFacilityKind.PocketTrack, facility.Kind, "O20 必須使用 topology-native pocket facility。 ");
        var turnbackStop = facility.TurnbackStopPosition
            ?? throw new InvalidOperationException("O20 折返缺少 edge-local turnbackStopPosition。 ");
        True(facility.Traversals.Any(traversal => traversal.TrackEdgeId == turnbackStop.TrackEdgeId),
            "O20 折返必須使用 facility traversal 上的 edge-local turnbackStopPosition。 ");

        AssertO20Turnback(
            execution,
            facility,
            "SECTION-VEHICLE-01",
            LargeAirportLineFullScenarioBuilder.SectionDownRunId,
            LargeAirportLineFullScenarioBuilder.SectionUpRunId,
            "區間車");
    }

    public static void AirportDirectTurnsAtO20AndReturns()
    {
        var execution = Execution.Value;
        var facility = execution.Document.Topology.TurnbackFacilities.Single();
        AssertO20Turnback(
            execution,
            facility,
            "AIRPORT-DIRECT-01",
            LargeAirportLineFullScenarioBuilder.AirportDirectRunId,
            LargeAirportLineFullScenarioBuilder.AirportDirectUpRunId,
            "機場直達車");
        var events = execution.World.Events.Where(item => item.VehicleId == "AIRPORT-DIRECT-01").ToArray();
        True(events.All(item => item.StationId is not ("O21" or "O22" or "O23" or "O24" or "O25" or "O26")),
            "AIRPORT-DIRECT 折返前後都不得觸及 O21-O26。 ");
    }

    private static void AssertO20Turnback(
        ScenarioExecution execution,
        TurnbackFacilityDefinition facility,
        string vehicleId,
        string downRunId,
        string upRunId,
        string serviceName)
    {
        var turnbackStop = facility.TurnbackStopPosition
            ?? throw new InvalidOperationException("O20 折返缺少 edge-local turnbackStopPosition。 ");
        var events = execution.World.Events.Where(item => item.VehicleId == vehicleId).ToArray();
        True(events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == turnbackStop.TrackEdgeId),
            $"{serviceName}必須實際進入 O20 pocket 並抵達折返停點。 ");
        True(events.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId == turnbackStop.TrackEdgeId),
            $"{serviceName}必須由同一實體 pocket edge 換端返回。 ");
        var changed = events.Single(item => item.EventType == SimulationEventType.DirectionChanged);
        Equal(upRunId, changed.ServiceRunId,
            "O20 換端後必須切換到明列的上行接續車次。 ");
        True(events.Any(item => item.ServiceRunId == downRunId)
            && events.Any(item => item.ServiceRunId == upRunId),
            "折返前後必須保留同一 VehicleId 並切換 ServiceRunId。 ");
        True(execution.World.Trajectory.Any(item => item.VehicleId == vehicleId
                && item.ServiceRunId == upRunId
                && item.Direction == TrainDirection.Inbound),
            "O20 折返後必須產生上行實體軌跡。 ");
        var facilityResource = facility.ConflictResourceIds.Single();
        True(events.Any(item => item.EventType == SimulationEventType.RouteReserved
                && item.ResourceId == facility.FacilityId
                && item.ResourceIds?.Contains(facilityResource, StringComparer.OrdinalIgnoreCase) == true)
            && events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains(facilityResource, StringComparer.OrdinalIgnoreCase) == true),
            "O20 pocket resource 必須在折返前鎖定，並於完整車尾淨空後釋放。 ");
    }

    public static void O04OvertakingCompletesSafely() => AssertOvertake(
        "O04",
        LargeAirportLineFullScenarioBuilder.AirportDirectRunId,
        "AIRPORT-DIRECT-01",
        "FULL-O04");

    public static void O13OvertakingCompletesSafely() => AssertOvertake(
        "O13",
        LargeAirportLineFullScenarioBuilder.AirportDirectRunId,
        "AIRPORT-DIRECT-01",
        "FULL-O13");

    public static void SectionOvertakesAtO04ThenTurnsAtO20()
    {
        var section = Execution.Value.Document.ServiceTypes.Single(item => item.Id ==
            LargeAirportLineFullScenarioBuilder.SectionServiceId);
        var full = Execution.Value.Document.ServiceTypes.Single(item => item.Id ==
            LargeAirportLineFullScenarioBuilder.FullLineServiceId);
        True(section.CanRequestOvertake && section.Priority > full.Priority,
            "SECTION 必須明列較高 priority 與 CanRequestOvertake。 ");
        AssertOvertake(
            "O04",
            LargeAirportLineFullScenarioBuilder.SectionDownRunId,
            "SECTION-VEHICLE-01",
            "FULL-SECTION-O04");
        True(Execution.Value.World.Events.Any(item => item.VehicleId == "SECTION-VEHICLE-01"
                && item.EventType == SimulationEventType.DirectionChanged
                && item.ServiceRunId == LargeAirportLineFullScenarioBuilder.SectionUpRunId),
            "SECTION 在 O04 完成越行後必須繼續到 O20 並折返為上行車次。 ");
    }

    public static void AirportDirectCompletesTwoOvertakes()
    {
        var execution = Execution.Value;
        var facilityIds = execution.Document.Topology.PassingOperations
            .Where(operation => operation.ServiceRouteId == LargeAirportLineFullScenarioBuilder.DownRouteId
                && operation.ExpressServiceTypeId == LargeAirportLineFullScenarioBuilder.AirportDirectServiceId)
            .Select(operation => operation.FacilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var completed = execution.World.Events
            .Where(item => item.EventType == SimulationEventType.OvertakeCompleted
                && item.ServiceRunId == LargeAirportLineFullScenarioBuilder.AirportDirectRunId)
            .Select(item => item.ResourceId)
            .ToArray();
        Equal(2, completed.Length, "同一班機場直達車必須完成兩次實體越行。 ");
        True(facilityIds.All(facilityId => completed.Contains(facilityId, StringComparer.OrdinalIgnoreCase)),
            "兩次越行必須分別使用 O04 與 O13 facility。 ");
    }

    public static void O04AndO13AreBidirectionalFourTrackStations()
    {
        var document = Stages.Value.FullScenario;
        foreach (var stationId in new[] { "O04", "O13" })
        {
            var station = document.Topology.Stations.Single(item => item.StationId == stationId);
            var platforms = document.Topology.Platforms
                .Where(item => station.PlatformIds.Contains(item.PlatformId, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            Equal(4, platforms.Length,
                $"{stationId} 必須有上下行兩個側線停靠月台，以及兩個正線通過股 operational platform marker。 ");
            Equal(2, platforms.Count(item => item.AllowsPassengerService),
                $"{stationId} 必須維持 2 個可供旅客停靠的側式月台。 ");
            Equal(2, platforms.Count(item => !item.AllowsPassengerService),
                $"{stationId} 的兩個正線通過股不得被誤當旅客月台。 ");

            var facilities = document.Topology.PassingFacilities.Where(item => item.StationId == stationId).ToArray();
            Equal(2, facilities.Length, $"{stationId} 必須各有上下行正式 PassingFacility。 ");
            var operationalTrackIds = facilities
                .SelectMany(item => item.Traversals.Select(traversal => traversal.TrackEdgeId)
                    .Append(document.Topology.Platforms.Single(platform => platform.PlatformId == item.LocalPlatformId).TrackEdgeId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Equal(4, operationalTrackIds.Count,
                $"{stationId} 必須由 4 條相異實體 edge 組成上下行停靠股與通過股。 ");
            True(facilities.All(facility =>
            {
                var localPlatform = document.Topology.Platforms.Single(platform => platform.PlatformId == facility.LocalPlatformId);
                var expressPlatform = document.Topology.Platforms.Single(platform => platform.PlatformId == facility.ExpressPlatformId);
                var localEdge = document.Topology.Edges.Single(edge => edge.TrackEdgeId == localPlatform.TrackEdgeId);
                var expressEdges = facility.Traversals.Select(traversal =>
                    document.Topology.Edges.Single(edge => edge.TrackEdgeId == traversal.TrackEdgeId)).ToArray();
                return localEdge.Kind == TrackEdgeKind.Siding
                    && expressEdges.All(edge => edge.Kind == TrackEdgeKind.Mainline)
                    && expressEdges.Any(edge => edge.TrackEdgeId == expressPlatform.TrackEdgeId);
            }),
                $"{stationId} 上下行普通車必須在 Siding 側線停靠，高等列車必須循 Mainline 正線通過。 ");
            True(document.Topology.PassingOperations.Any(operation => operation.FacilityId == facilities[0].FacilityId)
                && document.Topology.PassingOperations.Any(operation => operation.FacilityId == facilities[1].FacilityId),
                $"{stationId} 上下行 facility 都必須納入正式 PassingOperation，不可孤立。 ");
        }
    }

    public static void NoCollisionOrStationStopViolation()
    {
        var unsafeEvents = Execution.Value.World.Events
            .Where(item => item.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation)
            .ToArray();
        True(unsafeEvents.Length == 0,
            "完整情境不得發生 Collision 或 StationStopViolation："
            + string.Join("；", unsafeEvents.Select(item => $"{item.SimulationTimeSeconds:0.0}s {item.EventType} {item.ServiceRunId}")));
    }

    public static void AllExpectedTrainsComplete()
    {
        var execution = Execution.Value;
        True(execution.World.GetSnapshot().Trains.All(train => !train.IsActive),
            "代表性班表推進後所有實體列車都必須退出營運。 ");
        var endedVehicles = execution.World.Events
            .Where(item => item.EventType == SimulationEventType.ServiceEnded)
            .Select(item => item.VehicleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var vehicleId in new[]
                 {
                     "FULL-O04", "FULL-O13", "FULL-SECTION-O04", "AIRPORT-DIRECT-01", "FULL-UP-01",
                     "SECTION-VEHICLE-01"
                 })
            True(endedVehicles.Contains(vehicleId), $"車輛 {vehicleId} 必須留下 ServiceEnded。 ");
    }

    public static void Schema8RoundTripPreservesFullScenario()
    {
        var source = Stages.Value.FullScenario;
        var json = TopologyProjectFormat.Serialize(source);
        var roundTrip = TopologyProjectFormat.Deserialize(json);
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "大型機場線-完整營運示範範例.mrtsim.json");
        var artifact = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        Equal(TopologyProjectFormat.CurrentSchemaVersion, roundTrip.SchemaVersion);
        Equal(28, roundTrip.Topology.Stations.Count);
        Equal(1, roundTrip.Topology.TurnbackFacilities.Count);
        Equal(4, roundTrip.Topology.PassingFacilities.Count);
        Equal(8, roundTrip.Dispatch.ManualTimetableRows?.Length ?? 0);
        True(roundTrip.Topology.DirectedConnections.Count > source.ServiceRoutes.Sum(route => route.Traversals.Count - 1),
            "round-trip 必須保留主線以外 facility 的有向轉向。 ");
        Equal(json, TopologyProjectFormat.Serialize(roundTrip),
            "builder 產生的 Schema 8 document 必須可 lossless round-trip。 ");
        Equal(json, TopologyProjectFormat.Serialize(artifact),
            "sample artifact 必須與可重建 builder 輸出一致，不可手工漂移。 ");
        TopologyProjectFormat.Validate(roundTrip);
    }

    public static void CreatesTopologyNativeSimulationWorld()
    {
        var execution = Execution.Value;
        True(execution.World.Trajectory.Any(sample => sample.TrackEdgeId is not null),
            "完整 scenario 必須產生 edge-local topology trajectory。 ");
        True(execution.World.Events.All(item => item.TrackEdgeId is null
                || !item.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "完整 scenario 不得退回 legacy virtual track。 ");
        Throws<InvalidOperationException>(() => _ = execution.World.Route, "不提供 compatibility Route");
        Throws<InvalidOperationException>(() => _ = execution.World.Infrastructure, "不提供 legacy InfrastructureGraph");
    }

    private static void AssertOvertake(
        string stationId,
        string expressRunId,
        string expressVehicleId,
        string localVehicleId)
    {
        var execution = Execution.Value;
        var facilityId = execution.Document.Topology.PassingOperations.Single(item =>
            item.ServiceRouteId == LargeAirportLineFullScenarioBuilder.DownRouteId
            && item.ExpressServiceTypeId == (expressRunId == LargeAirportLineFullScenarioBuilder.SectionDownRunId
                ? LargeAirportLineFullScenarioBuilder.SectionServiceId
                : LargeAirportLineFullScenarioBuilder.AirportDirectServiceId)
            && execution.Document.Topology.PassingFacilities.Single(facility => facility.FacilityId == item.FacilityId)
                .StationId == stationId).FacilityId;
        var facility = execution.Document.Topology.PassingFacilities.Single(item => item.FacilityId == facilityId);
        var facilityResource = facility.ConflictResourceIds.Single();
        var requested = execution.World.Events.Single(item => item.EventType == SimulationEventType.OvertakeRequested
            && item.ServiceRunId == expressRunId
            && item.ResourceId == facility.FacilityId);
        Equal(localVehicleId, requested.RelatedVehicleId,
            $"{stationId} 必須由指定普通車先抵達並成為直達車越行對象。 ");
        var localArrival = execution.World.Events.Last(item => item.EventType == SimulationEventType.Arrival
            && item.VehicleId == localVehicleId
            && item.StationId == stationId
            && item.SimulationTimeSeconds <= requested.SimulationTimeSeconds);
        var localPlatform = execution.Document.Topology.Platforms.Single(item => item.PlatformId == facility.LocalPlatformId);
        Equal(localPlatform.TrackEdgeId, localArrival.TrackEdgeId,
            $"{stationId} 待避普通車必須實際在側線月台所屬 edge 到站。 ");
        Equal(TrackEdgeKind.Siding, execution.Document.Topology.Edges.Single(item =>
                item.TrackEdgeId == localArrival.TrackEdgeId).Kind,
            $"{stationId} 待避普通車的到站 edge 必須明確是 Siding。 ");
        True(localArrival.SimulationTimeSeconds < requested.SimulationTimeSeconds,
            $"{stationId} 必須由普通車先抵達，直達車後到才提出越行。 ");
        var completed = execution.World.Events.Single(item => item.EventType == SimulationEventType.OvertakeCompleted
            && item.ServiceRunId == expressRunId
            && item.ResourceId == facility.FacilityId);
        True(completed.SimulationTimeSeconds > requested.SimulationTimeSeconds,
            $"{stationId} OvertakeCompleted 必須晚於 OvertakeRequested。 ");
        True(execution.World.Trajectory.Any(item => item.ServiceRunId == expressRunId
                && facility.Traversals.Any(traversal => traversal.TrackEdgeId == item.TrackEdgeId)),
            $"{expressRunId} 必須實際行經 {stationId} passing traversal。 ");

        var released = execution.World.Events.First(item => item.EventType == SimulationEventType.RouteReleased
            && item.VehicleId == expressVehicleId
            && item.SimulationTimeSeconds >= completed.SimulationTimeSeconds
            && item.ResourceIds?.Contains(facilityResource, StringComparer.OrdinalIgnoreCase) == true);
        var localDeparture = execution.World.Events.First(item => item.EventType == SimulationEventType.Departure
            && item.VehicleId == localVehicleId
            && item.StationId == stationId
            && item.SimulationTimeSeconds >= requested.SimulationTimeSeconds);
        True(localDeparture.SimulationTimeSeconds >= released.SimulationTimeSeconds,
            $"{stationId} 普通車必須等直達車車尾淨空、facility resource 釋放後才離站。 ");
    }

    private static ScenarioExecution RunFullScenario()
    {
        var document = Stages.Value.FullScenario;
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
            Topology: runtime.Topology).CreateWorld();
        world.AdvanceTo(8000);
        return new ScenarioExecution(document, world);
    }

    private static void AssertAllStopJourney(
        SimulationWorld world,
        string serviceRunId,
        IReadOnlyList<string> stationIds)
    {
        var events = world.Events.Where(item => item.ServiceRunId == serviceRunId).ToArray();
        True(events.Any(item => item.EventType == SimulationEventType.Departure && item.StationId == stationIds[0]),
            $"{serviceRunId} 必須由 {stationIds[0]} 發車。 ");
        var arrivals = events.Where(item => item.EventType == SimulationEventType.Arrival)
            .Select(item => item.StationId)
            .Where(stationId => stationId is not null)
            .Cast<string>()
            .ToArray();
        SequenceEqual(stationIds.Skip(1), arrivals,
            $"{serviceRunId} 必須按正確順序全停至 {stationIds[^1]}。 ");
        True(events.Any(item => item.EventType == SimulationEventType.ServiceEnded),
            $"{serviceRunId} 必須在終點正常退出營運。 ");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("找不到 repository root。 ");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message = "")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}預期={expected}，實際={actual}。 ");
    }

    private static void Close(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}預期={expected:0.###}，實際={actual:0.###}。 ");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<TException>(Action action, string expectedMessage)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception) when (exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException($"預期擲出 {typeof(TException).Name} 且包含「{expectedMessage}」。 ");
    }

    private sealed record ScenarioExecution(TopologyProjectDocument Document, SimulationWorld World);
}
