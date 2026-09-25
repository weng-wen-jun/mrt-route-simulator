namespace MrtRouteSimulator.Engine;

public sealed class SimulationWorld
{
    public const double FixedTimeStepSeconds = 0.1;

    private const double NumericalTolerance = 1e-7;
    private const double ArrivalPositionToleranceMeters = 0.5;
    // 固定 0.1 秒、Jerk 受限煞車在近停時可能留下約 1.5 m 的殘距；不應再重新牽引。
    private const double StationStopSnapToleranceMeters = 2;
    private const double ArrivalSpeedToleranceMetersPerSecond = 0.15;
    private const double StationBrakingLookAheadMeters = 0.05;
    private const double MovingBlockControlLookAheadSeconds = 0.5;
    private const string DefaultPatternId = "ALL_STOP";
    private const string DefaultServiceClassId = "普通車";
    private readonly List<MutableTrain> _trains = [];
    private readonly SimulationTraceStore _traceStore;
    private readonly List<SafetyObservation> _safetyHistory = [];
    private readonly Dictionary<SafetyObservationKey, double> _lastSafetyHistorySamples = [];
    private readonly List<SimulationEvent> _events = [];
    private readonly List<SimulationEvent> _newEvents = [];
    private readonly List<ScheduledObstacle> _scheduledObstacles = [];
    private readonly Dictionary<string, SafetyStatus> _lastSafetyStatuses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _controlBrakingActive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ServicePattern> _servicePatterns = new(StringComparer.Ordinal);
    private readonly Dictionary<ServiceRunPlanKey, ServiceRunPlan> _serviceRunPlans = [];
    private readonly Dictionary<string, VehicleTypeDefinition> _vehicleTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServiceTypeDefinition> _serviceTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PlannedServiceRun> _dispatchRunsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PlatformRotationKey, string> _lastDestinationPlatforms = [];
    private readonly Dictionary<string, int> _destinationPlatformAllocationCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _platformLastReleasedAtSeconds = new(StringComparer.OrdinalIgnoreCase);
    private readonly TopologyMovementOccupancyIndex _topologyOccupancy = new();
    private readonly ResolvedDispatchPlan? _dispatchPlan;
    private readonly RouteResourceReservationManager _resourceReservations = new();
    private readonly Dictionary<string, PocketServiceReservation> _pocketServiceReservations = new(StringComparer.Ordinal);
    private readonly LinearInfrastructureBuildResult _linearTopology;
    private readonly RouteProjection _outboundRouteProjection;
    private readonly RouteProjection _inboundRouteProjection;
    private readonly TopologyRouteNavigator _outboundTopologyNavigator;
    private readonly TopologyRouteNavigator _inboundTopologyNavigator;
    private readonly TopologyMovementNavigator _outboundMovementNavigator;
    private readonly TopologyMovementNavigator _inboundMovementNavigator;
    // V2 topology-native runtime 的 station order 由 ServiceRoute 的 resolved stops 定義；
    // 不得以 compatibility Route.Stations 倒推目前／下一站。
    private readonly ResolvedRunRouteContext _outboundRunRouteContext;
    private readonly ResolvedRunRouteContext _inboundRunRouteContext;
    private readonly IReadOnlyDictionary<string, ResolvedStop> _outboundResolvedStops;
    private readonly IReadOnlyDictionary<string, ResolvedStop> _inboundResolvedStops;
    private readonly TrackSpeedLimitService _trackSpeedLimits;
    private readonly bool _enforceRouteResources;
    private readonly bool _topologyNativeRuntime;
    private readonly SafetyObservationRetentionPolicy _safetyObservationRetentionPolicy;
    private IReadOnlyList<SafetyObservation> _currentSafety = [];
    private int _nextEventIndex;
    private readonly HashSet<SafetyObservationKey> _safetyKeysWithStatusChange = [];

    private VehiclePerformance GetVehiclePerformance(MutableTrain train)
    {
        if (_vehicleTypes.TryGetValue(train.VehicleTypeId, out var vehicleType))
        {
            return new VehiclePerformance(
                vehicleType.LengthMeters,
                vehicleType.MaxSpeedMetersPerSecond,
                vehicleType.AccelerationMetersPerSecondSquared,
                vehicleType.ServiceBrakeDecelerationMetersPerSecondSquared,
                vehicleType.EmergencyBrakeDecelerationMetersPerSecondSquared,
                vehicleType.JerkMetersPerSecondCubed,
                vehicleType.TractionDecayPerSecond,
                vehicleType.CoastingDecelerationMetersPerSecondSquared);
        }

        return new VehiclePerformance(
            OperationalParameters.TrainLengthMeters,
            TrainParameters.MaxSpeedMetersPerSecond,
            TrainParameters.AccelerationMetersPerSecondSquared,
            Math.Min(
                TrainParameters.DecelerationMetersPerSecondSquared,
                OperationalParameters.ServiceBrakingMetersPerSecondSquared),
            OperationalParameters.EmergencyBrakingMetersPerSecondSquared,
            OperationalParameters.JerkMetersPerSecondCubed,
            OperationalParameters.TractionFadeRatio,
            0);
    }

    private readonly record struct VehiclePerformance(
        double LengthMeters,
        double MaxSpeedMetersPerSecond,
        double AccelerationMetersPerSecondSquared,
        double ServiceBrakingMetersPerSecondSquared,
        double EmergencyBrakingMetersPerSecondSquared,
        double JerkMetersPerSecondCubed,
        double TractionDecayPerSecond,
        double CoastingDecelerationMetersPerSecondSquared);

    /// <summary>
    /// 只作為 UI 的初始班距估計。V2 實際時刻仍由固定 tick 的 topology movement 產生；
    /// 此估計不建立 Route，也不把 chainage 當作物理位置。
    /// </summary>
    private double CalculateTopologyBaselineCycleTime()
    {
        double EstimateTravelTime(ServiceRouteDefinition serviceRoute) => serviceRoute.Traversals
            .Select(traversal => TopologyInfrastructure.GetRequiredEdge(traversal.TrackEdgeId))
            .Sum(edge => edge.LengthMeters / Math.Min(
                TrainParameters.MaxSpeedMetersPerSecond,
                edge.DefaultSpeedLimitMetersPerSecond));

        double EstimateDwellTime(ResolvedRunRouteContext context) => context.Stops
            .Select(stop => TopologyInfrastructure.Stations[stop.StationId].DefaultDwellTimeSeconds)
            .Sum();

        return EstimateTravelTime(_linearTopology.OutboundServiceRoute)
            + EstimateDwellTime(_outboundRunRouteContext)
            + EstimateTravelTime(_linearTopology.InboundServiceRoute)
            + EstimateDwellTime(_inboundRunRouteContext)
            + TrainParameters.TerminalTurnaroundTimeSeconds
            + TrainParameters.OriginTurnaroundTimeSeconds;
    }

    /// <summary>
    /// 過渡表單入口：只把 Route 一次性轉成 Schema 8 physical topology，建構完成後 world
    /// 不持有 Route，也不啟用 legacy virtual-track runtime。新呼叫端應直接使用
    /// <see cref="TopologySimulationDefinition"/> 建構子。
    /// </summary>
    public SimulationWorld(
        Route route,
        TrainParameters trainParameters,
        OperationalParameters operationalParameters,
        int trainCount,
        double? initialDepartureIntervalSeconds = null,
        IEnumerable<SpeedLimitSegment>? speedLimits = null,
        OperationProfileMode profileMode = OperationProfileMode.RealisticOperations,
        MovingBlockMode movingBlockMode = MovingBlockMode.Control,
        IEnumerable<ServicePattern>? servicePatterns = null,
        IEnumerable<ServiceRunPlan>? serviceRunPlans = null,
        ResolvedDispatchPlan? dispatchPlan = null,
        IEnumerable<VehicleTypeDefinition>? vehicleTypes = null,
        InfrastructureGraph? infrastructure = null,
        SimulationTraceRetentionPolicy? traceRetentionPolicy = null,
        IEnumerable<ServiceTypeDefinition>? serviceTypes = null,
        SafetyObservationRetentionPolicy? safetyObservationRetentionPolicy = null)
        : this(
            CreateTopologyFromRouteInput(route, trainParameters, speedLimits, infrastructure),
            trainParameters,
            operationalParameters,
            trainCount,
            initialDepartureIntervalSeconds,
            speedLimits: null,
            profileMode,
            movingBlockMode,
            servicePatterns,
            serviceRunPlans,
            dispatchPlan,
            vehicleTypes,
            infrastructure: null,
            traceRetentionPolicy,
            serviceTypes,
            safetyObservationRetentionPolicy)
    {
    }

    private static TopologySimulationDefinition CreateTopologyFromRouteInput(
        Route route,
        TrainParameters trainParameters,
        IEnumerable<SpeedLimitSegment>? speedLimits,
        InfrastructureGraph? infrastructure)
    {
        if (infrastructure is not null)
        {
            throw new SimulationValidationException([
                "V2 不再接受 legacy InfrastructureGraph；請建立 Schema 8 TopologySimulationDefinition。"
            ]);
        }

        return TopologyProjectFactory.CreateLinearRuntimeTopology(route, trainParameters, speedLimits);
    }

    private SimulationWorld(
        Route? route,
        TrainParameters trainParameters,
        OperationalParameters operationalParameters,
        int trainCount,
        double? initialDepartureIntervalSeconds = null,
        IEnumerable<SpeedLimitSegment>? speedLimits = null,
        OperationProfileMode profileMode = OperationProfileMode.RealisticOperations,
        MovingBlockMode movingBlockMode = MovingBlockMode.Control,
        IEnumerable<ServicePattern>? servicePatterns = null,
        IEnumerable<ServiceRunPlan>? serviceRunPlans = null,
        ResolvedDispatchPlan? dispatchPlan = null,
        IEnumerable<VehicleTypeDefinition>? vehicleTypes = null,
        InfrastructureGraph? infrastructure = null,
        SimulationTraceRetentionPolicy? traceRetentionPolicy = null,
        IEnumerable<ServiceTypeDefinition>? serviceTypes = null,
        LinearInfrastructureBuildResult? topologyBuild = null,
        bool topologyNativeRuntime = false,
        SafetyObservationRetentionPolicy? safetyObservationRetentionPolicy = null)
    {
        if (trainCount <= 0)
        {
            throw new SimulationValidationException(["列車數量必須大於 0。"]);
        }

        if (!topologyNativeRuntime || route is not null)
        {
            throw new InvalidOperationException("V2 SimulationWorld 內部建構必須使用 topology-native 輸入。 ");
        }

        if (topologyNativeRuntime && infrastructure is not null)
        {
            throw new SimulationValidationException([
                "topology-native V2 不可注入 legacy InfrastructureGraph；請改用 TopologySimulationDefinition。"
            ]);
        }

        TrainParameters = trainParameters ?? throw new ArgumentNullException(nameof(trainParameters));
        OperationalParameters = operationalParameters ?? throw new ArgumentNullException(nameof(operationalParameters));
        _linearTopology = topologyBuild ?? LinearInfrastructureBuilder.Build(route!, TrainParameters.MaxSpeedMetersPerSecond);
        _topologyNativeRuntime = topologyNativeRuntime;
        _outboundRouteProjection = new RouteProjection(_linearTopology.Infrastructure, _linearTopology.OutboundServiceRoute);
        _inboundRouteProjection = new RouteProjection(_linearTopology.Infrastructure, _linearTopology.InboundServiceRoute);
        _outboundTopologyNavigator = new TopologyRouteNavigator(_linearTopology.Infrastructure, _linearTopology.OutboundServiceRoute);
        _inboundTopologyNavigator = new TopologyRouteNavigator(_linearTopology.Infrastructure, _linearTopology.InboundServiceRoute);
        _outboundMovementNavigator = new TopologyMovementNavigator(
            _linearTopology.Infrastructure,
            TopologyMovementPlanResolver.ResolveServiceRoute(_linearTopology.Infrastructure, _linearTopology.OutboundServiceRoute));
        _inboundMovementNavigator = new TopologyMovementNavigator(
            _linearTopology.Infrastructure,
            TopologyMovementPlanResolver.ResolveServiceRoute(_linearTopology.Infrastructure, _linearTopology.InboundServiceRoute));
        _outboundRunRouteContext = ResolvedRunRouteContext.Create(
            _linearTopology.Infrastructure,
            _linearTopology.OutboundServiceRoute);
        _inboundRunRouteContext = ResolvedRunRouteContext.Create(
            _linearTopology.Infrastructure,
            _linearTopology.InboundServiceRoute);
        _outboundResolvedStops = _outboundRunRouteContext.Stops
            .ToDictionary(stop => stop.StationId, StringComparer.OrdinalIgnoreCase);
        _inboundResolvedStops = _inboundRunRouteContext.Stops
            .ToDictionary(stop => stop.StationId, StringComparer.OrdinalIgnoreCase);
        SpeedLimits = new SpeedLimitService();
        _trackSpeedLimits = new TrackSpeedLimitService(
            _linearTopology.Infrastructure,
            null);
        ProfileMode = profileMode;
        MovingBlockMode = movingBlockMode;
        BrakingEstimationMode = BrakingEstimationMode.Service;
        TraceRetentionPolicy = traceRetentionPolicy ?? SimulationTraceRetentionPolicy.Full;
        _traceStore = new SimulationTraceStore(TraceRetentionPolicy);
        _safetyObservationRetentionPolicy = safetyObservationRetentionPolicy
            ?? SafetyObservationRetentionPolicy.Full;
        _enforceRouteResources = false;
        _dispatchPlan = dispatchPlan;
        foreach (var run in dispatchPlan?.Runs ?? [])
        {
            _dispatchRunsById.Add(run.ServiceRunId, run);
        }
        foreach (var vehicleType in vehicleTypes ?? [])
        {
            if (!_vehicleTypes.TryAdd(vehicleType.Id, vehicleType))
            {
                throw new SimulationValidationException([$"車型 ID「{vehicleType.Id}」重複。"]);
            }
        }
        foreach (var serviceType in serviceTypes ?? [])
        {
            if (!_serviceTypes.TryAdd(serviceType.Id, serviceType))
            {
                throw new SimulationValidationException([$"服務類型 ID「{serviceType.Id}」重複。"]);
            }
        }

        var runtimeTrainCount = dispatchPlan?.Runs.Count ?? trainCount;
        if (runtimeTrainCount <= 0)
        {
            throw new SimulationValidationException(["發車計畫至少需要一個車次。"]);
        }

        ValidateDispatchVehicleAssignments(dispatchPlan);
        var effectiveServicePlans = MergeDispatchServicePlans(serviceRunPlans, dispatchPlan);
        InitializeServicePlans(runtimeTrainCount, servicePatterns, effectiveServicePlans);
        ValidateVehicleTypeReferences();

        BaselineCycleTimeSeconds = CalculateTopologyBaselineCycleTime();
        HeadwaySeconds = initialDepartureIntervalSeconds
            ?? BaselineCycleTimeSeconds / runtimeTrainCount;
        InitializeTrains(runtimeTrainCount);
        ActivateDueTrains();
        RefreshTopologyOccupancy();
    }

    public SimulationWorld(
        TopologySimulationDefinition topology,
        TrainParameters trainParameters,
        OperationalParameters operationalParameters,
        int trainCount,
        double? initialDepartureIntervalSeconds = null,
        IEnumerable<SpeedLimitSegment>? speedLimits = null,
        OperationProfileMode profileMode = OperationProfileMode.RealisticOperations,
        MovingBlockMode movingBlockMode = MovingBlockMode.Control,
        IEnumerable<ServicePattern>? servicePatterns = null,
        IEnumerable<ServiceRunPlan>? serviceRunPlans = null,
        ResolvedDispatchPlan? dispatchPlan = null,
        IEnumerable<VehicleTypeDefinition>? vehicleTypes = null,
        InfrastructureGraph? infrastructure = null,
        SimulationTraceRetentionPolicy? traceRetentionPolicy = null,
        IEnumerable<ServiceTypeDefinition>? serviceTypes = null,
        SafetyObservationRetentionPolicy? safetyObservationRetentionPolicy = null)
        : this(
            null,
            trainParameters,
            operationalParameters,
            trainCount,
            initialDepartureIntervalSeconds,
            speedLimits,
            profileMode,
            movingBlockMode,
            servicePatterns,
            serviceRunPlans,
            dispatchPlan,
            vehicleTypes,
            infrastructure,
            traceRetentionPolicy,
            serviceTypes,
            topology.ToBuildResult(),
            topologyNativeRuntime: true,
            safetyObservationRetentionPolicy: safetyObservationRetentionPolicy)
    {
    }

    /// <summary>
    /// 僅供 V1／legacy V2 adapter 使用。TopologySimulationDefinition 建構的 V2 world
    /// 不會建立或持有 Route；呼叫端若需顯示里程應讀取 snapshot 的 derived projection 欄位。
    /// </summary>
    public Route Route => throw new InvalidOperationException(
        "topology-native SimulationWorld 不提供 compatibility Route。");

    public TrainParameters TrainParameters { get; }

    public OperationalParameters OperationalParameters { get; }

    public SpeedLimitService SpeedLimits { get; }

    public SimulationEngineKind EngineKind => SimulationEngineKind.V2RealisticOperations;

    /// <summary>僅 legacy V2 station-yard adapter 可用；Schema 8 topology runtime 不持有此模型。</summary>
    public InfrastructureGraph Infrastructure => throw new InvalidOperationException(
        "topology-native SimulationWorld 不提供 legacy InfrastructureGraph。");

    /// <summary>Phase D 的逐區間 topology runtime mirror；既有 Infrastructure 仍供 Schema 7 資源管理使用。</summary>
    public InfrastructureGraphV4 TopologyInfrastructure => _linearTopology.Infrastructure;

    /// <summary>取得方向別 route-local projection；僅供相容輸出，非物理位置權威來源。</summary>
    public RouteProjection GetRouteProjection(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? _outboundRouteProjection : _inboundRouteProjection;

    public ResolvedDispatchPlan? DispatchPlan => _dispatchPlan;

    /// <summary>
    /// 取得 topology-native V2 的結果輸入。此 API 不建立 compatibility Route，且只輸出
    /// 由 resolved stop / traversal 推導的顯示 chainage。
    /// </summary>
    public TopologyResultContext GetTopologyResultContext()
    {
        if (!_topologyNativeRuntime)
        {
            throw new InvalidOperationException("僅 topology-native SimulationWorld 可提供 TopologyResultContext。");
        }

        return TopologyResultContext.Create(
            TopologyInfrastructure,
            _outboundRunRouteContext,
            _inboundRunRouteContext);
    }

    public OperationProfileMode ProfileMode { get; }

    public MovingBlockMode MovingBlockMode { get; private set; }

    public BrakingEstimationMode BrakingEstimationMode { get; private set; }

    public double CurrentTimeSeconds { get; private set; }

    public double HeadwaySeconds { get; }

    public double BaselineCycleTimeSeconds { get; }

    public double TimeStepSeconds => FixedTimeStepSeconds;

    public SimulationTraceRetentionPolicy TraceRetentionPolicy { get; }

    public SafetyObservationRetentionPolicy SafetyObservationRetentionPolicy =>
        _safetyObservationRetentionPolicy;

    /// <summary>所有已排入世界的車輛均已完成最後一個車次並退出營運。</summary>
    public bool IsComplete => _trains.Count > 0 && _trains.All(train => train.Completed);

    public IReadOnlyList<TrajectorySample> Trajectory => _traceStore.Trajectory;

    public IReadOnlyList<SafetyObservation> SafetyHistory => _safetyHistory;

    public IReadOnlyList<SimulationEvent> Events => _events;

    /// <summary>
    /// 目前 active 列車的 edge-local footprint。只包含已遷移 unified movement cursor 的列車；
    /// legacy virtual track 仍由既有資源模型管理，避免將兩套座標混為同一個占用空間。
    /// </summary>
    public IReadOnlyDictionary<string, TopologyMovementFootprint> TopologyOccupancy => _topologyOccupancy.FootprintsByOwner;

    public TrackPosition? GetTrainCenterPosition(string vehicleId)
    {
        var train = _trains.FirstOrDefault(t => t.VehicleId == vehicleId);
        return train?.RuntimeTopologyCursor is { } cursor
            ? GetMovementNavigator(train).Retreat(cursor, GetVehiclePerformance(train).LengthMeters / 2).Position
            : null;
    }

    public IReadOnlyList<PlatformDefinitionV4> GetTopologyPlatformOccupancy(string vehicleId)
    {
        if (!_topologyOccupancy.FootprintsByOwner.TryGetValue(vehicleId, out var footprint))
        {
            return [];
        }

        return TopologyPlatformOccupancy.GetOverlappingPlatforms(TopologyInfrastructure, footprint);
    }

    public IReadOnlyCollection<ServicePattern> ServicePatterns => _servicePatterns.Values;

    public IReadOnlyCollection<ServiceRunPlan> ServiceRunPlans => _serviceRunPlans.Values;

    public SimulationSnapshot GetSnapshot() => new(
        CurrentTimeSeconds,
        _trains.Select(ToState).ToArray(),
        _currentSafety,
        _events.Skip(_nextEventIndex).ToArray());

    public void AcknowledgeSnapshotEvents() => _nextEventIndex = _events.Count;

    public void SetMovingBlockMode(MovingBlockMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new SimulationValidationException(["移動閉塞模式無效。"]);
        }

        MovingBlockMode = mode;
        _currentSafety = ComputeSafetyObservations(recordStatusEvents: true);
    }

    public void SetBrakingEstimationMode(BrakingEstimationMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new SimulationValidationException(["煞車估算模式無效。"]);
        }

        if (BrakingEstimationMode == mode)
        {
            return;
        }

