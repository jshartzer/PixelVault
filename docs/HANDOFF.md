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

- `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md` for refactor **principles** (tiered MainWindow bar, `ILibraryScanHost` as port, FS/async scope)—pairs with extraction and service docs below
- `C:\Codex\docs\PROJECT_CONTEXT.md` for architecture and data model
- `C:\Codex\docs\ROADMAP.md` for sequencing and larger direction
- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` for MainWindow split work
- `C:\Codex\docs\PERFORMANCE_TODO.md` for responsiveness/scalability follow-up
- `C:\Codex\docs\MANUAL_GOLDEN_PATH_CHECKLIST.md` for risky manual verification
- `C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md` for service boundaries and parallel lanes

## Current Published Build

Current live build:

- `0.849`

Current executable:

- `C:\Codex\dist\PixelVault-0.849\PixelVault.exe`

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

The app is currently published at `0.849`.

**Notion:** release sync for **0.849** is currently pending because the Notion connector token expired during publish prep. [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) remains the active extraction tracker. Further releases: `docs/DOC_SYNC_POLICY.md`.

Recent extraction progress (repo):

- **Library grouping (game-first browse):** Added persisted **`LibraryGroupingMode`** with **`All`** and **`By Console`** controls in the Library banner area. Browser rows now project from raw **`LibraryFolderInfo`** into **`LibraryBrowserFolderView`** so the default view can merge same-game captures across consoles without changing storage or scanner persistence. The `All` merge key now prefers normalized game name instead of platform-specific saved row IDs, so cross-platform titles actually collapse into one game row. In `All`, folder cards and the detail header now suppress console-first badge/text chrome so the browse experience reads game-first by default. Merged rows intentionally use **Open Primary Folder** and keep cover / ID actions constrained until the next hardening slice.
- **Library polish (current publish):** The detail header now carries platform badges beside the game title instead of placing them over the cover art or screenshot tiles, keeping the game-first Library cleaner while preserving console context.
- **Library action hardening (current publish):** Real game-to-game selection changes now reset the screenshot pane cleanly instead of reusing the previous game’s detail rows, while same-folder rerenders still use the smoother refresh path. Merged rows still support shared custom covers, **Open Folders**, and merged **Fetch Cover Art** behavior from the previous slices.
- **Diagnostics (current publish):** Settings now includes an opt-in troubleshooting logging toggle that writes a separate `PixelVault-troubleshooting.log`. Library refresh, selection, detail render, metadata repair, and banner-art events now leave a cleaner breadcrumb trail for async UI bug hunts. See `docs/TROUBLESHOOTING_LOGGING.md`.
- **Diagnostics (current publish, deeper tracing):** Library detail rendering now logs metadata-index load, file enumeration, quick/refined snapshot build, and dispatcher handoff start/complete so the next stalled right-pane repro can be pinned to a specific render step.
- **Library stability (current publish):** Rapid browsing no longer lets queued image/video warmup tasks occupy thread-pool workers while waiting on semaphores, so detail-render background work is much less likely to get starved before it starts.
- **Thumbnail cache (current publish):** Thumbnail cache writes now use unique temp files per writer instead of a shared `destination.tmp` path, so concurrent cache saves no longer fight over the same temp file or spam the log with benign access-denied races.
- **E1–E3:** Library browser: **`LibraryBrowserHost`** entry + **`ShowLibraryBrowserCore`** on **`MainWindow`** in **`UI/Library/MainWindow.LibraryBrowserOrchestrator.cs`**; top nav / window chrome in **`MainWindow.LibraryBrowserChrome.cs`**; folder + detail layout in **`MainWindow.LibraryBrowserLayout.cs`**; folder-tile + detail-pane rendering in **`MainWindow.LibraryBrowserRender.FolderList.cs`** / **`MainWindow.LibraryBrowserRender.DetailPane.cs`**. **`ILibrarySession`** / **`LibrarySession`** (workspace + scanner + **`IFileSystemService`** + root), **`LibraryWorkspaceContext`** caches, virtualization unchanged in **`LibraryVirtualization.cs`**
- **Responsiveness:** **`PERFORMANCE_TODO.md`** — item 5 long-workflow spot-check; item 10 **`ShowLibraryBrowserCore`** in **`MainWindow.LibraryBrowserOrchestrator.cs`** (**`LibraryBrowserHost`** entry); manual-metadata game-title list off UI thread when rebuilding choices
- **F1–F2:** Settings shell partial (incl. path settings dialog, **`ShowSettingsWindow`** modal), photography gallery + Steam picker partial; photography wired from Library + Settings
- **Phase 5 (import):** Import-and-edit **Steam store title** when the user leaves the loaded title unchanged — **`IImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** (**`ICoverService.SteamNameAsync`**). Manual metadata **finish** — **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`** uses **`IGameIndexEditorAssignmentService`** for row resolve + persist ( **`ImportServiceDependencies.GameIndexEditorAssignment`** ); **“Add New Game”** preview/ensure — **`BuildUnresolvedManualMetadataMasterRecordLabels`** / **`EnsureNewManualMetadataMasterRecordsInGameIndex`**; tag-text platform alignment + Other-name validation — **`ApplyManualMetadataTagTextToPlatformFlags`** / **`ManualMetadataItemsMissingOtherPlatformName`**; finish **MessageBox** copy — **`GetManualMetadataFinishEmptySelectionMessage`**, **`GetManualMetadataFinishConfirmBody`**, **`BuildManualMetadataAddNewGamePrompt`**. **`RunSteamRename`** uses **`SteamNameAsync`** when **`ResolveSteamStoreTitle`** is not set. Unit tests: **`tests/PixelVault.Native.Tests/ImportServiceManualMetadataTests.cs`**.
- **Phase E2 (library session):** **`ILibrarySession`** includes **`HasLibraryRoot`**, **`EnsureLibraryRootAccessible`** (**`EnsureDir`**), index/game-index/metadata helpers, folder-cache snapshot, **`RemoveLibraryMetadataIndexEntries`**, **`LoadLibraryFoldersCached`**, **`RefreshLibraryCoversAsync`**, and **`RunLibraryMetadataScan`**. Library browser orchestration (**`ShowLibraryBrowserCore`**, **`MainWindow.LibraryBrowserOrchestrator.cs`**) does not read **`librarySession.LibraryRoot`** directly for those flows; the session owns the active root.
- **Library covers (UI thread):** Removed sync **`ResolveLibraryArt`**; tiles use **`GetLibraryArtPathForDisplayOnly`**; folder-detail banner runs **`GetLibraryArtPathForDisplayOnly`** + **`File.Exists`** on the thread pool, then dispatcher **`QueueImageLoad`**. **`ResolveLibraryArtAsync(..., false)`** returns **`Task.FromResult`** (**`GetLibraryArtPathForDisplayOnly`**) so there is no async state machine on the no-download path.
- **Publish:** script copies full native + test sources under `dist/.../source/`

**Refactor (Apr 2026):** **`GetSavedGameIndexRowsForRoot`** + **`ILibrarySession`** for active-root game index reads / metadata upsert; **`IImportService.RunSteamRenameAsync`**, **`LoadManualMetadataGameTitleRowsAsync`**, async **`RunBackgroundWorkflowWithProgress`** / game-index resolve host; Library **`NavChromeAndToolbar`** + **`PaneEvents`** partials. **Refactor (continued):** **`ILibrarySession`** now exposes **`UpsertLibraryMetadataIndexEntries`** (both overloads), **`RefreshFolderCacheAfterGameIndexChange`**, and **`EnsureGameIndexFolderContext`** so game-index and library-metadata-apply paths avoid **`librarySession.Scanner`**; removed unused **`MainWindow.LoadLibraryFoldersCached(root, …)`** wrapper. **Next likely slices:** trim remaining **`indexPersistenceService`** in **`LibraryMetadataIndexing`** / editor saves when those files change; optional stream **`CopyFile`** on **`IFileSystemService`** if needed. **Game index:** **`IGameIndexEditorAssignmentService`** remains the seam for import finalize + shared save/resolve (see **`GameIndexEditorAssignmentService.cs`**).

If you are picking work up midstream:

1. decide whether the task is a shipped-behavior change or a source-only refactor
2. check whether the change belongs in `MainWindow`, an extracted UI partial/host, or a service seam
3. update the matching docs and Notion per `DOC_SYNC_POLICY.md` when milestones or releases change
