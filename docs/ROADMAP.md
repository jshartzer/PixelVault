# PixelVault Roadmap

October 26 — **PC connector 0.15.0** is installed with automatic photo syncing; NAS/web remains **0.14.0**. New indexed captures, assigned photo-group edits and XMP changes settle then publish per game, with persistent verified checkpoints, incremental previews/originals and existing Pause/Resume controls. Polling is every 30 seconds; desktop intake must index new captures first. All 282 live collections/5,837 photos reconciled as current with zero image/original uploads and identical NAS catalogue bytes. All 33 nonvolatile table digests remain unchanged; three ordinary connector/achievement sync tables changed. Pairing/workspace/source bindings and Windows sign-in startup retained. Sixteen connector tests pass, including new arrival/group edits after resume, restart reuse, locked unchanged originals, metadata updates/retention, delta verification and interrupted transfer. Source-only metadata polling of the real library took 619ms with zero original-byte reads. Protected rollback backup: pre-0.15.0-20261027T022417Z. [Operation](COMPANION_SCREENSHOTS.md).

October 26 — Companion **0.14.0** completes the unified photo timeline and full current-library rollout: **5,837 photos across 282 canonical collections**, newest capture first, all six consoles, optional per-game/date groups, date navigation and automatic 60-row continuation. Home View all photos opens the timeline; the banner uses the newest 30 across the library. Offscreen image sources are released, while rows/scroll/focus remain stable. All 94 HDR/JXR captures have SDR previews and unchanged originals; 18 XMP sidecars have their own download. Publication downloaded and hash-verified all metadata/assets/originals; network-disabled NAS audit and saved-data comparison pass. 73 server +10 connector +33 JS checks pass. The installed automatic connector stays 0.8.0; media tool 0.14.0 is manual, with no screenshot watcher installed. [Operation/evidence](COMPANION_SCREENSHOTS.md).

October 26 — Companion Screenshot Library Step 5 complete on NAS **0.13.0**. Recent photos appears above the home game grid: up to 30 saved square thumbnails, horizontal touch/keyboard/arrow scrolling, and direct opening in the shared viewer. Page and strip position survive dismissal; next/previous stay within recent photos. Browse photos opens the enabled game gallery until the full timeline lands in Step 6. The authenticated recent API returns bounded metadata without requiring the PC; the game grid renders independently. 70 server and 32 JS checks pass; phone/landscape/desktop QA has no overflow/runtime errors. All 1376 stable data entries match fresh backup `pre-0.13.0-20261026T205538Z.tar`, NAS/protected-PC SHA256 `65B0C1BC4C7604DE7272399E1E0ED89EFB3B09DAD86931FF732B6ADFA852C43F`. Normal connector remains 0.8.0. [Plan/evidence](plans/PV-PLN-WEB-002-companion-screenshot-library.md).

October 26 — Companion Screenshot Library Step 4 complete on NAS **0.12.0**. Group photos defaults off, remembers the browser choice, and displays existing named/Ungrouped sections independently inside each capture date. Collapse state survives photo navigation, grouping toggles, filtering and pagination within the app. All 233 photos and 18 authored memberships retained. Removed the viewer X; swipe down, Escape and browser Back still dismiss. 30 JS cases and the shell/cache check pass; phone/landscape/desktop QA passes in both densities, with exact viewer-return scroll and no overlaps/overflow/errors. All 1376 stable data entries match fresh backup `pre-0.12.0-20261026T204330Z.tar`, with matching NAS/protected-PC SHA256 `1CCD17C3ADE564FD4137F10D8DB93A1761C1089D273088A24F9CBCB172BCF530`. Normal connector 0.8.0 restored; no extraction or original-file changes. Next: Step 5, bounded home thumbnail strip. [Plan/evidence](plans/PV-PLN-WEB-002-companion-screenshot-library.md).

October26 — Companion Screenshot Library Step3 complete on NAS0.11.0: mixed small/large squares, Comfortable/Compact density saved per browser, balanced edge-to-edge final rows and retained date/source order. Packing reuses thumbnail nodes, handles width changes and keeps gallery/viewport navigation stable.28JS cases cover8000 packing combinations; shell/cache check and desktop/phone/landscape QA pass. Xbox102 unique rows, no overlaps/overflow/runtime errors, and direct-tap return3982→3982 verified. All1376 prior data entries match backup pre-0.11.0-20261026T200813Z.tar; SHA25614D586054045FB400E2AFA1DFF4465A9A083D4EFDD3A51E6E4946EBBB892EC23. Existing connector0.8.0 restored, originals/downloads unchanged. Step4 existing groups is next. [Plan/evidence](plans/PV-PLN-WEB-002-companion-screenshot-library.md).

