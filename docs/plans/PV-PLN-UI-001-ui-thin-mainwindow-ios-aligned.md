# PV-PLN-UI-001 — UI thin-out: MainWindow shrink + iOS-aligned seams

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-UI-001` |
| **Status** | Active (planning / execution) |
| **Owner** | PixelVault / Codex |
| **Parent roadmap** | `docs/ROADMAP.md` — **Phase 3: Shrink MainWindow** (A–F extraction **complete**; this plan is **what comes next**) |
| **Cross-cutting guardrails** | `docs/ios_foundation_guide.md` — prefer **services + plain models** for library/capture/query and **mobile-safe** writes; keep WPF at the shell edge |
| **Related** | `docs/NEXT_TRIM_PLAN.md`, `docs/MAINWINDOW_EXTRACTION_ROADMAP.md`, `docs/ARCHITECTURE_REFACTOR_PLAN.md`, `docs/PERFORMANCE_TODO.md`, `docs/DOC_SYNC_POLICY.md` |

## Purpose

After **Phases A–F** of the MainWindow extraction initiative, the **largest remaining liability** is still the **`PixelVault.Native.cs` monolith** (~3k+ lines of fields, nested types, indexing mirrors, and orchestration) sitting beside **40+ `MainWindow` partials** under `UI/`, `Import/`, etc.

This plan defines **prioritized slices** to:

1. **Thin the UI shell** — fewer concerns and types living in `PixelVault.Native.cs`; clearer ownership per subsystem.
2. **Align extractions with long-term clients** — without building iOS or a backend now, shape new boundaries so **library/capture data, queries, stars, and comments** can later become **API-stable contracts** (per **`docs/ios_foundation_guide.md`**).
3. **Preserve behavior** — vertical slices, tests after each merge, manual golden path when Library/import/metadata paths move.

**Non-goals for this plan:** shipping an iOS app, shipping a backend, full MVVM rewrite, nullable migration of the entire tree.

---

## Full review — where things stand

### Completed extraction (baseline)

- **Library browser:** `LibraryBrowserHost`, `ILibraryBrowserShell`, `LibraryBrowserShellBridge`, `LibraryBrowserShowOrchestration`, many **`MainWindow.LibraryBrowser*.cs`** partials (layout, render, chrome, workspace mode, etc.).
- **Settings:** `SettingsShellHost`, `MainWindow.SettingsShell`, `MainWindow.SettingsPersistence`.
- **Photography / Steam picker:** `MainWindow.PhotographyAndSteam.cs`.
- **Intake / manual metadata:** split partials under `UI/Intake/`.
- **Import:** `Import/ImportWorkflow.cs` + `MainWindow.ImportWorkflow.*.cs`.
- **Index ctor wiring:** `MainWindow.IndexServicesWiring.cs`.

Canonical record: `docs/completed-projects/MAINWINDOW_EXTRACTION_PHASES_A-F.md`.

### What still concentrates in `PixelVault.Native.cs`

Roughly, the core file still **owns**:

- **Large field block:** paths, library roots, persisted library-browser UI state, **in-memory `libraryMetadataIndex` mirror + locks**, folder-cache RW lock, import/gaming editor window refs, **image LRU + `LibraryImageLoadCoordinator` + thumbnail pipeline**, video warm semaphores, etc.
- **Nested types used across library UI:** e.g. **`LibraryDetailRenderSnapshot`**, **`LibraryDetailRenderGroup`**, **`LibraryTimelineCaptureContext`** (defined inside `MainWindow` today).
- **Metadata index helpers:** clone/merge/load paths that could stay on `MainWindow` or move behind **`ILibrarySession`** / a narrow façade.
- **Constructor composition** and **main capture grid / intake UI** build (non-library surfaces).
- **Steam / cover / queue image load** glue that is not yet fully isolated from WPF-free helpers.

**Net:** extraction moved **flows** into partials and hosts, but **`PixelVault.Native.cs` remains the gravitational center** for shared state and several domain-adjacent types.

### Risk / opportunity (iOS foundation lens)

From **`docs/ios_foundation_guide.md`**:

- **Prefer:** service-layer logic, plain models, async service calls, reusable domain logic that could sit behind an API later.
- **Avoid:** reusable rules in `MainWindow`; business logic tied to `Dispatcher` / `MessageBox`; embedding Windows paths into core logic when a service boundary is reasonable.
- **Mobile-safe writes** (stars, comments, light flags) should eventually be **mediated** — desktop can implement the same contracts locally until a backend exists.

**Practical alignment:** each slice below notes whether it is **shell-only**, **desktop-service**, or **contract-shaped** (helps iOS/backend later).

---

## Target shape (lightweight)

| Layer | Responsibility |
|-------|----------------|
| **WPF / `MainWindow` partials** | Windows, dialogs, virtualization hosts, input, dispatch to services |
| **Session / services** (`ILibrarySession`, `IImportService`, …) | Storage, indexing, workflows; **plain DTOs in / out** where feasible |
| **Shared models** (future-friendly) | Serializable summaries/queries/filters — **no WPF types** |

We do **not** require a new `PixelVault.Core` project in phase 1; we **do** require that **new extractions** stop growing `MainWindow` as a **logic dump**.

---

## Staged delivery

Implement as **small vertical slices**. After each slice: **`dotnet test`** (`PixelVault.Native.Tests`), and **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** when Library, detail pane, import, or metadata editors are touched.

### Step 1 — Move library detail render models out of `PixelVault.Native.cs`

**Deliverable:** `LibraryDetailRenderSnapshot`, `LibraryDetailRenderGroup`, `LibraryTimelineCaptureContext` live in a dedicated file (e.g. `UI/Library/LibraryDetailRenderModels.cs`) as **`internal`** types; `MainWindow` partials unchanged except usings/namespaces.

**iOS alignment:** **Contract-shaped** — snapshot/context types are closer to plain DTOs than nested private classes on the window.

**TEST GATE:** Build + tests; open library, folder detail, timeline; verify no reflection/type break.

---

### Step 2 — Image load / thumbnail pipeline: one orchestration owner

**Deliverable:** Fields and methods for **`LibraryImageLoadCoordinator`**, LRU cache, **`QueueImageLoad`**, and thumbnail pipeline wiring are grouped in **one partial** (extend `MainWindow.LibraryImageLoading.cs` or add `MainWindow.LibraryImagePipeline.cs`). **`PixelVault.Native.cs`** should not re-grow this surface.

**iOS alignment:** **Shell-only** — decoding and WPF bitmap apply stay desktop-only; **no change** to mobile contracts yet.

**TEST GATE:** Folder grid, detail pane, cover refresh; cold cache scroll.

---

### Step 3 — Metadata index mirror: façade behind `ILibrarySession` (incremental)

**Deliverable:** Reduce direct use of **`MainWindow`-owned `libraryMetadataIndex`** from new code; route **read/update** paths used by library detail through **`ILibrarySession`** (or a small new interface implemented by existing session). **No behavior change** in slice 1 — **mechanical delegation** from `MainWindow` to session for chosen call sites.

**iOS alignment:** **Desktop-service** — index access becomes a **stable port**; future backend can substitute implementation.

**TEST GATE:** Detail load, metadata repair, star toggle, timeline footers.

**Progress (2026-04-06):** Added **`LoadLibraryMetadataIndexViaSessionWhenActive`** / **`SaveLibraryMetadataIndexViaSessionWhenActive`** on `MainWindow` (`LibraryMetadataIndexing.cs`). Library UI paths (**capture quick actions**, **photography gallery**, **folder cache IO** stamp validation) call through **`ILibrarySession`** when `root` matches the active library; otherwise behavior unchanged.

---

### Step 4 — Library browse projection: document + first DTO seam

**Deliverable:**

- Short appendix in this plan or **`docs/SMART_VIEWS_LIBRARY.md`** listing **folder row** fields that are **conceptually** `GameSummary`-class data today (`LibraryBrowserFolderView` / cache projection).
- **Optional code slice:** introduce **`internal`** read-only DTOs (name/path/count/platform labels) built **once** in scanner/session projection; UI maps DTO → existing view models **without** a big bang. Start with **one** read path (e.g. folder list projection) if scope is large.

**iOS alignment:** **Contract-shaped** — matches `LibraryQuery` / `LibraryQueryResult` direction in **`ios_foundation_guide.md`**.

**TEST GATE:** Folder list, search, filters, grouping unchanged.

**Progress (2026-04-06):** **`LibraryBrowseFolderSummary`** + **`FromFolderView`** in `UI/Library/LibraryBrowseFolderSummary.cs`; field mapping documented in **`docs/SMART_VIEWS_LIBRARY.md`** (appendix). Unit tests in **`LibraryBrowseFolderSummaryTests`**. **Follow-on:** smart-view predicates **`MatchesFilter`** / **`IsSteamTagged`** live on the DTO; **`MainWindow.LibraryBrowserFolderFilter`** delegates from **`LibraryBrowserFolderView`** via **`FromFolderView`** (folder list behavior unchanged).

---

### Step 5 — Star / comment writes: single orchestration entry

**Deliverable:** Audit star toggle and comment persistence; ensure **one** service/session entry point with **plain parameters** (path + payload); **`MainWindow`** only invokes it from UI events.

**iOS alignment:** **Mobile-safe writes** — same surface a future API would expose.

**TEST GATE:** Star in library/detail/photo index; edit comment; persistence after restart.

**Progress (2026-04-06):** **`ILibrarySession.RequestToggleCaptureStarred`** / **`RequestSaveCaptureComment`** implemented on **`LibrarySession`** (host delegates wired in `MainWindow` ctor). Library detail tiles / timeline footer use the session API; **`ToggleLibraryFileStarredByPath`** / **`SaveLibraryFileCommentByPath`** remain private host workers. **Completion:** `RequestToggleCaptureStarred` takes a bool callback (`true` when the index row toggled); photography gallery stars route through the session (removed duplicate Exif/index task). Manual-metadata batch comments remain on the existing workflow / index upsert paths.

---

### Step 6 — Constructor and path/root field diet

**Deliverable:** Follow **`MainWindow.IndexServicesWiring.cs`** pattern: group **path initialization**, **settings-related field defaults**, or **timer/diagnostic** setup into a **single partial**; target **measurable line reduction** in `PixelVault.Native.cs` (align with **`NEXT_TRIM_PLAN.md`** ~2k line aspiration over time).

**iOS alignment:** **Shell-only** — paths remain Windows-specific at the edge.

**TEST GATE:** Cold start, settings load, library open.

**Progress (2026-04-06):** **`MainWindow.StartupInitialization.cs`** — `ComputePersistentStorageLayout` (static, supports `readonly` path fields), `CreateStartupDirectories`, `InitializeDefaultWorkspaceRootsAndTools`; ctor in **`PixelVault.Native.cs`** delegates to these. **Follow-on:** static factories for settings/file IO, cover + metadata services, library scanner, import dependencies, library session, game index service — ctor assigns `readonly` fields from return values only. **`RunPostServiceStartup`** (directories, readme, changelog seed, migrate, default roots, **`LoadSettings`**) and **`ApplyMainWindowChromeAndShell`** (window metrics, icon, content, **`ShowLibraryBrowser`**) complete the ctor diet.

---

### Step 7 — Ongoing opportunistic

- **`ImportWorkflow` → `IImportService`:** move more orchestration when touching imports (`NEXT_TRIM_PLAN.md` Tier 1b follow-on).
- **Steam / cover workflows:** dedicated service when editing that area (`ROADMAP.md` Phase 3 candidates).
- **Nullable:** new extracted files first (`ROADMAP.md` Phase 4).

**Progress (2026-04-06):** **`ImportWorkflowOrchestration`** (`Services/Import/ImportWorkflowOrchestration.cs`) — **`GetMetadataWorkerCount`**, **`ThrowIfCancellationRequested`**. Import progress lambdas and **MetadataService** worker cap use the shared helpers; **Game index resolve** cancellation uses the same static (no longer an instance method on **`MainWindow`**).

**Progress (2026-04-06, follow-on):** **Import prep on `IImportService`** — **`ComputeStandardImportWorkTotals`**, **`ComputeUnifiedImportProgressPlan`**, **`ComputeManualIntakeProgressPlan`** (implemented via **`ImportWorkflowOrchestration`**; **`ImportWorkflow`** / **`MainWindow`** uses them for **`totalWork`** and progress offsets). **Steam store title helper** — **`CoverWorkflowHelpers.ResolveSteamStoreTitleForAppIdAsync`** (**`RunSteamRenameAsync`**, **`ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`**); preserves “sync resolver only, no async fallback when injected” semantics. **Nullable** — **`#nullable enable`** on **`ImportWorkflowOrchestration`**, **`CoverWorkflowHelpers`**, **`LibraryBrowseFolderSummary`**. Tests: **`ImportWorkflowOrchestrationProgressTests`**.

