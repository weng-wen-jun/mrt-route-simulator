# MRT 路線進出站時間模擬器 - QA 報告

## 長時間播放效能 Phase 1：修改前基準（2026-09-20）

以 `samples/V4.0.0-完整拓撲執行驗證範例.mrtsim.json` 執行獨立 benchmark runner：

```powershell
dotnet run --project .\tests\MrtRouteSimulator.Performance\MrtRouteSimulator.Performance.csproj -c Release --no-restore
```

基準 runner 先建立與 WPF 相同的 topology `SimulationWorldOptions`（`Full` trajectory retention），再量測單一 ActualWorld、現行雙 world `SimulationSession.AdvanceTo(8000)` 與現行 `PreparePlannedTimeline`。此結果是本機一次基準執行，wall time 不是硬 timing unit test；修改後以同一 runner、同一 sample、同一順序重跑比較。

| 項目 | 修改前基準 |
|---|---:|
| ActualWorld requested / current time | 8000 / 8000 s |
| ActualWorld elapsed | 1661.55 ms |
| ActualWorld fixed ticks / ms per tick | 80000 / 0.02077 ms |
| ActualWorld trajectory / safety / events | 26674 / 3282 / 99 |
| ActualWorld working-set delta / managed-memory delta | +37,482,496 / +15,990,808 bytes |
| 現行雙 world `AdvanceTo(8000)` elapsed / ms per tick | 857.11 / 0.01071 ms |
| 雙 world actual trajectory / planned trajectory | 26674 / 26491 |
| 雙 world actual safety / planned safety | 3282 / 0 |
| 雙 world actual events / planned events | 99 / 98 |
| Planned requested duration | 2536.00 s |
| Planned timeline elapsed / last event | 200.36 ms / 2470.5 s |
| Planned trajectory / safety / events | 25952 / 0 / 94 |

本基準確認目前 WPF 路徑的兩項待修來源：播放 API 會同時推進 ActualWorld 與 PlannedWorld；planned timeline 會依固定預估 duration 推進，而不是以 `SimulationWorld.IsComplete` 為完成條件。`tests/MrtRouteSimulator.Performance` 會保留作為後續比較用 diagnostic，不將 wall time 寫成穩定性 unit test。

## 長時間播放效能 Phase 1：修改後結果（2026-09-20）

同一 benchmark、sample 與 `AdvanceTo(8000)` 範圍重跑；另加入實際 WPF playback 所用的 `AdvanceActualTo` measurement：

| 項目 | 修改後結果 |
|---|---:|
| ActualWorld requested / current time | 8000 / 8000 s |
| ActualWorld elapsed / fixed ticks / ms per tick | 1746.51 ms / 80000 / 0.02183 ms |
| ActualWorld trajectory / safety / events | 5489 / 334 / 99 |
| ActualWorld working-set delta / managed-memory delta | +27,500,544 / +9,811,504 bytes |
| 實際 playback `AdvanceActualTo(8000)` elapsed / ms per tick | 619.08 / 0.00774 ms |
| 實際 playback trajectory / safety / events | 5489 / 334 / 99 |
| 實際 playback 後 PlannedWorld current time | 0 s（未被推進） |
| 舊雙 world `AdvanceTo(8000)` elapsed / ms per tick | 911.54 / 0.01139 ms |
| 雙 world actual trajectory / planned trajectory | 5489 / 26491 |
| 雙 world actual safety / planned safety | 334 / 0 |
| 雙 world actual events / planned events | 99 / 98 |
| Planned max duration（latest dispatch + baseline × 2） | 2748.00 s |
| Planned actual completion / last event | 2590.0 / 2590.0 s |
| Planned trajectory / events | 26491 / 98 |

相較修改前，互動 ActualWorld trajectory 由 26674 降至 5489（約少 79.4%），歷史 safety observation 由 3282 降至 334（約少 89.8%）；實際 playback path 不再推進 PlannedWorld。wall time 與 process memory 會受 JIT／GC／OS 影響，僅作觀察值；固定 tick 仍為 0.1 秒，未改 physics、occupancy、moving block、rear-clear 或 safety decision。

本 Phase 已完成：

- `SimulationSession.AdvanceActualTo()` 與 WPF ActualWorld-only playback。
- `PreparePlannedTimelineUntilComplete(maxDurationSeconds)`，以 `SimulationWorld.IsComplete` 為完成條件，超過 fail-safe 會回報 validation error；記錄 `PlannedTimelineCompletedAtSeconds`。
- topology interactive ActualWorld `Decimated(0.5)` trajectory 與 `Decimated(1.0)` safety history；planned chart 仍使用 Full retention。
- 獨立 benchmark runner 與 retention／completion regression。

尚未解決（刻意留給後續階段）：

- Engine 單 tick < 1.67 ms 的 hot-path 優化。
- simulation worker／single-writer background task／immutable playback snapshot。
- adaptive UI render FPS、hidden-tab lazy rendering、incremental result accumulator。

最終驗證閘門：

- `dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore`：0 warnings／0 errors。
- `dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore`：148/148 通過，0 失敗；包含 fixed 0.1 s、moving block、collision、turnback、passing、rear-clear、ActualWorld-only、completion 與 safety retention regression。
- `dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore`：PASS WPF visual rules；完整 sample matrix、計畫時間軸、雙向預覽、速度圖、CSV／PNG／PDF 輸出通過。
- `tests/MrtRouteSimulator.Performance` Release build／benchmark：通過；`git diff --check`：通過。
- 本輪未執行 60× 原生桌面連續播放人工 smoke；這仍屬 Phase 2／桌面驗收範圍，不以離屏 WPF runner 代替。

