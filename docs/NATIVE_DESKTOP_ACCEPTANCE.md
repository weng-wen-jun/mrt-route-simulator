# Native desktop / long-run acceptance

## Current integration checkpoint — 2026-10-06

This section supersedes the preparation/build status below; earlier measurements remain historical, not merged across binaries or configurations. Current workspace is `D:/AI/codex/mrt-v403-integration`, branch `codex/v403-playback-integration`, baseline `68084cbd39d32b02fab9f8718d29b4ef5d5fb88f`, product V4.0.3. See [current raw-evidence ledger](NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md) and `QA_REPORT.md`.

- Release build: 0 warnings/errors; Engine 198/198; complete WPF runner and separate no-Show synchronous-export/managed-retention runners exit0.
- Native same-process sample14 Diagram five-run session `9c989122` at measured 120 DPI: each completes7172.9s,1025 full-field events match the current baseline, effective active-wall rate59.99988754–60.00110921. All five normal Reset checkpoints are recorded before collector stop. This session predates the reset-start flag addition.
- Separate post-reset-start-fix session `85312aec`: three complete runs with identical full events. Third normal Reset starts4.564s after completion; no false10s idle checkpoint is written through later collector Stop. Early-reset sub-gate PASS; this is not a replacement five-run memory protocol.
- Five-run reset private memory236.46→272.05MiB remains UNRESOLVED. Test-only forced-GC weak references prove selected old managed frame/history/actual-cache roots collectible after normal Reset and collector Stop while the window/worker/planned graph remain rooted; this does not prove native/WPF memory boundedness.
- New sessions contain0 raw >100ms DispatcherTimer gaps, but the older matched Diagram session has127, max1820.6542ms. Covered timing scopes do not explain that gap. Neither GC causality nor OS input/compositor latency is established; native input overall is not promoted to PASS.
- Final official binary: native A3/A4 save-dialog paper titles match the selection; both cancelled without file writes. Independent vertical zoom100%→200% leaves horizontal100%; collapsed events expand the chart, and time-axis/end label remain visible after scrolling to the bottom. These are scoped visual observations, not a new full DPI matrix.
- OS100%/150% latest matrix still requires manual scaling followed by actual WPF DPI telemetry. Existing event/resize evidence is not reclassified by these gates. Native playback overall remains NOT COMPLETED; leader default remains oracle / NEEDS MORE DATA. No Engine/Schema/sample or publication changes.

### Evening update — latest sticky-scrollbar binary

- Separate measured96DPI sample14/Diagram session `d70a3e60` completes five same-process60× runs without Pause. Each1025 full-field events equals the9c baseline; active-wall rates59.99693298–60.00276976 use frame-publication boundaries. Five valid10s idle/normal Reset checkpoints precede Stop. Actual cache clears each time; raw dispatcherStall0 applies only to this session, not prior Route stalls.
- Reset private memory240.70→270.53MiB remains UNRESOLVED. Run3 already has the initial frame (trajectory0/safety0/event1) and8 planned polylines/120 visuals after Reset; other reset snapshots have the blank placeholder. This is not retained actual history, nor proof of native memory boundedness. Direct owner play/reset n5 p95/max80.1657/64.6809ms still exceeds the50ms recommendation.
- User manually resized to800×520 DIP at measured96DPI. Scoped fixed-axis/end-label, independent scroll and Route proxy observations are recorded. Expanded events make the chart viewport too short; the user requested a larger minimum chart viewport and collapsible top controls. Presentation repair and regression/native recheck are pending. Full DPI/resize and native input gates remain NOT COMPLETED.

## Baseline correction — 2026-10-01

The records below were collected on the V4.0.2-derived worktree, not the current V4.0.3 integration. They remain historical evidence only. The isolated integration at `D:/AI/codex/mrt-v403-integration` uses baseline `68084cb`; Engine regression is198/198 PASS but WPF regression is still unresolved. Native acceptance remains paused until the integration gate passes. See [integration progress](V403_INTEGRATION_PROGRESS.md). Do not reuse old vehicle IDs or event timestamps as current sample evidence.

