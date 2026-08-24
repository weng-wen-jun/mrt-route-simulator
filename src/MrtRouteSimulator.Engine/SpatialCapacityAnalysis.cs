namespace MrtRouteSimulator.Engine;

/// <summary>URCS 相容容量計算的方向；只有中間站使用獨立的順、逆行輸入。</summary>
public enum SpatialCapacityDirection
{
    Forward,
    Reverse
}

/// <summary>URCS 全域容量參數；預設值與 URCS (beta) 一致。</summary>
public sealed record SpatialCapacityGlobalParameters(
    double InterchangeTimeSeconds = 10,
    double ReactionTimeSeconds = 1,
    double TrainPeakHourFactor = 0.8,
    double MarginRate = 0.3);

/// <summary>URCS 列車容量與動力參數；速度統一由各空間參考點以 m/s 儲存。</summary>
public sealed record SpatialCapacityTrainParameters(
    double LengthMeters = 141,
    double AccelerationMetersPerSecondSquared = 1,
    double DecelerationMetersPerSecondSquared = 1,
    double BrakeEffect = 0.75,
    int SeatCount = 352,
    double StandingAreaSquareMeters = 264,
    int StandingDensityPerSquareMeter = 6)
{
    public double PassengerCapacity => SeatCount + StandingAreaSquareMeters * StandingDensityPerSquareMeter;

    public double EffectiveAcceleration(double gradePermille) =>
        AccelerationMetersPerSecondSquared - 0.009 * gradePermille;

    public double EffectiveDeceleration(double gradePermille) =>
        DecelerationMetersPerSecondSquared + 0.009 * gradePermille;
}

/// <summary>同時保留安全時距與加入營運餘裕後的設計班距，避免沿用舊版 headway 語意混淆。</summary>
public sealed record SpatialCapacityResult(
    double SafetyHeadwaySeconds,
    double DesignHeadwaySeconds,
    int LineCapacityPerHour,
    int DesignPassengerCapacityPerHour,
    int AchievablePassengerCapacityPerHour);

/// <summary>
/// 依 URCS (beta) Components.dll 的 IL 行為重製五類空間參考點公式。
/// 一般容量採向零截斷；不在公式中偷偷修正或夾限無效物理組合。
/// </summary>
public static class SpatialCapacityAnalysis
{
    public static SpatialCapacityResult Calculate(
        SpatialReferencePointDefinition point,
        SpatialCapacityGlobalParameters? global = null,
        SpatialCapacityTrainParameters? train = null,
        SpatialCapacityDirection direction = SpatialCapacityDirection.Forward)
    {
        ArgumentNullException.ThrowIfNull(point);
        global ??= new SpatialCapacityGlobalParameters();
        train ??= new SpatialCapacityTrainParameters();
        Validate(global, train);

        var safetyHeadway = point.Kind switch
        {
            SpatialReferencePointKind.IntermediateStation => CalculateStation(point, global, train, direction),
            SpatialReferencePointKind.BeforeStationTurnback => CalculateFrontTurnback(point, global, train),
            SpatialReferencePointKind.AfterStationTurnback => CalculateRearTurnback(point, global, train),
            SpatialReferencePointKind.CentralSidingTurnback => CalculatePocketTurnback(point, global, train),
            SpatialReferencePointKind.Junction => CalculateJunction(point, global, train),
            _ => throw new SimulationValidationException(["空間參考點型式不是有效的容量計算類型。"])
        };

        if (!double.IsFinite(safetyHeadway) || safetyHeadway <= 0)
        {
            throw new SimulationValidationException(["空間參考點計算得到無效安全時距；請檢查坡度、加減速度、速限與安全係數組合。"]);
        }

        var designHeadway = safetyHeadway * (1 + global.MarginRate);
        if (!double.IsFinite(designHeadway) || designHeadway <= 0)
        {
            throw new SimulationValidationException(["加入營運餘裕後的設計班距必須大於 0。"]);
        }

        var lineCapacity = (int)(3600 / designHeadway);
        var designCapacity = (int)(train.PassengerCapacity * lineCapacity);
        var achievableCapacity = (int)(designCapacity * global.TrainPeakHourFactor);
        return new SpatialCapacityResult(
            safetyHeadway,
            designHeadway,
            lineCapacity,
            designCapacity,
            achievableCapacity);
    }

