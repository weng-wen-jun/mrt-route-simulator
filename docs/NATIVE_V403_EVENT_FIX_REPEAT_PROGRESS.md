# 事件座標修正版原生續驗（2026-10-03）

## 固定條件與已完成

- V4.0.3 整合工作樹 Release；sample14；100%（96DPI）／interfaceScale1；window137866，同程序，不換檔、不強制GC。
- 上行跨站事件點改用既有結果context的共同車站顯示座標，不動Engine／raw事件／CSV；方向標示「上下行皆顯示／僅下行／僅上行」。Release0警告／錯誤、Engine198/198、完整WPF及diff check PASS。
- 首輪互動 session20813ba5e6fd4852bb06c987594be14e：暫停5432.8s，原生上行60～65分鐘跨站點跟隨實線、下行與雙向篩選、事件off/on PASS。續播完整7173.0s、1025事件、trajectory37172／safety9147／active0。1025事件與先前201efe基準逐筆完整JSON差異0。
- 首輪有Pause及多次篩選，不用它宣稱固定頁全程throughput。早期動畫2s有110.3109ms DispatcherTimer gap（非OS點擊延遲、非GC因果）；仍列限制。
- 首輪量測正常停止flush，兩份raw歸檔及原檔保留：metrics SHA256 `298B58F1082AE21B478BB7718C58B733EA6615075BF517966792231AE46D4A88`；events `232696AB7947B688F5F594DA6796E655BB8D4C0EAF412743433198F7B298619D`。

## 同程序五輪結果：四輪固定100%＋第五輪跨螢幕，不能列固定五輪PASS

- 新session `75f888da35ab4ae395cfcc7c1e2d7ea6`；stem `native-acceptance-20261003-113857378-75f888da35ab4ae395cfcc7c1e2d7ea6`。
- 目標固定Diagram、雙向、車輛全部、計畫／實際／事件勾選、時間0／自動結束、60×；正常選單Reset→Play，各次重設前確認10s idle；沒有重型build／tests並行。
- 第1輪完成7172.9s、1025事件、active wall119.5430382s／60.00266×，hasPause=false、boundaryDefined=true、exact=false；Dispatcher gap超100ms為0。已等到10sIdleAfterCompletion並正常Reset→Play第2輪；未達五輪，不提前列五輪PASS。
- 第2輪完成7172.9s、1025事件、119.547548s／60.00039×、無Pause、stall0；完成10s idle並正常Reset→Play第3輪。
- 第3輪完成7172.9s、1025事件、119.5475694s／60.00038×、stall0；完成10s idle並正常Reset→Play第4輪。
- 第4輪完成7172.9s、1025事件、119.5478955s／60.00022×、stall0；完成10s idle並正常Reset→Play第5輪。
- 第5輪完成7173.3s、1025事件、119.5537718s／60.00062×、無Pause，但開始223.1s附近出现windowMoveResize#37，未由本代理觸發。完成量測為120DPI／DISPLAY1、1550.4×926.4 DIP，與前4輪96DPI／DISPLAY2不同；有12筆Dispatcher gap（104.72～606.65ms），不可聲稱移窗一定是所有gap原因或GC原因。此輪列跨螢幕條件變動，不列第五輪固定100% gate。
- 五輪完整1025事件逐筆JSON mismatch0；final trajectory37172／safety9147／active0。completion private MiB 280.14／291.82／296.01／304.32／319.71；managed heap77.04／91.81／91.56／87.05／70.52，working set359.71／369.80／375.43／384.92／379.25。每次已記錄Reset actual series0、ordinary3107、Canvas children1，完成actual8／planned8、ordinary6392～6394、children592～593。快取／畫布數沒有逐輪累積，但private仍增且第五輪DPI不同，不宣稱嚴格BOUNDED／無洩漏／完整五輪memory PASS。
- session正常manual-stop flush；原始及歸檔SHA256一致，metrics `7B3222963248DDF3F070D0F029E5FCC2E375FC860604CCB9046A540F132635ED`；events `100B05FB501B68E7525050E88C26F8F864B5A92F05F1396946A0365FE9BF69A3`。保留當前125%畫面，不自動移回原螢幕。
- 整體仍NOT COMPLETED；此檔不替代其他DPI／尺寸矩陣、完整input p95與純resize owner gate。

## 固定125%補驗：兩輪後轉入新的運行圖顯示需求

- 保留同一window137866／同一程序／sample14，當前DISPLAY1、120DPI／interfaceScale1；session `ffb298eae2f746d9b621be363dfe113a`，stem `native-acceptance-20261003-115846630-ffb298eae2f746d9b621be363dfe113a`。
- sessionStart實測1534.4×910.4 DIP；正常Reset→Play第1輪已啟動。固定Diagram、雙向／全部車／計畫實際與事件開啟、60×，不移窗／不改DPI；目標五輪，但須每輪核對是否條件被外部更改。
- 125%第1輪完成7173.0s、1025事件、119.5505151s／59.99974×、hasPause=false、stall0；完成10s idle並正常Reset→Play第2輪。
- 第2輪完成7173.1s、1025事件、119.5517267s／59.99997×、hasPause=false；同樣120DPI與1534.4×910.4 DIP，但有9筆Dispatcher gap（102.20～630.72ms）。原因未證明，不列無卡頓PASS，不推斷為GC或移窗。
- 兩輪各1025事件與session20813基準逐筆完整JSON mismatch0；最終trajectory37172／safety9147／active0。
- 收到固定時間軸、事件列表收折及獨立上下縮放要求後，不繼續堆疊舊版五輪。正常停止量測（completedRuns2、manual-stop），正常關閉舊App，修改新版後重新驗收。
- 兩份raw已歸檔於docs/native-acceptance-raw，原檔保留且SHA256一致：metrics `4E5CA8C6D912BF5A611FDF84F510A33377314BD0B0659662325C54B96724649D`；events `3C674533F7DA053434C5B436457C9F04445D43E4F138481B5613105CDECEB438`。
