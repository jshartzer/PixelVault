using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string GuessGameIndexNameForFile(string file)
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(file) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(folderName)) return CleanTag(folderName);
            return NormalizeGameIndexName(GetGameNameFromFileName(Path.GetFileNameWithoutExtension(file)));
        }

        GameIndexEditorRow EnsureGameIndexRowForAssignment(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId = null)
        {
            var normalizedName = NormalizeGameIndexName(name);
            var normalizedPlatform = NormalizeConsoleLabel(platformLabel);
            var normalizedGameId = NormalizeGameId(preferredGameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byId = FindSavedGameIndexRowById(rows, normalizedGameId);
                if (byId != null && string.Equals(BuildGameIndexIdentity(byId.Name, byId.PlatformLabel), BuildGameIndexIdentity(normalizedName, normalizedPlatform), StringComparison.OrdinalIgnoreCase))
                {
                    return byId;
                }
            }
            var byIdentity = FindSavedGameIndexRowByIdentity(rows, normalizedName, normalizedPlatform);
            if (byIdentity != null) return byIdentity;
            var created = new GameIndexEditorRow
            {
                GameId = !string.IsNullOrWhiteSpace(normalizedGameId) ? normalizedGameId : CreateGameId(rows.Select(row => row.GameId)),
                Name = normalizedName,
                PlatformLabel = normalizedPlatform,
                SteamAppId = string.Empty,
                SteamGridDbId = string.Empty,
                FileCount = 0,
                FolderPath = string.Empty,
                PreviewImagePath = string.Empty,
                FilePaths = new string[0]
            };
            rows.Add(created);
            return created;
        }

        void EnsureSteamAppIdInGameIndex(string root, string name, string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(steamAppId)) return;
            var rows = LoadSavedGameIndexRows(root);
            var row = EnsureGameIndexRowForAssignment(rows, name, "Steam");
            if (string.Equals(row.SteamAppId ?? string.Empty, steamAppId, StringComparison.Ordinal)) return;
            row.SteamAppId = steamAppId;
            SaveSavedGameIndexRows(root, rows);
        }

        GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId = null)
        {
            var normalizedRows = rows ?? Enumerable.Empty<GameIndexEditorRow>();
            var normalizedGameId = NormalizeGameId(preferredGameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byId = FindSavedGameIndexRowById(normalizedRows, normalizedGameId);
                if (byId != null) return byId;
            }
            return FindSavedGameIndexRowByIdentity(normalizedRows, NormalizeGameIndexName(name), NormalizeConsoleLabel(platformLabel));
        }

        bool DoesSavedGameIndexRowMatchIndexedFile(GameIndexEditorRow row, string file, string platformLabel)
        {
            if (row == null || string.IsNullOrWhiteSpace(file)) return false;
            return string.Equals(
                BuildGameIndexIdentity(row.Name, row.PlatformLabel),
                BuildGameIndexIdentity(GuessGameIndexNameForFile(file), platformLabel),
                StringComparison.OrdinalIgnoreCase);
        }

        bool DoesSavedGameIndexRowMatchIndexedPlatform(GameIndexEditorRow row, string platformLabel)
        {
            if (row == null) return false;
            return string.Equals(NormalizeConsoleLabel(row.PlatformLabel), NormalizeConsoleLabel(platformLabel), StringComparison.OrdinalIgnoreCase);
        }

        string ResolveGameIdForIndexedFile(string root, string file, string platformLabel, IEnumerable<string> tags, Dictionary<string, LibraryMetadataIndexEntry> index, List<GameIndexEditorRow> gameRows, string preferredGameId = null)
        {
            var normalizedPlatform = NormalizeConsoleLabel(platformLabel);
            var guessedName = GuessGameIndexNameForFile(file);
            var normalizedPreferredGameId = NormalizeGameId(preferredGameId);
            var existingRow = FindSavedGameIndexRowById(gameRows, normalizedPreferredGameId);
            if (DoesSavedGameIndexRowMatchIndexedFile(existingRow, file, normalizedPlatform)) return existingRow.GameId;
            if (DoesSavedGameIndexRowMatchIndexedPlatform(existingRow, normalizedPlatform)) return existingRow.GameId;
            LibraryMetadataIndexEntry existingEntry;
            if (index != null && index.TryGetValue(file, out existingEntry))
            {
                var existingGameId = NormalizeGameId(existingEntry.GameId);
                var existingGameRow = FindSavedGameIndexRowById(gameRows, existingGameId);
                if (DoesSavedGameIndexRowMatchIndexedFile(existingGameRow, file, normalizedPlatform)) return existingGameId;
                if (DoesSavedGameIndexRowMatchIndexedPlatform(existingGameRow, normalizedPlatform)) return existingGameId;
            }
            var resolvedByIdentity = FindSavedGameIndexRowByIdentity(gameRows, guessedName, normalizedPlatform);
            if (resolvedByIdentity != null) return resolvedByIdentity.GameId;
            var resolvedRow = EnsureGameIndexRowForAssignment(gameRows, guessedName, normalizedPlatform);
            return resolvedRow == null ? string.Empty : resolvedRow.GameId;
        }

        bool SyncGameIndexRowsFromLibraryFolders(List<GameIndexEditorRow> rows, IEnumerable<LibraryFolderInfo> folders)
        {
            var rowList = rows ?? new List<GameIndexEditorRow>();
            var folderMap = (folders ?? Enumerable.Empty<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.GameId))
                .GroupBy(folder => NormalizeGameId(folder.GameId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            bool changed = false;
            foreach (var row in rowList)
            {
                if (row == null) continue;
                var normalizedGameId = NormalizeGameId(row.GameId);
                LibraryFolderInfo folder;
                if (!string.IsNullOrWhiteSpace(normalizedGameId) && folderMap.TryGetValue(normalizedGameId, out folder))
                {
                    if (!string.Equals(row.Name ?? string.Empty, folder.Name ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.Name = folder.Name ?? string.Empty;
                        changed = true;
                    }
                    if (!string.Equals(row.PlatformLabel ?? string.Empty, folder.PlatformLabel ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.PlatformLabel = folder.PlatformLabel ?? string.Empty;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(folder.SteamAppId) && !string.Equals(row.SteamAppId ?? string.Empty, folder.SteamAppId ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.SteamAppId = folder.SteamAppId ?? string.Empty;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId) && !string.Equals(row.SteamGridDbId ?? string.Empty, folder.SteamGridDbId ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.SteamGridDbId = folder.SteamGridDbId ?? string.Empty;
                        changed = true;
                    }
                    if (row.FileCount != folder.FileCount)
                    {
                        row.FileCount = folder.FileCount;
                        changed = true;
                    }
                    if (!string.Equals(row.FolderPath ?? string.Empty, folder.FolderPath ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.FolderPath = folder.FolderPath ?? string.Empty;
                        changed = true;
                    }
                    if (!string.Equals(row.PreviewImagePath ?? string.Empty, folder.PreviewImagePath ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.PreviewImagePath = folder.PreviewImagePath ?? string.Empty;
                        changed = true;
                    }
                    if (!Enumerable.SequenceEqual((row.FilePaths ?? new string[0]).OrderBy(path => path, StringComparer.OrdinalIgnoreCase), (folder.FilePaths ?? new string[0]).OrderBy(path => path, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
                    {
                        row.FilePaths = folder.FilePaths ?? new string[0];
                        changed = true;
                    }
                }
                else
                {
                    if (row.FileCount != 0) { row.FileCount = 0; changed = true; }
                    if (!string.IsNullOrWhiteSpace(row.FolderPath)) { row.FolderPath = string.Empty; changed = true; }
                    if (!string.IsNullOrWhiteSpace(row.PreviewImagePath)) { row.PreviewImagePath = string.Empty; changed = true; }
                    if ((row.FilePaths ?? new string[0]).Length > 0) { row.FilePaths = new string[0]; changed = true; }
                }
            }
            return changed;
        }

        List<LibraryFolderInfo> LoadLibraryFolders(string root, Dictionary<string, LibraryMetadataIndexEntry> index = null)
        {
            return libraryScanner.LoadLibraryFolders(root, index);
        }

        string GuessSteamAppIdFromFileName(string file)
        {
            return ParseFilename(file).SteamAppId;
        }

        string ResolveLibraryFolderSteamAppId(string platformLabel, IEnumerable<string> files)
        {
            foreach (var file in files ?? Enumerable.Empty<string>())
            {
                var appId = GuessSteamAppIdFromFileName(file);
                if (!string.IsNullOrWhiteSpace(appId)) return appId;
            }
            if (!string.Equals(NormalizeConsoleLabel(platformLabel), "Steam", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return string.Empty;
        }
    }
}
