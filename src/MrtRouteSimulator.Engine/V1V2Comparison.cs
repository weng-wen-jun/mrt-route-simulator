using System.Globalization;

namespace MrtRouteSimulator.Engine;

/// <summary>同一派車條件下的 V1 解析理論與 V2 寫實事件逐站比較列。</summary>
public sealed record V1V2StationComparison(
    string VehicleId,
    string ServiceRunId,
    TrainDirection Direction,
    string VehicleTypeId,
    string StopPatternId,
    string StationId,
    string StationName,
    double PositionMeters,
    double? TheoreticalArrivalTimeSeconds,
    double? TheoreticalDepartureTimeSeconds,
    double? TheoreticalDwellSeconds,
    double? ActualArrivalTimeSeconds,
    double? ActualDepartureTimeSeconds,
    double? ActualDwellSeconds,
    double? ArrivalDifferenceSeconds,
    double? DepartureDifferenceSeconds,
    double? DepartureDifferencePercent,
    string Status);

public sealed record V1V2ComparisonResult(IReadOnlyList<V1V2StationComparison> Stations)
{
    public string BuildCsv(double displayClockStartSeconds = 0) => V1V2Comparison.BuildCsv(this, displayClockStartSeconds);
}

/// <summary>
/// V1 理論以解析式三角／梯形曲線重算每一段「實際有停靠」的連續距離；跨站站點
/// 不會被錯當作一次煞停。V2 實際一律從同一個 SimulationWorld 的結構化事件讀取。
/// </summary>
public static class V1V2Comparison
{
    private const double PositionToleranceMeters = 0.6;

    public static V1V2ComparisonResult Analyze(
        Route route,
        ResolvedDispatchPlan dispatchPlan,
        IEnumerable<VehicleTypeDefinition> vehicleTypes,
        IEnumerable<StopPatternDefinition> stopPatterns,
        TrainParameters fallbackParameters,
        IEnumerable<SimulationEvent>? actualEvents)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(dispatchPlan);
        ArgumentNullException.ThrowIfNull(fallbackParameters);
        var vehicles = ToDictionary(vehicleTypes, item => item.Id, "車型");
        var patterns = ToDictionary(stopPatterns, item => item.Id, "停站模式");
        var actual = actualEvents?.ToArray() ?? [];
        var rows = new List<V1V2StationComparison>();

