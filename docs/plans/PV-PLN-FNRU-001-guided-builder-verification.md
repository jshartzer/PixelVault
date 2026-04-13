# PV-PLN-FNRU-001 — Guided builder verification (manual + regression)

| Field | Value |
|-------|--------|
| **Plan ID** | `PV-PLN-FNRU-001` |
| **Status** | In progress — Stage 0 complete; Stage 1+ manual pending |
| **Owner** | PixelVault / Codex |
| **Parent context** | Renaming Rules **Guided Builder** — behavior and test cases live in [`docs/FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`](../FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md) |
| **Related** | [`docs/FILENAME_RULES_FORM_SPEC.md`](../FILENAME_RULES_FORM_SPEC.md), [`docs/FILENAME_PARSING_ARCHITECTURE.txt`](../FILENAME_PARSING_ARCHITECTURE.txt), [`docs/MANUAL_GOLDEN_PATH_CHECKLIST.md`](../MANUAL_GOLDEN_PATH_CHECKLIST.md) (when broader app paths are touched) |

**Topic mnemonic:** `FNRU` — **F**ile**n**ame **Ru**les (aligns with repo `FILENAME_RULES_*` doc family).

---

## Purpose

Turn the **handoff** document into a **repeatable verification program**: same build, same ordered tests, comparable results. This plan does **not** restate full expected behavior — it defines **execution order**, **exit criteria**, and **what to do when something fails**.

**Non-goals (verification track):** redesigning the builder UX; large refactors of `FilenameRulesService` / `FilenameConventionBuilder` unless a test exposes a defect. **Follow-on work** (below) may add UX and persistence deliberately, after verification gates are green.

---

## Strategy — “Remember” filename shape + platform (product direction)

This aligns the filename-rules experience with the goal: **analyze a basename, capture date/time (and other roles), and reuse that behavior on similar files with the right console / platform context.**

### Primary path: Guided Builder → **saved rule** = the memory

- **Saving a custom rule from the Guided Builder** already persists a **readable pattern**, **timestamp format**, **platform tags**, and optional **app ID** groups — the same structure `FilenameParserService` uses when matching.
- **Recommendation:** treat **save** as the main user story: *“Remember this naming style for this platform.”* Messaging in the Renaming Rules UI should reinforce that (clear rule names, e.g. “Phone export”, “Xbox Game Bar”).
- **Why:** one pipeline (builder → rules → parser), testable, no duplicate inference engine, no silent wrong guesses.

### Optional path: sample + platform → **same outcome as a rule**

- For users who resist editing patterns: **pick a recent or staged sample**, set **platform** (and optionally game / convention context), and opt into **“use this shape for similar filenames.”**
- **Implementation constraint:** that flow should still **materialize a normal `FilenameConventionRule`** (or equivalent storage that compiles to the same matcher), reusing **builder segment / timestamp logic** — not a loose “fingerprint” table that can false-positive across devices.

### Parser ordering (when learned / auto-created rules exist)

1. **Explicit user rules** (current behavior — priority ordered).
2. **Conservative “learned” or auto-created rules** at a **fixed priority band** below hand-tuned rules but **above** ad hoc heuristics, so mistakes can be overridden by a higher-priority custom rule.
3. **Existing heuristics** (Xbox trailing timestamp, Steam shortcuts, generic date, game-index title hints, etc.).

### Deferred / avoid (until the above is stable)

- **Silent auto-learn** on every unmatched import (platform guesses will often be wrong and hard to undo).
- **Shape-only matching** without a real readable/regex pattern aligned with `FilenameParserService` — hard to test and to explain.

### Sequencing vs this plan

| Track | When |
|-------|------|
| **Stages 0–5** below | **Verification** — prove current builder + rules behave per handoff. |
| **Stage 6** | **Product follow-through** — copy/messaging, then optional “remember shape” + priority rules; schema/parser only as needed to support auto-created rules cleanly. |

---

## Preconditions

1. Read **§ Actual Form / Asset Names** and **Related documentation** in the handoff (correct window vs partial class names, links to form spec and parsing architecture).
2. Build or install a **known** PixelVault binary (local Debug/Release or published build).
3. Fill the **regression anchor** in the handoff (`App version / build`, `Date tested`) **before** running tests so logs and screenshots stay attributable.

---

## Execution log

| Date | Stage | Notes |
|------|--------|--------|
| 2026-04-09 | **0** — Regression anchor | Handoff anchor: **0.075.008**, git **`6309851`**, dist `C:\Codex\dist\PixelVault-0.075.008\PixelVault.exe` |
| 2026-04-12 | **Docs alignment** | Handoff + trim plan: **0.075.011**, git **`f1a0c3e`**, dist `C:\Codex\dist\PixelVault-0.075.011\PixelVault.exe`; see [`FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`](../FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md) |
| 2026-04-09 | **Automation baseline** | `dotnet test` `PixelVault.Native.Tests` — **`FilenameRulesServiceTests`**: 4 passed |

