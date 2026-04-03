# PixelVault Manual Golden Path

Short manual verification path for risky changes.

Use this after touching import, indexing, metadata persistence, or Library behavior.

## Setup

- Confirm the app launches from the current published build.
- Keep one small known-good intake sample ready.
- Optional: use repo **`C:\Codex\PixelVaultData\import-stability-test-upload`** (minimal PNGs + `README.txt`) as the Settings upload folder for import smoke tests, or copy those files into your real upload folder.
- Keep the NAS-backed test export folder available at `Y:\PixelVault-Test-Exports`.

## Minimum Pass

Run this short path after any risky change before moving on to broader manual QA.

1. Launch PixelVault and open the Library.
2. Run a Library refresh or targeted rescan.
3. Import one known-good file through the normal intake path.
4. Confirm the file lands in the expected destination folder and the SQLite-backed data still matches it.
5. Close and reopen the Library, then verify the imported file still appears in the right folder.

If this path fails, stop and fix that first before spending time on deeper spot checks.

## Expanded Checks

1. Launch PixelVault and open the Library.
2. Run a Library refresh or targeted rescan.
3. Confirm the Library still renders folders and capture rows normally.
4. Import one known-good file through the normal intake path.
5. Confirm the file lands in the expected destination folder.
6. Reopen the Library and verify the imported file appears in the right folder.
7. If metadata or index code changed, confirm the corresponding SQLite-backed data still reflects the file correctly.
8. If Manual Intake changed, verify one manual case from start to finish.
9. If Steam handling changed, verify at least one Steam filename case.
10. If virtualization or layout changed, scroll a larger folder and look for spacing, rerender, or selection regressions.

## Phase C3 — Intake UI extraction + Steam rename / move glue

Run after changes to **`UI/Intake/*`**, **`ImportWorkflow`**, or Steam rename / move ordering. Automated coverage: `SteamRenamePathMappingTests` (path map → review items, manual batch, move source list).

1. **Intake preview** — Settings → **Preview Intake** → **Refresh**; confirm queue/rename/manual counts and RichText summary still look right.
2. **Steam rename → move** — Put one **numeric Steam AppID–prefixed** screenshot in the upload folder (same shape `FilenameParserServiceTests` uses). Run **Import** (not “import and comment” unless you are testing that path). Confirm the file is **renamed** to `GameTitle_…` in upload (or logs), then **lands in the library** under the expected game folder after move/sort.
3. **Import and comment / Import and Edit** — If you use that flow, complete it once with a small batch; confirm no regression vs. plain import for Steam-titled files.
4. **Recurse rename (optional)** — With **recurse rename** enabled for a nested Steam file, confirm rename still runs on the intended scope and the workflow finishes.

## Good Extra Checks

- Open the intake preview window once.
- Test one video tile if media behavior changed.
- Test one rename or comment/tag update if metadata writing changed.

## Pass Criteria

- No blocking UI stall on the happy path
- Imported file is visible in the expected Library location
- Metadata-dependent UI still matches the imported file
- No obvious layout or selection regressions in the Library
