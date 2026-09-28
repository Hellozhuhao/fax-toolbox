# Dot-source from PowerShell: . C:\Users\21588\Desktop\toolbox\scripts\Enter-ToolboxDev.ps1
$ErrorActionPreference = 'Stop'
$dotnetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$goBin = Join-Path $env:LOCALAPPDATA 'Programs\Toolbox\Go-1.27.1\go\bin'
$rustBin = Join-Path $env:USERPROFILE '.cargo\bin'
$vsDevCmd = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\Tools\VsDevCmd.bat'

foreach ($file in @((Join-Path $dotnetRoot 'dotnet.exe'),
                   (Join-Path $goBin 'go.exe'),
                   (Join-Path $rustBin 'cargo.exe'),
                   $vsDevCmd)) {
  if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
    throw "Toolchain file missing: $file"
  }
}

# Import the Visual Studio compiler, linker, and SDK environment into this shell.
$vsLines = & cmd.exe /d /s /c "call `"$vsDevCmd`" -arch=x64 >nul && set"
if ($LASTEXITCODE -ne 0) { throw 'Visual Studio developer environment failed' }
$vsPathLine = $vsLines | Where-Object { $_.StartsWith('PATH=', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
if (-not $vsPathLine) { throw 'Visual Studio PATH not found' }
foreach ($line in $vsLines) {
  if ($line.StartsWith('=')) { continue }
  $separator = $line.IndexOf('=')
  if ($separator -le 0) { continue }
  if ($line.Substring(0, $separator) -ieq 'PATH') { continue }
  [Environment]::SetEnvironmentVariable(
    $line.Substring(0, $separator), $line.Substring($separator + 1), 'Process')
}

$env:DOTNET_ROOT = $dotnetRoot
$env:PATH = ($dotnetRoot, $goBin, $rustBin, $vsPathLine.Substring(5)) -join ';'
Write-Host 'Toolbox Windows development environment is ready.'
Write-Host "dotnet: $(& (Join-Path $dotnetRoot 'dotnet.exe') --version)"
Write-Host "go: $(& (Join-Path $goBin 'go.exe') version)"
Write-Host "rustc: $(& (Join-Path $rustBin 'rustc.exe') --version)"
