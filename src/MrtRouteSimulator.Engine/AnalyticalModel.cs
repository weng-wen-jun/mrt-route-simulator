namespace MrtRouteSimulator.Engine;

public static class AnalyticalModel
{
    private const double BoundaryTolerance = 1e-9;

    public static SegmentTravelResult CalculateSegmentTravelTime(
        double segmentDistanceMeters,
        double maxSpeedMetersPerSecond,
        double accelerationMetersPerSecondSquared,
        double decelerationMetersPerSecondSquared)
    {
        var errors = new List<string>();

        if (!RouteValidator.IsFinite(segmentDistanceMeters) || segmentDistanceMeters < 0)
        {
            errors.Add("站間距離必須是有限的非負數。");
        }

        var allUnlimited = double.IsPositiveInfinity(maxSpeedMetersPerSecond)
            && double.IsPositiveInfinity(accelerationMetersPerSecondSquared)
            && double.IsPositiveInfinity(decelerationMetersPerSecondSquared);

        if (!allUnlimited)
        {
            RouteValidator.RequirePositiveFinite(maxSpeedMetersPerSecond, "最高速度", errors);
            RouteValidator.RequirePositiveFinite(accelerationMetersPerSecondSquared, "加速度", errors);
            RouteValidator.RequirePositiveFinite(decelerationMetersPerSecondSquared, "減速度", errors);
        }

        RouteValidator.ThrowIfAny(errors);

        if (segmentDistanceMeters == 0 || allUnlimited)
        {
            return new SegmentTravelResult(
                segmentDistanceMeters,
                0,
                0,
                SpeedProfileType.Instantaneous,
                0,
                0,
                0,
                0,
                segmentDistanceMeters,
                0);
        }

        var accelerationDistance = maxSpeedMetersPerSecond * maxSpeedMetersPerSecond
            / (2 * accelerationMetersPerSecondSquared);
        var decelerationDistance = maxSpeedMetersPerSecond * maxSpeedMetersPerSecond
            / (2 * decelerationMetersPerSecondSquared);

        if (accelerationDistance + decelerationDistance <= segmentDistanceMeters + BoundaryTolerance)
        {
            var cruisingDistance = Math.Max(0, segmentDistanceMeters - accelerationDistance - decelerationDistance);
            var accelerationTime = maxSpeedMetersPerSecond / accelerationMetersPerSecondSquared;
            var cruisingTime = cruisingDistance / maxSpeedMetersPerSecond;
            var decelerationTime = maxSpeedMetersPerSecond / decelerationMetersPerSecondSquared;

            return new SegmentTravelResult(
                segmentDistanceMeters,
                accelerationTime + cruisingTime + decelerationTime,
                maxSpeedMetersPerSecond,
                SpeedProfileType.Trapezoidal,
                accelerationTime,
                cruisingTime,
                decelerationTime,
                accelerationDistance,
                cruisingDistance,
                decelerationDistance);
        }

        var peakSpeed = Math.Sqrt(
            2 * segmentDistanceMeters
            * accelerationMetersPerSecondSquared
            * decelerationMetersPerSecondSquared
            / (accelerationMetersPerSecondSquared + decelerationMetersPerSecondSquared));
        var triangularAccelerationTime = peakSpeed / accelerationMetersPerSecondSquared;
        var triangularDecelerationTime = peakSpeed / decelerationMetersPerSecondSquared;
        var triangularAccelerationDistance = peakSpeed * peakSpeed / (2 * accelerationMetersPerSecondSquared);
        var triangularDecelerationDistance = segmentDistanceMeters - triangularAccelerationDistance;

        return new SegmentTravelResult(
            segmentDistanceMeters,
            triangularAccelerationTime + triangularDecelerationTime,
            peakSpeed,
            SpeedProfileType.Triangular,
            triangularAccelerationTime,
            0,
            triangularDecelerationTime,
            triangularAccelerationDistance,
            0,
            triangularDecelerationDistance);
    }
}
