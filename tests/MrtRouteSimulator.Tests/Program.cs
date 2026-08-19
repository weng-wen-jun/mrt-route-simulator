using MrtRouteSimulator.Engine;

var tests = new (string Name, Action Run)[]
{
    ("無限制性能不產生 NaN", TestUnlimitedPerformance),
    ("長距離使用梯形速度曲線", TestTrapezoidalProfile),
    ("短距離使用三角速度曲線", TestTriangularProfile),
    ("零距離回傳零時間", TestZeroDistance),
    ("非法列車性能回傳清楚錯誤", TestInvalidPerformance),
    ("負距離回傳清楚錯誤", TestNegativeDistance),
    ("站間距離正確累加為 position", TestRouteFactoryPositions),
    ("一站路線遭拒", TestSingleStationRoute),
    ("重複車站編號遭拒", TestDuplicateStationIds),
    ("停站到離站時間正確", TestDwellTime),
    ("五站全程時間逐項相符", TestFullTripTotals),
    ("起點與終點不重複計入一般停站", TestEndpointDwellDefinition),
    ("折返循環公式正確", TestCycleTime),
    ("十二列車理論班距與 ID 正確", TestMultipleTrains),
    ("使用者指定班距優先", TestExplicitHeadway),
    ("列車數量零遭拒", TestZeroTrainCount),
    ("Simulation Engine 呈現加速狀態", TestEngineAccelerationState),
    ("Simulation Engine 與解析抵達時間一致", TestEngineArrivalConsistency),
    ("Simulation Engine 終點進入折返", TestEngineTurnaroundState),
    ("Simulation Engine 下行位置反向遞減", TestEngineInboundDirection),
    ("Simulation Tick 固定為 0.1 秒", TestEngineFixedTick),
    ("多組物理參數距離與時間守恆", TestNumericalConservation),
    ("停站時間零仍能完成行程", TestZeroDwell),
    ("負停站與非連續 position 遭拒", TestRouteBoundaryValidation),
    ("V2 固定子步進與 Jerk 受限", TestV2FixedTickAndJerk),
    ("實際營運軌跡平順抵站且不越站", TestOperationalTripStopsAtStation),
    ("實際營運長區間包含惰行階段", TestOperationalTripContainsCoasting),
    ("里程速限重疊採最低且方向分離", TestSpeedLimitOverlapAndDirection),
    ("里程速限輸入精度與範圍驗證", TestSpeedLimitValidation),
    ("列車進入低速區前已提前煞車", TestAdvanceBrakingForSpeedLimit),
    ("多列車依同方向同軌形成相鄰配對", TestAdjacentTrainPairing),
    ("反應時間增加會放大安全距離", TestSafetyDistanceRespondsToReactionTime),
    ("移動閉塞控制不高於監視模式速度", TestMovingBlockControl),
    ("障礙物急停留下事件且列車不穿越", TestObstacleStopAndCollisionProtection),
    ("緊急煞車估算距離不大於營運煞車", TestBrakingModeSwitch),
    ("車輛 ID 與折返後車次 ID 分離", TestVehicleAndServiceRunIdentity),
    ("CSV 支援跨日時間及必要欄位", TestTrajectoryCsv),
    ("軌跡降採樣保留端點與相位轉折", TestTrajectoryDecimation),
    ("V2 首班列車在零秒準時啟用", TestInitialV2Departure),
    ("極短班距碰撞保護不產生負里程", TestCollisionProtectionClampsRouteBoundary),
    ("障礙物急停可指定列車與排程時間", TestScheduledObstacleStop)
};

var passed = 0;
var failures = new List<string>();

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("MRT 路線進出站時間模擬器 V2.0 - 自動化測試");
Console.WriteLine(new string('=', 58));

