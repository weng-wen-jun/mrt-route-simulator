using System.Collections.Immutable;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

/// <summary>
/// 把 Engine footprint 的占用區間整理成「edgeId → 車號」，供配線圖區段亮燈使用。
/// 只做彙整，不推算任何位置；只在節點邊界接觸的零長度區間不算占用。
/// </summary>
internal static class TrackOccupancySnapshot
{
    public static readonly ImmutableDictionary<string, ImmutableArray<string>> Empty =
        ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.OrdinalIgnoreCase);

    public static ImmutableDictionary<string, ImmutableArray<string>> Build(
        IEnumerable<KeyValuePair<string, IReadOnlyList<TrackOccupancyInterval>>> intervalsByVehicle)
    {
        var occupants = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vehicleId, intervals) in intervalsByVehicle)
        {
            foreach (var interval in intervals)
            {
                if (interval.EndOffsetMeters - interval.StartOffsetMeters <= TrackPosition.DefaultToleranceMeters) continue;
                if (!occupants.TryGetValue(interval.TrackEdgeId, out var vehicles))
                {
                    vehicles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    occupants[interval.TrackEdgeId] = vehicles;
                }
                vehicles.Add(vehicleId);
            }
        }

        return occupants.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutableArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}
