using System.Collections.ObjectModel;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

// Schema 8 editor rows deliberately copy the domain values.  WPF never binds a
// TopologyInfrastructureDefinition directly, so an unfinished DataGrid edit is
// a draft rather than a mutation of the runnable project.
internal sealed class ProjectViewModel(ProjectEditorState state)
{
    public ProjectEditorState State { get; } = state;
    public string ProjectName => State.Draft.ProjectName;
    public string ProjectId => State.Draft.ProjectId;
}

internal sealed class ProjectValidationMessageViewModel(ProjectValidationMessage source)
{
    public ProjectValidationMessage Source { get; } = source;
    public string DisplayMessage => UiDisplayText.ProjectValidation(Source);
}

internal sealed class InfrastructureViewModel
{
    public ObservableCollection<TrackNodeEditorViewModel> Nodes { get; } = [];
    public ObservableCollection<TrackEdgeEditorViewModel> Edges { get; } = [];
    public ObservableCollection<FacilityEditorViewModel> Facilities { get; } = [];
    public ObservableCollection<StationEditorViewModel> Stations { get; } = [];
    public ObservableCollection<PlatformEditorViewModel> Platforms { get; } = [];
    public ObservableCollection<TrackSpeedLimitEditorViewModel> SpeedLimits { get; } = [];
    public ObservableCollection<GradientEditorViewModel> Gradients { get; } = [];
    public ObservableCollection<ResourceEditorViewModel> Resources { get; } = [];
}

internal sealed class ServiceNetworkViewModel
{
    public ObservableCollection<ServiceRouteEditorViewModel> Routes { get; } = [];
    public ObservableCollection<StopPatternEditorViewModel> StopPatterns { get; } = [];
    public ObservableCollection<ServiceTypeEditorViewModel> ServiceTypes { get; } = [];
    public ObservableCollection<VehicleTypeEditorViewModel> VehicleTypes { get; } = [];
}

internal sealed class DispatchViewModel
{
    public ObservableCollection<DispatchRunEditorViewModel> Runs { get; } = [];
    public ObservableCollection<HeadwayPlanEditorViewModel> HeadwayPlans { get; } = [];
    public ObservableCollection<ManualTimetableEditorViewModel> ManualRows { get; } = [];
}

internal sealed class SimulationViewModel(ProjectEditorState state)
{
    public string Engine => "V2";
    public string Schema => $"格式版本 {state.Draft.SchemaVersion}";
    public string Profile => UiDisplayText.Enum(state.Draft.Simulation.ProfileMode);
}

internal sealed class TrackNodeEditorViewModel(TrackNodeDefinition source)
{
    public double? SchematicPosition { get; set; } = source.SchematicPosition;
    public double? SchematicLane { get; set; } = source.SchematicLane;
    public string Id { get; set; } = source.NodeId;
    public string Name { get; set; } = source.Name;
    public TrackNodeKind Kind { get; set; } = source.Kind;
    public TrackNodeDefinition ToDomain() => new(Id.Trim(), Name.Trim(), Kind)
        { SchematicPosition = SchematicPosition, SchematicLane = SchematicLane };
}

internal sealed class TrackEdgeEditorViewModel(TrackEdgeDefinition source)
{
    public TrackPortSide? FromPortSide { get; set; } = source.FromPortSide;
    public TrackPortSide? ToPortSide { get; set; } = source.ToPortSide;
    public double? SchematicLane { get; set; } = source.SchematicLane;
    public string Id { get; set; } = source.TrackEdgeId;
    public string From { get; set; } = source.FromNodeId;
    public string To { get; set; } = source.ToNodeId;
    public double LengthMeters { get; set; } = source.LengthMeters;
    public TrackEdgeKind Kind { get; set; } = source.Kind;
    public TrackDirectionality Directionality { get; set; } = source.Directionality;
    public double DefaultSpeedKmh { get; set; } = source.DefaultSpeedLimitMetersPerSecond * 3.6;
    public TrackEdgeDefinition ToDomain() => new()
    {
        TrackEdgeId = Id.Trim(),
        FromPortSide = FromPortSide,
        ToPortSide = ToPortSide,
        SchematicLane = SchematicLane,
        FromNodeId = From.Trim(),
        ToNodeId = To.Trim(),
        LengthMeters = LengthMeters,
        Kind = Kind,
        Directionality = Directionality,
        DefaultSpeedLimitMetersPerSecond = DefaultSpeedKmh / 3.6,
        ConflictResourceIds = new HashSet<string>(source.ConflictResourceIds, StringComparer.OrdinalIgnoreCase)
    };
}

