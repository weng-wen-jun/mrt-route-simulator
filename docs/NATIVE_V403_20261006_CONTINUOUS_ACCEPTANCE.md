# V4.0.3 原生續驗（2026-10-06）

## 最新停點摘要

- early-reset開始旗標版本：Release、Engine198/198、完整WPF及原生第三輪早reset子項通過。先前五輪正常idle/reset與第五輪收尾亦通過，但不是同一版本／同一量測session，不合併為新五輪。
- 目前仍NOT COMPLETED：記憶體分類、先前gap根因、完整互動與其他DPI矩陣。test-only弱引用檢查及A3匯出對話框標示窄修正進行中，後續變更需重新gate。
- branch `codex/v403-playback-integration`、HEAD `68084cbd39d32b02fab9f8718d29b4ef5d5fb88f`；未commit／push／merge，保留所有既有dirty修改。下方為依序追加的過程，不以早期待驗文字覆蓋較晚結果。

## 範圍與停點

- 接續2026-10-04第五輪idle/reset量測修正版；整合工作樹D:\AI\codex\mrt-v403-integration，未改Engine／Schema／sample，未commit／push。
- 正式Release DLL時間2026-10-04 20:25:08、1120768bytes，NativeAcceptance.cs 58048bytes；僅啟動一個正式App window1774136，正常讀取sample14。
- 新session `9c989122-b4db-4a55-a540-775fbb85b76b`；來源NativeAcceptanceLogs/native-acceptance-20261005-233628216-9c989122b4db4a55a540775fbb85b76b.jsonl。檔名為UTC，使用者日期為台北2026-10-06。
- sessionStarted實測120DPI（125%）、interfaceScale1、DISPLAY1非primary；windowBoundsDip1382.4×820.8是Width/Height metadata，不能冒稱actual client。正常視窗、Diagram頁、左右/上下100%、手動10分鐘、終點on、事件列表展開、60×。
- 計畫五輪每次完整完成、等待10sIdle checkpoint後正常Reset，不切頁／重啟process、不強制GC、不於播放期間建置或跑重測。
- 第五輪afterReset原生修正尚未驗證；完整WPF、其他DPI／尺寸與gap根因仍待，整體NOT COMPLETED。
- 第一輪完整7172.9s，10sIdleAfterCompletion與afterReset均寫入，正常reset後第二輪播放已開始；不以單輪證明第五輪collector收尾修正或無洩漏。
- 前三輪各7172.9s完整完成，均確認10sIdle後才正常reset；同一正式process／Diagram頁第四輪已開始。未跑建置／測試、未切換DPI或頁面，最終event parity／cache及memory統計待五輪收尾。
- 五輪7172.9s均完整完成，每輪確認10sIdle checkpoint後才正常Reset。第五idle後選單Start disabled／Stop enabled，證實session仍保活；正常第五Reset寫出afterReset之後才sessionStopped completedRuns5／activefalse。collector第五reset漏記修正原生子項PASS，不能擴大為無洩漏／流暢度PASS。
- 原始檔保留，歸檔至artifacts/native-20261006，五輪events／倍率／cache／GC及gap交叉核對進行中；未新增第六measured run。
- 歸檔metrics SHA256 `73BFEC9F072EB0BF1D539041E46269E3B69CCCB7CAE799E3682A799D750ED729`；events `2CA4C37361533180BE079A96E251BACED24CB11BEC56CB9CAC9E9E738E037C7F`，來源／複本Equal，原檔未覆蓋。
- 主代理核對runCompleted五輪eventCount1025／hasPausefalse，activeWall119.5461233／119.5481031／119.5474668／119.5485331／119.5485574s，active倍率59.99988754～60.00110921；整份raw dispatcherStall共0，不宣稱OS input/compositor或跨環境流暢度通過。

## 完整WPF gate（未通過）

- 五輪session停止後才執行完整default WPF runner，明確預告測試依序Show/Close；至原生量測入口的early Reset回歸FAIL exit1，後續SpeedJourney／Output尚未執行，不列full PASS。
- 失敗NativeAcceptanceTests.VerifyEarlyResetSkipsIdleForFourthRun line568：在reset lifecycle開始與AfterResetApplied之間，await worker.ResetAsync會pump Dispatcher，ProbeTick可能於reset完成前寫出idle。正在限定collector／對應tests做最小開始邊界修正，不降低assert、不改Engine或sample。
- 主代理序列化完整events陣列逐筆比較五輪與2026-10-04 c3a6基準，每輪1025筆全部欄位Equal=true；五次afterReset actualProcessed／actualOrdinary／actualCritical皆0，plannedProcessed178992固定。afterReset privateMiB236.4609／247.4102／254.2617／261.9844／272.0508仍上升，不列無洩漏PASS；first/fifth frameHistory null屬發布邊界，其他為T0/S0/E1，不可把null強稱完整已讀取。
- 唯讀source及raw核對InputStall每輪Count3803／3805／3803／3807／3805，max72.1309／65.4356／63.1681／63.1370／70.2435ms，並非probe未採樣導致0 gap。這是DispatcherTimer surrogate，非OS input或compositor證據；前輪gap原因未被證明已解決。
- early Reset最小修正仅NativeAcceptance.cs新增ResetLifecycleStarted，既有playback-reset lifecycle開始即設旗標，idle timer排除重設中區間；對應fixture在ResetAsync pending、afterReset前明確pump ProbeTick。舊DLL deterministic RED line573，主代理source/tests複核、Release0警告／錯誤、native-acceptance-only GREEN exit0、diff --check PASS，完整WPF重跑中。
- 9c989122五輪證據屬此次early-reset開始旗標修正之前的正式binary；不得將其當成修正後的完整新五輪。正常idle→reset漏記修正子項已驗，新增早reset邊界有自動化證據，原生早reset另待。

