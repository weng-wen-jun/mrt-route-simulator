using MrtRouteSimulator.Engine;

/// <summary>
/// Capacity handbook turnback cases backed by physical Schema 8 topologies.
///
/// These fixtures intentionally stay outside the Engine and existing samples.  The
/// capacity point is the handbook calculation input; the document is the executable
/// topology input.  The two lists on <see cref="TurnbackFixture"/> make the boundary
/// explicit when the experimental runner compares the two.
/// </summary>
public sealed record TurnbackServiceBinding(
    string FacilityId,
    string ArrivalServiceRouteId,
    string DepartureServiceRouteId);

public sealed record TurnbackFixture(
    string Name,
    string HandbookCase,
    TopologyProjectDocument Document,
    ProjectSpatialReferencePoint CapacityPoint,
    SpatialCapacityResult Theory,
    string MeasuredStationId,
    TrainDirection MeasuredArrivalDirection,
    IReadOnlyList<TurnbackServiceBinding> TurnbackBindings,
    IReadOnlyList<string> EnforcedParameters,
    IReadOnlyList<string> UnsupportedParameters);

/// <summary>
/// Builds small, isolated physical turnback cases for the capacity experiment.
/// The <paramref name="root"/> argument is accepted for parity with the experiment
/// runner and is validated, but no source sample is read or modified.
/// </summary>
public static class TurnbackFixtures
{
    public static IReadOnlyList<TurnbackFixture> Build(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("實驗根目錄不可為空白。", nameof(root));

        var repositoryRoot = Path.GetFullPath(root);
        if (!Directory.Exists(repositoryRoot))
            throw new DirectoryNotFoundException($"找不到實驗根目錄：{repositoryRoot}");

        return
        [
            BuildStationFrontCase02(),
            BuildStationFrontCase05(),
            BuildRearCase07(singleFacility: true, turnbackDwellSeconds: 120, variant: "07"),
            BuildRearCase07(singleFacility: false, turnbackDwellSeconds: 120, variant: "08"),
            BuildRearCase07(singleFacility: true, turnbackDwellSeconds: 35, variant: "08b"),
            BuildRearCase07(singleFacility: true, turnbackDwellSeconds: 33, variant: "08c")
        ];
    }

    private static TurnbackFixture BuildStationFrontCase02()
    {
        var global = new SpatialCapacityGlobalParameters(15, 1, .8, .3);
        var train = new SpatialCapacityTrainParameters(140, 1, 1, .75, 350, 260, 5);
        var point = new ProjectSpatialReferencePoint(
            "CASE02",
            "A",
            "範例二站前交替折返",
            SpatialReferencePointKind.BeforeStationTurnback,
            AlternateBerthing: true,
            MainlineGradePermille: 5,
            DistanceFromStopToCrossoverMeters: 15,
            CrossoverLengthMeters: 170,
            TurnbackDwellSeconds: 150,
            MainlineApproachCruiseSpeedMetersPerSecond: 65 / 3.6,
            MainlineSafetyFactor: 3.7);

        var topology = PhysicalTemplate(StationLayoutTemplateKind.FrontTurnback, point, keepSecondFacility: true, train.LengthMeters);
        var document = ApplyRuntimeParameters(
            topology.Document,
            topology.Point,
            global,
            train,
            point.MainlineApproachCruiseSpeedMetersPerSecond,
            point.TurnbackDwellSeconds,
            "CASE02-STATION-FRONT");

        return CreateFixture(
            "case02-station-front",
            "02-範例二-站前交替折返",
            document,
            point,
            global,
            train,
            "A",
            [
                new("TURN", "UP", "DOWN"),
                new("TURN:PLATFORM2", "UP:PLATFORM2", "DOWN:PLATFORM2")
            ],
            [
                "handbook global: interchange=15 s, reaction=1 s, PHF=0.8, margin=0.3",
                "handbook train: length=140 m, acceleration=1 m/s², deceleration=1 m/s², brake effect=0.75",
                "runtime vehicle and operational train length/acceleration/deceleration",
                "runtime TURN minimum dwell=150 s",
                "runtime crossover X1 length=170 m and speed=40 km/h",
                "runtime explicit TURN and TURN:PLATFORM2 operation pairs with separate berth resources; X1/X2 share an explicit synthetic flat diamond protected by node reservations"
            ],
            [
                "runtime applies explicit crossover rear-clear/interchange delay and braking safety factor; service brake uses nominal deceleration × BrakeEffect; PHF/passenger capacity/margin remain analytical demand/design terms, not runtime throttles",
                "the handbook alternate-berthing value does not provide a surveyed facility geometry; the two executable station-front facilities are the template's TURN and TURN:PLATFORM2"
            ]);
    }

