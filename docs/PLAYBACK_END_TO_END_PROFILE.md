# Indexed-only benchmark and end-to-end playback profile

## Current native continuation — 2026-10-06

Current evidence uses the V4.0.3 integration at `D:/AI/codex/mrt-v403-integration`, not the historical V4.0.2-derived tree below. See [native checkpoint](NATIVE_DESKTOP_ACCEPTANCE.md) and [detailed ledger](NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md).

Same-process sample14/Diagram/120DPI five-run normal60× session `9c989122` completes all five7172.9s runs; active-wall rates59.99988754–60.00110921, each1025 full-field events equal the baseline. Five afterReset actual caches clear, including the final reset before collector Stop. A separate three-run post-reset-start-fix session verifies the early-reset sub-gate; configurations/binaries are not pooled. These rates use published completion-frame boundaries, not exact final Engine-tick timing.

Release0 warnings/errors, Engine198/198, complete WPF and dedicated synchronous-export/retention runners pass. Retention forced GC is test-only, outside formal native playback. Private-memory growth and prior unexplained dispatcher gaps remain UNRESOLVED; zero new timer gaps is not OS input/compositor proof. Remaining DPI100/150% matrix requires actual telemetry after manual scaling. Native overall remains NOT COMPLETED; no new Engine performance investigation or nearest-leader productionization.

Latest sticky-scrollbar binary, independent100%/96DPI session `d70a3e60`: five same-process sample14/Diagram60× runs complete without Pause;1025 events per run match9c all fields, active rates59.99693298–60.00276976 (publication boundary, not exact final tick). All five normal Reset checkpoints follow valid10s idle and precede Stop. Actual caches clear; raw gaps0 in this session only. Reset private240.70→270.53MiB is UNRESOLVED, not a leak or bounded-memory proof; direct owner play/reset n5 p95/max80.1657/64.6809ms does not meet the50ms recommendation. The user-confirmed800×520 DIP native chart exposed too little height with events expanded; requested Presentation-only minimum viewport/collapsible controls repair is pending. These results do not replace the old125% session or full DPI/input gates.

## Final native preparation — 2026-10-01

Presentation station-label collision layout and App-only opt-in input/memory evidence preparation were added; see [execution log](NATIVE_PLAYBACK_FINAL_PROGRESS.md) and [native acceptance boundary](NATIVE_DESKTOP_ACCEPTANCE.md). Incremental/full renderers share label layout without changing trajectory/grid coordinates. Five-run diagnostics preserve normal playback handlers, add process/cache/GC checkpoints and timestamped >100ms stall contexts. Input/apply proxies are not OS-injection/compositor latency.

User requested stopping after fixes/regressions and will return for page/DPI operations. This phase has zero new native runs; no new input percentile, memory-boundedness, event visual, DPI or throughput PASS is claimed. The previous three native throughput sub-gates remain historical PASS; native overall remains NOT COMPLETED and leader default remains oracle. Fresh regression results are recorded in the final progress log.

Status: measurement and automated validation completed, 2026-09-30. This round measures explicitly isolated indexed-only worlds and opt-in WPF diagnostics.
Normal application configuration remains oracle-only. All native desktop claims require actual native acceptance;
offscreen WPF timings will be labeled separately.

## Worktree and validation boundary

User authorized continuing the existing uncommitted prototype. Branch `codex/nearest-leader-index-prototype`,
HEAD/base `558e06b534f97c997f450379f747e7709528ac17`; no publication actions are authorized by this continuation.

## Measurement scope (completed; native acceptance excluded)

- One warm-up and five fresh-process 8,000 s runs per oracle-only/indexed-only/shadow mode, same sample and retention.
- Mode initialization allocation/time, first lookup/graph call, steady-state query cost, managed/working memory and GC.
- Full-retention event/trajectory/safety/final-state parity and explicit O04/O13/O20 event gates.
- 60x offscreen playback, each visible tab, worker/frame/accumulator/dispatcher/UI component aggregate timings.
- Hidden-tab refresh audit, intentionally slow UI/frame-drop parity, source/history explanation of old ms/tick numbers.
- Final Engine/WPF regression and productionization recommendation.

## Historical timing reconciliation (source/history verified)

The old 2.75275 ms/tick was a single ActualWorld `AdvanceTo(8000)` at the temporary `e57d16b`
baseline with Full trajectory/safety retention (220,220.04 ms, 204,736 trajectory / 113,646 safety / 621 events).
Phase 1's 3.20274 ms/tick likewise measured a single ActualWorld; its actual playback-only path measured
3.18534 ms/tick. The old dual-world measurements were **3.84774 and 4.23581**, not 2.75/3.2.
The 0.10725 ms/tick prototype baseline was already the current single-world, cached-graph full-scan oracle.
It is not an improvement caused by the candidate index.

- Strongest direct acceleration evidence: `090a9aa24268c6fa8b46ea22c2331d20a5484852` adds graph-instance/
  directed traversal bridge-distance caching in `TopologyRuntime.cs` and removes a repeated graph search
  for footprint gap in `SimulationWorld.cs`. QA records a 600 s sample dropping from 47.83 s to 1.69 s
  with identical event SHA-256, and an 8,000 s run at 7.98 s. This already reaches the current timing scale.
