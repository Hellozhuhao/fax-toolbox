$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\apps\windows-shell\assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$iconPath = Join-Path $assetDirectory 'fax-toolbox.ico'
$pngPath = Join-Path $assetDirectory 'fax-toolbox.png'
$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
function New-RoundedPath([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(4, 4)
    $shape = New-RoundedPath 2 2 60 60 19
    $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new([System.Drawing.Rectangle]::new(0,0,64,64),[Drawing.ColorTranslator]::FromHtml('#404e66'),[Drawing.ColorTranslator]::FromHtml('#242e42'),45.0)
    $paper = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#fff4e2'))
    $gold = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#f6d4a4'),3.5)
    $seam = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#a58b6a'),1.8)
    $coral = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#d17b59'))
    $star = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#e49a71'))
    $handle = New-RoundedPath 23 16 18 18 5
    $case = New-RoundedPath 13 26 40 26 7
    $clasp = New-RoundedPath 29 35 6 9 2
    try {
        $graphics.FillPath($gradient,$shape)
        $graphics.DrawPath($gold,$handle)
        $graphics.FillPath($paper,$case)
        $graphics.DrawBezier($seam,14,37,25,42,39,42,52,37)
        $graphics.FillPath($coral,$clasp)
        $graphics.FillPolygon($star,[Drawing.PointF[]]@([Drawing.PointF]::new(46,9),[Drawing.PointF]::new(47.8,13.2),[Drawing.PointF]::new(52,15),[Drawing.PointF]::new(47.8,16.8),[Drawing.PointF]::new(46,21),[Drawing.PointF]::new(44.2,16.8),[Drawing.PointF]::new(40,15),[Drawing.PointF]::new(44.2,13.2)))
    } finally { $shape.Dispose();$gradient.Dispose();$paper.Dispose();$gold.Dispose();$seam.Dispose();$coral.Dispose();$star.Dispose();$handle.Dispose();$case.Dispose();$clasp.Dispose() }
} finally { $graphics.Dispose() }

$png = [System.IO.MemoryStream]::new()
try {
    $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $png.ToArray()
    [System.IO.File]::WriteAllBytes($pngPath, $bytes)
    $output = [System.IO.File]::Create($iconPath)
    try {
        $writer = [System.IO.BinaryWriter]::new($output)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]1)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$bytes.Length)
        $writer.Write([uint32]22)
        $writer.Write($bytes)
        $writer.Flush()
    } finally { $output.Dispose() }
} finally { $png.Dispose(); $bitmap.Dispose() }
Write-Host "Created $iconPath and $pngPath"
