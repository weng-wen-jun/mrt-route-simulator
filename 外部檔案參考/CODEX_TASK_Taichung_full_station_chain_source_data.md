# Codex 任務：以正式站里程資料建立臺中機場捷運 Full Station Chain

## 任務目的

請依本檔提供的 source-backed 規劃資料，更新 `mrt-route-simulator` 的臺中機場捷運大型範例建模資料，優先完成 **Full Station Chain** 階段。

本次重點是把先前 synthetic 的主線站距，改成由正式規劃表中的 **車站里程（chainage）** 推導，不要再人工猜測站距。

Repository：

`weng-wen-jun/mrt-route-simulator`

基準分支：

`main`

現有 minimal sample：

`samples/臺中機場捷運-主要站簡化可執行範例.mrtsim.json`

後續 full sample 應保留 minimal sample，不得覆蓋或刪除。

---

# 1. Git 安全流程

先執行：

```powershell
git fetch origin
git status
```

若工作樹不是 clean：

- 停止
- 回報
- 不得 reset / clean / stash / force push

若 clean，從最新 `origin/main` 開新 branch：

```powershell
git switch -c codex/taichung-full-station-chain origin/main
```

禁止：

- 直接修改 `main`
- 直接修改 `GPT-use`
- force push
- tag
- release
- 自行 merge PR

---

# 2. Source-backed 車站里程

以下車站里程為本次規劃資料的正式參照值。

單位：m  
`0+190 = 190 m`

| 站號 | 車站里程 |
|---|---:|
| O01 | 190 |
| O02 | 1,567 |
| O03 | 2,197 |
| O04 | 4,467 |
| O05 | 5,067 |
| O06 | 6,727 |
| O07 | 7,677 |
| O08 | 10,137 |
| O08a | 10,843 |
| O09 | 11,773 |
| O10 | 12,873 |
| O11 | 14,278 |
| O12 | 15,348 |
| O13 | 16,208 |
| O14 | 17,053 |
| O15 | 17,737 |
| O15a | 18,878 |
| O16 | 19,450 |
| O17 | 20,728 |
| O18 | 21,943 |
| O19 | 23,103 |
| O20 | 24,023 |
| O21 | 24,453 |
| O22 | 25,738 |
| O23 | 26,388 |
| O24 | 28,118 |
| O25 | 28,873 |
| O26 | 30,133 |

O01 → O26 主線 chainage 差：

```text
30,133 - 190 = 29,943 m
```

即：

```text
29.943 km ≈ 規劃資料所列 29.9 km
```

---

# 3. 站間距：必須由 chainage 相減產生

不要人工維護第二份站距來源。

請由上表 chainage 程式化計算：

| 區間 | 距離 |
|---|---:|
| O01–O02 | 1,377 m |
| O02–O03 | 630 m |
| O03–O04 | 2,270 m |
| O04–O05 | 600 m |
| O05–O06 | 1,660 m |
| O06–O07 | 950 m |
| O07–O08 | 2,460 m |
| O08–O08a | 706 m |
| O08a–O09 | 930 m |
| O09–O10 | 1,100 m |
| O10–O11 | 1,405 m |
| O11–O12 | 1,070 m |
| O12–O13 | 860 m |
| O13–O14 | 845 m |
| O14–O15 | 684 m |
| O15–O15a | 1,141 m |
| O15a–O16 | 572 m |
| O16–O17 | 1,278 m |
| O17–O18 | 1,215 m |
| O18–O19 | 1,160 m |
| O19–O20 | 920 m |
| O20–O21 | 430 m |
| O21–O22 | 1,285 m |
| O22–O23 | 650 m |
| O23–O24 | 1,730 m |
| O24–O25 | 755 m |
| O25–O26 | 1,260 m |

**實作原則：**

```text
edgeDistance(i → i+1)
=
chainage(i+1) - chainage(i)
```

不要把上面的 Δ 表再建立成另一份硬編碼權威來源。

---

# 4. Full Station Chain 車站順序

Full sample 必須至少建立：

```text
O01
O02
O03
O04
O05
O06
O07
O08
O08a
O09
O10
O11
O12
O13
O14
O15
O15a
O16
O17
O18
O19
O20
O21
O22
O23
O24
O25
O26
```

共 28 個營運建模站。

另外規劃資料提到：

```text
O00 = 預留站
```

但目前沒有足夠資料建立其正式營運 chainage / service role。

因此：

- 本次不要把 O00 加入運行 ServiceRoute
- 在 Scenario Manifest 記錄為 future/reserved station
- 不得自行猜測 O00 里程

---

# 5. StationChainage / mainline projection 權威原則

本表的 chainage 應作為：

**主線 station-center projected chainage 的 source of truth**

例如：

```text
O04 center = 4,467 m
O05 center = 5,067 m
```

主線投影距離必須保持：

```text
O04 → O05 = 600 m
```

但是特殊站內部 topology 不需要把整個站間硬塞成單一 `TrackEdge.Length`。

例如 O04 未來可拆成：

```text
approach
→ turnout
→ platform siding / through track
→ exit turnout
```

此時：

