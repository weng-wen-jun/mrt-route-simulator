# 可執行範例

目前共 14 份範例，依車站規模由小至大排列，同規模內再按軌道區段與設施複雜度排序。檔名開頭的兩位數是排序序號，不是版本號；檔案主名稱及程式顯示名稱均為中文。

逐檔合理性、修正及運行結果見 [範例檢查表](範例檢查表.md)。範例路線圖以起始站月台中心 0K 顯示；七種站場範例及完整拓樸範例使用車體中心，其餘範例保留車頭停點基準。最新驗證以 QA_REPORT.md 為準。

此目錄內所有 .mrtsim.json 都是 schemaVersion = 8 的 topology-first 專案，可從桌面程式的「檔案 → 讀取存檔」直接載入並播放。這些檔案不含 Route、legacy Infrastructure、servicePatterns、serviceRuns 或舊的 turnbackStopAfterTraversalIndex 欄位；折返停點均使用實體 edge-local turnbackStopPosition，並明列 directedConnections。

| 序號 | 範例檔案 | 規模 | 主要情境 |
|---:|---|---|---|
| 01 | 01-小型-三站雙股島式站場範例.mrtsim.json | 3 站、6 軌道區段、6 月台 | 雙股島式月台、上下行全停 |
| 02 | 02-小型-三站雙股側式站場範例.mrtsim.json | 3 站、6 軌道區段、6 月台 | 雙股側式月台、上下行全停 |
| 03 | 03-小型-三站快速越行範例.mrtsim.json | 3 站、6 軌道區段、7 月台 | 快速車越行 |
| 04 | 04-小型-三站三股島側待避範例.mrtsim.json | 3 站、7 軌道區段、7 月台 | 三股島側站場、快速車越行 |
| 05 | 05-小型-三站四股雙島待避範例.mrtsim.json | 3 站、8 軌道區段、8 月台 | 四股雙島站場、雙向待避 |
| 06 | 06-小型-三站站前折返範例.mrtsim.json | 3 站、10 軌道區段、6 月台 | 站前渡線折返 |
| 07 | 07-小型-三站尾軌袋狀軌折返範例.mrtsim.json | 3 站、10 軌道區段、6 月台 | 站後尾軌與中間袋狀軌折返 |
| 08 | 08-小型-三站中央袋狀軌範例.mrtsim.json | 3 站、11 軌道區段、5 月台 | 中央袋狀軌停靠、外側通過 |
| 09 | 09-小型-三站站後尾軌折返範例.mrtsim.json | 3 站、14 軌道區段、6 月台 | 站後尾軌折返、同車接續 |
| 10 | 10-小型-三站完整拓樸基準範例.mrtsim.json | 3 站、15 軌道區段、7 月台 | 完整設施拓樸基準 |
| 11 | 11-小型-三站完整拓樸運行範例.mrtsim.json | 3 站、15 軌道區段、7 月台 | 道岔、越行、袋狀軌與雙端尾軌運行 |
| 12 | 12-中型-五站完整營運範例.mrtsim.json | 5 站、14 軌道區段、10 月台 | 雙向班表、停站模式、指定接續與尾軌折返 |
| 13 | 13-中型-六站主要站示範範例.mrtsim.json | 6 站、12 軌道區段、12 月台 | 合成主要站路線與全程／快速車停站模式 |
| 14 | 14-大型-二十八站完整營運範例.mrtsim.json | 28 站、65 軌道區段、60 月台 | 合成長路線；非正式路線資料或工程設計 |

副檔名 .mrtsim.json 是程式載入格式所需，保留不變。

自動化範例驗收目前涵蓋 14 個現行範例（含下列七種站場範例）；一般範例以 topology-native `SimulationWorld` 推進 3,600 秒，檢查未使用 edge、legacy facility edge 欄位、未具名續行班次、碰撞、停站違規及列車退出。合成長路線 full scenario 建議使用 8,000 秒有界模擬時間，以涵蓋 O04／O13 越行、O20 折返與後續退出。這些 gate 與情境測試不代表任何正式路線、工程配線或安全設計。

