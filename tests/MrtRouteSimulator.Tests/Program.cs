using System.Text.Json.Nodes;
using MrtRouteSimulator.Engine;

if (args.Length == 1 && args[0].Equals("--report-taichung-full-sample", StringComparison.OrdinalIgnoreCase))
{
    TaichungAirportFullScenarioTests.PrintKeyEventReport();
    return;
}

if (args.Length == 2 && args[0].Equals("--write-taichung-full-sample", StringComparison.OrdinalIgnoreCase))
{
    var outputPath = Path.GetFullPath(args[1]);
    var outputDirectory = Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException("輸出路徑缺少目錄。 ");
    Directory.CreateDirectory(outputDirectory);
    File.WriteAllText(outputPath, TopologyProjectFormat.Serialize(TaichungAirportFullScenarioBuilder.BuildFull()));
    Console.WriteLine($"已輸出臺中機場捷運 full Schema 8 sample：{outputPath}");
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("臺中機場捷運 full station chain 完整驗證", TaichungAirportFullScenarioTests.FullStationChainValidates),
    ("臺中機場捷運 29.9km／23.8km 路線長度", TaichungAirportFullScenarioTests.RouteLengthsMatchReviewedSource),
    ("臺中機場捷運全程車 28 站全停完成", TaichungAirportFullScenarioTests.FullLineAllStopCompletes),
    ("臺中機場捷運機場直達車 skip-stop", TaichungAirportFullScenarioTests.AirportDirectSkipStopWorks),
    ("臺中機場捷運 SECTION O20 topology-native 袋狀軌折返", TaichungAirportFullScenarioTests.O20TurnbackCompletes),
    ("臺中機場捷運 DIRECT O20 折返且不進入 O21-O26", TaichungAirportFullScenarioTests.AirportDirectTurnsAtO20AndReturns),
    ("臺中機場捷運 SECTION O04 越行後 O20 折返", TaichungAirportFullScenarioTests.SectionOvertakesAtO04ThenTurnsAtO20),
    ("臺中機場捷運 O04 越行與 rear-clear", TaichungAirportFullScenarioTests.O04OvertakingCompletesSafely),
    ("臺中機場捷運 O13 越行與 rear-clear", TaichungAirportFullScenarioTests.O13OvertakingCompletesSafely),
    ("臺中機場捷運直達車完成兩次實體越行", TaichungAirportFullScenarioTests.AirportDirectCompletesTwoOvertakes),
    ("臺中機場捷運 O04／O13 雙向四股實體 topology", TaichungAirportFullScenarioTests.O04AndO13AreBidirectionalFourTrackStations),
    ("臺中機場捷運完整情境無碰撞與停站違規", TaichungAirportFullScenarioTests.NoCollisionOrStationStopViolation),
    ("臺中機場捷運代表性班表所有列車完成", TaichungAirportFullScenarioTests.AllExpectedTrainsComplete),
    ("臺中機場捷運 full sample Schema 8 round-trip", TaichungAirportFullScenarioTests.Schema8RoundTripPreservesFullScenario),
    ("臺中機場捷運 full sample 建立 topology-native world", TaichungAirportFullScenarioTests.CreatesTopologyNativeSimulationWorld),
    ("V4 尾軌折返後反向終點月台停站與首站", TopologyTurnbackRegressionTests.PhysicalTailTurnbackStopsAtFirstReverseStation),
    ("V4 尾軌折返 0 秒停站仍等待接續班表", TopologyTurnbackRegressionTests.PhysicalTailTurnbackWaitsForScheduledDepartureWithZeroDwell),
    ("V4 topology 上行結果輸出與方向篩選", TopologyResultsOutputTests.InboundTopologyResultsUseGlobalDisplayPositions),
    ("V4 中心停點須真正到站才完成區間", TopologyResultsOutputTests.TrainCenterResultsRequireStationEvents),
    ("快速建線、分割與設施精靈保存實體接軌側別", DirectionPortTests.ConstructionAndSplitPreservePorts),
    ("起始站中心0K及端點外延伸里程", StationChainageTests.StationCentersAnchorZeroAndExtendPastTerminals),
    ("多車長換端保持完整車體占用", TurnbackFootprintTests.ReversalPreservesWholeVehicleForMultipleLengths),
    ("袋狀軌多車長換端保持完整車體占用", TurnbackFootprintTests.PocketReversalPreservesWholeVehicleForMultipleLengths),
    ("月台容量依實體範圍與雙向車頭後方長度檢核", StationConstructionRuleTests.RejectsFalsePlatformCapacity),
    ("中段折返連續尋徑且禁止錯誤轉向", InteriorTurnbackTests.NavigatorTraversesInteriorStopsWithoutSkippingEdges),
    ("折返返回實際駛至中段月台", InteriorTurnbackTests.WorldDrivesToInteriorDeparturePlatform),
    ("站場建置拒絕停點錯位及錯誤接續", StationConstructionRuleTests.RejectsInconsistentStationConstruction),
    ("計畫時間軸折返保留實體cursor", StationLayoutTemplateTests.PlannedTurnbackTimelinesKeepPhysicalCursors),
    ("共用站節點不允許跨股道安全距離捷徑", GraphDistanceConstraintTests.SharedStationNodesDoNotConnectParallelTracks),
    ("袋狀軌對向發車互斥與車尾淨空釋放", PocketServiceReservationTests.CentralPocketServiceRoutesReserveWaitAndReleaseSafely),
    ("有向轉向 physical port 規則與 Schema 8 round-trip", DirectionPortTests.CentralPocketRejectsIllegalCrossoverTurn),
    ("有向轉向加入 ServiceRoute 仍拒絕同側跨接", DirectionPortTests.CentralPocketRejectsIllegalTurnWhenRouteDeclaresIt),
    ("合法未使用備用有向轉向可保存", DirectionPortTests.UnusedLegalBackupConnectionCanBeSaved),
    ("schematicLane 不影響 physical port 驗證", DirectionPortTests.SchematicLaneDoesNotChangePortValidation),
    ("單端 physical port metadata 拒絕", DirectionPortTests.OneSidedPortMetadataIsRejected),
    ("原地折返同 edge 反向 traversal 合法", DirectionPortTests.InteriorSameEdgeReverseTurnbackRemainsLegal),
    ("physical port metadata Schema 8 round-trip 保留", DirectionPortTests.PortMetadataRoundTripsThroughSchema8),
    ("PDF 七種站場往返及完整運行", StationLayoutTemplateTests.BuildsAndRoundTripsAllStationLayoutTemplates),
    ("PDF 站前站後折返實體軌跡及接續", StationLayoutTemplateTests.BuildsFacilityTemplatesWithPhysicalTraversals),
    ("PDF 三四股道實際使用側線", StationLayoutTemplateTests.ThreeAndFourTrackTemplatesExposeAdditionalPhysicalTracks),
    ("PDF 袋狀軌僅中央停靠、外側通過", StationLayoutTemplateTests.CentralPocketUsesPocketOnlyAtStationBAndBypassRoutesSkipB),
    ("站體呈現設定 Schema 8 往返", StationPresentationTests.Schema8RoundTripsStationPresentationFields),
    ("站體呈現設定非法值拒絕", StationPresentationTests.RejectsInvalidStationPresentationFields),
    ("站體呈現設定不改變實體運行", StationPresentationTests.StationPresentationFieldsDoNotChangeRuntimeStopTrajectory),
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
    ("Route 表單輸入會先轉為 topology-native V2 world", TestRouteInputConvertsToTopologyNativeWorld),
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
    ("軌跡留存策略不改變事件且可限制取樣", TestTraceRetentionPolicies),
    ("模擬會話統一推進實際與計畫世界", TestSimulationSession),
    ("V2 首班列車在零秒準時啟用", TestInitialV2Departure),
    ("極短班距碰撞保護不產生負里程", TestCollisionProtectionClampsRouteBoundary),
    ("障礙物急停可指定列車與排程時間", TestScheduledObstacleStop),
    ("終點長折返時控制模式不發生追撞", TestTerminalOccupancyProtection),
    ("控制模式在起點未淨空時延後發車", TestDepartureClearanceProtection),
    ("同一追撞只記錄一次碰撞事件", TestCollisionEventRecordedOnce),
    ("動態煞車包絡線使到站前速度接近零", TestDynamicStationBrakingContinuity),
    ("StationStopController 以曲線與預測停點控制精停", TestStationStopControllerTracksCurveAndPrediction),
    ("進站低速限制解除後仍受精停目標速度約束", TestStationStopControllerPreventsPostNearStopBounce),
    ("SimulationWorld 進站近停後不會重新牽引回彈", TestSimulationWorldStationBrakingDoesNotReaccelerate),
    ("移動閉塞控制不再把高速列車瞬間歸零", TestMovingBlockControlDoesNotHardStop),
    ("跨站車不套用固定進站速度或停站", TestExpressServicePassesStation),
    ("跨站車仍遵守車站通過速限", TestPassingServiceHonorsStationLimit),
    ("折返後可套用不同停站模式", TestTurnaroundLoadsDifferentServicePattern),
    ("軟體版本符合三段式規則且與組件一致", TestProductVersionMetadata),
    ("Schema 7 派車計畫支援上下行與跨午夜排序", TestDispatchPlanExpansion),
    ("Schema 7 派車計畫拒絕缺漏目錄參照", TestDispatchPlanMissingCatalogReference),
    ("V2 寫實引擎拒絕重複指定車輛", TestDispatchDuplicateVehicleRejected),
    ("V3.4 資源占用時間軸可獨立統計月台與衝突區", TestResourceOccupancyAnalysis),
    ("V2 區間統計支援完整篩選、控制受限秒數與 P95", TestIntervalStatistics),
    ("產品 EngineKind 與 ProfileMode 可獨立設定", TestEngineKindProfileModeSeparation),
    ("V3.2 五類空間參考點欄位與上下限有效", TestSpatialReferencePointValidation),
    ("V3.4 實體站空間分類完整且拒絕虛擬節點", TestPhysicalStationSpatialClassification),
    ("V3.2 URCS 五類預設安全時距與容量相符", TestSpatialCapacityDefaultVectors),
    ("V3.2 URCS 中間站順逆行參數獨立", TestSpatialCapacityStationDirections),
    ("V3.2 URCS 容量採截斷且拒絕無效坡度組合", TestSpatialCapacityTruncationAndValidation),
    ("V3.2 首班零秒與端點退出完整進入時刻表及區間統計", TestV3TimetableAndIntervalTerminalBoundaries),
    ("V2 車型目錄性能成為列車運算權威", TestVehicleCatalogPerformanceAuthority),
    ("V4 線性 builder 逐站建立雙向 topology", TopologyRegressionTests.LinearBuilderCreatesSegmentedBidirectionalTopology),
    ("V4 線性 builder 保留舊 Route", TopologyRegressionTests.LinearBuilderPreservesLegacyRoute),
    ("V4 topology validator 拒絕缺漏節點與無效月台 offset", TopologyRegressionTests.ValidatorRejectsMissingNodesAndInvalidPlatformOffsets),
    ("V4 topology validator 拒絕不連續與違反方向的 traversal", TopologyRegressionTests.ValidatorRejectsDisconnectedAndDirectionallyInvalidRoute),
    ("V4 RouteProjection 保留正向 chainage 與邊界", TopologyRegressionTests.RouteProjectionPreservesForwardChainageAndBoundaries),
    ("V4 RouteProjection 處理反向與重複 edge", TopologyRegressionTests.RouteProjectionHandlesReverseTraversalAndRequiresTraversalIndexForLoops),
    ("V4 ResolvedStop 以月台 track position 依序解析", TopologyRegressionTests.ResolvedStopsUsePlatformTrackPositionsInRouteOrder),
    ("V4 Phase D SimulationWorld 輸出同步 topology position", TopologyRegressionTests.SimulationWorldPublishesSynchronizedTopologyRuntimeMirror),
    ("V4 Phase E SimulationWorld 主線依 traversal 推進", TopologyRegressionTests.SimulationWorldAdvancesNormalMainlineByOrderedTraversal),
    ("V4 Phase H SimulationWorld 可由 topology 建立", TopologyRegressionTests.SimulationWorldCanStartFromTopologyWithoutRouteInput),
    ("V4 topology path finder 依約束產生可重現有向路徑", TopologyRegressionTests.PathFinderBuildsDeterministicConstrainedTraversal),
    ("V4 topology 幾何、折返設施與車站作業均受驗證", TopologyRegressionTests.DomainExtensionsValidateTopologyReferences),
    ("Schema 8 topology 專案可完整往返", TopologyRegressionTests.Schema8TopologyProjectRoundTrips),
    ("Schema 8 拒絕 legacy 欄位與非本版格式", TopologyRegressionTests.Schema8RejectsLegacyOrWrongVersion),
    ("Schema 8 topology baseline 範例可載入並建立世界", TopologyRegressionTests.Schema8TopologyBaselineSampleLoadsAndBuildsWorld),
    ("所有範例均為有界且完整可執行的 Schema 8 topology 專案", TopologyRegressionTests.AllSamplesLoadAndBuildTopologyWorld),
    ("V4 topology SimulationWorld 不建立 compatibility Route", TopologyRegressionTests.TopologySimulationWorldDoesNotConstructCompatibilityRoute),
    ("V4 完整 topology 範例逐一執行實體越行、袋狀軌與雙端尾軌", TopologyRegressionTests.ComprehensiveTopologySampleExercisesAllPhysicalFacilities),
    ("V4 道岔有向轉向限制約束尋徑與 runtime movement plan", TopologyRegressionTests.DirectedConnectionsRestrictSwitchPathsAndMovementPlans),
    ("V4 尾軌折返停點保留 edge-local offset 並立即反向", TopologyRegressionTests.TurnbackStopPositionUsesPhysicalOffsetAndImmediateReverse),
    ("V4 topology 時刻表、區間統計與 CSV 不需要 compatibility Route", TopologyRegressionTests.TopologyResultsUseResolvedStopsWithoutCompatibilityRoute),
    ("V4 topology runtime cursor、footprint、occupancy 與 graph distance 一致", TopologyRegressionTests.TopologyRuntimeCursorFootprintOccupancyAndDistance),
    ("V4 unified movement plan 以實體 facility traversal 管理 footprint 與 rear-clear", TopologyRegressionTests.UnifiedMovementPlanUsesFacilityTraversalsAndRearClear),
    ("V4 SimulationWorld 以實體 facility traversal 折返且不落入 virtual track", TopologyRegressionTests.SimulationWorldTurnsBackThroughTopologyFacilityWithoutVirtualTrack),
    ("V4 SimulationWorld 以實體 pocket traversal 折返且不落入 virtual track", TopologyRegressionTests.SimulationWorldTurnsBackAtPocketTrackWithoutVirtualLocation),
    ("V4 平行 passing edge 的 occupancy 與 graph safety 不以投影里程誤判", TopologyRegressionTests.ParallelPassingEdgesRemainSeparateForOccupancyAndGraphSafety),
    ("V4 快速車以實體 passing facility 跨越停靠普通車並 rear-clear", TopologyRegressionTests.SimulationWorldPassesLocalTrainThroughPhysicalTopologyFacility),
    ("V4 topology facility 折返保留 VehicleId 並接續指定車次", TopologyRegressionTests.TopologyTurnbackPreservesVehicleAndActivatesContinuationRun),
    ("V4 正常主線 SimulationWorld 以 topology footprint 執行安全觀測", TopologyRegressionTests.SimulationWorldUsesTopologyFootprintsForNormalMainlineSafety),
    ("V4 SimulationSession options 可建立 topology world", TopologyRegressionTests.SimulationSessionOptionsCanConstructTopologyWorld),
    ("Schema 8 編輯 state 保持交易式 commit 邊界", TopologyRegressionTests.ProjectEditorStateIsTransactional),
    ("Schema 8 edge split 同步重寫月台、區間與 ServiceRoute", TopologyRegressionTests.EdgeSplitRewritesPhysicalReferences),
    ("Schema 8 刪除被引用 edge 會列出 dependency 並拒絕", TopologyRegressionTests.ReferencedTopologyObjectsCannotBeDeleted),
    ("Schema 8 設施精靈建立實體 tail 與 pocket topology", TopologyRegressionTests.FacilityWizardsCreatePhysicalTopologyWithoutVirtualTracks),
    ("Schema 8 快速建線直接建立可執行 topology 專案", TopologyRegressionTests.QuickLinearBuilderCreatesExecutableTopologyProject),
    ("Schema 8 ServiceRoute 候選月台限於實體 traversal", TopologyRegressionTests.ServiceRouteCandidatesStayOnPhysicalTraversal),
    ("大型 sample builder 依三層 gate 驗證分階段", TopologyScenarioBuilderTests.MinimalBaselineAndStagesUseThreeLayerGate),
    ("大型 sample builder 由 quick builder 建立 minimal baseline", TopologyScenarioBuilderTests.QuickBuilderCreatesValidatedMinimalBaseline),
    ("大型 sample builder 失敗階段不取代有效文件", TopologyScenarioBuilderTests.InvalidStageDoesNotReplaceCurrentDocument),
    ("legacy port migration 只盤點兩端皆缺側別的 edge", LegacyPortMigrationTests.ListsOnlyEdgesWithBothSidesMissing),
    ("legacy port migration 套用明確側別後重新驗證", LegacyPortMigrationTests.AppliesOnlyExplicitAssignmentsAndRevalidates),
    ("legacy port migration 不從缺資料推測側別", LegacyPortMigrationTests.NeverInfersMissingAssignment),
    ("Schema 8 目錄刪除會保留引用保護", TopologyRegressionTests.CatalogDeletesRespectReferences),
    ("Schema 8 驗證 metadata 保留舊介面並鎖定錯誤 owner", TopologyRegressionTests.ValidationMetadataPreservesCompatibilityAndOwnerIdentity),
    ("Schema 8 驗證 target 涵蓋目錄、派車、基礎設施與設施", TopologyRegressionTests.ValidationTargetsCoverCatalogDispatchInfrastructureAndFacilities),
    ("Schema 8 ServiceRoute 驗證回傳結構化 target", TopologyRegressionTests.RouteValidationUsesStructuredTargetMetadata),
    ("Schema 7 可轉為可驗證的 Schema 8 topology 編輯起稿", TestSchema7CreatesTopologyEditorDraft)
};

