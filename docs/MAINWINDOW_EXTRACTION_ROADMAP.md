# MainWindow extraction roadmap

This document is the **execution roadmap** for slicing responsibilities off `MainWindow`. It extends **Phase 3: Shrink MainWindow** in `C:\Codex\docs\ROADMAP.md` and aligns with `C:\Codex\docs\pixelvault_service_split_plan.txt` and `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md`.

**Principles and scope (tiered MainWindow bar, `ILibraryScanHost` as application port, `IFileSystemService` / async expectations):** `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md`. This roadmap stays the **how/where** checklist; that file is the **why/what counts as done** contract.

**Not a duplicate of `PERFORMANCE_FIX_PLAN.txt`:** that file tracks **Library performance** (virtualization, sort cost, batch reads, threading). This file tracks **where UI and orchestration code lives** (partials, hosts). The **service split** (`ILibraryScanner`, `IImportService`, …) is in `pixelvault_service_split_plan.txt` — see **Service split alignment** below.

**Notion (Project Wiki):** [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6)

**Not a scratchpad:** concrete slices and order of operations. Update this file when a slice ships or the plan changes; sync Notion if you track milestones there (`DOC_SYNC_POLICY.md`).

---

## Context

- `MainWindow` is a **partial** class spread across many files, but **`PixelVault.Native.cs` still holds the bulk of UI orchestration** (library browser, settings shell, intake preview, editors, photography flow, etc.).
- Several **services already exist** and are injected in the constructor (`ICoverService`, `IMetadataService`, `IIndexPersistenceService`, `IFilenameParserService`, `IFilenameRulesService`, **`ILibraryScanner`**, **`IImportService`**). Extraction means **moving glue code** out of the giant file, not reinventing those services.
- **Phases 1–2** of the product roadmap (tests, UI-thread responsiveness) should stay **in progress or done** before aggressive slicing; extraction is easier when call sites are already background-safe.

---

## Current partial map (line counts are approximate)

| File | ~Lines | Role |
|------|--------|------|
| `PixelVault.Native.cs` | ~3,330 | Constructor, fields, intake preview wiring, logging, image cache, `QueueImageLoad`, Steam/cover/library-cache helpers, settings path textbox helpers; scan monitor + import summary dialogs moved to library/import partials (Apr 2026) |
| `Import/ImportWorkflow.cs` | ~970 | Import / manual intake / rename-move-sort orchestration (move-to-destination, undo manifest, destination sort → **`ImportService`**) |
| `Import/MainWindow.ImportSummaryDialogs.cs` | ~155 | `BuildImportSummaryLines`, `ShowImportSummaryWindow` (`MainWindow` partial) |
| `UI/FilenameConventionEditor.cs` | ~315 | Filename rule helpers + `OpenFilenameConventionEditor` shell |
| `UI/Editors/FilenameConventionEditorWindow.cs` | ~925 | Filename rules editor UI (`FilenameConventionEditorWindow.Show` + `FilenameConventionEditorServices`, Phase D1) |
| `UI/Editors/GameIndexEditorHost.cs` | ~410 | Game index editor UI (`GameIndexEditorHost.Show` + `GameIndexEditorServices`, Phase D2) |
| `UI/Editors/PhotoIndexEditorHost.cs` | ~370 | Photo index editor UI (`PhotoIndexEditorHost.Show` + `PhotoIndexEditorServices`, Phase D3) |
| `MediaTools/MediaToolHelpers.cs` | ~700 | Exe runners, FFmpeg helpers |
| `UI/Library/LibraryWorkspaceContext.cs` | ~175 | Library root + folder listing + file-tag cache facade (Phase E2) |
| `UI/Library/MainWindow.LibraryBrowserOrchestrator.cs` | ~390 | `ShowLibraryBrowserCore` shell: nav, panes, delegates wiring into folder/detail partials (Phase E1) |
| `UI/Library/MainWindow.LibraryBrowserOrchestrator.Selection.cs` | ~150 | Detail multi-select helpers + shared sort/group pill chrome; replaces large inline lambdas in **`ShowLibraryBrowserCore`** (E2 follow-on, Apr 2026) |
| `UI/Library/MainWindow.LibraryMetadataScan.cs` | ~195 | **`ShowLibraryMetadataScanWindow`** — metadata index scan progress UI (`MainWindow` partial; Apr 2026) |
| `UI/Settings/MainWindow.SettingsShell.cs` | ~310 | `BuildUi`, `BuildSettingsSummary`, `BuildDiagnosticsSummary`, `ShowPathSettingsWindow`, `ShowSettingsWindow` (Phase F1); library-aligned dark theme; no import/maintenance panel |
| `UI/Photography/MainWindow.PhotographyAndSteam.cs` | ~265 | `ShowPhotographyGallery`, `ShowSteamAppMatchWindow` (Phase F2) |
| `UI/LibraryVirtualization.cs` | ~570 | Virtualized rows / scroll hosts |
| `Services/Library/LibraryScanner.cs` | — | **`ILibraryScanner`**: metadata index scan, folder grouping, folder cache rebuild/cached load; uses **`IMetadataService`** + **`ILibraryScanHost`**. |
| `Services/Import/ImportService.cs` | — | **`IImportService`**: move-to-destination, undo manifest, sort destination root, undo moves (`ImportServiceDependencies`). |
| `Indexing/LibraryMetadataIndexing.cs` | ~500 | Metadata index building (scan/save paths delegate to **`LibraryScanner`** where split) |
| `Indexing/GameIndexCore.cs` | ~370 | Game index core |
| `Indexing/LibraryFolderIndexing.cs` | ~290 | Folder inventory / library folders |
| `Indexing/GameIndexFolderAlignment.cs` | ~300 | Folder alignment |
| `Metadata/MetadataHelpers.cs` | ~345 | Metadata helpers |
| `Metadata/LibraryMetadataEditing.cs` | ~195 | Library metadata edit flows |
| `Indexing/GameIndexEditorOperations.cs` | ~200 | Game index editor ops |
| `Storage/IndexDatabaseStorage.cs` | ~40 | Cache paths / stamps |

