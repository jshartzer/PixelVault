# PixelVault Handoff

This file is the short current-state handoff.

October 4 Hub Step 4 data foundation: background notification checks now merge into the hub store for all library games (no extra requests; saved rarity/counters preserved; unchanged writes skipped; in-memory file cache). Live: 107 of 189 games stored in 40 s; the other 82 report Steam progress unavailable. Refresh moved to the header. 163 achievement tests pass. Collection UI is next.

October 4 Achievement Hub performance (uncommitted): load reports are coalesced (at most one render per 350 ms); the collection, shelves, and unlocks skip or reuse unchanged content; the hub session's library game list is cached instead of resolving covers on every activation and report. With real data, a per-report render dropped from 0.56–0.89 s to about 20 ms. 179 achievement tests pass.

October 4 Achievement Hub details (committed `ca12c60`): clicking a hub achievement opens a new `AchievementDetailWindow` (status, counter progress, rarity context, game completion, guide with Add/Edit, web search, Next Up, pin game, open game). Steam badge downloads now prefer `shared.fastly.steamstatic.com/community_assets` because the legacy hosts 404 for newer games. 179 achievement tests pass.

October 4 Achievement Hub layout follow-up (uncommitted, with the rarity backfill): new Pinned games section with large covers above a compact 10-game Recently played row; both rows scroll with hover arrows. Pins are hub-only (`PixelVaultData/achievement-hub-pins.json`), toggled from a cover pin button or by right-clicking any hub achievement. 173 achievement tests pass.