- Confirmed additional hot-path change: `24de560624e2b8de24722e5213bfcdf689107468` changes `AdvanceTo`
  from `Tick()` to `TickCore()`, eliminating discarded per-tick snapshots. No isolated percentage is available.
- Retention is a measurement confounder: Full became trajectory Decimated(0.5) (`5ea27c7`) and safety
  Decimated(1.0) (`ca9c5f4`); this benchmark uses trajectory Decimated(0.2), default decimated safety.
  Phase 1 reduced retained counts to 42,742/11,401 and memory deltas, but did not claim single-world speedup.
- `925603a`'s actual-only playback path, `0a3aa51`'s planned completion and `40e8ad1`'s worker integration
  explain WPF/dual-world cost. They do not directly explain a comparison of two single-world measurements.
- The workloads are not byte-identical: old Taichung topology had 34 nodes and 180 m approach distance;
  current airport topology has 36 nodes and 65 m approach distance. `af39eb7` changes O04 geometry/split nodes.
  Both have 28 stations / 65 edges / 60 platforms. Renaming alone (`ea3dedc`) did not cause this geometry change.

No same-revision/sample/retention isolation was run for each historical optimization. JIT/GC/OS noise and
simultaneous workload changes prevent honest per-factor percentages. UI cache/incremental results are not
the principal cause of the Engine-only number. See the dated Phase 1 and 2026-09-24 entries in `QA_REPORT.md`.

The two task-referenced open-source comparison/recommendation documents were not found by an all-files
search under `D:/AI/codex`; only the earlier study task specification exists. Their contents are not assumed.

## Measurement harness corrections

Two pre-measurement attempts were discarded: direct invocation of an async-void UI handler lacked a
Dispatcher synchronization context; then the new default-off contract test closed the first temporary window
and triggered automatic Application shutdown. The profiler now explicitly sets/restores the Dispatcher context,
and the test runner owns shutdown (`OnExplicitShutdown`). Product handlers and app shutdown policy were not changed.
These failed attempts provide no accepted performance results. Release rebuild passed after both corrections.

Engine full regression after indexed-mode mutual-exclusion/Reset checks: **186/186 passed, 0 failed**.
Independent Engine benchmark summaries are in `NEAREST_LEADER_INDEX_PROTOTYPE.md` section 12 and
`INDEXED_ONLY_BENCHMARK_RAW.json`; they were completed before WPF profiling.

The first complete-tab attempt subsequently reproduced a pre-existing Safety-tab stack overflow:
`RefreshPairFilter → ComboBox.SelectionChanged → WorkspaceTabControl_SelectionChanged → UpdateV2PlaybackView`
reentered indefinitely. The sender was the workspace even for bubbled child events. The user explicitly approved
the minimal repair: the handler now requires `e.OriginalSource == WorkspaceTabControl`. No model, physics or
refresh cadence changes accompany this. A regression asserts that child events cause no workspace refresh,
while a genuine workspace event still refreshes. Measurements before this repair are discarded from the final table.

A further instrumentation review found millions of coordinator `AdvanceTo` calls with no complete 0.1 s tick.
Recording each as a productive advance both obscured p50 and added a collector lock on the hot polling loop.
Final diagnostics record productive advance latency only; no-progress count and measured call time accumulate
locally and flush once per published frame. `WorkerNoProgressAdvanceBatch` percentiles are **per-frame batch
totals**, not per-call latency. Coordinator scheduling itself is unchanged. The earlier per-call profile was
discarded, not mixed with the final measurements. The continuous-consumption parity probe now explicitly
consumes frames; the final slow-UI resource probe starts at 680 s, based on current actual events around O04.

## Final 60× automated/offscreen WPF profile

The App remains **oracle-only**. These are automated WPF test-window results, not native desktop acceptance.
Eight sequential profiles each reset the same sample world. Each measured 600.2–605.9 s simulation and
10.07–10.15 s wall time. Startup/configuration and pre-tab selection are excluded; graph/JIT/render caches
may remain warm across tabs. Each measurement includes the 50 ms pause/drain tail, so effective rate/FPS
are slightly conservative. Only 60× was measured; 1×/10×/30× were optional and not run.
Raw timing counts/totals and all JSON metrics are in `PLAYBACK_PROFILE_RAW.json`.

| Tab | Effective × | Publish / unique UI FPS | Skipped frames | UI apply p50 / p95 / max ms | Frame-ready→consume age p50 / p95 / max ms | Input max gap / excess ms |
|---|---:|---:|---:|---|---|---:|
| Route | 59.62 | 30.30 / 21.26 | 91 | 0.754 / 1.569 / 9.197 | 16.769 / 31.419 / 32.548 | 52.17 / 32.17 |
| Speed | 59.69 | 30.26 / 21.23 | 91 | 0.351 / 9.743 / 19.788 | 16.081 / 30.937 / 33.029 | 40.89 / 20.89 |
| Safety | 59.57 | 30.17 / 21.24 | 90 | 0.250 / 0.736 / 1.939 | 15.788 / 31.324 / 32.909 | 41.87 / 21.87 |
| TimeDistance | 59.67 | 30.23 / 17.79 | 126 | 0.096 / 55.727 / 80.096 | 15.147 / 30.924 / 32.885 | 108.44 / 88.44 |
| Timetable | 59.62 | 30.19 / 21.25 | 90 | 0.076 / 0.826 / 2.146 | 16.152 / 31.163 / 32.996 | 41.60 / 21.60 |
| Segment | 59.68 | 30.29 / 21.31 | 91 | 0.083 / 0.368 / 1.623 | 16.014 / 31.290 / 32.934 | 40.98 / 20.98 |
| Resource | 59.68 | 30.24 / 21.28 | 91 | 0.080 / 0.290 / 3.518 | 15.951 / 31.042 / 32.984 | 41.30 / 21.30 |
| Comparison | 59.68 | 30.29 / 21.31 | 91 | 0.083 / 0.389 / 0.804 | 16.776 / 31.473 / 32.749 | 39.62 / 19.62 |