October26 — Companion Screenshot Library Step2 is live on NAS0.9.1. The233-photo Director's Cut pilot opens a date-based square gallery with native console icons, server-wide console filtering and60-row pages. Full-screen uncropped viewing copies start with info hidden; tap toggles info, swipe down dismisses, horizontal gestures/arrow keys navigate loaded filtered photos, and Close/Escape/Back return to retained gallery state. Late page loads resume safely after photo round trips.68server+26JS tests and desktop/mobile viewport QA pass; direct taps restore exact scroll, no overflow/runtime errors. All1143 prior data entries match. Backup pre-0.9.1-20261026T183520Z.tar, SHA256 C2C3CF82AF86C1648D8DDBD751EB8BAC021756D29AC0C3FEACDD5746992F8CBD, protected PC copy retained. Installed connector remains0.8.0. No new media watcher or broader-game sync. Step3 variable tile layout is next. [Plan](plans/PV-PLN-WEB-002-companion-screenshot-library.md).

October26 — Companion Screenshot Library (PV-PLN-WEB-002): Step1 complete on NAS0.9.0. Read-only desktop export and atomic authenticated NAS media store contain233 Director's Cut screenshots (Steam121/Xbox102/PS5 10),408 distinct JPEG derivatives/94,969,378bytes, exact indexed dates and18 group memberships;7videos excluded. All original selected images unchanged, every uploaded asset and metadata row verified, unchanged retry uploads0. NAS network-disabled read passed while PC connector stopped; public API401. All734 prior entries retained,409 media files added.68server+9connector tests pass. Existing installed connector0.8.0 restored; the one-shot pilot0.9.0 is separate and has no automatic media loop. Screenshots remains Coming soon until Step2 (basic gallery/viewer). [Plan and operational evidence](plans/PV-PLN-WEB-002-companion-screenshot-library.md).

October 24 chip-reader correction: the user's #8 turn-in control is 0x19 → 0x0B → 0x1B, with only that collector record changing. Reader v2 accepts 0x0B as pre-final/incomplete and requires the corroborated final state 0x1B for completion; bit 0x02 alone is insufficient. All 190 identities/bounds/prefix/sentinel/index checks remain, unknown states identify the value/record and preserve progress, and ambiguous tables remain rejected. Actual packaged-worker SaveChanges testing keeps 23/56 before/pre-final and reaches 24/56 only after confirmed terminal submission, with all other chip observations, notes and original copy hashes preserved. A backed-up targeted live automatic-mode refresh reconciled 56 observations to 24/56, cleared failures, and verified every source binding, automatic setting and authored note/manual field unchanged. Backup: PixelVaultData/achievement-tracking/backups/before-chip-prefinal-fix-20261025-002646813.sqlite (UTC filename). Review build: C:\Codex\App Testing\Achievement Trackers\PixelVault.exe, 0.077.000 unchanged; the retained older running app must be reopened using this updated build for continued monitoring. See docs/DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md. Overall Phase E/F stay active.

October 24 map-library extension implemented for review: reusable game-scoped image storage and optional achievement/tracker links, profile Maps/Trackers access, pin editing/filtering and saved-progress colours. Death Stranding starts with three locally extracted high-resolution regional maps and all 56 calibrated chip pins. Existing tracker E/F scope and status are unchanged; portable map packages/additional location adapters are optional follow-ups. See [Saved game maps](GAME_MAPS.md).

October 24 native chip restoration refresh (unreleased): complete installed 190-key collector catalogue (56 chips +134 customization entries), fixed supported prefix, every 12-byte record/sentinel/sequential index and corroborated flags verified in 58 private copied payloads. Ambiguous, unknown, reordered or truncated tables fail without observations. Completion now requires validated final state 0x1B; pre-final 0x0B and other unfinished states do not count; availability stays Unknown. Generic source/worker/form/dashboard/reconciliation/scheduler now support Fount of Knowledge, with stable IDs and no game-specific UI.

