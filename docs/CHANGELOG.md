## 0.836
- **Release:** Version **0.836** — publish refresh (when shipped). Responsiveness / correctness fixes below; see **`docs/REVIEW_RESPONSE_2026-04-02.txt`**.
- **Engineering / structure (Phase E2 + import):** **`ILibrarySession`** adds **`EnsureLibraryRootAccessible`** (**`EnsureDir`**), **`HasLibraryRoot`**, **`RemoveLibraryMetadataIndexEntries`**, **`LoadLibraryFoldersCached`**, **`RefreshLibraryCoversAsync`**, **`RunLibraryMetadataScan`**, plus metadata index helpers and folder-cache snapshot; **`MainWindow.LibraryBrowser`** no longer reads **`librarySession.LibraryRoot`** for those flows. Manual metadata finish applies tag-text platform hints and validates “Other” custom name via **`IImportService.ApplyManualMetadataTagTextToPlatformFlags`** / **`ManualMetadataItemsMissingOtherPlatformName`** (**`ImportServiceDependencies.ParseManualMetadataTagText`**, **`CleanTag`**). Finish dialog copy (empty selection, **Add New Game** prompt, final confirm) is **`GetManualMetadataFinishEmptySelectionMessage`**, **`BuildManualMetadataAddNewGamePrompt`**, **`GetManualMetadataFinishConfirmBody`**. Removed unused **`MainWindow`** cover shims (**`TryResolveSteamAppId`**, **`SearchSteamAppMatches`**, **`TryResolveSteamGridDbIdBySteamAppId`**, **`TryResolveSteamGridDbIdByName`**, sync **`ResolveBestLibraryFolderSteamAppId`** / **`ResolveBestLibraryFolderSteamGridDbId`**, sync **`TryDownloadSteamCover`** / **`TryDownloadSteamGridDbCover`**). Unit tests extended in **`ImportServiceManualMetadataTests`**.
- **Library (screenshots / detail):** Incremental detail rerender restores a **saved scroll offset only on the first snapshot** for a render pass; the **metadata-refined** second pass no longer reapplies that offset, so scrolling after the quick paint is not yanked back.
- **Import and Edit:** Steam store title resolution on finish keeps **`ManualMetadataItem`** updates on the **captured UI synchronization context** (no **`ConfigureAwait(false)`** before mutating **`GameName`** in **`ImportService`**; aligns with **`MainWindow`** **`ConfigureAwait(true)`** on the outer **`await`**).
- **Docs:** **`docs/PERFORMANCE_TODO.md`** item 11 clarifies UI-thread expectations for **`ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`**. **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** adds spot checks for detail scroll and import-and-edit Steam title finish.
- **Engineering / structure (MainWindow extraction Phase F1):** **`ShowSettingsWindow`** moved from **`PixelVault.Native.cs`** to **`UI/Settings/MainWindow.SettingsShell.cs`** with **`BuildUi`** / path dialog (no behavior change).
- **Engineering / structure (Phase E2 + import):** **`ILibrarySession.PersistGameIndexRows`** delegates game-index save to **`IGameIndexEditorAssignmentService`**; Library detail metadata repair uses **`librarySession.PersistGameIndexRows`**. **`IGameIndexEditorAssignmentService`** / **`GameIndexEditorAssignmentService`** (**`Services/Indexing/GameIndexEditorAssignmentService.cs`**) owns resolve-by-id/identity + clone/save/invalidate previously wired as **`MainWindow`** lambdas on **`ImportServiceDependencies`**. **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`** uses **`GameIndexEditorAssignment`**; **`RunSteamRename`** uses **`ICoverService.SteamNameAsync`** when **`ResolveSteamStoreTitle`** is unset.
- **Engineering / async cover paths:** **`EnrichLibraryFoldersWithSteamAppIds`** still completes via **`Task`** / **`GetAwaiter().GetResult()`** where applicable. **`ResolveLibraryArt`** sync wrapper removed; Library folder tiles use **`GetLibraryArtPathForDisplayOnly`** (custom / cache / preview, no download); the selected-folder banner runs that helper plus **`File.Exists`** on the thread pool, then **`QueueImageLoad`** on the dispatcher. **`ResolveLibraryArtAsync(..., false)`** returns **`Task.FromResult`** (no async state machine); downloads use **`ResolveLibraryArtWithDownloadAsync`**. Unused **`MainWindow`** sync wrappers over **`coverService`** were removed earlier. **`ICoverService`** sync methods still delegate into **`*Async`**.

## 0.835
- **Release:** Version **0.835** — publish refresh. Continues **Phase 5** import service extraction and structural work from **0.834**; Library responsiveness fixes below are user-visible.
- **Engineering / structure (service split):** **Phase 5** — import-and-edit **Steam store title** refresh when the loaded title is unchanged is **`IImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** (**`ICoverService.SteamNameAsync`**). Manual metadata **finish** (normalize name, assign **`GameId`**, copy canonical name and **Steam AppID** onto the matched **`GameIndexEditorRow`**, persist index) is **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`**, with **`ImportServiceDependencies`** supplying normalization, row resolution, platform label, grouping identity, and save. Unit coverage: **`tests/PixelVault.Native.Tests/ImportServiceManualMetadataTests.cs`** (stub **`IMetadataService`** / **`ICoverService`**).
- **Library (covers):** Cancelling **Fetch Cover Art** no longer wipes the global decoded-image cache, so other folder tiles stay stable. **Fetch Cover Art** on one game skips the full folder-cache rebuild when IDs can be merged into the existing cache (faster finish).
- **Library (screenshots):** Selected-game **Screenshots** grid paints from the file list and index/file-date fallbacks first; embedded-metadata repair (ExifTool batch) runs afterward and refreshes grouping only when day order changes. Detail thumbnails use the priority image queue so the open game loads sooner.
- **Engineering / structure (service split):** Intake **review-item metadata** writes live in **`IImportService.WriteMetadataForReviewItems`** (see **0.834** changelog for detail) — shipped in this build.
- **Engineering / structure (item 7 slice):** Library folder refresh and game-index save alignment read cached folders via **`ILibraryScanner.LoadLibraryFoldersCached`** instead of **`MainWindow`** indirection.

## 0.834
- **Release:** Version **0.834** — publish refresh. **Phase 4** / **Phase 5** service-split intent since **0.833** is unchanged (see **0.833**); additional engineering below.
- **Docs:** Added **`docs/ARCHITECTURE_REFACTOR_PLAN.md`** — refactor contract (tiered MainWindow vs legacy, **`ILibraryScanHost`** as application port, **`IFileSystemService`** / async scope, sequencing). Linked from **`HANDOFF.md`**, **`PROJECT_CONTEXT.md`**, **`MAINWINDOW_EXTRACTION_ROADMAP.md`**, **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`**, **`PERFORMANCE_TODO.md`**.
- **Engineering / structure (MainWindow extraction):** **`LibraryBrowserHost`** (`UI/Library/LibraryBrowserHost.cs`) is the Library browser entry point (wired with **`ILibrarySession`**); **`MainWindow.ShowLibraryBrowserCore`** in **`UI/Library/MainWindow.LibraryBrowser.cs`** holds the existing UI. **`ILibrarySession`** / **`LibrarySession`** aggregate **`LibraryWorkspaceContext`**, **`ILibraryScanner`**, **`IFileSystemService`**, and **`LibraryRoot`**. See **`docs/MAINWINDOW_EXTRACTION_ROADMAP.md`** Phases E1–E2.
- **Engineering / responsiveness (no intended user-visible behavior change):** Manual metadata **game title** combo loads saved game-index rows on the thread pool and, when that list is empty, **`LoadGameIndexEditorRowsCore(root, null)`** off the UI thread; UI updates marshal on **`Dispatcher.BeginInvoke`**. Long-workflow spot-check notes and **`PERFORMANCE_TODO`** items 5 / 10 updated accordingly.
- **Engineering / Library performance:** Left-side folder list virtualization reuses visible row **`FrameworkElement`**s across scroll (**`VirtualizedRowHost.RecycleVisibleRowElements`**, **`UI/LibraryVirtualization.cs`**); the capture/detail pane keeps full rebuilds so **`detailTiles`** stays consistent. **`PERFORMANCE_TODO`** item 9.
- **Engineering / async I/O (no intended user-visible behavior change):** **`TimeoutWebClient`** exposes **`DownloadStringAsync`** / **`DownloadFileAsync`**; **`IMetadataService`** adds batch **`ReadEmbedded*Async`** (composable with **`Task.Run`** / **`await`**; ExifTool work stays synchronous). **`ICoverService`** adds **`SteamNameAsync`**, Steam search/resolve/download **`*Async`** methods; sync cover APIs delegate into them. Library detail metadata warm-up **`await`**s **`ReadEmbeddedMetadataBatchAsync`**. **`PERFORMANCE_TODO`** item 11 first slice.
- **Engineering / file-system seam:** **`IFileSystemService`** / **`FileSystemService`** (**`Services/IO/`**); **`LibraryScanner`** uses it for enumeration and existence checks. Extended surface: **`ReadAllLines`**, **`WriteAllLines`**, **`DeleteFile`**, **`MoveFile`**, **`CopyFile`**, **`CreateDirectory`**, **`GetLastWriteTime`**. **`ImportService`** uses the same **`IFileSystemService`** instance wired from **`MainWindow`**. Library metadata scan parallel batches use **`ReadEmbeddedMetadataBatch`** on thread-pool workers with **`SemaphoreSlim`** + **`Task.WaitAll`**. **`PERFORMANCE_TODO`** item 8; **`FileSystemServiceTests`**.
- **Engineering / responsiveness (no intended user-visible behavior change):** Library metadata scan progress uses **`ILibraryScanner.ScanLibraryMetadataIndexAsync`** (**`Task.Run`** off the UI thread) instead of wrapping the synchronous scan in **`Task.Factory.StartNew`**.
- **Engineering / async I/O (continued):** Library **cover refresh** (**`RefreshLibraryCoversAsync`**) **`await`**s **`ICoverService`** **`*Async`** for ID resolution and downloads (**`ResolveBestLibraryFolder*Async`**, **`TryDownloadSteamCoverAsync`**, **`TryDownloadSteamGridDbCoverAsync`**, **`ForceRefreshLibraryArtAsync`**, **`ResolveLibraryArtAsync`**). **`MainWindow.LibraryBrowser`** schedules refresh with **`Task.Run`** over the async method so HTTP work does not block thread-pool threads via sync **`GetAwaiter().GetResult()`** wrappers.
- **Engineering / file-system seam (continued):** **`IFileSystemService.CopyFile`**; **`CoverServiceDependencies.FileSystem`** for **Set Custom Cover**; **`MainWindow`** migration copy helpers and cover backup use **`fileSystemService`**. **`ImportService.RunManualRename`** uses **`MoveFile`** on the seam.
- **Engineering / library scanner (item 7 slice):** **`ILibraryScanner.EnsureGameIndexFolderContext`**; **`LoadGameIndexEditorRowsCore`** delegates to it. Parallel library metadata scan batches use **`ReadEmbeddedMetadataBatch`** + **`Task.WaitAll`** (removed **`ReadEmbeddedMetadataBatchAsync`**.**`GetResult()`** / fake **`async`** batch lambdas).
- **Engineering / async I/O (metadata editor):** Manual metadata **Search Steam** uses **`coverService.SearchSteamAppMatchesAsync`** inside **`Task.Run`**.
- **Engineering / structure (service split):** **Phase 5** — **`BuildSourceInventory`** (top-level and optional recursive rename-scope media lists from configured source roots) moved from **`MainWindow`** to **`IImportService`** / **`ImportService`** with **`ImportServiceDependencies.EnumerateSourceMediaFiles`**. Intake preview, review/manual metadata queue builders, library browser source-file count, and **`ImportWorkflow`** use **`importService.BuildSourceInventory`**. Plan: `docs/pixelvault_service_split_plan.txt`.
- **Engineering / structure (service split):** **Phase 5** — import **delete-before-processing** for explicit paths is **`IImportService.DeleteSourceFiles`**; **`ImportWorkflow`** still selects **`ReviewItem`** / **`ManualMetadataItem`** rows marked for delete, then delegates the file deletes to the service.
- **Engineering / structure (service split):** **Phase 5** — Steam intake rename helpers live in **`Services/Import/SteamImportRename.cs`** (path-map application, top-level path resolution after rename, **`TryBuildSteamRenameBase`** / AppID-prefix rules). **`IImportService.RunSteamRename`** performs the Steam title rename loop using **`ImportServiceDependencies`** (**`ParseFilenameForImport`**, **`ResolveSteamStoreTitle`**, **`EnsureSteamAppIdInGameIndex`**, existing **`UniquePath`** / sidecar move / **`Log`**). **`ImportWorkflow`** delegates **`RunRename(files, …)`** to the service.
- **Engineering / structure (service split):** **Phase 5** — manual game-title prefix renames (**`GameName_` + original base**) are **`IImportService.RunManualRename`**, using **`SanitizeManualRenameGameTitle`** and **`NormalizeTitleForManualRename`** from **`MainWindow`**; library metadata apply calls the service directly.
- **Engineering / structure (service split):** **Phase 5** — intake **review-item metadata write** step is **`IImportService.WriteMetadataForReviewItems`** (builds **`ExifWriteRequest`** list, **`IMetadataService.BuildExifArgs`** / **`RunExifWriteRequests`**); **`ImportWorkflow.RunMetadata`** delegates to the service. **`ImportServiceDependencies`** adds **`MetadataService`**, file time funcs, **`GamePhotographyTagLabel`**.

