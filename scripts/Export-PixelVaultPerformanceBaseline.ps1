param(
    [string]$LogPath = "C:\Codex\PixelVaultData\logs\PixelVault-native.log",
    [string]$OutputPath,
    [datetimeoffset]$Since,
    [int]$SlowSampleCount = 8
)

$ErrorActionPreference = "Stop"

function Resolve-DefaultOutputPath {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $dateStamp = Get-Date -Format "yyyy-MM-dd"
    Join-Path $repoRoot "docs\perf\PV-PLN-PERF-001-baseline-$dateStamp.md"
}

function Parse-PerfRows {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Performance log not found: $Path"
    }

    Select-String -LiteralPath $Path -Pattern "PERF \|" | ForEach-Object {
        $line = $_.Line
        if ($line -notmatch "^\[(?<timestamp>[^\]]+)\]\s+PERF \| (?<area>[^|]+) \| (?<ms>\d+) ms \| T=(?<thread>\d+)(?: \| (?<detail>.*))?$") {
            return
        }

        $timestampText = $matches["timestamp"]
        $area = $matches["area"].Trim()
        $ms = [int]$matches["ms"]
        $thread = [int]$matches["thread"]
        $detail = ""
        if ($matches.ContainsKey("detail")) {
            $detail = [string]$matches["detail"]
        }

        $session = ""
        if ($detail -match "(^|[ ;|])S=(?<session>[^ ;|]+)") {
            $session = $matches["session"]
        }

        [pscustomobject]@{
            Timestamp = [datetimeoffset]::Parse($timestampText)
            Area = $area
            Ms = $ms
            Thread = $thread
            Session = $session
            Detail = $detail
            LineNumber = $_.LineNumber
        }
    }
}