internal sealed class FacilityEditorViewModel(string id, string name, string kind, string detail, FacilityEditorKind backingKind)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Kind { get; } = kind;
    public string Detail { get; } = detail;
    public FacilityEditorKind BackingKind { get; } = backingKind;
}

internal enum FacilityEditorKind { Turnback, Passing, CrossoverEdge }

internal sealed class StationEditorViewModel(StationDefinitionV4 source)
{
    public string Id { get; set; } = source.StationId;
    public string Name { get; set; } = source.Name;
    public double DefaultDwellSeconds { get; set; } = source.DefaultDwellTimeSeconds;
    public string Platforms { get; set; } = string.Join(", ", source.PlatformIds);
    public StationDefinitionV4 ToDomain() => new()
    {
        StationId = Id.Trim(), Name = Name.Trim(), DefaultDwellTimeSeconds = DefaultDwellSeconds,
        PlatformIds = Platforms.Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    };
}

internal sealed class PlatformEditorViewModel(PlatformDefinitionV4 source)
{
    public string PlatformNumber { get; set; } = source.PlatformNumber;
    public string PlatformBodyId { get; set; } = source.PlatformBodyId;
    public PlatformSide DisplaySide { get; set; } = source.DisplaySide;
    public StopPositionReference StopPositionReference { get; set; } = source.StopPositionReference;
    public string Id { get; set; } = source.PlatformId;
    public string Name { get; set; } = source.Name;
    public string Station { get; set; } = source.StationId;
    public string TrackEdge { get; set; } = source.TrackEdgeId;
    public double StartOffsetMeters { get; set; } = source.PlatformStartOffsetMeters;
    public double StopOffsetMeters { get; set; } = source.StopPositionOffsetMeters;
    public double EndOffsetMeters { get; set; } = source.PlatformEndOffsetMeters;
    public double EffectiveLengthMeters { get; set; } = source.EffectiveLengthMeters;
    public TrackDirection Direction { get; set; } = source.AllowedDirection;
    public bool PassengerService { get; set; } = source.AllowsPassengerService;
    public PlatformDefinitionV4 ToDomain() => new()
    {
        PlatformId = Id.Trim(), Name = Name.Trim(), StationId = Station.Trim(), TrackEdgeId = TrackEdge.Trim(),
        PlatformNumber = PlatformNumber.Trim(), PlatformBodyId = PlatformBodyId.Trim(), DisplaySide = DisplaySide,
        StopPositionReference = StopPositionReference,
        PlatformStartOffsetMeters = StartOffsetMeters, StopPositionOffsetMeters = StopOffsetMeters,
        PlatformEndOffsetMeters = EndOffsetMeters, EffectiveLengthMeters = EffectiveLengthMeters,
        AllowedDirection = Direction, AllowsPassengerService = PassengerService,
        AllowedVehicleTypeIds = new HashSet<string>(source.AllowedVehicleTypeIds, StringComparer.OrdinalIgnoreCase),
        AllowedServiceTypeIds = new HashSet<string>(source.AllowedServiceTypeIds, StringComparer.OrdinalIgnoreCase)
    };
}

internal sealed class TrackSpeedLimitEditorViewModel(TrackSpeedLimitDefinition source)
{
    public string Id { get; set; } = source.SpeedLimitId;
    public string TrackEdge { get; set; } = source.TrackEdgeId;
    public double StartOffsetMeters { get; set; } = source.StartOffsetMeters;
    public double EndOffsetMeters { get; set; } = source.EndOffsetMeters;
    public double LimitKmh { get; set; } = source.LimitMetersPerSecond * 3.6;
    public TraversalDirection Direction { get; set; } = source.Direction;
    public TrackSpeedLimitDefinition ToDomain() => new()
    {
        SpeedLimitId = Id.Trim(), TrackEdgeId = TrackEdge.Trim(), StartOffsetMeters = StartOffsetMeters,
        EndOffsetMeters = EndOffsetMeters, LimitMetersPerSecond = LimitKmh / 3.6, Direction = Direction
    };
}

