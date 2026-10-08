using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MrtRouteSimulator.App;

/// <summary>
/// Names used by the opt-in playback profiler.  The profiler is deliberately aggregate-only:
/// it keeps a bounded rolling sample for each timing and never writes one record per frame.
/// </summary>
public static class PlaybackDiagnosticMetricNames
{
    public const string WorkerAdvance = "WorkerAdvance";
    public const string WorkerNoProgressAdvanceBatch = "WorkerNoProgressAdvanceBatch";
    public const string WorkerNoProgressAdvanceCount = "WorkerNoProgressAdvanceCount";
    public const string WorkerFrameBuild = "WorkerFrameBuild";
    public const string WorkerFramePublish = "WorkerFramePublish";
    public const string WorkerFramePublishedCount = "WorkerFramePublishedCount";
    public const string DroppedFrameCount = "DroppedFrameCount";
    public const string WorkerFrameConsumedCount = "WorkerFrameConsumedCount";
    public const string UiFrameAppliedCount = "UiFrameAppliedCount";
    public const string UiFrameConsumedCount = "UiFrameConsumedCount";
    // Publish-to-consume age observed by the UI timer; this is not a direct enqueue probe.
    public const string DispatcherApplyAge = "DispatcherApplyAge";
    public const string ApplyPlaybackFrame = "ApplyPlaybackFrame";
    public const string ResultAccumulator = "ResultAccumulator";
    public const string TrainRowUpdate = "TrainRowUpdate";
    public const string SafetyRowUpdate = "SafetyRowUpdate";
    public const string EventRowUpdate = "EventRowUpdate";
    public const string RouteMarkerRender = "RouteMarkerRender";
    public const string SpeedChartRender = "SpeedChartRender";
    public const string SafetyChartRender = "SafetyChartRender";
    public const string TimeDistanceRender = "TimeDistanceRender";
    public const string TimetableUpdate = "TimetableUpdate";
    public const string SegmentStatistics = "SegmentStatistics";
    public const string ResourceOccupancy = "ResourceOccupancy";
    public const string V1V2Comparison = "V1V2Comparison";
    public const string InputStall = "InputStall";
    public const string InputStallExcess = "InputStallExcess";
}

public sealed record PlaybackTimingSummary(
    string Name,
    long Count,
    double TotalMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double MaxMilliseconds)
{
    public double AverageMilliseconds => Count == 0 ? 0 : TotalMilliseconds / Count;
}

public sealed record PlaybackRuntimeSnapshot(
    long AllocatedBytes,
    long ManagedBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public string AllocatedMiB => FormatMiB(AllocatedBytes);

    public string ManagedMiB => FormatMiB(ManagedBytes);

    public string WorkingSetMiB => FormatMiB(WorkingSetBytes);

    private static string FormatMiB(long bytes) =>
        bytes < 0 ? "unmeasurable" : (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture);
}

