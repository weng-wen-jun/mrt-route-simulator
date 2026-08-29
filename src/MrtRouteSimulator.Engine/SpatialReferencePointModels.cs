namespace MrtRouteSimulator.Engine;

/// <summary>空間參考點的站場型式，名稱對應交通部運研所 URCS (beta) 的設定介面。</summary>
public enum SpatialReferencePointKind
{
    IntermediateStation,
    Junction,
    BeforeStationTurnback,
    AfterStationTurnback,
    CentralSidingTurnback
}

/// <summary>
/// 各站場型式在新增實體車站時套用的預設值。範本本身不帶車站或參考點識別碼；
/// 建立站點時會複製為獨立的 <see cref="SpatialReferencePointDefinition"/>，所以之後修改範本
/// 不會回寫既有站場的個別覆寫。
/// </summary>
public sealed class SpatialReferencePointTemplateDefinition
{
    public SpatialReferencePointTemplateDefinition(
        SpatialReferencePointKind kind,
        bool alternateBerthing = false,
        double mainlineGradePermille = 0,
        double branchlineGradePermille = 0,
        double distanceFromStopToCrossoverMeters = 100,
        double crossoverLengthMeters = 100,
        double distanceFromCrossoverToTurnbackStopMeters = 100,
        double turnbackDwellSeconds = 30,
        double switchSpeedLimitMetersPerSecond = 40 / 3.6,
        double mainlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
        double branchlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
        double mainlineSafetyFactor = 1.5,
        double branchlineSafetyFactor = 1.5,
        double mainlineTrafficRatio = 0.5,
        double stationForwardGradeInPermille = 0,
        double stationForwardGradeOutPermille = 0,
        double stationForwardDistanceToSignalMeters = 15,
        double stationForwardOverlapMeters = 300,
        double stationForwardDwellSeconds = 30,
        double stationForwardEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationForwardLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationForwardSafetyFactor = 1.5,
        double stationReverseGradeInPermille = 0,
        double stationReverseGradeOutPermille = 0,
        double stationReverseDistanceToSignalMeters = 15,
        double stationReverseOverlapMeters = 300,
        double stationReverseDwellSeconds = 30,
        double stationReverseEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationReverseLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationReverseSafetyFactor = 1.5)
    {
        // 以同一個定義的驗證規則驗證範本，避免 UI 範本與實際 Engine 設定接受不同範圍。
        var validation = new SpatialReferencePointDefinition(
            $"TEMPLATE:{kind}", "TEMPLATE", "範本", kind,
            alternateBerthing, mainlineGradePermille, branchlineGradePermille,
            distanceFromStopToCrossoverMeters, crossoverLengthMeters,
            distanceFromCrossoverToTurnbackStopMeters, turnbackDwellSeconds,
            switchSpeedLimitMetersPerSecond, mainlineApproachCruiseSpeedMetersPerSecond,
            branchlineApproachCruiseSpeedMetersPerSecond, mainlineSafetyFactor,
            branchlineSafetyFactor, mainlineTrafficRatio,
            stationForwardGradeInPermille, stationForwardGradeOutPermille,
            stationForwardDistanceToSignalMeters, stationForwardOverlapMeters,
            stationForwardDwellSeconds, stationForwardEarlierCruiseSpeedMetersPerSecond,
            stationForwardLaterCruiseSpeedMetersPerSecond, stationForwardSafetyFactor,
            stationReverseGradeInPermille, stationReverseGradeOutPermille,
            stationReverseDistanceToSignalMeters, stationReverseOverlapMeters,
            stationReverseDwellSeconds, stationReverseEarlierCruiseSpeedMetersPerSecond,
            stationReverseLaterCruiseSpeedMetersPerSecond, stationReverseSafetyFactor);

        Kind = validation.Kind;
        AlternateBerthing = validation.AlternateBerthing;
        MainlineGradePermille = validation.MainlineGradePermille;
        BranchlineGradePermille = validation.BranchlineGradePermille;
        DistanceFromStopToCrossoverMeters = validation.DistanceFromStopToCrossoverMeters;
        CrossoverLengthMeters = validation.CrossoverLengthMeters;
        DistanceFromCrossoverToTurnbackStopMeters = validation.DistanceFromCrossoverToTurnbackStopMeters;
        TurnbackDwellSeconds = validation.TurnbackDwellSeconds;
        SwitchSpeedLimitMetersPerSecond = validation.SwitchSpeedLimitMetersPerSecond;
        MainlineApproachCruiseSpeedMetersPerSecond = validation.MainlineApproachCruiseSpeedMetersPerSecond;
        BranchlineApproachCruiseSpeedMetersPerSecond = validation.BranchlineApproachCruiseSpeedMetersPerSecond;
        MainlineSafetyFactor = validation.MainlineSafetyFactor;
        BranchlineSafetyFactor = validation.BranchlineSafetyFactor;
        MainlineTrafficRatio = validation.MainlineTrafficRatio;
        StationForwardGradeInPermille = validation.StationForwardGradeInPermille;
        StationForwardGradeOutPermille = validation.StationForwardGradeOutPermille;
        StationForwardDistanceToSignalMeters = validation.StationForwardDistanceToSignalMeters;
        StationForwardOverlapMeters = validation.StationForwardOverlapMeters;
        StationForwardDwellSeconds = validation.StationForwardDwellSeconds;
        StationForwardEarlierCruiseSpeedMetersPerSecond = validation.StationForwardEarlierCruiseSpeedMetersPerSecond;
        StationForwardLaterCruiseSpeedMetersPerSecond = validation.StationForwardLaterCruiseSpeedMetersPerSecond;
        StationForwardSafetyFactor = validation.StationForwardSafetyFactor;
        StationReverseGradeInPermille = validation.StationReverseGradeInPermille;
        StationReverseGradeOutPermille = validation.StationReverseGradeOutPermille;
        StationReverseDistanceToSignalMeters = validation.StationReverseDistanceToSignalMeters;
        StationReverseOverlapMeters = validation.StationReverseOverlapMeters;
        StationReverseDwellSeconds = validation.StationReverseDwellSeconds;
        StationReverseEarlierCruiseSpeedMetersPerSecond = validation.StationReverseEarlierCruiseSpeedMetersPerSecond;
        StationReverseLaterCruiseSpeedMetersPerSecond = validation.StationReverseLaterCruiseSpeedMetersPerSecond;
        StationReverseSafetyFactor = validation.StationReverseSafetyFactor;
    }

