# Draws the app icon (a blue key cap with a white keyboard) and writes app.ico with PNG frames.
# Run from the repository root: pwsh tools/make-icon.ps1
param([string]$Output = "app.ico")

Add-Type -AssemblyName System.Drawing

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Frame([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    # Draw in a 256 x 256 grid and scale it to the frame size.
    $g.ScaleTransform($size / 256.0, $size / 256.0)

    $blue = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(0x25, 0x63, 0xEB))
    $background = New-RoundedRect 8 8 240 240 52
    $g.FillPath($blue, $background)

    # Keyboard body.
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $body = New-RoundedRect 36 72 184 112 18
    $g.FillPath($white, $body)

    # Keys: three rows cut out in blue, and a space bar.
    $key = 22; $gap = 8
    $rows = @(@{ Y = 86; Count = 6; Left = 42 }, @{ Y = 116; Count = 5; Left = 57 })
    foreach ($row in $rows) {
        for ($i = 0; $i -lt $row.Count; $i++) {
            $k = New-RoundedRect ($row.Left + $i * ($key + $gap)) $row.Y $key $key 5
            $g.FillPath($blue, $k)
            $k.Dispose()
        }
    }
    $space = New-RoundedRect 77 148 102 22 5
    $g.FillPath($blue, $space)

    foreach ($o in $space, $body, $background, $white, $blue, $g) { $o.Dispose() }
    return $bitmap
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) {
    $bitmap = New-Frame $size
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

# ICO file: header, one directory entry per frame, then the PNG data.
$out = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($out)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $Output), $out.ToArray())
Write-Output "Wrote $Output"