---

## Metrics

| Metric | Now (approx.) | Direction |
|--------|----------------|-----------|
| `PixelVault.Native.cs` lines | ~3.0k | Down over Steps 1–2–6 |
| New domain logic in `MainWindow` | Discouraged | **Zero** for new features per `ios_foundation_guide` checklist |
| `MainWindow` partial count | ~47 | May grow slightly if partials **replace** monolith lines |

---

## Checklist (merge gate — from iOS foundation doc)

Before merging a slice, ask:

- Does this logic need to live in `MainWindow`?
- Could this become a backend responsibility later?
- Are inputs/outputs plain models where the slice touches **library/capture** data?
- Is WPF coupling limited to the shell?

---

## Changelog / doc sync

| Date | Change |
|------|--------|
| 2026-04-06 | Initial plan: post–A–F UI thin-out; iOS foundation alignment; staged Steps 1–7. |
| 2026-04-06 | **Step 1 done:** `LibraryDetailRenderSnapshot`, `LibraryDetailRenderGroup`, `LibraryTimelineCaptureContext` → `src/PixelVault.Native/UI/Library/LibraryDetailRenderModels.cs` (registered in `.csproj`). |
| 2026-04-06 | **`LibraryDetailMediaLayoutInfo`** moved from `MainWindow.LibraryPhotoMasonryLayout.cs` into **`LibraryDetailRenderModels.cs`**. |
| 2026-04-06 | **Step 2 done:** `MaxImageCacheEntries`, `libraryBitmapCache`, `imageLoadCoordinator`, `libraryThumbnailPipeline` field declarations → **`MainWindow.LibraryImageLoading.cs`**; **`InitializeLibraryThumbnailPipeline(thumbsRoot)`** owns pipeline construction (ctor calls it). |
| 2026-04-06 | **Step 3 (incremental):** Session-routing helpers + capture/photography/folder-cache call sites → **`ILibrarySession`** when root is active library. |
| 2026-04-06 | **Step 4 (initial):** **`LibraryBrowseFolderSummary`**, **`SMART_VIEWS_LIBRARY.md`** browse appendix, tests. |
| 2026-04-06 | **Step 5 (initial):** Session **`RequestToggleCaptureStarred`** / **`RequestSaveCaptureComment`**; library detail + quick-comment path routable for future non-WPF callers. |
| 2026-04-06 | **Step 5 (complete slice):** Star toggle bool completion callback; photography gallery uses session; **Step 6 (initial):** **`MainWindow.StartupInitialization.cs`** + ctor diet. |
| 2026-04-06 | **Step 6 (follow-on):** Service wiring factories in **`MainWindow.StartupInitialization.cs`**; ctor slimmed. **Step 6 (shell):** **`RunPostServiceStartup`**, **`ApplyMainWindowChromeAndShell`**. |
| 2026-04-06 | **Perf note** recorded in **`docs/PERFORMANCE_TODO.md`** (landed table): star lookup cache, folder enum + index batch, detail masonry off-UI, repair cap + deferred queue. |
| 2026-04-06 | **Step 4 (follow-on):** **`LibraryBrowseFolderSummary.MatchesFilter`** / **`IsSteamTagged`**; **`MainWindow.LibraryBrowserFolderFilter`** delegates; folder list render filters via summary (**`MainWindow.LibraryBrowserRender.FolderList`**). |
| 2026-04-06 | **Step 7 (initial):** **`ImportWorkflowOrchestration`** — metadata worker count + cancellation throw shared by import workflow, **MetadataService** deps, and **GameIndexEditorHost** services. |
| 2026-04-06 | **Step 7 (follow-on):** import progress totals on **`IImportService`** + **`ImportWorkflowOrchestration`**; **`CoverWorkflowHelpers`** for Steam display name; **`#nullable enable`** on new/chosen files; **`ImportWorkflowOrchestrationProgressTests`**. |

When execution starts, reference **`PV-PLN-UI-001`** in commits; Notion per **`docs/DOC_SYNC_POLICY.md`** if milestones are tracked there.
