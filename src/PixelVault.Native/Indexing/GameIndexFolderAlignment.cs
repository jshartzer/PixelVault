using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string BuildCanonicalGameIndexFolderName(GameIndexEditorRow row, Dictionary<string, int> titleCounts)
        {
            var normalizedName = NormalizeGameIndexName(row == null ? string.Empty : row.Name, row == null ? null : row.FolderPath);
            if (string.IsNullOrWhiteSpace(normalizedName)) normalizedName = "Unknown Game";
            var safeName = GetSafeGameFolderName(normalizedName);
            var key = NormalizeGameIndexName(normalizedName);
            int count;
            if (titleCounts != null && titleCounts.TryGetValue(key, out count) && count > 1)
            {
                var platform = NormalizeConsoleLabel(row == null ? string.Empty : row.PlatformLabel);
                return GetSafeGameFolderName(safeName + " - " + platform);
            }
            return safeName;
        }

        string BuildCanonicalGameIndexFolderPath(string root, GameIndexEditorRow row, Dictionary<string, int> titleCounts)
        {
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            return Path.Combine(root, BuildCanonicalGameIndexFolderName(row, titleCounts));
        }

        void TryDeleteEmptyDirectory(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
                if (Directory.EnumerateFileSystemEntries(path).Any()) return;
                Directory.Delete(path, false);
            }
            catch (Exception ex)
            {
                Log("TryDeleteEmptyDirectory: " + path + " — " + ex.Message);
            }
        }

        void AlignLibraryFoldersToGameIndex(string root, List<GameIndexEditorRow> rows)
        {
            if (string.IsNullOrWhiteSpace(root) || rows == null) return;

            var titleCounts = BuildGameIndexTitleCounts(rows);
            var index = LoadLibraryMetadataIndex(root, true);
            var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var affectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows.Where(entry => entry != null))
            {
                var desiredDirectory = BuildCanonicalGameIndexFolderPath(root, row, titleCounts);
                if (string.IsNullOrWhiteSpace(desiredDirectory))
                {
                    row.FolderPath = string.Empty;
                    continue;
                }

                var updatedFiles = new List<string>();
                foreach (var sourcePath in (row.FilePaths ?? new string[0]).Where(File.Exists).ToArray())
                {
                    var currentDirectory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
                    var targetPath = sourcePath;

                    if (!string.Equals(currentDirectory.TrimEnd(Path.DirectorySeparatorChar), desiredDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(desiredDirectory);
                        targetPath = Path.Combine(desiredDirectory, Path.GetFileName(sourcePath));
                        if (File.Exists(targetPath)) targetPath = Unique(targetPath);
                        File.Move(sourcePath, targetPath);
                        MoveMetadataSidecarIfPresent(sourcePath, targetPath);
                        affectedFiles.Add(sourcePath);
                        affectedFiles.Add(targetPath);
                        Log("Game index folder align: " + sourcePath + " -> " + targetPath);
                        if (!string.IsNullOrWhiteSpace(currentDirectory)) touchedDirectories.Add(currentDirectory);
                        if (!string.IsNullOrWhiteSpace(desiredDirectory)) touchedDirectories.Add(desiredDirectory);
                    }

                    LibraryMetadataIndexEntry entry;
                    if (index.TryGetValue(sourcePath, out entry))
                    {
                        index.Remove(sourcePath);
                    }
                    else
                    {
                        var fallbackTags = ReadEmbeddedKeywordTagsDirect(targetPath);
                        var targetStamp = BuildLibraryMetadataStamp(targetPath);
                        entry = new LibraryMetadataIndexEntry
                        {
                            FilePath = targetPath,
                            Stamp = targetStamp,
                            GameId = NormalizeGameId(row.GameId),
                            RetroAchievementsGameId = CleanTag(row.RetroAchievementsGameId),
                            ConsoleLabel = NormalizeConsoleLabel(row.PlatformLabel),
                            TagText = string.Join(", ", fallbackTags),
                            CaptureUtcTicks = ResolveLibraryMetadataCaptureUtcTicks(targetPath, targetStamp, null, null),
                            IndexAddedUtcTicks = DateTime.UtcNow.Ticks
                        };
                    }

                    entry.FilePath = targetPath;
                    entry.Stamp = BuildLibraryMetadataStamp(targetPath);
                    entry.GameId = NormalizeGameId(row.GameId);
                    entry.RetroAchievementsGameId = CleanTag(row.RetroAchievementsGameId);
                    entry.ConsoleLabel = NormalizeConsoleLabel(row.PlatformLabel);
                    if (entry.CaptureUtcTicks <= 0)
                    {
                        entry.CaptureUtcTicks = ResolveLibraryMetadataCaptureUtcTicks(targetPath, entry.Stamp, null, entry);
                    }
                    index[targetPath] = entry;
                    updatedFiles.Add(targetPath);
                }

                row.FilePaths = updatedFiles
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                row.FileCount = row.FilePaths.Length;
                row.FolderPath = row.FilePaths.Length > 0
                    ? desiredDirectory
                    : (string.IsNullOrWhiteSpace(row.FolderPath) ? string.Empty : desiredDirectory);
                row.PreviewImagePath = row.FilePaths.FirstOrDefault(IsImage) ?? row.FilePaths.FirstOrDefault() ?? string.Empty;
            }

            SaveLibraryMetadataIndex(root, index);
            foreach (var directory in touchedDirectories) TryDeleteEmptyDirectory(directory);
            RemoveCachedImageEntries(affectedFiles);
            RemoveCachedFolderListings(touchedDirectories);
            RemoveCachedFileTagEntries(affectedFiles);
        }

        int CountSharedGameIndexFiles(GameIndexEditorRow left, GameIndexEditorRow right)
        {
            if (left == null || right == null) return 0;
            var leftFiles = new HashSet<string>((left.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
            if (leftFiles.Count == 0) return 0;
            return (right.FilePaths ?? new string[0]).Count(path => !string.IsNullOrWhiteSpace(path) && leftFiles.Contains(path));
        }

        Dictionary<string, string> BuildGameIndexSaveAliasMap(IEnumerable<GameIndexEditorRow> previousRows, IEnumerable<GameIndexEditorRow> currentRows)
        {
            var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var oldRows = MergeGameIndexRows(previousRows).Where(row => row != null).ToList();
            var newRows = MergeGameIndexRows(currentRows).Where(row => row != null).ToList();
            var newByGameId = newRows
                .Where(row => !string.IsNullOrWhiteSpace(NormalizeGameId(row.GameId)))
                .GroupBy(row => NormalizeGameId(row.GameId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var newByIdentity = newRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(row => BuildGameIndexIdentity(row.Name, row.PlatformLabel), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var oldRow in oldRows)
            {
                var oldGameId = NormalizeGameId(oldRow.GameId);
                if (string.IsNullOrWhiteSpace(oldGameId)) continue;

                GameIndexEditorRow target;
                if (newByGameId.TryGetValue(oldGameId, out target))
                {
                    aliasMap[oldGameId] = oldGameId;
                    continue;
                }

                var bestByFiles = newRows
                    .Select(row => new { Row = row, Shared = CountSharedGameIndexFiles(oldRow, row) })
                    .Where(match => match.Shared > 0)
                    .OrderByDescending(match => match.Shared)
                    .ThenByDescending(match => match.Row.FileCount)
                    .Select(match => match.Row)
                    .FirstOrDefault();
                if (bestByFiles != null)
                {
                    aliasMap[oldGameId] = NormalizeGameId(bestByFiles.GameId);
                    continue;
                }

                var folderMatches = newRows
                    .Where(row => !string.IsNullOrWhiteSpace(oldRow.FolderPath)
                        && string.Equals(row.FolderPath ?? string.Empty, oldRow.FolderPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (folderMatches.Count == 1)
                {
                    aliasMap[oldGameId] = NormalizeGameId(folderMatches[0].GameId);
                    continue;
                }

                var identity = BuildGameIndexIdentity(oldRow.Name, oldRow.PlatformLabel);
                if (!string.IsNullOrWhiteSpace(identity) && newByIdentity.TryGetValue(identity, out target))
                {
                    aliasMap[oldGameId] = NormalizeGameId(target.GameId);
                }
            }

            return aliasMap
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        GameIndexEditorRow FindSavedGameIndexRow(IEnumerable<GameIndexEditorRow> rows, LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            var savedRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).ToList();
            var byGameId = FindSavedGameIndexRowById(savedRows, folder.GameId);
            if (byGameId != null) return byGameId;
            return FindSavedGameIndexRowByIdentity(savedRows, folder.Name, folder.PlatformLabel);
        }

        bool ApplySavedGameIndexRows(string root, List<LibraryFolderInfo> folders)
        {
            var savedRows = GetSavedGameIndexRowsForRoot(root);
            if (savedRows.Count == 0 || folders == null || folders.Count == 0) return false;
            bool changed = false;
            foreach (var folder in folders)
            {
                if (folder.PendingGameAssignment) continue;
                var saved = FindSavedGameIndexRow(savedRows, folder);
                if (saved == null) continue;
                if (!string.IsNullOrWhiteSpace(saved.GameId) && !string.Equals(folder.GameId ?? string.Empty, saved.GameId ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.GameId = saved.GameId;
                    changed = true;
                }
                var storageGroupId = saved.StorageGroupId ?? string.Empty;
                if (!string.Equals(folder.StorageGroupId ?? string.Empty, storageGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    folder.StorageGroupId = storageGroupId;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(saved.Name) && !string.Equals(folder.Name ?? string.Empty, saved.Name ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.Name = saved.Name;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(saved.PlatformLabel) && !string.Equals(folder.PlatformLabel ?? string.Empty, saved.PlatformLabel ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.PlatformLabel = saved.PlatformLabel;
                    changed = true;
                }
                if (saved.SuppressSteamAppIdAutoResolve)
                {
                    if (!folder.SuppressSteamAppIdAutoResolve || !string.IsNullOrWhiteSpace(folder.SteamAppId))
                    {
                        folder.SteamAppId = string.Empty;
                        folder.SuppressSteamAppIdAutoResolve = true;
                        changed = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(saved.SteamAppId) && (!string.Equals(folder.SteamAppId ?? string.Empty, saved.SteamAppId ?? string.Empty, StringComparison.Ordinal) || folder.SuppressSteamAppIdAutoResolve))
                {
                    folder.SteamAppId = saved.SteamAppId;
                    folder.SuppressSteamAppIdAutoResolve = false;
                    changed = true;
                }
                if (!string.Equals(folder.NonSteamId ?? string.Empty, saved.NonSteamId ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.NonSteamId = saved.NonSteamId ?? string.Empty;
                    changed = true;
                }
                if (saved.SuppressSteamGridDbIdAutoResolve)
                {
                    if (!folder.SuppressSteamGridDbIdAutoResolve || !string.IsNullOrWhiteSpace(folder.SteamGridDbId))
                    {
                        folder.SteamGridDbId = string.Empty;
                        folder.SuppressSteamGridDbIdAutoResolve = true;
                        changed = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(saved.SteamGridDbId) && (!string.Equals(folder.SteamGridDbId ?? string.Empty, saved.SteamGridDbId ?? string.Empty, StringComparison.Ordinal) || folder.SuppressSteamGridDbIdAutoResolve))
                {
                    folder.SteamGridDbId = saved.SteamGridDbId;
                    folder.SuppressSteamGridDbIdAutoResolve = false;
                    changed = true;
                }
                if (!string.Equals(folder.RetroAchievementsGameId ?? string.Empty, saved.RetroAchievementsGameId ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.RetroAchievementsGameId = saved.RetroAchievementsGameId ?? string.Empty;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(saved.PreviewImagePath) && File.Exists(saved.PreviewImagePath) && !string.Equals(folder.PreviewImagePath ?? string.Empty, saved.PreviewImagePath ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.PreviewImagePath = saved.PreviewImagePath;
                    changed = true;
                }
                if (folder.IsCompleted100Percent != saved.IsCompleted100Percent)
                {
                    folder.IsCompleted100Percent = saved.IsCompleted100Percent;
                    changed = true;
                }
                if (folder.CompletedUtcTicks != saved.CompletedUtcTicks)
                {
                    folder.CompletedUtcTicks = saved.CompletedUtcTicks;
                    changed = true;
                }
                if (folder.IsFavorite != saved.IsFavorite)
                {
                    folder.IsFavorite = saved.IsFavorite;
                    changed = true;
                }
                if (folder.IsShowcase != saved.IsShowcase)
                {
                    folder.IsShowcase = saved.IsShowcase;
                    changed = true;
                }
                if (!string.Equals(folder.CollectionNotes ?? string.Empty, saved.CollectionNotes ?? string.Empty, StringComparison.Ordinal))
                {
                    folder.CollectionNotes = saved.CollectionNotes ?? string.Empty;
                    changed = true;
                }
            }
            return changed;
        }

        void UpsertSavedGameIndexRow(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(root)) return;
            var rows = GetSavedGameIndexRowsForRoot(root);
            var saved = FindSavedGameIndexRow(rows, folder);
            var gameId = NormalizeGameId(folder.GameId);
            if (string.IsNullOrWhiteSpace(gameId))
            {
                var byIdentity = FindSavedGameIndexRowByIdentity(rows, folder.Name, folder.PlatformLabel);
                gameId = byIdentity == null ? CreateGameId(rows.Select(row => row.GameId)) : byIdentity.GameId;
            }
            if (saved == null)
            {
                rows.Add(new GameIndexEditorRow
                {
                    GameId = gameId,
                    FolderPath = folder.FolderPath ?? string.Empty,
                    Name = folder.Name ?? string.Empty,
                    PlatformLabel = folder.PlatformLabel ?? string.Empty,
                    SteamAppId = folder.SteamAppId ?? string.Empty,
                    NonSteamId = folder.NonSteamId ?? string.Empty,
                    SteamGridDbId = folder.SteamGridDbId ?? string.Empty,
                    RetroAchievementsGameId = folder.RetroAchievementsGameId ?? string.Empty,
                    SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                    SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                    FileCount = folder.FileCount,
                    PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                    FilePaths = folder.FilePaths ?? new string[0],
                    IsCompleted100Percent = folder.IsCompleted100Percent,
                    CompletedUtcTicks = folder.CompletedUtcTicks,
                    IsFavorite = folder.IsFavorite,
                    IsShowcase = folder.IsShowcase,
                    CollectionNotes = folder.CollectionNotes ?? string.Empty,
                    StorageGroupId = folder.StorageGroupId ?? string.Empty
                });
            }
            else
            {
                saved.GameId = gameId;
                saved.Name = folder.Name ?? string.Empty;
                saved.PlatformLabel = folder.PlatformLabel ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folder.SteamAppId) || folder.SuppressSteamAppIdAutoResolve)
                {
                    saved.SteamAppId = folder.SteamAppId ?? string.Empty;
                    saved.SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve;
                }
                saved.NonSteamId = folder.NonSteamId ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId) || folder.SuppressSteamGridDbIdAutoResolve)
                {
                    saved.SteamGridDbId = folder.SteamGridDbId ?? string.Empty;
                    saved.SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve;
                }
                saved.RetroAchievementsGameId = folder.RetroAchievementsGameId ?? string.Empty;
                saved.FileCount = folder.FileCount;
                saved.PreviewImagePath = folder.PreviewImagePath ?? string.Empty;
                saved.FilePaths = folder.FilePaths ?? new string[0];
                saved.IsCompleted100Percent = folder.IsCompleted100Percent;
                saved.CompletedUtcTicks = folder.CompletedUtcTicks;
                saved.IsFavorite = folder.IsFavorite;
                saved.IsShowcase = folder.IsShowcase;
                saved.CollectionNotes = folder.CollectionNotes ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(folder.StorageGroupId))
                    saved.StorageGroupId = folder.StorageGroupId;
            }
            SaveSavedGameIndexRows(root, rows);
        }
    }
}
