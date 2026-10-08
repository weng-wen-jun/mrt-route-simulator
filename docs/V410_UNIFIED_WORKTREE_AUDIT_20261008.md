# 工作樹核對與單一 V4.1.0（2026-10-08）

> 以下保留整合時的歷史路徑。後續依使用者指定遷回 `D:/AI/codex/mrt-route-simulator`；現行入口與備份見 [遷移紀錄](V410_CANONICAL_MIGRATION_20261008.md)。

## 唯一現行來源與執行入口

- 原始碼：`D:/AI/codex/mrt-v403-integration`，分支 `codex/v403-playback-integration`，HEAD `68084cbd39d32b02fab9f8718d29b4ef5d5fb88f` 加目前未提交整合內容。
- 桌面執行檔：`output/v4.1.0-unified/bin/MrtRouteSimulator.App/release/MRT路線進出站時間模擬器.exe`。
- MCP：`output/v4.1.0-unified/bin/MrtRouteSimulator.Mcp/release/MrtRouteSimulator.Mcp.dll`。
- `Directory.Build.props` 為唯一版本來源，MrtVersion=4.1.0；新App DLL實測FileVersion=4.1.0.0／ProductVersion=V4.1.0。
- 不把舊工作樹的4.0.2版本字串改成4.1.0來假裝整合，也不把歷史輸出複製覆蓋新版。舊工作樹／輸出／驗收證據保留，不刪除；之後只以以上入口開發及使用。

## 工作樹盤點

| 工作樹 | HEAD／分支 | 地位 |
|---|---|---|
| `D:/AI/codex/mrt-route-simulator` | `558e06b`／`codex/nearest-leader-index-prototype`，dirty4.0.2 | 舊原型及驗收歷史，保留 |
| `D:/AI/codex/mrt-v403-integration` | `68084cb`／`codex/v403-playback-integration`，dirty4.1.0 | 單一現行整合來源 |

Git worktree list只有上述兩個。共同祖先為`40e8ad1`；`558e06b`是舊PR merge節點，不能因版本或log相鄰便假定它是`68084cb`的祖先。先前4.0.3三方來源整合與衝突處理記錄在`V403_INTEGRATION_PROGRESS.md`，本輪以實際檔案及測試再次核對，不重新套用旧patch。無未解衝突項目，既有staged／unstaged內容不重置、不重新暫存。

舊工作樹source/tests dirty共36檔（12 tracked modified／24 untracked），整合樹36/36均有對應，9檔byte-identical／27檔不同／0 missing；主代理獨立重算清單及SHA結果相同。唯讀子代理比對舊dirty hunk、相鄰重構与先前三方整合紀錄，未發現尚未合入的獨有行為；SimulationWorld與Engine Program舊dirty修改均有保留。運行圖改為可取消背景準備、Route scrollbar改為shell proxy、collector測試增加ack race及sample新名稱均有對應，不把字面不同誤判為漏合。完整Engine／WPF回歸也通過，因此本轮無需再搬source/test或重套舊patch。

文件／raw驗收紀錄採歷史保存，不用舊QA覆蓋現行QA，也不恢复新版刻意刪除或已改名的文件／samples。這是既有三方整合內容的核對、唯一入口統一与新建置，不是新增Git merge commit。

## 不只 MCP

MCP功能本身是在4.0.3整合目錄上加入，沒有另外待合入的MCP工作樹；但目前單一版本同時保留先前累積的修改：

- nearest-leader index／shadow／diagnostics原型與測試，production預設full-scan oracle、安全與0.1秒步進不變。
- 播放pacing、結果增量累積、lazy／背景運行圖準備及原生collector／Play／Reset調整。
- 軟體介面縮放與偏好、800×520 DIP最小窗、依工作區比例的開啟尺寸、外層捲動、Route底緣頂層水平捲軸。
- 運行圖事件開關／方向篩選、兩軸獨立縮放、人工時間刻度／終點時間、固定時間軸、控制區與事件列表收折、警告關閉。
- PNG標籤防互蓋與PDF分頁／文字配置修正。
- MCP／Automation／opt-in同使用者named pipe，共用既有App操作與worker；不是另一套物理模型。

