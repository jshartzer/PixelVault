[CmdletBinding()]
param()
# SVG is the single geometry/color source for every export.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
[xml]$svg = Get-Content -Raw (Join-Path $assets 'PixelVault-Monogram.svg')
$view = $svg.DocumentElement.GetAttribute('viewBox').Split(' ') | ForEach-Object { [double]$_ }
function New-BrandPng([int]$size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size / $view[2], $size / $view[3]))
    $drawing.PushTransform([System.Windows.Media.TranslateTransform]::new(-$view[0], -$view[1]))
    foreach ($node in $svg.DocumentElement.ChildNodes) {
        if ($node.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($node.GetAttribute('fill'))
        if ($node.LocalName -eq 'path') {
            $geometry = [System.Windows.Media.Geometry]::Parse($node.GetAttribute('d'))
            $drawing.DrawGeometry($brush, $null, $geometry)
        } elseif ($node.LocalName -eq 'rect') {
            $rect = [System.Windows.Rect]::new([double]$node.x, [double]$node.y, [double]$node.width, [double]$node.height)
            $drawing.DrawRoundedRectangle($brush, $null, $rect, [double]$node.rx, [double]$node.rx)
        } else { throw "Unsupported SVG element: $($node.LocalName)" }
    }
    $drawing.Pop(); $drawing.Pop(); $drawing.Close()
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}
[System.IO.File]::WriteAllBytes((Join-Path $assets 'PixelVault.png'), (New-BrandPng 1024))
# Each ICO size is rendered from the vector to retain crisp small icons.
$sizes = @(16,24,32,48,64,128,256)
$frames = @($sizes | ForEach-Object { ,(New-BrandPng $_) })
$stream = [System.IO.File]::Create((Join-Path $assets 'PixelVault.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i=0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Copy-Item (Join-Path $assets 'PixelVault.png') (Join-Path $root 'src/PixelVault.Companion/wwwroot/brand.png') -Force
Write-Host 'Generated PNG, multi-size ICO, and Companion branding from the approved monogram SVG.'
