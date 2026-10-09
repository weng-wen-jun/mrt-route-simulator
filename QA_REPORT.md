# MRT 路線進出站時間模擬器 - QA 報告

## 介面翻新第一輪（2026-10-09）

- Release build：Engine、Automation、App、Tests、WpfTests、PlaybackBenchmarks 六個專案皆 0 warning／0 error。MCP 專案未建置：`ModelContextProtocol` 2.2.0 不在本機 NuGet 快取，還原需下載，待使用者確認；MCP 只依賴未修改的 Automation／Engine。
- Engine runner：198/198 通過（與改動前基準相同）。
- WPF runner：新增 `TrackDiagramThemeTests`（UiTheme、膠囊列車、雙色軌道、月台圓點、站名徽章、占用彙整、亮燈開關與圖例）8 項全部通過；`--visual-rules-only`（全範例主圖／編輯器版面矩陣）、`--playback-only`、`--mcp-only`、`--validation-warning-only`、`--interface-scale-only`、`--speed-only`、`--output-only` 通過。
- 未能在本次驗收完成：驗收期間 Windows session 已中斷連線並鎖定（`query user` 顯示 `Disc`），離屏渲染輸出空白影像、視窗工作區僅約 336 DIP。`SynchronousDiagramExportTests`、`--pacing-lazy-only`、`--workspace-only`、`--native-final-only`、`--diagram-compact-only`、`--outer-shell-only` 與大型 28 站 `--large-playback-only` 失敗；以改動前 commit `43938b8` 重建後，在同一環境以完全相同訊息與版面數值失敗，判定為環境限制而非回歸。完整 WPF runner、大型路線截圖與人工外觀檢查需在互動桌面 session 重跑。
- 未涵蓋：主視窗外框、結果圖表、拓樸編輯器軌道顏色（子專案 C／D／E）；原生桌面 DPI 與滑鼠操作未重跑。

## OSS 申請前整理（2026-10-08）

> 以下待提交敘述保留初次整理時的快照。後續使用者已授權本地提交、正常推送至 origin/main 並保留 Git 歷史，不含 tag／Release；GitHub 私人漏洞回報已啟用且 API 讀回 true，另補 SECURITY.md。公開結果以遠端 commit 核對為準。

- 本輪僅授權／文件／Git 公開範圍整理，不改來源版本 4.1.0、production、tests、Schema 或 UI 行為。實際標準 restore 與 Release build exit 0、0 warnings／0 errors；Engine 198/198、完整 default WPF PASS。
- 本機參考檔 17 份另行 ZIP 備份、逐檔 SHA256 一致；16 份原 tracked 檔已 git rm --cached，本機仍完整、git ls-files 0 份，.gitignore 及 export-ignore 已設定。此為待提交狀態，GitHub HEAD／歷史尚未移除；不 commit、push 或重寫歷史。
- 新增標準 MIT License（2026 weng-wen-jun）、README 英文起步／安全界線、貢獻流程，從本專案既有 Git 恢復 MODEL_SPEC，保留歷史版本警示。GitHub About／10 Topics 已設定並讀回。維護者後續確認大型機場線為抽象規劃、非實際線路里程，已同步公開文件，移除現行 sample 的實際里程來源權限阻擋項；未來取得政府公開可信資料後再記錄來源與日期更新。歷史文件標示差異保留，限定內容檢查不是完整安全／授權保證。此澄清只改 Markdown，未重跑 build／tests。詳見 [OSS 整理報告](docs/OSS_READINESS_20261008.md)。

## 指定資料夾整合（2026-10-08）

- 現行來源遷至 `D:/AI/codex/mrt-route-simulator`，版本 4.1.0；外部參考檔 16/16 與遷移前備份 SHA256 一致，全部納入本地提交。
- 新位置 Release 0 警告／0 錯誤、Engine 198/198、完整 default WPF PASS、MCP 預設選址黑箱 15 個工具及三輪並行測試 PASS，結果見 [遷移紀錄](docs/V410_CANONICAL_MIGRATION_20261008.md)。歷史原生驗收未完成界線保留，不以建置回歸取代。
- 整合已本地提交 `9e2b1ac`；舊樹 3,570/3,570 與備份一致後已移除，僅剩指定主工作樹。備份與 Git 分支歷史保留；不推送、不 tag、不發布。

## 單一 V4.1.0 工作版本（2026-10-08）

> 本節是遷移前歷史紀錄；現行位置及提交／移除狀態以以上遷移紀錄為準。

- 全部Git工作樹僅兩個；現行來源統一`mrt-v403-integration`，App與MCP共用`output/v4.1.0-unified`，版本實測4.1.0.0／V4.1.0。保留已累積的非MCP修改，不以舊4.0.2覆蓋新版；舊source差異最終核對與來源證據見[整合紀錄](docs/V410_UNIFIED_WORKTREE_AUDIT_20261008.md)。
- 本輪Release0警告／0錯誤、Engine198/198、完整default WPF exit0、MCP stdio15tools／三輪並行advance-reset exit0。全域及專案MRT設定指向統一DLL，其他全域設定指紋不變；現有連線需重新連線才載入新建置，未聲稱熱更新。
- 這是本地來源／建置整合，不是全部原生gate完成；連續拉邊框即時重繪、延遲／stall／memory限制保留。未commit、push、tag、發布或刪除工作樹。

## 部分完成項目續驗（2026-10-08）

- 依指示只續「已部分完成」項目，125%／150%最新完整排版矩陣略過，不展開cold latency／Reset／stall／memory根因。隔離4.1.0 `diagram-draw-phases-20261007`，原4.0.3不覆寫、不合併。
- sample14正常60×完成7172.9s；限定功能組合補驗PASS：雙收折、圖表兩軸獨立縮放、水平／垂直thumb拖曳且固定時間軸可見；最大化／還原刷新正常，但純寬／純高大幅resize與連續視窗拖曳未列PASS。
- 9個PDF組合逐頁render補驗：A4／A3單頁與分頁endtime off、A4／A3單頁及A3分頁110.2分短尾、A3 H1/V4 aspect-fit single短尾／endtime off。未見窄尾頁／文字截斷／標籤互蓋，限定配置PASS；單頁縮字纸本閱讀限制保留。高解析PNG下行H2/V2發現FULL-O13／FULL-O04標籤互蓋，**FAIL，尚未修正／回歸**。
- 原生session07aaffd6於播放中才arm，08:54:27+08正常Stop、activefalse／completedRuns0；raw非覆寫封存、report SHA256 `6ED0845D8ED17750B608A863A85CE9411B848EC1FA3EE294C72A0120E90B24E1`。起始viewport120DPI／interface1／1162.4×643.2DIP，非完整DPI矩陣；不作完整run parity／memory／compositor結論。逐項證據見[本輪紀錄](docs/NATIVE_PARTIAL_MATRIX_20261008.md)。
- 同資料first/warm局部profiler已實作且相同frame／cache／viewport／filters檢查通過，冷51.8112ms、3暖9.4007／12.6660／8.4238ms，只限test-only，不當原生50ms gate。隔離Release0警告／錯誤、Engine198/198、完整default WPF exit0／PASS。此前收折314→314失敗在舊binary也重現，compact三scale PASS；固定sticky測試邏輯高度1100後保留原assert通過，未改production排版。失敗log保留，原生匯出與profiler binary證據分開。
- PNG標籤互蓋已做最小匯出層修正，重用PDF單頁annotation overlay；1x／2x像素與source不變gate通過，隔離Release0警告／錯誤、Engine198/198／完整WPF PASS。僅WPF修正版回歸，sample14原生重匯出待驗，尚不撤銷原生FAIL。
- 額度99%接續點：PNG修正版原生正常60×完成01:59:32.9／37,172軌跡／9,147安全觀測；視窗保留在Diagram、H2V2／manual10／僅下行／events與endtime off／highres on，未開始新匯出。下一輪沿用現存隔離視窗重匯出並檢視，不重啟或覆寫舊FAIL圖；大幅resize／連續視窗拖曳仍未完整。詳見本輪紀錄末段。
- 額度重置接續已完成PNG原生重匯出：4948×1520新檔、SHA256 `24F62B4B44B593C925C141705057AA11C5B30E008E7E49CCEF9B15CB4F2D67DF`，全圖與原尺寸局部确认FULL-O13／FULL-O04分列、兩份planned/actual標籤與leader保留，限定此組合修復PASS。舊FAIL與較窄viewport證據保留，不宣稱全部PNG矩陣或互動文字排版完成。
- 後續原生純寬1382.4→996 DIP、純高820.8→569.6 DIP，raw確認各次另一尺寸／位置／120DPI不變，外層捲動可達固定時間軸及事件列表底端，限定完成後配置PASS。標題列真實滑鼠拖曳位移+120／+40 DIP、尺寸996×569.6不變，clock7172.9s凍結，限定移動功能PASS。四session正常Stop activefalse／completedRuns0、raw與空events非覆寫封存。連續拉邊框的即時內容重繪仍未完整：邊框嘗試未命中有效resize，正常系統大小模式僅outline，不作逐幀／compositor PASS；需實際可見拉邊框觀察，不擴大Windows全域設定或重跑已跳過DPI矩陣。詳見本輪紀錄末段。

## 待驗項目逐項處理（2026-10-07）

- 繪圖內部細分續驗：隔離`diagram-draw-phases-20261007` Release0警告／錯誤、Engine198/198／完整default WPF PASS。native36d6f3fe首次owner48.1223ms僅此單筆<50（非效能修正／整個cold gate PASS）；selectedChart42.2457ms含series25.2274、固定圖形13.1030ms，未證明JIT／逐點Add／GC根因。正常Stop completedRuns0／activefalse，raw非覆寫封存且雜湊一致；保留前輪51.3548ms FAIL。詳見[運行圖互動續驗](docs/NATIVE_DIAGRAM_INTERACTION_20261007.md)，下一步局部benchmark定位最大series段。

- 首次Diagram分段續驗：隔離`tab-input-phases-20261007` Release0警告／錯誤、Engine198/198、完整default WPF PASS（補dirty／clean summary與可見Diagram Reset測試情境，production刷新條件不變）。原生session2a7b2568首次owner51.3548ms仍FAIL50ms，handler46.9418ms中selectedChart44.8912ms，frameRead／accumulator／trainRows／eventRows各<1ms。只定位到繪圖段，未證明內部根因；正常Stop completedRuns0、activefalse，未作全程播放或memory結論。raw JSONL已非覆寫封存，source／archive SHA256均`DF744CDC6B4A9F57DE584B7185A8A5FF9EB51B8234FAD8113A5B806C47D8DB48`。詳見[運行圖互動續驗](docs/NATIVE_DIAGRAM_INTERACTION_20261007.md)；接續細分繪圖量測，原4.0.3未覆寫／合併。

- 後續去Reset提前empty draw最小WPF修正：reset-render-once隔離Release0警告／錯誤、Engine198/198／完整default WPF PASS；新增正常owner Reset的actual清空／planned保留、Safety已選取及Diagram隱藏後切回回歸。原生83c4baf6三輪事件全欄位0差異／正常Idle、Reset、Stop3；Play26.8280～33.3631ms，Reset36.8651／43.2529／首筆51.9069ms，仍未全達50。完整資料warm Diagram五次0.8387～1.0844ms限定PASS，first-tab55.5355ms仍待；單次11DIP keyboard resize只證明刷新／時間凍結，不列完整矩陣。raw stall2筆max427.9311ms根因未知，長期memory仍UNRESOLVED。詳見[運行圖互動續驗](docs/NATIVE_DIAGRAM_INTERACTION_20261007.md)及單項紀錄；未合併原4.0.3。

- 快速本程序診斷取樣隔離4.1.0 session c392e6d2五輪：正常完成，各1025events／37172trajectory／9147safety／active0，與原4.0.3全欄位ordered JSON逐筆0差異；五次合法10sIdle／Reset actual四項0、planned8／visual8/40/114，afterReset後Stop completedRuns5。Play總26.4282～38.8407ms；Reset37.1375～50.2658ms，第四筆略超50，嚴格互動gate仍未全過。完整Engine198/198、default WPF PASS；修正只降低同步診斷取樣成本，不改Engine步進／Schema／計時邊界。dispatcherStall累計7筆max518.7174ms，根因未知；private後段非單調仍不足判定bounded，memory仍UNRESOLVED。詳見[單項紀錄](docs/PLAY_RESET_LATENCY_20261007.md)，原4.0.3未覆寫／未合併。

- 新修正版原生session8692905d：Diagram60×／96DPI正常單輪7173.2s、1025events／37172trajectory／9147safety／active0，與原4.0.3 d70a3e60逐筆全欄位事件0差異。合法10sIdle後正常Reset actual四項0／planned8／visual8/40/114；Stop completedRuns1/activefalse，raw gap0限本輪。Play78.7588ms、Reset67.1094ms仍>50，互動gate未過；續查opt-in診斷取樣成本，不挪邊界洗PASS。兩檔已hash一致非覆寫封存，詳見單項紀錄；不是memory五輪或原Route gap已修復。