UI apply includes accumulator and synchronous refresh preparation; it does not time later WPF composition/GPU
presentation. "DispatcherApplyAge" starts just before final immutable frame assembly/channel publish, and ends
when the 33 ms UI timer consumes the frame. This path uses timer polling, **not Dispatcher.Enqueue**; actual enqueue
latency is unavailable. Age is not claimed to be pure dispatcher wait. "Dropped" is cumulative sequence gaps
skipped by latest-frame consumption, not a count of proven channel-overwrite operations. UI FPS counts unique
consumed/applied frames, excluding forced refresh of the same frame. Input excess subtracts its requested 20 ms interval.

### Stage timings: p50 / p95 / max ms

An em dash indicates the refresh path did not execute in that tab, not a measured zero cost. Percentiles use a
bounded rolling 4,096 sample window, while count/total/max cover the full measurement. Worker and UI collectors
have disjoint stage names and are merged without averaging percentiles. Nested stages must not be added to their parent.

| Stage | Route | Speed | Safety | TimeDistance | Timetable | Segment | Resource | Comparison |
|---|---|---|---|---|---|---|---|---|
| WorkerAdvance | 0.214 / 0.307 / 4.607 | 0.160 / 0.266 / 2.868 | 0.161 / 0.290 / 2.833 | 0.167 / 0.296 / 3.836 | 0.161 / 0.289 / 17.816 | 0.165 / 0.288 / 4.286 | 0.162 / 0.288 / 2.373 | 0.160 / 0.284 / 11.424 |
| WorkerNoProgressAdvanceBatch | 4.070 / 5.083 / 8.124 | 3.810 / 4.064 / 4.179 | 3.802 / 4.077 / 4.206 | 3.781 / 4.065 / 4.397 | 3.801 / 4.075 / 4.206 | 3.789 / 4.026 / 4.232 | 3.815 / 4.035 / 4.113 | 3.792 / 4.026 / 4.244 |
| WorkerFrameBuild | 0.031 / 0.060 / 0.283 | 0.013 / 0.024 / 0.035 | 0.014 / 0.025 / 0.034 | 0.015 / 0.025 / 0.034 | 0.013 / 0.024 / 0.037 | 0.014 / 0.025 / 0.059 | 0.013 / 0.024 / 0.059 | 0.013 / 0.026 / 0.038 |
| WorkerFramePublish | 0.001 / 0.002 / 0.006 | 0.001 / 0.001 / 0.002 | 0.001 / 0.001 / 0.003 | 0.001 / 0.001 / 0.002 | 0.001 / 0.001 / 0.002 | 0.001 / 0.001 / 0.002 | 0.001 / 0.001 / 0.005 | 0.001 / 0.001 / 0.003 |
| ResultAccumulator | 0.073 / 0.308 / 8.200 | 0.030 / 0.123 / 0.249 | 0.031 / 0.089 / 0.159 | 0.032 / 0.095 / 0.173 | 0.031 / 0.079 / 0.149 | 0.031 / 0.079 / 0.123 | 0.032 / 0.081 / 0.107 | 0.030 / 0.076 / 0.148 |
| TrainRowUpdate | 0.106 / 0.200 / 0.436 | 0.058 / 0.078 / 1.048 | 0.055 / 0.075 / 0.084 | 0.056 / 0.081 / 1.847 | 0.054 / 0.068 / 0.085 | 0.057 / 0.071 / 0.084 | 0.057 / 0.075 / 0.083 | 0.055 / 0.077 / 0.262 |
| SafetyRowUpdate | — | — | 0.321 / 0.634 / 1.159 | — | — | — | — | — |
| EventRowUpdate | — | — | 0.026 / 0.066 / 0.071 | — | — | — | — | — |
| RouteMarkerRender | 0.524 / 0.947 / 1.929 | 0.252 / 0.354 / 1.325 | — | — | — | — | — | — |
| SpeedChartRender | 0.004 / 0.008 / 0.013 | 7.152 / 10.801 / 19.497 | — | — | — | — | — | — |
| SafetyChartRender | — | — | 0.293 / 0.440 / 0.468 | — | — | — | — | — |
| TimeDistanceRender | — | — | — | 50.765 / 58.923 / 79.999 | — | — | — | — |
| TimetableUpdate | — | — | — | — | 0.509 / 0.882 / 1.884 | — | — | — |
| SegmentStatistics | — | — | — | — | — | 1.018 / 1.384 / 1.423 | — | — |
| ResourceOccupancy | — | — | — | — | — | — | 0.027 / 0.070 / 0.097 | — |
| V1V2Comparison | — | — | — | — | — | — | — | 0.569 / 0.746 / 0.751 |

