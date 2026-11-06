using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    sealed class PhotoCase:IDisposable
    {
        internal readonly DirectCase Data=new();
        internal string Root=>Path.Combine(Data.Case.Base,"captures");
        internal static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6ioAAAAASUVORK5CYII=");
        internal static string Epoch=>new('A',64);
        internal static string Id(string relative)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Epoch+"\nG1\n"+relative.Replace('\\','/').ToLowerInvariant()))).ToLowerInvariant();
        internal static string Worker
        {
            get{var dir=new DirectoryInfo(AppContext.BaseDirectory);while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"src","PixelVault.Companion.Media","PixelVault.Companion.Media.csproj")))dir=dir.Parent;return Path.Combine(dir!.FullName,"src","PixelVault.Companion.Media","bin",new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name,"net10.0-windows","PixelVault.Companion.Media.dll");}
        }
        internal CompanionOptions Options=>new(){DataRoot=Data.State,ApplicationDataRoot=Data.App,CacheRoot=Data.Cache,StorageMode="direct-local",ScreenshotLibraryEnabled=true,PhotoWorkerPath=Worker};
        internal PhotoCase()
        {
            Sql("ALTER TABLE game_index ADD COLUMN storage_group_id TEXT DEFAULT 'SG1';ALTER TABLE photo_index ADD COLUMN game_id TEXT DEFAULT 'G1';ALTER TABLE photo_index ADD COLUMN console_label TEXT DEFAULT 'Steam';");
            foreach(var name in new[]{"a.png","b.png","hidden.png"})Capture(name,name=="b.png"?0:638900000000000000);
            File.WriteAllText(Path.Combine(Root,"a.png.xmp"),"<xmp>exact original metadata</xmp>");File.SetLastWriteTimeUtc(Path.Combine(Root,"a.png.xmp"),DateTime.UtcNow.AddMinutes(-1));
            using(var c=new SqliteConnection("Data Source="+Path.Combine(Data.App,"capture-groups.sqlite")+";Pooling=False"))
            {c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE capture_group_member(root TEXT,game TEXT,file TEXT,name TEXT);INSERT INTO capture_group_member VALUES($r,'G1','a.png','Legacy'),($r,'storage:SG1','a.png','Canonical')";q.Parameters.AddWithValue("$r",Root);q.ExecuteNonQuery();}
            var photos=new[]{"a.png","b.png","hidden.png"}.Select(name=>new ScreenshotPhoto(Id(name),"G1","Steam",name=="b.png"?0:638900000000000000,null,"",1,1,new string('b',64)+".jpg",new string('c',64)+".jpg")
            {Original=new(Convert.ToHexString(SHA256.HashData(Png)).ToLowerInvariant()+".png",name,Png.Length,"image/png",File.GetLastWriteTimeUtc(Path.Combine(Root,name)).Ticks)}).ToArray();
            var archived=photos[2] with{Id=new string('d',64)};
            LocalPhotoPreparation.Initialize(Options,Epoch,[new(1,Epoch,"steam","42","Test journey",new string('a',64),TimeZoneInfo.Local.Id,DateTime.UtcNow.Ticks,3,0,photos)],new Dictionary<string,IReadOnlyList<ScreenshotPhoto>>{["steam\n42"]=[photos[2],archived]});
        }
        internal void Sql(string sql,string? from=null,string? to=null)
        {using var c=new SqliteConnection("Data Source="+Data.Index+";Pooling=False");c.Open();using var q=c.CreateCommand();q.CommandText=sql;if(from is not null)q.Parameters.AddWithValue("$from",from);if(to is not null)q.Parameters.AddWithValue("$to",to);q.ExecuteNonQuery();}
        internal void Capture(string name,long ticks=638900000000000001)
        {
            var path=Path.Combine(Root,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,Png);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddMinutes(-1));
            using var c=new SqliteConnection("Data Source="+Data.Index+";Pooling=False");c.Open();using var q=c.CreateCommand();q.CommandText="INSERT INTO photo_index(root,file_path,capture_utc_ticks) VALUES($r,$p,$t)";q.Parameters.AddWithValue("$r",Root);q.Parameters.AddWithValue("$p",path);q.Parameters.AddWithValue("$t",ticks);q.ExecuteNonQuery();Inventory();
        }
        internal void Inventory()
        {
            var fields=File.ReadAllText(Data.FolderCache).Split('\t');fields[6]=string.Join('|',Directory.EnumerateFiles(Root,"*.png",SearchOption.AllDirectories));File.WriteAllText(Data.FolderCache,string.Join('\t',fields));
        }
        internal Host Host()=>new(false,root:Data.State,screenshotLibrary:true,applicationRoot:Data.App,cacheRoot:Data.Cache,photoWorker:Worker);
        public void Dispose()=>Data.Dispose();
    }
    const string PhotoPage="/api/v1/screenshots/steam/42";

    [Fact]
    public async Task LocalPhotosUseNativeGroupsDatesAndExactAuthenticatedRangeDownloads()
    {
        using var f=new PhotoCase();using var host=f.Host();using var client=host.Client();await Setup(host,client);
        var page=await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage);Assert.Equal(2,page!.Total);Assert.Equal("Canonical",page.Photos.Single(p=>p.Id==PhotoCase.Id("a.png")).Group);Assert.Null(page.Photos.Single(p=>p.Id==PhotoCase.Id("b.png")).CaptureDay);
        Assert.DoesNotContain(f.Root.Replace("\\","\\\\"),await client.GetStringAsync(PhotoPage));
        var url=PhotoPage+"/"+PhotoCase.Id("a.png")+"/original";
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(url)).StatusCode);
        using var response=await client.GetAsync(url);Assert.Equal(PhotoCase.Png,await response.Content.ReadAsByteArrayAsync());Assert.Equal("a.png",response.Content.Headers.ContentDisposition!.FileNameStar);Assert.Equal("\""+Convert.ToHexString(SHA256.HashData(PhotoCase.Png)).ToLowerInvariant()+"\"",response.Headers.ETag!.Tag);
        using var rangeRequest=new HttpRequestMessage(HttpMethod.Get,url);rangeRequest.Headers.Range=new(0,7);using var range=await client.SendAsync(rangeRequest);Assert.Equal(HttpStatusCode.PartialContent,range.StatusCode);Assert.Equal(PhotoCase.Png[..8],await range.Content.ReadAsByteArrayAsync());
        Assert.Equal("<xmp>exact original metadata</xmp>",await client.GetStringAsync(PhotoPage+"/"+PhotoCase.Id("a.png")+"/metadata"));
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(url+"?asset=old")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/screenshots/steam/999/"+PhotoCase.Id("a.png")+"/original")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(PhotoPage+"/"+PhotoCase.Id("hidden.png")+"/original")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone,(await client.GetAsync("/connector/v1/screenshots/removals")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(f.Data.State,"screenshots")));
    }

    [Fact]
    public async Task LocalPhotoIdentitySurvivesRenameRestartAndCacheRegenerationAndNewCaptureNeedsNoUpload()
    {
        using var f=new PhotoCase();string cookie;string firstVersion;byte[] first;
        using(var host=f.Host())using(var client=host.Client())
        {
            cookie=await Setup(host,client);var before=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;firstVersion=before.Version;
            var url=PhotoPage+"/"+PhotoCase.Id("a.png")+"/thumbnail";first=await client.GetByteArrayAsync(url);Assert.Equal((byte)255,first[0]);Assert.Equal((byte)216,first[1]);
            var cacheFiles=Directory.GetFiles(Path.Combine(f.Data.Cache,"photos"),"*.jpg");Assert.Single(cacheFiles);File.Delete(cacheFiles[0]);
            Assert.Equal(first,await client.GetByteArrayAsync(url));
            File.Move(Path.Combine(f.Root,"a.png"),Path.Combine(f.Root,"moved.png"));f.Sql("UPDATE photo_index SET file_path=$to WHERE file_path=$from",Path.Combine(f.Root,"a.png"),Path.Combine(f.Root,"moved.png"));f.Inventory();
            await Task.Delay(1100);_=await client.GetAsync(PhotoPage);await Task.Delay(1100);
            var renamed=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.Equal(2,renamed.Total);Assert.Equal("moved.png",renamed.Photos.Single(p=>p.Id==PhotoCase.Id("a.png")).Original!.FileName);Assert.DoesNotContain(renamed.Photos,p=>p.Id==PhotoCase.Id("moved.png"));
            f.Capture("new.png");await Task.Delay(1100);_=await client.GetAsync(PhotoPage);await Task.Delay(1100);
            var updated=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.Equal(3,updated.Total);Assert.Contains(updated.Photos,p=>p.Id==PhotoCase.Id("new.png"));Assert.NotEqual(firstVersion,updated.Version);
            Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(PhotoPage+"?offset=1&version="+firstVersion)).StatusCode);
            var timeline=await client.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline");Assert.Equal(3,timeline!.Total);
        }
        using var restart=f.Host();using var reader=restart.Client();reader.DefaultRequestHeaders.Add("Cookie",cookie);
        var after=(await reader.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.Equal(3,after.Total);Assert.Contains(after.Photos,p=>p.Id==PhotoCase.Id("a.png"));Assert.DoesNotContain(after.Photos,p=>p.Id==PhotoCase.Id("hidden.png"));
    }

    [Fact]
    public async Task LocalPhotoChangesAreVersionedAndUnavailableFilesNeverBecomeDeletions()
    {
        using var f=new PhotoCase();using var host=f.Host();using var client=host.Client();await Setup(host,client);
        var before=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;var photo=before.Photos.Single(p=>p.Id==PhotoCase.Id("a.png"));var path=Path.Combine(f.Root,"a.png");var oldTime=File.GetLastWriteTimeUtc(path);var changed=PhotoCase.Png.ToArray();changed[^20]^=1;File.WriteAllBytes(path,changed);File.SetLastWriteTimeUtc(path,oldTime);
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync(PhotoPage+"/"+photo.Id+"/original?asset="+photo.Original!.Asset)).StatusCode);
        await Task.Delay(1100);_=await client.GetAsync(PhotoPage);await Task.Delay(1100);var next=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.NotEqual(photo.Original.Asset,next.Photos.Single(p=>p.Id==photo.Id).Original!.Asset);Assert.Equal(changed,await client.GetByteArrayAsync(PhotoPage+"/"+photo.Id+"/original"));
        File.Move(path,path+".offline");f.Sql("DELETE FROM photo_index WHERE file_path=$from",path);f.Inventory();await Task.Delay(1100);
        var missing=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.Equal(2,missing.Total);Assert.Equal("unavailable",missing.Photos.Single(p=>p.Id==photo.Id).Availability);
        Directory.CreateDirectory(Path.Combine(f.Data.App,"logs"));File.WriteAllText(Path.Combine(f.Data.App,"logs","PixelVault-native.log"),"["+DateTimeOffset.UtcNow.ToString("O")+"] Library delete: "+path+"\n");await Task.Delay(1100);
        Assert.Equal(1,(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!.Total);
        File.Move(path+".offline",path);f.Capture("new.png");f.Inventory();f.Sql("INSERT INTO photo_index(root,file_path,capture_utc_ticks) VALUES('"+f.Root.Replace("'","''")+"',$from,0)",path);await Task.Delay(1100);
        var restored=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.DoesNotContain(restored.Photos,p=>p.Id==photo.Id);
    }

    [Fact]
    public async Task LocalPhotoOpenRejectsJunctionEscapeAndOfflineRootPreservesMetadata()
    {
        using var f=new PhotoCase();using var host=f.Host();using var client=host.Client();await Setup(host,client);_=await client.GetAsync(PhotoPage);
        var overlapping=f.Options;overlapping.CacheRoot=Path.Combine(f.Root,"generated");using(var invalid=new TrackerWorkspace(overlapping))Assert.Throws<InvalidDataException>(()=>invalid.LocalPhotos);
        Assert.False(Directory.Exists(overlapping.CacheRoot));
        var outside=Path.Combine(f.Data.Case.Base,"outside");Directory.CreateDirectory(outside);File.WriteAllBytes(Path.Combine(outside,"a.png"),PhotoCase.Png);
        Directory.Move(f.Root,f.Root+"-retained");
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,RedirectStandardOutput=true};
        foreach(var arg in new[]{"/c","mklink","/J",f.Root,outside})start.ArgumentList.Add(arg);
        using(var process=System.Diagnostics.Process.Start(start)!){await process.WaitForExitAsync();Assert.Equal(0,process.ExitCode);}
        try{Assert.False((await client.GetAsync(PhotoPage+"/"+PhotoCase.Id("a.png")+"/original")).IsSuccessStatusCode);}
        finally{Directory.Delete(f.Root);Directory.Move(f.Root+"-retained",f.Root);}
        var db=Path.Combine(f.Data.App,"local-photos","identities.sqlite");var before=File.ReadAllBytes(db);Directory.Move(f.Root,f.Root+"-offline");
        try{Assert.Equal(HttpStatusCode.InternalServerError,(await client.GetAsync(PhotoPage)).StatusCode);Assert.Equal(before,File.ReadAllBytes(db));}
        finally{Directory.Move(f.Root+"-offline",f.Root);}
    }

    [Fact]
    public async Task LocalPhotoCacheEvictsWithinQuotaAndParallelRequestsShareDerivatives()
    {
        using var f=new PhotoCase();using var host=f.Host();using var client=host.Client();await Setup(host,client);
        host.Services.GetRequiredService<CompanionOptions>().PhotoCacheBytes=8L*1024*1024;
        var cache=Path.Combine(f.Data.Cache,"photos");Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache,new string('a',64)+".jpg"),new byte[5*1024*1024]);File.WriteAllBytes(Path.Combine(cache,new string('b',64)+".jpg"),new byte[5*1024*1024]);
        var url=PhotoPage+"/"+PhotoCase.Id("a.png")+"/thumbnail";
        var responses=await Task.WhenAll(Enumerable.Range(0,8).Select(_=>client.GetByteArrayAsync(url)));Assert.All(responses,r=>Assert.Equal(responses[0],r));
        Assert.True(Directory.EnumerateFiles(cache,"*.jpg").Sum(p=>new FileInfo(p).Length)<=8L*1024*1024);Assert.Empty(Directory.EnumerateFiles(cache,"*.tmp",SearchOption.AllDirectories));Assert.Empty(Directory.EnumerateDirectories(cache,"work-*"));
        var download=await host.Services.GetRequiredService<TrackerWorkspace>().LocalPhotos!.DownloadOriginal("steam","42",PhotoCase.Id("a.png"),null,false,CancellationToken.None);
        await using(download.Stream)Assert.Throws<IOException>(()=>{using var writer=File.Open(Path.Combine(f.Root,"a.png"),FileMode.Open,FileAccess.Write,FileShare.Read);});
    }
}
