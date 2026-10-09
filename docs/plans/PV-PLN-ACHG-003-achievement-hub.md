# PV-PLN-ACHG-003 — Achievement Hub

| Field | Value |
|-------|-------|
| **Plan ID** | `PV-PLN-ACHG-003` |
| **Status** | Active — Steps 1–4 implemented and verified; awaiting user review |
| **Created** | 2026-10-04 |
| **Owner** | PixelVault / Codex |
| **Topic mnemonic** | `ACHG` — Achievement Guides and Collection |
| **Purpose** | Bring achievement activity and the library-wide collection together in one hub. |
| **Related** | [Achievement Collection Experience — ACHG-002](PV-PLN-ACHG-002-achievement-collection-experience.md), [Achievement Guide Library — ACHG-001](PV-PLN-ACHG-001-achievement-guide-library.md), [Game Profile Dashboard — GPRO-001](PV-PLN-GPRO-001-game-profile-dashboard.md) |

## Scope and delivery

Notion tracking: [Roadmap initiative](https://www.notion.so/3ef73adc59b6813a88eef3d2d0e23b32), [Backlog execution](https://www.notion.so/3ef73adc59b68196a615e8d47d64c1ca). Steps 1–4 delivered; awaiting user review.

Add an **Achievements** button to the top toolbar of the main library. It opens an Achievement Hub with three sections: a recent-unlocks banner, three recently played game cards, and a searchable, sortable collection across the library. Reuse existing covers, artwork, per-game achievement navigation, and rarity styling.

The user requested planning first and implementation **one step at a time**. On October 4 the user authorized saving this named plan and implementing **Step 1 only**. Review each delivered step before proceeding to the next. This initiative does not close ACHG-002's remaining manual verification or reorder the strategic roadmap.

## Intended experience

From top to bottom:

1. **Recent unlocks:** a compact horizontal timeline with icons, achievement names, game names, and actual earned dates, newest first.
2. **Recently played:** up to three library games, using the existing cover/artwork, earned/total count, and progress bar. Clicking a game opens its existing achievement page.
3. **All achievements:** the current achievement presentation extended across library games. Show game identity on every entry and retain grid/list views.

| Control | Options |
|---------|---------|
| Search | Achievement name, description, or game |
| Status | All, earned, unearned, progress unavailable |
| Game | All games or one selected game |
| Rarity | Common, bronze, silver, gold, uber rare; unavailable remains distinguishable |
| Sort | Rarest first, most common first, newest/oldest earned, game, achievement name |
| View | List or grid |

Rarity uses the established global-unlock-percentage thresholds: common above 10%, bronze above 5% through 10%, silver above 1% through 5%, gold from 0.01% through 1%, and metallic red strictly below 0.01%. Missing rarity keeps the existing neutral appearance. Locked/unknown entries stay muted; confirmed earned entries use the reflective treatment.

## Steps and acceptance criteria

### Step 1 — Toolbar entry and hub window

- [x] Add an accessible **Achievements** button to the main library's top toolbar.
- [x] Open a dedicated, non-modal Achievement Hub using the existing dark visual language.
- [x] Reuse an already open hub for the same library owner; restore it if minimized.
- [x] Lay out Recent unlocks, Recently played, and All achievements with honest coming-soon states. Do not display invented progress, dates, or example games as real data.
- [x] Support Back to Library, Escape, standard window close, and vertical scrolling at smaller sizes.
- [x] Build and verify default/minimum layouts, navigation, duplicate opening, and owner-close behavior.

**Exit:** the button opens the hub reliably, all three sections are readable, and returning to the library retains the browsing session. No provider requests or achievement persistence are introduced in this step.

### Step 2 — Recently played games and shared data

- [x] Add actual last-played retrieval for supported providers, scoped to games in the PixelVault library. Confirm available provider capabilities during this step.
- [x] Add account-scoped saved achievement snapshots for quick reopening and background refresh, sharing successful results with existing per-game retrieval where practical.
- [x] Show up to three recent games using the library's cover/art resolution, including custom art and existing fallbacks.
- [x] Show earned/total counts and progress only when known; open the matching existing achievement page on click.
- [x] Handle zero/fewer than three eligible games, unavailable play history, disconnected accounts, and provider failures.

**Exit:** ordering reflects real play-history data, cards reuse the expected artwork and navigate correctly, and reopening does not wait for a complete network scan.

**Data gap:** the current library stores recent capture/activity timestamps, not actual last-played dates. Do not silently label capture recency as play history. If a capture-based fallback is used, label it **Recent activity** and preserve that distinction. No additional provider integration is committed by this plan.

**Step 2 implementation (2026-10-04):** Uses Steam GetOwnedGames rtime_last_played and RetroAchievements Recently Played LastPlayed, then selects up to three distinct provider/game identities from the full PixelVault folder set, independently of library search/view filters. RA checks pages of 50, stopping after three library matches or 500 recent games, with that coverage displayed. No capture-date fallback is used. Games without achievements remain eligible recent games and explicitly show that they have no achievements.

Shared account snapshots live in **PixelVaultData/achievement-hub/**, with hashed filenames and validated provider/account envelopes. Atomic serialized writes retain full achievement rows, counters, rarity and unlock dates; authored guides/pins are untouched. Successful full per-game/profile fetches also populate the store; lightweight notification-only results cannot overwrite richer snapshots. Unknown/failed progress cannot overwrite successful data. No API keys are persisted.

The hub paints saved results before network refresh, uses a ten-minute freshness interval, performs achievement requests sequentially for only the three cards, and backs off failed requests for two minutes. Manual Refresh bypasses freshness/backoff. Saved timestamps and failure notes distinguish cached data from unavailable progress. Account, credentials, library root, and game-set changes invalidate the current view; closed/superseded loads cannot repaint it. Opening a card uses the existing achievement window with the selected game's exact provider/ID and guide service.

Sources: [Steam IPlayerService](https://partner.steamgames.com/doc/webapi/IPlayerService?language=english), [RetroAchievements Recently Played](https://api-docs.retroachievements.org/v1/get-user-recently-played-games.html). Steam response timestamps verified live; RA behavior checked with documented fixtures.

### Step 3 — Recent-unlocks banner

- [x] Combine dated, earned achievements across library games into a compact horizontal banner.
- [x] Show achievement icon/name, game, and localized earned date; sort newest first.
- [x] Use actual provider unlock timestamps. Notification observation time is not an earned date.
- [x] Exclude undated achievements from chronological placement while preserving them in earned totals/collection.
- [x] Preserve prior successful data on refresh failure and clearly indicate stale/partial coverage.

**Exit:** a mixed-game fixture orders genuine earned dates correctly and handles missing dates, empty history, and failed refreshes without fabricating events.

**Step 3 implementation (2026-10-04):** `AchievementHubService.SelectUnlocks` reads the saved full-row snapshots for every library game of each connected account, not only the three recent cards. Only confirmed earned rows with a provider `UnlockUtcTicks` between the Unix epoch and now are placed (`AchievementCollectionStats.ValidUnlock`); undated or future-dated earned rows are counted and named in the caption instead. Achievements are deduplicated by provider/game/achievement ID and ordered newest first, ties by game then achievement ID; the banner shows the 24 newest. Notification-only results never reach the store, so observation time can't become an earned date.

The banner paints from saved data before any network work and updates with each refresh report. The coverage paragraph (saved-progress counts, oldest snapshot time, Partial note, undated-unlock count) was removed at the user's request. If a whole load fails, the previously loaded timeline stays with a short "Refresh failed" notice; account/library scope changes clear it. Tiles reuse the existing badge download (bounded to the 24 shown), rarity styling, localized date/time, and open the matching game's achievement page. The banner scrolls horizontally (Shift+wheel); the normal wheel scrolls the page.

**Coverage limit:** the timeline only knows games whose progress has been saved: the three recent cards, plus any game whose achievements were opened in the app. A full-library background fetch is deferred to Step 4, which needs bounded, library-wide refresh anyway.

**Step 4 data foundation (2026-10-04, user-selected lowest-cost approach):** the existing background notification check (every library game, configurable interval) now feeds the hub store at no extra network cost. Those lighter results are authoritative for the achievement set, earned status, and unlock dates but skip Steam rarity/counters. They create a snapshot when none exists (`DetailsUpdatedUtc` = 0) or merge into a full one, keeping saved rarity, counters, descriptions, icons, and groups (`AchievementHubStore.Merge`). Unchanged progress is not rewritten (timestamp-only refresh at most every 6 hours), and the parsed account file is cached in memory, invalidated by file time/length. Version 1 files are read as full-detail snapshots. Refresh moved to the hub header beside Back to Library. Live check against a copy of the real store: 189 library achievement games, 107 stored in 40 s, Steam file about 4.4 MB, 20 unchanged repeat saves skipped writes; Death Stranding kept its saved rarity through the merge. The other 82 report Steam player progress unavailable (mostly games without stats or not owned), so coverage will show "Partial" until Step 4 distinguishes "no achievements" from "unavailable". 292 earned achievements from early Valve games have Steam unlock time 0 and are correctly left off the timeline. Remaining Step 4 work: collection UI, filters/sorting, optional Steam rarity backfill.

### Step 4 — Library-wide achievement collection

- [x] Add game-labeled grid/list entries using existing achievement visuals and rarity tiers.
- [x] Add the search, status/game/rarity filters, and sorting described above; put missing sort values last with deterministic ties.
- [x] Keep full-library totals independent of filters and distinguish unknown progress from unearned progress.
- [x] Deduplicate repeated folder representations by stable provider/game/achievement identity while retaining separate provider editions.
- [x] Keep large libraries responsive using bounded image loading and virtualized/paged presentation as appropriate.
- [x] Verify existing per-game navigation and guide behavior when adding collection actions.

**Implemented (2026-10-04):** `AchievementHubCollection` (plain, WPF-free) builds entries from saved snapshots and runs queries; `AchievementHubWindow.Collection.cs` renders them. Search matches title, description, and game (case- and accent-insensitive, 250 ms debounce). Filters: status (all/earned/unearned), game, rarity tier (including "rarity unavailable"). Sorts: rarest, most common, newest/oldest earned, game, name; missing values last. Ties use achievement name and ID, then game key, so rarity/date/name sorts do not clump by game. Sort → Game is the only grouping by title. Totals ignore filters and report games with progress, games without achievements, and games whose progress is unknown. There is no per-achievement "progress unavailable" status filter: the store only keeps known progress, so unknown progress is shown at game level in the totals. Large collections are paged (100 per page, "Show more") rather than virtualized, because the hub content sits in one outer scroll view; badge downloads are cached (up to 1,500) and limited to 8 at a time. Entries open the existing per-game achievement window. Steam games that return a player-stats error with an empty schema are now stored as having no achievements rather than counted as unavailable.

**Steam rarity backfill (2026-10-04, user-requested follow-up):** after each background check, up to 30 saved Steam games with achievements missing rarity get Steam's public global percentages (one request per game, 2 s apart). Only missing percentages are filled; earned status, dates, and existing values are untouched. `Snapshot.RarityCheckedUtc` records the attempt, so games whose rarity Steam doesn't publish are retried at most every 30 days (failed requests retry next check). Games with a full fetch within 30 days are skipped.

**Exit:** a mixed-library fixture can be searched, filtered, and sorted accurately; large collections scroll smoothly and maintain clear game identity.

## Architecture and behavior rules

- `UI/AchievementHubWindow.cs` owns the hub presentation; toolbar construction/wiring stays in the existing library chrome partials.
- Reuse `GameAchievementsFetchService`, `AchievementCollectionStats`, `AchievementRarityUi`, library art resolution, and `LibraryBrowserShowAchievementsInfo` as the later steps require.
- Keep aggregation, sorting, play-history selection, and persistence out of the main window where practical, with plain models that can be tested independently of WPF.
- Snapshot identities include provider, account, game, and achievement. Changing accounts must never show the previous account's progress as current.
- The guide catalog is durable authored data, not a progress cache. Guide imports and hub refreshes cannot erase guides or pins.
- Load saved data first and refresh in the background with bounded concurrency, cancellation, and failure backoff. Show refresh coverage/staleness; avoid silently presenting a partial library as complete.
- Preserve current successful snapshots on network failure. Missing dates, rarity, progress, and unavailable provider capabilities stay explicit.
- Preserve keyboard focus, accessible names, high contrast, and usable layouts at smaller sizes and higher display scaling.

## Verification and handoff

The user explicitly requested running the test build script after **each completed step**. Use the existing review output when preserving a running App Testing session.

October 4 follow-up: [counter progress and sorting](../ACHIEVEMENT_COUNTER_PROGRESS.md) were added to the existing per-game achievement window at the user's request. Step 4 should reuse those validated fields and visibility rules. This follow-up does not complete Hub Step 2.

Run checks appropriate to each step. Step 1 needs a build and a focused WPF layout/navigation smoke check; later data steps need meaningful automated checks for scope isolation, recency, missing values, sorting, and refresh failure handling. Use App Testing without changing the published release version. Record actual verification and remaining limitations below. Synchronize roadmap/backlog status under [Documentation Sync Policy](../DOC_SYNC_POLICY.md).

## Execution log

**Review build:** `C:\Codex\App Testing Rarity\PixelVault.exe` rebuilt successfully for Step 1; the existing `App Testing` process was preserved. Published version remains `0.077.000`.

| Date | Entry |
|------|-------|
| 2026-10-04 | User asked to confirm whether All achievements still groups by game. It had no section headers, but every sort tied by game title, so same-rarity and undated rows clumped. Ties now use achievement name/ID; Sort → Game still groups by title. |
| 2026-10-04 | User asked to remove the Recent unlocks explanatory caption (coverage counts, oldest saved time, Partial note, undated unlocks). The heading and tiles remain; loading, no-account, empty, and refresh-failed notices stay. |
| 2026-10-04 | User-reported hub lag on open and while moving around. Measured against a copy of the real store (98 Steam games, 6,180 rows): each load report re-rendered everything on the UI thread for 0.56–0.89 s (collection page ≈ 300 ms, mostly layout of 100 entries; shelves ≈ 100 ms; unlocks ≈ 60 ms), and opening produces a report per fetched game. Separately, every activation and every report rebuilt the hub session, resolving library cover art from disk for each game. Scrolling itself measured fine (≈ 9 ms per frame). **Fixes:** load reports are coalesced to at most one render per 350 ms, and the final state always renders. The collection keeps rendered entries when everything they show is unchanged (snapshot re-stamps no longer rebuild them) and only rebuilds the game filter when the game list changes. Shelves skip identical renders and reuse unchanged cards, so covers don't reload. The unlocks banner skips identical renders. The session's library game list is cached (count, first/last folder identity, library root, 60 s expiry). A render per report with an unchanged visible page went from 564–893 ms to 17–21 ms. Added card-reuse and collection-keep regression tests. **179 achievement tests pass.** The full suite fails only the 2 known `TradingCardFoilTests` pixel assertions. Review build rebuilt. |
| 2026-10-04 | User-requested achievement details and badge fix. Clicking an achievement in the hub (unlock banner or collection) now opens `AchievementDetailWindow` instead of the game's achievement page. It shows the badge with rarity styling, earned status/date, description, Steam counter progress, global rarity, rank among your earned achievements in the game, game completion, group, saved time, and the guide (tags including Missable, text, source link, updated date). Actions: Add/Edit guide (existing guide editor, focused on the achievement), web search, Add to/Remove from Next Up (catalogues the game first if needed), Pin game to hub, and Open game achievements. Guide reads use a new read-only `LoadGuideEntry`. **Badges:** some Steam icons 404 on the legacy `steamcdn-a`/`cdn.akamai` hosts (newer games); downloads now try `shared.fastly.steamstatic.com/community_assets/...` first with the original as fallback, new fetches save that host, and icon URLs without a file name are treated as missing. A sample of 205 saved badge URLs had 5 failures (2 games plus one empty icon name); the fixed host serves the affected games. **179 achievement tests pass**, including URL candidate/resolve tests and a detail-window test against a real guide database (guide, tags, link, counter, rank, completion, Next Up with and without an existing catalog entry, hub pin). Rendered with real data. Review build rebuilt. |
| 2026-10-04 | User-requested layout follow-up. Recently played shows 10 games (was 3; refresh fetches up to 10 plus pinned games) in a compact single row. A new **Pinned games** section above it uses large covers (220×320 vs 116×169). Both rows hide the scrollbar and show edge arrows on overflow that scroll while hovered, like the library toolbar (click jumps a page). Pins are hub-only (user chose them over library Favorites), saved in `PixelVaultData/achievement-hub-pins.json` by provider game key, in pin order. They are toggled from a pin button on game covers (shown on hover/focus, always visible when pinned) or by right-clicking any hub achievement or game. **173 achievement tests pass**, including 5 new ones: pin persistence, order, invalid keys and corruption; failed saves leave pins unchanged; the 10-game limit and pinned selection; refresh fetching non-recent pinned games; window sizes, arrow visibility, hover scrolling, scroll position kept across re-renders, and pin toggling. Rendered with real covers at 1040 wide. Review build rebuilt. |
| 2026-10-04 | Step 4 committed (`b54c872`). User then requested the Steam rarity backfill. **168 achievement tests pass**, including a backfill test (selection and order, fills only missing/valid percents, earned status untouched, failed requests not stamped, 30-day recheck, merge keeps the stamp). Live check on a copy of the real store: 60 of 64 Steam games needed rarity (3,962 achievements); 10 requests in 2.4 s all succeeded and every achievement in those games got rarity, with earned counts unchanged. Review build rebuilt. |
| 2026-10-04 | User resumed Step 4. Background notification results now merge into the hub store, Refresh moved beside Back to Library, and the library-wide collection (grid/list, search, filters, sorts, filter-independent totals, paging) was added. **167 achievement tests pass**, including 4 collection tests (dedupe and provider editions, filters, sorts with missing values, window paging/filter/view/click-through) and the merge tests. Live check against a copy of the real store: 123 of 189 achievement games stored (16 with no achievements, 66 with progress unavailable), 7,011 entries; build 8 ms, search and sort 14 ms, re-query and page render about 30 ms. Grid and list renders checked at 1040×780 (list rows were centered by the toolbar button template; entries now use their own stretching template). Review build rebuilt at `C:\Codex\App Testing Rarity\PixelVault.exe`; published version remains `0.077.000`. OS scaling/high contrast remain manual checks. All four plan steps are implemented; next: user review. |
| 2026-10-04 | User resumed Step 3. Added the recent-unlocks banner from saved snapshots across all library games, with provider dates only, coverage/partial/stale captions, and retained timeline on failed loads. **158 achievement tests pass**, including 4 new timeline tests: mixed-game ordering with ties, duplicates, undated/future rows, outside-library and disconnected providers; empty history; failed refresh retention including non-recent games; banner click navigation and failure retention. Real saved Steam snapshots (3 of 13 sampled games) produced 23 correctly ordered unlocks; WPF renders at 1040×780 and 680×520 checked. The full suite has 2 failing `TradingCardFoilTests` pixel assertions (shader software rendering), unrelated to these files. The App Testing build couldn't clear the folder while the running instance held files, so the review build is at `C:\Codex\App Testing Rarity\PixelVault.exe`; App Testing's removed `assets`/`tools`/`CHANGELOG.md` were restored from that build. Live RetroAchievements follow-up with the configured account: the real hub load (isolated snapshot folder) matched 3 of 9 library RA games from recent play in 1.6 s and placed 14 dated unlocks newest first, with badge icons loading in the rendered banner; an offline cached reopen painted the same data in 23 ms. This closes the Step 2/3 live RA check. OS scaling/high contrast remain manual checks. Next: review before Step 4. |
| 2026-10-04 | User resumed Step 2. Implemented recent cards, shared account snapshots, cached-first refresh, account-change cancellation, and exact game navigation. **154 achievement tests pass**, including 11 hub tests covering account/provider isolation, corrupted cache recovery, concurrent saves, duplicate identities, private/missing dates, failed refresh retention, no-account states, cancellation, RA dates, card navigation, and account changes during a load. Live Steam returned 298 dated games; isolated end-to-end hub refresh saved three real snapshots in 1.8 seconds, with a cached reopen in 2 ms. WPF layouts rendered with real library covers at wide/narrow widths; unknown progress has no bar. Actual OS scaling/high contrast and live RA account QA remain manual checks. Next: review before Step 3. |
| 2026-10-04 | Registered ACHG-003 and completed Step 1 at the user's request. Added toolbar wiring and a reusable owner-scoped hub window with three coming-soon sections, Back to Library, Escape, and scrollable content. Build passed without warnings/errors; all 130 existing achievement tests passed (test project has an unrelated existing CS8600 warning). WPF smoke passed open/reuse/minimized restore/Back/Escape/reopen/owner-close; default 1040×780 and minimum 680×520 content renders checked, including 2× pixel-density rendering. Actual OS high-contrast and display-scale switching remain manual checks. Steps 2–4 remain pending. |