- station-center projected chainage 仍應符合 4,467 m
- 特殊 siding traversal 實際距離可與 mainline projection 不同
- 不要為了讓特殊設施 edge 長度相加剛好等於站間距，而扭曲 topology

---

# 6. 車站功能定位

規劃資料可作為 station metadata 的資訊：

## 端點站

```text
O01
O26
```

另：

```text
O00 = 未來預留端點／預留站
```

## 轉乘／重要站

規劃表中可辨識：

```text
O05
O08
O11
O14
O16
O20
```

重要轉乘／周邊資訊：

| 站號 | 規劃資訊 |
|---|---|
| O08 | 水湳園區核心，轉乘紅線 |
| O11 | 轉乘綠線文心中清站 |
| O14 | 轉乘紅線 |
| O16 | 臺中大車站計畫，轉乘藍線 B19 |
| O20 | 轉乘紫線 |

若目前 Schema 無適合欄位：

- 不要為本任務強行新增 Schema
- 先寫進 Scenario Manifest / README
- 不影響 Engine runtime

---

# 7. 特殊站型資訊

以下可視為 source-backed 規劃資訊。

## O08

規劃表中可辨識：

```text
2 島 3 股
```

這與先前資料中的舊區間折返概念一致。

用途：

- 未來建立 historical O08–O20 區間車 scenario
- 中間股可作折返用途

目前 full scenario 若採 O01–O20 區間車：

- 主折返仍放 O20
- 不要把 O08 舊折返方案當成目前主要營運方案

---

## O15

有地下穿越停車場相關工程條件。

目前只作 metadata。

不得自行生成：

- 精確坡度
- 精確地下線形
- 施工限制速度

---

## O16

規劃資訊：

- 地下穿越臺鐵土地
- 臺中大車站計畫
- 轉乘藍線 B19

另依先前資料：

```text
B2F = 穿堂層
B3F = 上月台層
B4F = 下月台層
```

採地下疊式月台。

若目前 Schema 沒有垂直站型欄位：

- 寫入 Manifest
- 不為本任務修改 Schema

---

## O24 / O25

規劃表可辨識為：

```text
側式車站
```

---

## O26

規劃表可辨識：

```text
島式車站
站前迴車
```

注意：

目前 Full Station Chain 階段先建立合法主線終點即可。

**不要在本次僅憑文字自行猜測 O26 turnout geometry。**

未來若新增 O26 全程車折返 facility：

- 需另有詳細配線資料
- 或明確使用 synthetic facility 並在 Manifest 標示

---

# 8. 規劃中的站位調整背景

這些資訊應放入 Scenario Manifest，不是 Engine validator。

規劃資料列出：

1. 增設 O00 預留站，服務未來門戶計畫。
2. O02、O03 站距過近，周邊發展強度低，檢討存廢。
3. O04、O05 站距過近，周邊發展強度低，O05 採預留／配合未來路網建設概念。
4. 增設 O08a、移設 O09，服務中央公園、超巨蛋等重大建設。
5. 增設 O15a，服務干城商圈。
6. 移設 O16，大幅縮短臺鐵、藍線轉乘距離。
7. 北移 O19，服務大明路商圈。
8. O20、O21 距離過近，檢討存廢。
9. O22、O23 距離過近，檢討存廢。
10. O24、O25、O26 移設林森路，降低原中正路方案之交通、景觀及營運調度衝擊。

因此目前 Full Sample 可以保留全部表列站，但應在 Manifest 標註：

```text
planning-status:
current reference alignment

planning-note:
some station pairs remain subject to retention/removal review
```

不要把這份規劃表解讀成所有站位永久定案。

---

# 9. 車站間距規劃原則

來源頁面提供：

```text
捷運站周邊 500–800 m：
步行服務範圍

市區合理站距：
約 0.8–1.2 km

市郊合理站距：
約 1.2–2.0 km
```

這些只可作：

- QA warning
- planning metadata
- reasonableness check

不得做成：

- Schema hard error
- InfrastructureValidator failure

因為正式方案本身就存在例外，例如：

```text
O20–O21 = 430 m
O15a–O16 = 572 m
O04–O05 = 600 m
O02–O03 = 630 m
O22–O23 = 650 m
```

以及較長區間：

```text
O03–O04 = 2,270 m
O07–O08 = 2,460 m
```

---

# 10. 車站段線形原則

規劃文字指出車站段平縱面線形原則上應：

```text
保持直線
保持水平
```

目前可作為：

- future geometry rule
- Scenario Manifest
- station-zone modeling guideline

但本次不得自行新增：

- gradient physics
- curvature physics
- station curve validator

除非 repository 已有對應模型且不需改架構。

---

# 11. 本次 Full Station Chain 實作要求

本任務只完成大型 sample 分階段流程中的：

```text
Stage 1 = Full Station Chain
```

必須建立：

- 28 個 station
- 上下行 platform
- mainline nodes
- mainline track edges
- ServiceRoute traversals
- directedConnections
- outbound / inbound directionRouteBindings

先只做：

```text
普通雙線主線
+
全停服務 smoke test
```

本次不要同時加入：

