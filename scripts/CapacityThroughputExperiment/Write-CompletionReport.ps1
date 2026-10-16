param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$OutputRoot = 'artifacts/capacity-throughput-experiment/handbook-completion-11h'
)

$ErrorActionPreference = 'Stop'
$Invariant = [Globalization.CultureInfo]::InvariantCulture
$ExpectedEngine = 'FC38528FF0876BBBC9E017CAA56DEC6545FF190039EFDE179A6056277DD6FC12'
$ExpectedTopologySchema = 8
$Expected11h = [ordered]@{
    totalHorizonSeconds = 39600
    measurementStartSeconds = 1800
    measurementEndSeconds = 37800
    measurementDurationSeconds = 36000
    bucketSeconds = 1800
    bucketCount = 20
    horizonLabel = '11h'
    cacheKey = 'horizon-39600-discard-1800-bucket-1800-v2'
}
$Expected12h = [ordered]@{
    totalHorizonSeconds = 43200
    measurementStartSeconds = 5400
    measurementEndSeconds = 41400
    measurementDurationSeconds = 36000
    bucketSeconds = 1800
    bucketCount = 20
    horizonLabel = '12h'
    cacheKey = 'horizon-43200-discard-1800-bucket-1800-v2-warmup-5400-cooldown-1800'
}
$SafetyNames = @('Collision', 'PredictedCollision', 'StationStopViolation', 'SignalAuthorityViolation')
$ValidationErrors = [Collections.Generic.List[string]]::new()
$Checks = [Collections.Generic.List[object]]::new()
$ProjectChecks = [Collections.Generic.List[object]]::new()
$SchemaChecks = [Collections.Generic.List[object]]::new()

function Join-Root([string]$RelativePath) {
    $base = if ($null -ne $script:ArtifactRoot) { $script:ArtifactRoot } else { $Root }
    Join-Path $base ($RelativePath -replace '/', '\')
}

function Read-Json([string]$RelativePath) {
    $path = if ([IO.Path]::IsPathRooted($RelativePath)) { $RelativePath } else { Join-Root $RelativePath }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "缺少 JSON：$path"
    }
    try {
        Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 100
    } catch {
        throw "JSON 無法讀取：$path；$($_.Exception.Message)"
    }
}

function Get-Prop($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    $property.Value
}

function As-Array($Value) {
    if ($null -eq $Value) { return @() }
    if ($Value -is [Array]) { return @($Value) }
    return @($Value)
}

function Add-Error([string]$Message) {
    $null = $script:ValidationErrors.Add($Message)
}

function Add-Check([string]$Name, [bool]$Pass, $Actual, $Expected, [string]$Detail = '') {
    $null = $script:Checks.Add([pscustomobject]@{
        name = $Name
        pass = $Pass
        actual = $Actual
        expected = $Expected
        detail = $Detail
    })
    if (-not $Pass) { Add-Error "$Name：$Detail (actual=$Actual expected=$Expected)" }
}

function Require-Properties($Object, [string[]]$Names, [string]$Context) {
    $present = @($Object.PSObject.Properties.Name)
    foreach ($name in $Names) {
        if ($present -notcontains $name) {
            Add-Error "$Context 缺少欄位 $name"
        }
    }
}

function F([object]$Value, [int]$Digits = 2) {
    if ($null -eq $Value) { return '—' }
    ([double]$Value).ToString("F$Digits", $Invariant)
}

function I([object]$Value) {
    if ($null -eq $Value) { return '—' }
    ([int]$Value).ToString($Invariant)
}

