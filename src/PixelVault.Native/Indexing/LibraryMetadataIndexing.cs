using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

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
                Starred = existingEntry != null && existingEntry.Starred
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
            lock (libraryMetadataIndexSync)
            {
                if (!forceDiskReload && string.Equals(libraryMetadataIndexRoot, root, StringComparison.OrdinalIgnoreCase) && libraryMetadataIndex.Count > 0)
                {
                    return CloneLibraryMetadataIndexEntries(libraryMetadataIndex);
                }
                libraryMetadataIndex.Clear();
                libraryMetadataIndexRoot = root;
                foreach (var pair in indexPersistenceService.LoadLibraryMetadataIndexEntries(root))
                {
                    libraryMetadataIndex[pair.Key] = CloneLibraryMetadataIndexEntry(pair.Value);
                }
                return CloneLibraryMetadataIndexEntries(libraryMetadataIndex);
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

    }
}
