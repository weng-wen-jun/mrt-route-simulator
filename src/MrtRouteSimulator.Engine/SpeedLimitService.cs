namespace MrtRouteSimulator.Engine;

public sealed class SpeedLimitService
{
    private const double PositionEpsilon = 1e-6;
    private readonly IReadOnlyList<SpeedLimitSegment> _limits;

    public SpeedLimitService(Route route, IEnumerable<SpeedLimitSegment>? limits = null)
    {
        Route = route ?? throw new ArgumentNullException(nameof(route));
        _limits = limits?.ToArray() ?? [];
        V2Validator.ValidateSpeedLimits(route, _limits);
    }

    public Route Route { get; }

    public IReadOnlyList<SpeedLimitSegment> Limits => _limits;

    public double GetCurrentLimitMetersPerSecond(
        double positionMeters,
        TrainDirection direction,
        double trainMaximumMetersPerSecond)
    {
        var applicable = _limits
            .Where(limit => AppliesTo(limit, direction)
                && positionMeters >= limit.StartPositionMeters - PositionEpsilon
                && positionMeters <= limit.EndPositionMeters + PositionEpsilon)
            .Select(limit => limit.LimitMetersPerSecond)
            .Append(trainMaximumMetersPerSecond);
        return applicable.Min();
    }

    public double GetPermittedSpeedMetersPerSecond(
        double positionMeters,
        TrainDirection direction,
        double trainMaximumMetersPerSecond,
        double brakingMetersPerSecondSquared,
        double jerkMetersPerSecondCubed,
        double currentSpeedMetersPerSecond,
        double? mandatoryTargetPositionMeters = null,
        double mandatoryTargetSpeedMetersPerSecond = 0)
    {
        var permitted = GetCurrentLimitMetersPerSecond(positionMeters, direction, trainMaximumMetersPerSecond);
        var targets = GetUpcomingRestrictionTargets(positionMeters, direction, trainMaximumMetersPerSecond).ToList();
        if (mandatoryTargetPositionMeters is not null)
        {
            targets.Add((mandatoryTargetPositionMeters.Value, Math.Max(0, mandatoryTargetSpeedMetersPerSecond)));
        }

        foreach (var (targetPosition, targetSpeed) in targets)
        {
            var distance = direction == TrainDirection.Outbound
                ? targetPosition - positionMeters
                : positionMeters - targetPosition;
            if (distance < -PositionEpsilon)
            {
                continue;
            }

            var jerkAllowance = jerkMetersPerSecondCubed > 0
                ? currentSpeedMetersPerSecond * brakingMetersPerSecondSquared / jerkMetersPerSecondCubed
                    + brakingMetersPerSecondSquared * brakingMetersPerSecondSquared
                    / (2 * jerkMetersPerSecondCubed * jerkMetersPerSecondCubed)
                : 0;
            var usableDistance = Math.Max(0, distance - jerkAllowance);
            var brakingCurveSpeed = Math.Sqrt(
                Math.Max(0, targetSpeed * targetSpeed + 2 * brakingMetersPerSecondSquared * usableDistance));
            permitted = Math.Min(permitted, brakingCurveSpeed);
        }

        return Math.Clamp(permitted, 0, trainMaximumMetersPerSecond);
    }

    public IReadOnlyList<string> GetOverlapWarnings()
    {
        var warnings = new List<string>();
        for (var firstIndex = 0; firstIndex < _limits.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < _limits.Count; secondIndex++)
            {
                var first = _limits[firstIndex];
                var second = _limits[secondIndex];
                if (!DirectionsCanOverlap(first.Direction, second.Direction))
                {
                    continue;
                }

                var start = Math.Max(first.StartPositionMeters, second.StartPositionMeters);
                var end = Math.Min(first.EndPositionMeters, second.EndPositionMeters);
                if (end > start + PositionEpsilon)
                {
                    warnings.Add(
                        $"{start / 1000:0.00}～{end / 1000:0.00} km 重疊，實際採用較低的 "
                        + $"{Math.Min(first.LimitMetersPerSecond, second.LimitMetersPerSecond) * 3.6:0.#} km/h。");
                }
            }
        }

        return warnings;
    }

    private IEnumerable<(double Position, double Speed)> GetUpcomingRestrictionTargets(
        double positionMeters,
        TrainDirection direction,
        double trainMaximumMetersPerSecond)
    {
        foreach (var limit in _limits.Where(limit => AppliesTo(limit, direction)))
        {
            var boundary = direction == TrainDirection.Outbound
                ? limit.StartPositionMeters
                : limit.EndPositionMeters;
            var distanceAhead = direction == TrainDirection.Outbound
                ? boundary - positionMeters
                : positionMeters - boundary;
            if (distanceAhead < -PositionEpsilon)
            {
                continue;
            }

            var insidePosition = direction == TrainDirection.Outbound
                ? Math.Min(Route.TotalLengthMeters, boundary + PositionEpsilon * 10)
                : Math.Max(0, boundary - PositionEpsilon * 10);
            var targetSpeed = GetCurrentLimitMetersPerSecond(
                insidePosition,
                direction,
                trainMaximumMetersPerSecond);
            yield return (boundary, targetSpeed);
        }
    }

    private static bool AppliesTo(SpeedLimitSegment limit, TrainDirection direction) =>
        limit.Direction == SpeedLimitDirection.Both
        || limit.Direction == SpeedLimitDirection.Outbound && direction == TrainDirection.Outbound
        || limit.Direction == SpeedLimitDirection.Inbound && direction == TrainDirection.Inbound;

    private static bool DirectionsCanOverlap(SpeedLimitDirection first, SpeedLimitDirection second) =>
        first == SpeedLimitDirection.Both
        || second == SpeedLimitDirection.Both
        || first == second;
}
