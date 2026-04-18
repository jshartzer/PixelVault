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
| 249–418 | **WPF button/toolbar chrome factories** — `Btn`, `LibraryToolbarButtonStyle`, `BuildLibrary(Circle)ToolbarButtonTemplate`, `ApplyLibrary(Pill/Circle)Chrome`, `BuildRoundedTileButtonTemplate` (~170 lines) | **Step 9 done 2026-04-17** — bodies live in `UI/Library/LibraryButtonChrome.cs` (static); `PixelVault.Native.cs` keeps 9 one-line forwarders |
| 420–554 | `RefreshMainUi`, `ResolveStatusWindowOwner`, `RefreshActiveLibraryFolders`, `PickFolder` / `PickFile`, source-root helpers, `EnumerateSourceFiles`, `FindExecutableOnPath`, `OpenSourceFolders`, `ClearLibraryImageCaches` | Keep/trim (shell glue) |
| 555–652 | **`RunLibraryMetadataWorkflowWithProgress`** (~100 lines) | Step 2/3 follow-on → `MainWindow.LibraryMetadataScan.cs` next to `ShowLibraryMetadataScanWindow` |
| 654–662 | `ParseFilename` thin wrappers | Keep |
| 665–1490 | **Steam + SteamGridDB + cover resolution** — ~830 lines (the largest coherent block left) — `Resolve*SteamAppIdAsync`, `Resolve*SteamGridDbIdAsync`, `EnrichLibraryFoldersWithSteamAppIdsAsync`, `RefreshLibraryCoversAsync`, `ForceRefreshLibraryArtAsync`, custom + cached cover / hero / logo paths, SteamGridDB/Steam-store downloads | **Step 8** (new) — `ICoverService` / `Services/Covers/LibraryCoverResolutionService.cs` |
| 1492–1522 | `UpdateCachedLibraryFolderInfo` | Ride with Step 8 |
| 1523–1582 | Platform label helpers — `PrimaryPlatformLabel`, `FilenameGuessLabel`, `IsSteamManualExportWithoutAppId`, `PlatformGroupOrder`, `PreviewBadgeBrush` (~60 lines) | **Step 12 done 2026-04-18** — bodies live in `UI/Library/LibraryPlatformLabels.cs` (static, `#nullable enable`); `PixelVault.Native.cs` keeps 5 one-line forwarders so the ~dozen call sites across intake / manual-metadata / folder-tile partials resolve unchanged |
| 1584–1632 | Static text / path helpers — `ParseInt`/`Long`, `Sanitize`, `CleanComment`, `CleanTag`, `ParseTagText`, `SameManualText`, `Unique`, `EnsureDir`, `IsImage`, `IsPngOrJpeg`, `IsVideo`, `IsMedia`, `Quote`, `GetLibraryDate`, `SafeCacheName`, `NormalizeTitle`, `StripTags` (~50 lines) | **Step 12 done 2026-04-18** — bodies live in `Infrastructure/TextAndPathHelpers.cs` (static, `#nullable enable`); `PixelVault.Native.cs` keeps static-forwarder shims so `MainWindow.CleanTag` / `.IsImage` / `.Sanitize` / `.ParseTagText` / `.Unique` / `.EnsureDir` external callers (StartupInitialization, IndexServicesWiring, LibraryScanner, GameIndexCore, LibraryWorkspaceContext, LibraryBrowserShellBridge, LibraryScannerBridge) resolve unchanged. Dead `ParseSteamManualExportCaptureDate` and `ParseCaptureDate` removed. |
| 1634–1782 | **Persistent-data migration + `OpenFolder` / saved-covers readme** — `ResolvePersistentDataRoot`, `MigratePersistentDataFromLegacyVersions`, `CopyIfNewerOrMissing`, `CopyDirectoryContentsIfNewer`, `CopyDirectoryContentsIfMissing`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder` (~115 lines) | **Step 11 done 2026-04-17** — bodies live in `Infrastructure/PersistentDataMigrator.cs` (static, `#nullable enable`, 228 lines); `PixelVault.Native.cs` keeps 5 one-line forwarders so the `ResolvePersistentDataRoot` method-group pass to `ComputePersistentStorageLayout` and the ~14 call sites that bind `OpenFolder` / `OpenSavedCoversFolder` as delegates into `SettingsShellDependencies` / `PhotoIndexEditorHost` / `GameIndexEditorHost` / `HealthDashboardWindow` / palette / nav chrome / tile menus resolve unchanged |
| 1784–1938 | `OpenPhotoIndexEditor`, `OpenGameIndexEditor` wrappers (~155 lines) — already thin but build editor dependencies inline | Opportunistic (Step 7): move dependency builders next to each editor host |
| 1940–2058 | **Logging + troubleshooting redaction** — log file IO, `RotateTroubleshootingLogIfNeeded`, `LogException`, `FormatExceptionForTroubleshooting`, `RedactEmbeddedPathsForTroubleshooting`, `RedactBareWindowsPathForTroubleshooting`, `FormatPathForTroubleshooting`, `FormatViewKeyForTroubleshooting` (~120 lines) | **Step 10 done 2026-04-17** — bodies live in `Infrastructure/TroubleshootingLog.cs` (plain class, `#nullable enable`, 274 lines); `PixelVault.Native.cs` keeps 9 one-line forwarders so MainWindow partials and `SettingsShellDependencies` call sites resolve unchanged |

