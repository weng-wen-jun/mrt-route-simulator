# MRT Route Simulation Engine - Model Specification V3.3.0

> 現行規格（2026-08-24）。V3.0～V3.2 章節保留作核心演進說明；V3.3 以單一營運資料來源、逐車型性能、完整區間統計與 Schema 7 為準。

產品版本、引擎與存檔格式分開表述：產品版本為 V3.3.0，`SimulationEngineKind` 可選 V1 基礎引擎或 V2 寫實引擎，現行專案格式為 `schemaVersion = 7`。本階段僅供內部測試，不移轉舊專案；Schema 1～6 會明確拒絕。

## V3.3.0 本輪變更

- V2 寫實引擎與 Schema 7 僅使用 `VehicleTypes + ServiceTypes + StopPatterns + Dispatch`；不再寫出或執行舊 `ServicePatterns／ServiceRuns` 雙資料源。
- `VehicleTypeDefinition` 是逐車次性能的權威來源，實際套用最高速度、加速度、營運／緊急煞車、Jerk、牽引衰減、惰行減速度與車長；V2 UI 隱藏舊 V1 全域性能欄位。
- 停站模式以交易式 UI 編輯名稱、停站／跨站、停站秒數覆寫及通過速限；取消不套用，刪除受引用項目會說明阻擋原因。
- 手動發車計畫開啟時自動選取目前模式；端點可退出、建立反向續行，或指定接續既有的反向車次，同一實體車輛沿用 `VehicleId`。
- V3.3 區間統計支援方向、車輛、車次、車型、服務、停站模式、模擬秒範圍及是否包含運行中等篩選，並輸出實際軌跡中的移動閉塞受限秒數。
- `V3.3.0-完整功能驗證範例.mrtsim.json` 同時涵蓋上述功能、雙向派車及五類空間參考點，作為自動化與 Windows UI 驗收基準。

## V3.2.0 本輪變更

- `SpatialReferencePointDefinition` 支援 `IntermediateStation`、`FrontTurnback`、`RearTurnback`、`PocketTurnback` 與 `Junction`。中間站的順行／逆行參數分開保存及驗證。
- `SpatialCapacityAnalysis.Calculate()` 以 URCS `Components.dll` 的 `CalTs()`／`CalCap()` 行為為相容基準，輸出順行或逆行設計班距、正常容量與設計容量；容量採與原程式一致的整數截斷。
- 中間站雙方向參數包含進／離站坡度、停車點至號誌距離、號誌重疊距離、停站時間、前後區間巡航速度及安全係數。
- 三類折返與銜接點保留原 UI 圖示所對應的距離、坡度、道岔速度、安全係數及停等欄位。空間折返距離會使用 `AnalyticalModel` 計算進出折返區旅行時間，再加上設定停等時間。
- 中間站的方向別停站時間會套入 `SimulationWorld`；未續行列車完成端點工作後進入 `OutOfService`、`Active = false` 並產生 `ServiceEnded`。
- V2 時刻表、區間物理與統計由同一份 `TrajectorySample`／`SimulationEvent` 建立；事件比對同時使用 `VehicleId` 與 `ServiceRunId`，避免折返接續後誤接其他實體車輛。

### URCS 相容容量輸入與輸出

`SpatialCapacityGlobalParameters` 保存號誌／轉換時間、反應時間、PHF 與營運餘裕率；`SpatialCapacityTrainParameters` 保存列車長度、加減速度與煞車有效因子。各參考點先依型式計算最小追蹤時間 `Ts`，再套用安全係數、PHF 與營運餘裕率求得設計班距及每小時容量。坡度造成有效加減速度小於等於 0 時會拒絕計算，不輸出無效容量。

## V3.1.0 本輪變更

- V2 首頁不再顯示僅 V1 使用的列車數量、指定班距與首班時間；V2 初始發車以 `DispatchPlanDefinition`／發車計畫為準。播放倍率仍只控制 UI 推進節奏，不改變 Engine 時間。
- `HeadwayDirectionPlan` 與 `ManualTimetableRow` 均支援端點折返續行設定。續行時，端點處理完成後沿用 `VehicleId`、`VehicleTypeId`、`ServiceTypeId`、`StopPatternId`，並建立方向相反的新 `ServiceRunId`。
- 手動發車計畫另可指定「折返後接續車次 ID」。例如下行第一車可接續上行第六車；接續沿用相同 `VehicleId`，不另生成目標車次。實際抵達早於目標計畫時間時進入等待，晚於目標時間時記錄延誤。
- 未續行的計畫車次抵達端點後，完成該站停站／清車秒數即轉為退出營運；退出列車不再出現在路線圖，也不參與安全配對與安全計算。
- 下行速度曲線使用 V3 車次識別，建立 `SimulationWorld` 後即可產生並顯示，不依賴播放後才補建識別。

