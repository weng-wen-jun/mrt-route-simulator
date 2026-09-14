# 變更紀錄（V4；保留 V3 歷史）

本檔依軟體版本由新到舊記錄。Git 標籤使用小寫 `v`，軟體畫面使用大寫 `V`。

## V4.0.1 - 2026-08-31

### 工作區修正（2026-09-14，尚未發布）

- 合併上下行速度圖，以實體列車完整行程呈現，保留獨立方向的歷史移動閉塞檢核。
- 尾軌折返返回反向月台後正常停站，並等待接續車次發車時間。
- 修正 topology 時刻表、區間統計、V1/V2 比較及摘要的資料比對與初始化；站事件新增結構化站號，避免以車頭里程或訊息文字誤判到站。
- 修正 PNG／PDF 透明黑底及父容器配置偏移；驗證與分頁限制見 QA_REPORT.md。

### 工作區修正（2026-09-13，尚未發布）

- 站型／快速建線／設施精靈共用明確進路產生器；新增實體接軌側別檢核，阻擋同側回頭，讀檔失敗保留目前工作區。
- 修正站後折返使用錯誤渡線、baseline月台與端點捷徑；全13範例補入接軌側別，舊單軌折返範例增設實體進出渡線。尾軌精靈同步改為四段路徑。
- 主圖與編輯器共用位置映射及平滑接軌，完整範例三站停靠對位；細節、相容性限制及驗證見 `ROUTE_LAYOUT_ISSUES.md`／`QA_REPORT.md`。

### 工作區修正（2026-09-11，尚未發布）

- 路線圖採起始站月台中心0K，尾軌允許負里程，終點外側延伸；即時車輛標記與位置欄位使用實際車體中心。
- 新增明確停點基準，七PDF模板採車體中心；換端改由原車尾成為新車頭，保持完整footprint，涵蓋車尾恰落節點的邊界。舊專案預設車頭基準。
- 逐檔修正13範例月台容量、起點停車標、越行月台位置及尾軌／袋狀軌容車長度；新增月台有效長度／整車停靠與完整反向返回進路驗證。修正精確抵達越行入口漏接旁線，以及中心停點導致速度預覽找不到終點。
- 站名對準每個實際月台本體，錯列月台分別標示；完整127/127測試、52圖WPF檢核及13範例載入通過。詳見 `QA_REPORT.md` 與 `samples/AUDIT-2026-09-11.md`。

### 工作區新增（2026-09-09，尚未發布）

- 路線站場建置規則化：拒絕站內折返停點、月台及進路首末站不一致和同站重複月台號碼；站名共用中心定位並提供版面警告，設施圖例可捲動。載入先完整準備候選模擬再替換狀態；新增正式 WPF 範例載入／幾何回歸與統一驗收腳本。

- 修正 PDF 站前／站後折返範例讀檔建立計畫時間軸時遺失 runtime cursor；停點保護改用 resolved traversal／offset。全部範例實際初始化通過，120/120 回歸通過。

- 逐檔核對 13 個範例與啟動六站：修正舊範例股道／月台配置及不符實體的站型名稱、水平袋狀軌、尾軌回接與編輯器設施文字重疊。
- 修正線性轉換的上行起站選錯，以及 graph-distance 忽略首尾轉向造成共用站節點跨股道假碰撞；全部情境 3,600 秒完成退出，119/119 回歸通過。

- 依《軌道及站體形式》加入七種可執行站型與對應範例；主畫面及編輯器共用靠右行駛、月台編號與站體呈現。
- 新增站型起稿、進路方向綁定、模擬安全參數編輯；Schema 8 可保存非運行用示意欄位。
- 共享袋狀軌的一般服務進路增加發車前互斥及車尾淨空釋放，補充越行、兩組折返、同車接續與重設回歸。
- Release build 0 警告／0 錯誤，118/118 測試通過；WPF 離屏繪圖及控制項驗證通過，桌面手動驗收仍見 QA_REPORT.md。

### 修正與驗證

