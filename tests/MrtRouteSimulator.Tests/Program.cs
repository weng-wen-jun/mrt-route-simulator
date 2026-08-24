using System.Text.Json.Nodes;
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
    ("移動閉塞控制主動制動並維持安全距離", TestMovingBlockControl),
    ("障礙物急停留下事件且列車不穿越", TestObstacleStopAndCollisionProtection),
    ("緊急煞車估算距離不大於營運煞車", TestBrakingModeSwitch),
    ("車輛 ID 與折返後車次 ID 分離", TestVehicleAndServiceRunIdentity),
    ("CSV 支援跨日時間及必要欄位", TestTrajectoryCsv),
    ("軌跡降採樣保留端點與相位轉折", TestTrajectoryDecimation),
    ("V2 首班列車在零秒準時啟用", TestInitialV2Departure),
    ("極短班距碰撞保護不產生負里程", TestCollisionProtectionClampsRouteBoundary),
    ("障礙物急停可指定列車與排程時間", TestScheduledObstacleStop),
    ("終點長折返時控制模式不發生追撞", TestTerminalOccupancyProtection),
    ("控制模式在起點未淨空時延後發車", TestDepartureClearanceProtection),
    ("同一追撞只記錄一次碰撞事件", TestCollisionEventRecordedOnce),
    ("動態煞車包絡線使到站前速度接近零", TestDynamicStationBrakingContinuity),
    ("移動閉塞控制不再把高速列車瞬間歸零", TestMovingBlockControlDoesNotHardStop),
    ("跨站車不套用固定進站速度或停站", TestExpressServicePassesStation),
    ("跨站車仍遵守車站通過速限", TestPassingServiceHonorsStationLimit),
    ("折返後可套用不同停站模式", TestTurnaroundLoadsDifferentServicePattern),
    ("專案存檔 JSON 可完整往返", TestSimulationProjectRoundTrip),
    ("舊 Schema 存檔會明確拒絕", TestLegacyProjectRejected),
    ("專案存檔拒絕未知版本與破損 JSON", TestSimulationProjectValidation),
    ("軟體版本符合三段式規則且與組件一致", TestProductVersionMetadata),
    ("Schema 7 派車計畫支援上下行與跨午夜排序", TestDispatchPlanExpansion),
    ("Schema 7 派車計畫拒絕缺漏目錄參照", TestDispatchPlanMissingCatalogReference),
    ("V2 寫實引擎依派車計畫從兩端發車並保留結構化識別", TestDispatchWorldFromBothEnds),
    ("V3 折返續行選項可展開並由存檔保留", TestDispatchContinuationPersistence),
    ("V3 未續行車完成端點清車後退出並消失", TestDispatchTerminalExitAfterDwell),
    ("V3 續行車完成端點作業後折返為新車次", TestDispatchTerminalContinuation),
    ("V3 手動班表可將下行車次接續為指定上行車次", TestDispatchSpecificContinuationChain),
    ("V2 寫實引擎拒絕重複指定車輛", TestDispatchDuplicateVehicleRejected),
    ("V3 舊路線與自訂資源會產生鎖定事件", TestInfrastructureResourceLockEvents),
    ("V2 區間統計支援完整篩選、控制受限秒數與 P95", TestIntervalStatistics),
    ("Schema 7 存檔不再寫出舊執行資料源", TestCanonicalSchemaOmitsLegacySources),
    ("產品 EngineKind 與 ProfileMode 可獨立設定", TestEngineKindProfileModeSeparation),
    ("V3.2 五類空間參考點欄位與上下限有效", TestSpatialReferencePointValidation),
    ("V3.3 空間參考點可由 Schema 7 完整存取", TestSpatialReferencePointPersistence),
    ("V3.2 站後折返幾何、道岔限速與停等時間會進入模擬", TestSpatialReferencePointTurnbackTiming),
    ("V3.2 URCS 五類預設安全時距與容量相符", TestSpatialCapacityDefaultVectors),
    ("V3.2 URCS 中間站順逆行參數獨立", TestSpatialCapacityStationDirections),
    ("V3.2 URCS 容量採截斷且拒絕無效坡度組合", TestSpatialCapacityTruncationAndValidation),
    ("V3.2 中間站方向別停站設定接入 SimulationWorld", TestSpatialStationDwellIntegration),
    ("V3.2 首班零秒與端點退出完整進入時刻表及區間統計", TestV3TimetableAndIntervalTerminalBoundaries),
    ("V3.3 完整功能範例可讀取並產生主要營運事件", TestComprehensiveSampleProject),
    ("V2 車型目錄性能成為列車運算權威", TestVehicleCatalogPerformanceAuthority)
};

var passed = 0;
var failures = new List<string>();

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"MRT 路線進出站時間模擬器 {ProductVersion.Current} - 自動化測試");
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

static void TestProductVersionMetadata()
{
    var displayVersion = ProductVersion.Current;
    True(displayVersion.StartsWith('V'), "顯示版本必須以大寫 V 開頭。");

    var segments = displayVersion[1..].Split('.');
    Equal(3, segments.Length);
    True(
        segments.All(segment => int.TryParse(segment, out _)),
        "版本號必須是 V主版本.次版本.修訂版本，且三段皆為非負整數。");

    var assemblyVersion = typeof(ProductVersion).Assembly.GetName().Version
        ?? throw new InvalidOperationException("找不到 Engine 組件版本。");
    var assemblySemanticVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    Equal(displayVersion[1..], assemblySemanticVersion);
}

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
    var world = CreateWorld(trainCount: 3, headwaySeconds: 30, movingBlockMode: MovingBlockMode.Control);
    world.AdvanceTo(100);
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
    var world = CreateWorld(trainCount: 2, headwaySeconds: 20, movingBlockMode: MovingBlockMode.Control);
    world.AdvanceTo(80);
    True(world.Events.Any(item => item.EventType == SimulationEventType.ControlBraking), "控制模式應在安全包絡不足時主動制動。");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision), "控制模式不應發生追撞。");
    True(
        world.SafetyHistory.All(item => item.ActualGapMeters >= item.DynamicSafetyDistanceMeters - 1e-6),
        "控制模式必須維持完整動態安全距離。");
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
    True(csv.StartsWith("software_version,model_version,vehicle_id,service_run_id", StringComparison.Ordinal),
        "CSV 應包含版本標籤與必要欄位。");
    True(csv.Contains(ProductVersion.Current, StringComparison.Ordinal)
        && csv.Contains("V2 SimulationWorld", StringComparison.Ordinal), "CSV 每列都應標示軟體與模型版本。");
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

static void TestTerminalOccupancyProtection()
{
    var route = RouteFactory.FromSegmentDistances(
        "E",
        "終點占用測試線",
        [new StationInput("E01", "起點", 0, 0), new StationInput("E02", "終點", 1000, 0)],
        0);
    var parameters = new TrainParameters(20, 1, 1, 0, 30, 180);
    var world = new SimulationWorld(
        route,
        parameters,
        OperationalParameters.CreateDefault(),
        trainCount: 3,
        initialDepartureIntervalSeconds: 25,
        movingBlockMode: MovingBlockMode.Control);

    world.AdvanceTo(240);

    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision), "控制模式在終點長折返占用期間不應發生追撞。");
    var minimumMargin = world.SafetyHistory.MinBy(item => item.ActualGapMeters - item.DynamicSafetyDistanceMeters)!;
    True(
        world.SafetyHistory.All(item => item.ActualGapMeters >= item.DynamicSafetyDistanceMeters - 1e-6),
        $"控制模式必須維持完整動態安全距離；最低裕度 {minimumMargin.ActualGapMeters - minimumMargin.DynamicSafetyDistanceMeters:0.###} m，時間 {minimumMargin.SimulationTimeSeconds:0.0} s。");
}

static void TestDepartureClearanceProtection()
{
    var world = CreateWorld(trainCount: 3, headwaySeconds: 3, movingBlockMode: MovingBlockMode.Control);
    world.AdvanceTo(30);

    var secondDeparture = world.Events.Single(item =>
        item.EventType == SimulationEventType.Departure
        && item.VehicleId == "Vehicle 02");
    True(secondDeparture.SimulationTimeSeconds > 3, "起點未淨空時，第二列車不得照原排定時間強制發車。");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision), "延後發車後不得在起點追撞。");
    True(
        world.SafetyHistory.All(item => item.ActualGapMeters >= item.DynamicSafetyDistanceMeters - 1e-6),
        "起點發車也必須維持完整動態安全距離。");
}

static void TestCollisionEventRecordedOnce()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 3, movingBlockMode: MovingBlockMode.Monitoring);
    world.AdvanceTo(20);

    var collisions = world.Events.Count(item =>
        item.EventType == SimulationEventType.Collision
        && item.VehicleId == "Vehicle 02");
    Equal(1, collisions);
}

static void TestDynamicStationBrakingContinuity()
{
    var route = CreateThreeStationRoute();
    var result = OperationalTrajectoryPlanner.GenerateOutboundTrip(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault());
    var arrivals = result.Events.Where(item => item.EventType == SimulationEventType.Arrival).ToArray();
    Equal(2, arrivals.Length);
    foreach (var arrival in arrivals)
    {
        var prior = result.Samples.Last(sample => sample.SimulationTimeSeconds < arrival.SimulationTimeSeconds);
        True(prior.SpeedMetersPerSecond <= 1 / 3.6 + 1e-9,
            $"{arrival.PositionMeters:0.#} m 到站前速度仍過高：{prior.SpeedMetersPerSecond * 3.6:0.##} km/h。");
        True(Math.Abs(arrival.PositionMeters - prior.PositionMeters) <= 0.5 + 1e-7,
            "到站前一個 Tick 必須已位於停車點容許範圍。");
    }

    True(result.Events.All(item => item.EventType != SimulationEventType.StationStopViolation),
        "預設參數不得產生停車超限事件。");
    var envelope = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
        80 / 3.6,
        1,
        0.9,
        0.65,
        0.1,
        jerkLimited: true);
    True(envelope.DistanceMeters > 0 && envelope.DurationSeconds > 0, "動態煞停距離與時間必須為正值。");
}

