# MRT Route Simulation Engine - Model Specification V1.0

## 1. 核心原則

- Engine 與 WPF UI 完全分離，Engine 不參考 HTML、CSS、DOM、Canvas、WPF 或瀏覽器時間。
- 解析模型直接計算每個區間的精確旅行時間；Simulation Engine 以固定 `dt = 0.1 s` 推進顯示時間，並使用同一組解析相位求當下位置與速度。
- 核心單位只有公尺、秒、m/s、m/s²。km、km/h、分鐘及時鐘字串只出現在輸入轉換或輸出格式化。
- V1.0 僅處理單一直線路線與性能相同的多列車，不加入號誌、閉塞、追蹤或乘客模型。

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

測試執行器包含 24 項案例：

- 無限制性能、零距離及非法性能。
- 5000 m 長距離梯形速度曲線。
- 200 m 短距離三角速度曲線。
- 停站、三站 position 累加、五站全程逐項加總。
- 起終點停站定義、折返公式、多列車理論及指定班距。
- 0.1 秒 Tick、加速、抵達、折返、上行 position 遞減。
- 多組距離／速度的時間與距離守恆。
- 0 停站、負停站、重複 position、單站及重複站號。

最新結果記錄於 `QA_REPORT.md`。

## 9. 已知限制

- 沒有號誌、閉塞、列車追蹤、超車、故障或臨時限速。
- 沒有坡度、曲線阻力、不同車種或乘客上下車時間模型。
- 多列車只依班距獨立運行，不判斷衝突，因此班距是理論值。
- 只處理單一路線；UI 的路線圖是抽象直線，不是地理地圖。

## 10. 未來擴充建議（不屬於 V1.0）

- 保持現有 Engine API，相鄰增加號誌／閉塞層，不修改基礎物理公式。
- 以方向別 TrainParameters 支援上下行不同性能。
- 加入營運緩衝、最小班距、延誤傳播及實際時刻資料。
- 另建資料匯入與地理視覺層，避免把地圖或檔案格式混入 Engine。
