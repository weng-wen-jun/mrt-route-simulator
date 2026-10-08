# Topology Nearest-Leader / Occupancy Index Shadow Prototype

> 狀態：本輪獨立 indexed-only 與 profiler 自動化驗證完成。Production leader lookup 仍以既有 full scan 為唯一權威；最新實測與決策見第 12 節。

## 1. 執行基線

- Branch：`codex/nearest-leader-index-prototype`
- Base / initial HEAD：`558e06b534f97c997f450379f747e7709528ac17`（`origin/main`，2026-09-29 fetch 後確認）
- 固定物理步進：`SimulationWorld.FixedTimeStepSeconds = 0.1 s`
- 本機外部參考資料以 `.git/info/exclude` 排除；不納入版本控制，也沒有刪除或移動原檔。
- Prototype 邊界：只做 diagnostics、唯讀候選 index、shadow compare、benchmark 與 regression；不切換 production lookup。

## 2. Current full-scan algorithm

### 2.1 Leader lookup

`SimulationWorld.FindNearestTopologyLeader`（`src/MrtRouteSimulator.Engine/SimulationWorld.cs:4350`）會：

1. 掃描 `_trains` 全集合。
2. 保留 active、非 `OutOfService`、使用 topology runtime safety 且不是 follower 自己的列車。
3. 對每個候選呼叫 `TryGetTopologySafetyMetrics`。
4. 依 `HeadDistanceMeters` 排序；同距離時依 `VehicleId`（ordinal ignore-case）排序。
5. 取第一名。

目前沒有按 edge、方向、movement plan 或 occupancy 做 broad phase。若 active topology train 數為 A，單次完整 safety pass 的 candidate evaluation 最壞為 O(A²)，且每個 follower 還會排序可達候選。

### 2.2 Graph-distance narrow phase

`TryGetTopologySafetyMetrics`（`SimulationWorld.cs:4284`）對 follower 與 candidate leader：

- 從現行 `RuntimeTopologyCursor` 建立兩個 physical footprint。
- 呼叫 `TopologyGraphDistance.TryGetForwardDistance` 算車頭距離。
- 由 leader navigator 計算 leader footprint 長度。
- `actual gap = head distance - leader length`。

`TopologyGraphDistance`（`src/MrtRouteSimulator.Engine/TopologyRuntime.cs:787`）維持物理真值：

- 同 movement plan 使用 navigator forward distance。
- 不同 movement plan 先嘗試同 edge、同方向快速路徑。
- 其餘使用受 direction/connection 約束的 graph bridge distance。
- bridge shortest path 由 `ConditionalWeakTable<InfrastructureGraphV4, ConcurrentDictionary<...>>` 快取；快取不消除每一 candidate 的 footprint、distance 與 leader-length 工作。

## 3. Tick hot path 與重複工作

`TickCore`（`SimulationWorld.cs:542-577`）的實際順序：

1. 時間前進、觸發排程障礙。
2. 啟用到期列車；可能做 departure clearance 與資源預約。
3. 完整 refresh topology occupancy。
4. pre-move `ComputeSafetyObservations`，產生 moving-block limit 與 protection leader map。
5. 逐列車 `UpdateTrain`；可移動 cursor、進出 passing/turnback、變更 reservation/resource state。
6. 完整 refresh topology occupancy。
7. `ApplyCollisionProtection`。
8. 再次完整 refresh topology occupancy。
9. 釋放已 rear-clear 的 pocket service reservation。
10. post-move `ComputeSafetyObservations`、記錄 safety history 與 trajectory。

重複 leader/metrics 工作：

- pre-move `ComputeSafetyObservations`：每個 follower 做 full leader scan。
- 選出 leader 後，`CalculateSafetyObservation` 再算一次 metrics。
- `TryCalculateMovementAuthority` 對 protection leader 再算一次 metrics。
- `ApplyCollisionProtection`：每個 follower 再做 full leader scan，選出後再算一次 metrics。
- post-move `ComputeSafetyObservations` 重複上述 safety pass。
- departure clearance、障礙物距離與特定狀態轉換還會額外觸發 scan/metrics。

