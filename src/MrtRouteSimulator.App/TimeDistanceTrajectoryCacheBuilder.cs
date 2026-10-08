using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// Builds the bounded interactive time-distance cache away from the WPF
/// dispatcher.  The returned cache is complete before it is published to the
/// UI; no caller should append to it after publication.
/// </summary>
public static class TimeDistanceTrajectoryCacheBuilder
{
    public static Task<TimeDistanceTrajectoryCache> BuildAsync(
        IReadOnlyList<TrajectorySample> source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.Run(() => Build(source, cancellationToken), cancellationToken);
    }

    private static TimeDistanceTrajectoryCache Build(
        IReadOnlyList<TrajectorySample> source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cache = new TimeDistanceTrajectoryCache();
        cache.Append(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return cache;
    }
}
