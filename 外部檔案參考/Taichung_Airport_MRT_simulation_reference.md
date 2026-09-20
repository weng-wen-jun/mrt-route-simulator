# 臺中機場捷運（橘線）模擬參數與營運邏輯長期參照

> 用途：作為 `mrt-route-simulator` 臺中機場捷運大型範例、Scenario Builder、Regression Test、Scenario Manifest 與後續 Codex 開發的長期參照。
> 資料來源：使用者提供之「臺中市政府捷運工程局－臺中機場捷運（橘線）綜合規劃、環境影響評估等相關工作及基本設計委託技術服務」服務構想頁面照片。
> 注意：本文將「照片中可直接辨識的規劃資訊」與「仍需採 synthetic test values 的缺口」分開。若未來取得正式報告 PDF、路線縱斷面、車輛規格或號誌資料，應以正式文件覆核與更新。

---

## 1. 建模定位

本專案中的臺中機場捷運 sample 應分成兩個層級：

### 1.1 Minimal baseline
現有 GitHub sample：

`臺中機場捷運-主要站簡化可執行範例.mrtsim.json`

目前僅包含：

- O01
- O08
- O11
- O16
- O20
- O26

用途：

- Schema 8 基本讀檔
- topology-native runtime
- 雙向主線
- 基本停站模式
- 基本手動班表
- Structural / Operational smoke test

### 1.2 Full operational sample
後續完整範例至少需包含：

- O01～O26 全站
- O08a
- O15a
- 上下行完整月台與主線
- 全程車
- 區間車
- 機場直達車
- O20 站後折返
- O04 越行
- O13 越行
- 多車交錯營運
- Scenario Manifest
- Structural / Operational / Regression validation

---

# 2. 路線基本資料

## 2.1 路線分段

照片中的路線示意將橘線大致分成：

| 區段 | 約略範圍 | 型式 |
|---|---|---|
| 機場大雅段 | O01～O07 | 以高架為主 |
| 屯區市區段 | O08～O20 | 高架、地下混合，市區段以地下為主 |
| 大里霧峰段 | O21～O26 | 以高架為主 |

示意圖可看出：

- O01～O03：高架
- O03/O04 一帶：有型式轉換
- O04～O08 一帶：高架
- O09～O18 左右：地下為主
- O19/O20：地下／高架轉換
- O21～O26：高架

> 這些僅適合當 station/alignment metadata，不可由此示意圖推算精確坡度、轉換里程或曲線半徑。

---

## 2.2 路線長度

| 營運區段 | 長度 |
|---|---:|
| O01～O26 全程 | 29.9 km |
| O01～O20 | 23.8 km |
| O08～O20（較早區間車方案） | 13.9 km |

---

# 3. 列車與容量相關參數

## 3.1 列車容量

規劃資料採：

**450 人／列**

可用於：

- pphpd 理論容量檢核
- 車隊規模檢核
- 班距—運能換算
- Scenario Manifest

---

## 3.2 理論運能參考

| 等效班距 | 理論運能 |
|---|---:|
| 3.5 分鐘 | 7,714 pphpd |
| 7 分鐘 | 3,857 pphpd |

這與 450 人／列相符：

- 60 / 3.5 × 450 ≈ 7,714 pphpd
- 60 / 7 × 450 ≈ 3,857 pphpd

---

## 3.3 最大站間需求

照片中的規劃比較：

| 情境 | 最大站間需求 | 發生區間 |
|---|---:|---|
| 無延伸海線 | 7,683 pphpd | O15a～O16 |
| 有延伸海線 | 7,708 pphpd | O15a～O16 |

可用於：

- 容量合理性檢核
- 尖峰班距需求驗證
- 報表說明

---

# 4. 尖峰營運模式：全程車 + 區間車

## 4.1 尖峰時段

來源資料明確寫到：

- 07:00～09:00
- 17:00～19:00

尖峰時段 **不營運機場直達車**。

---

## 4.2 尖峰服務組合

尖峰採：

**全程車 + 區間車**

### 全程車

| 項目 | 數值 |
|---|---|
| 營運區間 | O01～O26 |
| 停站方式 | 站站停 |
| 班距 | 7 min |
| 模擬旅行時間 | 53.2 min |
| 模擬營運速率 | 33.7 km/h |
| 列車數 | 17 列 |

### 區間車

在「有延伸海線」情境下：

