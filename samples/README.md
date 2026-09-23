# V4.0.2 可執行範例

2026-09-13：當時的13份現行範例均補入實體接軌側別 `fromPortSide`／`toPortSide`。七種站型移除未宣告的多餘轉向；站後折返修正渡線端點與實際去回路徑。兩份V3.3及baseline的尾軌／袋狀軌新增各180m進出軌，因此運行時間可能較舊範例增加；baseline中央上行月台及西端連接一併修正。最新主圖／編輯器均無配線警告。2026-09-20 新增大型機場線 full scenario 後，現行總數為14份。

逐檔合理性、修正及運行結果見 [範例檢查表](AUDIT-2026-09-11.md)。路線圖以起始站月台中心0K顯示；七PDF及完整 topology 範例使用車體中心，其餘五檔保留車頭停點基準。完整 topology 範例於2026-09-12調整中央站月台所在軌段、袋狀軌進出道岔及端點渡線長度，最新驗證以 `QA_REPORT.md` 為準。舊 V3.4「雙島四股」檔案實際為三股道，已修正畫面名稱與配置說明，保留檔名相容；真正雙島四股範例為 `PDF-DoubleIslandFourTracks.mrtsim.json`。

此目錄內所有 `.mrtsim.json` 都是 `schemaVersion = 8` 的 topology-first 專案；可從桌面程式的「檔案 → 讀取存檔」直接載入，並直接按「播放」執行。它們不含 `Route`、legacy `Infrastructure`、`servicePatterns`、`serviceRuns` 或舊的 `turnbackStopAfterTraversalIndex` 欄位；所有折返停點均為實體 edge-local `turnbackStopPosition`，並明列 `directedConnections`。

| 檔案 | 情境 | 重點 |
|---|---|---|
| `V4.0.0-topology-baseline.mrtsim.json` | V4 基線 | 雙端單股雙向尾軌、中央單股雙向袋狀軌與實體越行線；無未使用分支，折返停點與每條合法轉向均已顯式化。 |
| `大型機場線-主要站簡化可執行範例.mrtsim.json` | 大型機場線主要站 `minimal` baseline | O01／O08／O11／O16／O20／O26 六站 synthetic topology；全程車與機場直達停站模式，未宣稱正式路線資料。 |
| `大型機場線-完整營運示範範例.mrtsim.json` | 大型機場線 Stage 1 `full station chain` | O01～O26（含 O08a／O15a）28 站 source-backed chainage 主線；既有 FULL-LINE／SECTION／AIRPORT-DIRECT、O20 pocket 與 O04／O13 情境仍是後續 synthetic regression，不得視為完成的正式營運方案。 |
| `V4.0.0-完整拓撲執行驗證範例.mrtsim.json` | 完整 topology 執行 | 有向道岔、快速越行、中央袋狀軌折返、雙端 crossover 尾軌、rear-clear 資源釋放與結果資料流。 |
| `V3.3.0-完整功能驗證範例.mrtsim.json` | 五站完整營運 | 雙向手動班表、普通／快速停站模式、指定接續、端點退出、edge-local 速限與尾軌折返。 |
| `V3.3.0-端點站前與中間站中央避車線折返檢核.mrtsim.json` | 站後尾軌／袋狀軌折返 | 三筆派車及一次中間站反向產生四個方向別車次，完成兩次折返；舊檔名保留。 |
| `V3.4.0-雙島四股快速車越行驗證.mrtsim.json` | 三股道越行 | 普通車以 240 秒停站待避，快速車於 55 秒後發車並完成超越；實際三股道、一島一側，舊檔名保留。 |

檔名中的 V3.x 只保留作為既有範例入口與情境沿革；檔案內容已全面翻新為 V4 Schema 8 topology runtime 格式，不能再當作舊版存檔使用。

自動化 sample gate 目前涵蓋 14 個現行範例（含下列七個 PDF 站型）；一般範例以 topology-native `SimulationWorld` 推進 3,600 秒，檢查未使用 edge、legacy facility edge 欄位、未具名續行班次、碰撞、停站違規及列車退出。大型機場線 full scenario 建議使用 8,000 秒有界模擬時間，以涵蓋 O04／O13 越行、O20 折返與後續退出。這些 gate 與情境測試不代表任何正式路線、工程配線或安全設計。

## 大型 sample Scenario Manifest

大型真實路線 sample（建議 10 站以上，或含兩種以上特殊設施）必須在本節或同目錄 Markdown 維護一份可辨識的 Scenario Manifest。`.mrtsim.json` 是輸出／載入產物，不是大型 topology 的主要原始碼；建模應使用既有 scenario builder、factory 或局部 helper，並保留 Schema 8 topology-native runtime。