var passed = 0;
var failures = new List<string>();
var selectedTests = args.Length >= 2 && args[0].Equals("--filter", StringComparison.OrdinalIgnoreCase)
    ? tests.Where(test => test.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)).ToArray()
    : tests;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine($"MRT 路線進出站時間模擬器 {ProductVersion.Current} - 自動化測試");
Console.WriteLine(new string('=', 58));

foreach (var test in selectedTests)
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
Console.WriteLine($"結果：{passed}/{selectedTests.Length} 通過，{failures.Count} 失敗");

if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

return;

#pragma warning disable CS8321 // 已移出執行基準的 Schema 7／virtual-track 回歸案例，待舊測試檔分拆後刪除。

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
    True(result.Samples.All(sample => sample.PositionMeters <= route.TotalLengthMeters + 1e-8), "位置不得越站。");
}

static void TestRouteInputConvertsToTopologyNativeWorld()
{
    var world = new SimulationWorld(
        CreateThreeStationRoute(),
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        trainCount: 1,
        movingBlockMode: MovingBlockMode.Independent);

    Throws<InvalidOperationException>(() => _ = world.Route, "不提供 compatibility Route");
    Throws<InvalidOperationException>(() => _ = world.Infrastructure, "不提供 legacy InfrastructureGraph");
    Equal(2, world.TopologyInfrastructure.TurnbackFacilities.Count);
    True(world.TopologyInfrastructure.Edges.Values.Any(edge => edge.Kind == TrackEdgeKind.TailTrack),
        "Route 表單輸入進入 V2 前必須轉成實體尾軌 topology。 ");
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
    var speedLimits = Enumerable.Range(0, 100).Select(index => source[0] with
    {
        SimulationTimeSeconds = index * .1,
        TrackSpeedLimitMetersPerSecond = index == 37 ? 5 : 20
    }).ToArray();
    var reducedLimits = TrajectoryAnalysis.DecimatePreservingCriticalPoints(speedLimits, 8);
    foreach (var index in new[] { 36, 37, 38 })
        True(reducedLimits.Contains(speedLimits[index]), "短速限區段的進出邊界不可被抽樣省略。");
}

static void TestTraceRetentionPolicies()
{
    var options = new SimulationWorldOptions(
        CreateFiveStationRoute(),
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        1,
        InitialDepartureIntervalSeconds: 60,
        MovingBlockMode: MovingBlockMode.Independent);
    var full = (options with { TraceRetentionPolicy = SimulationTraceRetentionPolicy.Full }).CreateWorld();
    var decimated = (options with { TraceRetentionPolicy = SimulationTraceRetentionPolicy.Decimated(2) }).CreateWorld();
    var eventsOnly = (options with { TraceRetentionPolicy = SimulationTraceRetentionPolicy.EventsOnly }).CreateWorld();

    full.AdvanceTo(60);
    decimated.AdvanceTo(60);
    eventsOnly.AdvanceTo(60);

    True(decimated.Trajectory.Count < full.Trajectory.Count,
        "降採樣留存應少於完整 0.1 秒軌跡，但不得改變引擎推進。" );
    Equal(full.Events.Count, decimated.Events.Count);
    Equal(full.Events.Count, eventsOnly.Events.Count);
    Equal(0, eventsOnly.Trajectory.Count);
    True(decimated.Trajectory.Any(sample => sample.Phase == OperationalPhase.Accelerating),
        "降採樣留存至少應保留車次的初始相位。" );
}

static void TestSimulationSession()
{
    var actualOptions = new SimulationWorldOptions(
        CreateThreeStationRoute(),
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        1,
        InitialDepartureIntervalSeconds: 60,
        MovingBlockMode: MovingBlockMode.Independent);
    var plannedOptions = actualOptions with
    {
        ProfileMode = OperationProfileMode.BasicPhysics,
        MovingBlockMode = MovingBlockMode.Independent
    };
    var session = new SimulationSession(actualOptions, plannedOptions);

    session.PreparePlannedTimeline(180);
    var previewSamples = session.PlannedTrajectory.ToArray();
    True(previewSamples.Length > 0 && previewSamples[^1].SimulationTimeSeconds > 100,
        "計畫速度預覽必須保留準備完成的時間軸。");
    True(session.PlannedEvents.Any(item => item.EventType == SimulationEventType.Departure),
        "模擬會話應先建立獨立的計畫事件時間線。" );
    var snapshot = session.AdvanceTo(10);
    NearlyEqual(10, snapshot.SimulationTimeSeconds, 1e-9);
    NearlyEqual(10, session.PlannedWorld.CurrentTimeSeconds, 1e-9);
    session.Reset();
    NearlyEqual(0, session.ActualWorld.CurrentTimeSeconds, 1e-9);
    NearlyEqual(0, session.PlannedWorld.CurrentTimeSeconds, 1e-9);
    True(session.PlannedEvents.Count > 0, "重設播放不應遺失已建立的計畫事件時間線。" );
    True(session.PlannedTrajectory.SequenceEqual(previewSamples), "推進和重設不可覆寫完整計畫軌跡。");
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
    var maximumDisplayChainage = world.GetTopologyResultContext()
        .GetStops(TrainDirection.Outbound)
        .Max(stop => stop.ProjectedChainageMeters);
    True(world.GetSnapshot().Trains.All(train => train.FrontPositionMeters <= maximumDisplayChainage + 1e-9), "碰撞停止位置不得超過路線終點。");
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
        True(Math.Abs(arrival.PositionMeters - prior.PositionMeters) <= 2 + 1e-7,
            "Jerk 受限近停收斂時，到站前一個 Tick 必須位於 2 m 停車點容許範圍。");
    }

    True(result.Events.All(item => item.EventType != SimulationEventType.StationStopViolation),
        "預設參數不得產生停車超限事件。");
    foreach (var arrival in arrivals)
    {
        var station = route.Stations.Single(item => Math.Abs(item.PositionMeters - arrival.PositionMeters) <= 1e-7);
        var approachSamples = result.Samples
            .Where(sample => sample.NextStationId == station.StationId
                && sample.SimulationTimeSeconds < arrival.SimulationTimeSeconds)
            .ToArray();
        var prematureNearStops = approachSamples
            .Where(sample => sample.Phase == OperationalPhase.ApproachBraking
                && sample.SpeedMetersPerSecond <= 0.15
                && Math.Abs(station.PositionMeters - sample.PositionMeters) > 0.5)
            .ToArray();
        True(!prematureNearStops.Any(nearStop => approachSamples.Any(later =>
                later.SimulationTimeSeconds > nearStop.SimulationTimeSeconds
                && later.SpeedMetersPerSecond > nearStop.SpeedMetersPerSecond + 0.1)),
            $"{station.StationId} 進站近停後不得再次牽引加速。");
    }
    var envelope = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
        80 / 3.6,
        1,
        0.9,
        0.65,
        0.1,
        jerkLimited: true);
    True(envelope.DistanceMeters > 0 && envelope.DurationSeconds > 0, "動態煞停距離與時間必須為正值。");
}

static void TestStationStopControllerTracksCurveAndPrediction()
{
    var highSpeed = StationStopController.Calculate(new StationStopControlInput(
        RemainingDistanceMeters: 140,
        CurrentSpeedMetersPerSecond: 22,
        CurrentAccelerationMetersPerSecondSquared: 0.5,
        LinePermittedSpeedMetersPerSecond: 25,
        ServiceBrakingMetersPerSecondSquared: 0.9,
        JerkMetersPerSecondCubed: 0.65,
        TimeStepSeconds: 0.1,
        JerkLimited: true,
        SnapDistanceMeters: 2,
        SnapSpeedMetersPerSecond: 0.15));
    True(highSpeed.TargetSpeedMetersPerSecond < 25,
        "高速進站必須由距離—速度煞車曲線壓低一般線速。 ");
    True(highSpeed.RequiresServiceBraking,
        "全煞預測越過停車點時必須要求營運煞車。 ");
    True(highSpeed.StopPositionErrorMeters > 0,
        "停點預測誤差為正時必須明確表示會越過停車點。 ");

    var precision = StationStopController.Calculate(new StationStopControlInput(
        RemainingDistanceMeters: 8,
        CurrentSpeedMetersPerSecond: 0.1,
        CurrentAccelerationMetersPerSecondSquared: 0,
        LinePermittedSpeedMetersPerSecond: 20,
        ServiceBrakingMetersPerSecondSquared: 0.9,
        JerkMetersPerSecondCubed: 0.65,
        TimeStepSeconds: 0.1,
        JerkLimited: true,
        SnapDistanceMeters: 2,
        SnapSpeedMetersPerSecond: 0.15));
    Equal(StationStopControlMode.PrecisionApproach, precision.Mode);
    True(precision.TargetSpeedMetersPerSecond < StationStopController.PrecisionMaximumSpeedMetersPerSecond,
        "低速精停階段必須再依剩餘距離收斂目標速度。 ");

    var snap = StationStopController.Calculate(new StationStopControlInput(
        RemainingDistanceMeters: 1.5,
        CurrentSpeedMetersPerSecond: 0.1,
        CurrentAccelerationMetersPerSecondSquared: 0,
        LinePermittedSpeedMetersPerSecond: 20,
        ServiceBrakingMetersPerSecondSquared: 0.9,
        JerkMetersPerSecondCubed: 0.65,
        TimeStepSeconds: 0.1,
        JerkLimited: true,
        SnapDistanceMeters: 2,
        SnapSpeedMetersPerSecond: 0.15));
    Equal(StationStopControlMode.StopSnap, snap.Mode);
    Equal(0d, snap.TargetSpeedMetersPerSecond);
}

