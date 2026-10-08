# Native playback final acceptance — execution log

2026-10-01. Task: `外部檔案參考/CODEX_TASK_native_playback_final_acceptance.md`.
Branch `codex/nearest-leader-index-prototype`, HEAD/base `558e06b534f97c997f450379f747e7709528ac17`.

## Preflight

- `fetch origin` completed; branch/HEAD and dirty-file inventory match the preceding recorded work. Existing prototypes, playback changes and the separately approved TODO edit are preserved. No commit/push/merge/tag/release/reset/stash/clean.
- Prior native full-run throughput sub-gate PASS is retained, not re-proved unnecessarily. Native visual/input/memory/event/DPI overall gates are not promoted from missing evidence.
- This task authorizes presentation label layout, App-only opt-in input/memory diagnostics and WPF regression tests. Engine/sample/Schema/physical truth/default oracle are excluded.
- Scoped low-cost agents: generic station-label layout + tests; native diagnostics extension; read-only current event observation planning. Root reviews/integrates and controls native UI using computer-use.

## Implementation and validation boundary

- Label fix implemented and automatic regression passed: no collision test previously existed for boxes at nearby true Y anchors. The helper measures WPF TextBlock metrics, fits font to the fixed sidebar, preserves anchor order, packs forward/backward at boundaries and uses tagged leaders. Grid/trajectory mapping and left82/top48 remain unchanged; no station-name exceptions. Incremental and full/export drawing share the helper.
- Input recorder/stall context and five-run same-page memory instrumentation implemented. Explicit normal owner hooks cover Play/Resume, Pause acknowledgement, Reset apply, guarded workspace/nested tabs and marker-to-Speed drawing. Generic receipt/settled/update surrogates remain separate from actual owner update timestamps. Each physical preview/wheel action gets a distinct token, while its owner reuses that token. Earliest App/WPF receipt is not OS injection; apply is not compositor present.
- Opt-in handlers/HwndSource hook are removed on Stop, with bounded action history and background JSONL queue. Normal playback menu actions allow later TimeDistance repeats without changing selected page. No automatic playback/reset or GC.Collect. Fifth completed run remains armed for its 10-second idle checkpoint.
- Fresh native inventory: previous MRT window is closed; no user app is force-closed. New Release executable was built, but not launched for new native acceptance in this deferred phase.
- App and final solution Release builds passed (0 warnings/0 errors). Engine runner passed186/186. `--native-final-only` passed label/input/memory/measurement lifecycle coverage. First complete WPF run passed; final post-review recheck follows below. Build-only import/missing test helper defects were corrected; no validator/threshold was weakened.

## User handoff boundary

- User requested finishing code fixes/tests first, then ending the turn. Native page operations, five-run memory protocol, slow event visual inspection and DPI matrix are deferred until the user returns.
- User agreed to manually change100/125/150% scaling when notified later. No scaling was changed. This turn's Settings launch returned `launched app did not expose a targetable window`; refreshed inventory had no Settings window. No workaround or permission bypass attempted.
- New executable is `src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe`. Supporting DLLs remain beside it; this is a local Release build, not a published package.

## Scoped files / evidence distinction

This phase changes App `MainWindow.TimeDistance.cs`, `TimeDistanceStationLabelLayout.cs`, `MainWindow.NativeAcceptance.cs`, `MainWindow.NativeAcceptanceInput.cs`, `MainWindow.PlaybackRefresh.cs`, `MainWindow.V2.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`; WPF `TimeDistanceStationLabelTests.cs`, `NativeAcceptanceInputTests.cs`, `Program.cs`; and the five acceptance/progress/QA Markdown reports. Existing Engine/prototype/worker/TODO edits remain unrelated pre-existing work and were not reset or included in a commit.

Label cases cover both dense station clusters, all actual28 outbound stops from the current sample, bounds/leader/tags, resize-target heights380/600 and scales1/1.25/1.5 plus synthetic stress/fallback. Existing incremental/full visual tests confirm trajectory/grid coordinates. These are **automated layout proxies**, not native maximize/restore or OS DPI evidence.

