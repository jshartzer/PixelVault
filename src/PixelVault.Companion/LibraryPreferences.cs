using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVault.Companion;

public sealed record LibraryLayout(string Sort,IReadOnlyList<string> Pins);
public sealed record SaveLibrarySort(string Sort);
public sealed record SaveLibraryPin(string GameId,bool Pinned);

public sealed class LibraryPreferences
{
    static readonly string[] Sorts=["screenshot-newest","screenshot-oldest","title-az","title-za","last-played","playtime"];
    readonly string path;
    public LibraryPreferences(CompanionOptions options)
    {
        path=Path.Combine(options.DataRoot,"companion.sqlite");
        using var c=Open();using var cmd=c.CreateCommand();
        cmd.CommandText="CREATE TABLE IF NOT EXISTS companion_preference (name TEXT PRIMARY KEY,value TEXT NOT NULL,revision INTEGER NOT NULL)";cmd.ExecuteNonQuery();
    }
    SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();return c;}
    static LibraryLayout Read(SqliteConnection c,SqliteTransaction? tx)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT value FROM companion_preference WHERE name='library'";
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<LibraryLayout>(json)! : new("screenshot-newest",[]);
    }
    static LibraryLayout Resolve(LibraryLayout layout,GameLibrary? library)
    {
        if(library is null)return layout;
        var aliases=library.GroupAliases();
        return layout with{Pins=layout.Pins.Select(id=>aliases.GetValueOrDefault(id)??id).Distinct(StringComparer.Ordinal).ToArray()};
    }
    public LibraryLayout Load(GameLibrary? library=null){using var c=Open();return Resolve(Read(c,null),library);}
    LibraryLayout Update(Func<LibraryLayout,LibraryLayout> change)
    {
        using var c=Open();using var tx=c.BeginTransaction();var value=change(Read(c,tx));using var cmd=c.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO companion_preference(name,value,revision) VALUES('library',$value,1) ON CONFLICT(name) DO UPDATE SET value=$value,revision=revision+1";
        cmd.Parameters.AddWithValue("$value",JsonSerializer.Serialize(value));cmd.ExecuteNonQuery();tx.Commit();return value;
    }
    public LibraryLayout Sort(string sort,GameLibrary? library=null)
    {
        if(!Sorts.Contains(sort,StringComparer.Ordinal))throw new InvalidDataException("Choose an available library sort.");
        return Resolve(Update(current=>current with{Sort=sort}),library);
    }
    public LibraryLayout Pin(string game,bool pinned,GameLibrary library)
    {
        var games=library.Games();
        var currentIds=games.Select(g=>g.GroupId).ToHashSet(StringComparer.Ordinal);
        if(!currentIds.Contains(game))game=games.FirstOrDefault(g=>g.TitleGroupId==game||g.EditionGroupId==game)?.GroupId??throw new KeyNotFoundException();
        var aliases=games.Where(g=>g.GroupId==game).SelectMany(g=>new[]{g.TitleGroupId,g.EditionGroupId}).Append(game).ToHashSet(StringComparer.Ordinal);
        // Each operation changes just one pin; simultaneous pins/sort changes preserve one another.
        return Resolve(Update(current=>current with{Pins=pinned?current.Pins.Append(game).Distinct(StringComparer.Ordinal).ToArray():current.Pins.Where(id=>!aliases.Contains(id)).ToArray()}),library);
    }
}
