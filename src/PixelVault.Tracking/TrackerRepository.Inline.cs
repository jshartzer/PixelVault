using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    public partial class TrackerRepository
    {
        // Provenance follows the last non-undone reported-field change, not the
        // current binding or an author-supplied source/evidence string.
        protected static Dictionary<string, (string Adapter, TrackerStoredRecord After)> LatestInlineReports(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string item = null)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = @"SELECT json_extract(j.value,'$.ItemId'),h.adapter_id,json_extract(j.value,'$.After')
FROM tracker_history h,json_each(h.changes) j
WHERE h.tracker_id=$t AND h.account=$a AND h.undone_utc=0
AND (json_extract(j.value,'$.Fields') & 1)<>0
AND ($i IS NULL OR json_extract(j.value,'$.ItemId')=$i) ORDER BY h.id DESC";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$i", (object)item ?? DBNull.Value);
            using var r = cmd.ExecuteReader(); var result = new Dictionary<string, (string, TrackerStoredRecord)>(StringComparer.Ordinal);
            while (r.Read())
            {
                var id = r.GetString(0);
                if (!result.ContainsKey(id)) result.Add(id, (r.GetString(1), FromJson<TrackerStoredRecord>(r.GetString(2))));
            }
            return result;
        }
        public string InlineRevision(string tracker, string account)
        {
            account = AchievementTrackingCatalog.AccountKey(Definition(tracker).Provider, account);
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(id),0),COALESCE(MAX(undone_utc),0) FROM tracker_history WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader(); r.Read();
            return r.GetInt64(0) + ":" + r.GetInt64(1);
        }
        public HashSet<string> InlineGameCompletions(string tracker, string account, IReadOnlyList<AchievementTrackerState> states)
        {
            account = AchievementTrackingCatalog.AccountKey(Definition(tracker).Provider, account);
            var native = GameFileAdapterIds;
            using var c = Open(); var reports = LatestInlineReports(c, null, tracker, account);
            return states.Where(s => s.ReportedCurrent >= s.Item.Target && reports.TryGetValue(s.Item.Id, out var report)
                && native.Contains(report.Adapter) && report.After.Reported == s.ReportedCurrent
                && report.After.ObservedUtc == s.ObservedUtcTicks && report.After.Source == s.Source
                && report.After.Evidence == s.Evidence && report.After.Availability == (int)s.Availability)
                .Select(s => s.Item.Id).ToHashSet(StringComparer.Ordinal);
        }
        public void SetInlineCompletion(string tracker, string account, string item, bool complete)
        {
            var definition = Definition(tracker); account = RequireAccount(definition, account);
            var target = definition.Items.SingleOrDefault(i => i.Id == item && !i.Archived)
                ?? throw new InvalidDataException("This tracker item is no longer available.");
            var native = GameFileAdapterIds;
            using var c = Open(); using var tx = c.BeginTransaction();
            using (var catalogue = c.CreateCommand())
            {
                catalogue.Transaction = tx; catalogue.CommandText = "SELECT definition FROM tracker_definition WHERE id=$t";
                catalogue.Parameters.AddWithValue("$t", tracker);
                if (catalogue.ExecuteScalar() is string json)
                {
                    var current = FromJson<AchievementTrackerDefinition>(json);
                    var currentItem = current.Items.SingleOrDefault(i => i.Id == item && !i.Archived);
                    if (current.Provider != definition.Provider || current.GameId != definition.GameId || currentItem?.Target != target.Target)
                        throw new InvalidOperationException("The tracker catalogue changed. Reopen the item before changing progress.");
                }
            }
            var prior = ReadRecord(c, tx, tracker, account, item);
            var reports = LatestInlineReports(c, tx, tracker, account, item);
            bool gameComplete = prior.Reported >= target.Target && reports.TryGetValue(item, out var report)
                && native.Contains(report.Adapter) && Json(report.After.Fields(TrackerChangeFields.Reported)) == Json(prior.Fields(TrackerChangeFields.Reported));
            if (!complete && gameComplete)
                throw new InvalidOperationException("Game data confirms this item is complete. It cannot be unchecked here.");
            // Checking an existing game completion restores that observation rather
            // than creating an unnecessary override over future source updates.
            int? value = gameComplete ? null : complete ? target.Target : 0;
            if (prior.Manual == value) { tx.Commit(); return; }
            var after = new TrackerStoredRecord { Manual = value, ManualUtc = DateTime.UtcNow.Ticks };
            WriteFields(c, tx, tracker, account, item, after, TrackerChangeFields.Manual);
            RecordBatch(c, tx, tracker, account, "manual", "Progress: " + target.Name, "manual", "1", "", 0,
                new() { new() { ItemId = item, Fields = TrackerChangeFields.Manual, Before = prior.Fields(TrackerChangeFields.Manual), After = after.Fields(TrackerChangeFields.Manual) } });
            tx.Commit();
        }
    }
}
