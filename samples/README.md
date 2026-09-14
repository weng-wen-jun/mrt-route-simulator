# V4.0.1 可執行範例

2026-09-13：12份現行範例均補入實體接軌側別 `fromPortSide`／`toPortSide`。七種站型移除未宣告的多餘轉向；站後折返修正渡線端點與實際去回路徑。兩份V3.3及baseline的尾軌／袋狀軌新增各180m進出軌，因此運行時間可能較舊範例增加；baseline中央上行月台及西端連接一併修正。最新主圖／編輯器均無配線警告。

逐檔合理性、修正及運行結果見 [範例檢查表](AUDIT-2026-09-11.md)。路線圖以起始站月台中心0K顯示；七PDF及完整 topology 範例使用車體中心，其餘五檔保留車頭停點基準。完整 topology 範例於2026-09-12調整中央站月台所在軌段、袋狀軌進出道岔及端點渡線長度，最新驗證以 `QA_REPORT.md` 為準。舊 V3.4「雙島四股」檔案實際為三股道，已修正畫面名稱與配置說明，保留檔名相容；真正雙島四股範例為 `PDF-DoubleIslandFourTracks.mrtsim.json`。

此目錄內所有 `.mrtsim.json` 都是 `schemaVersion = 8` 的 topology-first 專案；可從桌面程式的「檔案 → 讀取存檔」直接載入，並直接按「播放」執行。它們不含 `Route`、legacy `Infrastructure`、`servicePatterns`、`serviceRuns` 或舊的 `turnbackStopAfterTraversalIndex` 欄位；所有折返停點均為實體 edge-local `turnbackStopPosition`，並明列 `directedConnections`。

| 檔案 | 情境 | 重點 |
|---|---|---|
| `V4.0.0-topology-baseline.mrtsim.json` | V4 基線 | 雙端單股雙向尾軌、中央單股雙向袋狀軌與實體越行線；無未使用分支，折返停點與每條合法轉向均已顯式化。 |
| `V4.0.0-完整拓撲執行驗證範例.mrtsim.json` | 完整 topology 執行 | 有向道岔、快速越行、中央袋狀軌折返、雙端 crossover 尾軌、rear-clear 資源釋放與結果資料流。 |
| `V3.3.0-完整功能驗證範例.mrtsim.json` | 五站完整營運 | 雙向手動班表、普通／快速停站模式、指定接續、端點退出、edge-local 速限與尾軌折返。 |
| `V3.3.0-端點站前與中間站中央避車線折返檢核.mrtsim.json` | 站後尾軌／袋狀軌折返 | 三筆派車及一次中間站反向產生四個方向別車次，完成兩次折返；舊檔名保留。 |
| `V3.4.0-雙島四股快速車越行驗證.mrtsim.json` | 三股道越行 | 普通車以 240 秒停站待避，快速車於 55 秒後發車並完成超越；實際三股道、一島一側，舊檔名保留。 |

檔名中的 V3.x 只保留作為既有範例入口與情境沿革；檔案內容已全面翻新為 V4 Schema 8 topology runtime 格式，不能再當作舊版存檔使用。

自動化測試會逐一反序列化所有 12 個現行範例（含下列七個 PDF 站型）、建立 topology-native `SimulationWorld` 並推進 3,600 秒；驗證沒有未使用 edge、legacy facility edge 欄位、未具名的續行班次、碰撞或停站違規，且所有列車均能完成退出。情境測試另確認快速車實際越行、實體折返與指定反向接續。數值均為合成測試資料，不代表真實路線或安全設計。

## PDF 站型範例

### 完整 topology 範例的新增待轉軌列車

2026-09-12 新增 `POCKET-02`（車次 `POCKET-DEMO-DOWN`），於 00:06:40 從西站發車，在中央站停靠後駛入中央袋狀待轉軌，再換端返回上行線往西站。實測於 00:09:13.3 抵達袋狀軌停點，停留30秒後於 00:09:43.3 開始返回；可在此時段暫停觀察。原有 `POCKET-01` 與雙端尾軌接續保留。更新後共7筆派車資料（含同車接續），不是7台不同實體車。

| PDF 頁 | 檔案 | 示範運行 |
|---|---|---|
| 2 | `PDF-IslandTwoTracks.mrtsim.json` | 島式二股，上下行全停 |
| 3 | `PDF-SideTwoTracks.mrtsim.json` | 外側月台二股，上下行全停 |
| 4 | `PDF-IslandAndSideThreeTracks.mrtsim.json` | 上行側線待避、快速車越行 |
| 5 | `PDF-DoubleIslandFourTracks.mrtsim.json` | 雙向側線待避、快速車越行 |
| 6 | `PDF-RearTurnback.mrtsim.json` | 站後尾軌折返、同車接續；另附第二尾軌進路 |
| 7 | `PDF-FrontTurnback.mrtsim.json` | 站前渡線、月台反向；另附第二月台進路 |
| 8 | `PDF-CentralPocket.mrtsim.json` | 中央軌停靠、對向等待車尾淨空；另附外側通過進路 |

第 1 頁的靠右行駛配置套用於共用路線圖。工作區快速建立也可產生上述七種站型。替代折返請把上下行綁定一起改為相同後綴的進路；發車月台會隨綁定更新。袋狀軌 `:BYPASS` 進路不在 B 站停車。