## 大型 sample builder、PDF 分頁與 GPT-use 乾淨整合（2026-09-19）

- 本節結果來自 `codex/integrate-large-route-clean`：以最新 `origin/GPT-use` 為基底，非直接 merge 舊分支；保留 13 個 Schema 8 sample、臺中主要站簡化 sample 與 `.github/pull_request_template.md`。
- 本次重新執行 `dotnet build MrtRouteSimulator.slnx -c Release`、Engine runner、WPF runner 及 `git diff --check`；以下數字是本次整合分支實際結果，不沿用 `a61d4bc` 的舊執行紀錄。

- 速度圖按 VehicleId 串接上下行及折返，保留計畫預覽／實際截至目前的區別；重設及換檔不保留舊列車。移動閉塞仍可獨立按方向、配對及時間篩選，列車退出後可查歷史。
- 尾軌返回反向終點月台後產生到站、停站及出站事件；0 秒停站仍等待接續班表，正常停站案例須滿足反向停站時間。
- 時刻表、區間及比較使用結構化站號／月台識別；TrainCenter 車頭經過中心但尚未到站時，不提早完成區間。上行顯示里程、預覽結果初始化及全程時間摘要一併修正。
- Release build：0 warnings／0 errors；Engine：145/145，0 失敗；WPF runner：PASS；`git diff --check`：PASS。完整 WPF runner 包含 13 個範例、720／1200px 速度圖、閉塞方向篩選、完整拓撲及 TrainCenter 範例推進 3600 秒，以及 CSV／PNG／PDF 輸出。
- 臺中主要站簡化 sample 經本次整合修正為可驗證的 minimal baseline：直達模式的 O26 為合法終點停靠，topology 內明列 10 段主線 `directedConnections`；仍只使用 synthetic test values，不宣稱正式路線資料。
- `TopologyScenarioBuilder`／`TopologyScenarioValidation` 已加入可重用的 minimal baseline → station chain → service pattern → turnback → passing → timetable 分階段流程；Structural／Operational smoke gate 與 3 組 regression tests 通過。`samples/README.md` 已補 Scenario Manifest 與正式／synthetic 資料界線。
- Legacy port migration 已加入明確 edge assignment API、缺資料盤點與 WPF 遷移引導；選擇相容讀取時會明確保留「方向尚未確認」狀態，不從示意位置猜測側別。Engine migration regression 已加入，完整桌面互動仍待驗收；完整 Engine runner 為 145/145。
- 輸出證據位於 `artifacts/output-qa/`。PDF 分頁改為各頁重繪標題、圖例、座標軸與頁內標籤；`complete-diagram.pdf` 以 `pdfinfo` 確認為 A4 兩頁，並以 Poppler 渲染檢查白底、頁面標題、座標軸及跨頁列車標籤未被切斷。頁內同點標籤重疊仍屬來源圖表的既有呈現限制。
- 原生桌面驗證因 Computer Use 應用程式核准逾時未完成；以上為 WPF 離屏與程式驗證，不代表不同 DPI 或連續桌面播放已完成驗收。

## 建立與讀檔接軌防堵（2026-09-13）

- 新增獨立實體接軌側別與共用 `TrackPortRules`。同節點同側轉向拒絕，加入 ServiceRoute 或移除 directedConnections 不可繞過；合法備用連接與原地換端保留。Schema 8 讀寫、WPF 編輯器及失敗讀檔交易保護納入回歸。
- 模板不再枚舉所有共享節點組合。站後折返 X1／X2 端點及兩條 facility traversal 修正；兩份 V3.3 範例及 baseline 增設實體 ENTRY／EXIT 軌道，baseline 移除西端直接跨股道捷徑並校正中央上行月台。13份範例均補入固定側別。
- 快速建線、軌道分割及設施精靈延續側別；尾軌精靈改為進軌、同軌去回、出軌四段路徑。新建尾軌／袋狀軌的進出渡線各25m，範例遷移的進出軌各180m，均屬實體運行距離。
- 全13檔主圖／編輯器於720／1200px無配線警告，折線內部及有向接軌突折檢核通過；完整範例0／120／311.5秒停靠仍與月台中心對位。另目視站前／站後及baseline輸出。輸出位於 `artifacts/station-rules-visual/`。
- Release build：0 warnings／0 errors。Engine：135/135；WPF runner：通過，含全部範例載入、計畫時間軸、雙向預覽、側別保存，以及無效接軌保留既有專案／會話／檔名／標題。
- 相容性邊界：兩端皆未填側別的舊軌道仍可載入，不宣稱已驗證其實體轉向方向；不在讀檔時從示意位置猜測側別。舊檔引導遷移、不同DPI及全程桌面連續播放仍待辦。以上離屏WPF檢核不是完整桌面人工驗收。

## 全範例轉角盤點與問題清單（2026-09-12）