**Next:** **Stage 1** — manual Tests **A → D** (gates **G1–G3**) in the Renaming Rules window using the anchored build.

---

## Staged execution

### Stage 0 — Regression anchor

| Step | Action | Done when |
|------|--------|-----------|
| 0.1 | Record app version, commit SHA or CI id, and date in [`FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md`](../FILENAME_RULES_GUIDED_BUILDER_TEST_HANDOFF.md) § Regression anchor | **Done** (2026-04-09) |

### Stage 1 — Core flow (blocking)

Run **Tests A → D** in order from the handoff **Suggested Manual Test Checklist**. These cover staging, simple title/timestamp, Steam ID, and Non-Steam ID shapes.

| Gate | Tests | Pass = |
|------|-------|--------|
| **G1** | A, B | Staging works; builder hydrates; save/reopen retains rule |
| **G2** | C | Steam-style triple is mapped end-to-end |
| **G3** | D | Long numeric prefix maps to Non-Steam ID, not Steam ID |

If **G1** fails, treat as **P0** (builder not wired); prefer debugging notes in handoff § Notes For Debugging (`FilenameRulesService`, `FilenameConventionBuilder`, `FilenameConventionEditorWindow`).

### Stage 2 — Edge cases (blocking before “done”)

| Step | Tests | Notes |
|------|-------|--------|
| 2.1 | **E** — Advanced fallback | Confirms no silent degradation for regex-heavy rules |
| 2.2 | **F** — Built-in customization | Clone/override vs mutating shipped defaults |

### Stage 3 — Multi-sample and hints (explicit outcome)

| Step | Test | Pass options |
|------|------|----------------|
| 3.1 | **G** — Cross-sample hint | **(a)** Hint behaves as documented, or **(b)** absence is **explicit** (document in handoff or issue: “not implemented”) — not ambiguous silence |

Cross-check **§3** and **“Signs working / broken”** bullets in the handoff for multi-sample coherence.

### Stage 4 — Accessibility (recommended)

| Step | Scope | Reference |
|------|--------|-----------|
| 4.1 | Tab order, focus trap, keyboard role changes, names for assistive tech | Handoff **§10** |

Record **pass / gap / not tested** in the regression anchor area or a short note below it (avoid duplicating §10 text).

### Stage 5 — Closure

| Step | Action |
|------|--------|
| 5.1 | If all gates pass, mark verification **complete** in the **Execution log** and in [`README.md`](README.md) (Status → Completed for the verification track); optionally add one line to [`docs/CHANGELOG.md`](../CHANGELOG.md) if verification shipped with a release |
| 5.2 | If gaps remain (Test G, a11y), file tracked follow-ups or link existing issues; keep handoff as the **behavior spec** |

### Stage 6 — Product follow-through (after Stage 5)

Implements the **Strategy** section above; do not block Stage 5 on this.

| Step | Action | Done when |
|------|--------|-----------|
| 6.1 | **Copy / education** — In Renaming Rules (and docs links as needed), state that **saving a rule from the Guided Builder** is how PixelVault **remembers** a capture source’s filename shape + platform | Reviewed in UI |
| 6.2 | **Optional “remember shape”** — Design + implement sample-driven creation of a **normal custom rule** (reuse `ApplyBuilderDraft` / builder draft from selection), plus clear **default priority** in the conservative band | Spec + PR |
| 6.3 | **Parser / persistence** — Only if 6.2 needs it: ensure auto-created rules load with correct ordering; avoid parallel match tables | Tests + manual parse check on two basenames of same shape |

**Status field:** keep **In progress** until Stage 5 is done; after that, either **Completed (verification)** with Stage 6 as tracked follow-ups, or extend status to **In progress — Stage 6** if actively building 6.x.

---

## Verification matrix (quick reference)

| ID | Focus |
|----|--------|
| A | Stage from disk (`Add From File...`) |
| B | Title + timestamp + extension |
| C | Steam App ID + timestamp + counter |
| D | Non-Steam long ID |
| E | Advanced-only rules |
| F | Built-in clone/customize |
| G | Multiple staged samples + cross-sample hint |

---

## Relationship to other plans

- Does **not** replace [`PV-PLN-UI-001`](PV-PLN-UI-001-ui-thin-mainwindow-ios-aligned.md) — if guided-builder work touches `MainWindow` extraction boundaries, follow that plan’s slice/testing discipline.
- **Stage 6** UI work may overlap **PV-PLN-UI-001** (thin shell / editor host); prefer small vertical slices and tests per that plan.
- Optional: after Library/import changes, re-run relevant rows from [`MANUAL_GOLDEN_PATH_CHECKLIST.md`](../MANUAL_GOLDEN_PATH_CHECKLIST.md).
