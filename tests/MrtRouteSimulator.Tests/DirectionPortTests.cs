using MrtRouteSimulator.Engine;
using System.Text.Json.Nodes;

/// <summary>
/// Regression coverage for physical switch-port metadata.  These tests intentionally
/// exercise the Engine validation boundary rather than any schematic geometry.
/// </summary>
internal static class DirectionPortTests
{
    public static void ConstructionAndSplitPreservePorts()
    {
        var template = StationLayoutTemplateService.Build(StationLayoutTemplateKind.SideTwoTracks);
        var split = TopologyEditingService.SplitEdge(template, "D0", 400);
        TopologyProjectFormat.Validate(split);
        True(split.Topology.Edges.All(e => e.FromPortSide.HasValue && e.ToPortSide.HasValue), "分割不得遺失側別。");
        var quick = ProjectDocumentMapper.BuildQuickLinearProject(template,
            [new("A", "甲", 0, 20), new("B", "乙", 800, 20), new("C", "丙", 800, 20)]);
        TopologyProjectFormat.Validate(quick);
        True(quick.Topology.Edges.All(e => e.FromPortSide.HasValue && e.ToPortSide.HasValue), "快速建線必須有完整側別。");
        var down = quick.ServiceRoutes.Single(r => r.ServiceRouteId == quick.DirectionRouteBindings.Single(b => b.Direction == TrainDirection.Outbound).ServiceRouteId);
        var up = quick.ServiceRoutes.Single(r => r.ServiceRouteId == quick.DirectionRouteBindings.Single(b => b.Direction == TrainDirection.Inbound).ServiceRouteId);
        var incoming = quick.Topology.Edges.Single(e => e.TrackEdgeId == down.Traversals[^1].TrackEdgeId);
        var outgoing = quick.Topology.Edges.Single(e => e.TrackEdgeId == up.Traversals[0].TrackEdgeId);
        var tail = FacilityCreationService.CreateTailTrack(quick, new("尾軌", incoming.ToNodeId, incoming.TrackEdgeId, outgoing.TrackEdgeId, 200, 8, 10));
        var pocket = FacilityCreationService.CreatePocketTrack(quick, new("袋軌", incoming.ToNodeId, outgoing.FromNodeId, incoming.TrackEdgeId, outgoing.TrackEdgeId, 200, 8, 190));
        foreach (var document in new[] { tail, pocket })
        {
            TopologyProjectFormat.Validate(document);
            True(document.Topology.Edges.All(e => e.FromPortSide.HasValue && e.ToPortSide.HasValue), "設施精靈不得遺失側別。");
            var facility = document.Topology.TurnbackFacilities.Single();
            Equal(4, facility.Traversals.Count, "設施必須具有進軌、換端與出軌。");
            var invalid = AddConnection(document, new(facility.Traversals[0].TrackEdgeId, TraversalDirection.Forward,
                facility.Traversals[^1].TrackEdgeId, TraversalDirection.Forward));
            Throws<SimulationValidationException>(() => TopologyProjectFormat.Validate(invalid), "CONNECTION-001");
        }
    }

    public static void CentralPocketRejectsIllegalCrossoverTurn()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        var invalid = AddConnection(source,
            new DirectedTrackConnectionDefinition("PDIN", TraversalDirection.Forward,
                "PUOUT", TraversalDirection.Forward));