- 新增 `ROUTE_LAYOUT_ISSUES.md`，列出9類易復發問題、13個範例的結果，以及建立路線／讀檔防堵入口；TODO保留根源修正未完成。
- WPF共用最終幾何使用水平切線與平滑側線過渡，單軌折返展開示意喉區並統一防出界；明確節點配置不再被中心里程映射覆蓋。模型、JSON範例及模擬距離本輪未修改。
- 全13檔、主圖／編輯器、720／1200px驗證已繪出rail內部及跨edge接點突折角小於45度（不把止衝標記及合法停車換端算為彎軌），並保留三站實際停靠對位。不能呈現的轉向顯示配線待修，不以垂直線或回頭曲線掩蓋。
- PDF-CentralPocket、PDF-FrontTurnback、PDF-RearTurnback、V4 baseline仍有配置警告；警告項目未計作合法配置通過，具體清單見新文件。模板自動組合所有共享端點traversal是後續優先調查入口。
- Release build 0 warnings／0 errors；WPF runner通過（含上述已知配置警告）；Engine完整回歸127/127。原執行目錄已更新。離屏目視及程式檢核不等同不同DPI／全程播放人工驗收。

## 新增中央待轉軌示範列車（2026-09-12）

- 完整 topology 範例新增 POCKET-02／POCKET-DEMO-DOWN，400秒發車，使用 POCKET-TURNBACK 停站模式；原有班次保留，simulation.trainCount同步7筆派車。
- 回歸按VehicleId確認新增列車：553.3秒在POCKET-M抵達停點，583.3秒於同軌開始反向返回，軌跡實際進入UP-M-W，完整情境3000秒後全部退出且無碰撞。全部範例3600秒有界運行與多車長換端回歸通過。
- Release build 0 warnings／0 errors；Engine 127/127；WPF全範例載入、雙向預覽及版面runner通過。此輪新增範例與驗證，未變更模擬核心。

## 端點月台 5 px 偏移補正（2026-09-12）

- 使用者回報 311.5 秒東站停靠仍未對齊。根因為 DrawPlatforms 將月台限制在軌道端點內縮 5 px 的範圍，導致西站月台右移、東站月台左移，而列車仍依物理中心呈現；上一輪僅測中央站未涵蓋此情境。
- 移除端點內縮造成的整體平移；最小顯示寬度仍以真實中心向兩側展開。未修改 Engine、範例或停車位置。
- 新增 0／120／311.5 秒與 720／1200 px 組合，先確認實體車體中心，再於 Measure／Arrange 後以 TranslatePoint 取得實際圖示與月台中心。修正前測試重現 -5 px；修正後六組均小於 0.51 px，13 範例主圖／編輯器與載入矩陣通過。
- Release build 0 warnings／0 errors；完整 Engine 127/127。修正版已建置回原執行目錄；已檢視 311.5 秒東站 WPF 輸出圖，新版桌面及不同 DPI 尚未重驗。

## A～D 配置及停靠對位修正（2026-09-12）

- 完整 topology 範例：中央站上行月台移至 UP-M-W 的 130–270 m，與下行同一站中心；全部月台採 TrainCenter，同步設施到發錨點。中央袋狀軌新增實體進出道岔軌段，雙端 crossover 長度改為 180 m，保留站外喉區及尾軌。
- 主畫面與編輯器共用 StationChainageProjection 的 edge-offset 映射，軌道、月台、列車使用同一位置函式；明確的四段折返進路以平滑曲線銜接，連接點位於軌道端點而非月台停車標。
- 原目錄 Release build：0 warnings／0 errors；完整 Engine runner：127/127 通過（擴充既有站中心測試）。WPF runner：PASS，13 範例 × 主圖／編輯器 × 720／1200 px，共 52 圖。
- 新增完整範例回歸：所有月台中心里程及畫面 X 一致、全部有向接軌端點直接重合、渡線及袋狀軌無垂直／逆向反折、120 秒中央站停靠列車的實體中心與圖示皆對齊月台。既有多車長換端、完整越行／折返與全部範例有界運行亦通過。
- 抽查修正版 720 px 主圖及 1200 px 編輯器 PNG；使用者關閉舊程式後已建置回原執行目錄。新版桌面互動及不同 DPI 尚未重驗，下節先前桌面抽查不代表本次幾何已完成全部人工驗收。

## 桌面實機驗收進度（2026-09-11～12）

- 使用既有 Release WPF 執行檔，透過 Windows 檔案對話框實際讀取 baseline 與完整 topology 範例；本輪未儲存或改寫範例。
- baseline：已檢視主畫面一般／窄視窗（擷取寬度約 1426／1166 px）、編輯器一般／窄視窗（約 1346／974 px）。站名、月台及設施圖例在抽查畫面可辨識；窄主視窗另抽查 00:00:04、00:01:22、暫停於 00:02:35 的列車標記，未見站名遭遮擋。
- 完整 topology：已檢視窄主視窗（約 1166 px）及最大化主視窗（1920 px）。抽查普通車停站與快速車越行（00:00:42、00:02:08、00:03:18）、袋狀軌車次（00:13:00、00:15:08）、尾軌停留與返回上行（00:26:24、暫停於 00:27:34）；上述畫面未見站名與列車標記互相遮擋。
- 完整 topology 編輯器：2026-09-12 已補驗一般／窄視窗（約 1346／974 px），站名、月台及四項設施圖例可辨識，抽查畫面未見標籤重疊。取消編輯後返回原專案。
- 修正窄主視窗頂端摘要卡長文字硬裁切：五個動態摘要值使用換行，摘要列高度自適應並保留 100 DIP 最小高度。新版實機約 1166 px 窄視窗已確認路網與播放後速度摘要完整換行；未改 Engine 或範例資料。
- 修正後驗證：Release build 0 warnings／0 errors；Engine 127/127；WPF runner 回報 PASS WPF visual rules。
- 工具限制複核（2026-09-12）：嘗試啟動 Windows SystemSettings.exe 後，桌面工具回報 launched app did not expose a targetable window；重新列舉仍無設定視窗，未變更顯示縮放。另實測完整範例切至 1 倍速，00:05:01 到站減速、00:05:11 停站、00:05:22.7 暫停的畫面未見標記遮擋；但工具提供離散截圖，且 accessibility 時鐘與影像存在時間差，故不視為逐幀通過。
- 本節為目前桌面縮放下的畫面抽查，並非全程逐幀、不同 DPI 的完整通過證明。兩範例主畫面／編輯器一般及窄視窗均已有實機抽查；仍須補足不同 DPI 與折返／交會關鍵畫面的連續檢查，因此 `V4-UI-TRACK-DIAGRAM-MANUAL-01` 保留未勾選。