Engine `SimulationWorld.cs` and the large sample SHA256 match the pre-edit baselines exactly:

- Engine: `B622DEAF8D3BF54CBF045F67F1B4AE1C77C7887B558A1B674741372CF80A895A`
- Sample: `EB19348BB4E91AC1CD554B7EA23064755F701F729FE37B46624F90DCF545E65A`

## Deferred result matrix

| Field | Current new evidence / decision |
|---|---|
| Label algorithm / O08,O08a,O09 / O15,O15a,O16 / all28 | Automatic measured-layout checks PASS; native corrected-label decision NOT COMPLETED |
| Native tab/nested/marker/pause/resume/scroll p50,p95,max | N/A; no new native interactions collected |
| Native resize/move timing, >100ms occurrence count/context | N/A; recorder ready, test-only artificial stall is not native evidence |
| Same-page memory complete runs | 0 new native runs; run1–5 checkpoint values/trends N/A |
| Max WS/private bytes, heap/allocation/GC/display-cache trends | N/A for new native protocol; classification UNRESOLVED |
| DPI100/125/150%, maximize/restore/resize/hit-test | NOT COMPLETED; user returns later to operate scaling |
| O04/O13 visual; O20 entry/reverse/return/release | NOT COMPLETED; archived event plan below is not new visual evidence |
| Post-fix full60× sanity | NOT COMPLETED; prior native throughput sub-gates remain historical PASS |
| Native input / native playback overall | NOT COMPLETED |
| Nearest-leader | NEEDS MORE DATA; production default oracle |

Next action after the user returns: open this local Release build, recheck corrected labels, collect a same-page five-run memory session and real interactions, then slow event windows and independently verify WPF DPI after each manual scale change. Do not automatically continue before that return.

## Deferred visual observation plan (not new native acceptance evidence)

Read-only archived event comparison: previous three native runs each contain620 events and identical target sequences. These seconds are planning aids from the archived current sample, and must be revalidated against the next native session's event JSONL before assigning a visual PASS.

| Gate / vehicle | Slow observation window s | Archived event boundaries s |
|---|---:|---|
| O04 / AIRPORT-DIRECT-01 | 680–731 | request689.1, complete720.5, release725.1 |
| O13 / AIRPORT-DIRECT-01 | 1282–1341 | request1290.7, complete1330.2, release1334.8 |
| O20 pocket / AIRPORT-DIRECT-01 | 1765–1883 | entry1771, tail reached1812.3, return started1842.3, up-platform arrival1877.8, release1877.9 |
| O20 scheduled departure | 2595–2610 | DirectionChanged/Departure2600 |
| O04 / SECTION-VEHICLE-01 | 3184–3231 | request3189.1, complete3220.5, release3225.1 |
| O20 pocket / SECTION-VEHICLE-01 | 4583–4700 | entry4588, reached4629.3, return4659.3, arrival4694.8, release4694.9 |

AIRPORT-DIRECT keeps VehicleId while changing `AIRPORT-DIRECT-DOWN-01` to `AIRPORT-DIRECT-UP-01` at TailTrackReturnStarted; pocket edge `EDGE:POCKET-001`, return edge `EDGE:UP:O20:O19`, down/up platforms `PLATFORM:O20:DOWN` / `PLATFORM:O20:UP`. Passing resources `FACILITY:PASS-001` / `FACILITY:PASS-003`, pocket `FACILITY:POCKET-001`. A RouteReleased event can have null ResourceId, so retain ResourceIds/message and preceding reservation context rather than inferring “no resource” from null alone.

No screenshots or new event executions were collected in this deferred phase; all O04/O13/O20 visual gates remain NOT COMPLETED.

## Final automatic gate and handoff