- 主畫面與 topology editor 改採鐵路配線圖視覺：上下行維持固定間距，軌道、橫渡線與實體設施使用一致的青色實線；移除每個主線節點的假性垂直連線，只有 Schema 8 `DirectedTrackConnection` 才呈現轉向。
- 月台依 `PlatformStartOffsetMeters`／`PlatformEndOffsetMeters` 畫成實際長色帶；站碼與站名成為主要標籤，edge ID 降為 tooltip。尾軌沿抵達方向主線股道直線延伸，只有回程切換股道才畫實際轉向，`BufferStop` 會畫止衝符號。
- 配線圖變更已完成 Release build 0 warnings／0 errors及 107/107 自動化；尚待 Windows WPF 對 baseline、完整範例、topology editor 與窄視窗進行新樣式人工複核。
- 重新盤點六個 Schema 8 範例：移除孤立分支、重複回程 edge 與 legacy facility edge 欄位；尾軌及袋狀軌改以單一雙向實體 edge 完成駛入、停等及反向返回。
- 修整範例班次與進站參數，排除碰撞、停站違規、未具名無限續行及未實際發生的越行；V3.4 快速車現會在普通車待避期間實際完成通過線超越。
- 主路線圖與 topology editor 共用平行 edge 幾何，並明確繪出有向轉向連接器；設施精靈會保存既有自然轉向，validator 會拒絕未接上服務路線或中段停點跳接其他 edge 的折返。
- 六個範例均推進 3,600 秒並完整退出，無碰撞或停站違規；Release build 0 warnings／0 errors，自動化 107/107 通過。Windows WPF 已實機載入並播放完整 topology 範例；本輪未重做匯出，也未 commit、tag、push 或發布。
- 新增 `V4.0.0-完整拓撲執行驗證範例.mrtsim.json`：以實際手動派車依序執行有向道岔、快速越行、中央袋狀軌、東西端 crossover 尾軌、指定接續與 edge-local 折返停點。
- 修正快速車剛由 passing edge 匯入正線時，普通車仍在平行 local edge 卻被 safety 誤算為負間距、進而可能碰撞的問題；普通車現在保持待避至快速車車尾 rear-clear，平行 edge 不再錯作共線。
- 補完整情境 regression，並把既有 physical passing regression 延長到合流後，確認無碰撞、資源釋放、時刻表、區間統計及 CSV 都使用 topology 結果。
- Release build 0 warnings／0 errors；自動化 100/100 通過。本輪未重做 Windows UI 手動操作；未 commit、tag、push 或發布。

## V4.0.0 - 2026-08-31

### Topology-native completion（2026-08-31）

- Schema 8 `TopologyProjectFormat`、baseline sample、topology editor 與 runtime factory 已成為 V2 正式入口；Schema 8 結果頁、運行圖與 CSV／PNG／PDF 匯出均不需要 Route。
- `SimulationWorld` V2 runtime 不再持有 compatibility Route、legacy InfrastructureGraph 或 route-chainage speed service。舊表單 Route 僅在 world 建立前一次性轉成 physical topology。
- 快速線性起稿會補齊兩端實體尾軌、conflict resource、station operation 與 turnback operation；tail、pocket、turnback、passing 皆由 unified cursor、footprint 與 rear-clear resource release 執行。
- 新增有向 `DirectedTrackConnectionDefinition`：在道岔／渡線節點可限制合法 edge-to-edge transition，並由 validator、path finder 與 runtime navigator 共用；快速線性起稿的端點折返加入實體 crossover edge 與 switch resource。
- `TurnbackFacilityDefinition` 的折返停點改可使用 edge-local `TrackPosition`；中段停點僅能接到同一實體 edge 的反向 traversal，防止以 virtual position 或 teleport 偽造折返。
- `ResolvedStopResolver`、時刻表、區間統計與軌跡匯出改以 resolved topology stop／結果 context 為輸入；`RouteProjection` 僅用於顯示衍生 chainage。
- 自動化驗收為 99/99 通過，Release build 0 warnings／0 errors；Windows WPF 已實測 Schema 8 讀檔、播放、時刻表及 CSV／PNG／PDF 匯出。修正 direct Schema 8 world 因無 V1 `SimulationEngine` 而無法進入播放計時器的 UI gate；未 commit、tag、push 或發布。

### Track-first topology 初始階段（歷史紀錄；已由上方完成項取代）

