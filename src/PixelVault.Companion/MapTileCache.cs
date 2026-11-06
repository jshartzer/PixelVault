using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PixelVaultNative;

namespace PixelVault.Companion;

public sealed record MapTileManifest(string Version,int Width,int Height,int TileSize,int MaxZoom,string TileUrl,string OverviewUrl);

/// <summary>Versioned, rebuildable tile pyramids. Only the worker decodes original images.</summary>
public sealed class MapTileCache(CompanionOptions options,MapLibrary maps)
{
    // A single large map conversion at a time; cache access/publication/eviction shares this gate.
    static readonly SemaphoreSlim gate=new(1,1);
    const string Generator="map-jpeg-512-v1";
    sealed record Index(int Width,int Height,int MaxZoom,Dictionary<string,string> Tiles);
    string Root=>Path.Combine(options.CacheRoot,"maps");
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static int Zoom(int width,int height)=>Math.Max(0,(int)Math.Ceiling(Math.Log2(Math.Max(width,height)/512d)));

    public Task<MapTileManifest> Manifest(string provider,string game,string id,CancellationToken token)
        =>Access(provider,game,id,null,null,token);

    public async Task<(FileStream Stream,string Digest)> Tile(string provider,string game,string id,int z,int x,int y,string version,CancellationToken token)
    {
        var definition=maps.Definition(provider,game,id);var max=Zoom(definition.PixelWidth,definition.PixelHeight);
        if(z<0||z>max||x<0||y<0)throw new KeyNotFoundException();
        var divisor=1<<(max-z);
        if(x>=((definition.PixelWidth+divisor-1)/divisor+511)/512||y>=((definition.PixelHeight+divisor-1)/divisor+511)/512)throw new KeyNotFoundException();
        FileStream? stream=null;string digest="";
        await Access(provider,game,id,version,(directory,index)=>
        {
            if(z<0||z>index.MaxZoom||x<0||y<0)throw new KeyNotFoundException();
            var name=$"{z}-{x}-{y}.jpg";
            if(!index.Tiles.TryGetValue(name,out digest!))throw new KeyNotFoundException();
            var opened=LocalPhotoFile.Open(directory,name,true);stream=opened.Stream;
            try
            {
                if(stream.Length>2*1024*1024||Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()!=digest)
                    throw new InvalidDataException("A map tile needs rebuilding.");
                stream.Position=0;
            }
            catch{stream.Dispose();stream=null;throw;}
        },token);
        return(stream!,digest);
    }