static void TestMovingBlockControlDoesNotHardStop()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 15, movingBlockMode: MovingBlockMode.Control);
    world.AdvanceTo(800);
    var stationPositions = CreateFiveStationRoute().Stations.Select(station => station.PositionMeters).ToArray();

    foreach (var group in world.Trajectory.GroupBy(sample => sample.VehicleId))
    {
        var samples = group.ToArray();
        for (var index = 1; index < samples.Length; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            var nearStation = stationPositions.Any(position => Math.Abs(position - current.PositionMeters) <= 0.6);
            True(previous.SpeedMetersPerSecond <= 5
                    || current.SpeedMetersPerSecond > 0.01
                    || nearStation,
                $"{current.VehicleId} 在 {current.SimulationTimeSeconds:0.0} s 被移動閉塞由 "
                    + $"{previous.SpeedMetersPerSecond * 3.6:0.##} km/h 瞬間歸零。");
        }
    }

    True(world.Events.All(item => item.EventType != SimulationEventType.Collision), "控制模式不得產生碰撞事件。");
}

static void TestExpressServicePassesStation()
{
    var world = CreateServicePatternWorld(passingSpeedKmh: null, inboundAllStop: false);
    world.AdvanceTo(300);

    var pass = world.Events.Single(item => item.EventType == SimulationEventType.StationPassed
        && item.Direction == TrainDirection.Outbound
        && Math.Abs(item.PositionMeters - 1000) < 0.01);
    True(pass.SpeedMetersPerSecond * 3.6 > 30, "未指定通過速限時不得被固定 30 km/h 進站值限制。");
    True(world.Events.All(item => item.EventType != SimulationEventType.Arrival
        || item.Direction != TrainDirection.Outbound
        || Math.Abs(item.PositionMeters - 1000) > 0.01), "跨站車不得在跨站車站產生抵達事件。");
}

static void TestPassingServiceHonorsStationLimit()
{
    var world = CreateServicePatternWorld(passingSpeedKmh: 40, inboundAllStop: false);
    world.AdvanceTo(300);

    var pass = world.Events.Single(item => item.EventType == SimulationEventType.StationPassed
        && item.Direction == TrainDirection.Outbound
        && Math.Abs(item.PositionMeters - 1000) < 0.01);
    True(pass.SpeedMetersPerSecond * 3.6 <= 40.6,
        $"跨站速度超過 40 km/h 上限：{pass.SpeedMetersPerSecond * 3.6:0.##} km/h。");
}

static void TestTurnaroundLoadsDifferentServicePattern()
{
    var world = CreateServicePatternWorld(passingSpeedKmh: null, inboundAllStop: true);
    world.AdvanceTo(700);

    True(world.Events.Any(item => item.EventType == SimulationEventType.StationPassed
        && item.Direction == TrainDirection.Outbound
        && Math.Abs(item.PositionMeters - 1000) < 0.01), "下行快速車應跨越中間站。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.Arrival
        && item.Direction == TrainDirection.Inbound
        && Math.Abs(item.PositionMeters - 1000) < 0.01), "折返後上行普通車應停靠中間站。");
}

static SimulationWorld CreateServicePatternWorld(double? passingSpeedKmh, bool inboundAllStop)
{
    var patterns = new[]
    {
        new ServicePattern(
            "EXPRESS",
            "快速車",
            [new StationServiceInstruction("O02", StationServiceMode.Pass, passingSpeedKmh / 3.6)])
    };
    var plans = new List<ServiceRunPlan>
    {
        new("Vehicle 01", 1, TrainDirection.Outbound, "快速車", "EXPRESS")
    };
    if (inboundAllStop)
    {
        plans.Add(new ServiceRunPlan("Vehicle 01", 2, TrainDirection.Inbound, "普通車", "ALL_STOP"));
    }

    return new SimulationWorld(
        CreateThreeStationRoute(),
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        1,
        movingBlockMode: MovingBlockMode.Independent,
        servicePatterns: patterns,
        serviceRunPlans: plans);
}

static void TestDispatchPlanExpansion()
{
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var plan = new DispatchPlanDefinition(
        [
            new HeadwayDirectionPlan(
                TrainDirection.Outbound,
                new TimeSpan(23, 50, 0),
                TimeSpan.FromMinutes(10),
                2,
                "LOCAL",
                "EMU-6",
                "ALL_STOP"),
            new HeadwayDirectionPlan(
                TrainDirection.Inbound,
                new TimeSpan(0, 10, 0),
                TimeSpan.FromMinutes(10),
                1,
                "LOCAL",
                "EMU-6",
                "ALL_STOP")
        ],
        [],
        DispatchPlanningMode.SimpleHeadway);

    var expanded = DispatchPlanExpander.Expand(plan, vehicles, services, stops);
    Equal(3, expanded.Runs.Count);
    Equal(TrainDirection.Outbound, expanded.Runs[0].Direction);
    Equal(TrainDirection.Outbound, expanded.Runs[1].Direction);
    Equal(TrainDirection.Inbound, expanded.Runs[2].Direction);
    Equal(new TimeSpan(23, 50, 0), expanded.Runs[0].PlannedDepartureTime);
    Equal(TimeSpan.Zero, expanded.Runs[1].PlannedDepartureTime);
    Equal(new TimeSpan(0, 10, 0), expanded.Runs[2].PlannedDepartureTime);
    True(expanded.Runs[0].Sequence < expanded.Runs[1].Sequence
        && expanded.Runs[1].Sequence < expanded.Runs[2].Sequence, "跨午夜班次應依相對服務日排序。");
}

static void TestDispatchPlanMissingCatalogReference()
{
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var plan = new DispatchPlanDefinition(
        [],
        [new ManualTimetableRow(TimeSpan.FromHours(1), TrainDirection.Outbound, "MISSING_SERVICE", "EMU-6", "ALL_STOP")],
        DispatchPlanningMode.ManualTimetable);

    Throws<SimulationValidationException>(
        () => DispatchPlanExpander.Expand(plan, vehicles, services, stops),
        "找不到服務類型");
}

static void TestDispatchWorldFromBothEnds()
{
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var plan = new DispatchPlanDefinition(
        [],
        [
            new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-DOWN", serviceRunId: "RUN-DOWN"),
            new ManualTimetableRow(TimeSpan.FromSeconds(30), TrainDirection.Inbound, "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-UP", serviceRunId: "RUN-UP")
        ],
        DispatchPlanningMode.ManualTimetable);
    var expanded = DispatchPlanExpander.Expand(plan, vehicles, services, stops);
    var world = new SimulationWorld(
        CreateThreeStationRoute(),
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        trainCount: 2,
        movingBlockMode: MovingBlockMode.Independent,
        dispatchPlan: expanded,
        vehicleTypes: vehicles,
        infrastructure: InfrastructureGraph.CreateLegacy(CreateThreeStationRoute()));

    world.AdvanceTo(30);
    var snapshot = world.GetSnapshot();
    Equal(TrainDirection.Outbound, snapshot.Trains.Single(train => train.VehicleId == "EMU-DOWN").Direction);
    Equal(TrainDirection.Inbound, snapshot.Trains.Single(train => train.VehicleId == "EMU-UP").Direction);
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure && item.ServiceRunId == "RUN-DOWN"), "下行發車事件應保留結構化車次 ID。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure && item.ServiceRunId == "RUN-UP"), "上行發車事件應保留結構化車次 ID。");
    True(world.Trajectory.Where(item => item.VehicleId is "EMU-DOWN" or "EMU-UP")
        .All(item => item.VehicleTypeId == "EMU-6"), "軌跡應保留車型 ID。");
}

static void TestDispatchDuplicateVehicleRejected()
{
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var plan = new DispatchPlanDefinition(
        [],
        [
            new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-SAME", serviceRunId: "RUN-1"),
            new ManualTimetableRow(TimeSpan.FromSeconds(30), TrainDirection.Inbound, "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-SAME", serviceRunId: "RUN-2")
        ],
        DispatchPlanningMode.ManualTimetable);
    var expanded = DispatchPlanExpander.Expand(plan, vehicles, services, stops);

    Throws<SimulationValidationException>(
        () => _ = new SimulationWorld(
            CreateThreeStationRoute(),
            CreateParameters(),
            OperationalParameters.CreateDefault(),
            2,
            movingBlockMode: MovingBlockMode.Independent,
            dispatchPlan: expanded,
            vehicleTypes: vehicles),
        "未串接");
}

