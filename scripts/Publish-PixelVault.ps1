[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot,
    [switch]$SelfContained,
    [switch]$IncludeBootstrapSettings,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.csproj"
$sourcePath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.cs"
$changelogPath = Join-Path $repoRoot "docs\CHANGELOG.md"
$assetsPath = Join-Path $repoRoot "assets"
$toolsPath = Join-Path $repoRoot "tools"
$settingsPath = Join-Path $repoRoot "PixelVaultData\PixelVault.settings.ini"

if ([string]::IsNullOrWhiteSpace($OutputRoot))
{
    $OutputRoot = Join-Path $repoRoot "dist"
}

if (-not (Test-Path $projectPath))
{
    throw "Could not find project: $projectPath"
}

if (-not (Test-Path $sourcePath))
{
    throw "Could not find source file: $sourcePath"
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    $versionMatch = Select-String -Path $sourcePath -Pattern 'const string AppVersion = "([^"]+)"' | Select-Object -First 1
    if ($null -eq $versionMatch)
    {
        throw "Could not determine AppVersion from $sourcePath"
    }

    $Version = $versionMatch.Matches[0].Groups[1].Value
}

$outputDir = Join-Path $OutputRoot ("PixelVault-" + $Version)

if (Test-Path $outputDir)
{
    if (-not $Force)
    {
        throw "Output folder already exists: $outputDir . Use -Force to overwrite it."
    }

    Remove-Item $outputDir -Recurse -Force
}

$publishArgs = @(
    "publish",
    $projectPath,
    "-nodeReuse:false",
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "-p:PublishSingleFile=true",
    "-p:UseSharedCompilation=false",
    ("-p:SelfContained=" + $(if ($SelfContained) { "true" } else { "false" })),
    "-o", $outputDir
)

Write-Host "Publishing PixelVault $Version to $outputDir"
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item $sourcePath (Join-Path $outputDir "PixelVault.Native.cs") -Force
Copy-Item $changelogPath (Join-Path $outputDir "CHANGELOG.md") -Force
Copy-Item $assetsPath (Join-Path $outputDir "assets") -Recurse -Force
Copy-Item $toolsPath (Join-Path $outputDir "tools") -Recurse -Force

if ($IncludeBootstrapSettings -and (Test-Path $settingsPath))
{
    Copy-Item $settingsPath (Join-Path $outputDir "PixelVault.settings.ini") -Force
}

$expectedChangelogHeader = "## " + $Version
if (-not (Select-String -Path $changelogPath -Pattern ([regex]::Escape($expectedChangelogHeader)) -Quiet))
{
    Write-Warning "CHANGELOG.md does not contain a release header for $Version"
}

Write-Host "Published PixelVault $Version"
Write-Host "Exe: $(Join-Path $outputDir 'PixelVault.exe')"
