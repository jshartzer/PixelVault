# PixelVault PC performance and experience review

Reviewed November 5, 2026. Native release 0.124.000 and the current C:\Codex source. Recommendation: preserve the polished WPF presentation and improve performance across import, startup, image loading, profiles, and navigation. Tracker reads are one bottleneck within that broader scope. A framework replacement is not justified by this review.

The original review did not change application source. A subsequent, unshipped performance pass now implements the changes documented below; it is not an application release. Runtime inspection covered the Library, TCG game profile, and game tracker list. Other findings are source analysis. Database measurements used a consistent SQLite backup in `artifacts/pc-app-review-20261105/snapshot`, not the live database. Existing working-tree changes were preserved.

## Highest priority findings

### 1. Tracker browsing repeatedly deserializes the entire catalogue

**P1 · Measured data-layer bottleneck.** `TrackerDashboard.Load` loads all definitions and all achievement choices even when opening one game. For each matching tracker it calls `Load`, `Automation`, and `Source`. `Load` and `Automation` each resolve their definition through `Definitions()`, deserializing every custom catalogue again. A small game's screen therefore pays for unrelated large catalogues. The native store also retains the default schema-initializing connection path, which executes CREATE TABLE IF NOT EXISTS statements on each open.

Evidence: [dashboard load](../src/PixelVault.Native/Services/Achievements/TrackerDashboard.cs), [repository definitions and state reads](../src/PixelVault.Tracking/TrackerRepository.cs), [automation reads](../src/PixelVault.Tracking/TrackerRepository.Automation.Commands.cs), [achievement choices](../src/PixelVault.Native/Services/Achievements/AchievementTrackingStore.cs).

The snapshot contained 42 definitions, 6,789 items, and 11,247 state rows. Five measured repetitions after the initial catalogue load produced:

| Operation | Median elapsed | Mean managed allocations per pass |
| --- | ---: | ---: |
| All definitions | 65 ms | 19.8 MiB |
| TCG dashboard store loop, 10 trackers | 705 ms | 425.0 MiB |
| Death Stranding dashboard store loop, 12 trackers | 793 ms | 497.8 MiB |
| Restory dashboard store loop, 20 trackers | 1,288 ms | 812.3 MiB |
| TCG existing batched state read | 67 ms | 27.9 MiB |
| Death Stranding existing batched state read | 32 ms | 22.4 MiB |
| Restory existing batched state read | 31 ms | 19.9 MiB |

These are component timings, not UI load times. The loop reproduces definition/state/automation/source reads; it excludes achievement choices, totals, artwork, and WPF rendering. The existing batched API reads definitions and states only, so its timing is evidence of headroom, not a like-for-like finished replacement. Allocations are cumulative managed bytes, not resident memory. The probe uses one nonempty saved account for all selected games and does not publish account identifiers.

**Change:** introduce one game-scoped snapshot containing definitions, states, source status, automation status, and achievement summaries. Separate schema preparation from normal reads. Scope achievement lookup to the game. The existing `ReadGameTrackers` is a useful starting point, but still reads all definitions before filtering. Ultimately query selected custom definitions and overlay records directly, and cache immutable built-ins separately.

Preserve account isolation, deletion visibility, manual overrides, conflict revisions, and cross-process changes from Companion. A process-local cache without reliable invalidation would be a correctness regression. See [probe](../artifacts/pc-app-review-20261105/Probe/Program.cs) and [results](../artifacts/pc-app-review-20261105/benchmark.txt).

### 2. Startup restores and loads captures that are not visible

**P1 · Reproduced in the current release.** The Library opened in its cover view with search `tcg`, showing one game. It nevertheless restored Death Stranding as the selected detail view and processed 285 captures with a zero-height detail viewport. Session restoration falls back from visible folders to all folders, then calls `showFolder` without a cover-workspace visibility gate.

In session `f39ced98`, the log recorded:

| Milestone | Logged measurement |
| --- | ---: |
| Folder projection, 319 folders / 286 views | 394 ms |
| Folder-list render | 641 ms |
| First folder-list milestone | 1,115 ms |
| Hidden detail initial render | 895 ms, including 199 ms UI apply |
| First detail milestone | 1,869 ms |

Hidden metadata work continued until 15:28:41 Chicago time, roughly 9 seconds after the initial detail application, including seven deferred batches and another UI rebuild. This is background work, not a claim that the app was frozen for nine seconds.

Evidence: [session selection restoration](../src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.FolderList.cs), [detail rendering](../src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs), [diagnostic excerpts](../artifacts/pc-app-review-20261105/runtime-evidence.txt).

**Change:** restore selection identity and scroll state without rendering the detail view. Start detail work only when Captures or a time-based workspace becomes visible. Cancel queued detail work on exit. A search-hidden selection must not trigger a background capture scan just to restore state.