- 使用者允許調查及修正未完成項目，隨後指定逐項處理、節省額度。目前只處理 Play／Reset 互動延遲；Route gap、長期 memory 及匯出矩陣保留待辦，未開始新一輪驗收。最新125%／150%排版矩陣仍略過。
- 原五輪 raw 拆解：Play handler38.4399～46.9114ms、Reset handler26.0330～36.5567ms；總延遲另包含 mouse-down preview 至 handler 的約26～30ms，不把全部時間歸因於 worker／重繪，也不修改原計時邊界來宣稱PASS。隔離4.1.0調查進行中，原4.0.3不覆寫、不合併或發布。詳見[單項紀錄](docs/PLAY_RESET_LATENCY_20261007.md)。
- 本段最小修正：正常V2 Reset不再先Pause發布中間frame；Play不再強制發布重複world snapshot，下一週期仍正常發布，其他snapshot-changing command保留立即frame。不改Engine／Schema／collector邊界。隔離output/play-reset-latency-20261007 Release0警告／錯誤、Engine198/198、完整default WPF exit0／PASS，含新增Reset單frame與command邊界回歸。原生總延遲仍待重測，不列50ms gate通過；本段依逐項／節省額度停在修正與完整回歸，下一段先續本項。

## Collector修正隔離版五輪原生驗收（2026-10-07，新增授權）

- 使用者允許以已修正collector的隔離整合版續驗五輪。版本明確4.1.0，output/collector-ack-fix-20261007既有Release輸出，不覆寫原4.0.3、不合併Git；僅此五輪範圍擴大，125%／150%仍略過。
- 同一程序／sample14／96DPI／Diagram60×，五輪正常完成，各1025events／37172trajectory／9147safety／active0，與原版d70a3e60 run1全欄位orderedJSON皆0差異；5次合法10sIdle後正常Reset，actual四項皆0、planned8、visual8/40/114一致。第五afterReset後自動Stop completedRuns5/activefalse；runAborted0，collector ack阻擋本輪未重現。
- acknowledged-active-wall倍率59.99963165～60.00313113，publication邊界非精確tick。上述限定生命週期／事件一致／快取重設PASS，不能當成原4.0.3binary已修復。
- afterReset private MiB238.602→248.156→252.066→261.504→261.469，末兩輪持平不足宣稱bounded，memory仍UNRESOLVED。owner Play67.4921／66.5899／74.2828／68.8187／66.9733ms，Reset57.1289／56.9085／56.8061／64.2635／54.0882ms皆>50ms，互動延遲未達建議門檻；session一次dispatcher gap101.7264ms、未見重複，原因未知，不另要求零gap，也不沖銷原Route既有FAIL。
- 原始兩檔SHA256一致且非覆寫封存，原exe／DLLhash不變；詳見[五輪紀錄](docs/NATIVE_ISOLATED_ACKFIX_FIVE_RUN_20261007.md)。整體NOT COMPLETED，不把MCP功能或offscreen回歸冒充原生效能證據。

## V4.0.3 跨頁互動與完整事件續驗（2026-10-07）

- 原binary／sample14／96DPI，a4b4d3f3正常30→60×、Pause及Route／Speed／Diagram切頁，暫停後點FULL-O04／FULL-O13能選對曲線且時間不變；完成7172.9s、1025events、37172trajectory、9147safety、active0。
- 與d70a3e60 run1逐筆全欄位JSON對照0差異。完成10sIdle後正常Reset，actual cache/processed皆0、WPF polyline0/text1/canvas1；正常Stop completedRuns1/activefalse，原始檔SHA256一致封存、原exe/DLL不變。
- 限定功能／事件一致PASS；Play64.9641ms、首次trainClick91.2105ms、Diagram切頁57.1064ms、Reset62.793ms仍超50ms，互動效能未過。單輪memory仍UNRESOLVED、未改五輪阻擋結論，不混4.1.0原生效能、未重驗125%／150%。詳細原始證據見[續驗紀錄](docs/NATIVE_V403_20261007_CONTINUOUS_ACCEPTANCE.md)。

## 原生collector acknowledgement時序最小修正（2026-10-07）

- 已授權修正：worker identity／generation檢查只在正常Play acknowledgement後開始；未改Engine／Schema／MCP命令，不做Git合併或發布。
- 新增決定性回歸，ack前Probe不得abort，真實PlayAsync確認後建立新worker，仍必須偵測replacement。移除guard時專項準確FAIL，恢復後PASS。
- 全新output/collector-ack-fix-20261007隔離Release build 0 warnings／0 errors、Engine198/198、專項及完整WPF exit0／PASS。來源樹仍4.1.0，僅修正所需回歸，不作4.0.3原生五輪驗收證據。
- 原4.0.3 exe／DLL SHA256不變，未覆寫；完整範圍與red/green結果見[修正紀錄](docs/COLLECTOR_ACK_RACE_FIX_20261007.md)。

## V4.0.3 後續量測阻擋（2026-10-07）

- 原V4.0.3 binary／sample14／100% DPI／固定運行圖60×：session6ed4abc9第1輪完成7172.9s、1025events，完成後10sIdle及正常Reset已取樣；actual cache清空。單輪不構成五輪memory gate通過。
- 第2輪正常Play派送後20.1877ms、acknowledgement尚為0，collector即記錄`playback-worker-replaced`中止量測，UI仍推進。正常Pause及Stop後completedRuns1／activefalse；五輪未完成。疑似armed到ack期間的量測競爭條件，未修改程式或以V4.1替代。
- 原始metrics／events已封存，完整條件及SHA256見[本輪紀錄](docs/NATIVE_V403_20261007_CONTINUOUS_ACCEPTANCE.md)。125%／150%仍依使用者指定不重驗；此結果不得沖銷既有route gap／memory未解項。

## 驗收範圍調整（2026-10-07，最新）

- 使用者指定：最新排版125%／150%矩陣先不重驗；V4.1.0目前只驗MCP正常，不驗其他排版／效能／記憶體項目。原V4.0.3驗收保持獨立，日後合併另行指示；沒有當下合併或發布授權。
- V4.1.0既有MCP正常load／play／pause／切頁／zoom／reset及完整事件parity結果保留。新增PID31024亦正常load、Diagram、play60、完成後reset成功；第2輪依範圍調整正常pause於1197.9s，隔次status時間相同。這些是MCP功能證據，不列V4.1.0效能正式通過。
- 原生量測7ca80edb於09:25:00+08正常停止、completedRuns1／activefalse；五輪計畫取消，第2輪未完成。原始資料保存，不與V4.0.3五輪結果合併。

## 原生小視窗與專用MCP接續（2026-10-07）

- V4.0.3新排版原生視窗已由App量測確認96DPI／800×520 DIP／interfaceScale1。操作區及事件表四種收折組合、外層底端可達、圖內兩滑桿、固定時間軸、右端終點時間、重新展開設定保留均完成有限畫面檢查；非125／150%完整矩陣。
- 已實際使用連接的V4.1.0專用MCP，獨立PID35932正常load／play60／pause／切頁／兩軸zoom／reset成功；pause後狀態穩定。正常完整播放7172.9s、1025events，與既有V4.0.3 d70a3e60第1輪事件全欄位比較0不一致（只正規化JSON欄位大小寫及列舉表示）。未使用advance替代正常播放。
- MCP測試視窗僅啟動一次並已正常關閉；保留使用者縮好的原視窗。橋接status會更新view，RPC不代表原生輸入或compositor延遲；此輪雙視窗操作不當作無干擾效能／記憶體證據。
- 原生短session dc3fae1f正常停止completedRuns0／activefalse、metrics已非覆寫歸檔且SHA256一致。完整證據與未解項見[接續紀錄](docs/NATIVE_ACCEPTANCE_20261007_MCP.md)。

## V4.1.0 MCP 實作與連線驗證（2026-10-07）

- 在最新 `mrt-v403-integration` 整合樹實作，產品版本依 VERSIONING.md 升為 V4.1.0；尚未發布。採官方 ModelContextProtocol 2.2.0，新增獨立 Automation／Mcp 模組與 15 個 stdio 工具。Engine 仍不依賴 WPF，沿用 Schema 8、TopologyProjectFormat 與 SimulationWorld；未建立另一套物理或營運資料源。
- headless 專案驗證／載入／取代／儲存、固定 0.1 秒推進、快照、事件、時刻表及 CSV 可直接呼叫。桌面橋接由明確啟動參數開啟，使用同一使用者 named pipe，支援載入／儲存、播放／暫停／重設／推進、分頁／縮放及 CSV／PNG／PDF；沿用既有 Dispatcher 與單一播放 worker。
- 最終 V4.1.0 Release build：0 警告／0 錯誤；Engine 198/198、0 失敗；完整 default WPF runner exit 0，包含 MCP bridge、全部現行 sample、長行程與輸出回歸。記錄：`output/mcp-engine-final.log`、`output/mcp-wpf-final.log`。
- stdio 黑箱 initialize／tools/list／tools/call、15 個工具、失敗載入保留、越界路徑與覆寫拒絕、三輪並行 advance/reset 通過；橋接專項包含超大回應及破損請求後恢復、明確 tick／狀態確認及輸出驗證。
- 已註冊 MCP 的桌面黑箱建立獨立新版 WPF 視窗，完成載入、推進 10 秒、切頁、縮放、三種匯出、播放／暫停、事件／時刻表、重設，正常關閉該測試視窗。`output/mcp-desktop-smoke.json` 回報 passed=true、closedNormally=true。
- 本次 Codex 對話曾直接呼叫 MCP，確認 server_info 版本 4.1.0.0／Schema 8，實際載入、推進及快照到達 10 秒；無須在此對話使用 computer use 才能完成這些操作。最後修正版建置時已停止被鎖檔的本次 MCP 連線；新開對話或重新連線即載入最終版本。使用方法見 [MCP 說明](docs/MCP.md)。
- 最後審查補強：快照為可重複狀態讀取，事件以獨立分頁取得；播放確認完成狀態、無效推進競態恢復原本播放。新增初始事件保留／重複快照、播放中拒絕無效推進、600 秒內完成及正常重播回歸。重新建置首次因本次 MCP 連線鎖檔失敗，停止精確匹配的 MCP 子程序後重建 0 警告／0 錯誤；未停止其他桌面程序。完成後 Play 的測試先誤設為拒絕，已按既有正常按鈕自動重設再播放的契約修正，專項 PASS。
- 本次程式及協定驗證不代表原生 DPI、滑鼠操作延遲或 compositor 驗收；先前尚未完成的互動效能／記憶體驗收維持原有結論。未 commit、push 或發布 Release。

## 運行圖最小圖高／操作區收折修正（2026-10-06 晚間）

- 新增預設展開的「運行圖篩選、縮放與匯出（可收合）」；图框最小344 DIP、內層圖區最小300 DIP，事件表仍可獨立收折。僅Presentation，不改Engine／Schema／模擬／版本。
- 小視窗額外內容納入外層scroll extent，折疊／寬窄／切頁重新計算，不沿用stretch desired高度累加；其他分頁保留720 DIP政策。
- Release0警告／0錯誤、Engine198/198、完整default WPF exit0；新增compact專項scale1／0.8／1.25及控制／事件四種fold、底端可達、縮放保留、寬高／復原／切頁皆PASS。首次build兩個新增tests nullable警告已修正，未掩蓋。
- 正式exe已單次重開／sample14已正常讀取，新版原生800×520仍待手動縮窗確認，不用automated scale代理全DPI驗收。詳見[修正紀錄](docs/DIAGRAM_COMPACT_LAYOUT_PROGRESS.md)。

## 新版 100% 運行圖五輪／窄矮視窗（2026-10-06 晚間）

- 同Release process／sample14／Diagram／實測96DPI，d70a3e60五輪60×無Pause完整完成，各1025事件逐欄與基準相同、37172軌跡／9147安全觀測／active0；active倍率59.99693298～60.00276976，非精確final tick timestamp。五輪throughput／事件一致子項PASS。
- 五次合法10sIdle後正常Reset，非空actual cache清為0；第五次afterReset後才Stop completedRuns5。raw gap0只適用本session。afterReset private240.70→263.21→266.02→266.38→270.53 MiB，memory仍UNRESOLVED；direct play/reset小樣本p95/max80.1657／64.6809ms，互動效能未通過。
- 手動800×520 DIP／96DPI獨立短session71a5f935，固定時間軸／終點水平同步、事件收折增加圖高、Route下緣proxy拖曳等限定子項已觀察；但事件列表展開時图區過小、滑桿難操作，使用者要求加大最小圖高及上方控制可收折，修正進行中。尚非完整resize／DPI矩陣。
- 原始兩session皆正常停止、非覆寫SHA256一致歸檔，詳見[續驗紀錄](docs/NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md)。整体NOT COMPLETED，未改Engine／Schema。

