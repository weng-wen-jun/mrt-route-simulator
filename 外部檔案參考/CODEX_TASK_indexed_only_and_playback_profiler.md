# Codex 任務：Nearest-Leader Indexed-Only Benchmark + End-to-End 60× Playback Profiler

## 0. 任務目的

請延續既有 `nearest-leader / occupancy index shadow prototype`，完成下一輪「**收益證明與 productionization 判斷**」。

本輪不要再擴大 index 功能，也不要直接切換正式 production runtime。

目前已知 prototype 結果：

```text
大型 28 站 sample
8,000 s
80,000 ticks

baseline median:
8.58 s
0.10725 ms/tick

shadow median:
21.56 s
0.26950 ms/tick

oracle lookup median:
1,667.529 ms

indexed query median:
873.334 ms

index rebuild median:
16.261 ms

candidate average:
2.340 → 1.008

candidate reduction:
約 56.9%

fallback:
73 / 602,454
≈ 0.0121%

mismatch:
0

projection:
8.58 - 1.667529 + 0.873334 + 0.016261
≈ 7.802 s

projected overall gain:
約 9.1%
```

但目前仍缺：

1. **真正 indexed-only world 的獨立實測**
2. **cold / warm index 成本**
3. **indexed-only graph-call / allocation / memory**
4. **完整 end-to-end 60× WPF 各階段 profile**
5. **正式 GO / NO-GO productionization 決策**

本輪的核心問題不是「index 正不正確」，而是：

> index 在正式單一路徑執行時，實際能不能帶來足夠的 overall gain，且不增加不可接受的 allocation / complexity？

---

# 1. 既有文件

先閱讀：

```text
docs/NEAREST_LEADER_INDEX_PROTOTYPE.md
QA_REPORT.md
docs/OPEN_SOURCE_RAIL_SIM_COMPARISON.md
docs/OPEN_SOURCE_ARCHITECTURE_RECOMMENDATIONS.md
```

以實際 repository 最新內容為準。

不要假設之前聊天中的 line number / SHA 一定仍有效。

---

# 2. Git 安全流程

先執行：

```powershell
git fetch origin
git status
```

若工作樹不是 clean：

- 停止
- 回報
- 不得 reset
- 不得 clean
- 不得 stash
- 不得 force push

若 clean：

```powershell
git switch -c codex/nearest-leader-index-benchmark origin/main
```

若 prototype branch 尚未整合到 main，請先判斷：

```text
A. 應基於 prototype branch 繼續
或
B. 先建立乾淨整合 branch
```

不要偷偷 merge / cherry-pick。

先回報 branch/base 狀態，再依 repository 實際情況執行。

---

# 3. 本輪只做三件事

```text
A. indexed-only isolated benchmark
B. cold/warm + allocation / graph-call measurement
C. end-to-end 60× WPF profiler
```

不要：

- 擴大 index 功能
- 改 topology schema
- 改 0.1 s fixed tick
- 改 moving-block semantics
- 改 rear-clear
- 改 resource arbitration
- 改 passing / turnback behavior
- 直接將 index 切成 production default

---

# 4. Indexed-Only Benchmark Mode

新增 benchmark-only / test-only execution mode。

至少支援三種：

```text
oracle-only
indexed-only
shadow
```

語意：

## oracle-only

沿用現有 production full-scan leader lookup。

## indexed-only

在 benchmark / isolated test world 中：

```text
candidate index
→ narrow-phase TryGetTopologySafetyMetrics
→ TopologyGraphDistance
→ existing tie-break
```

直接回傳 indexed 結果。

但：

- 不改正式 app 預設
- 不改 production config
- 不移除 oracle code
- 不降低 fallback

若 index 無法保證完整：

```text
fallback full scan
```

並記錄原因。

## shadow

保留目前：

```text
oracle + indexed compare
production result = oracle
```

---

# 5. Indexed-Only 必須保留的真值

以下不能改：

```text
RuntimeTopologyCursor
TopologyMovementNavigator
TopologyMovementFootprint
TopologyGraphDistance
VehicleId tie-break
```

index 仍只是 broad phase。

禁止：

```text
ProjectedChainageMeters 作 leader order
UI X 作 physical order
schematic left/right 作 direction
```

---

# 6. 正確性 Gate

indexed-only 必須與 oracle-only 完全比對：

```text
event sequence
event timestamps
trajectory
safety observations
final train state
arrival/departure
overtake order
turnback order
route reserve/release
collision count
StationStopViolation count
```

至少涵蓋：

```text
same edge
cross-edge tail
branch
merge
passing
opposite shared track
repeated traversal
tail track
O04 passing
O13 passing
O20 pocket turnback
28-station large sample
dense mixed-length fixture
```

如果 dense fixture 本身 oracle 就不完成：

```text
只要求 oracle/index parity
不要錯誤宣稱 completion
```

---

# 7. Indexed-Only Performance Counters

