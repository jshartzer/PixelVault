using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    internal sealed class LibraryScanner : ILibraryScanner
    {
        readonly ILibraryScanHost host;
        readonly IMetadataService metadataService;
        readonly IFileSystemService fileSystem;
        readonly Action<string, Dictionary<string, LibraryMetadataIndexEntry>> folderCacheRebuildHook;

        /// <param name="folderCacheRebuildHook">Optional test hook; when set, replaces full folder cache rebuild (avoids heavy host dependencies).</param>
        public LibraryScanner(
            ILibraryScanHost host,
            IMetadataService metadataService,
            IFileSystemService fileSystem,
            Action<string, Dictionary<string, LibraryMetadataIndexEntry>> folderCacheRebuildHook = null)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
            this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            this.folderCacheRebuildHook = folderCacheRebuildHook;
        }

        public int ScanLibraryMetadataIndex(
            string root,
            string folderPath,
            bool forceRescan,
            Action<int, int, string> progress,
            CancellationToken cancellationToken = default)
        {
            int updatedResult = 0;
            string summaryAfterSave = string.Empty;
            int fileListCountAfterSave = 0;
            lock (host.LibraryMaintenanceSync)
            {
                host.EnsureLibraryRootExists(root);
                host.EnsureExifTool();
                var index = host.LoadLibraryMetadataIndex(root, false);
                var gameRows = host.LoadSavedGameIndexRows(root);
                var targets = new List<string>();
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    foreach (var dir in fileSystem.EnumerateDirectories(root))
                    {
                        targets.AddRange(fileSystem.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(host.IsLibraryMediaFile));
                    }
                }
                else
                {
                    targets.AddRange(fileSystem.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories).Where(host.IsLibraryMediaFile));
                }

                var fileList = targets.Where(fileSystem.FileExists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                var targetSet = new HashSet<string>(fileList, StringComparer.OrdinalIgnoreCase);
                int updated = 0, unchanged = 0, removed = 0;
                var scopeLabel = string.IsNullOrWhiteSpace(folderPath) ? "library" : (Path.GetFileName(folderPath) ?? "folder");
                if (progress != null) progress(0, fileList.Count, "Queued " + fileList.Count + " media file(s) for " + scopeLabel + " scan.");
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    foreach (var stale in index.Keys.Where(key => !targetSet.Contains(key) || !fileSystem.FileExists(key)).ToList())
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
                        var underScope = LibraryPlacementService.PathsEqualNormalized(fileDirectory, folderPath)
                            || LibraryPlacementService.IsDirectoryWithinCanonicalStorage(fileDirectory, folderPath);
                        if (!underScope) return false;
                        return !targetSet.Contains(key) || !fileSystem.FileExists(key);
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
                    var stamp = host.BuildLibraryMetadataStamp(file);
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
                var scanWorkerCount = host.GetLibraryScanWorkerCount(batches.Count, string.IsNullOrWhiteSpace(folderPath) ? root : folderPath);
                if (batches.Count > 0)
                {
                    host.LogLibraryScan("Running library metadata scan with " + scanWorkerCount + " worker(s) across " + batches.Count + " ExifTool read batch(es) for " + pendingFiles.Count + " changed file(s).");
                }

                try
                {
                    Parallel.ForEach(
                        batches,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = scanWorkerCount,
                            CancellationToken = cancellationToken
                        },
                        batch =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (progress != null) progress(unchanged, fileList.Count, "Reading embedded metadata in batch " + batch.Item1 + " of " + batchCount + " (" + batch.Item2.Length + " file(s)).");
                            var batchMetadata = metadataService.ReadEmbeddedMetadataBatch(batch.Item2, cancellationToken);
                            foreach (var file in batch.Item2)
                            {
                                EmbeddedMetadataSnapshot snapshot;
                                if (!batchMetadata.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                                batchMetadataByFile[file] = snapshot;
                            }
                        });
                }
                catch (OperationCanceledException)
                {
                    throw;
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
                    var rebuiltEntry = host.BuildResolvedLibraryMetadataIndexEntry(root, file, pendingStamps[file], snapshot, existingEntry, index, gameRows);
                    index[file] = rebuiltEntry;
                    host.SetCachedFileTagsForLibraryScan(file, host.ParseTagText(rebuiltEntry.TagText), host.MetadataCacheStamp(file));
                    updated++;
                    processed++;
                    var remaining = fileList.Count - (unchanged + processed);
                    if (progress != null) progress(unchanged + processed, fileList.Count, "Indexed " + (unchanged + processed) + " of " + fileList.Count + " | " + remaining + " remaining | " + file);
                }

                host.SaveLibraryMetadataIndex(root, index);
                updatedResult = updated;
                fileListCountAfterSave = fileList.Count;
                summaryAfterSave = string.IsNullOrWhiteSpace(folderPath)
                    ? "Library metadata index scan complete: updated " + updated + ", unchanged " + unchanged + ", removed " + removed + "."
                    : "Library folder scan complete for " + Path.GetFileName(folderPath) + ": updated " + updated + ", unchanged " + unchanged + ", removed " + removed + ".";
            }

            RebuildLibraryFolderCache(root, null);
            host.LogLibraryScan(summaryAfterSave);
            if (progress != null) progress(fileListCountAfterSave, fileListCountAfterSave, summaryAfterSave);
            return updatedResult;
        }

        public Task<int> ScanLibraryMetadataIndexAsync(
            string root,
            string folderPath,
            bool forceRescan,
            Action<int, int, string> progress,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ScanLibraryMetadataIndex(root, folderPath, forceRescan, progress, cancellationToken), cancellationToken);
        }

        public void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            var savedMetadataIndex = false;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && fileSystem.FileExists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (fileList.Count == 0) return;
                var index = host.LoadLibraryMetadataIndex(root, true);
                var gameRows = host.LoadSavedGameIndexRows(root);
                var metadataByFile = metadataService.ReadEmbeddedMetadataBatch(fileList, CancellationToken.None);
                foreach (var file in fileList)
                {
                    EmbeddedMetadataSnapshot snapshot;
                    if (!metadataByFile.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                    var stamp = host.BuildLibraryMetadataStamp(file);
                    LibraryMetadataIndexEntry existingEntry;
                    if (!index.TryGetValue(file, out existingEntry)) existingEntry = null;
                    var rebuiltEntry = host.BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, existingEntry, index, gameRows);
                    index[file] = rebuiltEntry;
                    host.SetCachedFileTagsForLibraryScan(file, host.ParseTagText(rebuiltEntry.TagText), host.MetadataCacheStamp(file));
                }

                host.SaveLibraryMetadataIndex(root, index);
                savedMetadataIndex = true;
            }

            if (savedMetadataIndex) RebuildLibraryFolderCache(root, null);
        }

        public void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items, string root)
        {
            var savedMetadataIndex = false;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var itemList = (items ?? Enumerable.Empty<ManualMetadataItem>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && fileSystem.FileExists(item.FilePath))
                    .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();
                if (itemList.Count == 0) return;
                var index = host.LoadLibraryMetadataIndex(root, true);
                var gameRows = host.LoadSavedGameIndexRows(root);
                foreach (var item in itemList)
                {
                    var tags = host.BuildManualMetadataTagsForIndexUpsert(item);
                    var platformLabel = host.DetermineConsoleLabelFromTags(tags);
                    var preferredGameId = host.ManualMetadataChangesGroupingIdentity(item) ? string.Empty : item.GameId;
                    var resolvedRow = host.ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, platformLabel, preferredGameId);
                    item.GameId = resolvedRow == null ? string.Empty : resolvedRow.GameId;
                    if (resolvedRow != null && !string.IsNullOrWhiteSpace(resolvedRow.Name)) item.GameName = resolvedRow.Name;
                    LibraryMetadataIndexEntry priorEntry;
                    index.TryGetValue(item.FilePath, out priorEntry);
                    index[item.FilePath] = new LibraryMetadataIndexEntry
                    {
                        FilePath = item.FilePath,
                        Stamp = host.BuildLibraryMetadataStamp(item.FilePath),
                        GameId = item.GameId,
                        ConsoleLabel = platformLabel,
                        TagText = string.Join(", ", tags),
                        CaptureUtcTicks = host.ToCaptureUtcTicks(item.CaptureTime),
                        Starred = priorEntry != null && priorEntry.Starred,
                        IndexAddedUtcTicks = priorEntry != null && priorEntry.IndexAddedUtcTicks > 0
                            ? priorEntry.IndexAddedUtcTicks
                            : DateTime.UtcNow.Ticks,
                        RetroAchievementsGameId = priorEntry != null ? (priorEntry.RetroAchievementsGameId ?? string.Empty) : string.Empty
                    };
                    host.SetCachedFileTagsForLibraryScan(item.FilePath, tags, host.MetadataCacheStamp(item.FilePath));
                }

                host.SaveLibraryMetadataIndex(root, index);
                savedMetadataIndex = true;
            }

            if (savedMetadataIndex) RebuildLibraryFolderCache(root, null);
        }

        public void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            var rebuildFolderCache = false;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (fileList.Count == 0) return;
                var touchedDirectories = new HashSet<string>(
                    fileList
                        .Select(file => Path.GetDirectoryName(file) ?? string.Empty)
                        .Where(path => !string.IsNullOrWhiteSpace(path)),
                    StringComparer.OrdinalIgnoreCase);
                var index = host.LoadLibraryMetadataIndex(root, true);
                var changed = false;
                foreach (var file in fileList)
                {
                    if (index.Remove(file)) changed = true;
                }

                host.RemoveCachedFileTagEntries(fileList);
                if (changed)
                {
                    host.SaveLibraryMetadataIndex(root, index);
                    rebuildFolderCache = true;
                    host.RemoveCachedImageEntries(fileList);
                    host.RemoveCachedFolderListings(touchedDirectories);
                }
            }

            if (rebuildFolderCache) RebuildLibraryFolderCache(root, null);
        }

        public void SavePhotoIndexEditorRows(string root, IEnumerable<PhotoIndexEditorRow> rows, IEnumerable<string> removedPaths = null)
        {
            List<string> rehomeAfterGameIdChange = null;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
                    throw new InvalidOperationException("Library folder is not set or no longer exists. Check Settings, then try saving the photo index again.");
                var rowList = (rows ?? Enumerable.Empty<PhotoIndexEditorRow>())
                    .Where(row => row != null && !string.IsNullOrWhiteSpace(row.FilePath))
                    .GroupBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();
                var missingGameId = rowList.FirstOrDefault(row => string.IsNullOrWhiteSpace(host.NormalizeGameId(row.GameId)));
                if (missingGameId != null) throw new InvalidOperationException("Each photo index row needs a Game ID before saving. Missing: " + Path.GetFileName(missingGameId.FilePath));

                var existingSnapshot = host.LoadLibraryMetadataIndex(root, true);
                var index = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in existingSnapshot)
                {
                    if (kv.Value == null || string.IsNullOrWhiteSpace(kv.Key)) continue;
                    index[kv.Key] = kv.Value;
                }
                foreach (var path in removedPaths ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    index.Remove(path.Trim());
                }
                var ratingWrites = new List<ExifWriteRequest>();
                rehomeAfterGameIdChange = new List<string>();
                foreach (var row in rowList)
                {
                    var normalizedTags = string.Join(", ", host.ParseTagText(row.TagText));
                    var normalizedConsole = host.NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.ConsoleLabel) ? host.DetermineConsoleLabelFromTags(host.ParseTagText(normalizedTags)) : row.ConsoleLabel);
                    var stamp = host.BuildLibraryMetadataStamp(row.FilePath);
                    LibraryMetadataIndexEntry existingEntry;
                    if (!existingSnapshot.TryGetValue(row.FilePath, out existingEntry)) existingEntry = null;
                    var oldGid = existingEntry == null ? string.Empty : host.NormalizeGameId(existingEntry.GameId);
                    var newGid = host.NormalizeGameId(row.GameId);
                    if (LibraryRehomeRules.PhotoIndexGameIdChangedForRehome(oldGid, newGid)) rehomeAfterGameIdChange.Add(row.FilePath);
                    var hadStarred = existingEntry != null && existingEntry.Starred;
                    if (row.Starred != hadStarred && fileSystem.FileExists(row.FilePath))
                    {
                        var args = metadataService.BuildStarRatingExifArgs(row.FilePath, row.Starred);
                        if (args != null && args.Length > 0)
                        {
                            ratingWrites.Add(new ExifWriteRequest
                            {
                                FilePath = row.FilePath,
                                FileName = Path.GetFileName(row.FilePath),
                                Arguments = args,
                                RestoreFileTimes = false,
                                OriginalCreateTime = DateTime.MinValue,
                                OriginalWriteTime = DateTime.MinValue,
                                SuccessDetail = "XMP star rating"
                            });
                        }
                    }
                    index[row.FilePath] = new LibraryMetadataIndexEntry
                    {
                        FilePath = row.FilePath,
                        Stamp = stamp,
                        GameId = host.NormalizeGameId(row.GameId),
                        ConsoleLabel = normalizedConsole,
                        TagText = normalizedTags,
                        CaptureUtcTicks = host.ResolveLibraryMetadataCaptureUtcTicks(row.FilePath, stamp, null, existingEntry),
                        Starred = row.Starred,
                        IndexAddedUtcTicks = existingEntry != null && existingEntry.IndexAddedUtcTicks > 0
                            ? existingEntry.IndexAddedUtcTicks
                            : DateTime.UtcNow.Ticks,
                        RetroAchievementsGameId = MainWindow.CleanTag(row.RetroAchievementsGameId ?? string.Empty)
                    };
                }

                if (ratingWrites.Count > 0)
                {
                    host.EnsureExifTool();
                    metadataService.RunExifToolBatch(ratingWrites);
                    foreach (var write in ratingWrites)
                    {
                        LibraryMetadataIndexEntry entry;
                        if (!index.TryGetValue(write.FilePath, out entry) || entry == null) continue;
                        if (!fileSystem.FileExists(entry.FilePath)) continue;
                        entry.Stamp = host.BuildLibraryMetadataStamp(entry.FilePath);
                    }
                }

                var gameRows = host.LoadSavedGameIndexRows(root);
                foreach (var group in index.Values.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.GameId)).GroupBy(entry => host.NormalizeGameId(entry.GameId), StringComparer.OrdinalIgnoreCase))
                {
                    var first = group.First();
                    var row = host.EnsureGameIndexRowForAssignment(gameRows, host.GuessGameIndexNameForFile(first.FilePath), first.ConsoleLabel, group.Key);
                    if (row == null) continue;
                    var filePaths = group.Select(entry => entry.FilePath).Where(fileSystem.FileExists).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
                    row.FileCount = filePaths.Length;
                    row.FilePaths = filePaths;
                    row.PreviewImagePath = filePaths.FirstOrDefault(host.IsLibraryImageFile) ?? filePaths.FirstOrDefault() ?? string.Empty;
                    row.FolderPath = filePaths
                        .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
                        .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(pathGroup => pathGroup.Count())
                        .ThenBy(pathGroup => pathGroup.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(pathGroup => pathGroup.Key)
                        .FirstOrDefault() ?? string.Empty;
                    row.PlatformLabel = host.NormalizeConsoleLabel(first.ConsoleLabel);
                }

                host.SaveSavedGameIndexRows(root, gameRows);
                host.SaveLibraryMetadataIndex(root, index);
            }

            RebuildLibraryFolderCache(root, null);
            if (rehomeAfterGameIdChange != null && rehomeAfterGameIdChange.Count > 0)
            {
                var moved = host.RehomeLibraryCapturesTowardCanonicalFolders(root, rehomeAfterGameIdChange);
                if (moved > 0) RebuildLibraryFolderCache(root, null);
            }
        }

        public List<PhotoIndexEditorRow> LoadPhotoIndexEditorRows(string root)
        {
            return host.LoadLibraryMetadataIndex(root, true)
                .Values
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath))
                .Select(entry => new PhotoIndexEditorRow
                {
                    FilePath = entry.FilePath ?? string.Empty,
                    Stamp = entry.Stamp ?? string.Empty,
                    GameId = host.NormalizeGameId(entry.GameId),
                    RetroAchievementsGameId = MainWindow.CleanTag(entry.RetroAchievementsGameId ?? string.Empty),
                    ConsoleLabel = host.NormalizeConsoleLabel(entry.ConsoleLabel),
                    TagText = entry.TagText ?? string.Empty,
                    Starred = entry.Starred,
                    IndexAddedUtcTicks = entry.IndexAddedUtcTicks
                })
                .OrderBy(row => row.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<LibraryFolderInfo> LoadLibraryFolders(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            return LoadLibraryFoldersCore(root, index, null);
        }

        /// <summary>
        /// Builds folder-cache rows grouped by photo-index <c>GameId</c> (not by directory). <see cref="LibraryFolderInfo.FolderPath"/> is observed placement
        /// (majority parent of assigned files, or game-index path when set)—never used here to infer game title (LIBST Step 4).
        /// </summary>
        List<LibraryFolderInfo> LoadLibraryFoldersCore(string root, Dictionary<string, LibraryMetadataIndexEntry> index, List<string> precomputedOneLevelMediaFilesOrNull)
        {
            var list = new List<LibraryFolderInfo>();
            if (index == null) index = host.LoadLibraryMetadataIndex(root);
            var gameRows = host.LoadSavedGameIndexRows(root);
            bool indexChanged = false;
            bool gameRowsChanged = false;
            List<string> allFiles;
            if (precomputedOneLevelMediaFilesOrNull == null)
            {
                allFiles = fileSystem.EnumerateDirectories(root)
                    .SelectMany(dir => fileSystem.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(host.IsLibraryMediaFile))
                    .Where(fileSystem.FileExists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                allFiles = precomputedOneLevelMediaFilesOrNull
                    .Where(path => !string.IsNullOrWhiteSpace(path) && fileSystem.FileExists(path) && host.IsLibraryMediaFile(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            var missingOrIncompleteFiles = allFiles
                .Where(file =>
                {
                    LibraryMetadataIndexEntry entry;
                    return !index.TryGetValue(file, out entry) || entry == null || entry.CaptureUtcTicks <= 0;
                })
                .ToList();
            var metadataByFile = metadataService.ReadEmbeddedMetadataBatch(missingOrIncompleteFiles, CancellationToken.None);
            foreach (var file in allFiles)
            {
                LibraryMetadataIndexEntry entry;
                if (!index.TryGetValue(file, out entry) || entry == null || entry.CaptureUtcTicks <= 0)
                {
                    EmbeddedMetadataSnapshot snapshot;
                    if (!metadataByFile.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                    var stamp = host.BuildLibraryMetadataStamp(file);
                    var previousGameId = entry == null ? string.Empty : host.NormalizeGameId(entry.GameId);
                    var previousConsole = entry == null ? string.Empty : host.NormalizeConsoleLabel(entry.ConsoleLabel);
                    var rebuiltEntry = host.BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, entry, index, gameRows);
                    index[file] = rebuiltEntry;
                    entry = rebuiltEntry;
                    host.SetCachedFileTagsForLibraryScan(file, host.ParseTagText(rebuiltEntry.TagText), host.MetadataCacheStamp(file));
                    indexChanged = true;
                    if (!string.Equals(previousGameId, host.NormalizeGameId(rebuiltEntry.GameId), StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(previousConsole, host.NormalizeConsoleLabel(rebuiltEntry.ConsoleLabel), StringComparison.OrdinalIgnoreCase))
                    {
                        gameRowsChanged = true;
                    }
                }
                else if (string.IsNullOrWhiteSpace(entry.GameId))
                {
                    var tags = host.ParseTagText(entry.TagText);
                    var platformLabel = string.IsNullOrWhiteSpace(entry.ConsoleLabel)
                        ? host.NormalizeConsoleLabel(host.DetermineConsoleLabelFromTags(tags))
                        : host.NormalizeConsoleLabel(entry.ConsoleLabel);
                    entry.GameId = host.ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, null);
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
                .GroupBy(item => host.NormalizeGameId(item.Entry.GameId), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var group in groupedFiles)
            {
                var groupFiles = group.Select(item => item.File).OrderByDescending(file => host.ResolveIndexedLibraryDate(root, file, index)).ThenBy(Path.GetFileName).ToArray();
                var saved = host.FindSavedGameIndexRowById(gameRows, group.Key);
                var preferredFolderPath = groupFiles
                    .Select(file => Path.GetDirectoryName(file) ?? string.Empty)
                    .GroupBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(pathGroup => pathGroup.Count())
                    .ThenBy(pathGroup => pathGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pathGroup => pathGroup.Key)
                    .FirstOrDefault();
                var platformLabel = saved == null
                    ? host.DetermineFolderPlatformForFiles(groupFiles.ToList(), index)
                    : host.NormalizeConsoleLabel(saved.PlatformLabel);
                long newestCaptureUtcTicks = 0;
                long newestRecentSortUtcTicks = 0;
                if (groupFiles.Length > 0)
                {
                    LibraryMetadataIndexEntry newestEntry;
                    if (index.TryGetValue(groupFiles[0], out newestEntry) && newestEntry != null)
                    {
                        newestCaptureUtcTicks = newestEntry.CaptureUtcTicks;
                    }

                    if (newestCaptureUtcTicks <= 0)
                    {
                        newestCaptureUtcTicks = host.ToCaptureUtcTicks(host.ResolveIndexedLibraryDate(root, groupFiles[0], index));
                    }

                    foreach (var file in groupFiles)
                    {
                        var r = host.ResolveLibraryFileRecentSortUtcTicks(root, file, index);
                        if (r > newestRecentSortUtcTicks) newestRecentSortUtcTicks = r;
                    }
                }

                list.Add(new LibraryFolderInfo
                {
                    GameId = group.Key,
                    Name = saved == null ? host.GuessGameIndexNameForFile(groupFiles[0]) : saved.Name,
                    FolderPath = string.IsNullOrWhiteSpace(saved == null ? string.Empty : saved.FolderPath) ? preferredFolderPath : saved.FolderPath,
                    FileCount = groupFiles.Length,
                    PreviewImagePath = groupFiles.FirstOrDefault(host.IsLibraryImageFile) ?? groupFiles.FirstOrDefault(),
                    PlatformLabel = platformLabel,
                    FilePaths = groupFiles,
                    NewestCaptureUtcTicks = newestCaptureUtcTicks,
                    NewestRecentSortUtcTicks = newestRecentSortUtcTicks,
                    SteamAppId = saved != null && (saved.SuppressSteamAppIdAutoResolve || !string.IsNullOrWhiteSpace(saved.SteamAppId))
                        ? (saved.SteamAppId ?? string.Empty)
                        : host.ResolveLibraryFolderSteamAppId(platformLabel, groupFiles),
                    NonSteamId = saved == null ? string.Empty : (saved.NonSteamId ?? string.Empty),
                    SteamGridDbId = saved == null ? string.Empty : (saved.SteamGridDbId ?? string.Empty),
                    RetroAchievementsGameId = saved == null ? string.Empty : (saved.RetroAchievementsGameId ?? string.Empty),
                    SuppressSteamAppIdAutoResolve = saved != null && saved.SuppressSteamAppIdAutoResolve,
                    SuppressSteamGridDbIdAutoResolve = saved != null && saved.SuppressSteamGridDbIdAutoResolve,
                    StorageGroupId = saved == null ? string.Empty : (saved.StorageGroupId ?? string.Empty)
                });
            }

            AppendUnassignedGameIdLibraryFolders(root, allFiles, index, list);

            gameRowsChanged = host.SyncGameIndexRowsFromLibraryFolders(gameRows, list) || gameRowsChanged;
            gameRowsChanged = host.PruneObsoleteMultipleTagsRows(gameRows) || gameRowsChanged;
            if (gameRowsChanged) host.SaveSavedGameIndexRows(root, gameRows);
            if (indexChanged) host.SaveLibraryMetadataIndex(root, index);
            return list;
        }

        public void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (folderCacheRebuildHook != null)
            {
                folderCacheRebuildHook(root, index);
                return;
            }
            host.LibraryFolderCacheRwLock.EnterWriteLock();
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root))
                {
                    host.ClearLibraryFolderCache(root);
                    return;
                }

                var indexSnapshot = index ?? host.LoadLibraryMetadataIndex(root, true);
                var stopwatch = Stopwatch.StartNew();
                host.LogLibraryScan("Rebuilding library folder cache.");
                var fresh = LoadLibraryFoldersCore(root, indexSnapshot, null);
                host.ApplySavedGameIndexRows(root, fresh);
                host.SaveLibraryFolderCache(root, host.BuildLibraryFolderInventoryStamp(root), fresh);
                stopwatch.Stop();
                host.LogLibraryScan("Library folder cache rebuild complete in " + stopwatch.ElapsedMilliseconds + " ms for " + fresh.Count + " folder(s).");
            }
            finally
            {
                host.LibraryFolderCacheRwLock.ExitWriteLock();
            }
        }

        public void RefreshFolderCacheAfterGameIndexChange(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root)) return;
            RebuildLibraryFolderCache(root, null);
        }

        public List<LibraryFolderInfo> LoadLibraryFoldersCached(string root, bool forceRefresh)
        {
            if (string.IsNullOrWhiteSpace(root)) return new List<LibraryFolderInfo>();

            var rw = host.LibraryFolderCacheRwLock;
            if (!forceRefresh)
            {
                rw.EnterReadLock();
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var stamp = host.BuildLibraryFolderInventoryStamp(root);
                    var cached = host.LoadLibraryFolderCache(root, stamp);
                    if (cached != null)
                    {
                        var cacheUpdated = host.PopulateMissingLibraryFolderSortKeys(cached);
                        if (host.ApplySavedGameIndexRows(root, cached)) cacheUpdated = true;
                        if (!cacheUpdated)
                        {
                            host.LogLibraryScan("Library folder cache hit.");
                            stopwatch.Stop();
                            host.LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=hit; folders=" + cached.Count + "; forceRefresh=" + forceRefresh, 40);
                            return cached;
                        }
                    }
                }
                finally
                {
                    rw.ExitReadLock();
                }
            }

            rw.EnterWriteLock();
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var stamp = host.BuildLibraryFolderInventoryStamp(root);
                if (!forceRefresh)
                {
                    var cached = host.LoadLibraryFolderCache(root, stamp);
                    if (cached != null)
                    {
                        var cacheUpdated = host.PopulateMissingLibraryFolderSortKeys(cached);
                        if (host.ApplySavedGameIndexRows(root, cached)) cacheUpdated = true;
                        if (cacheUpdated) host.SaveLibraryFolderCache(root, stamp, cached);
                        host.LogLibraryScan("Library folder cache hit.");
                        stopwatch.Stop();
                        host.LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=hit; folders=" + cached.Count + "; forceRefresh=" + forceRefresh, 40);
                        return cached;
                    }
                }

                List<LibraryFolderInfo> fresh;
                if (!forceRefresh && host.TryGetIndexOnlyFolderCacheRefresh(root, stamp, out var indexOnlyFiles))
                {
                    host.LogLibraryScan("Library folder cache index-only refresh (metadata index revision matches; child-folder mtimes changed).");
                    fresh = LoadLibraryFoldersCore(root, null, indexOnlyFiles);
                    host.ApplySavedGameIndexRows(root, fresh);
                    host.SaveLibraryFolderCache(root, stamp, fresh);
                    stopwatch.Stop();
                    host.LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=indexOnlyRefresh; folders=" + fresh.Count + "; forceRefresh=" + forceRefresh, 40);
                    return fresh;
                }

                host.LogLibraryScan("Refreshing library folder cache.");
                fresh = LoadLibraryFoldersCore(root, null, null);
                host.ApplySavedGameIndexRows(root, fresh);
                host.SaveLibraryFolderCache(root, stamp, fresh);
                stopwatch.Stop();
                host.LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=rebuild; folders=" + fresh.Count + "; forceRefresh=" + forceRefresh, 40);
                return fresh;
            }
            finally
            {
                rw.ExitWriteLock();
            }
        }

        public List<LibraryFolderInfo> EnsureGameIndexFolderContext(string root, Action<string> setUiStatus)
        {
            var folders = LoadLibraryFoldersCached(root, false);
            if (folders == null || folders.Count == 0)
            {
                setUiStatus?.Invoke("Building game index");
                host.LogLibraryScan("Game index cache missing or stale. Rebuilding it before editing.");
                folders = LoadLibraryFoldersCached(root, true);
            }
            return folders;
        }

        /// <summary>One browse row per directory that has indexed media but no resolved <c>GameId</c> (LIBST unresolved surface).</summary>
        void AppendUnassignedGameIdLibraryFolders(
            string root,
            IReadOnlyList<string> allFiles,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<LibraryFolderInfo> list)
        {
            if (string.IsNullOrWhiteSpace(root) || allFiles == null || index == null || list == null) return;
            var orphans = allFiles
                .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file))
                .Select(file => new { File = file, Entry = index.TryGetValue(file, out var e) ? e : null })
                .Where(x => x.Entry != null && string.IsNullOrWhiteSpace(host.NormalizeGameId(x.Entry.GameId)))
                .ToList();
            if (orphans.Count == 0) return;

            foreach (var g in orphans.GroupBy(x => Path.GetDirectoryName(x.File) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var dir = g.Key;
                if (string.IsNullOrWhiteSpace(dir)) continue;

                var groupFiles = g.Select(x => x.File)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(file => host.ResolveIndexedLibraryDate(root, file, index))
                    .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (groupFiles.Length == 0) continue;

                var platformLabel = host.DetermineFolderPlatformForFiles(groupFiles.ToList(), index);
                long newestCaptureUtcTicks = 0;
                long newestRecentSortUtcTicks = 0;
                LibraryMetadataIndexEntry newestEntry;
                if (index.TryGetValue(groupFiles[0], out newestEntry) && newestEntry != null)
                    newestCaptureUtcTicks = newestEntry.CaptureUtcTicks;
                if (newestCaptureUtcTicks <= 0)
                    newestCaptureUtcTicks = host.ToCaptureUtcTicks(host.ResolveIndexedLibraryDate(root, groupFiles[0], index));
                foreach (var file in groupFiles)
                {
                    var r = host.ResolveLibraryFileRecentSortUtcTicks(root, file, index);
                    if (r > newestRecentSortUtcTicks) newestRecentSortUtcTicks = r;
                }

                var leaf = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(leaf)) leaf = "folder";

                list.Add(new LibraryFolderInfo
                {
                    GameId = string.Empty,
                    Name = "Needs assignment · " + leaf,
                    FolderPath = dir,
                    FileCount = groupFiles.Length,
                    PreviewImagePath = groupFiles.FirstOrDefault(host.IsLibraryImageFile) ?? groupFiles.FirstOrDefault(),
                    PlatformLabel = platformLabel,
                    FilePaths = groupFiles,
                    NewestCaptureUtcTicks = newestCaptureUtcTicks,
                    NewestRecentSortUtcTicks = newestRecentSortUtcTicks,
                    SteamAppId = string.Empty,
                    NonSteamId = string.Empty,
                    SteamGridDbId = string.Empty,
                    RetroAchievementsGameId = string.Empty,
                    PendingGameAssignment = true,
                    StorageGroupId = string.Empty
                });
            }
        }
    }

    /// <summary>Pure rules for Step 7–8 re-home triggers (unit-tested).</summary>
    internal static class LibraryRehomeRules
    {
        internal static bool PhotoIndexGameIdChangedForRehome(string previousNormalizedGameId, string nextNormalizedGameId)
        {
            return !string.Equals(
                previousNormalizedGameId ?? string.Empty,
                nextNormalizedGameId ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