User-authorized source connection and SaveChanges opt-in completed after consistent backup before-native-chip-refresh-20261024-151554776.sqlite: full 56 observations applied, 22/56 restored. All authored manual counts/notes, other tracker progress and source/automatic configuration retained. All 69 original checkpoint hashes unchanged. Private-copy actual-database setup and packaged-worker before/pickup/intermediate/restored automatic transitions pass, including no-change history suppression, notes, manual-conflict review and account isolation. A targeted automatic-mode production read with the updated worker succeeds unchanged (0 applied), clears old unsupported-reader failure status and retains source/automatic settings. Close the two older root App Testing windows and open C:\Codex\App Testing\Achievement Trackers\PixelVault.exe to load the new reader for monitoring while the app is open; old sessions were retained. Published version remains 0.077.000.

Verification: 57 focused cases and the 18-case private packaged-worker run pass. Full native suite: 1,146 passed plus one unrelated AchievementPinTests temporary SQLite cleanup lock; the affected case passes on targeted rerun (1/1). Reports: chip-reader-focused.trx, chip-reader-private-controls.trx, chip-reader-full.trx, chip-reader-cleanup-rerun.trx.

Phase E remains In Progress for facility level/cap/catalogue validation and broader compatibility/gameplay/OS QA. Phase F remains In Progress for configurable header rows, creation/source handoff and broader Excel/OS QA. First LL and chip pickup/restoration controls are obtained, and both native readers are implemented. See docs/DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md. Earlier dated status/counts below are historical.

**October 24 — Memory-chip control obtained:** #52 controlled saves distinguish pickup bit0 from final restoration bit1, with other 55 candidates unchanged. One confirmed restoration observation imported after backup; duplicate is a no-op and authored/unrelated state/settings are retained. E remains In Progress for complete indexed-table/header/flag/compatibility validation before native chip refresh, plus facilities. No further first-chip example is pending; no app build/code change. [Evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md).

**October 23 — Tracker browsing and category subtotals:** dashboard/game list/tracker are modeless siblings; earlier pickers can close while later trackers remain editable. Generic Group by category toggle preserves selection/drafts and groups premium orders by delivery type. Header completed/total subtotals count each record once, retain full totals under filtering and show separate capped goals. All 42 focused and 1,129 full tests pass; narrow/wide layouts inspected. Review build updated without version bump/live-state changes. F remains In Progress for remaining import/source/QA work; E facility/chip validation unchanged. [Details](TRACKER_DASHBOARD.md).

**October 23 — Native premium-order refresh:** supported counted table validated across 38 private saves; Growth of a Legend now reads all 540 standard orders/1,080 independent LL/LLL boxes through the isolated worker. Source connected and SaveChanges enabled at user request after backup, initial native refresh and successful production automatic check; latest totals 3 LL/48 LLL, authored/other tracker state retained. Updated review executable required for ongoing monitoring. 28 focused pass; full 1,121 plus two cleanup locks, both passing targeted rerun. E stays In Progress for facility/chip validation; F unchanged. [Details](DEATH_STRANDING_ORDER_RESEARCH.md).

**October 23 evening — LL control received:** user-confirmed orders 238/502/503 calibrate bit 7 independently of LLL; the tracker now has 3 LL/43 LLL positive observations after backup, with notes/manual state retained. Order table/history decoding and restored-chip control remain open; Phase E stays In Progress. Chiral Artist's Studio level-4 candidate matches the user's report; complete facility/cap/catalogue validation precedes native refresh. No application build change. [Orders](DEATH_STRANDING_ORDER_RESEARCH.md), [facilities](DEATH_STRANDING_FACILITY_RESEARCH.md).

**October 23 — Tracker dashboard and source indicators:** Library/Hub cover dashboard, per-game tracker list, exact tracker navigation, same-game switching and direct achievement shortcuts implemented. Generic indicators distinguish native game-file connections, external/manual sources and automation capability/state. New/import workflows remain accessible throughout. Phase F stays In Progress for header rows/source handoff and broader QA; E awaits the LL/chip examples the user will provide later. [Details](TRACKER_DASHBOARD.md).

**October 23 — Shared tracker forms and named completion authoring:** first styling/access pass plus universal manual/spreadsheet setup for 1–16 completion boxes per item implemented. Wide boolean mapping, saved refresh and group-aware Excel roundtrip reuse generic state/reconciliation. All 1,092 tests pass. Phase F remains In Progress for header rows/source handoff and broader QA; later visual passes cover staged imports, field-level reviews, unified history and category collapse. Phase E awaits LL/chip controls. [Details](TRACKER_COMPLETION_COLUMNS.md).

