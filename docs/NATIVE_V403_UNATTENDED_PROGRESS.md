# 無人值守原生續驗（2026-10-03）

## 範圍

- 使用者允許其離開時繼續能自行完成的驗收；需要手動更改Windows螢幕縮放的項目暫留，不阻擋其餘檢查。
- computer-use 操作整合版 V4.0.3 正常讀檔、播放／暫停／重設、選單與視窗；不操控 Engine 時鐘、不改專案檔或Windows設定。
- 新視窗2627414，正常檔案選單載入整合工作樹 sample14；檔案對話框UIA索引失效後，重新觀察可見清單，選取14並Return開啟成功。
- 新量測 session `ecea18919d774e78a4bac4da966a7256`；raw `native-acceptance-20261003-011150458-ecea18919d774e78a4bac4da966a7256.jsonl`。
- sessionStarted 實測96 DPI／scale1，interfaceScale1，window806×520 DIP；非主monitor DISPLAY1。不要用截圖尺寸推測DPI。

## 初段紀錄（後續結果見下方）

- sample14 載入後左欄收合；小窗Route有實際水平溢位時固定proxy恢復可見。最大化圖面與工作區填滿。
- 第一次60×接近O04錯過完整事件窗口，不算目視PASS；正常Pause／Reset，改10×接近後在535.0s暫停。
- 暫停右鍵命中O04側線列車並正常選單跟隨，畫面確認FULL-O04；改1×由535.0s繼續觀察。O04 gate尚未完成。
- 本輪是互動驗收，不是同頁不中斷60×完整run；Pause/Reset導致runAborted不可當成完整播放失敗或成功。

## 初段待辦（本輪結束狀態以最後一節為準）

- O04 678.8～715.5s FULL-O04/EXPRESS-01越行、釋放待避與合流原生目視。
- O20 4529.7～4571.7s袋狀軌進入、5600～5665.5s SECTION-VEHICLE-01由DOWN到UP返回。
- 100%剩餘label/resize矩陣、冷暖互動p50/p95/max與純resize樣本；其他DPI需手動切換留待使用者。
- 原有五輪完成與memory分類沿用各自紀錄，不因本輪局部結果宣稱整體PASS。

## O04 原生1×目視完成

- 535.0s暫停側線 FULL-O04 並經正常右鍵選單確認同車跟隨；其後1×正常播放。
- 原生快照時鐘：565.1、619.1、671.2、687.7、695.4、704.9、715.4、724.0、731.1、741.0s（文字與截圖採集相差小於約1s，不假稱同步）。
- 687.7s快速車在左側主線入口、待避車仍在下方側線；695.4～704.9s快速車沿直線跨過側線車的位置，待避車未提早移動；715.4s快速車已位於右側主線、側線出口進路標示；724.0～731.1s普通車沿側線出口彎道向右行進；741.0s普通車已回到主線，保持在快速車後方。無瞬移／錯股道現象。
- 同session首個runEvents作獨立核對：EXPRESS-01於678.8鎖通過進路、689.1進入passing traversal、710.8完成匯入主線、715.4釋放站場；FULL-O04於715.5鎖側線出口並發車。該事件資料來自先前60×partial run，不冒充1×影像；1×畫面另有上述逐段原生快照證據。
- O04慢速越行／待避解除／普通車合流目視子項PASS；非完整物理安全證明或整體原生PASS。

## 冷暖切頁量測

- 暫停766.3s，第一次開啟運行圖action16：application208.31ms；暖切運行圖action18/20/22：1.01/0.92/0.93ms；切回模擬頁action17/19/21/23：1.97/1.87/2.11/1.71ms。
- 使用去重actionId及inputToApplicationVisualUpdateMilliseconds，不混入Loaded surrogate、不使用工具roundtrip；不是OS injection／compositor時間。
- 3個暖樣本僅描述範圍，不宣稱完整p95 gate。冷運行圖208ms與此前191/194ms等獨立native樣本一致地>100ms，首次切頁互動項不得列PASS；保留待診斷，不用warm數據掩蓋cold延遲。
- 最大化運行圖O26～O01全28站可見，O08/O08a/O09與O15/O15a/O16無重疊；clock凍結766.3s。只代表本尺寸100%DPI，不取代小窗分段／其他DPI矩陣。

## O20進入中央袋狀軌與等待