## 現行驗證（2026-09-11）：站中心里程與完整車體折返

- `tests/ValidateStationConstruction.ps1` 完整通過：Release build 0 warnings／0 errors，Engine **127/127**；13檔主圖／編輯器各720／1200 px，共52圖的實體月台對位與版面檢核通過；13檔完整載入、計畫時間軸、雙向預覽及無效專案保留原會話通過。
- 新增起始站中心0K、負尾軌及終點外延伸里程。七PDF月台使用車體中心錨點，runtime保留車頭cursor；即時標記與位置欄位改顯示實際車體中心。
- 80／120／140 m車長、前後折返兩條進路、完整範例尾軌／袋狀軌及車尾恰落節點，共18個完整運行情境逐次驗證換端前後逐edge占用相等，且完整退出、無碰撞／停站違規。新增 TURN-004／005 及月台 STATION-002／003，拒絕不足整車的停靠及反向返回進路。
- 修正26個PDF端點月台虛報有效長度、12個舊範例起點車尾超出月台、完整V4尾軌／袋狀軌容量不足、越行入口cursor正規化漏接，以及中心停點造成速度預覽未判定終點。逐檔資料見 `samples/AUDIT-2026-09-11.md`。
- 13範例加預設六站各推進3,600秒：全部列車退出、碰撞0、停站違規0；三／四股道及舊越行情境確有完成超越，完整V4尾軌與袋狀軌合計3次換向。紀錄為 `artifacts/station-layout-qa/sample-audit.tsv`。
- 已檢視本輪前／後折返窄版PNG，站名及0K對準月台本體。自動化為WPF程序內驗證，未代替桌面檔案對話框、所有DPI及逐幀人工驗收；未重啟使用者目前執行中的程式。

## 歷史驗證（2026-09-09）：PDF 站型與模擬

- 建置規則化：新增 STATION-001／TURN-001～003 共用驗證，原地折返停點／月台／首末站不一致即拒絕；新增正式 WPF 測試專案及 `tests/ValidateStationConstruction.ps1`。Release build 零警告／零錯誤，Engine 121/121；WPF 七站型 × 兩寬度、故意錯位／重疊／越界、20 設施圖例、13 個範例完整載入與無效專案保留會話均通過。規則及未涵蓋範圍見 `STATION_CONSTRUCTION_RULES.md`。

- 站前折返站內定位：修正 PDF-FrontTurnback 的兩座月台、折返點與反向發車點，全部使用月台中心的實體 offset；新增同 edge 同位置原地折返處理。實際／計畫模式及第二月台進路 regression 確認整段停等速度零、offset 不變。262.7 秒 WPF 主畫面已檢視，列車與 A 站月台中心對齊。120/120 完整測試通過，Release 零警告／零錯誤；全部範例讀檔初始化通過。

- 讀檔追修：實際呼叫 `ConfigureTopologyProjectForPlayback` 重現 PDF-FrontTurnback／PDF-RearTurnback 在 `PreparePlannedTimeline` 的「topology 折返列車缺少 runtime cursor」。停車超越保護原以顯示里程回推位置，會在反向停點遺失 cursor；改以 resolved stop 的實體 traversal／offset 定位並保留速度繼續煞車。修正後 13 個範例及啟動六站全部通過讀檔初始化、計畫時間軸與速度預覽。
- 新增 BasicPhysics／Independent 計畫模式的站前與站後折返回歸，驗證完成接續退出且無 LEGACY 軌道。Release build 零警告／零錯誤，完整測試 120/120。此次未操作桌面檔案選擇對話框。

- 全範例合理性追修：逐檔結果見 `samples/AUDIT.md`。13 個 JSON 加預設六站均推進 3,600 秒，全部退出、零碰撞／停站違規；補舊範例股道／月台編號、三股道及站後折返名稱，修正水平袋狀軌和設施標籤重疊。
- 發現預設六站上行起站錯選 O01，修正線性轉換依進路首停點選 O06；另發現 541.2 秒對向列車假碰撞，修正 graph-distance 尋路兩端轉向限制及 incoming traversal 搜尋狀態，保留合法繞行距離。新增回歸後完整測試 119/119；Release build 0 警告／0 錯誤。

