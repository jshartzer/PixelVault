using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

// Reads Native's index and inventory; never enumerates or decodes the capture collection.
internal static class LocalPhotoIndex
{
    internal sealed record Row(string Provider,string Game,string Title,string Edition,string Console,string Relative,long Ticks,string Group);
    internal sealed record Snapshot(string Root,string Zone,IReadOnlyList<Row> Rows,IReadOnlyDictionary<string,int> Videos,HashSet<string> Listed,Dictionary<string,long> Deleted);
    internal static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static string Id(string epoch,string edition,string relative)=>Hash(epoch+"\n"+edition+"\n"+relative.Replace('\\','/').ToLowerInvariant());
    static string Safe(string value)=>Regex.Replace(Regex.Replace(value.Replace("™","").Replace("®","").Replace("©","").Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
    internal static string Root(string source)=>Path.GetFullPath(File.ReadLines(Path.Combine(source,"PixelVault.settings.ini")).First(l=>l.StartsWith("library=",StringComparison.OrdinalIgnoreCase)).Split('=',2)[1].Trim());
    internal static string Database(string source,string root)=>Path.Combine(source,"cache","pixelvault-index-"+Safe(root)+".sqlite");
    internal static string Inventory(string source,string root)=>Path.Combine(source,"cache","library-folders-"+Safe(root)+".cache");
    internal static Snapshot Read(string source,GameCatalogue approved)
    {
        var root=Root(source);LocalPhotoFile.CheckRoot(root);
        var index=Path.Combine(source,"cache","pixelvault-index-"+Safe(root)+".sqlite");
        var inventory=Path.Combine(source,"cache","library-folders-"+Safe(root)+".cache");
        LocalApplicationData.RequireFile(index,source);LocalApplicationData.RequireFile(inventory,source);
        var before=File.ReadAllText(inventory);
        var folders=before.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>l.TrimEnd('\r').Split('\t')).Where(f=>f.Length>=14&&!(f.Length>15&&f[15]=="1")).ToArray();
        using var c=LocalApplicationData.Read(index);using var tx=c.BeginTransaction(deferred:true);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT game_id,storage_group_id,platform_label FROM game_index WHERE root=$r";q.Parameters.AddWithValue("$r",root);
        var editions=new Dictionary<string,(string Storage,string Platform)>(StringComparer.OrdinalIgnoreCase);
        using(var r=q.ExecuteReader())while(r.Read())editions[r.GetString(0)]=(r.GetString(1),r.GetString(2));
        var known=approved.Games.ToDictionary(g=>g.Provider+":"+g.GameId,StringComparer.Ordinal);
        var selections=new List<(string Provider,string Game,string Title,HashSet<string> Editions,HashSet<string> Files)>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var family in folders.GroupBy(f=>editions.TryGetValue(f[0],out var e)&&e.Storage.Length>0?"storage:"+e.Storage:f[0],StringComparer.OrdinalIgnoreCase))
        {
            var files=family.SelectMany(f=>f[6].Split('|',StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!files.Any(f=>Types.ContainsKey(Path.GetExtension(f).ToLowerInvariant())))continue;
            if(files.Any(f=>!seen.Add(f)))throw new InvalidDataException("A capture has ambiguous indexed ownership.");
            var candidates=family.Select(f=>Identity(f)).Where(x=>known.ContainsKey(x.Provider+":"+x.Game)).OrderBy(x=>x.Provider=="steam"?0:x.Provider=="retroachievements"?1:2).ThenBy(x=>x.Game,StringComparer.Ordinal).ToArray();
            if(candidates.Length==0)continue;
            var lead=candidates[0];selections.Add((lead.Provider,lead.Game,known[lead.Provider+":"+lead.Game].Title,family.Select(f=>f[0]).ToHashSet(StringComparer.OrdinalIgnoreCase),files));
        }
        var input=new Dictionary<string,(string Edition,string Console,long Ticks)>(StringComparer.OrdinalIgnoreCase);
        q.CommandText="SELECT file_path,game_id,console_label,capture_utc_ticks FROM photo_index WHERE root=$r";
        using(var r=q.ExecuteReader())while(r.Read())input[r.GetString(0)]=(r.GetString(1),r.GetString(2),r.GetInt64(3));
        tx.Commit();
        var groupScopes=ReadGroups(source,root);
        var rows=new List<Row>();var videos=new Dictionary<string,int>();
        foreach(var game in selections.GroupBy(s=>s.Provider+":"+s.Game))
        {
            var lead=game.First();var ids=game.SelectMany(g=>g.Editions).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var scopes=ids.Select(id=>editions[id].Storage.Length>0?"storage:"+editions[id].Storage:id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var scope=scopes.Length==1?scopes[0]:"game:"+lead.Provider+":"+lead.Game;
            var groups=Groups(groupScopes,ids,scope);
            foreach(var other in scopes.Where(s=>s!=scope))foreach(var entry in Groups(groupScopes,[],other))groups[entry.Key]=entry.Value;
            foreach(var path in game.SelectMany(g=>g.Files).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!input.TryGetValue(path,out var photo)||!ids.Contains(photo.Edition))continue; // Import not committed in both stores yet.
                var ext=Path.GetExtension(path).ToLowerInvariant();
                if(new[]{".mp4",".mkv",".mov",".avi",".webm",".m4v"}.Contains(ext)){videos[game.Key]=videos.GetValueOrDefault(game.Key)+1;continue;}
                if(!Types.ContainsKey(ext))continue;
                var relative=LocalPhotoFile.Relative(root,path);
                if(photo.Ticks<0||photo.Ticks>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid indexed capture date.");
                rows.Add(new(lead.Provider,lead.Game,lead.Title,photo.Edition,photo.Console.Length>0?photo.Console:editions[photo.Edition].Platform,relative,photo.Ticks,groups.GetValueOrDefault(relative,"")));
            }
        }
        if(before!=File.ReadAllText(inventory))throw new IOException("The capture inventory is changing. Retry the request.");
        var listed=folders.SelectMany(f=>f[6].Split('|',StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(root,TimeZoneInfo.Local.Id,rows,videos,listed,Deletions(source,root));
    }
    static (string Provider,string Game) Identity(string[] f)
    {
        if(f[5] is "Steam" or "PC"&&int.TryParse(f[7],out var app)&&app>0)return("steam",app.ToString(CultureInfo.InvariantCulture));
        if(f[5]=="Emulation"&&int.TryParse(f[13],out var ra)&&ra>0)return("retroachievements",ra.ToString(CultureInfo.InvariantCulture));
        return("library",Regex.IsMatch(f[0],"^[A-Za-z0-9._-]{1,80}$")?f[0]:Hash(f[5]+"\n"+f[2])[..24]);
    }
    static Dictionary<string,Dictionary<string,string>> ReadGroups(string source,string root)
    {
        var result=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);var file=Path.Combine(source,"capture-groups.sqlite");if(!File.Exists(file))return result;
        LocalApplicationData.RequireFile(file,source);using var c=LocalApplicationData.Read(file);using var tx=c.BeginTransaction(deferred:true);
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT game,file,name FROM capture_group_member WHERE root=$r";q.Parameters.AddWithValue("$r",root);
        using var r=q.ExecuteReader();while(r.Read()){var scope=r.GetString(0);if(!result.TryGetValue(scope,out var members))result[scope]=members=new(StringComparer.OrdinalIgnoreCase);members[r.GetString(1)]=r.GetString(2);}
        return result;
    }
    static Dictionary<string,string> Groups(Dictionary<string,Dictionary<string,string>> scopes,IEnumerable<string> ids,string scope)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var id in ids.Order(StringComparer.OrdinalIgnoreCase).Append(scope))if(scopes.TryGetValue(id,out var members))foreach(var member in members)result[member.Key]=member.Value;
        return result;
    }
    static Dictionary<string,long> Deletions(string source,string root)
    {
        var result=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);var file=Path.Combine(source,"logs","PixelVault-native.log");if(!File.Exists(file))return result;
        LocalApplicationData.RequireFile(file,source);
        using var stream=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);
        while(reader.ReadLine() is {} line)
        {
            if(!line.Contains("] Library delete: ",StringComparison.Ordinal))continue;
            var m=Regex.Match(line,@"^\[([^\]\r\n]+)\] Library delete: (.+)$");
            if(!m.Success||!DateTimeOffset.TryParse(m.Groups[1].Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var time)||!Path.IsPathFullyQualified(m.Groups[2].Value))continue;
            try{var rel=LocalPhotoFile.Relative(root,m.Groups[2].Value);result[rel]=Math.Max(result.GetValueOrDefault(rel),time.UtcTicks);}catch(InvalidDataException){}
        }
        return result;
    }
    internal static readonly Dictionary<string,string> Types=new(StringComparer.OrdinalIgnoreCase){[".png"]="image/png",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg",[".webp"]="image/webp",[".gif"]="image/gif",[".bmp"]="image/bmp",[".jxr"]="image/vnd.ms-photo",[".xmp"]="application/rdf+xml"};
}
