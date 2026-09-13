using MrtRouteSimulator.Engine;

internal static class GraphDistanceConstraintTests
{
    public static void SharedStationNodesDoNotConnectParallelTracks()
    {
        TrackEdgeDefinition Edge(string id, string from, string to) => new() {
            TrackEdgeId = id, FromNodeId = from, ToNodeId = to, LengthMeters = 100,
            Directionality = TrackDirectionality.ForwardOnly, Kind = TrackEdgeKind.Mainline,
            DefaultSpeedLimitMetersPerSecond = 20 };
        var graph = new InfrastructureGraphV4(new() {
            Nodes = [new("A", "A", TrackNodeKind.Ordinary), new("B", "B", TrackNodeKind.Ordinary), new("C", "C", TrackNodeKind.Ordinary)],
            Edges = [Edge("D0", "A", "B"), Edge("D1", "B", "C"), Edge("U1", "C", "B"), Edge("U0", "B", "A")],
            DirectedConnections = [new("D0", TraversalDirection.Forward, "D1", TraversalDirection.Forward),
                new("U1", TraversalDirection.Forward, "U0", TraversalDirection.Forward)] });
        TopologyMovementNavigator Navigator(string id, string edge) => new(graph, new() {
            MovementPlanId = id, Legs = [new() { LegId = id, Traversals = [new(graph.GetRequiredEdge(edge), TraversalDirection.Forward)] }] });
        var down = Navigator("down", "D0");
        var up = Navigator("up", "U0");
        var sameDirection = Navigator("next", "D1");
        if (TopologyGraphDistance.TryGetForwardDistance(graph, down, down.CreateEndCursor(), up, up.CreateStartCursor()) != 200)
            throw new InvalidOperationException("D0 到 U0 必須繞經 C 返回，不能跨過禁止轉向的 B 節點走零距離捷徑。");
        var gap = TopologyGraphDistance.TryGetForwardDistance(graph, down, down.CreateEndCursor(), sameDirection, sameDirection.CreateStartCursor());
        if (gap != 0) throw new InvalidOperationException("合法 D0→D1 邊界距離應為零。");
        var first = new TopologyRouteNavigator(graph, new() { ServiceRouteId = "D", Name = "D", Traversals = [new("D0", TraversalDirection.Forward)] });
        var second = new TopologyRouteNavigator(graph, new() { ServiceRouteId = "U", Name = "U", Traversals = [new("U0", TraversalDirection.Forward)] });
        if (TopologyGraphDistance.TryGetForwardDistance(graph, first, new("D", 0, new("D0", 100)), second, new("U", 0, new("U0", 0))) != 200)
            throw new InvalidOperationException("Route navigator 也必須遵守端點轉向限制。");
    }
}
