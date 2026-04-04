# Game-First Library Grouping Plan

Last updated for published build `0.846`.

This document tracks the low-risk path to make the Library feel game-first by default while preserving console tags, console-aware sorting, and console-specific data where it still matters.

The target user experience is:

- default Library view groups all captures for the same game together
- console/platform stays visible as metadata, badges, filters, and optional grouping
- the user can switch grouping with small pill buttons in the Library chrome: `All` and `By Console`
- physical on-disk storage stays unchanged for the first implementation pass

This plan is intentionally scoped as a UI/view-model change first. A future storage/layout rewrite can follow once the browse model proves out.

## Current status

High-level progress:

- Phase 1: complete
- Phase 2: in progress
- Phase 3: not started

What is already live:

- persisted `LibraryGroupingMode` with `All` and `By Console`
- browser-only `LibraryBrowserFolderView` projection layer
- real game-first merge in `All`, including cross-platform same-title collapse
- merged detail timeline built from combined `FilePaths`
- `ViewKey`-based selection identity for browser rows
- immediate delete refresh in the right-hand screenshot pane
- `Open Folders` for merged rows with multiple source folders
- merged-row `Fetch Cover Art` fanout across source folders
- merged-row custom-cover set / clear fanout across source folders
- console badges moved into the detail header beside the game title
- detail-pane stability fix so real folder switches no longer leave stale screenshots onscreen

What is still intentionally limited:

- merged-row `Edit IDs` remains guarded
- storage and saved-row identity are still platform-shaped under the hood
- custom-cover identity is still per-source-folder, even though the UI now fans the action out across merged rows

## Goals

- Make the default Library browse mode feel like a unified game timeline.
- Preserve console information for filtering, sorting, and metadata editing.
- Avoid breaking game-index identity, cover resolution, or saved-row behavior in the first pass.
- Reuse the current Library browser seams instead of pushing more logic back into `MainWindow`.

## Non-goals for the first pass

- Do not change how imports or library reorganization write files to disk.
- Do not collapse game-index rows across platforms yet.
- Do not change `LibraryScanner` cache persistence format unless a later phase requires it.
- Do not rewrite custom cover, external ID, or folder-open behavior to be fully platform-agnostic in one step.

## Product decision

Treat Library browsing as:

- primary identity: game
- secondary facet: console/platform

That means:

- captures keep their console tags
- the default view is game-first
- console becomes a facet the user can surface when needed

## Desired UI behavior

### Grouping controls

Add two small pill buttons in the Library chrome, visually matching the existing Library pill controls:

- `All`
- `By Console`

Behavior:

- `All` is the default
- `By Console` restores the current console-section style browse behavior
- the chosen grouping mode is persisted in settings

Placement:

- in the left-side Library toolbar row, right-aligned opposite the sort/filter pills

### View semantics

#### `All`

- show one card per game
- merge captures across Steam, PS5, Xbox, PC, and other tags into one timeline
- keep the card game-first by default
- show capture count and source-folder count on the card subtitle
- show console context beside the selected game title in the detail header
- allow sorting by the existing sort modes

#### `By Console`

- keep the current console-grouped sections and console-specific cards
- preserve the existing platform section collapse behavior

## Current code reality

The current Library is already closer to this model than it first appears.

### What is already game-first

`LibraryScanner.LoadLibraryFolders` groups indexed files by `GameId` in:

- `src/PixelVault.Native/Services/Library/LibraryScanner.cs`

That means the cached Library rows are not purely a direct reflection of physical directories.

`GetFilesForLibraryFolderEntry` already prefers `folder.FilePaths` over walking `FolderPath` in:

- `src/PixelVault.Native/PixelVault.Native.cs`

That makes a merged game-first timeline feasible without changing storage.

The browser also now projects raw rows into a dedicated game-first view model in:

- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserViewModel.cs`

That layer is where `All` vs `By Console` semantics now live.

### What is still platform-coupled

The current `LibraryFolderInfo` model still assumes one concrete platform label:

- `src/PixelVault.Native/Models/IndexModels.cs`

The browser uses `PlatformLabel` heavily for:

- folder list grouping and section headers
- card badges and captions
- current selection identity
- cover and external ID matching fallbacks
- saved-row lookup in game-index alignment

Important remaining coupled spots:

- `SameLibraryFolderSelection` in `src/PixelVault.Native/PixelVault.Native.cs`
- `BuildLibraryFolderMasterKey` in `src/PixelVault.Native/Indexing/GameIndexCore.cs`
- `ApplySavedGameIndexRows` / `FindSavedGameIndexRow` in `src/PixelVault.Native/Indexing/GameIndexFolderAlignment.cs`
- folder list rendering in `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.FolderList.cs`
- folder tile rendering in `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserOrchestrator.FolderTile.cs`
- library metadata item building in `src/PixelVault.Native/Metadata/LibraryMetadataEditing.cs`
- custom cover identity in `src/PixelVault.Native/Services/Covers/CoverService.cs`

## Recommendation

Do not make `LibraryFolderInfo` or game-index persistence fully console-agnostic in the first pass.

Instead:

1. Keep the current raw library rows from the scanner/cache intact.
2. Add a derived browser view model for display only.
3. Merge those rows into a game-first view when grouping mode is `All`.
4. Leave the raw platform-specific data available for actions that still need it.

This keeps the first implementation focused on browse experience instead of forcing an identity rewrite across indexing, covers, and saved rows.

## Architecture lanes

### 1. Add a persisted grouping setting

Status: done

Add a new setting:

- `LibraryGroupingMode = all | console`

Files:

- `src/PixelVault.Native/Services/Config/AppSettings.cs`
- `src/PixelVault.Native/Services/Config/SettingsService.cs`
- `src/PixelVault.Native/PixelVault.Native.cs`

Notes:

- default should be `all`
- keep it separate from `LibraryFolderSortMode`
- do not overload the existing sort mode to also mean grouping

### 2. Introduce a UI-only browser view model

Status: done

Add a new type under `UI/Library/`, e.g.:

- `LibraryBrowserFolderView`

Suggested fields:

- `ViewKey`
- `GameId`
- `Name`
- `PrimaryFolderPath`
- `SourceFolders`
- `PrimaryPlatformLabel`
- `PlatformLabels`
- `PlatformSummaryText`
- `FileCount`
- `PreviewImagePath`
- `FilePaths`
- `NewestCaptureUtcTicks`
- `SteamAppId`
- `SteamGridDbId`
- `IsMergedAcrossPlatforms`

Rationale:

- the raw model remains what the scanner/cache produces
- the browser gets a stable, explicit model for `All` and `By Console`
- selection identity can move from `FolderPath + PlatformLabel + Name` to a dedicated `ViewKey`

### 3. Build a view-projection step in the browser layer

Status: done

Add a projection step that converts raw `LibraryFolderInfo` rows into browser view rows.

Likely home:

- `src/PixelVault.Native/UI/Library/`

Suggested behavior:

#### `By Console`

- one browser row per raw `LibraryFolderInfo`
- preserve current behavior

#### `All`

- merge rows by:
  - `GameId` first when available
  - normalized game name as fallback

Current merge rules:

- `FilePaths`: union, distinct, date-sorted
- `FileCount`: merged from combined file list
- `PlatformLabels`: distinct set from source rows
- `PlatformSummaryText`: still available for search/filter text and grouped mode support
- `PreviewImagePath`: prefer the primary source row's explicit preview, then fallback to a merged image path
- `PrimaryFolderPath`: primary source folder path from the current merge result
- `SteamAppId` / `SteamGridDbId`: only carried when unambiguous

### 4. Update folder-list rendering to use the view model

Status: done

Primary file:

- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.FolderList.cs`

Changes:

- render from `LibraryBrowserFolderView` rows instead of raw `LibraryFolderInfo` rows
- `All` mode:
  - flatten by game
  - no console section grouping
