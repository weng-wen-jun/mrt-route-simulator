using System.Collections.ObjectModel;
using static MrtRouteSimulator.Engine.PlanningModelValidation;

namespace MrtRouteSimulator.Engine;

/// <summary>車型目錄中的穩定定義。所有數值均使用 SI 單位。</summary>
public sealed class VehicleTypeDefinition
{
    public VehicleTypeDefinition(
        string id,
        string displayName,
        double lengthMeters,
        double maxSpeedMetersPerSecond,
        double accelerationMetersPerSecondSquared,
        double serviceBrakeDecelerationMetersPerSecondSquared,
        double emergencyBrakeDecelerationMetersPerSecondSquared,
        double jerkMetersPerSecondCubed,
        double tractionDecayPerSecond,
        double coastingDecelerationMetersPerSecondSquared,
        string? defaultStopPatternId = null)
    {
        var errors = new List<string>();
        Id = NormalizeRequired(id, "車型 ID", errors);
        DisplayName = NormalizeRequired(displayName, "車型名稱", errors);
        RequirePositiveFinite(lengthMeters, "車長", errors);
        RequirePositiveFinite(maxSpeedMetersPerSecond, "最高速度", errors);
        RequirePositiveFinite(accelerationMetersPerSecondSquared, "加速度", errors);
        RequirePositiveFinite(serviceBrakeDecelerationMetersPerSecondSquared, "一般煞車減速度", errors);
        RequirePositiveFinite(emergencyBrakeDecelerationMetersPerSecondSquared, "緊急煞車減速度", errors);
        RequirePositiveFinite(jerkMetersPerSecondCubed, "Jerk", errors);
        RequireNonNegativeFinite(tractionDecayPerSecond, "牽引衰減", errors);
        RequireNonNegativeFinite(coastingDecelerationMetersPerSecondSquared, "惰行減速度", errors);

        if (emergencyBrakeDecelerationMetersPerSecondSquared < serviceBrakeDecelerationMetersPerSecondSquared)
        {
            errors.Add("緊急煞車減速度不可小於一般煞車減速度。");
        }

        LengthMeters = lengthMeters;
        MaxSpeedMetersPerSecond = maxSpeedMetersPerSecond;
        AccelerationMetersPerSecondSquared = accelerationMetersPerSecondSquared;
        ServiceBrakeDecelerationMetersPerSecondSquared = serviceBrakeDecelerationMetersPerSecondSquared;
        EmergencyBrakeDecelerationMetersPerSecondSquared = emergencyBrakeDecelerationMetersPerSecondSquared;
        JerkMetersPerSecondCubed = jerkMetersPerSecondCubed;
        TractionDecayPerSecond = tractionDecayPerSecond;
        CoastingDecelerationMetersPerSecondSquared = coastingDecelerationMetersPerSecondSquared;
        DefaultStopPatternId = NormalizeOptional(defaultStopPatternId, "車型預設停站模式 ID", errors);
        RouteValidator.ThrowIfAny(errors);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public double LengthMeters { get; }
    public double MaxSpeedMetersPerSecond { get; }
    public double AccelerationMetersPerSecondSquared { get; }
    public double ServiceBrakeDecelerationMetersPerSecondSquared { get; }
    public double EmergencyBrakeDecelerationMetersPerSecondSquared { get; }
    public double JerkMetersPerSecondCubed { get; }
    public double TractionDecayPerSecond { get; }
    public double CoastingDecelerationMetersPerSecondSquared { get; }
    public string? DefaultStopPatternId { get; }

    // 常用中文領域名稱的相容別名。
    public double DecelerationMetersPerSecondSquared => ServiceBrakeDecelerationMetersPerSecondSquared;
    public double GeneralBrakeDecelerationMetersPerSecondSquared => ServiceBrakeDecelerationMetersPerSecondSquared;
    public double OperationalBrakeDecelerationMetersPerSecondSquared => ServiceBrakeDecelerationMetersPerSecondSquared;
}

public sealed class ServiceTypeDefinition
{
    public ServiceTypeDefinition(
        string id,
        string displayName,
        string colorHex,
        string runPrefix,
        string? defaultStopPatternId = null,
        string? defaultVehicleTypeId = null,
        int priority = 0,
        bool canRequestOvertake = false,
        IEnumerable<string>? preferredPlatformIds = null)
    {
        var errors = new List<string>();
        Id = NormalizeRequired(id, "服務類型 ID", errors);
        DisplayName = NormalizeRequired(displayName, "服務類型名稱", errors);
        ColorHex = NormalizeRequired(colorHex, "服務類型顏色", errors);
        RunPrefix = NormalizeRequired(runPrefix, "服務類型車次前綴", errors);
        DefaultStopPatternId = NormalizeOptional(defaultStopPatternId, "預設停站模式 ID", errors);
        DefaultVehicleTypeId = NormalizeOptional(defaultVehicleTypeId, "預設車型 ID", errors);
        PreferredPlatformIds = ReadOnlyStrings(preferredPlatformIds, "偏好月台 ID", errors);
        if (priority < 0)
        {
            errors.Add("服務類型優先序不可為負數。");
        }

        if (!IsHexColor(ColorHex))
        {
            errors.Add("服務類型顏色必須是 #RRGGBB 或 #AARRGGBB 格式。");
        }

        Priority = priority;
        CanRequestOvertake = canRequestOvertake;
        RouteValidator.ThrowIfAny(errors);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string ColorHex { get; }
    public string RunPrefix { get; }
    public string? DefaultStopPatternId { get; }
    public string? DefaultVehicleTypeId { get; }
    public int Priority { get; }
    public bool CanRequestOvertake { get; }
    public IReadOnlyList<string> PreferredPlatformIds { get; }

    private static bool IsHexColor(string value) =>
        (value.Length == 7 || value.Length == 9)
        && value[0] == '#'
        && value.Skip(1).All(Uri.IsHexDigit);
}

public enum StopPatternAction
{
    Stop,
    Pass,
    Turnback
}

public sealed class StopPatternInstruction
{
    public StopPatternInstruction(
        string stationId,
        StopPatternAction action,
        double? dwellTimeSeconds = null,
        double? passingSpeedLimitMetersPerSecond = null)
    {
        var errors = new List<string>();
        StationId = NormalizeRequired(stationId, "停站模式車站 ID", errors);
        if (!Enum.IsDefined(action))
        {
            errors.Add("停站模式動作只能是 Stop、Pass 或 Turnback。");
        }

        if (dwellTimeSeconds is not null)
        {
            RequireNonNegativeFinite(dwellTimeSeconds.Value, "停站時間覆寫", errors);
        }

        if (passingSpeedLimitMetersPerSecond is not null)
        {
            RequirePositiveFinite(passingSpeedLimitMetersPerSecond.Value, "通過速限覆寫", errors);
        }

        Action = action;
        DwellTimeSeconds = dwellTimeSeconds;
        PassingSpeedLimitMetersPerSecond = passingSpeedLimitMetersPerSecond;
        RouteValidator.ThrowIfAny(errors);
    }

