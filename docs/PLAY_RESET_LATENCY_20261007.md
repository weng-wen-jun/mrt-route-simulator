# Play／Reset 互動延遲逐項處理紀錄

## 範圍

- 使用者要求逐項處理、節省額度；本輪只調查播放／重設互動延遲，不並行展開 memory、Route gap 或匯出矩陣。
- 目標是隔離整合來源 4.1.0；不覆寫原 4.0.3 binary，不合併、提交或發布。125%／150% 最新排版矩陣仍略過。
- 調查中，尚未修正或重新驗收，不預先列 PASS。

## 既有五輪證據拆解

來源：`artifacts/native-20261007/native-acceptance-20261007-050151286-b8f80c7863cd417180e4194bf0af5360.jsonl`。讀取 `inputAction` 的 `applicationVisualUpdate`，保留原始 WPF receipt→owner update 邊界；不是 OS injection→compositor。

| 輪次 | Play 輸入→handler ms | Play handler ms | Play 合計 ms | Reset 輸入→handler ms | Reset handler ms | Reset 合計 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 26.3130 | 41.1308 | 67.4921 | 27.6172 | 29.5031 | 57.1289 |
| 2 | 28.1410 | 38.4399 | 66.5899 | 29.5088 | 27.3902 | 56.9085 |
| 3 | 27.3659 | 46.9114 | 74.2828 | 27.3124 | 29.4851 | 56.8061 |
| 4 | 28.5034 | 40.3096 | 68.8187 | 27.7002 | 36.5567 | 64.2635 |
| 5 | 25.5854 | 41.3822 | 66.9733 | 28.0426 | 26.0330 | 54.0882 |

- 輸入→handler 包含 preview mouse-down 至 Click 開始的期間，不能將全部時間指認成播放 worker 或重繪成本；這也尚不足證明全由自動點擊按住時間造成。
- Play handler 範圍 38.4399～46.9114 ms；Reset handler 範圍 26.0330～36.5567 ms。原總延遲仍全部超過 50 ms，不改計時起點來宣稱通過。
- 單一低成本子代理調查 command acknowledgement、forced frame publication 及 Reset 重複工作，限 Presentation／worker 與功能回歸；主代理將核對改動並做隔離 build／tests。

## 待完成

- 確認可重現的成本來源與最小修正。
- 若有改動，隔離 Release build 與完整 Engine／WPF tests。
- 正常原生輸入重測；程式測試通過不能替代互動延遲驗收。

## 最小修正與驗證進度

- V2 正常 Reset 不再先排入 Pause；Reset 本身由同一 worker 有序停止並重設 world。停止 UI timer／playing 狀態後等待 Reset acknowledgement，仍套用初始 frame、清空 actual 結果及保留 planned 計畫圖。
- Play 只改 coordinator 的播放狀態／rate，移除重複 world snapshot 的強制發布；後續畫面仍按既有33ms週期由單一 writer 發布，Pause／Reset／AdvanceTo／模式設定保留立即 frame。第一個週期到來前，已持有 frame 的 performance rate 可能仍為舊值，不拿它作即時 acknowledgement 保證。
- 新增正常 WPF Reset sequence 僅增加1的功能回歸；worker連續 paused Reset 邊界僅增加1。初稿 Play→Pause sequence 斷言可能受33ms週期影響，主代理review後撤除，改採既有 deterministic AdvanceTo 檢查 rate與固定 tick，不用脆弱耗時斷言。
- 未改 Engine／Schema／版本／collector量測邊界。子代理修改由主代理讀取相關程式及回歸整合。
- 隔離輸出 `output/play-reset-latency-20261007`：restore成功，Release全solution build成功、0警告／0錯誤。完整Engine及WPF runner執行中，尚不列測試PASS。
- 新App DLL SHA256：`779C4E9840EFBB18DA83B29C70AE292610FAA749A41DCF1C2B3116570473143F`。原4.0.3 exe仍為 `66BC6230C149622A646DF2F8B924BF111EF43BCD3CD21FF177D9E9153840AFA0`。
- 目前是消除已確認重複工作的修正，不是總輸入延遲已達50ms的結論；collector自身成本尚未剖析。

## 本段交付結果