foreach (var test in tests)
{
    try
    {
        test.Run();
        passed++;
        Console.WriteLine($"[通過] {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"[失敗] {test.Name}");
        Console.WriteLine($"       {exception.Message}");
    }
}

Console.WriteLine(new string('-', 58));
Console.WriteLine($"結果：{passed}/{tests.Length} 通過，{failures.Count} 失敗");

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

return;

static void TestUnlimitedPerformance()
{
    var result = AnalyticalModel.CalculateSegmentTravelTime(
        1000,
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.PositiveInfinity);
    Equal(SpeedProfileType.Instantaneous, result.ProfileType);
    NearlyEqual(0, result.TravelTimeSeconds);
    True(double.IsFinite(result.TravelTimeSeconds), "時間不可為 NaN 或 Infinity。");
}

static void TestTrapezoidalProfile()
{
    var result = AnalyticalModel.CalculateSegmentTravelTime(5000, 22.222, 1, 1);
    Equal(SpeedProfileType.Trapezoidal, result.ProfileType);
    NearlyEqual(22.222, result.PeakSpeedMetersPerSecond);
    True(result.CruisingTimeSeconds > 0, "長區間應包含巡航階段。");
    NearlyEqual(5000, result.AccelerationDistanceMeters + result.CruisingDistanceMeters + result.DecelerationDistanceMeters);
}

static void TestTriangularProfile()
{
    var result = AnalyticalModel.CalculateSegmentTravelTime(200, 22.222, 1, 1);
    Equal(SpeedProfileType.Triangular, result.ProfileType);
    True(result.PeakSpeedMetersPerSecond < 22.222, "短區間峰值速度必須低於最高速度。");
    NearlyEqual(Math.Sqrt(200), result.PeakSpeedMetersPerSecond);
    NearlyEqual(200, result.AccelerationDistanceMeters + result.DecelerationDistanceMeters);
}

static void TestZeroDistance()
{
    var result = AnalyticalModel.CalculateSegmentTravelTime(0, 20, 1, 1);
    Equal(SpeedProfileType.Instantaneous, result.ProfileType);
    NearlyEqual(0, result.TravelTimeSeconds);
    NearlyEqual(0, result.PeakSpeedMetersPerSecond);
}

static void TestInvalidPerformance()
{
    Throws<SimulationValidationException>(() => new TrainParameters(0, 0, 0, 30, 180, 360), "最高速度");
    Throws<SimulationValidationException>(() => AnalyticalModel.CalculateSegmentTravelTime(100, 20, 0, 1), "加速度");
    Throws<SimulationValidationException>(() => AnalyticalModel.CalculateSegmentTravelTime(100, 20, 1, double.NaN), "減速度");
}

static void TestNegativeDistance()
{
    Throws<SimulationValidationException>(() => AnalyticalModel.CalculateSegmentTravelTime(-1, 20, 1, 1), "站間距離");
}

static void TestRouteFactoryPositions()
{
    var route = RouteFactory.FromSegmentDistances(
        "O",
        "測試線",
        [
            new StationInput("O01", "第一站", 0),
            new StationInput("O02", "第二站", 1000),
            new StationInput("O03", "第三站", 2000)
        ],
        30);
    NearlyEqual(0, route.Stations[0].PositionMeters);
    NearlyEqual(1000, route.Stations[1].PositionMeters);
    NearlyEqual(3000, route.Stations[2].PositionMeters);
    NearlyEqual(3000, route.TotalLengthMeters);
}

static void TestSingleStationRoute()
{
    Throws<SimulationValidationException>(
        () => new Route("O", "單站", [new Station("O01", "唯一站", 0, 30)]),
        "至少需要 2 個車站");
}

static void TestDuplicateStationIds()
{
    Throws<SimulationValidationException>(
        () => new Route(
            "O",
            "重複站",
            [new Station("O01", "第一站", 0, 30), new Station("O01", "第二站", 1000, 30)]),
        "重複");
}

static void TestDwellTime()
{
    var trip = TripSimulator.SimulateSingleTrip(CreateThreeStationRoute(), CreateParameters(), 0);
    var middle = trip.StationEvents[1];
    NearlyEqual(30, middle.DepartureTimeSeconds - middle.ArrivalTimeSeconds);
}

static void TestFullTripTotals()
{
    var route = CreateFiveStationRoute();
    var trip = TripSimulator.SimulateSingleTrip(route, CreateParameters(), 0);
    var expectedTravel = trip.Segments.Sum(segment => segment.Motion.TravelTimeSeconds);
    var expectedDwell = route.Stations.Skip(1).SkipLast(1).Sum(station => station.DwellTimeSeconds);
    NearlyEqual(expectedTravel, trip.TotalTravelTimeSeconds);
    NearlyEqual(expectedDwell, trip.TotalDwellTimeSeconds);
    NearlyEqual(expectedTravel + expectedDwell, trip.TotalRunTimeSeconds);
}

static void TestEndpointDwellDefinition()
{
    var route = new Route(
        "E",
        "端點定義",
        [new Station("E01", "起點", 0, 99), new Station("E02", "中間", 500, 25), new Station("E03", "終點", 1000, 88)]);
    var trip = TripSimulator.SimulateSingleTrip(route, CreateParameters(), 0);
    NearlyEqual(25, trip.TotalDwellTimeSeconds);
    NearlyEqual(0, trip.StationEvents[0].DwellTimeSeconds);
    NearlyEqual(0, trip.StationEvents[^1].DwellTimeSeconds);
}

static void TestCycleTime()
{
    var route = CreateFiveStationRoute();
    var parameters = CreateParameters();
    var cycle = TripSimulator.CalculateCycleTime(route, parameters);
    var expected = cycle.OutboundTrip.TotalRunTimeSeconds
        + 360
        + cycle.InboundTrip.TotalRunTimeSeconds
        + 180;
    NearlyEqual(expected, cycle.CycleTimeSeconds);
}

static void TestMultipleTrains()
{
    var route = CreateFiveStationRoute();
    var parameters = CreateParameters();
    var result = TripSimulator.SimulateMultipleTrains(route, parameters, 12);
    Equal(12, result.Trains.Count);
    Equal(12, result.Trains.Select(train => train.TrainId).Distinct().Count());
    NearlyEqual(result.CycleTimeSeconds / 12, result.HeadwaySeconds);
    for (var index = 1; index < result.Trains.Count; index++)
    {
        NearlyEqual(result.HeadwaySeconds, result.Trains[index].InitialDepartureTimeSeconds - result.Trains[index - 1].InitialDepartureTimeSeconds);
        Equal(route.Stations.Count, result.Trains[index].OutboundTrip.StationEvents.Count);
    }
}

static void TestExplicitHeadway()
{
    var result = TripSimulator.SimulateMultipleTrains(CreateThreeStationRoute(), CreateParameters(), 3, 90);
    NearlyEqual(90, result.HeadwaySeconds);
    NearlyEqual(180, result.Trains[2].InitialDepartureTimeSeconds);
}

static void TestZeroTrainCount()
{
    Throws<SimulationValidationException>(
        () => TripSimulator.SimulateMultipleTrains(CreateThreeStationRoute(), CreateParameters(), 0),
        "列車數量");
}

static void TestEngineAccelerationState()
{
    var engine = new SimulationEngine(CreateThreeStationRoute(), CreateParameters(), 1);
    var state = engine.GetTrainStates(1)[0];
    Equal(TrainMotionState.Accelerating, state.State);
    NearlyEqual(0.5, state.PositionMeters);
    NearlyEqual(1, state.SpeedMetersPerSecond);
}

static void TestEngineArrivalConsistency()
{
    var route = CreateThreeStationRoute();
    var parameters = CreateParameters();
    var analytical = TripSimulator.SimulateSingleTrip(route, parameters, 0);
    var engine = new SimulationEngine(route, parameters, 1);
    var state = engine.GetTrainStates(analytical.TerminalArrivalTimeSeconds)[0];
    Equal(TrainMotionState.Arriving, state.State);
    NearlyEqual(route.TotalLengthMeters, state.PositionMeters, 0.01);
    NearlyEqual(0, state.SpeedMetersPerSecond, 0.01);
}

static void TestEngineTurnaroundState()
{
    var route = CreateThreeStationRoute();
    var parameters = CreateParameters();
    var trip = TripSimulator.SimulateSingleTrip(route, parameters, 0);
    var engine = new SimulationEngine(route, parameters, 1);
    var state = engine.GetTrainStates(trip.TerminalArrivalTimeSeconds + 1)[0];
    Equal(TrainMotionState.Turning, state.State);
    Equal(TrainDirection.Inbound, state.Direction);
    NearlyEqual(route.TotalLengthMeters, state.PositionMeters);
}

static void TestEngineInboundDirection()
{
    var route = CreateThreeStationRoute();
    var parameters = CreateParameters();
    var outbound = TripSimulator.SimulateSingleTrip(route, parameters, 0);
    var inboundStart = outbound.TerminalArrivalTimeSeconds + parameters.TerminalTurnaroundTimeSeconds;
    var engine = new SimulationEngine(route, parameters, 1);
    var first = engine.GetTrainStates(inboundStart + 1)[0];
    var second = engine.GetTrainStates(inboundStart + 2)[0];
    Equal(TrainDirection.Inbound, first.Direction);
    True(second.PositionMeters < first.PositionMeters, "下行返回起點時 position 應遞減。");
}

static void TestEngineFixedTick()
{
    var engine = new SimulationEngine(CreateThreeStationRoute(), CreateParameters(), 1, timeStepSeconds: 0.1);
    for (var index = 0; index < 10; index++)
    {
        engine.Tick();
    }

    NearlyEqual(1, engine.CurrentTimeSeconds, 1e-12);
}

static void TestNumericalConservation()
{
    var distances = new[] { 1d, 25, 100, 200, 500, 1000, 5000, 12500 };
    var speeds = new[] { 5d, 12.5, 22.222, 30 };
    foreach (var distance in distances)
    {
        foreach (var speed in speeds)
        {
            var result = AnalyticalModel.CalculateSegmentTravelTime(distance, speed, 0.7, 1.1);
            NearlyEqual(distance, result.AccelerationDistanceMeters + result.CruisingDistanceMeters + result.DecelerationDistanceMeters, 1e-8);
            NearlyEqual(result.TravelTimeSeconds, result.AccelerationTimeSeconds + result.CruisingTimeSeconds + result.DecelerationTimeSeconds, 1e-8);
            True(result.PeakSpeedMetersPerSecond <= speed + 1e-8, "峰值速度不得超過最高速度。");
        }
    }
}

static void TestZeroDwell()
{
    var route = new Route(
        "Z",
        "零停站",
        [new Station("Z01", "甲", 0, 0), new Station("Z02", "乙", 500, 0), new Station("Z03", "丙", 1200, 0)]);
    var trip = TripSimulator.SimulateSingleTrip(route, new TrainParameters(20, 1, 1, 0, 0, 0), 0);
    NearlyEqual(0, trip.TotalDwellTimeSeconds);
    NearlyEqual(trip.TotalTravelTimeSeconds, trip.TotalRunTimeSeconds);
}

static void TestRouteBoundaryValidation()
{
    Throws<SimulationValidationException>(
        () => new Route(
            "B",
            "錯誤路線",
            [new Station("B01", "甲", 0, -1), new Station("B02", "乙", 0, 30)]),
        "停站時間");
}

static void TestV2FixedTickAndJerk()
{
    var world = CreateWorld(trainCount: 1, headwaySeconds: 60);
    for (var index = 0; index < 80; index++)
    {
        world.Tick();
    }

    NearlyEqual(8, world.CurrentTimeSeconds, 1e-9);
    NearlyEqual(0.1, world.TimeStepSeconds, 1e-9);
    var samples = world.Trajectory.Where(sample => sample.VehicleId == "Vehicle 01").ToArray();
    for (var index = 1; index < samples.Length; index++)
    {
        var jerk = Math.Abs(samples[index].AccelerationMetersPerSecondSquared
            - samples[index - 1].AccelerationMetersPerSecondSquared) / 0.1;
        True(jerk <= 0.650001, $"Jerk 超過限制：{jerk}");
        True(samples[index].PositionMeters + 1e-8 >= samples[index - 1].PositionMeters, "下行位置不得倒退。");
    }
}

static void TestOperationalTripStopsAtStation()
{
    var route = CreateThreeStationRoute();
    var result = OperationalTrajectoryPlanner.GenerateOutboundTrip(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault());
    var final = result.Samples[^1];
    NearlyEqual(route.TotalLengthMeters, final.PositionMeters, 0.01);
    NearlyEqual(0, final.SpeedMetersPerSecond, 0.01);
    True(result.MaximumObservedJerkMetersPerSecondCubed <= 0.650001, "軌跡 Jerk 應受限制。");
    True(result.Samples.All(sample => sample.PositionMeters <= route.TotalLengthMeters + 1e-8), "位置不得越站。");
}

static void TestOperationalTripContainsCoasting()
{
    var route = RouteFactory.FromSegmentDistances(
        "L",
        "長區間",
        [new StationInput("L01", "甲", 0, 0), new StationInput("L02", "乙", 5000, 0)],
        0);
    var result = OperationalTrajectoryPlanner.GenerateOutboundTrip(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault());
    True(result.Samples.Any(sample => sample.Phase == OperationalPhase.Coasting), "長區間應出現惰行階段。");
}

static void TestSpeedLimitOverlapAndDirection()
{
    var route = CreateFiveStationRoute();
    var service = new SpeedLimitService(route,
    [
        new SpeedLimitSegment(500, 1500, 50 / 3.6, SpeedLimitDirection.Both, "彎道"),
        new SpeedLimitSegment(1000, 2000, 35 / 3.6, SpeedLimitDirection.Outbound, "道岔")
    ]);
    NearlyEqual(35 / 3.6, service.GetCurrentLimitMetersPerSecond(1200, TrainDirection.Outbound, 80 / 3.6));
    NearlyEqual(50 / 3.6, service.GetCurrentLimitMetersPerSecond(1200, TrainDirection.Inbound, 80 / 3.6));
    True(service.GetOverlapWarnings().Count == 1, "重疊速限應產生一項提示。");
}

static void TestSpeedLimitValidation()
{
    var route = CreateThreeStationRoute();
    Throws<SimulationValidationException>(
        () => _ = new SpeedLimitService(route, [new SpeedLimitSegment(1255, 1870, 45 / 3.6)]),
        "0.01 km");
    Throws<SimulationValidationException>(
        () => _ = new SpeedLimitService(route, [new SpeedLimitSegment(100, route.TotalLengthMeters + 10, 45 / 3.6)]),
        "里程必須位於");
}

static void TestAdvanceBrakingForSpeedLimit()
{
    var route = RouteFactory.FromSegmentDistances(
        "S",
        "速限線",
        [new StationInput("S01", "甲", 0, 0), new StationInput("S02", "乙", 3000, 0)],
        0);
    var limit = new SpeedLimitSegment(1250, 1870, 45 / 3.6, SpeedLimitDirection.Both, "測試");
    var result = OperationalTrajectoryPlanner.GenerateOutboundTrip(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        [limit]);
    var beforeBoundary = result.Samples.Last(sample => sample.PositionMeters <= 1250 + 0.01);
    True(beforeBoundary.SpeedMetersPerSecond <= 45 / 3.6 + 0.15, $"進入速限前速度過高：{beforeBoundary.SpeedMetersPerSecond * 3.6:0.##} km/h");
    True(result.Samples.Any(sample => sample.PositionMeters < 1250 && sample.Phase is OperationalPhase.Braking or OperationalPhase.ApproachBraking), "應在速限起點前開始煞車。");
}

static void TestAdjacentTrainPairing()
{
    var world = CreateWorld(trainCount: 3, headwaySeconds: 12);
    world.AdvanceTo(40);
    var snapshot = world.GetSnapshot();
    Equal(3, snapshot.Trains.Count(train => train.IsActive));
    Equal(2, snapshot.SafetyObservations.Count);
    Equal(2, snapshot.SafetyObservations.Select(item => item.FollowerVehicleId).Distinct().Count());
}

static void TestSafetyDistanceRespondsToReactionTime()
{
    var normal = CreateWorld(trainCount: 2, headwaySeconds: 15, reactionTime: 1.5);
    var delayed = CreateWorld(trainCount: 2, headwaySeconds: 15, reactionTime: 3.0);
    normal.AdvanceTo(45);
    delayed.AdvanceTo(45);
    var normalDistance = normal.GetSnapshot().SafetyObservations.Single().DynamicSafetyDistanceMeters;
    var delayedDistance = delayed.GetSnapshot().SafetyObservations.Single().DynamicSafetyDistanceMeters;
    True(delayedDistance > normalDistance, "較長反應時間應增加動態安全距離。");
}

static void TestMovingBlockControl()
{
    var monitor = CreateWorld(trainCount: 2, headwaySeconds: 6, movingBlockMode: MovingBlockMode.Monitoring);
    var control = CreateWorld(trainCount: 2, headwaySeconds: 6, movingBlockMode: MovingBlockMode.Control);
    monitor.AdvanceTo(40);
    control.AdvanceTo(40);
    var monitorFollower = monitor.GetSnapshot().Trains.Single(train => train.VehicleId == "Vehicle 02");
    var controlFollower = control.GetSnapshot().Trains.Single(train => train.VehicleId == "Vehicle 02");
    True(controlFollower.SpeedMetersPerSecond <= monitorFollower.SpeedMetersPerSecond + 0.01, "控制模式後車速度不應高於監視模式。");
}

static void TestObstacleStopAndCollisionProtection()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 8, movingBlockMode: MovingBlockMode.Control);
    world.AdvanceTo(35);
    world.TriggerObstacleEmergencyStop("Vehicle 01");
    world.AdvanceTo(80);
    True(world.Events.Any(item => item.EventType == SimulationEventType.ObstacleEmergencyStop), "應記錄障礙物急停事件。");
    var snapshot = world.GetSnapshot();
    var leader = snapshot.Trains.Single(train => train.VehicleId == "Vehicle 01");
    var follower = snapshot.Trains.Single(train => train.VehicleId == "Vehicle 02");
    True(follower.FrontPositionMeters <= leader.RearPositionMeters + 1e-6, "後車不得穿越前車車尾。");
}

