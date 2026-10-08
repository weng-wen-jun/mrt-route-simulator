# MRT MCP 使用方式

本功能以最新 V4.0.3 工作目錄為基礎實作，依新功能進版規則更新為 V4.1.0（尚未發布），沿用 Schema 8、TopologyProjectFormat 與 SimulationWorld。MCP C# SDK 使用官方穩定版 2.2.0；Microsoft.Extensions.Hosting 為 10.0.10。

## 建置與連線

因既有桌面程式可能鎖住預設 Release 檔案，MCP 使用獨立 artifacts 目錄：

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\src\MrtRouteSimulator.Mcp\NuGet.Config -p:ArtifactsPath=output/v4.1.0-unified
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore -p:ArtifactsPath=output/v4.1.0-unified
dotnet .\output\v4.1.0-unified\bin\MrtRouteSimulator.Mcp\release\MrtRouteSimulator.Mcp.dll --workspace-root .
```

最後一行是 stdio server，會等待 MCP client，非一般互動式 CLI。所有日誌寫 stderr，stdout 僅提供 JSON-RPC。專案根目錄原有的離線 NuGet.Config 保留；僅 MCP 的 NuGet.Config 啟用官方 nuget.org。

已透過 `codex mcp add` 註冊啟用 `mrt-route-simulator`，固定指向這個最新工作目錄；啟動逾時20秒、工具逾時180秒。本次對話曾自動載入工具並完成直接呼叫；最後修正版重新建置時已停止本次 MCP 連線，請新開對話或重新連線以載入最終版本。目前 CLI 的 `mcp get` 未讀取專案 config，因此以本機註冊為驗證依據；專案 `.codex/config.toml` 保留相同連線設定。其他電腦須調整 config 的 cwd；不要把本機工作路徑直接套用到別台電腦。啟動的是已建置版本，修改後須重新 build 及重新連線。

## headless 模擬

工具 `project_validate` 不替換 session。`project_load` 載入後，可用 `simulation_advance`、`simulation_snapshot`、`simulation_events`、`simulation_timetable`、`simulation_reset` 和 `simulation_export_csv`。

`project_get` 取得完整 Schema 8 JSON；修改後用 `project_replace` 完整驗證及建立候選 runtime，成功才替換 session。修改後模擬重設。這是修改車型／服務／停站／派車／topology 的第一版入口，未新增另一套 domain model。

`project_save` 採暫存檔與原子搬移。所有輸出預設 `overwrite=false`，需明確設 true 才覆寫，輸出目錄須先存在。路徑只接受 workspace 內的實體檔案，不接受 junction／symbolic link。

範例流程：
1. project_load：path = samples/10-小型-三站完整拓樸基準範例.mrtsim.json
2. simulation_advance：targetSeconds = 10
3. simulation_snapshot
4. simulation_events：offset = 0，limit = 100
5. simulation_timetable：offset = 0，limit = 100

時間與距離採 Engine 單位 s、m、m/s。推進保留完整 0.1 秒子步進，實際時間可能小於小數目標；不可倒退，倒退須先 reset。單次最多推進 60 秒，最大時間 86400 秒。取消 headless advance 會保留已完成子步進，請先查詢 snapshot 再繼續。

快照為可重複的狀態讀取，snapshot.newEvents 固定為空；營運事件請用 simulation_events 的 offset 分頁讀取，不以快照 delta 收集事件。

headless 與桌面 session 各自獨立；headless 不控制正在開啟的視窗。headless 時刻表共用 world 事件，未另跑完整計畫時間軸；尚未發生的計畫到站欄位可能空白。軌跡保留採 0.5 秒 decimation，安全歷史採 1 秒，物理步進仍為 0.1 秒。

## 操作桌面程式

正常啟動不開啟橋接。使用 `desktop_launch` 建立一個新的 MRT 視窗，回傳 processId，接著用 `desktop_status` 確認就緒。這不會接管既有未啟用橋接的視窗，也不會關閉其他程式。

也可自行啟動：

```powershell
& '.\output\v4.1.0-unified\bin\MrtRouteSimulator.App\release\MRT路線進出站時間模擬器.exe' --mcp-bridge --mcp-workspace-root 'D:\AI\codex\mrt-route-simulator'
```

每次 `desktop_command` 都須指定 processId、command 和 argumentsJson。argumentsJson 是 JSON 物件字串。

| command | argumentsJson 範例 |
|---|---|
| project | {} |
| load | {"path":"samples/10-小型-三站完整拓樸基準範例.mrtsim.json"} |
| save | {"path":"output/project.mrtsim.json","overwrite":false} |
| play | {"rate":10} |
| pause / reset | {} |
| advance | {"targetSeconds":30} |
| events / timetable | {"offset":0,"limit":100} |
| select_page | {"page":"diagram"} |
| zoom | {"route":1.25,"horizontal":1.5,"vertical":1.25} |
| export | {"path":"output/diagram.png","format":"png","overwrite":false} |

page 支援 route、trains、speed、timetable、segments、comparison、resources、safety、intervals、diagram。路線縮放為 1／1.25／1.5／2，運行圖兩軸為 1～4。縮放沿用正常控制項 handler；路線縮放會保存至原有本機顯示偏好。export 支援 png／pdf／csv，視覺輸出沿用目前篩選、解析度及 PDF 頁面設定。

橋接採 Windows 同一使用者信任界線，不隔離同帳號的其他程序，透過每 PID 的 named pipe，無 TCP listener。UI 操作在 Dispatcher，world 仍只由原有播放 worker 寫入，讀取來自 immutable frame。播放、暫停與重設和人工按鈕共用可等待的操作；完成後播放會沿用正常按鈕重設再播放，事件與時刻表讀相同 frame。JSON bridge 單則最大 4 MB，事件／時刻表提供分頁，limit 最大 500。

桌面載入只接受明確 port-side 的 Schema 8，避免在遠端命令中猜測接軌側別；Schema 7／固定時刻表與接軌遷移仍使用現有 UI。開啟編輯器／對話框或載入時會拒絕其他操作。

桌面命令完成才回應；正在執行的桌面操作不會因 client 取消而回滾。逾時／斷線後不要直接重送寫入或 reset，先查 desktop_status。這是程式操作入口，不是原生滑鼠或 compositor 延遲驗收；DPI、文字重疊與拖曳手感仍須畫面驗收。

## 驗證

```powershell
dotnet .\output\v4.1.0-unified\bin\MrtRouteSimulator.Tests\release\MrtRouteSimulator.Tests.dll
dotnet .\output\v4.1.0-unified\bin\MrtRouteSimulator.WpfTests\release\MrtRouteSimulator.WpfTests.dll . --mcp-only
dotnet .\output\v4.1.0-unified\bin\MrtRouteSimulator.WpfTests\release\MrtRouteSimulator.WpfTests.dll .
python .\scripts\test-mcp.py --workspace-root .
python .\scripts\test-mcp-desktop.py --workspace-root .
```

WPF 專項透過真正 named pipe 執行 PID、workspace、載入與失敗保留、固定 tick、分頁、覆寫拒絕、切頁、縮放、CSV／PNG／PDF、Play／Pause 確認、Reset 等回歸。Python 腳本檢查 stdio initialize、tools/list、tools/call 與 session 行為，無第三方 Python 套件。額外驗證三輪並行 advance/reset；桌面黑箱腳本使用已註冊的 command／args，建立新視窗完成操作與輸出後只正常關閉該測試視窗，報告位於 `output/mcp-desktop-smoke.json`。

2026-10-07 最終驗證：V4.1.0 Release 建置 0 警告／0 錯誤，Engine 198/198、完整 WPF runner、stdio 黑箱及已註冊 MCP 的桌面黑箱全部通過。本次 Codex 對話亦直接呼叫 server_info、project_validate、project_load、simulation_advance 與 simulation_snapshot，確認版本 4.1.0.0、Schema 8 及時間到達 10 秒。記錄見 QA_REPORT.md 與 output/mcp-engine-final.log、output/mcp-wpf-final.log、output/mcp-desktop-smoke.json。這些結果不取代既有原生 DPI／互動效能驗收。

官方來源：
- https://www.nuget.org/packages/ModelContextProtocol/2.2.0
- https://github.com/modelcontextprotocol/csharp-sdk
- https://learn.chatgpt.com/docs/extend/mcp
