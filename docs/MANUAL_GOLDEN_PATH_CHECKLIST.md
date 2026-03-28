# PixelVault Manual Golden Path

Short manual verification path for risky changes.

Use this after touching import, indexing, metadata persistence, or Library behavior.

## Setup

- Confirm the app launches from the current published build.
- Keep one small known-good intake sample ready.
- Keep the NAS-backed test export folder available at `Y:\PixelVault-Test-Exports`.

## Golden Path

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

## Good Extra Checks

- Open the intake preview window once.
- Test one video tile if media behavior changed.
- Test one rename or comment/tag update if metadata writing changed.

## Pass Criteria

- No blocking UI stall on the happy path
- Imported file is visible in the expected Library location
- Metadata-dependent UI still matches the imported file
- No obvious layout or selection regressions in the Library