## 0.833
- **Release:** Version **0.833** — publish refresh. User-facing changes since **0.831** are summarized under **0.832** (settings, library UI, covers, photography, MainWindow extraction, **Phase 4** **`LibraryScanner`** work).
- **Engineering / structure (service split):** **Phase 4 complete** for the current scope — **`LibraryScanner`** now depends on **`IMetadataService`** for **`ReadEmbeddedMetadataBatch`** (removed from **`ILibraryScanHost`**). Added **`RefreshFolderCacheAfterGameIndexChange`**. **MainWindow** / import / library browser call **`libraryScanner`** directly for metadata scans, index upserts/removes, and photo-index load/save; redundant partial wrappers in **`LibraryMetadataIndexing`** and the unused **`LoadLibraryFolders`** shim in **`LibraryFolderIndexing`** are removed.
- **Engineering / structure (service split):** **Phase 5 (initial)** — **`IImportService`** / **`ImportService`** in `Services/Import/ImportService.cs`: undo manifest load/save, **move-to-destination** pipeline (conflict mode, sidecars, undo entries), **sort destination root** into game folders + metadata index upsert, **undo import** file moves + remaining manifest rows. **`ImportWorkflow`** keeps progress UI and delegates to **`importService`**; **`ImportServiceDependencies`** supplies paths, **`Unique`**, sidecar helpers, and **`ILibraryScanner`**. Plan: `docs/pixelvault_service_split_plan.txt`.

## 0.832
- **Settings:** Path Settings **MinHeight** no longer exceeds window height; paths area is **scrollable**; main Settings **header actions wrap** on a second row; **Control Center** scrolls so the intake preview keeps space; default window height fits smaller displays better.
- **Library (platform sort):** each console section has a **chevron** to **collapse or expand** its tiles; section counts read **`N games`** on **one line** (no stacked “folders” label).
- **Set Custom Cover:** opens **only** the file picker (starts in **My Covers**); **Open My Covers Folder** still opens Explorer from the same menu.
- **Photography gallery:** **Photography** on the **Library** toolbar and **Settings** header opens the game-photography tagged capture browser (same behavior as before; entry points restored).
- **Engineering / structure (no intentional user-visible behavior change for these):** MainWindow extraction **Phase E** — `ShowLibraryBrowser` → `UI/Library/MainWindow.LibraryBrowser.cs`; **`LibraryWorkspaceContext`** holds folder listing + file-tag caches; **Phase F1–F2** — settings shell partial (`BuildUi`, path settings window), photography + Steam match picker partial. **`Publish-PixelVault.ps1`** bundles `source/src/PixelVault.Native` and `source/tests/PixelVault.Native.Tests` into the publish folder. See `docs/MAINWINDOW_EXTRACTION_ROADMAP.md`.
- **Engineering / structure (service split):** **Phase 4** — **`LibraryScanner`** now owns `LoadPhotoIndexEditorRows`, `LoadLibraryFolders`, **`RebuildLibraryFolderCache`**, and **`LoadLibraryFoldersCached`** (metadata-index scan paths call the scanner’s rebuild; **`MainWindow`** delegates cached loads). **`ILibraryScanHost`** exposes folder-cache helpers (clear/stamp/load/save, `ApplySavedGameIndexRows`, `PopulateMissingLibraryFolderSortKeys`, `LogPerformanceSample`) instead of forwarding a monolithic rebuild. Thin wrappers remain on indexing partials where applicable. Plan: `docs/pixelvault_service_split_plan.txt`.

## 0.831
- **Import and Edit:** if you leave the **game title** as loaded (e.g. the numeric hint from the filename) but the row is **Steam** with an AppID, PixelVault now resolves the **store title** before the game-index prompts—same as automatic import. If you **change** the title, your text is kept.
- **Manual metadata / library edit preview:** fixed **QueueImageLoad** so images become visible when a callback sets `Source` (single-file preview was staying collapsed).
- **Steam lookup:** search accepts a **numeric AppID** (store `appdetails`) as well as a game name; help text updated.

## 0.830
- **Import and Comment** now opens the same **manual metadata** editor as library edits and manual intake: every top-level upload file appears as a row (rule-matched files show an **Auto** badge). Select which files to import; unselected files stay in the upload folder. Optional **delete before import** applies to the selection. The workflow runs Steam rename (on the selection), manual title rename, delete, EXIF/metadata, and per-file move—no separate review window.
- **Publish script:** after a successful publish, updates repo-root `PixelVault.lnk` to point at the new `PixelVault.exe` and working directory.