static void TestStationStopControllerPreventsPostNearStopBounce()
{
    var route = CreateThreeStationRoute();
    var world = new SimulationWorld(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        trainCount: 1,
        speedLimits:
        [
            new SpeedLimitSegment(980, 990, 0.05, SpeedLimitDirection.Outbound, "近停限制解除回歸")
        ],
        movingBlockMode: MovingBlockMode.Independent);
    while (!world.Events.Any(item => item.EventType == SimulationEventType.Arrival
               && item.Direction == TrainDirection.Outbound
               && Math.Abs(item.PositionMeters - 1000) <= 0.5)
           && world.CurrentTimeSeconds < 500)
    {
        world.Tick();
    }

    var arrival = world.Events.Single(item => item.EventType == SimulationEventType.Arrival
        && item.Direction == TrainDirection.Outbound && Math.Abs(item.PositionMeters - 1000) <= 0.5);
    var approach = world.Trajectory
        .Where(sample => sample.Direction == TrainDirection.Outbound
            && sample.NextStationId == "O02"
            && sample.SimulationTimeSeconds < arrival.SimulationTimeSeconds)
        .OrderBy(sample => sample.SimulationTimeSeconds)
        .ToArray();
    var nearStop = approach
        .Where(sample => sample.PositionMeters >= 980 && sample.PositionMeters <= 990.5)
        .MinBy(sample => sample.SpeedMetersPerSecond);
    True(nearStop is not null && nearStop.SpeedMetersPerSecond <= 1,
        $"近站低速限制必須使列車接近低速；實際最低 {nearStop?.SpeedMetersPerSecond * 3.6:0.##} km/h。 ");
    var laterPeakSpeed = approach
        .Where(sample => sample.SimulationTimeSeconds > nearStop!.SimulationTimeSeconds)
        .Max(sample => sample.SpeedMetersPerSecond);

    True(laterPeakSpeed <= StationStopController.PrecisionMaximumSpeedMetersPerSecond + 0.2,
        $"低速限制解除後，進站列車最高僅能回復至精停目標速度；實際 {laterPeakSpeed * 3.6:0.##} km/h。 ");
}

static void TestSimulationWorldStationBrakingDoesNotReaccelerate()
{
    var route = CreateThreeStationRoute();
    var world = new SimulationWorld(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        trainCount: 1,
        movingBlockMode: MovingBlockMode.Independent);
    world.AdvanceTo(500);

    var arrivals = world.Events
        .Where(item => item.EventType == SimulationEventType.Arrival && item.Direction == TrainDirection.Outbound)
        .ToArray();
    True(arrivals.Length >= 2, "測試列車必須抵達中間站與端點站。 ");
    var checkedApproachPairs = 0;
    foreach (var arrival in arrivals)
    {
        var stationId = route.Stations
            .Single(station => Math.Abs(station.PositionMeters - arrival.PositionMeters) <= 1e-7)
            .StationId;
        var approachSamples = world.Trajectory
            .Where(sample => sample.VehicleId == arrival.VehicleId
                && sample.Direction == arrival.Direction
                && sample.NextStationId == stationId
                && sample.SimulationTimeSeconds < arrival.SimulationTimeSeconds)
            .OrderBy(sample => sample.SimulationTimeSeconds)
            .ToArray();
        for (var index = 1; index < approachSamples.Length; index++)
        {
            var previous = approachSamples[index - 1];
            var current = approachSamples[index];
            if (previous.Phase != OperationalPhase.ApproachBraking
                || current.Phase != OperationalPhase.ApproachBraking)
            {
                continue;
            }

            checkedApproachPairs++;
            True(current.SpeedMetersPerSecond <= previous.SpeedMetersPerSecond + 1e-7,
                $"{stationId} 進站煞車鎖定後不應再次牽引加速。 ");
        }
    }

    True(checkedApproachPairs > 0, "測試情境必須覆蓋 SimulationWorld 的進站煞車軌跡。 ");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.StationStopViolation),
        "近停吸附不應產生停車超限事件。 ");
}

static void TestMovingBlockControlDoesNotHardStop()
{
    var world = CreateWorld(trainCount: 2, headwaySeconds: 15, movingBlockMode: MovingBlockMode.Control);
    world.AdvanceTo(260);
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
    world.AdvanceTo(820);

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

static void TestIndependentStopPatternAssignments()
{
    var vehicleSpecific = new VehicleTypeDefinition("VEH-LOCAL", "車型專屬停站", 80, 22.222, 1, 0.9, 1.3, 0.65, 0.45, 0.05, "VEHICLE_STOP");
    var serviceSpecific = new VehicleTypeDefinition("VEH-EXPRESS", "服務專屬停站", 80, 22.222, 1, 0.9, 1.3, 0.65, 0.45, 0.05);
    var services = new[]
    {
        new ServiceTypeDefinition("LOCAL", "普通服務", "#4472C4", "L", "SERVICE_STOP", "VEH-LOCAL"),
        new ServiceTypeDefinition("EXPRESS", "快速服務", "#C00000", "E", "SERVICE_STOP", "VEH-EXPRESS")
    };
    var stops = new[]
    {
        new StopPatternDefinition("VEHICLE_STOP", "車型專屬", [new StopPatternInstruction("P01", StopPatternAction.Stop), new StopPatternInstruction("P02", StopPatternAction.Pass), new StopPatternInstruction("P03", StopPatternAction.Stop)]),
        new StopPatternDefinition("SERVICE_STOP", "服務專屬", [new StopPatternInstruction("P01", StopPatternAction.Stop), new StopPatternInstruction("P02", StopPatternAction.Stop), new StopPatternInstruction("P03", StopPatternAction.Stop)])
    };
    var plan = new DispatchPlanDefinition([], [
        new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "VEH-LOCAL", serviceRunId: "VEHICLE-RUN"),
        new ManualTimetableRow(TimeSpan.FromMinutes(5), TrainDirection.Outbound, "EXPRESS", "VEH-EXPRESS", serviceRunId: "SERVICE-RUN")
    ], DispatchPlanningMode.ManualTimetable);

    var dispatch = DispatchPlanExpander.Expand(plan, [vehicleSpecific, serviceSpecific], services, stops);
    Equal("VEHICLE_STOP", dispatch.Runs.Single(item => item.ServiceRunId == "VEHICLE-RUN").StopPatternId);
    Equal("SERVICE_STOP", dispatch.Runs.Single(item => item.ServiceRunId == "SERVICE-RUN").StopPatternId);

    var document = CreateProjectDocument() with
    {
        VehicleTypes = [new ProjectVehicleType("VEH-LOCAL", "車型專屬停站", 80, 22.222, 1, 0.9, 1.3, 0.65, 0.45, 0.05, "VEHICLE_STOP")],
        ServiceTypes = [new ProjectServiceType("LOCAL", "普通服務", "#4472C4", "L", "SERVICE_STOP", "VEH-LOCAL")],
        StopPatterns = stops.Select(item => new ProjectStopPattern(item.Id, item.DisplayName, item.Instructions.Select(instruction =>
            new ProjectStopPatternInstruction(instruction.StationId, instruction.Action, instruction.DwellTimeSeconds, instruction.PassingSpeedLimitMetersPerSecond)).ToArray())).ToArray(),
        Dispatch = new ProjectDispatchPlan(DispatchPlanningMode.ManualTimetable, VehicleAssignmentMode.Automatic, [],
            [new ProjectManualTimetableRow(0, TrainDirection.Outbound, "LOCAL", "VEH-LOCAL", null, ServiceRunId: "VEHICLE-RUN")])
    };
    var restored = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    Equal("VEHICLE_STOP", restored.VehicleTypes!.Single().DefaultStopPatternId
        ?? throw new InvalidOperationException("車型預設停站模式未保存。"));
    var restoredDispatch = DispatchPlanExpander.Expand(ToDispatchPlanDefinition(restored.Dispatch!),
        (restored.VehicleTypes ?? throw new InvalidOperationException("缺少車型目錄。")).Select(ToVehicleTypeDefinition),
        (restored.ServiceTypes ?? throw new InvalidOperationException("缺少服務類型目錄。")).Select(item => new ServiceTypeDefinition(item.Id, item.DisplayName, item.ColorHex, item.RunPrefix,
            item.DefaultStopPatternId, item.DefaultVehicleTypeId, item.Priority, item.CanRequestOvertake, item.PreferredPlatformIds)),
        (restored.StopPatterns ?? throw new InvalidOperationException("缺少停站模式目錄。")).Select(item => new StopPatternDefinition(item.Id, item.DisplayName,
            item.Instructions.Select(instruction => new StopPatternInstruction(instruction.StationId, instruction.Action,
                instruction.DwellTimeSeconds, instruction.PassingSpeedLimitMetersPerSecond)))));
    Equal("VEHICLE_STOP", restoredDispatch.Runs.Single().StopPatternId);
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

static void TestResourceOccupancyAnalysis()
{
    var events = new[]
    {
        new SimulationEvent(0, SimulationEventType.RouteReserved, "V-01", null, TrainDirection.Outbound, "DOWN", 0, 0, "鎖定", ServiceRunId: "RUN-01", ResourceIds: ["PLATFORM:A", "CONFLICT:X"]),
        new SimulationEvent(10, SimulationEventType.RouteReleased, "V-01", null, TrainDirection.Outbound, "DOWN", 100, 5, "釋放衝突區", ServiceRunId: "RUN-01", ResourceIds: ["CONFLICT:X"]),
        new SimulationEvent(15, SimulationEventType.RouteReserved, "V-02", null, TrainDirection.Outbound, "DOWN", 0, 0, "鎖定", ServiceRunId: "RUN-02", ResourceIds: ["CONFLICT:X"]),
        new SimulationEvent(20, SimulationEventType.RouteReleased, "V-01", null, TrainDirection.Outbound, "DOWN", 200, 0, "釋放月台", ServiceRunId: "RUN-01", ResourceIds: ["PLATFORM:A"]),
        new SimulationEvent(30, SimulationEventType.RouteReleased, "V-02", null, TrainDirection.Outbound, "DOWN", 200, 0, "釋放衝突區", ServiceRunId: "RUN-02", ResourceIds: ["CONFLICT:X"])
    };

    var result = ResourceOccupancyAnalysis.Analyze(events, 30);
    Equal(3, result.Intervals.Count);
    var platform = result.Resources.Single(item => item.ResourceId == "PLATFORM:A");
    NearlyEqual(20, platform.OccupiedSeconds);
    NearlyEqual(66.6666667, platform.UtilizationPercent, 1e-5);
    var conflict = result.Resources.Single(item => item.ResourceId == "CONFLICT:X");
    Equal(2, conflict.ReservationCount);
    NearlyEqual(25, conflict.OccupiedSeconds);
    NearlyEqual(20, conflict.MinimumReleaseHeadwaySeconds ?? -1);
    True(ResourceOccupancyAnalysis.BuildCsv(result).Contains("CONFLICT:X", StringComparison.Ordinal),
        "資源占用分析 CSV 必須保留資源 ID。 ");
}

static void TestAutomaticPlatformAllocationBalancesCandidates()
{
    var route = RouteFactory.FromSegmentDistances(
        "PA", "自動多月台配置測試線",
        [new StationInput("PA01", "起點", 0, 0), new StationInput("PA02", "終點", 1000, 0)], 0);
    var vehicle = new VehicleTypeDefinition("EMU-PA", "多月台測試車", 80, 22.222, 1, 0.9, 1.3, 0.65, 0.45, 0.05);
    var service = new ServiceTypeDefinition("LOCAL", "普通車", "#4472C4", "PA", "ALL_STOP", "EMU-PA");
    var stops = new StopPatternDefinition("ALL_STOP", "全停站", [
        new StopPatternInstruction("PA01", StopPatternAction.Stop),
        new StopPatternInstruction("PA02", StopPatternAction.Stop)]);
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition([], [
            new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-PA", "ALL_STOP", "PA01:D1", "PA-V1", "PA-R1"),
            new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-PA", "ALL_STOP", "PA01:D2", "PA-V2", "PA-R2"),
            new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-PA", "ALL_STOP", "PA01:D3", "PA-V3", "PA-R3")],
            DispatchPlanningMode.ManualTimetable),
        [vehicle], [service], [stops]);
    var platforms = Enumerable.Range(1, 3).SelectMany(index => new[]
    {
        new PlatformDefinition($"PA01:D{index}", "PA01", $"起點下行 {index}", TrackDirection.Outbound, 220, trackSegmentIds: [$"PA-D{index}"]),
        new PlatformDefinition($"PA02:D{index}", "PA02", $"終點下行 {index}", TrackDirection.Outbound, 220, trackSegmentIds: [$"PA-D{index}"])
    }).ToArray();
    var tracks = Enumerable.Range(1, 3).Select(index => new TrackSegmentDefinition(
        $"PA-D{index}", "PA01", "PA02", 0, 1000, TrackDirection.Outbound, TrackKind.Mainline, 1000, 22.222,
        [$"TRACK:PA:{index}"])).ToArray();
    var paths = Enumerable.Range(1, 3).Select(index => new RoutePathDefinition(
        $"PA-P{index}", $"PA01:D{index}", $"PA02:D{index}", TrackDirection.Outbound, [$"PA-D{index}"],
        [$"PATH:PA:{index}"])).ToArray();
    var infrastructure = new InfrastructureGraph(route,
        [
            new StationYardDefinition("PA01", "起點站場", platforms.Where(item => item.StationId == "PA01"),
                tracks.Select(item => item.TrackId), paths.Select(item => item.PathId)),
            new StationYardDefinition("PA02", "終點站場", platforms.Where(item => item.StationId == "PA02"),
                tracks.Select(item => item.TrackId), paths.Select(item => item.PathId),
                platformAllocationStrategy: PlatformAllocationStrategy.Automatic)
        ],
        tracks, paths);
    var world = new SimulationWorld(route, CreateParameters(), OperationalParameters.CreateDefault(), 3,
        movingBlockMode: MovingBlockMode.Independent, dispatchPlan: dispatch, vehicleTypes: [vehicle], infrastructure: infrastructure);
    world.AdvanceTo(1);

    var assignedDestinationPlatforms = world.Events
        .Where(item => item.EventType == SimulationEventType.RouteReserved && item.ResourceIds is not null)
        .SelectMany(item => item.ResourceIds!)
        .Where(resource => resource.StartsWith("PLATFORM:PA02:", StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    Equal(3, assignedDestinationPlatforms.Length);
    True(assignedDestinationPlatforms.SequenceEqual(["PLATFORM:PA02:D1", "PLATFORM:PA02:D2", "PLATFORM:PA02:D3"], StringComparer.OrdinalIgnoreCase),
        "Automatic 策略必須在三座同方向可用月台間平均分配，而非固定取第一座。 ");
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
    True(world.IsComplete, "最後一列車退出營運後世界應回報完整循環完成。");
}

static void TestWorldCompletionRequiresAllDispatchVehiclesToExit()
{
    var route = CreateThreeStationRoute();
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition(
            [],
            [
                new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Outbound, "LOCAL", "EMU-6", "ALL_STOP",
                    vehicleId: "EMU-COMPLETE-01", serviceRunId: "RUN-COMPLETE-01"),
                new ManualTimetableRow(TimeSpan.FromSeconds(300), TrainDirection.Inbound, "LOCAL", "EMU-6", "ALL_STOP",
                    vehicleId: "EMU-COMPLETE-02", serviceRunId: "RUN-COMPLETE-02")
            ],
            DispatchPlanningMode.ManualTimetable),
        vehicles,
        services,
        stops);
    var world = new SimulationWorld(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        2,
        movingBlockMode: MovingBlockMode.Independent,
        dispatchPlan: dispatch,
        vehicleTypes: vehicles,
        infrastructure: InfrastructureGraph.CreateLegacy(route));

    while (!world.Events.Any(item => item.EventType == SimulationEventType.ServiceEnded
               && item.ServiceRunId == "RUN-COMPLETE-01")
           && world.CurrentTimeSeconds < 600)
    {
        world.Tick();
    }

    True(!world.IsComplete, "仍有尚未離開路線的排程車輛時不得提前回報完整循環完成。");
    while (!world.IsComplete && world.CurrentTimeSeconds < 1200)
    {
        world.Tick();
    }

    True(world.IsComplete, "全部排程車輛均退出營運後應回報完整循環完成。");
    Equal(2, world.Events.Count(item => item.EventType == SimulationEventType.ServiceEnded));
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

    while (!world.IsComplete && world.CurrentTimeSeconds < 1000)
    {
        world.Tick();
    }

    True(world.IsComplete, "指定折返接續的最後一個車次退出後，完整循環才應完成。");
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
    Equal(4, result.JourneyStatistics.Count);
    Equal(3, result.JourneyStatistics.Count(item => item.IsComplete));
    NearlyEqual(5, result.JourneyStatistics.Single(item => item.ServiceRunId == "RUN-2").AverageSpeedMetersPerSecond!.Value);
    var csv = IntervalStatistics.BuildCsv(result);
    var summaryCsv = IntervalStatistics.BuildSummaryCsv(result);
    var journeyCsv = IntervalStatistics.BuildJourneyCsv(result);
    True(csv.Contains("完成", StringComparison.Ordinal) && csv.Contains("運行中", StringComparison.Ordinal), "中文 CSV 應區分完成與運行中。");
    True(csv.Contains("移動閉塞受限(s)", StringComparison.Ordinal), "區間 CSV 應輸出移動閉塞受限秒數。");
    True(summaryCsv.Contains("第95百分位旅行時間(s)", StringComparison.Ordinal), "摘要 CSV 應包含 P95 欄位。");
    True(journeyCsv.Contains("起終站平均速度(km/h)", StringComparison.Ordinal), "全程平均速率 CSV 應輸出起終站平均速度欄位。");

    var completedOnly = IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(IncludeInProgress: false));
    Equal(3, completedOnly.CompletedCount);
    Equal(0, completedOnly.InProgressCount);
    var selected = IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(VehicleId: "V-2", ServiceRunId: "RUN-2", VehicleTypeId: "EMU-6",
            ServiceClassId: "普通車", ServicePatternId: "ALL_STOP", StartSimulationTimeSeconds: 90,
            EndSimulationTimeSeconds: 125, IncludeInProgress: false));
    Equal(1, selected.CompletedCount);
    Equal(1, selected.JourneyStatistics.Count);
    NearlyEqual(20, selected.CompletedIntervals.Single().ControlLimitedSeconds!.Value);
    Equal(0, IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(Direction: TrainDirection.Inbound)).AllIntervals.Count);
    Equal(0, IntervalStatistics.Analyze(route, samples, events,
        filter: new IntervalStatisticsFilter(StartSimulationTimeSeconds: 400)).AllIntervals.Count);
}

