using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string BuildLibraryFolderInventoryStamp(string root)
        {
            long latestDirTicks = 0;
            int folderCount = 0;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                folderCount++;
                var dirTicks = Directory.GetLastWriteTimeUtc(dir).Ticks;
                if (dirTicks > latestDirTicks) latestDirTicks = dirTicks;
            }

            var metadataPath = IndexDatabasePath(root);
            if (!File.Exists(metadataPath)) metadataPath = LibraryMetadataIndexPath(root);
            long metadataTicks = File.Exists(metadataPath) ? File.GetLastWriteTimeUtc(metadataPath).Ticks : 0;
            long metadataLength = File.Exists(metadataPath) ? new FileInfo(metadataPath).Length : 0;
            return folderCount + "|" + latestDirTicks + "|" + metadataTicks + "|" + metadataLength;
        }

        string LibraryFolderCachePath(string root)
        {
            return Path.Combine(cacheRoot, "library-folders-" + SafeCacheName(root) + ".cache");
        }

        string IndexDatabasePath(string root)
        {
            return Path.Combine(cacheRoot, "pixelvault-index-" + SafeCacheName(root) + ".sqlite");
        }

        string GameIndexPath(string root)
        {
            return Path.Combine(cacheRoot, "game-index-" + SafeCacheName(root) + ".cache");
        }

        string LibraryMetadataIndexPath(string root)
        {
            return Path.Combine(cacheRoot, "library-metadata-index-" + SafeCacheName(root) + ".cache");
        }

        SqliteConnection OpenIndexDatabase(string root)
        {
            Directory.CreateDirectory(cacheRoot);
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = IndexDatabasePath(root),
                Mode = SqliteOpenMode.ReadWriteCreate
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            InitializeIndexDatabase(connection);
            EnsureLegacyIndexMigration(root, connection);
            return connection;
        }

        void InitializeIndexDatabase(SqliteConnection connection)
        {
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=OFF;";
                pragma.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS game_index (
    root TEXT NOT NULL,
    game_id TEXT NOT NULL,
    folder_path TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL DEFAULT '',
    platform_label TEXT NOT NULL DEFAULT '',
    steam_app_id TEXT NOT NULL DEFAULT '',
    steam_grid_db_id TEXT NOT NULL DEFAULT '',
    file_count INTEGER NOT NULL DEFAULT 0,
    preview_image_path TEXT NOT NULL DEFAULT '',
    file_paths TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (root, game_id)
);
CREATE INDEX IF NOT EXISTS idx_game_index_root_identity ON game_index(root, name, platform_label);
CREATE TABLE IF NOT EXISTS photo_index (
    root TEXT NOT NULL,
    file_path TEXT NOT NULL,
    stamp TEXT NOT NULL DEFAULT '',
    game_id TEXT NOT NULL DEFAULT '',
    console_label TEXT NOT NULL DEFAULT '',
    tag_text TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (root, file_path)
);
CREATE INDEX IF NOT EXISTS idx_photo_index_root_game ON photo_index(root, game_id);
";
                command.ExecuteNonQuery();
            }
        }

        long CountIndexDatabaseRows(SqliteConnection connection, string tableName, string root)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM " + tableName + " WHERE root = $root;";
                command.Parameters.AddWithValue("$root", root ?? string.Empty);
                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0L : Convert.ToInt64(result);
            }
        }

        List<GameIndexEditorRow> ReadSavedGameIndexRowsFromDatabase(string root, SqliteConnection connection)
        {
            var rows = new List<GameIndexEditorRow>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT game_id, folder_path, name, platform_label, steam_app_id, steam_grid_db_id, file_count, preview_image_path, file_paths
FROM game_index
WHERE root = $root
ORDER BY name COLLATE NOCASE, platform_label COLLATE NOCASE, game_id COLLATE NOCASE;";
                command.Parameters.AddWithValue("$root", root ?? string.Empty);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var filePathsText = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
                        rows.Add(new GameIndexEditorRow
                        {
                            GameId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                            FolderPath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            PlatformLabel = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            SteamAppId = DisplayExternalIdValue(reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
                            SteamGridDbId = DisplayExternalIdValue(reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
                            SuppressSteamAppIdAutoResolve = IsClearedExternalIdValue(reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
                            SuppressSteamGridDbIdAutoResolve = IsClearedExternalIdValue(reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
                            FileCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                            PreviewImagePath = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                            FilePaths = string.IsNullOrWhiteSpace(filePathsText)
                                ? new string[0]
                                : filePathsText.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                        });
                    }
                }
            }

            return rows;
        }

        void WriteSavedGameIndexRowsToDatabase(string root, IEnumerable<GameIndexEditorRow> rows, SqliteConnection connection)
        {
            using (var transaction = connection.BeginTransaction())
            {
                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM game_index WHERE root = $root;";
                    delete.Parameters.AddWithValue("$root", root ?? string.Empty);
                    delete.ExecuteNonQuery();
                }

                foreach (var row in (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null))
                {
                    using (var insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText = @"
INSERT INTO game_index (root, game_id, folder_path, name, platform_label, steam_app_id, steam_grid_db_id, file_count, preview_image_path, file_paths)
VALUES ($root, $gameId, $folderPath, $name, $platformLabel, $steamAppId, $steamGridDbId, $fileCount, $previewImagePath, $filePaths);";
                        insert.Parameters.AddWithValue("$root", root ?? string.Empty);
                        insert.Parameters.AddWithValue("$gameId", NormalizeGameId(row.GameId));
                        insert.Parameters.AddWithValue("$folderPath", row.FolderPath ?? string.Empty);
                        insert.Parameters.AddWithValue("$name", row.Name ?? string.Empty);
                        insert.Parameters.AddWithValue("$platformLabel", row.PlatformLabel ?? string.Empty);
                        insert.Parameters.AddWithValue("$steamAppId", SerializeExternalIdValue(row.SteamAppId, row.SuppressSteamAppIdAutoResolve));
                        insert.Parameters.AddWithValue("$steamGridDbId", SerializeExternalIdValue(row.SteamGridDbId, row.SuppressSteamGridDbIdAutoResolve));
                        insert.Parameters.AddWithValue("$fileCount", Math.Max(0, row.FileCount));
                        insert.Parameters.AddWithValue("$previewImagePath", row.PreviewImagePath ?? string.Empty);
                        insert.Parameters.AddWithValue("$filePaths", string.Join("|", (row.FilePaths ?? new string[0]).Where(File.Exists)));
                        insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        Dictionary<string, LibraryMetadataIndexEntry> ReadLibraryMetadataIndexFromDatabase(string root, SqliteConnection connection, Dictionary<string, string> aliasMap)
        {
            var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT file_path, stamp, game_id, console_label, tag_text
FROM photo_index
WHERE root = $root
ORDER BY file_path COLLATE NOCASE;";
                command.Parameters.AddWithValue("$root", root ?? string.Empty);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var filePath = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) continue;

                        var currentGameId = NormalizeGameId(reader.IsDBNull(2) ? string.Empty : reader.GetString(2));
                        string mappedGameId;
                        var tagText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                        index[filePath] = new LibraryMetadataIndexEntry
                        {
                            FilePath = filePath,
                            Stamp = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            GameId = !string.IsNullOrWhiteSpace(currentGameId) && aliasMap != null && aliasMap.TryGetValue(currentGameId, out mappedGameId) ? mappedGameId : currentGameId,
                            ConsoleLabel = DetermineConsoleLabelFromTags(ParseTagText(tagText)),
                            TagText = tagText
                        };
                    }
                }
            }

            return index;
        }

        void WriteLibraryMetadataIndexToDatabase(string root, Dictionary<string, LibraryMetadataIndexEntry> index, SqliteConnection connection)
        {
            var savedEntries = (index ?? new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase))
                .Values
                .Where(v => v != null && !string.IsNullOrWhiteSpace(v.FilePath) && File.Exists(v.FilePath))
                .OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using (var transaction = connection.BeginTransaction())
            {
                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM photo_index WHERE root = $root;";
                    delete.Parameters.AddWithValue("$root", root ?? string.Empty);
                    delete.ExecuteNonQuery();
                }

                foreach (var entry in savedEntries)
                {
                    using (var insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText = @"
INSERT INTO photo_index (root, file_path, stamp, game_id, console_label, tag_text)
VALUES ($root, $filePath, $stamp, $gameId, $consoleLabel, $tagText);";
                        insert.Parameters.AddWithValue("$root", root ?? string.Empty);
                        insert.Parameters.AddWithValue("$filePath", entry.FilePath ?? string.Empty);
                        insert.Parameters.AddWithValue("$stamp", entry.Stamp ?? string.Empty);
                        insert.Parameters.AddWithValue("$gameId", NormalizeGameId(entry.GameId));
                        insert.Parameters.AddWithValue("$consoleLabel", entry.ConsoleLabel ?? string.Empty);
                        insert.Parameters.AddWithValue("$tagText", entry.TagText ?? string.Empty);
                        insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        Dictionary<string, LibraryMetadataIndexEntry> ReadLegacyLibraryMetadataIndexFile(string root, Dictionary<string, string> aliasMap)
        {
            var legacyIndex = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            var path = LibraryMetadataIndexPath(root);
            if (!File.Exists(path)) return legacyIndex;

            foreach (var line in File.ReadAllLines(path).Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 5)
                {
                    if (!File.Exists(parts[0])) continue;

                    var tagText = parts[4] ?? string.Empty;
                    string mappedGameId;
                    var currentGameId = NormalizeGameId(parts[2]);
                    legacyIndex[parts[0]] = new LibraryMetadataIndexEntry
                    {
                        FilePath = parts[0],
                        Stamp = parts[1],
                        GameId = !string.IsNullOrWhiteSpace(currentGameId) && aliasMap != null && aliasMap.TryGetValue(currentGameId, out mappedGameId) ? mappedGameId : parts[2],
                        ConsoleLabel = DetermineConsoleLabelFromTags(ParseTagText(tagText)),
                        TagText = tagText
                    };
                }
                else if (parts.Length >= 4)
                {
                    if (!File.Exists(parts[0])) continue;

                    var tagText = parts[3] ?? string.Empty;
                    legacyIndex[parts[0]] = new LibraryMetadataIndexEntry
                    {
                        FilePath = parts[0],
                        Stamp = parts[1],
                        GameId = string.Empty,
                        ConsoleLabel = DetermineConsoleLabelFromTags(ParseTagText(tagText)),
                        TagText = tagText
                    };
                }
            }

            return legacyIndex;
        }

        void EnsureLegacyIndexMigration(string root, SqliteConnection connection)
        {
            if (string.IsNullOrWhiteSpace(root)) return;

            var gameRowsInDb = CountIndexDatabaseRows(connection, "game_index", root);
            var photoRowsInDb = CountIndexDatabaseRows(connection, "photo_index", root);

            List<GameIndexEditorRow> rawLegacyGameRows = null;
            List<GameIndexEditorRow> normalizedLegacyGameRows = null;
            Dictionary<string, string> aliasMap = null;

            if (gameRowsInDb == 0)
            {
                rawLegacyGameRows = ReadSavedGameIndexRowsFile(root);
                normalizedLegacyGameRows = MergeGameIndexRows(rawLegacyGameRows);
                WriteSavedGameIndexRowsToDatabase(root, normalizedLegacyGameRows, connection);
                aliasMap = BuildGameIdAliasMap(rawLegacyGameRows, normalizedLegacyGameRows);
                if (HasGameIdAliasChanges(aliasMap))
                {
                    RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
                }
            }

            if (photoRowsInDb == 0)
            {
                if (aliasMap == null)
                {
                    var currentRows = ReadSavedGameIndexRowsFromDatabase(root, connection);
                    rawLegacyGameRows = ReadSavedGameIndexRowsFile(root);
                    if (rawLegacyGameRows.Count > 0 && currentRows.Count > 0)
                    {
                        aliasMap = BuildGameIdAliasMap(rawLegacyGameRows, currentRows);
                    }
                    else if (currentRows.Count > 0)
                    {
                        aliasMap = BuildGameIdAliasMap(currentRows, currentRows);
                    }
                    else
                    {
                        normalizedLegacyGameRows = MergeGameIndexRows(rawLegacyGameRows);
                        aliasMap = BuildGameIdAliasMap(rawLegacyGameRows, normalizedLegacyGameRows);
                    }
                }

                var legacyIndex = ReadLegacyLibraryMetadataIndexFile(root, aliasMap);
                WriteLibraryMetadataIndexToDatabase(root, legacyIndex, connection);
            }
        }
    }
}
