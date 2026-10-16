namespace MrtRouteSimulator.Engine;

/// <summary>容量敏感度分析允許修改的單一參數。</summary>
public enum SpatialCapacitySensitivityParameter
{
    MainlineTrafficRatio,
    TurnbackDwellSeconds
}

/// <summary>同一手動分析情境中，指定方向的容量瓶頸。</summary>
public sealed record SpatialCapacityBottleneckResult(
    SpatialCapacityDirection Direction,
    string ReferencePointId,
    string Name,
    SpatialCapacityResult Result);

/// <summary>指定參考點與參數值的容量敏感度結果。</summary>
public sealed record SpatialCapacitySensitivityResult(
    double Value,
    SpatialCapacityResult Result);

/// <summary>
/// 只對明確標記為單一分析情境的手動容量參考點做局部瓶頸與敏感度分析。
/// 結果不會建立或修改 topology runtime，也不會自動把整條路線的點聚合為容量上限。
/// </summary>
public static class SpatialCapacityScenarioAnalysis
{
    public static IReadOnlyList<SpatialCapacityBottleneckResult> Calculate(
        SpatialCapacityProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.CompareAsSingleScenario)
        {
            throw new SimulationValidationException([
                "容量參考點預設分別計算；請先確認這些參考點屬同一分析情境，再啟用瓶頸比較。"]);
        }

        SpatialCapacityProjectAnalysis.Validate(settings);
        var points = settings.Points
            .Select(point => (Dto: point, Definition: SpatialCapacityProjectAnalysis.ToDefinition(point)))
            .ToArray();
        var results = new List<SpatialCapacityBottleneckResult>();
        foreach (var direction in Enum.GetValues<SpatialCapacityDirection>())
        {
            var candidates = points.Select(item =>
            {
                var result = SpatialCapacityAnalysis.Calculate(
                    item.Definition,
                    settings.Global,
                    settings.Train,
                    direction,
                    settings.Mode);
                return new SpatialCapacityBottleneckResult(
                    direction,
                    item.Dto.ReferencePointId,
                    string.IsNullOrWhiteSpace(item.Dto.Name) ? item.Dto.ReferencePointId : item.Dto.Name,
                    result);
            });
            var bottleneck = candidates
                .OrderByDescending(item => item.Result.DesignHeadwaySeconds)
                .ThenBy(item => item.ReferencePointId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (bottleneck is not null)
                results.Add(bottleneck);
        }

        return results;
    }

    public static IReadOnlyList<SpatialCapacitySensitivityResult> AnalyzeSensitivity(
        SpatialCapacityProjectSettings settings,
        string pointId,
        SpatialCapacitySensitivityParameter parameter,
        double start,
        double end,
        double step,
        SpatialCapacityDirection direction = SpatialCapacityDirection.Forward)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SpatialCapacityProjectAnalysis.Validate(settings);
        if (!Enum.IsDefined(parameter))
            throw new SimulationValidationException(["容量敏感度參數不是有效的設定。"]);
        if (!Enum.IsDefined(direction))
            throw new SimulationValidationException(["容量敏感度分析方向不是有效的設定。"]);
        if (string.IsNullOrWhiteSpace(pointId))
            throw new SimulationValidationException(["容量敏感度分析必須指定參考點 ID。"]);
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(step))
            throw new SimulationValidationException(["容量敏感度分析的起點、終點與步長都必須是有限數值。"]);
        if (step <= 0)
            throw new SimulationValidationException(["容量敏感度分析步長必須大於 0。"]);
        if (end < start)
            throw new SimulationValidationException(["容量敏感度分析終點必須大於或等於起點。"]);

        var point = settings.Points.SingleOrDefault(item =>
            item.ReferencePointId.Equals(pointId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
            throw new SimulationValidationException([$"找不到容量空間參考點「{pointId}」。"]);

        ValidateParameterKind(point, parameter);
        ValidateParameterRange(parameter, start, end);
        var values = CreateValues(start, end, step);
        var results = new List<SpatialCapacitySensitivityResult>(values.Count);
        foreach (var value in values)
        {
            var adjusted = parameter switch
            {
                SpatialCapacitySensitivityParameter.MainlineTrafficRatio => point with { MainlineTrafficRatio = value },
                SpatialCapacitySensitivityParameter.TurnbackDwellSeconds => point with { TurnbackDwellSeconds = value },
                _ => throw new SimulationValidationException(["容量敏感度參數不是有效的設定。"])
            };
            var definition = SpatialCapacityProjectAnalysis.ToDefinition(adjusted);
            var result = SpatialCapacityAnalysis.Calculate(
                definition,
                settings.Global,
                settings.Train,
                direction,
                settings.Mode);
            results.Add(new SpatialCapacitySensitivityResult(value, result));
        }

        return results;
    }

    private static void ValidateParameterKind(
        ProjectSpatialReferencePoint point,
        SpatialCapacitySensitivityParameter parameter)
    {
        var valid = parameter switch
        {
            SpatialCapacitySensitivityParameter.MainlineTrafficRatio =>
                point.Kind == SpatialReferencePointKind.Junction,
            SpatialCapacitySensitivityParameter.TurnbackDwellSeconds => point.Kind is
                SpatialReferencePointKind.BeforeStationTurnback
                or SpatialReferencePointKind.AfterStationTurnback
                or SpatialReferencePointKind.CentralSidingTurnback,
            _ => false
        };
        if (!valid)
        {
            throw new SimulationValidationException([
                $"敏感度參數「{parameter}」不適用於參考點「{point.ReferencePointId}」的型式「{point.Kind}」。"]);
        }
    }

    private static void ValidateParameterRange(
        SpatialCapacitySensitivityParameter parameter,
        double start,
        double end)
    {
        var (minimum, maximum, name) = parameter switch
        {
            SpatialCapacitySensitivityParameter.MainlineTrafficRatio => (0d, 1d, "主線列車連續通過比例"),
            SpatialCapacitySensitivityParameter.TurnbackDwellSeconds => (0d, 1800d, "折返停等時間"),
            _ => throw new SimulationValidationException(["容量敏感度參數不是有效的設定。"])
        };
        if (start < minimum || start > maximum || end < minimum || end > maximum)
        {
            throw new SimulationValidationException([
                $"敏感度參數「{name}」必須介於 {minimum:0.##} 至 {maximum:0.##}。"]);
        }
    }

    private static List<double> CreateValues(double start, double end, double step)
    {
        var quotient = (end - start) / step;
        if (!double.IsFinite(quotient) || quotient > 201)
        {
            throw new SimulationValidationException(["容量敏感度分析最多只能產生 201 個參數值。"]);
        }
        var nearestInteger = Math.Round(quotient);
        var isIntegral = Math.Abs(quotient - nearestInteger)
            <= 1e-10 * Math.Max(1, Math.Abs(quotient));
        var count = (long)Math.Floor(quotient + (isIntegral ? 1e-10 : 0)) + 1;
        if (count > 201)
        {
            throw new SimulationValidationException(["容量敏感度分析最多只能產生 201 個參數值。"]);
        }

        var values = new List<double>((int)count);
        for (var index = 0L; index < count; index++)
        {
            var value = start + index * step;
            if (value > end && !isIntegral)
                break;
            if (isIntegral && index == count - 1)
                value = end;
            values.Add(value);
        }
        return values;
    }
}