static void TestV1V2Comparison()
{
    var route = CreateThreeStationRoute();
    var (vehicles, services, stops) = CreatePlanningCatalogs();
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition([], [new ManualTimetableRow(
            TimeSpan.Zero,
            TrainDirection.Outbound,
            "LOCAL",
            "EMU-6",
            "ALL_STOP",
            vehicleId: "CMP-01",
            serviceRunId: "CMP-OUT")], DispatchPlanningMode.ManualTimetable),
        vehicles,
        services,
        stops);
    var world = new SimulationWorld(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        trainCount: 1,
        movingBlockMode: MovingBlockMode.Independent,
        dispatchPlan: dispatch,
        vehicleTypes: vehicles,
        infrastructure: InfrastructureGraph.CreateLegacy(route));
    world.AdvanceTo(600);

    var result = V1V2Comparison.Analyze(route, dispatch, vehicles, stops, CreateParameters(), world.Events);
    Equal(3, result.Stations.Count);
    var intermediate = result.Stations.Single(item => item.StationId == "O02");
    True(intermediate.TheoreticalArrivalTimeSeconds is not null && intermediate.ActualArrivalTimeSeconds is not null,
        "同條件比較必須同時保留中間站 V1 理論與 V2 實際到站時間。 ");
    True(intermediate.DepartureDifferenceSeconds is not null && intermediate.DepartureDifferencePercent is not null,
        "同條件比較必須計算出站秒差與百分比。 ");
    True(V1V2Comparison.BuildCsv(result).Contains("V2 實際出站", StringComparison.Ordinal),
        "比較 CSV 必須包含理論／實際逐欄欄位。 ");
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
    var stored = restored.Infrastructure!.SpatialReferencePoints!.Single(item => item.ReferencePointId == "REF-P02-STATION");
    Equal("REF-P02-STATION", stored.ReferencePointId);
    Equal(SpatialReferencePointKind.IntermediateStation, stored.Kind);
    NearlyEqual(1.5, stored.StationForwardGradeInPermille);
    NearlyEqual(42, stored.StationForwardDwellSeconds);
    NearlyEqual(-2, stored.StationReverseGradeOutPermille);
    NearlyEqual(65, stored.StationReverseEarlierCruiseSpeedMetersPerSecond * 3.6);
}

static void TestPhysicalStationSpatialClassification()
{
    var route = CreateThreeStationRoute();
    var legacy = InfrastructureGraph.CreateLegacy(route);
    Equal(route.Stations.Count, legacy.SpatialReferencePoints.Count);
    Equal(route.Stations.Count, legacy.SpatialReferencePoints.Select(item => item.StationId)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    True(route.Stations.All(station => legacy.SpatialReferencePoints.Any(point =>
            point.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase))),
        "每一實體路線站必須取得恰好一個空間分類。 ");

    var virtualPoint = new SpatialReferencePointDefinition(
        "INVALID-TAIL", "TAIL:O03", "不應列為車站的尾軌節點", SpatialReferencePointKind.AfterStationTurnback);
    Throws<SimulationValidationException>(
        () => _ = new InfrastructureGraph(route, legacy.StationYards, legacy.TrackSegments, legacy.Paths,
            legacy.TurnbackPlans, [virtualPoint]),
        "空間參考點");
}

