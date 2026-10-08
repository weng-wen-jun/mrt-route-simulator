# Codex 任務：Native Windows 60× Long-Run Acceptance + Late-Run UI Validation

## 0. 任務目的

請延續目前 `mrt-route-simulator` 的 playback performance 工作，進入**原生 Windows 桌面驗收階段**。

前一輪已完成：

```text
Playback coordinator pacing 修正
Nested Route / Speed lazy refresh
Incremental / bounded Time-Distance rendering
```

目前自動化結果已證明：

```text
No-progress AdvanceTo：
47–61 million / ~10 s
→ 0

60× offscreen effective rate：
59.59–59.68×

TimeDistance render：
p50 50.765 → 0.644 ms
p95 58.923 → 1.1245 ms
max 79.999 → 12.877 ms

TimeDistance max input gap：
108.44 → 41.68 ms

TimeDistance allocation：
127.49 → 54.70 MiB/s

Nested hidden render：
Route visible / Speed render = 0
Speed visible / Route render = 0

Release build：
0 warnings / 0 errors

Engine：
186 / 186 PASS

Full offscreen WPF：
PASS
```

但目前仍缺：

```text
Native Windows desktop acceptance
long-run / late-run 60× playback
resize / drag / tab switching
different DPI / scaling
O04 / O13 / O20 visual acceptance
repeatability
```

本輪主要目標：

> 證明目前 playback 架構在真正 Windows 桌面、長時間 60× 播放與後段複雜事件下，仍能保持穩定、可操作、可視且不破壞既有 simulation correctness。

---

# 1. 先閱讀既有文件

請先讀：

```text
docs/PLAYBACK_END_TO_END_PROFILE.md
docs/PLAYBACK_PACING_PROGRESS.md
QA_REPORT.md
docs/NEAREST_LEADER_INDEX_PROTOTYPE.md
```

以 repository 實際內容為準。

本任務不重新解讀或重做前輪已完成的：

```text
coordinator pacing
nested lazy refresh
time-distance streaming cache
nearest-leader prototype
```

---

# 2. Git / Worktree 安全

目前已知：

```text
branch:
codex/nearest-leader-index-prototype

HEAD/base:
558e06b534f97c997f450379f747e7709528ac17
```

且工作樹仍可能保留未提交 prototype / profiler edits。

先執行：

```powershell
git fetch origin
git status
git branch --show-current
git rev-parse HEAD
```

如果 dirty changes 與既有 profiler / playback 文件記錄一致：

```text
允許繼續使用目前 worktree
```

但禁止：

```text
reset
clean
stash
force
commit
push
tag
merge
release
```

除非使用者另外明確授權。

若出現不明修改：

```text
STOP
```

先回報。

---

# 3. 本輪禁止事項

本輪是驗收與量測，不是新一輪大改。

禁止：

```text
Productionize nearest-leader index
改 0.1 s fixed tick
改 moving-block
改 rear-clear
改 collision logic
改 resource arbitration
改 passing / turnback semantics
改 sample operational truth
改 Schema
重寫 playback architecture
大改 Time-Distance cache
大改 Speed chart
```

除非原生驗收發現**明確 bug**，且屬：

```text
small
isolated
reproducible
low-risk
```

才可在使用者明確同意後修。

---

# 4. 核心驗收場景

使用大型 28 站 sample：

```text
samples/大型機場線-完整營運示範範例.mrtsim.json
```

或 repository 中目前對應的正式大型 sample。

請確認實際檔名與 path。

至少覆蓋：

```text
O04 passing
O13 passing
O20 pocket turnback
return movement
late-run train completion
resource release
```

---

# 5. Native Windows Desktop Acceptance

必須在真正 Windows WPF app 上驗收，不得用 offscreen runner 取代。

至少測：

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

每個分頁確認：

```text
可正常切換
資料持續更新
無 UI freeze
無明顯 layout corruption
無跨 tab stale visuals
無 hidden-tab redraw leakage
```

---

# 6. 60× Long-Run

至少做：

```text
60×
```

建議完整播放：

```text
0 s
→ scenario completion
```

若完整跑到結束太耗時，最低要求：

```text
至少跨過 O04
至少跨過 O13
至少跨過 O20
至少進入 late-run / return phase
```

但若 sample 約 8,000 s，可優先完整跑完。

記錄：

```text
requested rate
effective rate
simulation start/end
wall elapsed
completion status
```

---

# 7. Late-Run Validation

前輪 profiler 主要集中在早期約 600 s。

本輪必須特別驗證：

```text
late-run
```

至少在：

```text
O04 前後
O13 前後
O20 pocket turnback 前後
回程
接近 scenario completion
```

各取一段觀察。

確認：

```text
TimeDistance cache 沒有隨歷史成長而退化
Speed chart 沒有明顯變慢
working set 沒有異常持續上升
UI input gap 沒有後期惡化
```

---

# 8. Repeatability

同一台機器、同一 sample、同一 Release build。

至少：

```text
3 runs
```

測：

```text
60×
```

記錄：

