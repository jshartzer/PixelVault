# PixelVault

PixelVault is a native Windows desktop app for organizing, tagging, indexing, and browsing game screenshots and clips.

The live app line is based in:

- `C:\Codex`

Current published build:

- `C:\Codex\dist\PixelVault-0.728\PixelVault.exe`

Desktop shortcut:

- `C:\Codex\PixelVault.lnk`

## What PixelVault Does

- imports and organizes new captures from intake folders
- supports review-before-processing with comments and platform tags
- supports manual intake for unmatched files
- writes metadata into files and sidecars through `ExifTool`
- maintains a photo-level metadata index and a master game index
- groups the library by stable `GameId` records
- fetches Steam cover art using saved Steam App IDs
- provides in-app editors for the Game Index and Photo Index
- opens into the Library as the main browsing experience

## Current Window Model

### Library

The Library is the main startup view.

It is used for:

- browsing grouped game folders
- searching the library
- resizing folder tiles and preview tiles
- opening folder detail previews
- running `Refresh`, `Rebuild`, and `Fetch Covers`
- right-click actions such as single-folder cover fetch

### Settings

Settings is the utility and import hub.

It is used for:

- previewing intake items
- running `Process`
- running `Process with Comments`
- launching `Manual Intake`
- opening index editors and utility views

### Path Settings

Path Settings is used only for environment configuration:

- source folders
- destination folder
- library folder
- `ExifTool` path

### Index editors

PixelVault currently includes:

- Game Index editor for master game records
- Photo Index editor for per-file metadata rows

## Data Layout

Shared persistent app data lives under:

- `C:\Codex\PixelVaultData`

Important files:

- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`
- `C:\Codex\CURRENT_BUILD.txt`
- `C:\Codex\CHANGELOG.md`
- `C:\Codex\HANDOFF.md`
- `C:\Codex\POLICY.md`
- `C:\Codex\PROJECT_CONTEXT.md`

## Source And Packaging

The live published source snapshot for the current build is:

- `C:\Codex\dist\PixelVault-0.728\PixelVault.Native.cs`

There is also an older `native\PixelVault.Native.cs` workspace copy in the repo for reference/history, but the current shipped line should be treated carefully and documented through the live `C:\Codex` workflow.

## Running The App

Use the current published executable:

```powershell
C:\Codex\dist\PixelVault-0.728\PixelVault.exe
```

Or launch it from:

- `C:\Codex\PixelVault.lnk`

## Project Documents

Use these together:

- `POLICY.md`: working rules, versioning, form responsibilities, and git policy
- `HANDOFF.md`: short current stop point for the next conversation
- `PROJECT_CONTEXT.md`: broader project architecture and current-state overview
- `CHANGELOG.md`: published version history

## Git Scope

This repo is intended to track:

- app source and launcher code
- project documentation
- curated snapshots of the game index and photo index

It should not track:

- the actual screenshot/video library on `Y:\`
- runtime logs
- cover cache
- thumbnail cache
- backup files
- generated EXEs

## Legacy Note

Older PowerShell workflow files are still present in the repo because they are part of the project history, but the active product is the native Windows app line documented above.
