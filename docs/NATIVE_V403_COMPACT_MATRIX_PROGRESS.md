# 原生 150% 最小視窗續驗（2026-10-02）

## 本輪條件與邊界

- 唯一原生 App 視窗2625400，整合工作樹 Release V4.0.3，sample14；不沿用前輪 handle。
- 正常選單開啟量測 session `f82f2b6d-8fa0-4ff2-92d8-43f9b8e4a8e3`。sessionStarted證實144 DPI／1.5、介面縮放1、實際800×520 DIP，位於非主螢幕DISPLAY1。
- 原始JSONL：`src/MrtRouteSimulator.App/bin/Release/net10.0-windows/NativeAcceptanceLogs/native-acceptance-20261002-115815631-f82f2b6d8fa04ff292d843f9b8e4a8e3.jsonl`。
- 只操作既有原生介面；未改Engine、未提交或發布。以下為本輪已觀察結果，不代表完整尺寸/DPI矩陣。

## 已觀察結果

- 800×520／摘要收合：outer捲到底255.333 DIP，產品標題與選單可捲出；Route水平捲軸仍固定底部。
- 停止狀態在FULL-標記右鍵，正確出現「視角跟隨此車輛」；選擇後狀態為FULL-O13，停止跟隨可操作。操作前後時鐘保持00:00:00.0。
- Route垂直捲到底71.885 DIP時，固定水平捲軸拖動0→2047.716 DIP，圖面由O01/O03移到O09/O10/O11。外層底部與Route底部組合通過。
- 最大化與還原可操作，最大化時更多站名及O13設施可見，還原後捲軸與頁面未消失；時鐘保持0。
- 右邊框拖到窗外被工具拒絕（outside window bounds），不列為resize成功。系統標題列「大小」搭配方向鍵可微幅增寬，但尚非寬/窄/高/矮完整矩陣。
- 小視窗手動展開摘要後，可透過外層捲動到Route，底部水平捲軸仍在；圖面不是不可達零高度。
- 切換完整行程速度曲線，固定Route水平捲軸隱藏；EXPRESS-01曲線、底部時間軸及站名可見。

## 追加：全站標籤與播放後點選

- 150%接近最小尺寸（系統尺寸鍵微幅增寬）、摘要展開、outer底部：運行圖內從頂端分六次正常滾輪捲到最底，28站O26→O01及O08a/O15a逐段可見，未見站名互蓋；O01及底部時間刻度可达。這不是要求28站同時塞進小視窗。最大化亦確認上/下段標籤可捲到，完整最大化分段掃描尚待。
- 正常30×播放後按暫停，停在239.5s。最大化畫面右鍵命中移動後FULL-O13，跟隨顯示正確車輛。
- 還原小視窗（縮小時摘要自動收合）後，外層捲到底，右鍵命中同車且選單出現已勾選的「停止跟隨此車輛」。關閉選單後跟隨可重新置中。
- 小視窗左鍵點選FULL-O13，正常切到該車實際速度曲線，選車下拉顯示FULL-O13，圖表最終時間239.5s；返回Route，時鐘仍239.5s。這提供實際native左右鍵命中證據，不只是離屏hit-test。
- 此短播放因正常Pause而記為runAborted，屬互動驗收情境，不是五輪完整播放失敗。尚不宣稱完整input p95。

## 特殊設施定位與觀察缺口

- 子代理唯讀核對已驗證第一輪1025 events，主代理原生操作：O04 678.8–715.5s（FULL-O04／EXPRESS-01），O13 1308.9–1350.4s（FULL-O13／EXPRESS-01），O20進入袋狀軌4529.7–4571.7s、折返返回5600–5665.5s（SECTION-VEHICLE-01，同車由SECTION-DOWN-01轉SECTION-UP-01）。
- 本輪10×接近O04時，原生擷取間隔錯過完整678.8–715.5s，因此O04仍NOT COMPLETED，不以既有events冒充本輪目視通過。
- O13已在1144.3s提前改1×並固定視角。1199.9s畫面：FULL列車位於下行側線，EXPRESS位於O11附近下行主線，作為越行前基線。
- O13原生1×分次目視：1265.0s快速車仍在主線接近、普通車留在側線；1298.3s快速車在O12之前；1316.9s快速車进入主線通過區、主線保護色顯示；1325.7s在普通車之前；1333.6s主線快速車與側線普通車在同站附近呈現不同軌位；1343.4s快速車已越過普通車；1355.6/1364.2s普通車沿側線出口移動；1373.6s普通車已回主線且仍在快速車後方。幾何未見跳移或切到錯軌。此窗口native目視PASS（150%、最大化、1×），不擴大為其他DPI/設施全面PASS。
- 最後正常Pause於1383.4s，量測正常Stop；App保留開啟、最大化、1×、暫停，不重設使用者當前畫面。

## 本輪操作延遲（小樣本，尚非完整gate）

- JSONL共50個去重actionId、192條inputAction邊界；nearest-rank p95只使用每action最後一條、非null值。不是OS injection或compositor latency。

| 動作 | handler樣本 | receipt→handler p95 ms | handler duration p95 ms | application update樣本 | receipt→application p95 ms |
|---|---:|---:|---:|---:|---:|
| nested tab |4|1.6361|11.3483|4|12.0783|
| pause |5|22.2391|1.9963|5|23.5694|
| play |2|19.9494|50.9252|2|70.8813|
| resume |3|19.6748|0.7915|3|20.4682|
| workspace tab |2|0.7632|191.2213|2|191.9254|
| marker左鍵 |1|19.3606|37.8923|1|66.0837|

- 未達建議normal interaction p95<50ms的類別：play、marker、workspace tab。樣本少，不可宣稱穩定達標；不在本輪自行修改效能實作。
- 本輪明確一次application update>100ms：2026-10-02 20:03:00.568+08、simulation0、首次切列車運行圖／匯出、191.9254ms（handler191.2213ms）。需要暖/冷切頁重複樣本判斷，不以一次當成持續重複FAIL，也不直接PASS。
- scroll7筆只有Loaded surrogate，p95/max2.393ms；nested tab surrogate p95/max65.695ms、workspace260.6978ms。與owner application update分開，不混成同一口徑。
- windowMove2筆沒有完成visual latency；windowMoveResize1筆26,603.8631ms包含系統「大小」模式持續期間，不可當UI阻塞26秒或單次resize渲染成本。resize純處理延遲仍N/A。
- 沒有dispatcherStall/stall事件列；最後checkpoint的InputStall rolling max66.4572ms（非exact全程percentile）。不以timer資料抵銷191.9254ms切頁觀測。
- 歸檔 `docs/native-acceptance-raw/native-acceptance-20261002-115815631-f82f2b6d8fa04ff292d843f9b8e4a8e3.jsonl`，原檔/副本SHA256均 `9C06D7DEBFD1114078B0E64452580A1E5405D604AAE22A30699000B83EB8534E`。本輪沒有完整完成run，因此不拿空events檔宣稱事件repeat。

## 尚待

- 150%寬/窄/高/矮完整矩陣、其他DPI下標籤矩陣、O04完整越行與O20袋狀軌慢速目視、完整input p50/p95/max及暖/冷切頁重複樣本。
- 100%／125% Windows DPI仍待使用者切换後驗證。
- overall NOT COMPLETED；本輪有界階段已收尾，等待下一DPI確認後續驗。
