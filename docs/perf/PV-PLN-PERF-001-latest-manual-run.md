# PV-PLN-PERF-001 baseline capture

| Field | Value |
|-------|-------|
| Plan | PV-PLN-PERF-001 |
| Generated | 2026-05-07 07:19:24 -05:00 |
| Source log | `C:\Codex\PixelVaultData\logs\PixelVault-native.log` |
| Rows parsed | 611 |
| Captured range | 2026-05-01 23:15:29 UTC to 2026-05-07 04:22:47 UTC |
| Since filter | 2026-05-01 00:00:00 +00:00 |

## Summary

| Area | Count | Min | Median | P95 | Max | Latest |
|------|------:|----:|-------:|----:|----:|--------|
| ImportWorkflowRun | 6 | 1915 ms | 2810 ms | 4337 ms | 4337 ms | 2026-05-07 04:21:59 UTC |
| ImportWorkflowStep | 37 | 0 ms | 8 ms | 2264 ms | 3233 ms | 2026-05-07 04:21:59 UTC |
| IntakePreviewBuild | 14 | 76 ms | 142 ms | 560 ms | 560 ms | 2026-05-07 04:21:52 UTC |
| LibraryBrowserFirstDetailPaint | 18 | 449 ms | 21337 ms | 117766 ms | 117766 ms | 2026-05-07 04:22:11 UTC |
| LibraryBrowserFirstFolderListPaint | 22 | 169 ms | 4816 ms | 6114 ms | 6234 ms | 2026-05-07 04:21:51 UTC |
| LibraryDetailRender | 180 | 57 ms | 187 ms | 4305 ms | 43948 ms | 2026-05-07 04:22:44 UTC |
| LibraryFolderCache | 70 | 141 ms | 188 ms | 987 ms | 1532 ms | 2026-05-07 04:22:18 UTC |
| LibraryFolderRender | 207 | 40 ms | 194 ms | 4738 ms | 8338 ms | 2026-05-07 04:22:42 UTC |
| ManualIntakePreparation | 5 | 380 ms | 528 ms | 614 ms | 614 ms | 2026-05-01 23:27:12 UTC |
| RetroAchievementsSearch | 2 | 408 ms | 408 ms | 611 ms | 611 ms | 2026-05-06 03:55:03 UTC |
| SteamAppDetails | 4 | 216 ms | 221 ms | 295 ms | 295 ms | 2026-05-07 04:21:56 UTC |
| SteamCoverDownload | 2 | 2356 ms | 2356 ms | 2528 ms | 2528 ms | 2026-05-07 04:22:14 UTC |
| SteamGridDbCoverDownload | 1 | 610 ms | 610 ms | 610 ms | 610 ms | 2026-05-01 23:30:27 UTC |
| SteamGridDbHeroDownload | 9 | 233 ms | 524 ms | 657 ms | 657 ms | 2026-05-06 04:06:29 UTC |
| SteamGridDbIdByAppId | 2 | 216 ms | 216 ms | 239 ms | 239 ms | 2026-05-07 04:22:11 UTC |
| SteamGridDbLogoDownload | 15 | 277 ms | 527 ms | 631 ms | 631 ms | 2026-05-07 04:22:47 UTC |
| SteamGridDbSearch | 9 | 188 ms | 206 ms | 236 ms | 236 ms | 2026-05-07 04:22:44 UTC |
| SteamSearch | 7 | 191 ms | 199 ms | 396 ms | 396 ms | 2026-05-06 04:03:50 UTC |
| SteamStoreHeaderHeroDownload | 1 | 2333 ms | 2333 ms | 2333 ms | 2333 ms | 2026-05-07 04:22:47 UTC |

## Slowest library samples

| Area | Time | Duration | Detail |
|------|------|---------:|--------|
| LibraryBrowserFirstDetailPaint | 2026-05-01 23:41:44 UTC | 117766 ms | S=3bb2c594 / folder=Donkey Kong Bananza; files=5; groups=2 |
| LibraryBrowserFirstDetailPaint | 2026-05-03 00:19:01 UTC | 79078 ms | S=6970a856 / folder=Eternal Darkness Sanity's Requiem; files=80; groups=6 |
| LibraryBrowserFirstDetailPaint | 2026-05-06 03:55:09 UTC | 75382 ms | S=835de170 / folder=Vampire Crawlers; files=5; groups=1 |
| LibraryBrowserFirstDetailPaint | 2026-05-03 02:02:49 UTC | 72502 ms | S=1f1ed500 / folder=Diablo® IV; files=2; groups=1 |
| LibraryBrowserFirstDetailPaint | 2026-05-03 03:57:18 UTC | 59795 ms | S=0623a698 / folder=Diablo IV; files=376; groups=114 |
| LibraryBrowserFirstDetailPaint | 2026-05-05 04:32:03 UTC | 54209 ms | S=fe47ebb4 / folder=Donkey Kong Bananza; files=5; groups=2 |
| LibraryDetailRender | 2026-05-03 03:57:18 UTC | 43948 ms | S=0623a698 / folder=Diablo IV; groups=114; files=376; rows=29; columns=4; size=560; uiApplyMs=50; quickPrepMs=10; quickMediaMapMs=43547; quickTailMs=204; quickMediaReused=False |
| LibraryBrowserFirstDetailPaint | 2026-05-03 05:18:43 UTC | 38197 ms | S=e679e83c / folder=Donkey Kong Bananza; files=5; groups=2 |

