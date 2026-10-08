using System.Diagnostics;

namespace MrtRouteSimulator.Engine;

/// <summary>
/// Opt-in hot-path counters for performance studies. Production worlds keep diagnostics disabled.
/// Timestamp values use <see cref="Stopwatch.Frequency"/> and include nested timings, so they are
/// not intended to be added together.
/// </summary>
public sealed record SimulationWorldPerformanceDiagnostics(
    bool Enabled,
    long TickCount,
    int ActiveTrainCount,
    long ActiveTrainCountSum,
    int PeakActiveTrainCount,
    long LeaderLookupCount,
    long CandidatePairCount,
    long TopologySafetyMetricCallCount,
    long GraphDistanceCallCount,
    long OccupancyRefreshCount,
    long OccupancySetCount,
    long FullScanFallbackCount,
    long ShadowLookupCount,
    long IndexedSuccessCount,
    long ShadowMismatchCount,
    long ShadowOracleCandidateCountSum,
    long ShadowIndexedCandidateCountSum,
    int ShadowOracleCandidateMax,
    int ShadowIndexedCandidateP95,
    int ShadowIndexedCandidateMax,
    long IndexBuildCount,
    long ComputeSafetyObservationsTimestampTicks,
    long FindNearestTopologyLeaderTimestampTicks,
    long TopologySafetyMetricsTimestampTicks,
    long GraphDistanceTimestampTicks,
    long ApplyCollisionProtectionTimestampTicks,
    long OccupancyRefreshTimestampTicks,
    long IndexBuildTimestampTicks,
    long IndexQueryTimestampTicks,
    long FirstLeaderLookupTimestampTicks,
    long FirstGraphDistanceTimestampTicks,
    long TickCoreTimestampTicks,
    long TimestampFrequency)
{
    public double AverageActiveTrainCount => TickCount == 0 ? 0 : (double)ActiveTrainCountSum / TickCount;
    public double AverageOracleCandidateCount => ShadowLookupCount == 0
        ? 0
        : (double)ShadowOracleCandidateCountSum / ShadowLookupCount;
    public double AverageIndexedCandidateCount => IndexedSuccessCount == 0
        ? 0
        : (double)ShadowIndexedCandidateCountSum / IndexedSuccessCount;

    public static double ToMilliseconds(long timestampTicks) =>
        timestampTicks * 1_000d / Stopwatch.Frequency;
}

internal sealed class SimulationWorldPerformanceDiagnosticsAccumulator
{
    public bool Enabled { get; set; }

    public long TickCount { get; private set; }
    public int ActiveTrainCount { get; private set; }
    public long ActiveTrainCountSum { get; private set; }
    public int PeakActiveTrainCount { get; private set; }
    public long LeaderLookupCount { get; private set; }
    public long CandidatePairCount { get; private set; }
    public long TopologySafetyMetricCallCount { get; private set; }
    public long GraphDistanceCallCount { get; private set; }
    public long OccupancyRefreshCount { get; private set; }
    public long OccupancySetCount { get; private set; }
    public long FullScanFallbackCount { get; private set; }
    public long ShadowLookupCount { get; private set; }
    public long IndexedSuccessCount { get; private set; }
    public long ShadowMismatchCount { get; private set; }
    public long ShadowOracleCandidateCountSum { get; private set; }
    public long ShadowIndexedCandidateCountSum { get; private set; }
    public int ShadowOracleCandidateMax { get; private set; }
    public int ShadowIndexedCandidateMax { get; private set; }
    public long IndexBuildCount { get; private set; }
    public long ComputeSafetyObservationsTimestampTicks { get; private set; }
    public long FindNearestTopologyLeaderTimestampTicks { get; private set; }
    public long TopologySafetyMetricsTimestampTicks { get; private set; }
    public long GraphDistanceTimestampTicks { get; private set; }
    public long ApplyCollisionProtectionTimestampTicks { get; private set; }
    public long OccupancyRefreshTimestampTicks { get; private set; }
    public long IndexBuildTimestampTicks { get; private set; }
    public long IndexQueryTimestampTicks { get; private set; }
    public long FirstLeaderLookupTimestampTicks { get; private set; }
    public long FirstGraphDistanceTimestampTicks { get; private set; }
    public long TickCoreTimestampTicks { get; private set; }
    private readonly long[] indexedCandidateHistogram = new long[1_025];

    public long StartTiming() => Enabled ? Stopwatch.GetTimestamp() : 0;

    public void RecordTick(int activeTrainCount)
    {
        if (!Enabled) return;
        TickCount++;
        ActiveTrainCount = activeTrainCount;
        ActiveTrainCountSum += activeTrainCount;
        PeakActiveTrainCount = Math.Max(PeakActiveTrainCount, activeTrainCount);
    }

    public void RecordLeaderLookup() { if (Enabled) LeaderLookupCount++; }
    public void RecordCandidatePair() { if (Enabled) CandidatePairCount++; }
    public void RecordTopologySafetyMetricCall() { if (Enabled) TopologySafetyMetricCallCount++; }
    public void RecordGraphDistanceCall() { if (Enabled) GraphDistanceCallCount++; }
    public void RecordOccupancyRefresh() { if (Enabled) OccupancyRefreshCount++; }
    public void RecordOccupancySet() { if (Enabled) OccupancySetCount++; }
    public void RecordFullScanFallback() { if (Enabled) FullScanFallbackCount++; }
    public void RecordShadowLookup(int oracleCandidateCount)
    {
        if (!Enabled) return;
        ShadowLookupCount++;
        ShadowOracleCandidateCountSum += oracleCandidateCount;
        ShadowOracleCandidateMax = Math.Max(ShadowOracleCandidateMax, oracleCandidateCount);
    }

