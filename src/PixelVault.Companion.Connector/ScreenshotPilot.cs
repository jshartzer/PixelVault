using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PixelVault.Companion.Export;

namespace PixelVault.Companion.Connector;

public static partial class ScreenshotPilot
{
    public static async Task<int> Run(string configFile,string provider,string game,int limit)
    {
        using var lease=PhotoSyncLease.Acquire(Path.GetDirectoryName(Path.GetFullPath(configFile))!);
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var config=JsonSerializer.Deserialize<ConnectorPairing>(File.ReadAllText(configFile),json)??throw new InvalidDataException("Missing pairing.");
        var origin=new Uri(config.Url);
        if(origin.Scheme is not ("http" or "https")||origin.AbsolutePath!="/"||!string.IsNullOrEmpty(origin.Query)||!string.IsNullOrEmpty(origin.UserInfo)
            ||config.Token.Length!=64||string.IsNullOrWhiteSpace(config.DesktopDataRoot))throw new InvalidDataException("Invalid approved PC connection.");
        var output=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configFile))!,"screenshot-pilot",provider+"-"+game);
        // This pilot is deliberately separate from automatic monitoring. The server allowlist and pause controls are enforced again there.
        var catalog=ScreenshotSnapshotExporter.Export(config.DesktopDataRoot,config.WorkspaceId,provider,game,output,limit,includeOriginals:true);
        return await Send(configFile,catalog,output);
    }
    public static async Task<int> Send(string configFile,ScreenshotCatalogue catalog,string output,bool verifyAll=true,CancellationToken token=default)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var config=JsonSerializer.Deserialize<ConnectorPairing>(File.ReadAllText(configFile),json)??throw new InvalidDataException("Missing pairing.");
        var origin=new Uri(config.Url);
        if(origin.Scheme is not ("http" or "https")||origin.AbsolutePath!="/"||!string.IsNullOrEmpty(origin.Query)||!string.IsNullOrEmpty(origin.UserInfo)
            ||config.Token.Length!=64||string.IsNullOrWhiteSpace(config.DesktopDataRoot))throw new InvalidDataException("Invalid approved PC connection.");
        var provider=catalog.Provider;var game=catalog.GameId;
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=origin,Timeout=TimeSpan.FromMinutes(2)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",config.Token);
        return await Send(http,catalog,output,verifyAll,token);
    }
    public static async Task<int> Send(HttpClient http,ScreenshotCatalogue catalog,string output,bool verifyAll=true,CancellationToken token=default)
    {
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);var provider=catalog.Provider;var game=catalog.GameId;
        using var prepare=await http.PostAsJsonAsync("connector/v1/screenshots/prepare",catalog,json,token);prepare.EnsureSuccessStatusCode();
        var plan=await prepare.Content.ReadFromJsonAsync<ScreenshotUploadPlan>(json,token)??throw new InvalidDataException("Invalid screenshot upload plan.");
        var allowed=catalog.Photos.SelectMany(p=>new[]{p.Thumbnail,p.Preview}).ToHashSet(StringComparer.Ordinal);
        foreach(var name in plan.MissingAssets)
        {
            if(!allowed.Contains(name))throw new InvalidDataException("Unexpected screenshot asset request.");
            using var bytes=new ByteArrayContent(await File.ReadAllBytesAsync(Path.Combine(output,"assets",name),token));
            using var sent=await http.PostAsync($"connector/v1/screenshots/{plan.Id}/assets/{name}",bytes,token);sent.EnsureSuccessStatusCode();
        }
        var originalMap=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(output,"original-sources.private.json")),json)??throw new InvalidDataException("Missing original source map.");
        var originals=catalog.Photos.SelectMany(p=>new[]{p.Original,p.Metadata}).OfType<ScreenshotOriginal>().GroupBy(o=>o.Asset,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        var uploadedOriginals=0;
        foreach(var name in plan.MissingOriginals)
        {
            if(!originals.TryGetValue(name,out var original)||!originalMap.TryGetValue(name,out var source))throw new InvalidDataException("Unexpected original file request.");
            var content=await File.ReadAllBytesAsync(source,token);
            if(content.LongLength!=original.Bytes||ScreenshotSnapshotExporter.Hash(content)!=name[..64])throw new IOException("An original changed before transfer. Re-run photo sync.");
            using var bytes=new ByteArrayContent(content);using var sent=await http.PostAsync($"connector/v1/screenshots/{plan.Id}/originals/{name}",bytes,token);sent.EnsureSuccessStatusCode();
            uploadedOriginals++;if(uploadedOriginals%20==0||uploadedOriginals==plan.MissingOriginals.Count)Console.WriteLine($"Originals uploaded: {uploadedOriginals}/{plan.MissingOriginals.Count}");
        }
        if(!plan.Current){using var commit=await http.PostAsync($"connector/v1/screenshots/{plan.Id}/commit",null,token);commit.EnsureSuccessStatusCode();}
        var returned=new List<ScreenshotPhoto>();string version=null;int? offset=0;
        do
        {
            var page=await http.GetFromJsonAsync<ScreenshotPage>($"connector/v1/screenshots/{provider}/{game}?offset={offset}&limit=60",json,token)??throw new InvalidDataException("Missing screenshot page.");
            if(version is not null&&version!=page.Version)throw new InvalidDataException("Screenshot manifest changed during verification.");version=page.Version;
            returned.AddRange(page.Photos);offset=page.NextOffset;
        }while(offset is not null);
        var expected=catalog.Photos.Concat(plan.RetainedPhotos).Where(p=>!plan.SuppressedPhotoIds.Contains(p.Id)).ToArray();
        if(returned.Count!=expected.Length||!returned.OrderBy(p=>p.Id).SequenceEqual(expected.OrderBy(p=>p.Id)))throw new InvalidDataException("NAS screenshot metadata did not match the PC export.");
        // Read every distinct published asset back; no success claim from upload acknowledgements alone.
        var checkedAssets=new HashSet<string>(StringComparer.Ordinal);long total=0;
        foreach(var p in returned)foreach(var (kind,name) in new[]{("thumbnail",p.Thumbnail),("preview",p.Preview)})
        {
            if((!verifyAll&&!plan.MissingAssets.Contains(name))||!checkedAssets.Add(name))continue;
            var bytes=await http.GetByteArrayAsync($"connector/v1/screenshots/{provider}/{game}/{p.Id}/{kind}",token);
            if(ScreenshotSnapshotExporter.Hash(bytes)+".jpg"!=name)throw new InvalidDataException("NAS screenshot derivative did not match.");total+=bytes.Length;
        }
        var checkedOriginals=new HashSet<string>(StringComparer.Ordinal);long originalBytes=0;
        foreach(var p in returned)foreach(var (original,kind) in new[]{(p.Original,"original"),(p.Metadata,"metadata")})
        {
            if(original is null||(!verifyAll&&!plan.MissingOriginals.Contains(original.Asset))||!checkedOriginals.Add(original.Asset))continue;
            using var response=await http.GetAsync($"connector/v1/screenshots/{provider}/{game}/{p.Id}/{kind}?asset={Uri.EscapeDataString(original.Asset)}",HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            using var stream=await response.Content.ReadAsStreamAsync(token);var hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream,token)).ToLowerInvariant();
            if(hash!=original.Asset[..64]||response.Content.Headers.ContentLength!=original.Bytes||response.Content.Headers.ContentType?.MediaType!=original.ContentType)throw new InvalidDataException("NAS original did not match the PC file.");
            originalBytes+=original.Bytes;
        }
        Console.WriteLine(JsonSerializer.Serialize(new{Photos=returned.Count,catalog.Eligible,catalog.SkippedVideos,Consoles=returned.GroupBy(p=>p.Console).ToDictionary(g=>g.Key,g=>g.Count()),Grouped=returned.Count(p=>p.Group.Length>0),Assets=checkedAssets.Count,Bytes=total,Uploaded=plan.MissingAssets.Count,Originals=checkedOriginals.Count,OriginalBytes=originalBytes,UploadedOriginals=uploadedOriginals,plan.Current,MetadataVerified=true,Verification=verifyAll?"all-published":"newly-uploaded",AllAssetsVerified=verifyAll,AllOriginalsVerified=verifyAll,AllUploadedFilesVerified=true},json));return 0;
    }
}
