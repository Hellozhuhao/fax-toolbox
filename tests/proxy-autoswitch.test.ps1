$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'modules/proxy-autoswitch/auto-switch.ps1') -LibraryOnly
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$settings = [pscustomobject]@{ workProcesses=@('ChatGPT','codex.exe'); gameProcesses=@('game'); gamePathHints=@('\steamapps\common\'); gamePathExcludes=@('\wallpaper_engine\'); priority='game'; idleBehavior='keep' }
$both = @(@{ Name='ChatGPT'; Path='C:\app\ChatGPT.exe' }, @{Name='game';Path='C:\game.exe'})
Assert ((Get-Decision $settings $both).want -eq 'off') '游戏优先应选择直连'
$settings.priority='work'
Assert ((Get-Decision $settings $both).want -eq 'on') '工作优先应保留代理'
Assert ((Get-Decision $settings @(@{Name='codex';Path='C:\codex.exe'})).want -eq 'on') '进程规则应兼容 .exe 后缀'
$settings.priority='game'
Assert ((Get-Decision $settings @(@{Name='wallpaper';Path='C:\steamapps\common\wallpaper_engine\wallpaper.exe'})).want -eq 'keep') '壁纸程序应被排除'
Assert ((Get-Decision $settings @(@{Name='other';Path='D:\steamapps\common\game\game.exe'})).want -eq 'off') '安装路径应识别游戏'
Assert ((Get-Decision $settings @()).want -eq 'keep') '空闲保持规则无效'
$settings.idleBehavior='off'
Assert ((Get-Decision $settings @()).want -eq 'off') '空闲直连规则无效'
foreach ($invalid in @('https://127.0.0.1:10809','http://example.com:10809','http://user:pass@127.0.0.1:10809','http://127.0.0.1:10809/api')) {
    $rejected=$false; try { Get-Endpoint $invalid | Out-Null } catch { $rejected=$true }; Assert $rejected ('应拒绝非本机 HTTP 代理：'+$invalid)
}
Assert ((Get-Endpoint 'http://127.0.0.1:10809').Port -eq 10809) '本机代理端口解析失败'
$self = Get-Process -Id $PID
$correct = @{pid=$PID;started=$self.StartTime.ToUniversalTime().ToString('o')}
Assert ((Get-OwnedProcess $correct $self.Path).Id -eq $PID) '正确进程身份未识别'
$stale = @{pid=$PID;started=[DateTime]::UtcNow.AddDays(-1).ToString('o')}
Assert (-not (Get-OwnedProcess $stale $self.Path)) '不得信任 PID 相同但启动时间不同的进程'
Assert (-not (Get-OwnedProcess $correct 'C:\unrelated.exe')) '不得信任路径不同的进程'
Write-Output 'PASS network rules, local endpoint validation, and process ownership'

# Provider mocks verify ownership-aware restoration without touching registry or environment.
$script:Root=Join-Path $repoRoot ('.cache\network-restore-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:Root -Force | Out-Null
$script:SnapshotPath=Join-Path $script:Root 'restore.json'
$script:LogPath=Join-Path $script:Root 'events.log'
$script:RegistryMock=@{ProxyEnable=1;ProxyServer='127.0.0.1:10809';ProxyOverride='<local>'}
function Get-ItemProperty { param($LiteralPath) [pscustomobject]$script:RegistryMock }
function New-ItemProperty { param($LiteralPath,$Name,$Value,$PropertyType,[switch]$Force) $script:RegistryMock[$Name]=$Value }
function Remove-ItemProperty { param($LiteralPath,$Name,$ErrorAction) $script:RegistryMock.Remove($Name) }
function Notify-ProxyChange { }
function Stop-OwnedCore { }
$original=@{proxy=@{ProxyEnable=@{exists=$true;value=0;kind='DWord'};ProxyServer=@{exists=$true;value='original:8080';kind='String'};ProxyOverride=@{exists=$false;value=$null;kind='String'}};environment=@{}}
$applied=@{proxyServer='127.0.0.1:10809';proxyUrl='http://127.0.0.1:10809';system=$true;environment=$false;enabled=1}
Write-AtomicJson $script:SnapshotPath $original
Write-AtomicJson (Join-Path $script:Root 'last-applied.json') $applied
Restore-Proxy
Assert ($script:RegistryMock.ProxyEnable -eq 0 -and $script:RegistryMock.ProxyServer -eq 'original:8080') '应恢复原代理设置'
Assert (-not $script:RegistryMock.ContainsKey('ProxyOverride')) '应还原原本不存在的代理属性'
Write-AtomicJson $script:SnapshotPath $original
Write-AtomicJson (Join-Path $script:Root 'last-applied.json') $applied
$script:RegistryMock.ProxyEnable=1; $script:RegistryMock.ProxyServer='other-program:9090'
Restore-Proxy
Assert ($script:RegistryMock.ProxyServer -eq 'other-program:9090') '不得覆盖其他程序随后修改的代理'
Write-Output 'PASS restore original state and preserve external proxy edits (mock registry)'
