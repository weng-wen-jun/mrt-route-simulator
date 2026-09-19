# MRT 路線進出站時間模擬器 — 開發交接指南

> 適用版本：V4.0.1
> 目的：讓新的開發者或自動化程式代理在不重新考古整個 repository 的前提下，快速理解目前架構、權威資料來源、未完成工作與驗證方式。

## 1. 先讀什麼

開始修改前，依下列順序建立共同上下文：

1. `AGENTS.md`：功能對應模組、責任邊界、修改前後檢查與禁止事項。
2. `MODEL_SPEC.md`：V4.0.1 模型、資料型別、物理與 topology runtime 契約。
3. `TODO.md`：**只有「現行待辦」內未勾選項目代表目前尚未完成的工作**。
4. `QA_REPORT.md`：最近一次實際驗證、測試數、人工驗收範圍與已知限制。
5. `CHANGELOG.md`：需要追溯版本變更時再讀。
6. `README.md`：使用方式與一般專案說明。

版本不要從文件標題、檔名或 UI 標籤猜測；產品版本的單一權威來源是 `Directory.Build.props`。目前為 **V4.0.1**。

## 2. 目前架構一句話

本專案是分層單體 WPF 桌面程式；V2 寫實模擬已是 **topology-native runtime**，正式輸入為 `TopologySimulationDefinition`，Schema 8 topology 是目前可編輯、可儲存、可直接執行的專案格式。

```text
WPF Presentation
    ↓
Domain / Project Models
    ↓
SimulationWorld + Engine services
    ↓
Trajectory / Timetable / Interval / Events
    ↓
Results / Visualization / Export
```

核心依賴原則：**UI 可以呼叫 Engine；Engine 不應依賴 WPF。**

## 3. V4.0.1 必須守住的契約

### 3.1 V2 權威位置

V2 runtime 的物理位置以以下資料為準：

```text
TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex
```

`PositionMeters`、`ProjectedChainageMeters`、`RouteProjection` 與 `TopologyResultContext` 是顯示、統計或匯出邊界，不可重新變成物理、安全、occupancy 或 routing 的權威來源。

### 3.2 正式營運資料來源

不要重新建立第二套執行資料源。現行 V2 規劃資料為：

```text
VehicleTypes
+ ServiceTypes
+ StopPatterns
+ Dispatch
```

- 車輛性能：`VehicleTypeDefinition`
- 派車：`DispatchPlanDefinition` 與展開後派車計畫
- 停／跨站：`StopPatternDefinition`
- `VehicleId` 是實體車輛；`ServiceRunId` 是方向別運行車次

不要恢復舊 `ServicePatterns / ServiceRuns` 雙資料源。

### 3.3 專案格式

```text
TopologyProjectFormat.CurrentSchemaVersion = 8
SimulationProjectFormat.CurrentSchemaVersion = 7  // legacy 匯入／固定時刻表相容
```

現行政策：

- 新建、編輯、儲存 `.mrtsim.json` 使用 Schema 8。
- 合法 Schema 7 可以在建立 world 前一次性轉成 Schema 8 draft。
- Schema 1～6、未知／未來 schema、破損 JSON、無效參照與不合法欄位應拒絕。
- 讀檔必須整份驗證成功後才替換目前 UI 狀態。
- 不可在 V2 `SimulationWorld` 中重新保留 compatibility `Route` 或 legacy `InfrastructureGraph`。

### 3.4 模擬與安全

- `SimulationWorld.Tick()` 固定使用 **0.1 s** 子步進。
- 播放倍率只改變 UI 推進節奏，不可跳過 Engine 子步進。
- 主線、尾軌、袋狀軌、crossover、折返與 passing facility 應共用 topology movement plan。
- footprint、occupancy、rear-clear、資源釋放與安全判定必須維持 topology-native。
- UI 不應重新實作列車物理、移動閉塞、安全距離或另一份實際軌跡。

## 4. 常用責任區

### WPF / Presentation

主要集中於：

- `src/MrtRouteSimulator.App/MainWindow.xaml`
- `src/MrtRouteSimulator.App/MainWindow.*.cs`
- `src/MrtRouteSimulator.App/TopologyEditorWindow.cs`
- `src/MrtRouteSimulator.App/TopologyEditorViewModels.cs`
- `src/MrtRouteSimulator.App/DiagramExportService.cs`

這一層負責輸入、顯示、播放、結果頁與匯出；不要在這裡另造 Engine 已提供的物理或營運真值。

### Domain / topology / persistence

主要集中於：

- `PlanningModels.cs`
- `InfrastructureModels.cs`
- `TopologyModels.cs`
- `InfrastructureGraphV4.cs`
- `InfrastructureValidator.cs`
- `ServiceRouteModels.cs`
- `LinearInfrastructureBuilder.cs`
- `RouteProjection.cs`
- `TopologyRuntime.cs`
- `TopologyProject.cs`
- `TopologyProjectFactory.cs`
- `TopologyEditingServices.cs`
- `TopologyResultContext.cs`
- `SimulationProject.cs`

如果修改 schema、路線建立、設施、directed connection 或 legacy 匯入，務必同步檢查 validator、factory、WPF 讀寫與 regression tests。

### Simulation Core

主要集中於：

- `src/MrtRouteSimulator.Engine/SimulationWorld.cs`
- `src/MrtRouteSimulator.Engine/V2Models.cs`

