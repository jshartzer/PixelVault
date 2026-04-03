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

- `0.835`

Current executable:

- `C:\Codex\dist\PixelVault-0.835\PixelVault.exe`

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

The app is currently published at `0.835`.

**Notion:** **0.835** is in **Releases**; [release page](https://www.notion.so/33773adc59b681628cfdcb7e21044773). [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) status block updated (Phase 5 import slice + published **0.835**). Further releases: `docs/DOC_SYNC_POLICY.md`.

Recent extraction progress (repo):

- **E1–E3:** Library browser: **`LibraryBrowserHost`** entry + **`ShowLibraryBrowserCore`** on **`MainWindow`** in **`UI/Library/MainWindow.LibraryBrowserOrchestrator.cs`**, **`ILibrarySession`** / **`LibrarySession`** (workspace + scanner + **`IFileSystemService`** + root), **`LibraryWorkspaceContext`** caches, virtualization unchanged in **`LibraryVirtualization.cs`**
- **Responsiveness:** **`PERFORMANCE_TODO.md`** — item 5 long-workflow spot-check; item 10 **`ShowLibraryBrowserCore`** in **`MainWindow.LibraryBrowserOrchestrator.cs`** (**`LibraryBrowserHost`** entry); manual-metadata game-title list off UI thread when rebuilding choices
- **F1–F2:** Settings shell partial (incl. path settings dialog, **`ShowSettingsWindow`** modal), photography gallery + Steam picker partial; photography wired from Library + Settings
- **Phase 5 (import):** Import-and-edit **Steam store title** when the user leaves the loaded title unchanged — **`IImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** (**`ICoverService.SteamNameAsync`**). Manual metadata **finish** — **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`** uses **`IGameIndexEditorAssignmentService`** for row resolve + persist ( **`ImportServiceDependencies.GameIndexEditorAssignment`** ); **“Add New Game”** preview/ensure — **`BuildUnresolvedManualMetadataMasterRecordLabels`** / **`EnsureNewManualMetadataMasterRecordsInGameIndex`**; tag-text platform alignment + Other-name validation — **`ApplyManualMetadataTagTextToPlatformFlags`** / **`ManualMetadataItemsMissingOtherPlatformName`**; finish **MessageBox** copy — **`GetManualMetadataFinishEmptySelectionMessage`**, **`GetManualMetadataFinishConfirmBody`**, **`BuildManualMetadataAddNewGamePrompt`**. **`RunSteamRename`** uses **`SteamNameAsync`** when **`ResolveSteamStoreTitle`** is not set. Unit tests: **`tests/PixelVault.Native.Tests/ImportServiceManualMetadataTests.cs`**.
- **Phase E2 (library session):** **`ILibrarySession`** includes **`HasLibraryRoot`**, **`EnsureLibraryRootAccessible`** (**`EnsureDir`**), index/game-index/metadata helpers, folder-cache snapshot, **`RemoveLibraryMetadataIndexEntries`**, **`LoadLibraryFoldersCached`**, **`RefreshLibraryCoversAsync`**, and **`RunLibraryMetadataScan`**. Library browser orchestration (**`ShowLibraryBrowserCore`**, **`MainWindow.LibraryBrowserOrchestrator.cs`**) does not read **`librarySession.LibraryRoot`** directly for those flows; the session owns the active root.
- **Library covers (UI thread):** Removed sync **`ResolveLibraryArt`**; tiles use **`GetLibraryArtPathForDisplayOnly`**; folder-detail banner runs **`GetLibraryArtPathForDisplayOnly`** + **`File.Exists`** on the thread pool, then dispatcher **`QueueImageLoad`**. **`ResolveLibraryArtAsync(..., false)`** returns **`Task.FromResult`** (**`GetLibraryArtPathForDisplayOnly`**) so there is no async state machine on the no-download path.
- **Publish:** script copies full native + test sources under `dist/.../source/`

Next likely slices: more **`ShowManualMetadataWindow`** orchestration behind **`IImportService`** only when it stays pure (no UI in the service); further **`ShowLibraryBrowserCore`** decomposition (named handlers, smaller methods) when Library UI is next touched; or further **`await`** cleanup per item 11. **F1:** **`ShowSettingsWindow`** on **`MainWindow.SettingsShell`**. **Game index:** **`IGameIndexEditorAssignmentService`** is the current seam for import finalize + shared save/resolve (see **`GameIndexEditorAssignmentService.cs`**).

If you are picking work up midstream:

1. decide whether the task is a shipped-behavior change or a source-only refactor
2. check whether the change belongs in `MainWindow`, an extracted UI partial/host, or a service seam
3. update the matching docs and Notion per `DOC_SYNC_POLICY.md` when milestones or releases change
