using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Title hint for indexing when photo metadata does not already pin <c>GameId</c>.
        /// Per <c>PV-PLN-LIBST-001</c> Step 1, parent folder names are not used as identity — only filename parse / stem heuristics.
        /// </summary>
        string GuessGameIndexNameForFile(string file)
        {
            if (string.IsNullOrWhiteSpace(file)) return string.Empty;
            var displayFileName = Path.GetFileName(file);
            var parsed = ParseFilename(displayFileName, libraryRoot);
            var hint = parsed.GameTitleHint ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var normHint = NormalizeGameIndexName(CleanTag(hint));
                if (!string.IsNullOrWhiteSpace(normHint)) return normHint;
            }

            return NormalizeGameIndexName(GetGameNameFromFileName(displayFileName));
        }

        GameIndexEditorRow EnsureGameIndexRowForAssignment(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId = null)
        {
            return gameIndexEditorAssignmentService.EnsureManualMetadataMasterRow(rows, name, platformLabel, preferredGameId);
        }

        void EnsureSteamAppIdInGameIndex(string root, string name, string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(steamAppId)) return;
            var rows = GetSavedGameIndexRowsForRoot(root);
            var row = EnsureGameIndexRowForAssignment(rows, name, "Steam");
            if (string.Equals(row.SteamAppId ?? string.Empty, steamAppId, StringComparison.Ordinal)) return;
            row.SteamAppId = steamAppId;
            SaveSavedGameIndexRows(root, rows);
        }

        void EnsureNonSteamIdInGameIndex(string root, string name, string nonSteamId)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(nonSteamId)) return;
            var rows = GetSavedGameIndexRowsForRoot(root);
            var row = EnsureGameIndexRowForAssignment(rows, name, "Emulation");
            if (string.Equals(row.NonSteamId ?? string.Empty, nonSteamId, StringComparison.Ordinal)) return;
            row.NonSteamId = nonSteamId;
            SaveSavedGameIndexRows(root, rows);
        }

        GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId = null)
        {
            return gameIndexEditorAssignmentService.ResolveExistingGameIndexRowForAssignment(rows, name, platformLabel, preferredGameId);
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
                        var rowNorm = NormalizeGameIndexName(row.Name, row.FolderPath);
                        var folderNorm = NormalizeGameIndexName(folder.Name, folder.FolderPath);
                        if (!string.Equals(FoldGameTitleForIdentityMatch(rowNorm), FoldGameTitleForIdentityMatch(folderNorm), StringComparison.OrdinalIgnoreCase))
                        {
                            row.Name = folder.Name ?? string.Empty;
                            changed = true;
                        }
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
                    if (!string.IsNullOrWhiteSpace(folder.NonSteamId) && !string.Equals(row.NonSteamId ?? string.Empty, folder.NonSteamId ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.NonSteamId = folder.NonSteamId ?? string.Empty;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId) && !string.Equals(row.SteamGridDbId ?? string.Empty, folder.SteamGridDbId ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.SteamGridDbId = folder.SteamGridDbId ?? string.Empty;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(folder.RetroAchievementsGameId) && !string.Equals(row.RetroAchievementsGameId ?? string.Empty, folder.RetroAchievementsGameId ?? string.Empty, StringComparison.Ordinal))
                    {
                        row.RetroAchievementsGameId = folder.RetroAchievementsGameId ?? string.Empty;
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