### Allocation, GC and measured worker/UI duty

Memory values are non-forced-GC end snapshots, not peaks; allocation is process-wide during the interval,
including Engine, UI, diagnostics and harness work. GC pauses are not directly traced; allocation does not establish
that GC caused any particular stall. The worker/UI execute concurrently: these measured duty ratios are not an
exclusive end-to-end breakdown and exclude much framework/layout/composition and coordinator overhead.

| Tab | Alloc MiB/s | End managed / working MiB | Gen0/1/2 | Productive advance count / ms | No-progress calls / measured ms | UI apply ms / wall % |
|---|---:|---:|---:|---:|---:|---:|
| Route | 151.38 | 75.98 / 260.71 | 96/2/0 | 5998 / 1203.71 | 46730068 / 1271.38 | 183.64 / 1.82% |
| Speed | 61.45 | 88.27 / 258.18 | 37/5/1 | 6017 / 993.50 | 60743978 / 1153.46 | 335.94 / 3.33% |
| Safety | 53.89 | 80.67 / 268.04 | 34/3/0 | 6003 / 1023.10 | 59996553 / 1147.90 | 62.60 / 0.62% |
| TimeDistance | 127.49 | 87.92 / 308.66 | 60/23/13 | 6028 / 1096.92 | 59338437 / 1143.32 | 1902.04 / 18.79% |
| Timetable | 52.92 | 91.18 / 303.23 | 34/8/0 | 5991 / 1019.56 | 60064804 / 1146.95 | 33.94 / 0.34% |
| Segment | 54.15 | 104.15 / 323.36 | 34/7/0 | 6044 / 1045.03 | 60411674 / 1151.83 | 29.44 / 0.29% |
| Resource | 52.61 | 100.62 / 313.33 | 34/6/0 | 6058 / 996.94 | 61289117 / 1159.36 | 23.30 / 0.23% |
| Comparison | 52.87 | 108.15 / 323.14 | 34/9/0 | 6042 / 1024.79 | 61149310 / 1153.72 | 24.99 / 0.25% |

### Interpretation / primary bottleneck

**60× PRIMARY BOTTLENECK: MIXED — CHARTS / WPF refresh preparation and FRAME PIPELINE coordinator polling.**
There is no measured failure to sustain the requested simulation throughput: effective rates are
59.57–59.69× including pause/drain.

- Slowest tab/component: **TimeDistance / DrawTimeDistanceDiagram**, 1882.03 ms total,
  p50/p95/max 50.765 / 58.923 / 79.999. It accounts for 98.95% of that tab's
  synchronous UI apply time and 18.60% of measured wall time.
  UI unique apply FPS drops to 17.79, with 126 skipped frames and 108.44 ms max input gap.
- Speed chart costs 258.47 ms total, p95 10.80 ms;
  other table/resource/comparison paths are much smaller in this first ~600 s observation window.
- Productive Engine advances use about 9.82–11.96% of wall time.
  The existing coordinator also makes **47–61 million no-progress calls per ~10 s**; measured inner-call time alone
  is roughly 1.15–1.27 s, comparable to productive advance. Outer-loop/coordinator/Stopwatch/diagnostic overhead
  is not fully attributed. This is a future pacing investigation, not justification for skipping fixed ticks.
- Frame assembly/publish and accumulator have low measured totals relative to time-distance refresh. Allocation
  and GC are nonzero; no GC causality claim or CPU utilization/ETW/GPU trace was collected.
- These early-run tab profiles do not prove dense/late-run 8,000 s desktop responsiveness, DPI/resize/dragging,
  later O04/O13/O20 visuals, or native composition speed. Complete Engine facility parity is independently tested.

### Hidden-tab audit (behavior preserved)

Exact call path: `PlaybackTimer_TickCore → UpdateV2PlaybackView → selectedTab == SimulationTabItem`
invokes both `DrawV2Route` (33 ms) and `DrawV2SpeedProfile` (250 ms), without checking
`SimulationViewTabControl.SelectedItem`. Therefore SpeedChartRender executes while Route is visible, and
RouteMarkerRender executes while Speed is visible. TrainRowUpdate also runs on all workspace tabs, even
when the train rows are hidden. It costs little in this sample, but is explicitly not counted as visible work.
Other measured expensive workspace paths are correctly gated to their own selected tab; absent paths are marked
unmeasurable rather than claiming no cost anywhere else in the UI. Summary/filter/framework layout work is not
separately instrumented. Recommendations: gate nested route/speed refresh by the actual nested tab and investigate
bounded incremental time-distance drawing. Neither optimization was included in this measurement round.

### Frame-drop parity and regression

