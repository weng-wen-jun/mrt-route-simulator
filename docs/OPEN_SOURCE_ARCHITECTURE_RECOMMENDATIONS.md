# 開源鐵路模擬器調研：本專案架構建議

調研日期：2026-09-28。比較證據與 upstream 版本見 [比較報告](OPEN_SOURCE_RAIL_SIM_COMPARISON.md)。本文件是設計建議，未修改 production runtime、Schema 或 sample，也未執行新效能量測。

## 現況與優先順序

本地核對基準：`f703b46fd52dc1967ca3fd04946e08b8ea53b053` 加上既有未提交 UI 變更。核心依據如下。

| 現況 | 本地證據 | 判斷 |
|---|---|---|
| `SimulationWorld` 每次 `TickCore` 推進 0.1 s；移動前、移動後重算 safety，並多次刷新 occupancy | [`SimulationWorld.cs`](../src/MrtRouteSimulator.Engine/SimulationWorld.cs) 的 `TickCore` | 固定步進仍是物理與安全回歸基準；先量測重複工作，再調整快取。 |
| topology 前車查找對每列 follower 掃描其他 active train，逐對算 graph distance，按距離與 VehicleId 排序 | 同檔 `FindNearestTopologyLeader`、`TryGetTopologySafetyMetrics` | 典型候選產生近似 O(N²)；索引有研究價值，但分岔與折返不保證只看同 edge 前一車。 |
| `TopologyMovementOccupancyIndex` 持有車輛 footprint，`Set` 對其他 footprint 掃描重疊 | [`TopologyRuntime.cs`](../src/MrtRouteSimulator.Engine/TopologyRuntime.cs) | 索引應附著在既有 physical occupancy，不建立另一個位置權威。 |
| 資源 ID 由 `RouteResourceReservationManager` 原子預約、釋放；袋狀軌、越行與設施切換有特定流程 | [`InfrastructureModels.cs`](../src/MrtRouteSimulator.Engine/InfrastructureModels.cs)、`SimulationWorld.cs` | 它只排除請求相同資源的車，不能稱為所有主線衝突均受完整聯鎖保護。 |
| `SimulationPlaybackWorker` 是 actual world 單一寫入者；命令有序，`PlaybackFrame` 為不可變結果，容量一的 UI channel 丟棄舊 frame | [`SimulationPlaybackWorker.cs`](../src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs) | 原研究提案的 Phase 1 大部分已存在，應補驗證與收斂，不重做架構。 |
| `SimulationResultAccumulator` 以游標增量讀 events、trajectory、safety；結果分頁按目前 tab 與間隔刷新 | [`SimulationResultAccumulator.cs`](../src/MrtRouteSimulator.App/SimulationResultAccumulator.cs)、[`MainWindow.PlaybackRefresh.cs`](../src/MrtRouteSimulator.App/MainWindow.PlaybackRefresh.cs) | 優先 profile 殘餘耗時和記憶體，再決定是否進一步增量化。 |

### 最優先的三件事

1. **量測 topology 前車查找與 graph distance**：對 `ComputeSafetyObservations`、`FindNearestTopologyLeader`、`TryGetTopologySafetyMetrics` 計數與配置採樣，分開移動前、移動後與碰撞防護。保留現有全掃描作 oracle。
2. **在既有 occupancy 加入候選索引**：以 `TrackEdgeId`、實體 offset、方向及 footprint interval 取得候選，最後仍由原 `TopologyGraphDistance` 驗證真正的前方距離。先做 read-only 衍生索引與 shadow compare。
3. **減少同一狀態版本的重複幾何工作**：`TickCore` 目前刷新 occupancy 三次，且前車候選在 safety 與 collision 流程重查；先確認每次刷新前後的 train mutation，再建立版本化 footprint/候選快取，任何發車、位移、碰撞狀態、折返、passing 切換均立即失效。不可跨 tick 使用未驗證快取。

## Phase 1：鞏固現有 simulation / UI 分離

