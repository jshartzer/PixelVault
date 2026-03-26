# PixelVault Working Policy

## Purpose

This document defines the working rules for the live `C:\Codex` PixelVault app line.

Use it as the source of truth for:

- workspace expectations
- versioning behavior
- form responsibilities
- data ownership rules
- release packaging
- git snapshot policy

## Active Workspace

Current active workspace:

- `C:\Codex`

Treat `C:\Codex` as the only live workspace for the active PixelVault line.

If any shell, app thread, or tool session reports `A:\Codex`, treat that as a runtime/environment quirk only.

The active project workspace remains `C:\Codex`.

Live packaged builds are published under:

- `C:\Codex\dist\PixelVault-x.xxx`

Shared persistent app data lives under:

- `C:\Codex\PixelVaultData`

## Versioning Policy

PixelVault uses decimal build versioning.

General rule:

- very small fix or narrow cleanup: increment by roughly `0.001` to `0.004`
- normal bugfix or focused UX improvement: increment by roughly `0.005`
- feature-sized change or workflow adjustment: increment by roughly `0.010`
- major data-model or architectural shift: increment by more than `0.010` when justified

The exact bump should reflect the real size and risk of the change, not an arbitrary schedule.

### Release responsibilities

When a new app build is published:

1. Update `AppVersion` in the current live source file.
2. Build and publish from `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`, normally through `C:\Codex\scripts\Publish-PixelVault.ps1`.
3. Publish the build into a new folder under `C:\Codex\dist\PixelVault-x.xxx`.
4. Copy the current source snapshot into that dist folder as `PixelVault.Native.cs`.
5. Update `C:\Codex\docs\CURRENT_BUILD.txt`.
6. Update `C:\Codex\docs\CHANGELOG.md`.
7. Update the version-local `CHANGELOG.md` inside the new dist folder.
8. Repoint `C:\Codex\PixelVault.lnk` to the newest `PixelVault.exe`.

Data-only cleanup work does not require a new build version unless app code changes are also shipped.

## Application Structure

PixelVault is a native Windows desktop app built in C# and WPF.

The current live implementation is now a modular monolith rooted in:

- `C:\Codex\src\PixelVault.Native`

Key live source areas currently include:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`
- `C:\Codex\src\PixelVault.Native\Indexing`
- `C:\Codex\src\PixelVault.Native\Import`
- `C:\Codex\src\PixelVault.Native\MediaTools`
- `C:\Codex\src\PixelVault.Native\Metadata`
- `C:\Codex\src\PixelVault.Native\Models`
- `C:\Codex\src\PixelVault.Native\Storage`
- `C:\Codex\src\PixelVault.Native\UI`

The SDK build project for that source tree lives at:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`

Published builds should still carry a version-local source snapshot in:

- `C:\Codex\dist\PixelVault-x.xxx\PixelVault.Native.cs`

The modular extraction is acceptable as long as the behavioral contracts below remain stable.

## Form Responsibilities

### Library window

The Library is the main startup window.

It is responsible for:

- browsing the grouped game library
- searching folders
- running `Import`, `Import and Comment`, and `Manual Import` from the Library toolbar
- resizing folder tiles
- opening folder detail previews
- running `Refresh`, `Rebuild`, and `Fetch Covers`
- supporting right-click folder actions such as single-folder cover fetch

It should not act as the global settings editor.

### Settings window

The Settings window is the former home screen and now acts as the utility hub.

It is responsible for:

- intake preview
- import processing
- process-with-comments flow
- manual intake launch
- maintenance utilities
- utility shortcuts such as indexes and logs

It should not be the primary browsing surface for the library.

### Path Settings window

The Path Settings window is only for environment configuration.

It is responsible for:

- source folder list
- destination folder
- library folder
- `ExifTool` path
- `FFmpeg` path
- SteamGridDB token

It should not mix in import actions or index editing.

### Review window

The Review window is the `Process with Comments` flow.

It is responsible for:

- reviewing intake items before processing
- per-item comments
- console/platform tag selection
- optional `Game Photography` tagging
- optional delete-before-processing

It should only affect the items currently being reviewed.

### Manual Intake / metadata editor

The manual metadata form handles unmatched or manually-curated intake items.

It is responsible for:

- assigning title, platform, tags, comments, and capture time
- selecting or confirming a master game record
- writing metadata back to the selected files
- updating the photo index to match the applied edit

Behavior contract:

- file metadata writes are driven by the form values
- the photo index must mirror the applied form values immediately after save
- new game-index rows should only be created through explicit confirmation
- game-title choices should be searchable by game name while still showing console context in the picker

### Library Edit Metadata form

The library metadata form is for editing existing media already in the library.

It is responsible for:

- updating metadata on the selected file or files
- reassigning a file to an existing game record
- optionally confirming creation of a new master game record
- updating the photo index for the selected rows only

Behavior contract:

- only selected items may be changed
- tag removals must remove tags from the file itself, not just the index
- tag changes must be written both to the file and to the photo index
- console tags must be interpreted consistently from the final saved tag set
- library-edit startup should favor batched metadata reads over per-file tool launches when practical