static void TestSpatialReferencePointTemplatePersistence()
{
    var source = CreateProjectDocument();
    var route = RouteFactory.FromSegmentDistances(source.RouteId, source.RouteName,
        source.Stations.Select(station => new StationInput(station.StationId, station.StationName,
            station.DistanceFromPreviousMeters, station.DwellTimeSeconds)), source.Train.DefaultDwellTimeSeconds);
    var legacy = InfrastructureGraph.CreateLegacy(route);
    var templates = SpatialReferencePointTemplateDefinition.CreateDefaults().ToList();
    var templateIndex = templates.FindIndex(item => item.Kind == SpatialReferencePointKind.BeforeStationTurnback);
    templates[templateIndex] = new SpatialReferencePointTemplateDefinition(
        SpatialReferencePointKind.BeforeStationTurnback,
        alternateBerthing: true,
        distanceFromStopToCrossoverMeters: 260,
        crossoverLengthMeters: 180,
        turnbackDwellSeconds: 42,
        switchSpeedLimitMetersPerSecond: 32 / 3.6,
        mainlineApproachCruiseSpeedMetersPerSecond: 70 / 3.6,
        mainlineSafetyFactor: 1.8);
    var originalStation = new SpatialReferencePointDefinition(
        "REF-P02", "P02", "已建立站場", SpatialReferencePointKind.IntermediateStation,
        stationForwardDwellSeconds: 47);
    var graph = new InfrastructureGraph(route, legacy.StationYards, legacy.TrackSegments, legacy.Paths,
        legacy.TurnbackPlans, [originalStation], spatialReferencePointTemplates: templates);
    var document = source with { Infrastructure = ProjectInfrastructureFromGraph(graph) };

    var restored = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    var restoredGraph = ToInfrastructureGraph(restored.Infrastructure!, route);
    Equal(5, restoredGraph.SpatialReferencePointTemplates.Count);
    var restoredTemplate = restoredGraph.GetSpatialReferencePointTemplate(SpatialReferencePointKind.BeforeStationTurnback);
    True(restoredTemplate.AlternateBerthing, "站前折返範本的交替停靠設定必須保存。");
    NearlyEqual(260, restoredTemplate.DistanceFromStopToCrossoverMeters);
    NearlyEqual(42, restoredTemplate.TurnbackDwellSeconds);
    var newStation = restoredTemplate.Create("REF-P03", "P03", "由範本建立");
    NearlyEqual(260, newStation.DistanceFromStopToCrossoverMeters);
    NearlyEqual(47, restoredGraph.FindSpatialReferencePoint("P02")!.StationForwardDwellSeconds);
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

static void TestAfterStationTailTrackVirtualNodeTurnback()
{
    var route = RouteFactory.FromSegmentDistances(
        "RT",
        "站後折返尾軌虛擬節點測試線",
        [
            new StationInput("RT01", "北端站", 0, 0),
            new StationInput("RT02", "南端站", 1000, 0)
        ],
        0);
    var tailNodeId = "TAIL:RT02-REAR";
    var platforms = new[]
    {
        new PlatformDefinition("RT01:DOWN", "RT01", "北端下行月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN"]),
        new PlatformDefinition("RT01:UP", "RT01", "北端上行月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP"]),
        new PlatformDefinition("RT02:DOWN", "RT02", "南端下行月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN"]),
        new PlatformDefinition("RT02:UP", "RT02", "南端上行月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP"])
    };
    var tracks = new[]
    {
        new TrackSegmentDefinition("DOWN", "RT01", "RT02", 0, 1000, TrackDirection.Outbound, TrackKind.Mainline, 1000, 22.222, ["TRACK:DOWN"]),
        new TrackSegmentDefinition("UP", "RT02", "RT01", 1000, 0, TrackDirection.Inbound, TrackKind.Mainline, 1000, 22.222, ["TRACK:UP"]),
        new TrackSegmentDefinition("RT02-TAIL-DOWN", "RT02", tailNodeId, 1000, 1160, TrackDirection.Outbound, TrackKind.TailTrack, 160, 25 / 3.6, ["TAIL:RT02"]),
        new TrackSegmentDefinition("RT02-TAIL-UP", tailNodeId, "RT02", 1160, 1000, TrackDirection.Inbound, TrackKind.TailTrack, 160, 25 / 3.6, ["TAIL:RT02"])
    };
    var paths = new[]
    {
        new RoutePathDefinition("RT-D", "RT01:DOWN", "RT02:DOWN", TrackDirection.Outbound, ["DOWN"], ["PATH:RT:D"]),
        new RoutePathDefinition("RT-U", "RT02:UP", "RT01:UP", TrackDirection.Inbound, ["UP"], ["PATH:RT:U"])
    };
    var turnback = new TurnbackPlanDefinition(
        "RT02-REAR", "南端站後尾軌折返", "RT02", TurnbackKind.AfterStation,
        "RT02:DOWN", "RT02:UP", ["RT02-TAIL-DOWN", "RT02-TAIL-UP"], ["TURNBACK:RT02"], 0);
    var yards = new[]
    {
        new StationYardDefinition("RT01", "北端站場", platforms.Where(item => item.StationId == "RT01"), ["DOWN", "UP"], ["RT-D", "RT-U"]),
        new StationYardDefinition("RT02", "南端站場", platforms.Where(item => item.StationId == "RT02"),
            ["DOWN", "UP", "RT02-TAIL-DOWN", "RT02-TAIL-UP"], ["RT-D", "RT-U"], [turnback])
    };
    var point = new SpatialReferencePointDefinition(
        "RT02-REAR", "RT02", "南端站後折返", SpatialReferencePointKind.AfterStationTurnback,
        distanceFromStopToCrossoverMeters: 0, crossoverLengthMeters: 0,
        distanceFromCrossoverToTurnbackStopMeters: 160, turnbackDwellSeconds: 3,
        switchSpeedLimitMetersPerSecond: 25 / 3.6);
    var infrastructure = new InfrastructureGraph(route, yards, tracks, paths, [turnback], [point]);
    var layout = infrastructure.FindAfterStationTailTrackLayout(turnback);
    True(layout is not null, "站後折返的成對尾軌必須建立虛擬節點配置。 ");
    Equal(tailNodeId, layout!.VirtualNodeId);
    NearlyEqual(1160, layout.VirtualNodePositionMeters);

    var world = new SimulationWorld(
        route,
        CreateParameters(),
        OperationalParameters.CreateDefault(),
        trainCount: 1,
        movingBlockMode: MovingBlockMode.Independent,
        infrastructure: infrastructure);
    while (!world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged
        && item.Direction == TrainDirection.Inbound)
        && world.CurrentTimeSeconds < 500)
    {
        world.Tick();
    }

    var tailArrival = world.Events.Single(item => item.EventType == SimulationEventType.TailTrackReached);
    Equal("RT02-TAIL-DOWN", tailArrival.TrackId);
    Equal(tailNodeId, tailArrival.ResourceId!);
    NearlyEqual(1160, tailArrival.PositionMeters, 1e-7);
    var returnStarted = world.Events.Single(item => item.EventType == SimulationEventType.TailTrackReturnStarted);
    Equal(TrainDirection.Inbound, returnStarted.Direction);
    Equal("RT02-TAIL-UP", returnStarted.TrackId);
    True(returnStarted.SimulationTimeSeconds >= tailArrival.SimulationTimeSeconds + 3 - 0.11,
        "列車抵達尾軌虛擬節點後必須先完成設定的折返停等。 ");
    True(world.Trajectory.Any(sample => sample.TrackId == "RT02-TAIL-DOWN"
        && sample.PositionMeters > 1000 + 1e-7),
        "列車必須實際駛入端點站外的下行尾軌。 ");
    True(world.Trajectory.Any(sample => sample.TrackId == "RT02-TAIL-UP"
        && sample.PositionMeters > 1000 + 1e-7),
        "列車必須改走上行尾軌返回端點站。 ");
    var outboundTailSamples = world.Trajectory
        .Where(sample => sample.TrackId == "RT02-TAIL-DOWN")
        .OrderBy(sample => sample.SimulationTimeSeconds)
        .Select(sample => sample.PositionMeters)
        .ToArray();
    var inboundTailSamples = world.Trajectory
        .Where(sample => sample.TrackId == "RT02-TAIL-UP")
        .OrderBy(sample => sample.SimulationTimeSeconds)
        .Select(sample => sample.PositionMeters)
        .ToArray();
    True(outboundTailSamples.Length > 2 && inboundTailSamples.Length > 2,
        "站後折返尾軌必須保留固定子步進的連續軌跡樣本。 ");
    True(outboundTailSamples.Zip(outboundTailSamples.Skip(1), (previous, next) => next + 1e-7 >= previous).All(value => value),
        "下行尾軌的里程樣本必須連續朝虛擬節點前進。 ");
    True(inboundTailSamples.Zip(inboundTailSamples.Skip(1), (previous, next) => next <= previous + 1e-7).All(value => value),
        "上行尾軌的里程樣本必須連續返回端點站。 ");
    True(world.Events.Any(item => item.EventType == SimulationEventType.DirectionChanged
        && item.Direction == TrainDirection.Inbound),
        "由尾軌返回端點站後必須接續上行新車次。 ");
    var reverseArrival = world.Events.Single(item => item.EventType == SimulationEventType.Arrival
        && item.Direction == TrainDirection.Inbound && item.PositionMeters >= 999.5);
    True(reverseArrival.PlatformId == "RT02:UP", "尾軌換向返回後必須停靠反方向月台。 ");
    var reverseDeparture = world.Events.First(item => item.EventType == SimulationEventType.Departure
        && item.Direction == TrainDirection.Inbound && item.SimulationTimeSeconds >= reverseArrival.SimulationTimeSeconds);
    True(reverseDeparture.PlatformId == "RT02:UP", "尾軌返回後必須由反方向月台發車。 ");
    True(reverseDeparture.SimulationTimeSeconds >= reverseArrival.SimulationTimeSeconds,
        "反方向月台的發車事件不可早於尾軌返回抵達事件。 ");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision),
        "站後折返尾軌資源預約期間不得產生碰撞。 ");
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
    var roundTripJson = SimulationProjectFormat.Serialize(document);
    var roundTrip = SimulationProjectFormat.Deserialize(roundTripJson);
    True(!roundTripJson.Contains("servicePatterns", StringComparison.OrdinalIgnoreCase)
        && !roundTripJson.Contains("serviceRuns", StringComparison.OrdinalIgnoreCase),
        "範例往返後不得產生舊執行資料源。");
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
            instruction.Action switch
            {
                StopPatternAction.Stop => StationServiceMode.Stop,
                StopPatternAction.Pass => StationServiceMode.Pass,
                StopPatternAction.Turnback => StationServiceMode.Turnback,
                _ => throw new InvalidOperationException("停站模式動作無效。")
            },
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

static void TestFourTrackExpressPassingScenario()
{
    var repositoryRoot = FindRepositoryRoot();
    var samplePath = Path.Combine(repositoryRoot, "samples", "V3.4.0-雙島四股快速車越行驗證.mrtsim.json");
    True(File.Exists(samplePath), $"找不到雙島四股越行驗證範例：{samplePath}");

    var document = SimulationProjectFormat.Deserialize(File.ReadAllText(samplePath));
    Equal(SimulationProjectFormat.CurrentSchemaVersion, document.SchemaVersion);
    Equal(3, document.Stations.Length);
    Equal(2, document.ServiceTypes!.Length);
    Equal(2, document.StopPatterns!.Length);
    var roundTrip = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    Equal(6, roundTrip.Infrastructure!.TrackSegments.Length);
    Equal(2, roundTrip.Dispatch!.ManualTimetableRows!.Length);
    var overtakeYard = document.Infrastructure!.StationYards.Single(item => item.StationId == "OV02");
    Equal(4, overtakeYard.Platforms!.Length);
    True(overtakeYard.TrackSegmentIds!.Contains("DOWN-MAIN"), "越行站的站間方向必須維持單一共線正線。");
    var facility = document.Infrastructure.StationOvertakeFacilities!.Single();
    Equal("DOWN-MAIN", facility.MainlineTrackSegmentId);
    Equal("OV02-D-LOCAL", facility.LocalTrackSegmentId);
    Equal("OV02-D-EXPRESS", facility.ExpressTrackSegmentId);
    Equal(facility.FacilityId, roundTrip.Infrastructure!.StationOvertakeFacilities!.Single().FacilityId);

    var express = document.ServiceTypes.Single(item => item.Id == "EXPRESS");
    True(express.CanRequestOvertake && express.Priority > document.ServiceTypes.Single(item => item.Id == "LOCAL").Priority,
        "快速車範例必須保留較高優先序與追越請求設定。");
    True(document.StopPatterns.Single(item => item.Id == "EXPRESS_PASS").Instructions
        .Single(item => item.StationId == "OV02").Action == StopPatternAction.Pass,
        "快速車必須跨越雙島四股越行站。 ");

    var (world, _, _) = CreateWorldFromProjectDocument(document);
    world.AdvanceTo(1000);

    var eventTrace = string.Join(" | ", world.Events
        .Where(item => item.ServiceRunId is "RUN-LOCAL-001" or "RUN-EXPRESS-001")
        .Select(item => $"{item.ServiceRunId}:{item.EventType}@{item.SimulationTimeSeconds:0.0}:{item.TrackId}"));
    var localDwell = world.Events.SingleOrDefault(item => item.EventType == SimulationEventType.DwellStarted
        && item.ServiceRunId == "RUN-LOCAL-001" && Math.Abs(item.PositionMeters - 1600) <= 0.5)
        ?? throw new InvalidOperationException($"普通車未在 OV02 待避：{eventTrace}");
    var expressPass = world.Events.SingleOrDefault(item => item.EventType == SimulationEventType.StationPassed
        && item.ServiceRunId == "RUN-EXPRESS-001" && Math.Abs(item.PositionMeters - 1600) <= 0.5)
        ?? throw new InvalidOperationException($"快速車未通過 OV02：{eventTrace}");
    var localDeparture = world.Events.SingleOrDefault(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "RUN-LOCAL-001" && Math.Abs(item.PositionMeters - 1600) <= 0.5)
        ?? throw new InvalidOperationException($"普通車未離開 OV02：{eventTrace}");
    True(localDwell.SimulationTimeSeconds < expressPass.SimulationTimeSeconds
        && expressPass.SimulationTimeSeconds < localDeparture.SimulationTimeSeconds,
        $"快速車必須在普通車停靠 OV02 期間跨越該站。普通車停靠={localDwell.SimulationTimeSeconds:0.0}、快速車通過={expressPass.SimulationTimeSeconds:0.0}、普通車發車={localDeparture.SimulationTimeSeconds:0.0}；{eventTrace}");
    var requested = world.Events.Single(item => item.EventType == SimulationEventType.OvertakeRequested
        && item.ServiceRunId == "RUN-EXPRESS-001");
    var completed = world.Events.Single(item => item.EventType == SimulationEventType.OvertakeCompleted
        && item.ServiceRunId == "RUN-EXPRESS-001");
    Equal("EMU-LOCAL-01", requested.RelatedVehicleId!);
    Equal("EMU-LOCAL-01", completed.RelatedVehicleId!);
    Equal("OV02-D-EXPRESS", requested.TrackId);
    Equal("DOWN-MAIN", completed.TrackId);
    True(localDwell.SimulationTimeSeconds < requested.SimulationTimeSeconds
        && requested.SimulationTimeSeconds < completed.SimulationTimeSeconds
        && completed.SimulationTimeSeconds <= expressPass.SimulationTimeSeconds
        && completed.SimulationTimeSeconds < localDeparture.SimulationTimeSeconds,
        "越行事件必須在普通車待避期間完成，並在快速車通過站場後才允許普通車發車。 ");
    True(world.Events.Any(item => item.EventType == SimulationEventType.WaitingForResource
        && item.ServiceRunId == "RUN-LOCAL-001"
        && item.RelatedVehicleId == "EMU-EXPRESS-01"
        && item.ResourceId == facility.FacilityId),
        $"普通車的原定停站時間到達時，必須因快速車正接近越行站而繼續待避。普通車停站={localDwell.SimulationTimeSeconds:0.0}、快速車通過={expressPass.SimulationTimeSeconds:0.0}、普通車發車={localDeparture.SimulationTimeSeconds:0.0}。 ");

    var pairedSamples = world.Trajectory
        .Where(item => item.ServiceRunId is "RUN-LOCAL-001" or "RUN-EXPRESS-001")
        .GroupBy(item => item.SimulationTimeSeconds)
        .Select(group => new
        {
            Local = group.SingleOrDefault(item => item.ServiceRunId == "RUN-LOCAL-001"),
            Express = group.SingleOrDefault(item => item.ServiceRunId == "RUN-EXPRESS-001")
        })
        .Where(item => item.Local is not null && item.Express is not null)
        .ToArray();
    True(pairedSamples.Any(item => item.Express!.PositionMeters + 20 < item.Local!.PositionMeters),
        "快速車發車後必須先位於普通車後方。");
    var localTracks = world.Trajectory.Where(item => item.ServiceRunId == "RUN-LOCAL-001").Select(item => item.TrackId).ToHashSet();
    var expressTracks = world.Trajectory.Where(item => item.ServiceRunId == "RUN-EXPRESS-001").Select(item => item.TrackId).ToHashSet();
    True(localTracks.Contains("DOWN-MAIN") && localTracks.Contains("OV02-D-LOCAL"),
        "普通車應先使用共線正線，抵達越行站後轉入站內待避線。 ");
    True(expressTracks.Contains("DOWN-MAIN") && expressTracks.Contains("OV02-D-EXPRESS"),
        "快速車只在站內分歧至通過線，完成後必須匯回共線正線。 ");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision),
        "站內越行示範不得產生碰撞。");
}

static void TestMultipleStationOvertakeCandidates()
{
    var repositoryRoot = FindRepositoryRoot();
    var samplePath = Path.Combine(repositoryRoot, "samples", "V3.4.0-雙島四股快速車越行驗證.mrtsim.json");
    var source = SimulationProjectFormat.Deserialize(File.ReadAllText(samplePath));
    var primary = source.Infrastructure!.StationOvertakeFacilities!.Single();
    var nearerAlternative = primary with
    {
        FacilityId = "OV02-D-OVERTAKE-ALT",
        EntryPositionMeters = 1320,
        ResourceIds = ["OV02:D:ALT:ENTRY", "OV02:D:ALT:THROAT", "OV02:D:ALT:EXIT"]
    };
    var document = source with
    {
        Infrastructure = source.Infrastructure with
        {
            StationOvertakeFacilities = [primary, nearerAlternative]
        }
    };
    var (world, _, _) = CreateWorldFromProjectDocument(document);
    world.AdvanceTo(1000);

    Equal(2, world.Infrastructure.FindStationOvertakeFacilities("OV02", TrainDirection.Outbound).Count);
    var requested = world.Events.Single(item => item.EventType == SimulationEventType.OvertakeRequested
        && item.ServiceRunId == "RUN-EXPRESS-001");
    Equal("OV02-D-OVERTAKE-ALT", requested.ResourceId!);
    True(world.Events.Any(item => item.EventType == SimulationEventType.RouteReserved
        && item.ResourceId == "OV02-D-OVERTAKE-ALT"
        && item.ResourceIds is not null
        && item.ResourceIds.Contains("OV02:D:ALT:THROAT", StringComparer.OrdinalIgnoreCase)),
        "選定的越行候選必須保留其自身衝突資源，而非誤用第一筆設施。 ");
}

