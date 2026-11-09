using System.Text.Json;

namespace PixelVault.Companion;

public sealed record AchievementUpdateGame(string Provider,string GameId,string Scope,string[] Locked,string[] Unlocked);

public sealed partial class AchievementStore
{
    // A small, read-only projection. Titles/artwork are fetched only for actual new unlocks.
    public IReadOnlyList<AchievementUpdateGame> Updates()
    {
        if(workspace.ReadOnly)return [];
        var accounts=AchievementAccountScope.Read(options,workspace);
        var games=new Dictionary<(string Provider,string Game),Dictionary<string,(bool Known,bool Unlocked)>>();
        void Add(string provider,string game,string id,bool known,bool unlocked)
        {
            if(!accounts.ContainsKey(provider))return;
            if(!games.TryGetValue((provider,game),out var rows))games[(provider,game)]=rows=new(StringComparer.Ordinal);
            rows[id]=(known,unlocked);
        }
        if(local is not null)
        {
            foreach(var game in local.Achievements())
                if(accounts.TryGetValue(game.Provider,out var account)&&account==game.Account)
                    foreach(var row in game.Rows)Add(game.Provider,game.GameId,row.Id,row.ProgressKnown,row.Unlocked);
        }
        else
        {
            lock(gate)
            {
                using var c=Open();using var cmd=c.CreateCommand();
                cmd.CommandText="SELECT provider,game,value FROM companion_achievement WHERE workspace=$w";
                cmd.Parameters.AddWithValue("$w",workspace.Identity);
                using var rows=cmd.ExecuteReader();
                while(rows.Read())
                {
                    var provider=rows.GetString(0);
                    if(!accounts.ContainsKey(provider))continue;
                    var saved=JsonSerializer.Deserialize<Saved>(rows.GetString(2),Json)!;
                    Add(provider,rows.GetString(1),saved.Data.Id,saved.Data.ProgressKnown,saved.Data.Unlocked);
                }
            }
        }
        foreach(var definition in PixelVault.Tracking.CenturyAchievementCatalog.All)
        {
            if(!accounts.TryGetValue(definition.Provider,out var account))continue;
            var progress=workspace.Repository.Century(definition.Provider,definition.GameId,definition.Id,account);
            Add(definition.Provider,definition.GameId,definition.Id,progress is not null,progress?.Unlocked==true);
        }
        return games.OrderBy(g=>g.Key.Provider,StringComparer.Ordinal).ThenBy(g=>g.Key.Game,StringComparer.Ordinal).Select(game=>new AchievementUpdateGame(
            game.Key.Provider,game.Key.Game,
            Hash(JsonSerializer.Serialize(new{workspace=workspace.Identity,provider=game.Key.Provider,game=game.Key.Game,account=accounts[game.Key.Provider]})),
            game.Value.Where(r=>r.Value.Known&&!r.Value.Unlocked).Select(r=>r.Key).Order(StringComparer.Ordinal).ToArray(),
            game.Value.Where(r=>r.Value.Known&&r.Value.Unlocked).Select(r=>r.Key).Order(StringComparer.Ordinal).ToArray())).ToArray();
    }
}