static void TestDispatchContinuationPersistence()
{
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var definition = new DispatchPlanDefinition(
        [new HeadwayDirectionPlan(TrainDirection.Outbound, TimeSpan.Zero, TimeSpan.FromMinutes(5), 1,
            "LOCAL", "EMU-6", "ALL_STOP", continueAfterTerminal: true)],
        [],
        DispatchPlanningMode.SimpleHeadway);
    var expanded = DispatchPlanExpander.Expand(definition, vehicles, services, stops);
    True(expanded.Runs.Single().ContinueAfterTerminal, "展開後車次應保留端點折返續行設定。");

    var document = CreateProjectDocument() with
    {
        VehicleTypes = [new ProjectVehicleType("EMU-6", "六節電聯車", 80, 22.222, 1, 0.9, 1.3, 0.65, 0.45, 0.05)],
        ServiceTypes = [new ProjectServiceType("LOCAL", "普通車", "#4472C4", "LOCAL", "ALL_STOP", "EMU-6")],
        StopPatterns = [new ProjectStopPattern("ALL_STOP", "普通車（全停站）", [
            new ProjectStopPatternInstruction("P01", StopPatternAction.Stop),
            new ProjectStopPatternInstruction("P02", StopPatternAction.Stop),
            new ProjectStopPatternInstruction("P03", StopPatternAction.Stop)])],
        Dispatch = new ProjectDispatchPlan(
            DispatchPlanningMode.SimpleHeadway,
            VehicleAssignmentMode.Automatic,
            [new ProjectHeadwayPlan(TrainDirection.Outbound, 0, 300, 1, "LOCAL", "EMU-6", "ALL_STOP",
                ContinueAfterTerminal: true)],
            [])
    };
    var restored = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    True(restored.Dispatch!.SimpleHeadwayPlans!.Single().ContinueAfterTerminal,
        "專案存檔往返後應保留端點折返續行設定。");

    var legacyJson = JsonNode.Parse(SimulationProjectFormat.Serialize(document))!.AsObject();
    legacyJson["dispatch"]!["simpleHeadwayPlans"]![0]!.AsObject().Remove("continueAfterTerminal");
    var withoutFlag = SimulationProjectFormat.Deserialize(legacyJson.ToJsonString());
    True(!withoutFlag.Dispatch!.SimpleHeadwayPlans!.Single().ContinueAfterTerminal,
        "既有 V3 存檔缺少欄位時應維持端點退出的相容預設。");
}

static void TestDispatchTerminalExitAfterDwell()
{
    var world = CreateDispatchTerminalWorld(continueAfterTerminal: false, terminalDwellSeconds: 35);
    while (!world.Events.Any(item => item.EventType == SimulationEventType.Arrival
        && item.ServiceRunId == "RUN-EXIT"
        && Math.Abs(item.PositionMeters - 2000) <= 0.5) && world.CurrentTimeSeconds < 600)
    {
        world.Tick();
    }

    var arrival = world.Events.Single(item => item.EventType == SimulationEventType.Arrival
        && item.ServiceRunId == "RUN-EXIT"
        && Math.Abs(item.PositionMeters - 2000) <= 0.5);
    var clearing = world.GetSnapshot().Trains.Single();
    True(clearing.IsActive && clearing.Phase == OperationalPhase.Dwelling,
        "端點抵達後應先保持可見並執行停站／清車。");

    world.AdvanceTo(arrival.SimulationTimeSeconds + 34.9);
    True(world.GetSnapshot().Trains.Single().IsActive, "35 秒清車完成前列車不應提早消失。");
    world.AdvanceTo(arrival.SimulationTimeSeconds + 35.2);
    var exited = world.GetSnapshot().Trains.Single();
    True(!exited.IsActive && exited.Phase == OperationalPhase.OutOfService,
        "清車完成後退出營運列車應從路線上消失。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.ServiceEnded
        && item.ServiceRunId == "RUN-EXIT"), "應留下結構化退出營運事件。");
}

static void TestDispatchTerminalContinuation()
{
    var world = CreateDispatchTerminalWorld(continueAfterTerminal: true, terminalDwellSeconds: 1);
    while (!world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged)
        && world.CurrentTimeSeconds < 600)
    {
        world.Tick();
    }

    var state = world.GetSnapshot().Trains.Single();
    True(state.IsActive, "折返續行列車不應退出營運。");
    Equal("EMU-CONTINUE", state.VehicleId);
    Equal(TrainDirection.Inbound, state.Direction);
    Equal("RUN-CONT-R001", state.ServiceRunId);
    Equal("LOCAL", state.ServiceClassId);
    Equal("ALL_STOP", state.ServicePatternId);
    Equal("EMU-6", state.VehicleTypeId);
    True(world.Events.Any(item => item.EventType == SimulationEventType.TurnaroundStarted),
        "完成端點作業後應記錄折返開始事件。");
}

static void TestDispatchSpecificContinuationChain()
{
    var route = CreateThreeStationRoute();
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition(
            [],
            [
                new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-6", "ALL_STOP",
                    vehicleId: "EMU-CIRCULATION-01", serviceRunId: "RUN-DOWN-001",
                    continueAfterTerminal: true, continuationServiceRunId: "RUN-UP-006"),
                new ManualTimetableRow(TimeSpan.FromMinutes(5), TrainDirection.Inbound, "LOCAL", "EMU-6", "ALL_STOP",
                    serviceRunId: "RUN-UP-006")
            ],
            DispatchPlanningMode.ManualTimetable),
        vehicles,
        services,
        stops);
    Equal("EMU-CIRCULATION-01", dispatch.Runs.Single(run => run.ServiceRunId == "RUN-UP-006").VehicleId!);

    var world = new SimulationWorld(
        route,
        new TrainParameters(22.222, 1, 1, 30, 2, 2),
        OperationalParameters.CreateDefault(),
        2,
        movingBlockMode: MovingBlockMode.Independent,
        dispatchPlan: dispatch,
        vehicleTypes: vehicles,
        infrastructure: InfrastructureGraph.CreateLegacy(route));
    Equal(1, world.GetSnapshot().Trains.Count);

    world.AdvanceTo(299.9);
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "RUN-UP-006"), "指定上行車次不應在計畫時間前發車。");
    world.AdvanceTo(300.2);
    var state = world.GetSnapshot().Trains.Single();
    Equal("EMU-CIRCULATION-01", state.VehicleId);
    Equal("RUN-UP-006", state.ServiceRunId);
    Equal(TrainDirection.Inbound, state.Direction);
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "RUN-UP-006" && item.VehicleId == "EMU-CIRCULATION-01"),
        "下行第一車應由同一實體車輛接續為指定上行第六車。");
}

static SimulationWorld CreateDispatchTerminalWorld(bool continueAfterTerminal, double terminalDwellSeconds)
{
    var route = RouteFactory.FromSegmentDistances(
        "O",
        "端點作業測試線",
        [
            new StationInput("O01", "起點站", 0, 0),
            new StationInput("O02", "中央站", 1000, 0),
            new StationInput("O03", "終點站", 1000, terminalDwellSeconds)
        ],
        0);
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var runId = continueAfterTerminal ? "RUN-CONT" : "RUN-EXIT";
    var vehicleId = continueAfterTerminal ? "EMU-CONTINUE" : "EMU-EXIT";
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition(
            [],
            [new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-6", "ALL_STOP",
                vehicleId: vehicleId, serviceRunId: runId, continueAfterTerminal: continueAfterTerminal)],
            DispatchPlanningMode.ManualTimetable),
        vehicles,
        services,
        stops);
    var parameters = new TrainParameters(22.222, 1, 1, 0, 2, 2);
    return new SimulationWorld(
        route,
        parameters,
        OperationalParameters.CreateDefault(),
        1,
        movingBlockMode: MovingBlockMode.Independent,
        dispatchPlan: dispatch,
        vehicleTypes: vehicles,
        infrastructure: InfrastructureGraph.CreateLegacy(route));
}

static void TestInfrastructureResourceLockEvents()
{
    var route = CreateThreeStationRoute();
    var legacy = InfrastructureGraph.CreateLegacy(route);
    True(legacy.TrackSegments.Any(item => item.TrackId == "DOWN"), "舊版路線應建立 DOWN 股道。");
    True(legacy.TrackSegments.Any(item => item.TrackId == "UP"), "舊版路線應建立 UP 股道。");

    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition([], [new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-CUSTOM", serviceRunId: "RUN-CUSTOM")], DispatchPlanningMode.ManualTimetable),
        vehicles, services, stops);
    var custom = CreateCustomInfrastructure(route);
    var world = new SimulationWorld(route, CreateParameters(), OperationalParameters.CreateDefault(), 1,
        movingBlockMode: MovingBlockMode.Independent, dispatchPlan: dispatch, vehicleTypes: vehicles, infrastructure: custom);
    world.AdvanceTo(30);

    True(world.Events.Any(item => item.EventType == SimulationEventType.PlatformAssigned), "應記錄月台配置事件。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReserved && item.ResourceId == "PATH:CUSTOM:1"), "應記錄自訂進路資源鎖定事件。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReleased), "列車離開保護區後應記錄進路釋放事件。");

    var manager = new RouteResourceReservationManager();
    True(manager.Reserve("RUN-A", ["CUSTOM:SHARED"]), "第一個車次應能鎖定自訂衝突資源。");
    True(!manager.Reserve("RUN-B", ["CUSTOM:SHARED"]), "同一自訂衝突資源不可被第二車次同時鎖定。");
    True(manager.Release("RUN-A"), "應能釋放自訂衝突資源。");
    True(manager.Reserve("RUN-B", ["CUSTOM:SHARED"]), "釋放後第二車次應能取得自訂衝突資源。");
}

