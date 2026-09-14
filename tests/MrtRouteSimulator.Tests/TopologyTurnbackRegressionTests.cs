using MrtRouteSimulator.Engine;

/// <summary>
/// 專門鎖定 Schema 8 實體尾軌折返後的反方向首站銜接。
///
/// 此檔不直接加入共用 Program.cs；由 runner 維護者註冊公開測試方法。
/// </summary>
internal static class TopologyTurnbackRegressionTests
{
    public static void PhysicalTailTurnbackStopsAtFirstReverseStation()
    {
        var samplePath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "V4.0.0-完整拓撲執行驗證範例.mrtsim.json");
        if (!File.Exists(samplePath))
        {
            throw new InvalidOperationException("找不到完整 topology 執行驗證範例。");
        }

        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = CreateWorld(runtime, document, runtime.DispatchPlan, runtime.ServicePatterns);

        world.AdvanceTo(3000);

        var reverseEvents = world.Events
            .Where(item => item.VehicleId == "TAIL-01" && item.ServiceRunId == "TAIL-UP")
            .ToArray();
        True(
            reverseEvents.Any(item => item.EventType == SimulationEventType.TailTrackReturnStarted
                && item.TrackEdgeId == "E-TAIL"),
            "東端尾軌折返後必須以 TAIL-UP 返回上行路線。");

        var reverseTerminalArrival = reverseEvents
            .Where(item => item.EventType == SimulationEventType.Arrival
                && item.PlatformId == "P-E-U")
            .OrderBy(item => item.SimulationTimeSeconds)
            .FirstOrDefault();
        True(reverseTerminalArrival is not null,
            "東端尾軌返回後必須先抵達 E 站上行月台 P-E-U。");

        var reverseTerminalDwell = reverseEvents
            .Where(item => item.EventType == SimulationEventType.DwellStarted
                && item.PlatformId == "P-E-U")
            .OrderBy(item => item.SimulationTimeSeconds)
            .FirstOrDefault();
        True(
            reverseTerminalDwell is not null
                && reverseTerminalDwell.SimulationTimeSeconds
                    >= reverseTerminalArrival!.SimulationTimeSeconds,
            "東端尾軌返回後必須在 E 站上行月台開始正常停站。");

        var reverseTerminalDeparture = reverseEvents
            .Where(item => item.EventType == SimulationEventType.Departure)
            .Where(item => item.PlatformId == "P-E-U")
            .OrderBy(item => item.SimulationTimeSeconds)
            .FirstOrDefault();
        True(
            reverseTerminalDeparture is not null
                && reverseTerminalDeparture.SimulationTimeSeconds
                    >= reverseTerminalDwell!.SimulationTimeSeconds + 30 - 0.11
                && reverseTerminalDeparture.SimulationTimeSeconds >= 1500,
            "E 站上行月台必須完成至少 30 秒停站，且遵守 TAIL-UP 接續計畫時間後發車。");

        var reverseDeparture = reverseTerminalDeparture;

        var firstReverseStationArrival = reverseEvents
            .Where(item => item.EventType == SimulationEventType.Arrival
                && item.PlatformId == "P-M-U")
            .OrderBy(item => item.SimulationTimeSeconds)
            .FirstOrDefault();
        True(
            firstReverseStationArrival is not null
                && firstReverseStationArrival.SimulationTimeSeconds > reverseDeparture!.SimulationTimeSeconds,
            "TAIL-UP 折返後第一個站 M 必須在反向發車後正常抵達上行月台。");

        var firstReverseStationDwell = reverseEvents
            .Where(item => item.EventType == SimulationEventType.DwellStarted
                && item.PlatformId == "P-M-U")
            .OrderBy(item => item.SimulationTimeSeconds)
            .FirstOrDefault();
        True(
            firstReverseStationDwell is not null
                && firstReverseStationDwell.SimulationTimeSeconds
                    >= firstReverseStationArrival!.SimulationTimeSeconds,
            "TAIL-UP 折返後第一個站 M 必須進入正常停站，而非直接略過。");

        var firstReverseStationDeparture = reverseEvents
            .Where(item => item.EventType == SimulationEventType.Departure
                && item.PlatformId == "P-M-U")
            .OrderBy(item => item.SimulationTimeSeconds)
            .FirstOrDefault();
        True(
            firstReverseStationDeparture is not null
                && firstReverseStationDeparture.SimulationTimeSeconds
                    > firstReverseStationDwell!.SimulationTimeSeconds,
            "TAIL-UP 必須完成 M 站停站後才繼續駛向 W。");

