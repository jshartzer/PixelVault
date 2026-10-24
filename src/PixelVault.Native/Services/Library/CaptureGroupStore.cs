using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
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
        {
            using var connection = Open();
            var snapshot = new CaptureGroupSnapshot(root);
            using (var command = Command(connection, "SELECT name FROM capture_group WHERE root=$root AND game=$game ORDER BY name COLLATE NOCASE", root, game))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) snapshot.Names.Add(reader.GetString(0));
            using (var command = Command(connection, "SELECT file,name FROM capture_group_member WHERE root=$root AND game=$game", root, game))
            using (var reader = command.ExecuteReader())
                while (reader.Read()) snapshot.Assignments[reader.GetString(0)] = reader.GetString(1);
            using (var command = Command(connection, "SELECT grouped FROM capture_group_view WHERE root=$root AND game=$game", root, game))
            {
                var value = command.ExecuteScalar();
                if (value != null) snapshot.Grouped = Convert.ToInt32(value) != 0;
            }
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
