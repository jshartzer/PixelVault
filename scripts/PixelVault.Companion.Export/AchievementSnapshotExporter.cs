using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;

namespace PixelVault.Companion.Export;

public static class AchievementSnapshotExporter
{
    public static Dictionary<string,string> ConnectedAccounts(string root,Dictionary<string,string> trackerAccounts)
    {
        var result=trackerAccounts.ToDictionary(p=>p.Key,p=>p.Value);var file=Path.Combine(root,"PixelVault.settings.ini");if(!File.Exists(file))return result;
        foreach(var line in File.ReadLines(file)){var pair=line.Split('=',2);if(pair.Length!=2)continue;var provider=pair[0].Trim() switch{"steam_user_id_64"=>"steam","retroachievements_username"=>"retroachievements",_=>null};if(provider is null||string.IsNullOrWhiteSpace(pair[1]))continue;var account=pair[1].Trim();if(result.TryGetValue(provider,out var old)&&!string.Equals(account,old,provider=="steam"?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The connected achievement account does not match the approved tracker account.");result[provider]=result.GetValueOrDefault(provider)??account;}
        return result;
    }
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static string S(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
    static bool B(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.True;
    static long L(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.TryGetInt64(out var result)?result:0;
    static double? N(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var result)?result:null;
    public static AchievementGroups Groups(string category,string dlc,int order)
    {
        category=category.Trim();dlc=dlc.Trim();
        if(dlc.Equals("Base Game",StringComparison.OrdinalIgnoreCase))dlc="";
        if(category.StartsWith("DLC: ",StringComparison.OrdinalIgnoreCase)){if(dlc.Length==0)dlc=category[5..].Trim();category="";}
        if(category.Equals("DLC",StringComparison.OrdinalIgnoreCase)){if(dlc.Length==0)dlc="DLC";category="";}
        if(category.Equals("Base Game",StringComparison.OrdinalIgnoreCase)||category.Equals("Uncategorized",StringComparison.OrdinalIgnoreCase))category="";
        return new(category,dlc,order);
    }
    public static IReadOnlyList<AchievementGameSync> Read(string root,string workspace,Dictionary<string,string> accounts)
    {
        SQLitePCL.Batteries_V2.Init();var snapshots=new Dictionary<string,(long Updated,Dictionary<string,JsonElement> Rows)>();
        var folder=Path.Combine(root,"achievement-hub");
        foreach(var file in Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.json"):[])
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(file));var value=doc.RootElement;var provider=S(value,"Provider");
            if(!accounts.TryGetValue(provider,out var account)||!string.Equals(S(value,"Account"),account,provider=="steam"?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase))continue;
            if(!value.TryGetProperty("Games",out var games))continue;
            foreach(var game in games.EnumerateObject())
            {
                var rows=new Dictionary<string,JsonElement>();
                foreach(var row in game.Value.GetProperty("Rows").EnumerateArray())
                    if(S(row,"Provider")==provider&&S(row,"ProviderGameId")==game.Name&&S(row,"ProviderAchievementId") is var id&&id.Length>0)rows.TryAdd(id,row.Clone());
                snapshots[provider+":"+game.Name]=(L(game.Value,"UpdatedUtc"),rows);
            }
        }
        var definitions=new Dictionary<string,Dictionary<string,CompanionAchievement>>();var categories=new List<string>{"Story","Actions","Difficulty","Collectibles","Multiplayer","Miscellaneous","Uncategorized"};var order=new List<string>();var dlcs=new Dictionary<string,List<string>>();
        var database=Path.Combine(root,"guides","pixelvault-guides.sqlite");
        if(File.Exists(database))
        {
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();using var tx=c.BeginTransaction(deferred:true);
            bool Table(string name){using var check=c.CreateCommand();check.Transaction=tx;check.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";check.Parameters.AddWithValue("$n",name);return (long)check.ExecuteScalar()!>0;}
            using var cmd=c.CreateCommand();cmd.Transaction=tx;
            if(Table("achievement_category")){cmd.CommandText="SELECT name FROM achievement_category";using var names=cmd.ExecuteReader();while(names.Read())categories.Add(names.GetString(0));}
            if(Table("achievement_category_order")){cmd.CommandText="SELECT name FROM achievement_category_order ORDER BY position";using var names=cmd.ExecuteReader();while(names.Read())order.Add(names.GetString(0));}
            if(Table("achievement_dlc")){cmd.CommandText="SELECT provider,provider_game_id,name FROM achievement_dlc";using var names=cmd.ExecuteReader();while(names.Read()){var key=names.GetString(0)+":"+names.GetString(1);if(!dlcs.ContainsKey(key))dlcs[key]=[];dlcs[key].Add(names.GetString(2));}}
            cmd.CommandText="""
                SELECT d.provider,d.provider_game_id,d.provider_achievement_id,d.title,d.description,d.display_order,d.is_hidden,d.icon_url,d.group_name,d.dlc_name,d.group_sort_order,
                COALESCE(g.guide_text,''),COALESCE(g.source_url,''),COALESCE(g.source_title,''),COALESCE(g.tags,''),COALESCE(g.is_missable,0)
                FROM achievement_definition d LEFT JOIN achievement_guide g USING(achievement_id) WHERE d.is_active=1
                """;
            using var rows=cmd.ExecuteReader();while(rows.Read())
            {
                var provider=rows.GetString(0);if(!accounts.ContainsKey(provider))continue;var game=rows.GetString(1);var id=rows.GetString(2);var key=provider+":"+game;
                if(!definitions.ContainsKey(key))definitions[key]=[];
                var groups=Groups(rows.GetString(8),rows.GetString(9),rows.GetInt32(10));
                definitions[key][id]=new(id,rows.GetString(3),rows.GetString(4),rows.GetInt32(5),rows.GetInt32(6)!=0,false,false,0,null,null,null,rows.GetString(7),rows.GetString(11),rows.GetString(12),rows.GetString(13),rows.GetString(14),rows.GetInt32(15)!=0,groups);
            }
        }
        var results=new List<AchievementGameSync>();
        foreach(var key in definitions.Keys.Union(snapshots.Keys).Order())
        {
            var parts=key.Split(':',2);var provider=parts[0];var game=parts[1];var saved=definitions.GetValueOrDefault(key)??new();var live=snapshots.GetValueOrDefault(key);
            if(live.Rows is not null)foreach(var pair in live.Rows)
            {
                var row=pair.Value;var old=saved.GetValueOrDefault(pair.Key);var known=B(row,"ProgressKnown");
                saved[pair.Key]=new(pair.Key,old?.Title??S(row,"Title"),old?.Description??S(row,"Description"),old?.Order??(int)L(row,"SortKey"),old?.Hidden??B(row,"Hidden"),known,known&&B(row,"Unlocked"),L(row,"UnlockUtcTicks"),N(row,"GlobalUnlockPercent"),N(row,"CounterCurrent"),N(row,"CounterTarget"),S(row,"IconUrlColor") is var icon&&icon.Length>0?icon:old?.Icon??"",old?.Guide??"",old?.SourceUrl??"",old?.SourceTitle??"",old?.Tags??"",old?.Missable??B(row,"IsMissable"),old?.Groups??Groups(S(row,"GroupName"),S(row,"DlcName"),(int)L(row,"GroupSortOrder")));
            }
            if(saved.Count>0)results.Add(new(workspace,provider,game,accounts[provider],DateTime.UtcNow.Ticks,live.Updated,saved.Values.OrderBy(r=>r.Order).ToArray(),categories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),order,dlcs.GetValueOrDefault(key)??[]));
        }
        return results;
    }
}
