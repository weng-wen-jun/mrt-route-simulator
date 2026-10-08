using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Fast, non-visual regression checks for the presentation-only time-distance
/// source cache. The WPF runner may invoke <see cref="Run"/> from its harness.
/// </summary>
internal static class TimeDistanceCacheTests
{
    public static void Run()
    {
        VerifyIncrementalAndBatchUseTheSameStreamingAlgorithm();
        VerifyCriticalBoundariesAndExtremaArePinned();
        VerifyCursorAndSourceResetContract();
        VerifyTypicalLongSourceHasBoundedOrdinaryPointsAndCoverage();
        Console.WriteLine("[通過] Time-Distance incremental trajectory cache");
    }

    private static void VerifyIncrementalAndBatchUseTheSameStreamingAlgorithm()
    {
        var source = Enumerable.Range(0, 420)
            .Select(index => BuildSample(index, TrainDirection.Outbound))
            .Concat(Enumerable.Range(0, 12).Select(index => BuildSample(index, TrainDirection.Inbound)))
            .ToArray();

        var batch = new TimeDistanceTrajectoryCache();
        batch.Append(source);

        var incremental = new TimeDistanceTrajectoryCache();
        incremental.Append(source.Take(37).ToArray());
        incremental.Append(source.Take(163).ToArray());
        incremental.Append(source.Take(317).ToArray());
        incremental.Append(source);

        Require(incremental.ProcessedCount == source.Length, "增量快取 cursor 未走到 source 尾端。");
        Require(incremental.TotalProcessed == source.Length, "TotalProcessed 應與 ProcessedCount 一致。");
        Require(batch.Groups.Count == incremental.Groups.Count, "批次與增量快取的 group 數不同。");
        foreach (var expected in batch.Groups)
        {
            var actual = incremental.Groups.Single(item =>
                item.VehicleId == expected.VehicleId
                && item.ServiceRunId == expected.ServiceRunId
                && item.Direction == expected.Direction);
            Require(expected.Samples.SequenceEqual(actual.Samples),
                $"批次與增量 group {expected.VehicleId}/{expected.ServiceRunId}/{expected.Direction} 顯示點不同。");
            Require(expected.CriticalPointCount == actual.CriticalPointCount,
                "批次與增量 critical point 數不同。");
            Require(expected.OrdinaryPointCount == actual.OrdinaryPointCount,
                "批次與增量 ordinary point 數不同。");
        }

        Require(incremental.MinPosition == batch.MinPosition
            && incremental.MaxPosition == batch.MaxPosition
            && incremental.MaxTime == batch.MaxTime,
            "批次與增量 global bounds 不同。");

        var version = incremental.Version;
        var seriesVersion = incremental.Groups[0].Version;
        incremental.Append(source);
        Require(incremental.Version == version, "重複 append 相同 source count 不應再次處理。");
        Require(incremental.Groups[0].Version == seriesVersion,
            "重複 append 不應重建 group 顯示點。");
    }

    private static void VerifyCriticalBoundariesAndExtremaArePinned()
    {
        var source = Enumerable.Range(0, 140)
            .Select(index => BuildSample(index, TrainDirection.Outbound))
            .ToArray();
        var cache = new TimeDistanceTrajectoryCache(ordinaryPointBudget: 1, ordinarySamplingStride: 128);
        cache.Append(source);
        var series = cache.Groups.Single();
        var displayed = series.Samples.ToHashSet();

        // Each side of every transition is retained, even when the ordinary
        // budget is deliberately too small to retain ordinary samples.
        foreach (var index in new[] { 19, 20, 39, 40, 59, 60, 79, 80 })
        {
            Require(displayed.Contains(source[index]), $"critical transition point {index} 遺失。");
        }

        Require(displayed.Contains(source[101]), "position/speed local extrema 未保留。");
        Require(displayed.Contains(source[0]), "first point 未保留。");
        Require(displayed.Contains(source[^1]), "last point 未保留。");
        Require(series.CriticalPointCount > cache.OrdinaryPointBudget,
            "critical-only 超過 nominal budget 時應允許透明 soft exception。");
        Require(series.OrdinaryPointCount <= cache.OrdinaryPointBudget,
            "ordinary points 不得超過 nominal budget。");
    }