    private static TurnbackFixture BuildStationFrontCase05()
    {
        var global = new SpatialCapacityGlobalParameters();
        var train = new SpatialCapacityTrainParameters();
        var point = new ProjectSpatialReferencePoint(
            "CASE05",
            "A",
            "範例五臨時站前折返參考點",
            SpatialReferencePointKind.BeforeStationTurnback,
            DistanceFromStopToCrossoverMeters: 30,
            CrossoverLengthMeters: 85,
            TurnbackDwellSeconds: 120,
            MainlineApproachCruiseSpeedMetersPerSecond: 80 / 3.6,
            MainlineSafetyFactor: 1);

        var topology = PhysicalTemplate(StationLayoutTemplateKind.FrontTurnback, point, keepSecondFacility: false, train.LengthMeters);
        var document = ApplyRuntimeParameters(
            topology.Document,
            topology.Point,
            global,
            train,
            point.MainlineApproachCruiseSpeedMetersPerSecond,
            point.TurnbackDwellSeconds,
            "CASE05-STATION-FRONT");

        return CreateFixture(
            "case05-station-front",
            "05-範例五-臨時折返參考點",
            document,
            point,
            global,
            train,
            "A",
            [new("TURN", "UP", "DOWN")],
            [
                "handbook default global capacity parameters",
                "handbook default train: length=141 m, acceleration=1 m/s², deceleration=1 m/s², brake effect=0.75",
                "runtime vehicle and operational train length/acceleration/deceleration",
                "runtime TURN minimum dwell=120 s",
                "runtime crossover X1 length=85 m and speed=40 km/h",
                "runtime only TURN and TURN:BERTH are retained; the inactive TURN:PLATFORM2 facility and routes are removed; X1/X2 retain an explicit synthetic flat diamond"
            ],
            [
                "runtime applies explicit crossover rear-clear/interchange delay and braking safety factor; service brake uses nominal deceleration × BrakeEffect; PHF/passenger capacity/margin remain analytical demand/design terms, not runtime throttles",
                "the handbook's 1.8 km route sketch is not used as an asserted physical route length"
            ]);
    }

