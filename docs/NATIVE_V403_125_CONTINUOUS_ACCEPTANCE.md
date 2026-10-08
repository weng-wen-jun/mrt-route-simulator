# 125% 持續驗收（2026-10-03）

## 執行中檢查點

- 使用者要求持續驗收到額度耗盡；保持125%／軟體介面100%，只做可自主完成的本地驗收，逐段保存結果。
- 正確App為整合工作樹Release V4.0.3、sample14。同一process全程1171.2s暫停，未改Engine／Schema／production code。
- 前輪未成功的寬矮調整，本輪改正常標題列右鍵→系統Size快捷鍵S，分開調整寬度／高度成功。不要用前輪工具失效推論App resize缺陷。
- session `0a8b0e332a1f4363b88875918e840b6b`，stem `native-acceptance-20261003-065522402-0a8b0e332a1f4363b88875918e840b6b`；起始120DPI／interfaceScale1、1319.2×520 DIP。
- 寬矮畫面子項PASS：摘要展開仍可用外層捲動隱藏標題；Diagram可到O01、時間軸及事件表；Route固定水平滑桿在頁首／底部存在；圖面可達、FULL-O04右鍵顯示同車停止跟隨、左鍵開啟同車實際速度圖且底部可見，clock不變。
- 左鍵第一次按舊marker位置沒有開圖，後續穩定畫面顯示follow置中造成位置變更；重新觀察最新位置後命中。不把過期座標當App hit-test FAIL。
- session已正常停止（manual-stop，2026-10-03T07:00:23.3140602Z）；本段沒有Play、completedRuns0，不冒充全gate完成。

## 暖切與暫停互動統計

- 依actionId去重，只取applicationVisualUpdate；nearest-rank p50/p95，排除null及surrogate。主代理與唯讀子代理結果一致。
- 本session暖切Diagram n=5：p50 0.5930、p95/max 0.7852 ms；返回動畫n=5：p50 3.3292、p95/max 3.4565 ms。
- 本session列車左鍵n=1：45.4380 ms；與064133 session合併列車左鍵n=2：p50 45.4380、p95/max 59.1677 ms。樣本不足，不宣稱完整互動gate通過。
- 純resize完成時間N/A。WM_EXITSIZEMOVE只有surrogate，23355.587 ms含整段系統Size操作停留，不能解讀為resize阻塞。
- 量測邊界為WPF輸入接收→application callback；不是OS注入→像素顯示，亦未量測compositor present。
- 原始JSONL與events CSV已複製至docs/native-acceptance-raw，保留來源；SHA256比對一致。JSONL：D0C08587181A0ADB5DCDB6A38968ACF0D0917278F075B6CD6E084C2DFD67563A；events為0 bytes（本段沒有播放），SHA256 E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855。

## 新process首次開圖（進行中）

- 舊process正常關閉後，由正常UI啟動同一整合Release exe；新window id15210576。正常讀取sample14，不使用反射或直接修改clock；首次Diagram尚未開啟。
- session `201efe9da65146aaa2083f60cbc74822`，stem `native-acceptance-20261003-070620838-201efe9da65146aaa2083f60cbc74822`。sessionStarted確認120DPI/interfaceScale1，初始1382.4×820.8 DIP，monitor工作區1536×912 DIP，四邊留白（左右76.8、上下45.6 DIP），即工作區90%。
- 正常30×播放後暫停742.9s，再首次點Diagram；action3 applicationVisualUpdate 56.9519 ms。目視計畫虛線與實際彩色軌跡／事件表正常。601.5s開圖前checkpoint顯示plannedProcessedCount178992、actualProcessedCount0、Diagram polylineCount0，證明背景預熱而非提前開圖。
- action1 Play 63.8301 ms、action2 Pause 23.0485 ms；新process首次冷開n=1，不宣稱完整p95，也不與其他不同clock/尺寸樣本硬算配對改善率。
- 暫停後Safety／Resource正常顯示；FULL-O04左鍵同車實際速度圖時間軸/站名可見，clock742.9s不變。
- 正常切60×繼續，播放中時刻表、區間物理與區間統計正常出現資料；摘要完成列車後換行增高會改變分頁位置，一次舊座標未切頁，依新觀察位置操作成功，不列App hit-test FAIL。此run倍率混合30×/60×，不可稱純60×重複長跑。
- run1完成7173.1s、1025 events、activeTrainCount0；afterReset實際display cache processedCount0、WPF polylineCount0，planned178992背景cache保留，可正常重用。10s idle checkpoint已採集。
- run2以60×開始；Diagram正常Pause action24 38.2034 ms，clock912.8s；等待後Resume action26仍912.8s、32.5427 ms，後續event及動畫時鐘前進，暫停穩定／繼續子項PASS。
- 同run切Speed後Pause action29 28.2494 ms，clock2350.6s；兩次UI觀察均00:39:10.6，間隔大於2s；Resume action30 19.7558 ms，首幀00:39:18.1且之後前進。Speed正常Pause/Resume子項PASS；不是同頁memory protocol。

