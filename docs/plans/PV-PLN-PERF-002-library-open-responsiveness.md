# PV-PLN-PERF-002 - Library open responsiveness and cache correctness

| **Plan ID** | `PV-PLN-PERF-002` |
| **Status** | Verification in progress - automated checks pass; fresh-cache open verified from logs |
| **Owner** | PixelVault / Codex |
| **Created** | 2026-06-12 |
| **Related** | [`PV-PLN-PERF-001`](PV-PLN-PERF-001-app-speed-and-efficiency.md), [`PV-PLN-UI-001`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md), [`docs/LIBRARY_PERFORMANCE_PLAN.md`](../LIBRARY_PERFORMANCE_PLAN.md) |

## 1. Intent

Make Library open feel immediate and predictable by removing avoidable UI-thread work, duplicate cache reads, and stale async updates in the folder list startup path.

This plan follows the 2026-06 startup review that found the app could show an empty Library for several seconds before cached folders appeared.

## 2. Scope

In scope:

- Library browser first paint and folder-list cache hydration.
- Folder projection cost for All / Timeline / Sessions modes.
- Saved game-index row merging during cache reads.
- Background refresh safety when windows close or sessions become stale.
- Regression coverage for cold-open and stale-cache behavior.

Out of scope:

- Full metadata scan throughput.
- Cover download throughput.
- New Library UX features.
- Distribution or installer behavior.

## 3. Findings To Correct

### A. Reuse metadata index during folder projection

**Problem:** `LibraryBrowserViewModel.BuildLibraryBrowserFolderViews` resolves dates for many paths without passing a metadata index, causing repeated full-index clones through `ResolveIndexedLibraryDate` / `ResolveLibraryFileRecentSortUtcTicks`.

**Target:** Load or borrow the metadata index once per projection build and pass it through all date-resolution calls.

**Acceptance:**

- All / Timeline / Sessions folder projection avoids per-file full-index clone work.
- Existing folder sort and capture-date behavior remains unchanged.
- Add or update tests around merged folder ordering and date fallback.

### B. Make saved game-index merge linear

**Problem:** `ApplySavedGameIndexRows` scans saved rows repeatedly per folder and recreates row lists in `FindSavedGameIndexRow`.

**Target:** Build lookup maps once per merge:

- normalized `GameId`
- normalized title + platform identity
- normalized folder path + platform

**Acceptance:**

- Merge behavior remains identical for id, identity, and folder-path matching.
- Unit tests cover precedence: GameId first, then identity, then folder path + platform.
- Large folder lists avoid O(folders * savedRows) matching.

### C. Collapse duplicate startup cache reads

**Problem:** Library startup can load the folder cache snapshot for immediate paint, then load/parse it again to answer whether the strict/current snapshot exists.

**Target:** Replace the two-call pattern with either:

- a single cache-read result containing `{ folders, isMetadataRevisionCurrent }`, or
- a header-only freshness probe that avoids parsing rows twice.

**Acceptance:**

- Startup first paint still supports stale snapshots.
- Background refresh still runs when the strict cache is stale.
- Cache parsing happens once on the normal first-paint path.

### D. Guard background refresh results after window/session close

**Problem:** `LibraryBrowserRefreshFoldersAsync` posts refresh results back to a dispatcher without checking whether the target window/session is still active.

**Target:** Add a validity gate before applying async results.

**Acceptance:**

- Closing a secondary Library window before refresh completion does not mutate dead UI state.
- Main-window Library refresh still applies normally.
- Stale refresh version handling remains intact.

## 4. Execution Slices

### Slice 1 - Baseline and instrumentation

- Capture a before/after manual startup sample using the troubleshooting log.
- Add a focused performance sample around folder projection metadata-index reuse.
- Record cache state used for first paint: fresh snapshot, stale snapshot, or no snapshot.

### Slice 2 - Projection metadata-index reuse