## 新版 100% 營運事件目視續驗（2026-10-06 下午）

- 正式sticky Route新版、sample14、實測96DPI/interfaceScale1；6a5d與8baf兩獨立多倍率／多暫停完整輪次，各1025事件與既有基準全欄位相同，trajectory37172／safety9147／active0。
- O04／O13待避前、快車正線通過、普通車沿側線出口回主線及車尾淨空；O20入袋／袋內等待／同SECTION-VEHICLE-01轉上行／出袋及資源釋放，已完成限定案例分段event+visual子項。O20由兩輪互補，不假稱單輪連續錄影或全DPI矩陣。
- 互動效能未通過：6a5d raw dispatcherStall45筆最大1050.7862ms；小樣本direct play／pause／trainClick p95/max395.0506／76.1686／306.0606ms，未達50ms門檻。不混surrogate／OS／compositor，不下GC因果。新版不暫停60×sanity、完整DPI／resize矩陣、五輪private memory及gap根因仍待，整體NOT COMPLETED。
- 原始metrics／events正常Stop後非覆寫SHA256一致歸檔；詳見[續驗紀錄](docs/NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md)。本段僅驗收／文件，不另改Engine或Schema。
- 新版獨立47790043 Route 100%單輪60×無Pause完整完成7172.9s，active倍率60.00095151、1025事件逐欄Equal=true，限定throughput／事件一致子項PASS；raw gap15次max1751.6236ms，流暢度仍FAIL。合法10sIdle後正常Reset／Stop已保存，不將未開Diagram的零快取當非空快取清理證據；不是五輪bounded memory驗收。
- 額度恢復後同binary新process／sample14／實測96DPI，31283537短互動正常Stop completedRuns0：Route／Speed／Diagram各正常續播再暫停、等待>2s及切頁time不變，限定穩定性子項PASS。raw gap0不外推效能PASS，direct Diagram tab56.996ms、Pause63.5195ms仍超50ms；不是完整輪次。原始檔SHA256一致歸檔。

## 路線圖下緣頂層水平滑桿（2026-10-06）

- 使用者要求滑桿貼路線圖下緣、維持頂層，路線整張離開視窗才隱藏。僅改Presentation：Grid頂層Canvas proxy、route下緣預留及viewport交集定位；不改Engine／Schema／sample。
- Release0警告／錯誤；Engine198/198、完整default WPF exit0；主代理強化非零host offscreen斷言後再build／focused exit0。覆蓋App縮放1／0.8／1.25、compact outer-top/bottom、部分可見／完全離開／復原、內層捲動、雙向offset、切頁與overlay層級。首次測試helper编譯FAIL及1.25 sliver位置FAIL均保留，後者已修正。
- 正式新版sample14原生位置／拖曳／速度子頁隱藏／回Route恢復子項PASS，clock保持0；未重新完成OS DPI全矩陣、throughput或五輪記憶體驗收。詳見[本次修改紀錄](docs/ROUTE_STICKY_SCROLLBAR_PROGRESS.md)。整體原生長程驗收仍NOT COMPLETED。

## 125% 原生五輪與重設收尾續驗（2026-10-06）

- 原整合V4.0.3 Release、同process／sample14／Diagram頁、實测120DPI／interfaceScale1、正常60×五輪各7172.9s完整完成、無Pause；完整1025筆事件彼此及10月4日c3a6基準全部欄位相同。
- 每輪均在10sIdle後正常Reset，五次actual display cache歸零；第五輪idle後session仍保活，afterReset寫入後才sessionStopped，最後reset漏記修正原生子項PASS。不是無洩漏PASS，afterReset privateMiB236.46→272.05仍上升。
- 主代理核對active倍率59.99988754～60.00110921、raw dispatcherStall共0；只屬本條件dispatcher surrogate證據，不外推OS input／compositor或所有DPI流暢度。
- 新版獨立85312aec三輪完整播放事件1025筆皆與基準全欄位一致；第三輪完成後4.564秒正常reset，actual cache歸零，直到45秒後session Stop都未偽寫10sIdle，原生早reset子項PASS。前兩次因操作延遲超過10秒，保留但不列早reset；不混為五輪記憶體驗收。
- 追加A3對話框Title最小修正：同一paper enum用於Title及實際匯出，舊Title scaffold在A3斷言RED、新Release及無Show同步匯出GREEN；完整WPF最終再次exit0。test-only sample14弱引用檢查在停止collector而保留視窗／worker／planned graph時，舊completed frame／非空history與actual series/samples皆可回收，專用exit0；不是原生private memory無洩漏PASS。委派誤落舊樹已轉移並撤回僅代理新增檔案／入口，舊樹結果不採用。最終Engine198/198；正式新版原生A3/A4對話框Title皆與選取紙張相符，兩次皆取消，不當成重新匯出PDF內容驗收。
- 首次完整WPF在early Reset量測順序邊界FAIL；collector新增reset lifecycle開始旗標，fixture確定性觸發reset中timer，舊DLL RED／新Release0警告錯誤及專用回歸GREEN。修正後完整default WPF重跑exit0（含原生量測入口、全部sample／長行程及輸出），Engine198/198、0失敗及diff --check通過；原生早reset已通過上述限定子項，其他DPI／記憶體與gap根因仍待，整體NOT COMPLETED。原始metrics/events SHA256一致歸檔，詳見[本輪紀錄](docs/NATIVE_V403_20261006_CONTINUOUS_ACCEPTANCE.md)。

## 125%新版五輪續驗與原生匯出（2026-10-04）

- 同一Release process、sample14、Diagram頁、120DPI／1382.4×820.8DIP五輪60×完成，倍率59.99939～60.00074；五輪均1025events／37172trajectory／9147safety／active0，事件與既有完整基準逐筆一致。
- 流暢度未通過：第3／4／5輪分別2／6／1次>100ms dispatcher gap，最大1087.94ms；原因未證實，不冒稱GC因果或OS input／compositor量測。completion private241.79→265.39MiB；前四輪reset cache清零，第五輪未reset，不列strict bounded／無洩漏PASS。
- **原生匯出內容與新分頁問題**：舊PNG／PDF空白已修正；原生PNG終點on/off及12秒短尾標籤PASS。原生PDF逐頁確認有內容，但發現4頁窄尾與頁界裁切，FAIL；使用者允許最小匯出層修正，已red/green回歸、Release0警告／錯誤及output-only通過，正在以新正式視窗原生重驗，不列PDF整体PASS。
- 使用者新增「關閉警告」：最小Presentation實作已Release建置0警告／錯誤、專用不Show視窗回歸PASS（收合／清空／新警告重顯）；修正版原生按鈕亦已兩次觸發／關閉通過，專案路徑不變。Engine198/198、同步匯出及既有output-only通過；完整WPF未重跑，本輪不連續彈出測試視窗。
- 整體仍NOT COMPLETED；原始metrics／events已SHA256歸檔。詳見[本輪續驗紀錄](docs/NATIVE_V403_20261004_CONTINUOUS_ACCEPTANCE.md)。
- 空白修正版125%單輪60×、無pause，1025完整事件與五輪基準全相同；11次>100ms gap最大1116.9064ms，不列無卡頓／五輪bounded PASS。PDF分頁修正版另建session，條件不同不得合併。
- PDF第一修正版原生3頁／下方站名已恢復，但首刻度重複仍FAIL；第二修正以文字中心識別左軸，新增測試先RED後GREEN，Release及三項專用回歸通過，原生第二次重驗中。另9d6b Route單輪事件與50913053全相同，20次gap最大639.7635ms，不能列流暢度或五輪bounded PASS。
- PDF第四修正已原生逐頁確認3頁、下方站名、首刻度不重複、正常字形比例與防擠壓分列；終點on/off及12秒短尾三份PDF子項PASS。VisualBrush字形拉伸另以像素測試RED/GREEN證實；Release0警告／錯誤、PDF專用及同步／警告／output-only通過。原有車次起點名稱相互／圖例重疊仍待，不列整體PDF零重疊PASS；完整WPF仍未重跑。

- PDF第五修正：站名引導線漏線RED→GREEN，Release0警告／錯誤、Engine198/198及三項專用WPF通過。新版原生PDF3頁全部render／檢視，引導線映射子項PASS；既有車次名稱重疊仍存，整體PDF仍未通過。
- 第五修正版新同頁五輪：無pause，倍率59.999714～60.000184、每輪1025事件整份與基準一致；raw gap22／18／20／28／39，最大1820.6542ms，流暢度未通過。前四reset actual快取清零，第五auto-stop缺reset，不列strict無洩漏PASS。原始metrics/events已SHA256核對歸檔。
- 第六PDF文字修正僅匯出層：車次名依原折線首點歸屬單一頁、避讓圖例／其他車次文字並補引導線。舊DLL像素測試RED，新Release0警告／錯誤、PDF專用及Engine198/198／三項專用WPF通過；原生逐頁重驗中，非分頁模式尚不宣稱已防重疊。
- 第六版原生A4分頁終點on3頁全部檢視，文字配置子項PASS（車次文字／圖例不再互相重疊，引導線與時間軸正常）；個別標籤保留／dense fallback回歸已強化並GREEN。非分頁、終點off／短尾最新版本及完整WPF仍待，不列全矩陣完成。
- 第六版最新補驗：A4終點off與110.2分鐘短尾各3頁全render檢視，文字子項PASS；A3實際1191×842pt、3頁，紙張／時間／車次避讓子項正常，長標題頁右端仍疑似裁切待複核。未分頁A3修正前對照確認車次／圖例及終點caption重疊，正在做匯出層單頁窄修正，整體仍NOT COMPLETED；儲存對話框標題寫死A4另記不一致。
- 複核更正：第六版A3長標題／圖例末尾完整，render右侧至少37px留白，無裁切證據。第七版單頁車次overlay先RED後GREEN、Release0警告／錯誤、Engine198/198及三項專用WPF通過；原生A4單頁train避讓子項PASS，終點caption擠壓另以第八匯出層窄修正處理中，不列單頁全排版或完整WPF PASS。

- 第八版PDF追加驗證（2026-10-04）：單頁caption避讓窄修正Release0警告／錯誤，PDF專用GREEN、Engine198/198及同步匯出／警告／output-only各exit0。原生sample14單頁A4、A3及上下400%／勾選分頁但aspect適合單頁A3，各已render全頁檢視，車次避讓／終點時間與caption分列子項PASS；最新A3三頁亦逐頁檢視通過此案例文字子項。完整WPF未重跑、dispatcher gap根因及第五reset仍待，整體NOT COMPLETED。各PDF與SHA256見本輪續驗紀錄。

- 第五輪量測collector生命週期窄修正：idle後保活到正常afterReset再Stop，早Reset不偽造idle，最多5 measured runs；舊DLL合成fixture RED、新Release0警告／錯誤、native-acceptance-only最終兩次GREEN、Engine198/198、PDF專用及diff --check通過。首次focused在既有Resume紀錄時序案例失敗，原命令重跑通過，保留非決定性風險。未重跑新原生五輪／完整WPF，strict memory protocol仍待；正式App已正常關閉，收尾不啟動新長輪。output-only／native-acceptance-only含共用Show回歸前置，先前no-show措辭已在續驗紀錄更正。

## 運行圖終點時間開關（2026-10-03）

- 固定間隔之外可用「顯示終點時間」選擇補上圖表右端時間，預設開啟；已落在固定刻度時不重複，短尾間隔錯開文字避免重疊。篩選結束分鐘時是篩選終點，不能當作未完成模擬的實際完成時刻。
- 畫面與full PNG／PDF共用刻度／標籤邏輯；Engine／raw事件／CSV不變。Release0警告／錯誤、Engine198/198、完整WPF（開關、端點邊界、去重、非零clock、full一致、paused source、標籤邊界及既有输出）與diff --check通過。
- 原生續驗僅一個正式App視窗、sample14：終點on/off/on、短尾間隔12秒分行、整刻度去重、左右200%捲動對齊、上下200%固定軸與事件收折子項PASS；保留手動10分鐘與終點開啟畫面。未取得本輪DPI量測，不冒充全矩陣／效能／原生PNG-PDF輸出PASS。詳見[進度紀錄](docs/DIAGRAM_VIEWPORT_CONTROLS_PROGRESS.md)。

## 運行圖固定橫軸、事件收折與獨立縮放（2026-10-03）

- 時間軸固定於圖表底部，隨水平捲動對齊但不隨圖內垂直捲動消失；新增獨立上下縮放，左右／上下各預設100%、上限400%；下方事件列表可收折。
- 僅Presentation修改，Engine／Schema／原始事件／CSV不變；full PNG／PDF仍保留完整內嵌時間軸。Release0警告／錯誤、Engine198/198、完整WPF（包含新增固定軸、雙軸獨立、事件收折、paused source及既有輸出）通過；diff --check通過。
- 追加「時間刻度（分）」可選／自訂1～1440分鐘及套用按鈕；預設仍自動六等分，手動對齊顯示時鐘整倍數，畫面／grid／full匯出共用。追加版Release0警告／錯誤、完整WPF（合法／非法值、非零clock與full／interactive一致）再次通過。
- 追加版Engine再次198/198；原生sample14輸入10並按套用後，時間軸／grid呈每10分鐘刻度，功能子項通過。固定軸、事件收折、雙軸放大與水平對齊亦已原生觀察；本轮沒有新DPI session證據，不列指定DPI全矩陣或效能PASS。整體仍NOT COMPLETED，詳見[本輪進度](docs/DIAGRAM_VIEWPORT_CONTROLS_PROGRESS.md)。

