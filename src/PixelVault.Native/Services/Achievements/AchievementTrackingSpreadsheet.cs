using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    sealed partial class AchievementTrackingStore
    {
        internal void AddSpreadsheetDefinition(AchievementTrackerDefinition definition, string account, IReadOnlyList<AchievementTrackerProgress> rows, long observed, string fingerprint)
        {
            if (rows == null || rows.Count == 0) { AddDefinition(definition); return; }
            AchievementTrackingCatalog.Validate(definition); account = RequireAccount(definition,account);
            var known = definition.Items.Select(i=>i.Id).ToHashSet(StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal);
            if (observed <= 0 || observed > DateTime.UtcNow.AddMinutes(5).Ticks || string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 256
                || rows.Any(r=>r == null || !known.Contains(r.ItemId ?? "") || !seen.Add(r.ItemId) || r.Current < 0 || r.Current > 1000000 || !Enum.IsDefined(r.Availability) || r.Note?.Length > 20000))
                throw new InvalidDataException("The initial spreadsheet observations are invalid.");
            if (Definitions().Any(d=>d.Id==definition.Id)) throw new InvalidDataException("This checklist already exists.");
            using var c=Open();using var tx=c.BeginTransaction();using(var cmd=c.CreateCommand())
            {
                cmd.Transaction=tx;cmd.CommandText="INSERT INTO tracker_definition(id,definition) VALUES($i,$d)";
                cmd.Parameters.AddWithValue("$i",definition.Id);cmd.Parameters.AddWithValue("$d",Json(definition));cmd.ExecuteNonQuery();
            }
            var changes=new List<TrackerHistoryChange>();
            foreach(var row in rows)
            {
                var after=new TrackerStoredRecord { Reported=row.Current,Availability=(int)row.Availability,ObservedUtc=observed,
                    Source="Spreadsheet initial import",Evidence="Values-only import; blank counts remain unknown; explicit zero remains zero. File SHA-256 "+fingerprint,Note=row.Note??"" };
                var fields=TrackerChangeFields.Reported;if(after.Note.Length>0)fields|=TrackerChangeFields.Note;
                WriteFields(c,tx,definition.Id,account,row.ItemId,after,fields);
                changes.Add(new TrackerHistoryChange { ItemId=row.ItemId,Fields=fields,Before=new TrackerStoredRecord().Fields(fields),After=after.Fields(fields) });
            }
            RecordBatch(c,tx,definition.Id,account,"import","Spreadsheet initial progress",SpreadsheetTrackerSource.AdapterId,"1",fingerprint,observed,changes);tx.Commit();
        }
    }
}
