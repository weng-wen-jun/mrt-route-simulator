# MRT 路線進出站時間模擬器 — Codex 專案導覽與修改規則

> 用途：供 Codex / 自動化程式代理在修改本專案前快速判斷「功能屬於哪一層、先讀哪些檔案、哪些資料是權威來源、修改後必須驗證什麼」。
>
> 版本、格式與驗收基準不在本檔寫死：產品版本以 `Directory.Build.props` 為準；現行 topology 專案格式以 `TopologyProjectFormat.CurrentSchemaVersion` 為準；legacy 線性輸入格式以 `SimulationProjectFormat.CurrentSchemaVersion` 為準；最近驗收結果看 `QA_REPORT.md`。
>
> 本檔是**導覽與修改邊界**，不是完整模型規格。公式、資料型別、API 與邊界條件仍以 `MODEL_SPEC.md` 為準；版本變更看 `CHANGELOG.md`；驗收狀態看 `QA_REPORT.md`。

---

## 0. Codex 先讀這裡

### 子代理模型預設

- 開立子代理時，預設使用 `gpt-5.6-luna`，思考強度設為 `xhigh`。
- 只有任務確實需要較強的推理、跨領域判斷或高風險審核時，才提高模型能力；回報時應說明升級原因。

### 修改前的最短流程

1. 先用本檔「功能 → 所屬模組 → 主要檔案」定位責任範圍。
2. 若涉及物理公式、模擬狀態、容量或安全距離，先讀 `MODEL_SPEC.md` 對應章節。
3. 若涉及專案檔，確認 `TopologyProject.cs`、`SimulationProject.cs`、`MainWindow.ProjectFiles.cs`、Schema 8／legacy Schema 7 驗證與測試同步。
4. 若涉及 UI，不要直接在 UI 重算 Engine 已提供的結果。
5. 修改完成後執行 Release build 與完整自動化測試。
6. 不要因檔名含 `V2` 或 UI 標籤含 `V3.2` 就判定它是舊引擎；這些是歷史命名。

### Topology runtime 現況

- V2 `SimulationWorld` 的正式輸入是 `TopologySimulationDefinition`；world 不持有 compatibility `Route` 或 legacy `InfrastructureGraph`。
- Schema 8 是可編輯、可儲存及可直接執行的 topology 專案格式。舊線性表單與合法 Schema 7 專案只在建立 world 前轉為 Schema 8 draft，不再輸出新的 Schema 7 專案。
- 正常主線、尾軌、袋狀軌、crossover、折返與 passing facility 共用 topology movement plan；權威位置為 edge-local cursor，安全與資源生命週期使用 footprint、occupancy 與 rear-clear。
- 時刻表、區間統計、運行圖、CSV／PNG／PDF 與 WPF topology 工作區均消費 topology context，不得重新引入 Route 作為 V2 runtime 權威來源。
- V4 架構契約以 `MODEL_SPEC.md` 為準，驗收案例落在 tests 與 `QA_REPORT.md`；現行待辦只看 `TODO.md`，不要把已完成的歷史實作稿或 Phase 名稱當成待辦清單。

### 不要做的事

- 不要把 WPF 邏輯搬進 Engine。
- 不要在不同結果分頁各自重新推導時刻、軌跡或區間統計。
- 不要建立第二套車型／服務／停站／派車執行資料源。
- 不要繞過 `SimulationWorld` 另做一套 V2 寫實營運時間推進。
- 不要將 UI 使用的 km、km/h、分鐘字串直接滲入 Engine 核心計算。
- 不要因單一畫面需求就修改不相關的 Domain Model 或專案 Schema。
- 不要 commit、tag、push 或發布 Release；除非使用者明確要求。

---

# 1. 專案架構：以責任而不是檔名理解

本專案屬於**分層單體 WPF 桌面程式（layered monolith）**。`MainWindow.*.cs` 多數是同一個 WPF 主視窗 partial class 的功能拆檔；不要把每個檔案誤認成獨立服務。

