# PV-PLN-LIBWS-001 — Library workspace modes (Folder / Photo / Timeline)

> **Archived** (2026-04-08). This file is the full **done** snapshot of `PV-PLN-LIBWS-001`. For what to build next, use `docs/ROADMAP.md`, `docs/HANDOFF.md`, and active plans under `docs/plans/README.md`.

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-LIBWS-001` |
| **Status** | **Done** (archived 2026-04-08) |
| **Owner** | PixelVault / Codex |
| **Source brief** | `docs/pixelvault_folder_photo_workspace_staged_plan.txt` (superseded for mode count; this doc remains the full record for **three** modes) |
| **Related** | `docs/plans/PV-PLN-V1POL-001-pre-v1-polish-program.md` (polish program; palette/chrome may intersect), `docs/DOC_SYNC_POLICY.md` |

## Purpose

Replace the **two peer surfaces** model (folder browser and photo/detail vying for width) with a **workspace model** where the user is clearly in one of **three** modes:

1. **Folder** — default; folder grid is the main canvas; search, filters, sort, and grouping stay available.
2. **Photo** — focused workspace for the active game’s photos/captures; folder browsing is **not** co-equal. A **narrow cover rail** (mini folder list for the active game context) ships today and is subordinate to the main capture grid.
3. **Timeline** — **distinct** from Folder and Photo; its own layout contract and future feature set. Today’s timeline behavior (e.g. collapsing the folder pane so the timeline surface leads) should be expressed as **Timeline mode**, not as an ad hoc variant of split layout that fights Photo.

**Sequencing:** Land **Folder + Photo** workspace clarity first; keep **Timeline as a first-class mode** in state and layout from the start so refactors do not treat it as a special case of “photo” or “folder only.” Deep Timeline **features** can follow this plan; the immediate goal is **correct mode separation** and **non-competing shells**.

## Product goals

- Stop left/right **peer competition** for width in Folder and Photo modes.
- **Folder** is what users see at rest; **Photo** is a deliberate “enter workspace” action.
- **Timeline** remains available and **does not** share Photo’s layout contract unless intentionally unified later.
- Narrow/snap layouts stay intentional (not phone mimicry).
- **Manual density** for covers and photos (user-led, clamped to fit), per the source brief — after the mode + shell work is sound.

## Mode rules (contract)

| Mode | Main canvas | Folder grid | Photo/detail grid |
|------|-------------|-------------|-------------------|
| **Folder** | Folder grid | Visible (primary) | Collapsed or hidden |
| **Photo** | Photos/captures for active game | **Narrow rail** (covers list): subordinate, not peer | Primary |
| **Timeline** | Timeline surface | Per existing timeline UX, but governed by **Timeline mode** | As today’s timeline layout dictates — **not** the same as Photo unless explicitly designed |

**Global rules**

- Do not solve the competition problem mainly with a richer header.
- **Explicit mode** in working set / shell: no inferring mode from multiple loosely related flags.
- Preserve virtualization, decode priority, batched metadata I/O, and non-blocking mode switches (see source brief §7).

## Technical direction

- **State:** introduce something equivalent to `LibraryWorkspaceMode` with **at least** `Folder`, `Photo`, and `Timeline` (names may match existing enums if extended rather than duplicated).
- **Layout:** extend or refactor `ApplyLibraryBrowserLayoutMode(...)` (and related split/timeline paths) so **workspace mode** is an explicit input, not only “is timeline on.”
- **Hooks:** same file anchors as the source brief — `MainWindow.LibraryBrowserWorkingSet.cs`, `MainWindow.LibraryBrowserSplitLayout.cs`, `MainWindow.LibraryBrowserLayout.cs`, `MainWindow.LibraryBrowserShowOrchestration.cs`, folder/photo render and orchestration partials.

## Staged delivery (with test pauses)

Implement in **small vertical slices**. After each **TEST GATE**, run manual checks for everything delivered so far; fix regressions before the next slice.

### Progress snapshot (canonical)

| Step | Status | Notes |
|------|--------|--------|
| 1 | **Done** | `LibraryWorkspaceMode`, `LibraryBrowserWorkingSet.WorkspaceMode`, helpers; `LibraryBrowserSyncWorkspaceModeWithGrouping`; tests in `LibraryWorkspaceModeTests`. |
| 2 | **Done** | `ApplyLibraryBrowserLayoutMode`: Folder → folder grid owns canvas, detail hidden. |
| 3 | **Done** | Photo shell: main capture area + explicit layout; **rail shipped early** (see mode table). |
| 4 | **Done** | Timeline forces `WorkspaceMode.Timeline`; layout branches on mode, not ad hoc collapse. Exiting timeline grouping → Folder unless Photo was preserved (Photo kept when switching among non-timeline groupings). |
| 5 | **Done** | Enter/exit paths + **restoration scope** documented (see **Photo workspace: exit & restoration**). |
| 6 | **Done** | Folder + Photo density controls and persistence; detail packed cards respect user tile size (`detailTileSize` wiring). |
| 7 | **Done** | Slim rail delivered as part of Photo (intentionally before this step in the original sequence). |
| 8 | **Done** | Photo chrome overlap / compaction (`ApplyLibraryPhotoDetailChromeLayout`); **narrow width**: title/footer sizing, `WrapPanel` for primary actions, rail column `MinWidth` 0, capture layout control constraints. |
| 9 | **Done** | Photo **hero/banner** strip (SteamGrid / custom hero); graceful without art. |
| 10 | **Done** | **Manual regression matrix** below for ongoing release checks; automated tests include mode transitions (`LibraryWorkspaceModeTests`), masonry/layout tests. |

---

### Step 1 — Three-valued workspace mode + invariants

**Status: Done.**

**Deliverable:** `Folder` | `Photo` | `Timeline` on the working set (or single authoritative model); helpers (`IsFolderWorkspace`, `IsPhotoWorkspace`, `IsTimelineWorkspace`); active game/folder tracking unchanged where it already exists.

**Notes:** Wiring every entrypoint can wait; the codebase should **not** represent Timeline only as “folder collapsed.”

**TEST GATE:** Unit tests for default mode, and for transitions already present in codepaths (e.g. toggling timeline vs selecting folder). No user-visible regression required if behavior is unchanged.

---

### Step 2 — Folder workspace shell

**Status: Done.**

**Deliverable:** In **Folder** mode, folder grid owns the main content area; photo/detail surface collapsed or hidden (reuse existing split/timeline precedents, but keyed off **Folder** mode).

**TEST GATE:** Launch → folder-first canvas; snap/narrow; virtualization still OK.

---

### Step 3 — Photo workspace shell

**Status: Done** (with **narrow rail** per product choice — see mode table).

**Deliverable:** In **Photo** mode, photo/detail owns the main area; folder pane hidden (no slim rail in this slice unless trivially already there).

**TEST GATE:** Enter/exit Photo (temporary dev entry OK if open/close UI lands in Step 5) without breaking layout.

---

### Step 4 — Timeline workspace shell (explicit)

**Status: Done.** **Choice documented:** Exiting timeline grouping sets **Folder** unless **Photo** was already active — then Photo is **preserved** when switching among **non-timeline** groupings (`LibraryBrowserSyncWorkspaceModeWithGrouping`).

**Deliverable:** Entering timeline sets **Timeline** mode; layout matches **today’s** intended timeline presentation, but **layout code** branches on `Timeline` explicitly. Exiting timeline returns to **Folder** (or last non-timeline mode if you already persist that — document the choice).

**TEST GATE:** Timeline on/off, narrow width, no accidental Photo layout; Folder ↔ Timeline transitions stable.

---

### Step 5 — Enter/exit Photo (discoverability + persistence)

**Status: Done** — behavior locked; restoration expectations documented below.

**Deliverable:** Double-click + explicit open on folder tile; back/close in Photo; preserve selection; define and implement **scroll/position** restoration scope; keyboard path (e.g. Escape/back) and command palette entries if low cost.

**TEST GATE:** Full Folder ↔ Photo loop feels intentional; checklist from source brief §9 for open/close.

#### Photo workspace: exit & restoration

**Entering Photo** (`LibraryBrowserEnterPhotoWorkspace` in `MainWindow.LibraryBrowserWorkspaceMode.cs`):

- Sets `ScrollPhotoRailSelectionToTopPending = true` so the **mini cover rail** scrolls the current game into view from the top on the next folder-list render (`MainWindow.LibraryBrowserRender.FolderList.cs`).
- Sets `WorkspaceMode` to **Photo**, calls `showFolder(folder)`, `ApplyLibraryBrowserLayoutMode`, refreshes sort/filter chrome, rerenders the folder list.

**Exiting Photo** (`LibraryBrowserExitPhotoWorkspace`):

- Clears `PhotoRailExcludedConsoleLabels` (console badge filters in the detail grid).
- Sets `WorkspaceMode` to **Folder** and reapplies layout.
- Refreshes sort/filter chrome and invokes `renderTiles` (full folder/detail refresh path used by the orchestrator).

**User actions that exit:** splitter **“Back to cover-only list”** button; **Escape** exits Photo unless the quick-edit drawer is open (Escape closes the drawer first); command palette **`workspace_back_to_folders`**.

**What is not restored or guaranteed**

- **No explicit restoration** of main folder grid scroll offset, capture grid scroll position, or expanded/collapsed UI state from before Photo entry; exit relies on **`renderTiles`** and current **`Current` selection** — typically the same game stays selected, but prior scroll positions are not persisted across the mode switch.
- **`ScrollPhotoRailSelectionToTopPending`** applies on **enter**, not on exit; exiting does not set a mirror “restore rail scroll” flag.

---

### Step 6 — Manual density (folder + photo) + persistence

**Status: Done.**

**Deliverable:** One intentional View/Layout control per of **Folder** and **Photo**; persisted settings; user preference drives layout with viewport **clamp** only.

**TEST GATE:** Restart persistence; narrow clamp; no auto-layout fighting the user.

---

### Step 7 — (Optional) Slim jump rail in Photo

**Status: Done** (delivered in tandem with Step 3).

**Deliverable:** Only after Steps 2–6 stable; narrow, subordinate rail.

**TEST GATE:** Photo remains photo-first.

---

### Step 8 — Photo chrome compaction

**Status: Done.**

**Deliverable:** Compact Photo top region; overflow rules for narrow desktop widths.

**TEST GATE:** No clipped primary actions.

**Implemented notes:** `ApplyLibraryPhotoDetailChromeLayout` toggles compact typography and padding; primary action row uses a horizontal **WrapPanel** (`MainWindow.LibraryBrowserLayout.cs`); narrow path tightens right-pane padding and photo layout control alignment (`MainWindow.LibraryBrowserSplitLayout.cs`).

---

### Step 9 — (Optional) Hero/banner in Photo

**Status: Done** (banner strip + download/custom hero paths).

**Deliverable:** Only after workspace stable; graceful without art.

**TEST GATE:** Missing art does not degrade UI.

---

### Step 10 — Verification

**Status: Done** — matrix retained below for release regression; automated tests extended as listed in Step 10 row.

**Deliverable:** Regression pass: default Folder, Photo open/close, **Timeline independent**, density persistence, snap, virtualization, caching. Add/extend automated tests for mode transitions and settings round-trip.

**TEST GATE:** Final sign-off.

#### Manual regression matrix (Step 10)

Run after meaningful library UI changes; record failures as bugs, not as plan drift.

| Area | Check |
|------|--------|
| **Default** | Cold launch → **Folder** mode; main canvas is folder grid; detail/photo not peer-competing for width. |
| **Photo enter** | Double-click / Open captures → **Photo**; rail visible; hero strip + title chrome; capture grid fills main area. |
| **Photo narrow** | Shrink window / snap: title readable; **Open folder**, refresh, achievements row **wraps** (no permanent horizontal scroll); **Photo size** control visible. |
| **Photo exit** | Divider back, **Escape**, palette “back to folders” → **Folder**; console badge filters **cleared**; folder grid usable. |
| **Rail scroll** | Enter Photo from deep in list → rail scrolls selection **toward top** once (pending flag). |
| **Timeline** | Enable timeline grouping → **Timeline** layout; disable → **Folder** (or Photo preserved if switching among non-timeline groupings — see Step 4). |
| **Density** | Folder + Photo tile/size controls persist across restart (smoke). |
| **Performance** | Large library: scroll folder grid + photo grid without long UI stalls (smoke). |

---

## After this plan (historical)

The initiative is **closed**. Deeper **Timeline** work and further library UX belong in **new** plans. Reuse the **manual regression matrix** above when touching library layout.

## PR / branch grouping (suggested)

| PR | Scope |
|----|--------|
| **A** | Steps 1–2 (mode + Folder shell) |
| **B** | Steps 3–4 (Photo shell + explicit Timeline shell) |
| **C** | Step 5 (enter/exit + restoration) |
| **D** | Step 6 (density) |
| **E** | Steps 7–8 (optional rail + Photo chrome) |
| **F** | Step 9 (optional hero) + Step 10 (verification) |

Adjust PR boundaries if a smaller merge is needed; **keep Step 4 (Timeline explicit)** with the shell work so Timeline never depends on Photo/Folder hacks.

## Manual verification checklist (additions for Timeline)

- From **Folder**, enable Timeline → **Timeline** mode layout; folder/photo do not incorrectly show as peers.
- From **Timeline**, disable Timeline → returns to **Folder** (or documented behavior).
- **Photo** and **Timeline** are never confused in layout (e.g. Photo commands or density do not apply to Timeline unless explicitly specified later).

## Changelog / Doc sync

This plan ID remains valid for commit history and cross-links. Prefer **`docs/DOC_SYNC_POLICY.md`** for Notion.

| Date | Change |
|------|--------|
| 2026-04-06 | Initial plan: three-mode workspace (Folder / Photo / Timeline); staged delivery with test gates. |
| 2026-04-06 | **Shipped in 0.075.007:** Photo mode detail header — **per-game** console badges **right of the title**; badges **toggle visibility** of that platform’s captures in the **photo/detail grid** only (mini cover rail unchanged). Dimmed = excluded; selection change clears exclusions. See **`docs/CHANGELOG.md` (0.075.007)**. |
| 2026-04-08 | **Plan sync:** Status set to *core shipped*; Steps **1–4, 6–7, 9** marked **Done**; **5** mostly done; **8** partial; **10** open. Photo contract updated (**narrow rail** shipped). Timeline exit behavior **documented** (Photo preserved when appropriate). Added **What to do next** section. |
| 2026-04-08 | **Close-out batch:** Step **5** — **Photo workspace: exit & restoration** subsection (code-accurate). Step **8** → **Done** (narrow chrome + wrap). Step **10** — **manual regression matrix** + partial status. Tests: `LibraryWorkspaceModeTests`, masonry aspect test aligned with **justified rows**. |
| 2026-04-08 | **Archived:** Plan marked **Done**; file moved to **`docs/archive/`**; active plan index updated. |

## Recent implementation (repo)

- **0.075.007 — Photo detail console filter:** Working set holds normalized excluded labels (`PhotoRailExcludedConsoleLabels`); detail render filters file lists via `DetermineFolderPlatform` + metadata index. `RefreshDetailPaneForPhotoFilters` re-renders only the detail pane on badge toggle. Title row uses a two-column grid so badges stay right-aligned with the game name.
- **Workspace core (multiple releases through 0.075.x):** `LibraryWorkspaceMode`, `ApplyLibraryBrowserLayoutMode`, `LibraryBrowserEnterPhotoWorkspace` / `ExitPhotoWorkspace`, Photo **hero/banner** chrome, **density** persistence for folder + detail grids, command palette / Escape / divider for Photo exit. See **`src/PixelVault.Native/UI/Library/`** partials (`*SplitLayout*`, `*WorkspaceMode*`, `*ShowOrchestration*`, `*PhotoHero*`).
- **Capture grid layout (post-plan tweak):** Day-card thumbnails use **equal-height justified rows** (`BuildLibraryDetailMasonryChunks`) to avoid dead space from column masonry + hero spans; hero tiles within the day card removed in favor of a flush grid.