    private static void VerifyCursorAndSourceResetContract()
    {
        var source = Enumerable.Range(0, 90)
            .Select(index => BuildSample(index, TrainDirection.Outbound))
            .ToArray();
        var cache = new TimeDistanceTrajectoryCache();
        cache.Append(source);
        var firstVersion = cache.Version;
        cache.Append(source.Take(45).ToArray());

        Require(cache.ProcessedCount == 45, "source count drop 後應從新 artifact 重新處理。");
        Require(cache.Groups.Count == 1 && cache.Groups[0].SourceSampleCount == 45,
            "source count drop 後不可保留舊 artifact 的尾端樣本。");
        Require(cache.MaxTime == source[44].SimulationTimeSeconds,
            "source count drop 後 MaxTime 應重新計算。");
        Require(cache.Version > firstVersion, "source reset/append 應建立新 cache generation。");

        cache.Reset();
        Require(cache.ProcessedCount == 0 && cache.TotalProcessed == 0 && cache.Groups.Count == 0,
            "Reset 未清除 source cursor 與 groups。");
        Require(!cache.HasData && cache.MinPosition == 0 && cache.MaxPosition == 0 && cache.MaxTime == 0,
            "Reset 未清除 global bounds。");
    }

    private static void VerifyTypicalLongSourceHasBoundedOrdinaryPointsAndCoverage()
    {
        var source = Enumerable.Range(0, 20_000)
            .Select(index => BuildMonotonicSample(index) with
            {
                AccelerationMetersPerSecondSquared = index < 100
                    ? 0
                    : index < 10_000
                        ? 1
                        : index < 19_000
                            ? 0
                            : -1
            })
            .ToArray();
        var cache = new TimeDistanceTrajectoryCache();
        cache.Append(source);
        var series = cache.Groups.Single();

        Require(series.OrdinaryPointCount <= TimeDistanceTrajectoryCache.DefaultOrdinaryPointBudget,
            "20,000 sample source 的 ordinary display points 未受 per-group budget 限制。");
        Require(series.Samples.Count <= TimeDistanceTrajectoryCache.DefaultOrdinaryPointBudget + 4,
            "典型長來源應維持有界 display geometry（critical points 除外）。");
        Require(series.Samples[0].Equals(source[0]) && series.Samples[^1].Equals(source[^1]),
            "長來源仍須保留 first/last pinned points。");

        foreach (var index in new[] { 99, 100, 9_999, 10_000, 18_999, 19_000 })
        {
            Require(series.Samples.Contains(source[index]),
                $"加速度 regime boundary {index} 未保留為 critical point。");
        }

        Require(series.Samples.Any(sample => sample.SimulationTimeSeconds is >= 25.6 and <= 204.8),
            "階層抽稀遺失長來源的舊段 ordinary coverage。");
        Require(series.Samples.Any(sample => sample.SimulationTimeSeconds is >= 768 and <= 1280),
            "階層抽稀遺失長來源的中段 ordinary coverage。");
        Require(series.Samples.Any(sample => sample.SimulationTimeSeconds is >= 1792 and <= 1984),
            "階層抽稀遺失長來源的尾段 ordinary coverage。");
        Require(cache.MinPosition == source[0].PositionMeters
            && cache.MaxPosition == source[^1].PositionMeters
            && cache.MaxTime == source[^1].SimulationTimeSeconds,
            "長來源 global min/max/time incremental bounds 錯誤。");
    }

    private static TrajectorySample BuildSample(int index, TrainDirection direction)
    {
        var phase = index < 20 ? OperationalPhase.Accelerating : OperationalPhase.Cruising;
        var station = index < 40 ? "A" : "B";
        var track = index < 60 ? "DOWN" : "DOWN-ALT";
        var speedLimit = index < 80 ? 20d : 10d;
        var position = index * 10d;
        var speed = 10d;
        if (index == 101)
        {
            position = 1_500;
            speed = 50;
        }
        else if (index == 100 || index == 102)
        {
            position = 1_000;
            speed = 30;
        }

        return new TrajectorySample(
            SimulationTimeSeconds: index * 0.1,
            VehicleId: direction == TrainDirection.Outbound ? "V01" : "V02",
            ServiceRunId: direction == TrainDirection.Outbound ? "R01" : "R02",
            ServiceClassId: "LOCAL",
            ServicePatternId: "ALL",
            Direction: direction,
            TrackId: track,
            PositionMeters: position,
            SpeedMetersPerSecond: speed,
            AccelerationMetersPerSecondSquared: 0,
            Phase: phase,
            CurrentStationId: station,
            NextStationId: station == "A" ? "B" : "C",
            IsPlanned: false,
            TrackSpeedLimitMetersPerSecond: speedLimit);
    }

    private static TrajectorySample BuildMonotonicSample(int index) => new(
        SimulationTimeSeconds: index * 0.1,
        VehicleId: "V-LONG",
        ServiceRunId: "R-LONG",
        ServiceClassId: "LOCAL",
        ServicePatternId: "ALL",
        Direction: TrainDirection.Outbound,
        TrackId: "DOWN",
        PositionMeters: index * 10d,
        SpeedMetersPerSecond: 10,
        AccelerationMetersPerSecondSquared: 0,
        Phase: OperationalPhase.Cruising,
        CurrentStationId: "A",
        NextStationId: "B",
        IsPlanned: false,
        TrackSpeedLimitMetersPerSecond: 20);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
