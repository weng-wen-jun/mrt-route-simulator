using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MrtRouteSimulator.Engine;

internal static class Program
{
    private const double DefaultDurationSeconds = 8_000;
    private const string DefaultSampleRelativePath =
        "samples/11-小型-三站完整拓樸運行範例.mrtsim.json";

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            return Run(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"benchmark failed: {exception.Message}");
            return 2;
        }
    }

    private static int Run(BenchmarkOptions options)
    {
        var samplePath = ResolveSamplePath(options.SamplePath);
        var durationSeconds = options.DurationSeconds;
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        var worldOptions = new SimulationWorldOptions(
            Route: null,
            TrainParameters: runtime.TrainParameters,
            OperationalParameters: runtime.OperationalParameters,
            TrainCount: runtime.DispatchPlan.Runs.Count,
            InitialDepartureIntervalSeconds: document.Simulation.HeadwaySeconds,
            ProfileMode: document.Simulation.ProfileMode,
            MovingBlockMode: options.Planned ? MovingBlockMode.Independent : document.Simulation.MovingBlockMode,
            InitialBrakingEstimationMode: options.Planned
                ? BrakingEstimationMode.Service : document.Simulation.BrakingEstimationMode,
            TraceRetentionPolicy: SimulationTraceRetentionPolicy.Decimated(0.2),
            ServicePatterns: runtime.ServicePatterns,
            DispatchPlan: runtime.DispatchPlan,
            VehicleTypes: runtime.VehicleTypes,
            ServiceTypes: runtime.ServiceTypes,
            Topology: runtime.Topology) with
        {
            ProfileMode = options.Planned ? OperationProfileMode.BasicPhysics : document.Simulation.ProfileMode
        };
        var world = worldOptions.CreateWorld();
        var gcBeforeInitialization = CaptureGcCollections();
        var modeInitializationElapsedTicks = 0L;
        long modeInitializationAllocatedBytes = 0;
        if (options.Diagnostics || options.Mode != LeaderLookupBenchmarkMode.OracleOnly)
        {
            world.PerformanceDiagnosticsEnabled = true;
            if (options.Mode != LeaderLookupBenchmarkMode.OracleOnly)
            {
                var initializationAllocationStart = GC.GetTotalAllocatedBytes(precise: true);
                var initializationStopwatch = Stopwatch.StartNew();
                switch (options.Mode)
                {
                    case LeaderLookupBenchmarkMode.Shadow:
                        world.NearestLeaderShadowModeEnabled = true;
                        break;
                    case LeaderLookupBenchmarkMode.IndexedOnly:
                        world.BenchmarkIndexedLeaderLookupEnabled = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                initializationStopwatch.Stop();
                modeInitializationElapsedTicks = initializationStopwatch.ElapsedTicks;
                modeInitializationAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - initializationAllocationStart;
            }
            world.ResetPerformanceDiagnostics();
        }
        var gcAfterInitialization = CaptureGcCollections();
        var requestedTicks = (long)Math.Floor(
            durationSeconds / SimulationWorld.FixedTimeStepSeconds + 1e-9);
        var progressIntervalSeconds = Math.Clamp(durationSeconds / 20, 10, 100);
        var progressIntervalTicks = Math.Max(1L, (long)Math.Ceiling(
            progressIntervalSeconds / SimulationWorld.FixedTimeStepSeconds));

        Console.WriteLine("MRT playback performance benchmark (read-only)");
        if (options.Mode != LeaderLookupBenchmarkMode.OracleOnly)
        {
            Console.WriteLine($"modeInitializationMs={ToMilliseconds(modeInitializationElapsedTicks).ToString("0.###", CultureInfo.InvariantCulture)} "
                + $"modeInitializationAllocatedBytes={modeInitializationAllocatedBytes}");
            if (options.Mode == LeaderLookupBenchmarkMode.Shadow)
            {
                // Preserve the legacy fields for scripts that already consume --shadow output.
                Console.WriteLine($"shadowInitializationMs={ToMilliseconds(modeInitializationElapsedTicks).ToString("0.###", CultureInfo.InvariantCulture)} "
                    + $"shadowInitializationAllocatedBytes={modeInitializationAllocatedBytes}");
            }
        }
        Console.WriteLine($"sample={samplePath}");
        Console.WriteLine($"mode={(options.Planned ? "planned" : "actual")}");
        Console.WriteLine($"leaderLookupMode={options.Mode.ToCliValue()}");
        Console.WriteLine($"duration={durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s "
            + $"requestedTicks={requestedTicks} fixedStep={SimulationWorld.FixedTimeStepSeconds:0.0}s");
        Console.WriteLine($"profile={document.Simulation.ProfileMode} "
            + $"movingBlock={document.Simulation.MovingBlockMode} "
            + $"braking={document.Simulation.BrakingEstimationMode} "
            + $"trains={runtime.DispatchPlan.Runs.Count} retention=decimated/0.2s");

        var gcBeforeRun = CaptureGcCollections();
        var allocatedBytesBefore = GC.GetTotalAllocatedBytes(precise: false);
        var preciseAllocatedBytesBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        long firstTickElapsedTicks = 0;
        long firstTickAllocatedBytes = 0;
        long firstTickDiagnosticsAllocatedBytes = 0;
        SimulationWorldPerformanceDiagnostics? firstTickDiagnostics = null;
        long completedTicks = 0;
        if (requestedTicks > 0)
        {
            var preciseAllocationBeforeFirstTick = GC.GetTotalAllocatedBytes(precise: true);
            var firstTickStopwatch = Stopwatch.StartNew();
            world.AdvanceTo(SimulationWorld.FixedTimeStepSeconds);
            firstTickStopwatch.Stop();
            firstTickElapsedTicks = firstTickStopwatch.ElapsedTicks;
            var preciseAllocationAfterFirstTick = GC.GetTotalAllocatedBytes(precise: true);
            firstTickAllocatedBytes = preciseAllocationAfterFirstTick - preciseAllocationBeforeFirstTick;
            completedTicks = 1;
            if (options.Diagnostics || options.Mode != LeaderLookupBenchmarkMode.OracleOnly)
            {
                var preciseAllocationBeforeFirstTickDiagnostics = GC.GetTotalAllocatedBytes(precise: true);
                firstTickDiagnostics = world.GetPerformanceDiagnostics();
                firstTickDiagnosticsAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true)
                    - preciseAllocationBeforeFirstTickDiagnostics;
            }
            if (completedTicks >= progressIntervalTicks || completedTicks == requestedTicks)
            {
                PrintProgress(world, completedTicks, requestedTicks, stopwatch.Elapsed);
            }
        }

        while (completedTicks < requestedTicks)
        {
            var nextTicks = Math.Min(requestedTicks, completedTicks + progressIntervalTicks);
            world.AdvanceTo(nextTicks * SimulationWorld.FixedTimeStepSeconds);
            completedTicks = nextTicks;
            PrintProgress(world, completedTicks, requestedTicks, stopwatch.Elapsed);
        }

        stopwatch.Stop();
        var gcAfterRun = CaptureGcCollections();
        var allocatedBytesDelta = GC.GetTotalAllocatedBytes(precise: false) - allocatedBytesBefore;
        var preciseAllocatedBytesDelta = GC.GetTotalAllocatedBytes(precise: true) - preciseAllocatedBytesBefore;
        Console.WriteLine();
        Console.WriteLine("summary");
        PrintProgress(world, completedTicks, requestedTicks, stopwatch.Elapsed);
        Console.WriteLine($"status=duration-reached worldComplete={world.IsComplete}");
        Console.WriteLine($"allocatedBytesDelta={allocatedBytesDelta}");
        var finalDiagnostics = world.GetPerformanceDiagnostics();
        if (options.Diagnostics || options.Mode != LeaderLookupBenchmarkMode.OracleOnly)
        {
            Console.WriteLine(
                $"timing firstTickMs={ToMilliseconds(firstTickElapsedTicks):0.######} "
                + "firstTickIncludesLeaderQueries=true "
                + $"firstLeaderLookupMs={ToMilliseconds(finalDiagnostics.FirstLeaderLookupTimestampTicks, finalDiagnostics.TimestampFrequency):0.######} "
                + $"firstGraphDistanceMs={ToMilliseconds(finalDiagnostics.FirstGraphDistanceTimestampTicks, finalDiagnostics.TimestampFrequency):0.######}");
        }
        if (options.Diagnostics || options.Mode != LeaderLookupBenchmarkMode.OracleOnly)
        {
            var diagnostics = finalDiagnostics;
            PrintDiagnostics(diagnostics);
            if (options.Mode == LeaderLookupBenchmarkMode.Shadow)
            {
                PrintShadowDiagnostics(diagnostics, world);
            }
            else if (options.Mode == LeaderLookupBenchmarkMode.IndexedOnly)
            {
                PrintIndexedDiagnostics(diagnostics);
            }
        }
        var eventBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(world.Events));
        var eventSha256 = Convert.ToHexString(SHA256.HashData(eventBytes));
        Console.WriteLine($"eventSha256={eventSha256}");
        PrintJsonSummary(
            options,
            samplePath,
            durationSeconds,
            requestedTicks,
            completedTicks,
            world,
            stopwatch,
            firstTickElapsedTicks,
            firstTickAllocatedBytes,
            firstTickDiagnosticsAllocatedBytes,
            firstTickDiagnostics,
            modeInitializationElapsedTicks,
            modeInitializationAllocatedBytes,
            allocatedBytesDelta,
            preciseAllocatedBytesDelta,
            gcBeforeInitialization,
            gcAfterInitialization,
            gcBeforeRun,
            gcAfterRun,
            eventSha256);
        return 0;
    }

    private static void PrintDiagnostics(SimulationWorldPerformanceDiagnostics diagnostics)
    {
        Console.WriteLine(
            $"diagnostics enabled={diagnostics.Enabled} "
            + $"tickCount={diagnostics.TickCount} "
            + $"activeTrainCount={diagnostics.ActiveTrainCount} "
            + $"averageActiveTrainCount={diagnostics.AverageActiveTrainCount.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"peakActiveTrainCount={diagnostics.PeakActiveTrainCount} "
            + $"leaderLookupCount={diagnostics.LeaderLookupCount} "
            + $"candidatePairCount={diagnostics.CandidatePairCount} "
            + $"topologySafetyMetricCallCount={diagnostics.TopologySafetyMetricCallCount} "
            + $"graphDistanceCallCount={diagnostics.GraphDistanceCallCount} "
            + $"occupancyRefreshCount={diagnostics.OccupancyRefreshCount} "
            + $"occupancySetCount={diagnostics.OccupancySetCount} "
            + $"fullScanFallbackCount={diagnostics.FullScanFallbackCount}");
        Console.WriteLine(
            $"diagnosticsMs computeSafetyObservations={ToMilliseconds(diagnostics.ComputeSafetyObservationsTimestampTicks):0.###} "
            + $"findNearestTopologyLeader={ToMilliseconds(diagnostics.FindNearestTopologyLeaderTimestampTicks):0.###} "
            + $"firstLeaderLookup={ToMilliseconds(diagnostics.FirstLeaderLookupTimestampTicks):0.###} "
            + $"topologySafetyMetrics={ToMilliseconds(diagnostics.TopologySafetyMetricsTimestampTicks):0.###} "
            + $"graphDistance={ToMilliseconds(diagnostics.GraphDistanceTimestampTicks):0.###} "
            + $"firstGraphDistance={ToMilliseconds(diagnostics.FirstGraphDistanceTimestampTicks):0.###} "
            + $"applyCollisionProtection={ToMilliseconds(diagnostics.ApplyCollisionProtectionTimestampTicks):0.###} "
            + $"occupancyRefresh={ToMilliseconds(diagnostics.OccupancyRefreshTimestampTicks):0.###} "
            + $"tickCore={ToMilliseconds(diagnostics.TickCoreTimestampTicks):0.###}");

        double ToMilliseconds(long timestampTicks) =>
            timestampTicks * 1_000d / diagnostics.TimestampFrequency;
    }

    private static void PrintShadowDiagnostics(
        SimulationWorldPerformanceDiagnostics diagnostics,
        SimulationWorld world)
    {
        var fallbackRatio = diagnostics.ShadowLookupCount == 0
            ? 0
            : (double)diagnostics.FullScanFallbackCount / diagnostics.ShadowLookupCount;
        Console.WriteLine(
            $"shadowLookupCount={diagnostics.ShadowLookupCount} "
            + $"indexedSuccessCount={diagnostics.IndexedSuccessCount} "
            + $"fallbackCount={diagnostics.FullScanFallbackCount} "
            + $"fallbackRatio={fallbackRatio.ToString("0.000000", CultureInfo.InvariantCulture)} "
            + $"mismatchCount={diagnostics.ShadowMismatchCount} "
            + $"averageCandidatesBefore={diagnostics.AverageOracleCandidateCount.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"averageCandidatesAfter={diagnostics.AverageIndexedCandidateCount.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"p95CandidatesAfter={diagnostics.ShadowIndexedCandidateP95} "
            + $"maxCandidatesAfter={diagnostics.ShadowIndexedCandidateMax} "
            + $"indexBuildCount={diagnostics.IndexBuildCount} "
            + $"indexBuildMs={ToMilliseconds(diagnostics.IndexBuildTimestampTicks):0.###} "
            + $"indexQueryMs={ToMilliseconds(diagnostics.IndexQueryTimestampTicks):0.###}");

        var fallbackReasons = world.NearestLeaderShadowFallbackReasons
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}:{item.Value}")
            .ToArray();
        Console.WriteLine($"fallbackReasons={(fallbackReasons.Length == 0 ? "none" : string.Join(",", fallbackReasons))}");

        var mismatches = world.NearestLeaderShadowMismatches.Take(10).ToArray();
        Console.WriteLine($"mismatchSamples={mismatches.Length}");
        for (var index = 0; index < mismatches.Length; index++)
        {
            var mismatch = mismatches[index];
            Console.WriteLine(
                $"mismatch[{index}] time={mismatch.SimulationTimeSeconds.ToString("0.###", CultureInfo.InvariantCulture)} "
                + $"follower={mismatch.FollowerVehicleId} "
                + $"oracle={mismatch.OracleLeaderVehicleId ?? "null"} "
                + $"indexed={mismatch.IndexedLeaderVehicleId ?? "null"} "
                + $"oracleHead={FormatNullable(mismatch.OracleHeadDistanceMeters)} "
                + $"indexedHead={FormatNullable(mismatch.IndexedHeadDistanceMeters)} "
                + $"oracleGap={FormatNullable(mismatch.OracleActualGapMeters)} "
                + $"indexedGap={FormatNullable(mismatch.IndexedActualGapMeters)} "
                + $"oracleStatus={mismatch.OracleSafetyStatus?.ToString() ?? "null"} "
                + $"indexedStatus={mismatch.IndexedSafetyStatus?.ToString() ?? "null"} "
                + $"oracleControl={FormatNullable(mismatch.OracleControlLimitMetersPerSecond)} "
                + $"indexedControl={FormatNullable(mismatch.IndexedControlLimitMetersPerSecond)} "
                + $"candidates=[{string.Join(",", mismatch.CandidateVehicleIds)}] "
                + $"generation={mismatch.PhysicalStateGeneration}");
        }

        double ToMilliseconds(long timestampTicks) =>
            timestampTicks * 1_000d / diagnostics.TimestampFrequency;

        static string FormatNullable(double? value) =>
            value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";
    }

    private static void PrintIndexedDiagnostics(SimulationWorldPerformanceDiagnostics diagnostics)
    {
        var fallbackRatio = diagnostics.ShadowLookupCount == 0
            ? 0
            : (double)diagnostics.FullScanFallbackCount / diagnostics.ShadowLookupCount;
        Console.WriteLine(
            $"indexedLookupCount={diagnostics.ShadowLookupCount} "
            + $"indexedSuccessCount={diagnostics.IndexedSuccessCount} "
            + $"fallbackCount={diagnostics.FullScanFallbackCount} "
            + $"fallbackRatio={fallbackRatio.ToString("0.000000", CultureInfo.InvariantCulture)} "
            + $"averageCandidates={diagnostics.AverageIndexedCandidateCount.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"p95Candidates={diagnostics.ShadowIndexedCandidateP95} "
            + $"maxCandidates={diagnostics.ShadowIndexedCandidateMax} "
            + $"indexBuildCount={diagnostics.IndexBuildCount} "
            + $"indexBuildMs={ToMilliseconds(diagnostics.IndexBuildTimestampTicks, diagnostics.TimestampFrequency):0.###} "
            + $"indexQueryMs={ToMilliseconds(diagnostics.IndexQueryTimestampTicks, diagnostics.TimestampFrequency):0.###}");
    }

    private static double ToMilliseconds(long timestampTicks) =>
        ToMilliseconds(timestampTicks, Stopwatch.Frequency);

    private static double ToMilliseconds(long timestampTicks, long timestampFrequency) =>
        timestampFrequency == 0 ? 0 : timestampTicks * 1_000d / timestampFrequency;

    private static void PrintProgress(
        SimulationWorld world,
        long completedTicks,
        long requestedTicks,
        TimeSpan elapsed)
    {
        var elapsedSeconds = Math.Max(elapsed.TotalSeconds, double.Epsilon);
        var simulatedSeconds = world.CurrentTimeSeconds;
        var effectiveRate = simulatedSeconds / elapsedSeconds;
        var millisecondsPerTick = elapsed.TotalMilliseconds / Math.Max(1, completedTicks);
        var progress = requestedTicks == 0
            ? 100
            : Math.Min(100, completedTicks * 100d / requestedTicks);
        var managedMemoryMiB = GC.GetTotalMemory(forceFullCollection: false) / (1024d * 1024d);
        using var process = Process.GetCurrentProcess();
        var workingSetMiB = process.WorkingSet64 / (1024d * 1024d);

        Console.WriteLine(
            $"progress={progress,6:0.0}% sim={simulatedSeconds,9:0.0}s "
            + $"ticks={completedTicks}/{requestedTicks} elapsed={elapsed.TotalSeconds,8:0.00}s "
            + $"msPerTick={millisecondsPerTick,8:0.000} effectiveRate={effectiveRate,8:0.00}x "
            + $"managedMiB={managedMemoryMiB,8:0.0} workingSetMiB={workingSetMiB,8:0.0} "
            + $"trajectory={world.Trajectory.Count} safety={world.SafetyHistory.Count} "
            + $"events={world.Events.Count}");
    }

    private static void PrintJsonSummary(
        BenchmarkOptions options,
        string samplePath,
        double durationSeconds,
        long requestedTicks,
        long completedTicks,
        SimulationWorld world,
        Stopwatch stopwatch,
        long firstTickElapsedTicks,
        long firstTickAllocatedBytes,
        long firstTickDiagnosticsAllocatedBytes,
        SimulationWorldPerformanceDiagnostics? firstTickDiagnostics,
        long modeInitializationElapsedTicks,
        long modeInitializationAllocatedBytes,
        long allocatedBytesDelta,
        long preciseAllocatedBytesDelta,
        GcCollectionCounts gcBeforeInitialization,
        GcCollectionCounts gcAfterInitialization,
        GcCollectionCounts gcBeforeRun,
        GcCollectionCounts gcAfterRun,
        string eventSha256)
    {
        var diagnostics = world.GetPerformanceDiagnostics();
        var elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        var managedBytes = GC.GetTotalMemory(forceFullCollection: false);
        using var process = Process.GetCurrentProcess();
        process.Refresh();

        var summary = new
        {
            benchmarkSchemaVersion = 1,
            sample = samplePath,
            mode = options.Mode.ToCliValue(),
            profileMode = options.Planned ? "planned" : "actual",
            durationSeconds,
            requestedTicks,
            completedTicks,
            fixedStepSeconds = SimulationWorld.FixedTimeStepSeconds,
            worldComplete = world.IsComplete,
            status = "duration-reached",
            initialization = new
            {
                cold = options.Mode != LeaderLookupBenchmarkMode.OracleOnly,
                elapsedStopwatchTicks = modeInitializationElapsedTicks,
                elapsedMilliseconds = ToMilliseconds(modeInitializationElapsedTicks),
                allocatedBytes = modeInitializationAllocatedBytes,
                gc = CreateGcDelta(gcBeforeInitialization, gcAfterInitialization)
            },
            timing = new
            {
                stopwatchFrequency = Stopwatch.Frequency,
                elapsedStopwatchTicks = stopwatch.ElapsedTicks,
                elapsedMilliseconds,
                elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                firstTick = new
                {
                    available = firstTickElapsedTicks != 0,
                    elapsedStopwatchTicks = firstTickElapsedTicks,
                    elapsedMilliseconds = ToMilliseconds(firstTickElapsedTicks),
                    includesLeaderQueries = true,
                    note = "first Tick includes all queries performed by that Tick; first leader lookup is reported separately"
                },
                firstLeaderLookup = new
                {
                    available = diagnostics.FirstLeaderLookupTimestampTicks != 0,
                    elapsedTimestampTicks = diagnostics.FirstLeaderLookupTimestampTicks,
                    elapsedMilliseconds = ToMilliseconds(
                        diagnostics.FirstLeaderLookupTimestampTicks,
                        diagnostics.TimestampFrequency)
                },
                firstGraphDistance = new
                {
                    available = diagnostics.FirstGraphDistanceTimestampTicks != 0,
                    elapsedTimestampTicks = diagnostics.FirstGraphDistanceTimestampTicks,
                    elapsedMilliseconds = ToMilliseconds(
                        diagnostics.FirstGraphDistanceTimestampTicks,
                        diagnostics.TimestampFrequency),
                    note = "first graph-distance call is reported separately; it is not necessarily the full graph-cache initialization"
                },
                steadyStateAfterFirstTick = CreateSteadyStateDiagnostics(diagnostics, firstTickDiagnostics),
                steadyStateAfterFirstLookup = CreateSteadyStateAfterFirstLookup(diagnostics)
            },
            memory = new
            {
                managedBytes,
                managedMiB = managedBytes / (1024d * 1024d),
                workingSetBytes = process.WorkingSet64,
                workingSetMiB = process.WorkingSet64 / (1024d * 1024d)
            },
            allocation = new
            {
                simulationDeltaBytes = allocatedBytesDelta,
                simulationDeltaBytesPrecise = preciseAllocatedBytesDelta,
                firstTickDeltaBytes = firstTickAllocatedBytes,
                firstTickDiagnosticsDeltaBytes = firstTickDiagnosticsAllocatedBytes,
                steadyStateAfterFirstTickDeltaBytes = SubtractFirst(
                    preciseAllocatedBytesDelta,
                    firstTickAllocatedBytes + firstTickDiagnosticsAllocatedBytes),
                totalDeltaBytes = modeInitializationAllocatedBytes + preciseAllocatedBytesDelta,
                steadyStateNote = "steadyStateAfterFirstTickDeltaBytes removes first Tick and its diagnostics snapshot allocation; it is a first-tick-removed proxy"
            },
            gc = new
            {
                initialization = CreateGcDelta(gcBeforeInitialization, gcAfterInitialization),
                simulation = CreateGcDelta(gcBeforeRun, gcAfterRun),
                total = CreateGcDelta(gcBeforeInitialization, gcAfterRun)
            },
            hash = new { eventSha256 },
            fallbackReasons = world.NearestLeaderShadowFallbackReasons
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
            counts = new
            {
                trajectory = world.Trajectory.Count,
                safety = world.SafetyHistory.Count,
                events = world.Events.Count,
                requestedTicks,
                completedTicks
            },
            diagnostics,
            firstTickDiagnostics,
            steadyStateDiagnostics = CreateSteadyStateDiagnostics(diagnostics, firstTickDiagnostics)
        };

        Console.WriteLine($"BENCHMARK_JSON={JsonSerializer.Serialize(summary)}");
    }

    private static object CreateSteadyStateAfterFirstLookup(
        SimulationWorldPerformanceDiagnostics diagnostics)
    {
        var firstLookupCount = diagnostics.LeaderLookupCount > 0 ? 1 : 0;
        var firstGraphDistanceCount = diagnostics.FirstGraphDistanceTimestampTicks != 0 ? 1 : 0;
        return new
        {
            leaderLookupCount = Math.Max(0, diagnostics.LeaderLookupCount - firstLookupCount),
            graphDistanceCallCount = Math.Max(0, diagnostics.GraphDistanceCallCount - firstGraphDistanceCount),
            findNearestTopologyLeaderTimestampTicks = SubtractFirst(
                diagnostics.FindNearestTopologyLeaderTimestampTicks,
                diagnostics.FirstLeaderLookupTimestampTicks),
            graphDistanceTimestampTicks = SubtractFirst(
                diagnostics.GraphDistanceTimestampTicks,
                diagnostics.FirstGraphDistanceTimestampTicks),
            indexQueryTimestampTicksAll = diagnostics.IndexQueryTimestampTicks,
            findNearestTopologyLeaderMilliseconds = ToMilliseconds(
                SubtractFirst(
                    diagnostics.FindNearestTopologyLeaderTimestampTicks,
                    diagnostics.FirstLeaderLookupTimestampTicks),
                diagnostics.TimestampFrequency),
            graphDistanceMilliseconds = ToMilliseconds(
                SubtractFirst(
                    diagnostics.GraphDistanceTimestampTicks,
                    diagnostics.FirstGraphDistanceTimestampTicks),
                diagnostics.TimestampFrequency),
            indexQueryMillisecondsAll = ToMilliseconds(
                diagnostics.IndexQueryTimestampTicks,
                diagnostics.TimestampFrequency),
            note = "leader and graph-distance timings remove their first call; index query timing remains all-query inclusive because no first-index-query diagnostic is available"
        };
    }

    private static object? CreateSteadyStateDiagnostics(
        SimulationWorldPerformanceDiagnostics diagnostics,
        SimulationWorldPerformanceDiagnostics? first)
    {
        if (first is null)
        {
            return null;
        }

        var tickCount = SubtractFirst(diagnostics.TickCount, first.TickCount);
        var shadowLookupCount = SubtractFirst(diagnostics.ShadowLookupCount, first.ShadowLookupCount);
        var indexedSuccessCount = SubtractFirst(diagnostics.IndexedSuccessCount, first.IndexedSuccessCount);
        return new
        {
            enabled = diagnostics.Enabled,
            tickCount,
            activeTrainCount = diagnostics.ActiveTrainCount,
            activeTrainCountSum = SubtractFirst(diagnostics.ActiveTrainCountSum, first.ActiveTrainCountSum),
            averageActiveTrainCount = tickCount == 0
                ? 0
                : (double)SubtractFirst(diagnostics.ActiveTrainCountSum, first.ActiveTrainCountSum) / tickCount,
            peakActiveTrainCount = diagnostics.PeakActiveTrainCount,
            leaderLookupCount = SubtractFirst(diagnostics.LeaderLookupCount, first.LeaderLookupCount),
            candidatePairCount = SubtractFirst(diagnostics.CandidatePairCount, first.CandidatePairCount),
            topologySafetyMetricCallCount = SubtractFirst(
                diagnostics.TopologySafetyMetricCallCount,
                first.TopologySafetyMetricCallCount),
            graphDistanceCallCount = SubtractFirst(
                diagnostics.GraphDistanceCallCount,
                first.GraphDistanceCallCount),
            occupancyRefreshCount = SubtractFirst(diagnostics.OccupancyRefreshCount, first.OccupancyRefreshCount),
            occupancySetCount = SubtractFirst(diagnostics.OccupancySetCount, first.OccupancySetCount),
            fullScanFallbackCount = SubtractFirst(diagnostics.FullScanFallbackCount, first.FullScanFallbackCount),
            shadowLookupCount,
            indexedSuccessCount,
            shadowMismatchCount = SubtractFirst(diagnostics.ShadowMismatchCount, first.ShadowMismatchCount),
            shadowOracleCandidateCountSum = SubtractFirst(
                diagnostics.ShadowOracleCandidateCountSum,
                first.ShadowOracleCandidateCountSum),
            shadowIndexedCandidateCountSum = SubtractFirst(
                diagnostics.ShadowIndexedCandidateCountSum,
                first.ShadowIndexedCandidateCountSum),
            shadowOracleCandidateMax = diagnostics.ShadowOracleCandidateMax,
            shadowIndexedCandidateP95 = diagnostics.ShadowIndexedCandidateP95,
            shadowIndexedCandidateMax = diagnostics.ShadowIndexedCandidateMax,
            indexBuildCount = SubtractFirst(diagnostics.IndexBuildCount, first.IndexBuildCount),
            computeSafetyObservationsTimestampTicks = SubtractFirst(
                diagnostics.ComputeSafetyObservationsTimestampTicks,
                first.ComputeSafetyObservationsTimestampTicks),
            findNearestTopologyLeaderTimestampTicks = SubtractFirst(
                diagnostics.FindNearestTopologyLeaderTimestampTicks,
                first.FindNearestTopologyLeaderTimestampTicks),
            topologySafetyMetricsTimestampTicks = SubtractFirst(
                diagnostics.TopologySafetyMetricsTimestampTicks,
                first.TopologySafetyMetricsTimestampTicks),
            graphDistanceTimestampTicks = SubtractFirst(
                diagnostics.GraphDistanceTimestampTicks,
                first.GraphDistanceTimestampTicks),
            applyCollisionProtectionTimestampTicks = SubtractFirst(
                diagnostics.ApplyCollisionProtectionTimestampTicks,
                first.ApplyCollisionProtectionTimestampTicks),
            occupancyRefreshTimestampTicks = SubtractFirst(
                diagnostics.OccupancyRefreshTimestampTicks,
                first.OccupancyRefreshTimestampTicks),
            indexBuildTimestampTicks = SubtractFirst(
                diagnostics.IndexBuildTimestampTicks,
                first.IndexBuildTimestampTicks),
            indexQueryTimestampTicks = SubtractFirst(
                diagnostics.IndexQueryTimestampTicks,
                first.IndexQueryTimestampTicks),
            firstLeaderLookupTimestampTicks = SubtractFirst(
                diagnostics.FirstLeaderLookupTimestampTicks,
                first.FirstLeaderLookupTimestampTicks),
            firstGraphDistanceTimestampTicks = SubtractFirst(
                diagnostics.FirstGraphDistanceTimestampTicks,
                first.FirstGraphDistanceTimestampTicks),
            tickCoreTimestampTicks = SubtractFirst(
                diagnostics.TickCoreTimestampTicks,
                first.TickCoreTimestampTicks),
            timestampFrequency = diagnostics.TimestampFrequency
        };
    }

    private static long SubtractFirst(long total, long first) =>
        total >= first ? total - first : 0;

    private static object CreateGcDelta(GcCollectionCounts before, GcCollectionCounts after) =>
        new
        {
            generation0 = after.Generation0 - before.Generation0,
            generation1 = after.Generation1 - before.Generation1,
            generation2 = after.Generation2 - before.Generation2
        };

    private static GcCollectionCounts CaptureGcCollections() =>
        new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    private static BenchmarkOptions ParseArguments(string[] args)
    {
        string? samplePath = null;
        var durationSeconds = DefaultDurationSeconds;
        var durationSpecified = false;
        var showHelp = false;
        var planned = false;
        var diagnostics = false;
        var mode = LeaderLookupBenchmarkMode.OracleOnly;
        var modeSpecified = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    continue;
                case "--sample":
                case "-s":
                    samplePath = ReadValue(args, ref index, argument);
                    continue;
                case "--duration":
                case "-d":
                    durationSeconds = ParseDuration(ReadValue(args, ref index, argument));
                    durationSpecified = true;
                    continue;
                case "--planned":
                    planned = true;
                    continue;
                case "--diagnostics":
                    diagnostics = true;
                    continue;
                case "--mode":
                {
                    var requestedMode = ParseLeaderLookupMode(ReadValue(args, ref index, argument));
                    if (modeSpecified && requestedMode != mode)
                    {
                        throw new ArgumentException(
                            $"conflicting leader lookup modes: {mode.ToCliValue()} and {requestedMode.ToCliValue()}");
                    }

                    mode = requestedMode;
                    modeSpecified = true;
                    continue;
                }
                case "--shadow":
                    if (modeSpecified && mode != LeaderLookupBenchmarkMode.Shadow)
                    {
                        throw new ArgumentException(
                            $"conflicting leader lookup modes: {mode.ToCliValue()} and shadow");
                    }

                    mode = LeaderLookupBenchmarkMode.Shadow;
                    modeSpecified = true;
                    continue;
            }

            if (argument.StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"unknown option: {argument}");
            }

            if (samplePath is null)
            {
                samplePath = argument;
                continue;
            }

            if (!durationSpecified)
            {
                durationSeconds = ParseDuration(argument);
                durationSpecified = true;
                continue;
            }

            throw new ArgumentException($"unexpected argument: {argument}");
        }

        return new BenchmarkOptions(
            samplePath ?? DefaultSampleRelativePath,
            durationSeconds,
            showHelp,
            planned,
            diagnostics,
            mode);
    }

    private static LeaderLookupBenchmarkMode ParseLeaderLookupMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "oracle-only" => LeaderLookupBenchmarkMode.OracleOnly,
            "indexed-only" => LeaderLookupBenchmarkMode.IndexedOnly,
            "shadow" => LeaderLookupBenchmarkMode.Shadow,
            _ => throw new ArgumentException(
                $"mode must be oracle-only, indexed-only, or shadow: {value}")
        };

    private static string ToCliValue(this LeaderLookupBenchmarkMode mode) =>
        mode switch
        {
            LeaderLookupBenchmarkMode.OracleOnly => "oracle-only",
            LeaderLookupBenchmarkMode.IndexedOnly => "indexed-only",
            LeaderLookupBenchmarkMode.Shadow => "shadow",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            throw new ArgumentException($"missing value for {option}");
        }

        index++;
        return args[index];
    }

    private static double ParseDuration(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            || !double.IsFinite(duration)
            || duration < 0)
        {
            throw new ArgumentException($"duration must be a finite non-negative number of seconds: {value}");
        }

        return duration;
    }

    private static string ResolveSamplePath(string samplePath)
    {
        var candidates = Path.IsPathRooted(samplePath)
            ? [samplePath]
            : EnumerateSearchRoots()
                .Select(root => Path.GetFullPath(Path.Combine(root, samplePath)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var resolved = candidates.FirstOrDefault(File.Exists);
        return resolved ?? throw new FileNotFoundException(
            $"Schema 8 sample was not found: {samplePath}", samplePath);
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                yield return directory.FullName;
                directory = directory.Parent;
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: dotnet run --project tests/MrtRouteSimulator.PlaybackBenchmarks "
            + "-- [sample-path] [duration-seconds]");
        Console.WriteLine("       --sample, -s <path>       Schema 8 sample path");
        Console.WriteLine("       --duration, -d <seconds>  requested simulation duration (default: 8000)");
        Console.WriteLine("       --planned                 use the planned-timeline profile");
        Console.WriteLine("       --diagnostics             enable opt-in hot-path counters and timings");
        Console.WriteLine("       --mode <mode>             oracle-only (default), indexed-only, or shadow");
        Console.WriteLine("       --shadow                  alias for --mode shadow (implies diagnostics)");
        Console.WriteLine($"Default sample: {DefaultSampleRelativePath}");
    }

    private enum LeaderLookupBenchmarkMode
    {
        OracleOnly,
        IndexedOnly,
        Shadow
    }

    private readonly record struct GcCollectionCounts(
        int Generation0,
        int Generation1,
        int Generation2);

    private sealed record BenchmarkOptions(
        string SamplePath,
        double DurationSeconds,
        bool ShowHelp,
        bool Planned,
        bool Diagnostics,
        LeaderLookupBenchmarkMode Mode);
}