## 事件觸發點開關與125%持續驗收（2026-10-03，最新）

- Diagram新增「顯示事件觸發點」，預設顯示；取消只隱藏圓點／圖例，事件表、軌跡與全量CSV不改。Release0 warnings/errors、Engine198/198、完整WPF、diff --check PASS；新版原生on/off/on及暫停clock不變PASS。
- 125%寬矮1319.2×520 DIP子項PASS；新程序90%啟動留白、首次Diagram56.95ms、Route/Diagram/Speed正常Pause/Resume通過；兩輪1025 events整筆一致、timestamp delta0。另1輪固定Diagram正常60×完成，不冒充新版5輪同頁memory repeat。
- 上行跨站孤立圓點已修正為既有共同車站顯示座標（不改 Engine／raw events／CSV），既有方向篩選明確標示為「上下行皆顯示／僅下行／僅上行」。最終 Release0 warnings/errors、Engine198/198、完整WPF（新增座標／方向回歸、CSV/PNG/PDF）與diff --check PASS。修正版96DPI／interfaceScale1原生上行60～65分鐘綠點對齊、下行／雙向篩選及事件off/on PASS；不冒充125%或全矩陣。重複Dispatcher gap及純resize owner時間仍待，整體仍NOT COMPLETED。詳見[事件點開關與診斷](docs/DIAGRAM_EVENT_MARKERS_PROGRESS.md)及[125%持續驗收](docs/NATIVE_V403_125_CONTINUOUS_ACCEPTANCE.md)。

## 125% 新版畫面續驗（2026-10-03，最新）

- 三份原生session均120DPI／interfaceScale1、sample14，全程1171.2s暫停。最大化／還原、精確800×520小窗外層與圖內捲動、28站分段標籤、Route常駐水平滑桿、FULL-O04暫停左右鍵／同車速度曲線通過。
- 窄高800×863.2 DIP、摘要展開文字換行及內層捲動、Speed底部、Diagram O26/O01兩端與時間軸可達。本輪窄高未重複列車hit-test，不擴大為完整全尺寸矩陣PASS。
- 三份正常停止flush、六份原始／歸檔SHA256一致；無新Play，completedRuns0。未改code，未commit／push。整體仍NOT COMPLETED；新版寬矮／其他尺寸交叉組合、其他DPI與完整input／resize gate待續。詳見[125%本輪畫面紀錄](docs/NATIVE_V403_125_DPI_FOLLOWUP.md)。

## 首次運行圖加速與續驗（2026-10-03，最新）

- 計畫軌跡 display cache 改為背景預熱、Dispatcher完成交換；source／generation／task取消隔離，完整計畫及原始匯出資料不變。未改Engine、Schema、版本。
- Release 0 warnings/errors、Engine198/198、修正後完整WPF runner PASS。首次完整WPF發現診斷等待入口跨UI thread，已修正並完整重跑通過；新增背景／取消競態／換檔／reset回歸。
- 原生整合版V4.0.3、sample14、96DPI／interfaceScale1、正常30×播放暫停1171.2s：首次開圖application57.96ms（原208.31ms），暖切0.88～0.93ms，計畫虛線及實際軌跡已顯示。planned178,992筆開圖前已背景處理。本輪單次冷切子項PASS，不冒充完整p95／所有DPI矩陣；條件與原766.3s不完全配對。
- session `f1895fbb4f0a4f02a9ca7bb7a9ff69d7` 正常停止flush，原檔保留、兩份SHA256歸檔相符。App暫停於1171.2s、運行圖頁。整體原生驗收仍 **NOT COMPLETED**；其他DPI／精確尺寸、完整互動p95／resize延遲待續，詳見[首次開圖修正與量測](docs/TIME_DISTANCE_COLD_OPEN_PROGRESS.md)。

## 無人值守原生續驗（2026-10-03，最新）

- 整合版V4.0.3、sample14，session實測96DPI／interfaceScale1。O04慢速越行、待避解除與合流，以及O20袋狀軌進入、等待、反向返回上行、停站後離站／資源釋放的原生目視子項PASS；事件紀錄獨立核對同VehicleId與車次切換。
- 806×520小窗的28站運行圖分段標籤、時間軸、水平滑桿實際拖動；Route固定滑桿上段／底部與暫停列車左右鍵命中／實際速度曲線通過。806×784窄高補驗Route與運行圖兩端可達，不冒充精確800×520矩陣。
- 首次運行圖application208.31ms，暖切0.92～1.01ms；冷切仍超過100ms目標，不能列PASS，完整互動p95與純resize延遲仍待。無production code變更；未新增完整60×輪次。
- 兩份量測正常停止flush、原檔保留及SHA256歸檔。App暫停5693.4s、窄高運行圖；overall **NOT COMPLETED**。其他DPI切換待使用者在場，詳見[無人值守續驗紀錄](docs/NATIVE_V403_UNATTENDED_PROGRESS.md)。

## 未讀檔預設頁排版修正（2026-10-03，最新）

- 左欄改依實際內容寬度自適應（38%、280～450 DIP），小窗約300；標題可換行，車站表站名最低80並可自行水平捲動。
- 空白 Route 不套用路線縮放，settled viewport 同步；未建立路線不顯示固定 Route 滑桿。已建立路線有水平溢位時仍常駐，原有 proxy regression PASS。
- Release 0 warnings/0 errors，Engine 198/198，完整 WPF PASS。原生未讀檔200%路線縮放、大窗→小窗、車站表水平拖到停站欄均通過；程式維持未讀檔頁首。
- 本輪純 WPF 修正，不改 Engine/Schema/版本。原載入 sample 的100%DPI續驗中斷，整體驗收仍 NOT COMPLETED；不以本輪取代所有DPI矩陣／事件目視／效能gate。詳見[預設頁修正紀錄](docs/STARTUP_LAYOUT_PROGRESS.md)。

## 125% 精確最小／窄高原生續驗（2026-10-02，最新）

- 兩份session起始viewport實測120 DPI、interfaceScale1，分別800×520與800×863.2 DIP；同sample14、245.5s全程暫停。
- 精確最小：摘要展開時整頁底部、運行圖O01／時間軸、速度圖底部可達，固定Route水平滑桿仍常駐。最小及窄高尺寸的FULL-O04左右鍵命中、同車跟隨與實際速度曲線正常，時鐘不變。窄高摘要收合／展開、換行文字內層捲動，以及運行圖O26/O01端點通過。
- 本輪無程式變更、無新Play；armed-only記錄已正常停止flush與hash歸檔，不算完整播放／事件或效能gate。App保留窄高視窗、運行圖、暫停，量測停止。下一段100%DPI需使用者手動切換；O04/O20目視、互動冷暖／純resize延遲仍待，overall NOT COMPLETED。詳見[續驗紀錄](docs/NATIVE_V403_125_DPI_PROGRESS.md)。

## 125% sample14 原生 UI 續驗（2026-10-02，最新）

- 實際 120 DPI／軟體比例1；一般90%視窗、最大化/還原、寬矮與800×539.2 DIP窄小視窗已檢查。暫停FULL-O04左右鍵命中、速度圖底部、28站運行圖分段標籤與固定Route水平捲軸通過；水平滑桿在外層頁首即可操作，外層底部仍常駐。未宣稱精確800×520或窄高矩陣完成。
- 首次運行圖切頁application194.71ms；play63.69ms、marker92.38ms，小樣本互動gate仍待重複。dispatcherStall count0不取代action延遲。本輪在245.5s暫停後manual-stop，不算完整播放輪次。
- 不改程式；raw已停止flush與SHA256歸檔核對。overall NOT COMPLETED；125%剩餘矩陣、100%DPI、O04/O20目視及互動重複仍待。詳見[本輪紀錄](docs/NATIVE_V403_125_DPI_PROGRESS.md)。App目前800×539.2、sample14運行圖、暫停且量測停止；下節啟動留白/建置結果繼續有效。

## 125% 最大化上下白邊／預設留白修正（2026-10-02，最新）

- WPF外層viewport更新時序修正，最大化內容填滿、不留上下居中白邊；一般啟動維持所在monitor工作區90%置中，修正125%跨monitor自動尺寸誤判。只改Presentation與回歸測試，Engine/Schema不變。
- 最終Release0 warnings/errors、Engine198/198、完整WPF、新large-window fill及800×520回歸均PASS。125%原生確認120 DPI、90%尺寸1382.4×820.8 DIP，四邊留白且不超出工作區；最大化/還原目視PASS。
- App保留一般90%視窗，目前預設示範資料；sample14完整125%矩陣、100%DPI與前節未完成項目仍待，overall NOT COMPLETED。詳見[修正紀錄](docs/SHELL_VIEWPORT_FILL_PROGRESS.md)。

## 150% 最小視窗／列車點選／O13慢速目視續驗（2026-10-02，最新補充）

- native144 DPI、800×520（系統尺寸键微調後807.333×520）、sample14確認；最小視窗外層/Route捲動、摘要展開、最大化/還原、停止及播放後暫停的列車左右鍵命中通過。左鍵切FULL-O13實際速度曲線，時鐘不變。
- 150%小視窗時間里程28站標籤已逐段由O26掃到O01、O08a/O15a可達且未見互蓋；其他DPI矩陣尚待。O13 native1×越行/普通車側線離站匯回主線目視PASS；O04窗口本輪錯過，O20未驗，不擴大PASS。
- 互動小樣本仍未達全面gate：首次運行圖切頁application191.9254ms，play70.8813ms、marker66.0837ms；需暖/冷重複及resize純處理延遲，不以dispatcher timer無>100ms記錄取代實際action結果。
- 本輪不修改程式、不重跑既有build/tests、不commit/push。App保留最大化、1×、1383.4s正常暫停；量測已停止且raw已hash核對歸檔。overall NOT COMPLETED。詳見[本輪紀錄](docs/NATIVE_V403_COMPACT_MATRIX_PROGRESS.md)。下節150%五輪與先前build/tests結果繼續有效，未完成清單以本節更新為準。

## 外層捲動／預設比例視窗與150%五輪驗收（2026-10-02，最新）

- 預設視窗改為所在monitor工作區90%，取消固定1440×900；標題可隨整頁上捲，Route水平捲軸固定視窗底部，不需將垂直捲軸捲到底。最終Release0 warnings/errors、Engine198/198、完整WPF PASS；保留啟動時序與新增測試首跑失敗／修復歷程。
- 真實150%原生預設1152×676.667 DIP，四邊在所在非主螢幕工作區內；outer offset0→48→96標題捲出，Route水平拖曳0→2199.154及垂直0→48可操作，Speed底部時間軸／站名可達。只代表本次尺寸，不是全resize矩陣。
- 同Release process／sample14／TimeDistance五輪完整60×播放均約60.0×，每輪1025事件序列全欄位hash一致，無>100ms dispatcher stall。memory分類LIKELY BOUNDED WITH FRAMEWORK RESERVE；第五輪Reset由同程序補錄session取得，明確記錄限制。
- overall仍NOT COMPLETED：100/125 DPI、全尺寸resize/hit-test、28站標籤掃描、O04/O13/O20慢速目視及完整input p95待驗。詳見[布局紀錄](docs/OUTER_SHELL_WINDOW_PROGRESS.md)與[五輪原生紀錄](docs/NATIVE_V403_FIVE_RUNS_PROGRESS.md)。下方舊階段狀態保留作歷史，不能覆蓋本節最新結果。

## 主視窗最低尺寸800 × 520（2026-10-02）

- 正式XAML最低尺寸由1180 × 720降到800 × 520 DIP；原開啟尺寸不變。兩個WPF regression直接驗證production minimum，不再覆寫假下限。
- 初次建置因App檔鎖失敗；使用者授權Alt+F4正常關閉後重建成功。小視窗展開摘要造成Route零高度的回歸FAIL已修正：進入短視窗自動收合摘要，手動展開可捲動、不刪資料。
- Release0warnings/errors、Engine198/198 PASS、完整WPF PASS；再加強摘要自動收合斷言後重建／native-final專項PASS。新版執行檔已更新，native DPI／resize仍未完成，不以自動化代替。見[執行紀錄](docs/MINIMUM_WINDOW_PROGRESS.md)。

## 軟體介面縮放與Speed小視窗捲動（2026-10-02）

