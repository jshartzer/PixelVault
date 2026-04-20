# PV-PLN-RVW-001 — Post–app-review hardening & planning refresh

| Field | Value |
|-------|-------|
| **Plan ID** | `PV-PLN-RVW-001` |
| **Status** | **Complete** — Phases **0–3** **done** in repo (**2026-04-12**); Phase 3 = **NEXT_TRIM** Tier 3 numeric ID prefix hardening (`SteamImportRename.cs` + tests) |
| **Owner** | PixelVault / Codex |
| **Source review** | [`docs/APP_REVIEW_2026-04-12.md`](../APP_REVIEW_2026-04-12.md) (build/test health + code inspection; not a hands-on WPF smoke pass) |
| **Related** | [`docs/NEXT_TRIM_PLAN.md`](../NEXT_TRIM_PLAN.md) (**refreshed** 2026-04-12), [`docs/PERFORMANCE_TODO.md`](../PERFORMANCE_TODO.md), [`docs/CODE_QUALITY_IMPROVEMENT_PLAN.md`](../CODE_QUALITY_IMPROVEMENT_PLAN.md), [`docs/DOC_SYNC_POLICY.md`](../DOC_SYNC_POLICY.md) (optional Notion mirror) |

**Topic mnemonic:** `RVW` — **R**e**v**ie**w** (codified follow-up from the dated app review).

**Location:** Canonical **completed** copy under [`docs/completed-projects/`](.) (this file). Redirect stubs: [`docs/plans/PV-PLN-RVW-001-post-app-review-hardening.md`](../plans/PV-PLN-RVW-001-post-app-review-hardening.md), [`docs/plans/open/PV-PLN-RVW-001-post-app-review-hardening.md`](../plans/open/PV-PLN-RVW-001-post-app-review-hardening.md). Plan index: [`docs/plans/README.md`](../plans/README.md).

---

## Purpose

Ship **bounded-risk** improvements called out in the **2026-04-12** app review, plus **planning hygiene** so the next structural bets target **today’s** hotspots—not assumptions from an older release line.

**In scope**

1. **P1** — User-authored filename-rule **regex safety** (compile + runtime bounds).
2. **P2** — Photo workspace **hero/banner download** dedupe, cancellation, or coalescing for rapid selection changes.
3. **P2** — Refresh **`docs/NEXT_TRIM_PLAN.md`** (and plan index) to the **current** version train and real file hotspots — **done in repo** 2026-04-12 (optional **Notion** mirror still per `DOC_SYNC_POLICY.md`).

**Out of scope (unless a follow-up plan says otherwise)**

- Large MainWindow / MVVM rewrites.
- Changing product semantics of filename rules (readable vs raw regex) beyond **safety and validation messaging**.
- Full interactive UX audit (review explicitly excluded hands-on WPF smoke).

---

## Context (from review + engineering notes)

### Finding 1 — Custom regex (P1)

- **`FilenameParserService.BuildRegexPattern`**: when the pattern does not look like the **readable** token grammar, the stored string is passed through as a **raw .NET regex**.
- **`FilenameRulesService.NormalizeRuleForSave`**: validates by **`new Regex(...)`** only—**no** match timeout, **no** `RegexOptions.NonBacktracking`, **no** pathological-pattern tests.
- **Risk:** one catastrophic pattern can stall **imports**, **library scans**, and the **filename convention editor** because parsing reuses these rules broadly.

**Engineering note:** `RegexOptions.NonBacktracking` (.NET 7+) reduces ReDoS-style backtracking blowups for many patterns but is **not universal**—some constructs fail at compile time or behave differently. Plan for **compile-time fallback** (classic regex + **strict** `matchTimeout`) or a **clear save error** when NonBacktracking is incompatible.

### Finding 2 — Hero banner churn (P2)

- **`MainWindow.LibraryBrowserPhotoHero`**: on cache miss, kicks off **`Task.Run`** with **`CancellationToken.None`**; selection is checked when **applying** the bitmap, not when **starting** or **sharing** download work.
- **`CoverService`**: cache purge / rewrite can interact with overlapping downloads for the same title.
- **Risk:** duplicate network work, races on the same cache file, quiet background churn when the user scrubs the photo rail quickly.

**Engineering note:** Prefer **one in-flight download per dedupe key** (e.g. normalized title + banner source identity), **`CancellationToken`** tied to the current selection, and **await existing** in-flight work instead of spawning duplicates.

**Status (repo):** Addressed for the photo hero path — **`CoverService`** coalesced hero downloads, **`MainWindow`** selection **CTS** + resolver **`ThrowIfCancellationRequested`** between steps and around Steam / SteamGridDB **ID resolution** (see Phase 2 completion notes).

