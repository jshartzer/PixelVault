using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

// Isolated pilot media store. A manifest becomes visible only when every referenced derivative is present.
public sealed partial class ScreenshotLibrary(CompanionOptions options,TrackerWorkspace workspace,GameLibrary library,ConnectorSyncState state)
{
    readonly object gate=new();
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    string Root=>Path.Combine(options.DataRoot,"screenshots",workspace.Identity.ToLowerInvariant());
    string AssetRoot=>Path.Combine(Root,"assets");
    string Stage=>Path.Combine(Root,"staging");
    const long Quota=512L*1024*1024;
    static bool Hash(string? value)=>value is not null&&Regex.IsMatch(value,"^[a-f0-9]{64}$");
    static bool Asset(string? value)=>value is not null&&Regex.IsMatch(value,"^[a-f0-9]{64}\\.jpg$");
    static string[] Assets(ScreenshotCatalogue c)=>c.Photos.SelectMany(p=>new[]{p.Thumbnail,p.Preview}).Distinct(StringComparer.Ordinal).ToArray();
    static string Digest(ScreenshotCatalogue c)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(c with{ExportedUtcTicks=0},Json))).ToLowerInvariant();
    static void Protect(string file){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(file,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
    bool Verified(string name)
    {
        var file=Path.Combine(AssetRoot,name);if(!File.Exists(file))return false;
        var info=new FileInfo(file);if(info.Length is <12 or >6*1024*1024||(info.Attributes&FileAttributes.ReparsePoint)!=0)return false;
        using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()==name[..64];
    }
    void Scope(string provider,string game)
    {
        if(workspace.ReadOnly||options.ScreenshotPilot!=provider+":"+game)throw new KeyNotFoundException();
        _=library.Game(provider,game);
    }
    void Enabled(){var s=state.Settings();if(!s.AutomaticEnabled||!s.LibraryEnabled||s.WorkspaceId!=workspace.Identity)throw new InvalidOperationException("PC library sync is paused.");}
    void Validate(ScreenshotCatalogue c)
    {
        Scope(c.Provider,c.GameId);
        if(c.Version!=1||c.WorkspaceId!=workspace.Identity||!Hash(c.GameScope)||c.Title!=library.Game(c.Provider,c.GameId).Title
            ||c.Photos is null||c.Photos.Count is <1 or >500||c.Eligible<c.Photos.Count||c.Eligible>100000||c.SkippedVideos<0
            ||string.IsNullOrWhiteSpace(c.DayTimeZone)||c.DayTimeZone.Length>100||c.ExportedUtcTicks<=0||c.ExportedUtcTicks>DateTime.UtcNow.AddMinutes(5).Ticks)
            throw new InvalidDataException("Invalid screenshot catalogue.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in c.Photos)
            if(!Hash(p.Id)||!ids.Add(p.Id)||!Regex.IsMatch(p.EditionId??"","^[A-Za-z0-9._-]{1,80}$")||string.IsNullOrWhiteSpace(p.Console)||p.Console.Length>80
                ||p.Group is null||p.Group.Length>200||p.Width is <1 or >40000||p.Height is <1 or >40000||!Asset(p.Thumbnail)||!Asset(p.Preview)||p.Original is not null&&!ValidOriginal(p.Original)
                ||p.CaptureUtcTicks<0||p.CaptureUtcTicks>DateTime.MaxValue.Ticks
                ||(p.CaptureUtcTicks==0)!=(p.CaptureDay is null)||p.CaptureDay is not null&&!DateOnly.TryParseExact(p.CaptureDay,"yyyy-MM-dd",out _))
                throw new InvalidDataException("Invalid screenshot metadata.");
        if(c.Photos.Select(p=>p.Original).OfType<ScreenshotOriginal>().GroupBy(o=>o.Asset,StringComparer.Ordinal).Any(g=>g.Select(o=>o.Bytes).Distinct().Count()>1))throw new InvalidDataException("Original file sizes conflict.");
    }
    string Active=>Path.Combine(Root,"catalogue.json");
    string PlanPath(string id)=>Hash(id)?Path.Combine(Stage,id+".json"):throw new InvalidDataException("Invalid screenshot upload.");
    ScreenshotCatalogue Read(string file)
    {
        if(!File.Exists(file))throw new KeyNotFoundException();
        var c=JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(file),Json)??throw new InvalidDataException("Invalid screenshot catalogue.");Validate(c);return c;
    }
    void Write(string file,ScreenshotCatalogue c)
    {
        var tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(c,Json));Protect(tmp);File.Move(tmp,file,true);
    }
    public ScreenshotUploadPlan Prepare(ScreenshotCatalogue c)
    {
        lock(gate)
        {
            Enabled();Validate(c);Directory.CreateDirectory(Stage);Directory.CreateDirectory(AssetRoot);
            var id=Digest(c);
            if(File.Exists(Active))
            {
                var old=Read(Active);var incoming=c.Photos.ToDictionary(p=>p.Id,StringComparer.Ordinal);
                if(old.Photos.Any(p=>p.Original is not null&&incoming.TryGetValue(p.Id,out var next)&&next.Original is null))throw new InvalidDataException("A newer photo sync tool is required to preserve original files.");
                if(Digest(old)==id&&Assets(c).All(Verified)&&Originals(c).All(VerifiedOriginal))return new(id,[],true);
            }
            // One pending manifest per pilot. Same-payload retries reuse already verified assets.
            foreach(var old in Directory.EnumerateFiles(Stage,"*.json"))if(Path.GetFileName(old)!=id+".json")File.Delete(old);
            Write(PlanPath(id),c);return new(id,Assets(c).Where(a=>!Verified(a)).ToArray(),false){MissingOriginals=Originals(c).Where(o=>!VerifiedOriginal(o)).Select(o=>o.Asset).ToArray()};
        }
    }
    public void Upload(string id,string name,byte[] bytes)
    {
        lock(gate)
        {
            Enabled();var c=Read(PlanPath(id));
            if(!Asset(name)||!Assets(c).Contains(name,StringComparer.Ordinal)||bytes.Length is <12 or >6*1024*1024
                ||bytes[0]!=255||bytes[1]!=216||bytes[2]!=255||bytes[^2]!=255||bytes[^1]!=217
                ||Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()!=name[..64])throw new InvalidDataException("Invalid screenshot derivative.");
            var file=Path.Combine(AssetRoot,name);if(Verified(name))return;
            if(Directory.EnumerateFiles(AssetRoot).Sum(f=>new FileInfo(f).Length)+bytes.Length>Quota)throw new InvalidOperationException("Screenshot pilot storage limit reached.");
            var tmp=file+".upload";File.WriteAllBytes(tmp,bytes);Protect(tmp);File.Move(tmp,file,true);
        }
    }
    public object Commit(string id)
    {
        lock(gate)
        {
            Enabled();
            if(File.Exists(Active)&&Digest(Read(Active))==id&&Assets(Read(Active)).All(Verified)&&Originals(Read(Active)).All(VerifiedOriginal))return new{version=id,updated=false};
            var c=Read(PlanPath(id));if(Assets(c).Any(a=>!Verified(a))||Originals(c).Any(o=>!VerifiedOriginal(o)))throw new InvalidOperationException("Screenshot upload is incomplete.");
            Write(Active,c);File.Delete(PlanPath(id));return new{version=id,updated=true};
        }
    }
    public ScreenshotPage Page(string provider,string game,int offset=0,int limit=60,string? console=null,string? version=null)
    {
        lock(gate)
        {
            Scope(provider,game);if(offset is <0 or >100000||limit is <1 or >60||console?.Length>80)throw new InvalidDataException("Invalid screenshot page.");
            var c=Read(Active);var digest=Digest(c);if(!string.IsNullOrEmpty(version)&&version!=digest)throw new InvalidOperationException("The photo library changed. Refresh photos to continue.");
            var photos=c.Photos.Where(p=>string.IsNullOrEmpty(console)||p.Console==console).OrderByDescending(p=>p.CaptureUtcTicks).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            return new(digest,c.Title,c.Photos.Count,c.Eligible,c.SkippedVideos,c.DayTimeZone,offset+limit<photos.Length?offset+limit:null,photos.Skip(offset).Take(limit).ToArray())
                {FilteredTotal=photos.Length,Consoles=c.Photos.GroupBy(p=>p.Console).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new ScreenshotConsole(g.Key,g.Count())).ToArray()};
        }
    }
    public ScreenshotPhoto Photo(string provider,string game,string id)
    {
        lock(gate){Scope(provider,game);return Read(Active).Photos.FirstOrDefault(p=>p.Id==id)??throw new KeyNotFoundException();}
    }
    public RecentScreenshots Recent(int limit=30)
    {
        lock(gate)
        {
            if(limit is <1 or >30)throw new InvalidDataException("Invalid recent-photo limit.");
            if(workspace.ReadOnly||string.IsNullOrEmpty(options.ScreenshotPilot)||!File.Exists(Active))return new("",0,[]);
            var scope=options.ScreenshotPilot.Split(':',2);if(scope.Length!=2)return new("",0,[]);
            Scope(scope[0],scope[1]);var c=Read(Active);
            return new(Digest(c),c.Photos.Count,c.Photos.OrderByDescending(p=>p.CaptureUtcTicks).ThenBy(p=>p.Id,StringComparer.Ordinal).Take(limit).Select(p=>new RecentScreenshot(c.Provider,c.GameId,c.Title,p)).ToArray());
        }
    }
    public static ScreenshotLink? Available(CompanionOptions options,TrackerWorkspace workspace)
    {
        if(workspace.ReadOnly||string.IsNullOrEmpty(options.ScreenshotPilot))return null;
        try
        {
            var file=Path.Combine(options.DataRoot,"screenshots",workspace.Identity.ToLowerInvariant(),"catalogue.json");
            if(!File.Exists(file))return null;
            var c=JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(file),Json);
            return c is not null&&c.Version==1&&c.WorkspaceId==workspace.Identity&&options.ScreenshotPilot==c.Provider+":"+c.GameId&&c.Photos is {Count:>0}?new(c.Provider,c.GameId,c.Photos.Count):null;
        }
        catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    public string Image(string provider,string game,string id,string kind)
    {
        lock(gate)
        {
            Scope(provider,game);var c=Read(Active);var photo=c.Photos.FirstOrDefault(p=>p.Id==id)??throw new KeyNotFoundException();
            var name=kind switch{"thumbnail"=>photo.Thumbnail,"preview"=>photo.Preview,_=>throw new KeyNotFoundException()};
            var file=Path.Combine(AssetRoot,name);if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new KeyNotFoundException();return file;
        }
    }
}