static void TestIntervalStatistics()
{
    var route = RouteFactory.FromSegmentDistances(
        "I", "統計線", [new StationInput("I01", "甲站", 0, 0), new StationInput("I02", "乙站", 100, 0)], 0);
    var samples = new List<TrajectorySample>();
    var events = new List<SimulationEvent>();
    foreach (var (vehicleId, serviceRunId, start, duration) in new[]
    {
        ("V-1", "RUN-1", 0d, 10d),
        ("V-2", "RUN-2", 100d, 20d),
        ("V-3", "RUN-3", 200d, 30d),
        ("V-4", "RUN-4", 300d, 5d)
    })
    {
        samples.Add(new TrajectorySample(start, vehicleId, serviceRunId, "普通車", "ALL_STOP", TrainDirection.Outbound, "DOWN", 0, 10, 0,
            OperationalPhase.Accelerating, "I01", "I02", true, "EMU-6", Constraints:
            serviceRunId == "RUN-2" ? OperationalConstraint.MovingBlock : OperationalConstraint.None));
        var endPosition = serviceRunId == "RUN-4" ? 50 : 100;
        var endStation = serviceRunId == "RUN-4" ? "I01" : "I02";
        samples.Add(new TrajectorySample(start + duration, vehicleId, serviceRunId, "普通車", "ALL_STOP", TrainDirection.Outbound, "DOWN", endPosition, 0, 0,
            serviceRunId == "RUN-4" ? OperationalPhase.Cruising : OperationalPhase.Arriving, endStation,
            serviceRunId == "RUN-4" ? "I02" : null, true, "EMU-6"));
        events.Add(new SimulationEvent(start, SimulationEventType.Departure, vehicleId, null, TrainDirection.Outbound, "DOWN", 0, 0, "發車", serviceRunId, "普通車", "ALL_STOP", "EMU-6"));
        if (serviceRunId != "RUN-4")
        {
            events.Add(new SimulationEvent(start + duration, SimulationEventType.Arrival, vehicleId, null, TrainDirection.Outbound, "DOWN", 100, 0, "抵達", serviceRunId, "普通車", "ALL_STOP", "EMU-6"));
        }
    }

    var result = IntervalStatistics.Analyze(route, samples, events);
    Equal(3, result.CompletedCount);
    Equal(1, result.InProgressCount);
    Equal(1, result.Summaries.Count);
    NearlyEqual(30, result.Summaries[0].P95TravelTimeSeconds!.Value);
    var csv = IntervalStatistics.BuildCsv(result);
    var summaryCsv = IntervalStatistics.BuildSummaryCsv(result);
    True(csv.Contains("完成", StringComparison.Ordinal) && csv.Contains("運行中", StringComparison.Ordinal), "中文 CSV 應區分完成與運行中。");
    True(csv.Contains("移動閉塞受限(s)", StringComparison.Ordinal), "區間 CSV 應輸出移動閉塞受限秒數。");
    True(summaryCsv.Contains("第95百分位旅行時間(s)", StringComparison.Ordinal), "摘要 CSV 應包含 P95 欄位。");

    var completedOnly = IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(IncludeInProgress: false));
    Equal(3, completedOnly.CompletedCount);
    Equal(0, completedOnly.InProgressCount);
    var selected = IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(VehicleId: "V-2", ServiceRunId: "RUN-2", VehicleTypeId: "EMU-6",
            ServiceClassId: "普通車", ServicePatternId: "ALL_STOP", StartSimulationTimeSeconds: 90,
            EndSimulationTimeSeconds: 125, IncludeInProgress: false));
    Equal(1, selected.CompletedCount);
    NearlyEqual(20, selected.CompletedIntervals.Single().ControlLimitedSeconds!.Value);
    Equal(0, IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(Direction: TrainDirection.Inbound)).AllIntervals.Count);
    Equal(0, IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(StartSimulationTimeSeconds: 400)).AllIntervals.Count);
}

static void TestCanonicalSchemaOmitsLegacySources()
{
    var json = SimulationProjectFormat.Serialize(CreateProjectDocument());
    var root = JsonNode.Parse(json)!.AsObject();
    Equal(7, root["schemaVersion"]!.GetValue<int>());
    True(!root.ContainsKey("servicePatterns") && !root.ContainsKey("serviceRuns"),
        "Schema 7 不得再寫出舊 ServicePatterns／ServiceRuns 雙資料源。");
    True(root.ContainsKey("vehicleTypes") && root.ContainsKey("serviceTypes")
        && root.ContainsKey("stopPatterns") && root.ContainsKey("dispatch"),
        "Schema 7 必須保存唯一的新版目錄與發車計畫。");
}

static void TestEngineKindProfileModeSeparation()
{
    var document = CreateProjectDocument() with
    {
        Simulation = CreateProjectDocument().Simulation with
        {
            EngineKind = SimulationEngineKind.V1BasicPhysics,
            ProfileMode = OperationProfileMode.RealisticOperations
        }
    };
    var restored = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    Equal(SimulationEngineKind.V1BasicPhysics, restored.Simulation.EngineKind);
    Equal(OperationProfileMode.RealisticOperations, restored.Simulation.ProfileMode);
}

static void TestSpatialReferencePointValidation()
{
    var points = new[]
    {
        new SpatialReferencePointDefinition("REF-S", "O02", "中間站", SpatialReferencePointKind.IntermediateStation,
            stationForwardGradeInPermille: 2, stationReverseGradeOutPermille: -2),
        new SpatialReferencePointDefinition("REF-J", "O02", "支線銜接", SpatialReferencePointKind.Junction,
            mainlineGradePermille: 2, branchlineGradePermille: -3, crossoverLengthMeters: 120,
            switchSpeedLimitMetersPerSecond: 35 / 3.6, mainlineApproachCruiseSpeedMetersPerSecond: 75 / 3.6,
            branchlineApproachCruiseSpeedMetersPerSecond: 60 / 3.6, mainlineSafetyFactor: 1.6,
            branchlineSafetyFactor: 1.7, mainlineTrafficRatio: 0.65),
        new SpatialReferencePointDefinition("REF-F", "O03", "站前折返", SpatialReferencePointKind.BeforeStationTurnback,
            alternateBerthing: true, mainlineGradePermille: 1, turnbackDwellSeconds: 35,
            switchSpeedLimitMetersPerSecond: 40 / 3.6, mainlineApproachCruiseSpeedMetersPerSecond: 60 / 3.6,
            mainlineSafetyFactor: 1.5),
        new SpatialReferencePointDefinition("REF-R", "O03", "站後折返", SpatialReferencePointKind.AfterStationTurnback,
            distanceFromCrossoverToTurnbackStopMeters: 180, turnbackDwellSeconds: 45),
        new SpatialReferencePointDefinition("REF-P", "O03", "中央避車線", SpatialReferencePointKind.CentralSidingTurnback,
            distanceFromCrossoverToTurnbackStopMeters: 150, turnbackDwellSeconds: 30)
    };
    Equal(5, points.Length);
    True(points.Skip(2).All(point => point.IsTurnback), "三種折返型式都必須被辨識為折返設定。");
    True(!points[0].IsTurnback && !points[1].IsTurnback, "中間站與銜接點不可誤判為折返設定。");
    NearlyEqual(45, points[3].AdditionalTurnbackSeconds);
    NearlyEqual(0, points[2].AdditionalTurnbackSeconds);

    Throws<SimulationValidationException>(() => new SpatialReferencePointDefinition(
        "REF-BAD", "O03", "錯誤限速", SpatialReferencePointKind.BeforeStationTurnback,
        switchSpeedLimitMetersPerSecond: 0), "道岔限速");
    Throws<SimulationValidationException>(() => new SpatialReferencePointDefinition(
        "REF-BAD-2", "O03", "錯誤避車線", SpatialReferencePointKind.CentralSidingTurnback,
        distanceFromCrossoverToTurnbackStopMeters: 501), "中央避車線停車區距離");
}

static void TestSpatialReferencePointPersistence()
{
    var source = CreateProjectDocument();
    var route = RouteFactory.FromSegmentDistances(source.RouteId, source.RouteName,
        source.Stations.Select(station => new StationInput(station.StationId, station.StationName,
            station.DistanceFromPreviousMeters, station.DwellTimeSeconds)), source.Train.DefaultDwellTimeSeconds);
    var legacy = InfrastructureGraph.CreateLegacy(route);
    var point = new SpatialReferencePointDefinition(
        "REF-P02-STATION", "P02", "中央中間站", SpatialReferencePointKind.IntermediateStation,
        stationForwardGradeInPermille: 1.5,
        stationForwardDwellSeconds: 42,
        stationReverseGradeOutPermille: -2,
        stationReverseEarlierCruiseSpeedMetersPerSecond: 65 / 3.6);
    var graph = new InfrastructureGraph(route, legacy.StationYards, legacy.TrackSegments, legacy.Paths,
        legacy.TurnbackPlans, [point]);
    var document = source with { Infrastructure = ProjectInfrastructureFromGraph(graph) };

    var json = SimulationProjectFormat.Serialize(document);
    var restored = SimulationProjectFormat.Deserialize(json);
    Equal(7, restored.SchemaVersion);
    var stored = restored.Infrastructure!.SpatialReferencePoints!.Single();
    Equal("REF-P02-STATION", stored.ReferencePointId);
    Equal(SpatialReferencePointKind.IntermediateStation, stored.Kind);
    NearlyEqual(1.5, stored.StationForwardGradeInPermille);
    NearlyEqual(42, stored.StationForwardDwellSeconds);
    NearlyEqual(-2, stored.StationReverseGradeOutPermille);
    NearlyEqual(65, stored.StationReverseEarlierCruiseSpeedMetersPerSecond * 3.6);
}

