# Performance, stability, and MainWindow shrink — slice plan

**Status:** Active for **ongoing** monolith shrink and routing glue. Phases **1–3** below are **landed** in source (game-index seam, image pipeline, intake partials + metadata-review partial). **Publish** still follows the normal release flow (`CHANGELOG` / `CURRENT_BUILD.txt`) when shipping behavior changes.  
**Supersedes nothing:** Backlog detail in `PERFORMANCE_TODO.md`; MainWindow extraction history in `completed-projects/`; old Library perf narrative in `docs/archive/PERFORMANCE_FIX_PLAN_HISTORICAL_2026-03.txt`.

## Decisions (locked)

| Topic | Choice |
|--------|--------|
| Phase 1 seam | **`IGameIndexService`** — thin surface; **implementation** depends on **`ILibrarySession`**, **`ILibraryScanner`**, and persistence/index types so active-root behavior stays delegated, not duplicated. |
| First vertical | **1a** — game index **editor** load/save + active-library root alignment. Then **1b** (startup preload / `GetSavedGameIndexRowsForRoot` routing). |
| Publish | **You decide** by when you want to test; no fixed “publish after 1a vs 1a+1b” rule. |

## Goals (priority order)

1. **Stability** — Fewer concurrent mutations of shared caches; clear ownership of mutable state; smaller blast radius when fixing regressions.
2. **Speed** — No new UI-thread blocking on background paths; preserve or extend virtualization, debouncing, and async-first call patterns already documented in `PERFORMANCE_TODO.md`.
3. **Monolith size** — Reduce `PixelVault.Native.cs` (~2.9k lines as of draft) toward a **primary file under ~2k lines**, then toward **~1.5k**, by moving **coherent verticals** into services or feature hosts—not by cosmetic reshuffles.

## Non-goals (for this program)

- Full MVVM, DI container, or UI redesign.
- Repo-wide nullable migration (enable nullable only on new/extracted files if helpful).
- Rewriting Library virtualization or game-first grouping (already landed; validate only if touched).

## Principles

- **Vertical slices:** Each milestone moves a **cohesive** chunk (fields + methods + call sites), not scattered renames across `MainWindow`.
- **Behavior-preserving unless called out:** Same user-visible behavior; document intentional exceptions in `CHANGELOG.md`.
- **Thread boundaries:** New or moved code must not introduce “background thread mutates MainWindow field bags” without an owning type and documented marshaling.
- **Tests + spot-check:** Run `PixelVault.Native.Tests`; use `MANUAL_GOLDEN_PATH_CHECKLIST.md` for Library, game index editor, manual metadata, and import when those areas change.

## Current baseline (reference)

- Large Library perf items (virtualization, search debounce, sort-key caching, instrumentation, cache locking on hot paths, `IFileSystemService`, async-first paths for major workflows) are **landed** per `PERFORMANCE_TODO.md` (summary table there).
- MainWindow extraction **Phases A–F** are **complete**; remaining work is **incremental**: thinner shell, smaller `PixelVault.Native.cs`, clearer services (`ROADMAP.md`, `completed-projects/README.md`).

## Recommended phases

### Phase 1 — Game index and library persistence orchestration (anchor slice)

**Intent:** Finish moving **game index / folder context / index persistence** coordination out of `MainWindow` into **`IGameIndexService`** (see **Decisions** above), per `pixelvault_service_split_plan.txt`.

**Why first:** `PERFORMANCE_TODO.md` item 7 follow-up still calls out direct `indexPersistenceService` / `libraryScanner` use from `MainWindow` for non-active-root paths, indexing partials, filename rules, and editors. Centralizing this improves **stability** (one place owns “which root, which scanner, which store”) and **monolith size** (large glue blocks leave the primary file). It **enables** a second wave of batching and profiling without another Library UI extraction.

**Suggested scope (incremental):**

