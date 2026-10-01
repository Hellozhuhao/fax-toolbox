$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assetDirectory = Join-Path $PSScriptRoot '..\apps\windows-shell\assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$iconPath = Join-Path $assetDirectory 'fax-toolbox.ico'
$bitmap = [System.Drawing.Bitmap]::new(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $bounds = [System.Drawing.Rectangle]::new(12, 12, 232, 232)
    $shape = [System.Drawing.Drawing2D.GraphicsPath]::new()
    try {
        $shape.AddArc(12, 12, 64, 64, 180, 90)
        $shape.AddArc(180, 12, 64, 64, 270, 90)
        $shape.AddArc(180, 180, 64, 64, 0, 90)
        $shape.AddArc(12, 180, 64, 64, 90, 90)
        $shape.CloseFigure()
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new($bounds,
            [System.Drawing.Color]::FromArgb(255, 239, 151, 109),
            [System.Drawing.Color]::FromArgb(255, 193, 79, 52), 130.0)
        try { $graphics.FillPath($gradient, $shape) } finally { $gradient.Dispose() }
        $font = [System.Drawing.Font]::new('Segoe UI', 154,
            [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        try {
            $format = [System.Drawing.StringFormat]::new()
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString('F', $font, [System.Drawing.Brushes]::White,
                [System.Drawing.RectangleF]::new(16, 6, 224, 232), $format)
        } finally { $font.Dispose() }
    } finally { $shape.Dispose() }
} finally { $graphics.Dispose() }

$png = [System.IO.MemoryStream]::new()
try {
    $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $png.ToArray()
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
Write-Host "Created $iconPath"
