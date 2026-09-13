namespace MrtRouteSimulator.Engine;

/// <summary>
/// Schema 8 編輯期間的單一草稿。草稿可以暫時不完整，但只有 <see cref="Commit"/>
/// 會將它送進 persistence/runtime；因此 Cancel 不會污染原始 document。
/// </summary>
public sealed class ProjectEditorState
{
    public ProjectEditorState(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Draft = document;
    }

    public TopologyProjectDocument Draft { get; private set; }

    public void Replace(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Draft = document;
    }

    public IReadOnlyList<ProjectValidationMessage> Validate() =>
        ProjectEditorValidationService.Validate(Draft);

    public TopologyProjectDocument Commit()
    {
        TopologyProjectFormat.Validate(Draft);
        return Draft;
    }
}

/// <summary>Editor／validation panel 共用的可導向訊息。TargetId 供 UI 跳到對應物件。</summary>
public sealed record ProjectValidationMessage(
    ProjectValidationSeverity Severity,
    string Message,
    string? TargetId = null,
    ProjectValidationTargetKind TargetKind = ProjectValidationTargetKind.Unknown,
    string? FieldName = null)
{
    public override string ToString() => $"{Severity.ToString().ToUpperInvariant()}  {Message}";
}

public enum ProjectValidationSeverity { Error, Warning, Info }

public enum ProjectValidationTargetKind
{
    Unknown, Project, Node, Edge, Station, Platform, Resource, SpeedLimit, Gradient,
    ServiceRoute, StopPattern, VehicleType, ServiceType, HeadwayPlan, ManualTimetable,
    TurnbackFacility, PassingFacility, Simulation
}

/// <summary>
/// Schema 8 的 application validation boundary。它故意不要求草稿在每一筆編輯後皆有效，
/// 但 Save／Run 前必須沒有 Error。
/// </summary>
public static class ProjectEditorValidationService
{
    public static IReadOnlyList<ProjectValidationMessage> Validate(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var messages = new List<ProjectValidationMessage>();
        try
        {
            TopologyProjectFormat.Validate(document);
        }
        catch (SimulationValidationException exception)
        {
            messages.AddRange(exception.Errors.Select(error => CreateError(error, document)));
        }

        var routedEdges = document.ServiceRoutes
            .SelectMany(route => route.Traversals)
            .Select(traversal => traversal.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in document.Topology.Edges.Where(edge => !routedEdges.Contains(edge.TrackEdgeId)))
        {
            messages.Add(new ProjectValidationMessage(
                ProjectValidationSeverity.Warning,
                $"軌道「{edge.TrackEdgeId}」尚未被任何 Service Route 使用。",
                edge.TrackEdgeId,
                ProjectValidationTargetKind.Edge));
        }

        var autoResources = document.Topology.Resources
            .Where(resource => resource.ResourceId.StartsWith("AUTO:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var resource in autoResources)
        {
            messages.Add(new ProjectValidationMessage(
                ProjectValidationSeverity.Info,
                $"資源「{resource.Name}」由設施精靈自動建立。",
                resource.ResourceId,
                ProjectValidationTargetKind.Resource));
        }

        return messages;
    }

    public static ProjectValidationMessage CreateError(string error, TopologyProjectDocument document) =>
        ProjectValidationTargetResolver.CreateError(error, document);
}

/// <summary>Schema 8 document 的 mapper。沒有 legacy Route／Station position 作為中介 state。</summary>
public static class ProjectDocumentMapper
{
    public static ProjectEditorState CreateEditorState(TopologyProjectDocument document) => new(document);

    public static TopologyProjectDocument BuildQuickLinearProject(
        TopologyProjectDocument template,
        IEnumerable<QuickLinearStationInput> stations,
        bool createBidirectionalTracks = true,
        bool createDirectionalPlatforms = true,
        bool createDirectionalServiceRoutes = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        var values = stations?.ToArray() ?? [];
        if (!createBidirectionalTracks || !createDirectionalPlatforms || !createDirectionalServiceRoutes)
        {
            throw new SimulationValidationException([
                "第一版快速建立固定產生雙線、上下行月台與上下行 Service Route；取消其中一項會產生不完整的 Schema 8 專案。"
            ]);
        }

        var position = 0d;
        var routeStations = values.Select((item, index) =>
        {
            if (index > 0) position += item.DistanceFromPreviousMeters;
            return new Station(item.StationId.Trim(), item.StationName.Trim(), position, item.DwellTimeSeconds);
        }).ToArray();
        var route = new Route(template.ProjectId, template.ProjectName, routeStations);
        var build = LinearInfrastructureBuilder.Build(route, template.Train.MaxSpeedMetersPerSecond);
        var topology = new TopologyInfrastructureDefinition
        {
            Nodes = build.Infrastructure.Nodes.Values.ToArray(),
            Edges = build.Infrastructure.Edges.Values.Select(e =>
                build.OutboundServiceRoute.Traversals.Any(t => t.TrackEdgeId == e.TrackEdgeId)
                    ? e with { FromPortSide = TrackPortSide.B, ToPortSide = TrackPortSide.A }
                    : e with { FromPortSide = TrackPortSide.A, ToPortSide = TrackPortSide.B }).ToArray(),
            Stations = build.Infrastructure.Stations.Values.ToArray(),
            Platforms = build.Infrastructure.Platforms.Values.ToArray(),
            Resources = [],
            SpeedLimits = [],
            Gradients = [],
            Curves = []
        };
        var outboundOrigin = build.OutboundServiceRoute.Stops[0].CandidatePlatformIds.Single();
        var inboundOrigin = build.InboundServiceRoute.Stops[0].CandidatePlatformIds.Single();
        var stationIds = routeStations.Select(item => item.StationId).ToArray();
        var stopPatterns = template.StopPatterns.Select(pattern => pattern with
        {
            Instructions = stationIds.Select(stationId => new ProjectStopPatternInstruction(
                stationId,
                pattern.Instructions.FirstOrDefault(item => item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))?.Action
                    ?? StopPatternAction.Stop,
                pattern.Instructions.FirstOrDefault(item => item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))?.DwellTimeSeconds,
                pattern.Instructions.FirstOrDefault(item => item.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))?.PassingSpeedLimitMetersPerSecond)).ToArray()
        }).ToArray();
        var dispatch = template.Dispatch with
        {
            SimpleHeadwayPlans = (template.Dispatch.SimpleHeadwayPlans ?? []).Select(plan => plan with
            {
                OriginPlatformId = plan.Direction == TrainDirection.Outbound ? outboundOrigin : inboundOrigin
            }).ToArray(),
            ManualTimetableRows = (template.Dispatch.ManualTimetableRows ?? []).Select(row => row with
            {
                OriginPlatformId = row.Direction == TrainDirection.Outbound ? outboundOrigin : inboundOrigin
            }).ToArray()
        };
        topology = topology with
        {
            DirectedConnections = DirectedTrackConnectionRules.Build(
                topology,
                [build.OutboundServiceRoute, build.InboundServiceRoute])
        };
        return template with
        {
            Topology = topology,
            ServiceRoutes = [build.OutboundServiceRoute, build.InboundServiceRoute],
            DirectionRouteBindings =
            [
                new TopologyDirectionRouteBinding(TrainDirection.Outbound, build.OutboundServiceRoute.ServiceRouteId),
                new TopologyDirectionRouteBinding(TrainDirection.Inbound, build.InboundServiceRoute.ServiceRouteId)
            ],
            StopPatterns = stopPatterns,
            Dispatch = dispatch
        };
    }
}

/// <summary>快速建立畫面暫存的線性輸入；建立後不會保存到 Schema 8 document。</summary>
public sealed record QuickLinearStationInput(
    string StationId,
    string StationName,
    double DistanceFromPreviousMeters,
    double DwellTimeSeconds);

