using Microsoft.Data.Sqlite;
using PixelVault.Companion.Export;

namespace PixelVault.Companion.Connector;

public static class DesktopAchievementGroups
{
    public static IReadOnlyList<AchievementGroupAck> Apply(string root,string privateData,Dictionary<string,string> accounts,IReadOnlyList<AchievementGroupChange> changes)
    {
        if(changes.Count>100)throw new InvalidDataException("Too many group edits.");
        var path=Path.Combine(root,"guides","pixelvault-guides.sqlite");
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());c.Open();
        using(var busy=c.CreateCommand()){busy.CommandText="PRAGMA busy_timeout=5000";busy.ExecuteNonQuery();}
        // A consistent backup must succeed before any desktop metadata can be changed.
        var backupRoot=Path.Combine(privateData,"backups","achievement-groups");Directory.CreateDirectory(backupRoot);
        using(var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(backupRoot,"web-groups-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+"-"+Guid.NewGuid().ToString("N")+".sqlite"),Pooling=false}.ToString())){backup.Open();c.BackupDatabase(backup);}
        foreach(var expired in Directory.EnumerateFiles(backupRoot,"web-groups-*.sqlite").OrderByDescending(File.GetLastWriteTimeUtc).Skip(12))File.Delete(expired);
        using var tx=c.BeginTransaction();var acknowledgements=new List<AchievementGroupAck>();
        foreach(var change in changes)
        {
            if(!accounts.ContainsKey(change.Provider)||change.Desired is null||change.Desired.Category is null||change.Desired.Dlc is null||change.Desired.Category.Length>120||change.Desired.Dlc.Length>120||change.Desired.Category.Any(char.IsControl)||change.Desired.Dlc.Any(char.IsControl)||change.Desired.SortOrder is <0 or >100000||!Guid.TryParse(change.Id,out _))throw new InvalidDataException("The group edit is outside the approved scope.");
            using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT achievement_id,group_name,dlc_name,group_sort_order FROM achievement_definition WHERE provider=$p AND provider_game_id=$g AND provider_achievement_id=$id AND is_active=1";
            cmd.Parameters.AddWithValue("$p",change.Provider);cmd.Parameters.AddWithValue("$g",change.GameId);cmd.Parameters.AddWithValue("$id",change.AchievementId);
            long nativeId;AchievementGroups current;
            using(var row=cmd.ExecuteReader()){if(!row.Read())continue;nativeId=row.GetInt64(0);current=AchievementSnapshotExporter.Groups(row.GetString(1),row.GetString(2),row.GetInt32(3));}
            if(current==change.Desired){acknowledgements.Add(new(change.Provider,change.GameId,change.AchievementId,change.Id,"applied",current));continue;}
            if(current!=change.Baseline){acknowledgements.Add(new(change.Provider,change.GameId,change.AchievementId,change.Id,"conflict",current));continue;}
            cmd.Parameters.Clear();cmd.CommandText="UPDATE achievement_definition SET group_name=$category,dlc_name=$dlc,group_sort_order=$sort WHERE achievement_id=$id; INSERT OR IGNORE INTO achievement_category(name) SELECT $category WHERE $category<>''; INSERT OR IGNORE INTO achievement_dlc(provider,provider_game_id,name) SELECT $p,$g,$dlc WHERE $dlc<>''";
            cmd.Parameters.AddWithValue("$id",nativeId);cmd.Parameters.AddWithValue("$category",change.Desired.Category);cmd.Parameters.AddWithValue("$dlc",change.Desired.Dlc);cmd.Parameters.AddWithValue("$sort",change.Desired.SortOrder);cmd.Parameters.AddWithValue("$p",change.Provider);cmd.Parameters.AddWithValue("$g",change.GameId);cmd.ExecuteNonQuery();
            acknowledgements.Add(new(change.Provider,change.GameId,change.AchievementId,change.Id,"applied",change.Desired));
        }
        tx.Commit();return acknowledgements;
    }
}
