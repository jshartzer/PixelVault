Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-AppLogger {
    param(
        [scriptblock]$Logger,
        [string]$Message
    )

    if ($Logger) {
        & $Logger $Message
    }
}

function Get-NormalizedExtensions {
    param([string[]]$Extensions)

    return @(
        $Extensions |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            if ($_.StartsWith('.')) { $_.ToLowerInvariant() } else { ".$($_.ToLowerInvariant())" }
        }
    )
}

function Get-UniquePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $Path
    }

    $directory = Split-Path -Parent $Path
    $baseName = [IO.Path]::GetFileNameWithoutExtension($Path)
    $extension = [IO.Path]::GetExtension($Path)
    $index = 2

    do {
        $candidate = Join-Path $directory "$baseName ($index)$extension"
        $index++
    } while (Test-Path -LiteralPath $candidate)

    return $candidate
}

function Sanitize-ForFilename {
    param([string]$Name)

    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    foreach ($ch in $invalid) {
        $Name = $Name.Replace($ch, '-')
    }

    return ($Name -replace '\s+', ' ').Trim()
}

function Get-SteamGameName {
    param(
        [Parameter(Mandatory)]
        [string]$AppId,

        [Parameter(Mandatory)]
        [hashtable]$Cache,

        [scriptblock]$Logger
    )

    if ($Cache.ContainsKey($AppId)) {
        return $Cache[$AppId]
    }

    $url = "https://store.steampowered.com/api/appdetails?appids=$AppId&l=english"
    try {
        $response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 15
        $entry = $response.$AppId
        if ($entry -and $entry.success -and $entry.data -and $entry.data.name) {
            $gameName = Sanitize-ForFilename -Name ([string]$entry.data.name)
            $Cache[$AppId] = $gameName
            return $gameName
        }

        Invoke-AppLogger -Logger $Logger -Message "No Steam name found for AppID $AppId."
    }
    catch {
        Invoke-AppLogger -Logger $Logger -Message "Steam lookup failed for AppID $AppId. $($_.Exception.Message)"
    }

    $Cache[$AppId] = $null
    return $null
}

function Rename-SteamCaptureFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [switch]$Recurse,

        [string[]]$Extensions = @('.png', '.jpg', '.jpeg', '.webp'),

        [string]$AppIdRegex = '(?<!\d)(\d{3,})(?!\d)',

        [switch]$WhatIf,

        [scriptblock]$Logger
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Source folder not found: $Path"
    }

    $normalizedExtensions = Get-NormalizedExtensions -Extensions $Extensions

    $cache = @{}
    $renamed = 0
    $skipped = 0

    $files = Get-ChildItem -LiteralPath $Path -File -Recurse:$Recurse |
        Where-Object { $normalizedExtensions -contains $_.Extension.ToLowerInvariant() }

    foreach ($file in $files) {
        $match = [regex]::Match($file.BaseName, $AppIdRegex)
        if (-not $match.Success) {
            $skipped++
            continue
        }

        $appId = $match.Groups[1].Value
        $gameName = Get-SteamGameName -AppId $appId -Cache $cache -Logger $Logger
        if ([string]::IsNullOrWhiteSpace($gameName)) {
            $skipped++
            continue
        }

        $newBase = [regex]::Replace($file.BaseName, [regex]::Escape($appId), $gameName, 1)
        $targetName = $newBase + $file.Extension
        if ($targetName -eq $file.Name) {
            $skipped++
            continue
        }

        $targetPath = Get-UniquePath -Path (Join-Path $file.DirectoryName $targetName)

        if ($WhatIf) {
            Invoke-AppLogger -Logger $Logger -Message "WhatIf rename: $($file.Name) -> $([IO.Path]::GetFileName($targetPath))"
        }
        else {
            Rename-Item -LiteralPath $file.FullName -NewName ([IO.Path]::GetFileName($targetPath))
            Invoke-AppLogger -Logger $Logger -Message "Renamed: $($file.Name) -> $([IO.Path]::GetFileName($targetPath))"
        }

        $renamed++
    }

    return [pscustomobject]@{
        Step    = 'Rename'
        Renamed = $renamed
        Skipped = $skipped
        Path    = $Path
        WhatIf  = [bool]$WhatIf
    }
}

function Get-CaptureDateFromName {
    param([string]$FileName)

    if ($FileName -match '_(\d{14})_') {
        return [datetime]::ParseExact($matches[1], 'yyyyMMddHHmmss', $null)
    }

    if ($FileName -match '-(\d{4})_(\d{2})_(\d{2})-(\d{2})-(\d{2})-(\d{2})') {
        $raw = "$($matches[1])$($matches[2])$($matches[3])$($matches[4])$($matches[5])$($matches[6])"
        return [datetime]::ParseExact($raw, 'yyyyMMddHHmmss', $null)
    }

    return $null
}