static void TestBrakingModeSwitch()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 15);
    world.AdvanceTo(45);
    var service = world.GetSnapshot().SafetyObservations.Single().ObstacleBrakingDemandMeters;
    world.SetBrakingEstimationMode(BrakingEstimationMode.Emergency);
    var emergency = world.GetSnapshot().SafetyObservations.Single().ObstacleBrakingDemandMeters;
    True(emergency <= service, "緊急煞車估算距離不應大於營運煞車。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.BrakingModeChanged), "切換時應記錄事件。");
}

static void TestVehicleAndServiceRunIdentity()
{
    var route = RouteFactory.FromSegmentDistances(
        "I",
        "識別測試",
        [new StationInput("I01", "甲", 0, 0), new StationInput("I02", "乙", 300, 0)],
        0);
    var parameters = new TrainParameters(15, 1, 1, 0, 0, 0);
    var world = new SimulationWorld(route, parameters, OperationalParameters.CreateDefault(), 1);
    world.AdvanceTo(120);
    var runs = world.Trajectory.Select(sample => sample.ServiceRunId).Distinct().ToArray();
    Equal(1, world.Trajectory.Select(sample => sample.VehicleId).Distinct().Count());
    True(runs.Length >= 2, "折返後應建立新車次 ID。");
    True(world.Trajectory.Any(sample => sample.Direction == TrainDirection.Inbound && sample.TrackId == "UP"), "折返後應切換方向與軌道。");
}

