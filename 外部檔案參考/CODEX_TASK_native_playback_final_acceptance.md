# Codex 任務：Native Playback Final Acceptance — Time-Distance 標籤修正 + Input / Memory / Event / DPI 驗收

## 0. 任務目的

請延續目前 `mrt-route-simulator` 的 native desktop acceptance 工作，完成剩餘的**原生 Windows 播放驗收**。

本輪已不需要再證明 Engine throughput。現有原生證據已顯示：

```text
大型 28 站 sample
三次連續 native 60× 完成

Run 1 effective：
59.99899×

Run 2 effective：
59.99814×

Run 3 effective：
59.99708×

event count：
620

三次 event sequence：
一致

event timestamp max delta：
0 s
```

因此本輪只處理下列四個未完成項目：

```text
1. 修正 Time-Distance 相鄰站名重疊
2. 建立真正 native input responsiveness 證據
3. 做更長 / 同頁 repeat memory + GC 觀察
4. 完成 O04 / O13 / O20 visual gate 與 100/125/150% DPI / resize gate
```

本輪禁止把工作重新擴大到 Engine 效能或 nearest-leader index。

---

# 1. 先閱讀現有文件

必讀：

```text
docs/NATIVE_DESKTOP_ACCEPTANCE.md
docs/PLAYBACK_END_TO_END_PROFILE.md
docs/PLAYBACK_PACING_PROGRESS.md
QA_REPORT.md
```

以 repository 實際內容為準。

現況結論不得被改寫成比證據更強的宣稱：

```text
Full-run >=58× throughput:
PASS

Native playback:
visual gate FAIL / overall NOT COMPLETED

Long-run 60×:
throughput/completion/repeat-event sub-gates PASS
overall NOT COMPLETED

DPI/resize:
NOT COMPLETED

Nearest-leader:
NEEDS MORE DATA
production default = oracle
```

---

# 2. Git / Worktree 安全

先執行：

```powershell
git fetch origin
git status
git branch --show-current
git rev-parse HEAD
```

目前已知基準：

```text
branch:
codex/nearest-leader-index-prototype

HEAD/base:
558e06b534f97c997f450379f747e7709528ac17
```

既有工作樹含未提交 prototype / playback / native measurement 變更。

如果 dirty changes 與現有報告記載一致：

```text
允許繼續使用目前 worktree
```

禁止：

```text
reset
clean
stash
force
commit
push
merge
tag
release
```

若出現來源不明的額外修改：

```text
STOP
```

先回報。

---

# 3. 本輪允許的 Production 修改

本任務明確授權：

```text
A. Time-Distance station-label layout / presentation 修正
B. native acceptance diagnostics / input timing instrumentation
C. 必要的 WPF-only regression tests
```

不得改：

```text
SimulationWorld physics
0.1 s fixed tick
moving block
rear-clear
collision
resource arbitration
passing semantics
turnback semantics
sample operational truth
Schema
nearest-leader production default
```

如果 visual fix 需要碰 Engine：

```text
STOP
```

先回報，不可自行擴大。

---

# 4. Work Package A：Time-Distance Station Label Overlap

目前原生畫面可重現：

```text
O08 / O08a / O09
O15 / O15a / O16
```

等相鄰站名垂直重疊。

這是目前 native visual FAIL。

---

# 5. 先定位 Label Layout Root Cause

請先拆解：

```text
station y-coordinate calculation
label anchor
font metrics
label margin
available vertical spacing
collision / overlap handling
DPI transform
canvas scaling
```

確認 overlap 是：

```text
固定 layout 演算法問題
或
特定 DPI / canvas size 問題
```

不要直接硬編：

```text
if station == O08a then offset + N
```

禁止站名特例。

---

# 6. Station Label Layout 原則

應採通用 presentation algorithm。

可研究：

```text
label collision avoidance
alternating left/right or offset lanes
minimum vertical spacing
greedy displacement
small leader line
multi-column station labels
```

但必須維持：

```text
station actual y-position truth
trajectory geometry truth
station-to-track correspondence
```

也就是：

```text
可以移 label
不可移 station coordinate
```

---

# 7. Label Acceptance

至少覆蓋：

```text
O08 / O08a / O09
O15 / O15a / O16
```

以及全 28 站掃描。

要求：

```text
no overlapping label bounding boxes
no clipped labels
no station-to-label ambiguity
no trajectory geometry shift
```