function Update-CaptureMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$TargetFolder,

        [Parameter(Mandatory)]
        [string]$ExifToolPath,

        [string[]]$Extensions = @('.png', '.jpg', '.jpeg'),

        [switch]$IncludeGameCaptureKeywords,

        [switch]$WhatIf,

        [scriptblock]$Logger
    )

    if (-not (Test-Path -LiteralPath $TargetFolder)) {
        throw "Target folder not found: $TargetFolder"
    }

    if (-not (Test-Path -LiteralPath $ExifToolPath)) {
        throw "ExifTool not found: $ExifToolPath"
    }

    $normalizedExtensions = Get-NormalizedExtensions -Extensions $Extensions

    $updated = 0
    $skipped = 0
    $files = Get-ChildItem -LiteralPath $TargetFolder -File |
        Where-Object { $normalizedExtensions -contains $_.Extension.ToLowerInvariant() }

    foreach ($file in $files) {
        $dateTaken = Get-CaptureDateFromName -FileName $file.Name
        if (-not $dateTaken) {
            Invoke-AppLogger -Logger $Logger -Message "Skip metadata: $($file.Name) (filename pattern not recognized)"
            $skipped++
            continue
        }

        $pngTime = $dateTaken.ToString('yyyy:MM:dd HH:mm:ss')
        $standardTime = $dateTaken.ToString('yyyyMMdd HH:mm:ss')
        $arguments = [System.Collections.Generic.List[string]]::new()

        if ($file.Extension.ToLowerInvariant() -eq '.png') {
            $arguments.Add("-PNG:CreationTime=$pngTime")
            $arguments.Add("-PNG:ModifyDate=$pngTime")
            $arguments.Add("-XMP:DateTimeOriginal=$standardTime")
            $arguments.Add("-XMP:CreateDate=$standardTime")
            $arguments.Add("-XMP:ModifyDate=$standardTime")
            $arguments.Add("-XMP:MetadataDate=$standardTime")
            $arguments.Add("-File:FileCreateDate=$standardTime")
            $arguments.Add("-File:FileModifyDate=$standardTime")
        }
        else {
            $arguments.Add("-EXIF:DateTimeOriginal=$standardTime")
            $arguments.Add("-EXIF:CreateDate=$standardTime")
            $arguments.Add("-EXIF:ModifyDate=$standardTime")
            $arguments.Add("-XMP:DateTimeOriginal=$standardTime")
            $arguments.Add("-XMP:CreateDate=$standardTime")
            $arguments.Add("-XMP:ModifyDate=$standardTime")
            $arguments.Add("-XMP:MetadataDate=$standardTime")
            $arguments.Add("-IPTC:DateCreated=$standardTime")
            $arguments.Add("-IPTC:TimeCreated=$standardTime")
            $arguments.Add("-File:FileCreateDate=$standardTime")
            $arguments.Add("-File:FileModifyDate=$standardTime")
        }

        if ($IncludeGameCaptureKeywords) {
            $arguments.Add('-XMP:Subject+=Game Capture')
            $arguments.Add('-IPTC:Keywords+=Game Capture')
            $arguments.Add('-Keywords+=Game Capture')
        }

        $arguments.Add('-overwrite_original')
        $arguments.Add($file.FullName)

        if ($WhatIf) {
            Invoke-AppLogger -Logger $Logger -Message "WhatIf metadata: $($file.Name) -> $($dateTaken.ToString('yyyy-MM-dd HH:mm:ss'))"
        }
        else {
            Invoke-AppLogger -Logger $Logger -Message "Updating metadata: $($file.Name) -> $($dateTaken.ToString('yyyy-MM-dd HH:mm:ss'))"
            $output = & $ExifToolPath @arguments 2>&1
            foreach ($line in $output) {
                if (-not [string]::IsNullOrWhiteSpace([string]$line)) {
                    Invoke-AppLogger -Logger $Logger -Message ([string]$line)
                }
            }
        }

        $updated++
    }

    return [pscustomobject]@{
        Step    = 'Metadata'
        Updated = $updated
        Skipped = $skipped
        Path    = $TargetFolder
        WhatIf  = [bool]$WhatIf
    }
}