- 上方「顯示設定」新增80/90/100/110/125%介面縮放，跟隨本機軟體偏好而非路線存檔；程式工作區視窗沿用。Speed最小圖高不再裁掉不可達底部，改為可捲動。
- 最終Release build 0warnings/errors、完整Engine198/198 PASS、最新binary完整WPF PASS（含設定保存／telemetry vectors）、diff check PASS，詳見[執行紀錄](docs/INTERFACE_SCALE_PROGRESS.md)。原生驗收仍暫停，不能以離屏捲動／hit-test proxy清除native FAIL或宣稱DPI矩陣PASS。

## 4.0.3 基底整合中（2026-10-01）

- 本檔以下原生／播放紀錄來自原4.0.2工作樹，保留作歷史證據，**不能代表4.0.3整合版驗收**。目前正依使用者指示先整合、驗證，再恢復原生驗收。
- 隔離整合位置 `D:/AI/codex/mrt-v403-integration`，基底 `68084cb`，包含4.0.3正式提交與其後Route跟隨／水平縮放；不是只修改版本號。
- 最終完整 Release build **PASS，0 warnings/errors**；完整 Engine **198/198 PASS**；完整 WPF runner **PASS，exit0**。初輪舊車次ID、Route超大finite sentinel、既有固定80ms測試等待失敗與修正原因保留在[整合紀錄](docs/V403_INTEGRATION_PROGRESS.md)，不隱藏重跑歷程。本地來源整合gate已完成。
- 原工作樹及83個檔案備份保全；未commit／push／tag／release。原生整體仍NOT COMPLETED，舊Speed小視窗裁切FAIL不得清除。

## 小視窗摘要／Route 捲軸修正（2026-10-01）

- 上方五張摘要卡改為預設展開、可手動收合；移除摘要／Simulation 區域過大的最低列高，Route ScrollViewer 依可見空間配置。只改 App XAML 與 WPF regression，不改 Engine、sample、schema 或播放資料。
- Release build 0 警告／0 錯誤；Engine 186/186、0 失敗。完整 WPF 首跑在既有 NativeAcceptanceInput 固定 80ms 等待後的 playing-state assertion 失敗；專項及完整 runner 重跑 PASS，首次失敗保留，不宣稱零偶發失敗。Compact regression 檢查摘要保值、收合增加 viewport、兩軸溢位時水平捲軸可見。
- 原生窄／短視窗：摘要展開／收合、Route 垂直 offset 0／48、水平拖曳 offset 0→1265.810238 可操作，水平捲軸一直位於可見圖面底部；clock 保持 0.0s。PNG 與詳情見 [執行紀錄](docs/NATIVE_PLAYBACK_FINAL_PROGRESS.md)。這項原生檢查通過，不代表全套 DPI／resize 或整體 final acceptance 通過；先前 Speed clipping、五輪 memory 等未完成門檻不因此清除。

## Final acceptance 修正階段（2026-10-01；使用者要求先結束，原生操作延後）

- 本輪授權僅 presentation station-label layout、App-only opt-in input/memory diagnostics、WPF regression。保留下節歷史三輪結果，不以自動化 layout proxy 升級原生 PASS。
- 修正原理：原先標籤固定放在真實 station Y 附近，未依 TextBlock 實測邊界避碰。新增通用字型／雙向 packing／leader line；保留 station grid、trajectory X/Y 與 topology truth，無站名特例。增量畫面及完整圖／匯出路徑共用標籤 helper。
- 輸入紀錄只在明確開始 session 後掛載；normal Play/Resume/Pause/Reset/tab/marker owner hooks、WPF routed receipt、HwndSource move/resize 及 scroll proxies。時間界線不是 OS injection 或 compositor present，無 ETW 時不把 GC 宣稱為原因。
- Session 上限改為五次完成 run，第五輪保留至 10 秒 idle checkpoint；加入 beforePlay/afterReset/nextPlay、Private Bytes、managed heap、配置／GC、history、display cache 與 WPF visual counts。無 GC.Collect、無自動播放／重設。
- 本輪新原生完整 run 數為 0；native input p50/p95/max、>100ms stall 數、同頁五輪 memory 趨勢、O04/O13/O20 screenshots、100/125/150% DPI/resize/hit-test 及修正後 60× sanity 均待使用者回來。
- TIME-DISTANCE LABELS 原生決策：NOT COMPLETED（歷史 FAIL 待新 native 複驗）；NATIVE INPUT：NOT COMPLETED；MEMORY：UNRESOLVED；O04/O13/O20：NOT COMPLETED；DPI/RESIZE：NOT COMPLETED；NATIVE PLAYBACK OVERALL：NOT COMPLETED。Nearest-leader NEEDS MORE DATA／production default oracle 不變。
- 最終 Release solution build：0警告／0錯誤；Engine：186/186、0失敗；label/input/memory 專項 PASS；重建後完整 WPF：PASS WPF visual rules、exit0（含 scroll token、樣本載入／圖表、worker／重設、CSV／PNG／PDF）。Engine source／sample SHA256 與修正前一致；結果與 synthetic raw JSONL 見 [執行紀錄](docs/NATIVE_PLAYBACK_FINAL_PROGRESS.md)。未 commit/push/merge/tag/release；保留既有 dirty worktree。

## 已授權原生量測入口與三輪補測（2026-10-01）

- App-only、預設關閉的「原生驗收量測」入口已加入；正常播放才採集，每 10 個 active wall seconds 記錄，三輪完成後自動停止並 flush。背景 bounded queue 寫檔；未改 Engine／physics／sample／oracle default／站名版面。新增預設關閉、pause/resume、generation、I/O failure、完成報告及正常關閉回歸。
- 真正 Windows Release 視窗／大型機場線再次完成三輪連續 60×，分別可見 Speed、TimeDistance（起始 Speed）、Route。以 Play dispatch 到完成 frame publication 的保守界線量得 **59.99899／59.99814／59.99708×**，三輪 full-run >=58× 子門檻 PASS；此界線不是精確最後 Engine tick 時刻，也不是同頁重複 benchmark。
- 三輪均 41,693 軌跡／11,170 安全觀測／620 事件／零營運列車。事件身分序列一致，對應事件時間 max delta=0 s；不代表所有 event field byte parity 或原生實體動態目視通過。
- early/mid/late 與 raw JSONL 詳見 `docs/NATIVE_DESKTOP_ACCEPTANCE.md`。Apply p95 6.05／3.26／2.80 ms；Input-priority timer gap max56.65／344.43／54.67 ms，非實際 click latency；未記錄 >100 ms 次數，不能宣稱 repeated-stall absent。p95 為 latest4096 滾動統計。
- 三輪 final WS312.58／332.52／342.41 MiB；max observed WS342.41 MiB，sampled heap max91.45 MiB。累積配置11797／7119／12294 MiB、GC totals1961/355/6；配置不等於 heap、Ui/Worker runtime 不可相加。無 late-run throughput collapse，但跨輪記憶體有界尚未證明。
- App 回報 WPF96 DPI／scale1，所屬非主 monitor bounds 為1536x960 reported pixels；OS Settings 縮放與100/125/150%矩陣未獨立確認。運行圖站名重疊再次重現。**NATIVE visual FAIL／overall NOT COMPLETED；LONG-RUN overall NOT COMPLETED；DPI/RESIZE NOT COMPLETED**。O04/O13/O20連續動態、實際互動、長期 memory gates 仍待補。
- 本次 code changes 非0（僅量測入口／hooks／tests）；前節 code=0 僅描述初始驗收。完整 scoped manifest 與 raw hashes 在驗收報告；既有 dirty files／TODO 保留，未發布。fresh Release0警告0錯誤、Engine186/186、入口專項PASS、完整WPF PASS WPF visual rules；diff check 最後核對於文件更新後。

## 原生桌面長時間驗收（2026-10-01；部分完成，未核發全面 PASS）

- 詳見 `docs/NATIVE_DESKTOP_ACCEPTANCE.md`。同一 Release／大型 28 站 sample 的真正 Windows 視窗完成三輪 60× 播放；完成時鐘 7,368.6／7,368.4／7,368.7 s，三輪皆 41,693 筆軌跡、11,170 筆安全觀測。
- 第三輪不中斷播放的中／後／接近完成牆鐘區間約 59.49／59.77／59.86×；完整停止牆鐘只取得上界，前兩輪亦缺完整實效倍率，不能宣稱三輪 full-run >=58× 已驗收。
- Route／Speed／Safety／TimeDistance／Timetable／Segment／Resource／Comparison 切換可顯示資料，Speed 暫停／恢復、最大化／還原功能通過；未量得原生輸入延遲分布、render p95、GC、allocation、hidden render 或完整事件 parity。
- **原生視覺問題**：運行圖相鄰站名垂直重疊，在多個時段及第二／第三輪重現。未修改 production code；修正仍需另行同意。
- sampled working-set peak 約 376.9 MiB；第三輪後段／完成約 376 MiB，但跨重設仍從約 308 -> 366 -> 377 MiB 上升。未證明 leak，也未證明有界；private bytes 不等於 managed heap。
- NATIVE PLAYBACK：視覺 gate **FAIL**、整體 **NOT COMPLETED**；LONG-RUN 60×：三輪 completion 成功，但全面驗收 **NOT COMPLETED**；DPI/RESIZE：**NOT COMPLETED**（Settings 啟動等候 App 核准逾時，未改縮放；manual resize 與完整 DPI 矩陣未完成）。O04／O13／O20 physical event motion gate 仍待補。
- 此輪 code changes = 0，只更新驗收文件；保留原有工作樹與使用者另行同意保留的 TODO 修改。索引 **NEEDS MORE DATA**／production default **oracle** 不變。最後 Release build：0 warnings／0 errors；Engine：186/186 PASS；完整 WPF：PASS WPF visual rules；diff check 通過。Offscreen 僅為 regression。

## Playback pacing／nested lazy／incremental 運行圖（2026-09-30～2026-10-01）

- 使用既有未提交工作樹 `codex/nearest-leader-index-prototype`，HEAD/base
  `558e06b534f97c997f450379f747e7709528ac17`；保留前輪變更，未 commit／push／tag／merge／Release。
  本輪只調整 App coordinator、presentation 與測試；正式 leader lookup 仍為 oracle，索引仍 **NEEDS MORE DATA**。
- Worker 以完整 0.1 s tick deadline＋可靠 semaphore command wakeup 等待，保留全部 physics ticks、命令順序與
  latest-frame-wins。八分頁各約 10 s 的 no-progress AdvanceTo **47～61 million → 0（觀測減少 100%）**。
  TimeDistance no-progress inner elapsed **1,143.32 → 0 ms**；這不是 ETW thread CPU 百分比。
- 同一大型 28 站樣本、Release／60×／同一 profiler 方法：八分頁有效倍率 **59.59～59.68×**。
  TimeDistance render p50/p95/max **50.765/58.923/79.999 → 0.644/1.1245/12.877 ms**；p95 減少 **98.09%**。
  unique UI apply FPS **17.79 → 21.23**；圖形維持約 4 Hz，採替代 responsiveness gate：最大輸入間隔
  **108.44 → 41.68 ms（<50 ms）**；skipped presentation frames **126 → 85**。
- TimeDistance allocation **127.49 → 54.70 MiB/s**；GC **60/23/13 → 35/7/0**；working set **308.66 → 261.20 MiB**。
  新增 process CPU 總時間為 2,890.625 ms／10.175 s，無同方法 before CPU，不能宣稱 CPU% 改善。
  productive advance inner elapsed **1,096.92 → 1,403.73 ms**，不以沒有證據的 scheduler／GC 原因解釋。
- Route visible 的 Route/Speed render count **214/36 → 214/0**；Speed visible **214/36 → 0/37**。
  暫停時切 nested tab 立即更新；子控制項冒泡事件不觸發全刷新。低成本 TrainRows 保持原更新方式。
- 運行圖只處理 immutable history 新尾段；planned artifact 一次處理，重用 static grid、polylines／labels／markers。
  每組 ordinary points 上限 600，分層 stride 保留全時段 coverage；critical transitions／extrema／first-last 不丟棄，
  因此**不是嚴格 600 total points 上限**（本次 planned 最多 1,673 點／1,218 critical）。大量 critical points 仍可能增長。
  actual-only 自動時間軸採 600 s forward bucket；PNG／PDF 保留原完整 history renderer，CSV／Engine truth 不變。
- 新增 rate 1/10/30/60×、pause/resume/rate-change/reset/stop、完整同目標輸出 JSON parity、nested hidden-count、
  cache batch/incremental／critical／長來源 coverage、resize/filter/reset/new-generation invalidation 與 full-reference
  端點／station grid／event marker 測試。聚焦測試 PASS；Pause/Reset/Stop 觀測 <1 ms，不是最壞 latency 保證。
- Slow-UI **680 → 740.5 s／31 skipped frames**，與同目標 deterministic reference 的 events／trajectory／safety／
  final trains／非空 resource occupancy JSON 相等；continuous-consume 另觀測 740.9 s。UI 丟幀不丟 physics tick。
