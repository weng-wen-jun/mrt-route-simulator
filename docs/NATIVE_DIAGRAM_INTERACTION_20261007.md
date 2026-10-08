# 原生運行圖首次／暖機互動續驗

## 範圍與邊界

- 隔離4.1.0 `output/reset-render-once-20261007`；不覆寫、合併或混用原4.0.3。
- Release0警告／0錯誤，完整Engine198/198、default WPF PASS，這不是原生效能PASS。
- 原生session83c4baf6-7113-4b06-b49d-9b1cd279735b；正常讀sample14／60×。不MCP advance、不forced GC；125%／150%排版矩陣仍依使用者指示略過。
- owner input→applicationVisualUpdate保留WPF receipt起點，與surrogate、OS／compositor分開。

## 首次頁面開啟

- 本程序首次切入Diagram，在讀檔／背景planned準備後、尚未Play；不是整個程式從啟動到可用的延遲。
- owner總55.5355ms，input→handler4.0466ms、handler51.3388ms；單筆超50，仍未達標，不歸因GC或缺cache。
- 暖機完整資料切頁：第三輪正常完成後待測，尚不列PASS。尺寸／clock／frame、方向／vehicle／events／planned／actual設定需要隨結果核對。
- 第三輪正常完成7173.0s並合法10sIdle後，五次正常Route↔Diagram切頁；Diagram owner總0.9116／0.8608／0.8387／1.0057／1.0844ms，raw activeTab均workspaceIndex7、simulationTimeSeconds均7173.0；暖機完整資料限定子項PASS，不沖銷首次55.5355ms。
- 正常截圖確認：上下行皆顯示、all vehicle、planned/actual/events/endtime皆on、兩軸1、auto ticks、控制／事件展開，完整曲線及事件資料仍在；新binary全程不MCP/private advance。接續純resize，紀錄WM gesture-span／WPF surrogate不冒充owner handler／compositor present延遲。
- 邊角drag未改變尺寸（raw只有otherClick），不算resize證據；改由系統「大小」命令→Right選邊→Left縮小→Return完成，截圖實際寬度縮小、時間仍7173.0。raw action23收WM_ENTERSIZEMOVE／WM_EXITSIZEMOVE，messages=1。
- 此keyboard sizing模式跨數次工具觀察，整段span45,621.3385ms包含操作／觀察等待，不能當卡頓或handler延遲；EXITSIZEMOVE／handlerEnd後到WPF surrogate回呼約43.4961ms，仍非compositor呈現／逐幀live resize。只證明此單次小幅寬度變更能刷新曲線／固定時間軸且時間不變；大幅純resize及連續拖曳效能仍待，不列完整resize矩陣PASS。
- 使用者指出Alt+Space會開Google AI；本輪確曾用一次該快捷鍵嘗試系統選單，屬誤觸其他App全域shortcut。後續已直接點視窗系統選單，禁止在此機器再用Alt+Space；未操作Google AI內容，不把這段當App效能問題。
- 使用者隨後回報已解除安裝Google AI，預期解除快捷鍵衝突；未做獨立安裝狀態核對。此任務仍優先直接點選App／系統選單，不依賴該全域快捷鍵。

## 後續待驗

- phase-only來源已整合：保留UpdateV2PlaybackView(bool) wrapper，其餘caller token0；Workspace／nested owner傳自身token給Core。frameRead／accumulator／trainRows／eventRows／selectedChart／summary沿用既有action字典，readonly struct scope不box，沒有新active-token全域狀態。欄位只在對應工作實際執行時出現，不將所有phase相加當完整handler時間。
- `output/tab-input-phases-20261007` 隔離Release build0警告／0錯誤；主代理已讀source及測試，完整Engine／WPF執行中。尚未原生phase重測，不列瓶頸或效能PASS。
- 首輪完整Engine198/198；WPF在NativeAcceptanceInput測試失敗：要求`playback.summary`，但0.2s切頁情境的摘要未dirty、正常刷新條件未執行。此為測試情境與條件式phase契約不符，正在補明確dirty／clean情境；未改production刷新條件或量測邊界，未將失敗列PASS。
- 測試fixture修正：首次真實Workspace selection前明確設summary dirty；第二次切回Simulation驗證clean boundary省略summary phase。主代理核對source後重建0警告／0錯誤；production DLL SHA256 `3C3EA9C4447FF78C200CC53D7D3F0A8A0C85408460252AC74796695B5BCB226D`，完整WPF重跑中。舊83c4baf6量測已停止、舊原生視窗已確認關閉。
- 第二輪WPF停在既有Reset cache斷言：新增clean切頁將fixture留在Simulation，而運行圖hidden cache依既有契約在重訪時才刷新。主代理核對Reset／Draw owner後，在Reset前切回Diagram，恢復原本可見運行圖Reset情境；保留cache清除斷言、production不變。再次build0警告／0錯誤，完整WPF第三輪重跑中。
- 第三輪完整default WPF runner exit0／PASS WPF visual rules，NativeAcceptanceInput phase timings／cache reset通過；Engine仍為同production版本198/198。此為自動化回歸證據，不是原生首次切頁／compositor效能PASS。開始隔離原生phase重測，量測時不並行建置或測試。

