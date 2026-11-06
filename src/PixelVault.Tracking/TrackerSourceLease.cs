using System.Security.Cryptography;
using System.Text;
namespace PixelVaultNative;

/// <summary>OS-owned cross-process exclusion for readers of the same approved save source.</summary>
public sealed class TrackerSourceLease:IDisposable
{
    readonly FileStream stream;
    TrackerSourceLease(FileStream stream)=>this.stream=stream;
    public static FileStream OwnBackground(string database)=>new(Path.Combine(Path.GetDirectoryName(database)!,"background-owner.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    public static bool BackgroundOwned(string database)
    {
        var file=Path.Combine(Path.GetDirectoryName(database)!,"background-owner.lock");if(!File.Exists(file))return false;
        try{using var check=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);return false;}catch(IOException){return true;}
    }
    static string OpenSignalFile(string database,string tracker,string account)=>Path.Combine(Path.GetDirectoryName(database)!,"open-signals",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tracker+"\n"+account)))+".txt");
    public static void SignalOpen(string database,string tracker,string account)
    {
        var file=OpenSignalFile(database,tracker,account);Directory.CreateDirectory(Path.GetDirectoryName(file)!);var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,Guid.NewGuid().ToString("N"));File.Move(temp,file,true);
    }
    public static string OpenSignal(string database,string tracker,string account)
    {
        try
        {
            using var input=new FileStream(OpenSignalFile(database,tracker,account),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);if(input.Length>64)return "";
            using var reader=new StreamReader(input);var token=reader.ReadToEnd();return Guid.TryParse(token,out _)?token:"";
        }
        catch(FileNotFoundException){return "";}catch(DirectoryNotFoundException){return "";}
    }
    public static async Task<TrackerSourceLease> AcquireAsync(string database,TrackerSourceBinding binding,CancellationToken token)
    {
        var source=string.Join("\n",binding.AdapterId,binding.Account,Path.GetFullPath(binding.Location).ToUpperInvariant(),binding.SpecificSave??"",binding.GameDirectory??"");
        var root=Path.Combine(Path.GetDirectoryName(database)!,"reader-locks");Directory.CreateDirectory(root);
        var file=Path.Combine(root,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))+".lock");
        while(true)
        {
            token.ThrowIfCancellationRequested();
            try{return new(new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
            catch(IOException){await Task.Delay(200,token).ConfigureAwait(false);}
        }
    }
    public void Dispose()=>stream.Dispose();
}
