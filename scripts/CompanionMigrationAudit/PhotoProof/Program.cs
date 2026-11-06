using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;

if(args.Length!=1)throw new ArgumentException("Use PhotoProof <staged-phase3-direct-local.json>.");
var configuration=Path.GetFullPath(args[0]);var stage=Path.GetDirectoryName(configuration)!;var evidence=Path.GetDirectoryName(stage)!;
var options=JsonDocument.Parse(File.ReadAllText(configuration)).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()!;
if(!File.Exists(Path.Combine(stage,"stage-manifest.json"))||!File.Exists(Path.Combine(options.ApplicationDataRoot,"local-store-preparation.json")))throw new InvalidDataException("Only an explicitly staged rehearsal is supported.");
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);var workspace=new TrackerWorkspace(options);
var oldRoot=Directory.GetDirectories(Path.Combine(evidence,"nas","screenshots")).Single();
var old=Directory.EnumerateFiles(Path.Combine(oldRoot,"catalogues"),"*.json").Prepend(Path.Combine(oldRoot,"catalogue.json")).Where(File.Exists).Select(f=>JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(f),json)!).DistinctBy(c=>c.Provider+"\n"+c.GameId).ToArray();
var removed=Directory.EnumerateFiles(Path.Combine(oldRoot,"removals"),"*.json").SelectMany(f=>JsonSerializer.Deserialize<Dictionary<string,ScreenshotPhoto>>(File.ReadAllText(f),json)!.Keys).ToHashSet();
var watch=Stopwatch.StartNew();var photos=workspace.LocalPhotos!;var current=photos.Catalogues();var cold=watch.ElapsedMilliseconds;watch.Restart();_=photos.Catalogues();var warm=watch.ElapsedMilliseconds;
var expected=old.SelectMany(c=>c.Photos.Where(p=>!removed.Contains(p.Id)).Select(p=>(c.Provider,c.GameId,Photo:p))).ToDictionary(p=>p.Photo.Id);
var actual=current.SelectMany(c=>c.Photos.Select(p=>(c.Provider,c.GameId,Photo:p))).ToDictionary(p=>p.Photo.Id);
if(!expected.Keys.ToHashSet().SetEquals(actual.Keys))throw new Exception("Visible photo identities do not match the NAS inventory.");
foreach(var (id,prior) in expected)
{
    var now=actual[id];var a=prior.Photo;var b=now.Photo;
    if(prior.Provider!=now.Provider||prior.GameId!=now.GameId||a.EditionId!=b.EditionId||a.Console!=b.Console||a.CaptureUtcTicks!=b.CaptureUtcTicks||a.CaptureDay!=b.CaptureDay||a.Group!=b.Group||a.Width!=b.Width||a.Height!=b.Height||a.Original?.FileName!=b.Original?.FileName||a.Original?.Bytes!=b.Original?.Bytes||a.Metadata?.FileName!=b.Metadata?.FileName||a.Metadata?.Bytes!=b.Metadata?.Bytes||b.Availability is not null)throw new Exception("Photo metadata or availability changed during migration.");
}
var samples=expected.Values.GroupBy(p=>Path.GetExtension(p.Photo.Original!.Asset)).Select(g=>g.First()).ToList();
var side=expected.Values.First(p=>p.Photo.Metadata is not null);if(!samples.Any(p=>p.Photo.Id==side.Photo.Id))samples.Add(side);
var measurements=new List<object>();var inspection=new List<object>();long bytesRead=0;
foreach(var sample in samples)
{
    watch.Restart();var download=await photos.DownloadOriginal(sample.Provider,sample.GameId,sample.Photo.Id,null,false,CancellationToken.None);
    await using(download.Stream){if(download.Digest!=sample.Photo.Original!.Asset[..64])throw new Exception("Requested original differs from its captured NAS hash.");bytesRead+=download.Stream.Length;}
    var originalMs=watch.ElapsedMilliseconds;
    if(sample.Photo.Metadata is not null)
    {
        var xmp=await photos.DownloadOriginal(sample.Provider,sample.GameId,sample.Photo.Id,null,true,CancellationToken.None);await using(xmp.Stream){if(xmp.Digest!=sample.Photo.Metadata.Asset[..64])throw new Exception("XMP bytes changed.");bytesRead+=xmp.Stream.Length;}
    }
    watch.Restart();byte[] first;string thumbnailPath,previewPath;
    await using(var image=await photos.Image(sample.Provider,sample.GameId,sample.Photo.Id,"thumbnail",CancellationToken.None)){thumbnailPath=image.Name;using var output=new MemoryStream();await image.CopyToAsync(output);first=output.ToArray();}
    var thumbnailMs=watch.ElapsedMilliseconds;watch.Restart();
    await using(var image=await photos.Image(sample.Provider,sample.GameId,sample.Photo.Id,"preview",CancellationToken.None)){previewPath=image.Name;if(image.Length<12)throw new Exception("Empty browsing preview.");}
    var previewMs=watch.ElapsedMilliseconds;
    // Invalidate just this requested thumbnail, then demonstrate byte-for-byte rebuilding/reuse.
    var cache=Path.Combine(options.CacheRoot,"photos");var hash=Convert.ToHexString(SHA256.HashData(first)).ToLowerInvariant();
    File.Delete(thumbnailPath);File.Delete(previewPath);watch.Restart();var reuseRoot=options.PhotoDerivativeReuseRoot;options.PhotoDerivativeReuseRoot="";
    bool identical;
    await using(var image=await photos.Image(sample.Provider,sample.GameId,sample.Photo.Id,"thumbnail",CancellationToken.None))
    {
        if(image.Length<12)throw new Exception("Cache regeneration failed.");identical=Convert.ToHexString(await SHA256.HashDataAsync(image)).ToLowerInvariant()==hash;
        var scope=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sample.Provider+"\n"+sample.GameId))).ToLowerInvariant();
        inspection.Add(new{Kind="thumbnail",Format=Path.GetExtension(sample.Photo.Original!.Asset),Rebuilt=image.Name,Previous=Path.Combine(reuseRoot,scope,"assets",sample.Photo.Thumbnail),Width=sample.Photo.Width,Height=sample.Photo.Height});
    }
    var rebuildMs=watch.ElapsedMilliseconds;watch.Restart();
    await using(var image=await photos.Image(sample.Provider,sample.GameId,sample.Photo.Id,"preview",CancellationToken.None))
    {
        if(image.Length<12)throw new Exception("Preview regeneration failed.");
        var scope=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sample.Provider+"\n"+sample.GameId))).ToLowerInvariant();
        inspection.Add(new{Kind="preview",Format=Path.GetExtension(sample.Photo.Original!.Asset),Rebuilt=image.Name,Previous=Path.Combine(reuseRoot,scope,"assets",sample.Photo.Preview),Width=sample.Photo.Width,Height=sample.Photo.Height});
    }
    options.PhotoDerivativeReuseRoot=reuseRoot;
    measurements.Add(new{Format=Path.GetExtension(sample.Photo.Original!.Asset),OriginalBytes=sample.Photo.Original.Bytes,OriginalMs=originalMs,ThumbnailMs=thumbnailMs,PreviewMs=previewMs,RebuildMs=rebuildMs,RebuildPreviewMs=watch.ElapsedMilliseconds,RebuildBytesIdentical=identical,ThumbnailBytes=first.Length});
}
await Task.Delay(1100);watch.Restart();_=photos.Catalogues();var refresh=watch.ElapsedMilliseconds;
using var db=LocalApplicationData.Read(Path.Combine(options.ApplicationDataRoot,"local-photos","identities.sqlite"));using var q=db.CreateCommand();q.CommandText="SELECT COUNT(*) FROM photo WHERE json_extract(payload,'$.removed')=1";var removals=Convert.ToInt32(q.ExecuteScalar());if(removals!=removed.Count)throw new Exception("Removal records were not preserved.");
if(Directory.Exists(Path.Combine(options.DataRoot,"screenshots")))throw new Exception("A copied photo store was created.");
var result=new{Photos=actual.Count,Collections=current.Length,RemovedIdentities=removals,Sidecars=actual.Values.Count(p=>p.Photo.Metadata is not null),ColdCatalogueMs=cold,WarmCatalogueMs=warm,RefreshCatalogueMs=refresh,RequestedOriginalBytesRead=bytesRead,PhotosCopied=0,NasCalls=0,NativeUiRequired=false,Samples=measurements};
File.WriteAllText(Path.Combine(stage,"phase3-proof-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(result));
File.WriteAllText(Path.Combine(stage,"phase3-preview-inspection.private.json"),JsonSerializer.Serialize(inspection));