目前沒有 counters，所以場景內的實際呼叫數仍待 instrumentation 量測。

## 4. Current occupancy index

`TopologyMovementOccupancyIndex`（`TopologyRuntime.cs:736`）目前只有：

```text
ownerId -> TopologyMovementFootprint
```

`Set` 會移除 owner、全掃既有 footprint 找 overlap，再寫回；`RefreshTopologyOccupancy` 則先 `Clear`，重新為所有 active topology trains 建立 footprint 並 `Set`。

一般 tick 固定完整 refresh 三次，約有 3A 次 `Set`；因每次 `Set` 掃描已寫入 owners，refresh 最壞也接近 O(A²)。`Set` 回傳的 blocking owners 在現行 refresh 中未使用。此 occupancy index 只用於外部 occupancy 查詢與 pocket rear-clear，不參與 nearest-leader lookup。

## 5. Physical truth boundary 與 versioning

物理位置權威維持：

```text
RuntimeTopologyCursor
+ TopologyMovementNavigator
+ TopologyMovementFootprint
```

禁止使用 `PositionMeters`、`ProjectedChainageMeters` 或 schematic 座標決定前後關係。

目前沒有 physical-state generation/version。`SetTrainRuntimeTopologyCursor` 可在同一個 `UpdateTrain` 中因正常前進、passing、turnback、return-to-mainline 多次改變 cursor；occupancy 只有在 `TickCore` 的明確 refresh 點才同步。Prototype 必須加入明確 generation，遵守：

```text
physical cursor/active-state mutation
-> index invalid
-> next query rebuild/refresh
```

本輪只考慮同一 physical-state generation 內重用，不做跨 tick leader cache。

## 6. Baseline benchmark inventory

既有 `tests/MrtRouteSimulator.PlaybackBenchmarks/Program.cs`：

- 預設 8,000 秒。
- CLI：`--sample/-s`、`--duration/-d`、`--planned`。
- 固定以 `AdvanceTo` 推進 0.1 秒 ticks。
- 已輸出 elapsed、ms/tick、effective rate、managed memory、working set、trajectory/safety/event count、`eventSha256`。
- retention 固定為 `SimulationTraceRetentionPolicy.Decimated(0.2)`。
- 尚無 allocated-bytes、leader/metrics/graph/occupancy counters，也沒有 5-run median/dispersion 摘要。

第一階段 smoke（使用既有 Release executable，不重建）：

- `V4.0.0-topology-baseline.mrtsim.json`、1 秒／10 ticks：完成，約 0.04 秒，trajectory=5、safety=0、events=1，成功輸出 event SHA-256。
- `大型機場線-完整營運示範範例.mrtsim.json`、duration=0：載入成功，8 車次；未進行長時模擬。
- `dotnet run --no-build --no-restore` 曾被使用者 TEMP 下的 MSBuildTemp ACL 拒絕；既有 Release executable 可執行。後續正式 build/test 應使用受控 TEMP/output 或經核准的外部執行。

大型 scenario 既有回歸涵蓋 O04/O13 passing、O20 pocket turnback、tail-track、physical footprint/rear-clear；prototype parity tests 尚未加入。

### 6.1 Instrumented single-run baseline（2026-09-29）

下表是 instrumentation 完成後的單次 Release run，尚未達成「預熱後至少五次」的正式 benchmark gate，因此只用來確認量級與挑選下一步，不用來宣稱穩定效能提升。

