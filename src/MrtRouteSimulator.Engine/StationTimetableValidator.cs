namespace MrtRouteSimulator.Engine;

/// <summary>Validates per-run station departure clocks against the selected physical service route.</summary>
internal static class StationTimetableValidator
{
    private const double NumericalTolerance = 1e-7;

    internal static void ValidateAndThrow(
        ResolvedDispatchPlan dispatchPlan,
        Func<PlannedServiceRun, IReadOnlyList<string>> stationIdsResolver,
        IEnumerable<ServicePattern> servicePatterns)
    {
        ArgumentNullException.ThrowIfNull(dispatchPlan);
        ArgumentNullException.ThrowIfNull(stationIdsResolver);
        var patterns = (servicePatterns ?? [])
            .ToDictionary(pattern => pattern.PatternId, StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var run in dispatchPlan.Runs.Where(run => run.StationTimetableEntries.Count > 0))
        {
            var routeStationIds = stationIdsResolver(run);
            if (routeStationIds.Count < 2)
            {
                errors.Add($"車次「{run.ServiceRunId}」的營運路線至少需要起點與終點兩站。");
                continue;
            }

            patterns.TryGetValue(run.StopPatternId, out var pattern);
            var indexed = new List<(int Index, StationTimetableEntry Entry, double RelativeSeconds)>();
            foreach (var entry in run.StationTimetableEntries)
            {
                var stationIndex = IndexOf(routeStationIds, entry.StationId);
                if (stationIndex < 0)
                {
                    errors.Add($"車次「{run.ServiceRunId}」的逐站班表引用不在營運路線上的車站「{entry.StationId}」。");
                    continue;
                }

                if (stationIndex == routeStationIds.Count - 1)
                {
                    errors.Add($"車次「{run.ServiceRunId}」的終點「{entry.StationId}」不可設定逐站發車時刻；折返接續請設定反向車次的起站時刻。");
                    continue;
                }

                var instruction = pattern?.Instructions.FirstOrDefault(item =>
                    item.StationId.Equals(entry.StationId, StringComparison.OrdinalIgnoreCase));
                if (instruction?.Mode is StationServiceMode.Pass or StationServiceMode.Turnback)
                {
                    errors.Add($"車次「{run.ServiceRunId}」只可對停靠站「{entry.StationId}」設定逐站發車時刻。");
                    continue;
                }

                var relativeSeconds = RelativeSeconds(entry.PlannedDepartureTime, run.PlannedDepartureTime);
                if (stationIndex == 0)
                {
                    if (entry.MinimumDwellSeconds > NumericalTolerance)
                    {
                        errors.Add($"車次「{run.ServiceRunId}」的起站「{entry.StationId}」沒有實際到站事件，表定停站時間必須為 0 秒。");
                    }
                    if (relativeSeconds > NumericalTolerance)
                    {
                        errors.Add($"車次「{run.ServiceRunId}」的起站「{entry.StationId}」發車時刻必須與車次表定發車時刻一致。");
                    }
                }

                indexed.Add((stationIndex, entry, relativeSeconds));
            }

            var ordered = indexed.OrderBy(item => item.Index).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].RelativeSeconds + NumericalTolerance < ordered[index - 1].RelativeSeconds)
                {
                    errors.Add($"車次「{run.ServiceRunId}」的逐站表定發車時刻必須依營運路線站序遞增。");
                    break;
                }
            }
        }

        RouteValidator.ThrowIfAny(errors);
    }

    private static int IndexOf(IReadOnlyList<string> stationIds, string stationId)
    {
        for (var index = 0; index < stationIds.Count; index++)
        {
            if (stationIds[index].Equals(stationId, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    private static double RelativeSeconds(TimeSpan value, TimeSpan anchor)
    {
        var seconds = (value - anchor).TotalSeconds;
        return seconds < 0 ? seconds + TimeSpan.FromDays(1).TotalSeconds : seconds;
    }
}