    public SpatialReferencePointKind Kind { get; }
    public bool AlternateBerthing { get; }
    public double MainlineGradePermille { get; }
    public double BranchlineGradePermille { get; }
    public double DistanceFromStopToCrossoverMeters { get; }
    public double CrossoverLengthMeters { get; }
    public double DistanceFromCrossoverToTurnbackStopMeters { get; }
    public double TurnbackDwellSeconds { get; }
    public double SwitchSpeedLimitMetersPerSecond { get; }
    public double MainlineApproachCruiseSpeedMetersPerSecond { get; }
    public double BranchlineApproachCruiseSpeedMetersPerSecond { get; }
    public double MainlineSafetyFactor { get; }
    public double BranchlineSafetyFactor { get; }
    public double MainlineTrafficRatio { get; }
    public double StationForwardGradeInPermille { get; }
    public double StationForwardGradeOutPermille { get; }
    public double StationForwardDistanceToSignalMeters { get; }
    public double StationForwardOverlapMeters { get; }
    public double StationForwardDwellSeconds { get; }
    public double StationForwardEarlierCruiseSpeedMetersPerSecond { get; }
    public double StationForwardLaterCruiseSpeedMetersPerSecond { get; }
    public double StationForwardSafetyFactor { get; }
    public double StationReverseGradeInPermille { get; }
    public double StationReverseGradeOutPermille { get; }
    public double StationReverseDistanceToSignalMeters { get; }
    public double StationReverseOverlapMeters { get; }
    public double StationReverseDwellSeconds { get; }
    public double StationReverseEarlierCruiseSpeedMetersPerSecond { get; }
    public double StationReverseLaterCruiseSpeedMetersPerSecond { get; }
    public double StationReverseSafetyFactor { get; }

