# Codex 任務：開源鐵路模擬器架構調研與本專案可借鑑方案

## 任務目的

請針對 `weng-wen-jun/mrt-route-simulator` 調研與本專案定位相近的開源鐵路／捷運模擬器，分析哪些架構、演算法與效能設計值得借鑑。

本任務不是找一套專案取代本專案，而是要回答：

1. 哪些成熟 OSS 正在解決與本專案相同的問題？
2. 它們如何處理 topology、timetable、physics、moving block、resource reservation、passing、turnback、deadlock、UI/engine separation、parallelism？
3. 哪些設計可以轉化成本專案的實作？
4. 哪些不適合？
5. 哪些 code 受 license 限制不能直接搬用？

---

## 本專案背景

Repository：

`weng-wen-jun/mrt-route-simulator`

基準分支：

`main`

Schema 8 / V2 runtime physical truth：

```text
TrackEdgeId
OffsetMeters
ServiceRouteTraversalIndex
```

以下只能做顯示／統計／輸出：

```text
PositionMeters
ProjectedChainageMeters
RouteProjection
TopologyResultContext
```

規劃與 runtime 正式來源：

```text
VehicleTypes
ServiceTypes
StopPatterns
Dispatch
```

不得復活 legacy planning/runtime source。

目前能力包含：

```text
topology-native graph
directedConnections
port-side rules
service routes
0.1 s fixed tick
acceleration / braking / jerk
moving block
safety distance
collision
resource reservation
platform allocation
tail / pocket / turnback
crossover
passing / overtaking
multi-train timetable
trajectory / safety history
WPF live playback
speed / time-distance charts
resource occupancy
CSV / PNG / PDF export
```

近期大型案例包含：

```text
臺中機場捷運 O01–O26
O08a / O15a
O04 passing
O13 passing
O20 rear-pocket turnback
FULL-LINE
SECTION
AIRPORT-DIRECT
```

目前效能基準約：

```text
Fixed timestep = 0.1 s
代表大型案例 ≈ 2.75 ms/tick

真正 60× realtime 需要：
< 1.667 ms/tick
```

目前架構優化方向：

```text
ActualWorld = single writer
Simulation worker off UI thread
Immutable PlaybackFrame
Latest-frame-wins
Incremental ResultAccumulator
Planned timeline independent worker
Hidden-tab lazy refresh
Differential ObservableCollection update
```

---

# 必須調研的 OSS

## 1. OSRD

Upstream：

`https://github.com/OpenRailAssociation/osrd`

重點研究：

```text
infrastructure graph
microscopic simulation
timetable
signalling
interlocking
capacity
headless simulation
large-scale execution
simulation/result separation
parallel/scalable architecture
```

請找實際 code，不只 README。

回答：

- infrastructure 如何建模
- simulation state 如何持有
- timetable 與 runtime 如何分離
- fixed-step / event-driven / hybrid
- signalling / interlocking 資料流
- parallelism
- result representation
- 是否有 simulation worker / immutable results 概念

---

## 2. Eclipse SUMO Railway

Upstream：

`https://github.com/eclipse-sumo/sumo`

重點：

```text
rail signals
moving block
train following
safe distance
bidirectional tracks
deadlock prevention
timetable constraints
reversal
simulation step length
action step length
```

特別研究：

### Simulation step 與 Action step

分析 SUMO 將：

```text
simulation step
vehicle action step
```

分開的實作與限制。

評估本專案是否可能採：

```text
physics integration = 0.1 s
expensive control decision = lower frequency / event-driven
critical safety = still guaranteed every required interval
```

但不可直接假設可套用。

需特別檢查：

```text
jerk
station stopping
moving block
rear-clear
resource arbitration
```

是否允許控制更新降頻。

---

## 3. DesRail

請搜尋目前最新 upstream repository。

重點：

```text
high-performance railway simulation
discrete-event simulation
passing loop
signal locking
section access
multi-train operation
simulation / visualization separation
```

這是本次最重要的架構對照之一。

請深入分析：

```text
Simulator
→ result / animation artifact
→ Visualizer
```

或實際等價架構。

回答：

- simulator 與 visualizer 如何解耦
- UI 是否完全不控制 physics
- event 如何表達
- passing 如何表達
- signal / section locking 如何表達
- 如何快速跑完整 scenario
- 是否有 coroutine / event queue / scheduler
- 這種設計哪些可借給本專案

---

## 4. NeTrainSim

Upstream：

`https://github.com/VTTI-CSM/NeTrainSim`

研究：

```text
network train simulation
longitudinal dynamics
energy
large network
Qt GUI
```

