using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        LibraryMetadataIndexEntry CloneLibraryMetadataIndexEntry(LibraryMetadataIndexEntry entry)
        {
            if (entry == null) return null;
            return new LibraryMetadataIndexEntry
            {
                FilePath = entry.FilePath,
                Stamp = entry.Stamp,
                GameId = NormalizeGameId(entry.GameId),
                ConsoleLabel = entry.ConsoleLabel,
                TagText = entry.TagText,
                CaptureUtcTicks = entry.CaptureUtcTicks,
                Starred = entry.Starred,
                IndexAddedUtcTicks = entry.IndexAddedUtcTicks,
                RetroAchievementsGameId = entry.RetroAchievementsGameId ?? string.Empty
            };
        }

        Dictionary<string, LibraryMetadataIndexEntry> CloneLibraryMetadataIndexEntries(IEnumerable<KeyValuePair<string, LibraryMetadataIndexEntry>> entries)
        {
            var clone = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in entries ?? Enumerable.Empty<KeyValuePair<string, LibraryMetadataIndexEntry>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                clone[pair.Key] = CloneLibraryMetadataIndexEntry(pair.Value);
            }
            return clone;
        }

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexForFilePaths(string root, IEnumerable<string> filePaths)
        {
            if (string.IsNullOrWhiteSpace(root)) return new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            return indexPersistenceService.LoadLibraryMetadataIndexEntriesForFilePaths(root, filePaths);
        }

        /// <summary>Persist metadata index rows with SQLite UPSERT and drop the in-memory cache so the next full load reflects disk (avoids treating a partial merge as complete).</summary>
        void MergePersistLibraryMetadataIndexEntries(string root, IEnumerable<LibraryMetadataIndexEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(root) || entries == null) return;
            var list = entries.Where(e => e != null && !string.IsNullOrWhiteSpace(e.FilePath)).ToList();
            if (list.Count == 0) return;
            indexPersistenceService.UpsertLibraryMetadataIndexEntries(root, list);
            lock (libraryMetadataIndexSync)
            {
                libraryMetadataIndex.Clear();
                libraryMetadataIndexRoot = root;
            }
        }

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

        string[] MergeLibraryMetadataTagsWithFilenameHint(string root, string file, string[] tags)
        {
            return MergePlatformTagsWithFilenamePlatformHint(tags, ParseFilename(file, root));
        }

        string ResolveStoredLibraryMetadataConsoleLabel(LibraryMetadataIndexEntry existingEntry, IEnumerable<string> tags)
        {
            var storedLabel = NormalizeConsoleLabel(existingEntry == null ? string.Empty : existingEntry.ConsoleLabel);
            if (ConsoleLabelBlocksFilenameFallback(storedLabel))
            {
                return storedLabel;
            }
            return NormalizeConsoleLabel(DetermineConsoleLabelFromTags(tags));
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

        /// <summary>When <see cref="EmbeddedMetadataSnapshot.Rating"/> is present (XMP 0–5), sync index star from embedded: 5 stars ⇒ starred. Otherwise keep the existing index flag.</summary>
        bool ResolveLibraryMetadataStarred(EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry)
        {
            if (snapshot != null && snapshot.Rating.HasValue) return snapshot.Rating.Value >= 5;
            return existingEntry != null && existingEntry.Starred;
        }

        LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(string root, string file, string stamp, EmbeddedMetadataSnapshot snapshot, LibraryMetadataIndexEntry existingEntry, Dictionary<string, LibraryMetadataIndexEntry> index, List<GameIndexEditorRow> gameRows)
        {
            var tags = ResolveLibraryMetadataTags(snapshot, existingEntry);
            tags = MergeLibraryMetadataTagsWithFilenameHint(root, file, tags);
            var platformLabel = ResolveStoredLibraryMetadataConsoleLabel(existingEntry, tags);
            var preferredGameId = NormalizeGameId(existingEntry == null ? string.Empty : existingEntry.GameId);
            var resolvedGameId = !string.IsNullOrWhiteSpace(preferredGameId)
                ? preferredGameId
                : ResolveGameIdForIndexedFile(root, file, platformLabel, tags, index, gameRows, preferredGameId);
            return new LibraryMetadataIndexEntry
            {
                FilePath = file,
                Stamp = stamp,
                GameId = resolvedGameId,
                ConsoleLabel = platformLabel,
                TagText = string.Join(", ", tags),
                CaptureUtcTicks = ResolveLibraryMetadataCaptureUtcTicks(file, stamp, snapshot, existingEntry),
                Starred = ResolveLibraryMetadataStarred(snapshot, existingEntry),
                IndexAddedUtcTicks = existingEntry != null && existingEntry.IndexAddedUtcTicks > 0
                    ? existingEntry.IndexAddedUtcTicks
                    : DateTime.UtcNow.Ticks,
                RetroAchievementsGameId = existingEntry != null ? CleanTag(existingEntry.RetroAchievementsGameId ?? string.Empty) : string.Empty
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

        /// <summary>UTC ticks for "recently added" ordering: <see cref="LibraryMetadataIndexEntry.IndexAddedUtcTicks"/> when set, otherwise capture ticks or resolved file date.</summary>
        long ResolveLibraryFileRecentSortUtcTicks(string root, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return 0;
            LibraryMetadataIndexEntry entry = null;
            if (index != null) index.TryGetValue(file, out entry);
            if (entry == null) entry = TryGetLibraryMetadataIndexEntry(root, file, index);
            if (entry != null)
            {
                if (entry.IndexAddedUtcTicks > 0) return entry.IndexAddedUtcTicks;
                if (entry.CaptureUtcTicks > 0) return entry.CaptureUtcTicks;
            }
            var d = ResolveIndexedLibraryDate(root, file, index);
            if (d <= DateTime.MinValue) return 0;
            try
            {
                return d.ToUniversalTime().Ticks;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Must be called with <see cref="libraryMetadataIndexSync"/> held. Populates the in-memory metadata cache for <paramref name="root"/> when stale or <paramref name="forceDiskReload"/>.</summary>
        void EnsureLibraryMetadataIndexCacheForRoot(string root, bool forceDiskReload)
        {
            if (!forceDiskReload
                && string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase)
                && libraryMetadataIndex.Count > 0)
            {
                return;
            }

            libraryMetadataIndex.Clear();
            libraryMetadataIndexRoot = root;
            foreach (var pair in indexPersistenceService.LoadLibraryMetadataIndexEntries(root))
            {
                libraryMetadataIndex[pair.Key] = CloneLibraryMetadataIndexEntry(pair.Value);
            }
        }

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root, bool forceDiskReload = false)
        {
            lock (libraryMetadataIndexSync)
            {
                EnsureLibraryMetadataIndexCacheForRoot(root, forceDiskReload);
                return CloneLibraryMetadataIndexEntries(libraryMetadataIndex);
            }
        }

        /// <summary>Reads starred state from the in-memory index cache without cloning the full dictionary (hot path: one lookup per gallery tile).</summary>
        bool TryGetLibraryMetadataStarredFromCachedIndex(string root, string filePath, out bool starred)
        {
            starred = false;
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(filePath)) return false;
            lock (libraryMetadataIndexSync)
            {
                EnsureLibraryMetadataIndexCacheForRoot(root, false);
                LibraryMetadataIndexEntry row;
                if (!libraryMetadataIndex.TryGetValue(filePath, out row) || row == null) return false;
                starred = row.Starred;
                return true;
            }
        }

        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var savedEntries = index.Values.Where(v => v != null && !string.IsNullOrWhiteSpace(v.FilePath)).OrderBy(v => v.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            indexPersistenceService.SaveLibraryMetadataIndexEntries(root, savedEntries.ToDictionary(entry => entry.FilePath, entry => entry, StringComparer.OrdinalIgnoreCase));
            lock (libraryMetadataIndexSync)
            {
                libraryMetadataIndex.Clear();
                libraryMetadataIndexRoot = root;
                foreach (var entry in savedEntries)
                {
                    libraryMetadataIndex[entry.FilePath] = CloneLibraryMetadataIndexEntry(entry);
                }
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
                if (TryGetCachedFileTags(file, stampsByFile[file], out cachedTags))
                {
                    result[file] = cachedTags;
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
                SetCachedFileTags(file, tags, stampsByFile[file]);
            }
            return result;
        }

        /// <summary>
        /// Prefer <see cref="ILibrarySession"/> for the active library (PV-PLN-UI-001 Step 3);
        /// falls back to <see cref="LoadLibraryMetadataIndex(string, bool)"/> when <paramref name="root"/> differs.
        /// </summary>
        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexViaSessionWhenActive(string root, bool forceDiskReload = false)
        {
            if (librarySession != null && librarySession.HasLibraryRoot
                && !string.IsNullOrWhiteSpace(root)
                && string.Equals(root, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                return librarySession.LoadLibraryMetadataIndex(forceDiskReload);
            return LoadLibraryMetadataIndex(root, forceDiskReload);
        }

        /// <summary>Pair with <see cref="LoadLibraryMetadataIndexViaSessionWhenActive"/> for writes on the active library.</summary>
        void SaveLibraryMetadataIndexViaSessionWhenActive(string root, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (librarySession != null && librarySession.HasLibraryRoot
                && !string.IsNullOrWhiteSpace(root)
                && string.Equals(root, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                librarySession.SaveLibraryMetadataIndex(index);
            else
                SaveLibraryMetadataIndex(root, index);
        }

        /// <summary>
        /// Prefer <see cref="ILibrarySession.LoadLibraryMetadataIndexForFilePaths"/> for the active library (PV-PLN-UI-001 Step 3);
        /// falls back to persistence when <paramref name="root"/> differs.
        /// </summary>
        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexForFilePathsViaSessionWhenActive(string root, IEnumerable<string> filePaths)
        {
            if (librarySession != null && librarySession.HasLibraryRoot
                && !string.IsNullOrWhiteSpace(root)
                && string.Equals(root, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                return librarySession.LoadLibraryMetadataIndexForFilePaths(filePaths);
            return LoadLibraryMetadataIndexForFilePaths(root, filePaths);
        }

        string MetadataSidecarPath(string file)
        {
            return IsVideo(file) ? file + ".xmp" : null;
        }

        long MetadataCacheStamp(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return 0;
            var info = new FileInfo(file);
            long stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;
            var sidecar = MetadataSidecarPath(file);
            if (!string.IsNullOrWhiteSpace(sidecar) && File.Exists(sidecar))
            {
                var sidecarInfo = new FileInfo(sidecar);
                stamp = stamp ^ sidecarInfo.LastWriteTimeUtc.Ticks ^ sidecarInfo.Length;
            }
            return stamp;
        }

        void DeleteMetadataSidecarIfPresent(string file)
        {
            var sidecar = MetadataSidecarPath(file);
            if (string.IsNullOrWhiteSpace(sidecar) || !File.Exists(sidecar)) return;
            File.Delete(sidecar);
            Log("Deleted sidecar: " + sidecar);
        }

        void AddSidecarUndoEntryIfPresent(string targetFile, string sourceDirectory, List<UndoImportEntry> entries)
        {
            var sidecar = MetadataSidecarPath(targetFile);
            if (string.IsNullOrWhiteSpace(sidecar) || !File.Exists(sidecar) || entries == null) return;
            entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(sidecar), CurrentPath = sidecar });
        }

    }
}
