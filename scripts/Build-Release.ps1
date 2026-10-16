[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')][string] $DataVersion = '1.0.0',
    [ValidatePattern('^10\.0\.[0-9]+$')][string] $RuntimeVersion = '10.0.13'
)

# Run from a clean checkout of the release commit. No Git refs or releases are modified.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output must not exist: $output" }
$dirty = & git -c "safe.directory=$source" -C $source status --porcelain --untracked-files=normal
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Release source must be a clean Git checkout.' }
$revision = (& git -c "safe.directory=$source" -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read release revision.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $source 'Directory.Build.props') -Raw
$appVersion = [string]$props.Project.PropertyGroup.MrtVersion
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid MrtVersion.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$publish = Join-Path $output 'publish'
$app = Join-Path $publish 'application'
$updater = Join-Path $publish 'updater'
$mcp = Join-Path $publish 'mcp'
$config = Join-Path $source 'src/MrtRouteSimulator.Mcp/NuGet.Config'

function Invoke-Dotnet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
foreach ($project in @(
    @{ Path = 'src/MrtRouteSimulator.App/MrtRouteSimulator.App.csproj'; Output = $app },
    @{ Path = 'src/MrtRouteSimulator.Updater/MrtRouteSimulator.Updater.csproj'; Output = $updater },
    @{ Path = 'src/MrtRouteSimulator.Mcp/MrtRouteSimulator.Mcp.csproj'; Output = $mcp }
)) {
    Invoke-Dotnet @('publish', (Join-Path $source $project.Path), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '--configfile', $config, '-o', $project.Output, "-p:RuntimeFrameworkVersion=$RuntimeVersion",
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=None', '-p:DebugSymbols=false')
}
# App's ordinary build target carries a framework-dependent helper. Replace only that generated
# directory with the explicitly self-contained helper published above.
$appUpdater = Join-Path $app 'updater'
if (Test-Path -LiteralPath $appUpdater) {
    $checked = [IO.Path]::GetFullPath($appUpdater)
    if (-not $checked.StartsWith($publish + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe generated directory.' }
    Remove-Item -LiteralPath $checked -Recurse -Force
}
Copy-Item -LiteralPath $updater -Destination $appUpdater -Recurse
Copy-Item -LiteralPath $mcp -Destination (Join-Path $app 'mcp') -Recurse
# The project-reference publish can also expose helper launch files at the app root.
# The supported helper lives entirely in updater/, with its own runtimeconfig and runtime.
foreach ($name in @('MrtRouteSimulator.Updater.exe', 'MrtRouteSimulator.Updater.dll', 'MrtRouteSimulator.Updater.deps.json', 'MrtRouteSimulator.Updater.runtimeconfig.json')) {
    $duplicate = Join-Path $app $name
    if (Test-Path -LiteralPath $duplicate -PathType Leaf) { Remove-Item -LiteralPath $duplicate }
}
foreach ($symbol in @(Get-ChildItem -LiteralPath $app -Recurse -File -Filter '*.pdb')) { Remove-Item -LiteralPath $symbol.FullName }
foreach ($relative in @('', 'mcp', 'updater')) {
    $root = if ($relative) { Join-Path $app $relative } else { $app }
    foreach ($file in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Self-contained runtime missing: $relative/$file" }
    }
    $runtimeConfig = @(Get-ChildItem -LiteralPath $root -File -Filter '*.runtimeconfig.json')
    if ($runtimeConfig.Count -ne 1) { throw "Expected one runtimeconfig: $root" }
    $runtime = (Get-Content -LiteralPath $runtimeConfig[0].FullName -Raw | ConvertFrom-Json -AsHashtable).runtimeOptions
    if ($runtime.ContainsKey('framework') -or $runtime.ContainsKey('frameworks') -or -not $runtime.ContainsKey('includedFrameworks')) { throw "Framework-dependent output: $root" }
    if (@($runtime.includedFrameworks | Where-Object { $_.version -ne $RuntimeVersion }).Count -gt 0) { throw "Unexpected runtime version: $root" }
}

# Preserve licenses and notices from the exact resolved package versions, including runtime packs.
$licenses = Join-Path $app 'third-party-licenses'
[IO.Directory]::CreateDirectory($licenses) | Out-Null
$packages = @{}
foreach ($project in @('MrtRouteSimulator.App', 'MrtRouteSimulator.Mcp', 'MrtRouteSimulator.Updater')) {
    $assets = Get-Content -LiteralPath (Join-Path $source "src/$project/obj/project.assets.json") -Raw | ConvertFrom-Json -AsHashtable
    foreach ($library in $assets.libraries.GetEnumerator()) {
        if ($library.Value.type -ne 'package') { continue }
        $packagePath = $null
        foreach ($packageRoot in $assets.packageFolders.Keys) {
            $candidate = Join-Path $packageRoot $library.Value.path
            if (Test-Path -LiteralPath $candidate -PathType Container) { $packagePath = $candidate; break }
        }
        if (-not $packagePath) { throw "Resolved package absent: $($library.Key)" }
        $packages[$library.Key] = $packagePath
    }
}
foreach ($pack in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $path = Join-Path $env:USERPROFILE ".nuget/packages/$pack/$RuntimeVersion"
    if (-not (Test-Path -LiteralPath $path)) { throw "Runtime package absent: $path" }
    $packages["$pack/$RuntimeVersion"] = $path
}
foreach ($package in @($packages.GetEnumerator() | Sort-Object Name)) {
    $id = $package.Key.Replace('/', '-')
    $nuspec = @(Get-ChildItem -LiteralPath $package.Value -File -Filter '*.nuspec')
    if ($nuspec.Count -ne 1) { throw "Package metadata missing: $id" }
    [xml]$metadata = Get-Content -LiteralPath $nuspec[0].FullName -Raw
    $manager = [Xml.XmlNamespaceManager]::new($metadata.NameTable)
    $manager.AddNamespace('n', $metadata.DocumentElement.NamespaceURI)
    $node = $metadata.SelectSingleNode('/n:package/n:metadata', $manager)
    [IO.File]::WriteAllText((Join-Path $licenses "$id-package-metadata.txt"), $node.OuterXml, [Text.UTF8Encoding]::new($false))
    $documents = @(Get-ChildItem -LiteralPath $package.Value -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES)(\..*)?$' })
    $index = 0
    foreach ($document in $documents) {
        $index++
        Copy-Item -LiteralPath $document.FullName -Destination (Join-Path $licenses "$id-$index-$($document.Name).txt")
    }
    if ($id -like 'ModelContextProtocol*') {
        $repo = $node.SelectSingleNode('n:repository', $manager)
        if ($repo.url -ne 'https://github.com/modelcontextprotocol/csharp-sdk' -or $repo.commit -notmatch '^[0-9a-f]{40}$') { throw "Unexpected MCP license source: $id" }
        foreach ($document in @('LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
            $uri = "https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/$($repo.commit)/$document"
            Invoke-WebRequest -Uri $uri -OutFile (Join-Path $licenses "$id-$document.txt")
        }
    }
    elseif ($documents.Count -eq 0) {
        $license = $node.SelectSingleNode('n:license', $manager)
        $repository = $node.SelectSingleNode('n:repository', $manager)
        if ($null -eq $license -or $license.InnerText -ne 'MIT' -or $null -eq $repository -or
            -not $repository.url.StartsWith('https://github.com/dotnet/', [StringComparison]::Ordinal)) {
            throw "Package requires explicit license review: $id"
        }
        # MIT text for Microsoft packages is included from the exact .NET runtime package;
        # each package's copyright and license declaration remain in its own metadata above.
    }
}
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $app 'LICENSE.txt')
$readme = @"
# MRT 路線進出站時間模擬器 V$appVersion

Windows x64 自含版本，已包含 .NET $RuntimeVersion，不需另行安裝 .NET。
解壓縮完整資料夾後，執行「MRT路線進出站時間模擬器.exe」。請保留所有相依檔案與子資料夾。

- samples/：專案維護範例，可從程式載入。
- mcp/MrtRouteSimulator.Mcp.exe：MCP stdio 主機，使用 --workspace-root 指定允許存取的專案資料夾。
- updater/：供程式內更新使用的獨立自含更新器。
- third-party-licenses/：實際相依套件的授權與第三方聲明。

Source commit: $revision
Application release: v$appVersion
Data release: data-v$DataVersion
"@
[IO.File]::WriteAllText((Join-Path $app 'README.md'), $readme, [Text.UTF8Encoding]::new($false))
$packagesOutput = Join-Path $output 'packages'
& (Join-Path $PSScriptRoot 'Build-UpdatePackages.ps1') -AppDirectory $app -OutputDirectory $packagesOutput -AppVersion $appVersion -DataVersion $DataVersion -SamplesDirectory (Join-Path $source 'samples')
$portable = Join-Path $packagesOutput 'portable'
$zipPath = Join-Path $packagesOutput "MRT-route-simulator-V$appVersion-windows.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($portable, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$sums = @(Get-ChildItem -LiteralPath $packagesOutput -File | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name })
[IO.File]::WriteAllLines((Join-Path $packagesOutput 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
Write-Output "Release commit: $revision"
Write-Output "Release assets: $packagesOutput"