**目前已實作**：單一 actual-world worker、可靠命令、不可變 `PlaybackFrame`、latest-frame-wins、獨立 planned world、部分結果累積及隱藏分頁延後刷新。這與 DesRail 的 headless simulator → 播放產物 → visualizer 在責任分界上相近，但本專案支援即時互動，不能直接改為只能離線播放。

**補強**：明確標記 frame 的 world generation、tick/sequence 與完成狀態；測試 UI 丟 frame 後事件及結果游標仍完整；確認所有 WPF 結果頁只讀 frame 或獨立產物，不取 background mutable world。將 planned artifact 的來源、設定和取消行為納入 parity 測試。對 UI 的資料列更新先 profile，再決定是否進一步差異更新。

**驗收**：播放倍率、分頁切換、重設、取消 planned 計算、隨機丟 frame 後，actual world 的事件序列與離線 headless run 一致；UI thread 不接觸 mutable world；0.1 s tick 無遺漏。

## Phase 2：leader 與 occupancy 索引

建議先在 `TopologyMovementOccupancyIndex` 派生一個**查詢視圖**，每次物理狀態版本只建一次：

```text
ownerId -> RuntimeTopologyCursor + TopologyMovementFootprint
TrackEdgeId -> 依 offset 排序的 front / occupied intervals
(TrackEdgeId, direction) -> 同向車頭順序
movement plan / traversal -> 下游 edge 候選範圍
```

查詢從 follower 的 `RuntimeTopologyCursor` 出發，沿其合法 directed traversals，在制動視距與必要防撞範圍內蒐集**可能**相遇的車；同 edge、匯流後的共享 edge、反向共用軌與跨多 edge 車尾都要涵蓋。以物理 edge interval 限縮候選後，仍呼叫原本的 `TryGetTopologySafetyMetrics` 判定可達性、head distance 與 rear gap。若路徑重複 edge、分岔、passing merge、facility 轉向或候選範圍無法保證完整，暫時回退原全掃描。查詢視圖不決定列車位置、授權或碰撞，也不能使用 `ProjectedChainageMeters` 排序。

**關鍵正確性**：索引只可產生超集，不能漏掉現有全掃描會找出的 leader；距離相同時保留原 `VehicleId` tie-break。除了最近前車，碰撞防護與對向共用軌仍須保留所有必要 footprint overlap 檢查。預期降低典型同向主線的候選數，不能先宣稱全網最壞情況已降成 O(N log N)。

**驗收**：以原全掃描為 oracle，在每 0.1 s 比對 leader ID、head distance、rear gap、safety status、控制限速、碰撞、resource release 與完整事件順序。案例至少含雙向、發車密集、不同車長、同 edge 跨車尾、分岔/匯流、O04/O13 越行、O20 袋狀軌、尾軌折返，以及 28 站大案例。先記錄候選數與 fallback 比率，再比較耗時與 allocation。

`RouteResourceReservationManager` 的輸入資源及 `TopologyMovementOccupancyIndex` 的 footprint 要繼續從同一 topology cursor 派生；不要讓按方向排序的快取變成第二套 physical truth。

## Phase 3：受控平行

| 工作 | 分類 | 條件 |
|---|---|---|
| 不同 `SimulationWorld` 的離線 planned/actual run、批次情境 | **SAFE FOR THIS PROJECT** | 每個 world 單一寫入者，輸入設定不可變，輸出按明確 run ID 合併。需控制 CPU/記憶體競爭。 |
| 對不可變 frame/artifact 的純分析及匯出 | **SAFE FOR THIS PROJECT** | 分頁結果不得依賴 UI mutable state；輸出順序與取消語意固定。 |
| 單 tick 各車 read-only proposal，再集中 arbitration/commit | **POSSIBLE WITH REFACTOR** | 快照版本固定；提出 proposal 不得修改 world；依既有穩定順序處理資源、月台、越行、折返與事件；proposal 過期時重算。先證明同單執行緒結果等價。 |
| 分區 event processing | **POSSIBLE WITH REFACTOR** | 必須先界定跨區共享資源與合併順序；目前無此證據，不列近期工作。 |
| `Parallel.ForEach(_trains)` 直接修改 `SimulationWorld` | **NOT RECOMMENDED** | 破壞資源仲裁、事件順序、碰撞判定與決定性。 |
| 多 worker 同時更新同一 occupancy 或 reservation manager | **NOT RECOMMENDED** | 鎖只保證個別呼叫原子性，不能保證整個 tick 的營運順序。 |