## 0.829
- **Refactor (no intentional behavior change):** [MainWindow extraction](docs/MAINWINDOW_EXTRACTION_ROADMAP.md) **Phase B** — changelog UI in `UI/ChangelogWindow.cs`, `UiBrushHelper.FromHex`, and `WorkflowProgressWindow` / `WorkflowProgressView` for import progress, library metadata scan/apply, and cover refresh.

## 0.828
- Fixed Steam intake rename for **title + timestamp** filenames (and AppIDs resolved from the game index): the old logic treated the numeric AppID length as a prefix to strip from the **title**, which duplicated the game name (e.g. `...Simulator` + `ewind...`). Renames now strip only a real **AppID prefix** on the filename, or replace the parsed **title segment** before the timestamp.
- Fixed import **after** Steam rename: metadata and move steps now use **post-rename paths** so files are not left stuck in the upload folder with new names.
- **Set Custom Cover** opens the **My Covers** folder in Explorer before the file picker (unchanged picker default).

## 0.827
- Added **My Covers**: a permanent `PixelVaultData/saved-covers` folder (outside `cache`) for cover art you collect, with **My Covers** on the Library toolbar and Settings, **Open My Covers Folder** on library tiles, a short `README.txt` on first use, and **Set Custom Cover** defaulting the file dialog there when no existing art path is resolved.

## 0.826
- Added filename-based console fallback for library metadata indexing when embedded keywords are empty or only resolve to `Other`, so cold-dropped PS5, Xbox, Steam, PC, and custom-platform files no longer stay misclassified just because their EXIF tags are weak.
- Fixed Manual Intake platform guessing so parser labels like `PlayStation` preselect the PS5 checkbox and custom parser labels flow into `Other` with the custom platform field populated instead of arriving with no platform decision.

## 0.825
- Fixed a Library cache regression where fetching cover art for a single selected game could overwrite the full folder cache with a one-game snapshot, leaving the Library stuck on that title until a rebuild.
- Hardened startup cache loading so obviously incomplete folder-cache snapshots are ignored and rebuilt instead of being trusted on launch.

## 0.824
- Fixed Xbox intake detection for filenames that use hyphens in the time portion, such as `Human Fall Flat-2026_03_31-00-09-35.png`, so they classify as Xbox instead of falling into Manual Intake as `No confident match`.
- Added a broader PS5 share rule for segmented or fractional timestamp filenames like `Astro's Playroom_CLIMBING RUN_2023101311054800.jpg` while keeping the standard PS5 share format intact.

## 0.823
- Reduced Library image decode work so the main folder grid, selected-game banner, and screenshot tiles stop requesting oversized bitmaps on first paint.
- Stopped queued image loads from re-reading files a second time when a cached thumbnail or poster is already available, which makes repeat opens feel much snappier.

## 0.822
- Fixed game-index persistence across library-root moves by backfilling Steam App IDs and SteamGridDB IDs from older root-scoped SQLite caches when the library is relocated to a new drive.
- This keeps rebuilt libraries from coming up as a fresh blank-ID index after a move from one capture root path to another, which restores cover fetch and other ID-dependent workflows.

## 0.821
- Hardened shared log-file access so PixelVault reads and appends `PixelVault-native.log` with shared file access and short retries instead of letting a transient log-file race block startup.
- This prevents the current build from failing to open just because another PixelVault process or thread touched the shared log at the same moment.

## 0.820
- Moved the remaining manual metadata rebuild controls out of the main Library view and into Settings, including selected-folder rebuilds, so everyday browsing stays focused on refresh, covers, and editing.
- Changed the Library `Refresh` and folder `Refresh Folder` actions to stay lightweight and refresh folder/cache state without launching metadata scans or rewriting metadata.
- Serialized library maintenance work that mutates the shared metadata and tag caches, which prevents the rebuild/refresh overlap that was throwing collection-corruption exceptions during large library scans.

## 0.819
- Restored the Library `Fetch Covers` button to full-library refresh behavior instead of limiting it to the currently selected game.
- Changed cover download order to prefer Steam portrait art first, then fall back to SteamGridDB via STID when Steam does not have a usable portrait cover.
- Always allow Steam AppID lookup during cover refresh so Steam portrait fallback still works even when a title already has an STID cached.

## 0.818
- Fixed shared SQLite metadata-index loads so stored `console_label` values survive restart instead of being recomputed from sparse tags and collapsing Steam files back to `Other`.
- Normalized folder-derived game names by stripping repeated platform suffixes like `- Steam` and `- PS5` before they reach the saved game index.
- Pruned stale zero-file `Other` rows when a live platform-specific row already exists for the same game, which stops duplicate `Other` entries from resurfacing in Game Index.

## 0.817
- Stopped library scans and folder-cache rebuilds from re-parsing filenames to change stored game or platform assignments after import, so the saved photo/game index stays authoritative unless the user edits it.
- Reused per-file intake analysis across the review queue and manual queue builders so upload-queue preview generation no longer repeats the same parse and capture-date work for the same files.
- Kept Steam AppID and cover-ID recovery available for stale library rows by allowing ID lookups from filename/title hints without rewriting the stored game or platform labels.
- Hardened published-build data migration so stale cache files inside older `dist` releases can no longer overwrite the shared `PixelVaultData` index on startup.

## 0.816
- Cached capture timestamps in the library metadata index so folder ordering and grouped Library detail renders can reuse indexed times instead of deriving capture dates file-by-file every time.
- Added an in-place SQLite upgrade for `photo_index` plus background backfill of missing capture timestamps during folder rebuilds and first detail selection, which cuts the first-click cost on laggy folders without requiring a cache reset.

## 0.815
- Cut the biggest Library responsiveness bottlenecks by caching parser and saved-game-index lookups, moving detail-date grouping off the UI thread, gating startup rebuilds, and debouncing folder search against a committed term.
- Reduced background churn in the Library by stopping off-screen tile work, batching missing metadata tag reads during folder and index rebuilds, and avoiding full-folder rescans when backfilling cached sort keys.
- Added in-flight cancellation to manual Steam metadata search so lookups can be stopped directly from the editor instead of waiting for the current provider request to finish.

## 0.814
- Stopped the Library from auto-selecting and rendering the first folder during startup cache prefill, so the left folder grid can paint immediately instead of stalling behind an expensive first-detail render on the network library.

## 0.813
- Kept the Library window on the fast window-first startup path while moving the cached folder snapshot load to its own async prefill step, so startup no longer regresses into a pre-window block.
- Left the full library refresh async behind that snapshot prefill, which keeps the EXE responsive while older cached folders appear first and fresh data swaps in afterward.

## 0.812
- Prefilled the Library from the last saved folder-cache snapshot on startup so the current EXE shows existing folders immediately instead of an empty pane while the background refresh runs.
- Removed eager `File.Exists` checks from cached folder-path parsing so startup cache loads stay fast on the network library.

## 0.811
- Stopped the main Library window from blocking on the full folder-cache load before first paint, so the published EXE opens immediately again instead of appearing hung on startup.
- Moved the initial library-folder refresh onto the existing async path and reused that same path for post-edit and post-scan library reloads.

## 0.810
- Fixed Steam intake so renamed screenshots keep their Steam classification during preview/import instead of dropping into `Other`.
- Carried renamed file paths through the rest of the import workflow so Steam captures no longer get stuck on stale pre-rename paths.
- Moved the Library splitter default to the right and lowered the four-cover breakpoint so the browser opens showing at least four covers per row.

## 0.804
- Pushed the Library screen closer to the Figma reference with pill-style left-side controls, a more intentional selected-game hero card, and calmer right-side action buttons.
- Added a framed cover treatment and stronger section hierarchy on the detail side so the Library reads more like a designed screen and less like a maintenance form.

## 0.803
- Added a gear icon to the Library `Settings` button and a refresh symbol to `Refresh` so the top action row reads more clearly at a glance.
- Restyled the Library search and filter strip into a darker rounded control bar with tighter, cleaner filter buttons.
- Rounded the outer corners of Library folder cards so the browse grid feels more cohesive with the rest of the new chrome.

## 0.802
- Restyled the Library top action row with a dedicated toolbar-button treatment so import and utility actions feel cleaner and less like generic app controls.
- Removed the heavy default button shadows from the Library toolbar and replaced them with calmer dark utility chrome and richer primary action fills.

## 0.801
- Restored the Library window to the last known-good left/right browser-detail layout after the visual shell pass hid the left-side game list.
- Removed the experimental top-level library shell composition so the browser pane is back on the reliable structure used before the redesign regression.

## 0.800
- Restored the Library browser to a safer two-pane layout after the visual overhaul regression that hid the left-side game list.
- Fixed the top-bar column wiring so the new library header, import actions, and utility actions no longer compete for the same layout space.

## 0.799
- Gave the Library a cleaner split-pane shell with a dedicated top action bar, calmer panel chrome, and a more intentional selected-game presentation inspired by the Figma direction.
- Restyled library folder cards to use a taller, image-led layout with quieter metadata so browsing feels calmer and more cover-first.

