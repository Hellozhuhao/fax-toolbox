param([switch]$Test, [switch]$SkipPublish, [string]$InputDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$distRoot = Join-Path $root 'dist'
$version = Get-Content -LiteralPath (Join-Path $root 'release-version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$source = if ($InputDirectory) { [IO.Path]::GetFullPath($InputDirectory) } else { Join-Path $distRoot 'windows-beta' }
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1') -OutputDirectory $source
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
}
if (-not $source.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'Package input must be an existing folder under dist'
}
foreach ($relative in @('FaxToolbox.Windows.exe', 'backend\toolbox-backend.exe',
    'apps\windows-shell\ui\index.html', 'apps\windows-shell\ui\theme.css',
    'apps\windows-shell\ui\fax-mark.svg',
    'apps\windows-shell\ui\button-glow.js', 'apps\windows-shell\ui\vendor\katex\katex.min.js',
    'apps\windows-shell\ui\vendor\katex\katex.min.css', 'modules\chaoxing\page-adapter.js',
    'modules\chaoxing\homework-reader.js', 'scripts\read-live-diagnostics.ps1',
    'runtime\scrcpy\scrcpy.exe', 'runtime\scrcpy\adb.exe',
    'runtime\scrcpy\scrcpy-server', 'runtime\scrcpy\LICENSE-scrcpy.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) {
        throw "Published package is incomplete: $relative"
    }
}

foreach ($item in Get-ChildItem -LiteralPath $source -Recurse -Force) {
    if ($item.Name -in @('.cache','.git','vault.json','answers.json','catalog.json','ai-settings.json','user-data','profiles','WebView2') -or
        $item.Extension -in @('.pdb','.sqlite','.db','.trace') -or $item.Name -like '.env*' -or $item.Name -like 'cookies*') {
        throw "Private or development data found in package input: $($item.FullName)"
    }
}
$archive = Join-Path $distRoot ('FaxToolbox-Windows-v'+$version.version+'-win-x64.zip')
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
$packagedItems = @(Get-ChildItem -LiteralPath $source -Force | Where-Object Name -ne '.cache' |
    Select-Object -ExpandProperty FullName)
Compress-Archive -LiteralPath $packagedItems -DestinationPath $archive -CompressionLevel Optimal

if ($Test) {
    . (Join-Path $PSScriptRoot 'Enter-ToolboxDev.ps1')
    $cacheRoot = Join-Path $root '.cache'
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    $extract = Join-Path $cacheRoot ('portable-' + [Guid]::NewGuid().ToString('N'))
    if (-not $extract.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe extraction directory' }
    try {
        [IO.Compression.ZipFile]::ExtractToDirectory($archive, $extract)
        $output = Join-Path $cacheRoot 'portable-smoke.txt'
        Remove-Item -LiteralPath $output, "$output.trace" -ErrorAction SilentlyContinue
        $process = Start-Process -FilePath (Join-Path $extract 'FaxToolbox.Windows.exe') `
            -ArgumentList @('--smoke', ('"'+$output+'"')) -WindowStyle Hidden -PassThru
        $handle = $process.Handle
        if (-not $process.WaitForExit(45000)) {
            Stop-Process -Id $process.Id -Force
            throw 'Relocated package smoke timed out'
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $output) -or
            -not ((Get-Content -LiteralPath $output -Raw).StartsWith('PASS'))) {
            throw 'Relocated package smoke failed'
        }
    }
    finally {
        if (Test-Path -LiteralPath $extract -PathType Container) {
            Remove-Item -LiteralPath $extract -Recurse -Force
        }
    }
}
Write-Host "Portable Windows archive ready: $archive"