static void TestTrajectoryCsv()
{
    var world = CreateWorld(trainCount: 1, headwaySeconds: 60);
    world.AdvanceTo(2);
    var csv = TrajectoryAnalysis.BuildCsv(CreateFiveStationRoute(), world.Trajectory, world.Events, 86399);
    True(csv.StartsWith("vehicle_id,service_run_id", StringComparison.Ordinal), "CSV 應包含必要欄位。");
    True(csv.Contains("+1日", StringComparison.Ordinal), "跨午夜時間應包含 +1日。");
    True(csv.Contains("Vehicle 01", StringComparison.Ordinal), "CSV 應包含車輛 ID。");
}

static void TestTrajectoryDecimation()
{
    var world = CreateWorld(trainCount: 1, headwaySeconds: 60);
    world.AdvanceTo(80);
    var source = world.Trajectory.ToArray();
    var decimated = TrajectoryAnalysis.DecimatePreservingCriticalPoints(source, 60);
    NearlyEqual(source[0].SimulationTimeSeconds, decimated[0].SimulationTimeSeconds, 1e-9);
    NearlyEqual(source[^1].SimulationTimeSeconds, decimated[^1].SimulationTimeSeconds, 1e-9);
    True(decimated.Select(sample => sample.Phase).Distinct().Count() >= 2, "相位轉折應保留。");
}