**Non-monolith liability worth naming:** `UI/Library/MainWindow.LibraryBrowserViewModel.cs` was **1,088 lines** at plan v2 — effectively an MVVM-style view model (folder view records, clones, projection fingerprints, persisted state) hanging off `MainWindow` as a partial. **Step 13 done 2026-04-18** dropped it to **714 lines** by un-nesting `LibraryBrowserFolderView` (top-level `internal sealed class`), extracting the pure static timeline / packed-row / variable-tile / fingerprint / "All" merge tail to **`UI/Library/LibraryBrowserViewModelMath.cs`** (437 lines, `#nullable enable`), and lifting the merged-projection cache state + `GetOrBuild` flow to **`UI/Library/LibraryBrowserProjectionCache.cs`** (64 lines, `#nullable enable`). The partial keeps thin static / instance forwarders so existing call sites resolve unchanged.

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

**Done 2026-04-17.** `UI/Library/LibraryButtonChrome.cs` (190 lines, `#nullable enable`) owns all nine factories as `public static` methods; brushes come directly from `UiBrushHelper.FromHex` so no MainWindow state is captured. `PixelVault.Native.cs` keeps nine thin one-line instance forwarders (20 lines total including header) so the ~70 existing call sites across MainWindow partials (`LibraryBrowserLayout`, `LibraryBrowserChrome`, `LibraryBrowserQuickEditDrawer`, `LibraryBrowserOrchestrator.Selection` / `.FolderTile`, `LibraryBrowserToastAndShortcuts`, `ManualMetadata.Layout`, `SystemTray`, `ImportWorkflow.Progress`, `ImportSummaryDialogs`, `LibraryFolderIdEditor`, `PhotographyAndSteam`, `LibraryMetadataScan`, `LibraryBrowserOrchestrator.CoverRefresh`) and the dependency-delegate wirings (`MainWindow.SettingsShell` binds `Btn = Btn` into `SettingsShellDependencies`; Intake / Diagnostics windows consume the same `Func` captured at construction) keep resolving unchanged. `PixelVault.Native.cs` dropped to **1,137 lines** (−127 vs Pass B). Build green; only pre-existing `LibraryPhotoMasonryLayoutTests` flake seen.

---

### Step 10 — Troubleshooting log + redaction helper (NEW)

**Deliverable:** Extract `PixelVault.Native.cs` lines ~1940–2058 (log file IO, `RotateTroubleshootingLogIfNeeded`, `LogException`, `FormatExceptionForTroubleshooting`, `RedactEmbeddedPathsForTroubleshooting`, `RedactBareWindowsPathForTroubleshooting`, `RedactPathMatchForTroubleshooting`, `FormatPathForTroubleshooting`, `FormatViewKeyForTroubleshooting`, `TroubleshootingSegmentLooksLikePath`) to **`Infrastructure/TroubleshootingLog.cs`** as a plain class with injected dependencies (paths, `logFileSync`, redact flag getter, clock). `MainWindow.Log` / `LogException` / `LogTroubleshooting` become thin forwarders.

**iOS alignment:** **Contract-shaped** — redaction rules are mobile-safe already (no WPF); the service is a port the mobile app could also consume once it exists. `#nullable enable` in the new file.

**TEST GATE:** Normal library session produces `PixelVault-native.log` as before; enable troubleshooting in Path Settings, drive an error, confirm `PixelVault-troubleshooting.log` rotates past the 5 MB limit, confirm paths are redacted when the checkbox is on. Add unit coverage for the redaction regexes (currently untested).

**Done 2026-04-17.** `Infrastructure/TroubleshootingLog.cs` (274 lines, `#nullable enable`) owns the main + troubleshooting log file IO, rotation, redaction, and formatting — bodies ported verbatim so file-on-disk shape, regex behavior, and the 5 MB rotation threshold stay byte-identical. `TroubleshootingLogDependencies` injects `LogsRoot`, `IsTroubleshootingLoggingEnabled`, `RedactPathsEnabled`, `DiagnosticsSessionId`, and the rotation cap; the flag getters read `MainWindow` fields so Settings checkbox toggles still apply without service recreation. `logFileSync` and `TroubleshootingLogMaxBytes` moved into the service. `PixelVault.Native.cs` keeps nine thin one-line forwarders (`LogFilePath`, `TroubleshootingLogFilePath`, `TryReadLogFile`, `Log`, `LogException`, `LogTroubleshooting`, `FormatExceptionForTroubleshooting`, `FormatPathForTroubleshooting`, `FormatViewKeyForTroubleshooting`) so MainWindow partials (`LibraryBrowserViewModel`, `LibraryBrowserOrchestrator.FolderTile` / `.FolderData`, `LibraryBrowserRender.DetailPane`, `SettingsShell`) and every `Log(...)` / `LogException(...)` / `LogTroubleshooting(...)` call site resolve unchanged. New `TroubleshootingLogRedactionTests` (194 lines, 18 cases) covers drive-letter / UNC / `\\?\` / DIAG key=value / stack-frame `:line` paths, `FormatViewKey` selective redaction, `SegmentLooksLikePath` edge cases, `FormatException` truncation, and a file-IO round-trip that verifies `DIAG | S=... | T=... | Area | body` shape with redaction on. `PixelVault.Native.cs` dropped to **931 lines** (−206 vs Step 9 — first time under the ~1k bar). Build green; only pre-existing `LibraryPhotoMasonryLayoutTests` flake seen.

---

### Step 11 — Persistent-data migration helper (NEW)

**Deliverable:** Lines ~1634–1782 — `ResolvePersistentDataRoot`, `MigratePersistentDataFromLegacyVersions`, `CopyIfNewerOrMissing`, `CopyDirectoryContentsIfNewer`, `CopyDirectoryContentsIfMissing`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder`, `OpenFolder` — move to **`Infrastructure/PersistentDataMigrator.cs`** as static helpers taking explicit paths. `MainWindow` ctor calls the migrator once; `ComputePersistentStorageLayout` already has the path shape.

