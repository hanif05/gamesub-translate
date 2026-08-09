# Generates src/GameSubTranslate.App/App.ico — green rounded square + "GS" monogram,
# matching the runtime tray icon (BuildTrayIcon in App.xaml.cs).
# Sizes: 16, 32, 48, 256 (PNG-compressed for 256, BMP for smaller).
$ErrorActionPreference = 'Stop'

$out = Join-Path $PSScriptRoot '..\src\GameSubTranslate.App\App.ico'

Add-Type -AssemblyName System.Drawing

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # Rounded square background — green matching tray idle color (40,160,80).
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(40, 160, 80))
    $r = [Math]::Max(1, [int]($size * 0.22))
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r*2, $r*2, 180, 90)
    $path.AddArc($size - $r*2, 0, $r*2, $r*2, 270, 90)
    $path.AddArc($size - $r*2, $size - $r*2, $r*2, $r*2, 0, 90)
    $path.AddArc(0, $size - $r*2, $r*2, $r*2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bg, $path)

    # "GS" monogram centered.
    $fontSize = $size * 0.52
    $font = New-Object System.Drawing.Font('Arial', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $drawRect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $g.DrawString('GS', $font, $white, $drawRect, $sf)

    $g.Dispose()
    return $bmp
}

# PNG-compressed ico format (Vista+): header + 1 PNG image entry per size.
$sizes = @(256, 48, 32, 16)
$images = @()
foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += @{ Size = $s; Data = $ms.ToArray() }
    $bmp.Dispose()
    $ms.Dispose()
}

# ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes each)
$dir = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($dir)
$bw.Write([uint16]0)          # reserved
$bw.Write([uint16]1)          # type = icon
$bw.Write([uint16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $s = $img.Size
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([byte]0)                                        # palette
    $bw.Write([byte]0)                                        # reserved
    $bw.Write([uint16]1)                                      # planes
    $bw.Write([uint16]32)                                     # bpp
    $bw.Write([uint32]$img.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $img.Data.Length
}
foreach ($img in $images) { $bw.Write($img.Data) }

$bw.Flush()
[System.IO.File]::WriteAllBytes($out, $dir.ToArray())
$bw.Dispose()
$dir.Dispose()
Write-Host "Wrote $out ($($dir.Length) bytes) - $($images.Count) sizes"
