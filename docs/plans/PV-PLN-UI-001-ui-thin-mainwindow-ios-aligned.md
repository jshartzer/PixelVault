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

After **Phases A–F** of the MainWindow extraction initiative, the **largest single file** is still **`PixelVault.Native.cs`** (now ~2.06k lines, down from ~3.3k pre-extraction — about a **38 % cut**) sitting beside **~55 `MainWindow` partials** under `UI/`, `Import/`, etc. The shape of the remaining code has changed since this plan was first written: the monolith has **stopped growing**, new subsystems have landed **as services plus partials** (the plan's direction has held), and the next wins are now **concrete, coherent blocks** inside the monolith rather than broad architectural sweeps.

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

### Subsystems that landed between plan v1 and v2 (Apr 9 → Apr 17)

The app grew while the monolith held steady at ~2k lines. These all shipped as **services + plain models + partials**, which is what the plan and **`docs/ios_foundation_guide.md`** argue for — worth recording so later slices respect the same pattern.

- **Background auto-intake (AINT-001).** `MainWindow.BackgroundIntake.cs` (~560 lines) hosts `BackgroundIntakeAgent`; it wires to `Services/Intake/HeadlessImportCoordinator.cs`, `AutoIntakePolicy`, `SourceFileStabilityProbe`, `IntakeAnalysisService`, `IntakePreviewFileAnalysis`, `BackgroundIntakeActivityModels`, `ForegroundIntakeBusyGate`. Activity UI in `UI/Intake/BackgroundIntakeActivityWindow.cs`. **Contract-shaped** — policy and analysis types are plain models.
- **System tray.** `MainWindow.SystemTray.cs` (~620 lines) owns minimize-to-tray, close-prompt, flyout, session-ending. **Shell-only** by design; does not leak business logic.
- **Achievements.** `Services/Achievements/GameAchievementsFetchService.cs` (~585 lines), `UI/AchievementsInfoWindow.cs` (~450 lines), `UI/Library/MainWindow.LibraryBrowserAchievements.cs` (~280 lines). Retro + Steam achievements with a dedicated service.
- **Health dashboard.** `UI/Diagnostics/HealthDashboardWindow.cs` (~850 lines) — standalone diagnostics window; opens from Settings; does not route through `MainWindow` state.
- **Command palette.** `UI/Library/LibraryCommandPaletteRegistry.cs`, `LibraryCommandPaletteWindow.cs`, `LibraryBrowserPaletteContext.cs`, `MainWindow.LibraryBrowserPaletteCommands.cs`. Good example of a registry + context seam — palette commands do not reach into the monolith.
- **Quick-edit drawer.** `MainWindow.LibraryBrowserQuickEditDrawer.cs` (~365 lines) — inline edit UI for the detail pane.
- **Photo hero / masonry / capture viewer / split layout / toast / shortcuts / workspace mode / folder id editor / storage merge.** All landed as dedicated partials or services (`Services/Library/LibraryPlacementService.cs`, `LibraryStorageMergePlanner.cs`, `LibraryStorageMergeModels.cs`).
- **Storage-group backfill + game-index service.** `Services/Indexing/GameIndexService.cs`, `IGameIndexService`, `GameIndexServiceDependencies`, `GameIndexStorageGroupBackfill`. First proper `IGameIndexService` seam.
- **IO seam.** `Services/IO/IFileSystemService.cs` + `FileSystemService` threaded into `ILibrarySession`.
- **PhotographyGalleryWindow (XAML).** `UI/Photography/PhotographyGalleryWindow.xaml` + `.xaml.cs` — **first proper XAML-based `Window`** in the codebase; a template for future modern windows.

### What still concentrates in `PixelVault.Native.cs`

Mapped from the current file (2,058 lines as of Apr 17, 2026):

| Lines | Region | Target owner |
|-------|--------|--------------|
| 1–54 | `Program.Main` + `MergeGlobalScrollBarTheme` | Stay (app bootstrap) |
| 56–170 | `MainWindow` constants, **~115 lines of fields** (paths, caches, `libraryMetadataIndex` + locks, tray state, persisted UI state, services) | Partially migrated; Step 6 continues |
| 169–213 | Constructor — slim today, delegates to `StartupInitialization` partials | **Done** (Step 6) |
| 215–248 | `Normalize/LabelLibraryFolderSortMode/FilterMode` (~35 lines) | Step 9 candidate (move next to `MainWindow.LibraryFolderSortKeys.cs`) |
| 249–418 | **WPF button/toolbar chrome factories** — `Btn`, `LibraryToolbarButtonStyle`, `BuildLibrary(Circle)ToolbarButtonTemplate`, `ApplyLibrary(Pill/Circle)Chrome`, `BuildRoundedTileButtonTemplate` (~170 lines) | **Step 9** (new) — `UI/Library/LibraryButtonChrome.cs` |
| 420–554 | `RefreshMainUi`, `ResolveStatusWindowOwner`, `RefreshActiveLibraryFolders`, `PickFolder` / `PickFile`, source-root helpers, `EnumerateSourceFiles`, `FindExecutableOnPath`, `OpenSourceFolders`, `ClearLibraryImageCaches` | Keep/trim (shell glue) |
| 555–652 | **`RunLibraryMetadataWorkflowWithProgress`** (~100 lines) | Step 2/3 follow-on → `MainWindow.LibraryMetadataScan.cs` next to `ShowLibraryMetadataScanWindow` |
| 654–662 | `ParseFilename` thin wrappers | Keep |
| 665–1490 | **Steam + SteamGridDB + cover resolution** — ~830 lines (the largest coherent block left) — `Resolve*SteamAppIdAsync`, `Resolve*SteamGridDbIdAsync`, `EnrichLibraryFoldersWithSteamAppIdsAsync`, `RefreshLibraryCoversAsync`, `ForceRefreshLibraryArtAsync`, custom + cached cover / hero / logo paths, SteamGridDB/Steam-store downloads | **Step 8** (new) — `ICoverService` / `Services/Covers/LibraryCoverResolutionService.cs` |
| 1492–1522 | `UpdateCachedLibraryFolderInfo` | Ride with Step 8 |
| 1523–1582 | Platform label helpers — `PrimaryPlatformLabel`, `FilenameGuessLabel`, `IsSteamManualExportWithoutAppId`, `PlatformGroupOrder`, `PreviewBadgeBrush` (~60 lines) | Step 12 candidate |
| 1584–1632 | Static text / path helpers — `ParseInt`/`Long`, `Sanitize`, `CleanComment`, `CleanTag`, `ParseTagText`, `SameManualText`, `Unique`, `EnsureDir`, `IsImage`, `IsPngOrJpeg`, `IsVideo`, `IsMedia`, `Quote`, `GetLibraryDate`, `SafeCacheName`, `NormalizeTitle`, `StripTags` (~50 lines) | **Step 12** (new) — `Infrastructure/TextAndPathHelpers.cs` |
| 1634–1782 | **Persistent-data migration + `OpenFolder` / saved-covers readme** — `ResolvePersistentDataRoot`, `MigratePersistentDataFromLegacyVersions`, `CopyIfNewerOrMissing`, `CopyDirectoryContentsIfNewer`, `CopyDirectoryContentsIfMissing`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder` (~115 lines) | **Step 11** (new) — `Infrastructure/PersistentDataMigrator.cs` |
| 1784–1938 | `OpenPhotoIndexEditor`, `OpenGameIndexEditor` wrappers (~155 lines) — already thin but build editor dependencies inline | Opportunistic (Step 7): move dependency builders next to each editor host |
| 1940–2058 | **Logging + troubleshooting redaction** — log file IO, `RotateTroubleshootingLogIfNeeded`, `LogException`, `FormatExceptionForTroubleshooting`, `RedactEmbeddedPathsForTroubleshooting`, `RedactBareWindowsPathForTroubleshooting`, `FormatPathForTroubleshooting`, `FormatViewKeyForTroubleshooting` (~120 lines) | **Step 10** (new) — `Infrastructure/TroubleshootingLog.cs` / `Services/Diagnostics/` |

**Non-monolith liability worth naming:** `UI/Library/MainWindow.LibraryBrowserViewModel.cs` is now **1,088 lines** — effectively an MVVM-style view model (folder view records, clones, projection fingerprints, persisted state) hanging off `MainWindow` as a partial. It has grown faster than the shrink pace of `PixelVault.Native.cs`. Addressed by **Step 13** (new).

**Net:** extraction moved **flows** into partials and hosts, but **`PixelVault.Native.cs` still owns** (a) a large Steam/cover resolution block, (b) WPF button/chrome factories, (c) troubleshooting log plumbing, (d) persistent-data migration, (e) a field block that is still the shared state for the library subsystem.

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

**Progress (2026-04-09):** Session routing for **`AlignLibraryFoldersToGameIndex`**; **`LoadLibraryMetadataIndexForFilePathsViaSessionWhenActive`**; clone/merge + sidecar/stamp helpers in **`LibraryMetadataIndexing.cs`**; folder row + **`SameLibraryFolderSelection`** in **`MainWindow.LibraryFolderCacheIo.cs`**; large shell split — **`MainWindow.MainWindowChrome.cs`**, **`MainWindow.SteamAndExternalApiCredentials.cs`**, **`MainWindow.LibraryResponsiveLayout.cs`**, **`MainWindow.LibraryFolderSortKeys.cs`**, **`LogPerformanceSample`** in **`MainWindow.LibraryBrowserInstrumentation.cs`** — **`PixelVault.Native.cs` ~2.1k** lines.

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
- **Steam / cover workflows:** dedicated service when editing that area — now scoped concretely in **Step 8**.
- **Nullable:** new extracted files first (`ROADMAP.md` Phase 4).
- **Editor host dependency builders:** trim `OpenPhotoIndexEditor` / `OpenGameIndexEditor` (`PixelVault.Native.cs` lines ~1784–1938) by moving dependency construction next to each host (`UI/Editors/PhotoIndexEditorHost.cs`, `UI/Editors/GameIndexEditorHost.cs`) when either is edited.

**Progress (2026-04-06):** **`ImportWorkflowOrchestration`** (`Services/Import/ImportWorkflowOrchestration.cs`) — **`GetMetadataWorkerCount`**, **`ThrowIfCancellationRequested`**. Import progress lambdas and **MetadataService** worker cap use the shared helpers; **Game index resolve** cancellation uses the same static (no longer an instance method on **`MainWindow`**).

**Progress (2026-04-06, follow-on):** **Import prep on `IImportService`** — **`ComputeStandardImportWorkTotals`**, **`ComputeUnifiedImportProgressPlan`**, **`ComputeManualIntakeProgressPlan`** (implemented via **`ImportWorkflowOrchestration`**; **`ImportWorkflow`** / **`MainWindow`** uses them for **`totalWork`** and progress offsets). **Steam store title helper** — **`CoverWorkflowHelpers.ResolveSteamStoreTitleForAppIdAsync`** (**`RunSteamRenameAsync`**, **`ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`**); preserves “sync resolver only, no async fallback when injected” semantics. **Nullable** — **`#nullable enable`** on **`ImportWorkflowOrchestration`**, **`CoverWorkflowHelpers`**, **`LibraryBrowseFolderSummary`**. Tests: **`ImportWorkflowOrchestrationProgressTests`**.

---

### Step 8 — Library cover / Steam / SteamGridDB resolution → cover service (NEW)

**Deliverable:** Move the ~830-line Steam / SteamGridDB / Steam-store / custom + cached cover + hero + logo resolution block (`PixelVault.Native.cs` lines ~665–1490) behind **`ICoverService`**. Start with a **`Services/Covers/LibraryCoverResolutionService.cs`** that owns:

- `Resolve*SteamAppIdAsync`, `Resolve*SteamGridDbIdAsync`, `Resolve*SteamGridDbIdForHeroAssetsAsync`, `ShouldUseSteamStoreLookups`, `EnrichLibraryFoldersWithSteamAppIdsAsync`.
- `ForceRefreshLibraryArtAsync`, `RefreshLibraryCoversAsync` (the outer orchestration that progress-reports; keep the UI callback signature identical).
- `CustomCover/Hero/Logo` + `CachedCover/Hero/Logo` path resolution (keep the IO in `ICoverService`/`fileSystemService`).
- `TryDownloadSteamGridDb(Hero|Logo|Cover)Async`, `TryDownloadSteamStoreHeaderHeroAsync`, `TryDownloadSteamCoverAsync`, `TryResolveSteamGridDbNameFallbackIdAsync`.

`MainWindow` keeps only the thin `GetLibraryArtPathForDisplayOnly` / `GetLibraryHeroBannerPathForDisplayOnly` / `GetLibraryHeroLogoPathForDisplayOnly` wrappers the renderers call through (and maybe moves them to the `LibraryBrowserShellBridge`). `ILibrarySession.RefreshLibraryCoversAsync` delegates to the new service instead of the `MainWindow` instance method.

**Do it in two passes:**

1. **Pass A — same-assembly move.** Drop the whole block into a new partial `UI/Library/MainWindow.LibraryCoverResolution.cs`, `#nullable enable`, zero behavior change. Gets the monolith under ~1.3k lines immediately.
   - **Done 2026-04-17.** `UI/Library/MainWindow.LibraryCoverResolution.cs` (~817 lines) carries the full block (`GetGameNameFromFileName` through `UpdateCachedLibraryFolderInfo`, including `ShouldUseSteamStoreLookups`, `Enrich…Async`, `Refresh…Async`, `ForceRefresh…Async`, `TryDownload(Steam|SteamGridDb)…Async`, and the cover/hero/logo path forwarders). `PixelVault.Native.cs` dropped to **1,262 lines** (−796, **−38.7 %**). Build green, all tests unrelated to pre-existing masonry failures passed. `#nullable enable` deferred to Pass B to keep the diff a verbatim move; file is registered after `MainWindow.LibraryBrowserLayout.cs` in `.csproj`.
2. **Pass B — port to service.** Introduce `LibraryCoverResolutionService` with a small `ILibraryCoverResolution` interface; `MainWindow` supplies `coverService`, `libraryRoot`, HTTP (`TimeoutWebClient`), and logging via dependencies. Progress callbacks remain `Action<int,int,string>`.
   - **Done 2026-04-17.** `Services/Covers/ILibraryCoverResolution.cs` (~76 lines) defines the interface; `Services/Covers/LibraryCoverResolutionService.cs` (~673 lines, `#nullable enable`) owns every orchestration body (`GetGameNameFromFileName`, `GetSafeGameFolderName`, `Resolve*SteamAppIdAsync`, `Resolve*SteamGridDbIdAsync`, `Resolve*SteamGridDbIdForHeroAssetsAsync` [private], `ShouldUseSteamStoreLookups` [private], `ForceRefreshLibraryArtAsync`, `RefreshLibraryCoversAsync`, `GetLibraryArt/Hero*PathForDisplayOnly`, `ResolveLibraryArtAsync`, `ResolveLibraryHeroBanner/LogoWithDownloadAsync`, `TryDownload(Steam|SteamGridDb)…Async`, `TryResolveSteamGridDbNameFallbackIdAsync`, `UpdateCachedLibraryFolderInfo`). Dependencies arrive via `LibraryCoverResolutionDependencies` (interfaces for `ICoverService` / `IFilenameParserService` / `IFileSystemService`, delegates for the handful of helpers `MainWindow` still owns: `NormalizeTitle/ConsoleLabel/GameId`, `BuildLibraryFolderMasterKey`, `BuildLibraryFolderInventoryStamp`, `Load/SaveLibraryFolderCache`, `RefreshCachedLibraryFoldersFromGameIndex`, `GetSavedGameIndexRowsForRoot`, `Find/UpsertSavedGameIndexRow`, `ResolveLibraryFolderSteamAppId`, `ParseFilename`, `Log`, `RemoveCachedImageEntries`, `HasSteamGridDbApiToken`, `GetLibraryRoot`). Wiring lives in `MainWindow.StartupInitialization.CreateLibraryCoverResolutionService`; `PixelVault.Native.cs` holds only a `readonly ILibraryCoverResolution libraryCoverResolutionService` field + ctor assignment. `UI/Library/MainWindow.LibraryCoverResolution.cs` collapsed from 817 → 84 lines (13 thin forwarders plus `CustomCoverPath`/`SaveCustomCover`/`ClearCustomCover`/`CustomHeroPath`/… that forward directly to `ICoverService`). Dead helpers (`CustomCoverKey`, `EnrichLibraryFoldersWithSteamAppIdsAsync`, the three SteamGridDB-JSON parsers) were dropped — `CoverService` already owns the live JSON parsing and the others had zero call sites. Build green; tests show the same pre-existing `LibraryPhotoMasonryLayoutTests` flake (`PrefersRectangles…`, `CompactDensity…`) — confirmed by stashing Pass B and re-running the baseline.

**iOS alignment:** **Desktop-service / contract-shaped.** Steam/SteamGridDB lookups are exactly the shape a future backend would host; keeping them behind a service leaves the iOS app free to consume cached results.

**TEST GATE:** Cover refresh full + scoped, hero/logo reveal in detail pane, missing-id filter, `ForceRefreshLibraryArtAsync` from folder context menu, Manual Steam search flow (which also reads these paths).

---

### Step 9 — WPF toolbar / button chrome → `UI/Library/LibraryButtonChrome.cs` (NEW)

**Deliverable:** Move the ~170 lines of button/toolbar style factories (`PixelVault.Native.cs` lines ~249–418) — `Btn`, `LibraryToolbarButtonStyle`, `BuildLibraryToolbarButtonTemplate`, `LibraryCircleToolbarButtonStyle`, `BuildLibraryCircleToolbarButtonTemplate`, `ApplyLibrary(Toolbar/Pill/Circle)Chrome`, `BuildRoundedTileButtonTemplate` — to a new static class **`LibraryButtonChrome`** under `UI/Library/`. `MainWindow.Brush` is the single cross-call it still needs; pass as a `Func<string, Brush>` or lean on the existing `UiBrushHelper.FromHex`. `Btn` is used widely across partials — make it a static with the same signature and keep `MainWindow.Btn` as a one-line forwarder for the transition.

**iOS alignment:** **Shell-only** — WPF-specific, but removes ~170 lines of boilerplate from the monolith and puts a clean seam between "library brushes/styles" and "library logic".

**TEST GATE:** Library toolbar, detail-pane buttons, folder tile hovers, quick-edit drawer buttons, command palette. Visual parity is the bar.

---

### Step 10 — Troubleshooting log + redaction helper (NEW)

**Deliverable:** Extract `PixelVault.Native.cs` lines ~1940–2058 (log file IO, `RotateTroubleshootingLogIfNeeded`, `LogException`, `FormatExceptionForTroubleshooting`, `RedactEmbeddedPathsForTroubleshooting`, `RedactBareWindowsPathForTroubleshooting`, `RedactPathMatchForTroubleshooting`, `FormatPathForTroubleshooting`, `FormatViewKeyForTroubleshooting`, `TroubleshootingSegmentLooksLikePath`) to **`Infrastructure/TroubleshootingLog.cs`** as a plain class with injected dependencies (paths, `logFileSync`, redact flag getter, clock). `MainWindow.Log` / `LogException` / `LogTroubleshooting` become thin forwarders.

**iOS alignment:** **Contract-shaped** — redaction rules are mobile-safe already (no WPF); the service is a port the mobile app could also consume once it exists. `#nullable enable` in the new file.

**TEST GATE:** Normal library session produces `PixelVault-native.log` as before; enable troubleshooting in Path Settings, drive an error, confirm `PixelVault-troubleshooting.log` rotates past the 5 MB limit, confirm paths are redacted when the checkbox is on. Add unit coverage for the redaction regexes (currently untested).

---

### Step 11 — Persistent-data migration helper (NEW)

**Deliverable:** Lines ~1634–1782 — `ResolvePersistentDataRoot`, `MigratePersistentDataFromLegacyVersions`, `CopyIfNewerOrMissing`, `CopyDirectoryContentsIfNewer`, `CopyDirectoryContentsIfMissing`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder`, `OpenFolder` — move to **`Infrastructure/PersistentDataMigrator.cs`** as static helpers taking explicit paths. `MainWindow` ctor calls the migrator once; `ComputePersistentStorageLayout` already has the path shape.

**iOS alignment:** **Shell-only** (Windows paths), but lets the migrator be unit-tested with a `TempFileSystem` fixture.

**TEST GATE:** Cold start in a fresh data root; cold start in a data root containing only `dist/` layout from 0.075.x; start after dropping old cover PNGs in `savedCoversRoot`. Existing `docs/MANUAL_GOLDEN_PATH_CHECKLIST.md` "first run" section applies.

---

### Step 12 — Small text / path / platform statics (NEW)

**Deliverable:** Lines ~1584–1632 (pure text / path / media-type statics) → **`Infrastructure/TextAndPathHelpers.cs`** (`#nullable enable`). Optionally fold the platform label helpers at lines ~1523–1582 (`PrimaryPlatformLabel`, `FilenameGuessLabel`, `IsSteamManualExportWithoutAppId`, `PlatformGroupOrder`, `PreviewBadgeBrush`) into **`UI/Library/LibraryPlatformLabels.cs`**. Both groups currently have multiple callers across partials, so keep the same signatures and let the wrappers in `MainWindow` delegate — remove them once the compiler shows all call sites moved.

**iOS alignment:** **Contract-shaped** — platform-label logic is data-layer; no WPF touches except the preview badge brush (stays in a WPF-only helper).

**TEST GATE:** Sort/group pills, manual-metadata badges, intake platform grouping, filename-convention preview. Existing filename-parser tests catch most behavior; add a small `LibraryPlatformLabelsTests` if the slice lands any logic change.

---

### Step 13 — Library browser view-model partial → owned class (NEW)

**Deliverable:** `UI/Library/MainWindow.LibraryBrowserViewModel.cs` has grown to **~1,090 lines** and is now the biggest non-monolith `MainWindow` partial. It defines `LibraryBrowserFolderView`, clone/fingerprint helpers, all-merge projection, persisted-search/scroll keys, and view-key lifetime — and reads/writes `MainWindow` fields (`_libraryBrowserPersistedSearch`, `_libraryBrowserLiveWorkingSet`, `libraryGroupingMode`, etc.) directly. Turn it into a non-partial `LibraryBrowserViewModel` class owned by `LibraryBrowserShowOrchestration` (or `ILibrarySession`) with explicit dependencies:

1. Move `LibraryBrowserFolderView` out of the nested type (`internal sealed class`) to top-level `internal`.
2. Introduce `internal sealed class LibraryBrowserViewModel` with ctor dependencies: `ILibrarySession`, `LibraryBrowseFolderSummary` factory, clock, persisted-state container.
3. Move the projection fingerprint + all-merge projection + clone helpers into the class.
4. Replace partial forwarders with `MainWindow` members that hold a lazy `LibraryBrowserViewModel` and call through it.

**iOS alignment:** **Contract-shaped** — this is the library's read-model; isolating it from `MainWindow` lets the iOS client (or an API) consume the same projections. Matches the `LibraryQueryResult` direction in `docs/ios_foundation_guide.md`.

**TEST GATE:** All-merge view, platform-grouped view, search filter (delegated via `LibraryBrowseFolderSummary.MatchesFilter`), sort-mode toggles, pane-scroll persistence across open/close, view-key reuse across sessions. Add a focused `LibraryBrowserViewModelTests` once the type is a plain class — it currently is unreachable from the test project.

**Scope note:** this is the single largest remaining liability after the monolith and should probably land **after** Step 8 so the full Library render side is backed by a service and a plain view-model.

---

## Metrics

| Metric | Apr 6 | Apr 9 | Apr 12 | Apr 17 (plan v2) | Apr 17 (Step 8A) | **Apr 17 (Step 8B)** | Direction |
|--------|------:|------:|-------:|------:|------:|------:|-----------|
| `PixelVault.Native.cs` lines | ~2.9k → ~2.1k | ~2.1k | ~1.9k | 2,058 | 1,262 | **1,264** (+2 for service field + ctor assignment) | Step 8 complete; Steps 9 → 10 → 11 → 12 expected to cut another ~400 |
| `MainWindow` partial count | ~40 | ~52 | ~54 | ~55 | ~56 | **~56** (partial stays, now thin) | Holds; may grow by 2–3 as Steps 9–13 replace monolith lines |
| `UI/Library/MainWindow.LibraryCoverResolution.cs` lines | — | — | — | — | 817 | **84** | Thin forwarders to `ILibraryCoverResolution` + `ICoverService` |
| `Services/Covers/` cover-resolution surface (lines) | — | — | — | — | — | **749** (`ILibraryCoverResolution.cs` 76 + `LibraryCoverResolutionService.cs` 673) | New desktop-service seam |
| Largest non-monolith partial (lines) | — | — | — | 1,088 (`LibraryBrowserViewModel`) | 1,088 (same; `LibraryCoverResolution` = 817) | **1,088** (`LibraryBrowserViewModel`; cover partial now 84) | Step 13 target |
| New domain logic in `MainWindow` | Discouraged | — | — | Still discouraged | Still discouraged | Still discouraged | **Zero** for new features per `ios_foundation_guide` checklist |

**Target after Steps 8–12:** `PixelVault.Native.cs` under **~900 lines** — essentially fields, ctor delegation, shell glue, and the thin wrappers that partials call. `ROADMAP.md` Phase 3 "under ~2,500 lines" stretch is already met; the new bar is "under 1k".

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
| 2026-04-09 | **Step 3 + Step 6 (incremental):** game-index **folder alignment** metadata load/save → session when active; **`CloneLibraryMetadataIndexEntry`** / **`MergePersistLibraryMetadataIndexEntries`** (+ related) relocated to **`LibraryMetadataIndexing.cs`**. |
| 2026-04-09 | **Step 3 + Step 6 (follow-on):** **`LoadLibraryMetadataIndexForFilePathsViaSessionWhenActive`**; folder row helpers + tag cache delegation in **`MainWindow.LibraryFolderCacheIo.cs`**. |
| 2026-04-09 | **Step 6 (incremental):** **`MetadataSidecarPath`** / **`MetadataCacheStamp`** / sidecar delete + undo entries → **`LibraryMetadataIndexing.cs`**; **`SameLibraryFolderSelection`** → **`MainWindow.LibraryFolderCacheIo.cs`**. |
| 2026-04-09 | **Step 6 (major):** WPF chrome + assets → **`MainWindow.MainWindowChrome.cs`**; Steam/external API → **`MainWindow.SteamAndExternalApiCredentials.cs`**; responsive layout → **`MainWindow.LibraryResponsiveLayout.cs`**; folder sort keys → **`MainWindow.LibraryFolderSortKeys.cs`**; **`LogPerformanceSample`** → **`MainWindow.LibraryBrowserInstrumentation.cs`**. |
| 2026-04-17 | **Plan revision (v2):** Re-baselined against the current app (`PixelVault.Native.cs` = 2,058 lines; ~55 partials). Recorded subsystems that landed between Apr 9 and Apr 17 as *aligned with this plan's direction* — **background auto-intake**, **system tray**, **achievements**, **health dashboard**, **command palette**, **quick-edit drawer**, **photo hero / masonry / capture viewer**, **storage merge**, **`IGameIndexService`**, **`IFileSystemService`**, **`PhotographyGalleryWindow.xaml`** (first proper XAML window). Added new slices: **Step 8** cover/Steam resolution service (~830 lines, biggest remaining block), **Step 9** WPF button-chrome to `LibraryButtonChrome`, **Step 10** troubleshooting log helper, **Step 11** persistent-data migrator, **Step 12** text/path/platform statics, **Step 13** `LibraryBrowserViewModel` partial → owned class (1,088 lines; largest non-monolith liability). New post-Step-12 target: **`PixelVault.Native.cs` under ~900 lines**. |
| 2026-04-17 | **Step 8 Pass A done:** Steam / SteamGridDB / cover resolution block moved verbatim from `PixelVault.Native.cs` to new partial **`UI/Library/MainWindow.LibraryCoverResolution.cs`** (~817 lines). `PixelVault.Native.cs` now **1,262 lines** (was 2,058; **−796, −38.7 %**). Build + tests green (the two `LibraryPhotoMasonryLayoutTests` failures are pre-existing and unrelated to this move). Pass B (port to `LibraryCoverResolutionService` behind `ICoverService`) remains open. |
| 2026-04-17 | **Step 8 Pass B done:** Orchestration ported behind **`ILibraryCoverResolution`** (`Services/Covers/ILibraryCoverResolution.cs`, 76 lines) + **`LibraryCoverResolutionService`** (`Services/Covers/LibraryCoverResolutionService.cs`, 673 lines, `#nullable enable`). Wiring factory **`CreateLibraryCoverResolutionService`** in `MainWindow.StartupInitialization.cs`; ctor holds one `readonly ILibraryCoverResolution libraryCoverResolutionService` field. `UI/Library/MainWindow.LibraryCoverResolution.cs` collapsed **817 → 84 lines** (thin forwarders to the service for the orchestration methods + direct `ICoverService` forwarders for the custom/cached cover/hero/logo path helpers called by other partials). Dead code removed: `CustomCoverKey`, `EnrichLibraryFoldersWithSteamAppIdsAsync`, the three SteamGridDB-JSON parsers. `PixelVault.Native.cs` = **1,264 lines** (+2 for field + ctor line). Build green; `LibraryPhotoMasonryLayoutTests` continue to show the pre-existing flake (`PrefersRectanglesOverSquares`, `CompactDensityFitsMoreTilesIntoFirstRow`) — verified by stashing Pass B and re-running baseline. Step 8 complete. |

When execution starts, reference **`PV-PLN-UI-001`** in commits; Notion per **`docs/DOC_SYNC_POLICY.md`** if milestones are tracked there.