### Finding 3 — Stale trim plan (P2)

- **At review time (2026-04-12),** **`docs/NEXT_TRIM_PLAN.md`** still framed work as **post–0.854** and cited **`PixelVault.Native.cs` ~2.9k lines** as the primary monolith target—stale vs the **`0.075.xxx`** train and extracted tree.
- **Remediation:** **`docs/NEXT_TRIM_PLAN.md`** was **fully refreshed** the same day (measured baseline table, current publish pointer, links to this plan and the app review). **Phase 0** steps **0.1–0.3** are **complete in-repo**; optional **Notion** mirror remains if your workflow uses it.

---

## Phased execution

### Phase 0 — Planning doc refresh (docs-only)

**Goal:** `NEXT_TRIM_PLAN.md` is a **decision tool** for the current train, not a snapshot of an old milestone.

**Repo status:** Steps **0.1–0.3** satisfied **2026-04-12** (see **Execution log**).

| Step | Action | Done when |
|------|--------|-----------|
| 0.1 | Replace stale **version / line-count** claims with **current** facts (verify with local `cloc` or IDE line count when editing). | Doc reads true for `0.075.xxx` (or whatever is current when edited). |
| 0.2 | Re-rank **Tier 1** targets using **today’s** largest/most-churned modules (review suggests `IndexPersistenceService`, `FilenameConventionEditorWindow`, `MainWindow.LibraryBrowserRender.DetailPane`—confirm with metrics). | Table lists real hotspots; obsolete “shrink Native.cs to 2k” framing removed or marked **complete/historical**. |
| 0.3 | Cross-link this plan from `NEXT_TRIM_PLAN` (one paragraph) and add **`PV-PLN-RVW-001`** row to [`docs/plans/README.md`](../plans/README.md). | Links resolve; README index updated. |

**Optional:** Notion mirror per [`docs/DOC_SYNC_POLICY.md`](../DOC_SYNC_POLICY.md).

---

### Phase 1 — Regex hardening (P1)

**Goal:** User-authored custom patterns cannot **hang** the app; save path and runtime path are **bounded**.

| Step | Action | Done when |
|------|--------|-----------|
| 1.1 | **Inventory** every code path that compiles or runs **user-stored** `FilenameConventionRule.Pattern` / `PatternText` (not only built-ins). | Short internal note or comment block listing call sites. |
| 1.2 | **Save-time (`FilenameRulesService`)** | Invalid or dangerous patterns **fail fast** with a clear user message. |
|  | — Max **pattern length** (bytes or chars); max **alternation** / repetition heuristics if cheap to implement. | |
|  | — Compile with **`RegexOptions.NonBacktracking`** when supported; on failure, **either** reject with guidance **or** compile with classic options + **mandatory** `matchTimeout`. | |
|  | — **`Regex(..., matchTimeout: …)`** (tune: e.g. tens–hundreds of ms for save validation). | |
|  | — Optional: run **one** `Match` on a bounded dummy input after compile to catch obvious stalls at save time. | |
| 1.3 | **Runtime (`FilenameParserService` / matcher)** | Same timeout + engine strategy for **custom** rules; **cache** compiled regex per rule id to avoid per-file allocation storms. | |
| 1.4 | **Tests** (`FilenameRulesServiceTests`, parser tests) | Pathological patterns **timeout or are rejected**; built-in / readable-token rules **unchanged** in behavior. |

**Acceptance criteria**

- No known path to **unbounded** `Regex.Match` on user pattern without timeout.
- Regression suite green; add explicit tests for ReDoS-style snippets where feasible.

**Implementation inventory (call sites — Step 1.1)**

- **`FilenameRulesService.NormalizeRuleForSave`** — save-time compile + probe (`FilenameParserService.ValidateConventionPatternForSave`).
- **`FilenameParserService.GetRegex` / `Parse`** — per-rule `Match` on filenames; cached `Regex` uses **match timeout** + **NonBacktracking** with fallback; **`RegexMatchTimeoutException`** skips rule.
- **`FilenameConventionBuilder.HydrateDraftWithActualFileName`** — one `Match` for guided builder hydration; uses same bounded `Regex` factory as runtime.
- **Editor** — `FilenameConventionEditorWindow` shows **`BuildRegexPattern`** preview text only (no full-file `Match`).

**Phase 1 completion (repo)**

- Raw-pattern **alternation cap** (`MaxFilenameConventionRawAlternationBars` + save rejection).
- **`ValidateConventionPatternForSave`** XML remarks list all execution surfaces.
- Tests: malformed regex, max length, alternation cap, nested-quantifier save smoke.

---

