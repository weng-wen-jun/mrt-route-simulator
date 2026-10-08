# Collector 修正隔離整合版五輪原生驗收

## 授權與界線

- 使用者2026-10-07允許改用已修正collector的隔離整合版接續五輪記憶體與重設驗收。來源與執行版本均4.1.0，不冒充原4.0.3，不與原版memory／latency數據混算；125%／150%最新排版矩陣仍依使用者指定略過。
- 執行檔：`output/collector-ack-fix-20261007/bin/MrtRouteSimulator.App/release/MRT路線進出站時間模擬器.exe`，FileVersion4.1.0.0。exe SHA256 `5FA1FBA47020E0FC733ED661A4367664D920B41932515F8758B3106012EA2A57`，DLL `13BF94C39BB28A333F8EBFB7E1A16C2DDD7D504008744CC7BB95CDC12D6630D6`。
- 沿用已通過red/green、Release build、Engine198/198及完整WPF的隔離輸出，本輪不重建、不覆寫原4.0.3、無Git合併／提交／發布。沒有開啟MCP bridge，也不使用MCP advance。
- 舊4.0.3量測停止後正常關窗；關閉按鈕首次geometry unavailable未成功，刷新後Alt+F4正常關閉。新隔離版只啟動一次，HWND465204，sample14正常讀取；未重啟來掩蓋每輪記憶體累積。

## 固定條件與紀錄

- 固定Diagram，60×，上下行皆顯示、全部車輛，計畫／實際／事件觸發點／終點時間開，兩軸1、自動時間刻度，控制與事件列表展開。
- 每輪正常Play→完整完成→collector寫入10sIdle→正常Reset；第五輪afterReset後再確認自動Stop。量測期間不並行build／tests、不forced GC、不私有Engine推進。
- 原始stem：`native-acceptance-20261007-050151286-b8f80c7863cd417180e4194bf0af5360`，資料位於上述exe同目錄的`NativeAcceptanceLogs`。2026-10-07 13:01:51+08正常啟用，尚待完成結果；不預先列PASS。

## 分輪進度

- raw run1 viewport：96DPI／interfaceScale1／1728×1037 DIP；固定100%條件。
- run1完成13:04:25.023+08，7173.1s／1025events／37172trajectory／9147safety／active0；13:04:35.054合法10sIdle；13:05:11.022正常afterReset。private238.602MiB／working set345.984MiB／heap84.568MiB／Gen2=6。
- run1 actualSeries／actual ordinary／actual critical／processed皆0，plannedSeries8；visual polyline8／text40／canvas114。不能要求所有Reset一律canvas1而忽略正常計畫線；尚待後續畫面及五輪數字核對，不直接宣稱與原4.0.3既有visual差異已全部解決。
- run1重設後原生目視只有計畫虛線，沒有實際實線；初始event保留正常。run2正常Play已ack並runStarted，沒有舊版ack前abort；13:07:41.010完成7173.1s／1025events／37172trajectory／9147safety／active0，13:07:51.040合法Idle，13:08:21.872 afterReset private248.156MiB／working set358.930MiB／heap71.219MiB／Gen2=8，actualSeries／processed0、visual8/40/114。兩輪仍不判定memory bounded。
- run3完成13:10:52.641+08，7173.2s／1025events／37172trajectory／9147safety／active0；13:11:02.665合法Idle，13:11:29.881正常afterReset private252.066MiB／working set362.066MiB／heap72.676MiB／Gen2=10。actualSeries／ordinary／critical／processed皆0，plannedSeries8／version1，visual8/40/114。
- run4完成13:14:00.541+08，7173.2s／1025events／37172trajectory／9147safety／active0；13:14:10.550合法Idle，13:14:39.631正常afterReset private261.504MiB／working set372.422MiB／heap73.357MiB／Gen2=12。actual四項皆0，plannedSeries8／version1，visual8/40/114。
- run5完成13:17:03.620+08，7173.2s／1025events／37172trajectory／9147safety／active0；13:17:13.637合法Idle，13:17:43.024 afterReset private261.469MiB／working set372.336MiB／heap69.842MiB／Gen2=14，actual四項皆0、planned8／version1、visual8/40/114。13:17:43.037隨後自動Stop，completedRuns5／activefalse；raw reason仍使用共用Stop路徑的manual-stop，不代表人為在afterReset前中止。

