using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Defines the simulation horizon and all derived measurement windows used by
/// the capacity experiments. Keeping these values together prevents a longer
/// run from accidentally retaining a two-hour denominator or four buckets.
/// </summary>
public sealed record MeasurementPlan
{
    public const double DefaultDiscardSeconds = 30 * 60;
    public const double DefaultBucketSeconds = 30 * 60;
    public const double DefaultTotalHorizonSeconds = 11 * 60 * 60;

    public required double TotalHorizonSeconds { get; init; }
    public required double DiscardSeconds { get; init; }
    public required double BucketSeconds { get; init; }
    public required bool LegacyThreeHourAlias { get; init; }

    public double? WarmupSecondsOverride { get; init; }
    public double? CooldownSecondsOverride { get; init; }
    public double MeasurementStartSeconds => WarmupSecondsOverride ?? DiscardSeconds;
    public double MeasurementEndSeconds => TotalHorizonSeconds - (CooldownSecondsOverride ?? DiscardSeconds);
    public double MeasurementDurationSeconds => MeasurementEndSeconds - MeasurementStartSeconds;
    public double MeasurementMidpointSeconds => MeasurementStartSeconds + MeasurementDurationSeconds / 2;
    public int BucketCount => checked((int)Math.Ceiling(MeasurementDurationSeconds / BucketSeconds - 1e-12));
    public string HorizonLabel => FormatHours(TotalHorizonSeconds);
    public string Id => $"horizon-{FormatSeconds(TotalHorizonSeconds)}-discard-{FormatSeconds(DiscardSeconds)}-bucket-{FormatSeconds(BucketSeconds)}-v2";
    public string CacheKey => WarmupSecondsOverride is null && CooldownSecondsOverride is null ? Id : Id + $"-warmup-{FormatSeconds(MeasurementStartSeconds)}-cooldown-{FormatSeconds(TotalHorizonSeconds - MeasurementEndSeconds)}";

    public static MeasurementPlan Parse(IReadOnlyList<string> args)
    {
        var legacyThreeHour = args.Any(item => item.Equals("--3h", StringComparison.OrdinalIgnoreCase));
        var horizonHours = ReadOptionalDouble(args, "--horizon-hours")
            ?? ReadOptionalDouble(args, "--total-hours")
            ?? (legacyThreeHour ? 3 : 11);
        var discardMinutes = ReadOptionalDouble(args, "--discard-minutes") ?? 30;
        var bucketMinutes = ReadOptionalDouble(args, "--bucket-minutes") ?? 30;
        var warmupMinutes = ReadOptionalDouble(args, "--warmup-minutes");
        var cooldownMinutes = ReadOptionalDouble(args, "--cooldown-minutes");
        if (warmupMinutes is { } warmup && (!double.IsFinite(warmup) || warmup < 0) || cooldownMinutes is { } cooldown && (!double.IsFinite(cooldown) || cooldown < 0)) throw new ArgumentException("暖機與收尾時間必須為非負有限數字。");

        if (!double.IsFinite(horizonHours) || horizonHours <= 0)
            throw new ArgumentException("--horizon-hours 必須是大於 0 的有限數字。");
        if (!double.IsFinite(discardMinutes) || discardMinutes < 0)
            throw new ArgumentException("--discard-minutes 必須是大於等於 0 的有限數字。");
        if (!double.IsFinite(bucketMinutes) || bucketMinutes <= 0)
            throw new ArgumentException("--bucket-minutes 必須是大於 0 的有限數字。");

        var plan = new MeasurementPlan
        {
            TotalHorizonSeconds = horizonHours * 3600,
            DiscardSeconds = discardMinutes * 60,
            BucketSeconds = bucketMinutes * 60,
            LegacyThreeHourAlias = legacyThreeHour,
            WarmupSecondsOverride = warmupMinutes * 60,
            CooldownSecondsOverride = cooldownMinutes * 60
        };
        if (plan.MeasurementDurationSeconds <= 0)
            throw new ArgumentException("丟棄前後的時間必須小於總模擬時間，才能保留中央量測窗口。");
        return plan;
    }

