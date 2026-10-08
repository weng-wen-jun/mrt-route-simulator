# 首次運行圖加速（2026-10-03）

## 授權與基準

- 使用者授權修正首次開圖延遲；只改Presentation圖表資料準備，不改Engine、Schema、版本、物理步進或原始匯出資料。
- 修正前原生sample14首次application208.31ms，暖切約0.92～1.01ms。冷圖表同步Append planned178,992筆是主要嫌疑；聚合timing不當作逐action因果證明。
- 正確工作樹為`D:/AI/codex/mrt-v403-integration`，不是舊4.0.2 checkout。既有dirty changes全部保留，不commit/push/release。

## 進行中

- 圖表planned cache背景準備／完成後UI接管；需保留critical points、generation/source隔離與取消、錯誤觀察。
- 專責代理實作與測試，主代理審查整合；Release／完整Engine／WPF與正常原生冷切重驗待完成。

## 實作與中間驗證

- 計畫artifact就緒時預熱Presentation cache，背景獨占建構後由Dispatcher交換；Draw不再同步Append整份planned trajectory。原critical/ordinary演算法、完整匯出資料來源不變。
- source identity＋worker generation＋task/CTS identity拒絕過期結果；清除／關閉先移除current request再取消。完成但尚未publication的取消競態經主代理審查修正，CTS在publication後才dispose；同source故障不反覆重試。
- 增加中文準備中／失敗提示；不把placeholder顯示當完整圖表就緒。
- Engine完整回歸198/198 PASS；定向背景快取測試PASS，包括sample14 planned178,992筆與同步演算法一致。
- 第一輪完整WPF在OutputTests失敗：診斷等待入口未必有SynchronizationContext，await後直接publication可能跨UI thread。主代理修正該入口亦使用Dispatcher；不隱藏失敗，修正後完整WPF重跑中。
- 主代理補充reset同artifact重用、owned stale generation與completed-before-publication cancel測試；nullable reflection參數修正後Release0 warnings/errors。

## 最終回歸與本輪原生子項（2026-10-03）

- 修正後完整 WPF runner exit 0、`PASS WPF visual rules`，涵蓋背景準備、暫停 resize、全部 sample 載入、工作區往返、播放 worker、長行程及 CSV／PNG／PDF 輸出。Release 0 warnings/errors；Engine 198/198 PASS。
- 正常啟動整合版 V4.0.3，從檔案選單載入 sample14，正常最大化、開啟量測、30×播放、暫停於1171.2s後首次點運行圖。未使用反射／直接推進取代原生操作；首次畫面確實有計畫虛線與實際實線，非準備中 placeholder。
- 原生 session `f1895fbb4f0a4f02a9ca7bb7a9ff69d7`，96 DPI／interfaceScale1。開圖前 checkpoint 已記錄 plannedProcessedCount178,992、plannedSeriesCount8；圖表未開啟時 polylineCount0，證明計畫 display cache 已預熱而未提前建圖表 visual。

| 動作 | actionId | handler ms | WPF receipt→application update ms |
|---|---:|---:|---:|
| 首次運行圖 | 3 | 55.9456 | 57.9616 |
| 暖切1 | 5 | 0.1049 | 0.9251 |
| 暖切2 | 7 | 0.1017 | 0.8819 |
| 暖切3 | 9 | 0.0984 | 0.9288 |

- 本輪單次冷切低於100ms目標；較原先208.3136ms降低約72%。條件並非完全配對：原先766.3s，本輪1171.2s且實際軌跡更多；不推論所有 DPI／尺寸／冷啟動均為同樣速度。不以4次開圖樣本宣稱完整互動p95。
- 量測界線是 WPF receipt 到 application callback，**不是 OS 注入到 compositor present**。正常停止量測、manual-stop flush；completedRuns0，不冒充完整長跑輪次。App 保持暫停於1171.2s、運行圖頁。
- 原檔保留，複製至 `docs/native-acceptance-raw/`，兩份 SHA256 與來源一致：
  - `native-acceptance-20261003-062848677-f1895fbb4f0a4f02a9ca7bb7a9ff69d7.jsonl`：`8190F105012145A18983B77FE90B07F09E593BFDC08A0413611569EAFFCA1C83`
  - 同 stem `.events.jsonl`：`A5977F961B3950D9E26A26C28A28838D20960B7814754EE3A7FCFF5CB0F480FB`
- 本輪冷開圖子項 PASS；整體原生驗收仍 **NOT COMPLETED**：其他 DPI／精確尺寸矩陣、完整互動p95與純resize延遲仍按原紀錄續驗。本輪未變更 Engine／Schema／版本，未commit／push／Release。