## 原生首次切頁分段 2a7b2568

- 新隔離程序首次Diagram頁，sample14正常讀檔／背景planned已就緒，尚未Play；96DPI、interface1、1728×1037DIP。上下行／all vehicle、planned／actual／events／endtime皆on、axes1／auto、controls／events展開；原生截圖計畫虛線及固定時間軸正常，不MCP/private advance，不forced GC。
- owner input→applicationVisualUpdate `51.3548ms`，仍FAIL嚴格50ms；input→handler `4.2674ms`，handler `46.9418ms`。handler包含frameRead `0.4032`、accumulator `0.2133`、trainRows `0.3715`、eventRows `0.0097`、selectedChart `44.8912ms`。summary未dirty，phase合理省略；不要以handler<50冒充owner總PASS。
- 量測定位到selectedChart段，但未證明JIT、GC、OS或某內部繪圖子步驟為原因。下一步只拆細圖表內部分段，保留相同input／visual邊界，不先改資料流或曲線。
- session於18:56:05.178+08正常手動停止，completedRuns0、activefalse；此次只驗首次切頁，不作全程播放／記憶體／事件parity結論。原始JSONL已保存到`artifacts/native-20261007`，未覆寫歷史FAIL。
- source／archive SHA256一致：`DF744CDC6B4A9F57DE584B7185A8A5FF9EB51B8234FAD8113A5B806C47D8DB48`。原4.0.3 exe／dll hash再次核對未變，隔離原生視窗已停止並確認關閉。
- 後續phase-only更細分版本：保留兩個無參數Draw reflection wrapper，加入token-aware Core及viewportSync／incrementalData／layout（固定圖形）／series／events phase；只有實際執行區塊才記錄。主代理讀source並將測試必要viewportSync／incrementalData改為必須存在，其餘conditional段若出現須有限非負。沒有新writer、Engine／Schema／刷新邏輯／量測邊界變更；新隔離`output/diagram-draw-phases-20261007`建置／回歸待確認，不列效能改善。
- 細分版Release0警告／0錯誤、完整Engine198/198 exit0、完整default WPF exit0／PASS，包含NativeAcceptanceInput子phase測試。DLL SHA256 `27D7D8E6AE2B6ABA057E198667B3C70FF44541A040F880916538F580482D6414`；開始原生首次切頁續測，不並行tests/build。

## 原生首次繪圖細分 36d6f3fe