- Final `dotnet build MrtRouteSimulator.slnx -c Release --no-restore`: PASS,0warnings/0errors.
- Full Engine runner: PASS186/186,0failed; source/sample baseline hashes unchanged.
- `--native-final-only`: PASS. Final rebuilt full WPF runner: PASS WPF visual rules,exit0; includes actual28 station projection layout, repeated-scroll token separation, input/memory schema, measurement lifecycle, baseline incremental/full coordinate comparison, all sample load/visual guards, worker/reset/long journey and CSV/PNG/PDF outputs.
- Synthetic offscreen raw report: [input/stall/memory JSONL](native-acceptance-raw/offscreen-final-preparation-20261001-input-memory.jsonl). The >100ms probe timestamp is artificially set by the test and actions are diagnostics-only; **never use its percentiles/occurrences as native acceptance evidence**. Existing archived native event planning evidence is not relabeled as this round's execution.
- Raw report SHA256: `D84948817A1522F3B076C3FB23FCCF8FD6AB022BAA69B4ABF0D149E50923C8CC`. Final `git diff --check` PASS; existing dirty changes were preserved.
- New local exe exists at the path above (last write2026-10-01 19:09:59 Taipei). No new native acceptance launch, no DPI/page operation after user deferral, no background continuation/automation. Worktree stays uncommitted. End this turn and await the user's return.

## Native resumption — 2026-10-01

- User returned with「我好了」; resumed the deferred native workflow. Dirty inventory matches recorded scope. No rebuild or source change, no publication.
- Launched exactly one Release MRT window from the recorded executable; title V4.0.2. Loaded `samples/大型機場線-完整營運示範範例.mrtsim.json` through the normal file dialog, and verified the current-file label and ready playback status. A dialog field index was unavailable and focus metadata inconsistent; refreshed screenshots showed the correct selected sample, then normal Enter opened it. No sample mutation.
- Armed native session `6e3c7b8be975465097ff5a895dcd83d3` through the normal menu, report `src/MrtRouteSimulator.App/bin/Release/net10.0-windows/NativeAcceptanceLogs/native-acceptance-20261001-114700867-6e3c7b8be975465097ff5a895dcd83d3.jsonl`. Still waiting for normal Play; no complete native run yet.
- Next user handoff: set the monitor containing MRT to100% scaling, report completion (or confirm it is already100%). Then read fresh WPF DPI telemetry and run the first matrix cell. Do not infer DPI from screenshot dimensions. All native gates still pending at this preparation point.

## Native 100% partial matrix — 2026-10-01, session 6e3c7b8b

