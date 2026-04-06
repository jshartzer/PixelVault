# PV-PLN-V1POL-001 — Pre-V1 polish program

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-V1POL-001` |
| **Status** | Active (planning / execution) |
| **Owner** | PixelVault / Codex |
| **Source brief** | `docs/PRE-V1 POLISH AND FEATURE REVIEW AND UPDATE.txt` |
| **Related** | `docs/LIBRARY_PERFORMANCE_PLAN.md` (performance, largely complete), `docs/DOC_SYNC_POLICY.md` (repo ↔ Notion) |

## Purpose

Deliver a **production-ready, cohesive** desktop experience before V1: intentional visuals, trustworthy feedback, fewer interruptions, helpful empty states, power-user commands, curated library views, and a clear **health / setup** surface—without reckless redesign or framework churn.

**Principles:** incremental slices, preserve speed and existing workflows, repo-appropriate abstractions, behavior changes only when justified.

---

## Grounded audit (repo snapshot)

### Surfaces

- **Primary:** Library browser (`MainWindow` + `LibraryBrowserShowOrchestration` / `ILibraryBrowserShell`).
- **Settings:** `SettingsShellHost` — modal Settings + Path Settings.
- **Secondary windows:** Game / Photo index editors, filename rules editor, intake / metadata review, photography gallery, import summaries, changelog viewer, library shortcuts help (**modal**).

### Feedback patterns

- **Library toasts:** `MainWindow.LibraryBrowserToastAndShortcuts.cs` (bottom-right, timed).
- **MessageBox:** Widespread (`PixelVault.Native.cs`, import, editors, library commands, starred export, etc.) — main candidate for **non-blocking** replacement.
- **Status line:** `TextBlock status`; folder refresh sets plain-text loading messages.

### Visual system

- **`UiBrushHelper.FromHex`** only — no semantic tokens; many repeated hex literals (`#0F1519`, `#141B20`, `#A7B5BD`, platform accents, toast chrome).
- **Global** `Themes/PixelVaultScrollBars.xaml` — precedent for shared theme resources.

### Loading / empty

- Folder grid: `LibraryFoldersLoading` + single-line `TextBlock` empty/loading copy in `MainWindow.LibraryBrowserRender.FolderList.cs` — adequate but not “product” empty states (no CTA, no skeleton).
- Detail / cover pipeline: strong perf work already; still room for **localized** placeholders and clearer **stale vs loading**.

### Commands / keyboard

- **F1** → modal shortcut list; **Enter** in search; ctrl/shift multi-select.
- **No** command palette or centralized command registry.

### Smart-view building blocks

- **Folder filters:** `libraryFolderFilterMode` (e.g. 100%, cross-platform, capture thresholds) — curated slices over folders.
- **Starred:** `photo_index.starred`, Export Starred, toolbar wiring.
- Definitions for “missing cover / missing IDs / needs metadata” map to **existing indexes**; avoid new backends until necessary.

### Health / diagnostics

- Path Settings lists paths and tools but not a **status dashboard**.
- Troubleshooting logs exist; **`PixelVault.LibraryAssets`** health types exist but are **not wired** into WPF (future enrichment).

---

## Execution roadmap (slices)

Recommended order balances **safety**, **reuse of seams** (`ILibraryBrowserShell`, `SettingsShellDependencies`), and **visible V1 readiness**.

