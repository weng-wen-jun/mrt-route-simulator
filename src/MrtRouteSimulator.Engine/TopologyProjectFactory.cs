namespace MrtRouteSimulator.Engine;

/// <summary>將既有 Schema 7 線性規劃資料轉成可在 Schema 8 topology editor 繼續編輯的起稿。</summary>
public static class TopologyProjectFactory
{
    /// <summary>
    /// 僅供舊表單／V1 adapter 於進入 V2 前建立一次 topology 起稿。回傳值本身不保留
    /// Route，V2 world 後續只接受這份實體 graph 與 ServiceRoute。
    /// </summary>
    public static TopologySimulationDefinition CreateLinearRuntimeTopology(
        Route route,
        TrainParameters train,
        IEnumerable<SpeedLimitSegment>? speedLimits = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(train);
        var build = LinearInfrastructureBuilder.Build(route, train.MaxSpeedMetersPerSecond);
        var limits = TrackSpeedLimitService.ProjectLegacyLimits(
            new RouteProjection(build.Infrastructure, build.OutboundServiceRoute),
            new RouteProjection(build.Infrastructure, build.InboundServiceRoute),
            speedLimits ?? []);
        var topology = CreateLinearTopologyWithPhysicalTerminalTurnbacks(
            build,
            train.OriginTurnaroundTimeSeconds,
            train.TerminalTurnaroundTimeSeconds,
            limits);
        return new TopologySimulationDefinition(
            new InfrastructureGraphV4(topology),
            build.OutboundServiceRoute,
            build.InboundServiceRoute);
    }

    public static TopologyProjectDocument CreateLinearDraft(SimulationProjectDocument legacyProject)
    {
        ArgumentNullException.ThrowIfNull(legacyProject);
        if (legacyProject.SchemaVersion != SimulationProjectFormat.CurrentSchemaVersion)
        {
            throw new SimulationValidationException([
                $"只能由 Schema {SimulationProjectFormat.CurrentSchemaVersion} 建立 topology 起稿。"
            ]);
        }
        var vehicleTypes = legacyProject.VehicleTypes
            ?? throw new SimulationValidationException(["Schema 7 起稿缺少車型目錄。"]);
        var serviceTypes = legacyProject.ServiceTypes
            ?? throw new SimulationValidationException(["Schema 7 起稿缺少服務類型目錄。"]);
        var stopPatterns = legacyProject.StopPatterns
            ?? throw new SimulationValidationException(["Schema 7 起稿缺少停站模式目錄。"]);
        var dispatch = legacyProject.Dispatch
            ?? throw new SimulationValidationException(["Schema 7 起稿缺少發車計畫。"]);

        var route = RouteFactory.FromSegmentDistances(
            legacyProject.RouteId,
            legacyProject.RouteName,
            legacyProject.Stations.Select(station => new StationInput(
                station.StationId,
                station.StationName,
                station.DistanceFromPreviousMeters,
                station.DwellTimeSeconds)),
            legacyProject.Train.DefaultDwellTimeSeconds);
        var build = LinearInfrastructureBuilder.Build(route, legacyProject.Train.MaxSpeedMetersPerSecond);
        var legacyLimits = legacyProject.SpeedLimits.Select(limit => new SpeedLimitSegment(
            limit.StartPositionMeters,
            limit.EndPositionMeters,
            limit.LimitMetersPerSecond,
            limit.Direction,
            limit.Note));
        var edgeLimits = TrackSpeedLimitService.ProjectLegacyLimits(
            new RouteProjection(build.Infrastructure, build.OutboundServiceRoute),
            new RouteProjection(build.Infrastructure, build.InboundServiceRoute),
            legacyLimits);
        var topology = CreateLinearTopologyWithPhysicalTerminalTurnbacks(
            build,
            legacyProject.Train.OriginTurnaroundTimeSeconds,
            legacyProject.Train.TerminalTurnaroundTimeSeconds,
            edgeLimits);

        return new TopologyProjectDocument(
            TopologyProjectFormat.CurrentSchemaVersion,
            legacyProject.RouteId,
            legacyProject.RouteName,
            legacyProject.Train,
            legacyProject.Operations,
            legacyProject.Simulation,
            vehicleTypes,
            serviceTypes,
            stopPatterns,
            RemapDispatchOriginPlatforms(dispatch, build),
            topology,
            [build.OutboundServiceRoute, build.InboundServiceRoute],
            [
                new TopologyDirectionRouteBinding(TrainDirection.Outbound, build.OutboundServiceRoute.ServiceRouteId),
                new TopologyDirectionRouteBinding(TrainDirection.Inbound, build.InboundServiceRoute.ServiceRouteId)
            ]);
    }

