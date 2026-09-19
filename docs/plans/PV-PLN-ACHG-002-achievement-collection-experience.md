# PV-PLN-ACHG-002 — Achievement Collection Experience

| Field | Value |
|-------|-------|
| **Plan ID** | `PV-PLN-ACHG-002` |
| **Status** | Active — Phases A–E implemented; Phase F manual verification remains open |
| **Created** | 2026-09-06 |
| **Owner** | PixelVault / Codex |
| **Topic mnemonic** | `ACHG` — Achievement Guides and Collection |
| **Purpose** | Make achievement progress easier to access, understand, and act on. |
| **Related** | [Achievement Guide Library — ACHG-001](PV-PLN-ACHG-001-achievement-guide-library.md), [Game Profile Dashboard — GPRO-001](PV-PLN-GPRO-001-game-profile-dashboard.md), [Pre-V1 Polish — V1POL-001](PV-PLN-V1POL-001-pre-v1-polish-program.md), [Achievement Groups](../ACHIEVEMENT_GROUPS.md), [Guide Imports](../ACHIEVEMENT_GUIDE_IMPORT.md), [Documentation Sync Policy](../DOC_SYNC_POLICY.md) |

## Purpose and scope

The Achievements window should help the player answer: **What have I collected, what remains, and what should I work on next?** The Guide window should make completion instructions immediately readable and keep editing available when needed.

This plan captures the accepted access, statistics, and visual improvements. Start with **Remaining Only**, then **read-first guides**, then **Next Up pins**. Follow with statistics and visual polish in small, reviewable increments. Implementation started on 2026-09-06 at the user's request; it complements the broader product roadmap.