| 項目 | 數值 |
|---|---|
| 營運區間 | O01～O20 |
| 班距 | 7 min |
| 模擬旅行時間 | 36.2 min |
| 模擬營運速率 | 39.5 km/h |
| 列車數 | 12 列 |

尖峰備用列車：

**3 列**

尖峰車隊規模：

**32 列**

---

## 4.3 尖峰交錯班距

全程車與區間車於 O01 交替發車：

- 全程車：7 min
- 區間車：7 min
- 共線區段等效班距：3.5 min

可在 timetable 中用交錯 210 秒方式表達：

```text
00:00 FULL
03:30 SECTION
07:00 FULL
10:30 SECTION
...
```

---

## 4.4 尖峰追越邏輯

資料明確指出：

- 全程車與區間車於 O01 每 7 分鐘交替發車
- 區間車於 **O04 站追越 1 列全程車**
- 通過 O08 之後，區間車站站停至 O20
- O08 之後不再發生追越

應建立的 regression scenario：

```text
FULL-1 先發
↓
SECTION-1 後發
↓
FULL-1 先抵達 O04
↓
FULL-1 進入 O04 外側待避／停靠股
↓
SECTION-1 使用通過股超越
↓
SECTION-1 rear-clear
↓
FULL-1 才離開 O04
↓
SECTION-1 通過 O08 後站站停
↓
SECTION-1 抵達 O20
↓
進入 O20 站後折返設施
↓
換端
↓
反向返回 O01
```

---

# 5. 離峰營運模式：全程車 + 機場直達車

## 5.1 服務組合

離峰採：

**全程車 + 機場直達車**

### 全程車

| 項目 | 數值 |
|---|---|
| 營運區間 | O01～O26 |
| 停站方式 | 站站停 |
| 代表班距 | 7 min |
| 模擬旅行時間 | 54.9 min |
| 模擬營運速率 | 32.7 km/h |
| 列車數 | 17 列 |

### 機場直達車

| 項目 | 數值 |
|---|---|
| 營運區間 | **O01～O20** |
| 停靠站 | **O01、O08、O11、O16、O20** |
| 班距 | 7 min |
| 模擬旅行時間 | 29.1 min |
| 模擬營運速率 | 49.2 km/h |
| 列車數 | 10 列 |

> 重要：機場直達車 **不是 O01～O26**。
> 完整 sample 中，`AIRPORT-DIRECT` 必須在 O20 終止。

---

## 5.2 機場直達車停站模式

正式規劃停靠：

```text
O01 臺中機場
O08
O11
O16 臺中車站
O20
```

其餘中間站皆為 Pass / Skip-stop。

---

## 5.3 離峰追越邏輯

機場直達車應完成兩次追越：

1. **O04**
2. **O13**

規劃邏輯：

```text
FULL-1 先發
↓
AIRPORT-DIRECT 後發
↓
O04：第一次追越 FULL
↓
繼續運行
↓
O13：第二次追越另一列／前方 FULL
↓
停靠 O16
↓
停靠 O20
↓
O20 終止
```

這應成為 Full Sample 最重要的 regression scenario 之一。

---

# 6. O04 / O13 越行站配置

## 6.1 站型

來源資料顯示 O04、O13 採：

**2 側式月台 + 4 股道**

概念：

```text
外側待避／停靠股
─────────╲      ╱─────────
           ╲____╱

────────── 內側通過股 ──────────

────────── 內側通過股 ──────────

           ╱‾‾‾‾╲
─────────╱      ╲─────────
外側待避／停靠股
```

用途：

- 全程車／區間車：外側停靠股
- 機場直達車：內側通過股
- 可執行實體追越

---

## 6.2 站型屬性

| 站 | 型式 |
|---|---|
| O04 | 高架追越站 |
| O13 | 地下追越站 |

來源資料另提及土建空間需求：

- O04 高架配置所需道路寬度：約 30 m
- O13 地下配置需求：約 35 m

> 這兩項是土建參考，不應直接進入列車 physics。

---

## 6.3 追越安全間隔

資料提到前後列車保持約：

**1.5 分鐘安全間隔**

在模擬器中建議當作 timetable / operational target，不宜直接取代既有 moving block / braking / rear-clear 安全模型。

換言之：

- 1.5 min 是營運時刻配置約束
- `SimulationWorld` 的實際安全仍由既有 safety / occupancy / rear-clear 邏輯決定

---

# 7. O20 區間車折返

