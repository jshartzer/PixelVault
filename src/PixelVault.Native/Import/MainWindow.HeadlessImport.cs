using System;
using System.Collections.Generic;
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
            fileSystemService.CreateDirectory(destinationRoot);
            var inventory = importService.BuildSourceInventory(importSearchSubfoldersForRename);
            var inventoryCandidates = new HashSet<string>(inventory.TopLevelMediaFiles ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var eligible = (eligibleTopLevelPaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p) && fileSystemService.FileExists(p))
                .Where(p => inventoryCandidates.Contains(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (eligible.Count == 0)
            {
                return new HeadlessStandardImportOutcome();
            }

            var prep = BuildIntakePreparation(inventory.TopLevelMediaFiles, false, cancellationToken);
            var eligibleSet = new HashSet<string>(eligible, StringComparer.OrdinalIgnoreCase);
            var reviewSubset = prep.ReviewItems
                .Where(item => item != null && eligibleSet.Contains(item.FilePath))
                .ToList();
            if (reviewSubset == null || reviewSubset.Count == 0)
            {
                return new HeadlessStandardImportOutcome();
            }

            return await intakePipeline.RunStandardTopLevelSubsetAsync(
                destinationRoot,
                libraryRoot,
                inventory,
                inventory,
                reviewSubset,
                prep.ManualPaths,
                cancellationToken,
                progress).ConfigureAwait(false);
        }
    }
}
