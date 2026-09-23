using System.Globalization;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private void PopulateV3Timetable()
    {
        if (!_v2Enabled || _latestPlaybackFrame is null || _v2DispatchPlan is null)
        {
            return;
        }

        var entries = _resultAccumulator.TimetableEntries ?? (_activeTopologyProjectDocument is null
            ? OperationsTimetable.Build(
                _route ?? throw new InvalidOperationException("相容 V2 時刻表需要路線資料。"),
                _v2DispatchPlan,
                _plannedTimetableEvents,
                _latestPlaybackFrame.Events)
            : OperationsTimetable.Build(
                _latestPlaybackFrame.GetTopologyResultContext(),
                _v2DispatchPlan,
                _plannedTimetableEvents,
                _latestPlaybackFrame.Events));
        var rows = entries.Select(entry =>
        {
            var serviceName = ServiceTypeRows.FirstOrDefault(row =>
                row.Id.Equals(entry.ServiceTypeId, StringComparison.OrdinalIgnoreCase))?.Name ?? entry.ServiceTypeId;
            var patternName = entry.StopPatternId.Equals("ALL_STOP", StringComparison.OrdinalIgnoreCase)
                ? "所有車站停靠"
                : ServicePatternRows.FirstOrDefault(row =>
                    row.PatternId.Equals(entry.StopPatternId, StringComparison.OrdinalIgnoreCase))?.PatternName
                    ?? entry.StopPatternId;
            return new TimetableRow(
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
                entry.Status);
        }).ToArray();
        ApplyRowsByKey(TimetableRows, rows, row => $"{row.ServiceRunId}|{row.StationId}");

        if (TimetableSourceText is not null)
        {
            TimetableSourceText.Text = _activeTopologyProjectDocument is null
                ? "格式版本 7 派車計畫＋V2 模擬世界的計畫／實際事件；播放、重設與折返接續會同步更新"
                : "格式版本 8 拓撲發車計畫＋V2 拓撲游標的計畫／實際事件；里程僅為衍生顯示。";
        }

        string DisplayClock(double? seconds) => seconds is { } value
            ? FormatClock(_startClockSeconds + value)
            : "—";
    }

    private void PopulateV3SegmentDetails()
    {
        if (!_v2Enabled || _latestPlaybackFrame is null)
        {
            return;
        }

        var filter = new IntervalStatisticsFilter(IncludeInProgress: true);
        var result = _resultAccumulator.BuildIntervalStatistics(filter)
            ?? (_activeTopologyProjectDocument is null
                ? IntervalStatistics.Analyze(
                    _route ?? throw new InvalidOperationException("相容 V2 區間統計需要路線資料。"),
                    _latestPlaybackFrame.Trajectory,
                    _latestPlaybackFrame.Events,
                    _latestPlaybackFrame.SpeedLimits.Limits,
                    filter)
                : IntervalStatistics.Analyze(
                    _latestPlaybackFrame.GetTopologyResultContext(),
                    _latestPlaybackFrame.Trajectory,
                    _latestPlaybackFrame.Events,
                    filter));
        var rows = result.AllIntervals
                     .OrderBy(value => value.DepartureTimeSeconds ?? value.FirstObservedTimeSeconds)
                     .ThenBy(value => value.VehicleId, StringComparer.OrdinalIgnoreCase)
                     .Select(item =>
        {
            var accelerating = Phase(item, OperationalPhase.Accelerating);
            var cruising = Phase(item, OperationalPhase.Cruising);
            var coasting = Phase(item, OperationalPhase.Coasting);
            var braking = Phase(item, OperationalPhase.Braking) + Phase(item, OperationalPhase.ApproachBraking);
            return new SegmentRow(
                $"{item.FromStationId} → {item.ToStationId}",
                (item.DistanceMeters / 1000).ToString("0.###", CultureInfo.InvariantCulture),
                "V2 實際（加加速度／速限／控制）",
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
                    : string.Join("、", item.ControlEvents.EventTypes.Select(EventTypeToChinese)));
        }).ToArray();
        ApplyRowsByKey(SegmentRows, rows, row => $"{row.VehicleId}|{row.ServiceRunId}|{row.Segment}");

        if (SegmentSourceText is not null)
        {
            SegmentSourceText.Text = result.AllIntervals.Count == 0
                ? "V2 實際資料：請播放模擬；第一個 0.1 秒時間步進後開始產生區間軌跡"
                : $"V2 實際資料：完成 {result.CompletedCount}、運行中 {result.InProgressCount}；包含加加速度、惰行、速限與移動閉塞事件";
        }

        static double Phase(IntervalStatistic item, OperationalPhase phase) =>
            item.PhaseSeconds.TryGetValue(phase, out var seconds) ? seconds : 0;
    }

    private void PopulateV1V2Comparison()
    {
        if (!_v2Enabled || _latestPlaybackFrame is null || _v2DispatchPlan is null || _parameters is null)
        {
            return;
        }

        var cached = _resultAccumulator.ComparisonEntries;
        var result = cached is not null ? null : _activeTopologyProjectDocument is null
            ? V1V2Comparison.Analyze(
                _route ?? throw new InvalidOperationException("相容 V1/V2 比較需要路線資料。"),
                _v2DispatchPlan,
                BuildVehicleTypeDefinitions(),
                BuildStopPatternDefinitions(),
                _parameters,
                _latestPlaybackFrame.Events)
            : V1V2Comparison.Analyze(
                _latestPlaybackFrame.GetTopologyResultContext(),
                _v2DispatchPlan,
                _activeTopologyProjectDocument.VehicleTypes.Select(item => new VehicleTypeDefinition(
                    item.Id,
                    item.DisplayName,
                    item.LengthMeters,
                    item.MaxSpeedMetersPerSecond,
                    item.AccelerationMetersPerSecondSquared,
                    item.ServiceBrakeDecelerationMetersPerSecondSquared,
                    item.EmergencyBrakeDecelerationMetersPerSecondSquared,
                    item.JerkMetersPerSecondCubed,
                    item.TractionDecayPerSecond,
                    item.CoastingDecelerationMetersPerSecondSquared,
                    item.DefaultStopPatternId)),
                _activeTopologyProjectDocument.StopPatterns.Select(pattern => new StopPatternDefinition(
                    pattern.Id,
                    pattern.DisplayName,
                    pattern.Instructions.Select(instruction => new StopPatternInstruction(
                        instruction.StationId,
                        instruction.Action,
                        instruction.DwellTimeSeconds,
                        instruction.PassingSpeedLimitMetersPerSecond)))),
                _parameters,
                _latestPlaybackFrame.Events);
        var rows = (cached ?? result!.Stations).Select(item => new V1V2ComparisonRow(
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
                item.Status)).ToArray();
        ApplyRowsByKey(V1V2ComparisonRows, rows, row => $"{row.VehicleId}|{row.ServiceRunId}|{row.Station}");

        string Clock(double? seconds) => seconds is { } value ? FormatClock(_startClockSeconds + value) : "—";
        static string Seconds(double? seconds, bool signed = false) => seconds is not { } value
            ? "—"
            : signed ? $"{value:+0.0;-0.0;0.0} s" : $"{value:0.0} s";
    }

    private void PopulateResourceOccupancy()
    {
        if (!_v2Enabled || _latestPlaybackFrame is null)
        {
            return;
        }

        var result = _resultAccumulator.BuildResourceOccupancy(_latestPlaybackFrame.CurrentTimeSeconds);
        var rows = result.Resources.Select(item => new ResourceOccupancyRow(
                item.ResourceId,
                $"{item.OccupiedSeconds:0.0} s",
                $"{item.UtilizationPercent:0.0}%",
                item.ReservationCount.ToString(),
                $"{item.ObservedReservationsPerHour:0.0}",
                item.MinimumReleaseHeadwaySeconds is { } headway ? $"{headway:0.0} s" : "—")).ToArray();
        ApplyRowsByKey(ResourceOccupancyRows, rows, row => row.ResourceId);
        if (ResourceOccupancySummaryText is not null)
        {
            ResourceOccupancySummaryText.Text = result.Resources.Count == 0
                ? "播放模擬後，將依進路鎖定／進路釋放事件顯示各資源的獨立占用時間軸。"
                : $"觀測窗 {result.WindowEndSeconds - result.WindowStartSeconds:0.0} s；共 {result.Intervals.Count} 段資源占用。容量欄位是此排程的觀測值，不是安全認證容量。";
        }
    }
}
