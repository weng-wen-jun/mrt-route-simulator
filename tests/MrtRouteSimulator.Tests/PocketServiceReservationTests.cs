using MrtRouteSimulator.Engine;

internal static class PocketServiceReservationTests
{
    public static void CentralPocketServiceRoutesReserveWaitAndReleaseSafely()
    {
        var runtime = TopologyProjectFormat.CreateRuntime(CreateCentralPocketDocument());
        var world = CreateWorld(runtime);
        var firstRun = RunToCompletion(world);

        var departures = firstRun.Events.Where(item => item.EventType == SimulationEventType.Departure)
            .GroupBy(e => e.ServiceRunId).Select(g => g.First()).ToArray();
        Equal(2, departures.Length, "上下行服務車次都應完成發車。");
        var waits = firstRun.Events.Where(item => item.EventType == SimulationEventType.WaitingForResource
            && item.ResourceId == "POCKET").ToArray();
        Equal(1, waits.Length, "同時發車時應只有一方等待 POCKET 資源。");
        True(departures.Any(item => item.VehicleId == waits[0].VehicleId
            && item.SimulationTimeSeconds > waits[0].SimulationTimeSeconds),
            "等待 POCKET 的車次應在資源釋放後才發車。");
        Equal(0, firstRun.Events.Count(item => item.EventType == SimulationEventType.Collision), "不應發生碰撞。");
        Equal(0, firstRun.Events.Count(item => item.EventType == SimulationEventType.StationStopViolation), "不應發生停站違規。");
        True(world.IsComplete, "雙向服務車次都應完成。");

        var pocketRelease = firstRun.Events
            .Where(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("POCKET", StringComparer.OrdinalIgnoreCase) == true)
            .ToArray();
        Equal(2, pocketRelease.Length, "兩個方向都應在車尾淨空後釋放 POCKET 資源。");
        Equal(pocketRelease.Length, firstRun.ReleaseObservations.Count,
            "每次 POCKET RouteReleased 都應有對應的 topology footprint rear-clear 觀察。");
        var pocketEdges = world.TopologyInfrastructure.Edges.Values
            .Where(edge => edge.ConflictResourceIds.Contains("POCKET"))
            .Select(edge => edge.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var observation in firstRun.ReleaseObservations)
        {
            True(observation.OccupiedIntervals.All(interval => !pocketEdges.Contains(interval.TrackEdgeId)),
                "POCKET RouteReleased 時所有現存 footprint 都必須已離開受保護 edge。");
        }

        var firstDepartureTime = departures.Min(item => item.SimulationTimeSeconds);
        var laterDeparture = departures.Max(item => item.SimulationTimeSeconds);
        var earlierRelease = pocketRelease.Min(item => item.SimulationTimeSeconds);
        True(laterDeparture >= earlierRelease, "等待車次的 Departure 必須晚於或等於前車 POCKET RouteReleased。");
        True(firstDepartureTime < laterDeparture, "同時到期的兩車次應有一方先發車、另一方延後。");

        world.Reset();
        var secondRun = RunToCompletion(world);
        Equal(firstRun.EventSignatures.Count, secondRun.EventSignatures.Count, "Reset 後事件數應可重現。");
        for (var index = 0; index < firstRun.EventSignatures.Count; index++)
        {
            Equal(firstRun.EventSignatures[index], secondRun.EventSignatures[index], "Reset 後事件順序與內容應可重現。");
        }
    }

    private static SimulationWorld CreateWorld(TopologyProjectRuntime runtime) => new SimulationWorldOptions(
        Route: null,
        TrainParameters: runtime.TrainParameters,
        OperationalParameters: runtime.OperationalParameters,
        TrainCount: 2,
        InitialDepartureIntervalSeconds: 1,
        MovingBlockMode: MovingBlockMode.Control,
        ServicePatterns: runtime.ServicePatterns,
        DispatchPlan: runtime.DispatchPlan,
        VehicleTypes: runtime.VehicleTypes,
        ServiceTypes: runtime.ServiceTypes,
        Topology: runtime.Topology).CreateWorld();

    private static RunResult RunToCompletion(SimulationWorld world)
    {
        var releaseObservations = new List<ReleaseObservation>();
        while (!world.IsComplete && world.CurrentTimeSeconds < 1200)
        {
            var snapshot = world.Tick();
            foreach (var item in snapshot.NewEvents.Where(item => item.EventType == SimulationEventType.RouteReleased
                && item.ResourceIds?.Contains("POCKET", StringComparer.OrdinalIgnoreCase) == true))
            {
                releaseObservations.Add(new ReleaseObservation(
                    item.SimulationTimeSeconds,
                    world.TopologyOccupancy.Values
                        .SelectMany(footprint => footprint.OccupiedIntervals)
                        .ToArray()));
            }
        }

        True(world.IsComplete, "中央袋狀軌測試應在時間上限內完成。");
        return new RunResult(
            world.Events.ToArray(),
            world.Events.Select(EventSignature).ToArray(),
            releaseObservations);
    }

    private static TopologyProjectDocument CreateCentralPocketDocument()
    {
        var source = StationLayoutTemplateService.Build(StationLayoutTemplateKind.CentralPocket);
        return source with
        {
            Dispatch = source.Dispatch with
            {
                ManualTimetableRows = source.Dispatch.ManualTimetableRows!
                    .Select(row => row with { PlannedDepartureTimeSeconds = 0 })
                    .ToArray()
            }
        };
    }

    private static string EventSignature(SimulationEvent item) => string.Join("|",
        item.SimulationTimeSeconds.ToString("R"), item.EventType, item.VehicleId, item.ServiceRunId,
        item.TrackEdgeId, item.PlatformId, item.ResourceId,
        string.Join(",", item.ResourceIds ?? []));

    private sealed record ReleaseObservation(double SimulationTimeSeconds, IReadOnlyList<TrackOccupancyInterval> OccupiedIntervals);

    private sealed record RunResult(
        IReadOnlyList<SimulationEvent> Events,
        IReadOnlyList<string> EventSignatures,
        IReadOnlyList<ReleaseObservation> ReleaseObservations);

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} 預期 {expected}，實際 {actual}。");
    }
}
