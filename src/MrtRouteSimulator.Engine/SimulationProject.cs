using System.Text.Json;
using System.Text.Json.Serialization;

namespace MrtRouteSimulator.Engine;

public sealed record ProjectStation(
    string StationId,
    string StationName,
    double DistanceFromPreviousMeters,
    double? DwellTimeSeconds);

public sealed record ProjectTrainSettings(
    double MaxSpeedMetersPerSecond,
    double AccelerationMetersPerSecondSquared,
    double DecelerationMetersPerSecondSquared,
    double DefaultDwellTimeSeconds,
    double OriginTurnaroundTimeSeconds,
    double TerminalTurnaroundTimeSeconds);

public sealed record ProjectOperationalSettings(
    double JerkMetersPerSecondCubed,
    double CoastingRatio,
    double ApproachDistanceMeters,
    double ApproachSpeedMetersPerSecond,
    double TractionFadeRatio,
    double TrainLengthMeters,
    double ServiceBrakingMetersPerSecondSquared,
    double EmergencyBrakingMetersPerSecondSquared,
    double ControlReactionTimeSeconds,
    double BrakeBuildUpTimeSeconds,
    double PositioningErrorMeters,
    double SafetyMarginMeters,
    double AbsoluteMinimumGapMeters);

public sealed record ProjectSpeedLimit(
    double StartPositionMeters,
    double EndPositionMeters,
    double LimitMetersPerSecond,
    SpeedLimitDirection Direction,
    string Note);

public sealed record ProjectRunSettings(
    int TrainCount,
    double? HeadwaySeconds,
    double StartClockSeconds,
    double PlaybackSpeed,
    OperationProfileMode ProfileMode,
    MovingBlockMode MovingBlockMode,
    BrakingEstimationMode BrakingEstimationMode,
    SimulationEngineKind EngineKind = SimulationEngineKind.V2RealisticOperations);

public sealed record ProjectVehicleType(
    string Id, string DisplayName, double LengthMeters, double MaxSpeedMetersPerSecond,
    double AccelerationMetersPerSecondSquared, double ServiceBrakeDecelerationMetersPerSecondSquared,
    double EmergencyBrakeDecelerationMetersPerSecondSquared, double JerkMetersPerSecondCubed,
    double TractionDecayPerSecond, double CoastingDecelerationMetersPerSecondSquared,
    string? DefaultStopPatternId = null);

public sealed record ProjectServiceType(
    string Id, string DisplayName, string ColorHex, string RunPrefix,
    string? DefaultStopPatternId = null, string? DefaultVehicleTypeId = null,
    int Priority = 0, bool CanRequestOvertake = false, string[]? PreferredPlatformIds = null);

public sealed record ProjectStopPatternInstruction(
    string StationId, StopPatternAction Action, double? DwellTimeSeconds = null,
    double? PassingSpeedLimitMetersPerSecond = null);

public sealed record ProjectStopPattern(string Id, string DisplayName, ProjectStopPatternInstruction[] Instructions);

public sealed record ProjectHeadwayPlan(
    TrainDirection Direction, double FirstDepartureTimeSeconds, double HeadwaySeconds, int RunCount,
    string ServiceTypeId, string? VehicleTypeId = null, string? StopPatternId = null,
    string? OriginPlatformId = null, string? VehicleId = null, bool ContinueAfterTerminal = false);

public sealed record ProjectManualTimetableRow(
    double PlannedDepartureTimeSeconds, TrainDirection Direction, string ServiceTypeId,
    string? VehicleTypeId = null, string? StopPatternId = null, string? OriginPlatformId = null,
    string? VehicleId = null, string? ServiceRunId = null, bool ContinueAfterTerminal = false,
    string? ContinuationServiceRunId = null);

public sealed record ProjectDispatchPlan(
    DispatchPlanningMode ActiveMode, VehicleAssignmentMode VehicleAssignmentMode = VehicleAssignmentMode.Automatic,
    ProjectHeadwayPlan[]? SimpleHeadwayPlans = null, ProjectManualTimetableRow[]? ManualTimetableRows = null);

public sealed record ProjectPlatform(
    string PlatformId, string StationId, string Name, TrackDirection Direction,
    double EffectiveLengthMeters, double StoppingPositionMeters = 0, bool AllowsPassengerService = true,
    string[]? AllowedVehicleTypeIds = null, string[]? AllowedServiceTypeIds = null, string[]? TrackSegmentIds = null);