The **priority** is to shrink **`PixelVault.Native.cs`**, not to collapse partials into fewer files.

---

## Principles

1. **Behavior first:** No user-visible change per slice unless the slice explicitly calls for it (bugfix).
2. **One vertical slice per milestone:** Compile, run manual golden path, then merge.
3. **Prefer new types over new partials:** `LibraryBrowserHost`, `IntakePreviewCoordinator`, `SettingsShellBuilder`, etc., taking **explicit dependencies** (services, `Func<>` for log, dispatcher, paths).
4. **MainWindow stays the WPF `Window` root:** It creates hosts, wires events, and forwards lifecycle; it does not need to own every helper.
5. **No full MVVM/DI container in this roadmap:** Thin code-behind plus plain classes is enough until Phase 3 exit criteria in `ROADMAP.md` are met.

---

## Phase A — Inventory and seams (short)

**Goal:** Know what to extract next without a big bang.

- List **regions** in `PixelVault.Native.cs` (Library, Settings, Intake, Review, Photography, Game Index UI, etc.).
- For each region, note: **Dispatcher usage**, **service calls**, **shared fields** (`status`, `libraryRoot`, caches).
- Mark **already-off-thread** vs **must stay on UI thread**.

**Exit:** A short **Seams** subsection (below) filled in or a linked checklist in Notion.

---

## Phase B — Low-risk extractions (first code moves)

**Goal:** Build muscle memory and reduce line count without touching the Library monster first.

| Slice | Move to | Notes |
|-------|---------|--------|
| B1 | `Infrastructure/` or `UI/Helpers/` | Pure path/UI helpers that do not touch `this` state (or only need `appRoot` / `dataRoot` passed in). |
| B2 | `UI/Progress/` or shared helper | **Progress window factory** used by import / scan (single place for layout + cancel token). |
| B3 | `UI/ChangelogWindow.cs` (or similar) | Self-contained `ShowChangelogWindow` if it is mostly UI construction. |

**Exit:** Measurable line drop in `PixelVault.Native.cs`; no regression on import or changelog.

**Progress:** **B3** — Changelog UI lives in `UI/ChangelogWindow.cs` (`ChangelogWindow.ShowDialog`); Settings “Changelog” button calls it with `AppVersion` and `changelogPath`.

**B1** — `UI/UiBrushHelper.cs` provides `FromHex`; `MainWindow.Brush` delegates to it (same call sites); `ChangelogWindow` hex brushes use the helper.

**B2** — `UI/Progress/WorkflowProgressWindow.cs` provides `WorkflowProgressWindow.Create` + `WorkflowProgressView` (log ring buffer, shared layout). Used by import `RunBackgroundWorkflowWithProgress`, `ShowLibraryMetadataScanWindow`, `RunLibraryMetadataWorkflowWithProgress`, and library cover refresh.

