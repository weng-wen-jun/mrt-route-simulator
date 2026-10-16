using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
if(args.Contains("--measurement-plan-self-test", StringComparer.OrdinalIgnoreCase) || args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
 MeasurementPlanSelfTests.Run();
 Console.WriteLine("MeasurementPlan self-test passed.");
 return;
}
var root=Path.GetFullPath(args.FirstOrDefault(x=>!x.StartsWith("--")) ?? ".");
var plan=MeasurementPlan.Parse(args);
var pilot=args.Contains("--pilot");
var diagnostic=args.Contains("--diagnose");
var safetyBoundary=args.Contains("--safety");
var alignedOnly=args.Contains("--aligned-only", StringComparer.OrdinalIgnoreCase);
var outputRoot=ExperimentOutput.ResolveRoot(root,args,plan);
var output=Path.Combine(outputRoot,diagnostic?"diagnostics":safetyBoundary?"safety":pilot?"pilot":"");
Directory.CreateDirectory(output);
Directory.CreateDirectory(Path.Combine(output,"projects"));
plan.WriteManifest(output);
var measurementDefinition=plan.CacheKey;
var options=new JsonSerializerOptions { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,NumberHandling=diagnostic ? JsonNumberHandling.AllowNamedFloatingPointLiterals : JsonNumberHandling.Strict };
options.Converters.Add(new JsonStringEnumConverter());
if(args.Contains("--handbook-points")) { HandbookPointExperiment.PublicRun(root,plan,outputRoot,args.FirstOrDefault(arg=>arg.StartsWith("--point-case="))?[13..]); return; }
if(args.Contains("--handbook-network-smoke")) { HandbookNetworkExperiment.Smoke(root,outputRoot); return; }
if(args.Contains("--handbook-network")) { HandbookNetworkExperiment.Run(root,plan,outputRoot,args.FirstOrDefault(arg=>arg.StartsWith("--network-case="))?[15..],args.FirstOrDefault(arg=>arg.StartsWith("--network-headway=")) is { } networkHeadway ? double.Parse(networkHeadway[18..],CultureInfo.InvariantCulture) : null); return; }
if(args.Contains("--handbook-s1")) { HandbookNetworkExperiment.RunS1(root,plan,outputRoot); return; }
if(args.Contains("--junction-smoke")) { JunctionExperiment.Smoke(root,outputRoot); return; }
if(args.Contains("--junctions")) { JunctionExperiment.Run(root,plan,outputRoot,args.FirstOrDefault(arg=>arg.StartsWith("--junction-ratio=")) is { } ratioArg ? double.Parse(ratioArg[17..],CultureInfo.InvariantCulture) : null); return; }
if(args.Contains("--station-margin")) { StationBoundaryExperiment.RunMarginCheck(root,args.FirstOrDefault(a=>a.StartsWith("--case="))?[7..] ?? "01",plan,outputRoot); return; }
if(args.Contains("--station-boundary")) { StationBoundaryExperiment.Run(root,args.FirstOrDefault(a=>a.StartsWith("--case="))?[7..] ?? "01",plan,outputRoot); return; }
if(args.Contains("--diamond-smoke")) { CrossingFixtureBuilder.Smoke(root); return; }
if(args.Contains("--turnbacks")) { TurnbackExperiment.Run(root,plan,outputRoot,args.FirstOrDefault(arg=>arg.StartsWith("--turnback-case="))?[16..],args.FirstOrDefault(arg=>arg.StartsWith("--turnback-headway=")) is { } headwayArg ? double.Parse(headwayArg[19..],CultureInfo.InvariantCulture) : null); return; }
var carrier=Load("samples/小型/01-小型-三站雙股島式站場範例.mrtsim.json");
var measured=new List<Measurement>();
var theories=new List<Theory>();
foreach(var file in Directory.GetFiles(Path.Combine(root,"samples","handbook-capacity"),"*.mrtsim.json").Order())
{
 var doc=TopologyProjectFormat.Deserialize(File.ReadAllText(file));
 var settings=doc.SpatialCapacity!;
 foreach(var p in settings.Points)
 foreach(var d in p.Kind==SpatialReferencePointKind.IntermediateStation ? Enum.GetValues<SpatialCapacityDirection>() : [SpatialCapacityDirection.Forward])
 {
  var r=SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(p),settings.Global,settings.Train,d,settings.Mode);
  theories.Add(new(Path.GetFileNameWithoutExtension(file),p.ReferencePointId,p.Kind,d,r.SafetyHeadwaySeconds,r.DesignHeadwaySeconds,r.LineCapacityPerHour));
 }
}
File.WriteAllText(Path.Combine(output,"theory.json"),JsonSerializer.Serialize(theories,options));
if(!alignedOnly)
 foreach(var headway in safetyBoundary ? new[]{70d,75d,80d,61d,62d,63d,64d} : diagnostic ? new[]{30d} : pilot ? new[]{60d} : new[]{150d,90d,60d,50d,40d})
  Run("carrier",carrier,headway,null);
