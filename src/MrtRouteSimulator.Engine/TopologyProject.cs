using System.Text.Json;
using System.Text.Json.Serialization;

namespace MrtRouteSimulator.Engine;

/// <summary>Schema 8 中方向別 dispatch 所使用的實體 ServiceRoute。</summary>
public sealed record TopologyDirectionRouteBinding(TrainDirection Direction, string ServiceRouteId);

/// <summary>
/// Schema 8 的唯一 topology-first 專案格式。它不保存 legacy Route、Station position、
/// ProjectInfrastructure 或 chainage speed limit；所有實體基礎設施都由 Topology 持有。
/// </summary>
public sealed record TopologyProjectDocument(
    int SchemaVersion,
    string ProjectId,
    string ProjectName,
    ProjectTrainSettings Train,
    ProjectOperationalSettings Operations,
    ProjectRunSettings Simulation,
    ProjectVehicleType[] VehicleTypes,
    ProjectServiceType[] ServiceTypes,
    ProjectStopPattern[] StopPatterns,
    ProjectDispatchPlan Dispatch,
    TopologyInfrastructureDefinition Topology,
    ServiceRouteDefinition[] ServiceRoutes,
    TopologyDirectionRouteBinding[] DirectionRouteBindings);

/// <summary>已驗證 Schema 8 專案轉成 V2 topology-native runtime 的唯一輸入組合。</summary>
public sealed record TopologyProjectRuntime(
    TopologySimulationDefinition Topology,
    TrainParameters TrainParameters,
    OperationalParameters OperationalParameters,
    ResolvedDispatchPlan DispatchPlan,
    IReadOnlyList<VehicleTypeDefinition> VehicleTypes,
    IReadOnlyList<ServiceTypeDefinition> ServiceTypes,
    IReadOnlyList<ServicePattern> ServicePatterns);

/// <summary>Schema 8 序列化、反序列化與跨參照驗證入口。</summary>
public static class TopologyProjectFormat
{
    public const int CurrentSchemaVersion = 8;
    public const int MaximumJsonCharacters = 2_000_000;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly string[] DisallowedLegacyPropertyNames =
    [
        "routeId", "routeName", "stations", "speedLimits", "infrastructure",
        "servicePatterns", "serviceRuns"
    ];

