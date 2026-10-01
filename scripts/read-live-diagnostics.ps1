param(
  [ValidateSet('snapshot', 'page')][string]$Command = 'snapshot',
  [string]$PipeName
)

$ErrorActionPreference = 'Stop'
if (-not $PipeName) {
  $directory = Join-Path $env:LOCALAPPDATA 'FaxToolbox\diagnostics-endpoints'
  $endpoints = @(Get-ChildItem -LiteralPath $directory -Filter '*.json' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending)
  foreach ($file in $endpoints) {
    $entry = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if (Get-Process -Id $entry.processId -ErrorAction SilentlyContinue) {
      $PipeName = $entry.pipeName
      break
    }
  }
}
if (-not $PipeName) { throw '没有运行中的诊断端点。请在设置中开启开发者模式。' }

$pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $PipeName,
  [System.IO.Pipes.PipeDirection]::InOut)
try {
  $pipe.Connect(5000)
  $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
  $reader = [System.IO.StreamReader]::new($pipe, [System.Text.Encoding]::UTF8, $true, 1024, $true)
  try {
    $writer.WriteLine($Command)
    $writer.Flush()
    $reader.ReadToEnd() | ConvertFrom-Json | ConvertTo-Json -Depth 100
  } finally {
    $reader.Dispose()
  }
} finally { $pipe.Dispose() }
