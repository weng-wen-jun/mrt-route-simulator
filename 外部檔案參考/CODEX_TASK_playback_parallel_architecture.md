# Codex 任務：V4 Playback / Simulation 平行化架構重整

## 0. 任務目的

Repository：`weng-wen-jun/mrt-route-simulator`  
基準分支：`main`

目標是在**不改變 0.1 秒 physics tick、不降低安全規則、不破壞 deterministic topology runtime** 的前提下，盡量利用多核心 CPU，將 Simulation、Planned Timeline、Result Analysis、WPF Rendering 拆開。

核心原則：

```text
Physics tick = 0.1 s
ActualWorld = single writer
WPF = immutable consumer
Statistics = incremental consumer
Planned timeline = independent worker
UI frame 可丟
Physics tick 不可丟
Commands 不可丟
No second Domain Model
No second runtime
No legacy Route authority
No validator weakening
```

---

## 1. Git 安全流程

先執行：

```powershell
git fetch origin
git status
```

若工作樹不是 clean：

- 停止並回報
- 不得 reset / clean / stash / force push

若 clean：

```powershell
git switch -c codex/playback-parallel-architecture origin/main
```

禁止：

- 直接修改 `main`
- 直接修改 `GPT-use`
- force push
- tag / release
- 自行 merge PR

---

## 2. 現況問題

目前 WPF 播放路徑把大量工作綁在 UI thread：

```text
UI update
→ SimulationSession.AdvanceTo(...)
→ ActualWorld.AdvanceTo(...)
→ PlannedWorld.AdvanceTo(...)
→ rebuild CurrentTrainRows
→ rebuild SafetyRows
→ rebuild EventRows
→ Draw route
→ Draw speed chart
→ Draw safety chart
→ Draw time-distance diagram
→ recompute timetable
→ recompute interval statistics
→ recompute segment details
→ recompute V1/V2 comparison
→ recompute resource occupancy
→ update summary
```

`MainWindow.V2.cs` 中 `UpdateV2PlaybackView()` 目前還直接讀：

```text
session.ActualWorld.Events
session.ActualWorld.SafetyHistory
session.ActualWorld.GetTrainCenterPosition(...)
```

這使 UI thread 與 mutable Engine state 強耦合。

大型 scenario 已觀察到：

- fixed tick = 0.1 s
- 單一 world 約 2.75 ms/tick 等級
- 60× 時 UI thread 明顯追不上
- WPF 重繪與 Collection rebuild 放大卡頓
- 長時間播放有 GC / memory / freeze 風險

---

# 3. 目標架構

```text
                         ┌────────────────────┐
                         │   WPF UI Thread    │
                         │ render / input only│
                         └─────────▲──────────┘
                                   │ immutable frame
                         ┌─────────┴──────────┐
                         │ PlaybackCoordinator│
                         │ latest-frame-wins  │
                         └─────────▲──────────┘
                                   │ bounded Channel
             ┌─────────────────────┴─────────────────────┐
             │                                           │
   ┌─────────┴──────────┐                     ┌──────────┴─────────┐
   │ Actual Simulation  │                     │ Result Accumulator │
   │ Worker             │──── delta/event ───▶│ Worker             │
   │ single writer      │                     │ statistics/results │
   └────────────────────┘                     └────────────────────┘

   ┌────────────────────┐     ┌────────────────────┐
   │ Planned Timeline   │     │ Export / Analysis  │
   │ Worker             │     │ Worker             │
   │ independent world  │     │ background         │
   └────────────────────┘     └────────────────────┘
```

不要手動綁 CPU core，交給 .NET / Windows scheduler。

---

# 4. `SimulationWorld` 必須維持 Single Writer

禁止：

```csharp
Parallel.ForEach(trains, train =>
{
    world.UpdateTrain(train);
});
```

禁止：

```csharp
Task.WhenAll(trains.Select(UpdateTrainAsync))
```

