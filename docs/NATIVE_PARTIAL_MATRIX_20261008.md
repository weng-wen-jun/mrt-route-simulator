# 部分完成項目續驗（2026-10-08）

## 範圍

- 依使用者指示只處理「已做部分、尚未完整完成」項目：大幅 resize／連續拖曳、其餘互動組合、剩餘 PNG／PDF 矩陣，最後才處理 test-only 局部 profiler。
- 最新 125%／150% 排版矩陣依指示略過；不展開 stall、記憶體或首次延遲根因調查。
- 使用隔離 4.1.0 `output/diagram-draw-phases-20261007`，不覆寫原 4.0.3，不合併、提交或發布。既有 Engine 198/198 與完整 WPF PASS 不冒充原生操作 PASS。
- 原生操作僅使用 Computer Use；不使用 Alt+Space、私有 advance 或 forced GC。

## 逐項進度

1. 大幅寬／高 resize、連續拖曳：純寬1382.4→996 DIP、純高820.8→569.6 DIP與標題列滑鼠拖曳移動120×40 DIP，限定完成後重排／可達性PASS。連續拉邊框的逐幀即時重繪仍未完整，不能以外框大小模式或圖表thumb拖曳替代。
2. 剩餘互動組合：已補雙收折、圖內水平／垂直拖曳與兩軸獨立縮放的限定功能組合；全量 Cartesian 與逐幀效能未驗。
3. 剩餘 PNG／PDF：本輪補9個PDF組合，逐頁視覺 PASS（單頁紙本字級限制保留）；高解析PNG標籤互蓋已有最小修正、完整回歸與限定原生重匯出PASS。舊FAIL圖與binary保留，不冒稱全部矩陣或舊4.0.3已修復。
4. 專用同資料 first／warm test-only profiler：已實作並通過同資料量測；Release build／Engine198/198／完整WPF通過。已處理既有sticky fixture未受控高度問題，不代表原生延遲問題已修復。

## 本輪操作

- 開始前原生視窗清單無 MRT 視窗；只啟動一次指定隔離執行檔。
- 正常讀取 sample14、60×播放，完成並自動停止於 01:59:32.9；UI 明確顯示 37,172 軌跡／9,147 安全觀測。此輪未從播放開始採集，不能列完整事件 parity／五輪證據。
- session `07aaffd64de1430eb838bed6d6653879` 在播放中才建立，僅供後續 input／尺寸紀錄；原始 stem `native-acceptance-20261008-002409464-07aaffd64de1430eb838bed6d6653879`。
- 最大化／還原後完整 Diagram 可刷新；還原畫面擷取 1150×637（這是 screenshot 尺寸，非原始 DIP 寬高／DPI）。需 raw metadata 才能量化實際尺寸。
- 右側拖曳命中 Shell 上下捲軸，使 outer offset 到 403.33，並非 resize；不得列連續 resize PASS。系統大小命令已退出，沒有使用 Alt+Space。
- 同一完整資料、事件列表先收折再上方控制區收折，outer offset 隨重新配置成 257.33／177.33；圖、固定時間軸及兩個收折標題均可見，列限定功能 PASS。
- 控制／事件皆收折，圖內垂直 scroll offset 0→48、outer offset 維持177.33；時間軸仍在圖下緣，完整底端曲線可見。列此捲動／收折組合 PASS，不冒稱全 DPI 矩陣或逐幀 compositor latency PASS。
- 展開 controls、事件仍收折：時間軸 1→2，里程軸保持1；再里程軸1→2，時間軸保持2。UIA值及截圖一致，限定獨立缩放功能 PASS。
- H2/V2 連續拖曳水平 thumb：offset 0→1002，固定軸顯示右半段 00:59:46.3～01:59:32.5；再拖垂直 thumb：offset48→463，水平仍1002、兩軸仍2、outer仍257.33。曲線下端完整、固定時間軸保持可見。這是圖表捲軸拖曳 PASS，不是視窗 resize 拖曳 PASS，不作逐幀延遲結論。

## 高解析 PNG：有新視覺 FAIL