## 0.798
- Stopped normal SQLite index writes from invalidating the library folder cache stamp, reducing needless NAS-backed cache rebuilds on startup.
- Persisted downloaded cover-art paths back into cached folder and game-index state so fetched covers stick across refreshes and restarts.

## 0.797
- Made the Filename Rules window scale more gracefully at smaller sizes with taller, scrollable editor and rules-list regions.
- Saving a filename rule now clears the editor and deselects the lists so reopening a custom rule is a deliberate edit action, and double-clicking a custom rule loads it back into the editor.

## 0.796
- Removed the duplicate bottom Save and Close buttons from the Filename Rules window, leaving those actions only in the top toolbar.
- Recent unmatched filename samples now leave the sample list once you create a rule from them, and the custom and built-in rule lists render as visible usable areas instead of header-only placeholders.

## 0.795
- Fixed the Library `Edit Metadata` window open path by deferring its initial selection until the dialog is loaded, preventing the closed-window exception that could block the form from opening at all.
- Switched the metadata editor's first preview paint onto the shared async image loader so large NAS-backed captures no longer block the dialog before it appears.

## 0.794
- Added a migration safety net that backfills missing `Steam AppID` and `STID` values from the legacy flat cache into the shared SQLite game index, so half-migrated libraries can recover their external IDs instead of staying blank.

## 0.793
- Fixed the rebuilt Filename Rules form so built-in and custom selections swap cleanly, `Disable Built-In` acts on the actual selected built-in rule, and unsaved edits are protected even when the window is closed from the title-bar `X`.

## 0.792
- Hardened the shared-data-root behavior for published builds so running from `PixelVault-current` keeps using the persistent `PixelVaultData` store instead of drifting into per-build data under `dist`.
- Changed `Fetch Covers` to refresh only the selected folder by default and require confirmation before running a full-library cover refresh, reducing the background Steam/SteamGridDB churn that was making the app feel sluggish.

## 0.791
- Restored shared launch behavior for current published builds and improved data-root migration so newer settings, cache, and log files move forward into the shared `PixelVaultData` store instead of only copying missing files.
- Reduced accidental whole-library cover-refresh work by making single-folder cover fetches the default path whenever a folder is selected.

## 0.790
- Continued the service extraction work by pulling filename-rule loading, starter-rule creation, built-in disable overrides, validation, save, and reload behavior into a dedicated `FilenameRulesService`.
- Normalized filename-derived capture timestamps to `DateTimeKind.Local` and rebuilt the Filename Rules window around the workflow spec so sample promotion and rule editing are less dependent on monolithic UI logic.

## 0.789
- Restored Steam manual-export routing so bare timestamp exports stay on manual intake when they still need a Steam AppID, even if the filename parser already found a capture time.
- Reduced filename-rule sample noise by recording recent unmatched samples only for files that did not match an explicit convention, and now prefill manual-intake game names from parser title hints when available.
- Added a dedicated Filename Rules form spec so the next UI pass can be rebuilt around a clear sample-to-rule workflow instead of the current dense grid.

## 0.788
- Made filename rules human-readable by shipping token-style rule text like `[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]` while still compiling those rules down to regex internally for matching.
- Improved the Filename Rules editor so the grids surface readable rule text, sample rows bind directly, selection-driven actions enable correctly, and add/promote/disable flows jump straight into editable custom rules instead of feeling inert.

## 0.787
- Fixed the Filename Rules editor bindings by converting the filename parsing models to bindable CLR properties, so built-in and custom rules now render correctly in the grids.
- Hardened `Add Rule`, sample promotion, and frequent-promotion flows to show validation errors instead of crashing the app when a starter rule cannot be built safely.

## 0.786
- Published a follow-up build after the filename parsing architecture/spec refinements so the current release line stays aligned with the documented parser defaults and edge-case rules.

## 0.785
- Centralized filename parsing behind a shared service and added a dedicated Filename Rules editor so built-in conventions, custom DB-backed overrides, and repeated unmatched samples can all be managed from one place.
- Improved sample-to-rule promotion by recognizing more timestamp/title filename shapes, supporting starter rules from recent unmatched samples, and adding a faster `Promote Frequent` path for repeated misses.
- Added test coverage for filename parser behavior plus filename convention persistence/sample accumulation so future convention changes have a stronger safety net.

## 0.784
- Polished the Steam match picker footer by aligning the `Cancel` and `Use Match` buttons on a fixed right-aligned grid instead of letting the shared button margins push them out of line.

## 0.783
- Tightened Library detail-grid spacing by switching the virtualized capture rows to balanced row gutters, so the gaps between screenshots stay even horizontally and vertically instead of drifting into oversized blank bands.
- Upgraded Manual Intake Steam lookup to fetch multiple possible store matches and open a picker window, so date-only Steam exports can be assigned the correct AppID even when the typed game name is not an exact match.

## 0.782
- Fixed a pair of Library/intake regressions by removing oversized gaps between virtualized capture rows and making the new intake preview wait until the window is actually loaded before applying its first async refresh.
- Tuned the upload-queue review button and preview styling by giving the controller badge more room, removing the oval pill chrome from the `Ready by console` list, and hiding the `Preserve file time` label from general intake UI.
- Added Manual Intake support for Steam’s date-only manual exports like `20200525124119_1.jpg`, including a Steam lookup box that searches by game name, fills the AppID before import, and carries that AppID into the saved game index.

## 0.781
- Continued the performance/maintainability pass by extracting cover, metadata, and index-persistence work into dedicated services, so the UI and indexing flows no longer own the full SteamGridDB/ExifTool/database implementation details directly.
- Moved import/manual-intake metadata work, intake preview refresh, and Game Index external-ID resolution further away from the UI thread with progress-backed background execution and added more targeted performance logging around slow network and preview-build paths.
- Finished the first round of Library scalability improvements by virtualizing the capture pane, caching `Recently Added` sort keys, debouncing Library search, and adding instrumentation for folder-cache, folder-render, and selected-folder render timings.

## 0.780
- Added a new Library upload-queue review button beside `Fetch Covers`, with an unread-style badge that shows how many top-level intake items are waiting in the upload folder.
- Reworked the intake preview from the old plain text report into a dedicated window with grouped console sections, summary cards, manual-intake visibility, and source-folder notes so queue review is easier to scan before import.
- Simplified Library video tiles by removing the hover tooltip and extra preview chrome, keeping the `CLIP` badge and duration bubble while reducing visual noise.
- Avoided a redundant second full folder-cache rebuild after library scans by reusing the freshly rebuilt cache for the tile refresh, and added timing logs around the folder-cache rebuild step to make future scan slowness easier to trace.

## 0.779
- Expanded FFmpeg-backed clip handling beyond poster generation by probing and caching clip metadata for Library tiles, surfacing duration, resolution, frame-rate, and audio details inline, and adding direct `Open 10s Preview Clip` / `Copy Clip Details` actions for video captures.
- Hardened Library virtualization and lazy-loading for resize-heavy browsing by preserving folder-grid and detail-pane scroll positions across layout-only rerenders instead of snapping back to the top.
- Added a dedicated Library virtualization stress-data generator plus a matching verification checklist so large mixed-media browse sessions can be reproduced more easily during future performance and UI checks.

## 0.778
- Changed the metadata edit game-title picker to show choices as `Game Name | Console`, so typing the game name now drives the dropdown/autofill more naturally instead of effectively keying off the console prefix first.
- Made the metadata edit window taller and batched the upfront ExifTool metadata reads for library edits, which should reduce the need to scroll immediately and make the form open faster on larger folders.
- The library metadata editor now batches tags, comments, and embedded capture-time reads together, which also helps reopened video edits reflect their sidecar-backed custom times more reliably.

## 0.777
- Tightened the Library folder-row virtualization geometry so the rendered row heights now match the spacer math more closely, which should reduce the scroll jitter that could show up in the new virtualized folder browser.
- Added support for Steam clip exports named like `clip_<unix-ms>.mp4`, parsing the filename timestamp as local time so import metadata and appended rename timestamps follow the captured time instead of the filesystem fallback.

## 0.776
- Virtualized the Library folder browser into row-based windowed rendering so the left-side game grid no longer builds every section and folder tile up front on large libraries.
- Changed the Library detail pane to lazy-load capture rows as you scroll, keeping thumbnail/video preview work focused on the visible portion of the current folder instead of materializing the whole folder immediately.

## 0.775
- Continued the modular-monolith refactor by moving storage, indexing, media-tool, import, and metadata/library-edit helper slices out of the main window source file into dedicated source files without changing app behavior.
- Kept the app as one desktop executable and verified the extraction work with clean Release builds so the shipped codebase is easier to extend for later batching, indexing, and media-tool improvements.

## 0.774
- Reverted Library hover preview back to the original direct-video playback method instead of the cached preview-clip path, since the earlier method felt much more immediate in practice.
- Kept the newer inline-tile rendering and aspect-ratio fix, so hover preview should now behave more like the old fast popup path without stretching the playing frame into a square.