        Throws<SimulationValidationException>(
            () => TopologyProjectFormat.Validate(invalid),
            "CONNECTION-001");
        Throws<SimulationValidationException>(() => TopologyProjectFormat.Serialize(invalid), "CONNECTION-001");
        var json = JsonNode.Parse(TopologyProjectFormat.Serialize(source))!;
        json["topology"]!["directedConnections"]!.AsArray().Add(JsonNode.Parse("""
            {"fromTrackEdgeId":"PDIN","fromDirection":"Forward","toTrackEdgeId":"PUOUT","toDirection":"Forward"}
            """));
        Throws<SimulationValidationException>(() => TopologyProjectFormat.Deserialize(json.ToJsonString()), "CONNECTION-001");
    }

    public static void CentralPocketRejectsIllegalTurnWhenRouteDeclaresIt()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        var invalid = AddConnection(source,
            new DirectedTrackConnectionDefinition("PDIN", TraversalDirection.Forward,
                "PUOUT", TraversalDirection.Forward));
        var route = new ServiceRouteDefinition
        {
            ServiceRouteId = "INVALID-PORT-ROUTE",
            Name = "非法喉區測試進路",
            Traversals =
            [
                new DirectedTrackTraversal("PDIN", TraversalDirection.Forward),
                new DirectedTrackTraversal("PUOUT", TraversalDirection.Forward)
            ],
            Stops = []
        };

        var result = InfrastructureValidator.Validate(invalid.Topology, [route]);
        False(result.IsValid, "即使 ServiceRoute 宣告了該轉向，物理 port 仍應拒絕同側跨接。 ");
        Contains(result.Errors, "CONNECTION-001", "應回報 same-side port 錯誤代碼。 ");
        Contains(InfrastructureValidator.Validate(invalid.Topology with { DirectedConnections = [] }, [route]).Errors,
            "CONNECTION-001", "移除顯式白名單也不可讓營運路線繞過接軌側別檢核。");
    }

    public static void UnusedLegalBackupConnectionCanBeSaved()
    {
        var definition = CreateLegalUnusedConnectionTopology();
        var result = InfrastructureValidator.Validate(definition);
        True(result.IsValid, "合法但尚未被 ServiceRoute 使用的備用轉向仍應可保存。 ");

        var graph = new InfrastructureGraphV4(definition);
        Equal(1, graph.DirectedConnections.Count, "備用轉向不得在建立 graph 時被刪除。 ");
    }

    public static void SchematicLaneDoesNotChangePortValidation()
    {
        var source = CreateLegalUnusedConnectionTopology();
        var changed = source with
        {
            Nodes = source.Nodes.Select(node => node with { SchematicLane = node.SchematicLane is 1 ? -1 : 1 }).ToArray(),
            Edges = source.Edges.Select(edge => edge with { SchematicLane = edge.SchematicLane is 1 ? -1 : 1 }).ToArray()
        };

        True(InfrastructureValidator.Validate(source).IsValid, "原始 physical port topology 應有效。 ");
        True(InfrastructureValidator.Validate(changed).IsValid,
            "修改 schematicLane 不得改變 physical port 驗證結果。 ");
    }

    public static void OneSidedPortMetadataIsRejected()
    {
        var definition = CreateLegalUnusedConnectionTopology() with
        {
            Edges = CreateLegalUnusedConnectionTopology().Edges.Select(edge =>
                edge.TrackEdgeId == "E1"
                    ? edge with { ToPortSide = null }
                    : edge).ToArray()
        };

        var result = InfrastructureValidator.Validate(definition);
        False(result.IsValid, "只填一端 port metadata 的 edge 應拒絕。 ");
        Contains(result.Errors, "CONNECTION-002", "應回報缺少另一端 port metadata。 ");
        var badEnum = definition with { Edges = definition.Edges.Select(e => e with
            { FromPortSide = (TrackPortSide)99, ToPortSide = TrackPortSide.A }).ToArray() };
        Contains(InfrastructureValidator.Validate(badEnum).Errors, "CONNECTION-002", "非法列舉值必須拒絕。");
    }

    public static void InteriorSameEdgeReverseTurnbackRemainsLegal()
    {
        var document = StationLayoutTemplateService.Build(StationLayoutTemplateKind.FrontTurnback);
        TopologyProjectFormat.Validate(document);

        var facility = document.Topology.TurnbackFacilities.Single(item => item.FacilityId == "TURN");
        True(facility.Traversals.Zip(facility.Traversals.Skip(1), (from, to) => (from, to))
                .Any(pair => pair.from.TrackEdgeId == pair.to.TrackEdgeId
                    && pair.from.Direction != pair.to.Direction),
            "原地折返必須保留同一 physical edge 的反向 traversal。 ");
    }

    public static void PortMetadataRoundTripsThroughSchema8()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        var restored = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(source));

        foreach (var edge in source.Topology.Edges)
        {
            var roundTripped = restored.Topology.Edges.Single(item => item.TrackEdgeId == edge.TrackEdgeId);
            Equal(edge.FromPortSide, roundTripped.FromPortSide,
                $"edge「{edge.TrackEdgeId}」FromPortSide round-trip 不得遺失。 ");
            Equal(edge.ToPortSide, roundTripped.ToPortSide,
                $"edge「{edge.TrackEdgeId}」ToPortSide round-trip 不得遺失。 ");
        }
    }

    private static TopologyProjectDocument AddConnection(
        TopologyProjectDocument document,
        DirectedTrackConnectionDefinition connection) => document with
        {
            Topology = document.Topology with
            {
                DirectedConnections = document.Topology.DirectedConnections.Append(connection).ToArray()
            }
        };

    private static TopologyInfrastructureDefinition CreateLegalUnusedConnectionTopology()
    {
        return new TopologyInfrastructureDefinition
        {
            Nodes =
            [
                new TrackNodeDefinition("N0", "入口", TrackNodeKind.Boundary),
                new TrackNodeDefinition("N1", "喉區", TrackNodeKind.Switch),
                new TrackNodeDefinition("N2", "出口", TrackNodeKind.Boundary),
                new TrackNodeDefinition("N3", "備用止端", TrackNodeKind.BufferStop)
            ],
            Edges =
            [
                new TrackEdgeDefinition
                {
                    TrackEdgeId = "E1", FromNodeId = "N0", ToNodeId = "N1", LengthMeters = 100,
                    Directionality = TrackDirectionality.ForwardOnly, DefaultSpeedLimitMetersPerSecond = 10,
                    FromPortSide = TrackPortSide.A, ToPortSide = TrackPortSide.A, SchematicLane = 1
                },
                new TrackEdgeDefinition
                {
                    TrackEdgeId = "E2", FromNodeId = "N1", ToNodeId = "N2", LengthMeters = 100,
                    Directionality = TrackDirectionality.ForwardOnly, DefaultSpeedLimitMetersPerSecond = 10,
                    FromPortSide = TrackPortSide.B, ToPortSide = TrackPortSide.B, SchematicLane = -1
                },
                new TrackEdgeDefinition
                {
                    TrackEdgeId = "SPARE", FromNodeId = "N1", ToNodeId = "N3", LengthMeters = 50,
                    Directionality = TrackDirectionality.ForwardOnly, DefaultSpeedLimitMetersPerSecond = 5,
                    FromPortSide = TrackPortSide.A, ToPortSide = TrackPortSide.B, SchematicLane = 0
                }
            ],
            DirectedConnections =
            [
                new DirectedTrackConnectionDefinition("E1", TraversalDirection.Forward,
                    "E2", TraversalDirection.Forward)
            ]
        };
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void False(bool condition, string message) => True(!condition, message);

    private static void Contains(IEnumerable<string> values, string expected, string message)
    {
        if (!values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{message} 實際錯誤：{string.Join(" | ", values)}");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} 預期 {expected}，實際 {actual}。 ");
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
            Contains(exception.Message.Split(Environment.NewLine), expectedMessage,
                $"應拋出包含「{expectedMessage}」的錯誤。 ");
            return;
        }

        throw new InvalidOperationException($"預期拋出 {typeof(TException).Name}，但沒有拋出。 ");
    }
}