## 修正後完整回歸

- 新增ResetLifecycleStarted的Release版本，完整default WPF runner重跑exit0；包含NativeAcceptance／NativeAcceptanceInput、長行程速度結果、全部sample載入、版面、CSV／PNG／PDF及分頁回歸。保留上述首次FAIL紀錄，不將其抹除。
- Engine完整runner正在重跑；記憶體持有路徑另做唯讀檢查。原生早reset、其他DPI及既有gap根因仍未因此通過，整體NOT COMPLETED。
- 子代理交叉核對五輪完整事件陣列與c3a6基準0 mismatch；throughputExact=false，active倍率以完成frame發布邊界计算，並非精確final engine tick。InputStall屬20ms Input priority timer量測，不等於真實點擊延遲。
- 新版完整Engine runner exit0：198/198通過、0失敗；diff --check exit0。正式App已單次啟動新binary window3084858，正常讀取sample14，後續原生邊界續驗不混入舊五輪。

## 新版原生早reset邊界

- 獨立session `85312aec-4c84-4d35-b44a-1b2fa07d5435`，同一新版正式process／Diagram頁、sample14、120DPI、interfaceScale1、正常60×，三轮完整7172.9s。每輪完整1025筆事件所有欄位與9c989122基準相同。
- 第一輪completion08:06:44.3189453、afterReset08:06:55.1420984；第二輪completion08:09:19.8313158、afterReset08:09:34.1975583，皆已先合法寫出10sIdle。因原生操作往返延遲，這兩次不列早reset通過，也不刪除紀錄。
- 第三輪completion08:12:08.6414269；正常playback-reset lifecycle08:12:13.2054790（差4.5640521s），afterReset08:12:13.2231842。actualProcessedCount=0；session直到08:12:58.002672才正常Stop／flush、completedRuns3，第三輪全段沒有10sIdleAfterCompletion。新版原生早reset邊界子項PASS，最窄reset await競態以確定性自動化fixture補足，不能冒稱原生必然撞到該競態。
- 三輪不是五輪strict memory repeat，圖表刻度為自動，不與先前手動10分鐘五輪合併。原始檔非覆蓋归檔artifacts/native-20261006；metrics SHA256 `B6547C03DE99FCCA8DC67BE8605239B932F98BA4BFBA1560722434EBEC1F16A0`、events `8E5E5604C18457EFE7AD5E65B1B432C3DD7A96C03BBDB3D36D505115356A3DE3`，來源／歸檔Equal=true。

## 記憶體靜態持有檢查（非heap證明）

- 子代理唯讀追蹤worker/world、result accumulator、actual diagram cache及collector；主代理複核worker reset清空immutable histories、cache Reset清空states/groups、事件寫檔任務與session dispose路徑。
- 同一worker/world重用；planned timeline／178992 planned samples刻意常駐。尚未找到每輪累積持有全部舊world／actual history/cache的確切路徑。
- collector LastFrame、事件寫檔task可能短暫保留舊frame／event root；完成task是否仍持有closure及WPF native allocations不能由privateBytes判斷。尚無heap/object-graph證據，不能列無洩漏或GC根因PASS。
- 可續做独立test-only weak-reference存活檢查，將強制GC限於測試程序；不可對正式原生五輪注入GCCollect。若仍存活再追根，不先修正推測洩漏。
- 已非阻塞詢問使用者協助切100% DPI；回覆前繼續可自行處理項目，不猜測目前DPI已改變。
- 85312aec新binary三輪active倍率60.00065378／59.99988729／60.00009297，throughputExact=false；final trajectory37172／safety9147／active0。raw dispatcherStall共0，InputStall count3797／3804／3805、max78.7074／75.3901／64.4982ms；不外推OS輸入／compositor，也不據此宣布先前gap根因已解決。
- 記憶體測試已限定委派為test-only NativeRetentionTests及共用Show前的專用route，正式App仍已停止播放／量測。測試若無法可靠隔離JIT強引用須保留不確定性，不降低assert。