## Run1 Dispatcher間隔（不可當作click latency）

- 主代理與子代理核對run1完成截點：四筆>100ms DispatcherTimer gap。時刻表15:08:39.4009282+08:00/1299.9s/470.7493ms，區間物理15:08:52.8348323+08:00/2108.0s/419.7573ms，統計15:09:15.4356304+08:00/3464.6s/270.2948ms，Diagram15:09:51.1548381+08:00/5607.7s/144.6255ms。
- recent action依序tabClick13/14/16/17；其owner完成延遲只有2.8282/13.9923/3.5542/27.1483ms。不能用owner handler已完成就忽略後續gap；也不能直接把gap當這些click的像素延遲。
- 同時Gen0/1/2為717/99/6、832/126/6、969/157/7、1192/220/8；只記相關上下文，沒有ETW或retained object證據，不宣稱GC為原因或memory leak。
- 本輪有重複>100ms gap，仍需釐清後續視覺工作／UIA觀察等可能因素，NATIVE INPUT overall維持NOT COMPLETED，不列全gate PASS。

## 兩輪互動session結案與新版同頁repeat啟動

- 201efe session已正常停止／flush，raw副本與來源SHA256一致：JSONL 3D48EA8EEF8FFF61D3D09FED8D9F9B2502508CD69E8D80EC9DC7A1A8249FEA50；events 17CD13BFED575FE557A4DC41530A88E30D12CACC6C6F0621A96321DE43A959A4。
- run2完成7173.3s、events1025；與run1逐筆完整event JSON比較mismatch0、timestamp max delta0s。completion觀察clock差0.2s不影響事件timestamp完全一致；兩輪均有Pause、throughputBoundaryDefined=false，不冒充exact full-run throughput。
- 同一Release process、sample14、125%/interfaceScale1，新session `a20c4b40b9624afba0fd9f83a9ca3ba7`，stem `native-acceptance-20261003-071657092-a20c4b40b9624afba0fd9f83a9ca3ba7`。固定Diagram、不換頁、不改倍率、正常Reset→Play，目標5輪complete/10s idle/Reset；禁止GC.Collect。第一輪已啟動，尚不列5輪PASS。
- 使用者中途提出事件圓點顯示開關及孤立圓點疑問，切入新功能／診斷；同頁repeat只有1輪完成7173.0s、1025 events、hasPause=false、activeWall119.5501777s、boundaryDefined=true（仍非exact），不冒充5輪。已正常停止flush並歸檔，來源保留且hash一致：JSONL 6EFCA26AE915B5E38C7C833B04ED691FA083CDD1502A967837C467782E14A082；events 23A71C5DB925029D4414CA9E824620333661136A10E952EF3E9F000F90311C57。
- 完整201efe session共7筆Dispatcher gap：run2另外607.6923/196.4296ms在Diagram1374.2/1393.0s，126.2721ms在動畫1414.4s；仍無因果結論。

## 事件點新增需求／診斷

- 使用者圈出的孤立綠點來自上行StationPassed，例如EXPRESS-UP-01在3770s O03 event PositionMeters27936、3798.3s O02 event PositionMeters28566；SECTION-UP-01在7065.7/7094.0s有同樣狀況。事件存在、VehicleId有效，但事件PositionMeters為方向別站點里程，trajectory.PositionMeters為共同路線顯示座標；renderer直接Y(ev.PositionMeters)造成反向跨站圓點與實線錯位。不是新增列車，不得修改Engine物理來修圖。
- 已新增ShowEventsCheckBox，預設顯示；incremental/full renderer皆可隱藏事件Ellipse，legend同步，事件表及trajectory/event source不改。座標修正等待使用者回覆，一般顯示開關先實作／驗證。

## 事件座標與方向篩選修正版續驗

- 使用者後續授權一併修正並要求方向開關，已完成共用 StationPassed 車站顯示座標 helper、明確方向標示及新增回歸；Release、Engine198/198、完整WPF及diff check通過。原始資料與Engine不變。
- 第一輪新版 window1644984 播放到2453.6s後視窗消失、原因未確認，不列PASS。Windows近20分鐘應用事件紀錄無對應 .NET／Application Error。
- 第二輪 window137866 正常啟動／讀sample14、60×播放，原生session `20813ba5e6fd4852bb06c987594be14e`，stem `native-acceptance-20261003-113034052-20813ba5e6fd4852bb06c987594be14e`。實測目前96DPI／interfaceScale1（非前輪125%），window1728×929 DIP、workarea1920×1032，啟動四邊留白；不得把本輪列為125%矩陣。先做跨站座標／方向原生子項，再回長跑gate。
- 正常暫停5432.8s，原生「僅上行」及60～65分鐘中EXPRESS-01跨站綠點跟隨實線，不再鏡射到高里程端；「僅下行」排除上行線／事件，保留SECTION與FULL-SECTION；「上下行皆顯示」恢復雙向。事件off/on只隱藏／恢復圓點，實線與事件表保留。原生新功能子項PASS；已恢复0／自動結束時間、雙向、事件勾選並續播，整體長跑gate仍待。