- Release solution build（2026-10-01 續跑確認）：**0 warnings／0 errors**。完整 Engine runner（2026-09-30）：
  **186/186 passed，0 failed**。完整離屏 WPF runner（2026-10-01 重新執行，exit 0）：**PASS**，
  包含新增回歸、所有 sample、讀檔失敗交易、worker cleanup、長行程與 CSV／PNG／PDF。
  `git diff --check`：PASS。上次中斷的 WPF session 無法取回，未用部分輸出宣稱完成。
- 原生 Windows desktop／resize/drag/DPI／60× 畫面驗收：**NOT COMPLETED**。
  Speed 是目前 selected-render p95 最慢項（11.224 ms）；Route 首個測段仍有 64.16 ms input-gap tail，
  不能宣稱全分頁 input gap <50 ms。後續建議先做 native／late-run／repeatability，再評估 Speed 或 UI consume cadence。
- 完整前後表、限制、變更清單與重現命令見 [PLAYBACK_END_TO_END_PROFILE.md](docs/PLAYBACK_END_TO_END_PROFILE.md)；
  原始量測見 [PLAYBACK_PACING_PROFILE_RAW.json](docs/PLAYBACK_PACING_PROFILE_RAW.json)；
  分階段紀錄見 [PLAYBACK_PACING_PROGRESS.md](docs/PLAYBACK_PACING_PROGRESS.md)。

## Indexed-only 與 end-to-end profiler（2026-09-30，本輪）

- 沿用使用者允許的未提交 prototype 工作樹；HEAD/base `558e06b534f97c997f450379f747e7709528ac17`。
  未 commit、push、merge、tag 或發布。正式 App 仍預設 oracle，profiler 預設關閉。
- 三模式各一次丟棄暖身＋五次 fresh-process Release／8,000 s／80,000 ticks，全部開相同 diagnostics。
  Oracle-only median **9.417129 s**（9.347780～9.507597）；indexed-only **9.077802 s**（8.872212～9.495322）；
  shadow **23.266440 s**（22.771982～23.392308）。本輪數字取代先前推估，但保留下節歷史紀錄。
- Indexed-only 整體改善 **3.60%**，lookup 改善 **27.79%**；graph calls **1,783,438 → 984,253**。
  初始化＋run allocation median **4,752,636,824 → 4,614,363,608 bytes**（-2.91%）。
  冷 index-mode 初始化 **10.5989 ms／505,128 bytes**；first graph call **10.0354 ms**，不等於完整快取初始化。
- 每輪 602,454 lookup；602,381 indexed success，73 fallback（5 missing follower＋68 stale generation），比例 0.0121%。
  候選平均 2.340 → 1.008，p95/max 3/5；18 輪事件 hash 均一致，shadow 0 mismatch。
- 完整／大型 sample indexed-only full-retention events、trajectory、safety、final-state 與 oracle 比對已通過。
  O04／O13／O20 gate、密集混合車長與 Reset 同步驗證；密集 fixture 仍僅證明有界 parity，不宣稱完成。
- 決策：**NEAREST LEADER INDEX: NEEDS MORE DATA**，實測介於 3%～5%，未達 GO 門檻且範圍重疊。
  不切 production。完整數據、counter 邊界及 raw JSON 見 [prototype report](docs/NEAREST_LEADER_INDEX_PROTOTYPE.md)。
- 本輪 Release solution build 已通過，0 warnings／0 errors；完整 Engine **186/186、0 failed**；完整離屏 WPF **PASS**。
  含 profiler default-off/reset/percentile、workspace 子事件邊界，以及既有視覺、載入、播放、長行程與輸出 gate。
- 60× 八個分頁均完成約 10 秒／600 秒以上量測，effective **59.57～59.69×**；publish 約 30.2～30.3 FPS。
  運行圖最慢，UI apply 約 **17.8 FPS**、126 skipped frames、max input gap **108.4 ms**；運行圖準備約占該頁
  synchronous UI apply **98.95%**。其他頁 UI 約 21.2～21.3 FPS。原生 composition／GPU 時間未量測。
- 主要瓶頸判斷：**MIXED（CHARTS／WPF refresh preparation ＋ FRAME PIPELINE coordinator polling）**。
  每 10 秒約 4,700～6,100 萬 no-progress AdvanceTo calls，diagnostics 現在每 frame 彙總，沒有修改協調排程。
  此窗口沒有觀察到 Engine 無法維持 60×；不能把 UI 略過幀當成物理 tick 遺失。
- 慢 UI／frame-drop parity 在 **740.1 s、32 skipped frames** 通過：events、retained trajectory／safety、current safety、
  trains 與非空 resource occupancy 與同 target 正常 replay 完全一致；正常持續取用另外觀察到 743.4 s。
  使用者另外授權的最小 Safety UI 修正：workspace SelectionChanged 忽略子控制項冒泡事件，回歸已通過。
  60× 分頁 profile 與歷史差異追查見 [end-to-end report](docs/PLAYBACK_END_TO_END_PROFILE.md)。
  原生桌面不同 DPI、拖曳／resize、8,000 秒桌面操作及 O04/O13/O20 人工畫面驗收尚未完成。

## Nearest-leader shadow prototype（2026-09-30）

- 分支：`codex/nearest-leader-index-prototype`；base／目前 HEAD：`558e06b534f97c997f450379f747e7709528ac17`。
  本輪變更尚未 commit／push。完整過程與五次數據見 [prototype report](docs/NEAREST_LEADER_INDEX_PROTOTYPE.md)。
- Production leader lookup 仍回傳既有 full-scan oracle；候選索引僅用於 shadow 比對。
  固定 0.1 s、physical cursor／footprint、graph-distance narrow phase、rear-clear 與資源仲裁均保留。
- Release build：0 warnings／0 errors；完整 Engine runner：**184/184 passed，0 failed**。
  新增 11 項 diagnostics／candidate／shadow tests；涵蓋真正跨 edge 車尾、branch／merge／passing、對向、
  重複 traversal、tie-break、保守 fallback、三次 deterministic replay、Reset、執行中切換與密集混合車長。
- 完整離屏 WPF runner：PASS（視覺規則、載入／失敗交易、播放 worker、長行程、CSV／PNG／PDF）。
  原生桌面 DPI、人工操作及 8,000 秒連續桌面播放尚未驗收。
- 大型 28 站 sample，8,000 s／80,000 ticks；各模式一次預熱後五次量測，同 Release／sample／0.2 s retention。
  baseline 中位數 **8.58 s／0.10725 ms per tick**（8.51～8.82 s）；shadow 中位數
  **21.56 s／0.26950 ms per tick**（20.94～21.77 s）。shadow 同時計算 oracle 與 safety/control 比對。
- 每次大型 shadow：602,454 lookup、602,381 indexed success、73 fallback（0.0121%）、**0 mismatch**；
  平均候選 2.340 → 1.008（減少約 56.9%），p95／max 為 3／5。O04／O13 越行與 O20 pocket 換端已實際觀測，
  全車完成，無 Collision／StationStopViolation。
- 所有五次 baseline／shadow 的 620 筆事件雜湊相同：
  `7746B7DDB5FBA1449971A956BD56D8DFD5178205556DC94BDBD7E5EE6B8F1D2D`。
  完整 topology sample 三次 3,000 s shadow 與 oracle-only 的事件、安全、軌跡與最終狀態雜湊亦一致。
- Full-scan lookup 中位數 1,667.529 ms；indexed query（含現有 narrow phase 與排序、排除額外 safety/control 比對）
  中位數 873.334 ms；世代重建 16.261 ms。相減估算整體 7.802 s（約省 9.1%），尚非獨立 indexed world 實測；
  indexed-only 冷快取成本、graph-call／allocation totals 未量測。baseline／shadow allocation 中位數約 4.783／7.361 GB。
- 密集三站壓力 fixture 的 oracle-only 與 shadow 在 1,200 s 輸出一致且無碰撞，八車均已發車；但兩者仍有三車停留於 S02 前，
  此 fixture 僅證明有界 parity，不能宣稱密集運行完成。
- `git diff --check`：PASS。結論：**NEEDS MORE DATA／本輪不建議 productionize**。


> **4.0.2 整合狀態：自動化驗證完成**：三個來源工作樹的程式與測試內容已合併至 `codex/integrate-v4.0.2-worktrees`，整合分支 Release build、Engine runner、完整 WPF runner 與大型 full sample 定向 WPF playback 診斷均已通過。原生桌面不同 DPI、長時間桌面播放與人工畫面驗收尚未完成。下方標示「來源工作樹」的數字保留歷史證據，不能冒充整合分支結果。

## 4.0.2 整合分支目前驗證（2026-09-25）

- Release build：0 warnings／0 errors。
- Engine runner：**173/173 passed，0 failed**。
- 大型 full sample 定向 WPF playback：PASS；60× 播放約 3.06 秒推進 180.4 模擬秒，觀測約 59.0×，輸入最大間隔 41.7 ms；同時確認 O04／O13 顯示接軌、站中心／月台中心投影及播放期間停站違規規則。
- Engine-only benchmark：大型 full sample 推進 180 模擬秒耗時 0.97 秒，有效速度約 185.46×；此數字只代表 Engine 計算，不代表 UI 播放倍率或桌面操作流暢度。
- 完整 WPF runner：PASS；涵蓋範例載入、playback worker、速度行程與 CSV／PNG／PDF 輸出 gate。
- 原生桌面不同 DPI、8,000 秒連續播放，以及 O04／O13 越行與 O20 pocket 換端關鍵畫面仍待人工驗收；離屏 WPF 診斷不等同桌面驗收。

## V4.0.2 大型機場線停車中心修正（2026-09-22）

- 大型機場線 full sample 的 56 個月台明確使用 `StopPositionReference.TrainCenter`，停點與月臺幾何中心一致；O01／O26 終端目的月臺保留完整 100m 車體所需的邊界前置空間。
- `SimulationWorld` 以有序 ServiceRoute navigator 解析中心停點；列車頭可跨越站界到相鄰 edge，車體中心仍落在原月臺中心，不寫入超出 edge 的 offset。
- 來源工作樹紀錄：Release build 0 warnings／0 errors；完整 Engine runner **170/170** 通過，包含上下行全程車實際停車中心回歸；Schema 8 sample round-trip 通過。此數字不代表整合分支結果。
- WPF runner 已執行，但在 full sample editor-720 的既有 `EDGE:PASS-002` 46.3° 示意突折停止；原生桌面／不同 DPI／8,000 秒連續播放仍未完成人工驗收。

## V4.0.2 大型機場線 full sample（2026-09-20）

- 新增 `samples/大型機場線-完整營運示範範例.mrtsim.json`，並保留既有 minimal sample。主要原始碼是測試專案內的 staged builder，JSON 為可載入輸出；minimal → 28 站完整鏈 → services → O20 pocket → O04 passing → O13 passing → timetable 共 7 個階段均先做 Structural／Operational gate。
- 去識別化參考 `外部檔案參考/大型機場線_模擬參考.md` 固定本 synthetic scenario 的服務邊界：AIRPORT-DIRECT 為 O01↔O20，停 O01／O08／O11／O16／O20，不前往 O26；FULL-LINE 為 O01↔O26 全停，SECTION 為 O01↔O20。29.9 km 與 O01～O20 23.8 km 僅是 synthetic aggregate targets；逐站距離分配、platform／train 尺寸、性能、dwell、facility 幾何／速限、port side 與 dispatch offset 均不是任何正式路線資料。
- O20 以 topology-native 站後袋式儲車軌完成 SECTION 與 AIRPORT-DIRECT 同 `VehicleId`、不同 `ServiceRunId` 的實體換端；O04／O13 各有上下行 PassingFacility，共 4 條通過 edge。O04 同時驗證 SECTION 越行，AIRPORT-DIRECT 依序於 O04／O13 兩次越行；普通車均等 express 車尾淨空及 resource 釋放後才離站。
- 代表班表把尖峰 FULL-LINE＋SECTION 與離峰 FULL-LINE＋AIRPORT-DIRECT 合併到同一 regression runtime，只為覆蓋多服務、越行與折返，不是正式同時營運班表。deterministic 事件：DIRECT 於 624.5／676.3 秒、1,252.0／1,303.8 秒提出／完成 O04、O13 越行，1,858.1 秒抵達 O20 pocket，2,600.0 秒換為上行；SECTION 於 3,124.5／3,176.3 秒提出／完成 O04 越行，4,704.5 秒抵達 O20 pocket，5,600.0 秒換為上行，7,446.5 秒退出。
- 來源工作樹驗證紀錄（非整合結果）：Release build 0 warnings／0 errors；Engine runner **161/161**、0 失敗（含新增 focused 15/15 與 14-sample 有界 gate）。WPF runner 的既有範例檢核通過，但 full sample editor-720 在 `EDGE:PASS-002` 回報 55.3° 示意突折，因此不能宣稱 14 份 sample 的 WPF runner 全數通過；此顯示層問題不改變 physical port metadata 或 Engine topology 驗證。
- 尚未完成的人工驗收：原生 WPF 不同 DPI、8,000 秒連續播放、O04／O13 越行與 O20 pocket 換端的關鍵畫面目視。本節的自動 WPF runner 不代表上述桌面人工驗收已完成；sample 也不能用於工程設計、號誌設計、安全認證或正式時刻表。

