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
            public long UpdatedUtc { get; set; }
            public List<GameAchievementsFetchService.AchievementRow> Rows { get; set; } = new();
        }
        internal sealed class AccountData
        {
            public int Version { get; set; } = 1;
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
                    if (data?.Version == 1 && data.Provider == provider && data.Account == AccountKey(account)
                        && data.Recent != null && data.Games != null)
                    {
                        foreach (var game in data.Games.Keys.ToArray())
                        {
                            var snapshot = data.Games[game];
                            if (snapshot == null || snapshot.UpdatedUtc <= 0 || snapshot.UpdatedUtc > DateTime.UtcNow.Ticks
                                || !CanSave(provider, game, new GameAchievementsFetchService.FetchResult { Rows = snapshot.Rows }))
                                data.Games.Remove(game);
                        }
                        return data;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException) { }
            return new AccountData { Provider = provider, Account = AccountKey(account) };
        }
        internal async Task<AccountData> LoadAsync(string provider, string account, CancellationToken token)
        {
            var path = FilePath(provider, account);
            var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try { return await Task.Run(() => Read(path, provider, account), token).ConfigureAwait(false); }
            finally { gate.Release(); }
        }
        async Task UpdateAsync(string provider, string account, Action<AccountData> update, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(account)) return;
            var path = FilePath(provider, account);
            var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    var data = Read(path, provider, account);
                    update(data);
                    Directory.CreateDirectory(directory);
                    var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, JsonSerializer.Serialize(data, Json));
                        token.ThrowIfCancellationRequested();
                        File.Move(temp, path, true);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }, token).ConfigureAwait(false);
            }
            finally { gate.Release(); }
        }
        internal Task SaveRecentAsync(string provider, string account, Dictionary<string, long> recent, CancellationToken token) =>
            UpdateAsync(provider, account, d => { d.Recent = recent; d.RecentUpdatedUtc = DateTime.UtcNow.Ticks; }, token);
        internal static bool CanSave(string provider, string game, GameAchievementsFetchService.FetchResult result) =>
            result != null && !result.IsError && !result.NotificationOnly && string.IsNullOrEmpty(result.ProgressErrorMessage)
            && result.Rows != null && result.Rows.All(r => r != null && r.ProgressKnown && r.Provider == provider
                && r.ProviderGameId == game && r.HasStableProviderIdentity)
            && result.Rows.Select(r => r.ProviderAchievementId).Distinct(StringComparer.Ordinal).Count() == result.Rows.Count;
        internal Task SaveGameAsync(string provider, string account, string game, GameAchievementsFetchService.FetchResult result, CancellationToken token)
        {
            if (!CanSave(provider, game, result)) return Task.CompletedTask;
            var rows = JsonSerializer.Deserialize<List<GameAchievementsFetchService.AchievementRow>>(JsonSerializer.Serialize(result.Rows, Json), Json);
            var snapshot = new Snapshot { UpdatedUtc = DateTime.UtcNow.Ticks, Rows = rows };
            return UpdateAsync(provider, account, d =>
            {
                if (!d.Games.TryGetValue(game, out var prior) || prior == null || prior.UpdatedUtc <= snapshot.UpdatedUtc)
                    d.Games[game] = snapshot;
            }, token);
        }
    }
}