static void TestInitialV2Departure()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 60);
    var snapshot = world.GetSnapshot();
    True(snapshot.Trains.Single(train => train.VehicleId == "Vehicle 01").IsActive, "首班列車應在 0 秒啟用。");
    True(!snapshot.Trains.Single(train => train.VehicleId == "Vehicle 02").IsActive, "後續列車應等待排定班距。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure && item.SimulationTimeSeconds == 0), "零秒應記錄發車事件。");
}

static void TestCollisionProtectionClampsRouteBoundary()
{
    var world = CreateWorld(trainCount: 4, headwaySeconds: 3);
    world.AdvanceTo(25);
    True(world.GetSnapshot().Trains.All(train => train.FrontPositionMeters >= -1e-9), "碰撞停止位置不得小於路線起點。");
    True(world.GetSnapshot().Trains.All(train => train.FrontPositionMeters <= world.Route.TotalLengthMeters + 1e-9), "碰撞停止位置不得超過路線終點。");
}

static void TestScheduledObstacleStop()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 30);
    world.ScheduleObstacleEmergencyStop("Vehicle 01", 20);
    world.AdvanceTo(19.9);
    True(!world.Events.Any(item => item.EventType == SimulationEventType.ObstacleEmergencyStop), "排程時間前不得觸發。");
    world.AdvanceTo(20);
    True(world.Events.Any(item => item.EventType == SimulationEventType.ObstacleEmergencyStop && item.VehicleId == "Vehicle 01"), "指定列車應在排程時間觸發。");
}

