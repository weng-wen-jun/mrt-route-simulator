# 容量參數的實際運行驗證

使用同一topology-native SimulationWorld，固定0.1秒步進。預設每輪11小時，去掉前後各30分鐘，統計中央10小時，20個半小時區段，事件數÷10得到單方向列/h。`--3h`可重跑舊3小時方案；`--horizon-hours=5 --discard-minutes=20 --bucket-minutes=10`可另定量測窗。

```powershell
dotnet run --project .\scripts\CapacityThroughputExperiment -c Release -- --self-test
dotnet run --project .\scripts\CapacityThroughputExperiment -c Release -- . --aligned-only
dotnet run --project .\scripts\CapacityThroughputExperiment -c Release -- . --turnbacks
powershell -File scripts/CapacityThroughputExperiment/Write-Report.ps1
```

預設輸出 `artifacts/capacity-throughput-experiment/after-alignment-11h/`，本輪最終驗證使用 `after-alignment-11h-final/`；`--output-root=另一子資料夾`可隔離執行。`--turnback-case=case08-s14-rear-double`只跑指定設施。`--resume`核對Engine DLL SHA-256、專案JSON及量測窗鍵；不得混用不同物理版本或分母。保留舊after-fix與before-fix證據。

主驗證依案例設計班距與範例八150秒目標；手冊沒有要求搜尋最密班距。未指定aligned-only時另跑原載體及40秒過載，這些屬補充壓力測試。

實際控制：中間站由CapacityRuntimeMapper明確映射到B站，套用方向別坡度、速限／停站、車型性能及營運煞車BrakeEffect；進站保護涵蓋出站號誌距離與overlap，車尾淨空才開放。折返由TurnbackGeometryBuilder建立15/30/20m間距、渡線及對稱尾軌清空距離，TurnbackSignalMapper套用渡線轉換時間與紅燈制動安全係數。PHF、旅客容量及設計餘裕不直接節流運行。

模型假設必須與數據一起看：坡度範圍、合成入口號誌、月台長度與尾軌餘裕並非現地設計；Jerk、定位／建立時間及緊急煞車政策與手冊簡化式可能不同。這仍非S1–S14完整路網。不得為了符合公式降低validator或關閉保護。

同時檢查積壓成長、前後半窗延誤、碰撞、預測碰撞、停站與SignalAuthorityViolation；授權最終防護被迫介入不得算無違規通過。等待事件次數不等於等待秒數；尚未出發的車也不能用已出發平均延誤代表。每班獨立入線供車、折返同車接續，未設有限車隊循環。

舊3小時測試找到精停牽引死區卡死與折返後道岔預約缺口，修正前證據位於before-fix/diagnostics。新的坡度／閉塞回歸與完整Engine、WPF結果見QA_REPORT.md。