    private static TurnbackFixture BuildRearCase07(bool singleFacility, double turnbackDwellSeconds, string variant)
    {
        var global = new SpatialCapacityGlobalParameters();
        var train = new SpatialCapacityTrainParameters(140, 1, 1, .75, 300, 250, 6);
        var label = variant switch
        {
            "07" => "S14站後單尾軌折返",
            "08" => "S14站後雙尾軌折返",
            "08b" => "S14站後單尾軌折返（停等35秒）",
            "08c" => "S14站後單尾軌折返（停等33秒）",
            _ => $"S14站後折返（{variant}）"
        };
        var point = new ProjectSpatialReferencePoint(
            $"CASE{variant.ToUpperInvariant()}-S14",
            "A",
            label,
            SpatialReferencePointKind.AfterStationTurnback,
            AlternateBerthing: !singleFacility,
            MainlineGradePermille: 2,
            DistanceFromStopToCrossoverMeters: 20,
            CrossoverLengthMeters: 160,
            DistanceFromCrossoverToTurnbackStopMeters: 15,
            TurnbackDwellSeconds: turnbackDwellSeconds);

        var topology = PhysicalTemplate(StationLayoutTemplateKind.RearTurnback, point, keepSecondFacility: !singleFacility, train.LengthMeters);
        var document = ApplyRuntimeParameters(
            topology.Document,
            topology.Point,
            global,
            train,
            point.MainlineApproachCruiseSpeedMetersPerSecond,
            point.TurnbackDwellSeconds,
            $"CASE{variant.ToUpperInvariant()}-S14-REAR-{(singleFacility ? "SINGLE" : "DOUBLE")}");

        IReadOnlyList<TurnbackServiceBinding> bindings = singleFacility
            ? [new TurnbackServiceBinding("TURN", "UP", "DOWN")]
            : [
                new TurnbackServiceBinding("TURN", "UP", "DOWN"),
                new TurnbackServiceBinding("TURN:TAIL2", "UP:TAIL2", "DOWN:TAIL2")
            ];

        return CreateFixture(
            variant switch
            {
                "07" => "case07-s14-rear-single",
                "08" => "case08-s14-rear-double",
                "08b" => "case08b-s14-rear-single-35s",
                "08c" => "case08c-s14-rear-single-33s",
                _ => $"case{variant}-s14-rear-{(singleFacility ? "single" : "double")}"
            },
            variant switch
            {
                "07" => "07-範例七-S14單尾軌",
                "08" => "08-範例八-S14雙尾軌改善",
                "08b" => "08b-範例八-S14單尾軌停等35秒",
                "08c" => "08c-範例八-S14單尾軌停等33秒",
                _ => $"{variant}-S14折返"
            },
            document,
            point,
            global,
            train,
            "A",
            TrainDirection.Inbound,
            bindings,
            [
                "handbook global default parameters",
                $"handbook S14 grade=2 permille, crossover length=160 m, crossover-to-turnback-stop=15 m, turnback dwell={turnbackDwellSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} s",
                "handbook train: length=140 m, acceleration=1 m/s², deceleration=1 m/s², brake effect=0.75",
                "runtime vehicle and operational train length/acceleration/deceleration",
                "runtime X1/X2 crossover total length=160 m each and speed=40 km/h; each is split at an explicit shared synthetic flat diamond protected by node reservations",
                "runtime symmetric physical rear facility: TU2@155 / TD2@25 on 180 m tails; both heads penetrate 155 m and both reversed heads clear the far crossover boundary by 15 m",
                singleFacility
                    ? "one active TURN operation with independent TURN:BERTH resource"
                    : "two active operations: TURN and TURN:TAIL2 with independent TURN:BERTH and TURN:TAIL2:BERTH resources"
            ],
            [
                "runtime applies explicit crossover rear-clear/interchange delay and braking safety factor; service brake uses nominal deceleration × BrakeEffect; PHF/passenger capacity/margin remain analytical demand/design terms, not runtime throttles",
                singleFacility ? "the second tail facility is removed from active facilities and operations" : "double-facility throughput requires the runner to dispatch UP/UP:TAIL2 and pair DOWN/DOWN:TAIL2 explicitly; TURN:TAIL2 uses a mirrored tail with the same 15 m reverse-head clearance as TURN"
            ]);
    }