## 0.773
- Changed Library video hover playback to use a lightweight cached 10-second FFmpeg preview clip instead of opening the full source video directly, which should make hover preview start much faster on large captures.
- Preview clips are now warmed in the background as visible video tiles render, so by the time you hover a video the short preview asset is often already ready on disk.

## 0.772
- Changed inline video hover preview to preload the local clip source in the tile so playback can start much faster on hover, instead of re-opening the media file from scratch each time.
- Kept the preview inside the existing tile bounds and stopped resetting it to a square-sized surface, so the playing video now preserves the same aspect ratio as the thumbnail/poster underneath.

## 0.771
- Changed Library video hover playback to run inline inside the existing capture tile instead of opening a separate popup preview, while keeping the muted 10-second hover behavior.
- Added an immediate cached-thumbnail path for Library tiles so existing on-disk image thumbs and cached video posters can show up right away on launch instead of making the Library look unloaded until async loads catch up.

## 0.770
- Improved video thumbnails by changing FFmpeg poster generation to use dedicated frame-cache files, retry real frame extraction instead of getting stuck on old generic fallback cards, and probe a few later seek positions for captures that do not decode cleanly at the opening instant.
- Added a muted 10-second hover preview for video captures in the Library detail view and increased the default capture tile size there from `320` to `500` without changing the existing slider scale.

## 0.769
- Replaced the Library's broad image-cache flush on metadata regroup, Game Index folder-align, and delete flows with targeted invalidation of only the files and folders that actually changed, so the rest of the library stays warm instead of blanking out after an edit.
- This specifically addresses the cases where renaming a game, regrouping captures, or deleting the last file from a group could temporarily unload large parts of the Library until a later refresh completed.

## 0.768
- Fixed manual external-ID clears so blanking a saved Steam AppID or STID from the Library `Edit IDs...` flow is treated as an explicit do-not-auto-resolve choice instead of ordinary missing data.
- Cover refresh and ID-resolution paths now respect those manual clears, so a cleared STID no longer gets silently re-resolved and written back after a right-click cover fetch.

## 0.767
- Improved thumbnail persistence by switching the in-memory image cache to recent-use behavior instead of simple FIFO eviction, so folders you just opened are less likely to cold-reload when you come back to them.
- Normalized thumbnail decode sizes into shared cache buckets, which reduces duplicate thumbnail variants on disk and increases cache reuse between the Library grid, folder detail view, and nearby slider sizes.

## 0.766
- Changed the Library folder right-click `Fetch Cover Art` action to force a fresh pull when the folder already has cached downloaded cover art, while leaving the main toolbar cover refresh on the existing skip-if-present behavior.
- Preserved custom covers during forced single-folder fetches, so a manual cover choice is not overwritten by the right-click refresh.

## 0.765
- Fixed a Library refresh regression after metadata edits or capture deletes by reusing the rebuilt folder cache instead of forcing a full cold library reload, which was causing the folder tiles and thumbnails to blank out temporarily after destructive changes.

## 0.764
- Added multi-select capture actions in the Library detail view, including selection-aware right-click metadata editing and a small red trash button that permanently deletes the selected files and their photo-index records.
- Changed the Photo Index editor action from `Delete Row` to `Forget Row` so cache-only removal is clearly distinct from real file deletion.
- Fixed Library metadata edits so changing a capture's game title or platform can move it into the correct existing group or create a new group instead of silently keeping the old `GameId` bucket.

## 0.759
- Started the modular refactor by moving the shared import/index model types and the small timeout web client into dedicated source files while keeping the live app as one desktop executable.
- Verified the refactor with a clean Release build plus safe app smoke tests covering startup, Settings preview, Game Index, Photo Index, and cover-refresh open/cancel flow.

## 0.758
- Added a new import summary screen for both the standard import flow and Manual Intake, using the same dark status-window styling as the Library scan and cover-refresh monitors.
- The import pipeline now captures explicit rename, delete, metadata, move, and sort counts so the summary window can show a reliable end-of-run breakdown instead of making users infer results from the main log.

## 0.757
- Fixed a Library-surface import null reference by making the shared preview renderer no-op when the Settings preview box is not present. Library imports now complete without trying to repaint the Settings-only preview control.

## 0.756
- Fixed the `Edit IDs` dialog action row so `Save` and `Cancel` use the same vertical alignment and margin treatment instead of sitting crooked.

## 0.755
- Increased the Library folder `Edit IDs` dialog height and cleaned up the Save/Cancel action row so both ID fields and buttons fit cleanly without clipping.
- Moved the `Game Index` and `Photo Index` buttons out of the top Library toolbar and into the space between Search and Sort, with a smaller centered style and light-purple treatment.

## 0.754
- Added `Game Index` and `Photo Index` buttons directly to the Library toolbar so those editors are available without going through the Settings surface.
- Changed the Game Index and Photo Index windows to open modelessly, which lets you keep using the Library while the tables stay open.
- Added a new folder-tile right-click `Edit IDs...` action in the Library to edit Steam App ID and SteamGridDB ID with a small Save/Cancel dialog.

## 0.753
- Fixed the SQLite runtime provider initialization so the new SQLite-backed index store no longer throws `You need to call SQLitePCL.raw.SetProvider()` when a save or write path touches the Game Index or Photo Index.
- Added the explicit SQLite bundle dependency and startup initialization needed for the per-library index database to work reliably in published builds.

## 0.752
- Moved the live Game Index and Photo Index runtime storage to a per-library SQLite database under `PixelVaultData\cache`, so index growth no longer depends on rewriting flat tab-delimited cache files on every save.
- Added first-run migration from the legacy `game-index-*.cache` and `library-metadata-index-*.cache` files into the SQLite store while keeping the folder cache as rebuildable derived state.
- Updated the SDK project and workspace docs for the new SQLite-backed index model, including Git ignores for SQLite runtime sidecar files.

## 0.751
- Fixed a null-reference crash during Library-driven imports by defaulting the move-conflict mode to `Rename` when the Settings-only conflict dropdown is not instantiated.
- Improved import failure logging so workflow and manual-intake exceptions now write the full exception text to the log instead of only the top-level message.

## 0.750
- Hardened Steam rename detection so the import pass only rewrites filenames that actually match supported Steam screenshot naming patterns instead of grabbing arbitrary 3+ digit numbers.
- Fixed library refresh/index reconciliation so existing `GameId` assignments are preserved when the saved platform still matches, which prevents folder-name guesses from splitting captures into the wrong game group.
- Stopped normal Steam intake from silently writing both `Steam` and `PC` tags by default, keeping shipped Steam metadata aligned with the review flow and prior cleanup expectations.

## 0.749
- Removed the PixelVault logo block and folder-count label from the Library header so the browse toolbar starts flush on the left instead of reserving a title column.
- Shifted the Library search field left so its leading edge aligns with the `Import` button, while keeping the sort and folder-size controls on the same filter row.

## 0.748
- Replaced the Library's `Game Library` text header with the shared PixelVault logo, kept the title area within a fixed width so the logo fits cleanly, and aligned the search field to the left-side import action group.
- Centered the selected text inside the sort picker, cleaned up the dropdown item alignment, and rebalanced the top filter row so the sort control and folder-size controls sit more evenly.
- Lowered the folder-size slider so it sits between the `Folder size` label and the current numeric value instead of riding too high in the row.

## 0.747
- Tightened the Library top-bar spacing by shrinking the `Settings` button and reducing the maintenance-button cluster width so `Fetch Covers` no longer gets clipped on the right edge.
- Restyled the Library sort picker with a softer shell, rounded border, and stronger text treatment so the selected mode reads more cleanly in the dark toolbar.

## 0.746
- Lightened the Library sort dropdown so its selected text reads in dark text on a light field instead of blending into the dark toolbar.
- Reworked the Library top bar to remove the Photography shortcut there, add a left-leaning import action group, and make `Import` the primary visual action beside `Import and Comment` and `Manual Import`.
- Moved the Library status indicator out of the pill badge and into a smaller footer-style line at the bottom of the left panel.

## 0.745
- Added a persistent Library sort control with `Platform`, `Recently Added`, and `Most Photos` modes so the browse layout can switch between grouped and global views without resetting between launches.
- Flattened the Library grid for the non-platform sorts, removed the platform section headers there, and added small platform icon badges to each folder tile so console identity still reads at a glance.

## 0.744
- Restyled the Library section headers with larger platform labels, cleaner count presentation, and platform-specific icon tiles sourced from the shared `C:\Codex\assets` workspace.
- Added restrained platform accent colors so Steam, PS5, Xbox, PC, and mixed-platform groups read more clearly without overpowering the cover art grid.

## 0.743
- Changed cover refresh to prefer saved `STID` values first, then fall back to Steam App ID discovery only when SteamGridDB IDs are missing.
- Stopped cover refresh from deleting existing cached covers before a replacement is successfully downloaded, so already-good art stays in place.
- Tightened single-folder cover fetch so right-click refresh no longer stalls on unnecessary Steam App ID lookups when an `STID` is already available.

