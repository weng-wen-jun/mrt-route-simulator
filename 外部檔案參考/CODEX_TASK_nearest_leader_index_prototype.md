# Codex 任務：Topology Nearest-Leader / Occupancy Index Shadow Prototype

## 0. 任務目的

Repository：`weng-wen-jun/mrt-route-simulator`

請針對 topology-native V2 runtime 實作一個**隔離式、shadow-mode 的 nearest-leader / occupancy candidate index prototype**，驗證目前 `FindNearestTopologyLeader` / `TryGetTopologySafetyMetrics` / `TopologyGraphDistance` 全掃描是否為主要效能瓶頸，以及是否能在**完全不改 production 模擬結果**的前提下降低每 tick 搜尋成本。

依據既有調研：

- `SimulationPlaybackWorker` 已是 ActualWorld 單一 writer。
- `PlaybackFrame` 已不可變且 latest-frame-wins。
- `SimulationResultAccumulator` 已增量消費。
- 現在更值得優先研究的是 topology 前車查找、graph distance、occupancy 重複工作。
- 新索引必須從既有 `TopologyMovementOccupancyIndex` / physical footprint 派生，不得建立第二套位置權威。

核心原則：

```text
Prototype first
Production behavior unchanged
Existing full scan = oracle
Index = broad-phase candidate generator only
TopologyGraphDistance = narrow-phase truth
Fixed tick remains 0.1 s
No safety weakening
No second physical truth
```

---

## 1. Git 安全流程

```powershell
git fetch origin
git status
```

若工作樹不是 clean：立即停止並回報；不得 `reset / clean / stash / force push`。

若 clean：

```powershell
git switch -c codex/nearest-leader-index-prototype origin/main
```

不要直接修改 `main` / `GPT-use`，不要自行 merge PR、tag 或 release。

---

## 2. 本輪範圍

只做：

```text
A. Profiling / instrumentation
B. Read-only candidate index
C. Shadow compare against full scan
D. Benchmark
E. Tests / docs
```

**不要在本輪把 production leader lookup 切換成 indexed lookup。**

只有在 shadow prototype 證明「零差異 + 候選大幅下降 + index 建置成本值得」後，下一輪才 productionize。

---

## 3. 先核對最新 main 的 hot path

先閱讀並定位：

```text
SimulationWorld.cs
TopologyRuntime.cs
TopologyMovementOccupancyIndex
FindNearestTopologyLeader
TryGetTopologySafetyMetrics
TopologyGraphDistance
ComputeSafetyObservations
ApplyCollisionProtection
TickCore
```

先確認：

1. `FindNearestTopologyLeader` 是否仍掃所有 active topology trains。
2. 每 follower 每 tick 的 leader lookup / safety metrics / graph distance 呼叫數。
3. `ComputeSafetyObservations` 與 collision protection 是否重複做相似查找。
4. occupancy 每 tick / state mutation 更新幾次。
5. 同一 physical-state version 是否有可安全共用的 footprint / candidate query。

若最新 code 與調研報告不同，以實際 code 為準並回報。

---

## 4. Baseline instrumentation

至少加入可關閉的 diagnostics counters：

```text
tickCount
activeTrainCount
leaderLookupCount
candidatePairCount
topologySafetyMetricCallCount
graphDistanceCallCount
occupancyRefreshCount
occupancySetCount
fullScanFallbackCount
```

時間量測至少分開：

```text
ComputeSafetyObservations
FindNearestTopologyLeader
TryGetTopologySafetyMetrics / graph distance
ApplyCollisionProtection
occupancy refresh / index build
whole TickCore
```

Release 正常模式不得大量 log，也不要讓 instrumentation 明顯增加 allocation。

---

## 5. Baseline benchmark

優先重用：

```text
tests/MrtRouteSimulator.PlaybackBenchmarks/Program.cs
```

使用代表性大型 sample，至少：

```text
AdvanceTo(8000 s)
```

同機、同 Release、同 sample、同 retention：預熱後至少 5 次，記錄 median 與離散度。

至少記錄：

```text
elapsed
ms/tick
managed allocation / memory
event count
trajectory count
safety count
eventSha256（若已有）
leaderLookupCount
candidatePairCount
graphDistanceCallCount
occupancyRefreshCount
```

不要只用三站小 sample 下結論。

---

## 6. Candidate Index 設計

Index 必須是既有 physical occupancy 的**唯讀查詢視圖**。

建議從 `TopologyMovementOccupancyIndex` 派生：

