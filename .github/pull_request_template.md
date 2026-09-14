## Summary

<!-- What problem does this PR solve? Keep this concise and outcome-focused. -->

## Scope

<!-- List the main files/modules changed and why. -->

## Architecture / model impact

- [ ] No change to V4 topology/runtime contract.
- [ ] No change to Schema 8 / legacy Schema 7 compatibility behavior.
- [ ] No change to V2 physical truth (`TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex`).
- [ ] No change to `VehicleTypes + ServiceTypes + StopPatterns + Dispatch` as the execution data source.
- [ ] No new Engine → WPF dependency.
- [ ] No UI-side reimplementation of Engine physics, safety, moving block, timetable, or actual trajectory truth.

If any item above is intentionally changed, explain the new contract and update `MODEL_SPEC.md` in this PR.

## Validation

### Build

- [ ] `dotnet build MrtRouteSimulator.slnx -c Release`
- Result: <!-- e.g. 0 warnings / 0 errors -->

### Engine runner

- [ ] `dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release`
- Result: <!-- e.g. N/N passed -->

### WPF runner

- [ ] `dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release`
- Result: <!-- PASS / not applicable / reason -->

### Additional validation

- [ ] Regression test added or updated for the changed boundary condition.
- [ ] Relevant `.mrtsim.json` samples load / round-trip / build a topology-native `SimulationWorld` as applicable.
- [ ] Station construction validation run if track ports, directed connections, station/facility wizard, or route construction changed.
- [ ] CSV / PNG / PDF export checked if output code changed.
- [ ] Desktop/manual validation completed when visual layout, playback, DPI, window size, or interactive WPF behavior changed.

Manual validation performed:

<!-- State exact scenarios, window sizes/DPI, timestamps, samples, exports, or explicitly say not performed. -->

## Documentation / delivery state

- [ ] `MODEL_SPEC.md` updated if runtime/model/schema contract changed.
- [ ] `AGENTS.md` / `HANDOFF.md` updated if responsibility boundaries or developer workflow changed.
- [ ] `QA_REPORT.md` updated if this PR changes the current verification baseline or manual QA status.
- [ ] `TODO.md` updated if a current pending item was completed, changed, or added.
- [ ] `CHANGELOG.md` updated when appropriate.

## Known limits / follow-up

<!-- Do not describe unverified work as complete. Call out remaining DPI, desktop playback, PDF pagination, legacy migration, or other limitations explicitly. -->

## Reviewer checklist

- [ ] Changes are scoped to the stated problem.
- [ ] No unrelated refactor or schema change is bundled in.
- [ ] V2 runtime remains topology-native unless the PR explicitly changes and documents that contract.
- [ ] Source, tests, `QA_REPORT.md`, and `TODO.md` do not contradict each other.
- [ ] Validation claims match the evidence actually produced.
