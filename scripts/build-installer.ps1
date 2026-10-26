param(
    [switch]$SkipPublish,
    [switch]$Test,
    [string]$InnoCompiler
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dist = Join-Path $root 'dist'
$published = Join-Path $dist 'windows'
$cache = Join-Path $root '.cache\installer-tools'
$script = Join-Path $PSScriptRoot 'installer.iss'
$bootstrapper = Join-Path $cache 'MicrosoftEdgeWebView2Setup.exe'
$release = Join-Path $dist 'FaxToolbox-Setup-v0.1.0-preview.2-win-x64.exe'

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1') -Test:$Test
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
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

# The installer includes only published runtime files. Reject personal data and
# development artifacts even if they accidentally reached dist/windows.
$blockedNames = @('vault.json', 'answers.json', 'catalog.json', 'ai-settings.json',
    'cookies', '.env', 'user-data', 'profiles', 'WebView2')
$blockedExtensions = @('.pdb', '.sqlite', '.db', '.trace')
foreach ($item in Get-ChildItem -LiteralPath $published -Recurse -Force) {
    if ($item.Name -in $blockedNames -or $item.Extension -in $blockedExtensions -or
        $item.Name -like '.env.*' -or $item.Name -like 'cookies*') {
        throw "Private or development file found in publish directory: $($item.FullName)"
    }
}

New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (-not (Test-Path -LiteralPath $bootstrapper -PathType Leaf)) {
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
}
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or
    $signature.SignerCertificate.Subject -notlike '*O=Microsoft Corporation*') {
    throw 'WebView2 bootstrapper is not signed by Microsoft.'
}

if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $cache 'InnoSetup7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
    )
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $InnoCompiler = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 7 compiler (ISCC.exe) not found. Install JRSoftware.InnoSetup.7 or pass -InnoCompiler.'
}

& $InnoCompiler $script
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $release -PathType Leaf)) {
    throw 'Inno Setup compilation failed.'
}

if ($Test) {
    # A separate AppId keeps the test installation away from a real installation.
    $smokeInstaller = Join-Path $cache 'FaxToolbox-Setup-Smoke.exe'
    & $InnoCompiler '/DSmokeBuild=1' ('/O' + $cache) '/FFaxToolbox-Setup-Smoke' $script
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $smokeInstaller -PathType Leaf)) {
        throw 'Smoke installer compilation failed.'
    }
    $installDir = [IO.Path]::GetFullPath((Join-Path $cache 'install-smoke'))
    if (-not $installDir.StartsWith($cache + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe smoke install path.' }
    if (Test-Path -LiteralPath $installDir) { throw 'Smoke install directory already exists.' }
    $setup = Start-Process -FilePath $smokeInstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS',
        ('/DIR="' + $installDir + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($setup.ExitCode -ne 0) { throw "Smoke installation failed: $($setup.ExitCode)" }
    $installedExe = Join-Path $installDir 'FaxToolbox.Windows.exe'
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) {
        throw 'Installer did not place the app or uninstaller.'
    }
    $smokeOutput = Join-Path $cache 'installed-smoke.txt'
    Remove-Item -LiteralPath $smokeOutput, "$smokeOutput.trace" -ErrorAction SilentlyContinue
    $app = Start-Process -FilePath $installedExe -ArgumentList @('--smoke', $smokeOutput) `
        -WindowStyle Hidden -Wait -PassThru
    if ($app.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeOutput) -or
        -not ((Get-Content -LiteralPath $smokeOutput -Raw).StartsWith('PASS'))) {
        throw 'Installed application smoke failed.'
    }
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
        '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
    if ($uninstall.ExitCode -ne 0 -or (Test-Path -LiteralPath $installedExe)) {
        throw 'Smoke uninstallation failed.'
    }
    Write-Host 'Installer install/launch/uninstall smoke: PASS'
}

Write-Host "Installer ready: $release"
