# MRT Route Simulation Engine - Model Specification V4.0.1

> 現行規格（2026-08-31）。V3.0～V3.4 章節僅保留歷史模型說明；V2 寫實運行已改為 topology-native runtime。

產品版本為 V4.0.1。V1 解析模型可保留 `Route` 作為輸入 adapter；V2 `SimulationWorld` 一律使用 Schema 8 的 `InfrastructureGraphV4 + ServiceRoute`，不持有 compatibility `Route` 或 legacy `InfrastructureGraph`。

## V4.0.1 Track-first topology 執行契約

V2 的權威位置與資料流：

```text
TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex
```

`WorldTrainState`、`TrajectorySample` 與 `SimulationEvent` 都輸出這些欄位。所有移動、停站、速限、資源、footprint 與 safety 判定均以 cursor／physical traversal 為準；`PositionMeters` 和 `ProjectedChainageMeters` 僅為結果顯示衍生值。

V2 的實體移動流程：

```text
DirectedTrackTraversal + traversal index + edge-local offset
    ↓
TopologyResultContext / RouteProjection（顯示用）
    ↓
結果頁、CSV 與圖表
```

Phase F 以 `ResolvedStopResolver` 將每個 `ServiceRouteStop` 的候選月台解析為有序的 `ResolvedStop`：

```text
StationId + PlatformId + TrackPosition + traversal index + chainage
```

正常主線與所有 facility 的進站距離、煞車、到站吸附及通過速限都消費該 resolved stop。`TrackSpeedLimitService` 以 edge-local interval 評估速限及提前煞車。

`TopologySimulationDefinition` 是 V2 的唯一 world 輸入。舊表單 Route 若被傳入 V2，僅在建構前由 `TopologyProjectFactory.CreateLinearRuntimeTopology()` 轉成 Schema 8 graph；轉換會補上兩端實體 crossover、尾軌、switch／tail resource 與 `TurnbackOperation`，world 本身不保留 Route。

- 新增 `InfrastructureGraphV4`，其索引資料由 `TopologyInfrastructureDefinition` 驗證後建立。`TrackEdgeDefinition` 只引用 `FromNodeId`／`ToNodeId`，不含 StationId 或 global position；`PlatformDefinitionV4` 以 `TrackEdgeId + local offset` 定位，`StationDefinitionV4` 僅保存邏輯月台群組。
- `InfrastructureValidator` 驗證 ID、node/edge/resource 參照、edge 長度與速限、月台 offset、ServiceRoute traversal 連續與方向、以及停靠候選月台的 station／route 歸屬。`DirectedTrackConnectionDefinition` 可在 switch／crossing node 限制合法 edge-to-edge transition，並由 path finder 與 runtime navigator 使用。`ServiceRouteDefinition.Traversals` 是有序 list，允許 loop 重複 edge。
- `LinearInfrastructureBuilder.Build(Route)` 是現有快速線性輸入的 adapter：每一站間建立獨立上下行 edge、方向別月台及 ordered ServiceRoute；它不修改傳入的 legacy `Route`。
- `RouteProjection` 把單一 ServiceRoute 映射為 route-local chainage；forward/reverse offset 可逆。相同 edge 重複出現時，`ToChainage(TrackPosition)` 會拒絕歧義，必須使用帶 traversal index 的 overload。
- **V2 權威位置：**`TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex`。
- **實體設施：**尾軌、袋狀軌、crossover／turnback 與 passing facility 均由有序 `DirectedTrackTraversal` 表示；`TurnbackStopPosition` 是 edge-local `TrackPosition`，若停在 edge 中段，返回 traversal 必須從同一物理位置立即反向開始；資源在車尾 footprint 淨空後才釋放。
- **passing rear-clear：**快速車車頭已離開 passing edge、但車尾仍在該設施時，停在平行 local edge 的普通車必須保持待避；不得把兩條平行 edge 投影成負 safety gap。普通車僅能在 passing movement rear-clear 後匯入正線。
- **結果邊界：**`TopologyResultContext`、`RouteProjection`、`PositionMeters` 僅能用於 UI、統計與匯出，不能用於物理、occupancy、safety 或 routing。
- **V1 邊界：**V1 analytical API 可以使用 Route；不得由 V1 Route 回灌 V2 runtime。