    public StopPatternInstruction(
        string stationId,
        bool stop,
        double? dwellTimeSeconds = null,
        double? passingSpeedLimitMetersPerSecond = null)
        : this(stationId, stop ? StopPatternAction.Stop : StopPatternAction.Pass, dwellTimeSeconds, passingSpeedLimitMetersPerSecond)
    {
    }

    public string StationId { get; }
    public StopPatternAction Action { get; }
    public bool IsStop => Action is StopPatternAction.Stop or StopPatternAction.Turnback;
    public bool Stop => IsStop;
    public double? DwellTimeSeconds { get; }
    public double? PassingSpeedLimitMetersPerSecond { get; }
    public double? PassThroughSpeedLimitMetersPerSecond => PassingSpeedLimitMetersPerSecond;
}

public sealed class StopPatternDefinition
{
    public StopPatternDefinition(string id, string displayName, IEnumerable<StopPatternInstruction> instructions)
    {
        var errors = new List<string>();
        Id = NormalizeRequired(id, "停站模式 ID", errors);
        DisplayName = NormalizeRequired(displayName, "停站模式名稱", errors);
        var values = instructions?.ToArray() ?? [];
        if (values.Length == 0)
        {
            errors.Add("停站模式至少需要一筆車站指令。");
        }

        var stationIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < values.Length; index++)
        {
            if (!stationIds.Add(values[index].StationId))
            {
                errors.Add($"停站模式車站 ID「{values[index].StationId}」重複。");
            }
        }