    public void RecordIndexedSuccess(int indexedCandidateCount)
    {
        if (!Enabled) return;
        IndexedSuccessCount++;
        ShadowIndexedCandidateCountSum += indexedCandidateCount;
        ShadowIndexedCandidateMax = Math.Max(ShadowIndexedCandidateMax, indexedCandidateCount);
        indexedCandidateHistogram[Math.Min(indexedCandidateCount, indexedCandidateHistogram.Length - 1)]++;
    }

    public void RecordShadowMismatch() { if (Enabled) ShadowMismatchCount++; }
    public void RecordIndexBuild() { if (Enabled) IndexBuildCount++; }

    public void RecordComputeSafetyObservationsElapsed(long startedAt) =>
        ComputeSafetyObservationsTimestampTicks += Elapsed(startedAt);

    public void RecordFindNearestTopologyLeaderElapsed(long startedAt)
    {
        var elapsed = Elapsed(startedAt);
        FindNearestTopologyLeaderTimestampTicks += elapsed;
        if (Enabled && LeaderLookupCount == 1) FirstLeaderLookupTimestampTicks = elapsed;
    }

    public void RecordTopologySafetyMetricsElapsed(long startedAt) =>
        TopologySafetyMetricsTimestampTicks += Elapsed(startedAt);

    public void RecordGraphDistanceElapsed(long startedAt)
    {
        var elapsed = Elapsed(startedAt);
        GraphDistanceTimestampTicks += elapsed;
        if (Enabled && GraphDistanceCallCount == 1) FirstGraphDistanceTimestampTicks = elapsed;
    }

    public void RecordApplyCollisionProtectionElapsed(long startedAt) =>
        ApplyCollisionProtectionTimestampTicks += Elapsed(startedAt);

    public void RecordOccupancyRefreshElapsed(long startedAt) =>
        OccupancyRefreshTimestampTicks += Elapsed(startedAt);

    public void RecordIndexBuildElapsed(long startedAt) =>
        IndexBuildTimestampTicks += Elapsed(startedAt);

    public void RecordIndexQueryElapsed(long startedAt) =>
        IndexQueryTimestampTicks += Elapsed(startedAt);

    public void RecordTickCoreElapsed(long startedAt) =>
        TickCoreTimestampTicks += Elapsed(startedAt);

    public SimulationWorldPerformanceDiagnostics Snapshot() => new(
        Enabled,
        TickCount,
        ActiveTrainCount,
        ActiveTrainCountSum,
        PeakActiveTrainCount,
        LeaderLookupCount,
        CandidatePairCount,
        TopologySafetyMetricCallCount,
        GraphDistanceCallCount,
        OccupancyRefreshCount,
        OccupancySetCount,
        FullScanFallbackCount,
        ShadowLookupCount,
        IndexedSuccessCount,
        ShadowMismatchCount,
        ShadowOracleCandidateCountSum,
        ShadowIndexedCandidateCountSum,
        ShadowOracleCandidateMax,
        CalculateIndexedCandidateP95(),
        ShadowIndexedCandidateMax,
        IndexBuildCount,
        ComputeSafetyObservationsTimestampTicks,
        FindNearestTopologyLeaderTimestampTicks,
        TopologySafetyMetricsTimestampTicks,
        GraphDistanceTimestampTicks,
        ApplyCollisionProtectionTimestampTicks,
        OccupancyRefreshTimestampTicks,
        IndexBuildTimestampTicks,
        IndexQueryTimestampTicks,
        FirstLeaderLookupTimestampTicks,
        FirstGraphDistanceTimestampTicks,
        TickCoreTimestampTicks,
        Stopwatch.Frequency);

    public void Reset()
    {
        TickCount = 0;
        ActiveTrainCount = 0;
        ActiveTrainCountSum = 0;
        PeakActiveTrainCount = 0;
        LeaderLookupCount = 0;
        CandidatePairCount = 0;
        TopologySafetyMetricCallCount = 0;
        GraphDistanceCallCount = 0;
        OccupancyRefreshCount = 0;
        OccupancySetCount = 0;
        FullScanFallbackCount = 0;
        ShadowLookupCount = 0;
        IndexedSuccessCount = 0;
        ShadowMismatchCount = 0;
        ShadowOracleCandidateCountSum = 0;
        ShadowIndexedCandidateCountSum = 0;
        ShadowOracleCandidateMax = 0;
        ShadowIndexedCandidateMax = 0;
        IndexBuildCount = 0;
        ComputeSafetyObservationsTimestampTicks = 0;
        FindNearestTopologyLeaderTimestampTicks = 0;
        TopologySafetyMetricsTimestampTicks = 0;
        GraphDistanceTimestampTicks = 0;
        ApplyCollisionProtectionTimestampTicks = 0;
        OccupancyRefreshTimestampTicks = 0;
        IndexBuildTimestampTicks = 0;
        IndexQueryTimestampTicks = 0;
        FirstLeaderLookupTimestampTicks = 0;
        FirstGraphDistanceTimestampTicks = 0;
        TickCoreTimestampTicks = 0;
        Array.Clear(indexedCandidateHistogram);
    }

    private int CalculateIndexedCandidateP95()
    {
        if (IndexedSuccessCount == 0) return 0;
        var target = (long)Math.Ceiling(IndexedSuccessCount * 0.95);
        long cumulative = 0;
        for (var index = 0; index < indexedCandidateHistogram.Length; index++)
        {
            cumulative += indexedCandidateHistogram[index];
            if (cumulative >= target) return index;
        }

        return indexedCandidateHistogram.Length - 1;
    }

    private long Elapsed(long startedAt) =>
        Enabled && startedAt != 0 ? Stopwatch.GetTimestamp() - startedAt : 0;
}
