# PixelVault Handoff

## Active Workspace

Work out of:

- `C:\Codex`

Treat `C:\Codex` as the only live source of truth for code, builds, docs, and shared app data.

Current live build source:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`
- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`
- `C:\Codex\scripts\Publish-PixelVault.ps1`

Published `dist\PixelVault-x.xxx\PixelVault.Native.cs` files are version snapshots, not the primary edit target.

## Rulebook First

Before making app changes, read:

- `C:\Codex\docs\POLICY.md`

That file now holds the durable working rules for:

- versioning
- form responsibilities
- data ownership
- index behavior
- release packaging
- git snapshot policy

This handoff is the short current-state summary.

## Current Published Build

Current live build:

- `0.755`

Current executable:

- `C:\Codex\dist\PixelVault-0.755\PixelVault.exe`

Current build pointer:

- `C:\Codex\docs\CURRENT_BUILD.txt`

Desktop shortcut that must always follow the newest published build:

- `C:\Codex\PixelVault.lnk`

## Current App Shape

PixelVault now opens into the Library view by default.

Important current windows/forms:

- Library window is the main startup surface
- Settings window is the utility/import hub
- Path Settings edits environment paths and tool paths only
- Game Index is the master game-record editor
- Photo Index is the per-file metadata cache editor
- Library Edit Metadata updates selected existing library files
- Review window handles `Process with Comments`

## Data Model Summary

Shared persistent data lives under:

- `C:\Codex\PixelVaultData`

Important live cache files:

- `C:\Codex\PixelVaultData\cache\pixelvault-index-y_game_captures.sqlite`
- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-folders-y_game_captures.cache`

Behavior summary:

- file metadata is the source of truth for per-file tags
- the SQLite index database is the live runtime store for the Game Index and Photo Index
- the photo index is the persistent per-file mirror/cache
- the game index is the master registry for `GameId`, canonical title, console, Steam App ID, and `STID`
- the folder cache is derived state and may be rebuilt
- the older `game-index-*.cache` and `library-metadata-index-*.cache` files are now legacy migration/snapshot files rather than the primary runtime store
- as of `0.742`, Game Index save is also authoritative for canonical library folder naming

## Recent Shipped State

Recent important published changes in the current `0.724` to `0.755` line:

- intake preview/process/manual flows now reuse shared source inventories instead of rescanning the same roots repeatedly
- metadata writes run with bounded parallel `ExifTool` workers
- the library/gallery now use deferred thumbnail loading plus capped image caching
- optional FFmpeg-backed video poster generation is implemented
- thumbnail-loader regressions were fixed through `0.728`
- cover-refresh requests now use explicit timeouts, and scoped cover refresh dedupes by folder master key instead of raw name
- the Game Index now stores `STID` for SteamGridDB IDs
- Game Index save now remaps stale game IDs before rebuild so deleted/consolidated rows stop resurrecting
- Game Index save now renames/moves library folders on disk to canonical names based on the saved title/platform, adding ` - Platform` suffixes when the same title exists on multiple platforms
- SteamGridDB token-backed `STID` resolution is now wired into the Game Index
- cover refresh can now prefer SteamGridDB portrait art when an `STID` is available
- Path Settings now supports both a SteamGridDB token and an optional `FFmpeg` path for video poster generation
- Steam screenshot rename/import now records the raw Steam AppID into the Game Index before filename normalization
- cover refresh now defaults to saved `STID` values, preserves existing cached art, and avoids unnecessary Steam App ID lookups during single-folder fetches
- Library section headers now use larger typography, cleaner folder counts, and shared console icons from `C:\Codex\assets`
- the Library now has persistent sort modes for grouped platform view, recently added, and most photos, with platform badges on tiles when headers are hidden
- the Library top bar now exposes Import actions directly, drops the Photography shortcut there, and moves status into a smaller footer line
- the Library toolbar spacing was tightened again so the right-side action buttons fit cleanly, and the sort picker got a cleaner shell treatment
- the Library header now uses the shared PixelVault logo, the search field lines up with the import actions, and the sort and folder-size controls were rebalanced for a cleaner filter row
- the Library header no longer reserves space for the PixelVault logo or folder count, and the search field now starts on the same left edge as the `Import` button
- Steam rename detection is now limited to known Steam screenshot filename patterns so unrelated numeric filenames do not get misclassified during intake
- library refresh/index reconciliation now preserves an existing `GameId` when the platform still matches, reducing accidental regrouping from folder-name guesses
- normal Steam intake now writes `Steam` without silently adding `PC`, keeping shipped metadata aligned with the current review flow and prior cleanup rules
- Library-driven imports now default move conflicts to `Rename` even when the Settings-only conflict dropdown has not been created yet, which fixes a null-reference crash after metadata writes
- import failure logging now records full exception details for workflow and manual-intake errors
- the live Game Index and Photo Index runtime store now uses a per-library SQLite database with first-run migration from the older flat cache files
- the SQLite-backed index store now initializes its runtime provider correctly on startup, fixing the `SetProvider` popup that appeared on write paths
- the Library toolbar now exposes `Game Index` and `Photo Index` directly, those editors stay modeless, and folder right-click now includes a small `Edit IDs...` dialog for Steam App ID and SteamGridDB ID edits
- the Library index buttons now sit centered between Search and Sort with a smaller light-purple treatment, and the `Edit IDs` dialog is taller with a cleaner action row

See `C:\Codex\docs\CHANGELOG.md` for the detailed version history.

## Recent Non-Build Maintenance

After `0.714`, a data cleanup pass was run against the live library state.

Result:

- any indexed file that still had both `Steam` and `PC` had `PC` removed from the file metadata when `Steam` was present
- the same cleanup was mirrored into the photo index

Cleanup result:

- `594` files updated on disk
- `594` photo-index rows updated
- verification found `0` remaining live files with both `Steam` and `PC`
- verification found `0` remaining photo-index rows with both `Steam` and `PC`

Safety backup created before that cleanup:

- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache.bak-20260322-165520`

This was a data-only maintenance pass, not a new app build.

## Current Stop Point

The current live build is `0.755`, and the latest work polishes the new Library-side index controls:

1. the Library index buttons now sit between Search and Sort instead of in the top toolbar, with a smaller centered treatment that leaves the action row cleaner
2. the folder `Edit IDs...` dialog is taller and its Save/Cancel buttons are aligned more cleanly, so both ID fields stay visible
3. the earlier modeless index editors, Library-side ID edit flow, SQLite migration, and provider-init fixes remain part of the current line

The most likely next product step is:

1. run a focused real-library validation pass against `0.755` to confirm the repositioned Library index buttons and taller `Edit IDs...` dialog feel right in normal use
2. decide whether the legacy tracked `game-index-*.cache` and `library-metadata-index-*.cache` snapshots should remain in git as historical artifacts or be retired now that SQLite is the live runtime store
3. consider adding lightweight index-health tooling such as row counts, rebuild/migrate status, and vacuum/backup actions if library scale keeps growing

## Important Expectations

- After every published build, repoint `C:\Codex\PixelVault.lnk` to the newest executable.
- Keep `C:\Codex\docs\POLICY.md` as the durable behavior contract.
- Keep `C:\Codex\docs\HANDOFF.md` short and current.
- Do not let the game index or photo index drift from the actual intended behavior without documenting the rule change.
- If a build changes record identity or folder naming, update both the handoff docs and the curated cache snapshots intentionally before committing.