## Final task preparation / user handoff — 2026-10-01

The user requested finishing code fixes and regression checks, then ending this turn; native page/scaling operations are deferred. See [final execution log](NATIVE_PLAYBACK_FINAL_PROGRESS.md). This section supersedes only preparation status, not the archived measurements below.

- Authorized presentation fix uses measured station-label boxes, stable anchor ordering, boundary-aware forward/backward packing, fitted font and tagged leader lines. Both incremental and full/export renderers preserve actual station grid Y and trajectory geometry. No station-specific exceptions.
- Opt-in input recording distinguishes earliest WPF routed receipt/preview, explicit normal owner handler boundaries, application visual apply, routed-settled proxies and HwndSource move/resize receipt. None is hardware injection/compositor timing or automation round-trip. Generic proxy callbacks are labeled as such. >100ms playing-probe gaps carry UTC, page, simulation clock, recent action, memory and GC counters; temporal association is not GC causality.
- Normal Play/Pause/Reset menu commands reuse existing handlers so later five-run TimeDistance measurement can remain on the same page. No automatic playback/reset, Engine change or forced GC. Maximum five completed runs; final stop follows its 10-second idle checkpoint. Named memory checkpoints include beforePlay, afterReset and nextPlay; cache/history/process/GC/visual counts accompany them.
- New native runs/screenshots this preparation phase: **0**. Input percentiles/spike counts, same-page memory trends, corrected-label native result, O04/O13/O20 visual evidence, DPI100/125/150%, resize/hit-test and one post-fix 60× sanity run are **NOT COMPLETED**. Memory remains **UNRESOLVED**; overall native playback remains **NOT COMPLETED**. Historical visual FAIL is retained pending actual recheck.
- User agreed to manual scaling when notified on a later turn. No DPI changed. Settings did not expose a controllable window; no capture dimensions are used to infer scaling. Automated target-size/scale tests are layout proxies, not native OS scaling acceptance.
- Fresh final solution Release build:0warnings/0errors; Engine186/186; final-preparation專項 and rebuilt complete WPF PASS (exit0). [Final progress log](NATIVE_PLAYBACK_FINAL_PROGRESS.md) records scoped files and synthetic offscreen raw input/stall/memory JSONL, which is not native interaction evidence. Engine/sample hashes unchanged. No publication; default leader mode remains oracle / NEEDS MORE DATA.

Date: 2026-10-01. Status: PARTIAL ACCEPTANCE RECORDED; full gates not completed.

- Branch: `codex/nearest-leader-index-prototype`; HEAD/base `558e06b534f97c997f450379f747e7709528ac17` plus existing uncommitted playback/index prototypes.
- User explicitly approved preserving/excluding the new station-zoom TODO edit. No production code modification authorized by this acceptance task; fixes require separate consent.
- `git fetch origin` completed. Release solution build: 0 warnings / 0 errors.
- Computer-use skill used for actual Windows app selection, launch, native screenshots and UI input; not an offscreen runner. Target executable: `src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe`.
- Actual title reports V4.0.2 (repository build), not an installed/released version inference.
- Sample: `samples/大型機場線-完整營運示範範例.mrtsim.json`.
- Native window launched and visible; file dialog path entry verified by screenshot and accessibility value. Some dialog element indexes/focus metadata were inconsistent; used refreshed screenshots and keyboard focus instead. Tool handling latency is not app input latency.
- Native desktop evidence is being collected independently from the prior automated/offscreen results. No simultaneous build/Engine benchmark during playback.

Three native 60x runs completed. Remaining gates are listed below. Missing metrics are N/A with reasons, never copied from offscreen as native evidence.

## Timestamped native observations (Taipei UTC+8)

