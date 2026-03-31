using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
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
            return MetadataCacheStamp(file).ToString();
        }

        long ToCaptureUtcTicks(DateTime captureTime)
        {
            return captureTime <= DateTime.MinValue ? 0L : captureTime.ToUniversalTime().Ticks;
        }

        long ToCaptureUtcTicks(DateTime? captureTime)
        {
            return captureTime.HasValue ? ToCaptureUtcTicks(captureTime.Value) : 0L;
        }

        bool TryConvertCaptureUtcTicksToLocal(long captureUtcTicks, out DateTime captureDate)
        {
            captureDate = DateTime.MinValue;
            if (captureUtcTicks <= 0) return false;
            try
            {
                captureDate = new DateTime(captureUtcTicks, DateTimeKind.Utc).ToLocalTime();
                return true;
            }
            catch
            {
                return false;
            }
        }

        string[] ResolveLibraryMetadataTags(EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry)
        {
            var snapshotTags = (snapshot == null ? new string[0] : (snapshot.Tags ?? new string[0]))
                .Select(CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (snapshotTags.Length > 0) return snapshotTags;
            return existingEntry == null ? new string[0] : ParseTagText(existingEntry.TagText);
        }

        long ResolveLibraryMetadataCaptureUtcTicks(string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry)
        {
            var snapshotTicks = ToCaptureUtcTicks(snapshot == null ? (DateTime?)null : snapshot.CaptureTime);
            if (snapshotTicks > 0) return snapshotTicks;

            if (existingEntry != null
                && existingEntry.CaptureUtcTicks > 0
                && string.Equals(existingEntry.Stamp ?? string.Empty, stamp ?? string.Empty, StringComparison.Ordinal))
            {
                return existingEntry.CaptureUtcTicks;
            }

            var fallback = GetLibraryDate(file);
            return fallback <= DateTime.MinValue ? 0L : fallback.ToUniversalTime().Ticks;
        }

        LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(string root, string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry, Dictionary<string, LibraryMetadataIndexEntry> index, List<GameIndexEditorRow> gameRows)
        {
            var tags = ResolveLibraryMetadataTags(snapshot, existingEntry);
            var platformLabel = DetermineConsoleLabelFromTags(tags);
            var preferredGameId = existingEntry == null ? string.Empty : existingEntry.GameId;
            return new LibraryMetadataIndexEntry
            {
                FilePath = file,
                Stamp = stamp,
                GameId = ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, preferredGameId),
                ConsoleLabel = platformLabel,
                TagText = string.Join(", ", tags),
                CaptureUtcTicks = ResolveLibraryMetadataCaptureUtcTicks(file, stamp, snapshot, existingEntry)
            };
        }

        DateTime ResolveIndexedLibraryDate(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return DateTime.MinValue;
            LibraryMetadataIndexEntry entry;
            if (index == null || !index.TryGetValue(file, out entry))
            {
                entry = TryGetLibraryMetadataIndexEntry(root, file, index);
            }
            if (entry != null && entry.CaptureUtcTicks > 0)
            {
                var stamp = BuildLibraryMetadataStamp(file);
                if (string.Equals(entry.Stamp ?? string.Empty, stamp, StringComparison.Ordinal))
                {
                    DateTime cachedCapture;
                    if (TryConvertCaptureUtcTicksToLocal(entry.CaptureUtcTicks, out cachedCapture)) return cachedCapture;
                }
            }
            return GetLibraryDate(file);
        }

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false)
        {
            if (!forceDiskReload && string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase) && libraryMetadataIndex.Count > 0) return libraryMetadataIndex;
            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            foreach (var pair in indexPersistenceService.LoadLibraryMetadataIndexEntries(root))
            {
                libraryMetadataIndex[pair.Key] = pair.Value;
            }
            return libraryMetadataIndex;
        }

        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var savedEntries = index.Values.Where(v => v != null && !string.IsNullOrWhiteSpace(v.FilePath) && File.Exists(v.FilePath)).OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            indexPersistenceService.SaveLibraryMetadataIndexEntries(root, savedEntries.ToDictionary(entry => entry.FilePath, entry => entry, StringComparer.OrdinalIgnoreCase));
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
                    TagText = entry.TagText,
                    CaptureUtcTicks = entry.CaptureUtcTicks
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

        int ScanLibraryMetadataIndex(string root, string folderPath, bool forceRescan, Action<int, int, string> progress, CancellationToken cancellationToken = default(CancellationToken))
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
                    cancellationToken.ThrowIfCancellationRequested();
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
                    cancellationToken.ThrowIfCancellationRequested();
                    index.Remove(stale);
                    removed++;
                }
            }
            if (removed > 0 && progress != null) progress(0, fileList.Count, "Removed " + removed + " stale index entr" + (removed == 1 ? "y" : "ies") + " before scanning.");

            var pendingFiles = new List<string>();
            var pendingStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            var batchMetadataByFile = new ConcurrentDictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
            var scanWorkerCount = GetLibraryScanWorkerCount(batches.Count, string.IsNullOrWhiteSpace(folderPath) ? root : folderPath);
            if (batches.Count > 0)
            {
                Log("Running library metadata scan with " + scanWorkerCount + " worker(s) across " + batches.Count + " ExifTool read batch(es) for " + pendingFiles.Count + " changed file(s).");
            }
            try
            {
                Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = scanWorkerCount, CancellationToken = cancellationToken }, delegate(Tuple<int, string[]> batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (progress != null) progress(unchanged, fileList.Count, "Reading embedded metadata in batch " + batch.Item1 + " of " + batchCount + " (" + batch.Item2.Length + " file(s)).");
                    var batchMetadata = ReadEmbeddedMetadataBatch(batch.Item2, cancellationToken);
                    foreach (var file in batch.Item2)
                    {
                        EmbeddedMetadataSnapshot snapshot;
                        if (!batchMetadata.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                        batchMetadataByFile[file] = snapshot;
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
                cancellationToken.ThrowIfCancellationRequested();
                EmbeddedMetadataSnapshot snapshot;
                if (!batchMetadataByFile.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                LibraryMetadataIndexEntry existingEntry;
                if (!index.TryGetValue(file, out existingEntry)) existingEntry = null;
                index[file] = BuildResolvedLibraryMetadataIndexEntry(root, file, pendingStamps[file], snapshot, existingEntry, index, gameRows);
                var tags = ResolveLibraryMetadataTags(snapshot, existingEntry);
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

        Dictionary<string, string[]> ReadEmbeddedKeywordTagsForFiles(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var sourceFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sourceFiles.Count == 0) return result;

            var stampsByFile = sourceFiles.ToDictionary(file => file, MetadataCacheStamp, StringComparer.OrdinalIgnoreCase);
            var pendingFiles = new List<string>();
            foreach (var file in sourceFiles)
            {
                string[] cachedTags;
                long cachedStamp;
                if (fileTagCache.TryGetValue(file, out cachedTags)
                    && fileTagCacheStamp.TryGetValue(file, out cachedStamp)
                    && cachedStamp == stampsByFile[file])
                {
                    result[file] = cachedTags ?? new string[0];
                }
                else
                {
                    pendingFiles.Add(file);
                }
            }

            if (pendingFiles.Count == 0) return result;

            var batchTags = ReadEmbeddedKeywordTagsBatch(pendingFiles, cancellationToken);
            foreach (var file in pendingFiles)
            {
                string[] tags;
                if (!batchTags.TryGetValue(file, out tags)) tags = new string[0];
                result[file] = tags;
                fileTagCache[file] = tags;
                fileTagCacheStamp[file] = stampsByFile[file];
            }
            return result;
        }

        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (fileList.Count == 0) return;
            var index = LoadLibraryMetadataIndex(root, true);
            var gameRows = LoadSavedGameIndexRows(root);
            var metadataByFile = ReadEmbeddedMetadataBatch(fileList);
            foreach (var file in fileList)
            {
                EmbeddedMetadataSnapshot snapshot;
                if (!metadataByFile.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                var stamp = BuildLibraryMetadataStamp(file);
                LibraryMetadataIndexEntry existingEntry;
                if (!index.TryGetValue(file, out existingEntry)) existingEntry = null;
                index[file] = BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, existingEntry, index, gameRows);
                var tags = ResolveLibraryMetadataTags(snapshot, existingEntry);
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
                    TagText = string.Join(", ", tags),
                    CaptureUtcTicks = ToCaptureUtcTicks(item.CaptureTime)
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

            var existingIndex = LoadLibraryMetadataIndex(root, true);
            var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rowList)
            {
                var normalizedTags = string.Join(", ", ParseTagText(row.TagText));
                var normalizedConsole = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.ConsoleLabel) ? DetermineConsoleLabelFromTags(ParseTagText(normalizedTags)) : row.ConsoleLabel);
                var stamp = BuildLibraryMetadataStamp(row.FilePath);
                LibraryMetadataIndexEntry existingEntry;
                if (!existingIndex.TryGetValue(row.FilePath, out existingEntry)) existingEntry = null;
                index[row.FilePath] = new LibraryMetadataIndexEntry
                {
                    FilePath = row.FilePath,
                    Stamp = stamp,
                    GameId = NormalizeGameId(row.GameId),
                    ConsoleLabel = normalizedConsole,
                    TagText = normalizedTags,
                    CaptureUtcTicks = ResolveLibraryMetadataCaptureUtcTicks(row.FilePath, stamp, null, existingEntry)
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