### Phase 2 — Hero / banner dedupe (P2)

**Goal:** At most **one** meaningful in-flight banner resolution per dedupe key; stale selection does not keep downloading.

| Step | Action | Done when |
|------|--------|-----------|
| 2.1 | Introduce **in-flight map** or equivalent in **`CoverService`** (or a dedicated small helper) keyed by stable banner identity (e.g. normalized title + source kind). | Rapid re-selection does not spawn parallel downloads for the same key. |
| 2.2 | Thread **`CancellationToken`** from Photo workspace selection into **`ResolveLibraryHeroBannerWithDownloadAsync`** (replace `None` where appropriate). | Cancelled work stops quickly; no dispatcher apply after cancellation. |
| 2.3 | **Coalesce:** if a download for key K is already running, **await** it instead of starting a second. | Logs / network show deduped behavior under manual stress. |

**Repo status (complete):** **`CoverService.TryDownloadSteamGridDbHeroAsync`** / **`TryDownloadSteamStoreHeaderHeroAsync`** coalesce concurrent HTTP for the same title+id key via **`HeroDownloadCoalesce.RunAsync`** (shared inner task uses **`CancellationToken.None`**; callers use **`WaitAsync`** with their token). **`MainWindow`**: **`ResolveLibraryHeroBannerWithDownloadAsync`** with **`ThrowIfCancellationRequested`** at start and between GridDB vs Steam fallback; **`TryDownloadSteamGridDbHeroAsync`** / **`TryDownloadSteamStoreHeaderHeroAsync`** throw after ID resolution and before hero HTTP **`WaitAsync`**. **`ResolveBestLibraryFolderSteamGridDbIdAsync`** / **`ResolveBestLibraryFolderSteamAppIdAsync`** and **`CoverService.TryResolveSteamAppIdAsync`** gain extra cancellation checkpoints before/after network hops. **`LibraryBrowserRefreshPhotoWorkspaceHeroBanner`**: per-selection **`CancellationTokenSource`**. **Fetch Banner Art**: **4-minute** **`CancellationTokenSource`**. Tests: **`HeroDownloadCoalesceTests`**.

**Acceptance criteria**

- Manual: fast scrub on photo rail → **no duplicate** banner fetches for the same title in a short window (verify via log or debugger).
- Automated tests where a seam exists; otherwise document manual check in `MANUAL_GOLDEN_PATH_CHECKLIST` if touched.

---

### Phase 3 — Correctness slice (**NEXT_TRIM** Tier 3) — **complete**

**Goal (as executed):** Land **one** small, test-backed hardening item from refreshed **`docs/NEXT_TRIM_PLAN.md`** rather than a broad MainWindow extraction.

**Choice:** **`SteamAppIdLooksLikeFilenamePrefix`** (`Services/Intake/SteamImportRename.cs`) — align with **`CODE_QUALITY_IMPROVEMENT_PLAN.md`** backlog: avoid treating arbitrary digit prefixes as Steam AppIDs when the capture shape is wrong.

**Shipped (repo):**

- **Minimum digit length:** Prefix token must be **at least two** digits (rejects single-digit prefix path for `TryBuildSteamRenameBase` while **canonical-title** branch still handles e.g. `My Game_screenshot_1` with a one-digit `appId`). **No upper bound** — the same helper serves long numeric **NonSteamId** keys (see `ImportServiceManualMetadataTests`).
- **Explicit separator:** When `baseName.Length > appId.Length`, the next character must be **`_`** or **`-`** (not merely “non-digit”), so stems like `2561580.…` or `730x…` do not match.
- **Module `<remarks>`** on `SteamImportRename` cross-linking tests, **`NEXT_TRIM`**, **`CODE_QUALITY`**, and this plan.
- **Tests:** `SteamRenamePathMappingTests` — extended assertions for `-` boundary, letter/dot rejection, single-digit AppID rejection.

**Rationale vs UI-001:** Tier 1 structural work stays under **PV-PLN-UI-001**; RVW-001 Phase 3 intentionally stayed **narrow** and **import-correctness** focused.

---

## Verification (repo)

Minimum bar after code phases:

```powershell
dotnet build C:\Codex\src\PixelVault.Native\PixelVault.Native.csproj -c Release
dotnet test C:\Codex\tests\PixelVault.Native.Tests\PixelVault.Native.Tests.csproj -c Release
dotnet test C:\Codex\tests\PixelVault.LibraryAssets.Tests\PixelVault.LibraryAssets.Tests.csproj -c Release
```

Add **targeted** tests for Phase 1; Phase 2 includes **`HeroDownloadCoalesceTests`** plus manual **`MANUAL_GOLDEN_PATH_CHECKLIST.md`** item **11**.

