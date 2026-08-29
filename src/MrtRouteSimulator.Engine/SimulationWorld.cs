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
    private readonly ResolvedDispatchPlan? _dispatchPlan;
    private readonly RouteResourceReservationManager _resourceReservations = new();
    private readonly bool _enforceRouteResources;
    private IReadOnlyList<SafetyObservation> _currentSafety = [];
    private int _nextEventIndex;

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
        IEnumerable<ServiceTypeDefinition>? serviceTypes = null)
    {
        if (trainCount <= 0)
        {
            throw new SimulationValidationException(["列車數量必須大於 0。"]);
        }

        Route = route ?? throw new ArgumentNullException(nameof(route));
        TrainParameters = trainParameters ?? throw new ArgumentNullException(nameof(trainParameters));
        OperationalParameters = operationalParameters ?? throw new ArgumentNullException(nameof(operationalParameters));
        SpeedLimits = new SpeedLimitService(route, speedLimits);
        ProfileMode = profileMode;
        MovingBlockMode = movingBlockMode;
        BrakingEstimationMode = BrakingEstimationMode.Service;
        TraceRetentionPolicy = traceRetentionPolicy ?? SimulationTraceRetentionPolicy.Full;
        _traceStore = new SimulationTraceStore(TraceRetentionPolicy);
        _enforceRouteResources = infrastructure is not null || dispatchPlan is not null;
        Infrastructure = infrastructure ?? InfrastructureGraph.CreateLegacy(route);
        Infrastructure.Validate();
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

        var baseline = TripSimulator.SimulateMultipleTrains(
            route,
            trainParameters,
            runtimeTrainCount,
            initialDepartureIntervalSeconds);
        HeadwaySeconds = baseline.HeadwaySeconds;
        BaselineCycleTimeSeconds = baseline.CycleTimeSeconds;
        InitializeTrains(runtimeTrainCount);
        ActivateDueTrains();
    }

    public Route Route { get; }

    public TrainParameters TrainParameters { get; }

    public OperationalParameters OperationalParameters { get; }

    public SpeedLimitService SpeedLimits { get; }

    public SimulationEngineKind EngineKind => SimulationEngineKind.V2RealisticOperations;

    public InfrastructureGraph Infrastructure { get; }

    public ResolvedDispatchPlan? DispatchPlan => _dispatchPlan;

    public OperationProfileMode ProfileMode { get; }

    public MovingBlockMode MovingBlockMode { get; private set; }

    public BrakingEstimationMode BrakingEstimationMode { get; private set; }

    public double CurrentTimeSeconds { get; private set; }

    public double HeadwaySeconds { get; }

    public double BaselineCycleTimeSeconds { get; }

    public double TimeStepSeconds => FixedTimeStepSeconds;

    public SimulationTraceRetentionPolicy TraceRetentionPolicy { get; }

    /// <summary>所有已排入世界的車輛均已完成最後一個車次並退出營運。</summary>
    public bool IsComplete => _trains.Count > 0 && _trains.All(train => train.Completed);

    public IReadOnlyList<TrajectorySample> Trajectory => _traceStore.Trajectory;

    public IReadOnlyList<SafetyObservation> SafetyHistory => _safetyHistory;

    public IReadOnlyList<SimulationEvent> Events => _events;

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

        ApplyCollisionProtection();
        _currentSafety = MovingBlockMode == MovingBlockMode.Independent
            ? []
            : ComputeSafetyObservations(recordStatusEvents: true);
        _safetyHistory.AddRange(_currentSafety);
        RecordTrajectory();

        return new SimulationSnapshot(
            CurrentTimeSeconds,
            _trains.Select(ToState).ToArray(),
            _currentSafety,
            _newEvents.ToArray());
    }

    public void Reset()
    {
        var trainCount = _trains.Count;
        foreach (var train in _trains)
        {
            ReleaseRouteReservation(train);
        }
        CurrentTimeSeconds = 0;
        _traceStore.Reset();
        _safetyHistory.Clear();
        _events.Clear();
        _newEvents.Clear();
        _currentSafety = [];
        _lastSafetyStatuses.Clear();
        _controlBrakingActive.Clear();
        _lastDestinationPlatforms.Clear();
        _destinationPlatformAllocationCounts.Clear();
        _platformLastReleasedAtSeconds.Clear();
        _scheduledObstacles.Clear();
        _nextEventIndex = 0;
        InitializeTrains(trainCount);
        ActivateDueTrains();
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

    private void InitializeServicePlans(
        int trainCount,
        IEnumerable<ServicePattern>? servicePatterns,
        IEnumerable<ServiceRunPlan>? serviceRunPlans)
    {
        _servicePatterns.Clear();
        _serviceRunPlans.Clear();
        var stationIds = Route.Stations.Select(station => station.StationId).ToHashSet(StringComparer.Ordinal);
        var endpointIds = new HashSet<string>(StringComparer.Ordinal)
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

                    var spatialReference = Infrastructure.FindSpatialReferencePoint(stationId);
                    if (spatialReference?.Kind != SpatialReferencePointKind.CentralSidingTurnback)
                    {
                        errors.Add($"服務模式 {patternId} 的 {stationId} 折返需對應 CentralSidingTurnback 空間參考點。");
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
                var stationIndex = outbound ? 0 : Route.Stations.Count - 1;
                var train = new MutableTrain
                {
                    VehicleId = run.VehicleId!,
                    ServiceNumber = 1,
                    StartTime = RelativeScheduleSeconds(run.PlannedDepartureTime, _dispatchPlan.ScheduleAnchorTime),
                    PlannedDepartureTime = RelativeScheduleSeconds(run.PlannedDepartureTime, _dispatchPlan.ScheduleAnchorTime),
                    Direction = run.Direction,
                    TrackId = outbound ? "DOWN" : "UP",
                    Position = Route.Stations[stationIndex].PositionMeters,
                    CurrentStationIndex = stationIndex,
                    NextStationIndex = stationIndex + (int)run.Direction,
                    Phase = OperationalPhase.Pending,
                    ContinueAfterTerminal = run.ContinueAfterTerminal,
                    DispatchServiceRunBaseId = run.ServiceRunId,
                    ContinuationServiceRunId = run.ContinuationServiceRunId
                };
                ApplyServicePlan(train);
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

    private void UpdateTrain(
        MutableTrain train,
        IReadOnlyDictionary<string, double> controlLimits,
        IReadOnlyDictionary<string, string> protectionLeaders)
    {
        train.Constraints = OperationalConstraint.None;
        ReleaseDepartureRouteIfClear(train);
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
                if (train.TerminalAction != TerminalAction.None)
                {
                    CompleteTerminalStationWork(train);
                    return;
                }

                if (ShouldHoldForStationOvertake(train))
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

        var nextStation = Route.Stations[train.NextStationIndex];
        var stationInstruction = GetStationInstruction(train, nextStation);
        var enteredStationOvertake = TryEnterStationOvertake(train);
        var isScheduledStop = stationInstruction.Mode is StationServiceMode.Stop or StationServiceMode.Turnback;
        var distanceToStation = ForwardDistance(train.Direction, train.Position, nextStation.PositionMeters);
        if (isScheduledStop
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToStation <= StationStopSnapToleranceMeters)
        {
            ArriveAtStation(train, nextStation);
            return;
        }

        var effectiveServiceBraking = performance.ServiceBrakingMetersPerSecondSquared;
        var permitted = SpeedLimits.GetPermittedSpeedMetersPerSecond(
            train.Position,
            train.Direction,
            performance.MaxSpeedMetersPerSecond,
            effectiveServiceBraking,
            performance.JerkMetersPerSecondCubed,
            train.Speed);

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
        if (isScheduledStop && stationStopControl is { RequiresServiceBraking: true })
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
        var hardCurrentLimit = SpeedLimits.GetCurrentLimitMetersPerSecond(
            train.Position,
            train.Direction,
            performance.MaxSpeedMetersPerSecond);
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
            train.Position += Math.Max(0, movementAuthority) * (int)train.Direction;
            train.Speed = 0;
            train.Acceleration = 0;
            train.Phase = OperationalPhase.Braking;
            return;
        }

        if (!enteredStationOvertake
            && overtakeEntryDistance is { } pendingOvertakeEntryDistance
            && traveled > pendingOvertakeEntryDistance + NumericalTolerance)
        {
            train.Position += Math.Max(0, pendingOvertakeEntryDistance) * (int)train.Direction;
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
            ArriveAtStation(train, nextStation);
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

            train.Position = nextStation.PositionMeters - (int)train.Direction * NumericalTolerance;
            train.Speed = newSpeed;
            train.Phase = OperationalPhase.ApproachBraking;
            train.StationBrakingActive = true;
            return;
        }

        if (!isScheduledStop && traveled >= distanceToStation - NumericalTolerance)
        {
            train.Position += traveled * (int)train.Direction;
            train.Speed = newSpeed;
            PassStation(train, nextStation);
            train.Phase = ClassifyPhase(train, desiredAcceleration, permitted, double.PositiveInfinity);
            return;
        }

        train.Position += traveled * (int)train.Direction;
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
            || leader.Direction != follower.Direction
            || leader.TrackId != follower.TrackId)
        {
            return false;
        }

        var leaderLength = GetVehiclePerformance(leader).LengthMeters;
        var leaderRear = leader.Direction == TrainDirection.Outbound
            ? leader.Position - leaderLength
            : leader.Position + leaderLength;
        var actualGap = leader.Direction == TrainDirection.Outbound
            ? leaderRear - follower.Position
            : follower.Position - leaderRear;
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
        double effectiveBraking)
    {
        if (distanceToStation <= StationBrakingLookAheadMeters)
        {
            return true;
        }

        var jerkLimited = ProfileMode == OperationProfileMode.RealisticOperations;
        var performance = GetVehiclePerformance(train);
        var stoppingDistance = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
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

    private void ArriveAtStation(MutableTrain train, Station station)
    {
        train.Position = station.PositionMeters;
        train.Speed = 0;
        train.StationBrakingActive = false;
        train.StationStopViolationRecorded = false;
        train.CurrentStationIndex = train.NextStationIndex;
        RetainDestinationPlatformOrReleaseRoute(train);
        train.PlatformId = train.ExpectedDestinationPlatformId;
        train.ExpectedDestinationPlatformId = null;
        AssignStationTrack(train, train.PlatformId);
        train.WaitingForOvertakeEventEmitted = false;
        train.Phase = OperationalPhase.Arriving;
        AddEvent(SimulationEventType.Arrival, train, null, $"{train.ServiceRunId} 抵達 {station.StationId}。", train.Position, 0);

        var isTerminal = train.Direction == TrainDirection.Outbound
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
            var spatialReference = Infrastructure.FindSpatialReferencePoint(station.StationId);
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

        var intermediateReference = Infrastructure.FindSpatialReferencePoint(station.StationId);
        var instructionDwellSeconds = GetStationInstruction(train, station).DwellTimeSeconds;
        train.DwellRemaining = GetConfiguredStationDwellSeconds(
            intermediateReference,
            train.Direction,
            instructionDwellSeconds ?? station.DwellTimeSeconds);
        train.NextStationIndex += (int)train.Direction;
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
        train.NextStationIndex += (int)train.Direction;
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

    private void CompleteTurnaround(MutableTrain train)
    {
        PrepareTurnaround(train);

        if (CurrentTimeSeconds + NumericalTolerance < train.PlannedDepartureTime)
        {
            return;
        }

        if (!TryReserveDepartureRoute(train)
            || (MovingBlockMode == MovingBlockMode.Control && !HasDepartureClearance(train)))
        {
            train.Constraints |= OperationalConstraint.RouteResource | OperationalConstraint.Platform;
            return;
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
    }

    private void PrepareTurnaround(MutableTrain train)
    {
        if (train.TurnaroundPrepared)
        {
            return;
        }

        train.Direction = train.Direction == TrainDirection.Outbound
            ? TrainDirection.Inbound
            : TrainDirection.Outbound;
        train.TrackId = train.Direction == TrainDirection.Outbound ? "DOWN" : "UP";
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

        // 站後尾軌折返回站時，月台應由實體折返拓撲決定，而不是沿用到達側或
        // 被未指定的自動派車覆蓋。這也讓後續出站進路以反方向月台為起點預約。
        var station = Route.Stations[train.CurrentStationIndex];
        var detailedTurnback = Infrastructure.TurnbackPlans.FirstOrDefault(plan =>
            plan.Kind == TurnbackKind.AfterStation
            && plan.StationId.Equals(station.StationId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(detailedTurnback?.DeparturePlatformId))
        {
            train.PlatformId = detailedTurnback.DeparturePlatformId;
        }

        train.NextStationIndex = train.CurrentStationIndex + (int)train.Direction;
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

        var station = Route.Stations[train.CurrentStationIndex];
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
            train.Position = targetPosition - (int)train.Direction * NumericalTolerance;
            train.Speed = newSpeed;
            train.Phase = movement.Stage == TailTrackMovementStage.RunningOutbound
                ? OperationalPhase.TailTrackOutbound
                : OperationalPhase.TailTrackReturn;
            movement.BrakingActive = true;
            return;
        }

        train.Position += traveled * (int)train.Direction;
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
        train.Position = targetPosition;
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
            train.Position = targetPosition - (int)train.Direction * NumericalTolerance;
            train.Speed = newSpeed;
            movement.BrakingActive = true;
        }
        else
        {
            train.Position += traveled * (int)train.Direction;
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
        train.Position = targetPosition;
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
        var groups = _trains
            .Where(train => train.Active
                && train.Phase != OperationalPhase.OutOfService
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

    private SafetyObservation CalculateSafetyObservation(MutableTrain follower, MutableTrain leader)
    {
        var followerPerformance = GetVehiclePerformance(follower);
        var leaderPerformance = GetVehiclePerformance(leader);
        var leaderRear = leader.Direction == TrainDirection.Outbound
            ? leader.Position - leaderPerformance.LengthMeters
            : leader.Position + leaderPerformance.LengthMeters;
        var actualGap = leader.Direction == TrainDirection.Outbound
            ? leaderRear - follower.Position
            : follower.Position - leaderRear;
        var headDistance = Math.Abs(leader.Position - follower.Position);
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
            follower.TrackId,
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

    private void ApplyCollisionProtection()
    {
        var groups = _trains
            .Where(train => train.Active && train.Phase != OperationalPhase.OutOfService)
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
                follower.Position = Math.Clamp(leaderRear, 0, Route.TotalLengthMeters);
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

    private void RecordTrajectory()
    {
        foreach (var train in _trains.Where(train => train.Active))
        {
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
                Route.Stations[train.CurrentStationIndex].StationId,
                train.NextStationIndex >= 0 && train.NextStationIndex < Route.Stations.Count
                    ? Route.Stations[train.NextStationIndex].StationId
                    : null,
                false,
                train.VehicleTypeId,
                train.PlatformId,
                train.Constraints);
            _traceStore.Record(sample);
        }
    }

    private WorldTrainState ToState(MutableTrain train)
    {
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
            Route.Stations[train.CurrentStationIndex].StationId,
            train.NextStationIndex >= 0 && train.NextStationIndex < Route.Stations.Count
                ? Route.Stations[train.NextStationIndex].StationId
                : null,
            train.Active,
            CurrentTimeSeconds,
            train.VehicleTypeId,
            train.PlatformId,
            train.PlannedDepartureTime,
            train.ActualDepartureTime,
            train.Constraints);
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
            resourceIds);
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
        direction == TrainDirection.Outbound ? position : Route.TotalLengthMeters - position;

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

        public string? ContinuationServiceRunId { get; set; }

        public bool Completed { get; set; }

        public TerminalAction TerminalAction { get; set; }

        public TailTrackMovement? TailTrackMovement { get; set; }

        public SpatialTurnbackMovement? SpatialTurnbackMovement { get; set; }
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
