using Microsoft.Data.Sqlite;

namespace PixelVaultNative;

public sealed record TrackerFieldVersions(string Progress, string Note);
public sealed record TrackerEditor(AchievementTrackerDefinition Definition, IReadOnlyList<AchievementTrackerState> States,
    IReadOnlyDictionary<string, TrackerFieldVersions> Versions, IReadOnlySet<string> ProtectedItems)
{
    public TrackerTotals Totals => TrackerTotals.Calculate(Definition, States);
}
public sealed record TrackerEdit(string ItemId, string Field, int? Current, string Note, string ExpectedVersion, string RequestId);
public sealed record TrackerMutation(long? HistoryId, bool Replayed);
public sealed record TrackerListing(AchievementTrackerDefinition Definition,TrackerTotals Totals);
public interface ITrackerRepository
{
    IReadOnlyList<AchievementTrackerDefinition> Definitions();
    TrackerEditor ReadEditor(string tracker, string account);
    TrackerMutation Edit(string tracker, string account, TrackerEdit command);
    TrackerMutation UndoCommand(string tracker, string account, long historyId, string requestId);
    IReadOnlyList<TrackerHistoryEntry> History(string tracker, string account);
}

public partial class TrackerRepository : ITrackerRepository
{
    public IReadOnlyList<TrackerListing> ListForAccounts(IReadOnlyDictionary<string,string> approved)
    {
        using var c=Open();using var tx=c.BeginTransaction(deferred:true);
        var rows=ReadDefinitions(c,tx).Where(d=>approved.ContainsKey(d.Provider)).Select(d=>new TrackerListing(d,
            TrackerTotals.Calculate(d,ReadStates(c,tx,d,AchievementTrackingCatalog.AccountKey(d.Provider,approved[d.Provider]))))).ToList();
        tx.Commit();return rows;
    }
    private static void EnsureRequests(SqliteConnection c, SqliteTransaction tx)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS tracker_client_request(
              tracker_id TEXT NOT NULL,account TEXT NOT NULL,request_id TEXT NOT NULL,fingerprint TEXT NOT NULL,
              history_id INTEGER NULL,PRIMARY KEY(tracker_id,account,request_id));
            """;
        cmd.ExecuteNonQuery();
    }

    private static string RequestKey(string value) => Guid.TryParse(value, out var key) && value.Length <= 36
        ? key.ToString("N") : throw new InvalidDataException("Each save needs a valid request ID.");

    private static TrackerMutation PreviousRequest(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string request, string fingerprint)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT fingerprint,history_id FROM tracker_client_request WHERE tracker_id=$t AND account=$a AND request_id=$r";
        cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$r", request);
        using var row = cmd.ExecuteReader();
        if (!row.Read()) return null;
        if (row.GetString(0) != fingerprint) throw new InvalidOperationException("This request ID was already used for a different save.");
        return new(row.IsDBNull(1) ? null : row.GetInt64(1), true);
    }

    private static TrackerMutation Remember(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string request, string fingerprint, long? history)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO tracker_client_request(tracker_id,account,request_id,fingerprint,history_id) VALUES($t,$a,$r,$f,$h)";
        cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$r", request);
        cmd.Parameters.AddWithValue("$f", fingerprint); cmd.Parameters.AddWithValue("$h", (object)history ?? DBNull.Value); cmd.ExecuteNonQuery();
        return new(history, false);
    }

    private static string FieldVersion(SqliteConnection c, SqliteTransaction tx, string tracker, string account, AchievementTrackerItem item, TrackerChangeFields fields)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COALESCE(MAX(h.id),0),COALESCE(SUM(CASE WHEN h.undone_utc>0 THEN 1 ELSE 0 END),0) FROM tracker_history h,json_each(h.changes) j
            WHERE h.tracker_id=$t AND h.account=$a AND json_extract(j.value,'$.ItemId')=$i
            AND (json_extract(j.value,'$.Fields') & $fields)<>0
            """;
        cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account);
        cmd.Parameters.AddWithValue("$i", item.Id); cmd.Parameters.AddWithValue("$fields", (int)fields);
        using var row = cmd.ExecuteReader(); row.Read(); var events = row.GetInt64(0) + ":" + row.GetInt64(1); row.Close();
        return Version(ReadRecord(c, tx, tracker, account, item.Id), item, fields, events,tracker,account);
    }

    private static string Version(TrackerStoredRecord record, AchievementTrackerItem item, TrackerChangeFields fields, string events,string tracker,string account) =>
        Digest((int)fields+"|"+Json(new[]{tracker,account})+"|"+Json(record.Fields(fields)) + "|" + Json(item) + "|" + events);

    private static TrackerStoredRecord Stored(AchievementTrackerState s) => new() { Reported = s.ReportedCurrent, Manual = s.ManualCurrent,
        Availability = (int)s.Availability, Note = s.Note, Source = s.Source, Evidence = s.Evidence, ObservedUtc = s.ObservedUtcTicks, ManualUtc = s.ManualUtcTicks };

    private static Dictionary<(string Item, bool Note), (long Id, long Undone)> VersionEvents(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT json_extract(j.value,'$.ItemId'),json_extract(j.value,'$.Fields'),h.id,h.undone_utc FROM tracker_history h,json_each(h.changes) j WHERE h.tracker_id=$t AND h.account=$a";
        cmd.Parameters.AddWithValue("$t",tracker);cmd.Parameters.AddWithValue("$a",account);using var r=cmd.ExecuteReader();
        var events=new Dictionary<(string,bool),(long,long)>();
        while(r.Read()) foreach(var note in new[]{false,true})
        {
            var fields=note?TrackerChangeFields.Note:TrackerChangeFields.Manual|TrackerChangeFields.Reported;
            if((r.GetInt32(1)&(int)fields)==0)continue;
            var key=(r.GetString(0),note);var previous=events.GetValueOrDefault(key);
            events[key]=(Math.Max(previous.Item1,r.GetInt64(2)),previous.Item2+(r.GetInt64(3)>0?1:0));
        }
        return events;
    }

    private static bool GameConfirmed(SqliteConnection c, SqliteTransaction tx, string tracker, string account, AchievementTrackerItem item,
        TrackerStoredRecord prior, IReadOnlySet<string> native)
    {
        var reports = LatestInlineReports(c, tx, tracker, account, item.Id);
        return prior.Reported >= item.Target && reports.TryGetValue(item.Id, out var report) && native.Contains(report.Adapter)
            && Json(report.After.Fields(TrackerChangeFields.Reported)) == Json(prior.Fields(TrackerChangeFields.Reported));
    }

    public TrackerEditor ReadEditor(string tracker, string account)
    {
        using var c = Open(); using var tx = c.BeginTransaction(deferred: true);
        var definition = ReadDefinitions(c, tx).FirstOrDefault(d => d.Id == tracker)
            ?? throw new InvalidDataException("The tracker was not found.");
        account = RequireAccount(definition, account);
        var states = ReadStates(c, tx, definition, account);
        var events=VersionEvents(c,tx,tracker,account);
        string Stamp(string item,bool note) {var e=events.GetValueOrDefault((item,note));return e.Id+":"+e.Undone;}
        var versions = states.Where(s => !s.Item.Archived).ToDictionary(s => s.Item.Id, s => new TrackerFieldVersions(
            Version(Stored(s),s.Item,TrackerChangeFields.Manual|TrackerChangeFields.Reported,Stamp(s.Item.Id,false),tracker,account),
            Version(Stored(s),s.Item,TrackerChangeFields.Note,Stamp(s.Item.Id,true),tracker,account)));
        var reports = LatestInlineReports(c, tx, tracker, account);
        var native = GameFileAdapterIds;
        var protectedItems = states.Where(s => s.ReportedCurrent >= s.Item.Target && reports.TryGetValue(s.Item.Id, out var report)
            && native.Contains(report.Adapter) && report.After.Reported == s.ReportedCurrent && report.After.ObservedUtc == s.ObservedUtcTicks
            && report.After.Source == s.Source && report.After.Evidence == s.Evidence && report.After.Availability == (int)s.Availability)
            .Select(s => s.Item.Id).ToHashSet(StringComparer.Ordinal);
        tx.Commit(); return new(definition, states, versions, protectedItems);
    }

    public TrackerMutation Edit(string tracker, string account, TrackerEdit command)
    {
        if (command is null || command.ExpectedVersion?.Length != 64 || command.Field is not ("progress" or "note" or "source")
            || (command.Note?.Length ?? 0) > 20000 || command.Current < 0 || command.Current > 1000000)
            throw new InvalidDataException("Invalid tracker save.");
        var request = RequestKey(command.RequestId); var fingerprint = Digest(Json(command));
        using var c = Open(); using var tx = c.BeginTransaction();
        var definition = ReadDefinitions(c, tx).FirstOrDefault(d => d.Id == tracker)
            ?? throw new InvalidDataException("The tracker was not found.");
        account = RequireAccount(definition, account); EnsureRequests(c, tx);
        var replay = PreviousRequest(c, tx, tracker, account, request, fingerprint);
        if (replay != null) { tx.Commit(); return replay; }
        var item = definition.Items.SingleOrDefault(i => i.Id == command.ItemId && !i.Archived)
            ?? throw new InvalidDataException("This item is no longer available.");
        var fields = command.Field == "note" ? TrackerChangeFields.Note : TrackerChangeFields.Manual;
        var revisionFields = fields == TrackerChangeFields.Note ? fields : fields | TrackerChangeFields.Reported;
        if (FieldVersion(c, tx, tracker, account, item, revisionFields) != command.ExpectedVersion)
            throw new InvalidOperationException("This value changed in another window. Reload the tracker and review your edit.");
        var prior = ReadRecord(c, tx, tracker, account, item.Id);
        var after = prior.Fields(fields);
        if (fields == TrackerChangeFields.Note) after.Note = command.Note ?? "";
        else
        {
            if (command.Field == "source" && command.Current.HasValue || command.Field == "progress" && !command.Current.HasValue)
                throw new InvalidDataException("Choose a progress count or use the saved source value.");
            var value = command.Field == "source" ? null : command.Current;
            if (value.HasValue && GameConfirmed(c, tx, tracker, account, item, prior, GameFileAdapterIds))
            {
                if(value < item.Target)throw new InvalidOperationException("Game data confirms completion. Use source progress to retain that completion.");
                if(item.Target==1 && value==1)value=null;
            }
            after.Manual = value;
            if (value != prior.Manual) after.ManualUtc = DateTime.UtcNow.Ticks;
        }
        var previousHistory = ReadHistory(c, tx, tracker, account).FirstOrDefault()?.Id ?? 0;
        WriteFields(c, tx, tracker, account, item.Id, after, fields);
        RecordBatch(c, tx, tracker, account, fields == TrackerChangeFields.Note ? "note" : "manual",
            (fields == TrackerChangeFields.Note ? "Note: " : "Progress: ") + item.Name, "manual", "1", "", 0,
            new() { new() { ItemId = item.Id, Fields = fields, Before = prior.Fields(fields), After = after } });
        var latest = ReadHistory(c, tx, tracker, account).FirstOrDefault()?.Id ?? 0;
        var result = Remember(c, tx, tracker, account, request, fingerprint, latest == previousHistory ? null : latest);
        tx.Commit(); return result;
    }

    public TrackerMutation UndoCommand(string tracker, string account, long historyId, string requestId)
    {
        var request = RequestKey(requestId); var fingerprint = Digest("undo:" + historyId);
        using var c = Open(); using var tx = c.BeginTransaction();
        var definition = ReadDefinitions(c, tx).FirstOrDefault(d => d.Id == tracker) ?? throw new InvalidDataException("The tracker was not found.");
        account = RequireAccount(definition, account); EnsureRequests(c, tx);
        var replay = PreviousRequest(c, tx, tracker, account, request, fingerprint);
        if (replay != null) { tx.Commit(); return replay; }
        UndoCore(c, tx, tracker, account, historyId);
        var result = Remember(c, tx, tracker, account, request, fingerprint, historyId);
        tx.Commit(); return result;
    }
}