        Instructions = ReadOnly(values);
        RouteValidator.ThrowIfAny(errors);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<StopPatternInstruction> Instructions { get; }
}

public enum DispatchPlanningMode
{
    SimpleHeadway,
    ManualTimetable
}

public enum VehicleAssignmentMode
{
    Automatic,
    AutoAssignMissing = Automatic,
    ExplicitOnly,
    Specified = ExplicitOnly,
    SpecifiedOrAutomatic
}

public sealed class HeadwayDirectionPlan
{
    public HeadwayDirectionPlan(
        TrainDirection direction,
        TimeSpan firstDepartureTime,
        TimeSpan headway,
        int runCount,
        string serviceTypeId,
        string? vehicleTypeId = null,
        string? stopPatternId = null,
        string? originPlatformId = null,
        string? vehicleId = null,
        bool continueAfterTerminal = false)
    {
        var errors = new List<string>();
        ValidateDirection(direction, errors);
        ValidateClock(firstDepartureTime, "首班時間", errors);
        if (headway <= TimeSpan.Zero)
        {
            errors.Add("班距必須大於 0。");
        }
        if (runCount <= 0)
        {
            errors.Add("班次數必須大於 0。");
        }

        Direction = direction;
        FirstDepartureTime = firstDepartureTime;
        Headway = headway;
        RunCount = runCount;
        ServiceTypeId = NormalizeRequired(serviceTypeId, "班距計畫服務類型 ID", errors);
        VehicleTypeId = NormalizeOptional(vehicleTypeId, "班距計畫車型 ID", errors);
        StopPatternId = NormalizeOptional(stopPatternId, "班距計畫停站模式 ID", errors);
        OriginPlatformId = NormalizeOptional(originPlatformId, "班距計畫起點月台 ID", errors);
        VehicleId = NormalizeOptional(vehicleId, "班距計畫車輛 ID", errors);
        ContinueAfterTerminal = continueAfterTerminal;
        RouteValidator.ThrowIfAny(errors);
    }

    public HeadwayDirectionPlan(
        TimeSpan firstDepartureTime,
        TimeSpan headway,
        int runCount,
        TrainDirection direction,
        string serviceTypeId,
        string? vehicleTypeId = null,
        string? stopPatternId = null,
        string? originPlatformId = null,
        string? vehicleId = null,
        bool continueAfterTerminal = false)
        : this(direction, firstDepartureTime, headway, runCount, serviceTypeId, vehicleTypeId, stopPatternId, originPlatformId, vehicleId, continueAfterTerminal)
    {
    }

    public TrainDirection Direction { get; }
    public TimeSpan FirstDepartureTime { get; }
    public TimeSpan Headway { get; }
    public int RunCount { get; }
    public string ServiceTypeId { get; }
    public string? VehicleTypeId { get; }
    public string? StopPatternId { get; }
    public string? OriginPlatformId { get; }
    public string? VehicleId { get; }
    public bool ContinueAfterTerminal { get; }
}

public sealed class ManualTimetableRow
{
    public ManualTimetableRow(
        TimeSpan plannedDepartureTime,
        TrainDirection direction,
        string serviceTypeId,
        string? vehicleTypeId = null,
        string? stopPatternId = null,
        string? originPlatformId = null,
        string? vehicleId = null,
        string? serviceRunId = null,
        bool continueAfterTerminal = false,
        string? continuationServiceRunId = null)
    {
        var errors = new List<string>();
        ValidateDirection(direction, errors);
        ValidateClock(plannedDepartureTime, "手動班表發車時間", errors);
        PlannedDepartureTime = plannedDepartureTime;
        Direction = direction;
        ServiceTypeId = NormalizeRequired(serviceTypeId, "手動班表服務類型 ID", errors);
        VehicleTypeId = NormalizeOptional(vehicleTypeId, "手動班表車型 ID", errors);
        StopPatternId = NormalizeOptional(stopPatternId, "手動班表停站模式 ID", errors);
        OriginPlatformId = NormalizeOptional(originPlatformId, "手動班表起點月台 ID", errors);
        VehicleId = NormalizeOptional(vehicleId, "手動班表車輛 ID", errors);
        ServiceRunId = NormalizeOptional(serviceRunId, "手動班表車次 ID", errors);
        ContinuationServiceRunId = NormalizeOptional(continuationServiceRunId, "折返接續車次 ID", errors);
        ContinueAfterTerminal = continueAfterTerminal || ContinuationServiceRunId is not null;
        RouteValidator.ThrowIfAny(errors);
    }