- User confirmed monitor100%; fresh run/viewport telemetry independently reports96x96 DPI, scale1x1, monitor `\\.\DISPLAY1` (non-primary). No inference from screenshot size. Interactive run1 is paused at282.3s; zero complete runs. Pauses/rate boundaries make this unsuitable as uninterrupted60x sanity or same-page memory evidence.
- Route Pause held169.4s across observations. Per user guidance, paused before train selection. Clicking the stable marker selected VehicleId `FULL-O13` and opened its Speed view. Two earlier moving-marker attempts hit `Polyline` and are excluded from hit-test PASS; they do not establish an App hit-test bug.
- Speed Resume advanced169.4s to260.5s, then Pause remained stable through subsequent operations. Diagram Resume advanced260.5s to282.3s; Pause acknowledgement and a later screenshot confirmed stable paused status. Route/Speed/Diagram controls used normal production handlers, not Engine manipulation.
- Native owner-update timings (WPF receipt to App visual callback, not OS injection/compositor): marker102.0679ms (handler74.0788ms); nested Speed tab10.22ms; first Diagram tab209.49ms (handler208.52ms); resume23.6486ms /36.89ms; pause21.35ms /19.23ms /32.52ms. Samples are too few for acceptance percentiles. Cold Diagram timing aligns with `TimeDistance.IncrementalData` max187.2277ms; this is correlation, not proof of root cause. No GC causal claim.
- Running checkpoint probe has8948 samples, max86.8828ms and no recorded >100ms stall records yet. Probe excludes paused periods, including the209ms Diagram switch; therefore this does NOT prove absence of input stalls.
- TimeDistance native visual scan: all28 labels present; O08/O08a/O09 and O15/O15a/O16 separated in maximized/restored/narrow/short observations. Leader lines remain visible; no station-specific exception. At short height, only a subset is visible at once but scrollbar reaches both top(O26/title) and bottom(O01/axis), offsets0 and289.24. Narrow toolbar wraps and export controls remain reachable. This is screenshot evidence, not live bounding-box/trajectory-coordinate telemetry.
- Native resize succeeded via title-bar system menu Size then edge drag: restored narrow `Width=1187`, short `Height=720` DIP in subsequent checkpoint. Original restored1440x900 and maximize also exercised. Earlier edge drags had no effect and are not counted. Alt+Space opened PowerToys launcher; no text/command entered; title-bar context click dismissed it and opened the actual system menu.
- **Open presentation issue:** while paused, maximize left the TimeDistance plot at its previous narrower width even after later observation; normal Resume rebuilt it to full viewport width. Restore/resize also changed horizontal extent. Do not claim paused resize freshness/no geometry-jump gate PASS. Route/Speed full resize coverage remains pending.
- Evidence: `native-acceptance-evidence/20261001-final-100-max-diagram.png`; additional lossless captured PNG base64 sidecars `20261001-final-100-narrow-short-bottom.png.b64` (intermediate scroll, NOT actual bottom), `20261001-final-100-narrow-short-bottom-verified.png.b64`, `20261001-final-100-narrow-short-top.png.b64`, `20261001-final-100-max-after-resume-paused.png.b64`. Final paused capture remains282.3s. Sidecars are raw evidence, not generated mockups.
- Latest sampled memory at280.2s: working set343060480B, private249044992B, managed65536288B; display actual/planned series3/8, ordinary4822, critical5567, canvas144 children. Only an interactive early checkpoint: memory classification remains UNRESOLVED, no leak conclusion.
-100% label visual subchecks are provisional positive evidence; full100% DPI/resize gate NOT COMPLETED because of paused resize issue and missing per-page resize coverage.125/150%, five complete same-page runs, O04/O13/O20 visual gates and post-fix60x sanity remain NOT COMPLETED. Overall remains NOT COMPLETED; nearest-leader unchanged NEEDS MORE DATA/oracle.
- Next handoff: keep MRT paused; user changes the same monitor to125%, then Codex verifies fresh WPF DPI before continuing. Do not count previous96 DPI or screenshot dimensions as125% evidence.

## User125% handoff — actual WPF DPI mismatch, 2026-10-01

- User reported scaling change complete. Resumed normal Diagram playback briefly solely to obtain fresh viewport checkpoints, then paused using the normal menu. Existing session/process/sample unchanged; no source/build/restart or Engine manipulation.
- Fresh checkpoints at20:46:43 /20:46:53 Taipei, simulation330.1 /340.2s, both report **96x96 DPI, DpiScale1x1** on `\\.\DISPLAY1`. Earlier checkpoint at290.2s also96/1. These are fresh after the user handoff, not old100% readings. **125% native gate NOT COMPLETED: App did not report expected120/1.25.** Do not use the visibly enlarged capture as proof of App native125% layout.
- Source `NativeAcceptanceCaptureViewport()` reads `VisualTreeHelper.GetDpi(this)` directly. App csproj has no explicit `ApplicationManifest`; repository search found no explicit per-monitor DPI declarations. This is a suspected DPI-awareness/startup configuration issue, not a confirmed runtime root cause. No assertion that user chose the wrong monitor, and no claim of a fully verified OS125% setting.
- Paused status confirmed after normal Pause at20:46:58. No125% label/hit-test PASS assigned;150% remains pending. Interactive run still incomplete and unsuitable for five-run memory or uninterrupted60x sanity.
- Follow-up authority requested before changing global App DPI awareness (which goes beyond the station-label algorithm): allow a minimal App-only DPI configuration/paused-layout freshness investigation and necessary fix/tests; no Engine/sample/schema changes. Keep current OS scaling unchanged until that decision.

## Authorized App DPI / paused viewport follow-up — 2026-10-01

