# PixelVault

PixelVault is a native Windows desktop app for organizing, tagging, indexing, and browsing game screenshots and clips.

The live app line is based in:

- `C:\Codex`

Current published build:

- `C:\Codex\dist\PixelVault-0.754\PixelVault.exe`

Desktop shortcut:

- `C:\Codex\PixelVault.lnk`

## What PixelVault Does

- imports and organizes new captures from intake folders
- supports review-before-processing with comments and platform tags
- supports manual intake for unmatched files
- writes metadata into files and sidecars through `ExifTool`
- maintains a photo-level metadata index and a master game index
- groups the library by stable `GameId` records
- fetches cover art with `STID`-first SteamGridDB lookup and Steam App ID fallback
- stores `STID` values in the Game Index for SteamGridDB-driven workflows
- supports optional SteamGridDB token and FFmpeg tool paths through Path Settings
- provides in-app editors for the Game Index and Photo Index
- opens into the Library as the main browsing experience

## Current Window Model

### Library

The Library is the main startup view.

It is used for:

- browsing grouped game folders
- searching the library
- running `Import`, `Import and Comment`, and `Manual Import` directly from the Library toolbar
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
- `FFmpeg` path
- SteamGridDB token

### Index editors

PixelVault currently includes:

- Game Index editor for master game records
- Photo Index editor for per-file metadata rows

The Game Index now also acts as the canonical folder-naming authority when records are saved, so library folders can be renamed/moved to follow edited game titles and platform splits.

## Data Layout

Shared persistent app data lives under:

- `C:\Codex\PixelVaultData`

Important files:

- `C:\Codex\PixelVaultData\cache\pixelvault-index-<library>.sqlite`
- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`
- `C:\Codex\docs\CURRENT_BUILD.txt`
- `C:\Codex\docs\CHANGELOG.md`
- `C:\Codex\docs\HANDOFF.md`
- `C:\Codex\docs\POLICY.md`
- `C:\Codex\docs\PROJECT_CONTEXT.md`

## Workspace Map

- `C:\Codex\src\PixelVault.Native`: live app source and SDK project
- `C:\Codex\scripts`: build/publish and developer utility scripts
- `C:\Codex\docs`: handoff, policy, changelog, project context, and current-build marker
- `C:\Codex\dist`: published versioned builds
- `C:\Codex\assets`: shared branding and UI assets
- `C:\Codex\tools`: bundled runtime dependencies such as `ExifTool` and `FFmpeg`
- `C:\Codex\PixelVaultData`: live shared app data, indexes, caches, and logs
- `C:\Codex\legacy`: older GameCaptureManager workflow files kept for history
- `C:\Codex\archive`: backups and old artifacts not needed for day-to-day development

## Source And Packaging

The live published source snapshot for the current build is:

- `C:\Codex\dist\PixelVault-0.754\PixelVault.Native.cs`

The live build source now lives at:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`
- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`

Use the publish helper for new release folders:

- `C:\Codex\scripts\Publish-PixelVault.ps1`

## Running The App

Use the current published executable:

```powershell
C:\Codex\dist\PixelVault-0.754\PixelVault.exe
```

Or launch it from:

- `C:\Codex\PixelVault.lnk`

## Building And Publishing

Build the live source with the SDK project:

```powershell
dotnet build C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj -c Release
```

Publish a new versioned dist folder with the helper script:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Codex\scripts\Publish-PixelVault.ps1 -Version 0.754
```

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

Older PowerShell workflow files are now grouped under `C:\Codex\legacy\GameCaptureManager` so the active native app line is easier to navigate.

## Storage Note

The live Game Index and Photo Index are now backed by a per-library SQLite database in `C:\Codex\PixelVaultData\cache`.

The older tab-delimited `game-index-*.cache` and `library-metadata-index-*.cache` files are now legacy migration inputs and historical snapshots, not the primary runtime store.
