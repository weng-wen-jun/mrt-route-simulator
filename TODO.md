# V4.0.1 交付狀態與後續清單

> 只有本檔「現行待辦」內未勾選的核取方塊用來判定目前尚待修整的工作。歷史版本紀錄不計入待辦數量；V4 架構契約以 `MODEL_SPEC.md` 為準，完成狀態以 source、tests 與 `QA_REPORT.md` 為準。

## V4.0.1 工作區與參考圖樣式（2026-09-08）

- [x] 2026-09-09：依 PDF 加入七種可執行站場、方向及月台配置、進路綁定、模擬參數設定與袋狀軌對向互斥；118/118 自動化通過，完成 WPF 離屏繪圖及站型按鈕／草稿隔離驗證。桌面手動驗收仍依現行待辦執行。

- [x] 主選單直達路網、營運及模擬設定；建立／讀取 topology 後收合快速起稿，播放倍率與驗證訊息留在主內容區。
- [x] 驗證訊息帶有物件及欄位目標，支援點擊與鍵盤定位；修正 DataGrid 綁定錯誤未阻擋套用、失焦後遺失欄位定位的問題。
- [x] 依使用者參考圖加入棕紅軌道、方向箭頭、實心矩形月台與平滑轉角；修正編輯器多站被壓至右側及短月台與端點連線重疊。
- [x] Release build 0 警告、0 錯誤，完整測試 110/110；Windows 實测播放倍率、錯誤阻擋／定位／取消、六站示意圖一般及窄視窗，以及完整範例主畫面與編輯器。

## V4.0.1 軌道配線圖 UI（2026-09-05）

- [x] 【V4-UI-TRACK-DIAGRAM-01】主畫面與 topology editor 改採鐵路配線圖風格：上下行固定分線、軌道統一青色實線，只有實際 directed connection 才畫轉向連線，不再於每個主線節點畫假性垂直連通。
- [x] 【V4-UI-PLATFORM-01】月台依 `PlatformStartOffsetMeters`／`PlatformEndOffsetMeters` 畫成沿軌道的長色帶，站碼與站名取代畫面上的 edge ID；技術 ID 保留於 tooltip。
- [x] 【V4-UI-TAIL-01】尾軌沿抵達方向的主線股道直線往端點外延伸；回程若需切換股道才顯示實際 directed connection，避免置中形成 Y 字。`BufferStop` 節點顯示垂直止衝，尾軌列車位置仍直接使用 edge-local cursor。
- [x] 【V4-UI-TRACK-DIAGRAM-BUILD-01】Release build 0 警告、0 錯誤；完整自動化 107/107 通過，確認 presentation 改動未改變 topology runtime。

## V4.0.1 範例拓撲與運行修整（2026-09-04）

- [x] 【V4-SAMPLE-TOPO-01】盤點六個 Schema 8 範例；移除孤立分支、無使用 edge 與 legacy facility edge 欄位，尾軌／袋狀軌改為單一實體 edge 的正反向 traversal，保留端點安全餘量。
- [x] 【V4-SAMPLE-OPS-01】修整碰撞、停站控制、無限續行及「名義上有越行但實際未發生」的班次設定；所有範例推進 3,600 秒後均有界完成。
- [x] 【V4-UI-SCHEMATIC-01】主路線圖與 topology editor 共用平行 edge 幾何；明確繪出 directed connection，設施支線可見地接回既有軌道，節點與支線標籤依上下側錯開。
- [x] 【V4-FACILITY-VALIDATE-01】精靈新增設施時保留既有節點的自然合法轉向；validator 拒絕未直接接上服務路線或中段停點未立即沿同 edge 反向的折返。
- [x] 【V4-SAMPLE-VERIFY-01】Release build 0 警告、0 錯誤；107/107 通過；Windows WPF 實機載入並播放完整 topology 範例，三種匯出未於本輪重測。

## V4.0.1 修正驗證（2026-08-31）

