# Release ownership is local configuration, not public release metadata.
function Assert-PixelVaultMaintainedSource {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $actual = [IO.Path]::GetFullPath($RepositoryRoot)
    $localConfig = Join-Path $actual '.pixelvault-release-root'
    $configured = $env:PIXELVAULT_MAINTAINED_SOURCE
    if ([string]::IsNullOrWhiteSpace($configured) -and (Test-Path -LiteralPath $localConfig -PathType Leaf)) {
        $configured = Get-Content -LiteralPath $localConfig -Raw
    }
    if ([string]::IsNullOrWhiteSpace($configured)) {
        throw 'Release source is not configured. Set PIXELVAULT_MAINTAINED_SOURCE or create .pixelvault-release-root in the maintained checkout. See docs/LOCAL_RELEASE_CONFIGURATION.md.'
    }
    $configured = $configured.Trim()
    if ($configured -match '[\r\n]' -or -not [IO.Path]::IsPathFullyQualified($configured)) {
        throw 'Release source configuration must be one absolute filesystem path.'
    }
    $maintained = [IO.Path]::GetFullPath($configured).TrimEnd('\', '/')
    $actual = $actual.TrimEnd('\', '/')
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $actual.Equals($maintained, $comparison)) {
        throw 'Release/build stopped: this checkout is not the configured maintained source. No build or output replacement was started.'
    }
    $nativeProject = Join-Path $actual 'src/PixelVault.Native/PixelVault.Native.csproj'
    if (-not (Test-Path -LiteralPath $nativeProject -PathType Leaf)) {
        throw 'The maintained source project is missing. Check the local release configuration.'
    }
}