static void TestSimultaneousBidirectionalOvertakes()
{
    var repositoryRoot = FindRepositoryRoot();
    var samplePath = Path.Combine(repositoryRoot, "samples", "V3.4.0-雙島四股快速車越行驗證.mrtsim.json");
    var document = CreateBidirectionalOvertakeDocument(
        SimulationProjectFormat.Deserialize(File.ReadAllText(samplePath)));
    var (world, _, _) = CreateWorldFromProjectDocument(document);
    world.AdvanceTo(1000);

    var requests = world.Events
        .Where(item => item.EventType == SimulationEventType.OvertakeRequested)
        .OrderBy(item => item.SimulationTimeSeconds)
        .ToArray();
    Equal(2, requests.Length);
    var outbound = requests.Single(item => item.Direction == TrainDirection.Outbound);
    var inbound = requests.Single(item => item.Direction == TrainDirection.Inbound);
    Equal("OV02-D-OVERTAKE", outbound.ResourceId!);
    Equal("OV02-U-OVERTAKE", inbound.ResourceId!);
    var completions = world.Events
        .Where(item => item.EventType == SimulationEventType.OvertakeCompleted)
        .ToArray();
    var outboundCompletion = completions.Single(item => item.Direction == TrainDirection.Outbound);
    var inboundCompletion = completions.Single(item => item.Direction == TrainDirection.Inbound);
    True(Math.Max(outbound.SimulationTimeSeconds, inbound.SimulationTimeSeconds)
            < Math.Min(outboundCompletion.SimulationTimeSeconds, inboundCompletion.SimulationTimeSeconds),
        "上下行越行的資源保留期間必須重疊，證明兩個方向可同時進行而非互相排隊。 ");
    var reservedResources = world.Events
        .Where(item => item.EventType == SimulationEventType.RouteReserved
            && item.ResourceId is "OV02-D-OVERTAKE" or "OV02-U-OVERTAKE")
        .SelectMany(item => item.ResourceIds ?? [])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    True(reservedResources.Contains("OV02:D:THROAT") && reservedResources.Contains("OV02:U:THROAT"),
        "雙向越行必須各自保留方向專屬衝突區。 ");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision),
        "反向同時越行不得產生碰撞。 ");
}

static SimulationProjectDocument CreateBidirectionalOvertakeDocument(SimulationProjectDocument source)
{
    var infrastructure = source.Infrastructure!;
    var inboundEndpointPlatforms = new[]
    {
        new ProjectPlatform("OV01-U-LOCAL", "OV01", "北端上行普通車月台", TrackDirection.Inbound, 220,
            110, true, ["EMU-LOCAL"], ["LOCAL"], ["UP-MAIN"]),
        new ProjectPlatform("OV01-U-EXPRESS", "OV01", "北端上行快速車月台", TrackDirection.Inbound, 220,
            110, true, ["EMU-EXPRESS"], ["EXPRESS"], ["UP-MAIN"]),
        new ProjectPlatform("OV03-U-LOCAL", "OV03", "南端上行普通車月台", TrackDirection.Inbound, 220,
            110, true, ["EMU-LOCAL"], ["LOCAL"], ["UP-MAIN"]),
        new ProjectPlatform("OV03-U-EXPRESS", "OV03", "南端上行快速車月台", TrackDirection.Inbound, 220,
            110, true, ["EMU-EXPRESS"], ["EXPRESS"], ["UP-MAIN"])
    };
    var inboundPaths = new[]
    {
        new ProjectRoutePath("PATH-U-LOCAL-01", "OV03-U-LOCAL", "OV02-U-LOCAL", TrackDirection.Inbound,
            ["UP-MAIN"], ["PATH:OV03:OV02:LOCAL"]),
        new ProjectRoutePath("PATH-U-LOCAL-02", "OV02-U-LOCAL", "OV01-U-LOCAL", TrackDirection.Inbound,
            ["UP-MAIN"], ["PATH:OV02:OV01:LOCAL"]),
        new ProjectRoutePath("PATH-U-EXPRESS-01", "OV03-U-EXPRESS", "OV02-U-EXPRESS", TrackDirection.Inbound,
            ["UP-MAIN"], ["PATH:OV03:OV02:EXPRESS"]),
        new ProjectRoutePath("PATH-U-EXPRESS-02", "OV02-U-EXPRESS", "OV01-U-EXPRESS", TrackDirection.Inbound,
            ["UP-MAIN"], ["PATH:OV02:OV01:EXPRESS"])
    };
    var yards = infrastructure.StationYards!.Select(yard => yard.StationId switch
    {
        "OV01" => yard with
        {
            Platforms = yard.Platforms!.Concat(inboundEndpointPlatforms.Where(item => item.StationId == "OV01")).ToArray(),
            RoutePathIds = yard.RoutePathIds!.Concat(inboundPaths.Where(item => item.ToPlatformId.StartsWith("OV01", StringComparison.Ordinal))
                .Select(item => item.PathId)).ToArray()
        },
        "OV03" => yard with
        {
            Platforms = yard.Platforms!.Concat(inboundEndpointPlatforms.Where(item => item.StationId == "OV03")).ToArray(),
            RoutePathIds = yard.RoutePathIds!.Concat(inboundPaths.Where(item => item.FromPlatformId.StartsWith("OV03", StringComparison.Ordinal))
                .Select(item => item.PathId)).ToArray()
        },
        "OV02" => yard with
        {
            RoutePathIds = yard.RoutePathIds!.Concat(inboundPaths.Select(item => item.PathId)).ToArray()
        },
        _ => yard
    }).ToArray();
    var inboundFacility = new ProjectStationOvertakeFacility(
        "OV02-U-OVERTAKE", "OV02", TrackDirection.Inbound, "UP-MAIN", "OV02-U-LOCAL", "OV02-U-EXPRESS",
        "OV02-U-LOCAL", "OV02-U-EXPRESS", 1840, ["OV02:U:ENTRY", "OV02:U:THROAT", "OV02:U:EXIT"]);
    var services = source.ServiceTypes!.Select(service => service with
    {
        PreferredPlatformIds = service.PreferredPlatformIds!.Concat(service.Id == "LOCAL"
            ? ["OV01-U-LOCAL", "OV02-U-LOCAL", "OV03-U-LOCAL"]
            : ["OV01-U-EXPRESS", "OV02-U-EXPRESS", "OV03-U-EXPRESS"])
            .ToArray()
    }).ToArray();
    var inboundRuns = new[]
    {
        new ProjectManualTimetableRow(21600, TrainDirection.Inbound, "LOCAL", "EMU-LOCAL", "ALL_STOP",
            "OV03-U-LOCAL", "EMU-LOCAL-02", "RUN-LOCAL-002"),
        new ProjectManualTimetableRow(21655, TrainDirection.Inbound, "EXPRESS", "EMU-EXPRESS", "EXPRESS_PASS",
            "OV03-U-EXPRESS", "EMU-EXPRESS-02", "RUN-EXPRESS-002")
    };
    return source with
    {
        Simulation = source.Simulation with { TrainCount = 4 },
        ServiceTypes = services,
        Dispatch = source.Dispatch! with { ManualTimetableRows = source.Dispatch.ManualTimetableRows!.Concat(inboundRuns).ToArray() },
        Infrastructure = infrastructure with
        {
            StationYards = yards,
            Paths = infrastructure.Paths!.Concat(inboundPaths).ToArray(),
            StationOvertakeFacilities = infrastructure.StationOvertakeFacilities!.Append(inboundFacility).ToArray()
        }
    };
}

static (SimulationWorld World, Route Route, ResolvedDispatchPlan Dispatch) CreateWorldFromProjectDocument(
    SimulationProjectDocument document)
{
    var route = RouteFactory.FromSegmentDistances(
        document.RouteId,
        document.RouteName,
        document.Stations.Select(item => new StationInput(
            item.StationId,
            item.StationName,
            item.DistanceFromPreviousMeters,
            item.DwellTimeSeconds)),
        document.Train.DefaultDwellTimeSeconds);
    var vehicleTypes = document.VehicleTypes!.Select(ToVehicleTypeDefinition).ToArray();
    var serviceTypes = document.ServiceTypes!.Select(item => new ServiceTypeDefinition(
        item.Id,
        item.DisplayName,
        item.ColorHex,
        item.RunPrefix,
        item.DefaultStopPatternId,
        item.DefaultVehicleTypeId,
        item.Priority,
        item.CanRequestOvertake,
        item.PreferredPlatformIds)).ToArray();
    var stopPatterns = document.StopPatterns!.Select(item => new StopPatternDefinition(
        item.Id,
        item.DisplayName,
        item.Instructions.Select(instruction => new StopPatternInstruction(
            instruction.StationId,
            instruction.Action,
            instruction.DwellTimeSeconds,
            instruction.PassingSpeedLimitMetersPerSecond)))).ToArray();
    var dispatch = DispatchPlanExpander.Expand(
        ToDispatchPlanDefinition(document.Dispatch!),
        vehicleTypes,
        serviceTypes,
        stopPatterns);
    var servicePatterns = document.StopPatterns!.Select(item => new ServicePattern(
        item.Id,
        item.DisplayName,
        item.Instructions.Select(instruction => new StationServiceInstruction(
            instruction.StationId,
            instruction.Action switch
            {
                StopPatternAction.Stop => StationServiceMode.Stop,
                StopPatternAction.Pass => StationServiceMode.Pass,
                StopPatternAction.Turnback => StationServiceMode.Turnback,
                _ => throw new InvalidOperationException("停站模式動作無效。")
            },
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
        ToInfrastructureGraph(document.Infrastructure!, route),
        serviceTypes: serviceTypes);
    world.SetBrakingEstimationMode(document.Simulation.BrakingEstimationMode);
    return (world, route, dispatch);
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
    item.CoastingDecelerationMetersPerSecondSquared,
    item.DefaultStopPatternId);

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
        point.StationReverseSafetyFactor)),
    (item.StationOvertakeFacilities ?? []).Select(facility => new StationOvertakeFacilityDefinition(
        facility.FacilityId,
        facility.StationId,
        facility.Direction,
        facility.MainlineTrackSegmentId,
        facility.LocalPlatformId,
        facility.ExpressPlatformId,
        facility.LocalTrackSegmentId,
        facility.ExpressTrackSegmentId,
        facility.EntryPositionMeters,
        facility.ResourceIds)),
    (item.SpatialReferencePointTemplates ?? []).Select(template => new SpatialReferencePointTemplateDefinition(
        template.Kind,
        template.AlternateBerthing,
        template.MainlineGradePermille,
        template.BranchlineGradePermille,
        template.DistanceFromStopToCrossoverMeters,
        template.CrossoverLengthMeters,
        template.DistanceFromCrossoverToTurnbackStopMeters,
        template.TurnbackDwellSeconds,
        template.SwitchSpeedLimitMetersPerSecond,
        template.MainlineApproachCruiseSpeedMetersPerSecond,
        template.BranchlineApproachCruiseSpeedMetersPerSecond,
        template.MainlineSafetyFactor,
        template.BranchlineSafetyFactor,
        template.MainlineTrafficRatio,
        template.StationForwardGradeInPermille,
        template.StationForwardGradeOutPermille,
        template.StationForwardDistanceToSignalMeters,
        template.StationForwardOverlapMeters,
        template.StationForwardDwellSeconds,
        template.StationForwardEarlierCruiseSpeedMetersPerSecond,
        template.StationForwardLaterCruiseSpeedMetersPerSecond,
        template.StationForwardSafetyFactor,
        template.StationReverseGradeInPermille,
        template.StationReverseGradeOutPermille,
        template.StationReverseDistanceToSignalMeters,
        template.StationReverseOverlapMeters,
        template.StationReverseDwellSeconds,
        template.StationReverseEarlierCruiseSpeedMetersPerSecond,
        template.StationReverseLaterCruiseSpeedMetersPerSecond,
        template.StationReverseSafetyFactor)));

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
        point.StationReverseLaterCruiseSpeedMetersPerSecond, point.StationReverseSafetyFactor)).ToArray(),
    graph.StationOvertakeFacilities.Select(facility => new ProjectStationOvertakeFacility(
        facility.FacilityId,
        facility.StationId,
        facility.Direction,
        facility.MainlineTrackSegmentId,
        facility.LocalPlatformId,
        facility.ExpressPlatformId,
        facility.LocalTrackSegmentId,
        facility.ExpressTrackSegmentId,
        facility.EntryPositionMeters,
        facility.ResourceIds.ToArray())).ToArray(),
    graph.SpatialReferencePointTemplates.Select(template => new ProjectSpatialReferencePointTemplate(
        template.Kind,
        template.AlternateBerthing,
        template.MainlineGradePermille,
        template.BranchlineGradePermille,
        template.DistanceFromStopToCrossoverMeters,
        template.CrossoverLengthMeters,
        template.DistanceFromCrossoverToTurnbackStopMeters,
        template.TurnbackDwellSeconds,
        template.SwitchSpeedLimitMetersPerSecond,
        template.MainlineApproachCruiseSpeedMetersPerSecond,
        template.BranchlineApproachCruiseSpeedMetersPerSecond,
        template.MainlineSafetyFactor,
        template.BranchlineSafetyFactor,
        template.MainlineTrafficRatio,
        template.StationForwardGradeInPermille,
        template.StationForwardGradeOutPermille,
        template.StationForwardDistanceToSignalMeters,
        template.StationForwardOverlapMeters,
        template.StationForwardDwellSeconds,
        template.StationForwardEarlierCruiseSpeedMetersPerSecond,
        template.StationForwardLaterCruiseSpeedMetersPerSecond,
        template.StationForwardSafetyFactor,
        template.StationReverseGradeInPermille,
        template.StationReverseGradeOutPermille,
        template.StationReverseDistanceToSignalMeters,
        template.StationReverseOverlapMeters,
        template.StationReverseDwellSeconds,
        template.StationReverseEarlierCruiseSpeedMetersPerSecond,
        template.StationReverseLaterCruiseSpeedMetersPerSecond,
        template.StationReverseSafetyFactor)).ToArray());

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
    Equal(1, restored.VehicleTypes!.Length);
    Equal(1, restored.ServiceTypes!.Length);
    Equal(2, restored.StopPatterns!.Length);
    var express = restored.StopPatterns.Single(item => item.Id == "EXPRESS");
    Equal(StopPatternAction.Pass, express.Instructions.Single().Action);
    NearlyEqual(45 / 3.6, express.Instructions.Single().PassingSpeedLimitMetersPerSecond!.Value);
    True(json.Contains($"\"schemaVersion\": {SimulationProjectFormat.CurrentSchemaVersion}", StringComparison.Ordinal), "存檔必須包含版本欄位。");
}