因為 world state 包含：

- trains
- topology occupancy
- route reservations
- platform allocation
- moving block
- passing
- rear-clear
- pocket / tail / turnback
- dispatch
- continuation
- event ordering

直接多執行緒修改會造成 nondeterministic race。

**ActualWorld 永遠只能由一個 Simulation Worker 修改。**

UI、Statistics、Charts、Export 不得直接讀一個正在被 worker 修改中的 `SimulationWorld`。

---

# 5. Phase B1：Simulation Worker

新增適當類別，例如：

```text
SimulationPlaybackWorker
PlaybackCoordinator
PlaybackCommand
PlaybackFrame
PlaybackPerformanceSnapshot
```

建議放 App / Application service 層。

不要把 WPF 相依性搬進 Engine。

UI 不再直接呼叫：

```text
ActualWorld.AdvanceTo(...)
```

---

# 6. PlaybackCommand

使用 command queue，例如：

```csharp
Channel<PlaybackCommand>
```

至少支援：

```text
Play
Pause
Reset
Stop
SetPlaybackRate
SetMovingBlockMode
SetBrakingMode
ScheduleObstacle（若既有功能需要）
```

Command channel：

- 不得 Drop
- 必須依序處理
- Pause / Reset / Stop 必須可靠

---

# 7. Immutable PlaybackFrame

UI 必須只吃 immutable data。

建議：

```csharp
public sealed record PlaybackFrame(
    double SimulationTimeSeconds,
    ImmutableArray<TrainPlaybackSnapshot> Trains,
    ImmutableArray<SafetyObservationSnapshot> CurrentSafety,
    ImmutableArray<SimulationEvent> NewEvents,
    ImmutableArray<ResourceStateSnapshot> Resources,
    PlaybackPerformanceSnapshot Performance);
```

可依現有 type 重用／補強，不要建立第二套同義資料源。

必要條件：

- frame 建立後不可變
- 不攜帶 mutable world reference
- UI 不需再直接讀 `ActualWorld`

---

# 8. Frame Channel：Latest Frame Wins

Frame delivery 使用 bounded channel：

```text
capacity = 1（最多 2）
FullMode = DropOldest
```

60× 時若 UI 還在畫舊 frame，而 Engine 已經往前，舊 frame 沒有價值。

可丟 UI frame，但：

```text
physics tick 不得丟
command 不得丟
```

---

# 9. 60× 定義

60×：

```text
1 real second = 60 simulation seconds
```

不代表每個 0.1 秒 tick 都要 render。

Engine 仍完整計算：

```text
100.0
100.1
...
105.9
106.0
```

UI 可只顯示：

```text
100
106
112
118
...
```

例如 60× 下 UI 10 FPS：

```text
每 100 ms wall time
≈ 前進 6 simulation seconds
```

---

# 10. Requested 與 Effective Rate

新增：

```text
RequestedPlaybackRate
EffectiveSimulationRate
```

如果 Engine 追不上：

```text
Requested = 60×
Effective = 37×
```

UI 顯示或 diagnostics 記錄真實值。

不得為追上 60×：

- 跳 tick
- variable timestep
- 將 0.1 秒改成 0.5 / 1 秒
- 省略 safety
- 關閉 collision / rear-clear

---

# 11. PlaybackCoordinator 計時

使用 `Stopwatch`。

概念：

```text
targetSimulationTime
=
playbackBaseSimulationTime
+
elapsedWallTime × requestedRate
```

Simulation worker 再用既有 fixed-step `AdvanceTo(target)` 或等價方式推進。

UI timer 不再負責追 physics。

---

# 12. Planned Timeline 分離

長期目標：

```text
SimulationSession
├─ ActualSimulationRunner
└─ PlannedTimelineArtifact
```

Planned world 只存在於 planned worker。

建立 artifact，例如：

