# 外層捲動與預設視窗比例（2026-10-02）

## 範圍

- 工作樹：`D:/AI/codex/mrt-v403-integration`，V4.0.3 整合版；不修改原 V4.0.2 工作樹。
- 整頁垂直捲動包含產品標題、主選單、摘要及結果內容；短視窗保留至少 720 DIP 的內容版面，透過外層捲軸到達。
- Route 水平捲軸置於外層捲動區之外的視窗底部。僅 Simulation／Route 且水平溢位時顯示，與實際 Route offset 雙向同步。
- 預設初始視窗採所在 monitor 工作區寬／高的 90%，以實際 DPI 換算 DIP 並置中。最低 800×520；工作區本身更小時不超出工作區。明確指定的測試尺寸不被 90% 規則覆寫。
- 不改 Engine、固定 0.1 秒步進、Schema 或播放資料來源。

## 本輪驗證

- 首次 Release build：App 成功，測試新增的參數型別／變數遮蔽造成 3 個編譯錯誤；修正後再次建置成功，0 warnings／0 errors。
- `--outer-shell-only` PASS：800×520 的 shell viewport 767×463、垂直可捲 257 DIP、Route 水平可捲 658 DIP。
- 回歸涵蓋 outer top／bottom × Route top／bottom 的固定水平捲軸可見性、proxy 操作同步、paused clock 不變、標題內容上捲、90% 尺寸向量及小於最低尺寸的工作區邊界。
- 完整 Engine 198/198 PASS；完整 WPF 在首轮及啟動時序調整後兩次 PASS。之後另補預設視窗實際 monitor 尺寸回歸，最終新版仍需重跑。
- 原生啟動首輪未通過：144 DPI、1280×752 DIP，佔滿 monitor 工作區。調整初始化事件但僅用固定值／NaN 判斷仍未修復；保留失敗 JSONL。加入 `initialWindowFit` 診斷確認 WPF 自动值為1440×753.333，實際 monitor 工作區1280×752，不應把它當呼叫者指定尺寸。
- 最終原生啟動尺寸 PASS：session `19aa5fa12b0041598c9fdd39826ee3b1`、DPI144／1.5、interfaceScale1，實際1152×676.667 DIP（目標1152×676.8，像素取整差0.133）。Left1344／Top-45.333；monitor 工作區 DIP (1280,-82.667) 到 (2560,669.333)，四邊均在工作區內。不是以截圖尺寸猜DPI。
- 正常讀檔流程的 UIA 清單項目報 cached-state unavailable；刷新後使用當下 screenshot 清單座標雙擊，正常讀檔進度已出現。大型案例來源待讀檔完成後核對，不混用錯誤檔案的事件秒數。
- 來源已確認為整合工作樹 sample14、clock0.0s；初始 interfaceScale100%、Route horizontal zoom200%（本機既有偏好）。
- 原生外層捲動0→48→96 DIP，產品標題／版本橫幅完全捲出；Route 固定水平捲軸仍在底部。正常拖曳使 proxy offset0→2199.154，畫面由O01/O03/O05移至O10/O12/O14；Route垂直offset0→48後水平捲軸仍可見，clock保持0.0s。
- 切Speed時Route固定捲軸正常隱藏；Speed垂直offset0→39.22，時間軸及底部站名可達；outer offset調整80.667，clock仍0.0s。僅本輪實際1152×676.667／150%尺寸，不擴張為全尺寸矩陣PASS。
- 代理曾嘗試拖曳右下角縮窗，但實際proxy offset增加32、視窗未縮小，故不計入resize證據；800×520實際原生縮窗仍待補。
- 運行圖正常選單Play、UI倍率60×開始連續量測；無注入world時間／強制GC，執行中不並行重型測試以免干擾吞吐。

## 交付邊界

- 使用者允許代理自行验收；若需手動更換 OS DPI，仍保留待驗。
- 未 commit／push／tag／Release。既有 dirty changes 保留。

## 最終回歸

- 最終Release build：0 warnings／0 errors。完整Engine198/198 PASS；最終完整WPF PASS（exit0）。
- 補強實際預設視窗測試首跑因JSON `DpiScaleX/Y` 欄位大小寫錯誤FAIL；修正後專項與完整runner PASS。測試同時涵蓋預設90%實際monitor尺寸和明確800×520不覆寫。
- 新增啟動診斷只寫入opt-in原生viewport紀錄，不改Engine／播放。五輪原生結果見 [五輪紀錄](NATIVE_V403_FIVE_RUNS_PROGRESS.md)。
