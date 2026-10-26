param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version = '5.0'
$hash = '44c10d9e82f20ea67227d14d37bf9fbe3603117c5736df3f514544a02ba20a73'
$archive = Join-Path $root ".cache\scrcpy-win64-v$version.zip"
$destination = Join-Path $root 'runtime\scrcpy'
if ((Test-Path -LiteralPath (Join-Path $destination 'scrcpy.exe')) -and
    (Test-Path -LiteralPath (Join-Path $destination 'LICENSE-scrcpy.txt')) -and
    (Test-Path -LiteralPath (Join-Path $destination 'runtime-version.txt')) -and
    ((Get-Content -LiteralPath (Join-Path $destination 'runtime-version.txt') -Raw).Trim() -eq $version)) { return }
New-Item -ItemType Directory -Path (Split-Path $archive) -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://github.com/Genymobile/scrcpy/releases/download/v$version/scrcpy-win64-v$version.zip" -OutFile $archive -TimeoutSec 120
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) {
    throw 'scrcpy archive checksum mismatch'
}
$stage = Join-Path $root ".cache\scrcpy-v$version"
Expand-Archive -LiteralPath $archive -DestinationPath $stage -Force
$source = Join-Path $stage "scrcpy-win64-v$version"
if (-not (Test-Path -LiteralPath (Join-Path $source 'scrcpy.exe'))) { throw 'scrcpy executable missing' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/Genymobile/scrcpy/v$version/LICENSE" -OutFile (Join-Path $destination 'LICENSE-scrcpy.txt') -TimeoutSec 30
Set-Content -LiteralPath (Join-Path $destination 'runtime-version.txt') -Value $version -Encoding ASCII
Write-Host "scrcpy $version ready: $destination"
