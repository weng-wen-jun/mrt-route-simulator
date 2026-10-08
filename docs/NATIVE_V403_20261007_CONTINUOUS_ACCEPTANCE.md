# V4.0.3 後續原生驗收（2026-10-07）

## 跨頁互動 session：a4b4d3f3（已完成並停止）

- 原 V4.0.3／HWND 3217662，sample14，96 DPI、interfaceScale1，正常 UI 30×；本輪不使用 MCP 推進、不並行 build、不強制 GC。
- 原始紀錄：`NativeAcceptanceLogs/native-acceptance-20261007-042652188-a4b4d3f3c1464e09a06c3bf35f1ad666.jsonl`。run1 正常 acknowledgement 後開始；後續完成結果見下方，不構成五輪 memory PASS。
- Route 暫停340.2s後點 FULL-O04，正確切到該車速度曲線且時間不變。Speed 續播後暫停493.2s，切回 Route、點 FULL-O13、再選 FULL-O04，時間均493.2s；遵守先暫停再選車。
- 493.2s切 Diagram；正常選單續播，raw runResumed 的 workspaceIndex7。正常暫停1279.7s，UI顯示「模擬已暫停」。Pause dispatch 到 worker 真正 pause 的精確邊界不可直接觀測，不宣稱精確倍率。
- owner applicationVisualUpdate（不是 OS 注入到 compositor）：Play64.9641ms；trainMarkerClick91.2105／37.565／33.4948ms；Diagram tabClick57.1064ms；Pause21.9922／19.6277／29.6905ms；Resume19.814／26.6988ms。功能檢查通過的範圍不等同互動效能通過，Play／首次列車點選／Diagram切頁仍有超過50ms樣本。
- 目前 source 的 collector ack 最小修正已另有 red/green、Release build、Engine198/198與完整WPF runner證據（見 `COLLECTOR_ACK_RACE_FIX_20261007.md`）；原4.0.3 exe/DLL未覆寫，不能據此宣稱原binary量測競爭條件已修復。

### 完成、重設與封存

- Diagram暫停後隔次回模擬頁仍1279.7s；之後正常選60×、返回Diagram續播。此輪含30→60×、多次暫停及切頁，不列固定倍率throughput／五輪memory證據。
- 12:34:29.898+08完成7172.9s／1025events／37172trajectory／9147safety／activeTrain0；12:34:39.898+08合法10sIdle。12:35:12.217+08正常Reset後actualSeries、actual ordinary/critical及processed皆0，plannedSeries8；WPF polyline0／text1／canvasChild1，frameHistoryCounts=null。
- afterReset private241.543MiB／working set352.328MiB／heap86.841MiB／Gen2=8；單輪不判定bounded或leak。Stop後原生畫面無殘留實際曲線，事件列表清空。既有另一輪visual120差異仍未因此解決。
- 正常Stop於12:35:29.938+08，completedRuns1／activefalse。raw runAborted0／dispatcherStall0僅屬本輪，不能沖銷既有Route長停頓。最後Resume26.7946ms、Reset62.793ms，互動效能仍未達全項50ms門檻。
- 對照d70a3e60 run1，逐筆序列化全欄位、不刪除時間／位置／message，1025對1025、ordered full JSON mismatch=0；跨頁與倍率變更下事件一致限定子項PASS。
- 原始metrics/events非覆寫封存到 `artifacts/native-20261007` 同名兩檔；來源與目標SHA256相同：metrics `03AA4B209118217DF7FA25EC2FB8752807C5A485BF5B56C29A92A8A6AA80B684`，events `3CB0C299CFFD692487E6F78CF83C4851C636B4DC492F412F3FEB7FE05BD1AE6D`。原exe／DLL hash再次相同。

### 互動热路徑唯讀檢查

- 低成本子代理唯讀定位，主代理核對：current source的AttachTrainMarkerNavigation（MainWindow.V2.cs）在MouseLeftButtonUp選Speed頁／更新車輛ComboBox／排程重畫；nested tab與ComboBox handler另有繪製入口。這些是source路徑，是否實際造成原4.0.3的91.2105ms仍未證實。
- Diagram已有actual append cache、planned background warmup、layout key及series/version reuse；不能在沒有分段量測下宣稱「缺少快取」就是瓶頸。
- 不執行子代理建議的4.1.0新程序效能量測，因使用者將4.1限制為MCP功能；也不做未授權的UI效能修正。本輪僅驗收及文件紀錄。

## 本輪界線