- 隔離完整 Engine runner：exit0，198/198通過、0失敗。
- 隔離完整 default WPF runner：exit0、PASS，包括新增command frame邊界與正常WPF Reset單frame回歸、播放pacing、MCP、原生collector、全sample、長行程與輸出回歸。
- 這些只證明修正後的功能／回歸通過，不是原生互動延遲達標。為依使用者要求逐項及節省額度，本段停在修正與完整回歸；下一段先做本修正版正常原生Play／Reset重測，不跳到其他三项。
- 最新執行檔：`output/play-reset-latency-20261007/bin/MrtRouteSimulator.App/release/MRT路線進出站時間模擬器.exe`。未啟動本版本的原生驗收、未替換使用者目前視窗。
- 本項狀態：最小修正與回歸完成；原生50ms總延遲gate仍待，不列整項PASS。

## 15:18 原生修正版重測

- 新版只啟動一次，HWND4397216；旧collector隔離版量測停止後正常關閉。sample14正常讀取、Diagram60×、96DPI、interfaceScale1、1728×1037DIP；不並行build/tests、不MCP/private advance、不forced GC。
- session `8692905d-f159-48e9-852a-a9d98c7f26d6`：正常Play，15:20:34.748+08完成7173.2s／1025events／37172trajectory／9147safety／active0；acknowledged-active倍率59.99867742，publication邊界非精確final tick。raw dispatcherStall0僅適用此Diagram單輪。
- 15:20:44.774合法10sIdle；15:21:25.920正常Reset，actualSeries／ordinary／critical／processed均0、planned8/version1、frame trajectory0／safety0／events1／time0、visual8/40/114。15:21:58.930正常Stop completedRuns1/activefalse。
- Play input→handler31.0521ms、handler47.6607ms、input→owner visual78.7588ms；Reset36.5728／30.5252／67.1094ms。總延遲仍>50ms，此重測不列PASS，不將單筆cold樣本判成變慢或改善。
- 完成frame sequence2892、afterReset2894之間仍含自動完成後Pause發布；不能只憑兩checkpoint的+2推論正常Reset又重複排Pause，應以正常Reset owner regression／command邊界判定。
- 原始兩檔非覆寫封存至 `artifacts/native-20261007/native-acceptance-20261007-071817783-8692905df15948e9852aa9d98c7f26d6{.jsonl,.events.jsonl}`，來源與副本hash一致：metrics `470C4254212954D7E4656141E66D276F6E7FB0DEA9B33287D55994C358E596B6`；events `0D3DECE6CD3BF19A735FB15099B763B1E4CF6432EFE6AD83AA4D4522F29FB718`。
- 仍逐項處理：委派同一子代理查opt-in診斷同步取樣成本，允許有證據的最小診斷修正／步驟計時，禁止挪計時邊界或延後baseline造成誤判。其他三項尚未展開。
- 主代理將1025筆事件與原4.0.3 d70a3e60 run1逐筆全欄位ordered JSON對照：1025/1025，mismatch0；只證明跨版本事件一致，不混合效能資料。單輪memory仍UNRESOLVED，不当成五輪gate。

## 分段計時版