- [x] 【V4-TOPO-COMPLETE-SAMPLE-01】新增完整 Schema 8 topology 執行範例，覆蓋 directed switch、passing、pocket、雙端 crossover tail、edge-local turnback stop、rear-clear 與接續車次。
- [x] 【V4-TOPO-PASSING-SAFETY-01】快速車匯入正線、車尾仍在 passing edge 時，普通車會保持待避；避免平行 edge 被錯算為負間距或發生碰撞。
- [x] 【V4-TOPO-VERIFY-02】Release build 0 警告、0 錯誤；自動化 100/100 通過。此輪未重做 Windows UI 手動驗收。

## V4.0.0 已完成範圍（2026-08-31）

- [x] 【V4-TOPO-ABC-01】完成 Track-first topology domain model、集中 `InfrastructureValidator`、`LinearInfrastructureBuilder` 與 route-local `RouteProjection`；linear quick builder 會建立逐站雙向 edge 與有序 ServiceRoute。
- [x] 【V4-TOPO-D】`WorldTrainState`、trajectory 與 event 已輸出同步的 `TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex` transitional state。
- [x] 【V4-TOPO-E-MAINLINE】正常主線列車已依有序 `DirectedTrackTraversal` 前進；`PositionMeters` 在正常主線僅作 topology cursor 的相容投影快取。
- [x] 【V4-TOPO-F-MAINLINE】正常主線停靠已由 `ResolvedStop` 解析至 platform 的 `TrackPosition`，進站剩餘距離與到站吸附消費相同停點。
- [x] 【V4-TOPO-G-MAINLINE】正常主線的即時速限與提前煞車已改由 `TrackSpeedLimitService` 使用 edge-local interval 計算；Schema 7 里程速限目前只作 runtime 轉換來源。
- [x] 【V4-TOPO-FACILITY】tail／pocket／turnback／passing facility 均以實體 edge 與 ordered traversal 執行；footprint、rear-clear resource release 及平行 edge safety 均有 regression。
- [x] 【V4-TOPO-H-CORE】`SimulationWorld` V2 runtime 不持有 compatibility `Route`、legacy `InfrastructureGraph` 或 `SpeedLimitService(route)`；表單 Route 僅在建構前一次性轉成實體 Schema 8 topology。
- [x] 【V4-SCHEMA8-01】Schema 8 topology 專案已具 round-trip、reference validation、legacy field／版本拒絕與 topology-native runtime factory。
- [x] 【V4-UI-TOPOLOGY】WPF 可讀取、編輯及播放 Schema 8 topology；運行圖、結果頁和 CSV／PNG／PDF 匯出不要求 Route。
- [x] 【V4-TOPO-VERIFY-01】Release build 為 0 警告、0 錯誤；自動化測試 99/99 通過，含有向道岔轉向與 edge-local 折返停點 regression。（V4.0.0 歷史基準）
- [x] 【V4-DOC-01】統一現行文件的版本、測試數、完成邊界與待辦；不再使用「Phase A～H 全部完成」代表完整 topology migration。

## V4.0.0 桌面人工驗收（2026-08-31）

- [x] 【V4-UI-MANUAL-01】以 Windows WPF 實際讀取 `V4.0.0-topology-baseline.mrtsim.json`、建立與播放 Schema 8 topology 世界、檢視實際進出站時刻表，並完成 CSV／PNG／PDF 匯出與讀回。輸出位於 `artifacts/V4.0.0-topology-manual-ui.*`。

## 現行待辦

- [x] PDF 分頁匯出改為各頁重繪標題、座標軸與頁內列車標籤，避免點陣切片切開長標題及跨頁列車標籤；2026-09-19 已完成 A4 兩頁渲染檢查、Release build、Engine 與 WPF 輸出驗證。

- [x] 【V4-ROUTE-CONSTRUCTION-GUARDS】已實作明確有向轉向產生器、實體接軌側別共用檢核、編輯器保存與讀檔交易保護；快速建線、分割及設施精靈接入，12份現行範例遷移且主圖／編輯器零配線警告。詳見 [路線圖常見問題清單](ROUTE_LAYOUT_ISSUES.md)。
- [ ] 【V4-LEGACY-PORT-MIGRATION】已加入缺資料 edge 盤點、逐 edge 明確 A/B assignment API 與 WPF「套用明確側別／保留相容讀取／取消」引導；不從示意位置推定實體方向。仍須完成原生桌面流程驗收，並確認產品是否要將遷移後的明確側別設為所有舊檔的全面強制政策。

