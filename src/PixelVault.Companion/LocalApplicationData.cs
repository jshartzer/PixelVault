using Microsoft.Data.Sqlite;
using PixelVault.Companion.Local;
using PixelVaultNative;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

/// <summary>Read-through projections of canonical PC stores. No exported authority or Native UI dependency.</summary>
public sealed class LocalApplicationData:IDisposable
{
    readonly CompanionOptions options;
    readonly TrackerWorkspace workspace;
    readonly object gate=new();
    readonly Dictionary<string,string> accounts;
    readonly string root,library,index,folders,guide;
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    string revision="";
    string sourceStamp="";
    long reconcileAt;
    SqliteConnection? indexChanges,guideChanges;
    readonly DirectoryStamp coverStamp;
    Dictionary<string,AchievementGameSync> achievements=[];
    Dictionary<string,LibraryGame> games=[];
    Dictionary<string,string> artwork=[];
    public string Revision {get{Refresh();return revision;}}
    public LocalApplicationData(CompanionOptions options,TrackerWorkspace workspace)
    {
        this.options=options;this.workspace=workspace;
        if(!workspace.DirectLocal)throw new InvalidOperationException("A direct-local workspace is required.");
        root=options.ApplicationDataRoot;accounts=AchievementAccountScope.Read(options,workspace);coverStamp=new(Path.Combine(root,"cache","covers"));
        var settings=File.ReadLines(Path.Combine(root,"PixelVault.settings.ini")).Where(s=>s.Contains('=')).Select(s=>s.Split('=',2)).ToDictionary(p=>p[0].Trim(),p=>p[1].Trim(),StringComparer.OrdinalIgnoreCase);
        library=settings.GetValueOrDefault("library")??throw new InvalidDataException("The PC library is not configured.");
        if(!Path.IsPathFullyQualified(library))throw new InvalidDataException("The library root must be absolute.");
        index=Path.Combine(root,"cache","pixelvault-index-"+Safe(library)+".sqlite");
        folders=Path.Combine(root,"cache","library-folders-"+Safe(library)+".cache");guide=Path.Combine(root,"guides","pixelvault-guides.sqlite");
        foreach(var path in new[]{index,folders,guide})RequireFile(path,root);
        using(var c=OpenGuide()){using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name IN ('achievement_group_revision','achievement_group_request','achievement_group_revision_update')";if(Convert.ToInt32(q.ExecuteScalar())!=3)throw new InvalidDataException("The guide store needs preparation by the schema owner.");}
        try{Refresh();}catch{Dispose();throw;}
    }
    public void Dispose(){lock(gate){indexChanges?.Dispose();guideChanges?.Dispose();coverStamp.Dispose();}}
    internal static long DataVersion(ref SqliteConnection? connection,string path)
    {
        connection??=Read(path);using var q=connection.CreateCommand();q.CommandText="PRAGMA data_version";return Convert.ToInt64(q.ExecuteScalar());
    }
    public static SqliteConnection Read(string path)
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();return c;
    }
    SqliteConnection OpenGuide()
    {
        RequireFile(guide,root);var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=guide,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());c.Open();using var q=c.CreateCommand();q.CommandText="PRAGMA busy_timeout=5000";q.ExecuteNonQuery();return c;
    }
    public static void RequireFile(string file,string approvedRoot)
    {
        file=Path.GetFullPath(file);approvedRoot=Path.GetFullPath(approvedRoot);
        var relative=Path.GetRelativePath(approvedRoot,file);
        if(Path.IsPathRooted(relative)||relative==".."||relative.StartsWith(".."+Path.DirectorySeparatorChar)||!File.Exists(file))throw new IOException("An approved local application file is unavailable.");
        for(var p=file;p is not null&&p.Length>=approvedRoot.Length;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked local application files are not supported.");
    }
    string Stamp()
    {
        foreach(var file in new[]{index,folders,guide})RequireFile(file,root);
        var files=new[]{index,index+"-wal",guide,guide+"-wal",folders,Path.Combine(root,"xbox-library.json"),Path.Combine(root,"game-profile-platforms.json"),Path.Combine(root,"local-provider-summaries.json"),Path.Combine(root,"PixelVault.settings.ini")}
            .Concat(Directory.EnumerateFiles(Path.Combine(root,"achievement-hub"),"*.json"));
        return Hash(string.Join("\n",files.Order(StringComparer.Ordinal).Select(p=>{var f=new FileInfo(p);return p+"|"+(f.Exists?f.Length:0)+"|"+(f.Exists?f.LastWriteTimeUtc.Ticks:0);}))+"\n"+coverStamp.Read()+"\n"+DataVersion(ref indexChanges,index)+"\n"+DataVersion(ref guideChanges,guide));
    }
    void Refresh()
    {
        lock(gate)
        {
            for(var attempt=0;attempt<3;attempt++)
            {
                var stamp=Stamp();if(stamp==sourceStamp&&Environment.TickCount64<reconcileAt)return;
                // This also checks that configured Native provider accounts agree with the approved web scope.
                var approved=LocalAchievementReader.ConnectedAccounts(root,accounts);
                if(approved.Count!=accounts.Count||approved.Any(p=>!accounts.TryGetValue(p.Key,out var a)||!string.Equals(a,p.Value,p.Key=="steam"?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("The local account scope changed.");
                var nextAchievements=LocalAchievementReader.Read(root,workspace.Identity,accounts).ToDictionary(g=>g.Provider+":"+g.GameId);
                var nextArt=new Dictionary<string,string>();var nextGames=ReadGames(nextAchievements,nextArt);
                if(stamp!=Stamp())continue;
                achievements=nextAchievements;games=nextGames;artwork=nextArt;sourceStamp=stamp;
                // Source stamps invalidate immediately. Periodically reconcile even an unchanged
                // stamp to cover missed filesystem timestamp changes without rebuilding on every poll.
                revision=Hash(JsonSerializer.Serialize(new{games=nextGames.OrderBy(p=>p.Key),achievements=nextAchievements.OrderBy(p=>p.Key).Select(p=>new{p.Key,p.Value.Rows,p.Value.ProgressUpdatedUtc,p.Value.Categories,p.Value.CategoryOrder,p.Value.Dlcs})},Json));
                reconcileAt=Environment.TickCount64+60000;return;
            }
            throw new IOException("Local application data is changing. Retry the request.");
        }
    }
    Dictionary<string,LibraryGame> ReadGames(Dictionary<string,AchievementGameSync> snapshots,Dictionary<string,string> art)
    {
        var result=new Dictionary<string,LibraryGame>();
        var summaries=new LocalProviderSummaryStore(root).Read();
        var saved=summaries.Where(s=>accounts.TryGetValue(s.Provider,out var account)&&account==s.Account).ToDictionary(s=>s.Provider+":"+s.GameId);
        using var c=Read(index);using var q=c.CreateCommand();q.CommandText="SELECT file_path,capture_utc_ticks FROM photo_index WHERE root=$r";q.Parameters.AddWithValue("$r",library);
        var dates=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);using(var r=q.ExecuteReader())while(r.Read())dates[r.GetString(0)]=r.GetInt64(1);
        q.CommandText="PRAGMA table_info(game_index)";
        var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);using(var r=q.ExecuteReader())while(r.Read())columns.Add(r.GetString(1));
        var identityColumns=new[]{"igdb_id","igdb_platforms"}.Select(name=>columns.Contains(name)?name:"''");
        q.CommandText="SELECT game_id,folder_path,name,platform_label,steam_app_id,retro_achievements_game_id,file_count,"+string.Join(",",identityColumns)+" FROM game_index WHERE root=$r";
        var indexedGames=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
        var captureLinks=new List<EditionGrouping.Capture>();
        using(var r=q.ExecuteReader())while(r.Read())indexedGames[r.GetString(0)]=Enumerable.Range(1,8).Select(i=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i),System.Globalization.CultureInfo.InvariantCulture)??"").ToArray();
        foreach(var line in File.ReadLines(folders))
        {
            var f=line.Split('\t');if(f.Length<14||f.Length>15&&f[15]=="1"||string.IsNullOrWhiteSpace(f[2]))continue;
            if(!indexedGames.TryGetValue(f[0],out var indexed))continue;
            f[1]=indexed[0];f[2]=indexed[1];f[5]=indexed[2];f[7]=indexed[3];f[13]=indexed[4];f[3]=indexed[5];
            var provider="library";var id=f[0];
            if(f[5] is "Steam" or "PC"&&int.TryParse(f[7],out var steam)&&steam>0){provider="steam";id=steam.ToString(System.Globalization.CultureInfo.InvariantCulture);}
            else if(f[5]=="Emulation"&&int.TryParse(f[13],out var retro)&&retro>0){provider="retroachievements";id=retro.ToString(System.Globalization.CultureInfo.InvariantCulture);}
            if(!Regex.IsMatch(id,"^[A-Za-z0-9._-]{1,80}$"))id=Hash(f[5]+"\n"+f[2])[..24];
            var key=provider+":"+id;captureLinks.Add(new(f[0],key,indexed[6],indexed[7]));if(result.ContainsKey(key))continue;
            snapshots.TryGetValue(key,out var achievement);saved.TryGetValue(key,out var summary);
            var latest=f[6].Split('|').Where(p=>new[]{".png",".jpg",".jpeg",".jxr",".webp",".bmp",".gif"}.Contains(Path.GetExtension(p).ToLowerInvariant())).Select(p=>dates.GetValueOrDefault(p)).DefaultIfEmpty().Max();
            var custom=Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(f[1]+"|"+f[5]+"|"+f[2]))).ToLowerInvariant();
            string? Art(string kind)
            {
                var prefix=kind=="cover"?"custom-":"custom-hero-";var normal=kind=="cover"?Safe(f[2]):"hero-"+Safe(f[2]);
                var file=new[]{prefix+custom,normal}.SelectMany(name=>new[]{".jpg",".jpeg",".png",".webp",".bmp",".gif"}.Select(ext=>Path.Combine(root,"cache","covers",name+ext))).FirstOrDefault(File.Exists);
                if(file is null)return null;RequireFile(file,root);art[key+":"+kind]=file;var info=new FileInfo(file);return Hash(file+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks)+Path.GetExtension(file).ToLowerInvariant();
            }
            result[key]=new(provider,id,f[2][..Math.Min(f[2].Length,250)],f[5],Art("cover"),Art("banner"),new(summary?.PlaytimeMinutes,
                achievement?.Rows.All(r=>r.ProgressKnown)==true?achievement.Rows.Count(r=>r.Unlocked):null,achievement?.Rows.Count,summary?.LastPlayedUtc,achievement?.ProgressUpdatedUtc,
                int.TryParse(f[3],out var count)?count:null,latest>0?latest:null));
        }
        foreach(var xbox in LocalAchievementReader.XboxGames(root,accounts,snapshots.Values.ToArray()))result["xbox:"+xbox.GameId]=xbox;
        EditionGrouping.Apply(result,captureLinks,EditionGrouping.ReadPreferences(root));
        return result;
    }
    public IReadOnlyDictionary<string,LibraryGame> Games(){Refresh();lock(gate)return new Dictionary<string,LibraryGame>(games);}
    public IReadOnlyList<AchievementGameSync> Achievements(){Refresh();lock(gate)return achievements.Values.ToArray();}
    public AchievementGameSync Game(string provider,string id){Refresh();lock(gate)return achievements.GetValueOrDefault(provider+":"+id)??new(workspace.Identity,provider,id,accounts.GetValueOrDefault(provider)??"",0,0,[],[],[],[]);}
    public AchievementGroupState GroupState(string provider,string game,string id)
    {
        using var c=OpenGuide();return AchievementGroupCommands.Read(c,null,AchievementId(c,null,provider,game,id));
    }
    public IReadOnlyDictionary<string,AchievementGroupState> GroupStates(string provider,string game)
    {
        if(!accounts.ContainsKey(provider))throw new KeyNotFoundException();
        RequireFile(guide,root);using var c=Read(guide);
        using var q=c.CreateCommand();q.CommandText="""
            SELECT d.provider_achievement_id,d.group_name,d.dlc_name,d.group_sort_order,COALESCE(v.revision,'')
            FROM achievement_definition d LEFT JOIN achievement_group_revision v USING(achievement_id)
            WHERE d.provider=$p AND d.provider_game_id=$g AND d.is_active=1
            """;q.Parameters.AddWithValue("$p",provider);q.Parameters.AddWithValue("$g",game);
        var result=new Dictionary<string,AchievementGroupState>(StringComparer.Ordinal);using var rows=q.ExecuteReader();
        while(rows.Read())
        {
            var groups=AchievementGroupCommands.Normalize(new(rows.GetString(1),rows.GetString(2),rows.GetInt32(3)));
            result.Add(rows.GetString(0),new(groups,AchievementGroupCommands.Version(groups,rows.GetString(4))));
        }
        return result;
    }
    long AchievementId(SqliteConnection c,SqliteTransaction? tx,string provider,string game,string id)
    {
        if(!accounts.ContainsKey(provider))throw new KeyNotFoundException();
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT achievement_id FROM achievement_definition WHERE provider=$p AND provider_game_id=$g AND provider_achievement_id=$id AND is_active=1";q.Parameters.AddWithValue("$p",provider);q.Parameters.AddWithValue("$g",game);q.Parameters.AddWithValue("$id",id);return q.ExecuteScalar() is long result?result:throw new KeyNotFoundException();
    }
    public AchievementGroupState EditGroups(string provider,string game,string id,EditAchievementGroups edit)
    {
        if(edit is null||edit.Groups is null||string.IsNullOrWhiteSpace(edit.Version))throw new InvalidDataException("The saved group version is required.");
        using var c=OpenGuide();
        var backups=Path.Combine(root,"guides","backups");Directory.CreateDirectory(backups);
        using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(backups,"before-local-groups-"+Guid.NewGuid().ToString("N")+".sqlite"),Pooling=false}.ToString())){backup.Open();c.BackupDatabase(backup);}
        using var tx=c.BeginTransaction();var nativeId=AchievementId(c,tx,provider,game,id);
        var current=AchievementGroupCommands.Read(c,tx,nativeId);
        var desired=edit.UseDesktop?current.Groups:new AchievementGroupAssignment(edit.Groups.Category,edit.Groups.Dlc,edit.Groups.SortOrder);
        var result=AchievementGroupCommands.Write(c,tx,nativeId,desired,edit.Version,edit.RequestId);tx.Commit();
        foreach(var stale in Directory.EnumerateFiles(backups,"before-local-groups-*.sqlite").OrderByDescending(File.GetLastWriteTimeUtc).Skip(12))File.Delete(stale);
        return result;
    }
    public (string Path,string Type) Artwork(string provider,string game,string kind)
    {
        Refresh();lock(gate){var path=artwork.GetValueOrDefault(provider+":"+game+":"+kind)??throw new KeyNotFoundException();RequireFile(path,root);return(path,Path.GetExtension(path).ToLowerInvariant() switch{".jpg" or ".jpeg"=>"image/jpeg",".png"=>"image/png",".webp"=>"image/webp",".bmp"=>"image/bmp",_=>"image/gif"});}
    }
    static string Safe(string title)=>Regex.Replace(Regex.Replace(title.Replace("™","").Replace("®","").Replace("©","").Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
