using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    sealed record TrackerDeletionPreview(string Id, string Title, string GameTitle, int Accounts, int SavedItems, string Revision);

    sealed partial class AchievementTrackingStore
    {
        // This command belongs to the PC application; no Companion route exposes it.
        static readonly string[] DeletionTables = { "tracker_definition", "tracker_link", "tracker_title", "tracker_icon",
            "tracker_item_rule", "tracker_source", "tracker_automation", "tracker_automation_result", "tracker_history",
            "tracker_item_state", "tracker_import", "tracker_client_request", "tracker_catalogue_history", "tracker_web_sync" };

        static Dictionary<string,List<Dictionary<string,object>>> DeletionRows(SqliteConnection c, SqliteTransaction tx, string tracker)
        {
            var result=new Dictionary<string,List<Dictionary<string,object>>>();
            foreach(var table in DeletionTables)
            {
                using var check=c.CreateCommand();check.Transaction=tx;
                check.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
                check.Parameters.AddWithValue("$table",table);if(Convert.ToInt32(check.ExecuteScalar())==0)continue;
                using var command=c.CreateCommand();command.Transaction=tx;
                command.CommandText="SELECT * FROM "+table+" WHERE "+(table=="tracker_definition"?"id":"tracker_id")+"=$t ORDER BY rowid";
                command.Parameters.AddWithValue("$t",tracker);using var rows=command.ExecuteReader();
                var values=new List<Dictionary<string,object>>();
                while(rows.Read())
                {
                    var row=new Dictionary<string,object>();
                    for(int i=0;i<rows.FieldCount;i++)row[rows.GetName(i)]=rows.IsDBNull(i)?null:rows.GetValue(i);
                    values.Add(row);
                }
                result[table]=values;
            }
            return result;
        }

        internal TrackerDeletionPreview PrepareDeletion(string tracker)
        {
            using var c=Open();using var tx=c.BeginTransaction(deferred:true);
            _=CustomCatalogue(c,tx,tracker); // Reject bundled catalogues and missing trackers.
            var definition=ReadDefinitions(c,tx).Single(d=>d.Id==tracker);
            var rows=DeletionRows(c,tx,tracker);
            var states=rows["tracker_item_state"];
            var accounts=rows.Values.SelectMany(r=>r).Where(r=>r.ContainsKey("account"))
                .Select(r=>r["account"]?.ToString()).Where(a=>!string.IsNullOrEmpty(a)).Distinct().Count();
            var preview=new TrackerDeletionPreview(tracker,definition.Title,definition.GameTitle,accounts,states.Count,Digest(Json(rows)));
            tx.Commit();return preview;
        }

        internal string DeleteCustomTracker(TrackerDeletionPreview preview)
        {
            if(preview==null)throw new ArgumentNullException(nameof(preview));
            using var c=Open();using var tx=c.BeginTransaction();
            _=CustomCatalogue(c,tx,preview.Id);
            var rows=DeletionRows(c,tx,preview.Id);
            if(Digest(Json(rows))!=preview.Revision)
                throw new InvalidOperationException("This tracker changed while deletion was open. Review it and try again.");
            var directory=Path.Combine(Path.GetDirectoryName(DatabasePath),"deleted-trackers");Directory.CreateDirectory(directory);
            var backup=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".json");
            // A flushed, scoped recovery copy must exist before any database row is removed.
            using(var stream=new FileStream(backup,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                JsonSerializer.Serialize(stream,new{Version=1,DeletedUtc=DateTime.UtcNow,TrackerId=preview.Id,preview.Title,preview.GameTitle,Tables=rows},AchievementTrackingCatalog.Json);
                stream.Flush(flushToDisk:true);
            }
            foreach(var table in rows.Keys)
            {
                using var command=c.CreateCommand();command.Transaction=tx;
                command.CommandText="DELETE FROM "+table+" WHERE "+(table=="tracker_definition"?"id":"tracker_id")+"=$t";
                command.Parameters.AddWithValue("$t",preview.Id);command.ExecuteNonQuery();
            }
            // Provider achievements, Century awards, game saves and source files are deliberately outside this scope.
            tx.Commit();NotifyAutomationChanged();return backup;
        }
    }
}
