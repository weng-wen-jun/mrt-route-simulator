using MrtRouteSimulator.Engine;

internal static class CapacityObservationTests
{
    public static void FiltersStationDirectionAndUsesHalfOpenWindow()
    {
        var canonical = CapacityObservationAnalysis.Analyze(
            [Event(10, "RUN-A", "S1", TrainDirection.Outbound), Event(10, "run-a", "s1", TrainDirection.Outbound)],
            "s1", TrainDirection.Outbound, 0, 20, CapacityObservationEventKind.Departure);
        Equal(1, canonical.Count); // topology IDs use ordinal-ignore-case identity
        var events = new[]
        {
            Event(10, "RUN-0", "S1", TrainDirection.Outbound),
            Event(20, "RUN-1", "S1", TrainDirection.Outbound),
            Event(30, "RUN-2", "S1", TrainDirection.Outbound),
            Event(40, "RUN-IN", "S1", TrainDirection.Inbound),
            Event(50, "RUN-OTHER", "S2", TrainDirection.Outbound),
            Event(60, "RUN-END", "S1", TrainDirection.Outbound)
        };

        var result = CapacityObservationAnalysis.Analyze(
            events, "S1", TrainDirection.Outbound, 10, 60, CapacityObservationEventKind.Departure);

        Equal(3, result.Count);
        NearlyEqual(216, result.CountPerHour);
        NearlyEqual(360, result.SpanRatePerHour!.Value);
        NearlyEqual(10, result.MedianHeadwaySeconds!.Value);
        NearlyEqual(10, result.FirstEventTimeSeconds!.Value);
        NearlyEqual(30, result.LastEventTimeSeconds!.Value);
    }

    public static void DeduplicatesServiceRunAndIgnoresMissingRunId()
    {
        var events = new[]
        {
            Event(10, "RUN-1", "S1", TrainDirection.Outbound, vehicleId: "V-1"),
            Event(11, "RUN-1", "S1", TrainDirection.Outbound, vehicleId: "V-1"),
            Event(20, "RUN-2", "S1", TrainDirection.Outbound),
            Event(50, "RUN-3", "S1", TrainDirection.Outbound),
            Event(30, "", "S1", TrainDirection.Outbound),
            Event(40, "   ", "S1", TrainDirection.Outbound)
        };

        var result = CapacityObservationAnalysis.Analyze(
            events, "S1", TrainDirection.Outbound, 0, 60, CapacityObservationEventKind.Departure);

        Equal(3, result.Count);
        Equal(2, result.IgnoredMissingServiceRunIdCount);
        NearlyEqual(10, result.FirstEventTimeSeconds!.Value);
        NearlyEqual(50, result.LastEventTimeSeconds!.Value);
        NearlyEqual(20, result.MedianHeadwaySeconds!.Value);
    }

    public static void EmptyObservationIsZeroAndNullableMetricsRemainMissing()
    {
        var result = CapacityObservationAnalysis.Analyze(
            [Event(10, "RUN-1", "S2", TrainDirection.Outbound)],
            "S1", TrainDirection.Outbound, 100, 160, CapacityObservationEventKind.Departure);

        Equal(0, result.Count);
        NearlyEqual(0, result.CountPerHour);
        True(result.SpanRatePerHour is null, "沒有觀測事件時跨度流率應為 null。");
        True(result.MedianHeadwaySeconds is null, "沒有觀測事件時中位班距應為 null。");
        True(result.FirstEventTimeSeconds is null && result.LastEventTimeSeconds is null,
            "沒有觀測事件時首末時間應為 null。");
    }

    public static void UsesExactShortWindowDenominator()
    {
        var irregular = CapacityObservationAnalysis.Analyze(
            [Event(0, "A", "S1", TrainDirection.Outbound), Event(10, "B", "S1", TrainDirection.Outbound), Event(40, "C", "S1", TrainDirection.Outbound), Event(60, "D", "S1", TrainDirection.Outbound)],
            "S1", TrainDirection.Outbound, 0, 61, CapacityObservationEventKind.Departure);
        NearlyEqual(20, irregular.MedianHeadwaySeconds!.Value); // gaps 10,30,20: sort before median
        var result = CapacityObservationAnalysis.Analyze(
            [Event(5, "RUN-1", "S1", TrainDirection.Outbound)],
            "S1", TrainDirection.Outbound, 0, 5, CapacityObservationEventKind.Departure);

        Equal(0, result.Count);
        NearlyEqual(0, result.CountPerHour);

        var included = CapacityObservationAnalysis.Analyze(
            [Event(4.999, "RUN-1", "S1", TrainDirection.Outbound)],
            "S1", TrainDirection.Outbound, 0, 5, CapacityObservationEventKind.Departure);
        NearlyEqual(720, included.CountPerHour);
    }

    public static void ArrivalRequiresExplicitEventKind()
    {
        var events = new[]
        {
            Event(10, "RUN-1", "S1", TrainDirection.Outbound, eventType: SimulationEventType.Arrival),
            Event(20, "RUN-1", "S1", TrainDirection.Outbound, eventType: SimulationEventType.Departure)
        };

        var departure = CapacityObservationAnalysis.Analyze(
            events, "S1", TrainDirection.Outbound, 0, 30, CapacityObservationEventKind.Departure);
        var arrival = CapacityObservationAnalysis.Analyze(
            events, "S1", TrainDirection.Outbound, 0, 30, CapacityObservationEventKind.Arrival);

        Equal(1, departure.Count);
        Equal(1, arrival.Count);
        NearlyEqual(20, departure.FirstEventTimeSeconds!.Value);
        NearlyEqual(10, arrival.FirstEventTimeSeconds!.Value);
        Equal(CapacityObservationEventKind.Arrival, arrival.EventKind);
    }

    public static void RejectsInvalidObservationWindow()
    {
        Throws<SimulationValidationException>(() => CapacityObservationAnalysis.Analyze(
            [], "S1", TrainDirection.Outbound, 10, 10, CapacityObservationEventKind.Departure));
        Throws<SimulationValidationException>(() => CapacityObservationAnalysis.Analyze(
            [], "S1", TrainDirection.Outbound, -1, 10, CapacityObservationEventKind.Departure));
        Throws<SimulationValidationException>(() => CapacityObservationAnalysis.Analyze(
            [], "S1", TrainDirection.Outbound, 0, double.NaN, CapacityObservationEventKind.Departure));
    }

    private static SimulationEvent Event(
        double time,
        string serviceRunId,
        string stationId,
        TrainDirection direction,
        string vehicleId = "V-1",
        SimulationEventType eventType = SimulationEventType.Departure) =>
        new(time, eventType, vehicleId, null, direction, "TRACK", 0, 0, eventType.ToString(),
            ServiceRunId: serviceRunId, StationId: stationId);

    private static void NearlyEqual(double expected, double actual, double tolerance = 1e-9)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
        }
    }

    private static void Equal<T>(T expected, T actual)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"預期 {typeof(TException).Name}。");
    }
}