## 1. 核心原則

- Engine 與 WPF UI 完全分離，Engine 不參考 HTML、CSS、DOM、Canvas、WPF 或瀏覽器時間。
- 解析模型直接計算每個區間的精確旅行時間；Simulation Engine 以固定 `dt = 0.1 s` 推進顯示時間，並使用同一組解析相位求當下位置與速度。
- 核心單位只有公尺、秒、m/s、m/s²。km、km/h、分鐘及時鐘字串只出現在輸入轉換或輸出格式化。
- V1.0 解析 API 保持相容；V2.1.0 以獨立的有狀態 `SimulationWorld` 加入實際營運軌跡、里程速限、移動閉塞與事件，不改寫 V1.0 的任意時間解析查詢。

## 2. 資料結構

### Route

| 欄位 | 型別 | 定義 |
|---|---|---|
| `RouteId` | `string` | 路線唯一編號 |
| `RouteName` | `string` | 路線名稱 |
| `Stations` | `IReadOnlyList<Station>` | 依路線位置遞增排序 |
| `TotalLengthMeters` | `double` | 最末站 `PositionMeters` |

### Station

| 欄位 | 單位 | 定義 |
|---|---|---|
| `StationId` | - | 車站唯一編號 |
| `StationName` | - | 車站名稱 |
| `PositionMeters` | m | 從第一站開始的累積里程；第一站為 0 |
| `DwellTimeSeconds` | s | 該站一般停站時間 |

UI 只輸入「與前站距離」，由 `RouteFactory.FromSegmentDistances()` 累加成唯一的 `PositionMeters`，避免兩套里程資料互相衝突。

### TrainParameters

- `MaxSpeedMetersPerSecond`
- `AccelerationMetersPerSecondSquared`
- `DecelerationMetersPerSecondSquared`
- `DefaultDwellTimeSeconds`
- `OriginTurnaroundTimeSeconds`
- `TerminalTurnaroundTimeSeconds`

最高速度、加速度與減速度必須是有限正數；其餘時間必須是有限非負數。

### SegmentTravelResult

- 距離、總旅行時間、實際峰值速度及曲線類型。
- 加速、巡航、減速的個別時間與距離。

### StationEvent

- 車站編號與名稱。
- `ArrivalTimeSeconds`、`DepartureTimeSeconds`、`DwellTimeSeconds`。
- 累積位置及方向。

### TripResult

- 方向與起點出發時間。
- 車站事件、區間明細、終點抵達時間。
- `TotalRunTimeSeconds`、`TotalDwellTimeSeconds`、`TotalTravelTimeSeconds`。

### TrainState

- 列車編號、位置、速度、狀態、目前車站、下一站、方向及模擬時間。

## 3. 物理公式

設：

- 區間距離 `d`
- 最高速度 `v_max`
- 加速度 `a`
- 減速度 `b`

### 3.1 達到最高速度所需距離

```text
d_accel = v_max² / (2a)
d_decel = v_max² / (2b)
```

若 `d_accel + d_decel <= d`，使用梯形速度曲線；否則使用三角速度曲線。

### 3.2 梯形速度曲線

```text
t_accel = v_max / a
t_decel = v_max / b
d_cruise = d - d_accel - d_decel
t_cruise = d_cruise / v_max
t_total = t_accel + t_cruise + t_decel
```

### 3.3 三角速度曲線

```text
v_peak = sqrt(2 × d × a × b / (a + b))
t_accel = v_peak / a
t_decel = v_peak / b
t_total = t_accel + t_decel
```

### 3.4 即時位置與速度

加速相位：

```text
v(t) = a × t
x(t) = 0.5 × a × t²
```

巡航相位：

```text
v(t) = v_peak
x(t) = d_accel + v_peak × t_cruise_elapsed
```

減速相位：