```csharp
public sealed record PlannedTimelineArtifact(
    ImmutableArray<SimulationEvent> Events,
    ImmutableArray<TrajectorySample> Trajectory,
    double CompletedAtSeconds);
```

流程：

```text
plannedOptions.CreateWorld()
→ background worker
→ simulate until operational complete
→ freeze events / trajectory
→ PlannedTimelineArtifact
→ release PlannedWorld
```

正常 playback 不應再推進 PlannedWorld。

---

# 13. Planned Timeline 不阻塞 UI 載入

大型專案：

```text
Validate topology
→ Actual runtime ready
→ UI 可立即顯示
→ background 計算 planned timeline
```

UI 可顯示：

```text
計畫時間軸：計算中…
```

Planned artifact 完成後再啟用／更新：

- 計畫速度圖
- 計畫時刻表
- V1/V2 comparison
- 其他 planned-dependent view

使用者在 planned 尚未完成時仍可播放 ActualWorld。

---

# 14. Phase B2：Incremental Result Accumulator

新增／補強：

```text
SimulationResultAccumulator
```

不要每個 UI frame 重掃：

```text
Events[0..now]
Trajectory[0..now]
SafetyHistory[0..now]
Resource history[0..now]
```

Accumulator 只吃新增資料：

```text
new events
new trajectory samples
new safety status changes
resource deltas
```

維護：

```text
TimetableResult
SectionStatistics
JourneyStatistics
ResourceUtilization
IntervalStatistics
V1/V2 comparison source data
```

目標：

```text
O(new data)
```

而不是：

```text
O(all history)
```

---

# 15. Event / Delta Cursor

使用可靠 cursor：

```text
LastProcessedEventIndex
LastProcessedTrajectoryIndex
LastProcessedSafetyIndex
```

或重用既有 `_nextEventIndex` / snapshot acknowledge 機制。

不要每次 `world.Events.ToArray()` 後從頭比對。

---

# 16. Phase B3：UI 分級刷新

建議：

| 項目 | 刷新頻率 |
|---|---|
| Simulation clock | 10–30 FPS |
| Train markers | 10–30 FPS |
| Current safety | 約 10 FPS |
| Speed chart | 2–5 FPS |
| Time-distance | 2–5 FPS |
| Safety history chart | 2–5 FPS |
| Timetable | Arrival/Departure event-driven |
| Segment statistics | 約 1 s 或 event-driven |
| Resource occupancy | Reserve/Release event-driven |
| V1/V2 comparison | Pause / completion / selected tab |
| Hidden tab | 不刷新 |

未顯示的 Tab 不要持續重畫。

---

# 17. ObservableCollection 改差分更新

避免：

```csharp
Rows.Clear();
foreach (...) Rows.Add(...);
```

## Current trains

使用 stable row：

```text
VehicleId → Row VM
```

每 frame 只更新：

```text
Direction
Phase
Chainage
Speed
Location
NextStation
```

## Events

只 append 新 event。

超過 300：

```text
remove oldest
```

不要每 frame `TakeLast(300).Reverse()` 後全部重建。

## Safety

可用：

```text
FollowerVehicleId + LeaderVehicleId + TrackId
```

或適合 key 做 stable rows。

---

# 18. Snapshot 建立頻率

不要每個 0.1 秒 physics tick 都建立完整 UI frame。

例如 60×：

```text
Physics = 600 ticks / real sec
Frames = 10 / real sec
```

Frame DTO allocation 只做 10 次，而不是 600 次。

---

# 19. Trace / Safety Retention

若 Phase 1 已完成：

```text
Trajectory = Decimated
SafetyHistory = Decimated / bounded
```

保持。

Interactive playback 不應強迫 Full 0.1s history 永久保留。

若高解析輸出需要 Full：

```text
offline export run
```

不要提高 interactive world retention。

---

# 20. Export / Analysis 背景化

CSV / PNG / PDF / 大量分析不可阻塞 simulation worker 或 UI。