特別關注：

```text
traction
mass
gradient resistance
curve resistance
aerodynamic resistance
energy consumption
GUI / engine separation
parallel execution
```

這部分主要作為本專案未來強化列車物理的參考。

---

## 5. TrainApp

請確認目前最新 upstream。

重點：

```text
graph railway
dispatcher
interlocking
resource reservation
event-driven simulation
deterministic behavior
```

請和本專案：

```text
RouteResourceReservationManager
TopologyMovementOccupancyIndex
WaitingForResource
RouteReserved
RouteReleased
```

比較。

---

## 6. TS2

Upstream：

`https://github.com/ts2/ts2`

重點：

```text
signal routes
points
train schedules
stop at signal
reversal
shunting
dispatcher UI
```

主要研究：

- dispatcher UX
- signal route UX
- timetable editor
- human-in-the-loop operation

不要把它當主要 physics engine 參考。

---

# 額外搜尋

若發現其他高度相關專案，可加入。

優先搜尋：

```text
open source railway microscopic simulation
open source metro simulator
railway discrete event simulator
moving block railway simulation
railway interlocking simulator github
railway timetable simulation open source
train dispatch simulation github
```

不要只列 toy project。

需有：

- 實際 codebase
- 可辨識 architecture
- domain model
- 有效 upstream 或完整歷史

---

# License 檢查

對每個 upstream 實際讀：

```text
LICENSE
COPYING
NOTICE
README license section
```

不要憑記憶猜。

輸出：

| Project | License | 可否直接複製 code | 可否研究架構 | 備註 |
|---|---|---|---|---|

原則：

```text
可以研究架構思想
可以重新實作通用演算法
不得直接複製 license 不相容程式碼
```

若不確定：

```text
Unknown / Requires Legal Review
```

不要自行給法律結論。

---

# 主比較表

建立：

`docs/OPEN_SOURCE_RAIL_SIM_COMPARISON.md`

至少比較：

| 項目 | 本專案 | OSRD | SUMO | DesRail | NeTrainSim | TrainApp | TS2 |
|---|---|---|---|---|---|---|---|

比較維度：

```text
Primary use case
Infrastructure graph
Track edge model
Direction model
Timetable model
Fixed-step / event-driven / hybrid
Physics fidelity
Moving block
Fixed block
Signals
Interlocking
Resource reservation
Platform assignment
Passing/overtaking
Turnback/reversal
Tail/pocket track
Deadlock handling
Collision prevention
Determinism
Large network support
Simulation/UI separation
Headless execution
Parallelism
Result storage
Visualization
Desktop UI
Export
Test strategy
License
```

---

# 必須回答的技術問題

## A. Fixed Tick vs Discrete Event

本專案目前：

```text
0.1 s fixed timestep
```

請比較各 OSS：

```text
fixed-step
event-driven
hybrid
```

並回答：

```text
本專案哪些部分必須 fixed-step？
哪些可 event-driven？
哪些可 hybrid？
```

至少分析：

```text
acceleration
braking
jerk
moving block
dwell
route lock
turnback
passing
resource release
```

不要只說「DES 比較快」。

---

## B. Moving Block / Leader Lookup

比較 SUMO、OSRD 與其他方案。

本專案關注：

```text
leader/follower
dynamic safety distance
braking requirement
rear-clear
collision prediction
```

請找：

```text
nearest leader indexing
track ordering
spatial index
braking envelope
control action interval
safety update interval
```

特別回答：

> 本專案是否可從近似 O(N²) train-pair scanning，改成按 track/direction/physical order 建 index，再做 nearest-leader lookup？

若可以，請提出具體資料結構，並說明如何與既有 `TopologyMovementOccupancyIndex` 整合，避免第二套 physical truth。

---

## C. Simulation / UI Separation

比較：

```text
DesRail
OSRD
SUMO GUI
NeTrainSim
```

回答：

- engine 是否 headless
- UI 是否只 consume result/snapshot
- offline / realtime
- result 是否 immutable
- UI 是否可 drop frames
- simulation 是否受 UI cadence 影響

再對照本專案：

```text
SimulationPlaybackWorker
PlaybackCoordinator
PlaybackFrame
ResultAccumulator
```

提出具體建議。

---

## D. Parallelism

請看實際 upstream code。

研究：

```text
train-level parallelism
network partition
parallel event processing
worker pool
async pipeline
background result computation
parallel export
```

對每種標記：

```text
SAFE FOR THIS PROJECT
POSSIBLE WITH REFACTOR
NOT RECOMMENDED
```

---

# 禁止方向

不要直接建議：