- [x] 【V4-SAMPLE-SCENARIO-BUILDER-01】已新增可重用 `TopologyScenarioBuilder`／`TopologyScenarioValidation`：以既有 Schema 8 document、quick builder 與 topology-native `SimulationWorld` 分階段驗證 minimal topology baseline、station chain、service pattern、turnback、passing facility 與 timetable；Structural／Operational gate 可獨立執行，Regression 由 Release／Engine／WPF 流程負責。`samples/README.md` 已補 Scenario Manifest 欄位規範；`.mrtsim.json` 維持為輸出／載入範例，不建立第二套 Domain Model 或 runtime。新增回歸後 Engine 145/145 通過。
- [x] 【V4-SAMPLE-TAICHUNG-AIRPORT-01】已保留主要站 minimal baseline，並以可重建 builder 分 7 階段建立 full sample：O01～O26（含 O08a、O15a）28 站鏈、29.9 km／O01～O20 23.8 km aggregate target、O20 站後 pocket、O04／O13 雙向四股越行，以及 FULL-LINE／SECTION／AIRPORT-DIRECT。補充參考資料將 AIRPORT-DIRECT 修正為 O01↔O20；正式／規劃來源與 synthetic test values 已在 `samples/README.md` Manifest 分開。2026-09-20 Release build 0 warnings／0 errors、Engine 160/160、WPF runner 與 `git diff --check` 通過；原生桌面不同 DPI 與關鍵事件連續播放仍列入 UI 人工驗收邊界。

- [ ] 【V4-UI-TRACK-DIAGRAM-MANUAL-01】2026-09-11～12 已實機讀取 baseline 與完整 topology，抽查兩範例主畫面／編輯器一般及窄視窗，以及越行、袋狀軌返回、尾軌折返等播放畫面；本輪抽查未見站名／設施圖例與列車標記互相遮擋。另修正窄視窗摘要卡文字裁切並完成新版目視複核。仍須補足不同 DPI 與折返／交會關鍵畫面的連續檢查，不能以抽查代表完整驗收；範圍、尺寸與時間點見 QA_REPORT.md「桌面實機驗收進度」。

## V3.4.2 已完成（2026-08-29）

- [x] 【首次發現：V3.4.1／V3.4-STOPCTRL-01】【修正版本：V3.4.2】`StationStopController` 接管排定停站的距離－速度曲線、煞停點預測與最後 12 m 低速精停；保留既有煞車鎖定解除條件，但近站低速限制解除後不會回到一般線速牽引。障礙物、越行與移動閉塞仍取更嚴格限制。
- [x] 【驗證版本：V3.4.2】Release build 0 警告、0 錯誤；自動化測試 96/96 通過，含高速曲線／預測停點與近站低速限制解除後不回彈的回歸測試。未執行 Windows UI 實測。

## V3.4.0 已完成（2026-08-27）