internal sealed class GradientEditorViewModel(TrackGradientSegment source)
{
    public string Id { get; set; } = source.GradientId;
    public string TrackEdge { get; set; } = source.TrackEdgeId;
    public double StartOffsetMeters { get; set; } = source.StartOffsetMeters;
    public double EndOffsetMeters { get; set; } = source.EndOffsetMeters;
    public double GradePermille { get; set; } = source.GradePermille;
    public TrackGradientSegment ToDomain() => new(Id.Trim(), TrackEdge.Trim(), StartOffsetMeters, EndOffsetMeters, GradePermille);
}

internal sealed class ResourceEditorViewModel(ConflictResourceDefinition source)
{
    public string Id { get; set; } = source.ResourceId;
    public string Name { get; set; } = source.Name;
    public ConflictResourceKind Kind { get; set; } = source.Kind;
    public bool IsAutoGenerated => Id.StartsWith("AUTO:", StringComparison.OrdinalIgnoreCase);
    public ConflictResourceDefinition ToDomain() => new(Id.Trim(), Name.Trim(), Kind);
}

internal sealed class ServiceRouteEditorViewModel(ServiceRouteDefinition source)
{
    public string Id { get; set; } = source.ServiceRouteId;
    public string Name { get; set; } = source.Name;
    public ObservableCollection<TraversalEditorViewModel> Traversals { get; } = source.Traversals.Select(item => new TraversalEditorViewModel(item)).ToObservableCollection();
    public ObservableCollection<ServiceRouteStopEditorViewModel> Stops { get; } = source.Stops.Select(item => new ServiceRouteStopEditorViewModel(item)).ToObservableCollection();
    public ServiceRouteDefinition ToDomain() => new()
    {
        ServiceRouteId = Id.Trim(), Name = Name.Trim(), Traversals = Traversals.Select(item => item.ToDomain()).ToArray(), Stops = Stops.Select(item => item.ToDomain()).ToArray()
    };
}

internal sealed class TraversalEditorViewModel(DirectedTrackTraversal source)
{
    public string TrackEdge { get; set; } = source.TrackEdgeId;
    public TraversalDirection Direction { get; set; } = source.Direction;
    public DirectedTrackTraversal ToDomain() => new(TrackEdge.Trim(), Direction);
}

internal sealed class ServiceRouteStopEditorViewModel(ServiceRouteStop source)
{
    public string Station { get; set; } = source.StationId;
    public string CandidatePlatforms { get; set; } = string.Join(", ", source.CandidatePlatformIds);
    public ServiceRouteStop ToDomain() => new()
    {
        StationId = Station.Trim(),
        CandidatePlatformIds = CandidatePlatforms.Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    };
}

internal sealed class StopPatternEditorViewModel(ProjectStopPattern source)
{
    public string Id { get; set; } = source.Id;
    public string Name { get; set; } = source.DisplayName;
    public ObservableCollection<StopPatternInstructionEditorViewModel> Instructions { get; } = source.Instructions.Select(item => new StopPatternInstructionEditorViewModel(item)).ToObservableCollection();
    public string Stations => string.Join(" → ", Instructions.Select(item => item.Station));
    public ProjectStopPattern ToDomain() => new(Id.Trim(), Name.Trim(), Instructions.Select(item => item.ToDomain()).ToArray());
}

internal sealed class StopPatternInstructionEditorViewModel(ProjectStopPatternInstruction source)
{
    public string Station { get; set; } = source.StationId;
    public StopPatternAction Action { get; set; } = source.Action;
    public double? DwellSeconds { get; set; } = source.DwellTimeSeconds;
    public double? PassingSpeedKmh { get; set; } = source.PassingSpeedLimitMetersPerSecond * 3.6;
    public ProjectStopPatternInstruction ToDomain() => new(Station.Trim(), Action, DwellSeconds,
        PassingSpeedKmh is null ? null : PassingSpeedKmh / 3.6);
}