    public SpatialReferencePointDefinition Create(
        string referencePointId,
        string stationId,
        string name,
        double? stationForwardDwellSecondsOverride = null,
        double? stationReverseDwellSecondsOverride = null) => new(
        referencePointId, stationId, name, Kind, AlternateBerthing, MainlineGradePermille, BranchlineGradePermille,
        DistanceFromStopToCrossoverMeters, CrossoverLengthMeters, DistanceFromCrossoverToTurnbackStopMeters,
        TurnbackDwellSeconds, SwitchSpeedLimitMetersPerSecond, MainlineApproachCruiseSpeedMetersPerSecond,
        BranchlineApproachCruiseSpeedMetersPerSecond, MainlineSafetyFactor, BranchlineSafetyFactor, MainlineTrafficRatio,
        StationForwardGradeInPermille, StationForwardGradeOutPermille, StationForwardDistanceToSignalMeters,
        StationForwardOverlapMeters, stationForwardDwellSecondsOverride ?? StationForwardDwellSeconds, StationForwardEarlierCruiseSpeedMetersPerSecond,
        StationForwardLaterCruiseSpeedMetersPerSecond, StationForwardSafetyFactor, StationReverseGradeInPermille,
        StationReverseGradeOutPermille, StationReverseDistanceToSignalMeters, StationReverseOverlapMeters,
        stationReverseDwellSecondsOverride ?? StationReverseDwellSeconds, StationReverseEarlierCruiseSpeedMetersPerSecond,
        StationReverseLaterCruiseSpeedMetersPerSecond, StationReverseSafetyFactor);

    public static IReadOnlyList<SpatialReferencePointTemplateDefinition> CreateDefaults() =>
        Enum.GetValues<SpatialReferencePointKind>()
            .Select(kind => new SpatialReferencePointTemplateDefinition(kind))
            .ToArray();
}

/// <summary>
/// 車站或銜接點的高階幾何與營運參數。股道、進路與衝突資源仍由 InfrastructureGraph
/// 的詳細模型管理；此模型提供站場型式、路線圖呈現與折返時間覆寫。
/// </summary>
public sealed class SpatialReferencePointDefinition
{
    public SpatialReferencePointDefinition(
        string referencePointId,
        string stationId,
        string name,
        SpatialReferencePointKind kind,
        bool alternateBerthing = false,
        double mainlineGradePermille = 0,
        double branchlineGradePermille = 0,
        double distanceFromStopToCrossoverMeters = 100,
        double crossoverLengthMeters = 100,
        double distanceFromCrossoverToTurnbackStopMeters = 100,
        double turnbackDwellSeconds = 30,
        double switchSpeedLimitMetersPerSecond = 40 / 3.6,
        double mainlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
        double branchlineApproachCruiseSpeedMetersPerSecond = 80 / 3.6,
        double mainlineSafetyFactor = 1.5,
        double branchlineSafetyFactor = 1.5,
        double mainlineTrafficRatio = 0.5,
        double stationForwardGradeInPermille = 0,
        double stationForwardGradeOutPermille = 0,
        double stationForwardDistanceToSignalMeters = 15,
        double stationForwardOverlapMeters = 300,
        double stationForwardDwellSeconds = 30,
        double stationForwardEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationForwardLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationForwardSafetyFactor = 1.5,
        double stationReverseGradeInPermille = 0,
        double stationReverseGradeOutPermille = 0,
        double stationReverseDistanceToSignalMeters = 15,
        double stationReverseOverlapMeters = 300,
        double stationReverseDwellSeconds = 30,
        double stationReverseEarlierCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationReverseLaterCruiseSpeedMetersPerSecond = 80 / 3.6,
        double stationReverseSafetyFactor = 1.5)
    {
        ReferencePointId = PlatformDefinition.NormalizeRequired(referencePointId, "空間參考點編號");
        StationId = PlatformDefinition.NormalizeRequired(stationId, "空間參考點所屬車站");
        Name = string.IsNullOrWhiteSpace(name) ? ReferencePointId : name.Trim();
        Kind = kind;
        AlternateBerthing = alternateBerthing;
        MainlineGradePermille = mainlineGradePermille;
        BranchlineGradePermille = branchlineGradePermille;
        DistanceFromStopToCrossoverMeters = distanceFromStopToCrossoverMeters;
        CrossoverLengthMeters = crossoverLengthMeters;
        DistanceFromCrossoverToTurnbackStopMeters = distanceFromCrossoverToTurnbackStopMeters;
        TurnbackDwellSeconds = turnbackDwellSeconds;
        SwitchSpeedLimitMetersPerSecond = switchSpeedLimitMetersPerSecond;
        MainlineApproachCruiseSpeedMetersPerSecond = mainlineApproachCruiseSpeedMetersPerSecond;
        BranchlineApproachCruiseSpeedMetersPerSecond = branchlineApproachCruiseSpeedMetersPerSecond;
        MainlineSafetyFactor = mainlineSafetyFactor;
        BranchlineSafetyFactor = branchlineSafetyFactor;
        MainlineTrafficRatio = mainlineTrafficRatio;
        StationForwardGradeInPermille = stationForwardGradeInPermille;
        StationForwardGradeOutPermille = stationForwardGradeOutPermille;
        StationForwardDistanceToSignalMeters = stationForwardDistanceToSignalMeters;
        StationForwardOverlapMeters = stationForwardOverlapMeters;
        StationForwardDwellSeconds = stationForwardDwellSeconds;
        StationForwardEarlierCruiseSpeedMetersPerSecond = stationForwardEarlierCruiseSpeedMetersPerSecond;
        StationForwardLaterCruiseSpeedMetersPerSecond = stationForwardLaterCruiseSpeedMetersPerSecond;
        StationForwardSafetyFactor = stationForwardSafetyFactor;
        StationReverseGradeInPermille = stationReverseGradeInPermille;
        StationReverseGradeOutPermille = stationReverseGradeOutPermille;
        StationReverseDistanceToSignalMeters = stationReverseDistanceToSignalMeters;
        StationReverseOverlapMeters = stationReverseOverlapMeters;
        StationReverseDwellSeconds = stationReverseDwellSeconds;
        StationReverseEarlierCruiseSpeedMetersPerSecond = stationReverseEarlierCruiseSpeedMetersPerSecond;
        StationReverseLaterCruiseSpeedMetersPerSecond = stationReverseLaterCruiseSpeedMetersPerSecond;
        StationReverseSafetyFactor = stationReverseSafetyFactor;
        Validate();
    }

