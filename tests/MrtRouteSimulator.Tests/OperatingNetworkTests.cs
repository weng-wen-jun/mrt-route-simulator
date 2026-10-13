using MrtRouteSimulator.Engine;

internal static class OperatingNetworkTests
{
    public static void DoubleTrackMainBaselineValidatesAndRuns()
    {
        var document = OperatingNetworkBuilder.Build(LoadTemplate(),
        [
            MainLine(lengthMeters: 2_000, stationCount: 3, trackCount: 2)
        ]);

        TopologyProjectFormat.Validate(document);
        Equal(2, document.ServiceRoutes.Length, "單一主線應建立一組上下行 ServiceRoute。");
        True(document.Topology.Edges.All(edge => edge.Directionality == TrackDirectionality.ForwardOnly),
            "雙線主線應使用分離的單向 edge。");
        True(document.ServiceRoutes.All(route => route.Stops.Count >= 3),
            "每個方向都必須保留完整停站順序。");

        var world = CreateWorld(document);
        world.AdvanceTo(600);
        True(world.Events.Any(item => item.EventType == SimulationEventType.Departure),
            "雙線主線 bounded smoke 應實際發車。");
        True(world.Events.All(item => item.EventType != SimulationEventType.Collision),
            "雙線主線 bounded smoke 不得發生碰撞。");
    }

