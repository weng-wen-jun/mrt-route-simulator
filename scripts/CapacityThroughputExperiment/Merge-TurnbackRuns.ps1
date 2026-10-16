param([string]$Root=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference='Stop'
$base=Join-Path $Root 'artifacts/capacity-throughput-experiment'
$target=Join-Path $base 'after-alignment-11h-final/turnbacks'
$plan=Get-Content (Join-Path $base 'after-alignment-11h-final/measurement-plan.json') -Raw|ConvertFrom-Json
$expected=[ordered]@{'case02-station-front'=1;'case05-station-front'=1;'case07-s14-rear-single'=2;'case08-s14-rear-double'=2;'case08b-s14-rear-single-35s'=2;'case08c-s14-rear-single-33s'=2}
$metrics=@();$manifests=@();$hashes=@();$sources=@()
foreach($entry in $expected.GetEnumerator()){
 $path=Join-Path $base ('alignment-final-shards/'+$entry.Key+'/turnbacks')
 $data=@(Get-Content (Join-Path $path 'metrics.json') -Raw|ConvertFrom-Json)
 if($data.Count -ne $entry.Value){throw ($entry.Key+' incomplete: '+$data.Count+'/'+$entry.Value)}
 foreach($row in $data){if(-not $row.executed -or [Math]::Abs($row.simulationTimeSeconds-$plan.totalHorizonSeconds) -gt .11 -or $row.measurementPlanKey -ne $plan.cacheKey -or $row.returnDepartureCountsByBucket.Count -ne $plan.bucketCount){throw ('Invalid horizon or denominator: '+$entry.Key)}}
 $metrics+=$data
 $manifests+=@(Get-Content (Join-Path $path 'manifest.json') -Raw|ConvertFrom-Json)
 $hashes+=(Get-Content (Join-Path $path 'engine-sha256.txt') -Raw).Trim()
 $sources+=[pscustomobject]@{fixture=$entry.Key;path=$path;count=$data.Count}
}
if(@($hashes|Select-Object -Unique).Count -ne 1){throw 'Mixed engine versions'}
$actualHash=(Get-FileHash (Join-Path $base 'alignment-final-bin/MrtRouteSimulator.Engine.dll') -Algorithm SHA256).Hash
if($hashes[0] -ne $actualHash){throw 'Unexpected engine hash'}
New-Item -ItemType Directory -Path (Join-Path $target 'projects') -Force|Out-Null
foreach($source in $sources){Get-ChildItem (Join-Path $source.path 'projects') -File|Copy-Item -Destination (Join-Path $target 'projects') -Force}
ConvertTo-Json -InputObject @($metrics) -Depth 40|Set-Content (Join-Path $target 'metrics.json') -Encoding utf8
ConvertTo-Json -InputObject @($manifests) -Depth 40|Set-Content (Join-Path $target 'manifest.json') -Encoding utf8
ConvertTo-Json -InputObject @($sources) -Depth 10|Set-Content (Join-Path $target 'run-index.json') -Encoding utf8
$hashes[0]|Set-Content (Join-Path $target 'engine-sha256.txt') -Encoding utf8
Copy-Item (Join-Path $base 'after-alignment-11h-final/measurement-plan.json') $target -Force
$metrics|ForEach-Object {
 $m=$_;$flat=[ordered]@{};foreach($p in $m.PSObject.Properties){$flat[$p.Name]=if($p.Value -is [Array]){$p.Value -join '/'}elseif($null -ne $p.Value -and $p.Value -is [pscustomobject]){ConvertTo-Json $p.Value -Compress}else{$p.Value}}
 $flat['centralReturnRatePerHour']=$m.actualReturnOutboundDeparturesAtMeasuredStation*3600/$plan.measurementDurationSeconds
 $flat['unarrivedDelta']=$m.pendingInboundAtMeasurementEnd-$m.pendingInboundAtWarmup
 [pscustomobject]$flat
}|Export-Csv (Join-Path $target 'metrics.csv') -NoTypeInformation -Encoding utf8
Write-Output ('Merged '+$metrics.Count+' runs, engine '+$actualHash)
