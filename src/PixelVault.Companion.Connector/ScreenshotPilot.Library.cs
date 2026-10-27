using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PixelVault.Companion.Export;

namespace PixelVault.Companion.Connector;
public static partial class ScreenshotPilot
{
    public static async Task<int> Library(string configFile,bool prepareOnly,bool preparedOnly=false)
    {
        using var lease=PhotoSyncLease.Acquire(Path.GetDirectoryName(Path.GetFullPath(configFile))!);
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var config=JsonSerializer.Deserialize<ConnectorPairing>(File.ReadAllText(configFile),json)??throw new InvalidDataException("Missing pairing.");
        var root=Path.GetDirectoryName(Path.GetFullPath(configFile))!;
        var approved=JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(Path.Combine(root,"library-cache","catalog.json")),json)??throw new InvalidDataException("No approved indexed library.");
        if(!prepareOnly)
        {
            var origin=new Uri(config.Url);if(origin.Scheme is not ("http" or "https")||origin.AbsolutePath!="/"||!string.IsNullOrEmpty(origin.Query)||!string.IsNullOrEmpty(origin.UserInfo)||config.Token.Length!=64)throw new InvalidDataException("Invalid approved PC connection.");
            using var http=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=origin,Timeout=TimeSpan.FromSeconds(30)};http.DefaultRequestHeaders.Authorization=new("Bearer",config.Token);
            using var response=await http.GetAsync("connector/v1/screenshots/capabilities");response.EnsureSuccessStatusCode();using var capabilities=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(!capabilities.RootElement.GetProperty("libraryEnabled").GetBoolean()||!capabilities.RootElement.GetProperty("metadata").GetBoolean()||!capabilities.RootElement.GetProperty("retention").GetBoolean())throw new InvalidOperationException("Update and enable the NAS photo library before publishing.");
        }
        var output=Path.Combine(root,"screenshot-library");
        IReadOnlyList<ScreenshotCatalogue> catalogues;
        if(preparedOnly)
        {
            var entries=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(output,"library-manifest.json")),json)??throw new InvalidDataException("No completed photo preparation.");
            if(entries.Length is <1 or >1000)throw new InvalidDataException("Invalid prepared photo count.");
            catalogues=entries.Select(entry=>{
                var provider=entry.GetProperty("provider").GetString()!;var game=entry.GetProperty("gameId").GetString()!;var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider+"\n"+game))).ToLowerInvariant();
                if(entry.GetProperty("directory").GetString()!=key)throw new InvalidDataException("Invalid prepared photo location.");
                var c=JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(Path.Combine(output,key,"catalogue.json")),json)??throw new InvalidDataException("Invalid prepared photos.");
                if(c.WorkspaceId!=config.WorkspaceId||c.Provider!=provider||c.GameId!=game||c.Photos.Count!=entry.GetProperty("photos").GetInt32()||!approved.Games.Any(g=>g.Provider==provider&&g.GameId==game&&g.Title==c.Title))throw new InvalidDataException("Prepared photo scope changed.");return c;
            }).ToArray();
        }
        else catalogues=ScreenshotSnapshotExporter.ExportLibrary(config.DesktopDataRoot,config.WorkspaceId,approved,output);
        if(!prepareOnly)
        {
            var done=0;
            foreach(var catalog in catalogues)
            {
                var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalog.Provider+"\n"+catalog.GameId))).ToLowerInvariant();
                await Send(configFile,catalog,Path.Combine(output,key));done++;Console.WriteLine($"Photo families synchronized and verified: {done}/{catalogues.Count}");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new{Prepared=true,Synchronized=!prepareOnly,Games=catalogues.Count,Photos=catalogues.Sum(c=>c.Photos.Count),VideosExcluded=catalogues.Sum(c=>c.SkippedVideos),Metadata=catalogues.Sum(c=>c.Photos.Count(p=>p.Metadata is not null))},json));return 0;
    }
}
