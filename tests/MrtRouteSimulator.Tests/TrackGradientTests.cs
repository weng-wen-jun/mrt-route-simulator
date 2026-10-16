using MrtRouteSimulator.Engine;

internal static class TrackGradientTests
{
    public static void GradeDirectionAndFutureBounds()
    {
        var document = Load();
        var edge = document.Topology.Edges.First() with { Directionality = TrackDirectionality.Bidirectional };
        var graph = new InfrastructureGraphV4(document.Topology with
        {
            Edges = document.Topology.Edges.Select(item => item.TrackEdgeId == edge.TrackEdgeId ? edge : item).ToArray(),
            Gradients = [new("G", edge.TrackEdgeId, edge.LengthMeters * 0.5, edge.LengthMeters, -20)]
        });
        var service = new TrackGradientService(graph);
        var navigator = new TopologyMovementNavigator(graph, new ResolvedMovementPlan
        {
            MovementPlanId = "GRADE",
            Legs = [new() { LegId = "ONE", Traversals = [new(edge, TraversalDirection.Forward)] }]
        });
        var start = navigator.CreateStartCursor();
        Assert(service.GetGradePermille(start) == 0, "Unspecified interval must remain flat.");
        Assert(service.GetForwardGradeBounds(navigator, start).Min == -20,
            "A downhill interval must affect braking before the train enters it.");
        var forward = navigator.CreateCursor(0, 0, edge.LengthMeters * 0.75);
        Assert(service.GetGradePermille(forward) == -20, "Forward traversal must preserve edge grade sign.");
        Assert(service.GetGradePermille(forward with { Direction = TraversalDirection.Reverse }) == 20,
            "Reverse traversal must negate the edge-local grade.");
        var reverseNavigator = new TopologyMovementNavigator(graph, new ResolvedMovementPlan
        {
            MovementPlanId = "REVERSE",
            Legs = [new() { LegId = "ONE", Traversals = [new(edge, TraversalDirection.Reverse)] }]
        });
        Assert(service.GetForwardGradeBounds(reverseNavigator, reverseNavigator.CreateStartCursor()).Max == 20,
            "Leader stopping credit must account for the strongest reachable uphill brake.");
    }

    public static void RuntimeGradesAffectTractionAndStops()
    {
        var flat = CreateWorld(0);
        var uphill = CreateWorld(20);
        var downhill = CreateWorld(-20);
        flat.AdvanceTo(5);
        uphill.AdvanceTo(5);
        downhill.AdvanceTo(5);
        var flatSpeed = flat.GetSnapshot().Trains.First(item => item.Direction == TrainDirection.Outbound).SpeedMetersPerSecond;
        var upSpeed = uphill.GetSnapshot().Trains.First(item => item.Direction == TrainDirection.Outbound).SpeedMetersPerSecond;
        var downSpeed = downhill.GetSnapshot().Trains.First(item => item.Direction == TrainDirection.Outbound).SpeedMetersPerSecond;
        Assert(upSpeed < flatSpeed && flatSpeed < downSpeed, "Physical grade must alter actual traction, not just analysis metadata.");
        foreach (var world in new[] { flat, uphill, downhill })
        {
            world.AdvanceTo(900);
            Assert(world.IsComplete, "Graded trains must complete their route.");
            Assert(!world.Events.Any(item => item.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation),
                "Grade-aware stopping and safety must avoid collision and stop violations.");
        }
    }

    public static void FlatGradePreservesRuntime()
    {
        var absent = CreateWorld(grade: null);
        var explicitFlat = CreateWorld(0);
        absent.AdvanceTo(900);
        explicitFlat.AdvanceTo(900);
        Assert(absent.Events.SequenceEqual(explicitFlat.Events), "Explicit zero grade must preserve all original events and timings.");
    }

    public static void DenseDownhillTransitionsRemainSafe()
    {
        var document = Load();
        var rows = document.Dispatch.ManualTimetableRows!.SelectMany(template => Enumerable.Range(0, 4).Select(index => template with
        {
            PlannedDepartureTimeSeconds = index * 30,
            VehicleId = $"GRADE-{template.Direction}-{index}",
            ServiceRunId = $"GRADE-{template.Direction}-{index}"
        })).ToArray();
        document = document with
        {
            Dispatch = new(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [], rows),
            Topology = document.Topology with
            {
                Gradients = document.Topology.Edges.SelectMany(edge => new[]
                {
                    new TrackGradientSegment("UP-" + edge.TrackEdgeId, edge.TrackEdgeId, 0, edge.LengthMeters * 0.4, 10),
                    new TrackGradientSegment("DOWN-" + edge.TrackEdgeId, edge.TrackEdgeId, edge.LengthMeters * 0.4, edge.LengthMeters, -30)
                }).ToArray()
            }
        };
        var world = CreateWorld(document);
        world.AdvanceTo(1200);
        Assert(world.IsComplete, "Dense trains crossing uphill/downhill boundaries must complete.");
        Assert(!world.Events.Any(item => item.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation),
            "A future downhill must not invalidate moving-block or station-stop protection.");
        foreach (var row in rows)
            Assert(world.Events.Any(item => item.ServiceRunId == row.ServiceRunId
                && item.EventType == SimulationEventType.Arrival && item.StationId == "B"),
                "Every graded dense run must actually stop at the middle station.");
    }

    private static SimulationWorld CreateWorld(double? grade)
    {
        var document = Load();
        document = document with
        {
            Topology = document.Topology with
            {
                Gradients = grade is null ? [] : document.Topology.Edges.Select(edge =>
                    new TrackGradientSegment("GRADE-" + edge.TrackEdgeId, edge.TrackEdgeId, 0, edge.LengthMeters, grade.Value)).ToArray()
            }
        };
        return CreateWorld(document);
    }

    private static SimulationWorld CreateWorld(TopologyProjectDocument document)
    {
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        return new SimulationWorldOptions(Route: null, TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters, TrainCount: runtime.DispatchPlan.Runs.Count,
            ProfileMode: document.Simulation.ProfileMode, MovingBlockMode: MovingBlockMode.Control,
            ServicePatterns: runtime.ServicePatterns, DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes, ServiceTypes: runtime.ServiceTypes, Topology: runtime.Topology,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.EventsOnly).CreateWorld();
    }

    private static TopologyProjectDocument Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        return TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root, "samples", "小型",
            "01-小型-三站雙股島式站場範例.mrtsim.json")));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
