using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    interface IIndexPersistenceService
    {
        void ApplyGameIdAliases(string root, Dictionary<string, string> aliasMap);
        Dictionary<string, string> BuildSavedGameIdAliasMap(string root);
        List<FilenameConventionRule> LoadFilenameConventions(string root);
        List<FilenameConventionSample> LoadFilenameConventionSamples(string root, int maxCount);
        List<GameIndexEditorRow> LoadSavedGameIndexRows(string root);
        void RecordFilenameConventionSample(string root, string fileName, FilenameParseResult parseResult);
        void DeleteFilenameConventionSamples(string root, IEnumerable<long> sampleIds);
        void SaveFilenameConventions(string root, IEnumerable<FilenameConventionRule> rules);
        void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows);
        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexEntries(string root);
        void SaveLibraryMetadataIndexEntries(string root, Dictionary<string, LibraryMetadataIndexEntry> index);
    }

    sealed class IndexPersistenceServiceDependencies
    {
        public string CacheRoot;
        public Func<string, string> SafeCacheName;
        public Func<string, string> NormalizeGameId;
        public Func<string, string> NormalizeGameIndexName;
        public Func<string, string> NormalizeConsoleLabel;
        public Func<string, string> DisplayExternalIdValue;
        public Func<string, bool> IsClearedExternalIdValue;
        public Func<string, bool, string> SerializeExternalIdValue;
        public Func<IEnumerable<GameIndexEditorRow>, List<GameIndexEditorRow>> MergeGameIndexRows;
        public Func<IEnumerable<GameIndexEditorRow>, IEnumerable<GameIndexEditorRow>, Dictionary<string, string>> BuildGameIdAliasMap;
        public Func<Dictionary<string, string>, bool> HasGameIdAliasChanges;
        public Func<string, int> ParseInt;
        public Func<string, IEnumerable<string>> ParseTagText;
        public Func<IEnumerable<string>, string> DetermineConsoleLabelFromTags;
        public Action<string, Dictionary<string, string>> RewriteGameIdAliasesInLibraryFolderCacheFile;
        public Action<string, Dictionary<string, string>> ApplyGameIdAliasesToCachedMetadataIndex;
    }

    sealed class IndexPersistenceService : IIndexPersistenceService
    {
        readonly IndexPersistenceServiceDependencies dependencies;

        public IndexPersistenceService(IndexPersistenceServiceDependencies dependencies)
        {
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        string CacheRoot
        {
            get { return dependencies.CacheRoot ?? string.Empty; }
        }

        string SafeCacheName(string value)
        {
            return dependencies.SafeCacheName == null ? (value ?? string.Empty) : dependencies.SafeCacheName(value);
        }

        string NormalizeGameId(string value)
        {
            return dependencies.NormalizeGameId == null ? (value ?? string.Empty) : dependencies.NormalizeGameId(value);
        }

        string NormalizeGameIndexName(string value)
        {
            return dependencies.NormalizeGameIndexName == null ? (value ?? string.Empty).Trim() : dependencies.NormalizeGameIndexName(value ?? string.Empty);
        }

        string NormalizeConsoleLabel(string value)
        {
            return dependencies.NormalizeConsoleLabel == null ? (value ?? string.Empty).Trim() : dependencies.NormalizeConsoleLabel(value ?? string.Empty);
        }

        int ParseInt(string value)
        {
            return dependencies.ParseInt == null ? 0 : dependencies.ParseInt(value);
        }

        IEnumerable<string> ParseTagText(string value)
        {
            return dependencies.ParseTagText == null ? Enumerable.Empty<string>() : dependencies.ParseTagText(value ?? string.Empty) ?? Enumerable.Empty<string>();
        }

        string DetermineConsoleLabelFromTags(IEnumerable<string> tags)
        {
            return dependencies.DetermineConsoleLabelFromTags == null ? string.Empty : dependencies.DetermineConsoleLabelFromTags(tags ?? Enumerable.Empty<string>()) ?? string.Empty;
        }

        string DisplayExternalIdValue(string value)
        {
            return dependencies.DisplayExternalIdValue == null ? (value ?? string.Empty) : dependencies.DisplayExternalIdValue(value);
        }

        bool IsClearedExternalIdValue(string value)
        {
            return dependencies.IsClearedExternalIdValue != null && dependencies.IsClearedExternalIdValue(value);
        }

        string SerializeExternalIdValue(string value, bool suppressAutoResolve)
        {
            return dependencies.SerializeExternalIdValue == null ? (value ?? string.Empty) : dependencies.SerializeExternalIdValue(value, suppressAutoResolve);
        }

        List<GameIndexEditorRow> MergeGameIndexRows(IEnumerable<GameIndexEditorRow> rows)
        {
            return dependencies.MergeGameIndexRows == null ? new List<GameIndexEditorRow>() : dependencies.MergeGameIndexRows(rows ?? Enumerable.Empty<GameIndexEditorRow>()) ?? new List<GameIndexEditorRow>();
        }

        Dictionary<string, string> BuildGameIdAliasMap(IEnumerable<GameIndexEditorRow> sourceRows, IEnumerable<GameIndexEditorRow> normalizedRows)
        {
            return dependencies.BuildGameIdAliasMap == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : dependencies.BuildGameIdAliasMap(sourceRows ?? Enumerable.Empty<GameIndexEditorRow>(), normalizedRows ?? Enumerable.Empty<GameIndexEditorRow>())
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        bool HasGameIdAliasChanges(Dictionary<string, string> aliasMap)
        {
            return dependencies.HasGameIdAliasChanges != null && dependencies.HasGameIdAliasChanges(aliasMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        string IndexDatabasePath(string root)
        {
            return Path.Combine(CacheRoot, "pixelvault-index-" + SafeCacheName(root) + ".sqlite");
        }

        string GameIndexPath(string root)
        {
            return Path.Combine(CacheRoot, "game-index-" + SafeCacheName(root) + ".cache");
        }

        string LibraryMetadataIndexPath(string root)
        {
            return Path.Combine(CacheRoot, "library-metadata-index-" + SafeCacheName(root) + ".cache");
        }

        SqliteConnection OpenIndexDatabase(string root)
        {
            Directory.CreateDirectory(CacheRoot);
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
                // We keep foreign keys off here because the index tables are cache-style mirrors that are rebuilt
                // and migrated in broad replacement passes rather than maintained as a relational source of truth.
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
    capture_utc_ticks INTEGER NOT NULL DEFAULT 0,
    starred INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (root, file_path)
);
CREATE INDEX IF NOT EXISTS idx_photo_index_root_game ON photo_index(root, game_id);
CREATE TABLE IF NOT EXISTS filename_convention (
    root TEXT NOT NULL,
    convention_id TEXT NOT NULL,
    name TEXT NOT NULL DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1,
    priority INTEGER NOT NULL DEFAULT 0,
    pattern TEXT NOT NULL DEFAULT '',
    platform_label TEXT NOT NULL DEFAULT '',
    platform_tags_text TEXT NOT NULL DEFAULT '',
    steam_app_id_group TEXT NOT NULL DEFAULT '',
    title_group TEXT NOT NULL DEFAULT '',
    timestamp_group TEXT NOT NULL DEFAULT '',
    timestamp_format TEXT NOT NULL DEFAULT '',
    preserve_file_times INTEGER NOT NULL DEFAULT 0,
    routes_to_manual_when_missing_steam_app_id INTEGER NOT NULL DEFAULT 0,
    confidence_label TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (root, convention_id)
);
CREATE INDEX IF NOT EXISTS idx_filename_convention_root_priority ON filename_convention(root, priority DESC, convention_id COLLATE NOCASE);
CREATE TABLE IF NOT EXISTS filename_convention_sample (
    sample_id INTEGER PRIMARY KEY AUTOINCREMENT,
    root TEXT NOT NULL,
    file_name TEXT NOT NULL,
    suggested_platform_label TEXT NOT NULL DEFAULT '',
    suggested_convention_id TEXT NOT NULL DEFAULT '',
    first_seen_utc_ticks INTEGER NOT NULL DEFAULT 0,
    last_seen_utc_ticks INTEGER NOT NULL DEFAULT 0,
    occurrence_count INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_filename_convention_sample_root_file ON filename_convention_sample(root, file_name);
";
                command.ExecuteNonQuery();
            }

            EnsurePhotoIndexCaptureTicksColumn(connection);
            EnsurePhotoIndexStarredColumn(connection);
        }

        void EnsurePhotoIndexCaptureTicksColumn(SqliteConnection connection)
        {
            if (connection == null) return;
            if (DatabaseTableHasColumn(connection, "photo_index", "capture_utc_ticks")) return;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE photo_index ADD COLUMN capture_utc_ticks INTEGER NOT NULL DEFAULT 0;";
                command.ExecuteNonQuery();
            }
        }

        void EnsurePhotoIndexStarredColumn(SqliteConnection connection)
        {
            if (connection == null) return;
            if (DatabaseTableHasColumn(connection, "photo_index", "starred")) return;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE photo_index ADD COLUMN starred INTEGER NOT NULL DEFAULT 0;";
                command.ExecuteNonQuery();
            }
        }

        bool DatabaseTableHasColumn(SqliteConnection connection, string tableName, string columnName)
        {
            if (connection == null || string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName)) return false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + tableName + ");";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var currentName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        if (string.Equals(currentName, columnName, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            return false;
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

        List<GameIndexEditorRow> ReadAllSavedGameIndexRowsFromDatabase(SqliteConnection connection)
        {
            var rows = new List<GameIndexEditorRow>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT game_id, folder_path, name, platform_label, steam_app_id, steam_grid_db_id, file_count, preview_image_path, file_paths
FROM game_index
ORDER BY name COLLATE NOCASE, platform_label COLLATE NOCASE, game_id COLLATE NOCASE;";
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
SELECT file_path, stamp, game_id, console_label, tag_text, capture_utc_ticks, starred
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
                        var storedConsoleLabel = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        var tagText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                        index[filePath] = new LibraryMetadataIndexEntry
                        {
                            FilePath = filePath,
                            Stamp = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            GameId = !string.IsNullOrWhiteSpace(currentGameId) && aliasMap != null && aliasMap.TryGetValue(currentGameId, out mappedGameId) ? mappedGameId : currentGameId,
                            ConsoleLabel = string.IsNullOrWhiteSpace(storedConsoleLabel)
                                ? DetermineConsoleLabelFromTags(ParseTagText(tagText))
                                : storedConsoleLabel,
                            TagText = tagText,
                            CaptureUtcTicks = reader.IsDBNull(5) ? 0L : reader.GetInt64(5),
                            Starred = !reader.IsDBNull(6) && reader.GetInt64(6) != 0
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
INSERT INTO photo_index (root, file_path, stamp, game_id, console_label, tag_text, capture_utc_ticks, starred)
VALUES ($root, $filePath, $stamp, $gameId, $consoleLabel, $tagText, $captureUtcTicks, $starred);";
                        insert.Parameters.AddWithValue("$root", root ?? string.Empty);
                        insert.Parameters.AddWithValue("$filePath", entry.FilePath ?? string.Empty);
                        insert.Parameters.AddWithValue("$stamp", entry.Stamp ?? string.Empty);
                        insert.Parameters.AddWithValue("$gameId", NormalizeGameId(entry.GameId));
                        insert.Parameters.AddWithValue("$consoleLabel", entry.ConsoleLabel ?? string.Empty);
                        insert.Parameters.AddWithValue("$tagText", entry.TagText ?? string.Empty);
                        insert.Parameters.AddWithValue("$captureUtcTicks", entry.CaptureUtcTicks > 0 ? entry.CaptureUtcTicks : 0L);
                        insert.Parameters.AddWithValue("$starred", entry.Starred ? 1L : 0L);
                        insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        List<FilenameConventionRule> ReadFilenameConventionsFromDatabase(string root, SqliteConnection connection)
        {
            var rules = new List<FilenameConventionRule>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT convention_id, name, enabled, priority, pattern, platform_label, platform_tags_text, steam_app_id_group, title_group, timestamp_group, timestamp_format, preserve_file_times, routes_to_manual_when_missing_steam_app_id, confidence_label
FROM filename_convention
WHERE root = $root
ORDER BY priority DESC, convention_id COLLATE NOCASE;";
                command.Parameters.AddWithValue("$root", root ?? string.Empty);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rules.Add(new FilenameConventionRule
                        {
                            ConventionId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                            Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            Enabled = !reader.IsDBNull(2) && reader.GetInt32(2) != 0,
                            Priority = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                            Pattern = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                            PatternText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                            PlatformLabel = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                            PlatformTagsText = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                            SteamAppIdGroup = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                            TitleGroup = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                            TimestampGroup = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                            TimestampFormat = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                            PreserveFileTimes = !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
                            RoutesToManualWhenMissingSteamAppId = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
                            ConfidenceLabel = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                            IsBuiltIn = false
                        });
                    }
                }
            }
            return rules;
        }

        void WriteFilenameConventionsToDatabase(string root, IEnumerable<FilenameConventionRule> rules, SqliteConnection connection)
        {
            using (var transaction = connection.BeginTransaction())
            {
                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM filename_convention WHERE root = $root;";
                    delete.Parameters.AddWithValue("$root", root ?? string.Empty);
                    delete.ExecuteNonQuery();
                }

                foreach (var rule in (rules ?? Enumerable.Empty<FilenameConventionRule>()).Where(rule => rule != null))
                {
                    using (var insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText = @"
INSERT INTO filename_convention (root, convention_id, name, enabled, priority, pattern, platform_label, platform_tags_text, steam_app_id_group, title_group, timestamp_group, timestamp_format, preserve_file_times, routes_to_manual_when_missing_steam_app_id, confidence_label)
VALUES ($root, $conventionId, $name, $enabled, $priority, $pattern, $platformLabel, $platformTagsText, $steamAppIdGroup, $titleGroup, $timestampGroup, $timestampFormat, $preserveFileTimes, $routesToManualWhenMissingSteamAppId, $confidenceLabel);";
                        insert.Parameters.AddWithValue("$root", root ?? string.Empty);
                        insert.Parameters.AddWithValue("$conventionId", rule.ConventionId ?? string.Empty);
                        insert.Parameters.AddWithValue("$name", rule.Name ?? string.Empty);
                        insert.Parameters.AddWithValue("$enabled", rule.Enabled ? 1 : 0);
                        insert.Parameters.AddWithValue("$priority", rule.Priority);
                        insert.Parameters.AddWithValue("$pattern", string.IsNullOrWhiteSpace(rule.Pattern) ? (rule.PatternText ?? string.Empty) : rule.Pattern);
                        insert.Parameters.AddWithValue("$platformLabel", rule.PlatformLabel ?? string.Empty);
                        insert.Parameters.AddWithValue("$platformTagsText", rule.PlatformTagsText ?? string.Empty);
                        insert.Parameters.AddWithValue("$steamAppIdGroup", rule.SteamAppIdGroup ?? string.Empty);
                        insert.Parameters.AddWithValue("$titleGroup", rule.TitleGroup ?? string.Empty);
                        insert.Parameters.AddWithValue("$timestampGroup", rule.TimestampGroup ?? string.Empty);
                        insert.Parameters.AddWithValue("$timestampFormat", rule.TimestampFormat ?? string.Empty);
                        insert.Parameters.AddWithValue("$preserveFileTimes", rule.PreserveFileTimes ? 1 : 0);
                        insert.Parameters.AddWithValue("$routesToManualWhenMissingSteamAppId", rule.RoutesToManualWhenMissingSteamAppId ? 1 : 0);
                        insert.Parameters.AddWithValue("$confidenceLabel", rule.ConfidenceLabel ?? string.Empty);
                        insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        List<FilenameConventionSample> ReadFilenameConventionSamplesFromDatabase(string root, int maxCount, SqliteConnection connection)
        {
            var samples = new List<FilenameConventionSample>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT sample_id, file_name, suggested_platform_label, suggested_convention_id, first_seen_utc_ticks, last_seen_utc_ticks, occurrence_count
FROM filename_convention_sample
WHERE root = $root
ORDER BY last_seen_utc_ticks DESC, file_name COLLATE NOCASE
LIMIT $maxCount;";
                command.Parameters.AddWithValue("$root", root ?? string.Empty);
                command.Parameters.AddWithValue("$maxCount", Math.Max(1, maxCount));
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        samples.Add(new FilenameConventionSample
                        {
                            SampleId = reader.IsDBNull(0) ? 0L : reader.GetInt64(0),
                            FileName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                            SuggestedPlatformLabel = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                            SuggestedConventionId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                            FirstSeenUtcTicks = reader.IsDBNull(4) ? 0L : reader.GetInt64(4),
                            LastSeenUtcTicks = reader.IsDBNull(5) ? 0L : reader.GetInt64(5),
                            OccurrenceCount = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
                        });
                    }
                }
            }
            return samples;
        }

        void UpsertFilenameConventionSample(string root, string fileName, FilenameParseResult parseResult, SqliteConnection connection)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(fileName)) return;
            var nowUtcTicks = DateTime.UtcNow.Ticks;
            using (var update = connection.CreateCommand())
            {
                update.CommandText = @"
UPDATE filename_convention_sample
SET suggested_platform_label = $platformLabel,
    suggested_convention_id = $conventionId,
    last_seen_utc_ticks = $lastSeenUtcTicks,
    occurrence_count = occurrence_count + 1
WHERE root = $root AND file_name = $fileName;";
                update.Parameters.AddWithValue("$root", root ?? string.Empty);
                update.Parameters.AddWithValue("$fileName", fileName ?? string.Empty);
                update.Parameters.AddWithValue("$platformLabel", parseResult == null ? string.Empty : (parseResult.PlatformLabel ?? string.Empty));
                update.Parameters.AddWithValue("$conventionId", parseResult == null ? string.Empty : (parseResult.ConventionId ?? string.Empty));
                update.Parameters.AddWithValue("$lastSeenUtcTicks", nowUtcTicks);
                var affected = update.ExecuteNonQuery();
                if (affected > 0) return;
            }

            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = @"
INSERT INTO filename_convention_sample (root, file_name, suggested_platform_label, suggested_convention_id, first_seen_utc_ticks, last_seen_utc_ticks, occurrence_count)
VALUES ($root, $fileName, $platformLabel, $conventionId, $firstSeenUtcTicks, $lastSeenUtcTicks, 1);";
                insert.Parameters.AddWithValue("$root", root ?? string.Empty);
                insert.Parameters.AddWithValue("$fileName", fileName ?? string.Empty);
                insert.Parameters.AddWithValue("$platformLabel", parseResult == null ? string.Empty : (parseResult.PlatformLabel ?? string.Empty));
                insert.Parameters.AddWithValue("$conventionId", parseResult == null ? string.Empty : (parseResult.ConventionId ?? string.Empty));
                insert.Parameters.AddWithValue("$firstSeenUtcTicks", nowUtcTicks);
                insert.Parameters.AddWithValue("$lastSeenUtcTicks", nowUtcTicks);
                insert.ExecuteNonQuery();
            }
        }

        List<GameIndexEditorRow> ReadSavedGameIndexRowsFile(string root)
        {
            var path = GameIndexPath(root);
            if (!File.Exists(path)) return new List<GameIndexEditorRow>();
            var lines = File.ReadAllLines(path);
            if (lines.Length < 1) return new List<GameIndexEditorRow>();
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return new List<GameIndexEditorRow>();
            var list = new List<GameIndexEditorRow>();
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 9)
                {
                    list.Add(new GameIndexEditorRow
                    {
                        GameId = parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        PlatformLabel = parts[3],
                        SteamAppId = parts[4],
                        SteamGridDbId = parts[5],
                        FileCount = parts.Length > 6 ? ParseInt(parts[6]) : 0,
                        PreviewImagePath = parts.Length > 7 ? parts[7] : string.Empty,
                        FilePaths = parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8])
                            ? parts[8].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                            : new string[0]
                    });
                }
                else if (parts.Length >= 8)
                {
                    list.Add(new GameIndexEditorRow
                    {
                        GameId = parts[0],
                        FolderPath = parts[1],
                        Name = parts[2],
                        PlatformLabel = parts[3],
                        SteamAppId = parts[4],
                        SteamGridDbId = string.Empty,
                        FileCount = parts.Length > 5 ? ParseInt(parts[5]) : 0,
                        PreviewImagePath = parts.Length > 6 ? parts[6] : string.Empty,
                        FilePaths = parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7])
                            ? parts[7].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                            : new string[0]
                    });
                }
                else if (parts.Length >= 4)
                {
                    list.Add(new GameIndexEditorRow
                    {
                        GameId = string.Empty,
                        FolderPath = parts[0],
                        Name = parts[1],
                        PlatformLabel = parts[2],
                        SteamAppId = parts[3],
                        SteamGridDbId = string.Empty,
                        FileCount = parts.Length > 4 ? ParseInt(parts[4]) : 0,
                        PreviewImagePath = parts.Length > 5 ? parts[5] : string.Empty,
                        FilePaths = parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6])
                            ? parts[6].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToArray()
                            : new string[0]
                    });
                }
            }
            return list;
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
                        TagText = tagText,
                        CaptureUtcTicks = 0,
                        Starred = false
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
                        TagText = tagText,
                        CaptureUtcTicks = 0,
                        Starred = false
                    };
                }
            }

            return legacyIndex;
        }

        void RewriteGameIdAliasesInLibraryMetadataIndexStorage(string root, Dictionary<string, string> aliasMap, SqliteConnection connection)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            var pairs = aliasMap
                .Where(pair => !string.IsNullOrWhiteSpace(NormalizeGameId(pair.Key)) && !string.IsNullOrWhiteSpace(NormalizeGameId(pair.Value)))
                .ToList();
            if (pairs.Count == 0) return;

            using (var transaction = connection.BeginTransaction())
            {
                foreach (var pair in pairs)
                {
                    using (var update = connection.CreateCommand())
                    {
                        update.Transaction = transaction;
                        update.CommandText = @"
UPDATE photo_index
SET game_id = $newGameId
WHERE root = $root AND game_id = $oldGameId;";
                        update.Parameters.AddWithValue("$root", root ?? string.Empty);
                        update.Parameters.AddWithValue("$oldGameId", NormalizeGameId(pair.Key));
                        update.Parameters.AddWithValue("$newGameId", NormalizeGameId(pair.Value));
                        update.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
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
                if (HasGameIdAliasChanges(aliasMap) && dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile != null)
                {
                    dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
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

            BackfillMissingGameIndexExternalIdsFromLegacy(root, connection);
        }

        string BuildLegacyRowMatchKey(GameIndexEditorRow row)
        {
            if (row == null) return string.Empty;
            var folderPath = (row.FolderPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(folderPath)) return "folder|" + folderPath.ToLowerInvariant();
            var gameId = NormalizeGameId(row.GameId);
            if (!string.IsNullOrWhiteSpace(gameId)) return "game|" + gameId.ToLowerInvariant();
            var name = (row.Name ?? string.Empty).Trim().ToLowerInvariant();
            var platform = (row.PlatformLabel ?? string.Empty).Trim().ToLowerInvariant();
            return "name|" + name + "|" + platform;
        }

        string BuildCrossRootRowMatchKey(GameIndexEditorRow row)
        {
            if (row == null) return string.Empty;
            var name = NormalizeGameIndexName(row.Name);
            var platform = NormalizeConsoleLabel(row.PlatformLabel);
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            return name.ToLowerInvariant() + "|" + platform.ToLowerInvariant();
        }

        int CountResolvedExternalIds(GameIndexEditorRow row)
        {
            if (row == null) return 0;
            int count = 0;
            if (!string.IsNullOrWhiteSpace(row.SteamAppId)) count++;
            if (!string.IsNullOrWhiteSpace(row.SteamGridDbId)) count++;
            return count;
        }

        bool HasMissingExternalIds(GameIndexEditorRow row)
        {
            if (row == null) return false;
            return (!row.SuppressSteamAppIdAutoResolve && string.IsNullOrWhiteSpace(row.SteamAppId))
                || (!row.SuppressSteamGridDbIdAutoResolve && string.IsNullOrWhiteSpace(row.SteamGridDbId));
        }

        void BackfillMissingGameIndexExternalIdsFromOtherDatabases(string root, SqliteConnection connection)
        {
            var dbRows = ReadSavedGameIndexRowsFromDatabase(root, connection);
            if (dbRows.Count == 0 || !dbRows.Any(HasMissingExternalIds)) return;

            var currentDatabasePath = IndexDatabasePath(root);
            if (!Directory.Exists(CacheRoot)) return;

            var donorByIdentity = new Dictionary<string, GameIndexEditorRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(CacheRoot, "pixelvault-index-*.sqlite", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(path, currentDatabasePath, StringComparison.OrdinalIgnoreCase)) continue;

                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWriteCreate
                };

                using (var donorConnection = new SqliteConnection(builder.ToString()))
                {
                    donorConnection.Open();
                    if (CountIndexDatabaseRows(donorConnection, "game_index", string.Empty) == 0 && !DatabaseHasAnyRows(donorConnection, "game_index")) continue;
                    foreach (var donorRow in ReadAllSavedGameIndexRowsFromDatabase(donorConnection).Where(row => row != null && CountResolvedExternalIds(row) > 0))
                    {
                        var identity = BuildCrossRootRowMatchKey(donorRow);
                        if (string.IsNullOrWhiteSpace(identity)) continue;
                        GameIndexEditorRow existing;
                        if (!donorByIdentity.TryGetValue(identity, out existing)
                            || CountResolvedExternalIds(donorRow) > CountResolvedExternalIds(existing)
                            || (CountResolvedExternalIds(donorRow) == CountResolvedExternalIds(existing) && donorRow.FileCount > existing.FileCount))
                        {
                            donorByIdentity[identity] = donorRow;
                        }
                    }
                }
            }

            if (donorByIdentity.Count == 0) return;

            var changed = false;
            foreach (var row in dbRows)
            {
                if (row == null || !HasMissingExternalIds(row)) continue;
                GameIndexEditorRow donor;
                if (!donorByIdentity.TryGetValue(BuildCrossRootRowMatchKey(row), out donor) || donor == null) continue;

                if (string.IsNullOrWhiteSpace(row.SteamAppId) && !row.SuppressSteamAppIdAutoResolve)
                {
                    if (!string.IsNullOrWhiteSpace(donor.SteamAppId))
                    {
                        row.SteamAppId = donor.SteamAppId;
                        changed = true;
                    }
                    else if (donor.SuppressSteamAppIdAutoResolve)
                    {
                        row.SuppressSteamAppIdAutoResolve = true;
                        changed = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(row.SteamGridDbId) && !row.SuppressSteamGridDbIdAutoResolve)
                {
                    if (!string.IsNullOrWhiteSpace(donor.SteamGridDbId))
                    {
                        row.SteamGridDbId = donor.SteamGridDbId;
                        changed = true;
                    }
                    else if (donor.SuppressSteamGridDbIdAutoResolve)
                    {
                        row.SuppressSteamGridDbIdAutoResolve = true;
                        changed = true;
                    }
                }
            }

            if (!changed) return;

            WriteSavedGameIndexRowsToDatabase(root, dbRows, connection);
        }

        bool DatabaseHasAnyRows(SqliteConnection connection, string tableName)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(1) FROM " + tableName + ";";
                var result = command.ExecuteScalar();
                return result != null && result != DBNull.Value && Convert.ToInt64(result) > 0;
            }
        }

        void BackfillMissingGameIndexExternalIdsFromLegacy(string root, SqliteConnection connection)
        {
            var dbRows = ReadSavedGameIndexRowsFromDatabase(root, connection);
            if (dbRows.Count == 0) return;

            var legacyRows = ReadSavedGameIndexRowsFile(root);
            if (legacyRows.Count == 0) return;

            var legacyByKey = new Dictionary<string, GameIndexEditorRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var legacyRow in legacyRows.Where(row => row != null))
            {
                var key = BuildLegacyRowMatchKey(legacyRow);
                if (string.IsNullOrWhiteSpace(key)) continue;
                GameIndexEditorRow existing;
                if (legacyByKey.TryGetValue(key, out existing))
                {
                    if (string.IsNullOrWhiteSpace(existing.SteamAppId) && !string.IsNullOrWhiteSpace(legacyRow.SteamAppId)) existing.SteamAppId = legacyRow.SteamAppId;
                    if (string.IsNullOrWhiteSpace(existing.SteamGridDbId) && !string.IsNullOrWhiteSpace(legacyRow.SteamGridDbId)) existing.SteamGridDbId = legacyRow.SteamGridDbId;
                }
                else
                {
                    legacyByKey[key] = new GameIndexEditorRow
                    {
                        GameId = legacyRow.GameId,
                        FolderPath = legacyRow.FolderPath,
                        Name = legacyRow.Name,
                        PlatformLabel = legacyRow.PlatformLabel,
                        SteamAppId = legacyRow.SteamAppId,
                        SteamGridDbId = legacyRow.SteamGridDbId,
                        SuppressSteamAppIdAutoResolve = legacyRow.SuppressSteamAppIdAutoResolve,
                        SuppressSteamGridDbIdAutoResolve = legacyRow.SuppressSteamGridDbIdAutoResolve,
                        FileCount = legacyRow.FileCount,
                        PreviewImagePath = legacyRow.PreviewImagePath,
                        FilePaths = legacyRow.FilePaths
                    };
                }
            }

            var changed = false;
            foreach (var row in dbRows)
            {
                if (row == null) continue;
                if ((!string.IsNullOrWhiteSpace(row.SteamAppId) || row.SuppressSteamAppIdAutoResolve)
                    && (!string.IsNullOrWhiteSpace(row.SteamGridDbId) || row.SuppressSteamGridDbIdAutoResolve))
                {
                    continue;
                }

                GameIndexEditorRow legacy;
                if (!legacyByKey.TryGetValue(BuildLegacyRowMatchKey(row), out legacy) || legacy == null) continue;

                if (string.IsNullOrWhiteSpace(row.SteamAppId) && !row.SuppressSteamAppIdAutoResolve && !string.IsNullOrWhiteSpace(legacy.SteamAppId))
                {
                    row.SteamAppId = legacy.SteamAppId;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(row.SteamGridDbId) && !row.SuppressSteamGridDbIdAutoResolve && !string.IsNullOrWhiteSpace(legacy.SteamGridDbId))
                {
                    row.SteamGridDbId = legacy.SteamGridDbId;
                    changed = true;
                }
            }

            if (!changed) return;

            WriteSavedGameIndexRowsToDatabase(root, dbRows, connection);
        }

        public Dictionary<string, string> BuildSavedGameIdAliasMap(string root)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                var rows = ReadSavedGameIndexRowsFromDatabase(root, connection);
                if (rows.Count > 0) return BuildGameIdAliasMap(rows, rows);
            }

            var rawRows = ReadSavedGameIndexRowsFile(root);
            if (rawRows.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedRows = MergeGameIndexRows(rawRows);
            return BuildGameIdAliasMap(rawRows, normalizedRows);
        }

        public void ApplyGameIdAliases(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            using (var connection = OpenIndexDatabase(root))
            {
                RewriteGameIdAliasesInLibraryMetadataIndexStorage(root, aliasMap, connection);
            }
            if (dependencies.ApplyGameIdAliasesToCachedMetadataIndex != null) dependencies.ApplyGameIdAliasesToCachedMetadataIndex(root, aliasMap);
        }

        public List<GameIndexEditorRow> LoadSavedGameIndexRows(string root)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                BackfillMissingGameIndexExternalIdsFromOtherDatabases(root, connection);
                var rawRows = ReadSavedGameIndexRowsFromDatabase(root, connection);
                var normalizedRows = MergeGameIndexRows(rawRows);
                var aliasMap = BuildGameIdAliasMap(rawRows, normalizedRows);
                if (rawRows.Count > 0 && (HasGameIdAliasChanges(aliasMap) || normalizedRows.Count != rawRows.Count || rawRows.Any(row => string.IsNullOrWhiteSpace(row.GameId))))
                {
                    WriteSavedGameIndexRowsToDatabase(root, normalizedRows, connection);
                    RewriteGameIdAliasesInLibraryMetadataIndexStorage(root, aliasMap, connection);
                    if (HasGameIdAliasChanges(aliasMap))
                    {
                        if (dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile != null) dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
                        if (dependencies.ApplyGameIdAliasesToCachedMetadataIndex != null) dependencies.ApplyGameIdAliasesToCachedMetadataIndex(root, aliasMap);
                    }
                }
                return normalizedRows;
            }
        }

        public List<FilenameConventionRule> LoadFilenameConventions(string root)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                return ReadFilenameConventionsFromDatabase(root, connection);
            }
        }

        public List<FilenameConventionSample> LoadFilenameConventionSamples(string root, int maxCount)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                return ReadFilenameConventionSamplesFromDatabase(root, maxCount, connection);
            }
        }

        public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var sourceRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).ToList();
            var mergedRows = MergeGameIndexRows(sourceRows);
            var aliasMap = BuildGameIdAliasMap(sourceRows, mergedRows);
            using (var connection = OpenIndexDatabase(root))
            {
                WriteSavedGameIndexRowsToDatabase(root, mergedRows, connection);
                if (HasGameIdAliasChanges(aliasMap))
                {
                    RewriteGameIdAliasesInLibraryMetadataIndexStorage(root, aliasMap, connection);
                }
            }
            if (HasGameIdAliasChanges(aliasMap))
            {
                if (dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile != null) dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
                if (dependencies.ApplyGameIdAliasesToCachedMetadataIndex != null) dependencies.ApplyGameIdAliasesToCachedMetadataIndex(root, aliasMap);
            }
        }

        public void SaveFilenameConventions(string root, IEnumerable<FilenameConventionRule> rules)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                WriteFilenameConventionsToDatabase(root, rules, connection);
            }
        }

        public void RecordFilenameConventionSample(string root, string fileName, FilenameParseResult parseResult)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                UpsertFilenameConventionSample(root, fileName, parseResult, connection);
            }
        }

        public void DeleteFilenameConventionSamples(string root, IEnumerable<long> sampleIds)
        {
            var ids = (sampleIds ?? Enumerable.Empty<long>()).Where(id => id > 0).Distinct().ToList();
            if (string.IsNullOrWhiteSpace(root) || ids.Count == 0) return;

            using (var connection = OpenIndexDatabase(root))
            {
                foreach (var id in ids)
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
DELETE FROM filename_convention_sample
WHERE root = $root AND sample_id = $sampleId;";
                        command.Parameters.AddWithValue("$root", root ?? string.Empty);
                        command.Parameters.AddWithValue("$sampleId", id);
                        command.ExecuteNonQuery();
                    }
                }
            }
        }

        public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexEntries(string root)
        {
            var aliasMap = BuildSavedGameIdAliasMap(root);
            using (var connection = OpenIndexDatabase(root))
            {
                return ReadLibraryMetadataIndexFromDatabase(root, connection, aliasMap);
            }
        }

        public void SaveLibraryMetadataIndexEntries(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            using (var connection = OpenIndexDatabase(root))
            {
                WriteLibraryMetadataIndexToDatabase(root, index, connection);
            }
        }
    }
}
