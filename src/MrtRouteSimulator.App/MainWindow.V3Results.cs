using System.Globalization;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private void PopulateV3Timetable()
    {
        if (!_v2Enabled || _route is null || _v2World is null || _v2DispatchPlan is null)
        {
            return;
        }

        var entries = OperationsTimetable.Build(
            _route,
            _v2DispatchPlan,
            _plannedTimetableEvents,
            _v2World.Events);
        TimetableRows.Clear();
        foreach (var entry in entries)
        {
            var serviceName = ServiceTypeRows.FirstOrDefault(row =>
                row.Id.Equals(entry.ServiceTypeId, StringComparison.OrdinalIgnoreCase))?.Name ?? entry.ServiceTypeId;
            var patternName = entry.StopPatternId.Equals("ALL_STOP", StringComparison.OrdinalIgnoreCase)
                ? "所有車站停靠"
                : ServicePatternRows.FirstOrDefault(row =>
                    row.PatternId.Equals(entry.StopPatternId, StringComparison.OrdinalIgnoreCase))?.PatternName
                    ?? entry.StopPatternId;
            TimetableRows.Add(new TimetableRow(
                entry.VehicleId,
                DirectionToChinese(entry.Direction),
                entry.StationId,
                entry.StationName,
                DisplayClock(entry.ActualArrivalTimeSeconds),
                DisplayClock(entry.ActualDepartureTimeSeconds),
                entry.ActualDwellSeconds is { } dwell ? $"{dwell:0.0} s" : "—",
                (entry.PositionMeters / 1000).ToString("0.###", CultureInfo.InvariantCulture),
                entry.ServiceRunId,
                serviceName,
                patternName,
                DisplayClock(entry.PlannedArrivalTimeSeconds),
                DisplayClock(entry.PlannedDepartureTimeSeconds),
                entry.DelaySeconds is { } delay ? $"{delay:0.0} s" : "—",
                entry.Status));
        }

        if (TimetableSourceText is not null)
        {
            TimetableSourceText.Text = "Schema 7 派車計畫＋V2 SimulationWorld 計畫／實際事件；播放、重設與折返接續會同步更新";
        }

        string DisplayClock(double? seconds) => seconds is { } value
            ? FormatClock(_startClockSeconds + value)
            : "—";
    }

    private void PopulateV3SegmentDetails()
    {
        if (!_v2Enabled || _route is null || _v2World is null)
        {
            return;
        }

        var result = IntervalStatistics.Analyze(
            _route,
            _v2World.Trajectory,
            _v2World.Events,
            _v2World.SpeedLimits.Limits,
            new IntervalStatisticsFilter(IncludeInProgress: true));
        SegmentRows.Clear();
        foreach (var item in result.AllIntervals
                     .OrderBy(value => value.DepartureTimeSeconds ?? value.FirstObservedTimeSeconds)
                     .ThenBy(value => value.VehicleId, StringComparer.OrdinalIgnoreCase))
        {
            var accelerating = Phase(item, OperationalPhase.Accelerating);
            var cruising = Phase(item, OperationalPhase.Cruising);
            var coasting = Phase(item, OperationalPhase.Coasting);
            var braking = Phase(item, OperationalPhase.Braking) + Phase(item, OperationalPhase.ApproachBraking);
            SegmentRows.Add(new SegmentRow(
                $"{item.FromStationId} → {item.ToStationId}",
                (item.DistanceMeters / 1000).ToString("0.###", CultureInfo.InvariantCulture),
                "V2 實際（Jerk／速限／控制）",
                item.PeakSpeedMetersPerSecond is { } peak ? (peak * 3.6).ToString("0.##", CultureInfo.InvariantCulture) : "—",
                item.TravelTimeSeconds is { } travel ? $"{travel:0.0} s" : "—",
                $"{accelerating:0.0} s",
                $"{cruising:0.0} s",
                $"{braking:0.0} s",
                item.VehicleId,
                item.ServiceRunId,
                DirectionToChinese(item.Direction),
                item.Status,
                $"{coasting:0.0} s",
                item.ControlEvents.EventTypes.Count == 0
                    ? "無"
                    : string.Join("、", item.ControlEvents.EventTypes.Select(EventTypeToChinese))));
        }

        if (SegmentSourceText is not null)
        {
            SegmentSourceText.Text = result.AllIntervals.Count == 0
                ? "V2 實際資料：請播放模擬；第一個 0.1 秒 Tick 後開始產生區間軌跡"
                : $"V2 實際資料：完成 {result.CompletedCount}、運行中 {result.InProgressCount}；包含 Jerk、惰行、速限與移動閉塞事件";
        }

        static double Phase(IntervalStatistic item, OperationalPhase phase) =>
            item.PhaseSeconds.TryGetValue(phase, out var seconds) ? seconds : 0;
    }

    private void PopulateV1V2Comparison()
    {
        if (!_v2Enabled || _route is null || _v2World is null || _v2DispatchPlan is null || _parameters is null)
        {
            return;
        }

        var result = V1V2Comparison.Analyze(
            _route,
            _v2DispatchPlan,
            BuildVehicleTypeDefinitions(),
            BuildStopPatternDefinitions(),
            _parameters,
            _v2World.Events);
        V1V2ComparisonRows.Clear();
        foreach (var item in result.Stations)
        {
            V1V2ComparisonRows.Add(new V1V2ComparisonRow(
                item.VehicleId,
                item.ServiceRunId,
                DirectionToChinese(item.Direction),
                item.VehicleTypeId,
                item.StopPatternId,
                $"{item.StationId} {item.StationName}",
                Clock(item.TheoreticalArrivalTimeSeconds),
                Clock(item.TheoreticalDepartureTimeSeconds),
                Seconds(item.TheoreticalDwellSeconds),
                Clock(item.ActualArrivalTimeSeconds),
                Clock(item.ActualDepartureTimeSeconds),
                Seconds(item.ActualDwellSeconds),
                Seconds(item.ArrivalDifferenceSeconds, signed: true),
                Seconds(item.DepartureDifferenceSeconds, signed: true),
                item.DepartureDifferencePercent is { } percent ? $"{percent:+0.0;-0.0;0.0}%" : "—",
                item.Status));
        }

        string Clock(double? seconds) => seconds is { } value ? FormatClock(_startClockSeconds + value) : "—";
        static string Seconds(double? seconds, bool signed = false) => seconds is not { } value
            ? "—"
            : signed ? $"{value:+0.0;-0.0;0.0} s" : $"{value:0.0} s";
    }

    private void PopulateResourceOccupancy()
    {
        if (!_v2Enabled || _v2World is null)
        {
            return;
        }

        var result = ResourceOccupancyAnalysis.Analyze(_v2World.Events, _v2World.CurrentTimeSeconds);
        ResourceOccupancyRows.Clear();
        foreach (var item in result.Resources)
        {
            ResourceOccupancyRows.Add(new ResourceOccupancyRow(
                item.ResourceId,
                $"{item.OccupiedSeconds:0.0} s",
                $"{item.UtilizationPercent:0.0}%",
                item.ReservationCount.ToString(),
                $"{item.ObservedReservationsPerHour:0.0}",
                item.MinimumReleaseHeadwaySeconds is { } headway ? $"{headway:0.0} s" : "—"));
        }
        if (ResourceOccupancySummaryText is not null)
        {
            ResourceOccupancySummaryText.Text = result.Resources.Count == 0
                ? "播放模擬後，將依 RouteReserved／RouteReleased 顯示各資源的獨立占用時間軸。"
                : $"觀測窗 {result.WindowEndSeconds - result.WindowStartSeconds:0.0} s；共 {result.Intervals.Count} 段資源占用。容量欄位是此排程的觀測值，不是安全認證容量。";
        }
    }
}
