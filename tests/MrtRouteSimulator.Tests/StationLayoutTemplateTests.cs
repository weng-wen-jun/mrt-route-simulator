using MrtRouteSimulator.Engine;

internal static class StationLayoutTemplateTests
{
    public static void PlannedTurnbackTimelinesKeepPhysicalCursors()
    {
        foreach (var kind in new[] { StationLayoutTemplateKind.FrontTurnback, StationLayoutTemplateKind.RearTurnback })
        {
            var source = StationLayoutTemplateService.Build(kind);
            var planned = source with { Simulation = source.Simulation with {
                ProfileMode = OperationProfileMode.BasicPhysics, MovingBlockMode = MovingBlockMode.Independent } };
            var world = CreateWorld(planned);
            if (kind == StationLayoutTemplateKind.FrontTurnback) AssertStationaryPlatformTurnback(planned, world);
            True(world.GetSnapshot().Trains.All(t => !t.IsActive), kind, "計畫時間軸的折返車次須完成退出。");
            True(world.Events.Any(e => e.EventType == SimulationEventType.DirectionChanged), kind, "計畫時間軸須實際完成折返。");
            True(world.Trajectory.All(t => t.TrackEdgeId is not null && !t.TrackEdgeId.StartsWith("LEGACY:")), kind, "計畫時間軸不得遺失實體軌道位置。");
        }
    }