- Run 1 started 10:03:58.948; completion later observed at simulation 7,368.6 s. Exact stop wall time not captured; effective full-run rate N/A. Final trajectory 41,693 / safety observations 11,170.
- Run 2 completed, simulation 7,368.4 s; completion observed 10:09:38.381, 168.689 s after initial Play, including an intentional pause and delayed observation. This is NOT an active-playback elapsed measurement. Final counts again 41,693 / 11,170.
- Run 2 early Speed at 540.5 s: UI-reported effective 59.2x. Pause at 1,088.1 s: two separate observations retained the same clock; resume advanced correctly. Mid Speed at 1,620.5 s: 59.4x; late at 6,761.5 s: 59.9x. These are rolling UI status readings, not full-run throughput statistics.
- Run 2 switched Route, Speed, Safety, TimeDistance, Timetable, Segment, Comparison, Resource while playing; tables/charts displayed current data without a persistent observed freeze. Safety selected pair had no recent samples and explicitly explained that condition; current pair rows were populated. Native input latency p50/p95/max cannot be derived from automation call duration.
- Maximize changed captured window from 1782x1116 to 1920x1140; restore returned to 1782x1116. Speed geometry and Resource table remained visible. Attempted bottom-right manual resize did not change dimensions; narrow/short resize is NOT verified.
- TimeDistance screenshots at 1,088.1 s and approximately 2,659 s show adjacent station labels overlapping vertically (including O08/O08a/O09, O15/O15a/O16). This is a reproduced native visual issue at current scaling, not proof of cache degradation. No production fix made.
- Process working set / private bytes: post-Run1 323,289,088 / 222,814,208; Run2 mid at 10:08:10.848 351,555,584 / 262,410,240; completion at 10:09:39.906 383,963,136 / 300,236,800. Process private bytes are NOT managed heap. Sampled peak so far 366.2 MiB; growth across resets requires further observation, not a bounded-memory PASS.

## Environment and measurement limits

Windows 11 Pro x64 build 26200; AMD Ryzen 9 8940HX (16 cores / 32 logical processors); 31.2 GB RAM; NVIDIA GeForce RTX 5060 Laptop GPU plus AMD Radeon 610M and Oray virtual display. WMI GPU memory fields are not authoritative VRAM capacity. Native screenshots reached 1920x1140; this is window capture size, not independently verified monitor resolution. Actual scaling has not yet been verified.

The normal App exposes rolling effective rate and last-frame timing in status/tooltip, but not enabled aggregate diagnostics. Native allocation, managed heap, GC totals, publish/apply FPS, render p50/p95/max, max input gap and hidden-render counters remain N/A. Existing opt-in profiler is harness-only by default; no new instrumentation was added. Prior offscreen measurements remain separate regression evidence.

## Repeatability and early/mid/late results

| Run | Effective x | Completion | Max input gap | Working set sampled peak | Gen2 | Visual issue |
|---|---|---|---|---|---|---|
| 1 | N/A: exact stop time missed | Yes, clock 7,368.6 s | N/A | 308.3 MiB (post-run sample only) | N/A | Route observed; event motion not captured |
| 2 | Rolling early/mid/late 59.2 / 59.4 / 59.9; full-run N/A (pause) | Yes, 7,368.4 s | N/A | 366.2 MiB | N/A | TimeDistance adjacent station-label overlap |
| 3 | Measured start-to-active sample: 59.49 / 59.77 / 59.86; full-run stop time bracket only | Yes, 7,368.7 s | N/A | 376.9 MiB | N/A | TimeDistance overlap reproduced; native Speed return curves visible |

Run 3 Play dispatch timestamp 10:10:52.714. No pause during Run 3. Conservative wall intervals include input dispatch and accessibility overhead:

| Stage | Observation time | Simulation clock | Wall since dispatch | Calculated x | UI rolling x |
|---|---|---:|---:|---:|---:|
| Early Route | 10:11:12.070 | 1,092.1 s | 19.356 s | 56.42 (startup-inclusive short window) | 60.3 |
| Mid Route | 10:11:54.638 | 3,683.6 s | 61.924 s | 59.49 | 60.7 |
| Late Speed | 10:12:26.933 | 5,631.9 s | 94.219 s | 59.77 | 60.2 |
| Near completion | 10:12:49.545 | 6,993.4 s | 116.831 s | 59.86 | 61.0 |
| Completion observed | 10:13:07.776 | 7,368.7 s | 135.062 s | Not an exact stop measurement | Stopped |

