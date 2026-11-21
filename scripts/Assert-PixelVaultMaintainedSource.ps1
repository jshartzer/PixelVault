# Local release guard. CURRENT_BUILD.txt records source ownership separately from runtime data.
function Assert-PixelVaultMaintainedSource {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $actual = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    $marker = Join-Path $actual 'docs\CURRENT_BUILD.txt'
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) {
        throw "Cannot verify maintained source: missing $marker. Reconcile the source-location pointer before publishing."
    }
    $matches = @(Select-String -LiteralPath $marker -Pattern '^Maintained source:\s*(.+?)\s*$')
    if ($matches.Count -ne 1) {
        throw "Cannot verify maintained source: $marker must contain one Maintained source: entry."
    }
    $maintained = [IO.Path]::GetFullPath($matches[0].Matches[0].Groups[1].Value).TrimEnd('\', '/')
    if (-not $actual.Equals($maintained, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release/build stopped: $actual is not the maintained source. Run the script from $maintained\scripts. No build or output replacement was started."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $maintained 'src\PixelVault.Native\PixelVault.Native.csproj') -PathType Leaf)) {
        throw "The maintained source project is missing from $maintained. Reconcile the source pointer before publishing."
    }
}