---

## Phase C — Intake and review surfaces

**Goal:** Large, somewhat isolated UI flows out of the main file.

| Slice | Move to | Notes |
|-------|---------|--------|
| C1 | `UI/Intake/IntakePreviewWindow.cs` (new type) | `ShowIntakePreviewWindow` + render delegates; `MainWindow` passes `LoadIntakePreviewSummaryAsync`, `OpenManualIntakeWindow`, etc. |
| C2 | `UI/Intake/MetadataReviewWindow.cs` | Metadata review / comment flow if it is a discrete block. |
| C3 | Tie-in | Ensure **Steam rename / move** paths stay covered by tests or manual checklist after C1. |

**Exit:** Intake preview and review are **owned by types under `UI/`**; `MainWindow` only opens them and supplies callbacks.

**Progress:** **C1** — Intake Preview modal lives in `UI/Intake/IntakePreviewWindow.cs` (`IntakePreviewWindow.Show` + `IntakePreviewServices`). `MainWindow.ShowIntakePreviewWindow` wires `LoadIntakePreviewSummaryAsync`, `OpenSourceFolders`, `OpenManualIntakeWindow`, `RenderPreview` / `RenderPreviewError`, logging, and shared helpers (`Btn`, `PreviewBadgeBrush`, `PlatformGroupOrder`, etc.).

**C2** — Pre-import **metadata review** (comments, platform tags, delete-before-processing) lives in `UI/Intake/MetadataReviewWindow.cs` (`MetadataReviewWindow.Show` + `MetadataReviewServices`). `MainWindow.ShowMetadataReviewWindow` passes `Btn`, `PreviewBadgeBrush`, `LoadImageSource`, and `GamePhotographyTag`. Note: “Import and comment” currently prefers Import-and-Edit when upload files qualify; `ShowMetadataReviewWindow` is not on the hot path today but remains the home for `List<ReviewItem>` review UI.

**C3** — **Steam rename / move tie-in:** `SteamRenamePathMappingTests` exercises `SteamImportRename` (`ApplySteamRenameMapToReviewItems`, `ApplySteamRenameMapToManualMetadataItems`, `ResolveTopLevelPathsAfterSteamRename`); rename loop is **`IImportService.RunSteamRename`** (`Services/Import/ImportService.cs`). Manual path: **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** section *Phase C3 — Intake UI extraction + Steam rename / move glue*.

---

## Phase D — Editors and conventions

**Goal:** Large editor partials become **owned windows or presenters**.

| Slice | Move to | Notes |
|-------|---------|--------|
| D1 | Split or rename `FilenameConventionEditor.cs` | Already a partial; consider **`FilenameConventionEditorWindow`** as a non-partial `Window` with ctor dependencies. |
| D2 | `UI/Editors/GameIndexEditorHost.cs` | Game index editor window construction + navigation. |
| D3 | `UI/Editors/PhotoIndexEditorHost.cs` | Same for photo index. |

**Exit:** `MainWindow` calls `new XxxEditorWindow(deps)` instead of hosting 500+ lines of editor UI inline.

**Progress:** **D1** — Filename rules editor UI lives in `UI/Editors/FilenameConventionEditorWindow.cs` (`Show` + `FilenameConventionEditorServices`). `MainWindow.OpenFilenameConventionEditor` keeps singleton activation, library check, and catch; it wires rules/parser services, status, log, preview refresh, `Btn`, `NormalizeConsoleLabel`, and `CleanTag`. Static `FilenameParserService` helpers stay called by name for pattern text/regex (instance `IFilenameParserService` is used for `InvalidateRules`).

**D2** — Game index editor UI lives in `UI/Editors/GameIndexEditorHost.cs` (`GameIndexEditorHost.Show` + `GameIndexEditorServices` + `GameIndexBackgroundIntArrayDelegate` for `RunBackgroundWorkflowWithProgress<int[]>`). `MainWindow.OpenGameIndexEditor` keeps singleton activation, library check, and catch; it wires load/save rows, merge keys, ID resolution, `ThrowIfWorkflowCancellationRequested`, and `OpenFolder`.

