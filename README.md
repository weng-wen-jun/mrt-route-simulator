# MRT Route Simulator

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![Platform: Windows](https://img.shields.io/badge/Platform-Windows-blue)
![.NET 10](https://img.shields.io/badge/.NET-10-purple)

MRT Route Simulator is an offline Windows desktop application for conceptual metro and railway operations simulation. It is intended for planning exploration, operations analysis, education, prototyping, and conceptual engineering studies. The interface and detailed technical documentation are primarily in Traditional Chinese.

MRT 路線進出站時間模擬器是一套離線 Windows 桌面工具，用於拓樸路網、多列車營運與時刻表的概念研究；不是正式工程設計或可部署的鐵路安全系統。

**Safety boundary:** This is not a certified signaling, ATP, ATO, ATS, interlocking, or safety-critical system. Results reflect model assumptions and inputs, not certified capacity or real-world deployment validation.

## Why this project exists

The project explores an open and inspectable approach to metro operations simulation, with an emphasis on topology-aware train movement, operational scenarios, and reproducible analysis. It provides a local desktop workspace for studying how infrastructure, dispatch plans, and operating constraints interact.

## Key capabilities

- Topology-based infrastructure and bidirectional multi-train simulation.
- Dispatch plans, timetables, station stops, and skip-stop service patterns.
- Physical turnback, tail-track, pocket-track, and passing-facility scenarios.
- Infrastructure resource occupation and conceptual moving-block constraints.
- Acceleration, braking, and jerk modeling with fixed simulation substeps.
- Time-distance diagrams and CSV / PNG / PDF export.
- Versioned project files, sample scenarios, automated regression runners, and QA records.
- Local MCP automation in the unreleased V4.1.0 source; see [MCP documentation](docs/MCP.md).

## Quick Start

Requirements: Windows and the Microsoft .NET 10 Desktop Runtime. Building from source additionally requires the .NET 10 SDK.

1. Download and extract the Windows ZIP from [Releases](https://github.com/weng-wen-jun/mrt-route-simulator/releases/latest).
2. Run `MRT路線進出站時間模擬器.exe`.
3. Start with the built-in six-station scenario, or use **檔案 → 讀取存檔** (File → Load) to open an included sample.
4. Select **計算並建立模擬 / 建立模擬** (Build simulation), then **播放** (Play).
5. Inspect the result tabs and export from **列車運行圖／匯出** (Time-distance diagram / Export).

As checked on 2026-10-08, the latest published package is `MRT-route-simulator-V4.0.3-windows.zip`. This checkout is V4.1.0, not yet released; build it from source for the newer UI and MCP work. For current source samples, start with [the three-station baseline](samples/10-小型-三站完整拓樸基準範例.mrtsim.json) and consult [sample manifests](samples/README.md).

## Build from source

Run in the repository root on Windows:

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\src\MrtRouteSimulator.Mcp\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
```

The full solution includes MCP dependencies from nuget.org. Root `NuGet.Config` intentionally has no package sources and is not the fresh-checkout restore configuration for this solution. The application runs offline after dependencies are installed. Run the WPF regression runner with:

```powershell
dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- .
```

The built application is at `src/MrtRouteSimulator.App/bin/Release/net10.0-windows/MRT路線進出站時間模擬器.exe`. Build/test evidence and native-desktop limitations are recorded in [QA_REPORT.md](QA_REPORT.md); automated WPF checks do not replace DPI or compositor acceptance.

## Project documentation and contribution

See [CONTRIBUTING.md](CONTRIBUTING.md), [AGENTS.md](AGENTS.md), [MODEL_SPEC.md](MODEL_SPEC.md), [CHANGELOG.md](CHANGELOG.md), and [TODO.md](TODO.md). For private vulnerability reports, see [SECURITY.md](SECURITY.md). Contributions require the applicable builds, tests, repository-defined validation, and maintainer review. AI-assisted implementation, testing, refactoring, documentation, and review follow the same requirements.

## License and material boundaries

The [MIT License](LICENSE) covers project-authored code, tests, documentation, samples, configuration, and scripts that the authors have the right to license. Third-party dependencies retain their own licenses. Local `外部檔案參考/` materials are excluded from Git tracking and source archives; third-party PDFs, images, agency documents, and engineering drawings are not relicensed under MIT. Sample assumptions and source-data limitations are documented in [samples/README.md](samples/README.md).

## 中文操作與技術說明

> V4.1.0 為本機未發布版本；本輪 OSS 整理不修改模擬行為。外部參考檔保留於本機與獨立備份，不納入後續公開原始碼。來源遷移歷史見[遷移紀錄](docs/V410_CANONICAL_MIGRATION_20261008.md)，本輪公開範圍與申請前評估見[OSS 整理報告](docs/OSS_READINESS_20261008.md)。

> V4.1.0（2026-10-07，尚未發布）新增本機 MCP：可直接載入、修改與驗證專案、推進模擬、查詢結果，並透過 opt-in 桌面橋接控制播放、切頁、縮放及匯出。使用與驗證方式見 [MCP 文件](docs/MCP.md)。

> V4.0.3（2026-09-28）V2 寫實模擬使用 track-first topology runtime；Route 僅保留給 V1 解析模型與表單一次性起稿。

這是一套完全離線的 Windows WPF 桌面軟體，用來建立抽象捷運路線的列車運行、雙向派車、資源占用、結構化事件、區間統計與時間－里程運行圖。V2 的正式資料來源為 Schema 8 `InfrastructureGraphV4 + ServiceRoutes + VehicleTypes + ServiceTypes + StopPatterns + Dispatch`。

2026-09-28 Release build 為 `0 warnings / 0 errors`、Engine runner `185/185 tests`，完整 WPF runner 通過 14 個範例載入、大型工作區編輯往返、播放與 CSV／PNG／PDF 輸出。大型 full sample 在 60× 觀測約 59.0×、輸入最大間隔 41.7 ms 是 2026-09-25 V4.0.2 整合版的定向診斷數值，V4.0.3 尚未重跑這項診斷。原生桌面不同 DPI、8,000 秒連續播放，以及 O04／O13 越行與 O20 袋狀軌換端的關鍵畫面仍待人工驗收；離屏 WPF 診斷不等同桌面驗收。站場建置規則見 [站場建置規則](STATION_CONSTRUCTION_RULES.md)，範例修正見 [範例檢查表](samples/範例檢查表.md)，現行待辦見 [TODO.md](TODO.md)。

路線圖以起始站月台中心為0K，外側尾軌為負里程，終點外側接續終點中心里程。七種PDF站型的停點採車體中心定位，換端保持整列車占用不動；即時列車位置顯示「車體中心 km」。舊專案未指定停點基準時保留車頭定位，相容進路距離統計仍使用原本的進路投影。

## 直接使用

1. 開啟 V4.1.0 桌面程式或自行建置 Release 版本。
2. 雙擊 `MRT路線進出站時間模擬器.exe`。
3. 第一次可直接使用六站示範資料，按「計算並建立模擬」。
4. 從「檔案 → 讀取存檔」載入 [`samples/10-小型-三站完整拓樸基準範例.mrtsim.json`](samples/10-小型-三站完整拓樸基準範例.mrtsim.json)，或依 [`samples/README.md`](samples/README.md) 選擇合成路線、七種站場、折返或越行情境；所有範例均為 Schema 8。
5. 以標題列右側的播放控制播放、暫停或重設（任何分頁都可操作）；左側導覽列可切換到時刻表、區間物理、移動閉塞與列車運行圖等結果頁。
6. Schema 8 topology 專案使用「檔案 → 存檔」保存可編輯設定，並從結果頁匯出 CSV／PNG／PDF。「檔案 → 匯出完成後固定時刻表」只保留給 legacy Schema 7 相容動態模擬；讀入的 Schema 7 專案會先轉成 Schema 8，因此轉換後此選項停用。

本機需要 Microsoft .NET 10 Desktop Runtime。桌面模擬不需要帳號、資料庫或執行時網路連線。新增 MCP Host 使用官方 MCP C# SDK 與 .NET Hosting 套件，首次還原需連線至 nuget.org；正常 MCP 操作透過本機 stdio／named pipe。

## 介面縮放（本機軟體設定）

上方與「檔案」同一排的「顯示設定 → 介面縮放（軟體設定）」可選80%、90%、100%、110%、125%；100%為恢復預設。字體、按鈕與間距同步調整，程式工作區／設定視窗沿用，重新啟動仍保留。此偏好存在本機display-settings.json，不寫進路線存檔，也不改Windows DPI或模擬數據。小視窗仍放不下的速度圖可用垂直捲軸查看底部時間軸。

主視窗最低尺寸為800 × 520 DIP（Windows 150%時約1200 × 780像素），不會因介面縮放而重新提高最低尺寸。進入短視窗時路線摘要自動收合；仍可手動展開並捲動查看，不清除摘要資料。配合80～90%介面縮放及各圖表／資料表捲軸操作；不保證所有內容同時顯示。

預設開啟尺寸依所在螢幕可用工作區的90%置中，不採固定1440 × 900；依該螢幕的Windows DPI換算，工作區小於最低尺寸時仍以工作區為上限。短視窗可用最外層上下捲軸，讓產品標題及路線摘要隨整頁捲走。Route有水平溢位時，左右捲軸以頂層浮層貼齊路線圖下緣；下緣超出視窗時固定在可見區底部，不必先將上下捲到底。整張路線圖離開視窗或切到其他頁時才隱藏。

## 指定車次待避

在主畫面選「營運設定 → 停站模式」，選取停站模式後，於車站指令的「待避指定車次」欄填入快速車的**車次編號**（`ServiceRunId`），例如 `EXPRESS-DOWN-01`。這是單一班次的編號，不是服務類型或列車實體編號。若使用主畫面的快速輸入表單，同名欄位也可輸入，並會與專案工作區及存檔同步。

指定車次待避時，「停站秒數」是最低停留時間。本站必須有可執行的越行設施，指定車次須排在手動班表中、具越行資格，且其停站模式須在本站跨站通過。待避車達到最低停留時間且指定車次通過本站後，才進一步檢查越行車車尾淨空、進路資源及安全條件並放行。未填「待避指定車次」時沿用原本停站秒數與既有待避規則；讀取舊專案不會自動指定車次。

大型 28 站範例的 O04／O13 已示範此設定，最低停站時間為 20 秒。可在「檔案 → 讀取存檔」載入範例，再開啟「營運設定 → 停站模式」查看或修改。

## V4.0.3 Track-first topology

- `InfrastructureGraphV4` 的 physical truth 是 `TrackNode`、`TrackEdge`、edge-local `TrackPosition` 與附著其上的 `PlatformDefinitionV4`；車站不再是此新 topology 的實體端點。
- `LinearInfrastructureBuilder` 會把線性三站 A—B—C 建成 A→B、B→C 與 C→B、B→A 四條獨立 edge，而非兩條全線 edge；快速表單轉 Schema 8 時會補齊兩端實體尾軌與折返資源。
- `RouteProjection` 只提供單一 ServiceRoute 的累積 chainage 給過渡相容使用。它不是 global coordinate，也不能用來回推權威 `TrackPosition`；有 loop 時必須指定 traversal index。
- `SimulationWorld`、trajectory 與 event 以 `TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex` 作為 V2 權威 cursor；`PositionMeters` 只是 cursor 投影出的顯示快取。
- 正常主線、tail／pocket／turnback 與 passing facility 都依 `DirectedTrackTraversal` 實際推進；明確資源預約會在車尾 footprint 淨空後才釋放，衝突時列車等待。
- 大型 Schema 8 工作區提供車站、軌道、營運、模擬與設施作業編輯入口；大型 sample 的派車、站務、有向接續及偏好月台設定可往返保存，取消草稿不會取代目前專案。
- `DirectedTrackConnectionDefinition` 可在 switch／crossing node 明確限制「哪一個進入 traversal 可以接哪一個離開 traversal」；path finder、service route validator 與 runtime movement 共用同一限制，未宣告限制的普通節點仍依 edge 接續。
- 折返停點使用 edge-local `TrackPosition`；若停在 edge 中段，返回 traversal 必須由相同實體位置立即反向開始，runtime 不會用 virtual position 或 teleport 補接。
- 主畫面與 topology editor 的線路示意採鐵路配線圖風格：上下行維持固定間距，只有實際 `DirectedTrackConnection` 才畫轉向線；月台依 edge-local 起訖 offset 顯示為長色帶，尾軌沿抵達方向的主線股道直線延伸，並在 `BufferStop` 節點畫止衝。edge ID 改由 tooltip 查閱，不再壓在線路圖上。
- 正常主線的 station/platform stop 會先解析成 `ResolvedStop`（edge-local stop position、traversal index 與 chainage），進站煞車與到站吸附直接使用它。
- `TrackSpeedLimitService` 使用 `TrackSpeedLimitDefinition` 的 edge-local interval。
- 列車進入較低速的尾軌、袋狀軌或越行進路前，會沿實體 movement plan 預視前方軌道速限，依減速度與 jerk 提前煞車；速度不會在軌道交界直接截斷。
- `TopologySimulationDefinition` 是 V2 world 的正式輸入；world 不提供 compatibility Route 或 legacy InfrastructureGraph。
- 時刻表、區間統計、運行圖、CSV、PNG/PDF 匯出皆消費 topology 結果 context；投影 chainage 不參與物理或 safety。
- V4.0.3 的袋狀軌與站後尾軌折返會在實體折返停點等待接續班表並發出反向車次；返回正線月台後只執行設定的上下客停靠時間，時刻表以折返設施的發車事件作為反向車次起點。
- 互動 ActualWorld 以 0.5 秒間隔保留軌跡，另保留事件與狀態轉折。現行 CSV、區間統計及圖表使用這份互動歷史；獨立的 0.1 秒完整軌跡離線匯出仍列於 [TODO.md](TODO.md)。

## 驗收與限制

Engine 自動化與完整 WPF runner 已完成；大型 full sample 的 60× 定向 playback 數值屬 V4.0.2 整合版紀錄，V4.0.3 尚待重測。原生桌面不同 DPI、8,000 秒連續播放及 O04／O13／O20 關鍵畫面仍待人工驗收。詳見 [TODO.md](TODO.md) 與 [CHANGELOG.md](CHANGELOG.md)。本程式是營運與號誌概念模擬器，不是可部署的鐵路安全系統。

## V3.4.2 進站精停修正

以下 V3.3～V3.4.2 段落保留當時版本的功能沿革，並非現行 V4 的站場建置步驟。現行專案使用 Schema 8；尾軌、袋狀軌、折返與越行均由實體 topology edge、連續 traversal 及對應作業執行。完整版本沿革見 [CHANGELOG.md](CHANGELOG.md)。

- 排定停站由 Engine 的 `StationStopController` 統一決定目標速度：高速時依剩餘距離、Jerk 與營運煞車能力追蹤煞車曲線；預測全煞停點會越線時才要求營運煞車。
- 最後 12 m 切入低速精停，目標速度受 3 m/s 上限與距停車點 2 m 邊界共同約束；即使近站低速限制解除，也不會再按一般線速加速。
- 障礙物、越行及移動閉塞限制仍在其後取更嚴格者。本程式仍是概念模擬器，非 ATP／ATO 安全系統。

## V3.4.1 新增功能

- `Automatic` 會平衡可用同方向月台；「資源占用與觀測容量」結果頁與 CSV 依事件中的實際月台、進路、衝突區及尾軌資源顯示占用時間、使用率、每小時觀測預約數與最短釋放間距。
- 快速車會在下一個排定停靠站之前的跨站區段搜尋候選越行站；同站多候選依可用資源與進站位置選擇，上下行可同步使用各自資源完成越行。
- 車型、服務與派車可有不同停站模式；複製模式後修改不會影響原模式。五類車站型式範本與既有站場覆寫分開保存。
- 站前、站後與中央避車線折返都有連續軌跡；其中站前／中央避車線透過 `TURNBACK:<參考點>:OUT/RETURN` 分段軌道呈現橫渡線外出、停等與返回，路線圖直接依該軌跡播放。
- V1／V2 同條件比較頁與 CSV 逐站列出理論／實際到離站、停站、秒差及百分比。

## V3.4.0 新增功能

- 站前折返的 `alternateBerthing` 與站場 `RoundRobin` 策略會實際輪替目的月台。列車自出發起保留目的月台，到下次發車才釋放／換出；若 A、B 都被占用，後車會產生等待資源事件。
- 停站模式新增「折返」。將中央避車線建成路線中的虛擬站點、設為 `CentralSidingTurnback`，再把該站設成「折返」，即可讓區間車停靠、占用避車線資源並反向接續車次。
- 站後折返可在折返設定的股道清單指定一條下行、一條上行 `TailTrack`；兩線共同端點以 `TAIL:<折返設定編號>` 表示執行期虛擬節點。列車先依到達方向駛入尾軌、在該點停等，再改走反方向尾軌返回端點站接續新車次；未配置成對尾軌的既有設定維持原本的時間抽象行為。
- 寫實引擎的進站煞車鎖定與 2 m 近停吸附門檻一致，避免 Jerk 受限減速在停車點前解除煞車並短暫重新牽引。
- 當時的 `SimulationSession` 統一實際與計畫世界；`SimulationTraceRetentionPolicy` 可選完整、降採樣或僅事件，預設完整保留每個活動車輛的 0.1 秒樣本。現行 V4 互動播放改採上節所述的 0.5 秒留存策略。
- 當時的 legacy Schema 7 動態模擬在全部完成後可匯出 `.mrttimetable.json` 固定時刻表封存，保留專案設定與每個車次／車站的實際到離站、停站、誤點與狀態；重新讀取後直接顯示凍結結果。現行 Schema 8 topology 專案不提供此封存匯出。

## V3.3.0 基礎功能

- 明確分離「V1／V2 模擬引擎」與 V2 內部的「軌跡曲線」；切換引擎會停止播放、清除舊結果並要求重新建立。
- 車型、服務類型、停站模式集中目錄；固定合法值使用中文下拉選單，一般 UI 隱藏技術 ID，新項目由系統產生穩定 ID。
- 輸入對話框採草稿交易：按「確認」才套用，按「取消」不修改目前專案。
- 雙向簡易班距與手動班表，支援跨午夜排序、結構化車輛／車次識別、計畫／實際發車與延誤原因。
- 簡易班距與手動班表均可設定「端點折返續行」。續行列車完成端點處理後保留 `VehicleId`、服務類型、停站模式與車型，建立反向的新 `ServiceRunId`；未續行列車完成站點停站／清車秒數後退出，並從路線圖與安全計算消失。
- 手動發車計畫可指定「折返後接續車次 ID」，例如將下行第一車接到上行第六車；接續沿用相同 `VehicleId`，目標車次不另生成。若前一車次早到則等待，晚到則記錄延誤。
- 速度曲線以實體列車 `VehicleId` 選取完整行程，串接上下行、停站及折返車次，並標示換向位置；圖上有 km/h 數值刻度、時間刻度與圖例，線色與該列車在配線圖、運行圖上的顏色相同。
- 尚未播放時使用同一模擬會話的計畫軌跡預覽；播放後顯示所選列車已產生的實際軌跡。速限線讀取 Engine 當時記錄的方向與軌道速限。
- 移動閉塞仍可分上下行檢核，方向篩選同時作用於歷史配對、圖表與安全摘要；列車退出後仍可選取歷史配對，以「全部時間」查看較早資料。相鄰列車距離圖標出最低安全裕度，顏色與閉塞表狀態色點相同（需要制動為黃色）。
- 空間參考點支援中間站、站前折返、站後折返、中央避車線折返與銜接點；中間站可分別輸入順／逆行參數，確認後立即更新路線圖及 URCS 相容設計班距／容量提示。
- 折返參考點的距離、道岔速度、坡度與停等時間會傳入端點處理；未續行車次完成端點停站／清車後退出並從路線圖消失。
- 進出站時刻表、區間物理、移動閉塞、區間統計、速度曲線與運行圖均標示功能版本；運行圖同步標記營運與安全事件。
- 月台、股道、路徑、折返設施與站場模型，以及保守的資源預約、等待及釋放事件。
- V2 區間統計分離完成與運行中樣本，支援方向、車輛、車次、車型、服務、停站模式及模擬秒範圍篩選，並輸出移動閉塞受限秒數、平均／極值／P95 與中文明細／彙總 CSV。
- 基礎物理／實際營運模式切換；實際營運模式納入 Jerk、牽引力遞減、惰行及依目前速度、煞車能力與 Jerk 動態計算的進站煞車包絡線。
- 以累積里程設定任意區間速限，支援雙向、上行、下行、重疊取最低值及提前煞車。
- 多列車 `SimulationWorld`：車輛 ID、車次 ID、方向與軌道分離，所有內部控制固定逐步執行 `0.1 s` Tick。
- 停站／跨站／虛擬站折返服務模式：可為各車次指定列車等級、方向與模式；未列出的車站預設停靠，跨站車可另設車站通過速限，折返後也可換用不同模式。
- 獨立運行、移動閉塞監視與控制；預設採「控制（防追撞）」，並核對起點發車淨空、終點占用、Jerk 轉換距離與每 Tick 移動授權。
- 營運／緊急煞車估算切換，以及立即或排程的「前車障礙物急停」保守測試情境。
- 時間－里程列車運行圖：計畫／理論與模擬實際軌跡、方向／車輛／時間篩選、縮放、事件標記（綠：站點、灰：端點、紅：安全）及滑鼠提示。
- PNG（一般／高解析度）、PDF（A4／A3、橫向、可分頁，每頁保留標題與圖例）及 CSV 軌跡／事件匯出。
- 專案工作區（拓樸編輯器）與主視窗採同一套外觀：膠囊式分頁、卡片、主要／次要按鈕、驗證訊息色點（錯誤紅、提醒黃）；路線示意圖的軌道配色與配線圖相同。
- 版本化 `.mrtsim.json` 專案檔；讀取前會完整驗證，儲存時採同目錄暫存檔後原子取代，避免半成品覆蓋原檔。

## 使用方式

### 1. 編輯路線與列車

- 標題列「編輯」選單提供「快速起稿」、「軌道與設施」、「服務與路徑」與「模擬設定」；後三者直接開啟專案工作區的對應頁。結果頁由左側導覽列切換。
- 快速起稿是按需開啟的抽屜（導覽列底部「起稿」鈕或「編輯 → 快速起稿」）；建立或讀取 topology 專案後抽屜會關閉，「起稿」改為開啟專案工作區的快速起稿頁，取消時不套用草稿。
- 播放控制（建立、播放、暫停、重設、障礙物急停、倍率、時鐘）固定在標題列；輸入或讀檔錯誤顯示在頁首的警告橫幅。
- 工作區的驗證訊息可用滑鼠、Enter 或空白鍵開啟對應頁面；可辨識的物件及欄位會一併定位。輸入格式錯誤時會阻擋切頁與套用，取消則保留原專案。
- 配線圖採棕紅色軌道、方向箭頭及實心矩形月台，支線轉角平滑化。圖形仍依實際 topology 繪製；短月台設有最小顯示寬度，精確長度請看 tooltip，不能以圖上尺寸量測。

- 使用快速線性起稿表單時，第一站的「前站 km」必須為 `0`，後續各站填入與前一站距離；Schema 8 的實際軌道位置仍以 edge-local offset 為準。
- UI 的速度使用 km/h，Engine 統一使用 m/s、m/s²、m/s³、m 與 s。
- V2 首頁隱藏僅 V1 使用的列車數量、指定班距與首班時間；V2 改由發車計畫作為班次基準。播放倍率仍保留。
- 「停站進站上限」輸入 `0` 表示由動態煞車包絡線自動決定；正值才會作為停站列車的額外進站速度上限，不會套用到跨站車。
- 停站模式的逐站行為以「停站／跨站／折返」下拉選擇；跨站可另設通過上限。Schema 8 的中間站「折返」須配置對應的 `StationOperation`、`TurnbackOperation` 與實體折返進路；legacy 線性輸入則須對應 `CentralSidingTurnback` 空間參考點。未設定車次計畫時採「普通車／所有車站停靠」。
- 播放倍率只影響畫面更新節奏，V2 Engine 仍依序完成每一個固定子步進。

### 2. 儲存與讀取專案

- 「存檔」會保存目前可編輯的路線、車站、列車性能、折返時間、V2 參數、速限、服務模式、發車計畫及模擬模式。播放倍率為目前視窗的播放控制。
- 「讀取存檔」只在整份檔案通過格式與數值驗證後套用；格式錯誤、版本不支援或取消操作時，目前設定不會被替換。
- 新建、編輯與儲存的專案一律使用 `schemaVersion = 8`。合法 Schema 7 舊專案讀取後會轉成 Schema 8 topology draft；Schema 1～6 與未知版本會顯示不支援版本錯誤。
- 讀取 topology 專案後即可播放；「建立模擬」可重新建立模擬狀態。專案檔不保存播放到一半的瞬時狀態。
- legacy Schema 7 相容動態模擬若要保存完成後的固定結果，須先播放至所有列車結束營運，再選擇「檔案 → 匯出完成後固定時刻表」。此 `.mrttimetable.json` 封存可由「讀取存檔」重新導入。讀入的 Schema 7 專案會轉成 Schema 8 topology draft，轉換後該選項停用；請使用 Schema 8 存檔及結果頁匯出。

### 3. 設定里程速限

以下「起訖里程」操作適用快速線性起稿與 legacy 輸入；Schema 8 topology 編輯器改用軌道 edge 上的起訖 offset 設定 `TrackSpeedLimitDefinition`，模擬由 `TrackSpeedLimitService` 評估。

- 起訖里程必須使用 `0.01 km` 精度，位於 `0.00 km` 至全線終點，且起點小於終點。
- 方向由下拉選擇「雙向」、「上行」或「下行」。多筆速限重疊時採最低值。
- 速度曲線與路線動畫會顯示速限區間，實際營運模式會在低速區起點前提前煞車。

### 4. 使用移動閉塞與障礙測試

- 「監視（只告警）」不會替使用者介入列車控制；預設的「控制（防追撞）」會把移動閉塞允許速度及完整動態安全距離移動授權加入列車控制。
- 控制模式在前車尚未離開起點安全範圍時會延後後車發車；終點仍有列車折返占用時，後車會停在絕對最小淨距之外。
- 可在「移動閉塞與煞車」切換營運／緊急煞車估算、篩選配對與狀態。
- 「障礙物急停」是刻意讓指定前車瞬間停止的最不利測試，不是正常物理減速；事件會記錄時間、位置與車輛。

### 5. 檢視與匯出運行圖

- 可分別顯示計畫／理論虛線與模擬實際實線，並依方向、車輛及時間範圍篩選。
- 上方「運行圖篩選、縮放與匯出」可收合，設定不會因此清除；事件列表可另外收折。小視窗保留圖區最小高度，多出的內容可用整頁上下滑桿查看。
- 畫面本身即為匯出預覽；PNG、PDF、CSV 會透過 Windows 儲存對話框選擇位置。
- CSV 欄位包含車輛／車次、列車等級、服務模式、模擬時間、顯示時間、里程、速度、方向、軌道、狀態、前後站及事件類型。

## 軟體版本控制

- 目前來源版本為 `V4.1.0`（尚未發布），唯一版本來源是根目錄 `Directory.Build.props` 的 `MrtVersion`；正式發布版以 GitHub Releases 為準。
- 建置時會同步套用到組件、檔案、資訊版本、主視窗標題及測試標題。
- Bug 修正增加修訂版本；新功能或其他變更增加次版本並把修訂版本歸零；未經使用者明確要求不得升主版本。
- 完整規則見 `VERSIONING.md`，各版內容見 `CHANGELOG.md`。Git commit、tag 與 GitHub 推送只在使用者明確要求發布時執行。

## 開發與驗證

在專案根目錄執行：

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\src\MrtRouteSimulator.Mcp\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
```

主要檔案：

- `src/MrtRouteSimulator.Engine`：V1 解析模型、V2 topology-native `SimulationWorld`、實體設施 traversal、速限與分析服務。
- `src/MrtRouteSimulator.App`：WPF 桌面介面、圖形與離線匯出。
- [samples](samples)：14 份可直接執行的 Schema 8 topology 範例，依規模排序，涵蓋小型站場、中型路線、大型合成路線、折返與越行。序號僅供排序，不是版本號；案例資料界線見 [samples/README.md](samples/README.md)。
- `tests/MrtRouteSimulator.Tests`：目前 Engine runner 為 198/198 通過，包含大型 sample 分階段 gate、指定車次待避、完整 topology 情境、directed switch、physical turnback、rear-clear、Schema 8 編輯與結果資料流 regression。
- `Directory.Build.props`：軟體版本的單一來源。
- `VERSIONING.md`／`CHANGELOG.md`：進版規則與版本變更紀錄。
- [TODO.md](TODO.md)：現行改善需求、完成狀態與後續驗收界線。

## 使用與安全界線

本軟體是營運與號誌概念模擬器，不是可部署的鐵路安全系統，未取得 ATP／ATO／ATS、安全完整性等級或任何鐵路安全認證。V4.0.3 仍支援概念層級的多月台平衡、站內越行與進站精停；相關資源、容量與事件不是安全認證容量。未輸入特定路線資料時，結果只代表使用者輸入與程式假設。

目前預設上下行使用不同軌道；共用單線與聯鎖失效尚未建模。V4 的尾軌、袋狀軌、crossover、passing 與折返皆為實體 topology edge／traversal，但仍是概念性幾何，不是實際軌道平面圖；坡度、曲線阻力、黏著變化、乘客量與真實路線校準仍不在 V4.0.3 已完成範圍內，也不宣稱全線自動超車排程或全域營運最佳化。服務類型與車型維持分離目錄。

大型機場線（28 站大型範例）為抽象規劃，站碼與站心里程用於示範及回歸對照，並非實際線路里程。月台、車輛性能、停站時間、設施幾何、速限與派車均為示範假設，正式坡度與曲線資料尚未建模。未來若取得政府公開且可信的資料，將在確認使用條件後，註明來源、資料日期與更新範圍再逐步更新；目前不代表正式路線、工程設計或時刻表。

## 依 PDF 建立站場（2026-09-09）

在專案工作區「快速建立」的「依參考圖建立站場」選擇站型，按「以此站型重新起稿」，檢查後套用。此按鈕會替換工作區草稿；取消工作區會保留原專案。亦可直接讀取 `samples/README.md 所列範例`。

提供島式二股、側式二股、一島一側三股、二島四股、站後折返、站前折返與中央袋狀軌七種可執行範例。路線圖採上行在上、下行在下的靠右行駛配置，顯示月台編號、島式共用站體、渡線與尾軌止衝。

三／四股道範例含普通車待避與快速車越行；折返範例含同車反向接續。待避可在「營運設定 → 停站模式」的站點指令填入「待避指定車次」，以車次編號指定要等候跨站通過的快速車；停站秒數仍是最低停留時間。舊專案未填此欄時維持原停站秒數。營運頁可選 ServiceRoute 後「設為下行進路／設為上行進路」；袋狀軌的 `:BYPASS` 進路只停 A、C 站，折返的 `:PLATFORM2`／`:TAIL2` 須將上下行一併選為對應進路。模擬頁的「調整模擬與安全參數…」可設定起始時鐘、播放倍率、反應時間及安全間距。

PDF 提供站型與配置概念，軌長、速限、車型、停站時間及派車為可編輯示範值。共享袋狀軌採發車前保守預約、車尾淨空後放行對向車；示意位置與股道設定不參與運行距離或安全計算。
