# Copies repo tools-licenses/* into <destination>\tools\licenses for §5.9 compliance.
# Dot-source from Publish-PixelVault.ps1 / Publish-Velopack.ps1:
#   . (Join-Path $PSScriptRoot "Merge-BundledToolLicenses.ps1")

function Merge-PixelVaultBundledToolLicenses {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$DestinationRoot
    )

    $src = Join-Path $RepoRoot "tools-licenses"
    if (-not (Test-Path $src))
    {
        Write-Warning "Bundled tool licenses folder missing (expected $src)."
        return
    }

    $toolsDest = Join-Path $DestinationRoot "tools"
    if (-not (Test-Path $toolsDest))
    {
        New-Item -ItemType Directory -Path $toolsDest -Force | Out-Null
    }

    $licDest = Join-Path $toolsDest "licenses"
    if (Test-Path $licDest)
    {
        Remove-Item $licDest -Recurse -Force
    }

    New-Item -ItemType Directory -Path $licDest -Force | Out-Null
    Copy-Item -Path (Join-Path $src "*") -Destination $licDest -Recurse -Force
    Write-Host "Bundled tool licenses -> $licDest"
}
