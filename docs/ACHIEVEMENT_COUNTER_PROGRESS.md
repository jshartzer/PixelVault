# Achievement counter progress

Implemented October 4, 2026 for the existing per-game Achievements window.

## Display and sorting

- **List:** show a numeric current/target label and thin bar when a reliable counter is available.
- **Grid:** show the same label/bar only under **Counter progress: highest first** or **Counter progress: lowest first**. Other grid sorts retain the compact cards.
- Sort by percentage through the defined range, not raw count, within the existing Base Game/DLC groups. Missing/unsupported counters sort last in both directions; ties retain input order.
- Use `(current - minimum) / (target - minimum)`, clamped to 0–100% for the bar. Preserve the actual numeric count even when it exceeds the target. Nonzero minimums are explained in the counter tooltip.
- Counter values never unlock achievements or change known earned/locked status. A 100% counter with a locked achievement remains locked. Unknown achievement status never becomes a fabricated zero counter.

## Sources and limits

Steam's documented [GetUserStatsForGame Web API](https://partner.steamgames.com/doc/webapi/isteamuserstats?l=english) supplies player stat values. Steam supports associating achievements with [progress stats](https://partner.steamgames.com/doc/features/achievements?l=english), but the public achievement schema is insufficient to provide every stat-to-achievement mapping.

PixelVault reads **definition metadata only** from the installed Steam client's `appcache/stats/UserGameStatsSchema_<appid>.bin`. The Steam installation is discovered through `HKCU/Software/Valve/Steam/SteamPath`, with the standard Program Files (x86) Steam folder as fallback. No game saves or local player-stat caches are read or modified.

Only explicit `progress.value.operation = statvalue` mappings with a single named operand and finite, valid minimum/maximum bounds are supported. Complex expressions, duplicate mappings, unsupported cache formats, missing cache files, and malformed/truncated data yield no inferred counter. This local cache format is a compatibility dependency, not a guaranteed public Web API contract.

The selected SteamID64 is used for the Web API request and validated against the returned account identity. Missing, negative, nonnumeric, nonfinite, or duplicate stats are unavailable. Only exact stat and achievement names are matched. No text-based guesses or manually inferred targets are used.

The optional stats request runs alongside the existing foreground achievement requests, only when mappings and an account exist. Notification-only polling does not add stat queries. HTTP errors, privacy restrictions, invalid JSON, and timeouts leave normal achievements available without counters. Each new successful fetch rebuilds counter availability; there is no cross-account counter cache. Counters reflect the last values the game sent to Steam, which may lag in-game activity.

## Implementation and verification

- `Services/Achievements/SteamAchievementCounters.cs`: bounded, read-only binary definition parsing, account-aware API stat parsing, and attaching validated values.
- `GameAchievementsFetchService.cs`: optional concurrent API retrieval and plain nullable counter fields.
- `AchievementCollectionStats.cs`: validation, range normalization, and stable progress sorting.
- `AchievementsInfoWindow.Stats.cs` and `AchievementsInfoWindow.cs`: shared label/bar and requested list/grid visibility rules.
- `AchievementCounterTests.cs`: string/int/float definitions, wrong-game protection, all truncation points, malformed/unsupported definitions, duplicates, invalid bounds, missing/private/wrong-account stats, zero/fractional/over-target counts, stale clearing, sorting, and List/Grid visibility.

All **143 achievement tests passed**. A live read-only check of Death Stranding Director's Cut (1850570) found 13 supported definition mappings and five mapped values returned by the configured account's API response. Other sampled games lacked usable values or mappings; these correctly remain unavailable. WPF fixture renders checked list bars, compact default grid, and progress-sorted grid. Actual OS high contrast/scaling changes remain manual checks.

This is a user-requested follow-up to the existing achievement page. It does not mark Achievement Hub Step 2 complete. Run the App Testing build script after each delivered step, as requested by the user.

The test-build script completed successfully for this change at `C:\Codex\App Testing Rarity\PixelVault.exe`, preserving the running App Testing session. Published version remains `0.077.000`.
