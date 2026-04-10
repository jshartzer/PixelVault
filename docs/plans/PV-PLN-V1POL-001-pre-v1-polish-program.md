# PV-PLN-V1POL-001 — Pre-V1 polish program

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-V1POL-001` |
| **Status** | **In progress** — **A–E shipped** on train **`0.075.000`**–**`0.075.005`** (see **`docs/CHANGELOG.md`**); **F** largely shipped with follow-ups possible; **G** / **H/J** partial. Canonical slice detail: [Execution status](#execution-status-shipped-vs-remaining) below. |
| **Owner** | PixelVault / Codex |
| **Source brief** | `docs/PRE-V1 POLISH AND FEATURE REVIEW AND UPDATE.txt` |
| **Related** | `docs/LIBRARY_PERFORMANCE_PLAN.md` (performance, largely complete), `docs/DOC_SYNC_POLICY.md` (repo ↔ Notion) |

## Purpose

Deliver a **production-ready, cohesive** desktop experience before V1: intentional visuals, trustworthy feedback, fewer interruptions, helpful empty states, power-user commands, curated library views, and a clear **health / setup** surface—without reckless redesign or framework churn.

**Principles:** incremental slices, preserve speed and existing workflows, repo-appropriate abstractions, behavior changes only when justified.

---

## Execution status (shipped vs remaining)

Authoritative complements: **`docs/CHANGELOG.md`** (**`0.075.xxx`**), **`docs/SMART_VIEWS_LIBRARY.md`** (folder filters), execution notes in the [revision history](#revision-history) below.

| Slice | Intent | Status |
|-------|--------|--------|
| **A** | Design token foundation | **Shipped** — **`DesignTokens`** / shared theme direction referenced in **`0.075.000`** (library empty/loading polish train). |
| **B** | Health / setup “control center” | **Shipped** — **Setup & health** / **`HealthDashboardWindow`** path from Settings in same train (**`0.075.000`**). |
| **C** | Loading + empty states | **Shipped** — Folder list **skeleton / empty states** with CTAs; detail pane **empty / loading** copy and actions (**`0.075.000`**). |
| **D** | Inline feedback / fewer modals | **Shipped (scoped)** — **`TryLibraryToast`**, **`NotifyOrMessageBox`**, **`NormalizeForLibraryToast`**; many **OK-only** **`MessageBox`** paths use library toasts when the browser is live (**`0.075.001`**–**`0.075.003`**). **OKCancel / YesNoCancel** remains modal; owner-window placement improved for key flows. |
| **E** | Command palette | **Shipped (library)** — **`LibraryCommandPaletteRegistry`**, **Ctrl+Shift+P**, footer **⋯**, keywords + **Tab** into list; tests (**`0.075.004`**). |
| **F** | Smart / curated views | **Largely shipped** — **`docs/SMART_VIEWS_LIBRARY.md`**; **`LibraryBrowserFolderViewMatchesFilter`**; **`needssteam`** / **`nocover`**; unified **`missingid`** (**Missing ID**) with legacy aliases via **`SettingsService`** (**`0.075.005`** + plan revs **2026-04-05**). Palette **`quick_edit_panel`** = drawer **shell** only. **Remaining:** any extra presets/chips called out in smart-views doc, deeper “every view defined” checklist. |
| **G** | Quick-edit drawer (staged) | **Partial** — Shell / palette entry exists; **narrow in-place edits** and full validation of drawer UX **not** closed out in this plan doc. |
| **H** | Motion + accessibility | **Partial** — e.g. **`PixelVaultFocus.xaml`**, toast vs **client-area animation**, nav **tooltips**, control **automation** names (**2026-04-05** rev). No claim of full reduced-motion / focus audit. |
| **J** | Consistency cleanup | **Partial** — Ongoing; **literal/tooltip/destructive** sweep not finished as a single milestone. |

**Adjacent polish (not slice-tagged in changelog):** e.g. **library 100% badge** polish (**`0.075.006`**) — fits program goals but not a slice letter in the table above.

---

## Grounded audit (repo snapshot — **pre–`0.075` baseline**)

*Captured at plan author time; many gaps below are **addressed** in [Execution status](#execution-status-shipped-vs-remaining). Kept for original context and remaining gap-hunt (e.g. stray **`MessageBox`**).*

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
| **A** | Design token foundation | Low–medium | **Shipped** (baseline). Further migration of hex literals → tokens is optional **J** work. |
| **B** | Health / setup “control center” | Medium | **Shipped** (dashboard from Settings). Enrichment (e.g. more **`LibraryAssets`** surfaces) can continue. |
| **C** | Loading + empty states | Low–medium | **Shipped** (folder + detail). Per-view tune-ups pair with **F** follow-ups. |
| **D** | Inline feedback / fewer modals | Medium | **Shipped (scoped)**. Remaining **`MessageBox.Show`** audit = incremental **J** / spot fixes. |
| **E** | Command palette | Medium | **Shipped (library)**. Future: more commands / other surfaces if needed. |
| **F** | Smart / curated views | Medium–high | **Largely shipped**; **`docs/SMART_VIEWS_LIBRARY.md`** is source of truth for filter semantics. |
| **G** | Quick-edit drawer (staged) | High | **Partial** — shell only; narrow flows TBD. |
| **H** | Motion + accessibility | Low–medium | **Partial** — shared focus + some motion; full checklist open. |
| **J** | Consistency cleanup | Low | **Partial** — ongoing pass on literals, tooltips, destructive affordances. |

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
| F | `SettingsService` filter modes, `LibraryScanner` / index queries, library chrome — see **`docs/SMART_VIEWS_LIBRARY.md`** |
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
| 2026-04-05 | Slice **D** (MessageBox audit / toasts) treated complete for ship scope; Slice **E** started — library **Ctrl+Shift+P** command palette + confirm dialogs parented to library/review window. |
| 2026-04-05 | Slice **E**: **LibraryCommandPaletteRegistry** (ids + keywords), **⋯** toolbar control, sort/filter/group + clear-search commands, keyword filtering, **Tab** to list; tests for registry invariants + handler binding. |
| 2026-04-05 | Slice **F** (first cut): **`docs/SMART_VIEWS_LIBRARY.md`**, filters **`needssteam`** / **`nocover`**, **Filter** menu + palette; **`LibraryBrowserFolderViewMatchesFilter`** + tests. |
| 2026-04-05 | Slice **F**: filter **`missinggameid`** (no saved game index id on folder row); palette **`filter_missing_game_id`**; combined-view note in **`docs/SMART_VIEWS_LIBRARY.md`**. |
| 2026-04-05 | Slice **F** continuation: palette **`quick_edit_panel`** (quick-edit drawer shell). **H/J:** merged **`PixelVaultFocus.xaml`**, toast fade respects **client-area animation**, nav **tooltips**, delete control **automation name**. **Item 7:** starred-export fingerprint I/O via **`ILibrarySession`**. |
| 2026-04-05 | Slice **F**: single **`missingid`** filter (**Missing ID**) replaces **`needssteam`** / **`needssteamgrid`** / **`missinggameid`**; legacy settings values normalize to **`missingid`**. |
| 2026-04-08 | **Plan doc** aligned to **shipped** work: header **Status**, new **[Execution status](#execution-status-shipped-vs-remaining)** table, roadmap **Notes** column updated; **Grounded audit** labeled **pre–`0.075` baseline** so it does not override changelog truth. |