- `By Console` mode:
  - keep the current section-grouped rendering

Sort behavior:

- keep `Recently Added`
- keep `Most Photos`
- if the current `platform` sort mode is retained, treat it as a sort within the active grouping mode, not as a hidden grouping toggle

### 5. Add the `All | By Console` pill controls

Status: done

Primary files:

- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserLayout.cs`
- `src/PixelVault.Native/UI/Library/LibraryBrowserHost.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserShowOrchestration.cs`
- `src/PixelVault.Native/PixelVault.Native.cs`

Implementation notes:

- reuse `ApplyLibraryPillChrome`
- persist the selected mode via `SaveSettings()`
- rerender the folder list when the mode changes
- keep the visual language consistent with the existing sort/filter pills

### 6. Update selection identity for browser rows

Status: done

Current selection logic still assumes concrete folder identity:

- `SameLibraryFolderSelection` in `src/PixelVault.Native/PixelVault.Native.cs`

For the new browser model:

- add a view-specific identity comparison based on `ViewKey`
- avoid reusing `FolderPath + PlatformLabel + Name` for merged rows

This is important because `All` mode will intentionally merge multiple platform rows into one view item.

### 7. Make detail rendering consume merged file sets

Status: done

Primary files:

- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs`
- `src/PixelVault.Native/PixelVault.Native.cs`

This should work well because the detail pane already consumes `GetFilesForLibraryFolderEntry`, and that method already favors `FilePaths`.

Current behavior:

- detail rendering operates from the merged view row’s `FilePaths`
- detail metadata uses game-first copy and source-folder count in `All`
- per-capture metadata and tags remain intact
- console context is now surfaced in the detail header beside the game title, not over the imagery

### 8. Put guardrails around actions in merged mode

Status: partially done

Some actions still assume one concrete folder or one saved game-index row.

Affected areas:

- folder-level metadata edit
- edit IDs
- open folder
- custom cover assignment / clearing
- cover refresh scoped to a single row

Current merged-row behavior:

#### Safe in `All` today

- browse captures
- select captures
- edit selected capture metadata
- delete selected captures
- sort and search
- `Open Folders` across source folders
- `Fetch Cover Art` across source folders
- set / clear custom covers across source folders

#### Still guarded or intentionally limited

- `Edit IDs`
  - still disabled for merged rows
- canonical custom-cover ownership
  - the UI now fans the action out across source folders, but there is not yet a true game-level custom-cover identity
- platform-specific external IDs
  - still intentionally conservative when the merged row spans ambiguous platform-specific data

### 9. Leave storage and disk layout for a later phase

Status: not started

The eventual idea of storing all captures for a game in one folder is valid, but it should be a separate follow-up after the view model proves out.

Later work would need to revisit:

- organize path in `src/PixelVault.Native/Metadata/LibraryMetadataEditing.cs`
- folder path assumptions across saved rows
- custom cover key generation
- game-index row identity and external IDs
- folder-open behavior when one game historically spans multiple physical folders

## Phased rollout

### Phase 1: game-first browse mode

Status: complete

Delivered:

- persisted `LibraryGroupingMode`
- `All | By Console` pill controls
- browser view model
- merged `All` mode in folder list
- merged detail timeline
- view-key selection identity
- game-first card/detail presentation

Do not change:

- disk layout
- raw scanner cache model
- game-index persistence model

### Phase 2: action hardening

Status: in progress

Delivered so far:

- `Open Folders` for merged rows
- merged-row `Fetch Cover Art`
- merged-row custom-cover set / clear fanout
- delete flow that refreshes the screenshot pane immediately
- detail-pane stability fix for rapid folder switching

Still to do:

- explicit merged-row `Edit IDs` behavior
- decide whether any merged-row action needs a chooser instead of direct fanout
- more manual verification of merged-row metadata and action flows

### Phase 3: optional storage/layout evolution

Status: not started

Explore:

- organizing all captures for the same game into one physical folder
- making saved game-index/library identity more truly console-agnostic
- reworking custom-cover keys and external ID handling for merged games

## Risks

### 1. Platform-specific IDs

`SteamAppId` and `SteamGridDbId` are platform-shaped data.

Merged game cards may represent a game with:

- Steam captures
- PS5 captures
- Xbox captures

That means a single folder-level Steam ID can become ambiguous. Phase 1 should avoid pretending the merged row always has one authoritative platform-specific ID.

### 2. Saved game-index lookups

Saved-row lookup still falls back to `name + platform`.

If this is changed too early, it could create surprising behavior in:

- ID editing
- saved-row alignment
- cover inheritance

### 3. Action ambiguity on merged rows

Some folder actions are conceptually “which real folder do you mean?” in `All` mode. The UX needs explicit rules, not silent guessing.

## Suggested implementation order

Completed:

1. Add `LibraryGroupingMode` to settings.
2. Add the `All | By Console` pill controls.
3. Add `LibraryBrowserFolderView`.
4. Project raw rows to browser rows in the Library browser layer.
5. Update folder-list rendering to consume browser rows.
6. Update selection identity to use a view key.
7. Update detail pane to render merged `FilePaths`.
8. Move console context into cleaner game-first presentation.
9. Add first merged-row action hardening for folder-open, cover refresh, and custom covers.

Next recommended order:

1. Define merged-row `Edit IDs` behavior.
2. Decide whether merged rows need a small platform/action chooser for ambiguous actions.
3. Expand manual verification around fast switching, merged metadata edits, and multi-source cover behavior.
4. Only then revisit storage/layout evolution.

## Manual verification

### Baseline

- open Library in default `All` mode
- confirm one card per game when multiple consoles exist
- confirm the detail timeline includes captures from each console
- confirm card subtitle reflects captures and source-folder count

### Toggle behavior

- switch `All` to `By Console`
- confirm the old console-section browse behavior returns
- switch back to `All`
- confirm selection and scroll remain stable enough for normal use

### Metadata and actions

- edit metadata on captures from a merged game
- confirm console tags are preserved
- test `Open Folders` on a merged row
- test merged custom-cover set / clear behavior
- test merged cover refresh behavior
- test `Edit IDs` and confirm the current guardrail behavior is still understandable

### Settings persistence

- close and reopen the Library window
- confirm grouping mode persists

### Stability

- click through folders rapidly and confirm the title, cover, and screenshot pane stay in sync
- confirm same-folder metadata refreshes still avoid unnecessary visual churn

## File map

High-confidence touch list:

- `src/PixelVault.Native/Services/Config/AppSettings.cs`
- `src/PixelVault.Native/Services/Config/SettingsService.cs`
- `src/PixelVault.Native/PixelVault.Native.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserLayout.cs`
- `src/PixelVault.Native/UI/Library/LibraryBrowserHost.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserShowOrchestration.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserViewModel.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.FolderList.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserWorkingSet.cs`
- `src/PixelVault.Native/UI/Library/MainWindow.LibraryBrowserOrchestrator.FolderTile.cs`
- `src/PixelVault.Native/UI/LibraryVirtualization.cs`
- `src/PixelVault.Native/Metadata/LibraryMetadataEditing.cs`

Secondary review/watch list:

- `src/PixelVault.Native/Indexing/GameIndexCore.cs`
- `src/PixelVault.Native/Indexing/GameIndexFolderAlignment.cs`
- `src/PixelVault.Native/Services/Covers/CoverService.cs`
- `src/PixelVault.Native/Services/Library/LibraryScanner.cs`
- `src/PixelVault.Native/Models/IndexModels.cs`

## Recommendation

Build this as a game-first view layer now.

Do not make storage or master identity fully console-agnostic yet.

That gets the user-facing win quickly:

- default unified timelines by game
- optional console grouping when desired
- console tags still preserved
- minimal risk to indexing, saved rows, and covers

Once the view proves out, revisit the bigger storage/layout change separately.
