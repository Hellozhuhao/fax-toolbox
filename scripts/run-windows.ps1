$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$executable = Join-Path $root 'apps\windows-shell\bin\Debug\net10.0-windows\FaxToolbox.Windows.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Windows shell is not built; run scripts/build-windows.ps1 first'
}
& $executable
exit $LASTEXITCODE