- 站名對齊追修：主畫面及編輯器改用實際月台符號的外框中心定位站名，不再使用停車點 X；端點文字區域對稱縮窄而不平移。已檢視預設六站主畫面 1200 px 與編輯器 720 px 的 WPF 圖像，確認 O01～O06 站名與月台垂直對齊。Release build 0 警告／0 錯誤；不改變停車點或運行資料。

- 使用者截圖追修：預設 O01～O06 六站、10 節點／16 edges 的三段式單尾軌折返，原自動排版把兩條 crossover 擠在相同 X 座標，平行線錯開後形成直立迴圈。主畫面與編輯器現在共用折返路徑展開：去程及尾軌沿抵達股道水平向外，回程斜接出發股道。未新增軌道或修改運行資料，因此不是 PDF 雙尾軌＋交叉渡線的自動轉換。
- 直接從 MainWindow 預設設定建立六站專案並擷取 720／1200 px 圖像，已檢視主畫面 1200 px 及編輯器 720 px，確認兩端無直立迴圈；Release build 0 警告／0 錯誤，完整測試仍為 118/118。尚未重啟使用者目前開啟的程式。

- Release build：0 warnings、0 errors；完整自動化測試 118/118 通過，0 失敗（命令同下方歷史紀錄）。
- 新增七種 PDF 站型，連同原六個範例共 13 個 Schema 8 專案均可往返、建立 world、推進 3,600 秒並完成退出，無碰撞與停站違規。
- 新增八項回歸，涵蓋示意欄位往返／非法值／不影響運行、七站型運行、站前及站後兩組折返進路與同車接續、三／四股道側線及實際越行、中央袋狀軌停靠，以及對向互斥、車尾淨空釋放和重設重現性。三股道完成上行越行，四股道完成雙向越行。
- WPF 程式內驗證：直接呼叫主畫面和編輯器的繪圖程式，在 720／1200 px 寬產生七個新範例與五個根目錄既有範例的圖像；已抽查主畫面四股道窄圖、站後折返寬圖、編輯器站前折返及中央袋狀軌圖。主畫面使用實際 90 秒快照。擷取時隔離 SizeChanged 的重新繪圖事件，避免驗證畫布被清空。
- 編輯控制項驗證：七選項下拉選單、重新起稿按鈕、草稿不污染原專案、示意欄位提交保留、袋狀軌下行通過進路綁定與 runtime 建立均通過。
- 限制：本輪為 WPF 程式內控制項與離屏繪圖驗證，未完成桌面手動的讀取／儲存／播放流程、參數對話框輸入或匯出驗收。舊完整範例密集標籤與全寬窄矩陣的人工驗收仍待完成。
- 未 commit、tag、push、發布或調整版本。

## 歷史驗證（2026-09-08）

- Release：`dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore`，0 warnings、0 errors。
- 完整測試：`dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore`，110/110 通過、0 失敗；新增三組驗證目標 metadata regression，涵蓋舊 API、物件／欄位解析、dispatch 與 route 目標。
- UI：主選單直接開啟工作區對應頁；快速起稿收合後播放倍率仍可操作，錯誤訊息在主內容區。工作區共用驗證訊息，依可辨識 metadata 定位頁面、物件及欄位；沒有穩定列 ID 的派車錯誤僅定位頁面。
- Windows 實測：六站範例建立後側欄收合，倍率由 20× 改為 1×，播放至 06:00:13.9 後暫停，列車標記位於軌道上。
- Windows 實測：車長輸入 abc 後，驗證顯示錯誤、切頁被阻擋、套用提示未變更；點擊錯誤回到 DEFAULT_VEHICLE 的長度欄。改回 92 後錯誤清除；點擊未使用軌道警告可定位對應軌道。取消後重開保留原始車長 92。
- 參考圖樣式：主畫面及編輯器改為棕紅色 5 px 軌道、方向箭頭、實心矩形月台及平滑支線轉角；月台仍由實際 edge offset 決定，短符號使用最小寬度並提供 tooltip。列車與軌道共用同一幾何，不改變 runtime 或 Schema。
- Windows 視覺檢查：六站編輯器於約 1682 px 與 1250 px 視窗寬度均可辨識所有站名、月台及尾軌；已修正右側站點被壓縮至同一位置的問題。完整 topology 範例成功載入，主畫面顯示 9 節點／13 軌道區段，並檢視編輯器示意圖。
- 未完成：完整範例密集設施附近仍有站名／設施標籤重疊；baseline 與完整範例完整的寬窄視窗矩陣及動態列車遮擋需續驗。彎曲軌道上的月台符號仍為起訖點之間的直線矩形。本輪未重測匯出，不代表全部 WPF 驗收完成。
- 未執行 commit、tag、push、Release 或版本調整。

## 歷史驗證（2026-09-05）