**D3** — Photo index editor UI lives in `UI/Editors/PhotoIndexEditorHost.cs` (`PhotoIndexEditorHost.Show` + `PhotoIndexEditorServices`). `MainWindow.OpenPhotoIndexEditor` keeps singleton activation, library check, and catch; it wires load/save, embedded-tag read / console label / metadata stamp, `OpenFolder`, `OpenWithShell`, and normalize/clean/parse helpers (`ReadEmbeddedKeywordTagsDirect` is wired as `path => ReadEmbeddedKeywordTagsDirect(path)` because of the optional `CancellationToken` overload).

---

## Phase E — Library browser (largest win, highest risk)

**Goal:** `ShowLibraryBrowser` and related state become a **dedicated host** class.

| Slice | Move to | Notes |
|-------|---------|--------|
| E1 | `UI/Library/LibraryBrowserHost.cs` (or `LibraryShellController`) | Owns grid split, folder tiles, detail pane, search, toolbar actions; receives `ILibraryServices` facade or individual services. |
| E2 | Facade | `ILibrarySession` or `LibraryWorkspaceContext`: library root, caches, `refresh` actions, **document thread affinity** for image cache. |
| E3 | Virtualization | Keep using `LibraryVirtualization.cs`; host only **calls** it. **Done** — no extra file move required. |

**Exit:** **Phase 3 definition of done** from `ROADMAP.md`: new nontrivial Library behavior is added in **`LibraryBrowserHost` (or services)**, not at the bottom of `PixelVault.Native.cs`.

**Dependency:** Prefer completing **Phase 2** responsiveness items that touch Library (debounce, image load paths) before E1, or do E1 behind a thin wrapper so background work stays correct.

**Progress (E1):** `ShowLibraryBrowser` moved from `PixelVault.Native.cs` to **`UI/Library/MainWindow.LibraryBrowserOrchestrator.cs`** (was **`MainWindow.LibraryBrowser.cs`**) as a **`MainWindow` partial**; entry is **`LibraryBrowserHost`** (`UI/Library/LibraryBrowserHost.cs`) delegating to **`MainWindow.ShowLibraryBrowserCore`**. Next: **`ILibrarySession` / facade** per E2 when Library is touched again.

**Progress (E2):** **`UI/Library/LibraryWorkspaceContext.cs`** — owns **folder image listing cache** and **file-tag cache** (formerly `fileTagCache` / `fileTagCacheStamp` / `fileTagCacheSync`), exposes **`LibraryRoot`** (via `LibraryWorkspaceRoot`) and **`UiDispatcher`**. `RemoveCachedFolderListings`, `ClearFolderImageListings`, `GetCachedFolderImages`, `TryGetCachedFileTags`, `SetCachedFileTags`, `RemoveCachedFileTagEntries`, and `ClearFileTagCache` live on the context; **`MainWindow`** keeps thin wrappers for call sites in partials. **`ILibrarySession`** / **`LibrarySession`** aggregate **`Workspace`**, **`Scanner`**, **`FileSystem`**, **`LibraryRoot`**, and **`PersistGameIndexRows`** ( **`IGameIndexEditorAssignmentService`** ); **`ShowLibraryBrowserCore`** (**`MainWindow.LibraryBrowserOrchestrator.cs`**) uses **`librarySession.PersistGameIndexRows`** after detail-pane metadata repair. **`MainWindow`** constructs **`librarySession`** after **`gameIndexEditorAssignmentService`**. Bitmap decode cache remains on **`MainWindow`** for now.

### E2 follow-on — session facade scope and UI-adjacent async (Mar 2026)

Review feedback matches the plan: **`LibraryBrowserHost`** is intentionally a **thin entry** into **`ShowLibraryBrowserCore`**. The next **substantive** Library slice is **not** moving code between partials for its own sake, but shaping a **library/session facade** (working name **`ILibrarySession`**, or an interface over **`LibraryWorkspaceContext`** plus scan/refresh seams) that:

- Surfaces **library root**, **folder refresh contracts**, and explicit rules for **thread-pool-safe** work vs **UI-thread-only** work (e.g. bitmap decode cache, **`Dispatcher.BeginInvoke`** boundaries).
- Cuts down **behavior expressed as deep nested `Action` delegates** on **`MainWindow`**, which is the main place **async UI races** show up (stale task completions, double window open, combo refill after close).
- Lets **`LibraryBrowserHost`** (and later, **`ShowLibraryBrowserCore`**) depend on **facade + services** (`ILibraryScanner`, `IMetadataService`, persistence) instead of **`MainWindow`** as a grab-bag of helpers.