發車、到站、停站、跨站、折返、反向接續、退出營運、資源鎖定／釋放、多列車安全與 facility traversal 先從這裡查。

## 5. 目前交付狀態（2026-09-19）

最新 `QA_REPORT.md` 的 V4.0.1 驗證基準：

- Release build：**0 warnings / 0 errors**。
- Engine runner：**145 / 145** 通過。
- 完整 WPF runner：通過；包含全部範例、速度圖、移動閉塞方向篩選、完整 topology、TrainCenter 情境與 CSV／PNG／PDF 輸出。
- PDF 分頁已改為各頁重繪標題、圖例、座標軸與頁內列車標籤；A4 兩頁輸出已以 Poppler 渲染檢查，未見跨頁切斷。
- `TopologyScenarioBuilder`／`TopologyScenarioValidation` 已完成大型 sample 的分階段 Structural／Operational gate，Scenario Manifest 規範同步寫入 `samples/README.md`。
- 離屏 WPF／程式驗證不等於不同 DPI 與完整桌面連續播放人工驗收。

不要把歷史版本的測試數當成現在基準；每次回報以最新 `QA_REPORT.md` 為準。

## 6. 現行待辦

依 `TODO.md`「現行待辦」，交接時仍需注意：

1. **Legacy port migration**：已加入缺資料 edge 盤點、逐 edge 明確 A/B assignment API 與 WPF「套用明確側別／保留相容讀取／取消」引導；不可從示意位置直接猜測實體方向。仍須完成原生桌面流程驗收，並確認產品是否要把遷移後側別設為所有舊檔的全面強制政策。
2. **臺中機場捷運大型案例**：尚缺 O01～O26（含 O08a、O15a）的正式來源資料與完整營運班表；未經來源或明確 synthetic 核准，不可把目前 O01～O06 baseline 擴寫成真實案例。
3. **桌面／DPI 完整驗收**：已完成部分實機抽查，但不同 DPI、折返／交會關鍵畫面與連續播放仍未完成完整驗收。

PDF 分頁輸出與通用 `TopologyScenarioBuilder` 已於 2026-09-19 完成；不要把這兩項重新列為待辦。

處理待辦時，完成條件必須同時反映到 source、tests、`QA_REPORT.md`，並在確認真正完成後更新 `TODO.md`。

## 7. 修改前的最短流程

1. 從 `AGENTS.md` 判斷責任模組與主要檔案。
2. 涉及物理、狀態、安全、容量或 topology 契約時，先讀 `MODEL_SPEC.md` 對應章節。
3. 涉及 schema 或專案檔時，同步檢查 Engine model/factory、WPF 讀寫與 tests。
4. 先找既有 regression；修 bug 時新增能重現原問題的測試。
5. 保持改動範圍局部，不要因單一 UI 需求修改不相關 Domain Model 或 schema。
6. 完成後依改動類型跑 Release build 與相對應完整驗證。

建立大型真實案例時，先以 minimal topology baseline 建立可執行基準，再逐類加入完整 station chain、service pattern、turnback、passing 與 timetable。驗證分為 Structural、Operational、Regression 三層；開發中先完成 Structural 與局部 Operational smoke test，階段完成後才跑 Regression（Release／Engine／WPF）驗證。

## 8. 本機驗證基準

本專案測試 runner 為可執行專案，而不是傳統 xUnit/NUnit test project。常用命令：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release
```

若修改站場建立規則，再依需要執行：

```powershell
powershell -ExecutionPolicy Bypass -File tests/ValidateStationConstruction.ps1
```

驗證層級依 `AGENTS.md`：

- Planning / Infrastructure / Spatial：Release build + 全部現行 tests + 對應 sample 建立／往返。
- SimulationWorld / 物理 / 安全 / 容量：Release build + 全部自動化 + 對應邊界 regression。
- WPF / 結果／匯出：除自動化外，涉及視覺、DPI、播放或匯出時還需要對應人工驗收；不可只用離屏 runner 宣稱完整桌面驗收。

## 9. Git 與 PR 交接原則

- 不直接在 `main` 開發；每個工作使用獨立 branch。
- 開始工作前先確認 branch 與 `main` 的差異，避免夾帶不相關 commit。
- commit 要小而可解釋，訊息描述「做了什麼」，不要只寫 `fix`／`update`。
- PR 需說明：問題、修改範圍、架構影響、驗證結果、未完成事項與文件更新。
- 若修改會改變 `MODEL_SPEC.md` 所描述的契約，文件與程式碼應在同一 PR 同步更新。
- 不要把已知未完成事項寫成已完成；未驗證的 DPI、桌面播放或人工視覺檢查需明確標示。

## 10. 新接手者第一個小時建議

1. 讀完本檔與 `AGENTS.md` 的「Codex 先讀這裡」。
2. 閱讀 `MODEL_SPEC.md` 最前面的 V4.0.1 Track-first topology 執行契約。
3. 只看 `TODO.md` 的「現行待辦」。
4. 閱讀 `QA_REPORT.md` 最新一節與「桌面實機驗收進度」。
5. 跑一次 Release build、Engine runner、WPF runner，建立自己的 clean baseline。
6. 再開始修改第一個 issue。

如果文件與程式碼互相矛盾，優先重新確認 source 與 tests；架構契約以 `MODEL_SPEC.md` 為準、目前完成狀態以最新 `QA_REPORT.md` 與 `TODO.md` 為準，並在 PR 中修正文檔漂移。