function Move-CaptureMediaFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$SourceFolder,

        [Parameter(Mandatory)]
        [string]$DestinationFolder,

        [string[]]$Extensions = @('.jpg', '.jpeg', '.png', '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.webm'),

        [ValidateSet('Rename','Overwrite','Skip')]
        [string]$ConflictMode = 'Rename',

        [switch]$WhatIf,

        [scriptblock]$Logger
    )

    if (-not (Test-Path -LiteralPath $SourceFolder)) {
        throw "Source folder not found: $SourceFolder"
    }

    if (-not (Test-Path -LiteralPath $DestinationFolder)) {
        New-Item -ItemType Directory -Path $DestinationFolder | Out-Null
        Invoke-AppLogger -Logger $Logger -Message "Created destination folder: $DestinationFolder"
    }

    $normalizedExtensions = Get-NormalizedExtensions -Extensions $Extensions

    $moved = 0
    $skipped = 0
    $renamedOnConflict = 0
    $files = Get-ChildItem -LiteralPath $SourceFolder -File |
        Where-Object { $normalizedExtensions -contains $_.Extension.ToLowerInvariant() }

    foreach ($file in $files) {
        $destinationPath = Join-Path $DestinationFolder $file.Name

        $effectiveDestination = $destinationPath
        $destinationExists = Test-Path -LiteralPath $destinationPath

        if ($destinationExists) {
            switch ($ConflictMode) {
                'Skip' {
                    Invoke-AppLogger -Logger $Logger -Message "Skipped move due to conflict: $($file.Name)"
                    $skipped++
                    continue
                }
                'Rename' {
                    $effectiveDestination = Get-UniquePath -Path $destinationPath
                    $renamedOnConflict++
                }
                'Overwrite' {
                }
            }
        }

        if ($WhatIf) {
            Invoke-AppLogger -Logger $Logger -Message "WhatIf move: $($file.FullName) -> $effectiveDestination"
            $moved++
            continue
        }

        $force = $ConflictMode -eq 'Overwrite'
        Move-Item -LiteralPath $file.FullName -Destination $effectiveDestination -Force:$force
        Invoke-AppLogger -Logger $Logger -Message "Moved: $($file.Name) -> $effectiveDestination"
        $moved++
    }

    return [pscustomobject]@{
        Step              = 'Move'
        Moved             = $moved
        Skipped           = $skipped
        RenamedOnConflict = $renamedOnConflict
        Source            = $SourceFolder
        Destination       = $DestinationFolder
        WhatIf            = [bool]$WhatIf
    }
}

function Get-WorkflowPreview {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$SourceFolder,

        [Parameter(Mandatory)]
        [string]$DestinationFolder,

        [string[]]$RenameExtensions = @('.png', '.jpg', '.jpeg', '.webp'),

        [string[]]$MetadataExtensions = @('.png', '.jpg', '.jpeg'),

        [string[]]$MoveExtensions = @('.jpg', '.jpeg', '.png', '.mp4', '.mkv', '.avi', '.mov', '.wmv', '.webm'),

        [string]$AppIdRegex = '(?<!\d)(\d{3,})(?!\d)',

        [switch]$RecurseRename
    )

    if (-not (Test-Path -LiteralPath $SourceFolder)) {
        throw "Source folder not found: $SourceFolder"
    }

    $renameExt = Get-NormalizedExtensions -Extensions $RenameExtensions
    $metadataExt = Get-NormalizedExtensions -Extensions $MetadataExtensions
    $moveExt = Get-NormalizedExtensions -Extensions $MoveExtensions

    $renameFiles = Get-ChildItem -LiteralPath $SourceFolder -File -Recurse:$RecurseRename |
        Where-Object { $renameExt -contains $_.Extension.ToLowerInvariant() }
    $renameCandidates = @($renameFiles | Where-Object { [regex]::Match($_.BaseName, $AppIdRegex).Success })

    $metadataFiles = Get-ChildItem -LiteralPath $SourceFolder -File |
        Where-Object { $metadataExt -contains $_.Extension.ToLowerInvariant() }
    $metadataCandidates = @($metadataFiles | Where-Object { $null -ne (Get-CaptureDateFromName -FileName $_.Name) })

    $moveFiles = Get-ChildItem -LiteralPath $SourceFolder -File |
        Where-Object { $moveExt -contains $_.Extension.ToLowerInvariant() }

    $moveConflicts = 0
    if (Test-Path -LiteralPath $DestinationFolder) {
        foreach ($file in $moveFiles) {
            if (Test-Path -LiteralPath (Join-Path $DestinationFolder $file.Name)) {
                $moveConflicts++
            }
        }
    }

    return [pscustomobject]@{
        RenameScanned          = @($renameFiles).Count
        RenameCandidates       = @($renameCandidates).Count
        MetadataScanned        = @($metadataFiles).Count
        MetadataCandidates     = @($metadataCandidates).Count
        MoveCandidates         = @($moveFiles).Count
        MoveConflicts          = $moveConflicts
        SampleRenameCandidates = @($renameCandidates | Select-Object -First 5 -ExpandProperty Name)
        SampleMetadataTargets  = @($metadataCandidates | Select-Object -First 5 -ExpandProperty Name)
        SampleMoveCandidates   = @($moveFiles | Select-Object -First 5 -ExpandProperty Name)
    }
}

Export-ModuleMember -Function Rename-SteamCaptureFiles, Update-CaptureMetadata, Move-CaptureMediaFiles, Get-WorkflowPreview
