$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    dotnet restore MrtRouteSimulator.slnx --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
    dotnet build MrtRouteSimulator.slnx -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/MrtRouteSimulator.Tests/MrtRouteSimulator.Tests.csproj -c Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Engine regression failed' }
    dotnet run --project tests/MrtRouteSimulator.WpfTests/MrtRouteSimulator.WpfTests.csproj -c Release --no-build --no-restore -- $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'WPF construction regression failed' }
}
finally { Pop-Location }