- [x] 【首次發現：V3.3.0／V3.4-TB-01】【修正版本：V3.4.0】端點站前折返接入交替月台與目的月台資源保留；連續抵達車次依 `alternateBerthing`／`RoundRobin` 交替配置，後車在 A/B 均占用時等待，前車折返發車後才取得已釋放月台。
- [x] 【首次發現：V3.3.0／V3.4-TB-02】【修正版本：V3.4.0】停站模式新增「折返」，中央避車線可作為路線中的非端點虛擬站；需對應 `CentralSidingTurnback`，列車保留月台／避車線資源並接續反向車次。
- [x] 【首次發現：V3.4.0／V3.4-ARCH-01】【修正版本：V3.4.0】以 `SimulationSession`／`SimulationWorldOptions` 收斂實際與計畫世界的建立及推進；軌跡提供完整、降採樣與僅事件留存，Schema 7 移除並拒絕舊雙資料源欄位。
- [x] 【首次發現：V3.4.0／V3.4-TT-01】【修正版本：V3.4.0】已完成 V2 動態模擬可匯出 `.mrttimetable.json` 固定時刻表封存，重複導入後直接呈現實際到離站、停站、誤點及狀態；封存攜帶正規化 Schema 7 專案設定，不會建立第二份派車資料源。
- [x] 【驗證版本：V3.4.0】Release build 0 警告、0 錯誤；自動化測試 86/86 通過，含站前 A/B 交替、後車等待且無碰撞、中央避車線虛擬站折返、站後成對尾軌虛擬節點、進站煞車不回彈、會話推進、軌跡留存、固定時刻表封存往返／破損拒絕與 Schema 7 範例匯入／往返。本輪依使用者指示未執行 Windows UI 實測。

## V3.4.1 已完成（2026-08-29）

- [x] 【首次保留：V3.0.0／V3.3-PLAT-01】【修正版本：V3.4.1】`Automatic` 月台策略按成功配置次數平衡同站同方向候選；`EarliestAvailable` 優先嘗試最早釋放者。`RouteReserved`／`RouteReleased` 會攜帶實際月台、進路、衝突區與尾軌資源，結果頁與 CSV 可獨立輸出占用時間、使用率、觀測每小時預約數與最短釋放間距。
- [x] 【首次保留：V3.0.0／V3.4-OVTK-01】【修正版本：V3.4.1】越行選擇會沿快速車下一個排定停靠站前的跨站區段搜尋，依待避普通車、進站位置與資源可用性選擇候選設施；同站多候選及上下行方向專屬越行可同時保留、無碰撞完成。
- [x] 【首次發現：V3.4.0／V3.4-STOP-01】【修正版本：V3.4.1】車型可指定預設停站模式，手動派車指定值優先於車型、車型優先於服務；停站模式可複製後獨立編輯，Schema 7 往返與 SimulationWorld 實際結果均有回歸測試。
- [x] 【首次發現：V3.4.0／V3.4-SPAT-01】【修正版本：V3.4.1】`InfrastructureGraph` 與 Schema 7 正規化會使每個實體站恰有一個五類分類；尾軌與中央避車線虛擬節點僅為實體站的拓撲／資源附屬節點，首頁路線圖採相同完整分類資料源。
- [x] 【首次發現：V3.4.0／V3.4-SPAT-02】【修正版本：V3.4.1】五類型式各自具可保存範本；新建站點複製目前範本後可獨立覆寫，日後修改範本不回寫既有站場。
- [x] 【首次發現：V3.4.0／V3.4-TB-03】【修正版本：V3.4.1】站後折返列車以固定 0.1 秒軌跡連續駛入／返回成對尾軌；回站後先抵達反方向月台並由其發車，月台、進路及尾軌資源事件與無碰撞驗證均已涵蓋。
- [x] 【首次保留：V3.0.0／V3.3-GEO-01】【修正版本：V3.4.1】站前與中央避車線折返均以 `TURNBACK:<參考點>:OUT/RETURN` 執行期分段軌道完成外出、停等、反向返回；固定 0.1 秒軌跡、非零虛擬位置、路線圖動畫與無碰撞回歸均由同一 `SimulationWorld` 資料流驗證。
- [x] 【首次保留：V3.0.0／V3.3-STAT-02】【修正版本：V3.4.1】新增同一派車、車型及停站模式的 V1 理論／V2 實際逐站到離站、停站秒數、秒差與百分比比較頁及 CSV。
- [x] 【驗證版本：V3.4.1】Release build 0 警告、0 錯誤；自動化測試 94/94 通過，覆蓋多月台平衡、資源時間軸／CSV、多候選與反向同時越行、獨立停站模式、實體站分類／範本、三類折返連續軌跡與 V1／V2 比較。

## V3.3.0 已完成（2026-08-24）