## 先前gap唯讀對照

- 有界選取c3a6舊session與9c989122配對：同sample14、Diagram workspace、120DPI、interfaceScale1、視窗metadata1382.4×820.8、正常60×、手動10分鐘。85312aec為自動刻度，不能排除它與舊session的刻度配置混雜。
- c3a6 raw共127筆dispatcherStall；line259 occurrence106 elapsed1820.6542ms，真正active workspace是「列車運行圖／匯出」。同record的simulationViewHeader僅隱藏動畫子頁選擇，不得誤稱該gap在Route頁。
- line260 checkpoint WorkerAdvance max31.7457ms、ApplyPlaybackFrame max16.6314ms、TimeDistanceRender max14.7729ms、DispatcherApplyAge max40.2496ms，未找到接近1.8s的已記錄scope；recentInputAction僅play#18，handler已在gap開始前18.6s結束，不支持這次gap由已記錄的按鈕handler阻塞。
- 本次9c五輪／853三輪InputStall都持續採樣而raw>100ms gap0，排除整段probe未arm。既有累積GC counters不含pause／ETW，不能證實或排除GC因果；Dispatcher排程／未覆蓋UI工作／跨process背景壓力仍是候選，不是根因結論。
- 下一個最小方案是有界opt-in逐次timestamp trace對齊probe／publication／Apply／Render／Advance，若再次重現再找scope overlap；尚未實作，不先改正常播放路徑。

## 匯出紙張標示與存活測試整合

- A3儲存對話框寫死A4已由source確認。MainWindow.V2.cs先解析同一pageSize供dialog Title與實際ExportPdf使用；保留filter／副檔名／自動補名／OverwritePrompt／預設檔名。新增無Show回歸A3/A4。
- 測試最初誤把SaveFileDialog.DefaultExt讀回值當成`.pdf`；實測getter為`pdf`、AddExtension/OverwritePrompt=true，校正為既有contract。保留舊A4標示的scaffold建置後，A3標題斷言RED exit1；恢復正確pageSize標示後Release0警告錯誤、同步PNG/PDF專用GREEN exit0。尚未新原生對話框重驗。
- 存活測試委派誤落舊樹mrt-route-simulator，舊樹build/run不列整合版證據。主代理逐字轉移新test（正規化換行ContentEqual=true）到mrt-v403-integration，撤回僅代理新增的舊Program route／test檔；內容仍保留於整合樹，可回復。其他dirty修改未動。
- 主代理將整合版`--native-retention-only`置於共用Show前；替換不存在的舊sample檔名為現行sample14、等待既有planned timeline/cache ready。初次正確樹執行因舊檔名FAIL，下一次在reset初始event斷言FAIL，均未到weak存活結論。
- 依Engine.Reset的ActivateDueTrains契約，reset會產生合法零秒發車event，不能要求events空。測試改為reset events與beforePlay初始frame events全欄位一致，trajectory/safety仍要求清空；sample14 completion另強制非空safety以避免空singleton跳過。這是修正測試預期，不改Engine或降低舊history清理要求。專用重跑中。
- 正確整合版sample14關閉後弱引用檢查已exit0；主代理追加更強邊界：停止collector後仍以probe.Owner強持有未關閉window／worker／planned graph，再強制GC檢查舊completed frame／非空trajectory/safety/events／actual series/samples。最後才Close清理，避免僅證明整個關閉視窗graph可回收。此強化專用重跑中；不將測試GC當原生五輪無洩漏證明。
- 保留owner graph的強化專用exit0，selected old roots全部不可達；planned／worker／world在Reset重用且planned cache保留正向對照通過。只能支持此sample／此Stop邊界的managed-root釋放，不能外推無WPF/native洩漏、正式五輪private growth已解釋或GC因果。最後整合Release0警告錯誤，完整WPF重跑中，正式App尚未重新啟動。
- 最後整合版本完整default WPF再次exit0，含A3/A4 dialog標示、同步PNG/PDF、原生入口、長行程與全部sample gate；獨立retention專用另exit0，不注入default runner的pacing流程。diff --check exit0；Engine最後重跑中。正式App維持關閉，待gate後只單次重開，尚未新原生PDF Title觀察。
- 本輪實際程式變更：MainWindow.NativeAcceptance.cs reset-start flag；MainWindow.V2.cs PDF Title/pageSize共用；對應NativeAcceptanceTests.cs、SynchronousDiagramExportTests.cs、NativeRetentionTests.cs及Program.cs專用路由。未改Engine／Schema／sample／nearest-leader預設；所有其他dirty內容保留。
- 最後完整Engine exit0：198/198、0失敗；Release0警告錯誤、完整WPF及獨立retention／同步匯出全部通過，diff --check exit0。正式App此後才單次重開最終整合Release，續做原生Title文字核對，不重新啟動正式五輪。