/// <summary>刪除前可顯示給使用者的「誰仍在使用我」訊息。</summary>
public sealed record TopologyDependencyReference(string Kind, string Id, string DisplayName);

public static class TopologyDependencyAnalyzer
{
    public static IReadOnlyList<TopologyDependencyReference> GetTrackEdgeReferences(
        TopologyProjectDocument document,
        string trackEdgeId)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<TopologyDependencyReference>();
        var topology = document.Topology;
        result.AddRange(topology.Platforms.Where(item => Same(item.TrackEdgeId, trackEdgeId))
            .Select(item => Ref("Platform", item.PlatformId, item.Name)));
        result.AddRange(topology.SpeedLimits.Where(item => Same(item.TrackEdgeId, trackEdgeId))
            .Select(item => Ref("SpeedLimit", item.SpeedLimitId, item.SpeedLimitId)));
        result.AddRange(topology.Gradients.Where(item => Same(item.TrackEdgeId, trackEdgeId))
            .Select(item => Ref("Gradient", item.GradientId, item.GradientId)));
        result.AddRange(topology.Curves.Where(item => Same(item.TrackEdgeId, trackEdgeId))
            .Select(item => Ref("Curve", item.CurveId, item.CurveId)));
        result.AddRange(topology.Edges.Where(item => item.ConflictResourceIds.Any(id => Same(id, trackEdgeId)))
            .Select(item => Ref("ResourceOwner", item.TrackEdgeId, item.TrackEdgeId)));
        result.AddRange(topology.DirectedConnections.Where(item => Same(item.FromTrackEdgeId, trackEdgeId) || Same(item.ToTrackEdgeId, trackEdgeId))
            .Select(item => Ref("DirectedConnection", $"{item.FromTrackEdgeId}>{item.ToTrackEdgeId}", "有向道岔轉向")));
        result.AddRange(topology.TurnbackFacilities.Where(item => Same(item.ArrivalTrackEdgeId, trackEdgeId)
                || Same(item.DepartureTrackEdgeId, trackEdgeId)
                || item.Traversals.Any(traversal => Same(traversal.TrackEdgeId, trackEdgeId)))
            .Select(item => Ref("TurnbackFacility", item.FacilityId, item.Name)));
        result.AddRange(topology.PassingFacilities.Where(item => Same(item.ArrivalTrackEdgeId, trackEdgeId)
                || Same(item.DepartureTrackEdgeId, trackEdgeId)
                || item.Traversals.Any(traversal => Same(traversal.TrackEdgeId, trackEdgeId)))
            .Select(item => Ref("PassingFacility", item.FacilityId, item.Name)));
        result.AddRange(document.ServiceRoutes.Where(route => route.Traversals.Any(item => Same(item.TrackEdgeId, trackEdgeId)))
            .Select(route => Ref("ServiceRoute", route.ServiceRouteId, route.Name)));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetTrackNodeReferences(
        TopologyProjectDocument document,
        string nodeId) => document.Topology.Edges
            .Where(edge => Same(edge.FromNodeId, nodeId) || Same(edge.ToNodeId, nodeId))
            .Select(edge => Ref("TrackEdge", edge.TrackEdgeId, edge.TrackEdgeId))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<TopologyDependencyReference> GetStationReferences(
        TopologyProjectDocument document,
        string stationId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange(document.Topology.Platforms.Where(item => Same(item.StationId, stationId))
            .Select(item => Ref("Platform", item.PlatformId, item.Name)));
        result.AddRange(document.ServiceRoutes.Where(route => route.Stops.Any(stop => Same(stop.StationId, stationId)))
            .Select(route => Ref("ServiceRoute", route.ServiceRouteId, route.Name)));
        result.AddRange(document.StopPatterns.Where(pattern => pattern.Instructions.Any(item => Same(item.StationId, stationId)))
            .Select(pattern => Ref("StopPattern", pattern.Id, pattern.DisplayName)));
        result.AddRange(document.Topology.StationOperations.Where(operation => Same(operation.StationId, stationId))
            .Select(operation => Ref("StationOperation", operation.StationOperationId, operation.StationOperationId)));
        result.AddRange(document.Topology.PassingFacilities.Where(facility => Same(facility.StationId, stationId))
            .Select(facility => Ref("PassingFacility", facility.FacilityId, facility.Name)));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetPlatformReferences(
        TopologyProjectDocument document,
        string platformId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange(document.Topology.Stations.Where(station => station.PlatformIds.Any(id => Same(id, platformId)))
            .Select(station => Ref("Station", station.StationId, station.Name)));
        result.AddRange(document.Topology.StationOperations.Where(operation => operation.ArrivalPlatformIds.Concat(operation.DeparturePlatformIds).Any(id => Same(id, platformId)))
            .Select(operation => Ref("StationOperation", operation.StationOperationId, operation.StationOperationId)));
        result.AddRange(document.ServiceRoutes.Where(route => route.Stops.Any(stop => stop.CandidatePlatformIds.Any(id => Same(id, platformId))))
            .Select(route => Ref("ServiceRoute", route.ServiceRouteId, route.Name)));
        result.AddRange((document.Dispatch.SimpleHeadwayPlans ?? []).Where(plan => Same(plan.OriginPlatformId, platformId))
            .Select(plan => Ref("Dispatch", $"Headway:{plan.Direction}", "班距計畫")));
        result.AddRange((document.Dispatch.ManualTimetableRows ?? []).Where(row => Same(row.OriginPlatformId, platformId))
            .Select(row => Ref("Dispatch", row.ServiceRunId ?? $"Manual:{row.PlannedDepartureTimeSeconds}", "手動班表")));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetTurnbackFacilityReferences(TopologyProjectDocument document, string facilityId) =>
        document.Topology.TurnbackOperations.Where(operation => Same(operation.FacilityId, facilityId))
            .Select(operation => Ref("TurnbackOperation", operation.OperationId, operation.OperationId))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<TopologyDependencyReference> GetPassingFacilityReferences(TopologyProjectDocument document, string facilityId) =>
        document.Topology.PassingOperations.Where(operation => Same(operation.FacilityId, facilityId))
            .Select(operation => Ref("PassingOperation", operation.OperationId, operation.OperationId))
            .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<TopologyDependencyReference> GetServiceRouteReferences(TopologyProjectDocument document, string routeId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange(document.DirectionRouteBindings.Where(binding => Same(binding.ServiceRouteId, routeId))
            .Select(binding => Ref("DirectionBinding", binding.Direction.ToString(), binding.Direction.ToString())));
        result.AddRange(document.Topology.TurnbackOperations.Where(operation => Same(operation.ArrivalServiceRouteId, routeId) || Same(operation.DepartureServiceRouteId, routeId))
            .Select(operation => Ref("TurnbackOperation", operation.OperationId, operation.OperationId)));
        result.AddRange(document.Topology.PassingOperations.Where(operation => Same(operation.ServiceRouteId, routeId))
            .Select(operation => Ref("PassingOperation", operation.OperationId, operation.OperationId)));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetVehicleTypeReferences(TopologyProjectDocument document, string vehicleTypeId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange(document.ServiceTypes.Where(item => Same(item.DefaultVehicleTypeId, vehicleTypeId))
            .Select(item => Ref("ServiceType", item.Id, item.DisplayName)));
        result.AddRange((document.Dispatch.SimpleHeadwayPlans ?? []).Where(item => Same(item.VehicleTypeId, vehicleTypeId))
            .Select(item => Ref("Dispatch", $"Headway:{item.Direction}", "班距計畫")));
        result.AddRange((document.Dispatch.ManualTimetableRows ?? []).Where(item => Same(item.VehicleTypeId, vehicleTypeId))
            .Select(item => Ref("Dispatch", item.ServiceRunId ?? "手動班表", "手動班表")));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetServiceTypeReferences(TopologyProjectDocument document, string serviceTypeId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange((document.Dispatch.SimpleHeadwayPlans ?? []).Where(item => Same(item.ServiceTypeId, serviceTypeId))
            .Select(item => Ref("Dispatch", $"Headway:{item.Direction}", "班距計畫")));
        result.AddRange((document.Dispatch.ManualTimetableRows ?? []).Where(item => Same(item.ServiceTypeId, serviceTypeId))
            .Select(item => Ref("Dispatch", item.ServiceRunId ?? "手動班表", "手動班表")));
        result.AddRange(document.Topology.PassingOperations.Where(item => Same(item.ExpressServiceTypeId, serviceTypeId))
            .Select(item => Ref("PassingOperation", item.OperationId, item.OperationId)));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetStopPatternReferences(TopologyProjectDocument document, string stopPatternId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange(document.VehicleTypes.Where(item => Same(item.DefaultStopPatternId, stopPatternId))
            .Select(item => Ref("VehicleType", item.Id, item.DisplayName)));
        result.AddRange(document.ServiceTypes.Where(item => Same(item.DefaultStopPatternId, stopPatternId))
            .Select(item => Ref("ServiceType", item.Id, item.DisplayName)));
        result.AddRange((document.Dispatch.SimpleHeadwayPlans ?? []).Where(item => Same(item.StopPatternId, stopPatternId))
            .Select(item => Ref("Dispatch", $"Headway:{item.Direction}", "班距計畫")));
        result.AddRange((document.Dispatch.ManualTimetableRows ?? []).Where(item => Same(item.StopPatternId, stopPatternId))
            .Select(item => Ref("Dispatch", item.ServiceRunId ?? "手動班表", "手動班表")));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<TopologyDependencyReference> GetResourceReferences(TopologyProjectDocument document, string resourceId)
    {
        var result = new List<TopologyDependencyReference>();
        result.AddRange(document.Topology.Edges.Where(item => item.ConflictResourceIds.Any(id => Same(id, resourceId)))
            .Select(item => Ref("TrackEdge", item.TrackEdgeId, item.TrackEdgeId)));
        result.AddRange(document.Topology.TurnbackFacilities.Where(item => item.ConflictResourceIds.Any(id => Same(id, resourceId)))
            .Select(item => Ref("TurnbackFacility", item.FacilityId, item.Name)));
        result.AddRange(document.Topology.PassingFacilities.Where(item => item.ConflictResourceIds.Any(id => Same(id, resourceId)))
            .Select(item => Ref("PassingFacility", item.FacilityId, item.Name)));
        return result.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static TopologyDependencyReference Ref(string kind, string id, string displayName) => new(kind, id, displayName);
    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 所有 topology surgery 的 application service。ViewModel 不直接拆 edge、修 reference
/// 或產生 facility resource，避免任何一個編輯器把物理 topology 改成不一致狀態。
/// </summary>
public static class TopologyEditingService
{
    public static TopologyProjectDocument AddTrackNode(TopologyProjectDocument document, string name, TrackNodeKind kind = TrackNodeKind.Ordinary)
    {
        var id = CreateStableId("NODE", document.Topology.Nodes.Select(item => item.NodeId));
        return document with { Topology = document.Topology with { Nodes = document.Topology.Nodes.Append(new TrackNodeDefinition(id, name.Trim(), kind)).ToArray() } };
    }

    public static TopologyProjectDocument AddTrackEdge(
        TopologyProjectDocument document,
        string fromNodeId,
        string toNodeId,
        double lengthMeters,
        TrackEdgeKind kind = TrackEdgeKind.Mainline,
        TrackDirectionality directionality = TrackDirectionality.Bidirectional,
        double? defaultSpeedLimitMetersPerSecond = null)
    {
        var id = CreateStableId("EDGE", document.Topology.Edges.Select(item => item.TrackEdgeId));
        var edge = new TrackEdgeDefinition
        {
            TrackEdgeId = id,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            LengthMeters = lengthMeters,
            FromPortSide = document.Topology.Edges.Any(e => e.FromPortSide.HasValue) ? TrackPortSide.B : null,
            ToPortSide = document.Topology.Edges.Any(e => e.FromPortSide.HasValue) ? TrackPortSide.A : null,
            Kind = kind,
            Directionality = directionality,
            DefaultSpeedLimitMetersPerSecond = defaultSpeedLimitMetersPerSecond ?? document.Train.MaxSpeedMetersPerSecond
        };
        return document with { Topology = document.Topology with { Edges = document.Topology.Edges.Append(edge).ToArray() } };
    }

    public static TopologyProjectDocument ReplaceTrackNode(TopologyProjectDocument document, TrackNodeDefinition node) =>
        document with { Topology = document.Topology with { Nodes = Replace(document.Topology.Nodes, node, item => item.NodeId).ToArray() } };

    public static TopologyProjectDocument ReplaceTrackEdge(TopologyProjectDocument document, TrackEdgeDefinition edge) =>
        document with { Topology = document.Topology with { Edges = Replace(document.Topology.Edges, edge, item => item.TrackEdgeId).ToArray() } };

    public static TopologyProjectDocument ReplaceStation(TopologyProjectDocument document, StationDefinitionV4 station) =>
        document with { Topology = document.Topology with { Stations = Replace(document.Topology.Stations, station, item => item.StationId).ToArray() } };

    public static TopologyProjectDocument ReplacePlatform(TopologyProjectDocument document, PlatformDefinitionV4 platform) =>
        document with { Topology = document.Topology with { Platforms = Replace(document.Topology.Platforms, platform, item => item.PlatformId).ToArray() } };

    public static TopologyProjectDocument DeleteTrackNode(TopologyProjectDocument document, string nodeId)
    {
        ThrowIfReferenced("軌道節點", nodeId, TopologyDependencyAnalyzer.GetTrackNodeReferences(document, nodeId));
        return document with { Topology = document.Topology with { Nodes = document.Topology.Nodes.Where(item => !Same(item.NodeId, nodeId)).ToArray() } };
    }

    public static TopologyProjectDocument DeleteTrackEdge(TopologyProjectDocument document, string edgeId)
    {
        ThrowIfReferenced("軌道", edgeId, TopologyDependencyAnalyzer.GetTrackEdgeReferences(document, edgeId));
        return document with { Topology = document.Topology with { Edges = document.Topology.Edges.Where(item => !Same(item.TrackEdgeId, edgeId)).ToArray() } };
    }

    public static TopologyProjectDocument DeleteStation(TopologyProjectDocument document, string stationId)
    {
        ThrowIfReferenced("車站", stationId, TopologyDependencyAnalyzer.GetStationReferences(document, stationId));
        return document with { Topology = document.Topology with { Stations = document.Topology.Stations.Where(item => !Same(item.StationId, stationId)).ToArray() } };
    }

    public static TopologyProjectDocument DeletePlatform(TopologyProjectDocument document, string platformId)
    {
        ThrowIfReferenced("月台", platformId, TopologyDependencyAnalyzer.GetPlatformReferences(document, platformId));
        return document with { Topology = document.Topology with { Platforms = document.Topology.Platforms.Where(item => !Same(item.PlatformId, platformId)).ToArray() } };
    }

    public static TopologyProjectDocument DeleteTurnbackFacility(TopologyProjectDocument document, string facilityId)
    {
        ThrowIfReferenced("折返設施", facilityId, TopologyDependencyAnalyzer.GetTurnbackFacilityReferences(document, facilityId));
        return document with { Topology = document.Topology with { TurnbackFacilities = document.Topology.TurnbackFacilities.Where(item => !Same(item.FacilityId, facilityId)).ToArray() } };
    }

    public static TopologyProjectDocument DeletePassingFacility(TopologyProjectDocument document, string facilityId)
    {
        ThrowIfReferenced("待避設施", facilityId, TopologyDependencyAnalyzer.GetPassingFacilityReferences(document, facilityId));
        return document with { Topology = document.Topology with { PassingFacilities = document.Topology.PassingFacilities.Where(item => !Same(item.FacilityId, facilityId)).ToArray() } };
    }

    public static TopologyProjectDocument DeleteServiceRoute(TopologyProjectDocument document, string serviceRouteId)
    {
        ThrowIfReferenced("Service Route", serviceRouteId, TopologyDependencyAnalyzer.GetServiceRouteReferences(document, serviceRouteId));
        return document with { ServiceRoutes = document.ServiceRoutes.Where(item => !Same(item.ServiceRouteId, serviceRouteId)).ToArray() };
    }

    public static TopologyProjectDocument DeleteVehicleType(TopologyProjectDocument document, string vehicleTypeId)
    {
        ThrowIfReferenced("車型", vehicleTypeId, TopologyDependencyAnalyzer.GetVehicleTypeReferences(document, vehicleTypeId));
        return document with { VehicleTypes = document.VehicleTypes.Where(item => !Same(item.Id, vehicleTypeId)).ToArray() };
    }

    public static TopologyProjectDocument DeleteServiceType(TopologyProjectDocument document, string serviceTypeId)
    {
        ThrowIfReferenced("服務類型", serviceTypeId, TopologyDependencyAnalyzer.GetServiceTypeReferences(document, serviceTypeId));
        return document with { ServiceTypes = document.ServiceTypes.Where(item => !Same(item.Id, serviceTypeId)).ToArray() };
    }

    public static TopologyProjectDocument DeleteStopPattern(TopologyProjectDocument document, string stopPatternId)
    {
        ThrowIfReferenced("停站模式", stopPatternId, TopologyDependencyAnalyzer.GetStopPatternReferences(document, stopPatternId));
        return document with { StopPatterns = document.StopPatterns.Where(item => !Same(item.Id, stopPatternId)).ToArray() };
    }

    /// <summary>
    /// 交易式 split edge。所有能在同一 edge-local 座標轉換的 reference 一次更新；任何
    /// 月台跨越 split 點或無法判定的設施會先拒絕，呼叫端保留原 document。
    /// </summary>
    public static TopologyProjectDocument SplitEdge(TopologyProjectDocument document, string edgeId, double offsetMeters)
    {
        ArgumentNullException.ThrowIfNull(document);
        var edge = document.Topology.Edges.SingleOrDefault(item => Same(item.TrackEdgeId, edgeId))
            ?? throw new SimulationValidationException([$"找不到要分割的軌道 edge「{edgeId}」。"]);
        if (!double.IsFinite(offsetMeters) || offsetMeters <= 0 || offsetMeters >= edge.LengthMeters)
            throw new SimulationValidationException([$"分割位置必須位於 edge「{edgeId}」的 0 與 {edge.LengthMeters:0.###} m 之間。"]);

        var spanningPlatforms = document.Topology.Platforms.Where(platform => Same(platform.TrackEdgeId, edgeId)
            && platform.PlatformStartOffsetMeters < offsetMeters - TrackPosition.DefaultToleranceMeters
            && platform.PlatformEndOffsetMeters > offsetMeters + TrackPosition.DefaultToleranceMeters).ToArray();
        if (spanningPlatforms.Length > 0)
        {
            throw new SimulationValidationException([
                $"不能在月台「{spanningPlatforms[0].PlatformId}」範圍內分割 edge；請先調整或拆分月台，原 topology 未變更。"
            ]);
        }

        var nodeId = CreateStableId("NODE:SPLIT", document.Topology.Nodes.Select(item => item.NodeId));
        var leftId = CreateStableId($"{edge.TrackEdgeId}:A", document.Topology.Edges.Select(item => item.TrackEdgeId));
        var rightId = CreateStableId($"{edge.TrackEdgeId}:B", document.Topology.Edges.Select(item => item.TrackEdgeId).Append(leftId));
        var left = edge with { TrackEdgeId = leftId, ToNodeId = nodeId, LengthMeters = offsetMeters,
            ToPortSide = edge.ToPortSide.HasValue ? TrackPortSide.A : null };
        var right = edge with { TrackEdgeId = rightId, FromNodeId = nodeId, LengthMeters = edge.LengthMeters - offsetMeters,
            FromPortSide = edge.FromPortSide.HasValue ? TrackPortSide.B : null };

        string MapEdgeAtOffset(string id, double position, out double mappedPosition)
        {
            if (!Same(id, edgeId)) { mappedPosition = position; return id; }
            if (position <= offsetMeters + TrackPosition.DefaultToleranceMeters)
            {
                mappedPosition = Math.Min(position, offsetMeters);
                return leftId;
            }
            mappedPosition = position - offsetMeters;
            return rightId;
        }

        var topology = document.Topology;
        var platforms = topology.Platforms.Select(platform =>
        {
            if (!Same(platform.TrackEdgeId, edgeId)) return platform;
            var target = platform.StopPositionOffsetMeters <= offsetMeters + TrackPosition.DefaultToleranceMeters ? leftId : rightId;
            var shift = target == rightId ? offsetMeters : 0;
            return platform with
            {
                TrackEdgeId = target,
                PlatformStartOffsetMeters = platform.PlatformStartOffsetMeters - shift,
                StopPositionOffsetMeters = platform.StopPositionOffsetMeters - shift,
                PlatformEndOffsetMeters = platform.PlatformEndOffsetMeters - shift
            };
        }).ToArray();
        var speedLimits = SplitIntervals(topology.SpeedLimits, edgeId, offsetMeters, leftId, rightId,
            item => item.TrackEdgeId, item => item.StartOffsetMeters, item => item.EndOffsetMeters,
            (item, id, start, end, suffix) => item with { SpeedLimitId = suffix is null ? item.SpeedLimitId : $"{item.SpeedLimitId}:{suffix}", TrackEdgeId = id, StartOffsetMeters = start, EndOffsetMeters = end });
        var gradients = SplitIntervals(topology.Gradients, edgeId, offsetMeters, leftId, rightId,
            item => item.TrackEdgeId, item => item.StartOffsetMeters, item => item.EndOffsetMeters,
            (item, id, start, end, suffix) => item with { GradientId = suffix is null ? item.GradientId : $"{item.GradientId}:{suffix}", TrackEdgeId = id, StartOffsetMeters = start, EndOffsetMeters = end });
        var curves = SplitIntervals(topology.Curves, edgeId, offsetMeters, leftId, rightId,
            item => item.TrackEdgeId, item => item.StartOffsetMeters, item => item.EndOffsetMeters,
            (item, id, start, end, suffix) => item with { CurveId = suffix is null ? item.CurveId : $"{item.CurveId}:{suffix}", TrackEdgeId = id, StartOffsetMeters = start, EndOffsetMeters = end });

        IReadOnlyList<DirectedTrackTraversal> MapTraversals(IEnumerable<DirectedTrackTraversal> traversals) => traversals.SelectMany(traversal =>
        {
            if (!Same(traversal.TrackEdgeId, edgeId)) return new[] { traversal };
            return traversal.Direction == TraversalDirection.Forward
                ? new[] { new DirectedTrackTraversal(leftId, traversal.Direction), new DirectedTrackTraversal(rightId, traversal.Direction) }
                : new[] { new DirectedTrackTraversal(rightId, traversal.Direction), new DirectedTrackTraversal(leftId, traversal.Direction) };
        }).ToArray();

        var connections = topology.DirectedConnections.Select(connection => connection with
        {
            FromTrackEdgeId = MapConnectionEdge(connection.FromTrackEdgeId, connection.FromDirection, entering: true),
            ToTrackEdgeId = MapConnectionEdge(connection.ToTrackEdgeId, connection.ToDirection, entering: false)
        }).ToArray();
        var facilities = topology.TurnbackFacilities.Select(facility =>
        {
            var arrivalEdge = MapEdgeAtOffset(facility.ArrivalTrackEdgeId, facility.ArrivalStopOffsetMeters, out var arrivalOffset);
            var departureEdge = MapEdgeAtOffset(facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters, out var departureOffset);
            TrackPosition? stop = facility.TurnbackStopPosition is { } position
                ? new TrackPosition(MapEdgeAtOffset(position.TrackEdgeId, position.OffsetMeters, out var mappedOffset), mappedOffset)
                : null;
            return facility with
            {
                ArrivalTrackEdgeId = arrivalEdge,
                ArrivalStopOffsetMeters = arrivalOffset,
                DepartureTrackEdgeId = departureEdge,
                DepartureStartOffsetMeters = departureOffset,
                Traversals = MapTraversals(facility.Traversals),
                FacilityTrackEdgeIds = MapTraversals(facility.FacilityTrackEdgeIds.Select(id => new DirectedTrackTraversal(id, TraversalDirection.Forward))).Select(item => item.TrackEdgeId).ToArray(),
                TurnbackStopPosition = stop
            };
        }).ToArray();
        var passingFacilities = topology.PassingFacilities.Select(facility =>
        {
            var arrivalEdge = MapEdgeAtOffset(facility.ArrivalTrackEdgeId, facility.ArrivalOffsetMeters, out var arrivalOffset);
            var departureEdge = MapEdgeAtOffset(facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters, out var departureOffset);
            return facility with
            {
                ArrivalTrackEdgeId = arrivalEdge,
                ArrivalOffsetMeters = arrivalOffset,
                DepartureTrackEdgeId = departureEdge,
                DepartureStartOffsetMeters = departureOffset,
                Traversals = MapTraversals(facility.Traversals)
            };
        }).ToArray();
        return document with
        {
            Topology = topology with
            {
                Nodes = topology.Nodes.Append(new TrackNodeDefinition(nodeId, $"{edge.TrackEdgeId} 分割點", TrackNodeKind.Switch)).ToArray(),
                Edges = topology.Edges.Where(item => !Same(item.TrackEdgeId, edgeId)).Append(left).Append(right).ToArray(),
                Platforms = platforms,
                SpeedLimits = speedLimits,
                Gradients = gradients,
                Curves = curves,
                DirectedConnections = connections,
                TurnbackFacilities = facilities,
                PassingFacilities = passingFacilities
            },
            ServiceRoutes = document.ServiceRoutes.Select(route => route with { Traversals = MapTraversals(route.Traversals) }).ToArray()
        };

        string MapConnectionEdge(string id, TraversalDirection direction, bool entering)
        {
            if (!Same(id, edgeId)) return id;
            var reachesToNode = direction == TraversalDirection.Forward;
            return entering == reachesToNode ? rightId : leftId;
        }
    }

    private static IReadOnlyList<T> SplitIntervals<T>(
        IEnumerable<T> items,
        string edgeId,
        double split,
        string leftId,
        string rightId,
        Func<T, string> edgeSelector,
        Func<T, double> startSelector,
        Func<T, double> endSelector,
        Func<T, string, double, double, string?, T> update)
    {
        var result = new List<T>();
        foreach (var item in items)
        {
            if (!Same(edgeSelector(item), edgeId)) { result.Add(item); continue; }
            var start = startSelector(item);
            var end = endSelector(item);
            if (end <= split + TrackPosition.DefaultToleranceMeters)
            {
                result.Add(update(item, leftId, start, Math.Min(end, split), null));
            }
            else if (start >= split - TrackPosition.DefaultToleranceMeters)
            {
                result.Add(update(item, rightId, Math.Max(0, start - split), end - split, null));
            }
            else
            {
                result.Add(update(item, leftId, start, split, "A"));
                result.Add(update(item, rightId, 0, end - split, "B"));
            }
        }
        return result;
    }

    private static IEnumerable<T> Replace<T>(IEnumerable<T> values, T replacement, Func<T, string> id)
    {
        var found = false;
        foreach (var value in values)
        {
            if (Same(id(value), id(replacement))) { yield return replacement; found = true; }
            else yield return value;
        }
        if (!found) throw new SimulationValidationException([$"找不到要更新的項目「{id(replacement)}」。"]);
    }

    private static void ThrowIfReferenced(string kind, string id, IReadOnlyList<TopologyDependencyReference> references)
    {
        if (references.Count == 0) return;
        var details = string.Join(Environment.NewLine, references.Select(reference => $"- {reference.Kind}: {reference.DisplayName}"));
        throw new SimulationValidationException([$"無法刪除{kind}「{id}」。以下項目仍在使用：{Environment.NewLine}{details}"]);
    }

    internal static string CreateStableId(string prefix, IEnumerable<string> existingIds)
    {
        var existing = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}-{index:000}";
            if (!existing.Contains(candidate)) return candidate;
        }
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>設施精靈請求。使用 node 而非 global chainage；若要在 edge 中段接入，先呼叫 SplitEdge。</summary>
public sealed record CrossoverFacilityRequest(string Name, string FromNodeId, string ToNodeId, double LengthMeters, double SpeedLimitMetersPerSecond);
public sealed record TailFacilityRequest(string Name, string ConnectionNodeId, string ArrivalTrackEdgeId, string DepartureTrackEdgeId, double LengthMeters, double SpeedLimitMetersPerSecond, double BufferStopSafetyOffsetMeters, string? ArrivalServiceRouteId = null, string? DepartureServiceRouteId = null, double MinimumDwellTimeSeconds = 0);
public sealed record PocketFacilityRequest(string Name, string EntryNodeId, string ExitNodeId, string ArrivalTrackEdgeId, string DepartureTrackEdgeId, double LengthMeters, double SpeedLimitMetersPerSecond, double StopOffsetMeters, string? ArrivalServiceRouteId = null, string? DepartureServiceRouteId = null, double MinimumDwellTimeSeconds = 0);
public sealed record PassingFacilityRequest(string Name, string StationId, string EntryNodeId, string ExitNodeId, string ArrivalTrackEdgeId, string DepartureTrackEdgeId, string LocalPlatformId, string ExpressPlatformId, double LengthMeters, double SpeedLimitMetersPerSecond, string ServiceRouteId, string? ExpressServiceTypeId = null);

/// <summary>建立 true topology edge、switch/buffer node 與 AUTO resource；絕不產生 virtual track。</summary>
public static class FacilityCreationService
{
    public static TopologyProjectDocument CreateCrossover(TopologyProjectDocument document, CrossoverFacilityRequest request)
    {
        RequireNodes(document, request.FromNodeId, request.ToNodeId);
        RequirePositive(request.LengthMeters, "橫渡線長度");
        RequirePositive(request.SpeedLimitMetersPerSecond, "橫渡線速限");
        var edgeId = TopologyEditingService.CreateStableId("EDGE:XOVER", document.Topology.Edges.Select(item => item.TrackEdgeId));
        var resourceId = TopologyEditingService.CreateStableId("AUTO:RESOURCE:XOVER", document.Topology.Resources.Select(item => item.ResourceId));
        var edge = new TrackEdgeDefinition
        {
            TrackEdgeId = edgeId,
            FromNodeId = request.FromNodeId,
            ToNodeId = request.ToNodeId,
            LengthMeters = request.LengthMeters,
            FromPortSide = document.Topology.Edges.Any(e => e.FromPortSide.HasValue) ? TrackPortSide.B : null,
            ToPortSide = document.Topology.Edges.Any(e => e.FromPortSide.HasValue) ? TrackPortSide.A : null,
            Directionality = TrackDirectionality.Bidirectional,
            Kind = TrackEdgeKind.Crossover,
            DefaultSpeedLimitMetersPerSecond = request.SpeedLimitMetersPerSecond,
            ConflictResourceIds = new HashSet<string>([resourceId], StringComparer.OrdinalIgnoreCase)
        };
        var candidate = document with { Topology = document.Topology with
        {
            Edges = document.Topology.Edges.Append(edge).ToArray(),
            Resources = document.Topology.Resources.Append(new ConflictResourceDefinition(resourceId, $"{request.Name} 衝突區", ConflictResourceKind.Crossover)).ToArray()
        } };
        return candidate with
        {
            Topology = candidate.Topology with
            {
                DirectedConnections = DirectedTrackConnectionRules.Build(candidate)
            }
        };
    }

    public static TopologyProjectDocument CreateTailTrack(TopologyProjectDocument document, TailFacilityRequest request)
    {
        RequireNodes(document, request.ConnectionNodeId);
        RequirePositive(request.LengthMeters, "尾軌長度");
        RequirePositive(request.SpeedLimitMetersPerSecond, "尾軌速限");
        if (request.BufferStopSafetyOffsetMeters < 0 || request.BufferStopSafetyOffsetMeters >= request.LengthMeters)
            throw new SimulationValidationException(["尾軌 buffer-stop 安全餘量必須大於等於 0 且小於尾軌長度。"]);
        var topology = document.Topology;
        var bufferNodeId = TopologyEditingService.CreateStableId("NODE:BUFFER", topology.Nodes.Select(item => item.NodeId));
        var throatNodeId = TopologyEditingService.CreateStableId("NODE:TAIL:THROAT", topology.Nodes.Select(item => item.NodeId).Append(bufferNodeId));
        var outId = TopologyEditingService.CreateStableId("EDGE:TAIL:OUT", topology.Edges.Select(item => item.TrackEdgeId));
        var entryId = TopologyEditingService.CreateStableId("EDGE:TAIL:ENTRY", topology.Edges.Select(item => item.TrackEdgeId));
        var exitId = TopologyEditingService.CreateStableId("EDGE:TAIL:EXIT", topology.Edges.Select(item => item.TrackEdgeId));
        var resourceId = TopologyEditingService.CreateStableId("AUTO:RESOURCE:TAIL", topology.Resources.Select(item => item.ResourceId));
        var facilityId = TopologyEditingService.CreateStableId("FACILITY:TAIL", topology.TurnbackFacilities.Select(item => item.FacilityId));
        var arrivalOffset = ResolveNodeOffset(topology, request.ArrivalTrackEdgeId, request.ConnectionNodeId, isArrival: true);
        var departureOffset = ResolveNodeOffset(topology, request.DepartureTrackEdgeId, request.ConnectionNodeId, isArrival: false);
        var arrivalSide = topology.Edges.Single(e => e.TrackEdgeId == request.ArrivalTrackEdgeId).ToPortSide;
        var departureSide = topology.Edges.Single(e => e.TrackEdgeId == request.DepartureTrackEdgeId).FromPortSide;
        var entryEdge = CreateEdge(entryId, request.ConnectionNodeId, throatNodeId, 25, TrackEdgeKind.Crossover, request.SpeedLimitMetersPerSecond, resourceId) with
        { FromPortSide = Opposite(arrivalSide), ToPortSide = arrivalSide is null ? null : TrackPortSide.A };
        var exitEdge = CreateEdge(exitId, throatNodeId, request.ConnectionNodeId, 25, TrackEdgeKind.Crossover, request.SpeedLimitMetersPerSecond, resourceId) with
        { FromPortSide = departureSide is null ? null : TrackPortSide.A, ToPortSide = Opposite(departureSide) };
        var outEdge = CreateEdge(outId, throatNodeId, bufferNodeId, request.LengthMeters, TrackEdgeKind.TailTrack, request.SpeedLimitMetersPerSecond, resourceId) with
        {
            Directionality = TrackDirectionality.Bidirectional,
            FromPortSide = arrivalSide is null ? null : TrackPortSide.B,
            ToPortSide = arrivalSide is null ? null : TrackPortSide.A
        };
        var facility = new TurnbackFacilityDefinition
        {
            FacilityId = facilityId,
            Name = request.Name,
            Kind = TurnbackFacilityKind.TailTrack,
            ArrivalTrackEdgeId = request.ArrivalTrackEdgeId,
            ArrivalStopOffsetMeters = arrivalOffset,
            DepartureTrackEdgeId = request.DepartureTrackEdgeId,
            DepartureStartOffsetMeters = departureOffset,
            Traversals = [new(entryId, TraversalDirection.Forward), new(outId, TraversalDirection.Forward),
                new(outId, TraversalDirection.Reverse), new(exitId, TraversalDirection.Forward)],
            TurnbackStopPosition = new TrackPosition(outId, request.LengthMeters - request.BufferStopSafetyOffsetMeters),
            ConflictResourceIds = new HashSet<string>([resourceId], StringComparer.OrdinalIgnoreCase)
        };
        var operations = request.ArrivalServiceRouteId is { Length: > 0 } arrival && request.DepartureServiceRouteId is { Length: > 0 } departure
            ? topology.TurnbackOperations.Append(new TurnbackOperationDefinition
            {
                OperationId = TopologyEditingService.CreateStableId("TURNBACK", topology.TurnbackOperations.Select(item => item.OperationId)),
                FacilityId = facilityId,
                ArrivalServiceRouteId = arrival,
                DepartureServiceRouteId = departure,
                MinimumDwellTimeSeconds = request.MinimumDwellTimeSeconds
            }).ToArray()
            : topology.TurnbackOperations;
        return document with { Topology = topology with
        {
            Nodes = topology.Nodes.Append(new TrackNodeDefinition(bufferNodeId, $"{request.Name} 止衝", TrackNodeKind.BufferStop))
                .Append(new TrackNodeDefinition(throatNodeId, $"{request.Name} 喉區", TrackNodeKind.Switch)).ToArray(),
            Edges = topology.Edges.Concat([entryEdge, outEdge, exitEdge]).ToArray(),
            Resources = topology.Resources.Append(new ConflictResourceDefinition(resourceId, $"{request.Name} 占用", ConflictResourceKind.TailTrack)).ToArray(),
            DirectedConnections = AppendConnectionsPreservingNaturalTopology(document, [entryEdge, outEdge, exitEdge],
            [
                new DirectedTrackConnectionDefinition(request.ArrivalTrackEdgeId, TraversalDirection.Forward, entryId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(entryId, TraversalDirection.Forward, outId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(outId, TraversalDirection.Forward, outId, TraversalDirection.Reverse),
                new DirectedTrackConnectionDefinition(outId, TraversalDirection.Reverse, exitId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(exitId, TraversalDirection.Forward, request.DepartureTrackEdgeId, TraversalDirection.Forward)
            ]),
            TurnbackFacilities = topology.TurnbackFacilities.Append(facility).ToArray(),
            TurnbackOperations = operations
        } };
    }

    public static TopologyProjectDocument CreatePocketTrack(TopologyProjectDocument document, PocketFacilityRequest request)
    {
        RequireNodes(document, request.EntryNodeId, request.ExitNodeId);
        RequirePositive(request.LengthMeters, "袋狀軌長度");
        RequirePositive(request.SpeedLimitMetersPerSecond, "袋狀軌速限");
        if (request.StopOffsetMeters < 0 || request.StopOffsetMeters > request.LengthMeters)
            throw new SimulationValidationException(["袋狀軌停止位置必須位於袋狀軌長度內。"]);
        var topology = document.Topology;
        var startNode = TopologyEditingService.CreateStableId("NODE:POCKET:IN", topology.Nodes.Select(item => item.NodeId));
        var endNode = TopologyEditingService.CreateStableId("NODE:POCKET:BUFFER", topology.Nodes.Select(item => item.NodeId).Append(startNode));
        var entryId = TopologyEditingService.CreateStableId("EDGE:POCKET:ENTRY", topology.Edges.Select(item => item.TrackEdgeId));
        var pocketId = TopologyEditingService.CreateStableId("EDGE:POCKET", topology.Edges.Select(item => item.TrackEdgeId).Append(entryId));
        var exitId = TopologyEditingService.CreateStableId("EDGE:POCKET:EXIT", topology.Edges.Select(item => item.TrackEdgeId).Append(entryId).Append(pocketId));
        var resourceId = TopologyEditingService.CreateStableId("AUTO:RESOURCE:POCKET", topology.Resources.Select(item => item.ResourceId));
        var facilityId = TopologyEditingService.CreateStableId("FACILITY:POCKET", topology.TurnbackFacilities.Select(item => item.FacilityId));
        var arrivalSide = topology.Edges.Single(e => e.TrackEdgeId == request.ArrivalTrackEdgeId).ToPortSide;
        var departureSide = topology.Edges.Single(e => e.TrackEdgeId == request.DepartureTrackEdgeId).FromPortSide;
        var entryEdge = CreateEdge(entryId, request.EntryNodeId, startNode, 25, TrackEdgeKind.Crossover, request.SpeedLimitMetersPerSecond, resourceId) with
        { FromPortSide = Opposite(arrivalSide), ToPortSide = arrivalSide is null ? null : TrackPortSide.A };
        var pocketEdge = CreateEdge(pocketId, startNode, endNode, request.LengthMeters, TrackEdgeKind.PocketTrack, request.SpeedLimitMetersPerSecond, resourceId) with
        {
            Directionality = TrackDirectionality.Bidirectional,
            FromPortSide = arrivalSide is null ? null : TrackPortSide.B,
            ToPortSide = arrivalSide is null ? null : TrackPortSide.A
        };
        var exitEdge = CreateEdge(exitId, startNode, request.ExitNodeId, 25, TrackEdgeKind.Crossover, request.SpeedLimitMetersPerSecond, resourceId) with
        { FromPortSide = departureSide is null ? null : TrackPortSide.A, ToPortSide = Opposite(departureSide) };
        var facility = new TurnbackFacilityDefinition
        {
            FacilityId = facilityId, Name = request.Name, Kind = TurnbackFacilityKind.PocketTrack,
            ArrivalTrackEdgeId = request.ArrivalTrackEdgeId,
            ArrivalStopOffsetMeters = ResolveNodeOffset(topology, request.ArrivalTrackEdgeId, request.EntryNodeId, isArrival: true),
            DepartureTrackEdgeId = request.DepartureTrackEdgeId,
            DepartureStartOffsetMeters = ResolveNodeOffset(topology, request.DepartureTrackEdgeId, request.ExitNodeId, isArrival: false),
            Traversals =
            [
                new DirectedTrackTraversal(entryId, TraversalDirection.Forward),
                new DirectedTrackTraversal(pocketId, TraversalDirection.Forward),
                new DirectedTrackTraversal(pocketId, TraversalDirection.Reverse),
                new DirectedTrackTraversal(exitId, TraversalDirection.Forward)
            ],
            TurnbackStopPosition = new TrackPosition(pocketId, request.StopOffsetMeters),
            ConflictResourceIds = new HashSet<string>([resourceId], StringComparer.OrdinalIgnoreCase)
        };
        var operations = request.ArrivalServiceRouteId is { Length: > 0 } arrival && request.DepartureServiceRouteId is { Length: > 0 } departure
            ? topology.TurnbackOperations.Append(new TurnbackOperationDefinition
            {
                OperationId = TopologyEditingService.CreateStableId("TURNBACK", topology.TurnbackOperations.Select(item => item.OperationId)),
                FacilityId = facilityId, ArrivalServiceRouteId = arrival, DepartureServiceRouteId = departure, MinimumDwellTimeSeconds = request.MinimumDwellTimeSeconds
            }).ToArray()
            : topology.TurnbackOperations;
        return document with { Topology = topology with
        {
            Nodes = topology.Nodes.Append(new TrackNodeDefinition(startNode, $"{request.Name} 入口", TrackNodeKind.Switch)).Append(new TrackNodeDefinition(endNode, $"{request.Name} 止衝", TrackNodeKind.BufferStop)).ToArray(),
            Edges = topology.Edges
                .Append(entryEdge)
                .Append(pocketEdge)
                .Append(exitEdge).ToArray(),
            Resources = topology.Resources.Append(new ConflictResourceDefinition(resourceId, $"{request.Name} 占用", ConflictResourceKind.PocketTrack)).ToArray(),
            DirectedConnections = AppendConnectionsPreservingNaturalTopology(document, [entryEdge, pocketEdge, exitEdge],
            [
                new DirectedTrackConnectionDefinition(request.ArrivalTrackEdgeId, TraversalDirection.Forward, entryId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(entryId, TraversalDirection.Forward, pocketId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(pocketId, TraversalDirection.Forward, pocketId, TraversalDirection.Reverse),
                new DirectedTrackConnectionDefinition(pocketId, TraversalDirection.Reverse, exitId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(exitId, TraversalDirection.Forward, request.DepartureTrackEdgeId, TraversalDirection.Forward)
            ]),
            TurnbackFacilities = topology.TurnbackFacilities.Append(facility).ToArray(),
            TurnbackOperations = operations
        } };
    }

    public static TopologyProjectDocument CreatePassingTrack(TopologyProjectDocument document, PassingFacilityRequest request)
    {
        RequireNodes(document, request.EntryNodeId, request.ExitNodeId);
        RequirePositive(request.LengthMeters, "待避線長度");
        RequirePositive(request.SpeedLimitMetersPerSecond, "待避線速限");
        var topology = document.Topology;
        if (!topology.Stations.Any(item => Same(item.StationId, request.StationId))
            || !topology.Platforms.Any(item => Same(item.PlatformId, request.LocalPlatformId))
            || !topology.Platforms.Any(item => Same(item.PlatformId, request.ExpressPlatformId)))
            throw new SimulationValidationException(["待避設施必須選擇既有車站、普通月台與快速月台。"]);
        var edgeId = TopologyEditingService.CreateStableId("EDGE:PASS", topology.Edges.Select(item => item.TrackEdgeId));
        var resourceId = TopologyEditingService.CreateStableId("AUTO:RESOURCE:PASS", topology.Resources.Select(item => item.ResourceId));
        var facilityId = TopologyEditingService.CreateStableId("FACILITY:PASS", topology.PassingFacilities.Select(item => item.FacilityId));
        var edge = CreateEdge(edgeId, request.EntryNodeId, request.ExitNodeId, request.LengthMeters, TrackEdgeKind.PassingTrack, request.SpeedLimitMetersPerSecond, resourceId) with
        {
            FromPortSide = Opposite(topology.Edges.Single(e => e.TrackEdgeId == request.ArrivalTrackEdgeId).ToPortSide),
            ToPortSide = Opposite(topology.Edges.Single(e => e.TrackEdgeId == request.DepartureTrackEdgeId).FromPortSide)
        };
        var facility = new PassingFacilityDefinition
        {
            FacilityId = facilityId, Name = request.Name, StationId = request.StationId,
            ArrivalTrackEdgeId = request.ArrivalTrackEdgeId,
            ArrivalOffsetMeters = ResolveNodeOffset(topology, request.ArrivalTrackEdgeId, request.EntryNodeId, isArrival: true),
            DepartureTrackEdgeId = request.DepartureTrackEdgeId,
            DepartureStartOffsetMeters = ResolveNodeOffset(topology, request.DepartureTrackEdgeId, request.ExitNodeId, isArrival: false),
            Traversals = [new DirectedTrackTraversal(edgeId, TraversalDirection.Forward)],
            LocalPlatformId = request.LocalPlatformId,
            ExpressPlatformId = request.ExpressPlatformId,
            ConflictResourceIds = new HashSet<string>([resourceId], StringComparer.OrdinalIgnoreCase)
        };
        var operation = new PassingOperationDefinition
        {
            OperationId = TopologyEditingService.CreateStableId("PASSING", topology.PassingOperations.Select(item => item.OperationId)),
            FacilityId = facilityId, ServiceRouteId = request.ServiceRouteId, ExpressServiceTypeId = request.ExpressServiceTypeId
        };
        return document with { Topology = topology with
        {
            Edges = topology.Edges.Append(edge).ToArray(),
            Resources = topology.Resources.Append(new ConflictResourceDefinition(resourceId, $"{request.Name} 占用", ConflictResourceKind.Block)).ToArray(),
            DirectedConnections = AppendConnectionsPreservingNaturalTopology(document, [edge],
            [
                new DirectedTrackConnectionDefinition(request.ArrivalTrackEdgeId, TraversalDirection.Forward, edgeId, TraversalDirection.Forward),
                new DirectedTrackConnectionDefinition(edgeId, TraversalDirection.Forward, request.DepartureTrackEdgeId, TraversalDirection.Forward)
            ]),
            PassingFacilities = topology.PassingFacilities.Append(facility).ToArray(),
            PassingOperations = topology.PassingOperations.Append(operation).ToArray()
        } };
    }

    private static TrackPortSide? Opposite(TrackPortSide? side) => side is { } value ? TrackPortRules.Opposite(value) : null;

    private static TrackEdgeDefinition CreateEdge(string id, string from, string to, double length, TrackEdgeKind kind, double speed, string resourceId) => new()
    {
        TrackEdgeId = id, FromNodeId = from, ToNodeId = to, LengthMeters = length,
        Directionality = TrackDirectionality.ForwardOnly, Kind = kind, DefaultSpeedLimitMetersPerSecond = speed,
        ConflictResourceIds = new HashSet<string>([resourceId], StringComparer.OrdinalIgnoreCase)
    };

    private static IReadOnlyList<DirectedTrackConnectionDefinition> AppendConnectionsPreservingNaturalTopology(
        TopologyProjectDocument document,
        IReadOnlyList<TrackEdgeDefinition> newEdges,
        IReadOnlyList<DirectedTrackConnectionDefinition> requestedConnections)
    {
        var candidate = document with
        {
            Topology = document.Topology with
            {
                Edges = document.Topology.Edges.Concat(newEdges).ToArray()
            }
        };
        return DirectedTrackConnectionRules.Build(candidate, requestedConnections);
    }

    private static double ResolveNodeOffset(TopologyInfrastructureDefinition topology, string edgeId, string nodeId, bool isArrival)
    {
        var edge = topology.Edges.SingleOrDefault(item => Same(item.TrackEdgeId, edgeId))
            ?? throw new SimulationValidationException([$"找不到設施連接 edge「{edgeId}」。"]);
        var valid = isArrival ? Same(edge.ToNodeId, nodeId) : Same(edge.FromNodeId, nodeId);
        if (!valid)
            throw new SimulationValidationException([$"edge「{edgeId}」未以指定方向接到 node「{nodeId}」。請先在正確 node 建立設施或以 SplitEdge 插入接點。"]);
        return isArrival ? edge.LengthMeters : 0;
    }

    private static void RequireNodes(TopologyProjectDocument document, params string[] nodeIds)
    {
        foreach (var nodeId in nodeIds)
        {
            if (!document.Topology.Nodes.Any(item => Same(item.NodeId, nodeId)))
                throw new SimulationValidationException([$"找不到 topology node「{nodeId}」。"]);
        }
    }

    private static void RequirePositive(double value, string field)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new SimulationValidationException([$"{field}必須是有限且大於 0 的數值。"]);
    }

    private static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>ServiceRoute ordered traversal 的 application service。</summary>
public static class ServiceRouteEditingService
{
    public static IReadOnlyList<string> GetCandidatePlatformIds(
        TopologyProjectDocument document,
        ServiceRouteDefinition route,
        string stationId) => document.Topology.Platforms
            .Where(platform => platform.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase)
                && route.Traversals.Any(traversal => traversal.TrackEdgeId.Equals(platform.TrackEdgeId, StringComparison.OrdinalIgnoreCase)))
            .Select(platform => platform.PlatformId)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static TopologyProjectDocument MoveTraversal(TopologyProjectDocument document, string serviceRouteId, int fromIndex, int toIndex)
    {
        var route = GetRoute(document, serviceRouteId);
        if (fromIndex < 0 || fromIndex >= route.Traversals.Count || toIndex < 0 || toIndex >= route.Traversals.Count)
            throw new SimulationValidationException(["要移動的 traversal 索引超出範圍。"]);
        var traversals = route.Traversals.ToList();
        var value = traversals[fromIndex];
        traversals.RemoveAt(fromIndex);
        traversals.Insert(toIndex, value);
        return ReplaceRoute(document, route with { Traversals = traversals.ToArray() });
    }

    public static TopologyProjectDocument BuildShortestPath(
        TopologyProjectDocument document,
        string serviceRouteId,
        string originPlatformId,
        string destinationPlatformId,
        TopologyPathConstraints? constraints = null)
    {
        var infrastructure = new InfrastructureGraphV4(document.Topology);
        var origin = infrastructure.Platforms.GetValueOrDefault(originPlatformId)
            ?? throw new SimulationValidationException([$"找不到起點月台「{originPlatformId}」。"]);
        var destination = infrastructure.Platforms.GetValueOrDefault(destinationPlatformId)
            ?? throw new SimulationValidationException([$"找不到終點月台「{destinationPlatformId}」。"]);
        var originEdge = infrastructure.GetRequiredEdge(origin.TrackEdgeId);
        var destinationEdge = infrastructure.GetRequiredEdge(destination.TrackEdgeId);
        var startNode = origin.AllowedDirection == TrackDirection.Inbound ? originEdge.ToNodeId : originEdge.FromNodeId;
        var endNode = destination.AllowedDirection == TrackDirection.Inbound ? destinationEdge.FromNodeId : destinationEdge.ToNodeId;
        var path = TopologyPathFinder.FindShortestPath(infrastructure, startNode, endNode, constraints);
        var route = GetRoute(document, serviceRouteId) with
        {
            Traversals = path.Traversals,
            Stops = [
                new ServiceRouteStop { StationId = origin.StationId, CandidatePlatformIds = [origin.PlatformId] },
                new ServiceRouteStop { StationId = destination.StationId, CandidatePlatformIds = [destination.PlatformId] }
            ]
        };
        return ReplaceRoute(document, route);
    }

    public static IReadOnlyList<ProjectValidationMessage> ValidateRoute(TopologyProjectDocument document, string serviceRouteId)
    {
        var route = GetRoute(document, serviceRouteId);
        var errors = InfrastructureValidator.Validate(document.Topology, [route]).Errors;
        return errors.Select(error => ProjectEditorValidationService.CreateError(error, document)).ToArray();
    }

    private static ServiceRouteDefinition GetRoute(TopologyProjectDocument document, string id) =>
        document.ServiceRoutes.SingleOrDefault(item => item.ServiceRouteId.Equals(id, StringComparison.OrdinalIgnoreCase))
        ?? throw new SimulationValidationException([$"找不到 Service Route「{id}」。"]);

    private static TopologyProjectDocument ReplaceRoute(TopologyProjectDocument document, ServiceRouteDefinition replacement) =>
        document with { ServiceRoutes = document.ServiceRoutes.Select(item => item.ServiceRouteId.Equals(replacement.ServiceRouteId, StringComparison.OrdinalIgnoreCase) ? replacement : item).ToArray() };
}