```text
┌──────────────────────────────────────────────┐
│ Presentation / WPF                          │
│ MainWindow.xaml + MainWindow.*.cs           │
└──────────────────────┬───────────────────────┘
                       │ input / commands
                       ▼
┌──────────────────────────────────────────────┐
│ Domain & Project Models                     │
│ Planning / Infrastructure / Spatial / Save  │
└──────────────────────┬───────────────────────┘
                       │ executable model
                       ▼
┌──────────────────────────────────────────────┐
│ Simulation Core                             │
│ SimulationWorld + V1 analytical physics    │
└───────────────┬───────────────────┬──────────┘
                │                   │
                ▼                   ▼
┌──────────────────────┐  ┌────────────────────┐
│ Operational Services │  │ Analysis Services  │
│ Speed / Brake / etc. │  │ Time/Interval/etc. │
└──────────┬───────────┘  └──────────┬─────────┘
           └──────────────┬───────────┘
                          ▼
┌──────────────────────────────────────────────┐
│ Results / Visualization / Export            │
│ WPF result pages / PNG / PDF / CSV          │
└──────────────────────────────────────────────┘
```

### 核心依賴方向

```text
WPF UI
  ↓
Domain / Project Models
  ↓
SimulationWorld + Engine services
  ↓
Trajectory / Timetable / Interval / Events
  ↓
WPF Results / Export
```

允許 UI 呼叫 Engine；**Engine 不應依賴 WPF**。

---

# 2. 六個邏輯模組與責任邊界

## M1 — WPF Presentation

**用途**：輸入、按鈕、DataGrid、播放控制、圖表、結果顯示。

主要檔案：

- `src/MrtRouteSimulator.App/MainWindow.xaml`
- `src/MrtRouteSimulator.App/MainWindow.xaml.cs`
- `src/MrtRouteSimulator.App/MainWindow.InputEditors.cs`
- `src/MrtRouteSimulator.App/MainWindow.SpatialReferencePointEditors.cs`
- `src/MrtRouteSimulator.App/MainWindow.SpatialReferencePointDiagram.cs`
- `src/MrtRouteSimulator.App/MainWindow.V2.cs`
- `src/MrtRouteSimulator.App/MainWindow.V3Results.cs`
- `src/MrtRouteSimulator.App/MainWindow.IntervalStatistics.cs`
- `src/MrtRouteSimulator.App/MainWindow.ProjectFiles.cs`
- `src/MrtRouteSimulator.App/MainWindow.Topology.cs`
- `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`
- `src/MrtRouteSimulator.App/TopologyEditorViewModels.cs`
- `src/MrtRouteSimulator.App/UiModels.cs`
- `src/MrtRouteSimulator.App/UiDisplayText.cs`

### M1 可以做

- 顯示與收集輸入。
- 將 UI 單位轉成 Engine 單位。
- 建立 Domain Model / `SimulationWorld`。
- 將 Engine 結果轉成 DataGrid / 圖表顯示模型。
- 控制播放、暫停、重設與篩選。

### M1 不應做

- 重新實作列車物理。
- 自行推導與 Engine 不同的進出站時間。
- 自行計算第二份移動閉塞、安全距離或容量公式。
- 產生另一份與 `SimulationWorld` 不一致的「實際軌跡」。

---

## M2 — Domain / Planning / Infrastructure Models

**用途**：定義使用者規劃資料與可執行的基礎設施模型。

主要檔案：

- `src/MrtRouteSimulator.Engine/PlanningModels.cs`
- `src/MrtRouteSimulator.Engine/InfrastructureModels.cs`
- `src/MrtRouteSimulator.Engine/SpatialReferencePointModels.cs`
- `src/MrtRouteSimulator.Engine/TopologyModels.cs`
- `src/MrtRouteSimulator.Engine/InfrastructureGraphV4.cs`
- `src/MrtRouteSimulator.Engine/InfrastructureValidator.cs`
- `src/MrtRouteSimulator.Engine/ServiceRouteModels.cs`
- `src/MrtRouteSimulator.Engine/LinearInfrastructureBuilder.cs`
- `src/MrtRouteSimulator.Engine/RouteProjection.cs`
- `src/MrtRouteSimulator.Engine/TopologyRuntime.cs`
- `src/MrtRouteSimulator.Engine/TopologyProject.cs`
- `src/MrtRouteSimulator.Engine/TopologyProjectFactory.cs`
- `src/MrtRouteSimulator.Engine/TopologyEditingServices.cs`
- `src/MrtRouteSimulator.Engine/TopologyResultContext.cs`

### 權威來源

現行 V2 寫實引擎的正式營運規劃資料來源為：

```text
VehicleTypes
+ ServiceTypes
+ StopPatterns
+ Dispatch
```