至少測：

```text
current native size
maximize
restore
100%
125%
150% scaling
```

若系統環境無法自動切 DPI：

```text
明確記錄 NOT COMPLETED
```

不得用 capture pixel size 猜 DPI。

---

# 8. Label Regression Test

新增 WPF visual/layout regression。

至少測：

```text
TimeDistanceStationLabelsDoNotOverlap
TimeDistanceStationLabelsRemainMappedToStations
TimeDistanceLabelsRemainValidAfterResize
TimeDistanceLabelsRemainValidAcrossSupportedScaleFactors
```

若 test harness 無法真的模擬 OS DPI：

可測：

```text
WPF layout scale / target size
```

但文件必須標：

```text
automated layout proxy
!= native OS scaling acceptance
```

---

# 9. Work Package B：Native Input Responsiveness Instrumentation

目前已有：

```text
Input-priority 20 ms DispatcherTimer gap
```

但它不是實際 mouse / click latency。

Run 2 曾觀察：

```text
max gap 344.428 ms
```

且沒有 timestamp / cause attribution。

本輪要新增 opt-in native acceptance diagnostics。

---

# 10. Input Timing 應量什麼

至少記錄下列 native action：

```text
tab click
nested tab click
train marker click
pause
resume
window resize start/end
window drag / move
scroll
```

每個 action 儘可能記錄：

```text
input received timestamp
handler start
handler end
first resulting visual/frame apply timestamp
simulation clock
selected tab
```

輸出：

```text
input-to-handler
handler duration
input-to-visible-update
```

不要用 automation framework round-trip time 當 App latency。

---

# 11. >100 ms Stall Evidence

保留 DispatcherTimer gap probe，但新增：

```text
timestamp
selected page
simulation time
recent input action
process memory
GC generation counters
```

至少能回答：

```text
>100 ms gap 發生幾次？
在哪個 tab？
是否緊接 tab switch / resize / GC？
```

不要沒有 ETW 證據就宣稱 GC 是原因。

---

# 12. Input Acceptance Threshold

建議：

```text
normal interaction p95 < 50 ms
```

允許少數 spike，但：

```text
repeated >100 ms
```

必須列出 occurrence count 與 context。

若：

```text
0 或極少、可解釋
```

可標 PASS WITH LIMITATIONS。

若持續重複：

```text
FAIL / investigate
```

---

# 13. Work Package C：Longer Same-Page Memory / GC Repeat

目前三次 native run 分別使用不同 visible page：

```text
Speed
TimeDistance
Route
```

final working set：

```text
312.58
332.52
342.41 MiB
```

目前不能判定：

```text
bounded
或
leak
```

---

# 14. Memory Protocol

至少做：

```text
同一 Release process
同一 sample
同一 selected page
連續 5 次 complete run
```

建議先選：

```text
TimeDistance
```

因為它是 history-heavy path。

另可做：

```text
Speed 3 runs
```

作對照。

每輪不要重啟 process。

---

# 15. Memory Checkpoints

每 run 記錄：

```text
before Play
early
mid
late
completion
10 s idle after completion
after Reset
before next Play
```

至少：

```text
Working Set
Private Bytes
GC.GetTotalMemory(false)
GC collections Gen0/1/2
process allocation total / delta
trajectory count
safety count
event count
display cache point counts
```

不要主動：

```text
GC.Collect()
```

除非只是另開 diagnostic experiment，且不得混入 acceptance 主結果。

---

# 16. Memory 判斷

不要只看 Working Set。

請判斷：

```text
retained managed heap
display cache
trajectory retained data
WPF visual count
process working set
```

是否每 Reset / run 都持續階梯式增加。

結果分類：

```text
BOUNDED
LIKELY BOUNDED WITH FRAMEWORK RESERVE
UNRESOLVED
SUSPECTED LEAK
CONFIRMED LEAK
```

只有有物件 retention / heap evidence 才可用 `leak`。

---

# 17. Time-Distance Cache Metrics

每次 checkpoint 額外記：

```text
actual series count
planned series count
ordinary point total
critical point total
Polyline count
TextBlock count
Canvas child count
```

確認：

```text
Reset
project reload
new generation
```

會正確清除 presentation cache。

---

# 18. Work Package D：Native O04 / O13 / O20 Visual Gates

60× 太快，不適合目視捕捉短事件。