    public IEnumerable<double> ProgressBoundaries(double stepSeconds)
    {
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepSeconds));
        for (var boundary = stepSeconds; boundary < TotalHorizonSeconds - 1e-9; boundary += stepSeconds)
            yield return boundary;
        yield return TotalHorizonSeconds;
    }

    public bool IsInMeasurementWindow(double simulationTimeSeconds) =>
        simulationTimeSeconds >= MeasurementStartSeconds - 1e-9
        && simulationTimeSeconds < MeasurementEndSeconds - 1e-9;

    public bool IsInBucket(double simulationTimeSeconds, int index)
    {
        if (index < 0 || index >= BucketCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        var start = MeasurementStartSeconds + index * BucketSeconds;
        var end = Math.Min(MeasurementEndSeconds, start + BucketSeconds);
        return simulationTimeSeconds >= start - 1e-9 && simulationTimeSeconds < end - 1e-9;
    }

    public int[] CountBuckets<T>(IEnumerable<T> samples, Func<T, double> timeSelector)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(timeSelector);
        var counts = new int[BucketCount];
        foreach (var sample in samples)
        {
            var time = timeSelector(sample);
            if (!IsInMeasurementWindow(time))
                continue;
            var bucket = (int)Math.Floor((time - MeasurementStartSeconds) / BucketSeconds);
            if (bucket >= 0 && bucket < counts.Length)
                counts[bucket]++;
        }
        return counts;
    }

    public string ResolveDefaultOutputDirectoryName() =>
        LegacyThreeHourAlias ? "after-alignment-3h" : $"after-alignment-{HorizonLabel}";

    public void WriteManifest(string outputDirectory, JsonSerializerOptions? options = null)
    {
        Directory.CreateDirectory(outputDirectory);
        options ??= new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict
        };
        File.WriteAllText(Path.Combine(outputDirectory, "measurement-plan.json"), JsonSerializer.Serialize(this, options));
    }

    private static double? ReadOptionalDouble(IReadOnlyList<string> args, string name)
    {
        var prefix = name + "=";
        var value = args.FirstOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (value is null)
            return null;
        var text = value[prefix.Length..];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{name} 的值無法解析：{text}");
        return parsed;
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatHours(double seconds) =>
        (seconds / 3600).ToString("0.###", CultureInfo.InvariantCulture) + "h";
}

public static class MeasurementPlanSelfTests
{
    public static void Run()
    {
        var standard = MeasurementPlan.Parse([]);
        AssertNear(39_600, standard.TotalHorizonSeconds, "default horizon");
        AssertNear(1_800, standard.MeasurementStartSeconds, "default start");
        AssertNear(37_800, standard.MeasurementEndSeconds, "default end");
        AssertNear(36_000, standard.MeasurementDurationSeconds, "default duration");
        AssertEqual(20, standard.BucketCount, "default bucket count");
        AssertEqual("after-alignment-11h", standard.ResolveDefaultOutputDirectoryName(), "default output");

        var network = MeasurementPlan.Parse(["--horizon-hours=12", "--warmup-minutes=90", "--cooldown-minutes=30"]);
        AssertNear(5400, network.MeasurementStartSeconds, "network warmup");
        AssertNear(36000, network.MeasurementDurationSeconds, "network duration");
        AssertEqual(20, network.BucketCount, "network buckets");
        var legacy = MeasurementPlan.Parse(["--3h"]);
        AssertNear(10_800, legacy.TotalHorizonSeconds, "legacy horizon");
        AssertNear(9_000, legacy.MeasurementEndSeconds, "legacy end");
        AssertNear(7_200, legacy.MeasurementDurationSeconds, "legacy duration");
        AssertEqual(4, legacy.BucketCount, "legacy bucket count");
        AssertEqual("after-alignment-3h", legacy.ResolveDefaultOutputDirectoryName(), "legacy output");

        var alternate = MeasurementPlan.Parse(["--horizon-hours=5", "--discard-minutes=20", "--bucket-minutes=10"]);
        AssertNear(18_000, alternate.TotalHorizonSeconds, "alternate horizon");
        AssertNear(1_200, alternate.MeasurementStartSeconds, "alternate start");
        AssertNear(16_800, alternate.MeasurementEndSeconds, "alternate end");
        AssertEqual(26, alternate.BucketCount, "alternate bucket count");
        var bucketCounts = alternate.CountBuckets(new[] { 0d, 1_200d, 1_799.9, 1_800d, 16_799.9, 16_800d }, item => item);
        AssertEqual(4, bucketCounts.Sum(), "window sample count");
        AssertEqual(2, bucketCounts[0], "first bucket sample count");
        AssertEqual(1, bucketCounts[^1], "last bucket sample count");
    }

    private static void AssertNear(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 1e-9)
            throw new InvalidOperationException($"MeasurementPlan self-test failed for {name}: expected {expected}, actual {actual}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"MeasurementPlan self-test failed for {name}: expected {expected}, actual {actual}.");
    }
}

public static class ExperimentOutput
{
    public static string ResolveRoot(string repositoryRoot, IReadOnlyList<string> arguments, MeasurementPlan measurementPlan)
    {
        var requested = arguments.FirstOrDefault(item => item.StartsWith("--output-root=", StringComparison.OrdinalIgnoreCase))?[14..];
        if (!string.IsNullOrWhiteSpace(requested))
            return Path.GetFullPath(Path.IsPathRooted(requested)
                ? requested
                : Path.Combine(repositoryRoot, "artifacts", "capacity-throughput-experiment", requested));
        return Path.Combine(repositoryRoot, "artifacts", "capacity-throughput-experiment", measurementPlan.ResolveDefaultOutputDirectoryName());
    }
}
