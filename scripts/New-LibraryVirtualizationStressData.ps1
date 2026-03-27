[CmdletBinding()]
param(
    [string]$Root = "C:\Codex\tmp_verify\library-virtualization-stress",
    [int]$FolderCount = 96,
    [int]$FilesPerFolder = 12,
    [int]$MegaFolderFileCount = 180,
    [switch]$SkipVideos
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$libraryRoot = Join-Path $Root "library"
$sourceRoot = Join-Path $Root "source"
$ffmpegPath = "C:\Codex\tools\ffmpeg.exe"

if (Test-Path $Root)
{
    Remove-Item $Root -Recurse -Force
}

New-Item -ItemType Directory -Path $libraryRoot -Force | Out-Null
New-Item -ItemType Directory -Path $sourceRoot -Force | Out-Null

function New-StressJpeg {
    param(
        [string]$Path,
        [string]$Title,
        [string]$Subtitle,
        [string]$HexColor
    )

    $bitmap = New-Object System.Drawing.Bitmap 640, 360
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    try
    {
        $background = [System.Drawing.ColorTranslator]::FromHtml($HexColor)
        $graphics.Clear($background)

        $overlay = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(76, 8, 12, 16))
        $graphics.FillRectangle($overlay, 24, 208, 592, 120)
        $overlay.Dispose()

        $framePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(105, 255, 255, 255), 3)
        $graphics.DrawRectangle($framePen, 14, 14, 610, 332)
        $framePen.Dispose()

        $titleFont = New-Object System.Drawing.Font("Segoe UI", 20, [System.Drawing.FontStyle]::Bold)
        $metaFont = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Regular)
        $metaBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 239, 244, 248))

        $graphics.DrawString($Title, $titleFont, [System.Drawing.Brushes]::White, 38, 228)
        $graphics.DrawString($Subtitle, $metaFont, $metaBrush, 40, 266)
        $graphics.DrawString((Split-Path $Path -Leaf), $metaFont, $metaBrush, 40, 290)

        $metaBrush.Dispose()
        $titleFont.Dispose()
        $metaFont.Dispose()

        $jpegCodec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" } | Select-Object -First 1
        $encoderParams = New-Object System.Drawing.Imaging.EncoderParameters 1
        $encoderParams.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, 88L)
        $bitmap.Save($Path, $jpegCodec, $encoderParams)
        $encoderParams.Dispose()
    }
    finally
    {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function New-StressVideo {
    param(
        [string]$Path,
        [string]$HexColor,
        [double]$DurationSeconds = 2.4
    )

    if ($SkipVideos -or -not (Test-Path $ffmpegPath))
    {
        return
    }

    $color = $HexColor.TrimStart('#')
    $args = @(
        "-hide_banner",
        "-loglevel", "error",
        "-y",
        "-f", "lavfi",
        "-i", "color=c=0x${color}:s=640x360:r=30",
        "-f", "lavfi",
        "-i", "sine=frequency=880:sample_rate=44100",
        "-t", ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:0.0}", $DurationSeconds)),
        "-shortest",
        "-c:v", "libx264",
        "-pix_fmt", "yuv420p",
        "-preset", "veryfast",
        "-c:a", "aac",
        "-b:a", "96k",
        "-movflags", "+faststart",
        $Path
    )

    & $ffmpegPath @args | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "FFmpeg failed for $Path"
    }
}

$palette = @(
    "#2A475E",
    "#34566D",
    "#41667C",
    "#0B5FFF",
    "#2F73FF",
    "#107C10",
    "#239423",
    "#8A4B00",
    "#7A355D",
    "#595959"
)

$baseTime = [DateTime]::Parse("2026-04-01T08:00:00")

