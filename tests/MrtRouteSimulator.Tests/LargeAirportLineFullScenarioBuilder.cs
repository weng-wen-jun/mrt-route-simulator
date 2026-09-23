using MrtRouteSimulator.Engine;

internal sealed record LargeAirportLineScenarioStages(
    TopologyProjectDocument MinimalBaseline,
    TopologyProjectDocument FullStationChain,
    TopologyProjectDocument ServicePatterns,
    TopologyProjectDocument O20Turnback,
    TopologyProjectDocument O04Passing,
    TopologyProjectDocument O13Passing,
    TopologyProjectDocument FullScenario,
    IReadOnlyList<TopologyScenarioStageValidation> StageValidations);

/// <summary>
/// Rebuildable source for the de-identified Large Airport Line full demonstration sample.
/// Station-center chainages are source-backed planning reference data. Facility geometry, dwell
/// times, rolling-stock performance and timetable offsets remain synthetic test values.
/// </summary>
internal static class LargeAirportLineFullScenarioBuilder
{
    internal const string ProjectId = "LARGE-AIRPORT-LINE-FULL-DEMO";
    internal const string DownRouteId = ProjectId + ":DOWN";
    internal const string UpRouteId = ProjectId + ":UP";
    internal const string VehicleTypeId = "EMU-100";
    internal const string FullLineServiceId = "FULL-LINE";
    internal const string SectionServiceId = "SECTION";
    internal const string AirportDirectServiceId = "AIRPORT-DIRECT";
    internal const string FullLinePatternId = "FULL-LINE-ALL";
    internal const string O04HoldingPatternId = "FULL-LINE-O04-HOLD";
    internal const string O13HoldingPatternId = "FULL-LINE-O13-HOLD";
    internal const string SectionPatternId = "SECTION-O20";
    internal const string AirportDirectPatternId = "AIRPORT-DIRECT-O20";
    internal const string AirportDirectRunId = "AIRPORT-DIRECT-DOWN-01";
    internal const string AirportDirectUpRunId = "AIRPORT-DIRECT-UP-01";
    internal const string SectionDownRunId = "SECTION-DOWN-01";
    internal const string SectionUpRunId = "SECTION-UP-01";
    internal const double FullRouteLengthMeters = 29_943;
    internal const double AirportSectionLengthMeters = 23_833;

    internal static readonly string[] StationIds =
    [
        "O01", "O02", "O03", "O04", "O05", "O06", "O07", "O08", "O08a", "O09", "O10", "O11",
        "O12", "O13", "O14", "O15", "O15a", "O16", "O17", "O18", "O19", "O20", "O21", "O22",
        "O23", "O24", "O25", "O26"
    ];