static void TestSpatialReferencePointTurnbackTiming()
{
    var route = CreateThreeStationRoute();
    var legacy = InfrastructureGraph.CreateLegacy(route);
    var point = new SpatialReferencePointDefinition(
        "REF-O03-REAR", "O03", "終點尾軌", SpatialReferencePointKind.AfterStationTurnback,
        turnbackDwellSeconds: 7);
    var infrastructure = new InfrastructureGraph(route, legacy.StationYards, legacy.TrackSegments, legacy.Paths,
        legacy.TurnbackPlans, [point]);
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition([], [new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound,
            "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-REF", serviceRunId: "RUN-REF",
            continueAfterTerminal: true)], DispatchPlanningMode.ManualTimetable),
        vehicles, services, stops);
    var world = new SimulationWorld(route, new TrainParameters(22.222, 1, 1, 0, 180, 360),
        OperationalParameters.CreateDefault(), 1, movingBlockMode: MovingBlockMode.Independent,
        dispatchPlan: dispatch, vehicleTypes: vehicles, infrastructure: infrastructure);

    while (!world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged)
        && world.CurrentTimeSeconds < 600)
    {
        world.Tick();
    }

    var started = world.Events.Single(item => item.EventType == SimulationEventType.TurnaroundStarted);
    var changed = world.Events.Single(item => item.EventType == SimulationEventType.DirectionChanged);
    var vehicle = vehicles.Single(item => item.Id == "EMU-6");
    var oneWay = AnalyticalModel.CalculateSegmentTravelTime(
        300,
        40 / 3.6,
        vehicle.AccelerationMetersPerSecondSquared,
        vehicle.ServiceBrakeDecelerationMetersPerSecondSquared).TravelTimeSeconds;
    NearlyEqual(7 + oneWay * 2, changed.SimulationTimeSeconds - started.SimulationTimeSeconds, 0.11);
    Equal(TrainDirection.Inbound, world.GetSnapshot().Trains.Single().Direction);
}

static void TestSpatialCapacityDefaultVectors()
{
    var station = new SpatialReferencePointDefinition(
        "REF-S", "O02", "中間站", SpatialReferencePointKind.IntermediateStation);
    var front = new SpatialReferencePointDefinition(
        "REF-F", "O03", "站前折返", SpatialReferencePointKind.BeforeStationTurnback,
        mainlineApproachCruiseSpeedMetersPerSecond: 60 / 3.6);
    var rear = new SpatialReferencePointDefinition(
        "REF-R", "O03", "站後折返", SpatialReferencePointKind.AfterStationTurnback);
    var pocket = new SpatialReferencePointDefinition(
        "REF-P", "O03", "中央避車線", SpatialReferencePointKind.CentralSidingTurnback);
    var junction = new SpatialReferencePointDefinition(
        "REF-J", "O02", "銜接點", SpatialReferencePointKind.Junction);

    var stationResult = SpatialCapacityAnalysis.Calculate(station);
    var frontResult = SpatialCapacityAnalysis.Calculate(front);
    var rearResult = SpatialCapacityAnalysis.Calculate(rear);
    var pocketResult = SpatialCapacityAnalysis.Calculate(pocket);
    var junctionResult = SpatialCapacityAnalysis.Calculate(junction);

    NearlyEqual(95.964444444, stationResult.SafetyHeadwaySeconds, 1e-6);
    NearlyEqual(121.705555556, frontResult.SafetyHeadwaySeconds, 1e-6);
    NearlyEqual(118.658975487, rearResult.SafetyHeadwaySeconds, 1e-6);
    NearlyEqual(127.460086598, pocketResult.SafetyHeadwaySeconds, 1e-6);
    NearlyEqual(66.412222222, junctionResult.SafetyHeadwaySeconds, 1e-6);
    Equal(28, stationResult.LineCapacityPerHour);
    Equal(54208, stationResult.DesignPassengerCapacityPerHour);
    Equal(43366, stationResult.AchievablePassengerCapacityPerHour);
}

static void TestSpatialCapacityStationDirections()
{
    var station = new SpatialReferencePointDefinition(
        "REF-S", "O02", "順逆行中間站", SpatialReferencePointKind.IntermediateStation,
        stationReverseGradeInPermille: 10,
        stationReverseGradeOutPermille: -10,
        stationReverseDistanceToSignalMeters: 30,
        stationReverseOverlapMeters: 200,
        stationReverseDwellSeconds: 45,
        stationReverseEarlierCruiseSpeedMetersPerSecond: 60 / 3.6,
        stationReverseLaterCruiseSpeedMetersPerSecond: 70 / 3.6,
        stationReverseSafetyFactor: 2);

    var forward = SpatialCapacityAnalysis.Calculate(station, direction: SpatialCapacityDirection.Forward);
    var reverse = SpatialCapacityAnalysis.Calculate(station, direction: SpatialCapacityDirection.Reverse);
    NearlyEqual(95.964444444, forward.SafetyHeadwaySeconds, 1e-6);
    NearlyEqual(116.591298331, reverse.SafetyHeadwaySeconds, 1e-6);
    True(Math.Abs(forward.SafetyHeadwaySeconds - reverse.SafetyHeadwaySeconds) > 1,
        "中間站順逆行不得共用同一組計算結果。");
}

static void TestSpatialCapacityTruncationAndValidation()
{
    var front = new SpatialReferencePointDefinition(
        "REF-F", "O03", "站前折返", SpatialReferencePointKind.BeforeStationTurnback,
        mainlineApproachCruiseSpeedMetersPerSecond: 60 / 3.6);
    var result = SpatialCapacityAnalysis.Calculate(front);
    Equal(22, result.LineCapacityPerHour);
    Equal(42592, result.DesignPassengerCapacityPerHour);
    Equal(34073, result.AchievablePassengerCapacityPerHour);

    var invalidTrain = new SpatialCapacityTrainParameters(
        AccelerationMetersPerSecondSquared: 0.1,
        DecelerationMetersPerSecondSquared: 1);
    var invalidStation = new SpatialReferencePointDefinition(
        "REF-BAD-A", "O02", "無效有效加速度", SpatialReferencePointKind.IntermediateStation,
        stationForwardGradeInPermille: 30);
    Throws<SimulationValidationException>(
        () => SpatialCapacityAnalysis.Calculate(invalidStation, train: invalidTrain),
        "有效加速度");
}

static void TestSpatialStationDwellIntegration()
{
    var route = CreateThreeStationRoute();
    var legacy = InfrastructureGraph.CreateLegacy(route);
    var station = new SpatialReferencePointDefinition(
        "REF-O02-STATION", "O02", "方向別中間站", SpatialReferencePointKind.IntermediateStation,
        stationForwardDwellSeconds: 7,
        stationReverseDwellSeconds: 11);
    var infrastructure = new InfrastructureGraph(route, legacy.StationYards, legacy.TrackSegments, legacy.Paths,
        legacy.TurnbackPlans, [station]);
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition([], [new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound,
            "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-STATION", serviceRunId: "RUN-STATION")],
            DispatchPlanningMode.ManualTimetable),
        vehicles, services, stops);
    var world = new SimulationWorld(route, CreateParameters(), OperationalParameters.CreateDefault(), 1,
        movingBlockMode: MovingBlockMode.Independent, dispatchPlan: dispatch, vehicleTypes: vehicles,
        infrastructure: infrastructure);

    while (!world.Events.Any(item => item.EventType == SimulationEventType.Departure
               && item.ServiceRunId == "RUN-STATION"
               && Math.Abs(item.PositionMeters - 1000) <= 0.5)
           && world.CurrentTimeSeconds < 400)
    {
        world.Tick();
    }

    var arrival = world.Events.Single(item => item.EventType == SimulationEventType.Arrival
        && item.ServiceRunId == "RUN-STATION" && Math.Abs(item.PositionMeters - 1000) <= 0.5);
    var departure = world.Events.Single(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "RUN-STATION" && Math.Abs(item.PositionMeters - 1000) <= 0.5);
    NearlyEqual(7, departure.SimulationTimeSeconds - arrival.SimulationTimeSeconds, 0.11);
}

static void TestV3TimetableAndIntervalTerminalBoundaries()
{
    var route = CreateThreeStationRoute();
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition([], [new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound,
            "LOCAL", "EMU-6", "ALL_STOP", vehicleId: "EMU-EXIT", serviceRunId: "RUN-EXIT",
            continueAfterTerminal: false)], DispatchPlanningMode.ManualTimetable),
        vehicles, services, stops);
    var world = new SimulationWorld(route, CreateParameters(), OperationalParameters.CreateDefault(), 1,
        movingBlockMode: MovingBlockMode.Independent, dispatchPlan: dispatch, vehicleTypes: vehicles);

    while (!world.Events.Any(item => item.EventType == SimulationEventType.ServiceEnded
               && item.ServiceRunId == "RUN-EXIT")
           && world.CurrentTimeSeconds < 600)
    {
        world.Tick();
    }

    var originDeparture = world.Events.Single(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "RUN-EXIT" && Math.Abs(item.PositionMeters) <= 0.5);
    NearlyEqual(0, originDeparture.SimulationTimeSeconds, 1e-7);
    var statistics = IntervalStatistics.Analyze(route, world.Trajectory, world.Events);
    Equal(2, statistics.CompletedCount);
    Equal(0, statistics.InProgressCount);

    var timetable = OperationsTimetable.Build(route, dispatch, [], world.Events);
    var terminal = timetable.Single(item => item.ServiceRunId == "RUN-EXIT" && item.StationId == "O03");
    True(terminal.ActualArrivalTimeSeconds is not null, "端點退出車次必須保留實際抵達時刻。");
    Equal("退出營運", terminal.Status);
    True(world.GetSnapshot().Trains.All(item => !item.IsActive), "清車退出後車輛不得繼續留在營運路線上。");
}