- [x] 【首次發現：V3.2.0／V3.2-UI-01】【修正版本：V3.3.0】「輸入 → 停站模式」改為可交易式編輯視窗，支援模式名稱、車站、停站／跨站、停站秒數及通過速限；取消不套用，刪除被引用模式會顯示原因。
- [x] 【首次發現：V3.2.0／V3.2-DATA-01】【修正版本：V3.3.0】專案升級為 `schemaVersion = 7`，執行與存檔只採 `VehicleTypes + ServiceTypes + StopPatterns + Dispatch`；不再寫出舊 `ServicePatterns／ServiceRuns`，Schema 1～6 明確拒絕。
- [x] 【首次發現：V3.2.0／V3.2-UI-02】【修正版本：V3.3.0】一般車型、服務類型與停站模式 UI 隱藏內部 ID；新增項目由系統產生穩定 ID，刪除仍被引用的目錄項目會阻擋並顯示中文原因。
- [x] 【首次發現：V3.2.0／V3.2-ENG-01】【修正版本：V3.3.0】V2 寫實引擎以各車次的車型目錄作為性能權威來源，實際套用最高速度、加速度、營運／緊急煞車、Jerk、牽引衰減、惰行減速度與車長；V2 首頁隱藏不使用的 V1 全域性能欄位。
- [x] 【首次發現：V3.2.0／V3.2-STAT-01】【修正版本：V3.3.0】V2 區間統計補齊方向、車輛、車次、車型、服務、停站模式與模擬秒範圍篩選；由軌跡約束精確累積移動閉塞受限秒數，UI 與 CSV 同步輸出。
- [x] 【首次發現：V3.2.0／V3.2-DOC-01】【修正版本：V3.3.0】統一「產品版本 V3.3.0／V1 基礎引擎／V2 寫實引擎／Schema 7」名詞，歷史需求移出現行清單。
- [x] 【首次發現：V3.2.0／V3.2-SAMPLE-01】【修正版本：V3.3.0】新增 `V3.3.0-完整功能驗證範例.mrtsim.json`，直接驗證停站秒數覆寫、跨站速限、單一資料來源、雙向派車、折返接續與五類空間參考點。
- [x] 【驗證版本：V3.3.0】隔離 Release 建置 0 警告、0 錯誤；自動化測試 75/75 通過；Windows UI 已載入完整範例、建立並播放至端點作業完成，確認退出列車消失且指定反向車次接續。原 Release 輸出因正在執行的 V3.2.0 程式鎖定 DLL，故未強制關閉程式覆寫。

## 已完成的既有 UI 修正

- [x] 【首次發現：V3.4.0／V3.4-UI-03】【修正版本：V3.4.1】所有管理／編輯視窗以共用登錄表阻擋同類第二實例；車型、服務類型、停站模式、發車計畫、基礎設施與空間參考點入口均先啟用既有視窗。Release 實機確認車型目錄連續觸發維持同一視窗、最小化後還原同一視窗、取消關閉後才建立新視窗，且草稿未被第二實例取代。
## 遠期修正目標

- 【首次保留：V3.0.0／V3.3-CAL-01】待取得指定真實路線的坡度、曲率、黏著、車型性能與實測資料後，進行校準，並執行長時間、多月台與大量列車效能測試。本項目前不計入現行交付阻擋。

## 非產品目標

- 鐵路安全認證、ATP／ATO／ATS 部署與失效安全分析不在本軟體範圍內；本軟體僅供營運概念模擬與內部測試，不能作為安全認證或行車授權依據。

## 標籤規則

- 待辦使用 `【首次發現／問題 ID】`；完成時保留原標籤並追加 `【修正版本：Vx.x.x】`。
- 產品版本、引擎與存檔格式分開表述：產品版本以 `Directory.Build.props` 為準，`V1／V2` 是引擎，現行可編輯與儲存格式為 Schema 8；Schema 7 只保留 legacy 匯入與固定時刻表相容資料。
- 既有 V3.1／V3.2 完成明細見 `CHANGELOG.md` 與 `QA_REPORT.md`。

---