**October 23 — Catalogue reconciliation and compact category groups:** explicit custom add/update/archive/restore plus metadata undo implemented. Shared records are half height and group by category metadata, with arbitrary milestone support and generic game choices. All 185 focused /1,081 full cases pass. F remains In Progress for header/source setup and Excel/OS QA; E awaits LL/chip controls. [Details](TRACKER_CATALOGUE_RECONCILIATION.md).

**October 23 — Foil fix and ACHG-004 definition portability:** the two shader failures are resolved; all 1,063 native tests pass. Definition JSON export/import now preserves counts, groups and optional achievement links, with a shared preview, legacy compatibility and explicit independent copies for existing IDs. All 163 tracker +4 foil cases pass. Phase F remains In Progress for catalogue add/update/archive reconciliation, header-row/source setup and broader QA; E still needs LL/chip controls. [Portability](TRACKER_DEFINITION_PORTABILITY.md), [foil fix](TRADING_CARD_FOIL.md).

**October 22 — ACHG-004 Phase F in progress:** progress-aware XLSX/CSV creation, saved stable-ID/header mappings for Refresh, protected source-note additions, and separate definition/template/account exports are implemented for review. All 150 focused tests pass; full 1,047 +2 known shader failures. Next: explicit catalogue metadata/add/archive reconciliation and portable definition import/group/link roundtrip. Phase E continues pending LL/chip controls. Latest private sync: 44/108 fabricated, 10/13 constructed, 1/10 upgrades, 41 LLL. [Spreadsheet progress](TRACKER_SPREADSHEET_PROGRESS.md), [plan](plans/PV-PLN-ACHG-004-game-progress-trackers.md).

**October 21 — ACHG-004 native structure refresh:** Current progress reconciled to 43/108 fabrication, 10/13 construction, 1/10 upgrades and 40 LLL orders. Native construction/maximum-upgrade reader now validates full counted persistent-fact tables across 24 copied checkpoints and uses shared refresh/history protections. 134 focused tests pass; full 1,031 +2 known shader failures. Phase E stays In Progress for chip restoration/LL validation and compatibility QA; F planned. [World source](DEATH_STRANDING_WORLD_TRACKERS.md).

**October 21 — ACHG-004 premium orders:** Growth of a Legend now has 540 paired LL/LLL rows, unique-order counting and four category targets. The dated private seed supplies 40 calibrated LLL observations; LL remains unknown pending the user's positive-control example. 122 focused tests pass; full 1,019 passes + two known shader failures. Native order refresh remains disabled. Phase E remains In Progress and F planned. [Evidence](DEATH_STRANDING_ORDER_RESEARCH.md).

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

**October 21 — ACHG-004 Phase E in progress:** Manual achievement-linked construction (13), maximum-upgrade (10) and memory-chip restoration (56) catalogues are in App Testing. Stable user save pairs provide a repeat zip-line control and a first Chiral Bridge positive (trophy fact absent → record followed by 1). Full table/absence semantics and upgrade/chip transitions remain unverified; native refresh for these catalogues stays disabled. 117 focused tests pass; full 1,014 pass with the same two known shader failures. [World trackers](DEATH_STRANDING_WORLD_TRACKERS.md), [plan](plans/PV-PLN-ACHG-004-game-progress-trackers.md). **Next within E: persistent-fact parser validation and controlled maximum-upgrade / chip pickup-restoration pairs.**