- 新增獨立的 `InfrastructureGraphV4` topology aggregate：`TrackNodeDefinition` 與 `TrackEdgeDefinition` 描述實體軌道；`StationDefinitionV4` 僅作邏輯月台群組；`PlatformDefinitionV4` 明確附著單一 edge 的 local offset。
- 新增 `ServiceRouteDefinition` 與有序 `DirectedTrackTraversal`。traversal 必為 list，不以 set 儲存；validator 會檢查 edge/node、月台 offset、方向相容性、相鄰 traversal 連續性及候選月台是否屬於該 route。
- 新增 `LinearInfrastructureBuilder`，由既有線性 `Route` 產生每一相鄰站的下行／上行 edge、方向別月台與雙向 ServiceRoute；保留舊 `RouteFactory`、`InfrastructureGraph` 與 Schema 7 資料源不變。
- 新增 `RouteProjection`，將單一 ServiceRoute 投影為相容用 chainage；reverse traversal 正確轉換 offset，重複 edge（loop）必須指定 traversal index，避免把投影座標誤作實體位置。

- `SimulationWorld` 現在建立 linear topology runtime mirror，將主線列車的 legacy projected position 同步輸出為 `TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex`；`WorldTrainState`、`TrajectorySample` 與 `SimulationEvent` 均可攜帶該資訊。折返／越行等 legacy 虛擬軌道則明確標示為 `LEGACY:<TrackId>`，不假裝成實體 topology edge。
- 正常主線已改由有序 `DirectedTrackTraversal` 推進，並在跨 edge 時更新 runtime `TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex`；`PositionMeters` 只保留為 topology cursor 投影出的 Schema 7／UI 相容快取。
- `ResolvedStopResolver` 將 ServiceRoute 的 station/platform 停靠語意解析為 `TrackEdgeId + StopPositionOffsetMeters + traversal index`；主線進站距離與到站吸附改用此 track-local stop。上行中間站固定為其抵達 edge 的終端，避免把 stop 錯放到離站 edge。
- `TrackSpeedLimitService` 以 traversal direction + edge-local offset 評估正常主線即時速限與提前煞車；Schema 7 chainage speed limit 僅在 runtime adapter 轉換，不再作主線速限計算座標。
- `TopologySimulationDefinition` 讓 `SimulationWorld` 可由 `InfrastructureGraphV4` 與雙向 ServiceRoute 建立；內部仍會產生僅供 Schema 7／virtual-track 相容的 Route adapter。

### 當時尚未完成（歷史紀錄）

- `TopologySimulationDefinition` 目前只是呼叫端免傳 Route 的入口；`SimulationWorld` 主建構器仍依賴 compatibility Route、legacy `InfrastructureGraph`、legacy `SpeedLimitService`、station index 與 analytical baseline，Phase H 核心尚未完成。
- 站內越行、尾軌、站前／中央避車線與空間折返仍使用 `LEGACY:<TrackId>`；發車淨空、移動閉塞、障礙物與碰撞保護也尚未全面改用 graph distance／topology occupancy。
- Schema 8 persistence、V4 baseline sample、topology path finder、gradient／curve、turnback facility／operation、track occupancy／train footprint、進階 V4 UI 與 graph-based route drawing 尚未完成。
- 規格要求的十項正式 topology acceptance 尚未全部完成：普通雙線仍缺 V3.3 旅行時間誤差 ≤ 0.1 秒的直接比較；站後渡線、尾軌、袋狀軌、Y 分岔、待避站、同站多月台、跨 edge footprint、單 tick 跨多個短 edge 與反向 traversal runtime，均尚缺正式 topology regression 或完整 runtime 驗收。這十項與目前已通過的 10 項 topology regression 是不同集合。

### 當時驗證

- Release build：0 warnings、0 errors。
- 自動化測試：106/106 通過；新增 topology-first 建構入口 regression。
- 本輪未執行 Windows UI 實測；未執行 Git commit、tag、推送或 Release 發布。

## V3.4.2 - 2026-08-29

### 修正

- 新增 Engine 端 `StationStopController`。每個固定 `0.1 s` Tick 先以剩餘距離、目前速度、營運煞車能力與 Jerk 生成高速距離－速度煞車曲線，再以同一動態煞車包絡線預測停點；預測越過停車點時才要求營運煞車。
- 最後 12 m 進入低速精停曲線：目標速度同時受線速、煞車曲線、3 m/s 上限及距停車點 2 m 吸附邊界約束。即使近站低速限制解除，列車也不會重新按一般線速牽引。
- 保留既有 `StationBrakingActive` 低速解除條件不變；排定停站的速度／煞車命令改由新控制器決定，障礙物、越行與移動閉塞限制仍在其後取更嚴格者。