Actual Run 3 completion wall time is within (116.831, 135.062] s, so a full-run >=58x claim is not proven by that bracket. Rolling status can exceed 60x over short snapshots; it is not a full-run aggregate. All three final trajectory/safety counts agree (41,693 / 11,170); this is a limited parity check, not event-by-event or state-hash equality.

Run 3 process samples: 10:11:42.381 WS/private 393,084,928 / 306,941,952; 10:12:28.384 395,190,272 / 313,221,120; 10:13:27.038 after completion 394,465,280 / 312,270,848 bytes. Late/completion WS flattened around 376 MiB in this run, but the across-reset rise (Run1 308 -> Run2 366 -> Run3 377 MiB) is unresolved. No forced GC. Do not call it a proven leak or a bounded-memory PASS.

## Event visual gates

| Event | Engine observed | Native visual observed | Result | Note |
|---|---|---|---|---|
| O04 passing | Prior integrated automated evidence; updated request/completion approximately 689.1/720.5 s | Static physical passing branches; event motion missed | NOT COMPLETED | 60x event is too brief for these sparse snapshots |
| O13 passing | Prior integrated automated evidence | Resource aggregate visible, not entry/overtake/merge motion | NOT COMPLETED | Historical old exact event seconds not reused |
| O20 pocket entry | Prior integrated automated evidence | TimeDistance turnback plateau, not physical pocket marker entry | NOT COMPLETED | Chart does not prove edge-local marker correctness |
| O20 reverse | Prior integrated automated evidence | Speed chart contains direction transition and return curve | NOT COMPLETED | VehicleId/ServiceRun transition not inspected at event boundary |
| Return mainline | Prior integrated automated evidence | Late native up-direction marker / return charts | NOT COMPLETED | Continuous merge and platform alignment not captured |
| Resource release | Prior integrated automated evidence | Resource aggregate at 4,423.2/5,040.5/5,482.3 s populated | NOT COMPLETED | Aggregate durations do not prove active-lock release |

Native event CSV was not exported; no current exact event-time authority is inferred from old QA numbers. Sample uses synthetic scenario values, not real-world airport infrastructure truth.

## DPI and interaction gates

| DPI | Route | Speed | TimeDistance | Labels | Hit test | Result |
|---|---|---|---|---|---|---|
| 100% | N/A | N/A | N/A | N/A | N/A | NOT COMPLETED |
| 125% | N/A | N/A | N/A | N/A | N/A | NOT COMPLETED |
| 150% | N/A | N/A | N/A | N/A | N/A | NOT COMPLETED |

Scaling could not be verified: Settings was not in the installed-app inventory; launch of the verified `C:\Windows\ImmersiveControlPanel\SystemSettings.exe` via computer-use timed out waiting for App approval. No scaling was changed, and no approval bypass attempted. Capture pixel dimensions alone do not identify DPI. No DPI-specific PASS/FAIL assigned to the observed overlap.

| Action | Result | Max delay / symptom | Result gate |
|---|---|---|---|
| Maximize | Actual window dimensions changed; Speed chart visible | Precise latency N/A | PASS functional only |
| Restore | Original dimensions restored; Resource visible | Precise latency N/A | PASS functional only |
| Narrow / short resize | Drag attempted; dimensions unchanged | Native MinWidth=1180 / MinHeight=720; no successful smaller size verified | NOT COMPLETED |
| Wide / tall manual resize | Only maximize covered | N/A | NOT COMPLETED |
| Drag window | Action accepted; playback subsequently advanced 1,092.1 -> 1,944.4 s | No before/after origin pair; smooth motion / delay unmeasured | NOT COMPLETED full gate |
| Route -> Speed | Early/mid/late chart visible | No persistent freeze observed; sub-50 ms unmeasured | PASS functional only |
| Speed -> TimeDistance | Chart populated | Adjacent station-label overlap at multiple phases/runs | FAIL visual |
| Timetable / Segment / Resource / Comparison / Safety | Populated native pages | No sustained freeze observed; chart/table freshness at every update not proven | PASS switching only |
| Train click / scroll / rapid tab switching | Not performed with current evidence | N/A | NOT COMPLETED |
| Pause/resume Speed | Clock stable at 1,088.1 s on two observations; resume advanced | Command-boundary ticks / latency distribution unmeasured | PASS functional only |
| Pause/resume Route / TimeDistance | Not tested | N/A | NOT COMPLETED |

