# PixelVault Handoff

## Active Workspace

Work out of:

- `C:\Codex`

Treat `C:\Codex` as the only live source of truth for code, builds, docs, and shared app data.

Important:

- work out of `C:\Codex`
- if a session or app shell reports `A:\Codex`, ignore that and continue using `C:\Codex`
- do not treat `A:\` as the active project drive

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

- `0.774`

Current executable:

- `C:\Codex\dist\PixelVault-0.774\PixelVault.exe`

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

Recent important published changes in the current `0.724` to `0.774` line:

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
- the `Edit IDs` Save and Cancel buttons now share the same vertical alignment and margin treatment so the action row sits level
- Library-driven imports now skip the Settings-only preview repaint path when that preview control is not present, which fixes the shared workflow null reference after sort
- completed import runs now open a dark themed summary window that matches the other status monitors and shows rename, metadata, move, sort, and unmatched-item totals
- the live source has started moving toward a modular monolith by extracting shared models and the timeout web client into dedicated files without changing the one-executable app shape
- the Library detail capture view now supports multi-select actions, including selection-aware metadata editing and permanent delete from the live viewer instead of from the Photo Index editor
- Photo Index row removal is now explicitly `Forget Row`, and Library metadata edits can regroup files when the title or platform identity changes
- the Library now reuses the rebuilt folder cache after metadata edits and live deletes instead of forcing a cold refresh that temporarily blanked the full tile grid
- the folder-tile right-click `Fetch Cover Art` action now forces a fresh pull for cached downloaded art, while the toolbar-wide Library cover refresh still skips titles that already have art
- thumbnail caching now keeps recently used images around longer in memory and buckets decode sizes more aggressively so revisiting a folder is less likely to trigger a cold thumbnail reload
- manual clears for Steam AppID and STID now persist as explicit do-not-auto-resolve choices, so cover refresh and ID resolution stop writing those values back after you blank them out in `Edit IDs...`
- Library regroup, Game Index folder-align, and capture-delete flows now invalidate only the changed file and folder thumbnails instead of clearing the whole image cache, which keeps the rest of the Library populated during edits
- video capture tiles now retry FFmpeg-based frame extraction instead of staying stuck on generic fallback cards, and Library detail tiles support muted hover previews for video clips
- Library launch now reuses existing on-disk cached thumbnails immediately so the browser looks populated faster on startup, and video hover playback now runs inline inside the same capture tile
- inline video hover playback now preloads each visible clip source and reuses the existing poster bounds, which makes hover start faster and preserves the video aspect ratio instead of forcing a square playback surface
- Library video hover now prefers a lightweight cached 10-second preview clip generated with FFmpeg, and visible video tiles warm those preview clips in the background to reduce hover startup lag
- Library hover preview has been switched back to the original direct-video playback method because it felt materially faster than the preview-clip path, while keeping the newer inline-tile aspect-ratio fix

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

The current live build is `0.774`, and the latest work now layers real library-side selection/delete behavior plus a grouping fix on top of the modular-refactor baseline:

1. Library detail view supports multi-select capture actions, with right-click respecting the selected set and `Edit Metadata` working on the current selection
2. permanent delete now lives on the Library capture viewer, where it removes the files from disk and clears their photo-index entries; Photo Index row removal is only a temporary `Forget Row`
3. Library metadata edits now reassign grouping when the game title or platform identity changes, so renamed titles can move into the correct existing group or a new one
4. post-edit and post-delete Library refresh now uses the rebuilt cache directly, avoiding the temporary full-grid thumbnail unload seen after deleting the last file from a folder
5. right-click folder cover refresh now forces a new pull for cached auto-downloaded art without changing the toolbar-wide refresh behavior
6. thumbnail reuse is now stickier across folder revisits because the image cache is recent-use instead of FIFO and on-disk thumbnails share normalized size buckets
7. manual AppID/STID clears from `Edit IDs...` are now respected by later cover-refresh and resolver flows instead of being auto-filled again
8. regroup, delete, and folder-align actions now preserve the rest of the live Library thumbnails by evicting only affected cache entries instead of globally clearing the image cache
9. Library video captures now default to larger `500`-pixel detail tiles, retry real FFmpeg frame thumbs when older fallback cards exist, and can play a muted 10-second hover preview in the detail pane
10. Library tiles now reuse cached thumbs immediately on startup, and video hover playback happens inline in the existing tile rather than in a popup surface
11. inline hover preview now preloads the local media source and keeps the video constrained to the poster’s existing layout, improving hover responsiveness and preserving aspect ratio during playback
12. video hover playback now uses and warms a short cached preview clip rather than the full source file, with the goal of making large local captures start much faster on hover
13. hover preview now uses the original direct playback path again, but still renders inline inside the tile and preserves the thumbnail aspect ratio while playing

The most likely next product step is:

1. keep peeling code out of `PixelVault.Native.cs` in low-risk slices, with SQLite/indexing still the next best extraction target
2. exercise the new Library capture selection/delete path and the metadata regrouping fix against a few live title edge cases such as punctuation and trademark variants
3. decide whether the legacy tracked `game-index-*.cache` and `library-metadata-index-*.cache` snapshots should remain in git as historical artifacts or be retired now that SQLite is the live runtime store

## Important Expectations

- After every published build, repoint `C:\Codex\PixelVault.lnk` to the newest executable.
- Keep `C:\Codex\docs\POLICY.md` as the durable behavior contract.
- Keep `C:\Codex\docs\HANDOFF.md` short and current.
- Do not let the game index or photo index drift from the actual intended behavior without documenting the rule change.
- If a build changes record identity or folder naming, update both the handoff docs and the curated cache snapshots intentionally before committing.
