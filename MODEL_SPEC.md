# MRT Route Simulation Engine - Model Specification V2.1.0

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

測試執行器包含 53 項案例，其中前 24 項為 V1.0 相容性測試：

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
    serviceRunPlans)

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

`SimulationProjectFormat` 使用版本化 UTF-8 JSON，預設副檔名為 `.mrtsim.json`，目前 `schemaVersion = 2`。內容包含：

- 路線編號、名稱、車站順序、站間距離與個別停站時間。
- 列車性能、起終點折返時間，以及全部 V2 營運與安全參數。
- 任意里程速限、列車數、指定／自動班距、首班時刻、播放倍率及三種模式選擇。
- 服務模式、各站停／跨與通過速限，以及方向別車次的列車等級和模式。

讀取時先限制檔案大小，再反序列化並以既有 `Route`、`TrainParameters`、`OperationalParameters`、`SpeedLimitService` 與 `SimulationWorld` 做完整語意驗證；只有整份通過後 UI 才會替換目前設定。`schemaVersion = 1` 會升級成版本 2，並預設採 `普通車 / ALL_STOP`；未知版本、破損 JSON、缺欄位、無效列舉或超出模型範圍都會拒絕。儲存採同目錄暫存檔寫入後原子取代目標，降低中途失敗留下半份檔案的風險。

專案檔保存可重建模擬的設定，不保存播放到一半的列車瞬時位置、速度或事件歷史。

## 16. 輸入驗證與邊界

| 輸入／情境 | V2.1.0 行為 |
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
| 未指定服務模式／車次 | 採 `普通車 / ALL_STOP` |
| 高速越過停車點 | 保持煞車並記錄停站超限，不以單 Tick 歸零掩蓋 |
| 專案檔破損或版本未知 | 拒絕讀取，UI 目前設定不變 |

## 17. 已知限制與後續擴充

- 僅支援抽象單一直線；不是地理地圖。
- 預設上下行不同軌道，尚未建立單線共用、交叉渡線、道岔與聯鎖。
- 尚未納入坡度、曲線阻力、超高、黏著變化、不同車型的物理性能及乘客上下車模型；目前 `ServiceClassId` 是普通／快速等服務等級，不是車型。
- 未以真實路線資料校準；人工輸入結果不能宣稱重現特定捷運路線。
- 移動閉塞為概念模型，未涵蓋通訊失效、列車完整性、ATP／ATO／ATS 或安全完整性認證。
- 後續可增加站間通過間隔分析、進階圖層篩選、真實資料校準與效能基準。
