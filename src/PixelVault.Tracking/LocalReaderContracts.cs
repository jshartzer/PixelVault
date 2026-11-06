namespace PixelVaultNative;
public sealed class LocalReaderRequest
{
    public string Operation {get;set;}="";
    public string ApplicationRoot {get;set;}="";
    public string CacheRoot {get;set;}="";
    public TrackerSourceBinding Binding {get;set;}
    public AchievementTrackerDefinition Definition {get;set;}
    public string Provider {get;set;}="";
    public string GameId {get;set;}="";
    public Dictionary<string,string> Accounts {get;set;}=new();
    public bool RequireProviderCredentials {get;set;}
}
public sealed class LocalReaderResult
{
    public bool Success {get;set;}
    public string Error {get;set;}="";
    public string Signature {get;set;}="";
    public string Fingerprint {get;set;}="";
    public string SnapshotJson {get;set;}
}
public sealed class LocalStoreLease:IDisposable
{
    readonly FileStream stream;
    LocalStoreLease(FileStream stream)=>this.stream=stream;
    public static async Task<LocalStoreLease> AcquireAsync(string file,CancellationToken token)
    {
        var leases=Path.Combine(Path.GetDirectoryName(file)!,".leases");Directory.CreateDirectory(leases);file=Path.Combine(leases,Path.GetFileName(file));
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while(true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try{return new(new FileStream(file+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None));}
            catch(IOException){await Task.Delay(100,timeout.Token).ConfigureAwait(false);}
        }
    }
    public void Dispose()=>stream.Dispose();
}