### 驗證

- Release build：0 warnings、0 errors。
- 自動化測試：96/96 通過；新增高速煞車曲線／預測停點、低速精停，以及近站低速限制解除後不回彈至一般線速的回歸案例。
- 未執行 Windows UI 實測；未執行 Git commit、tag、推送或 Release 發布。

## V3.4.1 - 2026-08-29

### 修正與完成項目

- `Automatic` 月台配置改依成功配置次數平衡可用的同站同方向月台；`EarliestAvailable` 以月台釋放時點排序。資源事件保留實際資源清單，新增資源占用／觀測容量結果頁與 UTF-8 CSV。
- `SimulationWorld` 的越行會搜尋快速車下一個排定停靠站前的跨站候選，依普通車待避、進站位置與可用衝突資源選擇設施；多候選與上下行同時越行保留方向專屬資源。
- 車型可設定預設停站模式，優先序為派車明確指定、車型、服務；停站模式可複製後獨立調整。
- Schema 7 與 `InfrastructureGraph` 會完整化每一實體站的單一五類分類，範本與各站覆寫分離保存；路線圖使用同一分類資料。
- 站後尾軌折返以連續軌跡駛出／返回，回站後保證使用反方向月台到達與發車；新增 V1／V2 同條件逐站比較與 CSV。
- 站前與中央避車線折返改為 `TURNBACK:<參考點>:OUT/RETURN` 分段軌道：列車以固定 0.1 秒完成橫渡線外出、停等、反向返回，路線圖與即時位置直接讀取同一軌跡／軌道 ID。

### 驗證

- Release build：0 warnings、0 errors。
- 自動化測試：94/94 通過；新增多月台平衡、資源時間軸、同站／全線候選越行、反向同時越行、停站模式隔離、實體站分類與範本、三類折返連續軌跡／反向月台及 V1／V2 比較的回歸覆蓋。
- Windows UI 單一視窗實測：車型目錄連續開啟維持相同視窗，最小化後會還原同一視窗，取消關閉後才會建立新的視窗；所有管理／編輯入口皆經同一登錄守衛。未執行 Git commit、tag、推送或 Release 發布。

## V3.4.0 - 2026-08-27

### 新增與修正

- 站前折返的 `alternateBerthing` 與 `RoundRobin` 月台策略已接入 `SimulationWorld`：相容目的月台依序輪替選擇，目的月台從進路建立起即受保留，直到該車下一次發車以同一筆原子預約換出。
- 抵達月台、端點站前折返資源與明確折返資源都納入資源生命週期；資源不足時保留既有月台並記錄 `WaitingForResource`，避免後車提前侵入。
- 停站模式新增 `Turnback`。中央避車線可建成路線中的虛擬站點，且僅能對應 `CentralSidingTurnback` 空間參考點；列車於該點停靠、保留避車線資源後反向接續既有或自動產生的車次。
- 新增可匯入的「端點站前與中間站中央避車線折返檢核」Schema 7 範例，涵蓋北端 A/B 交替月台與 TB02V 中央避車線虛擬站區間車。
- 新增無 WPF 相依的 `SimulationSession` 與 `SimulationWorldOptions`，統一實際／計畫世界的建構、固定 Tick 推進、重設及計畫事件時間線。
- `SimulationWorld` 新增完整、降採樣與僅事件三種軌跡留存策略；預設仍為完整 0.1 秒取樣，以維持既有結果、CSV 與圖表行為。
- Schema 7 的正規文件模型移除舊 `ServicePatterns`／`ServiceRuns` 欄位；讀檔若出現這些舊雙資料源會明確拒絕，不再保留可誤用的轉換程式。
- 完成所有列車營運後，可從檔案選單匯出 `.mrttimetable.json` 固定時刻表封存；封存攜帶建立當下正規化的 Schema 7 設定與 `SimulationWorld` 實際到離站事件，重新讀取後直接呈現凍結結果而不重跑模擬。
- 新增 `StationOvertakeFacilityDefinition` 與 Schema 7 `stationOvertakeFacilities`：站間每方向可維持單一正線，僅於雙島四股站指定普通車待避線、快速車通過線、分歧位置與衝突資源。快速車符合優先序與 `CanRequestOvertake` 後才可跨越；普通車原定停站結束但快速車接近／正在越行時會繼續待避，完成後快速車匯回正線。
- 基礎設施編輯器新增「站內越行」圖形化分頁，可直接維護設施 ID、方向、共線正線、普通車待避／快速車通過月台與股道、分歧公里及衝突資源；讀檔、建模與 Schema 7 儲存使用同一資料列。
- 新增三站「雙島四股快速車越行驗證」範例；自動測試確認普通車待避、快速車站內分歧、結構化追越事件、匯回單一正線與無碰撞。
- 站後折返若指定成對下行／上行 `TailTrack`，`SimulationWorld` 會建立 `TAIL:<TurnbackId>` 虛擬節點並實際記錄駛入、尾軌停等、反方向返回與端點接續；尾軌及衝突資源以同一原子預約保留。未配置成對尾軌的舊設定仍採原本的時間抽象。
- 修正進站煞車鎖定在殘距 0.5 m 與到站吸附 2 m 之間過早解除的門檻不一致；低速近停時不再重新牽引後再停站。