### 3. Game profiles can block on network access before appearing

**P1 · Confirmed blocking path; network delay not benchmarked.** `ShowLibraryGameProfileWindow` calls `EnsureLibraryGameProfileIgdbMetadataCachedBeforeOpen` before checking whether the profile is already open. The helper runs an online metadata request with `Task.Run(...).GetAwaiter().GetResult()` and no view-lifetime cancellation. When hero metadata is missing and credentials are configured, the WPF dispatcher waits for network completion. Profile construction also checks every capture's existence and reads metadata before `win.Show()`.

Evidence: [profile open and metadata helper](../src/PixelVault.Native/UI/Library/MainWindow.LibraryGameProfile.cs), particularly lines 48–57, 106–125, and 283–306.

**Change:** reuse an open profile first; otherwise show a cached title, artwork, and section placeholders immediately. Read capture summaries and refresh metadata asynchronously. Cancel work on close or game change and apply results only to the matching game/account. Defer related games, artwork-heavy sections, and detailed capture data until needed. Offline operation should leave a usable profile.

### 4. Tracker windows reload and recreate their contents too often

**P2 · Confirmed behavior.** Dashboard and game-list windows reload on `Loaded` and `Activated`. Their navigation methods call `Show()` for a child and immediately reload the parent; this does not wait for a child to close or for an edit. Generation numbers discard stale results but do not stop the redundant work. Rendering clears the entire panel and builds all cards again.

Evidence: [dashboard window](../src/PixelVault.Native/UI/TrackerDashboardWindow.cs), [game tracker window](../src/PixelVault.Native/UI/TrackerGameWindow.cs), [navigation](../src/PixelVault.Native/UI/TrackerNavigation.cs).

**Change:** coalesce concurrent reload requests, refresh on a changed store revision, and update only affected rows after edits. Preserve selection, scroll, and artwork. Use virtualized lists for the dashboard and game cards as they grow. The tracker item editor already uses recycling virtualization; extend that strength instead of replacing it.

## Further performance and foundation work

**P2 · Build lightweight Library summaries.** The All projection loads the full metadata index and sorts capture paths for every game before applying search. This explains why one visible result still incurs broad projection work. By Console always rebuilds and clears the All cache. Persist or maintain a compact game summary projection—counts, latest capture, platform identities, cover, and completion—and load ordered capture paths on entry to Captures. Key projections by relevant revisions and grouping mode. Keep duplicate/edition grouping semantics intact. Evidence: [view model](../src/PixelVault.Native/UI/Library/LibraryBrowserViewModel.cs) and [projection cache](../src/PixelVault.Native/UI/Library/LibraryBrowserProjectionCache.cs).

**P2 · Make rendering metrics trustworthy.** The “FirstFolderListPaint” event is emitted by the render-building path, not a confirmed compositor paint, and starts after part of startup. In detail rendering the same stopwatch is stopped at initial application but reused by later traces: `elapsedMs=895` persists while wall time advances by seconds. Add process start, shell shown, first usable content, artwork ready, and background-settled milestones with separate running clocks. Record dispatcher stalls, allocation/GC activity, and cold/warm runs. Existing numbers should not be presented as total launch time. Evidence: [instrumentation](../src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserInstrumentation.cs) and [detail renderer](../src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs).

**P2 · Standardize view lifetimes and scheduling.** Keep the modular monolith and existing shared Tracking, Maps, and LibraryAssets projects. Extract remaining expensive orchestration into game-scoped query services and view models. Give each view one cancellation lifetime and a bounded queue with visible work ahead of prefetch. Avoid solving repeated queries by adding more concurrent tasks. Preserve the current thumbnail LRU, image-load limits, viewport prioritization, and detail cancellation; tune them only with measurements.

**P3 · Consolidate UI resources.** `DesignTokens` exists, but profile/Library/tracker code still repeats colors, dimensions, and templates. `TrackerFormStyle` parses an inline XAML resource dictionary each time it is applied. Move stable styles into compiled resources, centralize spacing/type/radius/action tokens, and cache reusable frozen brushes/templates where safe. This is primarily a consistency and maintenance improvement; its startup benefit has not been measured.

**P3 · Review diagnostic I/O.** Logging appends and flushes synchronously and retries with sleeps on I/O failure. The main log is about 29 MB and does not use the troubleshooting log's rotation path. Move routine diagnostic writes to a bounded writer and rotate the main log, retaining reliable error/shutdown flushing. This is a secondary optimization, not the cause established by the tracker benchmark.

## Visual cohesion and experience flow

