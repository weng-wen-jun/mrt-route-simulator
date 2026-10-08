# 運行圖事件觸發點開關與錯位診斷（2026-10-03）

## 實作

- Diagram工具列「模擬實際」後新增「顯示事件觸發點」，預設勾選。取消只隱藏圖上事件Ellipse，圖例同步；事件表、trajectory、Engine、Schema及實際全量CSV不變。
- 只改MainWindow.xaml、MainWindow.TimeDistance.cs、MainWindow.V2.cs。開關納入incremental layout key，暫停時也立即重畫；full renderer及PNG/PDF套用同一選擇。不新增存檔欄位。
- 低成本子代理補TimeDistanceVisualTests，主代理review整合：預設/on→off→on、marker消失恢復、圖例、两種renderer、軌跡端點與來源counts不變。

## 驗證

- Release build 0 warnings/errors；完整Engine198/198 PASS；完整WPF exit0（含事件開關及CSV/PNG/PDF）；git diff --check exit0。
- 新Release process/window15406184正常讀sample14、30×播放後暫停00:40:27.9；原生on→off→on確認綠/紫/紅事件點消失及恢復，實線/虛線與事件表保留，回動畫確認clock仍00:40:27.9。開關原生子項PASS，維持125%monitor；不擴大為完整DPI矩陣PASS。App保留運行圖、暫停、事件點勾選。
- 未commit/push/tag/Release。

## 孤立圓點修正與方向篩選：自動化與100%原生子項 PASS

- 不是多出列車。EXPRESS-UP-01在3770s通過O03事件PositionMeters27936，3798.3s通過O02事件PositionMeters28566；SECTION-UP-01在7065.7/7094.0s也有同樣問題。
- PassStation傳入方向別station.PositionMeters，trajectory的train.Position則是共同圖表座標（上行totalLength−projectedChainage）；renderer直接Y(event.PositionMeters)，導致部分上行跨站圓點鏡射錯位。原始VehicleId/ServiceRunId有效。
- 使用者已授權「一併修正」。兩種 renderer 共用 StationPassed 顯示座標 helper，由既有 TopologyResultContext.GetDisplayStations 取得該方向車站的共同座標；保留跨站事件的車站位置語意，不改成越過車站後的車頭 cursor。其他事件及無法辨識的車站保留原座標，legacy 無 topology context 亦不改。Engine/raw events/CSV 不變。
- 使用者另要求上行／下行／雙向開關。沿用既有 DiagramDirectionComboBox（兩種 renderer 原本已同步篩選計畫、實際及事件），標示改為「上下行皆顯示／僅下行／僅上行」，加說明；不新增重複控制或資料來源。PNG/PDF 使用相同完整 renderer，CSV 保持全量。
- 最終 Release build 0 warnings/errors；完整 Engine 198/198 PASS；完整 WPF exit0 PASS（含新增共同車站座標、非跨站事件／缺站 fallback、雙 renderer 上下行篩選與座標一致性，以及既有 CSV/PNG/PDF）；diff --check exit0。新增測試使用非鏡射中心的車站，避免錯誤座標恰巧相同而漏測。
- 子代理因額度限制未完成新測試，改由主代理實作與驗證。原生新版第一輪在 00:40:53.6 後視窗不再出現在清單，原因未確認；近20分鐘 Application log 無 .NET Runtime／Application Error 紀錄，不能據此推論是正常關閉或沒有問題。該輪不列 PASS。
- 重啟 window137866，sample14、60×播放後暫停01:30:32.8；session20813ba5e6fd4852bb06c987594be14e實測96DPI／interfaceScale1。正常選方向「僅上行」，60～65分鐘片段中 EXPRESS-01 綠點跟隨實線，原本另一端孤立綠點已消失；「僅下行」排除該上行線及事件、保留下行 SECTION／FULL-SECTION；「上下行皆顯示」恢復雙向。未完成的未來上行計畫虛線亦跟隨方向篩選。事件開關off→on圓點消失／恢復、實線與事件表不變。以上原生子項PASS，不冒充125%或完整DPI矩陣；CSV/PNG/PDF本輪為自動化證據。
