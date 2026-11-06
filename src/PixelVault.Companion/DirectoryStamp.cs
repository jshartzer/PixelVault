namespace PixelVault.Companion;

internal sealed class DirectoryStamp(string path):IDisposable
{
    FileSystemWatcher? watcher;
    long dirty=1,observed,expires;
    string stamp="";
    public string Read()
    {
        if(watcher is null&&Directory.Exists(path))
        {
            watcher=new(path){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};
            watcher.Changed+=Changed;watcher.Created+=Changed;watcher.Deleted+=Changed;watcher.Renamed+=Changed;
            watcher.Error+=(_,_)=>Interlocked.Increment(ref dirty);watcher.EnableRaisingEvents=true;
        }
        var revision=Volatile.Read(ref dirty);
        if(revision==observed&&Environment.TickCount64<expires)return stamp;
        stamp=string.Join("\n",(Directory.Exists(path)?Directory.EnumerateFiles(path):[]).Order(StringComparer.Ordinal).Select(p=>{var f=new FileInfo(p);return p+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;}));
        observed=revision;expires=Environment.TickCount64+1000;return stamp;
    }
    void Changed(object sender,FileSystemEventArgs e)=>Interlocked.Increment(ref dirty);
    public void Dispose()=>watcher?.Dispose();
}
