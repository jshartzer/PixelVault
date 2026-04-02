using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    internal sealed class LibraryScanner : ILibraryScanner
    {
        readonly ILibraryScanHost host;

        public LibraryScanner(ILibraryScanHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public int ScanLibraryMetadataIndex(
            string root,
            string folderPath,
            bool forceRescan,
            Action<int, int, string> progress,
            CancellationToken cancellationToken = default)
        {
            lock (host.LibraryMaintenanceSync)
            {
                host.EnsureLibraryRootExists(root);
                host.EnsureExifTool();
                var index = host.LoadLibraryMetadataIndex(root);
                var gameRows = host.LoadSavedGameIndexRows(root);
                var targets = new List<string>();
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        targets.AddRange(Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Where(host.IsLibraryMediaFile));
                    }
                }
                else
                {
                    targets.AddRange(Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly).Where(host.IsLibraryMediaFile));
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
                    Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = scanWorkerCount, CancellationToken = cancellationToken }, delegate(Tuple<int, string[]> batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (progress != null) progress(unchanged, fileList.Count, "Reading embedded metadata in batch " + batch.Item1 + " of " + batchCount + " (" + batch.Item2.Length + " file(s)).");
                        var batchMetadata = host.ReadEmbeddedMetadataBatch(batch.Item2, cancellationToken);
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
                    var rebuiltEntry = host.BuildResolvedLibraryMetadataIndexEntry(root, file, pendingStamps[file], snapshot, existingEntry, index, gameRows);
                    index[file] = rebuiltEntry;
                    host.SetCachedFileTagsForLibraryScan(file, host.ParseTagText(rebuiltEntry.TagText), host.MetadataCacheStamp(file));
                    updated++;
                    processed++;
                    var remaining = fileList.Count - (unchanged + processed);
                    if (progress != null) progress(unchanged + processed, fileList.Count, "Indexed " + (unchanged + processed) + " of " + fileList.Count + " | " + remaining + " remaining | " + file);
                }

                host.SaveLibraryMetadataIndex(root, index);
                host.RebuildLibraryFolderCache(root, index);
                var summary = string.IsNullOrWhiteSpace(folderPath)
                    ? "Library metadata index scan complete: updated " + updated + ", unchanged " + unchanged + ", removed " + removed + "."
                    : "Library folder scan complete for " + Path.GetFileName(folderPath) + ": updated " + updated + ", unchanged " + unchanged + ", removed " + removed + ".";
                host.LogLibraryScan(summary);
                if (progress != null) progress(fileList.Count, fileList.Count, summary);
                return updated;
            }
        }
    }
}
