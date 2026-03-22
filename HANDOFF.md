# PixelVault Handoff

## Active Workspace

Work out of:

- `C:\Codex`

Do not use `A:\Codex` as the live source of truth. It may contain older staging work, failed handoffs, and historical artifacts.

## Rulebook First

Before making app changes, read:

- `C:\Codex\POLICY.md`

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

- `0.714`

Current executable:

- `C:\Codex\dist\PixelVault-0.714\PixelVault.exe`

Current build pointer:

- `C:\Codex\CURRENT_BUILD.txt`

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

- `C:\Codex\PixelVaultData\cache\game-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-metadata-index-y_game_captures.cache`
- `C:\Codex\PixelVaultData\cache\library-folders-y_game_captures.cache`

Behavior summary:

- file metadata is the source of truth for per-file tags
- the photo index is the persistent per-file mirror/cache
- the game index is the master registry for `GameId`, canonical title, console, and Steam App ID
- the folder cache is derived state and may be rebuilt

## Recent Shipped State

Recent important published changes already in the `0.710` to `0.714` line:

- Library opens first and Settings became the separate utility hub
- library search and persistent folder-size slider were added
- folder preview slider was re-centered and preview tile chrome was reduced
- right-click actions were added for folder preview images
- stale `Multiple Tags` master rows now self-heal when files are corrected

See `C:\Codex\CHANGELOG.md` for the detailed version history.

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

The app docs have now been brought forward with a working policy document.

Immediate next task after this handoff:

1. initialize git in `C:\Codex`
2. add an appropriate `.gitignore`
3. commit the code, docs, and curated index snapshots
4. push to GitHub

## Important Expectations

- After every published build, repoint `C:\Codex\PixelVault.lnk` to the newest executable.
- Keep `POLICY.md` as the durable behavior contract.
- Keep `HANDOFF.md` short and current.
- Do not let the game index or photo index drift from the actual intended behavior without documenting the rule change.