    public string ReferencePointId { get; }
    public string StationId { get; }
    public string Name { get; }
    public SpatialReferencePointKind Kind { get; }
    public bool AlternateBerthing { get; }
    public double MainlineGradePermille { get; }
    public double BranchlineGradePermille { get; }
    public double DistanceFromStopToCrossoverMeters { get; }
    public double CrossoverLengthMeters { get; }
    public double DistanceFromCrossoverToTurnbackStopMeters { get; }
    public double TurnbackDwellSeconds { get; }
    public double SwitchSpeedLimitMetersPerSecond { get; }
    public double MainlineApproachCruiseSpeedMetersPerSecond { get; }
    public double BranchlineApproachCruiseSpeedMetersPerSecond { get; }
    public double MainlineSafetyFactor { get; }
    public double BranchlineSafetyFactor { get; }
    public double MainlineTrafficRatio { get; }
    public double StationForwardGradeInPermille { get; }
    public double StationForwardGradeOutPermille { get; }
    public double StationForwardDistanceToSignalMeters { get; }
    public double StationForwardOverlapMeters { get; }
    public double StationForwardDwellSeconds { get; }
    public double StationForwardEarlierCruiseSpeedMetersPerSecond { get; }
    public double StationForwardLaterCruiseSpeedMetersPerSecond { get; }
    public double StationForwardSafetyFactor { get; }
    public double StationReverseGradeInPermille { get; }
    public double StationReverseGradeOutPermille { get; }
    public double StationReverseDistanceToSignalMeters { get; }
    public double StationReverseOverlapMeters { get; }
    public double StationReverseDwellSeconds { get; }
    public double StationReverseEarlierCruiseSpeedMetersPerSecond { get; }
    public double StationReverseLaterCruiseSpeedMetersPerSecond { get; }
    public double StationReverseSafetyFactor { get; }

    public bool IsTurnback => Kind is SpatialReferencePointKind.BeforeStationTurnback
        or SpatialReferencePointKind.AfterStationTurnback
        or SpatialReferencePointKind.CentralSidingTurnback;

    /// <summary>
    /// 站前折返的停站秒數取代車站一般停站秒數；站後與中央避車線折返則作為站後額外停等。
    /// </summary>
    public double AdditionalTurnbackSeconds =>
        Kind == SpatialReferencePointKind.BeforeStationTurnback ? 0 : TurnbackDwellSeconds;