| Metric | 7-run full-topology sample | 28-station large sample |
|---|---:|---:|
| requested duration / ticks | 8,000 s / 80,000 | 8,000 s / 80,000 |
| elapsed with diagnostics | 1.82 s | 9.01 s |
| ms/tick with diagnostics | 0.023 | 0.113 |
| average / peak active trains | 0.331 / 2 | 2.510 / 6 |
| leader lookups | 79,499 | 602,454 |
| full-scan candidate pairs | 14,629 | 1,409,782 |
| topology safety metric calls | 25,153 | 1,786,921 |
| graph-distance calls | 24,253 | 1,783,438 |
| occupancy refresh / set | 240,000 / 79,487 | 240,000 / 602,310 |
| ComputeSafetyObservations time | 169.300 ms | 1,439.965 ms |
| FindNearestTopologyLeader time | 118.578 ms | 1,673.448 ms |
| TopologySafetyMetrics time | 107.509 ms | 1,726.901 ms |
| graph-distance time | 25.964 ms | 457.982 ms |
| ApplyCollisionProtection time | 85.340 ms | 799.048 ms |
| occupancy refresh time | 101.158 ms | 352.903 ms |
| TickCore time | 1,075.493 ms | 8,206.902 ms |
| allocated bytes delta | 450,675,728 | 4,700,807,216 |
| event SHA-256 | `3E60300B...B74577` | `7746B7DD...F1D2D` |

大型 sample 另做一次 diagnostics 關閉的同場景 run：9.01 s、0.113 ms/tick、allocated bytes delta 4,761,917,240，event SHA-256 仍為 `7746B7DDB5FBA1449971A956BD56D8DFD5178205556DC94BDBD7E5EE6B8F1D2D`。單次 wall-clock 幾乎相同，且 regression test 已逐項比較 events、trajectory 與 safety history；這只能證明 instrumentation 沒有可見輸出差異，不能替代正式五次 median/dispersion。

初步量級結論：大型 sample 的 602,454 次 leader lookup 會評估 1,409,782 個 full-scan pair，metrics/graph-distance 又因選出 leader 後重算而上升至約 178 萬次；nearest-leader 與 occupancy refresh 都值得進入 shadow prototype，但目前尚未測量 index build/query 成本。

## 7. Shadow candidate index boundary

已完成第一版 shadow prototype；設計與實作約束如下：

- Index 是既有 physical occupancy 的唯讀查詢視圖。
- Index 只產生 `PotentialCandidates(follower)`。
- `TryGetTopologySafetyMetrics` / `TopologyGraphDistance` 仍是 narrow-phase truth。
- false positive 可接受；false negative 禁止。
- 無法證明候選為 oracle 超集時必須 fallback full scan，並記錄原因。
- Production world 仍使用 full-scan oracle 結果。

靜態 broad phase 以 `(TrackEdgeId, TravelDirection)` directed traversal 為節點，使用
`InfrastructureGraphV4.AllowsTransition` 建立 reflexive transitive closure。closure 包含所有合法方向、
directed connection 與特殊 facility edge，不設 512 traversal 上限，因此相較現行 graph-distance oracle
只會放寬、不會因路徑長度上限而縮小候選集合。動態 owner/front 資料直接由
`TopologyMovementOccupancyIndex.FootprintsByOwner` 重建，不建立第二套位置座標。

Candidate query 後仍逐一呼叫 `TryGetTopologySafetyMetrics`；同距離 tie-break 仍採既有
`VehicleId` ordinal-ignore-case。shadow 會比較 leader ID、head distance、actual gap、safety status 與
moving-block control limit；production 呼叫端永遠收到 full-scan oracle 結果。

物理 cursor 或 active state 變更會遞增 generation。index 只在 shadow 啟用且 generation 改變時重建；
一般模式採 lazy initialization，完全不建構 reachability closure。generation 不一致、follower 尚未進入
occupancy 或 owner 無法解析時記錄 fallback，並保留 oracle 結果。

## 8. Regression matrix（進行中）

