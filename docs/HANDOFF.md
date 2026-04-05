# PixelVault Handoff

This file is the short current-state handoff.

Use it for:

- where to work
- what to read first
- what build is live right now
- what the current source focus is

For durable architecture and data-model context, use:

- `C:\Codex\docs\PROJECT_CONTEXT.md`

## Active Workspace

Work out of:

- `C:\Codex`

Important:

- `C:\Codex` is the source of truth for code, builds, docs, and shared app data
- if a shell or tool reports `A:\Codex`, ignore that and keep using `C:\Codex`
- do not treat `A:\` as the active project drive

Live source:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`
- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`

Do not edit published `dist\PixelVault-x.xxx\PixelVault.Native.cs` snapshots as the primary source.

## Read First

Before making app changes, read:

- `C:\Codex\docs\POLICY.md`
- `C:\Codex\docs\DOC_SYNC_POLICY.md`

Then use these based on the task:

- `C:\Codex\docs\completed-projects\README.md` for **finished initiatives** (e.g. MainWindow extraction Phases A–F) — not active handoff
- `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md` for refactor **principles** (tiered MainWindow bar, `ILibraryScanHost` as port, FS/async scope)—pairs with extraction and service docs below
- `C:\Codex\docs\PROJECT_CONTEXT.md` for architecture and data model
- `C:\Codex\docs\ROADMAP.md` for sequencing and larger direction
- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` for **complete** MainWindow extraction record (technical detail; see completed-projects index first)
- `C:\Codex\docs\PERFORMANCE_TODO.md` for responsiveness/scalability follow-up
- `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md` for hardening / edge-case backlog
- `C:\Codex\docs\MANUAL_GOLDEN_PATH_CHECKLIST.md` for risky manual verification
- `C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md` for service boundaries and parallel lanes

## Current Published Build

Current live build:

- `0.949`

Current executable:

- `C:\Codex\dist\PixelVault-0.949\PixelVault.exe`

Current build pointer:

- `C:\Codex\docs\CURRENT_BUILD.txt`

Desktop shortcut that should always follow the newest published build:

- `C:\Codex\PixelVault.lnk`

## Current Direction

The current codebase direction is:

1. keep shrinking orchestration out of `MainWindow`
2. keep responsiveness and long-running workflow polish moving forward
3. add service seams and UI host extractions without changing visible behavior unless intended

Practical current focus:

- continue the MainWindow extraction roadmap in small slices
- keep service extraction coordinated so parallel work does not collide
- treat source-only refactors and published-build changes as different things

## Working Expectations

- keep `POLICY.md` as the durable behavior contract
- keep `HANDOFF.md` short and current
- use `CHANGELOG.md` for release history, not this file
- after a publish, update `CURRENT_BUILD.txt`, `CHANGELOG.md`, and this file together
- if repo docs and Notion can drift, follow `DOC_SYNC_POLICY.md`

## Current Stop Point

The app is currently published at `0.949`.

**Notion:** [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) updated for Phase **E** capstone (**`ILibraryBrowserShell`**). If release rows in Notion lag `docs/CURRENT_BUILD.txt`, re-sync per `docs/DOC_SYNC_POLICY.md`.

Recent extraction progress (repo):

- **Packed day-card gallery scale-up (0.949):** Library detail and timeline now pack days beside each other instead of forcing a full-width date break every time, and the minimum card/tile sizing is larger overall with timeline intentionally much roomier.
- **Media-aware masonry layout (0.944):** Library detail and timeline masonry tiles now size from real media dimensions when available, sparse groups use fewer columns so they breathe on wide panes, and hero tiles are bounded by image shape instead of hash-only randomness.
- **Detail/timeline tile alignment (0.933):** The regular screenshot pane keeps adaptive variable tile sizing but now caps singleton growth so one image does not dominate the view, and timeline has been brought back to date-grouped rows of individual capture tiles using that same capped adaptive sizing.
- **Detail grid alignment (0.932):** The regular screenshot/detail pane now uses pane-width-driven columns and row fill much more like timeline mode, instead of the old coarse column thresholds that could leave it stuck in an oversized single-column layout.
- **Timeline packed-card regression fix (0.931):** Timeline day cards now keep the same internal photo-column count they were sized for, so the packed layout no longer falls back to a single skinny photo column inside oversized cards.
- **Library layout follow-up (0.930):** Folder-cover rows now respond to pane width more reliably and expand sparse rows instead of leaving a large dead gutter, and timeline/detail width-sensitive layout now rerenders when splitter/window changes matter even if the coarse column count does not.
- **Library timeline layout (0.926):** Timeline now uses **packed day cards** instead of full-width date bands, so sparse capture days can sit beside each other while keeping the existing game/platform/time/comment footer. Reference note: `docs/LIBRARY_TIMELINE_LAYOUT_REFERENCE.md`.

