# Codex 任務：Playback Coordinator Pacing + Nested-Tab Lazy Refresh + Incremental Time-Distance Rendering

## 0. 任務目的

請延續目前 `mrt-route-simulator` 的 playback performance 工作，針對最新 end-to-end profile 已確認的瓶頸做**低風險、可量測、不得改變物理結果**的優化。

本輪只處理三件事：

```text
1. Playback coordinator pacing / no-progress busy polling
2. Route / Speed nested-tab 真正 lazy refresh
3. Time-Distance 圖 incremental / bounded rendering
```

不要再擴大 nearest-leader index，也不要在本輪 productionize indexed lookup。

最新量測已確認：

```text
60× offscreen playback：
Effective ≈ 59.57–59.69×

Engine productive advance：
約佔 9.8–12.0% wall time

Coordinator no-progress AdvanceTo：
約 47–61 million calls / 10 s
measured inner-call time 約 1.15–1.27 s

TimeDistanceRender：
p50 ≈ 50.765 ms
p95 ≈ 58.923 ms
max ≈ 79.999 ms

TimeDistance tab：
unique UI FPS ≈ 17.79
skipped frames ≈ 126
max input gap ≈ 108.44 ms

SpeedChartRender：
p95 ≈ 10.8 ms

Nested tab audit：
Route visible 時仍刷新 Speed chart
Speed visible 時仍刷新 Route marker
```

本輪最重要的問題：

> 在完全保留 0.1 秒 physics tick、所有 Engine safety/resource/deterministic 行為的前提下，能否大幅降低 coordinator 空轉、避免隱藏 nested tab 的無效刷新，並讓 Time-Distance 圖從每次完整重建改成增量／有界顯示？

---

# 1. 先閱讀現有文件

請先讀：

```text
docs/PLAYBACK_END_TO_END_PROFILE.md
docs/NEAREST_LEADER_INDEX_PROTOTYPE.md
QA_REPORT.md
```

以 repository 實際內容為準，不要只依本任務摘要。

尤其確認：

```text
SimulationPlaybackWorker
PlaybackCoordinator
PlaybackPerformanceDiagnostics
MainWindow.PlaybackRefresh
SimulationResultAccumulator
DrawV2Route
DrawV2SpeedProfile
DrawTimeDistanceDiagram
PlaybackTimer_TickCore
```

---

# 2. Git / Worktree 安全

目前已知上一輪工作可能仍在：

```text
branch:
codex/nearest-leader-index-prototype

HEAD/base:
558e06b534f97c997f450379f747e7709528ac17
```

且 prototype edits 可能仍未 commit。

先執行：

```powershell
git fetch origin
git status
git branch --show-current
git rev-parse HEAD
```

如果目前正是既有 prototype worktree，且 dirty changes 與下列文件記載的 prototype/profiler edits 相符：

```text
docs/NEAREST_LEADER_INDEX_PROTOTYPE.md
docs/PLAYBACK_END_TO_END_PROFILE.md
QA_REPORT.md
```

本任務**允許在該既有 worktree 上繼續實作**，但：

- 不得 reset
- 不得 clean
- 不得 stash
- 不得丟失既有 prototype edits
- 不得假裝 dirty worktree 是 clean
- 不得自行 commit / push / tag / merge，除非另有明確指示

若 dirty changes 含有與現有 prototype 無關的未知修改：

```text
STOP
```

先回報，不要碰。

---

# 3. 本輪禁止事項

本輪禁止：

```text
Production default 改 indexed-only
移除 full-scan oracle
改 0.1 s fixed tick
跳 physics tick
moving-block 降頻
rear-clear 降頻
collision check 降頻
resource arbitration 改序
passing / turnback 語意變更
直接 Parallel.ForEach(_trains) 修改 world
改 Schema
改 sample operational truth
大改 SimulationWorld
```

也不要因為 UI 卡頓：

```text
降低 Engine 正確性
```

