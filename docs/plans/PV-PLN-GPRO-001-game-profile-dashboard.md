# PV-PLN-GPRO-001 — Game Profile dashboard

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-GPRO-001` |
| **Status** | Draft (planning) |
| **Owner** | PixelVault / Codex |
| **Parent context** | Library browser → "Open Game Profile" window introduced in `MainWindow.LibraryGameProfile.cs` (commit `17ffdc6`); review tightening landed atop that file before this plan was written |
| **Related** | [`PV-PLN-LIBPV-001`](PV-PLN-LIBPV-001-in-app-library-photo-viewer.md) (capture viewer reused by the filmstrip), [`PV-PLN-LIBWS-001`](../archive/PV-PLN-LIBWS-001-library-workspace-modes.md) (workspace modes baseline; "Open Photo View" CTA target), [`PV-PLN-LIBST-001`](../completed-projects/PV-PLN-LIBST-001-single-folder-storage-model.md) (folder/game identity model — the profile is one row in that model), [`PV-PLN-UI-001`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) (shell vs. services discipline for any new helpers extracted), [`PV-PLN-PERF-001`](PV-PLN-PERF-001-app-speed-and-efficiency.md) (no expensive metadata scans on profile open), [`docs/DOC_SYNC_POLICY.md`](../DOC_SYNC_POLICY.md) |

**Topic mnemonic:** `GPRO` — **G**ame **Pro**file.

---

## Purpose

The current Game Profile window (Library tile → "Open Game Profile") opens a Steam-style hero with cover art, platform badges, four stat cards, a capture filmstrip, and an achievements grid. It is a useful **viewer** for one folder, but it does not yet feel like a **dashboard** — there is no fast way to act on the game (favorite, mark complete, change cover), there is no per-game session history, and the long-form "what is this game" surface (notes, ID links, last-played) is missing.

This plan turns the existing window into a **per-game dashboard** by adding only the layout and small wiring that the codebase already supports. The data sources, persistence paths, and rendering helpers required are all in the repo; almost no new services are needed.

**Non-goals (this plan):**

- New external integrations beyond the existing **Steam / SteamGridDB / RetroAchievements** plumbing already used by the achievements section and cover picker.
- A new metadata schema. **Favorite** and **Showcase** flags are already in `LibraryFolderInfo`/`GameIndexEditorRow`/SQLite; this plan only adds toggle UI + small persistence helpers.
- Background reindexing or expensive metadata scans on profile open. The existing `librarySession.LoadLibraryMetadataIndexForFilePaths(...)` snapshot is the only allowed read path.
- Replacing the Library browser's Photo View workspace. The profile **launches** Photo View through the existing `_libraryBrowserLiveWorkingSet.OpenPhotoWorkspaceForFolder` callback — it never duplicates that surface.

---

## Named policies (stable IDs for spec, tests, UI copy)

| Policy ID | Name | Statement |
|-----------|------|-----------|
| **PV-POL-GPRO-DATA-001** | Snapshot at open | The profile reads `GetFilesForLibraryFolderEntry` and `librarySession.LoadLibraryMetadataIndexForFilePaths` exactly **once** at open and reuses that result for every section. New data sources may be added only when they are already cached (e.g. `LibraryFolderInfo` fields). No EXIF/ffprobe per-file calls during render. |
| **PV-POL-GPRO-CANCEL-001** | Lifetime-scoped async | Every async load triggered by the profile window must take the per-window `CancellationToken` already created in `ShowLibraryGameProfileWindow` and bail before touching the dispatcher when cancelled or `!owner.IsLoaded`. New async sections follow the same shape used by `BeginLoadLibraryGameProfileAchievements`. |
| **PV-POL-GPRO-DEDUPE-001** | One window per game | Repeated "Open Game Profile" clicks for the same `GameId` (or `FolderPath` fallback) reactivate the existing window via the `_libraryGameProfileWindows` registry instead of stacking duplicates. Closing the window removes its entry. |
| **PV-POL-GPRO-REUSE-001** | Reuse, don't reinvent | Actions in the profile call the **same** persistence and helper paths the rest of the library uses: `SetLibraryBrowserCompletionState`, `OpenLibraryFolderIdEditor`, `ChooseLibraryAssetFromSteamGridDbAsync`, `OpenSavedCoversFolder`, `UpsertSavedGameIndexRow`, `librarySession.PersistGameIndexRows`, `OpenFolder`, `TryLibraryToast`. The profile is a **shell**, not a parallel write surface. |
| **PV-POL-GPRO-SESSION-001** | Session threshold parity | The profile's Sessions section uses **the same** `librarySessionThresholdMinutes` setting (and `SettingsService.NormalizeLibrarySessionThresholdMinutes` clamping) the main browser uses. Changing the threshold from the profile writes the same setting and re-renders both surfaces. |
| **PV-POL-GPRO-PIPE-001** | Single thumbnail pipeline | Image and video tiles in the profile go through `CreateAsyncImageTile` (which delegates to `LibraryThumbnailPipeline` and `EnsureVideoPoster`). No new bitmap loaders, no per-profile cache. |
| **PV-POL-GPRO-FALLBACK-001** | Graceful empty states | Every section renders a `BuildLibraryGameProfileEmptyCard`-style row when its data is missing (no captures, no notes, no sessions, no achievements). The profile must **never** render a blank section header with no body. |
| **PV-POL-GPRO-ESC-001** | Esc closes | The profile window handles `Key.Escape` via `PreviewKeyDown` (consistent with `LibraryCommandPaletteWindow`, `MainWindow.LibraryCaptureViewer`, etc.). |

---

## Data and reuse audit

This plan is cheap because the codebase already exposes everything it needs. Each row below is a reuse, not a new feature.

| Capability | Source / hook | Notes |
|------------|---------------|-------|
| Folder identity, name, IDs, completion ticks, favorite/showcase, notes | `LibraryFolderInfo` (`src/PixelVault.Native/Models/IndexModels.cs`) | All fields already populated from the folder cache + sqlite index. |
| Capture date / starred / metadata snapshot | `librarySession.LoadLibraryMetadataIndexForFilePaths(files)` (used today by the existing profile) | One call per open. |
| Cover, hero banner, hero logo paths | `GetLibraryArtPathForDisplayOnly`, `GetLibraryHeroBannerPathForDisplayOnly`, `GetLibraryHeroLogoPathForDisplayOnly` | Display-only — already used by the hero. |
| 100% complete toggle | `SetLibraryBrowserCompletionState(view, isCompleted)` (`MainWindow.LibraryBrowserOrchestrator.FolderTile.cs`) | Persists via `librarySession.PersistGameIndexRows`. |
| Cover / Banner picker | `ChooseLibraryAssetFromSteamGridDbAsync(... LibraryAssetPickerKind.Cover / .Banner ...)` | Requires `HasSteamGridDbApiToken()` for SteamGridDB picker; "Set Custom Cover..." path also exists for local file pick. |
| Edit IDs / metadata | `OpenLibraryFolderIdEditor(folder, callback)` and `openLibraryMetadataEditor(folder)` | Already wired into the folder-tile context menu. |
| Open source folders | `GetLibraryBrowserSourceFolderPaths(folder)` + `OpenFolder(path)` | Already used by the "Open folders" menu item. |
| Open My Covers folder | `OpenSavedCoversFolder()` | Available via shell + palette today. |
| Notes editor | `LibraryBrowserQuickEditDrawer` writes `CollectionNotes` via `UpsertSavedGameIndexRow` | The profile can either embed a small notes editor that calls the same upsert, or reuse the drawer. |
| Sessions threshold | `librarySessionThresholdMinutes` + `SettingsService.NormalizeLibrarySessionThresholdMinutes(value)` | Already persisted to `library_session_threshold_minutes` in settings. |
| Photo View launch | `_libraryBrowserLiveWorkingSet.OpenPhotoWorkspaceForFolder(view)` | The current "Open Photo View" tile uses this; reused by the new "View All Captures" CTA. |
| Image / video thumbnail pipeline | `CreateAsyncImageTile(...)` → `LibraryThumbnailPipeline` + `EnsureVideoPoster` | Already used by the profile capture filmstrip after the recent tightening pass. |
| Achievements fetch | `GameAchievementsFetchService.FetchAsync(...)` | Already cancellation-token-aware. |
| Toasts / logging | `TryLibraryToast(text, MessageBoxImage.Warning)`, `Log(msg)`, `LogException(scope, ex)` | Used everywhere; profile already calls `TryLibraryToast` on the Photo View failure path. |

The only **new persistence helpers** introduced in this plan are:

- `SetLibraryBrowserFavoriteState(view, isFavorite)` — mirrors `SetLibraryBrowserCompletionState`.
- `SetLibraryBrowserShowcaseState(view, isShowcase)` — same shape.

Both go in `MainWindow.LibraryBrowserOrchestrator.FolderTile.cs` (or a new `MainWindow.LibraryBrowserFolderActions.cs` partial if the file is getting long) so they are usable from the profile **and** the folder-tile context menu in the same release.

---

## Execution roadmap

The phases are **layout-cheap → data-cheap → small new code → polish**. Each phase is intentionally shippable on its own; the dashboard becomes more useful at every step without breaking the previous one.

### Phase A — Cosmetic and stat-strip realignment

Lands the visual delta first so the rest of the work has the right canvas. No new data sources, no new actions.

| Step | Action | Done when |
|------|--------|-----------|
| A.1 | Split the existing **Date range** stat into **First Capture** and **Latest Capture** cards. Add a fifth **Sessions** stat card whose value is computed by the shared session-grouping helper introduced in Phase D (placeholder count using `librarySessionThresholdMinutes` is acceptable until D lands). Update `BuildLibraryGameProfileStats` to render 5 columns. | Profile shows 5 stat cards: Captures, Videos, Sessions, First Capture, Latest Capture. |
| A.2 | Add a single-line **summary strip** under the H1 title in `BuildLibraryGameProfileHero`: `<N> captures · <V> videos · <S> sessions · <First> → <Latest>`. Pluralize all four nouns. Hide tokens whose value is 0 / unknown rather than rendering "0 captures". | Long-form summary line is visible under the title in muted color, wraps gracefully. |
| A.3 | Tone down the hero platform badge border from `#F4F8FB` (1.15px) to `#3E5665` (1px) **only** when used inside the profile hero (`BuildLibraryBrowserDetailTitlePlatformBadge` already returns a fresh element per call — pass a `forProfileHero: true` overload, or post-process the returned `Border` in `BuildLibraryGameProfileHero`, to avoid changing the folder-tile look). | Hero badges read as integrated chips, folder tiles unchanged. |
| A.4 | Drop `BorderThickness` to `0` on `BuildLibraryGameProfileStatCard` and the achievements container; rely on background contrast (`#111A21` on `#0B1116`). Keep the cover-art `#3E5665` 1px border. | Profile reads less like a form. |
| A.5 | Confirm `MinHeight = 275` (already landed) is the only constraint on the hero; no fixed `Height`. Long titles + collection notes still render without clipping. | Verified manually with a long collection note. |

