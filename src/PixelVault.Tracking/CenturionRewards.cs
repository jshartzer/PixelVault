using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelVaultNative;

namespace PixelVault.Tracking;

public sealed record CenturionItem(string Id, string Title, string Description, int Order, int Points, bool Unlocked, long UnlockUtc);
public sealed record CenturionGame(string Provider, string GameId, string GameTitle, string Scope, CenturionItem[] Items,
    bool BadgeAvailable, bool BadgeUnlocked, long BadgeUnlockUtc);
public sealed record CenturionReward(string Scope, string Provider, string GameId, string GameTitle, string Id,
    string Title, string Description, int Points, bool Badge);

public static class CenturionRewards
{
    public const string BadgeId = "centurion.death-stranding-dc";
    public static bool HasBadge(string provider, string game) => provider == "steam" && game == "1850570";
    public static string Scope(string workspace, string provider, string game, string account) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] {
            workspace, provider, game, AchievementTrackingCatalog.AccountKey(provider, account) }))));

    public static CenturionGame Snapshot(string provider, string game, string scope, Func<string, CenturyProgress> read)
    {
        var definitions = CenturyAchievementCatalog.ForGame(provider, game);
        var items = definitions.Select(d => { var p = read(d.Id); return new CenturionItem(d.Id, d.Title, d.Description,
            d.Order, d.Points, p?.Unlocked == true, p?.UnlockUtc ?? 0); }).ToArray();
        var complete = HasBadge(provider, game) && items.Length == CenturyAchievementCatalog.PlannedPerGame && items.All(i => i.Unlocked);
        return new(provider, game, definitions.FirstOrDefault()?.GameTitle ?? game, scope, items, HasBadge(provider, game),
            complete, complete ? items.Max(i => i.UnlockUtc) : 0);
    }
    public static byte[] BadgeArtwork(string provider, string game)
    {
        if (!HasBadge(provider, game)) return null;
        using var stream = typeof(CenturionRewards).Assembly.GetManifestResourceStream("PixelVault.CenturyArtwork.death-stranding-centurion.png");
        if (stream == null) return null;
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
}

// Separate delivery history, never achievement progress. First observation baselines historical unlocks.
public sealed class CenturionRewardLedger
{
    public Dictionary<string, HashSet<string>> Seen { get; set; } = new(StringComparer.Ordinal);
    public List<CenturionReward> Observe(IEnumerable<CenturionGame> games)
    {
        var notices = new List<CenturionReward>();
        foreach (var game in games)
        {
            var initialized = Seen.TryGetValue(game.Scope, out var seen);
            if (!initialized) Seen[game.Scope] = seen = new(StringComparer.Ordinal);
            foreach (var item in game.Items.Where(i => i.Unlocked).OrderBy(i => i.Order))
                if (seen.Add(item.Id) && initialized)
                    notices.Add(new(game.Scope, game.Provider, game.GameId, game.GameTitle, item.Id, item.Title, item.Description, item.Points, false));
            // Always enqueue the badge after every new achievement in the same committed snapshot.
            if (game.BadgeUnlocked && seen.Add(CenturionRewards.BadgeId) && initialized)
                notices.Add(new(game.Scope, game.Provider, game.GameId, game.GameTitle, CenturionRewards.BadgeId,
                    "Centurion", "All five Centurion achievements unlocked.", 0, true));
        }
        return notices;
    }
}
