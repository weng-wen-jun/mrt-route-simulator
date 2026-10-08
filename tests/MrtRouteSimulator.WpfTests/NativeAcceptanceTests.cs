using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Engine;

/// <summary>
/// Short, off-screen coverage for the opt-in native acceptance measurement hook.
/// This deliberately exercises only a few seconds of the baseline project; long native
/// runs are performed separately against the real desktop window.
/// </summary>
internal static class NativeAcceptanceTests
{
    private const string StartMethodName = "StartNativeAcceptanceMeasurement";
    private const string StopMethodName = "StopNativeAcceptanceMeasurementAsync";
    private const string ActivePropertyName = "IsNativeAcceptanceMeasurementActive";
    private const string ReportPathPropertyName = "NativeAcceptanceReportPath";

    public static void Run(string root)
    {
        VerifyApiShape();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var samplePath = Path.Combine(root, "samples", "10-小型-三站完整拓樸基準範例.mrtsim.json");
        var document = TopologyProjectFormat.Deserialize(File.ReadAllText(samplePath));
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "MrtRouteSimulator.NativeAcceptanceTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var window = new MainWindow();
        try
        {
            VerifyDefaultDisabled(window, outputDirectory);
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));

            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("原生量測測試未建立 playback worker。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("原生量測測試未建立 playback timer。"))).Stop();

            var initial = WpfTestWait.LatestFrame(window);
            Require(initial.SimulationTimeSeconds == 0,
                "啟動原生量測前，baseline 應停在 0 秒。");
            var diagnosticsWasEnabled = window.PlaybackDiagnostics.IsEnabled;
            var workerDiagnosticsWasEnabled = worker.Diagnostics.IsEnabled;

            var reportPathBefore = GetReportPath(window);
            Require(string.IsNullOrWhiteSpace(reportPathBefore),
                "預設關閉時不應先建立原生量測報告路徑。");
            Require(!IsActive(window), "原生量測預設必須關閉。");

            var existingPath = Path.Combine(outputDirectory, "native-acceptance.json");
            File.WriteAllText(existingPath, "existing-report-must-survive");

            InvokeStart(window, outputDirectory);
            Require(IsActive(window), "StartNativeAcceptanceMeasurement 應啟用量測。");
            Require(SameFrame(initial, WpfTestWait.LatestFrame(window)),
                "Start 原生量測不可推進模擬或自動播放。");

            // Exercise the normal UI path: Play -> Pause -> Resume -> Reset.  The timer is
            // stopped so only the worker's normal commands can advance this short probe.
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(35));
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(worker.ResetAsync());
            Invoke(window, "UpdateV2PlaybackView", true);
            var resetFrame = WpfTestWait.LatestFrame(window);
            Require(resetFrame.SimulationTimeSeconds == 0,
                "Reset 後應回到 0 秒，且不得混入舊 generation。");
            Require(resetFrame.GenerationId == worker.GenerationId,
                "Reset frame 不得混用 worker generation。");

            var stopResult = InvokeStop(window);
            Require(!IsActive(window), "Stop 後量測應關閉。");
            Require(window.PlaybackDiagnostics.IsEnabled == diagnosticsWasEnabled
                    && worker.Diagnostics.IsEnabled == workerDiagnosticsWasEnabled,
                "Stop 後必須復原量測開始前的 diagnostics 狀態。");
            Require(WpfTestWait.Field(window, "_nativeAcceptanceProbeTimer") is null,
                "Stop 後 probe timer 必須清理。");
            Require(GetReportPath(window) is { Length: > 0 },
                "成功停止量測後應公開報告路徑。");
            VerifyReport(window, stopResult, existingPath, outputDirectory);

            // Stop is idempotent and must not throw or replace the completed report.
            var reportPath = GetReportPath(window)!;
            var reportBytes = File.ReadAllBytes(reportPath);
            InvokeStop(window);
            Require(!IsActive(window), "第二次 Stop 後量測仍應關閉。");
            Require(File.Exists(reportPath) && reportBytes.SequenceEqual(File.ReadAllBytes(reportPath)),
                "第二次 Stop 不得覆寫或損壞既有報告。");