## 7.1 正式規劃概念

來源明確指出：

**O20 站後設置袋式儲車軌供區間車迴車。**

因此 Full Sample 不應使用「月台直接反向」或任意瞬移方式。

---

## 7.2 應建模的 topology-native 流程

```text
SECTION 抵達 O20 月台
↓
離站
↓
進入 O20 站後袋式儲車軌
↓
停等
↓
換端
↓
反方向 traversals
↓
返回 O20 反方向月台
↓
開始 O20 → O01 反向區間服務
```

必要模型元件：

- physical pocket / rear turnback edge
- crossover
- directedConnections
- movement traversal
- edge-local turnback stop
- reverse service run
- vehicle continuation
- facility occupancy / release

---

## 7.3 不允許的實作

不得：

- teleport
- UI 直接改 PositionMeters
- 以 legacy Route 當物理權威
- 取消 connection validation
- 省略 rear-clear
- 直接把車從 DOWN route 接成 UP route 而不經實體 facility

---

# 8. O08 舊區間折返方案

較早方案中：

- 區間車為 O08～O20
- O08 因街廓限制採 **2 島 3 股**
- 區間車利用中間股道折返

這可保留作為：

- historical scenario
- regression sample
- station-layout test

但目前 Full Sample 若採「有延伸海線」方案，主要區間服務應使用：

**O01～O20 + O20 站後袋式折返**

---

# 9. O16 臺中車站

## 9.1 車站型式

來源資料可確認 O16 採：

**地下疊式月台**

樓層配置：

| 樓層 | 功能 |
|---|---|
| B2F | 穿堂層 |
| B3F | 上月台層 |
| B4F | 下月台層 |

可作 station metadata。

---

## 9.2 轉乘關係

O16 與：

- 臺鐵臺中車站
- 藍線 B19

形成轉乘關係。

來源文字約提到：

**步行距離約 100～150 m**

可用於 UI / station metadata / future interchange model。

不應直接影響目前列車 physics。

---

# 10. 模擬時間成果參考

來源規劃提供以下旅行時間，可作 reasonableness target：

## 尖峰

O01 → O16：

- 全程車：約 34 min
- 區間車：約 28 min
- 節省：約 6 min

## 離峰

O01 → O16：

- 全程車：約 36 min
- 機場直達車：約 23 min
- 節省：約 13 min

O01 → O08：

- 機場直達車相對全程車約可節省 6 min

> 這些不建議設成 exact-equality regression。
> 在尚未取得完全一致的車輛性能、坡度、曲線與 dwell time 前，應當作合理範圍或 target。

---

# 11. Full Sample 建議服務定義

## 11.1 FULL-LINE

```text
serviceTypeId = FULL-LINE
route = O01 ↔ O26
stopPattern = ALL_STOP
priority = normal
```

---

## 11.2 SECTION

```text
serviceTypeId = SECTION
route = O01 ↔ O20
peak only
priority = higher than FULL-LINE if needed for overtaking request
turnback = O20 rear pocket
```

尖峰：

- O01 起發
- O04 超越 1 列 FULL-LINE
- O08 以後站站停
- O20 折返

---

## 11.3 AIRPORT-DIRECT

```text
serviceTypeId = AIRPORT-DIRECT
route = O01 ↔ O20
off-peak only
stops = O01, O08, O11, O16, O20
priority = express
canRequestOvertake = true
```

應在：

- O04
- O13

完成兩次追越。

---

# 12. Full Sample 建議 timetable

## 12.1 尖峰

```text
FULL-LINE  headway = 7 min
SECTION    headway = 7 min
interleave = 3.5 min
```

示意：

```text
07:00 FULL
07:03:30 SECTION
07:07 FULL
07:10:30 SECTION
...
```

---

## 12.2 離峰

```text
FULL-LINE       headway = 7 min
AIRPORT-DIRECT  headway = 7 min
```

直達車插班於全程車之間，具體 offset 應調整至：

- O04 可完成第一次追越
- O13 可完成第二次追越
- 不違反 occupancy / rear-clear / moving block

---

# 13. Regression Test 必備情境

## 13.1 Full station chain

確認：

- O01～O26
- O08a
- O15a
- 上下行平台
- service route traversals
- directed connections
- direction-route bindings

全部合法。

---

## 13.2 全程車

確認：

```text
O01 → O26
all-stop
complete
exit
```

反方向亦同。