### Game Index editor

The Game Index editor is the master-record editor.

It is responsible for:

- maintaining one canonical record per game and platform
- storing `GameId`, title, console, Steam App ID, `STID`, file counts, and folder path context
- adding new game records intentionally
- resolving or manually correcting Steam App IDs
- resolving or manually correcting SteamGridDB IDs
- deleting or merging stale master rows when appropriate

Behavior contract:

- same title on different platforms gets different master records
- duplicate rows for the same title and platform should merge
- zero-file rows should not appear as active library folders

### Photo Index editor

The Photo Index editor is the per-file metadata cache editor.

It is responsible for:

- editing per-file `GameId`, console label, and tag text
- multi-row selection
- deleting selected rows
- pulling selected rows from the underlying file metadata
- saving the updated index and refreshing the editor grid

Behavior contract:

- it reflects per-file state, not master-record state
- `Pull From File` / `Reload` must read the selected file(s), update only those rows, save, and refresh
- it should not invent tags or game assignments on its own

### Photography window

The Photography window is a filtered browsing surface for captures tagged as game photography.

It should remain a browse/filter view, not a competing metadata source.

### Changelog and logs windows

These are support windows.

They are responsible for inspection and traceability only and should not mutate app state.

## Data Ownership Rules

### File metadata

Per-file embedded metadata is the source of truth for:

- tag membership
- comments / descriptions
- platform tags
- date overrides written into the file

If file metadata and cache disagree, the long-term target is for scans and reloads to realign the cache to the file.

### Photo index

The photo index is a persistent per-file mirror and working cache.

Its live runtime storage is the per-library SQLite index database under `C:\Codex\PixelVaultData\cache`.

Current file:

- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`

It stores:

- file path
- stamp
- `GameId`
- console label
- tag text

The photo index should mirror the effective file metadata and should be updated whenever metadata edits are applied or file-based reloads are run.

### Game index

The game index is the master registry for game identities.

Its live runtime storage is the per-library SQLite index database under `C:\Codex\PixelVaultData\cache`.

Current file:

- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`

It stores canonical game-level data such as:

- `GameId`
- canonical title
- platform / console
- Steam App ID
- `STID`
- file count
- folder path reference

Grouping should be driven by `GameId`, not fragile title text.

### Folder cache

The folder cache is derived state.

Current file:

- `C:\Codex\PixelVaultData\cache\library-folders-y_game_captures.cache`

It is allowed to be rebuilt from the photo index and game index. It should not be treated as the long-term source of truth for tags.

### Legacy cache files

The old tab-delimited `game-index-*.cache` and `library-metadata-index-*.cache` files are now legacy migration/snapshot artifacts.

They should not be treated as the authoritative live runtime store once the SQLite database exists for a library root.

### Covers and thumbnails

Cover art caches and thumbnails are derived assets.

They should be considered rebuildable and should not be treated as canonical metadata.

## Console Tag Rules

Console grouping must come from recognized console tags in the final saved tag set.

Current recognized families:

- `Steam`
- `PC`
- `PS5` / `PlayStation`
- `Xbox`
- `Platform:<Custom>`

Rules:

- `Steam` and `PC` are separate console tags
- if more than one console family is present, the item becomes `Multiple Tags`
- `Other` should only be used when no recognized platform family exists

## Index Integrity Rules

- Photo-index edits should only change the selected row or rows.
- Library metadata edits should only change the selected item or items.
- Game-index changes should be explicit and deliberate because it is the master record.
- If a file is corrected away from `Multiple Tags`, stale zero-file `Multiple Tags` master rows should disappear.
- If a file has both `Steam` and `PC` and `Steam` is the intended platform, `PC` must be removable from both file metadata and the photo index.

## Persistence Rules

App state should persist across versioned builds by storing shared data under:

- `C:\Codex\PixelVaultData`

Versioned `dist` folders should be replaceable without losing:

- settings
- indexes
- logs
- covers
- thumbnails

## Git Snapshot Policy

GitHub should be used for source history and curated state snapshots.

Track in git:

- source files
- docs such as `PROJECT_CONTEXT.md`, `HANDOFF.md`, `POLICY.md`, and `CHANGELOG.md`
- `CURRENT_BUILD.txt`
- the game index
- the photo index

Avoid tracking in git:

- the actual screenshot / video library on `Y:\`
- logs
- cover cache
- generated thumbnails
- `.bak-*` backup files
- temporary files
- versioned EXE output unless intentionally archived

Recommended commit cadence:

- after each published build
- after meaningful metadata/index cleanup passes
- after behavior-contract documentation changes

Recommended tag cadence:

- create a git tag for important published builds such as `v0.714`

## Handoff Rule

Any future handoff should update:

- `C:\Codex\docs\HANDOFF.md`
- `C:\Codex\docs\CHANGELOG.md`
- `C:\Codex\docs\CURRENT_BUILD.txt` when a new build is published

`C:\Codex\docs\HANDOFF.md` should summarize the current stop point.

`C:\Codex\docs\POLICY.md` should remain the durable rulebook.