- 同項續查未直接猜修瓶頸。加入opt-in owner phase durations，暫存在action state，沿用既有handlerEnd／visual JSONL輸出；沒有增加獨立writer往返、沒有把診斷取樣挪到計時結束後。
- Play分段：beforeDispatch、workerAwait、afterAcknowledged、statusUpdate；診斷內層分段：enable/reset、beforePlayMemoryCheckpoint、runArmedViewport/write、runtimeBaseline、runStartedViewport/write。
- Reset分段：lifecycleBoundary、workerAwait、uiApply、statusUpdate。workerAwait包住整個ResetV2PlaybackAsync（含ack後的cache清理），uiApply含正常更新及afterReset checkpoint；不能將這兩段全部解讀成Engine worker或渲染耗時。外層與內層是包含關係，不能把所有phase相加。
- 正常Play／Reset回歸要求各phase具有有限非負值，不以耗時門檻做脆弱斷言。
- `output/latency-phases-20261007` 隔離Release build0警告／0錯誤，完整Engine／WPF runner執行中；尚未原生重測，亦不宣稱效能PASS。
- 後續完整Engine runner exit0、198/198；完整default WPF exit0／PASS（含phase timings、原生collector、長行程、MCP及全部輸出）。
- 原生分段版只啟動一次HWND1513828，正常sample14／Diagram60×。session `4e8f25ab-0c41-4970-8fca-aa01c267aacd`，raw位於新版exe同目錄NativeAcceptanceLogs，stem `native-acceptance-20261007-073303475-4e8f25ab0c4149708fcaaa01c267aacd`。目前仍正常播放，不先列完成。
- Play handler48.3612ms、輸入→handler28.1708ms。內層beforePlayMemoryCheckpoint21.9678ms、runtimeBaseline21.6117ms；workerAwait2.7248ms、viewport0.0083/0.0254ms、runArmed/runStarted write0.0718/0.1373ms。主要同步成本已定位在兩次取樣，而非worker等待或viewport。這不代表GC本身造成延遲，也不把內外層phase相加。
- 同子代理接續查取樣內Process查詢，僅允許保持欄位語義、原baseline及checkpoint時點的等價本程序診斷讀取／安全fallback；不挪邊界、不以cache假數字。
- 分段輪次正常完成7173.1s／1025events／37172trajectory／9147safety／active0、raw gap0；15:35:30.931合法10sIdle後正常Reset actual四項0／planned8。Play總76.5986ms，Reset總76.1009ms（handler47.1786ms：resetV2整段26.8904、uiApply含checkpoint19.9387ms），仍未達50ms。
- 中途額度限制，worker取樣helper來源與新增回歸留下未建置狀態。其後17:32:09.343+08正常Stop completedRuns1／activefalse；不能把中斷後長時間保留session說成連續active播放或memory protocol。兩檔非覆寫封存hash一致：metrics `AFB7F9917B2E4DDEDB7F6591555D4F94BBC3321867556A1B359216FF85305472`、events `9F08804AE9938A3228C5F62ABDAB466A9C11961D350E50FB95B83C47625D939C`。
- 額度恢復後續同項。來源helper以Windows本程序GetProcessMemoryInfo直接取得WorkingSetSize／PrivateUsage，失敗或非Windows回退原Process properties；保留GC／heap欄位、取樣時點，沒有snapshot cache。原生改善尚未驗。
- 主代理核對結構與欄位：[Microsoft PROCESS_MEMORY_COUNTERS_EX](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex)、[GetProcessMemoryInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo)。新增mapping/positive-value smoke採依pointer size檢查，不對兩次即時讀取強求byte相等；fallback分支尚無故障注入回歸。
- 主代理已讀取中斷時新增回歸，檢查Windows native正值、x64/x86結構大小與WorkingSet/PrivateUsage offset、disabled runtime六欄sentinel。x64映射size80／WorkingSet offset16／PrivateUsage72；x86size44／12／40，按官方SIZE_T欄位計算，不使用錯誤的x86 offset36。
- `output/process-memory-fast-20261007` 隔離Release build成功、0警告／0錯誤；完整Engine／WPF執行中。原生修正版尚未量測，不列改善PASS。
- 此後完整Engine exit0／198/198、完整default WPF exit0／PASS（含新native memory mapping／sentinel回歸、phase timings及既有全部功能）；diff --check exit0，只見既有CRLF換行提示。新App DLL SHA256 `844CC2AB1CB8C4F3A9540D5D99EC2F7A6F8F665EBBD707F7C5DF88870B8D782B`；原4.0.3 exe/DLL hash均保持既有值。

## 快速本程序取樣原生重測（進行中）

