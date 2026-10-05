# Loaded by auto-switch.ps1. No network/proxy changes occur during installation.
$script:MihomoVersion = 'v1.19.32'
$script:MihomoZipHash = '00549b347d8e124600e46cac67d59293358454f465e9d86d2511567da56b7029'
function Get-CoreType($Settings) {
    if ($Settings.coreType -eq 'mihomo') { return 'mihomo' }
    if ($Settings.coreType -and $Settings.coreType -ne 'xray') { throw '不支持的代理内核类型。' }
    return 'xray'
}
function Get-CoreArguments($Settings, [switch]$Check) {
    if ((Get-CoreType $Settings) -eq 'mihomo') {
        $data = Split-Path -Parent $Settings.xrayConfig
        if ($Check) { return @('-t','-d', ('"'+$data+'"'), '-f', ('"'+$Settings.xrayConfig+'"')) }
        return @('-d', ('"'+$data+'"'), '-f', ('"'+$Settings.xrayConfig+'"'))
    }
    if ($Check) { return @('run','-test','-c', ('"'+$Settings.xrayConfig+'"')) }
    return @('run','-c', ('"'+$Settings.xrayConfig+'"'))
}
function Test-CoreConfiguration($Settings) {
    if (-not (Test-Path -LiteralPath $Settings.xrayPath -PathType Leaf) -or -not (Test-Path -LiteralPath $Settings.xrayConfig -PathType Leaf)) { throw '请先选择代理内核及配置文件。' }
    $output = Join-Path $script:Root 'core-check.log'
    $errors = Join-Path $script:Root 'core-check-error.log'
    $process = Start-Process -FilePath $Settings.xrayPath -ArgumentList (Get-CoreArguments $Settings -Check) -WindowStyle Hidden -PassThru -RedirectStandardOutput $output -RedirectStandardError $errors
    # Cache the handle before exit; PS 5.1 otherwise loses ExitCode for fast tools.
    $handle = $process.Handle
    if (-not $process.WaitForExit(20000)) { Stop-Process -Id $process.Id -Force; throw '内核配置检查超时，未启用代理。' }
    if ($process.ExitCode -ne 0) { throw '内核不接受此配置，请检查配置格式及节点协议（本机 core-check 日志中有详情）。' }
}
function Convert-XrayToMihomo($Source, [int]$Port = 10819) {
    $nodes = @(); $targets = @{}; $rules = @(); $names = @()
    foreach ($outbound in $Source.outbounds) {
        $tag = [string]$outbound.tag
        if (-not $tag -or $targets.ContainsKey($tag)) { throw '节点标识缺失或重复，请手动导入 Mihomo 配置。' }
        if ($outbound.proxySettings -or $outbound.streamSettings -or $outbound.mux.enabled) { throw '此 Xray 节点包含特殊传输或链式代理，请手动导入 Mihomo 配置。' }
        if ($outbound.protocol -eq 'freedom') { $targets[$tag]='DIRECT'; continue }
        if ($outbound.protocol -eq 'blackhole') { $targets[$tag]='REJECT'; continue }
        $servers = @($outbound.settings.servers)
        if ($servers.Count -ne 1 -or -not $servers[0]) { throw '自动导入需要每个节点只有一个服务器。' }
        $server=$servers[0]
        $node=@{name=$tag;server=[string]$server.address;port=[int]$server.port;udp=$true}
        if ($outbound.sendThrough -and $outbound.sendThrough -notin @('0.0.0.0','::')) {
            $interfaces=@([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object {
                @($_.GetIPProperties().UnicastAddresses | Where-Object { $_.Address.ToString() -eq $outbound.sendThrough }).Count -gt 0
            })
            if ($interfaces.Count -ne 1) { throw '原节点绑定的本机地址已不存在，请先更新 Xray 配置中的 sendThrough。' }
            $node['interface-name']=$interfaces[0].Name
            $node['ip-version']=if ($outbound.sendThrough.Contains(':')) { 'ipv6' } else { 'ipv4' }
        }
        switch ($outbound.protocol) {
            'shadowsocks' { $node.type='ss'; $node.cipher=[string]$server.method; $node.password=[string]$server.password }
            'socks' {
                $node.type='socks5'
                if ($server.users) { if (@($server.users).Count -ne 1) { throw 'SOCKS 节点认证配置过多。' }; $node.username=[string]$server.users[0].user; $node.password=[string]$server.users[0].pass }
            }
            default { throw '自动导入目前支持 Shadowsocks 和 SOCKS5；其他协议请选择原生 Mihomo 配置。' }
        }
        $nodes += $node; $names += $tag; $targets[$tag]=$tag
    }
    if (-not $nodes.Count) { throw '没有可导入的代理节点。' }
    $groups=@(); $balancers=@{}
    foreach ($balancer in $Source.routing.balancers) {
        $members=@($names | Where-Object { $name=$_; @($balancer.selector | Where-Object { $name.StartsWith($_,[StringComparison]::Ordinal) }).Count -gt 0 })
        if (-not $members.Count -or $targets.ContainsKey([string]$balancer.tag)) { throw '自动选择组没有有效节点或名称冲突。' }
        if ($balancer.strategy.type -notin @('leastPing','')) { throw '此负载均衡策略无法自动转换。' }
        $groups+=@{name=[string]$balancer.tag;type='url-test';proxies=$members;url='https://www.gstatic.com/generate_204';interval=300;tolerance=100;lazy=$false}
        $balancers[[string]$balancer.tag]=[string]$balancer.tag
    }
    foreach ($rule in $Source.routing.rules) {
        $unknown=@($rule.PSObject.Properties.Name | Where-Object { $_ -notin @('type','domain','ip','network','outboundTag','balancerTag') })
        if ($unknown.Count -or ($rule.domain -and $rule.ip) -or ($rule.network -and ($rule.domain -or $rule.ip))) { throw '此路由包含无法自动转换的组合条件，请手动导入 Mihomo 配置。' }
        $target = if ($rule.balancerTag) { $balancers[[string]$rule.balancerTag] } else { $targets[[string]$rule.outboundTag] }
        if (-not $target) { throw '路由引用了不存在的节点。' }
        if ($rule.domain) {
            foreach ($domain in $rule.domain) {
                if ($domain.Contains(',')) { throw '路由含逗号，不能直接导入。' }
                $value=$domain -replace '^[^:]+:',''
                if ($domain.StartsWith('geosite:')) { $rules += 'GEOSITE,'+$value+','+$target }
                elseif ($domain.StartsWith('domain:')) { $rules += 'DOMAIN-SUFFIX,'+$value+','+$target }
                elseif ($domain.StartsWith('full:')) { $rules += 'DOMAIN,'+$value+','+$target }
                elseif ($domain.Contains(':')) { throw '暂不支持此域名路由格式。' }
                else { $rules += 'DOMAIN-KEYWORD,'+$domain+','+$target }
            }
        } elseif ($rule.ip) {
            foreach ($ip in $rule.ip) {
                if ($ip -eq 'geoip:private') {
                    foreach ($cidr in @('127.0.0.0/8','10.0.0.0/8','172.16.0.0/12','192.168.0.0/16','169.254.0.0/16')) { $rules += 'IP-CIDR,'+$cidr+','+$target+',no-resolve' }
                    foreach ($cidr in @('::1/128','fc00::/7','fe80::/10')) { $rules += 'IP-CIDR6,'+$cidr+','+$target+',no-resolve' }
                } elseif ($ip.StartsWith('geoip:')) { $rules+='GEOIP,'+($ip.Substring(6).ToUpperInvariant())+','+$target }
                elseif ($ip -match '^[\da-fA-F.:]+/\d+$') { $kind=if ($ip.Contains(':')) {'IP-CIDR6'} else {'IP-CIDR'}; $rules+=$kind+','+$ip+','+$target }
                else { throw '暂不支持此 IP 路由格式。' }
            }
        } elseif (-not $rule.network -or $rule.network -in @('tcp,udp','udp,tcp')) { $rules+='MATCH,'+$target }
        elseif ($rule.network -in @('tcp','udp')) { $rules+='NETWORK,'+$rule.network+','+$target }
        else { throw '无法识别网络路由。' }
    }
    if (-not @($rules | Where-Object { $_.StartsWith('MATCH,') }).Count) { $rules+='MATCH,'+$names[0] }
    # JSON is accepted by Mihomo's YAML parser. No custom YAML string escaping.
    return @{ 'mixed-port'=$Port; 'allow-lan'=$false; 'bind-address'='127.0.0.1'; mode='rule'; 'log-level'='warning'; ipv6=$false; 'geodata-mode'=$true; 'geodata-loader'='standard'; 'geo-auto-update'=$false; proxies=$nodes; 'proxy-groups'=$groups; rules=$rules; tun=@{enable=$false} }
}
function Install-Mihomo {
    if (Get-Watcher) { throw '请先停止自动切换，再安装或切换内核。' }
    $previous=Get-Settings
    if ((Get-CoreType $previous) -eq 'mihomo') { Test-CoreConfiguration $previous; return }
    $record=Read-Json $script:CorePath
    if (Get-OwnedProcess $record $record.path) { throw '请先点击停止并还原，再切换内核。' }
    $destination=Join-Path $script:Root ('cores\mihomo-'+$script:MihomoVersion)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $archive=Join-Path $destination 'official.zip'; $binary=Join-Path $destination 'mihomo.exe'
    $bundled=Join-Path (Split-Path $script:ScriptPath) 'mihomo-official.zip'
    if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $script:MihomoZipHash) {
        if (Test-Path -LiteralPath $bundled) { Copy-Item -LiteralPath $bundled -Destination $archive -Force }
        else {
            [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -UseBasicParsing -Uri ('https://github.com/MetaCubeX/mihomo/releases/download/'+$script:MihomoVersion+'/mihomo-windows-amd64-v1-'+$script:MihomoVersion+'.zip') -OutFile $archive -TimeoutSec 120
        }
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $script:MihomoZipHash) { throw '官方内核文件校验失败，未安装。' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entry=@($zip.Entries | Where-Object { $_.FullName -notmatch '[/\\]' -and $_.Name -like 'mihomo*.exe' })
        if ($entry.Count -ne 1) { throw '内核压缩包结构异常。' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry[0],$binary,$true)
    } finally { $zip.Dispose() }
    $data=Join-Path $destination 'data'; New-Item -ItemType Directory -Path $data -Force | Out-Null
    $source=Read-Json $previous.xrayConfig
    $converted=Convert-XrayToMihomo $source
    foreach ($name in @('geoip.dat','geosite.dat')) {
        $original=Join-Path (Split-Path -Parent $previous.xrayConfig) $name
        if (-not (Test-Path -LiteralPath $original)) { throw ('导入需要原配置目录中的 '+$name+'，或直接选择原生 Mihomo 配置。') }
        Copy-Item -LiteralPath $original -Destination (Join-Path $data $name) -Force
    }
    $configFile=Join-Path $data 'config.json'; Write-AtomicJson $configFile $converted
    $settings=($previous | ConvertTo-Json -Depth 12 | ConvertFrom-Json)
    $settings | Add-Member -NotePropertyName coreType -NotePropertyValue 'mihomo' -Force
    $settings.xrayPath=$binary; $settings.xrayConfig=$configFile; $settings.proxyUrl='http://127.0.0.1:10819'
    Test-CoreConfiguration $settings
    # Activate the prepared configuration only after the binary accepted it.
    $backup=Join-Path $script:Root 'xray-fallback.json'
    if (-not (Test-Path -LiteralPath $backup)) { Write-AtomicJson $backup $previous }
    Write-AtomicJson (Join-Path $script:Root 'config.json') $settings
    Write-Event ('Mihomo '+$script:MihomoVersion+' 已校验并导入节点；代理设置保持当前状态')
}
function Restore-XrayConfiguration {
    if (Get-Watcher) { throw '请先停止自动切换，再恢复配置。' }
    $record=Read-Json $script:CorePath
    if (Get-OwnedProcess $record $record.path) { throw '请先点击停止并还原，再恢复 Xray 配置。' }
    $previous=Read-Json (Join-Path $script:Root 'xray-fallback.json')
    if (-not $previous) { throw '暂无 Xray 回退配置。' }
    Write-AtomicJson (Join-Path $script:Root 'config.json') $previous
    Write-Event '已恢复 Xray 配置，系统代理保持当前状态'
}
