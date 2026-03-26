[CmdletBinding()]
param(
    [string]$Root = "C:\Codex\tmp_verify\library-workflows"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$libraryRoot = Join-Path $Root "library"
$sourceRoot = Join-Path $Root "source"
$exifToolPath = "C:\Codex\tools\exiftool.exe"

if (Test-Path $Root)
{
    Remove-Item $Root -Recurse -Force
}

New-Item -ItemType Directory -Path $libraryRoot -Force | Out-Null
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null

function New-TestJpeg {
    param(
        [string]$Path,
        [string]$Title,
        [string]$Subtitle,
        [string]$HexColor
    )

    $bitmap = New-Object System.Drawing.Bitmap 1280, 720
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    try
    {
        $background = [System.Drawing.ColorTranslator]::FromHtml($HexColor)
        $graphics.Clear($background)

        $overlay = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(60, 0, 0, 0))
        $graphics.FillRectangle($overlay, 48, 450, 1184, 200)
        $overlay.Dispose()

        $framePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255), 4)
        $graphics.DrawRectangle($framePen, 22, 22, 1236, 676)
        $framePen.Dispose()

        $titleFont = New-Object System.Drawing.Font("Segoe UI", 38, [System.Drawing.FontStyle]::Bold)
        $metaFont = New-Object System.Drawing.Font("Segoe UI", 18, [System.Drawing.FontStyle]::Regular)
        $subBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(232, 239, 242, 245))

        $graphics.DrawString($Title, $titleFont, [System.Drawing.Brushes]::White, 70, 500)
        $graphics.DrawString($Subtitle, $metaFont, $subBrush, 72, 566)
        $graphics.DrawString((Split-Path $Path -Leaf), $metaFont, $subBrush, 72, 604)

        $subBrush.Dispose()
        $titleFont.Dispose()
        $metaFont.Dispose()

        $jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" } | Select-Object -First 1
        $encoderParams = New-Object System.Drawing.Imaging.EncoderParameters 1
        $encoderParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, 92L)
        $bitmap.Save($Path, $jpegCodec, $encoderParams)
        $encoderParams.Dispose()
    }
    finally
    {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Write-ExifMetadata {
    param(
        [string]$Path,
        [string]$Timestamp,
        [string[]]$Tags,
        [string]$Comment
    )

    if (-not (Test-Path $exifToolPath))
    {
        return
    }

    $tagString = [string]::Join("||", ($Tags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }))
    $args = @(
        "-overwrite_original",
        "-sep",
        "||",
        "-XMP:Subject=$tagString",
        "-XMP-dc:Subject=$tagString",
        "-XMP:TagsList=$tagString",
        "-XMP-digiKam:TagsList=$tagString",
        "-XMP-lr:HierarchicalSubject=$tagString",
        "-IPTC:Keywords=$tagString",
        "-Keywords=$tagString",
        "-EXIF:DateTimeOriginal=$Timestamp",
        "-EXIF:CreateDate=$Timestamp",
        "-EXIF:ModifyDate=$Timestamp",
        "-XMP:DateTimeOriginal=$Timestamp",
        "-XMP:CreateDate=$Timestamp",
        "-XMP:ModifyDate=$Timestamp",
        "-XMP:MetadataDate=$Timestamp"
    )

    if ($Comment -ne $null)
    {
        $args += @(
            "-XMP-dc:Description-x-default=$Comment",
            "-XMP-dc:Description=$Comment",
            "-XMP-exif:UserComment=$Comment",
            "-EXIF:ImageDescription=$Comment",
            "-EXIF:UserComment=$Comment",
            "-IPTC:Caption-Abstract=$Comment"
        )
    }

    $args += $Path
    & $exifToolPath @args | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "ExifTool failed for $Path"
    }
}