若 WPF Visual 必須在 Dispatcher / STA：

```text
background data preparation
→ Dispatcher final render
```

不要把 WPF Visual 直接放任意 ThreadPool thread。

---

# 21. Phase C：Engine Tick 內部平行化

只有 Phase B 完成後重新 profile，確認 Engine 本身仍是主瓶頸，才做。

每個 tick 改為：

```text
Tick N
│
├─ A. Capture read-only TickContext
│
├─ B. Parallel proposal calculation
│     Train A proposal
│     Train B proposal
│     Train C proposal
│
├─ C. Deterministic arbitration
│     route/resource/platform/passing
│
├─ D. Parallel pure motion calculation（若安全）
│
└─ E. Deterministic commit + event ordering
```

---

# 22. TrainTickProposal

可新增：

```csharp
TrainTickProposal[]
```

Proposal 階段不得：

- 修改 `_trains`
- 修改 occupancy
- reserve resource
- append final event
- 改 World mutable state

只算候選值，例如：

```text
vehicle performance
speed limit
station braking candidate
requested acceleration
moving-block constraint
leader gap
route request intent
proposed position
proposed speed
```

---

# 23. 可平行的候選計算

優先找 pure/read-only：

```text
vehicle performance lookup
speed limit lookup
station braking candidate
basic acceleration candidate
route-distance calculations
read-only leader constraint calculation
trajectory DTO candidate
部分 safety candidate evaluation
```

可用：

```csharp
Parallel.For(...)
```

但必須 benchmark。

不要每列車建立一個 Task。

可依 activeTrainCount 設平行門檻，門檻由 benchmark 決定。

---

# 24. 必須 deterministic 的部分

保留 sequential / deterministic arbitration：

```text
dispatch activation
route reservation
platform allocation
passing arbitration
turnback reservation
pocket / tail resource
directed connection ownership
collision final decision
event ordering
world commit
```

若多列車競爭同一資源，使用固定排序，例如：

```text
planned departure time
→ service priority
→ serviceRunId
→ vehicleId
```

同一 input 多次執行必須得到一致結果。

---

# 25. Safety / Moving Block 資料結構優化

若 profiler 發現 safety hot path，先檢查是否近似：

```text
for each train
    check every other train
```

若接近 O(N²)，建立：

```text
track
direction
physical order
```

索引。

follower 通常只需找最近有效 leader。

目標：

```text
O(N²)
→ O(N log N) 或更好
```

若現有 `TopologyMovementOccupancyIndex` 可重用，優先重用，不建立第二套 physical truth。

---

# 26. 物理位置權威不得改

仍以：

```text
TrackEdgeId
OffsetMeters
ServiceRouteTraversalIndex
```

作 physics authority。

不得把：

```text
PositionMeters
ProjectedChainageMeters
RouteProjection
UI X coordinate
```

變回物理真相。

---

# 27. Performance Instrumentation

至少記錄：

```text
RequestedPlaybackRate
EffectiveSimulationRate
SimulationAdvanceMs
FrameBuildMs
FramePublishMs
UIRenderMs
ResultAccumulatorMs
ActiveTrainCount
TrajectoryCount
SafetyHistoryCount
FrameDropCount
```

Release 不要大量寫 log。

可用：

- debug diagnostics
- performance runner
- optional diagnostics panel

---

# 28. Benchmark

## A. Engine baseline

代表性 full sample：

```text
ActualWorld.AdvanceTo(8000 s)
```

記錄：

```text
elapsed
ms/tick
effective simulation x
managed memory / working set
trajectory count
safety count
event count
```

## B. Playback

測：

```text
1×
10×
30×
60×
```

記錄：

```text
requested rate
effective rate
UI average FPS
frame drop count
max UI stall
memory
```

## C. Determinism

同一 scenario 至少重跑 3 次，比較：

```text
event sequence
arrival/departure times
overtaking order
turnback
resource reservations
collision
station stop violations
```

