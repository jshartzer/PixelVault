[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$appRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$assetsRoot = Join-Path $appRoot 'assets'

if (-not (Test-Path -LiteralPath $assetsRoot)) {
    New-Item -ItemType Directory -Path $assetsRoot | Out-Null
}

$svgConcept1 = @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="56" fill="#101417"/>
  <path fill="#F7F3EB" d="M79 86h98c15 0 28 11 31 26l5 22c5 20-11 39-31 39h-8c-8 0-16-4-21-11l-10-13h-30l-10 13c-5 7-13 11-21 11h-8c-20 0-36-19-31-39l5-22c3-15 16-26 31-26z"/>
  <circle cx="128" cy="127" r="30" fill="#101417"/>
  <path fill="#F7F3EB" d="M128 106l9 5 9 1-6 8 1 10-9-5-9 5 1-10-6-8 9-1z"/>
  <rect x="88" y="117" width="20" height="8" rx="4" fill="#101417"/>
  <rect x="94" y="111" width="8" height="20" rx="4" fill="#101417"/>
  <circle cx="166" cy="118" r="5" fill="#101417"/>
  <circle cx="178" cy="130" r="5" fill="#101417"/>
</svg>
'@

$svgConcept2 = @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="56" fill="#101417"/>
  <path fill="#F7F3EB" d="M71 98c5-17 20-28 38-28h38c4 0 7 1 10 3l18 14h12c18 0 33 11 38 28l7 23c4 16-8 32-24 32h-17c-7 0-13-3-17-7l-19-21H99l-18 21c-4 4-10 7-17 7H47c-16 0-28-16-24-32l7-23z"/>
  <circle cx="128" cy="114" r="26" fill="#101417"/>
  <circle cx="128" cy="114" r="15" fill="#F7F3EB"/>
  <rect x="82" y="114" width="18" height="7" rx="3.5" fill="#101417"/>
  <rect x="87.5" y="108.5" width="7" height="18" rx="3.5" fill="#101417"/>
  <circle cx="168" cy="111" r="4.5" fill="#101417"/>
  <circle cx="178" cy="121" r="4.5" fill="#101417"/>
</svg>
'@

$svgConcept3 = @'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  <rect width="256" height="256" rx="56" fill="#F7F3EB"/>
  <path fill="#101417" d="M77 90h102c15 0 28 11 31 26l4 18c5 21-11 41-31 41h-10c-8 0-15-4-20-10l-10-11h-30l-10 11c-5 6-12 10-20 10H73c-20 0-36-20-31-41l4-18c3-15 16-26 31-26z"/>
  <circle cx="128" cy="123" r="27" fill="#F7F3EB"/>
  <path fill="#101417" d="M128 105c10 0 18 8 18 18s-8 18-18 18-18-8-18-18 8-18 18-18zm0 6c-7 0-12 5-12 12s5 12 12 12 12-5 12-12-5-12-12-12z"/>
  <rect x="89" y="116" width="18" height="7" rx="3.5" fill="#F7F3EB"/>
  <rect x="94.5" y="110.5" width="7" height="18" rx="3.5" fill="#F7F3EB"/>
  <circle cx="167" cy="116" r="4.5" fill="#F7F3EB"/>
  <circle cx="177" cy="126" r="4.5" fill="#F7F3EB"/>
</svg>
'@

Set-Content -LiteralPath (Join-Path $assetsRoot 'PixelVault-Concept-01.svg') -Value $svgConcept1 -Encoding UTF8
Set-Content -LiteralPath (Join-Path $assetsRoot 'PixelVault-Concept-02.svg') -Value $svgConcept2 -Encoding UTF8
Set-Content -LiteralPath (Join-Path $assetsRoot 'PixelVault-Concept-03.svg') -Value $svgConcept3 -Encoding UTF8

function New-RoundedRectPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$size = 256
$bitmap = New-Object System.Drawing.Bitmap $size, $size
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(0x10, 0x14, 0x17))

$ivory = [System.Drawing.Color]::FromArgb(0xF7, 0xF3, 0xEB)
$ink = [System.Drawing.Color]::FromArgb(0x10, 0x14, 0x17)
$mint = [System.Drawing.Color]::FromArgb(0x67, 0xD5, 0xB5)

$bodyBrush = New-Object System.Drawing.SolidBrush $ivory
$darkBrush = New-Object System.Drawing.SolidBrush $ink
$mintBrush = New-Object System.Drawing.SolidBrush $mint
$pen = New-Object System.Drawing.Pen $mint, 8
$pen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Center

$controllerPath = New-RoundedRectPath -x 40 -y 76 -width 176 -height 96 -radius 34
$graphics.FillPath($bodyBrush, $controllerPath)

$leftGrip = New-RoundedRectPath -x 48 -y 118 -width 48 -height 68 -radius 22
$rightGrip = New-RoundedRectPath -x 160 -y 118 -width 48 -height 68 -radius 22
$graphics.FillPath($bodyBrush, $leftGrip)
$graphics.FillPath($bodyBrush, $rightGrip)

$graphics.FillEllipse($darkBrush, 92, 92, 72, 72)
$graphics.DrawEllipse($pen, 100, 100, 56, 56)
$graphics.FillEllipse($mintBrush, 118, 118, 20, 20)

$graphics.FillRectangle($darkBrush, 76, 120, 22, 8)
$graphics.FillRectangle($darkBrush, 83, 113, 8, 22)
$graphics.FillEllipse($darkBrush, 165, 116, 10, 10)
$graphics.FillEllipse($darkBrush, 178, 129, 10, 10)

$graphics.Dispose()

$pngPath = Join-Path $assetsRoot 'PixelVault.png'
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$iconPath = Join-Path $assetsRoot 'PixelVault.ico'
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [System.IO.File]::Create($iconPath)
$icon.Save($stream)
$stream.Close()

$icon.Dispose()
$bitmap.Dispose()

Write-Host "Branding assets generated in $assetsRoot"