No claim of hidden-tab redraw leakage = 0, native p95 <<16 ms, most input <50 ms, or absence of repeated >100 ms stalls. General UI has no aggregate counters for those gates; screenshot/tool wall duration cannot substitute.

## Decision and follow-up boundary

- NATIVE PLAYBACK: **FAIL visual gate / overall acceptance NOT COMPLETED**. TimeDistance station labels overlap reproducibly; no sustained playback freeze observed.
- LONG-RUN 60x: **NOT COMPLETED for full acceptance**, despite three successful completions and Run3 mid/late active intervals >=58x. Full-run rate repeatability, allocation/GC, bounded-memory and input-stall thresholds lack measurements. NOT COMPLETED is deliberately used instead of falsely classifying missing data as a measured FAIL or PASS.
- DPI/RESIZE: **NOT COMPLETED**. Maximize/restore functionally passed; scaling matrix and manual size variants remain missing.
- Nearest-leader index: **NEEDS MORE DATA**, production default **oracle**, unchanged.
- Next bounded work: approve/enable a native aggregate diagnostics entrypoint using the existing opt-in profiler (no Engine change), then collect three exact-stop runs and current event CSV timestamps. Inspect each physical event at slower playback / paused boundaries, then repeat 60x interaction and DPI gates. Diagnose station-label spacing separately; any production fix still requires explicit consent under this task.
- This acceptance round changes **zero code files**. Its docs: this new report, `QA_REPORT.md`, `docs/PLAYBACK_END_TO_END_PROFILE.md`. Earlier dirty files (including TODO) preserved. No commit/push/tag/merge/release.

## Final regression (2026-10-01)

- `dotnet build MrtRouteSimulator.slnx -c Release --no-restore`: exit 0, 0 warnings / 0 errors.
- Full Engine runner (`-c Release --no-build --no-restore`): exit 0, **186/186 PASS**, 0 failures. Includes current large-sample O04/O13/O20 and full-retention oracle parity; this is automated Engine evidence, not native physical-marker acceptance.
- Full WPF runner (same flags, no selective switch): exit 0, **PASS WPF visual rules**; includes pacing/lazy/incremental, sample loading, worker command/parity, speed and CSV/PNG/PDF output tests. This is offscreen regression, not native acceptance.
- `git -c safe.directory=D:/AI/codex/mrt-route-simulator diff --check`: exit 0; rechecked after report updates.
- No production or test code changed in this acceptance round. Branch/base remain as stated; no publishing action performed. Native completed window remains open for follow-up.

## Approved native measurement entrypoint follow-up (2026-10-01)

The user explicitly approved adding a native measurement entrypoint after the partial report above. The previous zero-code-change statement describes the initial acceptance round only, not this follow-up. Scope: opt-in App diagnostics UI and regression tests, existing profiler reused; Engine, fixed tick, leader default, sample, playback semantics and label layout remain unchanged. No publishing is authorized.

### Implementation and validation

Implemented an opt-in `原生驗收量測` menu. Start only arms a session; normal Play drives the existing worker. The measurement is disabled by default, samples every 10 active wall seconds, stops after three completed runs, and restores diagnostics switches. JSON serialization/write/flush runs on a background bounded queue (256 records); I/O failures stop measurement, not simulation. Pause/resume, generation replacement, close and duplicate-stop have regression coverage. No Engine, sample, physics, leader-default or station-label changes were made in this follow-up.