**Testing:** Unit coverage is solid for core logic; **UI-adjacent async** (pool load → marshal → show window or refill controls) is the weakest area. Mitigations, cheapest first:

1. **Regression tests** on **non-WPF** paths: e.g. **`LoadGameIndexEditorRowsCore`**, merge/save invariants, version-token “ignore stale completion” behavior where logic can be isolated.
2. A small **internal helper** (e.g. run work on **`TaskScheduler.Default`**, continue with **`dispatcher.BeginInvoke`**, unified fault logging) **used by hosts** and covered by tests for ordering and exceptions.
3. **Manual steps** in **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** for Library → Game Index and manual metadata until (1)–(2) cover the riskiest flows.

**Coordination:** Prefer **not** pairing a large facade pass with heavy **`IImportService`** / **`ImportWorkflow`** edits in the same window; see **`docs/SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`**.

**Progress (E2 slice, Apr 2026):** Scoped library cover refresh (progress window, cancel, folder reload) moved from an inline delegate in **`ShowLibraryBrowserCore`** to **`RunLibraryBrowserScopedCoverRefresh`** in **`UI/Library/MainWindow.LibraryBrowserOrchestrator.CoverRefresh.cs`** (**`MainWindow`** partial); orchestrator assigns **`runScopedCoverRefresh`** to call it (no intended behavior change).

**Progress (E2 slice, continued):** Additional **`ShowLibraryBrowserCore`** blocks moved to **`MainWindow`** partials — detail delete + metadata editor + “edit metadata for folder” in **`MainWindow.LibraryBrowserOrchestrator.DetailCommands.cs`**; folder list refresh, snapshot prefill, and metadata scan callback in **`MainWindow.LibraryBrowserOrchestrator.FolderData.cs`**; intake review badge refresh in **`MainWindow.LibraryBrowserOrchestrator.IntakeBadge.cs`**. Orchestrator keeps thin delegates (no intended behavior change).

**Progress (E2 slice, Apr 2026 — selection + chrome):** **`UI/Library/MainWindow.LibraryBrowserOrchestrator.Selection.cs`** — named factories/helpers for **`getVisibleDetailFilesOrdered`**, **`getSelectedDetailFiles`**, **`updateDetailSelection`**, **`refreshDetailSelectionUi`** (**`LibraryBrowserCreate*`** / **`LibraryBrowserApplyDetailSelectionChange`**) plus **`LibraryBrowserApplySortGroupPillState`** for sort/group buttons. **`ShowLibraryBrowserCore`** wires these instead of large nested delegates (no intended behavior change).

**Progress (E3):** Library virtualization stays in **`UI/LibraryVirtualization.cs`**; **`ShowLibraryBrowser`** already consumes it via **`MainWindow`** partial methods — **no further structural change** for this slice.

**Progress (`PixelVault.Native.cs` shrink, Apr 2026):** **`ShowLibraryMetadataScanWindow`** moved to **`UI/Library/MainWindow.LibraryMetadataScan.cs`** (**`LibrarySession`** still receives the same partial method reference). **`BuildImportSummaryLines`** + **`ShowImportSummaryWindow`** moved to **`Import/MainWindow.ImportSummaryDialogs.cs`**.

---

## Phase F — Settings, photography, misc windows

**Goal:** Remaining top-level flows.

| Slice | Move to | Notes |
|-------|---------|--------|
| F1 | `UI/Settings/MainWindow.SettingsShell.cs` (or `SettingsShellBuilder`) | `BuildUi`, path summary, top buttons, **`ShowPathSettingsWindow`**, **`ShowSettingsWindow`**. **Done** as `MainWindow` partial. |
| F2 | `UI/Photography/MainWindow.PhotographyAndSteam.cs` (roadmap name: `PhotographyWindowHost`) | **`ShowPhotographyGallery`** + **`ShowSteamAppMatchWindow`** as `MainWindow` partial. |
| F3 | `Services/Config` (optional) | Persisted settings read/write if still embedded in `MainWindow`. |

**Exit:** `PixelVault.Native.cs` is mostly **constructor wiring**, `MainWindow` lifecycle, and delegation to hosts.