```text
ownerId -> RuntimeTopologyCursor + TopologyMovementFootprint
TrackEdgeId -> occupied intervals ordered by offset
(TrackEdgeId, direction) -> vehicle fronts ordered by offset
```

必要時可再有：

```text
movement traversal -> downstream candidate edges
```

但不得建立第二套 route / chainage truth。

不得使用：

```text
ProjectedChainageMeters
UI x-coordinate
schematic left/right
```

作 physics order。

---

## 7. Broad-phase / Narrow-phase

Index 只做：

```text
PotentialCandidates(follower)
```

最後仍交給現有：

```text
TryGetTopologySafetyMetrics
TopologyGraphDistance
```

判斷真正的：

```text
reachable
ahead
head distance
rear gap
leader ordering
```

也就是：

```text
Index = broad-phase
Existing graph-distance logic = narrow-phase truth
```

---

## 8. 必須考慮的 topology 邊界

至少涵蓋：

```text
same edge
adjacent downstream edge
vehicle tail spanning multiple edges
merge
branch
passing loop
crossover
pocket
tail track
turnback
repeated edge in movement plan
opposite-direction shared track
```

任何無法保證 candidate set 為 oracle 超集的情況：

```text
fallback full scan
```

不要猜。

---

## 9. Shadow mode

建立 shadow lookup：

```text
oracleLeader  = current full scan
indexedLeader = indexed shadow lookup
```

正式 world 行為**仍使用 oracleLeader**。

每次比對：

```text
leader vehicle id
head distance
rear gap
safety status
control/braking-relevant result
```

距離使用既有 numerical tolerance。

若 indexed path 無法保證完整：fallback，並記錄原因。

---

## 10. 正確性規則

索引候選可以多：

```text
false positive = acceptable
```

但不能少：

```text
false negative = forbidden
```

同距離 tie-break 必須沿用 production 既有規則（例如 VehicleId；以最新 code 為準）。

每次 mismatch 至少記錄：

```text
simulation time
follower
oracle leader
indexed leader
follower cursor
head/rear distance
current edge
traversal index
candidate list
fallback status/reason
```

Benchmark 摘要至少輸出：

```text
lookup count
indexed-success count
fallback count / ratio
mismatch count
average candidates before/after
p95 candidates after
max candidates after
```

---

## 11. Occupancy / Index versioning

先畫出最新 `TickCore` 的實際 mutation sequence。

若同一 tick 內會經過：

```text
dispatch
movement
collision
turnback
passing switch
resource state change
```

則 index 必須綁定明確 physical state version / generation。

原則：

```text
physical state mutation
→ index invalid
→ next query rebuild/refresh
```

本輪優先只做**同一 physical-state-version reuse**，不要直接做跨 tick leader cache。

---

## 12. 必測 scenarios

至少：

### Basic

```text
single train
two trains same direction
dense dispatch
different vehicle lengths
```

### Footprint

```text
head on one edge, tail on previous edge
partial shared-edge occupancy
```

### Topology

```text
branch
merge
crossover
opposite-direction shared track
loop/repeated edge if supported
```

### Project-specific

```text
O04 passing
O13 passing
O20 pocket turnback
tail-track reversal
28-station Taichung large scenario
```

若目前 main 尚無某 full sample，使用現有最接近 regression 並清楚記錄未覆蓋範圍，不要自行捏造。

---

## 13. 每 0.1 秒 parity

重要 scenarios 盡量逐 fixed tick 比較：

```text
leader id
head distance
rear gap
safety status
control braking decision
collision state
```

最終再比較：

```text
full event sequence
arrival/departure times
overtake order
turnback events
route reserve/release
collision count
StationStopViolation count
```

Prototype 不得改 production output。

---

## 14. Benchmark 解讀

Shadow mode 同時跑 oracle + index，本身可能比 baseline 慢。

所以除了 shadow wall-clock，必須另外評估：

```text
index build cost
index query cost
full scan saved candidate cost
graph-distance calls reduced
```

最好做 benchmark-only isolated replay / query benchmark，估算 indexed-only 成本。

不要直接把 shadow mode wall-clock 當 production index 效能。

至少產出：

| Metric | Full Scan Baseline | Shadow/Indexed Estimate |
|---|---:|---:|
| ms/tick | | |
| 8000 s elapsed | | |
| leader lookups | | |
| candidate pairs | | |
| graph-distance calls | | |
| index build time | | |
| index query time | | |
| fallback ratio | | |
| mismatches | | |
| allocation | | |
| memory | | |

---

## 15. Productionization Gate

只有全部滿足才可建議下一輪進 production：

