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
        "samples/V4.0.0-完整拓撲執行驗證範例.mrtsim.json";

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
        var requestedTicks = (long)Math.Floor(
            durationSeconds / SimulationWorld.FixedTimeStepSeconds + 1e-9);
        var progressIntervalSeconds = Math.Clamp(durationSeconds / 20, 10, 100);
        var progressIntervalTicks = Math.Max(1L, (long)Math.Ceiling(
            progressIntervalSeconds / SimulationWorld.FixedTimeStepSeconds));

        Console.WriteLine("MRT playback performance benchmark (read-only)");
        Console.WriteLine($"sample={samplePath}");
        Console.WriteLine($"mode={(options.Planned ? "planned" : "actual")}");
        Console.WriteLine($"duration={durationSeconds.ToString("0.###", CultureInfo.InvariantCulture)}s "
            + $"requestedTicks={requestedTicks} fixedStep={SimulationWorld.FixedTimeStepSeconds:0.0}s");
        Console.WriteLine($"profile={document.Simulation.ProfileMode} "
            + $"movingBlock={document.Simulation.MovingBlockMode} "
            + $"braking={document.Simulation.BrakingEstimationMode} "
            + $"trains={runtime.DispatchPlan.Runs.Count} retention=decimated/0.2s");

        var stopwatch = Stopwatch.StartNew();
        long completedTicks = 0;
        while (completedTicks < requestedTicks)
        {
            var nextTicks = Math.Min(requestedTicks, completedTicks + progressIntervalTicks);
            world.AdvanceTo(nextTicks * SimulationWorld.FixedTimeStepSeconds);
            completedTicks = nextTicks;
            PrintProgress(world, completedTicks, requestedTicks, stopwatch.Elapsed);
        }

        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine("summary");
        PrintProgress(world, completedTicks, requestedTicks, stopwatch.Elapsed);
        Console.WriteLine($"status=duration-reached worldComplete={world.IsComplete}");
        var eventBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(world.Events));
        Console.WriteLine($"eventSha256={Convert.ToHexString(SHA256.HashData(eventBytes))}");
        return 0;
    }

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

    private static BenchmarkOptions ParseArguments(string[] args)
    {
        string? samplePath = null;
        var durationSeconds = DefaultDurationSeconds;
        var durationSpecified = false;
        var showHelp = false;
        var planned = false;

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

        return new BenchmarkOptions(samplePath ?? DefaultSampleRelativePath, durationSeconds, showHelp, planned);
    }

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
        Console.WriteLine($"Default sample: {DefaultSampleRelativePath}");
    }

    private sealed record BenchmarkOptions(string SamplePath, double DurationSeconds, bool ShowHelp, bool Planned);
}