static void TestFixedTimetableArchiveRoundTrip()
{
    var source = CreateProjectDocument();
    var timetable = new[]
    {
        new OperationsTimetableEntry("EMU-001", "RUN-001", TrainDirection.Outbound, "LOCAL", "EXPRESS", "EMU-6",
            "P01", "起點", 0, null, 0, null, 0, null, 0, "已發車"),
        new OperationsTimetableEntry("EMU-001", "RUN-001", TrainDirection.Outbound, "LOCAL", "EXPRESS", "EMU-6",
            "P02", "中央", 1200, null, null, 72.4, 72.4, 0, 2.4, "已抵達"),
        new OperationsTimetableEntry("EMU-001", "RUN-001", TrainDirection.Outbound, "LOCAL", "EXPRESS", "EMU-6",
            "P03", "終點", 2000, null, null, 124.8, null, null, null, "退出營運")
    };

    var archive = FixedTimetableArchiveFormat.Create(source, timetable);
    var json = FixedTimetableArchiveFormat.Serialize(archive);
    True(FixedTimetableArchiveFormat.IsFixedTimetableArchive(json), "封存 JSON 必須可辨識為固定時刻表。");
    var restored = FixedTimetableArchiveFormat.Deserialize(json);

    Equal(FixedTimetableArchiveFormat.CurrentArchiveFormatVersion, restored.ArchiveFormatVersion);
    Equal(FixedTimetableArchiveFormat.ArchiveType, restored.ArchiveType);
    Equal("測試專案線", restored.Project.RouteName);
    Equal(3, restored.Entries.Length);
    NearlyEqual(72.4, restored.Entries.Single(item => item.StationId == "P02").ActualDepartureTimeSeconds!.Value, 1e-9);
    Equal("退出營運", restored.Entries.Single(item => item.StationId == "P03").Status);
}

static void TestFixedTimetableArchiveValidation()
{
    var archive = FixedTimetableArchiveFormat.Create(
        CreateProjectDocument(),
        [new OperationsTimetableEntry("EMU-001", "RUN-001", TrainDirection.Outbound, "LOCAL", "EXPRESS", "EMU-6",
            "P01", "起點", 0, null, 0, null, 0, null, 0, "已發車")]);
    var root = JsonNode.Parse(FixedTimetableArchiveFormat.Serialize(archive))!.AsObject();
    root["archiveFormatVersion"] = 999;
    Throws<SimulationValidationException>(() => FixedTimetableArchiveFormat.Deserialize(root.ToJsonString()),
        "不支援固定時刻表封存版本");

    root["archiveFormatVersion"] = FixedTimetableArchiveFormat.CurrentArchiveFormatVersion;
    root["entries"]!.AsArray()[0]!["stationId"] = "MISSING";
    Throws<SimulationValidationException>(() => FixedTimetableArchiveFormat.Deserialize(root.ToJsonString()),
        "參照不存在的車站");
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

    root["schemaVersion"] = SimulationProjectFormat.CurrentSchemaVersion;
    root["servicePatterns"] = new JsonArray();
    Throws<SimulationValidationException>(() => SimulationProjectFormat.Deserialize(root.ToJsonString()),
        "不支援舊執行資料來源");
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

static void TestFrontTurnbackAlternateBerthing()
{
    var route = RouteFactory.FromSegmentDistances(
        "FT",
        "站前交替月台折返測試線",
        [
            new StationInput("FT01", "北端站", 0, 0),
            new StationInput("FT02", "南端站", 1000, 0)
        ],
        0);
    var vehicle = new VehicleTypeDefinition("EMU-FT", "站前折返測試車", 140, 22.222, 1, 1, 1.3, 0.65, 0.45, 0.05);
    var service = new ServiceTypeDefinition("LOCAL", "普通車", "#4472C4", "FT", "ALL_STOP", "EMU-FT");
    var stops = new StopPatternDefinition("ALL_STOP", "所有車站停靠",
    [
        new StopPatternInstruction("FT01", StopPatternAction.Stop),
        new StopPatternInstruction("FT02", StopPatternAction.Stop)
    ]);
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition(
            [],
            [
                new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Inbound, "LOCAL", "EMU-FT", "ALL_STOP",
                    originPlatformId: "FT02:UP", vehicleId: "EMU-FT-A", serviceRunId: "FT-IN-A",
                    continueAfterTerminal: true, continuationServiceRunId: "FT-OUT-A"),
                new ManualTimetableRow(TimeSpan.FromSeconds(10), TrainDirection.Inbound, "LOCAL", "EMU-FT", "ALL_STOP",
                    originPlatformId: "FT02:UP", vehicleId: "EMU-FT-B", serviceRunId: "FT-IN-B",
                    continueAfterTerminal: true, continuationServiceRunId: "FT-OUT-B"),
                new ManualTimetableRow(TimeSpan.FromSeconds(20), TrainDirection.Inbound, "LOCAL", "EMU-FT", "ALL_STOP",
                    originPlatformId: "FT02:UP", vehicleId: "EMU-FT-C", serviceRunId: "FT-IN-C"),
                new ManualTimetableRow(TimeSpan.FromSeconds(200), TrainDirection.Outbound, "LOCAL", "EMU-FT", "ALL_STOP",
                    originPlatformId: "FT01:DOWN-A", serviceRunId: "FT-OUT-A"),
                new ManualTimetableRow(TimeSpan.FromSeconds(230), TrainDirection.Outbound, "LOCAL", "EMU-FT", "ALL_STOP",
                    originPlatformId: "FT01:DOWN-B", serviceRunId: "FT-OUT-B")
            ],
            DispatchPlanningMode.ManualTimetable),
        [vehicle],
        [service],
        [stops]);
    var infrastructure = CreateFrontTurnbackInfrastructure(route);
    var world = new SimulationWorld(
        route,
        new TrainParameters(22.222, 1, 1, 0, 1, 1),
        OperationalParameters.CreateDefault(),
        3,
        movingBlockMode: MovingBlockMode.Independent,
        servicePatterns:
        [
            new ServicePattern("ALL_STOP", "所有車站停靠",
            [
                new StationServiceInstruction("FT01", StationServiceMode.Stop),
                new StationServiceInstruction("FT02", StationServiceMode.Stop)
            ])
        ],
        dispatchPlan: dispatch,
        vehicleTypes: [vehicle],
        infrastructure: infrastructure);

    world.AdvanceTo(300);
    var inboundArrivals = world.Events
        .Where(item => item.EventType == SimulationEventType.Arrival
            && item.Direction == TrainDirection.Inbound
            && (item.VehicleId is "EMU-FT-A" or "EMU-FT-B")
            && item.PositionMeters <= 0.5)
        .OrderBy(item => item.SimulationTimeSeconds)
        .ToArray();
    Equal(2, inboundArrivals.Length);
    Equal("FT01:UP-A", inboundArrivals[0].PlatformId!);
    Equal("FT01:UP-B", inboundArrivals[1].PlatformId!);
    True(world.Events.Any(item => item.EventType == SimulationEventType.WaitingForResource
        && item.VehicleId == "EMU-FT-C"),
        "兩座交替月台都被預留時，後車必須留在起點等待資源。");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision),
        $"交替月台與資源保留期間不得發生碰撞：{string.Join(" | ", world.Events.Where(item => item.EventType == SimulationEventType.Collision).Select(item => $"{item.VehicleId}@{item.SimulationTimeSeconds:0.0}({item.TrackId})"))}");

    world.AdvanceTo(420);
    var thirdArrival = world.Events.SingleOrDefault(item => item.EventType == SimulationEventType.Arrival
        && item.VehicleId == "EMU-FT-C"
        && item.PositionMeters <= 0.5);
    True(thirdArrival is not null,
        $"第三車尚未抵達北端站；事件：{string.Join(" | ", world.Events.Where(item => item.VehicleId == "EMU-FT-C").Select(item => $"{item.EventType}@{item.SimulationTimeSeconds:0.0}:{item.ResourceId}"))}");
    Equal("FT01:UP-A", thirdArrival!.PlatformId!);
    var turnbacks = world.Events
        .Where(item => item.EventType == SimulationEventType.DirectionChanged
            && (item.ServiceRunId is "FT-OUT-A" or "FT-OUT-B"))
        .OrderBy(item => item.SimulationTimeSeconds)
        .ToArray();
    Equal(2, turnbacks.Length);
    var frontTurnbackTrajectory = world.Trajectory
        .Where(item => item.VehicleId == "EMU-FT-A"
            && item.TrackId.StartsWith("TURNBACK:FT01-FRONT:", StringComparison.Ordinal))
        .OrderBy(item => item.SimulationTimeSeconds)
        .ToArray();
    True(frontTurnbackTrajectory.Any(item => item.TrackId.EndsWith(":OUT", StringComparison.Ordinal)
        && item.Phase == OperationalPhase.SpatialTurnbackOutbound),
        "站前折返應把外出橫渡線行程記錄為獨立固定子步進軌跡。 ");
    True(frontTurnbackTrajectory.Any(item => item.TrackId.EndsWith(":RETURN", StringComparison.Ordinal)
        && item.Phase == OperationalPhase.SpatialTurnbackReturn),
        "站前折返應把返回月台行程記錄為獨立固定子步進軌跡。 ");
    True(frontTurnbackTrajectory.Any(item => item.PositionMeters < -0.5),
        "站前折返虛擬橫渡線必須有離開實體站的非零位置。 ");
    True(frontTurnbackTrajectory.Zip(frontTurnbackTrajectory.Skip(1), (previous, current) =>
            current.SimulationTimeSeconds - previous.SimulationTimeSeconds)
        .All(step => Math.Abs(step - 0.1) < 1e-6),
        "站前折返的外出、等待與返回軌跡必須維持 0.1 秒固定子步進。 ");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision),
        "等待解除與後續折返期間不得產生碰撞。");
}

static void TestCentralSidingAnchoredStationTurnback()
{
    var route = RouteFactory.FromSegmentDistances(
        "CS",
        "中央避車線實體錨定站折返測試線",
        [
            new StationInput("CS01", "北端站", 0, 0),
            new StationInput("CS02", "中央避車線錨定站", 800, 3),
            new StationInput("CS03", "南端站", 800, 0)
        ],
        0);
    var vehicle = new VehicleTypeDefinition("EMU-CS", "中央折返測試車", 140, 22.222, 1, 1, 1.3, 0.65, 0.45, 0.05);
    var service = new ServiceTypeDefinition("SHORT", "區間車", "#70AD47", "CS", "SHORT-TURN", "EMU-CS");
    var pattern = new StopPatternDefinition("SHORT-TURN", "中央避車線區間車",
    [
        new StopPatternInstruction("CS01", StopPatternAction.Stop),
        new StopPatternInstruction("CS02", StopPatternAction.Turnback, dwellTimeSeconds: 3),
        new StopPatternInstruction("CS03", StopPatternAction.Stop)
    ]);
    var dispatch = DispatchPlanExpander.Expand(
        new DispatchPlanDefinition(
            [],
            [
                new ManualTimetableRow(TimeSpan.Zero, TrainDirection.Inbound, "SHORT", "EMU-CS", "SHORT-TURN",
                    originPlatformId: "CS03:UP", vehicleId: "EMU-CS-01", serviceRunId: "CS-IN-01",
                    continuationServiceRunId: "CS-OUT-01"),
                new ManualTimetableRow(TimeSpan.FromSeconds(150), TrainDirection.Outbound, "SHORT", "EMU-CS", "SHORT-TURN",
                    originPlatformId: "CS02:DOWN", serviceRunId: "CS-OUT-01")
            ],
            DispatchPlanningMode.ManualTimetable),
        [vehicle],
        [service],
        [pattern]);
    var infrastructure = CreateCentralSidingVirtualInfrastructure(route);
    var world = new SimulationWorld(
        route,
        new TrainParameters(22.222, 1, 1, 0, 1, 1),
        OperationalParameters.CreateDefault(),
        1,
        movingBlockMode: MovingBlockMode.Independent,
        servicePatterns:
        [
            new ServicePattern("SHORT-TURN", "中央避車線區間車",
            [
                new StationServiceInstruction("CS01", StationServiceMode.Stop),
                new StationServiceInstruction("CS02", StationServiceMode.Turnback, DwellTimeSeconds: 3),
                new StationServiceInstruction("CS03", StationServiceMode.Stop)
            ])
        ],
        dispatchPlan: dispatch,
        vehicleTypes: [vehicle],
        infrastructure: infrastructure);

    world.AdvanceTo(300);
    var arrival = world.Events.Single(item => item.EventType == SimulationEventType.Arrival
        && item.ServiceRunId == "CS-IN-01"
        && item.PositionMeters >= 799.5 && item.PositionMeters <= 800.5);
    Equal("CS02:UP", arrival.PlatformId!);
    var turnaround = world.Events.Single(item => item.EventType == SimulationEventType.TurnaroundStarted
        && item.ServiceRunId == "CS-IN-01");
    True(turnaround.SimulationTimeSeconds >= arrival.SimulationTimeSeconds + 3 - 0.11,
        "中央避車線錨定站折返應先完成設定的停站秒數。");
    var changed = world.Events.Single(item => item.EventType == SimulationEventType.DirectionChanged
        && item.ServiceRunId == "CS-OUT-01");
    Equal("EMU-CS-01", changed.VehicleId);
    True(changed.SimulationTimeSeconds >= 150 - 0.11,
        "指定接續車次尚未到計畫時間前不得由實體錨定站發車。");
    True(world.Events.Any(item => item.EventType == SimulationEventType.Departure
        && item.ServiceRunId == "CS-OUT-01"),
        "中央避車線錨定站折返後必須產生反向接續發車事件。");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.ServiceEnded
        && item.ServiceRunId == "CS-IN-01"),
        "中央避車線折返不應把進站車次當成端點退出。");
    var centralTurnbackTrajectory = world.Trajectory
        .Where(item => item.VehicleId == "EMU-CS-01"
            && item.TrackId.StartsWith("TURNBACK:CS02-POCKET:", StringComparison.Ordinal))
        .ToArray();
    True(centralTurnbackTrajectory.Any(item => item.TrackId.EndsWith(":OUT", StringComparison.Ordinal)
        && item.Phase == OperationalPhase.SpatialTurnbackOutbound)
        && centralTurnbackTrajectory.Any(item => item.TrackId.EndsWith(":RETURN", StringComparison.Ordinal)
            && item.Phase == OperationalPhase.SpatialTurnbackReturn),
        "中央避車線折返應完整保留外出與返回兩段固定子步進軌跡。 ");
    True(centralTurnbackTrajectory.Any(item => item.PositionMeters < 799.5),
        "中央避車線虛擬停車區必須有偏離實體錨定站的非零位置。 ");
    True(!world.Events.Any(item => item.EventType == SimulationEventType.Collision),
        "中央避車線錨定站折返不得產生碰撞。");
}