    /// <summary>
    /// 線性表單轉成 Schema 8 時，兩端也必須補成真正可走的尾軌折返；不能因為沒有舊
    /// virtual node 就把續行車退回 Route position。ServiceRoute 仍只描述載客服務主線，
    /// 折返 traversal 由 facility operation 於端點銜接。
    /// </summary>
    private static TopologyInfrastructureDefinition CreateLinearTopologyWithPhysicalTerminalTurnbacks(
        LinearInfrastructureBuildResult build,
        double originTurnaroundTimeSeconds,
        double terminalTurnaroundTimeSeconds,
        IReadOnlyList<TrackSpeedLimitDefinition> edgeLimits)
    {
        var nodes = build.Infrastructure.Nodes.Values.ToList();
        var edges = build.Infrastructure.Edges.Values.ToList();
        var resources = build.Infrastructure.Resources.Values.ToList();
        var directedConnections = build.Infrastructure.DirectedConnections.ToList();
        var facilities = new List<TurnbackFacilityDefinition>();
        var operations = new List<TurnbackOperationDefinition>();
        var stationOperations = new List<StationOperationDefinition>();
        var outboundTerminal = build.OutboundServiceRoute.Stops[^1].StationId;
        var inboundTerminal = build.InboundServiceRoute.Stops[^1].StationId;

        AddRouteConnections(build.OutboundServiceRoute);
        AddRouteConnections(build.InboundServiceRoute);

        AddTerminalFacility(
            terminalStationId: outboundTerminal,
            terminalStationName: build.Infrastructure.Stations[outboundTerminal].Name,
            suffix: "DOWN-END",
            arrivalTraversal: build.OutboundServiceRoute.Traversals[^1],
            arrivalOffset: build.Infrastructure.GetRequiredEdge(build.OutboundServiceRoute.Traversals[^1].TrackEdgeId).LengthMeters,
            departureTraversal: build.InboundServiceRoute.Traversals[0],
            arrivalServiceRouteId: build.OutboundServiceRoute.ServiceRouteId,
            departureServiceRouteId: build.InboundServiceRoute.ServiceRouteId,
            arrivalPlatformId: $"PLATFORM:{outboundTerminal}:DOWN",
            departurePlatformId: $"PLATFORM:{outboundTerminal}:UP",
            minimumDwellTimeSeconds: terminalTurnaroundTimeSeconds);
        AddTerminalFacility(
            terminalStationId: inboundTerminal,
            terminalStationName: build.Infrastructure.Stations[inboundTerminal].Name,
            suffix: "UP-END",
            arrivalTraversal: build.InboundServiceRoute.Traversals[^1],
            arrivalOffset: build.Infrastructure.GetRequiredEdge(build.InboundServiceRoute.Traversals[^1].TrackEdgeId).LengthMeters,
            departureTraversal: build.OutboundServiceRoute.Traversals[0],
            arrivalServiceRouteId: build.InboundServiceRoute.ServiceRouteId,
            departureServiceRouteId: build.OutboundServiceRoute.ServiceRouteId,
            arrivalPlatformId: $"PLATFORM:{inboundTerminal}:UP",
            departurePlatformId: $"PLATFORM:{inboundTerminal}:DOWN",
            minimumDwellTimeSeconds: originTurnaroundTimeSeconds);

        return new TopologyInfrastructureDefinition
        {
            Nodes = nodes,
            Edges = edges,
            Stations = build.Infrastructure.Stations.Values.ToArray(),
            Platforms = build.Infrastructure.Platforms.Values.ToArray(),
            Resources = resources,
            SpeedLimits = edgeLimits.ToArray(),
            DirectedConnections = directedConnections,
            TurnbackFacilities = facilities,
            TurnbackOperations = operations,
            StationOperations = stationOperations
        };

        void AddTerminalFacility(
            string terminalStationId,
            string terminalStationName,
            string suffix,
            DirectedTrackTraversal arrivalTraversal,
            double arrivalOffset,
            DirectedTrackTraversal departureTraversal,
            string arrivalServiceRouteId,
            string departureServiceRouteId,
            string arrivalPlatformId,
            string departurePlatformId,
            double minimumDwellTimeSeconds)
        {
            var terminalNodeId = $"NODE:{terminalStationId}";
            var crossoverNodeId = $"NODE:CROSSOVER:{suffix}";
            var bufferNodeId = $"NODE:TAIL:{suffix}";
            var crossoverOutEdgeId = $"EDGE:CROSSOVER:{suffix}:OUT";
            var crossoverReturnEdgeId = $"EDGE:CROSSOVER:{suffix}:RETURN";
            var outboundEdgeId = $"EDGE:TAIL:{suffix}:OUT";
            var resourceId = $"RESOURCE:TAIL:{suffix}";
            var switchResourceId = $"RESOURCE:SWITCH:{suffix}";
            var facilityId = $"FACILITY:TAIL:{suffix}";
            var operationId = $"TURNBACK:{suffix}";
            nodes.Add(new TrackNodeDefinition(crossoverNodeId, $"{terminalStationName} 尾軌渡線", TrackNodeKind.Switch));
            nodes.Add(new TrackNodeDefinition(bufferNodeId, $"{terminalStationName} 尾軌止衝", TrackNodeKind.BufferStop));
            resources.Add(new ConflictResourceDefinition(resourceId, $"{terminalStationName} 尾軌", ConflictResourceKind.TailTrack));
            resources.Add(new ConflictResourceDefinition(switchResourceId, $"{terminalStationName} 尾軌渡線", ConflictResourceKind.Crossover));
            edges.Add(new TrackEdgeDefinition
            {
                TrackEdgeId = crossoverOutEdgeId,
                FromNodeId = terminalNodeId,
                ToNodeId = crossoverNodeId,
                LengthMeters = 30,
                Directionality = TrackDirectionality.ForwardOnly,
                Kind = TrackEdgeKind.Crossover,
                DefaultSpeedLimitMetersPerSecond = 25d / 3.6d,
                ConflictResourceIds = new HashSet<string>([switchResourceId], StringComparer.OrdinalIgnoreCase)
            });
            edges.Add(new TrackEdgeDefinition
            {
                TrackEdgeId = outboundEdgeId,
                FromNodeId = crossoverNodeId,
                ToNodeId = bufferNodeId,
                LengthMeters = 120,
                Directionality = TrackDirectionality.Bidirectional,
                Kind = TrackEdgeKind.TailTrack,
                DefaultSpeedLimitMetersPerSecond = 25d / 3.6d,
                ConflictResourceIds = new HashSet<string>([resourceId], StringComparer.OrdinalIgnoreCase)
            });
            edges.Add(new TrackEdgeDefinition
            {
                TrackEdgeId = crossoverReturnEdgeId,
                FromNodeId = crossoverNodeId,
                ToNodeId = terminalNodeId,
                LengthMeters = 30,
                Directionality = TrackDirectionality.ForwardOnly,
                Kind = TrackEdgeKind.Crossover,
                DefaultSpeedLimitMetersPerSecond = 25d / 3.6d,
                ConflictResourceIds = new HashSet<string>([switchResourceId], StringComparer.OrdinalIgnoreCase)
            });
            facilities.Add(new TurnbackFacilityDefinition
            {
                FacilityId = facilityId,
                Name = $"{terminalStationName} 實體尾軌折返",
                Kind = TurnbackFacilityKind.TailTrack,
                ArrivalTrackEdgeId = arrivalTraversal.TrackEdgeId,
                ArrivalStopOffsetMeters = arrivalOffset,
                DepartureTrackEdgeId = departureTraversal.TrackEdgeId,
                DepartureStartOffsetMeters = 0,
                Traversals = [
                    new DirectedTrackTraversal(crossoverOutEdgeId, TraversalDirection.Forward),
                    new DirectedTrackTraversal(outboundEdgeId, TraversalDirection.Forward),
                    new DirectedTrackTraversal(outboundEdgeId, TraversalDirection.Reverse),
                    new DirectedTrackTraversal(crossoverReturnEdgeId, TraversalDirection.Forward)
                ],
                TurnbackStopPosition = new TrackPosition(outboundEdgeId, 100),
                ConflictResourceIds = new HashSet<string>([resourceId, switchResourceId], StringComparer.OrdinalIgnoreCase)
            });
            directedConnections.Add(new DirectedTrackConnectionDefinition(
                arrivalTraversal.TrackEdgeId, arrivalTraversal.Direction,
                crossoverOutEdgeId, TraversalDirection.Forward));
            directedConnections.Add(new DirectedTrackConnectionDefinition(
                crossoverOutEdgeId, TraversalDirection.Forward,
                outboundEdgeId, TraversalDirection.Forward));
            directedConnections.Add(new DirectedTrackConnectionDefinition(
                outboundEdgeId, TraversalDirection.Forward,
                outboundEdgeId, TraversalDirection.Reverse));
            directedConnections.Add(new DirectedTrackConnectionDefinition(
                outboundEdgeId, TraversalDirection.Reverse,
                crossoverReturnEdgeId, TraversalDirection.Forward));
            directedConnections.Add(new DirectedTrackConnectionDefinition(
                crossoverReturnEdgeId, TraversalDirection.Forward,
                departureTraversal.TrackEdgeId, departureTraversal.Direction));
            operations.Add(new TurnbackOperationDefinition
            {
                OperationId = operationId,
                FacilityId = facilityId,
                ArrivalServiceRouteId = arrivalServiceRouteId,
                DepartureServiceRouteId = departureServiceRouteId,
                MinimumDwellTimeSeconds = minimumDwellTimeSeconds
            });
            stationOperations.Add(new StationOperationDefinition
            {
                StationOperationId = $"STATION-OP:{suffix}",
                StationId = terminalStationId,
                ArrivalPlatformIds = [arrivalPlatformId],
                DeparturePlatformIds = [departurePlatformId],
                TurnbackOperationIds = [operationId],
                DefaultDwellTimeSeconds = build.Infrastructure.Stations[terminalStationId].DefaultDwellTimeSeconds
            });
        }

        void AddRouteConnections(ServiceRouteDefinition route)
        {
            for (var index = 1; index < route.Traversals.Count; index++)
            {
                var previous = route.Traversals[index - 1];
                var current = route.Traversals[index];
                directedConnections.Add(new DirectedTrackConnectionDefinition(
                    previous.TrackEdgeId,
                    previous.Direction,
                    current.TrackEdgeId,
                    current.Direction));
            }
        }
    }

    private static ProjectDispatchPlan RemapDispatchOriginPlatforms(
        ProjectDispatchPlan dispatch,
        LinearInfrastructureBuildResult build)
    {
        // 起站取自進路順序，不可按站碼排序；上行起站通常在另一端。
        string? Map(TrainDirection direction) => build.GetServiceRoute(direction)
            .Stops[0].CandidatePlatformIds.FirstOrDefault();

        return dispatch with
        {
            SimpleHeadwayPlans = (dispatch.SimpleHeadwayPlans ?? [])
                .Select(plan => plan with { OriginPlatformId = Map(plan.Direction) })
                .ToArray(),
            ManualTimetableRows = (dispatch.ManualTimetableRows ?? [])
                .Select(row => row with { OriginPlatformId = Map(row.Direction) })
                .ToArray()
        };
    }
}