```text
effective rate
max input stall
working set
allocation
GC
completion
visual issues
```

不要只用單次好看的結果下結論。

---

# 9. DPI / Scaling

至少測：

```text
100%
125%
150%
```

若環境可行。

若只能測其中兩種：

```text
明確記錄
```

檢查：

```text
route labels
platform labels
train markers
charts
TimeDistance
Speed chart
tab layout
scrollbars
selection
```

避免：

```text
重疊
裁切
錯位
模糊到不可讀
點選 hitbox 偏移
```

---

# 10. Resize

在播放中測：

```text
maximize
restore
manual resize
narrow width
wide width
short height
tall height
```

特別注意：

```text
Route cache invalidation
TimeDistance static layer rebuild
Speed chart redraw
train marker alignment
```

Resize 後：

```text
visual geometry 必須正確
```

---

# 11. Window Drag / Interaction

60× 播放中進行：

```text
拖動視窗
快速切 tab
點選列車
切 Route ↔ Speed
切 TimeDistance
scroll
resize
```

觀察：

```text
UI 是否卡住
輸入是否延遲
frame 是否長時間不更新
worker 是否停止前進
```

---

# 12. O04 Visual Acceptance

在 O04 passing 事件前後觀察：

```text
列車是否進入正確 physical passing path
route marker 是否連續
列車 marker 是否跳軌
主線 / 側線是否鏡射合理
passing order 是否符合 Engine event
```

不接受：

```text
marker teleport
wrong track display
route discontinuity
wrong platform alignment
```

---

# 13. O13 Visual Acceptance

同樣確認：

```text
passing request
entry
overtake
merge
release
```

畫面與 Engine event 一致。

---

# 14. O20 Pocket Turnback Acceptance

重點確認：

```text
列車進入 O20 pocket
停止
reverse
return opposite direction
回到正確 platform / mainline
VehicleId continuity
ServiceRun transition
```

不接受：

```text
UI teleport
direction icon error
wrong platform
wrong resource state
wrong route highlight
```

---

# 15. Speed Chart 驗收

目前 selected-render p95 約：

```text
11.224 ms
```

本輪先驗收，不先優化。

測：

```text
early-run
mid-run
late-run
selected train
train after turnback
```

記錄：

```text
render p50
render p95
render max
input gap
```

只有原生桌面確認仍有明顯卡頓時，才建議下一輪優化。

---

# 16. Time-Distance 長時間驗收

前輪已大幅優化。

本輪確認：

```text
early-run
mid-run
late-run
completion
```

仍保持：

```text
bounded geometry
no runaway memory
no full-history redraw regression
no visual gap around critical events
```

尤其確認：

```text
station
turnback
passing
direction change
```

仍有保留。

---

# 17. Memory / GC 長時間觀察

至少記錄：

```text
managed memory
working set
Gen0
Gen1
Gen2
allocation rate
```

時間點：

```text
start
after O04
after O13
after O20
late-run
completion
```

判斷：

```text
bounded
or
monotonic unbounded growth
```

不要只比較 end snapshot。

---

# 18. Native Input Responsiveness

前輪 offscreen 的 Route 仍觀察到：

```text
64.16 ms input-gap tail
```

本輪 native desktop 特別觀察：

```text
tab switch latency
drag responsiveness
resize responsiveness
click train latency
pause latency
resume latency
```

如果可以量：

```text
p50 / p95 / max
```

若不能：

```text
至少做 timestamped manual acceptance note
```

---

# 19. Pause / Resume During Heavy UI

在下列分頁測：

```text
Speed
TimeDistance
Route
```

於 60× 中：

```text
Pause
wait
Resume
```

確認：

```text
worker reacts promptly
no extra ticks after pause beyond expected command boundary
resume correct
no stale frame burst
```

---

# 20. Frame Drop Correctness

保留既有 slow-UI parity 原則。

Native interaction 造成 frame skip 時：

```text
Engine tick 不可丟
event 不可丟
resource state 不可丟
```

若可自動化，在同 simulation target 再做：

```text
native slow interaction
vs
headless / deterministic reference
```

---

# 21. Long-Run Playback Metrics

至少收集：

```text
requested rate
effective rate
publish FPS
unique apply FPS
skipped frames
max input gap
allocation MiB/s
managed memory
working set
Gen0/1/2
Speed render p95
TimeDistance render p95
Route render p95
```

分：

```text
early
mid
late
```

---

# 22. Acceptance Thresholds

## Simulation throughput

```text
60× requested
effective >= 58×
```

若低於：

```text
FAIL / investigate
```

## Native responsiveness

目標：

```text
most interactions < 50 ms
```

偶發 spike 可記錄，但：

```text
repeated >100 ms
```

需標記問題。

## TimeDistance

應維持前輪量級：

```text
p95 << 16 ms
```

若 late-run 明顯回升：

```text
FAIL / investigate cache growth
```

## Memory

不接受：

```text
持續線性增長且 completion 後不收斂
```

---

# 23. Repeatability Table

請產出：