## 大型範例情境資料說明

大型合成路線範例（建議 10 站以上，或含兩種以上特殊設施）必須在本節或同目錄 Markdown 維護一份可辨識的 Scenario Manifest。`.mrtsim.json` 是輸出／載入產物，不是大型 topology 的主要原始碼；建模應使用既有 scenario builder、factory 或局部 helper，並保留 Schema 8 topology-native runtime。

每份 Manifest 至少記錄：

- 案例名稱與層級：`minimal`、`operational` 或 `full`。
- 資料來源、車站與里程來源，以及哪些欄位是正式來源資料。
- 哪些欄位是「示範假設／synthetic test value／非正式設計值」。
- 已建模功能、尚未建模／刻意省略功能，以及使用中的 turnback、passing、crossover、pocket、tail 等特殊設施。
- 預期驗證情境、建議模擬時間或關鍵觀察時間點。
- 最後一次 Structural、Operational、Regression 驗證結果與已知限制。

目前本目錄的既有範例使用合成 topology 與 regression 情境。維護者於 2026-10-08 確認：大型機場線及其主要站簡化範例為抽象規劃，站碼、站序與里程作為案例設定及回歸對照，並非實際線路里程。未來若取得政府公開且可信的資料，將在確認使用條件後，註明來源、資料日期與更新範圍再逐步更新；目前不宣稱已按正式資料校準。

### 合成長路線主要站簡化範例 Manifest

- 案例與層級：`13-中型-六站主要站示範範例`，`minimal`；僅示範抽象主要站基線，不是完整路線。
- 資料性質：O01、O08、O11、O16、O20、O26 車站代碼及站距為抽象規劃設定，非實際線路里程；月台、車型性能、停站秒數與 O26 終點處理均為示範值。O26 快速車模式是這份 minimal baseline 的情境設定，不是 full sample 的最新規則。
- 已建模：六站雙向主線、Schema 8 topology、全程車停站模式、歷史 minimal 快速車模式（中間站停靠、O26 終點停靠）與基本手動班表。
- 尚未建模／刻意省略：O02～O07、O08a、O09～O10、O12～O15、O15a、O17～O19、O21～O25、正式里程與營運班表、越行、crossover、袋狀軌、尾軌及中間站折返。
- 預期驗證：Structural 讀檔／參照檢查、Operational 3,600 秒有界運行、Regression Release／Engine／WPF runners；最後一次結果以本次整合 `QA_REPORT.md` 為準。
- 限制：通過驗證只代表此 synthetic minimal baseline 可執行，不代表任何正式路線設計、站距、設施或安全能力。

### 合成長路線 Full Station Chain Manifest

