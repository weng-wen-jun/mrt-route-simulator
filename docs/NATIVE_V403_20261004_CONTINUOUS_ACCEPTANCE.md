# V4.0.3 原生桌面續驗（2026-10-04）

## 範圍與現況

- 使用者已開啟 sample14；僅沿用正式 Release App window6752400，不啟動 WPF runner／其他主視窗。
- 工作樹：`D:\AI\codex\mrt-v403-integration`。本輪不修改 Engine、Schema 或 sample。
- 原生 session：`8987a2a3-ead2-4260-9d26-7248199d79d9`；來源位於正式 App `NativeAcceptanceLogs/native-acceptance-20261004-023031367-8987a2a3ead242609d267248199d79d9.jsonl`。
- sessionStarted 實測 DPI120（125%）、interfaceScale1、window1382.4×820.8 DIP、DISPLAY1。截圖像素大小不作 DPI 依據。
- sample14 未播放時，人工10分鐘刻度顯示00:00至01:50及額外終點01:59:22.6。
- 未播放直接按匯出 PNG：App 顯示「請先播放 V2 模擬，產生軌跡後再匯出運行圖」，沒有產生檔案；屬現行前置條件，不列匯出成功。

## 本輪待驗

- 同一 process／sample／Diagram 頁面／固定 DPI 的五次完整60×播放與 reset／idle記憶體 checkpoint。
- 完整播放後的原生 PNG／PDF：人工刻度、終點開關及短尾間隔；實際匯出、render、視覺檢查。
- 輸入與 resize 邊界：WPF receipt／application visual update 與 dispatcher surrogate 分開，不冒稱 OS／compositor 延遲。
- 最新版各 DPI／尺寸矩陣仍未全部完成；整體 NOT COMPLETED。

## 證據限制

- 不沿用舊版本長時間測試作最新版五次 gate。
- 不強制 GC，不於播放期間執行重型建置／測試，不將人工操作時間當作純 resize latency。
- 一般匯出檔暫存於 `output/pdf/native-20261004`；不覆蓋舊證據。

## 追加：五輪完成與原生匯出失敗

- 同一正式 process 五輪已完成，session因maxRuns5自動停止並flush；沒有pause、換頁或resize。詳細五輪量測分析待整合。
- run1：7172.9s／active wall119.5474482s，60.000444242×；事件1025與既有基準逐筆canonical JSON mismatch0；trajectory37172／safety9147／active0；dispatcher>100ms gap0。這不是OS click／compositor量測。
- 原始metrics及events備份到 `artifacts/native-20261004`，之後建置不依賴bin內唯一證據。
- 正常原生按鈕匯出 `output/pdf/native-20261004/diagram-endtime-on-10min.png`：1254×380檔案可寫入，實際檢視卻全白，FAIL。
- 原生PDF同名 `.pdf`：pdfinfo顯示3頁、A4橫向842×595pt、35828bytes；Poppler render第3頁只有外加坐標軸，沒有標籤／軌跡，FAIL；其餘頁仍需檢視，不宣稱所有頁已核對。
- 警告列新增「關閉警告」已由子代理實作，主代理檢查只呼叫既有HideValidation；目前尚未建置，不冒稱現有window已更新。
- 空白匯出根因正在診斷；保留失敗檔，不覆蓋成修復後PASS證據。

## 五輪量測摘要（修正警告按鈕前的同一Release process）

| 輪次 | 模擬秒 | active wall秒 | 倍率 | 本輪>100ms dispatcher gap | 最大gap ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 7172.9 | 119.5474482 | 60.000444242 | 0 | — |
| 2 | 7173.0 | 119.5498799 | 60.000060276 | 0 | — |
| 3 | 7172.9 | 119.5489917 | 59.999669575 | 2 | 435.44 |
| 4 | 7172.9 | 119.5495421 | 59.999393339 | 6 | 1087.94 |
| 5 | 7173.0 | 119.5485164 | 60.000744601 | 1 | 364.24 |

- 子代理唯讀分析後主代理核對raw runCompleted與9筆dispatcherStall。runCompleted的stallCount是session累計0/0/2/8/9，不可直接當本輪值。
- 五輪均無pause，1025events／37172trajectory／9147safety／active0；事件逐筆canonical JSON比對既有20813及201efe基準均mismatch0。
- 89筆含viewport records：120DPI、1382.4×820.8DIP、同一位置與DISPLAY1均不變。不含viewport的records不作尺寸推論。
- 60×吞吐gate PASS；流暢度gate NOT PASS，9次gap原因未證實，不直接歸因GC、resize或系統。
- completion private MiB依序241.79／245.93／250.58／262.17／265.39；managed heap78.14／72.49／82.18／77.80／81.93。前四輪reset actual cache與points清零、history null、canvas1child；第5輪未reset。不能宣稱strict bounded或無洩漏。
- 首次cold運行圖tab：WPF receipt到handler3.8378ms、handler50.6357ms、owner visual update54.6144ms；Loaded surrogate175.4443ms。不是OS click或compositor latency，也不足以當輸入p95 gate。
- metrics SHA256：`6FC238A4349F2E13C226D74D6028C00D425E00DEE3A1F092CF3EA9FC13C750A1`；events：`B7EB3A78C6D229364D92BD4140D7E15C38800C10793E457D1DAC933A004FBBAE`。
- sessionStopped raw reason為manual-stop，completedRuns5／activefalse；程式maxRuns路徑會呼叫同一Stop API，不將reason字串當成人工操作證據。