    public static string Serialize(TopologyProjectDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static TopologyProjectDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new SimulationValidationException(["存檔內容是空白的。"]);
        if (json.Length > MaximumJsonCharacters)
            throw new SimulationValidationException([$"存檔內容超過 {MaximumJsonCharacters / 1_000_000} MB 上限。"]);

        try
        {
            RejectLegacyFields(json);
            var document = JsonSerializer.Deserialize<TopologyProjectDocument>(json, SerializerOptions)
                ?? throw new SimulationValidationException(["無法讀取 Schema 8 topology 存檔。"]);
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

    public static void Validate(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        if (document.SchemaVersion != CurrentSchemaVersion)
            errors.Add($"不支援存檔版本 {document.SchemaVersion}；Schema 8 topology 格式僅支援版本 {CurrentSchemaVersion}。");
        RequireText(document.ProjectId, "專案編號", errors);
        RequireText(document.ProjectName, "專案名稱", errors);
        if (document.ProjectId?.Length > 100 || document.ProjectName?.Length > 200)
            errors.Add("專案編號或名稱過長。");
        if (document.Topology is null) errors.Add("Schema 8 存檔缺少 topology 基礎設施。");
        if (document.ServiceRoutes is null || document.ServiceRoutes.Length == 0)
            errors.Add("Schema 8 存檔至少需要一條 ServiceRoute。");
        if (document.DirectionRouteBindings is null || document.DirectionRouteBindings.Length == 0)
            errors.Add("Schema 8 存檔至少需要一個方向與 ServiceRoute 綁定。");
        if (document.Train is null) errors.Add("存檔缺少列車性能設定。");
        if (document.Operations is null) errors.Add("存檔缺少 V2 營運設定。");
        if (document.Simulation is null) errors.Add("存檔缺少模擬設定。");
        if (document.VehicleTypes is null || document.VehicleTypes.Length == 0)
            errors.Add("存檔至少需要一組車型目錄。");
        if (document.ServiceTypes is null || document.ServiceTypes.Length == 0)
            errors.Add("存檔至少需要一組服務類型目錄。");
        if (document.StopPatterns is null || document.StopPatterns.Length == 0)
            errors.Add("存檔至少需要一組停站模式目錄。");
        if (document.Dispatch is null) errors.Add("存檔缺少發車計畫。");
        RouteValidator.ThrowIfAny(errors);

        if (document.Topology is not null && document.ServiceRoutes is not null)
        {
            try
            {
                InfrastructureValidator.ValidateAndThrow(document.Topology, document.ServiceRoutes);
            }
            catch (SimulationValidationException exception)
            {
                errors.AddRange(exception.Errors);
            }
        }

        if (document.DirectionRouteBindings is not null && document.ServiceRoutes is not null)
            ValidateDirectionBindings(document.DirectionRouteBindings, document.ServiceRoutes, errors);
        if (document.Topology is not null && document.ServiceRoutes is not null)
            ValidateTurnbackOperationRoutes(document.Topology, document.ServiceRoutes, errors);
        if (document.Topology is not null && document.VehicleTypes is not null && document.ServiceTypes is not null
            && document.StopPatterns is not null && document.Dispatch is not null)
            ValidatePlanningCatalog(document, errors);
        RouteValidator.ThrowIfAny(errors);
    }

    /// <summary>
    /// 將 Schema 8 的 catalog、dispatch、ServiceRoute 與 physical topology 一次解析為 V2
    /// runtime 輸入。此路徑不建立 Route、InfrastructureGraph 或 virtual-track adapter。
    /// </summary>
    public static TopologyProjectRuntime CreateRuntime(TopologyProjectDocument document)
    {
        Validate(document);
        var infrastructure = new InfrastructureGraphV4(document.Topology);
        var routes = document.ServiceRoutes.ToDictionary(route => route.ServiceRouteId, StringComparer.OrdinalIgnoreCase);
        var outboundId = document.DirectionRouteBindings
            .Single(binding => binding.Direction == TrainDirection.Outbound)
            .ServiceRouteId;
        var inboundId = document.DirectionRouteBindings
            .Single(binding => binding.Direction == TrainDirection.Inbound)
            .ServiceRouteId;
        var topology = new TopologySimulationDefinition(infrastructure, routes[outboundId], routes[inboundId]);
        var vehicleTypes = document.VehicleTypes.Select(ToRuntime).ToArray();
        var serviceTypes = document.ServiceTypes.Select(item => new ServiceTypeDefinition(
            item.Id,
            item.DisplayName,
            item.ColorHex,
            item.RunPrefix,
            item.DefaultStopPatternId,
            item.DefaultVehicleTypeId,
            item.Priority,
            item.CanRequestOvertake,
            item.PreferredPlatformIds)).ToArray();
        var stopPatterns = document.StopPatterns.Select(ToRuntime).ToArray();
        var dispatch = DispatchPlanExpander.Expand(
            ToRuntime(document.Dispatch),
            vehicleTypes,
            serviceTypes,
            stopPatterns);
        StationConstructionRules.ValidateBerthing(document, dispatch);
        var servicePatterns = stopPatterns.Select(pattern => new ServicePattern(
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
                instruction.DwellTimeSeconds)).ToArray())).ToArray();
        var train = new TrainParameters(
            document.Train.MaxSpeedMetersPerSecond,
            document.Train.AccelerationMetersPerSecondSquared,
            document.Train.DecelerationMetersPerSecondSquared,
            document.Train.DefaultDwellTimeSeconds,
            document.Train.OriginTurnaroundTimeSeconds,
            document.Train.TerminalTurnaroundTimeSeconds);
        var operations = new OperationalParameters(
            document.Operations.JerkMetersPerSecondCubed,
            document.Operations.CoastingRatio,
            document.Operations.ApproachDistanceMeters,
            document.Operations.ApproachSpeedMetersPerSecond,
            document.Operations.TractionFadeRatio,
            document.Operations.TrainLengthMeters,
            document.Operations.ServiceBrakingMetersPerSecondSquared,
            document.Operations.EmergencyBrakingMetersPerSecondSquared,
            document.Operations.ControlReactionTimeSeconds,
            document.Operations.BrakeBuildUpTimeSeconds,
            document.Operations.PositioningErrorMeters,
            document.Operations.SafetyMarginMeters,
            document.Operations.AbsoluteMinimumGapMeters);
        return new TopologyProjectRuntime(
            topology,
            train,
            operations,
            dispatch,
            vehicleTypes,
            serviceTypes,
            servicePatterns);
    }