## 0.742
- Added SteamGridDB integration so PixelVault can store a local API token, resolve missing `STID` values in the Game Index, and prefer SteamGridDB portrait grid art during cover refresh when an `STID` is available.
- Updated the Steam screenshot rename/import path so the raw Steam AppID is captured into the Game Index before the filename is converted to the game title.
- Added support for older Steam screenshot export names like `35720_2012-03-31_00001.jpg` so they are recognized as Steam imports and can still contribute a capture date during intake.
- Refined the Settings window with a taller default height and a stretchable, scrollable intake preview area so larger preview batches are easier to inspect.

## 0.732
- Made the Game Index authoritative for library folder naming by deriving canonical folder names from the saved game title and platform instead of leaving folders bound to the original filename guess.
- Game Index saves now move files and sidecars into the canonical folder on disk, including adding ` - Platform` suffixes for duplicate multi-platform titles, before rebuilding the library cache.

## 0.731
- Fixed Game Index saves so deleted or consolidated master rows no longer get immediately resurrected on reopen from stale file-to-game assignments.
- Added a save-time game ID remap pass that rewrites the library metadata index and folder cache toward the surviving record before the library cache rebuild runs.

## 0.730
- Added an `STID` field to the Game Index so SteamGridDB IDs can be stored, searched, edited, and saved alongside Steam AppIDs.
- Extended the game-index and library-folder cache formats to persist `STID` while remaining backward-compatible with older saved rows.

## 0.729
- Added explicit timeouts to the Steam AppID and cover-download web requests so a single slow or stalled response can no longer hang cover refresh indefinitely.
- Made cover-refresh deduping use the library folder master key instead of raw game name, which keeps scoped right-click fetches aligned with the app's folder identity rules.

## 0.728
- Stopped folder-detail thumbnail clicks from clogging the priority image lane by reserving priority for the banner art and letting capture thumbnails fall back to the normal queue.
- Added stale-request checks to the async image loader so abandoned folder-detail loads bail out before decoding once you switch to a different folder.

## 0.727
- Prioritized the selected-folder banner art and capture thumbnails ahead of the larger library thumbnail backlog so the active folder view fills in sooner.
- Increased background image-load concurrency and stopped rebuilding the right-hand folder detail pane when the same folder is still selected, which prevents late refreshes from making already-loaded images disappear again.

## 0.726
- Hardened deferred library image loading so a follow-up refresh no longer blanks folder art, banner previews, or capture thumbnails while another async request is still resolving.

## 0.725
- Fixed the deferred library and gallery image loader so folder art, banner previews, and capture thumbnails actually render again instead of staying blank after the performance refactor.

## 0.724
- Reduced repeated intake folder scans by building shared source inventories for preview, process, and manual-intake flows instead of re-enumerating the same files several times.
- Switched metadata writes to bounded parallel ExifTool execution so larger imports can lean on more CPU without freezing the UI on one file at a time.
- Added capped in-memory image caching, broader on-disk thumbnail reuse, and deferred library/gallery image loading so large folders stay more responsive while browsing.
- Added optional FFmpeg-based video poster generation with hardware-accel preference and fallback poster rendering when FFmpeg is unavailable.

## 0.714
- Fixed library self-healing so files whose tags were corrected away from `Multiple Tags` can remap onto the proper single-platform game record when the library reloads.
- Added cleanup for stale zero-file `Multiple Tags` game-index rows so the old master record disappears once its files have been reassigned to the corrected platform record.

## 0.713
- Recentered the folder-preview capture-size slider around `320` so the preview sizes feel more balanced between small and large.
- Removed the border chrome from the capture previews and tightened their spacing for a cleaner folder-detail strip.
- Added a right-click menu to folder preview captures with quick actions including open, open folder, copy path, and single-photo metadata editing.

## 0.712
- Tightened the library folder-size slider range so smaller tiles are easier to reach, with `240` now sitting at the midpoint instead of near the low end.
- Added persistence for the library folder-size slider so PixelVault reopens the library using the last folder-tile size you chose.

## 0.711
- Fixed single-folder `Fetch Cover Art` so it no longer rewrites the cached library folder list with only the selected folder after a scoped cover refresh.
- Scoped cover refresh now merges updated Steam AppID state back into the full cached library instead of replacing the entire folder cache.

## 0.710
- Made the library the startup view so PixelVault now opens directly into the main browser instead of the old workflow home screen.
- Moved the old home screen into a separate Settings utility window, removed the library link from that view, and reorganized its actions around paths, import tools, maintenance, and utilities.
- Added library search plus a dedicated folder-size slider for the left-hand folder browser, and increased the default library window size substantially while keeping it bounded to the current screen.

## 0.704
- Added a right-click `Fetch Cover Art` action on library folders so a single selected game folder can refresh Steam cover art without running the full library cover pass.
- Reused the existing cover-refresh status window, cancellation flow, and completion logging for folder-scoped cover refreshes.

## 0.703
- Switched library tag writes from clear-and-append to full-list assignment, which makes the file metadata itself match the tags shown in the metadata form much more reliably when tags are added or removed.

## 0.702
- Changed the library metadata apply flow so the photo index is updated from the same normalized tag set submitted by the metadata form, instead of rereading tags back from disk for that immediate post-edit update.

## 0.701
- Fixed library tag writes so the full tag set is replaced across all tag fields PixelVault reads back from, which means deleting a tag in `Additional tags` now removes it from the file instead of leaving it behind in older XMP fields.

## 0.700
- Fixed library `Edit Metadata` so tag-field, console, custom-platform, and photography-tag changes explicitly force a metadata tag write back into the file instead of relying only on the old change-detection path.

## 0.699
- Updated the Photo Index editor to support multi-row selection.
- Changed the Photo Index `Reload` action so it rereads tags from the selected file(s), writes those values back into the matching index rows, saves the index, and refreshes the grid.
- Expanded the Photo Index `Pull From File` and delete actions to work across multiple selected rows.

## 0.698
- Fixed game-index remapping so scans no longer keep a stale `GameId` when the current file name and current console tags point to a different title/platform identity.
- Stopped applying saved game-index rows by folder path alone, which prevents bad rows from overwriting a folder with the wrong game name or console.

## 0.697
- Stopped auto-adding the `PC` tag whenever `Steam` is selected, so Steam and PC now behave as separate platform tags in both intake/review metadata writes and library metadata edits.

## 0.696
- Fixed the batched ExifTool photo-index scan parser so `Refresh`, `Rebuild`, and `Scan Folder` keep the real tag set from disk instead of dropping rows when ExifTool returns placeholder path fields.
- Added safer fallback matching for batch tag reads so files with special characters in their paths still resolve back to the correct photo-index row.
- Filtered ExifTool placeholder `-` values out of the parsed tag list so they cannot end up as fake tags in the photo index.

## 0.695
- Fixed the library `Edit Metadata` apply path so it only submits the currently selected images into the file-write, organize, and photo-index update workflow.
- Tightened the metadata form's new-game flow so verified entries are created at the `Add New Game` confirmation step instead of being silently added later during apply.
- Changed folder metadata saves to refresh the photo-level index from the tags actually written to each file, which keeps the index aligned with disk and promotes console-name tags like `Xbox` or `PS5` into the photo index console field.

## 0.690
- Added delete actions to both the Game Index and Photo Index editors so selected rows can be removed before saving.
- Added a `Pull From File` action in the Photo Index editor to reread embedded tags from the selected file and refresh that row in the index.
- Fixed the library metadata editor so changing a file's `Game ID` no longer triggers a file-tag rewrite by itself, which prevents tag clearing and stops bad master-record rows from being created during reassignment.

## 0.680
- Added a top-level `Photo Index` button on the main screen that opens the per-file index in its own in-app editor window.
- Added a searchable photo-index table with editable `Game ID`, `Console`, and `Tags` fields, plus open-file/open-folder shortcuts for the selected row.
- Saving the photo index now rewrites the photo-level cache and rebuilds the grouped library immediately so Game ID changes take effect right away.

## 0.675
- Switched master game IDs to shorter sequential values in the `G00001` format and added migration so older saved IDs are rewritten through the game index and related caches.
- Added an `Add Game` action to the Game Index editor so new master records can be created directly in-app before they have assigned files.
- Updated the library metadata title dropdown to show platform-first labels like `Xbox | Diablo IV`, while still saving just the canonical game title behind the scenes.

## 0.670
- Added a stable `GameId` field to the photo-level metadata index and shifted library grouping to follow that ID instead of raw game-name matching.
- Changed rebuild and folder-scan refreshes to rescan embedded file tags into the photo index and prune stale entries from the scan scope so the cache stays aligned with the files on disk.
- Updated the Game Index and metadata-edit flows to preserve `GameId`-based master records, keep AppID lookups keyed to those records, and expose the `GameId` directly in the editor.