其中：

- 每車次性能以 `VehicleTypeDefinition` 為權威來源。
- 班次以 `DispatchPlanDefinition` / 展開後的派車計畫為準。
- 停／跨站邏輯以 `StopPatternDefinition` 為準。
- 服務分類與停站模式是不同目錄，不要混併。

**不要恢復或建立舊 `ServicePatterns / ServiceRuns` 雙資料源。**

---

## M3 — Project Persistence / Schema

**用途**：`.mrtsim.json` topology 專案、legacy 線性專案匯入、序列化、反序列化與完整驗證。

主要檔案：

- `src/MrtRouteSimulator.Engine/TopologyProject.cs`
- `src/MrtRouteSimulator.Engine/TopologyProjectFactory.cs`
- `src/MrtRouteSimulator.Engine/SimulationProject.cs`
- `src/MrtRouteSimulator.App/MainWindow.ProjectFiles.cs`
- `src/MrtRouteSimulator.App/MainWindow.Topology.cs`

目前契約：

```text
TopologyProjectFormat.CurrentSchemaVersion = 8
SimulationProjectFormat.CurrentSchemaVersion = 7（legacy 匯入／固定時刻表相容資料）
```

目前政策：

- WPF 新建、編輯與儲存 `.mrtsim.json` 一律使用 Schema 8。
- 合法 Schema 7 專案可讀取並一次性轉成 Schema 8 draft；Schema 1～6 明確拒絕。
- Schema 8 不接受 Route、legacy Infrastructure 或舊執行資料源欄位；未知／未來 Schema 拒絕。
- 破損 JSON、缺欄位、無效列舉、目錄參照失效或數值越界均應拒絕。
- 必須整份驗證通過後才替換 UI 目前設定。
- 儲存採暫存檔後原子取代。

### 修改 Schema 時必查

1. `TopologyProject.cs`／`TopologyProjectFactory.cs`
2. `SimulationProject.cs`（若影響 legacy 匯入或固定時刻表）
3. `MainWindow.ProjectFiles.cs`／`MainWindow.Topology.cs`
4. `tests/MrtRouteSimulator.Tests/Program.cs`／`TopologyRegressionTests.cs`
5. `samples/V4.0.0-topology-baseline.mrtsim.json` 與完整 topology 範例
6. `MODEL_SPEC.md`
7. 必要時 `README.md`／`CHANGELOG.md`

---

## M4 — Simulation Core

**用途**：V2 topology-native 寫實營運狀態、固定時間步進、停站、實體設施 traversal、折返、接續、退出營運、資源與安全控制。

主要檔案：

- `src/MrtRouteSimulator.Engine/SimulationWorld.cs`
- `src/MrtRouteSimulator.Engine/V2Models.cs`

核心原則：

```text
SimulationWorld.Tick() = 固定 0.1 s
```

- 播放倍率只影響 UI 推進節奏，不可跳過 Engine 子步進。
- `AdvanceTo()` 也必須依序完成固定子步進。
- 列車實體身分與車次身分分開：
  - `VehicleId` = 實體車輛。
  - `ServiceRunId` = 方向別運行車次。
- 折返接續可沿用 `VehicleId`，但 `ServiceRunId` 依規則更新或接續指定既有反向車次。
- 未續行車完成端點停站／清車後退出營運；退出車不再參與路線顯示、安全配對或安全計算。

### 修改下列行為先看 `SimulationWorld.cs`

- 發車。
- 到站／停站／跨站。
- 折返。
- 端點退出營運。
- 反向接續。
- 資源鎖定／等待／釋放。
- 多列車相鄰配對。
- 移動閉塞控制。
- 障礙物急停。

---

## M5 — Physics / Operational Services / Analysis

此層不是一個檔案，而是數個服務，各自有單一責任。

### V1 Analytical Physics

- `AnalyticalModel.cs`
- `TripSimulator.cs`
- `SimulationEngine.cs`

用途：解析式三角／梯形速度曲線、V1 相容 API、部分折返距離旅行時間計算。

**不要為了 V2 UI 修改而破壞 V1 公開解析行為。**

### Speed limit

- `SpeedLimitService.cs`

用途：方向別里程速限、重疊取最低、提前煞車相關允許速度。

### Braking / Safety envelope

- `BrakingEnvelopeCalculator.cs`

用途：使用 Jerk、減速度與 0.1 s 積分估算煞停包絡線。