    private static double CalculateStation(
        SpatialReferencePointDefinition point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train,
        SpatialCapacityDirection direction)
    {
        var reverse = direction == SpatialCapacityDirection.Reverse;
        var gradeIn = reverse ? point.StationReverseGradeInPermille : point.StationForwardGradeInPermille;
        var gradeOut = reverse ? point.StationReverseGradeOutPermille : point.StationForwardGradeOutPermille;
        var distanceToSignal = reverse
            ? point.StationReverseDistanceToSignalMeters
            : point.StationForwardDistanceToSignalMeters;
        var overlap = reverse ? point.StationReverseOverlapMeters : point.StationForwardOverlapMeters;
        var dwell = reverse ? point.StationReverseDwellSeconds : point.StationForwardDwellSeconds;
        var earlierSpeed = reverse
            ? point.StationReverseEarlierCruiseSpeedMetersPerSecond
            : point.StationForwardEarlierCruiseSpeedMetersPerSecond;
        var laterSpeed = reverse
            ? point.StationReverseLaterCruiseSpeedMetersPerSecond
            : point.StationForwardLaterCruiseSpeedMetersPerSecond;
        var safetyFactor = reverse ? point.StationReverseSafetyFactor : point.StationForwardSafetyFactor;
        var distance = distanceToSignal + overlap + train.LengthMeters;
        var acceleration = RequireEffective(train.EffectiveAcceleration(gradeIn), "中間站進站有效加速度");
        var deceleration = RequireEffective(train.EffectiveDeceleration(gradeOut), "中間站離站有效減速度");
        var approach = Math.Sqrt(2 * distance / acceleration);
        if (Math.Sqrt(2 * acceleration * distance) > earlierSpeed)
        {
            approach = distance / earlierSpeed + earlierSpeed / (2 * acceleration);
        }

        return dwell
            + approach
            + global.ReactionTimeSeconds
            + laterSpeed * (safetyFactor / train.BrakeEffect - 1) / (2 * deceleration)
            + laterSpeed / deceleration;
    }

    private static double CalculateFrontTurnback(
        SpatialReferencePointDefinition point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train)
    {
        var acceleration = RequireEffective(
            train.EffectiveAcceleration(-point.MainlineGradePermille),
            "站前折返有效加速度");
        var deceleration = RequireEffective(
            train.EffectiveDeceleration(point.MainlineGradePermille),
            "站前折返有效減速度");
        var distance = train.LengthMeters + point.DistanceFromStopToCrossoverMeters + point.CrossoverLengthMeters;
        var switchSpeed = point.SwitchSpeedLimitMetersPerSecond;
        var cruiseSpeed = point.MainlineApproachCruiseSpeedMetersPerSecond;
        var first = distance / switchSpeed + switchSpeed / (2 * acceleration);
        var second = cruiseSpeed * (point.MainlineSafetyFactor / train.BrakeEffect - 1) / (2 * deceleration)
            + distance / cruiseSpeed
            + cruiseSpeed / deceleration;
        var baseline = first + global.InterchangeTimeSeconds + second + point.TurnbackDwellSeconds;
        if (!point.AlternateBerthing)
        {
            return baseline;
        }

        var clearance = Math.Sqrt(2 * point.DistanceFromStopToCrossoverMeters / deceleration);
        var threshold = 2 * (point.TurnbackDwellSeconds + clearance - global.InterchangeTimeSeconds);
        return baseline >= threshold
            ? first + 2 * global.InterchangeTimeSeconds + second - clearance
            : baseline / 2;
    }

    private static double CalculateRearTurnback(
        SpatialReferencePointDefinition point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train)
    {
        var distance = point.DistanceFromStopToCrossoverMeters
            + point.CrossoverLengthMeters
            + point.DistanceFromCrossoverToTurnbackStopMeters
            + train.LengthMeters;
        var firstAcceleration = RequireEffective(
            train.EffectiveAcceleration(-point.MainlineGradePermille),
            "站後折返第一方向有效加速度");
        var firstDeceleration = RequireEffective(
            train.EffectiveDeceleration(-point.MainlineGradePermille),
            "站後折返第一方向有效減速度");
        var switchSpeed = point.SwitchSpeedLimitMetersPerSecond;
        var first = distance / switchSpeed
            + switchSpeed / (2 * firstAcceleration)
            + switchSpeed / (2 * firstDeceleration)
            - Math.Sqrt(2 * point.DistanceFromStopToCrossoverMeters / firstDeceleration);

        var secondAcceleration = RequireEffective(
            train.EffectiveAcceleration(point.MainlineGradePermille),
            "站後折返返回方向有效加速度");
        var secondDeceleration = RequireEffective(
            train.EffectiveDeceleration(point.MainlineGradePermille),
            "站後折返返回方向有效減速度");
        var total = secondAcceleration + secondDeceleration;
        var second = Math.Sqrt(2 * secondDeceleration * distance / (secondAcceleration * total))
            + Math.Sqrt(2 * secondAcceleration * distance / (secondDeceleration * total));
        var baseline = first + global.InterchangeTimeSeconds + second + point.TurnbackDwellSeconds;
        if (!point.AlternateBerthing)
        {
            return baseline;
        }

        var clearance = Math.Sqrt(2 * point.DistanceFromCrossoverToTurnbackStopMeters / secondDeceleration);
        var threshold = 2 * (point.TurnbackDwellSeconds + clearance - global.InterchangeTimeSeconds);
        return baseline >= threshold
            ? first + 2 * global.InterchangeTimeSeconds + second - clearance
            : baseline / 2;
    }

