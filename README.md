# PixelVault

PixelVault is a native Windows desktop app for organizing, tagging, indexing, and browsing game screenshots and clips.

The live app line is based in:

- `C:\Codex`

Solution (optional): `C:\Codex\Codex.slnx` includes `PixelVault.Native`, **`PixelVault.LibraryAssets`**, and test projects.

Important workspace note:

- work out of `C:\Codex`
- if a tool session ever starts in `A:\Codex`, treat that as an environment quirk, not the real project root
- `C:\Codex` is the source of truth for code, builds, docs, and shared app data

**Current version and published exe path** are not duplicated here (they change every release). Use:

- `docs/CURRENT_BUILD.txt` — version string and full path to the last published `PixelVault.exe`
- `docs/CHANGELOG.md` — release notes per version
- `src/PixelVault.Native/PixelVault.Native.cs` — `const string AppVersion` for the in-repo build

**Convenience:** after `Publish-PixelVault.ps1`, the repo-root shortcut `PixelVault.lnk` and the junction folder below track the latest publish without editing this file:

- `C:\Codex\PixelVault.lnk`
- `C:\Codex\dist\PixelVault-current\` → points at the most recently published `PixelVault-<version>\` folder

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
- `FFmpeg` path for optional video thumbnails, clip previews, and richer clip details
- SteamGridDB token

### Index editors

PixelVault currently includes:

- Game Index editor for master game records
- Photo Index editor for per-file metadata rows

The Game Index also acts as the canonical folder-naming authority when records are saved, so library folders can be renamed/moved to follow edited game titles and platform splits.

## Data Layout

Shared persistent app data lives under:

- `C:\Codex\PixelVaultData`

Important paths (names depend on configured library root and cache hashing — adjust for your machine):

- `PixelVaultData/cache/` — SQLite indexes, folder caches, covers, thumbs, logs (see `POLICY.md` for what belongs in git)
- `docs/CURRENT_BUILD.txt`
- `docs/CHANGELOG.md`
- `docs/HANDOFF.md`
- `docs/POLICY.md`
- `docs/PROJECT_CONTEXT.md`

## Workspace Map

- `C:\Codex\src\PixelVault.Native`: live app source and SDK project
- `C:\Codex\scripts`: build/publish and developer utility scripts
- `C:\Codex\docs`: handoff, policy, changelog, project context, and current-build marker
- `C:\Codex\dist`: published versioned builds (`PixelVault-<version>/`) plus `PixelVault-current` junction
- `C:\Codex\tools-licenses`: third-party **license texts** merged into published **`tools\licenses\`** (primarily **ExifTool** — see **`docs/BUNDLED_TOOLS_REDISTRIBUTION.md`**)
- `C:\Codex\assets`: shared branding and UI assets
- `C:\Codex\tools`: optional local **`tools\`** folder for **`exiftool.exe`** (gitignored); **FFmpeg** is installed separately (**Path Settings**), not bundled here by default
- `C:\Codex\PixelVaultData`: live shared app data, indexes, caches, and logs
- `C:\Codex\legacy`: older GameCaptureManager workflow files kept for history
- `C:\Codex\archive`: backups and old artifacts not needed for day-to-day development

## Source And Packaging

Authoritative source:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`
- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`

Each publish run also copies a source tree and changelog into the output folder (see `scripts/Publish-PixelVault.ps1`).

## Running The App

**Latest published build** (after you run the publish script):

```powershell
& "C:\Codex\dist\PixelVault-current\PixelVault.exe"
```

Or open `C:\Codex\PixelVault.lnk`. For an exact versioned path, read `docs/CURRENT_BUILD.txt`.

## Building And Publishing

Build the live source with the SDK project:

```powershell
dotnet build C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj -c Release
```

Publish a new versioned folder under `dist\` (name pattern **`PixelVault-M.AAA.BBB`** per **`docs/POLICY.md`**). The script reads **`AppVersion`** from `PixelVault.Native.cs` when you omit `-Version`:

```powershell
pwsh -File C:\Codex\scripts\Publish-PixelVault.ps1 -Force
```

Optional: `-OutputRoot` if the default `dist` folder is locked; see script header comments.

**Installer / delta-update channel (Velopack):** self-contained **`win-x64`** publish + **`vpk pack`** — **`scripts/Publish-Velopack.ps1`** and **`docs/VELOPACK.md`**. Fresh-machine smoke tests: **`docs/VELOPACK_VM_SPIKE_CHECKLIST.md`**.

**Code signing (Authenticode):** **`docs/PUBLISH_SIGNING.md`** — **`Publish-PixelVault.ps1 -Sign`** and **`vpk pack`** **`signParams`**.

## Project Documents

Use these together:

- `POLICY.md`: working rules, versioning, form responsibilities, and git policy
- `HANDOFF.md`: short current stop point for the next conversation
- `PROJECT_CONTEXT.md`: broader project architecture and current-state overview
- `CHANGELOG.md`: published version history
- `docs/EULA.md`: end-user license **draft** for distribution (**§5.4**); host at HTTPS with `docs/PRIVACY_POLICY.md` for Store/listings

## Git Scope

This repo is intended to track:

- app source and launcher code
- project documentation
- curated snapshots of the game index and photo index

It should not track:

- the actual screenshot/video library on `E:\` (or other capture volumes)
- runtime logs
- cover cache
- thumbnail cache
- backup files
- generated EXEs

## Legacy Note

Older PowerShell workflow files are now grouped under `C:\Codex\legacy\GameCaptureManager` so the active native app line is easier to navigate.

## Storage Note

The live Game Index and Photo Index are backed by a per-library SQLite database under `PixelVaultData/cache/`.

Older tab-delimited `game-index-*.cache` and `library-metadata-index-*.cache` files may still appear as migration inputs or historical artifacts; SQLite is the primary runtime store for current builds.
