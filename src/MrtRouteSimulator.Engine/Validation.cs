namespace MrtRouteSimulator.Engine;

public sealed class SimulationValidationException : ArgumentException
{
    public SimulationValidationException(IEnumerable<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<string> Errors { get; }
}

internal static class RouteValidator
{
    public static void Validate(Route route)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(route.RouteId))
        {
            errors.Add("路線編號不可空白。");
        }

        if (string.IsNullOrWhiteSpace(route.RouteName))
        {
            errors.Add("路線名稱不可空白。");
        }

        if (route.Stations.Count < 2)
        {
            errors.Add("路線至少需要 2 個車站。");
        }

        if (route.Stations.Count > 0 && !NearlyEqual(route.Stations[0].PositionMeters, 0))
        {
            errors.Add("第一站的累積里程必須為 0 公尺。");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < route.Stations.Count; index++)
        {
            var station = route.Stations[index];
            var label = $"第 {index + 1} 站";

            if (string.IsNullOrWhiteSpace(station.StationId))
            {
                errors.Add($"{label}的車站編號不可空白。");
            }
            else if (!ids.Add(station.StationId.Trim()))
            {
                errors.Add($"車站編號「{station.StationId}」重複。");
            }

            if (string.IsNullOrWhiteSpace(station.StationName))
            {
                errors.Add($"{label}的車站名稱不可空白。");
            }

            if (!IsFinite(station.PositionMeters) || station.PositionMeters < 0)
            {
                errors.Add($"{label}的累積里程必須是有限的非負數。");
            }

            if (!IsFinite(station.DwellTimeSeconds) || station.DwellTimeSeconds < 0)
            {
                errors.Add($"{label}的停站時間必須是有限的非負數。");
            }

            if (index > 0 && station.PositionMeters <= route.Stations[index - 1].PositionMeters)
            {
                errors.Add($"{label}的位置必須大於前一站；站間距離必須為正數。");
            }
        }

        ThrowIfAny(errors);
    }

    public static void Validate(TrainParameters parameters)
    {
        var errors = new List<string>();
        RequirePositiveFinite(parameters.MaxSpeedMetersPerSecond, "最高速度", errors);
        RequirePositiveFinite(parameters.AccelerationMetersPerSecondSquared, "加速度", errors);
        RequirePositiveFinite(parameters.DecelerationMetersPerSecondSquared, "減速度", errors);
        RequireNonNegativeFinite(parameters.DefaultDwellTimeSeconds, "預設停站時間", errors);
        RequireNonNegativeFinite(parameters.OriginTurnaroundTimeSeconds, "起點折返時間", errors);
        RequireNonNegativeFinite(parameters.TerminalTurnaroundTimeSeconds, "終點折返時間", errors);
        ThrowIfAny(errors);
    }

    public static void RequirePositiveFinite(double value, string name, ICollection<string> errors)
    {
        if (!IsFinite(value) || value <= 0)
        {
            errors.Add($"{name}必須是有限且大於 0 的數值。");
        }
    }

    public static void RequireNonNegativeFinite(double value, string name, ICollection<string> errors)
    {
        if (!IsFinite(value) || value < 0)
        {
            errors.Add($"{name}必須是有限的非負數。");
        }
    }

    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    public static void ThrowIfAny(IReadOnlyCollection<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new SimulationValidationException(errors);
        }
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 1e-9;
}