for ($folderIndex = 1; $folderIndex -le $FolderCount; $folderIndex++)
{
    $folderName = "Stress Game {0:D3}" -f $folderIndex
    $folderPath = Join-Path $libraryRoot $folderName
    New-Item -ItemType Directory -Path $folderPath -Force | Out-Null

    for ($fileIndex = 1; $fileIndex -le $FilesPerFolder; $fileIndex++)
    {
        $stamp = $baseTime.AddMinutes((($folderIndex - 1) * 19) + $fileIndex)
        $color = $palette[($folderIndex + $fileIndex) % $palette.Count]
        $isVideo = (-not $SkipVideos) -and (Test-Path $ffmpegPath) -and ($fileIndex % 5 -eq 0)

        if ($isVideo)
        {
            $unixStamp = [DateTimeOffset]::new($stamp).ToUnixTimeMilliseconds()
            $fileName = "clip_{0}.mp4" -f $unixStamp
            New-StressVideo -Path (Join-Path $folderPath $fileName) -HexColor $color
        }
        elseif ($fileIndex % 2 -eq 0)
        {
            $fileName = "{0}_{1}_{2:D2}.jpg" -f $folderName, $stamp.ToString("yyyyMMddHHmmss"), $fileIndex
            New-StressJpeg -Path (Join-Path $folderPath $fileName) -Title $folderName -Subtitle "Stress image $fileIndex" -HexColor $color
        }
        else
        {
            $fileName = "{0}-{1}.jpg" -f $folderName, $stamp.ToString("yyyy_MM_dd-HH_mm_ss")
            New-StressJpeg -Path (Join-Path $folderPath $fileName) -Title $folderName -Subtitle "Stress image $fileIndex" -HexColor $color
        }
    }
}

$megaFolderPath = Join-Path $libraryRoot "Mega Mix"
New-Item -ItemType Directory -Path $megaFolderPath -Force | Out-Null
for ($megaIndex = 1; $megaIndex -le $MegaFolderFileCount; $megaIndex++)
{
    $stamp = $baseTime.AddDays(2).AddSeconds($megaIndex * 41)
    $color = $palette[$megaIndex % $palette.Count]
    $isVideo = (-not $SkipVideos) -and (Test-Path $ffmpegPath) -and ($megaIndex % 6 -eq 0)

    if ($isVideo)
    {
        $fileName = "Mega Mix clip_{0}.mp4" -f ([DateTimeOffset]::new($stamp).ToUnixTimeMilliseconds())
        New-StressVideo -Path (Join-Path $megaFolderPath $fileName) -HexColor $color -DurationSeconds 3.2
    }
    elseif ($megaIndex % 3 -eq 0)
    {
        $fileName = "Mega Mix_{0}_{1:D3}.jpg" -f $stamp.ToString("yyyyMMddHHmmss"), $megaIndex
        New-StressJpeg -Path (Join-Path $megaFolderPath $fileName) -Title "Mega Mix" -Subtitle "Large mixed-media folder item $megaIndex" -HexColor $color
    }
    else
    {
        $fileName = "Mega Mix-{0}-{1:D3}.jpg" -f $stamp.ToString("yyyy_MM_dd-HH_mm_ss"), $megaIndex
        New-StressJpeg -Path (Join-Path $megaFolderPath $fileName) -Title "Mega Mix" -Subtitle "Large mixed-media folder item $megaIndex" -HexColor $color
    }
}

for ($sourceIndex = 1; $sourceIndex -le 18; $sourceIndex++)
{
    $color = $palette[$sourceIndex % $palette.Count]
    $sourceName = "STRESS SOURCE_{0}_{1:D2}.jpg" -f $baseTime.AddHours($sourceIndex).ToString("yyyyMMddHHmmss"), $sourceIndex
    New-StressJpeg -Path (Join-Path $sourceRoot $sourceName) -Title "Stress Source" -Subtitle "Source item $sourceIndex" -HexColor $color
}

$folderTotal = (Get-ChildItem $libraryRoot -Directory).Count
$fileTotal = (Get-ChildItem $libraryRoot -Recurse -File).Count
$videoTotal = (Get-ChildItem $libraryRoot -Recurse -File -Include *.mp4,*.mkv,*.avi,*.mov,*.wmv,*.webm).Count

Write-Host "Stress-test data created."
Write-Host "Library root: $libraryRoot"
Write-Host "Source root:  $sourceRoot"
Write-Host "Folders:      $folderTotal"
Write-Host "Files:        $fileTotal"
Write-Host "Videos:       $videoTotal"
