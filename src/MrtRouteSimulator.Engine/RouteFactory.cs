namespace MrtRouteSimulator.Engine;

public sealed record StationInput(
    string StationId,
    string StationName,
    double DistanceFromPreviousMeters,
    double? DwellTimeSeconds = null);

public static class RouteFactory
{
    public static Route FromSegmentDistances(
        string routeId,
        string routeName,
        IEnumerable<StationInput> stationInputs,
        double defaultDwellTimeSeconds)
    {
        var inputs = stationInputs?.ToArray() ?? [];
        var errors = new List<string>();
        RouteValidator.RequireNonNegativeFinite(defaultDwellTimeSeconds, "預設停站時間", errors);

        var stations = new List<Station>(inputs.Length);
        var cumulativePosition = 0d;

        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            if (!RouteValidator.IsFinite(input.DistanceFromPreviousMeters))
            {
                errors.Add($"第 {index + 1} 站的站間距離必須是有限數值。");
                continue;
            }

            if (index == 0 && Math.Abs(input.DistanceFromPreviousMeters) > 1e-9)
            {
                errors.Add("第一站的『與前站距離』必須為 0。");
            }
            else if (index > 0 && input.DistanceFromPreviousMeters <= 0)
            {
                errors.Add($"第 {index + 1} 站與前站距離必須大於 0。");
            }

            if (index > 0)
            {
                cumulativePosition += input.DistanceFromPreviousMeters;
            }

            var dwell = input.DwellTimeSeconds ?? defaultDwellTimeSeconds;
            stations.Add(new Station(
                input.StationId.Trim(),
                input.StationName.Trim(),
                cumulativePosition,
                dwell));
        }

        RouteValidator.ThrowIfAny(errors);
        return new Route(routeId, routeName, stations);
    }
}