- 新隔離程序／相同sample14讀檔與planned已就緒後首次Diagram、尚未Play；60×設定、96DPI、interface1、1728×1037DIP、axes1、auto ticks、雙向／all vehicle、四顯示開關皆on、controls／events展開。截圖虛線／站點／固定時間軸正常。
- owner總`48.1223ms`、input→handler`3.5753ms`、handler`44.4025ms`；這一筆<50，僅單次觀察，不代表整個cold gate／歷史FAIL已修好。此版本只診斷分段，沒有效能修正。
- selectedChart`42.2457ms`包含viewportSync`0.0113`、incrementalData`0.1567`、固定圖形layout`13.1030`、series`25.2274`、events`2.0362ms`。frameRead`.4235`／accumulator`.2299`／trainRows`.3570`／eventRows`.0057`。子phase與selectedChart為包含關係，禁止相加成完整總延遲。
- series為本筆最大已量段，但25.2274ms混合DrawCache首次呼叫、WPF圖形建立與點集合更新；未證明是JIT、逐點Add、GC或OS排程。下一步先找可獨立隔離的局部benchmark，不能憑此直接移動cold工作或降低線條資料。
- 正常manual Stop19:05:49.804+08，completedRuns0／activefalse。raw JSONL非覆寫封存至`artifacts/native-20261007`，source／archive SHA256均`B03A9F2D8C611948FE28402BE7F5E617481BFAE9C5E345C1F10EB676BE4A7FF6`。不作全程parity／memory／compositor結論；最新125%／150%矩陣仍略過。
- 局部benchmark決策：既有`--profile-full-timedistance-only`量匯出full renderer，不能代表interactive；既有`--profile-playback-only`先選Diagram再Reset量正常播放，不能直接隔離首次Series建立。下一步新增test-only opt-in interactive首次／同frame暖機強制layout重建3次，核對相同cache generation、viewport／filters、點数／endpoints與clock，輸出SeriesVisuals及整段配置／GC（不冒稱phase allocation）。production不改、不forced GC、無原生PASS主張；此profile尚待source/build/test確認。
- 額度96%接續檢查點：所有native session已停止／原始量測已hash封存，最後細分原生視窗已確認關閉。局部profile必須核對cold/warm相同viewport，timingCount不能把deferred重畫混為單次。尚未交付／執行其source，不能列完成。下輪先核對此test-only方案／實作、隔離build與完整回歸，再局部量測；不要重新跑已完成的125%／150%略過矩陣，保留cold gate未全過／memory與Route stall根因未知。

## 額度末段既有interactive暖機診斷

- 新test-only局部profiler未改檔；子代理只交付方案。需要另外await `WaitForPlannedTimeDistanceCacheWarmupAsync`，不能將`WaitForPlannedTimeline`直接視為display cache已就緒；建置／執行一律使用隔離artifact DLL，不用default輸出來混入原4.0.3。排空deferred callbacks後必須檢查timing count，不能把多次draw當一次cold sample。
- 執行已完整驗證的`diagram-draw-phases-20261007` WPF DLL，`--profile-playback-only --profile-tab=TimeDistance samples/14-大型-二十八站完整營運範例.mrtsim.json`；exit0／PASS。原生量測已停止且原生視窗已關閉，沒有並行build/tests；runner是程式驅動的暖機／短播放profiler，不是native input／compositor驗收。
- 60×實際59.60465×，sim606.3s／wall10.1720252s，publish22.2178fps／apply19.3668fps，drop29；Input dispatcher probe gap max216.4629ms（excess196.4629ms），不代表已消除舊Route stall或原生互動延遲。
- TimeDistanceRender37筆p50 `1.2967`／p95 `3.68252`／max `11.0165ms`；SeriesVisuals37筆p50 `.2298`／p95 `1.10018`／max `10.2706ms`；IncrementalData37筆p50 `.5281`／p95 `2.37058`／max `4.3354ms`。此測段沒有StaticVisuals timing，不能拿暖機增量series取代cold建立25.2274ms的同資料重建比較。
- cache：actual3795筆／4groups、planned178992筆／8groups；max actual display691／critical130、max planned display1624／critical1217、max ordinary571。critical點明確允許超过ordinary600預算，不為效能丟棄必要transition／extrema；資料指出planned曲線建立非空小樣本，但不證明逐點Add是原因。
- 整段allocated600383096 bytes（572.6MiB，56.289MiB/s）、managed65364312／workingSet268312576 bytes、GC36/1/0；這是整個程序測段，不是series phase allocation，不能單憑GC或配置量歸因cold25ms或判memory bounded。
- runner附帶dropped-frame parity：normal consume741.5s／281frames，target740.6s／dropped25；physics／events／trajectory／safety／resources全相等。這是固定target deterministic parity，非本輪完整native1025events acceptance。
- 目前無新增performance production修正；下一段仍先建立同資料first/warm局部benchmark，不將單筆48.1223ms或暖機資料列整體cold gate PASS。
- 完整資料的暖機切頁與純resize；任何首次／暖機結論需分開，不以空資料切頁冒充late-run結果。
- 若仍超門檻，先做owner內部步驟量測再修正，不修改計時邊界洗PASS。