Follow-up changed-file manifest: new `src/MrtRouteSimulator.App/MainWindow.NativeAcceptance.cs`, new `tests/MrtRouteSimulator.WpfTests/NativeAcceptanceTests.cs`; hooks/menu in `MainWindow.xaml.cs`, `MainWindow.xaml`, and WPF test `Program.cs`; this report, `QA_REPORT.md`, `PLAYBACK_END_TO_END_PROFILE.md`, and generated raw evidence below. Prior dirty changes including TODO are preserved and excluded from this manifest. Branch/base remain `codex/nearest-leader-index-prototype` / `558e06b534f97c997f450379f747e7709528ac17`.

Fresh follow-up regression: Release solution build exit 0, 0 warnings/0 errors; full Engine runner 186/186 PASS; `--native-acceptance-only .` PASS; full WPF runner exit 0 / PASS WPF visual rules (including the new suite). Tests are offscreen regression, not native acceptance. Native runs were performed after the test process completed, with no concurrent builds/tests. No publishing performed.

### Instrumented native runs (2026-10-01, approximately 11:04–11:12 Taiwan time)

Same real Windows Release process, same large sample and oracle default; three continuous normal 60x plays, no pause. Run1 visible Speed; Run2 starts on Speed and switches to TimeDistance after the first 10 s checkpoint; Run3 visible Route. They test different visible-page loads, not a same-page repeat benchmark. No UI controls were driven by the measurement entrypoint.

Raw archived files: [metrics](native-acceptance-raw/native-acceptance-20261001-030446228-e982fc5eb8a14ed4903f06f072a45862.jsonl), [events](native-acceptance-raw/native-acceptance-20261001-030446228-e982fc5eb8a14ed4903f06f072a45862.events.jsonl).
SHA256 respectively `CDD976CD70DB3F2280FB59C1F94765F1B4362654DD66E426B56949EE0497F102` and `CA70BC2C989DD3A199179B0D02715DBA71EA2A2699E7FE3435FBFF5B090F83C3`.

| Instrumented run / visible page | Completion clock s | Conservative wall s | Effective multiplier | Apply p95 ms | Probe gap p95 / max ms | Allocation MiB | GC 0/1/2 | Final WS / heap MiB |
|---|---:|---:|---:|---:|---:|---:|---|---:|
| 1 Speed | 7368.7 | 122.813739 | 59.99899x | 6.049 | 41.330 / 56.654 | 11797.22 | 741/50/3 | 312.58 / 66.60 |
| 2 TimeDistance, initial Speed | 7368.5 | 122.812138 | 59.99814x | 3.256 | 39.033 / 344.428 | 7119.05 | 447/104/2 | 332.52 / 81.70 |
| 3 Route | 7368.5 | 122.814308 | 59.99708x | 2.805 | 36.651 / 54.667 | 12294.03 | 773/201/1 | 342.41 / 82.45 |

Wall boundary is normal Play dispatch through the completed worker frame publication timestamp, excluding delayed UI observation. Publication timestamp is captured near publication, **not the exact final Engine tick** (`throughputExact=false` in raw data). This provides a conservative defined-boundary full-run measurement; all three exceed 58x. Completion clock variations are idle ticks; all three have 41,693 trajectory / 11,170 safety samples, zero active trains, and 620 events. Event sequence comparison using EventType/VehicleId/ServiceRunId/TrackEdgeId/PlatformId/ResourceId matches across all three; corresponding event timestamps have max delta **0 s**, last event 7368.4 s. This is repeat event parity, not byte-for-byte comparison of every event field or a separate oracle replay.

### Early / mid / late evidence

Timing p95 uses the existing latest-4096 reservoir. Counts/total/max are cumulative from run start; the phase p95 is **not an independent 10 s interval p95**. Checkpoints use active wall time, not simulated time. Runtime Ui and Worker fields are two snapshots of the **same process** and must not be added. Allocation is cumulative process allocation including telemetry, not retained heap or leak size.

