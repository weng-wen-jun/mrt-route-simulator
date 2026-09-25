# MRT 路線進出站時間模擬器 V4.0.2

> V4.0.2（2026-09-25 整合版）V2 寫實模擬使用 track-first topology runtime；Route 僅保留給 V1 解析模型與表單一次性起稿。

這是一套完全離線的 Windows WPF 桌面軟體，用來建立抽象捷運路線的列車運行、雙向派車、資源占用、結構化事件、區間統計與時間－里程運行圖。V2 的正式資料來源為 Schema 8 `InfrastructureGraphV4 + ServiceRoutes + VehicleTypes + ServiceTypes + StopPatterns + Dispatch`。

目前整合分支的 Engine runner 為 `173/173 tests`、Release build `0 warnings / 0 errors`（2026-09-25）；完整 WPF runner 已通過範例載入、playback worker、速度行程與 CSV／PNG／PDF 輸出，另大型 full sample 定向 playback 診斷在 60× 觀測約 59.0×、輸入最大間隔 41.7 ms。原生桌面不同 DPI 與連續播放仍未人工驗收。站場建置的阻擋規則、版面提示及一鍵驗收入口見 [站場建置規則](STATION_CONSTRUCTION_RULES.md)，逐檔修正見 [範例檢查表](samples/AUDIT-2026-09-11.md)；完整驗收邊界見 `QA_REPORT.md`。

路線圖以起始站月台中心為0K，外側尾軌為負里程，終點外側接續終點中心里程。七種PDF站型的停點採車體中心定位，換端保持整列車占用不動；即時列車位置顯示「車體中心 km」。舊專案未指定停點基準時保留車頭定位，相容進路距離統計仍使用原本的進路投影。

## 直接使用

1. 開啟 V4.0.2 桌面程式或自行建置 Release 版本。
2. 雙擊 `MRT路線進出站時間模擬器.exe`。
3. 第一次可直接使用六站示範資料，按「計算並建立模擬」。
4. 從「檔案 → 讀取存檔」載入 [`samples/V4.0.0-topology-baseline.mrtsim.json`](samples/V4.0.0-topology-baseline.mrtsim.json)，或依 [`samples/README.md`](samples/README.md) 選擇大型機場線、PDF 站型、折返或越行情境；所有範例均為 Schema 8。
5. 在「模擬動畫」播放、暫停或重設；其他分頁可查看時刻表、區間物理、移動閉塞與列車運行圖。
6. 模擬全部完成後，從「檔案 → 匯出完成後固定時刻表」建立可重複讀取的封存檔；從「檔案」可存取專案或重新讀取封存。

本機需要 Microsoft .NET 10 Desktop Runtime。本專案不使用帳號、資料庫、遙測或執行時網路連線，也沒有第三方 NuGet 套件。

## V4.0.2 Track-first topology

