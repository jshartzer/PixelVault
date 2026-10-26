using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVault.Companion;

public sealed record GameSectionOrder(IReadOnlyList<string> Sections,string Version);
public sealed record SaveGameSectionOrder(List<string> Sections,string ExpectedVersion);

/// <summary>The owner's layout is shared by all game profiles and devices; stale saves require review.</summary>
public sealed class GameSectionPreferences
{
    public static readonly string[] Default=["trackers","achievements","screenshots"];
    readonly string path;
    public GameSectionPreferences(CompanionOptions options)
    {
        path=Path.Combine(options.DataRoot,"companion.sqlite");
        using var connection=Open();using var command=connection.CreateCommand();
        command.CommandText="CREATE TABLE IF NOT EXISTS companion_preference (name TEXT PRIMARY KEY,value TEXT NOT NULL,revision INTEGER NOT NULL)";command.ExecuteNonQuery();
    }
    SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,DefaultTimeout=5}.ToString());c.Open();return c;}
    static string Version(string value,long revision)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision+"\n"+value)));
    static (string Value,long Revision) Read(SqliteConnection c,SqliteTransaction? transaction)
    {
        using var command=c.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT value,revision FROM companion_preference WHERE name='game-sections'";
        using var row=command.ExecuteReader();return row.Read()?(row.GetString(0),row.GetInt64(1)):(JsonSerializer.Serialize(Default),0);
    }
    static GameSectionOrder View((string Value,long Revision) row)
    {
        var saved=JsonSerializer.Deserialize<List<string>>(row.Value)??[];
        var sections=saved.Where(Default.Contains).Distinct(StringComparer.Ordinal).Concat(Default).Distinct(StringComparer.Ordinal).ToList();
        return new(sections,Version(row.Value,row.Revision));
    }
    public GameSectionOrder Load(){using var c=Open();return View(Read(c,null));}
    public GameSectionOrder Save(SaveGameSectionOrder request)
    {
        if(request.Sections is null||request.Sections.Count!=Default.Length||request.Sections.Distinct(StringComparer.Ordinal).Count()!=Default.Length
            ||request.Sections.Any(s=>!Default.Contains(s,StringComparer.Ordinal)))throw new InvalidDataException("Choose each available section exactly once.");
        using var c=Open();using var tx=c.BeginTransaction();var current=Read(c,tx);
        if(request.ExpectedVersion!=Version(current.Value,current.Revision))throw new InvalidOperationException("The section order changed on another device. Reload its saved order before trying again.");
        var value=JsonSerializer.Serialize(request.Sections);
        if(value==current.Value){tx.Commit();return View(current);}
        using var command=c.CreateCommand();command.Transaction=tx;
        command.CommandText="INSERT INTO companion_preference(name,value,revision) VALUES('game-sections',$value,$revision) ON CONFLICT(name) DO UPDATE SET value=$value,revision=$revision";
        command.Parameters.AddWithValue("$value",value);command.Parameters.AddWithValue("$revision",current.Revision+1);command.ExecuteNonQuery();tx.Commit();return View((value,current.Revision+1));
    }
}