## V3.4.2 本輪變更

- `StationStopController` 是 V2 排定停站的單一進站速度控制來源。它以剩餘距離、目前速度／加速度、營運煞車能力、Jerk 與 0.1 秒步長產生距離－速度曲線，並呼叫 `BrakingEnvelopeCalculator` 預測現在全營運煞車的停止距離。
- 預測停止距離超過剩餘距離 0.05 m 時，控制器要求營運煞車；否則由目標速度控制牽引、惰行或煞車。最後 12 m 的精停曲線額外限制為 3 m/s，並以 `sqrt(2 × 0.5 × max(0, D - 2 m))` 收斂至 2 m 近停吸附邊界。
- `SimulationWorld` 先取站點控制目標，再對障礙物、越行及移動閉塞取更低允許速度；因此新控制器不放寬既有保護。既有 `StationBrakingActive` 低速解除條件保留，排定停站不再以該旗標作為持續全煞車的唯一控制。

## V3.4.1 本輪變更

- `Automatic` 月台配置依成功配置數平衡同方向候選；`EarliestAvailable` 依實際月台最後釋放時間排序。候選均須通過原子資源預約，不可用時才改試下一座。
- `SimulationEvent.ResourceIds` 保存每次預約或釋放的實際資源。`ResourceOccupancyAnalysis` 由結構化事件重建各資源占用區間，輸出使用率、觀測每小時預約數及最短釋放間距。
- 越行候選可跨連續跨站區段搜尋，遇到快速車應停站即停止；選擇順序為可用資源、進站距離及設施 ID。越行中仍會保持普通車待避，方向專屬資源可讓上下行越行重疊執行。
- `VehicleTypeDefinition.DefaultStopPatternId` 是派車未明確指定時的第二優先停站模式來源；Schema 7 保存該欄位。五類型式範本與實體站分類完整化皆由基礎設施模型處理。
- 站後折返尾軌以固定 0.1 秒樣本連續記錄；返回端點後必須先產生反方向月台到達事件，再從相同月台發車。
- 站前與中央避車線折返採 `TURNBACK:<ReferencePointId>:OUT/RETURN` 執行期分段軌道。列車先依其空間參考點的停車點、橫渡線與中央避車線距離駛至虛擬折返點，停等後反向駛回實體錨定站；兩段均使用既有列車性能、道岔限速、坡度、Jerk 與 0.1 秒 Tick，路線圖只消費這些軌跡樣本。

## V3.4.0 本輪變更