---

# 4. Phase A：Playback Coordinator Pacing

## 4.1 現況問題

最新 profiler 顯示約 10 秒內：

```text
47–61 million no-progress AdvanceTo calls
```

這些呼叫沒有完成新的 0.1 秒 simulation tick。

目前需要先精確定位：

```text
worker loop
target simulation time calculation
AdvanceActualTo / AdvanceTo
Stopwatch polling
frame cadence
command polling
```

畫出實際 pacing loop。

---

# 5. Pacing 設計目標

目標不是降低 physics tick。

目標是：

> 當下一個完整 0.1 秒 simulation tick 尚未到 wall-clock deadline 時，不要用數百萬次 tight loop 重複呼叫 AdvanceTo。

60× 下：

```text
0.1 simulation second
≈ 1.6667 ms wall time
```

因此 coordinator 應能計算：

```text
nextPhysicsTickDueWallTime
```

或等價 deadline。

---

# 6. 建議的 pacing 方向

可以研究：

```text
Stopwatch deadline
+
bounded wait / yield strategy
+
CancellationToken / command wakeup
```

例如：

```text
距離下一 tick 尚久
→ await delay / timer / async wait

接近 deadline
→ short yield / spin only if measured necessary

tick 到期
→ Advance fixed-step physics
```

但不要先入為主硬選 `Task.Delay(1)`。

Windows timer precision、scheduler、command latency必須實測。

可選：

```text
PeriodicTimer
Task.Delay
SemaphoreSlim / Channel wakeup
WaitHandle
hybrid delay + short spin/yield
```

依現有 worker architecture 選最少侵入的方案。

---

# 7. Pacing 必須保留的語意

必須保持：

```text
RequestedPlaybackRate
EffectiveSimulationRate
Play
Pause
Reset
Stop
SetPlaybackRate
Obstacle command
MovingBlock command
Braking command
project/session cancellation
generation isolation
```

Command channel：

```text
不可 drop
```

Frame channel：

```text
可 latest-frame-wins
```

Physics：

```text
不可 drop tick
```

---

# 8. Pacing Responsiveness Gate

新增測試：

```text
Play → Pause latency
Pause → Resume
SetPlaybackRate while running
Reset while running
Stop while running
session cancellation
project reload cleanup
```

不要因為 worker 在 wait 而導致 command 卡死。

若採 wait：

```text
command arrival
```

必須能喚醒 worker，而不是只能等 timeout。

---

# 9. No-Progress Call Acceptance

優化後：

```text
no-progress AdvanceTo
```

不得再是數百萬次／秒等級。

請量：

```text
count / wall second
measured no-progress time
CPU duty
```

目標：

```text
至少下降 99%+
```

若能接近：

```text
<= 1~2 次 no-progress check / physics tick
```

更佳。

不要為達數字犧牲播放倍率或 command latency。

---

# 10. Phase B：Nested Route / Speed Tab Lazy Refresh

最新 audit：

```text
PlaybackTimer_TickCore
→ UpdateV2PlaybackView
→ selected workspace tab == Simulation
→ DrawV2Route
→ DrawV2SpeedProfile
```

目前只判斷外層 workspace tab，沒有判斷：

```text
SimulationViewTabControl.SelectedItem
```

因此：

```text
Route visible
→ Speed chart still renders

Speed visible
→ Route markers still render
```

---

# 11. Nested Tab 修正

讓刷新依真正 visible nested tab 執行。

概念：

```text
Simulation workspace selected
    ├─ Route sub-tab selected
    │   → route marker refresh
    │   → no speed chart refresh
    │
    └─ Speed sub-tab selected
        → speed chart refresh
        → no route marker refresh
```

切換 nested tab 時：

```text
立即 refresh 新 selected tab
```

避免等很久才看到最新狀態。

---

# 12. Nested Tab Test

至少驗證：