    public ManualTimetableRow(
        TrainDirection direction,
        TimeSpan plannedDepartureTime,
        string serviceTypeId,
        string? vehicleTypeId = null,
        string? stopPatternId = null,
        string? originPlatformId = null,
        string? vehicleId = null,
        string? serviceRunId = null,
        bool continueAfterTerminal = false,
        string? continuationServiceRunId = null)
        : this(plannedDepartureTime, direction, serviceTypeId, vehicleTypeId, stopPatternId, originPlatformId, vehicleId, serviceRunId,
            continueAfterTerminal, continuationServiceRunId)
    {
    }

    public TimeSpan PlannedDepartureTime { get; }
    public TrainDirection Direction { get; }
    public string ServiceTypeId { get; }
    public string? VehicleTypeId { get; }
    public string? StopPatternId { get; }
    public string? OriginPlatformId { get; }
    public string? VehicleId { get; }
    public string? ServiceRunId { get; }
    public bool ContinueAfterTerminal { get; }
    public string? ContinuationServiceRunId { get; }
}

public sealed class DispatchPlanDefinition
{
    public DispatchPlanDefinition(
        IEnumerable<HeadwayDirectionPlan>? simpleHeadwayPlans,
        IEnumerable<ManualTimetableRow>? manualTimetableRows,
        DispatchPlanningMode activeMode,
        VehicleAssignmentMode vehicleAssignmentMode = VehicleAssignmentMode.Automatic)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(activeMode))
        {
            errors.Add("發車規劃模式無效。");
        }
        if (!Enum.IsDefined(vehicleAssignmentMode))
        {
            errors.Add("車輛配置模式無效。");
        }

        SimpleHeadwayPlans = ReadOnly(simpleHeadwayPlans?.ToArray() ?? []);
        ManualTimetableRows = ReadOnly(manualTimetableRows?.ToArray() ?? []);
        ActiveMode = activeMode;
        VehicleAssignmentMode = vehicleAssignmentMode;
        RouteValidator.ThrowIfAny(errors);
    }

    public DispatchPlanDefinition(
        DispatchPlanningMode activeMode,
        IEnumerable<HeadwayDirectionPlan>? simpleHeadwayPlans,
        IEnumerable<ManualTimetableRow>? manualTimetableRows,
        VehicleAssignmentMode vehicleAssignmentMode = VehicleAssignmentMode.Automatic)
        : this(simpleHeadwayPlans, manualTimetableRows, activeMode, vehicleAssignmentMode)
    {
    }

    public IReadOnlyList<HeadwayDirectionPlan> SimpleHeadwayPlans { get; }
    public IReadOnlyList<ManualTimetableRow> ManualTimetableRows { get; }
    public DispatchPlanningMode ActiveMode { get; }
    public VehicleAssignmentMode VehicleAssignmentMode { get; }
}

public sealed class PlannedServiceRun
{
    public PlannedServiceRun(
        string serviceRunId,
        TimeSpan plannedDepartureTime,
        TrainDirection direction,
        string? vehicleId,
        string vehicleTypeId,
        string serviceTypeId,
        string stopPatternId,
        string? originPlatformId,
        int sequence,
        bool continueAfterTerminal = false,
        string? continuationServiceRunId = null)
    {
        var errors = new List<string>();
        ServiceRunId = NormalizeRequired(serviceRunId, "計畫車次 ID", errors);
        ValidateClock(plannedDepartureTime, "計畫發車時間", errors);
        ValidateDirection(direction, errors);
        VehicleId = NormalizeOptional(vehicleId, "計畫車輛 ID", errors);
        VehicleTypeId = NormalizeRequired(vehicleTypeId, "計畫車型 ID", errors);
        ServiceTypeId = NormalizeRequired(serviceTypeId, "計畫服務類型 ID", errors);
        StopPatternId = NormalizeRequired(stopPatternId, "計畫停站模式 ID", errors);
        OriginPlatformId = NormalizeOptional(originPlatformId, "計畫起點月台 ID", errors);
        ContinuationServiceRunId = NormalizeOptional(continuationServiceRunId, "折返接續車次 ID", errors);
        if (sequence < 0)
        {
            errors.Add("計畫車次排序序號不可為負數。");
        }

        PlannedDepartureTime = plannedDepartureTime;
        Direction = direction;
        Sequence = sequence;
        ContinueAfterTerminal = continueAfterTerminal || ContinuationServiceRunId is not null;
        RouteValidator.ThrowIfAny(errors);
    }

