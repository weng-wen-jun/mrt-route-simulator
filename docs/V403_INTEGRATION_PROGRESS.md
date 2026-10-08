# 4.0.3 基底整合與驗證紀錄

> 2026-10-02 後續：使用者新增軟體介面縮放要求，已在本隔離4.0.3工作樹完成顯示設定入口、偏好保存與Speed小視窗捲動修正；Release/Engine198/198/完整WPF通過。見[介面縮放紀錄](INTERFACE_SCALE_PROGRESS.md)。原生驗收仍依使用者停點暫停，沒有自動恢復。

## 範圍與保全（2026-10-01）

- 使用者授權先驗證及合併，確認後才恢復原生驗收。本輪不推送、打標籤或發布，也不將舊版驗收移植成新版 PASS。
- 原工作樹：`D:/AI/codex/mrt-route-simulator`，分支 `codex/nearest-leader-index-prototype`，HEAD `558e06b534f97c997f450379f747e7709528ac17`，版本4.0.2。新版未提交修改與原生執行程序保留，未切分支／stash／reset／clean。
- 備份：`D:/AI/codex/mrt-integration-backup-20261001`，保全83個 modified/untracked paths；另保存本輪 src/tests/TODO 的完整三方合併 patch。未覆蓋既有備份。
- 隔離工作樹：`D:/AI/codex/mrt-v403-integration`，detached HEAD `68084cb`，包含4.0.3 tag提交 `d93de6b` 與之後Route跟隨／水平縮放。App工作樹工具因擁有權檢查失敗，改用command-scoped safe.directory建立，無全域Git設定變更。
- 新版 main 尚停4.0.2，不是本輪基底；v4.0.3遠端tag仍指向d93de6b。不是修改版本字串來偽裝整合。

## 合併策略與衝突

- 新版已提交內容為主，三方套入本次dirty src/tests/TODO patch；複製新增程式／測試／原生證據，遇到不同內容的同名新版檔案即停止。QA_REPORT保留為歷史檔案，**其4.0.2測試與原生記錄不能代表整合版**。不重建新版曾刪除的MODEL_SPEC等文件。
- 六個衝突檔：TODO、MainWindow.PlaybackRefresh、MainWindow.V2、SimulationWorld、Engine Program、WPF Program。
- TODO保留新版大型工作區待辦與本次站體水平縮放待辦。
- 核心TickCore融合diagnostics的try/finally時，將新版Siding／Facility switch rear-clear與Abandoned passing reservation釋放放回原序列內，僅執行一次collision/safety/trajectory，避免重複記錄或遺漏新版資源生命週期。
- UI子代理限定兩個partial檔，保留Route follow/zoom/preferences/進路顯示與本次pacing、lazy/incremental TimeDistance、DPI、native diagnostics；主代理審核diff。
- 測試入口需取雙方集合，適配新版正式sample名稱，不把舊sample複製覆蓋新版營運truth。Nearest-leader仍Oracle production default；indexed-only僅test/benchmark opt-in。

## 驗證狀態

- Restore已通過。Release build、完整Engine/WPF回歸尚待整合衝突全數處理後執行。
- 原生驗收暫停：Speed展開摘要裁切、DPI/input/memory/O04/O13/O20/60x gates待整合版驗證，不沿用舊event秒數或舊測試數宣稱新版完成。
- 原工作樹既有修改尚未替換；本工作樹未commit/push/tag/release。

### 第一輪整合驗證

- Engine與App單獨Release build，以及完整solution Release build均PASS，0warnings/0errors；DLL FileVersion4.0.3.0 / ProductVersionV4.0.3，Schema8不變。原工作樹83個備份內容比對零差異。
- 核心唯讀審查確認新版physicalTurnbackOrigin、facility速度連續性、指定待避/進路預留/rear-clear仍在；唯一手動核心衝突依四個release原序列融合。App審查追加修復incremental事件marker漏掉新版direction/VehicleId篩選，與full/export renderer一致。
- Engine第一輪196/198 PASS，2FAIL：新增indexed-only與shadow大型sample assertions仍硬編AIRPORT-DIRECT-01，與新版sample身分不符。正式新版營運/安全/速度連續性/預留釋放等cases通過；正在適配新增測試，不改sample或降低事件gate。
- WPF第一輪FAIL：14sample主圖/工作區載入、4.0.3大型編輯workspace往返與輸入頁、pacing/label/compact已通過；PlaybackWorkerTests等待SetBrakingEstimationModeAsync期間Polyline配置PositiveInfinity錯誤。正在診斷App合併layout問題，未將局部PASS當成完整PASS；原生驗收仍暫停。

### 第二輪 Engine gate

- 新增測試由新版文件的 pocket facility → station operation → turnback stop pattern → 初始派車取得預期 VehicleId，排除在 O20 起始的接續列；不再硬編舊 AIRPORT-DIRECT-01。仍要求恰好兩輛來源車，各自具備抵達／返回／換端事件，並保留 8000s 有界完成、零碰撞／停站違規及 O04/O13 實體越行 assertions。
- Engine test project Release build PASS，0 warnings / 0 errors；完整 Engine runner **198/198 PASS，0 failed**，包含新版原有185 cases及本次13 cases。production nearest-leader 仍 oracle。
- WPF finite viewport width fallback 修正後，重新建置 PASS，但 `--playback-only` 仍在 Close 排空 dispatcher 時報 Polyline PositiveInfinity；此修正尚不足，繼續診斷。不得宣稱 WPF／整合 gate 已完成。