            VerifyOutputFailureDoesNotBreakPlayback(window, worker, outputDirectory);
            VerifyBackgroundWriterFailure(window, worker, outputDirectory);
            VerifyShortCompletedRun(document, outputDirectory);
            VerifyFifthIdleResetLifecycle(document, outputDirectory);
            VerifyCollectorAckBoundaryAndWorkerReplacement(window, document, outputDirectory);
            VerifyCloseAbortsMeasurement(document, outputDirectory);
            Console.WriteLine("[通過] WPF 原生量測入口：預設關閉、短播放生命週期、report／generation／I/O guard");
        }
        finally
        {
            // Stop before Close makes the test also cover timer/writer cleanup.
            TryStop(window);
            WpfTestWait.Close(window);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void VerifyApiShape()
    {
        var type = typeof(MainWindow);
        var start = type.GetMethod(StartMethodName, BindingFlags.Instance | BindingFlags.Public);
        var stop = type.GetMethod(StopMethodName, BindingFlags.Instance | BindingFlags.Public);
        Require(start is not null,
            $"MainWindow 缺少公開 API {StartMethodName}。");
        Require(stop is not null,
            $"MainWindow 缺少公開 API {StopMethodName}。");
        Require(start!.ReturnType == typeof(void)
                && start.GetParameters() is [{ ParameterType: var parameterType, IsOptional: true }]
                && parameterType == typeof(string),
            "StartNativeAcceptanceMeasurement 必須是 void StartNativeAcceptanceMeasurement(string? outputDirectory = null)。");
        Require(typeof(Task).IsAssignableFrom(stop!.ReturnType),
            "StopNativeAcceptanceMeasurementAsync 必須回傳 Task。");
        Require(type.GetProperty(ActivePropertyName, BindingFlags.Instance | BindingFlags.Public) is
                { PropertyType: { } activeType } && activeType == typeof(bool),
            $"MainWindow 缺少公開屬性 {ActivePropertyName}。");
        Require(type.GetProperty(ReportPathPropertyName, BindingFlags.Instance | BindingFlags.Public) is
                { PropertyType: { } reportType } && reportType == typeof(string),
            $"MainWindow 缺少公開屬性 {ReportPathPropertyName}。");
    }

    private static void VerifyDefaultDisabled(MainWindow window, string outputDirectory)
    {
        Require(!IsActive(window), "新 MainWindow 不應自動啟用原生量測。");
        Require(string.IsNullOrWhiteSpace(GetReportPath(window)),
            "新 MainWindow 不應自動建立原生量測報告。");
        Require(!Directory.EnumerateFileSystemEntries(outputDirectory).Any(),
            "僅建立 temp directory 不應產生日誌檔案。");
    }

    private static void VerifyReport(
        MainWindow window,
        object? stopResult,
        string existingPath,
        string outputDirectory)
    {
        var path = GetReportPath(window)
            ?? ExtractPath(stopResult)
            ?? throw new InvalidOperationException("Stop 未提供 NativeAcceptanceReportPath。");
        Require(File.Exists(path), $"原生量測報告不存在：{path}");
        Require(File.ReadAllText(existingPath) == "existing-report-must-survive",
            "existing report sentinel 不應被覆寫。");

        var records = ReadJsonLines(path);
        Require(records.Length >= 2, "原生量測 JSONL 至少應包含 sessionStarted／sessionStopped。");
        Require(records.All(record => record.ValueKind == JsonValueKind.Object),
            "原生量測 JSONL 每行根節點必須是 object。");
        Require(records.Any(record => HasAnyProperty(record, "type")
                && string.Equals(GetString(record, "type"), "sessionStarted", StringComparison.Ordinal)),
            "report 缺少 sessionStarted record。");
        Require(records.Any(record => HasAnyProperty(record, "type")
                && string.Equals(GetString(record, "type"), "sessionStopped", StringComparison.Ordinal)),
            "report 缺少 sessionStopped record。");
        Require(records.Any(record => HasAnyProperty(record, "runtime", "Runtime")),
            "report 缺少 Runtime metrics。");
        Require(records.Any(record => HasAnyProperty(record, "diagnostics", "Diagnostics")),
            "report 缺少合併後的 worker／UI diagnostics。");
        var diagnostics = records.Select(record => FindObject(record, "diagnostics", "Diagnostics"))
            .FirstOrDefault(value => value is not null);
        Require(diagnostics is { } diagnosticsObject
                && (HasAnyProperty(diagnosticsObject, "Timings", "timings")
                    || HasAnyProperty(diagnosticsObject, "Counters", "counters")),
            "合併後 diagnostics 應包含 timing 或 counter metrics。");
        Require(records.Any(record => HasAnyProperty(record, "activeTab", "tabs", "Tabs", "tabInfo")),
            "report 缺少 tab 資訊。");
        Require(records.Any(record => HasAnyProperty(record, "viewport", "dpi", "Dpi", "dpiInfo")),
            "report 缺少 DPI／viewport 資訊。");
        Require(records.Any(record => HasAnyProperty(record, "workerGenerationId", "generation", "Generation", "generationId")),
            "report 缺少 worker generation 資訊。");
        VerifyResumeBaseline(records);

        foreach (var record in records)
        {
            var runtime = FindObject(record, "runtime", "Runtime");
            if (runtime is not { } runtimeObject)
            {
                continue;
            }

            VerifyFiniteNumbers(runtimeObject,
                "gc0Collections", "gc1Collections", "gc2Collections", "gen0Collections",
                "gen1Collections", "gen2Collections", "gen0", "gen1", "gen2",
                "allocatedBytes", "allocationBytes", "managedHeapBytes", "managedBytes",
                "privateBytes", "workingSetBytes");
        }

        var eventPath = records
            .Select(record => GetString(record, "eventPath"))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (eventPath is not null)
        {
            Require(File.Exists(eventPath), "原生量測 event writer 應在 Stop 後完成輸出。");
        }

        var generationRecord = records.FirstOrDefault(record =>
            HasAnyProperty(record, "workerGenerationId", "generation", "Generation", "generationId"));
        var generation = FindValue(generationRecord, "workerGenerationId", "generation", "Generation", "generationId");
        Require(generation is not null && (generation.Value.ValueKind is JsonValueKind.String
                or JsonValueKind.Object or JsonValueKind.Null),
            "report generation 必須可識別。");

        var reportDirectory = Path.GetFullPath(Path.GetDirectoryName(path)!);
        Require(reportDirectory.StartsWith(Path.GetFullPath(outputDirectory), StringComparison.OrdinalIgnoreCase),
            "報告必須輸出至測試指定的唯一 temp directory。");
    }

    private static void VerifyOutputFailureDoesNotBreakPlayback(
        MainWindow window,
        SimulationPlaybackWorker worker,
        string outputDirectory)
    {
        var blockedPath = Path.Combine(outputDirectory, "output-is-a-file");
        File.WriteAllText(blockedPath, "not-a-directory");
        object? startResult = null;
        Exception? failure = null;
        try
        {
            startResult = InvokeStart(window, blockedPath);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Require(failure is null,
            $"輸出 I/O 失敗不應由 Start 直接拋出：{failure?.GetBaseException().Message}");
        Require(!IsActive(window), "輸出 I/O 失敗後只應停用量測。");

        WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(0.2));
        Invoke(window, "UpdateV2PlaybackView", true);
        Require(WpfTestWait.LatestFrame(window).GenerationId == worker.GenerationId,
            "輸出 I/O 失敗不可破壞正常 playback worker。");
        _ = startResult;
    }

    private static void VerifyBackgroundWriterFailure(
        MainWindow window,
        SimulationPlaybackWorker worker,
        string outputDirectory)
    {
        var failureDirectory = Path.Combine(outputDirectory, "writer-failure");
        Directory.CreateDirectory(failureDirectory);
        InvokeStart(window, failureDirectory);
        Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
        WpfTestWait.Wait(Task.Delay(60));

        var session = WpfTestWait.Field(window, "_nativeAcceptanceSession")
            ?? throw new InvalidOperationException("找不到 active native acceptance session。");
        var writer = session.GetType().GetProperty("ReportWriter",
            BindingFlags.Instance | BindingFlags.Public)?.GetValue(session)
            ?? throw new InvalidOperationException("找不到 report writer。");
        var run = session.GetType().GetProperty("Run",
            BindingFlags.Instance | BindingFlags.Public)?.GetValue(session)
            ?? throw new InvalidOperationException("找不到 active measurement run。");
        var nextCheckpoint = run.GetType().GetProperty("NextCheckpointWallSeconds",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("找不到 checkpoint horizon。");
        nextCheckpoint.SetValue(run, 0d);
        var stream = writer.GetType().GetField("_stream",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(writer) as FileStream
            ?? throw new InvalidOperationException("找不到 report writer stream。");
        stream.Dispose();

        var generation = worker.GenerationId;
        WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(0.2));
        Invoke(window, "UpdateV2PlaybackView", true);
        Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
        WpfTestWait.Wait(Task.Delay(200));
        Require(!worker.Completion.IsFaulted,
            "背景 JSONL I/O 失敗不可讓 playback worker fault。");
        Require(WpfTestWait.LatestFrame(window).GenerationId == generation,
            "背景 JSONL I/O 失敗不可混用 generation。");
        Require(!IsActive(window), "背景 JSONL I/O 失敗後應只停用量測。");
    }

    private static void VerifyShortCompletedRun(
        TopologyProjectDocument document,
        string outputDirectory)
    {
        var completeDirectory = Path.Combine(outputDirectory, "completed");
        Directory.CreateDirectory(completeDirectory);
        var window = new MainWindow();
        var closed = false;
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("completed case 未建立 worker。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("completed case 未建立 timer。"))).Stop();
            InvokeStart(window, completeDirectory);
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("completed case timer disappeared."))).Stop();

            var complete = false;
            foreach (var target in new[] { 600d, 1200d, 2400d, 3600d })
            {
                WpfTestWait.Wait(worker.AdvanceToSimulationTimeAsync(target));
                Invoke(window, "PlaybackTimer_TickCore");
                complete = WpfTestWait.LatestFrame(window).IsComplete;
                if (complete) break;
            }

            Require(complete, "短 completed case 未在 bounded simulation horizon 完成。");
            WpfTestWait.Wait(Task.Delay(100));
            var reportPath = GetReportPath(window)
                ?? throw new InvalidOperationException("completed case 缺少報告路徑。");
            InvokeStop(window);
            VerifyCompletedRunReport(reportPath);
            TryStop(window);
            WpfTestWait.Close(window);
            closed = true;
        }
        finally
        {
            if (!closed && !window.Dispatcher.HasShutdownStarted)
            {
                TryStop(window);
                WpfTestWait.Close(window);
            }
        }
    }

    private static void VerifyFifthIdleResetLifecycle(
        TopologyProjectDocument document,
        string outputDirectory)
    {
        var lifecycleDirectory = Path.Combine(outputDirectory, "fifth-idle-reset");
        Directory.CreateDirectory(lifecycleDirectory);
        var window = new MainWindow();
        var closed = false;
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("fifth idle case 未建立 worker。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("fifth idle case 未建立 timer。"))).Stop();

            InvokeStart(window, lifecycleDirectory);
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));

            var session = WpfTestWait.Field(window, "_nativeAcceptanceSession")
                ?? throw new InvalidOperationException("fifth idle case 找不到 active session。" );
            var run = session.GetType().GetProperty("Run",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(session)
                ?? throw new InvalidOperationException("fifth idle case 找不到 active run。" );
            SetProperty(session, "CompletedRuns", 5);
            SetProperty(run, "Completed", true);
            SetProperty(run, "IsPlaying", false);
            SetProperty(run, "IdleCheckpointDueTimestamp", Stopwatch.GetTimestamp() - 1);
            SetProperty(run, "IdleCheckpointWritten", false);

            Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
            WpfTestWait.Wait(Task.Delay(120));
            Require(IsActive(window),
                "第五輪 10s idle 應保留 collector，等待正常 Reset 的 afterReset checkpoint，不得先清除 session。" );

            Invoke(window, "NativeAcceptanceAbortForLifecycle", "playback-reset");
            WpfTestWait.Wait(worker.ResetAsync());
            Invoke(window, "UpdateV2PlaybackView", true);
            Require(WpfTestWait.LatestFrame(window).SimulationTimeSeconds == 0,
                "第五輪 idle→Reset case 必須經過真正 worker Reset 並回到 0 秒。" );
            InvokePublic(window, "NativeAcceptanceAfterResetApplied");
            WpfTestWait.Wait(Task.Delay(160));
            Require(!IsActive(window),
                "第五輪 idle pending 在 afterReset checkpoint 寫入後應以既有 Stop 收尾。" );

            var reportPath = GetReportPath(window)
                ?? throw new InvalidOperationException("fifth idle case 缺少報告路徑。" );
            var records = ReadJsonLines(reportPath);
            var idleIndex = Array.FindIndex(records, record => string.Equals(GetString(record, "reason"),
                "10sIdleAfterCompletion", StringComparison.Ordinal));
            var afterResetIndex = Array.FindIndex(records, record => string.Equals(GetString(record, "reason"),
                "afterReset", StringComparison.Ordinal));
            var stoppedIndex = Array.FindIndex(records, record => string.Equals(GetString(record, "type"),
                "sessionStopped", StringComparison.Ordinal));
            Require(idleIndex >= 0, "第五輪 idle case 缺少 10sIdleAfterCompletion checkpoint。" );
            Require(afterResetIndex > idleIndex,
                "第五輪 idle→Reset case 的 afterReset 必須在 idle checkpoint 之後。" );
            Require(stoppedIndex > afterResetIndex,
                "第五輪 idle→Reset case 必須在 afterReset 之後才 sessionStopped。" );
            Require(records.Count(record => string.Equals(GetString(record, "type"),
                    "sessionStopped", StringComparison.Ordinal)) == 1,
                "第五輪 idle→Reset case 應只寫入一次 sessionStopped。" );
            Require(records.Where(record => string.Equals(GetString(record, "type"),
                        "runArmed", StringComparison.Ordinal))
                    .Select(record => GetRequiredInt64(record, "run"))
                    .All(runNumber => runNumber <= 5),
                "第五輪 idle→Reset case 不得建立第六個 measured run。" );

            TryStop(window);
            WpfTestWait.Close(window);
            closed = true;
        }
        finally
        {
            if (!closed && !window.Dispatcher.HasShutdownStarted)
            {
                TryStop(window);
                WpfTestWait.Close(window);
            }
        }

        VerifyFifthIdleNextPlayDoesNotArmSixth(document, outputDirectory);
        VerifyEarlyResetSkipsIdleForFourthRun(document, outputDirectory);
    }

    private static void VerifyFifthIdleNextPlayDoesNotArmSixth(
        TopologyProjectDocument document,
        string outputDirectory)
    {
        var lifecycleDirectory = Path.Combine(outputDirectory, "fifth-idle-next-play");
        Directory.CreateDirectory(lifecycleDirectory);
        var window = new MainWindow();
        var closed = false;
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("fifth next-play case 未建立 worker。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("fifth next-play case 未建立 timer。"))).Stop();

            InvokeStart(window, lifecycleDirectory);
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            var session = WpfTestWait.Field(window, "_nativeAcceptanceSession")
                ?? throw new InvalidOperationException("fifth next-play case 找不到 active session。" );
            var run = session.GetType().GetProperty("Run",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(session)
                ?? throw new InvalidOperationException("fifth next-play case 找不到 active run。" );
            SetProperty(session, "CompletedRuns", 5);
            SetProperty(run, "Completed", true);
            SetProperty(run, "IsPlaying", false);
            SetProperty(run, "IdleCheckpointDueTimestamp", Stopwatch.GetTimestamp() - 1);
            SetProperty(run, "IdleCheckpointWritten", false);

            Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
            WpfTestWait.Wait(Task.Delay(100));
            Require(IsActive(window),
                "第五輪 idle 後，下一次 Play 前 collector 必須仍可完成 lifecycle 收尾。" );
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(160));
            Require(!IsActive(window),
                "第五輪 idle 後 Play 應收尾 collector，不得留下 active session。" );

            var reportPath = GetReportPath(window)
                ?? throw new InvalidOperationException("fifth next-play case 缺少報告路徑。" );
            var records = ReadJsonLines(reportPath);
            Require(records.Where(record => string.Equals(GetString(record, "type"),
                        "runArmed", StringComparison.Ordinal))
                    .Select(record => GetRequiredInt64(record, "run"))
                    .All(runNumber => runNumber <= 5),
                "第五輪 idle 後 Play 不得 arm 第六個 measured run。" );
            Require(records.Count(record => string.Equals(GetString(record, "type"),
                    "sessionStopped", StringComparison.Ordinal)) == 1,
                "第五輪 idle 後 Play 應只寫入一次 sessionStopped。" );

            TryStop(window);
            WpfTestWait.Close(window);
            closed = true;
        }
        finally
        {
            if (!closed && !window.Dispatcher.HasShutdownStarted)
            {
                TryStop(window);
                WpfTestWait.Close(window);
            }
        }
    }

    private static void VerifyEarlyResetSkipsIdleForFourthRun(
        TopologyProjectDocument document,
        string outputDirectory)
    {
        var lifecycleDirectory = Path.Combine(outputDirectory, "fourth-early-reset");
        Directory.CreateDirectory(lifecycleDirectory);
        var window = new MainWindow();
        var closed = false;
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            var worker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
                ?? throw new InvalidOperationException("fourth early-reset case 未建立 worker。"));
            WpfTestWait.Wait(worker.Ready);
            ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
                ?? throw new InvalidOperationException("fourth early-reset case 未建立 timer。"))).Stop();

            InvokeStart(window, lifecycleDirectory);
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(80));
            Invoke(window, "Pause_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            var session = WpfTestWait.Field(window, "_nativeAcceptanceSession")
                ?? throw new InvalidOperationException("fourth early-reset case 找不到 active session。" );
            var run = session.GetType().GetProperty("Run",
                BindingFlags.Instance | BindingFlags.Public)?.GetValue(session)
                ?? throw new InvalidOperationException("fourth early-reset case 找不到 active run。" );
            SetProperty(session, "CompletedRuns", 4);
            SetProperty(run, "Completed", true);
            SetProperty(run, "IsPlaying", false);
            SetProperty(run, "IdleCheckpointDueTimestamp", Stopwatch.GetTimestamp() - 1);
            SetProperty(run, "IdleCheckpointWritten", false);

            // Match the normal reset ordering: lifecycle boundary, real worker reset, then
            // the post-reset notification after the first reset frame has been applied.
            Invoke(window, "NativeAcceptanceAbortForLifecycle", "playback-reset");
            var resetTask = worker.ResetAsync();
            // The probe timer may run in this interval while ResetAsync is pending. Explicitly
            // pump that boundary before afterReset so a past-due idle horizon cannot be hidden
            // by the later presentation/cache checkpoint.
            Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
            WpfTestWait.Wait(resetTask);
            Invoke(window, "UpdateV2PlaybackView", true);
            Require(WpfTestWait.LatestFrame(window).SimulationTimeSeconds == 0,
                "第四輪 early Reset 必須經過真正 worker Reset 並回到 0 秒。" );
            InvokePublic(window, "NativeAcceptanceAfterResetApplied");
            Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
            WpfTestWait.Wait(Task.Delay(80));

            var resetRun = WpfTestWait.Field(window, "_nativeAcceptanceSession")
                ?.GetType().GetProperty("Run", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(WpfTestWait.Field(window, "_nativeAcceptanceSession"));
            Require(resetRun is not null, "第四輪 early Reset 後應仍保留 collector 供下一輪 Play。" );
            Invoke(window, "Play_Click", new Button(), new RoutedEventArgs(Button.ClickEvent));
            WpfTestWait.Wait(Task.Delay(120));
            Require(IsActive(window), "第四輪 early Reset 後下一輪 Play 應可正常建立 measured run。" );
            InvokeStop(window);

            var reportPath = GetReportPath(window)
                ?? throw new InvalidOperationException("fourth early-reset case 缺少報告路徑。" );
            var records = ReadJsonLines(reportPath);
            Require(records.Any(record => string.Equals(GetString(record, "reason"),
                    "afterReset", StringComparison.Ordinal)),
                "第四輪 early Reset case 缺少 afterReset checkpoint。" );
            Require(!records.Any(record => string.Equals(GetString(record, "reason"),
                    "10sIdleAfterCompletion", StringComparison.Ordinal)),
                "第四輪 early Reset 不得把 reset 後 frame/cache 假標成 10sIdleAfterCompletion。" );

            WpfTestWait.Close(window);
            closed = true;
        }
        finally
        {
            if (!closed && !window.Dispatcher.HasShutdownStarted)
            {
                TryStop(window);
                WpfTestWait.Close(window);
            }
        }
    }

    private static void VerifyResumeBaseline(JsonElement[] records)
    {
        var runStarted = records.Where(record => string.Equals(
                GetString(record, "type"), "runStarted", StringComparison.Ordinal))
            .ToArray();
        Require(runStarted.Length == 1,
            $"Play/Pause/Resume 生命週期應只有一個 runStarted，實際 {runStarted.Length}。");
        Require(records.Any(record => string.Equals(
                GetString(record, "type"), "runResumed", StringComparison.Ordinal)),
            "Resume 應寫入 runResumed record。");

        var baseline = FindObject(runStarted[0], "runtimeBaseline")
            ?? throw new InvalidOperationException("runStarted 缺少 runtime baseline。");
        var baselineUi = FindObject(baseline, "Ui", "ui")
            ?? throw new InvalidOperationException("runtime baseline 缺少 UI snapshot。");
        var runStopped = records.FirstOrDefault(record =>
            string.Equals(GetString(record, "type"), "runAborted", StringComparison.Ordinal)
            || string.Equals(GetString(record, "type"), "runCompleted", StringComparison.Ordinal));
        var runtime = FindObject(runStopped, "runtime", "Runtime")
            ?? throw new InvalidOperationException("run terminal record 缺少 runtime delta。");
        var runtimeUi = FindObject(runtime, "Ui", "ui")
            ?? throw new InvalidOperationException("run terminal runtime 缺少 UI delta。");
        var start = FindObject(runtimeUi, "start")
            ?? throw new InvalidOperationException("run terminal runtime 缺少 UI start snapshot。");
        Require(GetRequiredInt64(start, "allocatedBytes") == GetRequiredInt64(baselineUi, "AllocatedBytes"),
            "Resume 不得重設原始 runtime allocation baseline。");
    }

    private static void VerifyCompletedRunReport(string reportPath)
    {
        var records = ReadJsonLines(reportPath);
        var run = records.FirstOrDefault(record =>
            string.Equals(GetString(record, "type"), "runCompleted", StringComparison.Ordinal));
        Require(run.ValueKind == JsonValueKind.Object,
            "bounded completed case 必須寫入 runCompleted record。");

        var dispatch = GetRequiredInt64(run, "playDispatchStopwatchTimestamp");
        var acknowledged = GetRequiredInt64(run, "playAcknowledgedStopwatchTimestamp");
        var published = GetRequiredInt64(run, "completionFramePublishedStopwatchTimestamp");
        var observed = GetRequiredInt64(run, "completionObservedStopwatchTimestamp");
        Require(dispatch <= acknowledged && acknowledged <= published && published <= observed,
            "runCompleted dispatch／ack／publish／observe timestamp 順序不合法。");
        Require(GetRequiredDouble(run, "activeWallSecondsFromPlayAcknowledgement") > 0,
            "runCompleted active wall time 必須為正值。");

        var runtime = FindObject(run, "runtime", "Runtime")
            ?? throw new InvalidOperationException("runCompleted 缺少 runtime。");
        var ui = FindObject(runtime, "Ui", "ui")
            ?? throw new InvalidOperationException("runCompleted runtime 缺少 UI metrics。");
        var delta = FindObject(ui, "delta")
            ?? throw new InvalidOperationException("runCompleted runtime 缺少 allocation delta。");
        var current = FindObject(ui, "current")
            ?? throw new InvalidOperationException("runCompleted runtime 缺少 current snapshot。");
        Require(GetRequiredInt64(delta, "allocatedBytes") >= 0
                && GetRequiredInt64(delta, "gen0") >= 0
                && GetRequiredInt64(delta, "gen1") >= 0
                && GetRequiredInt64(delta, "gen2") >= 0,
            "runCompleted allocation／GC delta 必須是有效非負值。");
        Require(GetRequiredInt64(current, "managedBytes") > 0
                && GetRequiredInt64(current, "workingSetBytes") > 0,
            "runCompleted managed heap／working set 必須是正值。");

        var diagnostics = FindObject(run, "diagnostics", "Diagnostics")
            ?? throw new InvalidOperationException("runCompleted 缺少 merged diagnostics。");
        var timings = FindObject(diagnostics, "Timings", "timings")
            ?? throw new InvalidOperationException("merged diagnostics 缺少 timing metrics。");
        var counters = FindObject(diagnostics, "Counters", "counters")
            ?? throw new InvalidOperationException("merged diagnostics 缺少 counter metrics。");
        Require(HasDirectProperty(timings, PlaybackDiagnosticMetricNames.ApplyPlaybackFrame)
                || HasDirectProperty(timings, "ApplyPlaybackFrame"),
            "merged diagnostics 應包含 UI ApplyPlaybackFrame timing。");
        Require(HasDirectProperty(counters, PlaybackDiagnosticMetricNames.WorkerFramePublishedCount)
                || HasDirectProperty(counters, PlaybackDiagnosticMetricNames.WorkerFrameConsumedCount),
            "merged diagnostics 應包含 worker frame count。");
    }

    private static void VerifyCollectorAckBoundaryAndWorkerReplacement(
        MainWindow window,
        TopologyProjectDocument document,
        string outputDirectory)
    {
        var replacementDirectory = Path.Combine(outputDirectory, "replacement");
        Directory.CreateDirectory(replacementDirectory);
        InvokeStart(window, replacementDirectory);
        var oldWorker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("找不到原 worker。"));
        ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
            ?? throw new InvalidOperationException("換 worker case 找不到 playback timer。"))).Stop();
        ((DispatcherTimer)(WpfTestWait.Field(window, "_nativeAcceptanceProbeTimer")
            ?? throw new InvalidOperationException("換 worker case 找不到 native acceptance probe timer。"))).Stop();

        // Reproduce the real Play ordering without depending on an async-void timing window:
        // BeforePlayDispatch arms the run, while the worker command acknowledgement has not
        // arrived yet. A probe in this interval must not classify the not-yet-bound worker as
        // a replacement, because WorkerAtStart/WorkerGenerationId are intentionally assigned
        // only by AfterPlayAcknowledged.
        Invoke(window, "NativeAcceptanceBeforePlayDispatch");
        var session = WpfTestWait.Field(window, "_nativeAcceptanceSession")
            ?? throw new InvalidOperationException("ack boundary case 找不到 active session。");
        var pendingRun = GetAcceptanceRun(session)
            ?? throw new InvalidOperationException("BeforePlayDispatch 未建立 acceptance run。");
        Require(GetLongProperty(pendingRun, "PlayAcknowledgedTimestamp") == 0,
            "BeforePlayDispatch 後必須仍處於 worker acknowledgement 前。");
        Require(GetProperty(pendingRun, "WorkerAtStart") is null
                && GetNullableGuidProperty(pendingRun, "WorkerGenerationId") is null,
            "ack 前不得先綁定 worker identity。");

        Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
        Require(!GetBoolProperty(pendingRun, "Completed"),
            "NativeAcceptanceProbeTick 不得在 Play acknowledgement 前將 runAborted。");
        Require(GetIntProperty(session, "CompletedRuns") == 0,
            "Play acknowledgement 前 probe 不得計入 aborted completed run。");

        // Bind the real worker only after the explicit acknowledgement boundary. The following
        // project reconfiguration creates and installs a real SimulationPlaybackWorker; the
        // replacement probe must still abort the now-bound run.
        WpfTestWait.Wait(oldWorker.PlayAsync(1));
        Invoke(window, "NativeAcceptanceAfterPlayAcknowledged");
        var acknowledgedRun = GetAcceptanceRun(session)
            ?? throw new InvalidOperationException("AfterPlayAcknowledged 後找不到 acceptance run。");
        Require(GetLongProperty(acknowledgedRun, "PlayAcknowledgedTimestamp") > 0,
            "AfterPlayAcknowledged 必須建立 acknowledgement timestamp。");
        Require(ReferenceEquals(GetProperty(acknowledgedRun, "WorkerAtStart"), oldWorker),
            "AfterPlayAcknowledged 必須綁定當下的實際 playback worker。");
        Require(GetNullableGuidProperty(acknowledgedRun, "WorkerGenerationId") == oldWorker.GenerationId,
            "AfterPlayAcknowledged 必須綁定當下 worker generation。");

        var oldGeneration = oldWorker.GenerationId;
        WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
            window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
        var newWorker = (SimulationPlaybackWorker)(WpfTestWait.Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("換 worker 後找不到新 worker。"));
        WpfTestWait.Wait(newWorker.Ready);
        ((DispatcherTimer)(WpfTestWait.Field(window, "_playbackTimer")
            ?? throw new InvalidOperationException("換 worker 後找不到 timer。"))).Stop();
        Require(newWorker.GenerationId != oldGeneration,
            "換 worker 必須建立新的 generation。");
        var frame = WpfTestWait.LatestFrame(window);
        Require(frame.GenerationId == newWorker.GenerationId,
            "換 worker 後 UI frame 不得混入舊 generation。");
        Require(!GetBoolProperty(acknowledgedRun, "Completed"),
            "實際 worker 替換前，ack 後 run 不應提前結束。");
        Invoke(window, "NativeAcceptanceProbeTick", new object(), EventArgs.Empty);
        Require(GetBoolProperty(acknowledgedRun, "Completed"),
            "ack 後實際 worker 更換必須由 probe 產生 runAborted。");
        InvokeStop(window);
        var reportPath = GetReportPath(window)
            ?? throw new InvalidOperationException("換 worker 後缺少量測報告。");
        var records = ReadJsonLines(reportPath);
        Require(records.Any(record => string.Equals(GetString(record, "type"),
                    "runAborted", StringComparison.Ordinal)
                && string.Equals(GetString(record, "reason"),
                    "playback-worker-replaced", StringComparison.Ordinal)),
            "ack 後換 worker 時 active run 應以 playback-worker-replaced abort record 結束。");
    }

    private static object? GetProperty(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);

    private static int GetIntProperty(object target, string name) =>
        GetProperty(target, name) is int value
            ? value
            : throw new InvalidOperationException($"找不到整數屬性 {name}。");

    private static long GetLongProperty(object target, string name) =>
        GetProperty(target, name) is long value
            ? value
            : throw new InvalidOperationException($"找不到長整數屬性 {name}。");

    private static bool GetBoolProperty(object target, string name) =>
        GetProperty(target, name) is bool value
            ? value
            : throw new InvalidOperationException($"找不到布林屬性 {name}。");

    private static Guid? GetNullableGuidProperty(object target, string name) =>
        GetProperty(target, name) is Guid value ? value : null;

    private static object? GetAcceptanceRun(object session) =>
        GetProperty(session, "Run");

    private static void VerifyCloseAbortsMeasurement(
        TopologyProjectDocument document,
        string outputDirectory)
    {
        var closeDirectory = Path.Combine(outputDirectory, "close");
        Directory.CreateDirectory(closeDirectory);
        var window = new MainWindow();
        string? reportPath = null;
        var closed = false;
        var closedEvent = false;
        window.Closed += (_, _) => closedEvent = true;
        try
        {
            WpfTestWait.Wait(WpfTestWait.InvokeOnUiAsync(
                window, "ConfigureTopologyProjectForPlaybackAsync", document, true));
            InvokeStart(window, closeDirectory);
            reportPath = GetReportPath(window);
            // Use the real Closing event; WpfTestWait.Close intentionally bypasses it for
            // ordinary test cleanup and would not exercise NativeAcceptanceAbortForLifecycle.
            window.Close();
            WpfTestWait.Wait(Task.Delay(100));
            closed = closedEvent;
            Require(reportPath is not null && File.Exists(reportPath),
                "關閉視窗時應完成原生量測 writer 清理。");
            Require(File.ReadLines(reportPath!).Any(line =>
                    line.Contains("sessionStopped", StringComparison.Ordinal)
                    || line.Contains("lifecycleBoundary", StringComparison.Ordinal)),
                "關閉視窗時應留下 lifecycle abort／stop record。");
        }
        finally
        {
            if (!closed && !window.Dispatcher.HasShutdownStarted)
            {
                TryStop(window);
                WpfTestWait.Close(window);
            }
        }
    }

    private static object? InvokeStart(MainWindow window, string outputDirectory) =>
        InvokePublic(window, StartMethodName, outputDirectory);

    private static object? InvokeStop(MainWindow window)
    {
        var result = InvokePublic(window, StopMethodName);
        if (result is Task task)
        {
            WpfTestWait.Wait(task);
            return TryGetTaskResult(task);
        }

        return result;
    }

    private static void TryStop(MainWindow window)
    {
        try
        {
            if (IsActive(window))
            {
                _ = InvokeStop(window);
            }
        }
        catch
        {
            // Preserve the original test failure; Close still owns final WPF cleanup.
        }
    }

    private static bool IsActive(MainWindow window) =>
        (bool)(window.GetType().GetProperty(ActivePropertyName,
            BindingFlags.Instance | BindingFlags.Public)?.GetValue(window)
            ?? throw new InvalidOperationException($"無法讀取 {ActivePropertyName}。"));

    private static string? GetReportPath(MainWindow window) =>
        window.GetType().GetProperty(ReportPathPropertyName,
            BindingFlags.Instance | BindingFlags.Public)?.GetValue(window) as string;

    private static object? InvokePublic(MainWindow window, string method, params object?[] args)
    {
        var methodInfo = window.GetType().GetMethod(method,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"找不到 MainWindow 公開方法 {method}。");
        try
        {
            return methodInfo.Invoke(window, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static void SetProperty(object target, string name, object? value)
    {
        var property = target.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException($"找不到可設定屬性 {name}。" );
        property.SetValue(target, value);
    }

    private static object? TryGetTaskResult(Task task)
    {
        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public);
        return resultProperty?.GetValue(task);
    }

    private static string? ExtractPath(object? value) => value switch
    {
        string path => path,
        FileInfo file => file.FullName,
        _ => value?.GetType().GetProperty("ReportPath", BindingFlags.Instance | BindingFlags.Public)?.GetValue(value) as string
    };

    private static bool SameFrame(PlaybackFrame first, PlaybackFrame second) =>
        first.GenerationId == second.GenerationId
        && first.Sequence == second.Sequence
        && first.SimulationTimeSeconds == second.SimulationTimeSeconds;

    private static JsonElement[] ReadJsonLines(string path) => File.ReadLines(path)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        })
        .ToArray();

    private static long GetRequiredInt64(JsonElement element, string name)
    {
        var value = FindValue(element, name);
        long number = 0;
        if (value is not { ValueKind: JsonValueKind.Number }
            || !value.Value.TryGetInt64(out number))
        {
            throw new InvalidOperationException($"JSON record 缺少整數欄位 {name}。");
        }

        return number;
    }

    private static double GetRequiredDouble(JsonElement element, string name)
    {
        var value = FindValue(element, name);
        double number = 0;
        if (value is not { ValueKind: JsonValueKind.Number }
            || !value.Value.TryGetDouble(out number)
            || double.IsNaN(number) || double.IsInfinity(number))
        {
            throw new InvalidOperationException($"JSON record 缺少有限數值欄位 {name}。");
        }

        return number;
    }

    private static bool HasDirectProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Any(property =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static JsonElement? FindValue(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                var nested = FindValue(property.Value, names);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    private static JsonElement? FindObject(JsonElement element, params string[] names)
    {
        var value = FindValue(element, names);
        return value is { ValueKind: JsonValueKind.Object } ? value : null;
    }

    private static bool HasAnyProperty(JsonElement element, params string[] names) =>
        FindValue(element, names) is not null;

    private static void VerifyFiniteNumbers(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = FindValue(element, name);
            if (value is not { } number || number.ValueKind is JsonValueKind.Null)
            {
                continue;
            }

            if (number.ValueKind == JsonValueKind.Number)
            {
                Require(number.TryGetDouble(out var numeric) &&
                        !double.IsNaN(numeric) && !double.IsInfinity(numeric),
                    $"report {name} 必須是有效有限數值。");
            }
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        var value = FindValue(element, name);
        return value is { ValueKind: JsonValueKind.String } stringValue
            ? stringValue.GetString()
            : null;
    }

    private static object? Invoke(MainWindow window, string method, params object[] args) =>
        WpfTestWait.Invoke(window, method, args);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
