# Next trim and optimization plan

**Purpose:** Rank **near-horizon** shrink, perf, and hardening work after the **`0.075.xxx`** version train and completed MainWindow extraction **A–F**. Aligns with `PERFORMANCE_TODO.md`, `PERFORMANCE_MONOLITH_SLICE_PLAN.md`, `CODE_QUALITY_IMPROVEMENT_PLAN.md`, and `ARCHITECTURE_REFACTOR_PLAN.md`.

**Published build (doc refresh):** **`0.075.010`** — see [`docs/CURRENT_BUILD.txt`](CURRENT_BUILD.txt) and [`docs/CHANGELOG.md`](CHANGELOG.md).

**Staged MainWindow thin-out (post–extraction A–F):** [`docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md`](plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) and [`docs/ios_foundation_guide.md`](ios_foundation_guide.md).

**Notion (Project Wiki):** [Next trim and optimization plan](https://www.notion.so/33873adc59b68131ae12f82c97363684) — mirror of this doc; update when tiers complete ([`docs/DOC_SYNC_POLICY.md`](DOC_SYNC_POLICY.md)).

**Small UI / polish backlog (tiered by effort):** [`docs/SMALL_FEATURE_TODO.md`](SMALL_FEATURE_TODO.md) — same items as [PixelVault HQ — Small Feature To-Do](https://www.notion.so/cf63eb7c2524470d9b9a601ab46f4cc6).

**Review-driven hardening (P1/P2):** [`docs/plans/open/PV-PLN-RVW-001-post-app-review-hardening.md`](plans/open/PV-PLN-RVW-001-post-app-review-hardening.md) (from [`docs/APP_REVIEW_2026-04-12.md`](APP_REVIEW_2026-04-12.md)).

---

## Measured baseline (2026-04-12, repo `src/PixelVault.Native`)

Line counts are **non-blank lines** (`Measure-Object -Line`) for orientation only—they drift with every edit.

| File (relative to `src/PixelVault.Native/`) | ~Lines | Note |
|---------------------------------------------|-------:|------|
| **`PixelVault.Native.cs`** | **~1.9k** | Still the **largest** single compilation unit; further vertical moves per UI-001 / monolith slice plan. |
| **`Services/Indexing/IndexPersistenceService.cs`** | **~1.5k** | Persistence hot spot; split by schema domain only when that work is active. |
| **`UI/Editors/FilenameConventionEditorWindow.cs`** | **~1.4k** | Large editor surface; pair with **PV-PLN-FNRU-001** / **PV-PLN-RVW-001** when touching rules UI or regex safety. |
| **`UI/Library/MainWindow.LibraryBrowserRender.DetailPane.cs`** | **~1.1k** | Library detail / timeline render; profile before broad refactors. |
| **`Services/FilenameParsing/FilenameParserService.cs`** | **~1.1k** | Parser + built-ins; **custom regex hardening** in RVW-001. |
| **`Services/Covers/CoverService.cs`** | **~1.1k** | Covers + hero fetch; **banner dedupe** in RVW-001. |

Historical context: older plans referred to **`PixelVault.Native.cs` ~2.9k lines** and a **post–0.854** milestone—that era is **closed**; the **`0.854`** entry remains in **`CHANGELOG.md`** as a docs-only publish marker.

---

## Tier 1 — Structure (done or owned elsewhere)

| Priority | Item | Rationale |
|----------|------|-----------|
| **1a** | **`MainWindow.ManualMetadata` (split across ~5 partials)** | **Done (2026-04-04):** Orchestration in **`MainWindow.ManualMetadata.cs`** (~550 lines); **`MainWindow.ManualMetadata.Helpers.cs`**, **`Layout.cs`**, **`SteamSearch.cs`**, **`Finish.cs`** hold dialog layout, Steam match flow, finish/confirm, and shared helpers. |
| **1b** | **`ImportWorkflow` (split partials)** | **Done (2026-04-04):** Core intake/progress orchestration in **`Import/ImportWorkflow.cs`**; **`MainWindow.ImportWorkflow.Progress.cs`** (`RunBackgroundWorkflowWithProgress`); **`MainWindow.ImportWorkflow.Steps.cs`** (rename/move/metadata/delete/sort/undo). Further steps can move into **`IImportService`** when touching behavior. |
| **1c** | **`PixelVault.Native.cs` ctor / composition** | **Done (2026-04-04):** **`MainWindow.IndexServicesWiring.cs`** — static **`CreateIndexFilenameRulesServices`** builds index persistence, filename parser, game-index assignment, and filename rules from one place; ctor assigns via deconstruction (no behavior change). |

---

## Tier 2 — Performance / polish (profile or user reports)

| Item | Notes |
|------|--------|
| Library virtualization **`SizeChanged`** debounce | Same pattern as search debounce if resize-driven rerenders show up in traces (`CODE_QUALITY_IMPROVEMENT_PLAN.md`). |
| Capture grid “full” virtualization | `PERFORMANCE_TODO.md` — only if N-scaling issues remain after viewport cancel work. |
| Video-heavy folders | Poster-first / stricter off-screen unload if jank returns. |

---

## Tier 3 — Correctness / hardening (smaller, test-friendly)

| Item | Notes |
|------|--------|
| **`SteamAppIdLooksLikeFilenamePrefix`** | Require separator + length bounds (`CODE_QUALITY_IMPROVEMENT_PLAN.md`, `SteamImportRename.cs`). |
| Steam rename **single doc** | One module-level docblock linking parser, `ImportWorkflow`, and tests. |
| **`BuildLibraryFolderInventoryStamp`** | NAS / huge trees — session cache or off-UI if startup stalls reported. |
| **User-authored filename regex** | **PV-PLN-RVW-001** Phase 1 — timeouts, NonBacktracking where compatible, save-time limits (`FilenameRulesService` / `FilenameParserService`). |

---

## Tier 4 — Larger refactors (only with intent)

| Item | Notes |
|------|--------|
| **`IndexPersistenceService.cs`** (~1.5k lines) | Split by schema domain (e.g. photo vs game vs conventions) when persistence work spikes—not required for MainWindow trim. |
| Nullable / analyzer passes | Per-file or project slice when new extracted files are added (`ROADMAP.md` Phase 4). |

---

## Verification

- **`dotnet test`** on `PixelVault.Native.Tests` after each slice.
- **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** when Library, import, manual metadata, or game index editors change.

## Doc hygiene

- When a tier is finished, move bullets to `CHANGELOG.md` / `HANDOFF.md` and trim this file so it stays a **short** horizon (avoid duplicating `PERFORMANCE_TODO.md` long-term).
- After each **published** build, keep **`docs/CURRENT_BUILD.txt`**, **`docs/CHANGELOG.md`**, and **`docs/HANDOFF.md`** aligned ([`docs/VERSIONING.md`](VERSIONING.md)).