## Phase 4：控制間隔與混合事件研究

[SUMO 的 step/action step 說明](https://sumo.dlr.de/docs/Simulation/Basic_Definition.html)證明「位置積分與決策頻率分離」在其車輛模型中存在；[安全說明](https://sumo.dlr.de/docs/Simulation/Safety.html)也指出 action 間隔超過反應時間可能導致碰撞。這是研究題目，並非本專案可直接套用的安全證明。

| 狀態/流程 | 建議時基 | 理由 |
|---|---|---|
| acceleration、braking、jerk、位置與速度積分 | 保留 0.1 s | 控制/連續性 regression 依相鄰固定 sample；粗化會改物理結果。 |
| moving block、rear-clear、碰撞與動態安全距離 | 每一必要 0.1 s 邊界檢查 | leader 或路徑可能在兩次「昂貴決策」間改變；任何降頻都需保守包絡與全案例證明。 |
| dwell 結束、派車到期、定時障礙物 | 可 event queue 排程到下一個 tick | 事件在固定 tick 兌現，排序與時間四捨五入規則保持一致。 |
| route lock / release、月台分配、passing entry、turnback ownership | 由狀態變更觸發，於固定 tick 原子仲裁 | 省去無變更時的搜尋有機會，但不能延後安全/資源釋放判定或改變 tie-break。 |
| expensive control decision | 僅 isolated prototype | 必須建立 lookahead/jerk/braking 的保守界，且逐 tick safety guard 永遠有效；先比較所有 trajectory 與事件，再決定是否值得進 production。 |

研究 prototype 最多兩項：`NearestLeaderIndexBenchmark` 與 control-interval shadow mode。兩者只讀現有 world 輸出或在隔離程式中建立對照 world，不改正式行為、Schema、sample。若前者已能達到效能目標，後者不必做。

## 量測與決策門檻

現有 [`PlaybackBenchmarks/Program.cs`](../tests/MrtRouteSimulator.PlaybackBenchmarks/Program.cs)可量 `elapsed`、`msPerTick`、記憶體、事件 hash；但預設是三站 sample，對大案例應明確傳 `--sample`。本次**未執行 benchmark**，附件的「約 2.75 ms/tick」是待重測假設。0.1 s tick 在 60× 的純計算時間預算是 `100 ms / 60 = 1.667 ms/tick`；這尚未扣除 UI、GC 與其他負載。

同機、同 Release 建置、同 sample/派車/retention、固定 8,000 s、預熱後至少五次，記錄 median 與離散度；分別量 actual、planned、UI rendering，不把三者混為單一數字。比較 `eventSha256` 之前還要檢查軌跡和安全紀錄逐項等價，特別是 0.1 s 相鄰速度差、jerk、停站、O04/O13/O20 事件與 rear-clear。大案例目前另有區間統計 parity 的已知驗收風險，不能只憑事件 hash 宣稱整體完成。

只有 profiling 顯示 leader/occupancy 是主要成本，且 shadow oracle 零差異、候選數明顯下降、重複量測改善足以覆蓋索引建置成本時，才進入 production 變更。涉及物理與安全的實作依專案規則執行 Release build、完整 Engine/WPF 自動化測試及針對邊界的 regression；桌面視覺驗收另外記錄。

## 授權界線

上述均為架構思想和獨立重新實作的研究方向，不複製任何 upstream 程式碼。比較報告逐案列出實際 LICENSE/COPYING 及未知處；本專案目前未找到根目錄 LICENSE，若要引入程式碼或資產，先釐清本專案授權與相依授權，再做正式審核。
