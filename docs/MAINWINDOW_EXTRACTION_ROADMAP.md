# MainWindow extraction roadmap

This document is the **execution roadmap** for slicing responsibilities off `MainWindow`. It extends **Phase 3: Shrink MainWindow** in `C:\Codex\docs\ROADMAP.md` and aligns with `C:\Codex\docs\pixelvault_service_split_plan.txt` and `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md`.

**Notion (Project Wiki):** [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6)

**Not a scratchpad:** concrete slices and order of operations. Update this file when a slice ships or the plan changes; sync Notion if you track milestones there (`DOC_SYNC_POLICY.md`).

---

## Context

- `MainWindow` is a **partial** class spread across many files, but **`PixelVault.Native.cs` still holds the bulk of UI orchestration** (library browser, settings shell, intake preview, editors, photography flow, etc.).
- Several **services already exist** and are injected in the constructor (`ICoverService`, `IMetadataService`, `IIndexPersistenceService`, `IFilenameParserService`, `IFilenameRulesService`). Extraction means **moving glue code** out of the giant file, not reinventing those services.
- **Phases 1–2** of the product roadmap (tests, UI-thread responsiveness) should stay **in progress or done** before aggressive slicing; extraction is easier when call sites are already background-safe.

---

## Current partial map (line counts are approximate)

| File | ~Lines | Role |
|------|--------|------|
| `PixelVault.Native.cs` | ~7,290 | Constructor, fields, Library UI, Settings, intake preview delegates, metadata review delegate, photography, Steam matches, logging, image cache, many helpers |
| `Import/ImportWorkflow.cs` | ~970 | Import / manual intake / rename-move-sort orchestration |
| `UI/FilenameConventionEditor.cs` | ~315 | Filename rule helpers + `OpenFilenameConventionEditor` shell |
| `UI/Editors/FilenameConventionEditorWindow.cs` | ~925 | Filename rules editor UI (`FilenameConventionEditorWindow.Show` + `FilenameConventionEditorServices`, Phase D1) |
| `MediaTools/MediaToolHelpers.cs` | ~700 | Exe runners, FFmpeg helpers |
| `UI/LibraryVirtualization.cs` | ~570 | Virtualized rows / scroll hosts |
| `Indexing/LibraryMetadataIndexing.cs` | ~500 | Metadata index building |
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

**C3** — **Steam rename / move tie-in:** `SteamRenamePathMappingTests` exercises `ApplySteamRenameMapToReviewItems`, `ApplySteamRenameMapToManualMetadataItems`, and `ResolveTopLevelPathsAfterSteamRename` (`ImportWorkflow.cs`). Manual path: **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** section *Phase C3 — Intake UI extraction + Steam rename / move glue*.

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

---

## Phase E — Library browser (largest win, highest risk)

**Goal:** `ShowLibraryBrowser` and related state become a **dedicated host** class.

| Slice | Move to | Notes |
|-------|---------|--------|
| E1 | `UI/Library/LibraryBrowserHost.cs` (or `LibraryShellController`) | Owns grid split, folder tiles, detail pane, search, toolbar actions; receives `ILibraryServices` facade or individual services. |
| E2 | Facade | `ILibrarySession` or `LibraryWorkspaceContext`: library root, caches, `refresh` actions, **document thread affinity** for image cache. |
| E3 | Virtualization | Keep using `LibraryVirtualization.cs`; host only **calls** it. |

**Exit:** **Phase 3 definition of done** from `ROADMAP.md`: new nontrivial Library behavior is added in **`LibraryBrowserHost` (or services)**, not at the bottom of `PixelVault.Native.cs`.

**Dependency:** Prefer completing **Phase 2** responsiveness items that touch Library (debounce, image load paths) before E1, or do E1 behind a thin wrapper so background work stays correct.

---

## Phase F — Settings, photography, misc windows

**Goal:** Remaining top-level flows.

| Slice | Move to | Notes |
|-------|---------|--------|
| F1 | `UI/Settings/SettingsShellBuilder.cs` | `BuildUi`, path summary cards, top buttons. |
| F2 | `UI/Photography/PhotographyWindowHost.cs` | Photography / Steam matches if they remain large standalone surfaces. |
| F3 | `Services/Config` (optional) | Persisted settings read/write if still embedded in `MainWindow`. |

**Exit:** `PixelVault.Native.cs` is mostly **constructor wiring**, `MainWindow` lifecycle, and delegation to hosts.

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
| *(example)* Library browser | Yes | `folderImageCache`, `libraryRoot` | cover, metadata, index persistence |
| | | | |

---

## References

- `C:\Codex\docs\ROADMAP.md` — Phase 3 strategy and definition of done  
- `C:\Codex\docs\pixelvault_service_split_plan.txt` — service map and folder ideas  
- `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md` — threading, HTTP, catches  
- `C:\Codex\docs\PERFORMANCE_TODO.md` — Library / thread work that overlaps Phase E  
- `C:\Codex\PixelVaultData\TODO.md` — rolling tasks; link individual slices here when active  
