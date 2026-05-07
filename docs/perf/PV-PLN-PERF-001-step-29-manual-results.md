# PV-PLN-PERF-001 baseline capture

| Field | Value |
|-------|-------|
| Plan | PV-PLN-PERF-001 |
| Generated | 2026-05-07 08:31:38 -05:00 |
| Source log | `C:\Codex\.tmp\pv-perf-step29\PixelVaultData\logs\PixelVault-native.log` |
| Rows parsed | 35 |
| Captured range | 2026-05-07 13:21:35 UTC to 2026-05-07 13:30:23 UTC |
| Since filter | 2026-05-07 13:20:26 +00:00 |

## Summary

| Area | Count | Min | Median | P95 | Max | Latest |
|------|------:|----:|-------:|----:|----:|--------|
| ImportPreparation | 2 | 42 ms | 42 ms | 78 ms | 78 ms | 2026-05-07 13:22:47 UTC |
| ImportWorkflowRun | 3 | 1493 ms | 2011 ms | 7239 ms | 7239 ms | 2026-05-07 13:22:54 UTC |
| ImportWorkflowStep | 18 | 0 ms | 10 ms | 4527 ms | 4527 ms | 2026-05-07 13:22:54 UTC |
| LibraryBrowserFirstDetailPaint | 1 | 260070 ms | 260070 ms | 260070 ms | 260070 ms | 2026-05-07 13:24:47 UTC |
| LibraryBrowserFirstFolderListPaint | 1 | 183345 ms | 183345 ms | 183345 ms | 183345 ms | 2026-05-07 13:23:30 UTC |
| LibraryDetailRender | 4 | 48 ms | 200 ms | 290 ms | 290 ms | 2026-05-07 13:30:23 UTC |
| LibraryFolderCache | 3 | 43 ms | 46 ms | 224 ms | 224 ms | 2026-05-07 13:22:54 UTC |
| LibraryFolderMetadataRepair | 2 | 330 ms | 330 ms | 717 ms | 717 ms | 2026-05-07 13:22:14 UTC |
| LibraryFolderRender | 1 | 136 ms | 136 ms | 136 ms | 136 ms | 2026-05-07 13:23:30 UTC |

## Manual smoke result

This run used an isolated WPF sandbox rooted at `C:\Codex\.tmp\pv-perf-step29` with source, destination, library, export, logs, and settings all redirected away from the user's live folders via `PIXELVAULT_DATA_ROOT`. The scratch sandbox was removed after this summary was exported so the repo was not left with hundreds of generated test captures.

| Flow | Result | Evidence |
|------|--------|----------|
| App open -> library usable | Pass | `PixelVault 0.076.000` launched from the current Debug build and became responsive. A clean rebuild was required first because the prior stale build output was missing compiled WPF theme BAML resources. |
| Import 25 top-level files | Pass | Foreground Import moved 25/25 generated PS5-pattern PNG files into the isolated library; progress summary reported `25 file(s) imported | 25 metadata update(s)`. |
| Import 100 top-level files | Pass | Foreground Import moved 100/100 generated PS5-pattern PNG files; final sandbox library count reached 125 PNG files. |
| Import 500 top-level files | Pass | Foreground Import moved 500/500 generated PS5-pattern PNG files; `ImportWorkflowRun` completed in 7,239 ms and final sandbox library count reached 625 PNG files. |
| Large-folder photo/detail view | Pass | Opening the large synthetic folder produced `LibraryDetailRender` rows of 290 ms, 248 ms, and 200 ms with `quickMediaMapMs` at 14 ms, then 2 ms on rerenders. |
| Fast-scroll detail pane | Pass | Troubleshooting log captured `VirtualizedRowHostScroll` for `DetailRows` with `mode=render-coalesced`, followed by a folder switch with a clean `LibrarySelection` and `LibraryDetailRenderApplied` for the new folder. |
| HDR PNG/JXR live pair | Not run live in this sandbox | Covered by automated HDR filtering/duplicate guardrails; no valid throwaway JXR writer was available for a faithful live import without using real captures. |
| SteamGridDB picker / custom art save | Not run live in this sandbox | Covered by picker preview/save tests and file-copy off-dispatcher code review; no SteamGridDB token was configured in the isolated sandbox. |

Residual note: the intentionally synthetic titles `Perf Game 100` and `Perf Game 500` were grouped under one saved game-index identity in the Library UI even though their files landed in separate physical folders. That did not block this performance gate, but it is worth revisiting if numbered test/sequel titles appear to merge too aggressively in real data.

## Slowest library samples

