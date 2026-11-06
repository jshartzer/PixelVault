using System.Diagnostics;
using System.Text.Json;
using PixelVaultNative;
namespace PixelVault.Companion;

public interface ILocalReaderClient
{
    Task<LocalReaderResult> Run(LocalReaderRequest request,CancellationToken token);
}
public sealed class LocalReaderClient(CompanionOptions options):ILocalReaderClient
{
    public async Task<LocalReaderResult> Run(LocalReaderRequest request,CancellationToken token)
    {
        if(!Path.IsPathFullyQualified(options.ReaderWorkerPath)||!File.Exists(options.ReaderWorkerPath))throw new InvalidOperationException("The local reader worker is unavailable.");
        var root=Path.Combine(options.CacheRoot,"reader-jobs");Directory.CreateDirectory(root);
        var directory=Path.Combine(root,"run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var input=Path.Combine(directory,"request.json");var output=Path.Combine(directory,"result.json");
        await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request,AchievementTrackingCatalog.Json),token);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(75));
        var start=new ProcessStartInfo(options.ReaderWorkerPath){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=directory};start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"]=Path.Combine(options.CacheRoot,"worker-bundles");start.ArgumentList.Add("--companion-readers");start.ArgumentList.Add(input);
        try
        {
            using var process=Process.Start(start)??throw new InvalidOperationException("The local reader could not start.");
            using var lifetime=new LocalWorkerLifetime(process);
            try
            {
                try{process.PriorityClass=ProcessPriorityClass.BelowNormal;}catch(System.ComponentModel.Win32Exception){}
                await process.WaitForExitAsync(timeout.Token);
                LocalApplicationData.RequireFile(output,options.CacheRoot);
                if(new FileInfo(output).Length>4*1024*1024)throw new InvalidDataException("The reader result exceeded its limit.");
                var result=JsonSerializer.Deserialize<LocalReaderResult>(await File.ReadAllTextAsync(output,token),AchievementTrackingCatalog.Json)??throw new InvalidDataException("Invalid reader result.");
                if(!result.Success)
                {
                    var diagnostic=Path.Combine(directory,"reader-diagnostic.private.txt");
                    if(File.Exists(diagnostic)&&new FileInfo(diagnostic).Length<=16384){var logs=Path.Combine(options.DataRoot,"logs");Directory.CreateDirectory(logs);File.Copy(diagnostic,Path.Combine(logs,"reader-error.private.txt"),true);}
                    throw new InvalidOperationException("The configured source could not be read. Saved progress was preserved.");
                }
                return result;
            }
            catch
            {
                if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));}
                throw;
            }
        }
        finally
        {
            foreach(var name in new[]{"request.json","result.json","result.json.tmp","reader-diagnostic.private.txt"})try{File.Delete(Path.Combine(directory,name));}catch(IOException){}
            try{Directory.Delete(directory);}catch(IOException){}
        }
    }
}