**iOS alignment:** **Shell-only** (Windows paths), but lets the migrator be unit-tested with a `TempFileSystem` fixture.

**TEST GATE:** Cold start in a fresh data root; cold start in a data root containing only `dist/` layout from 0.075.x; start after dropping old cover PNGs in `savedCoversRoot`. Existing `docs/MANUAL_GOLDEN_PATH_CHECKLIST.md` "first run" section applies.

**Done 2026-04-17.** `Infrastructure/PersistentDataMigrator.cs` (228 lines, `#nullable enable`) owns all six bodies as `public static` / `internal static` helpers — `ResolvePersistentDataRoot`, `MigrateFromLegacyVersions`, `CopyIfNewerOrMissing`, `CopyDirectoryContentsIfNewer` (kept for parity; currently unused by callers), `CopyDirectoryContentsIfMissing`, `OpenFolder`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder`. Bodies ported verbatim so the `dist/PixelVault-VERSION` → `<dist parent>/PixelVaultData` probe order, the dev-checkout walk-up, the copy thresholds (length + `LastWriteTimeUtc`), the "PixelVaultData authoritative once it exists" invariant, and the primary-shell / `explorer.exe`-fallback order in `OpenFolder` stay byte-identical. A `static readonly Regex LegacyReleaseFolderRegex` and a `const string SavedCoversReadmeBody` make the one loose regex and the README body explicit. `MainWindow` keeps five one-line forwarders (`ResolvePersistentDataRoot`, `MigratePersistentDataFromLegacyVersions`, `OpenFolder`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder`) so (a) `ComputePersistentStorageLayout(appRoot, ResolvePersistentDataRoot)` still compiles as a method-group pass, (b) `d.OpenFolder = OpenFolder` / `d.OpenSavedCoversFolder = OpenSavedCoversFolder` in `SettingsShellDependencies` / `PhotoIndexEditorHost` / `GameIndexEditorHost` / `HealthDashboardWindow` / `LibraryBrowserPaletteContext` resolve unchanged, and (c) the ~14 direct call sites across MainWindow partials (`LibraryBrowserOrchestrator.FolderTile` / `.NavChromeAndToolbar`, `LibraryBrowserPhotoHero`, `LibraryBrowserQuickEditDrawer`, `LibraryBrowserPaletteCommands`, `PhotographyAndSteam`, `ManualMetadata`, `LibraryVirtualization`) never changed. Added **`PersistentDataMigratorTests`** (313 lines, 20 cases) covering: `dist/PixelVault-VERSION` → sibling `PixelVaultData`; `PixelVault-current` shim folder; dev-checkout walk-up (`PixelVaultData/` + `src/PixelVault.Native/` coexist); non-matching folder-name fallback; `CopyIfNewerOrMissing` create / skip-same-size-newer / overwrite-stale / overwrite-different-size / noop-when-source-missing; `CopyDirectoryContentsIfMissing` recursive tree copy, authoritative-destination preservation, missing-source noop; `MigrateFromLegacyVersions` equal-path noop, full copy into fresh `PixelVaultData`, authoritative-cache preservation, sibling-release fill; `EnsureSavedCoversReadme` create / don't-overwrite / swallow-and-log-on-invalid-path. `PixelVault.Native.cs` dropped to **802 lines** (−129 vs Step 10 — clears the ~900-line target from the v2 plan). Build green; same two pre-existing `LibraryPhotoMasonryLayoutTests` failures (unrelated to this slice) on the full suite — new migrator suite is 20/20 green.

---

### Step 12 — Small text / path / platform statics (NEW)

**Deliverable:** Lines ~1584–1632 (pure text / path / media-type statics) → **`Infrastructure/TextAndPathHelpers.cs`** (`#nullable enable`). Fold the platform label helpers at lines ~1523–1582 (`PrimaryPlatformLabel`, `FilenameGuessLabel`, `IsSteamManualExportWithoutAppId`, `PlatformGroupOrder`, `PreviewBadgeBrush`) into **`UI/Library/LibraryPlatformLabels.cs`**. Both groups have multiple callers across partials, so keep the same signatures and let the wrappers in `MainWindow` delegate.

**iOS alignment:** **Contract-shaped** — platform-label logic is data-layer; no WPF touches except the preview badge brush (stays in a WPF-only helper).

**TEST GATE:** Sort/group pills, manual-metadata badges, intake platform grouping, filename-convention preview. Existing filename-parser tests catch most behavior; add focused `TextAndPathHelpersTests` + `LibraryPlatformLabelsTests` that lock the observable strings / brushes.

**Done 2026-04-18.** Two new files:

- **`Infrastructure/TextAndPathHelpers.cs`** (166 lines, `#nullable enable`) owns **`ParseInt` / `ParseLong` / `FormatFriendlyTimestamp` / `Sanitize` / `CleanComment` / `CleanTag` / `ParseTagText` / `SameManualText` / `Unique` / `EnsureDir` / `IsImage` / `IsPngOrJpeg` / `IsVideo` / `IsMedia` / `Quote` / `NormalizeTitle` / `SafeCacheName` / `StripTags`**. All bodies verbatim, including the Windows-1252 mojibake scrubbing in `NormalizeTitle` (`â„¢` / `Â®` / `Â©` → space, as found in scraped Steam / store titles). **`GetLibraryDate`** now takes a pre-parsed `FilenameParseResult` so the helper is parser-free and pure; the `MainWindow` instance wrapper does the single `ParseFilename(file)` call and forwards.
- **`UI/Library/LibraryPlatformLabels.cs`** (85 lines, `#nullable enable`) owns **`PrimaryPlatformLabel(FilenameParseResult)` / `FilenameGuessLabel(FilenameParseResult)` / `IsSteamManualExportWithoutAppId(FilenameParseResult)` / `PlatformGroupOrder(string)` / `PreviewBadgeBrush(string)`**. The four data-shaped methods take `FilenameParseResult` so they stay parser-free; `PreviewBadgeBrush` is the lone WPF-only tail (uses `UiBrushHelper.FromHex`).

`MainWindow` keeps **5 instance forwarders** for the platform-label methods (so the ~dozen call sites across `ManualMetadata.Helpers`, `ManualMetadata`, `IntakePreview`, `MetadataReview`, `LibraryBrowserViewModel`, `LibraryBrowserRender.FolderList`, `LibraryBrowserOrchestrator.FolderTile`, `LibraryBrowserQuickEditDrawer`, and the method-group captures in `IntakePreview.PreviewBadge` / `PlatformOrder` / `FilenameGuess` / `MetadataReview.PreviewBadge` resolve unchanged) and **18 static forwarders** for the pure helpers (so `MainWindow.CleanTag` / `.IsImage` / `.IsVideo` / `.ParseTagText` / `.Sanitize` / `.Unique` / `.EnsureDir` external callers — `StartupInitialization` (×10), `IndexServicesWiring` (×8), `LibraryScanner` (×2), `GameIndexCore`, `LibraryWorkspaceContext`, `LibraryBrowserShellBridge`, `LibraryScannerBridge` — resolve unchanged). `GetLibraryDate` keeps an instance forwarder so the method-group captures in `IntakeAnalysisService`, `LibraryMetadataEditing`, `PhotographyAndSteam`, `LibraryVirtualization`, `ImportWorkflow.Steps` still bind. The dead `ParseSteamManualExportCaptureDate` and `ParseCaptureDate` instance methods (zero in-tree callers) were removed.

Added **`TextAndPathHelpersTests`** (53 cases — `ParseInt` / `ParseLong` invalid fallbacks; `FormatFriendlyTimestamp` 12-hour padding at 00:05, 12:00, 23:15; `Sanitize` strips invalid-filename chars and collapses whitespace; `CleanComment` collapses CRLF/tabs/nulls; `CleanTag` trims; `ParseTagText` splits on `,;\r\n` and dedupes case-insensitively; `SameManualText` trims+Ordinal; `Unique` returns input / creates `" (2)"`/`" (3)"` candidates; `EnsureDir` throws on empty/missing, noop on present; `IsImage` / `IsPngOrJpeg` / `IsVideo` / `IsMedia` cover every supported extension; `Quote` wraps only when spaces present and escapes inner quotes; `NormalizeTitle` scrubs hyphens/colons/underscores, strips HTML entities (`&#39;` → space-gapped), scrubs the Windows-1252 mojibake TM/R/C bytes; `SafeCacheName` replaces spaces with underscores; `StripTags` removes `<...>`; `GetLibraryDate` prefers parsed capture time for non-Xbox, falls back to earlier-of-created/modified for Xbox even when capture time present, handles null `PlatformTags`). Added **`LibraryPlatformLabelsTests`** (35 cases — `FilenameGuessLabel` all four branches (Steam AppID wins over everything, non-Steam ID wins over manual hint, manual hint fires when no IDs + Steam routing, platform label fallback including case-insensitive `other` → "No confident match"); `IsSteamManualExportWithoutAppId` true only when all three gates pass (routes to manual AND no Steam AppID AND no non-Steam ID); `PlatformGroupOrder` covers all 8 known labels plus "PlayStation" / empty / unknown → 8; `PreviewBadgeBrush` asserts `SolidColorBrush` with the exact `#FF...` ARGB for Xbox / Xbox PC / Steam / Emulation / PC / PS5 / PlayStation / unknown).

`PixelVault.Native.cs` dropped to **742 lines** (−60 vs Step 11 — well below the former ~900 / ~700 targets). Build green; full suite 424/425 (same one pre-existing `LibraryPhotoMasonryLayoutTests.BuildLibraryDetailMasonryChunks_PrefersRectanglesOverSquares` flake from Step 8 — unrelated to this slice). New Step 12 suite is 88/88 green.

---

### Step 13 — Library browser view-model partial → owned class

**Status — done 2026-04-18.** Landed as three vertical sub-passes (mirrors Step 8 Pass A/B):

