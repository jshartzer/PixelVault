using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>Outcome of <see cref="HeadlessImportCoordinator.RunStandardTopLevelSubsetAsync"/> (same steps as standard UI import, subset move).</summary>
    internal sealed class HeadlessStandardImportOutcome
    {
        public RenameStepResult RenameResult;
        public DeleteStepResult DeleteResult;
        public MetadataStepResult MetadataResult;
        public MoveStepResult MoveResult;
        public SortStepResult SortResult;
    }

    /// <summary>
    /// Headless standard import for an explicit top-level eligible set: Steam rename (full scope), delete/metadata on <paramref name="reviewSubset"/>,
    /// move only files in that subset (others stay in upload), sort, append undo manifest.
    /// </summary>
    internal static class HeadlessImportCoordinator
    {
        /// <param name="fileSystem">Filesystem seam for existence checks on move candidates (same instance as <see cref="IImportService"/> wiring when possible).</param>
        /// <param name="manualPathsOriginal">Paths of manual-intake top-level files (not auto-capable); unchanged after Steam rename until resolved here.</param>
        /// <param name="progress">Cumulative step index and detail (same shape as UI import progress).</param>
        public static async Task<HeadlessStandardImportOutcome> RunStandardTopLevelSubsetAsync(
            IImportService import,
            IFileSystemService fileSystem,
            string destinationRoot,
            string libraryRoot,
            SourceInventory renameInventory,
            SourceInventory inventory,
            List<ReviewItem> reviewSubset,
            HashSet<string> manualPathsOriginal,
            CancellationToken cancellationToken = default,
            Action<int, string> progress = null)
        {
            if (import == null) throw new ArgumentNullException(nameof(import));
            ArgumentNullException.ThrowIfNull(fileSystem);
            if (reviewSubset == null) throw new ArgumentNullException(nameof(reviewSubset));
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));

            var manualPaths = manualPathsOriginal ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var standardTotals = import.ComputeStandardImportWorkTotals(renameInventory, reviewSubset, inventory, manualPaths);
            var renameOffset = standardTotals.RenameOffset;
            var deleteOffset = standardTotals.DeleteOffset;
            var metadataOffset = standardTotals.MetadataOffset;
            var moveOffset = standardTotals.MoveOffset;
            var sortOffset = standardTotals.SortOffset;
            var totalWork = standardTotals.TotalWork;

            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Headless import");
            var renameResult = await import.RunSteamRenameAsync(
                renameInventory == null ? new List<string>() : renameInventory.RenameScopeFiles,
                delegate(int current, int total, string detail)
                {
                    progress?.Invoke(renameOffset + current, detail);
                },
                cancellationToken).ConfigureAwait(false);

            var steamMap = renameResult == null ? null : renameResult.OldPathToNewPath;
            if (steamMap != null && steamMap.Count > 0)
                SteamImportRename.ApplySteamRenameMapToReviewItems(reviewSubset, steamMap);

            var moveSourcePathsAfterRename = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(
                inventory == null ? null : inventory.TopLevelMediaFiles,
                steamMap);

            var manualResolved = ResolvePathsThroughMap(manualPaths, steamMap);
            var eligibleAfterSteam = new HashSet<string>(
                reviewSubset.Where(i => i != null && !string.IsNullOrWhiteSpace(i.FilePath)).Select(i => i.FilePath),
                StringComparer.OrdinalIgnoreCase);

            var skipMove = new HashSet<string>(manualResolved, StringComparer.OrdinalIgnoreCase);
            foreach (var p in moveSourcePathsAfterRename)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (!eligibleAfterSteam.Contains(p)) skipMove.Add(p);
            }

            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Headless import");
            var deleteResult = import.DeleteSourceFiles(
                reviewSubset.Where(i => i != null && i.DeleteBeforeProcessing).Select(i => i.FilePath),
                delegate(int current, int total, string detail)
                {
                    progress?.Invoke(deleteOffset + current, detail);
                },
                cancellationToken);

            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Headless import");
            var metadataResult = import.WriteMetadataForReviewItems(
                reviewSubset,
                delegate(int current, int total, string detail)
                {
                    progress?.Invoke(metadataOffset + current, detail);
                },
                cancellationToken);

            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Headless import");
            var moveFiles = moveSourcePathsAfterRename
                .Where(p => !string.IsNullOrWhiteSpace(p) && fileSystem.FileExists(p) && !skipMove.Contains(p));
            var moveResult = import.MoveFilesToLibraryDestination(
                moveFiles,
                "Headless import move",
                delegate(int current, int total, string detail)
                {
                    progress?.Invoke(moveOffset + current, detail);
                },
                cancellationToken);

            SortStepResult sortResult = null;
            if (moveResult != null && moveResult.Moved > 0)
            {
                ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Headless import");
                import.AppendUndoManifestEntries(moveResult.Entries);
                progress?.Invoke(sortOffset, "Sorting imported captures into game folders...");
                sortResult = import.SortDestinationRootIntoGameFolders(destinationRoot, libraryRoot, cancellationToken);
            }

            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Headless import");
            progress?.Invoke(totalWork, "Headless import complete.");

            return new HeadlessStandardImportOutcome
            {
                RenameResult = renameResult,
                DeleteResult = deleteResult,
                MetadataResult = metadataResult,
                MoveResult = moveResult,
                SortResult = sortResult
            };
        }

        static HashSet<string> ResolvePathsThroughMap(HashSet<string> paths, Dictionary<string, string> oldToNew)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null) return set;
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (oldToNew != null && oldToNew.TryGetValue(p, out var n) && !string.IsNullOrWhiteSpace(n)) set.Add(n);
                else set.Add(p);
            }
            return set;
        }
    }
}
