# PixelVault Project Context

## Overview

`PixelVault` is a native Windows desktop app built in C# and WPF for organizing, tagging, previewing, and browsing a game screenshot and clip library.

The current live app line is based in:

- `C:\Codex`

The app now runs from packaged builds under `C:\Codex\dist\PixelVault-x.xxx`, with shared persistent data stored outside the versioned build folders in `C:\Codex\PixelVaultData`.

## Current Published State

Current published build:

- `0.752`

Current executable:

- `C:\Codex\dist\PixelVault-0.752\PixelVault.exe`

Desktop shortcut:

- `C:\Codex\PixelVault.lnk`

Current build pointer:

- `C:\Codex\docs\CURRENT_BUILD.txt`

## Core Goals

PixelVault is intended to:

- normalize and enrich incoming captures from multiple platforms
- preserve metadata so external-library tools such as Immich can ingest tags and comments
- browse an existing NAS-hosted capture library in a polished desktop UI
- support manual cleanup and metadata correction without losing batch efficiency

## Important Paths

Workspace root:

- `C:\Codex`

Current live source file:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`

Current native build project:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`

Current publish helper:

- `C:\Codex\scripts\Publish-PixelVault.ps1`

Shared data root:

- `C:\Codex\PixelVaultData`

Shared cache folder:

- `C:\Codex\PixelVaultData\cache`

Shared logs folder:

- `C:\Codex\PixelVaultData\logs`

Live cache files:

- `C:\Codex\PixelVaultData\cache\pixelvault-index-y_game_captures.sqlite`
- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-folders-y_game_captures.cache`

Current library/intake defaults:

- source: `Y:\Game Capture Uploads`
- destination: `Y:\Game Captures`
- library: `Y:\Game Captures`

Tool dependency:

- `C:\Codex\tools\exiftool.exe`
- `C:\Codex\tools\ffmpeg.exe` when video poster generation is desired

## Current Architecture

The app is still implemented primarily in one native source file.

That file now builds directly through the SDK-style project under `C:\Codex\src\PixelVault.Native`, and each published dist folder keeps a version-local `PixelVault.Native.cs` snapshot for traceability.

Major subsystems inside the current line include:

- startup Library window
- Settings utility window
- Path Settings window
- intake preview and processing
- review-with-comments flow
- manual intake / metadata editing
- library browser and folder detail preview
- photography browser
- metadata writing through `ExifTool`
- video sidecar metadata support
- photo-level metadata index
- game master index
- derived folder cache
- cover-art cache
- thumbnail cache
- undo-last-import support

## Current Window Model

### Library

The Library is the main startup form.

It handles:

- grouped folder browsing
- search
- direct `Import`, `Import and Comment`, and `Manual Import` actions from the toolbar
- folder tile sizing
- folder detail preview
- `Refresh`
- `Rebuild`
- `Fetch Covers`

### Settings

The old home screen has been converted into a utility-oriented Settings window.

It handles:

- preview intake
- process
- process with comments
- manual intake
- utility shortcuts

### Index editors

There are now two distinct index editors:

- Game Index editor for master records
- Photo Index editor for per-file metadata rows

## Metadata Strategy

### Files

Per-file metadata is the source of truth for:

- tags
- comments / descriptions
- platform identity
- capture-date overrides written to the file

### Photo index

The photo index is the persistent per-file mirror/cache.

It is now stored in the per-library SQLite index database and surfaced in the UI through the Photo Index editor.

It stores:

- file path
- stamp
- `GameId`
- console label
- tag text

### Game index

The game index is the master record table.

It is now stored in the per-library SQLite index database and remains the authority for `GameId`, canonical title, platform, Steam App ID, and `STID`.

It stores:

- stable `GameId`
- canonical title
- console/platform
- Steam App ID
- `STID` for SteamGridDB
- file count
- folder-path context

Grouping is intended to follow `GameId`, not raw title text.

As of `0.742`, Game Index save is also responsible for normalizing library folder names on disk. When multiple records share the same normalized title across platforms, canonical folder naming now appends ` - Platform`.

### Folder cache

The folder cache is derived state rebuilt from the indexes and should not be treated as the canonical source of tag truth.

## Platform / Tag Model

Current recognized platform families are:

- `Steam`
- `PC`
- `PS5` / `PlayStation`
- `Xbox`
- `Platform:<Custom>`

Rules:

- `Steam` and `PC` are separate tags
- multiple recognized families produce `Multiple Tags`
- `Other` should only be used when no recognized platform family exists

## Supported Capture Sources

The app currently supports workflows for:

- Steam captures
- PS5 captures
- Xbox captures
- manual/unmatched captures
- videos via sidecars where needed

## Recent Important Evolution

Recent published lines introduced:

- persistent shared data outside build folders
- batched `ExifTool` reads for faster scans
- dedicated game and photo index editors
- `GameId`-based grouping
- Steam App ID persistence in the game index
- `STID`-first SteamGridDB cover refresh with Steam fallback only when needed
- right-click cover fetch on a single folder
- refreshed platform-group headers in the Library with icon-led presentation from the shared workspace asset set
- persistent Library sorting modes with flattened non-platform views and per-tile platform badges
- Library top-bar import actions moved into the main browse surface with a quieter footer-style status line
- tightened Library toolbar spacing and restyled the sort picker shell
- Library header branding with the shared PixelVault logo, aligned search placement, and a cleaner filter-row balance for sort and folder-size controls
- startup Library view
- search and size sliders in the library
- preview-tile right-click actions
- self-healing for stale `Multiple Tags` master rows
- thumbnail queue prioritization and loader hardening for large libraries
- Steam cover-refresh timeout protection and better scoped-refresh deduping
- `STID` persistence in the Game Index
- Game Index save-time game-ID remapping and canonical folder renaming/moves

## Recent Non-Build Maintenance

After the `0.714` build, a live data cleanup pass removed `PC` from any indexed file that still had both `Steam` and `PC` when `Steam` was present.

Result:

- `594` files updated on disk
- `594` photo-index rows updated
- `0` remaining verified live files with both `Steam` and `PC`
- `0` remaining verified photo-index rows with both `Steam` and `PC`

## Source Of Truth Documents

Use these documents together:

- `C:\Codex\docs\POLICY.md` for behavior contracts and workflow rules
- `C:\Codex\docs\HANDOFF.md` for the current stop point
- `C:\Codex\docs\CHANGELOG.md` for release history

## Immediate Next Step

The next likely milestone is to keep polishing the Library browse surface while continuing the SteamGridDB backfill and validating the `STID`-first cover workflow against real library folders.
