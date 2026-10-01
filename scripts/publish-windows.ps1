param([switch]$Test, [string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Enter-ToolboxDev.ps1')
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = Join-Path $root '.cache\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.cache\nuget-packages'

# Ensure the pinned WebView2 SDK and the development build are available.
& (Join-Path $PSScriptRoot 'build-windows.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Windows development build failed' }

$backend = Join-Path $root 'apps\rust-backend'
& cargo build --release --locked --manifest-path (Join-Path $backend 'Cargo.toml')
if ($LASTEXITCODE -ne 0) { throw 'Rust release build failed' }

$project = Join-Path $root 'apps\windows-shell\Toolbox.Windows.csproj'
$feed = Join-Path $root '.cache\nuget-feed'
$publishConfig = Join-Path $root '.cache\nuget-feed\Publish.NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
Set-Content -LiteralPath $publishConfig -Encoding UTF8 -Value "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear /><add key=`"local`" value=`"$escapedFeed`" /><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`" /></packageSources></configuration>"
& dotnet restore $project -r win-x64 --configfile $publishConfig --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET win-x64 restore failed' }

$distRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'dist'))
$dist = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $distRoot 'windows' }
$resolvedDist = [System.IO.Path]::GetFullPath($dist)
if (-not $resolvedDist.StartsWith($distRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unsafe publish directory'
}
$runningDistProcess = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('FaxToolbox.Windows.exe', 'toolbox-backend.exe') -and
    $_.ExecutablePath -and
    $_.ExecutablePath.StartsWith($resolvedDist + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}
if ($runningDistProcess) { throw 'Close FaxToolbox.Windows.exe before publishing to dist/windows' }
if (Test-Path -LiteralPath $resolvedDist) { Remove-Item -LiteralPath $resolvedDist -Recurse -Force }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
& dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $dist --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET self-contained publish failed' }
# Debug symbols can contain developer-machine paths; API XML files are not runtime assets.
Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Extension -in @('.pdb', '.xml') } |
    Remove-Item -Force

foreach ($relative in @('apps\windows-shell\ui', 'modules\smoke', 'packages\page-agent')) {
    $source = Join-Path $root $relative
    $destination = Join-Path $dist $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}
$diagnosticsScript = Join-Path $dist 'scripts\read-live-diagnostics.ps1'
New-Item -ItemType Directory -Path (Split-Path $diagnosticsScript) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'scripts\read-live-diagnostics.ps1') `
    -Destination $diagnosticsScript -Force
$chaoxingDestination = Join-Path $dist 'modules\chaoxing'
New-Item -ItemType Directory -Path $chaoxingDestination -Force | Out-Null
foreach ($name in @('module.json', 'catalog-reader.js', 'homework-reader.js', 'page-adapter.js')) {
    Copy-Item -LiteralPath (Join-Path $root "modules\chaoxing\$name") `
        -Destination (Join-Path $chaoxingDestination $name) -Force
}
$backendDestination = Join-Path $dist 'backend'
New-Item -ItemType Directory -Path $backendDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $backend 'target\release\toolbox-backend.exe') -Destination (Join-Path $backendDestination 'toolbox-backend.exe') -Force

$executable = Join-Path $dist 'FaxToolbox.Windows.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Published EXE is missing' }
if ($Test) {
    $output = Join-Path $root '.cache\published-smoke.txt'
    Remove-Item -LiteralPath $output, "$output.trace" -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath $executable -ArgumentList @('--smoke', $output) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(30000)) {
        Stop-Process -Id $process.Id -Force
        throw 'Published EXE smoke timed out'
    }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output) -or -not ((Get-Content -LiteralPath $output -Raw).StartsWith('PASS'))) {
        if (Test-Path -LiteralPath "$output.trace") { Get-Content -LiteralPath "$output.trace" }
        if (Test-Path -LiteralPath $output) { Get-Content -LiteralPath $output }
        throw 'Published EXE smoke failed'
    }
    Get-Content -LiteralPath $output
}
Write-Host "Windows EXE ready: $executable"
