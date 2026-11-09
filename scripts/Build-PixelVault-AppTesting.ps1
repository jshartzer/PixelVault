# Refresh the fixed App Testing folder from the newest published PixelVault release.
# Uses dist\PixelVault-current; never recompiles an older source checkout.
# Keeps this script's name/location, the App Testing path and its shortcut unchanged.
# Existing data-root and settings files are retained. No release or main shortcut is changed.
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath,
    # Retained for existing callers; published binaries already have their build configuration.
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SelfContained,
    [switch]$IncludeBootstrapSettings,
    [switch]$IncludeSourceBundle,
    [switch]$NoStopRunningInstance,
    [switch]$NoClean,
    [string]$ShortcutName = "PixelVault.lnk",
    # Repository containing published dist releases, not a source checkout to compile.
    [string]$RepositoryPath
)

$ErrorActionPreference = "Stop"

function Get-FullDirectoryPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}
function Test-PathWithin([string]$Path, [string]$Parent) {
    return $Path.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Parent + '\', [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoLinkedAncestors([string]$Path) {
    $cursor = $Path
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "The testing output must not pass through a linked directory: $cursor"
            }
        }
        $cursor = Split-Path -Path $cursor -Parent
    }
}
function Resolve-ReleaseDirectory([string]$Path) {
    for ($depth = 0; $depth -lt 16; $depth++) {
        $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
        if (-not $item.PSIsContainer) { throw "Release path is not a directory: $Path" }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
            return Get-FullDirectoryPath $item.FullName
        }
        $target = [string](@($item.Target)[0])
        if ([string]::IsNullOrWhiteSpace($target)) { throw "Cannot resolve release link: $Path" }
        if (-not [IO.Path]::IsPathRooted($target)) { $target = Join-Path $item.Parent.FullName $target }
        $Path = $target
    }
    throw "Too many linked release directories."
}
function Read-PublishedRelease([string]$Path) {
    $exe = Join-Path $Path 'PixelVault.exe'
    $source = Join-Path $Path 'PixelVault.Native.cs'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
        -not (Test-Path -LiteralPath $source -PathType Leaf)) { return $null }
    $match = Select-String -LiteralPath $source -Pattern 'const string AppVersion = "([^"]+)"' | Select-Object -First 1
    if ($null -eq $match) { return $null }
    return [pscustomobject]@{
        Path = $Path
        Exe = $exe
        Version = $match.Matches[0].Groups[1].Value
        PublishedUtc = (Get-Item -LiteralPath $exe).LastWriteTimeUtc
    }
}
function Assert-NoLinkedContents([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $linked = Get-ChildItem -LiteralPath $Path -Recurse -Force |
            Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($null -ne $linked) { throw "Linked content cannot be copied or cleared: $($linked.FullName)" }
    }
}
function Clear-TestingContents {
    # Only the previously validated, explicit output is eligible for recursive cleanup.
    Assert-NoLinkedAncestors $outputDir
    Assert-NoLinkedContents $outputDir
    foreach ($item in @(Get-ChildItem -LiteralPath $outputDir -Force)) {
        $full = [IO.Path]::GetFullPath($item.FullName)
        if (-not (Test-PathWithin $full $outputDir) -or $full -eq $outputDir) {
            throw "Refusing to remove a path outside the testing output: $full"
        }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryPath)) {
    # Published releases live here even when this script is copied elsewhere or run from a worktree.
    $RepositoryPath = 'C:\Codex'
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryPath 'dist') -PathType Container)) {
        $RepositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    }
}
$repoRoot = Get-FullDirectoryPath $RepositoryPath
$distRoot = Join-Path $repoRoot 'dist'
$current = Join-Path $distRoot 'PixelVault-current'
if (-not (Test-Path -LiteralPath $distRoot -PathType Container)) { throw "No published releases found in $distRoot" }