    async Task<MapTileManifest> Access(string provider,string game,string id,string? version,Action<string,Index>? action,CancellationToken token)
    {
        var map=maps.Definition(provider,game,id);
        if(!Regex.IsMatch(map.ImageFile??"","^[A-Fa-f0-9]{64}\\.(png|jpg)$"))throw new MapLibraryUnavailableException(new InvalidDataException());
        await gate.WaitAsync(token);
        string? staging=null;
        try
        {
            var sourceRoot=Path.Combine(options.ApplicationDataRoot,"game-maps","images");
            var opened=LocalPhotoFile.Open(sourceRoot,map.ImageFile!,true);await using var original=opened.Stream;
            if(original.Length is <1 or >160L*1024*1024)throw new InvalidDataException();
            var key=Hash(Generator+"\n"+map.ImageFile!.ToLowerInvariant()+"\n"+opened.Stamp.Version+"\n"+map.PixelWidth+"x"+map.PixelHeight);
            if(version is not null&&version!=key)throw new KeyNotFoundException();
            Directory.CreateDirectory(Root);LocalPhotoFile.CheckRoot(Root);
            var target=Path.Combine(Root,key);Index? index=null;
            if(Directory.Exists(target))
            {
                try
                {
                    LocalPhotoFile.CheckRoot(target);
                    var openedIndex=LocalPhotoFile.Open(target,"index.json",true);await using var input=openedIndex.Stream;
                    if(input.Length>1024*1024)throw new InvalidDataException();
                    index=await JsonSerializer.DeserializeAsync<Index>(input,cancellationToken:token);
                    if(index is null||index.Tiles is null||index.Width!=map.PixelWidth||index.Height!=map.PixelHeight||index.MaxZoom!=Zoom(map.PixelWidth,map.PixelHeight)
                        ||!Expected(map).SequenceEqual(index.Tiles.Keys.Order(StringComparer.Ordinal))
                        ||index.Tiles.Values.Any(d=>!Regex.IsMatch(d,"^[a-f0-9]{64}$"))
                        ||index.Tiles.Keys.Any(n=>!File.Exists(Path.Combine(target,n))))throw new InvalidDataException();
                }
                catch(Exception e)when(e is IOException or JsonException or InvalidDataException){Remove(target);index=null;}
            }
            if(index is null)
            {
                staging=Path.Combine(Root,"work-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
                await Generate(original,map,staging,token);
                var tiles=new Dictionary<string,string>();long bytes=0;
                foreach(var name in Expected(map))
                {
                    var output=LocalPhotoFile.Open(staging,name,true);await using var tile=output.Stream;
                    if(tile.Length is <12 or >2*1024*1024)throw new InvalidDataException();
                    bytes+=tile.Length;tiles[name]=Convert.ToHexString(await SHA256.HashDataAsync(tile,token)).ToLowerInvariant();
                }
                index=new(map.PixelWidth,map.PixelHeight,Zoom(map.PixelWidth,map.PixelHeight),tiles);
                await File.WriteAllTextAsync(Path.Combine(staging,"index.json"),JsonSerializer.Serialize(index),token);
                bytes+=new FileInfo(Path.Combine(staging,"index.json")).Length;
                Evict(bytes,staging);Directory.Move(staging,target);staging=null;
            }
            Directory.SetLastWriteTimeUtc(target,DateTime.UtcNow);
            try{action?.Invoke(target,index);}
            catch(InvalidDataException){Remove(target);throw;}
            var prefix=$"/api/v1/maps/{Uri.EscapeDataString(provider)}/{Uri.EscapeDataString(game)}/{Uri.EscapeDataString(id)}/tiles";
            return new(key,map.PixelWidth,map.PixelHeight,512,index.MaxZoom,prefix+"/{z}/{x}/{y}?v="+key,prefix+"/0/0/0?v="+key);
        }
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {throw new MapLibraryUnavailableException(e);}
        finally
        {
            try{if(staging is not null)Remove(staging);}finally{gate.Release();}
        }
    }

    static IEnumerable<string> Expected(GameMapDefinition map)
    {
        var names=new List<string>();var max=Zoom(map.PixelWidth,map.PixelHeight);
        for(var z=0;z<=max;z++)
        {
            var divisor=1<<(max-z);var w=(map.PixelWidth+divisor-1)/divisor;var h=(map.PixelHeight+divisor-1)/divisor;
            for(var y=0;y<(h+511)/512;y++)for(var x=0;x<(w+511)/512;x++)names.Add($"{z}-{x}-{y}.jpg");
        }
        return names.Order(StringComparer.Ordinal);
    }

    void Evict(long incoming,string active)
    {
        if(incoming>options.MapCacheBytes||options.MapCacheBytes<1024)throw new IOException("The map cache quota is too small.");
        var directories=Directory.GetDirectories(Root).Where(p=>p!=active).Select(p=>new DirectoryInfo(p)).OrderBy(p=>p.LastWriteTimeUtc).ToArray();
        long used=0;
        foreach(var dir in directories){LocalPhotoFile.CheckRoot(dir.FullName);foreach(var file in dir.EnumerateFiles()){LocalApplicationData.RequireFile(file.FullName,Root);used+=file.Length;}}
        foreach(var dir in directories)
        {
            if(used+incoming<=options.MapCacheBytes&&!dir.Name.StartsWith("work-"))continue;
            var size=dir.EnumerateFiles().Sum(f=>f.Length);Remove(dir.FullName);used-=size;
        }
        if(used+incoming>options.MapCacheBytes)throw new IOException("The map cache is busy.");
    }

    void Remove(string directory)
    {
        // Only flat, generated directories under the validated cache root are removed.
        if(Path.GetDirectoryName(Path.GetFullPath(directory))!=Path.GetFullPath(Root))throw new InvalidDataException();
        LocalPhotoFile.CheckRoot(directory);
        foreach(var file in Directory.GetFiles(directory)){LocalApplicationData.RequireFile(file,Root);File.Delete(file);}
        Directory.Delete(directory);
    }

    async Task Generate(FileStream input,GameMapDefinition map,string output,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(options.PhotoWorkerPath)||!File.Exists(options.PhotoWorkerPath))throw new IOException("The map worker is unavailable.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(options.PhotoWorkerPath);
        using var process=Process.Start(start)??throw new IOException();using var lifetime=new LocalWorkerLifetime(process);
        try
        {
            var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Operation="map-tiles",Extension=Path.GetExtension(map.ImageFile),Bytes=input.Length,Output=output,Max=0,Width=map.PixelWidth,Height=map.PixelHeight}));
            await process.StandardInput.FlushAsync(timeout.Token);await input.CopyToAsync(process.StandardInput.BaseStream,timeout.Token);process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);var text=await stdout;_=await stderr;
            if(process.ExitCode!=0||text.Length>4096)throw new IOException("The map could not be converted.");
            using var result=JsonDocument.Parse(text);
            if(result.RootElement.GetProperty("Fingerprint").GetString()!=Path.GetFileNameWithoutExtension(map.ImageFile).ToLowerInvariant())throw new InvalidDataException();
        }
        catch
        {
            if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}
            if(!token.IsCancellationRequested&&timeout.IsCancellationRequested)throw new IOException("Map generation timed out.");
            throw;
        }
    }
}