**October 21 — ACHG-004 Phase D implemented:** Opt-in app-running automatic refresh now supports tracker-open, stable source-change and periodic modes with pause/resume, cancellation, durable results, deduplicated nonmodal notices and bounded backoff. Routine updates preserve manual progress/notes; conflicts, rollbacks and undone batches require explicit review. 106 focused tests pass; full suite 1,003 passes with the same two shader failures. Live automation remains off until opted in. [Automatic refresh](TRACKER_AUTOMATIC_REFRESH.md), [plan](plans/PV-PLN-ACHG-004-game-progress-trackers.md), [Notion](https://app.notion.com/p/40073adc59b6819494bccc9055d72943). **Next: Phase E, Death Stranding structures/upgrades/collectibles.** Live gameplay and broader OS/Excel QA remain open.

**October 21 — ACHG-004 Phase C implemented:** The Death Stranding DC fabrication source now reads current saves through a bundled isolated worker with configurable/discovered paths, account/checksum/catalogue validation and bounded copies. Real read: 108 targets / 35 fabricated; originals unchanged. 83 focused tests pass; full suite 980 passed with the same two known shader failures. [Adapter](DEATH_STRANDING_SAVE_ADAPTER.md), [plan](plans/PV-PLN-ACHG-004-game-progress-trackers.md), [Notion](https://app.notion.com/p/40073adc59b6819494bccc9055d72943). **Next: Phase D, opt-in automatic refresh.** Live in-game fabrication and general OS/Excel review remain open.

**October 21 — ACHG-004 Phase B implemented:** Refresh/reconciliation and history/undo now work through a configured JSON source, with cancellation, explicit conflict/rollback choices and guarded atomic writes. 62 focused tests pass; full suite 959 passes with the same two known rendering failures. [Plan](plans/PV-PLN-ACHG-004-game-progress-trackers.md), [Notion](https://app.notion.com/p/40073adc59b6819494bccc9055d72943). **Next: Phase C, the configurable Death Stranding save adapter.** No direct save reader or automatic refresh is enabled yet. General manual QA remains open.

**October 21 — ACHG-004 draft:** [Game progress trackers](plans/PV-PLN-ACHG-004-game-progress-trackers.md) now has a full phased roadmap. The foundation, optional achievement links and universal XLSX/CSV creation are implemented in App Testing for review. Next: real source Refresh/reconciliation and undo history, then a configurable Death Stranding fabrication adapter, opt-in automation, structures/collectibles, spreadsheet reimport and second-game validation. [Notion roadmap](https://app.notion.com/p/40073adc59b6819494bccc9055d72943). Later phases are planned; this does not reorder strategic phases or close existing achievement QA.

**October 4 update — ACHG-003 Step 4:** the library-wide achievement collection is implemented and verified (167 achievement tests; live 7,011-entry check). All four steps are done; next is user review.

**October 4 update — ACHG-003 Step 3:** the recent-unlocks banner is implemented and verified (158 achievement tests). Next is review before Step 4 (library-wide collection).

**October 4 update — ACHG-003:** Step 2 (recent games/shared snapshots) is now implemented and verified. **154 achievement tests pass**; live Steam and cached reopen checks passed. Next is review before Step 3 (earned timeline). Later paragraphs describing Step 1 reflect the initial kickoff.

**Current requested feature (2026-10-04):** [PV-PLN-ACHG-003 — Achievement Hub](plans/PV-PLN-ACHG-003-achievement-hub.md). Step 1 (toolbar entry and hub shell) implemented and verified. Review before Step 2 (recent games/shared snapshots), Step 3 (earned timeline), and Step 4 (library-wide collection). The user requested one step at a time; this does not reorder the strategic phases below or close ACHG-002's remaining QA.

**Active focused initiative (resumed 2026-09-07 at user request):** [PV-PLN-ACHG-002 — Achievement Collection Experience](plans/PV-PLN-ACHG-002-achievement-collection-experience.md). Phases A–E are implemented, including compact pins and collection styling/unlock feedback. Phase F manual verification remains. [Notion roadmap status](https://www.notion.so/3d373adc59b681cdb2bddc883381e1f8). This does not reorder the strategic phases below.

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

- **MainWindow extraction (Phases A–F): complete (Apr 2026).** Index: `C:\Codex\docs\completed-projects\README.md` · summary: `C:\Codex\docs\completed-projects\MAINWINDOW_EXTRACTION_PHASES_A-F.md` · full technical record: `C:\Codex\docs\MAINWINDOW_EXTRACTION_ROADMAP.md`. **Remaining Phase 3 work** (broader goal: thinner WPF shell, smaller `PixelVault.Native.cs`, clearer services) continues under this phase and other docs—not under the finished A–F extraction checklist.

Primary outcomes:

- more responsibilities move into dedicated services or extracted workflow types
- dependencies become more explicit
- the WPF layer becomes thinner in touched areas

Candidate scope:

- extract index database and path-oriented helpers into a dedicated service
- extract Steam and SteamGridDB cover workflows into a service
- keep pushing import orchestration into `Import/` or dedicated types
- isolate non-UI business logic so new work does not flow back into `MainWindow`
- continue incremental extractions from **`PixelVault.Native.cs`** per **HANDOFF.md**, **PERFORMANCE_MONOLITH_SLICE_PLAN.md**, and **MAINWINDOW_EXTRACTION_ROADMAP.md** (phases A–F complete; further slices are opportunistic)

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
- add a full-library Library `Timeline` browse mode that reuses the current browser seams while shifting the active mode from folder-first to capture-first; see `C:\Codex\docs\LIBRARY_TIMELINE_MODE_PLAN.md`

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