## 最終版原生對話框核對

- 同一最終正式Release window4592180、sample14。初始0秒匯出按鈕依既有契約要求先播放，正常顯示可關閉警告；正常關閉後短暫30×播放、暫停於00:03:01.9，再回運行圖。這次沒有開始原生量測session，不與前述60×完整輪次混算。
- 選A3後原生對話框／標題列均為「匯出列車運行圖 PDF（A3 橫向）」；選A4後均為「匯出列車運行圖 PDF（A4 橫向）」。兩次皆Escape取消並確認對話框消失、狀態仍為模擬已暫停，無寫入／覆寫PDF。紙張Title子項PASS，非重新render文件內容或完成全PDF矩陣。
- A3對話框的UIA取消索引不可用，重新觀察後跨父視窗截圖座標亦被邊界檢查拒絕；改用正常Escape取消成功。未重複儲存、未另開正式App。
- 最新自動化gate：Release0警告／錯誤、Engine198/198、完整default WPF、同步匯出專用與保留owner graph的retention專用皆exit0。其他DPI、private memory growth與既有dispatcher gap根因仍待，整體NOT COMPLETED。
- 同一暫停中的最終正式視窗，事件列表收折後圖區可見高度增加、EventDataGrid消失；左右縮放1不變，上下由1單獨調至2。正常滾輪及拖動圖內滑桿至下端（UIA scroll value479.87）後，時間軸與01:59:22.6終點仍在固定底部，原生可見子項PASS。沒有由截圖大小推測新DPI，也不外推100%／150%全矩陣。
- 依原始任務的文件更新要求，同步在NATIVE_DESKTOP_ACCEPTANCE.md及PLAYBACK_END_TO_END_PROFILE.md新增目前整合版checkpoint，保留歷史失敗與版本邊界，避免首頁仍只顯示10月1日準備／WPF未決狀態。

## 下午續驗：短互動紀錄與 100% 原生子項

- 接續時舊正式App已不存在；55b49018短互動session的raw最後為window-closing，不重用舊視窗或假稱仍在播放。此session161筆紀錄／45個actionId，runStarted1、runCompleted0、playback-reset abort1、沒有sessionStopped；不是完整五輪／throughput驗收。早期120DPI、後段96DPI，不能混成單一125%固定条件。
- 子代理唯讀核對、主代理續查原始尾段：explicit applicationVisualUpdate共8組，tab n3的input-to-application-update p50/p95/max為3.847/15.481/15.481ms；pause n2為33.663/101.840/101.840ms；play+resume n2為76.193/122.790/122.790ms。nearest-rank、小樣本，不能宣稱穩健p95或硬體／compositor延遲；scroll11只有surrogate，trainClick/nestedTab direct均0。raw gap2次141.8265／124.4567ms，仍未解釋，不與五輪0 gap紀錄合併。
- 55b原始metrics/events非覆寫歸檔至artifacts/native-20261006；來源／複本SHA256核對。停止時序不完整仍如實保留，不補造sessionStopped。
- 僅單次重開相同正式exe，新window136408。讀檔期間偵測到手動操作，停止原輸入並重新觀察；確認sample14已載入，不再重送檔案。舊UIA仍顯示讀檔中而截圖已更新，經重新選取／恢復後核對新控制樹才續行，未將過期UIA當驗收證據。
- 新獨立session959228f2-d910-45af-81f1-d87c3d8604cb，raw sessionStarted實測96×96DPI、interfaceScale1、DISPLAY1；這次100%有WPF實際telemetry，不以capture尺寸猜比例。正常30×播放後Pause，04:43.1在後續重新觀察時保持穩定；原生點選橘色marker後速度頁目標為FULL-O04，source也同VehicleId，hit-test子項PASS。Speed正常resume／pause後停07:49.8，非完整run。
- 100%運行圖左右／上下各1，事件列表收折後可見全28站；O08/O08a/O09、O15/O15a/O16文字與引導線分開。正常／最大化／還原原生畫面下，全28站與時間軸／終點可見，限定站名／固定軸子項PASS。尚未完成窄、矮、寬、高手動resize全矩陣，不外推150%。
- 縮小尺寸嘗試拖曳沒有改變視窗，Alt+Space擷取時accessibility為null；正常Escape恢復後，標題列右鍵可取得原生大小選單，但尺寸仍未成功改變。此為自動化限制／未完成，非App縮放PASS，不為此修改排版或Engine。
- O04續驗以本輪9c989122同sample14完整事件導引：678.8s正線鎖定、689.1s passing traversal、710.8s正線跨站、715.5s側線車發車、738.4srear-clear。新9592在469.8s原生看到FULL-O04位於O04側線；改10×後擷取已越過中段，840.7s為越行後主線畫面。中段未捕捉，不列本次完整O04 visual PASS；後續需1×再驗，不能用事件正確代替連續目視證據。