| Scenario | Existing coverage located | Shadow parity status |
|---|---|---|
| same edge / normal mainline | candidate tests + diagnostics + basic parity | candidate and integrated shadow passed |
| parallel passing edges | candidate branch/passing fixture + full topology sample | oracle superset and integrated shadow passed |
| physical passing | full topology sample, 3 x 3,000 s | passing events/release and full output parity passed |
| tail track | full topology sample and repeated-tail fixture | E/W tail reversal, resource release and output parity passed |
| pocket turnback | full topology sample | reached/return/UP movement and output parity passed |
| O04 passing | large 8,000 s integrated shadow | actual request/completion/physical traversal, 0 mismatch |
| O13 passing | large 8,000 s integrated shadow | actual request/completion/physical traversal, 0 mismatch |
| O20 turnback | large 8,000 s integrated shadow | DIRECT/SECTION pocket/reversal events, 0 mismatch |
| 28-station large scenario | large airport-line full scenario at 8,000 s | 602,454 comparisons, 0 mismatch; event hash matched |
| merge / branch / repeated edge / opposite shared track | dedicated physical candidate fixture | directed graph-distance oracle superset tests passed |
| single / dense mixed-length dispatch | basic 3-station factory fixture | bounded output parity; dense completion not established |

## 9. Progress log

### 2026-09-29 — Phase 0: Git safety and source audit

- Fetched `origin` and created isolated branch from `origin/main`.
- Preserved local external reference files with repository-local exclude only.
- Confirmed full-scan leader lookup, repeated safety/collision lookups, three occupancy rebuilds per normal tick, and absence of a physical-state generation.
- Confirmed benchmark capabilities and existing scenario coverage.
- No production behavior change; no source-code instrumentation yet.

### 2026-09-29 — Phase 1: Opt-in diagnostics instrumentation

- Added `SimulationWorldPerformanceDiagnostics`, disabled by default.
- Added counters for ticks, active trains, leader lookups, full-scan candidate pairs, topology safety metrics, graph distance, occupancy refresh/set and fallback.
- Added nested timestamp measurements for `TickCore`, safety observations, nearest-leader lookup, safety metrics, graph distance, collision protection and occupancy refresh.
- Extended playback benchmark with `--diagnostics` and allocated-bytes delta.
- Added `DiagnosticsAreOptInAndPreserveSimulationOutput` regression: diagnostics-off counters remain zero; diagnostics-on produces identical event, trajectory and safety-history JSON.
- Release build: passed, 0 warnings / 0 errors.
- Targeted Engine regression: 1/1 passed.
- Large 8,000 s diagnostics-on/off event SHA-256 matched exactly.

### 2026-09-29 — Phase 2: Read-only candidate index and shadow comparison

- Added a directed-traversal reachability index whose dynamic owner/front snapshot is derived from the existing
  topology occupancy footprint.
- Added physical-state generation invalidation. The index is lazy and disabled in normal production mode; it rebuilds
  only when shadow mode observes a changed generation.
- Kept the existing full scan as the production oracle and added shadow-only candidate narrow phase plus detailed
  mismatch/fallback records.
- Large 28-station sample, 8,000 s / 80,000 ticks:
  - oracle lookups: 602,454
  - indexed successes: 602,381
  - fallbacks: 73 (`0.000121`, about 0.0121%)
  - mismatches: 0
  - average oracle/indexed candidates: 2.340 / 1.008 (about 56.9% fewer candidates)
  - indexed candidate p95 / max: 3 / 5
  - index rebuilds: 69,500, down from the first every-refresh prototype's 240,000
  - event SHA-256: `7746B7DDB5FBA1449971A956BD56D8DFD5178205556DC94BDBD7E5EE6B8F1D2D`, identical to diagnostics-off and oracle baselines
- The shadow run took 21.23 s and allocated 7,351,769,624 bytes. This is deliberately **not** a production indexed-mode
  estimate: shadow mode executes the oracle plus indexed lookup and repeats safety/control comparisons. Its 12,542 ms
  query timer includes parity recomputation and current prototype allocations.
- Release build after generation reuse/lazy initialization: passed, 0 warnings / 0 errors.
- Targeted diagnostics/shadow regression: 1/1 passed.

## 10. Follow-up after prototype validation

