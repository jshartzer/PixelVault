# PixelVault Project Context

## Overview

`PixelVault` is a native Windows desktop app built in C# and WPF for organizing, tagging, previewing, and browsing a game screenshot and clip library.

The current live app line is based in:

- `C:\Codex`

The app now runs from packaged builds under `C:\Codex\dist\PixelVault-x.xxx`, with shared persistent data stored outside the versioned build folders in `C:\Codex\PixelVaultData`.

## Current Published State

Current published build:

- `0.714`

Current executable:

- `C:\Codex\dist\PixelVault-0.714\PixelVault.exe`

Desktop shortcut:

- `C:\Codex\PixelVault.lnk`

Current build pointer:

- `C:\Codex\CURRENT_BUILD.txt`

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

- `C:\Codex\dist\PixelVault-0.714\PixelVault.Native.cs`

Shared data root:

- `C:\Codex\PixelVaultData`

Shared cache folder:

- `C:\Codex\PixelVaultData\cache`

Shared logs folder:

- `C:\Codex\PixelVaultData\logs`

Live cache files:

- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-folders-y_game_captures.cache`

Current library/intake defaults:

- source: `Y:\Game Capture Uploads`
- destination: `Y:\Game Captures`
- library: `Y:\Game Captures`

Tool dependency:

- `C:\Codex\tools\exiftool.exe`

## Current Architecture

The app is still implemented primarily in one native source file.

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

It stores:

- file path
- stamp
- `GameId`
- console label
- tag text

### Game index

The game index is the master record table.

It stores:

- stable `GameId`
- canonical title
- console/platform
- Steam App ID
- file count
- folder-path context

Grouping is intended to follow `GameId`, not raw title text.

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
- right-click cover fetch on a single folder
- startup Library view
- search and size sliders in the library
- preview-tile right-click actions
- self-healing for stale `Multiple Tags` master rows

## Recent Non-Build Maintenance

After the `0.714` build, a live data cleanup pass removed `PC` from any indexed file that still had both `Steam` and `PC` when `Steam` was present.

Result:

- `594` files updated on disk
- `594` photo-index rows updated
- `0` remaining verified live files with both `Steam` and `PC`
- `0` remaining verified photo-index rows with both `Steam` and `PC`

## Source Of Truth Documents

Use these documents together:

- `C:\Codex\POLICY.md` for behavior contracts and workflow rules
- `C:\Codex\HANDOFF.md` for the current stop point
- `C:\Codex\CHANGELOG.md` for release history

## Immediate Next Step

The next planned milestone is to initialize git in `C:\Codex`, add a proper `.gitignore`, and commit the code plus curated docs/index snapshots to GitHub.
