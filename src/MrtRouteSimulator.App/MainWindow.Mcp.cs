using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.Automation;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.App;

public partial class MainWindow
{
    private DesktopBridgeServer? _mcpBridge;
    private WorkspaceFiles? _mcpFiles;

    /// <summary>Opt-in local bridge. Normal startup and WPF test windows have no listener.</summary>
    private void InitializeMcpBridgeFromArguments()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("--mcp-bridge", StringComparer.Ordinal)) return;
        var index = Array.IndexOf(args, "--mcp-workspace-root");
        if (index < 0 || index + 1 >= args.Length)
        {
            StatusTextBlock.Text = "MCP 未啟用：請指定 --mcp-workspace-root。";
            return;
        }
        EnableMcpBridge(args[index + 1]);
    }

    public void EnableMcpBridge(string workspaceRoot)
    {
        Dispatcher.VerifyAccess();
        if (_mcpBridge is not null) throw new InvalidOperationException("MCP bridge 已啟用。");
        _mcpFiles = new WorkspaceFiles(workspaceRoot);
        _mcpBridge = new DesktopBridgeServer((request, cancellationToken) =>
            Dispatcher.InvokeAsync(() => ExecuteMcpCommandAsync(request), System.Windows.Threading.DispatcherPriority.Normal, cancellationToken).Task.Unwrap());
        Closed += async (_, _) =>
        {
            var bridge = _mcpBridge;
            _mcpBridge = null;
            if (bridge is not null) await bridge.DisposeAsync();
        };
        StatusTextBlock.Text = $"已啟用本機 MCP 操作（PID {Environment.ProcessId}）。";
    }

    /// <summary>Shared UI-thread façade, also exercised by WPF regression tests.</summary>
    public async Task<object> ExecuteMcpCommandAsync(DesktopRequest request)
    {
        Dispatcher.VerifyAccess();
        if (_closeAfterPlaybackShutdown) throw new InvalidOperationException("視窗正在關閉，拒絕新的 MCP 操作。");
        var files = _mcpFiles ?? throw new InvalidOperationException("請先啟用 MCP bridge。");
        var args = request.Arguments;
        if (args.ValueKind != JsonValueKind.Object) throw new ArgumentException("參數必須是 JSON 物件。");
        var requestedRoot = Text(args, "workspaceRoot");
        if (!string.Equals(Path.GetFullPath(requestedRoot), files.Root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("MCP 與桌面 bridge 的工作目錄不一致。");
        if (!IsEnabled || OwnedWindows.Cast<Window>().Any(window => window.IsVisible))
            throw new InvalidOperationException("目前正在載入或開啟編輯對話框，請完成後再呼叫 MCP。");
        switch (request.Command)
        {
            case "status":
                UpdateV2PlaybackView();
                return McpStatus();
            case "project":
                return JsonSerializer.Deserialize<JsonElement>(TopologyProjectFormat.Serialize(
                    _activeTopologyProjectDocument ?? TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument())));
            case "load":
            {
                var path = files.Resolve(Text(args, "path"));
                // File I/O, validation and candidate runtime prep precede UI replacement.
                IsEnabled = false;
                try
                {
                    var document = await Task.Run(() => files.ReadProject(path));
                    if (LegacyPortMigration.FindUnspecifiedEdges(document).Count > 0)
                        throw new InvalidOperationException("MCP 載入要求明確接軌側別；請先在專案工作區完成遷移。");
                    await ConfigureTopologyProjectForPlaybackAsync(document, lockLegacyInputs: true);
                    SetCurrentProjectFile(path);
                    StatusTextBlock.Text = $"MCP 已載入專案：{path}";
                }
                finally { IsEnabled = true; }
                return McpStatus();
            }
            case "save":
            {
                var overwrite = Flag(args, "overwrite");
                var path = files.PrepareOutput(Text(args, "path"), ".mrtsim.json", overwrite);
                var document = _activeTopologyProjectDocument ?? TopologyProjectFactory.CreateLinearDraft(CaptureProjectDocument());
                var json = TopologyProjectFormat.Serialize(document);
                files.WriteAtomically(path, json, overwrite);
                SetCurrentProjectFile(path);
                return new { path, saved = true };
            }
            case "play":
            {
                var worker = McpWorker();
                var rate = Number(args, "rate", GetPlaybackSpeed());
                if (rate is not (1 or 10 or 30 or 60)) throw new ArgumentOutOfRangeException(nameof(rate));
                SelectComboBoxTag(PlaybackSpeedComboBox, rate.ToString(CultureInfo.InvariantCulture));
                await PlayPlaybackAsync();
                EnsureMcpWorkerUnchanged(worker);
                ThrowIfMcpWorkerStopped(worker);
                UpdateV2PlaybackView(force: true);
                if (McpFrame().IsComplete)
                {
                    await PausePlaybackCommandAsync();
                    throw new InvalidOperationException("模擬已完成，請先重設再播放。");
                }
                if (!_isV2PlaybackPlaying) throw new InvalidOperationException("播放未成功啟動。");
                return McpStatus();
            }
            case "pause":
            {
                var worker = McpWorker();
                await PausePlaybackCommandAsync();
                EnsureMcpWorkerUnchanged(worker);
                ThrowIfMcpWorkerStopped(worker);
                UpdateV2PlaybackView(force: true);
                return McpStatus();
            }
            case "reset":
            {
                var worker = McpWorker();
                await ResetPlaybackAsync();
                EnsureMcpWorkerUnchanged(worker);
                ThrowIfMcpWorkerStopped(worker);
                if (McpFrame().SimulationTimeSeconds != 0) throw new InvalidOperationException("重設未完成。");
                return McpStatus();
            }
            case "advance":
            {
                var worker = McpWorker();
                var target = Number(args, "targetSeconds");
                UpdateV2PlaybackView();
                SimulationAutomationSession.ValidateAdvance(target, McpFrame().SimulationTimeSeconds);
                var wasPlaying = _isV2PlaybackPlaying;
                // Pause and drain before validating against the acknowledged current time.
                await PausePlaybackCommandAsync();
                EnsureMcpWorkerUnchanged(worker);
                UpdateV2PlaybackView();
                try
                {
                    SimulationAutomationSession.ValidateAdvance(target, McpFrame().SimulationTimeSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // The worker may have advanced after the initial check; restore prior playback.
                    if (wasPlaying && !McpFrame().IsComplete) await PlayPlaybackAsync();
                    throw;
                }
                await worker.AdvanceToSimulationTimeAsync(target);
                EnsureMcpWorkerUnchanged(worker);
                ThrowIfMcpWorkerStopped(worker);
                UpdateV2PlaybackView(force: true);
                return McpStatus();
            }
            case "events":
                UpdateV2PlaybackView();
                return SimulationAutomationSession.Page(McpFrame().Events, Integer(args, "offset", 0), Integer(args, "limit", 100));
            case "timetable":
            {
                UpdateV2PlaybackView();
                var frame = McpFrame();
                var rows = OperationsTimetable.Build(frame.GetTopologyResultContext(), frame.DispatchPlan!,
                    _plannedTimetableEvents, frame.Events);
                return SimulationAutomationSession.Page(rows, Integer(args, "offset", 0), Integer(args, "limit", 100));
            }
            case "select_page":
                SelectMcpPage(Text(args, "page"));
                UpdateV2PlaybackView(force: true);
                return McpStatus();
            case "zoom":
            {
                var route = Number(args, "route", _routeMapHorizontalZoom);
                var horizontal = Number(args, "horizontal", DiagramZoomSlider.Value);
                var vertical = Number(args, "vertical", DiagramVerticalZoomSlider.Value);
                if (route is not (1 or 1.25 or 1.5 or 2) || horizontal is < 1 or > 4 || vertical is < 1 or > 4)
                    throw new ArgumentOutOfRangeException(nameof(request), "路線縮放為 1/1.25/1.5/2，運行圖縮放為 1～4。");
                // Validate all values before changing any setting; use normal control handlers.
                SelectComboBoxTag(RouteHorizontalZoomComboBox, route.ToString(CultureInfo.InvariantCulture));
                DiagramZoomSlider.Value = horizontal;
                DiagramVerticalZoomSlider.Value = vertical;
                return McpStatus();
            }
            case "export":
                return ExportMcpDiagram(files, args);
            default:
                throw new ArgumentException("不支援的 MCP 桌面命令。");
        }
    }

    private object McpStatus()
    {
        var frame = _latestPlaybackFrame;
        return new
        {
            mode = "desktop", processId = Environment.ProcessId,
            version = ProductVersion.Current, workspaceRoot = _mcpFiles?.Root,
            projectId = _activeTopologyProjectDocument?.ProjectId,
            projectName = _activeTopologyProjectDocument?.ProjectName,
            currentProjectFile = _currentProjectFilePath,
            isPlaying = _isV2PlaybackPlaying && frame?.IsComplete != true, simulationTimeSeconds = frame?.SimulationTimeSeconds ?? 0,
            isComplete = frame?.IsComplete ?? false, generationId = frame?.GenerationId,
            sequence = frame?.Sequence, page = (WorkspaceTabControl.SelectedItem as TabItem)?.Name,
            simulationView = SimulationViewTabControl.SelectedIndex,
            routeZoom = _routeMapHorizontalZoom,
            diagramHorizontalZoom = DiagramZoomSlider.Value, diagramVerticalZoom = DiagramVerticalZoomSlider.Value,
            status = StatusTextBlock.Text,
            trains = frame?.Trains, currentSafety = frame?.CurrentSafety,
            eventCount = frame?.Events.Count ?? 0
        };
    }

    private static void ThrowIfMcpWorkerStopped(SimulationPlaybackWorker worker)
    {
        if (worker.Completion.IsCompleted)
            throw new InvalidOperationException("模擬工作者已停止。", worker.Completion.Exception?.GetBaseException());
    }

    private SimulationPlaybackWorker McpWorker() => _v2Enabled && _playbackWorker is not null
        ? _playbackWorker : throw new InvalidOperationException("請先以 MCP load 載入 Schema 8 專案。");
    private PlaybackFrame McpFrame() => _latestPlaybackFrame ?? throw new InvalidOperationException("目前沒有模擬 frame。");
    private void EnsureMcpWorkerUnchanged(SimulationPlaybackWorker worker)
    {
        if (!ReferenceEquals(worker, _playbackWorker)) throw new InvalidOperationException("專案已切換，請重新查詢狀態。");
    }

    private void SelectMcpPage(string page)
    {
        switch (page)
        {
            case "route": WorkspaceTabControl.SelectedItem = SimulationTabItem; SimulationViewTabControl.SelectedIndex = 0; break;
            case "trains": WorkspaceTabControl.SelectedItem = SimulationTabItem; SimulationViewTabControl.SelectedIndex = 1; break;
            case "speed": WorkspaceTabControl.SelectedItem = SimulationTabItem; SimulationViewTabControl.SelectedItem = SpeedProfileTabItem; break;
            case "timetable": WorkspaceTabControl.SelectedItem = ResultsTabItem; break;
            case "segments": WorkspaceTabControl.SelectedItem = SegmentTabItem; break;
            case "comparison": WorkspaceTabControl.SelectedItem = ComparisonTabItem; break;
            case "resources": WorkspaceTabControl.SelectedItem = ResourceTabItem; break;
            case "safety": WorkspaceTabControl.SelectedItem = SafetyTabItem; break;
            case "intervals": WorkspaceTabControl.SelectedItem = IntervalStatisticsTabItem; break;
            case "diagram": WorkspaceTabControl.SelectedItem = DiagramTabItem; break;
            default: throw new ArgumentException("不支援的結果頁面。");
        }
    }

    private object ExportMcpDiagram(WorkspaceFiles files, JsonElement args)
    {
        var format = Text(args, "format").ToLowerInvariant();
        if (format is not ("png" or "pdf" or "csv")) throw new ArgumentException("format 必須是 png/pdf/csv。");
        var overwrite = Flag(args, "overwrite");
        var path = files.PrepareOutput(Text(args, "path"), "." + format, overwrite);
        UpdateV2PlaybackView(force: true);
        var frame = McpFrame();
        if (frame.Trajectory.Count == 0) throw new InvalidOperationException("請先推進模擬產生軌跡。");
        if (format == "csv")
        {
            files.WriteAtomically(path, TrajectoryAnalysis.BuildCsv(frame.Trajectory, frame.Events, _startClockSeconds), overwrite);
        }
        else
        {
            // Render once using the same visual/export implementation as the UI.
            SelectMcpPage("diagram");
            UpdateLayout();
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N") + "." + format);
            try
            {
                DrawTimeDistanceDiagramFull();
                if (format == "png")
                    DiagramExportService.ExportPng(TimeDistanceCanvas, temporary, HighResolutionCheckBox.IsChecked == true ? 2 : 1);
                else
                    DiagramExportService.ExportPdf(TimeDistanceCanvas, temporary, GetSelectedPdfPageSize(), PdfSplitPagesCheckBox.IsChecked == true);
                File.Move(temporary, path, overwrite);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                _diagramLayoutKey = null;
                DrawTimeDistanceDiagram();
            }
        }
        return new { path, exported = true, simulationTimeSeconds = frame.SimulationTimeSeconds };
    }

    private static string Text(JsonElement args, string key) =>
        args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new ArgumentException("缺少文字參數：" + key);
    private static double Number(JsonElement args, string key, double? fallback = null)
    {
        var result = args.TryGetProperty(key, out var value) ? value.GetDouble()
            : fallback ?? throw new ArgumentException("缺少數值參數：" + key);
        if (!double.IsFinite(result)) throw new ArgumentException("數值必須有限：" + key);
        return result;
    }
    private static int Integer(JsonElement args, string key, int fallback) =>
        args.TryGetProperty(key, out var value) ? value.GetInt32() : fallback;
    private static bool Flag(JsonElement args, string key) =>
        args.TryGetProperty(key, out var value) && value.GetBoolean();
}
