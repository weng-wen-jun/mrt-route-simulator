using MrtRouteSimulator.Engine;

internal static class TaichungAirportFullScenarioTests
{
    private static readonly Lazy<TaichungAirportScenarioStages> Stages = new(
        TaichungAirportFullScenarioBuilder.BuildStages);
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
        SequenceEqual(TaichungAirportFullScenarioBuilder.StationIds,
            document.Topology.Stations.Select(station => station.StationId),
            "站序必須包含 O01～O26、O08a 與 O15a。 ");
        Equal(56, document.Topology.Platforms.Count, "每站必須各有上下行月台。 ");
        Equal(54, document.Topology.Edges.Count, "28 站雙線主線必須有 54 條實體 edge。 ");
        True(document.Topology.Edges.All(edge => edge.FromPortSide is not null && edge.ToPortSide is not null),
            "完整站鏈每條主線 edge 都必須明列 physical port sides。 ");
        Equal(52, document.Topology.DirectedConnections.Count,
            "上下行各 27 個 traversal 應各有 26 個明確有向接續。 ");
        Equal(27, document.ServiceRoutes.Single(route => route.ServiceRouteId == TaichungAirportFullScenarioBuilder.DownRouteId).Traversals.Count,
            "下行 ServiceRoute 必須涵蓋完整站鏈。 ");
        Equal(27, document.ServiceRoutes.Single(route => route.ServiceRouteId == TaichungAirportFullScenarioBuilder.UpRouteId).Traversals.Count,
            "上行 ServiceRoute 必須涵蓋完整站鏈。 ");
        Equal(7, stages.StageValidations.Count,
            "minimal、station chain、service、turnback、兩個 passing 與 timetable 都必須留下 gate 結果。 ");
        True(stages.StageValidations.All(validation => validation.StructuralPassed && validation.OperationalPassed),
            "每個 Taichung builder stage 都必須通過 Structural 與 Operational gate。 ");
        TopologyProjectFormat.Validate(document);
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
            && TaichungAirportFullScenarioBuilder.StationIds.Skip(1).All(arrivals.Contains),
            "O01 發車的全程車必須實際完成 28 站全停（起點以 Departure、其餘以 Arrival 證明）。 ");
        True(events.Any(item => item.EventType == SimulationEventType.ServiceEnded),
            "全程車必須完成 O26 並退出營運。 ");
    }

    public static void RouteLengthsMatchReviewedSource()
    {
        var document = Stages.Value.FullStationChain;
        var route = document.ServiceRoutes.Single(item =>
            item.ServiceRouteId == TaichungAirportFullScenarioBuilder.DownRouteId);
        var edgeLengths = document.Topology.Edges.ToDictionary(
            item => item.TrackEdgeId,
            item => item.LengthMeters,
            StringComparer.OrdinalIgnoreCase);
        var fullLength = route.Traversals.Sum(item => edgeLengths[item.TrackEdgeId]);
        var o20TraversalCount = Array.IndexOf(TaichungAirportFullScenarioBuilder.StationIds, "O20");
        var o20Length = route.Traversals.Take(o20TraversalCount).Sum(item => edgeLengths[item.TrackEdgeId]);
        Close(TaichungAirportFullScenarioBuilder.FullRouteLengthMeters, fullLength, 0.001,
            "O01-O26 必須使用覆核來源的 29.9 km 總長度；逐站距仍為 synthetic 等分。 ");
        Close(TaichungAirportFullScenarioBuilder.AirportSectionLengthMeters, o20Length, 0.001,
            "O01-O20 必須使用覆核來源的 23.8 km 總長度；逐站距仍為 synthetic 等分。 ");
    }

    public static void AirportDirectSkipStopWorks()
    {
        var events = Execution.Value.World.Events
            .Where(item => item.ServiceRunId == TaichungAirportFullScenarioBuilder.AirportDirectRunId)
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
            && TaichungAirportFullScenarioBuilder.AirportDirectStops.Where(id => id != "O01").All(stopped.Contains),
            "機場直達車必須由 O01 發車並停靠 O08/O11/O16/O20。 ");
        True(new[] { "O02", "O03", "O04", "O05", "O08a", "O15a", "O19" }.All(passed.Contains),
            "機場直達車必須以 StationPassed 事件證明 O01-O20 中間站 skip-stop。 ");
        True(!passed.Overlaps(TaichungAirportFullScenarioBuilder.AirportDirectStops),
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
            TaichungAirportFullScenarioBuilder.SectionDownRunId,
            TaichungAirportFullScenarioBuilder.SectionUpRunId,
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
            TaichungAirportFullScenarioBuilder.AirportDirectRunId,
            TaichungAirportFullScenarioBuilder.AirportDirectUpRunId,
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
        TaichungAirportFullScenarioBuilder.AirportDirectRunId,
        "AIRPORT-DIRECT-01",
        "FULL-O04");

    public static void O13OvertakingCompletesSafely() => AssertOvertake(
        "O13",
        TaichungAirportFullScenarioBuilder.AirportDirectRunId,
        "AIRPORT-DIRECT-01",
        "FULL-O13");

    public static void SectionOvertakesAtO04ThenTurnsAtO20()
    {
        var section = Execution.Value.Document.ServiceTypes.Single(item => item.Id ==
            TaichungAirportFullScenarioBuilder.SectionServiceId);
        var full = Execution.Value.Document.ServiceTypes.Single(item => item.Id ==
            TaichungAirportFullScenarioBuilder.FullLineServiceId);
        True(section.CanRequestOvertake && section.Priority > full.Priority,
            "SECTION 必須明列較高 priority 與 CanRequestOvertake。 ");
        AssertOvertake(
            "O04",
            TaichungAirportFullScenarioBuilder.SectionDownRunId,
            "SECTION-VEHICLE-01",
            "FULL-SECTION-O04");
        True(Execution.Value.World.Events.Any(item => item.VehicleId == "SECTION-VEHICLE-01"
                && item.EventType == SimulationEventType.DirectionChanged
                && item.ServiceRunId == TaichungAirportFullScenarioBuilder.SectionUpRunId),
            "SECTION 在 O04 完成越行後必須繼續到 O20 並折返為上行車次。 ");
    }

    public static void AirportDirectCompletesTwoOvertakes()
    {
        var execution = Execution.Value;
        var facilityIds = execution.Document.Topology.PassingOperations
            .Where(operation => operation.ServiceRouteId == TaichungAirportFullScenarioBuilder.DownRouteId
                && operation.ExpressServiceTypeId == TaichungAirportFullScenarioBuilder.AirportDirectServiceId)
            .Select(operation => operation.FacilityId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var completed = execution.World.Events
            .Where(item => item.EventType == SimulationEventType.OvertakeCompleted
                && item.ServiceRunId == TaichungAirportFullScenarioBuilder.AirportDirectRunId)
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
                $"{stationId} 必須有上下行兩個側式停靠月台，以及兩個通過股 operational platform marker。 ");
            Equal(2, platforms.Count(item => item.AllowsPassengerService),
                $"{stationId} 必須維持 2 個可供旅客停靠的側式月台。 ");
            Equal(2, platforms.Count(item => !item.AllowsPassengerService),
                $"{stationId} 的兩個內側通過股不得被誤當旅客月台。 ");

            var facilities = document.Topology.PassingFacilities.Where(item => item.StationId == stationId).ToArray();
            Equal(2, facilities.Length, $"{stationId} 必須各有上下行正式 PassingFacility。 ");
            var operationalTrackIds = facilities
                .SelectMany(item => item.Traversals.Select(traversal => traversal.TrackEdgeId)
                    .Append(document.Topology.Platforms.Single(platform => platform.PlatformId == item.LocalPlatformId).TrackEdgeId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Equal(4, operationalTrackIds.Count,
                $"{stationId} 必須由 4 條相異實體 edge 組成上下行停靠股與通過股。 ");
            True(facilities.All(facility => facility.Traversals.All(traversal =>
                    document.Topology.Edges.Any(edge => edge.TrackEdgeId == traversal.TrackEdgeId
                        && edge.Kind == TrackEdgeKind.PassingTrack))),
                $"{stationId} 上下行通過股必須是 topology-native PassingTrack edge。 ");
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
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "臺中機場捷運-完整營運示範範例.mrtsim.json");
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
            item.ServiceRouteId == TaichungAirportFullScenarioBuilder.DownRouteId
            && item.ExpressServiceTypeId == (expressRunId == TaichungAirportFullScenarioBuilder.SectionDownRunId
                ? TaichungAirportFullScenarioBuilder.SectionServiceId
                : TaichungAirportFullScenarioBuilder.AirportDirectServiceId)
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