- Release build：0 warnings、0 errors。
- 自動化測試：107/107 通過，0 失敗。
- 六個 Schema 8 範例均推進 3,600 秒：沒有未使用 edge、legacy facility edge 欄位、未具名續行、碰撞或停站違規，且所有列車均完成退出；V3.4 範例實際產生快速車越行事件，V3.3 折返範例實際完成袋狀軌折返與指定反向接續。
- 範例尾軌／袋狀軌改為同一實體 edge 的正反向 traversal，移除孤立分支與重複回程 edge；facility 精靈會保留節點原有自然轉向，validator 會拒絕不連續或以中段停點跳接其他 edge 的折返。
- Windows WPF 實機載入並播放完整 topology 範例，確認平行主線／待避線可辨識，設施支線以實線連接器接回既有軌道，列車位置取自同一 edge-local 幾何。本輪未重做三種匯出。
- 依軌道配線圖參考重整主畫面與 topology editor：軌道統一為青色實線、移除每站假性垂直連線與畫面上的 edge ID，月台依實際起訖 offset 畫成長色帶，站碼／站名成為主要標籤；尾軌沿抵達方向主線股道直線延伸並以止衝結束，回程切換股道才畫實際轉向。列車仍依同一 edge-local 幾何定位。
- 新配線圖程式已完成 Release build 與 107/107 regression；本次執行環境未提供原生 Windows App 控制介面，因此尚未對新樣式完成不同視窗寬度、完整範例與 topology editor 的實機視覺複核。
- 未執行 Git commit、tag、推送或 Release 發布。

## V4.0.1 完整 topology 情境驗證（2026-08-31）

結論：新增的完整 Schema 8 情境會把快速越行、中央袋狀軌折返及雙端 crossover 尾軌折返放入同一個 world，推進至所有車次退出。首次執行發現快速車合流時會把停在平行 local edge 的普通車誤判為負間距；已改為保持普通車待避至快速車車尾 rear-clear，並排除平行 edge 的假性共線安全配對。

- 完整範例：`V4.0.0-完整拓撲執行驗證範例.mrtsim.json` 使用 `DirectedTrackConnectionDefinition`、`TrackPosition` 中段折返停點、tail／pocket／passing traversal 及手動接續車次。
- runtime regression：驗證快速車確實走過 `PASS-M`、袋狀軌與雙端尾軌抵達實體停等位置、四類 resource 皆於 rear-clear 後釋放、無 `LEGACY:` identity 或碰撞、所有車次完成，且時刻表／區間統計／CSV 可直接消費 topology 結果。
- 建置：Release 0 warnings、0 errors；自動化：100/100 通過，0 失敗。
- 本輪未重做 Windows UI 手動驗收；未執行 Git commit、tag、推送或 Release 發布。

## V4.0.0 topology-native 驗證（2026-08-31）

結論：V2 `SimulationWorld` 已以 Schema 8 topology 取代 Route／legacy Infrastructure runtime。Release build 為 0 warnings、0 errors；自動化測試 99/99 通過；Windows WPF 的 Schema 8 讀檔、播放、結果與三種匯出均已實測通過。

- Topology model：`InfrastructureGraphV4` 使用 node／edge dictionary 與 outgoing、incoming、station-platform、edge-platform index；`TrackEdgeDefinition` 不引用 StationId，`PlatformDefinitionV4` 以 `TrackEdgeId + local offset` 定位，ServiceRoute 使用有序 `DirectedTrackTraversal`。
- Validator：會拒絕不存在 node、無效 edge 長度／速限、無效月台 offset、缺漏 traversal edge、edge 方向不相容、相鄰 traversal 不連續，以及錯誤 station／platform／route 參照。
- 道岔轉向：`DirectedTrackConnectionDefinition` 讓 switch／crossing node 可拒絕未宣告的 edge-to-edge transition；path finder、service route 驗證與 runtime movement 使用相同規則。沒有宣告限制的普通連接點仍維持一般連通性。
- Linear builder／projection：三站線會建立四條逐區間雙向 edge，而非 legacy 全線兩條 edge；forward／reverse offset 可投影為 route-local chainage，重複 edge 的 loop 必須傳入 traversal index，避免歧義。
- Transitional state／主線 movement：`WorldTrainState`、`TrajectorySample`、`SimulationEvent` 均輸出同步的 `TrackEdgeId + OffsetMeters + ServiceRouteTraversalIndex`；正常主線依該 cursor 推進，跨 edge 後可由 route projection 回驗至相同 chainage，`PositionMeters` 為 projection cache。
- Resolved stop：`ServiceRouteStop` 的 candidate platform 會解析為有序 traversal 上的 `TrackPosition`；正常主線的煞車距離／到站吸附消費它，上行中間站 stop 固定在抵達 edge 的終端。
- Runtime：V2 world 不提供 compatibility `Route` 或 legacy `InfrastructureGraph`；快速表單 Route 只在建構前轉成含實體尾軌／resource／turnback operation 的 Schema 8 draft。
- Facility／safety：tail、pocket、turnback、passing 均採 physical traversal；快速線性起稿的 terminal turnback 含 crossover edge／switch resource。折返停點可精確定位到 `TrackPosition`，中段停點只允許同一 edge 的立即反向 traversal；footprint、rear-clear resource release、parallel edge isolation 及 graph safety 都有 regression。
- Results：時刻表、區間統計、運行圖與 CSV 不要求 Route，WPF 的 Schema 8 讀取、圖形與匯出路徑均已改為 topology context。
- Windows UI：載入 `V4.0.0-topology-baseline.mrtsim.json` 後建立 6 列車／3 站 topology world，播放至 06:02:37.7，路線圖顯示實體 edge cursor、時刻表顯示實際到離站／退出事件；CSV、PNG、PDF 均成功匯出並讀回。未執行 V4 Git commit、tag、推送或 Release 發布。

### V4 正式案例覆蓋

下表列出本輪自動化可證明的 topology acceptance。