> 以下 Phase 1 數據是 `codex/playback-performance-phase1` 來源工作樹的獨立驗證紀錄；合併後尚未重跑，不代表本次 4.0.2 整合分支已通過相同閘門。

## 長時間播放效能 Phase 1：修改前基準（2026-09-20）

以使用者指定的真正 full sample `D:\AI\codex\mrt-route-simulator\samples\臺中機場捷運-完整營運示範範例.mrtsim.json` 執行獨立 benchmark runner；該檔案目前位於原工作樹，不在此 Phase branch 內：

```powershell
dotnet run --project .\tests\MrtRouteSimulator.Performance\MrtRouteSimulator.Performance.csproj -c Release --no-restore -- "D:\AI\codex\mrt-route-simulator\samples\臺中機場捷運-完整營運示範範例.mrtsim.json" 8000
```

基準 runner 先建立與 WPF 相同的 topology `SimulationWorldOptions`（`Full` trajectory retention），再量測單一 ActualWorld、現行雙 world `SimulationSession.AdvanceTo(8000)` 與現行 `PreparePlannedTimeline`。本次在暫時 e57d16b 基準 worktree 以同一絕對 sample 路徑重跑；wall time 不是硬 timing unit test，修改後以同一 sample 比較。

| 項目 | 修改前基準 |
|---|---:|
| ActualWorld requested / current time | 8000 / 8000 s |
| ActualWorld elapsed | 220,220.04 ms |
| ActualWorld fixed ticks / ms per tick | 80000 / 2.75275 ms |
| ActualWorld trajectory / safety / events | 204736 / 113646 / 621 |
| ActualWorld working-set delta / managed-memory delta | +88,854,528 / +71,515,280 bytes |
| 現行雙 world `AdvanceTo(8000)` elapsed / ms per tick | 307,819.40 / 3.84774 ms |
| 雙 world actual trajectory / planned trajectory | 204736 / 200977 |
| 雙 world actual safety / actual events / planned events | 113646 / 621 / 662 |
| Planned requested duration | 12246.50 s |
| Planned timeline elapsed / last event | 83,442.88 ms / 7398.8 s |
| Planned trajectory / events | 200977 / 662 |

基準 runner 的 planned world 在封存 `PlannedEvents`／`PlannedTrajectory` 後立即 reset，因此不再把 reset 後的 `PlannedWorld.SafetyHistory.Count = 0` 當成 planned safety metric；本表只列有效的 planned trajectory／event 封存結果。

本基準確認目前 WPF 路徑的兩項待修來源：播放 API 會同時推進 ActualWorld 與 PlannedWorld；planned timeline 會依固定預估 duration 推進，而不是以 `SimulationWorld.IsComplete` 為完成條件。`tests/MrtRouteSimulator.Performance` 會保留作為後續比較用 diagnostic，不將 wall time 寫成穩定性 unit test。

## 長時間播放效能 Phase 1：修改後結果（2026-09-20）

同一 benchmark、同一絕對 sample 路徑與 `AdvanceTo(8000)` 範圍重跑；另加入實際 WPF playback 所用的 `AdvanceActualTo` measurement：

| 項目 | 修改後結果 |
|---|---:|
| ActualWorld requested / current time | 8000 / 8000 s |
| ActualWorld elapsed / fixed ticks / ms per tick | 256,219.26 ms / 80000 / 3.20274 ms |
| ActualWorld trajectory / safety / events | 42742 / 11401 / 621 |
| ActualWorld working-set delta / managed-memory delta | +31,924,224 / +9,575,008 bytes |
| 實際 playback `AdvanceActualTo(8000)` elapsed / ms per tick | 254,826.85 / 3.18534 ms |
| 實際 playback trajectory / safety / events | 42742 / 11401 / 621 |
| 實際 playback 後 PlannedWorld current time | 0 s（未被推進） |
| 舊雙 world `AdvanceTo(8000)` elapsed / ms per tick | 338,864.96 / 4.23581 ms |
| 雙 world actual trajectory / planned trajectory | 42742 / 200977 |
| 雙 world actual safety / actual events / planned events | 11401 / 621 / 662 |
| Planned max duration（latest dispatch + baseline × 2） | 14462.00 s |
| Planned actual completion / last event | 7398.8 / 7398.8 s |
| Planned trajectory / events | 200977 / 662 |

本輪修正後 runner 會輸出 `samplePath`／`sampleSource`，避免只看檔名而誤用 branch 內另一份 topology sample；planned safety history 不列入，因為 timeline 封存後 planned world 會 reset。

相較修改前，同一 full sample 的互動 ActualWorld trajectory 由 204736 降至 42742（約少 79.1%），歷史 safety observation 由 113646 降至 11401（約少 90.0%）；working-set delta 由約 88.9 MB 降至約 31.9 MB，managed-memory delta 由約 71.5 MB 降至約 9.6 MB。wall time 受 JIT／GC／OS 與 retention policy 影響，這次單 world elapsed 並未宣稱改善；Phase 1 的主要效益是移除正常 playback 的 planned 推進及控制歷史資料成長。實際 playback path 不再推進 PlannedWorld；固定 tick 仍為 0.1 秒，未改 physics、occupancy、moving block、rear-clear 或 safety decision。

本 Phase 已完成：

- `SimulationSession.AdvanceActualTo()` 與 WPF ActualWorld-only playback。
- `PreparePlannedTimelineUntilComplete(maxDurationSeconds)`，以 `SimulationWorld.IsComplete` 為完成條件，超過 fail-safe 會回報 validation error；記錄 `PlannedTimelineCompletedAtSeconds`。
- topology interactive ActualWorld `Decimated(0.5)` trajectory 與 `Decimated(1.0)` safety history；planned chart 仍使用 Full retention。
- 獨立 benchmark runner 與 retention／completion regression。

本 Phase 的 CSV／區間統計／圖表目前仍消費 interactive ActualWorld 的 0.5 秒樣本加事件與狀態轉折；尚未提供獨立的 0.1 秒 Full trajectory offline export。這是刻意保留的輸出精度邊界，已列入 `TODO.md` 的 `V4-PLAYBACK-PERF-PHASE4`，不把互動留存誤稱為完整高解析歷史。

尚未解決（刻意留給後續階段）：

- Engine 單 tick < 1.67 ms 的 hot-path 優化。
- simulation worker／single-writer background task／immutable playback snapshot。
- adaptive UI render FPS、hidden-tab lazy rendering、incremental result accumulator。

最終驗證閘門：

- `dotnet build .\MrtRouteSimulator.slnx -c Release --no-restore`：0 warnings／0 errors。
- `dotnet run --project .\tests\MrtRouteSimulator.Tests\MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore`：150/150 通過，0 失敗；包含 fixed 0.1 s、moving block、collision、turnback、passing、rear-clear、ActualWorld-only、completion、delayed/resource operation 與 safety retention regression。
- `dotnet run --project .\tests\MrtRouteSimulator.WpfTests\MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore`：PASS WPF visual rules；完整 sample matrix、計畫時間軸、雙向預覽、速度圖、CSV／PNG／PDF 輸出通過。
- `tests/MrtRouteSimulator.Performance` Release build／benchmark：通過；`git diff --check`：通過。
- 本輪未執行 60× 原生桌面連續播放人工 smoke；這仍屬 Phase 2／桌面驗收範圍，不以離屏 WPF runner 代替。
## O04 越行側線外觀與上下行鏡射（2026-09-25）

- 原大型範例的 O04 下行側線長 2,070 m，繪圖從 O03 後方一路偏離正線，形成長楔形；上下行月臺中心到實體匯入節點僅 90 m。只改畫線會讓列車在接點跳離圖上的側線。
- 原工作樹的可重建大型範例已將 O04 上下行分別重建為 480 m 的平行正線／側線：140 m 月臺落在 offset 170–310 m，停點在 240 m；岔出與匯入節點各距月臺中心 240 m。相鄰邊分段後總里程維持不變，`SimulationWorld`、車輛 cursor 與固定 0.1 秒步進未改。WPF 大型圖以月臺中心前 240 m 開始側線 Y 過渡，並在實體出口節點回到正線；X 仍取 `StationChainageProjection` 的 edge-local 映射。
- 大型樣本 WPF 診斷檢查 O04／O13 接軌連續、月臺與停點對位、上下行軌距鏡射，以及 O04 月臺中心前後 240 m 的實體節點與畫面座標。播放分支 Release build 0 警告／0 錯誤、Engine 148/148、完整 WPF runner PASS；用重生的正式大型樣本執行 60× 診斷 PASS，觀測 58.9×、最大輸入間隔 42.7 ms，並確認播放期間沒有 `StationStopViolation`。離屏截圖顯示 O04 上下行短分岔對稱；原生桌面與不同 DPI 仍需人工確認。
- 原工作樹最初完整 Engine runner 為 167/170，full station chain、完整情境及 V3.3 sample 均有 `StationStopViolation`。軌跡顯示停點前反覆煞車／再加速，持續全煞開始過晚。現在控制器預視下一個固定步進，並在停站煞車啟動後保持煞車；保留 65 m 設定及正常速度連續性。原工作樹 Release build 0 警告／0 錯誤、完整 Engine runner **170/170**，包括先前 8.58／9.3 km/h 紀錄的 V3.3 範例。

> 本節數字與 60× 診斷均為 `codex/playback-parallel-architecture` 等來源工作樹紀錄；整合分支尚未重跑，不能視為 4.0.2 整合驗證結果。

## O03→O04 路線圖速度失真修正（2026-09-25）

- 根因在 WPF `StationSchematicPresentation.ApplyChainage` 的大型圖例外壓縮：O04 實體越行側線長 2,070 m，畫面卻把兩端強制放在 O04 站心左右約 32 px；前一段 O03→分岔點僅 200 m，反而承擔大部分站間畫面距離。`StationChainageProjection` 的 O03 2.007K、O04 4.277K、O05 4.877K 本身連續，不能改動實體里程或列車 cursor 來修這個畫面問題。
- 移除越行軌端點的人工壓縮，軌道和列車標記按 edge-local offset 使用同一畫面幾何；物理長度、0.1 秒步進和安全計算不變。
- 大型樣本的 WPF 畫面規則現以所有明確站心的來源里程檢查站名與月臺 X 座標，檢查每個月臺中心對應站心，逐段檢查下行主線實體端點的畫面 X 與顯示里程，並確認相鄰 edge 接點連續。舊規則將 O03 後 200 m 的分岔點移到 O04 旁約 27 px，新規則可由端點投影偏差檢出。
- Release build 0 警告／0 錯誤，Engine 148/148、完整 WPF runner PASS；大型樣本 WPF 60× 診斷 PASS，觀測 58.8×、輸入最長間隔 48.8 ms。這是離屏測試結果，原生桌面與不同 DPI 尚未人工驗收。

## O04→O05 路線圖視覺跳躍修正（2026-09-24）

- 原因：大型機場線存檔的 O04 實體節點為來源里程 4,467 m、O05 為 5,067 m，兩站實體軌道為 600 m；播放分支的顯示投影卻從 O04 越行月臺在長邊上的 320 m 停點推得 2.527K，將 O04→O05 畫成 2,350 m。原樣本 O04 下行側線長 2,070 m，停點離站點仍有 1,750 m，使圖上先幾乎不動、再快速跨到 O05。
- 顯示里程現在使用存檔提供的明確站心位置，並以起點站 190 m 作零點；O04 顯示 4.277K、O05 顯示 4.877K。越行正線以所屬路線的進出邊和車站中心投影，月臺中心同站對齊。實體 edge 長度、列車 cursor、安全距離和固定 0.1 秒 tick 均未更動。
- 原工作樹的可重建 builder 與大型樣本只調整 O04 四個月臺：下行停點 320→1,980 m，上行 320→310 m；其餘欄位和原有未提交設定保留。原檔與候選檔逐行比對僅此四個月臺欄位組不同，重產後 SHA-256 相同。播放分支 WPF 診斷檢查站心、O04 月臺靠近站點、正線與側線出站接軌連續；60× 約 58.8×、輸入最大間隔 38.4 ms。Release build 0 警告／0 錯誤，播放分支 Engine 146/146、完整 WPF runner PASS。
- 原工作樹的 65 m 進站設定在本次煞車修正前產生 41 筆 `StationStopViolation`；此事件表示列車以非零速度觸及停點，不能直接當成車體超出月臺的筆數。控制器原本在進站限速後短暫恢復加速，只檢查目前煞停包絡，沒有預看下一個 0.1 秒步進可能造成的晚煞；V3.3 完整範例因此曾以 8.58／9.3 km/h 觸及停點。現在使用共用的下一步煞停預測，並只在觸點速度至少 3 km/h 時記錄違規，低速觸點仍維持煞車與到站流程。以同一份 65 m 大型樣本推進 8,000 秒，41→0 筆違規且全車完成，耗時 8.92 秒；V3.3 完整範例 7→0 筆。Release build 0 警告／0 錯誤，Engine 148/148、完整 WPF runner PASS；修正後大型 WPF 診斷 60× 觀測 58.9×、輸入最長間隔 42.4 ms。原生桌面與不同 DPI 尚未人工驗收。

