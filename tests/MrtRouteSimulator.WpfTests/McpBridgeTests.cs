using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MrtRouteSimulator.App;
using MrtRouteSimulator.Automation;

internal static class McpBridgeTests
{
    public static void Run(string root)
    {
        RunTransportRecovery();
        var output = Path.Combine(root, "output", "mcp-wpf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var window = new MainWindow { Width = 1100, Height = 800 };
        double oldRouteZoom = 1;
        try
        {
            window.Measure(new Size(1100, 800));
            window.Arrange(new Rect(0, 0, 1100, 800));
            window.UpdateLayout();
            window.EnableMcpBridge(root);
            var initial = Call("status");
            oldRouteZoom = initial.GetProperty("routeZoom").GetDouble();
            Require(initial.GetProperty("processId").GetInt32() == Environment.ProcessId, "bridge 必須指向目前 PID。");
            Reject("status", new { workspaceRoot = Path.GetPathRoot(root)! }, "不同工作目錄必須拒絕。", includeRoot: false);
            Call("load", new { path = "samples/10-小型-三站完整拓樸基準範例.mrtsim.json" });
            var before = Call("status");
            Require(before.GetProperty("simulationTimeSeconds").GetDouble() == 0, "載入須從時間 0 開始。");
            var advanced = Call("advance", new { targetSeconds = 10.05 });
            Require(Math.Abs(advanced.GetProperty("simulationTimeSeconds").GetDouble() - 10) < 1e-8,
                "指定時間只能完成完整 0.1 秒 tick。");
            var generation = advanced.GetProperty("generationId").GetString();
            Reject("advance", new { targetSeconds = 9 }, "倒退必须拒絕。");
            Reject("advance", new { targetSeconds = 100 }, "單次超過 60 秒必須拒絕。");
            Reject("load", new { path = Path.Combine(output, "missing.mrtsim.json") }, "無效載入必須拒絕。");
            var preserved = Call("status");
            Require(preserved.GetProperty("generationId").GetString() == generation
                && preserved.GetProperty("simulationTimeSeconds").GetDouble() == 10,
                "失敗載入不可替換專案或時間。");
            var invalid = Path.Combine(output, "invalid.mrtsim.json");
            File.WriteAllText(invalid, "{\"schemaVersion\":99}");
            Reject("load", new { path = invalid }, "未來 Schema 必須拒絕。");
            Reject("save", new { path = Path.Combine(root, "..", "escape.mrtsim.json") }, "工作目錄外路徑必須拒絕。");
            var saved = Path.Combine(output, "copy.mrtsim.json");
            Call("save", new { path = saved });
            Require(File.Exists(saved), "專案須可儲存。");
            Reject("save", new { path = saved }, "不可默默覆寫。");
            var events = Call("events", new { offset = 0, limit = 1 });
            Require(events.GetProperty("items").GetArrayLength() <= 1, "事件分頁限制須生效。");
            Reject("events", new { limit = 501 }, "過大的事件頁必須拒絕。");
            Call("timetable", new { limit = 5 });
            Call("select_page", new { page = "trains" });
            Require(Call("status").GetProperty("simulationView").GetInt32() == 1, "須能切換列車狀態子頁。");
            Call("select_page", new { page = "diagram" });
            var zoomBefore = Call("status");
            Reject("zoom", new { route = 2, horizontal = 9 }, "縮放參數應整批驗證。");
            Require(Call("status").GetProperty("routeZoom").GetDouble() == zoomBefore.GetProperty("routeZoom").GetDouble(),
                "無效縮放不得只套用部分參數。");
            Call("zoom", new { route = 1.25, horizontal = 1.5, vertical = 1.25 });
            var zoom = Call("status");
            Require(zoom.GetProperty("routeZoom").GetDouble() == 1.25
                && zoom.GetProperty("diagramHorizontalZoom").GetDouble() == 1.5, "縮放須套用共用 UI。");
            foreach (var format in new[] { "csv", "png", "pdf" })
            {
                var path = Path.Combine(output, "diagram." + format);
                Call("export", new { path, format });
                Require(new FileInfo(path).Length > 0, "輸出檔案不可空白：" + format);
                Reject("export", new { path, format }, "匯出不可默默覆寫：" + format);
            }
            Call("play", new { rate = 10 });
            Require(Call("status").GetProperty("isPlaying").GetBoolean(), "正常 Play 應已確認。");
            Reject("advance", new { targetSeconds = -1 }, "播放中無效推進必須拒絕。");
            Require(Call("status").GetProperty("isPlaying").GetBoolean(), "無效推進不得暫停原本播放。");
            Call("pause");
            var paused = Call("status");
            Require(!paused.GetProperty("isPlaying").GetBoolean(), "Pause 必須已確認停止。");
            var pauseTime = paused.GetProperty("simulationTimeSeconds").GetDouble();
            WpfTestWait.Wait(Task.Delay(100));
            Require(Call("status").GetProperty("simulationTimeSeconds").GetDouble() == pauseTime, "暫停後時間不可繼續前進。");
            var reset = Call("reset");
            Require(reset.GetProperty("simulationTimeSeconds").GetDouble() == 0
                && reset.GetProperty("eventCount").GetInt32() == before.GetProperty("eventCount").GetInt32(), "Reset 須回到初始時間與初始發車事件。實際：" + reset.GetRawText());
            for (var target = 60; target <= 600 && !Call("status").GetProperty("isComplete").GetBoolean(); target += 60)
                Call("advance", new { targetSeconds = target });
            Require(Call("status").GetProperty("isComplete").GetBoolean(), "三站案例須有界完成。");
            var replay = Call("play", new { rate = 10 });
            Require(replay.GetProperty("isPlaying").GetBoolean()
                && !replay.GetProperty("isComplete").GetBoolean()
                && replay.GetProperty("simulationTimeSeconds").GetDouble() < 60,
                "完成後 Play 須沿用正常按鈕重設再播放，不得同時回報完成與播放中。");
            Call("pause");
            Call("reset");

            // 第二段：設施與營運最完整的 28 站範例。大型路線的 PDF 匯出與完整播放會長時間占用
            // 互動桌面的 UI 執行緒，因此只驗證載入、推進、儲存、查詢、切頁與 CSV 匯出。
            Call("load", new { path = "samples/14-大型-二十八站完整營運範例.mrtsim.json" });
            WpfTestWait.WaitForPlannedTimeline(window);
            var large = Call("status");
            Require(large.GetProperty("simulationTimeSeconds").GetDouble() == 0
                && large.GetProperty("projectId").GetString() != before.GetProperty("projectId").GetString(),
                "範例 14 須取代三站範例並從時間 0 開始。");
            var largeAdvanced = Call("advance", new { targetSeconds = 30.05 });
            Require(Math.Abs(largeAdvanced.GetProperty("simulationTimeSeconds").GetDouble() - 30) < 1e-8,
                "範例 14 也只能完成完整 0.1 秒 tick。");
            Require(largeAdvanced.GetProperty("trains").GetArrayLength() > 0, "範例 14 推進後須有列車狀態。");
            var largeSaved = Path.Combine(output, "copy-large.mrtsim.json");
            Call("save", new { path = largeSaved });
            Require(File.Exists(largeSaved), "範例 14 須可儲存。");
            Require(Call("events", new { offset = 0, limit = 5 }).GetProperty("items").GetArrayLength() > 0, "範例 14 推進後須有事件。");
            Call("timetable", new { limit = 5 });
            Call("select_page", new { page = "trains" });
            Require(Call("status").GetProperty("simulationView").GetInt32() == 1, "範例 14 須能切換列車狀態子頁。");
            Call("select_page", new { page = "diagram" });
            // 完整 2 小時、28 站運行圖的 PNG／PDF 匯出目前超過影像編碼器尺寸上限（既有問題，另案處理），
            // 此段只驗證大型路線的 CSV 軌跡匯出。
            var largeCsv = Path.Combine(output, "diagram-large.csv");
            Call("export", new { path = largeCsv, format = "csv" });
            Require(new FileInfo(largeCsv).Length > 0, "範例 14 CSV 不可空白。");
            Reject("export", new { path = largeCsv, format = "csv" }, "範例 14 匯出不可默默覆寫。");
            Reject("unknown", new { }, "未知命令必須拒絕。");
            Console.WriteLine("[通過] MCP named pipe：範例 10 PID、載入、固定 tick、失敗不污染、分頁、儲存、播放確認、切頁、縮放及 CSV/PNG/PDF；範例 14 載入、推進、儲存、查詢、切頁及 CSV");
        }
        finally
        {
            if (WpfTestWait.Field(window, "_mcpFiles") is not null)
            {
                try { Call("zoom", new { route = oldRouteZoom }); }
                catch { }
            }
            WpfTestWait.Close(window);
            // Only remove this test's freshly created output directory.
            Directory.Delete(output, recursive: true);
        }

        JsonElement Call(string command, object? arguments = null, bool includeRoot = true)
        {
            var json = JsonSerializer.SerializeToNode(arguments ?? new { })!.AsObject();
            if (includeRoot) json["workspaceRoot"] = root;
            var request = new DesktopRequest(command, JsonSerializer.SerializeToElement(json, AutomationJson.Options));
            var task = Task.Run(() => DesktopBridge.CallAsync(Environment.ProcessId, request, CancellationToken.None));
            WpfTestWait.Wait(task);
            return task.GetAwaiter().GetResult();
        }

        void Reject(string command, object arguments, string message, bool includeRoot = true)
        {
            try { Call(command, arguments, includeRoot); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException(message);
        }
    }

    private static void RunTransportRecovery()
    {
        var server = new DesktopBridgeServer((request, _) => Task.FromResult<object>(
            request.Command == "oversize" ? new string('x', DesktopBridge.MaximumMessageBytes)
                : new { ok = true }));
        try
        {
            var args = JsonSerializer.SerializeToElement(new { });
            var oversized = Task.Run(() => DesktopBridge.CallAsync(Environment.ProcessId,
                new DesktopRequest("oversize", args), CancellationToken.None));
            var rejected = false;
            try { WpfTestWait.Wait(oversized); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "超過 4 MB 的回應須明確拒絕。");

            var malformed = Task.Run(async () =>
            {
                await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", DesktopBridge.PipeName(Environment.ProcessId),
                    System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(5000);
                await pipe.WriteAsync(new byte[] { 255, 255, 255, 255 });
                return await DesktopBridge.ReadAsync<DesktopResponse>(pipe, CancellationToken.None);
            });
            WpfTestWait.Wait(malformed);
            Require(!malformed.Result.Success, "無效訊息長度須拒絕。");

            var recovered = Task.Run(() => DesktopBridge.CallAsync(Environment.ProcessId,
                new DesktopRequest("status", args), CancellationToken.None));
            WpfTestWait.Wait(recovered);
            Require(recovered.Result.GetProperty("ok").GetBoolean(), "錯誤後橋接仍須可供下一次呼叫。");
            Console.WriteLine("[通過] MCP transport：超大回應與無效 framing 後可繼續服務");
        }
        finally { WpfTestWait.Wait(server.DisposeAsync().AsTask()); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
