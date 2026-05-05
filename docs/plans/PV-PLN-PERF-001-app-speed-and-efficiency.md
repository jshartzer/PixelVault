# PV-PLN-PERF-001 - App speed and efficiency pass

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-PERF-001` |
| **Status** | Active (Phase E step 18 complete; step 19 next) |
| **Owner** | PixelVault / Codex |
| **Source brief** | Codex full-app performance review, 2026-05-03 |
| **Parent roadmap** | `docs/ROADMAP.md` - performance, polish, reliability |
| **Related** | `docs/LIBRARY_PERFORMANCE_PLAN.md`, `docs/PERFORMANCE_TODO.md`, `docs/PERFORMANCE_MONOLITH_SLICE_PLAN.md`, `PV-PLN-UI-001`, `PV-PLN-EXT-002`, `PV-PLN-V1POL-001` |

## Purpose

Make PixelVault feel faster in the places users touch most: import, post-import refresh, library open, folder selection, photo browsing, and progress feedback.

This plan does **not** replace the existing thumbnail cache, virtualized rows, ExifTool batching, folder-cache snapshots, or service extraction work. Those are good foundations. The goal here is to remove repeated full-library work, make background repairs truly background, and coalesce noisy UI/file IO so the app feels snappier without changing the user's workflows.

## Baseline review findings

| Priority | Finding | Current touch points | Desired direction |
|----------|---------|----------------------|-------------------|
| **P1** | Small imports / metadata edits can trigger whole-library folder-cache rebuilds. | `ImportService.SortDestinationRootIntoGameFolders`, `LibraryScanner.UpsertLibraryMetadataIndexEntries` | Incremental touched-folder cache updates or one deferred/coalesced rebuild per workflow. |
| **P1** | Folder-cache rebuilds can recursively enumerate the library and call ExifTool for missing/incomplete metadata. | `LibraryScanner.LoadLibraryFoldersCore`, `LoadLibraryFoldersCached` | Fast index-backed projection first; background metadata repair queue second. |
| **P2** | Import prep can analyze the same files more than once. | `ImportWorkflow.RunWorkflow`, `MainWindow.HeadlessImport`, `MainWindow.IntakePreview` | One `SourceInventory` and one `IntakePreviewFileAnalysis` pass per workflow, reused by review/manual/import-edit rows. |
| **P2** | Import progress and logs are too chatty for larger batches. | `RunBackgroundWorkflowWithProgress`, `WorkflowProgressView.AppendLogLine`, per-file import logs | Throttle dispatcher updates and batch log writes while keeping useful detail. |
| **P2** | Detail-pane follow-up metadata work is not consistently tied to current selection cancellation. | `MainWindow.LibraryBrowserRender.DetailPane` | Per-selection CTS; only visible/near-visible metadata first; deferred repair cancelable by generation/token. |
| **P3** | Scroll virtualization is good, but scroll refresh can feel slightly delayed. | `LibraryVirtualization` | Coalesce on render priority, tune debounce, preserve row recycling. |

## Named policies

Use these stable IDs in commits, tests, and follow-up notes.

| Policy ID | Name | Statement |
|-----------|------|-----------|
| **PV-POL-PERF-MEASURE-001** | Measure before polish | Each slice should add or reuse targeted timing logs before claiming a speedup. Prefer existing `LogPerformanceSample` categories when possible. |
| **PV-POL-PERF-INCR-001** | Incremental by default | File-level operations must not rebuild the full library unless a structural change requires it. Prefer touched-folder updates or deferred/coalesced rebuild. |
| **PV-POL-PERF-BG-001** | Repair off the hot path | Metadata repair and re-resolution should not block first usable library paint or folder selection. Show stale-but-usable data, then repair in the background. |
| **PV-POL-PERF-UI-001** | Coalesce UI updates | Progress/log/status updates from batch work should be throttled or batched so the dispatcher is not updated once per file. |
| **PV-POL-PERF-CANCEL-001** | Cancel stale work | Selection-dependent image, cover, and metadata work must be cancelable or generation-gated so old work cannot consume resources after the user moves on. |

## Execution roadmap

### Phase A - Baseline and guardrails

1. Capture current timing logs for representative flows:
   - App open -> library visible.
   - Import 25, 100, and 500 files.
   - Import with HDR PNG/JXR pairs.
   - Open a large game folder in photo view.
   - Fast-scroll detail pane.
   - Baseline artifact: `docs/perf/PV-PLN-PERF-001-baseline-2026-05-03.md`.
2. Add missing `LogPerformanceSample` points only where needed.
   - Added `ImportWorkflowRun` and `ImportWorkflowStep` timing rows for standard import, import-and-comment, manual intake, HDR fallback parking, move, metadata, rename, and post-move sort.
   - Updated `scripts/Export-PixelVaultPerformanceBaseline.ps1` so future baseline exports include those import workflow rows.
3. Define manual pass/fail targets in `docs/MANUAL_GOLDEN_PATH_CHECKLIST.md` if the flow is user-visible and hard to unit test.
   - Added `PV-PLN-PERF-001 — performance spot checks` with setup, required flows, exported-log command, and pass/fail rules.

### Phase B - Single-pass import preparation

4. Refactor `ImportWorkflow.RunWorkflow` to build one `SourceInventory` and one intake analysis dictionary per run.
   - Foreground `RunWorkflow` now builds one `SourceInventory` and one `IntakePreviewFileAnalysis` dictionary, then reuses that for review, manual, and import-and-edit rows.
5. Reuse that analysis for `BuildReviewItems`, `BuildManualMetadataItems`, and `BuildImportAndEditMetadataItems`.
   - Manual Intake prep now uses the same shared-analysis pattern so it does not analyze once for recognized paths and again for manual rows.
6. Remove the duplicate inventory build in headless import.
   - Headless standard import now builds one `SourceInventory` and reuses it for rename/delete/move scope, matching the previous behavior without a second source-folder pass.
7. Add tests proving analysis is reused and HDR pair filtering still works.
   - Added `IntakePreparationBuilder` as the shared prep seam for intake preview, foreground import, manual intake, and headless import so review/manual/import-edit rows are produced from one analysis dictionary.
   - Added `IntakePreparationBuilderTests` to prove review/manual/import-edit prep calls the analyzer once, and tightened HDR inventory coverage so PNG alternates stay out of both import and rename scopes.

### Phase C - Incremental folder-cache updates after import/edit

8. Add scanner/session APIs that accept a touched file/folder set and can update folder-cache rows without rebuilding the full cache.
   - Added `TryUpdateLibraryFolderCacheForTouchedPaths` to the scanner/session seam. It can read the previous cache snapshot even after a metadata-index revision change, reloads only affected cached game/orphan rows from the current metadata index, preserves full refresh for force scans, logs `LibraryFolderCache mode=incremental`, and has regression coverage proving a touched game row updates without invoking the full rebuild hook.
9. Change `UpsertLibraryMetadataIndexEntries` call sites so import/edit flows do one of:
   - update touched game folders directly, or
   - mark folder cache dirty and schedule one coalesced rebuild after the workflow.
   - Centralized this in `LibraryScanner`: file upserts, manual metadata upserts, metadata removals, and photo-index saves now call the touched-path updater first and fall back to a full rebuild only when no usable cache snapshot/touched scope exists. Import sort benefits through its existing `UpsertLibraryMetadataIndexEntries` call.
10. Keep explicit full refresh available for manual refresh / force scan.
   - Added guardrail coverage that `RefreshFolderCacheAfterGameIndexChange` still calls the explicit full rebuild path, and `LoadLibraryFoldersCached(forceRefresh: true)` ignores stale cache snapshots and rebuilds from the current disk/index projection.
11. Add tests for import sort -> folder cache reflects new file without a full-library scan.
   - Added an end-to-end import-sort regression that moves a root-level capture into its game folder, then verifies the cached folder row reflects the new file while the scanner full-rebuild hook is not invoked.

### Phase D - Fast folder projection, background metadata repair

12. Split `LoadLibraryFoldersCore` into:
   - fast projection from metadata index / cache rows,
   - separate repair candidate discovery,
   - background repair execution.
   - Split folder loading into fast index projection, repair-candidate discovery, and queued background metadata repair. Cache rebuilds now save/display indexed folder rows first; repair reloads the latest index in the background and refreshes touched cache rows when entries change.
13. Ensure cache miss/library open paints usable folder rows before ExifTool repair.
   - Added cache-miss/library-open guardrail coverage for `LoadLibraryFoldersCached(forceRefresh: false)`: with no cache snapshot and missing capture ticks, usable indexed rows are returned and saved before blocked metadata repair completes, then the background repair updates the same touched cache row.
14. Keep the existing index-only refresh path, but make its intent explicit in code and tests.
   - Renamed/logged the path as an index-only projection refresh: it reuses persisted metadata-index paths when only child-folder mtimes changed and intentionally avoids a recursive folder sweep. Added scanner coverage proving the supplied indexed file list controls the projection.
15. Add tests around missing capture ticks/orphan Game IDs to prove they repair without blocking first folder list render.
   - Added orphan-GameId fast-paint guardrail coverage alongside the missing-capture-tick tests. Orphaned indexed rows now surface as pending-assignment folders before metadata repair completes, then the background repair resolves them back to saved game rows and refreshes the touched cache row.

### Phase E - Progress, logging, and dispatcher coalescing

16. Introduce a progress coalescer for workflow windows:
   - update progress UI at most every 75-150ms during high-volume work,
   - always flush final success/failure/cancel messages,
   - keep the most recent detail line.
   - Added `WorkflowProgressCoalescer` and routed import workflow progress through it. Progress UI applies are throttled to 100ms, rapid updates keep the newest detail, and pending progress flushes before cancel/success/failure text.
17. Batch progress log text updates instead of replacing the whole log on every file.
   - Added a shared `WorkflowProgressLogBuffer` and changed `WorkflowProgressView.AppendLogLine` to queue one dispatcher-background flush for bursts of appended lines. The log still keeps the existing max-line trimming behavior, while ordinary appends avoid rebuilding `TextBox.Text` immediately on every progress event.
18. Batch disk log appends for high-volume import steps or make per-file logging troubleshooting-only.
   - Added batched main-log append support (`ILogService.AppendMainLines` / `TroubleshootingLog.AppendMainLines`) and routed routine per-file import detail logs through disposable 100-line batches. Move, HDR duplicate parking, sort, delete, rename, manual rename, and metadata prep now keep summaries/errors immediate while reducing disk opens for bulk detail lines.
19. Add tests for coalescer behavior where practical; otherwise add manual checklist rows for large imports.

### Phase F - Detail pane cancellation and viewport-first metadata

20. Add a per-detail-render cancellation token tied to `LibraryDetailRenderVersion` or a dedicated CTS.
21. Replace `CancellationToken.None` in selection-dependent detail metadata reads with that token.
22. Read metadata first for visible/overscan files; defer the rest in small cancelable chunks.
23. Ensure stale detail renders do not update UI after selection changes.

### Phase G - Scroll/render polish

24. Tune virtualized row refresh to use render-priority coalescing where it improves perceived scroll response.
25. Preserve row recycling and existing image decode prioritization.
26. Re-run large-folder scroll checks with troubleshooting logging on.

### Phase H - Optional cleanup and final verification

27. Review remote asset picker previews for UI-thread image decode/download jank.
28. Review remaining synchronous file/log operations that can run during active UI flows.
29. Run unit tests and manual golden path.
30. Update `docs/CHANGELOG.md`, `docs/HANDOFF.md`, and this plan's revision history when slices ship.

## Acceptance criteria

- Import prep no longer repeats source inventory or intake analysis in the standard and import-and-edit flows.
- Importing or editing a small set of files no longer requires a full recursive library folder scan unless forced.
- Library open and folder selection can show cached/indexed rows before metadata repair completes.
- Progress windows remain responsive during large imports, with reduced dispatcher churn.
- Detail-pane background metadata work stops quickly when the user changes selection.
- Existing behavior remains intact: HDR duplicates, top-level-only import default, optional subfolder scan, game index assignment, console tagging, photo view, timeline grouping, and manual metadata edit flows.

## Test and verification checklist

| Area | Verification |
|------|--------------|
| Unit tests | `dotnet test` for `tests/PixelVault.Native.Tests` after each slice with behavior changes. |
| Import | Standard import, import-and-edit, manual intake, HDR PNG/JXR pair, source top-level-only scan, optional subfolder scan. |
| Library cache | Fresh library open, cache hit, cache miss, force refresh, metadata index scan, post-import refresh. |
| UI | Large folder open, photo view platform filters, detail scroll, folder scroll, progress window during 500-file import. |
| Regression | Deleted games not reintroduced in metadata dropdowns; PC/Switch/Xbox/Steam tagging remains stable; HDR duplicates ignored by library scan. |

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Incremental cache update misses an edge case and shows stale folder counts. | Keep force refresh, add tests for touched folders, and fall back to coalesced full rebuild when structural ambiguity is detected. |
| Background repair causes confusing delayed changes. | Use existing status/toast/log patterns sparingly; prefer silent repair unless user initiated scan. |
| Progress coalescing hides useful diagnostics. | Keep final counts and troubleshooting logs; optionally expose verbose mode when troubleshooting logging is enabled. |
| Cancellation changes drop legitimate updates. | Generation-gate UI application and flush final selected render only when `SameLibraryBrowserSelection` still matches. |

## Doc sync

When execution starts or completes a slice, reference **`PV-PLN-PERF-001`** in commits and update the appropriate docs per `docs/DOC_SYNC_POLICY.md`. At minimum, update:

- `docs/CHANGELOG.md` for shipped user-visible speed/reliability changes.
- `docs/HANDOFF.md` for current status and next slice.
- This plan's revision history.

## Revision history

| Date | Change |
|------|--------|
| 2026-05-05 | Completed Phase E step 18 by batching high-volume import detail log writes while preserving immediate summaries and errors. |
| 2026-05-04 | Completed Phase E step 17 by batching shared progress-window log text rendering and adding buffer tests. |
| 2026-05-04 | Completed Phase E step 16 by adding a workflow progress coalescer with deterministic tests and routing import workflow progress through it. |
| 2026-05-04 | Completed Phase D step 15 with orphan-GameId fast-paint and background-repair guardrail coverage; Phase D is complete. |
| 2026-05-04 | Completed Phase D step 14 by clarifying and testing the index-only projection refresh path. |
| 2026-05-04 | Completed Phase D step 13 with cache-miss/library-open fast-paint guardrail coverage. |
| 2026-05-04 | Completed Phase D step 12 by splitting folder projection from queued metadata repair and adding fast-projection guardrail coverage. |
| 2026-05-04 | Completed Phase C step 11 with import-sort folder-cache guardrail coverage; Phase C is complete. |
| 2026-05-04 | Completed Phase C step 10 by locking explicit refresh / force-refresh paths to full rebuild behavior with guardrail tests. |
| 2026-05-03 | Completed Phase C step 9 by routing import/edit metadata changes through incremental touched-path cache refresh with full-rebuild fallback. |
| 2026-05-03 | Completed Phase C step 8 by adding the touched-path folder-cache update API and guardrail test. |
| 2026-05-03 | Completed Phase B with shared intake prep guardrail tests and HDR pair filtering coverage. |
| 2026-05-03 | Removed the duplicate headless source inventory build for Phase B step 6. |
| 2026-05-03 | Started Phase B by reusing a single intake analysis pass in foreground import and manual-intake preparation. |
| 2026-05-03 | Added manual performance pass/fail targets to the golden path checklist for Phase A step 3. |
| 2026-05-03 | Added missing import workflow timing instrumentation and exporter coverage for Phase A step 2. |
| 2026-05-03 | Started Phase A and captured a reusable performance baseline from current native logs. |
| 2026-05-03 | Initial codified plan from full-app speed/efficiency review. |
