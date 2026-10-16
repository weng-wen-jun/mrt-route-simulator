using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

public static class HandbookNetworkExperiment
{
    private const string Version="handbook-network-v1-circulating-fleet";
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter()}};
    private static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private static void Write(string path,object value)=>File.WriteAllText(path,JsonSerializer.Serialize(value,Json));

    public static SimulationWorld World(TopologyProjectDocument doc)
    {
        var rt=TopologyProjectFormat.CreateRuntime(doc);
        return new SimulationWorldOptions(Route:null,TrainParameters:rt.TrainParameters,OperationalParameters:rt.OperationalParameters,TrainCount:rt.DispatchPlan.Runs.Count,ProfileMode:doc.Simulation.ProfileMode,MovingBlockMode:doc.Simulation.MovingBlockMode,InitialBrakingEstimationMode:doc.Simulation.BrakingEstimationMode,ServicePatterns:rt.ServicePatterns,DispatchPlan:rt.DispatchPlan,VehicleTypes:rt.VehicleTypes,ServiceTypes:rt.ServiceTypes,Topology:rt.Topology,TraceRetentionPolicy:SimulationTraceRetentionPolicy.EventsOnly,SafetyObservationRetentionPolicy:SafetyObservationRetentionPolicy.Decimated(10)).CreateWorld();
    }

    private static ProjectManualTimetableRow Row(TopologyProjectDocument doc,string route,string vehicle,string run,double time,bool continues=false,string? next=null)
    {
        var r=doc.ServiceRoutes.Single(r=>r.ServiceRouteId==route);
        return new(time,route.StartsWith("DOWN",StringComparison.Ordinal)?TrainDirection.Outbound:TrainDirection.Inbound,"LOCAL","EMU","ALL",r.Stops[0].CandidatePlatformIds[0],vehicle,run,continues,next,null,route);
    }
    private static TopologyProjectDocument Rows(TopologyProjectDocument doc,IEnumerable<ProjectManualTimetableRow> rows)
    {
        var array=rows.OrderBy(r=>r.PlannedDepartureTimeSeconds).ThenBy(r=>r.ServiceRunId).ToArray();
        return doc with {Dispatch=new(DispatchPlanningMode.ManualTimetable,VehicleAssignmentMode.Automatic,[],array),Simulation=doc.Simulation with {TrainCount=array.Length}};
    }
    private static string[] SafetyTypes=["Collision","PredictedCollision","StationStopViolation","SignalAuthorityViolation"];
    private static Dictionary<string,int> Safety(SimulationWorld world)=>SafetyTypes.ToDictionary(t=>t,t=>world.Events.Count(e=>e.EventType.ToString()==t));

    public static void Smoke(string root,string outputRoot)
    {
        var output=Path.Combine(outputRoot,"handbook-network");Directory.CreateDirectory(output);
        var gates=new List<object>();
        foreach(var size in new[]{3,14})
        {
            var baseline=LinearBaseline(root,size);Gate(baseline,$"stage{(size==3?1:2)}-straight-{size}stations",3000,false);
        }
        var front=HandbookNetworkBuilder.Build(root,"07",1200,false);
        front=Rows(front,[Row(front,"UP:0:0","FRONT:V","FRONT:IN",0,true,"FRONT:OUT"),Row(front,"DOWN:0:0","FRONT:V","FRONT:OUT",1)]);
        Gate(front,"stage3-all-stop-and-s1-front",4000,true);
        foreach(var variant in new[]{"07","08","08b","08c"})
        {
            var doc=HandbookNetworkBuilder.Build(root,variant);
            var calibrations=Calibrate(doc);
            File.WriteAllText(Path.Combine(output,$"stage4-{variant}-both-terminals.mrtsim.json"),TopologyProjectFormat.Serialize(doc));
            gates.Add(new{stage="stage4-"+variant,structural=true,calibrations});
            Write(Path.Combine(output,"gates.json"),gates);
            Console.WriteLine($"NETWORK SMOKE {variant} Cmax={calibrations.Max(c=>c.CycleSeconds):F1}s routes={calibrations.Count}");
        }
        // Bounded station-spacing sensitivity: free circuits only, no claim of 11h evidence.
        foreach(var spacing in new[]{800d,1600d})
        {
            var doc=HandbookNetworkBuilder.Build(root,"08",spacing);var calibration=Calibrate(doc);
            File.WriteAllText(Path.Combine(output,$"sensitivity-{spacing:0}m.mrtsim.json"),TopologyProjectFormat.Serialize(doc));
            Write(Path.Combine(output,$"sensitivity-{spacing:0}m.json"),calibration);
        }
        WriteManifest(output,root);
        File.WriteAllText(Path.Combine(output,"smoke-complete-key.txt"),SmokeKey(root));

        void Gate(TopologyProjectDocument doc,string name,double horizon,bool requireCircuit)
        {
            TopologyProjectFormat.Validate(doc);InfrastructureValidator.ValidateAndThrow(new InfrastructureGraphV4(doc.Topology),doc.ServiceRoutes);
            File.WriteAllText(Path.Combine(output,name+".mrtsim.json"),TopologyProjectFormat.Serialize(doc));
            var world=World(doc);world.AdvanceTo(horizon);var safety=Safety(world);
            var arrivals=world.Events.Where(e=>e.EventType==SimulationEventType.Arrival).Select(e=>new{e.StationId,e.Direction,e.ServiceRunId,e.SimulationTimeSeconds}).ToArray();
            var changes=world.Events.Count(e=>e.EventType==SimulationEventType.DirectionChanged);
            if(safety.Values.Any(n=>n>0)||!arrivals.Any(a=>a.Direction==TrainDirection.Outbound)||!arrivals.Any(a=>a.Direction==TrainDirection.Inbound)||(requireCircuit&&(changes!=1||!arrivals.Any(a=>a.Direction==TrainDirection.Outbound&&a.StationId=="S14"))))throw new InvalidOperationException(name+" operational gate failed: "+JsonSerializer.Serialize(new{safety,arrivals},Json));
            gates.Add(new{stage=name,structural=true,operational=true,world.CurrentTimeSeconds,safety,directionChanges=changes,arrivals});Write(Path.Combine(output,"gates.json"),gates);
        }
    }

    private static TopologyProjectDocument LinearBaseline(string root,int count)
    {
        var seed=HandbookNetworkBuilder.S1(root).Document;var nodes=new List<TrackNodeDefinition>();var edges=new List<TrackEdgeDefinition>();var platforms=new List<PlatformDefinitionV4>();
        for(var i=0;i<=count;i++){nodes.Add(new("D"+i,"D"+i,TrackNodeKind.Ordinary));nodes.Add(new("U"+i,"U"+i,TrackNodeKind.Ordinary));}
        for(var i=1;i<=count;i++)foreach(var down in new[]{true,false}){var p=down?"D":"U";var id=p+i;edges.Add(new(){TrackEdgeId=id,FromNodeId=p+(down?i-1:i),ToNodeId=p+(down?i:i-1),LengthMeters=1200,Directionality=TrackDirectionality.ForwardOnly,FromPortSide=down?TrackPortSide.B:TrackPortSide.A,ToPortSide=down?TrackPortSide.A:TrackPortSide.B,DefaultSpeedLimitMetersPerSecond=65/3.6});platforms.Add(new(){PlatformId="S"+i+":"+p,StationId="S"+i,Name="S"+i+":"+p,TrackEdgeId=id,StopPositionOffsetMeters=600,PlatformStartOffsetMeters=530,PlatformEndOffsetMeters=670,EffectiveLengthMeters=140,StopPositionReference=StopPositionReference.TrainCenter,AllowedDirection=down?TrackDirection.Outbound:TrackDirection.Inbound});}
        var stations=Enumerable.Range(1,count).Select(i=>new StationDefinitionV4{StationId="S"+i,Name="S"+i,DefaultDwellTimeSeconds=i is 7 or 10?40:30,PlatformIds=["S"+i+":D","S"+i+":U"]}).ToArray();
        var routes=new[]{new ServiceRouteDefinition{ServiceRouteId="DOWN",Name="DOWN",Direction=TrainDirection.Outbound,Traversals=Enumerable.Range(1,count).Select(i=>new DirectedTrackTraversal("D"+i,TraversalDirection.Forward)).ToArray(),Stops=stations.Select(s=>new ServiceRouteStop{StationId=s.StationId,CandidatePlatformIds=[s.StationId+":D"]}).ToArray()},new ServiceRouteDefinition{ServiceRouteId="UP",Name="UP",Direction=TrainDirection.Inbound,Traversals=Enumerable.Range(1,count).Reverse().Select(i=>new DirectedTrackTraversal("U"+i,TraversalDirection.Forward)).ToArray(),Stops=stations.Reverse().Select(s=>new ServiceRouteStop{StationId=s.StationId,CandidatePlatformIds=[s.StationId+":U"]}).ToArray()}};
        var top=new TopologyInfrastructureDefinition{Nodes=nodes,Edges=edges,Platforms=platforms,Stations=stations};top=top with {DirectedConnections=DirectedTrackConnectionRules.Build(top,routes)};
        var doc=seed with {Topology=top,ServiceRoutes=routes,DirectionRouteBindings=[new(TrainDirection.Outbound,"DOWN"),new(TrainDirection.Inbound,"UP")],SignalBlocks=[],SpatialCapacity=null,StopPatterns=[new("ALL","全停",stations.Select(s=>new ProjectStopPatternInstruction(s.StationId,StopPatternAction.Stop,s.DefaultDwellTimeSeconds)).ToArray())],ServiceTypes=[seed.ServiceTypes.Single(s=>s.Id=="LOCAL")]};
        return Rows(doc,[Row(doc,"DOWN","BASE:D","BASE:D",0),Row(doc,"UP","BASE:U","BASE:U",0)]);
    }

    public sealed record Calibration(string Suffix,double CycleSeconds,double InboundDepartureOffsetSeconds,int DirectionChanges,Dictionary<string,int> Safety,IReadOnlyList<object> Events);
    private static List<Calibration> Calibrate(TopologyProjectDocument doc)
    {
        var result=new List<Calibration>();
        foreach(var down in doc.ServiceRoutes.Where(r=>r.Direction==TrainDirection.Outbound))
        {
            var suffix=down.ServiceRouteId[5..];var up="UP:"+suffix;var vehicle="FREE:"+suffix;
            var prepared=Rows(doc,[Row(doc,down.ServiceRouteId,vehicle,"FREE:0",0,true,"FREE:1"),Row(doc,up,vehicle,"FREE:1",1,true,"FREE:2"),Row(doc,down.ServiceRouteId,vehicle,"FREE:2",2)]);
            TopologyProjectFormat.Validate(prepared);var world=World(prepared);world.AdvanceTo(6000);
            var departures=world.Events.Where(e=>e.EventType==SimulationEventType.Departure).ToArray();
            var first=departures.SingleOrDefault(e=>e.StationId=="S1"&&e.ServiceRunId=="FREE:0");var second=departures.SingleOrDefault(e=>e.StationId=="S1"&&e.ServiceRunId=="FREE:2");var reverse=departures.SingleOrDefault(e=>e.StationId=="S14"&&e.ServiceRunId=="FREE:1");
            var safety=Safety(world);var changes=world.Events.Count(e=>e.EventType==SimulationEventType.DirectionChanged);
            var events=world.Events.Where(e=>e.StationId is "S1" or "S14"||e.EventType is SimulationEventType.DirectionChanged or SimulationEventType.TailTrackReached or SimulationEventType.TailTrackReturnStarted).Select(e=>(object)new{type=e.EventType.ToString(),e.ServiceRunId,e.VehicleId,e.StationId,e.SimulationTimeSeconds}).ToArray();
            if(first is null||second is null||reverse is null||changes<2||safety.Values.Any(n=>n>0))throw new InvalidOperationException("Circuit smoke failed "+suffix+": "+JsonSerializer.Serialize(new{safety,changes,events},Json));
            result.Add(new(suffix,second.SimulationTimeSeconds-first.SimulationTimeSeconds,reverse.SimulationTimeSeconds-first.SimulationTimeSeconds,changes,safety,events));
        }
        return result;
    }

    private static string SmokeKey(string root)=>Hash(Version+File.ReadAllText(Path.Combine(root,"scripts","CapacityThroughputExperiment","HandbookNetworkBuilder.cs"))+File.ReadAllText(Path.Combine(root,"scripts","CapacityThroughputExperiment","HandbookNetworkExperiment.cs"))+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location))));

    public static void Run(string root,MeasurementPlan plan,string outputRoot,string? onlyVariant=null,double? onlyHeadway=null)
    {
        var output=Path.Combine(outputRoot,"handbook-network");Directory.CreateDirectory(output);plan.WriteManifest(output);WriteManifest(output,root);
        // Every structural/short operational gate runs before a dense measurement.
        var smokeKeyFile=Path.Combine(output,"smoke-complete-key.txt");
        if(!File.Exists(smokeKeyFile)||File.ReadAllText(smokeKeyFile)!=SmokeKey(root))Smoke(root,outputRoot);
        foreach(var variant in new[]{"07","08","08b","08c"}.Where(v=>onlyVariant is null||v==onlyVariant))
        {
            var doc=HandbookNetworkBuilder.Build(root,variant);var calibration=Calibrate(doc);var settings=doc.SpatialCapacity!;
            var theory=settings.Points.SelectMany(p=>(p.Kind==SpatialReferencePointKind.IntermediateStation?new[]{SpatialCapacityDirection.Forward,SpatialCapacityDirection.Reverse}:new[]{SpatialCapacityDirection.Forward}).Select(direction=>new{p.ReferencePointId,direction,result=SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(p.Kind==SpatialReferencePointKind.AfterStationTurnback?p with {AlternateBerthing=variant=="08",TurnbackDwellSeconds=variant=="08b"?35:variant=="08c"?33:120}:p),settings.Global,settings.Train,direction,settings.Mode)})).ToArray();
            var design=theory.Max(t=>t.result.DesignHeadwaySeconds);
            foreach(var headway in new[]{design,150d}.Distinct().Where(h=>onlyHeadway is null||Math.Abs(h-onlyHeadway.Value)<1e-7))RunNetwork(doc,variant,headway,plan,output,calibration,theory);
        }
    }

    private static void RunNetwork(TopologyProjectDocument doc,string variant,double headway,MeasurementPlan plan,string output,IReadOnlyList<Calibration> calibration,object theory)
    {
        var cycle=calibration.Max(c=>c.CycleSeconds);var minimumFleet=(int)Math.Ceiling(cycle/headway);var fleet=minimumFleet+(minimumFleet%2);var period=fleet*headway;
        var rows=new List<ProjectManualTimetableRow>();
        for(var v=0;v<fleet;v++)
        {
            var binding=calibration.Single(c=>c.Suffix==$"{v%2}:{(variant=="08"?v%2:0)}");var padding=Math.Max(0,period-binding.CycleSeconds)/2;var vehicle=$"NETWORK:{variant}:V{v:000}";var trips=(int)Math.Ceiling((plan.TotalHorizonSeconds+period-v*headway)/period);
            for(var k=0;k<trips;k++)
            {
                var downId=$"{vehicle}:D{k:000}";var upId=$"{vehicle}:U{k:000}";var nextId=k+1<trips?$"{vehicle}:D{k+1:000}":null;
                rows.Add(Row(doc,"DOWN:"+binding.Suffix,vehicle,downId,v*headway+k*period,true,upId));
                rows.Add(Row(doc,"UP:"+binding.Suffix,vehicle,upId,v*headway+k*period+binding.InboundDepartureOffsetSeconds+padding,nextId is not null,nextId));
            }
        }
        var prepared=Rows(doc,rows);TopologyProjectFormat.Validate(prepared);var serialized=TopologyProjectFormat.Serialize(prepared);var engineHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));
        var key=Hash(Version+plan.CacheKey+engineHash+serialized);var stem=$"case{variant}-{headway.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture)}s";var runFile=Path.Combine(output,stem+".json");
        if(File.Exists(runFile)&&JsonNodeKey(runFile)==key){Console.WriteLine("RESUME NETWORK "+stem);return;}
        File.WriteAllText(Path.Combine(output,stem+".mrtsim.json"),serialized);
        var world=World(prepared);var timer=Stopwatch.StartNew();var checkpoints=new List<object>();var denseGateWritten=false;
        foreach(var boundary in plan.ProgressBoundaries(900))
        {
            world.AdvanceTo(boundary);var active=world.GetSnapshot().Trains.Where(t=>t.IsActive).ToArray();world.AcknowledgeSnapshotEvents();
            var unique=active.Select(t=>t.VehicleId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if(active.Length>fleet||unique!=active.Length)throw new InvalidOperationException($"Finite fleet invariant failed {stem} at {boundary}:active={active.Length},unique={unique},N={fleet}");
            checkpoints.Add(new{time=world.CurrentTimeSeconds,active=active.Length,uniqueActiveVehicles=unique,fleet});
            Write(Path.Combine(output,"progress-"+stem+".json"),new{variant,headway,fleet,cycle,period,time=world.CurrentTimeSeconds,wall=timer.Elapsed.TotalSeconds,key,active=active.Length,uniqueActiveVehicles=unique});
            if(!denseGateWritten&&boundary>=3*period)
            {
                var originEvents=world.Events.Where(e=>e.EventType==SimulationEventType.Departure&&((e.StationId=="S1"&&e.Direction==TrainDirection.Outbound)||(e.StationId=="S14"&&e.Direction==TrainDirection.Inbound))).GroupBy(e=>e.ServiceRunId).ToDictionary(g=>g.Key,g=>g.First().SimulationTimeSeconds);
                var due=rows.Where(r=>r.PlannedDepartureTimeSeconds<boundary).ToArray();var pending=due.Count(r=>!originEvents.ContainsKey(r.ServiceRunId!));var delay=due.Where(r=>originEvents.ContainsKey(r.ServiceRunId!)).Select(r=>originEvents[r.ServiceRunId!]-r.PlannedDepartureTimeSeconds).ToArray();
                Write(Path.Combine(output,"stage5-dense-"+stem+".json"),new{key,structural=true,operationalHorizon=world.CurrentTimeSeconds,finiteFleet=fleet,period,freeCycle=cycle,padding=calibration.Select(c=>new{c.Suffix,perTerminalSeconds=(period-c.CycleSeconds)/2}),safety=Safety(world),pending,meanDelay=delay.DefaultIfEmpty().Average(),maxDelay=delay.DefaultIfEmpty().Max(),distinctVehiclesChangingDirection=world.Events.Where(e=>e.EventType==SimulationEventType.DirectionChanged).Select(e=>e.VehicleId).Distinct().Count(),checkpoints});denseGateWritten=true;
            }
            Console.WriteLine($"NETWORK {variant} h={headway:F3} t={world.CurrentTimeSeconds:F0}/{plan.TotalHorizonSeconds:F0} wall={timer.Elapsed.TotalSeconds:F1}");
        }
        var points=new List<object>();
        foreach(var station in new[]{"S1","S7","S10","S14"})foreach(var direction in Enum.GetValues<TrainDirection>())
        {
            var arrivals=world.Events.Where(e=>e.EventType==SimulationEventType.Arrival&&e.StationId==station&&e.Direction==direction&&plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).OrderBy(e=>e.SimulationTimeSeconds).ToArray();
            var departures=world.Events.Where(e=>e.EventType==SimulationEventType.Departure&&e.StationId==station&&e.Direction==direction&&plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).OrderBy(e=>e.SimulationTimeSeconds).ToArray();
            points.Add(new{station,direction,arrivals=Summarize(arrivals,plan),departures=Summarize(departures,plan)});
        }
        var origins=world.Events.Where(e=>e.EventType==SimulationEventType.Departure&&((e.StationId=="S1"&&e.Direction==TrainDirection.Outbound)||(e.StationId=="S14"&&e.Direction==TrainDirection.Inbound))).GroupBy(e=>e.ServiceRunId).ToDictionary(g=>g.Key,g=>g.First().SimulationTimeSeconds);
        int Pending(double t)=>rows.Count(r=>r.PlannedDepartureTimeSeconds<t&&(!origins.TryGetValue(r.ServiceRunId!,out var time)||time>=t));
        var delays=rows.Where(r=>origins.ContainsKey(r.ServiceRunId!)).Select(r=>new{planned=r.PlannedDepartureTimeSeconds,actual=origins[r.ServiceRunId!],delay=origins[r.ServiceRunId!]-r.PlannedDepartureTimeSeconds}).Where(r=>plan.IsInMeasurementWindow(r.actual)).ToArray();
        var safety=Safety(world);var changes=world.Events.Where(e=>e.EventType==SimulationEventType.DirectionChanged).ToArray();
        Write(runFile,new{key,Version,variant,inputHeadwaySeconds=headway,measurementPlan=plan,engineHash,projectHash=Hash(serialized),fleetCheckpoints=checkpoints,plannedRows=rows.Count,minimumFleet,finiteFleet=fleet,freeCircuitSeconds=cycle,scheduledCircuitSeconds=period,paddingAllocation=calibration.Select(c=>new{c.Suffix,perTerminalHoldSeconds=Math.Max(0,period-c.CycleSeconds)/2}),calibration,theory,points,safety,pendingAtWarmup=Pending(plan.MeasurementStartSeconds),pendingAtEnd=Pending(plan.MeasurementEndSeconds),pendingDrift=Pending(plan.MeasurementEndSeconds)-Pending(plan.MeasurementStartSeconds),delayMean=delays.Length>0?delays.Average(d=>d.delay):0,delayMaximum=delays.Length>0?delays.Max(d=>d.delay):0,delayFirstHalf=delays.Where(d=>d.actual<plan.MeasurementMidpointSeconds).Select(d=>d.delay).DefaultIfEmpty().Average(),delaySecondHalf=delays.Where(d=>d.actual>=plan.MeasurementMidpointSeconds).Select(d=>d.delay).DefaultIfEmpty().Average(),directionChanges=changes.Length,directionChangesByTerminal=changes.GroupBy(e=>e.StationId??"unknown").ToDictionary(g=>g.Key,g=>g.Count()),distinctVehiclesChangingDirection=changes.Select(e=>e.VehicleId).Distinct().Count(),tailReached=world.Events.Count(e=>e.EventType==SimulationEventType.TailTrackReached),tailReturnStarted=world.Events.Count(e=>e.EventType==SimulationEventType.TailTrackReturnStarted),waitingResourceMarkers=world.Events.Count(e=>e.EventType==SimulationEventType.WaitingForResource),simulationSeconds=world.CurrentTimeSeconds,wallSeconds=timer.Elapsed.TotalSeconds});
    }
    private static string? JsonNodeKey(string file)=>System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))?["key"]?.GetValue<string>();
    private static object Summarize(IReadOnlyList<SimulationEvent> events,MeasurementPlan plan)
    {
        var gaps=events.Zip(events.Skip(1),(a,b)=>b.SimulationTimeSeconds-a.SimulationTimeSeconds).Order().ToArray();
        return new{count=events.Count,perHour=events.Count/(plan.MeasurementDurationSeconds/3600),bucketCounts=plan.CountBuckets(events,e=>e.SimulationTimeSeconds),medianHeadway=gaps.Length==0?(double?)null:gaps.Length%2==1?gaps[gaps.Length/2]:(gaps[gaps.Length/2-1]+gaps[gaps.Length/2])/2,spanHeadway=events.Count<2?(double?)null:(events[^1].SimulationTimeSeconds-events[0].SimulationTimeSeconds)/(events.Count-1),minimumGap=gaps.Length>0?(double?)gaps[0]:null,maximumGap=gaps.Length>0?(double?)gaps[^1]:null};
    }

    public static void RunS1(string root,MeasurementPlan plan,string outputRoot)
    {
        var fixture=HandbookNetworkBuilder.S1(root);var output=Path.Combine(outputRoot,"handbook-s1");Directory.CreateDirectory(output);plan.WriteManifest(output);
        var rows=new List<ProjectManualTimetableRow>();var n=0;var h=fixture.Theory.DesignHeadwaySeconds;
        for(var t=0d;t<plan.TotalHorizonSeconds;t+=h,n++) {var binding=fixture.TurnbackBindings[n%2];var vehicle="S1:"+n;rows.Add(new(t,TrainDirection.Inbound,"LOCAL","EMU","ALL","C:U",vehicle,vehicle+":IN",true,vehicle+":OUT",null,binding.ArrivalServiceRouteId));rows.Add(new(t+1,TrainDirection.Outbound,"LOCAL","EMU","ALL",n%2==0?"A:D":"A:U",vehicle,vehicle+":OUT",false,null,null,binding.DepartureServiceRouteId));}
        var smokeDoc=Rows(fixture.Document,rows.Take(4));TopologyProjectFormat.Validate(smokeDoc);var smoke=World(smokeDoc);smoke.AdvanceTo(1200);var smokeReturns=smoke.Events.Where(e=>e.StationId=="A"&&e.EventType==SimulationEventType.Departure&&e.Direction==TrainDirection.Outbound).ToArray();
        if(smokeReturns.Length!=2||fixture.TurnbackBindings.Any(b=>!smokeReturns.Any(e=>e.ServiceRouteId==b.DepartureServiceRouteId))||Safety(smoke).Values.Any(n=>n>0))throw new InvalidOperationException("S1 isolated two-platform operational gate failed");
        Write(Path.Combine(output,"smoke.json"),new{structural=true,safety=Safety(smoke),returns=smokeReturns.Select(e=>new{e.VehicleId,e.ServiceRouteId,e.SimulationTimeSeconds})});
        var doc=Rows(fixture.Document,rows);TopologyProjectFormat.Validate(doc);var serialized=TopologyProjectFormat.Serialize(doc);var engineHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));var key=Hash(Version+plan.CacheKey+engineHash+serialized);
        var metricFile=Path.Combine(output,"metrics.json");if(File.Exists(metricFile)&&JsonNodeKey(metricFile)==key){Console.WriteLine("RESUME S1");return;}
        File.WriteAllText(Path.Combine(output,"project.mrtsim.json"),serialized);var world=World(doc);var timer=Stopwatch.StartNew();foreach(var boundary in plan.ProgressBoundaries(900)){world.AdvanceTo(boundary);Write(Path.Combine(output,"progress.json"),new{time=world.CurrentTimeSeconds,wall=timer.Elapsed.TotalSeconds});Console.WriteLine($"S1 t={world.CurrentTimeSeconds:F0} wall={timer.Elapsed.TotalSeconds:F1}");}
        var arrivals=world.Events.Where(e=>e.StationId=="A"&&e.EventType==SimulationEventType.Arrival&&e.Direction==TrainDirection.Inbound&&plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).OrderBy(e=>e.SimulationTimeSeconds).ToArray();var departures=world.Events.Where(e=>e.StationId=="A"&&e.EventType==SimulationEventType.Departure&&e.Direction==TrainDirection.Outbound&&plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).OrderBy(e=>e.SimulationTimeSeconds).ToArray();
        var origins=world.Events.Where(e=>e.EventType==SimulationEventType.Departure&&e.StationId=="C"&&e.Direction==TrainDirection.Inbound).GroupBy(e=>e.ServiceRunId).ToDictionary(g=>g.Key,g=>g.First().SimulationTimeSeconds);
        int Pending(double t)=>rows.Count(r=>r.Direction==TrainDirection.Inbound&&r.PlannedDepartureTimeSeconds<t&&(!origins.TryGetValue(r.ServiceRunId!,out var actual)||actual>=t));
        Write(metricFile,new{key,engineHash,projectHash=Hash(serialized),fixture.Name,fixture.Theory,headway=h,plan,arrivals=Summarize(arrivals,plan),departures=Summarize(departures,plan),safety=Safety(world),pendingAtWarmup=Pending(plan.MeasurementStartSeconds),pendingAtEnd=Pending(plan.MeasurementEndSeconds),pendingDrift=Pending(plan.MeasurementEndSeconds)-Pending(plan.MeasurementStartSeconds),facilities=fixture.TurnbackBindings.ToDictionary(b=>b.FacilityId,b=>departures.Count(e=>e.ServiceRouteId==b.DepartureServiceRouteId)),directionChanges=world.Events.Count(e=>e.EventType==SimulationEventType.DirectionChanged),fixture.EnforcedParameters,fixture.UnsupportedParameters,simulationSeconds=world.CurrentTimeSeconds,wallSeconds=timer.Elapsed.TotalSeconds});
    }
    private static void WriteManifest(string output,string root)=>Write(Path.Combine(output,"manifest.json"),new{Version,primarySource="運輸系統容量分析手冊 都會捷運篇6.1.7/8 PDF609–615",sourceProject=Path.GetFileName(Directory.GetFiles(Path.Combine(root,"samples","handbook-capacity"),"07-*.mrtsim.json").Single()),formalInputs=new[]{"S1 front dual-platform20/140m120s40km/h65km/h1.5","S7/S10 directional3/4 and3/12permille40s dwell/speeds/overlap/safety","S14rear20/160/15m+2permille120/35/33s; single/doubletail","140m train1m/s2 nominal acceleration/deceleration0.75brake effect"},syntheticAssumptions=new[]{"All13stationcentre spacings1200m bothdirections; handbook provides no measured station distances","800/1600m spacing are bounded free-circuit sensitivity only","Other stations30s dwell,140m platform length;25m tail safety buffer","S7/S10 slope andspeed limits extend to own station-edge boundaries, splitatstationcentre; remainingmiddleedgesflat","Signalentry atstopped trainrear,protecthead+15m+overlap; notclaimedidentical toanalyticalformula","S1/S14sharedflatdiamond conservative mutuallyexclusive crossing policy, notsurveyedplans","Finite fleet minimum N=ceil(max free circuit/headway), rounded up to even N for strict alternating S1 berths and S14 tails; all vehicles introduced at S1 and reused until final scheduled run; explicit continuation IDs; no extra opposite endpoint feeds; periodic padding split equally between S14 scheduled departure and S1 next departure","Nointerchangebranchtrainconflicts: othercoloredroutesinFigure6-27aretransfercontextandlackphysicaljunctiondata"},scope="Operational sensitivity network with exact local handbook parameters; not a surveyed reconstruction or proof of real line capacity",omitted=new[]{"passenger boarding interaction","driver/fleet availability beyond finite demonstrationfleet","real signal blocks","real gradients outside four supplied local profiles"},expectedObservations=new[]{"All14stations servedbothdirections","samevehiclecontinuation acrossS14andS1","single/doubletailresources andS1dualberths","centralwindow20half-hourbuckets andstartuptrend inspection","formula onlyselectsdemand,neverreleasegate"}});
}
