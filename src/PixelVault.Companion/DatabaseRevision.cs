using System.Security.Cryptography;

namespace PixelVault.Companion;

// Windows file identity/change-time covers recycled WAL writes even when size and mtime
// are restored. Release handles immediately so offline/restore workflows can move the store.
internal sealed class DatabaseRevision(string path):IDisposable
{
    public string Read()
    {
        string Stamp(string file)
        {
            if(!File.Exists(file))return "missing";
            try
            {
                if(OperatingSystem.IsWindows())return LocalPhotoFile.Inspect(Path.GetDirectoryName(file)!,Path.GetFileName(file)).Version;
                using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                return Convert.ToHexString(SHA256.HashData(input));
            }
            catch(FileNotFoundException){return "missing";}
        }
        return Stamp(path)+":"+Stamp(path+"-wal");
    }
    public void Dispose(){}
}
