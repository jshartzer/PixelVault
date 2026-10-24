# Tracker dashboard and source indicators

October 24 reader capability update: memory-chip restoration now supports native source setup and automatic capability indicators. The user-authorized source is connected and SaveChanges enabled (22/56 restored). Dashboard navigation remains read-only; opening it does not read saves or change opt-ins. Unsupported custom/game trackers still show reader unavailable. See [Chip reader evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md).

Implemented for App Testing on October 23, 2026. Review executable: `C:\Codex\App Testing\Achievement Trackers\PixelVault.exe`. Published version remains 0.077.000.

## Navigation

- The Library cover-view toolbar has **Trackers** centered in the space between **Sort / filter** and **All**, with equal clear gaps. It uses the existing toolbar chrome and horizontal overflow behavior. It is hidden in the narrow Photo workspace rail along with the cover-view grouping buttons.
- Trackers opens a searchable, modeless dashboard of games with trackers. Compact covers are 132 × 192 px and reuse the Achievement Hub's existing local artwork path and asynchronous tile loader. Games without artwork receive the existing loader's fallback or a named placeholder. The dashboard has one instance per owner and reloads saved summaries on activation.
- Clicking a game opens its tracker list, with saved progress, optional achievement title/link, a progress bar and source/automation status for each tracker. Clicking a tracker opens that exact definition in the existing tracker form. Its selector includes every tracker for the same provider/game, including trackers linked to other achievements and game-wide trackers.
- Dashboard, game list and individual tracker are modeless browsing windows owned by the outer application window, rather than one another. Earlier pickers remain closable while later windows are open; closing a dashboard/game list does not close or cancel the tracker. Standalone browsing windows have no intermediate owner. Closing the outer application still closes its owned windows. The game list reloads saved summaries when reactivated.
- The shared **Group by category** checkbox is visible when a tracker has category metadata and starts checked, preserving existing grouping. It can switch to a flat list while retaining selection and unsaved note/count drafts. In premium orders, the categories are delivery types: Condition, Quantity, Time and Miscellaneous. Headers show completed/total active records for the full category and a separate capped category goal when configured. LL and LLL together qualify an order once. Search/status/category filters do not shrink the subtotal denominator. Uncategorized records use Other; trackers with no categories hide the option. This view choice lasts for the open window and does not edit definitions, progress or source settings.
- Game cards use smaller 12.5 px tracker-count text and show an overall completion percentage. This is completed active tracker goals divided by all active goals, including partial trackers; it retains category-goal caps and excludes archived items. Unconfirmed goals are not counted as completed. No connected account or no active goals shows completion unavailable rather than a fabricated percentage. The value displays up to one decimal place.
- The Achievement Hub's global Trackers button opens the same dashboard. Per-game tracker entry points use the same game list. Achievement context actions use the linked flow.
- Achievement details have a visible **Open tracker** button below the header when one tracker is linked. Multiple links open a matching list rather than choosing arbitrarily. With no link, **Create linked tracker…** opens the universal editor with its optional link prefilled. Cached achievement names remain available in creation/import.
- Dashboard/game screens retain New checklist plus spreadsheet and portable definition-JSON import, including an empty dashboard. Definition imports reuse the existing preview, game-scope validation, explicit independent-copy requirement and unknown initial progress.

## Games, accounts and progress

Library games appear only when a matching provider/game definition exists. Custom definitions whose library folder is no longer available remain reachable with fallback metadata/artwork. Unrelated bundled templates do not create phantom dashboard games outside the current library. Provider identities remain separate even when numeric game IDs or titles coincide.

Summaries use only the current session's account. A disconnected account has an explicit connect prompt. Archived items are omitted; category-goal trackers count each qualifying record once and cap contributions at the existing goals. Manual progress, notes, source observations, links and achievements retain their existing ownership. Browsing the dashboard does not read game saves, apply pending imports, start source refreshes or change automation settings. Individual tracker open/refresh retains the existing opt-in scheduling and reconciliation behavior.

## Source indicators

The dashboard cover cards summarize game-file connections, manual trackers and automatic refresh state. Each tracker list entry and individual tracker form displays the detailed status:

| Status | Meaning |
| --- | --- |
| Game files connected | A saved connection uses a registered, supported native game-file adapter |
| Game files available · Not connected | A native reader supports this definition, but this account has not connected it |
| Spreadsheet connected / Progress file connected | Refresh rereads that external source; it does not directly read game files |
| Manual · Game-file reader unavailable | No validated native reader supports this tracker |
| Automatic available · Off | The connected adapter supports automatic change detection, with automation still opt-in |
| Automatic on / paused / review needed | The saved scheduling mode and review barrier determine the current state |

Source descriptors come from the adapter registry through `ITrackerGameFileSource`; forms do not contain game-specific source mappings. A new game adapter can supply support and coverage metadata without adding a new tracker form. A connection badge describes the configured adapter/capability, not proof of a successful new read; current read failures and last-success details remain in the existing source status/history.

Death Stranding fabrication, construction, maximum upgrades and premium-order LL/LLL support native refresh. The order reader was validated separately and its source/automation setup was user-authorized; see [Order reader evidence](DEATH_STRANDING_ORDER_RESEARCH.md). Memory-chip restoration now supports native refresh and generic capability indicators after separate validation; see [Chip reader evidence](DEATH_STRANDING_MEMORY_CHIP_RESEARCH.md). Facility connections remain research. Browsing/grouping itself does not alter saved progress or source bindings.

## Verification

October 23 browsing/grouping follow-up: six new cases cover real modeless navigation with and without an outer owner, closing earlier pickers while editing the later tracker, outer-window lifetime, grouping on/off, selection/draft retention, all four delivery categories, unique-record subtotals, capped goals, filtered totals, generic second-game counts/milestones and uncategorized hiding. All 42 focused dashboard/order/catalogue/browsing cases pass. Full native suite: **1,129 passed**, zero failed/skipped (`tracker-browsing-full.trx`). Updated grouped layouts inspected at 732 and 1,172 px. Review executable updated without a version bump, app shutdown or live progress/source writes. Earlier test figures below are historical.

Twelve new cases cover library/custom/orphan scope, provider/account isolation, category goals and archives, source/automation distinctions, equal toolbar spacing at two widths, dashboard → game → exact tracker navigation, same-game switching, single/multiple achievement links, account-free JSON import and cancellation, cached achievement choices, and the native/manual split. Normal/narrow dashboard/game/detail layouts were rendered and inspected, including real cached cover art. General OS scaling/high-contrast and broader live interactive QA remain open. Test run results are recorded in HANDOFF.md.

Final full native suite: 1,107 passed, zero failed/skipped (`tracker-dashboard-full.trx`). All 12 dashboard cases pass (`tracker-dashboard.trx`). Earlier isolated cache-file/timer failures were absent in the final full run.