- 只啟動一次修正版HWND1776634，正常載入sample14、Diagram60×。raw stem `native-acceptance-20261007-093800485-c392e6d2f6c742aeadc417e1fd5c73eb` 位於 `output/process-memory-fast-20261007/bin/MrtRouteSimulator.App/release/NativeAcceptanceLogs`。
- run1 Play總38.8407ms（輸入→handler27.5718、handler11.2075）。beforePlayMemoryCheckpoint2.7167ms、runtimeBaseline0.3754ms、workerAwait5.8315ms；保留原WPF receipt→owner邊界，未移走取樣。相較分段版兩次取樣約22/21.6ms，瓶頸改善有原生證據；單筆不宣稱整個互動gate PASS。
- 正常全程播放中；待合法Idle、Reset與暖機重播。沒有混同Engine效能改善或原4.0.3 binary已修復。
- run1 已正常完成7173.2s，17:40:29.474+08完成10sIdle；其後正常Reset總48.1902ms（輸入→handler26.7488、handler21.4303；reset.workerAwait14.5372、uiApply6.5535）。目前Play與Reset各一筆低於50ms，樣本仍不足；run2正常暖機播放已開始。
- run2 正常完成7173.3s，17:46:22.610+08合法10sIdle後正常Reset；Play總27.0224ms、Reset41.2933ms（handler14.5994、workerAwait9.7927、uiApply4.7713）。run3正常暖機播放開始，尚不列五輪完成。
- run3 正常完成7172.9s，合法10sIdle後正常Reset；Play總27.5460ms、Reset37.1375ms。run4正常播放已開始。
- run4 正常完成7172.9s，17:53:05.385+08合法10sIdle後正常Reset；Reset總50.2658ms，略超50ms，不能四捨五入為PASS。run5正常播放開始，待收完再拆解此筆。

## 快速本程序取樣五輪結論

| 輪次 | Play總 ms | Reset總 ms |
| --- | ---: | ---: |
| 1 | 38.8407 | 48.1902 |
| 2 | 27.0224 | 41.2933 |
| 3 | 27.5460 | 37.1375 |
| 4 | 26.4282 | 50.2658 |
| 5 | 28.7054 | 48.5928 |

- 五輪正常完成，各1025events／37172trajectory／9147safety／active0；全部與原4.0.3 d70a3e60 run1逐筆全欄位ordered JSON對照0差異。完成publication時間7173.2／7173.3／7172.9／7172.9／7172.9s，acknowledged-active-wall倍率59.996959～60.002341，非精確final tick邊界。
- 五次合法10sIdle後正常Reset，actualSeries／actualOrdinary／actualCritical／actualProcessed皆0；history trajectory0／safety0／events1／time0；planned8，visual8/40/114。run5曾索引點擊未觸發Reset（raw僅otherClick），重新觀察可見選單後座標點擊正常Reset，未重複執行；17:57:50.876+08 afterReset後自動Stop completedRuns5／activefalse。
- 限定生命週期、完整事件一致與快取重設PASS。Play五筆皆<50ms；Reset第四筆50.2658ms，嚴格max／本樣本nearest-rank p95仍未達50ms，不能將整體互動gate列PASS。第四筆input→handler32.0391ms＋handler18.2182ms；workerAwait13.3583／uiApply4.8133，尚無證据可把剩餘原因歸給GC／OS或另一個重複工作。
- dispatcherStall總7筆，runCompleted累計0／0／0／4／7（不是每輪各7筆）；max518.7174ms。早三輪無stall不能外推五輪無stall，更不能沖銷原Route 1.75s FAIL。未收GC／OS完整timeline，根因仍未知；五輪後段同時有唯讀子代理審查、但未並行build/tests，不能據此推定停頓因果。
- afterReset private MiB234.5742→260.1055→259.9609→259.3945→262.7500，heap69.3003→94.5788→73.8530→74.4618→72.6011，natural Gen2累計7→8→11→13→15；未forced GC。後段回落及快取清空支持未見單調保留，但五輪不足證明long-term bounded／排除洩漏，memory仍UNRESOLVED。
- 原始兩檔已非覆寫封存至artifacts/native-20261007，來源與副本SHA256需列於下一筆核對紀錄；4.1.0隔離版，不混合原4.0.3原生效能數據。Release與完整Engine198/198、default WPF PASS是回歸證據，不替代原生效能門檻。
- 主代理SHA256核對：metrics來源／副本均 `A837EE9DB1654A49BB2B81C2F1BE8CA0E4386D9B3E66EF0184E7F91B5AC242F1`；events均 `6018E1829E2252545289DBBFB6941DE0445417A695499085146E61965C1BA46B`。
- runStarted raw核對：96×96DPI／interfaceScale1／1728×1037DIP、workspaceIndex7 Diagram。原4.0.3 exe/DLL SHA256再核對均未變；diff --check exit0（既有CRLF提示）。

## 殘餘Reset重繪調查（進行中）