internal sealed class VehicleTypeEditorViewModel(ProjectVehicleType source)
{
    public string Id { get; set; } = source.Id;
    public string Name { get; set; } = source.DisplayName;
    public double LengthMeters { get; set; } = source.LengthMeters;
    public double MaxSpeedKmh { get; set; } = source.MaxSpeedMetersPerSecond * 3.6;
    public double Acceleration { get; set; } = source.AccelerationMetersPerSecondSquared;
    public double ServiceBrake { get; set; } = source.ServiceBrakeDecelerationMetersPerSecondSquared;
    public double EmergencyBrake { get; set; } = source.EmergencyBrakeDecelerationMetersPerSecondSquared;
    public double Jerk { get; set; } = source.JerkMetersPerSecondCubed;
    public double TractionDecay { get; set; } = source.TractionDecayPerSecond;
    public double CoastingDeceleration { get; set; } = source.CoastingDecelerationMetersPerSecondSquared;
    public string DefaultStopPattern { get; set; } = source.DefaultStopPatternId ?? "";
    public string CompatibleServiceTypes { get; internal set; } = "";
    public ProjectVehicleType ToDomain() => new(Id.Trim(), Name.Trim(), LengthMeters, MaxSpeedKmh / 3.6, Acceleration,
        ServiceBrake, EmergencyBrake, Jerk, TractionDecay, CoastingDeceleration, EditorValue.EmptyToNull(DefaultStopPattern));
}