static SimulationWorld CreateWorld(
    int trainCount,
    double headwaySeconds,
    double reactionTime = 1.5,
    MovingBlockMode movingBlockMode = MovingBlockMode.Monitoring)
{
    var defaults = OperationalParameters.CreateDefault();
    var operational = new OperationalParameters(
        defaults.JerkMetersPerSecondCubed,
        defaults.CoastingRatio,
        defaults.ApproachDistanceMeters,
        defaults.ApproachSpeedMetersPerSecond,
        defaults.TractionFadeRatio,
        defaults.TrainLengthMeters,
        defaults.ServiceBrakingMetersPerSecondSquared,
        defaults.EmergencyBrakingMetersPerSecondSquared,
        reactionTime,
        defaults.BrakeBuildUpTimeSeconds,
        defaults.PositioningErrorMeters,
        defaults.SafetyMarginMeters,
        defaults.AbsoluteMinimumGapMeters);
    return new SimulationWorld(
        CreateFiveStationRoute(),
        CreateParameters(),
        operational,
        trainCount,
        headwaySeconds,
        movingBlockMode: movingBlockMode);
}

static Route CreateThreeStationRoute() => RouteFactory.FromSegmentDistances(
    "O",
    "橘色測試線",
    [
        new StationInput("O01", "起點站", 0, 0),
        new StationInput("O02", "中央站", 1000, 30),
        new StationInput("O03", "終點站", 2000, 0)
    ],
    30);

static Route CreateFiveStationRoute() => RouteFactory.FromSegmentDistances(
    "T",
    "五站測試線",
    [
        new StationInput("T01", "起點", 0, 0),
        new StationInput("T02", "東站", 800, 25),
        new StationInput("T03", "中央站", 1200, 35),
        new StationInput("T04", "西站", 650, 20),
        new StationInput("T05", "終點", 1350, 0)
    ],
    30);

static TrainParameters CreateParameters() => new(22.222, 1, 1, 30, 180, 360);

static void NearlyEqual(double expected, double actual, double tolerance = 0.01)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"預期 {expected:0.########}，實際 {actual:0.########}，容差 {tolerance}。");
    }
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"預期 {expected}，實際 {actual}。");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<TException>(Action action, string expectedMessage)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        if (!exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"錯誤訊息未包含「{expectedMessage}」：{exception.Message}");
        }

        return;
    }

    throw new InvalidOperationException($"預期拋出 {typeof(TException).Name}。");
}
