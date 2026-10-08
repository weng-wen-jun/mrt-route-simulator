# 原生量測 Play acknowledgement 競爭條件修正

## 範圍

- 使用者於2026-10-07允許最小修正量測啟動時序並補回歸測試；不改Engine、Schema、物理或MCP功能，不做Git提交／合併／發布。
- 原V4.0.3原生session6ed4abc9 run2在Play dispatch後20.1877ms、ack=0時誤判worker replaced。正常播放仍推進；證據見NATIVE_V403_20261007_CONTINUOUS_ACCEPTANCE.md。
- MainWindow.NativeAcceptance.cs只在PlayAcknowledgedTimestamp非0後才執行worker identity／generation替換檢查；ack之後的真正更換偵測保留。
- 目前來源樹版本4.1.0，修正只在output/collector-ack-fix-20261007隔離建置。這是修正所需自動化回歸，不列為V4.0.3原生五輪或擴大V4.1其他原生驗收。
- 原V4.0.3 exe FileVersion4.0.3.0、SHA256 `66BC6230C149622A646DF2F8B924BF111EF43BCD3CD21FF177D9E9153840AFA0`；完成後須再次核對未變。

## 驗證

- 隔離Restore及完整Release build：PASS，0 warnings／0 errors；Directory.Build.props與原執行檔輸出不變。
- 新回歸由子代理修改NativeAcceptanceTests.cs，主代理審核並要求ack前實際等待oldWorker.PlayAsync(1)完成，才呼叫ack hook；不直接修改run／playing欄位。
- 可決定性重現BeforePlayDispatch→ProbeTick（ack前）不得abort；ack後以正常ConfigureTopologyProjectForPlaybackAsync建立真實新worker，ProbeTick仍須產生playback-worker-replaced abort。
- Red/green驗證：暫時只移除新ack guard，隔離build成功後專項exit1，準確失敗於「NativeAcceptanceProbeTick不得在Play acknowledgement前將runAborted」（test line698）。恢復guard並重新完整build後，相同專項exit0／PASS。未保留無防護版本作正式輸出。
- 完整Engine runner：198/198 PASS、0 failed。完整WPF runner exit0／PASS WPF visual rules，包含新ack競爭條件、真正worker replacement、NativeAcceptanceInput及既有長行程／輸出回歸；自動化通過不等於原生五輪memory通過。
- 原V4.0.3 exe再次核對SHA256相同；原DLL核對值 `DA598917F2F5B6501C63989141FEC5A28DE36177920622E8B99048F6E07D3422`，完成後再核對。
- 完成後原exe與DLL再次核對，兩者SHA256皆相同。修正尚未套入原V4.0.3 binary，五輪原生量測仍待可追溯的隔離來源／日後整合版本，不把4.1自動化回歸移植成原4.0.3原生PASS。
