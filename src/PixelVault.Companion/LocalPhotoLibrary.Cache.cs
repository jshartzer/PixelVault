using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace PixelVault.Companion;

public sealed partial class LocalPhotoLibrary
{
    const string CodecVersion="full-frame-hdr-reinhard-v3-1";
    static readonly SemaphoreSlim conversionSlots=new(2,2);
    readonly SemaphoreSlim[] cacheLocks=Enumerable.Range(0,64).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    readonly object cacheGate=new();
    long lastCleanup=long.MinValue;
    sealed record Converted(string Asset,int Width,int Height,string Fingerprint);
    sealed record CachedPreview(string Digest,int Width,int Height);
    sealed record Reusable(string Fingerprint,int Width,int Height,string Thumbnail,string Preview,string? Conversion);
    string Cache=>Path.Combine(options.CacheRoot,"photos");
    string CacheKey(Entry entry,string kind)=>LocalPhotoIndex.Hash(CodecVersion+"\n"+entry.Stamp!.Version+"\n"+kind);
    public async Task<FileStream> Image(string provider,string game,string id,string kind,CancellationToken token)
    {
        if(kind is not ("thumbnail" or "tile960" or "tile1280" or "preview"))throw new KeyNotFoundException();
        var entry=Resolve(provider,game,id);var key=CacheKey(entry,kind);var cacheLock=cacheLocks[Convert.ToByte(key[..2],16)%cacheLocks.Length];
        var temporaryFiles=new List<string>();var temporaryDirectories=new List<string>();
        Directory.CreateDirectory(Cache);LocalPhotoFile.CheckRoot(Cache);
        lock(cacheGate)if(lastCleanup==long.MinValue||Environment.TickCount64-lastCleanup>60000){PruneAbandoned();lastCleanup=Environment.TickCount64;}
        if(options.PhotoCacheBytes<8L*1024*1024)throw new InvalidDataException("The photo cache quota is too small.");
        await cacheLock.WaitAsync(token);
        try
        {
            // Check the requested original even on a cache hit; a pending replacement must not serve its old preview.
            var opened=LocalPhotoFile.Open(config.Root,entry.Relative,true);await using var original=opened.Stream;
            if(opened.Stamp.Version!=entry.Stamp!.Version)throw new InvalidOperationException("The photo changed. Refresh photos and try again.");
            var hit=Cached(key);if(hit is not null)return hit;
            await conversionSlots.WaitAsync(token);
            try
            {
                var reused=await Reuse(entry,kind,original,temporaryFiles,token);
                Converted converted;string output;
                if(reused is not null){converted=reused.Value.Result;output=reused.Value.File;}
                else
                {
                    if(!Path.IsPathFullyQualified(options.PhotoWorkerPath)||!File.Exists(options.PhotoWorkerPath))throw new InvalidOperationException("The Windows photo worker is unavailable.");
                    var staging=Path.Combine(Cache,"work-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);temporaryDirectories.Add(staging);
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
                    var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(options.PhotoWorkerPath);
                    using var process=Process.Start(start)??throw new InvalidOperationException("The photo worker could not start.");
                    using var lifetime=new LocalWorkerLifetime(process);
                    try
                    {
                        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Extension=Path.GetExtension(entry.Relative),Bytes=opened.Stamp.Bytes,Output=staging,Max=kind switch{"thumbnail"=>480,"tile960"=>960,"tile1280"=>1280,_=>2560}}));await process.StandardInput.FlushAsync(timeout.Token);
                        original.Position=0;await original.CopyToAsync(process.StandardInput.BaseStream,timeout.Token);process.StandardInput.Close();
                        var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var error=process.StandardError.ReadToEndAsync(timeout.Token);
                        await process.WaitForExitAsync(timeout.Token);var text=await stdout;_=await error;
                        if(process.ExitCode!=0||text.Length>4096)throw new InvalidOperationException("This photo's preview could not be generated.");
                        converted=JsonSerializer.Deserialize<Converted>(text)??throw new InvalidDataException("Invalid preview result.");
                        if(!System.Text.RegularExpressions.Regex.IsMatch(converted.Asset,"^[a-f0-9]{64}\\.jpg$")||converted.Width is <1 or >40000||converted.Height is <1 or >40000)throw new InvalidDataException("Invalid preview result.");
                        output=Path.Combine(staging,"assets",converted.Asset);
                    }
                    catch
                    {
                        if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}
                        var assets=Path.Combine(staging,"assets");if(Directory.Exists(assets))foreach(var leftover in Directory.EnumerateFiles(assets,"*.jpg"))File.Delete(leftover);
                        throw;
                    }
                }
                temporaryFiles.Add(output);
                LocalApplicationData.RequireFile(output,Cache);
                var bytes=await File.ReadAllBytesAsync(output,token);var digest=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if(bytes.Length is <12 or >6*1024*1024||digest+".jpg"!=converted.Asset||bytes[0]!=255||bytes[1]!=216||bytes[^2]!=255||bytes[^1]!=217)throw new InvalidDataException("Invalid generated preview.");
                var target=Path.Combine(Cache,key+".jpg");var temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                temporaryFiles.Add(temporary);
                await File.WriteAllBytesAsync(temporary,bytes,token);
                var metadata=target+".json";var metaTemp=metadata+"."+Guid.NewGuid().ToString("N")+".tmp";
                temporaryFiles.Add(metaTemp);
                await File.WriteAllTextAsync(metaTemp,JsonSerializer.Serialize(new CachedPreview(digest,converted.Width,converted.Height)),token);
                FileStream result;
                lock(cacheGate)
                {
                    Evict(bytes.LongLength,target);File.Move(temporary,target,true);File.Move(metaTemp,metadata,true);
                    result=Cached(key)??throw new IOException("The photo cache is unavailable.");
                }
                try{RecordDimensions(entry,converted.Width,converted.Height);return result;}catch{result.Dispose();throw;}
            }
            finally{conversionSlots.Release();}
        }
        finally
        {
            try
            {
                foreach(var file in temporaryFiles)try{File.Delete(file);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
                foreach(var directory in temporaryDirectories)
                {
                    var assets=Path.Combine(directory,"assets");
                    try{if(Directory.Exists(assets)){foreach(var file in Directory.EnumerateFiles(assets,"*.jpg"))File.Delete(file);Directory.Delete(assets);}Directory.Delete(directory);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
                }
            }
            finally{cacheLock.Release();}
        }
    }
    FileStream? Cached(string key)
    {
        var file=Path.Combine(Cache,key+".jpg");var metadata=file+".json";if(!File.Exists(file)||!File.Exists(metadata))return null;
        try
        {
            LocalApplicationData.RequireFile(metadata,Cache);var expected=JsonSerializer.Deserialize<CachedPreview>(File.ReadAllText(metadata));
            try{File.SetLastWriteTimeUtc(file,DateTime.UtcNow);}catch(IOException){}
            var opened=LocalPhotoFile.Open(Cache,key+".jpg",true);var stream=opened.Stream;
            if(stream.Length is <12 or >6*1024*1024){stream.Dispose();return null;}
            var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();stream.Position=0;
            if(hash!=expected?.Digest){stream.Dispose();return null;}
            return stream;
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException){return null;}
    }
    async Task<(Converted Result,string File)?> Reuse(Entry entry,string kind,FileStream original,List<string> temporaryFiles,CancellationToken token)
    {
        if(kind is "tile960" or "tile1280")return null;
        var seed=entry.Seed;var source=seed?.Original;
        if(string.IsNullOrEmpty(options.PhotoDerivativeReuseRoot)||source is null||source.Bytes!=entry.Stamp!.Bytes||source.LastWriteUtcTicks!=entry.Stamp.Modified)return null;
        var name=kind=="thumbnail"?seed!.Thumbnail:seed!.Preview;
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^[a-f0-9]{64}\\.jpg$"))return null;
        var relative=Path.Combine(LocalPhotoIndex.Hash(entry.Provider+"\n"+entry.Game),"assets",name);
        try
        {
            var index=LocalPhotoFile.Open(options.PhotoDerivativeReuseRoot,Path.Combine(LocalPhotoIndex.Hash(entry.Provider+"\n"+entry.Game),"source-index.private.json"),true);
            Reusable? compatible;
            await using(index.Stream)
            {
                if(index.Stream.Length>6*1024*1024)return null;
                var records=await JsonSerializer.DeserializeAsync<Dictionary<string,Reusable>>(index.Stream,Json,token);compatible=records?.GetValueOrDefault(entry.Photo.Id);
            }
            if(compatible is null||compatible.Fingerprint!=source.Asset[..64]||compatible.Width!=seed!.Width||compatible.Height!=seed.Height||compatible.Thumbnail!=seed.Thumbnail||compatible.Preview!=seed.Preview||Path.GetExtension(entry.Relative).Equals(".jxr",StringComparison.OrdinalIgnoreCase)&&compatible.Conversion!="hdr-reinhard-v3")return null;
            var opened=LocalPhotoFile.Open(options.PhotoDerivativeReuseRoot,relative,true);await using var input=opened.Stream;
            if(input.Length is <12 or >6*1024*1024)return null;
            var fingerprint=Convert.ToHexString(await SHA256.HashDataAsync(original,token)).ToLowerInvariant();original.Position=0;
            if(fingerprint!=source.Asset[..64])return null;
            var bytes=new byte[(int)input.Length];await input.ReadExactlyAsync(bytes,token);if(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+".jpg"!=name)return null;
            var output=Path.Combine(Cache,"reuse-"+Guid.NewGuid().ToString("N")+".jpg");temporaryFiles.Add(output);await File.WriteAllBytesAsync(output,bytes,token);
            return(new(name,seed!.Width,seed.Height,fingerprint),output);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){return null;}
    }
    void Evict(long incoming,string replacing)
    {
        var files=Directory.EnumerateFiles(Cache,"*.jpg").Where(f=>f!=replacing&&Path.GetFileNameWithoutExtension(f).Length==64).Select(f=>new FileInfo(f)).OrderBy(f=>f.LastWriteTimeUtc).ToArray();var used=files.Sum(f=>f.Length);
        foreach(var file in files)
        {
            if(used+incoming<=options.PhotoCacheBytes)break;
            try{var length=file.Length;File.Delete(file.FullName);File.Delete(file.FullName+".json");used-=length;}catch(IOException){}
        }
        if(used+incoming>options.PhotoCacheBytes)throw new InvalidOperationException("The photo cache is busy. Try again shortly.");
    }
    void PruneAbandoned()
    {
        // Crash leftovers only, in the validated disposable cache. Active conversions time out after one minute.
        var before=DateTime.UtcNow.AddMinutes(-10);
        foreach(var file in Directory.EnumerateFiles(Cache).Where(p=>Path.GetFileName(p).EndsWith(".tmp",StringComparison.Ordinal)||System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),"^reuse-[a-f0-9]{32}\\.jpg$")))
        {
            try{LocalApplicationData.RequireFile(file,Cache);if(File.GetLastWriteTimeUtc(file)<before)File.Delete(file);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
        }
        foreach(var directory in Directory.EnumerateDirectories(Cache).Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),"^work-[a-f0-9]{32}$")))
        {
            try
            {
                if(Directory.GetLastWriteTimeUtc(directory)>=before)continue;LocalPhotoFile.CheckRoot(directory);var assets=Path.Combine(directory,"assets");
                if(Directory.Exists(assets))
                {
                    LocalPhotoFile.CheckRoot(assets);
                    foreach(var file in Directory.EnumerateFiles(assets,"*.jpg")){LocalApplicationData.RequireFile(file,Cache);File.Delete(file);}
                    Directory.Delete(assets);
                }
                Directory.Delete(directory);
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){}
        }
    }
    void RecordDimensions(Entry entry,int width,int height)
    {
        if(entry.Photo.Width==width&&entry.Photo.Height==height)return;
        lock(gate)
        {
            using var c=Open(Database);using var tx=c.BeginTransaction();var current=Load(c,tx).GetValueOrDefault(Key(entry));
            if(current?.Stamp?.Version==entry.Stamp!.Version&&(current.Photo.Width!=width||current.Photo.Height!=height))
                Save(c,tx,current with{Photo=current.Photo with{Width=width,Height=height}});
            tx.Commit();expires=0;
        }
    }
}