$items = @(
    @{ Folder = "Alpha Quest"; Name = "Alpha Quest_20260301090001_01.jpg"; Title = "Alpha Quest"; Subtitle = "Steam capture A"; Color = "#2A475E"; Tags = @("Game Capture", "Steam"); Comment = "Alpha comment 1"; Timestamp = "20260301 09:00:01" },
    @{ Folder = "Alpha Quest"; Name = "Alpha Quest_20260301090002_02.jpg"; Title = "Alpha Quest"; Subtitle = "Steam capture B"; Color = "#34566D"; Tags = @("Game Capture", "Steam"); Comment = "Alpha comment 2"; Timestamp = "20260301 09:00:02" },
    @{ Folder = "Alpha Quest"; Name = "Alpha Quest_20260301090003_03.jpg"; Title = "Alpha Quest"; Subtitle = "Steam capture C"; Color = "#41667C"; Tags = @("Game Capture", "Steam"); Comment = "Alpha comment 3"; Timestamp = "20260301 09:00:03" },
    @{ Folder = "Beta Zone"; Name = "Beta Zone_20260301100001.jpg"; Title = "Beta Zone"; Subtitle = "PS5 capture A"; Color = "#0B5FFF"; Tags = @("Game Capture", "PS5", "PlayStation"); Comment = "Beta comment 1"; Timestamp = "20260301 10:00:01" },
    @{ Folder = "Beta Zone"; Name = "Beta Zone_20260301100002.jpg"; Title = "Beta Zone"; Subtitle = "PS5 capture B"; Color = "#2F73FF"; Tags = @("Game Capture", "PS5", "PlayStation"); Comment = "Beta comment 2"; Timestamp = "20260301 10:00:02" },
    @{ Folder = "Gamma Trials"; Name = "Gamma Trials-2026_03_01-11_15_01.jpg"; Title = "Gamma Trials"; Subtitle = "Xbox capture A"; Color = "#107C10"; Tags = @("Game Capture", "Xbox"); Comment = "Gamma comment 1"; Timestamp = "20260301 11:15:01" },
    @{ Folder = "Gamma Trials"; Name = "Gamma Trials-2026_03_01-11_15_02.jpg"; Title = "Gamma Trials"; Subtitle = "Xbox capture B"; Color = "#239423"; Tags = @("Game Capture", "Xbox"); Comment = "Gamma comment 2"; Timestamp = "20260301 11:15:02" },
    @{ Folder = "Delta Mix"; Name = "Delta Mix manual 01.jpg"; Title = "Delta Mix"; Subtitle = "Custom platform capture"; Color = "#8A4B00"; Tags = @("Game Capture", "Platform:Switch"); Comment = "Delta comment 1"; Timestamp = "20260301 12:00:01" }
)

foreach ($item in $items)
{
    $folderPath = Join-Path $libraryRoot $item.Folder
    New-Item -ItemType Directory -Path $folderPath -Force | Out-Null
    $filePath = Join-Path $folderPath $item.Name
    New-TestJpeg -Path $filePath -Title $item.Title -Subtitle $item.Subtitle -HexColor $item.Color
    Write-ExifMetadata -Path $filePath -Timestamp $item.Timestamp -Tags $item.Tags -Comment $item.Comment
}

$sourceItems = @(
    @{ Name = "VERIFY IMPORT_20260302120001_01.jpg"; Title = "Verify Import"; Subtitle = "Steam source"; Color = "#526D82" },
    @{ Name = "VERIFY IMPORT-2026_03_02-12_15_01.jpg"; Title = "Verify Import"; Subtitle = "Xbox source"; Color = "#2A7E2A" },
    @{ Name = "VERIFY IMPORT manual review 01.jpg"; Title = "Verify Import"; Subtitle = "Manual review source"; Color = "#A96300" }
)

foreach ($item in $sourceItems)
{
    $filePath = Join-Path $sourceRoot $item.Name
    New-TestJpeg -Path $filePath -Title $item.Title -Subtitle $item.Subtitle -HexColor $item.Color
}

Write-Host "Verification data created."
Write-Host "Library root: $libraryRoot"
Write-Host "Source root:  $sourceRoot"
