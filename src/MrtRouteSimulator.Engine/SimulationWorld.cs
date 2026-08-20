namespace MrtRouteSimulator.Engine;

public sealed class SimulationWorld
{
    public const double FixedTimeStepSeconds = 0.1;

    private const double NumericalTolerance = 1e-7;
    private const double ArrivalPositionToleranceMeters = 0.5;
    private const double ArrivalSpeedToleranceMetersPerSecond = 0.15;
    private const double StationBrakingLookAheadMeters = 0.05;
    private const double MovingBlockControlLookAheadSeconds = 0.5;
    private const string DefaultPatternId = "ALL_STOP";
    private const string DefaultServiceClassId = "普通車";
    private readonly List<MutableTrain> _trains = [];
    private readonly List<TrajectorySample> _trajectory = [];
    private readonly List<SafetyObservation> _safetyHistory = [];
    private readonly List<SimulationEvent> _events = [];
    private readonly List<SimulationEvent> _newEvents = [];
    private readonly List<ScheduledObstacle> _scheduledObstacles = [];
    private readonly Dictionary<string, SafetyStatus> _lastSafetyStatuses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _controlBrakingActive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ServicePattern> _servicePatterns = new(StringComparer.Ordinal);
    private readonly Dictionary<ServiceRunPlanKey, ServiceRunPlan> _serviceRunPlans = [];
    private IReadOnlyList<SafetyObservation> _currentSafety = [];
    private int _nextEventIndex;

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
        IEnumerable<ServiceRunPlan>? serviceRunPlans = null)
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
        InitializeServicePlans(trainCount, servicePatterns, serviceRunPlans);

        var baseline = TripSimulator.SimulateMultipleTrains(
            route,
            trainParameters,
            trainCount,
            initialDepartureIntervalSeconds);
        HeadwaySeconds = baseline.HeadwaySeconds;
        BaselineCycleTimeSeconds = baseline.CycleTimeSeconds;
        InitializeTrains(trainCount);
        ActivateDueTrains();
    }

    public Route Route { get; }

    public TrainParameters TrainParameters { get; }

    public OperationalParameters OperationalParameters { get; }

    public SpeedLimitService SpeedLimits { get; }

    public OperationProfileMode ProfileMode { get; }

    public MovingBlockMode MovingBlockMode { get; private set; }

    public BrakingEstimationMode BrakingEstimationMode { get; private set; }

    public double CurrentTimeSeconds { get; private set; }

    public double HeadwaySeconds { get; }

    public double BaselineCycleTimeSeconds { get; }

    public double TimeStepSeconds => FixedTimeStepSeconds;

    public IReadOnlyList<TrajectorySample> Trajectory => _trajectory;

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
        CurrentTimeSeconds = 0;
        _trajectory.Clear();
        _safetyHistory.Clear();
        _events.Clear();
        _newEvents.Clear();
        _currentSafety = [];
        _lastSafetyStatuses.Clear();
        _controlBrakingActive.Clear();
        _scheduledObstacles.Clear();
        _nextEventIndex = 0;
        InitializeTrains(trainCount);
        ActivateDueTrains();
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
            if (string.Equals(patternId, DefaultPatternId, StringComparison.Ordinal))
            {
                errors.Add($"服務模式編號 {DefaultPatternId} 為系統保留值。");
                continue;
            }

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

                if (instruction.SpeedLimitMetersPerSecond is { } limit
                    && (!double.IsFinite(limit) || limit <= 0))
                {
                    errors.Add($"服務模式 {patternId} 的 {stationId} 速度上限必須是有限正數。");
                }
            }

            _servicePatterns.Add(patternId, pattern with
            {
                PatternId = patternId,
                PatternName = string.IsNullOrWhiteSpace(pattern.PatternName) ? patternId : pattern.PatternName.Trim(),
                Instructions = instructions.ToArray()
            });
        }

        var validVehicleIds = Enumerable.Range(1, trainCount)
            .Select(index => $"Vehicle {index:00}")
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
                PatternId = patternId
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
            return;
        }

        train.ServiceClassId = DefaultServiceClassId;
        train.PatternId = DefaultPatternId;
    }

    private StationServiceInstruction GetStationInstruction(MutableTrain train, Station station)
    {
        if (string.Equals(train.PatternId, DefaultPatternId, StringComparison.Ordinal)
            || !_servicePatterns.TryGetValue(train.PatternId, out var pattern))
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
        for (var index = 0; index < trainCount; index++)
        {
            var train = new MutableTrain
            {
                VehicleId = $"Vehicle {index + 1:00}",
                ServiceNumber = 1,
                StartTime = index * HeadwaySeconds,
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
            .Where(train => !train.Active && CurrentTimeSeconds + NumericalTolerance >= train.StartTime)
            .OrderBy(train => train.StartTime))
        {
            if (MovingBlockMode == MovingBlockMode.Control && !HasDepartureClearance(train))
            {
                continue;
            }

            train.Active = true;
            train.Phase = OperationalPhase.Accelerating;
            AddEvent(SimulationEventType.Departure, train, null, $"{train.ServiceRunId} 發車。", train.Position, 0);
        }
    }

    private bool HasDepartureClearance(MutableTrain train)
    {
        var nearestLeader = _trains
            .Where(other => other.Active
                && other.Phase != OperationalPhase.OutOfService
                && other.Direction == train.Direction
                && other.TrackId == train.TrackId
                && Progress(train.Direction, other.Position) >= Progress(train.Direction, train.Position))
            .OrderBy(other => Math.Abs(other.Position - train.Position))
            .FirstOrDefault();
        if (nearestLeader is null)
        {
            return true;
        }

        var leaderRear = nearestLeader.Direction == TrainDirection.Outbound
            ? nearestLeader.Position - OperationalParameters.TrainLengthMeters
            : nearestLeader.Position + OperationalParameters.TrainLengthMeters;
        var gap = nearestLeader.Direction == TrainDirection.Outbound
            ? leaderRear - train.Position
            : train.Position - leaderRear;
        var stationarySafetyDistance = Math.Max(
            OperationalParameters.AbsoluteMinimumGapMeters,
            2 * OperationalParameters.PositioningErrorMeters + OperationalParameters.SafetyMarginMeters);
        return gap >= stationarySafetyDistance - NumericalTolerance;
    }

    private void UpdateTrain(
        MutableTrain train,
        IReadOnlyDictionary<string, double> controlLimits,
        IReadOnlyDictionary<string, string> protectionLeaders)
    {
        if (!train.Active || train.Phase == OperationalPhase.OutOfService || train.Collided || train.ObstacleStopped)
        {
            return;
        }

        if (train.DwellRemaining > NumericalTolerance)
        {
            train.DwellRemaining = Math.Max(0, train.DwellRemaining - FixedTimeStepSeconds);
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train.Acceleration);
            train.Phase = OperationalPhase.Dwelling;
            if (train.DwellRemaining <= NumericalTolerance)
            {
                train.Phase = OperationalPhase.Accelerating;
                AddEvent(SimulationEventType.Departure, train, null, $"{train.ServiceRunId} 停站後發車。", train.Position, 0);
            }

            return;
        }

        if (train.TurnaroundRemaining > NumericalTolerance)
        {
            train.TurnaroundRemaining = Math.Max(0, train.TurnaroundRemaining - FixedTimeStepSeconds);
            train.Speed = 0;
            train.Acceleration = MoveAccelerationTowardZero(train.Acceleration);
            train.Phase = OperationalPhase.Turning;
            if (train.TurnaroundRemaining <= NumericalTolerance)
            {
                CompleteTurnaround(train);
            }

            return;
        }

        var nextStation = Route.Stations[train.NextStationIndex];
        var stationInstruction = GetStationInstruction(train, nextStation);
        var isScheduledStop = stationInstruction.Mode == StationServiceMode.Stop;
        var distanceToStation = ForwardDistance(train.Direction, train.Position, nextStation.PositionMeters);
        if (isScheduledStop
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToStation <= ArrivalPositionToleranceMeters)
        {
            ArriveAtStation(train, nextStation);
            return;
        }

        var effectiveServiceBraking = Math.Min(
            TrainParameters.DecelerationMetersPerSecondSquared,
            OperationalParameters.ServiceBrakingMetersPerSecondSquared);
        var permitted = SpeedLimits.GetPermittedSpeedMetersPerSecond(
            train.Position,
            train.Direction,
            TrainParameters.MaxSpeedMetersPerSecond,
            effectiveServiceBraking,
            OperationalParameters.JerkMetersPerSecondCubed,
            train.Speed);

        if (isScheduledStop)
        {
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
                            TrainParameters.MaxSpeedMetersPerSecond,
                            effectiveServiceBraking,
                            OperationalParameters.JerkMetersPerSecondCubed,
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
                    TrainParameters.MaxSpeedMetersPerSecond,
                    effectiveServiceBraking,
                    OperationalParameters.JerkMetersPerSecondCubed,
                    train.Speed,
                    nextStation.PositionMeters,
                    passingLimit));
        }

        var obstacleDistance = GetObstacleDistanceAhead(train);
        if (obstacleDistance is not null)
        {
            permitted = Math.Min(permitted, CalculateStopCurveSpeed(train.Speed, obstacleDistance.Value));
        }

        if (controlLimits.TryGetValue(train.VehicleId, out var movingBlockLimit))
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
        }
        else
        {
            _controlBrakingActive.Remove(train.VehicleId);
        }

        if (train.StationBrakingActive
            && train.Speed <= ArrivalSpeedToleranceMetersPerSecond
            && distanceToStation > ArrivalPositionToleranceMeters)
        {
            train.StationBrakingActive = false;
        }

        var desiredAcceleration = CalculateDesiredAcceleration(train, permitted);
        if (isScheduledStop
            && (train.StationBrakingActive
                || ShouldBeginStationBraking(
                    train,
                    distanceToStation,
                    desiredAcceleration,
                    effectiveServiceBraking)))
        {
            train.StationBrakingActive = true;
            desiredAcceleration = -effectiveServiceBraking;
        }
        else if (!isScheduledStop)
        {
            train.StationBrakingActive = false;
            train.StationStopViolationRecorded = false;
        }

        if (controlLimits.TryGetValue(train.VehicleId, out var protectionLimit)
            && train.Speed > protectionLimit + 0.03)
        {
            desiredAcceleration = -effectiveServiceBraking;
        }

        if (ProfileMode == OperationProfileMode.RealisticOperations)
        {
            var maximumChange = OperationalParameters.JerkMetersPerSecondCubed * FixedTimeStepSeconds;
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
            TrainParameters.MaxSpeedMetersPerSecond);
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

        var remainingAfterMove = distanceToStation - traveled;
        if (isScheduledStop
            && newSpeed <= ArrivalSpeedToleranceMetersPerSecond
            && remainingAfterMove <= ArrivalPositionToleranceMeters)
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

        var leaderRear = leader.Direction == TrainDirection.Outbound
            ? leader.Position - OperationalParameters.TrainLengthMeters
            : leader.Position + OperationalParameters.TrainLengthMeters;
        var actualGap = leader.Direction == TrainDirection.Outbound
            ? leaderRear - follower.Position
            : follower.Position - leaderRear;
        var requiredGap = CalculateDynamicSafetyDistance(
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
        var stoppingDistance = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
            train.Speed,
            train.Acceleration,
            effectiveBraking,
            OperationalParameters.JerkMetersPerSecondCubed,
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
                OperationalParameters.JerkMetersPerSecondCubed * FixedTimeStepSeconds)
            : desiredAccelerationIfWaiting;
        var previewSpeed = Math.Max(0, train.Speed + previewAcceleration * FixedTimeStepSeconds);
        var previewTravel = Math.Max(0, (train.Speed + previewSpeed) * 0.5 * FixedTimeStepSeconds);
        var previewStoppingDistance = BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
            previewSpeed,
            previewAcceleration,
            effectiveBraking,
            OperationalParameters.JerkMetersPerSecondCubed,
            FixedTimeStepSeconds,
            jerkLimited).DistanceMeters;
        return previewTravel + previewStoppingDistance + StationBrakingLookAheadMeters >= distanceToStation;
    }

    private double CalculateDesiredAcceleration(MutableTrain train, double permittedSpeed)
    {
        var speedError = permittedSpeed - train.Speed;
        if (speedError < -0.03)
        {
            return -Math.Min(
                TrainParameters.DecelerationMetersPerSecondSquared,
                OperationalParameters.ServiceBrakingMetersPerSecondSquared);
        }

        if (speedError <= 0.12)
        {
            return 0;
        }

        var coastingThreshold = permittedSpeed * (1 - OperationalParameters.CoastingRatio * 0.35);
        if (OperationalParameters.CoastingRatio > 0 && train.Speed >= coastingThreshold)
        {
            return 0;
        }

        var speedRatio = TrainParameters.MaxSpeedMetersPerSecond <= 0
            ? 1
            : Math.Clamp(train.Speed / TrainParameters.MaxSpeedMetersPerSecond, 0, 1);
        var tractionFactor = 1 - OperationalParameters.TractionFadeRatio * speedRatio;
        return TrainParameters.AccelerationMetersPerSecondSquared * Math.Max(0.1, tractionFactor);
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
        train.Phase = OperationalPhase.Arriving;
        AddEvent(SimulationEventType.Arrival, train, null, $"{train.ServiceRunId} 抵達 {station.StationId}。", train.Position, 0);

        var isTerminal = train.Direction == TrainDirection.Outbound
            ? train.CurrentStationIndex == Route.Stations.Count - 1
            : train.CurrentStationIndex == 0;
        if (isTerminal)
        {
            train.TurnaroundRemaining = train.Direction == TrainDirection.Outbound
                ? TrainParameters.TerminalTurnaroundTimeSeconds
                : TrainParameters.OriginTurnaroundTimeSeconds;
            train.Phase = OperationalPhase.Turning;
            AddEvent(
                SimulationEventType.TurnaroundStarted,
                train,
                null,
                $"{train.ServiceRunId} 開始折返。",
                train.Position,
                0);
            if (train.TurnaroundRemaining <= NumericalTolerance)
            {
                CompleteTurnaround(train);
            }

            return;
        }

        train.DwellRemaining = station.DwellTimeSeconds;
        train.NextStationIndex += (int)train.Direction;
        if (train.DwellRemaining > NumericalTolerance)
        {
            train.Phase = OperationalPhase.Dwelling;
            AddEvent(SimulationEventType.DwellStarted, train, null, $"{station.StationId} 停站。", train.Position, 0);
        }
    }

    private void PassStation(MutableTrain train, Station station)
    {
        train.CurrentStationIndex = train.NextStationIndex;
        train.NextStationIndex += (int)train.Direction;
        AddEvent(
            SimulationEventType.StationPassed,
            train,
            null,
            $"{train.ServiceRunId}（{train.ServiceClassId}）通過 {station.StationId}，速度 {train.Speed * 3.6:0.##} km/h。",
            station.PositionMeters,
            train.Speed);
    }

    private void CompleteTurnaround(MutableTrain train)
    {
        train.Direction = train.Direction == TrainDirection.Outbound
            ? TrainDirection.Inbound
            : TrainDirection.Outbound;
        train.TrackId = train.Direction == TrainDirection.Outbound ? "DOWN" : "UP";
        train.ServiceNumber++;
        ApplyServicePlan(train);
        train.NextStationIndex = train.CurrentStationIndex + (int)train.Direction;
        train.StationBrakingActive = false;
        train.StationStopViolationRecorded = false;
        train.Phase = OperationalPhase.Accelerating;
        AddEvent(
            SimulationEventType.DirectionChanged,
            train,
            null,
            $"車輛 {train.VehicleId} 折返，開始新車次 {train.ServiceRunId}。",
            train.Position,
            0);
        AddEvent(SimulationEventType.Departure, train, null, $"{train.ServiceRunId} 發車。", train.Position, 0);
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
        var leaderRear = leader.Direction == TrainDirection.Outbound
            ? leader.Position - OperationalParameters.TrainLengthMeters
            : leader.Position + OperationalParameters.TrainLengthMeters;
        var actualGap = leader.Direction == TrainDirection.Outbound
            ? leaderRear - follower.Position
            : follower.Position - leaderRear;
        var headDistance = Math.Abs(leader.Position - follower.Position);
        var timeGap = follower.Speed > 1e-6 ? Math.Max(0, headDistance / follower.Speed) : double.PositiveInfinity;

        var reactionDistance = follower.Speed * OperationalParameters.ControlReactionTimeSeconds;
        var buildDistance = follower.Speed * OperationalParameters.BrakeBuildUpTimeSeconds;
        var dynamicSafety = CalculateDynamicSafetyDistance(
            follower.Speed,
            follower.Acceleration,
            leader.Speed);

        var selectedBraking = BrakingEstimationMode == BrakingEstimationMode.Service
            ? OperationalParameters.ServiceBrakingMetersPerSecondSquared
            : OperationalParameters.EmergencyBrakingMetersPerSecondSquared;
        var dynamicBraking = CalculateDynamicBrakingDistance(
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
        double followerSpeed,
        double followerAcceleration,
        double leaderSpeed)
    {
        var reactionDistance = followerSpeed * OperationalParameters.ControlReactionTimeSeconds;
        var buildDistance = followerSpeed * OperationalParameters.BrakeBuildUpTimeSeconds;
        var followerBraking = CalculateDynamicBrakingDistance(
            followerSpeed,
            followerAcceleration,
            Math.Min(
                TrainParameters.DecelerationMetersPerSecondSquared,
                OperationalParameters.ServiceBrakingMetersPerSecondSquared));
        var leaderBraking = leaderSpeed * leaderSpeed
            / (2 * OperationalParameters.EmergencyBrakingMetersPerSecondSquared);
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
            var fixedAllowance = 2 * OperationalParameters.PositioningErrorMeters
                + OperationalParameters.SafetyMarginMeters;
            var leaderStopDistance = leader.Speed * leader.Speed
                / (2 * OperationalParameters.EmergencyBrakingMetersPerSecondSquared);
            var controlReserve = Math.Max(
                1,
                follower.Speed * MovingBlockControlLookAheadSeconds);
            var available = observation.ActualGapMeters - fixedAllowance + leaderStopDistance - controlReserve;
            var reaction = OperationalParameters.ControlReactionTimeSeconds
                + OperationalParameters.BrakeBuildUpTimeSeconds;
            var braking = Math.Min(
                TrainParameters.DecelerationMetersPerSecondSquared,
                OperationalParameters.ServiceBrakingMetersPerSecondSquared);
            var allowed = CalculateMovingBlockPermittedSpeed(
                Math.Max(0, available),
                reaction,
                follower.Acceleration,
                braking);
            result[observation.FollowerVehicleId] = Math.Clamp(
                allowed,
                0,
                TrainParameters.MaxSpeedMetersPerSecond);
        }

        return result;
    }

    private double CalculateMovingBlockPermittedSpeed(
        double availableDistance,
        double reactionSeconds,
        double currentAcceleration,
        double braking)
    {
        var lower = 0d;
        var upper = TrainParameters.MaxSpeedMetersPerSecond;
        for (var iteration = 0; iteration < 48; iteration++)
        {
            var candidate = (lower + upper) * 0.5;
            var demand = candidate * reactionSeconds
                + CalculateDynamicBrakingDistance(candidate, currentAcceleration, braking);
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

    private double CalculateDynamicBrakingDistance(double speed, double acceleration, double braking) =>
        BrakingEnvelopeCalculator.CalculateStoppingEnvelope(
            speed,
            acceleration,
            braking,
            OperationalParameters.JerkMetersPerSecondCubed,
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

                var leaderRear = leader.Direction == TrainDirection.Outbound
                    ? leader.Position - OperationalParameters.TrainLengthMeters
                    : leader.Position + OperationalParameters.TrainLengthMeters;
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

        var obstacleBoundary = train.Direction == TrainDirection.Outbound
            ? obstacle.Position - OperationalParameters.TrainLengthMeters
            : obstacle.Position + OperationalParameters.TrainLengthMeters;
        return Math.Max(0, ForwardDistance(train.Direction, train.Position, obstacleBoundary));
    }

    private double CalculateStopCurveSpeed(double currentSpeed, double distanceMeters)
    {
        var jerkAllowance = currentSpeed * OperationalParameters.ServiceBrakingMetersPerSecondSquared
            / OperationalParameters.JerkMetersPerSecondCubed;
        var usable = Math.Max(0, distanceMeters - jerkAllowance - OperationalParameters.SafetyMarginMeters);
        return Math.Sqrt(2 * OperationalParameters.ServiceBrakingMetersPerSecondSquared * usable);
    }

    private double MoveAccelerationTowardZero(double acceleration)
    {
        if (ProfileMode != OperationProfileMode.RealisticOperations)
        {
            return 0;
        }

        var maximumChange = OperationalParameters.JerkMetersPerSecondCubed * FixedTimeStepSeconds;
        return Math.Abs(acceleration) <= maximumChange
            ? 0
            : acceleration - Math.Sign(acceleration) * maximumChange;
    }

    private void RecordTrajectory()
    {
        foreach (var train in _trains.Where(train => train.Active))
        {
            _trajectory.Add(new TrajectorySample(
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
                false));
        }
    }

    private WorldTrainState ToState(MutableTrain train)
    {
        var rear = train.Direction == TrainDirection.Outbound
            ? train.Position - OperationalParameters.TrainLengthMeters
            : train.Position + OperationalParameters.TrainLengthMeters;
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
            CurrentTimeSeconds);
    }

    private void AddEvent(
        SimulationEventType eventType,
        MutableTrain? train,
        string? relatedVehicleId,
        string message,
        double position,
        double speed)
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
            message);
        _events.Add(item);
        _newEvents.Add(item);
    }

    private static double ForwardDistance(TrainDirection direction, double fromPosition, double toPosition) =>
        direction == TrainDirection.Outbound ? toPosition - fromPosition : fromPosition - toPosition;

    private double Progress(TrainDirection direction, double position) =>
        direction == TrainDirection.Outbound ? position : Route.TotalLengthMeters - position;

    private sealed class MutableTrain
    {
        public string VehicleId { get; init; } = string.Empty;

        public int ServiceNumber { get; set; }

        public string ServiceRunId => $"{VehicleId.Replace("Vehicle ", "R", StringComparison.Ordinal)}-{(Direction == TrainDirection.Outbound ? "D" : "U")}{ServiceNumber:000}";

        public double StartTime { get; init; }

        public TrainDirection Direction { get; set; }

        public string TrackId { get; set; } = string.Empty;

        public string ServiceClassId { get; set; } = DefaultServiceClassId;

        public string PatternId { get; set; } = DefaultPatternId;

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
    }

    private readonly record struct ServiceRunPlanKey(
        string VehicleId,
        int ServiceNumber,
        TrainDirection Direction);

    private sealed class ScheduledObstacle(string vehicleId, double triggerTimeSeconds)
    {
        public string VehicleId { get; } = vehicleId;

        public double TriggerTimeSeconds { get; } = triggerTimeSeconds;

        public bool Triggered { get; set; }
    }
}