- **Pass A (commit `a69119e`).** `LibraryBrowserFolderView` un-nested from `MainWindow` to top-level `internal sealed class LibraryBrowserFolderView` in **`UI/Library/LibraryBrowserFolderView.cs`** (45 lines). External references in `LibraryBrowseFolderSummary.cs`, `ILibraryBrowserShell.cs`, and three test files (`LibraryBrowserFolderFilterTests`, `LibraryBrowseFolderSummaryTests`, `LibraryBrowserCombinedMergeTests`) switched from `MainWindow.LibraryBrowserFolderView` to the short name (same namespace). Plain classes (and future iOS / backend projections) can now reach the read-model without going through `MainWindow`.
- **Pass B (commit `57cf3c4`).** Extracted **`UI/Library/LibraryBrowserViewModelMath.cs`** (437 lines, `#nullable enable`) — the pure static helpers that previously lived as `internal static` members on the partial: timeline date math (`NormalizeLibraryTimelineDateRange`, `BuildLibraryTimelinePresetDateRange`, `DetectLibraryTimelinePresetKey`, `LibraryTimelineRangeContainsCapture`, `TryAlignLibraryTimelineRollingPresetToToday`, `BuildLibraryTimelineSummaryText`, `BuildLibraryTimelineCaptureTimeLabel`, `BuildLibraryTimelineDayCardTitle`), packed-card layout (`Calculate*PackedTileSize` / `*PackedCardColumns`, both `EstimateLibraryTimelinePackedCardWidth`/`Height` overloads, `BuildLibraryTimelinePackedRows`, both `EstimateLibraryPackedDayCardDesiredWidth` overloads, `ExpandLibraryPackedRowWidths`), variable-tile sizing (`LibraryDetailFileLayoutHash`, `ResolveLibraryVariableDetailTileWidth`, `PackLibraryDetailFilesIntoVariableRows`, `EstimateLibraryVariableDetailRowHeight` — defers to the existing `MainWindow.ResolveLibraryDetailAspectRatio` internal-static), fingerprint + "All" merge tail (`ComputeLibraryBrowserFoldersMergeFingerprint`, `MergeLibraryBrowserExternalIdsForCombinedView`, `MergeLibraryBrowserNonSteamIdForCombinedView`, `MergeLibraryBrowserRetroAchievementsGameIdForCombinedView`, `MergeLibraryBrowserCollectionNotesForCombinedView`). The partial keeps one-line static forwarders so existing call sites (`MainWindow.LibraryBrowserShowOrchestration`, `LibraryTimelineModeTests`, `LibraryBrowserCombinedMergeTests`) resolve unchanged. Added **`LibraryBrowserViewModelMathTests`** (20 cases) covering preset round-trip, packed-row layout, variable-tile clamp, fingerprint stability + change-detection, and Steam/Emulation/RetroAchievements merge-pick rules.
- **Pass C.** Extracted **`UI/Library/LibraryBrowserProjectionCache.cs`** (64 lines, `#nullable enable`) — the cache state (`_libraryBrowserAllMergeProjection*` fields) and the `GetOrBuild` flow that lets us skip rebuilding merged folder rows when the source list is unchanged. `MainWindow` keeps a `readonly LibraryBrowserProjectionCache _libraryBrowserProjectionCache = new();` and `GetOrBuildLibraryBrowserFolderViews(folders, mode) => _libraryBrowserProjectionCache.GetOrBuild(folders, mode, NormalizeLibraryGroupingMode, BuildLibraryBrowserFolderViews);`. The cache exposes `Reset()` + `HasCachedProjection` / `CachedFingerprint` for tests / diagnostics. Added **`LibraryBrowserProjectionCacheTests`** (7 cases) covering hit / miss / console-clears-cache / `Reset` / null-folders / null-delegate guard.

**Outcome.** `UI/Library/MainWindow.LibraryBrowserViewModel.cs` dropped from **1,088 → 714 lines** (−374, −34 %); the largest non-monolith partial is now well under the Step-13 ~600-line stretch. New backing files total 546 lines (45 + 437 + 64) but are **plain top-level classes** with no `MainWindow` references — directly consumable by tests today and by future `ILibrarySession` / iOS / backend projections without re-extraction.

**iOS alignment:** **Contract-shaped** — `LibraryBrowserFolderView` is the library's read-model record; `LibraryBrowserViewModelMath` is parameterized by plain delegates (no WPF, no `MainWindow`); `LibraryBrowserProjectionCache` accepts a `Func<...>` builder so an alternate front-end can wire its own merged-projection builder behind the same cache contract. Matches the `LibraryQueryResult` direction in `docs/ios_foundation_guide.md`.

**Deferred (intentional).** A full move of the remaining ~700 lines of `MainWindow.LibraryBrowserViewModel.cs` into a non-partial `LibraryBrowserViewModel` instance class with `ILibrarySession` + persisted-state ctor dependencies would touch ~15 distinct `MainWindow` helpers (`CloneLibraryFolderInfo`, `NormalizeGameId`, `NormalizeGameIndexName`, `NormalizeConsoleLabel`, `GuessGameIndexNameForFile`, `ResolveIndexedLibraryDate`, `ResolveLibraryFileRecentSortUtcTicks`, `TryGetLibraryMetadataIndexEntry`, `GetLibraryFolderNewestDate`, `SameLibraryFolderSelection`, plus the `libraryRoot` / `libraryGroupingMode` fields) and ~370 external call sites. That's a Step-14 candidate — current shape (un-nested DTO + pure-static math + cache class) already gives tests + iOS clients direct access to the read-model without paying the larger extraction cost.

---

## Metrics

