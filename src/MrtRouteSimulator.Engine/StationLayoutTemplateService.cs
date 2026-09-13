
namespace MrtRouteSimulator.Engine;

public enum StationLayoutTemplateKind
{
    IslandTwoTracks, SideTwoTracks, IslandAndSideThreeTracks, DoubleIslandFourTracks,
    RearTurnback, FrontTurnback, CentralPocket
}

/// <summary>PDF 站場的可執行起稿；所有尺寸、性能及派車時間均為可編輯的示範值。</summary>
public static class StationLayoutTemplateService
{
    public static string Name(StationLayoutTemplateKind kind) => kind switch
    {
        StationLayoutTemplateKind.IslandTwoTracks => "島式月台二股道",
        StationLayoutTemplateKind.SideTwoTracks => "側式月台二股道",
        StationLayoutTemplateKind.IslandAndSideThreeTracks => "一島一側月台三股道",
        StationLayoutTemplateKind.DoubleIslandFourTracks => "二島月台四股道",
        StationLayoutTemplateKind.RearTurnback => "島式月台站後折返",
        StationLayoutTemplateKind.FrontTurnback => "島式月台站前折返",
        StationLayoutTemplateKind.CentralPocket => "中央袋狀軌停車、兩側通過",
        _ => throw new SimulationValidationException(["未知站場形式。"])
    };