```text
v(t) = max(0, v_peak - b × t_decel_elapsed)
x(t) = d_accel + d_cruise
       + v_peak × t_decel_elapsed
       - 0.5 × b × t_decel_elapsed²
```

位置在區間終點強制限制於 `[0, d]`，避免浮點誤差越站。

## 4. 時間定義

- Station-to-station travel time：離開 A 站至抵達 B 站，不含 B 站停站。
- Dwell time：抵達中間站至再次出發。
- One-way running time：起點出發至終點抵達；包含中間站停站，不含起點及終點一般停站。
- Cycle time：下行單程 + 終點折返 + 上行單程 + 起點折返。
- 理論班距：未指定班距時為 `cycle_time / train_count`。

起點事件只有出發意義；終點事件只有抵達意義。終點開始反向前的等待歸類為 `TURNING`，不重複計入一般停站。

## 5. 狀態與轉換

支援狀態：

- `DWELLING`
- `ACCELERATING`
- `CRUISING`
- `DECELERATING`
- `ARRIVING`
- `TURNING`

典型循環：

```text
ARRIVING(origin)
→ ACCELERATING
→ CRUISING（距離足夠時）
→ DECELERATING
→ ARRIVING(station)
→ DWELLING（中間站）
→ ...
→ ARRIVING(terminal)
→ TURNING
→ inbound trip
→ ARRIVING(origin)
→ TURNING
→ next cycle
```

`SimulationEngine.Tick()` 每次增加固定 `0.1 s`；播放倍率僅改變 UI 每次推進多少模擬秒數，不改變模型及事件時間。

## 6. 公開 API

```csharp
AnalyticalModel.CalculateSegmentTravelTime(
    segmentDistanceMeters,
    maxSpeedMetersPerSecond,
    accelerationMetersPerSecondSquared,
    decelerationMetersPerSecondSquared)

TripSimulator.SimulateSingleTrip(
    route,
    parameters,
    startTimeSeconds,
    direction)

TripSimulator.CalculateCycleTime(
    route,
    parameters,
    startTimeSeconds)

TripSimulator.SimulateMultipleTrains(
    route,
    parameters,
    trainCount,
    initialDepartureIntervalSeconds,
    startTimeSeconds)

SimulationEngine.Tick()
SimulationEngine.SetCurrentTime(simulationTimeSeconds)
SimulationEngine.GetTrainStates()
SimulationEngine.GetTrainStates(simulationTimeSeconds)
```

## 7. 驗證與邊界

| 輸入 | V1.0 行為 |
|---|---|
| `distance = 0` | 直接區間函數回傳零時間、零峰值與 `Instantaneous`；Route 仍拒絕重複 position |
| 三個性能值皆為正無限 | 僅供規格 Test 1，回傳 `Instantaneous` 且不產生 NaN；一般 TrainParameters 拒絕非有限值 |
| 最高速度、加速度、減速度為 0／負值／NaN | 清楚的 validation error |
| 停站或折返時間為 0 | 合法 |
| 停站或折返時間為負值／NaN | validation error |
| 路線少於 2 站 | validation error |
| 重複站號、第一站 position 非 0、position 未嚴格遞增 | validation error |
| `train_count <= 0` | validation error |
| 指定班距小於等於 0 | validation error |

浮點比較使用容差；代表性時間與位置測試使用 `0.01`，數學守恆測試使用更小容差。

## 8. 自動化測試

測試執行器包含 65 項案例，其中前 24 項為 V1.0 相容性測試：

- 無限制性能、零距離及非法性能。
- 5000 m 長距離梯形速度曲線。
- 200 m 短距離三角速度曲線。
- 停站、三站 position 累加、五站全程逐項加總。
- 起終點停站定義、折返公式、多列車理論及指定班距。
- 0.1 秒 Tick、加速、抵達、折返、上行 position 遞減。
- 多組距離／速度的時間與距離守恆。
- 0 停站、負停站、重複 position、單站及重複站號。
- V2 固定子步進、Jerk、惰行、平順抵站及提前速限煞車。
- 速限重疊、方向、輸入精度與範圍。
- 相鄰列車配對、反應時間、安全距離、移動閉塞控制及碰撞防護。
- 障礙物急停、排程、煞車模式切換與車輛／車次身分。
- CSV 跨日、軌跡降採樣、首班發車與路線邊界。
- 起點未淨空延後發車、終點長折返占用、碰撞事件去重，以及專案檔完整往返與損壞／未知版本拒絕。
- 動態 Jerk 煞車包絡線、到站前低速連續性，以及移動閉塞控制不瞬間歸零。
- 普通停站、跨站、車站通過速限、折返換用不同模式及舊版存檔升級。
- 三段式軟體版本格式及組件版本一致性。
- V3 雙向／跨午夜派車、目錄參照驗證、雙端發車、重複車輛拒絕、資源鎖定事件、區間統計 P95、schema 1 → 4 串接升級，以及 EngineKind／ProfileMode 分離。
- V3.1 端點停站／清車後退出、一般折返續行、手動指定反向接續車次，以及折返設定的專案檔往返與舊檔預設行為。