    private static void ValidateDirectionBindings(
        IEnumerable<TopologyDirectionRouteBinding> bindings,
        IEnumerable<ServiceRouteDefinition> serviceRoutes,
        ICollection<string> errors)
    {
        var knownRoutes = serviceRoutes.Select(route => route.ServiceRouteId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directions = new HashSet<TrainDirection>();
        foreach (var binding in bindings)
        {
            if (!Enum.IsDefined(binding.Direction))
                errors.Add("Schema 8 ServiceRoute direction 綁定包含無效方向。");
            else if (!directions.Add(binding.Direction))
                errors.Add($"Schema 8 重複綁定方向「{binding.Direction}」。");
            if (string.IsNullOrWhiteSpace(binding.ServiceRouteId) || !knownRoutes.Contains(binding.ServiceRouteId))
                errors.Add($"Schema 8 方向「{binding.Direction}」引用不存在的 ServiceRoute「{binding.ServiceRouteId}」。");
        }

        foreach (var direction in new[] { TrainDirection.Outbound, TrainDirection.Inbound })
            if (!directions.Contains(direction)) errors.Add($"Schema 8 缺少「{direction}」方向的 ServiceRoute 綁定。");
    }

    private static void ValidatePlanningCatalog(TopologyProjectDocument document, ICollection<string> errors)
    {
        var stationIds = document.Topology.Stations.Select(item => item.StationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var platformIds = document.Topology.Platforms.Select(item => item.PlatformId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vehicleIds = Index(document.VehicleTypes, item => item.Id, "車型", errors);
        var serviceIds = Index(document.ServiceTypes, item => item.Id, "服務類型", errors);
        var stopPatternIds = Index(document.StopPatterns, item => item.Id, "停站模式", errors);

        foreach (var vehicle in document.VehicleTypes)
        {
            if (vehicle.DefaultStopPatternId is { } stopPatternId && !stopPatternIds.Contains(stopPatternId))
                errors.Add($"車型「{vehicle.Id}」引用不存在的停站模式「{stopPatternId}」。");
        }

        foreach (var service in document.ServiceTypes)
        {
            if (service.DefaultVehicleTypeId is { } vehicleId && !vehicleIds.Contains(vehicleId))
                errors.Add($"服務類型「{service.Id}」引用不存在的車型「{vehicleId}」。");
            if (service.DefaultStopPatternId is { } stopPatternId && !stopPatternIds.Contains(stopPatternId))
                errors.Add($"服務類型「{service.Id}」引用不存在的停站模式「{stopPatternId}」。");
            foreach (var platformId in service.PreferredPlatformIds ?? [])
                if (!platformIds.Contains(platformId)) errors.Add($"服務類型「{service.Id}」引用不存在的偏好月台「{platformId}」。");
        }

        foreach (var pattern in document.StopPatterns)
        foreach (var instruction in pattern.Instructions ?? [])
        {
            if (!stationIds.Contains(instruction.StationId))
                errors.Add($"停站模式「{pattern.Id}」引用不存在的 topology 車站「{instruction.StationId}」。");
            if (!Enum.IsDefined(instruction.Action))
                errors.Add($"停站模式「{pattern.Id}」包含無效動作。");
        }

        foreach (var plan in document.Dispatch.SimpleHeadwayPlans ?? [])
        {
            ValidateDispatchReferences(plan.ServiceTypeId, plan.VehicleTypeId, plan.StopPatternId, plan.OriginPlatformId,
                serviceIds, vehicleIds, stopPatternIds, platformIds, "等間距派車", errors);
        }
        foreach (var row in document.Dispatch.ManualTimetableRows ?? [])
        {
            ValidateDispatchReferences(row.ServiceTypeId, row.VehicleTypeId, row.StopPatternId, row.OriginPlatformId,
                serviceIds, vehicleIds, stopPatternIds, platformIds, "手動班表", errors);
        }
    }

    private static void ValidateTurnbackOperationRoutes(
        TopologyInfrastructureDefinition topology,
        IEnumerable<ServiceRouteDefinition> serviceRoutes,
        ICollection<string> errors)
    {
        var routeIds = serviceRoutes.Select(route => route.ServiceRouteId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var facility in topology.TurnbackFacilities)
        {
            if (facility.Traversals.Count == 0)
            {
                errors.Add($"Schema 8 折返設施「{facility.FacilityId}」必須提供有序 Traversals；不得以 FacilityTrackEdgeIds 或 virtual track 執行。 ");
            }
            else if (facility.TurnbackStopPosition is null && facility.TurnbackStopAfterTraversalIndex is null)
            {
                errors.Add($"Schema 8 折返設施「{facility.FacilityId}」必須明確指定 TurnbackStopPosition 或 TurnbackStopAfterTraversalIndex，避免 runtime 猜測折返位置。 ");
            }
        }
        foreach (var operation in topology.TurnbackOperations)
        {
            if (!routeIds.Contains(operation.ArrivalServiceRouteId))
                errors.Add($"折返作業「{operation.OperationId}」引用不存在的到達營運路線「{operation.ArrivalServiceRouteId}」。");
            if (!routeIds.Contains(operation.DepartureServiceRouteId))
                errors.Add($"折返作業「{operation.OperationId}」引用不存在的出發營運路線「{operation.DepartureServiceRouteId}」。");
        }
        foreach (var facility in topology.PassingFacilities)
        {
            if (facility.Traversals.Count == 0)
            {
                errors.Add($"Schema 8 越行設施「{facility.FacilityId}」必須提供有序 Traversals；不得以 virtual track 執行。 ");
            }
        }
        foreach (var operation in topology.PassingOperations)
        {
            if (!routeIds.Contains(operation.ServiceRouteId))
                errors.Add($"越行作業「{operation.OperationId}」引用不存在的營運路線「{operation.ServiceRouteId}」。");
        }
    }

    private static void ValidateDispatchReferences(
        string serviceTypeId,
        string? vehicleTypeId,
        string? stopPatternId,
        string? originPlatformId,
        ISet<string> serviceIds,
        ISet<string> vehicleIds,
        ISet<string> stopPatternIds,
        ISet<string> platformIds,
        string owner,
        ICollection<string> errors)
    {
        if (!serviceIds.Contains(serviceTypeId)) errors.Add($"{owner}引用不存在的服務類型「{serviceTypeId}」。");
        if (vehicleTypeId is { } vehicleId && !vehicleIds.Contains(vehicleId)) errors.Add($"{owner}引用不存在的車型「{vehicleId}」。");
        if (stopPatternId is { } stopPattern && !stopPatternIds.Contains(stopPattern)) errors.Add($"{owner}引用不存在的停站模式「{stopPattern}」。");
        if (originPlatformId is { } platformId && !platformIds.Contains(platformId)) errors.Add($"{owner}引用不存在的起始月台「{platformId}」。");
    }

    private static HashSet<string> Index<T>(IEnumerable<T> values, Func<T, string> idSelector, string type, ICollection<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var id = idSelector(value);
            if (string.IsNullOrWhiteSpace(id)) errors.Add($"{type}編號不可空白。");
            else if (!ids.Add(id)) errors.Add($"{type}編號「{id}」重複。");
        }
        return ids;
    }

    private static void RejectLegacyFields(string json)
    {
        using var jsonDocument = JsonDocument.Parse(json);
        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object) return;
        var names = jsonDocument.RootElement.EnumerateObject()
            .Where(property => DisallowedLegacyPropertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length > 0)
            throw new SimulationValidationException([$"Schema 8 不支援 legacy Route／Infrastructure 欄位：{string.Join("、", names)}。請使用 topology 與 serviceRoutes。"]);
    }

    private static void RequireText(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field}不可空白。");
    }