The initial enhanced resource probe rejected its empty set at ~660 s. Current source-backed actual O04 events
are 689.1 s request / 720.5 s completion, not the older 624.5/676.3 values. The corrected independent probe starts
at 680 s, deliberately blocks UI consumption for one wall second, and finishes at **740.1 s with 32 skipped frames**.
Normal continuous consumption advances to 743.4 s (different wall-clock outcome, not falsely compared at unequal times).
At the identical 740.1 s target, deterministic normal replay and slow-UI output have structurally identical **events,
retained trajectory, retained safety history, current safety, final trains and nonempty resource occupancy analysis**.
All frame histories are persistent; presentation skips do not discard Engine ticks or accumulator data. This is
resource-analysis parity, not resource UI visual acceptance. Full-retention Engine parity is a separate regression gate.

### Reproduce

```powershell
& './tests/MrtRouteSimulator.PlaybackBenchmarks/bin/Release/net10.0/MrtRouteSimulator.PlaybackBenchmarks.exe' --sample 'samples/大型機場線-完整營運示範範例.mrtsim.json' --duration 8000 --mode indexed-only --diagnostics
& './tests/MrtRouteSimulator.WpfTests/bin/Release/net10.0-windows/MrtRouteSimulator.WpfTests.exe' --profile-playback-only 'samples/大型機場線-完整營運示範範例.mrtsim.json'
# Optional independent tab or corrected parity-only probe:
# add --profile-tab=TimeDistance or --profile-tab=Parity
```

SDK: 10.0.401. All timing values are machine-local observations, not timing assertions. Final productionization
recommendation remains **NEEDS MORE DATA** (Engine whole-world median +3.60%, not >=5% GO); App default oracle
and all fixed-step/physical truth/safety/rear-clear/resource semantics remain unchanged.

## Final delivery / validation manifest

- Branch `codex/nearest-leader-index-prototype`; HEAD/base `558e06b534f97c997f450379f747e7709528ac17`.
  Commit SHAs: none created. No push/tag/merge/release. Dirty prototype was retained with explicit user permission.
- Engine changes: `SimulationWorld.cs`, `SimulationWorldPerformanceDiagnostics.cs`; the previously added
  candidate index and shadow mismatch types remain. No topology/schema/physical authority changes.
- Presentation changes: `PlaybackPerformanceDiagnostics.cs`, `SimulationPlaybackWorker.cs`,
  `SimulationResultAccumulator.cs`, `MainWindow.PlaybackRefresh.cs`. Diagnostics default off; the only
  user-observable repair is the separately authorized child-selection event guard (avoids Safety recursion).
- Harness/tests: PlaybackBenchmarks `Program.cs`; Engine `NearestLeaderShadowTests.cs` and `Program.cs`;
  WPF `PlaybackProfileTests.cs` and `Program.cs`. Existing boundary tests from the prototype remain.
- Documentation: this report, `NEAREST_LEADER_INDEX_PROTOTYPE.md`, `QA_REPORT.md`,
  `INDEXED_ONLY_BENCHMARK_RAW.json`, `PLAYBACK_PROFILE_RAW.json`.
- Release solution build: **0 warnings / 0 errors**. Full Engine runner: **186/186 passed, 0 failed**.
  Final `git diff --check`: **PASS**.
  Full automated/offscreen WPF runner: **PASS**, including diagnostics default-off/reset/percentiles,
  child selection event boundary, visual rules, project loading, worker, long journey, CSV/PNG/PDF.
  Eight tab timing samples completed; corrected focused nonempty-resource parity: **PASS**.
- Native desktop DPI/resize/tab-drag/8,000 s continuous playback and O04/O13/O20 visual acceptance:
  **not completed**. Automated gates do not substitute for native acceptance.
- Safe next task: isolated coordinator pacing + nested-tab/time-distance refresh optimization study,
  preserving every 0.1 s physics tick, followed by native desktop/late-run profiling. Index productionization
  still requires >=5% repeatable whole-engine benefit and broader workloads; do not enable it by default now.

## Coordinator pacing + nested-tab lazy + time-distance incremental rendering (2026-09-30)

### Scope and correctness boundaries

Continued the explicitly permitted dirty worktree on `codex/nearest-leader-index-prototype`, base/HEAD `558e06b534f97c997f450379f747e7709528ac17`. `git fetch origin` succeeded. No commit/push/tag/merge/release/reset/clean/stash. Prior prototype edits retained; this round changes presentation/coordinator/tests only. Production leader lookup stays **oracle; NEEDS MORE DATA**.

Root cause and implementation:

- Previous loop repeatedly called `AdvanceTo` before a complete tick was due. New loop: **ordered command → execute/publish → if playing, due fixed ticks (bounded catch-up slice 0.5 s) → cadence publish → wait until next 0.1 s tick deadline OR reliable command semaphore OR cancellation**. No abandoned channel waiter, no dropped commands, no dropped physics ticks. Small timeout rounds up to 1 ms; extremely low valid rates respect the semaphore timeout ceiling. UI frames remain capacity-one/latest-wins.
- Nested Route/Speed selected item now chooses exactly one expensive renderer. Selection change immediately refreshes even while paused; child selector bubbling is ignored. Train rows remain cheaply updated rather than adding broader gating.
- Original diagram cleared/recreated Canvas, rescanned bounds/filter/group, decimated every history and allocated new visuals. Incremental display cache consumes only the immutable trajectory tail; planned artifact is processed once per identity. It groups by VehicleId/ServiceRunId/direction and updates only changed series. Static axes/station grid/legend are reused while layout/filter/project/range is unchanged; event visuals update only when events/layout change.
- Per-series **600 ordinary-point** budget uses online hierarchical stride thinning of the bounded set. First/last, station/phase/track/speed-limit/acceleration-regime transitions and local extrema are pinned. This is a **soft total budget**, not a claim of strict 600 total points: required critical points can exceed it. Long monotonic 20,000-point tests retain old/middle/tail coverage. In this run max ordinary=550; actual max display=730/critical=179, planned max display=1,673/critical=1,218 (197,454 planned source points). Many critical transitions can still grow geometry; this limitation is explicit.
- Diagram cadence remains 250 ms (~4 Hz); same data/layout is a no-op; hidden automatic resize/filter handlers do not redraw. Explicit setup/test/export draws are exceptions. Actual-only automatic time axis uses stable forward 600 s buckets (visible blank future range); planned overlay retains its full range. Explicit time filters still apply. This display-only range choice does not change Engine/output times.
- PNG/PDF explicitly invoke the **unchanged full-history rendering/decimation algorithm**, then restore interactive visuals. CSV, planned/actual source lists, retention, Engine safety/resource ordering, schema and sample are unchanged.

### Before / after (TimeDistance unless labeled)

Same Release/default-oracle/28-station sample, eight sequential tab resets, same profiler measurement boundary (until >=10 s wall OR >=600 s sim), no concurrent performance work. Rolling timing samples remain aggregate diagnostics, not ETW. Raw data: [before](PLAYBACK_PROFILE_RAW.json), [after + subphase](PLAYBACK_PACING_PROFILE_RAW.json). Single before/after runs are not repeatability confidence intervals.

| Metric | Before | After | Delta |
|---|---:|---:|---:|
| Effective simulation × | 59.67 | 59.61 | -0.06 (-0.10%) |
| No-progress calls (measured ~10 s) | 59338437.00 | 0.00 | -59338437.00 (-100.00%) |
| No-progress inner elapsed ms | 1143.32 | 0.00 | -1143.32 (-100.00%) |
| Worker productive advance ms | 1096.92 | 1403.73 | 306.81 (27.97%) |
| Route unique UI FPS | 21.26 | 21.19 | -0.07 (-0.33%) |
| Speed unique UI FPS | 21.23 | 21.14 | -0.09 (-0.42%) |
| TimeDistance unique UI FPS | 17.79 | 21.23 | 3.44 (19.36%) |
| TimeDistance P50 ms | 50.76 | 0.64 | -50.12 (-98.73%) |
| TimeDistance P95 ms | 58.92 | 1.12 | -57.80 (-98.09%) |
| TimeDistance Max ms | 80.00 | 12.88 | -67.12 (-83.90%) |
| TimeDistance max input gap ms | 108.44 | 41.68 | -66.76 (-61.56%) |
| TimeDistance skipped presentation frames | 126.00 | 85.00 | -41.00 (-32.54%) |
| Allocation MiB/s | 127.49 | 54.70 | -72.79 (-57.10%) |
| Managed MiB | 87.92 | 63.41 | -24.51 (-27.88%) |
| Working set MiB | 308.66 | 261.20 | -47.46 (-15.38%) |
| Gen0/1/2 collections | 60/23/13 | 35/7/0 | -25/-16/-13 |

P95 reduction **98.09%**; <16 ms gate PASS. Unique apply FPS remains <25 because the existing UI timer/consumption architecture is unchanged; deliberately ~4 Hz diagram cadence uses alternative responsiveness gate: max input gap **41.68 ms <50 ms**, PASS. Not a claim of native responsiveness.

### All eight tabs, hidden nested work and measured duty

| Tab | Effective × | Publish / unique apply FPS | Skipped | Max input gap ms | Visible render p95 ms | Productive ms / wall duty | No-progress count | Process CPU ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Route | 59.62 | 29.60 / 21.19 | 85 | 64.16 | 1.068 | 1580.64 / 15.65% | 0 | 5281.25 |
| Speed | 59.65 | 29.50 / 21.14 | 85 | 42.98 | 11.224 | 1450.18 / 14.26% | 0 | 3250.00 |
| Safety | 59.68 | 29.63 / 21.28 | 84 | 41.14 | 0.431 | 1426.53 / 14.19% | 0 | 3750.00 |
| TimeDistance | 59.61 | 29.58 / 21.23 | 85 | 41.68 | 1.124 | 1403.73 / 13.80% | 0 | 2890.63 |
| Timetable | 59.62 | 29.57 / 21.29 | 84 | 42.13 | 0.830 | 1434.21 / 14.14% | 0 | 3000.00 |
| Segment | 59.63 | 29.52 / 21.23 | 84 | 41.19 | 1.409 | 1434.20 / 14.16% | 0 | 2640.63 |
| Resource | 59.59 | 29.63 / 21.26 | 85 | 42.38 | 0.092 | 1403.98 / 13.82% | 0 | 2234.38 |
| Comparison | 59.67 | 29.61 / 21.27 | 84 | 42.10 | 0.711 | 1422.73 / 14.14% | 0 | 2781.25 |

