using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

namespace PixelVault.Companion;

/// <summary>Outbound-only PC connection, fixed to a workspace and the locally approved source bindings.</summary>
public sealed partial class PcConnector
{
    readonly CompanionOptions options;readonly TrackerWorkspace workspace;readonly GameLibrary library;readonly object gate=new();
    readonly ConnectorSyncState sync;
    static readonly JsonSerializerOptions Json=AchievementTrackingCatalog.Json;
    public PcConnector(CompanionOptions options,TrackerWorkspace workspace,GameLibrary library)
    {
        this.options=options;this.workspace=workspace;this.library=library;sync=new(options,workspace);
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS companion_connector(id INTEGER PRIMARY KEY CHECK(id=1),workspace TEXT NOT NULL,token_hash TEXT NOT NULL,config TEXT NOT NULL,last_seen INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS companion_refresh_job(id TEXT PRIMARY KEY,workspace TEXT NOT NULL,created INTEGER NOT NULL,value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS companion_published_tracker(workspace TEXT NOT NULL,id TEXT NOT NULL,digest TEXT NOT NULL,PRIMARY KEY(workspace,id));
        CREATE TABLE IF NOT EXISTS companion_tracker_title(workspace TEXT NOT NULL,id TEXT NOT NULL,baseline TEXT NULL,desktop_title TEXT NOT NULL,conflict INTEGER NOT NULL,PRIMARY KEY(workspace,id));
        """;cmd.ExecuteNonQuery();
    }
    SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(options.DataRoot,"companion.sqlite"),Pooling=false}.ToString());c.Open();return c;}
    public static string Revision(TrackerSourceBinding binding)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(binding,Json))));
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    ConnectorPairing? Pairing(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT config FROM companion_connector WHERE id=1 AND workspace=$w";cmd.Parameters.AddWithValue("$w",workspace.Identity);
        return cmd.ExecuteScalar() is string value?JsonSerializer.Deserialize<ConnectorPairing>(value,Json):null;
    }
    public void Pair(string url,string output)
    {
        if(workspace.ReadOnly||File.Exists(output))throw new InvalidDataException("Pair a real workspace into a new private output file.");
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.AbsolutePath!="/"||!string.IsNullOrEmpty(uri.UserInfo)||!string.IsNullOrEmpty(uri.Query)||!string.IsNullOrEmpty(uri.Fragment))throw new InvalidDataException("Use the exact Companion origin.");
        var sources=workspace.Definitions().Select(d=>(Definition:d,Binding:workspace.Repository.Source(d.Id,workspace.Account(d.Id))))
            .Where(s=>s.Binding is not null&&s.Binding.AdapterVersion=="1"&&s.Binding.AdapterId is "death-stranding-dc.fabrication" or "pixelvault.json-file")
            .Select(s=>new ConnectorSource(s.Binding,s.Definition,Revision(s.Binding))).ToList();
        if(sources.Count==0)throw new InvalidDataException("No supported save sources are configured.");
        var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var pairing=new ConnectorPairing(uri.AbsoluteUri,workspace.Identity,token,sources);
        using var c=Open();using var tx=c.BeginTransaction();using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO companion_connector(id,workspace,token_hash,config,last_seen) VALUES(1,$w,$h,$c,0) ON CONFLICT(id) DO UPDATE SET workspace=$w,token_hash=$h,config=$c,last_seen=0; DELETE FROM companion_refresh_job;";
        cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$h",Hash(token));cmd.Parameters.AddWithValue("$c",JsonSerializer.Serialize(pairing with{Token=""},Json));cmd.ExecuteNonQuery();
        File.WriteAllText(output,JsonSerializer.Serialize(pairing,Json));tx.Commit();
        Console.WriteLine($"PC connection paired for {sources.Count} configured trackers. Credentials were written only to the private output file.");
    }
    public bool Authenticate(string authorization)
    {
        if(!authorization.StartsWith("Bearer ",StringComparison.Ordinal)||authorization.Length!=71)return false;
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT token_hash FROM companion_connector WHERE id=1 AND workspace=$w";cmd.Parameters.AddWithValue("$w",workspace.Identity);
        return cmd.ExecuteScalar() is string digest&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(digest),Convert.FromHexString(Hash(authorization[7..])));
    }
    ConnectorSource? Current(ConnectorPairing pair,string tracker)
    {
        var source=pair.Sources.FirstOrDefault(s=>s.Definition.Id==tracker);if(source is null)return null;
        var current=workspace.Repository.Source(tracker,workspace.Account(tracker));
        return current?.Id==source.Binding.Id&&Revision(current)==source.Revision?source:null;
    }
    public object Connection()
    {
        using var c=Open();var pair=Pairing(c);using var cmd=c.CreateCommand();cmd.CommandText="SELECT last_seen FROM companion_connector WHERE id=1 AND workspace=$w";cmd.Parameters.AddWithValue("$w",workspace.Identity);
        var last=cmd.ExecuteScalar() is long seen?seen:0;
        var games=library.Games();
        var supported=pair?.Sources.Where(s=>Current(pair,s.Definition.Id) is not null).Select(s=>games.FirstOrDefault(g=>g.Provider==s.Definition.Provider&&g.GameId==s.Definition.GameId)?.GroupId).Where(id=>id is not null).Distinct().ToArray()??[];
        var settings=sync.Settings();
        var reviews=Recent(c).Where(j=>State(j)=="review").Select(j=>new{id=j.Id,groupId=j.GroupId,message="Save changes need review."}).ToList();
        return new{paired=pair is not null,online=last>DateTime.UtcNow.AddSeconds(-35).Ticks,supported,workspaceId=workspace.Identity,
            automaticEnabled=settings.AutomaticEnabled,libraryEnabled=settings.LibraryEnabled,lastSeenUtc=last,
            lastSaveCheckUtc=sync.Get("save-check").Updated,lastSaveUpdateUtc=sync.Get("save-update").Updated,
            lastLibraryCheckUtc=sync.Get("library-check").Updated,lastLibraryUpdateUtc=sync.Get("library-update").Updated,
            lastMonitorCheckUtc=sync.Get("monitor-check").Updated,
            lastTrackerPublishUtc=sync.Get("tracker-publish").Updated,publicationError=sync.Get("publication-error").Value,
            trackerTitleConflicts=TitleConflicts(c),lastTrackerTitleCheckUtc=sync.Get("tracker-title-check").Updated,
            lastAchievementSyncUtc=sync.Get("achievement-check").Updated,achievementError=sync.Get("achievement-error").Value,
            libraryError=sync.Get("library-error").Value,sourceError=sync.Get("source-error").Value,reviewJobs=reviews};
    }
    public void Heartbeat()
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="UPDATE companion_connector SET last_seen=$n WHERE id=1 AND workspace=$w";cmd.Parameters.AddWithValue("$n",DateTime.UtcNow.Ticks);cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.ExecuteNonQuery();
    }
    public sealed class TaskState
    {
        public string TrackerId {get;set;}="";public string Revision {get;set;}="";public string BindingId {get;set;}="";
        public string Status {get;set;}="queued";public string Message {get;set;}="Waiting for the PC.";
        public string? Snapshot {get;set;}public string Fingerprint {get;set;}="";public string Baseline {get;set;}="";
        public int Changed {get;set;}public int Conflicts {get;set;}public int Older {get;set;}public List<RefreshChange> Changes {get;set;}=[];
    }
    public sealed class Job
    {
        public string Id {get;set;}="";public string GroupId {get;set;}="";public long Created {get;set;}
        public bool Automatic {get;set;}public string Signature {get;set;}="";
        public string Lease {get;set;}="";public long LeaseUntil {get;set;}public List<TaskState> Tasks {get;set;}=[];
    }
    Job Read(SqliteConnection c,string id)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM companion_refresh_job WHERE id=$id AND workspace=$w";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$w",workspace.Identity);
        return cmd.ExecuteScalar() is string json?JsonSerializer.Deserialize<Job>(json,Json)!:throw new KeyNotFoundException();
    }
    void Write(SqliteConnection c,Job job)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO companion_refresh_job(id,workspace,created,value) VALUES($id,$w,$n,$v) ON CONFLICT(id) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$id",job.Id);cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$n",job.Created);cmd.Parameters.AddWithValue("$v",JsonSerializer.Serialize(job,Json));cmd.ExecuteNonQuery();
    }
    List<Job> Recent(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM companion_refresh_job WHERE workspace=$w ORDER BY created DESC LIMIT 40";cmd.Parameters.AddWithValue("$w",workspace.Identity);
        using var rows=cmd.ExecuteReader();var jobs=new List<Job>();while(rows.Read())jobs.Add(JsonSerializer.Deserialize<Job>(rows.GetString(0),Json)!);return jobs;
    }
    static string State(Job job)=>job.Tasks.Any(t=>t.Status is "queued" or "reading")?(job.Created<DateTime.UtcNow.AddMinutes(-10).Ticks?"offline":"reading"):
        job.Tasks.Any(t=>t.Status=="review")?"review":job.Tasks.Any(t=>t.Status=="failed")?"failed":job.Tasks.Any(t=>t.Status=="paused")?"paused":"complete";
    string ReviewVersion(Job job)=>Hash(string.Join("|",job.Tasks.Select(t=>t.TrackerId+":"+t.Baseline+":"+t.Status)));
    GameRefreshView View(Job job)
    {
        var state=State(job);var message=state switch{
            "reading"=>job.Lease.Length==0?"Waiting for the PC. It must be on with Companion Connector running.":"Checking the PC save files…",
            "offline"=>"The PC did not finish this check. Turn it on and try Refresh again.",
            "review"=>"Save changes need review. Your saved progress and notes have been preserved.",
            "failed"=>"Some save files could not be read. Saved progress was preserved for those trackers.",
            "paused"=>"Automatic updates are paused. Saved progress was preserved.",
            _=>job.Tasks.Sum(t=>t.Changed)==0?"PC save check complete. No progress changes.":$"PC save check complete. Updated {job.Tasks.Sum(t=>t.Changed)} observations."};
        return new(job.Id,state,message,state=="review",job.Tasks.Any(t=>t.Older>0),ReviewVersion(job),job.Tasks.Select(t=>new RefreshTaskView(
            workspace.Definitions().FirstOrDefault(d=>d.Id==t.TrackerId)?.Title??"Tracker",t.Status,t.Message,t.Changed,t.Conflicts,t.Older,t.Changes.Take(80).ToList())).ToList());
    }
    public GameRefreshView Request(RequestGameRefresh request)
    {
        if(!Guid.TryParse(request.RequestId,out _))throw new InvalidDataException("Refresh needs a valid request ID.");
        lock(gate)
        {
            using var c=Open();var pair=Pairing(c)??throw new InvalidDataException("The PC connection has not been paired.");
            using(var seen=c.CreateCommand())
            {
                seen.CommandText="SELECT last_seen FROM companion_connector WHERE id=1 AND workspace=$w";seen.Parameters.AddWithValue("$w",workspace.Identity);
                if(seen.ExecuteScalar() is not long last||last<DateTime.UtcNow.AddSeconds(-35).Ticks)throw new InvalidOperationException("The PC is offline. Turn it on with Companion Connector running, then try Refresh again.");
            }
            var editionKeys=library.Games().Where(g=>g.GroupId==request.GroupId).Select(g=>g.Provider+":"+g.GameId).ToHashSet(StringComparer.Ordinal);
            var sources=pair.Sources.Where(s=>editionKeys.Contains(s.Definition.Provider+":"+s.Definition.GameId)&&Current(pair,s.Definition.Id) is not null).ToList();
            if(sources.Count==0)throw new InvalidDataException("No PC save source is configured for this game.");
            var recent=Recent(c);var duplicate=recent.FirstOrDefault(j=>j.Id==request.RequestId);
            if(duplicate is not null){if(duplicate.GroupId!=request.GroupId)throw new InvalidOperationException("Refresh request belongs to another game.");return View(duplicate);}
            var active=recent.FirstOrDefault(j=>j.GroupId==request.GroupId&&State(j)=="reading");if(active is not null)return View(active);
            var job=new Job{Id=request.RequestId,GroupId=request.GroupId,Created=DateTime.UtcNow.Ticks,Tasks=sources.Select(s=>new TaskState{TrackerId=s.Definition.Id,BindingId=s.Binding.Id,Revision=s.Revision}).ToList()};Write(c,job);
            using var prune=c.CreateCommand();prune.CommandText="DELETE FROM companion_refresh_job WHERE created<$cutoff";prune.Parameters.AddWithValue("$cutoff",DateTime.UtcNow.AddDays(-7).Ticks);prune.ExecuteNonQuery();return View(job);
        }
    }
    public GameRefreshView Get(string id){lock(gate){using var c=Open();return View(Read(c,id));}}
    public AutomaticReceipt Automatic(AutomaticSaveChange change)
    {
        lock(gate)
        {
            if(!sync.Settings().AutomaticEnabled)throw new InvalidOperationException("Automatic updates are paused.");
            if(change.Signature is null||change.Signature.Length!=64||!change.Signature.All(Uri.IsHexDigit)||change.Tasks is null||change.Tasks.Count is <1 or >20||change.Tasks.Select(t=>t.TrackerId).Distinct().Count()!=change.Tasks.Count)throw new InvalidDataException("Invalid source change.");
            using var c=Open();var pair=Pairing(c)??throw new InvalidOperationException("PC pairing changed.");
            var sources=change.Tasks.Select(t=>{
                var source=Current(pair,t.TrackerId);if(source is null||t.BindingId!=source.Binding.Id||t.Revision!=source.Revision)throw new InvalidOperationException("The paired source changed.");return source;
            }).ToList();
            var keys=sources.Select(s=>s.Definition.Provider+":"+s.Definition.GameId).Distinct().ToList();if(keys.Count!=1)throw new InvalidDataException("Changes must belong to one game.");
            var recent=Recent(c);var ids=sources.Select(s=>s.Definition.Id).ToHashSet(StringComparer.Ordinal);
            var pending=recent.FirstOrDefault(j=>j.Tasks.Any(t=>ids.Contains(t.TrackerId)&&t.Status=="review"));
            var blocked=recent.SelectMany(j=>j.Tasks).Where(t=>t.Status=="review").Select(t=>t.TrackerId).ToHashSet(StringComparer.Ordinal);
            sources=sources.Where(s=>!blocked.Contains(s.Definition.Id)).ToList();
            if(sources.Count==0)return new(pending!.Id,"paused-review",pending.Signature);
            ids=sources.Select(s=>s.Definition.Id).ToHashSet(StringComparer.Ordinal);
            var prior=recent.FirstOrDefault(j=>j.Automatic&&j.Signature==change.Signature&&j.Tasks.Select(t=>t.TrackerId).ToHashSet().SetEquals(ids));
            if(prior is not null&&State(prior) is "complete" or "review" or "reading")return new(prior.Id,State(prior),prior.Signature);
            if(prior is not null&&prior.Created>DateTime.UtcNow.AddMinutes(-1).Ticks)return new(prior.Id,"retry-later",prior.Signature);
            var busy=recent.FirstOrDefault(j=>State(j)=="reading"&&j.Tasks.Any(t=>ids.Contains(t.TrackerId)));if(busy is not null)return new(busy.Id,"reading",busy.Signature);
            var definition=sources[0].Definition;var game=library.Games().First(g=>g.Provider==definition.Provider&&g.GameId==definition.GameId);
            var job=new Job{Id=Guid.NewGuid().ToString(),Created=DateTime.UtcNow.Ticks,GroupId=game.GroupId,Automatic=true,Signature=change.Signature,
                Tasks=sources.Select(s=>new TaskState{TrackerId=s.Definition.Id,BindingId=s.Binding.Id,Revision=s.Revision}).ToList()};Write(c,job);
            using var prune=c.CreateCommand();prune.CommandText="DELETE FROM companion_refresh_job WHERE created<$cutoff";prune.Parameters.AddWithValue("$cutoff",DateTime.UtcNow.AddDays(-7).Ticks);prune.ExecuteNonQuery();return new(job.Id,"reading",job.Signature);
        }
    }
    public ConnectorWork? Work()
    {
        lock(gate)
        {
            Heartbeat();using var c=Open();var pair=Pairing(c)!;
            var job=Recent(c).Where(j=>State(j)=="reading").OrderBy(j=>j.Created).FirstOrDefault();if(job is null)return null;
            if(job.Automatic&&!sync.Settings().AutomaticEnabled){foreach(var t in job.Tasks.Where(t=>t.Status is "queued" or "reading"))t.Status="paused";Write(c,job);return null;}
            if(job.Lease.Length==0||job.LeaseUntil<DateTime.UtcNow.Ticks){job.Lease=Guid.NewGuid().ToString("N");job.LeaseUntil=DateTime.UtcNow.AddMinutes(8).Ticks;}
            foreach(var task in job.Tasks.Where(t=>t.Status is "queued" or "reading"))
            {
                if(Current(pair,task.TrackerId) is null){task.Status="failed";task.Message="The save source changed. Pair the PC again.";}
                else{task.Status="reading";task.Message="Reading a validated save copy on the PC.";}
            }
            Write(c,job);return new(job.Id,job.Lease,job.Tasks.Where(t=>t.Status=="reading").Select(t=>new ConnectorTask(t.TrackerId,t.BindingId,t.Revision)).ToList());
        }
    }
    void Apply(TaskState task,ConnectorSource source,bool reviewed,bool allowOlder)
    {
        var repo=workspace.Repository;var account=workspace.Account(task.TrackerId);var prior=repo.SourceStatus(task.TrackerId,account);
        var plan=repo.Preview(task.Snapshot!,task.TrackerId,account,source.Binding.AdapterId,source.Binding.AdapterVersion,task.Fingerprint);
        if(source.Binding.AdapterId=="death-stranding-dc.fabrication"&&plan.Rows.Count!=source.Definition.Items.Count)throw new InvalidDataException("The save reader did not cover the complete tracker.");
        var changedBaseline=reviewed&&task.Baseline!=plan.Baseline;
        task.Baseline=plan.Baseline;task.Conflicts=plan.Rows.Count(r=>r.Conflict);task.Older=plan.Rows.Count(r=>r.Stale);
        task.Changes=plan.Rows.Where(r=>r.Changed||r.Conflict||r.Stale).Select(r=>new RefreshChange(r.Name,r.Previous,r.Incoming,r.Manual,r.Stale)).ToList();
        if(changedBaseline||!reviewed&&(plan.HasRollback||task.Conflicts>0||prior?.Outcome==TrackerRefreshOutcome.Undone||prior?.SourceObservedUtc>plan.ObservedUtc))
        {task.Status="review";task.Message=changedBaseline?"Progress changed since this review. Review the latest differences again.":"Review manual conflicts, older observations or an undone refresh.";return;}
        var attempt=repo.StartRefresh(source.Binding);
        if(!repo.FinishAttempt(source.Binding,attempt,TrackerRefreshOutcome.AwaitingReview,"PC save observations validated."))throw new InvalidOperationException("The source changed during refresh.");
        plan=repo.Preview(task.Snapshot!,task.TrackerId,account,source.Binding.AdapterId,source.Binding.AdapterVersion,task.Fingerprint,source.Binding.Id,attempt);
        if(plan.Baseline!=task.Baseline)
        {
            repo.FinishAttempt(source.Binding,attempt,TrackerRefreshOutcome.ReviewRequired,"Progress changed during the PC check.");
            task.Status="review";task.Message="Progress changed during the PC check. Review again.";return;
        }
        var meaningful=plan.Rows.Any(r=>r.Changed);
        task.Changed=repo.ApplyRefresh(plan,reviewed&&allowOlder,[],CancellationToken.None);task.Status="complete";
        if(task.Changed>0&&meaningful)sync.Put("save-update");
        task.Message=$"{task.Changed} observations updated. Manual progress and existing notes kept.";
        task.Snapshot=null;task.Changes.Clear();
    }
    public GameRefreshView Result(string id,string tracker,ConnectorObservation observation)
    {
        lock(gate)
        {
            using var c=Open();var job=Read(c,id);
            if(job.Lease!=observation.Lease||job.LeaseUntil<DateTime.UtcNow.Ticks||State(job)=="offline")throw new InvalidOperationException("This refresh lease expired. Refresh again.");
            var task=job.Tasks.FirstOrDefault(t=>t.TrackerId==tracker)??throw new KeyNotFoundException();
            if(task.Status is "complete" or "review" or "failed" or "paused")return View(job);
            if(job.Automatic&&!sync.Settings().AutomaticEnabled){task.Status="paused";Write(c,job);return View(job);}
            var pair=Pairing(c)!;var source=Current(pair,tracker);
            if(source is null||source.Revision!=task.Revision||source.Binding.Id!=task.BindingId)throw new InvalidOperationException("The source changed. Pair the PC again.");
            if(observation.Error is not null){task.Status="failed";task.Message=observation.Error=="timeout"?"The PC save check timed out.":"The PC could not read a supported save. Check its configured save folder and game installation.";}
            else
            {
                if(observation.SnapshotJson is null||observation.SnapshotJson.Length>1536*1024||observation.Fingerprint.Length!=64||!observation.Fingerprint.All(Uri.IsHexDigit))throw new InvalidDataException("Invalid save observation.");
                task.Snapshot=observation.SnapshotJson;task.Fingerprint=observation.Fingerprint;
                try{Apply(task,source,false,false);}catch(InvalidOperationException){task.Status="review";task.Message="Progress changed during refresh. Review the latest save changes.";}
            }
            if(!job.Automatic&&task.Status is "review" or "complete")
            {
                foreach(var older in Recent(c).Where(j=>j.Created<job.Created))
                {
                    var superseded=older.Tasks.Where(t=>t.TrackerId==tracker&&t.Status=="review").ToList();
                    foreach(var previous in superseded){previous.Status="superseded";previous.Snapshot=null;previous.Changes.Clear();}
                    if(superseded.Count>0)Write(c,older);
                }
            }
            sync.Put("save-check");sync.Put("source-error",task.Status=="failed"?"A configured save could not be read.":"");Write(c,job);return View(job);
        }
    }
    public GameRefreshView Review(string id,ReviewGameRefresh request)
    {
        lock(gate)
        {
            using var c=Open();var job=Read(c,id);if(request.Version!=ReviewVersion(job))throw new InvalidOperationException("The refresh changed. Reload the review.");
            var pair=Pairing(c)??throw new InvalidOperationException("The PC connection changed.");
            foreach(var task in job.Tasks.Where(t=>t.Status=="review"))
            {
                var source=Current(pair,task.TrackerId)??throw new InvalidOperationException("The source changed. Pair the PC again.");
                try{Apply(task,source,true,request.AllowOlder);}catch(InvalidOperationException){task.Status="review";task.Message="Progress changed. Reload the review before applying.";}
            }
            Write(c,job);return View(job);
        }
    }
    public void Revoke()
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM companion_connector; DELETE FROM companion_refresh_job;";cmd.ExecuteNonQuery();
    }
}
