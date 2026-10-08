# 主視窗最低尺寸調整（2026-10-02）

- 使用者授權將最低視窗尺寸1180 × 720 DIP降至800 × 520 DIP；保留最低限制，預設開啟1440 × 900不變。Windows 150%最低約1200 × 780像素。只限主視窗，不改子工作區minimum或OS DPI。
- 正式XAML已更新，不改Engine、Schema、sample、介面偏好保存或播放進度。既有介面縮放／摘要收合／Route及Speed捲動保留。
- 子代理只補CompactRouteLayoutTests／InterfaceScaleTests，主代理負責審查及驗證。
- 兩個回歸已補：正式minimum斷言800/520、移除假minimum覆寫、主選單／播放倍率與分頁可見範圍、摘要展開收合／Route水平捲軸頂底位置。主代理已審查，但尚未執行新WPF binary。
- 完整Engine runner198/198 PASS、0failed；本輪無Engine修改。git diff --check PASS。
- 第一輪Release build因使用中的MRT process24060鎖定apphost.exe輸出失敗（MSB3027／MSB3021）；不強制結束、不當成成功。已請使用者正常關閉App，重建及測試待完成。
- 原生驗收暫停於sample14／clock0.0；新minimum仍待native DPI／resize複驗，不能清除既有native未完成項目。

## 授權正常關閉後完成（2026-10-02）

- 使用者明確授權代理關閉App；Computer Use重新選取唯一整合版視窗，確認sample14及clock0.0後Alt+F4正常關閉。再次list_windows確認MRT視窗已消失，沒有強制終止或捨棄未儲存提示。
- 重建Release成功，0warnings／0errors。首次新WPF回歸揭露800×520時展開摘要使Route viewport為0；隔離測試的100%比例後仍可重現，不能只歸因於125%偏好。摘要ScrollViewer固定80及compact40高度仍不足，失敗歷程保留。
- 最終修正：進入有效介面高度<600DIP的短視窗時自動收合摘要；回大視窗不強制展開、不改摘要值。手動仍可展開，摘要內容用垂直捲軸存取（短視窗max40／一般max100）。再次進入短視窗才自動收合，不在每次layout循環持續覆寫使用者选择。最低800×520不變。
- 最終Release build PASS，0warnings/errors；完整WPF runner PASS／exit0（包括正式minimum、五比例、14samples、worker及CSV/PNG/PDF）。隨後增強測試，從展開摘要狀態縮至800×520驗證自動收合，再建置並執行native-final-only全部PASS／exit0；最後變更僅測試斷言，production已由完整runner驗證。
- 最低尺寸下可見主選單／Play／倍率／模擬分頁、Route水平捲軸在vertical top/bottom固定可操作。summary資料與clock保留。這是WPF回歸，非native OS DPI／mouse/compositor驗收；最小視窗若手動展開摘要或放大介面，圖面仍可能不足，建議80～90%及摘要收合。不宣稱所有分頁同時完整顯示。
- 本輪scope：MainWindow.xaml、MainWindow.InterfaceScale.cs、CompactRouteLayoutTests.cs、InterfaceScaleTests.cs、README及QA／本紀錄。未改Engine、Schema或sample，未commit/push/tag/release。