The candidate/shadow tests, existing O04/O13/O20 regressions, five-run benchmark and full Engine/WPF runners
are complete. A subsequent performance study should run isolated cold/warm indexed queries, measure exact indexed-only
graph calls/allocation, and investigate the mixed-length dense oracle/control fixture's bounded non-completion.
Keep the oracle as production authority while collecting that evidence.

Current recommendation: **NEEDS MORE DATA**.

### 2026-09-30 — Resume checkpoint

- Confirmed the interrupted worktree contains only prototype edits and the previous Phase 2 result.
- Started dedicated direct candidate-index tests and fixed-tick integrated shadow/determinism tests.
- Corrected `IndexQueryTimestampTicks` to stop after candidate resolution, existing metric narrow phase and leader
  ordering, before safety/control parity recomputation. Earlier Phase 2 query timings retain their historical meaning.
- Started one warm-up plus five measured 8,000 s runs for each baseline/shadow mode. Runs use the same Release
  binaries, large sample and decimated 0.2 s retention; each run is a separate process. Warm-up removes first-run
  filesystem/runtime startup effects but does not preserve process-local JIT state across runs.
- Release build at this checkpoint: 0 warnings / 0 errors.

## 11. Five-run benchmark (2026-09-30)

One discarded 8,000 s warm-up precedes five measured runs per mode. Same machine, Release build,
large 28-station sample, actual Control profile and decimated 0.2 s retention. Each run uses a fresh process.

| Mode | Run | elapsed s | allocated bytes | oracle lookup ms | indexed query ms | index rebuild ms | managed / working MiB |
|---|---:|---:|---:|---:|---:|---:|---:|
| baseline | 1 | 8.57 | 4,783,080,184 | 1667.529 | — | — | 41.3 / 117 |
| baseline | 2 | 8.63 | 4,782,962,088 | 1657.239 | — | — | 41.2 / 117.2 |
| baseline | 3 | 8.82 | 4,782,289,512 | 1712.047 | — | — | 56.6 / 117.3 |
| baseline | 4 | 8.58 | 4,836,157,656 | 1695.397 | — | — | 43.9 / 117.3 |
| baseline | 5 | 8.51 | 4,781,963,968 | 1652.453 | — | — | 56.2 / 117 |
| shadow | 1 | 21.56 | 7,360,817,080 | 1615.149 | 839.779 | 16.185 | 43.3 / 117.5 |
| shadow | 2 | 21.31 | 7,323,369,048 | 1633.026 | 873.334 | 16.272 | 55.6 / 117.6 |
| shadow | 3 | 21.77 | 7,363,681,432 | 1643.970 | 876.728 | 16.261 | 46 / 117.4 |
| shadow | 4 | 21.58 | 7,358,854,032 | 1616.818 | 877.025 | 16.679 | 41.4 / 117.6 |
| shadow | 5 | 20.94 | 7,360,718,808 | 1635.138 | 871.167 | 14.554 | 43.2 / 117.4 |

- Baseline elapsed median: **8.58 s**, range 8.51–8.82 s; median 0.10725 ms/tick.
- Shadow elapsed median: **21.56 s**, range 20.94–21.77 s; median 0.26950 ms/tick.
- Baseline oracle lookup median: 1,667.529 ms.
- Shadow indexed query median (candidate resolution + narrow phase + ordering): 873.334 ms.
- Shadow index rebuild median: 16.261 ms (69,500 rebuilds).
- Allocation median: baseline 4,782,962,088 bytes; shadow 7,360,718,808 bytes.
- All measured runs: 80,000 ticks; 620 events; 101,370 trajectory samples; 110,921 safety samples;
  602,454 oracle lookups; 1,409,782 oracle candidate pairs; 1,783,438 production graph-distance calls;
  240,000 occupancy refreshes; 602,310 occupancy sets.
- Every shadow run: 602,381 indexed successes, 73 conservative fallbacks (5 follower-not-indexed,
  68 stale-generation), 0 mismatches; average candidates 2.340 → 1.008; p95/max after 3/5.