- 組合：sample14 完整資料、僅下行／all vehicle、planned／actual on、events off、endtime off、人工10分、H2/V2、高解析 on。圖內捲至右下端後匯出，PNG 仍輸出完整範圍，並非只截目前 viewport。
- 檔案 `artifacts/native-20261008/png-highres-down-h2v2-events-off-end-off-10min.png`，4068×1520；SHA256 `6BCF60CFEBA74A976377AE0ED71C938000AE927A6035C8FB5C29C2003FF4CDE6`。
- 原尺寸人工檢視：下行線、事件點關閉、10分刻度、額外終點時間關閉均符合；但左下角 FULL-O13／FULL-O04 的文字疊在一起。**整體視覺 FAIL，修正／回歸尚未做**，不可只憑輸出成功列 PASS。
- 工具限制：modal UIA 索引無法輸入、parent Return 曾開出第二個 PNG 儲存框。用各 modal 自身 screenshotId／自身座標取消額外框，再點原框存檔成功。之後禁止以 parent Return 送出原生檔案對話框；不把此操作失誤歸因 App 效能。

## PDF 補驗

- A4 split／僅下行／all vehicle／planned+actual／events off／endtime off／10分／H2V2：原生正常匯出 `pdf-a4-split-down-h2v2-events-off-end-off-10min.pdf`，463690 bytes、2頁、每頁842×595 pt。SHA256 `8BEAE3FA9C6E5F14C8379F2CF64E15CD4EEA7429247570F5E9FF98860D81E195`。
- 使用 PDF skill 的逐頁 render 檢查，120 DPI 兩頁完整檢視；無多餘狹窄尾頁，兩頁标题／station labels／時間 caption 完整；10分刻度、事件隱藏與終點時間關閉符合；第一頁近起點的 planned+actual 列車標籤分列並有 leader，沒有 PNG 的文字重疊。限定此組合 PASS，不代表單頁／A3／short-tail 尚缺組合通過。
- A4 single／同一組 filters/H2V2：原生匯出 `pdf-a4-single-down-h2v2-events-off-end-off-10min.pdf`，369053 bytes、1頁842×595 pt；SHA256 `A536E615AA46161843DF0CA1B9FE48DFE931132CEC8FCBBBF17B2AC141D99B7E`。150 DPI完整 render檢視，列車標籤分列、時間 caption／10分刻度未截斷、額外endtime未顯示；fit-to-page 會縮小整圖及文字，限定配置／文字完整子項 PASS，不承諾實際紙張閱讀尺寸。
- A3 single／同一組 filters/H2V2：原生匯出 `pdf-a3-single-down-h2v2-events-off-end-off-10min.pdf`，369053 bytes、1頁1191×842 pt；SHA256 `4A42B7F4AE1860F8C3E144967AD0EC1210B7FD15707EA709EC1291C3BD11F52B`。100 DPI完整 render 已檢視，標籤分列、caption 完整、額外終點時間關閉符合；限定配置／文字完整 PASS，fit-to-page 的紙本可讀性不在此結論內。
- A3 split／同一組 filters/H2V2：`pdf-a3-split-down-h2v2-events-off-end-off-10min.pdf`，460464 bytes、2頁1191×842 pt；SHA256 `5038E5BF5E1E82EDEC2F7977C6077C2AE0846F9A608A743AD2672DC580709812`。100 DPI逐頁完整檢視：未多出窄尾頁，兩頁 caption／標題／車站文字完整，列車標籤分列，額外終點時間未顯示，限定此組合 PASS。
- A3 split 短尾：上下行皆顯示、0～110.2分、人工10分、endtime on、events off、H2V2。畫面終點顯示01:50:12.0（是篩選终點，不是實際完成時間）。`pdf-a3-split-both-h2v2-events-off-shorttail-110p2.pdf`，496351 bytes、2頁A3；SHA256 `7A3E97CDE112FACA17DD99FFAB913957CB37131620E35D9577988935311D66ED`。100 DPI兩頁完整檢視：無額外12秒窄頁；末頁110分與110分12秒時間分兩行，不互蓋；右端曲線、caption完整。限定此短尾分頁組合 PASS。
- A3 single 短尾／同一篩選：`pdf-a3-single-both-h2v2-events-off-shorttail-110p2.pdf`，413773 bytes、1頁A3；SHA256 `AAB8588EEA29E27B76CB2E623B95B773DDDC578B179B5F6A1705EEF560F372DF`。150 DPI完整 render檢視：終點兩行分列、caption與右端曲線完整、標籤未互蓋。限定配置完整 PASS；單頁縮字紙本閱讀限制同前。
- A4 single 短尾／同一篩選：`pdf-a4-single-both-h2v2-events-off-shorttail-110p2.pdf`，413771 bytes、1頁A4；SHA256 `ECF6F3E9F721D28E2853B8FF3493E6EFCB2FCAD3E98A194C0E7B3627C5A110A3`。150 DPI完整 render檢視：終點兩行、caption、右端曲線完整，未見文字互蓋。限定配置完整 PASS；單頁縮字紙本閱讀限制同前。
- A3 aspect-fit single 短尾：同一上下行110.2分篩選、H1/V4、PDF分頁on。`pdf-a3-aspect-single-both-h1v4-events-off-shorttail-110p2.pdf`，389107 bytes、1頁A3；SHA256 `B6D629B05FD9EE446A0C6A54292B38D23350157ECE3B2A0B466563D71FFF5798`。100 DPI完整render檢視：高縱向比例分支只輸出1頁、兩行終點時間／caption／車站／全高曲線完整；限定此比例／短尾組合 PASS。
- A3 aspect-fit single endtime off：110.2分範圍、H1/V4、PDF分頁on，`pdf-a3-aspect-single-both-h1v4-events-off-end-off.pdf`，381119 bytes、1頁A3；SHA256 `9F5FE6E022A8F76BD2922B0B13D773D3203D5AD29EE2AB7CC1014F5CFE0172F3`。100 DPI完整檢視，額外01:50:12時間未顯示、固定110分仍保留，caption／全高曲線完整；限定此比例／開關組合 PASS。