All eight no-progress counts are **0**, versus ~47–61 million before: **100% observed reduction**, zero measured no-progress inner time. Omitted counter/timing in raw JSON means no calls were recorded, verified by rate tests. Productive inner elapsed time increased (TimeDistance 1,096.92→1,403.73 ms) despite similar effective rate; cannot attribute this to scheduler/GC without dedicated evidence. Process CPU is newly measured summed user+kernel CPU across all threads, not normalized % and no equivalent before exists (do not claim CPU-percent reduction). TimeDistance process CPU=2,890.625 ms/10.175 s (~28.4% of one CPU-equivalent); this differs from productive elapsed duty.

| Nested visible view | Before Route / Speed calls | After Route / Speed calls |
|---|---:|---:|
| Route | 214 / 36 | 214 / 0 |
| Speed | 214 / 36 | 0 / 37 |

Hidden expensive nested work=0. Switching paused tabs immediately invokes only selected view; TrainRows invokes neither. Forced setup/explicit render/resize/export may run out of normal timer path; those are not hidden timer refresh. TrainRowUpdate persists (TimeDistance total 6.63 ms/10.175 s), intentionally not restructured.

### Full-render subphase and incremental costs

Separate diagnostic: same large sample paused at600 s, five full renders, not a direct substitute for the earlier streaming 10 s profile. Largest aggregate measured phase is decimation (281.40 ms), then filter/group (109.59), series visuals (48.51), bounds (45.48), static visuals (9.17), events (4.23). Decimation is ~56.5% of this instrumented phase sum; filter/group ~22.0%. No GPU bottleneck inference. Interactive measured p95: incremental data0.531 ms, reused series0.397 ms, event updates0.163 ms. No static rebuild recorded in the steady streaming window after reset/setup.

### Correctness gates and remaining work

- Rate1/10/30/60x, short quantized150 ms observation: 0.7/9.9/29.6/59.3x; all fixed-tick monotonic output matches fresh same-target reference JSON (events/trajectory/safety/final trains). The 1x0.7 result reflects one0.1 s tick/0.15 s window, not a sustained1x slowdown. Pause0.1–0.4 ms, reset0.5 ms, stop/dispose0.1 ms in successful focused run; values are test-run observations, not worst-case SLA. Reliable semaphore wakes timed/paused waits. Pause/resume, running rate-change/reset/stop and disposed-command rejection pass; existing reload/window/session cleanup tests remain included. Internal fallback cancellation is not directly injected by a dedicated test.
- Pure cache batch/incremental parity, critical boundaries/extrema/acceleration regimes, bounded ordinary geometry, no-op cursor and reset PASS.
- Automated visual-object tests: static reuse; changed zoom/filter/size invalidation; reset/new generation; same-window full reference first/last XY, station grid and event marker counts/tooltips; retained sources unchanged PASS. This verifies selected semantic invariants, not pixel-exact/native acceptance.
- Slow-UI probe **680→740.5 s, 31 skipped frames**, full same-target events/trajectory/safety/current safety/final trains/nonempty resource analysis JSON equal. Continuous UI consumption observed740.9 s; canonical equality reference is deterministic `AdvanceTo`, as in prior methodology. Frames can drop; physical ticks do not.
- Final gates: full solution Release build (2026-10-01) **0 warnings/0 errors**; focused WPF PASS; full Engine runner (2026-09-30) **186/186 passed, 0 failed**; full automated/offscreen WPF suite (2026-10-01 rerun) **PASS, exit0**, including all sample loading, worker cleanup, long journeys and CSV/PNG/PDF. Original interrupted WPF session was unretrievable, so partial output was not treated as success. Final `git diff --check`: PASS.
- Native Windows desktop resize/drag/DPI/60x visual acceptance **NOT COMPLETED**. Offscreen automated measurements are not native acceptance.
- After optimization, **Speed** has largest selected-render p95 (11.224 ms); frame assembly and train rows remain small. Route first-run max input gap64.16 ms is still an isolated tail outside the TimeDistance gate; no all-tabs<50 ms claim. Next safe study: repeat/cold+late-run measurements and native acceptance, then speed-series incremental sampling or UI timer/consume cadence, without modifying physical truth or productionizing leader index.

### Changed-file manifest (this round only)

`src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs`, `MainWindow.PlaybackRefresh.cs`, `MainWindow.xaml`, `MainWindow.V2.cs`; new `MainWindow.TimeDistance.cs`, `TimeDistanceTrajectoryCache.cs`. WPF tests: `Program.cs`, `OutputTests.cs`, `PlaybackProfileTests.cs`; new `PlaybackPacingTests.cs`, `NestedPlaybackTabTests.cs`, `TimeDistanceCacheTests.cs`, `TimeDistanceVisualTests.cs`. Documentation: this section, QA_REPORT, PLAYBACK_PACING_PROGRESS, PLAYBACK_PACING_PROFILE_RAW. Prior Engine/index/accumulator edits remain dirty but were not expanded this round.

Reproduce (from repository root):

```powershell
dotnet build MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- --pacing-lazy-only .
dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- --profile-full-timedistance-only samples/大型機場線-完整營運示範範例.mrtsim.json
dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- --profile-playback-only samples/大型機場線-完整營運示範範例.mrtsim.json
```

## Native desktop follow-up — 2026-10-01