## 修正進度

- PDF三頁均已Poppler render檢視：均沒有圖形／文字，僅ComposePdfPage額外坐標軸；原生匯出內容FAIL已確定。
- source核對：DrawTimeDistanceDiagramFull清空並重建children後，原生click handler立即Render；既有OutputTests卻在同一步後額外UpdateLayout，且僅驗格式／檔案長度。此差異正在以同步像素回歸重現，不把VisualBrush offset或DPI猜測當根因。
- 已正常關閉window6752400，確認無同App視窗，並Release建置警告按鈕：0警告／錯誤；`--validation-warning-only` exit0、未Show視窗，diff --check通過。
- 正在補不Show視窗的同步匯出回歸；暫不執行會連續彈窗的完整WPF runner。原始五輪是警告修正前的exe，不將其冒稱修正版五輪。

## 同步空白匯出修正

- 新增no-show同步回歸：已arranged Canvas清空／重新加入紅圖形與文字後直接匯出，不補測試端UpdateLayout；修正前exit1，因PNG缺紅色像素，成功重現空白。
- DiagramExportService.Render先element.UpdateLayout，再讀尺寸及VisualBrush；修正後同一回歸exit0。一般／2倍高解析、offset、PDF1.6倍同源Render、hidden text恢復均通過。
- 主代理修正新測試缺System.IO與Path／Shapes.Path命名衝突後建置，未放寬像素assert。首個build失敗後曾誤跑舊binary、落入一般runner並因錯誤root退出，不將該次列PASS；之後確定新binary及專用分支再做red/green驗證。
- 最終Release build0警告／錯誤；`--validation-warning-only`與`--synchronous-export-only` exit0；既有`--output-only`（完整小型／TrainCenter尾軌、3600秒、CSV/PNG/PDF）exit0。
- 修正後已透過Computer Use正常啟動一次正式App，原生PNG／PDF與警告按鈕正在續驗，尚不列native PASS。

## 修正版原生警告與單輪

- 正式新版window856274，使用者介入正常讀取同一sample14後主代理續驗；不把讀檔操作冒稱全程代理自動完成。
- t0正常按匯出PNG觸發警告：原生「關閉警告」可見，點擊後警告列消失，project path不變；再次觸發與再次關閉通過。一次安全審核因先前讀檔遮擋資訊擋下操作，先刷新截圖證明讀檔完成／無modal再續操作，不繞過審核。
- 修正版session `50913053-7c88-4c39-bc4a-dd06239aeb0e`；source stem `native-acceptance-20261004-030618738-509130537c884c39bc4add06239aeb0e`。
- session實測120DPI／interfaceScale1、1534.4×910.4DIP、DISPLAY1；與前五輪1382.4×820.8不同，另作單輪／匯出驗證。
- Engine完整runner198/198、0失敗；Engine未修改。完整WPF未重跑，不拿focused gate當完整runnerPASS。

## 警告關閉交付與匯出 checkpoint

- 「關閉警告」位於橘色ValidationBorder右側；只清空／隱藏目前警告，不停用下一次驗證、不修改Engine／Schema／目前專案。
- 原生兩次觸發／關閉已通過；專用回歸亦涵蓋多行換行、automation name、新警告再次出現。diff --check再次通過。
- 修正版原生PNG `output/pdf/native-20261004/fixed-endtime-on-10min.png` 已實際檢視：不是空白，有軌跡、事件點、車站標籤、10分鐘刻度與01:59:32.5图表終點。此終點仍是軌跡端點，不冒稱01:59:32.9退出營運時刻。
- PDF儲存操作未完成，曾出現兩個owned匯出對話框；未把檔案不存在當PASS。已依新鮮截圖逐一取消，返回原生運行圖，未覆蓋舊失敗證據。PDF逐頁內容與其他剩餘矩陣仍待續。

## 額度接續點

- 使用者通知額度快沒了後，不再開新驗收項目；取消新開但未儲存的PDF對話框，保留正式App與已載入sample14。
- 本次要求「關閉警告」已實作、Release與專用回歸、原生操作驗證通過。完整WPF未重跑，整體驗收仍未完成。
- 下次先完成修正版原生PDF儲存／逐頁render，並歸檔分析50913053 session；再做終點off、短尾刻度及剩餘DPI／尺寸／效能矩陣。不得把舊五輪當成修正版五輪，也不得把吞吐PASS當成流暢度或記憶體bounded PASS。