### 驗證

- Release build：0 warnings、0 errors。
- 自動化測試：86/86 通過；覆蓋交替月台資源保留、後車等待且無碰撞、中央避車線虛擬站折返、站後成對尾軌虛擬節點、進站煞車不回彈、指定反向接續、會話同步推進、軌跡留存策略、固定時刻表封存往返／破損拒絕、雙島四股站內待避／快速車越行及範例 Schema 7 匯入／往返。
- 本輪依使用者指示未執行 Windows 桌面 UI 實測；未執行 Git commit、tag、推送或 Release 發布。

## V3.3.0 - 2026-08-24

### 新增與整合

- 專案格式升級為 Schema 7；V2 寫實引擎與存檔只使用車型、服務類型、停站模式及發車計畫四個正式來源，不再寫出舊執行資料源。依內部測試政策，Schema 1～6 明確拒絕，不做移轉。
- 停站模式改為交易式逐站編輯器，支援停站秒數覆寫、跨站與通過速限；技術 ID 由系統產生並在一般 UI 隱藏，刪除仍被引用的項目會阻擋並說明原因。
- 各車次的車型目錄成為 V2 性能權威來源，實際套用最高速度、加速度、營運／緊急煞車、Jerk、牽引衰減、惰行減速度與車長。
- V2 區間統計加入方向、車輛、車次、車型、服務、停站模式及模擬秒範圍篩選，並累積移動閉塞受限秒數至 UI 與 CSV。
- 新增首頁常駐「建立模擬【V3.3】」按鈕；V2 模式隱藏不使用的 V1 全域性能與排程欄位。手動發車計畫開啟時會自動選到目前使用的班表頁。
- 新增 Schema 7 完整功能驗證範例，涵蓋兩種車型／服務／停站模式、雙向手動派車、指定反向接續、端點退出、三組里程速限與五類空間參考點。

### 驗證

- 隔離 Release build：0 warnings、0 errors。
- 自動化測試：75/75 通過。
- Windows UI 實際載入範例、顯示 6 個手動班表車次、建立並播放至端點作業後，確認未續行車完成清車即消失，`EMU-CIRC-01` 接續 `RUN-UP-006`；進出站時刻表、區間物理、移動閉塞、V3.3 區間統計、速度曲線與運行圖皆有實際資料。
- 本輪未執行 Git commit、tag、推送或 Release 發布。

## V3.2.0 - 2026-08-24

### 新增

- 納入 URCS 五類空間參考點與相容容量分析：中間站、站前折返、站後折返、中央避車線折返、銜接點。
- 中間站提供順／逆行獨立坡度、停站、速度、號誌距離、重疊距離與安全係數；五類參考點設定確認後立即繪入路線圖。
- 專案格式升級為 schema 6，完整保存五類空間參考點與中間站雙方向參數。
- 下行速度曲線新增 V3 車次選單，並在播放後改用所選 `ServiceRunId` 的實際軌跡。
- 新增可直接載入的 V3.2.0 完整功能驗證存檔與逐項人工驗證對照表。

