using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
namespace PixelVault.Companion;

// Durable queue/reviews only. Tracker state and authority remain in the canonical domain repository.
public sealed class LocalRefreshService
{
    public sealed class TaskState
    {
        public string Tracker {get;set;}="";public string Revision {get;set;}="";public string Definition {get;set;}="";
        public string Status {get;set;}="queued";public string Snapshot {get;set;}="";public string Fingerprint {get;set;}="";public string Baseline {get;set;}="";public string Attempt {get;set;}="";
        public string Message {get;set;}="Queued for a local save check.";public int Changed {get;set;}public List<RefreshChange> Changes {get;set;}=[];
        public int Conflicts {get;set;}public int Older {get;set;}public string AutomationId {get;set;}="";public string Signature {get;set;}="";
        public string OpenSignal {get;set;}="";
        public bool Undone {get;set;}
    }
    public sealed class Job
    {
        public string State=>LocalRefreshService.Status(this);
        public string Id {get;set;}="";public string Group {get;set;}="";public long Created {get;set;}public List<TaskState> Tasks {get;set;}=[];
    }
    readonly CompanionOptions options;readonly TrackerWorkspace workspace;readonly GameLibrary library;readonly ConnectorSyncState state;readonly ILocalReaderClient reader;
    readonly object gate=new();readonly SemaphoreSlim execution=new(1,1);
    readonly Dictionary<string,(string Signature,long First)> stable=[];
    readonly HashSet<string> opened=[];
    string Database=>Path.Combine(options.DataRoot,"companion.sqlite");
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public LocalRefreshService(CompanionOptions options,TrackerWorkspace workspace,GameLibrary library,ConnectorSyncState state,ILocalReaderClient reader)
    {
        this.options=options;this.workspace=workspace;this.library=library;this.state=state;this.reader=reader;
        if(!options.DirectLocal)return;
        using var c=Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE IF NOT EXISTS local_refresh_job(workspace TEXT NOT NULL,id TEXT NOT NULL,created INTEGER NOT NULL,payload TEXT NOT NULL,PRIMARY KEY(workspace,id))";q.ExecuteNonQuery();
        // OS process death drops its source lock. Only this queue's interrupted tasks are retried.
        foreach(var job in Jobs(c))if(job.Tasks.Any(t=>t.Status=="reading")){foreach(var task in job.Tasks.Where(t=>t.Status=="reading"))task.Status="queued";Write(c,job);}
    }
    SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Mode=SqliteOpenMode.ReadWrite,Pooling=false,DefaultTimeout=5}.ToString());c.Open();return c;}
    List<Job> Jobs(SqliteConnection c)
    {
        using var q=c.CreateCommand();q.CommandText="SELECT payload FROM local_refresh_job WHERE workspace=$w ORDER BY created DESC LIMIT 200";q.Parameters.AddWithValue("$w",workspace.Identity);var jobs=new List<Job>();using(var r=q.ExecuteReader())while(r.Read())jobs.Add(JsonSerializer.Deserialize<Job>(r.GetString(0),Json)!);
        foreach(var job in jobs)
        {
            bool changed=false;
            foreach(var task in job.Tasks.Where(t=>t.Status=="review"))
            {
                var definition=workspace.Definitions().FirstOrDefault(d=>d.Id==task.Tracker);
                var binding=definition is null?null:workspace.Repository.Source(task.Tracker,workspace.Account(task.Tracker));
                var current=binding is null?null:workspace.Repository.SourceStatus(task.Tracker,binding.Account);
                // Only a successful check covering this snapshot supersedes its review.
                // Failed/in-flight checks and undo retain genuine pending decisions.
                var observed=task.Snapshot.Length==0?long.MaxValue:JsonSerializer.Deserialize<AchievementTrackerSnapshot>(task.Snapshot,AchievementTrackingCatalog.Json)!.ObservedUtcTicks;
                bool replaced=binding is null||Hash(binding)!=task.Revision||Hash(definition!)!=task.Definition;
                bool superseded=current?.Outcome is TrackerRefreshOutcome.Success or TrackerRefreshOutcome.Unchanged
                    &&current.LastSuccessUtc>=job.Created&&current.SourceObservedUtc>=observed;
                if(!replaced&&!superseded)continue;
                task.Status="complete";task.Message=replaced?"The source or tracker changed. This old review was cleared.":"A newer save check completed. This old review was cleared.";
                task.Snapshot="";task.Changes.Clear();task.Conflicts=0;task.Older=0;changed=true;
            }
            if(changed)Write(c,job);
        }
        return jobs;
    }
    void Write(SqliteConnection c,Job job)
    {
        using var q=c.CreateCommand();q.CommandText="INSERT INTO local_refresh_job VALUES($w,$id,$time,$value) ON CONFLICT(workspace,id) DO UPDATE SET payload=excluded.payload";q.Parameters.AddWithValue("$w",workspace.Identity);q.Parameters.AddWithValue("$id",job.Id);q.Parameters.AddWithValue("$time",job.Created);q.Parameters.AddWithValue("$value",JsonSerializer.Serialize(job,Json));q.ExecuteNonQuery();
        q.CommandText="DELETE FROM local_refresh_job WHERE workspace=$w AND id NOT IN (SELECT id FROM local_refresh_job WHERE workspace=$w ORDER BY created DESC LIMIT 100) AND json_extract(payload,'$.state') NOT IN ('reading','review')";q.ExecuteNonQuery();
    }
    static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value,AchievementTrackingCatalog.Json))));
    static string Status(Job job)=>job.Tasks.Any(t=>t.Status is "queued" or "reading")?"reading":job.Tasks.Any(t=>t.Status=="review")?"review":job.Tasks.Any(t=>t.Status=="failed")?"failed":job.Tasks.Any(t=>t.Status=="paused")?"paused":"complete";
    GameRefreshView View(Job job)=>new(job.Id,Status(job),Status(job) switch{"reading"=>"Checking local save files…","review"=>"Save changes need review. Progress and notes are preserved.","failed"=>"A configured source could not be read. Saved progress was preserved.","paused"=>"Automatic updates are paused.",_=>"Local save check complete."},Status(job)=="review",job.Tasks.Any(t=>t.Older>0),Hash(job.Tasks.Select(t=>new{t.Tracker,t.Status,t.Baseline,t.Revision})),job.Tasks.Select(t=>new RefreshTaskView(workspace.Definitions().FirstOrDefault(d=>d.Id==t.Tracker)?.Title??"Tracker",t.Status,t.Message,t.Changed,t.Conflicts,t.Older,t.Changes.Take(80).ToArray())).ToArray());
    void Enabled(){if(!options.DirectLocal||!options.LocalWorkersEnabled)throw new InvalidOperationException("Local save readers are not enabled for this installation.");}
    IEnumerable<(AchievementTrackerDefinition Definition,TrackerSourceBinding Binding)> Sources()
    {
        foreach(var pair in workspace.Repository.SourcesForAccounts(workspace.Manifest!.Accounts))
        {if(pair.Binding.AdapterId is "death-stranding-dc.fabrication" or "pixelvault.json-file")yield return pair;}
    }
    TaskState Task(AchievementTrackerDefinition d,TrackerSourceBinding b,string automation="",string signature="")=>new(){Tracker=d.Id,Revision=Hash(b),Definition=Hash(d),AutomationId=automation,Signature=signature};
    public GameRefreshView Request(RequestGameRefresh request)
    {
        Enabled();if(!Guid.TryParse(request.RequestId,out _))throw new InvalidDataException("Refresh needs a valid request ID.");
        lock(gate)
        {
            using var c=Open();var jobs=Jobs(c);var prior=jobs.FirstOrDefault(j=>j.Id==request.RequestId);if(prior is not null){if(prior.Group!=request.GroupId)throw new InvalidOperationException("This request belongs to another game.");return View(prior);}
            var active=jobs.FirstOrDefault(j=>j.Group==request.GroupId&&Status(j)=="reading");if(active is not null)return View(active);
            if(jobs.Count(j=>Status(j) is "reading" or "review")>=64)throw new InvalidOperationException("Finish pending save checks before starting another.");
            var games=library.Games().Where(g=>g.GroupId==request.GroupId).Select(g=>g.Provider+":"+g.GameId).ToHashSet();var sources=Sources().Where(s=>games.Contains(s.Definition.Provider+":"+s.Definition.GameId)).ToList();
            if(sources.Count==0)throw new InvalidDataException("No supported save source is configured for this game.");
            var job=new Job{Id=request.RequestId,Group=request.GroupId,Created=DateTime.UtcNow.Ticks,Tasks=sources.Select(s=>Task(s.Definition,s.Binding)).ToList()};Write(c,job);return View(job);
        }
    }
    public GameRefreshView Get(string id){Enabled();lock(gate){using var c=Open();return View(Jobs(c).FirstOrDefault(j=>j.Id==id)??throw new KeyNotFoundException());}}
    public void Opened(string tracker){if(options.LocalWorkersEnabled){TrackerSourceLease.SignalOpen(workspace.Repository.DatabasePath,tracker,workspace.Account(tracker));lock(gate)opened.Add(tracker);}}
    public object Connection()
    {
        if(!options.DirectLocal)throw new InvalidOperationException();
        lock(gate)
        {
        using var c=Open();var jobs=Jobs(c);var sources=Sources().ToArray();
        // Connection polling needs source group identities, never photo inventory or tracker totals.
        var games=options.LocalWorkersEnabled?workspace.LocalData!.Games():null;
        return new{versions=workspace.BrowseVersions(),local=true,paired=false,online=options.LocalWorkersEnabled&&state.Get("local-worker-tick").Updated>DateTime.UtcNow.AddSeconds(-30).Ticks,workspaceId=workspace.Identity,libraryEnabled=true,automaticEnabled=state.Settings().AutomaticEnabled,
            supported=options.LocalWorkersEnabled?sources.Select(s=>games!.GetValueOrDefault(s.Definition.Provider+":"+s.Definition.GameId) is { } game ? game.GroupId??GameLibrary.GroupKey(game.Title):GameLibrary.GroupKey(s.Definition.GameTitle)).Distinct().ToArray():[],
            lastSaveCheckUtc=state.Get("save-check").Updated,lastSaveUpdateUtc=state.Get("save-update").Updated,sourceError=state.Get("source-error").Value,lastMonitorCheckUtc=state.Get("local-worker-tick").Updated,
            lastAchievementSyncUtc=state.Get("local-provider-check").Updated,achievementError=state.Get("local-provider-error").Value,reviewJobs=jobs.Where(j=>Status(j)=="review").Select(j=>new{id=j.Id,groupId=j.Group,message="Save changes need review."}).ToArray()};
        }
    }
    LocalReaderRequest Work(string operation,AchievementTrackerDefinition d,TrackerSourceBinding b)=>new(){Operation=operation,ApplicationRoot=options.ApplicationDataRoot,CacheRoot=Path.Combine(options.CacheRoot,"save-readers"),Binding=b,Definition=d,Accounts=workspace.Manifest!.Accounts};
    public async Task Tick(CancellationToken token)
    {
        Enabled();if(!await execution.WaitAsync(0,token))return;
        try
        {
            state.Put("local-worker-tick");
            Job? job;TaskState? task;
            lock(gate){using var c=Open();job=Jobs(c).LastOrDefault(j=>j.Tasks.Any(t=>t.Status=="queued"));task=job?.Tasks.First(t=>t.Status=="queued");if(task is not null){task.Status="reading";Write(c,job!);}}
            if(job is not null&&task is not null){await Read(job,task,token);return;}
            if(!state.Settings().AutomaticEnabled)return;
            var probes=new Dictionary<string,LocalReaderResult>();
            foreach(var (d,b) in Sources())
            {
                token.ThrowIfCancellationRequested();var (settings,progress)=workspace.Repository.Automation(d.Id,b.Account);
                var signal=TrackerSourceLease.OpenSignal(workspace.Repository.DatabasePath,d.Id,b.Account);
                bool wasOpened;lock(gate)wasOpened=opened.Contains(d.Id)||signal.Length>0&&progress.HandledOpenSignal!=signal;
                if(progress.Failures>0&&DateTime.UtcNow.Ticks-progress.LastCheckUtc<TimeSpan.FromSeconds(Math.Min(1800,30*Math.Pow(2,Math.Min(progress.Failures-1,6)))).Ticks)continue;
                if(settings.Paused||settings.Mode==TrackerAutomationMode.Manual||progress.ReviewPending||settings.Mode==TrackerAutomationMode.OnOpen&&!wasOpened||settings.Mode==TrackerAutomationMode.Periodic&&progress.LastCheckUtc>0&&DateTime.UtcNow.Ticks-progress.LastCheckUtc<TimeSpan.FromMinutes(settings.IntervalMinutes).Ticks)continue;
                var pending=workspace.Repository.SourceStatus(d.Id,b.Account);
                if(pending?.Outcome==TrackerRefreshOutcome.AwaitingReview||pending?.Outcome==TrackerRefreshOutcome.Reading&&pending.LastAttemptUtc>DateTime.UtcNow.AddSeconds(-80).Ticks)continue;
                var key=Hash(new{b.AdapterId,b.Account,b.Location,b.GameDirectory,b.SpecificSave});
                LocalReaderResult? probe;
                try{probe=probes.GetValueOrDefault(key);if(probe is null){probe=await reader.Run(Work("probe",d,b),token);probes[key]=probe;}}
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch{progress.LastCheckUtc=DateTime.UtcNow.Ticks;progress.Failures=Math.Min(20,progress.Failures+1);progress.Message="The configured source is unavailable.";workspace.Repository.RecordAutomatic(b,settings.Id,progress);state.Put("source-error",progress.Message);continue;}
                if(!stable.TryGetValue(d.Id,out var observed)||observed.Signature!=probe.Signature){stable[d.Id]=(probe.Signature,Environment.TickCount64);continue;}
                if(Environment.TickCount64-observed.First<3000||settings.Mode==TrackerAutomationMode.SaveChanges&&progress.HandledSignature==probe.Signature&&pending?.Outcome!=TrackerRefreshOutcome.Undone)continue;
                lock(gate)
                {
                    using var c=Open();if(Jobs(c).Any(j=>j.Tasks.Any(t=>t.Tracker==d.Id&&t.Status is "queued" or "reading" or "review")))continue;
                    var automatic=new Job{Id=Guid.NewGuid().ToString("N"),Group=library.Games().FirstOrDefault(g=>g.Provider==d.Provider&&g.GameId==d.GameId)?.GroupId??"",Created=DateTime.UtcNow.Ticks,Tasks=[Task(d,b,settings.Id,probe.Signature)]};Write(c,automatic);opened.Remove(d.Id);
                    automatic.Tasks[0].OpenSignal=signal;Write(c,automatic);
                }
            }
        }
        finally{execution.Release();}
    }
    void Current(TaskState t,out AchievementTrackerDefinition d,out TrackerSourceBinding b)
    {
        d=workspace.Definitions().FirstOrDefault(d=>d.Id==t.Tracker)??throw new InvalidOperationException("The tracker changed.");b=workspace.Repository.Source(t.Tracker,workspace.Account(t.Tracker))??throw new InvalidOperationException("The source changed.");
        if(Hash(b)!=t.Revision||Hash(d)!=t.Definition)throw new InvalidOperationException("The source or tracker changed. Start a fresh check.");
        if(t.AutomationId.Length>0){var s=workspace.Repository.Automation(d.Id,b.Account).Settings;if(!state.Settings().AutomaticEnabled||s.Id!=t.AutomationId||s.Paused||s.Mode==TrackerAutomationMode.Manual)throw new OperationCanceledException("Automatic updates paused.");}
    }
    async Task Read(Job job,TaskState task,CancellationToken token)
    {
        TrackerSourceBinding? binding=null;string attempt="";
        try
        {
            Current(task,out var d,out var b);binding=b;
            using var lease=await TrackerSourceLease.AcquireAsync(workspace.Repository.DatabasePath,b,token);
            Current(task,out d,out b);var prior=workspace.Repository.SourceStatus(d.Id,b.Account);
            if(task.AutomationId.Length>0&&prior?.AttemptId!=task.Attempt&&(prior?.Outcome==TrackerRefreshOutcome.AwaitingReview||prior?.Outcome==TrackerRefreshOutcome.Reading&&prior.LastAttemptUtc>DateTime.UtcNow.AddSeconds(-80).Ticks)){task.Status="queued";task.Message="Another save check is active.";return;}
            attempt=workspace.Repository.StartRefresh(b);task.Attempt=attempt;
            lock(gate){using var c=Open();Write(c,job);}
            var observation=await reader.Run(Work("read",d,b),token);token.ThrowIfCancellationRequested();Current(task,out d,out b);
            if(observation.Fingerprint.Length!=64||!observation.Fingerprint.All(Uri.IsHexDigit)||observation.SnapshotJson is null||observation.SnapshotJson.Length>3*1024*1024)throw new InvalidDataException("Invalid reader observation.");
            task.Snapshot=observation.SnapshotJson;task.Fingerprint=observation.Fingerprint;task.Signature=observation.Signature;
            if(prior?.Fingerprint==task.Fingerprint&&prior.SourceObservedUtc>0&&b.AdapterId=="death-stranding-dc.fabrication")
            {var snapshot=JsonSerializer.Deserialize<AchievementTrackerSnapshot>(task.Snapshot,AchievementTrackingCatalog.Json)!;snapshot.ObservedUtcTicks=prior.SourceObservedUtc;task.Snapshot=JsonSerializer.Serialize(snapshot,AchievementTrackingCatalog.Json);}
            if(!workspace.Repository.FinishAttempt(b,attempt,TrackerRefreshOutcome.AwaitingReview,"Local save observations validated."))throw new InvalidOperationException("The source changed during reading.");
            var plan=Plan(task,b);Describe(task,plan,b);task.Undone=prior?.Outcome==TrackerRefreshOutcome.Undone;
            if(task.Changes.Count==0&&!task.Undone&&(plan.Rows.Any(r=>r.Stale)||prior?.SourceObservedUtc>plan.ObservedUtc))
            {
                // Identical older values need no decision or backwards timestamp write.
                workspace.Repository.FinishAttempt(b,attempt,TrackerRefreshOutcome.Unchanged,"No progress differences need review.");
                task.Status="complete";task.Message="No progress differences.";task.Snapshot="";RecordAutomation(task,b,plan,false);
            }
            else if(task.Older>0||task.Conflicts>0||task.Undone||prior?.SourceObservedUtc>plan.ObservedUtc){task.Status="review";task.Message=task.Undone?"This update was previously undone. Review before applying it again.":"Review manual conflicts or changes from an older save.";RecordAutomation(task,b,plan,true);}
            else Apply(task,b,plan,false);
            state.Put("save-check");state.Put("source-error","");
        }
        catch(OperationCanceledException){task.Status=token.IsCancellationRequested?"queued":"paused";task.Message="Save check stopped. Progress was preserved.";if(binding is not null&&attempt.Length>0)workspace.Repository.FinishAttempt(binding,attempt,TrackerRefreshOutcome.Cancelled,task.Message);if(token.IsCancellationRequested)throw;}
        catch
        {
            task.Status="failed";task.Snapshot="";task.Message="The configured source could not be read. Saved progress was preserved.";state.Put("source-error",task.Message);
            if(binding is not null&&attempt.Length>0)workspace.Repository.FinishAttempt(binding,attempt,TrackerRefreshOutcome.Failed,task.Message);
            if(binding is not null&&task.AutomationId.Length>0){var a=workspace.Repository.Automation(task.Tracker,binding.Account);a.State.LastCheckUtc=DateTime.UtcNow.Ticks;a.State.Failures=Math.Min(20,a.State.Failures+1);a.State.Message=task.Message;workspace.Repository.RecordAutomatic(binding,task.AutomationId,a.State);}
        }
        finally{lock(gate){using var c=Open();Write(c,job);}}
    }
    TrackerRefreshPlan Plan(TaskState t,TrackerSourceBinding b)
    {
        var plan=workspace.Repository.Preview(t.Snapshot,t.Tracker,b.Account,b.AdapterId,b.AdapterVersion,t.Fingerprint,b.Id,t.Attempt);
        if(b.AdapterId=="death-stranding-dc.fabrication"&&plan.Rows.Count!=workspace.Definitions().Single(d=>d.Id==t.Tracker).Items.Count)throw new InvalidDataException("The save reader did not cover the full tracker.");return plan;
    }
    static void Describe(TaskState t,TrackerRefreshPlan p,TrackerSourceBinding b)
    {
        var rows=p.Rows.Where(r=>(!b.ReplaceManualProgress||r.Incoming.HasValue)&&(r.Changed||!b.ReplaceManualProgress&&r.Conflict||b.ReplaceManualProgress&&r.HasManual&&!r.Stale)).ToArray();
        t.Baseline=p.Baseline;t.Conflicts=b.ReplaceManualProgress?0:rows.Count(r=>r.Conflict);t.Older=rows.Count(r=>r.Stale);t.Changes=rows.Select(r=>new RefreshChange(r.Name,r.Previous,r.Incoming,r.Manual,r.Stale)).ToList();
    }
    void RecordAutomation(TaskState t,TrackerSourceBinding b,TrackerRefreshPlan p,bool review)
    {
        if(t.AutomationId.Length==0)return;var a=workspace.Repository.Automation(t.Tracker,b.Account);a.State.LastCheckUtc=DateTime.UtcNow.Ticks;a.State.HandledSignature=t.Signature;a.State.HandledOpenSignal=t.OpenSignal;a.State.Failures=0;a.State.ReviewPending=review;a.State.Message=review?"Local save changes need review.":"Local save check complete.";
        workspace.Repository.RecordAutomatic(b,t.AutomationId,a.State,p,review?TrackerRefreshOutcome.ReviewRequired:null);
    }
    void Apply(TaskState t,TrackerSourceBinding b,TrackerRefreshPlan p,bool allowOlder)
    {
        t.Changed=workspace.Repository.ApplyRefresh(p,allowOlder,[],CancellationToken.None,t.AutomationId.Length==0?null:t.AutomationId);t.Status="complete";t.Message=$"{t.Changed} observations updated. Notes were preserved.";
        RecordAutomation(t,b,p,false);t.Snapshot="";t.Changes.Clear();if(t.Changed>0)state.Put("save-update");
    }
    public GameRefreshView Review(string id,ReviewGameRefresh request)
    {
        Enabled();lock(gate)
        {
            using var c=Open();var job=Jobs(c).FirstOrDefault(j=>j.Id==id)??throw new KeyNotFoundException();if(!job.Tasks.Any(t=>t.Status=="review"))return View(job);if(View(job).Version!=request.Version)throw new InvalidOperationException("The review changed. Reload it.");
            foreach(var task in job.Tasks.Where(t=>t.Status=="review"))
            {
                var automatic=task.AutomationId;task.AutomationId="";
                Current(task,out _,out var b);
                var prior=workspace.Repository.SourceStatus(task.Tracker,b.Account);
                if(prior?.AttemptId==task.Attempt&&prior.Fingerprint==task.Fingerprint&&prior.Outcome is TrackerRefreshOutcome.Success or TrackerRefreshOutcome.Unchanged){task.Status="complete";task.Message="This save check already completed.";task.Snapshot="";task.Changes.Clear();Write(c,job);continue;}
                var p=Plan(task,b);
                if(task.Baseline!=p.Baseline){Describe(task,p,b);Write(c,job);throw new InvalidOperationException("Progress changed. Review the latest differences again.");}
                // A global pause blocks automatic application; a deliberate manual review uses the current domain baseline.
                using var waiting=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                TrackerSourceLease lease;
                try{lease=TrackerSourceLease.AcquireAsync(workspace.Repository.DatabasePath,b,waiting.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){throw new InvalidOperationException("A save reader is active. Try the review again shortly.");}
                using(lease)
                {
                    task.Attempt=workspace.Repository.StartRefresh(b);workspace.Repository.FinishAttempt(b,task.Attempt,TrackerRefreshOutcome.AwaitingReview,"Manual local review.");
                    p=Plan(task,b);if(p.Baseline!=task.Baseline)throw new InvalidOperationException("Progress changed. Reload the review.");Write(c,job);
                    Apply(task,b,p,request.AllowOlder);task.AutomationId=automatic;Write(c,job);
                }
            }
            Write(c,job);return View(job);
        }
    }
}
