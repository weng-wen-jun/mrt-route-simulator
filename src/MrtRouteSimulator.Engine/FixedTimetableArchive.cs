using System.Text.Json;
using System.Text.Json.Serialization;

namespace MrtRouteSimulator.Engine;

/// <summary>已完成 SimulationWorld 的固定實際進出站時刻封存；不會取代 Schema 7 的動態派車資料。</summary>
public sealed record FixedTimetableArchiveDocument(
    int ArchiveFormatVersion,
    string ArchiveType,
    SimulationProjectDocument Project,
    FixedTimetableArchiveEntry[] Entries);

/// <summary>固定時刻表中的單一實際進出站記錄。</summary>
public sealed record FixedTimetableArchiveEntry(
    string VehicleId,
    string ServiceRunId,
    TrainDirection Direction,
    string ServiceTypeId,
    string StopPatternId,
    string VehicleTypeId,
    string StationId,
    string StationName,
    double PositionMeters,
    double? ActualArrivalTimeSeconds,
    double? ActualDepartureTimeSeconds,
    double? ActualDwellSeconds,
    double? DelaySeconds,
    string Status);

/// <summary>固定時刻表封存檔的建立、驗證與 JSON 往返。</summary>
public static class FixedTimetableArchiveFormat
{
    public const int CurrentArchiveFormatVersion = 1;
    public const string ArchiveType = "fixed-timetable";
    public const int MaximumJsonCharacters = 32_000_000;
    public const int MaximumEntries = 100_000;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static FixedTimetableArchiveDocument Create(
        SimulationProjectDocument project,
        IEnumerable<OperationsTimetableEntry> timetableEntries)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(timetableEntries);
        // 依循一般專案存檔的正規化流程，讓封存總是攜帶可獨立重建的完整 Schema 7 專案設定。
        var archivedProject = SimulationProjectFormat.Deserialize(SimulationProjectFormat.Serialize(project));
        var archive = new FixedTimetableArchiveDocument(
            CurrentArchiveFormatVersion,
            ArchiveType,
            archivedProject,
            timetableEntries.Select(entry => new FixedTimetableArchiveEntry(
                entry.VehicleId,
                entry.ServiceRunId,
                entry.Direction,
                entry.ServiceTypeId,
                entry.StopPatternId,
                entry.VehicleTypeId,
                entry.StationId,
                entry.StationName,
                entry.PositionMeters,
                entry.ActualArrivalTimeSeconds,
                entry.ActualDepartureTimeSeconds,
                entry.ActualDwellSeconds,
                entry.DelaySeconds,
                entry.Status)).ToArray());
        Validate(archive);
        return archive;
    }

    public static string Serialize(FixedTimetableArchiveDocument archive)
    {
        Validate(archive);
        return JsonSerializer.Serialize(archive, SerializerOptions);
    }

    public static bool IsFixedTimetableArchive(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("archiveType", out var archiveType)
                && archiveType.ValueKind == JsonValueKind.String
                && string.Equals(archiveType.GetString(), ArchiveType, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static FixedTimetableArchiveDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new SimulationValidationException(["固定時刻表封存檔不可為空白。"]);
        }

        if (json.Length > MaximumJsonCharacters)
        {
            throw new SimulationValidationException([$"固定時刻表封存檔超過 {MaximumJsonCharacters:N0} 字元上限。"]);
        }

        try
        {
            var archive = JsonSerializer.Deserialize<FixedTimetableArchiveDocument>(json, SerializerOptions)
                ?? throw new SimulationValidationException(["固定時刻表封存檔缺少必要內容。"]);
            Validate(archive);
            return archive;
        }
        catch (JsonException exception)
        {
            throw new SimulationValidationException([$"固定時刻表封存 JSON 格式無效：{exception.Message}"]);
        }
    }

    public static void Validate(FixedTimetableArchiveDocument archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var errors = new List<string>();
        if (archive.ArchiveFormatVersion != CurrentArchiveFormatVersion)
        {
            errors.Add($"不支援固定時刻表封存版本 {archive.ArchiveFormatVersion}；目前僅支援 {CurrentArchiveFormatVersion}。");
        }

        if (!string.Equals(archive.ArchiveType, ArchiveType, StringComparison.Ordinal))
        {
            errors.Add("固定時刻表封存類型無效。");
        }

        if (archive.Project is null)
        {
            errors.Add("固定時刻表封存缺少對應專案設定。");
        }

        if (archive.Entries is null)
        {
            errors.Add("固定時刻表封存缺少時刻列。");
        }
        else if (archive.Entries.Length == 0 || archive.Entries.Length > MaximumEntries)
        {
            errors.Add($"固定時刻表封存時刻列必須介於 1 至 {MaximumEntries:N0} 筆。");
        }

        if (errors.Count > 0)
        {
            throw new SimulationValidationException(errors);
        }

        var project = archive.Project!;
        SimulationProjectFormat.Validate(project);
        var stations = project.Stations!
            .ToDictionary(item => item.StationId, StringComparer.OrdinalIgnoreCase);
        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < archive.Entries!.Length; index++)
        {
            var entry = archive.Entries[index];
            if (entry is null)
            {
                errors.Add($"固定時刻表第 {index + 1} 列不可為空白。");
                continue;
            }

            var prefix = $"固定時刻表第 {index + 1} 列";
            if (string.IsNullOrWhiteSpace(entry.ServiceRunId)
                || string.IsNullOrWhiteSpace(entry.VehicleId)
                || string.IsNullOrWhiteSpace(entry.ServiceTypeId)
                || string.IsNullOrWhiteSpace(entry.StopPatternId)
                || string.IsNullOrWhiteSpace(entry.VehicleTypeId)
                || string.IsNullOrWhiteSpace(entry.StationId)
                || string.IsNullOrWhiteSpace(entry.StationName)
                || string.IsNullOrWhiteSpace(entry.Status))
            {
                errors.Add($"{prefix}缺少必要識別或狀態欄位。");
            }

            if (!Enum.IsDefined(entry.Direction))
            {
                errors.Add($"{prefix}方向無效。");
            }

            if (!double.IsFinite(entry.PositionMeters) || entry.PositionMeters < 0)
            {
                errors.Add($"{prefix}里程必須是非負有限數值。");
            }

            ValidateOptionalNonNegativeFinite(entry.ActualArrivalTimeSeconds, $"{prefix}實際到站時間", errors);
            ValidateOptionalNonNegativeFinite(entry.ActualDepartureTimeSeconds, $"{prefix}實際離站時間", errors);
            ValidateOptionalNonNegativeFinite(entry.ActualDwellSeconds, $"{prefix}實際停站時間", errors);
            ValidateOptionalNonNegativeFinite(entry.DelaySeconds, $"{prefix}誤點時間", errors);
            if (entry.ActualArrivalTimeSeconds is null && entry.ActualDepartureTimeSeconds is null)
            {
                errors.Add($"{prefix}至少要有一個實際到站或離站時間。");
            }

            if (entry.ActualArrivalTimeSeconds is { } arrival
                && entry.ActualDepartureTimeSeconds is { } departure
                && departure < arrival)
            {
                errors.Add($"{prefix}實際離站時間不可早於到站時間。");
            }

            if (!stations.TryGetValue(entry.StationId, out var station))
            {
                errors.Add($"{prefix}參照不存在的車站「{entry.StationId}」。");
            }
            else
            {
                if (!string.Equals(entry.StationName, station.StationName, StringComparison.Ordinal)
                    || Math.Abs(entry.PositionMeters - StationPosition(project.Stations!, station.StationId)) > 0.001)
                {
                    errors.Add($"{prefix}車站名稱或里程與封存專案不一致。");
                }
            }

            var key = $"{entry.ServiceRunId}\u001f{entry.Direction}\u001f{entry.StationId}";
            if (!duplicateKeys.Add(key))
            {
                errors.Add($"{prefix}與其他列重複對應同一車次、方向與車站。");
            }
        }

        if (errors.Count > 0)
        {
            throw new SimulationValidationException(errors);
        }
    }

    private static void ValidateOptionalNonNegativeFinite(double? value, string fieldName, ICollection<string> errors)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
        {
            errors.Add($"{fieldName}必須是非負有限數值。");
        }
    }

    private static double StationPosition(IEnumerable<ProjectStation> stations, string stationId)
    {
        var position = 0d;
        foreach (var station in stations)
        {
            position += station.DistanceFromPreviousMeters;
            if (station.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase))
            {
                return position;
            }
        }

        return double.NaN;
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
