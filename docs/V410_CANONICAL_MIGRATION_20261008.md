# V4.1.0 指定資料夾整合紀錄（2026-10-08）

現行來源：`D:/AI/codex/mrt-route-simulator`。使用者授權本地提交與移除舊分支資料夾，不含 push、tag 或 Release；Git 分支歷史保留。

## 保全與內容

- 備份位置：`D:/AI/codex/mrt-consolidation-backup-20261008`。
- `integration-full.zip` 包含舊整合樹全部 3,570 個檔案（含未提交、ignored 與輸出），逐檔解壓 SHA256 與來源比較 3,570/3,570 一致。ZIP SHA256：`7684FF66B9ECAAFC38728DB235E20E152E075F617C8D9E90E64EF40959701C8B`。
- `primary-before-full.zip` 保留遷移前主資料夾 2,297 個檔案（不含實體 .git），SHA256：`3323FBD266760D8D98D41D9A2AF45AC6ABD7E5B929BF67BE900D256532489FED`。
- `history.bundle` 已通過 git bundle verify，包含完整歷史與 21 個 refs。
- 從整合樹遷入 320 個來源／文件／設定／範例檔，複製時 SHA256 比對 0 差異。20 個被新版替代的舊檔已移除，仍可從備份與 Git 歷史還原。
- `外部檔案參考` 全部 16 個檔案保留並納入本地提交；不是新任務指令。整合樹沒有額外同名參考檔。
- 歷史驗收 artifacts 保留於 `artifacts/migration-v410/integration-artifacts`；大型原始驗收資料及建置輸出不提交，但完整備份保留。
- 合併歷史父節點：`558e06b534f97c997f450379f747e7709528ac17` 與 `68084cbd39d32b02fab9f8718d29b4ef5d5fb88f`。合併內容以已核對的 V4.1.0 整合來源為準，不以舊版覆蓋。

## 現行入口及驗證

- App：`output/v4.1.0-unified/bin/MrtRouteSimulator.App/release/MRT路線進出站時間模擬器.exe`。
- MCP：`output/v4.1.0-unified/bin/MrtRouteSimulator.Mcp/release/MrtRouteSimulator.Mcp.dll`。
- 版本唯一來源 `Directory.Build.props`：4.1.0。專案及全域 MRT MCP 設定同步指定資料夾；其他全域設定不變。
- 新位置 Release 建置 0 警告／0 錯誤、Engine 198/198、完整 default WPF exit 0／PASS、MCP 預設選址黑箱 15 個工具與三輪並行 advance/reset PASS。App 實測 FileVersion=4.1.0.0、ProductVersion=V4.1.0。
- 其他全域 Codex 設定（排除 MRT table）遷移前後指紋一致：`54dc0f9ab99ae2cfa386ee16b163bd87ca3c4ed4281bc485a27c6f5a2fce4820`。既有 MCP 連線不作熱更新宣稱，需重新連線。
- 暫存檢查未發現 bin／obj／output／artifacts 或二進位／憑證檔。歷史 Markdown 的 EOF 空行及一處雙空白換行維持原樣，保留外部參考檔逐檔一致，不為清除格式提示而改寫來源。
- 原生連續 resize、冷啟延遲、stall／memory 等未完成項目不因遷移改列 PASS；125%／150% 最新矩陣依使用者指示略過。
- 舊工作樹 `D:/AI/codex/mrt-v403-integration` 僅於新位置驗證、提交及備份確認後移除；既有 `mrt-integration-backup-20261001` 備份不刪除。

### 新位置證據 SHA256

| 檔案（output/v4.1.0-unified） | SHA256 |
|---|---|
| build-canonical.log | `8FA33B19C3197A1186825C742C3DCC8E2854215C7BDD3A29237BF2E71CB5C306` |
| engine-canonical.log | `37BE6520ACF123474F680981C61AE218C9264238E0083FAA6F639D5837825DFC` |
| wpf-canonical.log | `71AFB6AB9937FEF05285C4091F1637757E2BB523A69AA9A0DE2782AADCB72BA0` |
| mcp-canonical.log | `C27727835D154ED6D15E90F492FDA2B624F207857D9FF8B1AEF995EB16000916` |
| App DLL | `E2356057985C87F0DB2C05485667585EB9BD2BC4CDE1C474D8363EA87B2D31F6` |
| MCP DLL | `48E4F16F6B5A0A2DADF0D0A0ACFCF23445EB4813FD2CB7BBCEC2D2330C29B5C2` |