public sealed record ProjectTrackSegment(
    string TrackId, string FromStationId, string ToStationId, double StartPositionMeters, double EndPositionMeters,
    TrackDirection Direction, TrackKind Kind, double EffectiveLengthMeters, double SpeedLimitMetersPerSecond,
    string[]? ConflictResourceIds = null);

public sealed record ProjectRoutePath(
    string PathId, string FromPlatformId, string ToPlatformId, TrackDirection Direction,
    string[] TrackSegmentIds, string[]? ResourceIds = null);

public sealed record ProjectTurnbackPlan(
    string TurnbackId, string Name, string StationId, TurnbackKind Kind,
    string? ArrivalPlatformId = null, string? DeparturePlatformId = null,
    string[]? TrackSegmentIds = null, string[]? ResourceIds = null, double TurnbackTimeSeconds = 0);

public sealed record ProjectStationYard(
    string StationId, string Name, ProjectPlatform[]? Platforms = null, string[]? TrackSegmentIds = null,
    string[]? RoutePathIds = null, ProjectTurnbackPlan[]? TurnbackPlans = null,
    PlatformAllocationStrategy PlatformAllocationStrategy = PlatformAllocationStrategy.Automatic);

public sealed record ProjectStationOvertakeFacility(
    string FacilityId,
    string StationId,
    TrackDirection Direction,
    string MainlineTrackSegmentId,
    string LocalPlatformId,
    string ExpressPlatformId,
    string LocalTrackSegmentId,
    string ExpressTrackSegmentId,
    double EntryPositionMeters,
    string[]? ResourceIds = null);

public sealed record ProjectSpatialReferencePoint(
    string ReferencePointId,
    string StationId,
    string Name,
    SpatialReferencePointKind Kind,
    bool AlternateBerthing = false,
    double MainlineGradePermille = 0,
    double BranchlineGradePermille = 0,
    double DistanceFromStopToCrossoverMeters = 100,
    double CrossoverLengthMeters = 100,
    double DistanceFromCrossoverToTurnbackStopMeters = 100,
    double TurnbackDwellSeconds = 30,
    double SwitchSpeedLimitMetersPerSecond = 40 / 3.6,
    double MainlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
    double BranchlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
    double MainlineSafetyFactor = 1.5,
    double BranchlineSafetyFactor = 1.5,
    double MainlineTrafficRatio = 0.5,
    double StationForwardGradeInPermille = 0,
    double StationForwardGradeOutPermille = 0,
    double StationForwardDistanceToSignalMeters = 15,
    double StationForwardOverlapMeters = 300,
    double StationForwardDwellSeconds = 30,
    double StationForwardEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationForwardLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationForwardSafetyFactor = 1.5,
    double StationReverseGradeInPermille = 0,
    double StationReverseGradeOutPermille = 0,
    double StationReverseDistanceToSignalMeters = 15,
    double StationReverseOverlapMeters = 300,
    double StationReverseDwellSeconds = 30,
    double StationReverseEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationReverseLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationReverseSafetyFactor = 1.5);

/// <summary>五類站場型式的新建站預設參數；不含實體站識別碼，避免範本回寫既有站場覆寫。</summary>
public sealed record ProjectSpatialReferencePointTemplate(
    SpatialReferencePointKind Kind,
    bool AlternateBerthing = false,
    double MainlineGradePermille = 0,
    double BranchlineGradePermille = 0,
    double DistanceFromStopToCrossoverMeters = 100,
    double CrossoverLengthMeters = 100,
    double DistanceFromCrossoverToTurnbackStopMeters = 100,
    double TurnbackDwellSeconds = 30,
    double SwitchSpeedLimitMetersPerSecond = 40 / 3.6,
    double MainlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
    double BranchlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
    double MainlineSafetyFactor = 1.5,
    double BranchlineSafetyFactor = 1.5,
    double MainlineTrafficRatio = 0.5,
    double StationForwardGradeInPermille = 0,
    double StationForwardGradeOutPermille = 0,
    double StationForwardDistanceToSignalMeters = 15,
    double StationForwardOverlapMeters = 300,
    double StationForwardDwellSeconds = 30,
    double StationForwardEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationForwardLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationForwardSafetyFactor = 1.5,
    double StationReverseGradeInPermille = 0,
    double StationReverseGradeOutPermille = 0,
    double StationReverseDistanceToSignalMeters = 15,
    double StationReverseOverlapMeters = 300,
    double StationReverseDwellSeconds = 30,
    double StationReverseEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationReverseLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
    double StationReverseSafetyFactor = 1.5);

