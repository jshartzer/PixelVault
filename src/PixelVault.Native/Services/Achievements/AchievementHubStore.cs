using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    sealed class AchievementHubStore
    {
        internal sealed class Snapshot
        {
            /// <summary>When earned status was last confirmed (full fetch or background notification check).</summary>
            public long UpdatedUtc { get; set; }
            /// <summary>When rarity/counter details were last fetched in full; 0 when only background checks have been saved.</summary>
            public long DetailsUpdatedUtc { get; set; }
            public List<GameAchievementsFetchService.AchievementRow> Rows { get; set; } = new();
        }
        internal sealed class AccountData
        {
            public int Version { get; set; } = 2;
            public string Provider { get; set; }
            public string Account { get; set; }
            public long RecentUpdatedUtc { get; set; }
            public Dictionary<string, long> Recent { get; set; } = new();
            public Dictionary<string, Snapshot> Games { get; set; } = new();
        }
        static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
        static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
        readonly string directory;
        internal AchievementHubStore(string directory) { this.directory = Path.GetFullPath(directory); }
        internal static string AccountKey(string account) => (account ?? "").Trim().ToLowerInvariant();
        string FilePath(string provider, string account) => Path.Combine(directory,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider + "\n" + AccountKey(account)))) + ".json");
        AccountData Read(string path, string provider, string account)
        {
            try
            {
                if (new FileInfo(path).Length <= 32 * 1024 * 1024)
                {
                    var data = JsonSerializer.Deserialize<AccountData>(File.ReadAllText(path), Json);
                    if ((data?.Version == 1 || data?.Version == 2) && data.Provider == provider && data.Account == AccountKey(account)
                        && data.Recent != null && data.Games != null)
                    {
                        foreach (var game in data.Games.Keys.ToArray())
                        {
                            var snapshot = data.Games[game];
                            if (snapshot == null || snapshot.UpdatedUtc <= 0 || snapshot.UpdatedUtc > DateTime.UtcNow.Ticks
                                || !CanStore(provider, game, new GameAchievementsFetchService.FetchResult { Rows = snapshot.Rows }))
                                data.Games.Remove(game);
                            // Version 1 stored full fetches only.
                            else if (data.Version == 1) snapshot.DetailsUpdatedUtc = snapshot.UpdatedUtc;
                        }
                        data.Version = 2;
                        return data;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { }
            return new AccountData { Provider = provider, Account = AccountKey(account) };
        }
        // Background checks save every library game each cycle; avoid re-parsing the account file for each one.
        // Entries are only trusted while the file's timestamp and length are unchanged. Access is under the path gate.
        readonly ConcurrentDictionary<string, (DateTime Stamp, long Length, AccountData Data)> cache = new(StringComparer.OrdinalIgnoreCase);
        AccountData ReadCached(string path, string provider, string account)
        {
            var info = new FileInfo(path);
            if (info.Exists && cache.TryGetValue(path, out var hit) && hit.Stamp == info.LastWriteTimeUtc && hit.Length == info.Length)
                return hit.Data;
            var data = Read(path, provider, account);
            if (info.Exists) cache[path] = (info.LastWriteTimeUtc, info.Length, data);
            else cache.TryRemove(path, out _);
            return data;
        }
        internal async Task<AccountData> LoadAsync(string provider, string account, CancellationToken token)
        {
            var path = FilePath(provider, account);
            var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            // Callers mutate their copy, so never hand out the cached instance.
            try { return await Task.Run(() => JsonSerializer.Deserialize<AccountData>(JsonSerializer.Serialize(ReadCached(path, provider, account), Json), Json), token).ConfigureAwait(false); }
            finally { gate.Release(); }
        }
        async Task UpdateAsync(string provider, string account, Func<AccountData, bool> update, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(account)) return;
            var path = FilePath(provider, account);
            var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    var data = ReadCached(path, provider, account);
                    var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        if (!update(data)) return;
                        Directory.CreateDirectory(directory);
                        File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
                        token.ThrowIfCancellationRequested();
                        File.Move(temp, path, true);
                        var info = new FileInfo(path);
                        cache[path] = (info.LastWriteTimeUtc, info.Length, data);
                    }
                    catch { cache.TryRemove(path, out _); throw; }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }, token).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }
        internal Task SaveRecentAsync(string provider, string account, Dictionary<string, long> recent, CancellationToken token) =>
            UpdateAsync(provider, account, d => { d.Recent = recent; d.RecentUpdatedUtc = DateTime.UtcNow.Ticks; return true; }, token);
        /// <summary>Complete, known progress for one provider game. Background notification results also qualify; they are merged.</summary>
        internal static bool CanStore(string provider, string game, GameAchievementsFetchService.FetchResult result) =>
            result != null && !result.IsError && string.IsNullOrEmpty(result.ProgressErrorMessage)
            && result.Rows != null && result.Rows.All(r => r != null && r.ProgressKnown && r.Provider == provider
                && r.ProviderGameId == game && r.HasStableProviderIdentity)
            && result.Rows.Select(r => r.ProviderAchievementId).Distinct(StringComparer.Ordinal).Count() == result.Rows.Count;
        /// <summary>Full fetch with rarity/counter details.</summary>
        internal static bool CanSave(string provider, string game, GameAchievementsFetchService.FetchResult result) =>
            result != null && !result.NotificationOnly && CanStore(provider, game, result);
        internal Task SaveGameAsync(string provider, string account, string game, GameAchievementsFetchService.FetchResult result, CancellationToken token)
        {
            if (!CanStore(provider, game, result)) return Task.CompletedTask;
            var rows = JsonSerializer.Deserialize<List<GameAchievementsFetchService.AchievementRow>>(JsonSerializer.Serialize(result.Rows, Json), Json);
            var now = DateTime.UtcNow.Ticks;
            return UpdateAsync(provider, account, d =>
            {
                d.Games.TryGetValue(game, out var prior);
                if (prior != null && prior.UpdatedUtc > now) return false;
                if (!result.NotificationOnly)
                {
                    d.Games[game] = new Snapshot { UpdatedUtc = now, DetailsUpdatedUtc = now, Rows = rows };
                    return true;
                }
                var merged = Merge(prior, rows, now);
                if (prior != null && SameProgress(prior.Rows, merged.Rows) && now - prior.UpdatedUtc < TimeSpan.FromHours(6).Ticks) return false;
                d.Games[game] = merged;
                return true;
            }, token);
        }
        static bool SameProgress(List<GameAchievementsFetchService.AchievementRow> a, List<GameAchievementsFetchService.AchievementRow> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            var before = a.ToDictionary(r => r.ProviderAchievementId, StringComparer.Ordinal);
            return b.All(r => before.TryGetValue(r.ProviderAchievementId, out var old)
                && old.Unlocked == r.Unlocked && old.UnlockUtcTicks == r.UnlockUtcTicks);
        }
        /// <summary>
        /// Background checks are authoritative for the achievement set, earned status, and unlock dates, but skip rarity and
        /// counters; keep those from the last full fetch.
        /// </summary>
        internal static Snapshot Merge(Snapshot prior, List<GameAchievementsFetchService.AchievementRow> light, long now)
        {
            var saved = (prior?.Rows ?? new List<GameAchievementsFetchService.AchievementRow>())
                .Where(r => r?.ProviderAchievementId != null).GroupBy(r => r.ProviderAchievementId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var row in light)
            {
                if (!saved.TryGetValue(row.ProviderAchievementId, out var old)) continue;
                row.GlobalUnlockPercent ??= old.GlobalUnlockPercent;
                row.CounterCurrent ??= old.CounterCurrent;
                row.CounterMinimum ??= old.CounterMinimum;
                row.CounterTarget ??= old.CounterTarget;
                if (string.IsNullOrWhiteSpace(row.Description)) row.Description = old.Description;
                if (string.IsNullOrWhiteSpace(row.IconUrlColor)) row.IconUrlColor = old.IconUrlColor;
                if (string.IsNullOrWhiteSpace(row.IconUrlGray)) row.IconUrlGray = old.IconUrlGray;
                if (string.IsNullOrWhiteSpace(row.GroupName)) { row.GroupName = old.GroupName; row.GroupSortOrder = old.GroupSortOrder; }
            }
            return new Snapshot { UpdatedUtc = now, DetailsUpdatedUtc = prior?.DetailsUpdatedUtc ?? 0, Rows = light };
        }
    }
}