## 額度更新後續驗：PDF內容與分頁

- 同一正式App／sample14完成原生PDF儲存 `output/pdf/native-20261004/fixed-endtime-on-10min.pdf`；使用新鮮截圖按儲存，不以Return送到owner視窗。App顯示PDF已匯出，pdfinfo：4頁、842×595pt、446372bytes。
- Poppler逐頁render至 `tmp/pdfs/native-20261004/fixed-endtime-on-1.png`～`-4.png` 並全部檢視：空白問題已消失，前三頁有軌跡與文字；第3頁含01:59:32.5終點。
- 新發現PDF分頁FAIL：第4頁是狹窄尾片段，標題／時間被截；第2／3頁起點時間標籤亦被頁界裁切。不能把「圖形已出現」擴大成PDF整體PASS。正在唯讀診斷分頁計算，保留本輪檔案不覆蓋。
- 原生終點off PNG `fixed-endtime-off-10min.png` 已儲存並實際檢視：額外01:59:32.5標籤消失、00:00～01:50的10分鐘刻度及軌跡保留；終點開關PNG子項PASS。PDF未沿用此PNG結果作PASS。
- 12秒短尾：原生結束分鐘110.2、10分鐘刻度、終點on，畫面與 `fixed-short-tail-110p2min.png` 的01:50:00／01:50:12均分行、不重疊，PNG子項PASS。保留full內容來源與篩選端點的語意界線。
- 已向使用者提出最小PDF分頁／文字配置修正的範圍確認；未收到答覆前不改新發現的問題，先驗其他項目。
- 使用者隨後明確允許此最小修正；限定匯出層與測試，Engine／Schema不改。
- 另建不播放session `3880f376-5203-4364-b458-007cfded738a`，實測120DPI／interfaceScale1，完成sample14事件收折及最大化／還原，固定時間軸可见。事件列表收折後圖表增高，功能子項PASS；single routed input Loaded surrogate69.6155ms，非owner／OS／compositor量測，不列p95 gate PASS。此session completedRuns0，不混入播放五輪。

## 修正版單輪raw分析與歸檔

- 50913053單輪7172.9s，acknowledged active wall119.5474593s／60.00043867097057×，conservative119.5706654s／59.98879387351808×，無pause；events1025／trajectory37172／safety9147／active0。
- 本輪11筆dispatcher gap，最大1116.9064ms、median208.9540ms；原因未證實。吞吐子項PASS但流暢度仍NOT PASS。
- completion private283.3359MiB、managed74.1514MiB；10s idle private285.4844MiB、managed92.7286MiB；沒有afterReset／nextPlay，不列reset或五輪bounded PASS。allocatedBytes為累計配置量，不是存活heap。
- 子代理完整canonical事件比對0 mismatch；主代理另以整份1025事件JSON比對前五輪，五輪均FullEventsEqual=true，並核對11個gap／max1116.9064ms。
- 17筆viewport固定120DPI／1534.4×910.4DIP，與舊五輪尺寸不同，不能合併當同條件五輪。
- raw metrics/events已拷貝到artifacts/native-20261004並主代理核對兩份Equal=true。SHA256分別 `9E4DAA295D9910534A5A7E60484C9C9D8B00BA04BC8E13F496729A080470FDB9`、`1C3F5B3D138A8DDC79E4373C44924C69C5745B5BE8CE3F9752D715E7100CE197`。

## 使用者允許的PDF最小修正

- 主代理審查子代理新增no-show PdfPaginationRegressionTests：1406×380／1.6倍重現4頁窄尾（RED exit1）；只掛專用與完整runner，不Show視窗。
- DiagramExportService：頁數按完整graph span／slice capacity取最少頁數，再等距分配完整切片；station左軸身分先於time row判定，保留plotBottom以下站名；time label頁界內縮4DIP。未修改Engine／Schema。
- Release0警告／錯誤；修正後PDF專用回歸GREEN exit0，檢查3可用頁、下方站名、邊界刻度及graph-only無黑色文字。同步匯出、警告關閉、既有output-only各exit0。
- 舊正式視窗正常關閉並確認無App視窗；修正後只啟動一個正式App進行原生重驗。完整WPF仍未重跑，不冒稱全runnerPASS。
- 新版本window594328透過原生讀取存檔正常載入sample14；Engine完整runner198/198、0失敗，diff --check通過。
- 新session `9d6b7f9a-4962-4dc0-a124-238eea46cdee`：120DPI／interfaceScale1／1382.4×820.8DIP，DISPLAY1；正常60×播放，Route頁200%路線縮放，與舊Diagram五輪不同條件，另列native匯出前置單輪。
- 本次啟動initialWindowFit實測dpiScale1.25／workArea1536×912DIP，正常視窗1382.4×820.8（工作區90%），Left1612.8／Top-53.6；四邊留白76.8／45.6DIP，125%預設比例／不貼邊子項有新版本原生證據。不將旧96DPI啟動metadata混入。

