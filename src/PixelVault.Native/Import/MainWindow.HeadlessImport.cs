using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Runs the same standard import pipeline as the UI for an explicit top-level eligible set: full-scope Steam rename,
        /// delete + metadata on <paramref name="eligibleTopLevelPaths"/> only, move only those files (other upload files stay put).
        /// Appends undo manifest rows via <see cref="IImportService.AppendUndoManifestEntries"/> when something moves.
        /// </summary>
        internal async Task<HeadlessStandardImportOutcome> RunHeadlessStandardImportForTopLevelPathsAsync(
            IReadOnlyList<string> eligibleTopLevelPaths,
            CancellationToken cancellationToken = default,
            Action<int, string> progress = null)
        {
            EnsureSourceFolders();
            EnsureExifTool();
            Directory.CreateDirectory(destinationRoot);
            var renameInventory = importService.BuildSourceInventory(importSearchSubfoldersForRename);
            var inventory = importService.BuildSourceInventory(importSearchSubfoldersForRename);
            var eligible = (eligibleTopLevelPaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (eligible.Count == 0)
            {
                return new HeadlessStandardImportOutcome();
            }

            var reviewSubset = BuildReviewItems(eligible, cancellationToken);
            if (reviewSubset == null || reviewSubset.Count == 0)
            {
                return new HeadlessStandardImportOutcome();
            }

            var fullReview = BuildReviewItems(inventory.TopLevelMediaFiles, cancellationToken);
            var recognizedPaths = new HashSet<string>(fullReview.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths, cancellationToken);
            var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);

            return await HeadlessImportCoordinator.RunStandardTopLevelSubsetAsync(
                importService,
                destinationRoot,
                libraryRoot,
                renameInventory,
                inventory,
                reviewSubset,
                manualPaths,
                cancellationToken,
                progress).ConfigureAwait(false);
        }
    }
}