## 0.660
- Turned the game index into a stricter master record by merging duplicate rows per game and platform while keeping same-title entries on different platforms separate.
- Changed game-index save and AppID resolution to key off the merged game-plus-platform identity instead of raw folder-path duplicates.
- Updated the Edit Metadata title field to use an alphabetized dropdown from the master game index and prompt before adding a brand new game title.

## 0.651
- Fixed folder-level `Edit Metadata` so saving a folder merges the edited files back into the full photo-level metadata index instead of risking a partial index rewrite.
- Changed folder-level metadata saves to record the tag set directly from the applied edit state, which keeps the photo-level index aligned with what the editor just wrote to each file.
- Preserved the existing saved Steam App ID when a folder-level metadata edit reorganizes or renames a game folder, so the game index does not get blanked during that refresh.

## 0.650
- Added a dedicated saved game index cache so manual game-index edits and resolved Steam App IDs persist independently from the transient library folder cache.
- Added App ID resolution into the Game Index flow so missing Steam App IDs can be searched, written back into the game index, and synced into the folder cache.
- Updated cover fetching to consult the saved game index for Steam App IDs before falling back to filename parsing or live Steam title lookups.

## 0.640
- Added a top-row `Game Index` button on the main screen so the cached game index is easier to reach.
- Replaced the raw text-file handoff with an in-app table editor for the cached game index, including search, editable game/platform/AppID fields, and a save action that writes changes back into the cache.

## 0.638
- Fixed batched ExifTool library scans so rebuilds and folder scans normalize both queued metadata paths and ExifTool-returned SourceFile paths before matching them, which lets existing Steam/PS5/Xbox tags survive cache rebuilds instead of falling back to Other.
- Kept the safer folder-cover persistence from 0.636 so library tiles can keep using the resolved portrait cover art path.
## 0.637
- Fixed batched ExifTool library scans so they normalize returned source paths before matching them back to queued files, which keeps rebuilds from dropping tagged files into Other when the tags are still present.
- Kept the safer folder-cover persistence from 0.636 so library tiles can keep using the resolved portrait cover art path.

## 0.636
- Hardened library rebuilds so a blank tag rescan preserves the last known tag state instead of collapsing whole folders back to Other.
- Persisted resolved folder cover paths alongside cached library entries so folder tiles keep the same art the detail preview is already showing.
- Preserved cached Steam AppIDs and resolved cover art paths when rebuilding the library folder cache.

## 0.635
- Fixed a library/manual metadata regression where saving a loaded folder could clear keyword tags across every file in that batch.
- Changed library and manual metadata writes so PixelVault only rewrites tag fields when the tag-related values actually changed, leaving untouched files and untouched tag sets alone.
- Added original-value tracking for metadata editor items so bulk edits can tell the difference between a real change and a mixed or blank UI state.
## 0.634
- Fixed Fetch Covers so it refreshes cached Steam art instead of reusing older downloaded covers, which lets portrait-style art replace earlier wide header images.
- Kept custom cover overrides untouched while clearing only the built-in cached Steam cover before a refresh.

## 0.633
- Changed Steam cover downloads to prefer the portrait-style library capsule art instead of the wide store header image.
- Added a small fallback chain for Steam portrait art URLs so PixelVault can try the tall library image before falling back to the old header image.

## 0.632
- Added a non-blocking cover refresh monitor so Fetch Covers now shows live progress, descriptive status lines, and remaining work while it resolves AppIDs and pulls art.
- Added cancel support for the cover refresh flow so long Steam lookup/download runs can be stopped cleanly without freezing the library window.
- Expanded the cover refresh summary to report both AppIDs resolved and titles with dedicated cover art ready.

## 0.631
- Expanded the game-level library index so every game entry can store a best Steam AppID for cover-art lookups, not just Steam-tagged folders.
- Updated Fetch Covers to backfill missing Steam AppIDs into the shared library index before downloading art, so later cover refreshes can reuse cached IDs.
- Reused the stored Steam AppID during cover downloads so cover art can be fetched by ID instead of repeating title guesses each time.

## 0.630
- Added a persistent game-level library index so PixelVault can reopen the library from cached virtual game entries instead of rebuilding every game bucket from scratch.
- Stored per-game file lists in that index so the library detail view can load each virtual folder without regrouping the physical folder on every open.
- Added per-game Steam AppID tracking in the library index so Steam cover fetches can persist a resolved AppID for later cover refreshes.

## 0.620
- Batched ExifTool metadata reads during library scans so PixelVault no longer launches ExifTool once per file while rebuilding the library index.
- Batched tag reads for the photography gallery query path so tag-driven media lookups reuse a single ExifTool pass across many files.
- Kept the write path unchanged in this pass so the first performance upgrade stays focused on the highest-impact read bottleneck.
## 0.610
- Updated the library metadata editor so the library edit flow applies to every loaded item in the form instead of only the currently selected subset.
- Simplified the filename guess panel to the lighter secondary style and shortened it to the new Best Guess | ... format.
- Changed library folder covers to a portrait game-cover ratio in both the folder tiles and the selected-folder preview area.
## 0.600
- Added MP4 and other video captures to the import, review, library, and photography gallery flows so they show up alongside screenshots instead of disappearing behind empty folders.
- Switched video metadata writes to XMP sidecars and wrote Immich-friendly tag fields including digiKam TagsList and Lightroom HierarchicalSubject.
- Made video sidecars move, sort, and undo together with their media files so tags stay attached through the full PixelVault workflow.
- Added generated video poster thumbnails so videos render as browseable items in the library and gallery views.
## 0.591
- Fixed a cross-thread error in the library metadata progress flow by making metadata keyword reads safe for background processing.

## 0.590
- Added a library metadata progress window so long-running metadata edits now show a live step count, remaining work, and detailed per-file status.
- Added a display-only filename-based console guess in the metadata editor to help with manual tagging without changing any metadata automatically.
## 0.580
- Split the library-wide scan actions into Refresh for incremental metadata updates and Rebuild for a full forced rescan.
- Reworked the library header so Photography stays visually separate on the left while Refresh, Rebuild, and Fetch Covers stay grouped on the right.
- Increased library cover art sizing, added persistent custom cover overrides with a tile right-click menu, and kept those overrides in shared data so they survive app updates.
- Updated the right-hand capture browser to preserve original image aspect ratios and increased the default thumbnail size for easier browsing.
## 0.571
- Added a shared on-disk thumbnail cache under PixelVaultData so library thumbnails persist across app updates instead of regenerating every version.
- Library tile art and preview-sized images now reuse cached thumbnail files when available, which should make the library feel faster after the first load.
## 0.570
- Changed the full-library Scan Library action to force a true metadata rescan instead of trusting older cached entries as unchanged.
- This keeps whole-library scans aligned with Scan Folder so tag-based regrouping updates correctly across the full library.
## 0.569
- Fixed a background-thread logging bug that could make a completed library scan throw a cross-thread error at the end of the run.
- Library scan monitor logging now routes safely back to the UI thread so long scans can finish cleanly.
## 0.568
- Added a non-blocking library scan monitor so library and folder scans now run in the background instead of freezing the app.
- Added cancel support for library scans, with a dedicated Cancel Scan button that lets the current file finish cleanly before stopping.
- Added detailed scan progress reporting with processed counts, remaining counts, and per-file activity messages so it is clear what PixelVault is doing.
# PixelVault Changelog

## 0.567
- Switched virtual library grouping to embedded platform tags only, removing the old filename fallback from the library route.
- Normalized legacy custom platform tags like Platform:PC and Platform:Steam so they no longer create false Multiple Tags groupings.
- Updated the library metadata editor to read current embedded tags from the file, so reopening the editor reflects the actual saved console tag state.
- Fixed the selected-items apply path in the metadata editor so only the files you confirm are carried forward.

## 0.566
- Narrowed library tag reads to the explicit XMP/IPTC fields PixelVault writes, which avoids phantom platform combinations coming back from aggregate keyword fields.
- Normalized platform-family parsing so Steam stays a single logical platform instead of reappearing as both Steam and PC in the library editor.
- Fixed library regrouping and editor reloads to use the same normalized platform-family rules as the metadata index.


## 0.565
- Fixed library metadata edits so changing a console tag now replaces the old platform tags instead of appending a new one on top.
- Library regrouping should now stay aligned after reopening the editor because the file metadata and index agree on the final console tag set.

## 0.564
- Fixed library metadata editing so files in a Multiple Tags view now rebuild their console checkboxes from stored file tags instead of showing blank.
- Updated virtual library grouping to prefer the indexed tag list over stale aggregate folder labels, which helps mis-grouped single-platform files correct themselves.

## 0.563
- Moved settings, logs, and cache into a shared PixelVaultData folder so library scans persist across version upgrades.
- Added one-time migration of existing cache and settings from older version folders into the shared data location.
- Library metadata indexing now stays consistent when you open a newer build of the app.

## 0.562
- Added support for Xbox direct-export screenshot names like Game-2026_03_21-03_53_32.png during import detection.
- Updated Xbox timestamp parsing so direct-export files pull their capture time from the filename correctly.
- Direct Xbox exports now flow through import and destination sorting the same way as the auto-uploaded Xbox screenshots.

