using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    sealed class AchievementHubGame
    {
        public string Provider, Id, Title, ArtPath;
        public LibraryFolderInfo Folder;
        public string Key => Provider + ":" + Id;
    }
    sealed class AchievementHubSession
    {
        public List<AchievementHubGame> Games = new();
        /// <summary>Pinned game keys in pin order; not part of <see cref="Scope"/> because pins don't change whose data is shown.</summary>
        public List<string> Pinned = new();
        public string SteamAccount, SteamKey, RetroAccount, RetroKey, LibraryRoot;
        public string Account(string provider) => provider == "steam" ? (SteamAccount ?? "").Trim() : AchievementHubStore.AccountKey(RetroAccount);
        public string ApiKey(string provider) => provider == "steam" ? SteamKey : RetroKey;
        public string Scope => string.Join("\n", LibraryRoot, Account("steam"), Account("retroachievements"), SteamKey, RetroKey,
            string.Join("|", Games.Select(g => g.Key).OrderBy(k => k)));
    }
    sealed class AchievementHubCard
    {
        public AchievementHubGame Game;
        public long LastPlayedUtc;
        public AchievementHubStore.Snapshot Snapshot;
    }
    sealed class AchievementHubUnlock
    {
        public AchievementHubGame Game;
        public GameAchievementsFetchService.AchievementRow Row;
    }
    sealed class AchievementHubTimeline
    {
        public List<AchievementHubUnlock> Unlocks = new();
        public int LibraryGames, CoveredGames, UndatedEarned;
        public long OldestSnapshotUtc;
    }
    sealed class AchievementHubState
    {
        public List<AchievementHubCard> Cards = new();
        public List<AchievementHubCard> Pinned = new();
        public AchievementHubTimeline Timeline;
        public AchievementHubCollection Collection;
        public string Status;
    }
    sealed class AchievementHubService
    {
        static readonly ConcurrentDictionary<string, DateTime> RetryAfter = new();
        static bool BackingOff(string key) => RetryAfter.TryGetValue(key, out var time) && time > DateTime.UtcNow;
        readonly AchievementHubStore store;
        readonly Func<string, CancellationToken, Task<string>> download;
        readonly Func<AchievementHubGame, AchievementHubSession, CancellationToken, Task<GameAchievementsFetchService.FetchResult>> fetch;
        internal AchievementHubService(AchievementHubStore store,
            Func<string, CancellationToken, Task<string>> download = null,
            Func<AchievementHubGame, AchievementHubSession, CancellationToken, Task<GameAchievementsFetchService.FetchResult>> fetch = null)
        {
            this.store = store;
            this.download = download ?? DownloadAsync;
            this.fetch = fetch ?? ((game, session, token) => GameAchievementsFetchService.FetchAsync(
                game.Provider == "steam" ? "Steam" : "Emulation", game.Folder, session.SteamKey, session.RetroKey,
                session.SteamAccount, session.RetroAccount, "PixelVault/AchievementHub", token));
        }
        static async Task<string> DownloadAsync(string url, CancellationToken token)
        {
            using var client = new TimeoutWebClient { Encoding = Encoding.UTF8, TimeoutMilliseconds = 15000 };
            client.Headers["User-Agent"] = "PixelVault/AchievementHub";
            return await client.DownloadStringAsync(url, token).ConfigureAwait(false);
        }
        static bool Fresh(long ticks) => ticks > 0 && ticks <= DateTime.UtcNow.Ticks && DateTime.UtcNow.Ticks - ticks < TimeSpan.FromMinutes(10).Ticks;
        internal const int RecentLimit = 10;
        static AchievementHubCard Card(AchievementHubGame game, IReadOnlyDictionary<string, AchievementHubStore.AccountData> data)
        {
            data.TryGetValue(game.Provider, out var account);
            var time = account != null && account.Recent.TryGetValue(game.Id, out var last) ? last : 0;
            var snapshot = account != null && account.Games.TryGetValue(game.Id, out var saved) ? saved : null;
            return new AchievementHubCard { Game = game, LastPlayedUtc = time, Snapshot = snapshot };
        }
        static bool ValidPlayed(long ticks) => ticks >= DateTime.UnixEpoch.Ticks && ticks <= DateTime.UtcNow.Ticks;
        internal static List<AchievementHubCard> SelectRecent(IEnumerable<AchievementHubGame> games,
            IReadOnlyDictionary<string, AchievementHubStore.AccountData> data, int limit = RecentLimit)
        {
            return games.GroupBy(g => g.Key).Select(g => Card(g.First(), data)).Where(c => ValidPlayed(c.LastPlayedUtc))
                .OrderByDescending(c => c.LastPlayedUtc).ThenBy(c => c.Game.Key, StringComparer.Ordinal).Take(limit).ToList();
        }
        /// <summary>Pinned library games in pin order, for connected providers. Pins for games no longer in the library are skipped.</summary>
        internal static List<AchievementHubCard> SelectPinned(IEnumerable<AchievementHubGame> games, IEnumerable<string> pins,
            IReadOnlyDictionary<string, AchievementHubStore.AccountData> data)
        {
            var library = games.GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return (pins ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal)
                .Where(k => k != null && library.TryGetValue(k, out var game) && data.ContainsKey(game.Provider))
                .Select(k => Card(library[k], data)).Select(c => { if (!ValidPlayed(c.LastPlayedUtc)) c.LastPlayedUtc = 0; return c; }).ToList();
        }
        /// <summary>Earned achievements with provider unlock dates from saved snapshots of connected accounts, newest first.</summary>
        internal static AchievementHubTimeline SelectUnlocks(IEnumerable<AchievementHubGame> games,
            IReadOnlyDictionary<string, AchievementHubStore.AccountData> data, long nowUtcTicks, int limit = 24)
        {
            var timeline = new AchievementHubTimeline();
            var all = new List<AchievementHubUnlock>();
            foreach (var game in games.GroupBy(g => g.Key).Select(g => g.First()))
            {
                if (!data.TryGetValue(game.Provider, out var account)) continue;
                timeline.LibraryGames++;
                if (!account.Games.TryGetValue(game.Id, out var snapshot) || snapshot?.Rows == null) continue;
                timeline.CoveredGames++;
                if (timeline.OldestSnapshotUtc == 0 || snapshot.UpdatedUtc < timeline.OldestSnapshotUtc) timeline.OldestSnapshotUtc = snapshot.UpdatedUtc;
                foreach (var row in snapshot.Rows.Where(r => r != null && r.Provider == game.Provider && r.ProviderGameId == game.Id)
                    .GroupBy(r => r.ProviderAchievementId, StringComparer.Ordinal).Select(r => r.First()))
                {
                    if (AchievementCollectionStats.ValidUnlock(row, nowUtcTicks)) all.Add(new AchievementHubUnlock { Game = game, Row = row });
                    else if (row.ProgressKnown && row.Unlocked) timeline.UndatedEarned++;
                }
            }
            timeline.Unlocks = all.OrderByDescending(u => u.Row.UnlockUtcTicks).ThenBy(u => u.Game.Key, StringComparer.Ordinal)
                .ThenBy(u => u.Row.ProviderAchievementId, StringComparer.Ordinal).Take(limit).ToList();
            return timeline;
        }
        internal async Task LoadAsync(AchievementHubSession session, bool force, Action<AchievementHubState> report, CancellationToken token)
        {
            var data = new Dictionary<string, AchievementHubStore.AccountData>();
            var providers = session.Games.Select(g => g.Provider).Distinct().ToList();
            var notes = new List<string>();
            foreach (var provider in providers)
            {
                if (string.IsNullOrEmpty(session.Account(provider)) || string.IsNullOrWhiteSpace(session.ApiKey(provider)))
                { notes.Add((provider == "steam" ? "Steam" : "RetroAchievements") + ": connect your account in Settings."); continue; }
                data[provider] = await store.LoadAsync(provider, session.Account(provider), token);
            }
            void Report(string status) => report(new AchievementHubState { Cards = SelectRecent(session.Games, data),
                Pinned = SelectPinned(session.Games, session.Pinned, data),
                Timeline = SelectUnlocks(session.Games, data, DateTime.UtcNow.Ticks),
                Collection = AchievementHubCollection.Build(session.Games, data), Status = status });
            Report("Showing saved progress while checking recent play…");
            foreach (var provider in data.Keys.ToArray())
            {
                token.ThrowIfCancellationRequested();
                if (!force && Fresh(data[provider].RecentUpdatedUtc)) continue;
                var retryKey = provider + ":" + session.Account(provider) + ":recent";
                if (!force && BackingOff(retryKey)) { notes.Add(provider + ": retry available shortly; using saved history."); continue; }
                try
                {
                    var recent = await LoadRecentAsync(provider, session, token);
                    RetryAfter.TryRemove(retryKey, out _);
                    data[provider].Recent = recent;
                    data[provider].RecentUpdatedUtc = DateTime.UtcNow.Ticks;
                    try { await store.SaveRecentAsync(provider, session.Account(provider), recent, token); }
                    catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { notes.Add("Recent play could not be saved."); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch
                {
                    RetryAfter[retryKey] = DateTime.UtcNow.AddMinutes(2);
                    notes.Add((provider == "steam" ? "Steam" : "RetroAchievements") + ": play history unavailable; showing saved history if available.");
                }
            }
            Report("Updating achievement progress… " + string.Join(" ", notes));
            foreach (var card in SelectPinned(session.Games, session.Pinned, data).Concat(SelectRecent(session.Games, data))
                .GroupBy(c => c.Game.Key).Select(g => g.First()).ToList())
            {
                token.ThrowIfCancellationRequested();
                if (!force && card.Snapshot != null && Fresh(card.Snapshot.UpdatedUtc)) continue;
                var retryKey = card.Game.Key + ":" + session.Account(card.Game.Provider);
                if (!force && BackingOff(retryKey)) { notes.Add(card.Game.Title + ": retry available shortly; using saved progress."); continue; }
                try
                {
                    var result = await fetch(card.Game, session, token);
                    token.ThrowIfCancellationRequested();
                    if (AchievementHubStore.CanSave(card.Game.Provider, card.Game.Id, result))
                    {
                        RetryAfter.TryRemove(retryKey, out _);
                        var now = DateTime.UtcNow.Ticks;
                        data[card.Game.Provider].Games[card.Game.Id] = new AchievementHubStore.Snapshot { UpdatedUtc = now, DetailsUpdatedUtc = now, Rows = result.Rows };
                        try { await store.SaveGameAsync(card.Game.Provider, session.Account(card.Game.Provider), card.Game.Id, result, token); }
                        catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { notes.Add("Progress could not be saved."); }
                    }
                    else
                    {
                        RetryAfter[retryKey] = DateTime.UtcNow.AddMinutes(2);
                        notes.Add(card.Game.Title + ": progress unavailable; retaining any saved progress.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch
                {
                    RetryAfter[retryKey] = DateTime.UtcNow.AddMinutes(2);
                    notes.Add(card.Game.Title + ": refresh failed; retaining any saved progress.");
                }
                Report("Updating achievement progress… " + string.Join(" ", notes));
            }
            if (data.ContainsKey("retroachievements")) notes.Add("RetroAchievements coverage: up to 500 most recently played games.");
            Report(notes.Count > 0 ? string.Join(" ", notes) : data.Count == 0
                ? "Add a Steam or RetroAchievements account and game IDs in Settings / Edit Metadata."
                : "Provider-reported play dates · progress includes all achievement groups · refreshes cached for 10 minutes.");
        }
        async Task<Dictionary<string, long>> LoadRecentAsync(string provider, AchievementHubSession session, CancellationToken token)
        {
            var account = Uri.EscapeDataString(session.Account(provider));
            var key = Uri.EscapeDataString(session.ApiKey(provider).Trim());
            if (provider == "steam")
                return ParseSteamRecent(await download("https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key=" + key
                    + "&steamid=" + account + "&include_appinfo=0&include_played_free_games=1", token));
            var result = new Dictionary<string, long>();
            var library = session.Games.Where(g => g.Provider == provider).Select(g => g.Id).ToHashSet();
            for (var offset = 0; offset < 500; offset += 50)
            {
                var json = await download("https://retroachievements.org/API/API_GetUserRecentlyPlayedGames.php?y=" + key
                    + "&u=" + account + "&c=50&o=" + offset, token);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new System.IO.InvalidDataException();
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    if (row.TryGetProperty("GameID", out var id) && row.TryGetProperty("LastPlayed", out var date)
                        && DateTime.TryParse(date.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
                        && time >= DateTime.UnixEpoch && time <= DateTime.UtcNow) result[id.ToString()] = time.Ticks;
                }
                if (doc.RootElement.GetArrayLength() < 50 || result.Keys.Count(library.Contains) >= RecentLimit) break;
            }
            return result;
        }
        internal static Dictionary<string, long> ParseSteamRecent(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var response)
                || !response.TryGetProperty("game_count", out _)) throw new System.IO.InvalidDataException();
            var result = new Dictionary<string, long>();
            if (!response.TryGetProperty("games", out var games))
            {
                if (response.GetProperty("game_count").GetInt32() != 0) throw new System.IO.InvalidDataException();
                return result;
            }
            if (games.ValueKind != JsonValueKind.Array) throw new System.IO.InvalidDataException();
            if (games.GetArrayLength() > 0 && !games.EnumerateArray().Any(g => g.TryGetProperty("rtime_last_played", out _)))
                throw new System.IO.InvalidDataException();
            foreach (var game in games.EnumerateArray())
            {
                if (!game.TryGetProperty("appid", out var id) || !id.TryGetInt32(out var app) || app <= 0
                    || !game.TryGetProperty("rtime_last_played", out var last) || !last.TryGetInt64(out var unix)
                    || unix <= 0 || unix > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) continue;
                result[app.ToString(CultureInfo.InvariantCulture)] = DateTimeOffset.FromUnixTimeSeconds(unix).UtcTicks;
            }
            return result;
        }
    }
}