        foreach (var run in dispatchPlan.Runs.OrderBy(item => item.Sequence))
        {
            if (!vehicles.TryGetValue(run.VehicleTypeId, out var vehicle))
            {
                throw new SimulationValidationException([$"V1/V2 比較找不到車型「{run.VehicleTypeId}」。"]);
            }
            if (!patterns.TryGetValue(run.StopPatternId, out var pattern))
            {
                throw new SimulationValidationException([$"V1/V2 比較找不到停站模式「{run.StopPatternId}」。"]);
            }

            var parameters = new TrainParameters(
                vehicle.MaxSpeedMetersPerSecond,
                vehicle.AccelerationMetersPerSecondSquared,
                vehicle.ServiceBrakeDecelerationMetersPerSecondSquared,
                fallbackParameters.DefaultDwellTimeSeconds,
                fallbackParameters.OriginTurnaroundTimeSeconds,
                fallbackParameters.TerminalTurnaroundTimeSeconds);
            var orderedStations = run.Direction == TrainDirection.Outbound
                ? route.Stations.ToArray()
                : route.Stations.Reverse().ToArray();
            var instructions = pattern.Instructions.ToDictionary(item => item.StationId, StringComparer.OrdinalIgnoreCase);
            var runActual = actual.Where(item => item.ServiceRunId.Equals(run.ServiceRunId, StringComparison.OrdinalIgnoreCase)
                && item.Direction == run.Direction
                && (string.IsNullOrWhiteSpace(run.VehicleId)
                    || item.VehicleId.Equals(run.VehicleId, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(item => item.SimulationTimeSeconds)
                .ToArray();
            var theoreticalTime = RelativeSeconds(run.PlannedDepartureTime, dispatchPlan.ScheduleAnchorTime);
            var previousStoppedStation = orderedStations[0];

            rows.Add(CreateRow(
                run,
                previousStoppedStation,
                theoreticalTime,
                theoreticalTime,
                0,
                Find(runActual, previousStoppedStation, SimulationEventType.Arrival),
                Find(runActual, previousStoppedStation, SimulationEventType.Departure),
                "可比較"));

            for (var index = 1; index < orderedStations.Length; index++)
            {
                var station = orderedStations[index];
                var instruction = instructions.GetValueOrDefault(station.StationId);
                var action = instruction?.Action ?? StopPatternAction.Stop;
                if (action == StopPatternAction.Pass)
                {
                    rows.Add(CreateRow(run, station, null, null, null,
                        Find(runActual, station, SimulationEventType.StationPassed), null, "跨站不比較"));
                    continue;
                }
                if (action == StopPatternAction.Turnback)
                {
                    rows.Add(CreateRow(run, station, null, null, null,
                        Find(runActual, station, SimulationEventType.Arrival), null, "折返節點不適用 V1"));
                    break;
                }

                var motion = AnalyticalModel.CalculateSegmentTravelTime(
                    Math.Abs(station.PositionMeters - previousStoppedStation.PositionMeters),
                    parameters.MaxSpeedMetersPerSecond,
                    parameters.AccelerationMetersPerSecondSquared,
                    parameters.DecelerationMetersPerSecondSquared);
                var theoreticalArrival = theoreticalTime + motion.TravelTimeSeconds;
                var isTerminal = index == orderedStations.Length - 1;
                var theoreticalDwell = isTerminal ? 0 : instruction?.DwellTimeSeconds ?? station.DwellTimeSeconds;
                var theoreticalDeparture = theoreticalArrival + theoreticalDwell;
                var actualArrival = Find(runActual, station, SimulationEventType.Arrival);
                var actualDeparture = FindAfter(runActual, station, actualArrival, SimulationEventType.Departure);
                rows.Add(CreateRow(run, station, theoreticalArrival, theoreticalDeparture, theoreticalDwell,
                    actualArrival, actualDeparture, actualArrival is null ? "V2 尚未抵達" : "可比較"));
                theoreticalTime = theoreticalDeparture;
                previousStoppedStation = station;
            }
        }

        return new V1V2ComparisonResult(rows);
    }

    public static string BuildCsv(V1V2ComparisonResult result, double displayClockStartSeconds = 0)
    {
        ArgumentNullException.ThrowIfNull(result);
        var lines = new List<string>
        {
            "車輛 ID,車次 ID,方向,車型 ID,停站模式,站號,車站,理論到站,理論出站,理論停站秒,V2 實際到站,V2 實際出站,V2 實際停站秒,到站差秒,出站差秒,出站差百分比,狀態"
        };
        lines.AddRange(result.Stations.Select(item => string.Join(',', new[]
        {
            Csv(item.VehicleId), Csv(item.ServiceRunId), Csv(item.Direction == TrainDirection.Outbound ? "下行" : "上行"),
            Csv(item.VehicleTypeId), Csv(item.StopPatternId), Csv(item.StationId), Csv(item.StationName),
            Csv(Clock(item.TheoreticalArrivalTimeSeconds, displayClockStartSeconds)),
            Csv(Clock(item.TheoreticalDepartureTimeSeconds, displayClockStartSeconds)),
            Csv(Number(item.TheoreticalDwellSeconds)), Csv(Clock(item.ActualArrivalTimeSeconds, displayClockStartSeconds)),
            Csv(Clock(item.ActualDepartureTimeSeconds, displayClockStartSeconds)), Csv(Number(item.ActualDwellSeconds)),
            Csv(Number(item.ArrivalDifferenceSeconds)), Csv(Number(item.DepartureDifferenceSeconds)),
            Csv(Number(item.DepartureDifferencePercent)), Csv(item.Status)
        })));
        return string.Join(Environment.NewLine, lines);
    }

    private static V1V2StationComparison CreateRow(
        PlannedServiceRun run,
        Station station,
        double? theoreticalArrival,
        double? theoreticalDeparture,
        double? theoreticalDwell,
        double? actualArrival,
        double? actualDeparture,
        string status)
    {
        var actualDwell = actualArrival is { } arrival && actualDeparture is { } departure
            ? (double?)Math.Max(0, departure - arrival)
            : null;
        var arrivalDifference = theoreticalArrival is { } theoreticalArrivalValue && actualArrival is { } actualArrivalValue
            ? (double?)(actualArrivalValue - theoreticalArrivalValue)
            : null;
        var departureDifference = theoreticalDeparture is { } theoreticalDepartureValue && actualDeparture is { } actualDepartureValue
            ? (double?)(actualDepartureValue - theoreticalDepartureValue)
            : null;
        var departurePercent = theoreticalDeparture is { } theoretical && theoretical > 1e-7 && departureDifference is { } difference
            ? (double?)(difference / theoretical * 100)
            : null;
        return new V1V2StationComparison(
            run.VehicleId ?? string.Empty, run.ServiceRunId, run.Direction, run.VehicleTypeId, run.StopPatternId,
            station.StationId, station.StationName, station.PositionMeters,
            theoreticalArrival, theoreticalDeparture, theoreticalDwell,
            actualArrival, actualDeparture, actualDwell,
            arrivalDifference, departureDifference, departurePercent, status);
    }

    private static double? Find(IEnumerable<SimulationEvent> events, Station station, params SimulationEventType[] types) =>
        events.Where(item => types.Contains(item.EventType)
                && Math.Abs(item.PositionMeters - station.PositionMeters) <= PositionToleranceMeters)
            .Select(item => (double?)item.SimulationTimeSeconds)
            .FirstOrDefault();

    private static double? FindAfter(
        IEnumerable<SimulationEvent> events,
        Station station,
        double? after,
        params SimulationEventType[] types) =>
        events.Where(item => types.Contains(item.EventType)
                && Math.Abs(item.PositionMeters - station.PositionMeters) <= PositionToleranceMeters
                && (after is null || item.SimulationTimeSeconds + 1e-7 >= after.Value))
            .Select(item => (double?)item.SimulationTimeSeconds)
            .FirstOrDefault();

    private static Dictionary<string, T> ToDictionary<T>(IEnumerable<T> values, Func<T, string> key, string label) where T : class
    {
        var result = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? [])
        {
            if (!result.TryAdd(key(value), value))
            {
                throw new SimulationValidationException([$"V1/V2 比較的{label} ID 重複：{key(value)}。"]);
            }
        }
        return result;
    }

    private static double RelativeSeconds(TimeSpan value, TimeSpan anchor)
    {
        var result = (value - anchor).TotalSeconds;
        while (result < 0) result += TimeSpan.FromDays(1).TotalSeconds;
        return result;
    }

    private static string Clock(double? value, double start) => value is { } seconds
        ? TimeSpan.FromSeconds(start + seconds).ToString(@"hh\:mm\:ss\.f", CultureInfo.InvariantCulture)
        : string.Empty;

    private static string Number(double? value) => value is { } number
        ? number.ToString("0.###", CultureInfo.InvariantCulture)
        : string.Empty;

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
}