### Timetable

- `OperationsTimetable.cs`

用途：由派車、事件、軌跡產生計畫／實際進出站時刻資料。

### Interval physics / statistics

- `IntervalStatistics.cs`

用途：區間物理明細、完成／運行中樣本、P95、篩選與移動閉塞受限秒數。

### Trajectory

- `TrajectoryAnalysis.cs`

用途：軌跡分析、時間－里程圖資料與 CSV。

### Spatial capacity

- `SpatialCapacityAnalysis.cs`

用途：五類空間參考點之 URCS 相容 `Ts` / 班距 / 容量分析。

---

## M6 — Output / Verification

### Export

- `src/MrtRouteSimulator.App/DiagramExportService.cs`

用途：PNG / PDF 圖表輸出。

### Automated tests

- `tests/MrtRouteSimulator.Tests/Program.cs`
- `tests/MrtRouteSimulator.Tests/TopologyRegressionTests.cs`

測試清單以 runner 原始碼為準；最近一次完整執行的通過數與建置結果以 `QA_REPORT.md` 為準，不在本導覽固定寫死數量。

### Topology samples

- `samples/V4.0.0-topology-baseline.mrtsim.json`
- `samples/V4.0.0-完整拓撲執行驗證範例.mrtsim.json`
- `samples/README.md`

用途：Schema 8 基線與完整 physical facility 情境驗證；`samples` 內舊 V3.x 檔名只保留情境沿革，內容仍是 Schema 8。

---

# 3. MainWindow.* 是「同一 UI 類別拆檔」，不是多個獨立模組

Codex 在分析 UI 時，請把下列檔案視為同一個 Presentation 邏輯集合：

```text
MainWindow.xaml
MainWindow.xaml.cs
MainWindow.InputEditors.cs
MainWindow.SpatialReferencePointEditors.cs
MainWindow.SpatialReferencePointDiagram.cs
MainWindow.V2.cs
MainWindow.V3Results.cs
MainWindow.IntervalStatistics.cs
MainWindow.ProjectFiles.cs
```

歷史命名注意：

- `MainWindow.V2.cs` 仍是現行寫實模擬主要 UI / 執行入口之一。
- `V2Models.cs` 仍是現行使用中的核心資料結構。
- 某些 UI 顯示 `【V3.2】` 只表示功能首次導入版本，不代表資料仍走 V3.2 Schema 或舊核心。

不要為了「讓檔名看起來更新」任意改名；若要重構 partial class，必須先確認 XAML event handler、partial method 與所有參照。

---

# 4. 五類空間參考點：視為一個垂直子系統

此功能橫跨 UI、Domain Model、分析與 `SimulationWorld`，修改時必須整體追蹤。

```text
MainWindow.SpatialReferencePointEditors.cs
              │
              ▼
SpatialReferencePointModels.cs
              │
        ┌─────┴─────────┐
        ▼               ▼
SpatialCapacityAnalysis.cs     Infrastructure / SimulationWorld
        │               │
        ▼               ▼
MainWindow.SpatialReferencePointDiagram.cs
        │
        ▼
容量提示 / 幾何預覽 / 路線圖
```

五種類型：

```text
IntermediateStation   中間站
FrontTurnback         站前折返
RearTurnback          站後折返
PocketTurnback        中央避車線折返
Junction              銜接點
```

### Spatial 子系統的重要不變條件

- 中間站順行／逆行參數分開保存與驗證。
- `SpatialCapacityAnalysis.Calculate()` 以 URCS `Components.dll` 的 `CalTs()` / `CalCap()` 行為相容為目標。
- 容量結果使用與原程式一致的整數截斷行為。
- 坡度造成有效加／減速度 `<= 0` 時應拒絕容量計算。
- 空間折返距離可影響端點處理旅行時間；不要把它只當成 UI 圖示。
- 中間站方向別停站時間會進入實際模擬；不是純報表欄位。

### 修改 Spatial 功能至少檢查

- `SpatialReferencePointModels.cs`
- `SpatialCapacityAnalysis.cs`
- `MainWindow.SpatialReferencePointEditors.cs`
- `MainWindow.SpatialReferencePointDiagram.cs`
- `SimulationWorld.cs`（若值影響營運／端點處理）
- `SimulationProject.cs` + `MainWindow.ProjectFiles.cs`（若新增／改存檔欄位）
- 測試與完整範例