Notion tracking: [Roadmap initiative](https://www.notion.so/3d373adc59b681cdb2bddc883381e1f8), [Backlog execution](https://www.notion.so/3d473adc59b6816bac79dbbe4698f2de).

## Existing baseline

These capabilities already exist in the current source and are starting points, not new deliverables:

- Grid is the default view, with List available alongside it.
- Base Game/DLC groups have independent counts and percentages and collapsible headers.
- Grid separates Not obtained and Obtained within each group.
- Double-clicking an achievement opens its matching guide by stable provider ID.
- Guides store completion instructions, attribution, tags, and missable metadata; search and a Locked filter already exist.
- The Achievements footer has a refresh-symbol button. Refresh preserves the selected view and collapsed groups, blocks overlapping requests, and retains previous results on provider failure.
- Instructional helper notes have been removed from the Guide page.
- Recent verification: 643 existing tests passed, a refresh behavior smoke check passed, and the App Testing build was updated. This is baseline evidence only, not validation of this plan's future work.

## Experience decisions

| Area | Intended behavior |
|------|-------------------|
| Remaining Only | A compact toggle beside Grid/List filters known locked achievements in both views. Group percentages still describe the full group. |
| Direct access | Clicking a game's remaining count in Game Profile opens Achievements with Remaining Only enabled. |
| Next Up | Players can pin up to three unfinished achievements per provider/game. Pinned targets appear in a compact strip above the collection and open their exact guide. |
| Guide reading | Opening a guide shows readable completion text first. Edit reveals the existing fields with explicit Save and Cancel. |
| Refresh access | F5 invokes the same refresh as the icon. A quiet timestamp communicates the last successful update. |
| Overview | One compact summary shows earned, total, remaining, and completion percentage. |
| Statistics | Show the latest unlock/date, the rarest earned achievement, and progress over time when the provider supplies the necessary data. |
| Sorting | Offer provider/default order, rarity, and unlock date with predictable handling of missing values. Rarity ordering is also useful for remaining achievements. |
| Planning flags | Present existing tags such as online, missable, collectible, and grind as readable chips. |
| Visual feedback | Use restrained earned accents, readable locked artwork, group progress bars, and a brief highlight for newly observed unlocks after refresh. |

## Behavior and data rules

| Policy ID | Rule |
|-----------|------|
| **PV-POL-ACHG-REMAIN-001** | Unknown progress is not locked progress. Remaining totals and completion percentages must not manufacture a zero or a locked state when player data is unavailable. |
| **PV-POL-ACHG-GROUPSTATS-001** | Filtering and sorting change visible rows, not the full-game or full-group percentage denominator. A group with no visible matches must distinguish that state from having no achievements. |
| **PV-POL-ACHG-PINS-001** | Pins are durable local planning data keyed by provider, game ID, and achievement ID. Provider refresh and guide imports cannot overwrite them. |
| **PV-POL-ACHG-READ-001** | Read mode preserves all authored guide content and attribution. Editing has Save/Cancel and protects unsaved work during selection, filtering, imports, and closing. |
| **PV-POL-ACHG-STATS-001** | Statistics use the current player's provider data. Missing dates or rarity remain unavailable; first observation is never substituted for an unlock date. Rarity is labeled as global unlock percentage, not difficulty. |
| **PV-POL-ACHG-REFRESH-001** | Icon and F5 share a single refresh path. Failed refreshes do not advance the successful-update timestamp or generate unlock celebrations. |
| **PV-POL-ACHG-MOTION-001** | Unlock feedback is brief, nonblocking, and respects reduced-motion settings. Initial load and account changes do not celebrate historical unlocks as newly earned. |

Pins are scoped to the local PixelVault user's provider/game collection in the first version. If the configured achievement account changes, keep the chosen targets but re-evaluate progress against the new account and clear the comparison baseline used for unlock feedback. Do not mix accounts in statistics.

When a pinned target becomes earned, remove it from the active Next Up strip and release its slot; this must not alter guide content. A provider failure or temporarily missing definition must not erase a pin. Keep retired definitions recoverable in stored data.

## Phased execution

### Phase A — Remaining Only and direct access

- [x] Add the Remaining Only toggle to the Achievements footer, usable with Grid and List.
- [x] Keep view, filter, and collapsed-group choices through refresh and guide round trips within the open window.
- [x] Add a Game Profile remaining-count action that opens the same window with the filter enabled.
- [x] Give fully completed collections a clear completion state and unavailable progress an explicit unavailable state.
- [x] Bind F5 to refresh and show the last successful update time. Uses an absolute local date/time label with seconds and UTC offset in the tooltip; no aging relative label or timer.

**Exit:** a player can go from Game Profile to the unfinished collection in one action and refresh without losing their place. Repeated refresh commands cannot overlap requests.

### Phase B — Read-first guides

- [x] Make completion instructions the default view, with comfortable line spacing and paragraph wrapping.
- [x] Keep the achievement artwork, official description, progress, and group context visible.
- [x] Show compact tag chips and a source link in read mode; keep an obvious Edit action.
- [x] Reveal guide text, source fields, tags, missable, and group controls in edit mode with Save/Cancel.
- [x] Preserve dirty-state handling, exact-achievement navigation, Ctrl+F search, imports, and source opening. Existing import preview/confirmation path retained; interactive import QA remains in Phase F.
- [x] Use a concise empty-guide state with an Add Guide action.

**Exit:** opening an existing guide requires no editing interaction to read it. Cancel restores saved content, Save persists changes, and navigating away cannot silently discard edits.

### Phase C — Next Up targets

- [x] Add pin/unpin actions to achievement tiles, list entries, and the selected guide.
- [x] Display up to three pinned targets in a compact Next Up strip, with exact-guide navigation.
- [x] At the limit, explain that a target must be unpinned before adding another.
- [x] Store pins and their display order in durable local storage under the existing guide-data lifecycle, with an additive migration and backup coverage.
- [x] Persist pins across restart and imports; reconcile completed targets after successful progress refresh.
- [x] Keep the strip independent of the collection's Remaining Only filter and sorting so pinned targets stay easy to find.

**Exit:** targets survive restart and definition sync, open the correct guides, and leave Next Up when earned without affecting other games or authored notes.

### Phase D — Progress statistics and sorting

- [x] Consolidate the main summary into earned/total, remaining, and percentage.
- [x] Add the latest known unlock with its date and the rarest earned achievement with its global unlock percentage.
- [x] Add a compact cumulative earned-over-time chart derived from valid provider unlock timestamps, with a text alternative.
- [x] Label incomplete historical coverage when earned achievements have missing timestamps. Do not imply the plotted history accounts for all earned achievements in that case.
- [x] Add provider/default, rarity, and unlock-date sorting. Label the direction and put unavailable values last, with stable provider order as a tie-breaker.
- [x] Recompute statistics after refresh from the full current-player result, independently of visible filters.
- [x] Keep unavailable statistics compact for providers or profiles lacking dates or rarity.

**Exit:** a small fixture with known dates, rarity, and mixed progress produces the expected summary, latest unlock, rarest achievement, and chart. Missing data, tied dates/rarity, and account changes behave predictably.

### Phase E — Collection styling and unlock feedback

- [x] Add a restrained border/accent for earned tiles while keeping locked titles and artwork legible.
- [x] Add thin progress bars to Base Game/DLC headers alongside their numeric values. Unknown progress must not look like 0%.
- [x] Use tag chips consistently across the Guide and target surfaces without overcrowding tiles.
- [x] Tighten redundant headings and spacing; reserve prominent accents for selection, targets, and progress.
- [x] Compare successive successful results by stable provider identity and briefly highlight known locked-to-earned transitions after refresh.
- [x] Honor reduced motion and high contrast; retain keyboard focus, accessible names, and usable target sizes.

**Exit:** completed, partially complete, empty, and unavailable collections remain clear at the minimum window size and higher display scaling. Feedback never blocks interaction or triggers on initial load.

### Phase F — Verification and handoff

- [x] Add focused tests for remaining/unknown filtering, full-group totals, sorting, statistics, pin persistence/migration, and refresh transition detection as those behaviors are implemented.
- [ ] Verify Guide read/edit/Save/Cancel/import flows and unsaved-change handling.
- [ ] Manually check Steam and RetroAchievements, provider failure/retry, missing dates/rarity, 100% completion, account changes, and window closure during a fetch.
- [ ] Verify keyboard-only use, reduced motion, long names/tags, and 125–200% scaling.
- [ ] Check a large achievement collection for responsive scrolling, filtering, and refresh.
- [x] Run appropriate tests, build, and `git diff --check`; refresh App Testing for review.
- [ ] Update user-facing documentation and this plan's execution log. Synchronize roadmap/milestone tracking under the documentation policy when execution changes those statuses; register completion when all exit criteria pass.

## Implementation boundaries

- Extend `src/PixelVault.Native/UI/AchievementsInfoWindow.cs` for collection controls and rendering, and `AchievementGuideWindow.cs` for read/edit presentation.
- Reuse `Services/Achievements/GameAchievementsFetchService.cs` for provider retrieval. Keep status live; no game-save or provider-achievement writes.
- Keep pin persistence in the durable guide-data service and plain, testable helpers for filtering/statistics. Do not put authored planning state into the rebuildable library cache.
- Connect Game Profile through the existing library achievement entry points. Inspect current call sites before changing their signatures.
- Avoid new provider integrations, automatic guide generation, completion-time predictions, cross-game dashboards, or background polling in this plan.
- Apply improvements to the existing windows incrementally; retain Grid/List and the current Base Game/DLC organization.

## Risks and mitigations

| Risk | Mitigation |
|------|------------|
| Extra controls crowd the footer or header | Use compact controls and a collapsible details area where needed; verify the minimum window dimensions before expanding the layout. |
| Filters make group completion misleading | Compute all percentages from the complete successful provider result and distinguish filtered row counts. |
| Historical unlock dates are absent or imported together | Report provider dates as supplied, show coverage limitations, and never infer a play-session history. |
| New read mode hides editing or loses notes | Keep Edit discoverable and verify Save/Cancel plus every dirty-state transition. |
| Refresh produces false unlock feedback | Compare only known locked-to-earned states for the same account; reset the baseline after account changes. |
| Pins disappear during imports or provider outages | Store pins separately from imported guide fields and reconcile only against confirmed progress. |

## Definition of done

The plan is complete when Phases A–F pass their exit checks: remaining achievements are directly accessible, guides open for reading, Next Up targets persist, supported statistics are accurate, collection styling is readable and accessible, and refresh updates the experience without losing state or authored data. Missing provider capabilities must have deliberate unavailable states rather than fabricated values.

## Execution log
**Phase E verification (2026-09-07):** Full suite **691 passed**; 11 new checks cover transition states, immutable snapshots, provider/game/account isolation, errors, missing/unknown/conflicting identities, group progress availability, and filtered refresh feedback cleanup. Grid/List, Next Up tags, Guide pin/read/edit, and new-unlock styling were rendered at default/minimum dimensions and 200% pixel density. A 1,000-row WPF fixture rendered Grid in 380 ms, List in 175 ms, and Remaining Only in 132 ms (includes PNG encoding; excludes icon downloads and interactive scrolling). High contrast uses system brushes; the feedback has no motion under any OS setting. Actual OS contrast/scaling, live provider/account interaction, interactive import confirmation, and keyboard-only/manual scrolling still require Phase F verification. App Testing was rebuilt cleanly with no running process stopped; published version remains **0.077.000**.

**Current handoff (2026-09-07):** Resumed at the user's request after Shadow of War curation. Phases A–E are implemented; Phase F manual verification remains. Pins now use a 24-DIP outline/filled vector symbol with hover and focus styling; the Guide pin sits beside the title. Next Up shows one compact tag with all tags available in its tooltip. Group headers have thin progress bars only for complete known progress, and earned List rows match the restrained Grid accent. Refresh highlights only confirmed locked-to-earned transitions for six seconds, with a count visible even when filters hide earned rows. Feedback is static (no animation), does not move focus, clears on a new fetch/close, and compares immutable identity snapshots per account. New/missing/unknown/conflicting identities cannot create feedback. Pin data uses a separate `achievement_pin` table linked to stable definitions; inactive/missing targets retain their saved guides and can be unpinned. Only known unfinished achievements can be newly pinned. Provider failure, unknown progress, and conflicting duplicate status cannot erase pins. A write transaction serializes the three-target limit. Stored-guide fallback never fabricates a provider sync.

Statistics use current result data only. Unlock history is cumulative by UTC day, with explicit date coverage and a selectable text alternative. Global percentages currently come from the existing Steam response; providers without rarity stay unavailable. Sort direction is explicit, missing values are last, and ties retain default order within each group. Steam player-progress failure now preserves an existing successful result/timestamp; initial unavailable progress remains unknown. Account settings are captured for the modal session, so an account change takes effect when reopened and starts with fresh statistics.

**Verification (A–D baseline):** 680 tests pass, including 23 new C–D checks for pin persistence, additive migration, backups, concurrent limits, scope isolation, missing/earned reconciliation, tile/list/guide controls, dirty guide preservation, statistics, sorting, and Steam unknown-progress behavior. Rendered pinned collections and statistics at minimum/default dimensions and 200% pixel density. Full live provider/account, OS scaling, and broader Phase F manual QA remain open.

**Build handoff (2026-09-07):** Clean App Testing rebuild succeeded at `C:\Codex\App Testing\PixelVault.exe` with the published version unchanged at `0.077.000`. The old process had exited; its temporary previous executable from the prior handoff was removed by the normal clean build. No forced shutdown was needed. Repo status and Notion Roadmap/Backlog are synchronized.

**Build handoff (2026-09-06):** `C:\Codex\App Testing\PixelVault.exe` rebuilt successfully with `Build-PixelVault-AppTesting.ps1 -NoClean -NoStopRunningInstance`; SHA-256 matches the separately staged Release executable. Reopen App Testing to load the changes. The running session was preserved by moving its previous executable to `C:\Codex\App Testing\PixelVault.pre-achg-002.exe` before replacement. No forced process exit or published version change. A later normal clean App Testing build can remove that backup once the old process has exited.

| Date | Entry |
|------|-------|
| 2026-09-06 | Plan created from accepted access, statistics, and styling proposals. Registered as ACHG-002, following the completed ACHG-001 foundation. Implementation remains planned; priority order is Remaining Only → read-first guides → Next Up pins → statistics → visual polish. |
| 2026-09-06 | Started implementation and landed Phases A–B in source: Remaining Only, accurate full-group totals and unknown/complete states, Game Profile shortcut, shared F5 refresh, absolute successful-update timestamp, read-first guides, tags/source attribution, explicit Edit/Add/Save/Cancel. Also fixed narrow group headers so unavailable progress cannot squeeze out their names. Full suite: **657 passed** (14 added checks; one pre-existing CS8600 warning). WPF fixtures cover filtering, F5, refresh state/failure/overlap/close, read/edit persistence, dirty search and navigation, canceled close, and empty-guide actions. Rendered Grid/List and Guide read/edit at minimum dimensions and Guide reading at 200% pixel density; actual OS scaling, live provider/account flows, and interactive import checks remain in Phase F. Next implementation milestone: **Phase C — Next Up targets**. Published version remains **0.077.000**. |
