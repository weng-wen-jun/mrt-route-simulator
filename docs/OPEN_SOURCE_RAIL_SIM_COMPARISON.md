# 開源鐵路模擬器與 MRT 專案架構比較

研究日期：2026-09-28。對象為六個有公開程式碼的 upstream：OSRD、Eclipse SUMO Railway、DesRail、NeTrainSim、TrainApp、TS2；其中 TrainApp 的自訂授權有商業散布限制，應稱為**可閱讀原始碼專案**，不預設符合 OSI 開源定義。比較本專案現況以本地工作分支 HEAD `f703b46fd52dc1967ca3fd04946e08b8ea53b053` 的 Engine/播放程式碼為準，**不是** `main`。本地 `origin/main` ref 為 `558e06b534f97c997f450379f747e7709528ac17`；本輪未 fetch，不能斷言它是最新遠端 main。工作樹另有未提交 UI 變更，未納入已驗收功能。此為原始碼與文件調研，未執行 upstream 情境，也未重新量測本專案效能。對「未查得」與「沒有此功能」保持區分。

## 結論先行

1. **最可轉化的架構**是 DesRail 的模擬器產出獨立播放產物、SUMO 的物理步進與控制決策間隔概念、以及 SUMO 的 rail signal/conflict/deadlock 模型。前兩者只能按本專案安全條件作增量研究；本專案的單一 writer、不可變 frame 和增量累積已實作。[DesRail simulator/animation](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/dr_anim.cpp)、[SUMO step 說明](https://sumo.dlr.de/docs/Simulation/Basic_Definition.html)、[本地 worker](../src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs)。
2. **前車索引值得 prototype**。本專案 topology safety 查找對每個 follower 掃所有 active topology train，重複呼叫 graph distance；但候選索引必須從 `TopologyMovementOccupancyIndex` 的實體 footprint 派生，保留 directed path、merge、passing、折返和跨 edge 車尾的全掃描回退。[本地查找](../src/MrtRouteSimulator.Engine/SimulationWorld.cs)、[SUMO 車輛 leader 查詢](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/src/microsim/MSVehicle.cpp#L3079-L3096)。
3. **純 DES 不宜取代 0.1 s 物理核心**。DesRail 和 TrainApp 的事件驅動適合發車、停站、section/resource 狀態，但本專案 jerk、moving block、碰撞、rear-clear 仍要逐固定步進驗證。SUMO 明確警告較長 action interval 可能造成碰撞。[SUMO Safety](https://sumo.dlr.de/docs/Simulation/Safety.html)、[本地 tick](../src/MrtRouteSimulator.Engine/SimulationWorld.cs)。
4. **不可把設計文件當已完成能力**。OSRD core 的 v1.2.21 README 仍列「列車互相反應的多車模擬」為未支援；其 signaling/interlocking 文件可借架構思想，不能拿來證明目前已具有完整多車 moving block。[OSRD core](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/README.md)、[signaling design](https://osrd.fr/en/docs/reference/design-docs/signaling/)。
5. **授權需逐檔核對**。六案包含 LGPLv3、EPL-2.0/GPL secondary、MIT 加 CC BY 資料、GPLv3、自訂 SFL、GPLv2；本專案根目錄未找到 LICENSE。此處只建議研究原理和獨立實作，不直接搬 code 或資料。詳見下表。

## 版本與可核對的程式碼

| 專案 | 本次版本錨點 | 程式碼證據 | 版本與證據界線 |
|---|---|---|---|
| 本專案 | HEAD `f703b46fd52dc1967ca3fd04946e08b8ea53b053` | [`SimulationWorld`](../src/MrtRouteSimulator.Engine/SimulationWorld.cs)、[`TopologyRuntime`](../src/MrtRouteSimulator.Engine/TopologyRuntime.cs)、[`SimulationPlaybackWorker`](../src/MrtRouteSimulator.App/SimulationPlaybackWorker.cs) | 未提交 UI 變更未作為 production 行為證據。 |
| OSRD | [v1.2.21，release 顯示 `2b935ee`](https://github.com/OpenRailAssociation/osrd/releases/tag/v1.2.21) | [`FullInfra.kt`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/src/main/kotlin/fr/sncf/osrd/api/FullInfra.kt)、[`TrackInfra.kt`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/kt-osrd-sim-infra/src/main/kotlin/fr/sncf/osrd/sim_infra/api/TrackInfra.kt)、[`StandaloneSimulation.kt`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/src/main/kotlin/fr/sncf/osrd/standalone_sim/StandaloneSimulation.kt)、[`SimulationEndpoint.kt`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/src/main/kotlin/fr/sncf/osrd/api/standalone_sim/SimulationEndpoint.kt) | 實際 core 與 signaling/Train Sim V3/timetable v2 **設計文件**分開看；後者不能直接當 release 實作。 |
| SUMO | [`v1_27_1` / `7717f2379d9e314a0c81c5cec748444de06a2a91`](https://github.com/eclipse-sumo/sumo/commit/7717f2379d9e314a0c81c5cec748444de06a2a91) | [`MSRailSignal.cpp`](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/src/microsim/traffic_lights/MSRailSignal.cpp)、[`MSDriveWay.cpp`](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/src/microsim/traffic_lights/MSDriveWay.cpp)、[`MSVehicle.cpp`](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/src/microsim/MSVehicle.cpp) | 官方網頁文件可能比 tag 新；精確實作以固定 SHA 為準。 |
| DesRail | [`main` / `b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4`](https://github.com/corryp/DesRail/tree/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4)，未見 tag | [`simulator.cpp`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/simulator.cpp)、[`desrail.cpp`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/desrail.cpp)、[`dr_anim.cpp`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/dr_anim.cpp) | deadlock learner 在獨立 `deadlock-paper` 分支，非此 main snapshot。[README](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/README.md)。 |
| NeTrainSim | [`main` / `23cd24f507ec7f2423a754e39f576819cf4a6808`](https://github.com/VTTI-CSM/NeTrainSim/tree/23cd24f507ec7f2423a754e39f576819cf4a6808)；另有 [v0.1.4](https://github.com/VTTI-CSM/NeTrainSim/releases/tag/v0.1.4) | [`train.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/traindefinition/train.cpp)、[`simulator.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/simulator.cpp)、[`simulatorapi.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/simulatorapi.cpp) | main 與 release 不同 snapshot，功能不可混稱。 |
| TrainApp | [`main` / `9f64b895d74d590505e62b6f4f52564a28abcf17`](https://github.com/sairam4123/TrainApp/tree/9f64b895d74d590505e62b6f4f52564a28abcf17)，未見 tag | [`graph.go`](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/railway/graph.go)、[`dispatcher.go`](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/railway/dispatcher.go)、[`des.go`](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/des/des.go) | 小型示範 codebase；不能以 README 自稱決定性或聯鎖完整性作證。 |
| TS2 | [client v0.7.10 / `ffc0f50b3011f01265e3dc70a1c955adaff1d45e`](https://github.com/ts2/ts2/tree/ffc0f50b3011f01265e3dc70a1c955adaff1d45e)；[server `9d0afe4905993a94ed460280004aaa7d3c54c41c`](https://github.com/ts2/ts2-sim-server/tree/9d0afe4905993a94ed460280004aaa7d3c54c41c) | [client `route.py`](https://github.com/ts2/ts2/blob/ffc0f50b3011f01265e3dc70a1c955adaff1d45e/ts2/routing/route.py)、[`service.py`](https://github.com/ts2/ts2/blob/ffc0f50b3011f01265e3dc70a1c955adaff1d45e/ts2/trains/service.py)、[server `routes.go`](https://github.com/ts2/ts2-sim-server/blob/9d0afe4905993a94ed460280004aaa7d3c54c41c/simulation/routes.go) | client 與 server 分開；以 dispatcher UX 為主，不作 physics benchmark。 |

## 主比較表

`?` 表示本次未取得足夠 source 證據；「部分」表示有局部機制，不能解讀為完整工程聯鎖或真實營運能力。詳細證據見下節；表格不以名稱相似推定語意相同。

| 項目 | 本專案 | OSRD | SUMO | DesRail | NeTrainSim | TrainApp | TS2 |
|---|---|---|---|---|---|---|---|
| Primary use case | 捷運 topology 寫實營運、WPF 播放 | 路網/時刻/路徑/容量平台 | 微觀交通與鐵路行車 | 鐵路營運 DES 與吞吐實驗 | 路網列車動力/能源 | 簡化調度/預約示範 | 人工號誌調度遊戲 |
| Infrastructure graph | `InfrastructureGraphV4` | RailJSON、core Raw/Block infra | node/edge/lane、rail signal | arc/segment/section/signal | node/link/signal | point/track/edge graph | track items 與預定義 signal routes |
| Track edge model | `TrackEdgeId + OffsetMeters` 實體權威 | route/block/track offsets，未證等價 | directed edge/lane | arc、section | link、列車占用 | track segment/edge | track item positions |
| Direction model | directed traversal + port-side | 方向化 route/block | 雙向需 superposed 反向 edge | signal arc / section direction | link/對向 signal controller | path/signal facing | point normal/reverse、train reverse |
| Timetable model | VehicleTypes + ServiceTypes + StopPatterns + Dispatch | train schedule/timetable；結果快取設計 | route/stops/arrival/departure | template + arrival/spawn 設定 | train/network input | train schedule points | ServiceLine、postActions |
| Time advance | 固定 0.1 s | standalone envelope 傳 `timeStep=2.0`；非多車全域 tick | 可配置固定 step；action interval | priority queue DES | 預設 1 s step | heap DES | server 驅動；精確物理步長未查 |
| Physics fidelity | 加減速、jerk、制動包絡 | 單車 envelope/阻力/時分 | car-follow/Rail model | 事件間移動參數與加速度 | 詳細牽引、阻力、能耗、jerk | 簡化區段旅行 | 遊戲化，不作主要參考 |
| Moving block | 動態安全距離、控制/監視 | 現行多車互動未確認 | rail signal 可切 moving-block + Rail car-follow | 未見等價模型 | leader gap 參與加速度；未證完整 moving block | 未見 | 未見 |
| Fixed block | 資源/占用，非完整信號閉塞 | block/zone 設計 | signal block | section lock | signal/link occupancy | whole-path reservation | signal-to-signal route |
| Signals | 未建完整實體號誌 | 模組化 signal 設計 | `MSRailSignal` | signal arc | signal group controller | signal facing 用於 path 排序，無完整 aspect | 互動號誌與 aspect |
| Interlocking | 特定 facility 資源排他，非完整聯鎖 | 設計文件；現行能力需分查 | driveway/conflict/foe + deadlock | section/signal lock | 對向 signal group；未證完整 | path + switch lock，有限且有風險 | 預定義 route/point 衝突抑制 |
| Resource reservation | ID 原子預約；rear-clear 釋放 | zone/route requirements 設計 | signal/driveway grant | section lock FIFO | link occupancy + signal request | whole path/MA queue | activated route |
| Platform assignment | runtime 選月台 | timetable/path 相關；詳細 runtime 未核 | stop edge、排程 | terminal/loop 設定；? | network/train input；? | 目標月台 whole-path reservation | service track/platform |
| Passing/overtaking | O04/O13 實體 passing + 指定等候 | 未查得等價單一機制 | 線路/號誌/排程可表達；需建網 | passing loop 由 arc priority + section lock 實現 | link/號誌路徑，未證專門超車 | 路徑可競爭，未見專門超車 | 人工設定 route 引導 |
| Turnback/reversal | 尾軌/袋狀軌/折返接續 | 回溯路徑有設計/開發；未證等價 facility | reversal 有列車長度與低速條件 | 路徑/terminal；? | train/network 路徑；? | schedule/path 轉換；? | 停車後 reverse、service postAction |
| Tail/pocket track | 實體 edge + footprint | 未查得專用等價 | 以一般 edge/signal 組合 | passing/terminal track；無本專案同名 contract | 一般 link；? | 一般 track；? | 一般 track item；? |
| Deadlock handling | 特定等待/釋放；全域 cycle detector 未查 | 衝突偵測；完整多車死鎖未證 | signal 預防常見環；偵測/處置選項 | main 未含 deadlock-paper learner；section 等待 | FIFO 等待；全域偵測未查 | queue 可能 starvation/卡住 | 人工調度；自動 deadlock 未查 |
| Collision prevention | footprint + moving block + collision stop | 單車/號誌設計；多車互動未支援 | block + Rail car-follow | section exclusive/constraint | pairwise geometry/shared link check | reservation/MA；未證完整 | 依 route/signal 的遊戲規則 |
| Determinism | 固定 tick 與穩定仲裁為目標 | 多車等價未適用 | 設定/threads 可影響重現 | event time + priority；tie 細節另核 | per-network worker；逐車 loop | map 初始順序 + heap 同時事件 tie 未保證 | 未查得正式保證 |
| Large network support | 28 站 sample；效能需量測 | 大路網服務架構 | 大型 network 工具與選項 | 有 CPU/throughput 實驗資料 | 宣稱 network-scale；缺本次可重現上限 | 候選路徑枚舉可能擴張 | 小區域 dispatcher UX |
| Simulation/UI separation | worker + immutable frame | core / editoast / front | `sumo` / `sumo-gui` | C++ sim → JSON → Python viz | core/Qt GUI + QThread | headless Go；viz 非核心 | client/server 分離 |
| Headless execution | Engine world / benchmark runner | core CLI/worker | `sumo` CLI | `desrail` CLI | core Simulator API | Go CLI | server 可獨立；互動導向 |
| Parallelism | planned 與 actual 不同 world 可獨立；同 world single writer | 模組載入/前端批次設計；多車 parallel 未證 | `--threads` 存在，upstream 警告無顯著 speedup | 單事件 queue；多 process/seed 未查 | 每 network QThread；每車 tick sequential | 單 heap loop | client/server；同 sim parallel 未證 |
| Result storage | events/trajectory/safety + immutable frame | simulation output cache 設計 | 多種 CLI output | `anim_script.json`/logs | trajectory、energy/summary | event/log | server state/client view |
| Visualization | WPF 即時播放/圖表 | web front | `sumo-gui` | 離線 Pyglet playback | Qt/QCustomPlot | optional visualization | PyQt signal panel |
| Desktop UI | WPF | web | sumo-gui | Python viewer | Qt | 無核心 desktop GUI | PyQt dispatcher |
| Export | CSV/PNG/PDF | API/資料輸出 | 多種 XML/統計 | JSON animation/log | trajectory/summary | log/dump | 模擬檔/客戶端 |
| Test strategy | Engine/WPF runner + sample regression | core checks/服務測試 | 大型 upstream tests | 實驗資料；自動測試範圍未查 | 原始碼/測試存在；覆蓋率未查 | 範例/測試程度有限 | 手冊/歷史遊戲測試；覆蓋率未查 |
| License | 根目錄未找到 LICENSE | LGPLv3，檔案層級另查 | EPL-2.0 + GPL secondary | code MIT，data/docs CC BY 4.0 | GPLv3 | 自訂 SFL | GPLv2 |

## 實作細節與可借界線

### OSRD：服務分層有價值，multi-train 能力不可高估

[core README](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/README.md)明列 core 負責路徑、單車運轉、近端號誌模擬與衝突偵測，支援 headless `standalone-simulation` 與 worker。實際 [`FullInfra.fromRJSInfra()`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/src/main/kotlin/fr/sncf/osrd/api/FullInfra.kt)依 `parseRJSInfra` → `loadSignals` → `buildBlocks` 建立 raw/signal/block infra；[`TrackInfra.kt`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/kt-osrd-sim-infra/src/main/kotlin/fr/sncf/osrd/sim_infra/api/TrackInfra.kt)使用 detector、方向化 track chunk 和 typed offset。單車 [`runStandaloneSimulation`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/src/main/kotlin/fr/sncf/osrd/standalone_sim/StandaloneSimulation.kt)建立 signaling range、MRSP/safety speed、speed/effort envelope；[`SimulationEndpoint.kt`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/core/src/main/kotlin/fr/sncf/osrd/api/standalone_sim/SimulationEndpoint.kt)載入 infra、建 train path、呼叫該模擬並序列化結果，對 envelope 傳入 `timeStep=2.0`。這是單車 envelope 參數，**不能推成全域多車 2 秒 tick**。[`CONTRIBUTING`](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/CONTRIBUTING.md)把 core、資料/API 的 editoast、前端 front 分開。[signaling design](https://osrd.fr/en/docs/reference/design-docs/signaling/)與[simulation lifecycle](https://osrd.fr/en/docs/reference/design-docs/signaling/simulation/)把靜態 block、動態 zone occupancy、signal driver 和 route requirements 分層；[timetable v2 設計](https://osrd.fr/en/docs/reference/design-docs/timetable/)提到 schedule、可重算輸出與批次 projection。後三者屬設計描述。core README 同時列出「train reacts to surroundings 的 multi-train simulation」未支援；不能把設計文件推成現行 moving-block engine。對本專案的價值是**physical infrastructure、營運規劃、結果產物、UI 邊界**，而非替換已有多車 runtime。

### SUMO：兩種時基與 rail signal 的明確實作

[Railways](https://sumo.dlr.de/docs/Simulation/Railways.html)說明 bidirectional rail 以兩個重疊反向 edge 表達；`MSRailSignal` 和 [`MSDriveWay.cpp`](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/src/microsim/traffic_lights/MSDriveWay.cpp)處理 block、共用軌、foe/conflict 及死鎖關係。moving-block 模式取消一般同 block 排他的一部分，改由 Rail car-follow 保持車距，但交會/對向衝突仍需號誌。[`MSVehicle.cpp`](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/src/microsim/MSVehicle.cpp#L3079-L3096)顯示一般車輛 gap-control 路徑使用 `getLeader`；它不是單獨可證的 rail signal leader API。[Basic Definition](https://sumo.dlr.de/docs/Simulation/Basic_Definition.html)與[Safety](https://sumo.dlr.de/docs/Simulation/Safety.html)說明位置每 simulation step 推進，action step 可較疏，但會切換 ballistic integration，且反應時間不足可能撞車。本專案只能 prototype 控制計算降頻，不能照搬頻率或把每 tick safety 取消。SUMO 的死鎖預防處理常見 2–3 車環，複雜環與 loaded constraints 仍有限制；teleport/remove 是其情境處置，對本專案不適合作正常營運解法。[SUMO `--threads` 選項](https://sumo.dlr.de/docs/sumo.html)也不構成安全多車平行的證據。

### DesRail：事件核心與離線結果分離

[`simulator.cpp`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/simulator.cpp)的事件 queue 先比時間再比 priority；`Sim::step()` 推進下一事件並恢復 coroutine，`SimCondition`/`delay`/`sleep`/`wakeup` 表達等待與喚醒。[`desrail.cpp`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/desrail.cpp)有 `FREE/LOCKED/LOADED` segment、由 signal arcs 建構 section 的網路處理，以及 `SignalsManager::request_access()` 的 section 鎖與 FIFO 等候。passing loop 由 arc priority、section lock、signal/occupancy constraint 組成，不是通用的「超車演算法」；現有 source 仍有 train prioritisation TODO。模擬器透過[`dr_anim.cpp`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/DesRail/dr_anim.cpp)輸出 `anim_script.json`，後由[`DesRailAnim.py`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/Animation/DesRailAnim.py)離線播放；visualizer 依記錄命令和 timestamp 繪製，不回寫 simulation physics。可借的是**結果 artifact 契約**、resource wait/release 事件以及 headless 批次跑法；不可將其純 DES 直接代替本專案連續 jerk/moving-block tick。

### NeTrainSim：物理/能源參考與 per-network worker

[`train.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/traindefinition/train.cpp)依 leader gap、critical points、阻力與 jerk 等決定加速度，預設 1 秒 timestep；[`energyconsumption.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/traindefinition/energyconsumption.cpp)及[`locomotive.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/traindefinition/locomotive.cpp)計算牽引消耗、再生制動與電池/catenary 能量。這是未來物理校準的參照，不能假定本專案的示範車型、坡度與曲率已有真實資料。[`simulatorapi.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/simulatorapi.cpp)對**每個 network**配置 QThread；[`simulator.cpp`](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSim/simulator.cpp)仍在一個 network tick 內逐 train loop。`QtConcurrent` 相依不等於已平行修改同一 world。其 link occupancy、signal group FIFO 與 pairwise collision 是局部機制，未見可直接移植的完整聯鎖。[GUI](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/src/NeTrainSimGUI/gui/netrainsimmainwindow.cpp)以 Qt signal/slot 消費結果與控制暫停，提供速度、能耗、坡度/曲率等圖。

### TrainApp：path 預約可作反例與測試靈感

[`graph.go`](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/railway/graph.go)枚舉候選 path；[`interlocking.go`](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/railway/interlocking.go)依 signal 朝向與距離排序、整條路徑預約 track 並 lock switch；[`dispatcher.go`](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/railway/dispatcher.go)在釋放 track 後重試等待 route/MA。它比本專案的 `RouteResourceReservationManager` 多明確 movement authority/dispatcher 流程，卻也以全路徑預約鎖定過長，缺公平性。`des.go` 的 heap 僅比較 event time，`sim.go` 從 Go map 排初始事件，同時事件的決定性未證明；不能直接照搬其仲裁。與本專案 `WaitingForResource`/`RouteReserved`/`RouteReleased` 對照，可借「等待原因與喚醒觸發事件」的可觀測性，不能把本專案特定 facility 資源鎖當作完整 interlocking。

### TS2：dispatcher UX，而非 physics 範本

[`route.py`](https://github.com/ts2/ts2/blob/ffc0f50b3011f01265e3dc70a1c955adaff1d45e/ts2/routing/route.py)持有兩 signal 間預先定義的 route 與 point normal/reverse 狀態；[server `routes.go`](https://github.com/ts2/ts2-sim-server/blob/9d0afe4905993a94ed460280004aaa7d3c54c41c/simulation/routes.go)管理 Deactivated/Activated/Persistent/Destroying。其[README](https://github.com/ts2/ts2/blob/ffc0f50b3011f01265e3dc70a1c955adaff1d45e/README.md)描述 signal-to-signal 點選、高亮與取消；[`service.py`](https://github.com/ts2/ts2/blob/ffc0f50b3011f01265e3dc70a1c955adaff1d45e/ts2/trains/service.py)的各站時刻和 `REVERSE`/`SET_SERVICE` postAction 可提供編輯器 UX 參考。[server `trains.go`](https://github.com/ts2/ts2-sim-server/blob/9d0afe4905993a94ed460280004aaa7d3c54c41c/simulation/trains.go)的 reverse 要求先停車，再交換 head/tail 並重建號誌動作；`IsShunting()` 目前固定 false，不能把歷史 changelog 的 shunting 當完整現行能力。

## A. Fixed tick、DES、hybrid：逐行為判斷

| 行為 | 本專案安全的起點 | 可研究的事件化/降頻 | 不可跳過的驗證 |
|---|---|---|---|
| acceleration、braking、jerk | 0.1 s 積分 | 預計算不變的限制/前視資料 | 相鄰 0.1 s 速度、加速度、jerk 與停站邊界 |
| moving block/leader | 每 tick 更新/保護 | 用索引減候選、shadow mode 試控制頻率 | 每 tick 最短 gap、安全狀態、控制限速、碰撞 |
| dwell/dispatch | 在 tick 到期執行 | min-heap 排下一到期事件 | 同時事件 tie-break、0.1 s 對齊、ServiceRunId |
| route lock/resource release | 每 tick 維持可觀測狀態與 rear-clear | 狀態變更時喚醒等待者 | footprint 車尾淨空、鎖定/釋放事件順序 |
| passing/turnback | physical movement plan + 0.1 s | 排定等待/接續事件 | directed traversal、平台/袋狀軌所有權、車身跨 edge |

**判斷**：physics/safety 留 fixed tick；派車/停站/等待通知可混合事件佇列，但事件只能在 tick 邊界以穩定順序生效。DesRail 的純 DES 對 section throughput 有價值，無法單靠它保持本專案完整的 jerk 制動與 moving-block trajectory。SUMO action step 需要保守 braking envelope、jerk、station snap、rear-clear、resource arbitration 的逐 tick parity；不能把「車輛平均動作間隔」直接套成安全決策降頻。

## B. Moving block / nearest leader

本專案 [`FindNearestTopologyLeader`](../src/MrtRouteSimulator.Engine/SimulationWorld.cs)對每個 active follower 篩所有 active topology train，對每對呼叫 `TryGetTopologySafetyMetrics`/`TopologyGraphDistance`，並以 head distance、VehicleId 決定最前候選；`ComputeSafetyObservations` 與 `ApplyCollisionProtection` 都會用它。[`TopologyMovementOccupancyIndex`](../src/MrtRouteSimulator.Engine/TopologyRuntime.cs)持有 footprint，但目前 `Set` 也逐 owner 比對 overlap。SUMO 的 `getLeader()` 說明候選查找與 car-follow 決策分開；OSRD 本次未找到現行 production nearest-leader API。NeTrainSim 仍有 pairwise collision 掃描。可提出的索引為 `(TrackEdgeId, direction) → offset-ordered front/interval list`，加 `ownerId → RuntimeTopologyCursor/footprint`，沿 follower 的 directed movement plan 收集可能共路的候選，再用現有 graph-distance 真值函式確認。跨 edge 車尾、匯流、對向、passing、loop/repeated edge 必須保守納入或回退原掃描。具體資料結構、shadow oracle 與驗收條件見[架構建議 Phase 2](OPEN_SOURCE_ARCHITECTURE_RECOMMENDATIONS.md)。**預估收益只可表述為減少典型候選數；本次沒有實測 ms/tick 收益。**

## C. Simulation / UI separation

DesRail 最清楚：C++ 模擬完成後寫 `anim_script.json`，Python viewer 僅播放；SUMO 有 headless CLI 與 GUI；OSRD 有 core/editoast/front；NeTrainSim 有 per-network worker 和 Qt GUI，但 GUI 可控制 pause/resume。這些架構均未提供「UI 可以跳過 physics tick」的依據。本專案已由 `SimulationPlaybackWorker` 單獨持有 actual world、不可變 `PlaybackFrame` 把狀態交給 UI、容量 1 channel 可丟舊 frame；`PlannedTimelineWorker` 跑獨立 planned world，`SimulationResultAccumulator` 增量消費歷史。建議加強所有結果分頁只讀 frame/artifact 的稽核、frame drop 後游標完整性與 export parity，避免重做已完成的 Phase 1。

## D. Parallelism 可行性

| 方向 | 判斷 | 上游依據與本專案限制 |
|---|---|---|
| 不同 world 的獨立情境與 planned/actual 離線計算 | **SAFE FOR THIS PROJECT** | NeTrainSim 每 network QThread；本專案已分 actual/planned world。需限制資源競爭並固定輸出排序。 |
| immutable artifact 的分析與匯出 | **SAFE FOR THIS PROJECT** | DesRail 離線 viewer；資料不可變後才平行，不依賴 WPF mutable state。 |
| 每車 read-only proposal + 單點 deterministic arbitration | **POSSIBLE WITH REFACTOR** | 上游沒有可直接套用的同 world 證明；需鎖定同 tick 快照並集中處理資源/平台/越行/折返。 |
| 路網分區事件處理 | **POSSIBLE WITH REFACTOR** | 跨區共用軌及 route lock 須有全域順序；DesRail queue 不等於已平行。 |
| `Parallel.ForEach(_trains)` 修改 world | **NOT RECOMMENDED** | 會讓 reservation、collision 與 event order 取決於 thread interleaving。 |
| 因 SUMO 有 `--threads` 就直接採多車平行 | **NOT RECOMMENDED** | upstream 自身說多 thread 有問題且缺顯著加速；不能視為安全證據。 |

## 可借鑑分類與優先五項

| 類別 | 對應模組 | 理由與收益估計 |
|---|---|---|
| **A 已可採用的思想：1. headless core + result artifact 契約** | `SimulationWorld`、`SimulationPlaybackWorker` | 本專案已有基本邊界；補一致性與可重播 metadata，降低 UI cadence 對正確性的干擾。收益偏可靠度，未量測效能。 |
| **A：2. 以單一 physical cursor 派生 occupancy/衝突查詢** | `TopologyMovementOccupancyIndex` | 避免第二套 chainage truth；可供 leader、rear-clear、月台與資源分析共用。收益需 prototype 量測。 |
| **A：3. 結構化 resource wait/grant/release 可觀測事件** | `RouteResourceReservationManager`、`SimulationWorld` | 可釐清等待原因、死鎖風險及釋放時點；DesRail/TrainApp 均有類似狀態流。 |
| **A：4. 隱藏 tab/結果增量消費** | `SimulationResultAccumulator`、`MainWindow.PlaybackRefresh` | 已實作，補 frame-drop、匯出與重設 parity；先 profile 殘餘成本。 |
| **A：5. dispatcher UX 顯示進路與等待原因** | WPF topology/editor | TS2 操作方式有價值；仍須綁定本專案權威 `ServiceRoute`、physical resource。 |
| **B prototype：nearest-leader / spatial ordering** | occupancy index + safety | 近似 O(N²) 候選可能縮減；以全掃描 shadow oracle 驗證。 |
| **B prototype：control/action interval** | `SimulationWorld` 安全控制 | SUMO 只證明概念可行；本專案需逐 tick 保守安全與完整軌跡等價。 |
| **B prototype：event wakeup / resource delta** | reservation/dispatch | 降低無變更掃描，不能延遲 rear-clear 或改仲裁順序。 |
| **C 不適合：純 timetable DES 替換 physics** | 核心 | 丟失 0.1 s jerk、moving block、碰撞與精確站停。 |
| **C：全路徑先鎖定** | resource arbitration | TrainApp 顯示占用過早、可能 starvation；與本專案局部進路釋放需求不合。 |
| **C：teleport/remove 當一般死鎖解法** | runtime | SUMO 有此選項，但會破壞本專案真實列車軌跡/時刻。 |
| **C：直接逐車平行 mutable world** | core | 破壞決定性與資源仲裁。 |
| **C：把投影里程當 physical order** | leader/occupancy | passing、折返與雙向分支會得到錯誤鄰車。 |

## License 與程式碼使用界線

此表只記錄查到的授權文字與待審核事項，**不是法律意見**。本專案根目錄未找到 `LICENSE`，故「可否直接複製」均不作相容性承諾；即使 MIT 程式碼，也要先確定來源檔案、notice/attribution 與本專案預定授權。架構原理可研究，實作時自行撰寫。

| Project | License 原始證據 | 可否直接複製 code | 可否研究架構 | 備註 |
|---|---|---|---|---|
| OSRD | [README LGPLv3](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/README.md)、[LICENSES](https://github.com/OpenRailAssociation/osrd/tree/v1.2.21/LICENSES)、[REUSE.toml](https://github.com/OpenRailAssociation/osrd/blob/v1.2.21/REUSE.toml) | **Requires Legal Review** | 可 | 各檔可能有不同 SPDX/第三方授權；不能只看根 README。 |
| SUMO | [`LICENSE` EPL-2.0 + GPL-2.0-or-later secondary](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/LICENSE)、[`NOTICE.md`](https://github.com/eclipse-sumo/sumo/blob/7717f2379d9e314a0c81c5cec748444de06a2a91/NOTICE.md) | **Requires Legal Review** | 可 | NOTICE 包含不同第三方程式/資料授權。 |
| DesRail | [`LICENSE`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/LICENSE)、[`ATTRIBUTION`](https://github.com/corryp/DesRail/blob/b714668c76e1bcbe73ee8d9a2a5d5f95e518abd4/ATTRIBUTION) | **Requires Legal Review** | 可 | code/binary MIT；data/figures/docs/notebooks CC BY 4.0；第三方材料另依原條件。 |
| NeTrainSim | [`LICENSE` GPLv3](https://github.com/VTTI-CSM/NeTrainSim/blob/23cd24f507ec7f2423a754e39f576819cf4a6808/LICENSE) | **Requires Legal Review** | 可 | 物理公式/模型概念可研究，不直接搬實作。 |
| TrainApp | [`LICENSE` 自訂 SFL](https://github.com/sairam4123/TrainApp/blob/9f64b895d74d590505e62b6f4f52564a28abcf17/LICENSE) | **Requires Legal Review** | 可 | 有命名、散布與商業限制；不能當成 MIT。其商業限制與 [OSI 開源定義的自由再散布及用途不歧視條款](https://opensource.org/osd)不符，故本報告僅把它當可讀 codebase 參考。 |
| TS2 client/server | [client `COPYING` GPLv2](https://github.com/ts2/ts2/blob/ffc0f50b3011f01265e3dc70a1c955adaff1d45e/COPYING)、[server `LICENSE`](https://github.com/ts2/ts2-sim-server/blob/9d0afe4905993a94ed460280004aaa7d3c54c41c/LICENSE) | **Requires Legal Review** | 可 | signal icons、資料與依賴亦應逐項看授權。 |

## 限制與後續驗收

- 本次沒有 build/test/benchmark，也沒有 upstream 實機重現；矩陣中的 `?` 與「未查」不應變成未支援的定論。可選 prototype 本輪未做，避免在未建立 oracle 與基準前碰 production physics。
- 研究附件提到約 2.75 ms/tick 與 60× 需求；前者本輪未重測，後者在 0.1 s tick 的理論計算預算是 1.667 ms/tick。性能採納門檻見[架構建議](OPEN_SOURCE_ARCHITECTURE_RECOMMENDATIONS.md)。
- 若未來要實作，先建全掃描對照、同 tick event/trajectory/safety parity、O04/O13/O20 與完整大 sample regression，再評估索引收益；詳見[Phase 1–4 roadmap](OPEN_SOURCE_ARCHITECTURE_RECOMMENDATIONS.md)。