    public static TopologyProjectDocument Build(StationLayoutTemplateKind kind)
    {
        var name = Name(kind);
        var nodes = new List<TrackNodeDefinition>();
        var edges = new List<TrackEdgeDefinition>();
        var platforms = new List<PlatformDefinitionV4>();
        var resources = new List<ConflictResourceDefinition>();
        var facilities = new List<TurnbackFacilityDefinition>();
        var turnbacks = new List<TurnbackOperationDefinition>();
        var passings = new List<PassingFacilityDefinition>();
        var passingOperations = new List<PassingOperationDefinition>();
        var stationOperations = new List<StationOperationDefinition>();
        void Node(string id, double x, double lane, TrackNodeKind nk = TrackNodeKind.Switch) =>
            nodes.Add(new(id, id, nk) { SchematicPosition = x, SchematicLane = lane });
        void Edge(string id, string from, string to, double length, double? lane,
            TrackEdgeKind ek = TrackEdgeKind.Mainline, bool both = false, string? resource = null) =>
            edges.Add(new() { TrackEdgeId = id, FromNodeId = from, ToNodeId = to, LengthMeters = length,
                SchematicLane = lane, Kind = ek, Directionality = both ? TrackDirectionality.Bidirectional : TrackDirectionality.ForwardOnly,
                FromPortSide = nodes.Single(n => n.NodeId == to).SchematicPosition > nodes.Single(n => n.NodeId == from).SchematicPosition ? TrackPortSide.B : TrackPortSide.A,
                ToPortSide = nodes.Single(n => n.NodeId == to).SchematicPosition > nodes.Single(n => n.NodeId == from).SchematicPosition ? TrackPortSide.A : TrackPortSide.B,
                DefaultSpeedLimitMetersPerSecond = ek == TrackEdgeKind.Mainline ? 60 / 3.6 : 25 / 3.6,
                ConflictResourceIds = resource is null ? new HashSet<string>() : new HashSet<string>([resource]) });
        PlatformDefinitionV4 Platform(string id, string station, string edge, double offset, TrackDirection direction,
            string number, string body, PlatformSide side, double start, double end) => new()
            { PlatformId = id, StationId = station, Name = $"{station}站 {number}月台", TrackEdgeId = edge,
                StopPositionOffsetMeters = offset, PlatformStartOffsetMeters = start, PlatformEndOffsetMeters = end,
                EffectiveLengthMeters = end - start, AllowedDirection = direction, PlatformNumber = number, PlatformBodyId = body, DisplaySide = side,
                StopPositionReference = StopPositionReference.TrainCenter };
        for (var i = 0; i < 4; i++) { Node($"DN{i}", i * 600, 1); Node($"UN{i}", i * 600, -1); }
        for (var i = 0; i < 3; i++)
        {
            Edge($"D{i}", $"DN{i}", $"DN{i+1}", 600, 1, both: kind == StationLayoutTemplateKind.FrontTurnback && i == 0);
            Edge($"U{i}", $"UN{i+1}", $"UN{i}", 600, -1);
        }
        foreach (var station in new[] { "A", "B", "C" })
        {
            var segment = station == "A" ? 0 : station == "B" ? 1 : 2;
            var d = station == "A" ? 130 : station == "B" ? 300 : 470;
            var u = 600 - d;
            var side = kind == StationLayoutTemplateKind.SideTwoTracks;
            platforms.Add(Platform($"{station}:D", station, $"D{segment}", d, TrackDirection.Outbound, "1",
                side ? "" : station, side ? PlatformSide.Below : PlatformSide.Above, d - 130, d + 130));
            platforms.Add(Platform($"{station}:U", station, $"U{segment}", u, TrackDirection.Inbound, "2",
                side ? "" : station, side ? PlatformSide.Above : PlatformSide.Below, u - 130, u + 130));
        }
        var down = new List<DirectedTrackTraversal> { T("D0"), T("D1"), T("D2") };
        var up = new List<DirectedTrackTraversal> { T("U2"), T("U1"), T("U0") };
        var downB = "B:D"; var upB = "B:U"; var upA = "A:U";
        void AddPassing(bool outbound)
        {
            var p = outbound ? "D" : "U"; var siding = p + "SIDE";
            var arrival = outbound ? "D0" : "U2"; var departure = outbound ? "D2" : "U0";
            var resource = "PASS:" + p;
            resources.Add(new(resource, "站場匯入衝突區", ConflictResourceKind.Switch));
            Edge(siding, outbound ? "DN1" : "UN2", outbound ? "DN2" : "UN1", 650,
                outbound ? 3 : -3, TrackEdgeKind.Siding);
            var id = "B:" + siding; var four = kind == StationLayoutTemplateKind.DoubleIslandFourTracks;
            var number = outbound ? "1" : four ? "4" : "3"; var body = outbound ? "B:LOWER" : "B:UPPER";
            platforms.Add(Platform(id, "B", siding, 325, outbound ? TrackDirection.Outbound : TrackDirection.Inbound,
                number, body, outbound ? PlatformSide.Above : PlatformSide.Below, 195, 455));
            var i = platforms.FindIndex(v => v.PlatformId == "B:" + p);
            platforms[i] = platforms[i] with { PlatformNumber = outbound ? "2" : four ? "3" : "2",
                PlatformBodyId = body, DisplaySide = outbound ? PlatformSide.Below : PlatformSide.Above };
            if (outbound) { down[1] = T(siding); downB = id; } else { up[1] = T(siding); upB = id; }
            passings.Add(new() { FacilityId = resource, Name = "普通車側線待避、快速車正線通過", StationId = "B",
                ArrivalTrackEdgeId = arrival, ArrivalOffsetMeters = 600, DepartureTrackEdgeId = departure,
                LocalPlatformId = id, ExpressPlatformId = "B:" + p, Traversals = [T(p + "1")], ConflictResourceIds = new HashSet<string>([resource]) });
            passingOperations.Add(new() { OperationId = resource, FacilityId = resource,
                ServiceRouteId = outbound ? "DOWN" : "UP", ExpressServiceTypeId = "EXPRESS" });
        }
        if (kind is StationLayoutTemplateKind.IslandAndSideThreeTracks or StationLayoutTemplateKind.DoubleIslandFourTracks)
        {
            AddPassing(false);
            if (kind == StationLayoutTemplateKind.DoubleIslandFourTracks) AddPassing(true);
            else { var i = platforms.FindIndex(p => p.PlatformId == "B:D"); platforms[i] = platforms[i] with { PlatformBodyId = "", DisplaySide = PlatformSide.Below }; }
        }
        if (kind == StationLayoutTemplateKind.CentralPocket)
        {
            resources.Add(new("POCKET", "中央袋狀軌共用占用", ConflictResourceKind.PocketTrack));
            Node("PC1", 750, 0); Node("PC2", 1050, 0);
            Edge("PDIN", "DN1", "PC1", 150, null, TrackEdgeKind.Crossover, resource: "POCKET");
            Edge("PDOUT", "PC2", "DN2", 150, null, TrackEdgeKind.Crossover, resource: "POCKET");
            Edge("PUIN", "UN2", "PC2", 150, null, TrackEdgeKind.Crossover, resource: "POCKET");
            Edge("PUOUT", "PC1", "UN1", 150, null, TrackEdgeKind.Crossover, resource: "POCKET");
            Edge("POCKET", "PC1", "PC2", 300, 0, TrackEdgeKind.PocketTrack, true, "POCKET");
            platforms.RemoveAll(p => p.StationId == "B");
            platforms.Add(Platform("B:P", "B", "POCKET", 150, TrackDirection.Both, "2", "", PlatformSide.Below, 20, 280));
            down = [T("D0"), T("PDIN"), T("POCKET"), T("PDOUT"), T("D2")];
            up = [T("U2"), T("PUIN"), T("POCKET", true), T("PUOUT"), T("U0")]; downB = upB = "B:P";
        }
        var terminal = kind is StationLayoutTemplateKind.RearTurnback or StationLayoutTemplateKind.FrontTurnback;
        if (terminal)
        {
            resources.Add(new("TURN", "折返進路共用衝突區", ConflictResourceKind.Crossover));
            if (kind == StationLayoutTemplateKind.RearTurnback)
            {
                Node("U100", -100, -1); Node("U200", -200, -1); Node("UB", -400, -1, TrackNodeKind.BufferStop);
                Node("D100", -100, 1); Node("D200", -200, 1); Node("DB", -400, 1, TrackNodeKind.BufferStop);
                Edge("TU0", "UN0", "U100", 100, -1, TrackEdgeKind.TailTrack, true, "TURN");
                Edge("TU1", "U100", "U200", 100, -1, TrackEdgeKind.TailTrack, true, "TURN");
                Edge("TU2", "U200", "UB", 200, -1, TrackEdgeKind.TailTrack, true, "TURN");
                Edge("TD0", "D100", "DN0", 100, 1, TrackEdgeKind.TailTrack, true, "TURN");
                Edge("TD1", "D200", "D100", 100, 1, TrackEdgeKind.TailTrack, true, "TURN");
                Edge("TD2", "DB", "D200", 200, 1, TrackEdgeKind.TailTrack, true, "TURN");
                Edge("X1", "U200", "D100", 140, null, TrackEdgeKind.Crossover, true, "TURN");
                Edge("X2", "U100", "D200", 140, null, TrackEdgeKind.Crossover, true, "TURN");
                facilities.Add(new() { FacilityId = "TURN", Name = "站後尾軌折返", Kind = TurnbackFacilityKind.TailTrack,
                    ArrivalTrackEdgeId = "U0", ArrivalStopOffsetMeters = 470, DepartureTrackEdgeId = "D0", DepartureStartOffsetMeters = 130,
                    Traversals = [T("TU0"), T("TU1"), T("TU2"), T("TU2", true), T("X1"), T("TD0")],
                    TurnbackStopPosition = new("TU2", 160), ConflictResourceIds = new HashSet<string>(["TURN"]) });
            }
            else
            {
                // 渡線在月台東側；入站上行先換至下行月台，於同一 edge-local 停點反向。
                var ni = nodes.FindIndex(n => n.NodeId == "DN1"); nodes[ni] = nodes[ni] with { SchematicPosition = 500 };
                Node("DX", 600, 1); Node("UX", 500, -1);
                var di = edges.FindIndex(e => e.TrackEdgeId == "D1"); edges[di] = edges[di] with { FromNodeId = "DX" };
                var ui = edges.FindIndex(e => e.TrackEdgeId == "U0"); edges[ui] = edges[ui] with { FromNodeId = "UX", Directionality = TrackDirectionality.Bidirectional };
                Edge("DC", "DN1", "DX", 100, 1); Edge("UC", "UN1", "UX", 100, -1);
                Edge("X1", "UN1", "DN1", 140, null, TrackEdgeKind.Crossover, true, "TURN");
                Edge("X2", "DX", "UX", 140, null, TrackEdgeKind.Crossover, true, "TURN");
                down = [T("D0"), T("DC"), T("D1"), T("D2")]; up = [T("U2"), T("U1"), T("X1"), T("D0", true)]; upA = "A:D";
                var pi = platforms.FindIndex(p => p.PlatformId == "A:D"); platforms[pi] = platforms[pi] with { AllowedDirection = TrackDirection.Both,
                    StopPositionOffsetMeters = 180, PlatformStartOffsetMeters = 50, PlatformEndOffsetMeters = 310, EffectiveLengthMeters = 260 };
                var upPi = platforms.FindIndex(p => p.PlatformId == "A:U"); platforms[upPi] = platforms[upPi] with { AllowedDirection = TrackDirection.Both,
                    StopPositionOffsetMeters = 420, PlatformStartOffsetMeters = 290, PlatformEndOffsetMeters = 550, EffectiveLengthMeters = 260 };
                foreach (var id in new[] { "DN0", "UN0" }) { var i = nodes.FindIndex(n => n.NodeId == id); nodes[i] = nodes[i] with { Kind = TrackNodeKind.BufferStop }; }
                facilities.Add(new() { FacilityId = "TURN", Name = "站前渡線進站、月台原地反向", Kind = TurnbackFacilityKind.Crossover,
                    ArrivalTrackEdgeId = "D0", ArrivalStopOffsetMeters = 180, DepartureTrackEdgeId = "D0", DepartureStartOffsetMeters = 180,
                    Traversals = [T("D0"), T("D0", true)], TurnbackStopPosition = new("D0", 180), ConflictResourceIds = new HashSet<string>(["TURN"]) });
            }
            turnbacks.Add(new() { OperationId = "TURN", FacilityId = "TURN", ArrivalServiceRouteId = "UP", DepartureServiceRouteId = "DOWN", MinimumDwellTimeSeconds = 30 });
            stationOperations.Add(new() { StationOperationId = "A:TURN", StationId = "A", ArrivalPlatformIds = [upA], DeparturePlatformIds = ["A:D"], TurnbackOperationIds = ["TURN"] });
        }
        ServiceRouteDefinition Route(string id, IReadOnlyList<DirectedTrackTraversal> ts, string middle, string origin, string last, bool bypass = false) => new()
            { ServiceRouteId = id, Name = id.StartsWith("DOWN") ? "下行進路" : "上行進路", Traversals = ts,
                Stops = bypass ? [Stop(origin[..1], origin), Stop(last[..1], last)] : [Stop(origin[..1], origin), Stop("B", middle), Stop(last[..1], last)] };
        var routes = new List<ServiceRouteDefinition> { Route("DOWN", down, downB, "A:D", "C:D"), Route("UP", up, upB, "C:U", upA) };
        if (kind == StationLayoutTemplateKind.FrontTurnback)
        {
            routes.Add(Route("UP:PLATFORM2", [T("U2"), T("U1"), T("UC"), T("U0")], upB, "C:U", "A:U"));
            routes.Add(Route("DOWN:PLATFORM2", [T("U0", true), T("X2", true), T("D1"), T("D2")], downB, "A:U", "C:D"));
            facilities.Add(new() { FacilityId = "TURN:PLATFORM2", Name = "2月台反向、站前渡線出站", Kind = TurnbackFacilityKind.Crossover,
                ArrivalTrackEdgeId = "U0", ArrivalStopOffsetMeters = 420, DepartureTrackEdgeId = "U0", DepartureStartOffsetMeters = 420,
                Traversals = [T("U0", true), T("U0")], TurnbackStopPosition = new("U0", 420), ConflictResourceIds = new HashSet<string>(["TURN"]) });
            turnbacks.Add(new() { OperationId = "TURN:PLATFORM2", FacilityId = "TURN:PLATFORM2", ArrivalServiceRouteId = "UP:PLATFORM2", DepartureServiceRouteId = "DOWN:PLATFORM2", MinimumDwellTimeSeconds = 30 });
            stationOperations.Add(new() { StationOperationId = "A:TURN2", StationId = "A", ArrivalPlatformIds = ["A:U"], DeparturePlatformIds = ["A:U"], TurnbackOperationIds = ["TURN:PLATFORM2"] });
        }
        if (kind == StationLayoutTemplateKind.RearTurnback)
        {
            routes.Add(Route("UP:TAIL2", up, upB, "C:U", upA));
            routes.Add(Route("DOWN:TAIL2", down, downB, "A:D", "C:D"));
            facilities.Add(new() { FacilityId = "TURN:TAIL2", Name = "經第二渡線使用下行尾軌折返", Kind = TurnbackFacilityKind.TailTrack,
                ArrivalTrackEdgeId = "U0", ArrivalStopOffsetMeters = 470, DepartureTrackEdgeId = "D0", DepartureStartOffsetMeters = 130,
                Traversals = [T("TU0"), T("X2"), T("TD2", true), T("TD2"), T("TD1"), T("TD0")],
                TurnbackStopPosition = new("TD2", 40), ConflictResourceIds = new HashSet<string>(["TURN"]) });
            turnbacks.Add(new() { OperationId = "TURN:TAIL2", FacilityId = "TURN:TAIL2", ArrivalServiceRouteId = "UP:TAIL2", DepartureServiceRouteId = "DOWN:TAIL2", MinimumDwellTimeSeconds = 30 });
            stationOperations.Add(new() { StationOperationId = "A:TAIL2", StationId = "A", ArrivalPlatformIds = ["A:U"], DeparturePlatformIds = ["A:D"], TurnbackOperationIds = ["TURN:TAIL2"] });
        }
        if (kind == StationLayoutTemplateKind.CentralPocket)
        {
            routes.Add(Route("DOWN:BYPASS", [T("D0"), T("D1"), T("D2")], "", "A:D", "C:D", true));
            routes.Add(Route("UP:BYPASS", [T("U2"), T("U1"), T("U0")], "", "C:U", "A:U", true));
        }
        var topology = new TopologyInfrastructureDefinition { Nodes = nodes, Edges = edges, Platforms = platforms,
            Stations = new[] { "A", "B", "C" }.Select(id => new StationDefinitionV4 { StationId = id,
                Name = id == "B" ? terminal ? "中間站" : name : id == "A" ? terminal ? name : "西端站" : "東端站", DefaultDwellTimeSeconds = 30,
                PlatformIds = platforms.Where(p => p.StationId == id).Select(p => p.PlatformId).ToArray() }).ToArray(),
            Resources = resources, DirectedConnections = [], TurnbackFacilities = facilities, TurnbackOperations = turnbacks,
            StationOperations = stationOperations, PassingFacilities = passings, PassingOperations = passingOperations };
        topology = topology with { DirectedConnections = DirectedTrackConnectionRules.Build(topology, routes) };
        var rows = terminal ? new[] {
            new ProjectManualTimetableRow(0, TrainDirection.Inbound, "LOCAL", "EMU", "ALL", "C:U", "V1", "UP-1", true, "DOWN-1"),
            new ProjectManualTimetableRow(1000, TrainDirection.Outbound, "LOCAL", "EMU", "ALL", "A:D", "V1", "DOWN-1") }
            : new[] { new ProjectManualTimetableRow(0, TrainDirection.Outbound, "LOCAL", "EMU", "ALL", "A:D", "V1", "DOWN-1"),
                new ProjectManualTimetableRow(0, TrainDirection.Inbound, "LOCAL", "EMU", "ALL", "C:U", "V2", "UP-1") };
        var passingExample = kind is StationLayoutTemplateKind.IslandAndSideThreeTracks or StationLayoutTemplateKind.DoubleIslandFourTracks;
        if (passingExample)
        {
            rows = rows.Append(new ProjectManualTimetableRow(90, TrainDirection.Inbound, "EXPRESS", "EMU", "SKIP", "C:U", "V3", "EXP-UP")).ToArray();
            if (kind == StationLayoutTemplateKind.DoubleIslandFourTracks)
                rows = rows.Append(new ProjectManualTimetableRow(90, TrainDirection.Outbound, "EXPRESS", "EMU", "SKIP", "A:D", "V4", "EXP-DOWN")).ToArray();
        }
        var doc = new TopologyProjectDocument(8, "STATION-TEMPLATE", name,
            new(60 / 3.6, 1, 1, 30, 30, 30), new(1, .2, 250, 12, .1, 120, .8, 1, 1, 1, 2, 5, 10),
            new(rows.Length, 300, 0, 1, OperationProfileMode.RealisticOperations, MovingBlockMode.Control, BrakingEstimationMode.Service),
            [new("EMU", "示範電聯車", 120, 60 / 3.6, 1, 1, 1, 1, .01, 0)],
            [new("LOCAL", "普通車", "#19607D", "L", "ALL", "EMU"), new("EXPRESS", "快速車", "#D26523", "E", "SKIP", "EMU", CanRequestOvertake: true)],
            [new("ALL", passingExample ? "全停（中間站待避120秒）" : "全停", new[] { "A", "B", "C" }.Select(id => new ProjectStopPatternInstruction(id, StopPatternAction.Stop, passingExample && id == "B" ? 120 : 30)).ToArray()),
             new("SKIP", "中間站通過", new[] { "A", "B", "C" }.Select(id => new ProjectStopPatternInstruction(id, id == "B" ? StopPatternAction.Pass : StopPatternAction.Stop, 30)).ToArray())],
            new(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [], rows), topology, routes.ToArray(),
            [new(TrainDirection.Outbound, "DOWN"), new(TrainDirection.Inbound, "UP")]);
        TopologyProjectFormat.Validate(doc);
        return doc;
    }
    private static DirectedTrackTraversal T(string id, bool reverse = false) => new(id, reverse ? TraversalDirection.Reverse : TraversalDirection.Forward);
    private static ServiceRouteStop Stop(string station, string platform) => new() { StationId = station, CandidatePlatformIds = [platform] };
}
