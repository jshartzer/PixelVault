namespace PixelVault.Companion;

public sealed class CompanionOptions
{
    public string DataRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixelVaultCompanion");
    public bool DemoEnabled { get; set; }
    public bool AllowHttp { get; set; }
    public bool ScreenshotLibraryEnabled { get; set; }
    public string ScreenshotPilot { get; set; } = "";
    public string StorageMode { get; set; } = "isolated-copy";
    public string ApplicationDataRoot { get; set; } = "";
    public string CacheRoot { get; set; } = "";
    public string PhotoWorkerPath { get; set; } = Path.Combine(AppContext.BaseDirectory,"media-worker","PixelVault.Companion.Media.dll");
    public string PhotoDerivativeReuseRoot { get; set; } = "";
    public long PhotoCacheBytes { get; set; } = 1024L*1024*1024;
    public long MapCacheBytes { get; set; } = 512L*1024*1024;
    public bool LocalWorkersEnabled { get; set; }
    public bool LocalProviderRefreshEnabled { get; set; }
    public string ReaderWorkerPath { get; set; } = Path.Combine(AppContext.BaseDirectory,"readers","PixelVault.exe");
    public string ServiceName {get;set;}="PixelVaultCompanion";
    public bool DirectLocal => StorageMode == "direct-local";
    public string ArtworkDataRoot => DirectLocal ? ApplicationDataRoot : DataRoot;
    public string IconCacheRoot => DirectLocal ? Path.Combine(CacheRoot,"achievement-icons") : Path.Combine(DataRoot,"achievement-icons");

    public void ValidateRoots()
    {
        DataRoot=Path.GetFullPath(DataRoot);
        if(StorageMode is not ("isolated-copy" or "direct-local"))throw new InvalidDataException("Unsupported storage mode.");
        if(!DirectLocal)return;
        if(DemoEnabled||!Path.IsPathFullyQualified(ApplicationDataRoot)||!Path.IsPathFullyQualified(CacheRoot))
            throw new InvalidDataException("Direct-local mode requires explicit application and cache roots.");
        ApplicationDataRoot=Path.GetFullPath(ApplicationDataRoot);CacheRoot=Path.GetFullPath(CacheRoot);
        if(OperatingSystem.IsWindows()&&new[]{ApplicationDataRoot,DataRoot,CacheRoot}.Any(p=>p.StartsWith(@"\\",StringComparison.Ordinal)||new DriveInfo(Path.GetPathRoot(p)!).DriveType==DriveType.Network))
            throw new InvalidDataException("Direct-local databases and cache must be on this PC's local filesystems.");
        foreach(var pair in new[]{(ApplicationDataRoot,DataRoot),(ApplicationDataRoot,CacheRoot),(DataRoot,CacheRoot)})
        {
            var relative=Path.GetRelativePath(pair.Item1,pair.Item2);
            var reverse=Path.GetRelativePath(pair.Item2,pair.Item1);
            bool Inside(string p)=>p=="."||!Path.IsPathRooted(p)&&p!=".."&&!p.StartsWith(".."+Path.DirectorySeparatorChar);
            if(Inside(relative)||Inside(reverse))throw new InvalidDataException("Application, Companion state and cache roots must be separate.");
        }
        if(!Directory.Exists(ApplicationDataRoot)||!File.Exists(Path.Combine(ApplicationDataRoot,"achievement-tracking","pixelvault-tracking.sqlite")))
            throw new InvalidDataException("The canonical application store is unavailable. No replacement will be created.");
    }
}
