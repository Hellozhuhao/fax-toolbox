param([switch]$Test)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Enter-ToolboxDev.ps1')
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = Join-Path $root '.cache\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.cache\nuget-packages'

$version = '1.0.4258.31'
$archive = Join-Path $root ".cache\webview2-$version.zip"
$expectedHash = '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9'
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$version" -OutFile $archive -TimeoutSec 60
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'WebView2 SDK package checksum mismatch'
}
$feed = Join-Path $root '.cache\nuget-feed'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$package = Join-Path $feed "Microsoft.Web.WebView2.$version.nupkg"
if (-not (Test-Path -LiteralPath $package)) { Copy-Item -LiteralPath $archive -Destination $package }
$nugetConfig = Join-Path $feed 'NuGet.Config'
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
Set-Content -LiteralPath $nugetConfig -Encoding UTF8 -Value "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear /><add key=`"local`" value=`"$escapedFeed`" /></packageSources></configuration>"

$backend = Join-Path $root 'apps\rust-backend'
& cargo build --locked --manifest-path (Join-Path $backend 'Cargo.toml')
if ($LASTEXITCODE -ne 0) { throw 'Rust build failed' }
if ($Test) {
    & cargo test --locked --manifest-path (Join-Path $backend 'Cargo.toml')
    if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed' }
}

$project = Join-Path $root 'apps\windows-shell\Toolbox.Windows.csproj'
& dotnet restore $project --configfile $nugetConfig --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET restore failed' }
& dotnet build $project --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw '.NET build failed' }

if ($Test) {
    $executable = Join-Path $root 'apps\windows-shell\bin\Debug\net10.0-windows\FaxToolbox.Windows.exe'
    foreach ($mode in @('--smoke', '--course-smoke', '--course-video-smoke', '--course-document-smoke', '--course-challenge-smoke', '--course-pause-smoke', '--course-login-smoke')) {
        $output = Join-Path $root ('.cache\windows-' + $mode.TrimStart('-') + '.txt')
        Remove-Item -LiteralPath $output, "$output.trace" -ErrorAction SilentlyContinue
        $process = Start-Process -FilePath $executable -ArgumentList @($mode, $output) -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(30000)) {
            Stop-Process -Id $process.Id -Force
            throw "$mode end-to-end smoke timed out"
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output) -or
            -not ((Get-Content -LiteralPath $output -Raw).StartsWith('PASS'))) {
            if (Test-Path -LiteralPath "$output.trace") { Get-Content -LiteralPath "$output.trace" }
            if (Test-Path -LiteralPath $output) { Get-Content -LiteralPath $output }
            throw "$mode end-to-end smoke failed"
        }
        Get-Content -LiteralPath $output
    }
}

Write-Host 'Windows shell and Rust backend are ready.'
