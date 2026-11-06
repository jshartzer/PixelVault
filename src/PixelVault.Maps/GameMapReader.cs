using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative;

/// <summary>Reads the existing Native map schema without creating stores or running migrations.</summary>
public sealed class GameMapReader
{
    readonly string root;
    readonly bool allowMissing;

    public GameMapReader(string dataRoot, bool allowMissing = true)
    {
        root = Path.Combine(Path.GetFullPath(dataRoot), "game-maps");
        this.allowMissing = allowMissing;
    }

    public List<GameMapDefinition> List(string provider, string gameId, string achievement = null, string tracker = null)
        => Read(provider, gameId, null, achievement, tracker);

    public GameMapDefinition Find(string provider, string gameId, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Read(provider, gameId, id, null, null).SingleOrDefault();
    }

    List<GameMapDefinition> Read(string provider, string gameId, string id, string achievement, string tracker)
    {
        var path = Path.Combine(root, "maps.sqlite");
        try
        {
            // The configured application root is trusted; map-store redirects are not.
            foreach (var item in new[] { root, path, path + "-wal", path + "-shm" })
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(item); }
                catch (FileNotFoundException) when (item.EndsWith("-wal") || item.EndsWith("-shm")) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The map store must not be a filesystem redirect.");
            }
        }
        catch (FileNotFoundException) when (allowMissing) { return new(); }
        catch (DirectoryNotFoundException) when (allowMissing) { return new(); }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,definition,revision FROM game_map WHERE provider=$p AND game_id=$g"
            + (id is null ? " ORDER BY id" : " AND id=$id");
        command.Parameters.AddWithValue("$p", provider ?? "");
        command.Parameters.AddWithValue("$g", gameId ?? "");
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        var result = new List<GameMapDefinition>();
        while (reader.Read())
        {
            var map = JsonSerializer.Deserialize<GameMapDefinition>(reader.GetString(1));
            GameMapValidation.Validate(map);
            if (map.Id != reader.GetString(0) || map.Provider != provider || map.GameId != gameId
                || map.PixelWidth <= 0 || map.PixelHeight <= 0 || (long)map.PixelWidth * map.PixelHeight > 100_000_000
                || reader.GetInt64(2) <= 0)
                throw new InvalidDataException("The saved map definition is invalid.");
            // SQL revision is authoritative: older payloads contain the revision before the write.
            map.Revision = reader.GetInt64(2);
            if ((!string.IsNullOrEmpty(achievement) && !map.AchievementIds.Contains(achievement))
                || (!string.IsNullOrEmpty(tracker) && !map.TrackerIds.Contains(tracker))) continue;
            result.Add(map);
        }
        return result.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
