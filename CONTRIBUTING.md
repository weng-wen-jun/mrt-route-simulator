# Contributing to MRT Route Simulator

感謝參與本專案。這份文件說明 V4.0.2 的開發流程、架構邊界、驗證要求與 PR 準備方式。開始修改前請先讀 `AGENTS.md`；若涉及模擬模型、topology、物理、安全或 schema，再讀 `MODEL_SPEC.md`。

## 1. 開發分支

- 不直接在 `main` 開發或提交。
- 每個功能、修正或文件工作使用獨立 branch。
- 開始前確認 branch 與 `main` 的差異，避免把其他工作一起帶入 PR。
- 一個 PR 盡量只處理一個主題；不要順手重構不相關區域。

建議流程：

```bash
git switch main
git pull --ff-only
git switch -c <type>/<short-topic>
```

## 2. 修改前必讀

依任務性質閱讀：

- `AGENTS.md`：責任模組、主要檔案、禁止事項、驗證矩陣。
- `MODEL_SPEC.md`：V4.0.2 Track-first topology 契約、物理模型與 API 邊界。
- `TODO.md`：目前真正尚未完成的工作；只以「現行待辦」未勾選項目為準。
- `QA_REPORT.md`：最新測試基準、人工驗收範圍與已知限制。
- `HANDOFF.md`：新接手者的快速交接摘要。

產品版本以 `Directory.Build.props` 為單一權威來源。目前為 **V4.0.2**。

## 3. 架構邊界

專案為分層單體 WPF 應用：

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

### 必須遵守

- Engine 不依賴 WPF。
- V2 `SimulationWorld` 以 `TopologySimulationDefinition` 為正式輸入。
- V2 物理權威位置為：

```text
TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex
```

- `PositionMeters`、`ProjectedChainageMeters`、`RouteProjection`、`TopologyResultContext` 僅供結果／顯示邊界使用。
- `SimulationWorld.Tick()` 保持固定 **0.1 s** 子步進。
- footprint、occupancy、rear-clear、資源生命週期與 safety 判定維持 topology-native。
- 正式營運規劃資料保持為 `VehicleTypes + ServiceTypes + StopPatterns + Dispatch`。

### 不要做

- 不要在 UI 重算 Engine 已提供的物理、實際軌跡、移動閉塞或安全距離。
- 不要把 UI 使用的 km、km/h、分鐘字串滲入 Engine 核心計算。
- 不要恢復 legacy `ServicePatterns / ServiceRuns` 雙資料源。
- 不要把 compatibility `Route` 或 legacy `InfrastructureGraph` 重新變成 V2 runtime 權威資料。
- 不要因單一 UI 需求修改不相關 schema 或 Domain Model。

## 4. Schema / project file 修改規則

目前：

```text
TopologyProjectFormat.CurrentSchemaVersion = 8
SimulationProjectFormat.CurrentSchemaVersion = 7  // legacy / frozen timetable compatibility
```

若修改 topology project、legacy 匯入或 schema，至少同步檢查：

- `src/MrtRouteSimulator.Engine/TopologyProject.cs`
- `src/MrtRouteSimulator.Engine/TopologyProjectFactory.cs`
- `src/MrtRouteSimulator.Engine/SimulationProject.cs`
- `src/MrtRouteSimulator.App/MainWindow.ProjectFiles.cs`
- `src/MrtRouteSimulator.App/MainWindow.Topology.cs`
- `tests/MrtRouteSimulator.Tests/Program.cs`
- `tests/MrtRouteSimulator.Tests/TopologyRegressionTests.cs`
- 相關 `samples/*.mrtsim.json`
- `MODEL_SPEC.md`

所有讀檔流程都應維持「完整驗證成功後才替換目前 UI 狀態」的交易式邊界。

## 5. Bug fix 原則

修 bug 時，請依這個順序：

