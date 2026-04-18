# Reusable local build into a fixed folder (not a release).
# Builds from repo source only: dotnet publish on src\PixelVault.Native, then copies repo assets/ + tools/ + tools-licenses -> tools/licenses.
# Does not read or copy from dist\ — ever.
# Does not bump AppVersion, CHANGELOG, CURRENT_BUILD, dist/*, PixelVault-current, or repo-root PixelVault.lnk.
#
# Default output: <repo>\App Testing\  (overwrite each run so Taskbar pins stay valid)
#
# Cleanup deletes *contents* of the output folder but not the folder node itself, so an "empty" folder
# with a directory lock (Explorer, cwd, indexer) usually still works. This script moves cwd to repo root
# and can stop PixelVault when it was launched from this path. Use -NoClean to skip clearing entirely.
[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SelfContained,
    [switch]$IncludeBootstrapSettings,
    [switch]$IncludeSourceBundle,
    # Do not attempt Stop-Process on PixelVault.exe when its path is under the output folder
    [switch]$NoStopRunningInstance,
    # Skip deleting the output folder first (not recommended; publish may leave stale files)
    [switch]$NoClean,
    # Shortcut inside the output folder (default: PixelVault.lnk). Set to "" to skip.
    [string]$ShortcutName = "PixelVault.lnk"
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "Merge-BundledToolLicenses.ps1")

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.csproj"
$sourcePath = Join-Path $repoRoot "src\PixelVault.Native\PixelVault.Native.cs"
$nativeProjectDir = Join-Path $repoRoot "src\PixelVault.Native"
$testsProjectDir = Join-Path $repoRoot "tests\PixelVault.Native.Tests"
$changelogPath = Join-Path $repoRoot "docs\CHANGELOG.md"
$assetsPath = Join-Path $repoRoot "assets"
$toolsPath = Join-Path $repoRoot "tools"
$settingsPath = Join-Path $repoRoot "PixelVaultData\PixelVault.settings.ini"

if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $repoRoot "App Testing"
}

$outputDir = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirNormalized = $outputDir.TrimEnd('\', '/')
if ([string]::IsNullOrEmpty($outputDirNormalized)) { $outputDirNormalized = $outputDir }

function Stop-PixelVaultProcessesUnderPath
{
    param([Parameter(Mandatory)][string]$FolderPath)
    $prefix = $FolderPath.TrimEnd('\', '/')
    if ([string]::IsNullOrEmpty($prefix)) { return }

    $stopped = $false
    Get-Process -Name "PixelVault" -ErrorAction SilentlyContinue | ForEach-Object {
        $proc = $_
        try
        {
            $exe = $proc.Path
            if ([string]::IsNullOrWhiteSpace($exe)) { return }
            $exeFull = [System.IO.Path]::GetFullPath($exe)
            if ($exeFull.StartsWith($prefix + "\", [System.StringComparison]::OrdinalIgnoreCase) -or
                $exeFull.Equals($prefix, [System.StringComparison]::OrdinalIgnoreCase))
            {
                Write-Host "Stopping PixelVault locking output folder: $exeFull (PID $($proc.Id))"
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                $stopped = $true
            }
        }
        catch
        {
            Write-Warning "Could not stop PID $($proc.Id): $($_.Exception.Message)"
        }
    }
    if ($stopped) { Start-Sleep -Milliseconds 500 }
}

function Clear-OutputDirectoryContents
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$PerItemAttempts = 6,
        [int]$DelayMilliseconds = 400
    )
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue)
    foreach ($item in $children)
    {
        $full = $item.FullName
        for ($a = 1; $a -le $PerItemAttempts; $a++)
        {
            try
            {
                Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
                break
            }
            catch
            {
                if ($a -ge $PerItemAttempts)
                {
                    throw "Could not remove '$full' after $PerItemAttempts attempts: $($_.Exception.Message). Close PixelVault if using this folder, close Explorer here, or run with -NoClean."
                }
                Start-Sleep -Milliseconds $DelayMilliseconds
            }
        }
    }
}

