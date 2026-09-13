using MrtRouteSimulator.Engine;

internal static class TopologyRegressionTests
{
    public static void LinearBuilderCreatesSegmentedBidirectionalTopology()
    {
        var route = CreateThreeStationRoute();
        var result = LinearInfrastructureBuilder.Build(route);

        Equal(3, result.Infrastructure.Nodes.Count, "線性 builder 應建立每站一個 topology node。");
        Equal(4, result.Infrastructure.Edges.Count, "三站線應建立每相鄰站一條下行與一條上行 edge。");
        Equal(2, result.OutboundServiceRoute.Traversals.Count, "下行 route 必須保留兩段有序 traversal。");
        Equal(2, result.InboundServiceRoute.Traversals.Count, "上行 route 必須保留兩段有序 traversal。");

        var down = result.OutboundServiceRoute.Traversals;
        Equal("EDGE:DOWN:O01:O02", down[0].TrackEdgeId, "下行第一段不可被 set 重新排序。");
        Equal("EDGE:DOWN:O02:O03", down[1].TrackEdgeId, "下行第二段 edge 不正確。");
        NearlyEqual(1000, result.Infrastructure.GetRequiredEdge(down[0].TrackEdgeId).LengthMeters);
        NearlyEqual(2000, result.Infrastructure.GetRequiredEdge(down[1].TrackEdgeId).LengthMeters);

        var up = result.InboundServiceRoute.Traversals;
        Equal("EDGE:UP:O03:O02", up[0].TrackEdgeId, "上行第一段 edge 不正確。");
        Equal("EDGE:UP:O02:O01", up[1].TrackEdgeId, "上行第二段 edge 不正確。");
        Equal("PLATFORM:O02:DOWN", result.OutboundServiceRoute.Stops[1].CandidatePlatformIds.Single(), "中間站下行月台不正確。");
        Equal("PLATFORM:O02:UP", result.InboundServiceRoute.Stops[1].CandidatePlatformIds.Single(), "中間站上行月台不正確。");
        NearlyEqual(2000, result.Infrastructure.Platforms["PLATFORM:O02:UP"].StopPositionOffsetMeters);
    }

    public static void LinearBuilderPreservesLegacyRoute()
    {
        var route = CreateThreeStationRoute();
        var before = route.Stations.Select(station => (station.StationId, station.PositionMeters, station.DwellTimeSeconds)).ToArray();

        _ = LinearInfrastructureBuilder.Build(route);

        var after = route.Stations.Select(station => (station.StationId, station.PositionMeters, station.DwellTimeSeconds)).ToArray();
        Equal(before.Length, after.Length, "builder 不可變更既有 Route station 數量。");
        for (var index = 0; index < before.Length; index++)
        {
            Equal(before[index].StationId, after[index].StationId, "builder 不可變更既有 StationId。");
            NearlyEqual(before[index].PositionMeters, after[index].PositionMeters);
            NearlyEqual(before[index].DwellTimeSeconds, after[index].DwellTimeSeconds);
        }
    }

    public static void ValidatorRejectsMissingNodesAndInvalidPlatformOffsets()
    {
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes = [new TrackNodeDefinition("N1", "起點", TrackNodeKind.Boundary)],
            Edges = [CreateEdge("E1", "N1", "N2", 100)],
            Stations = [new StationDefinitionV4
            {
                StationId = "S1",
                Name = "站",
                PlatformIds = ["P1"]
            }],
            Platforms = [new PlatformDefinitionV4
            {
                PlatformId = "P1",
                StationId = "S1",
                Name = "月台",
                TrackEdgeId = "E1",
                PlatformStartOffsetMeters = 80,
                StopPositionOffsetMeters = 20,
                PlatformEndOffsetMeters = 120,
                EffectiveLengthMeters = 100
            }]
        };