## 原生紀錄封存

- 08:54:27+08正常 Stop；raw `sessionStopped` 明確 `active=false`、`completedRuns=0`。本輪mid-play才arm，因此沒有完整run，不能補全程parity／memory／五輪門檻。
- raw有165筆inputAction（含同action多phase，不等於165次操作）。sessionStarted原始viewport：120 DPI／1.25、interfaceScale1、1162.4×643.2 DIP。此是session起始尺寸，不冒稱最大化／還原後尺寸；125%／150%完整矩陣仍依指示略過。
- JSONL與events JSONL非覆寫封存至`artifacts/native-20261008`；report SHA256 `6ED0845D8ED17750B608A863A85CE9411B848EC1FA3EE294C72A0120E90B24E1`。末筆inputAction simulationTimeSeconds=7172.9，篩選／匯出沒有重新推進world。
- 下段同資料局部profiler僅test-only；開始前停止native採集，不並行native效能驗收與build/tests。
- Stop後正常關閉本輪隔離視窗；第二次原生視窗清單確認MRT為空。原4.0.3Release exe SHA256 `66BC6230C149622A646DF2F8B924BF111EF43BCD3CD21FF177D9E9153840AFA0`、DLL `DA598917F2F5B6501C63989141FEC5A28DE36177920622E8B99048F6E07D3422`，本輪再次唯讀核對一致。

## 同資料局部 profiler（進行中）

