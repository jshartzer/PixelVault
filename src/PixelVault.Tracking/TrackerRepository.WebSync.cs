using Microsoft.Data.Sqlite;

namespace PixelVaultNative;

public sealed record TrackerWebField(string ItemId,string Name,int Target,string GroupId,string Milestone,string Field,string Version,
    int? BaselineCurrent,string BaselineNote,int? Current,string Note);
public sealed record TrackerWebPage(string WorkspaceId,string TrackerId,string Provider,string GameId,IReadOnlyList<TrackerWebField> Fields,int? NextOffset);
public sealed record TrackerWebConflict(string WorkspaceId,string TrackerId,string Account,TrackerWebField Incoming,string LocalVersion,int? LocalCurrent,string LocalNote);
public sealed record TrackerWebResult(int Applied,int Conflicts,int Unavailable=0);

public partial class TrackerRepository
{
    void EnsureWebSync(SqliteConnection c,SqliteTransaction tx=null)
    {
        if(!initializeSchema)return;
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="CREATE TABLE IF NOT EXISTS tracker_web_sync(workspace TEXT NOT NULL,tracker_id TEXT NOT NULL,account TEXT NOT NULL,item_id TEXT NOT NULL,field TEXT NOT NULL,remote_version TEXT NOT NULL,local_version TEXT NULL,pending TEXT NULL,PRIMARY KEY(workspace,tracker_id,account,item_id,field))";cmd.ExecuteNonQuery();
    }
    public IReadOnlyList<TrackerWebField> WebAuthoredFields(string tracker,string account,long copiedUtc)
    {
        using var c=Open();using var tx=c.BeginTransaction(deferred:true);
        var d=ReadDefinitions(c,tx).SingleOrDefault(d=>d.Id==tracker)??throw new InvalidDataException("The tracker was not found.");account=RequireAccount(d,account);
        var first=new Dictionary<(string,string),TrackerStoredRecord>();
        // The history viewer intentionally caps at 100 rows; synchronization needs the full authored journal.
        using var journal=c.CreateCommand();journal.Transaction=tx;
        journal.CommandText="SELECT changes FROM tracker_history WHERE tracker_id=$t AND account=$a AND created_utc>$u AND adapter_id='manual' AND kind IN ('manual','note') ORDER BY id";
        journal.Parameters.AddWithValue("$t",tracker);journal.Parameters.AddWithValue("$a",account);journal.Parameters.AddWithValue("$u",copiedUtc);
        using(var entries=journal.ExecuteReader())while(entries.Read())
            foreach(var change in FromJson<List<TrackerHistoryChange>>(entries.GetString(0)))
                foreach(var field in new[]{"progress","note"})
                    if(change.Fields.HasFlag(field=="note"?TrackerChangeFields.Note:TrackerChangeFields.Manual))first.TryAdd((change.ItemId,field),change.Before);
        var result=new List<TrackerWebField>();
        foreach(var key in first.Keys.OrderBy(k=>k.Item1,StringComparer.Ordinal).ThenBy(k=>k.Item2,StringComparer.Ordinal))
        {
            var item=d.Items.SingleOrDefault(i=>i.Id==key.Item1&&!i.Archived);if(item==null)continue;
            var now=ReadRecord(c,tx,tracker,account,item.Id);var before=first[key];var fields=key.Item2=="note"?TrackerChangeFields.Note:TrackerChangeFields.Manual;
            result.Add(new(item.Id,item.Name,item.Target,item.GroupId,item.Milestone,key.Item2,FieldVersion(c,tx,tracker,account,item,fields),before.Manual,before.Note,now.Manual,now.Note));
        }
        tx.Commit();return result;
    }
    static SqliteCommand WebCommand(SqliteConnection c,SqliteTransaction tx,string workspace,string tracker,string account,TrackerWebField f)
    {
        var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.Parameters.AddWithValue("$w",workspace);cmd.Parameters.AddWithValue("$t",tracker);cmd.Parameters.AddWithValue("$a",account);cmd.Parameters.AddWithValue("$i",f.ItemId);cmd.Parameters.AddWithValue("$f",f.Field);return cmd;
    }
    static void ValidateWebField(AchievementTrackerDefinition d,TrackerWebField f)
    {
        var item=d.Items.SingleOrDefault(i=>i.Id==f?.ItemId&&!i.Archived);
        if(item==null||item.Name!=f.Name||item.Target!=f.Target||(item.GroupId??"")!=(f.GroupId??"")||(item.Milestone??"")!=(f.Milestone??"")
            ||f.Field is not ("progress" or "note")||f.Version?.Length!=64||!f.Version.All(Uri.IsHexDigit)||f.Current is <0 or >1000000||f.BaselineCurrent is <0 or >1000000||f.Note==null||f.Note.Length>20000||(f.BaselineNote?.Length??0)>20000)
            throw new InvalidDataException("A web edit does not match the active PC tracker item. Both values were preserved.");
    }
    public bool NeedsWebSync(string workspace,string tracker,string account,IReadOnlyList<TrackerWebField> fields)
    {
        var d=Definition(tracker);account=RequireAccount(d,account);foreach(var f in fields)ValidateWebField(d,f);
        using var c=Open();EnsureWebSync(c);
        foreach(var f in fields)
        {
            using var cmd=WebCommand(c,null,workspace,tracker,account,f);cmd.CommandText="SELECT remote_version FROM tracker_web_sync WHERE workspace=$w AND tracker_id=$t AND account=$a AND item_id=$i AND field=$f";
            if(cmd.ExecuteScalar() as string!=f.Version)return true;
        }
        return false;
    }
    public TrackerWebResult ApplyWebEdits(string workspace,string tracker,string account,IReadOnlyList<TrackerWebField> incoming,bool preview=false)
    {
        if(workspace?.Length!=64||!workspace.All(Uri.IsHexDigit)||incoming==null||incoming.Count>100||incoming.Any(f=>f==null)||incoming.Select(f=>(f.ItemId,f.Field)).Distinct().Count()!=incoming.Count)throw new InvalidDataException("Invalid web tracker batch.");
        using var c=Open();using var tx=c.BeginTransaction();EnsureWebSync(c,tx);
        var d=ReadDefinitions(c,tx).SingleOrDefault(d=>d.Id==tracker)??throw new InvalidDataException("The tracker was not found.");account=RequireAccount(d,account);
        foreach(var f in incoming)ValidateWebField(d,f);
        var changes=new List<TrackerHistoryChange>();int conflicts=0;
        foreach(var f in incoming)
        {
            using var cmd=WebCommand(c,tx,workspace,tracker,account,f);cmd.CommandText="SELECT remote_version,local_version,pending FROM tracker_web_sync WHERE workspace=$w AND tracker_id=$t AND account=$a AND item_id=$i AND field=$f";
            string remote=null,shared=null;using(var row=cmd.ExecuteReader())if(row.Read()){remote=row.GetString(0);shared=row.IsDBNull(1)?null:row.GetString(1);}
            if(remote==f.Version)continue;
            var item=d.Items.Single(i=>i.Id==f.ItemId);var fields=f.Field=="note"?TrackerChangeFields.Note:TrackerChangeFields.Manual;
            var before=ReadRecord(c,tx,tracker,account,f.ItemId);var version=FieldVersion(c,tx,tracker,account,item,fields);
            bool same=f.Field=="note"?before.Note==f.Note:before.Manual==f.Current;
            bool baseline=shared!=null?shared==version:f.Field=="note"?before.Note==(f.BaselineNote??""):before.Manual==f.BaselineCurrent;
            bool protectedValue=f.Field=="progress"&&f.Current.HasValue&&f.Current<item.Target&&GameConfirmed(c,tx,tracker,account,item,before,GameFileAdapterIds);
            bool conflict=!same&&(!baseline||protectedValue);
            if(!same&&!conflict)
            {
                var after=before.Fields(fields);if(f.Field=="note")after.Note=f.Note;else{after.Manual=f.Current;after.ManualUtc=DateTime.UtcNow.Ticks;}
                WriteFields(c,tx,tracker,account,f.ItemId,after,fields);
                // Record each field before calculating its durable local version.
                RecordBatch(c,tx,tracker,account,"web-sync","Web "+(f.Field=="note"?"note: ":"progress: ")+item.Name,"companion-web","1",f.Version,0,new(){new(){ItemId=f.ItemId,Fields=fields,Before=before.Fields(fields),After=after}});
                changes.Add(new(){ItemId=f.ItemId});
            }
            if(conflict)conflicts++;
            cmd.CommandText="INSERT INTO tracker_web_sync(workspace,tracker_id,account,item_id,field,remote_version,local_version,pending) VALUES($w,$t,$a,$i,$f,$r,$l,$p) ON CONFLICT(workspace,tracker_id,account,item_id,field) DO UPDATE SET remote_version=$r,local_version=$l,pending=$p";
            cmd.Parameters.AddWithValue("$r",f.Version);cmd.Parameters.AddWithValue("$l",conflict?(object)shared??DBNull.Value:FieldVersion(c,tx,tracker,account,item,fields));cmd.Parameters.AddWithValue("$p",conflict?Json(f):(object)DBNull.Value);cmd.ExecuteNonQuery();
        }
        if(preview)tx.Rollback();else tx.Commit();return new(changes.Count,conflicts);
    }
    public IReadOnlyList<TrackerWebConflict> WebSyncConflicts(string tracker=null,string account=null)
    {
        using var c=Open();EnsureWebSync(c);using var tx=c.BeginTransaction(deferred:true);using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT workspace,tracker_id,account,pending FROM tracker_web_sync WHERE pending IS NOT NULL AND ($t IS NULL OR tracker_id=$t) AND ($a IS NULL OR account=$a)";cmd.Parameters.AddWithValue("$t",(object)tracker??DBNull.Value);cmd.Parameters.AddWithValue("$a",(object)account??DBNull.Value);
        var rows=new List<(string W,string T,string A,TrackerWebField F)>();using(var r=cmd.ExecuteReader())while(r.Read())rows.Add((r.GetString(0),r.GetString(1),r.GetString(2),FromJson<TrackerWebField>(r.GetString(3))));
        var definitions=ReadDefinitions(c,tx);var result=new List<TrackerWebConflict>();
        foreach(var row in rows)
        {
            var item=definitions.SingleOrDefault(d=>d.Id==row.T)?.Items.SingleOrDefault(i=>i.Id==row.F.ItemId&&!i.Archived);if(item==null)continue;
            var value=ReadRecord(c,tx,row.T,row.A,item.Id);result.Add(new(row.W,row.T,row.A,row.F,FieldVersion(c,tx,row.T,row.A,item,row.F.Field=="note"?TrackerChangeFields.Note:TrackerChangeFields.Manual),value.Manual,value.Note));
        }
        tx.Commit();return result;
    }
    public void ResolveWebSync(TrackerWebConflict conflict,bool useWeb)
    {
        var f=conflict.Incoming;using var c=Open();using var tx=c.BeginTransaction();EnsureWebSync(c,tx);
        var d=ReadDefinitions(c,tx).Single(x=>x.Id==conflict.TrackerId);ValidateWebField(d,f);var account=RequireAccount(d,conflict.Account);var item=d.Items.Single(i=>i.Id==f.ItemId);var fields=f.Field=="note"?TrackerChangeFields.Note:TrackerChangeFields.Manual;
        using var cmd=WebCommand(c,tx,conflict.WorkspaceId,d.Id,account,f);cmd.CommandText="SELECT pending FROM tracker_web_sync WHERE workspace=$w AND tracker_id=$t AND account=$a AND item_id=$i AND field=$f";
        if(cmd.ExecuteScalar() as string!=Json(f)||FieldVersion(c,tx,d.Id,account,item,fields)!=conflict.LocalVersion)throw new InvalidOperationException("This field changed. Reload the review before choosing a value.");
        if(useWeb)
        {
            var prior=ReadRecord(c,tx,d.Id,account,item.Id);if(f.Field=="progress"&&f.Current.HasValue&&f.Current<item.Target&&GameConfirmed(c,tx,d.Id,account,item,prior,GameFileAdapterIds))throw new InvalidOperationException("Game data confirms completion. Keep the PC value or review its source first.");
            var after=prior.Fields(fields);if(f.Field=="note")after.Note=f.Note;else{after.Manual=f.Current;after.ManualUtc=DateTime.UtcNow.Ticks;}
            WriteFields(c,tx,d.Id,account,item.Id,after,fields);RecordBatch(c,tx,d.Id,account,"web-sync","Reviewed web edit: "+item.Name,"companion-web","1",f.Version,0,new(){new(){ItemId=item.Id,Fields=fields,Before=prior.Fields(fields),After=after}});
        }
        cmd.CommandText="UPDATE tracker_web_sync SET local_version=$l,pending=NULL WHERE workspace=$w AND tracker_id=$t AND account=$a AND item_id=$i AND field=$f";cmd.Parameters.AddWithValue("$l",FieldVersion(c,tx,d.Id,account,item,fields));cmd.ExecuteNonQuery();tx.Commit();
    }
}
