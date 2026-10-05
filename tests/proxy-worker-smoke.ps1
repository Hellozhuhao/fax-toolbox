$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$workerPath=Join-Path $root 'modules/proxy-autoswitch/auto-switch.ps1'
$testRoot=Join-Path $root ('.cache\network-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$powershellPath=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
@{ proxyUrl='http://127.0.0.1:1'; xrayPath=''; xrayConfig=''; workProcesses=@(); gameProcesses=@(); gamePathHints=@(); gamePathExcludes=@();
    setSystemProxy=$false; setEnvironmentProxy=$false; idleBehavior='keep'; priority='game'; pollSeconds=2; stableSeconds=0 } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'config.json') -Encoding UTF8
$arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',('"'+$workerPath+'"'),'-Action','Watch','-DataDirectory',('"'+$testRoot+'"'))
$worker=Start-Process -FilePath $powershellPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    for ($attempt=0; $attempt -lt 25 -and -not (Test-Path -LiteralPath (Join-Path $testRoot 'status.json')); $attempt++) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path -LiteralPath (Join-Path $testRoot 'status.json'))) { throw 'Worker did not publish a decision' }
    $decision=Get-Content -LiteralPath (Join-Path $testRoot 'status.json') -Raw | ConvertFrom-Json
    if ($decision.decision -ne 'keep') { throw 'Fixture unexpectedly requested a proxy change' }
    $duplicate=Start-Process -FilePath $powershellPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $duplicate.WaitForExit(5000) -or $duplicate.ExitCode -ne 0) { throw 'Single-instance lock failed' }
    $snapshot=& $powershellPath -NoProfile -ExecutionPolicy Bypass -File $workerPath -Action Status -DataDirectory $testRoot | ConvertFrom-Json
    if (-not $snapshot.ok -or -not $snapshot.status.running) { throw 'Controller could not identify owned worker' }
    $result=& $powershellPath -NoProfile -ExecutionPolicy Bypass -File $workerPath -Action Stop -DataDirectory $testRoot | ConvertFrom-Json
    if (-not $result.ok -or $result.status.running) { throw 'Graceful stop failed' }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'watcher.json')) { throw 'Owned PID record was not cleaned up' }
    if (Test-Path -LiteralPath (Join-Path $testRoot 'restore.json')) { throw 'Read-only fixture modified proxy settings' }
    Write-Output 'PASS standalone worker, single instance, status, and graceful stop (no proxy changes)'
} finally {
    if (-not $worker.HasExited) {
        [IO.File]::WriteAllText((Join-Path $testRoot 'stop.request'),'stop')
        if (-not $worker.WaitForExit(5000)) { Stop-Process -Id $worker.Id -Force }
    }
}
