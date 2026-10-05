param([ValidateSet('Inspect','Publish')][string]$Mode='Inspect', [string]$TargetCommit)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version=Get-Content -LiteralPath (Join-Path $root 'release-version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$repo='Hellozhuhao/fax-toolbox'
$api='https://api.github.com/repos/'+$repo
$credentialLines="protocol=https`nhost=github.com`n`n" | git -c credential.interactive=never credential fill
if ($LASTEXITCODE) { throw 'GitHub credential helper unavailable' }
$credentials=@{}
foreach ($line in $credentialLines) { if ($line -match '^([^=]+)=(.*)$') { $credentials[$matches[1]]=$matches[2] } }
if (-not $credentials.password) { throw 'GitHub credential is missing' }
$headers=@{Authorization=('Bearer '+$credentials.password);Accept='application/vnd.github+json';'User-Agent'='FaxToolbox-Release';'X-GitHub-Api-Version'='2022-11-28'}
function Invoke-JsonApi([string]$Method,[string]$Uri,$Body) {
    $json=$Body | ConvertTo-Json -Depth 8
    Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 60
}
try {
    $existing=@(Invoke-RestMethod -Uri ($api+'/releases') -Headers $headers)
    if ($Mode -eq 'Inspect') { $existing | Select-Object id,tag_name,name,draft,prerelease,html_url; return }
    if (-not $TargetCommit -or $TargetCommit -notmatch '^[a-f0-9]{40}$') { throw 'Pass the verified main commit SHA as -TargetCommit' }
    $main=Invoke-RestMethod -Uri ($api+'/git/ref/heads/main') -Headers $headers
    if ($main.object.sha -ne $TargetCommit) { throw 'Release target does not match GitHub main' }
    $tag='v'+$version.version
    $assetNames=@(('FaxToolbox-Setup-'+$tag+'-win-x64.exe'),('FaxToolbox-Windows-'+$tag+'-win-x64.zip'),'SHA256SUMS.txt')
    foreach ($name in $assetNames) { if (-not (Test-Path -LiteralPath (Join-Path $root ('dist\'+$name)) -PathType Leaf)) { throw "Release asset missing: $name" } }
    $notes=Get-Content -LiteralPath (Join-Path $root 'docs\BETA_RELEASE_NOTES.md') -Raw -Encoding UTF8
    $release=$existing | Where-Object { $_.tag_name -eq $tag } | Select-Object -First 1
    if ($release -and -not $release.draft) { throw 'This version is already published; do not replace public assets' }
    if (-not $release) { $release=Invoke-JsonApi 'POST' ($api+'/releases') @{tag_name=$tag;target_commitish=$TargetCommit;name=$version.name;body=$notes;draft=$true;prerelease=$true} }
    foreach ($name in $assetNames) {
        $file=Join-Path $root ('dist\'+$name)
        $prior=@($release.assets | Where-Object { $_.name -eq $name })
        foreach ($asset in $prior) { Invoke-RestMethod -Method Delete -Uri ($api+'/releases/assets/'+$asset.id) -Headers $headers | Out-Null }
        $uri='https://uploads.github.com/repos/'+$repo+'/releases/'+$release.id+'/assets?name='+[Uri]::EscapeDataString($name)
        $uploaded=Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/octet-stream' -InFile $file -TimeoutSec 900
        if ($uploaded.size -ne (Get-Item -LiteralPath $file).Length -or $uploaded.state -ne 'uploaded') { throw "Upload incomplete: $name" }
        $sha=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($uploaded.digest -and $uploaded.digest -ne ('sha256:'+$sha)) { throw "Uploaded asset checksum mismatch: $name" }
        Write-Output ('Verified asset: '+$name)
    }
    $verified=Invoke-RestMethod -Uri ($api+'/releases/'+$release.id) -Headers $headers
    if (@($verified.assets | Where-Object { $_.name -in $assetNames }).Count -ne $assetNames.Count) { throw 'Release is missing uploaded assets' }
    $published=Invoke-JsonApi 'PATCH' ($api+'/releases/'+$release.id) @{draft=$false;prerelease=$true;make_latest='false'}
    $published | Select-Object tag_name,name,draft,prerelease,html_url
} finally { $headers.Clear(); $credentials.Clear(); $credentialLines=$null }