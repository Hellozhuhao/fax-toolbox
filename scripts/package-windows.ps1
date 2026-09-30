param([switch]$Test, [switch]$SkipPublish, [string]$InputDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$distRoot = Join-Path $root 'dist'
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
}
$source = if ($InputDirectory) { [IO.Path]::GetFullPath($InputDirectory) } else { Join-Path $distRoot 'windows' }
if (-not $source.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'Package input must be an existing folder under dist'
}
foreach ($relative in @('FaxToolbox.Windows.exe', 'backend\toolbox-backend.exe',
    'apps\windows-shell\ui\index.html', 'apps\windows-shell\ui\theme.css',
    'apps\windows-shell\ui\button-glow.js', 'modules\chaoxing\page-adapter.js')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $relative) -PathType Leaf)) {
        throw "Published package is incomplete: $relative"
    }
}

$archive = Join-Path $distRoot 'FaxToolbox-Windows-win-x64.zip'
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
        $process = Start-Process -FilePath (Join-Path $extract 'FaxToolbox.Windows.exe') `
            -ArgumentList @('--tabs-smoke', $output) -WindowStyle Hidden -PassThru
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