- Every run event SHA-256: `7746B7DDB5FBA1449971A956BD56D8DFD5178205556DC94BDBD7E5EE6B8F1D2D`.

A subtraction estimate is 8.58 − 1.667529 + 0.873334 + 0.016261 = **7.802066 s**
(0.097526 ms/tick, about 9.1% below baseline). This is an optimistic lookup-only projection,
not an independently executed indexed world. Shadow queries run after the oracle and benefit from its
warm graph cache; oracle counters remain unchanged because production keeps using the oracle.
Indexed-only graph-call/allocation totals and cold-query performance have not been measured.
The measured lookup/rebuild subtotal supports further work, but the productionization decision remains
**NEEDS MORE DATA** until those limits and the remaining scenario gates are resolved.

### 2026-09-30 — Phase 3 regression checkpoint

- Registered five candidate-index boundary tests and four integrated shadow tests in the normal Engine runner.
- Direct candidate tests: 5/5 passed (same/adjacent edge, actual multi-edge footprint, tie-break, branch/merge/passing,
  opposite direction, tail reversal, repeated traversal, oracle superset, stale/missing follower fallback).
- Full topology: three 3,000 s deterministic shadow replays matched one oracle-only replay for event/safety/trajectory
  hashes and final train state; passing, pocket and both terminal tail-track events and resource releases were observed.
- Large 8,000 s integrated shadow test: O04/O13 passing, O20 pocket reversal, completion, no collision/stop violation passed.
- Reset testing exposed stale diagnostic reason records after counter reset. Fixed `ResetPerformanceDiagnostics()` to
  clear shadow mismatch/fallback details together with counters; targeted Reset regression then passed 1/1.
- Mid-run shadow toggle parity passed. All successful indexed lookups compare safety/control on every actual lookup;
  retained result samples in the long replay tests use 1 s retention to bound test memory.
- Separate startup smoke measured lazy closure initialization at 11.144 ms and 505,128 allocated bytes (single run).
- Full Engine/WPF runner and dense mixed-length dispatch verification remain next gates.

- Full offscreen WPF runner passed on 2026-09-30: visual rules, project loading/transactional failure,
  playback worker, long speed journeys, and CSV/PNG/PDF output. This does not establish native Windows
  DPI/manual interaction or 8,000 s continuous desktop playback acceptance.

- Added one single-train and one dense 8-run/10 s dispatch parity fixture with alternating 20/80 m vehicles.
  The dense shadow and oracle-only control match at 1,200 s, but both retain three active trains near S02.
  This fixture is therefore a bounded stress/parity test: it requires all eight vehicles to depart, peak active >= 3,
  matching event/safety/trajectory/final-state hashes and no collision/stop violation. It is not evidence of all-runs
  completion. That shared oracle/control limitation was observed during fixture validation and left outside this
  indexing task's scope; the full topology and large sample completion gates are independently required.

### 2026-09-30 — Final prototype validation

- Full Engine runner: **184/184 passed, 0 failed**; baseline had 173 tests, this branch adds 11.
- Full Release solution build: **0 warnings / 0 errors**.
- Full offscreen WPF runner: **PASS**; native desktop/manual acceptance remains unverified.
- `git diff --check`: **PASS**.
- Reviewed source changes and integrated subagent tests. Corrected the cross-edge fixture to actually span MAIN-1/TAIL,
  corrected the repeated-tail cursor leg/traversal index, and fixed Reset diagnostic detail/counter coherence before final validation.
- Final HEAD/base: `558e06b534f97c997f450379f747e7709528ac17`. All implementation/test/docs edits are uncommitted;
  no commit, push, tag, merge or release was performed.
- Current supported topology's candidate-superset argument is documented in section 7 and exercised by graph-distance
  fixtures. This finite regression set does not prove arbitrary future topology/runtime variants.