    public string ServiceRunId { get; }
    public TimeSpan PlannedDepartureTime { get; }
    public TrainDirection Direction { get; }
    public string? VehicleId { get; }
    public string VehicleTypeId { get; }
    public string ServiceTypeId { get; }
    public string StopPatternId { get; }
    public string? OriginPlatformId { get; }
    public int Sequence { get; }
    public bool ContinueAfterTerminal { get; }
    public string? ContinuationServiceRunId { get; }
}

public sealed class ResolvedDispatchPlan
{
    public ResolvedDispatchPlan(
        DispatchPlanningMode activeMode,
        VehicleAssignmentMode vehicleAssignmentMode,
        TimeSpan scheduleAnchorTime,
        IEnumerable<PlannedServiceRun> runs)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(activeMode)) errors.Add("發車規劃模式無效。");
        if (!Enum.IsDefined(vehicleAssignmentMode)) errors.Add("車輛配置模式無效。");
        ValidateClock(scheduleAnchorTime, "排程基準時間", errors);
        var values = runs?.ToArray() ?? [];
        if (values.Length == 0) errors.Add("展開後至少需要一個計畫車次。");
        Runs = ReadOnly(values);
        ActiveMode = activeMode;
        VehicleAssignmentMode = vehicleAssignmentMode;
        ScheduleAnchorTime = scheduleAnchorTime;
        RouteValidator.ThrowIfAny(errors);
    }

    public DispatchPlanningMode ActiveMode { get; }
    public VehicleAssignmentMode VehicleAssignmentMode { get; }
    public TimeSpan ScheduleAnchorTime { get; }
    public IReadOnlyList<PlannedServiceRun> Runs { get; }
    public IReadOnlyList<PlannedServiceRun> PlannedRuns => Runs;
}

public static class DispatchPlanExpander
{
    public static ResolvedDispatchPlan Expand(DispatchPlanDefinition definition) =>
        ExpandCore(definition, null, null, null);

    public static ResolvedDispatchPlan Expand(
        DispatchPlanDefinition definition,
        IEnumerable<VehicleTypeDefinition> vehicleTypes,
        IEnumerable<ServiceTypeDefinition> serviceTypes,
        IEnumerable<StopPatternDefinition> stopPatterns) =>
        ExpandCore(definition, vehicleTypes, serviceTypes, stopPatterns);

    private static ResolvedDispatchPlan ExpandCore(
        DispatchPlanDefinition definition,
        IEnumerable<VehicleTypeDefinition>? vehicleTypes,
        IEnumerable<ServiceTypeDefinition>? serviceTypes,
        IEnumerable<StopPatternDefinition>? stopPatterns)
    {
        if (definition is null)
        {
            throw new SimulationValidationException(["發車規劃不可為空。"]);
        }

        var errors = new List<string>();
        var vehicleCatalog = BuildCatalog(vehicleTypes, "車型", errors);
        var serviceCatalog = BuildCatalog(serviceTypes, "服務類型", errors);
        var stopCatalog = BuildCatalog(stopPatterns, "停站模式", errors);
        var candidates = new List<Candidate>();
        var sourceIndex = 0;

        if (definition.ActiveMode == DispatchPlanningMode.SimpleHeadway)
        {
            if (definition.SimpleHeadwayPlans.Count == 0) errors.Add("簡易班距模式至少需要一個方向計畫。");
            for (var planIndex = 0; planIndex < definition.SimpleHeadwayPlans.Count; planIndex++)
            {
                var plan = definition.SimpleHeadwayPlans[planIndex];
                for (var runIndex = 0; runIndex < plan.RunCount; runIndex++)
                {
                    var raw = plan.FirstDepartureTime + TimeSpan.FromTicks(plan.Headway.Ticks * (long)runIndex);
                    candidates.Add(new Candidate(raw, plan.Direction, plan.ServiceTypeId, plan.VehicleTypeId,
                        plan.StopPatternId, plan.OriginPlatformId, plan.VehicleId, null,
                        plan.ContinueAfterTerminal, null, sourceIndex++));
                }
            }
        }
        else if (definition.ActiveMode == DispatchPlanningMode.ManualTimetable)
        {
            if (definition.ManualTimetableRows.Count == 0) errors.Add("手動班表模式至少需要一筆班表資料。");
            foreach (var row in definition.ManualTimetableRows)
            {
                candidates.Add(new Candidate(row.PlannedDepartureTime, row.Direction, row.ServiceTypeId, row.VehicleTypeId,
                    row.StopPatternId, row.OriginPlatformId, row.VehicleId, row.ServiceRunId,
                    row.ContinueAfterTerminal, row.ContinuationServiceRunId, sourceIndex++));
            }
        }

        RouteValidator.ThrowIfAny(errors);
        // 使用第一筆輸入作為服務日基準，才能將 23:xx 後接 00:xx
        // 解讀為跨午夜，而不是把凌晨班次錯排到前一天。
        var anchor = candidates[0].RawTime;
        var ordered = candidates
            .Select(candidate => candidate with { Order = RelativeSeconds(candidate.RawTime, anchor) })
            .OrderBy(candidate => candidate.Order)
            .ThenBy(candidate => candidate.SourceIndex)
            .ToArray();

        var usedRunIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ordered.Where(candidate => candidate.ExplicitServiceRunId is not null))
        {
            if (!usedRunIds.Add(candidate.ExplicitServiceRunId!))
            {
                errors.Add($"服務車次 ID「{candidate.ExplicitServiceRunId}」重複。");
            }
        }