### WPF 定位與重跑

- 診斷輸出確認真正來源是 RouteCanvas：Width `5.9815259810321214E+38`、ActualWidth `2.9907629905160607E+38`，Polyline badPoints=0。未顯示 ScrollViewer 的超大 finite measure sentinel 仍會使 WPF 線條 DesiredSize 溢位；先前單純 IsFinite 防護不足。暫時診斷 instrumentation 已移除，沒有吞掉例外的 test handler 留下。
- App-only 有效尺寸防護拒絕 Infinity／超大 finite sentinel，正常站距與水平縮放保持；追加 Route width math sentinel vectors 與 TimeDistance incremental/full/deferred callback 的非空、finite geometry regression。後者 viewportBeforeArrange=0，屬未配置視窗 proxy，不宣稱 OS DPI。
- 修正後 `--playback-only` 與 `--pacing-lazy-only` PASS；完整 Release build PASS，0 warnings / 0 errors。
- 完整 WPF 第二輪已通過 worker 與所有14sample load/layout/新版workspace/input pages；再現既有 NativeAcceptanceInput test 固定80ms等待後的 playing-state assertion FAIL。正在以有界等待正常 async Play acknowledgment 修正 test synchronization，不改 production、不直接設定playing、不削弱stall/schema assertions。完整WPF尚未PASS。

### 本地合併狀態

- 六個衝突檔已經主代理審查，explicit `git add` 標記 resolved，`git ls-files -u` 為空；建立本地分支 `codex/v403-playback-integration`，HEAD仍 `68084cb`。這是未提交來源整合，沒有 merge commit／commit／push／tag／release。
- 原工作樹HEAD仍 `558e06b`，83個備份檔再次SHA256比對0差異。原目錄exe仍是舊4.0.2，勿拿它驗收新版。
- 整合版exe：`D:/AI/codex/mrt-v403-integration/src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe`。對應DLL FileVersion4.0.3.0／ProductVersionV4.0.3；WPF gate通過前不啟動原生驗收。

### 整合 gate 通過

- 最終完整solution Release build PASS，0 warnings / 0 errors；完整 Engine 198/198 PASS；最終完整 WPF runner exit0／PASS WPF visual rules。涵蓋14sample layout/load、新版大型workspace/input pages、worker、pacing/lazy/labels/resize/compact、opt-in native diagnostics、長行程與CSV/PNG/PDF。
- NativeAcceptanceInput test 改為最多5s、dispatcher pump等待正常Play acknowledgment，必須同時 run.IsPlaying 與 App._isV2PlaybackPlaying，否則逾時FAIL並列狀態；沒有設定playing或跳過assertions。5s是測試同步上限，**不是native responsiveness驗收數據**。
- 主代理新增NaN、Infinity、實測超大finite sentinel的Route width regression，仍要求26站/2x寬5024；正常站距／縮放vectors保留。TimeDistance未配置視窗測試各renderer必須產生非空且finite軌跡。
- 本地來源合併／自動化gate已完成，可開始新4.0.3原生驗收；整體native gate仍NOT COMPLETED，舊Speed clipping仍待新版確認。沒有把離屏pacing59.1x當成native60x sanity。

### 原生恢复與使用者停點（2026-10-01，23:06後）

- 使用computer-use啟動上述4.0.3隔離exe，實際窗口id1905336／標題V4.0.3；原4.0.2窗口1053604保留，未強制關閉。
- 初次觀察間有窗口大小變動與舊sample載入（不歸因為代理操作）。重新取得狀態，透過正常讀檔對話框載入隔離目錄的 `samples/14-大型-二十八站完整營運範例.mrtsim.json`；標題／CurrentProjectFile／就緒狀態確認正確來源，clock0.0s。
- 正常選單啟用 opt-in session `7baa84d7292e4446b12aca3a4959f972`，本地檔 `src/MrtRouteSimulator.App/bin/Release/net10.0-windows/NativeAcceptanceLogs/native-acceptance-20261001-150621862-7baa84d7292e4446b12aca3a4959f972.jsonl` 及events檔仍0bytes（尚未正常Play／未flush）。**未按Play、未開始60x sanity、未取得新DPI/input/memory/event驗收結果**。
- 使用者要求「驗收作業先這樣，明天再繼續」後立即停止所有UI輸入，沒有更改倍率／分頁／DPI，也未操作停止量測。session保持待命、模擬仍0s；不建立自動續跑／提醒。明天由使用者返回後重新觀察實際窗口／clock／session，不能使用舊element indices。
- 下一輪使用4.0.3整合exe＋sample14重新建立native證據；先取得真實DPI telemetry，確認Speed窄短裁切，完成60x sanity／同頁5輪memory／input／O04-O13-O20 visual／100-125-150 DPI等剩餘gate。若Speed最小修正需新授權，先回報，不默默擴大。