- `InfrastructureGraphV4` 的 physical truth 是 `TrackNode`、`TrackEdge`、edge-local `TrackPosition` 與附著其上的 `PlatformDefinitionV4`；車站不再是此新 topology 的實體端點。
- `LinearInfrastructureBuilder` 會把線性三站 A—B—C 建成 A→B、B→C 與 C→B、B→A 四條獨立 edge，而非兩條全線 edge；快速表單轉 Schema 8 時會補齊兩端實體尾軌與折返資源。
- `RouteProjection` 只提供單一 ServiceRoute 的累積 chainage 給過渡相容使用。它不是 global coordinate，也不能用來回推權威 `TrackPosition`；有 loop 時必須指定 traversal index。
- `SimulationWorld`、trajectory 與 event 以 `TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex` 作為 V2 權威 cursor；`PositionMeters` 只是 cursor 投影出的顯示快取。
- 正常主線、tail／pocket／turnback 與 passing facility 都依 `DirectedTrackTraversal` 實際推進；resource 會在車尾 footprint 淨空後才釋放。
- `DirectedTrackConnectionDefinition` 可在 switch／crossing node 明確限制「哪一個進入 traversal 可以接哪一個離開 traversal」；path finder、service route validator 與 runtime movement 共用同一限制，未宣告限制的普通節點仍依 edge 接續。
- 折返停點使用 edge-local `TrackPosition`；若停在 edge 中段，返回 traversal 必須由相同實體位置立即反向開始，runtime 不會用 virtual position 或 teleport 補接。
- 主畫面與 topology editor 的線路示意採鐵路配線圖風格：上下行維持固定間距，只有實際 `DirectedTrackConnection` 才畫轉向線；月台依 edge-local 起訖 offset 顯示為長色帶，尾軌沿抵達方向的主線股道直線延伸，並在 `BufferStop` 節點畫止衝。edge ID 改由 tooltip 查閱，不再壓在線路圖上。
- 正常主線的 station/platform stop 會先解析成 `ResolvedStop`（edge-local stop position、traversal index 與 chainage），進站煞車與到站吸附直接使用它。
- `TrackSpeedLimitService` 使用 `TrackSpeedLimitDefinition` 的 edge-local interval。
- `TopologySimulationDefinition` 是 V2 world 的正式輸入；world 不提供 compatibility Route 或 legacy InfrastructureGraph。
- 時刻表、區間統計、運行圖、CSV、PNG/PDF 匯出皆消費 topology 結果 context；投影 chainage 不參與物理或 safety。

## 驗收與限制

Engine 自動化、完整 WPF runner 及大型 full sample 的定向 playback 診斷已完成；原生桌面不同 DPI／連續播放驗收仍在進行。詳見 [`TODO.md`](TODO.md) 和 [`QA_REPORT.md`](QA_REPORT.md)；桌面匯出範例位於 [`artifacts`](artifacts)。本程式是營運與號誌概念模擬器，不是可部署的鐵路安全系統。

## V3.4.2 進站精停修正

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
- Engine 以 `SimulationSession` 統一實際與計畫世界；Schema 8 topology 互動播放的 ActualWorld 使用 0.5 秒 trajectory retention，並保留事件、相位、方向、edge／traversal、站點、月台與 constraint 狀態轉折。CSV／區間統計目前消費這份互動歷史；真正 0.1 秒 Full trajectory 的離線匯出尚未與互動留存分離，詳見後續 TODO。
- 模擬全部完成後，可匯出 `.mrttimetable.json` 固定時刻表封存。封存保留完成當下的 Schema 7 專案設定與每個車次／車站的實際到離站、停站、誤點與狀態；重新讀取後直接顯示凍結結果，不會重算。

## V3.3.0 基礎功能

- 明確分離「V1／V2 模擬引擎」與 V2 內部的「軌跡曲線」；切換引擎會停止播放、清除舊結果並要求重新建立。
- 車型、服務類型、停站模式集中目錄；固定合法值使用中文下拉選單，一般 UI 隱藏技術 ID，新項目由系統產生穩定 ID。
- 輸入對話框採草稿交易：按「確認」才套用，按「取消」不修改目前專案。
- 雙向簡易班距與手動班表，支援跨午夜排序、結構化車輛／車次識別、計畫／實際發車與延誤原因。
- 簡易班距與手動班表均可設定「端點折返續行」。續行列車完成端點處理後保留 `VehicleId`、服務類型、停站模式與車型，建立反向的新 `ServiceRunId`；未續行列車完成站點停站／清車秒數後退出，並從路線圖與安全計算消失。
- 手動發車計畫可指定「折返後接續車次 ID」，例如將下行第一車接到上行第六車；接續沿用相同 `VehicleId`，目標車次不另生成。若前一車次早到則等待，晚到則記錄延誤。
- 速度曲線以實體列車 `VehicleId` 選取完整行程，串接上下行、停站及折返車次，並標示換向位置。
- 尚未播放時使用同一模擬會話的計畫軌跡預覽；播放後顯示所選列車已產生的實際軌跡。速限線讀取 Engine 當時記錄的方向與軌道速限。
- 移動閉塞仍可分上下行檢核，方向篩選同時作用於歷史配對、圖表與安全摘要；列車退出後仍可選取歷史配對，以「全部時間」查看較早資料。
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
- 時間－里程列車運行圖：計畫／理論與模擬實際軌跡、方向／車輛／時間篩選、縮放、事件標記及滑鼠提示。
- PNG（一般／高解析度）、PDF（A4／A3、橫向、可分頁）及 CSV 軌跡／事件匯出。
- 版本化 `.mrtsim.json` 專案檔；讀取前會完整驗證，儲存時採同目錄暫存檔後原子取代，避免半成品覆蓋原檔。