**User direction after reviewing the concepts:** keep the current PC app style and polish. The sidebar/top-bar redesigns and compact tracker/profile simplifications are withdrawn. Performance work must preserve artwork, cover treatments, completion decoration, and the existing rich layouts.

Improve consistency within the current presentation: reuse existing color, typography, spacing, focus, and action styles; use the same labels for the same actions; preserve search, selection, and scroll when returning to a view; and standardize loading, empty, and error states. Check window sizing and clipping at different DPI settings without shrinking the design into a generic dashboard. Any larger rearrangement needs a concrete usability problem and should retain the current visual identity.

## First broad performance pass — published in 0.125.000

- **Import preparation:** combine stale ExifTool-file cleanup discovery and media discovery into one directory walk. Preserve stale-file age checks, HDR pairing, parked/hidden-file filtering, and recursive/nonrecursive behavior. Both main and manual intake discover source files on a worker after showing the existing busy state.
- **Images:** immediate thumbnail lookup now reuses the existing frozen bitmap LRU. Disk thumbnail/poster decoding goes through the existing bounded worker queue. Source timestamps, length, and normalized decode width still invalidate stale images; artwork and resolution policy are unchanged.
- **Library/Captures:** restoring selection in cover-only mode no longer builds hidden detail rows or loads the hidden banner. Cancel detail work when leaving Captures or Timeline; render it when the user enters the relevant view. Preserve deferred capture scroll restoration when saving a cover-only session.
- **Profiles:** show the existing profile using cached metadata, then resolve missing IGDB hero information asynchronously. Closing the window cancels the request; identity/root checks prevent applying a stale response after edits or library changes. The second batch below moves local capture enumeration/date preparation into the background; WPF content construction still runs on the dispatcher.
- **Diagnostics:** use a running lifecycle stopwatch for background detail traces, independently of the initial-render stopwatch.

Existing ExifTool writes are already batched and bounded, and Steam title lookup already caches results. This pass does not increase concurrency blindly. Tracker snapshots are implemented in the second batch below; saved-summary cover projection is implemented in the third batch.

## Second broad performance pass — published in 0.125.000

- **Library navigation:** retain separate All and By Console projections for the current root and folder data. Switching grouping no longer discards the expensive All result. Expand the fingerprint to include every capture path and all folder fields (including favorites, showcase/completion state, notes and IGDB metadata). Explicit folder refresh invalidates both projections. Console rows now carry favorite/showcase flags consistently.
- **Profile responsiveness:** show the existing window with a brief loading message, then enumerate files, load selected metadata and resolve/sort dates on a worker. Stats, filmstrip and sessions consume one immutable date snapshot; changing the session threshold no longer repeats file/date probes. Window lifetime cancels pending work and captured-root checks reject stale results after switching libraries. Profile window reuse is scoped to the library root. Preserve the final layout, artwork and completion decoration.
- **Profile section lifecycle:** background preparation means content can arrive after Window.Loaded. Achievement sections now start exactly once whether attached before or after that event, and queued work respects cancellation. Desktop testing caught and resolved this issue before delivery.
- **Tracker data:** one transaction resolves the catalogue once and reads selected progress/source/automation records together. No persistent data cache hides later writes. Account normalization and source-binding validation remain intact. The catalogue is still deserialized once per snapshot; game-specific SQL and one-time schema initialization remain possible follow-ups.

Five repetitions on the same isolated SQLite snapshot compare the former data-store call pattern with the new complete dashboard snapshot:

| Game | Previous median | New median | Previous / new managed allocations |
| --- | ---: | ---: | ---: |
| TCG | 673 ms | 51 ms | 425 / 28 MiB |
| Death Stranding | 595 ms | 24 ms | 498 / 23 MiB |
| Restory | 1,106 ms | 26 ms | 812 / 20 MiB |

The probe verifies state, source and automation parity before timing. These timings exclude achievement lookup, artwork and WPF construction; they are not end-to-end load-time claims. Snapshot provenance and method are the same as the original review; no live progress was changed.

Validation: the broad Native run passed 1,488 tests with the same five baseline failures. After the final profile lifecycle correction, 95 focused tests pass, including before/after Window.Loaded scheduling, cancellation, snapshot parity, another writer's updates, account isolation, grouping reuse and invalidation. The Release test build succeeds. Desktop smoke confirms the grouping round trip and the TCG profile with 29 captures / 16 sessions, hero, filmstrip, achievement/error state and Centurian section. Account credentials remain omitted from the isolated fixture, so live online fetches were not tested. Evidence: `artifacts/pc-performance-batch2-20261105/{verification.json,benchmark.txt,desktop-evidence.txt,performance.patch}`.

At the end of batch 2, cold Library projection still materialized and sorted the full capture index. Batch 3 below addresses that stage when saved folder summaries are complete. Further import throughput changes and screen-wide before/after profiling remain future batches.