- `StopPatternAction`／`StationServiceMode` 新增 `Turnback`。此指令只能設在非端點且具有 `CentralSidingTurnback` 空間參考點的虛擬站；列車先依設定停站，再進入折返資源與反向接續流程。
- `SimulationWorld` 在出發前預留目的停靠月台；列車尾端淨空進路後，預約縮為目的月台，持續保留至下一次發車或退出。端點站前交替月台與中央避車線折返另持有折返資源。
- 目的站站場採 `RoundRobin`，或站前折返設為 `alternateBerthing = true` 時，依前一次成功配置結果輪替相容月台；已占用月台會改試下一候選，全部不可用才記錄等待。
- `SimulationSession` 以 `SimulationWorldOptions` 建立實際與計畫世界，負責同一目標時間的固定 Tick 推進、重設及計畫事件時間線；WPF 僅消費快照與結構化輸出。
- `SimulationTraceRetentionPolicy` 可選完整、降採樣或僅事件。預設完整保留每個活動車輛的 0.1 秒樣本；降採樣仍保留規則時間點、相位／站點／軌道／月台／約束變化與有事件的車次，僅事件模式不保存軌跡樣本。
- 已完成的 V2 `SimulationWorld` 可匯出獨立 `.mrttimetable.json` 固定時刻表封存。封存包含正規化 Schema 7 專案設定與每個車次／車站的實際到離站、停站、誤點及狀態；重新讀取只呈現凍結結果，不把結果回寫成新的 `Dispatch` 資料源。
- `StationOvertakeFacilityDefinition` 可把越行限定在雙島四股站場：站間仍使用單一方向正線；普通車進入待避月台，具較高優先序且 `CanRequestOvertake = true` 的跨站快速車在站前取得進入、站內及站後衝突資源後切入通過線，完成跨越即匯回正線。普通車原定停站結束時，若快速車已進入接近範圍或站內越行中，須繼續待避。
- `TurnbackKind.AfterStation` 若在 `TrackSegmentIds` 指定一條下行與一條上行 `TailTrack`，兩線必須都連接端點站與 `TAIL:<TurnbackId>` 虛擬節點、並在相同的端點外側里程相接。列車依到達方向駛入對應尾軌，在虛擬節點停等後改走反方向尾軌回站；兩條尾軌及其衝突資源會原子保留。未指定 `TailTrack` 時保留既有解析旅行時間加折返停等的相容行為。

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
- 速度曲線以 `VehicleId` 串接同一實體列車的全部 `ServiceRunId`，依模擬時間繪製上下行、停站及折返，不依方向分圖。尚未播放的列車使用 `SimulationSession.PlannedTrajectory`；實際與計畫資料不拼接成同一曲線。
- topology 到站、出站、停站與跨站事件提供結構化 `StationId`，結果服務優先依站號、其次月台識別比對；不以訊息文字推測站號，也不以車頭越過月台中心代替到站事件。
- 尾軌返回反向出發月台後，先產生到站並執行反向停站時間，再等待接續班表及出發條件；零停站時間不代表可提前發車。
- `TrajectorySample.TrackSpeedLimitMetersPerSecond` 記錄 Engine 當下依車型、方向與 topology cursor 求得的速限，供顯示使用；移動閉塞的方向篩選仍獨立於速度圖。

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

測試執行器目前包含 107 項案例。Topology regression 驗證 domain、projection、正常主線、topology-first entry、有向道岔轉向、完整 physical facilities、passing rear-clear、edge-local physical turnback、Schema 8 編輯交易、dependency guard 與 legacy Schema 7 匯入轉換。

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
- V3 雙向／跨午夜派車、目錄參照驗證、雙端發車、重複車輛拒絕、資源鎖定事件、區間統計 P95、Schema 7 persistence 與舊／未知 Schema 拒絕，以及 EngineKind／ProfileMode 分離。
- V3.1 端點停站／清車後退出、一般折返續行、手動指定反向接續車次，以及折返設定的專案檔往返與舊檔預設行為。
- V4 topology 的逐站雙向 builder、Schema 7 一次性轉換、node／platform／traversal validation、forward／reverse projection、resolved stop、統一 movement plan、physical facility、footprint／occupancy／rear-clear、安全、結果資料流與 Schema 8 編輯操作。

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

- `ServicePattern` 以模式 ID 定義各站 `Stop`／`Pass`／`Turnback` 指令；未列出的車站一律採 `Stop`。
- `Pass` 指令可附有限正數的車站通過速限；跨站時不套用停站煞車曲線或停站進站上限。
- 起點與終點不得設定為 `Pass`。保留模式 `ALL_STOP` 代表全部停站。
- `Turnback` 只適用於設定為 `CentralSidingTurnback` 的非端點虛擬站；它不是一般端點退出，會保留月台／避車線資源並反向接續。
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
- 軌跡留存策略不改變 `Tick()` 的固定步進、`SimulationEvent` 或安全觀測；結果頁與 CSV 若需要完整曲線，必須使用預設完整策略。

## 10. V2.1.0 實際營運軌跡

`OperationalTrajectoryPlanner` 使用固定 `dt = 0.1 s` 逐步積分，依目前目標速度把加速度以最大 Jerk 漸變，並套用：

