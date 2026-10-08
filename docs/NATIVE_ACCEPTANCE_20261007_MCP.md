# 2026-10-07 原生驗收與 MCP 接續紀錄

## 使用者驗收範圍調整

- 使用者明確要求最新排版125%／150% DPI、大小與收折矩陣先不重驗，直接繼續後續項目。本輪將此項記為「使用者選擇暫不重驗」，保留先前版本的限定證據；不宣稱新版完整矩陣PASS，也不再以手動DPI切換阻擋其他驗收。
- 下一段優先：V4.1.0單一播放視窗、同程序／sample14／Diagram／正常60×五輪與10秒idle後reset，檢查completion／事件parity／private memory／retained cache。RPC操作及status的view更新干擾須分開標示，不取代原生輸入p95。
- 後續使用者再次明確調整：V4.1.0先只驗MCP是否正常，其他功能／效能不驗；日後才把原V4.0.3與MCP版V4.1.0合併。此前五輪規劃停止執行，已開始第2輪正常暫停並收尾保存；第1輪觀察僅保留，不作V4.1.0效能正式通過。此指示未授權當下Git合併／commit／發布。

## 新版單程序五輪開始

- 正常關閉已停止量測的V4.0.3舊視窗後，只啟動一次V4.1.0橋接App：PID31024／HWND2167158；list_windows確認唯一MRT視窗。sample14、Diagram、兩軸1／1、全部方向與車輛、計畫／實際／事件／終點勾選、刻度自動、事件表及操作區展開，條件固定。
- 使用正常原生選單啟用session7ca80edb-3fc3-4721-baa8-0dfe49d5c89b；raw stem native-acceptance-20261007-012131854-7ca80edb3fc34721baa80dfe49d5c89b，位於output/mcp-build/bin/MrtRouteSimulator.App/release/NativeAcceptanceLogs。
- MCP正常play60啟動第1輪。播放中不透過status強制更新畫面，只讀已有collector JSONL；沒有advance、forced GC、測試／建置負載。背景其他使用者App未停止，保留環境干擾界線。
- 五輪驗收尚在進行，不能提前列PASS。
- 第1輪正常完成7172.9s／1025events；原生collector於09:23:45+08已寫10sIdleAfterCompletion，之後才MCP正常reset／play第2輪。沒有省略idle等待。
- 使用者縮小範圍後，MCP正常pause於1197.9s／245events，隔次status時間未變；原生選單Stop collector於09:25:00+08確認sessionStopped completedRuns1／activefalse。第2輪中止，五輪計畫不再執行，沒有完整記憶體／效能結論。
- metrics/events皆非覆寫歸檔至artifacts/native-20261007，SHA256來源／複本一致；分別2F36719BE3EBE0684EF388D8FE12717B1497B0B3BC90D30AE98DF6FE7D994015、4D20A28AB6F59416285C5A855AB401EF44D9A2A13213118C73191F6BC9939675。第一次歸檔命令解析語法失敗，未執行複製；修正foreach輸出語法後歸檔成功。

## 版本與工具界線

- 使用者已完成縮小視窗；本次原生視窗 HWND 1642890，標題 V4.0.3，載入 sample14。擷取畫面 786×514 pixel；未以像素尺寸推論 OS DPI 或精確 DIP。
- 此視窗來自 `src/MrtRouteSimulator.App/bin/Release/net10.0-windows`，檔案版本 4.0.3.0，修改時間 2026-10-06T20:44:10+08:00。
- 已實際呼叫已連接的 MRT MCP `server_info`：版本 4.1.0.0、Schema 8、workspaceRoot `D:\AI\codex\mrt-v403-integration`。`Directory.Build.props` 目前亦為4.1.0；獨立 `output/mcp-build` App 為4.1.0.0。兩份 binary 不混用驗收。
- 已讀取 docs/MCP.md、桌面命令與 App bridge 實作。正常啟動的既有視窗沒有 bridge，MCP desktop_launch 會新增視窗；先保留使用者剛調好的視窗完成小視窗檢查，後續僅新增一個獨立MCP測試視窗。
- MCP 可減少切頁、播放、暫停、重設、事件分頁讀取的重複操作。橋接 status 也會更新 WPF view，因此不是完全無干擾的效能取樣。MCP/RPC 操作不等於原生滑鼠輸入延遲；不得用 advance 取代正式播放驗收。

## 小視窗運行圖：本次原生觀察

1. 正常切到運行圖，操作區與事件表預設展開。圖框保留最小高度，需使用外層上下捲動查看底部，未將圖區壓成零高度。
2. 拖曳外層滑桿到底：UIA外層offset527.48。圖內垂直／水平滑桿、固定時間軸、事件列表及底部狀態列都可見；事件表包含時間0的 FULL-O13 發車，底端可達。
3. 在图內正常滾輪：圖內垂直offset0→48，外層保持527.48；時間軸位置固定、標籤仍可見。
4. 收折事件表：表格消失，外層offset自動收斂到381.48，圖區與時間軸仍正常；沒有保留原表格空白區。
5. 回外層頂端offset0，收折上方操作區：篩選／縮放／匯出控制視覺上消失，圖區向上移動，操作區標題保留可再次展開。

