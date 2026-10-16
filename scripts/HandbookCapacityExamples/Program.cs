using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MrtRouteSimulator.Engine;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "samples", "handbook-capacity");
Directory.CreateDirectory(output);
var carrier = TopologyProjectFormat.Deserialize(File.ReadAllText(Path.Combine(root,"samples","小型/01-小型-三站雙股島式站場範例.mrtsim.json")));
var defaults = new SpatialCapacityGlobalParameters();
var defaultTrain = new SpatialCapacityTrainParameters();
var train1 = new SpatialCapacityTrainParameters(120,1,1,.75,240,300,5);
var train2 = new SpatialCapacityTrainParameters(140,1,1,.75,350,260,5);
var train3 = new SpatialCapacityTrainParameters(150,1,1,.75,350,270,5);
var train7 = new SpatialCapacityTrainParameters(140,1,1,.75,300,250,6);
var s13 = Station("S13", new[]{0d,0,10,190,45,80,55,1.96}, new[]{0d,0,10,145,45,55,80,1.7});
var s13Improved = s13 with { StationForwardOverlapMeters=0,StationReverseOverlapMeters=0,StationForwardDwellSeconds=40,StationReverseDwellSeconds=40,StationForwardSafetyFactor=1,StationReverseSafetyFactor=1 };
var s1 = new ProjectSpatialReferencePoint("S1","S1","S1 站前折返",SpatialReferencePointKind.BeforeStationTurnback,
 AlternateBerthing:true,DistanceFromStopToCrossoverMeters:20,CrossoverLengthMeters:140,TurnbackDwellSeconds:120,
 MainlineApproachCruiseSpeedMetersPerSecond:65/3.6,MainlineSafetyFactor:1.5);
var s7 = Station("S7",new[]{3d,-4,15,210,40,55,65,2.1},new[]{4d,-3,15,140,40,65,55,1.7});
var s10 = Station("S10",new[]{3d,-12,15,190,40,65,55,2.0},new[]{12d,-3,15,150,40,65,65,3.0});
var s14 = new ProjectSpatialReferencePoint("S14","S14","S14 站後折返",SpatialReferencePointKind.AfterStationTurnback,
 MainlineGradePermille:2,DistanceFromStopToCrossoverMeters:20,CrossoverLengthMeters:160,DistanceFromCrossoverToTurnbackStopMeters:15,TurnbackDwellSeconds:120);