- 正常60×/10×接近，在4475.4s暫停。右鍵命中區間車，跟隨狀態明確SECTION-VEHICLE-01。
- 1×原生快照4516.6、4524.6、4536.4、4550.9、4559.6、4570.3s：先在下行月台停站，再沿下行分歧曲線進入兩主線之間的中央袋狀軌；4550.9以後標記位於中央軌，沒有從下行直接跳至上行。
- 4580.2s正常Pause；即時列車狀態同VehicleId，下行／折返／速度0／位置24.259km／下一站O20。原生圖面同車仍在中央軌。進軌與等待子項PASS；返回上行主線仍待1×觀察。

## O20反向出軌與資源釋放完成

- 5393.7s恢復1×；原生快照5554.1、5571.1s仍在中央袋狀軌。5600.3、5606.4s出現通往上行主線的橙色進路；5624.5s列車沿上方彎道移到上行主線；5656.4s已返回O20上行月台、橙色鎖定消失；5685.4s向左離站，5693.4s正常暫停。跟隨同車時，軌道相對中央車輛平移；不將畫面中央固定誤認為車輛未動。
- 暫停即時列車表確認 SECTION-VEHICLE-01／上行／巡航／23.572km／80.1km/h／下一站O19；tooltip亦明確SECTION-UP-01、EDGE:UP:O20:O19。
- 本輪第二份runEvents：5600s切換SECTION-DOWN-01→SECTION-UP-01且VehicleId保持；5625.7及5627.6s車尾淨空道岔進路；5635.5s返回service route並停站；5635.6s釋放站場；5665.5s停站後發車。原生畫面與事件序列一致。
- O20進軌、等待、返回上行、停站後離站與資源釋放目視子項PASS。這是局部事件驗收，不替代全部安全回歸。

## 100%DPI小窗與窄高補驗

- 第一個session還原尺寸806×520 DIP；全程5693.4s暫停。運行圖O26至O01分段標籤逐段可達，O08/O08a/O09及O15/O15a/O16分離；底部時間軸可見；圖內水平滑桿實際拖至右端。
- 外層捲動可隱藏頂端標題；Route固定proxy在外層上段及底部均存在，實際拖動由O15a～O18移到O18～O21。列車左鍵命中開啟同車實際速度曲線且底部時間軸可見；返回Route後右鍵命中顯示「停止跟隨此車輛」。時鐘不變。
- 小窗向外拖曳resize遭工具邊界檢查拒絕，未假稱成功；重新觀察後改用正常標題列「大小」命令與固定Down按鍵序列調整高度。
- 窄高第二個session實測96DPI／interfaceScale1／806×784 DIP；Route車輛與固定水平滑桿可見，運行圖O26與O01兩端及時間軸可捲到。尺寸不是精確800×520／指定窄高矩陣，不冒充該尺寸gate完成。
- 窄高僅armed-only尺寸／互動session，未新Play；events檔為空是預期，completedRuns0。

## 歸檔與本輪結束狀態

- 兩個session均以正常停止選單flush，保留原檔並複製至`docs/native-acceptance-raw`。
- 第一個session：92 checkpoints、235 inputAction phase records、51個去重actionId，兩個partial run事件406／932筆；manual-stop、completedRuns0。不將混合倍率、暫停與重設的互動session當60×完整輪次。
- 第一個JSONL SHA256：`A6C376AD7AA0C7E21CF0C5545F4B958C8E5FA6A97A4AFFB1E17684D069ED6E83`；events：`9466940C76BE2D94CEC20E692C029F5077C94938BE5830EA7EC6998C60EE852F`。第二個JSONL：`E04E2E151CDE2032292E0265B4DD3FDD7D8225EA3FEB7814090FAE023D27B22F`；空events：`E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`。兩組原檔與副本hash均明確比對相等。
- 本輪無production code／Engine／Schema／版本變更，未commit/push/release。App保留sample14、806×784窄高、運行圖、5693.4s暫停、量測停止。
- Overall **NOT COMPLETED**。O04/O20目視子項已完成；首次運行圖208.31ms仍未通過100ms互動目標。暖切頁樣本不足完整p95；純resize延遲與精確尺寸剩餘矩陣仍待。其他DPI切換需要使用者在場，但不影響本輪已完成子項。
- 冷圖表延遲唯讀診斷指向Presentation首次建立planned軌跡快取，不能把聚合timing當作逐action因果證據；後續若修正需另行 scoped implementation、Release／Engine／WPF及native cold重驗，不降低資料完整性或物理步進。