- 案例與層級：`14-大型-二十八站完整營運範例` 的 Stage 1 為 `full station chain`；這是可重建、可載入的 Schema 8 topology sample，不是正式工程模型或完整營運情境。
- 資料性質與品質：站序、O01～O26 車站代碼及站心里程均作為抽象規劃案例設定，非實際線路里程，也未宣稱有政府正式資料背書；設施、營運與車輛參數為示範值。未來可信公開資料的更新原則見本節前述說明。
- 車站與路線情境：營運站鏈為 O01～O26，含 O08a、O15a，共 28 站；builder 順序即為 O01、O02、O03、O04、O05、O06、O07、O08、O08a、O09、O10、O11、O12、O13、O14、O15、O15a、O16、O17、O18、O19、O20、O21、O22、O23、O24、O25、O26。O00 是 future/reserved station，因沒有正式營運 chainage 或 service role，不加入 ServiceRoute。
- reference station-center chainage（m）：O01 190、O02 1,567、O03 2,197、O04 4,467、O05 5,067、O06 6,727、O07 7,677、O08 10,137、O08a 10,843、O09 11,773、O10 12,873、O11 14,278、O12 15,348、O13 16,208、O14 17,053、O15 17,737、O15a 18,878、O16 19,450、O17 20,728、O18 21,943、O19 23,103、O20 24,023、O21 24,453、O22 25,738、O23 26,388、O24 28,118、O25 28,873、O26 30,133。
- reference mainline projection：每段 edge 皆由相鄰站 center chainage 相減；O01–O26 為 `30,133 - 190 = 29,943 m`（29.943 km），O01–O20 為 23,833 m。特殊站內 edge 可另有 synthetic 實體長度，但不得改變主線 station-center projection。
- planning metadata：O01／O26 是端點；O05、O08、O11、O14、O16、O20 為重要轉乘／周邊節點。O08 規劃為 2 島 3 股，可供未來歷史 O08–O20 區間車研究；O15 的地下穿越條件、O16 的地下疊式月台（B2F 穿堂、B3F 上行、B4F 下行）、O24／O25 側式、O26 島式與站前迴車皆僅記為 metadata，不推導工程幾何。部分近距站對仍在存廢／調整檢討，現階段採 current reference alignment，不表示永久定案。
- 後續 synthetic operational scenario：
  - FULL-LINE：O01↔O26、站站停。
  - SECTION：O01↔O20，尖峰服務，O20 為營運終點並使用站後袋式儲車軌折返。
  - EXPRESS：O01↔O20，離峰服務，停靠 O01／O08／O11／O16／O20，O20 終止，不前往 O26。
  - 尖峰為 FULL-LINE＋SECTION；離峰為 FULL-LINE＋EXPRESS；參考規劃的代表班距、旅行時間、列車數、450 人／列及 O15a～O16 需求值只作合理性／容量目標，不作 exact-equality regression。
  - O04／O13 是 2 側式月台＋4 股道的雙向越行／待避概念；普通車駛入外側 `Siding` 側線停靠待避，高等列車保持在內側 `Mainline` 正線通過。O04 上下行均在月臺中心前 240 m 岔出、後 240 m 匯入；140 m 月臺在分岔後 170～310 m，中心為 240 m。O04 為高架、O13 為地下。1.5 分鐘是營運配置目標，不取代 moving block、occupancy、braking 或 rear-clear 安全模型。
- synthetic test values／非正式設計值：
  - 平台實際長度與停點、O04／O13 道岔位置與 crossover 幾何、O20 pocket 長度、O26 折返精確幾何。
  - Full sample 的停站語意明確使用 `StopPositionReference.TrainCenter`，並令停點等於月臺幾何中心；O01／O26 終端目的月臺以 100m synthetic platform 置於邊界前 50m，確保整列車仍在實體 edge 內。
  - `fromPortSide`／`toPortSide`、平台／列車長度與停點、車輛最高速率、加減速度、jerk、traction/coasting 參數。
  - 各站 dwell、O20 折返秒數、pocket／passing facility 長度、停點、速限、資源占用與釋放時間。
  - 代表性 dispatch offset 與普通停站 20 秒、指定車次待避條件；目前 builder 的 0／30／60／500／2,600／3,000／5,600 秒是 runtime regression input，不是正式班表。