if(safetyBoundary)
{
 bool Clean(Measurement m)=>m.PredictedCollisionEvents==0 && m.CollisionEvents==0 && m.StopViolations==0 && m.SignalAuthorityViolationEvents==0 && m.PendingDueRuns-m.PendingDueRunsAtWarmup<=1;
 var clean=measured.Where(Clean).Select(m=>m.InputHeadwaySeconds).DefaultIfEmpty(90).Min();
 var dirty=measured.Where(m=>!Clean(m)).Select(m=>m.InputHeadwaySeconds).DefaultIfEmpty(60).Max();
 while(clean-dirty>1) { var h=Math.Floor((clean+dirty)/2); Run("carrier",carrier,h,null); if(measured.Where(m=>m.InputHeadwaySeconds==h).All(Clean)) clean=h; else dirty=h; }
}
if(!pilot && !diagnostic && !safetyBoundary)
{
 if(!alignedOnly)
 {
  var saturated=measured.Where(m=>m.Scenario=="carrier" && m.InputHeadwaySeconds==40).ToArray();
  var boundary=Math.Ceiling(3600/saturated.Min(m=>m.SpanRatePerHour));
  foreach(var h in new[]{boundary,boundary+1,boundary+2}.Where(h=>!measured.Any(m=>m.Scenario=="carrier" && Math.Abs(m.InputHeadwaySeconds-h)<0.01))) Run("carrier",carrier,h,null);
 }
 foreach(var prefix in new[]{"01-","03-","04-","04b-"})
 {
  var file=Directory.GetFiles(Path.Combine(root,"samples","handbook-capacity"),prefix+"*.mrtsim.json").Single();
  var source=TopologyProjectFormat.Deserialize(File.ReadAllText(file));
  var setting=source.SpatialCapacity!;
  var point=setting.Points.Single();
  var train=setting.Train;
  // Explicit station mapping below applies grade, signal overlap and safety policy
  // to this synthetic carrier; it is not a surveyed reconstruction.
  var vmax=Math.Max(Math.Max(point.StationForwardEarlierCruiseSpeedMetersPerSecond,point.StationForwardLaterCruiseSpeedMetersPerSecond),
      Math.Max(point.StationReverseEarlierCruiseSpeedMetersPerSecond,point.StationReverseLaterCruiseSpeedMetersPerSecond));
  var aligned=carrier with {
   Train=carrier.Train with {MaxSpeedMetersPerSecond=vmax,AccelerationMetersPerSecondSquared=train.AccelerationMetersPerSecondSquared,
    DecelerationMetersPerSecondSquared=train.DecelerationMetersPerSecondSquared},
   Operations=carrier.Operations with {TrainLengthMeters=train.LengthMeters,ServiceBrakingMetersPerSecondSquared=train.DecelerationMetersPerSecondSquared,
    EmergencyBrakingMetersPerSecondSquared=train.DecelerationMetersPerSecondSquared,ControlReactionTimeSeconds=setting.Global.ReactionTimeSeconds},
   VehicleTypes=carrier.VehicleTypes.Select(v=>v with {LengthMeters=train.LengthMeters,MaxSpeedMetersPerSecond=vmax,
    AccelerationMetersPerSecondSquared=train.AccelerationMetersPerSecondSquared,ServiceBrakeDecelerationMetersPerSecondSquared=train.DecelerationMetersPerSecondSquared,
    EmergencyBrakeDecelerationMetersPerSecondSquared=train.DecelerationMetersPerSecondSquared}).ToArray(),
   Topology=carrier.Topology with {Edges=carrier.Topology.Edges.Select(e=>e with {DefaultSpeedLimitMetersPerSecond=vmax}).ToArray()},
   SpatialCapacity=setting
  };
  foreach(var direction in Enum.GetValues<TrainDirection>())
  {
   var reverse=direction==TrainDirection.Inbound;
   var dwell=reverse ? point.StationReverseDwellSeconds : point.StationForwardDwellSeconds;
   var configured=aligned with {StopPatterns=aligned.StopPatterns.Select(p=>p with {Instructions=p.Instructions.Select(i=>i.StationId=="B" ? i with {DwellTimeSeconds=dwell} : i).ToArray()}).ToArray()};
   var departureSpeed=reverse ? point.StationReverseEarlierCruiseSpeedMetersPerSecond : point.StationForwardEarlierCruiseSpeedMetersPerSecond;
   var arrivalSpeed=reverse ? point.StationReverseLaterCruiseSpeedMetersPerSecond : point.StationForwardLaterCruiseSpeedMetersPerSecond;
   var routeId=configured.DirectionRouteBindings.Single(b=>b.Direction==direction).ServiceRouteId;
   var route=configured.ServiceRoutes.Single(r=>r.ServiceRouteId==routeId);
   var platformId=route.Stops.Single(s=>s.StationId=="B").CandidatePlatformIds.Single();
   var platform=configured.Topology.Platforms.Single(p=>p.PlatformId==platformId);
   var middleIndex=route.Traversals.ToList().FindIndex(t=>t.TrackEdgeId==platform.TrackEdgeId);
   if(route.Traversals.Any(t=>t.Direction!=TraversalDirection.Forward)) throw new InvalidOperationException("Station fixture requires explicit forward traversal mapping");
   var target=platform.StopPositionOffsetMeters+(platform.StopPositionReference==StopPositionReference.TrainCenter ? train.LengthMeters/2 : 0);
   var before=route.Traversals.Take(middleIndex).Select(t=>t.TrackEdgeId).ToHashSet();
   var after=route.Traversals.Skip(middleIndex+1).Select(t=>t.TrackEdgeId).ToHashSet();
   var middleEdge=configured.Topology.Edges.Single(e=>e.TrackEdgeId==platform.TrackEdgeId);
   if(target<=0 || target>=middleEdge.LengthMeters) throw new InvalidOperationException("Station speed mapping requires the head stop within the same edge");
   configured=configured with {Topology=configured.Topology with {
    Edges=configured.Topology.Edges.Select(e=>before.Contains(e.TrackEdgeId) ? e with {DefaultSpeedLimitMetersPerSecond=arrivalSpeed} : after.Contains(e.TrackEdgeId) ? e with {DefaultSpeedLimitMetersPerSecond=departureSpeed} : e).ToArray(),
    SpeedLimits=[new TrackSpeedLimitDefinition {SpeedLimitId="B-ARRIVAL",TrackEdgeId=middleEdge.TrackEdgeId,StartOffsetMeters=0,EndOffsetMeters=target,LimitMetersPerSecond=arrivalSpeed},
                 new TrackSpeedLimitDefinition {SpeedLimitId="B-DEPARTURE",TrackEdgeId=middleEdge.TrackEdgeId,StartOffsetMeters=target,EndOffsetMeters=middleEdge.LengthMeters,LimitMetersPerSecond=departureSpeed}]
   }};
   var theory=theories.Single(t=>t.File==Path.GetFileNameWithoutExtension(file) && t.Direction==(reverse?SpatialCapacityDirection.Reverse:SpatialCapacityDirection.Forward));
   // The handbook point keeps its source ReferencePointId for theory.json.  The
   // aligned carrier deliberately maps the runtime station to B, the carrier's
   // physical middle station, before creating the world for this direction.
   var runtimeConfigured=CapacityRuntimeMapper.ApplyStations(
       configured with { SpatialCapacity = setting with { Points = [point with { StationId = "B" }] } },
       direction).Document;
   Run("aligned-"+prefix.TrimEnd('-')+"-"+direction,runtimeConfigured,theory.DesignHeadwaySeconds,direction);
   if(!alignedOnly) Run("aligned-"+prefix.TrimEnd('-')+"-"+direction,runtimeConfigured,40,direction);
  }
 }
}
File.WriteAllText(Path.Combine(output,"measurements.json"),JsonSerializer.Serialize(measured,options));
var csv=new List<string>{"scenario,inputHeadwaySeconds,direction,middleDeparturesPerHour,spanRatePerHour,minimumHeadwaySeconds,medianHeadwaySeconds,firstHalfCount,lastHalfCount,originDepartures,dueRuns,pendingDueRuns,meanOriginDelaySeconds,maxOriginDelaySeconds,resourceWaitEvents,collisionEvents,stopViolations,signalAuthorityViolationEvents,wallSeconds,predictedCollisionEvents,pendingAtWarmup,firstHalfOriginDelay,lastHalfOriginDelay,measurementBucketCounts,measurementPlanKey,flowStable,warningFree"};
foreach(var m in measured) csv.Add(string.Join(",",m.Scenario,m.InputHeadwaySeconds,m.Direction,m.MiddleDeparturesPerHour,m.SpanRatePerHour,m.MinimumHeadwaySeconds,m.MedianHeadwaySeconds,m.FirstHalfCount,m.LastHalfCount,m.OriginDepartures,m.DueRuns,m.PendingDueRuns,m.MeanOriginDelaySeconds,m.MaxOriginDelaySeconds,m.ResourceWaitEvents,m.CollisionEvents,m.StopViolations,m.SignalAuthorityViolationEvents,m.WallSeconds,m.PredictedCollisionEvents,m.PendingDueRunsAtWarmup,m.FirstHalfMeanOriginDelaySeconds,m.LastHalfMeanOriginDelaySeconds,string.Join(";",m.MeasurementBucketCounts),m.MeasurementPlanKey,FlowStable(m),FlowStable(m)&&m.PredictedCollisionEvents==0&&m.SignalAuthorityViolationEvents==0));
File.WriteAllLines(Path.Combine(output,"measurements.csv"),csv,Encoding.UTF8);
Console.WriteLine($"DONE {measured.Count} directional measurements; {output}");

