$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$destination = Join-Path $PSScriptRoot '..\assets\icons'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$colors = @{ blue = '#1565C0'; green = '#237A37'; orange = '#B95000'; red = '#C62828'; purple = '#7534AD'; teal = '#007C83' }
foreach ($label in @('MC', 'MT')) {
    foreach ($colorName in $colors.Keys) {
      foreach ($inverted in @($false, $true)) {
        $frames = @()
        foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
            $bitmap = New-Object System.Drawing.Bitmap($size, $size)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.SmoothingMode = 'AntiAlias'
            $graphics.TextRenderingHint = 'AntiAliasGridFit'
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $brush = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml($colors[$colorName]))
            if ($inverted) {
                $stroke = [Math]::Max(1.2, $size * 0.055)
                $inset = $stroke / 2 + $size * 0.025
                $extent = $size - 2 * $inset
                $diameter = $size * 0.30
                $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
                $shape.AddArc($inset, $inset, $diameter, $diameter, 180, 90)
                $shape.AddArc(($inset + $extent - $diameter), $inset, $diameter, $diameter, 270, 90)
                $shape.AddArc(($inset + $extent - $diameter), ($inset + $extent - $diameter), $diameter, $diameter, 0, 90)
                $shape.AddArc($inset, ($inset + $extent - $diameter), $diameter, $diameter, 90, 90)
                $shape.CloseFigure()
                $pen = New-Object System.Drawing.Pen($brush, $stroke)
                $graphics.DrawPath($pen, $shape)
                $pen.Dispose(); $shape.Dispose()
            } else { $graphics.FillRectangle($brush, 0, 0, $size, $size) }
            $font = New-Object System.Drawing.Font('Arial', ($size * 0.48), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
            $format = New-Object System.Drawing.StringFormat
            $format.Alignment = 'Center'
            $format.LineAlignment = 'Center'
            $textBrush = if ($inverted) { $brush } else { [System.Drawing.Brushes]::White }
            $graphics.DrawString($label, $font, $textBrush, (New-Object System.Drawing.RectangleF(0, 0, $size, $size)), $format)
            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames += ,($stream.ToArray())
            $stream.Dispose(); $format.Dispose(); $font.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
        }
        $suffix = if ($inverted) { '-inverted' } else { '' }
        $path = Join-Path $destination ($label.ToLowerInvariant() + '-' + $colorName + $suffix + '.ico')
        $file = [IO.File]::Create($path)
        $writer = New-Object System.IO.BinaryWriter($file)
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        $sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
        for ($index = 0; $index -lt $frames.Count; $index++) {
            $dimension = $sizes[$index] % 256
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
        $writer.Dispose()
      }
    }
}