| Metric | Apr 6 | Apr 9 | Apr 12 | Apr 17 (plan v2) | Apr 17 (Step 8A) | Apr 17 (Step 8B) | Apr 17 (Step 9) | Apr 17 (Step 10) | Apr 17 (Step 11) | Apr 18 (Step 12) | **Apr 18 (Step 13)** | Direction |
|--------|------:|------:|-------:|------:|------:|------:|------:|------:|------:|------:|------:|-----------|
| `PixelVault.Native.cs` lines | ~2.9k → ~2.1k | ~2.1k | ~1.9k | 2,058 | 1,262 | 1,264 | 1,137 | 931 | 802 | 742 | **742** (hold — Step 13 targets the `LibraryBrowserViewModel` partial, not the monolith) | Step 13 drains the largest partial, not `PixelVault.Native.cs` |
| `MainWindow` partial count | ~40 | ~52 | ~54 | ~55 | ~56 | ~56 | ~56 | ~56 | ~56 | ~56 | **~56** (no partial added — all three new files are plain classes) | Holds |
| `UI/Library/MainWindow.LibraryCoverResolution.cs` lines | — | — | — | — | 817 | 84 | 84 | 84 | 84 | 84 | **84** | Holds |
| `Services/Covers/` cover-resolution surface (lines) | — | — | — | — | — | 749 | 749 | 749 | 749 | 749 | **749** | Holds |
| `UI/Library/LibraryButtonChrome.cs` lines | — | — | — | — | — | — | 190 | 190 | 190 | 190 | **190** | Holds |
| `Infrastructure/TroubleshootingLog.cs` lines | — | — | — | — | — | — | — | 274 | 274 | 274 | **274** | Holds |
| `Infrastructure/PersistentDataMigrator.cs` lines | — | — | — | — | — | — | — | — | 228 | 228 | **228** | Holds |
| `Infrastructure/TextAndPathHelpers.cs` lines | — | — | — | — | — | — | — | — | — | 166 | **166** | Holds |
| `UI/Library/LibraryPlatformLabels.cs` lines | — | — | — | — | — | — | — | — | — | 85 | **85** | Holds |
| `UI/Library/LibraryBrowserFolderView.cs` lines | — | — | — | — | — | — | — | — | — | — | **45** (new — un-nested DTO) | Contract-shaped read-model record |
| `UI/Library/LibraryBrowserViewModelMath.cs` lines | — | — | — | — | — | — | — | — | — | — | **437** (new) | Pure static timeline / layout / fingerprint / merge statics |
| `UI/Library/LibraryBrowserProjectionCache.cs` lines | — | — | — | — | — | — | — | — | — | — | **64** (new) | Owns merged-projection cache state + `GetOrBuild` |
| `LibraryBrowserViewModelMathTests` cases | — | — | — | — | — | — | — | — | — | — | **20** | Locks timeline / packed-row / variable-tile / fingerprint / merge-pick behavior |
| `LibraryBrowserProjectionCacheTests` cases | — | — | — | — | — | — | — | — | — | — | **7** | Cache hit / miss / console-clears / reset / null-guard |
| `TextAndPathHelpersTests` cases | — | — | — | — | — | — | — | — | — | 53 | **53** | Holds |
| `LibraryPlatformLabelsTests` cases | — | — | — | — | — | — | — | — | — | 35 | **35** | Holds |
| `PersistentDataMigratorTests` cases | — | — | — | — | — | — | — | — | 20 | 20 | **20** | Holds |
| `TroubleshootingLogRedactionTests` cases | — | — | — | — | — | — | — | 18 | 18 | 18 | **18** | Holds |
| Largest non-monolith partial (lines) | — | — | — | 1,088 (`LibraryBrowserViewModel`) | 1,088 | 1,088 | 1,088 | 1,088 | 1,088 | 1,088 | **714** (`LibraryBrowserViewModel`) | −374 vs plan v2; under the ~600 stretch would take the deferred Step 14 |
| New domain logic in `MainWindow` | Discouraged | — | — | Still discouraged | Still discouraged | Still discouraged | Still discouraged | Still discouraged | Still discouraged | Still discouraged | Still discouraged | **Zero** for new features per `ios_foundation_guide` checklist |

**Target after Steps 8–12:** `PixelVault.Native.cs` under **~900 lines** — essentially fields, ctor delegation, shell glue, and the thin wrappers that partials call. **Cleared at Step 11 (802 lines); Step 12 drops another 60 → 742 lines**, which also meets the stretch "Step 12 + Step 13 should leave the monolith in the ~700-line range" — without touching Step 13.