| Run / phase | Active wall s | Simulation s | Cumulative effective x | Apply p95 ms | Selected render p95 ms | WS / heap MiB |
|---|---:|---:|---:|---:|---:|---:|
| 1 early | 10.006 | 597.8 | 59.957 | 9.226 | Speed 15.675 | 305.88 / 63.47 |
| 1 mid | 60.110 | 3602.2 | 59.993 | 4.545 | Speed 9.090 | 316.73 / 86.53 |
| 1 late | 110.194 | 6607.2 | 59.996 | 5.899 | Speed 8.145 | 311.64 / 66.30 |
| 2 early after tab switch | 20.022 | 1197.6 | 59.997 | 6.851 | TimeDistance 13.353 | 331.76 / 69.30 |
| 2 mid | 60.099 | 3603.2 | 60.000 | 2.623 | TimeDistance 2.508 | 327.25 / 82.23 |
| 2 late | 110.154 | 6606.3 | 59.999 | 3.263 | TimeDistance 1.875 | 320.46 / 70.42 |
| 3 early | 10.011 | 599.6 | 60.001 | 0.684 | RouteMarker 0.436 | 331.82 / 87.41 |
| 3 mid | 60.122 | 3605.0 | 60.000 | 2.184 | RouteMarker 0.463 | 336.70 / 78.12 |
| 3 late | 110.217 | 6611.3 | 60.000 | 2.919 | RouteMarker 0.427 | 341.15 / 84.88 |

Run2 first 10 s checkpoint was still Speed (35 renders); by 20 s Speed count reached45 and remained45 through completion, while TimeDistance count increased26 ->391. No RouteMarkerRender in runs1/2, no SpeedChartRender or TimeDistanceRender in run3. This supports hidden nested selected-chart refresh suppression for these runs; it is not an audit of every layout/visual path. Run3 Route metric is marker rendering, **not full static-track reconstruction**. Worker published / UI applied / dropped counts: 2986/2591/394, 2776/2519/256, 3030/2606/423; one final pending-frame count difference is not a skipped physics tick. UI apply rates approximately21.10/20.51/21.22 Hz, not screen-present FPS.

10 s sampled WS peaks322.26/336.29/342.07 MiB, sampled heap peaks91.45/88.41/87.94 MiB. Including terminal measurements, max observed WS342.41 MiB. Run1 and run2 late WS fall relative to some earlier checkpoints; run3 rises331.82 ->336.70 ->341.15 ->342.41 MiB. Across runs final WS312.58 ->332.52 ->342.41 MiB. GC totals1961/355/6 across three runs; allocation rates approximately96.1/58.0/100.1 MiB/s. These three short wall-time cycles do **not** prove bounded long-term memory or a leak; retain full-history growth and GC/allocator churn as follow-up concerns. No late-run throughput collapse was measured.

InputStall measures intervals of an Input-priority 20 ms DispatcherTimer, not actual clicks, mouse-motion latency or compositor latency. It includes timer granularity. Max observed gap344.428 ms on run2 (which contains a tab switch); no per-gap timestamps or >100 ms occurrence count exist, so neither exact cause nor repeated-stall absence is established. Even runs1/3 maxima exceed50 ms; **all input <50 ms is not claimed**. Render p95 near16 ms in early Speed and13.35 ms in early TimeDistance is not universally "much less than16 ms"; Apply p95 is lower. Do not replace missing responsiveness gates with throughput PASS.

### Native environment and remaining decisions

Environment remains Windows11Pro build26200 / Ryzen9 8940HX /31.2 GiB RAM / RTX5060LaptopGPU plus Radeon610M and virtual-display driver, as previously inventoried. App telemetry reports WPF DPI96x96, scale1x1, window1440x900 DIP; MonitorFromWindow reports non-primary `\\\\.\\DISPLAY1`, bounds(1920,-124)-(3456,836), **1536x960 reported monitor pixels**, work area(1920,-124)-(3456,788). Primary SystemParameters work area1920x1032 DIP is a different monitor and must not be substituted. Capture dimensions1782x1116 differ from App DIP/pixel telemetry; OS Settings scaling was not independently confirmed. Thus WPF96-DPI evidence exists, but the requested100/125/150% Settings matrix is still **NOT COMPLETED**.