See `docs/NATIVE_DESKTOP_ACCEPTANCE.md` for separate, timestamped native evidence. Three actual Windows Release-window 60x runs completed the large28station sample (clocks 7,368.6 / 7,368.4 / 7,368.7 s), all with 41,693 trajectory and 11,170 safety observations. Run3 continuous start-to-mid/late/near-completion samples measured approximately 59.49 / 59.77 / 59.86x. These do not prove exact full-run throughput for all three repeats; stop observation was sparse, and Run2 intentionally paused.

All eight requested native pages were visited while playback advanced; Speed pause/resume and maximize/restore worked. TimeDistance adjacent station-label overlap reproduced in Run2 and Run3. Sampled process WS peak 376.9 MiB; Run3 late/end flattened, but across-reset growth remains unresolved. No native p95/input-gap/GC/allocation/hidden-render aggregate exists in this report: ordinary App diagnostics are disabled by default, and automation latency is not app input latency.

Native visual gate **FAIL**, overall native acceptance **NOT COMPLETED**; full long-run gate **NOT COMPLETED**; DPI/resize **NOT COMPLETED**. Scaling was not changed after Settings launch approval timed out. O04/O13/O20 continuous physical-event visual gates remain pending; chart plateaus and prior automated evidence do not substitute. This round changes zero code files and does not optimize Speed, alter physical truth, or productionize nearest-leader index. Existing offscreen metrics above remain regression/profiler evidence only.

Final fresh regression on 2026-10-01: Release solution build 0 warnings/0 errors; Engine 186/186 PASS; full WPF PASS WPF visual rules; git diff --check PASS. Detailed commands and limits are in the native report.

### Approved native measurement follow-up — 2026-10-01

The user approved an opt-in App-only native measurement entrypoint and regression tests. No Engine/physics/default-index/label-layout changes. See `NATIVE_DESKTOP_ACCEPTANCE.md` for scoped file manifest, archived JSONL/hashes, native environment and full limitations. Default stays off; background bounded queue flushes wall-time checkpoints and terminal events, restores diagnostics, and automatically stops after three completed runs.

Three fresh native continuous 60x completions (Speed; TimeDistance after initial Speed; Route) measured conservative Play-dispatch-to-completed-frame-publication rates **59.99899 / 59.99814 / 59.99708x**. All have41,693 trajectory,11,170 safety,620 events and zero active trains; event identity sequences and corresponding timestamps match (max delta0s). This defines a full-run >=58x throughput sub-gate PASS, not exact final Engine tick timing or a same-page repeat benchmark.

Latest4096 rolling Apply p95 at completion6.049/3.256/2.805ms. Selected render early/mid/late p95: Speed15.675/9.090/8.145ms; TimeDistance13.353(at20s)/2.508/1.875ms; RouteMarker0.436/0.463/0.427ms. Run2 hidden Speed renders remain45 after switch; no Route marker timings in runs1/2 and no Speed/TimeDistance timings in run3. Full static-route layout and compositor FPS are not measured by marker timing.

Input-priority 20ms timer gap maxima56.654/344.428/54.667ms; not actual action latency, and no >100ms occurrence count, so responsiveness gates remain unresolved. Final WS312.58/332.52/342.41MiB; sampled heap peak91.45MiB. Allocation approximately96.1/58.0/100.1MiB/s; GC totals1961/355/6. Same-process Ui/Worker snapshots must not be summed. No late throughput collapse, but retained-history/cross-run bounded-memory claim remains unproven. Allocation/GC churn, early chart rendering and native input tail remain measured concerns.

Native TimeDistance label overlap reproduced, hence visual FAIL. Overall native/long-run acceptance remains NOT COMPLETED; physical O04/O13/O20 motion, actual input actions and real Settings100/125/150% DPI/resize matrix remain missing. App-reported WPF96DPI alone does not complete that matrix. Fresh follow-up Release0warnings0errors, Engine186/186PASS, native-entry regressionPASS and fullWPF PASS WPF visual rules; offscreen remains regression only. No commit/push/tag/merge/release.

# 2026-10-01 interactive input update

See `NATIVE_PLAYBACK_FINAL_PROGRESS.md` Native100% partial matrix for current native session6e3c7b8b. Owner App update marker102.07ms / first Diagram tab209.49ms; resume23.65/36.89ms; pauses19.23–32.52ms. Too few samples for acceptance p95. Running probe max86.88ms does not cover paused209ms tab switch. No repeated-memory or uninterrupted60x completion this session; overall NOT COMPLETED.

Follow-up App-only DPI/paused viewport fix regression PASS (Release0warnings/0errors, Engine186/186, fullWPF). New native sessione03f7a93 verifies120 DPI/1.25x, frozen14s marker target and paused Diagram max/restore redraw. This short interactive run is not throughput/memory evidence; old cold-tab209ms remains historical investigation evidence, not silently relabeled PASS.

Native150% continuation: fresh144/1.5 DPI; Diagram resume32.1624ms / pause30.6768ms, paused marker41.956ms owner App callbacks (not exact p95/compositor). Speed narrow/short chart clipping observed; layout FAIL is not evidence requiring Speed performance optimization. No complete memory/sanity run; overall NOT COMPLETED. Full details in final progress report.

