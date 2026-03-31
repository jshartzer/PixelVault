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
            var list = new List<LibraryFolderInfo>();
            if (index == null) index = LoadLibraryMetadataIndex(root);
            var gameRows = LoadSavedGameIndexRows(root);
            bool indexChanged = false;
            bool gameRowsChanged = false;
            var allFiles = Directory.EnumerateDirectories(root)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var missingOrIncompleteFiles = allFiles
                .Where(file =>
                {
                    LibraryMetadataIndexEntry entry;
                    return !index.TryGetValue(file, out entry) || entry == null || entry.CaptureUtcTicks <= 0;
                })
                .ToList();
            var metadataByFile = ReadEmbeddedMetadataBatch(missingOrIncompleteFiles);
            foreach (var file in allFiles)
            {
                LibraryMetadataIndexEntry entry;
                if (!index.TryGetValue(file, out entry) || entry == null || entry.CaptureUtcTicks <= 0)
                {
                    EmbeddedMetadataSnapshot snapshot;
                    if (!metadataByFile.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                    var stamp = BuildLibraryMetadataStamp(file);
                    var previousGameId = entry == null ? string.Empty : NormalizeGameId(entry.GameId);
                    var previousConsole = entry == null ? string.Empty : NormalizeConsoleLabel(entry.ConsoleLabel);
                    var rebuiltEntry = BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, entry, index, gameRows);
                    index[file] = rebuiltEntry;
                    entry = rebuiltEntry;
                    SetCachedFileTags(file, ParseTagText(rebuiltEntry.TagText), MetadataCacheStamp(file));
                    indexChanged = true;
                    if (!string.Equals(previousGameId, NormalizeGameId(rebuiltEntry.GameId), StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(previousConsole, NormalizeConsoleLabel(rebuiltEntry.ConsoleLabel), StringComparison.OrdinalIgnoreCase))
                    {
                        gameRowsChanged = true;
                    }
                }
                else if (string.IsNullOrWhiteSpace(entry.GameId))
                {
                    var tags = ParseTagText(entry.TagText);
                    var platformLabel = string.IsNullOrWhiteSpace(entry.ConsoleLabel)
                        ? NormalizeConsoleLabel(DetermineConsoleLabelFromTags(tags))
                        : NormalizeConsoleLabel(entry.ConsoleLabel);
                    entry.GameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows);
                    indexChanged = true;
                    gameRowsChanged = true;
                }
            }
            var groupedFiles = allFiles
                .Select(file => new
                {
                    File = file,
                    Entry = index.ContainsKey(file) ? index[file] : null
                })
                .Where(item => item.Entry != null && !string.IsNullOrWhiteSpace(item.Entry.GameId))
                .GroupBy(item => NormalizeGameId(item.Entry.GameId), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var group in groupedFiles)
            {
                var groupFiles = group.Select(item => item.File).OrderByDescending(file => ResolveIndexedLibraryDate(root, file, index)).ThenBy(Path.GetFileName).ToArray();
                var saved = FindSavedGameIndexRowById(gameRows, group.Key);
                var preferredFolderPath = groupFiles
                    .Select(file => Path.GetDirectoryName(file) ?? string.Empty)
                    .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(pathGroup => pathGroup.Count())
                    .ThenBy(pathGroup => pathGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pathGroup => pathGroup.Key)
                    .FirstOrDefault();
                var platformLabel = saved == null
                    ? DetermineFolderPlatform(groupFiles.ToList(), index)
                    : NormalizeConsoleLabel(saved.PlatformLabel);
                long newestCaptureUtcTicks = 0;
                if (groupFiles.Length > 0)
                {
                    LibraryMetadataIndexEntry newestEntry;
                    if (index.TryGetValue(groupFiles[0], out newestEntry) && newestEntry != null)
                    {
                        newestCaptureUtcTicks = newestEntry.CaptureUtcTicks;
                    }
                    if (newestCaptureUtcTicks <= 0)
                    {
                        newestCaptureUtcTicks = ToCaptureUtcTicks(ResolveIndexedLibraryDate(root, groupFiles[0], index));
                    }
                }
                list.Add(new LibraryFolderInfo
                {
                    GameId = group.Key,
                    Name = saved == null ? GuessGameIndexNameForFile(groupFiles[0]) : saved.Name,
                    FolderPath = string.IsNullOrWhiteSpace(saved == null ? string.Empty : saved.FolderPath) ? preferredFolderPath : saved.FolderPath,
                    FileCount = groupFiles.Length,
                    PreviewImagePath = groupFiles.FirstOrDefault(IsImage) ?? groupFiles.FirstOrDefault(),
                    PlatformLabel = platformLabel,
                    FilePaths = groupFiles,
                    NewestCaptureUtcTicks = newestCaptureUtcTicks,
                    SteamAppId = saved != null && (saved.SuppressSteamAppIdAutoResolve || !string.IsNullOrWhiteSpace(saved.SteamAppId))
                        ? (saved.SteamAppId ?? string.Empty)
                        : ResolveLibraryFolderSteamAppId(platformLabel, groupFiles),
                    SteamGridDbId = saved == null ? string.Empty : (saved.SteamGridDbId ?? string.Empty),
                    SuppressSteamAppIdAutoResolve = saved != null && saved.SuppressSteamAppIdAutoResolve,
                    SuppressSteamGridDbIdAutoResolve = saved != null && saved.SuppressSteamGridDbIdAutoResolve
                });
            }
            gameRowsChanged = SyncGameIndexRowsFromLibraryFolders(gameRows, list) || gameRowsChanged;
            gameRowsChanged = PruneObsoleteMultipleTagsRows(gameRows) || gameRowsChanged;
            if (gameRowsChanged) SaveSavedGameIndexRows(root, gameRows);
            if (indexChanged) SaveLibraryMetadataIndex(root, index);
            return list;
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
