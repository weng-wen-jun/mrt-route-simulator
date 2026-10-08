# 125% 原生 UI 續驗紀錄

## 2026-10-02：精確最小與窄高視窗（最新續驗）

- 原生 HWND 726048、同一 Release V4.0.3／sample14；DPI 實測均為120／1.25，interfaceScale1。全程保持245.5 s正常暫停，沒有播放、修改程式、重跑build/tests或commit/push。
- Session `8d617016-660d-4965-ad43-e78cd084cda3` 起始viewport確認 **800×520 DIP**。透過Windows標題列大小選單與方向鍵抵達MinHeight，不從截圖像素反推DIP。
- 精確最小尺寸：摘要展開後，外層可捲到237.6 DIP底端、標題與選單移出畫面，狀態列可見；運行圖內層289.48 DIP可到O01與時間軸。Route分頁外層底端255.6 DIP、固定水平滑桿仍存在。圖表預覽高度有限，但內容可捲到，不宣稱同時顯示完整內容。
- 精確最小尺寸／摘要展開：可見FULL-O04標記右鍵命中「停止跟隨此車輛」，執行後跟隨停止；同一標記左鍵切FULL-O04實際速度曲線，底部時間軸與O02/O03標記可見，時鐘不變。**PASS**。
- 窄高尺寸以標準Size模式固定方向鍵序列增高，補開viewport session `c224560e-04f6-42fa-ab6d-2bb6a9740c49` 實測 **800×863.2 DIP**／120 DPI。一次refresh參數未包含text/screenshot而被拒絕，重新觀察後才繼續；沒有把該失敗當驗收成功。
- 窄高尺寸摘要展開／收合均正常，操作與速度圖底部可見；摘要長文字換行，內層捲到22.55 DIP可看到最後一行，未見互蓋。摘要展開的運行圖上端O26與下端O01均可捲到，含本輪可見中間站，未宣稱此尺寸每一站重新逐段全掃。
- 窄高尺寸：右鍵命中FULL-O04並啟用同車跟隨，左鍵切同車實際速度曲線；底部可見，時鐘仍00:04:05.5。固定Route水平滑桿在視窗底部可見。**PASS（以上明列範圍）**。
- 兩個session都是armed-only、未正常Play，因此不算新完整播放輪次、不算throughput／O04越行事件證據，也不取代冷暖互動延遲gate。均透過正常停止選單flush；原始記錄已歸檔且source/archive SHA256相同：
  - `native-acceptance-raw/native-acceptance-20261002-125756078-8d617016660d4965ad43e78cd084cda3.jsonl`：`6C07DF20F03210DFD14CC55D828952CE169ECE22AC67ECA40DCA65A76734D8EC`。
  - `native-acceptance-raw/native-acceptance-20261002-130412341-c224560e04f642faab6d2bb6a9740c49.jsonl`：`6BFFB71D5F46474069A4DD9FBBA97F01FE298FBD3B8646751448C411029B02D6`。
- App保留800×863.2 DIP、摘要展開、運行圖、245.5 s暫停、量測停止。下段可以請使用者手動切100%，再讀actualDPI與進行相同配置重點；O04／O20動態目視與互動冷暖重測仍待。**Overall NOT COMPLETED**。

## 2026-10-02：sample14 一般／最大化／寬矮／窄小視窗

- 使用 integration worktree 的 V4.0.3 Release 執行檔，原生 HWND 726048；未改程式、未重跑 build/tests、未 commit/push。
- 正常檔案選單載入 `14-大型-二十八站完整營運範例.mrtsim.json`。檔案對話框 UIA 索引失效後，改用新截圖中的可見檔案列雙擊；成功載入，沒有修改 sample。
- 量測 session `90830412-3ce4-47bd-94c3-3b9afe871daa`；實際 DPI 120 / scale 1.25，軟體介面比例 1。起始視窗 1382.4×820.8 DIP，為所在 monitor 工作區 90% 置中。
- 正常 30× 播放後於 245.5 s 暫停；後續點選／切頁／捲動維持 00:04:05.5。本輪只做 UI 檢查，不是完整播放 throughput 或事件驗收；停止量測標為 manual-stop / runAborted，不能算一輪完成。

