using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

namespace PixelVault.Companion.Connector;

public static class DesktopTrackerPublication
{
    public static IReadOnlyList<TrackerTitle> ReadTitles(string root,Dictionary<string,string> accounts)
    {
        var path=Path.Combine(root,"achievement-tracking","pixelvault-tracking.sqlite");
        if(!File.Exists(path))throw new FileNotFoundException("The desktop tracker database is unavailable.");
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
        using var tx=c.BeginTransaction(deferred:true);using var cmd=c.CreateCommand();cmd.Transaction=tx;
        var definitions=AchievementTrackingCatalog.Builtins().ToList();
        cmd.CommandText="SELECT definition FROM tracker_definition ORDER BY id";
        using(var rows=cmd.ExecuteReader())while(rows.Read())definitions.Add(JsonSerializer.Deserialize<AchievementTrackerDefinition>(rows.GetString(0),AchievementTrackingCatalog.Json));
        var titles=definitions.Where(d=>accounts.ContainsKey(d.Provider)).ToDictionary(d=>d.Id,d=>new TrackerTitle(d.Id,d.Provider,d.GameId,d.Title),StringComparer.Ordinal);
        cmd.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tracker_title'";
        if(Convert.ToInt32(cmd.ExecuteScalar())!=0)
        {
            cmd.CommandText="SELECT tracker_id,title FROM tracker_title";
            using var rows=cmd.ExecuteReader();while(rows.Read())if(titles.TryGetValue(rows.GetString(0),out var title))titles[title.Id]=title with{Title=rows.GetString(1)};
        }
        return titles.Values.OrderBy(t=>t.Id,StringComparer.Ordinal).ToArray();
    }
    public static bool SamePermission(TrackerSourceBinding a,TrackerSourceBinding b)=>
        a.AdapterId==b.AdapterId&&a.AdapterVersion==b.AdapterVersion&&a.Account==b.Account
        &&string.Equals(a.Location,b.Location,StringComparison.OrdinalIgnoreCase)
        &&string.Equals(a.GameDirectory??"",b.GameDirectory??"",StringComparison.OrdinalIgnoreCase)
        &&string.Equals(a.SpecificSave??"",b.SpecificSave??"",StringComparison.OrdinalIgnoreCase)
        &&(a.Profile??"")==(b.Profile??"")&&a.Spreadsheet is null&&b.Spreadsheet is null;
    public static IReadOnlyList<PublishedTracker> Read(string root,Dictionary<string,string> accounts,IReadOnlyList<ConnectorSource> approved,Action<TrackerSourceBinding,AchievementTrackerDefinition> validate)
    {
        var path=Path.Combine(root,"achievement-tracking","pixelvault-tracking.sqlite");
        if(!File.Exists(path))throw new FileNotFoundException("The desktop tracker database is unavailable.");
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
        using var tx=c.BeginTransaction(deferred:true);using var command=c.CreateCommand();command.Transaction=tx;command.CommandText="SELECT definition FROM tracker_definition ORDER BY id";
        var definitions=new List<AchievementTrackerDefinition>();using(var rows=command.ExecuteReader())while(rows.Read())
        {
            var definition=JsonSerializer.Deserialize<AchievementTrackerDefinition>(rows.GetString(0),AchievementTrackingCatalog.Json);
            AchievementTrackingCatalog.Validate(definition);if(accounts.ContainsKey(definition.Provider))definitions.Add(definition);
        }
        command.CommandText="SELECT tracker_id,achievement_id FROM tracker_link";
        using(var links=command.ExecuteReader())while(links.Read())
        {
            var definition=definitions.FirstOrDefault(d=>d.Id==links.GetString(0));
            if(definition is not null)definition.AchievementId=links.IsDBNull(1)?null:links.GetString(1);
        }
        var result=new List<PublishedTracker>();
        foreach(var definition in definitions)
        {
            TrackerSourceBinding binding=null;command.Parameters.Clear();command.CommandText="SELECT binding FROM tracker_source WHERE tracker_id=$id AND account=$account";
            command.Parameters.AddWithValue("$id",definition.Id);command.Parameters.AddWithValue("$account",AchievementTrackingCatalog.AccountKey(definition.Provider,accounts[definition.Provider]));
            if(command.ExecuteScalar() is string json)
            {
                var candidate=JsonSerializer.Deserialize<TrackerSourceBinding>(json,AchievementTrackingCatalog.Json);
                if(candidate?.TrackerId==definition.Id&&candidate.AdapterId=="death-stranding-dc.fabrication"&&approved.Any(s=>s.Definition.Provider==definition.Provider&&s.Definition.GameId==definition.GameId&&SamePermission(candidate,s.Binding)))
                    try{validate(candidate,definition);binding=candidate;}catch(Exception error)when(error is InvalidDataException or NotSupportedException){}
            }
            result.Add(new(definition,binding));
        }
        return result;
    }
}
