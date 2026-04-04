# Next trim and optimization plan (post–0.854)

**Purpose:** Rank follow-up work after publishing **0.854**. Aligns with `PERFORMANCE_TODO.md`, `PERFORMANCE_MONOLITH_SLICE_PLAN.md`, `CODE_QUALITY_IMPROVEMENT_PLAN.md`, and `ARCHITECTURE_REFACTOR_PLAN.md`.

**Notion (Project Wiki):** [Next trim and optimization plan (post-0.854)](https://www.notion.so/33873adc59b68131ae12f82c97363684) — mirror of this doc; update when tiers complete (`DOC_SYNC_POLICY.md`).

**Baseline (Apr 2026):** `PixelVault.Native.cs` is still the largest single file (**~2.9k lines**). Monolith slice plan target remains **under ~2k lines** for the primary file, via coherent vertical moves—not cosmetic churn.

---

## Tier 1 — Highest ROI for structure (do when touching these areas)

| Priority | Item | Rationale |
|----------|------|-----------|
| **1a** | **`MainWindow.ManualMetadata.cs` (~994 lines)** | Largest UI partial after the primary file; splitting by concern (e.g. dialog builders vs grid commands vs Steam UI) reduces merge pain and clarifies thread boundaries. **Started (2026-04-04):** helpers in **`MainWindow.ManualMetadata.Helpers.cs`** (badges, shared field getters, multi-preview stack, console apply). **Next:** Steam search click/async block and/or finish-button flow in separate partials. |
| **1b** | **`ImportWorkflow.cs` (~710 lines)** | Natural home for more steps under **`IImportService`** or small workflow types; lowers conflict risk with `PixelVault.Native.cs` per `SERVICE_OWNERSHIP_AND_PARALLEL_WORK_MAP.md`. |
| **1c** | **`PixelVault.Native.cs` ctor / composition** | Filename-convention **`FilenameParserService`** deps still wire many **`indexPersistenceService`** lambdas in one block; consider a **`MainWindow.IndexServicesWiring.cs`** partial or a tiny bootstrap helper so the primary file’s top half is thinner (no behavior change). |

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