至少記錄：

```text
tickCount
activeTrainCount
leaderLookupCount
indexedCandidateCount
fallbackCount
fallbackReasons
graphDistanceCallCount
topologySafetyMetricCallCount
indexRebuildCount
indexBuildTime
indexQueryTime
leaderLookupTime
TickCoreTime
```

另外記錄：

```text
allocated bytes
managed memory
working set
Gen0 GC
Gen1 GC
Gen2 GC
```

若 .NET runner 已有部分數據，優先重用。

---

# 8. Cold / Warm Benchmark

必須區分：

## Cold

首次建立：

```text
reachability closure
index structures
graph cache
```

量：

```text
cold init time
cold allocation
first lookup latency
```

## Warm

index / graph cache 已初始化後的 steady-state。

量：

```text
steady-state query time
steady-state allocation
```

不要把：

```text
shadow 模式跑在 oracle 之後的 warm cache
```

誤當真正 indexed-only cold cost。

---

# 9. 五次 Benchmark 規則

大型 28 站 sample：

```text
duration = 8,000 s
ticks = 80,000
Release
same retention
same machine
same sample
```

每個模式：

```text
1 warm-up discarded
5 measured runs
```

模式：

```text
oracle-only
indexed-only
shadow
```

每次最好使用 fresh process。

輸出：

```text
median
min
max
range / stdev if available
```

---

# 10. 主要 Performance 表

請產出：

| Metric | Oracle-only | Indexed-only | Shadow |
|---|---:|---:|---:|
| elapsed median | | | |
| ms/tick | | | |
| effective sim × | | | |
| leader lookup ms | | | |
| index query ms | | | |
| index rebuild ms | | | |
| graph-distance calls | | | |
| safety metric calls | | | |
| candidates avg | | | |
| fallback ratio | — | | |
| allocated bytes | | | |
| managed MiB | | | |
| working MiB | | | |
| Gen0/1/2 | | | |
| event hash | | | |
| mismatch | — | | |

---

# 11. 需要回答的核心 Performance 問題

請明確回答：

```text
indexed-only 實際 overall improvement %
```

不要只報 lookup improvement。

同時計算：

```text
lookup-only improvement
whole-engine improvement
allocation change
memory change
```

如果：

```text
lookup 快很多
但 overall 只快 1~2%
```

要如實寫。

---

# 12. Productionization 建議門檻

本輪只做決策，不直接切 production。

建議判斷：

## GO

至少滿足：

```text
mismatch = 0
fallback conservative
all parity gates pass
overall Engine median improvement >= 5%
allocation / memory 無明顯惡化
complexity 可維護
```

若實際提升：

```text
>= 8%
```

且 allocation 不惡化，可標：

```text
STRONG GO
```

## NO-GO

例如：

```text
overall < 3%
allocation 明顯上升
cold cost 過高
fallback 太多
維護成本不成比例
```

## NEEDS MORE DATA

介於兩者或 scenario gate 尚未完成。

---

# 13. End-to-End 60× WPF Profiler

這部分與 indexed-only benchmark 同樣重要。

因為目前 Engine-only 已遠快於 60× realtime 預算，因此要確認真正 desktop bottleneck。

請把完整 playback 分拆計時：

```text
Simulation advance
PlaybackFrame build
Frame publish
ResultAccumulator
Dispatcher enqueue
ApplyPlaybackFrame
Train row update
Safety row update
Event row update
Route marker render
Speed chart render
Safety chart render
Time-distance render
Timetable update
Segment statistics
Resource occupancy
V1/V2 comparison
GC / allocation
```

---

# 14. UI Profiler 原則

不要每個 frame 寫大量 log。

使用：

```text
rolling counters
aggregate timing
p50 / p95 / max
```

可用：

```text
Stopwatch
EventSource
DiagnosticSource
```

或現有 diagnostics framework。

Release 預設關閉。

---

# 15. 60× Playback Test

大型 28 站 sample。

至少跑：

```text
60×
```

若方便，也測：

```text
1×
10×
30×
60×
```

每個模式至少：

```text
real wall time >= 10 s
```

或：

```text
simulation time >= 600 s
```

取較合理者。

---

# 16. WPF Playback Metrics

至少記錄：

```text
RequestedPlaybackRate
EffectiveSimulationRate
UI frame publish rate
UI frame apply rate
Dropped frame count
Dispatcher queue delay
p50 render ms
p95 render ms
max render ms
max input stall
GC counts
allocation rate MB/s
working set
```

---

# 17. 分頁測試

分別測：

```text
Route tab
Speed chart tab
Safety tab
Time-distance tab
Timetable
Resource occupancy
V1/V2 comparison
```

目的是確認：

> 到底哪個 tab / refresh path 才是 60× 真正 bottleneck。

---

# 18. Hidden Tab 驗證

驗證未顯示分頁：

```text
沒有持續完整重算 / redraw
```