- 已建模：28 個邏輯車站、上下行月台與主線 ServiceRoute、`directedConnections`、`directionRouteBindings`、FULL-LINE／SECTION／EXPRESS stop patterns、O20 topology-native pocket traversal、O04／O13 passing facility、VehicleId／ServiceRunId 接續，以及越行／rear-clear／折返／退出事件的 focused regression 情境。
- 刻意省略或尚未可工程化：平縱面、曲線／坡度／曲線限速、正式 turnout 型號與岔速、正式平台有效長度、正式車輛／號誌／閉塞參數、正式尖峰／離峰 timetable、fleet cycle／layover、完整容量客流模型及 O16 樓層／轉乘的列車 physics。O04／O13 的四股道只是 synthetic planning concept；目前可執行 facility 的細部幾何與 port side 仍屬 synthetic，不能視為工程配線。O04 的 `NODE:O04` 僅保留來源站心里程作顯示錨點，並非車輛行經的實體接點；上下行列車由各自的分岔／匯入節點及連續 480 m edge 通過站區。
- 代表性 runtime regression 班表：`FULL-O13` 於 0 秒、`FULL-UP-01` 於 30 秒、`FULL-O04` 於 60 秒、`EXPRESS-01` 於 500 秒、其上行接續於 2,600 秒、`FULL-SECTION-O04` 於 2,600 秒、`SECTION-DOWN-01` 於 3,000 秒、其上行接續於 5,600 秒。builder 將尖峰 SECTION 與離峰 EXPRESS 放在同一份代表班表，只為在單一 runtime regression 中觀察多服務、越行與折返；不表示三種服務是正式同時營運，也不表示這些 offset 是正式時刻。
- 預期關鍵事件：EXPRESS 依序完成 O04、O13 越行，再進入 O20 pocket 折返；SECTION 在 O04 越行後進入 O20 pocket 折返。O04／O13 待避模式以 `waitForOvertakeServiceRunId` 指定要等的車次，最低停站 20 秒；指定車次跨站通過後，仍須等車尾淨空、衝突資源釋放及安全許可才離開待避進路。舊存檔不含此欄位時沿用原停站秒數。FULL-LINE 持續至 O26。建議將完整 scenario 有界推進至 **8,000 秒**。O04 幾何更新後，舊版精確事件秒數不再適用。
- 目前驗證狀態（2026-09-25）：builder 的 7 個階段 Structural／Operational gate、Release solution build（0 warnings／0 errors）、完整 Engine runner **170/170**、O04／O13 雙向四股實體 topology、兩次越行與 rear-clear、Schema 8 round-trip 均通過。進站控制的下一步煞停預視及啟動後持續煞車已消除 65 m 設定下 full station chain、完整情境與 V3.3 sample 的 `StationStopViolation`。原生桌面不同 DPI、O04／O13 越行與 O20 折返的連續播放目視仍需驗證，不能用離屏 runner 代替。
- 已知限制：此案例的通過結果只代表 synthetic topology-native sample 可執行，以及目前列出的 runtime 事件可被驗證；不代表任何正式路線設計、實際站距、四股道配線、號誌安全能力、正式容量或營運時刻表。

## 七種站場範例

### 完整 topology 範例的新增待轉軌列車

2026-09-12 新增 `POCKET-02`（車次 `POCKET-DEMO-DOWN`），於 00:06:40 從西站發車，在中央站停靠後駛入中央袋狀待轉軌，再換端返回上行線往西站。實測於 00:09:13.3 抵達袋狀軌停點，停留30秒後於 00:09:43.3 開始返回；可在此時段暫停觀察。原有 `POCKET-01` 與雙端尾軌接續保留。更新後共7筆派車資料（含同車接續），不是7台不同實體車。

| 排序 | 範例檔案 | 示範運行 |
|---:|---|---|
| 01 | 01-小型-三站雙股島式站場範例.mrtsim.json | 島式月台二股道，上下行全停 |
| 02 | 02-小型-三站雙股側式站場範例.mrtsim.json | 外側月台二股道，上下行全停 |
| 04 | 04-小型-三站三股島側待避範例.mrtsim.json | 上行側線待避、快速車越行 |
| 05 | 05-小型-三站四股雙島待避範例.mrtsim.json | 雙向側線待避、快速車越行 |
| 06 | 06-小型-三站站前折返範例.mrtsim.json | 站前渡線、月台反向；另附第二月台進路 |
| 08 | 08-小型-三站中央袋狀軌範例.mrtsim.json | 中央軌停靠、對向等待車尾淨空；另附外側通過進路 |
| 09 | 09-小型-三站站後尾軌折返範例.mrtsim.json | 站後尾軌折返、同車接續；另附第二尾軌進路 |
第 1 頁的靠右行駛配置套用於共用路線圖。工作區快速建立也可產生上述七種站型。替代折返請把上下行綁定一起改為相同後綴的進路；發車月台會隨綁定更新。袋狀軌 `:BYPASS` 進路不在 B 站停車。
