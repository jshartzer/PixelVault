using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    [Flags] enum TrackerChangeFields { Reported = 1, Manual = 2, Note = 4, All = 7 }
    sealed class TrackerStoredRecord
    {
        public int? Reported { get; set; }
        public int? Manual { get; set; }
        public int Availability { get; set; }
        public string Note { get; set; } = "";
        public string Source { get; set; } = "";
        public string Evidence { get; set; } = "";
        public long ObservedUtc { get; set; }
        public long ManualUtc { get; set; }
        internal TrackerStoredRecord Fields(TrackerChangeFields fields) => new()
        {
            Reported = fields.HasFlag(TrackerChangeFields.Reported) ? Reported : null,
            Availability = fields.HasFlag(TrackerChangeFields.Reported) ? Availability : 0,
            Source = fields.HasFlag(TrackerChangeFields.Reported) ? Source : "",
            Evidence = fields.HasFlag(TrackerChangeFields.Reported) ? Evidence : "",
            ObservedUtc = fields.HasFlag(TrackerChangeFields.Reported) ? ObservedUtc : 0,
            Manual = fields.HasFlag(TrackerChangeFields.Manual) ? Manual : null,
            ManualUtc = fields.HasFlag(TrackerChangeFields.Manual) ? ManualUtc : 0,
            Note = fields.HasFlag(TrackerChangeFields.Note) ? Note : ""
        };
    }
    sealed class TrackerHistoryChange
    {
        public string ItemId { get; set; }
        public TrackerChangeFields Fields { get; set; }
        public TrackerStoredRecord Before { get; set; }
        public TrackerStoredRecord After { get; set; }
    }
    sealed class TrackerHistoryEntry
    {
        public long Id { get; init; }
        public string Kind { get; init; }
        public string Label { get; init; }
        public long CreatedUtc { get; init; }
        public long UndoneUtc { get; init; }
        public string AdapterId { get; init; }
        public string AdapterVersion { get; init; }
        public string Fingerprint { get; init; }
        public long ObservedUtc { get; init; }
        internal string Changes { get; init; }
        public string Display => new DateTime(CreatedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g") + " · " + Label + (UndoneUtc > 0 ? " · Undone" : "");
    }

    sealed partial class AchievementTrackingStore
    {
        static string Json<T>(T value) => JsonSerializer.Serialize(value, AchievementTrackingCatalog.Json);
        static T FromJson<T>(string text) => JsonSerializer.Deserialize<T>(text, AchievementTrackingCatalog.Json);
        static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        static bool CanAddSourceNote(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string item, TrackerStoredRecord prior, string note)
        {
            if (prior.Note.Length > 0 || string.IsNullOrWhiteSpace(note)) return false;
            // An explicitly cleared manual note remains authored data, even though its text is empty.
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM tracker_history h,json_each(h.changes) j WHERE h.tracker_id=$t AND h.account=$a AND h.kind='note' AND h.undone_utc=0 AND json_extract(j.value,'$.ItemId')=$i";
            cmd.Parameters.AddWithValue("$t",tracker); cmd.Parameters.AddWithValue("$a",account); cmd.Parameters.AddWithValue("$i",item);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 0;
        }

        static TrackerStoredRecord ReadRecord(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string item)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT reported_current,manual_current,availability,note,source,evidence,observed_utc,manual_utc FROM tracker_item_state WHERE tracker_id=$t AND account=$a AND item_id=$i";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$i", item);
            using var r = cmd.ExecuteReader();
            return !r.Read() ? new() : new TrackerStoredRecord { Reported = r.IsDBNull(0) ? null : r.GetInt32(0), Manual = r.IsDBNull(1) ? null : r.GetInt32(1),
                Availability = r.GetInt32(2), Note = r.GetString(3), Source = r.GetString(4), Evidence = r.GetString(5), ObservedUtc = r.GetInt64(6), ManualUtc = r.GetInt64(7) };
        }

        static void WriteFields(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string item, TrackerStoredRecord value, TrackerChangeFields fields)
        {
            var values = new Dictionary<string, object>();
            if (fields.HasFlag(TrackerChangeFields.Reported))
            {
                values["reported_current"] = value.Reported; values["availability"] = value.Availability; values["source"] = value.Source;
                values["evidence"] = value.Evidence; values["observed_utc"] = value.ObservedUtc;
            }
            if (fields.HasFlag(TrackerChangeFields.Manual)) { values["manual_current"] = value.Manual; values["manual_utc"] = value.ManualUtc; }
            if (fields.HasFlag(TrackerChangeFields.Note)) values["note"] = value.Note;
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            var names = values.Keys.ToList();
            cmd.CommandText = "INSERT INTO tracker_item_state(tracker_id,account,item_id," + string.Join(",", names) + ") VALUES($t,$a,$i," + string.Join(",", names.Select(n => "$" + n))
                + ") ON CONFLICT(tracker_id,account,item_id) DO UPDATE SET " + string.Join(",", names.Select(n => n + "=$" + n));
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$i", item);
            foreach (var pair in values) cmd.Parameters.AddWithValue("$" + pair.Key, pair.Value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        static void RecordBatch(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string kind, string label, string adapter, string version,
            string fingerprint, long observedUtc, List<TrackerHistoryChange> changes)
        {
            changes = changes.Where(x => Json(x.Before) != Json(x.After)).ToList(); if (changes.Count == 0) return;
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO tracker_history(tracker_id,account,kind,label,created_utc,adapter_id,adapter_version,fingerprint,observed_utc,changes) VALUES($t,$a,$k,$l,$u,$d,$v,$f,$o,$c)";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$k", kind);
            cmd.Parameters.AddWithValue("$l", label); cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.Ticks); cmd.Parameters.AddWithValue("$d", adapter);
            cmd.Parameters.AddWithValue("$v", version); cmd.Parameters.AddWithValue("$f", fingerprint); cmd.Parameters.AddWithValue("$o", observedUtc);
            cmd.Parameters.AddWithValue("$c", Json(changes)); cmd.ExecuteNonQuery();
        }

        internal IReadOnlyList<TrackerHistoryEntry> History(string tracker, string account)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open();
            return ReadHistory(c, null, tracker, account);
        }
        static List<TrackerHistoryEntry> ReadHistory(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT id,kind,label,created_utc,undone_utc,adapter_id,adapter_version,fingerprint,observed_utc,changes FROM tracker_history WHERE tracker_id=$t AND account=$a ORDER BY id DESC LIMIT 100";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader(); var rows = new List<TrackerHistoryEntry>();
            while (r.Read()) rows.Add(new() { Id = r.GetInt64(0), Kind = r.GetString(1), Label = r.GetString(2), CreatedUtc = r.GetInt64(3), UndoneUtc = r.GetInt64(4),
                AdapterId = r.GetString(5), AdapterVersion = r.GetString(6), Fingerprint = r.GetString(7), ObservedUtc = r.GetInt64(8), Changes = r.GetString(9) });
            return rows;
        }
        internal TrackerHistoryEntry Undo(string tracker, string account, long historyId)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open(); using var tx = c.BeginTransaction();
            var entry = ReadHistory(c, tx, tracker, account).FirstOrDefault(h => h.Id == historyId && h.UndoneUtc == 0)
                ?? throw new InvalidDataException("This change is unavailable or has already been undone.");
            var changes = FromJson<List<TrackerHistoryChange>>(entry.Changes);
            foreach (var change in changes)
                if (Json(ReadRecord(c, tx, tracker, account, change.ItemId).Fields(change.Fields)) != Json(change.After))
                    throw new InvalidOperationException("An affected value changed later. Undo the newer change first; saved data has not been changed.");
            foreach (var change in changes) WriteFields(c, tx, tracker, account, change.ItemId, change.Before, change.Fields);
            using var mark = c.CreateCommand(); mark.Transaction = tx;
            mark.CommandText = "UPDATE tracker_history SET undone_utc=$u WHERE id=$id AND tracker_id=$t AND account=$a AND undone_utc=0";
            mark.Parameters.AddWithValue("$u", DateTime.UtcNow.Ticks); mark.Parameters.AddWithValue("$id", entry.Id); mark.Parameters.AddWithValue("$t", tracker); mark.Parameters.AddWithValue("$a", account); mark.ExecuteNonQuery();
            if (entry.Kind == "refresh")
            {
                var source = ReadSource(c, tx, tracker, account);
                if (source.Binding != null)
                {
                    source.Status.AttemptId = Guid.NewGuid().ToString("N"); source.Status.Outcome = TrackerRefreshOutcome.Undone;
                    source.Status.Message = "A refresh was undone. Refresh again to reconcile with the source.";
                    WriteStatus(c, tx, source.Binding, source.Status);
                }
            }
            tx.Commit(); return entry;
        }

        internal void SaveSource(TrackerSourceBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            binding.Account = RequireAccount(Definition(binding.TrackerId), binding.Account);
            if (string.IsNullOrWhiteSpace(binding.AdapterId) || string.IsNullOrWhiteSpace(binding.AdapterVersion) || string.IsNullOrWhiteSpace(binding.Location)
                || binding.Location.Length > 4000 || (binding.Profile?.Length ?? 0) > 200 || (binding.GameDirectory?.Length ?? 0) > 4000 || (binding.SpecificSave?.Length ?? 0) > 4000) throw new InvalidDataException("The source configuration is invalid.");
            binding.Id = Guid.NewGuid().ToString("N");
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO tracker_source(tracker_id,account,binding,status) VALUES($t,$a,$b,$s) ON CONFLICT(tracker_id,account) DO UPDATE SET binding=$b,status=$s";
            cmd.Parameters.AddWithValue("$t", binding.TrackerId); cmd.Parameters.AddWithValue("$a", binding.Account); cmd.Parameters.AddWithValue("$b", Json(binding));
            cmd.Parameters.AddWithValue("$s", Json(new TrackerSourceStatus { BindingId = binding.Id, Message = "Source configured. Refresh to read current observations." })); cmd.ExecuteNonQuery();
            AutomationChanged?.Invoke();
        }
        internal TrackerSourceBinding Source(string tracker, string account)
        {
            account = AchievementTrackingCatalog.AccountKey(Definition(tracker).Provider, account);
            using var c = Open(); return ReadSource(c, null, tracker, account).Binding;
        }
        internal void RemoveSource(string tracker, string account)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM tracker_source WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.ExecuteNonQuery();
            AutomationChanged?.Invoke();
        }
        internal TrackerSourceStatus SourceStatus(string tracker, string account)
        {
            account = AchievementTrackingCatalog.AccountKey(Definition(tracker).Provider, account);
            using var c = Open(); return ReadSource(c, null, tracker, account).Status;
        }
        static (TrackerSourceBinding Binding, TrackerSourceStatus Status) ReadSource(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT binding,status FROM tracker_source WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader();
            return r.Read() ? (FromJson<TrackerSourceBinding>(r.GetString(0)), FromJson<TrackerSourceStatus>(r.GetString(1))) : (null, null);
        }
        static void WriteStatus(SqliteConnection c, SqliteTransaction tx, TrackerSourceBinding binding, TrackerSourceStatus status)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE tracker_source SET status=$s WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", binding.TrackerId); cmd.Parameters.AddWithValue("$a", binding.Account); cmd.Parameters.AddWithValue("$s", Json(status)); cmd.ExecuteNonQuery();
        }
        internal string StartRefresh(TrackerSourceBinding binding)
        {
            using var c = Open(); using var tx = c.BeginTransaction(); var current = ReadSource(c, tx, binding.TrackerId, binding.Account);
            if (current.Binding?.Id != binding.Id) throw new InvalidOperationException("The source configuration changed. Refresh again.");
            current.Status.AttemptId = Guid.NewGuid().ToString("N"); current.Status.LastAttemptUtc = DateTime.UtcNow.Ticks;
            current.Status.Outcome = TrackerRefreshOutcome.Reading; current.Status.Message = "Reading source…";
            WriteStatus(c, tx, binding, current.Status); tx.Commit(); return current.Status.AttemptId;
        }
        internal bool FinishAttempt(TrackerSourceBinding binding, string attempt, TrackerRefreshOutcome outcome, string message, TrackerRefreshOutcome? expectedOutcome = null)
        {
            using var c = Open(); using var tx = c.BeginTransaction(); var current = ReadSource(c, tx, binding.TrackerId, binding.Account);
            if (current.Binding?.Id != binding.Id || current.Status?.AttemptId != attempt) return false;
            if (expectedOutcome.HasValue && current.Status.Outcome != expectedOutcome.Value) return false;
            current.Status.Outcome = outcome; current.Status.Message = message; WriteStatus(c, tx, binding, current.Status); tx.Commit(); return true;
        }

        AchievementTrackerSnapshot ValidateObservation(string json, string tracker, string account)
        {
            if (json == null || json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Progress import is empty or too large.");
            var snapshot = FromJson<AchievementTrackerSnapshot>(json) ?? throw new InvalidDataException("Invalid snapshot."); var definition = Definition(tracker);
            account = RequireAccount(definition, account);
            if (snapshot.Version != 1 || snapshot.TrackerId != tracker || snapshot.Provider != definition.Provider || snapshot.GameId != definition.GameId
                || AchievementTrackingCatalog.AccountKey(snapshot.Provider, snapshot.Account) != account) throw new InvalidDataException("The observations belong to another tracker, game or account.");
            if (snapshot.ObservedUtcTicks <= 0 || snapshot.ObservedUtcTicks > DateTime.UtcNow.AddMinutes(5).Ticks || string.IsNullOrWhiteSpace(snapshot.Source)
                || snapshot.Source.Length > 1000 || (snapshot.Evidence?.Length ?? 0) > 4000
                || snapshot.Items == null || snapshot.Items.Count == 0 || snapshot.Items.Count > definition.Items.Count) throw new InvalidDataException("The source needs a valid observation time, source and item list.");
            var known = definition.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in snapshot.Items)
                if (item == null || !known.Contains(item.ItemId ?? "") || !ids.Add(item.ItemId) || item.Current < 0 || item.Current > 1000000 || !Enum.IsDefined(item.Availability) || (item.Note?.Length ?? 0) > 20000)
                    throw new InvalidDataException("The source contains invalid or duplicate items.");
            return snapshot;
        }
        static string Revision(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT item_id,reported_current,manual_current,availability,note,source,evidence,observed_utc,manual_utc FROM tracker_item_state WHERE tracker_id=$t AND account=$a ORDER BY item_id";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader(); var rows = new List<object[]>();
            while (r.Read()) { var row = new object[9]; for (int i = 0; i < row.Length; i++) row[i] = r.IsDBNull(i) ? null : r.GetValue(i); rows.Add(row); }
            r.Close();using var metadata=c.CreateCommand();metadata.Transaction=tx;metadata.CommandText="SELECT definition FROM tracker_definition WHERE id=$t";metadata.Parameters.AddWithValue("$t",tracker);
            return Digest(Json(rows)+"|"+(metadata.ExecuteScalar() as string??""));
        }
        internal TrackerRefreshPlan Preview(string json, string tracker, string account, string adapter, string version, string fingerprint, string bindingId = null, string attempt = null)
        {
            var snapshot = ValidateObservation(json, tracker, account); var definition = Definition(tracker); account = RequireAccount(definition, account);
            if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 256) throw new InvalidDataException("The source did not provide a valid fingerprint.");
            using var c = Open(); using var tx = c.BeginTransaction(); var rows = new List<TrackerReconcileRow>(); var names = definition.Items.ToDictionary(i => i.Id, i => i.Name);
            foreach (var item in snapshot.Items)
            {
                var prior = ReadRecord(c, tx, tracker, account, item.ItemId);
                var addNote = CanAddSourceNote(c,tx,tracker,account,item.ItemId,prior,item.Note);
                var changed = prior.Reported != item.Current || prior.Availability != (int)item.Availability || addNote;
                rows.Add(new TrackerReconcileRow { ItemId = item.ItemId, Name = names[item.ItemId], Previous = prior.Reported, Incoming = item.Current, Manual = prior.Manual,
                    Availability = item.Availability, IncomingNote = item.Note, AddNote = addNote, Changed = changed, Stale = prior.ObservedUtc > snapshot.ObservedUtcTicks || prior.ObservedUtc == snapshot.ObservedUtcTicks && changed });
            }
            return new TrackerRefreshPlan { Json = json, TrackerId = tracker, Account = account, AdapterId = adapter, AdapterVersion = version, Fingerprint = fingerprint,
                Rows = rows, Baseline = Revision(c, tx, tracker, account), BindingId = bindingId, AttemptId = attempt, ObservedUtc = snapshot.ObservedUtcTicks, Source = snapshot.Source, Evidence = snapshot.Evidence };
        }

        internal int ApplyRefresh(TrackerRefreshPlan plan, bool allowOlder, IEnumerable<string> useSourceItems, CancellationToken token, string automationId = null)
        {
            var snapshot = ValidateObservation(plan.Json, plan.TrackerId, plan.Account);
            var accept = (useSourceItems ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
            if (accept.Any(id => !snapshot.Items.Any(i => i.ItemId == id))) throw new InvalidDataException("An accepted item is not in this observation.");
            using var c = Open(); using var tx = c.BeginTransaction(); token.ThrowIfCancellationRequested();
            if (automationId != null) RequireAutomation(c, tx, plan.TrackerId, plan.Account, plan.BindingId, automationId);
            if (Revision(c, tx, plan.TrackerId, plan.Account) != plan.Baseline) throw new InvalidOperationException("Tracker progress changed after this preview. Refresh again to review current differences.");
            var source = ReadSource(c, tx, plan.TrackerId, plan.Account);
            if (plan.BindingId != null && (source.Binding?.Id != plan.BindingId || source.Status?.AttemptId != plan.AttemptId || source.Status.Outcome != TrackerRefreshOutcome.AwaitingReview))
                throw new InvalidOperationException("The source changed or a newer refresh started. Refresh again.");
            var changes = new List<TrackerHistoryChange>(); int applied = 0, skipped = 0;
            foreach (var item in snapshot.Items)
            {
                token.ThrowIfCancellationRequested(); var before = ReadRecord(c, tx, plan.TrackerId, plan.Account, item.ItemId);
                bool addNote = CanAddSourceNote(c,tx,plan.TrackerId,plan.Account,item.ItemId,before,item.Note);
                bool stale = before.ObservedUtc > snapshot.ObservedUtcTicks || before.ObservedUtc == snapshot.ObservedUtcTicks
                    && (before.Reported != item.Current || before.Availability != (int)item.Availability || addNote);
                if (stale && !allowOlder) { skipped++; continue; }
                var fields = TrackerChangeFields.Reported;
                if (addNote) fields |= TrackerChangeFields.Note;
                if (accept.Contains(item.ItemId) && before.Manual.HasValue) fields |= TrackerChangeFields.Manual;
                var after = new TrackerStoredRecord { Reported = item.Current, Availability = (int)item.Availability, Source = snapshot.Source.Trim(), Evidence = snapshot.Evidence ?? "", ObservedUtc = snapshot.ObservedUtcTicks };
                if (fields.HasFlag(TrackerChangeFields.Note)) after.Note = item.Note;
                if (fields.HasFlag(TrackerChangeFields.Manual)) after.ManualUtc = DateTime.UtcNow.Ticks;
                // Equal observations can still resolve a deliberately selected manual conflict.
                if (snapshot.ObservedUtcTicks == before.ObservedUtc && !stale)
                { after.Source = before.Source; after.Evidence = before.Evidence; }
                if (Json(before.Fields(fields)) == Json(after.Fields(fields))) continue;
                WriteFields(c, tx, plan.TrackerId, plan.Account, item.ItemId, after, fields); applied++;
                changes.Add(new TrackerHistoryChange { ItemId = item.ItemId, Fields = fields, Before = before.Fields(fields), After = after.Fields(fields) });
            }
            RecordBatch(c, tx, plan.TrackerId, plan.Account, plan.AdapterId == "json-import" ? "import" : "refresh", (plan.AdapterId == "json-import" ? "Import: " : "Refresh: ") + applied + " observations", plan.AdapterId, plan.AdapterVersion, plan.Fingerprint, snapshot.ObservedUtcTicks, changes);
            if (plan.BindingId != null)
            {
                source.Status.Outcome = skipped > 0 ? TrackerRefreshOutcome.Stale : applied > 0 ? TrackerRefreshOutcome.Success : TrackerRefreshOutcome.Unchanged;
                source.Status.Message = applied + " observations applied; " + skipped + " older/equal observations skipped. Manual edits and notes were preserved unless explicitly resolved.";
                if (skipped == 0) { source.Status.LastSuccessUtc = DateTime.UtcNow.Ticks; source.Status.Fingerprint = plan.Fingerprint; source.Status.SourceObservedUtc = snapshot.ObservedUtcTicks; }
                WriteStatus(c, tx, source.Binding, source.Status);
            }
            if (automationId == null && skipped == 0) ClearAutomationReview(c, tx, plan.TrackerId, plan.Account);
            token.ThrowIfCancellationRequested(); tx.Commit(); return applied;
        }
    }
}
