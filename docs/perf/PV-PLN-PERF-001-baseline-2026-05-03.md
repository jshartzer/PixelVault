# PV-PLN-PERF-001 baseline capture

| Field | Value |
|-------|-------|
| Plan | PV-PLN-PERF-001 |
| Generated | 2026-05-02 19:24:52 -05:00 |
| Source log | `C:\Codex\PixelVaultData\logs\PixelVault-native.log` |
| Rows parsed | 267 |
| Captured range | 2026-05-01 23:15:29 UTC to 2026-05-03 00:20:36 UTC |
| Since filter | 2026-05-01 00:00:00 +00:00 |

## Summary

| Area | Count | Min | Median | P95 | Max | Latest |
|------|------:|----:|-------:|----:|----:|--------|
| IntakePreviewBuild | 10 | 118 ms | 347 ms | 560 ms | 560 ms | 2026-05-03 00:18:11 UTC |
| LibraryBrowserFirstDetailPaint | 9 | 449 ms | 9126 ms | 117766 ms | 117766 ms | 2026-05-03 00:19:01 UTC |
| LibraryBrowserFirstFolderListPaint | 9 | 169 ms | 4495 ms | 5469 ms | 5469 ms | 2026-05-03 00:17:47 UTC |
| LibraryDetailRender | 80 | 72 ms | 220 ms | 9378 ms | 20319 ms | 2026-05-03 00:20:35 UTC |
| LibraryFolderCache | 40 | 142 ms | 185 ms | 804 ms | 987 ms | 2026-05-03 00:20:36 UTC |
| LibraryFolderRender | 99 | 40 ms | 196 ms | 4194 ms | 8338 ms | 2026-05-03 00:20:35 UTC |
| ManualIntakePreparation | 5 | 380 ms | 528 ms | 614 ms | 614 ms | 2026-05-01 23:27:12 UTC |
| RetroAchievementsSearch | 1 | 408 ms | 408 ms | 408 ms | 408 ms | 2026-05-01 23:29:13 UTC |
| SteamGridDbCoverDownload | 1 | 610 ms | 610 ms | 610 ms | 610 ms | 2026-05-01 23:30:27 UTC |
| SteamGridDbHeroDownload | 2 | 275 ms | 275 ms | 552 ms | 552 ms | 2026-05-02 00:39:47 UTC |
| SteamGridDbLogoDownload | 3 | 277 ms | 280 ms | 560 ms | 560 ms | 2026-05-02 00:39:48 UTC |
| SteamGridDbSearch | 4 | 188 ms | 197 ms | 236 ms | 236 ms | 2026-05-02 00:39:47 UTC |
| SteamSearch | 4 | 193 ms | 195 ms | 209 ms | 209 ms | 2026-05-02 00:39:48 UTC |

## Slowest library samples

| Area | Time | Duration | Detail |
|------|------|---------:|--------|
| LibraryBrowserFirstDetailPaint | 2026-05-01 23:41:44 UTC | 117766 ms | S=3bb2c594 / folder=Donkey Kong Bananza; files=5; groups=2 |
| LibraryBrowserFirstDetailPaint | 2026-05-03 00:19:01 UTC | 79078 ms | S=6970a856 / folder=Eternal Darkness Sanity's Requiem; files=80; groups=6 |
| LibraryBrowserFirstDetailPaint | 2026-05-02 19:49:00 UTC | 22554 ms | S=85ce3fe7 / folder=Diablo IV; files=316; groups=114 |
| LibraryDetailRender | 2026-05-02 23:50:49 UTC | 20319 ms | S=a11a85b0 / folder=Timeline; groups=24; files=456; rows=25; columns=4; size=490; uiApplyMs=263; quickPrepMs=45; quickMediaMapMs=19729; quickTailMs=9; quickMediaReused=False |
| LibraryBrowserFirstDetailPaint | 2026-05-02 15:55:22 UTC | 20232 ms | S=2e485aae / folder=Diablo IV; files=297; groups=113 |
| LibraryDetailRender | 2026-05-02 19:49:00 UTC | 15270 ms | S=85ce3fe7 / folder=Diablo IV; groups=114; files=316; rows=24; columns=4; size=560; uiApplyMs=57; quickPrepMs=3; quickMediaMapMs=15065; quickTailMs=20; quickMediaReused=False |
| LibraryDetailRender | 2026-05-02 15:55:22 UTC | 11011 ms | S=2e485aae / folder=Diablo IV; groups=113; files=297; rows=22; columns=4; size=560; uiApplyMs=236; quickPrepMs=7; quickMediaMapMs=10420; quickTailMs=211; quickMediaReused=False |
| LibraryDetailRender | 2026-05-02 23:28:20 UTC | 10576 ms | S=85ce3fe7 / folder=Diablo IV; groups=114; files=351; rows=26; columns=4; size=560; uiApplyMs=38; quickPrepMs=6; quickMediaMapMs=10374; quickTailMs=15; quickMediaReused=False |

## Slowest import/intake samples

| Area | Time | Duration | Detail |
|------|------|---------:|--------|
| ManualIntakePreparation | 2026-05-01 23:23:44 UTC | 614 ms | S=0ca8be5f / includeSubfolders=False; importCandidates=10; manualItems=10 |
| ManualIntakePreparation | 2026-05-01 23:22:50 UTC | 566 ms | S=0ca8be5f / includeSubfolders=False; importCandidates=11; manualItems=11 |
| IntakePreviewBuild | 2026-05-01 23:22:49 UTC | 560 ms | S=0ca8be5f / recurseRename=False; topLevel=11; reviewItems=0; manualItems=11; conflicts=0 |
| ManualIntakePreparation | 2026-05-01 23:24:38 UTC | 528 ms | S=0ca8be5f / includeSubfolders=False; importCandidates=9; manualItems=9 |
| ManualIntakePreparation | 2026-05-01 23:24:57 UTC | 516 ms | S=0ca8be5f / includeSubfolders=False; importCandidates=8; manualItems=8 |
| IntakePreviewBuild | 2026-05-01 23:23:42 UTC | 399 ms | S=0ca8be5f / recurseRename=False; topLevel=10; reviewItems=0; manualItems=10; conflicts=0 |
| ManualIntakePreparation | 2026-05-01 23:27:12 UTC | 380 ms | S=0ca8be5f / includeSubfolders=False; importCandidates=2; manualItems=2 |
| IntakePreviewBuild | 2026-05-01 23:24:37 UTC | 359 ms | S=0ca8be5f / recurseRename=False; topLevel=9; reviewItems=0; manualItems=9; conflicts=0 |

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
