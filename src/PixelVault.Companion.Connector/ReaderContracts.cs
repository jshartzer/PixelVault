using System.Security.Cryptography;
using System.Text;

namespace PixelVaultNative;

// The connector compiles the existing isolated readers directly, without starting Native or its local writer.
sealed class TrackerSourceObservation {public string SnapshotJson {get;init;} public string Fingerprint {get;init;}}
interface ITrackerDataSource
{
    string Id {get;}string Version {get;}TrackerSourceCapabilities Capabilities {get;}
    void Validate(TrackerSourceBinding binding,AchievementTrackerDefinition definition);
    Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding binding,AchievementTrackerDefinition definition,CancellationToken token);
}
interface ITrackerSourceChangeDetector {string ChangeSignature(TrackerSourceBinding binding);}
interface ITrackerGameFileSource {}
static class TrackerSourceFiles
{
    internal static string Signature(IEnumerable<FileInfo> candidates,int maximumBytes)
    {
        var files=candidates.ToList();if(files.Count==0)throw new InvalidDataException("No source files found.");
        var first=files[0];var length=first.Length;var stamp=first.LastWriteTimeUtc;
        if(length>maximumBytes)throw new InvalidDataException("The source exceeds its size limit.");
        using var input=new FileStream(first.FullName,FileMode.Open,FileAccess.Read,FileShare.Read);var hash=Convert.ToHexString(SHA256.HashData(input));first.Refresh();
        if(first.Length!=length||first.LastWriteTimeUtc!=stamp)throw new IOException("The source is being saved.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash+string.Join("\n",files.Select(f=>f.FullName+f.Length+f.LastWriteTimeUtc.Ticks)))));
    }
}