function Escape-Md([object]$Value) {
    if ($null -eq $Value) { return '—' }
    ([string]$Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Hash-File([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Check-Plan($Plan, $Expected, [string]$Context) {
    $ok = $true
    if ($null -eq $Plan) {
        Add-Check "plan:$Context" $false $null $Expected.cacheKey 'plan 缺少'
        return $false
    }
    $planNames = @('totalHorizonSeconds','measurementStartSeconds','measurementEndSeconds','measurementDurationSeconds','bucketSeconds','bucketCount','horizonLabel','cacheKey')
    $expectedShape = [ordered]@{}
    foreach ($name in $planNames) {
        $actual = Get-Prop $Plan $name
        $expectedValue = $Expected[$name]
        $expectedShape[$name] = $expectedValue
        $equal = if ($expectedValue -is [double] -or $expectedValue -is [int]) {
            $null -ne $actual -and ([double]$actual -eq [double]$expectedValue)
        } else {
            [string]$actual -eq [string]$expectedValue
        }
        if (-not $equal) { $ok = $false }
    }
    Add-Check "plan:$Context" $ok ($Plan | ConvertTo-Json -Compress) ($expectedShape | ConvertTo-Json -Compress) '固定 11h/12h 視窗欄位'
    $ok
}

function Check-MetricPlanKey($Metric, [string]$ExpectedKey, [string]$Context) {
    $key = Get-Prop $Metric 'measurementPlanKey'
    if ($null -eq $key) { $key = Get-Prop (Get-Prop $Metric 'plan') 'cacheKey' }
    Add-Check "metric-plan:$Context" ($key -eq $ExpectedKey) $key $ExpectedKey '量測窗 key'
}

function Check-EngineValue([string]$Context, [string]$Actual) {
    if ([string]::IsNullOrWhiteSpace($Actual)) {
        Add-Check "engine:$Context" $false 'not-provided' $ExpectedEngine '此來源沒有 Engine hash；不能猜測'
        return
    }
    Add-Check "engine:$Context" ($Actual.Trim().ToUpperInvariant() -eq $ExpectedEngine) $Actual.Trim().ToUpperInvariant() $ExpectedEngine 'Engine SHA-256'
}

function Check-ProjectFile([string]$Context, [string]$Path, [string]$ExpectedHash = '') {
    $exists = Test-Path -LiteralPath $Path -PathType Leaf
    if (-not $exists) {
        $entry = [pscustomobject]@{ context = $Context; path = $Path; exists = $false; schemaVersion = $null; schemaPass = $false; expectedHash = $ExpectedHash; actualHash = $null; hashPass = [string]::IsNullOrWhiteSpace($ExpectedHash) }
        $null = $script:SchemaChecks.Add($entry)
        Add-Error "project:$Context 缺少檔案 $Path"
        return $entry
    }
    $schema = $null
    try { $schema = Get-Prop (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100) 'schemaVersion' } catch { Add-Error "project:$Context JSON 無法讀取" }
    $schemaPass = $schema -eq $ExpectedTopologySchema
    $actualHash = Hash-File $Path
    $hashPass = [string]::IsNullOrWhiteSpace($ExpectedHash) -or $actualHash -eq $ExpectedHash.ToUpperInvariant()
    $entry = [pscustomobject]@{ context = $Context; path = $Path; exists = $true; schemaVersion = $schema; schemaPass = $schemaPass; expectedHash = $ExpectedHash; actualHash = $actualHash; hashPass = $hashPass }
    $null = $script:SchemaChecks.Add($entry)
    Add-Check "schema:$Context" $schemaPass $schema $ExpectedTopologySchema 'Schema 8 topology project'
    if (-not [string]::IsNullOrWhiteSpace($ExpectedHash)) {
        $null = $script:ProjectChecks.Add([pscustomobject]@{ context = $Context; path = $Path; expected = $ExpectedHash.ToUpperInvariant(); actual = $actualHash; pass = $hashPass })
        Add-Check "project-hash:$Context" $hashPass $actualHash $ExpectedHash.ToUpperInvariant() 'metric/project bytes SHA-256'
    }
    $entry
}

function Safety([hashtable]$Values, [string]$Context) {
    $result = [ordered]@{}
    foreach ($name in $SafetyNames) {
        $value = $Values[$name]
        $valid = $null -ne $value -and $value -is [ValueType] -and [double]$value -ge 0
        Add-Check "safety-field:${Context}:$name" $valid $value 'non-negative number' '四類安全欄位完整'
        $result[$name] = if ($null -eq $value) { $null } else { [int]$value }
    }
    [pscustomobject]$result
}

function Safety-Text($SafetyObject) {
    ($SafetyNames | ForEach-Object { "$_=$(I (Get-Prop $SafetyObject $_))" }) -join '; '
}

function Point-Cell($Point, [string]$Kind = 'arrivals') {
    $value = Get-Prop $Point $Kind
    if ($null -eq $value) { return '—' }
    "n=$(I (Get-Prop $value 'count'))/med=$(F (Get-Prop $value 'medianHeadway'))/span=$(F (Get-Prop $value 'spanHeadway'))"
}

$outputPath = if ([IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $Root $OutputRoot }
if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) { throw "輸出資料夾不存在：$outputPath" }
$script:ArtifactRoot = $outputPath

# The report reads the artifact schema defined by TopologyProjectFormat (Schema 8),
# plus the per-experiment JSON contracts. It never runs the Engine or rewrites inputs.
$rootPlan = Read-Json 'measurement-plan.json'
Check-Plan -Plan $rootPlan -Expected $Expected11h -Context 'root/11h' | Out-Null

$previousPlan = Read-Json 'previous-cases/measurement-plan.json'
Check-Plan -Plan $previousPlan -Expected $Expected11h -Context 'previous-cases/11h' | Out-Null
$previous = As-Array (Read-Json 'previous-cases/measurements.json')
$previousTheory = As-Array (Read-Json 'previous-cases/theory.json')
if ($previous.Count -ne 8) { Add-Error "previous-cases 應有 8 方向，實際 $($previous.Count)" }
$previousRows = [Collections.Generic.List[object]]::new()
foreach ($metric in $previous) {
    $context = "previous/$($metric.scenario)"
    Require-Properties $metric @('scenario','inputHeadwaySeconds','direction','middleDeparturesPerHour','spanRatePerHour','medianHeadwaySeconds','pendingDueRuns','pendingDueRunsAtWarmup','collisionEvents','predictedCollisionEvents','stopViolations','signalAuthorityViolationEvents','measurementPlanKey','measurementBucketCounts') $context
    Check-MetricPlanKey $metric $Expected11h.cacheKey $context
    if (@(Get-Prop $metric 'measurementBucketCounts').Count -ne 20) { Add-Error "$context 應有 20 個半小時桶" }
    $match = $null
    if ($metric.scenario -match '^aligned-(?<case>[0-9]+b?)-(?<direction>Outbound|Inbound)$') {
        $theoryDirection = if ($Matches.direction -eq 'Outbound') { 'Forward' } else { 'Reverse' }
        $filePrefix = "$($Matches.case)-"
        $match = $previousTheory | Where-Object { $_.file.StartsWith($filePrefix) -and $_.direction -eq $theoryDirection } | Select-Object -First 1
    }
    if ($null -eq $match) { Add-Error "$context 找不到 theory.json 對應項" }
    $safety = Safety @{ Collision = $metric.collisionEvents; PredictedCollision = $metric.predictedCollisionEvents; StationStopViolation = $metric.stopViolations; SignalAuthorityViolation = $metric.signalAuthorityViolationEvents } $context
    $project = Get-ChildItem -LiteralPath (Join-Root 'previous-cases/projects') -File -Filter "$($metric.scenario)-*.mrtsim.json" | Where-Object { $_.BaseName -match [regex]::Escape($metric.inputHeadwaySeconds.ToString([Globalization.CultureInfo]::InvariantCulture)) } | Select-Object -First 1
    if ($null -eq $project) { $project = Get-ChildItem -LiteralPath (Join-Root 'previous-cases/projects') -File -Filter "$($metric.scenario)-*.mrtsim.json" | Select-Object -First 1 }
    if ($null -eq $project) {
        Add-Error "$context 找不到 scenario+h 對應 Schema 8 project"
    } else {
        Check-ProjectFile $context $project.FullName | Out-Null
        $engineSidecar = "$($project.FullName).engine-sha256"
        $versionSidecar = "$($project.FullName).experiment-version"
        if (-not (Test-Path -LiteralPath $engineSidecar -PathType Leaf)) { Add-Error "$context 缺少 Engine sidecar $engineSidecar" } else { Check-EngineValue "$context/project-sidecar" ((Get-Content -LiteralPath $engineSidecar -Raw).Trim()) }
        if (-not (Test-Path -LiteralPath $versionSidecar -PathType Leaf)) { Add-Error "$context 缺少 experiment-version sidecar $versionSidecar" } else { Add-Check "experiment-version:$context" (((Get-Content -LiteralPath $versionSidecar -Raw).Trim()) -eq $Expected11h.cacheKey) ((Get-Content -LiteralPath $versionSidecar -Raw).Trim()) $Expected11h.cacheKey 'previous project sidecar measurement plan' }
    }
    $previousRows.Add([pscustomobject]@{
        scenario = $metric.scenario
        inputHeadwaySeconds = $metric.inputHeadwaySeconds
        theoryDesignHeadwaySeconds = Get-Prop $match 'designHeadwaySeconds'
        theoryLineCapacityPerHourTruncated = Get-Prop $match 'lineCapacityPerHour'
        theoryLineCapacityPerHourUntruncated = if ($null -eq $match) { $null } else { 3600 / [double]$match.designHeadwaySeconds }
        actualPerHour = $metric.middleDeparturesPerHour
        actualSpanPerHour = $metric.spanRatePerHour
        medianHeadwaySeconds = $metric.medianHeadwaySeconds
        pendingDrift = [int]$metric.pendingDueRuns - [int]$metric.pendingDueRunsAtWarmup
        delayFirst = Get-Prop $metric 'firstHalfMeanOriginDelaySeconds'
        delayLast = Get-Prop $metric 'lastHalfMeanOriginDelaySeconds'
        safety = $safety
    })
}

$turnbackRows = [Collections.Generic.List[object]]::new()
foreach ($relative in @('front-shards/case02/turnbacks','front-shards/case05/turnbacks')) {
    $folder = Join-Root $relative
    $metricPath = Join-Path $folder 'metrics.json'
    $metric = (As-Array (Get-Content -LiteralPath $metricPath -Raw | ConvertFrom-Json -Depth 100))[0]
    $context = "turnback/$($metric.fixture)"
    Require-Properties $metric @('fixture','theoryDesignHeadwaySeconds','inputHeadwaySeconds','executed','actualReturnOutboundDeparturesAtMeasuredStation','pendingInboundAtWarmup','pendingInboundAtMeasurementEnd','collisionEvents','predictedCollisionEvents','stationStopViolationEvents','signalAuthorityViolationEvents','measurementPlanKey','simulationTimeSeconds') $context
    Check-MetricPlanKey $metric $Expected11h.cacheKey $context
    Check-EngineValue "$context/sidecar" ((Get-Content -LiteralPath (Join-Path $folder 'engine-sha256.txt') -Raw).Trim())
    $manifest = Get-Content -LiteralPath (Join-Path $folder 'manifest.json') -Raw | ConvertFrom-Json -Depth 100
    $project = Get-ChildItem -LiteralPath (Join-Path $folder 'projects') -File -Filter '*.mrtsim.json' | Select-Object -First 1
    Check-ProjectFile $context $project.FullName | Out-Null
    $safety = Safety @{ Collision = $metric.collisionEvents; PredictedCollision = $metric.predictedCollisionEvents; StationStopViolation = $metric.stationStopViolationEvents; SignalAuthorityViolation = $metric.signalAuthorityViolationEvents } $context
    $turnbackRows.Add([pscustomobject]@{
        fixture = $metric.fixture
        handbookCase = $metric.handbookCase
        inputHeadwaySeconds = $metric.inputHeadwaySeconds
        theoryDesignHeadwaySeconds = $metric.theoryDesignHeadwaySeconds
        theoryLineCapacityPerHourTruncated = Get-Prop (Get-Prop $manifest 'theory') 'lineCapacityPerHour'
        theoryLineCapacityPerHourUntruncated = 3600 / [double]$metric.theoryDesignHeadwaySeconds
        returnPerHour = [double]$metric.actualReturnOutboundDeparturesAtMeasuredStation / 10
        returnCount = $metric.actualReturnOutboundDeparturesAtMeasuredStation
        pendingDrift = [int]$metric.pendingInboundAtMeasurementEnd - [int]$metric.pendingInboundAtWarmup
        waitingEpisodes = $metric.waitingForResourceEpisodes
        safety = $safety
    })
}
if ($turnbackRows.Count -ne 2) { Add-Error "front-shards 折返輪次應有 2，實際 $($turnbackRows.Count)" }

$points = As-Array (Read-Json 'points/metrics.json')
if ($points.Count -ne 8) { Add-Error "points/metrics.json 應有 8 方向，實際 $($points.Count)" }
$pointRows = [Collections.Generic.List[object]]::new()
foreach ($metric in $points) {
    $context = "points/$($metric.caseId)/$($metric.sourcePointId)/$($metric.direction)"
    Require-Properties $metric @('caseId','sourcePointId','direction','theoryDesignHeadwaySeconds','theoryLineCapacityPerHourTruncated','theoryLineCapacityPerHourUntruncated','inputHeadwaySeconds','actualEventCount','actualSpanRatePerHour','actualMedianHeadwaySeconds','pendingAtWarmup','pendingAtMeasurementEnd','originDelayFirstHalfMeanSeconds','originDelayLastHalfMeanSeconds','collisionEvents','predictedCollisionEvents','stationStopViolationEvents','signalAuthorityViolationEvents','simulationTimeSeconds','engineSha256','projectPath','measurementPlanKey') $context
    Check-MetricPlanKey $metric $Expected11h.cacheKey $context
    Check-EngineValue $context $metric.engineSha256
    $projectPath = [string]$metric.projectPath
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { $projectPath = Join-Root ('points/handbook-points/projects/' + [IO.Path]::GetFileName($metric.projectPath)) }
    Check-ProjectFile $context $projectPath | Out-Null
    $safety = Safety @{ Collision = $metric.collisionEvents; PredictedCollision = $metric.predictedCollisionEvents; StationStopViolation = $metric.stationStopViolationEvents; SignalAuthorityViolation = $metric.signalAuthorityViolationEvents } $context
    $pointRows.Add([pscustomobject]@{
        caseId = $metric.caseId
        modeTag = $metric.modeTag
        sourcePointId = $metric.sourcePointId
        direction = $metric.direction
        inputHeadwaySeconds = $metric.inputHeadwaySeconds
        theoryDesignHeadwaySeconds = $metric.theoryDesignHeadwaySeconds
        theoryLineCapacityPerHourTruncated = $metric.theoryLineCapacityPerHourTruncated
        theoryLineCapacityPerHourUntruncated = $metric.theoryLineCapacityPerHourUntruncated
        actualPerHour = [double]$metric.actualEventCount / 10
        actualCount = $metric.actualEventCount
        actualSpanPerHour = $metric.actualSpanRatePerHour
        actualMedianHeadwaySeconds = $metric.actualMedianHeadwaySeconds
        pendingDrift = [int]$metric.pendingAtMeasurementEnd - [int]$metric.pendingAtWarmup
        delayFirst = $metric.originDelayFirstHalfMeanSeconds
        delayLast = $metric.originDelayLastHalfMeanSeconds
        safety = $safety
    })
}

$s1 = Read-Json 'handbook-s1/metrics.json'
$s1Context = 'handbook-s1/case07-s1-front'
Require-Properties $s1 @('engineHash','projectHash','name','theory','headway','plan','arrivals','departures','safety','pendingAtWarmup','pendingAtEnd','pendingDrift','simulationSeconds') $s1Context
Check-Plan -Plan (Get-Prop $s1 'plan') -Expected $Expected11h -Context $s1Context | Out-Null
Check-EngineValue $s1Context $s1.engineHash
Check-ProjectFile $s1Context (Join-Root 'handbook-s1/project.mrtsim.json') $s1.projectHash | Out-Null
$s1Safety = Safety @{ Collision = $s1.safety.Collision; PredictedCollision = $s1.safety.PredictedCollision; StationStopViolation = $s1.safety.StationStopViolation; SignalAuthorityViolation = $s1.safety.SignalAuthorityViolation } $s1Context

$junctionFiles = Get-ChildItem -LiteralPath (Join-Root 'junctions-shards') -Recurse -File -Filter '*.metrics.json' | Sort-Object FullName
if ($junctionFiles.Count -ne 11) { Add-Error "junctions-shards metrics 應有 11，實際 $($junctionFiles.Count)" }
$junctionRows = [Collections.Generic.List[object]]::new()
foreach ($file in $junctionFiles) {
    $metric = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -Depth 100
    $context = "junction/$($metric.name)"
    Require-Properties $metric @('name','ratio','engineHash','projectHash','plan','designHeadwaySeconds','theoryMainTrainsPerHour','actualMainTrainsPerHour','actualBranchTrainsPerHour','actualMainCrossings','actualBranchCrossings','pendingBefore','pendingAfter','meanSourceDepartureDelaySeconds','maxSourceDepartureDelaySeconds','collisionEvents','predictedCollisionEvents','stationStopViolationEvents','signalAuthorityViolationEvents') $context
    Check-Plan -Plan $metric.plan -Expected $Expected11h -Context $context | Out-Null
    Check-EngineValue $context $metric.engineHash
    $projectPath = Join-Path $file.DirectoryName ('projects/' + ($file.BaseName -replace '\.metrics$','') + '.mrtsim.json')
    Check-ProjectFile $context $projectPath $metric.projectHash | Out-Null
    $safety = Safety @{ Collision = $metric.collisionEvents; PredictedCollision = $metric.predictedCollisionEvents; StationStopViolation = $metric.stationStopViolationEvents; SignalAuthorityViolation = $metric.signalAuthorityViolationEvents } $context
    $junctionRows.Add([pscustomobject]@{
        name = $metric.name
        ratio = $metric.ratio
        theoryDesignHeadwaySeconds = $metric.designHeadwaySeconds
        theoryLineCapacityPerHourTruncated = $metric.theoryMainTrainsPerHour
        theoryLineCapacityPerHourUntruncated = 3600 / [double]$metric.designHeadwaySeconds
        actualMainPerHour = $metric.actualMainTrainsPerHour
        actualBranchPerHour = $metric.actualBranchTrainsPerHour
        pendingMainDrift = [int]$metric.pendingAfter.main - [int]$metric.pendingBefore.main
        pendingBranchDrift = [int]$metric.pendingAfter.branch - [int]$metric.pendingBefore.branch
        meanDelay = $metric.meanSourceDepartureDelaySeconds
        maxDelay = $metric.maxSourceDepartureDelaySeconds
        safety = $safety
    })
}

$networkPlan = Read-Json 'handbook-network/measurement-plan.json'
Check-Plan -Plan $networkPlan -Expected $Expected12h -Context 'handbook-network/12h' | Out-Null
$networkFiles = Get-ChildItem -LiteralPath (Join-Root 'handbook-network') -File -Filter 'case*.json' | Where-Object { $_.Name -notlike '*.mrtsim.json' } | Sort-Object Name
if ($networkFiles.Count -ne 8) { Add-Error "handbook-network case*.json 應有 8（排除 mrtsim），實際 $($networkFiles.Count)" }
$networkRows = [Collections.Generic.List[object]]::new()
$networkPointRows = [Collections.Generic.List[object]]::new()
$NetworkStations = @('S1','S7','S10','S14')
$NetworkDirections = @('Outbound','Inbound')
foreach ($file in $networkFiles) {
    $metric = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -Depth 100
    $context = "network/$($file.BaseName)"
    Require-Properties $metric @('version','variant','inputHeadwaySeconds','measurementPlan','engineHash','projectHash','fleetCheckpoints','finiteFleet','theory','points','safety','pendingAtWarmup','pendingAtEnd','pendingDrift','delayFirstHalf','delaySecondHalf','distinctVehiclesChangingDirection','simulationSeconds') $context
    Check-Plan -Plan $metric.measurementPlan -Expected $Expected12h -Context $context | Out-Null
    Check-EngineValue $context $metric.engineHash
    Check-ProjectFile $context (Join-Path $file.DirectoryName ($file.BaseName + '.mrtsim.json')) $metric.projectHash | Out-Null
    $safety = Safety @{ Collision = $metric.safety.Collision; PredictedCollision = $metric.safety.PredictedCollision; StationStopViolation = $metric.safety.StationStopViolation; SignalAuthorityViolation = $metric.safety.SignalAuthorityViolation } $context
    $pointsForCase = As-Array $metric.points
    foreach ($station in $NetworkStations) {
        foreach ($direction in $NetworkDirections) {
            $point = $pointsForCase | Where-Object { $_.station -eq $station -and $_.direction -eq $direction } | Select-Object -First 1
            if ($null -eq $point) { Add-Error "$context 缺少 $station/$direction"; continue }
            $networkPointRows.Add([pscustomobject]@{
                caseName = $file.BaseName; station = $station; direction = $direction
                arrivals = [pscustomobject]@{ count=$point.arrivals.count; medianHeadway=$point.arrivals.medianHeadway; spanHeadway=$point.arrivals.spanHeadway }
                departures = [pscustomobject]@{ count=$point.departures.count; medianHeadway=$point.departures.medianHeadway; spanHeadway=$point.departures.spanHeadway }
            })
        }
    }
    $checkpoints = As-Array $metric.fleetCheckpoints
    $checkpointPass = $checkpoints.Count -gt 0 -and (@($checkpoints | Where-Object { $_.fleet -ne $metric.finiteFleet -or $_.active -gt $metric.finiteFleet -or $_.uniqueActiveVehicles -ne $_.active }).Count -eq 0)
    $uniquePass = [int]$metric.distinctVehiclesChangingDirection -le [int]$metric.finiteFleet
    Add-Check "fleet:$context" ($checkpointPass -and $uniquePass) ([ordered]@{ checkpoints=$checkpoints.Count; finiteFleet=$metric.finiteFleet; distinctDirectionVehicles=$metric.distinctVehiclesChangingDirection; checkpointInvariant=$checkpointPass; distinctBound=$uniquePass }) 'finite fleet checkpoints and unique vehicle bound' 'active count equals unique count and stays within fleet'
    $theoryEntries = As-Array $metric.theory
    $bottleneck = $theoryEntries | Sort-Object designHeadwaySeconds -Descending | Select-Object -First 1
    $networkRows.Add([pscustomobject]@{
        caseName = $file.BaseName
        variant = $metric.variant
        inputHeadwaySeconds = $metric.inputHeadwaySeconds
        theoryBottleneckPoint = if ($null -eq $bottleneck) { $null } else { "$($bottleneck.referencePointId)/$($bottleneck.direction)" }
        theoryDesignHeadwaySeconds = if ($null -eq $bottleneck) { $null } else { $bottleneck.designHeadwaySeconds }
        theoryLineCapacityPerHourTruncated = if ($null -eq $bottleneck) { $null } else { $bottleneck.result.lineCapacityPerHour }
        theoryLineCapacityPerHourUntruncated = if ($null -eq $bottleneck) { $null } else { 3600 / [double]$bottleneck.result.designHeadwaySeconds }
        finiteFleet = $metric.finiteFleet
        pendingAtWarmup = $metric.pendingAtWarmup
        pendingAtEnd = $metric.pendingAtEnd
        pendingDrift = $metric.pendingDrift
        delayFirst = $metric.delayFirstHalf
        delayLast = $metric.delaySecondHalf
        safety = $safety
        fleetCheckpoints = $checkpoints.Count
        fleetInvariantPass = $checkpointPass -and $uniquePass
    })
}

# All local source folders use the same 11h window. Point mode plans are stored in
# each mode folder; validating those also catches an accidentally mixed denominator.
foreach ($relative in @('points/taiwan-handbook/measurement-plan.json','points/urcs-compatible/measurement-plan.json','points/handbook-points/taiwan-handbook/measurement-plan.json','points/handbook-points/urcs-compatible/measurement-plan.json')) {
    Check-Plan -Plan (Read-Json $relative) -Expected $Expected11h -Context $relative | Out-Null
}
foreach ($folder in (Get-ChildItem -LiteralPath (Join-Root 'junctions-shards') -Directory | Sort-Object Name)) {
    $planPath = Join-Path $folder.FullName 'junctions/measurement-plan.json'
    if (Test-Path -LiteralPath $planPath) { Check-Plan -Plan (Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json) -Expected $Expected11h -Context "$($folder.Name)/junctions" | Out-Null }
}

# Concise observations used by the narrative. These are deliberately facts from
# the JSON outputs; no missing aligned hash is inferred from a parent/tool DLL.
$observationChecks = [Collections.Generic.List[object]]::new()
function Add-Observation([string]$Name, [bool]$Pass, $Actual, $Expected, [string]$Meaning) {
    $null = $script:observationChecks.Add([pscustomobject]@{ name=$Name; pass=$Pass; actual=$Actual; expected=$Expected; meaning=$Meaning })
}
$network07 = $networkRows | Where-Object caseName -eq 'case07-150s' | Select-Object -First 1
Add-Observation 'case07-150-pending' ($null -ne $network07 -and $network07.pendingDrift -eq 161) $(if($null -eq $network07){$null}else{$network07.pendingDrift}) 161 '整線 07-150 的 pending drift'
Add-Observation 'case07-150-predicted' ($null -ne $network07 -and $network07.safety.PredictedCollision -eq 150248) $(if($null -eq $network07){$null}else{$network07.safety.PredictedCollision}) 150248 '整線 07-150 全 12h 預測事件'
$network08150 = $networkRows | Where-Object caseName -eq 'case08-150s' | Select-Object -First 1
$network08150Points = @($networkPointRows | Where-Object caseName -eq 'case08-150s')
Add-Observation 'case08-150-bidirectional-24' ($network08150Points.Count -eq 8 -and @($network08150Points | Where-Object { [double]$_.arrivals.count / 10 -lt 24 }).Count -eq 0) $network08150Points.Count 8 'S1/S7/S10/S14 雙向中央 10h 到達量為 24/h'
Add-Observation 'case08-150-safety-zero' ($null -ne $network08150 -and ($SafetyNames | ForEach-Object { [int](Get-Prop $network08150.safety $_) } | Where-Object { $_ -ne 0 }).Count -eq 0) $(if($null -eq $network08150){$null}else{Safety-Text $network08150.safety}) 'all zero' '整線 08-150 四類安全'
$network08nom = $networkRows | Where-Object caseName -eq 'case08-139.648s' | Select-Object -First 1
Add-Observation 'case08-nominal-25.8' ($null -ne $network08nom -and @($networkPointRows | Where-Object { $_.caseName -eq 'case08-139.648s' -and [double]$_.arrivals.count / 10 -ge 25.8 }).Count -eq 8) $(if($null -eq $network08nom){$null}else{$network08nom.theoryLineCapacityPerHourUntruncated}) 25.8 '08 nominal case central 10h 約 25.8/h'
Add-Observation 'case08-nominal-predicted' ($null -ne $network08nom -and $network08nom.safety.PredictedCollision -eq 148) $(if($null -eq $network08nom){$null}else{$network08nom.safety.PredictedCollision}) 148 '08 nominal case predicted events'
$nominalSafetyRows = @($networkRows | Where-Object { $_.caseName -in @('case08b-150s','case08c-150s') })
Add-Observation 'case08bc-150-predicted' ($nominalSafetyRows.Count -eq 2 -and @($nominalSafetyRows | Where-Object { $_.safety.PredictedCollision -ge 29000 }).Count -eq 2) ($nominalSafetyRows | ForEach-Object { "$($_.caseName)=$($_.safety.PredictedCollision)" }) 'both >= 29000' '08b/08c 150s 達供給需求但有大量預測事件'

$allRounds = $previousRows.Count + $turnbackRows.Count + $pointRows.Count + 1 + $junctionRows.Count + $networkRows.Count
Add-Check 'round-count' ($allRounds -eq 38) $allRounds 38 '指定六組來源合計'
$engineProvided = @($Checks | Where-Object { $_.name -like 'engine:*' -and $_.actual -ne 'not-provided' })
$engineMissing = @($Checks | Where-Object { $_.name -like 'engine:*' -and $_.actual -eq 'not-provided' })
if ($engineMissing.Count -gt 0) {
    # The aligned previous-case rows intentionally have no engine field. Keep this
    # visible in audit.json instead of fabricating a hash from another artifact.
    $null = $Checks.Add([pscustomobject]@{ name='engine:aligned-previous-metadata'; pass=$true; actual='not-provided'; expected=$ExpectedEngine; detail='aligned hash is held outside these rows; no inference made' })
}

$audit = [ordered]@{
    format = 'capacity-throughput-completion-audit-v1'
    root = $Root
    output = $outputPath
    expected = [ordered]@{ rounds = 38; topologySchemaVersion = $ExpectedTopologySchema; engineSha256 = $ExpectedEngine; window11h = $Expected11h; window12h = $Expected12h }
    scope = [ordered]@{
        source = '2019 Taiwan rail capacity handbook metropolitan chapter'
        sourcePdf = '../../../../rea-evaluation/urcs-capacity/evidence/taiwan-rail-capacity-2019.pdf'
        toolRebuild = '../../handbook-completion-tools/'
        appBuild = '../../handbook-completion-build/'
        note = 'train counts are operational events; passenger throughput is not simulated'
    }
    coverage = [ordered]@{
        previousCases = $previousRows.Count
        turnbacks = $turnbackRows.Count
        points = $pointRows.Count
        handbookS1 = 1
        junctionShards = $junctionRows.Count
        handbookNetwork = $networkRows.Count
        total = $allRounds
    }
    plans = [ordered]@{ local11h = $Expected11h; network12h = $Expected12h; localMeasurement = '11h run, discard first/last 30m, central 10h'; networkMeasurement = '12h run, warmup 90m and end 30m, central 10h' }
    checks = $Checks
    schemaChecks = $SchemaChecks
    projectHashChecks = $ProjectChecks
    observations = $observationChecks
    rounds = [ordered]@{ previous = $previousRows; turnbacks = $turnbackRows; points = $pointRows; handbookS1 = [pscustomobject]@{ name=$s1.name; theory=$s1.theory; headway=$s1.headway; arrivals=$s1.arrivals; departures=$s1.departures; pendingDrift=$s1.pendingDrift; safety=$s1Safety }; junctions = $junctionRows; network = $networkRows; networkPoints = $networkPointRows }
    warnings = @(
        '13 interstation distances are not measured in the handbook fixture; the operational network uses a synthetic 13 × 1.2 km station-spacing assumption. 0.8 km/1.6 km are bounded free-circuit sensitivities only.',
        'r is the proportion of consecutive mainline gaps without an inserted branch, not a traffic or flow ratio.',
        'design headway 151.443 s for 35 s dwell does not provide 150 s design margin.',
        'the original eight handbook examples plus four supplemental branch variants are covered by these evidence sets; this is not field capacity certification.',
        'network safety counts are full 12h event counts; throughput point counts use the central 10h window. Waiting markers are not waiting seconds.',
        'passenger capacity values are analytical train-capacity multiplications only; boarding, alighting, load, crowding, and passenger flow are not simulated.'
    )
    status = if ($ValidationErrors.Count -eq 0) { 'PASS' } else { 'FAIL' }
    validationErrors = $ValidationErrors
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$auditPath = Join-Path $outputPath 'audit.json'
[IO.File]::WriteAllText($auditPath, ($audit | ConvertTo-Json -Depth 100), $utf8)

$report = [Collections.Generic.List[string]]::new()
$null = $report.Add('# 容量吞吐完成報告')
$null = $report.Add('')
$null = $report.Add("本報告由 `scripts/CapacityThroughputExperiment/Write-CompletionReport.ps1` 只讀彙整；audit 狀態：**$($audit.status)**。共核對 **$allRounds 輪**：previous-cases 8、front-shards 折返 2、points 8、handbook-s1 1、junctions-shards 11、handbook-network 8。原始手冊 8 例與 4 個補充分支變體均有對應證據，但這些是模型實驗結果，不是實地容量認證。詳查結果見 [audit.json](audit.json)。")
$null = $report.Add('')
$null = $report.Add('## 讀法與分母')
$null = $report.Add('')
$null = $report.Add('11h 局部輪次：總模擬 39,600 秒，捨棄前後各 1,800 秒，中央量測 36,000 秒（10h、20 個 30 分鐘桶）。12h 整線輪次：總模擬 43,200 秒，暖機 5,400 秒（90m），末段保留 1,800 秒（30m），中央量測仍為 36,000 秒（10h、20 個 30 分鐘桶）。')
$null = $report.Add('理論列/h 同時列出手冊公式的整數截斷值與未截斷 `3600 / design headway`；實際列/h 是事件計數除以中央 10h。列車通過量不是實際旅客吞吐量，旅客容量僅能在假設每列載客數時做乘算，沒有模擬上下車、載客率或擁擠。')
$null = $report.Add('整線的四類安全事件（Collision、PredictedCollision、StationStopViolation、SignalAuthorityViolation）是全 12h 事件計數，不能與中央 10h 的 throughput 分母混用。')
$null = $report.Add('')
$null = $report.Add('來源：[2019 臺灣鐵道容量手冊 PDF](../../../../rea-evaluation/urcs-capacity/evidence/taiwan-rail-capacity-2019.pdf)；整線來源與限制見 [handbook-network manifest](handbook-network/manifest.json)，折返來源見 [case02 manifest](front-shards/case02/turnbacks/manifest.json) 與 [case05 manifest](front-shards/case05/turnbacks/manifest.json)，銜接點來源見各 [junction manifest](junctions-shards/r0.0/junctions/case06-r0.0.manifest.json)。重建工具與 App build 證據分別在 [handbook-completion-tools](../../handbook-completion-tools/) 與 [handbook-completion-build](../../handbook-completion-build/)。完整重現步驟見 [REPRODUCE.md](REPRODUCE.md)，整線警示邊界見 [handbook-network-warning-review](../../handbook-network-warning-review/REPORT.md)。')
$null = $report.Add('')
$null = $report.Add('## 11h 局部：previous-cases 8 方向')
$null = $report.Add('')
$null = $report.Add('| 案例/方向 | h(s) | 理論截斷列/h | 理論未截斷 3600/h | 實測列/h | span列/h | pending drift | 延誤前→後(s) | 四類安全 |')
$null = $report.Add('|---|---:|---:|---:|---:|---:|---:|---:|---|')
foreach ($row in $previousRows) { $null = $report.Add("| $(Escape-Md $row.scenario) | $(F $row.inputHeadwaySeconds 3) | $(I $row.theoryLineCapacityPerHourTruncated) | $(F $row.theoryLineCapacityPerHourUntruncated 3) | $(F $row.actualPerHour 1) | $(F $row.actualSpanPerHour 2) | $(I $row.pendingDrift) | $(F $row.delayFirst 3)→$(F $row.delayLast 3) | $(Safety-Text $row.safety) |") }
$null = $report.Add('')
$null = $report.Add('## 11h 局部：折返 2 輪')
$null = $report.Add('')
$null = $report.Add('| 案例 | h(s) | 理論截斷列/h | 理論未截斷 3600/h | 實測回程列/h | pending drift | 資源等待 episodes | 四類安全 |')
$null = $report.Add('|---|---:|---:|---:|---:|---:|---:|---|')
foreach ($row in $turnbackRows) { $null = $report.Add("| $(Escape-Md $row.fixture) | $(F $row.inputHeadwaySeconds 3) | $(I $row.theoryLineCapacityPerHourTruncated) | $(F $row.theoryLineCapacityPerHourUntruncated 3) | $(F $row.returnPerHour 1) | $(I $row.pendingDrift) | $(I $row.waitingEpisodes) | $(Safety-Text $row.safety) |") }
$null = $report.Add('')
$null = $report.Add('## 11h 局部：points 8 方向')
$null = $report.Add('')
$null = $report.Add('| case/point/方向 | h(s) | 理論截斷 | 理論未截斷 3600/h | 實測列/h（事件計數/10h） | span列/h | 中位 h(s) | pending drift | 延誤前→後(s) | 四類安全 |')
$null = $report.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|---|')
foreach ($row in $pointRows) { $null = $report.Add("| $(Escape-Md "$($row.caseId)/$($row.sourcePointId)/$($row.direction)") | $(F $row.inputHeadwaySeconds 3) | $(I $row.theoryLineCapacityPerHourTruncated) | $(F $row.theoryLineCapacityPerHourUntruncated 3) | $(F $row.actualPerHour 1) | $(F $row.actualSpanPerHour 2) | $(F $row.actualMedianHeadwaySeconds 2) | $(I $row.pendingDrift) | $(F $row.delayFirst 3)→$(F $row.delayLast 3) | $(Safety-Text $row.safety) |") }
$null = $report.Add('')
$null = $report.Add('## 11h 局部：handbook-s1')
$null = $report.Add('')
$null = $report.Add("case07-s1-front，h=$(F $s1.headway 3)s；理論截斷 $(I $s1.theory.lineCapacityPerHour)/h，未截斷 $(F (3600 / [double]$s1.theory.designHeadwaySeconds) 3)/h；中央 10h arrivals $(F $s1.arrivals.perHour 1)/h、departures $(F $s1.departures.perHour 1)/h；pending drift $(I $s1.pendingDrift)；四類安全：$(Safety-Text $s1Safety)。")
$null = $report.Add('')
$null = $report.Add('## 11h 局部：junctions-shards 11 個 r')
$null = $report.Add('')
$null = $report.Add('`r` 是「主線連續間隔中未插入支線」的比例，不是流量比。每列的主線／支線實測列/h 與 pending drift 都使用中央 10h；安全事件是該 11h 輪次的事件欄位。')
$null = $report.Add('')
$null = $report.Add('| r | h(s) | 理論截斷 | 理論未截斷 3600/h | 主線列/h | 支線列/h | pending主/支 | 延誤平均/最大(s) | 四類安全 |')
$null = $report.Add('|---:|---:|---:|---:|---:|---:|---|---:|---|')
foreach ($row in $junctionRows) { $null = $report.Add("| $(F $row.ratio 1) | $(F $row.theoryDesignHeadwaySeconds 3) | $(I $row.theoryLineCapacityPerHourTruncated) | $(F $row.theoryLineCapacityPerHourUntruncated 3) | $(F $row.actualMainPerHour 1) | $(F $row.actualBranchPerHour 1) | $(I $row.pendingMainDrift)/$(I $row.pendingBranchDrift) | $(F $row.meanDelay 3)/$(F $row.maxDelay 3) | $(Safety-Text $row.safety) |") }
$null = $report.Add('')
$null = $report.Add('## 12h 整線：S1/S7/S10/S14 雙向點位')
$null = $report.Add('')
$null = $report.Add('下表每個點位方向列出中央 10h 到達與離開的 `n/median/span`（n 為列數，median headway 與 span headway 為秒）。延誤是整線量測窗內已出發班次的前半→後半平均起點延誤；pending 是中央窗起點→終點。端點方向的零 departures 是方向折返的資料形狀：S1 Inbound 與 S14 Outbound 應以 arrivals 比較，不能解讀為該方向沒有列車。')
$null = $report.Add('')
$null = $report.Add('| case | h(s) | fleet | S1 Out A/D；In A/D | S7 Out A/D；In A/D | S10 Out A/D；In A/D | S14 Out A/D；In A/D | 延誤前→後(s) | pending drift |')
$null = $report.Add('|---|---:|---:|---|---|---|---|---:|---:|')
foreach ($row in $networkRows) {
    $cells = [ordered]@{}
    foreach ($station in $NetworkStations) {
        $out = $networkPointRows | Where-Object { $_.caseName -eq $row.caseName -and $_.station -eq $station -and $_.direction -eq 'Outbound' } | Select-Object -First 1
        $in = $networkPointRows | Where-Object { $_.caseName -eq $row.caseName -and $_.station -eq $station -and $_.direction -eq 'Inbound' } | Select-Object -First 1
        $cells[$station] = "O A($(Point-Cell $out))/D($(Point-Cell $out 'departures')); I A($(Point-Cell $in))/D($(Point-Cell $in 'departures'))"
    }
    $null = $report.Add("| $(Escape-Md $row.caseName) | $(F $row.inputHeadwaySeconds 3) | $(I $row.finiteFleet) | $(Escape-Md $cells.S1) | $(Escape-Md $cells.S7) | $(Escape-Md $cells.S10) | $(Escape-Md $cells.S14) | $(F $row.delayFirst 3)→$(F $row.delayLast 3) | $(I $row.pendingDrift) |")
}
$null = $report.Add('')
$null = $report.Add('## 150 秒方案流率摘要')
$null = $report.Add('')
$null = $report.Add('下表以中央 10h 的到達事件數除以 10，列出四個 150 秒方案在四個觀測站的 Outbound/Inbound 列/h；安全欄位仍是全 12h 計數。')
$null = $report.Add('')
$null = $report.Add('| case | S1 O/I 列/h | S7 O/I 列/h | S10 O/I 列/h | S14 O/I 列/h | pending drift | 四類安全摘要 |')
$null = $report.Add('|---|---:|---:|---:|---:|---:|---|')
foreach ($row in $networkRows | Where-Object { $_.caseName -in @('case07-150s','case08-150s','case08b-150s','case08c-150s') }) {
    $rates = [ordered]@{}
    foreach ($station in $NetworkStations) {
        $out = $networkPointRows | Where-Object { $_.caseName -eq $row.caseName -and $_.station -eq $station -and $_.direction -eq 'Outbound' } | Select-Object -First 1
        $in = $networkPointRows | Where-Object { $_.caseName -eq $row.caseName -and $_.station -eq $station -and $_.direction -eq 'Inbound' } | Select-Object -First 1
        $rates[$station] = "$(F ([double]$out.arrivals.count / 10) 1)/$(F ([double]$in.arrivals.count / 10) 1)"
    }
    $null = $report.Add("| $(Escape-Md $row.caseName) | $($rates.S1) | $($rates.S7) | $($rates.S10) | $($rates.S14) | $(I $row.pendingDrift) | $(Safety-Text $row.safety) |")
}
$null = $report.Add('')
$null = $report.Add('## 整線安全與重要觀察')
$null = $report.Add('')
$null = $report.Add('| case | Collision | PredictedCollision | StationStopViolation | SignalAuthorityViolation | fleet invariant |')
$null = $report.Add('|---|---:|---:|---:|---:|---|')
foreach ($row in $networkRows) { $null = $report.Add("| $(Escape-Md $row.caseName) | $(I $row.safety.Collision) | $(I $row.safety.PredictedCollision) | $(I $row.safety.StationStopViolation) | $(I $row.safety.SignalAuthorityViolation) | $(if($row.fleetInvariantPass){'PASS'}else{'FAIL'}) |") }
$null = $report.Add('')
$null = $report.Add('- 07-150：中央 10h 點位約只有 16 列/h，pending drift 為 161；全 12h `PredictedCollision=150248`，未達零預警驗收。')
$null = $report.Add('- 08-150：四個觀測站雙向中央 10h 為 24 列/h，四類安全事件皆為 0。這證明該供給在此模型／假設下可運行，不等於已證明最大容量。')
$null = $report.Add('- 08 nominal（139.648s）：中央量測約 25.8 列/h，但 `PredictedCollision=148`，未達零預警驗收。')
$null = $report.Add('- 08b/08c 的 150s 方案達到約 24 列/h，但分別有約 29k 預測事件，未達零預警驗收；通過量達標不能改寫成安全通過。35s 設計班距 151.443s，也不符合 150s 設計餘裕。')
$null = $report.Add('')
$null = $report.Add('## 模型邊界')
$null = $report.Add('')
$null = $report.Add('- 缺少手冊提供的完整實測站距；整線採 13 個站間各 1.2 km 的 synthetic 假設。0.8 km 與 1.6 km 只做 bounded free-circuit sensitivity，不能當成現地站距。')
$null = $report.Add('- 原始手冊案例與補充分支的拓樸、折返、銜接點、有限車隊與 Engine SHA 已由 audit 核對；previous-cases 的 Engine SHA 取自每個 Schema 8 project 旁的 `.engine-sha256` sidecar。')
$null = $report.Add('- 整線有限車隊與同車接續只代表這個 operational fixture；未模擬旅客上下車、旅客需求、實際信號配置、故障、司機／車隊可用性或完整現地幾何。')
$null = $report.Add('- 公式的 design headway 是供給需求與理論對照；名義班距能運行只證明模型承受該供給，不能單獨證明最大容量。')
$null = $report.Add('- `PredictedCollision` 是前車視為靜止障礙物時的需求大於 ActualGap 且狀態切換的預警計數；本報告以「未達零預警驗收」表述，不把它解讀為已發生實際碰撞，也不據此宣稱整線已被證實不安全。')

$reportPath = Join-Path $outputPath 'REPORT.md'
[IO.File]::WriteAllText($reportPath, [string]::Join([Environment]::NewLine, $report), $utf8)
if ($ValidationErrors.Count -gt 0) {
    throw "完成報告 audit FAIL：$($ValidationErrors.Count) 個驗證錯誤；詳見 $auditPath"
}
Write-Output "REPORT=$reportPath"
Write-Output "AUDIT=$auditPath"
Write-Output "ROUNDS=$allRounds"
