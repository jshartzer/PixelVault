# PixelVault Roadmap

Strategic roadmap for the live native WPF app under `C:\Codex\src\PixelVault.Native`.

This document is the long-term planning layer.

Documentation sync rule:

- keep roadmap phase status aligned with the matching Notion roadmap entries
- use `C:\Codex\docs\DOC_SYNC_POLICY.md` when release, milestone, or workflow changes affect both sides

Use it for:

- sequencing major improvements
- understanding why work is ordered the way it is
- deciding what to do next when multiple good options exist
- defining what “good enough to move on” looks like for each phase

Do not use this file as a scratchpad for one-off ideas.

For smaller rolling tasks and opportunistic cleanup, use:

- `C:\Codex\PixelVaultData\TODO.md`

Source input for this roadmap:

- `C:\Codex\.cursor\CURSOR_TODO.md`

## Current Direction

PixelVault already has a strong core shape:

- a clear ingest → metadata → index → browse workflow
- strong project docs and release hygiene
- a growing modular-monolith split instead of one giant source file
- SQLite as the primary runtime index store
- a Library experience that now has real virtualization, lazy loading, and richer clip handling

The next stage is not a redesign.

The highest-value work is:

1. add safety nets around the parts most likely to break during refactors
2. reduce UI-thread blocking and long-operation rough edges
3. keep shrinking orchestration out of `MainWindow`
4. only then push deeper type-system or architectural modernization

## Planning Principles

When choosing work, prefer:

1. safety before ambition
2. extraction of real seams before framework rewrites
3. faster, more reliable day-to-day usage before cosmetic redesign
4. gradual modernization over repo-wide churn

Avoid:

- full MVVM rewrites before service seams exist
- large nullable or dependency-injection migrations in the giant main file
- UI redesign work that lands before responsiveness and maintainability improve

## Phase 1: Safety Net

Goal:

- make future refactors and bug fixes safer by adding a minimal but real verification layer

Why this comes first:

- the app now has enough storage, migration, parsing, and indexing logic that “manual confidence only” will get more expensive every release

Primary outcomes:

- a test project exists in the repo
- pure helpers and storage invariants have focused automated coverage
- a short manual golden-path checklist exists for risky app changes

Candidate scope:

- add `PixelVault.Native.Tests`
- test cache and path naming behavior such as `SafeCacheName`
- test SQLite read/write invariants in the index DB layer
- test legacy migration assumptions and alias behavior
- test critical filename and ID parsing paths
- keep a short manual verification checklist for import + refresh + row persistence

Definition of done:

- at least one test project runs locally
- high-value pure/storage coverage exists for the most fragile helpers
- risky refactors are no longer relying only on ad-hoc manual memory

## Phase 2: UI-Thread Responsiveness

Goal:

- make long operations feel safer and less stall-prone from the WPF surface

Why it comes before deeper architecture work:

- users feel thread stalls immediately
- it also forces cleaner boundaries between UI work and background work

Primary outcomes:

- blocking network and long-running workflows are audited
- long operations follow a consistent background-work pattern
- cancel behavior is clearer where it matters

Candidate scope:

- audit all `TimeoutWebClient` call sites
- ensure cover fetches, refreshes, scans, and similar work do not accidentally run on the UI thread
- standardize background execution plus dispatcher marshaling
- prefer `Task.Run` plus explicit cancellation for new/touched code
- align progress and cancel behavior across scans, imports, and cover flows

Definition of done:

- known blocking call sites are either moved off the UI thread or explicitly justified
- long-running workflows show consistent progress behavior
- cancellation exists where users would reasonably expect it

## Phase 3: Shrink MainWindow

Goal:

- keep `MainWindow` as an orchestration shell instead of the long-term home for every workflow and data concern

Why this is the core maintainability phase:

- this is the biggest structural limiter on future velocity
- better seams here make tests, nullable migration, and future UI improvements easier

