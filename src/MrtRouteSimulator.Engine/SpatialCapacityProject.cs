namespace MrtRouteSimulator.Engine;

/// <summary>
/// Schema 8 可選的空間容量分析設定。容量設定不參與 topology runtime，Points 只保存手動分析參考點。
/// </summary>
public sealed record SpatialCapacityProjectSettings(
    SpatialCapacityGlobalParameters Global,
    SpatialCapacityTrainParameters Train,
    ProjectSpatialReferencePoint[] Points,
    SpatialCapacityCalculationMode Mode = SpatialCapacityCalculationMode.TaiwanHandbook,
    bool CompareAsSingleScenario = false);

/// <summary>將容量分析專案設定轉為 Engine 定義並執行完整驗證的公共入口。</summary>
public static class SpatialCapacityProjectAnalysis
{
    public static SpatialReferencePointDefinition ToDefinition(ProjectSpatialReferencePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return new SpatialReferencePointDefinition(
            point.ReferencePointId,
            point.StationId,
            point.Name,
            point.Kind,
            point.AlternateBerthing,
            point.MainlineGradePermille,
            point.BranchlineGradePermille,
            point.DistanceFromStopToCrossoverMeters,
            point.CrossoverLengthMeters,
            point.DistanceFromCrossoverToTurnbackStopMeters,
            point.TurnbackDwellSeconds,
            point.SwitchSpeedLimitMetersPerSecond,
            point.MainlineApproachCruiseSpeedMetersPerSecond,
            point.BranchlineApproachCruiseSpeedMetersPerSecond,
            point.MainlineSafetyFactor,
            point.BranchlineSafetyFactor,
            point.MainlineTrafficRatio,
            point.StationForwardGradeInPermille,
            point.StationForwardGradeOutPermille,
            point.StationForwardDistanceToSignalMeters,
            point.StationForwardOverlapMeters,
            point.StationForwardDwellSeconds,
            point.StationForwardEarlierCruiseSpeedMetersPerSecond,
            point.StationForwardLaterCruiseSpeedMetersPerSecond,
            point.StationForwardSafetyFactor,
            point.StationReverseGradeInPermille,
            point.StationReverseGradeOutPermille,
            point.StationReverseDistanceToSignalMeters,
            point.StationReverseOverlapMeters,
            point.StationReverseDwellSeconds,
            point.StationReverseEarlierCruiseSpeedMetersPerSecond,
            point.StationReverseLaterCruiseSpeedMetersPerSecond,
            point.StationReverseSafetyFactor);
    }

    public static void Validate(SpatialCapacityProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<string>();
        if (!Enum.IsDefined(settings.Mode))
            errors.Add("容量計算模式不是有效的設定。");

        if (settings.Global is null)
            errors.Add("容量設定缺少全域參數。");
        if (settings.Train is null)
            errors.Add("容量設定缺少列車參數。");
        if (settings.Points is null)
            errors.Add("容量設定缺少空間參考點集合。");

        if (settings.Global is not null && settings.Train is not null)
        {
            try
            {
                SpatialCapacityAnalysis.Validate(settings.Global, settings.Train);
            }
            catch (SimulationValidationException exception)
            {
                errors.AddRange(exception.Errors);
            }
        }

        if (settings.Points is not null)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var projectPoint in settings.Points)
            {
                if (projectPoint is null)
                {
                    errors.Add("容量空間參考點不可為 null。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(projectPoint.ReferencePointId))
                    errors.Add("容量空間參考點編號不可空白。");
                else if (!ids.Add(projectPoint.ReferencePointId))
                    errors.Add($"容量空間參考點編號「{projectPoint.ReferencePointId}」重複。");

                SpatialReferencePointDefinition definition;
                try
                {
                    definition = ToDefinition(projectPoint);
                }
                catch (SimulationValidationException exception)
                {
                    errors.AddRange(exception.Errors);
                    continue;
                }

                if (settings.Global is null || settings.Train is null || !Enum.IsDefined(settings.Mode))
                    continue;

                foreach (var direction in Enum.GetValues<SpatialCapacityDirection>())
                {
                    try
                    {
                        _ = SpatialCapacityAnalysis.Calculate(
                            definition,
                            settings.Global,
                            settings.Train,
                            direction,
                            settings.Mode);
                    }
                    catch (SimulationValidationException exception)
                    {
                        errors.AddRange(exception.Errors.Select(error =>
                            $"容量空間參考點「{projectPoint.ReferencePointId}」{direction}：{error}"));
                    }
                }
            }
        }

        PlatformDefinition.Throw(errors);
    }
}
