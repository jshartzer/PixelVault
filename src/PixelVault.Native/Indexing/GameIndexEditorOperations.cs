using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                ClearLibraryFolderCache(root);
                return;
            }
            var fresh = LoadLibraryFolders(root, index);
            ApplySavedGameIndexRows(root, fresh);
            SaveLibraryFolderCache(root, BuildLibraryFolderInventoryStamp(root), fresh);
        }

        List<GameIndexEditorRow> LoadGameIndexEditorRows(string root)
        {
            var folders = LoadLibraryFoldersCached(root, false);
            if (folders == null || folders.Count == 0)
            {
                status.Text = "Building game index";
                Log("Game index cache missing or stale. Rebuilding it before editing.");
                folders = LoadLibraryFoldersCached(root, true);
            }
            var liveRows = BuildGameIndexRowsFromFolders(folders);
            var savedRows = LoadSavedGameIndexRows(root);
            var rows = MergeGameIndexRows(savedRows.Concat(liveRows));
            if (savedRows.Count == 0 || rows.Count != savedRows.Count)
            {
                SaveSavedGameIndexRows(root, rows);
                RefreshCachedLibraryFoldersFromGameIndex(root);
            }
            return rows;
        }

        void SaveGameIndexEditorRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var previousSavedRows = LoadSavedGameIndexRows(root);
            foreach (var row in (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(entry => entry != null))
            {
                var previous = FindSavedGameIndexRowById(previousSavedRows, row.GameId)
                    ?? FindSavedGameIndexRowByIdentity(previousSavedRows, row.Name, row.PlatformLabel);
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
            }
            var normalizedRows = MergeGameIndexRows(rows);
            var previousRows = MergeGameIndexRows(LoadSavedGameIndexRows(root).Concat(BuildGameIndexRowsFromFolders(LoadLibraryFoldersCached(root, false) ?? new List<LibraryFolderInfo>())));
            var aliasMap = BuildGameIndexSaveAliasMap(previousRows, normalizedRows);
            AlignLibraryFoldersToGameIndex(root, normalizedRows);
            SaveSavedGameIndexRows(root, normalizedRows);
            if (aliasMap.Count > 0)
            {
                RewriteGameIdAliasesInLibraryMetadataIndexFile(root, aliasMap);
                RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
            }
            RefreshCachedLibraryFoldersFromGameIndex(root);
        }

        int ResolveMissingGameIndexSteamAppIds(string root, List<GameIndexEditorRow> rows, Action<int, int, string> progress)
        {
            var allRows = rows ?? new List<GameIndexEditorRow>();
            var targets = allRows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(BuildGameIndexMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int resolved = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var row = targets[i];
                var detailPrefix = "Game " + (i + 1) + " of " + targets.Count + " | " + row.Name;
                if (row.SuppressSteamAppIdAutoResolve)
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | manually cleared");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(row.SteamAppId))
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | already cached as " + row.SteamAppId);
                    continue;
                }
                var folder = new LibraryFolderInfo
                {
                    GameId = row.GameId ?? string.Empty,
                    Name = row.Name ?? string.Empty,
                    FolderPath = row.FolderPath ?? string.Empty,
                    FileCount = row.FileCount,
                    PreviewImagePath = row.PreviewImagePath ?? string.Empty,
                    PlatformLabel = row.PlatformLabel ?? string.Empty,
                    FilePaths = row.FilePaths ?? new string[0],
                    SteamAppId = row.SteamAppId ?? string.Empty,
                    SteamGridDbId = row.SteamGridDbId ?? string.Empty
                };
                var appId = ResolveBestLibraryFolderSteamAppId(root, folder);
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    foreach (var match in allRows.Where(entry =>
                        string.Equals(BuildGameIndexMergeKey(entry), BuildGameIndexMergeKey(row), StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                    }
                    resolved++;
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | resolved " + appId);
                }
                else
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | no match");
                }
            }
            SaveGameIndexEditorRows(root, allRows);
            return resolved;
        }

        int ResolveMissingGameIndexSteamGridDbIds(string root, List<GameIndexEditorRow> rows, Action<int, int, string> progress)
        {
            if (!HasSteamGridDbApiToken())
            {
                if (progress != null) progress(0, 0, "SteamGridDB token not configured. STID resolution skipped.");
                return 0;
            }
            var allRows = rows ?? new List<GameIndexEditorRow>();
            var targets = allRows
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.Name))
                .GroupBy(BuildGameIndexMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(row => row.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            int resolved = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var row = targets[i];
                var detailPrefix = "STID " + (i + 1) + " of " + targets.Count + " | " + row.Name;
                if (row.SuppressSteamGridDbIdAutoResolve)
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | manually cleared");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(row.SteamGridDbId))
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | already cached as " + row.SteamGridDbId);
                    continue;
                }
                var folder = new LibraryFolderInfo
                {
                    GameId = row.GameId ?? string.Empty,
                    Name = row.Name ?? string.Empty,
                    FolderPath = row.FolderPath ?? string.Empty,
                    FileCount = row.FileCount,
                    PreviewImagePath = row.PreviewImagePath ?? string.Empty,
                    PlatformLabel = row.PlatformLabel ?? string.Empty,
                    FilePaths = row.FilePaths ?? new string[0],
                    SteamAppId = row.SteamAppId ?? string.Empty,
                    SteamGridDbId = row.SteamGridDbId ?? string.Empty
                };
                var steamGridDbId = ResolveBestLibraryFolderSteamGridDbId(root, folder);
                if (!string.IsNullOrWhiteSpace(steamGridDbId))
                {
                    foreach (var match in allRows.Where(entry =>
                        string.Equals(BuildGameIndexMergeKey(entry), BuildGameIndexMergeKey(row), StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamGridDbId = steamGridDbId;
                    }
                    resolved++;
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | resolved " + steamGridDbId);
                }
                else
                {
                    if (progress != null) progress(i + 1, targets.Count, detailPrefix + " | no match");
                }
            }
            SaveGameIndexEditorRows(root, allRows);
            return resolved;
        }
    }
}