public sealed record ProjectInfrastructure(
    ProjectStationYard[] StationYards, ProjectTrackSegment[] TrackSegments, ProjectRoutePath[] Paths,
    ProjectTurnbackPlan[]? TurnbackPlans = null,
    ProjectSpatialReferencePoint[]? SpatialReferencePoints = null,
    ProjectStationOvertakeFacility[]? StationOvertakeFacilities = null,
    ProjectSpatialReferencePointTemplate[]? SpatialReferencePointTemplates = null);

public sealed record SimulationProjectDocument(
    int SchemaVersion,
    string RouteId,
    string RouteName,
    ProjectStation[] Stations,
    ProjectTrainSettings Train,
    ProjectOperationalSettings Operations,
    ProjectSpeedLimit[] SpeedLimits,
    ProjectRunSettings Simulation,
    ProjectVehicleType[]? VehicleTypes = null,
    ProjectServiceType[]? ServiceTypes = null,
    ProjectStopPattern[]? StopPatterns = null,
    ProjectDispatchPlan? Dispatch = null,
    ProjectInfrastructure? Infrastructure = null);

public static class SimulationProjectFormat
{
    public const int CurrentSchemaVersion = 7;

    public const int MaximumJsonCharacters = 2_000_000;

    private static readonly string[] LegacyExecutionPropertyNames = ["servicePatterns", "serviceRuns"];

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string Serialize(SimulationProjectDocument document)
    {
        document = Normalize(document);
        Validate(document);
        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static SimulationProjectDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new SimulationValidationException(["存檔內容是空白的。"]);
        }

        if (json.Length > MaximumJsonCharacters)
        {
            throw new SimulationValidationException([$"存檔內容超過 {MaximumJsonCharacters / 1_000_000} MB 上限。"]);
        }