每份 Manifest 至少記錄：

- 案例名稱與層級：`minimal`、`operational` 或 `full`。
- 資料來源、車站與里程來源，以及哪些欄位是正式來源資料。
- 哪些欄位是「示範假設／synthetic test value／非正式設計值」。
- 已建模功能、尚未建模／刻意省略功能，以及使用中的 turnback、passing、crossover、pocket、tail 等特殊設施。
- 預期驗證情境、建議模擬時間或關鍵觀察時間點。
- 最後一次 Structural、Operational、Regression 驗證結果與已知限制。

目前本目錄的既有範例仍是去識別化合成測試資料，不能因通過自動化測試而宣稱重現任何正式路線；若未來新增真實案例，必須先把正式來源與 synthetic values 分開記錄。

### 大型機場線主要站簡化範例 Manifest

- 案例與層級：`大型機場線-主要站簡化可執行範例`，`minimal`；僅示範抽象主要站基線，不是完整路線。
- 資料來源：目前只使用專案提供的情境名稱與站號；沒有把外部正式路線資料寫入模型。O01、O08、O11、O16、O20、O26 的站距、月台、車型性能、停站秒數與 O26 終點處理均為 synthetic test values；O26 direct 是這份歷史 minimal baseline 的 synthetic 行為，不是 full sample 的最新規則。
- 已建模：六站雙向主線、Schema 8 topology、全程車停站模式、歷史 minimal 機場直達模式（中間站停靠、O26 終點停靠）與基本手動班表。
- 尚未建模／刻意省略：O02～O07、O08a、O09～O10、O12～O15、O15a、O17～O19、O21～O25、正式里程與營運班表、越行、crossover、袋狀軌、尾軌及中間站折返。
- 預期驗證：Structural 讀檔／參照檢查、Operational 3,600 秒有界運行、Regression Release／Engine／WPF runners；最後一次結果以本次整合 `QA_REPORT.md` 為準。
- 限制：通過驗證只代表此 synthetic minimal baseline 可執行，不代表任何正式路線設計、站距、設施或安全能力。

### 臺中機場捷運 Full Station Chain Manifest

- 案例與層級：`大型機場線-完整營運示範範例` 的 Stage 1 為 `full station chain`；這是可重建、可載入的 Schema 8 topology sample，不是正式工程模型或完整營運情境。
- 資料來源與品質：`外部檔案參考/CODEX_TASK_Taichung_full_station_chain_source_data.md` 的車站中心里程為規劃參照資料。只有本節明列的站序、chainage 與由其相減的主線投影距離可稱為 source-backed；其餘 runtime／設施數值仍為 synthetic。
- 車站與路線情境：營運站鏈為 O01～O26，含 O08a、O15a，共 28 站；builder 順序即為 O01、O02、O03、O04、O05、O06、O07、O08、O08a、O09、O10、O11、O12、O13、O14、O15、O15a、O16、O17、O18、O19、O20、O21、O22、O23、O24、O25、O26。O00 是 future/reserved station，因沒有正式營運 chainage 或 service role，不加入 ServiceRoute。
- source-backed station-center chainage（m）：O01 190、O02 1,567、O03 2,197、O04 4,467、O05 5,067、O06 6,727、O07 7,677、O08 10,137、O08a 10,843、O09 11,773、O10 12,873、O11 14,278、O12 15,348、O13 16,208、O14 17,053、O15 17,737、O15a 18,878、O16 19,450、O17 20,728、O18 21,943、O19 23,103、O20 24,023、O21 24,453、O22 25,738、O23 26,388、O24 28,118、O25 28,873、O26 30,133。
- source-backed mainline projection：每段 edge 皆由相鄰站 center chainage 相減；O01–O26 為 `30,133 - 190 = 29,943 m`（29.943 km），O01–O20 為 23,833 m。特殊站內 edge 可另有 synthetic 實體長度，但不得改變主線 station-center projection。
- planning metadata：O01／O26 是端點；O05、O08、O11、O14、O16、O20 為重要轉乘／周邊節點。O08 規劃為 2 島 3 股，可供未來歷史 O08–O20 區間車研究；O15 的地下穿越條件、O16 的地下疊式月台（B2F 穿堂、B3F 上行、B4F 下行）、O24／O25 側式、O26 島式與站前迴車皆僅記為 metadata，不推導工程幾何。部分近距站對仍在存廢／調整檢討，現階段採 current reference alignment，不表示永久定案。
- 後續 synthetic operational scenario：
  - FULL-LINE：O01↔O26、站站停。
  - SECTION：O01↔O20，尖峰服務，O20 為營運終點並使用站後袋式儲車軌折返。
  - AIRPORT-DIRECT：O01↔O20，離峰服務，停靠 O01／O08／O11／O16／O20，O20 終止，不前往 O26。
  - 尖峰為 FULL-LINE＋SECTION；離峰為 FULL-LINE＋AIRPORT-DIRECT；參考規劃的代表班距、旅行時間、列車數、450 人／列及 O15a～O16 需求值只作合理性／容量目標，不作 exact-equality regression。
  - O04／O13 是 2 側式月台＋4 股道的雙向越行／待避概念；普通車駛入外側 `Siding` 側線停靠待避，高等列車保持在內側 `Mainline` 正線通過。O04 為高架、O13 為地下。1.5 分鐘是營運配置目標，不取代 moving block、occupancy、braking 或 rear-clear 安全模型。