        var validation = InfrastructureValidator.Validate(definition);
        False(validation.IsValid, "不完整 topology 不可通過驗證。");
        Contains(validation.Errors, "不存在的終點節點", "validator 應指出 edge node 參照錯誤。");
        Contains(validation.Errors, "0 <= start <= stop <= end <= edge 長度", "validator 應拒絕無效月台 offset。");
    }

    public static void ValidatorRejectsDisconnectedAndDirectionallyInvalidRoute()
    {
        var disconnected = new InfrastructureGraphV4(new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("N1", "一", TrackNodeKind.Boundary),
                new TrackNodeDefinition("N2", "二", TrackNodeKind.Ordinary),
                new TrackNodeDefinition("N3", "三", TrackNodeKind.Ordinary),
                new TrackNodeDefinition("N4", "四", TrackNodeKind.Boundary)
            ],
            Edges = [CreateEdge("E1", "N1", "N2", 100), CreateEdge("E2", "N3", "N4", 100)]
        });
        var disconnectedRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = "DISCONNECTED",
            Name = "不連續",
            Traversals =
            [
                new DirectedTrackTraversal("E1", TraversalDirection.Forward),
                new DirectedTrackTraversal("E2", TraversalDirection.Forward)
            ]
        };
        Throws<SimulationValidationException>(() => new RouteProjection(disconnected, disconnectedRoute), "不連續");

        var directional = new InfrastructureGraphV4(new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("N1", "一", TrackNodeKind.Boundary),
                new TrackNodeDefinition("N2", "二", TrackNodeKind.Boundary)
            ],
            Edges = [CreateEdge("FORWARD", "N1", "N2", 100)]
        });
        var reverseRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = "REVERSE",
            Name = "逆向違規",
            Traversals = [new DirectedTrackTraversal("FORWARD", TraversalDirection.Reverse)]
        };
        Throws<SimulationValidationException>(() => new RouteProjection(directional, reverseRoute), "不符合 edge 方向設定");
    }

    public static void RouteProjectionPreservesForwardChainageAndBoundaries()
    {
        var build = LinearInfrastructureBuilder.Build(CreateThreeStationRoute());
        var projection = new RouteProjection(build.Infrastructure, build.OutboundServiceRoute);
        var first = build.OutboundServiceRoute.Traversals[0].TrackEdgeId;
        var second = build.OutboundServiceRoute.Traversals[1].TrackEdgeId;

        NearlyEqual(3000, projection.TotalLengthMeters);
        NearlyEqual(500, projection.ToChainage(new TrackPosition(first, 500)));
        Equal(new TrackPosition(second, 0), projection.FromChainage(1000), "edge 邊界應歸屬下一段 traversal。");
        Equal(new TrackPosition(second, 2000), projection.FromChainage(3000), "末端 chainage 應保留在最後一段 edge。");
        True(!projection.FromChainage(0).TrackEdgeId.Equals(projection.FromChainage(3000).TrackEdgeId, StringComparison.OrdinalIgnoreCase),
            "route-local chainage 不能取代實體 edge identity。");
    }

    public static void RouteProjectionHandlesReverseTraversalAndRequiresTraversalIndexForLoops()
    {
        var infrastructure = new InfrastructureGraphV4(new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("N1", "一", TrackNodeKind.Boundary),
                new TrackNodeDefinition("N2", "二", TrackNodeKind.Boundary)
            ],
            Edges = [CreateEdge("E", "N1", "N2", 100, TrackDirectionality.Bidirectional)]
        });
        var reverseRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = "REVERSE",
            Name = "反向",
            Traversals = [new DirectedTrackTraversal("E", TraversalDirection.Reverse)]
        };
        var reverseProjection = new RouteProjection(infrastructure, reverseRoute);
        NearlyEqual(75, reverseProjection.ToChainage(new TrackPosition("E", 25)));
        Equal(new TrackPosition("E", 100), reverseProjection.FromChainage(0), "反向 traversal 起點應映射到 edge 終端 offset。");
        Equal(new TrackPosition("E", 0), reverseProjection.FromChainage(100), "反向 traversal 終點應映射到 edge 起點 offset。");

        var loopRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = "LOOP",
            Name = "重複 edge",
            Traversals =
            [
                new DirectedTrackTraversal("E", TraversalDirection.Forward),
                new DirectedTrackTraversal("E", TraversalDirection.Reverse)
            ]
        };
        var loopProjection = new RouteProjection(infrastructure, loopRoute);
        Throws<SimulationValidationException>(() => loopProjection.ToChainage(new TrackPosition("E", 25)), "出現多次");
        NearlyEqual(125, loopProjection.ToChainage(1, new TrackPosition("E", 75)));
    }

    public static void ResolvedStopsUsePlatformTrackPositionsInRouteOrder()
    {
        var build = LinearInfrastructureBuilder.Build(CreateThreeStationRoute());
        var outboundProjection = new RouteProjection(build.Infrastructure, build.OutboundServiceRoute);
        var inboundProjection = new RouteProjection(build.Infrastructure, build.InboundServiceRoute);
        var outbound = ResolvedStopResolver.Resolve(build.Infrastructure, build.OutboundServiceRoute, outboundProjection);
        var inbound = ResolvedStopResolver.Resolve(build.Infrastructure, build.InboundServiceRoute, inboundProjection);

        Equal("PLATFORM:O02:DOWN", outbound[1].PlatformId);
        Equal("EDGE:DOWN:O02:O03", outbound[1].Position.TrackEdgeId);
        Equal(1, outbound[1].TraversalIndex);
        NearlyEqual(0, outbound[1].Position.OffsetMeters);
        NearlyEqual(1000, outbound[1].ChainageMeters);

        Equal("PLATFORM:O02:UP", inbound[1].PlatformId);
        Equal("EDGE:UP:O03:O02", inbound[1].Position.TrackEdgeId);
        Equal(0, inbound[1].TraversalIndex);
        NearlyEqual(2000, inbound[1].Position.OffsetMeters);
        NearlyEqual(2000, inbound[1].ChainageMeters);
    }

    public static void SimulationWorldPublishesSynchronizedTopologyRuntimeMirror()
    {
        var world = new SimulationWorld(
            CreateThreeStationRoute(),
            new TrainParameters(22.222, 1, 1, 0, 0, 0),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent);

        var initial = world.GetSnapshot().Trains.Single();
        Equal("EDGE:DOWN:O01:O02", initial.TrackEdgeId!, "初始下行列車必須位於第一條 DOWN edge。");
        NearlyEqual(0, initial.OffsetMeters ?? double.NaN);
        Equal(0, initial.ServiceRouteTraversalIndex ?? -1, "初始 traversal index 必須為 0。");
        NearlyEqual(0, initial.ProjectedChainageMeters ?? double.NaN);

        world.AdvanceTo(4);
        var current = world.GetSnapshot().Trains.Single();
        var projection = world.GetRouteProjection(current.Direction);
        var position = new TrackPosition(current.TrackEdgeId!, current.OffsetMeters ?? double.NaN);
        NearlyEqual(current.ProjectedChainageMeters ?? double.NaN,
            projection.ToChainage(current.ServiceRouteTraversalIndex ?? -1, position));
        NearlyEqual(current.FrontPositionMeters, current.ProjectedChainageMeters ?? double.NaN);
        True(world.TopologyInfrastructure.ContainsPosition(position), "snapshot 必須輸出 edge 範圍內的 topology position。");

        var sample = world.Trajectory.Last();
        Equal(current.TrackEdgeId!, sample.TrackEdgeId!, "trajectory 的 edge 必須與 snapshot 同步。");
        NearlyEqual(current.OffsetMeters ?? double.NaN, sample.OffsetMeters ?? double.NaN);
        var departure = world.Events.Single(item => item.EventType == SimulationEventType.Departure);
        Equal("EDGE:DOWN:O01:O02", departure.TrackEdgeId!, "departure event 必須攜帶 topology edge。");
        NearlyEqual(0, departure.OffsetMeters ?? double.NaN);
    }

    public static void SimulationWorldAdvancesNormalMainlineByOrderedTraversal()
    {
        var world = new SimulationWorld(
            CreateThreeStationRoute(),
            new TrainParameters(22.222, 1, 1, 0, 0, 0),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent);

        // O01→O02 抵達、停站後，列車應已從第二條 edge 的 traversal cursor 再次出發。
        world.AdvanceTo(120);
        var state = world.GetSnapshot().Trains.Single();
        Equal("EDGE:DOWN:O02:O03", state.TrackEdgeId!, "正常主線跨站後必須切換至下一條 physical edge。");
        Equal(1, state.ServiceRouteTraversalIndex ?? -1, "正常主線跨站後必須遞增 traversal index。");
        True((state.OffsetMeters ?? 0) > 0, "第二條 edge 的 offset 應由 traversal movement 累積。");

        var projection = world.GetRouteProjection(state.Direction);
        var topologyPosition = new TrackPosition(state.TrackEdgeId!, state.OffsetMeters ?? double.NaN);
        NearlyEqual(
            state.ProjectedChainageMeters ?? double.NaN,
            projection.ToChainage(state.ServiceRouteTraversalIndex ?? -1, topologyPosition));
        NearlyEqual(state.FrontPositionMeters, state.ProjectedChainageMeters ?? double.NaN);
        True(world.Trajectory.Any(sample =>
                sample.TrackEdgeId == "EDGE:DOWN:O01:O02"
                && sample.ServiceRouteTraversalIndex == 0)
            && world.Trajectory.Any(sample =>
                sample.TrackEdgeId == "EDGE:DOWN:O02:O03"
                && sample.ServiceRouteTraversalIndex == 1),
            "軌跡應記錄由第一條 edge 過渡至第二條 edge 的有序 traversal。");
    }

    public static void SimulationWorldCanStartFromTopologyWithoutRouteInput()
    {
        var build = LinearInfrastructureBuilder.Build(CreateThreeStationRoute());
        var world = new SimulationWorld(
            new TopologySimulationDefinition(build.Infrastructure, build.OutboundServiceRoute, build.InboundServiceRoute),
            new TrainParameters(22.222, 1, 1, 0, 0, 0),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent);

        Equal(4, world.TopologyInfrastructure.Edges.Count);
        var state = world.GetSnapshot().Trains.Single();
        Equal("EDGE:DOWN:O01:O02", state.TrackEdgeId!);
        world.AdvanceTo(120);
        Equal("EDGE:DOWN:O02:O03", world.GetSnapshot().Trains.Single().TrackEdgeId!);
    }

    public static void PathFinderBuildsDeterministicConstrainedTraversal()
    {
        var infrastructure = new InfrastructureGraphV4(new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "A", TrackNodeKind.Boundary),
                new TrackNodeDefinition("J", "J", TrackNodeKind.Junction),
                new TrackNodeDefinition("C", "C", TrackNodeKind.Ordinary),
                new TrackNodeDefinition("D", "D", TrackNodeKind.Boundary)
            ],
            Resources = [new ConflictResourceDefinition("SW", "道岔", ConflictResourceKind.Switch)],
            Edges =
            [
                CreateEdge("A-J", "A", "J", 50),
                CreateEdge("J-D-SHORT", "J", "D", 20) with { ConflictResourceIds = new HashSet<string>(["SW"], StringComparer.OrdinalIgnoreCase) },
                CreateEdge("J-C", "J", "C", 40),
                CreateEdge("C-D", "C", "D", 40)
            ]
        });

        var shortest = TopologyPathFinder.FindShortestPath(infrastructure, "A", "D");
        Equal(2, shortest.Traversals.Count);
        Equal("A-J", shortest.Traversals[0].TrackEdgeId);
        Equal("J-D-SHORT", shortest.Traversals[1].TrackEdgeId);
        NearlyEqual(70, shortest.TotalLengthMeters);
        True(shortest.ConflictResourceIds.Contains("SW"), "path 應回報 traversal 引用的衝突資源。");

        var constrained = TopologyPathFinder.FindShortestPath(infrastructure, "A", "D", new TopologyPathConstraints
        {
            ExcludedEdgeIds = new HashSet<string>(["J-D-SHORT"], StringComparer.OrdinalIgnoreCase)
        });
        True(
            constrained.Traversals.Select(item => item.TrackEdgeId)
                .SequenceEqual(["A-J", "J-C", "C-D"], StringComparer.OrdinalIgnoreCase),
            "排除捷徑後應選擇唯一可用的有向 branch traversal。");
        NearlyEqual(130, constrained.TotalLengthMeters);
        Throws<SimulationValidationException>(() => TopologyPathFinder.FindShortestPath(infrastructure, "D", "A"), "找不到");
    }

    public static void DomainExtensionsValidateTopologyReferences()
    {
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "A", TrackNodeKind.Boundary),
                new TrackNodeDefinition("B", "B", TrackNodeKind.BufferStop)
            ],
            Resources = [new ConflictResourceDefinition("TAIL-LOCK", "尾軌鎖定", ConflictResourceKind.TailTrack)],
            Edges =
            [
                CreateEdge("MAIN", "A", "B", 100),
                CreateEdge("TAIL", "B", "A", 40) with { Kind = TrackEdgeKind.TailTrack }
            ],
            Stations = [new StationDefinitionV4 { StationId = "S", Name = "S", PlatformIds = ["P"] }],
            Platforms = [new PlatformDefinitionV4
            {
                PlatformId = "P", StationId = "S", Name = "月台", TrackEdgeId = "MAIN",
                PlatformStartOffsetMeters = 10, StopPositionOffsetMeters = 20,
                PlatformEndOffsetMeters = 80, EffectiveLengthMeters = 70
            }],
            Gradients = [new TrackGradientSegment("G", "MAIN", 0, 100, 8)],
            Curves = [new TrackCurveSegment("C", "MAIN", 10, 90, 350)],
            TurnbackFacilities = [new TurnbackFacilityDefinition
            {
                FacilityId = "F", Name = "尾軌", Kind = TurnbackFacilityKind.TailTrack,
                ArrivalTrackEdgeId = "MAIN", ArrivalStopOffsetMeters = 90,
                DepartureTrackEdgeId = "TAIL", DepartureStartOffsetMeters = 40,
                FacilityTrackEdgeIds = ["MAIN", "TAIL"],
                ConflictResourceIds = new HashSet<string>(["TAIL-LOCK"], StringComparer.OrdinalIgnoreCase)
            }],
            TurnbackOperations = [new TurnbackOperationDefinition
            {
                OperationId = "OP", FacilityId = "F", ArrivalServiceRouteId = "DOWN",
                DepartureServiceRouteId = "UP", MinimumDwellTimeSeconds = 30
            }],
            StationOperations = [new StationOperationDefinition
            {
                StationOperationId = "S-OP", StationId = "S", ArrivalPlatformIds = ["P"],
                DeparturePlatformIds = ["P"], TurnbackOperationIds = ["OP"], DefaultDwellTimeSeconds = 30
            }]
        };
        var down = new ServiceRouteDefinition
        {
            ServiceRouteId = "DOWN", Name = "下行", Traversals = [new DirectedTrackTraversal("MAIN", TraversalDirection.Forward)],
            Stops = [new ServiceRouteStop { StationId = "S", CandidatePlatformIds = ["P"] }]
        };
        var up = new ServiceRouteDefinition
        {
            ServiceRouteId = "UP", Name = "上行", Traversals = [new DirectedTrackTraversal("TAIL", TraversalDirection.Forward)]
        };

        InfrastructureValidator.ValidateAndThrow(definition, [down, up]);
        var graph = new InfrastructureGraphV4(definition);
        Equal(1, graph.Gradients.Count);
        Equal(1, graph.Curves.Count);
        Equal("F", graph.TurnbackOperations["OP"].FacilityId);

        var invalid = definition with { Curves = [new TrackCurveSegment("C", "MAIN", 10, 90, 0)] };
        var result = InfrastructureValidator.Validate(invalid, [down, up]);
        False(result.IsValid, "曲線半徑為零時必須拒絕 topology。 ");
        Contains(result.Errors, "半徑", "應回報曲線半徑錯誤。");
    }

    public static void Schema8TopologyProjectRoundTrips()
    {
        var source = CreateSchema8ProjectDocument();
        var json = TopologyProjectFormat.Serialize(source);
        True(json.Contains("\"schemaVersion\": 8", StringComparison.Ordinal), "Schema 8 存檔必須寫出版本。 ");
        True(json.Contains("\"topology\"", StringComparison.Ordinal), "Schema 8 存檔必須以 topology 為基礎設施權威。 ");
        True(!json.Contains("\"infrastructure\"", StringComparison.OrdinalIgnoreCase), "Schema 8 不得保存 legacy Infrastructure。 ");

        var restored = TopologyProjectFormat.Deserialize(json);
        Equal(TopologyProjectFormat.CurrentSchemaVersion, restored.SchemaVersion);
        Equal(4, restored.Topology.Edges.Count);
        Equal(1, restored.Topology.Gradients.Count);
        Equal(1, restored.Topology.Curves.Count);
        Equal("F", restored.Topology.TurnbackOperations.Single().FacilityId);
        Equal("DOWN", restored.DirectionRouteBindings.Single(item => item.Direction == TrainDirection.Outbound).ServiceRouteId);
        InfrastructureValidator.ValidateAndThrow(restored.Topology, restored.ServiceRoutes);
    }

    public static void Schema8RejectsLegacyOrWrongVersion()
    {
        var source = CreateSchema8ProjectDocument();
        var validJson = TopologyProjectFormat.Serialize(source);
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Deserialize("{\"schemaVersion\":8,\"stations\":[]}"),
            "legacy Route");
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Deserialize(validJson.Replace("\"schemaVersion\": 8", "\"schemaVersion\": 7", StringComparison.Ordinal)),
            "不支援存檔版本");
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Deserialize(validJson.Replace("\"serviceRoutes\"", "\"missingServiceRoutes\"", StringComparison.Ordinal)),
            "至少需要一條 ServiceRoute");
        var missingTurnbackStop = source with
        {
            Topology = source.Topology with
            {
                TurnbackFacilities =
                [
                    source.Topology.TurnbackFacilities.Single() with
                    {
                        TurnbackStopAfterTraversalIndex = null
                    }
                ]
            }
        };
        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Serialize(missingTurnbackStop),
            "TurnbackStopAfterTraversalIndex");
    }

    public static void Schema8TopologyBaselineSampleLoadsAndBuildsWorld()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        True(File.Exists(samplePath), "找不到 Schema 8 topology baseline 範例。 ");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        Equal(TopologyProjectFormat.CurrentSchemaVersion, document.SchemaVersion);
        Equal(15, document.Topology.Edges.Count);
        Equal(3, document.Topology.TurnbackFacilities.Count);
        Equal(1, document.Topology.PassingFacilities.Count);
        Equal(1, document.Topology.PassingOperations.Count);
        True(document.Topology.TurnbackFacilities.All(facility => facility.Traversals.Count > 0),
            "Schema 8 baseline 的每項折返設施都必須提供 runtime traversals。 ");
        True(document.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.TailTrack), "範例必須包含 tail track edge。 ");
        True(document.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.PocketTrack), "範例必須包含 pocket track edge。 ");
        True(document.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.PassingTrack), "範例必須包含實體 passing track edge。 ");
        foreach (var edgeId in new[]
        {
            "TAIL-OUT:ENTRY", "TAIL-OUT:EXIT", "TAIL-W-OUT:ENTRY", "TAIL-W-OUT:EXIT",
            "POCKET-OUT:ENTRY", "POCKET-OUT:EXIT"
        })
        {
            True(document.Topology.Edges.Any(edge => edge.TrackEdgeId == edgeId),
                $"baseline 必須包含實體設施接入／離開 edge「{edgeId}」。 ");
            True(document.Topology.DirectedConnections.Any(connection =>
                    connection.FromTrackEdgeId == edgeId || connection.ToTrackEdgeId == edgeId),
                $"baseline 的實體 edge「{edgeId}」必須有明確有向轉向參照。 ");
        }

        var graph = new InfrastructureGraphV4(document.Topology);
        var down = document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN");
        var up = document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP");
        var path = TopologyPathFinder.FindShortestPath(graph, "N-W", "N-E");
        True(path.Traversals.Select(item => item.TrackEdgeId).SequenceEqual(["DOWN-W-M", "DOWN-M-LOCAL", "DOWN-M-E"]),
            "baseline 主線 path 必須保留逐 edge 下行 traversal。 ");

        var world = new SimulationWorld(
            new TopologySimulationDefinition(graph, down, up),
            new TrainParameters(22.222, 1, 1, 0, 0, 0),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent);
        Equal("DOWN-W-M", world.GetSnapshot().Trains.Single().TrackEdgeId!);

        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var factoryWorld = new SimulationWorldOptions(
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
        Equal("DOWN-W-M", factoryWorld.GetSnapshot().Trains.Single().TrackEdgeId!);
        Throws<InvalidOperationException>(() => _ = factoryWorld.Route, "不提供 compatibility Route");
    }

    public static void AllSamplesLoadAndBuildTopologyWorld()
    {
        var sampleDirectory = Path.Combine(FindRepositoryRoot(), "samples");
        var samplePaths = Directory.EnumerateFiles(sampleDirectory, "*.mrtsim.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Equal(13, samplePaths.Length, "範例數量意外變更；新增範例時請同步更新此驗證。 ");

        foreach (var samplePath in samplePaths)
        {
            var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
            Equal(TopologyProjectFormat.CurrentSchemaVersion, document.SchemaVersion,
                $"範例必須使用 Schema {TopologyProjectFormat.CurrentSchemaVersion}：{Path.GetFileName(samplePath)}");
            True(document.Topology.DirectedConnections.Count > 0,
                $"範例必須明列有向 topology 連接：{Path.GetFileName(samplePath)}");
            True(document.Topology.TurnbackFacilities.All(facility =>
                    facility.TurnbackStopPosition is not null
                    && facility.TurnbackStopAfterTraversalIndex is null
                    && facility.FacilityTrackEdgeIds.Count == 0),
                $"折返範例必須使用 edge-local TurnbackStopPosition：{Path.GetFileName(samplePath)}");
            True((document.Dispatch.ManualTimetableRows ?? []).All(row =>
                    !row.ContinueAfterTerminal || !string.IsNullOrWhiteSpace(row.ContinuationServiceRunId)),
                $"範例的接續班次必須明列終點，避免自動產生無界循環：{Path.GetFileName(samplePath)}");

            var operationalEdgeIds = document.ServiceRoutes.SelectMany(route => route.Traversals)
                .Select(traversal => traversal.TrackEdgeId)
                .Concat(document.Topology.TurnbackFacilities.SelectMany(facility =>
                    facility.Traversals.Select(traversal => traversal.TrackEdgeId)
                        .Append(facility.ArrivalTrackEdgeId)
                        .Append(facility.DepartureTrackEdgeId)))
                .Concat(document.Topology.PassingFacilities.SelectMany(facility =>
                    facility.Traversals.Select(traversal => traversal.TrackEdgeId)
                        .Append(facility.ArrivalTrackEdgeId)
                        .Append(facility.DepartureTrackEdgeId)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            True(document.Topology.Edges.All(edge => operationalEdgeIds.Contains(edge.TrackEdgeId)),
                $"範例不可保留沒有 ServiceRoute 或營運設施會使用的孤立軌道：{Path.GetFileName(samplePath)}");

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
            world.AdvanceTo(3600);
            True(world.Events.Any(item => item.EventType == SimulationEventType.Departure),
                $"範例建立後未能正常推進：{Path.GetFileName(samplePath)}");
            var collisions = world.Events.Where(item => item.EventType == SimulationEventType.Collision).ToArray();
            True(collisions.Length == 0,
                $"範例完整運行不得發生碰撞：{Path.GetFileName(samplePath)}；"
                + string.Join("；", collisions.Select(item =>
                    $"t={item.SimulationTimeSeconds:0.0} {item.ServiceRunId}/{item.VehicleId} 與 {item.RelatedVehicleId} @ {item.TrackEdgeId}:{item.OffsetMeters:0.0}")));
            var stopViolations = world.Events.Where(item => item.EventType == SimulationEventType.StationStopViolation).ToArray();
            True(stopViolations.Length == 0,
                $"範例不得留下未能正常停站的事件：{Path.GetFileName(samplePath)}；"
                + string.Join("；", stopViolations.Select(item =>
                    $"t={item.SimulationTimeSeconds:0.0} {item.ServiceRunId}/{item.VehicleId} @ {item.TrackEdgeId}:{item.OffsetMeters:0.0} {item.Message}")));
            True(world.GetSnapshot().Trains.All(train => !train.IsActive),
                $"範例推進 3600 秒後不得殘留無界接續或卡住的列車：{Path.GetFileName(samplePath)}");

            if (Path.GetFileName(samplePath).Equals("V3.4.0-雙島四股快速車越行驗證.mrtsim.json", StringComparison.OrdinalIgnoreCase))
            {
                var overtakeCompleted = world.Events.Any(item => item.EventType == SimulationEventType.OvertakeCompleted
                    && item.ServiceRunId == "RUN-EXPRESS-001");
                var operatingEvents = world.Events.Where(item => item.ServiceRunId is "RUN-LOCAL-001" or "RUN-EXPRESS-001")
                    .Where(item => item.EventType is SimulationEventType.Departure
                        or SimulationEventType.Arrival
                        or SimulationEventType.DwellStarted
                        or SimulationEventType.StationPassed
                        or SimulationEventType.StationStopViolation
                        or SimulationEventType.OvertakeRequested
                        or SimulationEventType.OvertakeCompleted)
                    .Select(item => $"t={item.SimulationTimeSeconds:0.0} {item.EventType} {item.ServiceRunId} @ {item.TrackEdgeId}:{item.OffsetMeters:0.0}");
                True(overtakeCompleted,
                    "越行範例必須真的完成快速車超越，而不是只有 passing facility 靜態資料；"
                    + string.Join("；", operatingEvents));
                True(world.Trajectory.Any(item => item.ServiceRunId == "RUN-EXPRESS-001"
                        && item.TrackEdgeId == "D02-EXPRESS"),
                    "越行範例的快速車實際軌跡必須進入 D02-EXPRESS。 ");
            }

            if (Path.GetFileName(samplePath).Equals("V3.3.0-端點站前與中間站中央避車線折返檢核.mrtsim.json", StringComparison.OrdinalIgnoreCase))
            {
                True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                        && item.TrackEdgeId == "PO"),
                    "折返檢核範例必須真的駛入中央袋狀軌。 ");
                True(world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged
                        && item.ServiceRunId == "CASE-TAIL-UP"),
                    "折返檢核範例必須完成明列的尾軌接續車次。 ");
            }
        }
    }

    public static void TopologySimulationWorldDoesNotConstructCompatibilityRoute()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                new InfrastructureGraphV4(document.Topology),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent);

        // V2 可啟動並產生 topology cursor，但不持有 compatibility Route 或 legacy yard graph。
        True(world.GetSnapshot().Trains.Single().TrackEdgeId is not null,
            "topology world 必須能在無 Route 的情況下解析發車 TrackPosition。 ");
        Throws<InvalidOperationException>(() => _ = world.Route, "不提供 compatibility Route");
        Throws<InvalidOperationException>(() => _ = world.Infrastructure, "不提供 legacy InfrastructureGraph");
    }

    public static void ComprehensiveTopologySampleExercisesAllPhysicalFacilities()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-完整拓撲執行驗證範例.mrtsim.json");
        True(File.Exists(samplePath), "找不到完整 topology 執行驗證範例。 ");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));

        Equal(3, document.Topology.TurnbackFacilities.Count);
        Equal(1, document.Topology.PassingFacilities.Count);
        True(document.Topology.DirectedConnections.Count >= 18,
            "完整範例必須明確列出 switch／crossing 的有向轉向規則。 ");
        True(document.Topology.TurnbackFacilities.All(facility =>
                facility.TurnbackStopPosition is not null
                && facility.TurnbackStopAfterTraversalIndex is null),
            "完整範例的折返停點必須全部使用 edge-local TrackPosition。 ");

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

        world.AdvanceTo(3000);

        var demonstrationEvents = world.Events.Where(item => item.VehicleId == "POCKET-02").ToArray();
        True(demonstrationEvents.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "POCKET-M")
            && demonstrationEvents.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId == "POCKET-M"),
            "新增 POCKET-02 必須實際駛入中央袋狀軌並換端返回。");
        True(world.Trajectory.Any(item => item.VehicleId == "POCKET-02" && item.TrackEdgeId == "UP-M-W"),
            "新增 POCKET-02 折返後必須駛離袋狀軌進入上行線。");
        foreach (var item in demonstrationEvents.Where(item => item.EventType is SimulationEventType.TailTrackReached
                     or SimulationEventType.TailTrackReturnStarted))
            Console.WriteLine($"[待轉軌示範] POCKET-02 {item.EventType} {item.SimulationTimeSeconds:0.0}s {item.TrackEdgeId}");

        True(world.Events.Any(item => item.EventType == SimulationEventType.OvertakeCompleted
                && item.ServiceRunId == "EXPRESS-DOWN"
                && item.ResourceId == "FAC-PASS-M"),
            "快速車必須實際走過 PASS-M 並完成跨越。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "POCKET-M"),
            "中央折返車必須抵達 POCKET-M 的實體停等位置。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "E-TAIL"),
            "端點接續車必須駛入東端實體尾軌。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "W-TAIL"),
            "端點接續車必須駛入西端實體尾軌。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId is "POCKET-M" or "E-TAIL" or "W-TAIL"),
            "每個中段 edge-local 停等都必須由同一實體 edge 的反向 traversal 返回。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("RES-PASS-M") == true)
            && world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("RES-POCKET-M") == true)
            && world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("RES-E-TAIL") == true)
            && world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("RES-W-TAIL") == true),
            "passing、pocket 與雙端 tail resource 都必須等車尾淨空後釋放。 ");
        True(world.Events.All(item => item.TrackEdgeId is null
                || !item.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "完整範例不得產生 legacy virtual track identity。 ");
        True(world.Events.All(item => item.EventType != SimulationEventType.Collision),
            "完整範例在移動閉塞控制下不得發生碰撞。 ");
        True(world.GetSnapshot().Trains.All(train => !train.IsActive),
            "完整情境推進至 3000 秒後所有車次都應結束。 ");

        var resultContext = world.GetTopologyResultContext();
        var timetable = OperationsTimetable.Build(resultContext, runtime.DispatchPlan, [], world.Events);
        var intervals = IntervalStatistics.Analyze(resultContext, world.Trajectory, world.Events);
        var csv = TrajectoryAnalysis.BuildCsv(world.Trajectory, world.Events, 0);
        True(timetable.Any(item => item.ServiceRunId == "TAIL-DOWN" && item.StationId == "E"),
            "時刻表必須保留東端尾軌接續車次的實際到站。 ");
        True(intervals.AllIntervals.Any(item => item.ServiceRunId == "EXPRESS-DOWN"),
            "區間統計必須直接消費完整 topology world 的快速車軌跡。 ");
        Contains(csv.Split('\n'), "track_id", "CSV 必須保留實體 edge identity。 ");
        Throws<InvalidOperationException>(() => _ = world.Route, "不提供 compatibility Route");
    }

    public static void DirectedConnectionsRestrictSwitchPathsAndMovementPlans()
    {
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "進入", TrackNodeKind.Boundary),
                new TrackNodeDefinition("S", "道岔", TrackNodeKind.Switch),
                new TrackNodeDefinition("M", "正線出口", TrackNodeKind.Boundary),
                new TrackNodeDefinition("C", "渡線出口", TrackNodeKind.Boundary)
            ],
            Edges =
            [
                CreateEdge("APPROACH", "A", "S", 100),
                CreateEdge("MAIN", "S", "M", 100),
                CreateEdge("CROSS", "S", "C", 50) with { Kind = TrackEdgeKind.Crossover, DefaultSpeedLimitMetersPerSecond = 8 }
            ],
            DirectedConnections =
            [
                new DirectedTrackConnectionDefinition(
                    "APPROACH", TraversalDirection.Forward,
                    "CROSS", TraversalDirection.Forward)
            ]
        };
        var graph = new InfrastructureGraphV4(definition);
        var allowed = TopologyPathFinder.FindShortestPath(graph, "A", "C");
        Equal("CROSS", allowed.Traversals[^1].TrackEdgeId, "道岔白名單必須保留允許的 crossover path。");
        Throws<SimulationValidationException>(
            () => TopologyPathFinder.FindShortestPath(graph, "A", "M"),
            "找不到");

        var illegalRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = "ILLEGAL",
            Name = "非法正線轉向",
            Traversals =
            [
                new DirectedTrackTraversal("APPROACH", TraversalDirection.Forward),
                new DirectedTrackTraversal("MAIN", TraversalDirection.Forward)
            ]
        };
        Throws<SimulationValidationException>(
            () => _ = new TopologyRouteNavigator(graph, illegalRoute),
            "有向轉向限制");

        var movement = new TopologyMovementNavigator(graph, new ResolvedMovementPlan
        {
            MovementPlanId = "CROSSOVER",
            Legs =
            [
                new ResolvedMovementLeg
                {
                    LegId = "SWITCH",
                    Kind = MovementLegKind.Crossover,
                    Traversals = TopologyMovementPlanResolver.ResolveTraversals(graph,
                    [
                        new DirectedTrackTraversal("APPROACH", TraversalDirection.Forward),
                        new DirectedTrackTraversal("CROSS", TraversalDirection.Forward)
                    ])
                }
            ]
        });
        Equal("CROSS", movement.Advance(movement.CreateStartCursor(), 125).Position.TrackEdgeId,
            "runtime movement plan 必須沿允許的 crossover edge 前進。 ");
    }

    public static void TurnbackStopPositionUsesPhysicalOffsetAndImmediateReverse()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                graph,
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent);

        world.AdvanceTo(480);

        True(world.Trajectory.Any(sample => sample.TrackEdgeId == "TAIL-OUT"
                && Math.Abs(sample.OffsetMeters.GetValueOrDefault() - 160) < 0.001),
            "尾軌折返停點必須保留為 TAIL-OUT 的實體 offset，而非 edge endpoint 或 virtual node。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId == "TAIL-OUT"),
            "edge-local 折返停點應直接切換同一實體 edge 的反向 traversal。 ");
        True(world.Events.All(item => item.TrackEdgeId is null
                || !item.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "edge-local 折返不可回退至 legacy virtual track。 ");
    }

    public static void TopologyResultsUseResolvedStopsWithoutCompatibilityRoute()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var dispatch = new ResolvedDispatchPlan(
            DispatchPlanningMode.ManualTimetable,
            VehicleAssignmentMode.ExplicitOnly,
            TimeSpan.Zero,
            [new PlannedServiceRun(
                "RESULT-DOWN", TimeSpan.Zero, TrainDirection.Outbound, "RESULT-01", "DEFAULT_VEHICLE", "LOCAL", "ALL_STOP", "P-W-D", 0)]);
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                graph,
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent,
            dispatchPlan: dispatch);

        world.AdvanceTo(300);

        var context = world.GetTopologyResultContext();
        var timetable = OperationsTimetable.Build(context, dispatch, [], world.Events);
        var intervals = IntervalStatistics.Analyze(context, world.Trajectory, world.Events);
        var csv = TrajectoryAnalysis.BuildCsv(world.Trajectory, world.Events, 0);

        Equal("DOWN-W-M", context.GetStops(TrainDirection.Outbound)[0].Position.TrackEdgeId);
        True(timetable.Any(item => item.ServiceRunId == "RESULT-DOWN" && item.StationId == "M"),
            "時刻表必須使用 resolved topology stop 建立中央站列。 ");
        True(intervals.AllIntervals.Any(item => item.ServiceRunId == "RESULT-DOWN"),
            "區間統計必須可直接消費 topology result context。 ");
        Contains(csv.Split('\n'), "track_id", "CSV 必須由 topology trajectory 輸出，不需 Route。 ");
        Throws<InvalidOperationException>(() => _ = world.Route, "不提供 compatibility Route");
    }

    public static void TopologyRuntimeCursorFootprintOccupancyAndDistance()
    {
        var (infrastructure, route) = CreateShortEdgeTopology();
        var navigator = new TopologyRouteNavigator(infrastructure, route);
        var start = navigator.CreateStartCursor();
        var crossed = navigator.Advance(start, 30);
        Equal(2, crossed.TraversalIndex, "單一 tick 跨越兩條短 edge 後必須落在第三條 edge。 ");
        NearlyEqual(5, crossed.Position.OffsetMeters);

        var footprint = navigator.CreateFootprint(navigator.CreateCursor(1, 5), 12);
        Equal(0, footprint.Rear.TraversalIndex);
        NearlyEqual(3, footprint.Rear.Position.OffsetMeters);
        Equal(2, footprint.OccupiedIntervals.Count, "列車跨 edge 時車頭／車尾必須同時占用兩段 edge。 ");
        True(footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == "E1" && item.StartOffsetMeters == 3 && item.EndOffsetMeters == 10),
            "footprint 應保留第一條 edge 的車尾區間。 ");
        True(footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == "E2" && item.StartOffsetMeters == 0 && item.EndOffsetMeters == 5),
            "footprint 應保留第二條 edge 的車頭區間。 ");

        var platforms = TopologyPlatformOccupancy.GetOverlappingPlatforms(infrastructure, footprint);
        Equal("P", platforms.Single().PlatformId, "月台重疊必須依 footprint interval 判定。 ");

        var occupancy = new TrackOccupancyIndex();
        True(occupancy.TryUpdate("FOLLOWER", footprint, out _), "第一個列車 footprint 必須能占用軌道。 ");
        var blockingFootprint = navigator.CreateFootprint(navigator.CreateCursor(1, 7), 4);
        False(occupancy.TryUpdate("LEADER", blockingFootprint, out var blocker), "重疊 footprint 必須遭 occupancy 拒絕。 ");
        Equal("FOLLOWER", blocker!, "occupancy 應回報既有占用車輛。 ");
        var separateFootprint = navigator.CreateFootprint(navigator.CreateCursor(2, 6), 4);
        True(occupancy.TryUpdate("LEADER", separateFootprint, out _), "不重疊 edge interval 必須可同時占用。 ");

        var distance = TopologyGraphDistance.TryGetForwardDistance(
            infrastructure,
            navigator, navigator.CreateCursor(0, 5),
            navigator, navigator.CreateCursor(2, 2));
        NearlyEqual(22, distance ?? double.NaN);
    }

    public static void UnifiedMovementPlanUsesFacilityTraversalsAndRearClear()
    {
        var edges = new[]
        {
            CreateEdge("MAIN-IN", "A", "B", 100),
            CreateEdge("CROSS", "B", "C", 20) with { Kind = TrackEdgeKind.Crossover, ConflictResourceIds = new HashSet<string>(["LOCK:CROSS"], StringComparer.OrdinalIgnoreCase) },
            CreateEdge("TAIL", "C", "D", 30) with { Kind = TrackEdgeKind.TailTrack, ConflictResourceIds = new HashSet<string>(["LOCK:TAIL"], StringComparer.OrdinalIgnoreCase) },
            CreateEdge("RETURN", "D", "C", 30) with { Kind = TrackEdgeKind.TailTrack, ConflictResourceIds = new HashSet<string>(["LOCK:TAIL"], StringComparer.OrdinalIgnoreCase) },
            CreateEdge("MAIN-OUT", "C", "A", 100)
        };
        var facility = new TurnbackFacilityDefinition
        {
            FacilityId = "TURN", Name = "實體尾軌折返", Kind = TurnbackFacilityKind.TailTrack,
            ArrivalTrackEdgeId = "MAIN-IN", ArrivalStopOffsetMeters = 100,
            DepartureTrackEdgeId = "MAIN-OUT", DepartureStartOffsetMeters = 0,
            Traversals =
            [
                new DirectedTrackTraversal("CROSS", TraversalDirection.Forward),
                new DirectedTrackTraversal("TAIL", TraversalDirection.Forward),
                new DirectedTrackTraversal("RETURN", TraversalDirection.Forward)
            ],
            FacilityTrackEdgeIds = ["CROSS", "TAIL", "RETURN"],
            ConflictResourceIds = new HashSet<string>(["LOCK:CROSS", "LOCK:TAIL"], StringComparer.OrdinalIgnoreCase)
        };
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "A", TrackNodeKind.Boundary),
                new TrackNodeDefinition("B", "B", TrackNodeKind.Switch),
                new TrackNodeDefinition("C", "C", TrackNodeKind.Switch),
                new TrackNodeDefinition("D", "D", TrackNodeKind.BufferStop)
            ],
            Edges = edges,
            Resources =
            [
                new ConflictResourceDefinition("LOCK:CROSS", "渡線鎖定", ConflictResourceKind.Crossover),
                new ConflictResourceDefinition("LOCK:TAIL", "尾軌鎖定", ConflictResourceKind.TailTrack)
            ],
            TurnbackFacilities = [facility]
        };
        InfrastructureValidator.ValidateAndThrow(definition);
        var infrastructure = new InfrastructureGraphV4(definition);
        var facilityLeg = TopologyMovementPlanResolver.ResolveFacilityLeg(infrastructure, facility);
        var plan = new ResolvedMovementPlan
        {
            MovementPlanId = "RUN:TURN",
            Legs =
            [
                new ResolvedMovementLeg
                {
                    LegId = "MAIN-IN", Kind = MovementLegKind.ServiceRoute,
                    Traversals = TopologyMovementPlanResolver.ResolveTraversals(infrastructure,
                        [new DirectedTrackTraversal("MAIN-IN", TraversalDirection.Forward)])
                },
                facilityLeg,
                new ResolvedMovementLeg
                {
                    LegId = "MAIN-OUT", Kind = MovementLegKind.ServiceRoute,
                    Traversals = TopologyMovementPlanResolver.ResolveTraversals(infrastructure,
                        [new DirectedTrackTraversal("MAIN-OUT", TraversalDirection.Forward)])
                }
            ]
        };
        var navigator = new TopologyMovementNavigator(infrastructure, plan);
        var cursor = navigator.Advance(navigator.CreateStartCursor(), 160);
        Equal(1, cursor.MovementLegIndex, "車頭應在實體 facility leg 中，而不是 virtual track。 ");
        Equal("RETURN", cursor.Position.TrackEdgeId, "單一 tick 跨越 mainline、crossover 與 tail 後應到 return edge。 ");
        NearlyEqual(10, cursor.Position.OffsetMeters);

        var footprint = navigator.CreateFootprint(cursor, 60);
        True(footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == "CROSS"), "車尾仍在渡線時 footprint 必須保留渡線占用。 ");
        True(footprint.OccupiedIntervals.Any(item => item.TrackEdgeId == "TAIL"), "footprint 必須保留尾軌占用。 ");
        False(TopologyResourceReleasePolicy.CanReleaseResource("LOCK:CROSS", [footprint], infrastructure),
            "車尾未淨空渡線時不得提前釋放資源。 ");
        True(TraversalResourceResolver.GetRequiredResources(facilityLeg).SetEquals(["LOCK:CROSS", "LOCK:TAIL"]),
            "facility 資源必須由 traversal 與 facility 定義集中推導。 ");

        var cleared = navigator.CreateFootprint(navigator.CreateEndCursor(), 60);
        True(TopologyResourceReleasePolicy.CanReleaseResource("LOCK:CROSS", [cleared], infrastructure),
            "車尾離開受保護 edge 後應可釋放渡線資源。 ");
        True(TopologyResourceReleasePolicy.CanReleaseResource("LOCK:TAIL", [cleared], infrastructure),
            "車尾離開受保護 edge 後應可釋放尾軌資源。 ");
    }

    public static void SimulationWorldTurnsBackThroughTopologyFacilityWithoutVirtualTrack()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var down = document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN");
        var up = document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP");
        var world = new SimulationWorld(
            new TopologySimulationDefinition(graph, down, up),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            new OperationalParameters(
                jerkMetersPerSecondCubed: 1,
                coastingRatio: 0.1,
                approachDistanceMeters: 100,
                approachSpeedMetersPerSecond: 10,
                tractionFadeRatio: 0,
                trainLengthMeters: 100,
                serviceBrakingMetersPerSecondSquared: 1,
                emergencyBrakingMetersPerSecondSquared: 1.2,
                controlReactionTimeSeconds: 1,
                brakeBuildUpTimeSeconds: 1,
                positioningErrorMeters: 1,
                safetyMarginMeters: 20,
                absoluteMinimumGapMeters: 100),
            1,
            movingBlockMode: MovingBlockMode.Independent);

        world.AdvanceTo(480);

        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "TAIL-OUT"),
            "尾軌折返必須抵達 TAIL-OUT 實體 edge，而不是 virtual node。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId == "TAIL-OUT"),
            "尾軌折返停等後必須由實體 traversal 開始返回。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.Arrival
                && item.TrackEdgeId == "UP-E-M"),
            "返回後必須接續到 inbound service route 的實體起點。 ");
        True(world.Events.All(item => item.TrackEdgeId is null
                || !item.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "Schema 8 topology 折返的事件不可洩漏 legacy virtual track identity。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("RES-TAIL") == true),
            "列車車尾淨空尾軌後必須釋放 topology conflict resource。 ");
    }

    public static void SimulationWorldTurnsBackAtPocketTrackWithoutVirtualLocation()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                graph,
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent,
            servicePatterns:
            [
                new ServicePattern(
                    "ALL_STOP",
                    "中央避車線折返",
                    [new StationServiceInstruction("M", StationServiceMode.Turnback, DwellTimeSeconds: 0)])
            ]);

        world.AdvanceTo(360);

        True(world.Events.Any(item => item.EventType == SimulationEventType.TailTrackReached
                && item.TrackEdgeId == "POCKET-OUT"),
            "中央避車線折返必須實際停在 POCKET-OUT edge。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.Arrival
                && item.TrackEdgeId == "UP-M-W"),
            "袋狀軌返回後必須接續 UP-M-W，而非重設為 virtual position。 ");
        True(world.Events.All(item => item.TrackEdgeId is null
                || !item.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "袋狀軌折返全程不可使用 legacy virtual track。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("RES-POCKET") == true),
            "袋狀軌資源必須在車尾淨空後釋放。 ");
    }

    public static void ParallelPassingEdgesRemainSeparateForOccupancyAndGraphSafety()
    {
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "進入", TrackNodeKind.Boundary),
                new TrackNodeDefinition("B", "匯合", TrackNodeKind.Switch),
                new TrackNodeDefinition("C", "離開", TrackNodeKind.Boundary)
            ],
            Edges =
            [
                CreateEdge("LOCAL", "A", "B", 100),
                CreateEdge("PASS", "A", "B", 100) with { Kind = TrackEdgeKind.PassingTrack },
                CreateEdge("MERGE", "B", "C", 100) with { ConflictResourceIds = new HashSet<string>(["LOCK:MERGE"], StringComparer.OrdinalIgnoreCase) }
            ],
            Resources = [new ConflictResourceDefinition("LOCK:MERGE", "合流進路", ConflictResourceKind.Switch)]
        };
        var graph = new InfrastructureGraphV4(definition);
        var local = new TopologyMovementNavigator(graph, new ResolvedMovementPlan
        {
            MovementPlanId = "LOCAL-RUN",
            Legs = [new ResolvedMovementLeg
            {
                LegId = "LOCAL", Kind = MovementLegKind.ServiceRoute,
                Traversals = TopologyMovementPlanResolver.ResolveTraversals(graph,
                    [new DirectedTrackTraversal("LOCAL", TraversalDirection.Forward), new DirectedTrackTraversal("MERGE", TraversalDirection.Forward)])
            }]
        });
        var express = new TopologyMovementNavigator(graph, new ResolvedMovementPlan
        {
            MovementPlanId = "EXPRESS-RUN",
            Legs = [new ResolvedMovementLeg
            {
                LegId = "EXPRESS", Kind = MovementLegKind.Passing,
                Traversals = TopologyMovementPlanResolver.ResolveTraversals(graph,
                    [new DirectedTrackTraversal("PASS", TraversalDirection.Forward), new DirectedTrackTraversal("MERGE", TraversalDirection.Forward)])
            }]
        });
        var localFootprint = local.CreateFootprint(local.CreateCursor(0, 0, 60), 40);
        var expressFootprint = express.CreateFootprint(express.CreateCursor(0, 0, 60), 40);
        var occupancy = new TopologyMovementOccupancyIndex();
        Equal(0, occupancy.Set("LOCAL", localFootprint).Count);
        Equal(0, occupancy.Set("EXPRESS", expressFootprint).Count);
        True(TopologyGraphDistance.TryGetFootprintGap(graph, local, localFootprint, express, expressFootprint) is null,
            "平行 passing edge 的列車不可依相同投影里程誤算為前後車。 ");

        var mergeFootprint = express.CreateFootprint(express.CreateCursor(0, 1, 60), 40);
        True(mergeFootprint.OccupiedIntervals.Any(interval => interval.TrackEdgeId == "MERGE"),
            "express 匯入正線後 footprint 必須改由 MERGE edge 表示。 ");
        False(TopologyResourceReleasePolicy.CanReleaseResource("LOCK:MERGE", [mergeFootprint], graph),
            "車尾仍在合流 edge 時不得釋放合流資源。 ");
    }

    public static void SimulationWorldPassesLocalTrainThroughPhysicalTopologyFacility()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var dispatch = new ResolvedDispatchPlan(
            DispatchPlanningMode.ManualTimetable,
            VehicleAssignmentMode.ExplicitOnly,
            TimeSpan.Zero,
            [
                new PlannedServiceRun(
                    "LOCAL-DOWN", TimeSpan.Zero, TrainDirection.Outbound, "LOCAL-01", "DEFAULT_VEHICLE", "LOCAL", "ALL-STOPS", "P-W-D", 0),
                new PlannedServiceRun(
                    "EXPRESS-DOWN", TimeSpan.FromSeconds(30), TrainDirection.Outbound, "EXPRESS-01", "DEFAULT_VEHICLE", "EXPRESS", "EXPRESS-SKIP-M", "P-W-D", 1)
            ]);
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                graph,
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            2,
            movingBlockMode: MovingBlockMode.Control,
            servicePatterns:
            [
                new ServicePattern(
                    "ALL-STOPS",
                    "普通車各站停靠",
                    [
                        new StationServiceInstruction("W", StationServiceMode.Stop, DwellTimeSeconds: 20),
                        new StationServiceInstruction("M", StationServiceMode.Stop, DwellTimeSeconds: 120),
                        new StationServiceInstruction("E", StationServiceMode.Stop, DwellTimeSeconds: 30)
                    ]),
                new ServicePattern(
                    "EXPRESS-SKIP-M",
                    "快速車跨越中央站",
                    [
                        new StationServiceInstruction("W", StationServiceMode.Stop, DwellTimeSeconds: 20),
                        new StationServiceInstruction("M", StationServiceMode.Pass, SpeedLimitMetersPerSecond: 16.6666667),
                        new StationServiceInstruction("E", StationServiceMode.Stop, DwellTimeSeconds: 30)
                    ])
            ],
            dispatchPlan: dispatch);

        world.AdvanceTo(420);

        True(world.Events.Any(item => item.EventType == SimulationEventType.OvertakeRequested
                && item.ServiceRunId == "EXPRESS-DOWN"
                && item.ResourceId == "FAC-PASS-M"),
            "快速車接近停靠普通車時必須鎖定 topology passing facility。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.OvertakeCompleted
                && item.ServiceRunId == "EXPRESS-DOWN"
                && item.ResourceId == "FAC-PASS-M"),
            "快速車完成跨越時必須有 topology passing 完成事件。 ");
        True(world.Trajectory.Any(sample => sample.ServiceRunId == "EXPRESS-DOWN"
                && sample.TrackEdgeId == "PASS-LOOP-M"),
            "快速車的實際軌跡必須進入 PASS-LOOP-M 實體 edge。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased
                && item.ServiceRunId == "EXPRESS-DOWN"
                && item.ResourceIds?.Contains("RES-PASS-M") == true),
            "快速車的車尾淨空 passing edge 後才可釋放 RES-PASS-M。 ");
        True(world.Events.All(item => item.TrackEdgeId is null
                || !item.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "Schema 8 越行事件不可出現 legacy virtual track。 ");
        True(world.Events.All(item => item.EventType != SimulationEventType.Collision),
            "快速車匯回正線後，待避普通車必須等到合流安全淨空，不得追撞。 ");
        Throws<InvalidOperationException>(() => _ = world.Route, "不提供 compatibility Route");
    }

    public static void TopologyTurnbackPreservesVehicleAndActivatesContinuationRun()
    {
        var samplePath = Path.Combine(FindRepositoryRoot(), "samples", "V4.0.0-topology-baseline.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var graph = new InfrastructureGraphV4(document.Topology);
        var dispatch = new ResolvedDispatchPlan(
            DispatchPlanningMode.ManualTimetable,
            VehicleAssignmentMode.ExplicitOnly,
            TimeSpan.Zero,
            [
                new PlannedServiceRun(
                    "RUN-DOWN", TimeSpan.Zero, TrainDirection.Outbound, "EMU-01", "DEFAULT_VEHICLE", "LOCAL", "ALL_STOP", "P-W-D", 0,
                    continueAfterTerminal: true, continuationServiceRunId: "RUN-UP"),
                new PlannedServiceRun(
                    "RUN-UP", TimeSpan.FromSeconds(180), TrainDirection.Inbound, "EMU-01", "DEFAULT_VEHICLE", "LOCAL", "ALL_STOP", "P-E-U", 1)
            ]);
        var world = new SimulationWorld(
            new TopologySimulationDefinition(
                graph,
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN"),
                document.ServiceRoutes.Single(item => item.ServiceRouteId == "UP")),
            new TrainParameters(22.2222222, 1, 1, 20, 30, 30),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent,
            dispatchPlan: dispatch);

        world.AdvanceTo(480);

        True(world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged
                && item.VehicleId == "EMU-01"
                && item.ServiceRunId == "RUN-UP"),
            "實體 facility 折返後必須保留 VehicleId 並切換到指定 UP Run。 ");
        True(world.Events.Any(item => item.EventType == SimulationEventType.Departure
                && item.VehicleId == "EMU-01"
                && item.ServiceRunId == "RUN-UP"
                && item.TrackEdgeId == "UP-E-M"),
            "接續車次必須從實體 inbound edge 發車。 ");
        Equal(1, world.Trajectory.Select(item => item.VehicleId).Distinct(StringComparer.Ordinal).Count(),
            "實體折返接續不得產生第二個車輛實體。 ");
    }

    public static void SimulationWorldUsesTopologyFootprintsForNormalMainlineSafety()
    {
        var world = new SimulationWorld(
            CreateThreeStationRoute(),
            new TrainParameters(22.222, 1, 1, 0, 0, 0),
            OperationalParameters.CreateDefault(),
            2,
            initialDepartureIntervalSeconds: 20,
            movingBlockMode: MovingBlockMode.Control);

        world.AdvanceTo(65);
        True(world.TopologyOccupancy.Count >= 2, "正常主線的 active 列車應寫入 topology footprint occupancy。 ");
        True(world.TopologyOccupancy.Values.All(footprint =>
            !footprint.Front.Position.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "正常主線 footprint 不可退回 legacy virtual track identity。 ");
        True(world.SafetyHistory.Count > 0, "兩班正常主線列車應產生安全觀測。 ");
        True(world.SafetyHistory.Any(observation =>
            observation.TrackId.StartsWith("EDGE:DOWN:", StringComparison.OrdinalIgnoreCase)),
            "正常主線 safety observation 應以 topology edge identity 回報。 ");
    }

    public static void SimulationSessionOptionsCanConstructTopologyWorld()
    {
        var build = LinearInfrastructureBuilder.Build(CreateThreeStationRoute());
        var topology = new TopologySimulationDefinition(
            build.Infrastructure,
            build.OutboundServiceRoute,
            build.InboundServiceRoute);
        var world = new SimulationWorldOptions(
            Route: null,
            TrainParameters: new TrainParameters(22.222, 1, 1, 0, 0, 0),
            OperationalParameters: OperationalParameters.CreateDefault(),
            TrainCount: 1,
            MovingBlockMode: MovingBlockMode.Independent,
            Topology: topology).CreateWorld();

        Equal("EDGE:DOWN:O01:O02", world.GetSnapshot().Trains.Single().TrackEdgeId!);
    }

    public static void ProjectEditorStateIsTransactional()
    {
        var original = CreateSchema8ProjectDocument();
        var state = ProjectDocumentMapper.CreateEditorState(original);
        var changed = TopologyEditingService.AddTrackNode(state.Draft, "草稿節點");
        state.Replace(changed);

        False(original.Topology.Nodes.Any(node => node.Name == "草稿節點"),
            "Editor draft 不可在 Cancel 前污染原始 document。 ");
        var committed = state.Commit();
        True(committed.Topology.Nodes.Any(node => node.Name == "草稿節點"),
            "Commit 後才應回傳已套用的 Schema 8 document。 ");
    }

    public static void EdgeSplitRewritesPhysicalReferences()
    {
        var source = CreateSchema8ProjectDocument();
        var split = TopologyEditingService.SplitEdge(source, "A-B", 50);
        TopologyProjectFormat.Validate(split);

        False(split.Topology.Edges.Any(edge => edge.TrackEdgeId == "A-B"),
            "分割後不得保留原 edge 作為 physical authority。 ");
        var platform = split.Topology.Platforms.Single(item => item.PlatformId == "P2-D");
        True(platform.TrackEdgeId.StartsWith("A-B:B", StringComparison.Ordinal),
            "split 後位於右半段的月台必須改附著至右 edge。 ");
        NearlyEqual(50, platform.StopPositionOffsetMeters);
        Equal(2, split.Topology.SpeedLimits.Count,
            "跨越 split 點的速限必須拆成兩段 edge-local interval。 ");
        Equal(2, split.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN").Traversals.Count,
            "ServiceRoute 必須以相同方向與順序插入兩個 traversal。 ");
        True(split.Topology.Gradients.All(item => item.TrackEdgeId.StartsWith("A-B:", StringComparison.Ordinal)),
            "坡度必須改成 split 後 edge-local reference。 ");
    }

    public static void ReferencedTopologyObjectsCannotBeDeleted()
    {
        var source = CreateSchema8ProjectDocument();
        var references = TopologyDependencyAnalyzer.GetTrackEdgeReferences(source, "A-B");
        True(references.Any(reference => reference.Kind == "Platform"),
            "刪除前 dependency analyzer 必須列出附著於 edge 的月台。 ");
        Throws<SimulationValidationException>(
            () => TopologyEditingService.DeleteTrackEdge(source, "A-B"),
            "以下項目仍在使用");
        True(TopologyDependencyAnalyzer.GetPlatformReferences(source, "P1-D").Any(reference => reference.Kind == "Station"),
            "月台 dependency analyzer 必須列出其所屬車站的 PlatformIds reference。 ");
        Throws<SimulationValidationException>(
            () => TopologyEditingService.DeletePlatform(source, "P1-D"),
            "以下項目仍在使用");
    }

    public static void FacilityWizardsCreatePhysicalTopologyWithoutVirtualTracks()
    {
        var crossover = FacilityCreationService.CreateCrossover(
            CreateSchema8ProjectDocument(),
            new CrossoverFacilityRequest("東端橫渡線", "A", "B", 60, 7));
        TopologyProjectFormat.Validate(crossover);
        True(crossover.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.Crossover),
            "Crossover wizard 必須建立實體 Crossover edge。 ");
        True(crossover.Topology.Resources.Any(resource => resource.ResourceId.StartsWith("AUTO:RESOURCE:XOVER", StringComparison.Ordinal)),
            "Crossover wizard 必須建立衝突資源。 ");

        var tail = FacilityCreationService.CreateTailTrack(
            CreateSchema8ProjectDocument(),
            new TailFacilityRequest("東端新增尾軌", "B", "A-B", "B-A", 90, 8, 8));
        TopologyProjectFormat.Validate(tail);
        True(tail.Topology.TurnbackFacilities.Any(item => item.Name == "東端新增尾軌"),
            "Tail wizard 必須建立正式 TurnbackFacility。 ");
        True(tail.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.TailTrack),
            "Tail wizard 必須建立實體 TailTrack edge。 ");
        var tailFacility = tail.Topology.TurnbackFacilities.Single(item => item.Name == "東端新增尾軌");
        Equal(4, tailFacility.Traversals.Count, "Tail wizard 應建立實體進出渡線及同軌去回 traversal。 ");
        Equal(tailFacility.Traversals[1].TrackEdgeId, tailFacility.Traversals[2].TrackEdgeId);
        Equal(TraversalDirection.Forward, tailFacility.Traversals[1].Direction);
        Equal(TraversalDirection.Reverse, tailFacility.Traversals[2].Direction);
        Equal(TrackDirectionality.Bidirectional,
            tail.Topology.Edges.Single(edge => edge.TrackEdgeId == tailFacility.Traversals[1].TrackEdgeId).Directionality);

        var pocket = FacilityCreationService.CreatePocketTrack(
            CreateSchema8ProjectDocument(),
            new PocketFacilityRequest("東端袋狀軌", "B", "B", "A-B", "B-A", 120, 8, 60));
        TopologyProjectFormat.Validate(pocket);
        True(pocket.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.PocketTrack),
            "Pocket wizard 必須建立實體 PocketTrack edge。 ");
        var pocketFacility = pocket.Topology.TurnbackFacilities.Single(item => item.Name == "東端袋狀軌");
        Equal(4, pocketFacility.Traversals.Count, "Pocket wizard 必須建立進入、同軌反向與離開 traversal。 ");
        Equal(pocketFacility.Traversals[1].TrackEdgeId, pocketFacility.Traversals[2].TrackEdgeId);
        Equal(TraversalDirection.Forward, pocketFacility.Traversals[1].Direction);
        Equal(TraversalDirection.Reverse, pocketFacility.Traversals[2].Direction);
        Equal(TrackNodeKind.BufferStop,
            pocket.Topology.Nodes.Single(node => node.NodeId ==
                pocket.Topology.Edges.Single(edge => edge.TrackEdgeId == pocketFacility.Traversals[1].TrackEdgeId).ToNodeId).Kind);
        True(pocket.Topology.Edges.All(edge => !edge.TrackEdgeId.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase)),
            "Facility wizard 不可建立 legacy virtual track identity。 ");

        var invalidPocket = pocket with
        {
            Topology = pocket.Topology with
            {
                TurnbackFacilities = pocket.Topology.TurnbackFacilities.Select(facility =>
                    facility.FacilityId == pocketFacility.FacilityId
                        ? facility with { Traversals = [facility.Traversals[0], facility.Traversals[1], facility.Traversals[3]] }
                        : facility).ToArray()
            }
        };
        Throws<SimulationValidationException>(() => TopologyProjectFormat.Validate(invalidPocket), "中段停等位置後必須緊接同一實體 edge 的反向 traversal");
    }

    public static void QuickLinearBuilderCreatesExecutableTopologyProject()
    {
        var template = CreateSchema8ProjectDocument() with
        {
            Operations = CreateSchema8ProjectDocument().Operations with
            {
                ServiceBrakingMetersPerSecondSquared = 1,
                EmergencyBrakingMetersPerSecondSquared = 1.3
            }
        };
        var project = ProjectDocumentMapper.BuildQuickLinearProject(
            template,
            [
                new QuickLinearStationInput("A", "甲站", 0, 20),
                new QuickLinearStationInput("B", "乙站", 800, 30),
                new QuickLinearStationInput("C", "丙站", 1100, 25)
            ]);
        TopologyProjectFormat.Validate(project);
        var runtime = TopologyProjectFormat.CreateRuntime(project);
        var world = new SimulationWorld(
            runtime.Topology,
            runtime.TrainParameters,
            runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count,
            dispatchPlan: runtime.DispatchPlan,
            vehicleTypes: runtime.VehicleTypes,
            serviceTypes: runtime.ServiceTypes,
            servicePatterns: runtime.ServicePatterns);
        world.AdvanceTo(180);
        True(world.Trajectory.Count > 0, "快速建立的 Schema 8 project 必須可直接建立 V2 SimulationWorld。 ");
    }

    public static void ServiceRouteCandidatesStayOnPhysicalTraversal()
    {
        var source = CreateSchema8ProjectDocument();
        var route = source.ServiceRoutes.Single(item => item.ServiceRouteId == "DOWN");
        var candidates = ServiceRouteEditingService.GetCandidatePlatformIds(source, route, "S2");
        Equal(1, candidates.Count, "候選月台只能來自目前 ServiceRoute 通過的 edge。 ");
        Equal("P2-D", candidates[0]);
        False(candidates.Contains("P2-U", StringComparer.OrdinalIgnoreCase),
            "反向 edge 的月台不可出現在下行 ServiceRoute 的候選清單。 ");
    }

    public static void CatalogDeletesRespectReferences()
    {
        var source = CreateSchema8ProjectDocument();
        Throws<SimulationValidationException>(() => TopologyEditingService.DeleteVehicleType(source, "VT"), "以下項目仍在使用");
        Throws<SimulationValidationException>(() => TopologyEditingService.DeleteServiceType(source, "LOCAL"), "以下項目仍在使用");
        Throws<SimulationValidationException>(() => TopologyEditingService.DeleteStopPattern(source, "ALL"), "以下項目仍在使用");
    }

    public static void ValidationMetadataPreservesCompatibilityAndOwnerIdentity()
    {
        var legacy = new ProjectValidationMessage(ProjectValidationSeverity.Error, "舊介面", "A");
        Equal(ProjectValidationTargetKind.Unknown, legacy.TargetKind);
        True(legacy.FieldName is null, "既有三參數建構介面必須維持預設 metadata。 ");

        var source = CreateSchema8ProjectDocument();
        var missingReference = ProjectEditorValidationService.CreateError(
            "服務類型「LOCAL」引用不存在的車型「A」。", source);
        Equal(ProjectValidationTargetKind.ServiceType, missingReference.TargetKind);
        Equal("LOCAL", missingReference.TargetId!);
        Equal("DefaultVehicleType", missingReference.FieldName!);

        var unknown = ProjectEditorValidationService.CreateError("A 欄位發生未分類錯誤。", source);
        Equal(ProjectValidationTargetKind.Unknown, unknown.TargetKind);
        True(unknown.TargetId is null, "未分類文字不可因短 ID 子字串而猜測 target。 ");
        True(unknown.FieldName is null, "未分類文字不可猜測欄位。 ");
    }

    public static void ValidationTargetsCoverCatalogDispatchInfrastructureAndFacilities()
    {
        var source = CreateSchema8ProjectDocument();
        var invalid = source with
        {
            VehicleTypes = [source.VehicleTypes[0] with { DefaultStopPatternId = "MISSING-PATTERN" }],
            ServiceTypes = [source.ServiceTypes[0] with { DefaultVehicleTypeId = "A" }],
            StopPatterns = [source.StopPatterns[0] with
            {
                Instructions = [new ProjectStopPatternInstruction("MISSING-STATION", StopPatternAction.Stop)]
            }],
            Dispatch = source.Dispatch with
            {
                SimpleHeadwayPlans = [source.Dispatch.SimpleHeadwayPlans![0] with { ServiceTypeId = "MISSING-SERVICE" }],
                ManualTimetableRows = [new ProjectManualTimetableRow(60, TrainDirection.Inbound, "LOCAL", "MISSING-VEHICLE")]
            },
            Topology = source.Topology with
            {
                Edges = source.Topology.Edges.Select(edge => edge.TrackEdgeId == "A-B"
                    ? edge with { FromNodeId = "MISSING-NODE" }
                    : edge).ToArray(),
                Resources = [source.Topology.Resources[0] with { Name = "" }],
                SpeedLimits = [source.Topology.SpeedLimits[0] with { TrackEdgeId = "MISSING-EDGE" }],
                Gradients = [source.Topology.Gradients[0] with { GradePermille = double.NaN }],
                TurnbackFacilities = [source.Topology.TurnbackFacilities[0] with { Name = "" }]
            }
        };

        var messages = ProjectEditorValidationService.Validate(invalid);
        AssertTarget("軌道 edge「A-B」引用不存在的起點節點", ProjectValidationTargetKind.Edge, "A-B", "From");
        AssertTarget("衝突資源「LOCK」名稱", ProjectValidationTargetKind.Resource, "LOCK", "Name");
        AssertTarget("軌道速限「LIMIT」引用不存在的軌道 edge", ProjectValidationTargetKind.SpeedLimit, "LIMIT", "TrackEdge");
        AssertTarget("坡度區間「GRADE」坡度", ProjectValidationTargetKind.Gradient, "GRADE", "GradePermille");
        AssertTarget("車型「VT」引用不存在的停站模式", ProjectValidationTargetKind.VehicleType, "VT", "DefaultStopPattern");
        AssertTarget("服務類型「LOCAL」引用不存在的車型", ProjectValidationTargetKind.ServiceType, "LOCAL", "DefaultVehicleType");
        AssertTarget("停站模式「ALL」引用不存在的 topology 車站", ProjectValidationTargetKind.StopPattern, "ALL", "Station");
        AssertTarget("等間距派車引用不存在的服務類型", ProjectValidationTargetKind.HeadwayPlan, null, "ServiceType");
        AssertTarget("手動班表引用不存在的車型", ProjectValidationTargetKind.ManualTimetable, null, "VehicleType");
        AssertTarget("折返設施「F」名稱", ProjectValidationTargetKind.TurnbackFacility, "F", "Name");

        var passing = ProjectEditorValidationService.CreateError(
            "越行設施「PASS」引用不存在的車站「MISSING-STATION」。", source);
        Equal(ProjectValidationTargetKind.PassingFacility, passing.TargetKind);
        Equal("PASS", passing.TargetId!);

        void AssertTarget(
            string text,
            ProjectValidationTargetKind expectedKind,
            string? expectedId,
            string expectedField)
        {
            var message = messages.Single(item => item.Message.Contains(text, StringComparison.Ordinal));
            Equal(expectedKind, message.TargetKind);
            True(string.Equals(expectedId, message.TargetId, StringComparison.Ordinal),
                $"「{text}」應指向 owner {expectedId ?? "<page>"}。 ");
            Equal(expectedField, message.FieldName!);
        }
    }

    public static void RouteValidationUsesStructuredTargetMetadata()
    {
        var source = CreateSchema8ProjectDocument();
        var invalidRoute = source.ServiceRoutes.Single(route => route.ServiceRouteId == "DOWN") with { Name = "" };
        source = source with
        {
            ServiceRoutes = source.ServiceRoutes.Select(route => route.ServiceRouteId == "DOWN" ? invalidRoute : route).ToArray()
        };

        var message = ServiceRouteEditingService.ValidateRoute(source, "DOWN")
            .Single(item => item.Message.Contains("營運路線「DOWN」名稱", StringComparison.Ordinal));
        Equal(ProjectValidationTargetKind.ServiceRoute, message.TargetKind);
        Equal("DOWN", message.TargetId!);
        Equal("Name", message.FieldName!);
    }

    private static TopologyProjectDocument CreateSchema8ProjectDocument()
    {
        var infrastructure = new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "西端", TrackNodeKind.Boundary),
                new TrackNodeDefinition("B", "東端", TrackNodeKind.Boundary)
            ],
            Resources = [new ConflictResourceDefinition("LOCK", "折返鎖定", ConflictResourceKind.Crossover)],
            Edges =
            [
                CreateEdge("A-B", "A", "B", 100),
                CreateEdge("B-A", "B", "A", 100),
                CreateEdge("TAIL-OUT", "B", "A", 40) with { Kind = TrackEdgeKind.TailTrack },
                CreateEdge("TAIL-RETURN", "A", "B", 40) with { Kind = TrackEdgeKind.TailTrack }
            ],
            Stations =
            [
                new StationDefinitionV4 { StationId = "S1", Name = "西站", PlatformIds = ["P1-D", "P1-U"] },
                new StationDefinitionV4 { StationId = "S2", Name = "東站", PlatformIds = ["P2-D", "P2-U"] }
            ],
            Platforms =
            [
                CreatePlatform("P1-D", "S1", "A-B", 0), CreatePlatform("P2-D", "S2", "A-B", 100),
                CreatePlatform("P2-U", "S2", "B-A", 0), CreatePlatform("P1-U", "S1", "B-A", 100)
            ],
            SpeedLimits = [new TrackSpeedLimitDefinition { SpeedLimitId = "LIMIT", TrackEdgeId = "A-B", StartOffsetMeters = 0, EndOffsetMeters = 100, LimitMetersPerSecond = 20 }],
            Gradients = [new TrackGradientSegment("GRADE", "A-B", 0, 100, 8)],
            Curves = [new TrackCurveSegment("CURVE", "A-B", 10, 90, 350)],
            TurnbackFacilities = [new TurnbackFacilityDefinition
            {
                FacilityId = "F", Name = "端點尾軌", Kind = TurnbackFacilityKind.TailTrack,
                ArrivalTrackEdgeId = "A-B", ArrivalStopOffsetMeters = 100,
                DepartureTrackEdgeId = "B-A", DepartureStartOffsetMeters = 0,
                Traversals =
                [
                    new DirectedTrackTraversal("TAIL-OUT", TraversalDirection.Forward),
                    new DirectedTrackTraversal("TAIL-RETURN", TraversalDirection.Forward)
                ],
                TurnbackStopAfterTraversalIndex = 0,
                FacilityTrackEdgeIds = ["TAIL-OUT", "TAIL-RETURN"],
                ConflictResourceIds = new HashSet<string>(["LOCK"], StringComparer.OrdinalIgnoreCase)
            }],
            TurnbackOperations = [new TurnbackOperationDefinition
            {
                OperationId = "TURN", FacilityId = "F", ArrivalServiceRouteId = "DOWN",
                DepartureServiceRouteId = "UP", MinimumDwellTimeSeconds = 30
            }],
            StationOperations =
            [
                new StationOperationDefinition { StationOperationId = "S1-OP", StationId = "S1", ArrivalPlatformIds = ["P1-U"], DeparturePlatformIds = ["P1-D"], DefaultDwellTimeSeconds = 20 },
                new StationOperationDefinition { StationOperationId = "S2-OP", StationId = "S2", ArrivalPlatformIds = ["P2-D"], DeparturePlatformIds = ["P2-U"], TurnbackOperationIds = ["TURN"], DefaultDwellTimeSeconds = 30 }
            ]
        };
        var down = new ServiceRouteDefinition
        {
            ServiceRouteId = "DOWN", Name = "下行", Traversals = [new DirectedTrackTraversal("A-B", TraversalDirection.Forward)],
            Stops = [new ServiceRouteStop { StationId = "S1", CandidatePlatformIds = ["P1-D"] }, new ServiceRouteStop { StationId = "S2", CandidatePlatformIds = ["P2-D"] }]
        };
        var up = new ServiceRouteDefinition
        {
            ServiceRouteId = "UP", Name = "上行", Traversals = [new DirectedTrackTraversal("B-A", TraversalDirection.Forward)],
            Stops = [new ServiceRouteStop { StationId = "S2", CandidatePlatformIds = ["P2-U"] }, new ServiceRouteStop { StationId = "S1", CandidatePlatformIds = ["P1-U"] }]
        };

        return new TopologyProjectDocument(
            TopologyProjectFormat.CurrentSchemaVersion,
            "V4-BASELINE",
            "Schema 8 topology 基線",
            new ProjectTrainSettings(22.222, 1, 1, 30, 30, 30),
            new ProjectOperationalSettings(1, 0.1, 100, 10, 0, 100, 100, 1, 1, 1, 1, 1, 100),
            new ProjectRunSettings(1, null, 0, 1, OperationProfileMode.RealisticOperations, MovingBlockMode.Independent, BrakingEstimationMode.Service),
            [new ProjectVehicleType("VT", "測試車", 100, 22.222, 1, 1, 1, 1, 0, 0, "ALL")],
            [new ProjectServiceType("LOCAL", "普通車", "#0066CC", "L", "ALL", "VT")],
            [new ProjectStopPattern("ALL", "各站停靠", [new ProjectStopPatternInstruction("S1", StopPatternAction.Stop), new ProjectStopPatternInstruction("S2", StopPatternAction.Stop)])],
            new ProjectDispatchPlan(DispatchPlanningMode.SimpleHeadway, VehicleAssignmentMode.Automatic,
                [new ProjectHeadwayPlan(TrainDirection.Outbound, 0, 300, 1, "LOCAL", "VT", "ALL", "P1-D")], []),
            infrastructure,
            [down, up],
            [new TopologyDirectionRouteBinding(TrainDirection.Outbound, "DOWN"), new TopologyDirectionRouteBinding(TrainDirection.Inbound, "UP")]);
    }

    private static PlatformDefinitionV4 CreatePlatform(string id, string stationId, string edgeId, double offset) => new()
    {
        PlatformId = id,
        StationId = stationId,
        Name = id,
        TrackEdgeId = edgeId,
        PlatformStartOffsetMeters = offset,
        StopPositionOffsetMeters = offset,
        PlatformEndOffsetMeters = offset,
        EffectiveLengthMeters = 100
    };

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx"))) return directory.FullName;
        }

        throw new InvalidOperationException("找不到專案根目錄。");
    }

    private static (InfrastructureGraphV4 Infrastructure, ServiceRouteDefinition Route) CreateShortEdgeTopology()
    {
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("A", "A", TrackNodeKind.Boundary),
                new TrackNodeDefinition("B", "B", TrackNodeKind.Ordinary),
                new TrackNodeDefinition("C", "C", TrackNodeKind.Ordinary),
                new TrackNodeDefinition("D", "D", TrackNodeKind.Boundary)
            ],
            Edges = [CreateEdge("E1", "A", "B", 10), CreateEdge("E2", "B", "C", 15), CreateEdge("E3", "C", "D", 8)],
            Stations = [new StationDefinitionV4 { StationId = "S", Name = "S", PlatformIds = ["P"] }],
            Platforms = [new PlatformDefinitionV4
            {
                PlatformId = "P", StationId = "S", Name = "短 edge 月台", TrackEdgeId = "E2",
                PlatformStartOffsetMeters = 2, StopPositionOffsetMeters = 6,
                PlatformEndOffsetMeters = 10, EffectiveLengthMeters = 8
            }]
        };
        var route = new ServiceRouteDefinition
        {
            ServiceRouteId = "SHORT", Name = "短 edge 路徑",
            Traversals =
            [
                new DirectedTrackTraversal("E1", TraversalDirection.Forward),
                new DirectedTrackTraversal("E2", TraversalDirection.Forward),
                new DirectedTrackTraversal("E3", TraversalDirection.Forward)
            ]
        };
        return (new InfrastructureGraphV4(definition), route);
    }

    private static TrackEdgeDefinition CreateEdge(
        string id,
        string fromNodeId,
        string toNodeId,
        double lengthMeters,
        TrackDirectionality directionality = TrackDirectionality.ForwardOnly) => new()
        {
            TrackEdgeId = id,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            LengthMeters = lengthMeters,
            Directionality = directionality,
            Kind = TrackEdgeKind.Mainline,
            DefaultSpeedLimitMetersPerSecond = 20
        };

    private static Route CreateThreeStationRoute() => RouteFactory.FromSegmentDistances(
        "O",
        "橘色測試線",
        [
            new StationInput("O01", "起點站", 0, 0),
            new StationInput("O02", "中央站", 1000, 30),
            new StationInput("O03", "終點站", 2000, 0)
        ],
        30);

    private static void NearlyEqual(double expected, double actual, double tolerance = 0.000001)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"預期 {expected:0.########}，實際 {actual:0.########}，容差 {tolerance}。");
        }
    }

    private static void Equal<T>(T expected, T actual, string message = "")
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} 預期 {expected}，實際 {actual}。");
        }
    }

    private static void False(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Contains(IEnumerable<string> values, string expected, string message)
    {
        if (!values.Any(value => value.Contains(expected, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<TException>(Action action, string expectedMessage)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            if (exception.Message.Contains(expectedMessage, StringComparison.Ordinal)) return;
            throw new InvalidOperationException($"錯誤訊息未包含「{expectedMessage}」：{exception.Message}");
        }

        throw new InvalidOperationException($"預期拋出 {typeof(TException).Name}。");
    }
}
