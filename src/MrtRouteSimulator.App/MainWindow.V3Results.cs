using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private bool _updatingTimetableFilters;

    private void PopulateV3Timetable()
    {
        if (!_v2Enabled || _latestPlaybackFrame is null || _v2DispatchPlan is null)
        {
            return;
        }

        var entries = GetTimetableEntries();
        UpdateTimetableFilters(entries);
        var rows = FilterTimetableEntries(entries).Select(entry =>
        {
            var serviceName = ServiceTypeRows.FirstOrDefault(row =>
                row.Id.Equals(entry.ServiceTypeId, StringComparison.OrdinalIgnoreCase))?.Name ?? entry.ServiceTypeId;
            var patternName = entry.StopPatternId.Equals("ALL_STOP", StringComparison.OrdinalIgnoreCase)
                ? "所有車站停靠"
                : ServicePatternRows.FirstOrDefault(row =>
                    row.PatternId.Equals(entry.StopPatternId, StringComparison.OrdinalIgnoreCase))?.PatternName
                    ?? entry.StopPatternId;
            var routeName = entry.ServiceRouteId is { } routeId
                ? GetServiceRouteDisplayName(routeId)
                : "—";
            var operatingLineName = entry.OperatingLineId is { } lineId
                ? GetOperatingLineDisplayName(lineId)
                : "—";
            return new TimetableRow(
                entry.VehicleId,
                DirectionToChinese(entry.Direction),
                entry.StationId,
                entry.StationName,
                DisplayClock(entry.ActualArrivalTimeSeconds),
                DisplayClock(entry.ActualDepartureTimeSeconds),
                FormatTimetableDwell(entry.ActualDwellSeconds, entry.Status, actual: true),
                (entry.PositionMeters / 1000).ToString("0.###", CultureInfo.InvariantCulture),
                entry.ServiceRunId,
                serviceName,
                patternName,
                DisplayClock(entry.PlannedArrivalTimeSeconds),
                DisplayClock(entry.PlannedDepartureTimeSeconds),
                entry.DelaySeconds is { } delay ? $"{delay:0.0} s" : "—",
                entry.Status,
                FormatTimetableDwell(entry.PlannedDwellSeconds, entry.Status, actual: false),
                routeName,
                operatingLineName);
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

    private string FormatTimetableDwell(double? seconds, string status, bool actual)
    {
        if (seconds is { } value) return $"{value:0.0} s";
        if (status.Contains("通過", StringComparison.Ordinal)) return "不適用";
        return actual && status.Equals("停站中", StringComparison.Ordinal) ? "停站中" : "—";
    }

    private IReadOnlyList<OperationsTimetableEntry> GetTimetableEntries()
    {
        if (_latestPlaybackFrame is null || _v2DispatchPlan is null)
        {
            return [];
        }

        return _resultAccumulator.TimetableEntries ?? (_activeTopologyProjectDocument is null
            ? OperationsTimetable.Build(
                _route ?? throw new InvalidOperationException("相容 V2 時刻表需要路線資料。"),
                _v2DispatchPlan,
                _plannedTimetableEvents,
                _latestPlaybackFrame.Events,
                GetResultServicePatterns())
            : OperationsTimetable.Build(
                _latestPlaybackFrame.GetTopologyResultContext(),
                _v2DispatchPlan,
                _plannedTimetableEvents,
                _latestPlaybackFrame.Events,
                GetResultServicePatterns()));
    }

    private IEnumerable<OperationsTimetableEntry> FilterTimetableEntries(
        IEnumerable<OperationsTimetableEntry> entries)
    {
        var routeId = GetSelectedFilterId(TimetableRouteFilterComboBox);
        var lineId = GetSelectedFilterId(TimetableOperatingLineFilterComboBox);
        return entries.Where(entry =>
            (routeId is null || string.Equals(entry.ServiceRouteId, routeId, StringComparison.OrdinalIgnoreCase))
            && (lineId is null || string.Equals(entry.OperatingLineId, lineId, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateTimetableFilters(IReadOnlyList<OperationsTimetableEntry> entries)
    {
        if (TimetableRouteFilterComboBox is null || TimetableOperatingLineFilterComboBox is null) return;
        _updatingTimetableFilters = true;
        try
        {
            SetFilterOptions(TimetableRouteFilterComboBox, entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.ServiceRouteId))
                .Select(entry => new CatalogOption(
                    entry.ServiceRouteId!,
                    GetServiceRouteDisplayName(entry.ServiceRouteId!))));
            SetFilterOptions(TimetableOperatingLineFilterComboBox, entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.OperatingLineId))
                .Select(entry => new CatalogOption(
                    entry.OperatingLineId!,
                    GetOperatingLineDisplayName(entry.OperatingLineId!))));
        }
        finally
        {
            _updatingTimetableFilters = false;
        }
    }

    private void TimetableFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_updatingTimetableFilters) PopulateV3Timetable();
    }

    private void ExportTimetableCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_v2Enabled || _latestPlaybackFrame is null || _v2DispatchPlan is null)
            {
                throw new InvalidOperationException("請先建立並播放 V2 寫實模擬。");
            }

            var entries = FilterTimetableEntries(GetTimetableEntries()).ToArray();
            var dialog = new SaveFileDialog
            {
                Title = "匯出進出站時刻表 CSV",
                Filter = "CSV 資料 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "V2進出站時刻表.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName,
                OperationsTimetable.BuildCsv(entries, _startClockSeconds),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusTextBlock.Text = $"已匯出：{dialog.FileName}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowValidation([$"無法匯出進出站時刻表：{exception.Message}"]);
        }
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
                        instruction.PassingSpeedLimitMetersPerSecond,
                        instruction.WaitForOvertakeServiceRunId)))),
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
