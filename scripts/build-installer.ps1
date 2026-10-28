param([switch]$SkipPublish, [switch]$Test, [string]$InnoCompiler, [string]$PublishedDirectory)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'Enter-ToolboxDev.ps1')
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = Join-Path $root '.cache\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.cache\nuget-packages'
$version = Get-Content -LiteralPath (Join-Path $root 'release-version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$dist = Join-Path $root 'dist'
$published = if ($PublishedDirectory) { [IO.Path]::GetFullPath($PublishedDirectory) } else { Join-Path $dist 'windows-beta' }
$cache = Join-Path $root '.cache\installer-tools'
$iss = Join-Path $PSScriptRoot 'installer.iss'
if (-not $SkipPublish) { & (Join-Path $PSScriptRoot 'publish-windows.ps1') -OutputDirectory $published; if ($LASTEXITCODE) { throw 'Windows publish failed' } }
foreach ($relative in @('FaxToolbox.Windows.exe','backend\toolbox-backend.exe','apps\windows-shell\ui\index.html','modules\chaoxing\page-adapter.js','modules\proxy-autoswitch\mihomo-official.zip','packages\page-agent\page-agent.js')) {
    if (-not (Test-Path -LiteralPath (Join-Path $published $relative) -PathType Leaf)) { throw "Missing published runtime: $relative" }
}
if (-not (Test-Path -LiteralPath $published -PathType Container)) {
    throw 'Published Windows files are missing. Run scripts/publish-windows.ps1 first.'
}
foreach ($relative in @('FaxToolbox.Windows.exe', 'backend\toolbox-backend.exe',
    'apps\windows-shell\ui\index.html', 'modules\chaoxing\page-adapter.js',
    'packages\page-agent\page-agent.js', 'scripts\read-live-diagnostics.ps1',
    'runtime\scrcpy\scrcpy.exe', 'runtime\scrcpy\adb.exe',
    'runtime\scrcpy\scrcpy-server', 'runtime\scrcpy\LICENSE-scrcpy.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $published $relative) -PathType Leaf)) {
        throw "Published Windows files are incomplete: $relative"
    }
}

$blockedNames = @('.cache','.git','vault.json','answers.json','catalog.json','ai-settings.json','cookies','.env','user-data','profiles','WebView2')
$blockedExtensions = @('.pdb','.sqlite','.db','.trace')
foreach ($item in Get-ChildItem -LiteralPath $published -Recurse -Force) {
    if ($item.Name -in $blockedNames -or $item.Extension -in $blockedExtensions -or $item.Name -like '.env.*' -or $item.Name -like 'cookies*') { throw "Private or test data found in publish directory: $($item.FullName)" }
}
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (-not $InnoCompiler) {
    $candidates=@((Join-Path $cache 'InnoSetup7\ISCC.exe'),(Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'))
    $command=Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $InnoCompiler=$candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $InnoCompiler) { throw 'Install Inno Setup 7 or pass -InnoCompiler.' }
$payloadName='FaxToolbox-Payload-v'+$version.version+'-win-x64'
& $InnoCompiler ('/DAppVersion='+$version.version) ('/DAppFileVersion='+$version.fileVersion) ('/DPublishedDirectory='+$published) ('/O'+$cache) ('/F'+$payloadName) $iss
if ($LASTEXITCODE) { throw 'Inno payload compilation failed' }
$payload=Join-Path $cache ($payloadName+'.exe')
$payloadHash=(Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$project=Join-Path $root 'apps\windows-installer\Toolbox.Installer.csproj'
$config=Join-Path $root '.cache\nuget-feed\Publish.NuGet.Config'
& dotnet restore $project -r win-x64 --configfile $config --nologo
if ($LASTEXITCODE) { throw 'Bootstrap restore failed' }
$bootstrapDir=Join-Path $cache 'bootstrap'
& dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ('-p:Version='+$version.version) ('-p:FileVersion='+$version.fileVersion) ('-p:PayloadPath='+$payload) ('-p:PayloadSha256='+$payloadHash) -o $bootstrapDir --nologo
if ($LASTEXITCODE) { throw 'Animated bootstrap publish failed' }
$release=Join-Path $dist ('FaxToolbox-Setup-v'+$version.version+'-win-x64.exe')
Copy-Item -LiteralPath (Join-Path $bootstrapDir 'FaxToolbox.Setup.exe') -Destination $release -Force
if ($Test) {
    $dependency=Join-Path $cache 'MicrosoftEdgeWebView2Setup.exe'
    if (-not (Test-Path -LiteralPath $dependency)) { Invoke-WebRequest -UseBasicParsing -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $dependency -TimeoutSec 120 }
    & dotnet restore (Join-Path $root 'tests\windows-installer\InstallerTests.csproj') --configfile $config --nologo
    if ($LASTEXITCODE) { throw 'Installer tests restore failed' }
    & dotnet run --project (Join-Path $root 'tests\windows-installer\InstallerTests.csproj') --no-restore -- $dependency
    if ($LASTEXITCODE) { throw 'Installer dependency tests failed' }
    & $InnoCompiler '/DSmokeBuild=1' ('/DAppVersion='+$version.version) ('/DAppFileVersion='+$version.fileVersion) ('/DPublishedDirectory='+$published) ('/O'+$cache) '/FFaxToolbox-Payload-Smoke' $iss
    if ($LASTEXITCODE) { throw 'Smoke payload compilation failed' }
    $smoke=Join-Path $cache 'FaxToolbox-Payload-Smoke.exe'
    $smokeHash=(Get-FileHash -LiteralPath $smoke -Algorithm SHA256).Hash
    $testBootstrap=Join-Path $cache 'bootstrap-smoke'
    & dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ('-p:PayloadPath='+$payload) ('-p:PayloadSha256='+$payloadHash) ('-p:SmokePayloadPath='+$smoke) ('-p:SmokePayloadSha256='+$smokeHash) -o $testBootstrap --nologo
    if ($LASTEXITCODE) { throw 'Smoke bootstrap build failed' }
    $installDir=Join-Path $cache ('install smoke 中文 '+[Guid]::NewGuid().ToString('N'))
    $result=Join-Path $cache 'bootstrap-install-smoke.txt'
    $setup=Start-Process -FilePath (Join-Path $testBootstrap 'FaxToolbox.Setup.exe') -ArgumentList @('--install-smoke',('"'+$installDir+'"'),('"'+$result+'"')) -WindowStyle Hidden -PassThru
    $handle=$setup.Handle
    if (-not $setup.WaitForExit(120000)) { throw 'Installer smoke timed out; inspect its process and log before retrying.' }
    if ($setup.ExitCode -ne 0) { Get-Content -LiteralPath $result; throw 'Bootstrap installation failed' }
    $output=Join-Path $cache 'beta-installed-agent-smoke.txt'
    $app=Start-Process -FilePath (Join-Path $installDir 'FaxToolbox.Windows.exe') -ArgumentList @('--agent-smoke',('"'+$output+'"')) -WindowStyle Hidden -PassThru
    $handle=$app.Handle
    if (-not $app.WaitForExit(60000)) { Stop-Process -Id $app.Id -Force; throw 'Installed app smoke timed out' }
    if ($app.ExitCode -ne 0) { Get-Content -LiteralPath $output; throw 'Installed application smoke failed' }
    $uninstall=Start-Process -FilePath (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -PassThru
    $handle=$uninstall.Handle
    if (-not $uninstall.WaitForExit(30000) -or $uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $installDir 'FaxToolbox.Windows.exe'))) { throw 'Smoke uninstall failed' }
    Get-Content -LiteralPath $result
    Get-Content -LiteralPath $output
    Write-Host 'PASS installer custom Unicode directory install, application launch and uninstall'
}
Write-Host "Beta installer ready: $release"