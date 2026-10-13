namespace MrtRouteSimulator.Engine;

public sealed partial class SimulationWorld
{
    private readonly Dictionary<string, RuntimeServiceRoute> _runtimeServiceRoutes = new(StringComparer.OrdinalIgnoreCase);
    private sealed record RuntimeServiceRoute(ServiceRouteDefinition Route, ResolvedRunRouteContext Context,
        RouteProjection Projection, TopologyRouteNavigator Navigator, TopologyMovementNavigator Movement,
        IReadOnlyDictionary<string, ResolvedStop> Stops);

    private void InitializeRuntimeServiceRoutes()
    {
        var routes = (_linearTopology.AllServiceRoutes ?? [])
            .Concat([_linearTopology.OutboundServiceRoute, _linearTopology.InboundServiceRoute])
            .DistinctBy(route => route.ServiceRouteId, StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            var context = ResolvedRunRouteContext.Create(TopologyInfrastructure, route);
            _runtimeServiceRoutes.Add(route.ServiceRouteId, new(route, context,
                new RouteProjection(TopologyInfrastructure, route), new TopologyRouteNavigator(TopologyInfrastructure, route),
                new TopologyMovementNavigator(TopologyInfrastructure, TopologyMovementPlanResolver.ResolveServiceRoute(TopologyInfrastructure, route)),
                context.Stops.ToDictionary(stop => stop.StationId, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private RuntimeServiceRoute RuntimeRoute(PlannedServiceRun run)
    {
        var id = run.ServiceRouteId ?? GetServiceRouteId(run.Direction);
        if (!_runtimeServiceRoutes.TryGetValue(id, out var state))
            throw new SimulationValidationException([$"車次 {run.ServiceRunId} 指定不存在的營運路徑 {id}。"]);
        if (state.Route.Direction is { } direction && direction != run.Direction)
            throw new SimulationValidationException([$"車次 {run.ServiceRunId} 的方向與營運路徑 {id} 不一致。"]);
        return state;
    }

    private RuntimeServiceRoute RuntimeRoute(MutableTrain train)
    {
        var runId = train.ServiceRunId;
        if (train.ActiveServiceRouteId is null || train.RouteOwnerServiceRunId != runId)
        {
            var run = _dispatchRunsById.GetValueOrDefault(runId)
                ?? (train.DispatchServiceRunBaseId is { } initialId ? _dispatchRunsById.GetValueOrDefault(initialId) : null);
            train.ActiveServiceRouteId = run is not null ? RuntimeRoute(run).Route.ServiceRouteId : GetServiceRouteId(train.Direction);
            train.RouteOwnerServiceRunId = runId;
        }
        var state = _runtimeServiceRoutes[train.ActiveServiceRouteId];
        if (GetDirectionForServiceRoute(state.Route.ServiceRouteId) != train.Direction)
        {
            var opposite = state.Route.OperatingLineId is { } line
                ? _runtimeServiceRoutes.Values.SingleOrDefault(candidate => candidate.Route.OperatingLineId == line
                    && candidate.Route.Direction == train.Direction) : null;
            state = opposite ?? _runtimeServiceRoutes[GetServiceRouteId(train.Direction)];
            train.ActiveServiceRouteId = state.Route.ServiceRouteId;
        }
        return state;
    }

    private ResolvedRunRouteContext GetRunRouteContext(MutableTrain train) => RuntimeRoute(train).Context;
    private TopologyRouteNavigator GetTopologyNavigator(MutableTrain train) => RuntimeRoute(train).Navigator;
    private TopologyMovementNavigator GetMainlineMovementNavigator(MutableTrain train) => RuntimeRoute(train).Movement;
    private RouteProjection GetRouteProjection(MutableTrain train) => RuntimeRoute(train).Projection;
    private string GetServiceRouteId(MutableTrain train) => RuntimeRoute(train).Route.ServiceRouteId;
    private ServiceRouteDefinition GetServiceRouteDefinition(MutableTrain train) => RuntimeRoute(train).Route;
    private int GetStationCount(MutableTrain train) => GetRunRouteContext(train).Stops.Count;
    private Station GetRuntimeRouteStation(RuntimeServiceRoute state, int index)
    {
        var stop = state.Context.Stops[index];
        var station = TopologyInfrastructure.Stations[stop.StationId];
        return new Station(station.StationId, station.Name, stop.ChainageMeters, station.DefaultDwellTimeSeconds);
    }

    public RouteProjection GetRouteProjection(PlannedServiceRun run) => RuntimeRoute(run).Projection;
}