| Run | Effective × | Completion | Max input gap | Working set peak | Gen2 | Visual issue |
|---|---:|---|---:|---:|---:|---|
| 1 | | | | | | |
| 2 | | | | | | |
| 3 | | | | | | |

---

# 24. Event Visual Gate Table

請產出：

| Event | Engine observed | Native visual observed | PASS/FAIL | Note |
|---|---|---|---|---|
| O04 passing | | | | |
| O13 passing | | | | |
| O20 pocket entry | | | | |
| O20 reverse | | | | |
| return mainline | | | | |
| resource release | | | | |

---

# 25. DPI Table

請產出：

| DPI | Route | Speed | TimeDistance | Labels | Hit test | Result |
|---|---|---|---|---|---|---|
| 100% | | | | | | |
| 125% | | | | | | |
| 150% | | | | | | |

若無法全部測：

```text
N/A + reason
```

---

# 26. Resize / Interaction Table

請產出：

| Action | Result | Max delay / symptom | PASS/FAIL |
|---|---|---|---|
| maximize | | | |
| restore | | | |
| narrow resize | | | |
| wide resize | | | |
| drag window | | | |
| Route→Speed | | | |
| Speed→TimeDistance | | | |
| train click | | | |
| pause/resume | | | |

---

# 27. 不要用 Offscreen 代替 Native

Offscreen runner：

```text
仍要跑
```

但只能作 regression。

Native acceptance 必須單獨標：

```text
PASS
FAIL
NOT COMPLETED
```

不要混在一起。

---

# 28. 若發現問題

若發現：

```text
Speed chart native-only stutter
late-run TimeDistance degradation
DPI misalignment
resize cache bug
O04/O13/O20 visual inconsistency
```

先：

```text
reproduce
measure
document
```

不要直接大修。

只有：

```text
明確 root cause
小範圍 fix
低風險
```

且取得使用者同意後才修改。

---

# 29. 文件更新

更新：

```text
docs/PLAYBACK_END_TO_END_PROFILE.md
QA_REPORT.md
```

新增：

```text
docs/NATIVE_DESKTOP_ACCEPTANCE.md
```

內容至少包含：

```text
environment
display scaling
hardware / GPU if available
sample
Release SHA
repeat runs
early/mid/late metrics
DPI results
resize/drag results
O04/O13/O20 visual gates
memory/GC trend
remaining risks
final acceptance decision
```

---

# 30. 最終 Acceptance Decision

請明確下結論：

```text
NATIVE PLAYBACK:
PASS
PASS WITH LIMITATIONS
FAIL
NOT COMPLETED
```

以及：

```text
LONG-RUN 60×:
PASS
FAIL
```

以及：

```text
DPI/RESIZE:
PASS
PASS WITH LIMITATIONS
FAIL
NOT COMPLETED
```

---

# 31. Nearest-Leader Index

本輪仍維持：

```text
NEEDS MORE DATA
production default = oracle
```

不要動。

---

# 32. Build / Regression

最終仍要跑：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

如果本輪完全沒有 code 變更，也照樣執行最後 regression。

---

# 33. 本輪不授權發布

不要：

```text
commit
push
merge
tag
release
```

完成後停在目前工作樹，回報結果。

---

# 34. 完成後回報

請回報：

1. current branch
2. HEAD/base SHA
3. code changes 是否為 0
4. modified files
5. Release build
6. Engine runner
7. WPF runner
8. git diff --check
9. native environment
10. monitor resolution
11. DPI/scaling
12. sample path
13. Run 1 effective rate
14. Run 2 effective rate
15. Run 3 effective rate
16. completion status
17. early-run metrics
18. mid-run metrics
19. late-run metrics
20. max working set
21. memory growth trend
22. GC totals
23. max input gap
24. Speed render p95 early/mid/late
25. TimeDistance p95 early/mid/late
26. Route render p95 early/mid/late
27. Route/Speed nested hidden render audit
28. O04 visual result
29. O13 visual result
30. O20 entry result
31. O20 reverse result
32. return mainline result
33. resource release result
34. 100% DPI result
35. 125% DPI result
36. 150% DPI result
37. resize result
38. drag result
39. tab-switch result
40. train-click result
41. pause/resume result
42. repeated >100 ms stalls 是否存在
43. native playback decision
44. long-run decision
45. DPI/resize decision
46. nearest-leader status
47. remaining primary bottleneck
48. next recommended action

---

# 35. 最終技術原則

```text
Acceptance first.
Do not optimize what has not failed natively.
Do not replace native evidence with offscreen tests.
Do not change Engine truth for UI smoothness.
Do not productionize nearest-leader index in this round.
Do not hide late-run degradation behind early-run averages.
```

本輪最重要的成果是：

> 證明目前經過 pacing、lazy refresh 與 incremental Time-Distance 優化後的 playback，在真正 Windows 桌面、不同 DPI、互動操作與 O04/O13/O20 等後段營運事件下，能長時間穩定維持 60×，且沒有 memory、visual、input responsiveness 或 simulation correctness 回歸。