static void TestComprehensiveSampleProject()
{
    var repositoryRoot = FindRepositoryRoot();
    var samplePath = Path.Combine(repositoryRoot, "samples", "V3.3.0-完整功能驗證範例.mrtsim.json");
    True(File.Exists(samplePath), $"找不到完整功能範例存檔：{samplePath}");

    var document = SimulationProjectFormat.Deserialize(File.ReadAllText(samplePath));
    Equal(SimulationProjectFormat.CurrentSchemaVersion, document.SchemaVersion);
    Equal(5, document.Stations.Length);
    Equal(3, document.SpeedLimits.Length);
    Equal(2, document.VehicleTypes!.Length);
    Equal(2, document.ServiceTypes!.Length);
    Equal(2, document.StopPatterns!.Length);
    Equal(5, document.Infrastructure!.SpatialReferencePoints!.Length);
    Equal(5, document.Infrastructure.SpatialReferencePoints.Select(item => item.Kind).Distinct().Count());
    Equal(DispatchPlanningMode.ManualTimetable, document.Dispatch!.ActiveMode);
    Equal(6, document.Dispatch.ManualTimetableRows!.Length);
    var expressPattern = document.StopPatterns.Single(item => item.Id == "EXPRESS");
    True(expressPattern.Instructions.Any(item => item.Action == StopPatternAction.Pass), "範例必須包含跨站停站模式。");
    NearlyEqual(25, expressPattern.Instructions.Single(item => item.StationId == "V03").DwellTimeSeconds!.Value);
    NearlyEqual(50, expressPattern.Instructions.Single(item => item.StationId == "V02")
        .PassingSpeedLimitMetersPerSecond!.Value * 3.6);
    var roundTrip = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    True(roundTrip.ServicePatterns is null && roundTrip.ServiceRuns is null, "範例往返後不得產生舊執行資料源。");
    NearlyEqual(25, roundTrip.StopPatterns!.Single(item => item.Id == "EXPRESS").Instructions
        .Single(item => item.StationId == "V03").DwellTimeSeconds!.Value);

    var route = RouteFactory.FromSegmentDistances(
        document.RouteId,
        document.RouteName,
        document.Stations.Select(item => new StationInput(
            item.StationId,
            item.StationName,
            item.DistanceFromPreviousMeters,
            item.DwellTimeSeconds)),
        document.Train.DefaultDwellTimeSeconds);
    var vehicleTypes = document.VehicleTypes.Select(ToVehicleTypeDefinition).ToArray();
    var serviceTypes = document.ServiceTypes.Select(item => new ServiceTypeDefinition(
        item.Id,
        item.DisplayName,
        item.ColorHex,
        item.RunPrefix,
        item.DefaultStopPatternId,
        item.DefaultVehicleTypeId,
        item.Priority,
        item.CanRequestOvertake,
        item.PreferredPlatformIds)).ToArray();
    var stopPatterns = document.StopPatterns.Select(item => new StopPatternDefinition(
        item.Id,
        item.DisplayName,
        item.Instructions.Select(instruction => new StopPatternInstruction(
            instruction.StationId,
            instruction.Action,
            instruction.DwellTimeSeconds,
            instruction.PassingSpeedLimitMetersPerSecond)))).ToArray();
    var dispatch = DispatchPlanExpander.Expand(
        ToDispatchPlanDefinition(document.Dispatch),
        vehicleTypes,
        serviceTypes,
        stopPatterns);
    Equal("EMU-CIRC-01", dispatch.Runs.Single(item => item.ServiceRunId == "RUN-UP-006").VehicleId!);

    var infrastructure = ToInfrastructureGraph(document.Infrastructure, route);
    var servicePatterns = document.StopPatterns.Select(item => new ServicePattern(
        item.Id,
        item.DisplayName,
        item.Instructions.Select(instruction => new StationServiceInstruction(
            instruction.StationId,
            instruction.Action == StopPatternAction.Stop ? StationServiceMode.Stop : StationServiceMode.Pass,
            instruction.PassingSpeedLimitMetersPerSecond,
            instruction.DwellTimeSeconds)).ToArray())).ToArray();
    var world = new SimulationWorld(
        route,
        new TrainParameters(
            document.Train.MaxSpeedMetersPerSecond,
            document.Train.AccelerationMetersPerSecondSquared,
            document.Train.DecelerationMetersPerSecondSquared,
            document.Train.DefaultDwellTimeSeconds,
            document.Train.OriginTurnaroundTimeSeconds,
            document.Train.TerminalTurnaroundTimeSeconds),
        new OperationalParameters(
            document.Operations.JerkMetersPerSecondCubed,
            document.Operations.CoastingRatio,
            document.Operations.ApproachDistanceMeters,
            document.Operations.ApproachSpeedMetersPerSecond,
            document.Operations.TractionFadeRatio,
            document.Operations.TrainLengthMeters,
            document.Operations.ServiceBrakingMetersPerSecondSquared,
            document.Operations.EmergencyBrakingMetersPerSecondSquared,
            document.Operations.ControlReactionTimeSeconds,
            document.Operations.BrakeBuildUpTimeSeconds,
            document.Operations.PositioningErrorMeters,
            document.Operations.SafetyMarginMeters,
            document.Operations.AbsoluteMinimumGapMeters),
        document.Simulation.TrainCount,
        document.Simulation.HeadwaySeconds,
        document.SpeedLimits.Select(item => new SpeedLimitSegment(
            item.StartPositionMeters,
            item.EndPositionMeters,
            item.LimitMetersPerSecond,
            item.Direction,
            item.Note)),
        document.Simulation.ProfileMode,
        document.Simulation.MovingBlockMode,
        servicePatterns,
        [],
        dispatch,
        vehicleTypes,
        infrastructure);
    world.SetBrakingEstimationMode(document.Simulation.BrakingEstimationMode);
    world.AdvanceTo(2400);

    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure
        && item.Direction == TrainDirection.Outbound), "範例必須產生下行發車事件。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure
        && item.Direction == TrainDirection.Inbound), "範例必須產生上行發車事件。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.StationPassed),
        "快車模式必須產生跨站事件。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged
        && item.VehicleId == "EMU-CIRC-01"),
        $"指定接續車輛必須完成折返換向。實際事件：{string.Join("；", world.Events.Where(item => item.VehicleId == "EMU-CIRC-01").Select(item => $"{item.SimulationTimeSeconds:F1}/{item.EventType}/{item.ServiceRunId}"))}。列車：{string.Join("；", world.GetSnapshot().Trains.Select(item => $"{item.VehicleId}/{item.ServiceRunId}/{item.Direction}/{item.Phase}/{item.FrontPositionMeters:F1}/{item.IsActive}"))}");
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "RUN-UP-006"
        && item.VehicleId == "EMU-CIRC-01"), "下行第一車必須接續為指定上行第六車。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.ServiceEnded
        && item.VehicleId == "EMU-EXIT-U01"), "不折返列車必須在端點清車後退出營運。");
    True(world.GetSnapshot().Trains.Where(item => item.VehicleId == "EMU-EXIT-U01").All(item => !item.IsActive),
        "退出營運列車不得繼續顯示於路線上。");
    True(world.SafetyHistory.Count > 0, "多列車範例必須產生移動閉塞安全距離歷程。");

    var timetable = OperationsTimetable.Build(route, dispatch, [], world.Events);
    True(timetable.Any(item => item.Status == "退出營運"), "進出站時刻表必須呈現退出營運狀態。");
    True(timetable.Any(item => item.Status == "折返接續"), "進出站時刻表必須呈現折返接續狀態。");
    var statistics = IntervalStatistics.Analyze(
        route,
        world.Trajectory,
        world.Events,
        document.SpeedLimits.Select(item => new SpeedLimitSegment(
            item.StartPositionMeters,
            item.EndPositionMeters,
            item.LimitMetersPerSecond,
            item.Direction,
            item.Note)));
    True(statistics.CompletedCount > 0, "區間物理明細／V2 區間統計必須產生已完成區間。");
    True(world.Trajectory.Any(item => item.Direction == TrainDirection.Inbound),
        "軌跡必須包含上行資料，供上行速度曲線與運行圖驗證。");
    True(world.Trajectory.Any(item => item.Direction == TrainDirection.Outbound),
        "軌跡必須包含下行資料，供下行速度曲線與運行圖驗證。");
}

static VehicleTypeDefinition ToVehicleTypeDefinition(ProjectVehicleType item) => new(
    item.Id,
    item.DisplayName,
    item.LengthMeters,
    item.MaxSpeedMetersPerSecond,
    item.AccelerationMetersPerSecondSquared,
    item.ServiceBrakeDecelerationMetersPerSecondSquared,
    item.EmergencyBrakeDecelerationMetersPerSecondSquared,
    item.JerkMetersPerSecondCubed,
    item.TractionDecayPerSecond,
    item.CoastingDecelerationMetersPerSecondSquared);

static DispatchPlanDefinition ToDispatchPlanDefinition(ProjectDispatchPlan item) => new(
    item.SimpleHeadwayPlans?.Select(plan => new HeadwayDirectionPlan(
        plan.Direction,
        TimeSpan.FromSeconds(plan.FirstDepartureTimeSeconds),
        TimeSpan.FromSeconds(plan.HeadwaySeconds),
        plan.RunCount,
        plan.ServiceTypeId,
        plan.VehicleTypeId,
        plan.StopPatternId,
        plan.OriginPlatformId,
        plan.VehicleId,
        plan.ContinueAfterTerminal)),
    item.ManualTimetableRows?.Select(row => new ManualTimetableRow(
        TimeSpan.FromSeconds(row.PlannedDepartureTimeSeconds),
        row.Direction,
        row.ServiceTypeId,
        row.VehicleTypeId,
        row.StopPatternId,
        row.OriginPlatformId,
        row.VehicleId,
        row.ServiceRunId,
        row.ContinueAfterTerminal,
        row.ContinuationServiceRunId)),
    item.ActiveMode,
    item.VehicleAssignmentMode);