```text
Route visible:
RouteMarkerRender count increases
SpeedChartRender count does not increase

Speed visible:
SpeedChartRender count increases
RouteMarkerRender count does not increase

Switch Route → Speed:
Speed immediately refreshes

Switch Speed → Route:
Route immediately refreshes
```

不要把：

```text
hidden-tab work = 0
```

只靠 timing 推測。

使用 counters / explicit invocation observation。

---

# 13. Train Row Refresh

Profiler 顯示：

```text
TrainRowUpdate
```

在 workspace 其他 tab 也可能執行。

目前成本低。

本輪：

```text
先量
```

如果非常小：

```text
不要為了潔癖重構
```

若可非常安全地 gated，才做。

優先級低於：

```text
nested Route/Speed
Time-Distance
pacing
```

---

# 14. Phase C：Time-Distance Rendering

目前：

```text
TimeDistanceRender
p50 ≈ 50.8 ms
p95 ≈ 58.9 ms
max ≈ 80.0 ms
```

這是目前最明顯的 UI hot path。

先閱讀 `DrawTimeDistanceDiagram()`。

請拆解：

```text
data retrieval
filter
group
sort
decimation
axis calculation
Polyline/Line/Text creation
Canvas.Children clear/add
event annotation
station grid
layout
```

量出哪一步最重。

不要直接猜是 WPF GPU。

---

# 15. Time-Distance 正確性邊界

Time-Distance 是：

```text
presentation
```

不是 physical truth。

可以：

```text
decimate
cache
incrementally append
reuse static visuals
```

但不能：

```text
改 Engine trajectory
改 retained Engine events
改 timetable truth
改 export truth
```

若 CSV/PDF/PNG export 使用不同完整資料路徑：

```text
保持不變
```

若 export 共用同一 render function：

```text
先確認需求
```

不要因 interactive 優化降低正式 export 資料精度。

---

# 16. 建議拆 Static / Dynamic Layers

優先研究把圖拆成：

```text
Static layer
- axes
- station grid
- station labels
- invariant legend

Dynamic layer
- vehicle trajectory polylines
- current cursor / selected marker
- event annotations that actually changed
```

當：

```text
canvas size
station projection
selected filters
project
```

沒變時，不要重建 static layer。

---

# 17. Incremental Trajectory Cache

建立 presentation-only cache，例如：

```text
VehicleId
→ lastProcessedTrajectoryIndex
→ cached display points
→ current decimated geometry
```

每次只處理：

```text
new trajectory samples
```

而不是重掃全部 history。

如果 `SimulationResultAccumulator` 已有 trajectory cursor：

```text
優先重用其 immutable/incremental data
```

不要再建立另一套 Engine truth。

---

# 18. Bounded Display Geometry

長時間播放不能讓 UI point count 無限制成長。

採：

```text
bounded display point budget
```

但：

- 不得改 Engine retention
- 不得改 CSV/export source
- 必須保留重要折點

應優先保留：

```text
station arrival/departure
direction change
turnback
passing-related phase changes
speed/trajectory slope change
first/last point
```

可重用現有：

```text
TrajectoryAnalysis.DecimatePreservingCriticalPoints
```

若適合。

---

# 19. Incremental + Re-Decimation 策略

建議：

```text
平常：
append new points incrementally

當 point budget 超限：
在低頻率時機重新 decimate

例如：
budget exceeded
selected vehicle/filter changed
resize
project changed
pause/completion
```

不要：

```text
每 250 ms full-history decimate
```

---

# 20. Render Cadence

Time-Distance 不需要 30 FPS。

請依目前 refresh architecture 判斷。

可維持：

```text
2–5 FPS
```

或 event/data-driven。

若沒有新增 trajectory：

```text
不要 redraw
```

若 hidden：

```text
不要 redraw
```

---

# 21. UI Object Allocation

特別量：

```text
new Polyline
new Line
new TextBlock
Canvas.Children.Clear()
Canvas.Children.Add()
PointCollection allocation
```