最新結果記錄於 `QA_REPORT.md`。

## 9. V2.1.0 資料結構

### SpeedLimitSegment

| 欄位 | 單位 | 定義 |
|---|---|---|
| `StartPositionMeters` | m | 累積里程起點，10 m 精度 |
| `EndPositionMeters` | m | 累積里程終點，不含終點邊界 |
| `LimitMetersPerSecond` | m/s | 有限正速限 |
| `Direction` | - | `Both`、`Outbound` 或 `Inbound` |
| `Note` | - | 使用者備註 |

同一位置同方向有多筆速限時取最低值；沒有有效速限時回傳列車最高速度。`GetPermittedSpeedMetersPerSecond()` 會向前檢查較低速限，依可用減速度與 Jerk 餘裕計算提前煞車曲線。

### OperationalParameters

包含 Jerk、惰行比例、停站進站上限、牽引力遞減比例、車長、營運／緊急煞車減速度、控制反應時間、煞車建立時間、定位誤差、安全餘裕與絕對最小淨距。停站進站上限為 `0` 時由動態煞車包絡線自動決定；正值才是額外速度上限。緊急煞車減速度不得小於營運煞車減速度。

### ServicePattern、ServiceRunPlan

- `ServicePattern` 以模式 ID 定義各站 `Stop`／`Pass` 指令；未列出的車站一律採 `Stop`。
- `Pass` 指令可附有限正數的車站通過速限；跨站時不套用停站煞車曲線或停站進站上限。
- 起點與終點不得設定為 `Pass`。保留模式 `ALL_STOP` 代表全部停站。
- `ServiceRunPlan` 依 `VehicleId`、服務序號與方向指定列車等級及模式。折返方向可使用另一筆計畫，因此同一實體列車往返可套用不同停站模式。
- 若完全沒有服務計畫，所有列車預設為 `普通車 / ALL_STOP`，維持舊版行為。

### WorldTrainState

位置明確定義為車頭累積里程，車尾依方向與車長計算。狀態同時保存：

- `VehicleId`：實體車輛，折返後不變。
- `ServiceRunId`：方向別運行車次，折返後更新。
- `ServiceClassId` 與 `ServicePatternId`：列車等級及本次運行使用的停站模式。
- `Direction` 與 `TrackId`：預設下行 `DOWN`、上行 `UP`，不同軌道不互相配對。
- 車頭／車尾位置、速度、加速度、相位、目前站、下一站、活動狀態與模擬時間。

### TrajectorySample、SafetyObservation、SimulationEvent

- 軌跡取樣保留車輛、車次、列車等級、服務模式、方向、軌道、位置、速度、加速度、相位與計畫／實際標記。
- 安全觀測保存前後車端點、車頭間距、淨距、時間間隔、動態安全距離、障礙煞車需求、預估停止里程、安全裕度及預測侵入量。
- 事件涵蓋發車、抵達、跨站通過、停站超限、停站、折返、安全狀態變更、控制煞車、障礙急停、預測碰撞、實際碰撞及煞車模式切換。

## 10. V2.1.0 實際營運軌跡

`OperationalTrajectoryPlanner` 使用固定 `dt = 0.1 s` 逐步積分，依目前目標速度把加速度以最大 Jerk 漸變，並套用：

1. 列車最高速度。
2. 隨速度遞減的牽引能力。
3. 使用者設定的惰行比例。
4. 由目前速度、加速度、營運煞車能力、Jerk 與剩餘距離逐 Tick 重算的停站煞車包絡線。
5. 現在及前方里程速限。

`BrakingEnvelopeCalculator` 使用與實際控制相同的減速度、Jerk 與 `0.1 s` 步長向前積分：