## O04 慢速重試與水平滑桿核對

- 同一9592 session正常Reset後重試：30×停525.8s，10×停644.6s，再改1×。694.1s原生畫面同時可見快車在O04正線、普通車在下方側線；724.6s快車已在O05附近，普通車仍在側線出口彎道。尚未觀察738.4s後rear-clear／完全回主線，不列整項完成。畫面由原生工具顯示，未另存PNG。
- 使用者詢問路線圖左右滑桿消失。核對當前畫面及UIA：`ShellRouteHorizontalScrollBar`可見、Value0，位置在視窗最底部、狀態列下面。XAML故意將RouteScrollViewer內建水平滑桿設Hidden，改由DockPanel底部固定滑桿同步HorizontalOffset；不是滑桿整體消失，也不是外層整頁水平捲動。這源自先前要求不必捲到底才看到滑桿的配置，現在位置與路線框分離，容易誤認。
- 本次嘗試UIA set_value因focus element 0x80131509失敗；重新觀察確認Value仍0。未宣稱此操作驗證拖曳成功、未擅自修改配置。播放保持暫停於724.6s，collector仍active，後續須正常Stop／flush再歸檔。
- 後續使用者授權改為路線圖下緣頂層滑桿。改版前9592已正常Stop／flush、completedRuns0，再Alt+F4關閉；原始兩檔非覆寫SHA256核對歸檔。本次UI修改、回歸與新版原生結果另見 [ROUTE_STICKY_SCROLLBAR_PROGRESS.md](ROUTE_STICKY_SCROLLBAR_PROGRESS.md)，不將此短session與新binary結果合併。

## 滑桿新版後獨立事件目視續驗

