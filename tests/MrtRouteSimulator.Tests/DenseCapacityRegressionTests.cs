using MrtRouteSimulator.Engine;

internal static class DenseCapacityRegressionTests
{
    public static void DenseArrivalsResumeOutsidePrecisionSnap()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"MrtRouteSimulator.slnx"))) dir=dir.Parent;
        var root=dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
        var doc=TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,"samples","小型","01-小型-三站雙股島式站場範例.mrtsim.json")));
        var runs=doc.Dispatch.ManualTimetableRows!.SelectMany(template=>Enumerable.Range(0,4).Select(i=>template with {
            PlannedDepartureTimeSeconds=i*30,VehicleId=$"{template.Direction}-{i}",ServiceRunId=$"{template.Direction}-{i}"
        })).ToArray();
        doc=doc with {Dispatch=new(DispatchPlanningMode.ManualTimetable,VehicleAssignmentMode.Automatic,[],runs)};
        var runtime=TopologyProjectFormat.CreateRuntime(doc);
        var world=new SimulationWorldOptions(Route:null,TrainParameters:runtime.TrainParameters,OperationalParameters:runtime.OperationalParameters,
            TrainCount:runs.Length,ProfileMode:doc.Simulation.ProfileMode,MovingBlockMode:MovingBlockMode.Control,
            ServicePatterns:runtime.ServicePatterns,DispatchPlan:runtime.DispatchPlan,VehicleTypes:runtime.VehicleTypes,
            ServiceTypes:runtime.ServiceTypes,Topology:runtime.Topology,TraceRetentionPolicy:SimulationTraceRetentionPolicy.EventsOnly).CreateWorld();
        world.AdvanceTo(900);
        if(!world.IsComplete) throw new InvalidOperationException("30秒密班距列車在前車離開後，仍停於精停容許區外且不能完成運行。");
        foreach(var run in runtime.DispatchPlan.Runs)
        {
            if(!world.Events.Any(e=>e.ServiceRunId==run.ServiceRunId && e.EventType==SimulationEventType.Arrival && e.StationId=="B"))
                throw new InvalidOperationException(run.ServiceRunId+"未實際抵達中間站。");
        }
        if(world.Events.Any(e=>e.EventType is SimulationEventType.Collision or SimulationEventType.StationStopViolation))
            throw new InvalidOperationException("密班距精停復歸不得造成碰撞或停站違規。");
    }
}
