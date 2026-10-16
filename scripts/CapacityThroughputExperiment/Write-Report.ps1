param(
 [string]$Root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
 [string]$OutputRoot='after-alignment-11h'
)
$ErrorActionPreference='Stop'
$out=if([IO.Path]::IsPathRooted($OutputRoot)){$OutputRoot}else{Join-Path $Root ('artifacts/capacity-throughput-experiment/'+$OutputRoot)}
$plan=Get-Content (Join-Path $out 'measurement-plan.json') -Raw|ConvertFrom-Json
$hours=$plan.measurementDurationSeconds/3600
$measure=Get-Content (Join-Path $out 'measurements.json') -Raw|ConvertFrom-Json
$theory=Get-Content (Join-Path $out 'theory.json') -Raw|ConvertFrom-Json
$lines=[Collections.Generic.List[string]]::new()
$lines.Add('# 容量參數進入實際運行後的手冊案例比較')
$lines.Add('')
$lines.Add(('每輪實際模擬{0:N0}秒，捨棄前後各{1:N0}秒，統計[{2:N0},{3:N0})中央{4:N1}小時；事件數除以{4:N1}得到單方向列/h。固定0.1秒物理步進不變；{5}個半小時桶保留流量趨勢。每多／少一班的解析度為{6:N3}列/h。' -f $plan.totalHorizonSeconds,$plan.discardSeconds,$plan.measurementStartSeconds,$plan.measurementEndSeconds,$hours,$plan.bucketCount,(1/$hours)))
$lines.Add('')
$lines.Add('手冊沒有要求動態搜尋最密班距。本報告依原設計班距與範例八150秒目標派車；舊3小時與密班距實驗保留於../after-fix，不混為本輪結果。名義班距能完成運行只能證明此模型承受該供給，不能獨立證明最大容量。')
$lines.Add('')
$lines.Add('## 已進入同一SimulationWorld的行為')
$lines.Add('')
$lines.Add('- 既有edge-local坡度現在影響牽引、惰行、營運／緊急制動；反向行駛翻號。停站、速限、道岔及移動閉塞的制動包絡前視剩餘movement最不利坡道。')
$lines.Add('- 中間站明確映射至載體B站：方向別進離站速限／停站、車長／性能、BrakeEffect轉成實際營運煞車、反應時間。閉塞保護包含停車區、出站號誌距離及overlap；前車車尾完全淨空後才開放。')
$lines.Add('- 折返幾何依手冊L+sp+sc(+sp2)重建：站前15／30m、站後20m、渡線170／85／160m、尾軌15m列車端部淨空；兩尾軌採對稱清空距離。折返仍由實體traversal、同車接續、diamond預約及rear-clear推進。')
$lines.Add('- 渡線閉塞明確套用轉換時間及紅燈制動安全係數；相交保護區的授權互斥。號誌最終移動防護若被迫介入，另記SignalAuthorityViolation，不能把該結果算成無違規通過。')
$lines.Add('')
$lines.Add('## 中間站名義設計班距')
$lines.Add('')
$lines.Add('| 案例／方向 | h(s) | 理論trunc列/h | 未截斷3600/h | 實測列/h | 首末跨度列/h | 中位班距(s) | 起點積壓增量 | 碰撞/預測/停站/授權違規（全11h） |')
$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---|')
foreach($m in $measure|Where-Object {$_.scenario -like 'aligned-*' -and $_.inputHeadwaySeconds -gt 40}){
 if($m.scenario -notmatch '^aligned-(.+)-(Outbound|Inbound)$'){throw $m.scenario}
 $prefix=$Matches[1]+'-'; $direction=if($Matches[2] -eq 'Outbound'){'Forward'}else{'Reverse'}
 $t=@($theory|Where-Object {$_.file.StartsWith($prefix) -and $_.direction -eq $direction})[0]
 $lines.Add(('| {0} | {1:N3} | {2} | {3:N3} | {4:N1} | {5:N3} | {6:N3} | {7} | {8}/{9}/{10}/{11} |' -f $m.scenario,$m.inputHeadwaySeconds,$t.lineCapacityPerHour,(3600/$t.designHeadwaySeconds),$m.middleDeparturesPerHour,$m.spanRatePerHour,$m.medianHeadwaySeconds,($m.pendingDueRuns-$m.pendingDueRunsAtWarmup),$m.collisionEvents,$m.predictedCollisionEvents,$m.stopViolations,$m.signalAuthorityViolationEvents))
}
$lines.Add('')
$lines.Add('## 折返：設計班距與150秒目標')
$lines.Add('')
$lines.Add('以A月台實際回程發車計數，排除尾軌換端的無StationId發車事件。手冊折返T只配置一次，A站一般停站為0。未到折返站增量包含尚在途中列車，需配合分段通過量判讀；不是純起點排隊數。')
$lines.Add('')
$lines.Add('| 案例 | h(s) | 供給列/h | 實測回程列/h | 未到折返站增量 | 碰撞/預測/停站/授權違規（全11h） |')
$lines.Add('|---|---:|---:|---:|---:|---|')
$turnbackPath=Join-Path $out 'turnbacks/metrics.json'
if(Test-Path $turnbackPath){foreach($m in (Get-Content $turnbackPath -Raw|ConvertFrom-Json)){
 if(-not $m.executed){$lines.Add('| '+$m.fixture+' | 未執行：'+$m.notExecutedReason+' | | | | |');continue}
 $lines.Add(('| {0} | {1:N3} | {2:N3} | {3:N1} | {4} | {5}/{6}/{7}/{8} |' -f $m.fixture,$m.inputHeadwaySeconds,(3600/$m.inputHeadwaySeconds),($m.actualReturnOutboundDeparturesAtMeasuredStation/$hours),($m.pendingInboundAtMeasurementEnd-$m.pendingInboundAtWarmup),$m.collisionEvents,$m.predictedCollisionEvents,$m.stationStopViolationEvents,$m.signalAuthorityViolationEvents))
}}
$lines.Add('')
$lines.Add('35秒停等在本批全域假設下，公式設計班距仍為151.443秒；33秒為148.843秒。即使150秒物理模擬可穩定供給24列/h，也不能將35秒方案改判為符合包含營運餘裕的150秒設計目標。')
$lines.Add('')
$lines.Add('## 精度與剩餘差異')
$lines.Add('')
$lines.Add('- 計數解析度由舊2小時的0.5列/h降為10小時的0.1列/h；理論整數截斷另以3600/h對照。積壓或延誤成長時，平均通過量只是過載輸出。20個半小時桶與前後半窗延誤留在JSON/CSV，暖機是否足夠應由趨勢判斷。')
$lines.Add('- 坡度採車頭單質點、0.009 m/s²/‰近似，尚未沿整列車長度積分；最不利剩餘路段制動包絡可能偏保守。坡度區域邊界、長度等於車長的月台與25m尾軌餘裕是明列示範假設。')
$lines.Add('- 中間站入口採停止列車上游車尾位置的合成號誌；出站號誌距離及overlap確實形成實體保護區，但手冊並未提供完整號誌配置。煞車安全係數在本模型作用於停止包絡；不保證與手冊所有簡化項有一對一等價。')
$lines.Add('- BrakeEffect乘於營運煞車；緊急煞車保留額定值。Jerk、惰行、建立時間、定位誤差與既有較低實體速限仍遵循原模型。PHF、乘客容量與設計餘裕留在分析／需求層，不以公式Ts硬性節流列車。')
$lines.Add('- 範例六銜接點、S7/S10及S1–S14完整路網仍未等條件重建。每班獨立入線供車，折返同車返回，沒有有限車隊、旅客上下車、隨機停站及故障模型。不能據本批宣布所有手冊公式或現地系統通過認證。')
$lines.Add('')
$lines.Add('軟體容量頁新增「套用中間站參數至運行」；先在容量設定明確對應現有實體StationId，再套用。舊分析設定不會自動改寫運行；既有幾何不自動搬動。配置衝突或不足的保護距離會拒絕套用。')
$lines.Add('')
$lines.Add('來源：運研所《2019年臺灣鐵道容量手冊》都會篇第2章與第6章；原輸入差異見samples/handbook-capacity/README.md，實體幾何與坡度假設見turnbacks/manifest.json。完整回歸與建置紀錄見QA_REPORT.md。')
$lines.Add('')
$lines.Add('數據與重跑：measurement-plan.json、measurements.json/csv、逐輪projects、turnbacks/metrics.json與manifest.json；腳本見../../../scripts/CapacityThroughputExperiment/README.md。')
$lines|Set-Content (Join-Path $out 'REPORT.md') -Encoding utf8
Write-Output (Join-Path $out 'REPORT.md')
