namespace MrtRouteSimulator.Engine;

/// <summary>
/// Selects the station event used by observed throughput analysis.
/// Arrival is intentionally an explicit choice because terminal-arrival throughput
/// and station-departure throughput answer different operational questions.
/// </summary>
public enum CapacityObservationEventKind
{
    Departure,
    Arrival
}

/// <summary>
/// Event-derived throughput metrics for one station, direction, and half-open
/// observation window. These metrics describe observed train events only; they
/// are not passenger capacity and do not apply a passenger-load factor.
/// </summary>
public sealed record CapacityObservationResult(
    string StationId,
    TrainDirection Direction,
    CapacityObservationEventKind EventKind,
    double WindowStartSeconds,
    double WindowEndSeconds,
    int Count,
    double CountPerHour,
    double? SpanRatePerHour,
    double? MedianHeadwaySeconds,
    double? FirstEventTimeSeconds,
    double? LastEventTimeSeconds,
    int IgnoredMissingServiceRunIdCount)
{
    public double WindowDurationSeconds => WindowEndSeconds - WindowStartSeconds;

    public bool HasObservations => Count > 0;
}

/// <summary>
/// Aggregates station events from an existing SimulationWorld event stream.
/// It does not simulate, inspect route geometry, or infer station identity from
/// positions. Events without a ServiceRunId are ignored conservatively so that
/// an unknown run cannot inflate an observed train count.
/// </summary>
public static class CapacityObservationAnalysis
{
    public static CapacityObservationResult Analyze(
        IEnumerable<SimulationEvent> events,
        string stationId,
        TrainDirection direction,
        double windowStartSeconds,
        double windowEndSeconds,
        CapacityObservationEventKind eventKind)
    {
        ArgumentNullException.ThrowIfNull(events);
        ValidateWindow(windowStartSeconds, windowEndSeconds);
        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new ArgumentException("容量觀測分析需要非空車站 ID。", nameof(stationId));
        }
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!Enum.IsDefined(eventKind))
        {
            throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, "容量觀測事件型別無效。");
        }

        var selectedEventType = eventKind switch
        {
            CapacityObservationEventKind.Departure => SimulationEventType.Departure,
            CapacityObservationEventKind.Arrival => SimulationEventType.Arrival,
            _ => throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, "容量觀測事件型別無效.")
        };

        var candidates = new List<SimulationEvent>();
        var ignoredMissingServiceRunIdCount = 0;
        foreach (var item in events)
        {
            if (item.EventType != selectedEventType
                || item.Direction != direction
                || !string.Equals(item.StationId, stationId, StringComparison.OrdinalIgnoreCase)
                || !double.IsFinite(item.SimulationTimeSeconds)
                || item.SimulationTimeSeconds < windowStartSeconds
                || item.SimulationTimeSeconds >= windowEndSeconds)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.ServiceRunId))
            {
                ignoredMissingServiceRunIdCount++;
                continue;
            }

            candidates.Add(item);
        }

        var observedEvents = candidates
            .GroupBy(item => item.ServiceRunId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.SimulationTimeSeconds)
                .ThenBy(item => item.VehicleId, StringComparer.Ordinal)
                .First())
            .OrderBy(item => item.SimulationTimeSeconds)
            .ThenBy(item => item.ServiceRunId, StringComparer.Ordinal)
            .ToArray();

        var times = observedEvents.Select(item => item.SimulationTimeSeconds).ToArray();
        var count = times.Length;
        var windowDurationSeconds = windowEndSeconds - windowStartSeconds;
        var countPerHour = count * 3600d / windowDurationSeconds;
        var first = count == 0 ? (double?)null : times[0];
        var last = count == 0 ? (double?)null : times[^1];
        var spanSeconds = count < 2 ? 0d : times[^1] - times[0];
        var spanRatePerHour = count < 2 || spanSeconds <= 0
            ? null
            : (double?)((count - 1) * 3600d / spanSeconds);
        var medianHeadwaySeconds = CalculateMedianHeadway(times);

        return new CapacityObservationResult(
            stationId,
            direction,
            eventKind,
            windowStartSeconds,
            windowEndSeconds,
            count,
            countPerHour,
            spanRatePerHour,
            medianHeadwaySeconds,
            first,
            last,
            ignoredMissingServiceRunIdCount);
    }

    private static void ValidateWindow(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end)
            || start < 0 || end < 0 || end <= start)
        {
            throw new SimulationValidationException(["容量觀測時間窗必須是有限的非負數，且結束時間必須大於開始時間。"]);
        }
    }

    private static double? CalculateMedianHeadway(IReadOnlyList<double> sortedTimes)
    {
        if (sortedTimes.Count < 2)
        {
            return null;
        }

        var headways = new double[sortedTimes.Count - 1];
        for (var index = 0; index < headways.Length; index++)
        {
            headways[index] = sortedTimes[index + 1] - sortedTimes[index];
        }
        Array.Sort(headways);

        var middle = headways.Length / 2;
        return headways.Length % 2 == 1
            ? headways[middle]
            : (headways[middle - 1] + headways[middle]) / 2d;
    }
}