- User explicitly allowed the minimal App DPI and paused-layout fix. Manifest declares `PerMonitorV2, PerMonitor` plus `true/pm` fallback; csproj explicitly embeds it. Keeps `asInvoker`, no UI access/elevation, no process-level P/Invoke workaround or OS settings change. Microsoft recommends declarative manifests: [DPI awareness configuration](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process).
- Existing native window was absent on refreshed inventory; no running MRT process found. Old JSONL ended with `sessionStopped`, manual-stop, completedRuns0. Did not force-kill anything. Preserved old report/event files as `docs/native-acceptance-raw/final-interactive-100-dpi-mismatch-6e3c7b8b[.events].jsonl`; old partial evidence is not a completed protocol.
- Initial manifest build PASS,0warnings/0errors. Resize follow-up and final regression gates pending at this entry. Engine/sample SHA256 still match the original baselines. No Engine/sample/schema edits or publication.

### Integrated fix and fresh125% evidence

- Changed only App csproj/`app.manifest`, `MainWindow.TimeDistance.cs`, DiagramScrollViewer events in `MainWindow.xaml`, the full-render width guard in `MainWindow.V2.cs`, and WPF `DpiConfigurationTests.cs`, `TimeDistanceResizeTests.cs`, runner registration. Existing unrelated dirty work preserved.
- Resize root cause: Canvas-only SizeChanged could redraw before the parent viewport had its final dimensions, with no redraw after viewport change while paused. Added size/viewport-change events and one coalesced Render-priority callback; ignores offset-only ScrollChanged. Also fixed `Math.Abs(NaN-width)>1` always false when Canvas.Width initially Auto/NaN, in both incremental/full renderers. No station/grid truth changes, no simulation advancement to refresh layout.
- Subagent implementation reviewed/integrated by main agent. First integrated build found two missing test imports; corrected. First actual resize regression exposed NaN width; main agent fixed both renderers. Final solution Release build PASS0warnings/0errors; `--native-final-only` PASS; full WPF runner PASS WPF visual rules (includes same-frame viewport resize, scroll-no-rebuild, label mapping/series preservation and frozen30.0s clock). Full Engine186/186 PASS,0failed. `git diff --check` PASS; baseline Engine/sample hashes unchanged.
- New native process started from updated Release apphost, window398102. Native session `e03f7a93-918a-43da-9ee3-2c45397b242e`, log `native-acceptance-20261001-133724822-e03f7a93918a43da9ee32c45397b242e.jsonl`. **sessionStarted at21:37:24 Taipei confirms120x120 DPI /1.25x1.25**, same monitor `\\.\DISPLAY1`, now physical bounds1920,-124–3840,1076. Normal-play checkpoint at9.9s independently confirms120/1.25. Explicit DPI manifest fixed the observed WPF96/1 mismatch; no screenshot-size inference.
- Fresh normal UI showed the current large sample loaded and ready at0s; external/user activity happened between captures, so do not attribute its file-dialog/maximize operations to agent automation. Verified filename label before normal1x Play, then paused at14.0s. Paused marker click selected `FULL-O13`, Speed source/vehicle target matches. First coordinate attempt returned unknown screenshotId without confirmed input; refreshed and used supported current window coordinate click, which succeeded. Clock stayed14.0s.
- Native125% Diagram observations: both dense station clusters separated, leader mappings visible. Restored/maximized while paused and plot now redraws to the available viewport without Resume. Restored window initially partly off-screen; an attempted drag reported user input detected, then refreshed observation showed the full restored window; do not count unconfirmed drag as agent move evidence. Maximized again while still paused with full-width plot. Evidence `native-acceptance-evidence/20261001-final-fixed-125-max-diagram.png[.b64]`, `20261001-final-fixed-125-paused-remaximize.png[.b64]`.
- Scope completion: minimal DPI/paused-viewport fix implemented and regressions PASS. Native125% DPI/paused hit-test/max-restore subchecks positive. Full125% resize matrix (per-page narrow/short/tall/scroll endpoints), revised100% process validation and150% remain incomplete; no full DPI or native overall PASS. Five-run memory, O04/O13/O20 and60x sanity still pending. Keep App paused at14.0s and request150% handoff for next independent DPI cell; do not erase remaining125% obligations.