1. 先找出原本應負責的模組，不要從症狀所在畫面直接下手。
2. 建立最小可重現案例。
3. 先補或調整 regression test，使問題可以穩定重現。
4. 修正權威層；presentation 只做必要的轉換與顯示。
5. 跑相對應完整驗證。
6. 將實際結果寫入 PR；若改變目前交付狀態，同步更新 `QA_REPORT.md`／`TODO.md`。

## 6. 建置與測試

測試專案是可執行 runner，不是傳統 xUnit/NUnit 專案。

### Release build

```powershell
dotnet build MrtRouteSimulator.slnx -c Release
```

Release build 應維持 **0 warnings / 0 errors**。

### Engine regression runner

```powershell
dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release
```

不要把文件中的舊測試數當成固定門檻；以當下 runner 全數通過及最新 `QA_REPORT.md` 為準。

### WPF runner

```powershell
dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release
```

### 站場建立規則

修改 station construction、directed connection、track port 或相關 wizard 時，依需要再執行：

```powershell
powershell -ExecutionPolicy Bypass -File tests/ValidateStationConstruction.ps1
```

## 7. 驗證矩陣

依 `AGENTS.md` 的原則：

### Planning / Infrastructure / Spatial

至少：

```text
Release build
+ 全部現行 tests
+ 對應完整 sample 往返或建立 SimulationWorld
```

### SimulationWorld / 物理 / 安全 / 容量

必須：

```text
Release build
+ 全部自動化測試
+ 該邊界條件的 regression test
```

### Project persistence / schema

至少涵蓋：

- round-trip
- reference validation
- 無效／未來 schema 拒絕
- legacy field 拒絕或相容遷移規則
- 失敗讀檔不污染目前 UI / session
- 現行 samples 可載入並建立 topology-native world

### WPF / results / export

- 自動化 runner 是基本要求。
- 若涉及視覺、播放、縮放、DPI、視窗尺寸、PNG/PDF 或桌面互動，還需相對應人工驗收。
- 離屏 WPF runner 通過不代表不同 DPI 或完整桌面連續播放已完成驗收。

## 8. 文件同步

下列情況請同步更新文件：

- 改變模型／runtime 契約：更新 `MODEL_SPEC.md`。
- 改變開發定位或責任邊界：更新 `AGENTS.md` 或 `HANDOFF.md`。
- 改變當前完成狀態或驗證基準：更新 `QA_REPORT.md`。
- 完成或新增現行待辦：更新 `TODO.md`。
- 對外版本變更：依專案版本規則更新版本與 `CHANGELOG.md`。

不要把歷史 Phase、舊測試數或舊文件敘述當成目前狀態。

## 9. Commit 建議

commit 應小而聚焦，例如：

```text
fix: keep tail-track return stop on reverse platform
feat: add guided legacy track-port migration
test: cover passing rear-clear merge regression
docs: update V4.0.2 handoff status
```

避免：

```text
fix
update
changes
misc
```

如果同一工作包含模型修改、regression 與必要文件更新，可以保留在同一主題 PR；不必為了形式把高度耦合的修正拆成無法獨立理解的 PR。

## 10. Pull Request 要求

PR 至少說明：

- 要解決的問題／目標。
- 修改的責任層與主要檔案。
- 是否改變 V4 topology、schema、物理、安全或資料來源契約。
- Release build 與測試結果。
- 是否做過 WPF／桌面／DPI／匯出人工驗收。
- 是否更新 `MODEL_SPEC.md`、`TODO.md`、`QA_REPORT.md` 或其他文件。
- 已知限制與尚未完成事項。

請使用 `.github/pull_request_template.md` 作為提交檢查表。

## 11. PR 前最後檢查

- [ ] Branch 只包含本次相關變更。
- [ ] 沒有直接修改 `main`。
- [ ] 沒有重新引入 V2 runtime 的 legacy physical truth。
- [ ] 新增／修正的 bug 有 regression coverage。
- [ ] Release build 無 warning / error。
- [ ] 相對應 runners 全數通過。
- [ ] 人工驗收範圍與未驗收範圍都寫清楚。
- [ ] TODO / QA / MODEL_SPEC 等文件與程式碼狀態一致。
