#nullable enable
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelVaultNative;

public sealed record AchievementGroupAssignment(string Category,string Dlc,int SortOrder);
public sealed record AchievementGroupState(AchievementGroupAssignment Groups,string Version);

/// <summary>Shared transactional group commands. Only the Native/migration schema owner calls Initialize.</summary>
public static class AchievementGroupCommands
{
    public static void Initialize(SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS achievement_group_revision(achievement_id INTEGER PRIMARY KEY,revision TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS achievement_group_request(request_id TEXT PRIMARY KEY,fingerprint TEXT NOT NULL,achievement_id INTEGER NOT NULL);
        CREATE TRIGGER IF NOT EXISTS achievement_group_revision_update AFTER UPDATE OF group_name,dlc_name,group_sort_order ON achievement_definition
        WHEN OLD.group_name IS NOT NEW.group_name OR OLD.dlc_name IS NOT NEW.dlc_name OR OLD.group_sort_order IS NOT NEW.group_sort_order
        BEGIN
          INSERT INTO achievement_group_revision VALUES(NEW.achievement_id,lower(hex(randomblob(16))))
          ON CONFLICT(achievement_id) DO UPDATE SET revision=excluded.revision;
        END;
        """;cmd.ExecuteNonQuery();
    }
    public static string Version(AchievementGroupAssignment value,string revision)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{value=Normalize(value),revision}))));
    public static AchievementGroupAssignment Normalize(AchievementGroupAssignment g)
    {
        if(g is null||g.Category is null||g.Dlc is null||g.Category.Length>120||g.Dlc.Length>120||g.Category.Any(char.IsControl)||g.Dlc.Any(char.IsControl)||g.SortOrder is <0 or >100000)
            throw new InvalidDataException("Invalid achievement groups.");
        var category=g.Category.Trim();var dlc=g.Dlc.Trim();
        if(dlc.Equals("Base Game",StringComparison.OrdinalIgnoreCase))dlc="";
        if(category.StartsWith("DLC: ",StringComparison.OrdinalIgnoreCase)){if(dlc.Length==0)dlc=category[5..].Trim();category="";}
        if(category.Equals("DLC",StringComparison.OrdinalIgnoreCase)){if(dlc.Length==0)dlc="DLC";category="";}
        if(category.Equals("Base Game",StringComparison.OrdinalIgnoreCase)||category.Equals("Uncategorized",StringComparison.OrdinalIgnoreCase))category="";
        return new(category,dlc,g.SortOrder);
    }
    public static AchievementGroupState Read(SqliteConnection c,SqliteTransaction? tx,long id)
    {
        using var q=c.CreateCommand();q.Transaction=tx;q.CommandText="""
        SELECT d.group_name,d.dlc_name,d.group_sort_order,COALESCE(v.revision,'')
        FROM achievement_definition d LEFT JOIN achievement_group_revision v USING(achievement_id)
        WHERE d.achievement_id=$id AND d.is_active=1
        """;q.Parameters.AddWithValue("$id",id);using var r=q.ExecuteReader();
        if(!r.Read())throw new KeyNotFoundException("The achievement is unavailable.");
        var groups=Normalize(new(r.GetString(0),r.GetString(1),r.GetInt32(2)));return new(groups,Version(groups,r.GetString(3)));
    }
    public static AchievementGroupState Write(SqliteConnection c,SqliteTransaction tx,long id,AchievementGroupAssignment desired,string? expectedVersion=null,string? requestId=null)
    {
        desired=Normalize(desired);var current=Read(c,tx,id);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{id,desired,expectedVersion}))));
        using var q=c.CreateCommand();q.Transaction=tx;
        if(requestId is not null)
        {
            if(!Guid.TryParse(requestId,out var guid))throw new InvalidDataException("A group edit needs a valid request ID.");
            requestId=guid.ToString("N");q.CommandText="SELECT fingerprint FROM achievement_group_request WHERE request_id=$r";q.Parameters.AddWithValue("$r",requestId);
            if(q.ExecuteScalar() is string saved){if(saved!=fingerprint)throw new InvalidOperationException("This request ID was already used.");return current;}
        }
        if(expectedVersion is not null&&expectedVersion!=current.Version)throw new InvalidOperationException("Achievement groups changed. Reload and review the saved assignment.");
        if(desired!=current.Groups)
        {
            q.CommandText="""
            UPDATE achievement_definition SET group_name=$c,dlc_name=$d,group_sort_order=$s WHERE achievement_id=$id;
            INSERT OR IGNORE INTO achievement_category(name) SELECT $c WHERE $c<>'';
            INSERT OR IGNORE INTO achievement_dlc(provider,provider_game_id,name)
              SELECT provider,provider_game_id,$d FROM achievement_definition WHERE achievement_id=$id AND $d<>'';
            """;q.Parameters.AddWithValue("$id",id);q.Parameters.AddWithValue("$c",desired.Category);q.Parameters.AddWithValue("$d",desired.Dlc);q.Parameters.AddWithValue("$s",desired.SortOrder);q.ExecuteNonQuery();
        }
        if(requestId is not null)
        {
            q.Parameters.Clear();q.CommandText="INSERT INTO achievement_group_request VALUES($r,$f,$id)";q.Parameters.AddWithValue("$r",requestId);q.Parameters.AddWithValue("$f",fingerprint);q.Parameters.AddWithValue("$id",id);q.ExecuteNonQuery();
        }
        return Read(c,tx,id);
    }
}