- synthetic test values／非正式設計值：
  - 平台實際長度與停點、O04／O13 道岔位置與 crossover 幾何、O20 pocket 長度、O26 折返精確幾何。
  - Full sample 的停站語意明確使用 `StopPositionReference.TrainCenter`，並令停點等於月臺幾何中心；O01／O26 終端目的月臺以 100m synthetic platform 置於邊界前 50m，確保整列車仍在實體 edge 內。
  - `fromPortSide`／`toPortSide`、平台／列車長度與停點、車輛最高速率、加減速度、jerk、traction/coasting 參數。
  - 各站 dwell、O20 折返秒數、pocket／passing facility 長度、停點、速限、資源占用與釋放時間。
  - 代表性 dispatch offset 與待避 dwell；目前 builder 的 0／30／60／500／2,600／3,000／5,600 秒是 runtime regression input，不是正式班表。
- 已建模：28 個邏輯車站、上下行月台與主線 ServiceRoute、`directedConnections`、`directionRouteBindings`、FULL-LINE／SECTION／AIRPORT-DIRECT stop patterns、O20 topology-native pocket traversal、O04／O13 passing facility、VehicleId／ServiceRunId 接續，以及越行／rear-clear／折返／退出事件的 focused regression 情境。
- 刻意省略或尚未可工程化：平縱面、曲線／坡度／曲線限速、正式 turnout 型號與岔速、正式平台有效長度、正式車輛／號誌／閉塞參數、正式尖峰／離峰 timetable、fleet cycle／layover、完整容量客流模型及 O16 樓層／轉乘的列車 physics。O04／O13 的四股道只是 synthetic planning concept；目前可執行 facility 的細部幾何、port side 與方向對稱性仍屬 synthetic，不能視為工程配線。
- 代表性 runtime regression 班表：`FULL-O13` 於 0 秒、`FULL-UP-01` 於 30 秒、`FULL-O04` 於 60 秒、`AIRPORT-DIRECT-01` 於 500 秒、其上行接續於 2,600 秒、`FULL-SECTION-O04` 於 2,600 秒、`SECTION-DOWN-01` 於 3,000 秒、其上行接續於 5,600 秒。builder 將尖峰 SECTION 與離峰 AIRPORT-DIRECT 放在同一份代表班表，只為在單一 runtime regression 中觀察多服務、越行與折返；不表示三種服務是正式同時營運，也不表示這些 offset 是正式時刻。
- 預期關鍵事件（目前 deterministic regression 實測）：AIRPORT-DIRECT 約於 624.5／676.3 秒完成 O04 越行請求／完成、1,252.0／1,303.8 秒完成 O13 越行請求／完成、1,858.1 秒抵達 O20 pocket 停點、2,600.0 秒換為上行車次；SECTION 約於 3,124.5／3,176.3 秒完成 O04 越行請求／完成、4,704.5 秒抵達 O20 pocket 停點、5,600.0 秒換為上行車次，並於 7,446.5 秒退出營運。普通車須等 express rear-clear 後離開待避進路；FULL-LINE 持續至 O26。建議將完整 scenario 有界推進至 **8,000 秒**。
- 目前驗證狀態（2026-09-22）：builder 的 7 個階段 Structural／Operational gate、focused LargeAirportLine scenario tests 17/17、Release build（0 warnings／0 errors）及完整 Engine runner 170/170 通過；WPF 離屏 runner 的其他已執行範例通過，但本 sample editor-720 仍在 `EDGE:PASS-002` 回報 46.3° 示意突折，14-sample WPF gate 尚未完成。原生桌面不同 DPI、O04／O13 越行與 O20 折返的連續播放目視仍未執行，不能用離屏 runner 代替。
- 已知限制：此案例的通過結果只代表 synthetic topology-native sample 可執行，以及目前列出的 runtime 事件可被驗證；不代表任何正式路線設計、實際站距、四股道配線、號誌安全能力、正式容量或營運時刻表。

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