        var runs = new List<PlannedServiceRun>(ordered.Length);
        var prefixCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var autoVehicleCounter = 1;
        var usedVehicleIds = new HashSet<string>(
            ordered.Where(candidate => candidate.VehicleId is not null).Select(candidate => candidate.VehicleId!),
            StringComparer.OrdinalIgnoreCase);
        var inheritedVehicleTargets = ordered
            .Where(candidate => candidate.ContinuationServiceRunId is not null)
            .Select(candidate => candidate.ContinuationServiceRunId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < ordered.Length; index++)
        {
            var candidate = ordered[index];
            var service = Resolve(serviceCatalog, candidate.ServiceTypeId, "服務類型", errors);
            var vehicleTypeId = candidate.VehicleTypeId ?? service?.DefaultVehicleTypeId;
            var vehicle = vehicleTypeId is not null && vehicleCatalog is not null
                && vehicleCatalog.TryGetValue(vehicleTypeId, out var configuredVehicle)
                ? configuredVehicle
                : null;
            var stopPatternId = candidate.StopPatternId ?? vehicle?.DefaultStopPatternId ?? service?.DefaultStopPatternId;
            RequireReference(vehicleCatalog, vehicleTypeId, "車型", errors);
            RequireReference(stopCatalog, stopPatternId, "停站模式", errors);
            if (service is null && serviceCatalog is not null) errors.Add($"找不到服務類型「{candidate.ServiceTypeId}」。");
            if (string.IsNullOrWhiteSpace(vehicleTypeId)) errors.Add($"車次的車型 ID 不可空白（服務類型：{candidate.ServiceTypeId}）。");
            if (string.IsNullOrWhiteSpace(stopPatternId)) errors.Add($"車次的停站模式 ID 不可空白（服務類型：{candidate.ServiceTypeId}）。");

            var vehicleId = candidate.VehicleId;
            if (vehicleId is null)
            {
                var inheritsVehicle = candidate.ExplicitServiceRunId is not null
                    && inheritedVehicleTargets.Contains(candidate.ExplicitServiceRunId);
                if (definition.VehicleAssignmentMode is VehicleAssignmentMode.ExplicitOnly or VehicleAssignmentMode.Specified
                    && !inheritsVehicle)
                {
                    errors.Add($"計畫車次（第 {index + 1} 筆）未指定車輛。");
                }
                else
                {
                    do vehicleId = $"AUTO-{autoVehicleCounter++:000}"; while (!usedVehicleIds.Add(vehicleId));
                }
            }

            var prefix = service?.RunPrefix;
            if (string.IsNullOrWhiteSpace(prefix)) prefix = "RUN";
            var directionCode = candidate.Direction == TrainDirection.Outbound ? "D" : "U";
            var counterKey = $"{prefix}|{directionCode}";
            var runId = candidate.ExplicitServiceRunId;
            if (runId is null)
            {
                var counter = prefixCounters.TryGetValue(counterKey, out var current) ? current : 0;
                do
                {
                    counter++;
                    runId = $"{prefix}-{directionCode}{counter:000}";
                } while (usedRunIds.Contains(runId));
                prefixCounters[counterKey] = counter;
                usedRunIds.Add(runId);
            }

            if (vehicleTypeId is not null && stopPatternId is not null)
            {
                runs.Add(new PlannedServiceRun(runId, NormalizeClock(candidate.RawTime), candidate.Direction, vehicleId,
                    vehicleTypeId, candidate.ServiceTypeId, stopPatternId, candidate.OriginPlatformId, index,
                    candidate.ContinueAfterTerminal, candidate.ContinuationServiceRunId));
            }
        }