## PDF首刻度重驗與第二次最小修正

- 第一修正版原生 `pagination-fixed-endtime-on-10min.pdf` 已逐頁render／檢視：3頁且窄尾消失，下方O03～O01保留；但00:00首刻度被誤當站名重複在各頁並重疊，仍FAIL。
- 改以文字中心判斷左軸身分，避免置中首刻度的左緣被誤判。新增藍色首刻度fixture，驗證第一頁時間軸保留、站名軸不混入、後續頁不重複，graph-only亦不含藍色文字。
- 將判定暫還原舊版後，新測試RED exit1（首刻度誤画站名軸）；恢復修正後Release0警告／錯誤，PDF專用GREEN exit0。同步匯出、警告關閉與output-only亦exit0；完整WPF仍待，不以專用結果代替全runner。
- 正常關閉舊App並核對沒有殘留視窗；只開一個新正式window922618原生重驗。沒有更動Engine／Schema／原始模擬資料。
- 9d6b session完成7172.9s／active wall119.5473817s／60.000477618155976×，1025events／37172trajectory／9147safety／active0，與50913053整份events相同。20個>100ms gap，最大639.7635ms，不能列流暢度PASS；completion private230.796875MiB，10s idle232.49609375MiB，無reset，不列bounded PASS。
- 9d6b metrics／events歸檔SHA256與來源Equal=true，分別 `4B0F49215C4F8049758B64B879D435D65BE567C42C2206945491891964D2D105`／`E309F3E9E301700BE9D0057D885BF451B5E9146D487A52EAF9834130B57AAE02`。
- 第二修正版window922618原生完整60×播放完成01:59:32.9，軌跡37172／安全9147；此播放未啟用session，不宣稱新的raw效能量測。Engine再次198/198。
- 原生 `pagination-fixed2-endtime-on-10min.pdf`：3頁A4／410006bytes，全部render檢視，首刻度重複消失；但頁界刻度內縮後與鄰近刻度擠壓，仍未列PDF整體PASS。已補export-only多列時間標籤配置及版面高度擴充，保留軌跡幾何；待建置／測試及第三次原生重驗。
- 尚可見原有車次標籤在相近起點重疊，不能以時間標籤修好就宣稱整張PDF零重疊。
- 第三修正Release0警告／錯誤，PDF專用通過（新增相近洋紅刻度像素須出现在第二列、Rect防碰撞／非碰撞不移列）；同步匯出／警告關閉／output-only均exit0。新正式window726074僅一個；正在重驗。另派唯讀子代理檢查export／regression邊界，未授權其修改。
- 第三修正版原生 `pagination-fixed3-endtime-on-10min.pdf` 3頁／410006bytes，逐頁檢視仍擠壓；hash與第二版相同，因相鄰框僅約0.125DIP間距，尚未碰撞。新增繪字實際像素測試後發現VisualBrush預設會將ink拉伸成完整留白文字框，舊版RED exit1。
- 第四修正明確指定VisualBrush Absolute Viewbox（包含parent offset與ActualWidth/Height）保留原字形比例，加4DIP水平間距；含offset20,4的fixture修正後GREEN。Release0警告／錯誤，準備第四次原生重驗；不因前三次匯出成功而列整體PDF PASS。
- 第四版window3937280正常60×完成，clock01:59:32.9／trajectory37172／safety9147；最大化、manual10／終點on原生 `pagination-fixed4-endtime-on-10min.pdf` 3頁A4／376722bytes，逐頁render檢視。窄尾、首刻度重複、刻度拉伸與頁界刻度互擠已消失，碰撞時間另起一列且完整保留；分頁／時間軸修正子項PASS。仍有原有車次起點名稱相互／圖例重疊，整體PDF視覺尚未列PASS。
- 第四版同一視窗終點off原生 `pagination-fixed4-endtime-off-10min.pdf` 3頁／375491bytes，全部render檢視：額外01:59:32.5消失，固定10分鐘及軌跡保留；終點off PDF子項PASS。
- 同一視窗結束分鐘110.2／終點on原生 `pagination-fixed4-short-tail-110p2min.pdf` 3頁／374910bytes，全部render檢視：01:50:00.0／01:50:12.0完整分行，caption另列且留白足夠；短尾PDF子項PASS。此終點是篩選端點，不是最後退出營運時間。

## PDF站名引導線第五次最小修正

