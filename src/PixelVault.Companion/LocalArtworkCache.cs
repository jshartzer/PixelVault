using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelVault.Companion;

/// <summary>Small browsing copies of canonical artwork; originals remain unchanged.</summary>
public sealed class LocalArtworkCache(CompanionOptions options)
{
    readonly SemaphoreSlim[] locks=Enumerable.Range(0,32).Select(_=>new SemaphoreSlim(1,1)).ToArray();
    static readonly SemaphoreSlim slots=new(2,2);
    readonly object evictionGate=new();
    const long Quota=128L*1024*1024;
    public async Task<(string Path,string ContentType)> Get((string Path,string ContentType) artwork,string kind,CancellationToken token)
    {
        if(!options.DirectLocal)return artwork;
        if(kind is not ("cover" or "banner"))throw new KeyNotFoundException();
        var sourceRoot=Path.Combine(options.ApplicationDataRoot,"cache","covers");
        LocalApplicationData.RequireFile(artwork.Path,sourceRoot);
        var opened=LocalPhotoFile.Open(sourceRoot,Path.GetRelativePath(sourceRoot,artwork.Path),true);
        await using var input=opened.Stream;
        if(input.Length is <1 or >64L*1024*1024)throw new InvalidDataException("The artwork is too large.");
        var max=kind=="cover"?480:2560;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("artwork-v1\n"+opened.Stamp.Version+"\n"+max))).ToLowerInvariant();
        var cache=Path.Combine(options.CacheRoot,"library-artwork");Directory.CreateDirectory(cache);LocalPhotoFile.CheckRoot(cache);
        var target=Path.Combine(cache,key+".jpg");var cacheLock=locks[Convert.ToByte(key[..2],16)%locks.Length];
        await cacheLock.WaitAsync(token);
        string? staging=null;
        try
        {
            if(Valid(target,cache)){File.SetLastWriteTimeUtc(target,DateTime.UtcNow);return(target,"image/jpeg");}
            await slots.WaitAsync(token);
            try
            {
                staging=Path.Combine(cache,"work-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
                var output=await ConvertImage(input,Path.GetExtension(artwork.Path),staging,max,token);
                LocalApplicationData.RequireFile(output,staging);
                var bytes=await File.ReadAllBytesAsync(output,token);
                if(bytes.Length is <12 or >6*1024*1024||bytes[0]!=255||bytes[1]!=216||bytes[^2]!=255||bytes[^1]!=217)throw new InvalidDataException("Invalid artwork preview.");
                var temporary=Path.Combine(staging,"completed.jpg");await File.WriteAllBytesAsync(temporary,bytes,token);
                lock(evictionGate)
                {
                    var files=Directory.EnumerateFiles(cache,"*.jpg").Where(p=>p!=target).Select(p=>new FileInfo(p)).OrderBy(f=>f.LastWriteTimeUtc).ToArray();var used=files.Sum(f=>f.Length);
                    foreach(var file in files){if(used+bytes.Length<=Quota)break;try{File.Delete(file.FullName);used-=file.Length;}catch(IOException){}}
                    if(used+bytes.Length>Quota)throw new IOException("The artwork cache is busy.");
                    File.Move(temporary,target,true);
                }
                return(target,"image/jpeg");
            }
            finally{slots.Release();}
        }
        finally
        {
            try
            {
                if(staging is not null)
                {
                    var assets=Path.Combine(staging,"assets");
                    if(Directory.Exists(assets)){foreach(var file in Directory.EnumerateFiles(assets))File.Delete(file);Directory.Delete(assets);}
                    foreach(var file in Directory.EnumerateFiles(staging))File.Delete(file);Directory.Delete(staging);
                }
            }
            finally{cacheLock.Release();}
        }
    }
    static bool Valid(string file,string root)
    {
        if(!File.Exists(file))return false;LocalApplicationData.RequireFile(file,root);
        using var stream=File.OpenRead(file);if(stream.Length is <12 or >6*1024*1024)return false;
        if(stream.ReadByte()!=255||stream.ReadByte()!=216)return false;stream.Position=stream.Length-2;return stream.ReadByte()==255&&stream.ReadByte()==217;
    }
    async Task<string> ConvertImage(FileStream input,string extension,string staging,int max,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(options.PhotoWorkerPath)||!File.Exists(options.PhotoWorkerPath))throw new InvalidOperationException("The artwork worker is unavailable.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add(options.PhotoWorkerPath);
        using var process=Process.Start(start)??throw new IOException("The artwork worker could not start.");using var lifetime=new LocalWorkerLifetime(process);
        try
        {
            var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{Extension=extension,Bytes=input.Length,Output=staging,Max=max}));await process.StandardInput.FlushAsync(timeout.Token);
            await input.CopyToAsync(process.StandardInput.BaseStream,timeout.Token);process.StandardInput.Close();await process.WaitForExitAsync(timeout.Token);var text=await stdout;_=await stderr;
            if(process.ExitCode!=0||text.Length>4096)throw new IOException("The artwork preview could not be generated.");
            var asset=JsonDocument.Parse(text).RootElement.GetProperty("Asset").GetString()??"";
            if(!System.Text.RegularExpressions.Regex.IsMatch(asset,"^[a-f0-9]{64}\\.jpg$"))throw new InvalidDataException("Invalid artwork result.");
            return Path.Combine(staging,"assets",asset);
        }
        catch{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}throw;}
    }
}
