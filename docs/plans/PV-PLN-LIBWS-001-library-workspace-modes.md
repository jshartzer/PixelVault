# PV-PLN-LIBWS-001 — Library workspace modes (Folder / Photo / Timeline)

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-LIBWS-001` |
| **Status** | Active (planning / execution) |
| **Owner** | PixelVault / Codex |
| **Source brief** | `docs/pixelvault_folder_photo_workspace_staged_plan.txt` (superseded for mode count; this doc is canonical for **three** modes) |
| **Related** | `docs/plans/PV-PLN-V1POL-001-pre-v1-polish-program.md` (polish program; palette/chrome may intersect), `docs/DOC_SYNC_POLICY.md` |

## Purpose

Replace the **two peer surfaces** model (folder browser and photo/detail vying for width) with a **workspace model** where the user is clearly in one of **three** modes:

1. **Folder** — default; folder grid is the main canvas; search, filters, sort, and grouping stay available.
2. **Photo** — focused workspace for the active game’s photos/captures; folder browsing is **not** co-equal (optional slim rail only after the takeover model is stable).
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
| **Photo** | Photos/captures for active game | Collapsed or hidden (optional slim rail **later**) | Primary |
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

### Step 1 — Three-valued workspace mode + invariants

**Deliverable:** `Folder` | `Photo` | `Timeline` on the working set (or single authoritative model); helpers (`IsFolderWorkspace`, `IsPhotoWorkspace`, `IsTimelineWorkspace`); active game/folder tracking unchanged where it already exists.

**Notes:** Wiring every entrypoint can wait; the codebase should **not** represent Timeline only as “folder collapsed.”

**TEST GATE:** Unit tests for default mode, and for transitions already present in codepaths (e.g. toggling timeline vs selecting folder). No user-visible regression required if behavior is unchanged.

---

### Step 2 — Folder workspace shell

**Deliverable:** In **Folder** mode, folder grid owns the main content area; photo/detail surface collapsed or hidden (reuse existing split/timeline precedents, but keyed off **Folder** mode).

**TEST GATE:** Launch → folder-first canvas; snap/narrow; virtualization still OK.

---

### Step 3 — Photo workspace shell

**Deliverable:** In **Photo** mode, photo/detail owns the main area; folder pane hidden (no slim rail in this slice unless trivially already there).

**TEST GATE:** Enter/exit Photo (temporary dev entry OK if open/close UI lands in Step 5) without breaking layout.

---

### Step 4 — Timeline workspace shell (explicit)

**Deliverable:** Entering timeline sets **Timeline** mode; layout matches **today’s** intended timeline presentation, but **layout code** branches on `Timeline` explicitly. Exiting timeline returns to **Folder** (or last non-timeline mode if you already persist that — document the choice).

**TEST GATE:** Timeline on/off, narrow width, no accidental Photo layout; Folder ↔ Timeline transitions stable.

---

### Step 5 — Enter/exit Photo (discoverability + persistence)

**Deliverable:** Double-click + explicit open on folder tile; back/close in Photo; preserve selection; define and implement **scroll/position** restoration scope; keyboard path (e.g. Escape/back) and command palette entries if low cost.

**TEST GATE:** Full Folder ↔ Photo loop feels intentional; checklist from source brief §9 for open/close.

---

### Step 6 — Manual density (folder + photo) + persistence

**Deliverable:** One intentional View/Layout control per of **Folder** and **Photo**; persisted settings; user preference drives layout with viewport **clamp** only.

**TEST GATE:** Restart persistence; narrow clamp; no auto-layout fighting the user.

---

### Step 7 — (Optional) Slim jump rail in Photo

**Deliverable:** Only after Steps 2–6 stable; narrow, subordinate rail.

**TEST GATE:** Photo remains photo-first.

---

### Step 8 — Photo chrome compaction

**Deliverable:** Compact Photo top region; overflow rules for narrow desktop widths.

**TEST GATE:** No clipped primary actions.

---

### Step 9 — (Optional) Hero/banner in Photo

**Deliverable:** Only after workspace stable; graceful without art.

**TEST GATE:** Missing art does not degrade UI.

---

### Step 10 — Verification

**Deliverable:** Regression pass: default Folder, Photo open/close, **Timeline independent**, density persistence, snap, virtualization, caching. Add/extend automated tests for mode transitions and settings round-trip.

**TEST GATE:** Final sign-off.

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

When execution starts, reference **`PV-PLN-LIBWS-001`** in commits and, if used, Notion per **`docs/DOC_SYNC_POLICY.md`**.

| Date | Change |
|------|--------|
| 2026-04-06 | Initial plan: three-mode workspace (Folder / Photo / Timeline); staged delivery with test gates. |
| 2026-04-06 | **Shipped in 0.075.007:** Photo mode detail header — **per-game** console badges **right of the title**; badges **toggle visibility** of that platform’s captures in the **photo/detail grid** only (mini cover rail unchanged). Dimmed = excluded; selection change clears exclusions. See **`docs/CHANGELOG.md` (0.075.007)**. |

## Recent implementation (repo)

- **0.075.007 — Photo detail console filter:** Working set holds normalized excluded labels (`PhotoRailExcludedConsoleLabels`); detail render filters file lists via `DetermineFolderPlatform` + metadata index. `RefreshDetailPaneForPhotoFilters` re-renders only the detail pane on badge toggle. Title row uses a two-column grid so badges stay right-aligned with the game name.