- Add a projection-local metadata index path in `LibraryBrowserViewModel`.
- Thread the index into date resolution calls.
- Test merged folder ordering and date fallback.

### Slice 3 - Saved-row merge lookup maps

- Introduce lookup construction inside `ApplySavedGameIndexRows`.
- Keep `FindSavedGameIndexRow` behavior available if other callers need it, or route it through the same helper.
- Add precedence/regression tests.

### Slice 4 - Single-read startup cache result

- Add a folder-cache snapshot result model or header probe.
- Update Library startup orchestration to use one row parse.
- Preserve stale-snapshot first paint plus strict-cache background refresh.

### Slice 5 - Async refresh validity guard

- Add window/session validity checks before applying refresh and prefill results.
- Add test coverage where practical; otherwise add a manual QA note.

### Slice 6 - Verification

- Run `dotnet test Codex.slnx`.
- Manually verify:
  - fresh cache open
  - stale cache open
  - no cache open
  - secondary Library window closed during refresh
  - All / Console / Timeline / Sessions folder modes

## 5. Risks

- Reusing a metadata index in projection must not accidentally use stale data after a refresh. Scope the index to a single projection build.
- Lookup maps must preserve existing match precedence exactly.
- Stale snapshot first paint can briefly show older rows. That is intentional, but refresh must still reconcile promptly.

## 6. Exit Criteria

- Cached Library folders paint immediately on open when any valid snapshot file exists.
- Strict stale-cache detection still triggers background refresh.
- Folder projection no longer clones the full metadata index per path.
- Saved game-index merge is linear for normal folder-cache loads.
- Full test suite passes.

## 7. Execution Log

| Date | Update |
|------|--------|
| 2026-06-12 | Plan created from Library startup/performance review findings. |
| 2026-06-12 | Slice 1 started: added startup cache-state troubleshooting telemetry and focused folder projection PERF baseline logging. |
| 2026-06-12 | Manual baseline captured after app open: `LibraryFolderProjection` **6005 ms** and `LibraryBrowserFirstFolderListPaint` **7015 ms** with `startupCache=freshSnapshot`, `foldersLoaded=291`, `views=262`, `grouping=all`. |
| 2026-06-12 | Slice 2 implemented: `LibraryBrowserViewModel` now loads the metadata index once per All / Timeline / Sessions projection and passes it through date-resolution calls; added regression coverage proving index reuse. |
| 2026-06-12 | Slice 2 after-sample captured after app reopen: `LibraryFolderProjection` **150 ms** and `LibraryBrowserFirstFolderListPaint` **776 ms** with the same fresh snapshot shape. |
| 2026-06-12 | Slice 3 implemented: added `SavedGameIndexRowLookup` so folder-cache saved-row merge builds GameId / identity / folder-path lookup maps once per merge; added precedence tests. |
| 2026-06-12 | Slice 4 implemented: startup now uses a single folder-cache snapshot read result carrying parsed folders plus metadata-revision freshness, preserving stale first-paint behavior while avoiding the second strict parse. `dotnet test Codex.slnx` passed: 618 tests. |
| 2026-06-12 | Slice 5 implemented: Library working sets are marked inactive when replaced or closed, and async refresh/snapshot callbacks now skip dispatcher-side mutation for inactive or shutting-down targets. `dotnet test Codex.slnx` passed: 618 tests. |
| 2026-06-12 | Slice 6 automated verification: `dotnet test Codex.slnx` passed: 618 tests. `git diff --check` reported only CRLF/LF normalization warnings, no whitespace errors. |
| 2026-06-12 | Slice 6 fresh-cache log verification: latest open showed `LibraryStartupCache state=freshSnapshot; folders=291; strictCurrent=True; backgroundRefreshQueued=False`; `LibraryFolderProjection` **164 ms** and `LibraryBrowserFirstFolderListPaint` **833 ms**. Remaining manual checks: stale cache open, no-cache open, close-during-refresh, and Console / Timeline / Sessions mode scan. |