本輪允許：

```text
先 60× 快轉到事件前
→ Pause
→ 切到 1× / 5× / 10×
→ Resume
```

或：

```text
直接以已知 current event CSV timestamp 導引
```

但事件時間必須來自：

```text
本輪 native event evidence / current sample
```

不得沿用過時 QA 秒數。

---

# 19. O04 Visual Gate

觀察完整：

```text
approach
passing request
diverge
physical passing track
overtake
merge
resource release
```

確認：

```text
marker continuous
correct physical branch
no teleport
no wrong platform
no marker crossing impossible geometry
event order matches current Engine event log
```

---

# 20. O13 Visual Gate

同樣觀察：

```text
approach
diverge
overtake
merge
release
```

並截取：

```text
before
during
after
```

三個 native screenshots。

---

# 21. O20 Pocket Visual Gate

至少捕捉：

```text
pocket entry
stop
reverse
opposite-direction departure
return mainline
resource release
```

核對：

```text
VehicleId continuity
ServiceRunId transition
direction
platform
resource state
```

---

# 22. Visual Evidence 格式

每個 event gate 至少記：

```text
native screenshot
simulation timestamp
VehicleId
ServiceRunId
current edge / platform if available
corresponding Engine event
PASS / FAIL
```

不要只有一句：

```text
looks correct
```

---

# 23. Work Package E：DPI / Resize Matrix

目前 WPF telemetry 顯示：

```text
DPI 96x96
scale 1x1
```

但 OS Settings 100/125/150% matrix 未完成。

本輪要實際完成：

```text
100%
125%
150%
```

如果 system automation 無法切：

```text
可由使用者手動切
```

但 Codex 必須：

```text
重新讀 native WPF DPI telemetry
```

證明 App 真正跑在新 scaling。

---

# 24. 每個 DPI 至少測

```text
Route
Speed
TimeDistance
labels
train marker
hit test
tab layout
scrollbars
```

並做：

```text
maximize
restore
wide resize
narrow resize（不低於 MinWidth）
tall
short（不低於 MinHeight）
```

---

# 25. DPI / Resize Visual Assertions

不接受：

```text
station label overlap
text clipping
wrong hit-test
marker offset
canvas geometry jump
route/platform misalignment
scrollbars hiding required content
```

Time-Distance label fix 必須在三個 DPI 都通過。

---

# 26. Train Click / Hit Test

在：

```text
100%
125%
150%
```

各至少點一個運行中 train marker。

確認：

```text
selected VehicleId correct
Speed view target correct
no coordinate offset
```

---

# 27. Pause / Resume Heavy UI Gate

至少在：

```text
Route
Speed
TimeDistance
```

各測：

```text
Pause
wait 2 s
Resume
```

記錄：

```text
command latency
clock stability
first post-resume update
```

---

# 28. Throughput 不需重做大規模證明

既有三次完整 native run：

```text
59.99899×
59.99814×
59.99708×
```

已足以證明 throughput sub-gate。

本輪只需在修改 label / diagnostics 後：

```text
至少 1 次完整 60× sanity run
```

確認沒有回歸。

若：

```text
effective <58×
```

才重新展開 throughput investigation。

---

# 29. Nearest-Leader Index

本輪仍然：

```text
NEEDS MORE DATA
production default = oracle
```

禁止 productionize。

不要因為本輪 native PASS 就順手切 index。

---

# 30. Speed Chart

目前 native early Speed p95 曾到：

```text
15.675 ms
```

mid / late：

```text
9.090 / 8.145 ms
```

本輪只觀察。

只有 input instrumentation 證明 Speed chart 是 repeated >100 ms stall 的 root cause，才提出下一輪 optimization。

本輪不要大改 Speed chart。

---

# 31. Acceptance Decision

完成後分別給：

```text
TIME-DISTANCE LABELS:
PASS / FAIL

NATIVE INPUT:
PASS / PASS WITH LIMITATIONS / FAIL / NOT COMPLETED

MEMORY:
BOUNDED / LIKELY BOUNDED WITH FRAMEWORK RESERVE / UNRESOLVED / SUSPECTED LEAK

O04 VISUAL:
PASS / FAIL / NOT COMPLETED

O13 VISUAL:
PASS / FAIL / NOT COMPLETED

O20 VISUAL:
PASS / FAIL / NOT COMPLETED

DPI / RESIZE:
PASS / PASS WITH LIMITATIONS / FAIL / NOT COMPLETED

NATIVE PLAYBACK OVERALL:
PASS / PASS WITH LIMITATIONS / FAIL / NOT COMPLETED
```

