using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PixelVault.Companion;
using Microsoft.Data.Sqlite;

namespace PixelVault.Companion.Export;
public static class LibrarySnapshotExporter
{
public static async Task<GameCatalogue> ExportAsync(string source,WorkspaceManifest manifest,string output,bool fetchProviders=true,bool reuse=false,string? workspaceIdOverride=null)
{
    source=Path.GetFullPath(source);output=Path.GetFullPath(output);SQLitePCL.Batteries_V2.Init();
    if(Directory.Exists(output)&&!reuse)throw new IOException("Choose a new export directory.");
    var previousFile=Path.Combine(output,"catalog.json");
    var previousCatalog=File.Exists(previousFile)?JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(previousFile),new JsonSerializerOptions(JsonSerializerDefaults.Web)):null;
    var scope=workspaceIdOverride??Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest))));
    var previous=previousCatalog?.WorkspaceId==scope?previousCatalog.Games.ToDictionary(g=>g.Provider+":"+g.GameId,StringComparer.Ordinal):new Dictionary<string,LibraryGame>();
    var imageIndexFile=Path.Combine(output,"artwork-index.json");
    var imageIndex=File.Exists(imageIndexFile)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(imageIndexFile))??new():new Dictionary<string,string>();
var workspaceId=scope;
var settings=File.ReadLines(Path.Combine(source,"PixelVault.settings.ini")).Where(l=>l.Contains('=')).Select(l=>l.Split('=',2))
    .ToDictionary(p=>p[0].Trim(),p=>p[1].Trim(),StringComparer.OrdinalIgnoreCase);
var library=settings.GetValueOrDefault("library")??throw new InvalidDataException("The desktop library is not configured.");
var cache=Path.Combine(source,"cache","library-folders-"+Safe(library)+".cache");
if(!File.Exists(cache))throw new FileNotFoundException("The saved desktop library inventory is unavailable.");
var artwork=Path.Combine(source,"cache","covers");var approved=manifest.Accounts;
var screenshotDates=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
var indexPath=Path.Combine(source,"cache","pixelvault-index-"+Safe(library)+".sqlite");
if(File.Exists(indexPath))
{
    using var index=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=indexPath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());index.Open();
    using var read=index.CreateCommand();read.CommandText="SELECT file_path,capture_utc_ticks FROM photo_index WHERE root=$root";read.Parameters.AddWithValue("$root",library);
    using var rows=read.ExecuteReader();while(rows.Read())
    {
        var file=rows.GetString(0);if(!IsScreenshot(file))continue;
        var ticks=rows.IsDBNull(1)?0:rows.GetInt64(1);
        if(ticks>0&&ticks<=DateTime.MaxValue.Ticks)screenshotDates[file]=ticks;
    }
}
var snapshots=new Dictionary<string,JsonDocument>(StringComparer.Ordinal);
foreach(var file in Directory.Exists(Path.Combine(source,"achievement-hub"))?Directory.EnumerateFiles(Path.Combine(source,"achievement-hub"),"*.json"):Array.Empty<string>())
{
    var document=JsonDocument.Parse(File.ReadAllText(file));var root=document.RootElement;
    var provider=root.GetProperty("Provider").GetString()??"";var account=root.GetProperty("Account").GetString()??"";
    if(approved.TryGetValue(provider,out var expected)&&string.Equals(account,expected,provider=="steam"?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase))snapshots[provider]=document;
    else document.Dispose();
}
var owned=new Dictionary<string,(int? Minutes,long? Last)>(StringComparer.Ordinal);
if(fetchProviders&&approved.TryGetValue("steam",out var steam)&&settings.TryGetValue("steam_web_api_key",out var key)&&key.Length>0)
{
    try
    {
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
        var uri="https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key="+Uri.EscapeDataString(key)+"&steamid="+Uri.EscapeDataString(steam)+"&include_played_free_games=1";
        using var response=await http.GetAsync(uri);
        if(response.IsSuccessStatusCode)
        {
            using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(document.RootElement.GetProperty("response").TryGetProperty("games",out var games))foreach(var game in games.EnumerateArray())
            {
                var id=game.GetProperty("appid").GetInt32().ToString(CultureInfo.InvariantCulture);
                int? minutes=game.TryGetProperty("playtime_forever",out var time)?time.GetInt32():null;
                long? played=game.TryGetProperty("rtime_last_played",out var last)&&last.GetInt64()>0?DateTimeOffset.FromUnixTimeSeconds(last.GetInt64()).UtcTicks:null;
                owned[id]=(minutes,played);
            }
        }
    }
    catch { Console.Error.WriteLine("Steam playtime was unavailable; no values were invented."); }
}
Directory.CreateDirectory(Path.Combine(output,"assets"));var exported=new Dictionary<string,LibraryGame>(StringComparer.Ordinal);
int covers=0,banners=0,achievements=0,playtimes=0;
foreach(var line in File.ReadLines(cache))
{
    var fields=line.Split('\t');if(fields.Length<14||fields.Length>15&&fields[15]=="1")continue;
    var title=fields[2];if(string.IsNullOrWhiteSpace(title))continue;title=title[..Math.Min(title.Length,250)];
    var platform=fields[5];string provider,id;
    if(platform is "Steam" or "PC"&&int.TryParse(fields[7],out var app)&&app>0){provider="steam";id=app.ToString(CultureInfo.InvariantCulture);}
    else if(platform=="Emulation"&&int.TryParse(fields[13],out var ra)&&ra>0){provider="retroachievements";id=ra.ToString(CultureInfo.InvariantCulture);}
    else{provider="library";id=Regex.IsMatch(fields[0],"^[A-Za-z0-9._-]{1,80}$")?fields[0]:Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(platform+"\n"+title)))[..24].ToLowerInvariant();}
    var identity=provider+":"+id;if(exported.ContainsKey(identity))continue;
    var custom=Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(fields[1]+"|"+platform+"|"+fields[2]))).ToLowerInvariant();
    var cover=Find("custom-"+custom)??Find(Safe(fields[2]));var banner=Find("custom-hero-"+custom)??Find("hero-"+Safe(fields[2]));
    var coverAsset=ExportImage(cover,480,720)??previous.GetValueOrDefault(identity)?.Cover;var bannerAsset=ExportImage(banner,1600,650)??previous.GetValueOrDefault(identity)?.Banner;
    if(coverAsset is not null)covers++;if(bannerAsset is not null)banners++;
    var prior=previous.GetValueOrDefault(identity); int? unlocked=prior?.Stats.AchievementUnlocked,total=prior?.Stats.AchievementTotal;long? updated=prior?.Stats.UpdatedUtc,played=prior?.Stats.LastPlayedUtc;int? minutes=prior?.Stats.PlaytimeMinutes;
    if(!approved.ContainsKey(provider)){unlocked=null;total=null;updated=null;played=null;minutes=null;}
    if(snapshots.TryGetValue(provider,out var snapshot))
    {
        var root=snapshot.RootElement;
        if(root.GetProperty("Games").TryGetProperty(id,out var game))
        {
            var rows=game.GetProperty("Rows").EnumerateArray().GroupBy(r=>r.GetProperty("ProviderAchievementId").GetString()).Select(g=>g.First()).ToList();
            total=rows.Count;unlocked=rows.All(r=>r.GetProperty("ProgressKnown").GetBoolean())?rows.Count(r=>r.GetProperty("Unlocked").GetBoolean()):null;
            updated=game.GetProperty("UpdatedUtc").GetInt64();achievements++;
        }
        if(root.TryGetProperty("Recent",out var recent)&&recent.TryGetProperty(id,out var last))played=last.GetInt64();
    }
    if(provider=="steam"&&owned.TryGetValue(id,out var personal)){minutes=personal.Minutes;played=personal.Last??played;if(minutes.HasValue)playtimes++;}
    int? captures=int.TryParse(fields[3],out var count)&&count>=0?count:null;
    var images=fields[6].Split('|',StringSplitOptions.RemoveEmptyEntries).Where(IsScreenshot);
    long? latest=images.Select(file=>screenshotDates.TryGetValue(file,out var ticks)?ticks:File.Exists(file)?File.GetLastWriteTimeUtc(file).Ticks:0).DefaultIfEmpty().Max();
    if(latest<=0)latest=null;
    exported.Add(identity,new(provider,id,title,platform,coverAsset,bannerAsset,new(minutes,unlocked,total,played,updated,captures,latest)));
}
var catalog=new GameCatalogue(1,workspaceId,DateTime.UtcNow,exported.Values.OrderBy(g=>g.Title,StringComparer.OrdinalIgnoreCase).ToList());
if(catalog.Games.Count==0)throw new InvalidDataException("The indexed library is empty; the current NAS library was preserved.");
var catalogTemp=Path.Combine(output,"catalog.tmp");File.WriteAllText(catalogTemp,JsonSerializer.Serialize(catalog,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));File.Move(catalogTemp,Path.Combine(output,"catalog.json"),true);
File.WriteAllText(imageIndexFile,JsonSerializer.Serialize(imageIndex));
foreach(var document in snapshots.Values)document.Dispose();
return catalog;

