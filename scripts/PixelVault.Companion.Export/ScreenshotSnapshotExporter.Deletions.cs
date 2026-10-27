using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PixelVault.Companion.Export;

public static partial class ScreenshotSnapshotExporter
{
    public sealed record NativePhotoDeletion(string File,bool Missing,bool Indexed,bool Listed,string? EditionId);
    public sealed record PhotoDeletionEvidence(string Signature,IReadOnlyList<NativePhotoDeletion> Events);
    static readonly Dictionary<string,(long Length,long Stamp,string[] Paths)> deletionLogs=new(StringComparer.OrdinalIgnoreCase);
    // The existing Native app writes this exact success message after deleting a capture.
    // Missing files, disconnected drives, index rebuilds and Forget-row operations are not deletion evidence.
    public static PhotoDeletionEvidence ReadDeletionEvidence(string source,string workspace,CancellationToken token=default)
    {
        SQLitePCL.Batteries_V2.Init();
        var root=Path.GetFullPath(File.ReadLines(Path.Combine(source,"PixelVault.settings.ini")).First(l=>l.StartsWith("library=",StringComparison.OrdinalIgnoreCase)).Split('=',2)[1].Trim());
        if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("The indexed capture root is unavailable.");
        using(var accessible=Directory.EnumerateFileSystemEntries(root).GetEnumerator())_=accessible.MoveNext();
        var log=Path.Combine(source,"logs","PixelVault-native.log");var logInfo=new FileInfo(log);string[] paths=[];
        if(logInfo.Exists)
        {
            lock(deletionLogs)
            {
                if(!deletionLogs.TryGetValue(log,out var cached)||cached.Length!=logInfo.Length||cached.Stamp!=logInfo.LastWriteTimeUtc.Ticks)
                {
                    var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using var input=File.Open(log,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(input);
                    while(reader.ReadLine() is { } line)
                    {
                        token.ThrowIfCancellationRequested();if(!line.Contains("] Library delete: ",StringComparison.Ordinal))continue;
                        var match=Regex.Match(line,@"^\[([^\]\r\n]+)\] Library delete: (.+)$");
                        if(!match.Success||!DateTimeOffset.TryParse(match.Groups[1].Value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _))continue;
                        var path=match.Groups[2].Value;
                        if(!Path.IsPathFullyQualified(path)||!new[]{".jpg",".jpeg",".png",".bmp",".gif",".webp",".jxr"}.Contains(Path.GetExtension(path).ToLowerInvariant()))continue;
                        path=Path.GetFullPath(path);var rel=Path.GetRelativePath(root,path);
                        if(rel=="."||Path.IsPathRooted(rel)||rel==".."||rel.StartsWith(".."+Path.DirectorySeparatorChar))continue;
                        found.Add(path);
                    }
                    cached=(logInfo.Length,logInfo.LastWriteTimeUtc.Ticks,found.Order(StringComparer.OrdinalIgnoreCase).ToArray());deletionLogs[log]=cached;
                }
                paths=cached.Paths;
            }
        }
        using var c=Read(Path.Combine(source,"cache","pixelvault-index-"+Safe(root)+".sqlite"));using var cmd=c.CreateCommand();cmd.CommandText="SELECT file_path,game_id FROM photo_index WHERE root=$r";cmd.Parameters.AddWithValue("$r",root);
        var indexed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);using(var rows=cmd.ExecuteReader())while(rows.Read())indexed[rows.GetString(0)]=rows.GetString(1);
        var listed=File.ReadLines(Path.Combine(source,"cache","library-folders-"+Safe(root)+".cache")).Select(l=>l.Split('\t')).Where(f=>f.Length>=14).SelectMany(f=>f[6].Split('|',StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var events=new List<NativePhotoDeletion>();
        foreach(var path in paths)
        {
            token.ThrowIfCancellationRequested();var missing=false;
            try{var attributes=File.GetAttributes(path);if((attributes&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0)continue;}
            catch(FileNotFoundException){missing=true;}catch(DirectoryNotFoundException){missing=true;}
            // A linked or inaccessible parent never produces a delete instruction.
            for(var parent=Path.GetDirectoryName(path);parent is not null&&parent.Length>=root.Length;parent=Path.GetDirectoryName(parent))
            {
                try{if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked capture source.");}
                catch(DirectoryNotFoundException){}catch(FileNotFoundException){}
            }
            events.Add(new(path,missing,indexed.ContainsKey(path),listed.Contains(path),indexed.GetValueOrDefault(path)));
        }
        return new(Hash(JsonSerializer.Serialize(new{workspace,root,events},Json)),events);
    }
    public static string DeletedPhotoId(string workspace,string edition,string root,string file)=>Hash(workspace+"\n"+edition+"\n"+Path.GetRelativePath(root,file).Replace('\\','/').ToLowerInvariant());
}
