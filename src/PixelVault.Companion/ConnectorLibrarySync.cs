using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

public sealed class ConnectorLibrarySync(CompanionOptions options,TrackerWorkspace workspace,GameLibrary library,ConnectorSyncState state)
{
    readonly object gate=new();static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    string Root=>Path.Combine(options.DataRoot,"library");
    string Stage=>Path.Combine(options.DataRoot,"library-staging");
    static bool Asset(string name)=>Regex.IsMatch(name,"^[a-f0-9]{64}\\.(jpg|png|webp)$");
    static string Digest(GameCatalogue catalog)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalog.WorkspaceId+JsonSerializer.Serialize(catalog.Games,Json)))).ToLowerInvariant();
    static string[] Assets(GameCatalogue catalog)=>catalog.Games.SelectMany(g=>new[]{g.Cover,g.Banner}).Where(a=>a is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
    void RequireEnabled()
    {
        var settings=state.Settings();if(!settings.AutomaticEnabled||!settings.LibraryEnabled||settings.WorkspaceId!=workspace.Identity)throw new InvalidOperationException("Automatic library updates are not enabled.");
    }
    static void Protect(string file){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
    string PlanPath(string id)=>Regex.IsMatch(id,"^[a-f0-9]{64}$")?Path.Combine(Stage,id+".json"):throw new InvalidDataException("Invalid library upload.");
    GameCatalogue Plan(string id)
    {
        var file=PlanPath(id);if(!File.Exists(file))throw new KeyNotFoundException();
        var catalog=JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(file),Json)??throw new InvalidDataException("Invalid library upload.");library.ValidateIncoming(catalog);return catalog;
    }
    public LibraryUploadPlan Prepare(GameCatalogue catalog)
    {
        lock(gate)
        {
            RequireEnabled();library.ValidateIncoming(catalog);
            var id=Digest(catalog);var active=Path.Combine(Root,"catalog.json");
            if(File.Exists(active))
            {
                var old=JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(active),Json);
                if(old is not null&&Digest(old)==id){state.Put("library-check");state.Put("library-error","");return new(id,[],true);}
            }
            Directory.CreateDirectory(Stage);Directory.CreateDirectory(Path.Combine(Root,"assets"));
            var plan=PlanPath(id);File.WriteAllText(plan,JsonSerializer.Serialize(catalog,Json));Protect(plan);
            foreach(var expired in Directory.EnumerateFiles(Stage,"*.json").Where(f=>Regex.IsMatch(Path.GetFileName(f),"^[a-f0-9]{64}\\.json$")&&File.GetLastWriteTimeUtc(f)<DateTime.UtcNow.AddDays(-1)))File.Delete(expired);
            return new(id,Assets(catalog).Where(a=>!File.Exists(Path.Combine(Root,"assets",a))).ToArray(),false);
        }
    }
    public void Upload(string id,string name,byte[] bytes)
    {
        lock(gate)
        {
            RequireEnabled();var catalog=Plan(id);
            if(!Asset(name)||!Assets(catalog).Contains(name,StringComparer.Ordinal)||bytes.Length<12||bytes.Length>2*1024*1024
                ||Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=name[..64])throw new InvalidDataException("Invalid library artwork.");
            var valid=Path.GetExtension(name) switch{
                ".jpg"=>bytes[0]==255&&bytes[1]==216&&bytes[2]==255,
                ".png"=>bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
                ".webp"=>Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&Encoding.ASCII.GetString(bytes,8,4)=="WEBP",_=>false};
            if(!valid)throw new InvalidDataException("Unsupported artwork content.");
            var assets=Path.Combine(Root,"assets");var file=Path.Combine(assets,name);if(File.Exists(file))return;
            if(Directory.EnumerateFiles(assets).Sum(f=>new FileInfo(f).Length)+bytes.Length>500L*1024*1024)throw new InvalidDataException("The private artwork cache needs maintenance.");
            var temp=file+".upload-"+Guid.NewGuid().ToString("N");File.WriteAllBytes(temp,bytes);Protect(temp);File.Move(temp,file);
        }
    }
    public object Commit(string id)
    {
        lock(gate)
        {
            RequireEnabled();var catalog=Plan(id);
            if(Assets(catalog).Any(a=>!File.Exists(Path.Combine(Root,"assets",a))))throw new InvalidOperationException("Library artwork is still uploading.");
            var file=Path.Combine(Root,"catalog.json");var temp=Path.Combine(Root,"catalog-"+Guid.NewGuid().ToString("N")+".tmp");
            File.WriteAllText(temp,JsonSerializer.Serialize(catalog,Json));Protect(temp);File.Move(temp,file,true);library.Reload();
            state.Put("library-check");state.Put("library-update");state.Put("library-error","");File.Delete(PlanPath(id));
            return new{games=catalog.Games.Count,updated=true};
        }
    }
}