## 使用方式

### 1. 編輯路線與列車

- 主選單提供「快速起稿」、「路網編輯」、「營運設定」、「模擬設定」與「分析結果」入口；路網、營運及模擬設定會直接開啟專案工作區的對應頁。
- 建立或讀取 topology 專案後，快速起稿側欄會收合，讓模擬與結果使用完整寬度；後續由專案工作區編輯，取消時不套用草稿。
- 播放倍率位於模擬動畫工具列，收合側欄後仍可調整；輸入或讀檔錯誤顯示在主內容區。
- 工作區的驗證訊息可用滑鼠、Enter 或空白鍵開啟對應頁面；可辨識的物件及欄位會一併定位。輸入格式錯誤時會阻擋切頁與套用，取消則保留原專案。
- 配線圖採棕紅色軌道、方向箭頭及實心矩形月台，支線轉角平滑化。圖形仍依實際 topology 繪製；短月台設有最小顯示寬度，精確長度請看 tooltip，不能以圖上尺寸量測。

- 第一站的「前站 km」必須為 `0`；後續各站填入與前一站距離，Engine 會累加成唯一里程。
- UI 的速度使用 km/h，Engine 統一使用 m/s、m/s²、m/s³、m 與 s。
- V2 首頁隱藏僅 V1 使用的列車數量、指定班距與首班時間；V2 改由發車計畫作為班次基準。播放倍率仍保留。
- 「停站進站上限」輸入 `0` 表示由動態煞車包絡線自動決定；正值才會作為停站列車的額外進站速度上限，不會套用到跨站車。
- 停站模式的逐站行為以「停站／跨站／折返」下拉選擇；「折返」僅可用在設定為中央避車線的非端點虛擬站。跨站可另設通過上限。未設定車次計畫時採「普通車／所有車站停靠」。
- 播放倍率只影響畫面更新節奏，V2 Engine 仍依序完成每一個固定子步進。

### 2. 儲存與讀取專案

- 「存檔」會保存目前可編輯的路線、車站、列車性能、折返時間、V2 參數、速限、服務模式、發車計畫及模擬模式。播放倍率為目前視窗的播放控制。
- 「讀取存檔」只在整份檔案通過格式與數值驗證後套用；格式錯誤、版本不支援或取消操作時，目前設定不會被替換。
- 新建、編輯與儲存的專案一律使用 `schemaVersion = 8`。合法 Schema 7 舊專案讀取後會轉成 Schema 8 topology draft；Schema 1～6 與未知版本會顯示不支援版本錯誤。
- 讀取 topology 專案後即可播放；「建立模擬」可重新建立模擬狀態。專案檔不保存播放到一半的瞬時狀態。
- 若要保存一次完整動態運算的結果，請先播放至所有列車結束營運，選擇「檔案 → 匯出完成後固定時刻表」。此 `.mrttimetable.json` 封存可由同一個「讀取存檔」重新導入，立即查看固定實際時刻；再次按「計算並建立模擬」才會依隨附設定建立新的動態世界。

### 3. 設定里程速限

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
- 畫面本身即為匯出預覽；PNG、PDF、CSV 會透過 Windows 儲存對話框選擇位置。
- CSV 欄位包含車輛／車次、列車等級、服務模式、模擬時間、顯示時間、里程、速度、方向、軌道、狀態、前後站及事件類型。