/// <summary>
/// Fast, read-only process memory sampling for opt-in diagnostics.  The public Process
/// properties may refresh a broader Windows process-info record; the native counters API asks
/// only for the two fields used by this profiler.  Callers retain a Process-property fallback
/// so diagnostics remain available if the API is unavailable or denied.
/// </summary>
internal static class PlaybackProcessMemoryDiagnostics
{
    public static bool TryCapture(out long privateBytes, out long workingSetBytes)
    {
        privateBytes = 0;
        workingSetBytes = 0;
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>();
            var counters = new ProcessMemoryCountersEx { Cb = size };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, size))
            {
                return false;
            }

            privateBytes = ToInt64(counters.PrivateUsage);
            workingSetBytes = ToInt64(counters.WorkingSetSize);
            return true;
        }
        catch
        {
            // The caller deliberately falls back to the established Process properties.
            return false;
        }
    }

    private static long ToInt64(UIntPtr value)
    {
        var unsigned = value.ToUInt64();
        return unsigned > long.MaxValue ? long.MaxValue : (long)unsigned;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(
        nint process,
        ref ProcessMemoryCountersEx counters,
        uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx
    {
        public uint Cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
    }
}

public sealed record PlaybackDiagnosticsSnapshot(
    bool Enabled,
    IReadOnlyDictionary<string, PlaybackTimingSummary> Timings,
    IReadOnlyDictionary<string, long> Counters,
    PlaybackRuntimeSnapshot? Runtime)
{
    public PlaybackTimingSummary? GetTiming(string name) =>
        Timings.TryGetValue(name, out var summary) ? summary : null;

    public long GetCount(string name) => Counters.TryGetValue(name, out var count) ? count : 0;

    /// <summary>
    /// Combines independent worker and UI collectors. Their metric names must be disjoint so that
    /// p50/p95 remain mathematically valid; duplicate names are rejected instead of averaged.
    /// </summary>
    public static PlaybackDiagnosticsSnapshot Merge(
        PlaybackDiagnosticsSnapshot first,
        PlaybackDiagnosticsSnapshot second,
        PlaybackRuntimeSnapshot? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var timings = new Dictionary<string, PlaybackTimingSummary>(first.Timings, StringComparer.Ordinal);
        foreach (var pair in second.Timings)
        {
            if (timings.ContainsKey(pair.Key))
            {
                throw new InvalidOperationException($"重複 playback diagnostic timing metric: {pair.Key}");
            }

            timings[pair.Key] = pair.Value;
        }

        var counters = new Dictionary<string, long>(first.Counters, StringComparer.Ordinal);
        foreach (var pair in second.Counters)
        {
            counters[pair.Key] = counters.TryGetValue(pair.Key, out var count)
                ? count + pair.Value
                : pair.Value;
        }

        return new PlaybackDiagnosticsSnapshot(first.Enabled || second.Enabled, timings, counters, runtime ?? first.Runtime ?? second.Runtime);
    }

    public string FormatReport(IEnumerable<string>? expectedTimingNames = null)
    {
        var names = expectedTimingNames?.ToArray() ?? Timings.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        var builder = new StringBuilder();
        builder.Append("enabled=").Append(Enabled ? "true" : "false");
        foreach (var name in names)
        {
            builder.AppendLine();
            if (!Timings.TryGetValue(name, out var timing) || timing.Count == 0)
            {
                builder.Append(name).Append("=unmeasurable");
                continue;
            }

            builder.Append(name)
                .Append(" count=").Append(timing.Count.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" p50=").Append(timing.P50Milliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" p95=").Append(timing.P95Milliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append("ms")
                .Append(" max=").Append(timing.MaxMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)).Append("ms");
        }

        if (Runtime is { } runtime)
        {
            builder.AppendLine();
            builder.Append("allocated=").Append(runtime.AllocatedMiB).Append("MiB")
                .Append(" managed=").Append(runtime.ManagedMiB).Append("MiB")
                .Append(" workingSet=").Append(runtime.WorkingSetMiB).Append("MiB")
                .Append(" gc=").Append(runtime.Gen0Collections).Append('/')
                .Append(runtime.Gen1Collections).Append('/').Append(runtime.Gen2Collections);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Bounded, opt-in timing and counter collection for the end-to-end playback path.
/// A disabled instance is safe to leave attached to production playback; its hot methods
/// return before taking a lock or allocating a sample.
/// </summary>
public sealed class PlaybackPerformanceDiagnostics
{
    private const int DefaultSampleCapacity = 4096;
    private readonly object _gate = new();
    private readonly int _sampleCapacity;
    private readonly Dictionary<string, TimingAccumulator> _timings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private bool _enabled;

    public PlaybackPerformanceDiagnostics(bool enabled = false, int sampleCapacity = DefaultSampleCapacity)
    {
        if (sampleCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCapacity));
        }

        _sampleCapacity = sampleCapacity;
        _enabled = enabled;
    }

    public bool IsEnabled => Volatile.Read(ref _enabled);

    public void Enable(bool enabled = true) => Volatile.Write(ref _enabled, enabled);

    public void Reset()
    {
        lock (_gate)
        {
            _timings.Clear();
            _counters.Clear();
        }
    }

    public IDisposable Measure(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return !IsEnabled ? NoopScope.Instance : new TimingScope(this, name);
    }

    public void RecordTiming(string name, double milliseconds)
    {
        if (!IsEnabled || !double.IsFinite(milliseconds) || milliseconds < 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_timings.TryGetValue(name, out var accumulator))
            {
                accumulator = new TimingAccumulator(_sampleCapacity);
                _timings.Add(name, accumulator);
            }

            accumulator.Add(milliseconds);
        }
    }

    public void RecordCount(string name, long delta = 1)
    {
        if (!IsEnabled || delta == 0)
        {
            return;
        }

        lock (_gate)
        {
            _counters[name] = _counters.TryGetValue(name, out var count) ? count + delta : delta;
        }
    }

    /// <summary>
    /// Measures elapsed age from worker publication timestamp until UI consumption. Because the
    /// sample is observed from the dispatcher timer, it is not a direct enqueue/dequeue probe.
    /// </summary>
    public void ObserveDispatcherApplyAge(long publishedTimestamp)
    {
        if (!IsEnabled || publishedTimestamp <= 0)
        {
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(publishedTimestamp).TotalMilliseconds;
        RecordTiming(PlaybackDiagnosticMetricNames.DispatcherApplyAge, elapsed);
    }

    public PlaybackRuntimeSnapshot CaptureRuntimeSnapshot()
    {
        if (!IsEnabled)
        {
            return new PlaybackRuntimeSnapshot(-1, -1, -1, -1, -1, -1);
        }

        long workingSetBytes;
        if (!PlaybackProcessMemoryDiagnostics.TryCapture(out _, out workingSetBytes))
        {
            using var process = Process.GetCurrentProcess();
            workingSetBytes = process.WorkingSet64;
        }

        return new PlaybackRuntimeSnapshot(
            GC.GetTotalAllocatedBytes(precise: false),
            GC.GetTotalMemory(forceFullCollection: false),
            workingSetBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    public PlaybackDiagnosticsMeasurement BeginMeasurement()
    {
        return new PlaybackDiagnosticsMeasurement(this, CaptureRuntimeSnapshot());
    }

    public PlaybackDiagnosticsSnapshot Snapshot(PlaybackRuntimeSnapshot? runtime = null)
    {
        lock (_gate)
        {
            var timings = _timings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Snapshot(pair.Key),
                StringComparer.Ordinal);
            var counters = new Dictionary<string, long>(_counters, StringComparer.Ordinal);
            return new PlaybackDiagnosticsSnapshot(IsEnabled, timings, counters, runtime);
        }
    }

    public string FormatReport(IEnumerable<string>? expectedTimingNames = null, PlaybackRuntimeSnapshot? runtime = null) =>
        Snapshot(runtime).FormatReport(expectedTimingNames);

    private sealed class TimingScope : IDisposable
    {
        private readonly PlaybackPerformanceDiagnostics _owner;
        private readonly string _name;
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();
        private int _disposed;

        public TimingScope(PlaybackPerformanceDiagnostics owner, string name)
        {
            _owner = owner;
            _name = name;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _owner.RecordTiming(_name, Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class TimingAccumulator
    {
        private readonly double[] _samples;
        private long _count;
        private double _totalMilliseconds;
        private double _maxMilliseconds;

        public TimingAccumulator(int capacity) => _samples = new double[capacity];

        public void Add(double milliseconds)
        {
            _samples[_count % _samples.Length] = milliseconds;
            _count++;
            _totalMilliseconds += milliseconds;
            _maxMilliseconds = Math.Max(_maxMilliseconds, milliseconds);
        }

        public PlaybackTimingSummary Snapshot(string name)
        {
            var sampleCount = (int)Math.Min(_count, _samples.Length);
            var values = new double[sampleCount];
            for (var index = 0; index < sampleCount; index++)
            {
                values[index] = _samples[index];
            }

            Array.Sort(values);
            return new PlaybackTimingSummary(
                name,
                _count,
                _totalMilliseconds,
                Percentile(values, .50),
                Percentile(values, .95),
                _maxMilliseconds);
        }

        private static double Percentile(double[] values, double percentile)
        {
            if (values.Length == 0)
            {
                return 0;
            }

            var position = (values.Length - 1) * percentile;
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return values[lower];
            }

            var fraction = position - lower;
            return values[lower] + (values[upper] - values[lower]) * fraction;
        }
    }
}

public sealed class PlaybackDiagnosticsMeasurement
{
    private readonly PlaybackPerformanceDiagnostics _owner;
    private readonly PlaybackRuntimeSnapshot _start;
    private int _completed;

    internal PlaybackDiagnosticsMeasurement(PlaybackPerformanceDiagnostics owner, PlaybackRuntimeSnapshot start)
    {
        _owner = owner;
        _start = start;
    }

    public PlaybackRuntimeSnapshot? Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0 || !_owner.IsEnabled || _start.AllocatedBytes < 0)
        {
            return null;
        }

        var end = _owner.CaptureRuntimeSnapshot();
        return new PlaybackRuntimeSnapshot(
            end.AllocatedBytes - _start.AllocatedBytes,
            end.ManagedBytes,
            end.WorkingSetBytes,
            end.Gen0Collections - _start.Gen0Collections,
            end.Gen1Collections - _start.Gen1Collections,
            end.Gen2Collections - _start.Gen2Collections);
    }
}
