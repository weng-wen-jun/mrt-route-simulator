[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $AppDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $AppVersion,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $DataVersion,

    [Parameter(Mandatory = $true)]
    [string] $SamplesDirectory,

    [ValidateRange(1, 2147483647)]
    [int] $SchemaVersion = 8
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression

$Product = 'MrtRouteSimulator'
$ApplicationExe = 'MRT路線進出站時間模擬器.exe'
$InstallationReceipt = 'mrt-installed-files.json'
$MaximumFiles = 10000
$MaximumPackageBytes = 512L * 1024L * 1024L
$MaximumExpandedBytes = 2L * 1024L * 1024L * 1024L

function Get-FullPath([string] $Path) {
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-ReparsePoint([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    return (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Assert-NoReparseTree([string] $Root) {
    if (Test-ReparsePoint $Root) {
        throw "來源不可是 reparse point：$Root"
    }

    $pending = [System.Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (Test-ReparsePoint $entry.FullName) {
                throw "來源樹不可包含 reparse point：$($entry.FullName)"
            }
            if ($entry.PSIsContainer) {
                $pending.Enqueue($entry.FullName)
            }
        }
    }
}

function Assert-NoReparseAncestors([string] $Path) {
    $current = Get-FullPath $Path
    while ($null -ne $current) {
        if (Test-Path -LiteralPath $current) {
            if (Test-ReparsePoint $current) {
                throw "路徑不可經過 reparse point：$current"
            }
        }
        $parent = [System.IO.Directory]::GetParent($current)
        if ($null -eq $parent) { break }
        $current = $parent.FullName
    }
}

function Test-PathWithin([string] $Path, [string] $Root) {
    $pathFull = [System.IO.Path]::TrimEndingDirectorySeparator((Get-FullPath $Path))
    $rootFull = [System.IO.Path]::TrimEndingDirectorySeparator((Get-FullPath $Root))
    return $pathFull.Equals($rootFull, [System.StringComparison]::OrdinalIgnoreCase) -or
        $pathFull.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-OutputDoesNotOverlapSources([string] $Output, [string[]] $Sources) {
    foreach ($source in $Sources) {
        if (Test-PathWithin $Output $source -or Test-PathWithin $source $Output) {
            throw "輸出目錄不可與來源目錄重疊：$Output / $source"
        }
    }
}

function Assert-SafeRelativePath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Length -gt 240 -or
        $Path.Contains('\') -or $Path.Contains(':') -or $Path.StartsWith('/') -or
        $Path.IndexOfAny([char[]]([char]0..[char]31)) -ge 0) {
        throw "更新檔案路徑不合法：$Path"
    }

    foreach ($part in $Path.Split('/')) {
        if ($part.Length -eq 0 -or $part -in @('.', '..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or
            $part.IndexOfAny([char[]]'< >"|?*'.Replace(' ', '')) -ge 0) {
            throw "更新路徑含不安全的區段：$Path"
        }
        $stem = $part.Split('.')[0]
        if ($stem -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
            throw "更新路徑含 Windows 裝置名稱：$Path"
        }
    }
    return $Path
}

function Test-AllowedApplicationPath([string] $Path) {
    try { Assert-SafeRelativePath $Path | Out-Null } catch { return $false }
    $parts = $Path.Split('/')
    if ($parts.Count -gt 1 -and $parts[0] -cnotin @('runtimes', 'mcp', 'updater')) { return $false }
    $name = $parts[$parts.Count - 1]
    if ($parts.Count -eq 1 -and $name -cin @('README.md', 'LICENSE.txt')) { return $true }
    return $name.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase) -or
        $name.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase) -or
        $name.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) -or
        $name.EndsWith('.deps.json', [System.StringComparison]::OrdinalIgnoreCase) -or
        $name.EndsWith('.runtimeconfig.json', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-AllowedDataPath([string] $Path) {
    try { Assert-SafeRelativePath $Path | Out-Null } catch { return $false }
    return $Path.EndsWith('.mrtsim.json', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.Equals('README.md', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.EndsWith('/README.md', [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-UniqueAndNonConflictingPaths([object[]] $Entries) {
    if ($Entries.Count -eq 0 -or $Entries.Count -gt $MaximumFiles) {
        throw "更新檔案數量不合法：$($Entries.Count)"
    }
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    [long]$expanded = 0
    foreach ($entry in $Entries) {
        if (-not $names.Add($entry.Path)) {
            throw "更新清單有重複路徑：$($entry.Path)"
        }
        if ($entry.Size -lt 0 -or $entry.Size -gt $MaximumExpandedBytes) {
            throw "更新檔案大小不合法：$($entry.Path)"
        }
        if ([long]$entry.Size -gt ($MaximumExpandedBytes - $expanded)) {
            throw '更新解壓大小超過上限。'
        }
        $expanded += [long]$entry.Size
    }
    foreach ($entry in $Entries) {
        $parts = $entry.Path.Split('/')
        for ($i = 1; $i -lt $parts.Count; $i++) {
            if ($names.Contains(($parts[0..($i - 1)] -join '/'))) {
                throw "更新清單檔案與目錄互相衝突：$($entry.Path)"
            }
        }
    }
}

function Get-FileRecord([string] $Root, [string] $RelativePath) {
    Assert-SafeRelativePath $RelativePath | Out-Null
    $relativeOs = $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $full = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $relativeOs))
    $rootFull = [System.IO.Path]::TrimEndingDirectorySeparator((Get-FullPath $Root))
    if (-not $full.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "檔案路徑超出來源目錄：$RelativePath"
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        throw "找不到檔案：$full"
    }
    if (Test-ReparsePoint $full) {
        throw "檔案不可是 reparse point：$full"
    }
    $item = Get-Item -LiteralPath $full -Force
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    return [pscustomobject]@{ Path = $RelativePath; Source = $full; Sha256 = $hash; Size = [int64]$item.Length }
}

function Get-ApplicationEntries([string] $Root) {
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = ([System.IO.Path]::GetRelativePath($Root, $file.FullName)).Replace('\', '/')
        if (Test-AllowedApplicationPath $relative) {
            $entries.Add((Get-FileRecord $Root $relative))
        }
    }
    $result = @($entries | Sort-Object -Property Path)
    Assert-UniqueAndNonConflictingPaths $result
    if (-not ($result | Where-Object { $_.Path.Equals($ApplicationExe, [System.StringComparison]::Ordinal) })) {
        throw "建置目錄缺少主程式：$ApplicationExe"
    }
    if (-not ($result | Where-Object { $_.Path.Equals('updater/MrtRouteSimulator.Updater.dll', [System.StringComparison]::OrdinalIgnoreCase) })) {
        throw '建置目錄缺少 updater/MrtRouteSimulator.Updater.dll，無法產出完整可更新 portable。'
    }
    return $result
}

function Get-SampleEntries([string] $Root) {
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)) {
        $relative = ([System.IO.Path]::GetRelativePath($Root, $file.FullName)).Replace('\', '/')
        if ($relative.EndsWith('.mrtsim.json', [System.StringComparison]::OrdinalIgnoreCase) -or
            $file.Name.Equals('README.md', [System.StringComparison]::OrdinalIgnoreCase)) {
            $packagePath = "samples/$relative"
            if (-not (Test-AllowedDataPath $packagePath)) {
                throw "範例檔案不符合資料更新 allowlist：$packagePath"
            }
            $record = Get-FileRecord $Root $relative
            $entries.Add([pscustomobject]@{ Path = $packagePath; Source = $record.Source; Sha256 = $record.Sha256; Size = [int64]$record.Size })
        }
    }
    $result = @($entries | ForEach-Object {
        [pscustomobject]@{ Path = $_.Path; Source = $_.Source; Sha256 = $_.Sha256; Size = [int64]$_.Size }
    } | Sort-Object -Property Path)
    Assert-UniqueAndNonConflictingPaths $result
    if (-not ($result | Where-Object { $_.Path.EndsWith('.mrtsim.json', [System.StringComparison]::OrdinalIgnoreCase) })) {
        throw 'SamplesDirectory 沒有任何 .mrtsim.json。'
    }
    return $result
}

function Copy-EntryToRoot([object] $Entry, [string] $Root, [string] $SourceRoot) {
    $relative = $Entry.Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $destination = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($Root, $relative))
    $rootFull = [System.IO.Path]::TrimEndingDirectorySeparator((Get-FullPath $Root))
    if (-not $destination.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "輸出檔案路徑超出 staging：$($Entry.Path)"
    }
    $parent = [System.IO.Path]::GetDirectoryName($destination)
    if ($null -eq $parent) { throw "無法建立輸出路徑：$($Entry.Path)" }
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
    $source = if ($Entry.PSObject.Properties.Name -contains 'SourceRootRelative') {
        [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($SourceRoot, $Entry.SourceRootRelative.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
    } else { $Entry.Source }
    [System.IO.File]::Copy($source, $destination, $false)
    $copied = Get-FileRecord $Root $Entry.Path
    if ($copied.Size -ne $Entry.Size -or $copied.Sha256 -ne $Entry.Sha256) {
        throw "staging 檔案與來源 hash/size 不一致：$($Entry.Path)"
    }
}

function New-ZipFromEntries([string] $Root, [object[]] $Entries, [string] $ZipPath) {
    $zipStream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($entry in @($Entries | Sort-Object -Property Path)) {
                $archiveEntry = $archive.CreateEntry($entry.Path, [System.IO.Compression.CompressionLevel]::Optimal)
                $source = [System.IO.Path]::Combine($Root, $entry.Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
                $input = [System.IO.File]::OpenRead($source)
                try {
                    $output = $archiveEntry.Open()
                    try { $input.CopyTo($output) } finally { $output.Dispose() }
                } finally { $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $zipStream.Dispose() }
}

function Write-Utf8Json([string] $Path, [object] $Value) {
    $json = $Value | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.UTF8Encoding]::new($false))
}

function New-Manifest([string] $Kind, [string] $Version, [string] $MinimumAppVersion, [int] $Schema, [string] $PackageName, [string] $PackagePath, [object[]] $Entries) {
    $packageInfo = Get-FileRecord ([System.IO.Path]::GetDirectoryName($PackagePath)) ([System.IO.Path]::GetFileName($PackagePath))
    if ($packageInfo.Size -le 0 -or $packageInfo.Size -gt $MaximumPackageBytes) {
        throw "更新套件大小不合法：$($packageInfo.Size)"
    }
    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in @($Entries | Sort-Object -Property Path)) {
        $files.Add([ordered]@{ path = $entry.Path; sha256 = $entry.Sha256.ToLowerInvariant(); size = [int64]$entry.Size })
    }
    return [ordered]@{
        formatVersion = 1
        product = $Product
        kind = $Kind
        version = $Version
        minimumAppVersion = $MinimumAppVersion
        schemaVersion = $Schema
        packageFileName = $PackageName
        packageSha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        packageSize = [int64]$packageInfo.Size
        files = $files.ToArray()
    }
}

$appRoot = Get-FullPath $AppDirectory
$samplesRoot = Get-FullPath $SamplesDirectory
$outputRoot = Get-FullPath $OutputDirectory

foreach ($input in @(@{ Path = $appRoot; Name = 'AppDirectory' }, @{ Path = $samplesRoot; Name = 'SamplesDirectory' })) {
    if (-not (Test-Path -LiteralPath $input.Path -PathType Container)) {
        throw "$($input.Name) 不存在或不是資料夾：$($input.Path)"
    }
    Assert-NoReparseAncestors $input.Path
    Assert-NoReparseTree $input.Path
}

Assert-NoReparseAncestors ([System.IO.Path]::GetDirectoryName($outputRoot))
Assert-OutputDoesNotOverlapSources $outputRoot @($appRoot, $samplesRoot)
if (Test-Path -LiteralPath $outputRoot) {
    throw "輸出目錄已存在，為避免破壞既有內容而拒絕：$outputRoot"
}

$appEntries = Get-ApplicationEntries $appRoot
$sampleEntries = Get-SampleEntries $samplesRoot
$allSampleFiles = @(Get-ChildItem -LiteralPath $samplesRoot -File -Recurse -Force)

[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$stagingRoot = [System.IO.Path]::Combine($outputRoot, ".staging-$([Guid]::NewGuid().ToString('N'))")
$appStage = [System.IO.Path]::Combine($stagingRoot, 'application')
$dataStage = [System.IO.Path]::Combine($stagingRoot, 'data')
$portableStage = [System.IO.Path]::Combine($stagingRoot, 'portable')

try {
    [System.IO.Directory]::CreateDirectory($appStage) | Out-Null
    [System.IO.Directory]::CreateDirectory($dataStage) | Out-Null
    [System.IO.Directory]::CreateDirectory($portableStage) | Out-Null

    foreach ($entry in $appEntries) {
        Copy-EntryToRoot $entry $appStage $appRoot
        Copy-EntryToRoot $entry $portableStage $appRoot
    }
    foreach ($entry in $sampleEntries) {
        $stageEntry = [pscustomobject]@{ Path = $entry.Path; Source = $entry.Source; Sha256 = $entry.Sha256; Size = [int64]$entry.Size }
        Copy-EntryToRoot $stageEntry $dataStage $samplesRoot
    }

    $portableSamples = [System.IO.Path]::Combine($portableStage, 'samples')
    foreach ($file in $allSampleFiles) {
        $relative = ([System.IO.Path]::GetRelativePath($samplesRoot, $file.FullName)).Replace('\', '/')
        Assert-SafeRelativePath $relative | Out-Null
        $target = [System.IO.Path]::Combine($portableSamples, $relative.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
        [System.IO.File]::Copy($file.FullName, $target, $false)
    }

    $appPackageName = "mrt-application-update-v$AppVersion.zip"
    $dataPackageName = "mrt-data-update-v$DataVersion.zip"
    $appPackagePath = [System.IO.Path]::Combine($outputRoot, $appPackageName)
    $dataPackagePath = [System.IO.Path]::Combine($outputRoot, $dataPackageName)
    New-ZipFromEntries $appStage $appEntries $appPackagePath
    New-ZipFromEntries $dataStage $sampleEntries $dataPackagePath

    $applicationManifest = New-Manifest 'application' $AppVersion '0.0.0' $SchemaVersion $appPackageName $appPackagePath $appEntries
    $dataManifest = New-Manifest 'data' $DataVersion $AppVersion $SchemaVersion $dataPackageName $dataPackagePath $sampleEntries
    Write-Utf8Json ([System.IO.Path]::Combine($outputRoot, 'mrt-application-update.json')) $applicationManifest
    Write-Utf8Json ([System.IO.Path]::Combine($outputRoot, 'mrt-data-update.json')) $dataManifest

    $receipt = [ordered]@{
        formatVersion = 1
        product = $Product
        version = $AppVersion
        files = @($appEntries | Sort-Object -Property Path | ForEach-Object { $_.Path })
    }
    Write-Utf8Json ([System.IO.Path]::Combine($portableStage, $InstallationReceipt)) $receipt

    $portableFinal = [System.IO.Path]::Combine($outputRoot, 'portable')
    [System.IO.Directory]::Move($portableStage, $portableFinal)
    Write-Output "Built update packages in $outputRoot"
    Write-Output "Application: $appPackageName + mrt-application-update.json"
    Write-Output "Data: $dataPackageName + mrt-data-update.json"
    Write-Output "Portable: $portableFinal"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