    public static void BuildsAndRoundTripsAllStationLayoutTemplates()
    {
        foreach (var kind in Enum.GetValues<StationLayoutTemplateKind>())
        {
            var source = StationLayoutTemplateService.Build(kind);
            TopologyProjectFormat.Validate(source);
            var restored = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(source));
            TopologyProjectFormat.Validate(restored);
            Equal(TopologyProjectFormat.CurrentSchemaVersion, restored.SchemaVersion, kind, "Schema 版本錯誤。");
            True(restored.ServiceRoutes.Length >= 2, kind, "必須有上下行 ServiceRoute。");
            True(restored.Topology.Edges.Count > 0, kind, "必須有實體 edge。");
            AssertWorldHasNoSafetyOrStopViolations(restored, kind);
        }
    }

    public static void BuildsFacilityTemplatesWithPhysicalTraversals()
    {
        var rear = StationLayoutTemplateService.Build(StationLayoutTemplateKind.RearTurnback);
        var front = StationLayoutTemplateService.Build(StationLayoutTemplateKind.FrontTurnback);
        var rearX1 = rear.Topology.Edges.Single(edge => edge.TrackEdgeId == "X1");
        var rearX2 = rear.Topology.Edges.Single(edge => edge.TrackEdgeId == "X2");
        True(rearX1.FromNodeId == "U200" && rearX1.ToNodeId == "D100",
            StationLayoutTemplateKind.RearTurnback, "站後折返第一條渡線必須由 U200 接至 D100。 ");
        True(rearX2.FromNodeId == "U100" && rearX2.ToNodeId == "D200",
            StationLayoutTemplateKind.RearTurnback, "站後折返第二條渡線必須由 U100 接至 D200。 ");
        var rearTurnback = rear.Topology.TurnbackFacilities.Single(item => item.FacilityId == "TURN");
        True(rearTurnback.Traversals.Select(item => item.TrackEdgeId).SequenceEqual(["TU0", "TU1", "TU2", "TU2", "X1", "TD0"])
            && rearTurnback.Traversals[3].Direction == TraversalDirection.Reverse,
            StationLayoutTemplateKind.RearTurnback, "站後折返第一條進路必須在 TU2 換端後經 X1 直接接至 TD0。 ");
        var rearTail2 = rear.Topology.TurnbackFacilities.Single(item => item.FacilityId == "TURN:TAIL2");
        True(rearTail2.Traversals.Select(item => item.TrackEdgeId).SequenceEqual(["TU0", "X2", "TD2", "TD2", "TD1", "TD0"])
            && rearTail2.Traversals[2].Direction == TraversalDirection.Reverse,
            StationLayoutTemplateKind.RearTurnback, "站後折返第二條進路必須由 X2 接入 TD2 反向後再返回 TD0。 ");
        AssertWorldHasNoSafetyOrStopViolations(rear, StationLayoutTemplateKind.RearTurnback);
        AssertWorldHasNoSafetyOrStopViolations(front, StationLayoutTemplateKind.FrontTurnback);
        AssertContinuationAndTurnaround(rear, StationLayoutTemplateKind.RearTurnback);
        AssertContinuationAndTurnaround(front, StationLayoutTemplateKind.FrontTurnback);
        var rearWorld = CreateWorld(rear);
        True(rearWorld.Trajectory.Any(item => item.TrackEdgeId is "TU0" or "TU1" or "TU2"),
            StationLayoutTemplateKind.RearTurnback, "站後折返車必須實際通過 TU 尾軌。");
        var frontWorld = CreateWorld(front);
        True(frontWorld.Trajectory.Any(item => item.TrackEdgeId == "X1"),
            StationLayoutTemplateKind.FrontTurnback, "站前折返車必須實際通過 X1 渡線。");
        True(!frontWorld.Trajectory.Any(item => item.TrackEdgeId is "TU0" or "TU1" or "TU2"),
            StationLayoutTemplateKind.FrontTurnback, "站前折返不得以 TU 尾軌代替 X1 渡線。");
        foreach (var kind in new[] { StationLayoutTemplateKind.FrontTurnback, StationLayoutTemplateKind.RearTurnback })
        {
            var doc = StationLayoutTemplateService.Build(kind);
            var suffix = kind == StationLayoutTemplateKind.FrontTurnback ? ":PLATFORM2" : ":TAIL2";
            doc = doc with { DirectionRouteBindings = doc.DirectionRouteBindings.Select(b => b with { ServiceRouteId = b.ServiceRouteId + suffix }).ToArray() };
            doc = doc with { Dispatch = doc.Dispatch with { ManualTimetableRows = doc.Dispatch.ManualTimetableRows!.Select(row =>
                row.Direction == TrainDirection.Outbound && kind == StationLayoutTemplateKind.FrontTurnback ? row with { OriginPlatformId = "A:U" } : row).ToArray() } };
            AssertWorldHasNoSafetyOrStopViolations(doc, kind);
            AssertContinuationAndTurnaround(doc, kind);
            True(CreateWorld(doc).Trajectory.Any(t => t.TrackEdgeId == "X2"), kind, "替代折返進路須實際通過第二渡線。");
        }
    }

    public static void ThreeAndFourTrackTemplatesExposeAdditionalPhysicalTracks()
    {
        var three = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandAndSideThreeTracks);
        var four = StationLayoutTemplateService.Build(StationLayoutTemplateKind.DoubleIslandFourTracks);
        Equal(3, three.Topology.Platforms.Count(item => item.StationId == "B"),
            StationLayoutTemplateKind.IslandAndSideThreeTracks, "三股 B 站必須恰有三座月台。");
        Equal(4, four.Topology.Platforms.Count(item => item.StationId == "B"),
            StationLayoutTemplateKind.DoubleIslandFourTracks, "四股 B 站必須恰有四座月台。");
        var threeWorld = CreateWorld(three);
        True(threeWorld.Trajectory.Any(item => item.TrackEdgeId == "USIDE"),
            StationLayoutTemplateKind.IslandAndSideThreeTracks, "三股模板的 USIDE 必須被實際 route 使用。");
        var fourWorld = CreateWorld(four);
        True(fourWorld.Trajectory.Any(item => item.TrackEdgeId == "USIDE"),
            StationLayoutTemplateKind.DoubleIslandFourTracks, "四股模板的 USIDE 必須被實際 route 使用。");
        True(fourWorld.Trajectory.Any(item => item.TrackEdgeId == "DSIDE"),
            StationLayoutTemplateKind.DoubleIslandFourTracks, "四股模板的 DSIDE 必須被實際 route 使用。");
        Equal(1, threeWorld.Events.Count(e => e.EventType == SimulationEventType.OvertakeCompleted), StationLayoutTemplateKind.IslandAndSideThreeTracks, "三股道須實際完成上行越行。");
        Equal(2, fourWorld.Events.Count(e => e.EventType == SimulationEventType.OvertakeCompleted), StationLayoutTemplateKind.DoubleIslandFourTracks, "四股道須實際完成双向越行。");
        True(fourWorld.Trajectory.Any(t => t.ServiceRunId == "EXP-UP" && t.TrackEdgeId == "U1")
            && fourWorld.Trajectory.Any(t => t.ServiceRunId == "EXP-DOWN" && t.TrackEdgeId == "D1"),
            StationLayoutTemplateKind.DoubleIslandFourTracks, "快速車須使用正線。");
    }

    public static void CentralPocketUsesPocketOnlyAtStationBAndBypassRoutesSkipB()
    {
        var document = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        True(!document.Topology.DirectedConnections.Any(connection =>
                connection.FromTrackEdgeId == "PDIN"
                && connection.ToTrackEdgeId == "PUOUT"),
            StationLayoutTemplateKind.CentralPocket,
            "袋狀軌入口不可由共享節點直接轉入另一方向的出口。 ");
        True(!document.Topology.DirectedConnections.Any(connection =>
                connection.FromTrackEdgeId == "PUIN"
                && connection.ToTrackEdgeId == "PDOUT"),
            StationLayoutTemplateKind.CentralPocket,
            "袋狀軌另一側入口不可由共享節點直接轉入反向出口。 ");
        var bPlatforms = document.Topology.Platforms.Where(item => item.StationId == "B").ToArray();
        True(bPlatforms.Length > 0 && bPlatforms.All(item => item.TrackEdgeId == "POCKET"),
            StationLayoutTemplateKind.CentralPocket, "中央袋狀軌模板的 B 站月台只能附著 POCKET。");
        var bypassRoutes = document.ServiceRoutes.Where(item => item.ServiceRouteId.EndsWith(":BYPASS", StringComparison.OrdinalIgnoreCase)).ToArray();
        Equal(2, bypassRoutes.Length, StationLayoutTemplateKind.CentralPocket, "中央袋狀軌必須提供上下行 bypass route。");
        foreach (var route in bypassRoutes)
            True(route.Stops.Select(item => item.StationId).SequenceEqual(route.ServiceRouteId.StartsWith("DOWN") ? ["A", "C"] : new[] { "C", "A" }),
                StationLayoutTemplateKind.CentralPocket, $"{route.ServiceRouteId} 只能有 A、C 停點。");
        var world = CreateWorld(document);
        AssertWorldHasNoSafetyOrStopViolations(document, StationLayoutTemplateKind.CentralPocket);
        var bArrivals = world.Events.Where(item => item.EventType == SimulationEventType.Arrival
            && item.PlatformId is not null && item.PlatformId.StartsWith("B", StringComparison.OrdinalIgnoreCase)).ToArray();
        True(bArrivals.Length > 0 && bArrivals.All(item => item.PlatformId == "B:P"),
            StationLayoutTemplateKind.CentralPocket, "實際抵達 B 站的列車只能停在 POCKET 月台。");
    }

    private static SimulationWorld CreateWorld(TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: null,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        world.AdvanceTo(3600);
        return world;
    }

    private static void AssertWorldHasNoSafetyOrStopViolations(TopologyProjectDocument document, StationLayoutTemplateKind kind)
    {
        var world = CreateWorld(document);
        world.AdvanceTo(3600);
        True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision), kind, "3600 秒內不得發生 Collision。");
        True(!world.Events.Any(item => item.EventType == SimulationEventType.StationStopViolation), kind, "3600 秒內不得發生 StationStopViolation。");
        True(world.GetSnapshot().Trains.All(t => !t.IsActive), kind, "所有示範班次須完成退出。");
    }

    private static void AssertContinuationAndTurnaround(TopologyProjectDocument document, StationLayoutTemplateKind kind)
    {
        var world = CreateWorld(document);
        if (kind == StationLayoutTemplateKind.FrontTurnback) AssertStationaryPlatformTurnback(document, world);
        world.AdvanceTo(3600);
        True(world.Events.Any(item => item.EventType == SimulationEventType.TurnaroundStarted && item.VehicleId == "V1"), kind, "V1 必須實際產生 TurnaroundStarted。");
        True(world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged && item.VehicleId == "V1" && item.ServiceRunId == "DOWN-1"), kind, "UP-1 必須由同一 V1 接續為 DOWN-1。");
    }

    private static void AssertStationaryPlatformTurnback(TopologyProjectDocument document, SimulationWorld world)
    {
        var routeId = document.DirectionRouteBindings.Single(b => b.Direction == TrainDirection.Inbound).ServiceRouteId;
        var route = document.ServiceRoutes.Single(r => r.ServiceRouteId == routeId);
        var platform = document.Topology.Platforms.Single(p => p.PlatformId == route.Stops[^1].CandidatePlatformIds[0]);
        var start = world.Events.First(e => e.EventType == SimulationEventType.TurnaroundStarted).SimulationTimeSeconds;
        var end = world.Events.First(e => e.EventType == SimulationEventType.DirectionChanged).SimulationTimeSeconds;
        var samples = world.Trajectory.Where(t => t.VehicleId == "V1" && t.SimulationTimeSeconds >= start && t.SimulationTimeSeconds <= end).ToArray();
        var length = document.VehicleTypes.Single(v => v.Id == "EMU").LengthMeters;
        True(samples.Length > 0 && samples.All(t => t.TrackEdgeId == platform.TrackEdgeId
            && t.OffsetMeters is { } offset && Math.Abs(PlatformStopPositionResolver.ResolveCenterPosition(
                new TrackPosition(platform.TrackEdgeId, offset),
                document.ServiceRoutes.Single(r => r.ServiceRouteId == document.DirectionRouteBindings.Single(b => b.Direction == t.Direction).ServiceRouteId)
                    .Traversals.First(tr => tr.TrackEdgeId == platform.TrackEdgeId).Direction, length).OffsetMeters - platform.StopPositionOffsetMeters) < .001
            && t.SpeedMetersPerSecond == 0), StationLayoutTemplateKind.FrontTurnback, "整段站內折返必須停在月台停點，不得移往止衝或其他軌道。");
        True(Math.Abs(platform.StopPositionOffsetMeters - (platform.PlatformStartOffsetMeters + platform.PlatformEndOffsetMeters) / 2) < .001,
            StationLayoutTemplateKind.FrontTurnback, "站前折返停點應與月台中心一致。");
    }

    private static void True(bool condition, StationLayoutTemplateKind kind, string message)
    {
        if (!condition) throw new InvalidOperationException($"{kind}: {message}");
    }

    private static void Equal<T>(T expected, T actual, StationLayoutTemplateKind kind, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{kind}: {message} 預期 {expected}，實際 {actual}。");
    }
}
