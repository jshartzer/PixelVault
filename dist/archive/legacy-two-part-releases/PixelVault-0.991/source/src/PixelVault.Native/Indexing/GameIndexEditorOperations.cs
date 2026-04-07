using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        List<GameIndexEditorRow> LoadGameIndexEditorRowsCore(string root, Action<string> setUiStatus)
        {
            return gameIndexService.LoadEditorRowsCore(root, setUiStatus);
        }

        List<GameIndexEditorRow> LoadGameIndexEditorRows(string root)
        {
            return LoadGameIndexEditorRowsCore(root, s => { if (status != null) status.Text = s; });
        }

        void SaveGameIndexEditorRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            gameIndexService.SaveEditorRows(root, rows);
        }

        async Task<int> ResolveMissingGameIndexSteamAppIdsAsync(string root, List<GameIndexEditorRow> rows, Action<int, int, string> progress, CancellationToken cancellationToken = default(CancellationToken))
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
                cancellationToken.ThrowIfCancellationRequested();
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
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
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

        async Task<int> ResolveMissingGameIndexSteamGridDbIdsAsync(string root, List<GameIndexEditorRow> rows, Action<int, int, string> progress, CancellationToken cancellationToken = default(CancellationToken))
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
                cancellationToken.ThrowIfCancellationRequested();
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
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(root, folder, cancellationToken).ConfigureAwait(false);
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