**Tests / acceptance:**

- Manual: open profile for a known game with `>= 30` captures + a long collection note, confirm hero grows, summary line wraps cleanly, stat strip never overflows the page width at 900px (the configured `MinWidth`).
- Snapshot the rendered profile width, compare side-by-side with the pre-Phase-A version (subjective; capture in PR description).

### Phase B — Game Notes card + achievements header upgrade + external links

Adds three small reads of data we already have.

| Step | Action | Done when |
|------|--------|-----------|
| B.1 | Add `BuildLibraryGameProfileNotesCard(folder)` rendering `folder.CollectionNotes` (or empty-state prompt: *"No notes yet — add one to remember mods, settings, or run rules."*). Card shows an **Edit notes** button that opens a small modal `TextBox` (300×220) and persists via `UpsertSavedGameIndexRow` (mirroring `MainWindow.LibraryBrowserQuickEditDrawer.cs:246–257`). | Notes show, empty state shows; edit → save → notes update without closing the window. |
| B.2 | Update the achievements summary line in `BeginLoadLibraryGameProfileAchievements` from `<source>: N of M earned` to `<source>: N of M earned · <pct>%` when `progressKnown` is true. Render a thin (~6px) muted progress bar (`Border` + clipped fill) on a row directly under the summary text. Keep the existing fallback for `progressKnown == false`. | Achievement section shows source + percent + progress bar; locked games still show "Progress unknown — showing achievement definitions". |
| B.3 | Extend `BuildLibraryGameProfileIdLine` (or replace with a horizontal `WrapPanel`) to render each ID as a clickable pill: **Open on Steam** (`steam://nav/games/details/<SteamAppId>`), **Achievements on Steam** (`steam://url/SteamIDOverlayPage/<SteamAppId>` or the user's profile achievements URL when `CurrentSteamUserId64()` is set), **SteamGridDB**, **RetroAchievements** (when each ID is present). Each pill `Process.Start`s the URL with `UseShellExecute = true` and logs failures via `LogException`. | All applicable pills appear, clicking each opens the right surface; copy-folder-path right-click works on the pill. |
| B.4 | Add **Last captured** sub-line under the **Latest Capture** stat card body: `(<N> days ago)` derived from `folder.NewestCaptureUtcTicks`. Same pluralization rules as Phase A. | Recently-captured games show "(2 days ago)", older games show "(1 year ago)". |

**Tests / acceptance:**

- Notes round-trip: open profile, add notes, close + reopen profile, notes persist; verify same text appears in the existing Quick Edit Drawer.
- Achievement summary verified for: Steam game with full unlock data, RetroAchievements game with full data, non-Steam folder where definitions only are returned.
- ID pills: at least one Steam pill, one RA pill, one SteamGridDB pill all open the expected surface; missing IDs hide the pill (no greyed-out chrome).

### Phase C — Hero action cluster + Favorite/Showcase persistence

The single phase with **new persistence code**. Designed to land Favorite/Showcase **everywhere** in one go (profile, folder-tile context menu, future right-click menus) so that future surfaces don't re-implement the toggle.

| Step | Action | Done when |
|------|--------|-----------|
| C.1 | Add `SetLibraryBrowserFavoriteState(view, isFavorite)` and `SetLibraryBrowserShowcaseState(view, isShowcase)` mirroring `SetLibraryBrowserCompletionState` (load rows, mutate, persist via `librarySession.PersistGameIndexRows`, mutate `view.IsFavorite/IsShowcase`, propagate to `view.PrimaryFolder` + `SourceFolders`). Add unit-style test coverage if a `MainWindow.LibraryBrowserOrchestrator.FolderTile.cs`-adjacent test file exists; otherwise add a manual verification row. | New helpers compile, return `true` on first toggle, `false` on no-op repeat; toggling persists across app restart. |
| C.2 | Add **Favorite**, **Showcase**, **100% Complete** as round 36px icon-toggle buttons (filled when on, outlined when off) in a right-aligned `StackPanel` inside the hero `content` grid. Each button calls the matching helper from C.1 / `SetLibraryBrowserCompletionState` and **re-renders only the hero corner indicator** (Phase F adds the in-cover badges). Use existing icon glyph helpers where possible; otherwise reuse `BuildSymbolIcon` patterns. | Three icon toggles render, click flips state immediately, state persists. |
| C.3 | Add **Edit Game** and **Open Folders** as pill buttons next to the toggles. **Edit Game** calls `OpenLibraryFolderIdEditor(actionFolder, callback)` (same callback shape as the folder-tile menu); **Open Folders** calls `foreach (p in GetLibraryBrowserSourceFolderPaths(folder)) OpenFolder(p)`. Re-render the hero on `OpenLibraryFolderIdEditor` callback so renamed/re-IDed games update without reopening the window. | Both pills work; edits in the IDs editor immediately reflect in the hero. |
| C.4 | Add a single **Change Art ▾** dropdown pill that opens a context menu with: **Choose Cover from SteamGridDB** (`HasSteamGridDbApiToken()` gated), **Choose Banner from SteamGridDB** (same gate), **Set Custom Cover...** (local file picker, mirrors `setCoverItem.Click` body in `MainWindow.LibraryBrowserOrchestrator.FolderTile.cs:529`), **Open My Covers Folder** (`OpenSavedCoversFolder()`). Use `ChooseLibraryAssetFromSteamGridDbAsync(...)` with a `refreshPhotoWorkspaceHeroBanner` callback that re-renders the profile hero. | Dropdown opens; each item completes; cover/banner change is reflected in the hero on the same window without reopen. |
| C.5 | Surface the new helpers from C.1 on the existing folder-tile context menu (Favorite + Showcase as checkable items) so the action set is consistent app-wide. Audit the photo-workspace title bar for the same opportunity (separate slice if scope grows). | Folder-tile context menu has Favorite + Showcase checkable rows; toggling round-trips with the profile hero. |

**Tests / acceptance:**

- Toggle Favorite from the profile, close it, open the folder-tile context menu — Favorite is checked.
- Toggle Showcase from the folder tile, open the profile — toggle reflects on.
- 100% Complete from the profile matches the existing folder-tile "100% Achievements" item.
- Change Art → Cover from SteamGridDB (with token configured) updates the profile cover and the folder tile cover; **Set Custom Cover...** does the same.
- Edit Game opens the IDs editor; saving updates the hero ID line **and** the achievements section without window reopen.

### Phase D — Sessions section

The only phase with new algorithmic code. Reuses `librarySessionThresholdMinutes`.

| Step | Action | Done when |
|------|--------|-----------|
| D.1 | Add `BuildLibraryGameProfileSessions(orderedFiles, metadataIndex, thresholdMinutes)` that walks `orderedFiles` (already date-sorted in the profile) and groups into `Session { Start, End, Files[] }` whenever the gap to the previous capture exceeds `thresholdMinutes`. Returns `IReadOnlyList<Session>` ordered newest-first. Pure function, no UI; testable. Add automated coverage in the existing `PixelVault.Native.Tests` project if a suitable file exists, otherwise a small new `LibraryGameProfileSessionsTests`. | Pure helper compiles and has at least three test cases: zero captures, one big session, multiple sessions across thresholds. |
| D.2 | Render a **Sessions** section in the profile body using `BuildLibraryGameProfileSectionTitle("Sessions", "Captures grouped by gameplay gaps...")`. Each row card shows: date, time range (`9:14 PM → 10:42 PM`), duration (`1h 28m`), capture count, video count, and a single thumbnail preview using `CreateAsyncImageTile` with the first image in the session. Show at most **6** session cards by default. | Sessions section renders for games with multiple sessions; each card shows all five fields. |
| D.3 | Add a **Threshold: 60 min ▾** picker in the section header that writes `librarySessionThresholdMinutes` via `SettingsService.NormalizeLibrarySessionThresholdMinutes` and re-renders **only** the sessions section + the Sessions stat card. Match the threshold options used in `MainWindow.LibraryBrowserShowOrchestration.cs:706–710` (30/60/90/120/180). | Changing the threshold from the profile updates the same settings key the main browser writes; reopening the main browser uses the new threshold. |
| D.4 | Add a **View all sessions** CTA (similar to "Open Photo View") that pops a small modal listing **all** sessions in the same row format. Tooltip the count. | Games with > 6 sessions show the CTA; clicking opens the dialog with the full list. |
| D.5 | Update Phase A's placeholder Sessions stat card to use the new helper. | Stat card value matches the section count. |

**Tests / acceptance:**

- Pure helper unit tests as in D.1.
- Manual: a game with at least one same-day burst (`>= 5` captures within 5 minutes) and at least one cross-day gap renders 2 sessions at 60min threshold, fewer at 180min, more at 30min.
- Threshold picker round-trips: change in profile, close window, reopen main browser → main browser sessions reflect new threshold.

### Phase E — Recent Captures rename + chevron scroll

Polish pass that lands once Sessions takes over the "what did I do here" job.

| Step | Action | Done when |
|------|--------|-----------|
| E.1 | Rename the filmstrip section header to **Recent Captures** and the trailing CTA to **View All Captures**. Both call `OpenLibraryGameProfilePhotoWorkspace`. | Copy reflects new naming; existing behavior unchanged. |
| E.2 | Switch the inner `ScrollViewer.HorizontalScrollBarVisibility` from `Auto` to `Hidden`. Add small left/right chevron `Border` overlays (~28px wide, semi-transparent like the prev/next overlays in `PV-PLN-LIBPV-001`) that scroll by one tile width on click. Hide each chevron at the corresponding scroll bound. | Filmstrip no longer shows an OS scrollbar; chevrons appear/disappear correctly. |
| E.3 | Confirm the existing `files.Take(14)` cap still applies. If users complain about the cap in dogfood, raise to 18 — do not remove. | Cap unchanged or raised by a single increment. |

**Tests / acceptance:**

- No horizontal scrollbar appears at default page width.
- Chevrons advance the strip by exactly one tile width per click.
- "View All Captures" still launches the existing Photo View workspace.

### Phase F — Final polish

Items that are individually small but elevate the dashboard feel.

| Step | Action | Done when |
|------|--------|-----------|
| F.1 | When `folder.IsShowcase` or `folder.IsFavorite` is set, render a small pin/star glyph in the top-right corner of the cover art tile in the hero (NOT the toolbar — this is the in-art badge). Update on toggle from the C.2 buttons. | Showcase / Favorite icons appear in the cover; toggling updates them live. |
| F.2 | When any persistence helper changes data the profile is showing (`SetLibraryBrowserCompletionState`, the new Favorite / Showcase helpers, notes save, IDs editor callback, cover/banner change), invoke a single `RefreshLibraryGameProfileWindowSection(...)` helper that re-renders only the affected section. No full-window rebuild on minor edits. | Toggling Favorite does not flicker the achievements grid; saving notes does not reload achievements. |
| F.3 | Add `AutomationProperties.Name` to the new toggles, pills, threshold picker, and chevron buttons. Confirm `Tab` order: title → toggles → pills → notes → sessions → captures → achievements. | Manual: keyboard-only navigation through every interactive element works; screen-reader names match labels. |
| F.4 | Add **Recommended extras** review row to confirm none became blockers (zoom, multi-platform sessions, learned game-specific session threshold). Track follow-ups separately if scope grew. | Plan Status moves to **Complete** or split into a follow-up plan. |

**Tests / acceptance:**

- All sections re-render in place when their data changes.
- Tab order verified manually.

---

## Suggested commit cadence

To keep PR review tractable, ship in this order. Each commit should be self-contained and pass `dotnet build` + the existing test suite.

1. **Phase A** as a single commit — pure cosmetic + summary line. Reviewer can scan the diff in one read.
2. **Phase B.1 (Notes card)** + **B.2 (achievement header)** + **B.4 (last captured)** — one commit, all read-only data plumbing.
3. **Phase B.3 (external links)** — separate so the URL handling can be reviewed for shell-execute safety.
4. **Phase C.1 (Favorite/Showcase helpers)** + minimal **folder-tile context menu** wiring (C.5) — one commit, lands persistence everywhere before any UI in the profile depends on it.
5. **Phase C.2–C.4** — hero action cluster.
6. **Phase D.1 (sessions helper + tests)** — pure code.
7. **Phase D.2–D.5** — sessions UI, threshold picker, View all dialog.
8. **Phase E** — filmstrip polish.
9. **Phase F** — corner indicators, in-place re-render, accessibility, status flip.

Estimated effort (focused work, single owner):

- Phase A: ~2h
- Phase B: ~4h (B.1 is the longest because of the notes editor wiring)
- Phase C: ~5h (the new helpers + their callers + the dropdown menu)
- Phase D: ~5h (mostly the threshold picker + View all dialog; the helper itself is small)
- Phase E: ~2h
- Phase F: ~2h

Total: roughly **3 focused sessions** if landed back-to-back.

---

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| **Stale view after action** — toggling Favorite/Showcase mutates `LibraryFolderInfo` but the profile snapshot is from open time. | Phase F.2's `RefreshLibraryGameProfileWindowSection(...)` re-renders the affected section from the **current** `view` state. Persistence helpers always mutate `view.IsFavorite/IsShowcase` (mirroring `SetLibraryBrowserCompletionState`). |
| **Sessions threshold drift** — user changes threshold in the profile while the main browser is in Sessions mode. | **PV-POL-GPRO-SESSION-001** mandates writing the same setting; the main browser's existing `RerenderFolderList?.Invoke()` path already responds to setting changes when the workspace is reopened. If the browser is open simultaneously, the threshold picker should also call `_libraryBrowserLiveWorkingSet?.RerenderFolderList?.Invoke()` (cheap) when the live working set is non-null. |
| **External-link safety** — Phase B.3 opens `steam://...` URLs via shell-execute. | Wrap each `Process.Start` in `try/catch` (already the pattern used by `BuildLibraryGameProfileCaptureTile` for video files). `LogException` on failure, `TryLibraryToast("Couldn't open external link")` for user feedback. URLs are derived from already-validated ID fields, not free-form input. |
| **Notes editor data race** — user edits notes in the profile while the Quick Edit Drawer is also open in the main browser. | Both surfaces persist via `UpsertSavedGameIndexRow`, which is the single write path. Last-write-wins is acceptable; document that in the manual checklist row. The profile re-reads `folder.CollectionNotes` on its own save callback. |
| **Sessions render cost** — a game with 2000 captures could produce hundreds of session rows. | Cap the inline list at **6** (Phase D.2); the full list lives in the dialog. Session grouping itself is `O(N)` and runs on the (already-loaded) date-sorted file list. |
| **Action cluster crowds the hero on narrow widths** | The window's `MinWidth = 900` already constrains how narrow it gets. The action cluster lives in the second column of the hero `content` grid (the cover art is column 0, copy is column 1) — actions go in a right-aligned WrapPanel inside the copy column. WrapPanel handles narrow widths gracefully. |
| **PV-PLN-LIBPV-001 capture-viewer regression** — the Recent Captures filmstrip launches `OpenLibraryCaptureViewer` for image tiles; a regression there would break the profile too. | Already covered: `BuildLibraryGameProfileCaptureTile` only routes images to the viewer (videos shell-execute). LIBPV-001 manual checklist rows already cover the path. |
| **Photo View click no-ops** — `OpenLibraryGameProfilePhotoWorkspace` shows a toast on the failure path (already landed). | Keep the existing `TryLibraryToast` + `Log(...)` and verify in dogfood once or twice — the path is rare in practice (only triggered if `LibraryBrowserShowOrchestration.Run` throws before `RegisterLibraryBrowserLiveWorkingSet`). |
| **External-ID URL formats change** (Steam achievement page, RA achievement page) | Wrap URL builders in named static helpers (`BuildSteamGameUrl`, `BuildSteamAchievementsUrl(steamUserId)`, `BuildRetroAchievementsGameUrl`) so a future change is a one-line edit. |

---

## Acceptance criteria (definition of done for the plan)

A user can:

1. Open a Game Profile and see at a glance: cover, hero banner, badges, **summary line**, **5 stat cards** (Captures · Videos · Sessions · First Capture · Latest Capture), Game Notes, Sessions, Recent Captures, Achievements.
2. From the profile hero, click to: **toggle Favorite**, **toggle Showcase**, **toggle 100% Complete**, **Edit Game** (opens IDs editor), **Open Folders**, **Change Art ▾** (cover / banner / set custom / open My Covers).
3. Edit notes inline without reopening the window.
4. See achievements summary as `<source>: N of M earned · <pct>%` with a thin progress bar.
5. Click external-ID pills to open Steam / SteamGridDB / RetroAchievements pages.
6. See **Sessions** grouped by the same session threshold the main browser uses, with a per-profile threshold picker that writes back to the same setting.
7. Browse **Recent Captures** with chevron scroll instead of an OS scrollbar; click **View All Captures** to launch the existing Photo View workspace.
8. Reopen the same profile from any entry (folder tile, photo workspace title click, right-click menu) and have the existing window reactivate (PV-POL-GPRO-DEDUPE-001).
9. Press **Esc** to close (PV-POL-GPRO-ESC-001).

The profile **must not**:

- Re-read EXIF or call ffprobe on render (PV-POL-GPRO-DATA-001).
- Open more than one window per game (PV-POL-GPRO-DEDUPE-001).
- Block on async work after close (PV-POL-GPRO-CANCEL-001).
- Introduce a parallel write path for any of the data it shows (PV-POL-GPRO-REUSE-001).

---

## Doc sync

When execution starts on a phase: follow [`docs/DOC_SYNC_POLICY.md`](../DOC_SYNC_POLICY.md).

- Update [`docs/CHANGELOG.md`](../CHANGELOG.md) for each phase that ships in a release.
- Update [`docs/HANDOFF.md`](../HANDOFF.md) when active focus shifts to or away from this plan.
- Update [`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`](../MANUAL_GOLDEN_PATH_CHECKLIST.md) once Phase D adds Sessions and Phase E changes filmstrip behavior, so dogfood paths are covered.
- When this plan finishes:
  - Move the canonical text under [`docs/completed-projects/PV-PLN-GPRO-001-game-profile-dashboard.md`](../completed-projects/) (plus a one-row entry in `completed-projects/README.md`).
  - Replace this file with a short redirect stub (matches the `PV-PLN-RVW-001` pattern).
  - Update [`docs/plans/README.md`](README.md) to mark **Status: Complete** and link the moved file.
  - Mirror the status in Notion if the team tracks this plan there.

---

## Relationship to other plans

- **Does not block**: any phase of [`PV-PLN-PERF-001`](PV-PLN-PERF-001-app-speed-and-efficiency.md) — the profile already conforms to PERF policies (cancellation, no full-library scans on open). Phase D's session helper is `O(N)` over a list we already loaded.
- **Coordinates with**: [`PV-PLN-UI-001`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) — any helper extracted out of `MainWindow.LibraryGameProfile.cs` should land under a `MainWindow.<Topic>.cs` partial or a service in `Services/Library/` per UI-001's "thin shell + services" direction. The new Favorite/Showcase persistence helpers in Phase C are good candidates for the existing `librarySession` seam.
- **Reuses**: [`PV-PLN-LIBPV-001`](PV-PLN-LIBPV-001-in-app-library-photo-viewer.md) — Recent Captures keeps using `OpenLibraryCaptureViewer` for images. No new viewer surface.
- **Builds on**: [`PV-PLN-RVW-001`](../completed-projects/PV-PLN-RVW-001-post-app-review-hardening.md) — the cancellation, dedupe, esc-to-close, and toast-on-failure work landed before this plan was written; this plan assumes those policies are in effect.
