using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PixelVault.Companion;
using PixelVault.Companion.Export;

namespace PixelVault.Companion.Connector;

// A shared exclusive file lease also covers the manual media commands.
public static class PhotoSyncLease
{
    public static FileStream Acquire(string root)=>File.Open(Path.Combine(root,"screenshot-sync.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
}

public sealed class AutomaticPhotoSync(ConnectorPairing config,string data,HttpClient client=null,TimeSpan? pollInterval=null)
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    readonly string checkpoint=Path.Combine(data,"automatic-photos.json");
    static void Save(string path,object value)
    {
        var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(value,Json));File.Move(temp,path,true);
    }
    void Status(string state,int synchronized=0)
    {
        try{Save(Path.Combine(data,"photo-sync-status.json"),new{State=state,SynchronizedGames=synchronized,UpdatedUtc=DateTime.UtcNow});}
        catch(IOException){}catch(UnauthorizedAccessException){} // Diagnostic contention must not stop photo/save monitoring.
    }
    public async Task RunAsync(CancellationToken token)
    {
        using var http=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(config.Url),Timeout=TimeSpan.FromMinutes(2)};
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",config.Token);
        Dictionary<string,string> saved;
        try{saved=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(checkpoint),Json)??new();}catch(IOException){saved=new();}catch(JsonException){saved=new();}
        var monitor=new StableSourceMonitor(saved);
        while(!token.IsCancellationRequested)
        {
            try
            {
                using var lease=PhotoSyncLease.Acquire(data);
                var settings=await http.GetFromJsonAsync<ConnectorSyncSettings>("connector/v1/settings",Json,token);
                if(settings?.WorkspaceId!=config.WorkspaceId)throw new InvalidDataException("The photo workspace changed.");
                if(!settings.AutomaticEnabled||!settings.LibraryEnabled||string.IsNullOrWhiteSpace(config.DesktopDataRoot)){Status("paused");}
                else
                {
                    using var capabilities=await http.GetAsync("connector/v1/screenshots/capabilities",token);capabilities.EnsureSuccessStatusCode();
                    using var caps=JsonDocument.Parse(await capabilities.Content.ReadAsStringAsync(token));
                    if(!caps.RootElement.GetProperty("libraryEnabled").GetBoolean()||!caps.RootElement.GetProperty("metadata").GetBoolean()||!caps.RootElement.GetProperty("retention").GetBoolean())throw new InvalidDataException("NAS photo capabilities are unavailable.");
                    if(caps.RootElement.TryGetProperty("removals",out var removalCapability)&&removalCapability.GetBoolean())
                    {
                        var evidence=await Task.Run(()=>ScreenshotSnapshotExporter.ReadDeletionEvidence(config.DesktopDataRoot,config.WorkspaceId,token),token);
                        var removalKey=config.WorkspaceId+":native-removals:v1";
                        if(monitor.Ready(removalKey,evidence.Signature,DateTime.UtcNow))
                        {
                            var removed=await PhotoRemovalSync.Reconcile(http,config,evidence,token);
                            var receipt=new Dictionary<string,string>(monitor.Handled){[removalKey]=evidence.Signature};Save(checkpoint,receipt);monitor.Acknowledge(removalKey,evidence.Signature);
                            if(removed>0)Console.WriteLine($"Native photo removals/restorations synchronized and verified: {removed}.");
                        }
                    }
                    var approved=JsonSerializer.Deserialize<GameCatalogue>(await File.ReadAllTextAsync(Path.Combine(data,"library-cache","catalog.json"),token),Json)??throw new InvalidDataException("No indexed library.");
                    var changes=await Task.Run(()=>ScreenshotSnapshotExporter.ObserveLibrary(config.DesktopDataRoot,config.WorkspaceId,approved,token),token);
                    var completed=0;var failed=false;var paused=false;
                    foreach(var change in changes)
                    {
                        token.ThrowIfCancellationRequested();var key=config.WorkspaceId+":"+change.Provider+":"+change.GameId;
                        if(!monitor.Ready(key,change.Signature,DateTime.UtcNow))continue;
                        try
                        {
                            var current=await http.GetFromJsonAsync<ConnectorSyncSettings>("connector/v1/settings",Json,token);
                            if(current?.WorkspaceId!=config.WorkspaceId)throw new InvalidDataException("The photo workspace changed.");
                            if(!current.AutomaticEnabled||!current.LibraryEnabled){paused=true;break;}
                            Status("syncing",completed);
                            var output=Path.Combine(data,"screenshot-library");
                            var catalog=await Task.Run(()=>ScreenshotSnapshotExporter.ExportLibraryGame(config.DesktopDataRoot,config.WorkspaceId,approved,output,change.Provider,change.GameId,token),token);
                            // Check again after export: don't publish an intermediate index/group/file state.
                            var stable=await Task.Run(()=>ScreenshotSnapshotExporter.ObserveLibrary(config.DesktopDataRoot,config.WorkspaceId,approved,token,change.Provider,change.GameId).SingleOrDefault(),token);
                            if(stable?.Signature!=change.Signature)throw new IOException("Photo sources are still changing.");
                            var directory=Path.Combine(output,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(change.Provider+"\n"+change.GameId))).ToLowerInvariant());
                            await ScreenshotPilot.Send(http,catalog,directory,false,token);
                            var acknowledged=new Dictionary<string,string>(monitor.Handled){[key]=change.Signature};
                            Save(checkpoint,acknowledged);monitor.Acknowledge(key,change.Signature);completed++;
                        }
                        catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                        catch(HttpRequestException error)when(error.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden){throw;}
                        catch(Exception error)when(Recoverable(error))
                        {
                            failed=true;monitor.Delay(key,DateTime.UtcNow,TimeSpan.FromMinutes(1));
                            Console.WriteLine($"Automatic photos deferred for {change.Provider}:{change.GameId} ({error.GetType().Name}); stored photos retained.");
                        }
                    }
                    var pending=changes.Count(c=>monitor.Handled.GetValueOrDefault(config.WorkspaceId+":"+c.Provider+":"+c.GameId)!=c.Signature);
                    Status(paused?"paused":failed?"retrying":pending>0?"pending":"ready",completed);
                    if(completed>0)Console.WriteLine($"Automatic photos synchronized: {completed} game collections; new uploaded files verified.");
                }
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception error)when(Recoverable(error))
            {
                Status("retrying");Console.WriteLine($"Automatic photos waiting ({error.GetType().Name}); stored photos retained.");
            }
            try{await Task.Delay(pollInterval??TimeSpan.FromSeconds(30),token);}catch(OperationCanceledException){break;}
        }
    }
    static bool Recoverable(Exception error)=>error is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or JsonException or InvalidDataException or InvalidOperationException or ArgumentException or KeyNotFoundException or Microsoft.Data.Sqlite.SqliteException or System.Runtime.InteropServices.ExternalException or NotSupportedException;
}
