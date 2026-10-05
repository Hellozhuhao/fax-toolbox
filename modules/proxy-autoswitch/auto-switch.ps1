param(
    [ValidateSet('Status','Start','Stop','Watch','Enable','Disable','StartupOn','StartupOff','Validate')][string]$Action = 'Status',
    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'FaxToolbox\proxy-autoswitch'),
    [switch]$TakeOverLegacy,
    [switch]$LibraryOnly
)
$ErrorActionPreference = 'Stop'
$script:Root = [IO.Path]::GetFullPath($DataDirectory)
$script:ScriptPath = [IO.Path]::GetFullPath($PSCommandPath)
$script:LegacyRoot = Join-Path ([Environment]::GetFolderPath('Desktop')) 'VPNsetting'
$script:StatePath = Join-Path $script:Root 'status.json'
$script:PidPath = Join-Path $script:Root 'watcher.json'
$script:SnapshotPath = Join-Path $script:Root 'restore.json'
$script:CorePath = Join-Path $script:Root 'core.json'
$script:LogPath = Join-Path $script:Root 'events.log'
$script:StartupPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'FaxToolbox-ProxyAutoSwitch.vbs'
$script:ProxyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
$script:EnvNames = @('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','NO_PROXY')

function Write-AtomicJson($Path, $Value) {
    $temporary = $Path + '.' + $PID + '.tmp'
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}
function Read-Json($Path) { if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json } }
function Write-Event([string]$Message) {
    if ((Test-Path -LiteralPath $script:LogPath) -and (Get-Item -LiteralPath $script:LogPath).Length -gt 262144) {
        Move-Item -LiteralPath $script:LogPath -Destination ($script:LogPath + '.previous') -Force
    }
    Add-Content -LiteralPath $script:LogPath -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + '  ' + $Message) -Encoding UTF8
}
function Get-Settings {
    $settings = Read-Json (Join-Path $script:Root 'config.json')
    if (-not $settings) { throw '请先保存代理配置。' }
    $endpoint = Get-Endpoint $settings.proxyUrl
    if ($settings.pollSeconds -lt 2 -or $settings.pollSeconds -gt 30) { throw '检查间隔应为 2–30 秒。' }
    if ($settings.stableSeconds -lt 0 -or $settings.stableSeconds -gt 60) { throw '切换等待应为 0–60 秒。' }
    if ($settings.priority -notin @('work','game') -or $settings.idleBehavior -notin @('keep','off')) { throw '切换规则无效。' }
    return $settings
}
function Get-Endpoint([string]$Url) {
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'http' -or
        $uri.Host -notin @('127.0.0.1','localhost','[::1]','::1') -or $uri.Port -lt 1 -or
        $uri.UserInfo -or $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) { throw '代理地址应为本机 HTTP 代理，例如 http://127.0.0.1:10809。' }
    return $uri
}
function Test-Port([Uri]$Endpoint) {
    $client = [Net.Sockets.TcpClient]::new()
    try { $pending = $client.ConnectAsync($Endpoint.DnsSafeHost, $Endpoint.Port); return ($pending.Wait(400) -and $client.Connected) }
    catch { return $false } finally { $client.Dispose() }
}
function Get-Decision($Settings, $Processes) {
    $work = @(); $games = @()
    foreach ($item in $Processes) {
        $name = $item.Name -replace '\.exe$',''
        $isWork = @($Settings.workProcesses | Where-Object { ($_ -replace '\.exe$','') -ieq $name }).Count -gt 0
        $isGame = @($Settings.gameProcesses | Where-Object { ($_ -replace '\.exe$','') -ieq $name }).Count -gt 0
        if ($item.Path) {
            $excluded = @($Settings.gamePathExcludes | Where-Object { $_ -and $item.Path.IndexOf($_,[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
            if (-not $excluded -and @($Settings.gamePathHints | Where-Object { $_ -and $item.Path.IndexOf($_,[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0) { $isGame = $true }
        }
        if ($isWork) { $work += $name }
        if ($isGame) { $games += $name }
    }
    $want = 'keep'; $reason = '未命中规则，保持当前状态'
    if ($work.Count -and ($Settings.priority -eq 'work' -or -not $games.Count)) { $want = 'on'; $reason = '工作应用需要代理' }
    elseif ($games.Count) { $want = 'off'; $reason = '检测到游戏，使用直连' }
    elseif ($Settings.idleBehavior -eq 'off') { $want = 'off'; $reason = '空闲时使用直连' }
    return @{ want=$want; reason=$reason; work=@($work | Select-Object -Unique); games=@($games | Select-Object -Unique) }
}
function Get-ProcessList {
    # One process snapshot per cycle instead of one WMI query per process.
    return @(Get-Process | ForEach-Object {
        $path = $null; try { $path = $_.MainModule.FileName } catch { }
        @{ Name=$_.ProcessName; Path=$path }
    })
}
function Get-OwnedProcess($Record, [string]$ExpectedPath, [string]$CommandFragment = '') {
    if (-not $Record -or -not $Record.pid -or -not $Record.started) { return $null }
    $process = Get-Process -Id ([int]$Record.pid) -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    try {
        if ($process.StartTime.ToUniversalTime().ToString('o') -ne $Record.started -or $process.Path -ine $ExpectedPath) { return $null }
        if ($CommandFragment) {
            $command = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $Record.pid) -OperationTimeoutSec 3).CommandLine
            if (-not $command -or $command.IndexOf($CommandFragment,[StringComparison]::OrdinalIgnoreCase) -lt 0) { return $null }
        }
        return $process
    } catch { return $null }
}
function Get-Watcher {
    return Get-OwnedProcess (Read-Json $script:PidPath) (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') $script:ScriptPath
}
function Get-LegacyWatchers {
    $legacyScript = Join-Path $script:LegacyRoot 'auto-switch.ps1'
    return @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -OperationTimeoutSec 3 | Where-Object {
        $_.ProcessId -ne $PID -and $_.CommandLine -and $_.CommandLine.IndexOf($legacyScript,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and
        $_.CommandLine -match '(?i)-File\s'
    })
}
function Initialize-Native {
    if ('FaxProxyNative' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class FaxProxyNative {
 [DllImport("wininet.dll")] public static extern bool InternetSetOption(IntPtr handle, int option, IntPtr buffer, int length);
 [DllImport("user32.dll", CharSet=CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
'@
}
function Notify-ProxyChange {
    Initialize-Native
    [FaxProxyNative]::InternetSetOption([IntPtr]::Zero,39,[IntPtr]::Zero,0) | Out-Null
    [FaxProxyNative]::InternetSetOption([IntPtr]::Zero,37,[IntPtr]::Zero,0) | Out-Null
    $result = [UIntPtr]::Zero
    [FaxProxyNative]::SendMessageTimeout([IntPtr]0xffff,0x001A,[UIntPtr]::Zero,'Environment',2,500,[ref]$result) | Out-Null
}
function Save-RestorePoint {
    if (Test-Path -LiteralPath $script:SnapshotPath) { return }
    $proxy = @{}; $key = Get-Item -LiteralPath $script:ProxyKey
    foreach ($name in @('ProxyEnable','ProxyServer','ProxyOverride')) {
        $exists = $key.GetValueNames() -contains $name
        $proxy[$name] = @{ exists=$exists; value=$key.GetValue($name); kind=if ($exists) { $key.GetValueKind($name).ToString() } else { 'String' } }
    }
    $environment = @{}
    foreach ($name in $script:EnvNames) { $value = [Environment]::GetEnvironmentVariable($name,'User'); $environment[$name] = @{ exists=$null -ne $value; value=$value } }
    Write-AtomicJson $script:SnapshotPath @{ proxy=$proxy; environment=$environment }
}
function Stop-OwnedCore {
    $record = Read-Json $script:CorePath
    $core = Get-OwnedProcess $record $record.path
    if ($core) { Stop-Process -Id $core.Id -Force; Write-Event '已停止由本模块启动的代理内核' }
    Remove-Item -LiteralPath $script:CorePath -Force -ErrorAction SilentlyContinue
}
function Enable-Proxy($Settings) {
    $endpoint = Get-Endpoint $Settings.proxyUrl
    if (-not (Test-Port $endpoint)) {
        if (-not (Test-Path -LiteralPath $Settings.xrayPath -PathType Leaf) -or -not (Test-Path -LiteralPath $Settings.xrayConfig -PathType Leaf)) { throw '代理端口未就绪，且代理内核或配置文件不存在。' }
        $core = Get-OwnedProcess (Read-Json $script:CorePath) $Settings.xrayPath
        if (-not $core) {
            # A core using this executable but a different port may have an invalid config. Do not spawn duplicates.
            $external = @(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($Settings.xrayPath)) -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -ieq $Settings.xrayPath } catch { $false } })
            if ($external.Count) { throw '代理内核已运行但目标端口未监听，请检查配置中的 HTTP 入站端口。' }
            $core = Start-Process -FilePath $Settings.xrayPath -ArgumentList @('run','-c', ('"' + $Settings.xrayConfig + '"')) -WindowStyle Hidden -PassThru
            Write-AtomicJson $script:CorePath @{ pid=$core.Id; started=$core.StartTime.ToUniversalTime().ToString('o'); path=$Settings.xrayPath }
        }
        for ($attempt=0; $attempt -lt 20 -and -not (Test-Port $endpoint); $attempt++) { Start-Sleep -Milliseconds 250 }
        if (-not (Test-Port $endpoint)) { Stop-OwnedCore; throw '代理内核启动后端口仍未就绪，未修改系统代理。' }
    }
    Save-RestorePoint
    if ($Settings.setSystemProxy) {
        Set-ItemProperty -LiteralPath $script:ProxyKey -Name ProxyServer -Value $endpoint.Authority
        Set-ItemProperty -LiteralPath $script:ProxyKey -Name ProxyEnable -Value 1
        Set-ItemProperty -LiteralPath $script:ProxyKey -Name ProxyOverride -Value '<local>;localhost;127.*;[::1]'
    }
    if ($Settings.setEnvironmentProxy) {
        foreach ($name in @('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY')) { [Environment]::SetEnvironmentVariable($name,$Settings.proxyUrl,'User') }
        [Environment]::SetEnvironmentVariable('NO_PROXY','localhost,127.0.0.1,::1','User')
    }
    Notify-ProxyChange
    Write-AtomicJson (Join-Path $script:Root 'last-applied.json') @{ proxyServer=$endpoint.Authority; proxyUrl=$Settings.proxyUrl; system=$Settings.setSystemProxy; environment=$Settings.setEnvironmentProxy; enabled=1 }
    Write-Event '代理已启用，监听端口已验证'
}
function Disable-Proxy($Settings) {
    Save-RestorePoint
    if ($Settings.setSystemProxy) { Set-ItemProperty -LiteralPath $script:ProxyKey -Name ProxyEnable -Value 0 }
    if ($Settings.setEnvironmentProxy) { foreach ($name in $script:EnvNames) { [Environment]::SetEnvironmentVariable($name,$null,'User') } }
    Stop-OwnedCore
    Notify-ProxyChange
    $endpoint = Get-Endpoint $Settings.proxyUrl
    $currentServer = (Get-ItemProperty -LiteralPath $script:ProxyKey).ProxyServer
    Write-AtomicJson (Join-Path $script:Root 'last-applied.json') @{ proxyServer=$currentServer; proxyUrl=$Settings.proxyUrl; system=$Settings.setSystemProxy; environment=$Settings.setEnvironmentProxy; enabled=0 }
    Write-Event '已切换直连；外部启动的代理内核保持运行'
}
function Restore-Proxy {
    $snapshot = Read-Json $script:SnapshotPath
    $applied = Read-Json (Join-Path $script:Root 'last-applied.json')
    if ($snapshot -and $applied) {
        $current = Get-ItemProperty -LiteralPath $script:ProxyKey
        # Restore only settings still owned by this module. Preserve subsequent user edits.
        if ($applied.system -and $current.ProxyEnable -eq $applied.enabled -and
            $current.ProxyServer -eq $applied.proxyServer) {
            foreach ($property in $snapshot.proxy.PSObject.Properties) {
                if ($property.Value.exists) { New-ItemProperty -LiteralPath $script:ProxyKey -Name $property.Name -Value $property.Value.value -PropertyType $property.Value.kind -Force | Out-Null }
                else { Remove-ItemProperty -LiteralPath $script:ProxyKey -Name $property.Name -ErrorAction SilentlyContinue }
            }
        } elseif ($applied.system) { Write-Event '系统代理已被其他程序更改，保留当前设置' }
        if ($applied.environment) {
            foreach ($property in $snapshot.environment.PSObject.Properties) {
                $currentValue = [Environment]::GetEnvironmentVariable($property.Name,'User')
                $expected = if ($applied.enabled -eq 0) { $null } elseif ($property.Name -eq 'NO_PROXY') { 'localhost,127.0.0.1,::1' } else { $applied.proxyUrl }
                if ($currentValue -eq $expected) { [Environment]::SetEnvironmentVariable($property.Name,$property.Value.value,'User') }
            }
        }
        Notify-ProxyChange
    }
    Stop-OwnedCore
    Remove-Item -LiteralPath $script:SnapshotPath,(Join-Path $script:Root 'last-applied.json') -Force -ErrorAction SilentlyContinue
    Write-Event '自动切换已停止，原代理设置已还原'
}
function Take-OverOldWatcher {
    foreach ($old in (Get-LegacyWatchers)) {
        Stop-Process -Id $old.ProcessId -Force -ErrorAction Stop
        Write-Event '已接管旧版自动切换进程'
    }
    $oldStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'xray-proxy-start.vbs'
    if (Test-Path -LiteralPath $oldStartup) {
        $content = Get-Content -LiteralPath $oldStartup -Raw
        if ($content.IndexOf((Join-Path $script:LegacyRoot 'auto-switch.ps1'),[StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $backup = Join-Path $script:Root 'legacy-startup.vbs.backup'
            if (-not (Test-Path -LiteralPath $backup)) { Copy-Item -LiteralPath $oldStartup -Destination $backup }
            if (Test-Path -LiteralPath ($oldStartup + '.orig')) { Copy-Item -LiteralPath ($oldStartup + '.orig') -Destination $oldStartup -Force }
            else { Remove-Item -LiteralPath $oldStartup }
            Write-Event '旧自动切换启动项已备份并撤下，避免双重切换'
        }
    }
}
function Get-Status {
    $settings = Get-Settings
    $watcher = Get-Watcher
    $proxy = Get-ItemProperty -LiteralPath $script:ProxyKey
    $state = Read-Json $script:StatePath
    # PS 5.1 Get-Content adds filesystem metadata to strings. Deep JSON serialization
    # follows that metadata into provider/assembly objects; use plain .NET strings.
    $log = @()
    if (Test-Path -LiteralPath $script:LogPath) { $log = @([IO.File]::ReadAllLines($script:LogPath, [Text.Encoding]::UTF8) | Select-Object -Last 30) }
    $legacy = @(Get-LegacyWatchers)
    $legacyStartupPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'xray-proxy-start.vbs'
    $legacyStartup = (Test-Path -LiteralPath $legacyStartupPath) -and
        (Get-Content -LiteralPath $legacyStartupPath -Raw).IndexOf((Join-Path $script:LegacyRoot 'auto-switch.ps1'),[StringComparison]::OrdinalIgnoreCase) -ge 0
    $ready = Test-Port (Get-Endpoint $settings.proxyUrl)
    return @{ running=[bool]$watcher; startup=(Test-Path -LiteralPath $script:StartupPath); proxyEnabled=($proxy.ProxyEnable -eq 1); proxyServer=$proxy.ProxyServer;
        portReady=$ready; legacyRunning=($legacy.Count -gt 0 -or $legacyStartup); state=$state; logs=$log }
}
function Invoke-Watcher {
    $mutexName = 'Local\FaxToolboxProxyWatcher-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $mutex = [Threading.Mutex]::new($false,$mutexName)
    if (-not $mutex.WaitOne(0)) { $mutex.Dispose(); return }
    try {
        $self = Get-Process -Id $PID
        Write-AtomicJson $script:PidPath @{ pid=$PID; started=$self.StartTime.ToUniversalTime().ToString('o') }
        Write-Event '自动切换已启动'
        $pending = ''; $since = [DateTime]::UtcNow; $last = ''; $failures=0; $retryAt=[DateTime]::MinValue; $lastError=''
        while (-not (Test-Path -LiteralPath (Join-Path $script:Root 'stop.request'))) {
            $settings = Get-Settings
            $decision = Get-Decision $settings (Get-ProcessList)
            $want = $decision.want
            if ($want -ne $pending) { $pending=$want; $since=[DateTime]::UtcNow }
            if ($want -ne 'keep' -and ([DateTime]::UtcNow - $since).TotalSeconds -ge $settings.stableSeconds -and [DateTime]::UtcNow -ge $retryAt) {
                try {
                    $proxy = Get-ItemProperty -LiteralPath $script:ProxyKey
                    $endpoint = Get-Endpoint $settings.proxyUrl
                    $drift = $settings.setSystemProxy -and (($want -eq 'on' -and ($proxy.ProxyEnable -ne 1 -or $proxy.ProxyServer -ne $endpoint.Authority)) -or ($want -eq 'off' -and $proxy.ProxyEnable -ne 0))
                    if ($want -ne $last -or $drift -or ($want -eq 'on' -and -not (Test-Port $endpoint))) {
                        if ($want -eq 'on') { Enable-Proxy $settings } else { Disable-Proxy $settings }
                        $last=$want
                    }
                    $failures=0; $lastError=''
                } catch { $lastError=$_.Exception.Message; $failures++; $retryAt=[DateTime]::UtcNow.AddSeconds([Math]::Min(60,5*$failures)); Write-Event ('切换失败，将稍后重试：' + $lastError) }
            }
            Write-AtomicJson $script:StatePath @{ decision=$want; reason=$decision.reason; work=$decision.work; games=$decision.games; error=$lastError; updated=[DateTime]::UtcNow.ToString('o'); waiting=([DateTime]::UtcNow-$since).TotalSeconds -lt $settings.stableSeconds }
            Start-Sleep -Seconds ([int]$settings.pollSeconds)
        }
    } catch { Write-Event ('监控异常退出：' + $_.Exception.Message) }
    finally {
        try { Restore-Proxy } catch { Write-Event ('还原失败：' + $_.Exception.Message) }
        Remove-Item -LiteralPath $script:PidPath,(Join-Path $script:Root 'stop.request') -Force -ErrorAction SilentlyContinue
        $mutex.ReleaseMutex(); $mutex.Dispose()
    }
}
if ($LibraryOnly) { return }
New-Item -ItemType Directory -Path $script:Root -Force | Out-Null
try {
    if ($Action -eq 'Watch') { Invoke-Watcher; exit }
    # Serialize controller operations across windows and startup launchers.
    $control = [Threading.Mutex]::new($false,('Local\FaxToolboxProxyControl-' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value))
    if (-not $control.WaitOne(10000)) { throw '另一个代理操作正在执行，请稍后重试。' }
    try {
        switch ($Action) {
            'Validate' { $settings=Get-Settings; $endpoint=Get-Endpoint $settings.proxyUrl; if (-not (Test-Port $endpoint) -and (-not (Test-Path -LiteralPath $settings.xrayPath -PathType Leaf) -or -not (Test-Path -LiteralPath $settings.xrayConfig -PathType Leaf))) { throw '端口未监听，请选择代理内核及配置文件。' } }
            'Start' {
                if (-not (Get-Watcher)) {
                    $settings=Get-Settings
                    if ((Get-LegacyWatchers).Count -gt 0 -and -not $TakeOverLegacy) { throw '旧版自动切换正在运行，请确认接管后重试。' }
                    if ($TakeOverLegacy) { Take-OverOldWatcher }
                    Remove-Item -LiteralPath (Join-Path $script:Root 'stop.request') -Force -ErrorAction SilentlyContinue
                    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
                    $worker = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',('"'+$script:ScriptPath+'"'),'-Action','Watch','-DataDirectory',('"'+$script:Root+'"')) -WindowStyle Hidden -PassThru
                    for ($i=0; $i -lt 20 -and -not (Get-Watcher); $i++) { Start-Sleep -Milliseconds 250 }
                    if (-not (Get-Watcher)) { throw '监控进程未能启动，请查看日志。' }
                }
            }
            'Stop' {
                $watcher=Get-Watcher
                if ($watcher) {
                    [IO.File]::WriteAllText((Join-Path $script:Root 'stop.request'),'stop')
                    if (-not $watcher.WaitForExit(35000)) { Stop-Process -Id $watcher.Id -Force; Restore-Proxy; Remove-Item -LiteralPath $script:PidPath -Force -ErrorAction SilentlyContinue }
                } else { Restore-Proxy }
            }
            'Enable' { if (Get-Watcher) { throw '请先停止自动切换，再手动切换。' }; Enable-Proxy (Get-Settings) }
            'Disable' { if (Get-Watcher) { throw '请先停止自动切换，再手动切换。' }; Disable-Proxy (Get-Settings) }
            'StartupOn' {
                Take-OverOldWatcher
                $q='"'; $command='powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$script:ScriptPath+'" -Action Start -TakeOverLegacy -DataDirectory "'+$script:Root+'"'
                [IO.File]::WriteAllText($script:StartupPath, ('Set ws = CreateObject("WScript.Shell")' + "`r`n" + 'ws.Run "' + $command.Replace($q,$q+$q) + '", 0, False' + "`r`n"), [Text.Encoding]::Unicode)
                Write-Event '已启用登录后自动切换'
            }
            'StartupOff' { Remove-Item -LiteralPath $script:StartupPath -Force -ErrorAction SilentlyContinue; Write-Event '已关闭登录后自动切换' }
        }
        @{ ok=$true; status=(Get-Status) } | ConvertTo-Json -Depth 12 -Compress
    } finally { $control.ReleaseMutex(); $control.Dispose() }
} catch { @{ ok=$false; error=$_.Exception.Message } | ConvertTo-Json -Compress; exit 1 }
