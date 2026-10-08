# V4.0.3 原生驗收續行（2026-10-02）

## 最新結果（2026-10-02）

- 最新續驗：150%最小視窗捲動/摘要/速度圖、最大化還原與暫停列車左右鍵命中通過；28站小視窗逐段標籤掃描及O13 native1×越行目視完成。input小樣本首次切運行圖191.9254ms，尚不能通過完整p95 gate。O04完整目視、O20、100/125 DPI與其他矩陣仍待；詳見[本輪續驗](NATIVE_V403_COMPACT_MATRIX_PROGRESS.md)，下列舊待驗描述以此更新為準。

- 外層垂直捲動、標題可捲走、Route固定水平捲軸及所在monitor工作區90%開啟已實作；Release／Engine198/198／完整WPF PASS。
- 150%原生1152×676.667 DIP預設尺寸、外層／Route／Speed捲動本輪通過。詳見 [布局紀錄](OUTER_SHELL_WINDOW_PROGRESS.md)。
- 同程序／sample14／TimeDistance五輪60×complete與事件repeat通過，memory分類LIKELY BOUNDED WITH FRAMEWORK RESERVE，不再沿用先前UNRESOLVED。詳見 [五輪紀錄](NATIVE_V403_FIVE_RUNS_PROGRESS.md)。
- overall仍NOT COMPLETED：100/125 DPI、全resize／hit-test、全站標籤掃描、O04/O13/O20慢速目視、完整input p95仍待補。本輪未改Engine，不commit／push／tag／Release。

## 先前階段（歷史記錄）

- 使用者確認看到介面縮放入口後，要求繼續完成驗收；恢復原生驗收操作。
- Computer Use list_apps 實際確認唯一 MRT 視窗 id397278，執行路徑為整合工作樹 Release exe，標題 V4.0.3。不沿用前日視窗 handle。
- 目前 UI 的 CurrentProjectFileTextBlock 顯示原目錄 `mrt-route-simulator/samples/大型機場線-完整營運示範範例.mrtsim.json`；clock0.0s，尚未播放。這不是整合版 sample14，不能混用事件秒數／營運證據。
- 透過正常檔案選單開啟讀檔對話框。工具 set_value 回報 `element 317 is not available in cached app state`。重新取得 window/state 並嘗試座標聚焦及 Alt+n 後，accessibility focused_element 仍回報搜尋欄位，而不是檔案名稱欄位；未輸入路徑、未按開啟、未開始播放。請使用者手動完成讀檔，避免未知焦點輸入。
- 指定案例：`D:/AI/codex/mrt-v403-integration/samples/14-大型-二十八站完整營運範例.mrtsim.json`。

## 下一步與判定邊界

### 150%重新驗收（2026-10-02，18:42後）

- 新視窗id267158，整合版V4.0.3、sample14來源確認。session a08d66ae1dc549959f48df86386f86df，正常30x播放後Pause，clock356.5s保持。
- 原生JSONL確認DPI144×144／1.5×1.5、interfaceScale1；window919.333×524.667 DIP。monitor非主DISPLAY1，不能以capture大小猜測DPI。
- Speed短視窗新增垂直捲軸可操作；UIA vertical value0→48→96→144，末畫面可見底部时间軸／站名，clock不變。原先底部完全不可達的問題在此尺寸未重現，但plot viewport極矮，不能標整體小視窗PASS，也尚未涵蓋摘要展開／全部尺寸。
- 三個application visual callback單次延遲：Play74.3571ms、Pause23.8833ms、nestedTab1.5122ms；不是compositor present／自動化roundtrip；無足夠samples宣稱p95。未完成60x sanity、五輪memory、全28labels或O04/O13/O20目視。
- 正常選單停止量測並flush，App仍paused356.5s。raw留在exe/NativeAcceptanceLogs/native-acceptance-20261002-104230937-a08d66ae1dc549959f48df86386f86df.jsonl（約50590bytes）；不改production／tests。
- 使用者追加布局需求：最外層需垂直捲動，最上方產品標題不必固定，應可隨整頁上捲隱藏，以騰出圖面；Route水平捲軸仍應固定在自己的圖框可見底部，不得退回要捲到整頁最底才可用的問題。僅需求記錄，尚未實作；將與後续原生驗收分開。

### 使用者完成讀檔後的核對

> 後續使用者要求降低minimum，並授權代理在其離開電腦時正常關閉App；Alt+F4關閉成功，已更新800×520版exe，Release／完整WPF及最新收合專項通過。詳見MINIMUM_WINDOW_PROGRESS.md。原生視窗目前已關閉，後續驗收須重新啟動、載入sample14並建立新session；不得重用id397278。

- 重新 list_windows 唯一視窗仍 id397278；標題與 CurrentProjectFileTextBlock 已確認為整合目錄 sample14，正常就緒、clock0.0s。讀檔子項完成。
- 嘗試開啟「原生驗收量測」後未看到子選單；focused_element 回報 RouteSummaryExpander。重新 activate/state 及以最新 screenshot 座標重試一次仍未生效。畫面擷取開始呈現左側／上側被裁切，snapshot origin=(1809,-235)，不能從擷取尺寸推測實際DPI，也不能把工具點擊偏移判定為App hit-test FAIL。
- 停止後續 UI 輸入，請使用者將App移至主螢幕並還原普通視窗，再重新觀察。未按Play、未啟用量測、未改DPI／介面縮放；native gates仍未通過。

1. 核對 sample14 的實際來源／就緒狀態，正常選單啟用量測，讀取實際 DPI 與介面縮放；兩者分開。
2. Speed 窄短視窗捲动複验、Route 常駐水平捲軸、Diagram 全站標籤與 paused train hit-test。
3. 至少一次60x sanity、同一 process／sample／TimeDistance頁連續五輪 complete run及10s idle/reset memory checkpoints；不得 GC.Collect。
4. 原生 input timings／>100ms stall contexts、当前 sample事件引導 O04/O13/O20慢速目視、100/125/150%真實 DPI矩陣。

目前所有新版 native gates 仍 NOT COMPLETED／memory UNRESOLVED；不把前版證據或最新自動化198/198／WPF PASS當原生 PASS。未改 production/test code、未 commit/push/tag/release，保留既有 dirty 工作樹。
