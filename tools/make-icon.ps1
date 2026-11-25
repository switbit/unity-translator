param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\assets\UnityTranslator.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$scale = 4

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $s = $size * $scale
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded blue tile.
    $tile = New-RoundedPath 0 0 $s $s ([float]($s * 0.20))
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 45, 108, 223),
        [System.Drawing.Color]::FromArgb(255, 24, 45, 122),
        90.0)
    $g.FillPath($brush, $tile)
    $brush.Dispose()
    $tile.Dispose()

    # Subtle highlight at the top.
    $glow = New-RoundedPath ($s * 0.06) ($s * 0.05) ($s * 0.88) ($s * 0.44) ([float]($s * 0.15))
    $glowBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.RectangleF 0, 0, $s, ($s * 0.5)),
        [System.Drawing.Color]::FromArgb(70, 255, 255, 255),
        [System.Drawing.Color]::FromArgb(0, 255, 255, 255),
        90.0)
    $g.FillPath($glowBrush, $glow)
    $glowBrush.Dispose()
    $glow.Dispose()

    # White speech bubble with a tail.
    $bubble = New-RoundedPath ($s * 0.10) ($s * 0.12) ($s * 0.80) ($s * 0.54) ([float]($s * 0.12))
    $g.FillPath([System.Drawing.Brushes]::White, $bubble)
    $bubble.Dispose()

    # Tail drawn separately so its overlap with the bubble stays solid white.
    $tail = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tail.AddPolygon(@(
        (New-Object System.Drawing.PointF ($s * 0.30), ($s * 0.58)),
        (New-Object System.Drawing.PointF ($s * 0.19), ($s * 0.87)),
        (New-Object System.Drawing.PointF ($s * 0.46), ($s * 0.61))
    ))
    $g.FillPath([System.Drawing.Brushes]::White, $tail)
    $tail.Dispose()

    # "A" + "Я" inside the bubble: Latin to Cyrillic.
    $em = [float]($s * 0.31)
    $font = New-Object System.Drawing.Font('Segoe UI', $em, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel))
    $measure = [System.Drawing.StringFormat]::GenericTypographic
    $measure.FormatFlags = $measure.FormatFlags -bor [System.Drawing.StringFormatFlags]::NoClip

    $letterA = 'A'
    $letterYa = [string][char]0x042F
    $sizeA = $g.MeasureString($letterA, $font, 1000, $measure)
    $sizeYa = $g.MeasureString($letterYa, $font, 1000, $measure)
    $gap = [float]($s * 0.025)
    $total = $sizeA.Width + $gap + $sizeYa.Width
    $x = [float](($s - $total) / 2)
    $textTop = [float]($s * 0.13)
    $textHeight = [float]($s * 0.50)

    $center = New-Object System.Drawing.StringFormat
    $center.Alignment = [System.Drawing.StringAlignment]::Center
    $center.LineAlignment = [System.Drawing.StringAlignment]::Center
    $center.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip

    $rectA = New-Object System.Drawing.RectangleF $x, $textTop, $sizeA.Width, $textHeight
    $rectYa = New-Object System.Drawing.RectangleF ($x + $sizeA.Width + $gap), $textTop, $sizeYa.Width, $textHeight

    $brushA = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 28, 78, 168))
    $brushYa = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 214, 69, 69))
    $g.DrawString($letterA, $font, $brushA, $rectA, $center)
    $g.DrawString($letterYa, $font, $brushYa, $rectYa, $center)
    $brushA.Dispose()
    $brushYa.Dispose()
    $font.Dispose()

    $g.Dispose()

    # Downscale to the target size for clean edges.
    $final = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $fg = [System.Drawing.Graphics]::FromImage($final)
    $fg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $fg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $fg.Clear([System.Drawing.Color]::Transparent)
    $fg.DrawImage($bmp, 0, 0, $size, $size)
    $fg.Dispose()
    $bmp.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $final.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $final.Dispose()
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

$images = @()
foreach ($size in $sizes) {
    $images += ,(New-IconPng $size)
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$file = [System.IO.File]::Create($OutFile)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0)          # reserved
$writer.Write([UInt16]1)          # icon type
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $data = $images[$i]
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]$(if ($size -ge 256) { 0 } else { $size }))
    $writer.Write([Byte]0)        # palette
    $writer.Write([Byte]0)        # reserved
    $writer.Write([UInt16]1)      # planes
    $writer.Write([UInt16]32)     # bpp
    $writer.Write([UInt32]$data.Length)
    $writer.Write([UInt32]$offset)
    $offset += $data.Length
}

foreach ($data in $images) {
    $writer.Write($data)
}

$writer.Flush()
$writer.Close()
$file.Close()

Write-Output ("{0} ({1} bytes, {2} sizes)" -f $OutFile, (Get-Item $OutFile).Length, $sizes.Count)