## 主代理完成核對

- runStarted5、runCompleted5、runAborted0；每輪正常完成且各自有合法Idle與afterReset，未重啟程序。第五輪afterReset先於sessionStopped。
- 五輪events各1025，與原4.0.3 d70a3e60 run1逐筆全欄位序列化比較，各ordered full JSON mismatch=0，不刪除時間／位置／message。這是跨版本事件一致性，不混合效能／memory資料。
- 五轮actualSeries／actual ordinary／actual critical／actualProcessed皆0，planned8條保持同一version1，visual8/40/114五次一致；配合重設後原生只見計畫虛線，限定快取清除／畫面重設子項PASS。不回溯宣稱原版所有Reset visual差異已修復。
- Private MiB 238.602→248.156→252.066→261.504→261.469；末兩輪相近，但較run1仍增加22.867MiB。heap 84.568→71.219→72.676→73.357→69.842非單調且自然Gen2增加。memory分類維持UNRESOLVED；不宣稱bounded或leak，不以forced GC替代原生證據。
- owner applicationVisualUpdate Play五筆67.4921／66.5899／74.2828／68.8187／66.9733ms；Reset57.1289／56.9085／56.8061／64.2635／54.0882ms。五筆皆>50ms；小樣本不冒充精確population p95，也不是OS injection到compositor。互動效能未過。
- 原始metrics/events已非覆寫封存至`artifacts/native-20261007`同stem，來源與副本SHA256皆一致：metrics `C18B3E9B8DA881504CBA765C11CD05849D66C79609714775A0598D19DAED5352`；events `037F697E918F188E5D3905A0F88C121E6681D7158310F1982E652BB91ADDFEAD`。原4.0.3 exe／DLL hash再次相同。
- 五輪無Pause、acknowledged-active-wall倍率59.99963165～60.00313113，保守dispatch-wall59.98851546～59.98986011。throughputExact=false，final publication邊界不是精確final tick；限定正常播放倍率／事件一致子項PASS。
- 補核checkpoint：beforePlay5、10sIdle5、afterReset5、nextPlay4。五次afterReset frame的trajectory0／safety0／events1／time0，GenerationId皆相同；events1為正常零秒發車事件，不誤判成歷史未清除。checkpoint清除不等同舊frame所有物件已無retention，未取得heap root證據。
- 整個session只出現1筆dispatcherStall，13:03:45.684+08／sim4806.7／Diagram／elapsed101.7264ms、threshold100ms，落在run1。runCompleted.dispatcherStallCount是session累積值1，不是每輪各1；不能誤算為5筆。是DispatcherTimer gap，不作OS input／GC／compositor因果主張；本輪未見重複>100ms，但原因仍未知，不能把它稱成零gap。原任務門檻允許少數可解釋spike，不另發明零gap必須通過的門檻。
- 整體NOT COMPLETED：五輪量測生命週期、事件一致、快取重設已驗；memory仍待判定、互動延遲與gap仍未解。此次授權只擴大到修正隔離版的五輪接續，未合併原版本，未重跑125%／150%。
- 低成本唯讀子代理獨立複核五輪lifecycle順序、5125筆events逐輪一致、actual四項清零、planned與visual數量及memory／throughput。主代理以raw JSONL再次核對並整合；子代理使用MB標籤的數值實際除以2^20，本文件統一為MiB。其結論不擴張成OS／compositor或原4.0.3已修复。
