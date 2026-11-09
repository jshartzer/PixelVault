using System.Text.Json;

namespace PixelVault.Companion.Client;

/// <summary>Local push transport over the existing protected data directory. No HTTP
/// credential sharing or new inbound port is needed by the desktop application.</summary>
public sealed class LocalRefreshListener : IDisposable
{
    public const string FileName = "refresh-updates.json";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly string directory;
    readonly object gate = new();
    FileSystemWatcher? watcher;
    Timer? timer;
    bool disposed;
    RefreshSnapshot? current;
    public event Action<RefreshSnapshot?,RefreshSnapshot>? Changed;
    public RefreshSnapshot? Current { get { lock(gate) return current; } }
    public bool IsFresh => Current is { } value && value.ObservedUtc <= DateTime.UtcNow.AddSeconds(5).Ticks
        && value.ObservedUtc > DateTime.UtcNow.AddSeconds(-45).Ticks;

    public LocalRefreshListener(string applicationRoot) => directory = Path.GetFullPath(applicationRoot);
    public void Start()
    {
        lock(gate)
        {
            if(disposed || timer is not null)return;
            timer = new Timer(_ => Read(),null,0,30000);
            Watch();
        }
    }
    void Watch()
    {
        if(watcher is not null || !Directory.Exists(directory))return;
        watcher = new FileSystemWatcher(directory,FileName) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Changed += Wake; watcher.Created += Wake; watcher.Deleted += Wake;
        watcher.Renamed += (_,__) => Wake(null!,null!);
        watcher.Error += (_,__) => { lock(gate) { watcher?.Dispose(); watcher=null; } Wake(null!,null!); };
        watcher.EnableRaisingEvents = true;
    }
    void Wake(object sender,FileSystemEventArgs args)
    { lock(gate) { if(!disposed)timer?.Change(100,30000); } }
    internal void Read()
    {
        RefreshSnapshot? prior; RefreshSnapshot? next;
        lock(gate)
        {
            if(disposed)return;
            try
            {
                Watch();
                var file=Path.Combine(directory,FileName);
                if(!File.Exists(file) || new FileInfo(file).Length > 1024*1024)return;
                using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                next=JsonSerializer.Deserialize<RefreshSnapshot>(input,Json);
                if(next is null || next.Schema!=1 || next.Sequence<1 || string.IsNullOrWhiteSpace(next.Epoch)
                    || string.IsNullOrWhiteSpace(next.WorkspaceId) || next.Versions is null || next.Trackers is null || next.Achievements is null)return;
                prior=current;
                if(prior?.Epoch==next.Epoch && prior.Sequence>next.Sequence)return;
                current=next;
                if(prior?.Id==next.Id && prior.WorkspaceId==next.WorkspaceId)return;
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return; }
        }
        var callbacks=Changed;
        if(callbacks is not null)foreach(Action<RefreshSnapshot?,RefreshSnapshot> callback in callbacks.GetInvocationList())
            try{callback(prior,next);}catch(InvalidOperationException){/* A closing UI cannot terminate the watcher thread. */}
    }
    public void Dispose()
    { lock(gate) { disposed=true;timer?.Dispose();timer=null;watcher?.Dispose();watcher=null;Changed=null; } }
}