---

# 5. 功能 → 首要檔案路由表

| 要修改的功能 | 先看 | 再追 | 不要先改 |
|---|---|---|---|
| 主畫面配置、按鈕、欄位 | `MainWindow.xaml` | 對應 `MainWindow.*.cs` | Engine |
| 路線／車型／服務／停站／派車輸入 | `MainWindow.InputEditors.cs` | `PlanningModels.cs` | 結果頁 |
| legacy 線性表單／Schema 7 匯入 | `MainWindow.InputEditors.cs`、`SimulationProject.cs` | `TopologyProjectFactory.cs` | 直接建立第二套 V2 runtime |
| topology node／edge／platform／resource | `TopologyModels.cs`、`TopologyEditingServices.cs` | `TopologyEditorWindow.cs`、`InfrastructureValidator.cs` | 舊 `InfrastructureModels.cs` |
| ServiceRoute／projection／resolved stop | `ServiceRouteModels.cs`、`RouteProjection.cs` | `TopologyRuntime.cs`、`SimulationWorld.cs` | legacy `RoutePathDefinition` |
| 實體折返／尾軌／袋狀軌／越行 | `TopologyModels.cs`、`TopologyRuntime.cs` | `SimulationWorld.cs`、`TopologyEditingServices.cs` | virtual-track adapter |
| 建立／播放／暫停／重設 | `MainWindow.xaml.cs`、`MainWindow.V2.cs` | `SimulationWorld.cs` | 分析服務 |
| 列車移動 | `SimulationWorld.cs` | `V2Models.cs`、煞車／速限服務 | WPF 圖表 |
| 到站／停站／跨站 | `SimulationWorld.cs` | `PlanningModels.cs` | 結果表硬算 |
| 端點退出／折返／續行 | `SimulationWorld.cs` | `PlanningModels.cs`、`InfrastructureModels.cs` | 時刻表硬補 |
| 方向別速限 | `TrackSpeedLimitService.cs`、`SpeedLimitService.cs` | `SimulationWorld.cs`、`MainWindow.V2.cs` | Project model |
| 煞車包絡／安全距離 | `BrakingEnvelopeCalculator.cs` | `SimulationWorld.cs`、`V2Models.cs` | UI 重算 |
| 移動閉塞 | `SimulationWorld.cs` | `BrakingEnvelopeCalculator.cs`、`V2Models.cs` | `OperationsTimetable.cs` |
| 障礙物急停 | `SimulationWorld.cs` | `V2Models.cs`、結果 UI | Spatial model |
| 進出站時刻表 | `OperationsTimetable.cs` | `MainWindow.V3Results.cs`、World events | 獨立重跑模擬 |
| 區間物理 | `IntervalStatistics.cs` | `MainWindow.V3Results.cs` | UI 自算 |
| V3.3 區間統計 | `IntervalStatistics.cs` | `MainWindow.IntervalStatistics.cs` | `TrajectoryAnalysis.cs` |
| 時間－里程運行圖 | `TrajectoryAnalysis.cs` | `MainWindow.V2.cs` | 重新推估軌跡 |
| 五類空間點資料 | `SpatialReferencePointModels.cs` | Spatial UI + Capacity | `PlanningModels.cs` |
| URCS 容量公式 | `SpatialCapacityAnalysis.cs` | `MODEL_SPEC.md`、測試 | UI |
| 專案 JSON Schema | `SimulationProject.cs` | `MainWindow.ProjectFiles.cs`、測試、sample | UI Model |
| PNG / PDF | `DiagramExportService.cs` | `MainWindow.V2.cs` | Engine |
| DataGrid 顯示列 | `UiModels.cs` | 對應 MainWindow 結果頁 | Domain Model |
| 版本號 | `Directory.Build.props` | `VERSIONING.md`、測試 | 各檔案手改字串 |

---

# 6. 主要執行資料流