    // Source-backed station-center chainage (m).  Interstation distances must be
    // derived from this single table; do not introduce a second interval table.
    internal static readonly IReadOnlyDictionary<string, double> StationChainages =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["O01"] = 190, ["O02"] = 1_567, ["O03"] = 2_197, ["O04"] = 4_467,
            ["O05"] = 5_067, ["O06"] = 6_727, ["O07"] = 7_677, ["O08"] = 10_137,
            ["O08a"] = 10_843, ["O09"] = 11_773, ["O10"] = 12_873, ["O11"] = 14_278,
            ["O12"] = 15_348, ["O13"] = 16_208, ["O14"] = 17_053, ["O15"] = 17_737,
            ["O15a"] = 18_878, ["O16"] = 19_450, ["O17"] = 20_728, ["O18"] = 21_943,
            ["O19"] = 23_103, ["O20"] = 24_023, ["O21"] = 24_453, ["O22"] = 25_738,
            ["O23"] = 26_388, ["O24"] = 28_118, ["O25"] = 28_873, ["O26"] = 30_133
        };

    internal static readonly HashSet<string> AirportDirectStops = new(
        ["O01", "O08", "O11", "O16", "O20"],
        StringComparer.OrdinalIgnoreCase);

    public static LargeAirportLineScenarioStages BuildStages()
    {
        var minimal = BuildMinimalBaseline();
        var builder = TopologyScenarioBuilder.FromMinimalBaseline(minimal);

        builder.AddStage(
            TopologyScenarioStage.FullStationChain,
            document => BuildLinearStationChain(document, StationIds),
            smokeDurationSeconds: 30);
        var stationChain = builder.Document;

        builder.AddStage(
            TopologyScenarioStage.ServicePatterns,
            AddServicePatterns,
            smokeDurationSeconds: 30);
        var servicePatterns = builder.Document;

        builder.AddStage(
            TopologyScenarioStage.TurnbackFacilities,
            AddO20PocketTurnback,
            smokeDurationSeconds: 30);
        var turnback = builder.Document;

        builder.AddStage(
            TopologyScenarioStage.PassingFacilities,
            document => AddBidirectionalPassingStation(document, "O04"),
            smokeDurationSeconds: 30);
        var o04Passing = builder.Document;

        builder.AddStage(
            TopologyScenarioStage.PassingFacilities,
            document => AddBidirectionalPassingStation(document, "O13"),
            smokeDurationSeconds: 30);
        var o13Passing = builder.Document;

        builder.AddStage(
            TopologyScenarioStage.OperationalTimetable,
            AddRepresentativeTimetable,
            smokeDurationSeconds: 30);
        var result = builder.Build();

        return new LargeAirportLineScenarioStages(
            minimal,
            stationChain,
            servicePatterns,
            turnback,
            o04Passing,
            o13Passing,
            result.Document,
            result.StageValidations);
    }

    public static TopologyProjectDocument BuildFull() => BuildStages().FullScenario;

    private static TopologyProjectDocument BuildMinimalBaseline()
    {
        var template = StationLayoutTemplateService.Build(StationLayoutTemplateKind.IslandTwoTracks) with
        {
            ProjectId = ProjectId,
            ProjectName = "大型機場線minimal baseline",
            Train = new ProjectTrainSettings(80d / 3.6d, 1.2, 1.5, 30, 30, 30),
            Operations = new ProjectOperationalSettings(1, 0.1, 180, 40d / 3.6d, 0, 100, 1.5, 2, 0.2, 0.2, 1, 5, 5),
            Simulation = new ProjectRunSettings(2, null, 0, 1, OperationProfileMode.RealisticOperations,
                MovingBlockMode.Control, BrakingEstimationMode.Service),
            VehicleTypes = [VehicleType()],
            ServiceTypes = [new ProjectServiceType(FullLineServiceId, "全程車", "#D26523", "F",
                FullLinePatternId, VehicleTypeId)],
            StopPatterns = [Pattern(FullLinePatternId, "minimal 全停", MinimalStationIds(), _ => StopPatternAction.Stop)],
            Dispatch = ManualDispatch(
                Row(0, TrainDirection.Outbound, FullLineServiceId, FullLinePatternId, "O01", "MIN-DOWN", "MIN-DOWN-01"),
                Row(0, TrainDirection.Inbound, FullLineServiceId, FullLinePatternId, "O26", "MIN-UP", "MIN-UP-01"))
        };

        return BuildLinearStationChain(template, MinimalStationIds());
    }

    private static TopologyProjectDocument BuildLinearStationChain(
        TopologyProjectDocument template,
        IReadOnlyList<string> stationIds)
    {
        var inputs = stationIds.Select((stationId, index) => new QuickLinearStationInput(
            stationId,
            StationName(stationId),
            index == 0 ? 0 : DistanceFromPrevious(stationIds, index),
            30));
        var document = ProjectDocumentMapper.BuildQuickLinearProject(template, inputs);
        if (stationIds.Count == StationIds.Length)
        {
            // The full-line sample documents station-centre stopping.  The quick
            // linear mapper intentionally keeps the legacy TrainFront default, so
            // make this sample's semantic choice explicit at its source.
            document = document with
            {
                Topology = document.Topology with
                {
                    Platforms = document.Topology.Platforms
                        .Select(platform => platform with { StopPositionReference = StopPositionReference.TrainCenter })
                        .ToArray()
                }
            };
            // The two terminal destination platforms cannot place a center-referenced
            // train with its head beyond the route boundary.  Keep their centers 50 m
            // before the buffer node so the complete vehicle remains on the physical edge.
            document = MoveTerminalPlatform(document, DownPlatform("O26"));
            document = MoveTerminalPlatform(document, UpPlatform("O01"));
        }
        // SchematicPosition is an existing presentation-only field.  Here it records
        // the source-backed station-center projection; it is never used to infer port
        // sides or physical connectivity.
        document = document with
        {
            Topology = document.Topology with
            {
                Nodes = document.Topology.Nodes.Select(node =>
                {
                    const string prefix = "NODE:";
                    var stationId = node.NodeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        ? node.NodeId[prefix.Length..]
                        : null;
                    return stationId is not null && StationChainages.TryGetValue(stationId, out var chainage)
                        ? node with { SchematicPosition = chainage }
                        : node;
                }).ToArray()
            }
        };
        return document with
        {
            ProjectId = ProjectId,
            ProjectName = stationIds.Count == StationIds.Length
                ? "大型機場線完整營運示範範例"
                : template.ProjectName
        };
    }

    private static TopologyProjectDocument AddServicePatterns(TopologyProjectDocument document)
    {
        var full = Pattern(FullLinePatternId, "全程車全站停靠", StationIds, _ => StopPatternAction.Stop);
        var airportSectionStations = StationIds.Take(StationIndex("O20") + 1).ToArray();
        // Pass means non-stop on the normal mainline in this synthetic scenario.  Keep the
        // optional passing-speed override unset so a higher-priority train is not capped at
        // an unrelated demonstration value before reaching a real passing facility.
        var section = Pattern(SectionPatternId, "區間車 O01－O20（O04 通過越行、O08 後站站停）", airportSectionStations, stationId =>
            StationIndex(stationId) is > 0 and < 7 ? StopPatternAction.Pass : StopPatternAction.Stop,
            dwellSeconds: 10);
        var direct = Pattern(AirportDirectPatternId, "機場直達車 O01/O08/O11/O16/O20 停靠並於 O20 折返", airportSectionStations,
            stationId => AirportDirectStops.Contains(stationId) ? StopPatternAction.Stop : StopPatternAction.Pass,
            dwellSeconds: 20);

        var rows = new[]
        {
            Row(0, TrainDirection.Outbound, FullLineServiceId, FullLinePatternId, "O01", "STAGE2-LOCAL", "STAGE2-LOCAL-DOWN"),
            Row(30, TrainDirection.Inbound, FullLineServiceId, FullLinePatternId, "O26", "STAGE2-UP", "STAGE2-LOCAL-UP"),
            Row(180, TrainDirection.Outbound, AirportDirectServiceId, AirportDirectPatternId, "O01", "STAGE2-DIRECT", "STAGE2-DIRECT-DOWN")
        };

        return document with
        {
            VehicleTypes = [VehicleType()],
            ServiceTypes =
            [
                new ProjectServiceType(FullLineServiceId, "全程車", "#D26523", "F", FullLinePatternId, VehicleTypeId, 0),
                new ProjectServiceType(SectionServiceId, "區間車", "#19607D", "S", SectionPatternId, VehicleTypeId, 5,
                    CanRequestOvertake: true),
                new ProjectServiceType(AirportDirectServiceId, "機場直達車", "#7B3FA1", "A", AirportDirectPatternId,
                    VehicleTypeId, 10, CanRequestOvertake: true)
            ],
            StopPatterns = [full, section, direct],
            Dispatch = ManualDispatch(rows),
            Simulation = document.Simulation with { TrainCount = rows.Length }
        };
    }

    private static TopologyProjectDocument AddO20PocketTurnback(TopologyProjectDocument document)
    {
        var downArrivalEdge = DownEdge("O19", "O20");
        var upDepartureEdge = UpEdge("O20", "O19");
        var downArrivalLength = document.Topology.Edges.Single(edge => edge.TrackEdgeId == downArrivalEdge).LengthMeters;
        document = MovePlatform(document, DownPlatform("O20"), downArrivalEdge, downArrivalLength - 100);
        document = MovePlatform(document, UpPlatform("O20"), upDepartureEdge, 100);

        document = FacilityCreationService.CreatePocketTrack(document, new PocketFacilityRequest(
            "O20 synthetic 站後袋式儲車軌",
            Node("O20"),
            Node("O20"),
            downArrivalEdge,
            upDepartureEdge,
            260,
            25d / 3.6d,
            180,
            DownRouteId,
            UpRouteId,
            30));

        document = document with
        {
            Topology = document.Topology with
            {
                TurnbackFacilities = document.Topology.TurnbackFacilities.Select(facility => facility with
                {
                    ArrivalStopOffsetMeters = downArrivalLength - 100,
                    DepartureStartOffsetMeters = 100
                }).ToArray()
            }
        };

        var operation = document.Topology.TurnbackOperations.Single();
        var stationOperation = new StationOperationDefinition
        {
            StationOperationId = "O20:SECTION-TURNBACK",
            StationId = "O20",
            ArrivalPlatformIds = [DownPlatform("O20")],
            DeparturePlatformIds = [UpPlatform("O20")],
            TurnbackOperationIds = [operation.OperationId],
            DefaultDwellTimeSeconds = 30
        };
        var turnbackPatternIds = new HashSet<string>([SectionPatternId, AirportDirectPatternId], StringComparer.OrdinalIgnoreCase);
        var rows = new[]
        {
            Row(0, TrainDirection.Outbound, SectionServiceId, SectionPatternId, "O01", "SECTION-VEHICLE-01",
                SectionDownRunId, continueAfterTerminal: true, continuationServiceRunId: SectionUpRunId),
            Row(1200, TrainDirection.Inbound, SectionServiceId, SectionPatternId, "O20", "SECTION-VEHICLE-01", SectionUpRunId),
            Row(60, TrainDirection.Inbound, FullLineServiceId, FullLinePatternId, "O26", "STAGE3-UP", "STAGE3-FULL-UP")
        };

        return document with
        {
            StopPatterns = document.StopPatterns.Select(pattern => !turnbackPatternIds.Contains(pattern.Id)
                ? pattern
                : pattern with
                {
                    Instructions = pattern.Instructions.Select(instruction => instruction.StationId == "O20"
                        ? instruction with { Action = StopPatternAction.Turnback, DwellTimeSeconds = 30 }
                        : instruction).ToArray()
                }).ToArray(),
            Dispatch = ManualDispatch(rows),
            Simulation = document.Simulation with { TrainCount = rows.Length },
            Topology = document.Topology with
            {
                StationOperations = document.Topology.StationOperations.Append(stationOperation).ToArray(),
                DirectedConnections = DirectedTrackConnectionRules.Build(document)
            }
        };
    }

    private static TopologyProjectDocument AddBidirectionalPassingStation(
        TopologyProjectDocument document,
        string stationId)
    {
        document = AddDirectionalPassingFacility(document, stationId, TrainDirection.Outbound);
        document = AddDirectionalPassingFacility(document, stationId, TrainDirection.Inbound);
        return document;
    }

    private static TopologyProjectDocument AddDirectionalPassingFacility(
        TopologyProjectDocument document,
        string stationId,
        TrainDirection direction)
    {
        var index = StationIndex(stationId);
        if (index <= 0 || index >= StationIds.Length - 1)
            throw new InvalidOperationException($"{stationId} 不適合建立測試用 passing facility。");

        var directionText = direction == TrainDirection.Outbound ? "下行" : "上行";
        var directionCode = direction == TrainDirection.Outbound ? "DOWN" : "UP";
        var adjacentBeforeArrival = direction == TrainDirection.Outbound ? StationIds[index - 1] : StationIds[index + 1];
        var adjacentAfterDeparture = direction == TrainDirection.Outbound ? StationIds[index + 1] : StationIds[index - 1];
        var originalArrivalEdge = direction == TrainDirection.Outbound
            ? DownEdge(adjacentBeforeArrival, stationId)
            : UpEdge(adjacentBeforeArrival, stationId);
        var departureEdge = direction == TrainDirection.Outbound
            ? DownEdge(stationId, adjacentAfterDeparture)
            : UpEdge(stationId, adjacentAfterDeparture);
        var localPlatformId = direction == TrainDirection.Outbound ? DownPlatform(stationId) : UpPlatform(stationId);
        var expressPlatformId = $"PLATFORM:{stationId}:{directionCode}:THROUGH";
        var routeId = direction == TrainDirection.Outbound ? DownRouteId : UpRouteId;

        // Synthetic geometry: split 200 m before the station node, then add a parallel siding.
        // Local trains follow the service route into that siding and stop there; higher-priority trains
        // use the original mainline edge as the physical passing traversal.
        document = TopologyEditingService.SplitEdge(document, originalArrivalEdge, 200);
        var arrivalEdge = document.Topology.Edges.Single(edge =>
            edge.TrackEdgeId.StartsWith(originalArrivalEdge + ":A", StringComparison.OrdinalIgnoreCase));
        var mainlineStationEdge = document.Topology.Edges.Single(edge =>
            edge.TrackEdgeId.StartsWith(originalArrivalEdge + ":B", StringComparison.OrdinalIgnoreCase));
        var stopOffset = Math.Min(320, mainlineStationEdge.LengthMeters - 20);
        document = MovePlatform(document, localPlatformId, mainlineStationEdge.TrackEdgeId, stopOffset);
        var localPlatform = document.Topology.Platforms.Single(platform => platform.PlatformId == localPlatformId);
        var expressPlatform = localPlatform with
        {
            PlatformId = expressPlatformId,
            Name = $"{stationId} {directionText}正線通過股（synthetic operational marker）",
            // Keep the 720 px overview readable; this is an operational marker, not a passenger platform number.
            PlatformNumber = direction == TrainDirection.Outbound ? "D" : "U",
            TrackEdgeId = mainlineStationEdge.TrackEdgeId,
            AllowsPassengerService = false,
            AllowedServiceTypeIds = new HashSet<string>(
                stationId == "O04" ? [SectionServiceId, AirportDirectServiceId] : [AirportDirectServiceId],
                StringComparer.OrdinalIgnoreCase)
        };
        document = document with
        {
            Topology = document.Topology with
            {
                Platforms = document.Topology.Platforms.Append(expressPlatform).ToArray(),
                Stations = document.Topology.Stations.Select(station => station.StationId == stationId
                    ? station with { PlatformIds = station.PlatformIds.Append(expressPlatformId).ToArray() }
                    : station).ToArray()
            }
        };

        var priorEdgeIds = document.Topology.Edges.Select(edge => edge.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        document = FacilityCreationService.CreatePassingTrack(document, new PassingFacilityRequest(
            $"{stationId} synthetic {directionText}側線待避",
            stationId,
            arrivalEdge.ToNodeId,
            Node(stationId),
            arrivalEdge.TrackEdgeId,
            departureEdge,
            localPlatformId,
            expressPlatformId,
            mainlineStationEdge.LengthMeters,
            80d / 3.6d,
            routeId,
            AirportDirectServiceId));
        var passingEdge = document.Topology.Edges.Single(edge => !priorEdgeIds.Contains(edge.TrackEdgeId));
        // Keep the physical port metadata independent from the drawing, but give the WPF schematic
        // enough lane information to draw a tangential throat instead of a vertical same-X jump.
        // Local sidings stay on the normal outer lanes; the original mainline through tracks use the inner lanes.
        var throughLane = direction == TrainDirection.Outbound ? 0.5d : -0.5d;
        var localLane = direction == TrainDirection.Outbound ? 2d : -2d;
        var createdFacilityId = document.Topology.PassingFacilities.Last().FacilityId;
        document = document with
        {
            ServiceRoutes = document.ServiceRoutes.Select(route => route.ServiceRouteId == routeId
                ? route with
                {
                    Traversals = route.Traversals.Select(traversal =>
                        traversal.TrackEdgeId == mainlineStationEdge.TrackEdgeId
                            ? new DirectedTrackTraversal(passingEdge.TrackEdgeId, traversal.Direction)
                            : traversal).ToArray()
                }
                : route).ToArray(),
            Topology = document.Topology with
            {
                Edges = document.Topology.Edges.Select(edge => edge.TrackEdgeId == mainlineStationEdge.TrackEdgeId
                    ? edge with { SchematicLane = throughLane }
                    : edge.TrackEdgeId == passingEdge.TrackEdgeId
                        ? edge with { Kind = TrackEdgeKind.Siding, SchematicLane = localLane }
                        : edge).ToArray(),
                PassingFacilities = document.Topology.PassingFacilities.Select(facility =>
                    facility.FacilityId == createdFacilityId
                        ? facility with
                        {
                            Name = $"{stationId} synthetic {directionText}正線通過／側線待避",
                            Traversals = [new DirectedTrackTraversal(mainlineStationEdge.TrackEdgeId, TraversalDirection.Forward)]
                        }
                        : facility).ToArray()
            }
        };
        document = MovePlatform(document, localPlatformId, passingEdge.TrackEdgeId, stopOffset);
        var platformSide = direction == TrainDirection.Outbound ? PlatformSide.Below : PlatformSide.Above;
        document = document with
        {
            Topology = document.Topology with
            {
                Platforms = document.Topology.Platforms.Select(platform =>
                    platform.PlatformId == localPlatformId || platform.PlatformId == expressPlatformId
                        ? platform with { DisplaySide = platformSide }
                        : platform).ToArray()
            }
        };

        var passingOperationIds = new List<string> { document.Topology.PassingOperations.Last().OperationId };
        if (stationId == "O04")
        {
            var sectionOperation = new PassingOperationDefinition
            {
                OperationId = $"PASSING:{stationId}:{directionCode}:SECTION",
                FacilityId = document.Topology.PassingFacilities.Last().FacilityId,
                ServiceRouteId = routeId,
                ExpressServiceTypeId = SectionServiceId
            };
            passingOperationIds.Add(sectionOperation.OperationId);
            document = document with
            {
                Topology = document.Topology with
                {
                    PassingOperations = document.Topology.PassingOperations.Append(sectionOperation).ToArray()
                }
            };
        }

        var stationOperation = new StationOperationDefinition
        {
            StationOperationId = $"{stationId}:PASSING:{directionCode}",
            StationId = stationId,
            ArrivalPlatformIds = [localPlatformId, expressPlatformId],
            DeparturePlatformIds = [localPlatformId, expressPlatformId],
            PassingOperationIds = passingOperationIds,
            DefaultDwellTimeSeconds = 30
        };
        var candidate = document with
        {
            Topology = document.Topology with
            {
                StationOperations = document.Topology.StationOperations.Append(stationOperation).ToArray()
            }
        };
        return candidate with
        {
            Topology = candidate.Topology with
            {
                DirectedConnections = DirectedTrackConnectionRules.Build(candidate)
            }
        };
    }

    private static TopologyProjectDocument AddRepresentativeTimetable(TopologyProjectDocument document)
    {
        var fullPattern = Pattern(FullLinePatternId, "全程車全站停靠", StationIds, _ => StopPatternAction.Stop,
            dwellSeconds: 20);
        var o04HoldingPattern = fullPattern with
        {
            Id = O04HoldingPatternId,
            DisplayName = "全程車全停（O04 待避）",
            Instructions = fullPattern.Instructions.Select(instruction => instruction.StationId == "O04"
                ? instruction with { DwellTimeSeconds = 600 }
                : instruction).ToArray()
        };
        var o13HoldingPattern = fullPattern with
        {
            Id = O13HoldingPatternId,
            DisplayName = "全程車全停（O13 待避）",
            Instructions = fullPattern.Instructions.Select(instruction => instruction.StationId switch
            {
                "O04" => instruction with { DwellTimeSeconds = 0 },
                "O13" => instruction with { DwellTimeSeconds = 600 },
                _ => instruction
            }).ToArray()
        };
        var rows = new[]
        {
            // Synthetic off-peak group: DIRECT overtakes one FULL at O04 and another at O13,
            // terminates at O20, then continues back to O01 with the same physical vehicle.
            Row(0, TrainDirection.Outbound, FullLineServiceId, O13HoldingPatternId, "O01", "FULL-O13", "FULL-O13-DOWN"),
            Row(60, TrainDirection.Outbound, FullLineServiceId, O04HoldingPatternId, "O01", "FULL-O04", "FULL-O04-DOWN"),
            Row(500, TrainDirection.Outbound, AirportDirectServiceId, AirportDirectPatternId, "O01", "AIRPORT-DIRECT-01",
                AirportDirectRunId, continueAfterTerminal: true, continuationServiceRunId: AirportDirectUpRunId),
            Row(2600, TrainDirection.Inbound, AirportDirectServiceId, AirportDirectPatternId, "O20", "AIRPORT-DIRECT-01",
                AirportDirectUpRunId),
            Row(30, TrainDirection.Inbound, FullLineServiceId, FullLinePatternId, "O26", "FULL-UP-01", "FULL-UP-01"),
            // Synthetic peak group: SECTION follows and overtakes a separate FULL at O04, then turns at O20.
            Row(2600, TrainDirection.Outbound, FullLineServiceId, O04HoldingPatternId, "O01", "FULL-SECTION-O04",
                "FULL-SECTION-O04-DOWN"),
            Row(3000, TrainDirection.Outbound, SectionServiceId, SectionPatternId, "O01", "SECTION-VEHICLE-01",
                SectionDownRunId, continueAfterTerminal: true, continuationServiceRunId: SectionUpRunId),
            Row(5600, TrainDirection.Inbound, SectionServiceId, SectionPatternId, "O20", "SECTION-VEHICLE-01", SectionUpRunId)
        };

        return document with
        {
            StopPatterns = document.StopPatterns.Where(pattern => pattern.Id != FullLinePatternId)
                .Concat([fullPattern, o04HoldingPattern, o13HoldingPattern]).ToArray(),
            Dispatch = ManualDispatch(rows),
            Simulation = document.Simulation with { TrainCount = rows.Length }
        };
    }

    private static TopologyProjectDocument MovePlatform(
        TopologyProjectDocument document,
        string platformId,
        string edgeId,
        double stopOffset)
    {
        var edgeLength = document.Topology.Edges.Single(edge => edge.TrackEdgeId == edgeId).LengthMeters;
        const double platformLength = 140;
        var platformStart = Math.Clamp(stopOffset - platformLength / 2, 0, Math.Max(0, edgeLength - platformLength));
        var platformEnd = Math.Min(edgeLength, platformStart + platformLength);
        stopOffset = platformStart + (platformEnd - platformStart) / 2;
        return document with
        {
            Topology = document.Topology with
            {
                Platforms = document.Topology.Platforms.Select(platform => platform.PlatformId == platformId
                    ? platform with
                    {
                        TrackEdgeId = edgeId,
                        PlatformStartOffsetMeters = platformStart,
                        PlatformEndOffsetMeters = platformEnd,
                        StopPositionOffsetMeters = stopOffset,
                        EffectiveLengthMeters = platformEnd - platformStart
                    }
                    : platform).ToArray()
            }
        };
    }

    private static TopologyProjectDocument MoveTerminalPlatform(
        TopologyProjectDocument document,
        string platformId)
    {
        var platform = document.Topology.Platforms.Single(item => item.PlatformId == platformId);
        var edgeLength = document.Topology.Edges.Single(edge => edge.TrackEdgeId == platform.TrackEdgeId).LengthMeters;
        const double platformLength = 100;
        var start = Math.Max(0, edgeLength - platformLength);
        var end = edgeLength;
        return document with
        {
            Topology = document.Topology with
            {
                Platforms = document.Topology.Platforms.Select(item => item.PlatformId == platformId
                    ? item with
                    {
                        PlatformStartOffsetMeters = start,
                        PlatformEndOffsetMeters = end,
                        StopPositionOffsetMeters = start + platformLength / 2,
                        EffectiveLengthMeters = end - start
                    }
                    : item).ToArray()
            }
        };
    }

    private static ProjectVehicleType VehicleType() => new(
        VehicleTypeId, "示範電聯車", 100, 80d / 3.6d, 1.2, 1.5, 2, 1, 0, 0, FullLinePatternId);

    private static ProjectStopPattern Pattern(
        string id,
        string name,
        IReadOnlyList<string> stationIds,
        Func<string, StopPatternAction> action,
        double? passingSpeedMetersPerSecond = null,
        double dwellSeconds = 30) => new(
            id,
            name,
            stationIds.Select(stationId => new ProjectStopPatternInstruction(
                stationId,
                action(stationId),
                action(stationId) == StopPatternAction.Pass ? null : dwellSeconds,
                action(stationId) == StopPatternAction.Pass ? passingSpeedMetersPerSecond : null)).ToArray());

    private static ProjectDispatchPlan ManualDispatch(params ProjectManualTimetableRow[] rows) =>
        new(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [], rows);

    private static ProjectManualTimetableRow Row(
        double departureSeconds,
        TrainDirection direction,
        string serviceTypeId,
        string stopPatternId,
        string originStationId,
        string vehicleId,
        string serviceRunId,
        bool continueAfterTerminal = false,
        string? continuationServiceRunId = null) => new(
            departureSeconds,
            direction,
            serviceTypeId,
            VehicleTypeId,
            stopPatternId,
            direction == TrainDirection.Outbound ? DownPlatform(originStationId) : UpPlatform(originStationId),
            vehicleId,
            serviceRunId,
            continueAfterTerminal,
            continuationServiceRunId);

    private static double DistanceFromPrevious(IReadOnlyList<string> stationIds, int index) =>
        SourceChainage(stationIds[index]) - SourceChainage(stationIds[index - 1]);

    internal static double SourceChainage(string stationId) =>
        StationChainages.TryGetValue(stationId, out var chainage)
            ? chainage
            : throw new InvalidOperationException($"未知車站 {stationId}。");

    private static int StationIndex(string stationId)
    {
        var index = Array.FindIndex(StationIds, item => item.Equals(stationId, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new InvalidOperationException($"未知車站 {stationId}。");
    }

    private static string[] MinimalStationIds() => ["O01", "O08", "O11", "O16", "O20", "O26"];
    private static string StationName(string id) => id switch
    {
        "O01" => "O01 機場端",
        "O16" => "O16 中央轉乘站",
        _ => $"{id} 站"
    };
    private static string Node(string stationId) => $"NODE:{stationId}";
    private static string DownEdge(string from, string to) => $"EDGE:DOWN:{from}:{to}";
    private static string UpEdge(string from, string to) => $"EDGE:UP:{from}:{to}";
    private static string DownPlatform(string stationId) => $"PLATFORM:{stationId}:DOWN";
    private static string UpPlatform(string stationId) => $"PLATFORM:{stationId}:UP";
}
