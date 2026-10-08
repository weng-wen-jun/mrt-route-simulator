# 未讀檔預設頁排版修正（2026-10-03）

## 範圍與原因

- 使用者目前未讀取專案，先中止載入 sample14 的 DPI 續驗，改檢查預設頁；不自動載入檔案或開始模擬。
- 左欄原固定 450 DIP，在約 800 DIP 小窗占超過一半，擠壓右側分頁／播放控制。
- 空白 Route canvas 原會乘上軟體保存的路線 200% 縮放；縮小視窗時 SizeChanged 又可能讀到舊 viewport，造成多餘固定水平滑桿。

## 修正

- 左欄依實際內容寬度取 38%，限制 280～450 DIP；收合狀態不被 resize 覆寫。
- 標題可換行；車站表站名採 SizeToCells、最低 80，表格自身水平滑桿 Auto。實測 star 欄寬即使設 MinWidth 仍無水平 extent，因此改為內容寬度，並加入實際 ScrollToRightEnd 驗證。
- 未建立路線的 canvas 不套用路線 zoom；settled viewport 變更時同步空白畫布尺寸。
- 固定 Route 水平滑桿需有實際 route/topology frame 且有水平 overflow 才顯示；真正路線超出寬度時仍常駐外層底部，不必垂直捲到底。
- 只改 WPF，不改 Engine、專案 Schema、版本或設定保存格式。工作樹仍為既有 V4.0.3 integration；無 commit/push。

## 驗證

- Release build：0 warnings、0 errors。第一次因開著的 App 鎖住 exe 失敗；正常關閉後重建通過。
- Engine：198/198，0 失敗。
- 最終完整 WPF runner：PASS WPF visual rules（包括新增預設頁回歸、跨頁／播放／輸出）；不同於原生驗收結論。
- Outer-shell runner：PASS，包含新未讀檔 800×520、zoom 200%、1400→800 來回 resize、左欄上限／收合，以及表格實際水平捲動。
- 載入路線的固定水平 proxy regression 仍 PASS：外層 top/bottom 可見、proxy 與 route offset 雙向同步。
- 原生觀察：未读檔正常預設視窗、路線 200% 無多餘水平滑桿；大窗縮小後左欄約 300、標題換行，整頁捲動時沒有空白 Route proxy。
- 最終原生小窗：左欄車站表自身水平滑桿可見，拖到右端可完整看到停站秒數，拖回左端可看到編號／站名。完成後恢復整頁頁首並維持未讀檔、未播放。
- 原生觀察與自動 WPF 分開：本輪不宣稱完整 100/125/150% 矩陣、O04/O20 事件目視或互動效能 gate 完成。之前 100% 載入 sample 的續驗中途改查本問題，仍為 partial。

## 下一步

- 先由使用者確認預設頁改善；目前保持未讀檔，不開始播放。
- 繼續原本已載入情境的剩餘 DPI／事件目視／冷暖互動驗收時，另行記錄，不以本輪排版測試取代。
