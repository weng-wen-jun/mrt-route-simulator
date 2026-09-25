using System.Globalization;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    /// <summary>
    /// 準備匯入封存的實際結果，不建立或重跑 SimulationWorld，也不先改動目前畫面。
    /// </summary>
    private async Task<IReadOnlyList<TimetableRow>> PrepareFixedTimetableArchiveRowsAsync(
        FixedTimetableArchiveDocument archive,
        IProgress<(double Percentage, string Message)> progress)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var project = archive.Project;
        var startClockSeconds = project.Simulation.StartClockSeconds;
        var rows = new List<TimetableRow>(archive.Entries.Length);
        for (var index = 0; index < archive.Entries.Length; index++)
        {
            var entry = archive.Entries[index];
            var serviceName = project.ServiceTypes!
                .FirstOrDefault(row => row.Id.Equals(entry.ServiceTypeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? entry.ServiceTypeId;
            var patternName = project.StopPatterns!
                .FirstOrDefault(row => row.Id.Equals(entry.StopPatternId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? entry.StopPatternId;
            rows.Add(new TimetableRow(
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
                "—",
                "—",
                entry.DelaySeconds is { } delay ? $"{delay:0.0} s" : "—",
                entry.Status));

            if ((index + 1) % 250 == 0)
            {
                var percentage = 75 + (index + 1) * 23d / archive.Entries.Length;
                progress.Report((percentage, $"正在建立固定時刻表結果… {percentage:0}%"));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        progress.Report((98, "正在完成固定時刻表結果…"));

        string DisplayClock(double? seconds) => seconds is { } value
            ? FormatClock(startClockSeconds + value)
            : "—";

        return rows;
    }

    /// <summary>將已完整準備的固定時刻表結果一次套用到結果頁。</summary>
    private void ApplyFixedTimetableArchive(
        FixedTimetableArchiveDocument archive,
        IReadOnlyList<TimetableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(rows);
        TimetableRows.Clear();
        foreach (var row in rows)
        {
            TimetableRows.Add(row);
        }

        TimetableSourceText.Text =
            $"固定時刻表封存（格式 {archive.ArchiveFormatVersion}）：已完成模擬世界的實際到離站事件；重新計算會依隨附格式版本 7 專案設定建立新的動態模擬。";
        WorkspaceTabControl.SelectedItem = ResultsTabItem;
        ResultsTabItem.BringIntoView();
    }
}
