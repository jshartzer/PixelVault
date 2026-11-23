# Exercises the release guard without touching a real checkout or building a release.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/Assert-PixelVaultMaintainedSource.ps1')

function Assert-Rejected([string]$Root, [string]$Expected) {
    try {
        Assert-PixelVaultMaintainedSource -RepositoryRoot $Root
    }
    catch {
        if ($_.Exception.Message -like "*$Expected*") { return }
        throw
    }
    throw "Release guard unexpectedly accepted a checkout ($Expected)."
}

$previous = $env:PIXELVAULT_MAINTAINED_SOURCE
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$fixture = Join-Path $tempBase ('PixelVault-release-test-' + [Guid]::NewGuid().ToString('N'))
try {
    $env:PIXELVAULT_MAINTAINED_SOURCE = $null
    $source = Join-Path $fixture 'source with spaces'
    $runtime = Join-Path $fixture 'older source'
    $projectDir = Join-Path $source 'src/PixelVault.Native'
    New-Item -ItemType Directory -Path $projectDir, $runtime | Out-Null
    $project = Join-Path $projectDir 'PixelVault.Native.csproj'
    Set-Content -LiteralPath $project -Value '<Project />'
    $marker = Join-Path $source '.pixelvault-release-root'

    Assert-Rejected $source 'not configured'
    Set-Content -LiteralPath $marker -Value 'not/an/absolute/path'
    Assert-Rejected $source 'absolute filesystem path'
    Set-Content -LiteralPath $marker -Value @($source, $runtime)
    Assert-Rejected $source 'absolute filesystem path'
    Set-Content -LiteralPath $marker -Value $source
    Assert-PixelVaultMaintainedSource -RepositoryRoot $source
    Assert-Rejected $runtime 'not configured'

    $env:PIXELVAULT_MAINTAINED_SOURCE = $source
    Assert-PixelVaultMaintainedSource -RepositoryRoot $source
    Assert-Rejected $runtime 'not the configured maintained source'
    $env:PIXELVAULT_MAINTAINED_SOURCE = $runtime
    Assert-Rejected $runtime 'project is missing'
    Assert-Rejected $source 'not the configured maintained source'
    'Maintained source configuration tests passed.'
}
finally {
    $env:PIXELVAULT_MAINTAINED_SOURCE = $previous
    if ((Split-Path -Parent $fixture) -eq $tempBase -and
        (Split-Path -Leaf $fixture) -match '^PixelVault-release-test-[a-f0-9]{32}$' -and
        (Test-Path -LiteralPath $fixture -PathType Container)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
