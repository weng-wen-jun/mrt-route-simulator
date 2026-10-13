using MrtRouteSimulator.Engine;

internal static class CoreV411Tests
{
    public static void ExamplePassesStructuralAndBoundedOperationalValidation()
    {
        var document = OperatingNetworkBuilder.CreateExample(LoadTemplate());

        TopologyProjectFormat.Validate(document);
        var operatingLines = document.OperatingLines!;
        var main = operatingLines.Single(line => line.LineId == "DEMO-MAIN");
        var branch = operatingLines.Single(line => line.LineId == "DEMO-BRANCH");
        Equal(2, main.TrackCount, "範例主線必須為雙線。");
        Equal(1, branch.TrackCount, "範例支線必須為單線。");
        Equal(750d, branch.JunctionChainageMeters, "範例支線必須在非站里程 750 m 接入。");
        True(document.Topology.Nodes.Any(node => node.Kind == TrackNodeKind.Junction
                && node.NodeId == EndNode(document, document.ServiceRoutes
                    .Single(route => route.ServiceRouteId == branch.OutboundServiceRouteId).Traversals[0])),
            "非站接軌點必須是實體 Junction node。");

        var created = CreateWorld(document);
        var runtime = created.Runtime;
        Equal(4, runtime.DispatchPlan.Runs.Count, "主線及支線上下行應各有一班，共四個 explicit route 車次。");
        foreach (var run in runtime.DispatchPlan.Runs)
        {
            var originStationId = created.Context.GetDisplayStations(run)[0].StationId;
            True(run.StationTimetableEntries.All(entry => entry.StationId != originStationId),
                $"{run.ServiceRunId} 的起站發車應以手動班表列為單一來源，不應重複建立逐站 entry。");
            True(run.StationTimetableEntries.Count >= 2,
                $"{run.ServiceRunId} 至少需要兩個中途站的逐站表定發車／停站資料。");
            True(run.StationTimetableEntries.All(entry => entry.MinimumDwellSeconds > 0),
                $"{run.ServiceRunId} 的中途站逐站停站時間必須是正數。");
        }
        var committed = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "samples", "15-小型-主支線逐站班表範例.mrtsim.json")));
        Equal(TopologyProjectFormat.Serialize(document), TopologyProjectFormat.Serialize(committed),
            "sample 15 必須由 OperatingNetworkBuilder.CreateExample 重建，不可讓手寫 JSON 漂移。");

        var world = created.World;
        RunToBound(world, 2_400);
        True(world.IsComplete, "小型主支線範例的四車次必須在 2,400 秒內有界完成。");
        True(world.Events.All(item => item.EventType != SimulationEventType.Collision),
            "小型主支線範例不得發生碰撞。");
        True(runtime.DispatchPlan.Runs.All(run => world.Events.Any(item =>
                item.ServiceRunId == run.ServiceRunId && item.EventType == SimulationEventType.ServiceEnded)),
            "四個車次都必須完成並退出營運。");

        var reservedResources = world.Events
            .Where(item => item.EventType == SimulationEventType.RouteReserved)
            .SelectMany(item => item.ResourceIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var releasedResources = world.Events
            .Where(item => item.EventType == SimulationEventType.RouteReleased)
            .SelectMany(item => item.ResourceIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        True(reservedResources.Count > 0 && reservedResources.IsSubsetOf(releasedResources),
            "bounded operational gate 結束前，所有曾占用的共用進路資源都必須出現釋放事件。");
    }

    public static void ResourceDelayedRunStillObservesEveryScheduledDwell()
    {
        var document = OperatingNetworkBuilder.CreateExample(LoadTemplate());
        var created = CreateWorld(document);
        RunToBound(created.World, 2_400);
        True(created.World.IsComplete, "資源延後案例必須在有界時間內完成。");

        var waiting = created.World.Events.FirstOrDefault(item =>
            item.EventType == SimulationEventType.WaitingForResource
            && item.ServiceRouteId is "DEMO-BRANCH:DOWN" or "DEMO-BRANCH:UP");
        True(waiting is not null, "單線支線相向車次必須至少有一班等待共用進路資源。");

        var waitingRun = created.Runtime.DispatchPlan.Runs.Single(run => run.ServiceRunId == waiting!.ServiceRunId);
        var rows = OperationsTimetable.Build(
                created.Context,
                created.Runtime.DispatchPlan,
                [],
                created.World.Events,
                created.Runtime.ServicePatterns)
            .Where(row => row.ServiceRunId == waitingRun.ServiceRunId)
            .ToArray();
        var origin = rows[0];
        True(origin.ActualDepartureTimeSeconds is { } actualOriginDeparture
                && origin.PlannedDepartureTimeSeconds is { } plannedOriginDeparture
                && actualOriginDeparture > plannedOriginDeparture + 0.01,
            "等待單線共用進路的車次必須晚於表定時刻發車。");
        var scheduledStops = rows.Where(row => row.PlannedDwellSeconds is not null).ToArray();
        True(scheduledStops.Length >= 2, "資源延後車次仍須有至少兩個逐站停站驗證點。");
        True(scheduledStops.Any(row => row.ActualArrivalTimeSeconds is { } actualArrival
                && row.PlannedDepartureTimeSeconds is { } plannedDeparture
                && actualArrival > plannedDeparture + 0.01),
            "此案例必須實際涵蓋受資源延後後，晚於逐站表定發車時刻才抵達的中途站。");
        foreach (var row in scheduledStops)
        {
            True(row.ActualDwellSeconds is { } actualDwell
                    && row.PlannedDwellSeconds is { } plannedDwell
                    && actualDwell + 0.11 >= plannedDwell,
                $"{waitingRun.ServiceRunId} 即使受資源延後，{row.StationId} 仍必須停滿 {row.PlannedDwellSeconds:0.###} 秒。");
        }
    }

    public static void MultipleBranchesShareTrunkAndCompleteWithoutCollision()
    {
        var main = MainLine();
        var firstBranch = Branch("BRANCH-A", "支線 A", main.LineId, 750, trackCount: 1);
        var secondBranch = Branch("BRANCH-B", "支線 B", main.LineId, 1_500, trackCount: 2);
        var document = OperatingNetworkBuilder.Build(LoadTemplate(), [main, firstBranch, secondBranch]);

        TopologyProjectFormat.Validate(document);
        var mainDown = Route(document, main.OutboundServiceRouteId);
        var firstDown = Route(document, firstBranch.OutboundServiceRouteId);
        var secondDown = Route(document, secondBranch.OutboundServiceRouteId);
        Equal(mainDown.Traversals[0], firstDown.Traversals[0],
            "第一支線必須共用主線起點至 750 m 的實體幹線。");
        True(secondDown.Traversals.Take(2).SequenceEqual(mainDown.Traversals.Take(2)),
            "第二支線必須共用主線起點至 1,500 m 的兩段實體幹線。");
        True(firstDown.Traversals[1] != secondDown.Traversals[1],
            "兩條支線在各自 junction 後不得誤用同一條分歧 edge。");

        var created = CreateWorld(document);
        RunToBound(created.World, 3_000);
        True(created.World.IsComplete, "雙支線共用幹線的六個方向車次必須在 3,000 秒內完成。");
        True(created.World.Events.All(item => item.EventType != SimulationEventType.Collision),
            "多支線共用幹線不得產生碰撞事件。");
        foreach (var run in created.Runtime.DispatchPlan.Runs)
        {
            True(created.World.Events.Any(item => item.ServiceRunId == run.ServiceRunId
                    && item.EventType == SimulationEventType.ServiceEnded),
                $"{run.ServiceRunId} 必須完成並退出營運。");
        }
    }

    public static void RouteSpecificComparisonAndTimetableCsvStayConsistent()
    {
        var source = OperatingNetworkBuilder.CreateExample(LoadTemplate());
        var branchPatternId = "STOP:DEMO-BRANCH:ALL";
        var branchRoute = Route(source, "DEMO-BRANCH:DOWN");
        var passStationId = branchRoute.Stops[2].StationId;
        var pendingStationId = branchRoute.Stops[^2].StationId;
        var branchPattern = source.StopPatterns.Single(pattern => pattern.Id == branchPatternId);
        var patterns = source.StopPatterns.Select(pattern => pattern.Id == branchPatternId
            ? pattern with
            {
                Instructions = pattern.Instructions.Select(instruction =>
                    instruction.StationId == passStationId
                        ? instruction with { Action = StopPatternAction.Pass, DwellTimeSeconds = 0 }
                        : instruction).ToArray()
            }
            : pattern).ToArray();
        var rows = source.Dispatch.ManualTimetableRows!.Select(row => row.StopPatternId == branchPatternId
            ? row with
            {
                StationTimetableEntries = row.StationTimetableEntries!
                    .Where(entry => entry.StationId != passStationId).ToArray()
            }
            : row).ToArray();
        var document = source with
        {
            StopPatterns = patterns,
            Dispatch = source.Dispatch with { ManualTimetableRows = rows }
        };
        TopologyProjectFormat.Validate(document);

        var created = CreateWorld(document);
        created.World.AdvanceTo(400);
        var comparison = V1V2Comparison.Analyze(
            created.Context,
            created.Runtime.DispatchPlan,
            created.Runtime.VehicleTypes,
            created.Runtime.ServicePatterns.Select(pattern => new StopPatternDefinition(
                pattern.PatternId,
                pattern.PatternName,
                pattern.Instructions.Select(instruction => new StopPatternInstruction(
                    instruction.StationId,
                    instruction.Mode == StationServiceMode.Pass ? StopPatternAction.Pass : StopPatternAction.Stop,
                    instruction.DwellTimeSeconds,
                    instruction.SpeedLimitMetersPerSecond)))),
            created.Runtime.TrainParameters,
            created.World.Events);

        foreach (var run in created.Runtime.DispatchPlan.Runs)
        {
            var expectedStations = created.Context.GetDisplayStations(run).Select(station => station.StationId);
            var actualStations = comparison.Stations.Where(row => row.ServiceRunId == run.ServiceRunId)
                .Select(row => row.StationId);
            True(actualStations.SequenceEqual(expectedStations),
                $"{run.ServiceRunId} 的 V1/V2 理論站序必須取自自己的 ServiceRoute。");
        }
        var mainRun = created.Runtime.DispatchPlan.Runs.Single(run =>
            run.ServiceRouteId == "DEMO-MAIN:DOWN");
        foreach (var entry in mainRun.StationTimetableEntries)
        {
            var theory = comparison.Stations.Single(row =>
                row.ServiceRunId == mainRun.ServiceRunId && row.StationId == entry.StationId);
            True(theory.TheoreticalArrivalTimeSeconds is not null
                    && theory.TheoreticalDepartureTimeSeconds is not null
                    && theory.TheoreticalDwellSeconds is not null,
                $"{entry.StationId} 必須有可比較的 V1 理論到／發與停站秒。");
            var theoreticalArrival = theory.TheoreticalArrivalTimeSeconds!.Value;
            var theoreticalDeparture = theory.TheoreticalDepartureTimeSeconds!.Value;
            var theoreticalDwell = theory.TheoreticalDwellSeconds!.Value;
            var scheduledDeparture = RelativeSeconds(
                entry.PlannedDepartureTime,
                created.Runtime.DispatchPlan.ScheduleAnchorTime);
            var expectedDeparture = Math.Max(
                theoreticalArrival + entry.MinimumDwellSeconds,
                scheduledDeparture);
            NearlyEqual(expectedDeparture, theoreticalDeparture, 0.001,
                $"V1 理論發車必須取實際到站加逐站最短停站與表定時刻的較晚者（{entry.StationId}）。");
            NearlyEqual(entry.MinimumDwellSeconds, theoreticalDwell, 0.001,
                $"V1 理論表定停站秒必須採用逐站最短停站設定（{entry.StationId}）。");
        }
        var branchRun = created.Runtime.DispatchPlan.Runs.Single(run =>
            run.ServiceRouteId == "DEMO-BRANCH:DOWN");
        var passComparison = comparison.Stations.Single(row =>
            row.ServiceRunId == branchRun.ServiceRunId && row.StationId == passStationId);
        True(passComparison.TheoreticalDepartureTimeSeconds is null
                && passComparison.TheoreticalDwellSeconds is null,
            "跨站的 V1 理論發車與停站秒必須維持 null。");

        var timetable = OperationsTimetable.Build(
            created.Context,
            created.Runtime.DispatchPlan,
            [],
            created.World.Events,
            created.Runtime.ServicePatterns);
        var passRow = timetable.Single(row =>
            row.ServiceRunId == branchRun.ServiceRunId && row.StationId == passStationId);
        True(passRow.PlannedDwellSeconds is null && passRow.ActualDepartureTimeSeconds is null,
            "跨站列不適用表定停站與實際發車秒。");
        var pendingRow = timetable.Single(row =>
            row.ServiceRunId == branchRun.ServiceRunId && row.StationId == pendingStationId);
        True(pendingRow.PlannedDepartureTimeSeconds is not null
                && pendingRow.ActualDepartureTimeSeconds is null,
            "尚未發車的逐站列必須保留表定發車秒，並讓實際發車秒維持空值。");

        var csv = ParseCsv(OperationsTimetable.BuildCsv(timetable));
        var header = csv[0];
        var routeColumn = Array.IndexOf(header, "營運路徑ID");
        var lineColumn = Array.IndexOf(header, "營運路線ID");
        var stationColumn = Array.IndexOf(header, "車站ID");
        var plannedDepartureColumn = Array.IndexOf(header, "表定發車秒");
        var actualDepartureColumn = Array.IndexOf(header, "實際發車秒");
        var plannedDwellColumn = Array.IndexOf(header, "表定停站秒");
        True(routeColumn >= 0 && lineColumn >= 0 && plannedDepartureColumn >= 0
                && actualDepartureColumn >= 0 && plannedDwellColumn >= 0,
            "時刻表 CSV 必須含 route identity 與表定／實際秒欄位。");
        var passCsv = csv.Single(cells => cells[1] == passRow.VehicleId
            && cells[2] == passRow.ServiceRunId && cells[stationColumn] == passStationId);
        Equal(branchRun.ServiceRouteId!, passCsv[routeColumn], "CSV 跨站列的 ServiceRouteId 必須一致。");
        Equal("DEMO-BRANCH", passCsv[lineColumn], "CSV 跨站列的 OperatingLineId 必須一致。");
        Equal(string.Empty, passCsv[plannedDwellColumn], "CSV 跨站列的表定停站秒必須為空。");
        Equal(string.Empty, passCsv[actualDepartureColumn], "CSV 跨站列的實際發車秒必須為空。");
        var pendingCsv = csv.Single(cells => cells[1] == pendingRow.VehicleId
            && cells[2] == pendingRow.ServiceRunId && cells[stationColumn] == pendingStationId);
        Equal(Format(pendingRow.PlannedDepartureTimeSeconds), pendingCsv[plannedDepartureColumn],
            "CSV 必須保存逐站表定發車秒。");
        Equal(string.Empty, pendingCsv[actualDepartureColumn],
            "CSV 尚未發車列的實際發車秒必須為空。");
        var completedRow = timetable.First(row => row.ServiceRouteId == mainRun.ServiceRouteId
            && row.ActualDepartureTimeSeconds is not null && row.PlannedDepartureTimeSeconds is not null);
        var completedCsv = csv.Single(cells => cells[1] == completedRow.VehicleId
            && cells[2] == completedRow.ServiceRunId && cells[stationColumn] == completedRow.StationId);
        Equal(mainRun.ServiceRouteId!, completedCsv[routeColumn],
            "CSV 已發車列的 ServiceRouteId 必須一致。");
        Equal("DEMO-MAIN", completedCsv[lineColumn],
            "CSV 已發車列的 OperatingLineId 必須一致。");
        Equal(Format(completedRow.PlannedDepartureTimeSeconds), completedCsv[plannedDepartureColumn],
            "CSV 已發車列的表定發車秒必須與結構化結果一致。");
        Equal(Format(completedRow.ActualDepartureTimeSeconds), completedCsv[actualDepartureColumn],
            "CSV 已發車列的實際發車秒必須與 SimulationWorld 事件一致。");
    }

    private static TopologyProjectDocument LoadTemplate()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        return TopologyProjectFormat.Deserialize(File.ReadAllText(path));
    }

    private static (SimulationWorld World, TopologyProjectRuntime Runtime, TopologyResultContext Context) CreateWorld(
        TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var world = new SimulationWorldOptions(
            Route: null,
            runtime.TrainParameters,
            runtime.OperationalParameters,
            runtime.DispatchPlan.Runs.Count,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology).CreateWorld();
        return (world, runtime, world.GetTopologyResultContext());
    }

    private static void RunToBound(SimulationWorld world, double maximumTimeSeconds)
    {
        while (!world.IsComplete && world.CurrentTimeSeconds < maximumTimeSeconds)
            world.Tick();
    }

    private static OperatingLineDefinition MainLine() => new()
    {
        LineId = "MAIN",
        Name = "共用主線",
        TrackCount = 2,
        LengthMeters = 3_000,
        StationCount = 5,
        OutboundServiceRouteId = "MAIN:DOWN",
        InboundServiceRouteId = "MAIN:UP"
    };

    private static OperatingLineDefinition Branch(
        string lineId,
        string name,
        string parentLineId,
        double junctionChainageMeters,
        int trackCount) => new()
    {
        LineId = lineId,
        Name = name,
        TrackCount = trackCount,
        ParentLineId = parentLineId,
        JunctionChainageMeters = junctionChainageMeters,
        LengthMeters = 1_200,
        StationCount = 3,
        OutboundServiceRouteId = $"{lineId}:DOWN",
        InboundServiceRouteId = $"{lineId}:UP"
    };

    private static ServiceRouteDefinition Route(TopologyProjectDocument document, string routeId) =>
        document.ServiceRoutes.Single(route => route.ServiceRouteId == routeId);

    private static string EndNode(TopologyProjectDocument document, DirectedTrackTraversal traversal)
    {
        var edge = document.Topology.Edges.Single(item => item.TrackEdgeId == traversal.TrackEdgeId);
        return traversal.Direction == TraversalDirection.Forward ? edge.ToNodeId : edge.FromNodeId;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("找不到 repository root。");
    }

    private static string[][] ParseCsv(string text) => text
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split(',').Select(Unquote).ToArray())
        .ToArray();

    private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"'
        ? value[1..^1].Replace("\"\"", "\"")
        : value;

    private static string Format(double? value) => value?.ToString(
        "0.###", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static double RelativeSeconds(TimeSpan value, TimeSpan anchor)
    {
        var seconds = (value - anchor).TotalSeconds;
        return seconds < 0 ? seconds + TimeSpan.FromDays(1).TotalSeconds : seconds;
    }

    private static void NearlyEqual(double expected, double actual, double tolerance, string message)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message} Expected={expected:0.###}; Actual={actual:0.###}");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected={expected}; Actual={actual}");
    }
}