        ValidateContinuationLinks(runs, ordered, errors);
        RouteValidator.ThrowIfAny(errors);
        runs = ApplyContinuationVehicleAssignments(runs);
        return new ResolvedDispatchPlan(definition.ActiveMode, definition.VehicleAssignmentMode,
            anchor, runs);
    }

    private sealed record Candidate(
        TimeSpan RawTime,
        TrainDirection Direction,
        string ServiceTypeId,
        string? VehicleTypeId,
        string? StopPatternId,
        string? OriginPlatformId,
        string? VehicleId,
        string? ExplicitServiceRunId,
        bool ContinueAfterTerminal,
        string? ContinuationServiceRunId,
        int SourceIndex,
        double Order = 0);

    private static void ValidateContinuationLinks(
        IReadOnlyList<PlannedServiceRun> runs,
        IReadOnlyList<Candidate> candidates,
        ICollection<string> errors)
    {
        var byId = runs.ToDictionary(run => run.ServiceRunId, StringComparer.OrdinalIgnoreCase);
        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in runs.Where(run => run.ContinuationServiceRunId is not null))
        {
            if (!byId.TryGetValue(source.ContinuationServiceRunId!, out var target))
            {
                errors.Add($"車次「{source.ServiceRunId}」的折返接續目標「{source.ContinuationServiceRunId}」不存在。");
                continue;
            }
            if (target.Direction == source.Direction)
            {
                errors.Add($"車次「{source.ServiceRunId}」的折返接續目標必須是反向車次。");
            }
            if (target.Sequence <= source.Sequence)
            {
                errors.Add($"車次「{source.ServiceRunId}」的折返接續目標必須排在來源車次之後。");
            }
            if (!target.VehicleTypeId.Equals(source.VehicleTypeId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"車次「{source.ServiceRunId}」與接續車次「{target.ServiceRunId}」必須使用相同車型，因為是同一實體車輛。");
            }
            if (!usedTargets.Add(target.ServiceRunId))
            {
                errors.Add($"折返接續目標「{target.ServiceRunId}」不可由多個來源車次共用。");
            }

            var sourceVehicle = candidates[source.Sequence].VehicleId;
            var targetVehicle = candidates[target.Sequence].VehicleId;
            if (sourceVehicle is not null && targetVehicle is not null
                && !sourceVehicle.Equals(targetVehicle, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"接續車次「{target.ServiceRunId}」若指定車輛，必須與來源車次「{source.ServiceRunId}」相同。");
            }
        }
    }

    private static List<PlannedServiceRun> ApplyContinuationVehicleAssignments(IReadOnlyList<PlannedServiceRun> runs)
    {
        var sourceByTarget = runs
            .Where(run => run.ContinuationServiceRunId is not null)
            .ToDictionary(run => run.ContinuationServiceRunId!, StringComparer.OrdinalIgnoreCase);
        var vehicleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string ResolveVehicle(PlannedServiceRun run)
        {
            if (vehicleCache.TryGetValue(run.ServiceRunId, out var cached)) return cached;
            var vehicleId = sourceByTarget.TryGetValue(run.ServiceRunId, out var source)
                ? ResolveVehicle(source)
                : run.VehicleId!;
            vehicleCache[run.ServiceRunId] = vehicleId;
            return vehicleId;
        }

        return runs.Select(run => new PlannedServiceRun(
            run.ServiceRunId,
            run.PlannedDepartureTime,
            run.Direction,
            ResolveVehicle(run),
            run.VehicleTypeId,
            run.ServiceTypeId,
            run.StopPatternId,
            run.OriginPlatformId,
            run.Sequence,
            run.ContinueAfterTerminal,
            run.ContinuationServiceRunId)).ToList();
    }

    private static Dictionary<string, T>? BuildCatalog<T>(IEnumerable<T>? values, string label, ICollection<string> errors)
        where T : class
    {
        if (values is null) return null;
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var id = value switch
            {
                VehicleTypeDefinition vehicle => vehicle.Id,
                ServiceTypeDefinition service => service.Id,
                StopPatternDefinition pattern => pattern.Id,
                _ => string.Empty
            };
            if (!result.TryAdd(id, value)) errors.Add($"{label} ID「{id}」重複。");
        }
        return result;
    }

    private static T? Resolve<T>(Dictionary<string, T>? catalog, string id, string label, ICollection<string> errors)
        where T : class
    {
        if (catalog is null) return null;
        if (catalog.TryGetValue(id, out var value)) return value;
        errors.Add($"找不到{label}「{id}」。");
        return null;
    }

    private static void RequireReference<T>(Dictionary<string, T>? catalog, string? id, string label, ICollection<string> errors)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (catalog is not null && !catalog.ContainsKey(id)) errors.Add($"找不到{label}「{id}」。");
    }

    private static double RelativeSeconds(TimeSpan value, TimeSpan anchor)
    {
        var result = (value - anchor).TotalSeconds;
        while (result < 0) result += TimeSpan.FromDays(1).TotalSeconds;
        return result;
    }

    private static TimeSpan NormalizeClock(TimeSpan value)
    {
        var seconds = value.TotalSeconds % TimeSpan.FromDays(1).TotalSeconds;
        if (seconds < 0) seconds += TimeSpan.FromDays(1).TotalSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    private static void ValidateDirection(TrainDirection value, ICollection<string> errors)
    {
        if (!Enum.IsDefined(value)) errors.Add("行駛方向無效。");
    }

    private static void ValidateClock(TimeSpan value, string name, ICollection<string> errors)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1)) errors.Add($"{name}必須介於 00:00:00（含）與 24:00:00（不含）。");
    }

    private static string NormalizeRequired(string? value, string name, ICollection<string> errors)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0) errors.Add($"{name}不可空白。");
        return result;
    }

    private static string? NormalizeOptional(string? value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (value is not null) errors.Add($"{name}不可只含空白。");
            return null;
        }
        return value.Trim();
    }

    private static IReadOnlyList<string> ReadOnlyStrings(IEnumerable<string>? values, string label, ICollection<string> errors)
    {
        var result = new List<string>();
        foreach (var value in values ?? [])
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0) errors.Add($"{label}不可空白。");
            else if (result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) errors.Add($"{label}「{normalized}」重複。");
            else result.Add(normalized);
        }
        return new ReadOnlyCollection<string>(result);
    }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());

    private static void RequirePositiveFinite(double value, string name, ICollection<string> errors)
    {
        if (!RouteValidator.IsFinite(value) || value <= 0) errors.Add($"{name}必須是有限且大於 0 的數值。");
    }

    private static void RequireNonNegativeFinite(double value, string name, ICollection<string> errors)
    {
        if (!RouteValidator.IsFinite(value) || value < 0) errors.Add($"{name}必須是有限的非負數。");
    }
}

