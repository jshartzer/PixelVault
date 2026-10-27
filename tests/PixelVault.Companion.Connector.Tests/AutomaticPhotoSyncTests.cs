using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVault.Companion.Connector;
using PixelVault.Companion.Export;
using Xunit;

public sealed class AutomaticPhotoSyncTests
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static string SharedRead(string path){using var stream=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
    sealed class Fixture:IDisposable
    {
        public readonly string Directory=Path.Combine(Path.GetTempPath(),"AutomaticPhotos-"+Guid.NewGuid().ToString("N"));
        public string Source=>Path.Combine(Directory,"source");public string Captures=>Path.Combine(Directory,"captures");public string Data=>Path.Combine(Directory,"private");public string Output=>Path.Combine(Data,"screenshot-library");
        public string Index="",Folders="";public GameCatalogue Approved;public ConnectorPairing Config;
        readonly List<string> paths=[];
        public Fixture()
        {
            System.IO.Directory.CreateDirectory(Path.Combine(Source,"cache"));System.IO.Directory.CreateDirectory(Captures);System.IO.Directory.CreateDirectory(Path.Combine(Data,"library-cache"));
            File.WriteAllText(Path.Combine(Source,"PixelVault.settings.ini"),"library="+Captures);
            var safe=Regex.Replace(Regex.Replace(Captures.Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
            Index=Path.Combine(Source,"cache","pixelvault-index-"+safe+".sqlite");Folders=Path.Combine(Source,"cache","library-folders-"+safe+".cache");
            SQL(Index,"CREATE TABLE game_index(root TEXT,game_id TEXT,name TEXT,platform_label TEXT,steam_app_id TEXT,storage_group_id TEXT);CREATE TABLE photo_index(root TEXT,file_path TEXT,game_id TEXT,console_label TEXT,capture_utc_ticks INTEGER);INSERT INTO game_index VALUES($r,'G1','Game','Steam','10','SG1');");
            SQL(Path.Combine(Source,"capture-groups.sqlite"),"CREATE TABLE capture_group_member(root TEXT,game TEXT,file TEXT,name TEXT);");
            Approved=new(1,new string('A',64),DateTime.UtcNow,[new("steam","10","Game","Steam",null,null,new())]);
            Config=new("http://localhost/",Approved.WorkspaceId,new string('B',64),[],new(),Source);
            File.WriteAllText(Path.Combine(Data,"library-cache","catalog.json"),JsonSerializer.Serialize(Approved,Json));Add("first.png");
        }
        public void SQL(string file,string sql,string? path=null)
        {
            using var c=new SqliteConnection($"Data Source={file};Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$r",Captures);if(path is not null)cmd.Parameters.AddWithValue("$f",path);cmd.ExecuteNonQuery();
        }
        public string Add(string name)
        {
            var path=Path.Combine(Captures,name);using(var bitmap=new Bitmap(20+paths.Count,12)){using(var g=Graphics.FromImage(bitmap))g.Clear(paths.Count==0?Color.Green:Color.Red);bitmap.Save(path,ImageFormat.Png);}
            SQL(Index,"INSERT INTO photo_index VALUES($r,$f,'G1','Steam',638900000000000000)",path);paths.Add(path);Inventory();return path;
        }
        public void Inventory(){var fields=Enumerable.Repeat("",16).ToArray();fields[0]="G1";fields[1]=Captures;fields[2]="Game";fields[5]="Steam";fields[6]=string.Join('|',paths);fields[7]="10";File.WriteAllText(Folders,string.Join('\t',fields));}
        public void Group()=>SQL(Path.Combine(Source,"capture-groups.sqlite"),"INSERT INTO capture_group_member VALUES($r,'storage:SG1','first.png','Favorites')");
        public void Remove(string name,bool nativeSuccess=true)
        {
            var path=Path.Combine(Captures,name);File.Delete(path);paths.Remove(path);Inventory();SQL(Index,"DELETE FROM photo_index WHERE root=$r AND file_path=$f",path);
            System.IO.Directory.CreateDirectory(Path.Combine(Source,"logs"));File.AppendAllText(Path.Combine(Source,"logs","PixelVault-native.log"),"[2026-10-27T02:00:00.000Z] "+(nativeSuccess?"Library delete: ":"Library delete failed for ")+path+"\n");
        }
        public ScreenshotCatalogue Export()=>ScreenshotSnapshotExporter.ExportLibraryGame(Source,Approved.WorkspaceId,Approved,Output,"steam","10");
        public string Signature()=>ScreenshotSnapshotExporter.ObserveLibrary(Source,Approved.WorkspaceId,Approved).Single().Signature;
        public string GameOutput=>Path.Combine(Output,Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("steam\n10"))).ToLowerInvariant());
        public void Dispose()=>System.IO.Directory.Delete(Directory,true);
    }
    sealed class Nas:HttpMessageHandler
    {
        public volatile bool Enabled=true;public bool FailOriginal;public int Uploads,Downloads,Commits,Prepares;public ScreenshotCatalogue? Catalogue;
        public readonly Dictionary<string,byte[]> Bytes=new();public readonly List<string> ReadNames=[];ScreenshotCatalogue? pending;string[] missingAssets=[],missingOriginals=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var path=request.RequestUri!.AbsolutePath;
            HttpResponseMessage Ok(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value,options:Json)};
            if(path.EndsWith("/settings"))return Ok(new ConnectorSyncSettings(Enabled,true,new string('A',64)));
            if(path.EndsWith("/capabilities"))return Ok(new{libraryEnabled=true,metadata=true,retention=true});
            if(!Enabled)return new(HttpStatusCode.Forbidden);
            if(path.EndsWith("/prepare"))
            {
                Prepares++;pending=await request.Content!.ReadFromJsonAsync<ScreenshotCatalogue>(Json,token);
                missingAssets=pending!.Photos.SelectMany(p=>new[]{p.Preview,p.Thumbnail}).Distinct().Where(n=>!Bytes.ContainsKey(n)).ToArray();
                missingOriginals=pending.Photos.SelectMany(p=>new[]{p.Original,p.Metadata}).OfType<ScreenshotOriginal>().Select(o=>o.Asset).Distinct().Where(n=>!Bytes.ContainsKey(n)).ToArray();
                return Ok(new ScreenshotUploadPlan("plan",missingAssets,false){MissingOriginals=missingOriginals});
            }
            if(request.Method==HttpMethod.Post&&(path.Contains("/assets/")||path.Contains("/originals/")))
            {
                if(FailOriginal&&path.Contains("/originals/"))return new(HttpStatusCode.ServiceUnavailable);
                var name=path.Split('/').Last();var bytes=await request.Content!.ReadAsByteArrayAsync(token);Assert.Equal(name[..64],Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());Bytes[name]=bytes;Uploads++;return Ok(new{ok=true});
            }
            if(path.EndsWith("/commit")){Catalogue=pending;Commits++;return Ok(new{ok=true});}
            if(path.EndsWith("/steam/10"))return Ok(new ScreenshotPage("v","Game",Catalogue!.Photos.Count,Catalogue.Eligible,0,Catalogue.DayTimeZone,null,Catalogue.Photos));
            var parts=path.Split('/');var p=Catalogue!.Photos.Single(p=>p.Id==parts[^2]);var kind=parts[^1];var original=kind=="original"?p.Original:kind=="metadata"?p.Metadata:null;var name2=original?.Asset??(kind=="preview"?p.Preview:p.Thumbnail);ReadNames.Add(name2);Downloads++;
            var content=new ByteArrayContent(Bytes[name2]);content.Headers.ContentType=new(original?.ContentType??"image/jpeg");return new(HttpStatusCode.OK){Content=content};
        }
    }
    [Fact]
    public void ObservationDetectsIndexedFilesGroupsDatesAndSidecarsWithoutReadingLockedOriginals()
    {
        using var f=new Fixture();var first=f.Signature();using(var locked=File.Open(Path.Combine(f.Captures,"first.png"),FileMode.Open,FileAccess.Read,FileShare.None))Assert.Equal(first,f.Signature());
        f.Group();var grouped=f.Signature();Assert.NotEqual(first,grouped);File.WriteAllText(Path.Combine(f.Captures,"first.png.xmp"),"<xmp>tags</xmp>");var tagged=f.Signature();Assert.NotEqual(grouped,tagged);
        f.SQL(f.Index,"UPDATE photo_index SET capture_utc_ticks=capture_utc_ticks+1 WHERE root=$r");Assert.NotEqual(tagged,f.Signature());f.Add("new.png");Assert.Equal(2,f.Export().Photos.Count);
    }
    [Fact]
    public void IncrementalExportReusesVerifiedUnchangedOriginalAndConvertsNewCapture()
    {
        using var f=new Fixture();var sidecar=Path.Combine(f.Captures,"first.png.xmp");File.WriteAllText(sidecar,"<xmp>keep archived metadata</xmp>");var old=f.Export();File.Delete(sidecar);f.Add("new.png");f.Group();
        using(var locked=File.Open(Path.Combine(f.Captures,"first.png"),FileMode.Open,FileAccess.Read,FileShare.None))
        {var next=f.Export();Assert.Equal(2,next.Photos.Count);var retained=next.Photos.Single(p=>p.Id==old.Photos[0].Id);Assert.Equal(old.Photos[0].Original,retained.Original);Assert.Equal(old.Photos[0].Metadata,retained.Metadata);Assert.Equal(old.Photos[0].Preview,retained.Preview);Assert.Equal("Favorites",retained.Group);}
        using(var canceled=new CancellationTokenSource()){canceled.Cancel();Assert.Throws<OperationCanceledException>(()=>ScreenshotSnapshotExporter.ExportLibraryGame(f.Source,f.Approved.WorkspaceId,f.Approved,f.Output,"steam","10",canceled.Token));}
    }
    [Fact]
    public async Task DeltaPublicationReadsBackNewFilesOnlyAndChecksCompleteMetadata()
    {
        using var f=new Fixture();using var nas=new Nas();using var http=new HttpClient(nas){BaseAddress=new Uri("http://localhost/")};var sidecar=Path.Combine(f.Captures,"first.png.xmp");File.WriteAllText(sidecar,"<xmp>before</xmp>");var old=f.Export();await ScreenshotPilot.Send(http,old,f.GameOutput,false);var oldNames=nas.Bytes.Keys.ToHashSet();
        f.Add("new.png");f.Group();File.WriteAllText(sidecar,"<xmp>after</xmp>");var next=f.Export();nas.ReadNames.Clear();await ScreenshotPilot.Send(http,next,f.GameOutput,false);Assert.Equal(2,nas.Catalogue!.Photos.Count);Assert.NotEmpty(nas.ReadNames);Assert.Contains(nas.ReadNames,n=>n.EndsWith(".xmp"));Assert.DoesNotContain(nas.ReadNames,n=>oldNames.Contains(n));
        nas.ReadNames.Clear();await ScreenshotPilot.Send(http,next,f.GameOutput,false);Assert.Empty(nas.ReadNames);
    }
    [Fact]
    public async Task InterruptedUploadLeavesPublishedCatalogueAndRetryVerifiesMissingFiles()
    {
        using var f=new Fixture();using var nas=new Nas();using var http=new HttpClient(nas){BaseAddress=new Uri("http://localhost/")};var old=f.Export();await ScreenshotPilot.Send(http,old,f.GameOutput,false);f.Add("new.png");var next=f.Export();nas.FailOriginal=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>ScreenshotPilot.Send(http,next,f.GameOutput,false));Assert.Single(nas.Catalogue!.Photos);nas.FailOriginal=false;await ScreenshotPilot.Send(http,next,f.GameOutput,false);Assert.Equal(2,nas.Catalogue.Photos.Count);
    }
    [Fact]
    public void MediaLeaseExcludesConcurrentManualAndAutomaticExports()
    {
        using var f=new Fixture();using(var lease=PhotoSyncLease.Acquire(f.Data))Assert.Throws<IOException>(()=>PhotoSyncLease.Acquire(f.Data));using var resumed=PhotoSyncLease.Acquire(f.Data);
    }
    [Fact]
    public async Task BackgroundConnectionHonorsPauseSettlesPublishesAndSkipsAcknowledgedGamesAfterRestart()
    {
        using var f=new Fixture();var nas=new Nas{Enabled=false};using var http=new HttpClient(nas){BaseAddress=new Uri("http://localhost/")};using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var task=new AutomaticPhotoSync(f.Config,f.Data,http,TimeSpan.FromMilliseconds(60)).RunAsync(stop.Token);
        await Task.Delay(200);Assert.Equal(0,nas.Prepares);nas.Enabled=true;
        while(nas.Commits<1&&!stop.IsCancellationRequested)await Task.Delay(40);Assert.Equal(1,nas.Commits);
        while(!File.Exists(Path.Combine(f.Data,"automatic-photos.json"))&&!stop.IsCancellationRequested)await Task.Delay(40);
        nas.Enabled=false;f.Add("arrived.png");f.Group();await Task.Delay(200);Assert.Equal(1,nas.Commits);nas.Enabled=true;
        while(nas.Commits<2&&!stop.IsCancellationRequested)await Task.Delay(40);Assert.Equal(2,nas.Commits);Assert.Equal(2,nas.Catalogue!.Photos.Count);Assert.Contains(nas.Catalogue.Photos,p=>p.Group=="Favorites");
        var signature=f.Signature();
        while(!stop.IsCancellationRequested&&!SharedRead(Path.Combine(f.Data,"automatic-photos.json")).Contains(signature))await Task.Delay(40);
        while(!stop.IsCancellationRequested&&!SharedRead(Path.Combine(f.Data,"photo-sync-status.json")).Contains("ready"))await Task.Delay(40);
        stop.Cancel();await task;
        var restartedNas=new Nas{Catalogue=nas.Catalogue};using var restartHttp=new HttpClient(restartedNas){BaseAddress=new Uri("http://localhost/")};using var restartStop=new CancellationTokenSource(TimeSpan.FromMilliseconds(3400));await new AutomaticPhotoSync(f.Config,f.Data,restartHttp,TimeSpan.FromMilliseconds(60)).RunAsync(restartStop.Token);
        Assert.Equal(0,restartedNas.Prepares);Assert.Contains("ready",File.ReadAllText(Path.Combine(f.Data,"photo-sync-status.json")));
    }
    sealed class RemovalNas(ScreenshotCatalogue catalogue):HttpMessageHandler
    {
        public HashSet<string> Removed=[];public int Posts;public bool Fail;int version;
        ScreenshotRemovalGame Game()=>new(catalogue.Provider,catalogue.GameId,version.ToString(),catalogue.Photos.Select(p=>new ScreenshotRemovalIdentity(p.Id,p.EditionId,Removed.Contains(p.Id))).ToArray());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=JsonContent.Create(new[]{Game()},options:Json)};
            Posts++;if(Fail)return new(HttpStatusCode.ServiceUnavailable);
            var body=(await request.Content!.ReadFromJsonAsync<ScreenshotRemovalRequest>(Json,token))!;Assert.Equal(catalogue.WorkspaceId,body.WorkspaceId);Assert.Equal(version.ToString(),body.Version);
            Assert.EndsWith("/steam/10",request.RequestUri!.AbsolutePath);foreach(var id in body.RemoveIds)Removed.Add(id);foreach(var id in body.RestoreIds)Removed.Remove(id);version++;
            return new(HttpStatusCode.OK){Content=JsonContent.Create(Game(),options:Json)};
        }
    }
    [Fact]
    public async Task SuccessfulNativeDeletionRemovesLastPhotoAndReimportRestoresItWithoutInferringMissingFiles()
    {
        using var f=new Fixture();var photo=f.Export();using var nas=new RemovalNas(photo);using var http=new HttpClient(nas){BaseAddress=new Uri("http://localhost/")};
        f.Remove("first.png");var evidence=ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId);Assert.True(evidence.Events.Single().Missing);Assert.False(evidence.Events.Single().Indexed);Assert.False(evidence.Events.Single().Listed);
        Assert.Equal(1,await PhotoRemovalSync.Reconcile(http,f.Config,evidence,default));Assert.Contains(photo.Photos[0].Id,nas.Removed);Assert.Equal(0,await PhotoRemovalSync.Reconcile(http,f.Config,evidence,default));
        f.Add("first.png");f.SQL(f.Index,"UPDATE photo_index SET game_id='G2' WHERE root=$r");Assert.Equal(0,await PhotoRemovalSync.Reconcile(http,f.Config,ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId),default));f.SQL(f.Index,"UPDATE photo_index SET game_id='G1' WHERE root=$r");var restored=ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId);Assert.NotEqual(evidence.Signature,restored.Signature);Assert.Equal(1,await PhotoRemovalSync.Reconcile(http,f.Config,restored,default));Assert.Empty(nas.Removed);
    }
    [Fact]
    public async Task FailedDeletionOrUnindexedMissingFileDoesNotHidePhotosAndNetworkFailureCanRetry()
    {
        using var f=new Fixture();var photo=f.Export();using var nas=new RemovalNas(photo);using var http=new HttpClient(nas){BaseAddress=new Uri("http://localhost/")};
        f.Remove("first.png",false);var failed=ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId);Assert.Empty(failed.Events);Assert.Equal(0,await PhotoRemovalSync.Reconcile(http,f.Config,failed,default));Assert.Equal(0,nas.Posts);
        File.AppendAllText(Path.Combine(f.Source,"logs","PixelVault-native.log"),"[2026-10-27T03:00:00.000Z] Library delete: "+Path.Combine(f.Captures,"first.png")+"\n");
        // A stale index is not yet a completed deletion, even when a success log exists.
        f.SQL(f.Index,"INSERT INTO photo_index VALUES($r,$f,'G1','Steam',638900000000000000)",Path.Combine(f.Captures,"first.png"));var pending=ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId);Assert.Equal(0,await PhotoRemovalSync.Reconcile(http,f.Config,pending,default));Assert.Equal(0,nas.Posts);
        f.SQL(f.Index,"DELETE FROM photo_index WHERE root=$r");var evidence=ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId);nas.Fail=true;await Assert.ThrowsAsync<HttpRequestException>(()=>PhotoRemovalSync.Reconcile(http,f.Config,evidence,default));Assert.Empty(nas.Removed);nas.Fail=false;Assert.Equal(1,await PhotoRemovalSync.Reconcile(http,f.Config,evidence,default));
        var offline=Path.Combine(f.Directory,"offline");Assert.StartsWith(Path.GetFullPath(f.Directory)+Path.DirectorySeparatorChar,Path.GetFullPath(offline));System.IO.Directory.Move(f.Captures,offline);Assert.Throws<IOException>(()=>ScreenshotSnapshotExporter.ReadDeletionEvidence(f.Source,f.Config.WorkspaceId));
    }
}