    private void Validate()
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(Kind))
        {
            errors.Add("空間參考點型式不是有效的設定。");
        }

        RequireRange(MainlineGradePermille, -30, 30, "主線／折返線坡度", errors);
        if (Kind == SpatialReferencePointKind.Junction)
        {
            RequireRange(BranchlineGradePermille, -30, 30, "側線坡度", errors);
        }

        RequireRange(CrossoverLengthMeters, 0, 500, "橫渡線區長度", errors);
        RequireRange(SwitchSpeedLimitMetersPerSecond * 3.6, 1, 60, "道岔限速", errors);

        if (IsTurnback)
        {
            RequireRange(DistanceFromStopToCrossoverMeters, 0, 1000, "停車位置至橫渡線區距離", errors);
            RequireRange(TurnbackDwellSeconds, 0, 1800, "折返停等時間", errors);
        }

        if (Kind == SpatialReferencePointKind.AfterStationTurnback)
        {
            RequireRange(DistanceFromCrossoverToTurnbackStopMeters, 0, 1000, "橫渡線區至尾軌停車區距離", errors);
        }
        else if (Kind == SpatialReferencePointKind.CentralSidingTurnback)
        {
            RequireRange(DistanceFromCrossoverToTurnbackStopMeters, 0, 500, "橫渡線區至中央避車線停車區距離", errors);
        }

        if (Kind is SpatialReferencePointKind.Junction or SpatialReferencePointKind.BeforeStationTurnback)
        {
            RequireRange(MainlineApproachCruiseSpeedMetersPerSecond * 3.6, 1, 100, "主線／進站前巡航速度", errors);
            RequireRange(MainlineSafetyFactor, 1, 5, "主線間隔安全係數", errors);
        }

        if (Kind == SpatialReferencePointKind.Junction)
        {
            RequireRange(BranchlineApproachCruiseSpeedMetersPerSecond * 3.6, 1, 100, "側線巡航速度", errors);
            RequireRange(BranchlineSafetyFactor, 1, 5, "側線間隔安全係數", errors);
            RequireRange(MainlineTrafficRatio, 0, 1, "主線列車連續通過比例", errors);
        }

        if (Kind == SpatialReferencePointKind.IntermediateStation)
        {
            ValidateStationDirection(
                StationForwardGradeInPermille,
                StationForwardGradeOutPermille,
                StationForwardDistanceToSignalMeters,
                StationForwardOverlapMeters,
                StationForwardDwellSeconds,
                StationForwardEarlierCruiseSpeedMetersPerSecond,
                StationForwardLaterCruiseSpeedMetersPerSecond,
                StationForwardSafetyFactor,
                "順行",
                errors);
            ValidateStationDirection(
                StationReverseGradeInPermille,
                StationReverseGradeOutPermille,
                StationReverseDistanceToSignalMeters,
                StationReverseOverlapMeters,
                StationReverseDwellSeconds,
                StationReverseEarlierCruiseSpeedMetersPerSecond,
                StationReverseLaterCruiseSpeedMetersPerSecond,
                StationReverseSafetyFactor,
                "逆行",
                errors);
        }

        PlatformDefinition.Throw(errors);
    }

    private static void ValidateStationDirection(
        double gradeIn,
        double gradeOut,
        double distanceToSignal,
        double overlap,
        double dwell,
        double earlierCruiseSpeed,
        double laterCruiseSpeed,
        double safetyFactor,
        string direction,
        ICollection<string> errors)
    {
        RequireRange(gradeIn, -30, 30, $"{direction}進站坡度", errors);
        RequireRange(gradeOut, -30, 30, $"{direction}離站坡度", errors);
        RequireRange(distanceToSignal, 0, 100, $"{direction}停車位置至區間離開點距離", errors);
        RequireRange(overlap, 0, 500, $"{direction}安全重疊區間長度", errors);
        RequireRange(dwell, 0, 120, $"{direction}停站時間", errors);
        RequireRange(earlierCruiseSpeed * 3.6, 1, 100, $"{direction}續行列車進站前巡航速度", errors);
        RequireRange(laterCruiseSpeed * 3.6, 1, 100, $"{direction}先行列車離站後巡航速度", errors);
        RequireRange(safetyFactor, 1, 5, $"{direction}間隔安全係數", errors);
    }

    private static void RequireRange(double value, double minimum, double maximum, string field, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            errors.Add($"{field}必須介於 {minimum:0.##} 至 {maximum:0.##}。" );
        }
    }
}