internal static class PlanningModelValidation
{
    internal static void ValidateDirection(TrainDirection value, ICollection<string> errors)
    {
        if (!Enum.IsDefined(value)) errors.Add("行駛方向無效。");
    }

    internal static void ValidateClock(TimeSpan value, string name, ICollection<string> errors)
    {
        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
        {
            errors.Add($"{name}必須介於 00:00:00（含）與 24:00:00（不含）。");
        }
    }

    internal static string NormalizeRequired(string? value, string name, ICollection<string> errors)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0) errors.Add($"{name}不可空白。");
        return result;
    }

    internal static string? NormalizeOptional(string? value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (value is not null) errors.Add($"{name}不可只含空白。");
            return null;
        }

        return value.Trim();
    }

    internal static IReadOnlyList<string> ReadOnlyStrings(
        IEnumerable<string>? values,
        string label,
        ICollection<string> errors)
    {
        var result = new List<string>();
        foreach (var value in values ?? [])
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (normalized.Length == 0) errors.Add($"{label}不可空白。");
            else if (result.Contains(normalized, StringComparer.OrdinalIgnoreCase)) errors.Add($"{label}「{normalized}」重複。");
            else result.Add(normalized);
        }

        return new ReadOnlyCollection<string>(result);
    }

    internal static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        new ReadOnlyCollection<T>(values.ToArray());

    internal static void RequirePositiveFinite(double value, string name, ICollection<string> errors)
    {
        if (!RouteValidator.IsFinite(value) || value <= 0)
        {
            errors.Add($"{name}必須是有限且大於 0 的數值。");
        }
    }

    internal static void RequireNonNegativeFinite(double value, string name, ICollection<string> errors)
    {
        if (!RouteValidator.IsFinite(value) || value < 0)
        {
            errors.Add($"{name}必須是有限的非負數。");
        }
    }
}