var assumption="全域未在該例明列：補充採聯鎖10秒、反應1秒、PHF0.8、餘裕0.3；不是由手冊確認的輸入。";
var cases = new List<Example>
{
 new("01-範例一-最繁忙車站",new(defaults,train1,[Station("BUSY",new[]{0d,0,40,200,40,80,80,1.2},new[]{0d,0,40,200,40,80,80,1.2})]),"6-1～6-3／PDF593～595",assumption,[new("BUSY",SpatialCapacityDirection.Forward,126,28,48720,38976)]),
 new("02-範例二-站前交替折返",new(new(15,1,.8,.3),train2,[new("TURNBACK","TURNBACK","站前折返",SpatialReferencePointKind.BeforeStationTurnback,AlternateBerthing:true,MainlineGradePermille:5,DistanceFromStopToCrossoverMeters:15,CrossoverLengthMeters:170,TurnbackDwellSeconds:150,MainlineApproachCruiseSpeedMetersPerSecond:65/3.6,MainlineSafetyFactor:3.7)]),"6-4～6-7／PDF596～599","本例全域、列車、空間參數均有圖表明列。",[new("TURNBACK",SpatialCapacityDirection.Forward,175,20,33000,26400)]),
 new("03-範例三-S13雙方向",new(defaults,train3,[s13]),"6-7～6-12／PDF599～604",assumption,[new("S13",SpatialCapacityDirection.Forward,131,27,45900,36720),new("S13",SpatialCapacityDirection.Reverse,143,25,42500,34000)]),
 new("04-範例四-圖6-19停站40秒",new(defaults,train3 with {AccelerationMetersPerSecondSquared=1.1,DecelerationMetersPerSecondSquared=1.1},[s13Improved]),"6-12～6-13／PDF604～605",assumption+"圖6-19明列停站40秒，承上題則是45秒；保留原圖數值，不為對上發布結果改寫。",[new("S13",SpatialCapacityDirection.Forward,null,34),new("S13",SpatialCapacityDirection.Reverse,null,31)]),
 new("04b-範例四-承上題停站45秒",new(defaults,train3 with {AccelerationMetersPerSecondSquared=1.1,DecelerationMetersPerSecondSquared=1.1},[s13Improved with {StationForwardDwellSeconds=45,StationReverseDwellSeconds=45}]),"6-12～6-13／PDF604～605",assumption+"比較分支：沿用例三45秒，與圖6-19的40秒不同；不是原圖輸入。",[new("S13",SpatialCapacityDirection.Forward,null,34),new("S13",SpatialCapacityDirection.Reverse,null,31)]),
 new("05-範例五-臨時折返參考點",new(defaults,defaultTrain,[new("TEMP","S23-S24","臨時折返參考點",SpatialReferencePointKind.BeforeStationTurnback,DistanceFromStopToCrossoverMeters:30,CrossoverLengthMeters:85,TurnbackDwellSeconds:120,MainlineApproachCruiseSpeedMetersPerSecond:80/3.6,MainlineSafetyFactor:1)]),"6-14～6-15／PDF606～607",assumption+"列車未明列，補充採本機URCS預設141m、加減速度1、煞車0.75、352座、264m²、密度6。1.8km是路線示意，未填入參考點30m距離。",[new("TEMP",SpatialCapacityDirection.Forward,null,14)]),
 new("06-範例六-銜接點敏感度",new(defaults,defaultTrain,[new("JUNCTION","JUNCTION","平面銜接點",SpatialReferencePointKind.Junction,CrossoverLengthMeters:100,MainlineApproachCruiseSpeedMetersPerSecond:65/3.6,BranchlineApproachCruiseSpeedMetersPerSecond:65/3.6)]),"6-15～6-17／PDF607～609",assumption+"列車未明列，補充採本機URCS預設；手冊端點只有約90／約30列，不能作精確整數golden。",[]),
 new("07-範例七-四點瓶頸",new(defaults,train7,[s1,s7,s10,s14],CompareAsSingleScenario:true),"6-17～6-21／PDF609～613",assumption+"四點明確同屬手冊S1至S14容量情境；此處不是完整14站路網復原。",[new("S1",SpatialCapacityDirection.Forward,null,26),new("S7",SpatialCapacityDirection.Forward,null,25),new("S10",SpatialCapacityDirection.Forward,null,27),new("S14",SpatialCapacityDirection.Forward,null,13)]),
 new("07b-範例七-URCS相容對照",new(defaults,train7,[s1,s7,s10,s14],SpatialCapacityCalculationMode.UrcsCompatible,true),"6-17～6-21／PDF609～613",assumption+"保留歷史相容模式，以辨認坡度模式造成的差異。",[new("S1",SpatialCapacityDirection.Forward,null,26),new("S7",SpatialCapacityDirection.Forward,null,25),new("S10",SpatialCapacityDirection.Forward,null,27),new("S14",SpatialCapacityDirection.Forward,null,13)]),
 new("08-範例八-雙尾軌改善",new(defaults,train7,[s1,s7,s10,s14 with {AlternateBerthing=true}],CompareAsSingleScenario:true),"6-21～6-23／PDF613～615",assumption+"S14局部容量改善不等於整個四點情境提升為27列；仍需比較其它瓶頸。",[new("S14",SpatialCapacityDirection.Forward,null,27)]),
 new("08b-範例八-尾軌停等35秒",new(defaults,train7,[s1,s7,s10,s14 with {TurnbackDwellSeconds=35}],CompareAsSingleScenario:true),"6-22／PDF614",assumption+"手冊給定35秒以內可滿足150秒；此檔示範35秒的臨界附近結果。",[]),
 new("08c-範例八-尾軌停等33秒",new(defaults,train7,[s1,s7,s10,s14 with {TurnbackDwellSeconds=33}],CompareAsSingleScenario:true),"6-22／PDF614",assumption+"補充驗證分支：在本檔明列的全域假設下，33秒可滿足150秒班距；不是手冊原圖的輸入。",[])
};
var jsonOptions = new JsonSerializerOptions { WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase };
jsonOptions.Converters.Add(new JsonStringEnumConverter());
var records = new List<object>();
foreach(var example in cases)
{
 var doc=carrier with {ProjectId="HANDBOOK:"+example.File,ProjectName=example.File+"（容量案例／示意載體）",SpatialCapacity=example.Settings};
 var serialized=TopologyProjectFormat.Serialize(doc);
 var roundTrip=TopologyProjectFormat.Deserialize(serialized);
 var runtime=TopologyProjectFormat.CreateRuntime(roundTrip);
 if(!roundTrip.SpatialCapacity!.Points.SequenceEqual(example.Settings.Points) || roundTrip.SpatialCapacity.Mode!=example.Settings.Mode || roundTrip.SpatialCapacity.CompareAsSingleScenario!=example.Settings.CompareAsSingleScenario)
  throw new InvalidOperationException("容量保存往返不一致："+example.File);
 var world=new SimulationWorldOptions(Route:null,TrainParameters:runtime.TrainParameters,OperationalParameters:runtime.OperationalParameters,
 TrainCount:runtime.DispatchPlan.Runs.Count,InitialDepartureIntervalSeconds:doc.Simulation.HeadwaySeconds,ProfileMode:doc.Simulation.ProfileMode,
 MovingBlockMode:doc.Simulation.MovingBlockMode,InitialBrakingEstimationMode:doc.Simulation.BrakingEstimationMode,ServicePatterns:runtime.ServicePatterns,
 DispatchPlan:runtime.DispatchPlan,VehicleTypes:runtime.VehicleTypes,ServiceTypes:runtime.ServiceTypes,Topology:runtime.Topology).CreateWorld();
 world.AdvanceTo(10);
 File.WriteAllText(Path.Combine(output,example.File+".mrtsim.json"),serialized,Encoding.UTF8);
 var rows=new List<object>();
 foreach(var point in example.Settings.Points)
 foreach(var direction in point.Kind==SpatialReferencePointKind.IntermediateStation ? Enum.GetValues<SpatialCapacityDirection>() : [SpatialCapacityDirection.Forward])
 {
  var result=SpatialCapacityAnalysis.Calculate(SpatialCapacityProjectAnalysis.ToDefinition(point),example.Settings.Global,example.Settings.Train,direction,example.Settings.Mode);
  var published=example.Published.FirstOrDefault(value=>value.Id==point.ReferencePointId && value.Direction==direction);
  bool? matches=published is null ? null : (published.Headway is null || Math.Round(result.DesignHeadwaySeconds)==published.Headway)
    && (published.Line is null || result.LineCapacityPerHour==published.Line)
    && (published.Design is null || result.DesignPassengerCapacityPerHour==published.Design)
    && (published.Achievable is null || result.AchievablePassengerCapacityPerHour==published.Achievable);
  rows.Add(new {point.ReferencePointId,direction,result,published,matches});
  Console.WriteLine($"{example.File} {point.ReferencePointId} {direction}: h={result.DesignHeadwaySeconds:F6}, line={result.LineCapacityPerHour}, publishedMatch={matches}");
 }
 var bottlenecks=example.Settings.CompareAsSingleScenario ? SpatialCapacityScenarioAnalysis.Calculate(example.Settings).ToArray() : [];
 records.Add(new {example.File,example.Pages,example.Note,mode=example.Settings.Mode,carrier="專案維護範例01的三站示意載體；非手冊完整實體路網",validation="Schema8 validate/roundtrip/CreateRuntime/10-second carrier smoke passed",rows,bottlenecks});
}
File.WriteAllText(Path.Combine(output,"verification-results.json"),JsonSerializer.Serialize(records,jsonOptions),Encoding.UTF8);
WriteSweep(cases.Single(x=>x.File.StartsWith("06-")),"JUNCTION",SpatialCapacitySensitivityParameter.MainlineTrafficRatio,0,1,.1,"06-主線通過比例敏感度.csv");
WriteSweep(cases.Single(x=>x.File.StartsWith("07-")),"S14",SpatialCapacitySensitivityParameter.TurnbackDwellSeconds,10,120,5,"08-尾軌停等敏感度.csv");
Console.WriteLine($"Validated {cases.Count} loadable project files at {output}");