- 唯讀複核確認分頁graph從82DIP起裁切，原有站名引導線端點不超過78DIP，文字重建未補線，移位站名失去原刻度映射；第四版時間軸子項通過不代表此映射通過。
- 新增紅色 tagged leader 像素回歸，各頁必須保留原AnchorY=338到移位站名的線；舊正式DLL測試RED exit1（第一頁漏線）。
- 僅DiagramExportService蒐集TimeDistanceStationLeaderTag線，依原X/Y、Stroke及Thickness每頁重畫；不改站名座標、里程刻度、軌跡、Engine或Schema。
- 第五修正Release0警告／錯誤、PDF專用GREEN exit0、diff --check通過。原正式視窗正常關閉並確認不存在後，只啟動新版window7473842；正常讀取sample14、60倍播放，準備原生逐頁重驗。此播放未開量測，不宣稱新raw效能數值。
- 第五版正式window7473842完成01:59:32.9，37172trajectory／9147safety；原生 `pagination-fixed5-endtime-on-10min.pdf` 3頁A4／396738bytes，三頁全部render並檢視，左側移位站名引導線均恢復，時間刻度、無窄尾子項仍正常。原有車次名称彼此／圖例重疊尚存，整體PDF仍非PASS。
- 第五版完整Engine198/198、同步匯出／警告關閉／output-only各exit0（子代理執行、主代理核對回報與flags）；未執行完整WPF，未以專用測試冒充原生畫面。

## 第五修正版同頁五輪量測（進行中）

- session `c3a6a1d6-59a8-4a5a-b54f-94860d8845c7`；正式window7473842、sample14、Diagram頁、60倍、manual10、終點on、事件列表展開。透過原生驗收選單呼叫正常播放／重設，不切換結果頁。
- raw記錄120DPI／interfaceScale1；畫面最大化，metadata的windowBoundsDip仍為1382.4×820.8，暫不以此欄宣稱最大化實際client尺寸，待量測程式欄位核對。
- 第一輪完成7172.9s，37172trajectory／9147safety／1025events；完成後10秒閒置紀錄已到，接著正常重設及第二輪播放。量測期間未執行建置／重測。
- 第一輪已見>100ms dispatcher gap，不列流暢度PASS，不歸因GC。五輪事件／倍率／memory比較及原始紀錄SHA256歸檔尚待。
- 五輪已完成，無pause，activeWall各約119.548s；active倍率59.999714～60.000184，conservative倍率59.978783～59.988098；每輪1025events。完整逐筆parity另由唯讀子代理交叉核對中。
- completion私有MiB依序271.4492／265.8750／279.8711／292.2617／289.8242。超標dispatcher間隔累計22／40／60／88／127；共有127筆，不列流暢度PASS，未證實原因。前四輪已正常重設，第五輪10秒閒置後session自動結束，缺第五afterReset，仍不列strict五reset／無洩漏PASS。
- 原始metrics／events已複製至artifacts/native-20261004，主代理核對來源／歸檔SHA256 Equal=true，分別 `8E81944CCA0B64D47F485391E2255E262A6E91D46FB48D0CAD16AD031EB20BEA`／`9BE86C21D69FBA44C01979D709AD7156C97E6EDFC443DB1467C36093DC00EB20`。sessionStopped completedRuns5／activefalse；reason欄共用manual-stop字串，不據此冒稱使用者手動停止。
- viewport來源核對MainWindow.NativeAcceptance.cs：windowBoundsDip只讀Left/Top/Width/Height，非ActualWidth/ActualHeight；workAreaDip為SystemParameters主螢幕欄位，與DISPLAY1 monitor原始pixels分開。最大化實際client尺寸驗收不能只依該欄位。
- 主代理整份event-array JSON比對五輪與50913053均FullEventsEqual=true；子代理以每事件23欄含陣列再核對，各輪eventMismatch0。主代理以stopwatch區間計數gap22／18／20／28／39，max1400.8419／376.4431／815.0941／500.6823／1820.6542ms，與子代理UTC區間結果一致。
- idle privateMiB261.7031／264.4648／281.8125／289.5195／291.0703；idle actualProcessed均37172、plannedProcessed178992、canvas592。前四afterReset actualProcessed0，history可能null或T0/S0/E1，canvas1或120（空畫布／新初始畫面發布邊界不同）。不能把累計allocatedBytes當存活heap。

## PDF車次文字第六次最小修正（2026-10-04續驗）