internal sealed class ServiceTypeEditorViewModel(ProjectServiceType source)
{
    public string Id { get; set; } = source.Id;
    public string Name { get; set; } = source.DisplayName;
    public string ColorHex { get; set; } = source.ColorHex;
    public string RunPrefix { get; set; } = source.RunPrefix;
    public string DefaultStopPattern { get; set; } = source.DefaultStopPatternId ?? "";
    public string DefaultVehicleType { get; set; } = source.DefaultVehicleTypeId ?? "";
    public int Priority { get; set; } = source.Priority;
    public bool CanRequestOvertake { get; set; } = source.CanRequestOvertake;
    public string PreferredPlatforms { get; set; } = string.Join(", ", source.PreferredPlatformIds ?? []);
    public string ServiceRoutes { get; internal set; } = "";
    public ProjectServiceType ToDomain() => new(Id.Trim(), Name.Trim(), ColorHex.Trim(), RunPrefix.Trim(),
        EditorValue.EmptyToNull(DefaultStopPattern), EditorValue.EmptyToNull(DefaultVehicleType), Priority, CanRequestOvertake,
        PreferredPlatforms.Split([',', '，', ';', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}

internal sealed class DispatchRunEditorViewModel(string run, string time, string service, string route, string origin, string vehicle)
{
    public string Run { get; } = run;
    public string Time { get; } = time;
    public string Service { get; } = service;
    public string Route { get; } = route;
    public string Origin { get; } = origin;
    public string Vehicle { get; } = vehicle;
}

internal sealed class HeadwayPlanEditorViewModel(ProjectHeadwayPlan source)
{
    public TrainDirection Direction { get; set; } = source.Direction;
    public double FirstDepartureSeconds { get; set; } = source.FirstDepartureTimeSeconds;
    public double HeadwaySeconds { get; set; } = source.HeadwaySeconds;
    public int RunCount { get; set; } = source.RunCount;
    public string ServiceType { get; set; } = source.ServiceTypeId;
    public string VehicleType { get; set; } = source.VehicleTypeId ?? "";
    public string StopPattern { get; set; } = source.StopPatternId ?? "";
    public string OriginPlatform { get; set; } = source.OriginPlatformId ?? "";
    public string VehicleId { get; set; } = source.VehicleId ?? "";
    public bool ContinueAfterTerminal { get; set; } = source.ContinueAfterTerminal;
    public ProjectHeadwayPlan ToDomain() => new(Direction, FirstDepartureSeconds, HeadwaySeconds, RunCount,
        ServiceType.Trim(), EditorValue.EmptyToNull(VehicleType), EditorValue.EmptyToNull(StopPattern), EditorValue.EmptyToNull(OriginPlatform),
        EditorValue.EmptyToNull(VehicleId), ContinueAfterTerminal);
}

internal sealed class ManualTimetableEditorViewModel(ProjectManualTimetableRow source)
{
    public double DepartureSeconds { get; set; } = source.PlannedDepartureTimeSeconds;
    public TrainDirection Direction { get; set; } = source.Direction;
    public string ServiceType { get; set; } = source.ServiceTypeId;
    public string VehicleType { get; set; } = source.VehicleTypeId ?? "";
    public string StopPattern { get; set; } = source.StopPatternId ?? "";
    public string OriginPlatform { get; set; } = source.OriginPlatformId ?? "";
    public string VehicleId { get; set; } = source.VehicleId ?? "";
    public string ServiceRunId { get; set; } = source.ServiceRunId ?? "";
    public bool ContinueAfterTerminal { get; set; } = source.ContinueAfterTerminal;
    public string ContinuationServiceRunId { get; set; } = source.ContinuationServiceRunId ?? "";
    public ProjectManualTimetableRow ToDomain() => new(DepartureSeconds, Direction, ServiceType.Trim(),
        EditorValue.EmptyToNull(VehicleType), EditorValue.EmptyToNull(StopPattern), EditorValue.EmptyToNull(OriginPlatform), EditorValue.EmptyToNull(VehicleId),
        EditorValue.EmptyToNull(ServiceRunId), ContinueAfterTerminal, EditorValue.EmptyToNull(ContinuationServiceRunId));
}

internal sealed class QuickStationEditorViewModel
{
    public string Id { get; set; } = "S";
    public string Name { get; set; } = "新車站";
    public double DistanceFromPreviousKm { get; set; }
    public double DwellSeconds { get; set; } = 30;
    public QuickLinearStationInput ToDomain() => new(Id.Trim(), Name.Trim(), DistanceFromPreviousKm * 1000, DwellSeconds);
}

internal sealed record SchematicNodePlacement(string NodeId, double X, double Y);

internal static class EditorValue
{
    public static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class SchematicLayoutService
{
    public static IReadOnlyDictionary<string, SchematicNodePlacement> AutoLayout(TopologyProjectDocument document)
    {
        var topology = document.Topology;
        var result = new Dictionary<string, SchematicNodePlacement>(StringComparer.OrdinalIgnoreCase);
        var outboundRouteId = document.DirectionRouteBindings
            .FirstOrDefault(binding => binding.Direction == TrainDirection.Outbound)?.ServiceRouteId;
        var mainline = document.ServiceRoutes.FirstOrDefault(route => route.ServiceRouteId.Equals(
                outboundRouteId, StringComparison.OrdinalIgnoreCase))?.Traversals
            ?? document.ServiceRoutes.FirstOrDefault()?.Traversals
            ?? [];
        // 左右預留站後尾軌與止衝空間；主線節點只決定站間水平順序，雙線 lane
        // 由 schematic renderer 依方向另行錯開。
        var nextX = 135d;
        foreach (var traversal in mainline)
        {
            var edge = topology.Edges.FirstOrDefault(item => item.TrackEdgeId.Equals(traversal.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
            if (edge is null) continue;
            var startNodeId = traversal.Direction == TraversalDirection.Forward ? edge.FromNodeId : edge.ToNodeId;
            var endNodeId = traversal.Direction == TraversalDirection.Forward ? edge.ToNodeId : edge.FromNodeId;
            if (!result.ContainsKey(startNodeId)) result[startNodeId] = new(startNodeId, nextX, 210);
            nextX += Math.Max(95, Math.Min(280, edge.LengthMeters / 5));
            result.TryAdd(endNodeId, new(endNodeId, nextX, 210));
        }

        var unplaced = topology.Nodes.Select(node => node.NodeId)
            .Where(nodeId => !result.ContainsKey(nodeId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var branchIndex = 0;
        while (unplaced.Count > 0)
        {
            var placedAny = false;
            foreach (var edge in topology.Edges.OrderBy(edge => edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase))
            {
                var fromPlaced = result.TryGetValue(edge.FromNodeId, out var from);
                var toPlaced = result.TryGetValue(edge.ToNodeId, out var to);
                if (fromPlaced == toPlaced) continue;

                var known = fromPlaced ? from! : to!;
                var unknownId = fromPlaced ? edge.ToNodeId : edge.FromNodeId;
                if (!unplaced.Contains(unknownId)) continue;

                var horizontalTail = edge.Kind is TrackEdgeKind.TailTrack or TrackEdgeKind.Turnback;
                var outward = known.X <= 360 ? -1d : 1d;
                var branchDirection = branchIndex++ % 2 == 0 ? -1d : 1d;
                var x = horizontalTail ? known.X + outward * 105 : known.X;
                var y = horizontalTail ? known.Y : known.Y + branchDirection * 82;
                result[unknownId] = new(unknownId, Math.Max(35, x), Math.Clamp(y, 55, 365));
                unplaced.Remove(unknownId);
                placedAny = true;
            }

            if (placedAny) continue;

            var index = 0;
            foreach (var nodeId in unplaced.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray())
            {
                result[nodeId] = new(nodeId, 90 + (index % 5) * 150, 70 + (index / 5) * 90);
                unplaced.Remove(nodeId);
                index++;
            }
        }
        return result;
    }

    internal static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> values) => new(values);
}