## 0.561
- Stabilized the library metadata index so folder classifications no longer fall back to Other just from reopening the app.
- Folder and file groupings now keep the last scanned platform label until you explicitly rescan the library or a folder.

## 0.560
- Added a dedicated PC platform checkbox to the manual and library metadata editor.
- Added an Other platform option with a required custom platform name field for manual platform tagging.
- Updated platform grouping and metadata indexing so custom platform tags and direct PC tags are recognized across the app.

## 0.550
- The Game Library now creates virtual per-console entries, so the same game can appear separately as Steam, PS5, Xbox, Multiple Tags, or Other without changing the physical NAS folder layout.
- Library previews and library metadata edits now operate on the selected virtual console view instead of every file in the physical folder.
- Scan Folder and selection refresh now preserve the current console-specific view more reliably.
## 0.540
- Added a persistent library metadata index so console grouping can be driven by embedded tags without rescanning the whole library on every open.
- Added Scan Library in the library header and Scan Folder in the preview banner so you can index everything or just the selected game folder on demand.
- Import sorting, library metadata edits, and undo now keep the library index in sync so new changes show up faster in the browser.
## 0.532
- Opening the Game Library no longer tries to fetch cover art on the UI thread before the window appears.
- Library folder grouping now uses the cached folder platform label first instead of rescanning every folder immediately on open.
- The explicit Fetch Covers button still handles Steam cover downloads when you want them.
## 0.531
- Library metadata editing now applies only to the files you actually select instead of silently targeting the whole folder.
- The library metadata editor now opens with a single visible selection and a clearer selected-state highlight in the left file list.
- Library console badges and library-browser grouping now refresh correctly after console-tag edits, so Steam/PS5/Xbox changes stop lingering under Other.
## 0.530
- Replaced the broken library separator glyph with a plain pipe so folder details read cleanly.
- Grouped the Game Library folders into collapsible Steam, PS5, Xbox, Multiple Tags, and Other sections.
- Increased the library folder art size a bit and tightened the caption text underneath for a cleaner browse view.

## 0.520
- Added an Edit Metadata action to the Game Library banner so existing folders can be updated without going back through intake.
- Reused the batch metadata editor for library files, including comments, tags, console tags, custom capture time, and game-title renaming.
- Library edits now reorganize renamed captures into the proper game folder automatically after metadata changes.

## 0.510
- Reorganized the main page buttons into clearer import, library, and utility groups.
- Promoted View Logs into the header alongside Settings and Changelog for faster access.
- Tightened the button labels so actions read more clearly at a glance.

## 0.501
- Wired destination sorting into both import paths so files are grouped into game folders automatically after import.
- Kept the Sort Destination button for re-running the organizer manually later.

## 0.500
- Switched the default intake and library paths to the NAS-based Game Capture Uploads and Game Captures locations.
- Added built-in destination sorting based on the existing PowerShell folder rules, using the current Destination setting.
- Added Undo Last Import so the most recent moved files can be sent back to their source folders.

## 0.420
- Expanded the main window so the workflow buttons stay on one line more reliably and the preview area has more vertical room.
- Switched the main workflow canvas to a cleaner white treatment while keeping cards and panels visually separated.
- Refined button styling with stronger shadows, a green Process with Comments action, and light-gray Open Sources/Open Destination buttons.

## 0.410
- Added support for multiple source folders in Settings so intake can scan across more than one location.
- Updated preview, process, manual intake, and move workflows to read from every configured source folder.
- Added an Open Sources action to open each configured intake folder from the main screen.

## 0.396
- Hardened manual intake date handling so the custom date picker no longer silently falls back to today's date when nothing is selected.
- Manual intake now opens with all unmatched files selected by default, making bulk date and tag edits apply more predictably.
- The finish action now re-applies the current custom date/time to the selected manual items before processing.

## 0.395
- Moved the Changelog button next to Settings and added an in-app changelog reader window instead of opening the markdown file externally.
- Simplified the manual multi-select preview art so it shows only the selected count.
- Fixed the manual title field so typing spaces or other edits no longer jumps the cursor back to the beginning.

## 0.390
- Added changelog tracking and a main-window button to open it.
- Updated Manual Intake so the badge reflects the selected console tag instead of always showing Manual.
- Manual Intake console tags are now mutually exclusive and separated visually from the Game Photography tag.
- Manual Intake now supports multi-select editing so shared game names, tags, dates, and comments can be applied to multiple unmatched files at once.
- Multi-select preview now switches to a stacked multiple-image placeholder with the selected item count.

## 0.370
- Added Steam, PS5, and Xbox console-tag checkboxes next to the Game Photography option in the review popup.
- Refined the main workflow layout with a lighter Preview button, consistent button sizing, stronger button shadows, and white content cards.

































## 0.809
- Moved the default Library split closer to center so the browser and detail panes start from a more balanced layout.
- Expanded the responsive size ranges for folder covers and screenshot tiles so both sides can shrink and grow more naturally.
- Reworked live pane resizing to use stepped responsive tile sizes plus short coalesced refresh timers, reducing drag lag while keeping the layout responsive.

## 0.808
- Switched the Library splitter to live resize mode so dragging updates both panes continuously instead of waiting for mouse-up.
- Removed the extra `Grid` button from the screenshot header.
- Tightened the pane-resize listeners and right-side tile width budget so folder covers and screenshot tiles resize in real time and stay within the visible pane.

## 0.807
- Fixed the center Library splitter so it is a real `GridSplitter` child of the main content grid and actually resizes the browser and detail panes.
- Removed `Recently Played` from the Library filter strip to keep the top controls tighter and closer to the design reference.
- Updated the release docs so `CURRENT_BUILD`, `HANDOFF`, and the changelog now point at the live `0.807` build.

## 0.806
- Moved `Game Index`, `Photo Index`, and `Filename Rules` into the top Library header and restyled them to match the main utility-button family.
- Replaced the old Library sort dropdown with design-style sort buttons and removed the folder-size and capture-size sliders.
- Made the center splitter drive real responsive sizing so the folder cards and screenshot tiles resize with the pane widths.

## 0.805
- Reworked the Library shell to match the Figma export more directly with a true full-width header, flatter split panes, and a visible center divider.
- Reorganized the left browser pane so search, controls, and the gallery grid read like a proper library browser instead of a utility form.
- Simplified the right detail pane into a cleaner showcase layout so the selected-game header and screenshot area feel much closer to the design reference.

## 0.799
- Gave the Library a cleaner split-pane shell with a dedicated top action bar, calmer panel chrome, and a visible draggable divider between the browser and detail views.
- Restyled the selected-game header to feel more intentional and cover-first, with a stronger hero frame and cleaner supporting metadata/actions.
- Updated the library folder cards to use a taller image-led presentation with quieter captions so the browse surface feels closer to the Figma design direction.

## 0.798
- Stopped normal SQLite index writes from invalidating the library folder cache stamp, so startup no longer needlessly triggers full NAS-backed folder-cache rebuilds.
- Persisted downloaded cover-art paths back into cached folder info and saved game-index rows so fetched covers stick across refreshes and restarts.

## 0.797
- Made the Filename Rules window scale more gracefully at smaller sizes with taller, scrollable editor and rules-list regions.
- Saving a filename rule now clears the editor and deselects the lists so reopening a custom rule is a deliberate edit action.
- Double-clicking a custom rule now loads it back into the editor for follow-up edits.

## 0.796
- Removed the duplicate bottom Save and Close buttons from the Filename Rules window, leaving those actions only in the top toolbar.
- Recent unmatched filename samples now leave the sample list once you create a rule from them.
- Expanded the custom and built-in rule lists into usable visible areas instead of header-only placeholders.

## 0.795
- Fixed the Edit Metadata dialog open path so it no longer tries to do its initial selection and preview work before the window is shown.
- Moved the first metadata preview decode onto the async image-loading path to avoid blocking the dialog while it opens.
- Hardened the metadata dialog actions so it cannot close itself before the window is fully ready.

## 0.794
- Restored missing Steam AppIDs and SteamGridDB IDs in the shared SQLite game index from the legacy flat cache when available.
- Added a safety backfill during index migration so partially populated SQLite game-index data can recover external IDs instead of keeping them blank forever.

## 0.793
- Fixed the Filename Rules editor so switching between built-in rules and custom drafts updates the active selection correctly.
- Added consistent unsaved-change protection when closing the Filename Rules window, including the title-bar close button.
- Verified the rules workflow end to end with a live click-through covering built-in selection, disable override, new rule drafts, sample promotion, reload, save, and close behavior.

## 0.792
- Fixed the persistent data-root resolver so `PixelVault-current` uses the shared `C:\Codex\PixelVaultData` store instead of drifting into per-build settings, cache, and index files.
- Updated startup data migration to copy newer settings, cache, and log files forward into the shared data store instead of only filling missing files.
- Changed `Fetch Covers` in the Library to refresh only the selected folder by default, with an explicit confirmation before running a full-library cover refresh.
