# Achievement scoring

PixelVault points (PV pts) measure provider-reported global rarity. They are separate from RetroAchievements' native RA points.

## Rarity bands (rule version 1)

| Global unlock percentage | PV points |
| --- | ---: |
| Above 75 through 100 | 5 |
| Above 50 through 75 | 10 |
| Above 25 through 50 | 15 |
| Above 10 through 25 | 20 |
| Above 5 through 10 | 25 |
| Above 2 through 5 | 30 |
| Above 1 through 2 | 35 |
| Above 0.1 through 1 | 40 |
| 0.01 through 0.1 | 45 |
| 0 through below 0.01 | 50 |

Unknown, nonfinite, or out-of-range rarity starts at 5 provisional points. Asterisk labels and accessible tooltips explain provisional values. A genuine provider-reported zero is valid and worth 50 points, matching the existing Uber Rare classification.

## Stable awards

- Locked achievements use the current catalog point value.
- Known earned progress creates one award per normalized provider account and achievement. The provider/game/achievement identity is already unique in the catalog.
- Finalized award values never change with rarity or scoring-rule updates. Provisional awards finalize once valid rarity becomes available, including through background rarity backfill.
- Unavailable rarity never overwrites known catalog rarity or finalizes an award speculatively.
- Unknown player progress and anonymous requests cannot create awards. Invalid/failed progress fetches are rejected by the Hub before saving.
- Repeated refreshes, concurrent observations, title/category/guide edits, and relocked achievements cannot duplicate or remove awards.
- Existing saved progress is backfilled lazily on account-store load. Its initial award uses the rarity available during backfill, not a reconstruction of rarity on the original unlock date.

## Database and totals

Durable data lives in `PixelVaultData/guides/pixelvault-guides.sqlite`, outside the library index cache. `achievement_definition` stores `score_points`, `score_rarity_percent`, `score_rule_version`, `score_updated_utc_ticks`, and `score_provisional`. `achievement_score_award` stores account/achievement identity, awarded points, rarity/rule snapshots, provisional status, award/finalization timestamps, and the reported unlock timestamp. The composite primary key and a write transaction make award creation idempotent. Point columns enforce 5–50 in increments of five.

Scoring migrations preserve existing guide text, groups, pins, and definition IDs. Schema additions tolerate simultaneous first use. Score fields in display rows are hydrated from SQLite and excluded from Hub JSON serialization.

Earned totals sum the database awards, including achievements later removed from the provider catalog. Available totals combine those saved awards with the current value of achievements without awards; available totals can change when the catalog or rarity changes. The Hub limits its totals to games in the current library and connected provider accounts and reports missing progress coverage. Removing a game from the library changes that scoped total without deleting its awards. Clearing Hub progress JSON cannot erase saved points.

The game collection, Hub collection and game cards, and achievement detail window display points. Game and Hub tiles show PV points below the title in place of obtained/status text, leaving the icon unobstructed; status and dates remain available in tooltips and accessibility labels. A summary card above Recent unlocks shows total earned PV points, earned achievement count, completion among known progress, and perfect games with every achievement earned across all groups. Sync coverage and provisional award details appear in the point total's tooltip. The summary ignores collection filters, retains the last successful values on refresh failure, and resets on account/library changes.

RetroAchievements currently does not populate global rarity in its parser, so its PV points remain provisional unless valid rarity is supplied later; native RA points remain separately labeled.

The score card also shows **Points this month**, summing saved award values whose reported unlock dates fall between the beginning of the local calendar month and the current time. It uses the timezone offset at month start, including daylight saving changes, and does not use import, award-creation, or rarity-finalization dates. Missing and future unlock dates are excluded. A later confirmed date can fill an undated award while preserving its point value. Monthly totals are read from SQLite, so cache loss and provider catalog removals cannot erase them.

**Rarest earned** selects the lowest valid current global unlock percentage among known unlocked achievements in the scoped collection. It displays the actual provider artwork, achievement name, game, and percentage and opens the achievement details on click. Missing rarity is excluded, ties use stable provider/game/achievement identity, and a missing selection shows an em dash. The feature ignores filters and survives refresh failure; its image is reused while unchanged and safely replaced when the selection changes. Numeric stats use two columns at narrower widths. No explanatory paragraphs are added to the card.

## Verification

`AchievementScoringTests` covers band boundaries, missing/invalid rarity, stable and provisional awards, account/provider/game isolation, concurrent observations, existing-schema migration, legacy snapshot backfill, cache loss, metadata preservation, account-scoped game refresh, accessible card labels, local month boundaries and daylight saving/year transitions, later date backfill, frozen monthly award values, rarest selection, and feature retention/reset. WPF renders of the game and Hub grid/list cards were visually checked at their actual sizes. The summary was also rendered at default/minimum sizes using an actual downloaded Steam achievement icon.
