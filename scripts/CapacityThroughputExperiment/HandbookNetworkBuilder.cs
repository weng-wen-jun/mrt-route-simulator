using System.Text.Json;
using System.Text.Json.Nodes;
using MrtRouteSimulator.Engine;

/// <summary>Handbook 6.1.7/8 local data on an explicitly synthetic station-spacing network.</summary>
public static class HandbookNetworkBuilder
{
    public static TopologyProjectDocument LoadSource(string root, string prefix = "07-") =>
        TopologyProjectFormat.Deserialize(File.ReadAllText(Directory.GetFiles(Path.Combine(root,"samples","handbook-capacity"), prefix+"*.mrtsim.json").Single()));

    public static TurnbackFixture S1(string root)
    {
        var source = LoadSource(root);
        var settings = source.SpatialCapacity!;
        var point = settings.Points.Single(p => p.Kind == SpatialReferencePointKind.BeforeStationTurnback) with { StationId="A" };
        var doc = StationLayoutTemplateService.Build(StationLayoutTemplateKind.FrontTurnback);
        doc = doc with { Topology=doc.Topology with {
            Edges=doc.Topology.Edges.Select(e => e with { ConflictResourceIds=new HashSet<string>(),
                LengthMeters=e.TrackEdgeId is "X1" or "X2" ? point.CrossoverLengthMeters : e.LengthMeters,
                DefaultSpeedLimitMetersPerSecond=e.Kind==TrackEdgeKind.Crossover ? point.SwitchSpeedLimitMetersPerSecond : 65/3.6 }).ToArray(),
            Resources=doc.Topology.TurnbackFacilities.Select(f=>new ConflictResourceDefinition(f.FacilityId+":BERTH",f.Name,ConflictResourceKind.Platform)).ToArray(),
            TurnbackFacilities=doc.Topology.TurnbackFacilities.Select(f=>f with { ConflictResourceIds=new HashSet<string>([f.FacilityId+":BERTH"]) }).ToArray(),
            TurnbackOperations=doc.Topology.TurnbackOperations.Select(o=>o with {MinimumDwellTimeSeconds=120}).ToArray() } };
        doc = TurnbackGeometryBuilder.Align(doc,StationLayoutTemplateKind.FrontTurnback,point,140);
        doc = CrossingFixtureBuilder.AddSharedDiamond(doc);
        doc = doc with { ProjectId="CASE07-S1-EXACT-LOCAL",ProjectName="手冊範例七 S1 站前雙月台折返（局部示範幾何）",
            Train=doc.Train with {MaxSpeedMetersPerSecond=65/3.6,AccelerationMetersPerSecondSquared=1,DecelerationMetersPerSecondSquared=1},
            Operations=doc.Operations with {TrainLengthMeters=140,ServiceBrakingMetersPerSecondSquared=.75,EmergencyBrakingMetersPerSecondSquared=1,ControlReactionTimeSeconds=settings.Global.ReactionTimeSeconds,ApproachSpeedMetersPerSecond=65/3.6},
            VehicleTypes=doc.VehicleTypes.Select(v=>v with {LengthMeters=140,MaxSpeedMetersPerSecond=65/3.6,AccelerationMetersPerSecondSquared=1,ServiceBrakeDecelerationMetersPerSecondSquared=.75,EmergencyBrakeDecelerationMetersPerSecondSquared=1}).ToArray(),
            StopPatterns=doc.StopPatterns.Select(p=>p with {Instructions=p.Instructions.Select(i=>i.StationId=="A" ? i with {DwellTimeSeconds=0}:i).ToArray()}).ToArray(),
            SpatialCapacity=new(settings.Global,settings.Train,[point],settings.Mode,true) };
        doc=TurnbackSignalMapper.Apply(doc,settings.Global.InterchangeTimeSeconds,point.MainlineSafetyFactor);
        var theory=SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(point),settings.Global,settings.Train,SpatialCapacityDirection.Forward,settings.Mode);
        TopologyProjectFormat.Validate(doc);
        return new("case07-s1-front", "07-S1",doc,point,theory,"A",TrainDirection.Inbound,
            [new("TURN","UP","DOWN"),new("TURN:PLATFORM2","UP:PLATFORM2","DOWN:PLATFORM2")],
            ["Primary handbook PDF611 table6.5:20m gap,140m crossover,120s station-front dwell,40km/h switch,65km/h approach,safety1.5,alternate platforms", "140m train,1m/s2 acceleration,0.75m/s2 effective service brake; independent berth resources; explicit shared diamond and connections"],
            ["Synthetic local three-station approaches and platform length140m; no surveyed S1 geometry supplied", "PHF/margin remain analytical, no formula-based release throttle"]);
    }

    // IDs are renamed by exact token, preserving typed ports/traversal direction. Coordinates never determine connectivity.
    private static TopologyProjectDocument Prefix(TopologyProjectDocument doc,string prefix,Dictionary<string,string> stationMap)
    {
        var ids=doc.Topology.Nodes.Select(n=>n.NodeId).Concat(doc.Topology.Edges.Select(e=>e.TrackEdgeId))
            .Concat(doc.Topology.Platforms.Select(p=>p.PlatformId)).Concat(doc.Topology.Resources.Select(r=>r.ResourceId))
            .Concat(doc.Topology.TurnbackFacilities.Select(f=>f.FacilityId)).Concat(doc.Topology.TurnbackOperations.Select(o=>o.OperationId))
            .Concat(doc.Topology.StationOperations.Select(o=>o.StationOperationId)).Concat(doc.ServiceRoutes.Select(r=>r.ServiceRouteId)).Distinct().ToDictionary(x=>x,x=>prefix+x);
        foreach(var pair in stationMap)ids[pair.Key]=pair.Value;
        var json=JsonNode.Parse(TopologyProjectFormat.Serialize(doc))!;
        void Visit(JsonNode node)
        {
            if(node is JsonObject obj)foreach(var pair in obj.ToArray()) {if(pair.Value is JsonValue v && v.TryGetValue<string>(out var s) && ids.TryGetValue(s,out var replacement) && (!stationMap.ContainsKey(s)||pair.Key is "stationId" or "platformBodyId"))obj[pair.Key]=replacement;else if(pair.Value is not null)Visit(pair.Value);}
            else if(node is JsonArray arr)for(var i=0;i<arr.Count;i++){if(arr[i] is JsonValue v && v.TryGetValue<string>(out var s)&&ids.TryGetValue(s,out var replacement))arr[i]=replacement;else if(arr[i] is {} child)Visit(child);}
        }
        Visit(json);
        // Prefix isolated signal IDs too, while shared sections/positions were already mapped.
        foreach(var signal in json["signalBlocks"]!.AsArray())signal!["signalId"]=prefix+signal["signalId"]!.GetValue<string>();
        return TopologyProjectFormat.Deserialize(json.ToJsonString());
    }

    public static TopologyProjectDocument Build(string root,string variant,double spacing=1200,bool includeEastFacility=true)
    {
        if(spacing<800)throw new ArgumentOutOfRangeException(nameof(spacing));
        var source=LoadSource(root,variant+"-");
        var west=Prefix(S1(root).Document,"W:",new(){{"A","S1"},{"B","S2"},{"C","S3"}});
        var eastFixture=TurnbackFixtures.Build(root).Single(f=>f.Name==(variant switch {"07"=>"case07-s14-rear-single","08"=>"case08-s14-rear-double","08b"=>"case08b-s14-rear-single-35s","08c"=>"case08c-s14-rear-single-33s",_=>throw new ArgumentException(variant)}));
        var east=Prefix(eastFixture.Document,"E:",new(){{"A","S14"},{"B","S13"},{"C","S12"}});
        // West and east templates face opposite ends; the east orientation is retained physically,
        // while service direction/platform permission is explicitly remapped to the full line.
        east=east with {Topology=east.Topology with {Platforms=east.Topology.Platforms.Select(p=>p with {AllowedDirection=p.AllowedDirection switch {TrackDirection.Outbound=>TrackDirection.Inbound,TrackDirection.Inbound=>TrackDirection.Outbound,_=>p.AllowedDirection}}).ToArray()}};
        var nodes=west.Topology.Nodes.Concat(east.Topology.Nodes).ToList();
        var edges=west.Topology.Edges.Concat(east.Topology.Edges).Select(e=>e.TrackEdgeId is "W:D1" or "W:D2" or "W:U1" or "W:U2" or "E:D1" or "E:D2" or "E:U1" or "E:U2" ? e with {LengthMeters=spacing,DefaultSpeedLimitMetersPerSecond=65/3.6}:e).ToList();
        var platforms=west.Topology.Platforms.Concat(east.Topology.Platforms).Select(p=> {
            var centre=p.TrackEdgeId switch {"W:D1" or "W:D2"=>spacing-230,"W:U1" or "W:U2"=>230,"E:D1" or "E:D2"=>spacing-510,"E:U1" or "E:U2"=>510,_=>p.StopPositionOffsetMeters};
            return p with {StopPositionOffsetMeters=centre,PlatformStartOffsetMeters=centre-70,PlatformEndOffsetMeters=centre+70,EffectiveLengthMeters=140};}).ToList();
        var downBridge=new List<DirectedTrackTraversal>();var upBridge=new List<DirectedTrackTraversal>();
        var downNode="W:DN3";var upNode="W:UN3";
        for(var station=4;station<=11;station++)
        {
            var nd="LINK:D:"+station;var nu="LINK:U:"+station;
            nodes.Add(new(nd,nd,TrackNodeKind.Ordinary));nodes.Add(new(nu,nu,TrackNodeKind.Ordinary));
            var de="LINK:D:"+station;var ue="LINK:U:"+station;
            edges.Add(Edge(de,downNode,nd,spacing,TrackPortSide.B,TrackPortSide.A));
            edges.Add(Edge(ue,nu,upNode,spacing,TrackPortSide.A,TrackPortSide.B));
            platforms.Add(Platform("S"+station+":D","S"+station,de,spacing-230,TrackDirection.Outbound));
            platforms.Add(Platform("S"+station+":U","S"+station,ue,230,TrackDirection.Inbound));
            downBridge.Add(new(de,TraversalDirection.Forward));upBridge.Insert(0,new(ue,TraversalDirection.Forward));downNode=nd;upNode=nu;
        }
        // 230m to west bridge edge end + connector +510m into east template = spacing.
        edges.Add(Edge("LINK:D:END",downNode,"E:UN3",spacing-740,TrackPortSide.B,TrackPortSide.B));
        edges.Add(Edge("LINK:U:END","E:DN3",upNode,spacing-740,TrackPortSide.B,TrackPortSide.B));
        downBridge.Add(new("LINK:D:END",TraversalDirection.Forward));upBridge.Insert(0,new("LINK:U:END",TraversalDirection.Forward));
        var routes=new List<ServiceRouteDefinition>();var operations=new List<TurnbackOperationDefinition>();
        var stationOperations=new List<StationOperationDefinition>();
        var westPairs=new[]{("UP","DOWN","TURN"),("UP:PLATFORM2","DOWN:PLATFORM2","TURN:PLATFORM2")};
        var eastPairs=variant=="08" ? new[]{("UP","DOWN","TURN"),("UP:TAIL2","DOWN:TAIL2","TURN:TAIL2")} : new[]{("UP","DOWN","TURN")};
        for(var w=0;w<westPairs.Length;w++)for(var e=0;e<eastPairs.Length;e++)
        {
            var dw=west.ServiceRoutes.Single(r=>r.ServiceRouteId=="W:"+westPairs[w].Item2);
            var uw=west.ServiceRoutes.Single(r=>r.ServiceRouteId=="W:"+westPairs[w].Item1);
            var de=east.ServiceRoutes.Single(r=>r.ServiceRouteId=="E:"+eastPairs[e].Item1);
            var ue=east.ServiceRoutes.Single(r=>r.ServiceRouteId=="E:"+eastPairs[e].Item2);
            var suffix=$"{w}:{e}";var did="DOWN:"+suffix;var uid="UP:"+suffix;
            routes.Add(new(){ServiceRouteId=did,Name=did,Direction=TrainDirection.Outbound,Traversals=dw.Traversals.Concat(downBridge).Concat(de.Traversals).ToArray(),Stops=dw.Stops.Concat(Enumerable.Range(4,8).Select(i=>Stop("S"+i,"S"+i+":D"))).Concat(de.Stops).ToArray()});
            routes.Add(new(){ServiceRouteId=uid,Name=uid,Direction=TrainDirection.Inbound,Traversals=ue.Traversals.Concat(upBridge).Concat(uw.Traversals).ToArray(),Stops=ue.Stops.Concat(Enumerable.Range(4,8).Reverse().Select(i=>Stop("S"+i,"S"+i+":U"))).Concat(uw.Stops).ToArray()});
            operations.Add(new(){OperationId="W:OP:"+suffix,FacilityId="W:"+westPairs[w].Item3,ArrivalServiceRouteId=uid,DepartureServiceRouteId=did,MinimumDwellTimeSeconds=120});
            stationOperations.Add(new(){StationOperationId="W:SO:"+suffix,StationId="S1",ArrivalPlatformIds=uw.Stops[^1].CandidatePlatformIds,DeparturePlatformIds=dw.Stops[0].CandidatePlatformIds,TurnbackOperationIds=["W:OP:"+suffix]});
            if(includeEastFacility){operations.Add(new(){OperationId="E:OP:"+suffix,FacilityId="E:"+eastPairs[e].Item3,ArrivalServiceRouteId=did,DepartureServiceRouteId=uid,MinimumDwellTimeSeconds=eastFixture.CapacityPoint.TurnbackDwellSeconds});stationOperations.Add(new(){StationOperationId="E:SO:"+suffix,StationId="S14",ArrivalPlatformIds=de.Stops[^1].CandidatePlatformIds,DeparturePlatformIds=ue.Stops[0].CandidatePlatformIds,TurnbackOperationIds=["E:OP:"+suffix]});}
        }
        var stations=Enumerable.Range(1,14).Select(i=>new StationDefinitionV4{StationId="S"+i,Name="S"+i,DefaultDwellTimeSeconds=i is 7 or 10?40:i is 1 or 14?0:30,PlatformIds=platforms.Where(p=>p.StationId=="S"+i).Select(p=>p.PlatformId).ToArray()}).ToArray();
        if(!includeEastFacility)
        {
            // Stage 3 contains no dormant east turnback geometry. End the two passenger
            // tracks at S14; tails/crossovers and their ports/resources are added in stage 4.
            edges.RemoveAll(e=>e.TrackEdgeId.StartsWith("E:",StringComparison.Ordinal)&&e.Kind is TrackEdgeKind.Crossover or TrackEdgeKind.TailTrack);
            var usedNodes=edges.SelectMany(e=>new[]{e.FromNodeId,e.ToNodeId}).ToHashSet(StringComparer.OrdinalIgnoreCase);
            nodes.RemoveAll(n=>!usedNodes.Contains(n.NodeId));
        }
        // Presentation coordinates are assigned after physical assembly; ports above remain authoritative.
        var wx=new Dictionary<string,double>{{"DN0",-510},{"UN0",-510},{"DN1",90},{"UX",90},{"DX",230},{"UN1",230},{"DN2",spacing+230},{"UN2",spacing+230},{"DN3",2*spacing+230},{"UN3",2*spacing+230},{CrossingFixtureBuilder.DiamondNodeId,160}};
        var ex=new Dictionary<string,double>{{"DN0",90},{"UN0",90},{"DN1",-510},{"UN1",-510},{"DN2",-spacing-510},{"UN2",-spacing-510},{"DN3",-2*spacing-510},{"UN3",-2*spacing-510},{"U200",250},{"D200",250},{"UB",430},{"DB",430},{CrossingFixtureBuilder.DiamondNodeId,170}};
        nodes=nodes.Select(n=>n.NodeId.StartsWith("W:")?n with {SchematicPosition=wx[n.NodeId[2..]],SchematicLane=n.SchematicLane}:n.NodeId.StartsWith("E:")?n with {SchematicPosition=13*spacing+ex[n.NodeId[2..]],SchematicLane=-(n.SchematicLane??0)}:n with {SchematicPosition=(int.Parse(n.NodeId[7..],System.Globalization.CultureInfo.InvariantCulture)-1)*spacing+230,SchematicLane=n.NodeId.StartsWith("LINK:D:")?1:-1}).ToList();
        edges=edges.Select(e=>e.TrackEdgeId.StartsWith("E:")?e with {SchematicLane=e.SchematicLane is {} lane?-lane:null}:e.TrackEdgeId.StartsWith("LINK:")?e with {SchematicLane=e.TrackEdgeId.StartsWith("LINK:D:")?1:-1}:e).ToList();
        var topology=new TopologyInfrastructureDefinition{Nodes=nodes,Edges=edges,Platforms=platforms,Stations=stations,Resources=west.Topology.Resources.Concat(includeEastFacility?east.Topology.Resources:[]).ToArray(),TurnbackFacilities=west.Topology.TurnbackFacilities.Concat(includeEastFacility?east.Topology.TurnbackFacilities:[]).ToArray(),TurnbackOperations=operations,StationOperations=stationOperations,Gradients=east.Topology.Gradients.Where(g=>edges.Any(e=>e.TrackEdgeId==g.TrackEdgeId)).ToArray()};
        topology=topology with {DirectedConnections=DirectedTrackConnectionRules.Build(topology,routes)};
        var doc=west with {ProjectId="HANDBOOK-SYNTHETIC-NETWORK:"+variant,ProjectName=$"範例{variant} S1-S14 整線敏感度（假設等站距{spacing}m）",Topology=topology,ServiceRoutes=routes.ToArray(),DirectionRouteBindings=[new(TrainDirection.Outbound,"DOWN:0:0"),new(TrainDirection.Inbound,"UP:0:0")],SignalBlocks=west.SignalBlocks.Concat(includeEastFacility?east.SignalBlocks:[]).ToArray(),SpatialCapacity=source.SpatialCapacity,
            StopPatterns=[new("ALL","全停",stations.Select(s=>new ProjectStopPatternInstruction(s.StationId,StopPatternAction.Stop,s.DefaultDwellTimeSeconds)).ToArray())],
            ServiceTypes=[west.ServiceTypes.Single(s=>s.Id=="LOCAL")],
            Dispatch=new(DispatchPlanningMode.ManualTimetable,VehicleAssignmentMode.Automatic,[],[new(0,TrainDirection.Outbound,"LOCAL","EMU","ALL",routes[0].Stops[0].CandidatePlatformIds[0],"SMOKE:D","SMOKE:D",false,null,null,"DOWN:0:0"),new(0,TrainDirection.Inbound,"LOCAL","EMU","ALL",routes[1].Stops[0].CandidatePlatformIds[0],"SMOKE:U","SMOKE:U",false,null,null,"UP:0:0")])};
        doc=AddIntermediatePolicies(doc,source.SpatialCapacity!);
        TopologyProjectFormat.Validate(doc);
        VerifySpacing(doc,spacing);
        return doc;
    }

    private static TopologyProjectDocument AddIntermediatePolicies(TopologyProjectDocument doc,SpatialCapacityProjectSettings settings)
    {
        var gradients=doc.Topology.Gradients.ToList();var limits=new List<TrackSpeedLimitDefinition>();var signals=doc.SignalBlocks.ToList();
        foreach(var station in new[]{"S7","S10"})foreach(var down in new[]{true,false})
        {
            var p=settings.Points.Single(p=>p.ReferencePointId.EndsWith(station,StringComparison.Ordinal)||p.Name.StartsWith(station,StringComparison.Ordinal));
            var platform=doc.Topology.Platforms.Single(p=>p.PlatformId==station+(down?":D":":U"));var edge=doc.Topology.Edges.Single(e=>e.TrackEdgeId==platform.TrackEdgeId);var at=platform.StopPositionOffsetMeters;
            var gi=down?p.StationForwardGradeInPermille:p.StationReverseGradeInPermille;var go=down?p.StationForwardGradeOutPermille:p.StationReverseGradeOutPermille;
            gradients.Add(new("GRADE:"+edge.TrackEdgeId+":IN",edge.TrackEdgeId,0,at,gi));gradients.Add(new("GRADE:"+edge.TrackEdgeId+":OUT",edge.TrackEdgeId,at,edge.LengthMeters,go));
            var vi=down?p.StationForwardLaterCruiseSpeedMetersPerSecond:p.StationReverseLaterCruiseSpeedMetersPerSecond;var vo=down?p.StationForwardEarlierCruiseSpeedMetersPerSecond:p.StationReverseEarlierCruiseSpeedMetersPerSecond;
            limits.Add(new(){SpeedLimitId="SPEED:"+edge.TrackEdgeId+":IN",TrackEdgeId=edge.TrackEdgeId,StartOffsetMeters=0,EndOffsetMeters=at,LimitMetersPerSecond=vi});limits.Add(new(){SpeedLimitId="SPEED:"+edge.TrackEdgeId+":OUT",TrackEdgeId=edge.TrackEdgeId,StartOffsetMeters=at,EndOffsetMeters=edge.LengthMeters,LimitMetersPerSecond=vo});
            var route=doc.ServiceRoutes.First(r=>r.Direction==(down?TrainDirection.Outbound:TrainDirection.Inbound));var graph=new InfrastructureGraphV4(doc.Topology);var stop=ResolvedStopResolver.Resolve(graph,route).Single(s=>s.StationId==station);var start=stop.ChainageMeters-70;var end=stop.ChainageMeters+70+(down?p.StationForwardDistanceToSignalMeters+p.StationForwardOverlapMeters:p.StationReverseDistanceToSignalMeters+p.StationReverseOverlapMeters);
            var sections=new List<TrackSignalSection>();var chain=0d;foreach(var t in route.Traversals){var len=graph.GetRequiredEdge(t.TrackEdgeId).LengthMeters;var a=Math.Max(0,start-chain);var b=Math.Min(len,end-chain);if(b>a)sections.Add(new(t.TrackEdgeId,t.Direction==TraversalDirection.Forward?a:len-b,t.Direction==TraversalDirection.Forward?b:len-a));chain+=len;}
            signals.Add(new("HANDBOOK:"+station+(down?":D":":U"),new(edge.TrackEdgeId,at-70),TraversalDirection.Forward,sections,settings.Global.ReactionTimeSeconds,down?p.StationForwardSafetyFactor:p.StationReverseSafetyFactor));
        }
        return doc with {Topology=doc.Topology with {Gradients=gradients,SpeedLimits=limits},SignalBlocks=signals};
    }

    private static void VerifySpacing(TopologyProjectDocument doc,double spacing)
    {
        var graph=new InfrastructureGraphV4(doc.Topology);
        foreach(var route in doc.ServiceRoutes){var stops=ResolvedStopResolver.Resolve(graph,route);for(var i=1;i<stops.Count;i++)if(Math.Abs(stops[i].ChainageMeters-stops[i-1].ChainageMeters-spacing)>1e-6)throw new InvalidOperationException($"Station spacing {route.ServiceRouteId} {stops[i-1].StationId}->{stops[i].StationId}: {stops[i].ChainageMeters-stops[i-1].ChainageMeters}");}
    }
    private static TrackEdgeDefinition Edge(string id,string from,string to,double length,TrackPortSide fp,TrackPortSide tp)=>new(){TrackEdgeId=id,FromNodeId=from,ToNodeId=to,LengthMeters=length,Directionality=TrackDirectionality.ForwardOnly,Kind=TrackEdgeKind.Mainline,DefaultSpeedLimitMetersPerSecond=65/3.6,FromPortSide=fp,ToPortSide=tp};
    private static PlatformDefinitionV4 Platform(string id,string station,string edge,double at,TrackDirection direction)=>new(){PlatformId=id,Name=id,StationId=station,TrackEdgeId=edge,StopPositionOffsetMeters=at,PlatformStartOffsetMeters=at-70,PlatformEndOffsetMeters=at+70,EffectiveLengthMeters=140,AllowedDirection=direction,StopPositionReference=StopPositionReference.TrainCenter};
    private static ServiceRouteStop Stop(string station,string platform)=>new(){StationId=station,CandidatePlatformIds=[platform]};
}