## Native150% partial matrix — 2026-10-01

- User confirmed150% switch. Same updated process/window398102 and interactive sessione03f7a93, current large sample verified. Fresh normal Diagram Resume checkpoint at21:48:09 Taipei, sim29.8s independently reports **144x144 DPI, DpiScale1.5x1.5** on `\\.\DISPLAY1`. Monitor physical bounds1920,-124–3840,1076. No DPI inference from image dimensions. Pause at21:48:31 acknowledged at52.6s; clock remained52.6s through later native operations.
- Diagram Resume receipt-to-owner App callback32.1624ms (handler3.4246ms); Pause30.6768ms (handler0.5576ms). Paused Simulation workspace tab1.8915ms, nested Route0.7166ms. These are individual samples, not exact p95 acceptance or compositor latency. Earlier post-fix first Diagram cold switch176.4085ms remains evidence, not relabeled fast.
- TimeDistance150% native scan: top(O26–O19), middle(O21–O11, including O15/O15a/O16), bottom(O10–O01, including O08/O08a/O09) visible via actual scrollbar; offsets0,103.50499 and240.90667. Both dense clusters separated and leaders visible. O01/time-axis endpoint reachable; viewport clipping at intermediate scroll positions is expected and does not remove the content. All28 label texts retained in accessibility tree. Screenshot evidence, not numeric native bounding-box or trajectory-coordinate parity.
- Maximize/restore and actual narrow/short native resizing completed in Diagram while paused. A direct corner drag had no effect and is excluded. Title-bar system menu Size→Right then right-edge drag visibly reduced width; Size→Down then bottom-edge drag visibly reduced height near the configured minimum. Numeric final bounds not freshly checkpointed yet; do not infer exact DIP sizes from captures. Toolbar wraps and export buttons stay reachable. Paused diagram updates fit new width, no Resume needed.
- Route narrow/short uses existing vertical/horizontal scrollbars; actual wheel moved vertical offset48 and revealed down-line marker. Paused marker at latest window-relative167,606 selected **FULL-O13**; Speed source/vehicle matches. Owner callback41.956ms (handler12.7598ms); nested Speed callback0.4368ms. Native150% paused hit-test subcheck PASS, no coordinate-offset symptom observed.
- **New native resize visual FAIL: Speed page at narrow/short size clips the lower chart/time-axis, with no enclosing scrollbar to reach it.** Current XAML SpeedCanvas has `MinHeight=220`, inside a star-sized Grid/Border without ScrollViewer. The V2 renderer uses canvas.ActualHeight; the parent viewport can be shorter, so the lower plot is unreachable. Screenshot `20261001-final-fixed-150-speed-hit-clipped.png[.b64]` shows the successful target selection AND the clipping; do not mark full150% UI/resize PASS. This is presentation only, not Speed performance/root-cause optimization evidence.
- Additional evidence `20261001-final-fixed-150-diagram-bottom.png[.b64]`, `20261001-final-fixed-150-diagram-middle.png[.b64]`, `20261001-final-fixed-150-narrow-short.png[.b64]`. All captured from native App, no mocks/image editing. Current App remains paused52.6s on Speed. New raw snapshot is partial, not completed or flushed-session memory/throughput evidence.
- No production/test changes in this150% observation turn. Last full Release/Engine186/186/WPF regressions remain the previous fix's results. Overall NOT COMPLETED; DPI/resize has observed Speed clipping FAIL, remaining100/125 per-page/tall gates uncompleted. Memory UNRESOLVED (zero complete runs); O04/O13/O20 and60x sanity pending; nearest-leader NEEDS MORE DATA/oracle unchanged.
- Next requires scoped approval: minimal App-only Speed small-window layout fix (fit chart to available viewport or make full chart scroll-reachable) and WPF regression; no changes to Speed data/aggregation/render optimization/Engine/sample. User need not change current150% until this is resolved.