    private static double CalculatePocketTurnback(
        SpatialReferencePointDefinition point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train)
    {
        var distance = point.DistanceFromStopToCrossoverMeters
            + point.CrossoverLengthMeters
            + point.DistanceFromCrossoverToTurnbackStopMeters
            + train.LengthMeters;
        var firstAcceleration = RequireEffective(
            train.EffectiveAcceleration(-point.MainlineGradePermille),
            "中央避車線第一方向有效加速度");
        var firstDeceleration = RequireEffective(
            train.EffectiveDeceleration(-point.MainlineGradePermille),
            "中央避車線第一方向有效減速度");
        var secondAcceleration = RequireEffective(
            train.EffectiveAcceleration(point.MainlineGradePermille),
            "中央避車線返回方向有效加速度");
        var secondDeceleration = RequireEffective(
            train.EffectiveDeceleration(point.MainlineGradePermille),
            "中央避車線返回方向有效減速度");
        var speed = point.SwitchSpeedLimitMetersPerSecond;
        var first = distance / speed
            + speed / (2 * firstAcceleration)
            + speed / (2 * firstDeceleration)
            - Math.Sqrt(2 * point.DistanceFromStopToCrossoverMeters / firstDeceleration);
        var second = distance / speed + speed / (2 * secondAcceleration) + speed / (2 * secondDeceleration);
        return first + global.InterchangeTimeSeconds + second + point.TurnbackDwellSeconds;
    }

    private static double CalculateJunction(
        SpatialReferencePointDefinition point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train)
    {
        var mainDeceleration = RequireEffective(
            train.EffectiveDeceleration(point.MainlineGradePermille),
            "銜接點主線有效減速度");
        var sideDeceleration = RequireEffective(
            train.EffectiveDeceleration(point.BranchlineGradePermille),
            "銜接點側線有效減速度");
        var mainSpeed = point.MainlineApproachCruiseSpeedMetersPerSecond;
        var sideSpeed = point.BranchlineApproachCruiseSpeedMetersPerSecond;
        var switchSpeed = point.SwitchSpeedLimitMetersPerSecond;
        var side = sideSpeed * (point.BranchlineSafetyFactor / train.BrakeEffect - 1) / (2 * sideDeceleration)
            + switchSpeed * switchSpeed / (2 * sideDeceleration * sideSpeed)
            + (sideSpeed - switchSpeed) / sideDeceleration
            + (train.LengthMeters + point.CrossoverLengthMeters) / switchSpeed;
        // URCS 原式刻意不對稱：主線安全係數項沒有「-1」。
        var main = point.MainlineSafetyFactor * mainSpeed / (2 * train.BrakeEffect * mainDeceleration)
            + (train.LengthMeters + point.CrossoverLengthMeters) / mainSpeed;
        return main + (1 - point.MainlineTrafficRatio) * (side + 2 * global.InterchangeTimeSeconds);
    }

    private static double RequireEffective(double value, string field)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new SimulationValidationException([$"{field}必須大於 0；請調整坡度或列車加減速度。"]);
        }

        return value;
    }

    private static void Validate(
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train)
    {
        var errors = new List<string>();
        Range(global.InterchangeTimeSeconds, 0, 60, "號誌／列車控制轉換時間", errors);
        Range(global.ReactionTimeSeconds, 0, 10, "反應時間", errors);
        Range(global.TrainPeakHourFactor, 0.25, 1, "列車尖峰小時因子", errors);
        Range(global.MarginRate, 0, 1, "營運餘裕率", errors);
        Range(train.LengthMeters, 10, 300, "列車長度", errors);
        Range(train.AccelerationMetersPerSecondSquared, 0.1, 3, "加速度", errors);
        Range(train.DecelerationMetersPerSecondSquared, 0.1, 3, "減速度", errors);
        Range(train.BrakeEffect, 0.01, 1, "煞車有效因子", errors);
        Range(train.SeatCount, 0, 600, "座位數", errors);
        Range(train.StandingAreaSquareMeters, 0, 900, "站立面積", errors);
        Range(train.StandingDensityPerSquareMeter, 0, 10, "站立密度", errors);
        PlatformDefinition.Throw(errors);
    }

    private static void Range(double value, double minimum, double maximum, string field, ICollection<string> errors)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            errors.Add($"{field}必須介於 {minimum:0.##} 至 {maximum:0.##}。");
        }
    }
}
