using MrtRouteSimulator.Engine;

internal static class StationTimetableTests
{
    public static void Schema8RoundTripPreservesPerStationSchedule()
    {
        var document = LoadBaseline();
        var scheduled = WithManualSchedule(document,
        [
            new ProjectStationTimetableEntry("W", 0, 0),
            new ProjectStationTimetableEntry("M", 180, 12)
        ]);

        var restored = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(scheduled));
        var row = restored.Dispatch.ManualTimetableRows!.Single();
        var entries = row.StationTimetableEntries!;
        Equal(2, entries.Length);
        Equal("M", entries[1].StationId);
        NearlyEqual(180, entries[1].PlannedDepartureTimeSeconds);
        NearlyEqual(12, entries[1].MinimumDwellSeconds);

        var runtime = TopologyProjectFormat.CreateRuntime(restored);
        var run = runtime.DispatchPlan.Runs.Single();
        Equal(2, run.StationTimetableEntries.Count);
        NearlyEqual(12, run.StationTimetableEntries[1].MinimumDwellSeconds);
    }

    public static void DepartureRequiresScheduledClockAndConfiguredDwell()
    {
        var document = LoadBaseline();
        var baseline = CreateWorld(WithManualSchedule(document, []));
        var baselineArrival = AdvanceUntilEvent(baseline.World, baseline.Context, "M", SimulationEventType.Arrival, 600)
            .SimulationTimeSeconds;

        var scheduledDeparture = Math.Ceiling(baselineArrival + 45);
        var scheduled = CreateWorld(WithManualSchedule(document,
        [
            new ProjectStationTimetableEntry("W", 0, 0),
            new ProjectStationTimetableEntry("M", scheduledDeparture, 10)
        ]));

        scheduled.World.AdvanceTo(scheduledDeparture - 1);
        var pending = OperationsTimetable.Build(
            scheduled.Context,
            scheduled.Runtime.DispatchPlan,
            [],
            scheduled.World.Events,
            scheduled.Runtime.ServicePatterns).Single(entry => entry.StationId == "M");
        NearlyEqual(scheduledDeparture, pending.PlannedDepartureTimeSeconds ?? -1);
        NearlyEqual(10, pending.PlannedDwellSeconds ?? -1);
        True(pending.ActualArrivalTimeSeconds is not null, "列車應已抵達中央站。");
        True(pending.ActualDepartureTimeSeconds is null, "尚未到表定時刻時不可從中央站發車。");
        True(pending.ActualDwellSeconds is null, "尚未發車時實際停站時間必須維持 null。");

        var departure = AdvanceUntilEvent(
            scheduled.World,
            scheduled.Context,
            "M",
            SimulationEventType.Departure,
            scheduledDeparture + 60);
        True(departure.SimulationTimeSeconds + 0.11 >= scheduledDeparture,
            "中央站不得早於表定發車時刻發車。");
        var arrival = scheduled.World.Events.First(item =>
            item.EventType == SimulationEventType.Arrival
            && scheduled.Context.MatchesStationEvent(TrainDirection.Outbound, "M", item));
        True(departure.SimulationTimeSeconds - arrival.SimulationTimeSeconds >= 9.9,
            "中央站必須同時滿足逐站表定停站時間。");

        var completed = OperationsTimetable.Build(
            scheduled.Context,
            scheduled.Runtime.DispatchPlan,
            [],
            scheduled.World.Events,
            scheduled.Runtime.ServicePatterns).Single(entry => entry.StationId == "M");
        NearlyEqual(departure.SimulationTimeSeconds - arrival.SimulationTimeSeconds,
            completed.ActualDwellSeconds ?? -1,
            0.11);

        var dwellOverride = CreateWorld(WithManualSchedule(document,
        [
            new ProjectStationTimetableEntry("W", 0, 0),
            new ProjectStationTimetableEntry("M", 0, 7)
        ]));
        var overrideDeparture = AdvanceUntilEvent(
            dwellOverride.World,
            dwellOverride.Context,
            "M",
            SimulationEventType.Departure,
            600);
        var overrideArrival = dwellOverride.World.Events.First(item =>
            item.EventType == SimulationEventType.Arrival
            && dwellOverride.Context.MatchesStationEvent(TrainDirection.Outbound, "M", item));
        var overrideDwell = overrideDeparture.SimulationTimeSeconds - overrideArrival.SimulationTimeSeconds;
        True(overrideArrival.SimulationTimeSeconds > 0,
            "此案例必須在表定發車時間之後才抵達，以驗證晚到仍須停滿。");
        True(overrideDwell is >= 6.9 and < 10,
            $"逐站表定停站 7 秒應覆寫停站模式的 30 秒，實際為 {overrideDwell:0.0} 秒。");
        True(overrideDeparture.SimulationTimeSeconds + 0.11
                >= overrideArrival.SimulationTimeSeconds + 7,
            "列車即使晚到，仍不得在實際抵達後未停滿表定時間就離站。");
    }

    public static void RejectsAmbiguousOriginTerminalAndPassSchedules()
    {
        var document = LoadBaseline();
        ThrowsValidation(
            () => TopologyProjectFormat.Serialize(WithManualSchedule(document,
                [new ProjectStationTimetableEntry("W", 0, 1)])),
            "表定停站時間必須為 0 秒");
        ThrowsValidation(
            () => TopologyProjectFormat.Serialize(WithManualSchedule(document,
                [new ProjectStationTimetableEntry("E", 300, 0)])),
            "終點");
        ThrowsValidation(
            () => TopologyProjectFormat.Serialize(WithManualSchedule(document,
                [new ProjectStationTimetableEntry("M", 180, 0)], "EXPRESS-SKIP-M", "EXPRESS")),
            "只可對停靠站");

        var express = CreateWorld(WithManualSchedule(document, [], "EXPRESS-SKIP-M", "EXPRESS"));
        var passedStation = OperationsTimetable.Build(
            express.Context,
            express.Runtime.DispatchPlan,
            [],
            [],
            express.Runtime.ServicePatterns).Single(entry => entry.StationId == "M");
        True(passedStation.PlannedDwellSeconds is null,
            "跨站通過不適用表定停站時間，結果必須輸出 null。");
    }

    public static void DifferentRunsUseAssignedServiceRoutesInResults()
    {
        var main = new OperatingLineDefinition
        {
            LineId = "RESULT-MAIN",
            Name = "結果主線",
            TrackCount = 2,
            LengthMeters = 3_000,
            StationCount = 4,
            OutboundServiceRouteId = "RESULT-MAIN:DOWN",
            InboundServiceRouteId = "RESULT-MAIN:UP"
        };
        var branch = new OperatingLineDefinition
        {
            LineId = "RESULT-BRANCH",
            Name = "結果支線",
            TrackCount = 2,
            ParentLineId = main.LineId,
            JunctionChainageMeters = 1_000,
            LengthMeters = 1_500,
            StationCount = 3,
            OutboundServiceRouteId = "RESULT-BRANCH:DOWN",
            InboundServiceRouteId = "RESULT-BRANCH:UP"
        };
        var document = OperatingNetworkBuilder.Build(LoadBaseline(), [main, branch]);
        var created = CreateWorld(document);
        created.World.AdvanceTo(600);
        var rows = OperationsTimetable.Build(
            created.Context,
            created.Runtime.DispatchPlan,
            [],
            created.World.Events,
            created.Runtime.ServicePatterns);

        foreach (var line in new[] { main, branch })
        {
            var run = created.Runtime.DispatchPlan.Runs.Single(item =>
                item.Direction == TrainDirection.Outbound
                && item.ServiceRouteId == line.OutboundServiceRouteId);
            var runRows = rows.Where(item => item.ServiceRunId == run.ServiceRunId).ToArray();
            Equal(line.OutboundServiceRouteId, runRows[0].ServiceRouteId!);
            Equal(line.LineId, runRows[0].OperatingLineId!);
            var expectedStations = document.ServiceRoutes.Single(route =>
                    route.ServiceRouteId == line.OutboundServiceRouteId)
                .Stops.Select(stop => stop.StationId);
            True(runRows.Select(item => item.StationId).SequenceEqual(expectedStations),
                $"車次 {run.ServiceRunId} 的結果站序必須由指定 ServiceRoute 控制。");
            True(created.World.Events.Any(item => item.ServiceRunId == run.ServiceRunId
                    && item.ServiceRouteId == line.OutboundServiceRouteId),
                $"車次 {run.ServiceRunId} 的實際事件必須保留指定 ServiceRouteId。");
        }

        var legacy = CreateWorld(LoadBaseline());
        var legacyRun = legacy.Runtime.DispatchPlan.Runs.Single();
        True(legacyRun.ServiceRouteId is null, "舊專案車次應維持未指定 ServiceRouteId 的相容輸入。");
        var legacyRows = OperationsTimetable.Build(
            legacy.Context,
            legacy.Runtime.DispatchPlan,
            [],
            [],
            legacy.Runtime.ServicePatterns);
        var expectedDefaultRoute = LoadBaseline().DirectionRouteBindings.Single(binding =>
            binding.Direction == legacyRun.Direction).ServiceRouteId;
        True(legacyRows.All(row => row.ServiceRouteId == expectedDefaultRoute),
            "舊專案未指定 route 的車次仍須由結果 context 輸出實際預設 ServiceRouteId。");
    }

    private static TopologyProjectDocument WithManualSchedule(
        TopologyProjectDocument document,
        ProjectStationTimetableEntry[] stationEntries,
        string stopPatternId = "ALL-STOPS",
        string serviceTypeId = "LOCAL") => document with
        {
            Dispatch = new ProjectDispatchPlan(
                DispatchPlanningMode.ManualTimetable,
                VehicleAssignmentMode.Automatic,
                [],
                [new ProjectManualTimetableRow(
                    0,
                    TrainDirection.Outbound,
                    serviceTypeId,
                    "EMU-100",
                    stopPatternId,
                    "P-W-D",
                    ServiceRunId: "SCHEDULE-D001",
                    StationTimetableEntries: stationEntries)])
        };

    private static (SimulationWorld World, TopologyProjectRuntime Runtime, TopologyResultContext Context) CreateWorld(
        TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(
            Route: null,
            runtime.TrainParameters,
            runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count,
            ProfileMode: OperationProfileMode.RealisticOperations,
            MovingBlockMode: MovingBlockMode.Independent,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        return (world, runtime, world.GetTopologyResultContext());
    }

    private static SimulationEvent AdvanceUntilEvent(
        SimulationWorld world,
        TopologyResultContext context,
        string stationId,
        SimulationEventType eventType,
        double maximumTimeSeconds)
    {
        while (world.CurrentTimeSeconds + SimulationWorld.FixedTimeStepSeconds <= maximumTimeSeconds + 1e-7)
        {
            world.Tick();
            var match = world.Events.FirstOrDefault(item =>
                item.EventType == eventType
                && context.MatchesStationEvent(TrainDirection.Outbound, stationId, item));
            if (match is not null) return match;
        }
        throw new InvalidOperationException($"在 {maximumTimeSeconds:0.0} 秒內找不到 {stationId} 的 {eventType} 事件。");
    }

    private static TopologyProjectDocument LoadBaseline()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        return TopologyProjectFormat.Deserialize(File.ReadAllText(path));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MrtRouteSimulator.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("找不到專案根目錄。");
    }

    private static void Equal<T>(T expected, T actual) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
    }

    private static void NearlyEqual(double expected, double actual, double tolerance = 0.01)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"預期 {expected:0.###}，實際 {actual:0.###}。");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ThrowsValidation(Action action, string expectedMessage)
    {
        try
        {
            action();
        }
        catch (SimulationValidationException exception)
        {
            if (exception.Message.Contains(expectedMessage, StringComparison.Ordinal)) return;
            throw new InvalidOperationException($"錯誤訊息未包含「{expectedMessage}」：{exception.Message}");
        }
        throw new InvalidOperationException("預期拋出 SimulationValidationException。");
    }
}
