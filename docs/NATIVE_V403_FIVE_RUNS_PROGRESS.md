# V4.0.3 同程序五輪原生播放／記憶體驗收（2026-10-02）

## 固定條件

- session `19aa5fa12b0041598c9fdd39826ee3b1`，由正常選單啟用；正常 Play／Reset，不直接推進 world、不呼叫 GC.Collect。
- App 是整合工作樹 Release exe；案例為整合工作樹 `14-大型-二十八站完整營運範例.mrtsim.json`。同一 process／同一案例／運行圖分頁，UI倍率60×。
- 真實 DPI144×144（150%）、介面縮放100%；1152×676.667 DIP，外層捲到頁首可隱藏位置。沒有在連續播放中並行重型 build／tests。
- 原始紀錄：`src/MrtRouteSimulator.App/bin/Release/net10.0-windows/NativeAcceptanceLogs/native-acceptance-20261002-110952157-19aa5fa12b0041598c9fdd39826ee3b1.jsonl`，另有同名 `.events.jsonl`。
- 已另存兩個session的原始metrics／events到 `docs/native-acceptance-raw/`，不依賴bin目錄長期存在。

## 完整運行

| 輪次 | 模擬秒 | acknowledged active wall秒 | 倍率 | >100ms dispatcher stalls | 狀態 |
| --- | ---: | ---: | ---: | ---: | --- |
| 1 | 7173.1 | 119.550925 | 60.00037 | 0 | 完成＋10s idle＋正常Reset |
| 2 | 7173.3 | 119.5582593 | 59.99836 | 0 | 完成＋10s idle＋正常Reset |
| 3 | 7173.1 | 119.5515414 | 60.00006 | 0 | 完成＋10s idle＋正常Reset |
| 4 | 7172.9 | 119.5481386 | 60.00010 | 0 | 完成＋10s idle＋正常Reset |
| 5 | 7172.9 | 119.5471957 | 60.00057 | 0 | 完成＋10s idle；Reset另session補錄 |

第1輪6列車全部完成、最終activeTrainCount0，無Pause。conservative dispatch wall為119.5730735s／59.98926×。frame publish邊界不是精確最後tick，也不是compositor present；完成觀察延遲46.2726ms。連續60× sanity（至少60s／≥58×）首輪通過。

## 記憶體與 cache（MiB）

| 輪次／階段 | private | managed heap | actual series | planned series | ordinary points |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 beforePlay | 200.00 | 51.18 | 0 | 8 | 3107 |
| 1 completion | 246.36 | 77.17 | 8 | 8 | 6392 |
| 1 10s idle | 245.99 | 83.42 | 8 | 8 | 6394 |
| 1 afterReset | 244.98 | 72.62 | 0 | 8 | 3107 |
| 1 nextPlay | 248.00 | 81.42 | 0 | 8 | 3107 |
| 2 completion | 253.86 | 88.79 | 8 | 8 | 6392 |
| 2 10s idle | 251.62 | 69.59 | 8 | 8 | 6394 |
| 2 afterReset | 253.70 | 82.16 | 0 | 8 | 3107 |
| 2 nextPlay | 248.68 | 83.04 | 0 | 8 | 3107 |
| 3 completion | 257.82 | 95.52 | 8 | 8 | 6394 |
| 3 10s idle | 259.36 | 92.08 | 8 | 8 | 6394 |
| 3 afterReset | 252.52 | 82.52 | 0 | 8 | 3107 |
| 3 nextPlay | 250.11 | 73.48 | 0 | 8 | 3107 |
| 4 completion | 258.04 | 71.44 | 8 | 8 | 6392 |
| 4 10s idle | 255.35 | 77.68 | 8 | 8 | 6394 |
| 4 afterReset | 259.74 | 86.33 | 0 | 8 | 3107 |
| 4 nextPlay | 255.61 | 85.76 | 0 | 8 | 3107 |
| 5 completion | 257.24 | 75.81 | 8 | 8 | 6392 |
| 5 10s idle | 257.23 | 85.42 | 8 | 8 | 6394 |
| 5 afterReset（補錄） | 262.22 | 82.03 | 0 | 8 | 3107 |

## 判定與限制

- 五輪相同事件序列：每輪1025事件，完整events陣列經PowerShell ConvertTo-Json（Depth10、Compress）標準化後SHA256均為 `BE00FF43A9BB8E6EC2D60D5F604866CA92E41493D1AF5EC61541335E9F1CF91B`；包含時刻、位置、速度與事件欄位，不僅比較count。
- 每輪final trajectory37172／safety9147／activeTrain0。完成時16條Polyline、55個TextBlock、592～593個Canvas child；每次Reset均回到0／1／1，actual series及actual points為0，planned baseline3107 ordinary／7022 critical保留。afterReset的frameHistoryCounts為null，不把null宣稱為零。
- completion working set依序340.06、349.70、354.79、355.19、353.53 MiB，有趨平與回落；累積配置19.470、32.728、45.655、58.305、70.874 GiB是allocation churn，不是retained heap。completion GC Gen0/1/2為1259/277/7、2115/528/9、2949/779/11、3767/1007/14、4578/1210/16，均為正常GC。
- 分類：**LIKELY BOUNDED WITH FRAMEWORK RESERVE**。managed heap／actual cache／WPF visuals沒有逐輪持續階梯式保留；private bytes仍有少量增長，沒有heap dump／物件retention證據，不能宣稱嚴格BOUNDED或完全無洩漏。
- 主量測器在第5輪10s idle後依既有上限自動停止。未重啟App（同一window1050752），正常選單重新啟用補錄session `424e58c36efc4f908f5c003c506d7673`，正常Reset並停止flush；其afterReset run=null，明確配對為第5輪後續檢查，不偽裝成主session第5輪紀錄，也沒有第6輪Play。
- 五輪complete／60× sanity／事件repeat子門檻PASS；memory為上述有限觀察分類。rolling p95 reservoir不是精確full-run percentile；dispatcher stall也不是實際native click latency。100/125 DPI、全尺寸resize/hit-test、全28站標籤掃描、O04/O13/O20慢速目視及完整input p95仍未完成，**overall NOT COMPLETED**。
