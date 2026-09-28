$ErrorActionPreference = 'Stop'
$version = '1.0.4258.31'
$expectedSha256 = '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$cache = Join-Path $root '.cache'
$sdk = Join-Path $cache "webview2-sdk-$version"
$header = Join-Path $sdk 'build\native\include\WebView2.h'
if (Test-Path -LiteralPath $header) {
  Write-Output "WebView2 SDK ready: $sdk"
  exit 0
}
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache "webview2-$version.zip"
Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$version" -OutFile $archive -TimeoutSec 60
$actualSha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
  throw "WebView2 SDK checksum mismatch: $actualSha256"
}
Expand-Archive -LiteralPath $archive -DestinationPath $sdk -Force
if (-not (Test-Path -LiteralPath $header)) { throw 'WebView2 SDK header missing after extraction' }
Write-Output "WebView2 SDK ready: $sdk"
