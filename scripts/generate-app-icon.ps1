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
            [System.Drawing.Color]::FromArgb(255, 236, 154, 115),
            [System.Drawing.Color]::FromArgb(255, 198, 91, 61), 130.0)
        try { $graphics.FillPath($gradient, $shape) } finally { $gradient.Dispose() }
        $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 250, 246))
        $ink = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 213, 108, 75))
        $slot = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 213, 108, 75), 3.0)
        try {
            $slot.StartCap = $slot.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $paper = New-RoundedPath 18 8 28 28 3
            $machine = New-RoundedPath 11 30 42 21 6
            try {
                $graphics.FillPath($white, $paper)
                foreach ($tile in @(@(23,14), @(34,14), @(23,24), @(34,24))) {
                    $module = New-RoundedPath $tile[0] $tile[1] 7 7 2
                    try { $graphics.FillPath($ink, $module) } finally { $module.Dispose() }
                }
                $graphics.FillPath($white, $machine)
                $graphics.DrawLine($slot, 19, 45, 45, 45)
                $graphics.FillEllipse($ink, 43, 35, 4, 4)
                $graphics.FillRectangle($white, 21, 50, 22, 5)
            } finally { $paper.Dispose(); $machine.Dispose() }
        } finally { $white.Dispose(); $ink.Dispose(); $slot.Dispose() }
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