---

## 13.3 機場直達車

確認：

停靠：

- O01
- O08
- O11
- O16
- O20

跨站：

- 其他中間站

不得前往 O26。

---

## 13.4 O20 折返

確認：

```text
O20 platform
→ pocket
→ stop
→ reverse
→ opposite O20 platform
→ continue to O01
```

並確認：

- trajectory continuous
- same vehicleId
- new serviceRunId
- resource lock/release
- no collision

---

## 13.5 O04 overtaking

應檢查事件順序：

```text
FULL reaches O04 first
FULL enters siding
SECTION / DIRECT arrives later
express uses through track
express passes FULL
express rear-clear
FULL departs
```

---

## 13.6 O13 overtaking

同 O04。

---

## 13.7 離峰雙追越

應檢查：

```text
AIRPORT-DIRECT overtakes FULL at O04
AND
AIRPORT-DIRECT overtakes FULL at O13
```

不可只測「無 exception」。

---

# 14. Structural / Operational / Regression Gate

## Structural Validation

至少執行：

- `InfrastructureValidator`
- `TopologyProjectFormat.Validate`

檢查：

- node / edge refs
- platform refs
- station refs
- traversal continuity
- direction
- from/to node
- port-side
- directedConnections
- resource refs

---

## Operational Validation

至少：

- `TopologyProjectFormat.CreateRuntime`
- `SimulationWorld`
- smoke test
- 推進到 overtaking / turnback 關鍵事件

檢查：

- dispatch
- stop/pass
- overtaking
- turnback
- vehicle continuation
- rear-clear
- exit

---

## Regression Validation

完整階段完成後：

```powershell
dotnet build MrtRouteSimulator.slnx -c Release

dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release

dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release

git diff --check
```

---

# 15. source-backed 與 synthetic 分界

## 15.1 可視為 source-backed 的項目

目前照片已足以支持：

- O01～O26 站序
- O08a
- O15a
- 全程長度 29.9 km
- O01～O20 23.8 km
- O08～O20 13.9 km
- 尖峰 07:00～09:00、17:00～19:00
- 尖峰：全程車 + 區間車
- 離峰：全程車 + 機場直達車
- 全程車 7 min
- 區間車 7 min
- 共線等效 3.5 min
- 直達車停靠 O01/O08/O11/O16/O20
- 尖峰區間車 O04 追越
- 離峰直達車 O04、O13 兩次追越
- O04/O13 為 2 側式月台 4 股道
- O20 站後袋式儲車軌折返
- 列車容量 450 人
- 各營運模式的旅行時間、營運速率、列車數
- 10% 備用率概念
- O16 地下疊式月台
- O16 與臺鐵／藍線 B19 轉乘
- 約 1.5 min 追越列車間隔概念

---

## 15.2 仍須 synthetic 或待補正式資料

目前照片不足以直接取得：

- 每一站精確站距
- 每一 edge 精確長度
- 精確 chainage
- O04/O13 道岔實際里程
- turnout 長度
- turnout speed
- passing siding 有效長度
- O20 袋式軌實際長度
- O20 折返秒數
- 各站 dwell time
- 車輛加速度
- 服務煞車
- 緊急煞車
- jerk
- traction fade
- coasting deceleration
- 列車長度
- 號誌 block / moving block 參數
- 全線縱坡
- 曲線半徑
- 曲線限速
- 站台實際長度
- 完整正式 timetable

這些若未取得正式來源：

必須標記：

`synthetic test value / 非正式設計值`

---

# 16. 不應由照片推論的資料

以下不得由示意圖猜測：

- `fromPortSide`
- `toPortSide`
- precise turnout orientation
- exact switch chainage
- exact platform length
- exact track gradient
- exact curvature
- exact speed restriction

特別是：

`fromPortSide / toPortSide`

不得由：

- 畫面左右
- schematic position
- 里程方向

自動推斷。

---

# 17. 對目前 GitHub minimal sample 的修正要點

現有 minimal sample 中，`AIRPORT-DIRECT` 曾設為：

```text
O01
O08
O11
O16
O20
O26
```

Full Sample 應改為：

```text
O01
O08
O11
O16
O20
```

**O20 為終點。**

另外：

- 尖峰不應營運 AIRPORT-DIRECT
- 尖峰應以 FULL-LINE + SECTION 為主
- SECTION 應於 O04 追越 FULL-LINE
- AIRPORT-DIRECT 應於 O04、O13 各追越一次
- SECTION 應於 O20 站後袋式軌折返