    public static void BranchAtSpecifiedChainageSharesTrunkAndDispatchRoutes()
    {
        var main = MainLine(lengthMeters: 3_000, stationCount: 4, trackCount: 2);
        var branch = new OperatingLineDefinition
        {
            LineId = "BRANCH",
            Name = "支線",
            TrackCount = 2,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 1_000,
            LengthMeters = 1_500,
            StationCount = 3,
            OutboundServiceRouteId = "BRANCH:DOWN",
            InboundServiceRouteId = "BRANCH:UP"
        };
        var document = OperatingNetworkBuilder.Build(LoadTemplate(), [main, branch]);
        TopologyProjectFormat.Validate(document);

        var mainDown = document.ServiceRoutes.Single(route => route.ServiceRouteId == main.OutboundServiceRouteId);
        var branchDown = document.ServiceRoutes.Single(route => route.ServiceRouteId == branch.OutboundServiceRouteId);
        Equal(mainDown.Traversals[0], branchDown.Traversals[0],
            "支線下行應重用主線起點至指定銜接里程的共用幹線。");
        True(mainDown.Traversals[1] != branchDown.Traversals[1],
            "支線應在指定銜接節點離開主線。");
        var sharedEndNode = EndNode(document, branchDown.Traversals[0]);
        var branchStartNode = StartNode(document, branchDown.Traversals[1]);
        Equal(sharedEndNode, branchStartNode, "支線必須在拆分後的實體 junction node 接入。");
        True(document.Topology.Nodes.Any(node => node.NodeId == sharedEndNode && node.Kind == TrackNodeKind.Junction),
            "指定里程應建立 Junction node。");

        var expectedRouteIds = document.ServiceRoutes.Select(route => route.ServiceRouteId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualRouteIds = document.Dispatch.ManualTimetableRows!
            .Select(row => row.ServiceRouteId)
            .Where(routeId => routeId is not null)
            .Select(routeId => routeId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        True(expectedRouteIds.SetEquals(actualRouteIds),
            "每條營運路線的上下行手動班表都必須明確指定 ServiceRouteId。");
    }

    public static void SingleTrackOpposingRunsMutuallyExcludeAndReset()
    {
        var document = OperatingNetworkBuilder.Build(LoadTemplate(),
        [
            MainLine(lengthMeters: 1_200, stationCount: 3, trackCount: 1)
        ]);
        var block = document.Topology.Resources.Single(resource => resource.Kind == ConflictResourceKind.Block);
        True(document.Topology.Edges.All(edge => edge.Directionality == TrackDirectionality.Bidirectional
                && edge.ConflictResourceIds.Contains(block.ResourceId, StringComparer.OrdinalIgnoreCase)),
            "單線所有 edge 都必須使用同一個保守閉塞資源。");

        var world = CreateWorld(document);
        var first = RunToBound(world, 1_200);
        True(first.Any(item => item.EventType == SimulationEventType.WaitingForResource),
            "相向車次同時需要單線時，至少一班必須等待整線閉塞資源。");
        True(first.All(item => item.EventType != SimulationEventType.Collision),
            "單線互斥不得產生碰撞事件。");
        True(world.IsComplete, "單線兩方向車次必須在有界時間內完成。");

        var firstSignature = Signature(first);
        world.Reset();
        var second = RunToBound(world, 1_200);
        Equal(firstSignature, Signature(second), "Reset 後單線資源等待與事件順序必須可重現。");
    }

    public static void MetadataRejectsInvalidNetworks()
    {
        var invalidTrack = MainLine() with { TrackCount = 3 };
        var errors = new List<string>();
        OperatingNetworkBuilder.ValidateMetadata([invalidTrack], errors);
        Contains(errors, "只能是 1 或 2", "metadata 應拒絕無效軌道數。");

        var parent = MainLine();
        var outside = new OperatingLineDefinition
        {
            LineId = "OUTSIDE",
            Name = "越界支線",
            ParentLineId = parent.LineId,
            JunctionChainageMeters = parent.LengthMeters + 1,
            OutboundServiceRouteId = "OUTSIDE:DOWN",
            InboundServiceRouteId = "OUTSIDE:UP"
        };
        errors.Clear();
        OperatingNetworkBuilder.ValidateMetadata([parent, outside], errors);
        Contains(errors, "範圍內", "metadata 應拒絕超出父線長度的接軌里程。");

        var cycleA = MainLine() with { LineId = "A", Name = "A", ParentLineId = "B",
            OutboundServiceRouteId = "A:DOWN", InboundServiceRouteId = "A:UP" };
        var cycleB = MainLine() with { LineId = "B", Name = "B", ParentLineId = "A",
            OutboundServiceRouteId = "B:DOWN", InboundServiceRouteId = "B:UP" };
        errors.Clear();
        OperatingNetworkBuilder.ValidateMetadata([cycleA, cycleB], errors);
        Contains(errors, "不可形成循環", "metadata 應拒絕 parent cycle／非 parent-before-child 排序。");

        var validDocument = OperatingNetworkBuilder.Build(LoadTemplate(), [parent]);
        try
        {
            TopologyProjectFormat.Validate(validDocument with { OperatingLines = [null!] });
            throw new InvalidOperationException("Schema 8 應拒絕 null 營運路線 metadata 項目。");
        }
        catch (SimulationValidationException exception)
        {
            if (!exception.Message.Contains("不可包含 null", StringComparison.Ordinal))
                throw new InvalidOperationException($"null metadata 錯誤訊息不明確：{exception.Message}");
        }
    }

    public static void Schema8RoundTripPreservesOperatingLines()
    {
        var main = MainLine(lengthMeters: 2_400, stationCount: 4, trackCount: 2);
        var branch = new OperatingLineDefinition
        {
            LineId = "ROUNDTRIP-BRANCH",
            Name = "往返支線",
            TrackCount = 1,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 800,
            LengthMeters = 900,
            StationCount = 3,
            OutboundServiceRouteId = "ROUNDTRIP-BRANCH:DOWN",
            InboundServiceRouteId = "ROUNDTRIP-BRANCH:UP"
        };
        var source = OperatingNetworkBuilder.Build(LoadTemplate(), [main, branch]);
        var loaded = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(source));

        Equal(TopologyProjectFormat.CurrentSchemaVersion, loaded.SchemaVersion,
            "多營運路線不得另立新 schema。");
        Equal(2, loaded.OperatingLines?.Length ?? 0, "Schema 8 round-trip 必須保存 OperatingLines metadata。");
        Equal(branch, loaded.OperatingLines![1], "支線 parent、里程、軌道數及 ServiceRoute ID 必須完整保存。");
        TopologyProjectFormat.Validate(loaded);
    }

    public static void MainAndBranchRunsKeepRouteIdentityAcrossAllResults()
    {
        var main = MainLine(lengthMeters: 3_000, stationCount: 4, trackCount: 2);
        var branch = new OperatingLineDefinition
        {
            LineId = "IDENTITY-BRANCH",
            Name = "識別支線",
            TrackCount = 2,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 1_000,
            LengthMeters = 1_600,
            StationCount = 3,
            OutboundServiceRouteId = "IDENTITY-BRANCH:DOWN",
            InboundServiceRouteId = "IDENTITY-BRANCH:UP"
        };
        var document = OperatingNetworkBuilder.Build(LoadTemplate(), [main, branch]);
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = CreateWorld(document);
        var routesByRun = runtime.DispatchPlan.Runs.ToDictionary(
            run => run.ServiceRunId,
            run => run.ServiceRouteId!,
            StringComparer.OrdinalIgnoreCase);
        var linesByRoute = document.ServiceRoutes.ToDictionary(
            route => route.ServiceRouteId,
            route => route.OperatingLineId!,
            StringComparer.OrdinalIgnoreCase);
        var snapshotRuns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!world.IsComplete && world.CurrentTimeSeconds < 1_800)
        {
            var snapshot = world.Tick();
            foreach (var train in snapshot.Trains.Where(train => routesByRun.ContainsKey(train.ServiceRunId)))
            {
                snapshotRuns.Add(train.ServiceRunId);
                Equal(routesByRun[train.ServiceRunId], train.ServiceRouteId,
                    $"snapshot 的 {train.ServiceRunId} 不得退回方向預設 route。");
                Equal(linesByRoute[routesByRun[train.ServiceRunId]], train.OperatingLineId,
                    $"snapshot 的 {train.ServiceRunId} 必須保留營運路線識別。");
            }
        }

        True(world.IsComplete, "主線與支線四個 explicit route 車次必須在有界時間內完成。");
        True(snapshotRuns.SetEquals(routesByRun.Keys), "snapshot 應觀察到四條 explicit route 的車次。");
        foreach (var run in runtime.DispatchPlan.Runs)
        {
            var expectedRouteId = routesByRun[run.ServiceRunId];
            var expectedLineId = linesByRoute[expectedRouteId];
            var trajectory = world.Trajectory.Where(item =>
                item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)).ToArray();
            True(trajectory.Length > 0, $"{run.ServiceRunId} 應有軌跡樣本。");
            True(trajectory.All(item => item.ServiceRouteId == expectedRouteId
                    && item.OperatingLineId == expectedLineId),
                $"{run.ServiceRunId} 的 trajectory route identity 不可串線。");

            var events = world.Events.Where(item =>
                item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)).ToArray();
            True(events.Any(item => item.EventType == SimulationEventType.Departure)
                    && events.Any(item => item.EventType == SimulationEventType.ServiceEnded),
                $"{run.ServiceRunId} 應完整發車並退出營運。");
            True(events.All(item => item.ServiceRouteId == expectedRouteId
                    && item.OperatingLineId == expectedLineId),
                $"{run.ServiceRunId} 的 event route identity 不可串線。");
        }

        var context = world.GetTopologyResultContext();
        var timetable = OperationsTimetable.Build(
            context, runtime.DispatchPlan, [], world.Events, runtime.ServicePatterns);
        foreach (var run in runtime.DispatchPlan.Runs)
        {
            var expectedStations = context.GetDisplayStations(run).Select(station => station.StationId).ToArray();
            var actualRows = timetable.Where(row => row.ServiceRunId == run.ServiceRunId).ToArray();
            True(actualRows.Select(row => row.StationId).SequenceEqual(expectedStations),
                $"{run.ServiceRunId} 的時刻表站序必須完全取自自己的 ServiceRoute。");
            True(actualRows.All(row => row.ServiceRouteId == run.ServiceRouteId),
                $"{run.ServiceRunId} 的時刻表必須保留 ServiceRouteId。");
            if (run.ServiceRouteId == branch.OutboundServiceRouteId
                || run.ServiceRouteId == branch.InboundServiceRouteId)
            {
                True(actualRows.All(row => row.StationId is not "STATION:MAIN:2"
                        and not "STATION:MAIN:3" and not "STATION:MAIN:4"),
                    "支線時刻表不得混入分歧後的主線車站。");
            }
        }

        var intervals = IntervalStatistics.Analyze(context, world.Trajectory, world.Events);
        foreach (var run in runtime.DispatchPlan.Runs)
        {
            var expectedRouteId = routesByRun[run.ServiceRunId];
            var expectedLineId = linesByRoute[expectedRouteId];
            var runIntervals = intervals.CompletedIntervals.Where(item =>
                item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)).ToArray();
            True(runIntervals.Length > 0 && runIntervals.All(item =>
                    item.ServiceRouteId == expectedRouteId && item.OperatingLineId == expectedLineId),
                $"{run.ServiceRunId} 的完成區間必須保留 route identity。");
            True(intervals.JourneyStatistics.Any(item => item.ServiceRunId == run.ServiceRunId
                    && item.IsComplete
                    && item.ServiceRouteId == expectedRouteId
                    && item.OperatingLineId == expectedLineId),
                $"{run.ServiceRunId} 的全程統計必須完成且保留 route identity。");
        }
    }

    public static void IndependentRootsAndNonStationJunctionRemainDistinct()
    {
        var main = MainLine(lengthMeters: 3_000, stationCount: 4, trackCount: 2);
        var independent = new OperatingLineDefinition
        {
            LineId = "INDEPENDENT",
            Name = "獨立主線",
            TrackCount = 1,
            LengthMeters = 1_400,
            StationCount = 3,
            OutboundServiceRouteId = "INDEPENDENT:DOWN",
            InboundServiceRouteId = "INDEPENDENT:UP"
        };
        var branch = new OperatingLineDefinition
        {
            LineId = "MID-BRANCH",
            Name = "里程支線",
            TrackCount = 2,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 750,
            LengthMeters = 1_200,
            StationCount = 3,
            OutboundServiceRouteId = "MID-BRANCH:DOWN",
            InboundServiceRouteId = "MID-BRANCH:UP"
        };
        var document = OperatingNetworkBuilder.Build(LoadTemplate(), [main, independent, branch]);
        TopologyProjectFormat.Validate(document);

        var mainDown = document.ServiceRoutes.Single(route => route.ServiceRouteId == main.OutboundServiceRouteId);
        var independentDown = document.ServiceRoutes.Single(route => route.ServiceRouteId == independent.OutboundServiceRouteId);
        var branchDown = document.ServiceRoutes.Single(route => route.ServiceRouteId == branch.OutboundServiceRouteId);
        True(!mainDown.Traversals.Select(item => item.TrackEdgeId).Intersect(
                independentDown.Traversals.Select(item => item.TrackEdgeId), StringComparer.OrdinalIgnoreCase).Any(),
            "多個獨立 root 不得共用實體 edge。");
        Equal(mainDown.Traversals[0], branchDown.Traversals[0],
            "非站位置支線仍應重用父線原點至接軌點的幹線 edge。");
        True(document.Topology.Edges.Single(edge =>
                edge.TrackEdgeId == mainDown.Traversals[0].TrackEdgeId).LengthMeters == 750,
            "父線必須在非站接軌里程 750 m 精確拆分 edge。");
        var junctionNodeId = EndNode(document, branchDown.Traversals[0]);
        True(document.Topology.Nodes.Any(node => node.NodeId == junctionNodeId
                && node.Kind == TrackNodeKind.Junction),
            "非站接軌里程必須建立實體 Junction node。");
        True(document.Topology.Stations.All(station => station.StationId != junctionNodeId),
            "非站接軌點不得被誤建為父線邏輯車站。");
        True(branchDown.Stops.Select(stop => stop.StationId).SequenceEqual(
                ["STATION:MAIN:1", "STATION:MID-BRANCH:1", "STATION:MID-BRANCH:2", "STATION:MID-BRANCH:3"]),
            "非站接軌支線只能包含接軌前共享站與自己的支線站。");
    }

    private static OperatingLineDefinition MainLine(
        double lengthMeters = 2_000,
        int stationCount = 3,
        int trackCount = 2) => new()
    {
        LineId = "MAIN",
        Name = "主線",
        TrackCount = trackCount,
        LengthMeters = lengthMeters,
        StationCount = stationCount,
        OutboundServiceRouteId = "MAIN:DOWN",
        InboundServiceRouteId = "MAIN:UP"
    };

    private static TopologyProjectDocument LoadTemplate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new InvalidOperationException("找不到 repository root。");
        var path = Path.Combine(directory.FullName, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        return TopologyProjectFormat.Deserialize(File.ReadAllText(path));
    }

    private static SimulationWorld CreateWorld(TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        return new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: null,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
    }

    private static IReadOnlyList<SimulationEvent> RunToBound(SimulationWorld world, double maximumTimeSeconds)
    {
        while (!world.IsComplete && world.CurrentTimeSeconds < maximumTimeSeconds)
            world.Tick();
        return world.Events.ToArray();
    }

    private static string Signature(IEnumerable<SimulationEvent> events) => string.Join("|", events.Select(item =>
        $"{item.SimulationTimeSeconds:0.0}:{item.EventType}:{item.ServiceRunId}:{item.ResourceId}"));

    private static string StartNode(TopologyProjectDocument document, DirectedTrackTraversal traversal)
    {
        var edge = document.Topology.Edges.Single(item => item.TrackEdgeId == traversal.TrackEdgeId);
        return traversal.Direction == TraversalDirection.Forward ? edge.FromNodeId : edge.ToNodeId;
    }

    private static string EndNode(TopologyProjectDocument document, DirectedTrackTraversal traversal)
    {
        var edge = document.Topology.Edges.Single(item => item.TrackEdgeId == traversal.TrackEdgeId);
        return traversal.Direction == TraversalDirection.Forward ? edge.ToNodeId : edge.FromNodeId;
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
    }

    private static void Contains(IEnumerable<string> values, string expected, string message)
    {
        if (!values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{message} Errors={string.Join(" | ", values)}");
    }
}
