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
        readonly ConcurrentDictionary<string, byte> queuedLibraryFolderMetadataRepairRoots =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

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
                        if (ImportService.IsHdrFallbackPath(dir)) continue;
                        targets.AddRange(fileSystem.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(host.IsLibraryMediaFile));
                    }
                }
                else
                {
                    targets.AddRange(fileSystem.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories).Where(host.IsLibraryMediaFile));
                }

                var fileList = targets
                    .Where(file => !ImportService.IsHdrFallbackPath(file))
                    .Where(fileSystem.FileExists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
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
            List<string> touchedFiles = null;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f) && fileSystem.FileExists(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (fileList.Count == 0) return;
                touchedFiles = fileList;
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

            if (savedMetadataIndex) RefreshFolderCacheForTouchedPathsOrRebuild(root, touchedFiles);
        }

        public void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items, string root)
        {
            var savedMetadataIndex = false;
            List<string> touchedFiles = null;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var itemList = (items ?? Enumerable.Empty<ManualMetadataItem>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && fileSystem.FileExists(item.FilePath))
                    .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();
                if (itemList.Count == 0) return;
                touchedFiles = itemList.Select(item => item.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

            if (savedMetadataIndex) RefreshFolderCacheForTouchedPathsOrRebuild(root, touchedFiles);
        }

        public void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root)
        {
            var rebuildFolderCache = false;
            List<string> touchedFiles = null;
            lock (host.LibraryMaintenanceSync)
            {
                if (string.IsNullOrWhiteSpace(root)) return;
                var fileList = (files ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (fileList.Count == 0) return;
                touchedFiles = fileList;
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

            if (rebuildFolderCache) RefreshFolderCacheForTouchedPathsOrRebuild(root, touchedFiles);
        }

        public void SavePhotoIndexEditorRows(string root, IEnumerable<PhotoIndexEditorRow> rows, IEnumerable<string> removedPaths = null)
        {
            List<string> rehomeAfterGameIdChange = null;
            List<string> touchedPathsAfterSave = null;
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
                var removedPathList = (removedPaths ?? Enumerable.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var path in removedPathList)
                {
                    index.Remove(path);
                }
                var ratingWrites = new List<ExifWriteRequest>();
                rehomeAfterGameIdChange = new List<string>();
                touchedPathsAfterSave = rowList
                    .Select(row => row.FilePath)
                    .Concat(removedPathList)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
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

            RefreshFolderCacheForTouchedPathsOrRebuild(root, touchedPathsAfterSave);
            if (rehomeAfterGameIdChange != null && rehomeAfterGameIdChange.Count > 0)
            {
                var moved = host.RehomeLibraryCapturesTowardCanonicalFolders(root, rehomeAfterGameIdChange);
                if (moved > 0) RefreshFolderCacheForTouchedPathsOrRebuild(root, rehomeAfterGameIdChange);
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
            List<string> repairCandidates;
            return LoadLibraryFoldersCore(root, index, null, out repairCandidates);
        }

        /// <summary>
        /// Builds folder-cache rows grouped by photo-index <c>GameId</c> (not by directory). <see cref="LibraryFolderInfo.FolderPath"/> is observed placement
        /// (majority parent of assigned files, or game-index path when set)—never used here to infer game title (LIBST Step 4).
        /// </summary>
        List<LibraryFolderInfo> LoadLibraryFoldersCore(
            string root,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<string> precomputedOneLevelMediaFilesOrNull,
            out List<string> repairCandidates)
        {
            var list = new List<LibraryFolderInfo>();
            if (index == null) index = host.LoadLibraryMetadataIndex(root);
            var gameRows = host.LoadSavedGameIndexRows(root);
            var allFiles = BuildLibraryFolderProjectionFileList(root, precomputedOneLevelMediaFilesOrNull);
            repairCandidates = FindLibraryFolderMetadataRepairCandidates(root, allFiles, index, gameRows);

            var groupedFiles = allFiles
                .Select(file => new
                {
                    File = file,
                    Entry = index.ContainsKey(file) ? index[file] : null
                })
                .Where(item => item.Entry != null
                    && !string.IsNullOrWhiteSpace(item.Entry.GameId)
                    && !LibraryFolderIndexEntryHasOrphanGameId(item.Entry, gameRows))
                .GroupBy(item => host.NormalizeGameId(item.Entry.GameId), StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var group in groupedFiles)
            {
                var row = BuildAssignedLibraryFolderInfo(root, group.Key, group.Select(item => item.File), index, gameRows);
                if (row != null) list.Add(row);
            }

            AppendUnassignedGameIdLibraryFolders(root, allFiles, index, gameRows, list);

            var gameRowsChanged = host.SyncGameIndexRowsFromLibraryFolders(gameRows, list);
            gameRowsChanged = host.PruneObsoleteMultipleTagsRows(gameRows) || gameRowsChanged;
            if (gameRowsChanged) host.SaveSavedGameIndexRows(root, gameRows);
            return list;
        }

        List<string> BuildLibraryFolderProjectionFileList(string root, List<string> precomputedOneLevelMediaFilesOrNull)
        {
            if (precomputedOneLevelMediaFilesOrNull == null)
            {
                return fileSystem.EnumerateDirectories(root)
                    .Where(dir => !ImportService.IsHdrFallbackPath(dir))
                    .SelectMany(dir => fileSystem.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories).Where(host.IsLibraryMediaFile))
                    .Where(file => !ImportService.IsHdrFallbackPath(file))
                    .Where(fileSystem.FileExists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return precomputedOneLevelMediaFilesOrNull
                .Where(path => !string.IsNullOrWhiteSpace(path) && fileSystem.FileExists(path) && host.IsLibraryMediaFile(path))
                .Where(path => !ImportService.IsHdrFallbackPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        List<string> FindLibraryFolderMetadataRepairCandidates(
            string root,
            IEnumerable<string> files,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows)
        {
            return (files ?? Enumerable.Empty<string>())
                .Where(file =>
                {
                    LibraryMetadataIndexEntry entry;
                    index.TryGetValue(file, out entry);
                    return LibraryFolderIndexEntryNeedsRepair(root, file, entry, gameRows);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        bool LibraryFolderIndexEntryNeedsRepair(string root, string file, LibraryMetadataIndexEntry entry, List<GameIndexEditorRow> gameRows)
        {
            if (entry == null || entry.CaptureUtcTicks <= 0) return true;
            if (LibraryFolderIndexEntryHasOrphanGameId(entry, gameRows)) return true;
            if (string.IsNullOrWhiteSpace(entry.GameId)) return true;
            if (host.IndexEntryShouldReResolveForNonSteamShortcutMislabel(root, file, entry)) return true;
            return host.IndexEntryShouldReResolveSteamPlatformWithoutAppId(root, file, entry, gameRows);
        }

        bool LibraryFolderIndexEntryNeedsMetadataRead(string root, string file, LibraryMetadataIndexEntry entry, List<GameIndexEditorRow> gameRows)
        {
            if (entry == null || entry.CaptureUtcTicks <= 0) return true;
            if (LibraryFolderIndexEntryHasOrphanGameId(entry, gameRows)) return true;
            if (host.IndexEntryShouldReResolveForNonSteamShortcutMislabel(root, file, entry)) return true;
            return host.IndexEntryShouldReResolveSteamPlatformWithoutAppId(root, file, entry, gameRows);
        }

        bool LibraryFolderIndexEntryHasOrphanGameId(LibraryMetadataIndexEntry entry, List<GameIndexEditorRow> gameRows)
        {
            var gameId = host.NormalizeGameId(entry == null ? string.Empty : entry.GameId);
            return !string.IsNullOrWhiteSpace(gameId)
                && host.FindSavedGameIndexRowById(gameRows, gameId) == null;
        }

        void QueueLibraryFolderMetadataRepair(string root, IEnumerable<string> candidates)
        {
            if (folderCacheRebuildHook != null) return;
            if (string.IsNullOrWhiteSpace(root)) return;
            var files = (candidates ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file) && host.IsLibraryMediaFile(file))
                .Where(file => !ImportService.IsHdrFallbackPath(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0) return;

            var key = NormalizeLibraryPath(root);
            if (string.IsNullOrWhiteSpace(key)) key = root;
            if (!queuedLibraryFolderMetadataRepairRoots.TryAdd(key, 0))
            {
                host.LogLibraryScan("Library folder metadata repair already queued for " + files.Count + " candidate(s).");
                return;
            }

            host.LogLibraryScan("Queued library folder metadata repair for " + files.Count + " candidate file(s).");
            Task.Run(() =>
            {
                try
                {
                    RunLibraryFolderMetadataRepair(root, files);
                }
                catch (Exception ex)
                {
                    host.LogLibraryScan("Library folder metadata repair failed: " + ex.Message);
                }
                finally
                {
                    byte ignored;
                    queuedLibraryFolderMetadataRepairRoots.TryRemove(key, out ignored);
                }
            });
        }

        void RunLibraryFolderMetadataRepair(string root, List<string> candidateFiles)
        {
            if (string.IsNullOrWhiteSpace(root) || candidateFiles == null || candidateFiles.Count == 0) return;
            if (!fileSystem.DirectoryExists(root)) return;

            var stopwatch = Stopwatch.StartNew();
            var repairedFiles = new List<string>();
            var indexChanged = false;
            var gameRowsChanged = false;
            lock (host.LibraryMaintenanceSync)
            {
                var index = host.LoadLibraryMetadataIndex(root, true);
                var gameRows = host.LoadSavedGameIndexRows(root);
                var repairFiles = FindLibraryFolderMetadataRepairCandidates(root, candidateFiles, index, gameRows)
                    .Where(file => fileSystem.FileExists(file))
                    .ToList();
                if (repairFiles.Count == 0) return;

                var metadataReadFiles = repairFiles
                    .Where(file =>
                    {
                        LibraryMetadataIndexEntry entry;
                        index.TryGetValue(file, out entry);
                        return LibraryFolderIndexEntryNeedsMetadataRead(root, file, entry, gameRows);
                    })
                    .ToList();
                var metadataByFile = metadataService.ReadEmbeddedMetadataBatch(metadataReadFiles, CancellationToken.None);

                foreach (var file in repairFiles)
                {
                    LibraryMetadataIndexEntry entry;
                    index.TryGetValue(file, out entry);
                    if (LibraryFolderIndexEntryNeedsMetadataRead(root, file, entry, gameRows))
                    {
                        EmbeddedMetadataSnapshot snapshot;
                        if (!metadataByFile.TryGetValue(file, out snapshot) || snapshot == null) snapshot = new EmbeddedMetadataSnapshot();
                        var stamp = host.BuildLibraryMetadataStamp(file);
                        var previousGameId = entry == null ? string.Empty : host.NormalizeGameId(entry.GameId);
                        var previousConsole = entry == null ? string.Empty : host.NormalizeConsoleLabel(entry.ConsoleLabel);
                        var rebuiltEntry = host.BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, entry, index, gameRows);
                        index[file] = rebuiltEntry;
                        host.SetCachedFileTagsForLibraryScan(file, host.ParseTagText(rebuiltEntry.TagText), host.MetadataCacheStamp(file));
                        indexChanged = true;
                        repairedFiles.Add(file);
                        if (!string.Equals(previousGameId, host.NormalizeGameId(rebuiltEntry.GameId), StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(previousConsole, host.NormalizeConsoleLabel(rebuiltEntry.ConsoleLabel), StringComparison.OrdinalIgnoreCase))
                        {
                            gameRowsChanged = true;
                        }
                    }
                    else if (entry != null && string.IsNullOrWhiteSpace(entry.GameId))
                    {
                        var tags = host.ParseTagText(entry.TagText);
                        var platformLabel = string.IsNullOrWhiteSpace(entry.ConsoleLabel)
                            ? host.NormalizeConsoleLabel(host.DetermineConsoleLabelFromTags(tags))
                            : host.NormalizeConsoleLabel(entry.ConsoleLabel);
                        entry.GameId = host.ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, null);
                        indexChanged = true;
                        gameRowsChanged = true;
                        repairedFiles.Add(file);
                    }
                }

                if (gameRowsChanged) host.SaveSavedGameIndexRows(root, gameRows);
                if (indexChanged) host.SaveLibraryMetadataIndex(root, index);
            }

            stopwatch.Stop();
            host.LogPerformanceSample(
                "LibraryFolderMetadataRepair",
                stopwatch,
                "files=" + repairedFiles.Count,
                40);
            if (repairedFiles.Count > 0)
                RefreshFolderCacheForTouchedPathsOrRebuild(root, repairedFiles);
        }

        LibraryFolderInfo BuildAssignedLibraryFolderInfo(
            string root,
            string gameId,
            IEnumerable<string> files,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows)
        {
            var groupFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file) && host.IsLibraryMediaFile(file))
                .Where(file => !ImportService.IsHdrFallbackPath(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(file => host.ResolveIndexedLibraryDate(root, file, index))
                .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (groupFiles.Length == 0) return null;

            var normalizedGameId = host.NormalizeGameId(gameId);
            var saved = host.FindSavedGameIndexRowById(gameRows, normalizedGameId);
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

            return new LibraryFolderInfo
            {
                GameId = normalizedGameId,
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
            };
        }

        LibraryFolderInfo BuildUnassignedLibraryFolderInfo(
            string root,
            string folderPath,
            IEnumerable<string> files,
            Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var groupFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file) && host.IsLibraryMediaFile(file))
                .Where(file => !ImportService.IsHdrFallbackPath(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(file => host.ResolveIndexedLibraryDate(root, file, index))
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (groupFiles.Length == 0) return null;

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

            var dir = folderPath ?? string.Empty;
            var leaf = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(leaf)) leaf = "folder";

            return new LibraryFolderInfo
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
            };
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
                List<string> repairCandidates;
                var fresh = LoadLibraryFoldersCore(root, indexSnapshot, null, out repairCandidates);
                host.ApplySavedGameIndexRows(root, fresh);
                host.SaveLibraryFolderCache(root, host.BuildLibraryFolderInventoryStamp(root), fresh);
                stopwatch.Stop();
                host.LogLibraryScan("Library folder cache rebuild complete in " + stopwatch.ElapsedMilliseconds + " ms for " + fresh.Count + " folder(s).");
                QueueLibraryFolderMetadataRepair(root, repairCandidates);
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

        void RefreshFolderCacheForTouchedPathsOrRebuild(string root, IEnumerable<string> touchedPaths)
        {
            var touchedList = (touchedPaths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (touchedList.Count > 0 && TryUpdateLibraryFolderCacheForTouchedPaths(root, touchedList))
                return;
            RebuildLibraryFolderCache(root, null);
        }

        public bool TryUpdateLibraryFolderCacheForTouchedPaths(string root, IEnumerable<string> touchedPaths)
        {
            if (string.IsNullOrWhiteSpace(root) || !fileSystem.DirectoryExists(root)) return false;
            var touched = NormalizeTouchedLibraryPaths(touchedPaths);
            if (touched.Count == 0) return false;

            host.LibraryFolderCacheRwLock.EnterWriteLock();
            try
            {
                var cached = host.LoadLibraryFolderCacheSnapshot(root, allowStaleMetadataRevision: true);
                if (cached == null || cached.Count == 0) return false;

                var stopwatch = Stopwatch.StartNew();
                var index = host.LoadLibraryMetadataIndex(root, true);
                var gameRows = host.LoadSavedGameIndexRows(root);
                var affectedGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var affectedOrphanDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var row in cached.Where(row => row != null))
                {
                    if (!LibraryFolderRowTouches(row, touched)) continue;
                    var gameId = host.NormalizeGameId(row.GameId);
                    if (string.IsNullOrWhiteSpace(gameId))
                    {
                        if (!string.IsNullOrWhiteSpace(row.FolderPath)) affectedOrphanDirs.Add(row.FolderPath);
                    }
                    else
                    {
                        affectedGameIds.Add(gameId);
                    }
                }

                foreach (var entry in index.Values.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath)))
                {
                    if (!LibraryPathTouches(entry.FilePath, touched)) continue;
                    var gameId = host.NormalizeGameId(entry.GameId);
                    if (string.IsNullOrWhiteSpace(gameId))
                    {
                        var dir = Path.GetDirectoryName(entry.FilePath) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(dir)) affectedOrphanDirs.Add(dir);
                    }
                    else
                    {
                        affectedGameIds.Add(gameId);
                    }
                }

                if (affectedGameIds.Count == 0 && affectedOrphanDirs.Count == 0) return false;

                var updated = cached
                    .Where(row => row != null)
                    .Where(row =>
                    {
                        var gameId = host.NormalizeGameId(row.GameId);
                        if (!string.IsNullOrWhiteSpace(gameId)) return !affectedGameIds.Contains(gameId);
                        return !LibraryFolderRowTouches(row, touched);
                    })
                    .ToList();

                foreach (var gameId in affectedGameIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    var files = index.Values
                        .Where(entry => entry != null && string.Equals(host.NormalizeGameId(entry.GameId), gameId, StringComparison.OrdinalIgnoreCase))
                        .Select(entry => entry.FilePath)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file) && host.IsLibraryMediaFile(file))
                        .Where(file => !ImportService.IsHdrFallbackPath(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var row = BuildAssignedLibraryFolderInfo(root, gameId, files, index, gameRows);
                    if (row != null) updated.Add(row);
                }

                foreach (var dir in affectedOrphanDirs.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    var files = index.Values
                        .Where(entry => entry != null && string.IsNullOrWhiteSpace(host.NormalizeGameId(entry.GameId)))
                        .Select(entry => entry.FilePath)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file) && host.IsLibraryMediaFile(file))
                        .Where(file => !ImportService.IsHdrFallbackPath(file))
                        .Where(file => LibraryPlacementService.PathsEqualNormalized(Path.GetDirectoryName(file) ?? string.Empty, dir))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var row = BuildUnassignedLibraryFolderInfo(root, dir, files, index);
                    if (row != null) updated.Add(row);
                }

                var gameRowsChanged = host.SyncGameIndexRowsFromLibraryFolders(gameRows, updated);
                gameRowsChanged = host.PruneObsoleteMultipleTagsRows(gameRows) || gameRowsChanged;
                if (gameRowsChanged) host.SaveSavedGameIndexRows(root, gameRows);
                host.ApplySavedGameIndexRows(root, updated);
                host.PopulateMissingLibraryFolderSortKeys(updated);
                host.SaveLibraryFolderCache(root, host.BuildLibraryFolderInventoryStamp(root), updated);
                stopwatch.Stop();
                host.LogPerformanceSample(
                    "LibraryFolderCache",
                    stopwatch,
                    "mode=incremental; touched=" + touched.Count + "; gameIds=" + affectedGameIds.Count + "; orphanDirs=" + affectedOrphanDirs.Count + "; folders=" + updated.Count,
                    0);
                host.LogLibraryScan("Library folder cache incremental update complete for " + touched.Count + " touched path(s).");
                return true;
            }
            finally
            {
                host.LibraryFolderCacheRwLock.ExitWriteLock();
            }
        }

        sealed class TouchedLibraryPath
        {
            public string Path;
            public bool IsDirectory;
        }

        List<TouchedLibraryPath> NormalizeTouchedLibraryPaths(IEnumerable<string> paths)
        {
            return (paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizeTouchedLibraryPath(path))
                .Where(path => path != null && !string.IsNullOrWhiteSpace(path.Path))
                .GroupBy(path => path.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        TouchedLibraryPath NormalizeTouchedLibraryPath(string path)
        {
            var normalized = NormalizeLibraryPath(path);
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            var isDirectory = fileSystem.DirectoryExists(normalized)
                || (!fileSystem.FileExists(normalized) && string.IsNullOrWhiteSpace(Path.GetExtension(normalized)));
            return new TouchedLibraryPath
            {
                Path = isDirectory
                    ? normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : normalized,
                IsDirectory = isDirectory
            };
        }

        static string NormalizeLibraryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        bool LibraryFolderRowTouches(LibraryFolderInfo row, IReadOnlyList<TouchedLibraryPath> touched)
        {
            if (row == null || touched == null || touched.Count == 0) return false;
            if (LibraryPathTouches(row.FolderPath, touched)) return true;
            if (LibraryPathTouches(row.PreviewImagePath, touched)) return true;
            return (row.FilePaths ?? Array.Empty<string>()).Any(file => LibraryPathTouches(file, touched));
        }

        bool LibraryPathTouches(string candidatePath, IReadOnlyList<TouchedLibraryPath> touched)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || touched == null || touched.Count == 0) return false;
            var candidate = NormalizeLibraryPath(candidatePath);
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            foreach (var item in touched)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Path)) continue;
                if (LibraryPlacementService.PathsEqualNormalized(candidate, item.Path)) return true;
                if (!item.IsDirectory) continue;
                var candidateDirectory = fileSystem.DirectoryExists(candidate)
                    ? candidate
                    : (Path.GetDirectoryName(candidate) ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(candidateDirectory)
                    && LibraryPlacementService.IsDirectoryWithinCanonicalStorage(candidateDirectory, item.Path))
                    return true;
            }

            return false;
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
                if (!forceRefresh && host.TryGetIndexOnlyFolderCacheRefresh(root, stamp, out var indexOnlyProjectionFiles))
                {
                    host.LogLibraryScan("Library folder cache index-only projection refresh (metadata index revision matches; child-folder mtimes changed; no recursive folder sweep).");
                    List<string> repairCandidates;
                    fresh = LoadLibraryFoldersCore(root, null, indexOnlyProjectionFiles, out repairCandidates);
                    host.ApplySavedGameIndexRows(root, fresh);
                    host.SaveLibraryFolderCache(root, stamp, fresh);
                    stopwatch.Stop();
                    host.LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=indexOnlyProjection; folders=" + fresh.Count + "; files=" + indexOnlyProjectionFiles.Count + "; forceRefresh=" + forceRefresh, 40);
                    QueueLibraryFolderMetadataRepair(root, repairCandidates);
                    return fresh;
                }

                host.LogLibraryScan("Refreshing library folder cache.");
                List<string> rebuildRepairCandidates;
                fresh = LoadLibraryFoldersCore(root, null, null, out rebuildRepairCandidates);
                host.ApplySavedGameIndexRows(root, fresh);
                host.SaveLibraryFolderCache(root, stamp, fresh);
                stopwatch.Stop();
                host.LogPerformanceSample("LibraryFolderCache", stopwatch, "mode=rebuild; folders=" + fresh.Count + "; forceRefresh=" + forceRefresh, 40);
                QueueLibraryFolderMetadataRepair(root, rebuildRepairCandidates);
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
            List<GameIndexEditorRow> gameRows,
            List<LibraryFolderInfo> list)
        {
            if (string.IsNullOrWhiteSpace(root) || allFiles == null || index == null || list == null) return;
            var orphans = allFiles
                .Where(file => !string.IsNullOrWhiteSpace(file) && fileSystem.FileExists(file))
                .Select(file => new { File = file, Entry = index.TryGetValue(file, out var e) ? e : null })
                .Where(x => x.Entry != null
                    && (string.IsNullOrWhiteSpace(host.NormalizeGameId(x.Entry.GameId))
                        || LibraryFolderIndexEntryHasOrphanGameId(x.Entry, gameRows)))
                .ToList();
            if (orphans.Count == 0) return;

            foreach (var g in orphans.GroupBy(x => Path.GetDirectoryName(x.File) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var dir = g.Key;
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var row = BuildUnassignedLibraryFolderInfo(root, dir, g.Select(x => x.File), index);
                if (row != null) list.Add(row);
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
