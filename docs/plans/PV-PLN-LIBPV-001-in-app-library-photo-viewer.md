# PV-PLN-LIBPV-001 — In-app library photo viewer (non-modal, timeline chrome)

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-LIBPV-001` |
| **Status** | Draft (planning) |
| **Owner** | PixelVault / Codex |
| **Parent roadmap** | `docs/ROADMAP.md` — Library / capture experience (align when execution starts) |
| **Related** | [`PV-PLN-LIBWS-001`](../archive/PV-PLN-LIBWS-001-library-workspace-modes.md) (library modes baseline), [`PV-PLN-UI-001`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) (shell vs services), [`docs/DOC_SYNC_POLICY.md`](../DOC_SYNC_POLICY.md) |

## Purpose

Replace **double-click → OS default** as the primary way to inspect captures with an **in-app photo viewer**: **single primary click** opens a **non-modal** window sized to the **main application window**, **edge-to-edge** image, with **the same timeline-style chrome** (game title, console chip, comment strip) as detail tiles. Support **Ctrl+primary-click** to **only** participate in **selection** (delete, metadata, batch actions) without opening a viewer. Add **translucent left/right affordances** for **previous / next** image within a defined **navigation scope**.

**Non-goals (initial slice):** RAW development tools, HDR tone mapping, true multi-monitor “presenter” mode, replacing the external editor for all file types.

---

## Named policies (stable IDs for spec + tests)

Use these IDs in acceptance criteria, tests, and UI copy where helpful.

| Policy ID | Name | Statement |
|-----------|------|-----------|
| **PV-POL-LIBPV-OPEN-001** | Primary open | **Primary** mouse button **without Ctrl** on a library detail **image** tile **opens** the in-app viewer for that file (unless an interactive child already handled the click — see risks). |
| **PV-POL-LIBPV-SEL-001** | Modifier preserves selection | **Ctrl+primary** **does not** open the viewer; it **only** updates **detail selection** so toolbar actions (delete, metadata, etc.) remain reachable. |
| **PV-POL-LIBPV-SEL-002** | Shift range selection | **Shift+primary** continues to use the existing **range-select** behavior (`DetailSelectionAnchorIndex` + contiguous `visibleFiles` slice) and **does not** open the viewer (**PV-POL-LIBPV-OPEN-001** applies only when **neither** Ctrl nor Shift is held). |
| **PV-POL-LIBPV-WIN-001** | Non-modal windows | Viewer is a normal `Window` shown with **`Show()`** (never **`ShowDialog()`**); **no application-modal focus lock**; user may open **multiple** viewers and rearrange them. |
| **PV-POL-LIBPV-SIZE-001** | Match main frame | New viewer’s **client size** matches **owning main window** `ActualWidth` / `ActualHeight` at open time (recommend: re-sync on **main** `SizeChanged` while viewer open, debounced). |
| **PV-POL-LIBPV-CHR-001** | Timeline chrome parity | When context is available (timeline + indexed metadata), viewer shows the **same information hierarchy** as `BuildLibraryTimelineCaptureFooter` (game name, time, platform chip, comment editor strip). Photo-only folder context may show a **reduced** strip (document explicitly). |
| **PV-POL-LIBPV-NAV-001** | Overlay navigation | **Semi-transparent** **Previous** / **Next** hit targets on **left** and **right** edges; advance within **navigation sequence** (see below). At ends, **hide** or **disable** with clear affordance (no wrap unless **PV-POL-LIBPV-NAV-002** enabled later). |
| **PV-POL-LIBPV-KEY-001** | Keyboard safety | **Esc** closes the **active** viewer; **Left/Right** arrows navigate when viewer has focus; **do not** steal system shortcuts when focus is elsewhere. |

Optional later policy (not required for v1):

| **PV-POL-LIBPV-NAV-002** | Wrap navigation | Optional setting: prev/next **wrap** at list ends (default **off**). |

---

## Navigation scope (recommendation)

**Default:** navigation order = **`ws.DetailFilesDisplayOrder`** (already maintained for the visible detail pane) **filtered to images** (and optionally same extension set as today’s “open” behavior). Opening from timeline uses the **merged visible list**; opening from a single-game folder uses that folder’s ordering.

**Rationale:** Reuses one ordered list, matches user mental model of “what I was browsing,” avoids a second query layer for v1.

**Risk / mitigation:** If `DetailFilesDisplayOrder` is stale mid-background refresh — on open, **snapshot** the ordered paths + indices into the viewer model; **refresh** button or **listener** on detail re-render can optionally resync (Phase 2).

---

## Execution steps (staged)

### Phase A — Input routing and selection (library detail)

1. **Audit** current `MouseLeftButtonDown` / selection handlers on detail tiles (`CreateLibraryDetailTile` in `LibraryVirtualization.cs` and any orchestrator paths).
2. Implement **PV-POL-LIBPV-OPEN-001** + **PV-POL-LIBPV-SEL-001**:
   - If **`Keyboard.Modifiers` has `Control`**: call existing **`updateDetailSelection`** path **only** (no viewer).
   - Else: **open viewer** and optionally **still** set primary selection to that file (recommend: **yes**, so delete/metadata apply to “what I’m looking at” — document in manual checklist).
3. **Tests (manual first):** Ctrl+click range selection if Shift already exists; star button, day badge, and footer controls **must remain hit-testable** (star sits top-right — ensure open-on-click does not fire when clicking star).

### Phase B — Viewer shell (WPF)

4. New **`PhotoViewerWindow`** (or `LibraryCaptureViewerWindow`) under `UI/Library/` — `WindowStyle` / chrome per product (borderless vs thin caption); **`Show()`**, optional **`Owner`** = main window only if you want minimize-group behavior (**PV-POL-LIBPV-WIN-001** recommends **no Owner** for “free floating” — pick one and document).
5. **PV-POL-LIBPV-SIZE-001:** bind size to main window; handle **DPI** (`VisualTreeHelper.GetDpi`); min size floor (e.g. 480×360) to avoid unusable chrome.
6. **Image presenter:** `Image` + `BitmapImage` or existing load coordinator; **decode strategy:** start with **bounded max dimension** (e.g. long edge 4096) for v1, **full-res** toggle later.

### Phase C — Chrome reuse

7. **Extract or share** timeline footer: factor **`BuildLibraryTimelineCaptureFooter`** into a helper usable from both **virtualized tile** and **viewer** (same `LibraryTimelineCaptureContext`, same comment save path). **PV-POL-LIBPV-CHR-001.**
8. Wire **comment persistence** through existing `librarySession.RequestSaveCaptureComment` paths; if **two viewers** on same path: recommend **last save wins** + **refresh** on success (Phase 1); conflict UI optional later.

### Phase D — Prev / next overlays

9. **PV-POL-LIBPV-NAV-001:** left/right **transparent `Button` or `Border`** (e.g. 20% width **hit slop**, chevron `Path`, **Opacity ~0.35** idle, **0.55** hover); keyboard **PV-POL-LIBPV-KEY-001.**
10. **Navigation:** consume **snapshotted** ordered list + current index; skip missing files.

### Phase E — Lifecycle, memory, polish

11. **Registry** of open viewers (weak references or explicit list): **close all** on library root change optional; **dispose** bitmaps on close.
12. **Manual golden path:** `docs/MANUAL_GOLDEN_PATH_CHECKLIST.md` — timeline, photo workspace, Ctrl selection, two viewers, comment edit, prev/next at ends.
13. **`dotnet test`** when adding any testable selection or order helper.

---

## Risks and mitigations (summary)

| Risk | Mitigation |
|------|------------|
| **Single-click steals clicks** from star, comment editor, day badge, **prev/next** | Hit-test: **open** only when click target is **image/background**; interactive children **`IsHitTestVisible`** and **handled** events first; integration tests / checklist rows for each overlay. |
| **Ctrl conflicts** (Ctrl+wheel zoom later, Ctrl+Tab) | **Document** that **Ctrl+click** is selection-only in library grid; viewer zoom may use **Ctrl+wheel** only when viewer **IsActive** (future). |
| **Memory** with multiple full-screen bitmaps | **Decode cap** + **Freeze** where appropriate; **clear `Image.Source`** on close; **cancel** async decode when navigating away quickly. |
| **Stale navigation list** after refresh | **Snapshot** order at open; optional **“Refresh list”** or auto-resync on `DetailRenderSequence` change (Phase 2). |
| **Comment edit in two windows** | **Save** pushes to index + **PropertyChanged** / simple **message** to refresh other viewer showing same path (minimal: refresh on **LostKeyboardFocus** after save). |
| **Video files** in detail pane | **Open viewer only for `IsImage`** paths in v1; video keeps existing behavior or separate plan. |
| **Accessibility** | Overlays: **`AutomationProperties.Name`** (“Previous image”, “Next image”); focus order and **Esc** close. |

---

## Recommended extras (post–v1, prioritized)

1. **Zoom / pan** (wheel + drag) with **reset** control.  
2. **Copy path** / **Reveal in Explorer** in overflow menu.  
3. **Filmstrip** strip of thumbs for current **day** or current **folder**.  
4. **Wrap** navigation (**PV-POL-LIBPV-NAV-002**) as a setting.

---

## Doc sync

When execution starts or completes a slice: follow **`docs/DOC_SYNC_POLICY.md`** (CHANGELOG / HANDOFF as appropriate); mirror status in Notion if the team tracks this plan there.