若仍有：

```text
hidden tab expensive refresh
```

請列出 exact call path。

不要順手大改，只記錄並提出 recommendation。

---

# 19. Frame Drop / Physics Parity

即使 UI drop frame：

```text
ActualWorld physics tick
events
safety
resource state
```

不可丟失。

測試：

```text
正常 UI consumption
vs
刻意慢 UI / drop frame
```

最終 Engine output 必須一致。

---

# 20. Native Desktop 與 Offscreen 分開寫

如果只能跑 offscreen WPF runner：

清楚標：

```text
offscreen result
```

不要冒充：

```text
native Windows desktop acceptance
```

如果可做原生桌面：

測：

```text
different DPI
window resize
tab switching
dragging
60× continuous playback
O04/O13/O20 visual events
```

---

# 21. 重新確認舊 2.75~3.2 ms/tick 差異

QA 歷史曾有：

```text
2.75~3.2 ms/tick
```

目前大型 sample benchmark：

```text
~0.107 ms/tick
```

請在文件中明確說明差異來源。

目前已知候選原因包含：

```text
graph bridge-distance cache
different retention
different sample state
different branch/revision
old double-world path
```

不要只寫「現在比較快」。

需做 source/history tracing，給出：

```text
主要差異來源
次要差異來源
仍不確定部分
```

---

# 22. 不要做的事情

本輪禁止：

```text
Production default 改 indexed-only
```

禁止：

```text
移除 full-scan oracle
```

禁止：

```text
改 fixed tick
```

禁止：

```text
moving block 降頻
```

禁止：

```text
直接 Parallel.ForEach(_trains)
```

禁止：

```text
UI profiler 過程順便大重構 WPF
```

禁止：

```text
為 benchmark 關掉 safety / validation
```

---

# 23. 文件更新

更新：

```text
docs/NEAREST_LEADER_INDEX_PROTOTYPE.md
QA_REPORT.md
```

新增：

```text
docs/PLAYBACK_END_TO_END_PROFILE.md
```

內容至少包含：

```text
Engine benchmark
indexed-only result
cold/warm data
allocation / GC
60× UI profile
tab breakdown
productionization recommendation
```

---

# 24. 決策輸出

最終請明確給：

```text
NEAREST LEADER INDEX:
STRONG GO / GO / NO-GO / NEEDS MORE DATA
```

以及：

```text
60× PRIMARY BOTTLENECK:
ENGINE
FRAME PIPELINE
RESULT ACCUMULATOR
WPF RENDER
CHARTS
GC/ALLOCATION
MIXED
```

可多選，但需附比例／證據。

---

# 25. 建議 Commit

例如：

```text
perf: add indexed-only leader benchmark mode
perf: measure cold and warm leader index costs
perf: add end-to-end playback profiling diagnostics
test: validate indexed-only parity against oracle
docs: record indexed-only and playback profiling results
```

不要 giant commit。

---

# 26. 驗證

執行：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

若有 performance runner：

```powershell
dotnet run --project tests/MrtRouteSimulator.PlaybackBenchmarks/MrtRouteSimulator.PlaybackBenchmarks.csproj -c Release -- <actual args>
```

依實際 CLI 為準。

---

# 27. Push

完成後：

```powershell
git push -u origin codex/nearest-leader-index-benchmark
```

不要自行 merge main。

---

# 28. 完成後回報

請回報：

1. branch HEAD SHA
2. base SHA
3. modified files
4. commit SHAs
5. oracle-only median
6. indexed-only median
7. shadow median
8. overall improvement %
9. lookup improvement %
10. graph-distance calls before/after
11. indexed candidate average / p95 / max
12. fallback count / ratio
13. mismatch count
14. cold init time
15. warm query time
16. allocation before/after
17. GC before/after
18. managed / working memory before/after
19. event parity
20. trajectory parity
21. safety parity
22. O04 parity
23. O13 parity
24. O20 parity
25. dense fixture parity
26. 60× requested/effective rate
27. UI frame publish/apply FPS
28. dropped frame count
29. dispatcher delay p50/p95/max
30. render p50/p95/max
31. slowest UI component
32. slowest tab
33. max input stall
34. primary playback bottleneck
35. nearest-leader decision:
   - STRONG GO
   - GO
   - NO-GO
   - NEEDS MORE DATA
36. Release build
37. Engine runner
38. WPF runner
39. git diff --check
40. native desktop acceptance status
41. next recommended production task

---

# 29. 最終技術原則

```text
Measure first
Indexed-only benchmark before production switch
Oracle remains available
No safety regression
No physical-truth duplication
No tick skipping
No benchmark cheating
No WPF rewrite during profiler round
```

本輪最重要的成果不是「讓 code 看起來更快」，而是：

> 用獨立 indexed-only 數據證明 nearest-leader index 的真實收益，同時找出 60× 桌面播放真正的 end-to-end bottleneck。