| V4 topology 驗收案例 | 目前狀態 | 尚缺驗收 |
|---|---|---|
| Schema 8 round-trip／reference rejection | 通過 | 自動化 |
| 道岔有向轉向／physical turnback 中段折返 | 通過 | 自動化 |
| 無 Route V2 world | 通過 | world.Route／Infrastructure 會拒絕 |
| 尾軌、袋狀軌與 facility continuation | 通過 | 實體 traversal／VehicleId regression |
| 平行 passing edge 與快車越行 | 通過 | footprint、rear-clear、graph safety regression |
| 結果／CSV 無 Route | 通過 | topology result context regression |
| Windows UI 操作 | 通過 | Schema 8 讀檔、播放、時刻表、CSV／PNG／PDF |

上述 Schema 8 round-trip、V4 baseline sample 與已完成的 Windows topology UI 操作均保留為歷史通過紀錄；目前 `TODO.md` 仍有唯一未完成項目 `V4-UI-TRACK-DIAGRAM-MANUAL-01`，一般及窄視窗已有本輪桌面抽查，仍須補足不同 DPI 與關鍵畫面的連續驗收。

### 現行建置與自動化

- 方案：`MrtRouteSimulator.slnx`
- 版本來源：`Directory.Build.props` 的 `MrtVersion = 4.0.0`
- 存檔格式：`schemaVersion = 8`（V2 topology）
- 建置：`dotnet build MrtRouteSimulator.slnx -c Release --no-restore --artifacts-path artifacts/v4-phase-a-c`
- 結果：Engine、Tests、App 全部成功，0 warnings、0 errors
- 測試：99/99 通過，0 失敗

自動化覆蓋 Schema 7 往返、舊雙資料源拒絕、進站控制、派車／接續／退出、資源、折返、越行、統計、完整範例與 10 項 topology regression；再以本節 Windows 操作確認 Schema 8 的實際資料流與三種匯出。這不表示本程式具備安全認證或超出 scope 的完整聯鎖模型。

## V3.4.2 進站精停驗證（2026-08-29）

結論：V3.4.2 的 `StationStopController` 已接入 `SimulationWorld` 排定停站。高速段採距離－速度煞車曲線，並以 Jerk 受限煞車包絡線預測停點；最後 12 m 以終端速度曲線收斂至 2 m 近停吸附邊界。Release build 為 0 warnings、0 errors；自動化測試 96/96 通過。

- 控制器單元驗證：高速進站會壓低一般線速；全煞停點預測超過目標時要求營運煞車；低速精停與 2 m 吸附模式皆有固定輸出。
- 引擎回歸：以 980～990 m 的近站低速限制迫使列車降速，限制結束後到 O02 的峰值速度仍受 3 m/s 精停上限約束，未回到一般線速牽引。
- 相容性：障礙物、越行、移動閉塞均繼續對控制器輸出取更低允許速度；既有端點退出、折返、資源、越行與 Schema 7 測試均通過。
- 本輪未執行 Windows UI 實測；未執行 Git commit、tag、推送或 Release 發布。

## V3.4.1 引擎與 Schema 驗證（2026-08-29）

結論：V3.4.1 已完成多月台平衡、資源占用時間軸／CSV、多候選與反向同時越行、車型獨立停站模式、實體站五類分類與範本、三類折返連續軌跡、站後尾軌反方向月台及 V1／V2 同條件比較。Release build 為 0 warnings、0 errors；自動化測試 94/94 通過。

- 多月台：三座同方向月台的同步三車次依 `Automatic` 平衡配置；資源占用分析可分別統計 `PLATFORM:*` 與衝突區。
- 越行：既有雙島四股案例保持普通車待避到快速車完成；多候選案例選擇較近的可達設施；上下行案例的兩段越行保留期間重疊、資源 ID 分離且無碰撞。
- 尾軌：下行／上行尾軌均有多筆固定子步進樣本；返回端點後的到達、發車皆使用反方向月台。
- 站前／中央避車線：`TURNBACK:*:OUT` 與 `TURNBACK:*:RETURN` 軌跡皆有非零虛擬位置與固定 0.1 秒樣本；兩種折返均完成無碰撞的反向接續。
- Schema／分析：車型預設停站模式、實體站完整分類、五類範本及 V1／V2 比較皆完成 Schema 7 往返或結構化輸出測試。
- Windows UI：Release 實機確認車型目錄連續觸發只保留一個視窗、最小化後會還原相同視窗、取消關閉後才重建新視窗；車型、服務類型、停站模式、發車計畫、基礎設施及空間參考點入口均使用同一登錄守衛。未執行 Git commit、tag、推送或 Release 發布。

## V3.4.0 引擎與 Schema 驗證（2026-08-27）

結論：V3.4.0 的站前交替月台、目的月台／折返資源保留、中央避車線虛擬站折返、站後成對尾軌虛擬節點、進站煞車近停不回彈、Schema 7 範例匯入、固定時刻表封存與後續架構整理已通過引擎自動化驗證。Release build 為 0 warnings、0 errors；自動化測試 86/86 通過。