function Get-PercentileValue {
    param(
        [int[]]$Values,
        [double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $index = [Math]::Ceiling($Values.Count * $Percentile) - 1
    $index = [Math]::Max(0, [Math]::Min($Values.Count - 1, $index))
    $Values[$index]
}

function Format-StatsTable {
    param($Rows)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("| Area | Count | Min | Median | P95 | Max | Latest |")
    $lines.Add("|------|------:|----:|-------:|----:|----:|--------|")

    $Rows |
        Group-Object Area |
        Sort-Object Name |
        ForEach-Object {
            $group = @($_.Group | Sort-Object Ms)
            $values = [int[]]($group | Select-Object -ExpandProperty Ms)
            $median = Get-PercentileValue -Values $values -Percentile 0.50
            $p95 = Get-PercentileValue -Values $values -Percentile 0.95
            $latest = ($group | Sort-Object Timestamp | Select-Object -Last 1).Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            $lines.Add("| $($_.Name) | $($values.Count) | $($values[0]) ms | $median ms | $p95 ms | $($values[-1]) ms | $latest |")
        }

    $lines
}

function Format-SlowSamples {
    param(
        $Rows,
        [string[]]$Areas,
        [int]$Count
    )

    $lines = New-Object System.Collections.Generic.List[string]
    $samples = @($Rows | Where-Object { $Areas -contains $_.Area } | Sort-Object Ms -Descending | Select-Object -First $Count)

    if ($samples.Count -eq 0) {
        $lines.Add("_No samples found._")
        return $lines
    }

    $lines.Add("| Area | Time | Duration | Detail |")
    $lines.Add("|------|------|---------:|--------|")
    foreach ($sample in $samples) {
        $detail = ($sample.Detail -replace "\|", "/" -replace "\s+", " ").Trim()
        if ($detail.Length -gt 180) {
            $detail = $detail.Substring(0, 177) + "..."
        }

        $lines.Add("| $($sample.Area) | $($sample.Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")) | $($sample.Ms) ms | $detail |")
    }

    $lines
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Resolve-DefaultOutputPath
}

$rows = @(Parse-PerfRows -Path $LogPath)
if ($PSBoundParameters.ContainsKey("Since")) {
    $rows = @($rows | Where-Object { $_.Timestamp -ge $Since })
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$capturedRange = "none"
if ($rows.Count -gt 0) {
    $first = ($rows | Sort-Object Timestamp | Select-Object -First 1).Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
    $last = ($rows | Sort-Object Timestamp | Select-Object -Last 1).Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
    $capturedRange = "$first to $last"
}

$content = New-Object System.Collections.Generic.List[string]
$content.Add("# PV-PLN-PERF-001 baseline capture")
$content.Add("")
$content.Add("| Field | Value |")
$content.Add("|-------|-------|")
$content.Add("| Plan | PV-PLN-PERF-001 |")
$content.Add("| Generated | $(Get-Date -Format "yyyy-MM-dd HH:mm:ss K") |")
$content.Add('| Source log | `' + $LogPath + '` |')
$content.Add("| Rows parsed | $($rows.Count) |")
$content.Add("| Captured range | $capturedRange |")
if ($PSBoundParameters.ContainsKey("Since")) {
    $content.Add("| Since filter | $($Since.ToString("yyyy-MM-dd HH:mm:ss K")) |")
}
$content.Add("")
$content.Add("## Summary")
$content.Add("")
if ($rows.Count -eq 0) {
    $content.Add("_No PERF rows found._")
} else {
    foreach ($line in (Format-StatsTable -Rows $rows)) {
        $content.Add([string]$line)
    }
}
$content.Add("")
$content.Add("## Slowest library samples")
$content.Add("")
foreach ($line in (Format-SlowSamples -Rows $rows -Areas @("LibraryBrowserFirstFolderListPaint", "LibraryBrowserFirstDetailPaint", "LibraryDetailRender", "LibraryFolderRender", "LibraryFolderCache") -Count $SlowSampleCount)) {
    $content.Add([string]$line)
}
$content.Add("")
$content.Add("## Slowest import/intake samples")
$content.Add("")
foreach ($line in (Format-SlowSamples -Rows $rows -Areas @("IntakePreviewBuild", "ImportPreparation", "ManualIntakePreparation", "ImportWorkflowRun", "ImportWorkflowStep") -Count $SlowSampleCount)) {
    $content.Add([string]$line)
}
$content.Add("")
$content.Add("## Manual capture matrix")
$content.Add("")
$content.Add("| Flow | Current baseline status | Notes |")
$content.Add("|------|-------------------------|-------|")
$content.Add('| App open -> library visible | Captured by `LibraryBrowserFirstFolderListPaint` and `LibraryBrowserFirstDetailPaint`. | Re-run after each slice from a cold app start. |')
$content.Add("| Import 25 files | Needs clean manual sample. | Current logs include smaller import/intake samples, but not a labeled 25-file run. |")
$content.Add("| Import 100 files | Needs clean manual sample. | Add one run before Phase B changes land. |")
$content.Add("| Import 500 files | Needs clean manual sample. | Use copied/staged captures only; do not mutate the real source set for measurement. |")
$content.Add("| Import HDR PNG/JXR pairs | Needs clean manual sample. | Capture both intake preview and final import progress logs. |")
$content.Add('| Open a large game folder in photo view | Captured by `LibraryDetailRender`. | Diablo IV and Timeline samples are the current large-folder stand-ins. |')
$content.Add('| Fast-scroll detail pane | Partially captured by repeated `LibraryFolderRender`/`LibraryDetailRender` samples. | Add an explicit scroll-pass note when testing UI changes. |')
$content.Add("")
$content.Add("## Reading guidance")
$content.Add("")
$content.Add('- Treat very large `LibraryBrowserFirstDetailPaint` values as suspect when the app sat open before a first detail render; use fresh cold-start samples for comparison.')
$content.Add('- `quickMediaMapMs` spikes inside `LibraryDetailRender` are the most useful photo-view clue from the current logs.')
$content.Add('- Builds with Phase A step 2 instrumentation also emit `ImportWorkflowRun` and `ImportWorkflowStep` rows for clean 25/100/500-file import comparisons.')
$content.Add("- Keep this file as a before/after reference, not as a product-facing performance claim.")

Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
Write-Host "Wrote $OutputPath"