        True(
            world.Trajectory.Any(sample => sample.ServiceRunId == "TAIL-UP"
                && sample.Direction == TrainDirection.Inbound
                && sample.TrackEdgeId == "UP-M-W"
                && sample.SpeedMetersPerSecond > 0),
            "TAIL-UP 折返後必須有離開 M 站的上行軌跡速度樣本。");

        True(
            world.Trajectory.Any(sample => sample.ServiceRunId == "TAIL-UP"
                && sample.TrackEdgeId == "UP-M-W"
                && sample.TrackSpeedLimitMetersPerSecond is > 0),
            "TAIL-UP 上行軌跡必須保存當下方向的實際速限供速度圖使用。");
    }

    public static void PhysicalTailTurnbackWaitsForScheduledDepartureWithZeroDwell()
    {
        var samplePath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "V4.0.0-完整拓撲執行驗證範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var zeroDwellPatterns = runtime.ServicePatterns
            .Select(pattern => pattern.PatternId.Equals("ALL-STOPS", StringComparison.OrdinalIgnoreCase)
                ? pattern with
                {
                    Instructions = pattern.Instructions
                        .Select(instruction => instruction.StationId.Equals("E", StringComparison.OrdinalIgnoreCase)
                            ? instruction with { DwellTimeSeconds = 0 }
                            : instruction)
                        .ToArray()
                }
                : pattern)
            .ToArray();
        var delayedPlan = new ResolvedDispatchPlan(
            runtime.DispatchPlan.ActiveMode,
            runtime.DispatchPlan.VehicleAssignmentMode,
            runtime.DispatchPlan.ScheduleAnchorTime,
            runtime.DispatchPlan.Runs.Select(run =>
                run.ServiceRunId.Equals("TAIL-UP", StringComparison.OrdinalIgnoreCase)
                    ? CloneRun(run, TimeSpan.FromSeconds(1900))
                    : CloneRun(run)));
        var world = CreateWorld(runtime, document, delayedPlan, zeroDwellPatterns);

        world.AdvanceTo(1800);
        AssertWaitingAtReverseTerminal(world, "第一次執行");

        world.Reset();
        world.AdvanceTo(1800);
        AssertWaitingAtReverseTerminal(world, "Reset 後第二次執行");

        world.AdvanceTo(2000);
        var departures = world.Events
            .Where(item => item.VehicleId == "TAIL-01"
                && item.ServiceRunId == "TAIL-UP"
                && item.EventType == SimulationEventType.Departure
                && item.PlatformId == "P-E-U")
            .ToArray();
        True(departures.Length == 1, "Reset 重跑後 TAIL-UP 應只產生一筆反向終點月台發車事件。");
        True(departures[0].SimulationTimeSeconds >= 1900,
            "0 秒停站仍須等待至 TAIL-UP 接續計畫時間 1900 秒後才能發車。");
    }

    private static void AssertWaitingAtReverseTerminal(SimulationWorld world, string runLabel)
    {
        var state = world.GetSnapshot().Trains
            .Single(item => item.VehicleId == "TAIL-01");
        True(state.ServiceRunId == "TAIL-UP"
            && state.CurrentStationId == "E"
            && state.PlatformId == "P-E-U"
            && state.Phase == OperationalPhase.Turning
            && state.SpeedMetersPerSecond <= 0.001,
            $"{runLabel} 應在 E/P-E-U 等待接續發車且速度為 0。");
        True(!world.Events.Any(item => item.VehicleId == "TAIL-01"
                && item.ServiceRunId == "TAIL-UP"
                && item.EventType == SimulationEventType.Departure),
            $"{runLabel} 在 1800 秒前不可提前產生 TAIL-UP 發車事件。");
    }

    private static SimulationWorld CreateWorld(
        TopologyProjectRuntime runtime,
        TopologyProjectDocument document,
        ResolvedDispatchPlan dispatchPlan,
        IReadOnlyList<ServicePattern> servicePatterns) =>
        new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: dispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: document.Simulation.MovingBlockMode,
            ServicePatterns: servicePatterns,
            DispatchPlan: dispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();

    private static PlannedServiceRun CloneRun(PlannedServiceRun run, TimeSpan? plannedDepartureTime = null) =>
        new(
            run.ServiceRunId,
            plannedDepartureTime ?? run.PlannedDepartureTime,
            run.Direction,
            run.VehicleId,
            run.VehicleTypeId,
            run.ServiceTypeId,
            run.StopPatternId,
            run.OriginPlatformId,
            run.Sequence,
            run.ContinueAfterTerminal,
            run.ContinuationServiceRunId);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("找不到專案根目錄。");
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