        try
        {
            RejectLegacyExecutionSources(json);
            var document = JsonSerializer.Deserialize<SimulationProjectDocument>(json, SerializerOptions)
                ?? throw new SimulationValidationException(["無法讀取存檔內容。"]);
            document = Normalize(document);
            Validate(document);
            return document;
        }
        catch (SimulationValidationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new SimulationValidationException([$"存檔 JSON 格式無效：{exception.Message}"]);
        }
    }

    public static void Validate(SimulationProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            errors.Add($"不支援存檔版本 {document.SchemaVersion}；目前支援版本為 {CurrentSchemaVersion}。");
        }

        if (document.Stations is null || document.Stations.Length is < 2 or > 500)
        {
            errors.Add("存檔車站數必須介於 2 至 500 站。");
        }

        if (document.SpeedLimits is null || document.SpeedLimits.Length > 5000)
        {
            errors.Add("存檔速限筆數不得超過 5000 筆。");
        }

        if (document.VehicleTypes is null || document.VehicleTypes.Length is < 1 or > 500)
            errors.Add("車型目錄至少需要 1 組且不得超過 500 組。");
        if (document.ServiceTypes is null || document.ServiceTypes.Length is < 1 or > 500)
            errors.Add("服務類型目錄至少需要 1 組且不得超過 500 組。");
        if (document.StopPatterns is null || document.StopPatterns.Length is < 1 or > 500)
            errors.Add("停站模式目錄至少需要 1 組且不得超過 500 組。");
        if (document.Dispatch is null)
            errors.Add("存檔缺少發車計畫。");
        if (document.Infrastructure is null)
            errors.Add("存檔缺少路線基礎設施設定。");

        if (document.Train is null)
        {
            errors.Add("存檔缺少列車性能設定。");
        }

        if (document.Operations is null)
        {
            errors.Add("存檔缺少 V2 營運設定。");
        }

        if (document.Simulation is null)
        {
            errors.Add("存檔缺少模擬設定。");
        }

        RouteValidator.ThrowIfAny(errors);

        var stations = document.Stations!;
        var speedLimits = document.SpeedLimits!;
        var train = document.Train!;
        var operations = document.Operations!;
        var run = document.Simulation!;
        var vehicleTypes = document.VehicleTypes!;
        var serviceTypes = document.ServiceTypes!;
        var stopPatterns = document.StopPatterns!;
        var routeId = document.RouteId ?? string.Empty;
        var routeName = document.RouteName ?? string.Empty;

        if (document.RouteId?.Length > 100 || document.RouteName?.Length > 200)
        {
            errors.Add("路線編號或名稱過長。");
        }

        if (stations.Any(station => station is null
            || station.StationId?.Length > 100
            || station.StationName?.Length > 200))
        {
            errors.Add("車站編號或名稱過長，或車站資料缺漏。");
        }

        if (speedLimits.Any(limit => limit is null || limit.Note?.Length > 500))
        {
            errors.Add("速限備註過長，或速限資料缺漏。");
        }

        if (run.TrainCount <= 0 || run.TrainCount > 1000)
        {
            errors.Add("列車數量必須介於 1 至 1000。");
        }

        if (run.HeadwaySeconds is { } headway
            && (!RouteValidator.IsFinite(headway) || headway <= 0))
        {
            errors.Add("指定班距必須是有限正數。");
        }

        if (!RouteValidator.IsFinite(run.StartClockSeconds)
            || run.StartClockSeconds < 0
            || run.StartClockSeconds >= 86400)
        {
            errors.Add("首班發車時間必須介於 00:00:00 與 23:59:59。");
        }

        if (!RouteValidator.IsFinite(run.PlaybackSpeed) || run.PlaybackSpeed <= 0)
        {
            errors.Add("播放倍率必須是有限正數。");
        }

        if (!Enum.IsDefined(run.ProfileMode)
            || !Enum.IsDefined(run.MovingBlockMode)
            || !Enum.IsDefined(run.BrakingEstimationMode)
            || !Enum.IsDefined(run.EngineKind))
        {
            errors.Add("存檔包含無效的模擬模式。");
        }

        RouteValidator.ThrowIfAny(errors);

        try
        {
            var route = RouteFactory.FromSegmentDistances(
                routeId,
                routeName,
                stations.Select(station => new StationInput(
                    station.StationId,
                    station.StationName,
                    station.DistanceFromPreviousMeters,
                    station.DwellTimeSeconds)),
                train.DefaultDwellTimeSeconds);
            var vehicleCatalog = vehicleTypes.Select(ToRuntime).ToArray();
            var serviceCatalog = serviceTypes.Select(item => new ServiceTypeDefinition(
                item.Id, item.DisplayName, item.ColorHex, item.RunPrefix, item.DefaultStopPatternId,
                item.DefaultVehicleTypeId, item.Priority, item.CanRequestOvertake, item.PreferredPlatformIds)).ToArray();
            var stopCatalog = stopPatterns.Select(ToRuntime).ToArray();
            var knownStopPatternIds = stopCatalog.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unknownDefaultPatterns = vehicleCatalog
                .Where(item => item.DefaultStopPatternId is { } patternId && !knownStopPatternIds.Contains(patternId))
                .Select(item => $"車型「{item.Id}」={item.DefaultStopPatternId}")
                .Concat(serviceCatalog
                    .Where(item => item.DefaultStopPatternId is { } patternId && !knownStopPatternIds.Contains(patternId))
                    .Select(item => $"服務類型「{item.Id}」={item.DefaultStopPatternId}"))
                .ToArray();
            if (unknownDefaultPatterns.Length > 0)
            {
                throw new SimulationValidationException(["預設停站模式引用不存在：" + string.Join("、", unknownDefaultPatterns)]);
            }
            var resolvedDispatch = DispatchPlanExpander.Expand(ToRuntime(document.Dispatch!), vehicleCatalog, serviceCatalog, stopCatalog);
            var infrastructure = ToRuntime(document.Infrastructure!, route);
            infrastructure.Validate();
            // V1 僅驗證其自身格式，不建立 V2 營運世界，避免被 V2 專屬規則阻擋。
            if (run.EngineKind == SimulationEngineKind.V1BasicPhysics)
            {
                return;
            }
            _ = new TrainParameters(
                train.MaxSpeedMetersPerSecond,
                train.AccelerationMetersPerSecondSquared,
                train.DecelerationMetersPerSecondSquared,
                train.DefaultDwellTimeSeconds,
                train.OriginTurnaroundTimeSeconds,
                train.TerminalTurnaroundTimeSeconds);
            var operationalParameters = new OperationalParameters(
                operations.JerkMetersPerSecondCubed,
                operations.CoastingRatio,
                operations.ApproachDistanceMeters,
                operations.ApproachSpeedMetersPerSecond,
                operations.TractionFadeRatio,
                operations.TrainLengthMeters,
                operations.ServiceBrakingMetersPerSecondSquared,
                operations.EmergencyBrakingMetersPerSecondSquared,
                operations.ControlReactionTimeSeconds,
                operations.BrakeBuildUpTimeSeconds,
                operations.PositioningErrorMeters,
                operations.SafetyMarginMeters,
                operations.AbsoluteMinimumGapMeters);
            var runtimeLimits = speedLimits.Select(limit => new SpeedLimitSegment(
                limit.StartPositionMeters,
                limit.EndPositionMeters,
                limit.LimitMetersPerSecond,
                limit.Direction,
                limit.Note)).ToArray();
            _ = new SpeedLimitService(
                route,
                runtimeLimits);
            _ = new SimulationWorld(
                route,
                new TrainParameters(
                    train.MaxSpeedMetersPerSecond,
                    train.AccelerationMetersPerSecondSquared,
                    train.DecelerationMetersPerSecondSquared,
                    train.DefaultDwellTimeSeconds,
                    train.OriginTurnaroundTimeSeconds,
                    train.TerminalTurnaroundTimeSeconds),
                operationalParameters,
                run.TrainCount,
                run.HeadwaySeconds,
                runtimeLimits,
                run.ProfileMode,
                run.MovingBlockMode,
                stopCatalog.Select(pattern => new ServicePattern(
                    pattern.Id,
                    pattern.DisplayName,
                    pattern.Instructions.Select(instruction => new StationServiceInstruction(
                        instruction.StationId,
                        instruction.Action switch
                        {
                            StopPatternAction.Stop => StationServiceMode.Stop,
                            StopPatternAction.Pass => StationServiceMode.Pass,
                            StopPatternAction.Turnback => StationServiceMode.Turnback,
                            _ => throw new SimulationValidationException(["停站模式動作無效。"])
                        },
                        instruction.PassingSpeedLimitMetersPerSecond,
                        instruction.DwellTimeSeconds)).ToArray())),
                serviceRunPlans: null,
                resolvedDispatch,
                vehicleCatalog,
                infrastructure,
                serviceTypes: serviceCatalog);
        }
        catch (SimulationValidationException exception)
        {
            throw new SimulationValidationException(exception.Errors);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void RejectLegacyExecutionSources(string json)
    {
        using var jsonDocument = JsonDocument.Parse(json);
        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var legacyNames = jsonDocument.RootElement.EnumerateObject()
            .Where(property => LegacyExecutionPropertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (legacyNames.Length > 0)
        {
            throw new SimulationValidationException([
                $"Schema {CurrentSchemaVersion} 不支援舊執行資料來源：{string.Join("、", legacyNames)}。請改用 VehicleTypes、ServiceTypes、StopPatterns 與 Dispatch。"
            ]);
        }
    }

    private static SimulationProjectDocument Normalize(SimulationProjectDocument document)
    {
        var infrastructure = document.Infrastructure;
        if (infrastructure is null && TryBuildRoute(document, out var route))
            infrastructure = FromRuntime(InfrastructureGraph.CreateLegacy(route));
        else if (infrastructure is not null)
        {
            infrastructure = infrastructure with
            {
                SpatialReferencePoints = infrastructure.SpatialReferencePoints ?? [],
                StationOvertakeFacilities = infrastructure.StationOvertakeFacilities ?? [],
                SpatialReferencePointTemplates = infrastructure.SpatialReferencePointTemplates
                    ?? SpatialReferencePointTemplateDefinition.CreateDefaults().Select(FromRuntime).ToArray()
            };
            if (TryBuildRoute(document, out route))
            {
                infrastructure = FromRuntime(ToRuntime(infrastructure, route));
            }
        }

        return document with
        {
            Infrastructure = infrastructure
        };
    }

    private static bool TryBuildRoute(SimulationProjectDocument document, out Route route)
    {
        try
        {
            route = RouteFactory.FromSegmentDistances(document.RouteId ?? string.Empty, document.RouteName ?? string.Empty,
                (document.Stations ?? []).Select(item => new StationInput(item.StationId, item.StationName, item.DistanceFromPreviousMeters, item.DwellTimeSeconds)),
                document.Train?.DefaultDwellTimeSeconds ?? 30);
            return true;
        }
        catch (SimulationValidationException) { route = null!; return false; }
    }

    private static VehicleTypeDefinition ToRuntime(ProjectVehicleType item) => new(item.Id, item.DisplayName, item.LengthMeters,
        item.MaxSpeedMetersPerSecond, item.AccelerationMetersPerSecondSquared, item.ServiceBrakeDecelerationMetersPerSecondSquared,
        item.EmergencyBrakeDecelerationMetersPerSecondSquared, item.JerkMetersPerSecondCubed, item.TractionDecayPerSecond,
        item.CoastingDecelerationMetersPerSecondSquared, item.DefaultStopPatternId);

    private static StopPatternDefinition ToRuntime(ProjectStopPattern item) => new(item.Id, item.DisplayName,
        item.Instructions.Select(instruction => new StopPatternInstruction(instruction.StationId, instruction.Action,
            instruction.DwellTimeSeconds, instruction.PassingSpeedLimitMetersPerSecond)));

    private static DispatchPlanDefinition ToRuntime(ProjectDispatchPlan item) => new(
        item.SimpleHeadwayPlans?.Select(plan => new HeadwayDirectionPlan(plan.Direction, TimeSpan.FromSeconds(plan.FirstDepartureTimeSeconds),
            TimeSpan.FromSeconds(plan.HeadwaySeconds), plan.RunCount, plan.ServiceTypeId, plan.VehicleTypeId, plan.StopPatternId,
            plan.OriginPlatformId, plan.VehicleId, plan.ContinueAfterTerminal)),
        item.ManualTimetableRows?.Select(row => new ManualTimetableRow(TimeSpan.FromSeconds(row.PlannedDepartureTimeSeconds), row.Direction,
            row.ServiceTypeId, row.VehicleTypeId, row.StopPatternId, row.OriginPlatformId, row.VehicleId, row.ServiceRunId,
            row.ContinueAfterTerminal, row.ContinuationServiceRunId)),
        item.ActiveMode, item.VehicleAssignmentMode);

    private static InfrastructureGraph ToRuntime(ProjectInfrastructure item, Route route) => new(route,
        item.StationYards.Select(yard => new StationYardDefinition(yard.StationId, yard.Name,
            (yard.Platforms ?? []).Select(platform => new PlatformDefinition(platform.PlatformId, platform.StationId, platform.Name,
                platform.Direction, platform.EffectiveLengthMeters, platform.StoppingPositionMeters, platform.AllowsPassengerService,
                platform.AllowedVehicleTypeIds, platform.AllowedServiceTypeIds, platform.TrackSegmentIds)), yard.TrackSegmentIds,
            yard.RoutePathIds, (yard.TurnbackPlans ?? []).Select(ToRuntime), yard.PlatformAllocationStrategy)),
        item.TrackSegments.Select(track => new TrackSegmentDefinition(track.TrackId, track.FromStationId, track.ToStationId,
            track.StartPositionMeters, track.EndPositionMeters, track.Direction, track.Kind, track.EffectiveLengthMeters,
            track.SpeedLimitMetersPerSecond, track.ConflictResourceIds)),
        item.Paths.Select(path => new RoutePathDefinition(path.PathId, path.FromPlatformId, path.ToPlatformId, path.Direction,
            path.TrackSegmentIds, path.ResourceIds)), item.TurnbackPlans?.Select(ToRuntime),
        (item.SpatialReferencePoints ?? []).Select(ToRuntime),
        (item.StationOvertakeFacilities ?? []).Select(item => new StationOvertakeFacilityDefinition(
            item.FacilityId,
            item.StationId,
            item.Direction,
            item.MainlineTrackSegmentId,
            item.LocalPlatformId,
            item.ExpressPlatformId,
            item.LocalTrackSegmentId,
            item.ExpressTrackSegmentId,
            item.EntryPositionMeters,
            item.ResourceIds)),
        (item.SpatialReferencePointTemplates ?? []).Select(ToRuntime));

    private static TurnbackPlanDefinition ToRuntime(ProjectTurnbackPlan item) => new(item.TurnbackId, item.Name, item.StationId, item.Kind,
        item.ArrivalPlatformId, item.DeparturePlatformId, item.TrackSegmentIds, item.ResourceIds, item.TurnbackTimeSeconds);

    private static SpatialReferencePointDefinition ToRuntime(ProjectSpatialReferencePoint item) => new(
        item.ReferencePointId, item.StationId, item.Name, item.Kind, item.AlternateBerthing,
        item.MainlineGradePermille, item.BranchlineGradePermille,
        item.DistanceFromStopToCrossoverMeters, item.CrossoverLengthMeters,
        item.DistanceFromCrossoverToTurnbackStopMeters, item.TurnbackDwellSeconds,
        item.SwitchSpeedLimitMetersPerSecond, item.MainlineApproachCruiseSpeedMetersPerSecond,
        item.BranchlineApproachCruiseSpeedMetersPerSecond, item.MainlineSafetyFactor,
        item.BranchlineSafetyFactor, item.MainlineTrafficRatio,
        item.StationForwardGradeInPermille, item.StationForwardGradeOutPermille,
        item.StationForwardDistanceToSignalMeters, item.StationForwardOverlapMeters,
        item.StationForwardDwellSeconds, item.StationForwardEarlierCruiseSpeedMetersPerSecond,
        item.StationForwardLaterCruiseSpeedMetersPerSecond, item.StationForwardSafetyFactor,
        item.StationReverseGradeInPermille, item.StationReverseGradeOutPermille,
        item.StationReverseDistanceToSignalMeters, item.StationReverseOverlapMeters,
        item.StationReverseDwellSeconds, item.StationReverseEarlierCruiseSpeedMetersPerSecond,
        item.StationReverseLaterCruiseSpeedMetersPerSecond, item.StationReverseSafetyFactor);

    private static SpatialReferencePointTemplateDefinition ToRuntime(ProjectSpatialReferencePointTemplate item) => new(
        item.Kind, item.AlternateBerthing, item.MainlineGradePermille, item.BranchlineGradePermille,
        item.DistanceFromStopToCrossoverMeters, item.CrossoverLengthMeters,
        item.DistanceFromCrossoverToTurnbackStopMeters, item.TurnbackDwellSeconds,
        item.SwitchSpeedLimitMetersPerSecond, item.MainlineApproachCruiseSpeedMetersPerSecond,
        item.BranchlineApproachCruiseSpeedMetersPerSecond, item.MainlineSafetyFactor,
        item.BranchlineSafetyFactor, item.MainlineTrafficRatio,
        item.StationForwardGradeInPermille, item.StationForwardGradeOutPermille,
        item.StationForwardDistanceToSignalMeters, item.StationForwardOverlapMeters,
        item.StationForwardDwellSeconds, item.StationForwardEarlierCruiseSpeedMetersPerSecond,
        item.StationForwardLaterCruiseSpeedMetersPerSecond, item.StationForwardSafetyFactor,
        item.StationReverseGradeInPermille, item.StationReverseGradeOutPermille,
        item.StationReverseDistanceToSignalMeters, item.StationReverseOverlapMeters,
        item.StationReverseDwellSeconds, item.StationReverseEarlierCruiseSpeedMetersPerSecond,
        item.StationReverseLaterCruiseSpeedMetersPerSecond, item.StationReverseSafetyFactor);

    private static ProjectInfrastructure FromRuntime(InfrastructureGraph graph) => new(
        graph.StationYards.Select(yard => new ProjectStationYard(yard.StationId, yard.Name,
            yard.Platforms.Select(platform => new ProjectPlatform(platform.PlatformId, platform.StationId, platform.Name, platform.Direction,
                platform.EffectiveLengthMeters, platform.StoppingPositionMeters, platform.AllowsPassengerService,
                platform.AllowedVehicleTypeIds.ToArray(), platform.AllowedServiceTypeIds.ToArray(), platform.TrackSegmentIds.ToArray())).ToArray(),
            yard.TrackSegmentIds.ToArray(), yard.RoutePathIds.ToArray(), yard.TurnbackPlans.Select(FromRuntime).ToArray(), yard.PlatformAllocationStrategy)).ToArray(),
        graph.TrackSegments.Select(track => new ProjectTrackSegment(track.TrackId, track.FromStationId, track.ToStationId, track.StartPositionMeters,
            track.EndPositionMeters, track.Direction, track.Kind, track.EffectiveLengthMeters, track.SpeedLimitMetersPerSecond,
            track.ConflictResourceIds.ToArray())).ToArray(),
        graph.Paths.Select(path => new ProjectRoutePath(path.PathId, path.FromPlatformId, path.ToPlatformId, path.Direction,
            path.TrackSegmentIds.ToArray(), path.ResourceIds.ToArray())).ToArray(), graph.TurnbackPlans.Select(FromRuntime).ToArray(),
         graph.SpatialReferencePoints.Select(FromRuntime).ToArray(),
         graph.StationOvertakeFacilities.Select(facility => new ProjectStationOvertakeFacility(
            facility.FacilityId,
            facility.StationId,
            facility.Direction,
            facility.MainlineTrackSegmentId,
            facility.LocalPlatformId,
            facility.ExpressPlatformId,
            facility.LocalTrackSegmentId,
            facility.ExpressTrackSegmentId,
             facility.EntryPositionMeters,
             facility.ResourceIds.ToArray())).ToArray(),
         graph.SpatialReferencePointTemplates.Select(FromRuntime).ToArray());

    private static ProjectTurnbackPlan FromRuntime(TurnbackPlanDefinition item) => new(item.TurnbackId, item.Name, item.StationId, item.Kind,
        item.ArrivalPlatformId, item.DeparturePlatformId, item.TrackSegmentIds.ToArray(), item.ResourceIds.ToArray(), item.TurnbackTimeSeconds);

    private static ProjectSpatialReferencePoint FromRuntime(SpatialReferencePointDefinition item) => new(
        item.ReferencePointId, item.StationId, item.Name, item.Kind, item.AlternateBerthing,
        item.MainlineGradePermille, item.BranchlineGradePermille,
        item.DistanceFromStopToCrossoverMeters, item.CrossoverLengthMeters,
        item.DistanceFromCrossoverToTurnbackStopMeters, item.TurnbackDwellSeconds,
        item.SwitchSpeedLimitMetersPerSecond, item.MainlineApproachCruiseSpeedMetersPerSecond,
        item.BranchlineApproachCruiseSpeedMetersPerSecond, item.MainlineSafetyFactor,
        item.BranchlineSafetyFactor, item.MainlineTrafficRatio,
        item.StationForwardGradeInPermille, item.StationForwardGradeOutPermille,
        item.StationForwardDistanceToSignalMeters, item.StationForwardOverlapMeters,
        item.StationForwardDwellSeconds, item.StationForwardEarlierCruiseSpeedMetersPerSecond,
        item.StationForwardLaterCruiseSpeedMetersPerSecond, item.StationForwardSafetyFactor,
        item.StationReverseGradeInPermille, item.StationReverseGradeOutPermille,
        item.StationReverseDistanceToSignalMeters, item.StationReverseOverlapMeters,
        item.StationReverseDwellSeconds, item.StationReverseEarlierCruiseSpeedMetersPerSecond,
        item.StationReverseLaterCruiseSpeedMetersPerSecond, item.StationReverseSafetyFactor);

    private static ProjectSpatialReferencePointTemplate FromRuntime(SpatialReferencePointTemplateDefinition item) => new(
        item.Kind, item.AlternateBerthing, item.MainlineGradePermille, item.BranchlineGradePermille,
        item.DistanceFromStopToCrossoverMeters, item.CrossoverLengthMeters,
        item.DistanceFromCrossoverToTurnbackStopMeters, item.TurnbackDwellSeconds,
        item.SwitchSpeedLimitMetersPerSecond, item.MainlineApproachCruiseSpeedMetersPerSecond,
        item.BranchlineApproachCruiseSpeedMetersPerSecond, item.MainlineSafetyFactor,
        item.BranchlineSafetyFactor, item.MainlineTrafficRatio,
        item.StationForwardGradeInPermille, item.StationForwardGradeOutPermille,
        item.StationForwardDistanceToSignalMeters, item.StationForwardOverlapMeters,
        item.StationForwardDwellSeconds, item.StationForwardEarlierCruiseSpeedMetersPerSecond,
        item.StationForwardLaterCruiseSpeedMetersPerSecond, item.StationForwardSafetyFactor,
        item.StationReverseGradeInPermille, item.StationReverseGradeOutPermille,
        item.StationReverseDistanceToSignalMeters, item.StationReverseOverlapMeters,
        item.StationReverseDwellSeconds, item.StationReverseEarlierCruiseSpeedMetersPerSecond,
        item.StationReverseLaterCruiseSpeedMetersPerSecond, item.StationReverseSafetyFactor);
}