    private static VehicleTypeDefinition ToRuntime(ProjectVehicleType item) => new(
        item.Id,
        item.DisplayName,
        item.LengthMeters,
        item.MaxSpeedMetersPerSecond,
        item.AccelerationMetersPerSecondSquared,
        item.ServiceBrakeDecelerationMetersPerSecondSquared,
        item.EmergencyBrakeDecelerationMetersPerSecondSquared,
        item.JerkMetersPerSecondCubed,
        item.TractionDecayPerSecond,
        item.CoastingDecelerationMetersPerSecondSquared,
        item.DefaultStopPatternId);

    private static StopPatternDefinition ToRuntime(ProjectStopPattern item) => new(
        item.Id,
        item.DisplayName,
        item.Instructions.Select(instruction => new StopPatternInstruction(
            instruction.StationId,
            instruction.Action,
            instruction.DwellTimeSeconds,
            instruction.PassingSpeedLimitMetersPerSecond)));

    private static DispatchPlanDefinition ToRuntime(ProjectDispatchPlan item) => new(
        item.SimpleHeadwayPlans?.Select(plan => new HeadwayDirectionPlan(
            plan.Direction,
            TimeSpan.FromSeconds(plan.FirstDepartureTimeSeconds),
            TimeSpan.FromSeconds(plan.HeadwaySeconds),
            plan.RunCount,
            plan.ServiceTypeId,
            plan.VehicleTypeId,
            plan.StopPatternId,
            plan.OriginPlatformId,
            plan.VehicleId,
            plan.ContinueAfterTerminal)),
        item.ManualTimetableRows?.Select(row => new ManualTimetableRow(
            TimeSpan.FromSeconds(row.PlannedDepartureTimeSeconds),
            row.Direction,
            row.ServiceTypeId,
            row.VehicleTypeId,
            row.StopPatternId,
            row.OriginPlatformId,
            row.VehicleId,
            row.ServiceRunId,
            row.ContinueAfterTerminal,
            row.ContinuationServiceRunId)),
        item.ActiveMode,
        item.VehicleAssignmentMode);

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new ReadOnlyStringSetConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ReadOnlyStringSetConverter : JsonConverter<IReadOnlySet<string>>
    {
        public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<string[]>(ref reader, options)
                ?? throw new JsonException("唯讀字串集合不可為 null。");
            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }

        public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(), options);
    }
}