    private static (TopologyProjectDocument Document, ProjectSpatialReferencePoint Point) PhysicalTemplate(
        StationLayoutTemplateKind kind,
        ProjectSpatialReferencePoint point,
        bool keepSecondFacility, double trainLengthMeters)
    {
        var source = StationLayoutTemplateService.Build(kind);
        var topology = source.Topology;

        var crossoverSpeed = point.SwitchSpeedLimitMetersPerSecond;
        var crossoverIds = kind == StationLayoutTemplateKind.FrontTurnback
            ? new[] { "X1", "X2" }
            : new[] { "X1", "X2" };
        var crossoverLength = point.CrossoverLengthMeters;
        var edges = topology.Edges.Select(edge =>
        {
            var updated = edge with
            {
                // TURN used to be attached to every tail/crossover edge, which
                // serialized both physical facilities behind one global lock.
                // Switch-node reservations and directed connections remain the
                // topology authority; only the facility berth is explicit here.
                ConflictResourceIds = edge.ConflictResourceIds
                    .Where(resourceId => !resourceId.Equals("TURN", StringComparison.OrdinalIgnoreCase))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            };
            if (!crossoverIds.Contains(edge.TrackEdgeId, StringComparer.OrdinalIgnoreCase)) return updated;
            return updated with { LengthMeters = crossoverLength, DefaultSpeedLimitMetersPerSecond = crossoverSpeed };
        }).ToArray();

        var secondaryFacilityId = kind == StationLayoutTemplateKind.RearTurnback
            ? "TURN:TAIL2"
            : kind == StationLayoutTemplateKind.FrontTurnback
                ? "TURN:PLATFORM2"
                : null;
        var facilities = topology.TurnbackFacilities
            .Where(facility => keepSecondFacility
                || secondaryFacilityId is null
                || !facility.FacilityId.Equals(secondaryFacilityId, StringComparison.OrdinalIgnoreCase))
            .Select(facility =>
            {
                var updated = facility;
                if (kind == StationLayoutTemplateKind.RearTurnback && facility.FacilityId.Equals("TURN", StringComparison.OrdinalIgnoreCase))
                    updated = facility with { TurnbackStopPosition = new TrackPosition("TU2", 185) };
                // Physical stop geometry is aligned after resource/route filtering.
                var berthResourceId = BerthResourceId(updated.FacilityId);
                return updated with
                {
                    ConflictResourceIds = new HashSet<string>([berthResourceId], StringComparer.OrdinalIgnoreCase)
                };
            }).ToArray();

        var operationIds = facilities.Select(facility => facility.FacilityId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var operations = topology.TurnbackOperations
            .Where(operation => operationIds.Contains(operation.FacilityId))
            .Select(operation => operation with { MinimumDwellTimeSeconds = point.TurnbackDwellSeconds })
            .ToArray();
        var stationOperations = topology.StationOperations
            .Where(operation => operation.TurnbackOperationIds.Any(operationIds.Contains))
            .Select(operation => operation with
            {
                TurnbackOperationIds = operation.TurnbackOperationIds.Where(operationIds.Contains).ToArray()
            })
            .Where(operation => operation.TurnbackOperationIds.Count > 0)
            .ToArray();

        var resources = topology.Resources
            .Where(resource => !resource.ResourceId.Equals("TURN", StringComparison.OrdinalIgnoreCase))
            .Concat(facilities.Select(facility => new ConflictResourceDefinition(
                BerthResourceId(facility.FacilityId),
                $"{facility.Name}獨立停等資源",
                facility.Kind == TurnbackFacilityKind.TailTrack
                    ? ConflictResourceKind.TailTrack
                    : ConflictResourceKind.Crossover)))
            .ToArray();

        topology = topology with
        {
            Edges = edges,
            Resources = resources,
            TurnbackFacilities = facilities,
            TurnbackOperations = operations,
            StationOperations = stationOperations
        };

        // The template already contains explicit, topology-derived directed
        // connections.  Changing only edge geometry and removing an unused
        // facility does not add a new movement permission.
        var secondaryRouteIds = kind == StationLayoutTemplateKind.RearTurnback
            ? new HashSet<string>(["UP:TAIL2", "DOWN:TAIL2"], StringComparer.OrdinalIgnoreCase)
            : kind == StationLayoutTemplateKind.FrontTurnback
                ? new HashSet<string>(["UP:PLATFORM2", "DOWN:PLATFORM2"], StringComparer.OrdinalIgnoreCase)
                : [];
        var routes = !keepSecondFacility && secondaryRouteIds.Count > 0
            ? source.ServiceRoutes.Where(route => !secondaryRouteIds.Contains(route.ServiceRouteId)).ToArray()
            : source.ServiceRoutes;
        var document = source with
        {
            Topology = topology,
            ServiceRoutes = routes,
            ProjectId = "TURNBACK-FIXTURE",
            ProjectName = $"{point.Name}（實體容量實驗 fixture）"
        };
        document = TurnbackGeometryBuilder.Align(document, kind, point, trainLengthMeters);
        document = CrossingFixtureBuilder.AddSharedDiamond(document);
        document = TurnbackGeometryBuilder.ApplyGrades(document, kind, point.MainlineGradePermille);
        return (document, point);
    }

    private static string BerthResourceId(string facilityId) => $"{facilityId}:BERTH";

    private static TopologyProjectDocument ApplyRuntimeParameters(
        TopologyProjectDocument source,
        ProjectSpatialReferencePoint point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters capacityTrain,
        double cruiseSpeed,
        double turnbackDwell,
        string projectId)
    {
        var vehicleTypes = source.VehicleTypes.Select(vehicle => vehicle with
        {
            LengthMeters = capacityTrain.LengthMeters,
            MaxSpeedMetersPerSecond = cruiseSpeed,
            AccelerationMetersPerSecondSquared = capacityTrain.AccelerationMetersPerSecondSquared,
            ServiceBrakeDecelerationMetersPerSecondSquared = capacityTrain.DecelerationMetersPerSecondSquared * capacityTrain.BrakeEffect,
            EmergencyBrakeDecelerationMetersPerSecondSquared = capacityTrain.DecelerationMetersPerSecondSquared,
            JerkMetersPerSecondCubed = source.Operations.JerkMetersPerSecondCubed
        }).ToArray();
        var maxSpeed = cruiseSpeed;
        var topology = source.Topology with
        {
            Edges = source.Topology.Edges.Select(edge => edge.Kind is TrackEdgeKind.Mainline
                ? edge with { DefaultSpeedLimitMetersPerSecond = maxSpeed }
                : edge).ToArray(),
            TurnbackOperations = source.Topology.TurnbackOperations.Select(operation =>
                operation with { MinimumDwellTimeSeconds = turnbackDwell }).ToArray()
        };
        var document = source with
        {
            ProjectId = projectId,
            ProjectName = source.ProjectName,
            Train = source.Train with
            {
                MaxSpeedMetersPerSecond = maxSpeed,
                AccelerationMetersPerSecondSquared = capacityTrain.AccelerationMetersPerSecondSquared,
                DecelerationMetersPerSecondSquared = capacityTrain.DecelerationMetersPerSecondSquared,
                TerminalTurnaroundTimeSeconds = turnbackDwell
            },
            Operations = source.Operations with
            {
                TrainLengthMeters = capacityTrain.LengthMeters,
                ApproachSpeedMetersPerSecond = cruiseSpeed,
                ServiceBrakingMetersPerSecondSquared = capacityTrain.DecelerationMetersPerSecondSquared * capacityTrain.BrakeEffect,
                EmergencyBrakingMetersPerSecondSquared = capacityTrain.DecelerationMetersPerSecondSquared,
                ControlReactionTimeSeconds = global.ReactionTimeSeconds
            },
            VehicleTypes = vehicleTypes,
            StopPatterns = source.StopPatterns.Select(pattern=>pattern with {
                Instructions = pattern.Instructions.Select(instruction=>instruction.StationId==point.StationId ? instruction with { DwellTimeSeconds=0 } : instruction).ToArray()
            }).ToArray(),
            Simulation = source.Simulation with { TrainCount = source.Dispatch.ManualTimetableRows?.Length ?? source.Simulation.TrainCount },
            Topology = topology,
            SpatialCapacity = new SpatialCapacityProjectSettings(
                global,
                capacityTrain,
                [point],
                SpatialCapacityCalculationMode.TaiwanHandbook,
                CompareAsSingleScenario: true)
        };
        document = TurnbackSignalMapper.Apply(document, global.InterchangeTimeSeconds, point.MainlineSafetyFactor);
        TopologyProjectFormat.Validate(document);
        return document;
    }

    private static TurnbackFixture CreateFixture(
        string name,
        string handbookCase,
        TopologyProjectDocument document,
        ProjectSpatialReferencePoint point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train,
        string measuredStationId,
        IReadOnlyList<TurnbackServiceBinding> bindings,
        IReadOnlyList<string> enforced,
        IReadOnlyList<string> unsupported) =>
        CreateFixture(
            name,
            handbookCase,
            document,
            point,
            global,
            train,
            measuredStationId,
            TrainDirection.Inbound,
            bindings,
            enforced,
            unsupported);

    private static TurnbackFixture CreateFixture(
        string name,
        string handbookCase,
        TopologyProjectDocument document,
        ProjectSpatialReferencePoint point,
        SpatialCapacityGlobalParameters global,
        SpatialCapacityTrainParameters train,
        string measuredStationId,
        TrainDirection measuredArrivalDirection,
        IReadOnlyList<TurnbackServiceBinding> bindings,
        IReadOnlyList<string> enforced,
        IReadOnlyList<string> unsupported)
    {
        var theory = SpatialCapacityAnalysis.Calculate(
            SpatialCapacityProjectAnalysis.ToDefinition(point),
            global,
            train,
            SpatialCapacityDirection.Forward,
            SpatialCapacityCalculationMode.TaiwanHandbook);
        return new(name, handbookCase, document, point, theory, measuredStationId,
            measuredArrivalDirection, bindings, enforced.Concat(unsupported.Where(note => note.StartsWith("runtime applies", StringComparison.Ordinal))).Concat(new[] {
                "實驗停等配置：手冊T全部放在facility minimum dwell；A站去程及回程一般停站均設0秒，避免額外疊加載體30+30秒。",
                $"runtime geometry: station-to-crossover train-end clearance={point.DistanceFromStopToCrossoverMeters:0.###} m; all straight/diagonal crossover paths={point.CrossoverLengthMeters:0.###} m; train length={train.LengthMeters:0.###} m",
                point.Kind == SpatialReferencePointKind.AfterStationTurnback
                    ? $"runtime geometry: both tail arrival-rear / reversed-head clearances={point.DistanceFromCrossoverToTurnbackStopMeters:0.###} m; 25 m synthetic buffer beyond incoming head"
                    : "runtime geometry: station-front s_p gap is measured from departure head / arrival rear; platform stop stores the train centre",
                $"runtime gradient: {document.Topology.Gradients.Count} explicit edge-local full-edge segments, sign reversed by traversal; handbook local grade={point.MainlineGradePermille:0.###} permille"
            }).ToArray(), unsupported.Where(note => !note.StartsWith("runtime applies", StringComparison.Ordinal)).Concat(new[] {
                "geometry assumption: s_p1/s_p2 interpreted as nearest-train-end clearances, consistent with primary handbook metropolitan chapter 2 pp.2-15/2-25, formulas (2.23)/(2.42), which add L separately; platform effective length equals train length and 25 m tail buffer are synthetic, not surveyed",
                point.Kind == SpatialReferencePointKind.AfterStationTurnback
                    ? "gradient extent assumption: handbook +2 permille tail-to-station exit grade is extended over D0/U0/crossover/tails; opposite movement is -2; no surveyed profile extent supplied"
                    : "gradient extent assumption: handbook front approach grade extends over U1/U0/UC/crossovers and corresponding D1/D0/DC return edges; opposite movement changes sign; no surveyed profile extent supplied",
                "local three-station operational fixture only; upstream station spacing, platform length, unmodelled signal blocks and facility drawing coordinates are synthetic and do not reconstruct the full handbook route"
            }).ToArray());
    }
}
