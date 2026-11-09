using PixelVault.Tracking;

namespace PixelVault.Companion;

public sealed partial class AchievementStore
{
    public IReadOnlyList<CenturionGame> CenturionCollection()
    {
        if (workspace.ReadOnly) return [];
        var accounts = AchievementAccountScope.Read(options, workspace);
        return CenturyAchievementCatalog.All.GroupBy(d => (d.Provider, d.GameId))
            .Select(g =>
            {
                var account = accounts.GetValueOrDefault(g.Key.Provider) ?? "";
                return CenturionRewards.Snapshot(g.Key.Provider, g.Key.GameId,
                    CenturionRewards.Scope(workspace.Identity, g.Key.Provider, g.Key.GameId, account),
                    id => string.IsNullOrWhiteSpace(account) ? null : workspace.Repository.Century(g.Key.Provider, g.Key.GameId, id, account));
            }).ToArray();
    }
}
