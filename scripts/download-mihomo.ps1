$ErrorActionPreference = 'Stop'
$module = Join-Path $PSScriptRoot '..\modules\proxy-autoswitch'
. (Join-Path $module 'mihomo.ps1')
$archive = Join-Path $module 'mihomo-official.zip'
if ((Test-Path -LiteralPath $archive) -and (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ieq $script:MihomoZipHash) { return }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -UseBasicParsing -Uri ('https://github.com/MetaCubeX/mihomo/releases/download/'+$script:MihomoVersion+'/mihomo-windows-amd64-v1-'+$script:MihomoVersion+'.zip') -OutFile $archive -TimeoutSec 120
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $script:MihomoZipHash) { throw 'Mihomo official archive checksum mismatch' }
Write-Host ('Verified official Mihomo ' + $script:MihomoVersion)