## 路線圖填滿畫面、33 ms 刷新與列車點選（2026-09-24）

- 模擬分頁的路線圖區域改為填滿剩餘高度，畫布依可視高度調整；小視窗保留必要的垂直捲動，站名與月臺版面檢核為零警告。運行列車標記可點選，會選取對應 VehicleId 並開啟「完整行程速度曲線」。
- UI timer 原已是 33 ms；本輪將 worker 畫面發布及路線圖自身節流改為 33 ms，並解除路線圖被列車狀態列 50 ms 節流包住的限制。列車狀態列維持 50 ms、速度圖維持 250 ms；Engine 固定 0.1 秒 tick 未變。
- 28 站大型樣本 720 px WPF 診斷：60× 播放 3.07 秒推進 180.4 模擬秒，觀測 58.8×；UI 輸入最大間隔 36.1 ms，固定配線重建次數 3→3。自動點選運行列車後，速度圖分頁及車輛選單均對應該車。此為測試視窗結果，原生桌面與不同 DPI 尚未人工驗收。
- Release build：0 warnings／0 errors；Engine：146/146；完整 WPF runner：PASS；`git diff --check`：PASS。

## 28 站大型存檔播放與路線圖修正（2026-09-24）

- 以 `D:\AI\codex\mrt-route-simulator\samples\大型機場線-完整營運示範範例.mrtsim.json` 驗證。列車安全距離原先對同一車對重跑 graph search，且每個 0.1 秒 tick 重找不變的有向節點路徑；現以基礎設施實例及接軌 traversal 為鍵快取橋接長度，保留動態車頭／車尾局部距離計算。600 秒 benchmark 由 47.83 秒降至 1.69 秒，事件 SHA-256 同為 `CEA84C253B4593757E504FA57C6FBAF8D34067284322A26F3073CCEE020DB87E`。
- 同一大型存檔推進 8,000 秒／80,000 tick，7.98 秒完成且 `worldComplete=True`。WPF 顯示視窗的 60× 診斷在約 3.07 秒實際時間推進 180.2 秒，觀測約 58.7×；固定配線重建次數維持 1 次，輸入計時器最長間隔 203.3 ms。此為測試視窗結果，仍需在使用者桌面與 DPI 設定下人工確認操作感。
- 路線圖採原版的大型路線最小站距、水平捲動、獨立路線／即時列車／速度曲線分頁，並修正 O04／O13 重複站名、平台編號與合法越行接軌的示意位置。大型樣本診斷檢查每站唯一標籤、版面警告為零、無配線待修警告、60× 推進及固定配線不重建；輸出 `large-playback-route.png` 供人工檢視。
- Release build：0 warnings／0 errors；Engine：146/146；完整 WPF runner：PASS；`git diff --check`：PASS。大型 sample 位於另一個工作樹，未將該工作樹未提交的 JSON 複製入本分支。

## Playback 架構補強與路線圖刷新（2026-09-23）

- 模擬路線圖獨立採 100 ms 刷新；拓撲、軌道、月台與標籤依專案及畫布尺寸快取，更新時只替換列車標記。列車狀態與時鐘仍採 50 ms、其他圖表 250 ms。WPF 測試確認固定配線在同尺寸時重用、尺寸改變時重建；尚未做原生桌面連續播放的主觀流暢度驗收。
- B2 已補時刻表、V1/V2 比較的新增事件累積，以及區間／全程統計的新增事件與軌跡累積。完整拓撲與尾軌折返樣本推進至 3,600 秒後，時刻表、V1/V2、區間與全程統計均與既有完整分析一致；區間統計的控制事件使用追加索引與時間二分查詢，不在每次顯示時重掃事件歷史。
- 修正計畫時間軸接近 2,536 秒終點時，浮點誤差使背景迴圈反覆要求不足 0.1 秒步進的死迴圈。完整 WPF runner 現已通過長行程、全分頁與 CSV／PNG／PDF 輸出；舊測試改為先切入延遲刷新的結果分頁再驗證。
- Release build：0 warnings／0 errors；Engine：146/146；完整 WPF runner：PASS；`git diff --check`：PASS。Engine 新測試檢查 BasicPhysics 閉式煞車包絡與原固定步進積分一致。
- 代表性 `V4.0.0-完整拓撲執行驗證範例` 有 7 車次。ActualWorld 推進 8,000 模擬秒三次，wall elapsed 2.39／2.40／2.47 秒，總平均 0.030／0.030／0.031 ms/tick；13442 trajectory、3282 safety、99 events，working set 終值約 77–78 MiB。三次事件序列 SHA-256 均為 `4F4F67E105A92544BF56FF34BD15E3A952FB494975678F5C6527A7A1BC0C22B2`。列車約 2,610 秒已全部完成，因此總平均包含後段空載 tick，不可當作運行中單 tick 成本。這個工作樹沒有 28 站臺中機場線 full sample，不能以此數值代表該大型路線。
- 計畫模式同範例推進 2,536 秒的單獨量測約 3.10 秒；完整 WPF runner 中的計畫時間軸也順利完成。依目前樣本 Engine 耗時遠低於 60× 的 1.667 ms/tick 門檻，暫不啟動 C 階段 tick 平行化；需先取得大型樣本及原生桌面 profile 才能判定真正瓶頸。
- 仍未完成原生桌面 1×／10×／30×／60× 操作、不同 DPI、UI FPS／最長卡頓實測及 O04／O13／O20 畫面驗收；目前無法宣稱路線圖已達主觀流暢度目標。

## Playback worker 與分級刷新第一階段紀錄（2026-09-23，後續結果見上節）

- B1 已將實際 `SimulationWorld` 交由單一播放 worker 擁有；命令佇列依序且可靠，UI frame 使用容量 1、丟棄舊 frame 的 bounded channel。WPF 消費 immutable frame，不再直接讀取 worker 正在修改的 world；0.1 秒 Engine tick 及拓撲安全流程未改。
- 計畫時間軸改由獨立背景 worker 建立 artifact；實際播放可在計畫運算期間啟動。工作者支援播放／暫停／重設、倍率與安全模式命令、障礙物事件、關閉與換檔清理，並以 generation ID 隔離舊 frame。
- B2 目前部分完成：事件、軌跡與安全歷史以獨立 cursor 只擷取新增資料，資源 Reserve／Release 占用由 accumulator 增量維護。時刻表、區間／全程統計及 V1/V2 比較仍在資料變更且分頁需要時呼叫既有完整分析，尚未達到所有結果 `O(new data)`。
- B3 加入差分列更新、事件 append 上限 300、隱藏分頁延遲刷新與分級刷新週期；播放狀態 tooltip 提供 worker、結果累加及 UI render 計時資訊。拓撲互動軌跡採 0.2 秒留存間隔，Physics 仍逐一執行 0.1 秒 tick；本輪未驗證高解析離線匯出。
- Release build：0 warnings／0 errors；Engine：145/145 通過；WPF playback worker targeted runner：PASS，涵蓋 latest-frame、可靠命令、固定步進、增量資源、暫停／重設及 worker 清理。
- 完整拓撲 WPF 長行程 runner 需背景計算 2,536 秒計畫時間軸。本輪執行超過 700 秒 CPU 仍未完成，之後中止；WPF 完整 runner 與 CSV／PNG／PDF 長情境輸出 runner 均未完成，不宣稱通過。視窗／DPI 桌面人工驗收、1×／10×／30×／60×播放及 8,000 秒前後效能基準亦未執行。
- B2 全結果增量化與代表性效能基準列為後續工作。C 階段 Engine tick 平行提案／leader 查找尚未實作；需待 B 階段完整驗收並 profile 確認 Engine 熱點後再評估，以維持 topology 資源仲裁與 deterministic single-writer 行為。
## 大型 sample builder、PDF 分頁與 GPT-use 乾淨整合（2026-09-19）

- 本節結果來自 `codex/integrate-large-route-clean`：以最新 `origin/GPT-use` 為基底，非直接 merge 舊分支；保留 13 個 Schema 8 sample、大型機場線主要站簡化 sample 與 `.github/pull_request_template.md`。
- 本次重新執行 `dotnet build MrtRouteSimulator.slnx -c Release`、Engine runner、WPF runner 及 `git diff --check`；以下數字是本次整合分支實際結果，不沿用 `a61d4bc` 的舊執行紀錄。

- 速度圖按 VehicleId 串接上下行及折返，保留計畫預覽／實際截至目前的區別；重設及換檔不保留舊列車。移動閉塞仍可獨立按方向、配對及時間篩選，列車退出後可查歷史。
- 尾軌返回反向終點月台後產生到站、停站及出站事件；0 秒停站仍等待接續班表，正常停站案例須滿足反向停站時間。
- 時刻表、區間及比較使用結構化站號／月台識別；TrainCenter 車頭經過中心但尚未到站時，不提早完成區間。上行顯示里程、預覽結果初始化及全程時間摘要一併修正。
- Release build：0 warnings／0 errors；Engine：145/145，0 失敗；WPF runner：PASS；`git diff --check`：PASS。完整 WPF runner 包含 13 個範例、720／1200px 速度圖、閉塞方向篩選、完整拓撲及 TrainCenter 範例推進 3600 秒，以及 CSV／PNG／PDF 輸出。
- 大型機場線主要站簡化 sample 經本次整合修正為可驗證的 minimal baseline：直達模式的 O26 為合法終點停靠，topology 內明列 10 段主線 `directedConnections`；仍只使用 synthetic test values，不宣稱正式路線資料。
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

本軟體是營運與號誌概念模擬器，不是可部署的鐵路安全系統。Schema 8、physical facility traversal、occupancy／footprint、topology-native safety、V2 內部 Route 相依移除與已完成的 V4 UI／正式驗收均保留為歷史通過事項；依現行 `TODO.md`，目前尚未完成的 V4 交付項目包含 `V4-LEGACY-PORT-MIGRATION`、`V4-UI-TRACK-DIAGRAM-MANUAL-01` 及長時間播放效能 Phase 2～4。去識別化大型 sample scenario builder 已完成 Engine gate，但 full sample WPF editor-720 尚有 `EDGE:PASS-002` 示意突折；桌面項目仍只完成部分抽查，尚待不同 DPI、折返／交會關鍵畫面與連續播放驗收。Phase 1 的來源分支建置、測試與 benchmark 紀錄也須在本次整合後重新執行，才能作為整合分支的驗證證據。

以下屬遠期修正或非產品目標：

- 指定真實路線的坡度、曲率、黏著、車型與時刻資料校準，以及高負載效能測試。
- 完整單線共用運轉、聯鎖失效模型與營運最佳化。
- ATP／ATO／ATS、安全完整性等級或現場設備認證。

歷史需求已整理於 `CHANGELOG.md`。

## 2026-10-01 native100% partial acceptance

Fresh WPF telemetry confirms96x96/1x after user100% confirmation. Paused FULL-O13 marker selection and TimeDistance28-label visual subchecks collected; narrow/short scrollbar endpoints verified. Paused maximize plot-width freshness remains open, as does cold Diagram tab209.49ms latency. Full DPI/resize,125/150%, event visual, five-run memory and post-fix60x sanity remain NOT COMPLETED. Details/evidence in `docs/NATIVE_PLAYBACK_FINAL_PROGRESS.md`; no Engine/sample changes or publication.

After user125% handoff, fresh20:46:43/20:46:53 Taipei checkpoints still report96x96/1x.125% NOT COMPLETED pending App DPI configuration diagnosis; no125% visual/hit-test PASS claimed. Simulation paused; proposed App-only follow-up requires user scope approval.

Subsequent explicit user approval: App DPI manifest/paused viewport fix completed; final Release0warnings/0errors, Engine186/186, fullWPF and diff-check PASS. New native sessione03f7a93 independently confirms120x120 DPI/1.25x; paused FULL-O13 hit-test and Diagram max/restore redraw positive. Full per-page125% resize, refreshed100%,150%, memory/event/sanity gates remain NOT COMPLETED. See `docs/NATIVE_PLAYBACK_FINAL_PROGRESS.md` for scoped files and evidence.

Later150% native observation independently confirms144x144/1.5x; paused FULL-O13 hit-test and Diagram dense-label/scroll/narrow-short checks positive. New **Speed small-window lower chart/time-axis clipping FAIL**, no scroll access. Overall NOT COMPLETED; permission requested for minimal presentation-only repair. No source changes in observation turn; remaining per-page DPI/memory/event/sanity gates retain pending status.