---

# 18. Codex 實作順序

建議固定順序：

```text
Stage 1
完整 O01～O26 + O08a/O15a station chain
↓
Structural
↓
Operational

Stage 2
FULL / SECTION / AIRPORT-DIRECT stop patterns
↓
Structural
↓
Operational

Stage 3
O20 rear pocket turnback
↓
Structural
↓
Operational

Stage 4
O04 4-track passing station
↓
Structural
↓
Operational

Stage 5
O13 4-track passing station
↓
Structural
↓
Operational

Stage 6
Peak timetable
FULL + SECTION
↓
Operational

Stage 7
Off-peak timetable
FULL + AIRPORT-DIRECT
↓
Operational

Stage 8
Regression
Release + Engine + WPF
↓
Scenario Manifest / QA / TODO
```

不得一次建立：

```text
full topology
+ O20
+ O04
+ O13
+ dense timetable
```

後才一起除錯。

---

# 19. 最終 Full Sample 應能證明的事情

完整範例完成後，至少應能證明：

1. O01～O26 全站 topology 可執行。
2. O08a / O15a 納入 route。
3. FULL-LINE 可全程運行。
4. SECTION 可 O01～O20 運行。
5. SECTION 可在 O20 站後袋式軌折返。
6. AIRPORT-DIRECT 只停 O01/O08/O11/O16/O20。
7. 尖峰不營運 AIRPORT-DIRECT。
8. SECTION 可於 O04 超越 FULL。
9. AIRPORT-DIRECT 可於 O04 超越 FULL。
10. AIRPORT-DIRECT 可於 O13 再次超越 FULL。
11. passing 後 FULL 必須等 express rear-clear 才離開。
12. 無 collision。
13. 無 station-stop violation。
14. vehicleId 在折返前後保持實體車一致。
15. serviceRunId 可正確切換。
16. 所有列車可完成預期 service 或 exit。
17. Schema 8 round-trip 正常。
18. WPF 可載入並播放。
19. Scenario Manifest 清楚區分 source-backed 與 synthetic。
20. 不降低任何 validator 或 safety rule。

---

# 20. 尚待取得之正式資料清單

若未來要從「可執行 full sample」提升為「高度貼近正式設計」，優先補：

1. 全線車站里程表
2. 各站站間距
3. 平縱面線形
4. 縱坡
5. 曲線半徑
6. 曲線限速
7. 車輛性能規格
8. 列車長度
9. 月台有效長度
10. O04 配線詳細圖
11. O13 配線詳細圖
12. O20 袋式儲車軌詳細圖
13. 道岔型號／岔速
14. 折返作業秒數
15. 各站標準 dwell time
16. 號誌／閉塞設計
17. 正式尖峰／離峰時刻表
18. 正式 fleet cycle / layover
19. 備援列車配置
20. 正式 OpenTrack 輸入參數（若可取得）

---

# 21. 文件維護原則

未來每取得一份正式來源，請更新：

- `samples/README.md` Scenario Manifest
- 本 MD
- Full Sample builder / helper
- Regression expected values
- `QA_REPORT.md`

任何數值從 synthetic 改為 source-backed 時，建議記錄：

```text
欄位：
舊值：
新值：
來源：
頁碼／圖號：
是否影響 regression：
是否需要重新跑完整 WPF：
```

---

## 22. 快速摘要

### 尖峰

```text
07:00–09:00 / 17:00–19:00

FULL:
O01–O26
all-stop
7 min

SECTION:
O01–O20
7 min

combined:
3.5 min

SECTION overtakes FULL at O04
SECTION all-stop after O08
SECTION turns back behind O20
```

### 離峰

```text
FULL:
O01–O26
all-stop
7 min

AIRPORT-DIRECT:
O01–O20
stops O01/O08/O11/O16/O20
7 min

overtake #1 = O04
overtake #2 = O13
```

### 核心站場

```text
O04 = elevated, 2 side platforms + 4 tracks
O13 = underground, 2 side platforms + 4 tracks
O20 = rear pocket / storage track turnback
O16 = underground stacked platforms, B3F/B4F
```

### 模擬核心原則

```text
Topology-native only
No teleport
No legacy Route authority
No validator weakening
No guessed port-side
Rear-clear before releasing waiting local train
Stage-by-stage validation
```
