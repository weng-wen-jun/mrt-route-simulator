# Playback pacing / nested lazy / incremental diagram — execution log

## Final native preparation / handoff — 2026-10-01

User requested finishing presentation/diagnostic fixes and automatic regressions, then ending this turn. Native page/DPI operations wait for the user to return. Generic measured station-label collision layout is shared by incremental/full drawing; grid/trajectory coordinates remain unchanged. App-only opt-in input/stall/memory instrumentation supports five normal complete runs with 10-second completion idle and cache/GC checkpoints, without automatic playback or forced GC.

See [final execution log](NATIVE_PLAYBACK_FINAL_PROGRESS.md) for fresh tests and deferred gates. Layout scale/size proxies are not native OS DPI acceptance. Native overall remains NOT COMPLETED, memory UNRESOLVED, leader NEEDS MORE DATA/default oracle. Historical throughput results are preserved, not newly remeasured. No publication or unrelated dirty-file cleanup.

Date: 2026-09-30. Branch: `codex/nearest-leader-index-prototype`.
Base: `558e06b534f97c997f450379f747e7709528ac17`.

## Implementation stage (validation pending)

- Verified existing dirty worktree matches preceding prototype/profiler work; fetched origin. No reset, stash, clean or publication.
- Worker waits on the next full 0.1 s tick deadline or reliable semaphore command wakeup. Commands remain ordered/unbounded; frames remain latest-frame-wins.
- Nested Route/Speed selection gates expensive render calls; selection change forces immediate refresh with routed-event source guards.
- Presentation cache processes immutable trajectory tails only, keeps per-series ordinary points within 600 using hierarchical stride thinning, pins critical transitions/extrema/first/last. Critical points may exceed the nominal budget rather than silently discard operational boundaries.
- Interactive diagram reuses axes/grid, polylines/labels and markers. Actual-only automatic horizontal range uses 600 s forward buckets. Planned overlay keeps its complete planned range. This is display-only; explicit filters and exports remain separate.
- PNG/PDF use the preceding full-history renderer and decimation; CSV/Engine sources unchanged.
- Three scoped low-cost subagents supplied worker/tests, nested-tab/tests and pure presentation cache/tests. Main agent reviews and integrates; no result is marked passed before execution.

Pending: focused tests; full-render subphase measurement; same eight-tab 60x profile; slow-UI parity; Release build; full Engine/WPF regression; final before/after report. Native desktop acceptance NOT COMPLETED.

## Validation and profiling stage — 2026-09-30

- App/solution compile repairs: nullable assertion in streaming cache; missing IO namespace/Path alias in new visual test. No model change.
- Test harness corrections: time0 Reset can legitimately contain initial samples/events (compare fresh time0 reference); faulted Task exception unwrap; render invocation counts are timing-summary Counts, not arbitrary counters; visible-layout resize setup. Failed attempts were not reported as passes.
- Focused pacing/nested/cache/visual tests PASS. Rate1/10/30/60x observed0.7/9.9/29.6/59.3x in short quantized window; no-progress0; pause0.1–0.4 ms, reset0.5 ms, stop0.1 ms.
- Separate paused600 s/five-call original renderer breakdown completed: decimation largest aggregate281.40 ms, filter/group109.59 ms. Not GPU evidence and not an apples-to-apples streaming timing substitute.
- Eight-tab60x same-method profile PASS. All no-progress0; effective59.59–59.68x. TimeDistance p95 58.923→1.1245 ms; gap108.44→41.68 ms; unique UI FPS17.79→21.23; skips126→85. Hidden Route/Speed calls0.
- Slow-UI parity PASS:680→740.5 s,31 skipped presentation frames; events/trajectory/safety/trains/nonempty resource analysis equal. Continuous consumption observed740.9 s.
- Full Engine runner PASS:186/186,0failed. No Engine edits in this round.
- Raw JSON and full before/after report saved. Full WPF started, but usage-limit approval-review failure interrupted final documentation update; no bypass attempted.

## Resume and final gate — 2026-10-01

- Original WPF terminal session was no longer retrievable; did not infer final success from its partial output.
- Rechecked branch/HEAD and dirty status: unchanged branch/base; prior edits preserved, no publication.
- Rebuilt solution Release:0warnings/0errors. Re-ran full WPF suite:PASS,exit0, including added pacing/nested/cache/visual cases, samples/load failures, worker cleanup, long journey and CSV/PNG/PDF.
- Corrected before nested render counts from raw JSON:214/36 (not214/38) for both Route and Speed profiles.
- Updated QA_REPORT and final profiler report. Native desktop acceptance remains NOT COMPLETED; leader index remains NEEDS MORE DATA/default oracle.
- Automatic validation complete. Worktree deliberately left dirty/uncommitted; remaining native acceptance is not replaced by offscreen tests.