### 修正

- 進出站時刻表改讀派車計畫、`SimulationWorld` 軌跡與結構化事件，保留 `VehicleId`、`ServiceRunId`、上下行、計畫／實際到發及端點退出／折返結果。
- 區間物理明細改為 V1 理論基準與 V2 實際區間分流；區間統計修正 0 秒首班發車與端點退出邊界。
- 首頁摘要改讀 V2 實際世界；未播放前明確標示無干擾基準／計畫預覽。
- 空間折返距離、道岔速度、坡度與停等時間納入端點處理；未續行列車完成停站／清車後立即退出並從路線圖消失。
- 各結果頁加入 `【V3.2】` 標籤；列車運行圖加入發車、抵達、跨站、折返、退出、等待與安全事件標記。
- 修正折返車在移動閉塞控制下把自身誤判為前車，造成指定接續車次永久無法離開端點的問題。

### 驗證

- Release build：0 warnings、0 errors。
- 自動化測試：74/74 通過，包含 schema 1～6、URCS 五類數值向量、雙方向站點、空間折返、首班 0 秒、端點退出及完整功能範例 2,400 秒實際模擬。
- Windows UI 已確認 V3.2 標題、五類參考點編輯、中間站即時路線圖、建模、播放後實際首頁摘要、車次速度曲線與 V3 時刻表。

## V3.1.0 - 2026-08-23

### 新增與修正

- V2 首頁隱藏僅 V1 使用的列車數量、指定班距與首班時間，改由發車計畫作為基準；播放倍率保留。
- 簡易班距與手動班表新增「端點折返續行」。續行列車保留 `VehicleId`、服務類型、停站模式與車型，建立反向新 `ServiceRunId`。
- 未續行列車在端點完成站點停站／清車秒數後退出，並從路線圖與安全計算移除。
- 修復下行速度曲線的 V3 車次識別，建立模擬後立即顯示下行曲線。

### 驗證

- Release build：0 warnings、0 errors。
- 自動化測試：65/65 通過。
- 手動發車計畫可用「折返後接續車次 ID」把前一車次接到指定反向車次；沿用 `VehicleId`、不另生成目標車次，早到等待、晚到記錄延誤。
- Windows UI 已實機確認 V2 首頁隱藏舊 V1 三欄、簡易班距與手動班表的端點折返續行、手動班表的「折返後接續車次 ID」，以及建立模擬後立即顯示下行速度曲線。

## V3.0.0 - 2026-08-23

### 新增

- schema 4，支援 1 → 2 → 3 → 4 串接升級；分離 `SimulationEngineKind` 與 `OperationProfileMode`。
- 車型／服務類型／停站模式目錄、輸入選單與交易式編輯對話框。
- 雙向班距、手動派車、雙端發車、月台／軌道／路徑／折返資源與保守預約。
- 結構化事件、V2 區間統計、P95 與中文 CSV。

### 驗證與界線

- `61/61 tests` 通過；Release build 0 warnings、0 errors；Windows UI 確認 V3 標題、選單／對話框取消、雙向列車與區間統計。
- 不宣稱完整空間幾何、超車執行、多月台最佳化或安全認證。

## V2.1.0 - 2026-08-20

### 新增

- 上方「檔案／設定」選單，以及版本化專案存檔／讀取功能。
- 停站／跨站服務模式、列車等級、車站通過速限及折返後方向別模式。
- 集中式三段版本控制、組件版本資訊與自動化一致性測試。

### 修正

- 改用 Jerk 動態煞停包絡線，避免列車進站時由約 20～30 km/h 單 Tick 歸零。
- 移動閉塞控制沿用動態煞停距離，降低終點占用與高速硬停造成的追撞風險。

### 相容性

- 保留 V1 解析模型及既有公開行為。
- `.mrtsim.json` 使用 schema 2，並可把 schema 1 舊檔升級為全部停站模式。

## V2.0.0 - 2026-08-19

- 加入獨立 V2 `SimulationWorld`、實際營運軌跡、里程速限、移動閉塞、障礙事件及運行圖匯出。

## V1.0.0 - 2026-08-19

- 初始版本：V1 三角形／梯形解析模型與 Windows WPF 操作介面。