- 上次額度中止在已修改source、尚未建置新production階段；本次額度更新後，先以舊正式DLL跑新增Cyan像素fixture，RED exit1重現車次文字進入圖例區域。
- export-only依正式full Canvas中Polyline後緊接TextBlock的原始首點配對，避免猜+3/-15。重疊分頁以首點歸屬最早涵蓋頁，不再依文字中心導致下一頁孤立重複。
- PDF頁內車次名限制於plot範圍、避開其他車次文字；需要移位時畫細線回原首點。極密配置保留全部名字到延伸annotation列並擴充高度，不移動graph bitmap／軌跡／Engine資料。非分頁輸出仍走既有原圖路徑，尚不宣稱其文字避讓已修好。
- 新Release0警告／錯誤，PDF專用GREEN exit0，包含圖例避讓、同點車次Rect不重疊、510DIP首點頁界不孤立重複、時間軸／站名引導線／字形比例既有回歸。尚待原生第六版逐頁檢視及唯讀複核；未列整體PDF PASS。
- 第六版正式window8524878完整播放停止，畫面clock01:59:33.0／37172trajectory／9147safety；本輪未arm session，不以UI多0.1秒觀察推論raw events／Engine變更。
- 原生 `pagination-fixed6-endtime-on-10min.pdf` 3頁A4／405217bytes，全部render／檢視：車次名字彼此／圖例分開，leader回原首點，站名leader與時間軸正常、無窄尾。sample14 A4分頁終點on文字配置子項PASS。計畫／實際同名標籤保留分列，非新增Vehicle；尚不宣稱非分頁或所有圖表零視覺缺陷。
- 唯讀複核目前split overlay正式配對、anchor單頁owner、密集overflow與字形比例可接受；指出同色fixture不足以證明個別標籤保留，已改Cyan/Lime/DarkOrange逐一assert、獨立dense Rect fallback。強化測試起初將red leader AA邊緣誤認orange而FAIL；render確認標籤在owner且未漏字，補G-B色差辨識後GREEN，未更改production來掩蓋測試。
- 最新Engine198/198、同步匯出／警告關閉／output-only各exit0，完整WPF仍未重跑；強化tests-only建置0警告／錯誤。
- 同一正式第六版window8524878：原生 `pagination-fixed6-endtime-off-10min.pdf` 3頁A4／404223bytes，全部render檢視，額外終點消失、fixed10與軌跡／避讓保留，終點off子項PASS。
- 結束分鐘110.2／終點on原生 `pagination-fixed6-short-tail-110p2min.pdf` 3頁A4／403557bytes，三頁全檢視，01:50:00／01:50:12分行完整、caption另列、車次名字／站名leader正常，12秒短尾子項PASS。篩選端點仍不是Engine完成clock。
- 委派既有唯讀複核子代理實作單頁分支的窄修正，僅DiagramExportService／PdfPaginationRegressionTests，保留原始graph幾何、不改PNG／MainWindow／Engine；主代理另驗A3原生，待整合source diff／build／native，不冒稱已完成單頁。
- 第六版原生 A3 `pagination-fixed6-A3-endtime-on-10min.pdf`：pdfinfo確認1191×842pt／3頁／400003bytes，全部render檢視。無窄尾、時間文字及列車名稱避讓子項正常；第2／3頁長標題右端仍裁切，整體PDF不列PASS。儲存對話框標題寫死A4，但實際A3輸出正確，標題不一致另記待辦。
- 第六版未分頁原生對照 `single-page-fixed6-before-A3.pdf`：1頁A3／271613bytes，render檢視確認頂部車次名進入圖例、底部同點車次名重疊；終點與時間caption亦擠壓。此為單頁修正前證據，保留不覆蓋；子代理正在補單頁train overlay，時間caption問題仍另列未通過。

## PDF單頁車次第七次最小修正

- 子代理新增單頁與split=true但aspect適合單頁的像素fixture，舊正式DLL先RED（Cyan車次名在圖例）；實作僅隱藏可見train TextBlock、Render其餘原圖、finally還原，再以原首點／非train文字occupancy重建標籤。非chart無anchor仍回傳原bitmap，PNG／Engine／Schema不變。
- 主代理已讀取source及完整新增測試並diff --check；正常關閉舊正式視窗、兩次list確認不存在後，Release build0警告／錯誤、PDF專用GREEN exit0。既有分頁、字形／時間軸／station leader回歸亦通過。正在以單一新正式App原生重驗，完整WPF未重跑，未宣稱所有單頁文字問題已修好。
- 第七版完整Engine198/198／0失敗，同步匯出、警告關閉、output-only各exit0（既有子代理重跑最新binary、主代理核對）；未執行完整WPF。正式window332384讀取sample14完成、正常60倍播放中；初次click遇geometry unavailable，刷新同一returned window並activate後成功，未重開程式或重送讀檔。
- A3長header疑點更正：唯讀子代理核對1600×1132三張原render，深色header最右x1532／1555／1555，右側至少44px（放寬AA門檻仍37px）；主代理重看可见末尾「固定時間步進0.1秒」與「安全／障礙」，無裁切證據。之前疑似裁切不維持FAIL，A3分頁文字子項PASS；仍不擴大為全PDF矩陣。
- 第七版正常完整播放已結束，單頁A4 `single-page-fixed7-A4.pdf`：1頁842×595pt／280536bytes，render檢視train名字分開／不進圖例、leader回原首點子項PASS；原終點caption仍擠壓。此輪未arm session，不列新的raw效能。已委派第八窄修正單頁caption避讓，先RED再實作，主代理保持原生第七版作對照。
- 第七版單頁A3 `single-page-fixed7-A3.pdf`：1頁1191×842pt／280538bytes，render交叉確認train避讓及原graph／station leader保留；caption問題與A4一致。另委派低成本唯讀子代理檢查五輪127筆gap與現有timing紀錄的可觀察關聯，禁止改collector或推斷GC因果。
- 原生PDF SHA256：第六A3分頁 `DD0AF6703697166E38F0811AF73055973E3EEF89809D23226A1DA467C1E15B64`；第六單頁A3對照 `49E4077C79D2507C95F4166B147BEBCBC6ED0FFB0ED87FBC9392FA06F09AAE28`；第七單頁A4 `5407D688B530A0D0ACB64388D122EA81DFB8FC64EA2AF08774760F956FC95D04`；第七單頁A3 `CA78B4B52BA555F643DAC0FF27EE0D508BA9533544AB8E50D2CB1DD19C742AA6`。