### 已驗證的範圍

| 情境 | 原生觀察 | 結果／邊界 |
| --- | --- | --- |
| 一般視窗列車右鍵 | 可見 FULL-O04 標記命中、開啟跟隨選單、啟用同車跟隨 | PASS；暫停時間不變 |
| 一般視窗列車左鍵 | 同標記切換至 FULL-O04 實際速度曲線 | PASS；來源文字與車輛一致 |
| 一般／最大化速度圖 | 完整圖形、底部時間軸與 O02/O03 標記可見 | PASS；只代表已累積的實際軌跡 |
| 最大化／還原 | 內容填滿工作區，上下沒有先前居中白邊 | PASS；抓取動畫中的殘影後另讀穩定畫面，不把殘影當永久缺陷 |
| 最大化運行圖 | O26→O01 28 站標籤可捲到，含 O08a/O15a | PASS；未見站名相互蓋住 |
| 寬矮視窗 | 使用 Windows 標題列大小選單＋方向鍵縮高；摘要自動收合，外層可捲到狀態列 | PASS；拖曳截圖內邊框未改尺寸，不算 resize 成功 |
| 窄小視窗 | 末次 telemetry 確認 800×539.2 DIP（不是 800×520） | 本輪只驗此實際尺寸，不擴大為精確最小尺寸 |
| 固定 Route 水平捲軸 | 外層頂端 offset 0 已可直接拖動，水平 0→1457.7626 DIP，O08a 可見；外層底端 236.4 DIP 仍常駐 | PASS；不必先把上下捲軸捲到底 |
| 窄小速度圖 | 整頁捲到底，速度圖時間軸與車站標記完全可達 | PASS |
| 窄小運行圖 | 逐段掃描 O26→O01，O08a/O15a 可達、未見站名互蓋；內層底端 249.48 DIP | PASS；水平圖表另有捲軸，沒有宣稱全部時間範圍同時塞進視窗 |

### 互動量測（小樣本，尚未達全面 gate）

175 筆 inputAction boundary，依 actionId 取末筆後 64 個動作。下列延遲是 WPF 最早 receipt 到 application visual update，不是 OS 注入／compositor present；無 owner callback 的 scroll/resize 不補成 0 ms。

| action | 動作 n | application 有值 n | nearest-rank p95 ms |
| --- | ---: | ---: | ---: |
| nestedTabClick | 3 | 3 | 8.50 |
| pause | 1 | 1 | 21.87 |
| play | 1 | 1 | 63.69 |
| tabClick | 3 | 3 | 194.71 |
| trainMarkerClick | 2 | 1 | 92.38 |
| scroll | 46 | 0 | 未量到 owner callback |
| windowMoveResize | 2 | 0 | 未量到 owner callback |

- >100 ms application 更新 1 次：首次本輪運行圖切頁，2026-10-02 20:47:08 +08，245.5 s，194.71 ms。
- Dispatcher stall count 0 不能代替 action 的 194.71 ms。正常互動 <50 ms 建議尚未全面達成；仍需冷／暖切頁、play、marker 重複與純 resize 處理延遲。
- 方向鍵 Size 模式包含刻意停留／多次鍵入，不能把整段 resize action 時間當成 UI 阻塞。

### 證據與狀態

- 已正常停止量測並 flush。raw 歸檔：`native-acceptance-raw/native-acceptance-20261002-124543446-908304123ce447bd94c33b9afe871daa.jsonl`。
- source/archive SHA256 相同：`816AB432E42E3A63E5238196613CB672BD8CA6D721FD5F652E6BB57A3610F5A4`。
- App 保留 sample14、800×539.2 DIP、245.5 s 暫停、運行圖頁首，量測停止。未要求使用者切 DPI。
- Overall **NOT COMPLETED**。下一段仍為 125% 精確 800×520／窄高與摘要展開、resize 後 marker 重驗；之後使用者手動切 100% 並讀實際 DPI。O04 完整越行、O20 pocket 入線／等待／反向以及互動重複 gate 仍待；既有 150%／五輪結果不擴大成新 UI binary 全 DPI PASS。