| Area | Time | Duration | Detail |
|------|------|---------:|--------|
| LibraryBrowserFirstDetailPaint | 2026-05-07 13:24:47 UTC | 260070 ms | S=9f8fc133 / folder=Perf Game 500; files=625; groups=1 |
| LibraryBrowserFirstFolderListPaint | 2026-05-07 13:23:30 UTC | 183345 ms | S=9f8fc133 / mode=flat; visible=2; rows=1 |
| LibraryDetailRender | 2026-05-07 13:24:47 UTC | 290 ms | S=9f8fc133 / folder=Perf Game 500; groups=1; files=625; rows=14; columns=1; size=240; uiApplyMs=68; quickPrepMs=28; quickMediaMapMs=14; quickTailMs=55; quickMediaReused=False |
| LibraryDetailRender | 2026-05-07 13:28:21 UTC | 248 ms | S=9f8fc133 / folder=Perf Game 500; groups=1; files=625; rows=14; columns=1; size=240; uiApplyMs=85; quickPrepMs=30; quickMediaMapMs=2; quickTailMs=55; quickMediaReused=False |
| LibraryFolderCache | 2026-05-07 13:22:54 UTC | 224 ms | S=9f8fc133 / mode=incremental; touched=500; gameIds=1; orphanDirs=0; folders=2 |
| LibraryDetailRender | 2026-05-07 13:28:21 UTC | 200 ms | S=9f8fc133 / folder=Perf Game 500; groups=1; files=625; rows=45; columns=4; size=560; uiApplyMs=38; quickPrepMs=27; quickMediaMapMs=2; quickTailMs=58; quickMediaReused=False |
| LibraryFolderRender | 2026-05-07 13:23:30 UTC | 136 ms | S=9f8fc133 / mode=flat; foldersLoaded=2; views=2; visible=2; rows=1; columns=6; grouping=all; search=(none); sort=alpha; projectMs=114; filterMs=0 |
| LibraryDetailRender | 2026-05-07 13:30:23 UTC | 48 ms | S=9f8fc133 / folder=Needs assignment · Perf Game 25; groups=1; files=25; rows=2; columns=4; size=560; uiApplyMs=17; quickPrepMs=1; quickMediaMapMs=0; quickTailMs=2; quickMediaRe... |

## Slowest import/intake samples

| Area | Time | Duration | Detail |
|------|------|---------:|--------|
| ImportWorkflowRun | 2026-05-07 13:22:54 UTC | 7239 ms | S=9f8fc133 / workflow=import; mode=standard; totalWork=1501; importCandidates=500; renameScope=500; hdrPairs=0; renamed=0; deleted=0; metadataUpdated=500; moved=500; sorted=500;... |
| ImportWorkflowStep | 2026-05-07 13:22:52 UTC | 4527 ms | S=9f8fc133 / workflow=import; step=metadata; items=500; updated=500; skipped=0; failures=0 |
| ImportWorkflowStep | 2026-05-07 13:22:54 UTC | 2504 ms | S=9f8fc133 / workflow=import; step=sort; items=500; sorted=500; foldersCreated=1; renamedOnConflict=0 |
| ImportWorkflowRun | 2026-05-07 13:22:13 UTC | 2011 ms | S=9f8fc133 / workflow=import; mode=standard; totalWork=301; importCandidates=100; renameScope=100; hdrPairs=0; renamed=0; deleted=0; metadataUpdated=100; moved=100; sorted=100; ... |
| ImportWorkflowRun | 2026-05-07 13:21:37 UTC | 1493 ms | S=9f8fc133 / workflow=import; mode=standard; totalWork=76; importCandidates=25; renameScope=25; hdrPairs=0; renamed=0; deleted=0; metadataUpdated=25; moved=25; sorted=25; hdrMov... |
| ImportWorkflowStep | 2026-05-07 13:22:13 UTC | 1249 ms | S=9f8fc133 / workflow=import; step=metadata; items=100; updated=100; skipped=0; failures=0 |
| ImportWorkflowStep | 2026-05-07 13:21:36 UTC | 1077 ms | S=9f8fc133 / workflow=import; step=metadata; items=25; updated=25; skipped=0; failures=0 |
| ImportWorkflowStep | 2026-05-07 13:22:13 UTC | 717 ms | S=9f8fc133 / workflow=import; step=sort; items=100; sorted=100; foldersCreated=1; renamedOnConflict=0 |

## Manual capture matrix

| Flow | Current baseline status | Notes |
|------|-------------------------|-------|
| App open -> library visible | Captured by `LibraryBrowserFirstFolderListPaint` and `LibraryBrowserFirstDetailPaint`. | Re-run after each slice from a cold app start. |
| Import 25 files | Passed in isolated WPF sandbox. | `ImportWorkflowRun` completed with 25 imported / 25 metadata updates. |
| Import 100 files | Passed in isolated WPF sandbox. | `ImportWorkflowRun` completed with 100 imported / 100 metadata updates. |
| Import 500 files | Passed in isolated WPF sandbox. | `ImportWorkflowRun` completed with 500 imported / 500 metadata updates in 7,239 ms. |
| Import HDR PNG/JXR pairs | Not live-smoked in this sandbox. | Automated HDR pair filtering/duplicate tests remain the guardrail; use real throwaway HDR captures before an RC. |
| Open a large game folder in photo view | Passed in isolated WPF sandbox. | Synthetic 625-item detail view rendered initially in 290 ms. |
| Fast-scroll detail pane | Passed in isolated WPF sandbox. | Troubleshooting log captured render-coalesced detail scroll events and clean selection switch behavior. |

## Reading guidance

- Treat very large `LibraryBrowserFirstDetailPaint` values as suspect when the app sat open before a first detail render; use fresh cold-start samples for comparison.
- `quickMediaMapMs` spikes inside `LibraryDetailRender` are the most useful photo-view clue from the current logs.
- Builds with Phase A step 2 instrumentation also emit `ImportWorkflowRun` and `ImportWorkflowStep` rows for clean 25/100/500-file import comparisons.
- Keep this file as a before/after reference, not as a product-facing performance claim.