---

# 32. Overall PASS 最低條件

要宣稱：

```text
NATIVE PLAYBACK OVERALL = PASS
```

至少：

```text
Time-Distance labels PASS
O04/O13/O20 visual PASS
100/125/150 DPI PASS
resize/hit-test PASS
no repeated unexplained >100 ms input stalls
memory not suspected/confirmed leak
60× sanity remains >=58×
Release/Engine/WPF regressions PASS
```

若 memory 尚無法完全證明 bounded，但沒有 leak evidence：

可考慮：

```text
PASS WITH LIMITATIONS
```

並明確列限制。

---

# 33. 文件更新

更新：

```text
docs/NATIVE_DESKTOP_ACCEPTANCE.md
docs/PLAYBACK_END_TO_END_PROFILE.md
QA_REPORT.md
```

另新增 raw evidence：

```text
docs/native-acceptance-raw/
```

至少保存：

```text
input timing JSONL
stall timestamp JSONL
memory checkpoints JSONL
current event timestamps
```

Screenshots 若 repository 規則允許：

```text
docs/native-acceptance-evidence/
```

若不適合 commit：

```text
放外部 local evidence path
並在報告記錄
```

---

# 34. Tests

至少新增／更新：

```text
TimeDistanceStationLabelsDoNotOverlap
TimeDistanceLabelsRemainMappedAfterResize
NativeAcceptanceInputRecorderIsOptIn
NativeAcceptanceInputRecorderDoesNotAffectPhysics
NativeAcceptanceStallRecordsIncludeContext
NativeAcceptanceMemoryResetClearsPresentationCache
```

命名依現有 style。

---

# 35. Build / Regression

最終執行：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

並確認：

```text
Engine event/trajectory/safety behavior未因 UI 修正改變
```

---

# 36. 本輪不授權發布

不要：

```text
commit
push
merge
tag
release
```

完成後保留工作樹，回報結果。

---

# 37. 完成後回報

請回報：

1. current branch
2. HEAD/base
3. modified files
4. label overlap root cause
5. label layout algorithm
6. O08/O08a/O09 result
7. O15/O15a/O16 result
8. all-28-station overlap scan
9. 100% DPI result
10. 125% DPI result
11. 150% DPI result
12. resize result
13. train hit-test result
14. input timing instrumentation design
15. tab click p50/p95/max
16. train click p50/p95/max
17. pause p50/p95/max
18. resume p50/p95/max
19. resize timing
20. >100 ms stall count
21. >100 ms stall contexts
22. same-page memory run count
23. memory run1 checkpoints
24. memory run2 checkpoints
25. memory run3 checkpoints
26. memory run4 checkpoints
27. memory run5 checkpoints
28. max working set
29. managed heap trend
30. allocation trend
31. GC trend
32. display cache trend
33. memory classification
34. O04 visual result
35. O13 visual result
36. O20 pocket entry result
37. O20 reverse result
38. O20 return mainline result
39. resource release visual result
40. final 60× sanity rate
41. Release build
42. Engine runner
43. WPF runner
44. git diff --check
45. nearest-leader status
46. Time-Distance label decision
47. Native input decision
48. DPI/resize decision
49. Native playback overall decision
50. remaining limitations
51. next recommended action

---

# 38. 最終技術原則

```text
Fix presentation with presentation logic.
Do not move physical station truth to solve label overlap.
Measure actual input latency, not automation round-trip.
Do not call Working Set growth a leak without retained-object evidence.
Use current event timestamps, not historical guessed seconds.
Slow down or pause around O04/O13/O20 to inspect real native visuals.
Verify actual WPF DPI telemetry after each scaling change.
Do not touch Engine semantics.
Do not productionize nearest-leader index.
```

本輪真正的完成條件是：

> 把目前唯一明確 native 視覺 FAIL（Time-Distance station label overlap）修掉，並用可稽核的原生 input、memory、O04/O13/O20 事件畫面與 100/125/150% DPI 證據，決定這一版是否真的可以從 `NOT COMPLETED` 升級成 `PASS` 或 `PASS WITH LIMITATIONS`。