**Execution roadmap (phased slices, line-count map, ordering):**

- `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md` — **Phases A–D complete**; **E1–E3** (library browser partial, `LibraryWorkspaceContext`, virtualization seam); **F1–F2** (settings shell + path dialog partial, photography / Steam picker partial); **E1 orchestration depth** — Library open/show wiring in **`MainWindow.LibraryBrowserShowOrchestration`** (Apr 2026). **`PixelVault.Native.cs`** line count shrinks incrementally with each slice; treat **~2.5k** orchestration target as stretch only. **Next:** new plan post Phase 3 exit (see roadmap maintenance).

Primary outcomes:

- more responsibilities move into dedicated services or extracted workflow types
- dependencies become more explicit
- the WPF layer becomes thinner in touched areas

Candidate scope:

- extract index database and path-oriented helpers into a dedicated service
- extract Steam and SteamGridDB cover workflows into a service
- keep pushing import orchestration into `Import/` or dedicated types
- isolate non-UI business logic so new work does not flow back into `MainWindow`
- follow **MAINWINDOW_EXTRACTION_ROADMAP.md** phases B–F for concrete UI and host extractions from `PixelVault.Native.cs`

Definition of done:

- new nontrivial workflow logic is no longer added directly to `MainWindow`
- at least one major responsibility area has been pulled into a dedicated type with clearer boundaries
- future changes in storage/import/network flows can be made with less WPF coupling

## Phase 4: Nullable And Modern C#

Goal:

- improve correctness and maintainability gradually without creating a repo-wide churn event

Why this is not earlier:

- nullable works best after responsibilities are extracted into smaller, clearer types
- doing it too early in the giant code-behind would create noise without enough payoff

Primary outcomes:

- nullable context is enabled for extracted or new code first
- newer C# defaults can be adopted opportunistically where they help

Candidate scope:

- enable nullable in new files or new projects
- migrate extracted services before touching the largest orchestration files
- optionally adopt newer project defaults only in bounded scopes

Definition of done:

- nullable-enabled code exists in meaningful areas of the app
- null handling is getting stricter without destabilizing the main WPF surface

## Phase 5: UX And Polish

Goal:

- make the app feel clearer and more reliable once the underlying seams and responsiveness are in better shape

Why this can overlap:

- some polish can land anytime, but it should not outrank safety, responsiveness, or maintainability

Primary outcomes:

- better user-facing error messaging
- more consistent progress and cancel affordances
- better keyboard and accessibility coverage

Candidate scope:

- replace deep raw exception dumps with “what failed and what to check”
- align progress copy across refresh, rebuild, imports, and cover fetches
- review tab order and keyboard paths for common actions
- do a small high-contrast and readability spot-check pass

Definition of done:

- common failure paths are more understandable
- long operations feel more consistent
- everyday keyboard flows are less awkward

## Deferred Work

These are valid ideas, but they should stay deferred until the earlier phases earn them:

- full MVVM + DI container
- removing WinForms entirely
- major UI redesign

Reason:

- those become much easier and much lower risk after service extraction, tests, and responsiveness work are already in place

## Suggested Execution Order

Recommended next sequence:

1. start Phase 1 with a small test project and a short golden-path checklist
2. use that safety net to audit `TimeoutWebClient` and other long-running call paths in Phase 2
3. extract one real service seam in Phase 3, using `MAINWINDOW_EXTRACTION_ROADMAP.md` (start with Phase B/C before the Library browser split)
4. enable nullable only in the newly extracted or new code from Phase 3 onward
5. keep UX polish incremental instead of batching it into a giant redesign pass

## How To Use This Roadmap

When new ideas appear:

- if the item changes app direction or affects sequencing, update this roadmap
- if the item is a concrete task, cleanup, bug, or quick win, put it in `TODO.md`
- if the item is only a thought and not actionable yet, do not clutter either file until it becomes real