- 站前折返案例：兩班列車分別停靠 A／B 月台；兩月台皆保留時第三班車記錄 `WaitingForResource`，前車折返發車後才進入釋放的 A 月台，全程無碰撞。
- 中央避車線案例：`Turnback` 停站模式只允許設在 `CentralSidingTurnback` 的非端點虛擬站；列車停靠、保留月台與避車線資源，並以指定反向車次接續。
- 站後尾軌案例：成對下行／上行 `TailTrack` 共用 `TAIL:<TurnbackId>` 虛擬節點；列車依到達方向駛入、停等後由反方向尾軌返回端點站接續，尾軌與衝突資源全程保留。
- 近停煞車案例：`SimulationWorld` 的進站煞車鎖定與 2 m 到站吸附門檻一致，進站煞車軌跡不得重新牽引加速。
- 範例：`V3.3.0-端點站前與中間站中央避車線折返檢核.mrtsim.json` 已通過 Schema 7 反序列化與往返測試。
- 架構：`SimulationSession` 會以相同固定 Tick 同步推進實際與計畫世界；完整、降採樣與僅事件軌跡留存策略不改變結構化事件序列。
- Schema 7：JSON 若包含舊 `servicePatterns` 或 `serviceRuns` 屬性會明確拒絕；正規文件模型不再帶有雙資料源欄位。
- 固定時刻表封存：只允許完整結束的 V2 世界匯出；封存可完整往返、保留實際到離站結果，且版本錯誤或車站對不上時會拒絕、不污染目前設定。
- 本輪依使用者指示未執行 Windows 桌面 UI 實測，亦未執行 Git commit、tag、推送、正式 Release 封裝或發布。

## V3.3.0 歷史 QA（2026-08-24）

結論：V3.3.0 的 Schema 7、停站模式、發車計畫、車型性能、端點退出／折返接續、五類空間參考點、速度曲線與 V3.3 區間統計已通過自動化及 Windows 桌面實測。隔離 Release 建置為 0 警告、0 錯誤；自動化測試 75/75 通過。

## Windows 桌面實測（V3.3 歷史驗收）

使用隔離建置的 `MRT路線進出站時間模擬器.exe` 載入 `samples/V3.3.0-完整功能驗證範例.mrtsim.json`，實際操作讀檔、設定檢視、建立、播放、暫停及結果分頁。

| 項目 | 結果 | 實測證據 |
|---|---|---|
| V3.3.0 主畫面 | 通過 | 視窗標題與摘要顯示 V3.3.0；V2 模式隱藏 V1 全域性能及舊排程欄位，動畫頁常駐「建立模擬【V3.3】」 |
| Schema 7 讀檔 | 通過 | 五站驗證線、三組速限及五類空間參考點在讀檔後立即出現 |
| 停站模式 | 通過 | 交易式編輯器顯示快速車逐站設定；V02／V04 跨站速限與 V03 25 秒停站可見 |
| 發車計畫 | 通過 | 編輯器自動選到目前的「手動班表」，顯示 6 車次；`RUN-DOWN-001` 勾選端點續行，保留指定車輛／車次資料 |
| 建立模擬 | 通過 | 顯示「V2 實際營運模擬建立完成：6 列車、5 站、固定 Tick 0.1 秒」 |
| 即時路線圖 | 通過 | 上下行列車同時運行；五類參考點、速限區及折返設施即時顯示 |
| 下行速度曲線 | 通過 | 建立後出現 `RUN-DOWN-001` 計畫預覽，播放後切換為實際速度－時間軌跡 |
| 端點退出 | 通過 | 播放至約 06:17 後，未續行車已從路線圖及即時列車狀態消失，不再形成後車追撞假象 |
| 指定折返接續 | 通過 | 同一 `EMU-CIRC-01` 由 `RUN-DOWN-001` 接續 `RUN-UP-006`，時刻表標示「折返接續」 |
| 進出站時刻表 | 通過 | 上下行計畫／實際到發、停站、延誤、跨站、折返接續與「退出營運」均有資料 |
| 區間物理明細 | 通過 | 完成／運行中區間、實際峰值、加速／巡航／惰行／減速及控制事件均有資料 |
| 移動閉塞與煞車 | 通過 | 煞車、配對、方向、狀態、時間窗與障礙物控制可用；晚期只剩單車時清楚顯示目前無符合配對 |
| V3.3 區間統計 | 通過 | 實測時顯示 23 完成、1 運行中；方向、車輛、車次、車型、服務、停站模式、秒範圍篩選及「閉塞受限 s」欄可見，含非零控制秒數 |
| 列車運行圖／匯出 | 通過 | 計畫與模擬軌跡、上下行線、事件點及事件表均有資料；PNG／PDF／CSV 控制可見 |

## 驗收界線

本軟體是營運與號誌概念模擬器，不是可部署的鐵路安全系統。Schema 8、physical facility traversal、occupancy／footprint、topology-native safety、V2 內部 Route 相依移除與已完成的 V4 UI／正式驗收均保留為歷史通過事項；依現行 `TODO.md`，目前尚未完成的 V4 交付項目為 `V4-LEGACY-PORT-MIGRATION`、`V4-SAMPLE-TAICHUNG-AIRPORT-01` 與 `V4-UI-TRACK-DIAGRAM-MANUAL-01`。PDF 分頁與通用大型 sample scenario builder 已完成；桌面項目仍只完成部分抽查，尚待不同 DPI、折返／交會關鍵畫面與連續播放驗收。

以下屬遠期修正或非產品目標：

- 指定真實路線的坡度、曲率、黏著、車型與時刻資料校準，以及高負載效能測試。
- 完整單線共用運轉、聯鎖失效模型與營運最佳化。
- ATP／ATO／ATS、安全完整性等級或現場設備認證。

歷史需求已整理於 `CHANGELOG.md`。
