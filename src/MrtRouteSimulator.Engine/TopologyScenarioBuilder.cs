namespace MrtRouteSimulator.Engine;

/// <summary>大型 topology sample 建模時的階段名稱。</summary>
public enum TopologyScenarioStage
{
    MinimalTopologyBaseline,
    FullStationChain,
    ServicePatterns,
    TurnbackFacilities,
    PassingFacilities,
    OperationalTimetable
}

/// <summary>大型 sample 建模快速迴圈所使用的驗證層級。</summary>
public enum TopologyScenarioValidationLayer
{
    Structural,
    Operational,
    Regression
}

/// <summary>單一建模階段的 Structural／Operational 結果。</summary>
public sealed record TopologyScenarioStageValidation(
    TopologyScenarioStage Stage,
    IReadOnlyList<string> StructuralErrors,
    IReadOnlyList<string> OperationalErrors,
    double SmokeDurationSeconds)
{
    public bool StructuralPassed => StructuralErrors.Count == 0;

    public bool OperationalPassed => OperationalErrors.Count == 0;

    public bool IsValid => StructuralPassed && OperationalPassed;

    public IReadOnlyList<string> Errors => StructuralErrors.Concat(OperationalErrors).ToArray();
}

/// <summary>完成一段大型 sample 建模流程後的 immutable 結果。</summary>
public sealed record TopologyScenarioBuildResult(
    TopologyProjectDocument Document,
    IReadOnlyList<TopologyScenarioStageValidation> StageValidations)
{
    public bool IsValid => StageValidations.All(item => item.IsValid);
}

/// <summary>
/// Schema 8 topology sample 的分階段 builder。它只持有既有
/// <see cref="TopologyProjectDocument"/>，不建立第二套 Domain Model 或 runtime。
/// 每個 stage 先通過 Structural，再通過短時段 Operational smoke test，才會成為下一階段的輸入。
/// Release／Engine／WPF 的 Regression gate 仍由外部驗證流程執行。
/// </summary>
public sealed class TopologyScenarioBuilder
{
    private readonly List<TopologyScenarioStageValidation> _stageValidations = [];
    private TopologyProjectDocument _document;

    private TopologyScenarioBuilder(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
    }

    /// <summary>從既有的 minimal topology baseline 開始。</summary>
    public static TopologyScenarioBuilder FromMinimalBaseline(TopologyProjectDocument document)
    {
        var builder = new TopologyScenarioBuilder(document);
        builder.RecordValidatedStage(TopologyScenarioStage.MinimalTopologyBaseline, document, 30);
        return builder;
    }

    /// <summary>
    /// 使用既有 quick builder／linear factory 建立 minimal topology baseline，
    /// 再交由本 builder 逐階段擴充。
    /// </summary>
    public static TopologyScenarioBuilder CreateMinimalBaseline(
        TopologyProjectDocument template,
        IEnumerable<QuickLinearStationInput> stations)
    {
        var document = ProjectDocumentMapper.BuildQuickLinearProject(template, stations);
        return FromMinimalBaseline(document);
    }

    public TopologyProjectDocument Document => _document;

    public IReadOnlyList<TopologyScenarioStageValidation> StageValidations => _stageValidations;

    /// <summary>
    /// 套用一個局部建模階段。候選文件若未通過 Structural 或 Operational，
    /// 不會取代目前文件，也不會讓錯誤狀態流入下一階段。
    /// </summary>
    public TopologyScenarioBuilder AddStage(
        TopologyScenarioStage stage,
        Func<TopologyProjectDocument, TopologyProjectDocument> apply,
        double smokeDurationSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var previousStage = _stageValidations.LastOrDefault()?.Stage;
        if (previousStage is not null && stage < previousStage.Value)
        {
            throw new SimulationValidationException([
                $"大型 sample 階段順序不可由「{previousStage}」退回「{stage}」。"
            ]);
        }

        var candidate = apply(_document) ?? throw new SimulationValidationException([
            $"大型 sample 階段「{stage}」不可回傳空白 Schema 8 文件。"
        ]);
        RecordValidatedStage(stage, candidate, smokeDurationSeconds);

        _document = candidate;
        return this;
    }

    public TopologyScenarioBuildResult Build() =>
        new(_document, _stageValidations.ToArray());

    private void RecordValidatedStage(
        TopologyScenarioStage stage,
        TopologyProjectDocument candidate,
        double smokeDurationSeconds)
    {
        var validation = TopologyScenarioValidation.ValidateStage(candidate, stage, smokeDurationSeconds);
        _stageValidations.Add(validation);
        if (!validation.IsValid)
        {
            throw new SimulationValidationException(validation.Errors);
        }
    }
}

/// <summary>大型 sample 的局部 Structural／Operational 驗證入口。</summary>
public static class TopologyScenarioValidation
{
    public static TopologyScenarioStageValidation ValidateStage(
        TopologyProjectDocument document,
        TopologyScenarioStage stage,
        double smokeDurationSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateSmokeDuration(smokeDurationSeconds);

        var structuralErrors = ValidateStructural(document);
        if (structuralErrors.Count > 0)
        {
            return new(stage, structuralErrors, [], smokeDurationSeconds);
        }

        var operationalErrors = ValidateOperationalSmoke(document, smokeDurationSeconds);
        return new(stage, [], operationalErrors, smokeDurationSeconds);
    }

    /// <summary>
    /// 同時執行 InfrastructureValidator 與完整 Schema 8 persistence/runtime validation。
    /// </summary>
    public static IReadOnlyList<string> ValidateStructural(TopologyProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        try
        {
            errors.AddRange(InfrastructureValidator.Validate(document.Topology, document.ServiceRoutes).Errors);
        }
        catch (Exception exception) when (exception is SimulationValidationException or ArgumentException)
        {
            errors.Add($"InfrastructureValidator 執行失敗：{exception.Message}");
        }

        try
        {
            TopologyProjectFormat.Validate(document);
        }
        catch (SimulationValidationException exception)
        {
            errors.AddRange(exception.Errors);
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// 建立既有 topology-native runtime，並以固定 0.1 秒子步進推進短時段 smoke test。
    /// </summary>
    public static IReadOnlyList<string> ValidateOperationalSmoke(
        TopologyProjectDocument document,
        double smokeDurationSeconds = 30)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateSmokeDuration(smokeDurationSeconds);
        try
        {
            var runtime = TopologyProjectFormat.CreateRuntime(document);
            var world = new SimulationWorldOptions(
                Route: null,
                TrainParameters: runtime.TrainParameters,
                OperationalParameters: runtime.OperationalParameters,
                TrainCount: runtime.DispatchPlan.Runs.Count,
                ProfileMode: OperationProfileMode.RealisticOperations,
                DispatchPlan: runtime.DispatchPlan,
                VehicleTypes: runtime.VehicleTypes,
                ServiceTypes: runtime.ServiceTypes,
                ServicePatterns: runtime.ServicePatterns,
                Topology: runtime.Topology).CreateWorld();
            world.AdvanceTo(smokeDurationSeconds);

            var errors = new List<string>();
            if (world.Trajectory.Count == 0)
            {
                errors.Add("Operational smoke test 未產生任何 topology trajectory sample。 ");
            }

            errors.AddRange(world.Events
                .Where(item => item.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation)
                .Select(item => $"Operational smoke test 出現 {item.EventType}：{item.Message}"));
            return errors.ToArray();
        }
        catch (SimulationValidationException exception)
        {
            return exception.Errors.ToArray();
        }
    }

    private static void ValidateSmokeDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "大型 sample smoke test 時間必須是正的有限數值。 ");
        }
    }
}
