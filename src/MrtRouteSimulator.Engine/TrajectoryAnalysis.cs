using System.Globalization;
using System.Text;

namespace MrtRouteSimulator.Engine;

public static class TrajectoryAnalysis
{
    public static IReadOnlyList<TrajectorySample> DecimatePreservingCriticalPoints(
        IReadOnlyList<TrajectorySample> samples,
        int maximumPointsPerRun)
    {
        if (maximumPointsPerRun < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPointsPerRun), "每一車次至少保留 2 個取樣點。");
        }

        var result = new List<TrajectorySample>();
        foreach (var group in samples.GroupBy(sample => (sample.VehicleId, sample.ServiceRunId)))
        {
            var ordered = group.OrderBy(sample => sample.SimulationTimeSeconds).ToArray();
            if (ordered.Length <= maximumPointsPerRun)
            {
                result.AddRange(ordered);
                continue;
            }

            var required = new SortedSet<int> { 0, ordered.Length - 1 };
            for (var index = 1; index < ordered.Length - 1; index++)
            {
                if (ordered[index].Phase != ordered[index - 1].Phase
                    || ordered[index].Direction != ordered[index - 1].Direction
                    || IsLocalExtremum(ordered[index - 1].PositionMeters, ordered[index].PositionMeters, ordered[index + 1].PositionMeters)
                    || IsLocalExtremum(ordered[index - 1].SpeedMetersPerSecond, ordered[index].SpeedMetersPerSecond, ordered[index + 1].SpeedMetersPerSecond))
                {
                    required.Add(index);
                }
            }

            var remaining = Math.Max(0, maximumPointsPerRun - required.Count);
            if (remaining > 0)
            {
                var stride = Math.Max(1, (ordered.Length - 2) / remaining);
                for (var index = 1; index < ordered.Length - 1 && required.Count < maximumPointsPerRun; index += stride)
                {
                    required.Add(index);
                }
            }

            result.AddRange(required.Select(index => ordered[index]));
        }

        return result.OrderBy(sample => sample.SimulationTimeSeconds).ToArray();
    }

    public static string BuildCsv(
        Route route,
        IEnumerable<TrajectorySample> samples,
        IEnumerable<SimulationEvent> events,
        double displayClockStartSeconds)
    {
        ArgumentNullException.ThrowIfNull(route);
        var eventLookup = events
            .GroupBy(item => (item.VehicleId, RoundedTime: Math.Round(item.SimulationTimeSeconds, 1)))
            .ToDictionary(group => group.Key, group => string.Join('|', group.Select(item => item.EventType)));
        var builder = new StringBuilder();
        builder.AppendLine("vehicle_id,service_run_id,service_class_id,service_pattern_id,simulation_time_s,display_time,position_km,speed_kmh,direction,track_id,state,previous_station,next_station,event_type");

        foreach (var sample in samples.OrderBy(item => item.SimulationTimeSeconds).ThenBy(item => item.VehicleId, StringComparer.Ordinal))
        {
            eventLookup.TryGetValue(
                (sample.VehicleId, Math.Round(sample.SimulationTimeSeconds, 1)),
                out var eventType);
            builder.Append(Csv(sample.VehicleId)).Append(',')
                .Append(Csv(sample.ServiceRunId)).Append(',')
                .Append(Csv(sample.ServiceClassId)).Append(',')
                .Append(Csv(sample.ServicePatternId)).Append(',')
                .Append(sample.SimulationTimeSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(FormatClock(displayClockStartSeconds + sample.SimulationTimeSeconds))).Append(',')
                .Append((sample.PositionMeters / 1000).ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append((sample.SpeedMetersPerSecond * 3.6).ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(sample.Direction).Append(',')
                .Append(Csv(sample.TrackId)).Append(',')
                .Append(sample.Phase).Append(',')
                .Append(Csv(sample.CurrentStationId)).Append(',')
                .Append(Csv(sample.NextStationId ?? string.Empty)).Append(',')
                .Append(Csv(eventType ?? string.Empty))
                .AppendLine();
        }

        return builder.ToString();
    }

    public static string FormatClock(double totalSeconds)
    {
        var tenths = (long)Math.Round(totalSeconds * 10, MidpointRounding.AwayFromZero);
        var days = tenths / 864000;
        var remainder = tenths % 864000;
        var hours = remainder / 36000;
        var minutes = remainder % 36000 / 600;
        var seconds = remainder % 600 / 10;
        var decimalPart = remainder % 10;
        return days > 0
            ? $"+{days}日 {hours:00}:{minutes:00}:{seconds:00}.{decimalPart}"
            : $"{hours:00}:{minutes:00}:{seconds:00}.{decimalPart}";
    }

    private static bool IsLocalExtremum(double previous, double current, double next) =>
        current > previous && current >= next || current < previous && current <= next;

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
