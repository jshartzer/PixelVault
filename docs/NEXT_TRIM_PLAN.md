# Next trim and optimization plan (post–0.854)

> **Note (2026-04-12):** This document’s baseline (version line, `PixelVault.Native.cs` line count) is **stale** relative to the current **`0.075.xxx`** train. A **full refresh** (facts, hotspots, tier ordering) is tracked under **[`docs/plans/PV-PLN-RVW-001-post-app-review-hardening.md`](plans/PV-PLN-RVW-001-post-app-review-hardening.md)** — Phase 0.

**Purpose:** Rank follow-up work after publishing **0.854**. Aligns with `PERFORMANCE_TODO.md`, `PERFORMANCE_MONOLITH_SLICE_PLAN.md`, `CODE_QUALITY_IMPROVEMENT_PLAN.md`, and `ARCHITECTURE_REFACTOR_PLAN.md`.

**Staged MainWindow thin-out (post–extraction A–F):** see **`docs/plans/PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md`** and **`docs/ios_foundation_guide.md`**.

**Notion (Project Wiki):** [Next trim and optimization plan (post-0.854)](https://www.notion.so/33873adc59b68131ae12f82c97363684) — mirror of this doc; update when tiers complete (`DOC_SYNC_POLICY.md`).

**Small UI / polish backlog (tiered by effort):** `docs/SMALL_FEATURE_TODO.md` — same items as [PixelVault HQ — Small Feature To-Do](https://www.notion.so/cf63eb7c2524470d9b9a601ab46f4cc6), grouped into quick vs medium vs larger work.

**Baseline (Apr 2026):** `PixelVault.Native.cs` is still the largest single file (**~2.9k lines**). Monolith slice plan target remains **under ~2k lines** for the primary file, via coherent vertical moves—not cosmetic churn.

---

## Tier 1 — Highest ROI for structure (do when touching these areas)

| Priority | Item | Rationale |
|----------|------|-----------|
| **1a** | **`MainWindow.ManualMetadata` (split across ~5 partials)** | **Done (2026-04-04):** Orchestration in **`MainWindow.ManualMetadata.cs`** (~550 lines); **`MainWindow.ManualMetadata.Helpers.cs`**, **`Layout.cs`**, **`SteamSearch.cs`**, **`Finish.cs`** hold dialog layout, Steam match flow, finish/confirm, and shared helpers. |
| **1b** | **`ImportWorkflow` (split partials)** | **Done (2026-04-04):** Core intake/progress orchestration in **`Import/ImportWorkflow.cs`**; **`MainWindow.ImportWorkflow.Progress.cs`** (`RunBackgroundWorkflowWithProgress`); **`MainWindow.ImportWorkflow.Steps.cs`** (rename/move/metadata/delete/sort/undo). Further steps can move into **`IImportService`** when touching behavior. |
| **1c** | **`PixelVault.Native.cs` ctor / composition** | **Done (2026-04-04):** **`MainWindow.IndexServicesWiring.cs`** — static **`CreateIndexFilenameRulesServices`** builds index persistence, filename parser, game-index assignment, and filename rules from one place; ctor assigns via deconstruction (no behavior change). |

## Tier 2 — Performance / polish (profile or user reports)

| Item | Notes |
|------|--------|
| Library virtualization **`SizeChanged`** debounce | Same pattern as search debounce if resize-driven rerenders show up in traces (`CODE_QUALITY_IMPROVEMENT_PLAN.md`). |
| Capture grid “full” virtualization | `PERFORMANCE_TODO.md` — only if N-scaling issues remain after viewport cancel work. |
| Video-heavy folders | Poster-first / stricter off-screen unload if jank returns. |

## Tier 3 — Correctness / hardening (smaller, test-friendly)

| Item | Notes |
|------|--------|
| **`SteamAppIdLooksLikeFilenamePrefix`** | Require separator + length bounds (`CODE_QUALITY_IMPROVEMENT_PLAN.md`, `SteamImportRename.cs`). |
| Steam rename **single doc** | One module-level docblock linking parser, `ImportWorkflow`, and tests. |
| **`BuildLibraryFolderInventoryStamp`** | NAS / huge trees — session cache or off-UI if startup stalls reported. |

## Tier 4 — Larger refactors (only with intent)

| `IndexPersistenceService.cs` (~1.3k lines) | Split by schema domain (e.g. photo vs game vs conventions) when persistence work spikes—not required for MainWindow trim. |
| Nullable / analyzer passes | Per-file or project slice when new extracted files are added (`ROADMAP.md` Phase 4). |

---

## Verification

- **`dotnet test`** on `PixelVault.Native.Tests` after each slice.
- **`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`** when Library, import, manual metadata, or game index editors change.

## Doc hygiene

- When a tier is finished, move bullets to `CHANGELOG.md` / `HANDOFF.md` and trim this file so it stays a **short** horizon (avoid duplicating `PERFORMANCE_TODO.md` long-term).
