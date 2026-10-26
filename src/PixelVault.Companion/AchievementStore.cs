using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PixelVault.Companion;

public sealed class AchievementStore
{
    readonly CompanionOptions options;readonly TrackerWorkspace workspace;readonly ConnectorSyncState sync;readonly object gate=new();
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public sealed record Saved(CompanionAchievement Data,AchievementGroups Desktop,AchievementGroups? Web=null,string? Pending=null,AchievementGroups? Baseline=null,bool Conflict=false);
    public AchievementStore(CompanionOptions options,TrackerWorkspace workspace,ConnectorSyncState sync)
    {
        this.options=options;this.workspace=workspace;this.sync=sync;
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS companion_achievement_game(workspace TEXT NOT NULL,provider TEXT NOT NULL,game TEXT NOT NULL,captured INTEGER NOT NULL,updated INTEGER NOT NULL,categories TEXT NOT NULL,category_order TEXT NOT NULL,dlcs TEXT NOT NULL,synced INTEGER NOT NULL,PRIMARY KEY(workspace,provider,game));
        CREATE TABLE IF NOT EXISTS companion_achievement(workspace TEXT NOT NULL,provider TEXT NOT NULL,game TEXT NOT NULL,id TEXT NOT NULL,value TEXT NOT NULL,PRIMARY KEY(workspace,provider,game,id));
        CREATE TABLE IF NOT EXISTS companion_achievement_receipt(workspace TEXT NOT NULL,id TEXT NOT NULL,digest TEXT NOT NULL,value TEXT NOT NULL,PRIMARY KEY(workspace,id));
        CREATE TABLE IF NOT EXISTS companion_achievement_category(workspace TEXT NOT NULL,name TEXT COLLATE NOCASE NOT NULL,PRIMARY KEY(workspace,name));
        CREATE TABLE IF NOT EXISTS companion_achievement_dlc(workspace TEXT NOT NULL,provider TEXT NOT NULL,game TEXT NOT NULL,name TEXT COLLATE NOCASE NOT NULL,PRIMARY KEY(workspace,provider,game,name));
        """;cmd.ExecuteNonQuery();
    }
    SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(options.DataRoot,"companion.sqlite"),Pooling=false}.ToString());c.Open();return c;}
    void Scope(string provider,string game)
    {
        if(workspace.ReadOnly||!AchievementAccountScope.Read(options,workspace).ContainsKey(provider)||!Regex.IsMatch(game??"","^[A-Za-z0-9._-]{1,80}$"))throw new KeyNotFoundException();
    }
    void Automatic(){var settings=sync.Settings();if(!settings.AutomaticEnabled||!settings.LibraryEnabled)throw new InvalidOperationException("Automatic achievement sync is paused.");}
    static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static AchievementGroups Normalize(AchievementGroups value)
    {
        if(value is null||value.Category is null||value.Dlc is null||value.Category.Length>120||value.Dlc.Length>120||value.Category.Any(char.IsControl)||value.Dlc.Any(char.IsControl)||value.SortOrder is <0 or >100000)throw new InvalidDataException("Invalid achievement groups.");
        return value with{Category=value.Category.Trim().Equals("Uncategorized",StringComparison.OrdinalIgnoreCase)?"":value.Category.Trim(),Dlc=value.Dlc.Trim().Equals("Base Game",StringComparison.OrdinalIgnoreCase)?"":value.Dlc.Trim()};
    }
    SqliteCommand Command(SqliteConnection c,string sql,string provider,string game,string? id=null,SqliteTransaction? tx=null)
    {var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$g",game);if(id is not null)cmd.Parameters.AddWithValue("$id",id);return cmd;}
    Saved Read(SqliteConnection c,string provider,string game,string id,SqliteTransaction? tx=null)
    {using var cmd=Command(c,"SELECT value FROM companion_achievement WHERE workspace=$w AND provider=$p AND game=$g AND id=$id",provider,game,id,tx);return cmd.ExecuteScalar() is string value?JsonSerializer.Deserialize<Saved>(value,Json)!:throw new KeyNotFoundException();}
    void Write(SqliteConnection c,string provider,string game,Saved value,SqliteTransaction? tx=null)
    {using var cmd=Command(c,"INSERT INTO companion_achievement(workspace,provider,game,id,value) VALUES($w,$p,$g,$id,$v) ON CONFLICT(workspace,provider,game,id) DO UPDATE SET value=$v",provider,game,value.Data.Id,tx);cmd.Parameters.AddWithValue("$v",JsonSerializer.Serialize(value,Json));cmd.ExecuteNonQuery();}
    static string Version(Saved saved)=>Hash(JsonSerializer.Serialize(new{groups=saved.Web??saved.Desktop,saved.Conflict,desktop=saved.Conflict?saved.Desktop:null},Json));
    static object View(Saved saved)=>new{achievement=saved.Data with{Groups=saved.Web??saved.Desktop},version=Version(saved),groupSync=saved.Conflict?"conflict":saved.Pending is not null?"pending":"synced",desktopGroups=saved.Desktop};
    void Register(SqliteConnection c,string provider,string game,IEnumerable<string> categories,IEnumerable<string> dlcs,SqliteTransaction tx)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        foreach(var name in categories.Where(n=>n.Length>0).Distinct(StringComparer.OrdinalIgnoreCase)){cmd.Parameters.Clear();cmd.CommandText="INSERT OR IGNORE INTO companion_achievement_category(workspace,name) VALUES($w,$n)";cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$n",name);cmd.ExecuteNonQuery();}
        foreach(var name in dlcs.Where(n=>n.Length>0).Distinct(StringComparer.OrdinalIgnoreCase)){cmd.Parameters.Clear();cmd.CommandText="INSERT OR IGNORE INTO companion_achievement_dlc(workspace,provider,game,name) VALUES($w,$p,$g,$n)";cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$p",provider);cmd.Parameters.AddWithValue("$g",game);cmd.Parameters.AddWithValue("$n",name);cmd.ExecuteNonQuery();}
    }
    public object Game(string provider,string game)
    {
        Scope(provider,game);lock(gate){using var c=Open();using var cmd=Command(c,"SELECT updated,categories,category_order,dlcs,synced FROM companion_achievement_game WHERE workspace=$w AND provider=$p AND game=$g",provider,game);
        long updated=0,synced=0;string[] categories=[],order=[],dlcs=[];using(var row=cmd.ExecuteReader())if(row.Read()){updated=row.GetInt64(0);categories=JsonSerializer.Deserialize<string[]>(row.GetString(1))!;order=JsonSerializer.Deserialize<string[]>(row.GetString(2))!;dlcs=JsonSerializer.Deserialize<string[]>(row.GetString(3))!;synced=row.GetInt64(4);}
        cmd.CommandText="SELECT value FROM companion_achievement WHERE workspace=$w AND provider=$p AND game=$g";var saved=new List<Saved>();using(var rows=cmd.ExecuteReader())while(rows.Read())saved.Add(JsonSerializer.Deserialize<Saved>(rows.GetString(0),Json)!);
        var registered=new List<string>();cmd.CommandText="SELECT name FROM companion_achievement_category WHERE workspace=$w";using(var names=cmd.ExecuteReader())while(names.Read())registered.Add(names.GetString(0));categories=categories.Concat(registered).ToArray();
        cmd.CommandText="SELECT name FROM companion_achievement_dlc WHERE workspace=$w AND provider=$p AND game=$g";using(var names=cmd.ExecuteReader())while(names.Read())dlcs=dlcs.Append(names.GetString(0)).ToArray();
        return new{provider,gameId=game,progressUpdatedUtc=updated,syncedUtc=synced,categories=categories.Concat(saved.Select(s=>(s.Web??s.Desktop).Category)).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase),categoryOrder=order,dlcs=dlcs.Concat(saved.Select(s=>(s.Web??s.Desktop).Dlc)).Where(s=>s.Length>0).Distinct(StringComparer.OrdinalIgnoreCase),rows=saved.OrderBy(s=>s.Data.Order).Select(View)};}
    }
    public object Detail(string provider,string game,string id){Scope(provider,game);lock(gate){using var c=Open();return View(Read(c,provider,game,id));}}
    public CompanionAchievement Data(string provider,string game,string id){Scope(provider,game);lock(gate){using var c=Open();return Read(c,provider,game,id).Data;}}
    public object Receive(AchievementGameSync value)
    {
        Automatic();Scope(value.Provider,value.GameId);
        if(value.WorkspaceId!=workspace.Identity||value.Account!=AchievementAccountScope.Read(options,workspace)[value.Provider]||value.Rows is null||value.Rows.Count is <1 or >5000||value.Rows.Any(r=>r is null)||value.Rows.Select(r=>r.Id).Distinct().Count()!=value.Rows.Count||value.CapturedUtc<1||value.CapturedUtc>DateTime.UtcNow.AddMinutes(5).Ticks)throw new InvalidDataException("Invalid achievement snapshot.");
        if(value.Categories is null||value.CategoryOrder is null||value.Dlcs is null||value.Categories.Count>1000||value.CategoryOrder.Count>1000||value.Dlcs.Count>1000)throw new InvalidDataException("Invalid achievement organization.");
        foreach(var name in value.Categories.Concat(value.CategoryOrder).Concat(value.Dlcs))_=Normalize(new(name,""));
        foreach(var row in value.Rows)
        {if(row is null||string.IsNullOrWhiteSpace(row.Id)||row.Id.Length>200||row.Id.Any(char.IsControl)||string.IsNullOrWhiteSpace(row.Title)||row.Title.Length>2000||row.Description is null||row.Description.Length>20000||row.Guide is null||row.Guide.Length>500000||row.SourceUrl is null||row.SourceUrl.Length>4000||row.SourceTitle is null||row.SourceTitle.Length>1000||row.Tags is null||row.Tags.Length>10000||row.Icon is null||row.Icon.Length>4000||row.Rarity is <0 or >100||row.CounterCurrent is <0||row.CounterTarget is <0)throw new InvalidDataException("Invalid achievement.");_=Normalize(row.Groups);}
        lock(gate)
        {
            using var c=Open();using var tx=c.BeginTransaction();using var cmd=Command(c,"SELECT captured,updated FROM companion_achievement_game WHERE workspace=$w AND provider=$p AND game=$g",value.Provider,value.GameId,tx:tx);
            long captured=0,priorProgress=0;using(var game=cmd.ExecuteReader())if(game.Read()){captured=game.GetInt64(0);priorProgress=game.GetInt64(1);}
            if(captured>=value.CapturedUtc)return new{received=false};
            foreach(var row in value.Rows)
            {
                var incoming=row with{Groups=Normalize(row.Groups),Unlocked=row.ProgressKnown&&row.Unlocked};Saved? old=null;try{old=Read(c,value.Provider,value.GameId,row.Id,tx);}catch(KeyNotFoundException){}
                if(old is not null&&value.ProgressUpdatedUtc<priorProgress)incoming=incoming with{ProgressKnown=old.Data.ProgressKnown,Unlocked=old.Data.Unlocked,UnlockUtc=old.Data.UnlockUtc,Rarity=old.Data.Rarity,CounterCurrent=old.Data.CounterCurrent,CounterTarget=old.Data.CounterTarget};
                var pending=old?.Pending;var web=old?.Web;var conflict=old?.Conflict??false;var baseline=old?.Baseline;
                if(pending is not null&&web==incoming.Groups){pending=null;web=null;baseline=null;conflict=false;}
                else if(pending is not null&&baseline!=incoming.Groups)conflict=true;
                Write(c,value.Provider,value.GameId,new(incoming,incoming.Groups,web,pending,baseline,conflict),tx);
            }
            Register(c,value.Provider,value.GameId,value.Categories.Concat(value.Rows.Select(r=>r.Groups.Category)),value.Dlcs.Concat(value.Rows.Select(r=>r.Groups.Dlc)),tx);
            cmd.CommandText="INSERT INTO companion_achievement_game(workspace,provider,game,captured,updated,categories,category_order,dlcs,synced) VALUES($w,$p,$g,$c,$u,$categories,$order,$dlcs,$s) ON CONFLICT(workspace,provider,game) DO UPDATE SET captured=$c,updated=$u,categories=$categories,category_order=$order,dlcs=$dlcs,synced=$s";
            cmd.Parameters.AddWithValue("$c",value.CapturedUtc);cmd.Parameters.AddWithValue("$u",Math.Max(priorProgress,value.ProgressUpdatedUtc));cmd.Parameters.AddWithValue("$categories",JsonSerializer.Serialize(value.Categories));cmd.Parameters.AddWithValue("$order",JsonSerializer.Serialize(value.CategoryOrder));cmd.Parameters.AddWithValue("$dlcs",JsonSerializer.Serialize(value.Dlcs));cmd.Parameters.AddWithValue("$s",DateTime.UtcNow.Ticks);cmd.ExecuteNonQuery();tx.Commit();
        }
        sync.Put("achievement-check");sync.Put("achievement-error","");return new{received=true};
    }
    public object Edit(string provider,string game,string id,EditAchievementGroups edit)
    {
        Scope(provider,game);if(!Guid.TryParse(edit.RequestId,out _))throw new InvalidDataException("A group edit needs a request ID.");var groups=Normalize(edit.Groups);var receipt=new GroupReceipt(provider,game,id,groups);var digest=Hash(JsonSerializer.Serialize(new{receipt,edit},Json));
        lock(gate){using var c=Open();using var tx=c.BeginTransaction();var saved=Read(c,provider,game,id,tx);using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT digest FROM companion_achievement_receipt WHERE workspace=$w AND id=$id";cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$id",edit.RequestId);
        if(cmd.ExecuteScalar() is string previous){if(previous!=digest)throw new InvalidOperationException("This request ID was already used.");return View(saved);}
        if(edit.Version!=Version(saved))throw new InvalidOperationException("The achievement groups changed. Review the saved assignment before trying again.");
        var next=edit.UseDesktop||groups==saved.Desktop?saved with{Web=null,Pending=null,Baseline=null,Conflict=false}:saved with{Web=groups,Pending=edit.RequestId,Baseline=saved.Desktop,Conflict=false};Write(c,provider,game,next,tx);
        Register(c,provider,game,[groups.Category],[groups.Dlc],tx);
        cmd.CommandText="INSERT INTO companion_achievement_receipt(workspace,id,digest,value) VALUES($w,$id,$d,$v)";cmd.Parameters.AddWithValue("$d",digest);cmd.Parameters.AddWithValue("$v",JsonSerializer.Serialize(receipt,Json));cmd.ExecuteNonQuery();tx.Commit();return View(next);}
    }
    public IReadOnlyList<AchievementGroupChange> Changes()
    {
        Automatic();lock(gate){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT provider,game,id,value FROM companion_achievement WHERE workspace=$w";cmd.Parameters.AddWithValue("$w",workspace.Identity);using var rows=cmd.ExecuteReader();var changes=new List<AchievementGroupChange>();while(rows.Read()){var saved=JsonSerializer.Deserialize<Saved>(rows.GetString(3),Json)!;if(saved.Pending is not null&&!saved.Conflict)changes.Add(new(rows.GetString(0),rows.GetString(1),rows.GetString(2),saved.Pending,saved.Baseline!,saved.Web!));}return changes.Take(100).ToArray();}
    }
    public void Ack(AchievementGroupAck ack)
    {
        Automatic();Scope(ack.Provider,ack.GameId);var desktop=Normalize(ack.Desktop);if(ack.Status is not ("applied" or "conflict"))throw new InvalidDataException("Invalid group receipt.");
        lock(gate){using var c=Open();var saved=Read(c,ack.Provider,ack.GameId,ack.AchievementId);if(saved.Pending!=ack.Id){
        if(ack.Status=="applied"&&saved.Pending is not null&&!saved.Conflict&&saved.Baseline==saved.Desktop){using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM companion_achievement_receipt WHERE workspace=$w AND id=$id";cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$id",ack.Id);if(cmd.ExecuteScalar() is string json&&JsonSerializer.Deserialize<GroupReceipt>(json,Json)==new GroupReceipt(ack.Provider,ack.GameId,ack.AchievementId,desktop))Write(c,ack.Provider,ack.GameId,saved with{Desktop=desktop,Baseline=desktop});}return;}
        if(ack.Status=="applied"&&desktop!=saved.Web)throw new InvalidDataException("Group receipt does not match the edit.");
        Write(c,ack.Provider,ack.GameId,ack.Status=="applied"?saved with{Desktop=desktop,Data=saved.Data with{Groups=desktop},Web=null,Pending=null,Baseline=null,Conflict=false}:saved with{Desktop=desktop,Conflict=true});}
    }
    sealed record GroupReceipt(string Provider,string Game,string Id,AchievementGroups Groups);
}