```text
a_next = MoveToward(a, -b, jerk × dt)
v_next = max(0, v + a_next × dt)
d_stop += (v + v_next) / 2 × dt
```

當預估煞停距離加一個 Tick 前視量到達剩餘距離時開始煞車。每一步先限制加速度變化，再更新速度與位置，正常運行的速度、加速度與位置保持連續。只有同時符合距停車點 `0.5 m` 及速度不高於 `0.15 m/s` 才判定到站；若高速越過停車點，保持煞車並記錄 `StationStopViolation`，不得無條件把速度歸零。障礙物急停是明確事件，可瞬間把指定前車速度設為 0，不納入正常連續性要求。

## 11. SimulationWorld

```csharp
new SimulationWorld(
    route,
    trainParameters,
    operationalParameters,
    trainCount,
    headwaySeconds,
    speedLimits,
    profileMode,
    movingBlockMode,
    servicePatterns,
    serviceRunPlans,
    dispatchPlan,
    vehicleTypes,
    infrastructure)

SimulationWorld.Tick()
SimulationWorld.AdvanceTo(targetTimeSeconds)
SimulationWorld.GetSnapshot()
SimulationWorld.SetMovingBlockMode(mode)
SimulationWorld.SetBrakingEstimationMode(mode)
SimulationWorld.TriggerObstacleEmergencyStop(vehicleId)
SimulationWorld.ScheduleObstacleEmergencyStop(vehicleId, triggerTimeSeconds)
SimulationWorld.Reset()
```

`Tick()` 必定只前進 `0.1 s`。`AdvanceTo()` 也會依序呼叫每個子步進，不能以播放倍率跳過控制或事件。尚未發車、退出營運、不在指定軌道或方向不同的列車不參與相鄰配對。

移動閉塞預設為 `Control`。控制模式會在發車前確認起點至最近前車車尾至少保留靜止安全距離（絕對最小淨距與定位誤差／安全餘裕兩者取大值）；若未淨空，保留 Pending 並在後續 Tick 重試。這項規則也防止極短指定班距在起點直接重疊。

## 12. 移動閉塞與安全包絡線

同方向、同軌道列車先依行駛方向排序，形成不重複的相鄰前後車配對。

```text
actual_gap = leader_rear_position - follower_front_position（依方向取正向距離）
head_to_head = 前後車頭沿行駛方向距離
time_gap = actual_gap / follower_speed

reaction_distance = follower_speed × reaction_time
build_up_distance = follower_speed × brake_build_up_time
follower_braking_distance = 以目前速度、加速度、Jerk、營運煞車與 0.1 s 步長向前積分的煞停距離
leader_braking_distance = leader_speed² / (2 × emergency_braking_rate)

dynamic_safety_distance = max(
    absolute_minimum_gap,
    reaction_distance + build_up_distance + jerk_transition_distance
    + max(0, follower_braking_distance - leader_braking_distance)
    + 2 × positioning_error + safety_margin)

safety_margin_value = actual_gap - dynamic_safety_distance
```

安全狀態依裕度分為 `Safe`、`Caution`、`BrakingRequired`、`EnvelopeIntrusion`。監視模式只觀測與記錄；控制模式以二分搜尋找出在可用距離內能依相同動態包絡線煞停的允許速度，再與列車性能及里程速限取最低值，另保留至少 `0.5 s` 的移動授權前視量。控制模式每 Tick 同時把 `actual_gap - dynamic_safety_distance` 當作後車移動授權，進站位置校正也不得越過授權邊界。即使監視模式不介入正常控制，最終碰撞防護仍會阻止列車穿越、負淨距或順序互換，並只留下單一碰撞事件。

營運／緊急煞車按鈕只切換估算比較使用的制動率；實際侵入安全距離時，保護控制可套用最高優先級煞車。

## 13. 障礙物急停情境

指定前車可立即或在指定模擬時間被標記為固定障礙。前車當下速度直接變為 0，位置與車身占用區間固定。後車每 Tick 重算反應距離、建立距離、制動距離、總需求、預估停止里程、安全裕度與預測侵入量；若到達障礙邊界則停止並記錄撞擊速度，不允許穿越。

此情境是比正常緊急煞車更保守的概念測試，不代表真實前車物理減速。

## 14. 運行圖與匯出

