using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication;
using PixelVault.Companion.Client;

namespace PixelVault.Companion;

// One coordinator observes committed canonical data for all screens. Full version
// snapshots make dropped/coalesced events, service restarts and reconnects recoverable.
public sealed class RefreshUpdates(CompanionOptions options,TrackerWorkspace workspace,
    LocalRefreshService refresh,AchievementStore achievements,ILogger<RefreshUpdates> log) : BackgroundService
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    readonly object gate=new();
    readonly HashSet<Channel<RefreshSnapshot>> clients=[];
    readonly string epoch=Guid.NewGuid().ToString("N");
    string fingerprint="",rawTrackers="",rawLibrary="";
    Dictionary<string,string> trackerVersions=[],achievementVersions=[];
    string libraryVersion="",trackerVersion="",sourceVersion="";
    RefreshSnapshot? current;
    public bool Enabled=>options.DirectLocal&&options.LocalWorkersEnabled;
    public RefreshSnapshot? Current {get{lock(gate)return current;}}
    static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,Json)));

    internal RefreshSnapshot Sample()
    {
        var connection=JsonSerializer.SerializeToNode(refresh.Connection(),Json)!.AsObject();
        var raw=connection["versions"]!.Deserialize<Dictionary<string,string>>(Json)!;
        if(raw["trackers"]!=rawTrackers)
        {
            var definitions=workspace.Definitions().ToDictionary(d=>d.Id);
            trackerVersions=workspace.Trackers().Select(row=>JsonSerializer.SerializeToElement(row,Json)).ToDictionary(
                row=>row.GetProperty("id").GetString()!,row=>Hash(new{row,definition=definitions[row.GetProperty("id").GetString()!]}));
            trackerVersion=Hash(new{trackers=trackerVersions.OrderBy(p=>p.Key),order=workspace.Repository.TrackerGroupOrder()});
            sourceVersion=Hash(definitions.Keys.Order().Select(id=>
            {
                var account=workspace.Account(id);var source=workspace.Repository.Source(id,account);
                var status=workspace.Repository.SourceStatus(id,account);var automatic=workspace.Repository.Automation(id,account);
                return new{id,source,automatic.Settings,outcome=status?.Outcome,message=status?.Message,automatic.State.ReviewPending};
            }).ToArray());
        }
        if(raw["library"]!=rawLibrary || raw["trackers"]!=rawTrackers)
        {
            var confirmed=achievements.Updates().ToDictionary(g=>g.Provider+":"+g.GameId);
            achievementVersions=workspace.LocalData!.Achievements().ToDictionary(g=>g.Provider+":"+g.GameId,
                g=>Hash(new{g.Rows,g.Categories,g.CategoryOrder,g.Dlcs,confirmed=confirmed.GetValueOrDefault(g.Provider+":"+g.GameId)}));
            foreach(var item in confirmed)if(!achievementVersions.ContainsKey(item.Key))achievementVersions[item.Key]=Hash(item.Value);
            foreach(var group in PixelVault.Tracking.CenturyAchievementCatalog.All.GroupBy(d=>d.Provider+":"+d.GameId))
            {
                var first=group.First();if(!workspace.Manifest!.Accounts.TryGetValue(first.Provider,out var account))continue;
                var progress=group.Select(d=>
                {
                    var p=workspace.Repository.Century(d.Provider,d.GameId,d.Id,account);
                    return new{d.Id,p?.Current,p?.Target,p?.Unknown,p?.UnlockUtc};
                }).ToArray();
                achievementVersions[group.Key]=Hash(new{official=achievementVersions.GetValueOrDefault(group.Key),progress});
            }
            libraryVersion=Hash(workspace.LocalData.Games().OrderBy(p=>p.Key).Select(p=>new{p.Key,game=p.Value with{Stats=p.Value.Stats with{UpdatedUtc=null}}}));
        }
        var versions=new Dictionary<string,string>(raw){["trackers"]=trackerVersion,["sources"]=sourceVersion,["library"]=libraryVersion,["achievements"]=Hash(achievementVersions.OrderBy(p=>p.Key))};
        connection["versions"]=JsonSerializer.SerializeToNode(versions,Json);connection["pushEnabled"]=true;
        // Monitor ticks and successful unchanged checks are not visible data changes.
        var comparable=connection.DeepClone().AsObject();
        foreach(var key in new[]{"lastSaveCheckUtc","lastSaveUpdateUtc","lastMonitorCheckUtc","lastAchievementSyncUtc"})comparable.Remove(key);
        var nextFingerprint=Hash(comparable);
        lock(gate)
        {
            var changed=current is null||fingerprint!=nextFingerprint;
            current=new RefreshSnapshot{WorkspaceId=workspace.Identity,Epoch=epoch,Sequence=(current?.Sequence??0)+(changed?1:0),
                ObservedUtc=DateTime.UtcNow.Ticks,Versions=versions,Trackers=trackerVersions,Achievements=achievementVersions,
                Connection=JsonSerializer.SerializeToElement(connection,Json)};
            fingerprint=nextFingerprint;rawTrackers=raw["trackers"];rawLibrary=raw["library"];
            if(changed)foreach(var client in clients)client.Writer.TryWrite(current);
            return current;
        }
    }
    internal (Channel<RefreshSnapshot> Channel,Action Close) Subscribe()
    {
        var channel=Channel.CreateBounded<RefreshSnapshot>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
        lock(gate){clients.Add(channel);if(current is not null)channel.Writer.TryWrite(current);}
        return(channel,()=>{lock(gate){clients.Remove(channel);channel.Writer.TryComplete();}});
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!Enabled)return;
        string written="";long writeAt=0;
        while(!token.IsCancellationRequested)
        {
            try
            {
                var snapshot=Sample();
                if(written!=snapshot.Id||Environment.TickCount64>=writeAt)
                {
                    var file=Path.Combine(options.ApplicationDataRoot,LocalRefreshListener.FileName);
                    var temporary=file+"."+epoch+".tmp";
                    try{await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(snapshot,Json),token);File.Move(temporary,file,true);}
                    finally{if(File.Exists(temporary))File.Delete(temporary);}
                    written=snapshot.Id;writeAt=Environment.TickCount64+15000;
                }
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception e)
            {
                log.LogWarning(e,"Refresh updates could not be sampled; clients retain their last saved view.");
                lock(gate)
                {
                    if(current is not null && fingerprint!="unavailable")
                    {
                        var failed=JsonNode.Parse(current.Connection.GetRawText())!.AsObject();
                        failed["online"]=false;failed["sourceError"]="Saved progress updates are temporarily unavailable. Retrying automatically.";
                        current=current with{Sequence=current.Sequence+1,Connection=JsonSerializer.SerializeToElement(failed,Json)};
                        fingerprint="unavailable";foreach(var client in clients)client.Writer.TryWrite(current);
                    }
                }
            }
            await Task.Delay(1000,token);
        }
    }
    public async Task Stream(HttpContext context,string? workspaceId)
    {
        if(!Enabled){context.Response.StatusCode=503;return;}
        if(workspaceId!=workspace.Identity){context.Response.StatusCode=409;return;}
        // Periodically renew the HTTP request so cookie expiry/revocation is rechecked.
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var authentication=await context.AuthenticateAsync();
        var remaining=(authentication.Properties?.ExpiresUtc??DateTimeOffset.UtcNow.AddMinutes(5))-DateTimeOffset.UtcNow;
        if(remaining<=TimeSpan.Zero){context.Response.StatusCode=401;return;}
        lifetime.CancelAfter(remaining<TimeSpan.FromMinutes(5)?remaining:TimeSpan.FromMinutes(5));
        var token=lifetime.Token;
        context.Response.ContentType="text/event-stream";
        context.Response.Headers.CacheControl="no-store";context.Response.Headers["X-Accel-Buffering"]="no";
        context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        var subscription=Subscribe();
        try
        {
            await context.Response.WriteAsync("retry: 3000\n\n",token);await context.Response.Body.FlushAsync(token);
            using var heartbeat=new PeriodicTimer(TimeSpan.FromSeconds(15));
            var read=subscription.Channel.Reader.WaitToReadAsync(token).AsTask();
            var tick=heartbeat.WaitForNextTickAsync(token).AsTask();
            while(!token.IsCancellationRequested)
            {
                await Task.WhenAny(read,tick);
                if(read.IsCompleted)
                {
                    if(!await read)break;
                    if(subscription.Channel.Reader.TryRead(out var snapshot))
                        await context.Response.WriteAsync("id: "+snapshot.Id+"\nevent: refresh\ndata: "+JsonSerializer.Serialize(snapshot,Json)+"\n\n",token);
                    read=subscription.Channel.Reader.WaitToReadAsync(token).AsTask();
                }
                if(tick.IsCompleted){if(!await tick)break;await context.Response.WriteAsync(": keepalive\n\n",token);tick=heartbeat.WaitForNextTickAsync(token).AsTask();}
                await context.Response.Body.FlushAsync(token);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        finally{subscription.Close();}
    }
}
