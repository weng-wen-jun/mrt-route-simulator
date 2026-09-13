namespace MrtRouteSimulator.Engine;

/// <summary>由既有線性 Route 快速建立 Phase B 的雙向逐區間 topology。</summary>
public static class LinearInfrastructureBuilder
{
    public const double DefaultSpeedLimitMetersPerSecond = 80d / 3.6d;

    public static LinearInfrastructureBuildResult Build(
        Route route,
        double defaultSpeedLimitMetersPerSecond = DefaultSpeedLimitMetersPerSecond)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!double.IsFinite(defaultSpeedLimitMetersPerSecond) || defaultSpeedLimitMetersPerSecond <= 0)
        {
            throw new SimulationValidationException(["線性 topology 預設速限必須是有限且大於 0 的數值。"]);
        }

        var stations = route.Stations;
        var nodes = stations.Select(station => new TrackNodeDefinition(
            NodeId(station.StationId), station.StationName, TrackNodeKind.Ordinary)).ToArray();
        var edges = new List<TrackEdgeDefinition>((stations.Count - 1) * 2);
        var outboundTraversals = new List<DirectedTrackTraversal>(stations.Count - 1);
        var inboundTraversals = new List<DirectedTrackTraversal>(stations.Count - 1);

        for (var index = 0; index < stations.Count - 1; index++)
        {
            var from = stations[index];
            var to = stations[index + 1];
            var length = to.PositionMeters - from.PositionMeters;
            var outboundId = OutboundEdgeId(from.StationId, to.StationId);
            var inboundId = InboundEdgeId(to.StationId, from.StationId);
            edges.Add(CreateEdge(outboundId, from.StationId, to.StationId, length, defaultSpeedLimitMetersPerSecond));
            edges.Add(CreateEdge(inboundId, to.StationId, from.StationId, length, defaultSpeedLimitMetersPerSecond));
            outboundTraversals.Add(new DirectedTrackTraversal(outboundId, TraversalDirection.Forward));
            inboundTraversals.Insert(0, new DirectedTrackTraversal(inboundId, TraversalDirection.Forward));
        }

        var platforms = new List<PlatformDefinitionV4>(stations.Count * 2);
        var topologyStations = new List<StationDefinitionV4>(stations.Count);
        for (var index = 0; index < stations.Count; index++)
        {
            var station = stations[index];
            var outboundPlatform = CreatePlatform(station, TrackDirection.Outbound,
                index < stations.Count - 1
                    ? outboundTraversals[index].TrackEdgeId
                    : outboundTraversals[index - 1].TrackEdgeId,
                index < stations.Count - 1 ? 0 : edges.Single(edge => edge.TrackEdgeId.Equals(outboundTraversals[index - 1].TrackEdgeId, StringComparison.OrdinalIgnoreCase)).LengthMeters);
            var inboundTraversalIndex = index == stations.Count - 1
                ? 0
                : stations.Count - 2 - index;
            // 上行中，終點站從第一條 edge 的 FromNode 發車；其餘車站皆從前一條
            // inbound edge 抵達，故停車點位於該 edge 的 ToNode。
            var inboundStopOffset = index == stations.Count - 1
                ? 0
                : edges.Single(edge => edge.TrackEdgeId.Equals(
                    inboundTraversals[inboundTraversalIndex].TrackEdgeId,
                    StringComparison.OrdinalIgnoreCase)).LengthMeters;
            var inboundPlatform = CreatePlatform(station, TrackDirection.Inbound,
                inboundTraversals[inboundTraversalIndex].TrackEdgeId, inboundStopOffset);
            platforms.Add(outboundPlatform);
            platforms.Add(inboundPlatform);
            topologyStations.Add(new StationDefinitionV4
            {
                StationId = station.StationId,
                Name = station.StationName,
                DefaultDwellTimeSeconds = station.DwellTimeSeconds,
                PlatformIds = [outboundPlatform.PlatformId, inboundPlatform.PlatformId]
            });
        }

        var outboundRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = $"{route.RouteId}:DOWN",
            Name = $"{route.RouteName} 下行",
            Traversals = outboundTraversals,
            Stops = stations.Select(station => new ServiceRouteStop
            {
                StationId = station.StationId,
                CandidatePlatformIds = [OutboundPlatformId(station.StationId)]
            }).ToArray()
        };
        var inboundRoute = new ServiceRouteDefinition
        {
            ServiceRouteId = $"{route.RouteId}:UP",
            Name = $"{route.RouteName} 上行",
            Traversals = inboundTraversals,
            Stops = stations.Reverse().Select(station => new ServiceRouteStop
            {
                StationId = station.StationId,
                CandidatePlatformIds = [InboundPlatformId(station.StationId)]
            }).ToArray()
        };
        var definition = new TopologyInfrastructureDefinition
        {
            Nodes = nodes,
            Edges = edges,
            Stations = topologyStations,
            Platforms = platforms
        };
        InfrastructureValidator.ValidateAndThrow(definition, [outboundRoute, inboundRoute]);
        return new LinearInfrastructureBuildResult(new InfrastructureGraphV4(definition), outboundRoute, inboundRoute);
    }

    private static TrackEdgeDefinition CreateEdge(
        string edgeId,
        string fromStationId,
        string toStationId,
        double lengthMeters,
        double speedLimitMetersPerSecond) => new()
        {
            TrackEdgeId = edgeId,
            FromNodeId = NodeId(fromStationId),
            ToNodeId = NodeId(toStationId),
            LengthMeters = lengthMeters,
            Directionality = TrackDirectionality.ForwardOnly,
            Kind = TrackEdgeKind.Mainline,
            DefaultSpeedLimitMetersPerSecond = speedLimitMetersPerSecond
        };

    private static PlatformDefinitionV4 CreatePlatform(
        Station station,
        TrackDirection direction,
        string edgeId,
        double stopOffsetMeters) => new()
        {
            PlatformId = direction == TrackDirection.Outbound
                ? OutboundPlatformId(station.StationId)
                : InboundPlatformId(station.StationId),
            StationId = station.StationId,
            Name = $"{station.StationName} {(direction == TrackDirection.Outbound ? "下行" : "上行")}月台",
            TrackEdgeId = edgeId,
            PlatformStartOffsetMeters = stopOffsetMeters,
            PlatformEndOffsetMeters = stopOffsetMeters,
            StopPositionOffsetMeters = stopOffsetMeters,
            AllowedDirection = direction,
            EffectiveLengthMeters = 220,
            AllowsPassengerService = true
        };

    private static string NodeId(string stationId) => $"NODE:{stationId}";
    private static string OutboundEdgeId(string fromStationId, string toStationId) => $"EDGE:DOWN:{fromStationId}:{toStationId}";
    private static string InboundEdgeId(string fromStationId, string toStationId) => $"EDGE:UP:{fromStationId}:{toStationId}";
    private static string OutboundPlatformId(string stationId) => $"PLATFORM:{stationId}:DOWN";
    private static string InboundPlatformId(string stationId) => $"PLATFORM:{stationId}:UP";
}

/// <summary>LinearInfrastructureBuilder 的已驗證 topology 與雙向 ServiceRoute。</summary>
public sealed record LinearInfrastructureBuildResult(
    InfrastructureGraphV4 Infrastructure,
    ServiceRouteDefinition OutboundServiceRoute,
    ServiceRouteDefinition InboundServiceRoute)
{
    public ServiceRouteDefinition GetServiceRoute(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? OutboundServiceRoute : InboundServiceRoute;
}