## Third performance pass — initial Library projection, published in 0.125.000

The All cover view now merges the saved folder summaries without loading the full photo index or resolving and sorting every capture date. Counts, preview choice, platform identities, completion/favorite/showcase flags and capture membership are preserved. Capture lists are sorted when entering the existing detail, profile, Timeline or Sessions views. Legacy/incomplete folder summaries retain the date/preview fallback, with metadata reads limited to those groups and an explicit index passed through the folder-date helper.

Seven repetitions on the isolated 319-folder / 286-game snapshot reduced the median uncached cover-projection component from **113.5 ms to 5.2 ms**, and managed allocations from **8.01 MiB to 2.69 MiB**. The old path resolved 6,082 capture dates per run; the complete-summary path made zero metadata reads and zero capture-date resolutions. The probe compares every summary field and capture membership before timing, using the prior implementation and the current implementation against the same snapshot. Filesystem caches were warm; these are component timings, not end-to-end cold-launch measurements.

A fresh test process logged `projectMs=11`, `LibraryFolderRender=40 ms` and `FirstFolderListPaint=479 ms` for the filtered Library. This is one functional trace, not a launch benchmark: the existing first-paint marker starts after part of startup and does not confirm compositor paint. Desktop smoke verified Timeline's 29 captures in descending date order (after widening its date filter), return to covers, and the profile's 29 captures / 16 sessions with hero, filmstrip and achievement/Centurian sections intact. The fixture has no account credentials, so online achievement fetches were not exercised.

Validation: **1,494 Native tests pass; the same five established baseline failures remain**. Four new cases cover complete summaries, scoped legacy fallback, and chronological Timeline/Session entry. Release test build succeeds. Evidence, pre-change sources and scoped patch: `artifacts/pc-performance-cold-library-20261105/`.

This improves first projection from a usable saved folder snapshot. Scanning a new library, cache parsing, image rendering, incomplete-summary fallback, import throughput and total process-to-usable time still need separate measurement. No visual redesign or production-data migration was made. The three batches were subsequently published together as 0.125.000; the standard App Testing build was rebuilt and verified. See `artifacts/pc-performance-release-20261105/verification.json`.

## Delivery order and acceptance checks

1. **Broad performance release:** validate the three implemented batches together, then coalesce reloads and profile the remaining startup/import stages. Benchmark source discovery, metadata preparation/writes, import completion, and each main view separately.
2. **Second foundation release:** revision-based invalidation across PC/Companion, consistent view lifetimes, and further lazy profile sections.
3. **Consistency pass:** consolidate shared resources and loading states, retain view state, and correct sizing/clipping while preserving the current presentation. Do not introduce the rejected navigation or compact-layout concepts.

Initial engineering targets, to validate rather than promise: cached game-section transitions under 200 ms; cached usable Library under 750 ms from process launch; no online request before a profile is usable; no capture metadata work in cover-only mode; unchanged activation causes no catalogue reload. Establish a full cold-launch baseline before setting a cold-launch target.

Compare release builds on the same machine and data with five cold-process and ten warm navigation trials; report medians and p95 separately. Include unavailable media roots, offline APIs, rapid navigation, the 4,460-field TCG collection, account changes, Companion writes, deletion, drafts/conflicts, undo, and DPI/high-contrast cases. A passing result must preserve progress correctness and responsiveness, not just reduce the stopwatch number.

Validation after implementation: the serialized Native suite passed 1,482 of 1,487 tests. All five failures also reproduce in an isolated source copy with the saved pre-performance files restored (achievement grouping, settings achievement ordering, two tracker width cases, and Century/Centurian naming). The initial parallel WPF run suffered STA test timeouts and was stopped; the serialized run completed. See `artifacts/pc-performance-20261105/native-tests-serial.txt` and `baseline-tests.txt`. Focused regression tests cover the single source walk, cleanup/HDR behavior, memory image reuse/invalidation, and hidden-view cancellation. A separate App Testing Performance build succeeds. No full profiler trace or exhaustive accessibility audit has been run. Exact end-to-end gains remain unmeasured and must not be inferred from the original component benchmarks.

Desktop smoke check: an isolated SQLite snapshot with automatic intake disabled opened the cover-only Library with zero detail-render events (session `72cbacdf`). Closing preserved the pending detail scroll value of 320. Reopening and explicitly entering Captures rendered the remembered 285-capture game; returning to covers and opening the TCG profile worked. The profile retained its hero, stat cards, filmstrip, and completion section. The fixture omitted accounts and artwork caches, so it is functional evidence rather than an artwork-cache or end-to-end speed benchmark. A live IGDB request was not exercised. See `artifacts/pc-performance-20261105/verification.json`.
