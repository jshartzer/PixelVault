using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    /// <summary>Stable game-wide identity, independent of the platform chosen for the cover/hero.</summary>
    internal sealed class CaptureGroupScope
    {
        internal string Key;
        internal string[] LegacyGameIds = Array.Empty<string>();

        internal static CaptureGroupScope ForFolder(LibraryBrowserFolderView folder, IEnumerable<GameIndexEditorRow> savedRows)
        {
            if (folder == null || folder.IsTimelineProjection || folder.IsSessionProjection || folder.PendingGameAssignment) return null;
            var sources = folder.SourceFolders.Concat(new[] { folder.PrimaryFolder }).Where(source => source != null).ToList();
            var ids = sources.Select(source => source.GameId).Append(folder.GameId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var rows = (savedRows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).ToList();
            var storageIds = sources.Select(source => source.StorageGroupId)
                .Concat(rows.Where(row => ids.Contains(row.GameId, StringComparer.OrdinalIgnoreCase)).Select(row => row.StorageGroupId))
                .Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (storageIds.Count == 1)
            {
                ids.AddRange(rows.Where(row => string.Equals(row.StorageGroupId?.Trim(), storageIds[0], StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.GameId).Where(id => !string.IsNullOrWhiteSpace(id)));
            }
            var orderedIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            if (orderedIds.Length == 0) return null;
            return new CaptureGroupScope
            {
                Key = storageIds.Count == 1 ? "storage:" + storageIds[0] : orderedIds[0],
                LegacyGameIds = orderedIds
            };
        }
    }

    /// <summary>Authored organization lives outside the rebuildable photo index and file metadata.</summary>
    internal sealed class CaptureGroupStore
    {
        readonly string databasePath;
        internal CaptureGroupStore(string databasePath) => this.databasePath = databasePath;

        SqliteConnection Open()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)));
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"PRAGMA busy_timeout=5000;
CREATE TABLE IF NOT EXISTS capture_group (
 root TEXT COLLATE NOCASE NOT NULL, game TEXT COLLATE NOCASE NOT NULL, name TEXT COLLATE NOCASE NOT NULL,
 PRIMARY KEY(root, game, name));
CREATE TABLE IF NOT EXISTS capture_group_member (
 root TEXT COLLATE NOCASE NOT NULL, game TEXT COLLATE NOCASE NOT NULL, file TEXT COLLATE NOCASE NOT NULL,
 name TEXT NOT NULL, PRIMARY KEY(root, game, file));