1. 列車最高速度。
2. 隨速度遞減的牽引能力。
3. 使用者設定的惰行比例。
4. `StationStopController` 依目前速度、加速度、營運煞車能力、Jerk 與剩餘距離逐 Tick 重算的停站距離－速度曲線及煞停預測。
5. 現在及前方里程速限。

`BrakingEnvelopeCalculator` 使用與實際控制相同的減速度、Jerk 與 `0.1 s` 步長向前積分：

```text
a_next = MoveToward(a, -b, jerk × dt)
v_next = max(0, v + a_next × dt)
d_stop += (v + v_next) / 2 × dt
```

`StationStopController` 先以距離－速度煞車曲線限制高速段；全煞停點預測越過停車點時才要求營運煞車。最後 12 m 再改用低速終端速度曲線，目標速度在 2 m 近停吸附邊界收斂至零，避免暫時低速後回到一般線速牽引。每一步先限制加速度變化，再更新速度與位置，正常運行的速度、加速度與位置保持連續。一般情況只有同時符合距停車點 `0.5 m` 及速度不高於 `0.15 m/s` 才判定到站；若殘距不超過 `2 m` 且速度已低於該門檻，則直接以停車點完成到站。若高速越過停車點，保持煞車並記錄 `StationStopViolation`，不得無條件把速度歸零。障礙物急停是明確事件，可瞬間把指定前車速度設為 0，不納入正常連續性要求。

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

`TopologyProjectFormat` 是現行 `.mrtsim.json` 的 persistence／runtime boundary，使用版本化 UTF-8 JSON，目前 `schemaVersion = 8`。WPF 新建、編輯與儲存一律輸出 Schema 8；內容包含：

- 專案識別、`TopologyInfrastructureDefinition` 的 node／edge／station／platform／resource、edge-local 速限與坡度。
- 有序 `ServiceRoute`、方向綁定、resolved stop 所需月台候選、實體 turnback／passing facility 與有向轉向。
- `VehicleTypes + ServiceTypes + StopPatterns + Dispatch` 單一營運資料來源。
- V2 營運、安全、播放與輸出所需設定；權威物理位置不使用 global `PositionMeters`。

`SimulationProjectFormat` 的 Schema 7 只保留為 legacy 線性專案匯入與固定時刻表相容資料。合法 Schema 7 專案會在建立 world 前一次性轉為 Schema 8 topology draft；Schema 1～6、未知／未來版本、Schema 8 legacy root fields、舊 `servicePatterns`／`serviceRuns`、缺欄位、無效列舉與失效參照均拒絕。

讀取時先限制檔案大小，再反序列化並做完整語意驗證；只有整份通過後 UI 才替換目前設定。儲存採同目錄暫存檔寫入後原子取代目標，降低中途失敗留下半份檔案的風險。

專案檔保存可重建模擬的設定，不保存播放到一半的列車瞬時位置、速度或事件歷史。

完成後固定時刻表使用獨立的 `FixedTimetableArchiveFormat`，格式版本目前為 `1`、副檔名為 `.mrttimetable.json`。它只在所有列車完成營運後由 `OperationsTimetable.Build()` 的實際事件建立；讀取時會同時驗證內含 Schema 7 專案、車站名稱／里程、實際時間的非負有限性與車次／方向／車站的唯一性。此封存不是動態模擬的檢查點，也不支援從中途續跑。

## 16. 輸入驗證與邊界

| 輸入／情境 | V4.0.1 現行保留行為 |
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

`InfrastructureGraph` 包含 `PlatformDefinition`、`TrackSegmentDefinition`、`RoutePathDefinition`、`TurnbackPlanDefinition`、`StationYardDefinition` 與 `StationOvertakeFacilityDefinition`。越行設施必須配置在至少四座月台、同方向至少兩座月台的雙島四股站，並明確指定共線正線、普通車待避月台／股道、快速車通過月台／股道、站前分歧位置及不可共用的衝突資源。站後折返可選擇成對 `TailTrack`，其非車站端使用 `TAIL:<TurnbackId>` 的虛擬節點；未配置時仍使用抽象端點折返。舊專案透過 legacy adapter 建立上下行雙軌、方向別月台、相鄰站路徑及抽象端點折返，維持既有拓樸語意。