- 正式exe同一window2954360／sample14，獨立session `6a5d4076-0694-4ec5-95df-8679d4563e40`，原始stem `native-acceptance-20261006-072304351-6a5d407606944ec595df8679d4563e40`。sessionStarted WPF實測96×96DPI、interfaceScale1；未與舊9592短run或120DPI五輪合併。
- 30×正常播放停523.3s，10×再停642.4s。O04普通車在下方側線、快車仍在O03–O04主線接近；暫停點選側線橘色marker，速度頁source明確為FULL-O04，再正常回Route，clock不變。關鍵段改1×，尚在補中段／離開側線／rear-clear畫面，不提前列整項PASS。
- UIA有過期索引／null accessibility與畫面不一致，採重新觀察及原生座標操作；開頭其他視窗遮住App後正常activate才目視。未透過殼層UIA／私有Engine方法推進，工具往返時間不列App輸入延遲。
- 新版O04關鍵段：692.8s快車marker位於O04正線、FULL-O04仍在下方側線；724.1s普通車位於側線出口彎道；748.4s普通車與快車皆位於主線、快車領先，側線已空且出口鎖定標色消失。補到同新版的前／中／後原生觀察，車輛身分已由暫停hit-test核對；本輪events尚未flush，待完整資料核對rear-clear738.4s後才收斂event+visual子項結論。畫面顯示不代表連續影片或另存PNG。
- 9592原始gap唯讀audit：run1 raw12筆最大2167.1288ms（L89），run2新增51筆最大3213.8117ms（L136），counter63為session累計。最大gap當時active Route；各run已量測WorkerAdvance最大17.9344／17.9073ms、Apply最大51.5504／22.8506ms、RouteMarkerRender最大22.3904／22.7306ms，未覆盖2–3秒gap。全部viewport96DPI/interfaceScale1、manualStop L268 completedRuns0；不支持GC/OS/compositor或recent handler因果，與新版6a5d不合併。
- 續播30×後，恢復控制時已到4878.5s；O13關鍵1199.2–1377.8s及O20入袋4499.7–4571.7s未捕捉。這是本次觀察漏段，不是App功能FAIL；不以預期deadline代替實際clock、不列完整visual PASS，須重跑補驗。
- 4878.5s正常拖動Route固定滑桿0→815.9394703656999；原生畫面到O08–O23。即時狀態表核對SECTION-VEHICLE-01為下行／折返／24.259km／0km/h／O20。兩次袋狀軌附近座標click未切速度頁，未宣稱marker hit-test成功。
- 30×接近後暫停實際5539.0s，改1×。5584.4s列車仍在袋狀軌等候；5606.8s原生marker沿袋狀軌出口移動，狀態表同VehicleId為上行／尾軌返回／24.236km／25.1km/h／下一站O19；5640.2s marker已回上行主線O20、袋狀軌內已無marker。切頁期間clock保持不變。此為折返等待／出發／回主線分段子項，入袋前／中段仍待；events須正常flush後核對5600s車次轉換及5635.6s資源釋放。
- 本輪checkpoint已見ApplyPlaybackFrame max207.0927ms、RouteMarkerRender max153.5267ms；不能沿用舊零gap輪次宣稱新版完全無尖峰。後續須核對raw dispatcherStall與對應scope；不以工具呼叫往返時間推算App latency，不先歸因GC／OS。
- 6a5d正常全程完成7172.9s、trajectory37172／safety9147／所有列車退出；15:36:41.6861161+08正常Stop，completedRuns1。多倍率／多暫停輪次不能當不暫停60×throughput gate。兩檔非覆寫歸檔：metrics SHA256 `38D055300AD78567344E9F9FE77D104C8DFE68A1F1C1F2FE66BE55C70101C663`、events `A8084159A348353CEEB8886941321B3CE7734A77E279B87AF44EB6C29ABB4943`，來源／複本Equal=true。
- 正常Reset後建立獨立補驗session `8baf3227-cde3-4a26-9bb5-b46c969172f8`，stem `native-acceptance-20261006-073732466-8baf3227cde34a269bb5b46c969172f8`。O13前段1147.2s普通車在入口前主線；10×停1246.1s後側線pink marker暫停點選，速度頁Source明確FULL-O13，回Route clock不變。改1×：1321.5s快車在O13正線passing區、FULL-O13留側線；1357.9s快車已過、側線車出口開始移動；1388.6s兩車皆在主線且快車領先，側線已空、出口鎖定標色消失。補齊分段前／中／後目視；本補驗events仍待flush核對，不假稱連續影片／已存PNG。
- 6a5d子代理唯讀核對及主代理獨立複核：runCompleted L319為7172.9s、1025事件、37172軌跡、9147安全觀測、active0；sessionStopped L327 completedRuns1／activefalse。events全部欄位與9c989122各五輪均0 mismatch；主代理獨立逐欄JSON比較第一輪Equal=true。O04 idx107 passing689.1s／idx129 rear-clear738.4s與本輪前中後目視吻合，限定sample14／96DPI／此binary的O04 event+visual子項PASS。O13 idx272 passing1316s／idx293 rear-clear1377.8s；pocket idx924同SECTION-VEHICLE-01、Direction +1→-1、SECTION-DOWN-01→SECTION-UP-01於5600s，idx931於5635.6s釋放AUTO:RESOURCE:POCKET-001；O20仍欠入袋目視，不列整體PASS。
- 6a5d raw dispatcherStall45筆，前三大1050.7862ms（L165／occurrence33／sim1683）、597.6899ms（sim16.9）、485.8352ms（sim119.4），皆active Route。final WorkerAdvance max34.1454／Apply207.0927／RouteMarker153.5267／DispatcherApplyAge76.7056ms，不足以直接解釋1.05s gap，尚無timestamp overlap因果證據。
- 6a5d直接applicationVisualUpdate（不混surrogate）nearest-rank：play n1 p95/max395.0506ms；pause n10 76.1686；resume n10 24.3614；nestedTabClick n6 5.4027；trainMarkerClick n1 306.0606。小樣本不能當穩健p95；play／pause／trainClick超50ms門檻，互動效能仍未通過。rate無direct record、otherClick只有surrogate；不等於OS injection／compositor present，整體NOT COMPLETED。
- 8baf補驗O20入袋：4276.6s列車仍在O17附近，10×接近後4482.9s在O20到站前主線，暫停點選marker的速度頁Source明確SECTION-VEHICLE-01。回Route保持clock，改1×：4510.7s在下行主線O20停站；4543.7s入袋已鎖定、中心仍在分支入口；4564.3s中心沿分支抬升進入袋軌；4582.7s停在袋內。即時狀態表同VehicleId、下行／折返／24.259km／0km/h／O20；主線上已無SECTION marker。此補驗前／中／後與先前6a5d出袋／轉上行分段互補，兩session／同binary條件明記，不假稱一次連續完整錄影。待8baf正常flush核對事件後收斂O13及O20限定子項。
- 8baf正常完整auto-stop frame time7173.0s、trajectory37172／safety9147；Stop15:49:39.7452012+08、completedRuns1。sample最後事件仍與基準一致，frame time相差0.1s不作事件完成時刻漂移結論。全1025筆events逐欄JSON比較與9c第一輪Equal=true；WPF實測96DPI/interfaceScale1。限定同binary／sample14／100%條件，O13 event+分段visual子項PASS；O20入袋、折返等待、同VehicleId轉上行及出袋／資源釋放以8baf和6a5d兩輪互補子項PASS。不是連續單輪影片／全DPI或整體流暢度PASS。
- 8baf非覆寫歸檔metrics SHA256 `556D07B6295249C12EBFCCB5B0DEE98693690792D4B8E4F0CA0E1974F3EF15C8`、events `52E74D9B38AAEF8F2A8419147333A2A23139631E989BC79A9D4DD86DE18C8A11`，來源／複本Equal=true。App保留完成頁，接著獨立新版不暫停60×sanity，不與多倍率補驗混算。

