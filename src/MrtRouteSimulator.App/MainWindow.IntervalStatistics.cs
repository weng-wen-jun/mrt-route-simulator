using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private int _lastIntervalRefreshSecond = -1;
    private PlaybackFrame? _intervalFilterSourceFrame;

    private void RefreshIntervalStatistics_Click(object sender, RoutedEventArgs e) => PopulateIntervalStatistics();

    private void IntervalFilter_Changed(object sender, SelectionChangedEventArgs e) => PopulateIntervalStatistics();

    private void IntervalFilter_Changed(object sender, RoutedEventArgs e) => PopulateIntervalStatistics();

    private void PopulateIntervalStatistics(bool throttled = false)
    {
        if (!_v2Enabled || _latestPlaybackFrame is null)
        {
            IntervalStatisticRows.Clear();
            JourneyStatisticRows.Clear();
            if (IntervalSummaryText is not null) IntervalSummaryText.Text = "目前不是 V2 模擬。";
            return;
        }

        var currentSecond = (int)Math.Floor(_latestPlaybackFrame.CurrentTimeSeconds);
        if (throttled && currentSecond == _lastIntervalRefreshSecond) return;
        _lastIntervalRefreshSecond = currentSecond;
        RefreshIntervalFilterOptions();
        IntervalStatisticsResult result;
        try
        {
            result = BuildIntervalStatisticsResult();
        }
        catch (InvalidOperationException exception)
        {
            ShowValidation([exception.Message]);
            IntervalSummaryText.Text = "篩選條件無效。";
            return;
        }
        JourneyStatisticRows.Clear();
        foreach (var item in result.JourneyStatistics)
        {
            JourneyStatisticRows.Add(new JourneyStatisticRow(
                item.VehicleId,
                item.ServiceRunId,
                DirectionToChinese(item.Direction),
                $"{item.OriginStationId} → {item.TerminalStationId}",
                item.Status,
                item.DepartureTimeSeconds is { } departure ? FormatClock(_startClockSeconds + departure) : "—",
                item.ArrivalTimeSeconds is { } arrival ? FormatClock(_startClockSeconds + arrival) : "—",
                item.TravelTimeSeconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—",
                item.AverageSpeedMetersPerSecond is { } average ? (average * 3.6).ToString("0.0", CultureInfo.InvariantCulture) : "—"));
        }

        IntervalStatisticRows.Clear();
        foreach (var item in result.AllIntervals)
        {
            IntervalStatisticRows.Add(new IntervalStatisticRow(
                item.VehicleId,
                item.ServiceRunId,
                DirectionToChinese(item.Direction),
                $"{item.FromStationId} → {item.ToStationId}",
                item.Status,
                item.DepartureTimeSeconds is { } departure ? FormatClock(_startClockSeconds + departure) : "—",
                item.ArrivalTimeSeconds is { } arrival ? FormatClock(_startClockSeconds + arrival) : "—",
                item.TravelTimeSeconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—",
                item.AverageSpeedMetersPerSecond is { } average ? (average * 3.6).ToString("0.0", CultureInfo.InvariantCulture) : "—",
                item.PeakSpeedMetersPerSecond is { } peak ? (peak * 3.6).ToString("0.0", CultureInfo.InvariantCulture) : "—",
                item.ControlLimitedSeconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—",
                item.ControlEvents.EventTypes.Count == 0
                    ? "無"
                    : string.Join("、", item.ControlEvents.EventTypes.Select(EventTypeToChinese))));
        }

        var completedJourneys = result.JourneyStatistics.Count(item => item.IsComplete);
        IntervalSummaryText.Text = $"完成 {result.CompletedCount} 區間、運行中 {result.InProgressCount} 區間；完成 {completedJourneys}/{result.JourneyStatistics.Count} 全程車次。";
    }

    private IntervalStatisticsResult BuildIntervalStatisticsResult()
    {
        if (_latestPlaybackFrame is null)
        {
            throw new InvalidOperationException("請先建立 V2 模擬。");
        }

        var direction = GetSelectedTag(IntervalDirectionComboBox) switch
        {
            "Outbound" => TrainDirection.Outbound,
            "Inbound" => TrainDirection.Inbound,
            _ => (TrainDirection?)null
        };
        var filter = new IntervalStatisticsFilter(
            Direction: direction,
            VehicleId: GetSelectedFilterId(IntervalVehicleComboBox),
            ServiceRunId: GetSelectedFilterId(IntervalServiceRunComboBox),
            VehicleTypeId: GetSelectedFilterId(IntervalVehicleTypeComboBox),
            ServiceClassId: GetSelectedFilterId(IntervalServiceTypeComboBox),
            ServicePatternId: GetSelectedFilterId(IntervalStopPatternComboBox),
            StartSimulationTimeSeconds: ParseOptionalSimulationSecond(IntervalStartSecondTextBox, "篩選開始秒"),
            EndSimulationTimeSeconds: ParseOptionalSimulationSecond(IntervalEndSecondTextBox, "篩選結束秒"),
            IncludeInProgress: IncludeInProgressCheckBox.IsChecked == true);
        if (filter.StartSimulationTimeSeconds is { } start
            && filter.EndSimulationTimeSeconds is { } end
            && start > end)
        {
            throw new InvalidOperationException("篩選開始秒不可大於結束秒。");
        }
        return _resultAccumulator.BuildIntervalStatistics(filter)
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
    }

    private void RefreshIntervalFilterOptions()
    {
        if (_latestPlaybackFrame is null || ReferenceEquals(_intervalFilterSourceFrame, _latestPlaybackFrame)) return;
        _intervalFilterSourceFrame = _latestPlaybackFrame;
        var runs = _latestPlaybackFrame.DispatchPlan?.Runs ?? [];
        SetFilterOptions(IntervalVehicleComboBox, runs
            .Where(item => !string.IsNullOrWhiteSpace(item.VehicleId))
            .Select(item => new CatalogOption(item.VehicleId!, item.VehicleId!)));
        SetFilterOptions(IntervalServiceRunComboBox, runs.Select(item => new CatalogOption(item.ServiceRunId, item.ServiceRunId)));
        SetFilterOptions(IntervalVehicleTypeComboBox, VehicleTypeOptions);
        SetFilterOptions(IntervalServiceTypeComboBox, ServiceTypeOptions);
        SetFilterOptions(IntervalStopPatternComboBox, StopPatternOptions);
    }

    private static void SetFilterOptions(ComboBox comboBox, IEnumerable<CatalogOption> options)
    {
        comboBox.ItemsSource = new[] { new CatalogOption(string.Empty, "全部") }
            .Concat(options
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()))
            .ToArray();
        comboBox.SelectedIndex = 0;
    }

    private static string? GetSelectedFilterId(ComboBox comboBox) => comboBox.SelectedItem is CatalogOption option
        && !string.IsNullOrWhiteSpace(option.Id) ? option.Id : null;

    private static double? ParseOptionalSimulationSecond(TextBox textBox, string field)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text)) return null;
        if (!double.TryParse(textBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value) || value < 0)
        {
            throw new InvalidOperationException($"{field}必須是有限非負數，或留空表示不限制。");
        }
        return value;
    }

    private void ExportIntervalCsv_Click(object sender, RoutedEventArgs e) =>
        ExportIntervalCsv(summary: false);

    private void ExportIntervalSummaryCsv_Click(object sender, RoutedEventArgs e) =>
        ExportIntervalCsv(summary: true);

    private void ExportResourceOccupancyCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_v2Enabled || _latestPlaybackFrame is null)
            {
                throw new InvalidOperationException("請先建立並播放 V2 寫實模擬。");
            }

            var dialog = new SaveFileDialog
            {
                Title = "匯出資源占用與觀測容量 CSV",
                Filter = "CSV 資料 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "V2資源占用與觀測容量.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            var result = _resultAccumulator.BuildResourceOccupancy(_latestPlaybackFrame.CurrentTimeSeconds);
            File.WriteAllText(dialog.FileName, ResourceOccupancyAnalysis.BuildCsv(result),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusTextBlock.Text = $"已匯出：{dialog.FileName}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowValidation([$"無法匯出資源占用資料：{exception.Message}"]);
        }
    }

    private void ExportJourneyCsv_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = BuildIntervalStatisticsResult();
            var dialog = new SaveFileDialog
            {
                Title = "匯出起終站平均速率 CSV",
                Filter = "CSV 資料 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "V2起終站平均速率.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            File.WriteAllText(dialog.FileName, IntervalStatistics.BuildJourneyCsv(result, _startClockSeconds),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusTextBlock.Text = $"已匯出：{dialog.FileName}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowValidation([$"無法匯出起終站平均速率：{exception.Message}"]);
        }
    }

    private void ExportIntervalCsv(bool summary)
    {
        try
        {
            var result = BuildIntervalStatisticsResult();
            var dialog = new SaveFileDialog
            {
                Title = summary ? "匯出區間彙總 CSV" : "匯出區間明細 CSV",
                Filter = "CSV 資料 (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = summary ? "V2區間彙總.csv" : "V2區間統計.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            var csv = summary
                ? IntervalStatistics.BuildSummaryCsv(result)
                : IntervalStatistics.BuildCsv(result, _startClockSeconds);
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusTextBlock.Text = $"已匯出：{dialog.FileName}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowValidation([$"無法匯出區間統計：{exception.Message}"]);
        }
    }
}