- 新增test-only `InteractiveTimeDistanceProfileTests.cs`與Program opt-in `--profile-interactive-timedistance-only`。主代理完整讀source，補actual cache在測量外預備、diagram generation實際欄位核對與非空資料／曲線gate；production未改。
- 範圍是單一600s frame、同cache資料首次visual建立與3次強制layout重建；direct draw scope不是owner input／OS／compositor、不是process或JIT cold，不forced GC。
- 新隔離`output/interactive-first-warm-20261008`還原成功。首次build因測試檔缺System.IO引用及WPF Shapes.Path命名衝突，0警告／3錯誤；補IO與Path alias後重建中，未把失敗列PASS。
- 引用修正後build0警告／0錯誤。首輪runner同viewport gate FAIL；增加指紋診斷原命令第二輪仍FAIL，資料／clock相同但cold canvas885寬、warm870寬。原因已在fixture定位：Diagram隱藏且viewer未arrange，renderer合法fallback到canvas.ActualWidth再扣15，每次改寬。測試端明確900×420 arrange viewer／固定兩捲軸可見（不先draw），保留完全相等assert並新增viewer viewport／兩軸縮放指紋；production不改。重建0警告／0錯誤，固定viewport第三輪執行中。
- 第三輪同資料profile exit0，cold／3warm的frame digest、cache版本／全部display sample digest／endpoints、filters、viewport完全相等，5個timing每次count=1；prepared viewport883×403、canvas868×420、96DPI、axes1、all vehicle／雙向、planned+actual+events+endtime on／auto tick。資料3742actual／178992planned／97events。初次visual總46.2821ms（static14.9987／series26.6725／events2.9038），3warm總9.2135／8.6481／11.8220ms。這不是native input timing，不能洗掉cold gate歷史FAIL，也不歸因JIT／GC。
- 主代理再補frame reference、cache reference及量測前後cache指紋相同，新增interfaceScale欄位；最終build0警告／0錯誤，最終profile與完整回歸待執行／完成。保留三份先前失敗／成功log，不覆寫。
- 最終加強版sample14 profile exit0；冷／3暖各5個timing count=1、相同frame reference／cache reference與量測前後全部cache指紋。600s／3742actual／178992planned／97events、96DPI／interface1、viewer900×420／viewport883×403／canvas868×420。冷總51.8112ms（static17.6530／series28.8722／events3.2163），暖總9.4007／12.6660／8.4238ms、series1.8455／2.5233／1.7576ms。僅測試工具有效，不用局部總時間判原生50ms gate。
- 整段allocated cold6246648、warm4599624／4606944／4639288 bytes；GC cold0/0/0、warm1/0/0、0/0/0、0/0/0。含draw後dispatcher排空的程序測段，不是各phase allocation；不可由此歸因首次延遲／JIT／GC或宣稱長期memory bounded。資料指紋hash在測段外計算，可能影響後續自然GC，未forced GC。

## 回歸檢查點

- 新隔離build 0警告／0錯誤，Engine完整runner exit0／198/198。`git diff --check` exit0（既有CRLF提示，不是失敗）。
- 新App DLL SHA256 `2C59DE5D42843FB6DC4C92E283557E3BB333992008A2F328AC30A24E51E38392`，WPF test DLL `EB9668DBA8BB7D5044979F8B0435510A5190C7B72F9E02906A549FDDF60D2279`；最終profile log `7D7C1DC03366586F1185934E40280F7A557BB15373A09305BA64D6FAA47E1A87`。以上不是先前原生匯出使用的App DLL `27D7D8E6AE2B6ABA057E198667B3C70FF44541A040F880916538F580482D6414`，不交叉冒稱binary驗收。
- 完整default WPF exit1，在`TimeDistanceStickyAxisTests.cs:59`失敗：收折events後viewer高度未增加，expanded=314.0／collapsed=314.0。raw `output/interactive-first-warm-20261008/wpf-full.log`保留；不列完整WPF PASS，尚待區分fixture條件／實際排版問題。本輪opt-in profile不在default分支執行。
- 對照前一隔離binary `--diagram-lazy-only`也FAIL314→314；新binary `--diagram-compact-only`三scale全PASS。source顯示compact會移除外層extra extent，保留圖框最小高度，不保證圖本身變高。只修正sticky-axis測試fixture：給ShellScrollViewer固定1100邏輯高度，保留expanded+20 assert；compact四組可達性仍由獨立test覆蓋。沒有改production排版或Windows DPI。
- fixture修正後Release0警告／錯誤、局部lazy／sticky PASS、完整default WPF exit0／PASS（`wpf-full-fixture-final.log`）；前次失敗raw全部保留。此是受控WPF回歸，並非native resize／125%矩陣PASS。
- 最終sticky受控fixture實測shell1100.0、expanded427.9→collapsed573.9，原增加20 gate通過。完整WPF log SHA256 `CEDA36CF7661311A2A04753B09D7097E458F1E4451E8CC12A8106BCC19847C7F`，Engine log `37BE6520ACF123474F680981C61AE218C9264238E0083FAA6F639D5837825DFC`；fixture版WPF DLL `5ECB8777DD3804F34194EF7C79ED5DDEE02951D82C5185E0909A9E97BE88F739`（比先前profile DLL多了fixture修正，不混淆雜湊）。
- 下一單項開始PNG匯出label overlay修正：重用既有PDF單頁防互蓋／leader overlay，來源軌跡與座標不移動；加入1x／2x兩標籤分列像素回歸及source不變gate。新隔離`png-label-overlay-20261008`首次no-restore build因新目錄尚無assets而0警告／7 NETSDK1004，正在先restore，不列PASS。
- 新PNG隔離restore後Release0警告／0錯誤；`--synchronous-export-only` exit0。新增fixture先證明舊direct Render的第二文字列缺字，再驗1x／2x實際PNG兩個車次名分列像素都有保留、尺寸不變、來源位置／visibility／Polyline points不變。2x輸出圖已完整檢視，FULL-O13／FULL-O04分開並以leader回指原anchor。這是合成WPF回歸，不撤銷sample14原生FAIL；完整Engine／WPF與原生重匯出尚待。舊失敗PNG未覆寫。
- 1x fixture輸出也完整檢視，兩名稱分列。PNG隔離App DLL `BBBACAD09F616DAE79631E1194BE4D9091FA68CF622BD707D79790911602D242`、WPF DLL `65E10943271984CA8E955724D117073DA1F80A05C2711E6F01B7ED317B378E9F`；此才是新匯出修正binary。source只新增ExportPng的共用overlay，不把該檔其餘既有dirty修改算成此輪變更。
- PNG隔離完整Engine exit0／198/198、完整default WPF exit0／PASS，含新像素回歸與既有PDF。Engine log SHA256 `37BE6520ACF123474F680981C61AE218C9264238E0083FAA6F639D5837825DFC`，PNG單項log `A96F9F13C8595B8FAA39F175A982A652E2BED45A8540DC95900923E716D3A03A`。接續原生重匯出，仍不可先列native修復PASS。

