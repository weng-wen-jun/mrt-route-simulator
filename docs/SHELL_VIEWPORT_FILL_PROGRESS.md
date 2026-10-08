# 最大化上下白邊修正（2026-10-02）

- 使用者切到125%後回報滿版畫面上下沒貼邊。原生唯一整合版視窗2098152、sample14、時鐘0；UIA標題列為「還原」，確認確實是最大化，不是90%預設一般視窗。原生畫面在產品標題上方及狀態列下方各有白邊。
- 原因定位：ShellContentGrid明確高度，由ShellScrollViewer.SizeChanged讀取ViewportHeight更新，但當下viewport尚未完成更新，可能保留舊高度；內容短於viewport時出現居中空白。
- 修正限WPF：ShellScrollViewer加Top內容對齊；ScrollChanged只處理自身ViewportHeightChange，重新同步ShellContentGrid到max(720, settled viewport)。保留小視窗720最小內容高度、外層上下捲動、底部固定Route水平捲軸；不改Engine/Schema/90%初始開啟政策。
- 已正常Alt+F4關閉，重建首次成功但WPF runner短暫檔鎖出現1個MSB3026重試警告；待測試整合後重建確認無警告。
- regression／完整Engine／WPF／修正後125%原生復驗進行中。整體DPI驗收尚未完成；不commit/push/release。

## 首輪結果及125%啟動追加

- 最終填滿修正build0 warnings/errors，Engine198/198、完整WPF PASS；新增1100×900／maximize／restore回歸及800×520既有捲動均PASS。新增測試未取得修正前FAIL（共享production已修），不宣稱有red-green證據。
- 第一版修正原生視窗2098008，正常量測session `08dd046b-e7c6-47f4-9a4e-40524e3f91f0`，實際120 DPI/1.25。最大化畫面產品標題接至原生標題列下方、狀態列接至固定底部水平捲軸，上下居中白邊消失。此時是預設示範資料，不冒充sample14全部驗收。
- 同時發現125%初始比例判斷false、WPF自動尺寸1440×752.8而非90%，導致正常視窗可能右緣超出工作區。使用者追加要求「預設不要頂到畫面邊緣」，維持90%開啟政策並修正：只把HWND建立前的呼叫者Width/Height設定視為explicit，避免WPF跨DPI自動回填尺寸被誤認為使用者指定。
- 追加修正build0 warnings/errors、focused outer-shell（含default90%與明確800×520保持、large-fill）PASS；最後binary完整WPF與125%原生再驗進行中。

## 最終結果

- 最後binary完整WPF exit0 PASS（含新large-window fill regression），Engine本輪198/198 PASS；build0 warnings/errors、diff check PASS。
- 最終125%原生視窗726048、session `a91ad6ba-d202-4cf3-945a-3002dc812c6f`，120 DPI/1.25、interfaceScale1；所在DISPLAY1工作區DIP=(1536,-99.2,1536,912)。正常預設DIP=(1612.8,-53.6,1382.4,820.8)，proportional=true，正好90%置中；左右各76.8 DIP、上下各45.6 DIP留白，四邊均在工作區內。
- 最終binary原生「最大化」→「還原」確認：最大化時標題緊貼原生標題列下方，狀態列下方接固定水平捲軸，无上下居中白邊；還原回90%一般視窗。工作列仍保留，不改無邊框全螢幕。
- 已停止量測，App保留一般90%視窗、預設示範資料尚未建立模擬。本次只清除125%預設啟動/最大化白邊問題，不代表sample14完整125%DPI驗收完成。
- 原始兩session紀錄位於Release/NativeAcceptanceLogs（123259029及123648184），保留首版125%啟動比例false與最終true證據；後续可歸檔，不混成同一session。