必須一致。

---

# 29. 60× 目標

0.1 秒 tick 下，60× 需要：

```text
600 physics ticks / real second
```

理論：

```text
< 1.667 ms/tick
```

建議 Engine target：

```text
<= 1.5 ms/tick
```

留 scheduler / frame / GC 餘裕。

如果尚未達標，顯示實際 Effective Rate，不可犧牲物理正確性。

---

# 30. UI Responsiveness 驗收

高倍率播放必須仍可：

- Pause
- Stop
- 切換 tab
- 拖動視窗
- Reset
- Reload project

即使：

```text
Requested = 60×
Effective = 40×
```

UI 仍不可 freeze。

---

# 31. Lifecycle / Cancellation

必須正確處理：

```text
Load project
→ stop old worker
→ cancel / join
→ release old world
→ create new worker

Reset
→ worker-owned reset

Window close
→ cancel worker
→ clean shutdown
```

使用：

```csharp
CancellationToken
```

建議每個 session 使用：

```text
SessionId / GenerationId
```

UI 只接受目前 generation 的 frame。

---

# 32. Thread Safety

完成後 UI thread 禁止直接讀：

```text
session.ActualWorld.Events
session.ActualWorld.SafetyHistory
session.ActualWorld.GetTrainCenterPosition(...)
```

UI 所需資料只能來自：

```text
PlaybackFrame
ResultSnapshot
PlannedTimelineArtifact
```

或等價 immutable data。

---

# 33. MainWindow.V2.cs 瘦身目標

最後應接近：

```text
Play
→ coordinator.Play()

Pause
→ coordinator.Pause()

Speed changed
→ coordinator.SetPlaybackRate(...)

Frame arrived
→ ApplyPlaybackFrame(frame)

Result changed
→ ApplyResultSnapshot(result)
```

MainWindow 不再：

```text
AdvanceTo
重掃完整 world
重建全部統計
```

---

# 34. 不得建立第二套結果真相

UI 不得自行重新推導：

- arrival / departure
- section time
- resource usage
- overtaking
- safety

應由：

```text
Engine events
ResultAccumulator
```

提供。

---

# 35. Regression Tests

至少涵蓋：

## Worker lifecycle

```text
Play
Pause
Reset
Stop
Reload
Cancellation
```

## Frame

- bounded queue
- drop old frame 不影響 physics
- simulation time monotonic
- UI 不讀 mutable world

## Determinism

相同 scenario 多次：

- event sequence 一致
- arrival/departure 一致
- overtaking 一致
- turnback 一致
- collision 一致
- StationStopViolation 一致

## Performance contract

不可用過度依賴硬體的 timing unit test。

Timing 放 performance runner。

---

# 36. WPF Runner

確認：

- 全部 samples 可載入
- Play / Pause / Reset
- 切 tab
- speed selector
- charts
- timetable
- resource occupancy
- CSV / PNG / PDF
- 無 cross-thread WPF exception

不得出現：

```text
The calling thread cannot access this object because a different thread owns it
```

---

# 37. 禁止做法

禁止：

```text
SimulationWorld 加大 lock，UI 繼續直接讀
```

禁止：

```text
Task.Run(ActualWorld.AdvanceTo)
```

同時讓 UI 直接讀 world。

禁止：

```text
Parallel.ForEach(_trains)
```

直接修改 train/world state。

禁止：

```text
每 train 一個 Task
```

禁止：

```text
60× 跳 tick
```

禁止：

```text
FixedTimeStepSeconds 改成 0.5 / 1.0
```

禁止：

```text
GC.Collect()
```

當效能解法。

禁止降低：

- InfrastructureValidator
- connection validation
- moving-block safety
- rear-clear
- collision detection
- station-stop validation

---

# 38. 建議實作階段

## B1
- SimulationPlaybackWorker
- PlaybackCoordinator
- PlaybackCommand
- PlaybackFrame
- bounded latest-frame channel