## 軟體版本控制

- 目前版本為 `V4.0.2`，唯一版本來源是根目錄 `Directory.Build.props` 的 `MrtVersion`。
- 建置時會同步套用到組件、檔案、資訊版本、主視窗標題及測試標題。
- Bug 修正增加修訂版本；新功能或其他變更增加次版本並把修訂版本歸零；未經使用者明確要求不得升主版本。
- 完整規則見 `VERSIONING.md`，各版內容見 `CHANGELOG.md`。Git commit、tag 與 GitHub 推送只在使用者明確要求發布時執行。

## 開發與驗證

在專案根目錄執行：

```powershell
dotnet restore .\MrtRouteSimulator.slnx --configfile .\NuGet.Config
dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore
dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
```

主要檔案：

- `src/MrtRouteSimulator.Engine`：V1 解析模型、V2 軌跡規劃、速限服務與 `SimulationWorld`。
- `src/MrtRouteSimulator.App`：WPF 桌面介面、圖形與離線匯出。
- `samples`：14 份可直接執行的 Schema 8 topology 範例，涵蓋基線、大型機場線 minimal／full、PDF 站型、實體折返與實體越行；舊檔名僅保留情境沿革。
- `tests/MrtRouteSimulator.Tests`：161 項無外部測試框架的自動化測試，包含大型 sample 分階段 gate、完整 topology 情境、directed switch、physical turnback、rear-clear、Schema 8 編輯與結果資料流 regression。
- `Directory.Build.props`：軟體版本的單一來源。
- `VERSIONING.md`／`CHANGELOG.md`：進版規則與版本變更紀錄。
- `MODEL_SPEC.md`：資料結構、公式、API、狀態與邊界。
- `QA_REPORT.md`：建置、測試、Windows UI 與匯出驗收紀錄。
- `TODO.md`：原始改善需求、完成狀態與後續界線。

## 使用與安全界線

本軟體是營運與號誌概念模擬器，不是可部署的鐵路安全系統，未取得 ATP／ATO／ATS、安全完整性等級或任何鐵路安全認證。V4.0.2 仍支援概念層級的多月台平衡、站內越行與進站精停；相關資源、容量與事件不是安全認證容量。未輸入特定路線資料時，結果只代表使用者輸入與程式假設。

目前預設上下行使用不同軌道；共用單線與聯鎖失效尚未建模。V4 的尾軌、袋狀軌、crossover、passing 與折返皆為實體 topology edge／traversal，但仍是概念性幾何，不是實際軌道平面圖；坡度、曲線阻力、黏著變化、乘客量與真實路線校準仍不在 V4.0.2 已完成範圍內。服務類型與車型維持分離目錄。
## 依 PDF 建立站場（2026-09-09）

在專案工作區「快速建立」的「依參考圖建立站場」選擇站型，按「以此站型重新起稿」，檢查後套用。此按鈕會替換工作區草稿；取消工作區會保留原專案。亦可直接讀取 `samples/PDF-*.mrtsim.json`。

提供島式二股、側式二股、一島一側三股、二島四股、站後折返、站前折返與中央袋狀軌七種可執行範例。路線圖採上行在上、下行在下的靠右行駛配置，顯示月台編號、島式共用站體、渡線與尾軌止衝。

三／四股道範例含普通車待避與快速車越行；折返範例含同車反向接續。營運頁可選 ServiceRoute 後「設為下行進路／設為上行進路」；袋狀軌的 `:BYPASS` 進路只停 A、C 站，折返的 `:PLATFORM2`／`:TAIL2` 須將上下行一併選為對應進路。模擬頁的「調整模擬與安全參數…」可設定起始時鐘、播放倍率、反應時間及安全間距。

PDF 提供站型與配置概念，軌長、速限、車型、停站時間及派車為可編輯示範值。共享袋狀軌採發車前保守預約、車尾淨空後放行對向車；示意位置與股道設定不參與運行距離或安全計算。