使用者指定對話`01a1111b-0db3-72d1-8009-120f000b469d`於本輪唯讀確認MCP來源與最後邊界。主代理核對MainWindow共用可等待Play／Pause／Reset與Loaded橋接入口、solution／App參照仍在；保留後續Reset移除重複Pause的優化，不倒退套回早期MCP實作。

## 統一建置與回歸

```powershell
dotnet restore MrtRouteSimulator.slnx --configfile src/MrtRouteSimulator.Mcp/NuGet.Config -p:ArtifactsPath=output/v4.1.0-unified
dotnet build MrtRouteSimulator.slnx -c Release --no-restore -p:ArtifactsPath=output/v4.1.0-unified
dotnet output/v4.1.0-unified/bin/MrtRouteSimulator.Tests/release/MrtRouteSimulator.Tests.dll
dotnet output/v4.1.0-unified/bin/MrtRouteSimulator.WpfTests/release/MrtRouteSimulator.WpfTests.dll .
python scripts/test-mcp.py --workspace-root . --server-dll output/v4.1.0-unified/bin/MrtRouteSimulator.Mcp/release/MrtRouteSimulator.Mcp.dll
```

上述本輪實際執行皆exit0：Release0警告／0錯誤，Engine198/198，完整default WPF PASS（含named pipe／播放／全sample／縮放／PNG／PDF），stdio initialize／tools/list／tools/call、15tools、三輪並行advance/reset PASS。Python使用Codex bundled runtime，非WindowsApps shim。

| 證據 | SHA256 |
|---|---|
| App DLL | `CBACEA7304CDBADC8DCEA4077EF34F94E65356896ECFEB382A7BB5C859515158` |
| MCP DLL | `74B16CF96E817BEB158C295B2D77C5D612AEDA250830D6FA06480C0C8257CF72` |
| build.log | `02BDD409375E8391E90D6DCA69326A42B2D89CFC3E53046A77D41DD694D7D316` |
| engine.log | `37BE6520ACF123474F680981C61AE218C9264238E0083FAA6F639D5837825DFC` |
| wpf.log | `2EAE07534ECEA3611BE488E3DC9D0B01C8E9BAD32A66000FCF70C1F82E37175D` |
| mcp-stdio.log | `1D734F05D5E502F03D6BB6E90350C827C7F14EA1D5B4DDC3669A6D9C920C5236` |

## MCP 連線統一

依[官方MCP設定文件](https://learn.chatgpt.com/docs/extend/mcp?surface=cli)，僅修改全域`C:/Users/wengw/.codex/config.toml`的MRT server args與本專案`.codex/config.toml`，使兩者指向本輪統一MCP DLL。逾時／權限／其他servers不變。TOML解析與`codex mcp get mrt-route-simulator --json`實際核對新路徑及enabled；移除MRT table後的其他全域設定指紋前後均為`54dc0f9ab99ae2cfa386ee16b163bd87ca3c4ed4281bc485a27c6f5a2fce4820`。

設定改好不代表目前已連線的舊MCP程序熱更新。本輪實際stdio驗證的是明確指定的新DLL；下次重新連線／新對話才會依新設定啟動，不強制停止其他對話的MCP或關閉現有App。

`scripts/test-mcp.py`預設亦改為先選統一輸出，不再自動選歷史`mcp-build`；歷史測試仍可明確指定`--server-dll`。修改後不帶`--server-dll`重跑亦exit0，15tools／三輪並行／10秒推進與Reset0通過；另直接assert choose_dll含`v4.1.0-unified`。`docs/MCP.md`建置／桌面／測試命令同步更新。

## 保留限制

本次統一來源／建置與自動化驗證，不是完整原生驗收。連續拉邊框即時重繪、cold／Reset嚴格互動延遲、stall／長期memory未知根因保留，不把建置或MCP PASS當成解決。最新125%／150%完整排版矩陣仍依使用者指示跳過。未建立commit／merge commit、tag、push或Release，也未刪除工作樹。