## Slowest import/intake samples

| Area | Time | Duration | Detail |
|------|------|---------:|--------|
| ImportWorkflowRun | 2026-05-04 04:15:42 UTC | 4337 ms | S=15e71c68 / workflow=import; mode=standard; totalWork=16; importCandidates=5; renameScope=5; hdrPairs=0; renamed=0; deleted=0; metadataUpdated=5; moved=5; sorted=5; hdrMoved=0;... |
| ImportWorkflowRun | 2026-05-05 04:31:25 UTC | 3652 ms | S=fe47ebb4 / workflow=import; mode=standard; totalWork=19; importCandidates=6; renameScope=6; hdrPairs=0; renamed=6; deleted=0; metadataUpdated=6; moved=6; sorted=6; hdrMoved=0;... |
| ImportWorkflowStep | 2026-05-04 04:15:41 UTC | 3233 ms | S=15e71c68 / workflow=import; step=metadata; items=5; updated=5; skipped=0; failures=0 |
| ImportWorkflowRun | 2026-05-07 04:21:59 UTC | 2894 ms | S=83e2dbbe / workflow=import; mode=standard; totalWork=13; importCandidates=4; renameScope=4; hdrPairs=0; renamed=4; deleted=0; metadataUpdated=4; moved=4; sorted=4; hdrMoved=0;... |
| ImportWorkflowRun | 2026-05-06 03:54:40 UTC | 2810 ms | S=835de170 / workflow=import; mode=standard; totalWork=34; importCandidates=11; renameScope=11; hdrPairs=0; renamed=0; deleted=0; metadataUpdated=11; moved=11; sorted=11; hdrMov... |
| ImportWorkflowRun | 2026-05-03 02:02:33 UTC | 2772 ms | S=1f1ed500 / workflow=import+comment; mode=unified; totalWork=9; importCandidates=4; renameScope=4; hdrPairs=2; renamed=2; deleted=0; metadataUpdated=2; moved=2; sorted=2; hdrMo... |
| ImportWorkflowStep | 2026-05-05 04:31:24 UTC | 2264 ms | S=fe47ebb4 / workflow=import; step=metadata; items=6; updated=6; skipped=0; failures=0 |
| ImportWorkflowRun | 2026-05-03 02:02:38 UTC | 1915 ms | S=1f1ed500 / workflow=import; mode=standard; totalWork=7; importCandidates=2; renameScope=2; hdrPairs=2; renamed=0; deleted=0; metadataUpdated=2; moved=2; sorted=2; hdrMoved=2; ... |

## Manual capture matrix

| Flow | Current baseline status | Notes |
|------|-------------------------|-------|
| App open -> library visible | Captured by `LibraryBrowserFirstFolderListPaint` and `LibraryBrowserFirstDetailPaint`. | Re-run after each slice from a cold app start. |
| Import 25 files | Needs clean manual sample. | Current logs include smaller import/intake samples, but not a labeled 25-file run. |
| Import 100 files | Needs clean manual sample. | Add one run before Phase B changes land. |
| Import 500 files | Needs clean manual sample. | Use copied/staged captures only; do not mutate the real source set for measurement. |
| Import HDR PNG/JXR pairs | Needs clean manual sample. | Capture both intake preview and final import progress logs. |
| Open a large game folder in photo view | Captured by `LibraryDetailRender`. | Diablo IV and Timeline samples are the current large-folder stand-ins. |
| Fast-scroll detail pane | Partially captured by repeated `LibraryFolderRender`/`LibraryDetailRender` samples. | Add an explicit scroll-pass note when testing UI changes. |

## Reading guidance

- Treat very large `LibraryBrowserFirstDetailPaint` values as suspect when the app sat open before a first detail render; use fresh cold-start samples for comparison.
- `quickMediaMapMs` spikes inside `LibraryDetailRender` are the most useful photo-view clue from the current logs.
- Builds with Phase A step 2 instrumentation also emit `ImportWorkflowRun` and `ImportWorkflowStep` rows for clean 25/100/500-file import comparisons.
- Keep this file as a before/after reference, not as a product-facing performance claim.