## PNG修正版原生重驗（進行中）

- 完整WPF结束後原生清單確認無MRT，僅正常launch一次PNG隔離exe。新window855992，正常Open對話框讀sample14，60×正常Play，未用MCP／私有advance或forced GC。
- 設定僅下行／all vehicle／planned+actual on／events off／endtime off／H2V2／manual10／highres on。等待正常完整停止後再匯出新檔，舊FAIL圖不覆寫；不宣稱此是完整collector session、效能或DPI矩陣。
- 額度99%檢查點：原生正常自動停止於01:59:32.9，UI 37,172trajectory／9,147safety。回到Diagram，H2V2／manual10仍保留。PNG尚未按匯出，沒有新modal、沒有新檔，不列native修復PASS。新隔離視窗855992保持開啟、已停止，便於下輪接續；不要再launch第二個App。
- PNG隔離完整WPF log SHA256 `E8F13BCBD6A2078981A84F6F2C1A1CE6F9C5AB1670D1B090281C235D15968159`。原4.0.3exe／DLL再次核對與前述雜湊一致。未commit／push／merge／release。

## 下輪接續順序

1. PNG修正版限定原生重匯出已完成，證據見下段；不要重做或覆寫舊FAIL圖。
2. 純寬／純高大幅resize與標題列拖曳的限定功能已完成，證據見末段；尚待連續拉邊框的即時重繪觀察。未具體指定的全量Cartesian組合不當作無限待辦，不重跑最新125%／150%略過矩陣。
3. 同資料first/warm test-only工具已完成，原生cold latency／Reset／stall／memory未知根因不屬本輪剩餘範圍，不因本輪量測或匯出修正宣稱已解決。

## 額度重置後PNG原生重匯出完成

- 接續時fresh UI顯示V軸已變為1.6224719101123573，未假定舊狀態仍相同；正常set回2並fresh核對H2/V2，activation後截圖確認僅下行／all vehicle／planned+actual on／events off／endtime off／manual10／highres on。現有sample14完整停止資料保持，未再launch或advance。
- 新原生PNG `png-overlay-fixed-down-h2v2-events-off-end-off-10min.png`，4948×1520、600407 bytes，SHA256 `24F62B4B44B593C925C141705057AA11C5B30E008E7E49CCEF9B15CB4F2D67DF`。舊FAIL的4068×1520未覆寫；本輪視窗寬度不同，不能以像素diff或完全相同viewport作結論。
- 全图檢視及900×270原尺寸細節檢視（`png-overlay-fixed-label-detail.png`，源x140/y1250）確認FULL-O13與FULL-O04互相分列、兩份planned/actual名稱均保留並有leader回指anchor；10分鐘刻度／事件點關閉／額外終點關閉、完整曲線範圍符合。**限定此新binary／組合標籤互蓋修復原生PASS**，不代表PNG所有組合或互動renderer文字都已修復。
- 下一步純寬／純高大幅resize與連續視窗拖曳；沿用這個完整停止資料視窗，仍不重跑最新125%／150%排版矩陣。

