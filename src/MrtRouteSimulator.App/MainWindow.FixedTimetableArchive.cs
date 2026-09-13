using System.Globalization;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    /// <summary>顯示匯入封存的實際結果，不建立或重跑 SimulationWorld。</summary>
    private void DisplayFixedTimetableArchive(FixedTimetableArchiveDocument archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var project = archive.Project;
        var startClockSeconds = project.Simulation.StartClockSeconds;
        TimetableRows.Clear();
        foreach (var entry in archive.Entries)
        {
            var serviceName = project.ServiceTypes!
                .FirstOrDefault(row => row.Id.Equals(entry.ServiceTypeId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                ?? entry.ServiceTypeId;
            var patternName = project.StopPatterns!
                .FirstOrDefault(row => row.Id.Equals(entry.StopPatternId, StringComparison.OrdinalIgnoreCase))?.DisplayName
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
                "—",
                "—",
                entry.DelaySeconds is { } delay ? $"{delay:0.0} s" : "—",
                entry.Status));
        }

        TimetableSourceText.Text =
            $"固定時刻表封存（格式 {archive.ArchiveFormatVersion}）：已完成模擬世界的實際到離站事件；重新計算會依隨附格式版本 7 專案設定建立新的動態模擬。";

        string DisplayClock(double? seconds) => seconds is { } value
            ? FormatClock(startClockSeconds + value)
            : "—";
    }
}
