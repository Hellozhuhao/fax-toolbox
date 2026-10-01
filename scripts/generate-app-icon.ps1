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
    $shape = New-RoundedPath 0 0 64 64 15
    try {
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Rectangle]::new(0, 0, 64, 64),
            [System.Drawing.Color]::FromArgb(255, 238, 154, 116),
            [System.Drawing.Color]::FromArgb(255, 198, 93, 64), 130.0)
        try { $graphics.FillPath($gradient, $shape) } finally { $gradient.Dispose() }
        $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 250, 246))
        $ink = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 203, 97, 68))
        $fold = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 244, 196, 178))
        $handlePen = [System.Drawing.Pen]::new($white.Color, 5.0)
        $detailPen = [System.Drawing.Pen]::new($ink.Color, 1.7)
        $seamPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(209, 203, 97, 68), 2.0)
        try {
            $handlePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $detailPen.StartCap = $detailPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $handle = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $handle.AddLine(19, 29, 19, 20)
            $handle.AddBezier(19, 20, 19, 17, 21, 15, 24, 15)
            $handle.AddLine(24, 15, 30, 15)
            $handle.AddBezier(30, 15, 33, 15, 35, 17, 35, 20)
            $handle.AddLine(35, 20, 35, 29)
            $paper = New-RoundedPath 37 10 14 20 2
            $case = New-RoundedPath 8 28 48 27 7
            $clasp = New-RoundedPath 29 35 6 9 2
            try {
                $graphics.DrawPath($handlePen, $handle)
                $graphics.FillPath($white, $paper)
                $graphics.FillPolygon($fold, [System.Drawing.Point[]]@(
                    [System.Drawing.Point]::new(46, 10), [System.Drawing.Point]::new(46, 15),
                    [System.Drawing.Point]::new(51, 15)))
                $graphics.DrawLine($detailPen, 40, 20, 48, 20)
                $graphics.DrawLine($detailPen, 40, 24, 46, 24)
                $graphics.FillPath($white, $case)
                $graphics.DrawLine($seamPen, 9, 39, 55, 39)
                $graphics.FillPath($ink, $clasp)
            } finally { $handle.Dispose(); $paper.Dispose(); $case.Dispose(); $clasp.Dispose() }
        } finally { $white.Dispose(); $ink.Dispose(); $fold.Dispose(); $handlePen.Dispose(); $detailPen.Dispose(); $seamPen.Dispose() }
    } finally { $shape.Dispose() }
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