- O20 pocket turnback
- O04 passing facility
- O13 passing facility
- dense timetable
- full peak/off-peak operational scenario

避免重新回到「所有特殊設施一次加入後才除錯」。

---

# 12. Builder 原則

大型案例優先使用既有：

```text
TopologyScenarioBuilder
TopologyScenarioValidation
TopologyProjectFactory
TopologyEditingServices
StationLayoutTemplateService
```

`.mrtsim.json` 是：

```text
loadable artifact / sample
```

不是大型 topology 的主要人工 source。

若需要新增 helper：

- 必須通用
- 不建立第二套 Domain Model
- 不建立第二套 runtime
- 不把 Taichung-specific hack 塞進 SimulationWorld

---

# 13. Port-side 規則

不得由以下資訊猜測：

```text
schematic position
畫面左／右
chainage 方向
```

去自動決定：

```text
fromPortSide
toPortSide
```

Port-side 必須使用明確建模資料。

遇到 connection 問題依序檢查：

```text
traversal direction
→ from/to node
→ from/to port-side
→ directedConnection
```

禁止：

- 關閉 `[CONNECTION-001]`
- 降低 validator
- 移除 directedConnections
- 改 virtual track 繞過問題

---

# 14. 驗證 Gate

## Structural Validation

至少：

```text
InfrastructureValidator
TopologyProjectFormat.Validate
```

確認：

- 28 stations refs 正確
- platforms refs 正確
- traversal 連續
- outbound route 正確
- inbound route 正確
- directedConnections 合法
- port-side 合法
- 無 unused / orphan topology

Structural 未通過不得進 Operational。

---

## Operational Validation

建立 topology-native runtime：

```text
TopologyProjectFormat.CreateRuntime
SimulationWorld
```

至少測：

```text
一班下行全停：
O01 → O26

一班上行全停：
O26 → O01
```

確認：

- 28 站順序正確
- 全部預期停站成功
- 最終正常退出
- collision = 0
- StationStopViolation = 0

---

## Regression Validation

階段完成後執行：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

---

# 15. 必須新增的測試

至少新增：

## Chainage test

確認所有站中心：

```text
expected chainage == source-backed chainage
```

允許合理 numerical tolerance。

## Distance derivation test

確認每個主線站間距：

```text
chainage[i+1] - chainage[i]
```

與 builder 實際 mainline projection 一致。

不要建立第二份人工 distance source。

## Full station chain validation

確認：

```text
O01～O26
+ O08a
+ O15a
```

皆存在且順序正確。

## Bidirectional operational test

確認：

```text
Outbound:
O01 → O26

Inbound:
O26 → O01
```

全程可完成。

---

# 16. Scenario Manifest 更新

在：

`samples/README.md`

更新臺中 Full Sample Manifest。

將以下項目改為 source-backed：

```text
station sequence
O08a
O15a
all station chainages
all interstation mainline projected distances
O01–O26 length = 29.943 km
```

保留仍屬 synthetic 的項目，例如：

```text
platform actual length
vehicle performance
dwell time
turnout geometry
turnout speed
O04/O13 facility length
O20 pocket length
O26 turnback exact geometry
gradient
curve radius
line speed profile
```

---

# 17. 不得誤宣稱的事項

完成本次任務後只能宣稱：

```text
Full Station Chain 完成
```

不得宣稱：

```text
Taichung full operational sample 完成
```

因為後續仍需要：

- Service patterns
- O20 turnback
- O04 passing
- O13 passing
- peak timetable
- off-peak timetable
- complete regression scenario

因此 `TODO.md` 中：

`V4-SAMPLE-TAICHUNG-AIRPORT-01`

仍應保持：

```text
[ ]
```

除非上述完整項目全部完成。

---

# 18. Commit 建議

請拆成合理 commits，例如：

```text
feat: add source-backed Taichung station chain data

feat: build full Taichung bidirectional station chain

test: validate Taichung chainage and station distances

docs: update Taichung scenario manifest
```

不要把整個工作塞進一個超大 commit。

---

# 19. 完成後 push

完成與驗證後：

```powershell
git push -u origin codex/taichung-full-station-chain
```

不要自行 merge main。

---

# 20. 完成後回報

請回報：

1. branch HEAD SHA
2. 修改檔案
3. logical commit SHA
4. Full sample / builder 檔案位置
5. 28 站是否全部存在
6. O08a 是否存在
7. O15a 是否存在
8. O01 chainage
9. O26 chainage
10. O01–O26 計算總長
11. 是否所有站距都由 chainage 相減產生
12. Structural Validation 結果
13. Operational Validation 結果
14. 下行 O01→O26 是否完成
15. 上行 O26→O01 是否完成
16. collision 數
17. StationStopViolation 數
18. Release build 結果
19. Engine runner 結果
20. WPF runner 結果
21. git diff --check
22. 仍屬 synthetic 的資料清單
23. 下一階段建議工作

若實際 repository 架構與本檔假設不同：

- 不要硬套
- 優先遵守 `AGENTS.md`
- 保持 Schema 8 / topology-native architecture
- 回報必要的最小調整