- **Library timeline mode (0.911):** Phase 2 first slice shipped. Timeline tiles now show lightweight **game title + platform chip + capture time** context, and the timeline header summarizes the visible feed by photo count, game count, platform count, and date range.
- **Library timeline mode (0.906):** Phase 1 shipped. Library now has a persisted **`Timeline`** browse mode that swaps the split folder/detail layout for a full-width chronological image feed built from the current visible library scope. Timeline keeps delete + metadata editing working on selected captures and adds a **Folder Browser** button to return to the classic left/right layout.

- **Library grouping (game-first browse):** Added persisted **`LibraryGroupingMode`** with **`All`** and **`By Console`** controls in the Library banner area. Browser rows now project from raw **`LibraryFolderInfo`** into **`LibraryBrowserFolderView`** so the default view can merge same-game captures across consoles without changing storage or scanner persistence. The `All` merge key now prefers normalized game name instead of platform-specific saved row IDs, so cross-platform titles actually collapse into one game row. In `All`, folder cards and the detail header now suppress console-first badge/text chrome so the browse experience reads game-first by default. Merged rows intentionally use **Open Primary Folder** and keep cover / ID actions constrained until the next hardening slice.
- **Library polish (current publish):** The detail header now carries platform badges beside the game title instead of placing them over the cover art or screenshot tiles, keeping the game-first Library cleaner while preserving console context.
- **Library action hardening (current publish):** Real game-to-game selection changes now reset the screenshot pane cleanly instead of reusing the previous game’s detail rows, while same-folder rerenders still use the smoother refresh path. Merged rows still support shared custom covers, **Open Folders**, and merged **Fetch Cover Art** behavior from the previous slices.
- **Diagnostics (current publish):** Settings now includes an opt-in troubleshooting logging toggle that writes a separate `PixelVault-troubleshooting.log`. Library refresh, selection, detail render, metadata repair, and banner-art events now leave a cleaner breadcrumb trail for async UI bug hunts. See `docs/TROUBLESHOOTING_LOGGING.md`.
- **Diagnostics (current publish, deeper tracing):** Library detail rendering now logs metadata-index load, file enumeration, quick/refined snapshot build, and dispatcher handoff start/complete so the next stalled right-pane repro can be pinned to a specific render step.
- **Library stability (current publish):** Rapid browsing no longer lets queued image/video warmup tasks occupy thread-pool workers while waiting on semaphores, so detail-render background work is much less likely to get starved before it starts.
- **Thumbnail cache (current publish):** Thumbnail cache writes now use unique temp files per writer instead of a shared `destination.tmp` path, so concurrent cache saves no longer fight over the same temp file or spam the log with benign access-denied races.
- **Monolith shrink (0.851):** **`LibraryThumbnailPipeline`** centralizes library thumbnail decode/cache/poster I/O; intake preview glue is **`UI/Intake/MainWindow.IntakePreview.cs`** (**`PERFORMANCE_MONOLITH_SLICE_PLAN`** Phase 2 follow-up + Phase 3). Details in **`docs/CHANGELOG.md`** **0.851**.
- **Library + photography (0.852):** **`All`**-mode projection cache, cheaper sort/search/merge paths, and photography window as **`UI/Photography/PhotographyGalleryWindow.xaml`** with index-first gallery load. See **`docs/CHANGELOG.md`** **0.852**.
- **Photo index stars (0.853):** **`Starred`** on **`LibraryMetadataIndexEntry`** / SQLite **`photo_index.starred`**, Photo Index grid column, photography gallery hover star toggle. See **`docs/CHANGELOG.md`** **0.853**.
- **Docs publish (0.854):** Planning docs consolidated (archive snapshots, slim **`PERFORMANCE_TODO`**, **`CODE_QUALITY_IMPROVEMENT_PLAN`**, stubs for perf/service split). See **`docs/CHANGELOG.md`** **0.854**. **Next trim/optimization priorities:** **`docs/NEXT_TRIM_PLAN.md`** (Notion: [mirror](https://www.notion.so/33873adc59b68131ae12f82c97363684)).
- **Monolith / Tier 1a (in source):** Manual metadata window — shared helpers moved to **`UI/Intake/MainWindow.ManualMetadata.Helpers.cs`**; **`ShowManualMetadataWindow`** behavior unchanged. Next slices: Steam search / finish handlers into additional partials.
- **E1–E3 (complete):** Library browser: **`LibraryBrowserHost.Show`** (try/catch + **`ILibrarySession`**) → **`LibraryBrowserShowOrchestration`**(**`ILibraryBrowserShell`** via **`LibraryBrowserShellBridge`**) for open/show/delegate wiring; top nav **`MainWindow.LibraryBrowserChrome.cs`**; layout **`MainWindow.LibraryBrowserLayout.cs`**; render **`MainWindow.LibraryBrowserRender.*.cs`**; toolbar/pane/cover/detail partials **`MainWindow.LibraryBrowserOrchestrator.*.cs`**. **`ILibrarySession`**, **`LibraryWorkspaceContext`**, **`LibraryVirtualization.cs`**
- **Responsiveness:** **`PERFORMANCE_TODO.md`** — item 5 long-workflow spot-check; item 10 **`LibraryBrowserHost`** + **`ILibraryBrowserShell`** / **`LibraryBrowserShowOrchestration`**; manual-metadata game-title list off UI thread when rebuilding choices
- **F1–F3 (complete):** **`SettingsShellHost`** + **`SettingsShellDependencies`** + thin **`MainWindow.SettingsShell`** bridge; **`MainWindow.SettingsPersistence`**; photography — **`MainWindow.PhotographyAndSteam.cs`**
- **Phase 5 (import):** Import-and-edit **Steam store title** when the user leaves the loaded title unchanged — **`IImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** (**`ICoverService.SteamNameAsync`**). Manual metadata **finish** — **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`** uses **`IGameIndexEditorAssignmentService`** for row resolve + persist ( **`ImportServiceDependencies.GameIndexEditorAssignment`** ); **“Add New Game”** preview/ensure — **`BuildUnresolvedManualMetadataMasterRecordLabels`** / **`EnsureNewManualMetadataMasterRecordsInGameIndex`**; tag-text platform alignment + Other-name validation — **`ApplyManualMetadataTagTextToPlatformFlags`** / **`ManualMetadataItemsMissingOtherPlatformName`**; finish **MessageBox** copy — **`GetManualMetadataFinishEmptySelectionMessage`**, **`GetManualMetadataFinishConfirmBody`**, **`BuildManualMetadataAddNewGamePrompt`**. **`RunSteamRename`** uses **`SteamNameAsync`** when **`ResolveSteamStoreTitle`** is not set. Unit tests: **`tests/PixelVault.Native.Tests/ImportServiceManualMetadataTests.cs`**.
- **Phase E2 (library session):** **`ILibrarySession`** includes **`HasLibraryRoot`**, **`EnsureLibraryRootAccessible`** (**`EnsureDir`**), index/game-index/metadata helpers, folder-cache snapshot, **`RemoveLibraryMetadataIndexEntries`**, **`LoadLibraryFoldersCached`**, **`RefreshLibraryCoversAsync`**, and **`RunLibraryMetadataScan`**. Library UI defers root/index work to the session where intended; **`LibraryBrowserHost`** receives **`ILibrarySession`** at construction.
- **Library covers (UI thread):** Removed sync **`ResolveLibraryArt`**; tiles use **`GetLibraryArtPathForDisplayOnly`**; folder-detail banner runs **`GetLibraryArtPathForDisplayOnly`** + **`File.Exists`** on the thread pool, then dispatcher **`QueueImageLoad`**. **`ResolveLibraryArtAsync(..., false)`** returns **`Task.FromResult`** (**`GetLibraryArtPathForDisplayOnly`**) so there is no async state machine on the no-download path.
- **Publish:** script copies full native + test sources under `dist/.../source/`

**Refactor (Apr 2026):** **`GetSavedGameIndexRowsForRoot`** + **`ILibrarySession`** for active-root game index reads / metadata upsert; **`IImportService.RunSteamRenameAsync`**, **`LoadManualMetadataGameTitleRowsAsync`**, async **`RunBackgroundWorkflowWithProgress`** / game-index resolve host; Library **`NavChromeAndToolbar`** + **`PaneEvents`** partials. **Refactor (continued):** **`ILibrarySession`** now exposes **`UpsertLibraryMetadataIndexEntries`** (both overloads), **`RefreshFolderCacheAfterGameIndexChange`**, and **`EnsureGameIndexFolderContext`**; removed unused **`MainWindow.LoadLibraryFoldersCached(root, …)`**. **Phase 3 extraction roadmap:** **A–E** complete (**E** includes **`ILibraryBrowserShell`** + bridge). **Phase F** complete: **`SettingsShellHost`** / **`SettingsShellDependencies`**, **`MainWindow.SettingsShell`** bridge, **`MainWindow.SettingsPersistence`**, photography partial — **`docs/MAINWINDOW_EXTRACTION_ROADMAP.md`**. **Next plan:** shrink **`PixelVault.Native.cs`** and heavy partials per **`docs/NEXT_TRIM_PLAN.md`** (ManualMetadata partial, ImportWorkflow → service, optional wiring partial); then **`PERFORMANCE_TODO.md`** item 7 when editing persistence/scanner call sites.

If you are picking work up midstream:

1. decide whether the task is a shipped-behavior change or a source-only refactor
2. check whether the change belongs in `MainWindow`, an extracted UI partial/host, or a service seam
3. update the matching docs and Notion per `DOC_SYNC_POLICY.md` when milestones or releases change
