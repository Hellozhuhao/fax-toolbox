# Dot-source from the repository root: . .\scripts\Enter-ToolboxDev.ps1
$ErrorActionPreference = 'Stop'

function Find-ToolboxExecutable([string]$name, [string[]]$candidates) {
  foreach ($candidate in $candidates) {
    if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
      return (Resolve-Path -LiteralPath $candidate).Path
    }
  }
  $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($command) { return $command.Source }
  return $null
}

$dotnetExe = Find-ToolboxExecutable 'dotnet.exe' @(
  $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
  $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }),
  $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' })
)
$cargoExe = Find-ToolboxExecutable 'cargo.exe' @(
  $(if ($env:CARGO_HOME) { Join-Path $env:CARGO_HOME 'bin\cargo.exe' }),
  $(if ($env:USERPROFILE) { Join-Path $env:USERPROFILE '.cargo\bin\cargo.exe' })
)
$goExe = Find-ToolboxExecutable 'go.exe' @(
  $(if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'Go\bin\go.exe' }),
  $(if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs\Go\bin\go.exe' })
)
if (-not $dotnetExe) { throw '.NET SDK not found. Install .NET 10 SDK or set DOTNET_ROOT.' }
if (-not $cargoExe) { throw 'Rust cargo not found. Install the stable MSVC Rust toolchain.' }

$vsDevCmd = $null
if ($env:VSINSTALLDIR) {
  $candidate = Join-Path $env:VSINSTALLDIR 'Common7\Tools\VsDevCmd.bat'
  if (Test-Path -LiteralPath $candidate -PathType Leaf) { $vsDevCmd = $candidate }
}
if (-not $vsDevCmd) {
  $vswhere = Find-ToolboxExecutable 'vswhere.exe' @(
    $(if (${env:ProgramFiles(x86)}) {
      Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    })
  )
  if ($vswhere) {
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath |
      Select-Object -First 1
    if ($installation) {
      $candidate = Join-Path $installation 'Common7\Tools\VsDevCmd.bat'
      if (Test-Path -LiteralPath $candidate -PathType Leaf) { $vsDevCmd = $candidate }
    }
  }
}
if (-not $vsDevCmd) { throw 'Visual Studio C++ Build Tools not found. Install the MSVC x64 tools and Windows SDK.' }

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

$env:DOTNET_ROOT = Split-Path -Parent $dotnetExe
$pathParts = @((Split-Path -Parent $dotnetExe), (Split-Path -Parent $cargoExe))
if ($goExe) { $pathParts += (Split-Path -Parent $goExe) }
$env:PATH = ($pathParts + $vsPathLine.Substring(5)) -join ';'
Write-Host 'Toolbox Windows development environment is ready.'
Write-Host "dotnet: $(& $dotnetExe --version)"
if ($goExe) { Write-Host "go: $(& $goExe version)" }
else { Write-Host 'go: optional toolchain not found' }
Write-Host "rustc: $(& (Join-Path (Split-Path -Parent $cargoExe) 'rustc.exe') --version)"