        BrakingEstimationMode = mode;
        AddEvent(
            SimulationEventType.BrakingModeChanged,
            _trains.FirstOrDefault(train => train.Active),
            null,
            $"煞車距離估算已切換為{(mode == BrakingEstimationMode.Service ? "營運" : "緊急")}煞車。",
            0,
            0);
        _currentSafety = ComputeSafetyObservations(recordStatusEvents: false);
    }

    public void ScheduleObstacleEmergencyStop(string vehicleId, double triggerTimeSeconds)
    {
        if (!_trains.Any(train => string.Equals(train.VehicleId, vehicleId, StringComparison.Ordinal)))
        {
            throw new SimulationValidationException([$"找不到車輛 {vehicleId}。"]);
        }

        if (!RouteValidator.IsFinite(triggerTimeSeconds) || triggerTimeSeconds < CurrentTimeSeconds)
        {
            throw new SimulationValidationException(["障礙物急停時間必須是有限且不得早於目前模擬時間。"]);
        }

        _scheduledObstacles.Add(new ScheduledObstacle(vehicleId, triggerTimeSeconds));
    }

    public void TriggerObstacleEmergencyStop(string vehicleId)
    {
        var train = _trains.FirstOrDefault(item => string.Equals(item.VehicleId, vehicleId, StringComparison.Ordinal));
        if (train is null || !train.Active)
        {
            throw new SimulationValidationException([$"車輛 {vehicleId} 尚未發車或不在營運中，無法觸發障礙物急停。"]);
        }

        if (train.ObstacleStopped)
        {
            return;
        }

        train.ObstacleStopped = true;
        train.Speed = 0;
        train.Acceleration = 0;
        train.Phase = OperationalPhase.EmergencyStopped;
        AddEvent(
            SimulationEventType.ObstacleEmergencyStop,
            train,
            null,
            "障礙物情境：前車瞬間停止；此為保守測試事件，不是正常物理減速。",
            train.Position,
            0);
        _currentSafety = ComputeSafetyObservations(recordStatusEvents: true);
    }

    public void AdvanceTo(double targetTimeSeconds)
    {
        if (!RouteValidator.IsFinite(targetTimeSeconds) || targetTimeSeconds < 0)
        {
            throw new SimulationValidationException(["模擬時間必須是有限的非負秒數。"]);
        }

        if (targetTimeSeconds + NumericalTolerance < CurrentTimeSeconds)
        {
            Reset();
        }

        while (CurrentTimeSeconds + FixedTimeStepSeconds <= targetTimeSeconds + NumericalTolerance)
        {
            Tick();
        }
    }

    public SimulationSnapshot Tick()
    {
        _newEvents.Clear();
        CurrentTimeSeconds = Math.Round(CurrentTimeSeconds + FixedTimeStepSeconds, 10);
        TriggerScheduledObstacles();
        ActivateDueTrains();
        RefreshTopologyOccupancy();

        var observationsBeforeMove = MovingBlockMode == MovingBlockMode.Independent
            ? []
            : ComputeSafetyObservations(recordStatusEvents: false);
        var controlLimits = MovingBlockMode == MovingBlockMode.Control
            ? CalculateMovingBlockSpeedLimits(observationsBeforeMove)
            : new Dictionary<string, double>(StringComparer.Ordinal);
        var protectionLeaders = MovingBlockMode == MovingBlockMode.Control
            ? observationsBeforeMove.ToDictionary(
                observation => observation.FollowerVehicleId,
                observation => observation.LeaderVehicleId,
                StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var train in _trains)
        {
            UpdateTrain(train, controlLimits, protectionLeaders);
        }

        RefreshTopologyOccupancy();
        ApplyCollisionProtection();
        RefreshTopologyOccupancy();
        ReleaseClearedPocketServiceReservations();
        _currentSafety = MovingBlockMode == MovingBlockMode.Independent
            ? []
            : ComputeSafetyObservations(recordStatusEvents: true);
        RecordSafetyHistory(_currentSafety);
        RecordTrajectory();

        return new SimulationSnapshot(
            CurrentTimeSeconds,
            _trains.Select(ToState).ToArray(),
            _currentSafety,
            _newEvents.ToArray());
    }

    public void Reset()
    {
        foreach (var reservation in _pocketServiceReservations.Values) _resourceReservations.Release(reservation.OwnerId);
        _pocketServiceReservations.Clear();
        var trainCount = _trains.Count;
        foreach (var train in _trains)
        {
            ReleaseRouteReservation(train);
        }
        CurrentTimeSeconds = 0;
        _traceStore.Reset();
        _safetyHistory.Clear();
        _lastSafetyHistorySamples.Clear();
        _events.Clear();
        _newEvents.Clear();
        _currentSafety = [];
        _lastSafetyStatuses.Clear();
        _safetyKeysWithStatusChange.Clear();
        _controlBrakingActive.Clear();
        _lastDestinationPlatforms.Clear();
        _destinationPlatformAllocationCounts.Clear();
        _platformLastReleasedAtSeconds.Clear();
        _topologyOccupancy.Clear();
        _scheduledObstacles.Clear();
        _nextEventIndex = 0;
        InitializeTrains(trainCount);
        ActivateDueTrains();
        RefreshTopologyOccupancy();
    }

    private static IEnumerable<ServiceRunPlan> MergeDispatchServicePlans(
        IEnumerable<ServiceRunPlan>? legacyPlans,
        ResolvedDispatchPlan? dispatchPlan)
    {
        foreach (var plan in legacyPlans ?? [])
        {
            yield return plan;
        }

        if (dispatchPlan is null)
        {
            yield break;
        }

        var continuationTargets = dispatchPlan.Runs
            .Where(run => run.ContinuationServiceRunId is not null)
            .Select(run => run.ContinuationServiceRunId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var run in dispatchPlan.Runs.Where(run => !continuationTargets.Contains(run.ServiceRunId)))
        {
            yield return new ServiceRunPlan(
                run.VehicleId!,
                1,
                run.Direction,
                run.ServiceTypeId,
                run.StopPatternId,
                run.VehicleTypeId,
                run.OriginPlatformId,
                RelativeScheduleSeconds(run.PlannedDepartureTime, dispatchPlan.ScheduleAnchorTime),
                run.ServiceRunId);
        }
    }

    private static void ValidateDispatchVehicleAssignments(ResolvedDispatchPlan? dispatchPlan)
    {
        if (dispatchPlan is null)
        {
            return;
        }

        var continuationTargets = dispatchPlan.Runs
            .Where(run => run.ContinuationServiceRunId is not null)
            .Select(run => run.ContinuationServiceRunId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = dispatchPlan.Runs
            .Where(run => !string.IsNullOrWhiteSpace(run.VehicleId))
            .GroupBy(run => run.VehicleId!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count(run => !continuationTargets.Contains(run.ServiceRunId)) > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new SimulationValidationException(
                duplicates.Select(id => $"車輛「{id}」被指派給多個未串接的計畫車次；請使用折返接續車次 ID 建立車輛運用鏈。"));
        }
    }

    private void ValidateVehicleTypeReferences()
    {
        if (_vehicleTypes.Count == 0)
        {
            return;
        }

        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in _serviceRunPlans.Values)
        {
            references.Add(plan.VehicleTypeId?.Trim() ?? string.Empty);
        }

        foreach (var run in _dispatchPlan?.Runs ?? [])
        {
            references.Add(run.VehicleTypeId?.Trim() ?? string.Empty);
        }

        if (references.Count == 0)
        {
            references.Add("DEFAULT_VEHICLE");
        }

        var errors = references
            .Where(id => !_vehicleTypes.ContainsKey(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id => string.IsNullOrWhiteSpace(id)
                ? "車型 ID 不得空白。"
                : $"找不到車型「{id}」。")
            .ToArray();
        if (errors.Length > 0)
        {
            throw new SimulationValidationException(errors);
        }
    }

    private static double RelativeScheduleSeconds(TimeSpan value, TimeSpan anchor)
    {
        var result = (value - anchor).TotalSeconds;
        while (result < 0)
        {
            result += TimeSpan.FromDays(1).TotalSeconds;
        }

        return result;
    }

    private ResolvedRunRouteContext GetRunRouteContext(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? _outboundRunRouteContext : _inboundRunRouteContext;

    private int GetStationCount(TrainDirection direction) =>
        _topologyNativeRuntime
            ? GetRunRouteContext(direction).Stops.Count
            : Route.Stations.Count;

    private int GetInitialStationIndex(TrainDirection direction) =>
        _topologyNativeRuntime
            ? GetRunRouteContext(direction).OriginStopIndex
            : direction == TrainDirection.Outbound
                ? 0
                : Route.Stations.Count - 1;

    private int GetStationIndexStep(MutableTrain train) =>
        _topologyNativeRuntime ? 1 : (int)train.Direction;

    /// <summary>
    /// facility return 所接上的 departure route 可能從中間站之後的 traversal 開始。回到
    /// ServiceRoute 時，current station 必須是該實體起點之前（或正好位於）的最後一個 stop，
    /// 而不能一律假定為 route origin。
    /// </summary>
    private int GetTopologyDepartureStationIndex(TurnbackOperationDefinition operation)
    {
        var direction = GetDirectionForServiceRoute(operation.DepartureServiceRouteId);
        var context = GetRunRouteContext(direction);
        var facility = TopologyInfrastructure.TurnbackFacilities[operation.FacilityId];
        var routeNavigator = GetTopologyNavigator(direction);
        var departureTraversalIndex = GetServiceRouteDefinition(direction).Traversals
            .Select((traversal, index) => (traversal, index))
            .Single(item => item.traversal.TrackEdgeId.Equals(
                facility.DepartureTrackEdgeId,
                StringComparison.OrdinalIgnoreCase))
            .index;
        var departureDistance = routeNavigator.GetDistanceAlongTraversal(
            new TopologyTraversalCursor(
                routeNavigator.ServiceRouteId,
                departureTraversalIndex,
                new TrackPosition(facility.DepartureTrackEdgeId, facility.DepartureStartOffsetMeters)));
        var result = context.OriginStopIndex;
        for (var index = 0; index < context.Stops.Count; index++)
        {
            var stop = context.Stops[index];
            if (stop.TraversalIndex > departureTraversalIndex)
            {
                break;
            }

            var stopDistance = routeNavigator.GetDistanceAlongTraversal(
                new TopologyTraversalCursor(
                    routeNavigator.ServiceRouteId,
                    stop.TraversalIndex,
                    stop.Position));
            if (stop.TraversalIndex < departureTraversalIndex
                || stopDistance <= departureDistance + TrackPosition.DefaultToleranceMeters)
            {
                result = index;
            }
        }

        return result;
    }

    private bool HasStationIndex(MutableTrain train, int stationIndex) =>
        stationIndex >= 0 && stationIndex < GetStationCount(train.Direction);

    /// <summary>
    /// 為尚未遷移的 station pattern / dwell API 提供 station 描述。Schema 8 的來源是
    /// ServiceRoute resolved stop + topology StationDefinitionV4；PositionMeters 僅是顯示投影。
    /// </summary>
    private Station GetRuntimeStation(TrainDirection direction, int stationIndex)
    {
        if (!_topologyNativeRuntime)
        {
            return Route.Stations[stationIndex];
        }

        var routeContext = GetRunRouteContext(direction);
        if (stationIndex < 0 || stationIndex >= routeContext.Stops.Count)
        {
            throw new SimulationValidationException([
                $"{direction} ServiceRoute 的停站索引 {stationIndex} 超出範圍。"
            ]);
        }

        var resolvedStop = routeContext.Stops[stationIndex];
        var topologyStation = TopologyInfrastructure.Stations[resolvedStop.StationId];
        return new Station(
            topologyStation.StationId,
            topologyStation.Name,
            resolvedStop.ChainageMeters,
            topologyStation.DefaultDwellTimeSeconds);
    }

    private Station GetRuntimeStation(MutableTrain train, int stationIndex) =>
        GetRuntimeStation(train.Direction, stationIndex);

    private string GetCurrentStationId(MutableTrain train) =>
        GetRuntimeStation(train, train.CurrentStationIndex).StationId;

    private string? GetNextStationId(MutableTrain train) =>
        HasStationIndex(train, train.NextStationIndex)
            ? GetRuntimeStation(train, train.NextStationIndex).StationId
            : null;

    private void InitializeTopologyTrainPosition(MutableTrain train)
    {
        var routeContext = GetRunRouteContext(train.Direction);
        var originStop = ResolveVehicleStop(train, routeContext.Stops[routeContext.OriginStopIndex]);
        var routeNavigator = GetTopologyNavigator(train.Direction);
        var distanceAlongTraversal = routeNavigator.GetDistanceAlongTraversal(
            new TopologyTraversalCursor(
                routeNavigator.ServiceRouteId,
                originStop.TraversalIndex,
                originStop.Position));
        SetTrainRuntimeTopologyCursor(
            train,
            GetMainlineMovementNavigator(train.Direction),
            GetMainlineMovementNavigator(train.Direction).CreateCursor(
                0,
                originStop.TraversalIndex,
                distanceAlongTraversal));
        train.PlatformId = originStop.PlatformId;
    }

    private void InitializeServicePlans(
        int trainCount,
        IEnumerable<ServicePattern>? servicePatterns,
        IEnumerable<ServiceRunPlan>? serviceRunPlans)
    {
        _servicePatterns.Clear();
        _serviceRunPlans.Clear();
        var stationIds = _topologyNativeRuntime
            ? _outboundRunRouteContext.Stops
                .Concat(_inboundRunRouteContext.Stops)
                .Select(stop => stop.StationId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : Route.Stations.Select(station => station.StationId).ToHashSet(StringComparer.Ordinal);
        var endpointIds = _topologyNativeRuntime
            ? new[]
                {
                    _outboundRunRouteContext.Stops[_outboundRunRouteContext.OriginStopIndex].StationId,
                    _outboundRunRouteContext.Stops[_outboundRunRouteContext.TerminalStopIndex].StationId,
                    _inboundRunRouteContext.Stops[_inboundRunRouteContext.OriginStopIndex].StationId,
                    _inboundRunRouteContext.Stops[_inboundRunRouteContext.TerminalStopIndex].StationId
                }
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.Ordinal)
            {
                Route.Stations[0].StationId,
                Route.Stations[^1].StationId
            };
        var errors = new List<string>();

        foreach (var pattern in servicePatterns ?? [])
        {
            if (pattern is null || string.IsNullOrWhiteSpace(pattern.PatternId))
            {
                errors.Add("服務模式編號不得空白。");
                continue;
            }

            var patternId = pattern.PatternId.Trim();
            if (_servicePatterns.ContainsKey(patternId))
            {
                errors.Add($"服務模式編號重複：{patternId}。");
                continue;
            }

            var instructions = pattern.Instructions ?? [];
            var duplicateStations = instructions
                .Where(instruction => instruction is not null)
                .GroupBy(instruction => instruction.StationId?.Trim() ?? string.Empty, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            foreach (var stationId in duplicateStations)
            {
                errors.Add($"服務模式 {patternId} 的車站指令重複：{stationId}。");
            }

            foreach (var instruction in instructions)
            {
                if (instruction is null || string.IsNullOrWhiteSpace(instruction.StationId))
                {
                    errors.Add($"服務模式 {patternId} 包含空白車站編號。");
                    continue;
                }

                var stationId = instruction.StationId.Trim();
                if (!stationIds.Contains(stationId))
                {
                    errors.Add($"服務模式 {patternId} 找不到車站 {stationId}。");
                }

                if (!Enum.IsDefined(instruction.Mode))
                {
                    errors.Add($"服務模式 {patternId} 的 {stationId} 停站模式無效。");
                }

                if (instruction.Mode == StationServiceMode.Pass && endpointIds.Contains(stationId))
                {
                    errors.Add($"起終點站 {stationId} 不得設定為跨站。");
                }

                if (instruction.Mode == StationServiceMode.Turnback)
                {
                    if (endpointIds.Contains(stationId))
                    {
                        errors.Add($"起終點站 {stationId} 應使用端點折返續行；中央避車線折返僅能設定於中間實體錨定站。 ");
                    }
                    if (_topologyNativeRuntime)
                    {
                        var stationOperation = TopologyInfrastructure.StationOperations.Values
                            .FirstOrDefault(operation => operation.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase));
                        if (stationOperation is null || stationOperation.TurnbackOperationIds.Count == 0)
                        {
                            errors.Add($"服務模式 {patternId} 的 {stationId} 折返需對應 topology StationOperation 與 TurnbackOperation。 ");
                        }
                    }
                    else
                    {
                        var spatialReference = Infrastructure.FindSpatialReferencePoint(stationId);
                        if (spatialReference?.Kind != SpatialReferencePointKind.CentralSidingTurnback)
                        {
                            errors.Add($"服務模式 {patternId} 的 {stationId} 折返需對應 CentralSidingTurnback 空間參考點。");
                        }
                    }
                }

                if (instruction.SpeedLimitMetersPerSecond is { } limit
                    && (!double.IsFinite(limit) || limit <= 0))
                {
                    errors.Add($"服務模式 {patternId} 的 {stationId} 速度上限必須是有限正數。");
                }

                if (instruction.DwellTimeSeconds is { } dwell
                    && (!double.IsFinite(dwell) || dwell < 0))
                {
                    errors.Add($"服務模式 {patternId} 的 {stationId} 停站時間必須是有限非負數。");
                }
            }

            _servicePatterns.Add(patternId, pattern with
            {
                PatternId = patternId,
                PatternName = string.IsNullOrWhiteSpace(pattern.PatternName) ? patternId : pattern.PatternName.Trim(),
                Instructions = instructions.ToArray()
            });
        }

        var validVehicleIds = _dispatchPlan is null
            ? Enumerable.Range(1, trainCount)
                .Select(index => $"Vehicle {index:00}")
                .ToHashSet(StringComparer.Ordinal)
            : _dispatchPlan.Runs
                .Select(run => run.VehicleId!)
                .ToHashSet(StringComparer.Ordinal);
        foreach (var plan in serviceRunPlans ?? [])
        {
            if (plan is null)
            {
                errors.Add("車次服務計畫不得為空。");
                continue;
            }

            var vehicleId = plan.VehicleId?.Trim() ?? string.Empty;
            var patternId = plan.PatternId?.Trim() ?? string.Empty;
            if (!validVehicleIds.Contains(vehicleId))
            {
                errors.Add($"車次服務計畫找不到車輛 {vehicleId}。");
            }

            if (plan.ServiceNumber <= 0)
            {
                errors.Add($"{vehicleId} 的車次序號必須大於 0。");
            }

            if (!Enum.IsDefined(plan.Direction))
            {
                errors.Add($"{vehicleId} 的車次方向無效。");
            }

            if (string.IsNullOrWhiteSpace(plan.ServiceClassId))
            {
                errors.Add($"{vehicleId} 的列車等級不得空白。");
            }

            if (!string.Equals(patternId, DefaultPatternId, StringComparison.Ordinal)
                && !_servicePatterns.ContainsKey(patternId))
            {
                errors.Add($"{vehicleId} 的車次服務模式不存在：{patternId}。");
            }

            var key = new ServiceRunPlanKey(vehicleId, plan.ServiceNumber, plan.Direction);
            if (_serviceRunPlans.ContainsKey(key))
            {
                errors.Add($"車次服務計畫重複：{vehicleId}／{plan.Direction}／{plan.ServiceNumber}。");
                continue;
            }

            _serviceRunPlans.Add(key, plan with
            {
                VehicleId = vehicleId,
                ServiceClassId = plan.ServiceClassId.Trim(),
                PatternId = patternId,
                VehicleTypeId = plan.VehicleTypeId?.Trim() ?? string.Empty
            });
        }

        if (errors.Count > 0)
        {
            _servicePatterns.Clear();
            _serviceRunPlans.Clear();
            throw new SimulationValidationException(errors);
        }
    }

    private void ApplyServicePlan(MutableTrain train)
    {
        var key = new ServiceRunPlanKey(train.VehicleId, train.ServiceNumber, train.Direction);
        if (_serviceRunPlans.TryGetValue(key, out var plan))
        {
            train.ServiceClassId = plan.ServiceClassId;
            train.PatternId = plan.PatternId;
            train.VehicleTypeId = plan.VehicleTypeId;
            train.PlatformId = plan.OriginPlatformId;
            train.ExplicitServiceRunId = plan.ExplicitServiceRunId;
            if (plan.PlannedDepartureTimeSeconds is { } plannedDeparture)
            {
                train.StartTime = plannedDeparture;
                train.PlannedDepartureTime = plannedDeparture;
            }
            return;
        }

        train.ServiceClassId = DefaultServiceClassId;
        train.PatternId = DefaultPatternId;
        train.VehicleTypeId = "DEFAULT_VEHICLE";
        train.PlatformId = null;
        train.ExplicitServiceRunId = null;
    }

    private StationServiceInstruction GetStationInstruction(MutableTrain train, Station station)
    {
        if (!_servicePatterns.TryGetValue(train.PatternId, out var pattern))
        {
            return new StationServiceInstruction(station.StationId, StationServiceMode.Stop);
        }

        return pattern.Instructions.FirstOrDefault(instruction =>
                string.Equals(instruction.StationId, station.StationId, StringComparison.Ordinal))
            ?? new StationServiceInstruction(station.StationId, StationServiceMode.Stop);
    }

    private void InitializeTrains(int trainCount)
    {
        _trains.Clear();
        if (_dispatchPlan is not null)
        {
            var continuationTargets = _dispatchPlan.Runs
                .Where(run => run.ContinuationServiceRunId is not null)
                .Select(run => run.ContinuationServiceRunId!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var run in _dispatchPlan.Runs
                         .Where(run => !continuationTargets.Contains(run.ServiceRunId))
                         .OrderBy(item => item.Sequence))
            {
                var outbound = run.Direction == TrainDirection.Outbound;
                var stationIndex = GetInitialStationIndex(run.Direction);
                var originStation = GetRuntimeStation(run.Direction, stationIndex);
                var train = new MutableTrain
                {
                    VehicleId = run.VehicleId!,
                    ServiceNumber = 1,
                    StartTime = RelativeScheduleSeconds(run.PlannedDepartureTime, _dispatchPlan.ScheduleAnchorTime),
                    PlannedDepartureTime = RelativeScheduleSeconds(run.PlannedDepartureTime, _dispatchPlan.ScheduleAnchorTime),
                    Direction = run.Direction,
                    TrackId = outbound ? "DOWN" : "UP",
                    Position = originStation.PositionMeters,
                    CurrentStationIndex = stationIndex,
                    NextStationIndex = stationIndex + (_topologyNativeRuntime ? 1 : (int)run.Direction),
                    Phase = OperationalPhase.Pending,
                    ContinueAfterTerminal = run.ContinueAfterTerminal,
                    DispatchServiceRunBaseId = run.ServiceRunId,
                    ContinuationServiceRunId = run.ContinuationServiceRunId
                };
                ApplyServicePlan(train);
                if (_topologyNativeRuntime)
                {
                    InitializeTopologyTrainPosition(train);
                }
                else
                {
                    SetTrainPositionFromProjectedCoordinate(train, train.Position);
                }
                _trains.Add(train);
            }

            return;
        }

        for (var index = 0; index < trainCount; index++)
        {
            var train = new MutableTrain
            {
                VehicleId = $"Vehicle {index + 1:00}",
                ServiceNumber = 1,
                StartTime = index * HeadwaySeconds,
                PlannedDepartureTime = index * HeadwaySeconds,
                Direction = TrainDirection.Outbound,
                TrackId = "DOWN",
                Position = 0,
                CurrentStationIndex = 0,
                NextStationIndex = 1,
                Phase = OperationalPhase.Pending
            };
            ApplyServicePlan(train);
            if (_topologyNativeRuntime)
            {
                InitializeTopologyTrainPosition(train);
            }
            else
            {
                SetTrainPositionFromProjectedCoordinate(train, train.Position);
            }
            _trains.Add(train);
        }
    }

    private void TriggerScheduledObstacles()
    {
        foreach (var scheduled in _scheduledObstacles
                     .Where(item => !item.Triggered && item.TriggerTimeSeconds <= CurrentTimeSeconds + NumericalTolerance))
        {
            var train = _trains.First(item => item.VehicleId == scheduled.VehicleId);
            if (train.Active)
            {
                TriggerObstacleEmergencyStop(train.VehicleId);
                scheduled.Triggered = true;
            }
        }
    }

    private void ActivateDueTrains()
    {
        foreach (var train in _trains
            .Where(train => !train.Active && !train.Completed
                && CurrentTimeSeconds + NumericalTolerance >= train.StartTime)
            .OrderBy(train => train.StartTime))
        {
            if (!TryReserveDepartureRoute(train))
            {
                continue;
            }

            if (MovingBlockMode == MovingBlockMode.Control && !HasDepartureClearance(train))
            {
                continue;
            }

            train.Active = true;
            train.ActualDepartureTime = CurrentTimeSeconds;
            train.Phase = OperationalPhase.Accelerating;
            var delay = Math.Max(0, CurrentTimeSeconds - train.PlannedDepartureTime);
            if (delay > NumericalTolerance)
            {
                AddEvent(
                    SimulationEventType.DepartureDelayed,
                    train,
                    null,
                    $"{train.ServiceRunId} 實際發車延後 {delay:0.0} 秒。",
                    train.Position,
                    0,
                    delaySeconds: delay);
            }
            AddEvent(SimulationEventType.Departure, train, null, $"{train.ServiceRunId} 發車。", train.Position, 0);
        }
    }

    private bool HasDepartureClearance(MutableTrain train, string? prospectiveTrackId = null)
    {
        if (UsesTopologyRuntimeSafety(train))
        {
            var nearestTopologyLeader = FindNearestTopologyLeader(train);
            if (nearestTopologyLeader is null
                || !TryGetTopologySafetyMetrics(train, nearestTopologyLeader, out var topologyMetrics))
            {
                return true;
            }

            var topologyStationarySafetyDistance = Math.Max(
                OperationalParameters.AbsoluteMinimumGapMeters,
                2 * OperationalParameters.PositioningErrorMeters + OperationalParameters.SafetyMarginMeters);
            return topologyMetrics.ActualGapMeters >= topologyStationarySafetyDistance - NumericalTolerance;
        }

        var trackId = prospectiveTrackId ?? train.TrackId;
        var nearestLeader = _trains
            .Where(other => !ReferenceEquals(other, train)
                && other.Active
                && other.Phase != OperationalPhase.OutOfService
                && other.Direction == train.Direction
                && other.TrackId.Equals(trackId, StringComparison.OrdinalIgnoreCase)
                && Progress(train.Direction, other.Position) >= Progress(train.Direction, train.Position))
            .OrderBy(other => Math.Abs(other.Position - train.Position))
            .FirstOrDefault();
        if (nearestLeader is null)
        {
            return true;
        }

        var leaderLength = GetVehiclePerformance(nearestLeader).LengthMeters;
        var leaderRear = nearestLeader.Direction == TrainDirection.Outbound
            ? nearestLeader.Position - leaderLength
            : nearestLeader.Position + leaderLength;
        var gap = nearestLeader.Direction == TrainDirection.Outbound
            ? leaderRear - train.Position
            : train.Position - leaderRear;
        var stationarySafetyDistance = Math.Max(
            OperationalParameters.AbsoluteMinimumGapMeters,
            2 * OperationalParameters.PositioningErrorMeters + OperationalParameters.SafetyMarginMeters);
        return gap >= stationarySafetyDistance - NumericalTolerance;
    }

    private bool TryReserveDepartureRoute(MutableTrain train)
    {
        if (_topologyNativeRuntime)
        {
            return TryReservePocketServiceRoute(train);
        }
        if (!_enforceRouteResources)
        {
            return true;
        }

        if (train.ReservationId is not null && !train.HasStationReservation)
        {
            return true;
        }

        if (train.NextStationIndex < 0 || train.NextStationIndex >= Route.Stations.Count)
        {
            return true;
        }

        var origin = Route.Stations[train.CurrentStationIndex];
        var destination = Route.Stations[train.NextStationIndex];
        var length = GetVehiclePerformance(train).LengthMeters;
        var originCandidates = Infrastructure.FindCompatiblePlatforms(
            origin.StationId,
            train.Direction,
            length,
            train.VehicleTypeId,
            train.ServiceClassId,
            requirePassengerService: true);
        var originPlatform = string.IsNullOrWhiteSpace(train.PlatformId)
            ? originCandidates.FirstOrDefault()
            : originCandidates.FirstOrDefault(item => item.PlatformId.Equals(train.PlatformId, StringComparison.OrdinalIgnoreCase));
        var destinationCandidates = Infrastructure.FindCompatiblePlatforms(
            destination.StationId,
            train.Direction,
            length,
            train.VehicleTypeId,
            train.ServiceClassId,
            requirePassengerService: true);
        if (originPlatform is null || destinationCandidates.Count == 0)
        {
            return MarkWaitingForResource(train, $"{origin.StationId} 找不到符合車型、服務類型與方向的月台。", origin.StationId);
        }

        var reservationId = train.ReservationId
            ?? $"{train.VehicleId}|{train.ServiceRunId}|{train.CurrentStationIndex}";
        var reserveDestinationPlatform = GetStationInstruction(train, destination).Mode != StationServiceMode.Pass;
        RoutePathDefinition? selectedPath = null;
        PlatformDefinition? selectedDestinationPlatform = null;
        string? blockedResourceId = null;
        foreach (var destinationPlatform in OrderDestinationPlatformCandidates(train, destination, destinationCandidates))
        {
            var path = Infrastructure.FindPaths(
                    originPlatform.PlatformId,
                    destinationPlatform.PlatformId,
                    train.Direction,
                    train.VehicleTypeId,
                    train.ServiceClassId,
                    length)
                .FirstOrDefault();
            if (path is null)
            {
                continue;
            }

            var resources = path.ResourceIds
                .Append($"PLATFORM:{originPlatform.PlatformId}")
                .Concat(reserveDestinationPlatform
                    ? [$"PLATFORM:{destinationPlatform.PlatformId}"]
                    : [])
                .ToArray();
            if (!_resourceReservations.Reserve(reservationId, resources))
            {
                blockedResourceId = path.PathId;
                continue;
            }

            selectedPath = path;
            selectedDestinationPlatform = destinationPlatform;
            break;
        }

        if (selectedPath is null || selectedDestinationPlatform is null)
        {
            var resourceId = blockedResourceId ?? originPlatform.PlatformId;
            return MarkWaitingForResource(
                train,
                $"{originPlatform.PlatformId} 至 {destination.StationId} 沒有可用進路或目的月台。",
                resourceId);
        }

        train.ReservationId = reservationId;
        train.ReservedAtPosition = train.Position;
        train.RoutePathId = selectedPath.PathId;
        train.ExpectedDestinationPlatformId = reserveDestinationPlatform
            ? selectedDestinationPlatform.PlatformId
            : null;
        train.HasDestinationPlatformReservation = reserveDestinationPlatform;
        train.HasStationReservation = false;
        train.PlatformId = originPlatform.PlatformId;
        train.PendingDepartureTrackId = selectedPath.TrackSegmentIds.FirstOrDefault() ?? train.TrackId;
        train.TrackId = train.PendingDepartureTrackId;
        train.WaitingResourceEventEmitted = false;
        train.Constraints |= OperationalConstraint.Platform | OperationalConstraint.RouteResource;
        RememberDestinationPlatform(train, destination, selectedDestinationPlatform);
        AddEvent(
            SimulationEventType.PlatformAssigned,
            train,
            null,
            $"{train.ServiceRunId} 配置月台 {originPlatform.Name}。",
            train.Position,
            train.Speed,
            originPlatform.PlatformId);
        AddEvent(
            SimulationEventType.RouteReserved,
            train,
            null,
            $"{train.ServiceRunId} 已鎖定進路 {selectedPath.PathId}，並保留目的月台 {selectedDestinationPlatform.PlatformId}。",
            train.Position,
            train.Speed,
            selectedPath.PathId,
            resourceIds: _resourceReservations.GetResources(reservationId));
        return true;
    }

    private IReadOnlyList<PlatformDefinition> OrderDestinationPlatformCandidates(
        MutableTrain train,
        Station destination,
        IReadOnlyList<PlatformDefinition> candidates)
    {
        if (candidates.Count < 2)
        {
            return candidates;
        }

        if (UsesRoundRobinDestinationAllocation(train, destination))
        {
            var key = new PlatformRotationKey(destination.StationId, train.Direction);
            if (!_lastDestinationPlatforms.TryGetValue(key, out var lastPlatformId))
            {
                return candidates;
            }

            var lastIndex = candidates
                .Select((platform, index) => (platform, index))
                .FirstOrDefault(item => item.platform.PlatformId.Equals(lastPlatformId, StringComparison.OrdinalIgnoreCase))
                .index;
            if (!candidates[lastIndex].PlatformId.Equals(lastPlatformId, StringComparison.OrdinalIgnoreCase))
            {
                return candidates;
            }

            return candidates
                .Skip(lastIndex + 1)
                .Concat(candidates.Take(lastIndex + 1))
                .ToArray();
        }

        return GetDestinationPlatformAllocationStrategy(destination) switch
        {
            // 所有候選都會先經過資源預約檢查；在可用月台之中，優先讓長時間未使用的月台承接。
            PlatformAllocationStrategy.EarliestAvailable => candidates
                .OrderBy(platform => _platformLastReleasedAtSeconds.TryGetValue(platform.PlatformId, out var releasedAt)
                    ? releasedAt
                    : double.NegativeInfinity)
                .ThenBy(platform => _destinationPlatformAllocationCounts.GetValueOrDefault(platform.PlatformId))
                .ThenBy(platform => platform.PlatformId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            // 自動策略以同站、同方向的實際成功配置次數均衡負載；無法預約者仍會繼續嘗試下一候選。
            PlatformAllocationStrategy.Automatic => candidates
                .OrderBy(platform => _destinationPlatformAllocationCounts.GetValueOrDefault(platform.PlatformId))
                .ThenBy(platform => _platformLastReleasedAtSeconds.TryGetValue(platform.PlatformId, out var releasedAt)
                    ? releasedAt
                    : double.NegativeInfinity)
                .ThenBy(platform => platform.PlatformId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => candidates
        };
    }

    private PlatformAllocationStrategy GetDestinationPlatformAllocationStrategy(Station destination) =>
        Infrastructure.StationYards.FirstOrDefault(item =>
            item.StationId.Equals(destination.StationId, StringComparison.OrdinalIgnoreCase))?.PlatformAllocationStrategy
        ?? PlatformAllocationStrategy.Automatic;

    private bool UsesRoundRobinDestinationAllocation(MutableTrain train, Station destination)
    {
        var yard = Infrastructure.StationYards.FirstOrDefault(item =>
            item.StationId.Equals(destination.StationId, StringComparison.OrdinalIgnoreCase));
        if (yard?.PlatformAllocationStrategy == PlatformAllocationStrategy.RoundRobin)
        {
            return true;
        }

        var spatialReference = Infrastructure.FindSpatialReferencePoint(destination.StationId);
        return spatialReference is
        {
            Kind: SpatialReferencePointKind.BeforeStationTurnback,
            AlternateBerthing: true
        };
    }

    private void RememberDestinationPlatform(
        MutableTrain train,
        Station destination,
        PlatformDefinition destinationPlatform)
    {
        if (UsesRoundRobinDestinationAllocation(train, destination))
        {
            _lastDestinationPlatforms[new PlatformRotationKey(destination.StationId, train.Direction)] =
                destinationPlatform.PlatformId;
            return;
        }

        if (GetDestinationPlatformAllocationStrategy(destination) is PlatformAllocationStrategy.Automatic
            or PlatformAllocationStrategy.EarliestAvailable)
        {
            _destinationPlatformAllocationCounts[destinationPlatform.PlatformId] =
                _destinationPlatformAllocationCounts.GetValueOrDefault(destinationPlatform.PlatformId) + 1;
        }
    }

    /// <summary>
    /// 正常服務進路穿越雙向袋狀軌時，採保守的發車前整組預約。
    /// 同一組入口／中央軌／出口的資源在車尾全數淨空後才釋放；不靠發車時差防止對向侵入。
    /// 折返及越行仍使用原有 facility reservation，不建立第二套占用管理員。
    /// </summary>
    private bool TryReservePocketServiceRoute(MutableTrain train)
    {
        var pocketResourceIds = TopologyInfrastructure.Edges.Values
            .Where(e => e.Kind == TrackEdgeKind.PocketTrack && e.Directionality == TrackDirectionality.Bidirectional)
            .SelectMany(e => e.ConflictResourceIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pocketResourceIds.Count == 0) return true;
        var route = GetServiceRouteDefinition(train.Direction);
        var start = train.ServiceRouteTraversalIndex ?? 0;
        var protectedTraversals = route.Traversals.Select((t, i) => (Edge: TopologyInfrastructure.GetRequiredEdge(t.TrackEdgeId), Index: i))
            .Where(item => item.Index >= start && item.Edge.ConflictResourceIds.Any(pocketResourceIds.Contains)).ToArray();
        if (protectedTraversals.Length == 0) return true;
        var required = protectedTraversals.SelectMany(t => t.Edge.ConflictResourceIds.Where(pocketResourceIds.Contains))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_pocketServiceReservations.TryGetValue(train.VehicleId, out var existing))
        {
            if (existing.RouteId == route.ServiceRouteId && required.IsSubsetOf(existing.Resources)) return true;
            required.UnionWith(existing.Resources);
        }
        var owner = $"SERVICE-POCKET:{train.VehicleId}";
        if (!_resourceReservations.Reserve(owner, required))
            return MarkWaitingForResource(train, $"{train.ServiceRunId} 等待共用袋狀軌進路淨空後發車。", required.Order().First());
        _pocketServiceReservations[train.VehicleId] = new(owner, route.ServiceRouteId, protectedTraversals.Max(t => t.Index), required);
        train.WaitingResourceEventEmitted = false;
        AddEvent(SimulationEventType.RouteReserved, train, null, $"{train.ServiceRunId} 已預約共用袋狀軌進路。",
            train.Position, train.Speed, resourceIds: required.Order().ToArray());
        return true;
    }

    private void ReleaseClearedPocketServiceReservations()
    {
        foreach (var (vehicleId, reservation) in _pocketServiceReservations.ToArray())
        {
            var train = _trains.First(t => t.VehicleId == vehicleId);
            var passed = train.Completed || train.Active &&
                (GetServiceRouteId(train.Direction) != reservation.RouteId || train.ServiceRouteTraversalIndex > reservation.LastTraversalIndex);
            if (!passed || reservation.Resources.Any(r => !TopologyResourceReleasePolicy.CanReleaseResource(
                    r, _topologyOccupancy.FootprintsByOwner.Values, TopologyInfrastructure))) continue;
            _resourceReservations.Release(reservation.OwnerId);
            _pocketServiceReservations.Remove(vehicleId);
            AddEvent(SimulationEventType.RouteReleased, train, null, $"{train.ServiceRunId} 車尾已淨空共用袋狀軌進路。",
                train.Position, train.Speed, resourceIds: reservation.Resources.Order().ToArray());
        }
    }

    private sealed record PocketServiceReservation(string OwnerId, string RouteId, int LastTraversalIndex, IReadOnlySet<string> Resources);

    private bool MarkWaitingForResource(MutableTrain train, string message, string resourceId)
    {
        train.Constraints |= OperationalConstraint.RouteResource | OperationalConstraint.Platform;
        if (!train.WaitingResourceEventEmitted)
        {
            AddEvent(
                SimulationEventType.WaitingForResource,
                train,
                null,
                message,
                train.Position,
                train.Speed,
                resourceId);
            train.WaitingResourceEventEmitted = true;
        }

        return false;
    }

    private void ReleaseDepartureRouteIfClear(MutableTrain train)
    {
        if (train.ReservationId is null || train.RoutePathId is null || !train.Active)
        {
            return;
        }

        var length = GetVehiclePerformance(train).LengthMeters;
        if (Math.Abs(train.Position - train.ReservedAtPosition) + NumericalTolerance < length)
        {
            return;
        }

        RetainDestinationPlatformOrReleaseRoute(train);
    }

    private void RetainDestinationPlatformOrReleaseRoute(MutableTrain train)
    {
        if (train.ReservationId is not null && train.HasStationReservation)
        {
            return;
        }

        if (train.ReservationId is null
            || !train.HasDestinationPlatformReservation
            || string.IsNullOrWhiteSpace(train.ExpectedDestinationPlatformId))
        {
            ReleaseRouteReservation(train);
            return;
        }

        var pathId = train.RoutePathId;
        var platformResource = $"PLATFORM:{train.ExpectedDestinationPlatformId}";
        var releasedResources = _resourceReservations.GetResources(train.ReservationId)
            .Where(resource => !resource.Equals(platformResource, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (!_resourceReservations.Reserve(train.ReservationId, [platformResource]))
        {
            throw new InvalidOperationException($"列車 {train.VehicleId} 無法保留已鎖定的目的月台 {train.ExpectedDestinationPlatformId}。");
        }

        train.RoutePathId = null;
        train.HasDestinationPlatformReservation = false;
        train.HasStationReservation = true;
        RememberReleasedPlatforms(releasedResources);
        AddEvent(
            SimulationEventType.RouteReleased,
            train,
            null,
            $"{train.ServiceRunId} 已釋放進路 {pathId}，保留抵達月台 {train.ExpectedDestinationPlatformId}。",
            train.Position,
            train.Speed,
            pathId,
            resourceIds: releasedResources);
    }

    private void ReleaseRouteReservation(MutableTrain train)
    {
        if (train.ReservationId is null)
        {
            return;
        }

        var pathId = train.RoutePathId;
        var releasedResources = _resourceReservations.GetResources(train.ReservationId);
        _resourceReservations.Release(train.ReservationId);
        RememberReleasedPlatforms(releasedResources);
        AddEvent(
            SimulationEventType.RouteReleased,
            train,
            null,
            pathId is null
                ? $"{train.ServiceRunId} 已釋放站場資源。"
                : $"{train.ServiceRunId} 已釋放進路 {pathId}。",
            train.Position,
            train.Speed,
            pathId,
            resourceIds: releasedResources);
        train.ReservationId = null;
        train.RoutePathId = null;
        train.HasDestinationPlatformReservation = false;
        train.HasStationReservation = false;
    }

    private void RememberReleasedPlatforms(IEnumerable<string> resourceIds)
    {
        foreach (var resourceId in resourceIds)
        {
            const string platformPrefix = "PLATFORM:";
            if (resourceId.StartsWith(platformPrefix, StringComparison.OrdinalIgnoreCase)
                && resourceId.Length > platformPrefix.Length)
            {
                _platformLastReleasedAtSeconds[resourceId[platformPrefix.Length..]] = CurrentTimeSeconds;
            }
        }
    }

    private StationOvertakeSelection? FindEligibleStationOvertake(MutableTrain expressTrain)
    {
        if (_topologyNativeRuntime)
        {
            return null;
        }

        if (!_serviceTypes.TryGetValue(expressTrain.ServiceClassId, out var expressService)
            || !expressService.CanRequestOvertake)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(expressTrain.OvertakeFacilityId))
        {
            var activeFacility = Infrastructure.StationOvertakeFacilities.FirstOrDefault(item =>
                item.FacilityId.Equals(expressTrain.OvertakeFacilityId, StringComparison.OrdinalIgnoreCase));
            var stationIndex = activeFacility is null
                ? -1
                : Route.Stations.ToList().FindIndex(station =>
                    station.StationId.Equals(activeFacility.StationId, StringComparison.OrdinalIgnoreCase));
            if (activeFacility is not null && stationIndex >= 0)
            {
                var station = Route.Stations[stationIndex];
                var localTrain = _trains
                    .Where(candidate => !ReferenceEquals(candidate, expressTrain)
                        && candidate.Active
                        && candidate.Phase == OperationalPhase.Dwelling
                        && candidate.Direction == expressTrain.Direction
                        && candidate.CurrentStationIndex == stationIndex
                        && candidate.PlatformId is not null
                        && candidate.PlatformId.Equals(activeFacility.LocalPlatformId, StringComparison.OrdinalIgnoreCase)
                        && GetStationInstruction(candidate, station).Mode != StationServiceMode.Pass
                        && _serviceTypes.TryGetValue(candidate.ServiceClassId, out var localService)
                        && expressService.Priority > localService.Priority)
                    .OrderBy(candidate => Math.Abs(candidate.Position - expressTrain.Position))
                    .FirstOrDefault();
                return localTrain is null
                    ? null
                    : new StationOvertakeSelection(station, activeFacility, localTrain);
            }
        }

        var selections = new List<StationOvertakeSelection>();
        for (var stationIndex = expressTrain.NextStationIndex;
             stationIndex >= 0 && stationIndex < Route.Stations.Count;
             stationIndex += (int)expressTrain.Direction)
        {
            var station = Route.Stations[stationIndex];
            // 一旦遇到快速車應停靠的車站，不能再跨越該停車點安排更遠的越行。
            if (GetStationInstruction(expressTrain, station).Mode != StationServiceMode.Pass)
            {
                break;
            }

            foreach (var facility in Infrastructure.FindStationOvertakeFacilities(station.StationId, expressTrain.Direction))
            {
                if (!expressTrain.TrackId.Equals(facility.MainlineTrackSegmentId, StringComparison.OrdinalIgnoreCase)
                    || ForwardDistance(expressTrain.Direction, expressTrain.Position, facility.EntryPositionMeters) < -NumericalTolerance)
                {
                    continue;
                }

                var localTrain = _trains
                    .Where(candidate => !ReferenceEquals(candidate, expressTrain)
                        && candidate.Active
                        && candidate.Phase == OperationalPhase.Dwelling
                        && candidate.Direction == expressTrain.Direction
                        && candidate.CurrentStationIndex == stationIndex
                        && candidate.PlatformId is not null
                        && candidate.PlatformId.Equals(facility.LocalPlatformId, StringComparison.OrdinalIgnoreCase)
                        && GetStationInstruction(candidate, station).Mode != StationServiceMode.Pass
                        && _serviceTypes.TryGetValue(candidate.ServiceClassId, out var localService)
                        && expressService.Priority > localService.Priority)
                    .OrderBy(candidate => Math.Abs(candidate.Position - expressTrain.Position))
                    .FirstOrDefault();
                if (localTrain is not null)
                {
                    selections.Add(new StationOvertakeSelection(station, facility, localTrain));
                }
            }
        }

        if (selections.Count == 0)
        {
            return null;
        }

        return selections
            .OrderBy(selection => _resourceReservations.IsAvailable(
                selection.Facility.ResourceIds.Append($"PLATFORM:{selection.Facility.ExpressPlatformId}")) ? 0 : 1)
            .ThenBy(selection => ForwardDistance(
                expressTrain.Direction,
                expressTrain.Position,
                selection.Facility.EntryPositionMeters))
            .ThenBy(selection => selection.Facility.FacilityId, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private bool TryEnterStationOvertake(MutableTrain expressTrain)
    {
        if (_topologyNativeRuntime)
        {
            return false;
        }

        if (expressTrain.OvertakeFacilityId is not null)
        {
            return true;
        }

        var selection = FindEligibleStationOvertake(expressTrain);
        if (selection is not { } candidate || !HasReachedStationOvertakeEntry(expressTrain, candidate.Facility))
        {
            return false;
        }

        var station = candidate.Station;
        var facility = candidate.Facility;
        var localTrain = candidate.LocalTrain;

        var reservationId = $"OVERTAKE:{expressTrain.VehicleId}|{facility.FacilityId}|{expressTrain.CurrentStationIndex}";
        var resources = facility.ResourceIds.Append($"PLATFORM:{facility.ExpressPlatformId}").ToArray();
        if (!_resourceReservations.Reserve(reservationId, resources))
        {
            expressTrain.WaitingForOvertakeFacilityId = facility.FacilityId;
            return false;
        }

        expressTrain.ReservationId = reservationId;
        expressTrain.RoutePathId = null;
        expressTrain.HasDestinationPlatformReservation = false;
        expressTrain.HasStationReservation = false;
        expressTrain.OvertakeFacilityId = facility.FacilityId;
        expressTrain.OvertakenVehicleId = localTrain.VehicleId;
        expressTrain.WaitingForOvertakeFacilityId = null;
        expressTrain.TrackId = facility.ExpressTrackSegmentId;
        EnterLegacyVirtualTrack(expressTrain, expressTrain.Position);
        AddEvent(
            SimulationEventType.RouteReserved,
            expressTrain,
            localTrain.VehicleId,
            $"{expressTrain.ServiceRunId} 已鎖定越行設施 {facility.FacilityId} 的衝突區與通過月台。",
            expressTrain.Position,
            expressTrain.Speed,
            facility.FacilityId,
            resourceIds: _resourceReservations.GetResources(reservationId));
        AddEvent(
            SimulationEventType.OvertakeRequested,
            expressTrain,
            localTrain.VehicleId,
            $"{expressTrain.ServiceRunId} 取得 {station.StationId} 站內通過線，普通車 {localTrain.ServiceRunId} 留在待避月台。",
            expressTrain.Position,
            expressTrain.Speed,
            facility.FacilityId);
        return true;
    }

    private static bool HasReachedStationOvertakeEntry(
        MutableTrain train,
        StationOvertakeFacilityDefinition facility) =>
        train.Direction == TrainDirection.Outbound
            ? train.Position >= facility.EntryPositionMeters - NumericalTolerance
            : train.Position <= facility.EntryPositionMeters + NumericalTolerance;

    private double? GetStationOvertakeEntryDistance(MutableTrain train)
    {
        if (_topologyNativeRuntime)
        {
            return null;
        }

        var selection = FindEligibleStationOvertake(train);
        if (selection is not { } candidate || train.OvertakeFacilityId is not null)
        {
            return null;
        }

        var distance = ForwardDistance(train.Direction, train.Position, candidate.Facility.EntryPositionMeters);
        return distance > NumericalTolerance ? distance : 0;
    }

    private bool ShouldHoldForStationOvertake(MutableTrain localTrain)
    {
        if (_topologyNativeRuntime)
        {
            return false;
        }

        if (!_serviceTypes.ContainsKey(localTrain.ServiceClassId)
            || localTrain.PlatformId is null)
        {
            return false;
        }

        var station = Route.Stations[localTrain.CurrentStationIndex];
        var selected = _trains
            .Where(candidate => candidate.Active
                && candidate.Direction == localTrain.Direction
                && !ReferenceEquals(candidate, localTrain)
                && _serviceTypes.TryGetValue(candidate.ServiceClassId, out var service)
                && service.CanRequestOvertake
                && service.Priority > _serviceTypes[localTrain.ServiceClassId].Priority)
            .Select(candidate => (Train: candidate, Selection: FindEligibleStationOvertake(candidate)))
            .Where(candidate => candidate.Selection is { } selection
                && ReferenceEquals(selection.LocalTrain, localTrain))
            .OrderBy(candidate => Math.Abs(candidate.Train.Position - localTrain.Position))
            .FirstOrDefault();
        if (selected.Selection is not { } overtake)
        {
            return false;
        }

        var facility = overtake.Facility;
        var waitingExpress = selected.Train;

        var entryDistance = ForwardDistance(localTrain.Direction, waitingExpress.Position, facility.EntryPositionMeters);
        if (waitingExpress.OvertakeFacilityId is null
            && entryDistance > OperationalParameters.ApproachDistanceMeters + NumericalTolerance)
        {
            return false;
        }

        if (!localTrain.WaitingForOvertakeEventEmitted)
        {
            AddEvent(
                SimulationEventType.WaitingForResource,
                localTrain,
                waitingExpress.VehicleId,
                $"{localTrain.ServiceRunId} 在 {station.StationId} 待避，等待快速車 {waitingExpress.ServiceRunId} 站內跨越。",
                localTrain.Position,
                0,
                facility.FacilityId);
            localTrain.WaitingForOvertakeEventEmitted = true;
        }

        return true;
    }

    private bool ShouldHoldForTopologyPassing(MutableTrain localTrain)
    {
        if (!_topologyNativeRuntime
            || localTrain.PlatformId is null
            || !localTrain.Active
            || !HasStationIndex(localTrain, localTrain.CurrentStationIndex))
        {
            return false;
        }

        var station = GetRuntimeStation(localTrain, localTrain.CurrentStationIndex);
        foreach (var expressTrain in _trains.Where(candidate => candidate.Active
                     && candidate.Direction == localTrain.Direction
                     && !ReferenceEquals(candidate, localTrain)))
        {
            var activeMovement = expressTrain.TopologyPassingMovement;
            if (activeMovement is not null
                && activeMovement.Facility.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)
                && activeMovement.Facility.LocalPlatformId.Equals(localTrain.PlatformId, StringComparison.OrdinalIgnoreCase))
            {
                // movement 直到快速車車尾離開 facility edge 才會清除；即使車頭已經
                // 匯入正線，普通車也不得在同一 merge node 前起動。這裡不以車頭是否
                // 完成越行作為解除條件，必須等待 rear-clear。
                if (!localTrain.WaitingForOvertakeEventEmitted)
                {
                    AddEvent(
                        SimulationEventType.WaitingForResource,
                        localTrain,
                        expressTrain.VehicleId,
                        $"{localTrain.ServiceRunId} 在 {station.StationId} 實體月台待避，等待 {expressTrain.ServiceRunId} 通過 topology passing edge 並淨空合流。",
                        localTrain.Position,
                        0,
                        activeMovement.Facility.FacilityId);
                    localTrain.WaitingForOvertakeEventEmitted = true;
                }

                return true;
            }

            if (!HasStationIndex(expressTrain, expressTrain.NextStationIndex))
            {
                continue;
            }

            var nextStation = GetRuntimeStation(expressTrain, expressTrain.NextStationIndex);
            var candidate = FindEligibleTopologyPassing(
                expressTrain,
                nextStation,
                GetStationInstruction(expressTrain, nextStation));
            if (candidate is null || !ReferenceEquals(candidate.LocalTrain, localTrain))
            {
                continue;
            }

            var entryDistance = GetTopologyPassingEntryDistance(
                expressTrain,
                nextStation,
                GetStationInstruction(expressTrain, nextStation));
            if (entryDistance is null || entryDistance > OperationalParameters.ApproachDistanceMeters + NumericalTolerance)
            {
                continue;
            }

            if (!localTrain.WaitingForOvertakeEventEmitted)
            {
                AddEvent(
                    SimulationEventType.WaitingForResource,
                    localTrain,
                    expressTrain.VehicleId,
                    $"{localTrain.ServiceRunId} 在 {station.StationId} 實體月台待避，等待 {expressTrain.ServiceRunId} 通過 topology passing edge。",
                    localTrain.Position,
                    0,
                    candidate.Facility.FacilityId);
                localTrain.WaitingForOvertakeEventEmitted = true;
            }

            return true;
        }

        return false;
    }

    private void UpdateTrain(
        MutableTrain train,
        IReadOnlyDictionary<string, double> controlLimits,
        IReadOnlyDictionary<string, string> protectionLeaders)
    {
        train.Constraints = OperationalConstraint.None;
        ReleaseDepartureRouteIfClear(train);
        TryReleaseCompletedTopologyTurnbackResources(train);
        TryReleaseCompletedTopologyPassingResources(train);
        if (!train.Active || train.Phase == OperationalPhase.OutOfService || train.Collided || train.ObstacleStopped)
        {
            return;
        }

        if (train.AwaitingPostPassRoute)
        {
            train.Speed = 0;
            train.Acceleration = 0;
            if (!TryReserveDepartureRoute(train))
            {
                train.Constraints |= OperationalConstraint.RouteResource | OperationalConstraint.Platform;
                return;
            }

            var facility = train.OvertakeFacilityId is null
                ? null
                : Infrastructure.StationOvertakeFacilities.FirstOrDefault(item =>
                    item.FacilityId.Equals(train.OvertakeFacilityId, StringComparison.OrdinalIgnoreCase));
            if (facility is not null)
            {
                CompleteStationOvertake(train, facility);
            }
        }

        var performance = GetVehiclePerformance(train);
        if (train.DwellRemaining > NumericalTolerance)
        {
            train.DwellRemaining = Math.Max(0, train.DwellRemaining - FixedTimeStepSeconds);
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train, train.Acceleration);
            train.Phase = OperationalPhase.Dwelling;
            if (train.DwellRemaining <= NumericalTolerance)
            {
                if (train.AwaitingTopologyTurnbackDeparture)
                {
                    train.Phase = OperationalPhase.Turning;
                    if (CompleteTurnaround(train))
                    {
                        train.AwaitingTopologyTurnbackDeparture = false;
                    }

                    return;
                }

                if (train.TerminalAction != TerminalAction.None)
                {
                    CompleteTerminalStationWork(train);
                    return;
                }

                if (ShouldHoldForTopologyPassing(train) || ShouldHoldForStationOvertake(train))
                {
                    train.DwellRemaining = FixedTimeStepSeconds;
                    return;
                }

                if (!TryReserveDepartureRoute(train))
                {
                    train.Constraints |= OperationalConstraint.RouteResource | OperationalConstraint.Platform;
                    return;
                }

                if (MovingBlockMode == MovingBlockMode.Control
                    && !HasDepartureClearance(train, train.PendingDepartureTrackId))
                {
                    train.Constraints |= OperationalConstraint.MovingBlock;
                    AssignStationTrack(train, train.PlatformId);
                    train.DwellRemaining = FixedTimeStepSeconds;
                    return;
                }

                train.TrackId = train.PendingDepartureTrackId ?? train.TrackId;
                train.Phase = OperationalPhase.Accelerating;
                AddEvent(SimulationEventType.Departure, train, null, $"{train.ServiceRunId} 停站後發車。", train.Position, 0);
            }

            return;
        }

        if (train.AwaitingTopologyTurnbackDeparture)
        {
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train, train.Acceleration);
            train.Phase = OperationalPhase.Turning;
            if (CompleteTurnaround(train))
            {
                train.AwaitingTopologyTurnbackDeparture = false;
            }

            return;
        }

        if (train.TopologyTurnbackMovement is { IsOperating: true })
        {
            UpdateTopologyTurnbackMovement(train);
            return;
        }

        if (train.TopologyPassingMovement is { IsOperating: true })
        {
            UpdateTopologyPassingMovement(train);
            return;
        }

        if (train.TailTrackMovement is not null)
        {
            UpdateAfterStationTailTrackMovement(train);
            return;
        }

        if (train.SpatialTurnbackMovement is not null)
        {
            UpdateSpatialTurnbackMovement(train);
            return;
        }

        if (train.TurnaroundRemaining > NumericalTolerance || train.TurnaroundPrepared)
        {
            if (train.TurnaroundRemaining > NumericalTolerance)
            {
                train.TurnaroundRemaining = Math.Max(0, train.TurnaroundRemaining - FixedTimeStepSeconds);
            }
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train, train.Acceleration);
            train.Phase = OperationalPhase.Turning;
            if (train.TurnaroundRemaining <= NumericalTolerance)
            {
                CompleteTurnaround(train);
            }

            return;
        }

        if (train.TerminalAction != TerminalAction.None)
        {
            CompleteTerminalStationWork(train);
            return;
        }

        var nextStation = GetRuntimeStation(train, train.NextStationIndex);
        var resolvedStop = GetResolvedStop(train, nextStation);
        var stationInstruction = GetStationInstruction(train, nextStation);
        if (TryEnterTopologyPassingMovement(train, nextStation, stationInstruction))
        {
            return;
        }
        var topologyPassingEntryDistance = GetTopologyPassingEntryDistance(train, nextStation, stationInstruction);
        var enteredStationOvertake = TryEnterStationOvertake(train);
        var isScheduledStop = stationInstruction.Mode is StationServiceMode.Stop or StationServiceMode.Turnback;
        var distanceToStation = GetDistanceToResolvedStop(train, nextStation, resolvedStop);
        if (isScheduledStop
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToStation <= StationStopSnapToleranceMeters)
        {
            ArriveAtStation(train, nextStation, resolvedStop);
            return;
        }

        var effectiveServiceBraking = performance.ServiceBrakingMetersPerSecondSquared;
        var permitted = GetTrackPermittedSpeed(train, performance, effectiveServiceBraking);

        if (isScheduledStop)
        {
            train.Constraints |= OperationalConstraint.StationStop;
            var approachLimit = stationInstruction.SpeedLimitMetersPerSecond
                ?? (OperationalParameters.ApproachSpeedMetersPerSecond > 0
                    ? OperationalParameters.ApproachSpeedMetersPerSecond
                    : null);
            if (approachLimit is { } stopApproachLimit)
            {
                var approachBoundary = nextStation.PositionMeters
                    - (int)train.Direction * OperationalParameters.ApproachDistanceMeters;
                var distanceToBoundary = ForwardDistance(train.Direction, train.Position, approachBoundary);
                if (distanceToBoundary >= -NumericalTolerance)
                {
                    permitted = Math.Min(
                        permitted,
                        SpeedLimits.GetPermittedSpeedMetersPerSecond(
                            train.Position,
                            train.Direction,
                            performance.MaxSpeedMetersPerSecond,
                            effectiveServiceBraking,
                            performance.JerkMetersPerSecondCubed,
                            train.Speed,
                            approachBoundary,
                            stopApproachLimit));
                }
                else
                {
                    permitted = Math.Min(permitted, stopApproachLimit);
                }
            }
        }
        else if (stationInstruction.SpeedLimitMetersPerSecond is { } passingLimit)
        {
            permitted = Math.Min(
                permitted,
                SpeedLimits.GetPermittedSpeedMetersPerSecond(
                    train.Position,
                    train.Direction,
                    performance.MaxSpeedMetersPerSecond,
                    effectiveServiceBraking,
                    performance.JerkMetersPerSecondCubed,
                    train.Speed,
                nextStation.PositionMeters,
                passingLimit));
        }

        StationStopControlOutput? stationStopControl = null;
        if (isScheduledStop)
        {
            stationStopControl = StationStopController.Calculate(new StationStopControlInput(
                distanceToStation,
                train.Speed,
                train.Acceleration,
                permitted,
                effectiveServiceBraking,
                performance.JerkMetersPerSecondCubed,
                FixedTimeStepSeconds,
                ProfileMode == OperationProfileMode.RealisticOperations,
                StationStopSnapToleranceMeters,
                ArrivalSpeedToleranceMetersPerSecond));
            permitted = Math.Min(permitted, stationStopControl.TargetSpeedMetersPerSecond);
        }

        var obstacleDistance = GetObstacleDistanceAhead(train);
        if (obstacleDistance is not null)
        {
            permitted = Math.Min(permitted, CalculateStopCurveSpeed(train, obstacleDistance.Value));
        }

        var overtakeEntryDistance = GetStationOvertakeEntryDistance(train);
        if (overtakeEntryDistance is { } entryDistance)
        {
            permitted = Math.Min(permitted, CalculateStopCurveSpeed(train, entryDistance));
            train.Constraints |= OperationalConstraint.RouteResource;
        }

        if (!enteredStationOvertake && controlLimits.TryGetValue(train.VehicleId, out var movingBlockLimit))
        {
            if (movingBlockLimit + 0.05 < permitted)
            {
                if (_controlBrakingActive.Add(train.VehicleId))
                {
                    AddEvent(
                        SimulationEventType.ControlBraking,
                        train,
                        null,
                        $"移動閉塞控制將允許速度限制為 {movingBlockLimit * 3.6:0.#} km/h。",
                        train.Position,
                        train.Speed);
                }
            }
            else
            {
                _controlBrakingActive.Remove(train.VehicleId);
            }

            permitted = Math.Min(permitted, movingBlockLimit);
            train.Constraints |= OperationalConstraint.MovingBlock;
        }
        else
        {
            _controlBrakingActive.Remove(train.VehicleId);
        }

        if (train.StationBrakingActive
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToStation > StationStopSnapToleranceMeters)
        {
            train.StationBrakingActive = false;
        }

        var desiredAcceleration = CalculateDesiredAcceleration(train, permitted);
        // 預視此 Tick 原本的加速度指令；一旦進入停站全煞，保持煞車，
        // 避免速度目標暫時放寬時重新牽引而錯過停車點。
        if (isScheduledStop && (train.StationBrakingActive
            || stationStopControl is { RequiresServiceBraking: true }
            || ShouldBeginStationBraking(
                train,
                distanceToStation,
                desiredAcceleration,
                effectiveServiceBraking,
                stationStopControl!.PredictedStoppingDistanceMeters)))
        {
            train.StationBrakingActive = true;
            desiredAcceleration = -effectiveServiceBraking;
        }
        else if (!isScheduledStop)
        {
            train.StationBrakingActive = false;
            train.StationStopViolationRecorded = false;
        }

        if (!enteredStationOvertake && controlLimits.TryGetValue(train.VehicleId, out var protectionLimit)
            && train.Speed > protectionLimit + 0.03)
        {
            desiredAcceleration = -effectiveServiceBraking;
        }

        if (ProfileMode == OperationProfileMode.RealisticOperations)
        {
            var maximumChange = performance.JerkMetersPerSecondCubed * FixedTimeStepSeconds;
            train.Acceleration = BrakingEnvelopeCalculator.MoveToward(
                train.Acceleration,
                desiredAcceleration,
                maximumChange);
        }
        else
        {
            train.Acceleration = desiredAcceleration;
        }

        var previousSpeed = train.Speed;
        var newSpeed = Math.Max(0, train.Speed + train.Acceleration * FixedTimeStepSeconds);
        var hardCurrentLimit = GetTrackCurrentLimit(train, performance);
        newSpeed = Math.Min(newSpeed, hardCurrentLimit + 0.02);
        var traveled = Math.Max(0, (previousSpeed + newSpeed) * 0.5 * FixedTimeStepSeconds);

        if (TryCalculateMovementAuthority(
                train,
                newSpeed,
                train.Acceleration,
                protectionLeaders,
                out var movementAuthority)
            && traveled > movementAuthority + NumericalTolerance)
        {
            AdvanceTrainAlongTraversal(train, Math.Max(0, movementAuthority));
            train.Speed = 0;
            train.Acceleration = 0;
            train.Phase = OperationalPhase.Braking;
            return;
        }

        var pendingFacilityEntryDistance = topologyPassingEntryDistance ?? overtakeEntryDistance;
        if (!enteredStationOvertake
            && pendingFacilityEntryDistance is { } pendingFacilityEntry
            && traveled > pendingFacilityEntry + NumericalTolerance)
        {
            // Advance 在精確端點會正規化成下一条主線 edge；那會令設施入口
            // 被視為已通過。保留同一實體端點的到達 traversal，下一步才能切進路。
            if (topologyPassingEntryDistance.HasValue
                && GetTopologyPassingEntryCursor(train, nextStation, stationInstruction) is { } entryCursor)
                SetTrainRuntimeTopologyCursor(train, GetMovementNavigator(train), entryCursor);
            else
                AdvanceTrainAlongTraversal(train, Math.Max(0, pendingFacilityEntry));
            train.Speed = 0;
            train.Acceleration = 0;
            train.Phase = OperationalPhase.Braking;
            return;
        }

        var remainingAfterMove = distanceToStation - traveled;
        if (isScheduledStop
            && newSpeed <= ArrivalSpeedToleranceMetersPerSecond
            && remainingAfterMove <= StationStopSnapToleranceMeters)
        {
            ArriveAtStation(train, nextStation, resolvedStop);
            return;
        }

        if (isScheduledStop && traveled >= distanceToStation - NumericalTolerance)
        {
            if (!train.StationStopViolationRecorded)
            {
                train.StationStopViolationRecorded = true;
                AddEvent(
                    SimulationEventType.StationStopViolation,
                    train,
                    null,
                    $"{train.ServiceRunId} 抵達 {nextStation.StationId} 停車點時仍有 {newSpeed * 3.6:0.##} km/h；"
                        + "已啟動持續煞車，未將速度直接歸零。",
                    nextStation.PositionMeters,
                    newSpeed);
            }

            // 停點屬於 resolved traversal；不能以顯示里程回推，或在 edge 邊界
            // Advance 到下一段後失去停點所屬 traversal。保留速度，後續繼續煞車。
            var stopNavigator = GetMovementNavigator(train);
            var stopDistance = GetTopologyNavigator(train.Direction).GetDistanceAlongTraversal(
                new TopologyTraversalCursor(GetServiceRouteId(train.Direction), resolvedStop.TraversalIndex, resolvedStop.Position));
            SetTrainRuntimeTopologyCursor(train, stopNavigator, stopNavigator.CreateCursor(
                GetCurrentServiceRouteLegIndex(train), GetMovementTraversalIndex(train, resolvedStop.TraversalIndex), stopDistance));
            train.Speed = newSpeed;
            train.Phase = OperationalPhase.ApproachBraking;
            train.StationBrakingActive = true;
            return;
        }

        if (!isScheduledStop && traveled >= distanceToStation - NumericalTolerance)
        {
            AdvanceTrainAlongTraversal(train, traveled);
            train.Speed = newSpeed;
            PassStation(train, nextStation);
            train.Phase = ClassifyPhase(train, desiredAcceleration, permitted, double.PositiveInfinity);
            return;
        }

        AdvanceTrainAlongTraversal(train, traveled);
        train.Speed = newSpeed;
        train.Phase = ClassifyPhase(train, desiredAcceleration, permitted, distanceToStation);
    }

    private bool TryCalculateMovementAuthority(
        MutableTrain follower,
        double prospectiveSpeed,
        double prospectiveAcceleration,
        IReadOnlyDictionary<string, string> protectionLeaders,
        out double movementAuthority)
    {
        movementAuthority = double.PositiveInfinity;
        if (!protectionLeaders.TryGetValue(follower.VehicleId, out var leaderVehicleId))
        {
            return false;
        }

        var leader = _trains.First(train => train.VehicleId == leaderVehicleId);
        if (!leader.Active
            || leader.Phase == OperationalPhase.OutOfService
            || leader.Direction != follower.Direction)
        {
            return false;
        }

        double actualGap;
        if (UsesTopologyRuntimeSafety(follower) && UsesTopologyRuntimeSafety(leader))
        {
            if (!TryGetTopologySafetyMetrics(follower, leader, out var topologyMetrics))
            {
                return false;
            }

            actualGap = topologyMetrics.ActualGapMeters;
        }
        else
        {
            if (leader.TrackId != follower.TrackId)
            {
                return false;
            }

            var leaderLength = GetVehiclePerformance(leader).LengthMeters;
            var leaderRear = leader.Direction == TrainDirection.Outbound
                ? leader.Position - leaderLength
                : leader.Position + leaderLength;
            actualGap = leader.Direction == TrainDirection.Outbound
                ? leaderRear - follower.Position
                : follower.Position - leaderRear;
        }

        var requiredGap = CalculateDynamicSafetyDistance(
            follower,
            leader,
            prospectiveSpeed,
            prospectiveAcceleration,
            leader.Speed);
        movementAuthority = Math.Max(0, actualGap - requiredGap);
        return true;
    }

    private bool ShouldBeginStationBraking(
        MutableTrain train,
        double distanceToStation,
        double desiredAccelerationIfWaiting,
        double effectiveBraking,
        double? precomputedStoppingDistance = null)
    {
        if (distanceToStation <= StationBrakingLookAheadMeters)
        {
            return true;
        }

        var jerkLimited = ProfileMode == OperationProfileMode.RealisticOperations;
        var performance = GetVehiclePerformance(train);
        var stoppingDistance = precomputedStoppingDistance
            ?? BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
                train.Speed,
                train.Acceleration,
                effectiveBraking,
                performance.JerkMetersPerSecondCubed,
                FixedTimeStepSeconds,
                jerkLimited).DistanceMeters;
        if (stoppingDistance + StationBrakingLookAheadMeters >= distanceToStation)
        {
            return true;
        }

        var previewAcceleration = jerkLimited
            ? BrakingEnvelopeCalculator.MoveToward(
                train.Acceleration,
                desiredAccelerationIfWaiting,
                performance.JerkMetersPerSecondCubed * FixedTimeStepSeconds)
            : desiredAccelerationIfWaiting;
        var previewSpeed = Math.Max(0, train.Speed + previewAcceleration * FixedTimeStepSeconds);
        var previewTravel = Math.Max(0, (train.Speed + previewSpeed) * 0.5 * FixedTimeStepSeconds);
        var previewStoppingDistance = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
            previewSpeed,
            previewAcceleration,
            effectiveBraking,
            performance.JerkMetersPerSecondCubed,
            FixedTimeStepSeconds,
            jerkLimited).DistanceMeters;
        return previewTravel + previewStoppingDistance + StationBrakingLookAheadMeters >= distanceToStation;
    }

    private double CalculateDesiredAcceleration(MutableTrain train, double permittedSpeed)
    {
        var performance = GetVehiclePerformance(train);
        var speedError = permittedSpeed - train.Speed;
        if (speedError < -0.03)
        {
            return -performance.ServiceBrakingMetersPerSecondSquared;
        }

        if (speedError <= 0.12)
        {
            return 0;
        }

        var coastingThreshold = permittedSpeed * (1 - OperationalParameters.CoastingRatio * 0.35);
        if ((_vehicleTypes.Count == 0 || performance.CoastingDecelerationMetersPerSecondSquared > 0)
            && train.Speed >= coastingThreshold)
        {
            return _vehicleTypes.Count == 0
                ? 0
                : -performance.CoastingDecelerationMetersPerSecondSquared;
        }

        var speedRatio = performance.MaxSpeedMetersPerSecond <= 0
            ? 1
            : Math.Clamp(train.Speed / performance.MaxSpeedMetersPerSecond, 0, 1);
        var tractionFactor = 1 - performance.TractionDecayPerSecond * speedRatio;
        return performance.AccelerationMetersPerSecondSquared * Math.Max(0.1, tractionFactor);
    }

    private OperationalPhase ClassifyPhase(
        MutableTrain train,
        double desiredAcceleration,
        double permittedSpeed,
        double distanceToStation)
    {
        if (distanceToStation <= 0.2)
        {
            return OperationalPhase.Arriving;
        }

        var performance = GetVehiclePerformance(train);
        if (performance.CoastingDecelerationMetersPerSecondSquared > 0
            && Math.Abs(desiredAcceleration + performance.CoastingDecelerationMetersPerSecondSquared) <= 0.02)
        {
            return OperationalPhase.Coasting;
        }

        if (train.Acceleration < -0.02)
        {
            return distanceToStation <= OperationalParameters.ApproachDistanceMeters
                ? OperationalPhase.ApproachBraking
                : OperationalPhase.Braking;
        }

        if (Math.Abs(desiredAcceleration) <= 0.02 && train.Speed + 0.15 < permittedSpeed)
        {
            return OperationalPhase.Coasting;
        }

        if (train.Acceleration > 0.02)
        {
            return OperationalPhase.Accelerating;
        }

        return OperationalPhase.Cruising;
    }

    private void ArriveAtStation(MutableTrain train, Station station, ResolvedStop resolvedStop)
    {
        if (train.UsesLegacyVirtualTrack)
        {
            SetTrainPositionFromProjectedCoordinate(train, station.PositionMeters);
        }
        else
        {
            var navigator = GetMovementNavigator(train);
            var topologyNavigator = GetTopologyNavigator(train.Direction);
            var distanceAlongTraversal = topologyNavigator.GetDistanceAlongTraversal(
                new TopologyTraversalCursor(
                    topologyNavigator.ServiceRouteId,
                    resolvedStop.TraversalIndex,
                    resolvedStop.Position));
            SetTrainRuntimeTopologyCursor(
                train,
                navigator,
                navigator.CreateCursor(
                    GetCurrentServiceRouteLegIndex(train),
                    GetMovementTraversalIndex(train, resolvedStop.TraversalIndex),
                    distanceAlongTraversal));
        }
        train.Speed = 0;
        train.StationBrakingActive = false;
        train.StationStopViolationRecorded = false;
        train.CurrentStationIndex = train.NextStationIndex;
        RetainDestinationPlatformOrReleaseRoute(train);
        train.PlatformId = _topologyNativeRuntime
            ? resolvedStop.PlatformId
            : train.ExpectedDestinationPlatformId;
        train.ExpectedDestinationPlatformId = null;
        AssignStationTrack(train, train.PlatformId);
        train.WaitingForOvertakeEventEmitted = false;
        train.Phase = OperationalPhase.Arriving;
        AddEvent(SimulationEventType.Arrival, train, null, $"{train.ServiceRunId} 抵達 {station.StationId}。", train.Position, 0);

        var isTerminal = _topologyNativeRuntime
            ? train.CurrentStationIndex == GetRunRouteContext(train.Direction).TerminalStopIndex
            : train.Direction == TrainDirection.Outbound
                ? train.CurrentStationIndex == Route.Stations.Count - 1
                : train.CurrentStationIndex == 0;
        var isVirtualTurnback = GetStationInstruction(train, station).Mode == StationServiceMode.Turnback;
        if (isTerminal || isVirtualTurnback)
        {
            train.TerminalAction = isVirtualTurnback
                ? TerminalAction.Turnaround
                : _dispatchPlan is not null && !train.ContinueAfterTerminal
                    ? TerminalAction.ExitService
                    : TerminalAction.Turnaround;
            var spatialReference = _topologyNativeRuntime
                ? null
                : Infrastructure.FindSpatialReferencePoint(station.StationId);
            var patternDwellSeconds = GetStationInstruction(train, station).DwellTimeSeconds;
            var stationDwellSeconds = GetConfiguredStationDwellSeconds(
                spatialReference,
                train.Direction,
                patternDwellSeconds ?? station.DwellTimeSeconds);
            train.DwellRemaining = !isVirtualTurnback
                && train.TerminalAction == TerminalAction.Turnaround
                && spatialReference?.Kind == SpatialReferencePointKind.BeforeStationTurnback
                    ? spatialReference.TurnbackDwellSeconds
                    : stationDwellSeconds;
            if (train.DwellRemaining > NumericalTolerance)
            {
                train.Phase = OperationalPhase.Dwelling;
                AddEvent(
                    SimulationEventType.DwellStarted,
                    train,
                    null,
                    isVirtualTurnback
                        ? $"{train.ServiceRunId} 在虛擬折返站 {station.StationId} 停站。"
                        : $"{train.ServiceRunId} 在端點 {station.StationId} 停站／清車。",
                    train.Position,
                    0);
                return;
            }

            CompleteTerminalStationWork(train);
            return;
        }

        var intermediateReference = _topologyNativeRuntime
            ? null
            : Infrastructure.FindSpatialReferencePoint(station.StationId);
        var instructionDwellSeconds = GetStationInstruction(train, station).DwellTimeSeconds;
        train.DwellRemaining = GetConfiguredStationDwellSeconds(
            intermediateReference,
            train.Direction,
            instructionDwellSeconds ?? station.DwellTimeSeconds);
        train.NextStationIndex += GetStationIndexStep(train);
        if (train.DwellRemaining > NumericalTolerance)
        {
            train.Phase = OperationalPhase.Dwelling;
            AddEvent(SimulationEventType.DwellStarted, train, null, $"{station.StationId} 停站。", train.Position, 0);
        }
    }

    private void PassStation(MutableTrain train, Station station)
    {
        var facility = train.OvertakeFacilityId is null
            ? null
            : Infrastructure.StationOvertakeFacilities.FirstOrDefault(item =>
                item.FacilityId.Equals(train.OvertakeFacilityId, StringComparison.OrdinalIgnoreCase));
        train.CurrentStationIndex = train.NextStationIndex;
        RetainDestinationPlatformOrReleaseRoute(train);
        train.PlatformId = facility?.ExpressPlatformId ?? train.ExpectedDestinationPlatformId;
        train.ExpectedDestinationPlatformId = null;
        train.NextStationIndex += GetStationIndexStep(train);
        AddEvent(
            SimulationEventType.StationPassed,
            train,
            null,
            $"{train.ServiceRunId}（{train.ServiceClassId}）通過 {station.StationId}，速度 {train.Speed * 3.6:0.##} km/h。",
            station.PositionMeters,
            train.Speed);

        if (facility is null)
        {
            return;
        }

        if (!TryReserveDepartureRoute(train))
        {
            train.Speed = 0;
            train.Acceleration = 0;
            train.Phase = OperationalPhase.Dwelling;
            train.AwaitingPostPassRoute = true;
            return;
        }

        CompleteStationOvertake(train, facility);
    }

    private void AssignStationTrack(MutableTrain train, string? platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId))
        {
            return;
        }

        if (_topologyNativeRuntime)
        {
            train.TrackId = TopologyInfrastructure.Platforms.TryGetValue(platformId, out var topologyPlatform)
                ? topologyPlatform.TrackEdgeId
                : train.TrackId;
            return;
        }

        var platform = Infrastructure.Platforms.FirstOrDefault(item =>
            item.PlatformId.Equals(platformId, StringComparison.OrdinalIgnoreCase));
        var trackId = platform?.TrackSegmentIds.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(trackId))
        {
            train.TrackId = trackId;
        }
    }

    private void CompleteStationOvertake(
        MutableTrain expressTrain,
        StationOvertakeFacilityDefinition facility)
    {
        // Schema 7 的站內通過線尚未轉成 V4 edge；完成越行、匯回正線後才恢復主線 topology cursor。
        ResumeMainlineTopologyTraversal(expressTrain);
        AddEvent(
            SimulationEventType.OvertakeCompleted,
            expressTrain,
            expressTrain.OvertakenVehicleId,
            $"{expressTrain.ServiceRunId} 已在 {facility.StationId} 站內通過線跨越普通車，並匯回共線正線。",
            expressTrain.Position,
            expressTrain.Speed,
            facility.FacilityId);
        expressTrain.OvertakeFacilityId = null;
        expressTrain.OvertakenVehicleId = null;
        expressTrain.AwaitingPostPassRoute = false;
    }

    private bool CompleteTurnaround(MutableTrain train)
    {
        PrepareTurnaround(train);

        if (CurrentTimeSeconds + NumericalTolerance < train.PlannedDepartureTime)
        {
            return false;
        }

        if (!TryReserveDepartureRoute(train)
            || (MovingBlockMode == MovingBlockMode.Control && !HasDepartureClearance(train)))
        {
            train.Constraints |= OperationalConstraint.RouteResource | OperationalConstraint.Platform;
            return false;
        }

        train.TurnaroundPrepared = false;
        train.ActualDepartureTime = CurrentTimeSeconds;
        train.Phase = OperationalPhase.Accelerating;
        var delay = Math.Max(0, CurrentTimeSeconds - train.PlannedDepartureTime);
        if (delay > NumericalTolerance)
        {
            AddEvent(
                SimulationEventType.DepartureDelayed,
                train,
                null,
                $"{train.ServiceRunId} 接續發車延後 {delay:0.0} 秒。",
                train.Position,
                0,
                delaySeconds: delay);
        }
        AddEvent(
            SimulationEventType.DirectionChanged,
            train,
            null,
            $"車輛 {train.VehicleId} 折返，開始新車次 {train.ServiceRunId}。",
            train.Position,
            0);
        AddEvent(SimulationEventType.Departure, train, null, $"{train.ServiceRunId} 發車。", train.Position, 0);
        return true;
    }

    private void PrepareTurnaround(
        MutableTrain train,
        TurnbackOperationDefinition? topologyTurnback = null)
    {
        if (train.TurnaroundPrepared)
        {
            return;
        }

        train.Direction = topologyTurnback is null
            ? train.Direction == TrainDirection.Outbound
                ? TrainDirection.Inbound
                : TrainDirection.Outbound
            : GetDirectionForServiceRoute(topologyTurnback.DepartureServiceRouteId);
        train.TrackId = train.Direction == TrainDirection.Outbound ? "DOWN" : "UP";
        if (!train.UsesLegacyVirtualTrack && train.TopologyTurnbackMovement is null)
        {
            SetTrainPositionFromProjectedCoordinate(train, train.Position);
        }
        train.ServiceNumber++;
        train.PlannedDepartureTime = CurrentTimeSeconds;
        if (_dispatchPlan is null)
        {
            ApplyServicePlan(train);
        }
        else if (train.ContinuationServiceRunId is { } targetRunId
            && _dispatchRunsById.TryGetValue(targetRunId, out var targetRun))
        {
            train.ExplicitServiceRunId = targetRun.ServiceRunId;
            train.DispatchServiceRunBaseId = targetRun.ServiceRunId;
            train.ServiceClassId = targetRun.ServiceTypeId;
            train.PatternId = targetRun.StopPatternId;
            train.VehicleTypeId = targetRun.VehicleTypeId.Trim();
            train.PlatformId = targetRun.OriginPlatformId;
            train.PlannedDepartureTime = RelativeScheduleSeconds(
                targetRun.PlannedDepartureTime,
                _dispatchPlan.ScheduleAnchorTime);
            train.ContinueAfterTerminal = targetRun.ContinueAfterTerminal;
            train.ContinuationServiceRunId = targetRun.ContinuationServiceRunId;
        }
        else
        {
            train.ExplicitServiceRunId = $"{train.DispatchServiceRunBaseId}-R{train.ServiceNumber - 1:000}";
            train.PlatformId = null;
            train.PlannedDepartureTime = CurrentTimeSeconds;
        }

        if (topologyTurnback is not null)
        {
            train.PlatformId = GetTopologyDeparturePlatform(topologyTurnback);
        }
        else
        {
            // 站後尾軌折返回站時，月台應由實體折返拓撲決定，而不是沿用到達側或
            // 被未指定的自動派車覆蓋。這也讓後續出站進路以反方向月台為起點預約。
            var station = GetRuntimeStation(train, train.CurrentStationIndex);
            var detailedTurnback = Infrastructure.TurnbackPlans.FirstOrDefault(plan =>
                plan.Kind == TurnbackKind.AfterStation
                && plan.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(detailedTurnback?.DeparturePlatformId))
            {
                train.PlatformId = detailedTurnback.DeparturePlatformId;
            }
        }

        if (_topologyNativeRuntime)
        {
            // 兩條 ServiceRoute 都以各自行進方向的 stop order 編號；折返後不能保留
            // 到達 route 的 terminal index，否則會把 outbound 的索引誤當 inbound 的索引。
            train.CurrentStationIndex = topologyTurnback is null
                ? GetRunRouteContext(train.Direction).OriginStopIndex
                : GetTopologyDepartureStationIndex(topologyTurnback);
        }
        train.NextStationIndex = train.CurrentStationIndex + GetStationIndexStep(train);
        train.StationBrakingActive = false;
        train.StationStopViolationRecorded = false;
        train.TurnaroundPrepared = true;
    }

    private void CompleteTerminalStationWork(MutableTrain train)
    {
        var action = train.TerminalAction;
        if (action == TerminalAction.ExitService)
        {
            ReleaseRouteReservation(train);
            train.TerminalAction = TerminalAction.None;
            train.Speed = 0;
            train.Acceleration = 0;
            train.Phase = OperationalPhase.OutOfService;
            train.Active = false;
            train.Completed = true;
            AddEvent(
                SimulationEventType.ServiceEnded,
                train,
                null,
                $"{train.ServiceRunId} 完成端點停站／清車並退出營運。",
                train.Position,
                0);
            return;
        }

        var station = GetRuntimeStation(train, train.CurrentStationIndex);
        var topologyTurnback = FindTopologyTurnbackOperation(train);
        if (topologyTurnback is not null)
        {
            if (TryStartTopologyTurnbackMovement(train, topologyTurnback))
            {
                train.TerminalAction = TerminalAction.None;
            }

            return;
        }

        if (_topologyNativeRuntime)
        {
            throw new SimulationValidationException([
                $"topology-native 列車 {train.ServiceRunId} 抵達 {station.StationId} 後找不到實體 TurnbackOperation；不得回退至 legacy virtual turnback。"
            ]);
        }

        var spatialReference = Infrastructure.FindSpatialReferencePoint(station.StationId);
        var detailedTurnback = Infrastructure.TurnbackPlans.FirstOrDefault(plan =>
            plan.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase)
            && plan.Kind != TurnbackKind.LegacyAbstract);
        if (!TryReserveTurnbackResources(train, station, spatialReference, detailedTurnback))
        {
            return;
        }

        train.TerminalAction = TerminalAction.None;
        if (TryStartAfterStationTailTrackMovement(train, spatialReference, detailedTurnback))
        {
            return;
        }

        if (TryStartSpatialTurnbackMovement(train, spatialReference))
        {
            return;
        }

        train.TurnaroundRemaining = spatialReference?.IsTurnback == true
            ? spatialReference.AdditionalTurnbackSeconds + CalculateSpatialTurnbackTravelSeconds(train, spatialReference)
            : detailedTurnback?.TurnbackTimeSeconds
                ?? (train.Direction == TrainDirection.Outbound
                    ? TrainParameters.TerminalTurnaroundTimeSeconds
                    : TrainParameters.OriginTurnaroundTimeSeconds);
        train.Phase = OperationalPhase.Turning;
        AddEvent(
            SimulationEventType.TurnaroundStarted,
            train,
            null,
            $"{train.ServiceRunId} 完成端點作業並開始折返。",
            train.Position,
            0);
        if (train.TurnaroundRemaining <= NumericalTolerance)
        {
            CompleteTurnaround(train);
        }
    }

    private bool TryReserveTurnbackResources(
        MutableTrain train,
        Station station,
        SpatialReferencePointDefinition? spatialReference,
        TurnbackPlanDefinition? detailedTurnback)
    {
        var resources = new List<string>();
        if (!string.IsNullOrWhiteSpace(train.PlatformId))
        {
            resources.Add($"PLATFORM:{train.PlatformId}");
        }

        resources.AddRange(detailedTurnback?.ResourceIds ?? Enumerable.Empty<string>());
        if (!string.IsNullOrWhiteSpace(detailedTurnback?.ArrivalPlatformId))
        {
            resources.Add($"PLATFORM:{detailedTurnback.ArrivalPlatformId}");
        }
        if (!string.IsNullOrWhiteSpace(detailedTurnback?.DeparturePlatformId))
        {
            resources.Add($"PLATFORM:{detailedTurnback.DeparturePlatformId}");
        }
        var tailTrackLayout = detailedTurnback is null
            ? null
            : Infrastructure.FindAfterStationTailTrackLayout(detailedTurnback);
        if (tailTrackLayout is not null)
        {
            foreach (var tailTrack in new[] { tailTrackLayout.OutboundTrack, tailTrackLayout.InboundTrack })
            {
                resources.Add($"TRACK:{tailTrack.TrackId}");
                resources.AddRange(tailTrack.ConflictResourceIds);
            }
        }
        if ((spatialReference?.Kind is SpatialReferencePointKind.BeforeStationTurnback
             && spatialReference.AlternateBerthing)
            || spatialReference?.Kind == SpatialReferencePointKind.CentralSidingTurnback)
        {
            resources.Add($"TURNBACK:{station.StationId}");
        }

        if (resources.Count == 0)
        {
            return true;
        }

        var reservationId = train.ReservationId
            ?? $"{train.VehicleId}|{train.ServiceRunId}|{train.CurrentStationIndex}|TURNBACK";
        if (!_resourceReservations.Reserve(reservationId, resources))
        {
            return MarkWaitingForResource(
                train,
                $"{station.StationId} 的折返資源正由其他列車使用。",
                $"TURNBACK:{station.StationId}");
        }

        train.ReservationId = reservationId;
        train.RoutePathId = null;
        train.HasDestinationPlatformReservation = false;
        train.HasStationReservation = true;
        train.WaitingResourceEventEmitted = false;
        AddEvent(
            SimulationEventType.RouteReserved,
            train,
            null,
            $"{train.ServiceRunId} 已鎖定 {station.StationId} 的折返資源。",
            train.Position,
            train.Speed,
            $"TURNBACK:{station.StationId}",
            resourceIds: _resourceReservations.GetResources(reservationId));
        return true;
    }

    /// <summary>
    /// Schema 8 的折返作業只由目前 service route 與實體 facility 定義選擇；不讀
    /// Route position、SpatialReferencePoint 或 virtual track。
    /// </summary>
    private TurnbackOperationDefinition? FindTopologyTurnbackOperation(MutableTrain train)
    {
        if (!_topologyNativeRuntime || train.TopologyTurnbackMovement is not null)
        {
            return null;
        }

        var serviceRouteId = GetServiceRouteId(train.Direction);
        var cursor = train.RuntimeTopologyCursor
            ?? throw new SimulationValidationException(["topology 折返列車缺少 runtime cursor。"]);
        var matches = TopologyInfrastructure.TurnbackOperations.Values
            .Where(operation => operation.ArrivalServiceRouteId.Equals(serviceRouteId, StringComparison.OrdinalIgnoreCase)
                && TopologyInfrastructure.TurnbackFacilities[operation.FacilityId].ArrivalTrackEdgeId
                    .Equals(cursor.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(ResolveFacilityPlatformHead(train,
                    TopologyInfrastructure.TurnbackFacilities[operation.FacilityId].ArrivalTrackEdgeId,
                    TopologyInfrastructure.TurnbackFacilities[operation.FacilityId].ArrivalStopOffsetMeters,
                    GetMovementNavigator(train).GetTraversal(cursor.MovementLegIndex, cursor.TraversalIndex).Direction).OffsetMeters
                    - cursor.Position.OffsetMeters) <= ArrivalPositionToleranceMeters)
            .OrderBy(operation => operation.OperationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new SimulationValidationException([
                $"營運路線「{serviceRouteId}」有多個可用 topology 折返作業；目前必須由 station operation 明確選擇。"
            ]);
        }

        return matches.SingleOrDefault();
    }

    private bool TryStartTopologyTurnbackMovement(
        MutableTrain train,
        TurnbackOperationDefinition operation)
    {
        var facility = TopologyInfrastructure.TurnbackFacilities[operation.FacilityId];
        var arrivalNavigator = GetMovementNavigator(train);
        var arrivalCursor = train.RuntimeTopologyCursor
            ?? throw new SimulationValidationException(["topology 折返列車缺少 runtime cursor。"]);
        var arrivalTraversal = arrivalNavigator.GetTraversal(
            arrivalCursor.MovementLegIndex,
            arrivalCursor.TraversalIndex);
        var expectedArrival = ResolveFacilityPlatformHead(train, facility.ArrivalTrackEdgeId, facility.ArrivalStopOffsetMeters, arrivalTraversal.Direction);
        if (!arrivalCursor.Position.ApproximatelyEquals(expectedArrival, ArrivalPositionToleranceMeters))
        {
            throw new SimulationValidationException([
                $"折返作業「{operation.OperationId}」要求從 {expectedArrival} 進入設施，但列車目前位於 {arrivalCursor.Position}。"
            ]);
        }

        var facilityLeg = TopologyMovementPlanResolver.ResolveFacilityLeg(TopologyInfrastructure, facility);
        var facilityEntryNode = InfrastructureValidator.GetStartNode(
            facilityLeg.Traversals[0].Edge,
            facilityLeg.Traversals[0].Direction);
        var arrivalExitNode = InfrastructureValidator.GetEndNode(arrivalTraversal.Edge, arrivalTraversal.Direction);
        if (!arrivalExitNode.Equals(facilityEntryNode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SimulationValidationException([
                $"折返作業「{operation.OperationId}」從 {arrivalTraversal.Edge.TrackEdgeId} 到 {facilityLeg.Traversals[0].Edge.TrackEdgeId} 不連續，不能 runtime teleport。"
            ]);
        }

        var departureDirection = GetDirectionForServiceRoute(operation.DepartureServiceRouteId);
        var departureRoute = GetServiceRouteDefinition(departureDirection);
        if (!departureRoute.ServiceRouteId.Equals(operation.DepartureServiceRouteId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SimulationValidationException([
                $"折返作業「{operation.OperationId}」出發營運路線「{operation.DepartureServiceRouteId}」不是目前 direction binding。"
            ]);
        }

        var arrivalRoute = GetServiceRouteDefinition(train.Direction);
        var departureStartTraversalIndex = departureRoute.Traversals
            .Select((traversal, index) => (traversal, index))
            .FirstOrDefault(item => item.traversal.TrackEdgeId.Equals(
                facility.DepartureTrackEdgeId,
                StringComparison.OrdinalIgnoreCase))
            .index;
        if (!departureRoute.Traversals.Any(traversal => traversal.TrackEdgeId.Equals(
                facility.DepartureTrackEdgeId,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new SimulationValidationException([
                $"折返設施「{facility.FacilityId}」的 DepartureTrackEdge「{facility.DepartureTrackEdgeId}」不在出發營運路線「{departureRoute.ServiceRouteId}」。"
            ]);
        }
        var arrivalLeg = new ResolvedMovementLeg
        {
            LegId = $"{arrivalRoute.ServiceRouteId}:ARRIVAL",
            Kind = MovementLegKind.ServiceRoute,
            // 中間站袋狀軌折返只需要保留到達 stop 前的 route prefix；若把整條 route
            // 都塞進 leg，M 站的 facility 會被錯接到後續 E 站而形成 teleport。
            Traversals = TopologyMovementPlanResolver.ResolveTraversals(
                TopologyInfrastructure,
                arrivalRoute.Traversals.Take(arrivalCursor.TraversalIndex + 1))
        };
        var departureLeg = new ResolvedMovementLeg
        {
            LegId = $"{departureRoute.ServiceRouteId}:DEPARTURE",
            Kind = MovementLegKind.ServiceRoute,
            Traversals = TopologyMovementPlanResolver.ResolveTraversals(
                TopologyInfrastructure,
                departureRoute.Traversals.Skip(departureStartTraversalIndex))
        };
        var navigator = new TopologyMovementNavigator(
            TopologyInfrastructure,
            new ResolvedMovementPlan
            {
                MovementPlanId = $"{train.VehicleId}:{operation.OperationId}:{train.ServiceRunId}",
                Legs = [arrivalLeg, facilityLeg, departureLeg]
            });
        var resources = TraversalResourceResolver.GetRequiredResources(facilityLeg).ToArray();
        var reservationId = resources.Length == 0
            ? null
            : $"TOPOLOGY:{train.VehicleId}|{operation.OperationId}|{train.ServiceRunId}";
        if (reservationId is not null && !_resourceReservations.Reserve(reservationId, resources))
        {
            return MarkWaitingForResource(
                train,
                $"{facility.Name} 的 topology 衝突資源正由其他列車使用。",
                facility.FacilityId);
        }

        var arrivalDistance = arrivalNavigator.GetDistanceAlongTraversal(arrivalCursor);
        var transitionCursor = navigator.CreateCursor(0, arrivalCursor.TraversalIndex, arrivalDistance);
        var stationary = PlatformTurnback.IsStationary(facility);
        var turnbackStopCursor = stationary ? transitionCursor : CreateTopologyTurnbackStopCursor(navigator, facilityLegIndex: 1, facility);
        var returnStartCursor = CreateTopologyTurnbackReturnStartCursor(navigator, stationary ? 2 : 1,
            turnbackStopCursor, GetVehiclePerformance(train).LengthMeters, stationary);
        train.TopologyTurnbackMovement = new TopologyTurnbackMovement(
            operation,
            facility,
            navigator,
            facilityLegIndex: 1,
            departureLegIndex: 2,
            turnbackStopCursor,
            returnStartCursor,
            operation.MinimumDwellTimeSeconds,
            reservationId,
            resources,
            train.Position,
            (int)train.Direction,
            navigator.GetDistanceFromStart(transitionCursor),
            departureStartTraversalIndex);
        train.ReservationId = reservationId;
        train.RoutePathId = null;
        train.HasDestinationPlatformReservation = false;
        train.HasStationReservation = reservationId is not null;
        train.TrackId = $"TOPOLOGY:{facility.FacilityId}";
        train.Speed = 0;
        train.Acceleration = 0;
        SetTrainRuntimeTopologyCursor(train, navigator, transitionCursor);
        AddEvent(
            SimulationEventType.RouteReserved,
            train,
            null,
            $"{train.ServiceRunId} 已鎖定 topology 折返設施 {facility.FacilityId}。",
            train.Position,
            0,
            facility.FacilityId,
            resourceIds: resources);
        AddEvent(
            SimulationEventType.TurnaroundStarted,
            train,
            null,
            $"{train.ServiceRunId} 由實體 edge traversal 進入 {facility.Name}。",
            train.Position,
            0,
            facility.FacilityId);
        return true;
    }

    private static RuntimeTopologyCursor CreateTopologyTurnbackStopCursor(
        TopologyMovementNavigator navigator,
        int facilityLegIndex,
        TurnbackFacilityDefinition facility)
    {
        if (facility.TurnbackStopPosition is { } position)
        {
            var match = navigator.Legs[facilityLegIndex].Traversals
                .Select((traversal, index) => (traversal, index))
                .FirstOrDefault(item => item.traversal.Edge.TrackEdgeId.Equals(
                    position.TrackEdgeId, StringComparison.OrdinalIgnoreCase));
            if (match.traversal is null)
            {
                throw new SimulationValidationException([
                    $"折返設施「{facility.FacilityId}」的實體停等位置「{position}」不在 movement plan。"
                ]);
            }
            var distance = match.traversal.Direction == TraversalDirection.Forward
                ? position.OffsetMeters
                : match.traversal.LengthMeters - position.OffsetMeters;
            return navigator.CreateCursor(facilityLegIndex, match.index, distance);
        }

        if (facility.TurnbackStopAfterTraversalIndex is { } explicitIndex)
        {
            return navigator.CreateCursor(
                facilityLegIndex,
                explicitIndex,
                navigator.GetTraversal(facilityLegIndex, explicitIndex).LengthMeters);
        }

        var fallbackIndex = facility.Traversals
            .Select((traversal, index) => (traversal, index))
            .FirstOrDefault(item => item.index < facility.Traversals.Count - 1)
            .index;
        return navigator.CreateCursor(
            facilityLegIndex,
            fallbackIndex,
            navigator.GetTraversal(facilityLegIndex, fallbackIndex).LengthMeters);
    }

    private static RuntimeTopologyCursor? CreateTopologyTurnbackReturnStartCursor(
        TopologyMovementNavigator navigator,
        int facilityLegIndex,
        RuntimeTopologyCursor turnbackStopCursor,
        double trainLengthMeters,
        bool stationary)
    {
        // 換端後行進車頭是原車尾；保持整列車的實體占用範圍，不沿用原車頭 offset。
        var rear = navigator.CreateFootprint(turnbackStopCursor, trainLengthMeters).Rear;
        // Retreat 在恰好落於節點時保留前一 traversal 的尾端表示；換端需要
        // 後一實際被占用 traversal 的起點表示，兩者是同一節點、沒有位移。
        while (navigator.GetTraversal(rear.MovementLegIndex, rear.TraversalIndex).LengthMeters
            - navigator.GetDistanceAlongTraversal(rear) <= TrackPosition.DefaultToleranceMeters)
        {
            var leg = rear.MovementLegIndex;
            var next = rear.TraversalIndex + 1;
            if (next >= navigator.Legs[leg].Traversals.Count) { leg++; next = 0; }
            if (leg >= navigator.Legs.Count) break;
            rear = navigator.CreateCursor(leg, next, 0);
        }
        var rearTraversal = navigator.GetTraversal(rear.MovementLegIndex, rear.TraversalIndex);
        var start = stationary ? 0 : turnbackStopCursor.TraversalIndex + 1;
        for (var i = start; i < navigator.Legs[facilityLegIndex].Traversals.Count; i++)
        {
            var traversal = navigator.GetTraversal(facilityLegIndex, i);
            if (traversal.Edge.TrackEdgeId.Equals(rear.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase)
                && traversal.Direction != rearTraversal.Direction)
                return navigator.CreateCursor(facilityLegIndex, i, traversal.Direction == TraversalDirection.Forward
                    ? rear.Position.OffsetMeters : traversal.LengthMeters - rear.Position.OffsetMeters);
        }
        throw new SimulationValidationException(["折返返回進路必須反向涵蓋整列車的車尾位置，不能以相同車頭 offset 換端。"]);
    }

    private void UpdateTopologyTurnbackMovement(MutableTrain train)
    {
        var movement = train.TopologyTurnbackMovement
            ?? throw new InvalidOperationException("topology 折返狀態遺失。");
        var navigator = movement.Navigator;
        if (movement.Stage == TopologyTurnbackStage.WaitingAtTurnback)
        {
            movement.WaitRemainingSeconds = Math.Max(0, movement.WaitRemainingSeconds - FixedTimeStepSeconds);
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train, train.Acceleration);
            train.Phase = OperationalPhase.Turning;
            if (movement.WaitRemainingSeconds > NumericalTolerance)
            {
                return;
            }

            PrepareTurnaround(train, movement.Operation);
            if (movement.ReturnStartCursor is { } returnStartCursor)
            {
                SetTrainRuntimeTopologyCursor(train, navigator, returnStartCursor);
            }
            movement.Stage = TopologyTurnbackStage.ReturningToDeparture;
            movement.BrakingActive = false;
            AddEvent(
                SimulationEventType.TailTrackReturnStarted,
                train,
                null,
                $"{train.ServiceRunId} 完成 {movement.Facility.Name} 停等，依 topology traversal 駛回出發月台。",
                train.Position,
                0,
                movement.Facility.FacilityId);
            return;
        }

        var target = movement.Stage == TopologyTurnbackStage.RunningToTurnback
            ? movement.TurnbackStopCursor
            : PlatformTurnback.IsStationary(movement.Facility) ? movement.ReturnStartCursor!.Value
            : CreateTopologyDepartureStopCursor(train, movement);
        var cursor = train.RuntimeTopologyCursor!.Value;
        var distanceToTarget = navigator.TryGetForwardDistance(cursor, target)
            ?? throw new SimulationValidationException(["topology 折返 movement target 位於列車 cursor 後方。"]);
        if (train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToTarget <= StationStopSnapToleranceMeters)
        {
            CompleteTopologyTurnbackLeg(train, movement, target);
            return;
        }

        if (movement.BrakingActive
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToTarget > StationStopSnapToleranceMeters)
        {
            movement.BrakingActive = false;
        }

        var performance = GetVehiclePerformance(train);
        var edgeLimit = navigator.GetTraversal(cursor.MovementLegIndex, cursor.TraversalIndex).Edge.DefaultSpeedLimitMetersPerSecond;
        var permitted = Math.Min(performance.MaxSpeedMetersPerSecond, edgeLimit);
        var desiredAcceleration = CalculateDesiredAcceleration(train, permitted);
        if (movement.BrakingActive
            || ShouldBeginStationBraking(
                train,
                distanceToTarget,
                desiredAcceleration,
                performance.ServiceBrakingMetersPerSecondSquared))
        {
            movement.BrakingActive = true;
            desiredAcceleration = -performance.ServiceBrakingMetersPerSecondSquared;
        }

        train.Acceleration = ProfileMode == OperationProfileMode.RealisticOperations
            ? BrakingEnvelopeCalculator.MoveToward(
                train.Acceleration,
                desiredAcceleration,
                performance.JerkMetersPerSecondCubed * FixedTimeStepSeconds)
            : desiredAcceleration;
        var previousSpeed = train.Speed;
        var newSpeed = Math.Min(
            Math.Max(0, train.Speed + train.Acceleration * FixedTimeStepSeconds),
            permitted + 0.02);
        var traveled = Math.Max(0, (previousSpeed + newSpeed) * 0.5 * FixedTimeStepSeconds);
        if (newSpeed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToTarget - traveled <= StationStopSnapToleranceMeters)
        {
            CompleteTopologyTurnbackLeg(train, movement, target);
            return;
        }

        var advance = traveled >= distanceToTarget - NumericalTolerance
            ? Math.Max(0, distanceToTarget - NumericalTolerance)
            : traveled;
        SetTrainRuntimeTopologyCursor(train, navigator, navigator.Advance(cursor, advance));
        train.Speed = newSpeed;
        train.Phase = movement.Stage == TopologyTurnbackStage.RunningToTurnback
            ? OperationalPhase.TailTrackOutbound
            : OperationalPhase.TailTrackReturn;
        movement.BrakingActive |= traveled >= distanceToTarget - NumericalTolerance;
    }

    private RuntimeTopologyCursor CreateTopologyDepartureStopCursor(MutableTrain train, TopologyTurnbackMovement movement)
    {
        var departure = movement.Navigator.GetTraversal(movement.DepartureLegIndex, 0);
        var head = ResolveFacilityPlatformHead(train, movement.Facility.DepartureTrackEdgeId,
            movement.Facility.DepartureStartOffsetMeters, departure.Direction);
        var distance = departure.Direction == TraversalDirection.Forward
            ? head.OffsetMeters
            : departure.LengthMeters - head.OffsetMeters;
        // 只指定連續 movement 的目的地；Update 仍逐 0.1 秒 Advance，不能跳到此點。
        return movement.Navigator.CreateCursor(movement.DepartureLegIndex, 0, distance);
    }

    private void CompleteTopologyTurnbackLeg(
        MutableTrain train,
        TopologyTurnbackMovement movement,
        RuntimeTopologyCursor target)
    {
        SetTrainRuntimeTopologyCursor(train, movement.Navigator, target);
        train.Speed = 0;
        train.Acceleration = 0;
        movement.BrakingActive = false;
        if (movement.Stage == TopologyTurnbackStage.RunningToTurnback)
        {
            movement.Stage = TopologyTurnbackStage.WaitingAtTurnback;
            train.Phase = OperationalPhase.Turning;
            AddEvent(
                SimulationEventType.TailTrackReached,
                train,
                null,
                $"{train.ServiceRunId} 抵達 {movement.Facility.Name} 的實體折返位置，開始停等。",
                train.Position,
                0,
                movement.Facility.FacilityId);
            return;
        }

        if (PlatformTurnback.IsStationary(movement.Facility))
        {
            SetTrainRuntimeTopologyCursor(train, movement.Navigator, movement.ReturnStartCursor!.Value);
        }
        movement.IsOperating = false;
        train.PlatformId = GetTopologyDeparturePlatform(movement.Operation);
        train.TrackId = $"EDGE:{GetServiceRouteId(train.Direction)}";
        AddEvent(
            SimulationEventType.Arrival,
            train,
            null,
            $"{train.ServiceRunId} 已沿 {movement.Facility.Name} 返回出發 service route。",
            train.Position,
            0,
            train.TrackId);

        // 尾軌返回後，列車已到達反方向終點月台（例如 E/P-E-U），
        // 必須先完成該側正常停站，再依接續車次的計畫時間發車。
        // 折返設施 movement 仍保留到車尾淨空，故使用獨立旗標銜接
        // CompleteTurnaround，不能把 TerminalAction 再設回 Turnaround，
        // 否則 CompleteTerminalStationWork 會重新尋找同一個折返作業。
        var departureStation = GetRuntimeStation(train, train.CurrentStationIndex);
        var departurePatternDwell = GetStationInstruction(train, departureStation).DwellTimeSeconds;
        train.DwellRemaining = GetConfiguredStationDwellSeconds(
            null,
            train.Direction,
            departurePatternDwell ?? departureStation.DwellTimeSeconds);
        train.AwaitingTopologyTurnbackDeparture = true;
        if (train.DwellRemaining > NumericalTolerance)
        {
            train.Phase = OperationalPhase.Dwelling;
            AddEvent(
                SimulationEventType.DwellStarted,
                train,
                null,
                $"{train.ServiceRunId} 返回反方向終點月台 {departureStation.StationId} 停站。",
                train.Position,
                0);
            return;
        }

        train.Phase = OperationalPhase.Turning;
        if (CompleteTurnaround(train))
        {
            train.AwaitingTopologyTurnbackDeparture = false;
        }
    }

    private void TryReleaseCompletedTopologyTurnbackResources(MutableTrain train)
    {
        var movement = train.TopologyTurnbackMovement;
        if (movement is null || movement.IsOperating || train.RuntimeTopologyCursor is not { } cursor
            || cursor.MovementLegIndex < movement.DepartureLegIndex)
        {
            return;
        }

        var footprint = movement.Navigator.CreateFootprint(cursor, GetVehiclePerformance(train).LengthMeters);
        var facilityEdges = movement.Navigator.Legs[movement.FacilityLegIndex].Traversals
            .Select(traversal => traversal.Edge.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (footprint.OccupiedIntervals.Any(interval => facilityEdges.Contains(interval.TrackEdgeId)))
        {
            return;
        }

        if (movement.ReservationId is not null)
        {
            ReleaseRouteReservation(train);
        }

        var mainlineNavigator = GetMainlineMovementNavigator(train.Direction);
        var distanceAlongTraversal = movement.Navigator.GetDistanceAlongTraversal(cursor);
        var serviceRouteTraversalIndex = cursor.TraversalIndex + movement.DepartureServiceRouteTraversalOffset;
        train.TopologyTurnbackMovement = null;
        SetTrainRuntimeTopologyCursor(
            train,
            mainlineNavigator,
            mainlineNavigator.CreateCursor(0, serviceRouteTraversalIndex, distanceAlongTraversal));
    }

    private TopologyPassingCandidate? FindEligibleTopologyPassing(
        MutableTrain expressTrain,
        Station nextStation,
        StationServiceInstruction instruction)
    {
        if (!_topologyNativeRuntime
            || instruction.Mode != StationServiceMode.Pass
            || !HasTopologyTraversalPosition(expressTrain))
        {
            return null;
        }

        var serviceRouteId = GetServiceRouteId(expressTrain.Direction);
        foreach (var operation in TopologyInfrastructure.PassingOperations.Values
                     .Where(candidate => candidate.ServiceRouteId.Equals(serviceRouteId, StringComparison.OrdinalIgnoreCase)
                         && (string.IsNullOrWhiteSpace(candidate.ExpressServiceTypeId)
                             || candidate.ExpressServiceTypeId.Equals(expressTrain.ServiceClassId, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(candidate => candidate.OperationId, StringComparer.OrdinalIgnoreCase))
        {
            var facility = TopologyInfrastructure.PassingFacilities[operation.FacilityId];
            if (!facility.StationId.Equals(nextStation.StationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localTrain = _trains
                .Where(candidate => !ReferenceEquals(candidate, expressTrain)
                    && candidate.Active
                    && candidate.Phase == OperationalPhase.Dwelling
                    && candidate.Direction == expressTrain.Direction
                    && candidate.CurrentStationIndex == expressTrain.NextStationIndex
                    && facility.LocalPlatformId.Equals(candidate.PlatformId, StringComparison.OrdinalIgnoreCase)
                    && GetStationInstruction(candidate, nextStation).Mode != StationServiceMode.Pass)
                .OrderBy(candidate => candidate.VehicleId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (localTrain is not null)
            {
                return new TopologyPassingCandidate(operation, facility, localTrain);
            }
        }

        return null;
    }

    private double? GetTopologyPassingEntryDistance(
        MutableTrain train,
        Station nextStation,
        StationServiceInstruction instruction)
    {
        var target = GetTopologyPassingEntryCursor(train, nextStation, instruction);
        return target is { } cursor
            ? GetMovementNavigator(train).TryGetForwardDistance(train.RuntimeTopologyCursor!.Value, cursor)
            : null;
    }

    private RuntimeTopologyCursor? GetTopologyPassingEntryCursor(
        MutableTrain train,
        Station nextStation,
        StationServiceInstruction instruction)
    {
        var candidate = FindEligibleTopologyPassing(train, nextStation, instruction);
        if (candidate is null)
        {
            return null;
        }

        var facility = candidate.Facility;
        var navigator = GetMovementNavigator(train);
        var cursor = train.RuntimeTopologyCursor!.Value;
        if (cursor.MovementLegIndex != 0)
        {
            return null;
        }

        var routeNavigator = GetTopologyNavigator(train.Direction);
        var traversalIndex = GetServiceRouteDefinition(train.Direction).Traversals
            .Select((traversal, index) => (traversal, index))
            .Single(item => item.traversal.TrackEdgeId.Equals(
                facility.ArrivalTrackEdgeId,
                StringComparison.OrdinalIgnoreCase))
            .index;
        var target = navigator.CreateCursor(
            0,
            traversalIndex,
            routeNavigator.GetDistanceAlongTraversal(new TopologyTraversalCursor(
                routeNavigator.ServiceRouteId,
                traversalIndex,
                new TrackPosition(facility.ArrivalTrackEdgeId, facility.ArrivalOffsetMeters))));
        return target;
    }

    private bool TryEnterTopologyPassingMovement(
        MutableTrain train,
        Station nextStation,
        StationServiceInstruction instruction)
    {
        var candidate = FindEligibleTopologyPassing(train, nextStation, instruction);
        if (candidate is null)
        {
            return false;
        }

        var distanceToEntry = GetTopologyPassingEntryDistance(train, nextStation, instruction);
        if (distanceToEntry is null || distanceToEntry > ArrivalPositionToleranceMeters)
        {
            return false;
        }

        var facility = candidate.Facility;
        var arrivalNavigator = GetMovementNavigator(train);
        var arrivalCursor = train.RuntimeTopologyCursor!.Value;
        var arrivalTraversal = arrivalNavigator.GetTraversal(
            arrivalCursor.MovementLegIndex,
            arrivalCursor.TraversalIndex);
        var expectedEntry = new TrackPosition(facility.ArrivalTrackEdgeId, facility.ArrivalOffsetMeters);
        if (!arrivalCursor.Position.ApproximatelyEquals(expectedEntry, ArrivalPositionToleranceMeters))
        {
            return false;
        }

        var facilityLeg = TopologyMovementPlanResolver.ResolvePassingLeg(TopologyInfrastructure, facility);
        var entryNode = InfrastructureValidator.GetStartNode(
            facilityLeg.Traversals[0].Edge,
            facilityLeg.Traversals[0].Direction);
        var arrivalExitNode = InfrastructureValidator.GetEndNode(arrivalTraversal.Edge, arrivalTraversal.Direction);
        if (!arrivalExitNode.Equals(entryNode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SimulationValidationException([
                $"越行作業「{candidate.Operation.OperationId}」從 {arrivalTraversal.Edge.TrackEdgeId} 到 {facilityLeg.Traversals[0].Edge.TrackEdgeId} 不連續，不能 runtime teleport。"
            ]);
        }

        var route = GetServiceRouteDefinition(train.Direction);
        var departureStartTraversalIndex = route.Traversals
            .Select((traversal, index) => (traversal, index))
            .Single(item => item.traversal.TrackEdgeId.Equals(
                facility.DepartureTrackEdgeId,
                StringComparison.OrdinalIgnoreCase))
            .index;
        var arrivalLeg = new ResolvedMovementLeg
        {
            LegId = $"{route.ServiceRouteId}:PASSING-ARRIVAL",
            Kind = MovementLegKind.ServiceRoute,
            Traversals = TopologyMovementPlanResolver.ResolveTraversals(
                TopologyInfrastructure,
                route.Traversals.Take(arrivalCursor.TraversalIndex + 1))
        };
        var departureLeg = new ResolvedMovementLeg
        {
            LegId = $"{route.ServiceRouteId}:PASSING-DEPARTURE",
            Kind = MovementLegKind.ServiceRoute,
            Traversals = TopologyMovementPlanResolver.ResolveTraversals(
                TopologyInfrastructure,
                route.Traversals.Skip(departureStartTraversalIndex))
        };
        var navigator = new TopologyMovementNavigator(
            TopologyInfrastructure,
            new ResolvedMovementPlan
            {
                MovementPlanId = $"{train.VehicleId}:{candidate.Operation.OperationId}:{train.ServiceRunId}",
                Legs = [arrivalLeg, facilityLeg, departureLeg]
            });
        var resources = TraversalResourceResolver.GetRequiredResources(facilityLeg).ToArray();
        var reservationId = resources.Length == 0
            ? null
            : $"TOPOLOGY:{train.VehicleId}|{candidate.Operation.OperationId}|{train.ServiceRunId}";
        if (reservationId is not null && !_resourceReservations.Reserve(reservationId, resources))
        {
            return false;
        }

        var transitionCursor = navigator.CreateCursor(
            0,
            arrivalCursor.TraversalIndex,
            arrivalNavigator.GetDistanceAlongTraversal(arrivalCursor));
        train.TopologyPassingMovement = new TopologyPassingMovement(
            candidate.Operation,
            facility,
            navigator,
            facilityLegIndex: 1,
            departureLegIndex: 2,
            departureStartTraversalIndex,
            train.NextStationIndex,
            reservationId,
            resources,
            train.Position,
            (int)train.Direction,
            navigator.GetDistanceFromStart(transitionCursor));
        train.ReservationId = reservationId;
        train.RoutePathId = null;
        train.HasDestinationPlatformReservation = false;
        train.HasStationReservation = reservationId is not null;
        train.TrackId = $"TOPOLOGY:{facility.FacilityId}";
        SetTrainRuntimeTopologyCursor(train, navigator, transitionCursor);
        AddEvent(
            SimulationEventType.RouteReserved,
            train,
            candidate.LocalTrain.VehicleId,
            $"{train.ServiceRunId} 已鎖定 topology 越行設施 {facility.FacilityId}。",
            train.Position,
            train.Speed,
            facility.FacilityId,
            resourceIds: resources);
        AddEvent(
            SimulationEventType.OvertakeRequested,
            train,
            candidate.LocalTrain.VehicleId,
            $"{train.ServiceRunId} 經 {facility.Name} 實體 passing traversal 跨越停靠列車 {candidate.LocalTrain.ServiceRunId}。",
            train.Position,
            train.Speed,
            facility.FacilityId);
        return true;
    }

    private void UpdateTopologyPassingMovement(MutableTrain train)
    {
        var movement = train.TopologyPassingMovement
            ?? throw new InvalidOperationException("topology 越行狀態遺失。");
        var navigator = movement.Navigator;
        var cursor = train.RuntimeTopologyCursor!.Value;
        var target = navigator.CreateCursor(
            movement.FacilityLegIndex,
            navigator.Legs[movement.FacilityLegIndex].Traversals.Count - 1,
            navigator.GetTraversal(
                movement.FacilityLegIndex,
                navigator.Legs[movement.FacilityLegIndex].Traversals.Count - 1).LengthMeters);
        var distanceToTarget = navigator.TryGetForwardDistance(cursor, target)
            ?? throw new SimulationValidationException(["topology 越行 facility target 位於列車 cursor 後方。"]);
        var performance = GetVehiclePerformance(train);
        var edgeLimit = navigator.GetTraversal(cursor.MovementLegIndex, cursor.TraversalIndex)
            .Edge.DefaultSpeedLimitMetersPerSecond;
        var permitted = Math.Min(performance.MaxSpeedMetersPerSecond, edgeLimit);
        var desiredAcceleration = CalculateDesiredAcceleration(train, permitted);
        train.Acceleration = ProfileMode == OperationProfileMode.RealisticOperations
            ? BrakingEnvelopeCalculator.MoveToward(
                train.Acceleration,
                desiredAcceleration,
                performance.JerkMetersPerSecondCubed * FixedTimeStepSeconds)
            : desiredAcceleration;
        var previousSpeed = train.Speed;
        var newSpeed = Math.Min(
            Math.Max(0, train.Speed + train.Acceleration * FixedTimeStepSeconds),
            permitted + 0.02);
        var traveled = Math.Max(0, (previousSpeed + newSpeed) * 0.5 * FixedTimeStepSeconds);
        if (traveled < distanceToTarget - NumericalTolerance)
        {
            SetTrainRuntimeTopologyCursor(train, navigator, navigator.Advance(cursor, traveled));
            train.Speed = newSpeed;
            train.Phase = OperationalPhase.Cruising;
            return;
        }

        // facility edge 結束與 departure route 起點必須在同一 graph node；cursor 的 edge
        // 變更是 traversal boundary，不是 PositionMeters 回跳。
        SetTrainRuntimeTopologyCursor(train, navigator, target);
        var departureStart = navigator.CreateCursor(movement.DepartureLegIndex, 0, 0);
        if (navigator.TryGetForwardDistance(target, departureStart) is not 0)
        {
            throw new SimulationValidationException(["越行設施離開端與 service route 無法連續接續。"]);
        }
        SetTrainRuntimeTopologyCursor(train, navigator, departureStart);
        train.Speed = newSpeed;
        train.PlatformId = movement.Facility.ExpressPlatformId;
        train.TrackId = $"EDGE:{GetServiceRouteId(train.Direction)}";
        train.CurrentStationIndex = movement.PassedStationIndex;
        train.NextStationIndex = train.CurrentStationIndex + GetStationIndexStep(train);
        train.Phase = OperationalPhase.Cruising;
        movement.IsOperating = false;
        AddEvent(
            SimulationEventType.StationPassed,
            train,
            null,
            $"{train.ServiceRunId} 經 {movement.Facility.Name} 實體通過線跨越 {movement.Facility.StationId}。",
            train.Position,
            train.Speed,
            movement.Facility.FacilityId);
        AddEvent(
            SimulationEventType.OvertakeCompleted,
            train,
            null,
            $"{train.ServiceRunId} 已在 topology passing edge 完成越行並匯入主線。",
            train.Position,
            train.Speed,
            movement.Facility.FacilityId);
    }

    private void TryReleaseCompletedTopologyPassingResources(MutableTrain train)
    {
        var movement = train.TopologyPassingMovement;
        if (movement is null || movement.IsOperating || train.RuntimeTopologyCursor is not { } cursor
            || cursor.MovementLegIndex < movement.DepartureLegIndex)
        {
            return;
        }

        var footprint = movement.Navigator.CreateFootprint(cursor, GetVehiclePerformance(train).LengthMeters);
        var facilityEdges = movement.Navigator.Legs[movement.FacilityLegIndex].Traversals
            .Select(traversal => traversal.Edge.TrackEdgeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (footprint.OccupiedIntervals.Any(interval => facilityEdges.Contains(interval.TrackEdgeId)))
        {
            return;
        }

        if (movement.ReservationId is not null)
        {
            ReleaseRouteReservation(train);
        }

        var mainlineNavigator = GetMainlineMovementNavigator(train.Direction);
        var distanceAlongTraversal = movement.Navigator.GetDistanceAlongTraversal(cursor);
        var serviceRouteTraversalIndex = cursor.TraversalIndex + movement.DepartureServiceRouteTraversalOffset;
        train.TopologyPassingMovement = null;
        SetTrainRuntimeTopologyCursor(
            train,
            mainlineNavigator,
            mainlineNavigator.CreateCursor(0, serviceRouteTraversalIndex, distanceAlongTraversal));
    }

    private bool TryStartAfterStationTailTrackMovement(
        MutableTrain train,
        SpatialReferencePointDefinition? spatialReference,
        TurnbackPlanDefinition? detailedTurnback)
    {
        var tailTrackLayout = detailedTurnback is null
            ? null
            : Infrastructure.FindAfterStationTailTrackLayout(detailedTurnback);
        if (tailTrackLayout is null)
        {
            return false;
        }

        var tailTrack = tailTrackLayout.GetTrack(train.Direction);
        train.TailTrackMovement = new TailTrackMovement(
            tailTrackLayout,
            spatialReference?.AdditionalTurnbackSeconds ?? detailedTurnback!.TurnbackTimeSeconds);
        train.TrackId = tailTrack.TrackId;
        EnterLegacyVirtualTrack(train, train.Position);
        train.Speed = 0;
        train.Acceleration = 0;
        train.Phase = OperationalPhase.TailTrackOutbound;
        AddEvent(
            SimulationEventType.TurnaroundStarted,
            train,
            null,
            $"{train.ServiceRunId} 離開端點站，經 {tailTrack.TrackId} 駛往尾軌虛擬節點 {tailTrackLayout.VirtualNodeId}。",
            train.Position,
            0,
            tailTrack.TrackId);
        return true;
    }

    private void UpdateAfterStationTailTrackMovement(MutableTrain train)
    {
        var movement = train.TailTrackMovement
            ?? throw new InvalidOperationException("尾軌折返狀態遺失。");
        if (movement.Stage == TailTrackMovementStage.WaitingAtVirtualNode)
        {
            movement.WaitRemainingSeconds = Math.Max(0, movement.WaitRemainingSeconds - FixedTimeStepSeconds);
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train, train.Acceleration);
            train.Phase = OperationalPhase.Turning;
            if (movement.WaitRemainingSeconds > NumericalTolerance)
            {
                return;
            }

            PrepareTurnaround(train);
            var returnTrack = movement.Layout.GetTrack(train.Direction);
            train.TrackId = returnTrack.TrackId;
            movement.Stage = TailTrackMovementStage.ReturningToStation;
            movement.BrakingActive = false;
            AddEvent(
                SimulationEventType.TailTrackReturnStarted,
                train,
                null,
                $"{train.ServiceRunId} 已在尾軌虛擬節點 {movement.Layout.VirtualNodeId} 完成折返，經 {returnTrack.TrackId} 駛回端點站。",
                train.Position,
                0,
                returnTrack.TrackId);
            return;
        }

        var targetPosition = movement.Stage == TailTrackMovementStage.RunningOutbound
            ? movement.Layout.VirtualNodePositionMeters
            : Route.Stations[train.CurrentStationIndex].PositionMeters;
        var track = movement.Layout.GetTrack(train.Direction);
        UpdateTailTrackMovementTowardTarget(train, movement, track, targetPosition);
    }

    private void UpdateTailTrackMovementTowardTarget(
        MutableTrain train,
        TailTrackMovement movement,
        TrackSegmentDefinition track,
        double targetPosition)
    {
        var distanceToTarget = ForwardDistance(train.Direction, train.Position, targetPosition);
        if (train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToTarget <= StationStopSnapToleranceMeters)
        {
            CompleteTailTrackLeg(train, movement, targetPosition);
            return;
        }

        if (movement.BrakingActive
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToTarget > StationStopSnapToleranceMeters)
        {
            movement.BrakingActive = false;
        }

        var performance = GetVehiclePerformance(train);
        var permitted = Math.Min(performance.MaxSpeedMetersPerSecond, track.SpeedLimitMetersPerSecond);
        var desiredAcceleration = CalculateDesiredAcceleration(train, permitted);
        if (movement.BrakingActive
            || ShouldBeginStationBraking(
                train,
                distanceToTarget,
                desiredAcceleration,
                performance.ServiceBrakingMetersPerSecondSquared))
        {
            movement.BrakingActive = true;
            desiredAcceleration = -performance.ServiceBrakingMetersPerSecondSquared;
        }

        if (ProfileMode == OperationProfileMode.RealisticOperations)
        {
            train.Acceleration = BrakingEnvelopeCalculator.MoveToward(
                train.Acceleration,
                desiredAcceleration,
                performance.JerkMetersPerSecondCubed * FixedTimeStepSeconds);
        }
        else
        {
            train.Acceleration = desiredAcceleration;
        }

        var previousSpeed = train.Speed;
        var newSpeed = Math.Max(0, train.Speed + train.Acceleration * FixedTimeStepSeconds);
        newSpeed = Math.Min(newSpeed, track.SpeedLimitMetersPerSecond + 0.02);
        var traveled = Math.Max(0, (previousSpeed + newSpeed) * 0.5 * FixedTimeStepSeconds);
        var remainingAfterMove = distanceToTarget - traveled;
        if (newSpeed <= ArrivalSpeedToleranceMetersPerSecond
            && remainingAfterMove <= StationStopSnapToleranceMeters)
        {
            CompleteTailTrackLeg(train, movement, targetPosition);
            return;
        }

        if (traveled >= distanceToTarget - NumericalTolerance)
        {
            SetTrainPositionFromProjectedCoordinate(train, targetPosition - (int)train.Direction * NumericalTolerance);
            train.Speed = newSpeed;
            train.Phase = movement.Stage == TailTrackMovementStage.RunningOutbound
                ? OperationalPhase.TailTrackOutbound
                : OperationalPhase.TailTrackReturn;
            movement.BrakingActive = true;
            return;
        }

        SetTrainPositionFromProjectedCoordinate(train, train.Position + traveled * (int)train.Direction);
        train.Speed = newSpeed;
        train.Phase = movement.Stage == TailTrackMovementStage.RunningOutbound
            ? OperationalPhase.TailTrackOutbound
            : OperationalPhase.TailTrackReturn;
    }

    private void CompleteTailTrackLeg(
        MutableTrain train,
        TailTrackMovement movement,
        double targetPosition)
    {
        SetTrainPositionFromProjectedCoordinate(train, targetPosition);
        train.Speed = 0;
        train.Acceleration = 0;
        movement.BrakingActive = false;
        if (movement.Stage == TailTrackMovementStage.RunningOutbound)
        {
            movement.Stage = TailTrackMovementStage.WaitingAtVirtualNode;
            train.Phase = OperationalPhase.Turning;
            AddEvent(
                SimulationEventType.TailTrackReached,
                train,
                null,
                $"{train.ServiceRunId} 抵達尾軌虛擬節點 {movement.Layout.VirtualNodeId}，開始折返停等。",
                train.Position,
                0,
                movement.Layout.VirtualNodeId);
            return;
        }

        train.TailTrackMovement = null;
        var station = Route.Stations[train.CurrentStationIndex];
        var detailedTurnback = Infrastructure.TurnbackPlans.FirstOrDefault(plan =>
            plan.Kind == TurnbackKind.AfterStation
            && plan.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(detailedTurnback?.DeparturePlatformId))
        {
            train.PlatformId = detailedTurnback.DeparturePlatformId;
        }
        AssignStationTrack(train, train.PlatformId);
        ResumeMainlineTopologyTraversal(train);
        AddEvent(
            SimulationEventType.Arrival,
            train,
            null,
            $"{train.ServiceRunId} 由尾軌返回 {station.StationId}，停靠反方向月台 {train.PlatformId ?? "（未指定）"}。",
            train.Position,
            0,
            train.TrackId);
        train.Phase = OperationalPhase.Turning;
        CompleteTurnaround(train);
    }

    private bool TryStartSpatialTurnbackMovement(
        MutableTrain train,
        SpatialReferencePointDefinition? point)
    {
        if (point?.Kind is not (SpatialReferencePointKind.BeforeStationTurnback
            or SpatialReferencePointKind.CentralSidingTurnback))
        {
            return false;
        }

        var oneWayDistance = GetSpatialTurnbackOneWayDistance(point);
        if (oneWayDistance <= NumericalTolerance)
        {
            return false;
        }

        var virtualPosition = train.Position + (int)train.Direction * oneWayDistance;
        train.SpatialTurnbackMovement = new SpatialTurnbackMovement(point, virtualPosition, point.AdditionalTurnbackSeconds);
        train.TrackId = GetSpatialTurnbackTrackId(point, "OUT");
        EnterLegacyVirtualTrack(train, train.Position);
        train.Speed = 0;
        train.Acceleration = 0;
        train.Phase = OperationalPhase.SpatialTurnbackOutbound;
        AddEvent(
            SimulationEventType.TurnaroundStarted,
            train,
            null,
            $"{train.ServiceRunId} 離開 {point.StationId}，依序通過橫渡線／折返線駛往虛擬折返點。",
            train.Position,
            0,
            train.TrackId);
        return true;
    }

    private void UpdateSpatialTurnbackMovement(MutableTrain train)
    {
        var movement = train.SpatialTurnbackMovement
            ?? throw new InvalidOperationException("空間折返分段狀態遺失。 ");
        if (movement.Stage == SpatialTurnbackMovementStage.WaitingAtVirtualTurnbackPoint)
        {
            movement.WaitRemainingSeconds = Math.Max(0, movement.WaitRemainingSeconds - FixedTimeStepSeconds);
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train, train.Acceleration);
            train.Phase = OperationalPhase.Turning;
            if (movement.WaitRemainingSeconds > NumericalTolerance)
            {
                return;
            }

            PrepareTurnaround(train);
            train.TrackId = GetSpatialTurnbackTrackId(movement.Point, "RETURN");
            movement.Stage = SpatialTurnbackMovementStage.ReturningToStation;
            movement.BrakingActive = false;
            return;
        }

        var targetPosition = movement.Stage == SpatialTurnbackMovementStage.RunningOutbound
            ? movement.VirtualTurnbackPositionMeters
            : Route.Stations[train.CurrentStationIndex].PositionMeters;
        UpdateSpatialTurnbackMovementTowardTarget(train, movement, targetPosition);
    }

    private void UpdateSpatialTurnbackMovementTowardTarget(
        MutableTrain train,
        SpatialTurnbackMovement movement,
        double targetPosition)
    {
        var distanceToTarget = ForwardDistance(train.Direction, train.Position, targetPosition);
        if (train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToTarget <= StationStopSnapToleranceMeters)
        {
            CompleteSpatialTurnbackLeg(train, movement, targetPosition);
            return;
        }

        var performance = GetVehiclePerformance(train);
        var outward = movement.Stage == SpatialTurnbackMovementStage.RunningOutbound;
        var grade = movement.Point.MainlineGradePermille * 0.009;
        var acceleration = outward
            ? performance.AccelerationMetersPerSecondSquared + grade
            : performance.AccelerationMetersPerSecondSquared - grade;
        var braking = outward
            ? performance.ServiceBrakingMetersPerSecondSquared - grade
            : performance.ServiceBrakingMetersPerSecondSquared + grade;
        if (acceleration <= 0 || braking <= 0)
        {
            throw new SimulationValidationException([
                $"空間參考點「{movement.Point.Name}」的坡度與列車加減速度組合會使折返有效加速度或減速度不大於 0。"
            ]);
        }

        var permitted = Math.Min(performance.MaxSpeedMetersPerSecond, movement.Point.SwitchSpeedLimitMetersPerSecond);
        var desiredAcceleration = CalculateDesiredAcceleration(train, permitted);
        if (desiredAcceleration > NumericalTolerance)
        {
            desiredAcceleration = acceleration;
        }

        if (movement.BrakingActive
            || ShouldBeginStationBraking(train, distanceToTarget, desiredAcceleration, braking))
        {
            movement.BrakingActive = true;
            desiredAcceleration = -braking;
        }

        if (ProfileMode == OperationProfileMode.RealisticOperations)
        {
            train.Acceleration = BrakingEnvelopeCalculator.MoveToward(
                train.Acceleration,
                desiredAcceleration,
                performance.JerkMetersPerSecondCubed * FixedTimeStepSeconds);
        }
        else
        {
            train.Acceleration = desiredAcceleration;
        }

        var previousSpeed = train.Speed;
        var newSpeed = Math.Max(0, train.Speed + train.Acceleration * FixedTimeStepSeconds);
        newSpeed = Math.Min(newSpeed, permitted + 0.02);
        var traveled = Math.Max(0, (previousSpeed + newSpeed) * 0.5 * FixedTimeStepSeconds);
        var remainingAfterMove = distanceToTarget - traveled;
        if (newSpeed <= ArrivalSpeedToleranceMetersPerSecond
            && remainingAfterMove <= StationStopSnapToleranceMeters)
        {
            CompleteSpatialTurnbackLeg(train, movement, targetPosition);
            return;
        }

        if (traveled >= distanceToTarget - NumericalTolerance)
        {
            SetTrainPositionFromProjectedCoordinate(train, targetPosition - (int)train.Direction * NumericalTolerance);
            train.Speed = newSpeed;
            movement.BrakingActive = true;
        }
        else
        {
            SetTrainPositionFromProjectedCoordinate(train, train.Position + traveled * (int)train.Direction);
            train.Speed = newSpeed;
        }

        train.Phase = outward
            ? OperationalPhase.SpatialTurnbackOutbound
            : OperationalPhase.SpatialTurnbackReturn;
    }

    private void CompleteSpatialTurnbackLeg(
        MutableTrain train,
        SpatialTurnbackMovement movement,
        double targetPosition)
    {
        SetTrainPositionFromProjectedCoordinate(train, targetPosition);
        train.Speed = 0;
        train.Acceleration = 0;
        movement.BrakingActive = false;
        if (movement.Stage == SpatialTurnbackMovementStage.RunningOutbound)
        {
            movement.Stage = SpatialTurnbackMovementStage.WaitingAtVirtualTurnbackPoint;
            train.Phase = OperationalPhase.Turning;
            return;
        }

        train.SpatialTurnbackMovement = null;
        var station = Route.Stations[train.CurrentStationIndex];
        AssignStationTrack(train, train.PlatformId);
        ResumeMainlineTopologyTraversal(train);
        AddEvent(
            SimulationEventType.Arrival,
            train,
            null,
            $"{train.ServiceRunId} 由折返線返回 {station.StationId}。",
            train.Position,
            0,
            train.TrackId);
        train.Phase = OperationalPhase.Turning;
        CompleteTurnaround(train);
    }

    private static double GetSpatialTurnbackOneWayDistance(SpatialReferencePointDefinition point)
    {
        var distance = point.DistanceFromStopToCrossoverMeters + point.CrossoverLengthMeters;
        if (point.Kind == SpatialReferencePointKind.CentralSidingTurnback)
        {
            distance += point.DistanceFromCrossoverToTurnbackStopMeters;
        }
        return distance;
    }

    private static string GetSpatialTurnbackTrackId(SpatialReferencePointDefinition point, string leg) =>
        $"TURNBACK:{point.ReferencePointId}:{leg}";

    private static double GetConfiguredStationDwellSeconds(
        SpatialReferencePointDefinition? point,
        TrainDirection direction,
        double fallback)
    {
        if (point?.Kind != SpatialReferencePointKind.IntermediateStation)
        {
            return fallback;
        }

        return direction == TrainDirection.Outbound
            ? point.StationForwardDwellSeconds
            : point.StationReverseDwellSeconds;
    }

    private double CalculateSpatialTurnbackTravelSeconds(MutableTrain train, SpatialReferencePointDefinition point)
    {
        var oneWayDistance = GetSpatialTurnbackOneWayDistance(point);
        if (point.Kind == SpatialReferencePointKind.AfterStationTurnback)
        {
            oneWayDistance += point.DistanceFromCrossoverToTurnbackStopMeters;
        }

        if (oneWayDistance <= NumericalTolerance)
        {
            return 0;
        }

        var performance = GetVehiclePerformance(train);
        var grade = point.MainlineGradePermille;
        var outwardAcceleration = performance.AccelerationMetersPerSecondSquared + 0.009 * grade;
        var outwardDeceleration = performance.ServiceBrakingMetersPerSecondSquared - 0.009 * grade;
        var returnAcceleration = performance.AccelerationMetersPerSecondSquared - 0.009 * grade;
        var returnDeceleration = performance.ServiceBrakingMetersPerSecondSquared + 0.009 * grade;
        if (outwardAcceleration <= 0 || outwardDeceleration <= 0
            || returnAcceleration <= 0 || returnDeceleration <= 0)
        {
            throw new SimulationValidationException([
                $"空間參考點「{point.Name}」的坡度與列車加減速度組合會使折返有效加速度或減速度不大於 0。"
            ]);
        }

        var outward = AnalyticalModel.CalculateSegmentTravelTime(
            oneWayDistance,
            point.SwitchSpeedLimitMetersPerSecond,
            outwardAcceleration,
            outwardDeceleration);
        var returning = AnalyticalModel.CalculateSegmentTravelTime(
            oneWayDistance,
            point.SwitchSpeedLimitMetersPerSecond,
            returnAcceleration,
            returnDeceleration);
        return outward.TravelTimeSeconds + returning.TravelTimeSeconds;
    }

    private IReadOnlyList<SafetyObservation> ComputeSafetyObservations(bool recordStatusEvents)
    {
        var result = new List<SafetyObservation>();
        var topologyTrains = _trains
            .Where(train => train.Active
                && train.Phase != OperationalPhase.OutOfService
                && UsesTopologyRuntimeSafety(train))
            .ToArray();
        foreach (var follower in topologyTrains)
        {
            if (follower.Collided)
            {
                continue;
            }

            var leader = FindNearestTopologyLeader(follower);
            if (leader is null)
            {
                continue;
            }

            var observation = CalculateSafetyObservation(follower, leader);
            result.Add(observation);
            if (recordStatusEvents)
            {
                RecordSafetyTransition(follower, leader, observation);
            }
        }

        var groups = _trains
            .Where(train => train.Active
                && train.Phase != OperationalPhase.OutOfService
                && !UsesTopologyRuntimeSafety(train)
                && !string.IsNullOrWhiteSpace(train.TrackId))
            .GroupBy(train => (train.Direction, train.TrackId));

        foreach (var group in groups)
        {
            var ordered = group
                .OrderByDescending(train => Progress(train.Direction, train.Position))
                .ToArray();
            for (var index = 0; index < ordered.Length - 1; index++)
            {
                var leader = ordered[index];
                var follower = ordered[index + 1];
                if (follower.Collided)
                {
                    continue;
                }

                var observation = CalculateSafetyObservation(follower, leader);
                result.Add(observation);

                if (recordStatusEvents)
                {
                    RecordSafetyTransition(follower, leader, observation);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 對正常 topology traversal，以列車車頭到前車車頭的 graph distance 減去前車實際
    /// footprint 長度，取得可供煞車／閉塞使用的淨距。它不讀 global projected chainage。
    /// </summary>
    private bool TryGetTopologySafetyMetrics(
        MutableTrain follower,
        MutableTrain leader,
        out TopologySafetyMetrics metrics)
    {
        metrics = default;
        if (ReferenceEquals(follower, leader)
            || !UsesTopologyRuntimeSafety(follower)
            || !UsesTopologyRuntimeSafety(leader))
        {
            return false;
        }

        // 快速車的車頭已匯入主線、車尾仍在 passing edge 時，停靠普通車的車頭會位於
        // 另一條平行月台 edge 的 merge endpoint。兩個 footprint 尚未共用同一條 edge，
        // 不能把「快速車整列長度」投影回 local edge 而誤判成負間距。普通車會由
        // ShouldHoldForTopologyPassing 保持停等，直到 passing movement rear-clear。
        var passingMovement = leader.TopologyPassingMovement;
        if (passingMovement is not null
            && follower.Direction == leader.Direction
            && follower.Phase is OperationalPhase.Arriving or OperationalPhase.Dwelling
            && follower.PlatformId is not null
            && follower.PlatformId.Equals(passingMovement.Facility.LocalPlatformId, StringComparison.OrdinalIgnoreCase)
            && HasStationIndex(follower, follower.CurrentStationIndex)
            && GetRuntimeStation(follower, follower.CurrentStationIndex).StationId
                .Equals(passingMovement.Facility.StationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var followerNavigator = GetMovementNavigator(follower);
        var leaderNavigator = GetMovementNavigator(leader);
        var followerFootprint = followerNavigator.CreateFootprint(
            follower.RuntimeTopologyCursor!.Value,
            GetVehiclePerformance(follower).LengthMeters);
        var leaderFootprint = leaderNavigator.CreateFootprint(
            leader.RuntimeTopologyCursor!.Value,
            GetVehiclePerformance(leader).LengthMeters);
        var headDistance = TopologyGraphDistance.TryGetForwardDistance(
            TopologyInfrastructure,
            followerNavigator,
            followerFootprint.Front,
            leaderNavigator,
            leaderFootprint.Front);
        var actualGap = TopologyGraphDistance.TryGetFootprintGap(
            TopologyInfrastructure,
            followerNavigator,
            followerFootprint,
            leaderNavigator,
            leaderFootprint);
        if (headDistance is null || actualGap is null)
        {
            return false;
        }

        metrics = new TopologySafetyMetrics(
            headDistance.Value,
            actualGap.Value,
            leaderFootprint);
        return true;
    }

    private MutableTrain? FindNearestTopologyLeader(MutableTrain follower) =>
        _trains
            .Where(other => other.Active
                && other.Phase != OperationalPhase.OutOfService
                && UsesTopologyRuntimeSafety(other)
                && !ReferenceEquals(other, follower))
            .Select(other => new
            {
                Train = other,
                Metrics = TryGetTopologySafetyMetrics(follower, other, out var metrics)
                    ? metrics
                    : (TopologySafetyMetrics?)null
            })
            .Where(item => item.Metrics is not null)
            .OrderBy(item => item.Metrics!.Value.HeadDistanceMeters)
            .ThenBy(item => item.Train.VehicleId, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Train)
            .FirstOrDefault();

    private readonly record struct TopologySafetyMetrics(
        double HeadDistanceMeters,
        double ActualGapMeters,
        TopologyMovementFootprint LeaderFootprint);

    private SafetyObservation CalculateSafetyObservation(MutableTrain follower, MutableTrain leader)
    {
        var followerPerformance = GetVehiclePerformance(follower);
        var leaderPerformance = GetVehiclePerformance(leader);
        double leaderRear;
        double actualGap;
        double headDistance;
        if (TryGetTopologySafetyMetrics(follower, leader, out var topologyMetrics))
        {
            leaderRear = leader.Position;
            actualGap = topologyMetrics.ActualGapMeters;
            headDistance = topologyMetrics.HeadDistanceMeters;
        }
        else
        {
            leaderRear = leader.Direction == TrainDirection.Outbound
                ? leader.Position - leaderPerformance.LengthMeters
                : leader.Position + leaderPerformance.LengthMeters;
            actualGap = leader.Direction == TrainDirection.Outbound
                ? leaderRear - follower.Position
                : follower.Position - leaderRear;
            headDistance = Math.Abs(leader.Position - follower.Position);
        }
        var timeGap = follower.Speed > 1e-6 ? Math.Max(0, headDistance / follower.Speed) : double.PositiveInfinity;

        var reactionDistance = follower.Speed * OperationalParameters.ControlReactionTimeSeconds;
        var buildDistance = follower.Speed * OperationalParameters.BrakeBuildUpTimeSeconds;
        var dynamicSafety = CalculateDynamicSafetyDistance(
            follower,
            leader,
            follower.Speed,
            follower.Acceleration,
            leader.Speed);

        var selectedBraking = BrakingEstimationMode == BrakingEstimationMode.Service
            ? followerPerformance.ServiceBrakingMetersPerSecondSquared
            : followerPerformance.EmergencyBrakingMetersPerSecondSquared;
        var dynamicBraking = CalculateDynamicBrakingDistance(
            follower,
            follower.Speed,
            follower.Acceleration,
            selectedBraking);
        var obstacleDemand = reactionDistance + buildDistance + dynamicBraking
            + OperationalParameters.PositioningErrorMeters
            + OperationalParameters.SafetyMarginMeters;
        var predictedStop = follower.Direction == TrainDirection.Outbound
            ? follower.Position + obstacleDemand
            : follower.Position - obstacleDemand;
        var margin = actualGap - dynamicSafety;
        var intrusion = Math.Max(0, obstacleDemand - Math.Max(0, actualGap));
        var reactionRemaining = follower.Speed > 1e-6
            ? Math.Max(0, (actualGap - obstacleDemand) / follower.Speed)
            : double.PositiveInfinity;
        var status = margin < 0
            ? SafetyStatus.EnvelopeIntrusion
            : actualGap < dynamicSafety * 1.15
                ? SafetyStatus.BrakingRequired
                : actualGap < dynamicSafety * 1.4
                    ? SafetyStatus.Caution
                    : SafetyStatus.Safe;

        return new SafetyObservation(
            CurrentTimeSeconds,
            follower.VehicleId,
            leader.VehicleId,
            follower.Direction,
            follower.CurrentTrackEdgeId ?? follower.TrackId,
            follower.Position,
            leaderRear,
            headDistance,
            actualGap,
            timeGap,
            dynamicSafety,
            obstacleDemand,
            predictedStop,
            margin,
            intrusion,
            reactionRemaining,
            status,
            BrakingEstimationMode);
    }

    private double CalculateDynamicSafetyDistance(
        MutableTrain follower,
        MutableTrain leader,
        double followerSpeed,
        double followerAcceleration,
        double leaderSpeed)
    {
        var followerPerformance = GetVehiclePerformance(follower);
        var leaderPerformance = GetVehiclePerformance(leader);
        var reactionDistance = followerSpeed * OperationalParameters.ControlReactionTimeSeconds;
        var buildDistance = followerSpeed * OperationalParameters.BrakeBuildUpTimeSeconds;
        var followerBraking = CalculateDynamicBrakingDistance(
            follower,
            followerSpeed,
            followerAcceleration,
            followerPerformance.ServiceBrakingMetersPerSecondSquared);
        var leaderBraking = leaderSpeed * leaderSpeed
            / (2 * leaderPerformance.EmergencyBrakingMetersPerSecondSquared);
        var fixedAllowance = 2 * OperationalParameters.PositioningErrorMeters
            + OperationalParameters.SafetyMarginMeters;
        return Math.Max(
            OperationalParameters.AbsoluteMinimumGapMeters,
            reactionDistance + buildDistance
                + Math.Max(0, followerBraking - leaderBraking) + fixedAllowance);
    }

    private Dictionary<string, double> CalculateMovingBlockSpeedLimits(IEnumerable<SafetyObservation> observations)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var observation in observations)
        {
            var leader = _trains.First(train => train.VehicleId == observation.LeaderVehicleId);
            var follower = _trains.First(train => train.VehicleId == observation.FollowerVehicleId);
            var leaderPerformance = GetVehiclePerformance(leader);
            var followerPerformance = GetVehiclePerformance(follower);
            var fixedAllowance = 2 * OperationalParameters.PositioningErrorMeters
                + OperationalParameters.SafetyMarginMeters;
            var leaderStopDistance = leader.Speed * leader.Speed
                / (2 * leaderPerformance.EmergencyBrakingMetersPerSecondSquared);
            var controlReserve = Math.Max(
                1,
                follower.Speed * MovingBlockControlLookAheadSeconds);
            var available = observation.ActualGapMeters - fixedAllowance + leaderStopDistance - controlReserve;
            var reaction = OperationalParameters.ControlReactionTimeSeconds
                + OperationalParameters.BrakeBuildUpTimeSeconds;
            var braking = followerPerformance.ServiceBrakingMetersPerSecondSquared;
            var allowed = CalculateMovingBlockPermittedSpeed(
                follower,
                Math.Max(0, available),
                reaction,
                follower.Acceleration,
                braking);
            result[observation.FollowerVehicleId] = Math.Clamp(
                allowed,
                0,
                followerPerformance.MaxSpeedMetersPerSecond);
        }

        return result;
    }

    private double CalculateMovingBlockPermittedSpeed(
        MutableTrain follower,
        double availableDistance,
        double reactionSeconds,
        double currentAcceleration,
        double braking)
    {
        var lower = 0d;
        var upper = GetVehiclePerformance(follower).MaxSpeedMetersPerSecond;
        for (var iteration = 0; iteration < 48; iteration++)
        {
            var candidate = (lower + upper) * 0.5;
            var demand = candidate * reactionSeconds
                + CalculateDynamicBrakingDistance(follower, candidate, currentAcceleration, braking);
            if (demand <= availableDistance)
            {
                lower = candidate;
            }
            else
            {
                upper = candidate;
            }
        }

        return lower;
    }

    private double CalculateDynamicBrakingDistance(
        MutableTrain train,
        double speed,
        double acceleration,
        double braking) =>
        BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
            speed,
            acceleration,
            braking,
            GetVehiclePerformance(train).JerkMetersPerSecondCubed,
            FixedTimeStepSeconds,
            ProfileMode == OperationProfileMode.RealisticOperations).DistanceMeters;

    private void RecordSafetyTransition(
        MutableTrain follower,
        MutableTrain leader,
        SafetyObservation observation)
    {
        var key = $"{follower.VehicleId}|{leader.VehicleId}|{follower.TrackId}";
        if (_lastSafetyStatuses.TryGetValue(key, out var previous) && previous == observation.Status)
        {
            return;
        }

        _lastSafetyStatuses[key] = observation.Status;
        _safetyKeysWithStatusChange.Add(new SafetyObservationKey(
            follower.VehicleId,
            leader.VehicleId,
            observation.TrackId));
        AddEvent(
            SimulationEventType.SafetyStatusChanged,
            follower,
            leader.VehicleId,
            $"{follower.VehicleId} → {leader.VehicleId} 安全狀態：{observation.Status}；"
                + $"淨距 {observation.ActualGapMeters:0.0} m，安全距離 {observation.DynamicSafetyDistanceMeters:0.0} m。",
            follower.Position,
            follower.Speed);
        if (observation.PredictedIntrusionMeters > 0)
        {
            AddEvent(
                SimulationEventType.PredictedCollision,
                follower,
                leader.VehicleId,
                $"預測煞車端點將侵入前車尾端 {observation.PredictedIntrusionMeters:0.0} m。",
                observation.PredictedStopPositionMeters,
                follower.Speed);
        }
    }

    private void RecordSafetyHistory(IReadOnlyList<SafetyObservation> observations)
    {
        var observedKeys = new HashSet<SafetyObservationKey>();
        foreach (var observation in observations)
        {
            var key = new SafetyObservationKey(
                observation.FollowerVehicleId,
                observation.LeaderVehicleId,
                observation.TrackId);
            observedKeys.Add(key);
            var statusChanged = _safetyKeysWithStatusChange.Remove(key);
            var shouldRecord = _safetyObservationRetentionPolicy.Mode == SafetyObservationRetentionMode.Full
                || !_lastSafetyHistorySamples.TryGetValue(key, out var previousTime)
                || observation.SimulationTimeSeconds - previousTime
                    >= _safetyObservationRetentionPolicy.MinimumSampleIntervalSeconds - NumericalTolerance
                || statusChanged;
            if (!shouldRecord)
            {
                continue;
            }

            _safetyHistory.Add(observation);
            _lastSafetyHistorySamples[key] = observation.SimulationTimeSeconds;
        }

        _safetyKeysWithStatusChange.RemoveWhere(key => !observedKeys.Contains(key));
    }

    private readonly record struct SafetyObservationKey(
        string FollowerVehicleId,
        string LeaderVehicleId,
        string TrackId);

    private void ApplyCollisionProtection()
    {
        foreach (var follower in _trains.Where(train => train.Active
                     && train.Phase != OperationalPhase.OutOfService
                     && UsesTopologyRuntimeSafety(train)))
        {
            if (follower.Collided)
            {
                continue;
            }

            var leader = FindNearestTopologyLeader(follower);
            if (leader is null
                || !TryGetTopologySafetyMetrics(follower, leader, out var topologyMetrics)
                || topologyMetrics.ActualGapMeters >= -NumericalTolerance)
            {
                continue;
            }

            var impactSpeed = follower.Speed;
            follower.Speed = 0;
            follower.Acceleration = 0;
            follower.Collided = true;
            follower.Phase = OperationalPhase.Collided;
            AddEvent(
                SimulationEventType.Collision,
                follower,
                leader.VehicleId,
                $"碰撞防護已停止 {follower.VehicleId}；撞擊瞬間速度 {impactSpeed * 3.6:0.#} km/h。",
                follower.Position,
                impactSpeed);
        }

        var groups = _trains
            .Where(train => train.Active
                && train.Phase != OperationalPhase.OutOfService
                && !UsesTopologyRuntimeSafety(train))
            .GroupBy(train => (train.Direction, train.TrackId));
        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(train => Progress(train.Direction, train.Position)).ToArray();
            for (var index = 0; index < ordered.Length - 1; index++)
            {
                var leader = ordered[index];
                var follower = ordered[index + 1];
                if (follower.Collided)
                {
                    continue;
                }

                var leaderLength = GetVehiclePerformance(leader).LengthMeters;
                var leaderRear = leader.Direction == TrainDirection.Outbound
                    ? leader.Position - leaderLength
                    : leader.Position + leaderLength;
                var gap = leader.Direction == TrainDirection.Outbound
                    ? leaderRear - follower.Position
                    : follower.Position - leaderRear;
                if (gap >= 0)
                {
                    continue;
                }

                var impactSpeed = follower.Speed;
                SetTrainPositionFromProjectedCoordinate(follower, Math.Clamp(leaderRear, 0, Route.TotalLengthMeters));
                follower.Speed = 0;
                follower.Acceleration = 0;
                follower.Collided = true;
                follower.Phase = OperationalPhase.Collided;
                AddEvent(
                    SimulationEventType.Collision,
                    follower,
                    leader.VehicleId,
                    $"碰撞防護已停止 {follower.VehicleId}；撞擊瞬間速度 {impactSpeed * 3.6:0.#} km/h。",
                    follower.Position,
                    impactSpeed);
            }
        }
    }

    private double? GetObstacleDistanceAhead(MutableTrain train)
    {
        if (UsesTopologyRuntimeSafety(train))
        {
            var topologyObstacle = _trains
                .Where(other => other.Active
                    && other.ObstacleStopped
                    && other.Direction == train.Direction
                    && UsesTopologyRuntimeSafety(other))
                .Select(other => new
                {
                    Train = other,
                    Metrics = TryGetTopologySafetyMetrics(train, other, out var metrics)
                        ? metrics
                        : (TopologySafetyMetrics?)null
                })
                .Where(item => item.Metrics is not null)
                .OrderBy(item => item.Metrics!.Value.HeadDistanceMeters)
                .FirstOrDefault();
            return topologyObstacle?.Metrics!.Value.ActualGapMeters is { } topologyGap
                ? Math.Max(0, topologyGap)
                : null;
        }

        var obstacle = _trains
            .Where(other => other.Active
                && other.ObstacleStopped
                && other.Direction == train.Direction
                && other.TrackId == train.TrackId
                && Progress(train.Direction, other.Position) > Progress(train.Direction, train.Position))
            .OrderBy(other => Math.Abs(other.Position - train.Position))
            .FirstOrDefault();
        if (obstacle is null)
        {
            return null;
        }

        var obstacleLength = GetVehiclePerformance(obstacle).LengthMeters;
        var obstacleBoundary = train.Direction == TrainDirection.Outbound
            ? obstacle.Position - obstacleLength
            : obstacle.Position + obstacleLength;
        return Math.Max(0, ForwardDistance(train.Direction, train.Position, obstacleBoundary));
    }

    private double CalculateStopCurveSpeed(MutableTrain train, double distanceMeters)
    {
        var performance = GetVehiclePerformance(train);
        var jerkAllowance = train.Speed * performance.ServiceBrakingMetersPerSecondSquared
            / performance.JerkMetersPerSecondCubed;
        var usable = Math.Max(0, distanceMeters - jerkAllowance - OperationalParameters.SafetyMarginMeters);
        return Math.Sqrt(2 * performance.ServiceBrakingMetersPerSecondSquared * usable);
    }

    private double MoveAccelerationTowardZero(MutableTrain train, double acceleration)
    {
        if (ProfileMode != OperationProfileMode.RealisticOperations)
        {
            return 0;
        }

        var maximumChange = GetVehiclePerformance(train).JerkMetersPerSecondCubed * FixedTimeStepSeconds;
        return Math.Abs(acceleration) <= maximumChange
            ? 0
            : acceleration - Math.Sign(acceleration) * maximumChange;
    }

    private void RefreshTopologyOccupancy()
    {
        _topologyOccupancy.Clear();
        foreach (var train in _trains.Where(train => train.Active
                     && train.Phase != OperationalPhase.OutOfService
                     && HasTopologyTraversalPosition(train)))
        {
            var footprint = GetMovementNavigator(train).CreateFootprint(
                train.RuntimeTopologyCursor!.Value,
                GetVehiclePerformance(train).LengthMeters);
            _topologyOccupancy.Set(train.VehicleId, footprint);
        }
    }

    private void RecordTrajectory()
    {
        foreach (var train in _trains.Where(train => train.Active))
        {
            SynchronizeTrainTopologyPosition(train);
            var sample = new TrajectorySample(
                CurrentTimeSeconds,
                train.VehicleId,
                train.ServiceRunId,
                train.ServiceClassId,
                train.PatternId,
                train.Direction,
                train.TrackId,
                train.Position,
                train.Speed,
                train.Acceleration,
                train.Phase,
                GetCurrentStationId(train),
                GetNextStationId(train),
                false,
                train.VehicleTypeId,
                train.PlatformId,
                train.Constraints,
                train.CurrentTrackEdgeId,
                train.OffsetMeters,
                train.ServiceRouteTraversalIndex,
                train.ProjectedChainageMeters,
                TrackSpeedLimitMetersPerSecond: GetTrackCurrentLimit(train, GetVehiclePerformance(train)));
            _traceStore.Record(sample);
        }
    }

    private WorldTrainState ToState(MutableTrain train)
    {
        SynchronizeTrainTopologyPosition(train);
        var length = GetVehiclePerformance(train).LengthMeters;
        var rear = train.Direction == TrainDirection.Outbound
            ? train.Position - length
            : train.Position + length;
        return new WorldTrainState(
            train.VehicleId,
            train.ServiceRunId,
            train.ServiceClassId,
            train.PatternId,
            train.Direction,
            train.TrackId,
            train.Position,
            rear,
            train.Speed,
            train.Acceleration,
            train.Phase,
            GetCurrentStationId(train),
            GetNextStationId(train),
            train.Active,
            CurrentTimeSeconds,
            train.VehicleTypeId,
            train.PlatformId,
            train.PlannedDepartureTime,
            train.ActualDepartureTime,
            train.Constraints,
            train.CurrentTrackEdgeId,
            train.OffsetMeters,
            train.ServiceRouteTraversalIndex,
            train.ProjectedChainageMeters);
    }

    private void AddEvent(
        SimulationEventType eventType,
        MutableTrain? train,
        string? relatedVehicleId,
        string message,
        double position,
        double speed,
        string? resourceId = null,
        double? delaySeconds = null,
        IReadOnlyList<string>? resourceIds = null)
    {
        if (train is not null)
        {
            SynchronizeTrainTopologyPosition(train);
        }
        var item = new SimulationEvent(
            CurrentTimeSeconds,
            eventType,
            train?.VehicleId ?? string.Empty,
            relatedVehicleId,
            train?.Direction ?? TrainDirection.Outbound,
            train?.TrackId ?? string.Empty,
            position,
            speed,
            message,
            train?.ServiceRunId ?? string.Empty,
            train?.ServiceClassId ?? string.Empty,
            train?.PatternId ?? string.Empty,
            train?.VehicleTypeId ?? "DEFAULT_VEHICLE",
            train?.PlatformId,
            resourceId,
            train?.PlannedDepartureTime,
            delaySeconds,
            resourceIds,
            train?.CurrentTrackEdgeId,
            train?.OffsetMeters,
            train?.ServiceRouteTraversalIndex,
            train?.ProjectedChainageMeters,
            StationId: train is not null && eventType is (SimulationEventType.Arrival
                or SimulationEventType.Departure or SimulationEventType.DwellStarted or SimulationEventType.StationPassed)
                    ? GetCurrentStationId(train) : null);
        _events.Add(item);
        _newEvents.Add(item);
        if (train is not null)
        {
            _traceStore.MarkEvent(train.VehicleId, train.ServiceRunId);
        }
    }

    private static double ForwardDistance(TrainDirection direction, double fromPosition, double toPosition) =>
        direction == TrainDirection.Outbound ? toPosition - fromPosition : fromPosition - toPosition;

    private double Progress(TrainDirection direction, double position) =>
        direction == TrainDirection.Outbound
            ? position
            : GetRouteProjection(direction).TotalLengthMeters - position;

    private ResolvedStop GetResolvedStop(MutableTrain train, Station station)
    {
        var stops = train.Direction == TrainDirection.Outbound
            ? _outboundResolvedStops
            : _inboundResolvedStops;
        return stops.TryGetValue(station.StationId, out var stop)
            ? ResolveVehicleStop(train, stop)
            : throw new SimulationValidationException([
                $"找不到 {station.StationId} 在 {train.Direction} 營運路線上的 topology stop。"
            ]);
    }

    private ResolvedStop GetOriginResolvedStop(TrainDirection direction) =>
        (direction == TrainDirection.Outbound ? _outboundResolvedStops : _inboundResolvedStops)
        .Values
        .OrderBy(stop => stop.ChainageMeters)
        .First();

    private ResolvedStop ResolveVehicleStop(MutableTrain train, ResolvedStop stop)
    {
        var platform = TopologyInfrastructure.Platforms[stop.PlatformId];
        var direction = GetServiceRouteDefinition(train.Direction).Traversals[stop.TraversalIndex].Direction;
        var trainLength = GetVehiclePerformance(train).LengthMeters;
        if (platform.StopPositionReference != StopPositionReference.TrainCenter)
        {
            return stop with
            {
                Position = PlatformStopPositionResolver.ResolveHeadPosition(platform, direction, trainLength)
            };
        }

        // A center-referenced stop may sit exactly at a node.  Resolve the
        // vehicle head by advancing along the ordered ServiceRoute instead of
        // writing an out-of-range offset on the platform's arrival edge.  This
        // keeps the physical center at the platform center while allowing the
        // footprint to span the adjacent edge at station boundaries.
        var routeNavigator = GetTopologyNavigator(train.Direction);
        var centerCursor = new TopologyTraversalCursor(
            routeNavigator.ServiceRouteId,
            stop.TraversalIndex,
            stop.Position);
        var headCursor = routeNavigator.Advance(centerCursor, trainLength / 2);
        var resolvedCenterOffset = routeNavigator.TryGetForwardDistance(centerCursor, headCursor) ?? 0;
        return stop with
        {
            TraversalIndex = headCursor.TraversalIndex,
            Position = headCursor.Position,
            ChainageMeters = stop.ChainageMeters + resolvedCenterOffset
        };
    }

    private TrackPosition ResolveFacilityPlatformHead(MutableTrain train, string edgeId, double anchor, TraversalDirection direction)
    {
        var platform = TopologyInfrastructure.Platforms.Values.FirstOrDefault(p => p.TrackEdgeId.Equals(edgeId, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(p.StopPositionOffsetMeters - anchor) < TrackPosition.DefaultToleranceMeters);
        return platform is null ? new TrackPosition(edgeId, anchor)
            : PlatformStopPositionResolver.ResolveHeadPosition(platform, direction, GetVehiclePerformance(train).LengthMeters);
    }

    private string? GetTopologyDeparturePlatform(TurnbackOperationDefinition operation)
    {
        var direction = GetDirectionForServiceRoute(operation.DepartureServiceRouteId);
        var stationOperation = TopologyInfrastructure.StationOperations.Values
            .SingleOrDefault(candidate => candidate.TurnbackOperationIds
                .Contains(operation.OperationId, StringComparer.OrdinalIgnoreCase));
        return stationOperation?.DeparturePlatformIds
            .Select(platformId => TopologyInfrastructure.Platforms[platformId])
            .FirstOrDefault(platform => platform.AllowedDirection is TrackDirection.Both
                || (direction == TrainDirection.Outbound && platform.AllowedDirection == TrackDirection.Outbound)
                || (direction == TrainDirection.Inbound && platform.AllowedDirection == TrackDirection.Inbound))
            ?.PlatformId
            ?? GetOriginResolvedStop(direction).PlatformId;
    }

    private string GetServiceRouteId(TrainDirection direction) =>
        direction == TrainDirection.Outbound
            ? _linearTopology.OutboundServiceRoute.ServiceRouteId
            : _linearTopology.InboundServiceRoute.ServiceRouteId;

    private ServiceRouteDefinition GetServiceRouteDefinition(TrainDirection direction) =>
        direction == TrainDirection.Outbound
            ? _linearTopology.OutboundServiceRoute
            : _linearTopology.InboundServiceRoute;

    private TrainDirection GetDirectionForServiceRoute(string serviceRouteId)
    {
        if (serviceRouteId.Equals(_linearTopology.OutboundServiceRoute.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
        {
            return TrainDirection.Outbound;
        }

        if (serviceRouteId.Equals(_linearTopology.InboundServiceRoute.ServiceRouteId, StringComparison.OrdinalIgnoreCase))
        {
            return TrainDirection.Inbound;
        }

        throw new SimulationValidationException([$"service route「{serviceRouteId}」未綁定 Outbound 或 Inbound direction。"]);
    }

    private double GetDistanceToResolvedStop(
        MutableTrain train,
        Station legacyStation,
        ResolvedStop resolvedStop)
    {
        if (!HasTopologyTraversalPosition(train))
        {
            return ForwardDistance(train.Direction, train.Position, legacyStation.PositionMeters);
        }

        var navigator = GetMovementNavigator(train);
        var target = navigator.CreateCursor(GetCurrentServiceRouteLegIndex(train), GetMovementTraversalIndex(train, resolvedStop.TraversalIndex),
            GetTopologyNavigator(train.Direction).GetDistanceAlongTraversal(
                new TopologyTraversalCursor(
                    GetTopologyNavigator(train.Direction).ServiceRouteId,
                    resolvedStop.TraversalIndex,
                    resolvedStop.Position)));
        return navigator.TryGetForwardDistance(train.RuntimeTopologyCursor!.Value, target)
            ?? ForwardDistance(train.Direction, train.Position, legacyStation.PositionMeters);
    }

    private double GetTrackPermittedSpeed(
        MutableTrain train,
        VehiclePerformance performance,
        double brakingMetersPerSecondSquared)
    {
        if (!HasTopologyTraversalPosition(train))
        {
            return SpeedLimits.GetPermittedSpeedMetersPerSecond(
                train.Position, train.Direction, performance.MaxSpeedMetersPerSecond,
                brakingMetersPerSecondSquared, performance.JerkMetersPerSecondCubed, train.Speed);
        }

        if (GetMovementNavigator(train).Legs[train.RuntimeTopologyCursor!.Value.MovementLegIndex].Kind != MovementLegKind.ServiceRoute)
        {
            return Math.Min(
                performance.MaxSpeedMetersPerSecond,
                GetMovementNavigator(train).GetTraversal(
                    train.RuntimeTopologyCursor.Value.MovementLegIndex,
                    train.RuntimeTopologyCursor.Value.TraversalIndex).Edge.DefaultSpeedLimitMetersPerSecond);
        }

        return _trackSpeedLimits.GetPermittedSpeedMetersPerSecond(
            GetTopologyNavigator(train.Direction),
            ToServiceRouteCursor(train),
            performance.MaxSpeedMetersPerSecond,
            brakingMetersPerSecondSquared,
            performance.JerkMetersPerSecondCubed,
            train.Speed);
    }

    private double GetTrackCurrentLimit(MutableTrain train, VehiclePerformance performance)
    {
        if (!HasTopologyTraversalPosition(train))
        {
            return SpeedLimits.GetCurrentLimitMetersPerSecond(
                train.Position, train.Direction, performance.MaxSpeedMetersPerSecond);
        }

        if (GetMovementNavigator(train).Legs[train.RuntimeTopologyCursor!.Value.MovementLegIndex].Kind != MovementLegKind.ServiceRoute)
        {
            return Math.Min(
                performance.MaxSpeedMetersPerSecond,
                GetMovementNavigator(train).GetTraversal(
                    train.RuntimeTopologyCursor.Value.MovementLegIndex,
                    train.RuntimeTopologyCursor.Value.TraversalIndex).Edge.DefaultSpeedLimitMetersPerSecond);
        }

        return _trackSpeedLimits.GetCurrentLimit(
            GetTopologyNavigator(train.Direction),
            ToServiceRouteCursor(train),
            performance.MaxSpeedMetersPerSecond);
    }

    /// <summary>
    /// Phase E normal-mainline movement entry point. 目前主線的 physical truth 是有序 traversal
    /// 上的 edge-local position；傳統 PositionMeters 僅由該 state 投影而成，供尚未遷移的
    /// station/resource/safety 相容邏輯讀取。
    /// </summary>
    private void AdvanceTrainAlongTraversal(MutableTrain train, double distanceMeters)
    {
        if (!double.IsFinite(distanceMeters) || distanceMeters < -NumericalTolerance)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }

        if (!HasTopologyTraversalPosition(train))
        {
            SetTrainPositionFromProjectedCoordinate(
                train,
                train.Position + Math.Max(0, distanceMeters) * (int)train.Direction);
            return;
        }

        var navigator = GetMovementNavigator(train);
        SetTrainRuntimeTopologyCursor(
            train,
            navigator,
            navigator.Advance(train.RuntimeTopologyCursor!.Value, Math.Max(0, distanceMeters)));
    }

    /// <summary>
    /// 將 legacy projected coordinate 轉成主線 topology position。此方法只保留給到站吸附、
    /// 碰撞保護等尚未改成 track-local target 的相容邊界；它不參與正常前進運算。
    /// </summary>
    private void SetTrainPositionFromProjectedCoordinate(MutableTrain train, double projectedPositionMeters)
    {
        if (train.UsesLegacyVirtualTrack)
        {
            SetLegacyVirtualTrackPosition(train, projectedPositionMeters);
            return;
        }

        var projection = GetRouteProjection(train.Direction);
        var routeChainage = train.Direction == TrainDirection.Outbound
            ? projectedPositionMeters
            : projection.TotalLengthMeters - projectedPositionMeters;

        if (!double.IsFinite(routeChainage)
            || routeChainage < -NumericalTolerance
            || routeChainage > projection.TotalLengthMeters + NumericalTolerance)
        {
            EnterLegacyVirtualTrack(train, projectedPositionMeters);
            return;
        }

        SetTrainTopologyPositionFromChainage(
            train,
            projection,
            Math.Clamp(routeChainage, 0, projection.TotalLengthMeters));
    }

    private void SetTrainTopologyPositionFromChainage(
        MutableTrain train,
        RouteProjection projection,
        double chainageMeters)
    {
        var normalizedChainage = Math.Clamp(chainageMeters, 0, projection.TotalLengthMeters);
        var segment = projection.Segments.First(segment =>
            normalizedChainage < segment.EndChainageMeters - NumericalTolerance
            || segment.TraversalIndex == projection.Segments.Count - 1);
        var position = projection.FromChainage(normalizedChainage);
        var navigator = GetMainlineMovementNavigator(train.Direction);
        var distanceAlongTraversal = segment.Traversal.Direction == TraversalDirection.Forward
            ? position.OffsetMeters
            : TopologyInfrastructure.GetRequiredEdge(position.TrackEdgeId).LengthMeters - position.OffsetMeters;
        SetTrainRuntimeTopologyCursor(train, navigator, navigator.CreateCursor(0, segment.TraversalIndex, distanceAlongTraversal));
    }

    private bool HasTopologyTraversalPosition(MutableTrain train)
    {
        if (train.RuntimeTopologyCursor is not { } cursor
            || train.UsesLegacyVirtualTrack
            || train.CurrentTrackEdgeId is not { Length: > 0 }
            || train.OffsetMeters is not { } offset
            || !cursor.Position.Equals(new TrackPosition(train.CurrentTrackEdgeId, offset))
            || !TopologyInfrastructure.ContainsPosition(cursor.Position))
        {
            return false;
        }

        try
        {
            var navigator = GetMovementNavigator(train);
            return cursor.MovementPlanId.Equals(navigator.MovementPlanId, StringComparison.OrdinalIgnoreCase)
                && navigator.GetTraversal(cursor.MovementLegIndex, cursor.TraversalIndex).Edge.TrackEdgeId
                    .Equals(cursor.Position.TrackEdgeId, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Schema 8 topology world 的所有實體 cursor 一律使用 graph safety。Schema 7 的
    /// station-yard resource model 尚可保留 virtual-track adapter，避免與未遷移設施混算。
    /// </summary>
    private bool UsesTopologyRuntimeSafety(MutableTrain train) =>
        (_topologyNativeRuntime || !_enforceRouteResources) && HasTopologyTraversalPosition(train);

    private void EnterLegacyVirtualTrack(MutableTrain train, double projectedPositionMeters)
    {
        train.UsesLegacyVirtualTrack = true;
        train.RuntimeTopologyCursor = null;
        SetLegacyVirtualTrackPosition(train, projectedPositionMeters);
    }

    private static void SetLegacyVirtualTrackPosition(MutableTrain train, double projectedPositionMeters)
    {
        train.Position = projectedPositionMeters;
        train.CurrentTrackEdgeId = string.IsNullOrWhiteSpace(train.TrackId)
            ? null
            : $"LEGACY:{train.TrackId}";
        train.OffsetMeters = projectedPositionMeters;
        train.ServiceRouteTraversalIndex = null;
        train.ProjectedChainageMeters = null;
    }

    private void ResumeMainlineTopologyTraversal(MutableTrain train)
    {
        train.UsesLegacyVirtualTrack = false;
        SetTrainPositionFromProjectedCoordinate(train, train.Position);
    }

    private void SynchronizeTrainTopologyPosition(MutableTrain train)
    {
        if (!HasTopologyTraversalPosition(train))
        {
            if (!train.UsesLegacyVirtualTrack)
            {
                SetTrainPositionFromProjectedCoordinate(train, train.Position);
            }
            return;
        }

        SetTrainRuntimeTopologyCursor(train, GetMovementNavigator(train), train.RuntimeTopologyCursor!.Value);
    }

    private TopologyRouteNavigator GetTopologyNavigator(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? _outboundTopologyNavigator : _inboundTopologyNavigator;

    private TopologyMovementNavigator GetMainlineMovementNavigator(TrainDirection direction) =>
        direction == TrainDirection.Outbound ? _outboundMovementNavigator : _inboundMovementNavigator;

    private TopologyMovementNavigator GetMovementNavigator(TrainDirection direction) =>
        GetMainlineMovementNavigator(direction);

    private TopologyMovementNavigator GetMovementNavigator(MutableTrain train) =>
        train.TopologyPassingMovement?.Navigator
        ?? train.TopologyTurnbackMovement?.Navigator
        ?? GetMainlineMovementNavigator(train.Direction);

    private int GetCurrentServiceRouteLegIndex(MutableTrain train) =>
        train.TopologyPassingMovement?.DepartureLegIndex
        ?? train.TopologyTurnbackMovement?.DepartureLegIndex
        ?? 0;

    private int GetMovementTraversalIndex(MutableTrain train, int serviceRouteTraversalIndex)
    {
        var offset = train.TopologyPassingMovement?.DepartureServiceRouteTraversalOffset
            ?? train.TopologyTurnbackMovement?.DepartureServiceRouteTraversalOffset
            ?? 0;
        var result = serviceRouteTraversalIndex - offset;
        if (result < 0)
        {
            throw new SimulationValidationException([
                $"列車 {train.VehicleId} 已由折返設施接續至 route traversal {offset}，不能回頭指向 traversal {serviceRouteTraversalIndex}。"
            ]);
        }

        return result;
    }

    private int GetServiceRouteTraversalIndex(MutableTrain train, RuntimeTopologyCursor cursor) =>
        cursor.TraversalIndex + GetServiceRouteTraversalOffset(train, cursor);

    private static int GetServiceRouteTraversalOffset(MutableTrain train, RuntimeTopologyCursor cursor)
    {
        if (train.TopologyPassingMovement is { } passing
            && cursor.MovementLegIndex == passing.DepartureLegIndex)
        {
            return passing.DepartureServiceRouteTraversalOffset;
        }

        return train.TopologyTurnbackMovement is { } turnback
            && cursor.MovementLegIndex == turnback.DepartureLegIndex
            ? turnback.DepartureServiceRouteTraversalOffset
            : 0;
    }

    private TopologyTraversalCursor ToServiceRouteCursor(MutableTrain train)
    {
        var cursor = train.RuntimeTopologyCursor
            ?? throw new InvalidOperationException("列車沒有 topology runtime cursor。");
        if (GetMovementNavigator(train).Legs[cursor.MovementLegIndex].Kind != MovementLegKind.ServiceRoute)
            throw new SimulationValidationException(["特殊設施 traversal 不可套用 mainline ServiceRoute speed-limit adapter。"]);
        return new TopologyTraversalCursor(
            GetTopologyNavigator(train.Direction).ServiceRouteId,
            GetServiceRouteTraversalIndex(train, cursor),
            cursor.Position);
    }

    /// <summary>
    /// 將 topology cursor 寫入列車 runtime。PositionMeters / ProjectedChainageMeters 僅在這個
    /// adapter 邊界同步，供尚未遷移的結果與 legacy API 顯示；movement 不以它們回推 cursor。
    /// </summary>
    private void SetTrainRuntimeTopologyCursor(
        MutableTrain train,
        TopologyMovementNavigator navigator,
        RuntimeTopologyCursor cursor)
    {
        train.UsesLegacyVirtualTrack = false;
        train.RuntimeTopologyCursor = cursor;
        train.CurrentTrackEdgeId = cursor.Position.TrackEdgeId;
        train.OffsetMeters = cursor.Position.OffsetMeters;
        var movementLeg = navigator.Legs[cursor.MovementLegIndex];
        if (movementLeg.Kind == MovementLegKind.ServiceRoute)
        {
            var projection = GetRouteProjection(train.Direction);
            var serviceRouteTraversalIndex = GetServiceRouteTraversalIndex(train, cursor);
            var projectedChainage = projection.ToChainage(serviceRouteTraversalIndex, cursor.Position);
            train.ServiceRouteTraversalIndex = serviceRouteTraversalIndex;
            train.ProjectedChainageMeters = projectedChainage;
            train.Position = train.Direction == TrainDirection.Outbound
                ? projectedChainage
                : projection.TotalLengthMeters - projectedChainage;
            return;
        }

        train.ServiceRouteTraversalIndex = null;
        train.ProjectedChainageMeters = null;
        if (train.TopologyTurnbackMovement is { } transition)
        {
            var distance = navigator.GetDistanceFromStart(cursor) - transition.PlanStartDistanceMeters;
            train.Position = transition.PresentationStartPositionMeters
                + transition.PresentationDirection * distance;
        }
        else if (train.TopologyPassingMovement is { } passing)
        {
            var distance = navigator.GetDistanceFromStart(cursor) - passing.PlanStartDistanceMeters;
            train.Position = passing.PresentationStartPositionMeters
                + passing.PresentationDirection * distance;
        }
    }

    private sealed class MutableTrain
    {
        public string VehicleId { get; init; } = string.Empty;

        public int ServiceNumber { get; set; }

        public string ServiceRunId => string.IsNullOrWhiteSpace(ExplicitServiceRunId)
            ? $"{VehicleId.Replace("Vehicle ", "R", StringComparison.Ordinal)}-{(Direction == TrainDirection.Outbound ? "D" : "U")}{ServiceNumber:000}"
            : ExplicitServiceRunId;

        public double StartTime { get; set; }

        public double PlannedDepartureTime { get; set; }

        public double? ActualDepartureTime { get; set; }

        public TrainDirection Direction { get; set; }

        public string TrackId { get; set; } = string.Empty;

        public string ServiceClassId { get; set; } = DefaultServiceClassId;

        public string PatternId { get; set; } = DefaultPatternId;

        public string VehicleTypeId { get; set; } = "DEFAULT_VEHICLE";

        public string? PlatformId { get; set; }

        public string? ExplicitServiceRunId { get; set; }

        public OperationalConstraint Constraints { get; set; }

        public double Position { get; set; }

        /// <summary>正常主線與日後 facility traversal 共用的唯一權威列車位置。</summary>
        public RuntimeTopologyCursor? RuntimeTopologyCursor { get; set; }

        /// <summary>Phase E 主線 physical truth；virtual track 尚以 legacy adapter 隔離。</summary>
        public string? CurrentTrackEdgeId { get; set; }

        public double? OffsetMeters { get; set; }

        public int? ServiceRouteTraversalIndex { get; set; }

        public double? ProjectedChainageMeters { get; set; }

        /// <summary>尾軌、空間折返與越行在 Phase E 前仍使用舊有 virtual-track runtime。</summary>
        public bool UsesLegacyVirtualTrack { get; set; }

        public double Speed { get; set; }

        public double Acceleration { get; set; }

        public OperationalPhase Phase { get; set; }

        public int CurrentStationIndex { get; set; }

        public int NextStationIndex { get; set; }

        public double DwellRemaining { get; set; }

        public double TurnaroundRemaining { get; set; }

        public bool Active { get; set; }

        public bool ObstacleStopped { get; set; }

        public bool Collided { get; set; }

        public bool StationBrakingActive { get; set; }

        public bool StationStopViolationRecorded { get; set; }

        public string? ReservationId { get; set; }

        public string? RoutePathId { get; set; }

        public string? PendingDepartureTrackId { get; set; }

        public string? ExpectedDestinationPlatformId { get; set; }

        public double ReservedAtPosition { get; set; }

        public bool HasDestinationPlatformReservation { get; set; }

        public bool HasStationReservation { get; set; }

        public bool WaitingResourceEventEmitted { get; set; }

        public string? OvertakeFacilityId { get; set; }

        public string? OvertakenVehicleId { get; set; }

        public string? WaitingForOvertakeFacilityId { get; set; }

        public bool WaitingForOvertakeEventEmitted { get; set; }

        public bool AwaitingPostPassRoute { get; set; }

        public bool ContinueAfterTerminal { get; set; }

        public string? DispatchServiceRunBaseId { get; set; }

        public bool TurnaroundPrepared { get; set; }

        /// <summary>
        /// 實體 topology 尾軌返回反方向終點月台後，先完成正常停站，
        /// 再依接續車次時間進入 CompleteTurnaround。
        /// </summary>
        public bool AwaitingTopologyTurnbackDeparture { get; set; }

        public string? ContinuationServiceRunId { get; set; }

        public bool Completed { get; set; }

        public TerminalAction TerminalAction { get; set; }

        public TailTrackMovement? TailTrackMovement { get; set; }

        public SpatialTurnbackMovement? SpatialTurnbackMovement { get; set; }

        /// <summary>
        /// Schema 8 折返的跨 arrival / facility / departure traversal transition。其 cursor
        /// 與 footprint 一直留在同一份 physical movement plan，直到車尾越過 facility。
        /// </summary>
        public TopologyTurnbackMovement? TopologyTurnbackMovement { get; set; }

        /// <summary>Schema 8 越行分歧／匯合的連續 topology movement；不會轉成 legacy virtual track。</summary>
        public TopologyPassingMovement? TopologyPassingMovement { get; set; }
    }

    private sealed class TailTrackMovement(
        AfterStationTailTrackLayout layout,
        double waitRemainingSeconds)
    {
        public AfterStationTailTrackLayout Layout { get; } = layout;
        public double WaitRemainingSeconds { get; set; } = waitRemainingSeconds;
        public TailTrackMovementStage Stage { get; set; } = TailTrackMovementStage.RunningOutbound;
        public bool BrakingActive { get; set; }
    }

    private sealed class SpatialTurnbackMovement(
        SpatialReferencePointDefinition point,
        double virtualTurnbackPositionMeters,
        double waitRemainingSeconds)
    {
        public SpatialReferencePointDefinition Point { get; } = point;
        public double VirtualTurnbackPositionMeters { get; } = virtualTurnbackPositionMeters;
        public double WaitRemainingSeconds { get; set; } = waitRemainingSeconds;
        public SpatialTurnbackMovementStage Stage { get; set; } = SpatialTurnbackMovementStage.RunningOutbound;
        public bool BrakingActive { get; set; }
    }

    private sealed class TopologyTurnbackMovement(
        TurnbackOperationDefinition operation,
        TurnbackFacilityDefinition facility,
        TopologyMovementNavigator navigator,
        int facilityLegIndex,
        int departureLegIndex,
        RuntimeTopologyCursor turnbackStopCursor,
        RuntimeTopologyCursor? returnStartCursor,
        double waitRemainingSeconds,
        string? reservationId,
        IReadOnlyList<string> resourceIds,
        double presentationStartPositionMeters,
        int presentationDirection,
        double planStartDistanceMeters,
        int departureServiceRouteTraversalOffset)
    {
        public TurnbackOperationDefinition Operation { get; } = operation;
        public TurnbackFacilityDefinition Facility { get; } = facility;
        public TopologyMovementNavigator Navigator { get; } = navigator;
        public int FacilityLegIndex { get; } = facilityLegIndex;
        public int DepartureLegIndex { get; } = departureLegIndex;
        public RuntimeTopologyCursor TurnbackStopCursor { get; } = turnbackStopCursor;
        public RuntimeTopologyCursor? ReturnStartCursor { get; } = returnStartCursor;
        public double WaitRemainingSeconds { get; set; } = waitRemainingSeconds;
        public string? ReservationId { get; } = reservationId;
        public IReadOnlyList<string> ResourceIds { get; } = resourceIds;
        public double PresentationStartPositionMeters { get; } = presentationStartPositionMeters;
        public int PresentationDirection { get; } = presentationDirection;
        public double PlanStartDistanceMeters { get; } = planStartDistanceMeters;
        public int DepartureServiceRouteTraversalOffset { get; } = departureServiceRouteTraversalOffset;
        public TopologyTurnbackStage Stage { get; set; } = TopologyTurnbackStage.RunningToTurnback;
        public bool BrakingActive { get; set; }
        public bool IsOperating { get; set; } = true;
    }

    private sealed class TopologyPassingMovement(
        PassingOperationDefinition operation,
        PassingFacilityDefinition facility,
        TopologyMovementNavigator navigator,
        int facilityLegIndex,
        int departureLegIndex,
        int departureServiceRouteTraversalOffset,
        int passedStationIndex,
        string? reservationId,
        IReadOnlyList<string> resourceIds,
        double presentationStartPositionMeters,
        int presentationDirection,
        double planStartDistanceMeters)
    {
        public PassingOperationDefinition Operation { get; } = operation;
        public PassingFacilityDefinition Facility { get; } = facility;
        public TopologyMovementNavigator Navigator { get; } = navigator;
        public int FacilityLegIndex { get; } = facilityLegIndex;
        public int DepartureLegIndex { get; } = departureLegIndex;
        public int DepartureServiceRouteTraversalOffset { get; } = departureServiceRouteTraversalOffset;
        public int PassedStationIndex { get; } = passedStationIndex;
        public string? ReservationId { get; } = reservationId;
        public IReadOnlyList<string> ResourceIds { get; } = resourceIds;
        public double PresentationStartPositionMeters { get; } = presentationStartPositionMeters;
        public int PresentationDirection { get; } = presentationDirection;
        public double PlanStartDistanceMeters { get; } = planStartDistanceMeters;
        public bool IsOperating { get; set; } = true;
    }

    private sealed record TopologyPassingCandidate(
        PassingOperationDefinition Operation,
        PassingFacilityDefinition Facility,
        MutableTrain LocalTrain);

    private enum TailTrackMovementStage
    {
        RunningOutbound,
        WaitingAtVirtualNode,
        ReturningToStation
    }

    private enum SpatialTurnbackMovementStage
    {
        RunningOutbound,
        WaitingAtVirtualTurnbackPoint,
        ReturningToStation
    }

    private enum TopologyTurnbackStage
    {
        RunningToTurnback,
        WaitingAtTurnback,
        ReturningToDeparture
    }

    private enum TerminalAction
    {
        None,
        ExitService,
        Turnaround
    }

    private readonly record struct ServiceRunPlanKey(
        string VehicleId,
        int ServiceNumber,
        TrainDirection Direction);

    private readonly record struct PlatformRotationKey(
        string StationId,
        TrainDirection Direction);

    private readonly record struct StationOvertakeSelection(
        Station Station,
        StationOvertakeFacilityDefinition Facility,
        MutableTrain LocalTrain);

    private sealed class ScheduledObstacle(string vehicleId, double triggerTimeSeconds)
    {
        public string VehicleId { get; } = vehicleId;

        public double TriggerTimeSeconds { get; } = triggerTimeSeconds;

        public bool Triggered { get; set; }
    }
}