static InfrastructureGraph ToInfrastructureGraph(ProjectInfrastructure item, Route route) => new(
    route,
    item.StationYards.Select(yard => new StationYardDefinition(
        yard.StationId,
        yard.Name,
        (yard.Platforms ?? []).Select(platform => new PlatformDefinition(
            platform.PlatformId,
            platform.StationId,
            platform.Name,
            platform.Direction,
            platform.EffectiveLengthMeters,
            platform.StoppingPositionMeters,
            platform.AllowsPassengerService,
            platform.AllowedVehicleTypeIds,
            platform.AllowedServiceTypeIds,
            platform.TrackSegmentIds)),
        yard.TrackSegmentIds,
        yard.RoutePathIds,
        (yard.TurnbackPlans ?? []).Select(ToTurnbackPlanDefinition),
        yard.PlatformAllocationStrategy)),
    item.TrackSegments.Select(track => new TrackSegmentDefinition(
        track.TrackId,
        track.FromStationId,
        track.ToStationId,
        track.StartPositionMeters,
        track.EndPositionMeters,
        track.Direction,
        track.Kind,
        track.EffectiveLengthMeters,
        track.SpeedLimitMetersPerSecond,
        track.ConflictResourceIds)),
    item.Paths.Select(path => new RoutePathDefinition(
        path.PathId,
        path.FromPlatformId,
        path.ToPlatformId,
        path.Direction,
        path.TrackSegmentIds,
        path.ResourceIds)),
    item.TurnbackPlans?.Select(ToTurnbackPlanDefinition),
    (item.SpatialReferencePoints ?? []).Select(point => new SpatialReferencePointDefinition(
        point.ReferencePointId,
        point.StationId,
        point.Name,
        point.Kind,
        point.AlternateBerthing,
        point.MainlineGradePermille,
        point.BranchlineGradePermille,
        point.DistanceFromStopToCrossoverMeters,
        point.CrossoverLengthMeters,
        point.DistanceFromCrossoverToTurnbackStopMeters,
        point.TurnbackDwellSeconds,
        point.SwitchSpeedLimitMetersPerSecond,
        point.MainlineApproachCruiseSpeedMetersPerSecond,
        point.BranchlineApproachCruiseSpeedMetersPerSecond,
        point.MainlineSafetyFactor,
        point.BranchlineSafetyFactor,
        point.MainlineTrafficRatio,
        point.StationForwardGradeInPermille,
        point.StationForwardGradeOutPermille,
        point.StationForwardDistanceToSignalMeters,
        point.StationForwardOverlapMeters,
        point.StationForwardDwellSeconds,
        point.StationForwardEarlierCruiseSpeedMetersPerSecond,
        point.StationForwardLaterCruiseSpeedMetersPerSecond,
        point.StationForwardSafetyFactor,
        point.StationReverseGradeInPermille,
        point.StationReverseGradeOutPermille,
        point.StationReverseDistanceToSignalMeters,
        point.StationReverseOverlapMeters,
        point.StationReverseDwellSeconds,
        point.StationReverseEarlierCruiseSpeedMetersPerSecond,
        point.StationReverseLaterCruiseSpeedMetersPerSecond,
        point.StationReverseSafetyFactor)));

static TurnbackPlanDefinition ToTurnbackPlanDefinition(ProjectTurnbackPlan item) => new(
    item.TurnbackId,
    item.Name,
    item.StationId,
    item.Kind,
    item.ArrivalPlatformId,
    item.DeparturePlatformId,
    item.TrackSegmentIds,
    item.ResourceIds,
    item.TurnbackTimeSeconds);

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MrtRouteSimulator.slnx")))
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("無法由測試輸出目錄定位 MrtRouteSimulator.slnx。");
}

static ProjectInfrastructure ProjectInfrastructureFromGraph(InfrastructureGraph graph) => new(
    graph.StationYards.Select(yard => new ProjectStationYard(yard.StationId, yard.Name,
        yard.Platforms.Select(platform => new ProjectPlatform(platform.PlatformId, platform.StationId, platform.Name,
            platform.Direction, platform.EffectiveLengthMeters, platform.StoppingPositionMeters,
            platform.AllowsPassengerService, platform.AllowedVehicleTypeIds.ToArray(),
            platform.AllowedServiceTypeIds.ToArray(), platform.TrackSegmentIds.ToArray())).ToArray(),
        yard.TrackSegmentIds.ToArray(), yard.RoutePathIds.ToArray(),
        yard.TurnbackPlans.Select(plan => new ProjectTurnbackPlan(plan.TurnbackId, plan.Name, plan.StationId,
            plan.Kind, plan.ArrivalPlatformId, plan.DeparturePlatformId, plan.TrackSegmentIds.ToArray(),
            plan.ResourceIds.ToArray(), plan.TurnbackTimeSeconds)).ToArray(), yard.PlatformAllocationStrategy)).ToArray(),
    graph.TrackSegments.Select(track => new ProjectTrackSegment(track.TrackId, track.FromStationId, track.ToStationId,
        track.StartPositionMeters, track.EndPositionMeters, track.Direction, track.Kind, track.EffectiveLengthMeters,
        track.SpeedLimitMetersPerSecond, track.ConflictResourceIds.ToArray())).ToArray(),
    graph.Paths.Select(path => new ProjectRoutePath(path.PathId, path.FromPlatformId, path.ToPlatformId,
        path.Direction, path.TrackSegmentIds.ToArray(), path.ResourceIds.ToArray())).ToArray(),
    graph.TurnbackPlans.Select(plan => new ProjectTurnbackPlan(plan.TurnbackId, plan.Name, plan.StationId,
        plan.Kind, plan.ArrivalPlatformId, plan.DeparturePlatformId, plan.TrackSegmentIds.ToArray(),
        plan.ResourceIds.ToArray(), plan.TurnbackTimeSeconds)).ToArray(),
    graph.SpatialReferencePoints.Select(point => new ProjectSpatialReferencePoint(
        point.ReferencePointId, point.StationId, point.Name, point.Kind, point.AlternateBerthing,
        point.MainlineGradePermille, point.BranchlineGradePermille,
        point.DistanceFromStopToCrossoverMeters, point.CrossoverLengthMeters,
        point.DistanceFromCrossoverToTurnbackStopMeters, point.TurnbackDwellSeconds,
        point.SwitchSpeedLimitMetersPerSecond, point.MainlineApproachCruiseSpeedMetersPerSecond,
        point.BranchlineApproachCruiseSpeedMetersPerSecond, point.MainlineSafetyFactor,
        point.BranchlineSafetyFactor, point.MainlineTrafficRatio,
        point.StationForwardGradeInPermille, point.StationForwardGradeOutPermille,
        point.StationForwardDistanceToSignalMeters, point.StationForwardOverlapMeters,
        point.StationForwardDwellSeconds, point.StationForwardEarlierCruiseSpeedMetersPerSecond,
        point.StationForwardLaterCruiseSpeedMetersPerSecond, point.StationForwardSafetyFactor,
        point.StationReverseGradeInPermille, point.StationReverseGradeOutPermille,
        point.StationReverseDistanceToSignalMeters, point.StationReverseOverlapMeters,
        point.StationReverseDwellSeconds, point.StationReverseEarlierCruiseSpeedMetersPerSecond,
        point.StationReverseLaterCruiseSpeedMetersPerSecond, point.StationReverseSafetyFactor)).ToArray());

static (VehicleTypeDefinition[] Vehicles, ServiceTypeDefinition[] Services, StopPatternDefinition[] Stops) CreatePlanningCatalogs()
{
    var vehicles = new[]
    {
        new VehicleTypeDefinition("EMU-6", "六節電聯車", 80, 22.222, 1, 0.9, 1.3, 0.65, 0.45, 0.05)
    };
    var services = new[]
    {
        new ServiceTypeDefinition("LOCAL", "普通車", "#4472C4", "LOCAL", "ALL_STOP", "EMU-6")
    };
    var stops = new[]
    {
        new StopPatternDefinition("ALL_STOP", "普通車（全停站）", [
            new StopPatternInstruction("O01", StopPatternAction.Stop),
            new StopPatternInstruction("O02", StopPatternAction.Stop),
            new StopPatternInstruction("O03", StopPatternAction.Stop)])
    };
    return (vehicles, services, stops);
}

static InfrastructureGraph CreateCustomInfrastructure(Route route)
{
    var track = new TrackSegmentDefinition("D-CUSTOM", route.Stations[0].StationId, route.Stations[^1].StationId,
        0, route.TotalLengthMeters, TrackDirection.Outbound, TrackKind.Mainline, route.TotalLengthMeters, 27.777,
        ["CUSTOM:BLOCK"]);
    var platforms = route.Stations.Select(station => new PlatformDefinition(
        $"{station.StationId}:CUSTOM", station.StationId, $"{station.StationName} 自訂月台", TrackDirection.Outbound,
        220, trackSegmentIds: ["D-CUSTOM"])).ToArray();
    var paths = route.Stations.Zip(route.Stations.Skip(1), (from, to) => new RoutePathDefinition(
        $"PATH:CUSTOM:{Array.IndexOf(route.Stations.ToArray(), from) + 1}", $"{from.StationId}:CUSTOM", $"{to.StationId}:CUSTOM",
        TrackDirection.Outbound, ["D-CUSTOM"], ["CUSTOM:BLOCK"])).ToArray();
    var yards = route.Stations.Select(station => new StationYardDefinition(
        station.StationId, station.StationName,
        platforms.Where(platform => platform.StationId == station.StationId), ["D-CUSTOM"],
        paths.Where(path => path.FromPlatformId.StartsWith(station.StationId, StringComparison.Ordinal)).Select(path => path.PathId))).ToArray();
    return new InfrastructureGraph(route, yards, [track], paths);
}