```text
使用者輸入／專案檔
  ├─ Schema 8 ──────────────────────────────→ TopologyProjectDocument
  └─ legacy Schema 7／快速線性表單 ── TopologyProjectFactory ──→ TopologyProjectDocument
  ↓
TopologyProjectFormat.CreateRuntime
  ├─ InfrastructureGraphV4 + ServiceRoutes + RouteProjection
  └─ VehicleTypes + ServiceTypes + StopPatterns + Dispatch
  ↓
DispatchPlanExpander（班距／手動班表 → 可執行車次）
  ↓
MainWindow.V2.cs 建立 SimulationWorld
  ↓
SimulationWorld.Tick() 固定 0.1 s
  ├─ 正常主線與實體 facility：統一 movement plan
  └─ TrackEdgeId + OffsetMeters + traversal index + footprint／occupancy
  ↓
TrajectorySample / SimulationEvent / Snapshot / SafetyObservation
  ↓
├─ OperationsTimetable
├─ IntervalStatistics
├─ TrajectoryAnalysis
└─ WPF 即時畫面
  ↓
MainWindow.V3Results / IntervalStatistics / V2 charts
  ↓
PNG / PDF / CSV
```

### 結果一致性原則

時刻表、區間物理、區間統計、速度曲線、移動閉塞與時間－里程運行圖應盡量讀取同一個 `SimulationWorld` 產生的結構化輸出。

**若某結果頁與其他頁數值不同，先追資料來源是否分叉，不要先用 UI 修正數值。**

---

# 7. 核心不變條件（修改時不得無意破壞）

## Engine / 單位

- Engine 核心單位：m、s、m/s、m/s²、必要時 m/s³。
- UI 可使用 km、km/h、分鐘與時鐘字串，但應在邊界轉換。
- 正常 V2 寫實控制固定 0.1 s 子步進。

## 車輛與車次

- `VehicleId` 與 `ServiceRunId` 不可混用。
- 折返後可同車不同車次。
- 指定接續既有反向車次時，不應另生成目標車次。
- 退出營運車不應再進入安全配對。

## 現行規劃來源

- 車型性能：`VehicleTypeDefinition`。
- 服務：`ServiceTypes`。
- 停站／跨站：`StopPatterns`。
- 發車：`Dispatch`。
- 不恢復舊雙資料源。

## Project

- Schema 8 是現行可編輯、可儲存與可執行的專案格式。
- Schema 7 僅保留合法舊專案匯入與固定時刻表相容資料；匯入後轉成 Schema 8，不得再另存新 Schema 7 專案。
- 讀檔要先完整驗證，失敗不可污染目前 UI 狀態。

## V1 / V2 相容

- V1 解析模型仍是保留功能。
- V2 `SimulationWorld` 是有狀態寫實營運核心。
- 不要為修 V2 顯示而改壞 V1 解析 API。

---

# 8. 修改類型與最低驗證集合

## A. 純 UI 排版／文字

至少：

```powershell
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
```

若 XAML event / binding 有變，執行完整 tests。

## B. Planning / Infrastructure / Spatial Model

至少：

```text
Release build
+ 全部現行 tests
+ 對應完整 sample 往返或建立 SimulationWorld
```

## C. SimulationWorld / 物理 / 安全 / 容量公式

必須：

```text
Release build
+ 全部自動化測試
+ 新增／修正針對該邊界條件的 regression test
```

若影響五類空間點，須保留 URCS 相容向量。

## D. Project Schema / 存檔

必須驗證：

```text
1. Schema 8 round-trip 與新存檔格式
2. Schema 7 合法匯入轉換，以及舊／未知 Schema 拒絕
3. 無效 topology／catalog reference 與 legacy 欄位拒絕
4. 取消／讀檔失敗不污染現有 UI
5. 所有 sample 可載入並建立 topology-native SimulationWorld
```

## E. 結果頁／統計

先確認輸入資料是否來自同一 `SimulationWorld`。若公式只屬於 presentation aggregation，可改分析服務；不要直接散落在多個 MainWindow 檔案。

---

# 9. 建置與測試