- 橫軸為連續模擬時間，縱軸為全線累積里程；時間格式支援跨日 `+N 日`。
- 計畫／理論軌跡由無干擾基準 `SimulationWorld` 產生；模擬實際軌跡套用選定速限、控制與事件。
- 視覺降採樣會強制保留各車次端點、相位轉折與事件節點。
- CSV 由 Engine 軌跡及事件直接產生，不重新估算位置或速度。
- PNG 使用 WPF 原生點陣輸出；PDF 使用程式內建的離線 PDF writer，可選 A4／A3 橫向與分頁，不需執行時下載套件。

## 15. 專案存檔格式

`SimulationProjectFormat` 使用版本化 UTF-8 JSON，預設副檔名為 `.mrtsim.json`，目前 `schemaVersion = 7`。現階段不支援舊存檔移轉；Schema 1～6、未知版本及未來版本都會拒絕。內容包含：

- 路線編號、名稱、車站順序、站間距離與個別停站時間。
- 列車性能、起終點折返時間，以及全部 V2 營運與安全參數。
- 任意里程速限、播放倍率及引擎／營運模式選擇；V2 發車完全由發車計畫決定，不使用 V1 的列車數、指定班距與首班時刻。
- 車型、服務類型、停站模式目錄，以及簡易班距／手動班表、端點續行與車輛配置模式。
- 停站模式的逐站停／跨、停站秒數覆寫及通過速限。
- 月台、股道、路徑、折返設施與站場配置。
- 五類空間參考點，以及中間站順／逆行獨立參數。
- 明確的 `SimulationEngineKind`，與 V2 的 `OperationProfileMode` 分開保存。

讀取時先限制檔案大小，再反序列化並做完整語意驗證；只有整份通過後 UI 才會替換目前設定。非 Schema 7、破損 JSON、缺欄位、無效列舉、目錄參照失效或超出模型範圍都會拒絕。儲存採同目錄暫存檔寫入後原子取代目標，降低中途失敗留下半份檔案的風險。

專案檔保存可重建模擬的設定，不保存播放到一半的列車瞬時位置、速度或事件歷史。

## 16. 輸入驗證與邊界

| 輸入／情境 | V3.3.0 行為 |
|---|---|
| 速限起點大於等於終點 | validation error |
| 速限超過全線或不是 10 m 精度 | validation error |
| 重疊速限 | 合法，採最低值 |
| Jerk、車長、煞車率非正有限值 | validation error |
| 比例不在 0～1 | validation error |
| 緊急煞車率小於營運煞車率 | validation error |
| 反應時間、建立時間、誤差或餘裕為負 | validation error |
| 首班車發車時間為世界起點 | 第一個 Tick 立即啟用，不延遲 0.1 s |
| 控制模式的極短班距 | 起點未淨空時延後發車，直到符合絕對最小淨距 |
| 終點折返仍占用 | 後車的進站位置校正受移動授權限制，不得侵入最小淨距 |
| 監視模式或障礙範圍被侵入 | 夾在合法路線邊界、停止並只記錄一次碰撞 |
| 起點或終點設為跨站 | validation error |
| 服務、停站模式或車次參照缺漏 | validation error，不猜測或補建執行資料 |
| 高速越過停車點 | 保持煞車並記錄停站超限，不以單 Tick 歸零掩蓋 |
| 專案檔破損或版本未知 | 拒絕讀取，UI 目前設定不變 |
| 空間參考點坡度使有效加／減速度小於等於 0 | validation error，不產生容量或折返旅行時間 |
| 未續行列車完成端點停站／清車 | 產生 `ServiceEnded`，設為 `OutOfService` 並從活動路線圖移除 |

## 17. V3.3 目錄與派車模型

`VehicleTypeDefinition`、`ServiceTypeDefinition` 與 `StopPatternDefinition` 分別表示硬體性能、營運身分及逐站停靠規則，三者以系統產生的穩定 ID 關聯，顯示名稱可獨立修改。一般 UI 顯示中文名稱，內部 ID 只用於 Schema 7 內部參照；刪除仍被發車計畫或其他目錄引用的項目時必須拒絕並回報原因。

每一 `PlannedServiceRun` 的 `VehicleTypeId` 會解析到獨立的 `TrainPerformance`；V2 `SimulationWorld` 不讀取首頁的 V1 全域性能值。因此不同車型可以在同一次模擬中具有不同的最高速度、加速度、營運／緊急煞車、Jerk、牽引衰減、惰行減速度與車長。

