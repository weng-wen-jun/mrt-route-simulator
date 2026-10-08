using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MrtRouteSimulator.Automation;
using MrtRouteSimulator.Engine;

namespace MrtRouteSimulator.Mcp;

[McpServerToolType]
public sealed class MrtTools(WorkspaceFiles files, SimulationAutomationSession session, ILogger<MrtTools> logger)
{
    private readonly SemaphoreSlim _gate = session.OperationGate;

    private async Task<string> HeadlessAsync(Func<object> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return AutomationJson.Serialize(operation()); }
        finally { _gate.Release(); }
    }

    [McpServerTool(Name = "server_info", ReadOnly = true)]
    [Description("取得 MRT MCP 版本、Schema、工作目錄與工具限制。headless session 與桌面程式分開；桌面命令需指定 PID。")]
    public string ServerInfo() => AutomationJson.Serialize(new
    {
        version = typeof(MrtTools).Assembly.GetName().Version?.ToString(),
        sdkVersion = typeof(McpServerToolAttribute).Assembly.GetName().Version?.ToString(),
        schemaVersion = TopologyProjectFormat.CurrentSchemaVersion,
        workspaceRoot = files.Root,
        transport = "stdio",
        maximumAdvanceSecondsPerCall = 60,
        desktopBridgeRequiresOptIn = true,
        desktopStatusTool = "desktop_status",
        desktopCommands = new[] { "project", "load", "save", "play", "pause", "reset", "advance", "events", "timetable", "select_page", "zoom", "export" }
    });

    [McpServerTool(Name = "project_validate", ReadOnly = true)]
    [Description("讀取並驗證工作目錄內的 Schema 8 專案，建立 runtime 檢查；不替換目前 session。")]
    public Task<string> Validate(string path, CancellationToken cancellationToken) => HeadlessAsync(() =>
    {
        var document = files.ReadProject(path);
        var runtime = TopologyProjectFormat.CreateRuntime(document);
        return new { valid = true, document.ProjectId, document.ProjectName, schemaVersion = document.SchemaVersion, runs = runtime.DispatchPlan.Runs.Count };
    }, cancellationToken);

    [McpServerTool(Name = "project_load")]
    [Description("載入工作目錄內的 Schema 8 專案到 headless session。完整驗證及 runtime 建立成功後才替換舊 session；不影響桌面。")]
    public Task<string> Load(string path, CancellationToken cancellationToken) => HeadlessAsync(() =>
    {
        var document = files.ReadProject(path);
        session.Load(document, files.Resolve(path));
        return session.Snapshot();
    }, cancellationToken);

    [McpServerTool(Name = "project_get", ReadOnly = true)]
    [Description("取得 headless session 目前的完整 Schema 8 專案 JSON，可修改 JSON 後用 project_replace 套用。")]
    public Task<string> GetProject(CancellationToken cancellationToken) => HeadlessAsync(() =>
    {
        using var json = JsonDocument.Parse(TopologyProjectFormat.Serialize(session.Document ?? throw new InvalidOperationException("請先載入專案。")));
        return json.RootElement.Clone();
    }, cancellationToken);

    [McpServerTool(Name = "project_replace")]
    [Description("以完整 Schema 8 JSON 替換 headless 專案並重設模擬。適用修改車型、服務、停站、派車或 topology；仍使用既有 validator。")]
    public Task<string> Replace(string projectJson, CancellationToken cancellationToken) => HeadlessAsync(() =>
    {
        session.Load(TopologyProjectFormat.Deserialize(projectJson));
        return session.Snapshot();
    }, cancellationToken);

    [McpServerTool(Name = "project_save")]
    [Description("將 headless 專案原子儲存為工作目錄內 .mrtsim.json；overwrite 預設 false。")]
    public Task<string> Save(string path, bool overwrite = false, CancellationToken cancellationToken = default) => HeadlessAsync(() =>
    {
        var target = files.PrepareOutput(path, ".mrtsim.json", overwrite);
        var json = TopologyProjectFormat.Serialize(session.Document ?? throw new InvalidOperationException("請先載入專案。"));
        files.WriteAtomically(target, json, overwrite);
        return new { path = target, saved = true };
    }, cancellationToken);

    [McpServerTool(Name = "simulation_snapshot", ReadOnly = true)]
    [Description("查詢 headless 模擬的實際時間、列車狀態、安全觀測；單位為 m、s、m/s。")]
    public Task<string> Snapshot(CancellationToken cancellationToken) => HeadlessAsync(session.Snapshot, cancellationToken);

    [McpServerTool(Name = "simulation_advance")]
    [Description("headless 模擬推進至 targetSeconds，保留每個 0.1 秒子步進。每次最多 60 秒；不可倒退；回報實際抵達時間。")]
    public Task<string> Advance(double targetSeconds, CancellationToken cancellationToken) => HeadlessAsync(() =>
        session.Advance(targetSeconds, cancellationToken), cancellationToken);

    [McpServerTool(Name = "simulation_reset")]
    [Description("將 headless 模擬重設為時間 0；清除本次軌跡與事件，保留已載入專案。")]
    public Task<string> Reset(CancellationToken cancellationToken) => HeadlessAsync(() =>
    {
        session.World.Reset();
        return session.Snapshot();
    }, cancellationToken);

    [McpServerTool(Name = "simulation_events", ReadOnly = true)]
    [Description("分頁查詢 headless 營運事件；offset >= 0，limit 1～500。")]
    public Task<string> Events(int offset = 0, int limit = 100, CancellationToken cancellationToken = default) =>
        HeadlessAsync(() => SimulationAutomationSession.Page(session.World.Events, offset, limit), cancellationToken);

    [McpServerTool(Name = "simulation_timetable", ReadOnly = true)]
    [Description("查詢 headless 實際進出站時刻表。以相同 world 事件與 topology context 建立；未另跑完整計畫時間軸。")]
    public Task<string> Timetable(int offset = 0, int limit = 100, CancellationToken cancellationToken = default) =>
        HeadlessAsync(() => session.Timetable(offset, limit), cancellationToken);

    [McpServerTool(Name = "simulation_export_csv")]
    [Description("匯出 headless 軌跡與事件 CSV 至工作目錄內；與模擬共用同一份輸出，overwrite 預設 false。")]
    public Task<string> ExportCsv(string path, bool overwrite = false, CancellationToken cancellationToken = default) =>
        HeadlessAsync(() =>
        {
            var target = files.PrepareOutput(path, ".csv", overwrite);
            var csv = TrajectoryAnalysis.BuildCsv(session.World.Trajectory, session.World.Events, session.Document!.Simulation.StartClockSeconds);
            files.WriteAtomically(target, csv, overwrite);
            return new { path = target, exported = true };
        }, cancellationToken);

    [McpServerTool(Name = "desktop_launch")]
    [Description("啟動工作目錄內 Release 版 MRT WPF（新視窗），並啟用本機 MCP bridge。回傳 PID；不關閉或改動既有程式。需先建置 App。")]
    public string Launch()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var sibling = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "MrtRouteSimulator.App", "release", "MRT路線進出站時間模擬器.exe"));
        var executable = File.Exists(sibling) ? files.Resolve(sibling)
            : files.Resolve("src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("請先建置 Release App。", executable);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = files.Root, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--mcp-bridge");
        start.ArgumentList.Add("--mcp-workspace-root");
        start.ArgumentList.Add(files.Root);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("無法啟動桌面程式。");
        logger.LogInformation("MRT desktop bridge launched: PID {ProcessId}", process.Id);
        return AutomationJson.Serialize(new { processId = process.Id, bridgePipe = DesktopBridge.PipeName(process.Id), ready = false, nextStep = "使用 desktop_status 確認啟動完成。" });
    }

    [McpServerTool(Name = "desktop_status", ReadOnly = true)]
    [Description("只讀查詢指定 PID 的 MRT WPF 狀態；程式須以 --mcp-bridge 啟動。")]
    public async Task<string> DesktopStatus(int processId, CancellationToken cancellationToken) =>
        AutomationJson.Serialize(await DesktopBridge.CallAsync(processId,
            new DesktopRequest("status", JsonSerializer.SerializeToElement(new { workspaceRoot = files.Root })), cancellationToken));

    [McpServerTool(Name = "desktop_command")]
    [Description("""
操作指定 PID 的 MRT WPF。command: project、load、save、play、pause、reset、advance、events、timetable、select_page、zoom、export。
argumentsJson 是 JSON 物件：load {path}；save {path,overwrite:false}；play {rate:1|10|30|60}；advance {targetSeconds} 每次最多60秒；events/timetable {offset:0,limit:100}；select_page {page:"route|trains|speed|timetable|segments|comparison|resources|safety|intervals|diagram"}；zoom {route:1|1.25|1.5|2,horizontal:1..4,vertical:1..4}；export {path,format:"png|pdf|csv",overwrite:false}。命令會改動目前桌面；不會重送。
""")]
    public async Task<string> DesktopCommand(int processId, string command, string argumentsJson = "{}", CancellationToken cancellationToken = default)
    {
        var allowed = new[] { "project", "load", "save", "play", "pause", "reset", "advance", "events", "timetable", "select_page", "zoom", "export" };
        if (!allowed.Contains(command, StringComparer.Ordinal)) throw new ArgumentException("不支援的桌面命令。");
        if (argumentsJson.Length > TopologyProjectFormat.MaximumJsonCharacters) throw new ArgumentException("命令參數過大。");
        var arguments = System.Text.Json.Nodes.JsonNode.Parse(argumentsJson) as System.Text.Json.Nodes.JsonObject
            ?? throw new ArgumentException("argumentsJson 必須是 JSON 物件。");
        arguments["workspaceRoot"] = files.Root;
        var result = await DesktopBridge.CallAsync(processId, new DesktopRequest(command,
            JsonSerializer.SerializeToElement(arguments, AutomationJson.Options)), cancellationToken);
        return AutomationJson.Serialize(result);
    }
}
