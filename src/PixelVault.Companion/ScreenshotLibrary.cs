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
    long Quota=>(options.ScreenshotLibraryEnabled?8L*1024:512L)*1024*1024;
    long? derivativeUsage;
    readonly Dictionary<string,(long Stamp,long Length,ScreenshotCatalogue Catalogue)> rawManifests=new(StringComparer.Ordinal);
    readonly Dictionary<string,(long Stamp,long Length,ScreenshotCatalogue Catalogue)> manifests=new(StringComparer.Ordinal);
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
    void Scope(string provider,string game,bool receiving=false)
    {
        if(workspace.ReadOnly||!options.ScreenshotLibraryEnabled&&options.ScreenshotPilot!=provider+":"+game)throw new KeyNotFoundException();
        if(receiving)_=library.MediaTitle(provider,game);
    }
    void Enabled(){var s=state.Settings();if(!s.AutomaticEnabled||!s.LibraryEnabled||s.WorkspaceId!=workspace.Identity)throw new InvalidOperationException("PC library sync is paused.");}
    void Validate(ScreenshotCatalogue c,bool receiving=true)
    {
        if(c.Provider is null||c.GameId is null)throw new InvalidDataException("Invalid photo scope.");
        Scope(c.Provider,c.GameId,receiving);
        if(!Regex.IsMatch(c.Provider??"","^(steam|library|retroachievements)$")||!Regex.IsMatch(c.GameId??"","^[A-Za-z0-9._-]{1,80}$")||string.IsNullOrWhiteSpace(c.Title)||c.Title.Length>250||c.Version!=1||c.WorkspaceId!=workspace.Identity||!Hash(c.GameScope)||receiving&&c.Title!=library.MediaTitle(c.Provider!,c.GameId!)
            ||c.Photos is null||receiving&&c.Photos.Count<1||c.Photos.Count>(options.ScreenshotLibraryEnabled?5000:500)||c.Eligible<c.Photos.Count||c.Eligible>100000||c.SkippedVideos<0
            ||string.IsNullOrWhiteSpace(c.DayTimeZone)||c.DayTimeZone.Length>100||c.ExportedUtcTicks<=0||c.ExportedUtcTicks>DateTime.UtcNow.AddMinutes(5).Ticks)
            throw new InvalidDataException("Invalid screenshot catalogue.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in c.Photos)
            if(!Hash(p.Id)||!ids.Add(p.Id)||!Regex.IsMatch(p.EditionId??"","^[A-Za-z0-9._-]{1,80}$")||string.IsNullOrWhiteSpace(p.Console)||p.Console.Length>80
                ||p.Group is null||p.Group.Length>200||p.Width is <1 or >40000||p.Height is <1 or >40000||!Asset(p.Thumbnail)||!Asset(p.Preview)||p.Original is not null&&!ValidOriginal(p.Original)||p.Metadata is not null&&!ValidOriginal(p.Metadata,true)
                ||p.CaptureUtcTicks<0||p.CaptureUtcTicks>DateTime.MaxValue.Ticks
                ||(p.CaptureUtcTicks==0)!=(p.CaptureDay is null)||p.CaptureDay is not null&&!DateOnly.TryParseExact(p.CaptureDay,"yyyy-MM-dd",out _))
                throw new InvalidDataException("Invalid screenshot metadata.");
        if(c.Photos.SelectMany(p=>new[]{p.Original,p.Metadata}).OfType<ScreenshotOriginal>().GroupBy(o=>o.Asset,StringComparer.Ordinal).Any(g=>g.Select(o=>o.Bytes).Distinct().Count()>1))throw new InvalidDataException("Original file sizes conflict.");
    }
    string Active=>Path.Combine(Root,"catalogue.json");
    string CatalogueRoot=>Path.Combine(Root,"catalogues");
    string ActiveFor(string provider,string game)
    {
        if(File.Exists(Active)){var c=CachedRaw(Active);if(c.Provider==provider&&c.GameId==game)return Active;}
        return Path.Combine(CatalogueRoot,Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(provider+"\n"+game))).ToLowerInvariant()+".json");
    }
    static ScreenshotCatalogue Raw(string file)=>JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(file),Json)??throw new InvalidDataException("Invalid screenshot catalogue.");
    ScreenshotCatalogue CachedRaw(string file)
    {
        var info=new FileInfo(file);if(rawManifests.TryGetValue(file,out var saved)&&saved.Stamp==info.LastWriteTimeUtc.Ticks&&saved.Length==info.Length)return saved.Catalogue;
        var c=Raw(file);rawManifests[file]=(info.LastWriteTimeUtc.Ticks,info.Length,c);return c;
    }
    string PlanPath(string id)=>Hash(id)?Path.Combine(Stage,id+".json"):throw new InvalidDataException("Invalid screenshot upload.");
    ScreenshotCatalogue Read(string file)
    {
        if(!File.Exists(file))throw new KeyNotFoundException();
        var info=new FileInfo(file);
        if(manifests.TryGetValue(file,out var saved)&&saved.Stamp==info.LastWriteTimeUtc.Ticks&&saved.Length==info.Length)return Visible(saved.Catalogue,Root);
        var c=CachedRaw(file);Validate(c,false);manifests[file]=(info.LastWriteTimeUtc.Ticks,info.Length,c);return Visible(c,Root);
    }
    void Write(string file,ScreenshotCatalogue c)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(c,Json));Protect(tmp);File.Move(tmp,file,true);manifests.Remove(file);rawManifests.Remove(file);InvalidateAvailable(options,workspace);
    }
    public ScreenshotUploadPlan Prepare(ScreenshotCatalogue c)
    {
        lock(gate)
        {
            Enabled();Validate(c);var suppressed=Removed(Root,c.Provider,c.GameId).Keys.ToArray();c=Visible(c,Root);Directory.CreateDirectory(Stage);Directory.CreateDirectory(AssetRoot);
            var active=ActiveFor(c.Provider,c.GameId);IReadOnlyList<ScreenshotPhoto> retained=[];
            if(File.Exists(active))
            {
                var old=Read(active);var incoming=c.Photos.ToDictionary(p=>p.Id,StringComparer.Ordinal);
                if(old.Photos.Any(p=>p.Original is not null&&incoming.TryGetValue(p.Id,out var next)&&next.Original is null)||old.Photos.Any(p=>p.Metadata is not null&&incoming.TryGetValue(p.Id,out var next)&&next.Metadata is null))throw new InvalidDataException("A newer photo sync tool is required to preserve original files.");
                retained=old.Photos.Where(p=>!incoming.ContainsKey(p.Id)).ToArray();
                if(retained.Count>0){var kept=old with{Photos=retained};if(!Assets(kept).All(Verified)||!Originals(kept).All(VerifiedOriginal))throw new InvalidOperationException("Previously stored photos need repair before syncing.");var merged=c.Photos.Concat(retained).ToArray();c=c with{Photos=merged,Eligible=Math.Max(c.Eligible,merged.Length)};Validate(c,false);}
                if(Digest(old)==Digest(c)&&Assets(c).All(Verified)&&Originals(c).All(VerifiedOriginal))return new(Digest(c),[],true){RetainedPhotos=retained,SuppressedPhotoIds=suppressed};
            }
            var id=Digest(c);
            // One pending manifest per game. Same-payload retries reuse already verified assets.
            foreach(var old in Directory.EnumerateFiles(Stage,"*.json"))if(Path.GetFileName(old)!=id+".json"){var prior=Raw(old);if(prior.Provider==c.Provider&&prior.GameId==c.GameId)File.Delete(old);}
            Write(PlanPath(id),c);return new(id,Assets(c).Where(a=>!Verified(a)).ToArray(),false){MissingOriginals=Originals(c).Where(o=>!VerifiedOriginal(o)).Select(o=>o.Asset).ToArray(),RetainedPhotos=retained,SuppressedPhotoIds=suppressed};
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
            derivativeUsage??=Directory.EnumerateFiles(AssetRoot).Sum(f=>new FileInfo(f).Length);var oldLength=File.Exists(file)?new FileInfo(file).Length:0;
            if(derivativeUsage-oldLength+bytes.Length>Quota)throw new InvalidOperationException("Photo preview storage limit reached.");
            var tmp=file+".upload";File.WriteAllBytes(tmp,bytes);Protect(tmp);File.Move(tmp,file,true);derivativeUsage+=bytes.Length-oldLength;
        }
    }
    public object Commit(string id)
    {
        lock(gate)
        {
            Enabled();
            if(!File.Exists(PlanPath(id))){var prior=Catalogues().FirstOrDefault(c=>Digest(c)==id);if(prior is not null&&Assets(prior).All(Verified)&&Originals(prior).All(VerifiedOriginal))return new{version=id,updated=false};throw new KeyNotFoundException();}
            var c=Read(PlanPath(id));if(Assets(c).Any(a=>!Verified(a))||Originals(c).Any(o=>!VerifiedOriginal(o)))throw new InvalidOperationException("Screenshot upload is incomplete.");
            Write(ActiveFor(c.Provider,c.GameId),c);File.Delete(PlanPath(id));manifests.Remove(PlanPath(id));rawManifests.Remove(PlanPath(id));return new{version=id,updated=true};
        }
    }
    public ScreenshotPage Page(string provider,string game,int offset=0,int limit=60,string? console=null,string? version=null)
    {
        lock(gate)
        {
            Scope(provider,game);if(offset is <0 or >100000||limit is <1 or >60||console?.Length>80)throw new InvalidDataException("Invalid screenshot page.");
            var c=Read(ActiveFor(provider,game));var digest=Digest(c);if(!string.IsNullOrEmpty(version)&&version!=digest)throw new InvalidOperationException("The photo library changed. Refresh photos to continue.");
            var photos=c.Photos.Where(p=>string.IsNullOrEmpty(console)||p.Console==console).OrderByDescending(p=>p.CaptureUtcTicks).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            return new(digest,c.Title,c.Photos.Count,c.Eligible,c.SkippedVideos,c.DayTimeZone,offset+limit<photos.Length?offset+limit:null,photos.Skip(offset).Take(limit).ToArray())
                {FilteredTotal=photos.Length,Consoles=c.Photos.GroupBy(p=>p.Console).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new ScreenshotConsole(g.Key,g.Count())).ToArray()};
        }
    }
    public ScreenshotPhoto Photo(string provider,string game,string id)
    {
        lock(gate){Scope(provider,game);return Read(ActiveFor(provider,game)).Photos.FirstOrDefault(p=>p.Id==id)??throw new KeyNotFoundException();}
    }
    public RecentScreenshots Recent(int limit=30)
    {
        lock(gate)
        {
            if(limit is <1 or >30)throw new InvalidDataException("Invalid recent-photo limit.");
            var catalogues=Catalogues();var rows=Ordered(catalogues);
            return new(TimelineVersion(catalogues),rows.Length,rows.Take(limit).ToArray());
        }
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,(DateTime Updated,IReadOnlyDictionary<string,ScreenshotLink> Links)> availability=new();
    static string AvailableKey(CompanionOptions o,TrackerWorkspace w)=>o.DataRoot+"|"+w.Identity+"|"+o.ScreenshotLibraryEnabled+"|"+o.ScreenshotPilot;
    static void InvalidateAvailable(CompanionOptions o,TrackerWorkspace w)=>availability.TryRemove(AvailableKey(o,w),out _);
    public static IReadOnlyDictionary<string,ScreenshotLink> AvailableAll(CompanionOptions o,TrackerWorkspace w)
    {
        var key=AvailableKey(o,w);if(availability.TryGetValue(key,out var saved)&&DateTime.UtcNow-saved.Updated<TimeSpan.FromSeconds(2))return saved.Links;
        var links=new Dictionary<string,ScreenshotLink>(StringComparer.Ordinal);
        if(!w.ReadOnly&&(o.ScreenshotLibraryEnabled||!string.IsNullOrEmpty(o.ScreenshotPilot)))
        {
            var root=Path.Combine(o.DataRoot,"screenshots",w.Identity.ToLowerInvariant());
            var files=(Directory.Exists(Path.Combine(root,"catalogues"))?Directory.EnumerateFiles(Path.Combine(root,"catalogues"),"*.json"):[]).Prepend(Path.Combine(root,"catalogue.json"));
            foreach(var file in files.Take(1001))if(File.Exists(file))
            {
                try{var c=Visible(Raw(file),root);if(c.Version==1&&c.WorkspaceId==w.Identity&&(o.ScreenshotLibraryEnabled||o.ScreenshotPilot==c.Provider+":"+c.GameId)&&c.Photos is {Count:>0})links[c.Provider+"\n"+c.GameId]=new(c.Provider,c.GameId,c.Photos.Count);}
                catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){ }
            }
        }
        availability[key]=(DateTime.UtcNow,links);return links;
    }
    public static ScreenshotLink? Available(CompanionOptions o,TrackerWorkspace w)=>AvailableAll(o,w).Values.FirstOrDefault();
    ScreenshotCatalogue[] Catalogues()
    {
        if(workspace.ReadOnly||!options.ScreenshotLibraryEnabled&&string.IsNullOrEmpty(options.ScreenshotPilot))return [];
        var files=(Directory.Exists(CatalogueRoot)?Directory.EnumerateFiles(CatalogueRoot,"*.json"):[]).Prepend(Active).Where(File.Exists).ToArray();
        if(files.Length>1000)throw new InvalidOperationException("Too many photo catalogues.");
        return files.Select(CachedRaw).Where(c=>options.ScreenshotLibraryEnabled||options.ScreenshotPilot==c.Provider+":"+c.GameId).Select(c=>Read(ActiveFor(c.Provider,c.GameId))).DistinctBy(c=>c.Provider+"\n"+c.GameId).ToArray();
    }
    static RecentScreenshot[] Ordered(ScreenshotCatalogue[] catalogues)=>catalogues.SelectMany(c=>c.Photos.Select(p=>new RecentScreenshot(c.Provider,c.GameId,c.Title,p))).OrderByDescending(p=>p.Photo.CaptureUtcTicks).ThenBy(p=>p.Provider,StringComparer.Ordinal).ThenBy(p=>p.GameId,StringComparer.Ordinal).ThenBy(p=>p.Photo.Id,StringComparer.Ordinal).ToArray();
    static string TimelineVersion(ScreenshotCatalogue[] catalogues)=>Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",catalogues.OrderBy(c=>c.Provider,StringComparer.Ordinal).ThenBy(c=>c.GameId,StringComparer.Ordinal).Select(Digest))))).ToLowerInvariant();
    public ScreenshotTimeline Timeline(int offset=0,int limit=60,string? console=null,string? through=null,string? version=null)
    {
        lock(gate)
        {
            if(offset is <0 or >100000||limit is <1 or >60||console?.Length>80||!string.IsNullOrEmpty(through)&&!DateOnly.TryParseExact(through,"yyyy-MM-dd",out _))throw new InvalidDataException("Invalid photo timeline page.");
            var catalogues=Catalogues();var digest=TimelineVersion(catalogues);if(!string.IsNullOrEmpty(version)&&version!=digest)throw new InvalidOperationException("The photo library changed. Refresh photos to continue.");
            var rows=Ordered(catalogues);var selected=rows.Where(p=>(string.IsNullOrEmpty(console)||p.Photo.Console==console)&&(string.IsNullOrEmpty(through)||p.Photo.CaptureDay is not null&&string.CompareOrdinal(p.Photo.CaptureDay,through)<=0)).ToArray();
            return new(digest,rows.Length,selected.Length,offset+limit<selected.Length?offset+limit:null,selected.Skip(offset).Take(limit).ToArray(),rows.GroupBy(p=>p.Photo.Console).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new ScreenshotConsole(g.Key,g.Count())).ToArray());
        }
    }
    public string Image(string provider,string game,string id,string kind)
    {
        lock(gate)
        {
            Scope(provider,game);var c=Read(ActiveFor(provider,game));var photo=c.Photos.FirstOrDefault(p=>p.Id==id)??throw new KeyNotFoundException();
            var name=kind switch{"thumbnail"=>photo.Thumbnail,"preview"=>photo.Preview,_=>throw new KeyNotFoundException()};
            var file=Path.Combine(AssetRoot,name);if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new KeyNotFoundException();return file;
        }
    }
}
