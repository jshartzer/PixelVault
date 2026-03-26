using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        bool IsNetworkPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase)) return true;
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrWhiteSpace(root)) return false;
                var drive = new DriveInfo(root);
                return drive.DriveType == DriveType.Network;
            }
            catch
            {
                return false;
            }
        }

        int GetLibraryScanWorkerCount(int batchCount, string pathHint)
        {
            if (batchCount <= 1) return 1;
            var maxWorkers = IsNetworkPath(pathHint)
                ? 2
                : Math.Min(Math.Max(Environment.ProcessorCount, 1), 4);
            return Math.Max(1, Math.Min(batchCount, maxWorkers));
        }

        string BuildLibraryMetadataStamp(string file)
        {
            var info = new FileInfo(file);
            return MetadataCacheStamp(file).ToString();
        }

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false)
        {
            if (!forceDiskReload && string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase) && libraryMetadataIndex.Count > 0) return libraryMetadataIndex;
            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            var aliasMap = BuildSavedGameIdAliasMapFromFile(root);
            using (var connection = OpenIndexDatabase(root))
            {
                foreach (var pair in ReadLibraryMetadataIndexFromDatabase(root, connection, aliasMap))
                {
                    libraryMetadataIndex[pair.Key] = pair.Value;
                }
            }
            return libraryMetadataIndex;
        }

        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var savedEntries = index.Values.Where(v => v != null && !string.IsNullOrWhiteSpace(v.FilePath) && File.Exists(v.FilePath)).OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            using (var connection = OpenIndexDatabase(root))
            {
                WriteLibraryMetadataIndexToDatabase(root, savedEntries.ToDictionary(entry => entry.FilePath, entry => entry, StringComparer.OrdinalIgnoreCase), connection);
            }
            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            foreach (var entry in savedEntries)
            {
                libraryMetadataIndex[entry.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = entry.FilePath,
                    Stamp = entry.Stamp,
                    GameId = NormalizeGameId(entry.GameId),
                    ConsoleLabel = entry.ConsoleLabel,
                    TagText = entry.TagText
                };
            }
        }

        LibraryMetadataIndexEntry TryGetLibraryMetadataIndexEntry(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            if (index == null) index = LoadLibraryMetadataIndex(root);
            LibraryMetadataIndexEntry entry;
            if (!index.TryGetValue(file, out entry)) return null;
            return entry;
        }

        int ScanLibraryMetadataIndex(string root, string folderPath, bool forceRescan, Action<int, int, string> progress, Func<bool> isCancellationRequested)
        {
            EnsureDir(root, "Library folder");
            EnsureExifTool();
            var index = LoadLibraryMetadataIndex(root);
            var gameRows = LoadSavedGameIndexRows(root);
            var targets = new List<string>();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    targets.AddRange(Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia));
                }
            }
            else
            {
                targets.AddRange(Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly).Where(IsMedia));
            }
            var fileList = targets.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var targetSet = new HashSet<string>(fileList, StringComparer.OrdinalIgnoreCase);
            int updated = 0, unchanged = 0, removed = 0;
            var scopeLabel = string.IsNullOrWhiteSpace(folderPath) ? "library" : (Path.GetFileName(folderPath) ?? "folder");
            if (progress != null) progress(0, fileList.Count, "Queued " + fileList.Count + " media file(s) for " + scopeLabel + " scan.");
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                foreach (var stale in index.Keys.Where(key => !targetSet.Contains(key) || !File.Exists(key)).ToList())
                {
                    if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                    index.Remove(stale);
                    removed++;
                }
            }
            else
            {
                foreach (var stale in index.Keys.Where(key =>
                {
                    var fileDirectory = Path.GetDirectoryName(key) ?? string.Empty;
                    return string.Equals(fileDirectory, folderPath, StringComparison.OrdinalIgnoreCase)
                        && (!targetSet.Contains(key) || !File.Exists(key));
                }).ToList())
                {
                    if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                    index.Remove(stale);
                    removed++;
                }
            }
            if (removed > 0 && progress != null) progress(0, fileList.Count, "Removed " + removed + " stale index entr" + (removed == 1 ? "y" : "ies") + " before scanning.");

            var pendingFiles = new List<string>();
            var pendingStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fileList)
            {
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                var stamp = BuildLibraryMetadataStamp(file);
                LibraryMetadataIndexEntry existing;
                if (!forceRescan && index.TryGetValue(file, out existing) && string.Equals(existing.Stamp, stamp, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }
                pendingFiles.Add(file);
                pendingStamps[file] = stamp;
            }

            if (progress != null)
            {
                progress(unchanged, fileList.Count,
                    pendingFiles.Count == 0
                        ? "All files were unchanged after checking cached metadata stamps."
                        : "Preparing batched ExifTool reads for " + pendingFiles.Count + " changed file(s); " + unchanged + " unchanged.");
            }

            const int batchSize = 250;
            int batchCount = pendingFiles.Count == 0 ? 0 : (int)Math.Ceiling((double)pendingFiles.Count / batchSize);
            var batches = pendingFiles
                .Chunk(batchSize)
                .Select((files, index) => Tuple.Create(index + 1, files))
                .ToList();
            var batchTagsByFile = new ConcurrentDictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var scanWorkerCount = GetLibraryScanWorkerCount(batches.Count, string.IsNullOrWhiteSpace(folderPath) ? root : folderPath);
            if (batches.Count > 0)
            {
                Log("Running library metadata scan with " + scanWorkerCount + " worker(s) across " + batches.Count + " ExifTool read batch(es) for " + pendingFiles.Count + " changed file(s).");
            }
            try
            {
                Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = scanWorkerCount }, delegate(Tuple<int, string[]> batch)
                {
                    if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                    if (progress != null) progress(unchanged, fileList.Count, "Reading embedded tags in batch " + batch.Item1 + " of " + batchCount + " (" + batch.Item2.Length + " file(s)).");
                    var batchTags = ReadEmbeddedKeywordTagsBatch(batch.Item2);
                    foreach (var file in batch.Item2)
                    {
                        string[] tags;
                        if (!batchTags.TryGetValue(file, out tags)) tags = new string[0];
                        batchTagsByFile[file] = tags;
                    }
                });
            }
            catch (AggregateException ex)
            {
                var cancellation = ex.Flatten().InnerExceptions.OfType<OperationCanceledException>().FirstOrDefault();
                if (cancellation != null) throw cancellation;
                throw;
            }

            int processed = 0;
            foreach (var file in pendingFiles)
            {
                if (isCancellationRequested != null && isCancellationRequested()) throw new OperationCanceledException("Library scan cancelled.");
                string[] tags;
                if (!batchTagsByFile.TryGetValue(file, out tags)) tags = new string[0];
                var platformLabel = DetermineConsoleLabelFromTags(tags);
                var existingGameId = index.ContainsKey(file) && index[file] != null ? index[file].GameId : string.Empty;
                index[file] = new LibraryMetadataIndexEntry
                {
                    FilePath = file,
                    Stamp = pendingStamps[file],
                    GameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, existingGameId),
                    ConsoleLabel = platformLabel,
                    TagText = string.Join(", ", tags)
                };
                fileTagCache[file] = tags;
                fileTagCacheStamp[file] = MetadataCacheStamp(file);
                updated++;
                processed++;
                var remaining = fileList.Count - (unchanged + processed);
                if (progress != null) progress(unchanged + processed, fileList.Count, "Indexed " + (unchanged + processed) + " of " + fileList.Count + " | " + remaining + " remaining | " + file);
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
            var summary = string.IsNullOrWhiteSpace(folderPath)
                ? "Library metadata index scan complete: updated " + updated + ", unchanged " + unchanged + ", removed " + removed + "."
                : "Library folder scan complete for " + Path.GetFileName(folderPath) + ": updated " + updated + ", unchanged " + unchanged + ", removed " + removed + ".";
            Log(summary);
            if (progress != null) progress(fileList.Count, fileList.Count, summary);
            return updated;
        }

        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root, true);
            var gameRows = LoadSavedGameIndexRows(root);
            foreach (var file in fileList)
            {
                var tags = ReadEmbeddedKeywordTagsDirect(file);
                var platformLabel = DetermineConsoleLabelFromTags(tags);
                var existingGameId = index.ContainsKey(file) && index[file] != null ? index[file].GameId : string.Empty;
                index[file] = new LibraryMetadataIndexEntry
                {
                    FilePath = file,
                    Stamp = BuildLibraryMetadataStamp(file),
                    GameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, existingGameId),
                    ConsoleLabel = platformLabel,
                    TagText = string.Join(", ", tags)
                };
                fileTagCache[file] = tags;
                fileTagCacheStamp[file] = MetadataCacheStamp(file);
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }

        void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var itemList = (items ?? Enumerable.Empty<ManualMetadataItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            if (itemList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root, true);
            var gameRows = LoadSavedGameIndexRows(root);
            foreach (var item in itemList)
            {
                var tags = BuildMetadataTagSet(null, BuildManualMetadataExtraTags(item), item.AddPhotographyTag);
                var platformLabel = DetermineConsoleLabelFromTags(tags);
                var preferredGameId = ManualMetadataChangesGroupingIdentity(item) ? string.Empty : item.GameId;
                var resolvedRow = ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, platformLabel, preferredGameId);
                item.GameId = resolvedRow == null ? string.Empty : resolvedRow.GameId;
                if (resolvedRow != null && !string.IsNullOrWhiteSpace(resolvedRow.Name)) item.GameName = resolvedRow.Name;
                index[item.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = item.FilePath,
                    Stamp = BuildLibraryMetadataStamp(item.FilePath),
                    GameId = item.GameId,
                    ConsoleLabel = platformLabel,
                    TagText = string.Join(", ", tags)
                };
                fileTagCache[item.FilePath] = tags;
                fileTagCacheStamp[item.FilePath] = MetadataCacheStamp(item.FilePath);
            }
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }

        void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var touchedDirectories = new HashSet<string>(
                fileList
                    .Select(file => Path.GetDirectoryName(file) ?? string.Empty)
                    .Where(path => !string.IsNullOrWhiteSpace(path)),
                StringComparer.OrdinalIgnoreCase);
            var index = LoadLibraryMetadataIndex(root, true);
            var changed = false;
            foreach (var file in fileList)
            {
                if (index.Remove(file)) changed = true;
                fileTagCache.Remove(file);
                fileTagCacheStamp.Remove(file);
            }
            if (changed)
            {
                SaveLibraryMetadataIndex(root, index);
                RebuildLibraryFolderCache(root, index);
                RemoveCachedImageEntries(fileList);
                RemoveCachedFolderListings(touchedDirectories);
            }
        }

        List<PhotoIndexEditorRow> LoadPhotoIndexEditorRows(string root)
        {
            return LoadLibraryMetadataIndex(root, true)
                .Values
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
                .Select(entry => new PhotoIndexEditorRow
                {
                    FilePath = entry.FilePath ?? string.Empty,
                    Stamp = entry.Stamp ?? string.Empty,
                    GameId = NormalizeGameId(entry.GameId),
                    ConsoleLabel = NormalizeConsoleLabel(entry.ConsoleLabel),
                    TagText = entry.TagText ?? string.Empty
                })
                .OrderBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        void SavePhotoIndexEditorRows(string root, IEnumerable<PhotoIndexEditorRow> rows)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            var rowList = (rows ?? Enumerable.Empty<PhotoIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(row.FilePath) && File.Exists(row.FilePath))
                .GroupBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();
            var missingGameId = rowList.FirstOrDefault(row => string.IsNullOrWhiteSpace(NormalizeGameId(row.GameId)));
            if (missingGameId != null) throw new InvalidOperationException("Each photo index row needs a Game ID before saving. Missing: " + Path.GetFileName(missingGameId.FilePath));

            var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rowList)
            {
                var normalizedTags = string.Join(", ", ParseTagText(row.TagText));
                var normalizedConsole = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.ConsoleLabel) ? DetermineConsoleLabelFromTags(ParseTagText(normalizedTags)) : row.ConsoleLabel);
                index[row.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = row.FilePath,
                    Stamp = BuildLibraryMetadataStamp(row.FilePath),
                    GameId = NormalizeGameId(row.GameId),
                    ConsoleLabel = normalizedConsole,
                    TagText = normalizedTags
                };
            }

            var gameRows = LoadSavedGameIndexRows(root);
            foreach (var group in index.Values.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.GameId)).GroupBy(entry => NormalizeGameId(entry.GameId), StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var row = EnsureGameIndexRowForAssignment(gameRows, GuessGameIndexNameForFile(first.FilePath), first.ConsoleLabel, group.Key);
                if (row == null) continue;
                var filePaths = group.Select(entry => entry.FilePath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                row.FileCount = filePaths.Length;
                row.FilePaths = filePaths;
                row.PreviewImagePath = filePaths.FirstOrDefault(IsImage) ?? filePaths.FirstOrDefault() ?? string.Empty;
                row.FolderPath = filePaths
                    .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                    .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(pathGroup => pathGroup.Count())
                    .ThenBy(pathGroup => pathGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pathGroup => pathGroup.Key)
                    .FirstOrDefault() ?? string.Empty;
                row.PlatformLabel = NormalizeConsoleLabel(first.ConsoleLabel);
            }

            SaveSavedGameIndexRows(root, gameRows);
            SaveLibraryMetadataIndex(root, index);
            RebuildLibraryFolderCache(root, index);
        }
    }
}
