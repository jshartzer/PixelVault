using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVault.Companion;

namespace PixelVault.Companion.Export;
public static partial class ScreenshotSnapshotExporter
{
    sealed record Selection(string Seed,string Title,HashSet<string> Editions,HashSet<string> Files);
    sealed record LibrarySelection(string Provider,string Game,string Title,Selection Selection);
    public sealed record LibraryChange(string Provider,string GameId,string Signature);
    static List<LibrarySelection> LibraryPlan(string source,string workspace,GameCatalogue approved)
    {
        SQLitePCL.Batteries_V2.Init();
        if(approved.WorkspaceId!=workspace)throw new InvalidDataException("The indexed library belongs to another workspace.");
        source=Path.GetFullPath(source);
        var root=File.ReadLines(Path.Combine(source,"PixelVault.settings.ini")).First(l=>l.StartsWith("library=",StringComparison.OrdinalIgnoreCase)).Split('=',2)[1].Trim();
        var index=Path.Combine(source,"cache","pixelvault-index-"+Safe(root)+".sqlite");
        using var c=Read(index);using var query=c.CreateCommand();query.CommandText="SELECT game_id,storage_group_id FROM game_index WHERE root=$r";query.Parameters.AddWithValue("$r",root);
        var scopes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);using(var rows=query.ExecuteReader())while(rows.Read())scopes[rows.GetString(0)]=rows.GetString(1);
        var folders=File.ReadLines(Path.Combine(source,"cache","library-folders-"+Safe(root)+".cache")).Select(l=>l.Split('\t')).Where(f=>f.Length>=14&&!(f.Length>15&&f[15]=="1")).ToArray();
        var known=approved.Games.ToDictionary(g=>g.Provider+":"+g.GameId,StringComparer.Ordinal);
        var plan=new List<(string[] Seed,string Provider,string Game,string Title,string[][] Editions,HashSet<string> Files)>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var family in folders.GroupBy(f=>scopes.TryGetValue(f[0],out var group)&&group.Length>0?"storage:"+group:f[0],StringComparer.OrdinalIgnoreCase))
        {
            var files=family.SelectMany(f=>f[6].Split('|',StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(!files.Any(file=>new[]{".jpg",".jpeg",".png",".jxr",".bmp",".gif",".webp"}.Contains(Path.GetExtension(file).ToLowerInvariant())))continue;
            if(files.Any(file=>!seen.Add(file)))throw new InvalidDataException("A capture appears in more than one indexed family.");
            var candidates=family.Select(f=>(Folder:f,Identity:LibraryIdentity(f))).Where(x=>known.ContainsKey(x.Identity.Provider+":"+x.Identity.Game)).OrderBy(x=>x.Identity.Provider=="steam"?0:x.Identity.Provider=="retroachievements"?1:2).ThenBy(x=>x.Identity.Game,StringComparer.Ordinal).ToArray();
            if(candidates.Length==0)throw new InvalidDataException("A photo family has no approved NAS game identity. Refresh the PC library first.");
            var seed=candidates[0];plan.Add((seed.Folder,seed.Identity.Provider,seed.Identity.Game,known[seed.Identity.Provider+":"+seed.Identity.Game].Title,family.ToArray(),files));
        }
        return plan.GroupBy(p=>p.Provider+":"+p.Game,StringComparer.Ordinal).Select(family=>{
            var leader=family.First();return new LibrarySelection(leader.Provider,leader.Game,leader.Title,new Selection(leader.Seed[0],leader.Title,family.SelectMany(f=>f.Editions).Select(f=>f[0]).ToHashSet(StringComparer.OrdinalIgnoreCase),family.SelectMany(f=>f.Files).ToHashSet(StringComparer.OrdinalIgnoreCase)));
        }).ToList();
    }
    // Poll index rows and file stamps, never original image bytes, to discover dirty game scopes.
    public static IReadOnlyList<LibraryChange> ObserveLibrary(string source,string workspace,GameCatalogue approved,CancellationToken token=default,string? provider=null,string? gameId=null)
    {
        var plan=LibraryPlan(source,workspace,approved);
        var root=Path.GetFullPath(File.ReadLines(Path.Combine(source,"PixelVault.settings.ini")).First(l=>l.StartsWith("library=",StringComparison.OrdinalIgnoreCase)).Split('=',2)[1].Trim());
        using var c=Read(Path.Combine(source,"cache","pixelvault-index-"+Safe(root)+".sqlite"));using var cmd=c.CreateCommand();cmd.CommandText="SELECT file_path,game_id,console_label,capture_utc_ticks FROM photo_index WHERE root=$r ORDER BY file_path";cmd.Parameters.AddWithValue("$r",root);
        var rows=new Dictionary<string,(string Edition,string Console,long Ticks)>(StringComparer.OrdinalIgnoreCase);using(var reader=cmd.ExecuteReader())while(reader.Read())rows[reader.GetString(0)]=(reader.GetString(1),reader.GetString(2),reader.GetInt64(3));
        var result=new List<LibraryChange>();
        foreach(var game in plan.Where(g=>provider is null||g.Provider==provider&&g.Game==gameId))
        {
            token.ThrowIfCancellationRequested();var editions=game.Selection.Editions.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var groups=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            // Include all native scopes; the exact exported group assignment is resolved by ExportCore.
            using(var q=c.CreateCommand()){q.CommandText="SELECT game_id,storage_group_id FROM game_index WHERE root=$r";q.Parameters.AddWithValue("$r",root);using var r=q.ExecuteReader();while(r.Read())if(game.Selection.Editions.Contains(r.GetString(0))){var scope=r.GetString(1);foreach(var g in Groups(Path.Combine(source,"capture-groups.sqlite"),root,[r.GetString(0)],scope.Length>0?"storage:"+scope:r.GetString(0)))groups[r.GetString(0)+"\n"+g.Key]=g.Value;}}
            var files=new List<object>();
            foreach(var path in game.Selection.Files.Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();if(!rows.TryGetValue(path,out var row)||!game.Selection.Editions.Contains(row.Edition))throw new InvalidDataException("A current capture is not yet indexed.");
                var file=Path.GetFullPath(path);EnsureUnder(root,file,true);var info=new FileInfo(file);if(!info.Exists)throw new IOException("A current capture is unavailable.");var side=new FileInfo(file+".xmp");
                files.Add(new{file,row.Edition,row.Console,row.Ticks,info.Length,Modified=info.LastWriteTimeUtc.Ticks,Sidecar=side.Exists?side.Length:0,SidecarModified=side.Exists?side.LastWriteTimeUtc.Ticks:0});
            }
            result.Add(new(game.Provider,game.Game,Hash(JsonSerializer.Serialize(new{Version=1,workspace,root,game.Title,editions,groups,files,Zone=TimeZoneInfo.Local.Id},Json))));
        }
        return result;
    }
    public static ScreenshotCatalogue ExportLibraryGame(string source,string workspace,GameCatalogue approved,string output,string provider,string game,CancellationToken token=default)
    {
        var selected=LibraryPlan(source,workspace,approved).Single(p=>p.Provider==provider&&p.Game==game);
        var catalog=ExportCore(source,workspace,provider,game,Path.Combine(output,Hash(provider+"\n"+game)),5000,true,selected.Selection,true,token);
        if(catalog.Photos.Count!=catalog.Eligible)throw new InvalidDataException("A photo family exceeded the export limit.");return catalog;
    }
    public static IReadOnlyList<ScreenshotCatalogue> ExportLibrary(string source,string workspace,GameCatalogue approved,string output)
    {
        var plan=LibraryPlan(source,workspace,approved);var result=new List<ScreenshotCatalogue>();
        foreach(var game in plan)
        {
            var catalog=ExportCore(source,workspace,game.Provider,game.Game,Path.Combine(output,Hash(game.Provider+"\n"+game.Game)),5000,true,game.Selection);
            if(catalog.Photos.Count!=catalog.Eligible)throw new InvalidDataException("A photo family exceeded the export limit.");
            result.Add(catalog);Console.WriteLine($"Photo families prepared: {result.Count}/{plan.Count}; photos: {result.Sum(g=>g.Photos.Count)}");
        }
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"library-manifest.json"),JsonSerializer.Serialize(result.Select(c=>new{c.Provider,c.GameId,Directory=Hash(c.Provider+"\n"+c.GameId),Photos=c.Photos.Count}),Json));return result;
    }
    static (string Provider,string Game) LibraryIdentity(string[] f)
    {
        if(f[5] is "Steam" or "PC"&&int.TryParse(f[7],out var app)&&app>0)return("steam",app.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(f[5]=="Emulation"&&int.TryParse(f[13],out var ra)&&ra>0)return("retroachievements",ra.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return("library",Regex.IsMatch(f[0],"^[A-Za-z0-9._-]{1,80}$")?f[0]:Hash(f[5]+"\n"+f[2])[..24]);
    }
    static Bitmap DecodeJxr(byte[] bytes)=>PixelVault.Media.PhotoCodec.DecodeJxr(bytes);
    static byte[] ToneMapHdr(float[] linear)=>PixelVault.Media.PhotoCodec.ToneMapHdr(linear);
}