| Order | Slice | Risk | Notes |
|------|--------|------|--------|
| **A** | Design token foundation | Low–medium | Semantic colors/radius/spacing; migrate high-traffic UI first; preserve behavior. |
| **B** | Health / setup “control center” | Medium | File/tool/token **status rows**; copy diagnostics; links to Path Settings & logs. |
| **C** | Loading + empty states | Low–medium | Folder grid + detail hero; CTAs (clear search, open Path Settings, refresh). |
| **D** | Inline feedback / fewer modals | Medium | Audit `MessageBox.Show`; keep true confirms; toasts / inline banners elsewhere. |
| **E** | Command palette | Medium | Static commands → existing actions; keyboard chord; explicit registry (no fragile reflection). |
| **F** | Smart / curated views | Medium–high | Extend filters or preset chips; document each view’s definition; start with low-risk views. |
| **G** | Quick-edit drawer (staged) | High | Shell first; **narrow** flows only; full grid editors stay windows until validated. |
| **H** | Motion + accessibility | Low–medium | Short transitions; respect reduced motion; shared focus visuals from tokens. |
| **J** | Consistency cleanup | Low | Remaining literals, tooltips on icon-only controls, destructive styling parity. |

### Dependencies

- **E** benefits from **D** (fewer modals competing with palette-triggered flows).
- **F** pairs with **C** (empty states per view).
- **G** last among major features — avoids destabilizing editors early.

### Commits

- One vertical slice per milestone where possible: **A** → **B** → **C** → **D** (sub-scope per area: library / import / editors) → **E** → **F** → **G** → **H/J**.

### Feature flags (optional)

- Palette and drawer behind settings bools for safe dogfooding.

---

## Pillars × PixelVault mapping (10)

| # | Pillar | Primary leverage in repo |
|---|--------|---------------------------|
| 1 | Design tokens | New token layer + `Themes/` / `UiBrushHelper` usage |
| 2 | Loading / perceived performance | Folder list, detail pane, status copy; avoid blocking full UI |
| 3 | Inline feedback | Extend toast pattern or app-level toast; reduce MessageBox |
| 4 | Empty states | Folder, detail, intake, search, starred, tooling |
| 5 | Drawers | New host; staged quick edits only |
| 6 | Motion | Toasts, section expand; subtle only |
| 7 | Accessibility / trust | Focus, contrast, labels, destructive clarity |
| 8 | Command palette | New window/overlay + registry |
| 9 | Smart views | Filters + index-backed predicates |
| 10 | Health / setup | New surface + Path Settings data |

---

## File-level starting points

| Slice | Likely files / areas |
|-------|----------------------|
| A | New `UI/Design/DesignTokens.cs` and/or `Themes/PixelVaultTokens.xaml`; `SettingsShellHost.cs`, `MainWindow.LibraryBrowserToastAndShortcuts.cs`, library chrome/tiles |
| B | New `UI/Diagnostics/HealthDashboard*.cs`; callbacks mirroring `SettingsShellDependencies` |
| C | `MainWindow.LibraryBrowserRender.FolderList.cs`, `MainWindow.LibraryBrowserRender.DetailPane.cs` |
| D | Grep `MessageBox.Show` under `src/PixelVault.Native` |
| E | New `CommandPaletteWindow` + chord in `LibraryBrowserShowOrchestration` |
| F | `SettingsService` filter modes, `LibraryScanner` / index queries, library chrome |
| G | Drawer host + `ILibraryBrowserShell` bridge |
| H/J | `App.xaml` themes, shared `Button`/`Focus` styles |

---

## Verification (per slice)

- **A:** Visual parity on library + settings; reduced duplicate hex in touched files.
- **B:** Broken path/tool/token states read clearly; copy summary works.
- **C:** Cold start, empty library, search miss, empty detail.
- **D:** Golden paths; informational MessageBox count down.
- **E:** Each command matches manual UI; Esc closes.
- **F:** Each smart view matches documented definition.
- **G/H:** Keyboard-only smoke; optional reduced-motion check.

---

## Notion

Planning/status for this initiative: **[PV-PLN-V1POL-001 — Pre-V1 polish program](https://www.notion.so/33a73adc59b6819d8ddcc20b9f03b2d6)** (child of **PixelVault HQ**). Repo remains source of truth for technical detail; sync narrative/status per `docs/DOC_SYNC_POLICY.md`.

---

## Revision history

| Date | Change |
|------|--------|
| 2026-04-05 | Initial plan authored in repo (`PV-PLN-V1POL-001`). |