## 大幅純寬resize（限定原生功能PASS）

- 原生opt-in session `831ca8cc-575b-4ec7-a863-94492305d9bc`起始raw：120DPI／1.25、interface1、window Left1612.8／Top-53.6／Width1382.4／Height820.8 DIP。此是實際WPF telemetry，並非由1370×815截圖猜DPI。
- 透過正常系統「大小」→Right選邊、原生pointer移到x990/y408形成縮小outline→Return完成（沒有Alt+Space）。後續session `a54b809c-f13e-42d1-86ea-dab82dfc0eb9`起始raw確認同Left／Top／Height與DPI，Width996 DIP；純寬减少386.4 DIP，非max/restore冒稱純resize。
- 窄窗controls重新wrap，H2/V2仍保留、full frame7172.9s不變。圖內offset175.69581749049428／H0保持；外層scroll0→48→265.73，最後完整事件表底端、固定時間軸與水平thumb皆可見。限定此寬度前後重排／可達性PASS，不代表逐幀連續resize或全DPI gate。
- 第一session正常Stop activefalse／completedRuns0。單次WM_ENTERSIZEMOVE→EXITSIZEMOVE收messages1；gesture span46,693ms含工具等待／觀察，不能當App latency。EXITSIZEMOVE後WPF surrogate約24.6ms，不是compositor present。沒有新完整播放或memory結論。

## 純高與視窗滑鼠拖曳續驗（額度重置後）

- 純高使用正常系統大小／底邊指標移動／Return完成。session `6f70f33a-04fb-4ea8-8e8e-b6c9e2764ffa`起始raw核對120DPI／interface1，Left1612.8／Top-53.6／Width996／Height569.6 DIP；比前session高度820.8減少251.2 DIP，寬度／位置／DPI不變。縮矮後outer417.24捲到底，固定時間軸、水平thumb與展開的事件列表底端完整可達；再拖outer thumb回0，上方選單／controls可達。限定此完成後功能PASS，非逐幀即時重繪PASS。
- 真正滑鼠drag標題列，window-relative (400,14)→(520,54)。後續session `461c6057-5f64-4029-a234-dd5621e36d6b`起始raw為Left1732.8／Top-13.6／Width996／Height569.6 DIP，同DPI／interface；位移+120／+40 DIP且尺寸不變。raw WM_ENTERSIZEMOVE→EXITSIZEMOVE只有messages1，約20.7ms message span、21.2021ms surrogate，不是連續多幀或compositor證据。限定標題列移動／完成後畫面正常PASS，clock始終7172.9s。
- 真實右邊框嘗試 (981,220)→(850,220) 未形成可驗證的resize，截圖寬度與配置不變，不列PASS。先前大小模式只見outline，不能替代連續即時內容重繪；不更改Windows全域拖曳顯示設定、不增production instrumentation、不反覆盲點邊框。此單項仍需使用者可見的實際拉邊框／即時重繪觀察；其餘已有明確名稱的本輪部分完成項目已補到上述限定證據。
- 四session均正常manual Stop，active=false／completedRuns=0；8份report／events已非覆寫封存至`artifacts/native-20261008`且source/archive hash一致。events均空，因未正常Play，不能當完整run、事件parity或memory證明。
- report SHA256：831ca8cc `F3641999BDCEB10C1DFC49F3F2AD34E03817D16F659877E58CCCF341ED0029E4`；a54b809c `4525F312A395469812FFF6E6D2B85D6CADA2291E7984E516A79BE11BEF607164`；6f70f33a `7BD568BE383184F2EEF1B59C060B43202E31FAFA6201AE7AB5ED3A0F39FEE4AA`；461c6057 `6E226981F87649ED3F56193691C14BB21BFE3962D3B9E26A7CE2A55651EBA164`。
