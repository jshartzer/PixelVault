using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    internal static class Xbox360LibraryMigration
    {
        internal sealed record Change(string GameId, string Name, string XboxTitleId);
        internal static XboxAchievementService.XboxTitle Match(string name, string platform,
            IEnumerable<XboxAchievementService.XboxTitle> titles, string linkedId = null, string platforms = null)
        {
            if (!string.Equals(platform, "Xbox", StringComparison.OrdinalIgnoreCase)) return null;
            var match = PixelVault.Tracking.GameTitleIdentity.MatchXbox(name, titles, t => t.Id, t => t.Name, t => t.Legacy,
                PixelVault.Tracking.GameTitleIdentity.XboxLegacyHint(platform, platforms), linkedId);
            return match?.Legacy == true ? match : null;
        }
        internal static List<Change> ApplyDatabase(string database, string libraryRoot,
            IReadOnlyList<XboxAchievementService.XboxTitle> titles,
            IReadOnlyDictionary<string, GameProfilePlatformStore.Preference> preferences, string backupDirectory)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            connection.Open();
            var changes = new List<Change>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('game_index') WHERE name='igdb_platforms'";
                var hasPlatforms = Convert.ToInt32(command.ExecuteScalar()) > 0;
                command.CommandText = "SELECT game_id,name,platform_label," + (hasPlatforms ? "igdb_platforms" : "''") + " FROM game_index WHERE root=$root COLLATE NOCASE AND platform_label='Xbox' COLLATE NOCASE";
                command.Parameters.AddWithValue("$root", libraryRoot);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetString(0); var name = reader.GetString(1);
                    preferences.TryGetValue("id:" + id, out var preference);
                    var match = Match(name, reader.GetString(2), titles, preference?.XboxTitleId, reader.IsDBNull(3) ? "" : reader.GetString(3));
                    if (match != null) changes.Add(new(id, name, match.Id));
                }
            }
            if (changes.Count == 0) return changes;
            Directory.CreateDirectory(backupDirectory);
            var backup = Path.Combine(backupDirectory, Path.GetFileNameWithoutExtension(database) + "-" + Guid.NewGuid().ToString("N") + ".sqlite");
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
            { destination.Open(); connection.BackupDatabase(destination); }
            using var transaction = connection.BeginTransaction();
            foreach (var change in changes)
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "UPDATE game_index SET platform_label='Xbox 360' WHERE root=$root COLLATE NOCASE AND game_id=$id AND name=$name AND platform_label='Xbox' COLLATE NOCASE";
                command.Parameters.AddWithValue("$root", libraryRoot); command.Parameters.AddWithValue("$id", change.GameId); command.Parameters.AddWithValue("$name", change.Name);
                if (command.ExecuteNonQuery() != 1) throw new IOException("The library changed during Xbox 360 classification. Retry after closing the metadata editor.");
            }
            transaction.Commit();
            return changes;
        }
        internal static int Run(string dataRoot)
        {
            var library = JsonSerializer.Deserialize<XboxAchievementService.Library>(File.ReadAllText(Path.Combine(dataRoot, "xbox-library.json")));
            var root = File.ReadLines(Path.Combine(dataRoot, "PixelVault.settings.ini"))
                .Select(line => line.Split('=', 2)).First(pair => pair.Length == 2 && pair[0].Trim().Equals("library", StringComparison.OrdinalIgnoreCase))[1].Trim();
            if (!Path.IsPathFullyQualified(root) || library?.Titles == null) throw new InvalidDataException("A canonical library and Xbox catalogue are required.");
            var preferencesPath = Path.Combine(dataRoot, "game-profile-platforms.json");
            var preferences = File.Exists(preferencesPath) ? JsonSerializer.Deserialize<Dictionary<string, GameProfilePlatformStore.Preference>>(File.ReadAllText(preferencesPath)) : new();
            var backup = Path.Combine(dataRoot, "backups", "xbox360-category", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            var changes = new List<Change>();
            foreach (var database in Directory.EnumerateFiles(Path.Combine(dataRoot, "cache"), "pixelvault-index-*.sqlite"))
                changes.AddRange(ApplyDatabase(database, root, library.Titles, preferences, backup));
            File.WriteAllText(Path.Combine(dataRoot, "xbox360-category-result.json"), JsonSerializer.Serialize(new { UpdatedUtc = DateTime.UtcNow, Changes = changes, BackupDirectory = changes.Count > 0 ? backup : null }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
    }
}