string? Find(string name)=>new[]{".jpg",".jpeg",".png",".bmp",".gif",".jxr"}.Select(ext=>Path.Combine(artwork,name+ext)).FirstOrDefault(File.Exists);
string? ExportImage(string? path,int maxWidth,int maxHeight)
{
    if(path is null)return null;
    try
    {
        var info=new FileInfo(path);var imageKey=path+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks+"|"+maxWidth+"|"+maxHeight;
        if(imageIndex.TryGetValue(imageKey,out var cached)&&Regex.IsMatch(cached,"^[a-f0-9]{64}\\.jpg$")&&File.Exists(Path.Combine(output,"assets",cached)))return cached;
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var image=Image.FromStream(stream);var scale=Math.Min(1,Math.Min((double)maxWidth/image.Width,(double)maxHeight/image.Height));
        using var bitmap=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.FromArgb(16,21,25));graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(image,0,0,bitmap.Width,bitmap.Height);}
        using var bytes=new MemoryStream();var codec=ImageCodecInfo.GetImageEncoders().Single(c=>c.MimeType=="image/jpeg");using var parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,86L);bitmap.Save(bytes,codec,parameters);
        var content=bytes.ToArray();var name=Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()+".jpg";var destination=Path.Combine(output,"assets",name);
        if(!File.Exists(destination))File.WriteAllBytes(destination,content);imageIndex[imageKey]=name;return name;
    }
    catch{return null;}
}
static string Safe(string title)=>Regex.Replace(Regex.Replace(title.Replace("™","").Replace("®","").Replace("©","").Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
static bool IsScreenshot(string file)=>new[]{".png",".jpg",".jpeg",".webp",".bmp",".gif",".tif",".tiff",".jxr",".heic",".avif"}.Contains(Path.GetExtension(file),StringComparer.OrdinalIgnoreCase);

}
}