Native screenshots show Speed early/mid/completed return curves; TimeDistance early/mid/completed data, with station-label overlap reproduced; Route markers mid/completed and empty at all-trains-exited completion. At 60x sparse observations still do not prove continuous O04/O13/O20 physical marker movement, entry/reverse/mainline return or resource release. Existing Engine tests and repeated event parity do not substitute for those visual gates. No new resize/drag/train-click/pause/resume matrix was performed; earlier partial functional results above remain applicable only to their recorded actions.

- **Full-run >=58x throughput sub-gate: PASS for the three instrumented continuous runs and stated publication boundary.**
- **NATIVE PLAYBACK: visual gate FAIL; overall NOT COMPLETED.** Label overlap remains; physical-event and actual input gates remain incomplete.
- **LONG-RUN 60x: throughput/completion/repeat-event sub-gates measured successfully; overall NOT COMPLETED**, because bounded-memory, repeated-input-stall and full interaction gates remain unresolved.
- **DPI/RESIZE: NOT COMPLETED**; no125/150% claims.
- Nearest-leader **NEEDS MORE DATA**, production **oracle** unchanged. Dominant measured concerns are allocation/GC churn, early selected-chart rendering and the344 ms native Dispatcher probe tail, not demonstrated Engine throughput failure.
- Next actions: diagnose/fix station-label overlap only with explicit authorization; collect actual input action timings and >100 ms gap occurrence timestamps, repeat same-page longer memory runs, then slow/paused O04/O13/O20 visual boundaries and the real Settings DPI/resize matrix. Do not optimize or alter Engine physical truth from these partial metrics.

The terminal `sessionStopped.reason` is currently the generic `manual-stop` label even for automatic three-run stop; native status and completedRuns=3 confirm the automatic stop. It must not be interpreted as a user pause or manual interruption. Raw evidence is preserved without relabeling.

Final follow-up `git -c safe.directory=D:/AI/codex/mrt-route-simulator diff --check`: PASS (exit0) after all report updates; no commit/push/tag/merge/release. The completed native window remains open, with measurement stopped.
# 2026-10-01 interactive100% update

Partial native evidence is recorded in `NATIVE_PLAYBACK_FINAL_PROGRESS.md`, section Native100% partial matrix: confirmed96 DPI/1x, paused FULL-O13 hit-test, separated28 TimeDistance labels and reachable top/bottom at1187x720 DIP. Cold Diagram tab owner-update209.49ms and paused-resize stale plot width remain open; not overall PASS.125/150%, complete repeat-memory/event visual/sanity runs remain pending.

User125% handoff later yielded fresh WPF96x96/1x checkpoints at20:46:43 and20:46:53 Taipei, not expected120/1.25.125% gate remains NOT COMPLETED, pending App DPI-awareness investigation; screenshot enlargement alone is not native DPI proof. App remains paused. See progress report for evidence and proposed scoped follow-up.

Authorized follow-up implemented explicit per-monitor V2 App manifest and coalesced viewport-change refresh (including initial Auto/NaN width guard). Final Release/Engine186/186/fullWPF PASS. New native sessione03f7a93 now confirms120/1.25 at startup and playback checkpoint; paused FULL-O13 hit-test and Diagram max/restore refresh verified. Full per-page resize/DPI matrix and overall remain NOT COMPLETED; details in final progress report.

150% handoff: fresh native checkpoint confirms144 DPI/1.5x. Diagram28-label scroll scan, paused narrow/short redraw and FULL-O13 hit-test positive. **Speed narrow/short chart/time-axis is clipped without scroll access: native resize visual FAIL.** Overall NOT COMPLETED, App paused52.6s pending permission for minimal Speed layout-only repair; see final progress report and native150% evidence.

