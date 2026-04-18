# Build a distributable PixelVault folder, verify it, and explain next steps in plain English.
# See docs/SIMPLE_RELEASE_STEPS.md
#
# Examples:
#   pwsh -File C:\Codex\scripts\Run-LocalReleaseChecks.ps1
#   pwsh -File C:\Codex\scripts\Run-LocalReleaseChecks.ps1 -MakeInstaller
#   pwsh -File C:\Codex\scripts\Run-LocalReleaseChecks.ps1 -OnlyVerify "C:\Codex\dist\Velopack\publish-0.076.000"
[CmdletBinding()]
param(
    [string]$OnlyVerify,
    [switch]$MakeInstaller,
    [switch]$Force,
    [string]$Version,
    [string]$SignParams
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourcePath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.cs"
$verifyScript = Join-Path $PSScriptRoot "Verify-DistributionLayout.ps1"
$publishScript = Join-Path $PSScriptRoot "Publish-Velopack.ps1"

function Get-AppVersionFromSource
{
    $m = Select-String -Path $sourcePath -Pattern 'const string AppVersion = "([^"]+)"' | Select-Object -First 1
    if ($null -eq $m) { throw "Could not read AppVersion from $sourcePath" }
    return $m.Matches[0].Groups[1].Value
}

function Write-PlainSummary
{
    param([string]$PublishFolder)
    Write-Host ""
    Write-Host "========== WHAT HAPPENED (plain English) ==========" -ForegroundColor Cyan
    Write-Host "PixelVault was built into this folder:"
    Write-Host "  $PublishFolder"
    Write-Host ""
    Write-Host "We checked that the main program and license files are present."
    Write-Host ""
    Write-Host "========== WHAT YOU DO NEXT ==========" -ForegroundColor Cyan
    Write-Host "1) On THIS PC: open File Explorer to the folder above and double-click PixelVault.exe."
    Write-Host "   Click around (Library, Settings) and make sure nothing obvious is broken."
    Write-Host ""
    Write-Host "2) For a stricter test: copy that whole folder to another Windows PC (or a clean VM)"
    Write-Host "   and run PixelVault.exe there — that mimics a new user without your dev setup."
    Write-Host "   Step-by-step: docs\VELOPACK_VM_SPIKE_CHECKLIST.md"
    Write-Host ""
    Write-Host "3) Deeper product test: docs\MANUAL_GOLDEN_PATH_CHECKLIST.md"
    Write-Host ""
    Write-Host "4) Legal / privacy pages on a website — do LAST when the app feels ready."
    Write-Host "   See: docs\SIMPLE_RELEASE_STEPS.md and docs\plans\PV-PLN-DIST-001-windows-store-and-distribution-roadmap.md (section 10.1)"
    Write-Host ""
    Write-Host "5) Optional: code signing reduces 'Unknown publisher' warnings — docs\PUBLISH_SIGNING.md"
    Write-Host "=================================================="
    Write-Host ""
}

if (-not [string]::IsNullOrWhiteSpace($OnlyVerify))
{
    $p = [System.IO.Path]::GetFullPath($OnlyVerify)
    Write-Host "Skipping build. Only verifying: $p" -ForegroundColor Yellow
    & $verifyScript -Path $p
    Write-PlainSummary -PublishFolder $p
    exit 0
}

$ver = $Version
if ([string]::IsNullOrWhiteSpace($ver)) { $ver = Get-AppVersionFromSource }

Write-Host ""
Write-Host "Building PixelVault (installer channel: Velopack-style publish folder)..." -ForegroundColor Cyan
Write-Host "App version: $ver"
if (-not $MakeInstaller)
{
    Write-Host "Mode: folder only (no Setup.exe yet) — use -MakeInstaller when vpk + ASP.NET 8 are installed."
}
Write-Host ""

$publishArgs = @{ Force = $Force }
if (-not $MakeInstaller) { $publishArgs.SkipVpk = $true }
if (-not [string]::IsNullOrWhiteSpace($Version)) { $publishArgs.Version = $Version }
if ($MakeInstaller -and -not [string]::IsNullOrWhiteSpace($SignParams)) { $publishArgs.SignParams = $SignParams }

& $publishScript @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishDir = Join-Path $repoRoot ("dist\Velopack\publish-" + $ver)
if (-not (Test-Path -LiteralPath $publishDir))
{
    throw "Expected publish folder not found: $publishDir"
}

Write-Host ""
Write-Host "Running automatic folder check..." -ForegroundColor Cyan
& $verifyScript -Path $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-PlainSummary -PublishFolder $publishDir
exit 0