- 唯讀子代理指出ResetV2PlaybackAsync末尾兩次提前draw；主代理核對 `_latestPlaybackFrame=null` 後即 DrawSafetyDistanceChart／DrawTimeDistanceDiagram，先建立empty view，owner隨即UpdatePlaybackView讀reset frame再畫選中頁。有明確重複呈現工作，但尚不證明50.2658ms那筆的全部原因。
- 允許同子代理只移除這兩個提前draw並補正常owner Reset的Diagram／Safety及切回隱藏頁回歸；不改cache／frame／計時／取樣邊界，不改完成後自動Pause。auto-Pause雖有額外publication，本次皆完成後合法10sIdle才Reset，不能把它宣稱為此筆排隊根因。
- 新修正尚未建置／原生重測，嚴格互動gate仍待；其他待驗項目沒有被此調查列為完成。
- 後續子代理只移除兩個提前draw；在既有TimeDistanceVisualTests改用正常owner Reset驗證actual清空／planned保留／no-data與切頁刷新。主代理檢查並再補「Safety已選取時Reset」及「Diagram隱藏時Reset後切回」回歸，檢查舊Polyline清除及非空提示，不新增production hook或耗時threshold。
- `output/reset-render-once-20261007` 新隔離Release build成功0警告／0錯誤；完整Engine及default WPF runner執行中，未列測試PASS或原生效能改善。
- 後續完整Engine exit0／198/198、完整default WPF exit0／PASS，包括選中Safety及隱藏Diagram回歸。原生改善尚待，新版資料不得併入c392e6d2五輪。
- 新版App DLL SHA256 `7B708C004418084315F56AD1D3DFEE0B0D8926C6506B19467E29208FC5211C7A`。舊量測版正常關閉後只啟動一次HWND1645476，正常讀sample14／60×；session83c4baf6-7113-4b06-b49d-9b1cd279735b，raw stem `native-acceptance-20261007-101315408-83c4baf671134b06b49d9b1cd279735b` 位於reset-render-once隔離exe同目錄NativeAcceptanceLogs。
- 在新程序首次切入Diagram前啟用session，尚未Play；first-tab owner總55.5355ms（input→handler4.0466、handler51.3388），超50ms，單筆不列切頁效能PASS。這是讀檔與背景planned準備後的首次頁面開啟，不是首次程序載入全部成本；目前正常run1開始，未列完成。
- 新版run1完成7173.0s，合法10sIdle後正常Reset。Play總33.3631ms；Reset51.9069ms（input→handler31.5541／handler20.3429／workerAwait9.6684／uiApply10.3380），仍超50ms，不宣稱去提前draw後已達標。準備暖機輪次以區分first-use與重複Reset；舊版50.2658樣本不刪除。
- 新版run2正常完成7173.3s／合法10sIdle後Reset；Play26.8280ms、Reset36.8651ms（workerAwait3.9516／uiApply6.5900）。暖機此筆<50，首輪仍FAIL；不當成五輪／population結論。下一輪完成後先做完整資料的暖機切頁，再Reset；因此這個新session是互動調查，不是固定頁五輪memory protocol。
- 新版run3完成7173.0s／合法10sIdle，五次完整資料warm Diagram切頁及單次11DIP keyboard resize後正常Reset43.2529ms；Play27.6850ms。18:30:22.552+08正常Stop completedRuns3／activefalse；三輪各1025events／37172trajectory／9147safety／active0，ordered全欄位對照原4.0.3全部0差異。raw dispatcherStall2筆max427.9311ms、原因未知，不列零停頓。三次Reset四項actual都已清空；最後width1717×1037DIP，原width1728，96DPI／interface1。
- 限定重設功能及回歸PASS；去提前draw後cold Reset51.9069ms仍>50，不能列整項效能PASS。原始metrics／events兩檔已非覆寫封存，來源／副本hash一致（下列核對）。暖機切頁／resize詳見NATIVE_DIAGRAM_INTERACTION_20261007.md；首次Diagram55.5355ms仍未過，續以同owner token內部phase診斷，不改行為／計時邊界。
- 83c4baf6封存hash：metrics `D3656ED1262267C72CA3048B67017F13749AD0B5FD73E7ED17B7226173F6A3F9`；events `6C72FEBE5CAECC2BB71E488FFE9A7026748568CCE40BBD28B493EC710BCF0730`，來源／副本皆一致。