`RouteResourceReservationManager` 以資源 ID 原子預約一組路徑、起點月台與（停靠時）目的月台。列車在計畫發車前檢查方向、車長、車型、服務類型、月台相容性與可用路徑；不足時維持等待並記錄原因。列車尾端淨空起點資源後釋放進路，但目的月台持續保留至下一次發車或退出；端點站前交替月台與中央避車線折返會在此期間加掛折返資源。

此機制是保守安全骨架，不等同完整聯鎖、道岔幾何或多月台最佳化；成對站後 `TailTrack` 已能提供尾軌往返的離散連續軌跡，但不代表可部署的實際聯鎖模型。

## 19. V3 結構化事件

除既有文字訊息外，`SimulationEvent` 可保存 `ServiceRunId`、車型、服務類型、停站模式、月台、路徑、計畫時間、實際延誤與限制旗標。V3 新增的主要事件包括：

- 計畫發車延誤與等待資源。
- 月台指派、進路預約與釋放。
- 站內追越提出、完成與取消事件型別。

站內越行只在 `StationOvertakeFacilityDefinition` 設定完整且快速車符合較高優先序、可請求越行、下一站為 `Pass`、普通車已進入指定待避月台等條件時執行。快速車若不能原子取得站前／站內／站後衝突資源，會在分歧點前煞停等待；不得以改寫位置或事件方式穿越普通車。完成後 `TrackId` 回到共線正線，移動閉塞、安全觀測與障礙物保護恢復以該正線配對。

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

產品版本以 `Directory.Build.props` 為唯一來源，不代表第三套模擬引擎。`SimulationEngineKind` 決定本次執行建立 V1 基礎引擎或 V2 寫實引擎；`OperationProfileMode` 只決定 V2 世界內的軌跡曲線；Schema 8 是現行 topology 專案契約，Schema 7 是 legacy 匯入契約，彼此不可互相推導。V1 專案不建立 `SimulationWorld`；V2 編輯確認、引擎切換或專案讀取後會清除舊世界與區間統計。

## 22. 已知限制與後續擴充

- V4 topology 是抽象軌道圖，不是地理地圖或可直接用於工程設計的真實軌道平面。尾軌、袋狀軌、crossover、passing 與折返均有實體 edge／traversal，但幾何仍是概念模型。
- 預設上下行使用不同軌道；尚未建立完整單線共用運轉與聯鎖失效模型。有向道岔轉向與 conflict resource 只提供概念層約束。
- 尚未以逐段坡度、曲線阻力、超高、黏著變化及乘客上下車量動態修正列車性能；不同車型的額定性能已能逐車次套用，服務類型仍只表示普通／快速等營運身分。
- 未以真實路線資料校準；人工輸入結果不能宣稱重現特定捷運路線。
- 移動閉塞為概念模型，未涵蓋通訊失效、列車完整性、ATP／ATO／ATS 或安全完整性認證。
- V4 已提供區間統計、中文 CSV、概念層多月台配置與實體 passing facility 越行；仍不宣稱全線自動超車排程、全域營運最佳化或安全認證。
## PDF 站型補充契約（2026-09-09）