- 使用者指定125%／150%最新排版矩陣暫不重驗；V4.1.0只驗MCP，日後合併另行指示。本輪其餘驗收限定原V4.0.3 binary，不修改／重建成4.1.0，不做Git合併或發布。
- 原V4.0.3來源：src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe；已核對FileVersion4.0.3.0。新版MCP使用output/mcp-build獨立輸出，證據不得混算。
- 先正常關閉已暫停且collector停止的4.1.0測試視窗，再開V4.0.3；使用computer-use操作正常UI、sample14、opt-in原生collector，不使用MCP advance／私有Engine推進、forced GC或並行建置測試。
- 待驗：同程序固定頁五輪完成→10sIdle→Reset的memory/cache趨勢、原生互動／停頓、營運事件目視的未覆蓋項。此為開始紀錄，未提前列PASS。

## 五輪 session：6ed4abc9

- HWND 332488；sample14已正常載入；60×，固定運行圖，上下行皆顯示／全部車輛，計畫、實際、事件點、終點時間開啟，兩軸縮放1，自動時間刻度，控制與事件列表展開。
- 使用computer-use skill正常UI操作；倍率選單首次點擊因geometry unavailable未執行，刷新並重新核對後成功，未重啟App。
- 量測檔：`src/MrtRouteSimulator.App/bin/Release/net10.0-windows/NativeAcceptanceLogs/native-acceptance-20261007-013321010-6ed4abc9a0e94dcd8e5bb08362f2500d.jsonl`。
- 第一輪已送出正常播放；完成數與memory gate待raw JSONL核對，不預先判定PASS。

### 執行結果與阻擋

- raw viewport確認96 DPI／interfaceScale1／1728×1037 DIP，100%固定頁；未驗125%或150%。
- run1完成7172.9s，事件1025；acknowledged-active-wall倍率60.0004225、conservative-dispatch-wall倍率59.986009；publication邊界並非精確final tick。
- 完成後10sIdle已寫入，正常Reset後actualSeries、actual ordinary/critical points與processed皆0；WPF polyline0/text1/canvas child1。PrivateBytes227.168MiB、WorkingSet330.313MiB、heap83.548MiB、Gen2=7。單輪不能判斷memory leak或五輪bounded trend。
- run2正常Play派送後20.1877ms、acknowledgement尚為0，collector就記錄runAborted，reason=`playback-worker-replaced`。UI仍正常推進，不能把collector abort當成模擬失敗；也不能宣稱第二輪有有效量測。
- 原生UI正常Pause後Stop collector；09:38:56+08 sessionStopped completedRuns1／activefalse。五輪驗收未完成，待量測生命週期問題處理，未以另一版本替代。
- 紀錄已封存至artifacts/native-20261007同名兩檔；metrics SHA256 `47FBF364B90652B4848415C198CCCE889EBA4F51368B7B0E667D01BBF9FA1B15`，events SHA256 `77558607706A1788F33CEE527B5B28CDE95101C4785F0641D70CF2C367E68224`。
- current source可見BeforePlay先建run，AfterPlayAcknowledged才綁定WorkerAtStart；Input probe在await期間仍會檢查worker identity。該時序與此次ack=0的abort吻合，屬於待核對的量測競爭條件推論，不宣稱已反編譯驗證原binary；本輪未改程式、未build、未合併。
- 封存後主代理逐序列化比較run1每筆event全部欄位（不刪除時間／位置／message等欄位），與artifacts/native-20261006的d70a3e60 run1皆1025筆，ordered full JSON mismatch=0。run1 finalFrameSummary：trajectory37172／safety9147／activeTrain0；run1 dispatcherStallCount=0，僅適用本輪Diagram，不代表Route既有停頓已解。
- owner application visual update輸入邊界：Play兩次78.6785／78.797ms，Reset61.9355ms，Pause38.3578ms（5274.4s）。Play／Reset仍超過50ms建議值；樣本小且不是OS injection至compositor測量，不以Loaded surrogate的0.3～2ms冒充實際owner更新延遲。
- 唯讀子代理獨立核對run1事件1025/1025全欄位一致、finalFrame／Reset／stall數字；主代理已以原始JSONL複核。current source的SimulationPlaybackWorker.GenerationId在Reset時不變，進一步支持「ack前null baseline造成collector早期誤判」推論；原binary內部路徑仍未直接驗證。後續如要修正，只限collector生命週期與回歸測試，须另獲授權並維持原4.0.3輸出／4.1 MCP隔離。
