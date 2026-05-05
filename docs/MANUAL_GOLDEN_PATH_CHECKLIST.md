# PixelVault Manual Golden Path

Short manual verification path for risky changes.

Use this after touching import, indexing, metadata persistence, or Library behavior.

## Setup

- Confirm the app launches from the current published build (for **zip-style** **`dist/PixelVault-*`**, **`PixelVault-current`**, or **`PixelVault.lnk`**).
- **Velopack / installer builds:** after **`Publish-Velopack.ps1`**, run **`scripts/Verify-DistributionLayout.ps1`** on **`dist\Velopack\publish-<version>`**, then **`docs/VELOPACK_VM_SPIKE_CHECKLIST.md`** on a clean VM before relying on this golden path alone.
- **Legal / listing URLs** (privacy, EULA, support): roadmap recommends finishing **after** installer + signing confidence — **`PV-PLN-DIST-001` §10.1**.
- Keep one small known-good intake sample ready.
- Optional: use repo **`C:\Codex\PixelVaultData\import-stability-test-upload`** (minimal PNGs + `README.txt`) as the Settings upload folder for import smoke tests, or copy those files into your real upload folder.
- Keep the NAS-backed test export folder available at `E:\PixelVault-Test-Exports`.

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
11. If **Photo workspace hero/banner** download or selection handling changed: switch to **Photo** mode, scrub the folder rail quickly on a few titles that auto-fetch banner art, and confirm the header banner still tracks the current selection without obvious duplicate fetch churn (network tray or debugger optional). Cancelling selection should abandon the prior wait quickly (Steam / SteamGridDB ID resolution and hero HTTP use cancellation between steps).

## PV-PLN-PERF-001 — performance spot checks

Run this section for PixelVault speed/efficiency slices, especially import, library cache, detail pane, folder rendering, progress windows, or background metadata work.

### Performance setup

1. Start from a normal build, not a debugger-attached session unless the change specifically needs debugger output.
2. Close other heavy disk or network jobs.
3. Confirm the app is writing `PERF` rows to `C:\Codex\PixelVaultData\logs\PixelVault-native.log`.
4. Before comparing a slice, export the current log summary:

```powershell
& 'C:\Codex\scripts\Export-PixelVaultPerformanceBaseline.ps1' -Since '2026-05-01T00:00:00Z' -OutputPath 'C:\Codex\docs\perf\PV-PLN-PERF-001-latest-manual-run.md'
```

### Required performance flows

| Flow | What to do | Record | Pass / fail target |
|------|------------|--------|--------------------|
| App open -> library visible | Fully close PixelVault, relaunch, open Library, wait for the first folder list and first detail pane. | `LibraryBrowserFirstFolderListPaint`, `LibraryBrowserFirstDetailPaint`. | Pass if Library is usable without a blank/stuck shell; fail if first folder paint is clearly worse than the latest baseline by more than about 25% or 1 second without an explained reason. |
| Import 25 files | Import a copied 25-file intake batch from top-level source only. | `ImportPreparation`, `ImportWorkflowRun`, `ImportWorkflowStep`. | Pass if progress moves steadily, final summary is correct, files land in expected folders, and timing is not worse than baseline by more than about 25%. |
| Import 100 files | Repeat with a copied 100-file batch. | Same import timing rows plus progress-window responsiveness notes. | Pass if the progress window remains responsive and final counts match; fail on obvious UI freeze, missing files, or repeated whole-library stalls. |
| Import 500 files | Repeat with a copied/staged 500-file batch. | Same import timing rows, plus any long `sort` or `LibraryFolderCache` rows after import. | Pass if the app stays responsive enough to cancel/observe progress and completion is correct; fail if the dispatcher visibly locks for multiple seconds per file or the workflow appears hung. |
| HDR PNG/JXR import | Import a batch containing same-stem `.png` + `.jxr` pairs. | `ImportWorkflowStep` rows for `hdrFallback`, `move`, and `sort`; verify `HDR Duplicates` folder. | Pass if selected JXR files import, PNG alternates move under the destination drive's `HDR Duplicates` folder by game, and the Library ignores that folder. |
| Large game folder photo view | Open a large folder such as Diablo IV or Timeline and switch thumbnail sizes once. | `LibraryDetailRender` details, especially `quickMediaMapMs`, file count, row count. | Pass if the first usable view appears promptly and later cached renders avoid repeated 10s+ `quickMediaMapMs` spikes; fail if selection becomes stale or UI updates after moving away. |
| Fast-scroll detail pane | Scroll a large detail pane quickly, then change selection while thumbnails/metadata are still settling. | Visual jank notes, stale render behavior, `LibraryDetailRender` spikes. | Pass if scrolling stays coherent, selection follows the current folder, and stale work does not overwrite the new selection. |

### Progress and log responsiveness notes

- During the 100-file and 500-file import flows, try moving the progress window and pressing **Cancel** once on a copied throwaway batch. Pass if the window responds quickly, the cancel/final status line appears, and the log pane shows the latest useful detail without multi-second per-file stalls.
- With troubleshooting logging enabled, confirm `PixelVault-native.log` still contains per-file import detail plus final summary/error lines after a large import. Pass if the log is complete and the app does not visibly freeze while those lines are written.
- Automated guardrails for this area: `WorkflowProgressCoalescerTests`, `WorkflowProgressLogBufferTests`, `ImportServiceLogBatchingTests`, and `TroubleshootingLogRedactionTests.AppendMainLines_ReturnsFormattedTimestampedLinesAndWritesAllMessages`.

### Performance reading rules

- Treat the first run after a rebuild, cache clear, or major library change as a warm-up unless the slice specifically targets cold start.
- Compare against the latest `docs/perf/PV-PLN-PERF-001-*.md` artifact, not memory.
- Functional failures beat timing wins: wrong destination folders, missing HDR duplicates, stale selection, or broken metadata are failures even if timings improve.
- A timing regression is acceptable only when it is explained in the plan or changelog and is offset by a deliberate correctness improvement.

## Library detail scroll + import-and-edit Steam title (threading)

Run after changes to **`LibraryBrowserHost`**, **`MainWindow.LibraryBrowserShowOrchestration`**, or Library partials under **`UI/Library/MainWindow.LibraryBrowser*.cs`** (detail / screenshot grid), embedded-metadata repair, or **`ImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** / manual metadata finish. See **`docs/REVIEW_RESPONSE_2026-04-02.txt`**.

### Library — preserved scroll vs. metadata-refined rerender

1. Open the Library and select a game folder with **many** captures (enough to scroll the right-hand **Screenshots** / detail grid).
2. Scroll the detail grid **down** so you are not at the top.
3. Prefer a folder where some files still trigger **embedded-metadata repair** (e.g. index stamp vs. file mismatch) so the UI does a **quick** render then a possible **refined** regroup; if unsure, try folders you recently added or rescanned.
4. After the quick list appears, **scroll again** if needed; wait for any background metadata work to finish.
5. **Pass:** the grid does **not** jump back to the previously saved offset on a later rerender while you are interacting; only the **first** snapshot after a deliberate refresh should restore a preserved offset when that feature applies.

### Import-and-edit — Steam store title on finish

1. Put at least one **Steam**-tagged capture in **Import and Edit** (or equivalent manual metadata finish path) with an AppID, leaving the **game title** unchanged from the loaded hint so the app resolves the **store title**.
2. Complete **Finish** (or the action that runs **`ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** then finalize).
3. **Pass:** titles update without binding or cross-thread warnings in the debug output; UI stays responsive and the grid shows the resolved names.

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