- 站內原地折返可位於 edge 中段：僅限 Crossover 設施的兩個同 edge 反向 traversal，arrival／turnback／departure 使用同一中心錨點，月台須採 `StopPositionReference.TrainCenter`。runtime cursor 始終代表行進車頭：到站車頭為中心沿行車方向加半車長，換端後原車尾成為新車頭，整段 footprint 不變。PDF 站前折返 A:D 中心為 D0:180 m，A:U 為 U0:420 m；120 m列車在D0占用120–240 m，換端不再平移120 m。其餘尾軌／袋狀軌也須保持換端占用連續，並由 TURN-005 檢查停點前有足夠反向返回軌道涵蓋整車。
- 新建七種 PDF 站型採車體中心定位；舊專案未填 `StopPositionReference` 時維持 `TrainFront`。月台有效長度不可超過非零實體範圍，依每班車型及方向解析車頭後，完整車體須位於月台範圍內。中段尾軌銜接僅允許與月台停點一致且有向端點連續的進路，列車須實際走完剩餘到達軌道及返回出發月台。
- `StationChainageProjection` 是顯示里程：主下行起始站月台中心為0K，上下行同站中心對齊；起站外側尾軌負值，終點外側超過終點中心里程。路線圖站名、尾軌提示及即時車體中心欄位使用此投影。它不改變edge長度、車頭cursor、安全距離或既有進路距離統計；`GetTrainCenterPosition` 從movement navigator沿車頭後退半車長取得真實車體中心，可跨edge。
- 建置時以 `StationConstructionRules` 強制原地折返停點等於月台停點並位於其範圍內，到達末站及出發首站候選須包含同一月台；同站非空月台號碼不可重複。規則代碼及 WPF 驗收入口見 `STATION_CONSTRUCTION_RULES.md`。

- 線性專案轉換的方向別起站月台取自該 ServiceRoute 第一停點，不按站碼排序。安全 graph-distance 在跨 movement plan 或 ServiceRoute 尋路時，必須同時檢查起點 incoming traversal 與終點 outgoing traversal 的合法轉向；搜尋狀態包含進入節點的 traversal。共用 node 不能跳過 directedConnections 當成零距離跨股道捷徑。

- Schema 8 的 node 可選 `SchematicPosition`／`SchematicLane`、edge 可選 `SchematicLane`；platform 可選 `PlatformNumber`、`PlatformBodyId`、`DisplaySide`（Auto／Above／Below）。缺省保留自動呈現，同站相同 BodyId 合併成共用站體。示意位置須有限且絕對值不超過 1,000,000，股道須有限且絕對值不超過 8，無效列舉拒絕。
- 以上只供 WPF 繪圖與編輯保存，不改變 edge 長度、連通性、stop offset、footprint 或運行物理；Engine 權威仍是 topology cursor 和 directed connections。
- `StationLayoutTemplateService` 建立七種站場的實體 edges、platforms、routes、設施、目錄與派車。站前折返在月台停點原地反向，渡線在站外；站後折返實際駛入尾軌後返回。三／四股道以既有 PassingFacility 執行越行。
- 一般 ServiceRoute 若將經過雙向 PocketTrack 的 conflict resource，發車前預約整組剩餘相關資源，與既有設施共用 reservation manager；失敗時留下 WaitingForResource。頭部越過最後受保護 traversal 且所有 footprint 車尾淨空後釋放；Reset 清除預約。這是保守的發車互斥策略，並非完整單線區間調度或逐道岔進路最佳化。

### 實體接軌側別與建立防堵（2026-09-13）

TrackEdgeDefinition 的可選 FromPortSide／ToPortSide 表示節點局部 A/B 兩侧。兩端各屬不同節點，不要求同一 edge 的兩個值必定相反；同節點的行進接續必須由不同側相接。TrackPortRules 同時檢查顯式連接、ServiceRoute、facility traversal，尋徑也套用同規則。既有同 edge 停車換端及 footprint 驗證不變。

CONNECTION-001 表示同側回頭或有側別與無側別混接；CONNECTION-002 表示只填單端或非法列舉。側別是獨立保存的建置資料，不由 runtime 的 schematic 座標、股道或站點里程反推。站型模板在固定藍圖建置時寫入側別，後續移動示意位置不會重算；快速建線、分割及設施精靈延續此契約。尾軌／袋狀軌精靈的進出渡線各25m，使用者指定長度仍為折返軌主體長度。

Schema 8 維持向後相容：兩端皆未指定的舊 edge 仍採原有結構與有向轉向驗證，不能視為通過側別方向檢查；一般讀檔不猜值、不刪除連接。13份已檢視範例已完整補入側別。缺資料舊檔的引導式遷移另列 TODO。