## 新版 100% Route 不暫停 60× 單輪

- 同正式window／sample14／Route、96DPI/interfaceScale1，獨立session `47790043-bd89-45c6-ab3c-c731ba5bd931`，stem `native-acceptance-20261006-075109788-47790043bd8945c6ab3cc731ba5bd931`。正常Reset後Start，再正常Play，未Pause／改倍率／切頁；7172.9s全程完成，1025事件全欄與9c第一輪Equal=true、37172軌跡／9147安全觀測／active0。
- playAcknowledgement→final frame publication119.5464375s，active倍率60.00095151308879；dispatch保守倍率59.987304559130486。hasPause=false、throughputBoundaryDefined=true、throughputExact=false（frame publication不是final tick精確timestamp）。限定新版Route單輪throughput／事件一致子項PASS，不是五輪或Diagram全條件。
- 原生圖已完成而UIA仍停01:05:20.8，重新截圖確認01:59:32.9／所有列車退出，再由raw核對15:53:15.4170057+08 completion。15:53:25.4426026合法10sIdle；15:53:57.4944928正常afterReset、clock0，15:54:11.5467287 Stop completedRuns1／activefalse。Route全程未開Diagram，actual cache原本即0，故不將afterReset0冒稱已建立非空圖表快取的清理PASS。
- beforePlay／10sIdle／afterReset privateMiB273.09／288.95／292.08；單輪不能證明bounded memory或無洩漏，未注入GCCollect。
- raw dispatcherStall15筆max1751.6236ms（occurrence9／sim4277.7／15:52:27.2190571+08，activeRoute）。WorkerAdvance max23.5191／ApplyPlaybackFrame32.3141／RouteMarkerRender10.4223／DispatcherApplyAge47.5796ms；recent play handler早於此gap約69.57秒結束，不支持該gap由已記錄play handler直接阻塞。沒有未覆蓋UI工作／GC／OS／compositor因果證據，流暢度FAIL仍保留。
- 兩檔正常Stop後非覆寫歸檔，metrics SHA256 `EA5D93E73984513B9B47014D068BBB130FEE30404CD91D2D34F1E4BF314F4EF4`、events `D8389C836567FDB65A67C8757F99CD0A4467D2C88C3AFEF13AA41B9F39301792`，來源／複本Equal=true；本輪只验收與MD紀錄，未改Engine／Schema／程式。

## 額度中斷後：三頁暫停與續播穩定性

- 前次額度中斷前的Speed短播已暫停330.0s、等待後切Diagram clock仍330.0s；未完成Diagram續播，不提前列完整三頁PASS。恢復時Windows窗口清單已無旧MRT process；僅單次重開同正式V4.0.3整合exe，新window20649186。開頭回報330秒是舊停點，已在重新觀察後更正，不當當前事實。
- computer-use更新26.930.61225，已讀完整skill及guidance／confirmations／api。讀檔遇geometry unavailable及cached element失效；重新觀察時sample14已載入，未重送。沒有用shell UIA／私有Engine入口繞過工具。
- 獨立短互動session `31283537-eee4-44d1-9ed6-4c269e123a9c`，stem `native-acceptance-20261006-120431850-31283537eee444d19ed64c269e123a9c`，WPF實測96DPI/interfaceScale1、正常30×。Route Play→Pause穩定203.6s，等待2.5s再讀仍203.6；切Speed clock不變，正常Resume→Pause穩定591.0s，等待2.5s仍591.0。
- Diagram由正常驗收選單共用Play/Pause入口Resume，直接applicationVisualUpdate Pause record activeWorkspace=7／Diagram、time1458.6s；確認已暫停後等待2.5s、正常切回動畫讀到1458.6s。三頁限定此binary／sample14／100% pause-wait-resume穩定性子項PASS；操作往返延遲及第一份過期UIA不算App輸入量測。
- 短session正常Stop completedRuns0／activefalse，非完整長程或五輪。raw dispatcherStall0；直接applicationVisualUpdate play70.7205ms，resume19.7595／30.2683，pause21.1572／20.4632／63.5195，nestedTab1.8517，tab56.996／3.7115ms。少量樣本與單次零gap不能否定既有FAIL；play／Diagram tab及Pause有>50ms，互動效能整體仍未過，不混surrogate／OS injection／compositor。
- 兩檔非覆寫歸檔metrics SHA256 `B7BAC580788B3B3F0187A550616A3878F8533060CA679CC36B2E760E3B9F0F2B`、events `419B68340AAD2FF534AA5A9267EE24C59BE8038D7B629DFCEA25A1418AE151A5`，來源／複本Equal=true。正式App保留Speed暫停1458.6s、sample14，沒有active量測。

## 新版 100% Diagram 同程序五輪