```text
mismatch = 0
all required regressions covered
fallback conservative and acceptable
candidate count materially reduced
index build + query cost < current full-scan cost
no changed event ordering
no changed safety result
no second physical truth
```

否則：

```text
DO NOT PRODUCTIONIZE
```

---

## 16. 本輪禁止事項

禁止：

```text
Parallel.ForEach(_trains) directly mutating world
Task per train
change FixedTimeStepSeconds
skip physics ticks
downsample moving-block safety
change braking envelope
change rear-clear semantics
change resource arbitration
ProjectedChainage as leader truth
weaken validators/tests
```

也不要順便重做：

```text
WPF worker architecture
PlaybackFrame
ResultAccumulator
planned timeline architecture
```

除非只是增加 benchmark 所需的最小 instrumentation。

---

## 17. 可選 Prototype 2：Control Interval Shadow Study

只有 nearest-leader prototype 完成且尚有時間才做。

只能 isolated/shadow：

```text
physics integration = 0.1 s
control decision candidate = 0.2 / 0.5 s
safety guard = every 0.1 s
```

比較：

```text
trajectory
speed
acceleration
jerk
station stop
safety
collision
rear-clear
event ordering
```

任何會改 physics/safety 的結果都標記 unsuitable；不得接 production。

---

## 18. Tests

至少新增等價測試：

```text
IndexedCandidateSetContainsOracleLeader
IndexedLeaderShadowMatchesFullScan
LeaderTieBreakMatchesOracle
CrossEdgeTailDoesNotMissLeader
MergeDoesNotMissLeader
PassingDoesNotMissLeader
TurnbackDoesNotMissLeader
FallbackReturnsOracle
```

命名可依現有 test style 調整。

同一 scenario 至少重跑 3 次，確認 deterministic：

```text
event sequence
event timestamps
leader sequence
collision
StationStopViolation
route reservation order
overtake order
turnback order
```

---

## 19. 文件

新增：

```text
docs/NEAREST_LEADER_INDEX_PROTOTYPE.md
```

至少包含：

1. Current full-scan algorithm
2. Hot-path measurements
3. Index data structure
4. Physical truth boundary
5. Broad-phase candidate generation
6. Graph-distance narrow-phase
7. Fallback cases
8. Shadow oracle strategy
9. Regression matrix
10. Benchmark
11. Productionization GO / NO-GO decision

更新 `QA_REPORT.md`，記錄 baseline、candidate reduction、fallback、mismatch、benchmark、regression。

不要宣稱 production 已優化，除非另有 production commit。

---

## 20. Build / Test

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

若現有 benchmark project 可用：

```powershell
dotnet run --project tests/MrtRouteSimulator.PlaybackBenchmarks/MrtRouteSimulator.PlaybackBenchmarks.csproj -c Release -- <實際支援參數>
```

不要硬套不存在的 CLI options。

---

## 21. 建議 commits

```text
perf: instrument topology leader lookup hot path
perf: add shadow nearest-leader candidate index
test: add indexed leader parity regressions
perf: benchmark topology leader candidate index
docs: document nearest-leader index prototype results
```

不要 giant commit。

---

## 22. Push

完成後：

```powershell
git push -u origin codex/nearest-leader-index-prototype
```

不要自行 merge main。

---

## 23. 完成後回報

請回報：

1. branch HEAD SHA
2. base SHA
3. modified files
4. commit SHAs
5. 最新 `FindNearestTopologyLeader` 實作摘要
6. baseline scanning complexity
7. baseline graph-distance call count
8. occupancy refresh count
9. index data structure
10. invalidation/version rule
11. candidate query strategy
12. fallback conditions
13. shadow lookup count
14. mismatch count
15. fallback count / ratio
16. average candidates before/after
17. p95 / max candidate count
18. baseline ms/tick
19. shadow ms/tick
20. isolated indexed-only estimated/actual ms/tick
21. 8000 s elapsed baseline / estimate
22. allocation / memory
23. event parity
24. safety parity
25. O04 passing parity
26. O13 passing parity
27. O20 turnback parity
28. cross-edge tail parity
29. Release build
30. Engine runner
31. WPF runner
32. `git diff --check`
33. Productionization recommendation：`GO / NO-GO / NEEDS MORE DATA`
34. 下一步建議

---

## 24. 最終判斷問題

這一輪真正要回答的不是「索引能不能寫」，而是：

> **能否證明索引永遠不漏掉 full scan 會找到的合法 leader，而且 index 建置成本確實低於它節省的 pair scan / graph-distance 成本。**

在證明前，現有 full scan 必須繼續作為 production oracle。
