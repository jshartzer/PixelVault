# PV-PLN-PERF-001 - App speed and efficiency pass

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-PERF-001` |
| **Status** | Complete (2026-05-07; isolated WPF manual smoke passed, RC follow-ups noted) |
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
   - Closed Phase E with final automated and manual guardrails: large import log batching now proves 100-line chunk flushes, `TroubleshootingLog.AppendMainLines` is covered, and `docs/MANUAL_GOLDEN_PATH_CHECKLIST.md` records progress-window cancel/log-completeness checks for 100-file and 500-file imports.

### Phase F - Detail pane cancellation and viewport-first metadata

20. Add a per-detail-render cancellation token tied to `LibraryDetailRenderVersion` or a dedicated CTS.
   - Added `LibraryDetailRenderCancellationController`, owned by each library browser working set. Every detail render now cancels the prior render token, background detail work checks the active token at stage boundaries, deferred metadata repair observes the same render token, and controller tests prove previous/current tokens are canceled as expected.
21. Replace `CancellationToken.None` in selection-dependent detail metadata reads with that token.
   - The initial detail metadata refresh, immediate missing-capture repair read, and deferred repair chunk reads now pass the active detail-render token into `ReadEmbeddedMetadataBatchAsync`. Stale selection changes can now interrupt the metadata process itself instead of only skipping the eventual UI apply.
22. Read metadata first for visible/overscan files; defer the rest in small cancelable chunks.
   - Virtualized detail rows now carry their file membership, and detail metadata builds a viewport-first file order from the active scroll offset plus overscan. Initial embedded metadata reads target that primary set first; non-primary files are processed afterward in 36-file cancelable chunks. Missing-capture repair reads use the same viewport-first ordering before deferring the rest.
23. Ensure stale detail renders do not update UI after selection changes.
   - Added a shared `LibraryDetailRenderGuard` so detail UI apply paths use one rule: active render token, active render version, and matching selection. Deferred metadata repair now carries the originating render version through scheduling/core execution and re-checks the guard before chunk work, final completion, and the idle redraw callback.

### Phase G - Scroll/render polish

24. Tune virtualized row refresh to use render-priority coalescing where it improves perceived scroll response.
   - Replaced the ordinary scroll debounce timer with a one-per-render-pass `DispatcherPriority.Render` refresh queue. Page-sized scroll jumps still refresh immediately, viewport/resize changes keep the slower resize debounce, and queued stale scroll refreshes are invalidated when rows are reset or a resize/immediate refresh takes over.
25. Preserve row recycling and existing image decode prioritization.
   - Added guardrail helpers/tests for row recycling and decode priority. Virtual row element cache pruning now preserves the inclusive visible row range, and detail image decode priority remains tied to row/viewport intersection so visible rows keep using the priority image-load lane.
26. Re-run large-folder scroll checks with troubleshooting logging on.
   - Added troubleshooting-only scroll diagnostics for virtualized hosts (`VirtualizedRowHostScroll` and `VirtualizedRowHostRowsRebuilt`) and labeled folder/detail hosts in logs. The manual golden path now includes a repeatable large-folder scroll pass that captures render-coalesced scroll refreshes, immediate page jumps, row rebuild ranges, and stale detail render skip/apply behavior.

### Phase H - Optional cleanup and final verification

27. Review remote asset picker previews for UI-thread image decode/download jank.
   - Reworked SteamGridDB asset picker thumbnails/previews so remote images download through `TimeoutWebClient.DownloadBytesAsync` off the UI thread, decode from memory with `BitmapCacheOption.OnLoad`, freeze before UI apply, and ignore stale image requests via per-image tokens. Picker previews now prefer SteamGridDB preview URLs for browsing and only download the full selected asset when the user confirms.
28. Review remaining synchronous file/log operations that can run during active UI flows.
   - Reviewed active UI file/log hotspots and moved custom cover/banner/logo save operations for SteamGridDB picker selections plus manual right-click art menus behind background task wrappers. Picker temp-file cleanup also runs off the dispatcher; file dialogs remain UI-modal by design, and the remaining clear/delete paths are small, explicit user actions.
29. Run unit tests and manual golden path.
   - Automated verification passed on 2026-05-07: `dotnet test C:\Codex\tests\PixelVault.Native.Tests\PixelVault.Native.Tests.csproj --no-restore` (549/549), `dotnet build C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj --no-restore`, and `git diff --check` (CRLF normalization warnings only). The isolated WPF manual smoke also passed for app launch, 25/100/500-file foreground imports, large-folder detail open, fast detail scroll, and selection switch diagnostics; see `docs/perf/PV-PLN-PERF-001-step-29-manual-run.md` and `docs/perf/PV-PLN-PERF-001-step-29-manual-results.md`. HDR PNG/JXR and SteamGridDB/custom-art live checks remain RC follow-ups because the isolated sandbox did not include a valid throwaway JXR writer or SteamGridDB token.
30. Update `docs/CHANGELOG.md`, `docs/HANDOFF.md`, and this plan's revision history when slices ship.
   - Final doc sync completed on 2026-05-07: `docs/CHANGELOG.md` records the shipped PV-PLN-PERF-001 speed/reliability changes, `docs/HANDOFF.md` points to the completed status and manual evidence, this plan's revision history is current, and `docs/perf/PV-PLN-PERF-001-step-29-manual-results.md` captures the isolated WPF close-out. The manual smoke also noted a residual synthetic-title grouping artifact (`Perf Game 100` / `Perf Game 500`) for future review if numbered real titles ever appear to merge too aggressively.

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
| Cancellation changes drop legitimate updates. | Gate UI application by active render token, render version, and `SameLibraryBrowserSelection`; deferred repair redraws carry the original render version. |

## Doc sync

When execution starts or completes a slice, reference **`PV-PLN-PERF-001`** in commits and update the appropriate docs per `docs/DOC_SYNC_POLICY.md`. At minimum, update:

- `docs/CHANGELOG.md` for shipped user-visible speed/reliability changes.
- `docs/HANDOFF.md` for current status and next slice.
- This plan's revision history.

## Revision history

| Date | Change |
|------|--------|
| 2026-05-07 | Marked PV-PLN-PERF-001 complete after isolated WPF manual smoke passed for launch, 25/100/500-file imports, large-folder detail render, fast detail scroll, and selection-switch diagnostics; HDR/JXR and SteamGridDB/custom-art live checks remain RC follow-ups. |
| 2026-05-07 | Completed Phase H step 30 doc sync across changelog, handoff, plan status, and revision history. |
| 2026-05-07 | Added the Phase H step 29 manual run sheet for live WPF import, large-folder, scroll, SteamGridDB picker, and custom-art save checks. |
| 2026-05-07 | Completed the automated verification portion of Phase H step 29 and exported the latest performance-baseline artifact; live manual golden-path checks remain pending. |
| 2026-05-07 | Completed Phase H step 28 by moving custom art save/copy and picker temp-file cleanup work off the UI thread for SteamGridDB picker and right-click art menu flows. |
| 2026-05-06 | Completed Phase H step 27 by moving SteamGridDB picker preview image download/decode off the UI thread and adding URL/cache-key guardrail tests. |
| 2026-05-06 | Completed Phase G step 26 by adding troubleshooting scroll diagnostics and documenting the large-folder scroll pass/log markers. |
| 2026-05-06 | Completed Phase G step 25 by adding guardrail coverage for virtual row recycling and visible-row image decode prioritization. |
| 2026-05-06 | Completed Phase G step 24 by switching ordinary virtualized row scroll refreshes from timer debounce to render-priority coalescing while keeping page-sized jumps immediate. |
| 2026-05-06 | Completed Phase F step 23 by centralizing stale detail-render apply checks and carrying render-version guardrails through deferred metadata repair redraws; Phase F is complete. |
| 2026-05-05 | Completed Phase F step 22 by prioritizing visible/overscan detail metadata reads and deferring the rest in cancelable chunks. |
| 2026-05-05 | Completed Phase F step 21 by replacing detail-pane metadata `CancellationToken.None` calls with the active render token. |
| 2026-05-05 | Completed Phase F step 20 by adding per-detail-render cancellation ownership and guardrail tests; step 21 will thread the token into embedded metadata reads. |
| 2026-05-05 | Completed Phase E step 19 with final coalescer/logging guardrails and manual large-import checklist updates; Phase E is complete. |
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