`DispatchPlanDefinition` 支援 `SimpleHeadway` 與 `ManualTimetable`。`DispatchPlanExpander.Expand()` 先驗證所有目錄參照，再產生不可變的 `ResolvedDispatchPlan`：

- 每一 `PlannedServiceRun` 都有計畫發車時間、方向、車型、服務類型、停站模式、`VehicleId` 與 `ServiceRunId`。
- 跨午夜以第一個計畫時間為錨點排序，不把午夜後車次錯排到前一天。
- 同一明確 `VehicleId` 不得同時指派給多個載入車次；目前不自行推測車輛周轉。
- 派車車次抵達終點後，依端點折返續行設定分流：未續行者完成站點停站／清車秒數後退出；續行者建立反向新 `ServiceRunId`，不重複建立 `VehicleId`。

## 18. V3 基礎設施與資源

`InfrastructureGraph` 包含 `PlatformDefinition`、`TrackSegmentDefinition`、`RoutePathDefinition`、`TurnbackPlanDefinition` 與 `StationYardDefinition`。舊專案透過 legacy adapter 建立上下行雙軌、方向別月台、相鄰站路徑及抽象端點折返，維持既有拓樸語意。

`RouteResourceReservationManager` 以資源 ID 原子預約一組路徑與起點月台。列車在計畫發車前檢查方向、車長、車型、服務類型、月台相容性與可用路徑；不足時維持等待並記錄原因。列車尾端淨空起點資源後釋放預約。

此機制是保守安全骨架，不等同完整聯鎖、道岔幾何、尾軌連續軌跡或多月台最佳化。

## 19. V3 結構化事件

除既有文字訊息外，`SimulationEvent` 可保存 `ServiceRunId`、車型、服務類型、停站模式、月台、路徑、計畫時間、實際延誤與限制旗標。V3 新增的主要事件包括：

- 計畫發車延誤與等待資源。
- 月台指派、進路預約與釋放。
- 預留的追越提出、完成與取消事件型別。

追越事件型別是後續擴充契約；V3 尚未執行普通車待避與快速車追越。

## 20. V3.3 區間統計

`IntervalStatistics.Analyze()` 直接讀取 `TrajectorySample` 與 `SimulationEvent`，依實際行車方向建立區間：

- 上下行分開判定，位置跨越在相鄰 `0.1 s` 樣本間線性內插；已有精確事件時優先使用事件。
- 完成與運行中區間分開；運行中樣本不納入平均、最小、最大與 P95。
- 完成樣本保存旅行時間、平均／峰值速度、相位時間與控制事件摘要。
- 可依方向、車輛、車次、車型、服務、停站模式、起訖模擬秒數與是否包含運行中資料篩選。
- 每一完成或運行中區間會由軌跡限制狀態累積移動閉塞受限秒數；明細、摘要與 CSV 使用同一計算結果。
- P95 使用 nearest-rank 規則。
- `BuildCsv()` 與 `BuildSummaryCsv()` 輸出中文欄名；UI 以 UTF-8 BOM 儲存，供試算表直接開啟。

## 21. 產品版本、引擎與 UI 邊界

V3.3.0 是產品版本，不是第三套模擬引擎。`SimulationEngineKind` 決定本次執行建立 V1 基礎引擎或 V2 寫實引擎；`OperationProfileMode` 只決定 V2 世界內的軌跡曲線，Schema 7 則只代表存檔契約，三者不可互相推導。V1 專案不建立 `SimulationWorld`；V2 編輯確認、引擎切換或專案讀取後會清除舊世界與區間統計。

## 22. 已知限制與後續擴充

- 僅支援抽象單一直線；不是地理地圖，也不宣稱完整空間幾何。
- 預設上下行不同軌道，尚未建立單線共用、交叉渡線、道岔與聯鎖。
- 尚未以逐段坡度、曲線阻力、超高、黏著變化及乘客上下車量動態修正列車性能；不同車型的額定性能已能逐車次套用，服務類型仍只表示普通／快速等營運身分。
- 未以真實路線資料校準；人工輸入結果不能宣稱重現特定捷運路線。
- 移動閉塞為概念模型，未涵蓋通訊失效、列車完整性、ATP／ATO／ATS 或安全完整性認證。
- V3 已提供區間統計與中文 CSV；仍不宣稱超車執行、多月台最佳化或安全認證。
