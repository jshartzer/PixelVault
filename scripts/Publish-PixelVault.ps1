[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot,
    [int]$KeepLatest = 10,
    [switch]$SelfContained,
    [switch]$IncludeBootstrapSettings,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.csproj"
$sourcePath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.cs"
$nativeProjectDir = Join-Path $repoRoot "src\PixelVault.Native"
$testsProjectDir = Join-Path $repoRoot "tests\PixelVault.Native.Tests"
$changelogPath = Join-Path $repoRoot "docs\CHANGELOG.md"
$assetsPath = Join-Path $repoRoot "assets"
$toolsPath = Join-Path $repoRoot "tools"
$settingsPath = Join-Path $repoRoot "PixelVaultData\PixelVault.settings.ini"
$shortcutPath = Join-Path $repoRoot "PixelVault.lnk"

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

function Copy-SourceTreeForBundle
{
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    if (-not (Test-Path $SourcePath))
    {
        return
    }

    if (Test-Path $DestinationPath)
    {
        Remove-Item $DestinationPath -Recurse -Force
    }

    $destParent = Split-Path $DestinationPath -Parent
    if (-not (Test-Path $destParent))
    {
        New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }

    # Robocopy: success codes 0-7; >= 8 = failure
    & robocopy.exe $SourcePath $DestinationPath /E /NFL /NDL /NJH /NJS /XD bin obj .vs | Out-Null
    $rc = $LASTEXITCODE
    if ($rc -ge 8)
    {
        throw "robocopy failed ($rc) copying $SourcePath -> $DestinationPath"
    }
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

$currentLinkDir = Join-Path $OutputRoot "PixelVault-current"
$outputDir = Join-Path $OutputRoot ("PixelVault-" + $Version)
$exePath = Join-Path $outputDir "PixelVault.exe"

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

# Source bundle: same layout as the repo (no bin/obj), for audits and rebuilds.
# Keep the legacy top-level snapshot too so older release workflows still work.
$bundleNativeDest = Join-Path $outputDir "source\src\PixelVault.Native"
$bundleTestsDest = Join-Path $outputDir "source\tests\PixelVault.Native.Tests"
Copy-SourceTreeForBundle -SourcePath $nativeProjectDir -DestinationPath $bundleNativeDest
Copy-SourceTreeForBundle -SourcePath $testsProjectDir -DestinationPath $bundleTestsDest
Write-Host "Bundled source under: $(Join-Path $outputDir 'source')"

Copy-Item $assetsPath (Join-Path $outputDir "assets") -Recurse -Force
Copy-Item $toolsPath (Join-Path $outputDir "tools") -Recurse -Force

if ($IncludeBootstrapSettings -and (Test-Path $settingsPath))
{
    Copy-Item $settingsPath (Join-Path $outputDir "PixelVault.settings.ini") -Force
}

if (Test-Path $currentLinkDir)
{
    Remove-Item $currentLinkDir -Recurse -Force
}

New-Item -ItemType Junction -Path $currentLinkDir -Target $outputDir | Out-Null

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $outputDir
$shortcut.Save()

if ($KeepLatest -gt 0)
{
    $releaseDirs = Get-ChildItem $OutputRoot -Directory -Filter "PixelVault-*" |
        Where-Object { $_.Name -ne "PixelVault-current" } |
        ForEach-Object {
            $versionText = $_.Name.Substring("PixelVault-".Length)
            $parsedVersion = $null
            if ([version]::TryParse($versionText, [ref]$parsedVersion))
            {
                [PSCustomObject]@{
                    Directory = $_
                    Version = $parsedVersion
                }
            }
        } |
        Where-Object { $_ -ne $null } |
        Sort-Object Version -Descending

    $oldReleases = @($releaseDirs | Select-Object -Skip $KeepLatest)
    foreach ($release in $oldReleases)
    {
        if ($null -eq $release -or $null -eq $release.Directory) { continue }
        Remove-Item $release.Directory.FullName -Recurse -Force
        Write-Host "Removed older release folder: $($release.Directory.FullName)"
    }
}

$expectedChangelogHeader = "## " + $Version
if (-not (Select-String -Path $changelogPath -Pattern ([regex]::Escape($expectedChangelogHeader)) -Quiet))
{
    Write-Warning "CHANGELOG.md does not contain a release header for $Version"
}

Write-Host "Published PixelVault $Version"
Write-Host "Exe: $exePath"
Write-Host "Current: $currentLinkDir"
Write-Host "Shortcut: $shortcutPath"
