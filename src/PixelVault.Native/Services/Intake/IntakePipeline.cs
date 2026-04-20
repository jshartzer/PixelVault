using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-EXT-002 B.2: thin intake facade — groups <see cref="IImportService"/>, <see cref="IFileSystemService"/>,
    /// and <see cref="IntakeAnalysisService"/> for headless standard import and preview analysis without growing a god-class.
    /// </summary>
    internal sealed class IntakePipeline
    {
        public IntakePipeline(IImportService importService, IFileSystemService fileSystem, IntakeAnalysisService analysis)
        {
            Import = importService ?? throw new ArgumentNullException(nameof(importService));
            FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        }

        public IImportService Import { get; }

        public IFileSystemService FileSystem { get; }

        public IntakeAnalysisService Analysis { get; }

        /// <summary>Delegates to <see cref="HeadlessImportCoordinator.RunStandardTopLevelSubsetAsync"/> (same behavior as before B.2).</summary>
        public Task<HeadlessStandardImportOutcome> RunStandardTopLevelSubsetAsync(
            string destinationRoot,
            string libraryRoot,
            SourceInventory renameInventory,
            SourceInventory inventory,
            List<ReviewItem> reviewSubset,
            HashSet<string> manualPathsOriginal,
            CancellationToken cancellationToken = default,
            Action<int, string> progress = null) =>
            HeadlessImportCoordinator.RunStandardTopLevelSubsetAsync(
                Import,
                FileSystem,
                destinationRoot,
                libraryRoot,
                renameInventory,
                inventory,
                reviewSubset,
                manualPathsOriginal,
                cancellationToken,
                progress);
    }
}