October 4 Achievement Hub Step 4 is implemented: background notification checks merge progress into the hub store (keeping saved rarity/counters), Refresh sits beside Back to Library, and an All achievements collection shows every saved achievement across the library in grid or list view with search, status/game/rarity filters, six sorts, filter-independent totals (games with progress / no achievements / progress unknown), and 100-per-page paging. Steam games with no achievements no longer count as unavailable. **167 achievement tests pass**; live check against a copy of the real store: 7,011 entries from 123 of 189 games, queries 14 ms, page render about 30 ms. Review build: `C:\Codex\App Testing Rarity\PixelVault.exe`. All four plan steps are done; awaiting user review. Committed as `b54c872`. Follow-up (uncommitted): background checks now backfill missing Steam rarity for up to 30 saved games per check (public global percentages only; 30-day recheck for games Steam doesn't publish); 168 achievement tests pass and a live 10-game check filled all rarity.

October 4 Achievement Hub Step 3 is implemented: the Recent unlocks banner combines provider-dated earned achievements from saved snapshots across all library games (not just the three cards), newest first, with coverage/partial/stale captions and failure retention. Coverage is limited to games with saved progress until Step 4 adds library-wide refresh. **158 achievement tests pass**; live RetroAchievements check passed with the configured account (14 dated unlocks, icons load, cached reopen 23 ms); 2 unrelated `TradingCardFoilTests` pixel assertions fail in the full suite. Review build: `C:\Codex\App Testing Rarity\PixelVault.exe` (App Testing was running; close it and rerun the script to refresh that folder). Stop for review before Step 4.

October 4 Achievement Hub Step 2 is implemented: recent-game cards use provider play dates and existing library cover resolution; click opens the exact provider/game achievement page. Shared full-row snapshots are stored under PixelVaultData/achievement-hub/, populated by successful foreground achievement fetches, and kept separate by provider/account. Cached-first refresh, failure preservation/backoff, and account-change cancellation are implemented. **154 achievement tests pass**; live Steam hub retrieval saved three progress snapshots (1.8 s) and reopened cached in 2 ms. See [ACHG-003](plans/PV-PLN-ACHG-003-achievement-hub.md). Stop for review before Step 3 (timeline); Step 4 remains pending.

October 4 counter-progress follow-up: [Achievement counters](ACHIEVEMENT_COUNTER_PROGRESS.md) implemented for the existing per-game collection. List shows known counters; Grid shows them only under the new highest/lowest counter-progress sorts. Local Steam definition metadata supplies exact stat mappings; current values come from the configured account's Web API, with no save access. All 143 achievement tests pass, including 13 new counter cases; Death Stranding DC live verification returned five mapped counters. The user also requested the test-build script after each completed step. Hub Step 2 remains separate and unimplemented.

October 4 achievement hub: **[PV-PLN-ACHG-003](plans/PV-PLN-ACHG-003-achievement-hub.md)** records the accepted four-step plan. **Step 1 implemented and verified:** main-toolbar Achievements button and a reusable, non-modal `AchievementHubWindow` with three honest coming-soon sections. Back/Escape/reopen/minimized restore/owner close and default/minimum layouts passed a WPF smoke check; 130 achievement tests pass. No provider fetches or new persistence in this step. Stop for user review before Step 2 (recent games and shared snapshots), then Step 3 (timeline), then Step 4 (combined collection). Existing ACHG-002 manual QA remains open. Published version unchanged.

September 21 foil follow-up: the shader now attaches to the cover image only (not the whole tile chrome), the empty leftover overlay grid is gone, unused 100% medal-badge helpers were removed, and hover light eases with HoldEnd instead of snapping to the destination first.

September 20 foil refinement: replaced the five translucent overlay layers with `UI/Effects/TradingCardFoilEffect.cs` and a bundled ps_2_0 shader applied to the actual cover artwork in both library and profile. Hover/scroll update one light position; fine grain is a shared frozen texture and there is no idle loop. Recompile shader changes using `scripts/Compile-TradingCardFoil.ps1` (Windows D3D compiler), then build normally. Actual WPF render comparisons checked Shadow of War and Thank Goodness You're Here! at three light positions. All 738 tests pass, including software shader rendering and premultiplied-alpha preservation.

September 19 completion follow-up: Shadow of War's saved progress confirmed 52/52 base achievements, but the background notification path never invoked profile-only auto-completion. All observed successful refreshes now load saved groups without rewriting the guide catalog, persist base-game completion for the matching provider/game, and refresh live library/profile flags. All 736 tests pass, including a 52-base/20-DLC regression.

Live startup verification passed in the rebuilt App Testing executable: Shadow of War's persisted `is_completed_100_percent` changed from 0 to 1 automatically and the completion timestamp was derived from its base-game unlocks. Startup polling prioritizes the selected game and then recent captures instead of leaving the selected game deep in the library queue. Older running executable instances must be restarted to pick up the fix.

September 19 achievement notifications: persistent account-scoped milestones and optional individual unlocks, stacked seven-second popups, fullscreen suppression, saved history, and configurable background checks. Notifications is available in the library toolbar and tray menu, including Preview popups. All 733 native tests pass. Test build updated at `C:\Codex\App Testing\PixelVault.exe`; published version remains 0.077.000. First successful check establishes a silent baseline. State/settings are stored in `PixelVaultData/achievement-notifications.json`. Startup and toolbar accessibility were checked; interactive popup QA remains for the user because the fullscreen game intercepted UI automation targeting.

September 7 Roblox naming correction: new imports use `Roblox_Experience_capture-token.png`, with Roblox (PC) as the game identity in parser and intake preparation. Earlier experience-first filenames remain recognized; existing library files and saved assignments are not migrated. All 77 targeted importer/parser/intake tests pass. Included in App Testing; no release-version bump.

September 7 settings follow-up: Settings now uses HealthApp-style top tabs with inline configuration and deferred utility pages. Opening no longer reads/renders the entire 20 MB main log; Run history loads a 64 KiB tail in the background when selected. All 69 targeted settings tests pass. See `docs/SETTINGS_UI.md`. Included in the App Testing rebuild; release version remains unchanged.

Use it for:

- where to work
- what to read first
- what build is live right now
- what the current source focus is

For durable architecture and data-model context, use:

- `C:\Codex\docs\PROJECT_CONTEXT.md`

## Active Workspace

September 7 follow-up: Grid/List achievement cards now expose Pin/Unpin to Next Up through their context menu, with no card pin icon. Hover text includes the global unlock percentage or an unavailable label; Guide and Next Up pin controls remain. Roblox captures now resolve experience names through the public API and rename to `Title_Roblox_capture-token.png`, using existing creation times without decoding the token. Unresolved captures stay in manual intake. All 703 tests pass, including 12 Roblox checks; the application service resolved the sample place to Sell Lemons through the live API. See `docs/ROBLOX_CAPTURES.md`. The requested App Testing rebuild includes both changes. The runnable script copy is `C:\Scripts\App Development\Build-PixelVault-AppTesting.ps1`; it uses `C:\Codex` by default when outside the repository and accepts `-RepositoryPath` to override. Published version remains 0.077.000.

Work out of:

- `C:\Codex`

Important:

- `C:\Codex` is the source of truth for code, builds, docs, and shared app data
- if a shell or tool reports `A:\Codex`, ignore that and keep using `C:\Codex`
- do not treat `A:\` as the active project drive

Live source:

- `C:\Codex\src\PixelVault.Native\PixelVault.Native.cs`
- `C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj`

Do not edit published `dist\PixelVault-<M.AAA.BBB>\PixelVault.Native.cs` snapshots as the primary source.

## Read First

Before making app changes, read:

- `C:\Codex\docs\POLICY.md`
- `C:\Codex\docs\DOC_SYNC_POLICY.md`

Then use these based on the task:

- `C:\Codex\docs\plans\PV-PLN-EXT-002-service-extraction-and-organization.md` — **complete (Apr 2026):** service extraction **A.1–A.6** + organization **B.1–B.3** (`MainWindowServiceGraph`, `ILogService`, `Services/Intake` + **`IntakePipeline`**, **`SERVICE_OWNERSHIP`** composition + diagram). Next structural work: **`docs/NEXT_TRIM_PLAN.md`**, **`PERFORMANCE_TODO.md`**, **`pixelvault_service_split_plan.txt`** Phase **7**.
- `C:\Codex\docs\LIBRARY_PERFORMANCE_PLAN.md` — library/app **performance** roadmap (folder cache, metadata, detail pane); Notion map under **MainWindow extraction roadmap**
- `C:\Codex\docs\completed-projects\README.md` for **finished initiatives** (e.g. MainWindow extraction Phases A–F, **PV-PLN-EXT-002**) — not active handoff
- `C:\Codex\docs\ARCHITECTURE_REFACTOR_PLAN.md` for refactor **principles** (tiered MainWindow bar, `ILibraryScanHost` as port, FS/async scope)—pairs with extraction and service docs below
- `C:\Codex\docs\PROJECT_CONTEXT.md` for architecture and data model
- `C:\Codex\docs\ROADMAP.md` for sequencing and larger direction
- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` for **complete** MainWindow extraction record (technical detail; see completed-projects index first)
- `C:\Codex\docs\PERFORMANCE_TODO.md` for responsiveness/scalability follow-up
- `C:\Codex\docs\CODE_QUALITY_IMPROVEMENT_PLAN.md` for hardening / edge-case backlog
- `C:\Codex\docs\MANUAL_GOLDEN_PATH_CHECKLIST.md` for risky manual verification
- `C:\Codex\docs\SIMPLE_RELEASE_STEPS.md` + `C:\Codex\scripts\Run-LocalReleaseChecks.ps1` for **non-expert “build + verify + next steps”** before deep Velopack docs
- `C:\Codex\docs\VELOPACK.md` + `C:\Codex\docs\VELOPACK_VM_SPIKE_CHECKLIST.md` for **installer / Velopack** distribution (**`PV-PLN-DIST-001`** §5.3)
- `C:\Codex\docs\PUBLISH_SIGNING.md` — **Authenticode** signing (**`-Sign`**, **`vpk`** **`-n`**) (**§5.2**)
- `C:\Codex\docs\EULA.md` + **`docs/PRIVACY_POLICY.md`** — **§5.4** drafts; **host HTTPS + finalize copy last** before Store/wide release (**`PV-PLN-DIST-001` §10.1**)
- `C:\Codex\docs\BUNDLED_TOOLS_REDISTRIBUTION.md` — bundled **ExifTool** + optional **FFmpeg** notes (**§5.9**)
- `C:\Codex\docs\archive\PV-PLN-LIBWS-001-library-workspace-modes.md` — **done** plan: library **Folder / Photo / Timeline** modes; **Photo workspace: exit & restoration** spells out what exits clear vs. what scroll state is not restored
- `C:\Codex\docs\SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md` for service boundaries and parallel lanes

## Current Published Build

Current live build:

- `0.077.000`

Current executable:

- `C:\Codex\dist\PixelVault-0.077.000\PixelVault.exe`

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

App Testing rebuilt for ACHG-002 on 2026-09-07; open it to load A–E. The old session had exited, so the normal clean build removed the temporary previous executable. No forced shutdown or published version change.

- **PV-PLN-ACHG-002 active, resumed at user request (2026-09-07):** Phases A–E implemented: access/read-first guides, durable Next Up pins, statistics/history, and sorting. **Next: Phase F manual verification.** Shadow of War guide/DLC curation completed separately: 72 guides (52 base, 10 Blade of Galadriel, 10 Desolation of Mordor), with live Steam IDs verified and saved pins preserved. The user resumed the plan: compact 24-DIP vector pins, Guide title pin, Next Up tag chips, group progress bars, earned List accents, and static six-second refresh feedback are implemented. Full suite 691 passed; pinned collection/statistics WPF layouts checked. Pin storage is separate from imports; missing targets open stored guides without provider sync. Steam unavailable player data is unknown, and failed player refresh preserves prior results/timestamp. Live provider/account, interactive import, and OS scaling QA remain in Phase F. See `docs/plans/PV-PLN-ACHG-002-achievement-collection-experience.md`; roadmap mirror: [Notion](https://www.notion.so/3d373adc59b681cdb2bddc883381e1f8). Published version remains 0.077.000.
- continue the MainWindow extraction roadmap in small slices
- keep service extraction coordinated so parallel work does not collide
- treat source-only refactors and published-build changes as different things
- distribution (**`PV-PLN-DIST-001`**): Velopack path + **§5.5** landed; **next (technical):** **`scripts/Verify-DistributionLayout.ps1`** on **`dist\Velopack\publish-*`** or **`dist\PixelVault-*`** after publish; then **`docs/VELOPACK_VM_SPIKE_CHECKLIST.md`** on a VM, **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** on an RC, trial **signed** build (**`docs/PUBLISH_SIGNING.md`**, **`Publish-Velopack.ps1`** **`-SignParams`** / **`VPK_SIGN_PARAMS`**); **legal last:** hosted **HTTPS** for **`docs/PRIVACY_POLICY.md`** + **`docs/EULA.md`** — **`PV-PLN-DIST-001` §10.1**; **`tools-licenses\`** = **ExifTool**; **FFmpeg** user-installed (**§5.9**)

## Working Expectations

- keep `POLICY.md` as the durable behavior contract
- keep `HANDOFF.md` short and current
- use `CHANGELOG.md` for release history, not this file
- after a publish, update `CURRENT_BUILD.txt`, `CHANGELOG.md`, and this file together
- if repo docs and Notion can drift, follow `DOC_SYNC_POLICY.md`

## Current Stop Point

The app is currently published at `0.077.000`.

**Notion:** [MainWindow extraction roadmap](https://www.notion.so/33573adc59b681d88b7dcd88cad53cb6) updated for Phase **E** capstone (**`ILibraryBrowserShell`**). If release rows in Notion lag `docs/CURRENT_BUILD.txt`, re-sync per `docs/DOC_SYNC_POLICY.md`.

Recent extraction progress (repo):

- **PV-PLN-EXT-002 closed (Apr 2026, source):** Composition graph (**`MainWindow.ServiceComposition`** / **`MainWindowServiceGraph`**), settings/import/library scan doc pass, **`IFileSystemService`** import sweep, **`ILogService`**, **`Services/Intake`** consolidation + **`IntakePipeline`** facade, **`SERVICE_OWNERSHIP`** composition table + mermaid data-flow. Plan: **`docs/plans/PV-PLN-EXT-002-service-extraction-and-organization.md`** (status **Complete**). Next: **`docs/NEXT_TRIM_PLAN.md`**, perf todos, **`PV-PLN-DIST-001`** per **Current Direction** below.
- **PV-PLN-PERF-001 shipped in 0.077.000:** Import prep now routes through **`IntakePreparationBuilder`** and folder-cache rebuilds split fast index projection from queued metadata repair. The library scanner/session seam has **`TryUpdateLibraryFolderCacheForTouchedPaths`**, cache-miss library loads paint usable rows before repair, index-only projection refresh is explicit, and orphan `GameId` rows have fast-paint repair coverage. Import workflow progress now routes through **`WorkflowProgressCoalescer`** (100ms UI throttle, newest detail wins, flush before cancel/success/failure), shared progress-window logs use **`WorkflowProgressLogBuffer`**, high-volume import detail logs batch main-log writes via **`ILogService.AppendMainLines`**, and manual large-import checks cover progress cancel/log completeness. Detail renders now own **`LibraryDetailRenderCancellationController`**, selection-dependent metadata reads pass the active render token, detail metadata prioritizes viewport/overscan files before deferred chunks, stale UI apply/redraw paths share an active-token/version/selection guard, ordinary virtualized row scroll refreshes now coalesce at render priority while page-sized jumps refresh immediately, guardrail tests preserve row recycling plus visible-row priority image loading, troubleshooting logs expose `VirtualizedRowHostScroll` / `VirtualizedRowHostRowsRebuilt`, SteamGridDB asset picker previews now download/decode off the UI thread with frozen cached bitmaps, and custom cover/banner/logo saves from SteamGridDB picker selections plus right-click art menus now run disk copy/temp cleanup work off the UI thread. Automated checks passed on 2026-05-07 (`dotnet test` 549/549, app build, `git diff --check`), and the isolated WPF smoke passed for launch, 25/100/500-file foreground imports, large-folder detail open, fast detail scroll, and selection-switch diagnostics. Evidence: `docs/perf/PV-PLN-PERF-001-step-29-manual-run.md` and `docs/perf/PV-PLN-PERF-001-step-29-manual-results.md`. RC follow-ups: live HDR PNG/JXR pair import with real throwaway captures, SteamGridDB picker save with a token, and right-click custom art save.
- **Quilt capture layout + continuous timeline flow (0.076.000):** The library screenshot/timeline feed now uses a square-first quilt layout instead of the older justified/day-card approach. **Compact** and **Roomy** now change real quilt cell size, detail tiles crop-fill their frames, and timeline day labels ride on the first capture of each day so the feed flows continuously without isolated date islands. See **`docs/CHANGELOG.md`**.
- **Auto-intake + metadata editor + photo-exit polish (0.075.017):** Background auto-intake now seeds existing top-level files already present in watched upload roots on startup/restart, so pre-existing captures do not sit idle waiting for a new file event. The library metadata editor now batch-loads embedded metadata only for the selected files and resolves the best visible owner window, which removes the worst single-photo open stalls and tray-hidden modal issues. Exiting **Photo** workspace now waits for the folder pane layout pass before re-rendering covers, preventing the brief skinny multi-column cover flash during the return to folder view. See **`docs/CHANGELOG.md`**.
- **Tray controls + background imports polish (0.075.016):** Background auto-intake can keep PixelVault running from the **system tray** with separate **Settings** toggles for **minimize to tray** and **ask on close**, a close prompt that can send the app to tray, and a tray status flyout for recent imports. The **Background imports** window is now a single **newest-first** list with more readable headers and reliable **Clear selection**, and undo restores are temporarily suppressed from watcher re-import so files do not loop straight back through auto-intake. See **`docs/CHANGELOG.md`**.
- **Post–app-review rename hardening (0.075.011):** **`PV-PLN-RVW-001`** Phase 3 — Steam / Non-Steam **numeric id** prefix detection for intake rename uses an explicit **`_`** / **`-`** boundary (or exact id stem) so odd filenames are not misclassified; plan closed under **`docs/completed-projects/`**. See **`docs/CHANGELOG.md`**.
- **Library storage + auto-intake + metadata fixes (0.075.010):** Single-folder **LIBST** behavior (re-home, nested scans, photo index alignment) ships with **background auto-intake** (`PV-PLN-AINT-001` slice 9), **metadata index** re-merge when Steam/non-Steam shortcut labels disagree with filename + game index, **filename** tweaks (emulation shortcut convention, Dolphin placeholder suppression, subtitle ` - ` → `: `), **`.jxr`** as library media, and import **Emulation** tags for shortcut captures. See **`docs/CHANGELOG.md`** for the full list.
- **Steam non-Steam shortcut IDs (0.075.008):** Steam-style numeric shortcut screenshots for non-Steam games now resolve as **`Emulation`** instead of bad Steam AppIDs. The parser recognizes long numeric shortcut IDs, uses the **Game Index** `Non-Steam ID` field to recover known game names, preserves unknown IDs through manual intake, and saves them back onto new master rows. `Emulation` is treated as a built-in/default console in manual metadata, game-index editing, folder ID editing, and filename-rule defaults.
- **Library photo workspace — console capture filter (0.075.007):** In **Photo** mode, title **platform badges** (per game) filter which consoles’ captures appear in the **main photo pane**; the **cover rail** stays complete. Badges use non-button hit targets; switching games resets toggles. Archived plan: **`docs/archive/PV-PLN-LIBWS-001-library-workspace-modes.md`**.
- **Pre-V1 polish Slice C (0.075.000):** Library folder and detail **empty/loading** states with CTAs; version **renumber** to **`0.075.xxx`** for runway to 1.0. See **`docs/plans/PV-PLN-V1POL-001-pre-v1-polish-program.md`**.
- **Library perf Steps 4–5 (0.999.000):** SQLite pragmas and sliced metadata reads/upserts for the library detail pane; deferred game-index warmup after the library window is shown (**`ApplicationIdle`**) instead of constructor preload. See **`docs/LIBRARY_PERFORMANCE_PLAN.md`**.
- **Library detail pane Step 3 (0.998.000):** Viewport-aware decode scheduling (visible virtual rows use the priority image-load lane; overscan-only rows use the normal lane). Detail `VirtualizedRowHost` now recycles row elements like folder tiles; selection chrome stays correct via repopulating **`DetailTiles`** from the visible visual tree after each virtual pass. See **`docs/LIBRARY_PERFORMANCE_PLAN.md`** Step 3.
- **Library thumbnail perf (0.997):** Smaller capped decode sizes for folder/detail/banner images, more parallel decode slots, async disk thumbnail writes, lower-priority bitmap apply on the UI thread, LowQuality scaling on grid tiles, and a larger in-memory LRU to reduce repeated decode churn when browsing.
- **Startup 100% badge cache fix (0.996):** The lightweight folder-cache snapshot now carries the 100% completion fields and reapplies saved game-index rows during startup prefill, so completion medals should appear immediately instead of waiting for a later refresh.
- **Library sort/filter popup cleanup (0.995):** The old persistent sort row is now compact **Sort** and **Filter** popup buttons, folder filters persist across sessions, the selected-cover medal was removed again, and the bottom tile-size controls were tightened and aligned.
- **Preview-cover completion badge (0.994):** The medal completion badge now renders on the selected game's main preview cover in the detail pane as well as the library grid tiles, which closes the gap where marked games could look undecorated in the primary cover view.
- **Published medal badge asset lookup fix (0.993):** The completion badge now resolves bundled medal artwork from the nested publish-assets path as well as the repo/source layouts, fixing the regression where the icon disappeared in published builds.
- **Medal badge shadow/placement polish (0.992):** The medal completion badge now has a stronger downward shadow and sits slightly inset from the top-right corner so it reads more like an overlay element and less like it is clipped to the tile edge.
- **Medal completion badge (0.991):** The library completion badge now uses the new medal artwork asset and scales from the image's natural aspect ratio, replacing the older text-mark treatment while keeping the top-right tile overlay behavior.
- **100% badge visibility fix (0.990):** The 100% library badge now strips the white background while keeping the main glyph intact, fixing the follow-up regression where the badge could disappear entirely on covers.
- **100% badge rendering polish (0.989):** The 100% library badge now drops the visible frame, renders much larger, and preprocesses the source bitmap so the edge-connected white background becomes transparent before the overlay is drawn.
- **Library 100% badge toggle (0.988):** Library game tiles now expose a right-click **`100% Achievements`** toggle that persists immediately to the saved game index and shows the new **`100 Percent Icon.png`** badge in the tile’s top-right corner. The flag now flows through folder-cache alignment and browse projection so it survives normal library reloads.
- **Game collection metadata schema (0.983):** Added persisted game-level fields on **`game_index`** for **100% complete**, **completed date**, **favorite**, **showcase**, and **collection notes**. SQLite upgrades older libraries in place with additive columns, and game-index normalization / assignment paths now preserve the new fields instead of dropping them during save or backfill.
- **Library perf + chrome (0.980):** Detail snapshot builds reuse media dimension maps when possible, parallel-probe layouts for large folders, richer **PERF** / troubleshooting timings for hunts, global thin dark rounded **ScrollBar** theme, slightly higher decode concurrency.
- **Packed card tile scale (0.975):** Packed day-card masonry uses a **1.25×** tile-width multiplier instead of **1.75×** for in-card targets and min/max column bounds (detail/timeline packed layout only).
- **Library aspect-native tiles (0.974):** Masonry heights are **width ÷ aspect** per file with no shared height clamps; images and video previews use **Uniform** stretch only; timeline packed-card estimates reserve space for tall portraits.
- **Library aspect accuracy (0.969):** Introduced natural pixel aspect for masonry height estimates (prior to 0.974 removing shared height clamps and Uniform-only rendering).
- **Library gallery refresh (0.964):** Detail scroll position is preserved across refined snapshot passes with debounced virtual refresh for smoother panning. Tile min/max bounds are ~1.75× larger for screenshot and timeline views; media uses cover-fill through rounded clips; timeline titles, meta, and comments sit on a bottom gradient over the image.
- **Seamless gallery grid (0.954):** Packed day cards no longer show visible frame chrome, dates are reduced to a small label above each day’s first cluster, media fits the frame shape without cover-cropping, and packed gallery widths were loosened again so the grid reads larger overall.
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
- **Docs publish (0.854):** Planning docs consolidated (archive snapshots, slim **`PERFORMANCE_TODO`**, **`CODE_QUALITY_IMPROVEMENT_PLAN`**, stubs for perf/service split). See **`docs/CHANGELOG.md`** **0.854**.
- **Next trim / hotspots (2026-04-12):** **`docs/NEXT_TRIM_PLAN.md`** fully aligned to the current **`0.075.xxx`** train (measured line counts, tier notes). **PV-PLN-RVW-001** (**post–app-review hardening**) is **complete** — **`docs/completed-projects/PV-PLN-RVW-001-post-app-review-hardening.md`**. Notion mirror: [Next trim plan](https://www.notion.so/33873adc59b68131ae12f82c97363684) — sync if it still titles “post-0.854” only (`DOC_SYNC_POLICY.md`).
- **Monolith / Tier 1a (in source):** Manual metadata window — shared helpers moved to **`UI/Intake/MainWindow.ManualMetadata.Helpers.cs`**; **`ShowManualMetadataWindow`** behavior unchanged. Next slices: Steam search / finish handlers into additional partials.
- **E1–E3 (complete):** Library browser: **`LibraryBrowserHost.Show`** (try/catch + **`ILibrarySession`**) → **`LibraryBrowserShowOrchestration`**(**`ILibraryBrowserShell`** via **`LibraryBrowserShellBridge`**) for open/show/delegate wiring; top nav **`MainWindow.LibraryBrowserChrome.cs`**; layout **`MainWindow.LibraryBrowserLayout.cs`**; render **`MainWindow.LibraryBrowserRender.*.cs`**; toolbar/pane/cover/detail partials **`MainWindow.LibraryBrowserOrchestrator.*.cs`**. **`ILibrarySession`**, **`LibraryWorkspaceContext`**, **`LibraryVirtualization.cs`**
- **Responsiveness:** **`PERFORMANCE_TODO.md`** — item 5 long-workflow spot-check; item 10 **`LibraryBrowserHost`** + **`ILibraryBrowserShell`** / **`LibraryBrowserShowOrchestration`**; manual-metadata game-title list off UI thread when rebuilding choices
- **F1–F3 (complete):** **`SettingsShellHost`** + **`SettingsShellDependencies`** + thin **`MainWindow.SettingsShell`** bridge; **`MainWindow.SettingsPersistence`**; photography — **`MainWindow.PhotographyAndSteam.cs`**
- **Phase 5 (import):** Import-and-edit **Steam store title** when the user leaves the loaded title unchanged — **`IImportService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync`** (**`ICoverService.SteamNameAsync`**). Manual metadata **finish** — **`IImportService.FinalizeManualMetadataItemsAgainstGameIndex`** uses **`IGameIndexEditorAssignmentService`** for row resolve + persist ( **`ImportServiceDependencies.GameIndexEditorAssignment`** ); **“Add New Game”** preview/ensure — **`BuildUnresolvedManualMetadataMasterRecordLabels`** / **`EnsureNewManualMetadataMasterRecordsInGameIndex`**; tag-text platform alignment + Other-name validation — **`ApplyManualMetadataTagTextToPlatformFlags`** / **`ManualMetadataItemsMissingOtherPlatformName`**; finish **MessageBox** copy — **`GetManualMetadataFinishEmptySelectionMessage`**, **`GetManualMetadataFinishConfirmBody`**, **`BuildManualMetadataAddNewGamePrompt`**. **`RunSteamRenameAsync`** uses **`SteamNameAsync`** when **`ResolveSteamStoreTitle`** is not set. Unit tests: **`tests/PixelVault.Native.Tests/ImportServiceManualMetadataTests.cs`**.
- **Phase E2 (library session):** **`ILibrarySession`** includes **`HasLibraryRoot`**, **`EnsureLibraryRootAccessible`** (**`EnsureDir`**), index/game-index/metadata helpers, folder-cache snapshot, **`RemoveLibraryMetadataIndexEntries`**, **`LoadLibraryFoldersCached`**, **`RefreshLibraryCoversAsync`**, and **`RunLibraryMetadataScan`**. Library UI defers root/index work to the session where intended; **`LibraryBrowserHost`** receives **`ILibrarySession`** at construction.
- **Library covers (UI thread):** Removed sync **`ResolveLibraryArt`**; tiles use **`GetLibraryArtPathForDisplayOnly`**; folder-detail banner runs **`GetLibraryArtPathForDisplayOnly`** + **`File.Exists`** on the thread pool, then dispatcher **`QueueImageLoad`**. **`ResolveLibraryArtAsync(..., false)`** returns **`Task.FromResult`** (**`GetLibraryArtPathForDisplayOnly`**) so there is no async state machine on the no-download path.
- **Publish:** script copies full native + test sources under `dist/.../source/`

**Refactor (Apr 2026):** **`GetSavedGameIndexRowsForRoot`** + **`ILibrarySession`** for active-root game index reads / metadata upsert; **`IImportService.RunSteamRenameAsync`**, **`LoadManualMetadataGameTitleRowsAsync`**, async **`RunBackgroundWorkflowWithProgress`** / game-index resolve host; Library **`NavChromeAndToolbar`** + **`PaneEvents`** partials. **Refactor (continued):** **`ILibrarySession`** now exposes **`UpsertLibraryMetadataIndexEntries`** (both overloads), **`RefreshFolderCacheAfterGameIndexChange`**, and **`EnsureGameIndexFolderContext`**; removed unused **`MainWindow.LoadLibraryFoldersCached(root, …)`**. **Phase 3 extraction roadmap:** **A–E** complete (**E** includes **`ILibraryBrowserShell`** + bridge). **Phase F** complete: **`SettingsShellHost`** / **`SettingsShellDependencies`**, **`MainWindow.SettingsShell`** bridge, **`MainWindow.SettingsPersistence`**, photography partial — **`docs/MAINWINDOW_EXTRACTION_ROADMAP.md`**. **Next plan:** shrink **`PixelVault.Native.cs`** and heavy partials per **`docs/NEXT_TRIM_PLAN.md`** (ManualMetadata partial, ImportWorkflow → service, optional wiring partial); then **`PERFORMANCE_TODO.md`** item 7 when editing persistence/scanner call sites.

If you are picking work up midstream:

1. decide whether the task is a shipped-behavior change or a source-only refactor
2. check whether the change belongs in `MainWindow`, an extracted UI partial/host, or a service seam
3. update the matching docs and Notion per `DOC_SYNC_POLICY.md` when milestones or releases change