截圖由 computer-use MCP 即時顯示；未另存截圖檔。UIA有時保留已收折的子項，因此收折結果以新擷取畫面核對，不只採樹狀清單。

## 小視窗後續檢查

- 兩區同時收折時外層底端offset239，圖區、兩個滑桿及時間軸可見。
- 操作區收折／事件表展開時，外層底端offset367.48，圖內vertical48；事件表完整底端可達，圖框沒有被壓縮成零高度。
- 拖曳圖內水平滑桿offset0→92，圖內垂直48及外層367.48均保持；時間軸跟隨水平捲動，右端01:59:22.6可見。
- 四種收折組合已完成有限原生畫面檢查；尚非精確DIP／DPI完整矩陣或長跑通過。

## 尚未完成

- 此次精確視窗DIP與OS DPI後續已由App量測確認，見下節。
- 後續已重展操作區：方向上下行皆顯示、車輛全部、計畫／實際／事件／終點勾選、刻度自動、兩軸1均保留；其他DPI及大小切換仍待接續。
- V4.1.0 MCP橋接功能已完成以下有限smoke；正式DPI／輸入延遲／長跑矩陣仍未完成。
- 先前長跑記憶體收斂與原生輸入延遲未解項保留，不因本次排版觀察改為PASS。

## MCP 桌面功能 smoke

- 僅呼叫desktop_launch一次，建立PID35932／HWND22220748；保留原V4.0.3小視窗，未重送load／play。status確認V4.1.0及正確integration根目錄。
- load sample14成功，generation9189f1f3-a9ac-4f92-92cf-a7931a6b7292、time0、eventCount1。切Diagram後正常play rate60成功；沒有使用advance。
- pause完成time517s、eventCount80、sequence258。後續隔次status仍517／80／258、isPlaying=false，確認暫停而不是命令尚未完成。
- events offset0 limit2：total80，首兩筆為FULL-O13於0秒、FULL-UP-01於30秒的發車；可讀取結構化資料，不需人工抄表。
- 在暫停狀態依序切speed（SimulationTabItem/view2）、route（view0）、diagram（DiagramTabItem）；全程時間保持517s。
- 將MCP視窗置前重新擷取，確認運行圖、實線／事件表與兩個收折標題實際存在。第一次背景截圖被舊視窗遮擋，不作為無遮擋視覺證據。
- 獨立zoom設定horizontal2／vertical1.5，回應符合；恢復1／1。此項僅參數／handler smoke，非精確視覺比例矩陣。
- reset正常回應時間0、isPlaying=false；事件回初始發車狀態。沒有以RPC耗時當作原生延遲或宣稱比較測速結論。

採用結論：後續重複切頁／播放控制／事件分頁優先考慮專用MCP；需要精確畫面或原生輸入量測仍使用computer-use及App既有驗收記錄，且與bridge更新畫面造成的觀測干擾分開。

## 精確小視窗量測接續

- 原V4.0.3正常UI啟用dc3fae1f-c5eb-4d78-b6d4-a8c54a2a4d05量測，正常30×播放再暫停；sessionStarted明確記錄96DPI、interfaceScale1、windowBoundsDip800×520。此前像素觀察不再作為DPI推論。
- 暫停worker確認484.5s，owner applicationVisualUpdate35.8449ms。這是單次樣本，且同時有另一MCP播放視窗，不能用作p95或排除先前卡頓。
- 原生排版觀察歸類96DPI／800×520有限子項；沒有完成125／150%矩陣或整體長跑。
- 正常標題列最大化再還原：最大化圖區與時間軸、事件列表完整可见；還原回原小視窗後控制正常換行，未把最大化圖框高度累積回小視窗。此項為畫面檢查，不宣稱額外實測DIP快照。
- 08:55:50+08正常停止、completedRuns0／activefalse；metrics非覆寫歸檔至artifacts/native-20261007，SHA256來源／歸檔一致：E086477324312279EBE5FC85B5D9B6175F3AF380A80C42A32531C3A6676BC419。

## MCP 正常完整播放第1輪

- 相同PID35932，reset後正常play60，沒有使用advance；完成時isComplete=true、isPlaying=false、7172.9s、1025events。這是功能完成證據，沒有原生效能collector，亦有另一視窗短播放干擾，不當作無干擾pacing／記憶體正式結果。
- events以offset0／500／1000、limit500分頁讀取，回傳500／500／25，共1025筆；保留全部欄位在artifacts/native-20261007/mcp-35932-full-run-events.json。
- 對照V4.0.3 d70a3e60第1輪1025筆完整事件，僅正規化JSON欄位大小寫及列舉字串／數字表示（EventType與Direction），其餘欄位全比較；0筆不一致。這是事件資料parity，非版本整體行為完全等價。
- MCP事件檔SHA256：FB769F44E58F1C39D07510D6090D6446B6BC62DECB7C046296B7657154BFC241。
- 完成資料保存後，使用標題列正常關閉獨立MCP測試視窗；再次list_windows確認HWND22220748消失，process35932亦已結束。原小視窗1642890仍存在。
