using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelVault.Companion;

/// <summary>Canonical Native photo metadata plus durable identity aliases. Only derivatives live in the cache.</summary>
public sealed partial class LocalPhotoLibrary:IDisposable
{
    internal sealed record Configuration(int Version,string Root,string Epoch,long PreparedUtcTicks);
    internal sealed record Entry(string Provider,string Game,string Title,string Relative,ScreenshotPhoto Photo,LocalPhotoFile.Stamp? Stamp,bool Removed,long SeenUtcTicks,ScreenshotPhoto? Seed=null);
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    readonly CompanionOptions options;
    readonly TrackerWorkspace workspace;
    readonly object gate=new();
    readonly Configuration config;
    readonly Dictionary<string,(string Version,long Since)> settling=new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string,Entry> entries=[];
    HashSet<string> available=[];
    ScreenshotCatalogue[] catalogues=[];
    long expires;
    readonly FileSystemWatcher watcher;
    readonly System.Collections.Concurrent.ConcurrentQueue<string> changed=new();
    long lastSweep;
    int forceSweep=1;
    string sourceStamp="";
    SqliteConnection? indexChanges,groupChanges,identityChanges;
    IReadOnlyDictionary<string,ScreenshotLink>? links;
    string Database=>Path.Combine(options.ApplicationDataRoot,"local-photos","identities.sqlite");
    internal static string Key(Entry e)=>e.Provider+"\n"+e.Game+"\n"+e.Photo.Id;
    public LocalPhotoLibrary(CompanionOptions options,TrackerWorkspace workspace)
    {
        this.options=options;this.workspace=workspace;
        LocalApplicationData.RequireFile(Database,options.ApplicationDataRoot);
        using var c=LocalApplicationData.Read(Database);using var q=c.CreateCommand();q.CommandText="SELECT value FROM configuration WHERE id=1";
        config=JsonSerializer.Deserialize<Configuration>((string?)q.ExecuteScalar()??"",Json)??throw new InvalidDataException("Photo identities need explicit preparation.");
        if(config.Version!=1||config.Root!=LocalPhotoIndex.Root(options.ApplicationDataRoot))throw new InvalidDataException("The approved photo root changed. Prepare its identity mapping before serving photos.");
        LocalPhotoFile.CheckRoot(config.Root);
        LocalPhotoFile.CheckDestinations(config.Root,options);
        watcher=new FileSystemWatcher(config.Root){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.CreationTime|NotifyFilters.Attributes,InternalBufferSize=64*1024};
        watcher.Changed+=Changed;watcher.Created+=Changed;watcher.Deleted+=Changed;
        watcher.Renamed+=(_,e)=>{Track(e.OldFullPath);Track(e.FullPath);};
        watcher.Error+=(_,_)=>Interlocked.Exchange(ref forceSweep,1);watcher.EnableRaisingEvents=true;
    }
    void Changed(object sender,FileSystemEventArgs e)=>Track(e.FullPath);
    void Track(string path){if(changed.Count<8192)changed.Enqueue(path);else Interlocked.Exchange(ref forceSweep,1);}
    public void Dispose(){watcher.Dispose();lock(gate){indexChanges?.Dispose();groupChanges?.Dispose();identityChanges?.Dispose();}}
    string SourceStamp()
    {
        var root=options.ApplicationDataRoot;var index=LocalPhotoIndex.Database(root,config.Root);var groups=Path.Combine(root,"capture-groups.sqlite");
        foreach(var file in new[]{index,Database})LocalApplicationData.RequireFile(file,root);
        var groupVersion="missing";
        if(File.Exists(groups)){LocalApplicationData.RequireFile(groups,root);groupVersion=LocalApplicationData.DataVersion(ref groupChanges,groups).ToString(CultureInfo.InvariantCulture);}
        var files=new[]{index,groups,Database,LocalPhotoIndex.Inventory(root,config.Root),Path.Combine(root,"PixelVault.settings.ini"),Path.Combine(root,"logs","PixelVault-native.log")};
        return string.Join("|",files.Select(p=>{var f=new FileInfo(p);return f.Exists?f.Length+":"+f.LastWriteTimeUtc.Ticks:"missing";}))+"|"+LocalApplicationData.DataVersion(ref indexChanges,index)+"|"+groupVersion+"|"+LocalApplicationData.DataVersion(ref identityChanges,Database);
    }
    public IReadOnlyDictionary<string,ScreenshotLink> Links()
    {
        var saved=Volatile.Read(ref links);if(saved is not null)return saved;
        LocalApplicationData.RequireFile(Database,options.ApplicationDataRoot);using var c=LocalApplicationData.Read(Database);using var q=c.CreateCommand();
        q.CommandText="SELECT json_extract(payload,'$.provider'),json_extract(payload,'$.game'),COUNT(*) FROM photo WHERE COALESCE(json_extract(payload,'$.removed'),0)=0 GROUP BY 1,2";
        var initial=new Dictionary<string,ScreenshotLink>(StringComparer.Ordinal);using var rows=q.ExecuteReader();
        while(rows.Read()){var provider=rows.GetString(0);var game=rows.GetString(1);if(options.ScreenshotLibraryEnabled||options.ScreenshotPilot==provider+":"+game)initial.Add(provider+"\n"+game,new(provider,game,rows.GetInt32(2)));}
        return Interlocked.CompareExchange(ref links,initial,null)??initial;
    }
    internal static SqliteConnection Open(string database)
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());c.Open();using var q=c.CreateCommand();q.CommandText="PRAGMA busy_timeout=5000;PRAGMA synchronous=FULL";q.ExecuteNonQuery();return c;
    }
    internal static Dictionary<string,Entry> Load(SqliteConnection c,SqliteTransaction? tx=null)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="SELECT id,payload FROM photo";
        var result=new Dictionary<string,Entry>();using var r=q.ExecuteReader();while(r.Read())result[r.GetString(0)]=JsonSerializer.Deserialize<Entry>(r.GetString(1),Json)??throw new InvalidDataException("Invalid photo identity record.");return result;
    }
    internal static void Save(SqliteConnection c,SqliteTransaction tx,Entry e)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="INSERT INTO photo(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";q.Parameters.AddWithValue("$id",Key(e));q.Parameters.AddWithValue("$payload",JsonSerializer.Serialize(e,Json));q.ExecuteNonQuery();
    }
    void Refresh()
    {
        lock(gate)
        {
            LocalPhotoFile.CheckRoot(config.Root); // An offline root never changes identity/removal state.
            if(Environment.TickCount64<expires)return;
            var cacheStamp=SourceStamp();
            if(expires!=0&&cacheStamp==sourceStamp&&changed.IsEmpty&&settling.Count==0&&Volatile.Read(ref forceSweep)==0&&Environment.TickCount64-lastSweep<60000)
            {expires=Environment.TickCount64+1000;return;}
            try
            {
            var dirty=new HashSet<string>(StringComparer.OrdinalIgnoreCase);while(changed.TryDequeue(out var path))dirty.Add(path);
            var sweep=Interlocked.Exchange(ref forceSweep,0)!=0||Environment.TickCount64-lastSweep>=60000;
            var source=LocalPhotoIndex.Read(options.ApplicationDataRoot,new(1,workspace.Identity,DateTime.UtcNow,workspace.LocalData!.Games().Values.ToList()));
            if(source.Root!=config.Root)throw new InvalidDataException("The approved photo root changed.");
            LocalApplicationData.RequireFile(Database,options.ApplicationDataRoot);
            using var c=Open(Database);using var tx=c.BeginTransaction();var old=Load(c,tx);var next=new Dictionary<string,Entry>(old);var ready=new HashSet<string>();
            var paths=source.Rows.Select(r=>r.Provider+"\n"+r.Game+"\n"+r.Edition+"\n"+r.Relative).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var byPath=old.Values.Where(e=>e.Relative.Length>0).ToDictionary(e=>e.Provider+"\n"+e.Game+"\n"+e.Photo.EditionId+"\n"+e.Relative,StringComparer.OrdinalIgnoreCase);
            var removedIds=old.Values.Where(e=>e.Removed).Select(e=>e.Photo.Id).ToHashSet();
            var removedFiles=old.Values.Where(e=>e.Removed&&e.Stamp is not null).Select(e=>e.Stamp!.Identity).ToHashSet();
            var moved=new HashSet<string>();
            foreach(var row in source.Rows)
            {
                var pathKey=row.Provider+"\n"+row.Game+"\n"+row.Edition+"\n"+row.Relative;byPath.TryGetValue(pathKey,out var prior);
                LocalPhotoFile.Stamp stamp;
                var fullPath=Path.Combine(config.Root,row.Relative);
                var inspect=sweep||prior?.Stamp is null||prior is not null&&!available.Contains(Key(prior))||settling.ContainsKey(pathKey)||dirty.Any(p=>p.Equals(fullPath,StringComparison.OrdinalIgnoreCase)||p.Equals(fullPath+".xmp",StringComparison.OrdinalIgnoreCase)||fullPath.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase));
                try{stamp=inspect?LocalPhotoFile.Inspect(config.Root,row.Relative):prior!.Stamp!;if(stamp.Bytes is <1 or >64L*1024*1024)continue;}
                catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){continue;}
                if(prior is null)
                {
                    var aliases=old.Values.Where(e=>e.Provider==row.Provider&&e.Game==row.Game&&e.Photo.EditionId==row.Edition&&e.Stamp?.Identity==stamp.Identity&&e.Relative.Length>0&&!paths.Contains(e.Provider+"\n"+e.Game+"\n"+e.Photo.EditionId+"\n"+e.Relative)&&!moved.Contains(Key(e))&&!File.Exists(Path.Combine(config.Root,e.Relative))).Take(2).ToArray();
                    if(aliases.Length==1&&!AmbiguousMove(source.Rows,byPath,row,stamp.Identity)){prior=aliases[0];moved.Add(Key(prior));}
                }
                var id=prior?.Photo.Id??LocalPhotoIndex.Id(config.Epoch,row.Edition,row.Relative);
                var key=row.Provider+"\n"+row.Game+"\n"+id;
                if(prior is null&&old.TryGetValue(key,out var archived))prior=archived;
                if(!Settled(pathKey,stamp,prior?.Stamp))continue;
                var day=row.Ticks==0?null:TimeZoneInfo.ConvertTimeFromUtc(new DateTime(row.Ticks,DateTimeKind.Utc),TimeZoneInfo.Local).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
                var photo=new ScreenshotPhoto(id,row.Edition,row.Console,row.Ticks,day,row.Group,prior?.Photo.Width??0,prior?.Photo.Height??0,stamp.Version+".jpg",stamp.Version+".jpg")
                    {ResponsiveThumbnails=true,Original=Descriptor(row.Relative,stamp),Metadata=inspect?Sidecar(row.Relative):prior!.Photo.Metadata};
                var entry=new Entry(row.Provider,row.Game,row.Title,row.Relative,photo,stamp,(prior?.Removed??false)||removedIds.Contains(id)||removedFiles.Contains(stamp.Identity),prior?.SeenUtcTicks??DateTime.UtcNow.Ticks,prior?.Seed);
                next[key]=entry;ready.Add(key);
            }
            foreach(var pair in next.ToArray())
            {
                var e=pair.Value;
                if(e.Relative.Length>0&&!ready.Contains(pair.Key)&&source.Deleted.TryGetValue(e.Relative,out var time)&&time>=e.SeenUtcTicks&&!source.Listed.Contains(Path.Combine(config.Root,e.Relative))&&!source.Rows.Any(r=>r.Relative.Equals(e.Relative,StringComparison.OrdinalIgnoreCase))&&ConfirmedMissing(e.Relative))next[pair.Key]=e with{Removed=true};
            }
            foreach(var pair in next)if(!old.TryGetValue(pair.Key,out var previous)||previous!=pair.Value)Save(c,tx,pair.Value);
            tx.Commit();entries=next;available=ready;
            catalogues=next.Values.Where(e=>!e.Removed&&(options.ScreenshotLibraryEnabled||options.ScreenshotPilot==e.Provider+":"+e.Game)).GroupBy(e=>e.Provider+"\n"+e.Game).Select(g=>
            {
                var first=g.First();var photos=g.Select(e=>available.Contains(Key(e))?e.Photo:e.Photo with{Availability="unavailable",Original=null,Metadata=null}).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
                return new ScreenshotCatalogue(1,workspace.Identity,first.Provider,first.Game,first.Title,LocalPhotoIndex.Hash(g.Key),source.Zone,config.PreparedUtcTicks,photos.Length,source.Videos.GetValueOrDefault(first.Provider+":"+first.Game),photos);
            }).ToArray();sourceStamp=cacheStamp;Volatile.Write(ref links,catalogues.Where(c=>c.Photos.Count>0).ToDictionary(c=>c.Provider+"\n"+c.GameId,c=>new ScreenshotLink(c.Provider,c.GameId,c.Photos.Count)));expires=Environment.TickCount64+1000;if(sweep)lastSweep=Environment.TickCount64;
            }
            catch{Interlocked.Exchange(ref forceSweep,1);throw;}
        }
    }
    bool AmbiguousMove(IReadOnlyList<LocalPhotoIndex.Row> rows,Dictionary<string,Entry> known,LocalPhotoIndex.Row row,string identity)
    {
        foreach(var other in rows.Where(r=>r.Provider==row.Provider&&r.Game==row.Game&&r.Edition==row.Edition&&!r.Relative.Equals(row.Relative,StringComparison.OrdinalIgnoreCase)&&!known.ContainsKey(r.Provider+"\n"+r.Game+"\n"+r.Edition+"\n"+r.Relative)))
        {try{if(LocalPhotoFile.Inspect(config.Root,other.Relative).Identity==identity)return true;}catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException){}}
        return false;
    }
    bool ConfirmedMissing(string relative)
    {
        try{_=LocalPhotoFile.Inspect(config.Root,relative);return false;}
        catch(FileNotFoundException){return ParentsSafe(relative);}catch(DirectoryNotFoundException){return ParentsSafe(relative);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){return false;}
    }
    bool ParentsSafe(string relative)
    {
        for(var p=Path.GetDirectoryName(Path.Combine(config.Root,relative));p is not null&&p.Length>=config.Root.Length;p=Path.GetDirectoryName(p))
        {try{if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return false;}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}}
        return Directory.Exists(config.Root);
    }
    bool Settled(string key,LocalPhotoFile.Stamp stamp,LocalPhotoFile.Stamp? prior)
    {
        if(stamp.Version==prior?.Version){settling.Remove(key);return true;}
        if(!settling.TryGetValue(key,out var observation)||observation.Version!=stamp.Version){settling[key]=(stamp.Version,Environment.TickCount64);return false;}
        var ready=Environment.TickCount64-observation.Since>=1000&&stamp.Modified<DateTime.UtcNow.AddSeconds(-1).Ticks;if(ready)settling.Remove(key);return ready;
    }
    ScreenshotOriginal? Sidecar(string relative)
    {
        try{var stamp=LocalPhotoFile.Inspect(config.Root,relative+".xmp");return stamp.Bytes is >0 and <=1024*1024&&stamp.Modified<DateTime.UtcNow.AddSeconds(-1).Ticks?Descriptor(relative+".xmp",stamp):null;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){return null;}
    }
    static ScreenshotOriginal Descriptor(string relative,LocalPhotoFile.Stamp stamp)=>new(stamp.Version+Path.GetExtension(relative).ToLowerInvariant(),Path.GetFileName(relative),stamp.Bytes,LocalPhotoIndex.Types[Path.GetExtension(relative)],stamp.Modified);
    public ScreenshotCatalogue[] Catalogues(){Refresh();lock(gate)return catalogues.ToArray();}
    public ScreenshotCatalogue Catalogue(string provider,string game)=>Catalogues().FirstOrDefault(c=>c.Provider==provider&&c.GameId==game)??throw new KeyNotFoundException();
    Entry Resolve(string provider,string game,string id)
    {
        Refresh();lock(gate)
        {
            var key=provider+"\n"+game+"\n"+id;
            if(!entries.TryGetValue(key,out var e)||e.Removed||!options.ScreenshotLibraryEnabled&&options.ScreenshotPilot!=provider+":"+game)throw new KeyNotFoundException();
            if(!available.Contains(key))throw new InvalidOperationException("This photo is temporarily unavailable or still being imported.");return e;
        }
    }
    public sealed record Download(FileStream Stream,ScreenshotOriginal Original,string Digest);
    public async Task<Download> DownloadOriginal(string provider,string game,string id,string? asset,bool metadata,CancellationToken token)
    {
        var e=Resolve(provider,game,id);var descriptor=(metadata?e.Photo.Metadata:e.Photo.Original)??throw new KeyNotFoundException();
        if(!string.IsNullOrEmpty(asset)&&asset!=descriptor.Asset)throw new InvalidOperationException("The original photo changed. Refresh photos and try again.");
        var opened=LocalPhotoFile.Open(config.Root,e.Relative+(metadata?".xmp":""),true);
        try
        {
            if(Descriptor(e.Relative+(metadata?".xmp":""),opened.Stamp)!=descriptor)throw new InvalidOperationException("The original photo changed. Refresh photos and try again.");
            var digest=Convert.ToHexString(await SHA256.HashDataAsync(opened.Stream,token)).ToLowerInvariant();opened.Stream.Position=0;
            return new(opened.Stream,descriptor,digest);
        }
        catch{await opened.Stream.DisposeAsync();throw;}
    }
}