## B2
- ResultAccumulator
- incremental timetable/statistics/resource results

## B3
- differential ObservableCollection update
- hidden-tab lazy refresh
- adaptive UI refresh

## C1
- profiler

## C2
- parallel read-only proposal calculation

## C3
- moving-block / leader lookup optimization

每階段完成後先 regression 再進下一階段。

---

# 39. 建議 commits

例如：

```text
feat: add single-writer playback simulation worker
feat: publish immutable playback frames
perf: decouple WPF rendering cadence from simulation ticks
perf: add incremental simulation result accumulator
perf: update WPF collections incrementally
perf: add lazy result-tab rendering
perf: add playback performance diagnostics
perf: parallelize read-only train tick proposals
perf: optimize moving-block leader lookup
test: add deterministic parallel playback regressions
docs: record parallel playback architecture and benchmarks
```

依實際變更拆，不要整包一個 commit。

---

# 40. QA_REPORT.md

新增：

```text
V4 Playback Parallel Architecture
```

記錄 Before / After：

```text
ActualWorld 8000 s elapsed
ms/tick
requested/effective rate
UI FPS
frame drop
max UI stall
trajectory count
safety count
memory
```

並清楚標示尚未完成人工驗收的：

- 不同 DPI
- 長時間 60×
- O04/O13 越行畫面
- O20 折返畫面

---

# 41. TODO.md

加入／更新：

```text
[V4-PLAYBACK-PERF-PHASE2]
single-writer simulation worker
immutable playback frame
adaptive UI refresh
incremental result accumulation
lazy hidden tabs

[V4-PLAYBACK-PERF-PHASE3]
profile and optimize SimulationWorld hot path
deterministic parallel proposal calculation
moving-block leader indexing
target <= 1.5 ms/tick for representative large scenario
```

只有實際完成並驗證才 `[x]`。

---

# 42. 完整驗證

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

---

# 43. Desktop Smoke Test

若可做原生桌面，至少測：

```text
1×
10×
30×
60×
```

大型 sample 上操作：

- Play
- Pause
- Resume
- tab switch
- resize/move window
- Reset
- Stop
- Reload

記錄：

```text
requested rate
effective rate
UI responsiveness
freeze/crash
memory trend
```

---

# 44. 完成後 push

```powershell
git push -u origin codex/playback-parallel-architecture
```

不要自行 merge main PR。

---

# 45. 完成後回報

請回報：

1. branch HEAD SHA
2. 修改檔案
3. logical commit SHAs
4. 新增架構類別
5. ActualWorld 是否 single-writer
6. WPF 是否已不直接呼叫 `ActualWorld.AdvanceTo`
7. WPF 是否仍直接讀 mutable world
8. Frame channel capacity / drop strategy
9. Requested / Effective rate 實作
10. Planned timeline 是否獨立 worker/artifact
11. ResultAccumulator 是否 incremental
12. ObservableCollection 是否差分更新
13. Hidden tabs 是否 lazy
14. 1×/10×/30×/60× benchmark
15. 8000 秒 Engine elapsed
16. ms/tick
17. trajectory count
18. safety count
19. memory
20. UI FPS / frame drop
21. determinism tests
22. Release build
23. Engine runner
24. WPF runner
25. git diff --check
26. 尚未完成人工驗收
27. 是否進行 Phase C
28. 若尚未達 60×，目前 EffectiveSimulationRate
29. profiler top hot paths
30. 下一階段建議

---

# 46. 最終優先順序

```text
1. Actual-only simulation
2. Simulation off UI thread
3. Immutable frame
4. UI render throttling
5. Incremental results
6. Lazy hidden tabs
7. Differential collection updates
8. Safety / moving-block indexing
9. Pure tick proposal parallelization
```

不要一開始就對整個 `SimulationWorld` 粗暴多執行緒。