```text
Parallel.ForEach(_trains) 直接修改 world
```

本專案必須保持：

```text
deterministic resource arbitration
route reservation
platform allocation
passing order
turnback ownership
event ordering
```

不要：

- 降低 safety
- 跳 physics tick
- 把 0.1 s 改 1 s 作主要效能解法
- UI 直接讀 background mutable world
- ProjectedChainage 變成 physics truth
- 建第二套 runtime
- 復活 legacy Route authority
- 降低 validator

---

# 可借鑑項目分類

在 `docs/OPEN_SOURCE_RAIL_SIM_COMPARISON.md` 中至少分：

## A. 建議直接採用的架構思想

例如：

```text
engine/UI separation
headless simulation
immutable result artifact
latest-frame-wins
incremental analysis
resource graph
nearest-leader index
```

## B. 建議 prototype 的項目

例如：

```text
action-step style control interval
hybrid fixed-step + event-driven
parallel read-only train proposal
spatial ordering
```

## C. 不適合本專案

例如：

```text
pure timetable-only DES
game-style simplified physics
nondeterministic train mutation
```

每項要寫理由。

---

# Architecture Recommendation

新增：

`docs/OPEN_SOURCE_ARCHITECTURE_RECOMMENDATIONS.md`

提出 roadmap：

## Phase 1：低風險

```text
simulation/UI separation
actual-only playback
planned artifact
result accumulator
lazy rendering
```

## Phase 2：資料結構／演算法

```text
leader indexing
safety spatial ordering
incremental occupancy
resource event delta
```

## Phase 3：受控平行

```text
read-only tick proposal parallelism
deterministic arbitration
parallel offline planned simulation
parallel analysis/export
```

## Phase 4：進階研究

```text
hybrid fixed-step + event-driven
action/control interval
adaptive expensive-control cadence
```

不可沒有 regression 就直接改 physics。

---

# Prototype（可選）

最多做 1～2 個 isolated prototype。

條件：

- 不改 production runtime behavior
- 不改 Schema
- 不改 sample
- 不改 main physics

例如：

```text
NearestLeaderIndexBenchmark
FixedVsIndexedSafetyBenchmark
ResultPipelinePrototype
```

可使用代表性 full sample：

```text
ActualWorld AdvanceTo(8000 s)
```

比較：

```text
elapsed
ms/tick
allocation
memory
event result equality
```

---

# Git 流程

先：

```powershell
git fetch origin
git status
```

若 dirty：

- 停止並回報
- 不得 reset / clean / stash

若 clean：

```powershell
git switch -c codex/open-source-rail-sim-study origin/main
```

本輪以：

```text
研究
比較
架構建議
prototype benchmark（可選）
```

為主。

不要直接大改 production runtime。

---

# 建議 commits

```text
docs: compare open-source railway simulation architectures
docs: add railway simulation architecture recommendations
perf: add isolated railway architecture benchmark
```

若沒有 prototype，只做 docs commits。

---

# 驗證

若只改 docs：

```powershell
git diff --check
```

若有 prototype / test：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

---

# 完成後 push

```powershell
git push -u origin codex/open-source-rail-sim-study
```

不要自行 merge main。

---

# 完成後回報

請回報：

1. branch HEAD SHA
2. 修改檔案
3. commits
4. 實際調研 upstream repositories
5. upstream commit / branch / version
6. licenses
7. fixed-step / event-driven / hybrid 分類
8. moving-block 實作重點
9. interlocking/resource model
10. passing/overtaking model
11. turnback/reversal model
12. simulation/UI separation
13. parallelization strategy
14. headless support
15. large-network strategy
16. 本專案最值得借鑑的 5 項
17. 不建議採用的 5 項
18. nearest-leader / spatial-index 是否值得導入
19. action-step / control-step 是否值得 prototype
20. 是否做 prototype
21. prototype benchmark（若有）
22. 對目前 `SimulationWorld` 最優先的 3 項改善
23. Phase 1～4 roadmap
24. git diff --check
25. 下一步建議

---

# 最終判斷原則

不要只回答：

```text
OSRD 很完整
SUMO 很成熟
DesRail 很快
```

必須做到：

```text
它們哪一段 architecture/code 解決什麼問題
↓
對本專案哪個類別／模組有價值
↓
能不能借
↓
風險
↓
預估收益
↓
建議實作順序
```

最終目標：

> 保留本專案捷運專用的 topology、passing、turnback、moving-block 與 Schema 8 優勢，同時吸收成熟 OSS 在 simulation architecture、resource model、scalability 與 performance 上已驗證的做法。
