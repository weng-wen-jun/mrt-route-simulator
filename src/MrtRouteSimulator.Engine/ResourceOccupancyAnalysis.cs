using System.Globalization;
using System.Text;

namespace MrtRouteSimulator.Engine;

/// <summary>單一資源從鎖定至釋放的不可重疊占用區間。</summary>
public sealed record ResourceOccupancyInterval(
    string ResourceId,
    string VehicleId,
    string ServiceRunId,
    double StartTimeSeconds,
    double EndTimeSeconds,
    bool IsClosed);

/// <summary>模擬觀測窗內的資源使用與容量指標；不是保證的理論路網容量。</summary>
public sealed record ResourceUtilization(
    string ResourceId,
    double OccupiedSeconds,
    double UtilizationPercent,
    int ReservationCount,
    double ObservedReservationsPerHour,
    double? MinimumReleaseHeadwaySeconds);

public sealed record ResourceOccupancyAnalysisResult(
    double WindowStartSeconds,
    double WindowEndSeconds,
    IReadOnlyList<ResourceOccupancyInterval> Intervals,
    IReadOnlyList<ResourceUtilization> Resources);

/// <summary>
/// 將 SimulationWorld 的結構化 RouteReserved／RouteReleased 事件轉為各衝突區、月台、
/// 進路與尾軌獨立的占用時間軸。觀測容量只反映實際排程下的資源使用，避免誤稱為安全認證容量。
/// </summary>
public static class ResourceOccupancyAnalysis
{
    public static ResourceOccupancyAnalysisResult Analyze(
        IEnumerable<SimulationEvent> source,
        double? windowEndSeconds = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var events = source
            .Where(item => item.EventType is SimulationEventType.RouteReserved or SimulationEventType.RouteReleased)
            .OrderBy(item => item.SimulationTimeSeconds)
            .ThenBy(item => item.EventType == SimulationEventType.RouteReleased ? 0 : 1)
            .ToArray();
        var start = events.FirstOrDefault()?.SimulationTimeSeconds ?? 0;
        var end = windowEndSeconds ?? events.LastOrDefault()?.SimulationTimeSeconds ?? start;
        if (!double.IsFinite(end) || end < start)
        {
            throw new SimulationValidationException(["資源占用分析的觀測結束時間無效。"]);
        }

        var open = new Dictionary<string, OpenOccupancy>(StringComparer.OrdinalIgnoreCase);
        var intervals = new List<ResourceOccupancyInterval>();
        foreach (var item in events)
        {
            var resources = GetResources(item);
            foreach (var resourceId in resources)
            {
                if (item.EventType == SimulationEventType.RouteReserved)
                {
                    if (open.TryGetValue(resourceId, out var current))
                    {
                        if (current.Matches(item))
                        {
                            continue;
                        }
                        intervals.Add(current.Close(item.SimulationTimeSeconds, isClosed: false));
                    }
                    open[resourceId] = new OpenOccupancy(resourceId, item.VehicleId, item.ServiceRunId, item.SimulationTimeSeconds);
                }
                else if (open.Remove(resourceId, out var current))
                {
                    intervals.Add(current.Close(item.SimulationTimeSeconds, isClosed: true));
                }
            }
        }

        foreach (var current in open.Values)
        {
            intervals.Add(current.Close(end, isClosed: false));
        }

        var windowSeconds = Math.Max(0, end - start);
        var summaries = intervals
            .GroupBy(item => item.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.StartTimeSeconds).ToArray();
                var occupied = ordered.Sum(item => Math.Max(0, item.EndTimeSeconds - item.StartTimeSeconds));
                var releaseTimes = ordered.Where(item => item.IsClosed).Select(item => item.EndTimeSeconds).OrderBy(value => value).ToArray();
                var headways = releaseTimes.Zip(releaseTimes.Skip(1), (first, second) => second - first)
                    .Where(value => value >= 0).ToArray();
                return new ResourceUtilization(
                    group.Key,
                    occupied,
                    windowSeconds <= 0 ? 0 : occupied / windowSeconds * 100,
                    ordered.Length,
                    windowSeconds <= 0 ? 0 : ordered.Length * 3600 / windowSeconds,
                    headways.Length == 0 ? null : headways.Min());
            })
            .OrderByDescending(item => item.UtilizationPercent)
            .ThenBy(item => item.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ResourceOccupancyAnalysisResult(start, end, intervals, summaries);
    }

    public static string BuildCsv(ResourceOccupancyAnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder();
        builder.AppendLine("資源 ID,占用秒數,使用率 %,預約次數,觀測每小時次數,最短釋放間距秒");
        foreach (var item in result.Resources)
        {
            builder.Append(Escape(item.ResourceId)).Append(',')
                .Append(item.OccupiedSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(item.UtilizationPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(item.ReservationCount).Append(',')
                .Append(item.ObservedReservationsPerHour.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                .Append(item.MinimumReleaseHeadwaySeconds?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty)
                .AppendLine();
        }
        return builder.ToString();
    }

    private static IReadOnlyList<string> GetResources(SimulationEvent item)
    {
        var values = item.ResourceIds is { Count: > 0 }
            ? item.ResourceIds
            : string.IsNullOrWhiteSpace(item.ResourceId) ? [] : [item.ResourceId];
        return values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? '"' + value.Replace("\"", "\"\"") + '"'
        : value;

    private sealed record OpenOccupancy(string ResourceId, string VehicleId, string ServiceRunId, double StartTimeSeconds)
    {
        public bool Matches(SimulationEvent item) => VehicleId.Equals(item.VehicleId, StringComparison.OrdinalIgnoreCase)
            && ServiceRunId.Equals(item.ServiceRunId, StringComparison.OrdinalIgnoreCase);

        public ResourceOccupancyInterval Close(double endTimeSeconds, bool isClosed) => new(
            ResourceId,
            VehicleId,
            ServiceRunId,
            StartTimeSeconds,
            Math.Max(StartTimeSeconds, endTimeSeconds),
            isClosed);
    }
}