### User-requested compact summary and persistent Route scrollbar — 2026-10-01

- User requested that Route's horizontal scrollbar remain visible whenever horizontal content overflows, independent of vertical scroll position; also requested that the upper route summary need not occupy small-window space. Implemented a default-expanded, manually collapsible `RouteSummaryExpander`, preserving all five existing summary values. Removed the summary row's 100-DIP minimum and Simulation's 300-DIP star-row minimum so the Route ScrollViewer is constrained to available space; existing Auto scrollbars remain attached to the viewport, not the canvas bottom. No Engine, sample, schema or playback-data changes in this follow-up.
- Initial Release build PASS, 0 warnings / 0 errors. Desktop inspection was interrupted by the user's physical Escape; stopped inputs immediately. User subsequently requested continuation. Focused WPF regression and fresh native validation are pending; do not treat the source fix or build as native acceptance, nor clear the prior Speed clipping FAIL yet.
- Continued native inspection of the new Release window with the large sample loaded (loading/resize activity occurred outside agent input; do not attribute those operations to automation). In the observed narrow/short window, expanded summary still leaves the horizontal Route scrollbar visible at vertical offset 0. Clicking the summary header hides the five cards and increases plot space; re-expanding restores the same five values. Simulation clock stays 0.0s throughout, no Play was invoked. Collapsed summary: actual vertical scroll 0 -> 48 -> 0 leaves the horizontal bar at the same visible viewport bottom; dragging the horizontal thumb changes offset 0 -> 1265.810238 and reveals the O12–O21 region. This is direct native evidence for the requested behavior, not a fresh numeric DPI or full per-page resize matrix.
- Native PNG evidence: `native-acceptance-evidence/20261001-compact-route-top.png`, `20261001-compact-route-bottom.png`, `20261001-compact-route-expanded.png` (original capture base64 sidecars retained).
- Engine full runner PASS 186/186, 0 failed. First full WPF run passed compact layout, sample load and worker gates, but failed the existing NativeAcceptanceInput test's `normal Play must leave the acceptance run playing for stall probe coverage` after its fixed 80ms wait. Immediate `--native-final-only` re-run PASS, including compact layout and input/stall checks; full re-run pending. No assertion weakened or production playback behavior changed to hide the failure. Engine/sample baseline SHA256 unchanged; diff check PASS.
- Subsequent full WPF re-run completed PASS WPF visual rules, exit 0, including sample layout/load, worker, native diagnostics, long journey, CSV/PNG/PDF. Kept the first failure above as a test reliability limitation. Current native App remains stopped at 0.0s, summary collapsed. The requested compact-summary / Route-horizontal-bar native subcheck is positive; prior Speed clipping FAIL and outstanding overall acceptance gates remain open.
- Main-agent review strengthened the delegated compact regression to inspect the actual `PART_HorizontalScrollBar` bounds inside viewer/window, fixed Y at vertical top/bottom in both expanded and collapsed states, re-expanded summary preservation and unchanged clock. Final solution Release build PASS 0 warnings / 0 errors; strengthened `--native-final-only` PASS (summary frees 99.7 DIP in the test layout). Full runner PASS above preceded this test-only strengthening; production XAML is unchanged since that full run. Integration briefly introduced duplicate Media import, corrected before final gates. No commit/push/tag/release.
> 2026-10-01 基底更正／新停點：以下既有紀錄屬原4.0.2工作樹，不能轉作4.0.3驗收。4.0.3在 `D:/AI/codex/mrt-v403-integration` 已完成本地來源整合、Release build、Engine198/198及完整WPF回歸。新版窗口1905336已載入sample14，clock0s，量測session7baa84d7292e4446b12aca3a4959f972待命、尚未Play；使用者要求明天繼續，已停止UI輸入。見[整合與停點紀錄](V403_INTEGRATION_PROGRESS.md)。沒有新native PASS、沒有背景自動續跑。