若每次刷新大量重建 WPF objects：

優先：

```text
reuse existing visual objects
update Points / position
```

不要先用 `GC.Collect()`。

---

# 22. Time-Distance Acceptance

優化後至少要求：

```text
visual semantics preserved
same train trajectories
same station alignment
same direction/turnback interpretation
same event markers where applicable
```

Performance 目標：

```text
p95 < 16 ms
```

若合理可達。

至少要求：

```text
p95 比現況下降 >= 60%
```

並且：

```text
TimeDistance unique UI FPS >= 25
```

或若 refresh cadence 刻意低於 25 FPS：

必須證明：

```text
input responsiveness 明顯改善
max input gap < 50 ms
```

不可為了 FPS 數字每 frame 畫沒有變化的圖。

---

# 23. End-to-End Reprofile

完成三項優化後，使用同一大型 28 站 sample 重跑：

```text
60×
```

相同 profiler methodology。

至少重測：

```text
Route
Speed
Safety
TimeDistance
Timetable
Segment
Resource
Comparison
```

---

# 24. Before / After 核心表

輸出：

| Metric | Before | After | Delta |
|---|---:|---:|---:|
| Effective simulation × | | | |
| no-progress calls / 10s | | | |
| no-progress measured ms | | | |
| Worker productive advance ms | | | |
| Route unique UI FPS | | | |
| Speed unique UI FPS | | | |
| TimeDistance unique UI FPS | | | |
| TimeDistance render p50 | | | |
| TimeDistance render p95 | | | |
| TimeDistance render max | | | |
| max input gap | | | |
| skipped frames | | | |
| allocation MiB/s | | | |
| Gen0/1/2 | | | |
| working set | | | |

---

# 25. Hidden Nested Tab Before / After

另列：

```text
Route visible:
Route render count
Speed render count

Speed visible:
Route render count
Speed render count
```

After 必須證明：

```text
hidden nested tab expensive render = 0
```

除非：

```text
explicit resize/project-switch/forced refresh
```

這些例外要記錄。

---

# 26. Frame-Drop Physics Parity

保留上一輪 slow-UI parity。

再次確認：

```text
normal consumption
vs
slow UI / skipped frames
```

在同 simulation target 比較：

```text
events
trajectory
safety
final trains
resource occupancy analysis
```

必須一致。

---

# 27. Engine Regression

本輪不應改 Engine physics。

所以：

```text
Engine event / trajectory / safety hashes
```

不得因 pacing/UI 優化改變。

若有改變：

```text
STOP
```

先找原因。

---

# 28. Coordinator Pacing Parity

特別驗證不同 playback rate：

```text
1×
10×
30×
60×
```

不是為 performance benchmark 全跑 8 tabs，而是確認 pacing correctness。

至少檢查：

```text
no skipped physics ticks
monotonic simulation time
pause/resume correct
effective rate reasonable
command responsiveness
```

---

# 29. CPU / Scheduler 觀察

若方便，可補：

```text
process CPU %
thread CPU
context switches
```

但不是必須。

不要沒有 ETW/profiler 證據就宣稱：

```text
GC
GPU
scheduler
```

是主要瓶頸。

---

# 30. Native Desktop Gate

Automated offscreen profile 完成後：

如果環境允許，做原生 Windows desktop smoke：

```text
60×
Route
Speed
TimeDistance
tab switch
window resize
drag window
different DPI if possible
```

觀察：

```text
input responsiveness
visual tearing
lag
stutter
chart update
```

若不能：

```text
明確標記 NOT COMPLETED
```

不要拿 offscreen 代替。

---

# 31. 文件更新

更新：

```text
docs/PLAYBACK_END_TO_END_PROFILE.md
QA_REPORT.md
```

新增或更新一節：

```text
Coordinator pacing + nested-tab lazy + time-distance incremental rendering
```