$currentItem = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue
if ($null -ne $currentItem) {
    $releaseDir = Resolve-ReleaseDirectory $current
    if (-not (Test-PathWithin $releaseDir $distRoot) -or $releaseDir -eq $distRoot) {
        throw "The current release must resolve inside $distRoot"
    }
    $release = Read-PublishedRelease $releaseDir
    if ($null -eq $release) { throw "The current published release is incomplete: $releaseDir. Testing output was not changed." }
} else {
    # Recovery for installations without the current junction. Executable build time
    # survives copied directory timestamps and historical version-number renumbering.
    $release = Get-ChildItem -LiteralPath $distRoot -Directory -Filter 'PixelVault-*' |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 } |
        ForEach-Object { Read-PublishedRelease $_.FullName } |
        Sort-Object PublishedUtc -Descending | Select-Object -First 1
    if ($null -eq $release) { throw "No complete published PixelVault release found in $distRoot" }
    $releaseDir = Get-FullDirectoryPath $release.Path
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repoRoot 'App Testing' }
$outputDir = Get-FullDirectoryPath $OutputPath
if ([string]::IsNullOrWhiteSpace($outputDir) -or
    $outputDir.TrimEnd(':') -eq [IO.Path]::GetPathRoot($outputDir).TrimEnd('\', ':') -or
    (Test-PathWithin $repoRoot $outputDir) -or
    (Test-PathWithin $outputDir $distRoot) -or
    (Test-PathWithin $releaseDir $outputDir)) {
    throw "Testing output must be separate from the repository root and published releases: $outputDir"
}
foreach ($protected in @('src','scripts','docs','tests','assets','tools','tools-licenses','PixelVaultData','.git')) {
    if (Test-PathWithin $outputDir (Join-Path $repoRoot $protected)) {
        throw "Testing output must not replace repository files or application data: $outputDir"
    }
}
Assert-NoLinkedAncestors $outputDir
Assert-NoLinkedContents $outputDir
Assert-NoLinkedContents $releaseDir
if (-not [string]::IsNullOrWhiteSpace($ShortcutName) -and
    ([IO.Path]::GetFileName($ShortcutName) -ne $ShortcutName -or [IO.Path]::GetExtension($ShortcutName) -ne '.lnk')) {
    throw "ShortcutName must be a .lnk filename inside the testing folder."
}
foreach ($parameter in @('Configuration','RuntimeIdentifier','SelfContained')) {
    if ($PSBoundParameters.ContainsKey($parameter)) {
        Write-Warning "$parameter does not rebuild or alter the published release."
    }
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) ('PixelVault-AppTesting-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $workRoot 'stage'
$backup = Join-Path $workRoot 'previous'
$outputChanged = $false
$keepBackup = $false
try {
    New-Item -ItemType Directory -Path $stage,$backup -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $releaseDir -Force) {
        if ($item.Name -in @('PixelVault.data-root.ini','PixelVault.settings.ini') -or $item.Extension -eq '.lnk') { continue }
        if ($item.Name -eq 'source' -and -not $IncludeSourceBundle) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $stage -Recurse -Force
    }
    foreach ($name in @('PixelVault.data-root.ini','PixelVault.settings.ini')) {
        $existing = Join-Path $outputDir $name
        $published = Join-Path $releaseDir $name
        if (Test-Path -LiteralPath $existing -PathType Leaf) {
            Copy-Item -LiteralPath $existing -Destination (Join-Path $stage $name)
        } elseif (($name -eq 'PixelVault.data-root.ini' -or $IncludeBootstrapSettings) -and
            (Test-Path -LiteralPath $published -PathType Leaf)) {
            Copy-Item -LiteralPath $published -Destination (Join-Path $stage $name)
        }
    }
    $dataRootFile = Join-Path $stage 'PixelVault.data-root.ini'
    if (-not (Test-Path -LiteralPath $dataRootFile)) {
        Set-Content -LiteralPath $dataRootFile -Value ('DataRoot=' + (Join-Path $repoRoot 'PixelVaultData')) -Encoding UTF8
    }
    $dataLine = Select-String -LiteralPath $dataRootFile -Pattern '^\s*DataRoot\s*=\s*(.+?)\s*$' | Select-Object -First 1
    if ($null -eq $dataLine) { throw "The retained data-root file does not specify DataRoot." }
    $dataPath = $dataLine.Matches[0].Groups[1].Value
    if (-not [IO.Path]::IsPathRooted($dataPath)) { $dataPath = Join-Path $outputDir $dataPath }
    if (Test-PathWithin (Get-FullDirectoryPath $dataPath) $outputDir) {
        throw "Application data is inside the testing output. Choose a separate output folder; existing files were preserved."
    }
    $expectedHash = (Get-FileHash -LiteralPath $release.Exe -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath (Join-Path $stage 'PixelVault.exe') -Algorithm SHA256).Hash -ne $expectedHash) {
        throw "The staged executable does not match the published release."
    }
    $exePath = Join-Path $outputDir 'PixelVault.exe'
    if (-not [string]::IsNullOrWhiteSpace($ShortcutName)) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $stage $ShortcutName))
        $shortcut.TargetPath = $exePath
        $shortcut.WorkingDirectory = $outputDir
        $shortcut.Save()
    }

    Write-Host "App Testing — published PixelVault $($release.Version)"
    Write-Host "Release: $releaseDir"
    Write-Host "Output: $outputDir"
    foreach ($process in @(Get-Process -Name 'PixelVault' -ErrorAction SilentlyContinue)) {
        $runningPath = $process.Path
        if ([string]::IsNullOrWhiteSpace($runningPath) -or -not (Test-PathWithin ([IO.Path]::GetFullPath($runningPath)) $outputDir)) { continue }
        if ($NoStopRunningInstance) { throw "Close PixelVault running from $outputDir before updating it." }
        Write-Host "Stopping PixelVault in the testing folder (PID $($process.Id))."
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(10000) | Out-Null
    }
    if (Test-PathWithin (Get-FullDirectoryPath (Get-Location).Path) $outputDir) { Set-Location -LiteralPath $repoRoot }
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $outputDir -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $backup -Recurse -Force
    }
    $outputChanged = $true
    if (-not $NoClean) { Clear-TestingContents }
    foreach ($item in Get-ChildItem -LiteralPath $stage -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $outputDir -Recurse -Force
    }
    if ((Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash -ne $expectedHash) { throw "Installed executable verification failed." }
    Write-Host "Done. PixelVault $($release.Version): $exePath"
    if (-not [string]::IsNullOrWhiteSpace($ShortcutName)) { Write-Host "Shortcut: $(Join-Path $outputDir $ShortcutName)" }
} catch {
    $failure = $_
    if ($outputChanged) {
        try {
            Clear-TestingContents
            foreach ($item in @(Get-ChildItem -LiteralPath $backup -Force)) {
                Copy-Item -LiteralPath $item.FullName -Destination $outputDir -Recurse -Force
            }
        } catch {
            $keepBackup = $true
            Write-Warning "Recovery files were retained at $backup. Restore them before using the testing output."
        }
    }
    throw $failure
} finally {
    if (-not $keepBackup -and (Test-Path -LiteralPath $workRoot)) {
        $resolvedWork = Get-FullDirectoryPath $workRoot
        $tempRoot = Get-FullDirectoryPath ([IO.Path]::GetTempPath())
        if (-not (Test-PathWithin $resolvedWork $tempRoot) -or
            [IO.Path]::GetFileName($resolvedWork) -notmatch '^PixelVault-AppTesting-[0-9a-f]{32}$') {
            throw "Refusing to remove an unexpected temporary path: $resolvedWork"
        }
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