- `Rebuild` rejects an invalid footprint traversal by exception; current navigator/graph validation prevents such input.
  Invalid topology is not silently accepted as a fallback scenario. Valid runtime stale/missing-owner queries conservatively use the oracle.
- Long replay output hashes use 1 s retained samples. Safety/control shadow comparisons still occur at every actual lookup;
  a standalone every-lookup deterministic sequence fingerprint has not been added.
- **Decision: NEEDS MORE DATA. Do not productionize in this round.** Measured broad-phase reduction is meaningful,
  and query + rebuild cost is below observed oracle lookup cost. The full scan represents about 19.4% of baseline wall time,
  not the whole runtime; the projected total gain is about 9.1%. Independent cold-query timing and indexed-only allocation
  remain necessary before a production decision. No control-interval study was started.

## 12. Indexed-only and playback profiler round (2026-09-30)

- User explicitly approved continuing with the uncommitted prototype worktree. Branch remains
  `codex/nearest-leader-index-prototype`; HEAD and freshly fetched `origin/main` both remain
  `558e06b534f97c997f450379f747e7709528ac17`. No merge/cherry-pick/commit/push occurred.
- `docs/OPEN_SOURCE_RAIL_SIM_COMPARISON.md` and `docs/OPEN_SOURCE_ARCHITECTURE_RECOMMENDATIONS.md`
  were absent from this checkout and the all-files search under `D:/AI/codex`; their contents are not assumed.
- Added explicit `BenchmarkIndexedLeaderLookupEnabled` opt-in for isolated benchmark/test worlds. Default remains
  oracle-only. Shadow and indexed-only are mutually exclusive. Both share identical candidate/narrow-phase code;
  indexed-only directly returns that result and conservatively falls back on stale/missing index snapshots.
- Generation invalidation/rebuild now also runs for indexed-only worlds. Performance counters include real
  indexed narrow-phase graph/metric calls, first lookup and first graph-call latency.
- Added full-retention oracle/indexed output parity for full topology and large sample; dense mixed-length and Reset parity.
  Candidate boundary fixtures from the first round remain in the runner.
- End-to-end profiler and history tracing completed; default profiler remains disabled. Full details and final
  validation manifest are in `PLAYBACK_END_TO_END_PROFILE.md` (Engine 186/186, full offscreen WPF PASS).

### Independent isolated world results

Protocol: 2026-09-30, same local machine, Release net10.0, current dirty prototype over HEAD/base
`558e06b534f97c997f450379f747e7709528ac17`. Large airport 28-station sample, 8,000 s / 80,000 fixed ticks,
trajectory Decimated(0.2) and default safety retention, all modes `--diagnostics` enabled.
Each mode had one discarded warm-up followed by five fresh-process measured runs; modes ran serially,
without builds or WPF profiling in parallel. Raw summaries (including discarded warmups) are in
`docs/INDEXED_ONLY_BENCHMARK_RAW.json`.

| Metric | Oracle-only | Indexed-only | Shadow |
|---|---:|---:|---:|
| elapsed median s | 9.417129 | 9.077801 | 23.266440 |
| elapsed min–max s | 9.347780–9.507597 | 8.872212–9.495322 | 22.771982–23.392308 |
| ms/tick | 0.117714 | 0.113473 | 0.290831 |
| effective sim × | 849.52 | 881.27 | 343.84 |
| leader lookup ms | 1846.997 | 1333.777 | 1820.297 |
| index query ms | 0.000 | 1165.656 | 1047.821 |
| runtime rebuild ms | 0.000 | 21.578 | 21.379 |
| graph-distance calls | 1783438 | 984253 | 1783438 |
| safety metric calls | 1786921 | 984667 | 1786921 |
| candidates avg | 2.340066 | 1.008216 | 1.008216 |
| candidate p95/max | not collected | 3/5 | 3/5 |
| fallback count / ratio | — | 73 / 0.012117% | 73 / 0.012117% |
| mode init + run allocated bytes | 4752636824 | 4614363608 | 7364869000 |
| managed MiB (end snapshot) | 54.147 | 56.341 | 48.903 |
| working MiB (end snapshot) | 118.395 | 118.625 | 118.938 |
| Gen0/1/2 median | 288/70/5 | 279/68/5 | 444/106/5 |
| shadow mismatch | — | not internally compared | 0 |

