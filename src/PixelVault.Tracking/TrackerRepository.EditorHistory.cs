using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PixelVaultNative;

public partial class TrackerRepository
{
    // Parse each history batch once. json_each + json_extract used to expand the same large
    // history twice, even though only the last report and two version counters are required.
    static (Dictionary<(string,bool),(long Id,long Undone)> Events,Dictionary<string,(string Adapter,TrackerStoredRecord After)> Reports)
        ReadEditorHistory(SqliteConnection c,SqliteTransaction tx,string tracker,string account)
    {
        var events=new Dictionary<(string,bool),(long,long)>();
        var reports=new Dictionary<string,(string,TrackerStoredRecord)>();
        using var command=c.CreateCommand();command.Transaction=tx;
        command.CommandText="SELECT id,undone_utc,adapter_id,changes FROM tracker_history WHERE tracker_id=$t AND account=$a ORDER BY id DESC";
        command.Parameters.AddWithValue("$t",tracker);command.Parameters.AddWithValue("$a",account);
        using var rows=command.ExecuteReader();
        while(rows.Read())
        {
            var id=rows.GetInt64(0);var undone=rows.GetInt64(1)>0;var adapter=rows.GetString(2);
            using var changes=JsonDocument.Parse(rows.GetString(3));
            foreach(var change in changes.RootElement.EnumerateArray())
            {
                var item=change.GetProperty("ItemId").GetString()!;
                var fields=(TrackerChangeFields)change.GetProperty("Fields").GetInt32();
                void Add(bool note){var key=(item,note);var previous=events.GetValueOrDefault(key);events[key]=(Math.Max(previous.Item1,id),previous.Item2+(undone?1:0));}
                if((fields&(TrackerChangeFields.Manual|TrackerChangeFields.Reported))!=0)Add(false);
                if((fields&TrackerChangeFields.Note)!=0)Add(true);
                if(!undone&&(fields&TrackerChangeFields.Reported)!=0&&!reports.ContainsKey(item))
                    reports[item]=(adapter,FromJson<TrackerStoredRecord>(change.GetProperty("After").GetRawText()));
            }
        }
        return(events,reports);
    }
}