- 獨立 session `d70a3e60-0f19-4f31-8e32-6062f2a893b2`，stem `native-acceptance-20261006-121523100-d70a3e600f194f318e326062f2a893b2`；同 window20649186、正式整合版V4.0.3、sample14。原生 telemetry 96×96 DPI、interfaceScale1、windowBounds1728×1037 DIP。保持 Diagram 頁、60×、左右／上下縮放1、上下行／全部車輛、計畫／實際／事件點及終點時間ON、手動10分鐘刻度、事件列表展開。不重啟程序、不強制GC，不與舊125%五輪或Route單輪混算。
- run1 正常完整完成7173.3s，20:17:57+08已有10sIdle；20:18:23正常afterReset仍為workspace7。非空actual cache由8組清為0、actualProcessedCount0、polyline0／canvasChild1，planned cache保留8組。afterReset private240.70 MiB。這僅是第一輪清理證據，尚非bounded memory／五輪完成；run2已正常開始。
- run2完成7173.1s、20:21:02+08合法10sIdle，正常Reset後private263.21 MiB、actual cache0／canvasChild1。run3完成7173.3s、20:24:08+08合法10sIdle，再正常Reset後開始run4；目前同頁／未重啟／無Pause，raw dispatcherStall0。記憶體趨勢尚未收斂，不提前宣稱無洩漏或整體流暢度PASS。
- 五輪正常完整完成frame time7173.3／7173.1／7173.3／7172.9／7172.9s，各1025事件／37172軌跡／9147安全觀測／active0；主代理逐欄JSON比較各輪events與9c第一輪全部Equal=true。active倍率59.99693298～60.00276976、hasPause=false、boundaryDefined=true／exact=false（非final Engine tick timestamp）。限定throughput／事件一致子項PASS。
- 每輪有合法10sIdle後正常Reset，actual cache及processed count均清為0、planned cache保留8。子代理核對發現run3 afterReset仍有120個視覺元素及frame metadata，其他輪canvasChild1；不能把五輪都宣稱polyline0／canvasChild1，reset visual parity仍UNRESOLVED。第五輪afterReset寫入後20:30:14.846+08才sessionStopped completedRuns5／activefalse。raw dispatcherStall0只屬此session，不推翻Route既有gap FAIL。
- afterReset各輪private MiB240.70／263.21／266.02／266.38／270.53；working set353.12／375.72／378.44／379.68／384.18；managed heap83.02／91.61／94.01／77.94／99.98；Gen2累計13／15／17／20／21。private仍上升、managed非單調；未強制GC、無heap retention因果證據，memory classification維持UNRESOLVED，不宣稱leak或bounded。
- direct applicationVisualUpdate nearest-rank：play n5 p95/max80.1657ms、reset n5 64.6809ms；小樣本、非OS/compositor，仍超50ms建議門檻，互動效能整體未過。
- 原始兩檔非覆寫歸檔至artifacts/native-20261006、SHA256來源／複本Equal=true：metrics `EBAC85F8F747EBA34FA0E802C2BF0DFAC985AF6269FAA5E4257F9989C751482A`；events `15E63844665156FFE8CFEB69952D857E662E86D9A6A0F12A391F23DE1FF457F0`。程式未因本次驗收改動。

## 100% 窄矮視窗子項與新排版需求

- 使用者手動縮窗後獨立短session `71a5f935-08dc-4513-864c-91e5fc6764ea`，實測96DPI/interfaceScale1／800×520 DIP。未改DPI；正常60×短播後暫停1134.2s，運行圖／速度／Route切頁及捲動時clock保持00:18:54.2。
- 原生圖內垂直offset202.48、水平offset92，時間軸保持圖框底部，右端01:59:22.6可見；事件列表展開／收折可切換，收折後圖區變高。Route proxy在路線可見下緣、拖曳0→1556.1934477379095，速度頁沒有proxy。這是限定小視窗子項，不是完整resize／DPI或全部marker驗收。
- 途中偵測使用者手動操作，重新觀察後外層已捲動；未把被阻擋的scroll輸入冒稱成功。最初對不可見Animation tab的UIA click未換頁，外層捲到可見tab後正常座標點選才切換，保留工具限制。
- 使用者指出事件列表未收折時圖區過小、難以操作垂直滑桿，要求加大運行圖最小可視尺寸、上方控制區改可收折。此native usability問題已確認，後續僅Presentation修正；不因時間軸子項PASS掩蓋問題。
- 20:37:40+08正常Stop completedRuns0／activefalse，兩檔非覆寫SHA256一致歸檔：metrics `BDC7C4D4E23CB2219C08D0A0C3427761C7741A598C346CFD7542ACB0748F436D`、events `D9B5CB2C9A05AFD99059FFC5586E0D181D0C56EC35100C5F352B71FEDF0FCCD5`。不是完整run，正式App暫停保留sample14。
