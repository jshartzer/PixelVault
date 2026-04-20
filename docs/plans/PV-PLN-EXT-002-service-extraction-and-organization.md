# PV-PLN-EXT-002 — Service extraction & organization (post–MainWindow split)

**Status:** Draft plan (Apr 2026)  
**Supersedes nothing:** This is a **forward-looking execution sequence**. It complements finished UI extraction (**`MAINWINDOW_EXTRACTION_ROADMAP.md`** Phases A–F) and ongoing **Phase 7** in **`pixelvault_service_split_plan.txt`**.

---

## 1. Purpose

After the **MainWindow** surface was split into partials, hosts, and bridges, the app is easier to navigate **by file**, but **ownership** is still mixed:

- **`MainWindow`** and **`ImportWorkflow`** remain **orchestration shells** with many **delegates** into window methods.
- Several **services exist** (`ILibraryScanner`, `IImportService`, `ICoverService`, `IMetadataService`, `IIndexPersistenceService`, `ISettingsService`, `IFileSystemService`, …) but **glue code** and **policy** still leak into UI partials.
- **`Services/`** has grown **by domain**; the layout is mostly right, yet **discoverability** and **one-place wiring** could improve **after** extraction stabilizes.

This plan defines:

1. **Phase A — Extraction** (do first): move **behavior** and **workflow I/O** behind stable seams; shrink **`ILibraryScanHost`** / window callbacks **incrementally**.
2. **Phase B — Organization** (do second): **folder/namespace**, optional **facades**, **composition-root** clarity, and doc map updates—**without** merging unrelated domains into god-types.

---

## 2. Principles (contract)

Aligned with **`ARCHITECTURE_REFACTOR_PLAN.md`**:

| Principle | Meaning for this plan |
|-----------|------------------------|
| **Extract before reorganize** | No large **folder-only** reshuffles that touch dozens of files until **nearby** extraction work is merged or explicitly deferred. |
| **Behavior parity** | User-visible behavior unchanged unless a slice documents an exception (bugfix or intentional change). |
| **Small vertical slices** | One milestone = compile + targeted regression (golden path, import, library scan as relevant). |
| **One owner for integration** | **`PixelVault.Native.cs`**, **`ImportWorkflow.cs`**, and **new service interfaces** follow **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`** (one integration commit owner). |
| **No DI container requirement** | Explicit constructors and a **documented** composition section remain the default. |
| **Domain boundaries stay separate** | Do **not** “consolidate” covers + indexing + import into one **Manager** class; use **facades** only inside a **single subsystem** (e.g. Intake) if call sites are noisy. |

---

## 3. Current baseline (snapshot)

**Already landed (see `pixelvault_service_split_plan.txt`):**

- Config: `ISettingsService` / `SettingsService`
- Metadata: `IMetadataService`
- Persistence: `IIndexPersistenceService`
- Library: `ILibraryScanner`, `ILibraryScanHost`, `ILibrarySession`, `IGameIndexService`
- Import: `IImportService` + `ImportWorkflow` shell
- Covers: `ICoverService`
- IO: `IFileSystemService`

**Still concentrated in the shell (extraction targets):**

- Large regions of **`PixelVault.Native.cs`** (constructor wiring, helpers, Steam/cover paths, settings textbox helpers, image cache, etc.).
- **`Import/ImportWorkflow.cs`** — orchestration and progress; more steps can move under **`IImportService`** with stable result models.
- **Metadata policy** and **edit orchestration** partially in **`Metadata/`** partials and **`MainWindow`**.
- **Operational logging** still mostly **`MainWindow.Log`** patterns; optional dedicated **`ILogService`** later.

**Organization targets (Phase B only):**

- **`Services/`** subfolders are already domain-scoped; optional **Intake** umbrella (`Services/Intake/*`) documentation or a thin **facade** if multiple entry points confuse callers.
- A short **`AppServices`** or **“composition”** region in **`MainWindow` ctor** (or a static factory type) so **construction order** is obvious—**documentation + structure**, not a framework.

---

## 4. Phase A — Extraction (ordered milestones)

Work items are **sequenced by leverage and merge risk**. Adjust order only when **`HANDOFF.md`** or a spike shows a hard dependency.

### A.1 — Composition clarity (low risk, optional first) — **landed**

**Goal:** Readers can see **which services exist** and **construction order** without reading the entire ctor.

**Deliverables:**

- **`MainWindow.ServiceComposition.cs`**: **`BuildApplicationServiceGraph`** builds the graph in dependency order; **`MainWindowServiceGraph`** record holds the same instances the ctor assigns to readonly fields. Documented XML lists the step sequence (links to existing **`Create*`** helpers).

**Exit:** New contributors find wiring in **one place**; behavior unchanged.

---

### A.2 — Settings and paths (medium) — **landed (initial slice)**

**Goal:** Path strings, secrets, and **Load/Save** call sites do not sprawl across unrelated partials.

**Deliverables:**

- **`MainWindow.SettingsState.cs`**: **`CaptureAppSettings`** / **`ApplyAppSettings`** — the only mapping between **`MainWindow`** fields and **`AppSettings`** (includes **`LibraryIndexAnchor`** on capture for parity with load).
- **`MainWindow.SettingsPersistence.cs`**: **`LoadSettings`** / **`SaveSettings`** + index-scope notify helpers only; all ini I/O via **`ISettingsService.LoadFromIni`** / **`SaveToIni`**.
- **`ISettingsService`**: contract XML states ini persistence must go through this interface.

**Exit:** Settings-related edits touch **`Services/Config`** + **`UI/Settings/MainWindow.SettingsState`** + thin persistence partial; further stragglers in other partials can move in follow-up slices.

**Follow-ups:** **`import_move_conflict_mode`** is on **`AppSettings`** / load-save / **`MainWindow.SettingsState`** (values **`Rename`** / **`Skip`** / **`Overwrite`**).

**Settings ini I/O sweep (live `src/` only):** No stray parse/read/write of **`PixelVault.settings.ini`** beyond:

| Location | Role |
|----------|------|
| **`SettingsService`** | **`LoadFromIni`**, **`SaveToIni`**, **`TryReadIniValue`** (anchor logic during save) |
| **`PersistentDataMigrator`** | Copies legacy **`PixelVault.settings.ini`** into resolved data root during migration |
| **`MainWindow.StartupInitialization.ComputePersistentStorageLayout`** | Builds **`settingsPath`** filename (`…/PixelVault.settings.ini`) |
| **`MainWindow.SettingsPersistence`** | Delegates to **`ISettingsService`** only |
| **`UI/Settings/SettingsShellHost.cs`** | Tooltip text only (no file I/O) |
| **`Publish-PixelVault.ps1`** | Publish script bundles settings into output (expected) |

Tests under **`tests/`** create temp ini files for **`PersistentDataMigrator`** / **`SettingsService`** — expected.

---

### A.3 — Import pipeline depth (medium–high)

**Goal:** **`ImportWorkflow`** is a **thin** coordinator; **move/copy/sort/undo** rules and **file I/O** live in **`IImportService`** (and **`IFileSystemService`**) with **testable** helpers.

**Deliverables:**

- Extract **pure** steps from **`ImportWorkflow`** into **`ImportService`** (or dedicated internal types) where **`MAINWINDOW_EXTRACTION_ROADMAP`** already points.
- Stable **result DTOs** for progress UI (avoid raw tuple soup).

**Exit:** New import behavior lands under **`Services/Intake`** (with other intake/import pipeline types) first; **`ImportWorkflow`** only sequences and shows UI.

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`RunWorkflow`**: single **`BuildSourceInventory`** call (was duplicated identical call for rename vs move totals). |
| **2026-04-18** | **`ImportWorkflowOrchestration.CombineRenameStepResults`**: unified import-and-comment Steam + manual rename aggregation; tests. |
| **2026-04-18** | **`MainWindow.SaveUndoAndSortAfterImportMoveIfNeeded`**: single path for post-move undo manifest + sort (standard, unified, manual intake). |
| **2026-04-18** | Post-import sort uses **`IImportService.SortDestinationRootIntoGameFolders`** directly (not **`SortDestinationFoldersCore`**, which adds UI toasts/status). |

---

### A.4 — Library scan host & metadata glue (medium–high)

**Goal:** **`ILibraryScanHost`** does not grow indefinitely; **`MainWindow`** forwards **narrow** operations.

**Deliverables:**

- Prefer **splitting** oversized host callbacks into **named** dependencies (existing direction in **`ARCHITECTURE_REFACTOR_PLAN.md`**).
- Push **metadata scan entry** and **index merge** policy toward **`LibraryScanner`** / **`IMetadataService`** when touching those paths.

**Exit:** Each new scan feature adds **one** clear owner type; host interface changes are **rare** and **reviewed**.

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`ILibraryScanHost`**: expanded contract XML (grouped concerns, implementation pointer, A.4 guidance). **`LibraryScanHost`** class summary. |

---

### A.5 — IFileSystemService on business paths (ongoing sweep)

**Goal:** **Workflow** file operations (copy, move, manifests, migration) go through **`IFileSystemService`** when a file is **already** being edited for extraction.

**Deliverables:**

- No repo-wide blind replace; follow **`ARCHITECTURE_REFACTOR_PLAN.md`** “touch the call site” rule.

**Exit:** Trend visible in **diffs**; tests use fake file system where added.

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`ImportWorkflow`**: destination **`CreateDirectory`** and **`File.Exists`** checks use **`IFileSystemService`** (`RunWorkflow`, `OpenManualIntakeWindow`, unified move path filter). |
| **2026-04-18** | **`ImportWorkflowOrchestration`** progress totals / unified + manual plans take **`IFileSystemService`**; **`MainWindow.ImportWorkflow.Steps`** (metadata timestamps, **`RunMove`** filter); **`HeadlessImportCoordinator`** + **`MainWindow.HeadlessImport`**; **`IFileSystemService.GetCreationTime`**. |

---

### A.6 — Optional logging seam (low–medium) — **landed (v1)**

**Goal:** Replace ad hoc **`Log(string)`** forwarding with **`ILogService`** only if **multiple** non-UI types need logging **or** testability demands it.

**Deliverables:**

- Thin wrapper around current behavior; **no** new log sinks required for v1.

**Exit:** **`ILogService`** + **`TroubleshootingLogService`**; **`ImportService`** consumes **`ImportServiceDependencies.LogService`**; **`NullLogService`** for unit tests.

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`ILogService`**, **`TroubleshootingLogService`**, **`NullLogService`** (`Services/Logging/`); **`MainWindowServiceGraph.LogService`**; **`MainWindow.Log`** → **`logService.AppendMainLine`** + **`logBox`** mirror; **`ImportServiceDependencies.LogService`** (required); **`LogServiceTests`**. |

---

### Phase A exit criteria (milestone)

Phase A is “on track” when:

- **`ARCHITECTURE_REFACTOR_PLAN.md`** success bullets hold for **new** work.
- **`PixelVault.Native.cs`** **stops growing** net new orchestration for library/import; additions go to **services** or **hosts**.
- **`CHANGELOG.md`** records shipped slices.

**Milestone note (2026-04-18):** A.1–A.6 slices above are **landed** (A.6 is the optional logging seam v1). Further Phase A work is **incremental** (call-site sweeps) unless a new plan item opens.

---

## 5. Phase B — Service organization (after Phase A milestones)

**Precondition:** At least **A.2** and **A.3** or **A.4** have landed **or** are explicitly **not** in scope for the quarter—avoid organizing **unstable** surfaces.

### B.1 — Folder and namespace pass

**Goal:** **`Services/`** tree matches **domains** in **`PROJECT_CONTEXT.md`** and **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`**.

**Deliverables:**

- Rename/move **only** when it **reduces** confusion (e.g. group **Intake** helpers under `Services/Intake` with **no** public API break).
- Update **`PROJECT_CONTEXT.md`** “Source Layout” if the tree changes.

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`Services/Import`** → **`Services/Intake`**: **`ImportService`**, **`ImportWorkflowOrchestration`**, **`SteamImportRename`** (namespace unchanged **`PixelVaultNative`**); **`PixelVault.Native.csproj`** compile paths; **`PROJECT_CONTEXT`**, **`SERVICE_OWNERSHIP`**, **`REAL_APP_IMPLEMENTATION_MAP`**, **`pixelvault_service_split_plan.txt`**, **`PV-PLN-AINT-001`** path references. |

---

### B.2 — Subsystem facades (optional) — **landed (v1)**

**Goal:** If **Intake** (coordinator + analysis + policy + stability) has **many** public entry points, introduce **one** **`IntakePipeline`** (or similar) **facade** used by **`MainWindow`**—implementations stay separate classes **behind** the facade.

**Deliverables:**

- Facade is **thin**; **no** business logic aggregation into a god-class.

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`IntakePipeline`** (`Services/Intake/IntakePipeline.cs`): **`Import`**, **`FileSystem`**, **`Analysis`**; **`RunStandardTopLevelSubsetAsync`** → **`HeadlessImportCoordinator`**. **`MainWindowServiceGraph`** / ctor: **`intakePipeline`** replaces bare **`IntakeAnalysisService`** field; **`MainWindow.IntakePreview`**, **`BackgroundIntake`**, **`HeadlessImport`** use the facade. |

---

### B.3 — Documentation map — **landed (diagram slice)**

**Goal:** **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`** reflects **post–Phase A** reality (remove stale “candidate” rows for shipped types; add **composition** pointer).

**Deliverables:**

- Single **diagram** or table: **UI** → **session/host** → **service** → **persistence**. *(Composition-at-a-glance table + revision rows shipped earlier; Apr 2026 adds a **mermaid data-flow** chart and Intake facade row.)*

**Landings:**

| Date | Change |
|------|--------|
| **2026-04-18** | **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`**: “Composition at a glance” table (**`MainWindow.ServiceComposition`** / graph → import, headless, library session, persistence); **`IFileSystemService`** / **`ISettingsService`** notes refreshed. |
| **2026-04-18** | Same doc: **session log** row (**`ILogService`** / graph **`LogService`**); **`ILogService`** candidate row → shipped v1 notes. |
| **2026-04-18** | **B.3 (diagram):** **`SERVICE_OWNERSHIP`** — mermaid **Data flow** chart (MainWindow → graph → IntakePipeline / library session → scanner / index persistence). |

---

### Phase B exit criteria

- A new contributor can answer: **Where is import logic?** **Where is library scan?** **Where is wiring?** in **under five minutes** using this doc + **`PROJECT_CONTEXT.md`**.
- No **behavior** change from organization PRs alone.

---

## 6. Explicit non-goals

- Full **MVVM** rewrite or **DI container** as a gate.
- Merging **CoverService** + **IndexPersistenceService** + **ImportService** into one type.
- Repo-wide **nullable** enablement as part of this plan.
- Replacing **ExifTool** in the same pass as structure.

---

## 7. Related documents

| Document | Role |
|----------|------|
| **`ARCHITECTURE_REFACTOR_PLAN.md`** | Durable refactor contract (north star, ports, IFileSystem, async). |
| **`MAINWINDOW_EXTRACTION_ROADMAP.md`** | Historical **UI** extraction record (Phases A–F complete). |
| **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`** | Parallel lanes and merge rules. |
| **`pixelvault_service_split_plan.txt`** | Service split status and Phase 7 stub. |
| **`PROJECT_CONTEXT.md`** | Product and subsystem orientation. |

---

## 8. Revision history

| Date | Change |
|------|--------|
| **2026-04-18** | Initial plan: Phase A extraction sequence, Phase B organization gates, non-goals. |
| **2026-04-18** | **A.1:** `MainWindow.ServiceComposition.cs` + `BuildApplicationServiceGraph` / `MainWindowServiceGraph` (`PixelVault.Native.csproj` compile include). |
| **2026-04-18** | **A.2 (initial):** `MainWindow.SettingsState.cs` (capture/apply); persistence partial slim; `ISettingsService` contract note. |
| **2026-04-18** | **A.2:** `import_move_conflict_mode` persisted (`AppSettings`, `SettingsService.NormalizeImportMoveConflictMode`, tests). |
| **2026-04-18** | **A.2:** Settings ini I/O sweep documented (table in this plan). |
| **2026-04-18** | **A.3:** `ImportWorkflow.RunWorkflow` — one `BuildSourceInventory` per run. |
| **2026-04-18** | **A.3:** `CombineRenameStepResults`; `SaveUndoAndSortAfterImportMoveIfNeeded`; post-import sort → **`IImportService.SortDestinationRootIntoGameFolders`**. |
| **2026-04-18** | **A.4 (initial):** **`ILibraryScanHost`** / **`LibraryScanHost`** contract documentation. |
| **2026-04-18** | **A.5 (slice):** **`ImportWorkflow`** — **`IFileSystemService`** for destination mkdir + file-existence filters. |
| **2026-04-18** | **A.5 (continuation):** orchestration + headless + workflow steps + **`GetCreationTime`** on **`IFileSystemService`**. |
| **2026-04-18** | **B.3 (initial slice):** composition table in **`SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`**. |
| **2026-04-18** | **A.6:** **`ILogService`** v1 + **`ImportService`** wiring (see A.6 landings). |
| **2026-04-18** | **B.3:** composition table — **`ILogService`** row; **`ILogService`** candidate note refresh. |
| **2026-04-18** | **B.1 (slice):** import-domain service files co-located under **`Services/Intake`** (see B.1 landings). |
| **2026-04-18** | **B.2:** **`IntakePipeline`** facade (see B.2 landings). |
| **2026-04-18** | **B.3:** mermaid data-flow diagram in **`SERVICE_OWNERSHIP`** (see B.3 landings). |
