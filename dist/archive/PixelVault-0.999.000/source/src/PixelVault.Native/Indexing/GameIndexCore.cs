using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

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
            if (string.IsNullOrWhiteSpace(normalized) && !string.IsNullOrWhiteSpace(folderPath))
            {
                normalized = CleanTag(Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)));
            }
            normalized = FilenameParserService.NormalizeColonStandinUnderscoresForGameTitle(normalized);
            return StripKnownPlatformSuffixes(normalized);
        }

        string StripKnownPlatformSuffixes(string value)
        {
            var cleaned = CleanTag(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            while (true)
            {
                var updated = Regex.Replace(cleaned, @"\s*-\s*(Steam|PS5|PlayStation|Xbox PC|Xbox\/Windows|Xbox Windows|Xbox|PC)\s*$", string.Empty, RegexOptions.IgnoreCase);
                updated = CleanTag(updated);
                if (string.Equals(updated, cleaned, StringComparison.Ordinal)) return cleaned;
                cleaned = updated;
            }
        }

        string BuildGameIndexIdentity(string name, string platformLabel)
        {
            return NormalizeGameIndexName(name) + "|" + NormalizeConsoleLabel(platformLabel);
        }

        string BuildGameTitleChoiceLabel(string name, string platformLabel)
        {
            var normalizedName = NormalizeGameIndexName(name);
            if (string.IsNullOrWhiteSpace(normalizedName)) return string.Empty;
            return normalizedName + " | " + NormalizeConsoleLabel(string.IsNullOrWhiteSpace(platformLabel) ? "Other" : platformLabel);
        }

        string ExtractGameNameFromChoiceLabel(string value)
        {
            var cleaned = CleanTag(value);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            var separatorIndex = cleaned.IndexOf(" | ", StringComparison.Ordinal);
            return separatorIndex > 0
                ? CleanTag(cleaned.Substring(0, separatorIndex))
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
                FilePaths = (row.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                IsCompleted100Percent = row.IsCompleted100Percent,
                CompletedUtcTicks = row.CompletedUtcTicks > 0 ? row.CompletedUtcTicks : 0L,
                IsFavorite = row.IsFavorite,
                IsShowcase = row.IsShowcase,
                CollectionNotes = row.CollectionNotes ?? string.Empty,
                IndexAddedUtcTicks = row.IndexAddedUtcTicks
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
                var mergedIndexAddedTicks = groupRows
                    .Select(row => row.IndexAddedUtcTicks)
                    .Where(t => t > 0)
                    .DefaultIfEmpty(0L)
                    .Min();
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
                    FilePaths = mergedFilePaths,
                    IsCompleted100Percent = groupRows.Any(row => row.IsCompleted100Percent),
                    CompletedUtcTicks = groupRows.Select(row => row.CompletedUtcTicks).Where(t => t > 0).DefaultIfEmpty(0L).Max(),
                    IsFavorite = groupRows.Any(row => row.IsFavorite),
                    IsShowcase = groupRows.Any(row => row.IsShowcase),
                    CollectionNotes = groupRows.Select(row => row.CollectionNotes ?? string.Empty).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                    IndexAddedUtcTicks = mergedIndexAddedTicks
                });
            }
            var activeRowsByName = mergedRows
                .Where(row => row != null
                    && !string.IsNullOrWhiteSpace(row.Name)
                    && (((row.FilePaths ?? new string[0]).Length > 0) || row.FileCount > 0 || !string.IsNullOrWhiteSpace(row.FolderPath)))
                .GroupBy(row => NormalizeGameIndexName(row.Name, row.FolderPath), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            return mergedRows
                .Where(row =>
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.Name)) return false;
                    if (((row.FilePaths ?? new string[0]).Length > 0) || row.FileCount > 0 || !string.IsNullOrWhiteSpace(row.FolderPath)) return true;
                    var normalizedPlatform = NormalizeConsoleLabel(row.PlatformLabel);
                    if (!string.Equals(normalizedPlatform, "Other", StringComparison.OrdinalIgnoreCase)) return true;
                    List<GameIndexEditorRow> activeRows;
                    if (!activeRowsByName.TryGetValue(NormalizeGameIndexName(row.Name, row.FolderPath), out activeRows)) return true;
                    return !activeRows.Any(active => active != null && !ReferenceEquals(active, row) && !string.Equals(NormalizeConsoleLabel(active.PlatformLabel), "Other", StringComparison.OrdinalIgnoreCase));
                })
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
            if (librarySession != null && librarySession.HasLibraryRoot
                && string.Equals(root, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                librarySession.RefreshFolderCacheAfterGameIndexChange();
            else
                libraryScanner.RefreshFolderCacheAfterGameIndexChange(root);
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

        /// <summary>Editor save path: normalize external IDs and suppress flags from prior disk rows (used by <see cref="IGameIndexService"/>).</summary>
        void ApplyGameIndexEditorSaveRowPolicies(List<GameIndexEditorRow> rows, List<GameIndexEditorRow> previousSaved)
        {
            var nowTicks = DateTime.UtcNow.Ticks;
            foreach (var row in rows ?? new List<GameIndexEditorRow>())
            {
                if (row == null) continue;
                var previous = FindSavedGameIndexRowById(previousSaved, row.GameId)
                    ?? FindSavedGameIndexRowByIdentity(previousSaved, row.Name, row.PlatformLabel);
                if (row.IndexAddedUtcTicks <= 0)
                {
                    if (previous != null && previous.IndexAddedUtcTicks > 0) row.IndexAddedUtcTicks = previous.IndexAddedUtcTicks;
                    else if (previous == null) row.IndexAddedUtcTicks = nowTicks;
                }
                var cleanedSteamAppId = CleanTag(row.SteamAppId);
                var cleanedSteamGridDbId = CleanTag(row.SteamGridDbId);
                row.SuppressSteamAppIdAutoResolve = ShouldSuppressExternalIdAutoResolve(
                    cleanedSteamAppId,
                    previous == null ? string.Empty : previous.SteamAppId,
                    (previous != null && previous.SuppressSteamAppIdAutoResolve) || row.SuppressSteamAppIdAutoResolve);
                row.SuppressSteamGridDbIdAutoResolve = ShouldSuppressExternalIdAutoResolve(
                    cleanedSteamGridDbId,
                    previous == null ? string.Empty : previous.SteamGridDbId,
                    (previous != null && previous.SuppressSteamGridDbIdAutoResolve) || row.SuppressSteamGridDbIdAutoResolve);
                row.SteamAppId = cleanedSteamAppId;
                row.SteamGridDbId = cleanedSteamGridDbId;
                row.CompletedUtcTicks = row.CompletedUtcTicks > 0 ? row.CompletedUtcTicks : 0L;
                row.CollectionNotes = row.CollectionNotes ?? string.Empty;
            }
        }

        Dictionary<string, string> BuildSavedGameIdAliasMapFromFile(string root)
        {
            return indexPersistenceService.BuildSavedGameIdAliasMap(root);
        }

        void ApplyGameIdAliasesToCachedMetadataIndex(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            if (!string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var entry in libraryMetadataIndex.Values.Where(entry => entry != null))
            {
                var currentGameId = NormalizeGameId(entry.GameId);
                string mappedGameId;
                if (!string.IsNullOrWhiteSpace(currentGameId) && aliasMap.TryGetValue(currentGameId, out mappedGameId)) entry.GameId = mappedGameId;
            }
        }

        void RewriteGameIdAliasesInLibraryFolderCacheFile(string root, Dictionary<string, string> aliasMap)
        {
            if (aliasMap == null || aliasMap.Count == 0) return;
            var path = LibraryFolderCachePath(root);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 3) return;
            var headerLines = 2;
            if (lines.Length > 2 && IsLibraryFolderCacheMetadataRevisionLine(lines[2]))
                headerLines = 3;
            if (lines.Length <= headerLines) return;
            bool changed = false;
            var rewritten = new List<string>();
            for (var h = 0; h < headerLines; h++)
                rewritten.Add(lines[h]);
            foreach (var line in lines.Skip(headerLines))
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
            return indexPersistenceService.LoadSavedGameIndexRows(root);
        }

        /// <summary>Uses <see cref="IGameIndexService.GetSavedRowsForRoot"/> when constructed; falls back during early <see cref="MainWindow"/> ctor.</summary>
        List<GameIndexEditorRow> GetSavedGameIndexRowsForRoot(string root)
        {
            if (gameIndexService != null)
                return gameIndexService.GetSavedRowsForRoot(root);
            if (string.IsNullOrWhiteSpace(root)) return new List<GameIndexEditorRow>();
            if (librarySession != null && string.Equals(root, libraryRoot, StringComparison.OrdinalIgnoreCase))
                return librarySession.LoadSavedGameIndexRows();
            return LoadSavedGameIndexRows(root) ?? new List<GameIndexEditorRow>();
        }

        void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            gameIndexEditorAssignmentService.SaveSavedGameIndexRows(root, rows);
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
                    FilePaths = folder.FilePaths ?? new string[0],
                    IsCompleted100Percent = folder.IsCompleted100Percent,
                    CompletedUtcTicks = folder.CompletedUtcTicks,
                    IsFavorite = folder.IsFavorite,
                    IsShowcase = folder.IsShowcase,
                    CollectionNotes = folder.CollectionNotes ?? string.Empty
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