static void TestTurnbackScenarioSampleProject()
{
    var repositoryRoot = FindRepositoryRoot();
    var samplePath = Path.Combine(repositoryRoot, "samples", "V3.3.0-端點站前與中間站中央避車線折返檢核.mrtsim.json");
    True(File.Exists(samplePath), $"找不到折返檢核範例：{samplePath}");

    var document = SimulationProjectFormat.Deserialize(File.ReadAllText(samplePath));
    Equal(SimulationProjectFormat.CurrentSchemaVersion, document.SchemaVersion);
    Equal(4, document.Stations.Length);
    Equal(2, document.ServiceTypes!.Length);
    Equal(2, document.StopPatterns!.Length);
    True(document.StopPatterns.Single(item => item.Id == "POCKET_SHORT_TURN").Instructions.Any(item =>
            item.StationId == "TB02" && item.Action == StopPatternAction.Turnback),
        "範例必須把中央避車線錨定實體站設定為 Turnback。");
    True(document.Infrastructure!.SpatialReferencePoints!.Any(item =>
            item.StationId == "TB01"
            && item.Kind == SpatialReferencePointKind.BeforeStationTurnback
            && item.AlternateBerthing),
        "範例必須保留端點站前交替月台設定。");
    True(document.Infrastructure.SpatialReferencePoints!.Any(item =>
            item.StationId == "TB02"
            && item.Kind == SpatialReferencePointKind.CentralSidingTurnback),
        "範例必須以 TB02 作為中央避車線實體錨定站。");
    True(document.Infrastructure.TurnbackPlans!.Any(item =>
            item.StationId == "TB02"
            && item.ResourceIds!.Contains("SIDING:TB02")),
        "範例必須鎖定中央避車線折返資源。");
    Equal(6, document.Dispatch!.ManualTimetableRows!.Length);

    var roundTrip = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(document));
    True(roundTrip.StopPatterns!.Single(item => item.Id == "POCKET_SHORT_TURN").Instructions.Any(item =>
            item.Action == StopPatternAction.Turnback),
        "範例往返後不得遺失中央避車線折返指令。");
}

static InfrastructureGraph CreateFrontTurnbackInfrastructure(Route route)
{
    var platforms = new[]
    {
        new PlatformDefinition("FT01:DOWN-A", "FT01", "北端下行 A 月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN-A"]),
        new PlatformDefinition("FT01:DOWN-B", "FT01", "北端下行 B 月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN-B"]),
        new PlatformDefinition("FT01:UP-A", "FT01", "北端上行 A 月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP-A"]),
        new PlatformDefinition("FT01:UP-B", "FT01", "北端上行 B 月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP-B"]),
        new PlatformDefinition("FT02:DOWN", "FT02", "南端下行月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN-A", "DOWN-B"]),
        new PlatformDefinition("FT02:UP", "FT02", "南端上行月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP-A", "UP-B"])
    };
    var tracks = new[]
    {
        new TrackSegmentDefinition("DOWN-A", "FT01", "FT02", 0, route.TotalLengthMeters,
            TrackDirection.Outbound, TrackKind.Mainline, route.TotalLengthMeters, 22.222, ["TRACK:DOWN-A"]),
        new TrackSegmentDefinition("DOWN-B", "FT01", "FT02", 0, route.TotalLengthMeters,
            TrackDirection.Outbound, TrackKind.Mainline, route.TotalLengthMeters, 22.222, ["TRACK:DOWN-B"]),
        new TrackSegmentDefinition("UP-A", "FT02", "FT01", route.TotalLengthMeters, 0,
            TrackDirection.Inbound, TrackKind.Mainline, route.TotalLengthMeters, 22.222, ["TRACK:UP-A"]),
        new TrackSegmentDefinition("UP-B", "FT02", "FT01", route.TotalLengthMeters, 0,
            TrackDirection.Inbound, TrackKind.Mainline, route.TotalLengthMeters, 22.222, ["TRACK:UP-B"])
    };
    var paths = new[]
    {
        new RoutePathDefinition("FT-D-A", "FT01:DOWN-A", "FT02:DOWN", TrackDirection.Outbound, ["DOWN-A"], ["PATH:FT:D:A"]),
        new RoutePathDefinition("FT-D-B", "FT01:DOWN-B", "FT02:DOWN", TrackDirection.Outbound, ["DOWN-B"], ["PATH:FT:D:B"]),
        new RoutePathDefinition("FT-U-A", "FT02:UP", "FT01:UP-A", TrackDirection.Inbound, ["UP-A"], ["PATH:FT:U:A"]),
        new RoutePathDefinition("FT-U-B", "FT02:UP", "FT01:UP-B", TrackDirection.Inbound, ["UP-B"], ["PATH:FT:U:B"])
    };
    var yards = new[]
    {
        new StationYardDefinition("FT01", "北端站場", platforms.Where(item => item.StationId == "FT01"),
            ["DOWN-A", "DOWN-B", "UP-A", "UP-B"], paths.Where(item => item.FromPlatformId.StartsWith("FT01", StringComparison.Ordinal)
                || item.ToPlatformId.StartsWith("FT01", StringComparison.Ordinal)).Select(item => item.PathId),
            platformAllocationStrategy: PlatformAllocationStrategy.RoundRobin),
        new StationYardDefinition("FT02", "南端站場", platforms.Where(item => item.StationId == "FT02"),
            ["DOWN-A", "DOWN-B", "UP-A", "UP-B"], paths.Where(item => item.FromPlatformId.StartsWith("FT02", StringComparison.Ordinal)
                || item.ToPlatformId.StartsWith("FT02", StringComparison.Ordinal)).Select(item => item.PathId))
    };
    var point = new SpatialReferencePointDefinition(
        "FT01-FRONT", "FT01", "北端站前折返", SpatialReferencePointKind.BeforeStationTurnback,
        alternateBerthing: true, distanceFromStopToCrossoverMeters: 30, crossoverLengthMeters: 20,
        turnbackDwellSeconds: 5, switchSpeedLimitMetersPerSecond: 20 / 3.6);
    return new InfrastructureGraph(route, yards, tracks, paths, spatialReferencePoints: [point]);
}

static InfrastructureGraph CreateCentralSidingVirtualInfrastructure(Route route)
{
    var platforms = new[]
    {
        new PlatformDefinition("CS02:DOWN", "CS02", "中央避車線下行月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN"]),
        new PlatformDefinition("CS02:UP", "CS02", "中央避車線上行月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP"]),
        new PlatformDefinition("CS03:DOWN", "CS03", "南端下行月台", TrackDirection.Outbound, 220, trackSegmentIds: ["DOWN"]),
        new PlatformDefinition("CS03:UP", "CS03", "南端上行月台", TrackDirection.Inbound, 220, trackSegmentIds: ["UP"])
    };
    var tracks = new[]
    {
        new TrackSegmentDefinition("DOWN", "CS01", "CS03", 0, route.TotalLengthMeters,
            TrackDirection.Outbound, TrackKind.Mainline, route.TotalLengthMeters, 22.222, ["TRACK:DOWN"]),
        new TrackSegmentDefinition("UP", "CS03", "CS01", route.TotalLengthMeters, 0,
            TrackDirection.Inbound, TrackKind.Mainline, route.TotalLengthMeters, 22.222, ["TRACK:UP"])
    };
    var paths = new[]
    {
        new RoutePathDefinition("CS-D-23", "CS02:DOWN", "CS03:DOWN", TrackDirection.Outbound, ["DOWN"], ["PATH:CS:D:23"]),
        new RoutePathDefinition("CS-U-32", "CS03:UP", "CS02:UP", TrackDirection.Inbound, ["UP"], ["PATH:CS:U:32"])
    };
    var turnback = new TurnbackPlanDefinition(
        "CS02-TURNBACK", "中央避車線折返資源", "CS02", TurnbackKind.BeforeStation,
        "CS02:UP", "CS02:DOWN", ["UP", "DOWN"], ["SIDING:CS02"], 0);
    var yards = new[]
    {
        new StationYardDefinition("CS01", "北端站場"),
        new StationYardDefinition("CS02", "中央避車線錨定站場", platforms.Where(item => item.StationId == "CS02"),
            ["DOWN", "UP"], paths.Select(item => item.PathId), [turnback]),
        new StationYardDefinition("CS03", "南端站場", platforms.Where(item => item.StationId == "CS03"),
            ["DOWN", "UP"], paths.Select(item => item.PathId))
    };
    var point = new SpatialReferencePointDefinition(
        "CS02-POCKET", "CS02", "中央避車線錨定站", SpatialReferencePointKind.CentralSidingTurnback,
        distanceFromStopToCrossoverMeters: 20, crossoverLengthMeters: 30, distanceFromCrossoverToTurnbackStopMeters: 40,
        turnbackDwellSeconds: 5, switchSpeedLimitMetersPerSecond: 20 / 3.6);
    return new InfrastructureGraph(route, yards, tracks, paths, [turnback], [point]);
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

static void TestSchema7CreatesTopologyEditorDraft()
{
    var input = CreateProjectDocument();
    var bidirectional = input with { Dispatch = input.Dispatch! with {
        SimpleHeadwayPlans = new[] { TrainDirection.Outbound, TrainDirection.Inbound }.Select(d =>
            new ProjectHeadwayPlan(d, 0, 180, 1, input.ServiceTypes![0].Id)).ToArray(),
        ManualTimetableRows = new[] { TrainDirection.Outbound, TrainDirection.Inbound }.Select(d =>
            new ProjectManualTimetableRow(0, d, input.ServiceTypes![0].Id)).ToArray() } };
    var origins = TopologyProjectFactory.CreateLinearDraft(bidirectional);
    foreach (var binding in origins.DirectionRouteBindings)
    {
        var expected = origins.ServiceRoutes.Single(r => r.ServiceRouteId == binding.ServiceRouteId).Stops[0].CandidatePlatformIds[0];
        Equal(expected, origins.Dispatch.SimpleHeadwayPlans!.Single(p => p.Direction == binding.Direction).OriginPlatformId!);
        Equal(expected, origins.Dispatch.ManualTimetableRows!.Single(p => p.Direction == binding.Direction).OriginPlatformId!);
    }
    var draft = TopologyProjectFactory.CreateLinearDraft(CreateProjectDocument());
    Equal(TopologyProjectFormat.CurrentSchemaVersion, draft.SchemaVersion);
    True(draft.Topology.Edges.Count >= 2, "線性起稿至少需建立上下行實體 edge。 ");
    Equal(2, draft.DirectionRouteBindings.Length);
    Equal(2, draft.Topology.TurnbackFacilities.Count);
    Equal(2, draft.Topology.TurnbackOperations.Count);
    True(draft.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.TailTrack),
        "線性起稿的端點續行必須使用實體尾軌 edge，不能建立 virtual track。");
    True(draft.Topology.Edges.Any(edge => edge.Kind == TrackEdgeKind.Crossover),
        "線性起稿的端點折返必須先通過實體 crossover edge。 ");
    True(draft.Topology.TurnbackFacilities.All(facility => facility.TurnbackStopPosition is not null),
        "線性起稿的尾軌折返停點必須保存為 edge-local TrackPosition。 ");
    _ = TopologyProjectFormat.Deserialize(TopologyProjectFormat.Serialize(draft));
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

#pragma warning restore CS8321