專案根目錄：

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
```

目前測試數與最近通過結果以 `tests/MrtRouteSimulator.Tests/Program.cs`、`TopologyRegressionTests.cs` 與 `QA_REPORT.md` 為準。若修改後基準下降，不要直接更新文件宣稱新基準；先判斷是否 regression。

---

# 10. Codex 閱讀順序：按任務選，不要全專案盲讀

## 一般功能修改

```text
AGENTS.md
→ 功能路由表指定的 UI / Engine owner
→ 相鄰 Model
→ 對應 tests
```

## 物理／模擬演算法

```text
AGENTS.md
→ MODEL_SPEC.md
→ SimulationWorld.cs 或對應 service
→ V2Models.cs
→ tests/Program.cs
```

## URCS / 五類空間點

```text
AGENTS.md
→ MODEL_SPEC.md 的 V3.2 / URCS 章節
→ SpatialReferencePointModels.cs
→ SpatialCapacityAnalysis.cs
→ SpatialReferencePointEditors / Diagram
→ SimulationWorld.cs（若影響營運）
→ tests
```

## 存檔格式

```text
AGENTS.md
→ TopologyProject.cs / TopologyProjectFactory.cs
→ MainWindow.ProjectFiles.cs / MainWindow.Topology.cs
→ SimulationProject.cs（legacy Schema 7／固定時刻表相容時）
→ sample JSON
→ tests/TopologyRegressionTests.cs
→ MODEL_SPEC.md 專案存檔格式
```

## UI 結果不一致

```text
MainWindow 對應結果頁
→ OperationsTimetable / IntervalStatistics / TrajectoryAnalysis
→ SimulationWorld 原始事件與軌跡
```

不要先在 UI 補數字。

---

# 11. 文件權威層級

遇到文件敘述衝突時，依任務類型判斷：

| 問題 | 優先來源 |
|---|---|
| 實際目前程式行為 | source code + tests |
| 物理公式／模型定義 | `MODEL_SPEC.md` |
| 版本唯一來源 | `Directory.Build.props` |
| 版本變更歷史 | `CHANGELOG.md` |
| 現行測試／Windows 驗收 | `QA_REPORT.md` |
| 使用者操作 | `README.md` |
| 未完成工作 | `TODO.md` |
| 架構導覽／修改範圍 | 本 `AGENTS.md` |

若文件與程式不一致，**不要自行假設哪邊正確**；應指出差異並以測試或使用者指定基準決定修改方向。

---

# 12. Codex 建議回報格式

完成程式修改後，回報盡量使用以下結構：

```text
變更範圍
- 修改哪些模組／檔案

行為變更
- 使用者可觀察到什麼
- 核心資料流是否改變

不變條件
- 哪些既有相容性刻意保留

驗證
- build 結果
- tests 結果
- 新增／更新哪些 regression case

風險／未涵蓋
- 尚未驗證的 UI / model / edge case
```

不要只回答「已完成」；應讓使用者能判斷修改是否跨越原本責任邊界。

---

# 13. 快速判斷：這個改動該不該跨層？

```text
只是顯示方式改變？
  → WPF / UiModels，通常不要碰 Engine。

輸入欄位新增，但會影響模擬？
  → UI + Domain Model + Engine owner；若要保存，再加 Project Schema。

模擬結果錯？
  → 先追 SimulationWorld / service，再看結果頁；不要 UI 修數字。

存檔後資料不見？
  → Project model + ProjectFiles + round-trip test。

五類空間點數字錯？
  → SpatialReferencePointModels + SpatialCapacityAnalysis + URCS regression vectors。

折返／退出／接續錯？
  → SimulationWorld + Dispatch / Infrastructure；再檢查 timetable 只是呈現問題還是 source event 錯。

速度曲線和運行圖不同？
  → 確認是否都取同一 ServiceRunId / TrajectorySample，不要各自重算。
```

---

# 14. 產品邊界與非目標

除非使用者明確要求擴充，Codex 不應假設下列功能已存在：

- 真實路線坡度、曲率、黏著、車型與時刻資料校準。
- 單線共用完整運轉。
- 完整聯鎖失效模型與營運最佳化。
- 真實軌道平面、設備配置或可直接用於工程設計的幾何；現有 tail／pocket／crossover／passing 是概念性實體 topology。
- ATP / ATO / ATS 或安全完整性認證。

本程式定位為**營運與號誌概念模擬器**，不是可部署的鐵路安全系統。

---

# 15. 最重要的四個架構判斷

1. **`MainWindow.*` 是 Presentation partial class 集合，不是多個真正獨立模組。**
2. **`SimulationWorld` 是 V2 寫實營運的核心狀態機；結果頁應消費它的輸出，而不是各自重跑。**
3. **五類空間參考點是一個垂直子系統，跨 UI → Model → Capacity → Simulation / Persistence，修改時不能只看單一檔案。**
4. **V2 runtime 的位置、設施 traversal、安全與資源權威均在 Schema 8 topology；Schema 7 只可作 legacy 匯入來源，不得重新引入 Route／virtual-track 作為 world 內部權威。**
