param([string]$XrayDirectory = (Join-Path $env:USERPROFILE "xray-proxy"))
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$taskData=Join-Path $repo '.cache\mihomo-acceptance'
New-Item -ItemType Directory -Path $taskData -Force | Out-Null
. (Join-Path $repo 'modules\proxy-autoswitch\auto-switch.ps1') -DataDirectory $taskData -LibraryOnly
function Get-Watcher { return $null }
function Assert($condition,$message) { if (-not $condition) { throw $message } }
$source=[pscustomobject]@{outbounds=@([pscustomobject]@{tag='node';protocol='shadowsocks';settings=@{servers=@(@{address='127.0.0.1';port=9;method='aes-128-gcm';password='test'})}},[pscustomobject]@{tag='direct';protocol='freedom'},[pscustomobject]@{tag='block';protocol='blackhole'});routing=@{balancers=@(@{tag='auto';selector=@('node');strategy=@{type='leastPing'}});rules=@(@{domain=@('full:test.example','domain:example.com');outboundTag='direct'},@{ip=@('geoip:private');outboundTag='direct'},@{network='tcp,udp';balancerTag='auto'})}}
$source=($source | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
$converted=Convert-XrayToMihomo $source
Assert ($converted.proxies.Count -eq 1 -and $converted['mixed-port'] -eq 10819 -and $converted.tun.enable -eq $false) '转换节点和端口失败'
Assert ($converted.rules -contains 'DOMAIN,test.example,DIRECT') '完整域名语义未保留'
Assert ($converted.rules -contains 'DOMAIN-SUFFIX,example.com,DIRECT') '域名后缀语义未保留'
Assert ($converted.rules -contains 'IP-CIDR6,fc00::/7,DIRECT,no-resolve') '本地IPv6分流未保留'
Assert ($converted.rules[-1] -eq 'MATCH,auto' -and $converted['proxy-groups'][0].type -eq 'url-test') '自动测速和默认路由失败'
$source.routing.rules[0] | Add-Member -NotePropertyName port -NotePropertyValue '443'; $rejected=$false
try { Convert-XrayToMihomo $source | Out-Null } catch { $rejected=$true }
Assert $rejected '不支持的路由条件应拒绝，不能静默丢弃'
Write-Output 'PASS migration semantics and unsupported rule rejection'
# Real local configuration is never printed; install into isolated test directory.
$settings=[pscustomobject]@{coreType='xray';xrayPath=(Join-Path $XrayDirectory 'xray.exe');xrayConfig=(Join-Path $XrayDirectory 'config-balancer.json');proxyUrl='http://127.0.0.1:10809';pollSeconds=3;stableSeconds=6;priority='game';idleBehavior='keep';setSystemProxy=$false;setEnvironmentProxy=$false;workProcesses=@();gameProcesses=@();gamePathHints=@();gamePathExcludes=@()}
Write-AtomicJson (Join-Path $taskData 'config.json') $settings
$before=(Get-FileHash -LiteralPath $settings.xrayConfig).Hash
Install-Mihomo
$installed=Get-Settings
Assert ($installed.coreType -eq 'mihomo' -and $installed.proxyUrl -eq 'http://127.0.0.1:10819') '安装未激活正确配置'
Assert ((Get-FileHash -LiteralPath $settings.xrayConfig).Hash -eq $before) '不得修改原Xray配置'
Assert (-not (Test-Path -LiteralPath $script:SnapshotPath)) '安装不得修改系统代理或环境变量'
Assert (-not (Test-Path -LiteralPath $script:CorePath)) '安装不得启动常驻内核'
& $installed.xrayPath -v
Restore-XrayConfiguration
Assert ((Get-Settings).coreType -eq 'xray') '回退原配置失败'
Write-Output 'PASS verified official binary, real-node config validation, no network settings changed, and Xray rollback'
