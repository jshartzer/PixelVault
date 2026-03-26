using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string NormalizeGameId(string gameId)
        {
            return CleanTag(gameId);
        }

        int ParseGameIdSequence(string gameId)
        {
            var normalized = NormalizeGameId(gameId);
            if (string.IsNullOrWhiteSpace(normalized)) return 0;
            var match = Regex.Match(normalized, @"^G(?<n>\d+)$", RegexOptions.IgnoreCase);
            return match.Success ? ParseInt(match.Groups["n"].Value) : 0;
        }

        string FormatGameId(int number)
        {
            if (number < 1) number = 1;
            return "G" + number.ToString("00000");
        }

        string CreateGameId(IEnumerable<string> existingGameIds)
        {
            var nextNumber = (existingGameIds ?? Enumerable.Empty<string>()).Select(ParseGameIdSequence).DefaultIfEmpty(0).Max() + 1;
            return FormatGameId(nextNumber);
        }

        string NormalizeGameIndexName(string name, string folderPath = null)
        {
            var normalized = CleanTag(name);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
            if (!string.IsNullOrWhiteSpace(folderPath)) return CleanTag(Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)));
            return string.Empty;
        }

        string BuildGameIndexIdentity(string name, string platformLabel)
        {
            return NormalizeGameIndexName(name) + "|" + NormalizeConsoleLabel(platformLabel);
        }

        string BuildGameTitleChoiceLabel(string name, string platformLabel)
        {
            var normalizedName = NormalizeGameIndexName(name);
            if (string.IsNullOrWhiteSpace(normalizedName)) return string.Empty;
            return NormalizeConsoleLabel(string.IsNullOrWhiteSpace(platformLabel) ? "Other" : platformLabel).PadRight(12) + " | " + normalizedName;
        }

        string ExtractGameNameFromChoiceLabel(string value)
        {
            var cleaned = CleanTag(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            var separatorIndex = cleaned.IndexOf(" | ", StringComparison.Ordinal);
            return separatorIndex >= 0 && separatorIndex + 3 < cleaned.Length
                ? CleanTag(cleaned.Substring(separatorIndex + 3))
                : cleaned;
        }

        string BuildGameIndexMergeKey(GameIndexEditorRow row)
        {
            if (row == null) return string.Empty;
            var gameId = NormalizeGameId(row.GameId);
            return !string.IsNullOrWhiteSpace(gameId)
                ? "id:" + gameId
                : "identity:" + BuildGameIndexIdentity(row.Name, row.PlatformLabel);
        }

        string BuildLibraryFolderMasterKey(LibraryFolderInfo folder)
        {
            if (folder == null) return string.Empty;
            var gameId = NormalizeGameId(folder.GameId);
            return !string.IsNullOrWhiteSpace(gameId)
                ? "id:" + gameId
                : "identity:" + BuildGameIndexIdentity(folder.Name, folder.PlatformLabel);
        }

        GameIndexEditorRow CloneGameIndexEditorRow(GameIndexEditorRow row)
        {
            if (row == null) return null;
            return new GameIndexEditorRow
            {
                GameId = NormalizeGameId(row.GameId),
                Name = NormalizeGameIndexName(row.Name, row.FolderPath),
                PlatformLabel = NormalizeConsoleLabel(row.PlatformLabel),
                SteamAppId = CleanTag(row.SteamAppId),
                SteamGridDbId = CleanTag(row.SteamGridDbId),
                SuppressSteamAppIdAutoResolve = row.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = row.SuppressSteamGridDbIdAutoResolve,
                FileCount = Math.Max(0, row.FileCount),
                FolderPath = (row.FolderPath ?? string.Empty).Trim(),
                PreviewImagePath = (row.PreviewImagePath ?? string.Empty).Trim(),
                FilePaths = (row.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }

        Dictionary<string, string> BuildGameIdAliasMap(IEnumerable<GameIndexEditorRow> sourceRows, IEnumerable<GameIndexEditorRow> normalizedRows)
        {
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedByIdentity = (normalizedRows ?? Enumerable.Empty<GameIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => BuildGameIndexIdentity(row.Name, row.PlatformLabel), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => NormalizeGameId(group.First().GameId), StringComparer.OrdinalIgnoreCase);
            foreach (var row in sourceRows ?? Enumerable.Empty<GameIndexEditorRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name)) continue;
                var oldGameId = NormalizeGameId(row.GameId);
                string newGameId;
                if (!normalizedByIdentity.TryGetValue(BuildGameIndexIdentity(row.Name, row.PlatformLabel), out newGameId)) continue;
                if (!string.IsNullOrWhiteSpace(oldGameId)) aliasMap[oldGameId] = newGameId;
                if (!string.IsNullOrWhiteSpace(newGameId)) aliasMap[newGameId] = newGameId;
            }
            foreach (var row in normalizedRows ?? Enumerable.Empty<GameIndexEditorRow>())
            {
                var gameId = NormalizeGameId(row == null ? string.Empty : row.GameId);
                if (!string.IsNullOrWhiteSpace(gameId)) aliasMap[gameId] = gameId;
            }
            return aliasMap;
        }

        bool HasGameIdAliasChanges(Dictionary<string, string> aliasMap)
        {
            return (aliasMap ?? new Dictionary<string, string>()).Any(pair => !string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase));
        }

        List<GameIndexEditorRow> EnsureGameIndexRowsHaveIds(IEnumerable<GameIndexEditorRow> rows)
        {
            var normalizedRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).Select(CloneGameIndexEditorRow).ToList();
            var groupedRows = normalizedRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => BuildGameIndexIdentity(row.Name, row.PlatformLabel), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var assignedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedNumbers = new HashSet<int>();
            foreach (var group in groupedRows)
            {
                var preferredNumber = group
                    .Select(row => ParseGameIdSequence(row.GameId))
                    .Where(number => number > 0)
                    .Distinct()
                    .OrderBy(number => number)
                    .FirstOrDefault(number => !usedNumbers.Contains(number));
                if (preferredNumber > 0)
                {
                    usedNumbers.Add(preferredNumber);
                    assignedIds[group.Key] = FormatGameId(preferredNumber);
                }
            }
            var nextNumber = usedNumbers.Count == 0 ? 1 : usedNumbers.Max() + 1;
            foreach (var group in groupedRows)
            {
                string assignedGameId;
                if (!assignedIds.TryGetValue(group.Key, out assignedGameId))
                {
                    while (usedNumbers.Contains(nextNumber)) nextNumber++;
                    assignedGameId = FormatGameId(nextNumber);
                    usedNumbers.Add(nextNumber);
                    assignedIds[group.Key] = assignedGameId;
                    nextNumber++;
                }
                foreach (var row in group) row.GameId = assignedGameId;
            }
            return normalizedRows;
        }

        List<GameIndexEditorRow> MergeGameIndexRows(IEnumerable<GameIndexEditorRow> rows)
        {
            var normalizedRows = EnsureGameIndexRowsHaveIds(rows).Where(row => !string.IsNullOrWhiteSpace(row.Name)).ToList();
            var mergedRows = new List<GameIndexEditorRow>();
            foreach (var group in normalizedRows.GroupBy(BuildGameIndexMergeKey, StringComparer.OrdinalIgnoreCase))
            {
                var groupRows = group.ToList();
                var preferredName = groupRows
                    .Select(row => NormalizeGameIndexName(row.Name, row.FolderPath))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var representative = groupRows
                    .OrderByDescending(row => !string.IsNullOrWhiteSpace(row.SteamAppId))
                    .ThenByDescending(row => (row.FilePaths ?? new string[0]).Length)
                    .ThenByDescending(row => row.FileCount)
                    .ThenBy(row => row.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .First();
                var mergedFilePaths = groupRows
                    .SelectMany(row => row.FilePaths ?? new string[0])
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var folderPath = groupRows
                    .Select(row => row.FolderPath ?? string.Empty)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    folderPath = groupRows
                        .Select(row => row.FolderPath ?? string.Empty)
                        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
                }
                var previewPath = groupRows
                    .Select(row => row.PreviewImagePath ?? string.Empty)
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
                if (string.IsNullOrWhiteSpace(previewPath)) previewPath = mergedFilePaths.FirstOrDefault(IsImage) ?? mergedFilePaths.FirstOrDefault() ?? string.Empty;
                mergedRows.Add(new GameIndexEditorRow
                {
                    GameId = NormalizeGameId(representative.GameId),
                    Name = preferredName ?? NormalizeGameIndexName(representative.Name, folderPath),
                    PlatformLabel = NormalizeConsoleLabel(representative.PlatformLabel),
                    SteamAppId = groupRows.Select(row => row.SteamAppId ?? string.Empty).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                    SteamGridDbId = groupRows.Select(row => row.SteamGridDbId ?? string.Empty).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                    SuppressSteamAppIdAutoResolve = !groupRows.Any(row => !string.IsNullOrWhiteSpace(row.SteamAppId)) && groupRows.Any(row => row.SuppressSteamAppIdAutoResolve),
                    SuppressSteamGridDbIdAutoResolve = !groupRows.Any(row => !string.IsNullOrWhiteSpace(row.SteamGridDbId)) && groupRows.Any(row => row.SuppressSteamGridDbIdAutoResolve),
                    FileCount = mergedFilePaths.Length > 0 ? mergedFilePaths.Length : groupRows.Max(row => row.FileCount),
                    FolderPath = folderPath ?? string.Empty,
                    PreviewImagePath = previewPath,
                    FilePaths = mergedFilePaths
                });
            }
            return mergedRows
                .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.PlatformLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        bool PruneObsoleteMultipleTagsRows(List<GameIndexEditorRow> rows)
        {
            var rowList = rows ?? new List<GameIndexEditorRow>();
            var staleRows = rowList
                .Where(row => row != null
                    && string.Equals(NormalizeConsoleLabel(row.PlatformLabel), "Multiple Tags", StringComparison.OrdinalIgnoreCase)
                    && (row.FilePaths == null || row.FilePaths.Length == 0)
                    && row.FileCount <= 0
                    && string.IsNullOrWhiteSpace(row.FolderPath)
                    && !string.IsNullOrWhiteSpace(row.Name)
                    && rowList.Any(other => other != null
                        && !ReferenceEquals(other, row)
                        && string.Equals(NormalizeGameIndexName(other.Name), NormalizeGameIndexName(row.Name), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(NormalizeConsoleLabel(other.PlatformLabel), "Multiple Tags", StringComparison.OrdinalIgnoreCase)
                        && (((other.FilePaths ?? new string[0]).Length > 0) || other.FileCount > 0)))
                .ToList();
            if (staleRows.Count == 0) return false;
            foreach (var staleRow in staleRows) rowList.Remove(staleRow);
            return true;
        }

        void RefreshCachedLibraryFoldersFromGameIndex(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            var folders = LoadLibraryFolders(root, LoadLibraryMetadataIndex(root, true));
            ApplySavedGameIndexRows(root, folders);
            SaveLibraryFolderCache(root, BuildLibraryFolderInventoryStamp(root), folders);
        }

        GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId)
        {
            var wantedId = NormalizeGameId(gameId);
            if (string.IsNullOrWhiteSpace(wantedId)) return null;
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>()).FirstOrDefault(row =>
                row != null && string.Equals(NormalizeGameId(row.GameId), wantedId, StringComparison.OrdinalIgnoreCase));
        }

        GameIndexEditorRow FindSavedGameIndexRowByIdentity(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel)
        {
            var wantedIdentity = BuildGameIndexIdentity(name, platformLabel);
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>()).FirstOrDefault(row =>
                row != null && string.Equals(BuildGameIndexIdentity(row.Name, row.PlatformLabel), wantedIdentity, StringComparison.OrdinalIgnoreCase));
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

        void WriteSavedGameIndexRowsFile(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var path = GameIndexPath(root);
            var lines = new List<string>();
            lines.Add(root);
            foreach (var row in rows.Where(row => row != null))
            {
                lines.Add(string.Join("\t", new[]
                {
                    NormalizeGameId(row.GameId),
                    row.FolderPath ?? string.Empty,
                    row.Name ?? string.Empty,
                    row.PlatformLabel ?? string.Empty,
                    row.SteamAppId ?? string.Empty,
                    row.SteamGridDbId ?? string.Empty,
                    row.FileCount.ToString(),
                    row.PreviewImagePath ?? string.Empty,
                    string.Join("|", (row.FilePaths ?? new string[0]).Where(File.Exists))
                }));
            }
            File.WriteAllLines(path, lines.ToArray());
        }

        Dictionary<string, string> BuildSavedGameIdAliasMapFromFile(string root)
        {
            var databasePath = IndexDatabasePath(root);
            if (File.Exists(databasePath))
            {
                using (var connection = OpenIndexDatabase(root))
                {
                    var rows = ReadSavedGameIndexRowsFromDatabase(root, connection);
                    return BuildGameIdAliasMap(rows, rows);
                }
            }
            var rawRows = ReadSavedGameIndexRowsFile(root);
            if (rawRows.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedRows = MergeGameIndexRows(rawRows);
            return BuildGameIdAliasMap(rawRows, normalizedRows);
        }

        void RewriteGameIdAliasesInLibraryMetadataIndexFile(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            var databasePath = IndexDatabasePath(root);
            if (File.Exists(databasePath))
            {
                using (var connection = OpenIndexDatabase(root))
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var pair in aliasMap.Where(pair => !string.IsNullOrWhiteSpace(NormalizeGameId(pair.Key)) && !string.IsNullOrWhiteSpace(NormalizeGameId(pair.Value))))
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
                if (string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in libraryMetadataIndex.Values.Where(entry => entry != null))
                    {
                        var currentGameId = NormalizeGameId(entry.GameId);
                        string mappedGameId;
                        if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId)) entry.GameId = mappedGameId;
                    }
                }
                return;
            }
            var path = LibraryMetadataIndexPath(root);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return;
            bool changed = false;
            var rewritten = new List<string> { lines[0] };
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 5)
                {
                    string mappedGameId;
                    var currentGameId = NormalizeGameId(parts[2]);
                    if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId) && !string.Equals(parts[2], mappedGameId, StringComparison.Ordinal))
                    {
                        parts[2] = mappedGameId;
                        changed = true;
                    }
                    rewritten.Add(string.Join("\t", new[] { parts[0], parts[1], parts[2], parts[3], parts[4] }));
                }
                else
                {
                    rewritten.Add(line);
                }
            }
            if (changed)
            {
                File.WriteAllLines(path, rewritten.ToArray());
                if (string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in libraryMetadataIndex.Values.Where(entry => entry != null))
                    {
                        var currentGameId = NormalizeGameId(entry.GameId);
                        string mappedGameId;
                        if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId)) entry.GameId = mappedGameId;
                    }
                }
            }
        }

        void RewriteGameIdAliasesInLibraryFolderCacheFile(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3) return;
            bool changed = false;
            var rewritten = new List<string> { lines[0], lines[1] };
            foreach (var line in lines.Skip(2))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 8)
                {
                    string mappedGameId;
                    var currentGameId = NormalizeGameId(parts[0]);
                    if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId) && !string.Equals(parts[0], mappedGameId, StringComparison.Ordinal))
                    {
                        parts[0] = mappedGameId;
                        changed = true;
                    }
                    rewritten.Add(string.Join("\t", parts));
                }
                else
                {
                    rewritten.Add(line);
                }
            }
            if (changed) File.WriteAllLines(path, rewritten.ToArray());
        }

        List<GameIndexEditorRow> LoadSavedGameIndexRows(string root)
        {
            List<GameIndexEditorRow> rawRows;
            using (var connection = OpenIndexDatabase(root))
            {
                rawRows = ReadSavedGameIndexRowsFromDatabase(root, connection);
                var normalizedRows = MergeGameIndexRows(rawRows);
                var aliasMap = BuildGameIdAliasMap(rawRows, normalizedRows);
                if (rawRows.Count > 0 && (HasGameIdAliasChanges(aliasMap) || normalizedRows.Count != rawRows.Count || rawRows.Any(row => string.IsNullOrWhiteSpace(row.GameId))))
                {
                    WriteSavedGameIndexRowsToDatabase(root, normalizedRows, connection);
                    RewriteGameIdAliasesInLibraryMetadataIndexFile(root, aliasMap);
                    RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
                }
                return normalizedRows;
            }
        }

        void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var sourceRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).Select(CloneGameIndexEditorRow).ToList();
            var mergedRows = MergeGameIndexRows(sourceRows);
            var aliasMap = BuildGameIdAliasMap(sourceRows, mergedRows);
            using (var connection = OpenIndexDatabase(root))
            {
                WriteSavedGameIndexRowsToDatabase(root, mergedRows, connection);
            }
            if (HasGameIdAliasChanges(aliasMap))
            {
                RewriteGameIdAliasesInLibraryMetadataIndexFile(root, aliasMap);
                RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
            }
        }

        List<GameIndexEditorRow> BuildGameIndexRowsFromFolders(IEnumerable<LibraryFolderInfo> folders)
        {
            return (folders ?? Enumerable.Empty<LibraryFolderInfo>())
                .Where(folder => folder != null)
                .Select(folder => new GameIndexEditorRow
                {
                    GameId = folder.GameId ?? string.Empty,
                    Name = folder.Name ?? string.Empty,
                    PlatformLabel = folder.PlatformLabel ?? string.Empty,
                    SteamAppId = folder.SteamAppId ?? string.Empty,
                    SteamGridDbId = folder.SteamGridDbId ?? string.Empty,
                    SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                    SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                    FileCount = folder.FileCount,
                    FolderPath = folder.FolderPath ?? string.Empty,
                    PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                    FilePaths = folder.FilePaths ?? new string[0]
                })
                .ToList();
        }

        Dictionary<string, int> BuildGameIndexTitleCounts(IEnumerable<GameIndexEditorRow> rows)
        {
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => NormalizeGameIndexName(row.Name, row.FolderPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