| Milestone | Scope (example) | Outcome |
|-----------|------------------|--------|
| 1a | Load/save game index editor rows + active-library root alignment | Service (or session) owns `LoadLibraryFoldersCached` / persistence pairing for the paths you touch |
| 1b | Startup preload + `GetSavedGameIndexRowsForRoot` call sites still on direct services | Route through the same seam when root matches session |
| 1c | Editors / filename rules / other-root paths | Narrow only while editing those files; avoid repo-wide churn |

**Acceptance:** No intentional behavior change; tests green; manual smoke on game index editor, Library metadata flow, manual metadata finish path if involved.

---

### Phase 2 — Image load queue and cache ownership

**Intent:** `MainWindow.LibraryImageLoading.cs` already groups much of the LRU/thumbnail path; elevate to a **named owner** (e.g. `ImageLoadQueue` + cache helper) constructed/wired once, with **locks and dispatcher rules** documented in one type.

**Why:** **Stability** (image cache and `QueueImageLoad` are classic race/UI-thread surfaces); **monolith** (drops field groups and methods from `MainWindow`); **speed** (easier to verify no regressions in warm paths).

**Acceptance:** Library browse and detail pane behave as today; troubleshooting logs still useful if issues arise.

**Status (landed):** **`LibraryBitmapLruCache`** (`UI/Library/LibraryBitmapLruCache.cs`) owns the thread-safe LRU; **`LibraryImageLoadCoordinator`** owns normal/priority **`SemaphoreSlim`** limits; **`LibraryThumbnailPipeline`** (`UI/Library/LibraryThumbnailPipeline.cs`) owns decode-width normalization, frozen bitmap / **`BitmapImage`** load, disk thumbnail cache read/write, and video poster path resolution. **`MainWindow.LibraryImageLoading.cs`** keeps **`QueueImageLoad`** and dispatcher marshaling; **`LoadImageSource`** and related helpers forward to the pipeline.

---

### Phase 3 — Intake / import-adjacent glue still in `PixelVault.Native.cs`

**Intent:** Move remaining intake orchestration into `Import/` or `IImportService` dependencies **only when a file is already being edited**—same pattern as `ImportWorkflow` and prior import slices.

**Why:** **Monolith** line reduction without a big-bang import rewrite; **stability** by extending existing service boundaries.

**Status (landed):** Intake preview orchestration is in **`UI/Intake/MainWindow.IntakePreview.cs`**. **`ShowMetadataReviewWindow`** is in **`UI/Intake/MainWindow.MetadataReview.cs`**. Library art decode-width helpers (**`CalculateLibrary*DecodeWidth`**) live with image load in **`MainWindow.LibraryImageLoading.cs`**. Further **`PixelVault.Native.cs`** shrink is opportunistic when touching related areas.

---

### Phase 4 — Optional small wins (as needed)

- Virtualization **`SizeChanged` debounce** (from `PixelVaultData/TODO.md` / code-quality plan) if resize churn shows up in traces.
- Revisit **`PERFORMANCE_TODO.md`** “Suggested Order” after Phase 1–2 to mark any new follow-ups.

## Measuring success

| Metric | Target |
|--------|--------|
| `PixelVault.Native.cs` line count | Step down each phase; primary file trending toward **<2k** then **<1.5k** |
| Regressions | None reported for Library, import, game index, settings |
| Threading | No new empty `catch`; no new unguarded shared caches |
| Docs | Update `PERFORMANCE_TODO.md` item 7 status, `HANDOFF.md` “current focus”, and `CHANGELOG.md` on publish |

## Doc hygiene

- Keep **`PixelVaultData/TODO.md`** aligned with **`PERFORMANCE_TODO.md`** / **`HANDOFF.md`** (avoid duplicating completed perf items as “current focus”).
- Align **Notion** Roadmap / Project Wiki when milestones complete, per `DOC_SYNC_POLICY.md`.

## Next step

Phases **1–3** are **done in source**. Prefer **Phase 4** optional items (virtualization `SizeChanged` debounce, etc.) only if traces justify them; otherwise continue **`PERFORMANCE_TODO.md` item 7** and monolith shrink when editing orchestration or non-active-root persistence call sites.