**Progress (F1):** **`UI/Settings/MainWindow.SettingsShell.cs`** — **`BuildUi`**, **`BuildSettingsSummary`**, **`BuildDiagnosticsSummary`**, **`ShowPathSettingsWindow`**, and **`ShowSettingsWindow`** (modal wrapper + field restore on close). **Apr 2026:** Library-first settings chrome (dark theme aligned with Library); import actions and intake preview pane removed from Settings (import lives on Library toolbar).

**Progress (F2):** **`UI/Photography/MainWindow.PhotographyAndSteam.cs`** — **`ShowPhotographyGallery`** (uses **`libraryWorkspace.LibraryRoot`** for paths) and **`ShowSteamAppMatchWindow`** (manual metadata Steam search picker).

**Progress (F3):** **`ISettingsService`** / **`SettingsService`** already own settings load/save (`Services/Config/`). **`ShowSettingsWindow`** and path UI remain on **`MainWindow`**; no further F3 slice required unless settings UI moves out of `PixelVault.Native.cs`.

---

## Service split alignment (parallel to phases A–F)

Tracked in **`docs/pixelvault_service_split_plan.txt`** (different numbering from extraction phases here).

| Plan phase | Status (Apr 2026) | Notes |
|------------|-------------------|--------|
| Phase 1 Settings | Done | `ISettingsService` |
| Phase 2 Metadata | Done | `IMetadataService` |
| Phase 3 Index persistence | Done | `IIndexPersistenceService` |
| Phase 4 Library scan | Done (current scope) | `ILibraryScanner` + `ILibraryScanHost`; `ShowLibraryBrowser` still **`MainWindow` partial** |
| Phase 5 Import | Initial slice done | `IImportService` — moves, undo manifest, destination sort; rename/metadata/delete steps still in **`ImportWorkflow`** / **`MainWindow`** |
| Phase 6+ Covers / composition root | Not this roadmap’s focus | See service split plan |

**Extraction roadmap vs service split:** Moving **`ShowLibraryBrowser`** to a non-`Window` host (Phase E1 “ideal”) is **orthogonal** to `LibraryScanner`; both can proceed independently.

---

## Milestone targets (optional metrics)

| Milestone | Rough target |
|-----------|----------------|
| After B | `PixelVault.Native.cs` −300 to −800 lines |
| After C | −800 to −1,500 additional |
| After D | −500 to −1,200 additional |
| After E | −2,000 to −4,000 additional (largest variable) |
| After F | Orchestration file **under ~2,500 lines** (stretch; adjust as needed) |

Treat numbers as **guides**, not gates.

---

## Anti-goals (this roadmap)

- Rewriting Library UI layout or visual design during extraction.
- Introducing a **DI container** or **full MVVM** as a prerequisite for every slice.
- Merging partials into **one** other mega-file.

---

## Suggested order (summary)

1. **Phase A** — seam inventory (short).  
2. **Phase B** — progress/changelog/helpers.  
3. **Phase C** — intake preview + review.  
4. **Phase D** — index / filename editors.  
5. **Phase E** — library browser host (after responsiveness work is healthy).  
6. **Phase F** — settings + photography + stragglers.

---

## Appendix: Seams inventory (WIP)

Fill this in during **Phase A** (region name, primary file lines if known, Dispatcher?, shared fields, services used).

| Region | Dispatcher / UI thread | Shared `MainWindow` state | Services |
|--------|-------------------------|---------------------------|----------|
| Library browser | Yes | `libraryWorkspace`, bitmap decode cache; orchestration split across **`LibraryBrowserOrchestrator`** (+ **`PaneEvents`**, **`NavChromeAndToolbar`**) | **`ILibrarySession`**, cover, metadata, **`libraryScanner`**, index persistence |
| Import workflow | Progress on UI; work on pool | `sourceRoot`, `destinationRoot`, `conflictBox` | **`importService`**, **`metadataService`**, **`libraryScanner`** |
| | | | |

---

## References

- `C:\Codex\docs\ROADMAP.md` — Phase 3 strategy and definition of done  
- `C:\Codex\docs\pixelvault_service_split_plan.txt` — service map and folder ideas  
- `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md` — threading, HTTP, catches  
- `C:\Codex\docs\PERFORMANCE_TODO.md` — short active checklist for Library / thread work  
- `C:\Codex\docs\PERFORMANCE_FIX_PLAN.txt` — Library **performance** phases and completion log (not the same as extraction phases A–F)  
- `C:\Codex\PixelVaultData\TODO.md` — rolling tasks; link individual slices here when active  