void WriteSweep(Example example,string id,SpatialCapacitySensitivityParameter parameter,double start,double end,double step,string file)
{
 var lines=new List<string>{"parameterValue,designHeadwaySeconds,trainsPerHour,designPassengerCapacity,achievablePassengerCapacity,meets150Seconds"};
 foreach(var row in SpatialCapacityScenarioAnalysis.AnalyzeSensitivity(example.Settings,id,parameter,start,end,step))
  lines.Add(FormattableString.Invariant($"{row.Value:R},{row.Result.DesignHeadwaySeconds:R},{row.Result.LineCapacityPerHour},{row.Result.DesignPassengerCapacityPerHour},{row.Result.AchievablePassengerCapacityPerHour},{row.Result.DesignHeadwaySeconds<=150}"));
 File.WriteAllLines(Path.Combine(output,file),lines,Encoding.UTF8);
}
static ProjectSpatialReferencePoint Station(string id,double[] f,double[] r) => new(id,id,id+" 中間站",SpatialReferencePointKind.IntermediateStation,
 StationForwardGradeInPermille:f[0],StationForwardGradeOutPermille:f[1],StationForwardDistanceToSignalMeters:f[2],StationForwardOverlapMeters:f[3],StationForwardDwellSeconds:f[4],StationForwardEarlierCruiseSpeedMetersPerSecond:f[5]/3.6,StationForwardLaterCruiseSpeedMetersPerSecond:f[6]/3.6,StationForwardSafetyFactor:f[7],
 StationReverseGradeInPermille:r[0],StationReverseGradeOutPermille:r[1],StationReverseDistanceToSignalMeters:r[2],StationReverseOverlapMeters:r[3],StationReverseDwellSeconds:r[4],StationReverseEarlierCruiseSpeedMetersPerSecond:r[5]/3.6,StationReverseLaterCruiseSpeedMetersPerSecond:r[6]/3.6,StationReverseSafetyFactor:r[7]);
record Published(string Id,SpatialCapacityDirection Direction,int? Headway=null,int? Line=null,int? Design=null,int? Achievable=null);
record Example(string File,SpatialCapacityProjectSettings Settings,string Pages,string Note,Published[] Published);