- Whole-engine median improvement: **3.60%**; lookup-only improvement: **27.79%**.
- Allocation change: **-2.91%** including index mode initialization; managed snapshot change
  2.194 MiB, working snapshot change 0.230 MiB.
  These are non-forced-GC end snapshots, not peak or retained-live-heap measurements.
- Real indexed graph calls reduce by 44.81%; candidate mean reduces by
  56.92%. All runs record 602,454 leader lookups, zero active trains
  at 8,000 s and completed worlds. Indexed/shadow rebuild count is 69,500, successful indexed queries 602,381.
- Fallbacks: 5 missing follower and 68 stale-generation queries per run; conservatively preserve full scan.
- Every one of the 18 processes has event hash
  `7746B7DDB5FBA1449971A956BD56D8DFD5178205556DC94BDBD7E5EE6B8F1D2D`.
  Indexed-only does not compare its own result to an oracle in the hot path, so its zero shadow-mismatch
  counter is not evidence of parity; separate full-retention regression comparisons provide that evidence.

### Cold/warm observations and limits

| Metric (five-run median) | Oracle-only | Indexed-only | Shadow |
|---|---:|---:|---:|
| index-mode init ms | 0.0000 | 10.5989 | 10.3898 |
| index-mode init bytes | 0 | 505128 | 505128 |
| first leader lookup ms | 0.0230 | 3.5275 | 0.0234 |
| first recorded graph call ms | 9.8228 | 10.0354 | 8.8965 |

Index-mode initialization includes directed reachability closure, initial index structures and empty occupancy
rebuild; excludes sample parsing, runtime/world construction. First lookup initially has only one active train
and no leader candidate, so it is not a representative populated query. The first recorded graph call is
separate and is not a measurement of full graph-cache initialization; cache populates lazily with new bridge keys.
Fresh processes ensure no previous-world graph cache warming. Shadow indexed queries run after oracle,
so their graph cache and disabled per-query diagnostics differ from isolated indexed-only query costs.

First-tick-removed allocation proxy medians:
- oracle-only: 4752598392 bytes; first tick 38176 bytes.
- indexed-only: 4613818480 bytes; first tick 39744 bytes.
- shadow: 7364321280 bytes; first tick 42336 bytes.

The raw JSON supplies first-tick counters and first-call-removed timing totals. These are operational warm proxies,
not a proven fully warm-cache microbenchmark: later first-seen routes still populate caches; console/progress,
diagnostic snapshots and timing instrumentation are included in run allocations. The allocation "total" is
mode initialization plus run, not process-lifetime total. Runtime rebuild count excludes the setter's cold rebuild.
Indexed-only still scans `_trains.Count(...)` for diagnostic oracle-candidate counts and resolves owners linearly;
these prototype costs are honestly included. Shared historic `Shadow*` counter names also serve indexed-only.
Shadow graph/metric counters count production/oracle work only, excluding its extra comparison narrow phase;
indexed-only counters record its actual narrow phase. Do not sum or directly rank shadow query costs against isolated ones.

### Decision

**NEAREST LEADER INDEX: NEEDS MORE DATA.** Measured 3.60% is between the task's NO-GO (<3%)
and GO (>=5%) bands, with overlapping elapsed ranges. Conservative fallbacks, parity and modest allocation
reduction are positive, but the projected ~9.1% was not reproduced by the actual isolated implementation.
Do not switch production defaults. Next authorized productionization study should remove diagnostic full-count
and linear owner-resolution overhead in an isolated branch, then repeat multi-workload/interleaved measurements
and fully warmed populated-query microbenchmarks. No such optimization is silently included in this round.

