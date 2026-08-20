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
    BrakingEstimationMode BrakingEstimationMode);

public sealed record ProjectStationServiceInstruction(
    string StationId,
    StationServiceMode Mode,
    double? SpeedLimitMetersPerSecond);

public sealed record ProjectServicePattern(
    string PatternId,
    string PatternName,
    ProjectStationServiceInstruction[] Instructions);

public sealed record ProjectServiceRunPlan(
    string VehicleId,
    int ServiceNumber,
    TrainDirection Direction,
    string ServiceClassId,
    string PatternId);

public sealed record SimulationProjectDocument(
    int SchemaVersion,
    string RouteId,
    string RouteName,
    ProjectStation[] Stations,
    ProjectTrainSettings Train,
    ProjectOperationalSettings Operations,
    ProjectSpeedLimit[] SpeedLimits,
    ProjectRunSettings Simulation,
    ProjectServicePattern[]? ServicePatterns = null,
    ProjectServiceRunPlan[]? ServiceRuns = null);

public static class SimulationProjectFormat
{
    public const int CurrentSchemaVersion = 2;

    public const int MaximumJsonCharacters = 2_000_000;

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
            var document = JsonSerializer.Deserialize<SimulationProjectDocument>(json, SerializerOptions)
                ?? throw new SimulationValidationException(["無法讀取存檔內容。"]);
            if (document.SchemaVersion == 1)
            {
                document = document with
                {
                    SchemaVersion = CurrentSchemaVersion,
                    ServicePatterns = [],
                    ServiceRuns = []
                };
            }

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

        if (document.ServicePatterns is null || document.ServicePatterns.Length > 500)
        {
            errors.Add("存檔服務模式數不得超過 500 組。");
        }

        if (document.ServiceRuns is null || document.ServiceRuns.Length > 10_000)
        {
            errors.Add("存檔車次服務計畫不得超過 10000 筆。");
        }

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
        var servicePatterns = document.ServicePatterns!;
        var serviceRuns = document.ServiceRuns!;
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

        if (servicePatterns.Any(pattern => pattern is null
            || pattern.PatternId?.Length > 100
            || pattern.PatternName?.Length > 200
            || pattern.Instructions is null
            || pattern.Instructions.Length > 500))
        {
            errors.Add("服務模式資料缺漏、文字過長或車站指令超過 500 筆。");
        }

        if (servicePatterns
            .Where(pattern => pattern?.Instructions is not null)
            .SelectMany(pattern => pattern!.Instructions)
            .Any(instruction => instruction is null
                || instruction.StationId?.Length > 100
                || !Enum.IsDefined(instruction.Mode)
                || instruction.SpeedLimitMetersPerSecond is { } speedLimit
                    && (!RouteValidator.IsFinite(speedLimit) || speedLimit <= 0)))
        {
            errors.Add("服務模式的車站指令缺漏、無效或通過速限不是有限正數。");
        }

        if (serviceRuns.Any(plan => plan is null
            || plan.VehicleId?.Length > 100
            || plan.ServiceClassId?.Length > 100
            || plan.PatternId?.Length > 100))
        {
            errors.Add("車次服務計畫資料缺漏或文字過長。");
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
            || !Enum.IsDefined(run.BrakingEstimationMode))
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
                servicePatterns.Select(pattern => new ServicePattern(
                    pattern.PatternId,
                    pattern.PatternName,
                    pattern.Instructions.Select(instruction => new StationServiceInstruction(
                        instruction.StationId,
                        instruction.Mode,
                        instruction.SpeedLimitMetersPerSecond)).ToArray())),
                serviceRuns.Select(plan => new ServiceRunPlan(
                    plan.VehicleId,
                    plan.ServiceNumber,
                    plan.Direction,
                    plan.ServiceClassId,
                    plan.PatternId)));
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

    private static SimulationProjectDocument Normalize(SimulationProjectDocument document) => document with
    {
        ServicePatterns = document.ServicePatterns ?? [],
        ServiceRuns = document.ServiceRuns ?? []
    };
}