## PDF單頁caption第八次窄修正（整合中）

- 新終點tick／dense annotation／caption fixture先以第七DLL RED（caption未向下分列）；noncollision fixture由主代理指出train與caption仍碰撞後，子代理改用真正分離位置。Source仅單頁export overlay暫隱train與caption／finally還原，tick、station、graph不移動；caption依所有其他可見TextBlock及已配置train Rect避讓。
- 主代理正常關閉第七版並核對視窗不存在、讀取新增source/tests、diff --check，Release0警告／錯誤。首次新版PDF專用仍FAIL：caption不得覆蓋終點車次／刻度。尚未啟動第八版原生App、不列GREEN；已委派核對像素bbox是否將紅色caption的AA邊緣誤認Orange，須以診斷證據判定，不能只放寬assert。
- 診斷確認fixture Orange predicate缺G-B>50，把紅色caption AA邊緣併入train bbox：source caption(1045,194,29,15)、final(1045,272,29,15)，誤分類trainA bbox高69；tick原／final同(961,196,66,11)，bitmap高288→297。補既有色差辨識、不弱化不相交assert，移除診斷輸出；tests-only0警告／錯誤、子代理PDF GREEN，主代理亦重跑GREEN exit0。production無再改動；noncollision原Y／tick不移位／dense增高／Visibility還原均通過。
- 第八正式window8065014只啟動一個，sample14正常讀取後60倍播放中；latest Engine198/198與三項no-show WPF各exit0，完整WPF未跑。
- 第八版正常完整播放已結束（未arm session）；原生單頁A4 `single-page-fixed8-A4.pdf` 1頁842×595pt／280535bytes，render完整檢視，終點01:59:32.5與時間caption分開，train避讓／station leader／原graph保留，sample14單頁文字子項PASS；A3及aspect-single原生分支待補，不列整體完成。

## 五輪Dispatcher gap唯讀關聯檢查

- 主代理重讀raw最大10筆elapsedMilliseconds，與子代理排序一致：occurrence106/12/1/47/92/14/9/72/95/55，最大1820.6542ms。起初誤用不存在的gapMilliseconds排序，已更正按raw elapsedMilliseconds，未据錯誤排序下結論。
- diagnostics source確認sample reservoir容量4096，p50/p95為滾動樣本，max/count/total累積、不於每10秒checkpoint清零；不能把累積max當作該時間窗獨立耗時。
- 子代理對最大10gap鄰近checkpoint：已量測ApplyPlaybackFrame／TimeDistanceRender／WorkerAdvance max多為數十ms，未見足以直接解释0.4～1.8s gap的scope證據；cache/visual增長只是狀態關聯。DispatcherApplyAge為frame發布到UI消費，不是probe gap。GC僅collection累積值，無pause／ETW因果，不能認定或排除GC。
- 根因仍UNRESOLVED；raw沒有每次refresh/scope timestamp，下一步可唯讀對齊gap stopwatch區間、frame publication/observation與input handler timestamp，再決定是否需要额外opt-in量測。不修改collector／Engine、不列真正OS click或compositor latency PASS。

## 第八版原生 A3 與 aspect-single 補驗

