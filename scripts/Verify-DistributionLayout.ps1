# Validates a PixelVault publish folder before VM testing or hosting (PV-PLN-DIST-001).
# Example:
#   pwsh -File C:\Codex\scripts\Verify-DistributionLayout.ps1 -Path C:\Codex\dist\Velopack\publish-0.076.000
#   pwsh -File C:\Codex\scripts\Verify-DistributionLayout.ps1 -Path C:\Codex\dist\PixelVault-0.076.000
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Path,
    [switch]$StrictToolsFolder
)

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($Path.TrimEnd('\', '/'))
if (-not (Test-Path -LiteralPath $root))
{
    throw "Path does not exist: $root"
}

$exe = Join-Path $root "PixelVault.exe"
if (-not (Test-Path -LiteralPath $exe))
{
    throw "Missing PixelVault.exe under: $root"
}

$licDir = Join-Path $root "tools\licenses"
$readme = Join-Path $licDir "README.txt"
$exifCopy = Join-Path $licDir "exiftool-gpl-3.0-COPYING.txt"

if (-not (Test-Path -LiteralPath $readme))
{
    throw "Missing tools\licenses\README.txt — run publish scripts so Merge-BundledToolLicenses ran (see docs/VELOPACK.md)."
}

if (-not (Test-Path -LiteralPath $exifCopy))
{
    throw "Missing tools\licenses\exiftool-gpl-3.0-COPYING.txt — check tools-licenses\ in repo."
}

Write-Host "OK: $root"
Write-Host "  PixelVault.exe present"
Write-Host "  tools\licenses\ (ExifTool GPLv3 + README) present"

$toolsExe = Join-Path $root "tools\exiftool.exe"
if (Test-Path -LiteralPath $toolsExe)
{
    Write-Host "  tools\exiftool.exe present (optional bundle)"
}
elseif ($StrictToolsFolder)
{
    throw "StrictToolsFolder: expected tools\exiftool.exe under $root"
}
else
{
    Write-Host "  tools\exiftool.exe not present (optional — OK for CI without local tools\)"
}

exit 0
