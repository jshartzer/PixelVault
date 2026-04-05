# Library Timeline Mode Plan

Last updated for source state on `2026-04-04`.

This document captures the product and implementation plan for a full-library `Timeline` mode in the PixelVault Library browser.

The target experience is:

- the user can switch the Library into a timeline-first browse mode
- the current dual-pane folder/detail split is replaced by one full-width chronological capture stream
- the stream shows image captures in capture-time order, regardless of game or console
- each capture keeps subtle game and console context without turning the view back into a folder browser
- the feature reuses existing Library browser seams instead of pushing new complexity back into `MainWindow`

This is intentionally a browse-mode project first, not a storage rewrite.

## Why this project exists

The current Library now does two different jobs well:

- game-first browsing through merged folder rows
- chronological detail browsing inside the selected game

The missing mode is a true cross-library timeline.

That mode is valuable because it supports:

- memory-driven browsing across games and platforms
- quick review of recent capture sessions
- lightweight curation without forcing the user to think in folders first
- a natural complement to `All`, `By Console`, and sort/filter workflows

## Product decision

Treat `Timeline` as a first-class Library browse mode, not as a disguised sort option.

That means:

- `All` and `By Console` remain folder-oriented modes
- `Timeline` becomes a capture-oriented mode
- the toolbar can still host the control, but the behavior is materially different
- selection, layout, and actions must become mode-aware

## Goals

- Add a full-width timeline mode that shows capture tiles ordered by capture time.
- Reuse indexed capture dates instead of walking file timestamps on the hot path.
- Keep game and console context visible with subtle chips or compact metadata.
- Preserve the current Library search behavior as much as possible.
- Keep the first pass low-risk by reusing existing virtualization and tile infrastructure.

## Non-goals for the first pass

- Do not change import output paths, scanner persistence, or game-index identity.
- Do not rewrite the existing folder-based Library modes.
- Do not add video clips to the first timeline pass unless they come along for free.
- Do not turn the first pass into a new Collections system, Favorites view, or editorial workflow.
- Do not redesign the whole Library chrome while adding this mode.

## Current code reality

The current codebase already has most of the structural pieces needed.

### What already exists

- browser-only view projection in `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserViewModel.cs`
- centralized Library show wiring in `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserShowOrchestration.cs`
- split-pane layout construction in `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserLayout.cs`
- virtualized folder-row rendering in `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserRender.FolderList.cs`
- date-grouped, capture-ordered detail rendering in `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserRender.DetailPane.cs`
- reusable capture tile construction in `C:\Codex\src\PixelVault.Native\UI\LibraryVirtualization.cs`

### Why that matters

PixelVault already knows how to:

- merge file paths from multiple source folders
- resolve capture dates from the metadata index
- render long capture lists with virtualized rows
- keep capture selection state across rerenders

So the project is not blocked on missing foundations.

## Proposed UX

### Mode controls

Add a third grouping/browse pill:

- `All`
- `By Console`
- `Timeline`

Behavior:

- `All` remains the default until timeline mode proves itself
- `Timeline` is persisted in settings as a first-class browser mode
- when `Timeline` is active, folder-specific chrome is hidden or downgraded

### Layout

In timeline mode:

- hide the left folder pane
- hide the splitter
- expand the right pane to full width
- replace the folder banner with a compact timeline header
- show only capture tiles, grouped by date and ordered newest-first inside each date group

### Tile metadata

Each capture tile should stay visually photo-first.

Context should be lightweight:

- game title
- console/platform label
- optional small icon or badge for console
- optional compact capture-time label when useful

The timeline should not look like a spreadsheet or metadata grid.

### Search and filters

Phase 1 expectation:

- search should continue to respect the current Library search box
- timeline contents should follow the same visible-folder scope as the current browser projection
- existing sort pills should be reduced or disabled when timeline mode makes them redundant

### Actions

Phase 1 actions should stay conservative:

- open file metadata editor
- multi-select captures
- delete selected captures
- use image as cover when that still maps cleanly to a real folder
- open containing folder from a capture tile context menu

Folder-level actions that do not make sense globally should be hidden or disabled in timeline mode.

## Implementation phases

### Phase 1: Timeline shell

Goal:

- add the mode and get a usable full-width image timeline rendering from indexed data

Scope:

- add persisted `LibraryBrowserMode` or equivalent normalized state
- add a `Timeline` control in Library chrome
- make layout mode-aware in `MainWindow.LibraryBrowserLayout.cs`
- build a timeline projection from visible library folders and indexed capture dates
- render only image captures in a single full-width virtualized feed

Definition of done:

- timeline mode opens and renders without folder selection
- images appear newest-first and grouped by day
- browse performance stays acceptable on large libraries

### Phase 2: Context and mode-aware actions

Goal:

- make the timeline understandable without overloading it

Scope:

- add subtle game and console chips to capture tiles
- add a small timeline header summary
- hide or rewrite folder-specific buttons while timeline mode is active
- make selection and delete flows work naturally in timeline mode

Definition of done:

- a user can identify what game/platform a capture belongs to without opening metadata
- toolbar and context-menu actions feel coherent in timeline mode

### Phase 3: Polish and navigation

Goal:

- make the timeline pleasant for long-range browsing

Scope:

- add jump affordances such as today / this month / oldest if still needed
- restore timeline scroll and search state across reopen
- consider clip support only if performance and UI still feel clean
- tune tile sizing or density for timeline mode separately if the current detail sizing feels cramped

Definition of done:

- timeline mode feels intentional, not like a repurposed detail pane
- session restore is reliable
- the mode is stable enough for normal daily browsing

## Technical approach

### Recommended model

Introduce a mode-aware timeline projection rather than bending `LibraryBrowserFolderView` into a capture model.

The likely new shape is:

- keep `LibraryBrowserFolderView` for folder-oriented modes
- add a small timeline item model for capture rows
- reuse `VirtualizedRowHost` row-building, but not the folder-card projection

### Data source

Build the timeline feed from:

- the currently loaded `LibraryFolderInfo` rows
- merged `FilePaths`
- indexed capture dates from the metadata index

Prefer indexed dates first, with existing metadata repair fallback when the index is stale.

### Layout strategy

The cleanest first pass is:

- keep the existing right-pane detail renderer as the conceptual base
- move timeline-specific row building into a dedicated renderer instead of overloading folder-detail code forever
- make the layout container decide between split mode and full-width timeline mode

### Search strategy

For phase 1, reuse the current Library search text and visible-folder scope.

That means timeline mode initially reflects:

- all visible games in `All`
- visible console-grouped rows in `By Console`
- then flattens their image captures into one stream

If that feels confusing later, timeline can get its own scoping controls in a follow-up slice.

## Risks and mitigation

### Risk: mode complexity leaks into every handler

Mitigation:

- add one normalized browser-mode concept early
- gate layout, selection, and toolbar behavior from that single mode
- avoid ad-hoc `if timeline` checks scattered through unrelated paths

### Risk: performance regresses on large libraries

Mitigation:

- reuse virtualized rows
- build rows from indexed metadata, not repeated filesystem walks
- keep phase 1 image-only if clip support complicates decode and hover-preview behavior

### Risk: timeline becomes visually noisy

Mitigation:

- keep context chips small
- avoid permanent large metadata panels per tile
- bias toward photo-first presentation with secondary context

### Risk: folder-level actions become ambiguous

Mitigation:

- hide or relabel folder-scoped actions in timeline mode
- prefer capture-scoped context menus in the first pass

## Acceptance criteria

- A user can toggle into `Timeline` from the Library browser without leaving the window.
- The layout becomes a single full-width capture feed.
- The feed is ordered by capture time, not by game or console.
- Tiles show enough context to identify game and console at a glance.
- Selection, delete, and single-file metadata editing still work.
- The mode does not reintroduce obvious UI-thread stalls on large libraries.

## Manual verification focus

Use `C:\Codex\docs\LIBRARY_TIMELINE_MODE_VERIFICATION.md` once implementation starts.

## Repo touch points

Expected files for the first implementation pass:

- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserLayout.cs`
- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserShowOrchestration.cs`
- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserWorkingSet.cs`
- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserRender.DetailPane.cs`
- `C:\Codex\src\PixelVault.Native\UI\Library\MainWindow.LibraryBrowserRender.FolderList.cs`
- `C:\Codex\src\PixelVault.Native\UI\LibraryVirtualization.cs`
- `C:\Codex\src\PixelVault.Native\Services\Config\SettingsService.cs`
- `C:\Codex\src\PixelVault.Native\Services\Config\AppSettings.cs`

## Notion sync note

Notion should track three things for this initiative:

- a wiki/spec page for the feature
- a roadmap initiative entry
- at least one backlog task for the first implementation slice
