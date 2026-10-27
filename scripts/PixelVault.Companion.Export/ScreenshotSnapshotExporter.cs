using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;

namespace PixelVault.Companion.Export;

public static partial class ScreenshotSnapshotExporter
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static string Hash(string text)=>Hash(Encoding.UTF8.GetBytes(text));
    static SqliteConnection Read(string file){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();return c;}
    static string Safe(string value)=>Regex.Replace(Regex.Replace(value.Replace("™","").Replace("®","").Replace("©","").Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
    public static ScreenshotCatalogue Export(string source,string workspace,string provider,string game,string output,int limit=500,bool includeOriginals=false)=>ExportCore(source,workspace,provider,game,output,limit,includeOriginals,null);
    static ScreenshotCatalogue ExportCore(string source,string workspace,string provider,string game,string output,int limit,bool includeOriginals,Selection? selection,bool incremental=false,CancellationToken token=default)
    {
        if(selection is null&&(provider!="steam"||!Regex.IsMatch(game,"^[0-9]{1,12}$"))||limit<1||limit>(selection is null?500:5000))throw new InvalidDataException("The pilot needs an explicit Steam game and a limit up to 500.");
        source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        var setting=File.ReadLines(Path.Combine(source,"PixelVault.settings.ini")).FirstOrDefault(l=>l.StartsWith("library=",StringComparison.OrdinalIgnoreCase));
        var root=Path.GetFullPath(setting?.Split('=',2)[1].Trim()??throw new InvalidDataException("No indexed library configured."));
        EnsureUnder(source,output,false); // Export must never be inside the source data or capture library.
        EnsureUnder(root,output,false);
        using var c=Read(Path.Combine(source,"cache","pixelvault-index-"+Safe(root)+".sqlite"));using var tx=c.BeginTransaction(deferred:true);
        using var games=c.CreateCommand();games.Transaction=tx;games.CommandText="SELECT game_id,name,platform_label,steam_app_id,storage_group_id FROM game_index WHERE root=$r";games.Parameters.AddWithValue("$r",root);
        var editions=new List<(string Id,string Title,string Platform,string Steam,string Storage)>();
        using(var rows=games.ExecuteReader())while(rows.Read())editions.Add((rows.GetString(0),rows.GetString(1),rows.GetString(2),rows.GetString(3),rows.GetString(4)));
        var candidates=editions.Where(g=>selection is not null?g.Id==selection.Seed:g.Steam==game&&g.Platform is "Steam" or "PC").ToList();
        if(candidates.Count!=1)throw new InvalidDataException("The indexed game identity is ambiguous or missing.");
        var seed=candidates[0];var selected=editions.Where(g=>selection is not null?selection.Editions.Contains(g.Id):g.Id==seed.Id||seed.Storage.Length>0&&g.Storage==seed.Storage).ToDictionary(g=>g.Id,StringComparer.OrdinalIgnoreCase);
        var distinctScopes=selected.Values.Select(g=>g.Storage.Length>0?"storage:"+g.Storage:g.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scope=distinctScopes.Length==1?distinctScopes[0]:"game:"+provider+":"+game;
        var groups=Groups(Path.Combine(source,"capture-groups.sqlite"),root,selected.Keys,scope);
        foreach(var otherScope in distinctScopes.Where(g=>g!=scope))foreach(var entry in Groups(Path.Combine(source,"capture-groups.sqlite"),root,[],otherScope))groups[entry.Key]=entry.Value;
        using var photos=c.CreateCommand();photos.Transaction=tx;photos.CommandText="SELECT file_path,game_id,console_label,capture_utc_ticks FROM photo_index WHERE root=$r ORDER BY capture_utc_ticks DESC,file_path";photos.Parameters.AddWithValue("$r",root);
        var input=new List<(string File,string Edition,string Console,long Ticks)>();int videos=0;
        using(var rows=photos.ExecuteReader())while(rows.Read())
        {
            var edition=rows.GetString(1);if(!selected.ContainsKey(edition))continue;var file=rows.GetString(0);if(selection is not null&&!selection.Files.Contains(file))continue;var ext=Path.GetExtension(file).ToLowerInvariant();
            if(new[]{".mp4",".mkv",".mov",".avi",".webm",".m4v"}.Contains(ext)){videos++;continue;}
            if(!new[]{".jpg",".jpeg",".png",".bmp",".gif",".webp",".jxr"}.Contains(ext))throw new InvalidDataException("The pilot contains an unrecognized capture format.");
            var console=rows.GetString(2);if(console.Length==0)console=selected[edition].Platform;
            input.Add((file,edition,console,rows.GetInt64(3)));
        }
        tx.Commit();
        Directory.CreateDirectory(Path.Combine(output,"assets"));
        var indexPath=Path.Combine(output,"source-index.private.json");
        var previous=File.Exists(indexPath)?JsonSerializer.Deserialize<Dictionary<string,Cached>>(File.ReadAllText(indexPath),Json)??new():new();
        var oldCatalogue=incremental&&File.Exists(Path.Combine(output,"catalogue.json"))?JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(Path.Combine(output,"catalogue.json")),Json):null;
        var oldPhotos=oldCatalogue?.Photos.ToDictionary(p=>p.Id)??new();
        var next=new Dictionary<string,Cached>();var originalSources=new Dictionary<string,string>(StringComparer.Ordinal);var exported=new List<ScreenshotPhoto>();using var originals=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var row in input.Take(limit))
        {
            token.ThrowIfCancellationRequested();var file=Path.GetFullPath(row.File);EnsureUnder(root,file,true);
            for(var p=file;p.Length>=root.Length;p=Path.GetDirectoryName(p)??"")
                if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked captures are outside the pilot.");
            var info=new FileInfo(file);if(!info.Exists||info.Length is <1 or >64*1024*1024)throw new InvalidDataException("A selected capture is unavailable or too large.");
            var id=Hash(workspace+"\n"+row.Edition+"\n"+Path.GetRelativePath(root,file).Replace('\\','/').ToLowerInvariant());
            var stamp=info.LastWriteTimeUtc.Ticks;var length=info.Length;
            var reusable=incremental&&previous.TryGetValue(id,out var prior)&&oldPhotos.TryGetValue(id,out var photo)&&photo.Original is not null&&photo.Original.Bytes==length&&photo.Original.LastWriteUtcTicks==stamp&&photo.Original.Asset[..64]==prior.Fingerprint&&(!file.EndsWith(".jxr",StringComparison.OrdinalIgnoreCase)||prior.Conversion=="hdr-reinhard-v3")&&File.Exists(Path.Combine(output,"assets",prior.Thumbnail))&&File.Exists(Path.Combine(output,"assets",prior.Preview));
            var before=reusable?null:File.ReadAllBytes(file);var fingerprint=reusable?previous[id].Fingerprint:Hash(before!);
            Cached cached;
            if(previous.TryGetValue(id,out var old)&&old.Fingerprint==fingerprint&&(!Path.GetExtension(file).Equals(".jxr",StringComparison.OrdinalIgnoreCase)||old.Conversion=="hdr-reinhard-v3")&&File.Exists(Path.Combine(output,"assets",old.Thumbnail))&&File.Exists(Path.Combine(output,"assets",old.Preview)))cached=old;
            else
            {
                using var stream=new MemoryStream(before!);using var image=Path.GetExtension(file).Equals(".jxr",StringComparison.OrdinalIgnoreCase)?DecodeJxr(before!):Image.FromStream(stream);
                if((long)image.Width*image.Height>100_000_000)throw new InvalidDataException("A selected capture exceeds the pixel limit.");
                if(image.PropertyIdList.Contains(0x112)){var orientation=image.GetPropertyItem(0x112)?.Value?[0]??1;image.RotateFlip(orientation switch{2=>RotateFlipType.RotateNoneFlipX,3=>RotateFlipType.Rotate180FlipNone,4=>RotateFlipType.Rotate180FlipX,5=>RotateFlipType.Rotate90FlipX,6=>RotateFlipType.Rotate90FlipNone,7=>RotateFlipType.Rotate270FlipX,8=>RotateFlipType.Rotate270FlipNone,_=>RotateFlipType.RotateNoneFlipNone});}
                cached=new(fingerprint,image.Width,image.Height,Derivative(image,480,output),Derivative(image,2560,output),Path.GetExtension(file).Equals(".jxr",StringComparison.OrdinalIgnoreCase)?"hdr-reinhard-v3":null);
            }
            var after=reusable?fingerprint:Hash(File.ReadAllBytes(file));info.Refresh();if(after!=fingerprint||info.Length!=length||info.LastWriteTimeUtc.Ticks!=stamp)throw new IOException("A capture changed during export; current NAS photos were retained.");
            originals.AppendData(Encoding.UTF8.GetBytes(id+":"+fingerprint+"\n"));next[id]=cached;
            if(row.Ticks<0||row.Ticks>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid indexed capture date.");
            var day=row.Ticks==0?null:TimeZoneInfo.ConvertTimeFromUtc(new DateTime(row.Ticks,DateTimeKind.Utc),TimeZoneInfo.Local).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            ScreenshotOriginal? original=null;
            if(includeOriginals)
            {
                var ext=Path.GetExtension(file).ToLowerInvariant();var name=fingerprint+ext;originalSources[name]=file;
                var mime=ext switch{".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",".webp"=>"image/webp",".gif"=>"image/gif",".bmp"=>"image/bmp",".jxr"=>"image/vnd.ms-photo",_=>throw new InvalidDataException("Unsupported original format.")};
                original=new(name,Path.GetFileName(file),length,mime,info.LastWriteTimeUtc.Ticks);
            }
            ScreenshotOriginal? metadata=null;var sidecar=file+".xmp";
            if(includeOriginals&&File.Exists(sidecar))
            {
                var sideInfo=new FileInfo(sidecar);if(sideInfo.Length is <1 or >1024*1024||(sideInfo.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Invalid photo metadata sidecar.");
                var content=File.ReadAllBytes(sidecar);var asset=Hash(content)+".xmp";if(Hash(File.ReadAllBytes(sidecar))!=asset[..64])throw new IOException("Photo metadata changed during export.");
                metadata=new(asset,Path.GetFileName(sidecar),content.LongLength,"application/rdf+xml",sideInfo.LastWriteTimeUtc.Ticks);originalSources[asset]=sidecar;
            }
            // Removing a local sidecar must not strip an already archived metadata download.
            else if(incremental&&oldPhotos.TryGetValue(id,out var oldPhoto))metadata=oldPhoto.Metadata;
            var relative=Path.GetRelativePath(root,file);exported.Add(new ScreenshotPhoto(id,row.Edition,row.Console,row.Ticks,day,groups.GetValueOrDefault(relative,""),cached.Width,cached.Height,cached.Thumbnail,cached.Preview){Original=original,Metadata=metadata});
        }
        if(exported.Count==0)throw new InvalidDataException("No screenshots are available for this pilot.");
        var catalogue=new ScreenshotCatalogue(1,workspace,provider,game,selection?.Title??seed.Title,Hash(scope),TimeZoneInfo.Local.Id,DateTime.UtcNow.Ticks,input.Count,videos,exported);
        File.WriteAllText(indexPath,JsonSerializer.Serialize(next,Json));
        if(includeOriginals)File.WriteAllText(Path.Combine(output,"original-sources.private.json"),JsonSerializer.Serialize(originalSources,Json));
        File.WriteAllText(Path.Combine(output,"catalogue.json"),JsonSerializer.Serialize(catalogue,Json));
        File.WriteAllText(Path.Combine(output,"source-audit.private.json"),JsonSerializer.Serialize(new{Selected=exported.Count,Eligible=input.Count,OriginalsUnchanged=true,Digest=Convert.ToHexString(originals.GetHashAndReset()),Formats=input.Take(limit).GroupBy(r=>Path.GetExtension(r.File).ToLowerInvariant()).ToDictionary(g=>g.Key,g=>g.Count())},Json));
        return catalogue;
    }
    static void EnsureUnder(string root,string path,bool expected)
    {
        var rel=Path.GetRelativePath(root,path);var inside=rel=="."||!Path.IsPathRooted(rel)&&rel!=".."&&!rel.StartsWith(".."+Path.DirectorySeparatorChar);
        if(inside!=expected)throw new InvalidDataException("Invalid capture/export location.");
    }
    static Dictionary<string,string> Groups(string file,string root,IEnumerable<string> ids,string scope)
    {
        var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(!File.Exists(file))return result;
        using var c=Read(file);using var tx=c.BeginTransaction(deferred:true);
        foreach(var id in ids.Order(StringComparer.OrdinalIgnoreCase).Append(scope))
        {
            using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT file,name FROM capture_group_member WHERE root=$r AND game=$g";cmd.Parameters.AddWithValue("$r",root);cmd.Parameters.AddWithValue("$g",id);
            using var rows=cmd.ExecuteReader();while(rows.Read())result[rows.GetString(0)]=rows.GetString(1);
        }
        return result;
    }
    static string Derivative(Image image,int max,string output)
    {
        var scale=Math.Min(1,(double)max/Math.Max(image.Width,image.Height));using var bitmap=new Bitmap(Math.Max(1,(int)(image.Width*scale)),Math.Max(1,(int)(image.Height*scale)),PixelFormat.Format24bppRgb);
        using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,0,0,bitmap.Width,bitmap.Height);}
        using var stream=new MemoryStream();var codec=ImageCodecInfo.GetImageEncoders().Single(c=>c.MimeType=="image/jpeg");using var parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,max==480?80L:88L);bitmap.Save(stream,codec,parameters);
        var bytes=stream.ToArray();if(bytes.Length>6*1024*1024)throw new InvalidDataException("A viewing copy exceeds the pilot limit.");var name=Hash(bytes)+".jpg";var file=Path.Combine(output,"assets",name);if(!File.Exists(file))File.WriteAllBytes(file,bytes);return name;
    }
    sealed record Cached(string Fingerprint,int Width,int Height,string Thumbnail,string Preview,string? Conversion=null);
}