---

## Execution log

| Date | Phase | Notes |
|------|--------|--------|
| 2026-04-12 | **0 (repo complete)** | **`docs/NEXT_TRIM_PLAN.md`** rewritten: **`0.075.010`** pointer, measured line-count baseline, tiers updated, RVW/FNRU links. **`PERFORMANCE_MONOLITH_SLICE_PLAN.md`** monolith goal line updated. **`FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`** + **FNRU** execution log aligned to **0.075.010** / **`f5d69ff`** (re-record SHA after your next commit if these docs ship in that commit). **`APP_REVIEW_2026-04-12.md`** follow-up + Finding 3 **status** note. **Notion** mirror: update manually if required by `DOC_SYNC_POLICY.md`. |
| 2026-04-12 | **1 (started)** | Plan moved to **`docs/plans/open/`**; **Phase 1 (partial):** `FilenameParserService` — `CreateConventionRegex` (NonBacktracking → classic + **match timeout**), **`ValidateConventionPatternForSave`** (length cap, smoke + raw near-miss probe), **`GetRegex`** cache uses bounded regex; **`Parse`** skips rule on **`RegexMatchTimeoutException`**. **`FilenameRulesService.NormalizeRuleForSave`** calls validation. **`FilenameConventionBuilder.HydrateDraftWithActualFileName`** uses bounded match. Tests: malformed regex, overlong pattern, nested-quantifier save smoke. |
| 2026-04-12 | **1 (complete)** | Raw-pattern **`|`** cap (**`MaxFilenameConventionRawAlternationBars`**), **`ValidateConventionPatternForSave`** XML inventory, tests **`SaveRules_RejectsRawPatternWithTooManyAlternations`** + **`ValidateConventionPatternForSave_RejectsExcessiveRawAlternation`**. |
| 2026-04-12 | **2 (complete)** | **`MainWindow`**: per-selection **`CancellationTokenSource`** for photo hero refresh; **Fetch Banner Art** 4-minute timeout. **`CoverService`**: **`HeroDownloadCoalesce`**. **`ThrowIfCancellationRequested`** on **`ResolveLibraryHeroBannerWithDownloadAsync`** / hero **`TryDownload*`** between ID resolution and coalesced HTTP wait; Steam ID resolution checkpoints. Tests **`HeroDownloadCoalesceTests`**. **`MANUAL_GOLDEN_PATH_CHECKLIST.md`** item **11**. |
| 2026-04-12 | **3 (complete)** | **`SteamAppIdLooksLikeFilenamePrefix`**: `_`/`-` separator + **min 2** digits (no max — NonSteamId); **`SteamImportRename`** module remarks; **`SteamRenamePathMappingTests`** edge cases. **`NEXT_TRIM_PLAN.md`** Tier 3 row + **`CODE_QUALITY_IMPROVEMENT_PLAN.md`** backlog updated. Plan moved to **`docs/completed-projects/`**. |

---

## Risks & mitigations

| Risk | Mitigation |
|------|------------|
| NonBacktracking rejects valid user patterns | Save-time message + fallback path (timeout + classic) or document unsupported constructs. |
| MatchTimeout too aggressive on slow machines | Tunable constant or settings default; start conservative on **save**, slightly looser on **batch** if needed. |
| Hero dedupe breaks cache refresh | Integration test or manual “purge + refetch” path after `TryRunPendingLibraryHeroBannerCacheRefresh`. |

---

## Relationship to other plans

| Plan | Relationship |
|------|----------------|
| **PV-PLN-FNRU-001** | Filename rules **verification** and guided-builder direction; RVW-001 **hardens** the same surface area for **safety**, not UX redesign. |
| **PV-PLN-UI-001** | Structural MainWindow thin-out; RVW-001 **does not** replace UI-001—**RVW-001 is complete**; pick the next UI slice from **updated** trim data under UI-001. |
| **PV-PLN-AINT-001** | Independent; no dependency unless shared Cover/regex code conflicts in merge order. |

---

## Summary

| Priority | Track | Outcome |
|----------|--------|---------|
| P2 | Phase 0 | Accurate **next trim** doc → better sequencing. |
| P1 | Phase 1 | **Regex safety** → lower denial-of-self risk from rules. |
| P2 | Phase 2 | **Banner dedupe + cancel** → less background churn in Photo workspace (**done** in repo). |
| P3 | Phase 3 | **Steam AppID prefix** hardening (`SteamImportRename`) + doc cross-links — **done** in repo. |

This plan **incorporates** the review verbatim priorities and adds **implementation constraints** (NonBacktracking caveats, cancellation, README/Notion hygiene) so execution stays bounded and testable.