至少記錄：

```text
root cause
implementation
before/after
correctness gates
remaining bottleneck
native acceptance status
```

---

# 32. 不要順便 Productionize Leader Index

即使本輪 UI 變快：

```text
nearest-leader index
```

仍維持：

```text
NEEDS MORE DATA
production default = oracle
```

除非使用者另外明確交辦。

---

# 33. Regression / Build

執行：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

若 profiler 有專用 CLI：

```powershell
dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release -- <actual profiler args>
```

依實際 CLI 為準。

---

# 34. 建議 Tests

至少加入／更新：

```text
CoordinatorDoesNotBusyPollBeforeNextFixedTick
CoordinatorWakesImmediatelyForCommand
PlaybackRateChangesPreserveFixedTickSequence
HiddenSpeedTabDoesNotRenderWhileRouteVisible
HiddenRouteTabDoesNotRenderWhileSpeedVisible
SwitchingNestedTabRefreshesSelectedView
TimeDistanceIncrementalMatchesFullRender
TimeDistanceCacheInvalidatesOnResize
TimeDistanceCacheInvalidatesOnProjectChange
TimeDistanceCacheInvalidatesOnFilterChange
SlowUiFrameDropPreservesEngineOutput
```

命名依現有 style 調整。

---

# 35. Commit / Push

本任務本身**不授權**：

```text
commit
push
tag
merge
release
```

完成修改與驗證後：

```text
停在工作樹
回報結果
等待下一步指示
```

不要自行發布。

---

# 36. 完成後回報

請回報：

1. current branch
2. base SHA
3. modified files
4. dirty status
5. coordinator pacing before call count
6. coordinator pacing after call count
7. no-progress reduction %
8. no-progress measured CPU/time before/after
9. command wakeup strategy
10. pause latency
11. stop/reset latency
12. 1× pacing result
13. 10× pacing result
14. 30× pacing result
15. 60× pacing result
16. Route visible / Speed render count
17. Speed visible / Route render count
18. nested-tab forced-refresh exceptions
19. TimeDistance old render architecture
20. TimeDistance new cache architecture
21. static/dynamic split
22. display point budget
23. decimation strategy
24. TimeDistance p50 before/after
25. TimeDistance p95 before/after
26. TimeDistance max before/after
27. TimeDistance UI FPS before/after
28. max input gap before/after
29. skipped frames before/after
30. allocation MiB/s before/after
31. Gen0/1/2 before/after
32. effective 60× before/after
33. slowest tab after optimization
34. slowest component after optimization
35. hidden-tab audit
36. slow-UI frame-drop parity
37. Engine parity
38. Release build
39. Engine runner
40. WPF runner
41. git diff --check
42. native desktop acceptance
43. remaining primary bottleneck
44. next recommended optimization

---

# 37. 成功條件

本輪成功不是看單一 FPS。

至少需同時滿足：

```text
Physics tick remains exactly 0.1 s
No Engine behavior regression
No safety/resource/order regression
No-progress busy polling drastically reduced
Hidden Route/Speed nested rendering eliminated
Time-Distance render cost materially reduced
60× remains sustainable
Input responsiveness improves
Automated regressions pass
```

---

# 38. 最終技術原則

```text
Do not optimize by weakening physics.
Do not optimize hidden UI work that should not run.
Do not redraw static geometry if nothing changed.
Do not rescan full history if only new trajectory data arrived.
Do not busy-poll before the next fixed tick is due.
Do not confuse offscreen WPF with native desktop acceptance.
```

本輪最重要的成果是：

> 把 60× 播放剩餘的 CPU/UI 浪費從「每秒數百萬次空轉 + 隱藏分頁重畫 + 50~80 ms Time-Distance 全重畫」收斂成有 deadline 的 pacing、真正 lazy 的分頁刷新，以及 bounded incremental visualization，同時維持所有 0.1 秒物理與營運結果不變。