CREATE TABLE IF NOT EXISTS capture_group_view (
 root TEXT COLLATE NOCASE NOT NULL, game TEXT COLLATE NOCASE NOT NULL, grouped INTEGER NOT NULL,
 PRIMARY KEY(root, game));";
            command.ExecuteNonQuery();
            return connection;
        }

        static string RootKey(string root) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        static string FileKey(string root, string file) => Path.GetRelativePath(RootKey(root), Path.GetFullPath(file));
        static SqliteCommand Command(SqliteConnection connection, string sql, string root, string game)
        {
            if (string.IsNullOrWhiteSpace(game)) throw new ArgumentException("A game identity is required.", nameof(game));
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$root", RootKey(root));
            command.Parameters.AddWithValue("$game", game.Trim());
            return command;
        }

        internal CaptureGroupSnapshot Load(string root, string game)
            => LoadScope(root, new CaptureGroupScope { Key = game });

        internal CaptureGroupSnapshot LoadScope(string root, CaptureGroupScope scope)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            var snapshot = new CaptureGroupSnapshot(root);
            // Read old per-platform records without rewriting them. Explicit game-wide writes take precedence,
            // including an empty assignment (Remove from Group) and the user's saved combined-view choice.
            var keys = scope.LegacyGameIds.Where(id => !string.Equals(id, scope.Key, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Append(scope.Key);
            foreach (var game in keys)
            {
                var canonical = string.Equals(game, scope.Key, StringComparison.OrdinalIgnoreCase);
                using (var command = Command(connection, "SELECT name FROM capture_group WHERE root=$root AND game=$game ORDER BY name COLLATE NOCASE", root, game))
                {
                    command.Transaction = transaction;
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var name = reader.GetString(0);
                        if (!snapshot.Names.Contains(name, StringComparer.OrdinalIgnoreCase)) snapshot.Names.Add(name);
                    }
                }
                using (var command = Command(connection, "SELECT file,name FROM capture_group_member WHERE root=$root AND game=$game", root, game))
                {
                    command.Transaction = transaction;
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var file = reader.GetString(0);
                        if (canonical || !snapshot.Assignments.ContainsKey(file)) snapshot.Assignments[file] = reader.GetString(1);
                    }
                }
                using (var command = Command(connection, "SELECT grouped FROM capture_group_view WHERE root=$root AND game=$game", root, game))
                {
                    command.Transaction = transaction;
                    var value = command.ExecuteScalar();
                    if (value != null && (canonical || !snapshot.Grouped.HasValue)) snapshot.Grouped = Convert.ToInt32(value) != 0;
                }
            }
            transaction.Commit();
            return snapshot;
        }

        internal static string ValidateName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 200 || name.Any(char.IsControl))
                throw new ArgumentException("Enter a group name of 1–200 characters.");
            if (string.Equals(name, "Ungrouped", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Ungrouped is reserved for photos without a group.");
            return name;
        }

        internal void Assign(string root, string game, IEnumerable<string> files, string name)
        {
            // Empty is an explicit removal, so Roblox automatic grouping will not undo the user's choice.
            name = string.IsNullOrEmpty(name) ? string.Empty : ValidateName(name);
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            if (name.Length > 0)
            {
                using var create = Command(connection, "INSERT OR IGNORE INTO capture_group VALUES($root,$game,$name)", root, game);
                create.Transaction = transaction;
                create.Parameters.AddWithValue("$name", name);
                create.ExecuteNonQuery();
            }
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var command = Command(connection, "INSERT INTO capture_group_member VALUES($root,$game,$file,$name) ON CONFLICT(root,game,file) DO UPDATE SET name=excluded.name", root, game);
                command.Transaction = transaction;
                command.Parameters.AddWithValue("$file", FileKey(root, file));
                command.Parameters.AddWithValue("$name", name);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        internal void SetGrouped(string root, string game, bool grouped)
        {
            using var connection = Open();
            using var command = Command(connection, "INSERT INTO capture_group_view VALUES($root,$game,$value) ON CONFLICT(root,game) DO UPDATE SET grouped=excluded.grouped", root, game);
            command.Parameters.AddWithValue("$value", grouped ? 1 : 0);
            command.ExecuteNonQuery();
        }

        internal void MoveFile(string root, string before, string after)
        {
            if (!File.Exists(databasePath)) return;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE capture_group_member SET file=$after WHERE root=$root AND file=$before";
            command.Parameters.AddWithValue("$root", RootKey(root));
            command.Parameters.AddWithValue("$before", FileKey(root, before));
            command.Parameters.AddWithValue("$after", FileKey(root, after));
            command.ExecuteNonQuery();
        }

        internal sealed class CaptureGroupSnapshot
        {
            readonly string root;
            internal CaptureGroupSnapshot(string root) => this.root = root;
            internal bool? Grouped;
            internal readonly List<string> Names = new();
            internal readonly Dictionary<string, string> Assignments = new(StringComparer.OrdinalIgnoreCase);
            internal string Resolve(string file, bool roblox) => Assignments.TryGetValue(FileKey(root, file), out var name)
                ? name : roblox ? RobloxCaptureNaming.GetExperienceName(file) : string.Empty;

            internal List<LibraryDetailRenderGroup> Project(IEnumerable<string> orderedFiles, bool roblox)
            {
                return orderedFiles.GroupBy(file => Resolve(file, roblox), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => string.IsNullOrEmpty(group.Key) ? 1 : 0)
                    .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new LibraryDetailRenderGroup
                    {
                        HeaderText = string.IsNullOrEmpty(group.Key) ? "Ungrouped" : group.Key,
                        Files = group.ToList()
                    }).ToList();
            }
        }
    }
}