function Clear-ShellLocationIfUnderOutputFolder
{
    param(
        [Parameter(Mandatory)][string]$OutputFolder,
        [Parameter(Mandatory)][string]$FallbackLocation
    )
    $prefix = $OutputFolder.TrimEnd('\', '/')
    if ([string]::IsNullOrEmpty($prefix) -or -not (Test-Path -LiteralPath $FallbackLocation)) { return }
    try
    {
        $cwd = [System.IO.Path]::GetFullPath((Get-Location -PSProvider FileSystem).Path).TrimEnd('\', '/')
    }
    catch
    {
        return
    }
    if ($cwd.Equals($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $cwd.StartsWith($prefix + '\', [System.StringComparison]::OrdinalIgnoreCase))
    {
        Write-Host "Current directory is the output folder (or inside it); switching to: $FallbackLocation"
        Set-Location -LiteralPath $FallbackLocation
    }
}

if (-not (Test-Path $projectPath)) { throw "Could not find project: $projectPath" }
if (-not (Test-Path $sourcePath)) { throw "Could not find source file: $sourcePath" }

$versionMatch = Select-String -Path $sourcePath -Pattern 'const string AppVersion = "([^"]+)"' | Select-Object -First 1
if ($null -eq $versionMatch) { throw "Could not read AppVersion from $sourcePath" }
$appVersion = $versionMatch.Matches[0].Groups[1].Value

function Copy-SourceTreeForBundle
{
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    if (-not (Test-Path $SourcePath)) { return }

    if (Test-Path $DestinationPath)
    {
        Remove-Item $DestinationPath -Recurse -Force
    }

    $destParent = Split-Path $DestinationPath -Parent
    if (-not (Test-Path $destParent))
    {
        New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }

    & robocopy.exe $SourcePath $DestinationPath /E /NFL /NDL /NJH /NJS /XD bin obj .vs | Out-Null
    $rc = $LASTEXITCODE
    if ($rc -ge 8) { throw "robocopy failed ($rc) copying $SourcePath -> $DestinationPath" }
}

if (-not $NoClean -and (Test-Path -LiteralPath $outputDir))
{
    Clear-ShellLocationIfUnderOutputFolder -OutputFolder $outputDirNormalized -FallbackLocation $repoRoot
    if (-not $NoStopRunningInstance)
    {
        Stop-PixelVaultProcessesUnderPath -FolderPath $outputDirNormalized
    }
    Clear-OutputDirectoryContents -Path $outputDir
}

if (-not (Test-Path $outputDir))
{
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
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

Write-Host "App testing build — AppVersion from source: $appVersion (not bumped)"
Write-Host "Publishing to: $outputDir"

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Copy-Item $sourcePath (Join-Path $outputDir "PixelVault.Native.cs") -Force
Copy-Item $changelogPath (Join-Path $outputDir "CHANGELOG.md") -Force

if ($IncludeSourceBundle)
{
    $bundleNativeDest = Join-Path $outputDir "source\src\PixelVault.Native"
    $bundleTestsDest = Join-Path $outputDir "source\tests\PixelVault.Native.Tests"
    Copy-SourceTreeForBundle -SourcePath $nativeProjectDir -DestinationPath $bundleNativeDest
    Copy-SourceTreeForBundle -SourcePath $testsProjectDir -DestinationPath $bundleTestsDest
    Write-Host "Bundled source under: $(Join-Path $outputDir 'source')"
}

Copy-Item $assetsPath (Join-Path $outputDir "assets") -Recurse -Force
if (Test-Path $toolsPath)
{
    Copy-Item $toolsPath (Join-Path $outputDir "tools") -Recurse -Force
}
else
{
    Write-Warning "Repo tools\ missing — exiftool.exe / ffmpeg.exe not copied (licenses still merged)."
}

Merge-PixelVaultBundledToolLicenses -RepoRoot $repoRoot -DestinationRoot $outputDir

if ($IncludeBootstrapSettings -and (Test-Path $settingsPath))
{
    Copy-Item $settingsPath (Join-Path $outputDir "PixelVault.settings.ini") -Force
}

$exePath = Join-Path $outputDir "PixelVault.exe"
if (-not [string]::IsNullOrWhiteSpace($ShortcutName))
{
    $shortcutPath = Join-Path $outputDir $ShortcutName
    Remove-Item $shortcutPath -Force -ErrorAction SilentlyContinue
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($shortcutPath)
    $sc.TargetPath = $exePath
    $sc.WorkingDirectory = $outputDir
    $sc.Save()
    Write-Host "Shortcut: $shortcutPath"
}

Write-Host "Done. Exe: $exePath"