static void TestSimulationProjectRoundTrip()
{
    var source = CreateProjectDocument();
    var json = SimulationProjectFormat.Serialize(source);
    var restored = SimulationProjectFormat.Deserialize(json);

    Equal(SimulationProjectFormat.CurrentSchemaVersion, restored.SchemaVersion);
    Equal("測試專案線", restored.RouteName);
    Equal(3, restored.Stations.Length);
    Equal(2, restored.SpeedLimits.Length);
    Equal(MovingBlockMode.Control, restored.Simulation.MovingBlockMode);
    Equal(BrakingEstimationMode.Emergency, restored.Simulation.BrakingEstimationMode);
    NearlyEqual(45 / 3.6, restored.SpeedLimits[0].LimitMetersPerSecond, 1e-9);
    True(restored.ServicePatterns is null && restored.ServiceRuns is null, "Schema 7 不應保留舊執行資料源。");
    Equal(1, restored.VehicleTypes!.Length);
    Equal(1, restored.ServiceTypes!.Length);
    Equal(2, restored.StopPatterns!.Length);
    var express = restored.StopPatterns.Single(item => item.Id == "EXPRESS");
    Equal(StopPatternAction.Pass, express.Instructions.Single().Action);
    NearlyEqual(45 / 3.6, express.Instructions.Single().PassingSpeedLimitMetersPerSecond!.Value);
    True(json.Contains($"\"schemaVersion\": {SimulationProjectFormat.CurrentSchemaVersion}", StringComparison.Ordinal), "存檔必須包含版本欄位。");
}

static void TestLegacyProjectRejected()
{
    var root = JsonNode.Parse(SimulationProjectFormat.Serialize(CreateProjectDocument()))!.AsObject();
    foreach (var schema in Enumerable.Range(1, 6))
    {
        root["schemaVersion"] = schema;
        Throws<SimulationValidationException>(() => SimulationProjectFormat.Deserialize(root.ToJsonString()),
            "不支援存檔版本");
    }
}

static void TestSimulationProjectValidation()
{
    var root = JsonNode.Parse(SimulationProjectFormat.Serialize(CreateProjectDocument()))!.AsObject();
    root["schemaVersion"] = 999;
    var unknownVersion = root.ToJsonString();
    Throws<SimulationValidationException>(
        () => SimulationProjectFormat.Deserialize(unknownVersion),
        "不支援存檔版本");
    Throws<SimulationValidationException>(
        () => SimulationProjectFormat.Deserialize("{ invalid json"),
        "JSON 格式無效");
}

static void TestVehicleCatalogPerformanceAuthority()
{
    var route = RouteFactory.FromSegmentDistances(
        "CAT",
        "車型目錄性能測試線",
        [
            new StationInput("CAT01", "起點", 0, 0),
            new StationInput("CAT02", "遠端", 50000, 0)
        ],
        0);
    var catalogVehicle = new VehicleTypeDefinition(
        "CAT-SLOW",
        "目錄慢車",
        lengthMeters: 150,
        maxSpeedMetersPerSecond: 12,
        accelerationMetersPerSecondSquared: 0.8,
        serviceBrakeDecelerationMetersPerSecondSquared: 0.6,
        emergencyBrakeDecelerationMetersPerSecondSquared: 1.2,
        jerkMetersPerSecondCubed: 0.2,
        tractionDecayPerSecond: 0.8,
        coastingDecelerationMetersPerSecondSquared: 0.3);
    var noFadeVehicle = new VehicleTypeDefinition(
        "CAT-NO-FADE",
        "目錄無衰減車",
        150,
        12,
        0.8,
        0.6,
        1.2,
        0.2,
        0,
        0.3);
    var plan = new ServiceRunPlan("Vehicle 01", 1, TrainDirection.Outbound, "普通車", "ALL_STOP", catalogVehicle.Id);

    SimulationWorld Build(VehicleTypeDefinition vehicle) => new(
        route,
        new TrainParameters(40, 4, 4, 0, 0, 0),
        OperationalParameters.CreateDefault(),
        trainCount: 1,
        movingBlockMode: MovingBlockMode.Independent,
        serviceRunPlans: [plan with { VehicleTypeId = vehicle.Id }],
        vehicleTypes: [vehicle]);

    var world = Build(catalogVehicle);
    world.Tick();
    var first = world.GetSnapshot().Trains.Single();
    NearlyEqual(0.02, first.AccelerationMetersPerSecondSquared, 1e-9);
    NearlyEqual(150, first.FrontPositionMeters - first.RearPositionMeters, 1e-9);
    Equal(catalogVehicle.Id, first.VehicleTypeId);

    world.AdvanceTo(100);
    True(world.Trajectory.All(sample => sample.SpeedMetersPerSecond <= catalogVehicle.MaxSpeedMetersPerSecond + 1e-6),
        "目錄最高速度應成為運算上限。");
    True(world.Trajectory.Any(sample => sample.Phase == OperationalPhase.Coasting
        && sample.AccelerationMetersPerSecondSquared < -0.02),
        "目錄惰行減速度應實際進入惰行運算。");

    var noFadeWorld = Build(noFadeVehicle);
    noFadeWorld.AdvanceTo(30);
    world.AdvanceTo(30);
    True(world.GetSnapshot().Trains.Single().SpeedMetersPerSecond
        < noFadeWorld.GetSnapshot().Trains.Single().SpeedMetersPerSecond - 0.05,
        "目錄牽引衰減應影響加速後速度。");

    var brakingWorld = new SimulationWorld(
        route,
        new TrainParameters(40, 4, 4, 0, 0, 0),
        OperationalParameters.CreateDefault(),
        trainCount: 2,
        movingBlockMode: MovingBlockMode.Monitoring,
        serviceRunPlans:
        [
            plan,
            new ServiceRunPlan("Vehicle 02", 1, TrainDirection.Outbound, "普通車", "ALL_STOP", catalogVehicle.Id,
                PlannedDepartureTimeSeconds: 100)
        ],
        vehicleTypes: [catalogVehicle]);
    brakingWorld.AdvanceTo(130);
    var serviceDemand = brakingWorld.GetSnapshot().SafetyObservations.Single().ObstacleBrakingDemandMeters;
    brakingWorld.SetBrakingEstimationMode(BrakingEstimationMode.Emergency);
    var emergencyDemand = brakingWorld.GetSnapshot().SafetyObservations.Single().ObstacleBrakingDemandMeters;
    True(emergencyDemand < serviceDemand, "目錄緊急煞車減速度應控制緊急煞車估算。");

    Throws<SimulationValidationException>(
        () => _ = new SimulationWorld(
            route,
            CreateParameters(),
            OperationalParameters.CreateDefault(),
            1,
            movingBlockMode: MovingBlockMode.Independent,
            serviceRunPlans: [plan with { VehicleTypeId = "MISSING" }],
            vehicleTypes: [catalogVehicle]),
        "找不到車型「MISSING」");
}

static SimulationProjectDocument CreateProjectDocument()
{
    var defaults = OperationalParameters.CreateDefault();
    return new SimulationProjectDocument(
        SimulationProjectFormat.CurrentSchemaVersion,
        "P",
        "測試專案線",
        [
            new ProjectStation("P01", "起點", 0, 0),
            new ProjectStation("P02", "中央", 1200, 30),
            new ProjectStation("P03", "終點", 800, 0)
        ],
        new ProjectTrainSettings(80 / 3.6, 1, 1, 30, 180, 360),
        new ProjectOperationalSettings(
            defaults.JerkMetersPerSecondCubed,
            defaults.CoastingRatio,
            defaults.ApproachDistanceMeters,
            defaults.ApproachSpeedMetersPerSecond,
            defaults.TractionFadeRatio,
            defaults.TrainLengthMeters,
            defaults.ServiceBrakingMetersPerSecondSquared,
            defaults.EmergencyBrakingMetersPerSecondSquared,
            defaults.ControlReactionTimeSeconds,
            defaults.BrakeBuildUpTimeSeconds,
            defaults.PositioningErrorMeters,
            defaults.SafetyMarginMeters,
            defaults.AbsoluteMinimumGapMeters),
        [
            new ProjectSpeedLimit(250, 600, 45 / 3.6, SpeedLimitDirection.Both, "彎道"),
            new ProjectSpeedLimit(1200, 1500, 35 / 3.6, SpeedLimitDirection.Outbound, "進站")
        ],
        new ProjectRunSettings(
            4,
            90,
            6 * 3600,
            20,
            OperationProfileMode.RealisticOperations,
            MovingBlockMode.Control,
            BrakingEstimationMode.Emergency),
        null,
        null,
        [new ProjectVehicleType("EMU-6", "六節電聯車", defaults.TrainLengthMeters, 80 / 3.6, 1,
            defaults.ServiceBrakingMetersPerSecondSquared, defaults.EmergencyBrakingMetersPerSecondSquared,
            defaults.JerkMetersPerSecondCubed, 0.45, 0.05)],
        [new ProjectServiceType("LOCAL", "普通車", "#4472C4", "L", "ALL_STOP", "EMU-6")],
        [
            new ProjectStopPattern("ALL_STOP", "所有車站停靠", [
                new ProjectStopPatternInstruction("P01", StopPatternAction.Stop),
                new ProjectStopPatternInstruction("P02", StopPatternAction.Stop, 31),
                new ProjectStopPatternInstruction("P03", StopPatternAction.Stop)]),
            new ProjectStopPattern("EXPRESS", "快速車", [
                new ProjectStopPatternInstruction("P02", StopPatternAction.Pass, PassingSpeedLimitMetersPerSecond: 45 / 3.6)])
        ],
        new ProjectDispatchPlan(
            DispatchPlanningMode.ManualTimetable,
            VehicleAssignmentMode.Automatic,
            [],
            [new ProjectManualTimetableRow(6 * 3600, TrainDirection.Outbound, "LOCAL", "EMU-6", "EXPRESS",
                VehicleId: "EMU-001", ServiceRunId: "RUN-001")]),
        null);
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
