using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;
public sealed partial class ScreenshotLibrary
{
    readonly SemaphoreSlim originalGate=new(1,1);
    string OriginalRoot=>Path.Combine(Root,"originals");
    const long OriginalQuota=4L*1024*1024*1024;
    const long MaxOriginal=64L*1024*1024;
    static readonly Dictionary<string,string> OriginalTypes=new(StringComparer.Ordinal){[".png"]="image/png",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg",[".webp"]="image/webp",[".gif"]="image/gif",[".bmp"]="image/bmp",[".jxr"]="image/vnd.ms-photo"};
    static bool ValidOriginal(ScreenshotOriginal o)=>Regex.IsMatch(o.Asset??"","^[a-f0-9]{64}\\.(png|jpg|jpeg|webp|gif|bmp|jxr)$")
        &&o.FileName is {Length:>0 and <=255}&&o.FileName.IndexOfAny(['/', '\\'])<0&&!o.FileName.Any(char.IsControl)
        &&o.Bytes is >0 and <=MaxOriginal&&o.LastWriteUtcTicks>0&&o.LastWriteUtcTicks<=DateTime.MaxValue.Ticks
        &&Path.GetExtension(o.FileName).Equals(Path.GetExtension(o.Asset),StringComparison.OrdinalIgnoreCase)
        &&OriginalTypes.TryGetValue(Path.GetExtension(o.Asset)??"",out var mime)&&o.ContentType==mime;
    static ScreenshotOriginal[] Originals(ScreenshotCatalogue c)=>c.Photos.Select(p=>p.Original).OfType<ScreenshotOriginal>().GroupBy(o=>o.Asset,StringComparer.Ordinal).Select(g=>g.First()).ToArray();
    bool VerifiedOriginal(ScreenshotOriginal o)
    {
        var file=Path.Combine(OriginalRoot,o.Asset);if(!File.Exists(file))return false;var info=new FileInfo(file);
        if(info.Length!=o.Bytes||(info.Attributes&FileAttributes.ReparsePoint)!=0)return false;
        using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()==o.Asset[..64];
    }
    public async Task UploadOriginal(string id,string name,Stream input,CancellationToken token)
    {
        await originalGate.WaitAsync(token);string? temporary=null;
        try
        {
            ScreenshotOriginal original;
            lock(gate)
            {
                Enabled();var c=Read(PlanPath(id));original=Originals(c).FirstOrDefault(o=>o.Asset==name)??throw new InvalidDataException("Unexpected original file.");
                if(VerifiedOriginal(original))return;Directory.CreateDirectory(OriginalRoot);temporary=Path.Combine(OriginalRoot,"incoming.upload");
                if(File.Exists(temporary))File.Delete(temporary);
                if(Directory.EnumerateFiles(OriginalRoot).Sum(f=>new FileInfo(f).Length)+original.Bytes>OriginalQuota)throw new InvalidOperationException("Original photo storage limit reached.");
            }
            long length=0;using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
            {
                Protect(temporary);var buffer=new byte[65536];int read;
                while((read=await input.ReadAsync(buffer,token))>0){length+=read;if(length>original.Bytes||length>MaxOriginal)throw new InvalidDataException("Original photo size changed.");digest.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),token);}
            }
            if(length!=original.Bytes||Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant()!=name[..64])throw new InvalidDataException("Original photo hash did not match.");
            lock(gate)
            {
                Enabled();if(!Originals(Read(PlanPath(id))).Any(o=>o==original))throw new InvalidOperationException("The photo upload changed.");
                var file=Path.Combine(OriginalRoot,name);File.Move(temporary,file,true);temporary=null;File.SetLastWriteTimeUtc(file,new DateTime(original.LastWriteUtcTicks,DateTimeKind.Utc));
            }
        }
        finally{if(temporary is not null&&File.Exists(temporary))File.Delete(temporary);originalGate.Release();}
    }
    public (string Path,ScreenshotOriginal Original) Original(string provider,string game,string id)
    {
        lock(gate)
        {
            var photo=Photo(provider,game,id);var original=photo.Original??throw new KeyNotFoundException();
            if(!VerifiedOriginal(original))throw new InvalidOperationException("The original file is unavailable. Re-sync photos from the PC.");
            return(Path.Combine(OriginalRoot,original.Asset),original);
        }
    }
}
