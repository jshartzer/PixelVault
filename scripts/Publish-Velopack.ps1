# Self-contained publish + Velopack vpk pack (PV-PLN-DIST-001 §5.3).
# Requires: dotnet SDK, global tool `vpk` matching NuGet Velopack (see docs/VELOPACK.md).
# Example:
#   pwsh -File C:\Codex\scripts\Publish-Velopack.ps1
#   pwsh -File C:\Codex\scripts\Publish-Velopack.ps1 -SkipVpk -Force
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot,
    [switch]$SkipVpk,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.csproj"
$sourcePath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.cs"

if (-not (Test-Path $projectPath)) { throw "Could not find project: $projectPath" }
if (-not (Test-Path $sourcePath)) { throw "Could not find source file: $sourcePath" }

if ([string]::IsNullOrWhiteSpace($Version))
{
    $versionMatch = Select-String -Path $sourcePath -Pattern 'const string AppVersion = "([^"]+)"' | Select-Object -First 1
    if ($null -eq $versionMatch) { throw "Could not determine AppVersion from $sourcePath" }
    $Version = $versionMatch.Matches[0].Groups[1].Value
}

if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $repoRoot "dist\Velopack"
}

# Velopack requires SemVer2 (three numeric parts). PixelVault uses dotted versions like 0.076.000 — normalize for -v.
function ConvertTo-VpkSemVer([string]$dotted)
{
    $segments = $dotted -split '\.', 4
    if ($segments.Length -lt 3) { throw "Expected at least a 3-part AppVersion for Velopack (got '$dotted')." }
    $major = [int]$segments[0]
    $minor = [int]$segments[1]
    $patch = [int]$segments[2]
    return "$major.$minor.$patch"
}

$vpkVersion = ConvertTo-VpkSemVer $Version

$publishDir = Join-Path $OutputRoot ("publish-" + $Version)
if (Test-Path $publishDir)
{
    if (-not $Force) { throw "Output folder already exists: $publishDir . Use -Force." }
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "Publishing PixelVault $Version (self-contained) to $publishDir"

$publishArgs = @(
    "publish",
    $projectPath,
    "-nodeReuse:false",
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "-p:SelfContained=true",
    "-p:PublishSingleFile=false",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $publishDir
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if ($SkipVpk)
{
    Write-Host "Skipping vpk (--SkipVpk). Published bits: $publishDir"
    exit 0
}

$vpk = Get-Command vpk -ErrorAction SilentlyContinue
if ($null -eq $vpk)
{
    throw "vpk not found on PATH. Install: dotnet tool install -g vpk --version 0.0.942   (match Velopack NuGet in csproj — see docs/VELOPACK.md)"
}

# packId must be stable across releases for updates to apply.
$packId = "Codex.PixelVault"
$releaseDir = Join-Path $OutputRoot $vpkVersion
if (Test-Path $releaseDir)
{
    if (-not $Force) { throw "Release folder already exists: $releaseDir . Use -Force." }
    Remove-Item $releaseDir -Recurse -Force
}

Write-Host "Running vpk pack (SemVer $vpkVersion from AppVersion $Version)..."
# CLI flags: https://docs.velopack.io/reference/cli — short forms avoid typos across vpk versions.
& vpk pack `
    -u $packId `
    -v $vpkVersion `
    -p $publishDir `
    -e PixelVault.exe `
    -o $releaseDir

if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

Write-Host "Velopack release output: $releaseDir"