**Step 13 target:** push the largest non-monolith partial (`UI/Library/MainWindow.LibraryBrowserViewModel.cs`) below ~600 lines. **Partial cleared 2026-04-18 to 714 lines** by un-nesting the `LibraryBrowserFolderView` DTO and extracting pure-math statics + projection-cache state to three plain top-level classes (45 + 437 + 64 = 546 lines of contract-shaped / parameterizable code). The final ~115 lines to ~600 would require promoting the partial to a non-partial `LibraryBrowserViewModel` class with `ILibrarySession` / persisted-state ctor dependencies — deferred as **Step 14** (see Step 13 deferred notes).

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
| 2026-04-17 | **Step 9 done:** WPF toolbar / button chrome factories (~170 lines) moved from `PixelVault.Native.cs` to new static class **`UI/Library/LibraryButtonChrome.cs`** (190 lines, `#nullable enable`). All nine helpers (`Btn`, `LibraryToolbarButtonStyle`, `BuildLibraryToolbarButtonTemplate`, `ApplyLibraryToolbarChrome`, `ApplyLibraryPillChrome`, `LibraryCircleToolbarButtonStyle`, `BuildLibraryCircleToolbarButtonTemplate`, `ApplyLibraryCircleToolbarChrome`, `BuildRoundedTileButtonTemplate`) are `public static` — brushes come from `UiBrushHelper.FromHex`, so no MainWindow state is captured. `PixelVault.Native.cs` keeps nine one-line instance forwarders so the ~70 existing call sites across partials and dependency-delegate wirings (`MainWindow.SettingsShell` binds `Btn` into `SettingsShellDependencies`; Intake / Diagnostics / Metadata Review windows receive the same `Func` at construction) resolve unchanged. `PixelVault.Native.cs` = **1,137 lines** (−127 vs Pass B). Build green; `LibraryPhotoMasonryLayoutTests` continue to show the same pre-existing flake. |
| 2026-04-17 | **Step 10 done:** Troubleshooting log file IO + rotation + path redaction (~200 lines) moved from `PixelVault.Native.cs` to new plain class **`Infrastructure/TroubleshootingLog.cs`** (274 lines, `#nullable enable`). `TroubleshootingLogDependencies` captures `LogsRoot`, `IsTroubleshootingLoggingEnabled`, `RedactPathsEnabled`, `DiagnosticsSessionId`, and a `MaxTroubleshootingBytes` cap; flag getters read `MainWindow` fields so Settings checkbox toggles still apply without service recreation. `logFileSync` and the 5 MB rotation threshold moved into the service. `MainWindow` keeps nine one-line forwarders (`LogFilePath`, `TroubleshootingLogFilePath`, `TryReadLogFile`, `Log`, `LogException`, `LogTroubleshooting`, `FormatExceptionForTroubleshooting`, `FormatPathForTroubleshooting`, `FormatViewKeyForTroubleshooting`) — `Log(message)` still owns the WPF `logBox` append, the service handles file IO and returns the formatted timestamped line so the shell echoes the same bytes. Added **`TroubleshootingLogRedactionTests`** (194 lines, 18 cases) covering drive-letter / UNC / `\\?\` / DIAG `key=value` / stack-frame `:line` paths, `FormatViewKey` selective redaction, `SegmentLooksLikePath` edge cases, `FormatException` truncation, a `LogTroubleshooting` disabled-noop check, and a file-IO round-trip that verifies `DIAG \| S=... \| T=... \| Area \| body` with path redaction on. `PixelVault.Native.cs` dropped to **931 lines** (−206 vs Step 9 — first time under ~1k). Build green; `LibraryPhotoMasonryLayoutTests` continue to show the same pre-existing flake (unrelated). |
| 2026-04-17 | **Step 11 done:** Persistent-data first-run migration + open-folder shell glue (~150 lines) moved from `PixelVault.Native.cs` to new static class **`Infrastructure/PersistentDataMigrator.cs`** (228 lines, `#nullable enable`). Bodies ported verbatim so the `dist/PixelVault-VERSION` → sibling `PixelVaultData` probe, the dev-checkout walk-up (`PixelVaultData/` + `src/PixelVault.Native/` coexist), the copy thresholds (length + `LastWriteTimeUtc`), the "PixelVaultData authoritative once it exists" invariant, and the primary-shell / `explorer.exe`-fallback order in `OpenFolder` stay byte-identical. `MainWindow` keeps five one-line forwarders (`ResolvePersistentDataRoot`, `MigratePersistentDataFromLegacyVersions`, `OpenFolder`, `EnsureSavedCoversReadme`, `OpenSavedCoversFolder`) so the `ComputePersistentStorageLayout(appRoot, ResolvePersistentDataRoot)` method-group pass and the ~14 call sites that bind `OpenFolder` / `OpenSavedCoversFolder` as delegates into `SettingsShellDependencies` / `PhotoIndexEditorHost` / `GameIndexEditorHost` / `HealthDashboardWindow` / palette / nav chrome / folder tile / photo hero / quick-edit drawer / manual metadata / photography & steam all resolve unchanged. Added **`PersistentDataMigratorTests`** (313 lines, 20 cases) covering the three `ResolvePersistentDataRoot` branches (`dist/PixelVault-0.076`, `PixelVault-current` shim, dev-checkout walk-up, non-matching fallback), all four `CopyIfNewerOrMissing` paths (create / skip-same-size-newer / overwrite-stale / overwrite-different-size / noop-missing-source), `CopyDirectoryContentsIfMissing` recursion + authoritative-destination preservation + missing-source noop, `MigrateFromLegacyVersions` equal-path noop / fresh-destination full copy / authoritative-cache preservation / sibling-release fill, and the three `EnsureSavedCoversReadme` branches (create, don't-overwrite, swallow-and-log-on-invalid-path). `PixelVault.Native.cs` dropped to **802 lines** (−129 vs Step 10) — clears the ~900-line post-v2 target. Build green; full suite 335 passing / 2 failing (the same pre-existing `LibraryPhotoMasonryLayoutTests` flakes tracked through Steps 8–10, unrelated to this slice). |
| 2026-04-18 | **Step 13 done:** `UI/Library/MainWindow.LibraryBrowserViewModel.cs` thinned **1,088 → 714 lines** (−374, −34 %) across three vertical sub-passes: **Pass A** un-nested `LibraryBrowserFolderView` to top-level `internal sealed class` in **`UI/Library/LibraryBrowserFolderView.cs`** (45 lines, `#nullable enable`) — external references in `LibraryBrowseFolderSummary.cs`, `ILibraryBrowserShell.cs`, and three test files (`LibraryBrowserFolderFilterTests`, `LibraryBrowseFolderSummaryTests`, `LibraryBrowserCombinedMergeTests`) updated from `MainWindow.LibraryBrowserFolderView` to the un-qualified name (same namespace), leaving method-group references like `MainWindow.LibraryBrowserFolderViewMatchesFilter` unchanged. **Pass B** extracted **`UI/Library/LibraryBrowserViewModelMath.cs`** (437 lines, `#nullable enable`) with the pure timeline / packed-card / variable-tile / fingerprint / "All" merge helpers (`NormalizeLibraryTimelineDateRange`, `TryAlignLibraryTimelineRollingPresetToToday`, `BuildLibraryTimelinePackedRows`, `EstimateLibraryVariableDetailRowHeight` — defers to `MainWindow.ResolveLibraryDetailAspectRatio`, `ComputeLibraryBrowserFoldersMergeFingerprint`, `MergeLibraryBrowserExternalIdsForCombinedView`, `MergeLibraryBrowserRetroAchievementsGameIdForCombinedView`, `MergeLibraryBrowserCollectionNotesForCombinedView`, plus the packed-card geometry statics). The partial keeps thin static forwarders so `LibraryBrowserShowOrchestration`, `LibraryTimelineModeTests`, `LibraryBrowserCombinedMergeTests`, and `LibraryPhotoMasonryLayoutTests` resolve unchanged. Added **`LibraryBrowserViewModelMathTests`** (20 cases) covering preset round-trip, packed-row layout, variable-tile clamp, fingerprint stability + change-detection, Steam / Emulation / RetroAchievements / collection-note merge-pick rules. **Pass C** extracted **`UI/Library/LibraryBrowserProjectionCache.cs`** (64 lines, `#nullable enable`) — owns the merged-projection fingerprint + cached `List<LibraryBrowserFolderView>` and the `GetOrBuild(folders, mode, normalizeMode, build)` flow (console mode never caches). `MainWindow` keeps a single `readonly LibraryBrowserProjectionCache _libraryBrowserProjectionCache = new();` instance and delegates `GetOrBuildLibraryBrowserFolderViews` through it; cache exposes `Reset()` + `HasCachedProjection` / `CachedFingerprint` for tests / diagnostics. Added **`LibraryBrowserProjectionCacheTests`** (7 cases) covering hit / miss / console-clears-cache / `Reset` / null-folders / null-delegate guard. All three new files are plain top-level classes (no `MainWindow` references) — directly consumable by tests, `ILibrarySession`, and future iOS / backend clients. Build green; full suite 451/452 (same pre-existing `LibraryPhotoMasonryLayoutTests` flake tracked through Steps 8–12, unrelated to this slice). The final ~115 lines needed to reach the ~600 stretch target were intentionally **deferred as a future Step 14** — promoting the partial to a non-partial `LibraryBrowserViewModel` instance class with `ILibrarySession` + persisted-state ctor dependencies would touch ~15 `MainWindow` helpers and ~370 call sites; the current shape (un-nested DTO + pure-math statics + projection-cache class) already surfaces the library read-model to tests / iOS without paying that cost. |
| 2026-04-18 | **Step 12 done:** Small text / path / platform statics (~110 lines) moved from `PixelVault.Native.cs` to two new static classes: **`Infrastructure/TextAndPathHelpers.cs`** (166 lines, `#nullable enable`) owns `ParseInt` / `ParseLong` / `FormatFriendlyTimestamp` / `Sanitize` / `CleanComment` / `CleanTag` / `ParseTagText` / `SameManualText` / `Unique` / `EnsureDir` / `IsImage` / `IsPngOrJpeg` / `IsVideo` / `IsMedia` / `Quote` / `NormalizeTitle` / `SafeCacheName` / `StripTags` / `GetLibraryDate` — bodies verbatim, including the Windows-1252 mojibake scrubbing in `NormalizeTitle`; `GetLibraryDate` now takes a pre-parsed `FilenameParseResult` so the helper is parser-free and pure. **`UI/Library/LibraryPlatformLabels.cs`** (85 lines, `#nullable enable`) owns `PrimaryPlatformLabel(FilenameParseResult)` / `FilenameGuessLabel(FilenameParseResult)` / `IsSteamManualExportWithoutAppId(FilenameParseResult)` / `PlatformGroupOrder(string)` / `PreviewBadgeBrush(string)` — the four data-shaped methods take `FilenameParseResult` so they stay parser-free; `PreviewBadgeBrush` is the lone WPF tail (via `UiBrushHelper.FromHex`). `MainWindow` keeps 18 static forwarders (so external `MainWindow.CleanTag` / `.IsImage` / `.IsVideo` / `.Sanitize` / `.ParseTagText` / `.Unique` / `.EnsureDir` callers across `StartupInitialization` / `IndexServicesWiring` / `LibraryScanner` / `GameIndexCore` / `LibraryWorkspaceContext` / `LibraryBrowserShellBridge` / `LibraryScannerBridge` resolve unchanged) + 5 instance forwarders for the platform-label methods + 1 instance forwarder for `GetLibraryDate` (keeps method-group captures in `IntakeAnalysisService`, `LibraryMetadataEditing`, `PhotographyAndSteam`, `LibraryVirtualization`, `ImportWorkflow.Steps` binding). Dead `ParseSteamManualExportCaptureDate` and `ParseCaptureDate` (zero callers) removed. Added **`TextAndPathHelpersTests`** (53 cases) and **`LibraryPlatformLabelsTests`** (35 cases) — together lock the observable strings / brushes / library-date branches (88/88 green). `PixelVault.Native.cs` dropped to **742 lines** (−60 vs Step 11) — clears both the Step-12 goal (~700–800) and the Step-12+13 stretch (~700-line range) in a single slice without touching Step 13. Build green; full suite 424/425 (same pre-existing `LibraryPhotoMasonryLayoutTests.BuildLibraryDetailMasonryChunks_PrefersRectanglesOverSquares` flake tracked through Steps 8–11, unrelated to this slice). |

When execution starts, reference **`PV-PLN-UI-001`** in commits; Notion per **`docs/DOC_SYNC_POLICY.md`** if milestones are tracked there.