bool FlowStable(Measurement m)=>m.PendingDueRuns-m.PendingDueRunsAtWarmup<=1 && m.LastHalfMeanOriginDelaySeconds<=m.FirstHalfMeanOriginDelaySeconds+1 && m.CollisionEvents==0 && m.StopViolations==0 && m.SignalAuthorityViolationEvents==0;
TopologyProjectDocument Load(string path)=>TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,path)));
void Run(string scenario,TopologyProjectDocument input,double headway,TrainDirection? onlyDirection)
{
 var clock=Stopwatch.StartNew();
 var rows=new List<ProjectManualTimetableRow>();
 foreach(var direction in Enum.GetValues<TrainDirection>().Where(d=>onlyDirection is null || d==onlyDirection))
 {
  var template=input.Dispatch.ManualTimetableRows!.First(r=>r.Direction==direction);
  for(var i=0; i*headway<plan.TotalHorizonSeconds-1e-7; i++) rows.Add(template with {PlannedDepartureTimeSeconds=i*headway,
   VehicleId=$"{direction}-{i}",ServiceRunId=$"{direction}-{i}",ContinueAfterTerminal=false,ContinuationServiceRunId=null,StationTimetableEntries=null});
 }
 var doc=input with {ProjectId="DENSE-EXPERIMENT",ProjectName=scenario+" "+headway.ToString("0.###")+"s",
  Dispatch=new(DispatchPlanningMode.ManualTimetable,VehicleAssignmentMode.Automatic,[],rows.ToArray()),
  Simulation=input.Simulation with {TrainCount=rows.Count,HeadwaySeconds=headway}};
 TopologyProjectFormat.Validate(doc);
 var path=Path.Combine(output,"projects",scenario+"-"+headway.ToString("0.###")+"s.mrtsim.json");
 var serialized=TopologyProjectFormat.Serialize(doc);
 var engineHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(SimulationWorld).Assembly.Location)));
 var hashPath=path+".engine-sha256";
 var cacheMatches=File.Exists(path) && File.ReadAllText(path)==serialized && File.Exists(hashPath) && File.ReadAllText(hashPath)==engineHash;
 var versionPath=path+".experiment-version";
 cacheMatches=cacheMatches && File.Exists(versionPath) && File.ReadAllText(versionPath)==measurementDefinition;
 File.WriteAllText(hashPath,engineHash);
 File.WriteAllText(versionPath,measurementDefinition);
 File.WriteAllText(path,serialized);
 var cacheFile=Path.Combine(output,scenario+"-"+headway.ToString("0.###")+"s.json");
 if(args.Contains("--resume") && cacheMatches && File.Exists(cacheFile))
 {
  var cached=JsonSerializer.Deserialize<Measurement[]>(File.ReadAllText(cacheFile),options)!;
  if(cached.All(m=>m.MeasurementPlanKey==plan.CacheKey && m.MeasurementBucketCounts is {Length:var count} && count==plan.BucketCount)) { measured.AddRange(cached); File.WriteAllText(Path.Combine(output,"measurements.json"),JsonSerializer.Serialize(measured,options)); Console.WriteLine("RESUME "+scenario+" "+headway); return; }
 }
 var runtime=TopologyProjectFormat.CreateRuntime(doc);
 var world=new SimulationWorldOptions(Route:null,TrainParameters:runtime.TrainParameters,OperationalParameters:runtime.OperationalParameters,
  TrainCount:runtime.DispatchPlan.Runs.Count,ProfileMode:doc.Simulation.ProfileMode,MovingBlockMode:MovingBlockMode.Control,
  InitialBrakingEstimationMode:doc.Simulation.BrakingEstimationMode,ServicePatterns:runtime.ServicePatterns,DispatchPlan:runtime.DispatchPlan,
  VehicleTypes:runtime.VehicleTypes,ServiceTypes:runtime.ServiceTypes,Topology:runtime.Topology,
  TraceRetentionPolicy:SimulationTraceRetentionPolicy.EventsOnly,SafetyObservationRetentionPolicy:SafetyObservationRetentionPolicy.Decimated(10)).CreateWorld();
 var boundaries=plan.ProgressBoundaries(900).Take(diagnostic?1:int.MaxValue);
 foreach(var boundary in boundaries) {
  world.AdvanceTo(boundary);
  File.WriteAllText(Path.Combine(output,"progress.json"),JsonSerializer.Serialize(new {scenario,headway,simulationTime=world.CurrentTimeSeconds,wallSeconds=clock.Elapsed.TotalSeconds,eventCount=world.Events.Count},options));
 }
 if(diagnostic) {
  File.WriteAllText(Path.Combine(output,"events.json"),JsonSerializer.Serialize(world.Events,options));
  File.WriteAllText(Path.Combine(output,"snapshot.json"),JsonSerializer.Serialize(world.GetSnapshot(),options));
  Console.WriteLine("DIAGNOSTIC events="+world.Events.Count); return;
 }
 if(!diagnostic && Math.Abs(world.CurrentTimeSeconds-plan.TotalHorizonSeconds)>0.11) throw new InvalidOperationException($"Simulation did not reach {plan.HorizonLabel}");
 var context=world.GetTopologyResultContext();
 var resultRows=new List<Measurement>();
 foreach(var direction in rows.Select(r=>r.Direction).Distinct())
 {
  var middle=world.Events.Where(e=>e.Direction==direction && e.EventType==SimulationEventType.Departure && context.MatchesStationEvent(direction,"B",e)
   && plan.IsInMeasurementWindow(e.SimulationTimeSeconds)).OrderBy(e=>e.SimulationTimeSeconds).ToArray();
  var intervals=middle.Zip(middle.Skip(1),(a,b)=>b.SimulationTimeSeconds-a.SimulationTimeSeconds).Order().ToArray();
  var origin=direction==TrainDirection.Outbound?"A":"C";
  var departed=world.Events.Where(e=>e.Direction==direction && e.EventType==SimulationEventType.Departure && context.MatchesStationEvent(direction,origin,e)
   && e.SimulationTimeSeconds<plan.MeasurementEndSeconds).GroupBy(e=>e.ServiceRunId).Select(g=>g.First()).ToArray();
  var planned=rows.Where(r=>r.Direction==direction && r.PlannedDepartureTimeSeconds<plan.MeasurementEndSeconds).ToDictionary(r=>r.ServiceRunId!,r=>r.PlannedDepartureTimeSeconds);
  var delay=departed.Select(e=>e.SimulationTimeSeconds-planned[e.ServiceRunId]).ToArray();
  int PendingAt(double cutoff)=>planned.Count(p=>p.Value<cutoff)-departed.Count(e=>e.SimulationTimeSeconds<cutoff);
  double CentralDelay(double from,double to) { var values=departed.Where(e=>e.SimulationTimeSeconds>=from && e.SimulationTimeSeconds<to).Select(e=>e.SimulationTimeSeconds-planned[e.ServiceRunId]).ToArray(); return values.Length==0?0:values.Average(); }
  var m=new Measurement(scenario,headway,direction,middle.Length*3600d/plan.MeasurementDurationSeconds,middle.Length>1?3600*(middle.Length-1)/(middle[^1].SimulationTimeSeconds-middle[0].SimulationTimeSeconds):0,
   intervals.Length>0?intervals[0]:0,intervals.Length>0?(intervals[(intervals.Length-1)/2]+intervals[intervals.Length/2])/2:0,
   middle.Count(e=>e.SimulationTimeSeconds<plan.MeasurementMidpointSeconds),middle.Count(e=>e.SimulationTimeSeconds>=plan.MeasurementMidpointSeconds),departed.Length,planned.Count,planned.Count-departed.Length,
   delay.Length>0?delay.Average():0,delay.Length>0?delay.Max():0,
   world.Events.Count(e=>e.Direction==direction && e.EventType==SimulationEventType.WaitingForResource),
   world.Events.Count(e=>e.Direction==direction && e.EventType==SimulationEventType.Collision),
   world.Events.Count(e=>e.Direction==direction && e.EventType==SimulationEventType.StationStopViolation),
   world.Events.Count(e=>e.Direction==direction && e.EventType==SimulationEventType.SignalAuthorityViolation),clock.Elapsed.TotalSeconds,PendingAt(plan.MeasurementStartSeconds),CentralDelay(plan.MeasurementStartSeconds,plan.MeasurementMidpointSeconds),CentralDelay(plan.MeasurementMidpointSeconds,plan.MeasurementEndSeconds),
   world.Events.Count(e=>e.Direction==direction && e.EventType==SimulationEventType.PredictedCollision),
   plan.CountBuckets(middle,e=>e.SimulationTimeSeconds),plan.CacheKey);
  measured.Add(m); resultRows.Add(m);
  Console.WriteLine($"{scenario} h={headway:F3} {direction}: B={m.MiddleDeparturesPerHour}/h span={m.SpanRatePerHour:F2}, queue={m.PendingDueRuns}, maxDelay={m.MaxOriginDelaySeconds:F1}, collision={m.CollisionEvents}, {m.WallSeconds:F1}s");
 }
 // Persist each completed run for interruption-safe inspection.
 File.WriteAllText(Path.Combine(output,scenario+"-"+headway.ToString("0.###")+"s.json"),JsonSerializer.Serialize(resultRows,options));
 File.WriteAllText(Path.Combine(output,"measurements.json"),JsonSerializer.Serialize(measured,options));
}
record Theory(string File,string ReferencePointId,SpatialReferencePointKind Kind,SpatialCapacityDirection Direction,double SafetyHeadwaySeconds,double DesignHeadwaySeconds,int LineCapacityPerHour);
record Measurement(string Scenario,double InputHeadwaySeconds,TrainDirection Direction,double MiddleDeparturesPerHour,double SpanRatePerHour,double MinimumHeadwaySeconds,double MedianHeadwaySeconds,int FirstHalfCount,int LastHalfCount,int OriginDepartures,int DueRuns,int PendingDueRuns,double MeanOriginDelaySeconds,double MaxOriginDelaySeconds,int ResourceWaitEvents,int CollisionEvents,int StopViolations,int SignalAuthorityViolationEvents,double WallSeconds,int PendingDueRunsAtWarmup,double FirstHalfMeanOriginDelaySeconds,double LastHalfMeanOriginDelaySeconds,int PredictedCollisionEvents,int[] MeasurementBucketCounts,string MeasurementPlanKey);
