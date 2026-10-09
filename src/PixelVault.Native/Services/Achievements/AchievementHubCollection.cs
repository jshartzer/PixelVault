using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PixelVaultNative
{
    enum AchievementHubStatusFilter { All, Earned, Unearned }
    enum AchievementHubRarityFilter { All, Common, Bronze, Silver, Gold, UberRare, Unavailable }
    enum AchievementHubSort { RarestFirst, MostCommonFirst, NewestEarned, OldestEarned, Game, Name }

    sealed class AchievementHubEntry
    {
        public AchievementHubGame Game;
        public GameAchievementsFetchService.AchievementRow Row;
        /// <summary>Provider order within the game; the final deterministic tie-breaker.</summary>
        public int Order;
    }

    sealed class AchievementHubQuery
    {
        public string Search = "";
        public AchievementHubStatusFilter Status;
        public string GameKey;
        public AchievementHubRarityFilter Rarity;
        public AchievementHubSort Sort;
    }

    /// <summary>Saved progress across library games of connected accounts, independent of any filter.</summary>
    sealed class AchievementHubCollection
    {
        public List<AchievementHubEntry> Entries = new();
        /// <summary>Games with at least one achievement, ordered by title, for the game filter.</summary>
        public List<AchievementHubGame> Games = new();
        public int LibraryGames, GamesWithProgress, GamesWithoutAchievements, Earned, Unearned;
        public string Signature = "";
        /// <summary>When each covered game's saved progress was last updated, by game key.</summary>
        public Dictionary<string, long> SnapshotUpdated = new(StringComparer.Ordinal);
        public int GamesWithoutProgress => LibraryGames - GamesWithProgress;

        internal static AchievementHubCollection Build(IEnumerable<AchievementHubGame> games,
            IReadOnlyDictionary<string, AchievementHubStore.AccountData> data)
        {
            var collection = new AchievementHubCollection();
            long stamp = 0;
            foreach (var game in games.GroupBy(g => g.Key).Select(g => g.First()))
            {
                if (!data.TryGetValue(game.Provider, out var account)) continue;
                collection.LibraryGames++;
                if (!account.Games.TryGetValue(game.Id, out var snapshot) || snapshot?.Rows == null) continue;
                collection.GamesWithProgress++;
                collection.SnapshotUpdated[game.Key] = snapshot.UpdatedUtc;
                unchecked { stamp += snapshot.UpdatedUtc + snapshot.RarityCheckedUtc; }
                var rows = snapshot.Rows.Where(r => r != null && r.Provider == game.Provider && r.ProviderGameId == game.Id)
                    .GroupBy(r => r.ProviderAchievementId, StringComparer.Ordinal).Select(r => r.First()).ToList();
                if (rows.Count == 0) { collection.GamesWithoutAchievements++; continue; }
                collection.Games.Add(game);
                for (var i = 0; i < rows.Count; i++)
                {
                    collection.Entries.Add(new AchievementHubEntry { Game = game, Row = rows[i], Order = i });
                    if (rows[i].ProgressKnown && rows[i].Unlocked) collection.Earned++;
                    else if (rows[i].ProgressKnown) collection.Unearned++;
                }
            }
            collection.Games = collection.Games.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            collection.Signature = string.Join(",", collection.LibraryGames, collection.GamesWithProgress, collection.Entries.Count, collection.Earned, stamp);
            return collection;
        }

        internal static AchievementHubRarityFilter RarityOf(GameAchievementsFetchService.AchievementRow row)
        {
            if (!AchievementCollectionStats.ValidPercent(row?.GlobalUnlockPercent)) return AchievementHubRarityFilter.Unavailable;
            return AchievementRarityUi.Tier(row.GlobalUnlockPercent) switch
            {
                AchievementRarity.UberRare => AchievementHubRarityFilter.UberRare,
                AchievementRarity.Gold => AchievementHubRarityFilter.Gold,
                AchievementRarity.Silver => AchievementHubRarityFilter.Silver,
                AchievementRarity.Bronze => AchievementHubRarityFilter.Bronze,
                _ => AchievementHubRarityFilter.Common
            };
        }

        internal List<AchievementHubEntry> Query(AchievementHubQuery query, long nowUtcTicks)
        {
            query ??= new AchievementHubQuery();
            var search = (query.Search ?? "").Trim();
            bool Contains(string value) => !string.IsNullOrEmpty(value)
                && CultureInfo.CurrentCulture.CompareInfo.IndexOf(value, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
            var matches = Entries.Where(e =>
                (search.Length == 0 || Contains(e.Row.Title) || Contains(e.Row.Description) || Contains(e.Game.Title))
                && (query.Status == AchievementHubStatusFilter.All
                    || (query.Status == AchievementHubStatusFilter.Earned) == (e.Row.ProgressKnown && e.Row.Unlocked)
                        && e.Row.ProgressKnown)
                && (string.IsNullOrEmpty(query.GameKey) || e.Game.Key == query.GameKey)
                && (query.Rarity == AchievementHubRarityFilter.All || RarityOf(e.Row) == query.Rarity));

            bool Dated(AchievementHubEntry e) => AchievementCollectionStats.ValidUnlock(e.Row, nowUtcTicks);
            bool HasPercent(AchievementHubEntry e) => AchievementCollectionStats.ValidPercent(e.Row.GlobalUnlockPercent);
            IOrderedEnumerable<AchievementHubEntry> ordered = query.Sort switch
            {
                AchievementHubSort.RarestFirst => matches.OrderBy(e => !HasPercent(e)).ThenBy(e => HasPercent(e) ? e.Row.GlobalUnlockPercent.Value : 0),
                AchievementHubSort.MostCommonFirst => matches.OrderBy(e => !HasPercent(e)).ThenByDescending(e => HasPercent(e) ? e.Row.GlobalUnlockPercent.Value : 0),
                AchievementHubSort.NewestEarned => matches.OrderBy(e => !Dated(e)).ThenByDescending(e => Dated(e) ? e.Row.UnlockUtcTicks : 0),
                AchievementHubSort.OldestEarned => matches.OrderBy(e => !Dated(e)).ThenBy(e => Dated(e) ? e.Row.UnlockUtcTicks : 0),
                AchievementHubSort.Name => matches.OrderBy(e => string.IsNullOrWhiteSpace(e.Row.Title))
                    .ThenBy(e => e.Row.Title ?? "", StringComparer.CurrentCultureIgnoreCase),
                AchievementHubSort.Game => matches.OrderBy(e => e.Game.Title ?? "", StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(e => e.Game.Key, StringComparer.Ordinal),
                _ => matches.OrderBy(e => 0)
            };
            // Every sort except Game ties on the achievement, not the game, so the page does not clump by title.
            return ordered.ThenBy(e => string.IsNullOrWhiteSpace(e.Row.Title))
                .ThenBy(e => e.Row.Title ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Row.ProviderAchievementId ?? "", StringComparer.Ordinal)
                .ThenBy(e => e.Game.Key, StringComparer.Ordinal).ThenBy(e => e.Order).ToList();
        }
    }
}
