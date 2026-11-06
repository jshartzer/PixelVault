using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PixelVault.Companion;

// Only the opened handle is trusted. Paths are private implementation details.
internal static class LocalPhotoFile
{
    internal sealed record Stamp(string Identity,string Version,long Bytes,long Modified);
    internal static string Relative(string root,string path)
    {
        if(!Path.IsPathFullyQualified(path))throw new InvalidDataException("A capture path is outside the approved library.");
        var rel=Path.GetRelativePath(Path.GetFullPath(root),Path.GetFullPath(path));
        if(rel=="."||Path.IsPathRooted(rel)||rel==".."||rel.StartsWith(".."+Path.DirectorySeparatorChar))throw new InvalidDataException("A capture path is outside the approved library.");
        return rel;
    }
    internal static void CheckRoot(string root)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Direct photo access requires Windows.");
        if(!Path.IsPathFullyQualified(root)||root.StartsWith(@"\\",StringComparison.Ordinal)||new DriveInfo(Path.GetPathRoot(root)!).DriveType==DriveType.Network)throw new InvalidDataException("The approved capture root must be local.");
        CheckAncestors(root);
        if(!Directory.Exists(root))throw new IOException("The capture library is unavailable.");
        using var entries=Directory.EnumerateFileSystemEntries(root).GetEnumerator();_=entries.MoveNext();
    }
    internal static void CheckDestinations(string root,CompanionOptions options)
    {
        static bool Inside(string parent,string path)
        {var rel=Path.GetRelativePath(parent,path);return rel=="."||!Path.IsPathRooted(rel)&&rel!=".."&&!rel.StartsWith(".."+Path.DirectorySeparatorChar);}
        foreach(var destination in new[]{options.ApplicationDataRoot,options.DataRoot,options.CacheRoot})
            if(Inside(root,destination)||Inside(destination,root))throw new InvalidDataException("Capture, application, state and cache roots must be separate.");
    }
    static void CheckAncestors(string path)
    {
        for(string? p=path;p is not null;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked capture paths are not supported.");
    }
    internal static (FileStream Stream,Stamp Stamp) Open(string root,string relative,bool reading)
    {
        var path=Path.GetFullPath(Path.Combine(root,relative));_=Relative(root,path);CheckAncestors(path);
        var stream=new FileStream(path,FileMode.Open,FileAccess.Read,reading?FileShare.Read:FileShare.ReadWrite|FileShare.Delete,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
        try
        {
            var name=new StringBuilder(32768);var n=GetFinalPathNameByHandleW(stream.SafeFileHandle,name,(uint)name.Capacity,0);
            if(n==0||n>=name.Capacity)throw new IOException("The opened capture could not be validated.");
            var final=name.ToString();if(final.StartsWith(@"\\?\",StringComparison.Ordinal))final=final[4..];
            if(!string.Equals(Path.GetFullPath(final),path,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The opened capture path changed.");
            _=Relative(root,final);CheckAncestors(final);
            if(!GetFileInformationByHandle(stream.SafeFileHandle,out var info)||!GetFileInformationByHandleEx(stream.SafeFileHandle,0,out var basic,(uint)Marshal.SizeOf<BasicInfo>()))throw new IOException("Capture identity is unavailable.");
            var identity=$"{info.VolumeSerialNumber:x8}:{info.FileIndexHigh:x8}{info.FileIndexLow:x8}:{basic.CreationTime:x16}";
            var length=stream.Length;var version=LocalPhotoIndex.Hash($"{identity}:{length}:{basic.LastWriteTime}:{basic.ChangeTime}");
            return(stream,new(identity,version,length,DateTime.FromFileTimeUtc(basic.LastWriteTime).Ticks));
        }
        catch{stream.Dispose();throw;}
    }
    internal static Stamp Inspect(string root,string relative)
    {
        var opened=Open(root,relative,false);using(opened.Stream)return opened.Stamp;
    }
    [StructLayout(LayoutKind.Sequential)] struct BasicInfo {internal long CreationTime,LastAccessTime,LastWriteTime,ChangeTime;internal uint Attributes;}
    [StructLayout(LayoutKind.Sequential)] struct FileInfoNative
    {
        internal uint Attributes;internal System.Runtime.InteropServices.ComTypes.FILETIME Creation,Access,Write;
        internal uint VolumeSerialNumber,FileSizeHigh,FileSizeLow,Links,FileIndexHigh,FileIndexLow;
    }
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInfoNative info);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool GetFileInformationByHandleEx(SafeFileHandle handle,int type,out BasicInfo info,uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle,StringBuilder path,uint length,uint flags);
}