- 同一正式 window8065014、sample14、完整播放後未開啟額外量測。單頁 A3 `single-page-fixed8-A3.pdf`：1頁1191×842pt／270976bytes，2400px render全頁檢視，車次名避讓／原首點引導線／站名引導線正常，終點01:59:32.5與「時間」分列；此案例文字子項PASS，不外推全部PDF矩陣。
- 上下縮放400%、左右100%、紙張A3、PDF分頁勾選的 `aspect-single-fixed8-A3-vertical400.pdf`：仍為1頁1191×842pt／448821bytes，已render完整檢視；全里程、固定10分鐘與終點時間、文字避讓均保留，aspect適合單頁分支子項PASS。列車資料未改、此輪未量測效能。
- 原生A4／A3／aspect-single SHA256依序：`158FA11351FE16875AC5942B060CB9C15C4CD5741FCA3C714EC4B2E62E26E4BE`／`B8C658825828AFE415E88357FC4926BA51A7BCEF3BDE6CB27E35F87FC464CC34`／`D06C3ABE6486C62A0AF969D06C95285CA042534E55F93DE7DE41CDF91344FB78`。舊失敗對照未覆蓋。
- 匯出對話框初次cached element unavailable，重新觀察後改在已選取檔名欄輸入、直接按存檔；未用Return重送匯出或重啟App。
- 第八版Release與PDF專用GREEN、Engine198/198、同步匯出／警告／output-only均exit0；完整WPF、流暢度根因與strict第五reset仍待，整體NOT COMPLETED。
- 同一window還原上下100%後補最新A3分頁 `pagination-fixed8-A3-endtime-on-10min.pdf`：3頁1191×842pt／411088bytes，三頁全部render檢視，無狹窄尾頁、頁界時間完整、車次與圖例避讓、站名引導線保留；本案例分頁文字子項PASS。SHA256 `744C8B3A27896A137F9B026C4B59DEA29D34A9788771A70C92C54F1C8516EC8A`；保留左右／上下100%、手動10分鐘、終點on、A3分頁on畫面。

## Gap timestamp 續查

- 唯讀子代理將最大10筆gap與inputAction／checkpoint原始stopwatch timestamp對齊；主代理另讀source欄位，逐筆重算handler區間overlap，10筆皆0。不表示沒有未量測工作，只表示沒有與已記錄handler區間重疊。
- 最大occurrence106：1820.6542ms，checkpoint frame18260在gap結束前約25ms才發布，publish→observation24.6915ms；occurrence47：815.0941ms，frame10898在結束前約5ms發布，publish→observation5.1335ms。兩筆不是整個gap的frame apply耗時證據；其他8筆無checkpoint frame落在gap內。
- 無per-frame publication stream、無逐次ApplyPlaybackFrame／TimeDistanceRender／WorkerAdvance scope開始結束timestamp、無GC pause／ETW／compositor-present證據。累積scope aggregate不能判定單次gap因果，根因維持UNRESOLVED。

## 第五輪 reset 量測生命週期修正（進行中）

- 主代理與唯讀子代理核對source：第五輪10sIdle分支立即Stop，Stop core清除session，之後正常Reset通知無法寫入afterReset。既有native-acceptance-only exit0，但短測試未涵蓋此順序。
- 依原任务允許的opt-in diagnostics範圍，委派僅MainWindow.NativeAcceptance.cs／NativeAcceptanceTests.cs窄修正：第五idle保活到正常afterReset再收尾、最多5 measured runs；早Reset不得以reset後frame/cache冒充完整10sIdle，缺checkpoint需如實保留。
- 先測試RED再source實作，主代理另關正式App、Release／GREEN整合；尚未建置或原生重跑新collector，不能宣稱strict五輪reset／memory protocol已完成。
- WPF執行模式更正：先前「三項no-show」措辭不準確。Program.cs中只有validation-warning-only／synchronous-export-only／pdf-pagination-only早return且無Show；output-only、native-acceptance-only等會先跑VerifyWorkspaceSelectionEventBoundary，其PlaybackProfileTests.cs有window.Show。主代理已核對source，這些是focused、非完整runner，不應稱完全不顯示視窗。
- 完整WPF無全域hide/offscreen旗標，部分測試依序Show並finally正常Close，輸入editor部分移到螢幕外但仍建立native Window。未為避免彈窗而改弱測試或將focused冒稱full；目前完整gate仍待。
- 主代理正常關閉正式App、兩次list確認不存在；新增fixture舊DLL RED於「第五輪10s idle應保留collector」，之後兩檔source實作。主代理Release建置0警告／錯誤，最新版native-acceptance-only兩次GREEN exit0，涵蓋idle→真worker Reset→afterReset→單次Stop ordering、nextPlay不arm第六measured run、第四輪early Reset不偽造idle且可續Play。
- 首次新版focused執行先在既有VerifyResumeBaseline缺runResumed失敗，尚未到新增fixture；未放寬assert或改production掩蓋，原命令重跑通過，再核對最終tests檔案時間／DLL及重跑通過。固定短等待可能存在非決定性，保留此失敗，不宣稱完全穩定。
- 此為反射合成completedRuns／idle due的有界自動化測試，並非真原生第五完整輪；第五early Reset／pending時Close或I/O failure尚未新增專屬組合案例。新版PDF專用亦GREEN；使用者通知額度快沒了，停止啟動新長輪，下一次先重做同process／同頁五次完整60×且每輪等10s idle再正常Reset。
- 收尾完整Engine198/198、0失敗、exit0；diff --check exit0。正式App維持已正常關閉，未啟動新長輪或full WPF。最新執行檔在整合工作樹Release目錄，collector新binary已自動化驗證但尚未原生重跑。下次接續優先：新collector五輪完整idle/reset→完整WPF（有依序顯示測試視窗）→未完成DPI／互動矩陣與gap根因；整體仍NOT COMPLETED。
