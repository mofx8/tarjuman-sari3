# Generates the Quick Translate icon (Assets/app.ico + docs/logo.png). Run: pwsh tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/QuickTranslate.App/Assets'
$docs = Join-Path $root 'docs'
New-Item -ItemType Directory $assets, $docs -Force | Out-Null

function New-Logo([int]$size) {
    $s = 1024.0
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.ScaleTransform($size / $s, $size / $s)

    function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure(); return $p
    }

    # background tile
    $tile = RoundRect 24 24 976 976 230
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(1024, 1024)), ([System.Drawing.Color]::FromArgb(255, 79, 70, 229)), ([System.Drawing.Color]::FromArgb(255, 6, 182, 212))
    $g.FillPath($bg, $tile)
    # soft highlight
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(0, 560)), ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $hl.WrapMode = [System.Drawing.Drawing2D.WrapMode]::TileFlipXY; $g.SetClip($tile); $g.FillRectangle($hl, 0, 0, 1024, 561); $g.ResetClip()

    # back bubble (Latin) - white, upper left, tail bottom-left
    $b1 = RoundRect 130 150 520 400 120
    $tail1 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tail1.AddPolygon([System.Drawing.PointF[]]@([System.Drawing.PointF]::new(210, 520), [System.Drawing.PointF]::new(190, 680), [System.Drawing.PointF]::new(360, 548)))
    $shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 15, 23, 42))
    $g.TranslateTransform(0, 14); $g.FillPath($shadow, $b1); $g.FillPath($shadow, $tail1); $g.TranslateTransform(0, -14)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $g.FillPath($white, $b1); $g.FillPath($white, $tail1)
    $fontA = New-Object System.Drawing.Font 'Segoe UI', 250, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 67, 56, 202))
    $g.DrawString('A', $fontA, $ink, [System.Drawing.RectangleF]::new(130, 150, 520, 400), $sf)

    # front bubble (Arabic) - dark, lower right, tail bottom-right
    $b2 = RoundRect 360 430 540 420 125
    $tail2 = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tail2.AddPolygon([System.Drawing.PointF[]]@([System.Drawing.PointF]::new(800, 820), [System.Drawing.PointF]::new(830, 960), [System.Drawing.PointF]::new(660, 840)))
    $g.TranslateTransform(0, 14); $g.FillPath($shadow, $b2); $g.FillPath($shadow, $tail2); $g.TranslateTransform(0, -14)
    $navy = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 15, 23, 42))
    $g.FillPath($navy, $b2); $g.FillPath($navy, $tail2)
    $fontAr = New-Object System.Drawing.Font 'Segoe UI', 330, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf2 = New-Object System.Drawing.StringFormat; $sf2.Alignment = 'Center'; $sf2.LineAlignment = 'Center'
    $cyan = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 103, 232, 249))
    $g.DrawString([string][char]0x0639, $fontAr, $cyan, [System.Drawing.RectangleF]::new(360, 400, 540, 430), $sf2)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $b = New-Logo $sz
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @($sz, $ms.ToArray())
    $b.Dispose()
}
# ICO container with PNG-compressed images
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $sz = $p[0]; $data = $p[1]
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($p in $pngs) { $w.Write($p[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $out.ToArray())

$big = New-Logo 512
$big.Save((Join-Path $docs 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose()
Write-Host 'Icon written.'


