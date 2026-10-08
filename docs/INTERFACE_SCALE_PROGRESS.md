# 介面縮放／小視窗捲動（2026-10-02）

## 使用者要求與範圍

- 入口在與「檔案／存檔」同一排的「顯示設定 → 介面縮放（軟體設定）」；80%、90%、100%（恢復預設）、110%、125%。預設100%。
- 字體、按鈕、欄位、間距與圖表一起layout縮放，程式自己的新開及已開工作區／設定視窗沿用。Windows原生檔案對話框及OS DPI不是此偏好設定的範圍。
- 使用既有 `%LOCALAPPDATA%/MrtRouteSimulator/display-settings.json`，新增InterfaceScale，保留已鎖定進路與Route水平zoom偏好。缺欄位／格式損壞／錯誤型別預設100%，合法數字正規化為最近支援選項；atomic temp→replace保存。
- 不修改Engine、Schema、sample、Version或路線專案序列化；不commit/push/tag/release，保留dirty整合工作樹。

## 實作

- InterfaceScaleService以Window內容的LayoutTransform統一縮放，保留原本transform；弱引用管理root transform，防止重複縮放或保留已關閉視窗。
- MainWindow選單互斥、即時套用並保存；保存失敗保留已套用效果並顯示錯誤。
- SpeedCanvas保持220DIP最低繪圖高度，增加Auto垂直捲軸，正常高視窗填滿viewport，小／短視窗可捲至圖底與時間軸；不是Speed演算法／效能調整。
- 原生量測viewport額外記錄interfaceScale，與真實OS DPI分開；不能把App80%當成Windows80%或100% DPI驗收。

## 驗證進度

- 第一輪完整Release build修正兩個整合編譯問題（normalize存取與Transform.Value.IsIdentity）；修正後PASS，0warnings/errors。
- 介面縮放專項PASS：五比例、入口位置、互斥選單、既有／新開Window套用、速度圖真實捲動、底部可見、座標可逆proxy、時鐘／event／trajectory不變、專案JSON不變。
- 完整WPF第一輪PASS（含縮放專項及所有既有回歸）。追加interfaceScale telemetry及設定保存／讀取unit vectors後，測試曾缺System.IO引入導致build失敗；已修正。該次舊binary回歸不當成最新測試證據。
- **最終完整solution Release build PASS，0warnings/errors；完整Engine 198/198 PASS，0failed；最新binary完整WPF runner PASS／exit0；git diff --check PASS。** 最終runner明確含偏好保存／舊設定／錯誤型別／malformed／normalization與五比例畫面／telemetry regressions，也含14sample、工作區、播放與CSV/PNG/PDF既有回歸。
- **未恢復原生桌面驗收**。縮放與Speed捲動尚需native DPI／hit-test／小視窗目視驗證，不能清除既有native clipping FAIL。未自動操作使用者視窗或真實偏好檔；test使用專屬temp設定路徑。

## 工作位置

- 分支 `codex/v403-playback-integration`，HEAD/base `68084cb`。
- 程式 `D:/AI/codex/mrt-v403-integration/src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe`；原目錄仍舊4.0.2來源，不能混用。
