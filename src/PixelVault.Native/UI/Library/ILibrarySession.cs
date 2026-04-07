using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    internal delegate Task<(int ResolvedIds, int CoversReady)> LibraryCoverRefreshAsyncInvoker(
        string root,
        List<LibraryFolderInfo> libraryFolders,
        List<LibraryFolderInfo> requestedFolders,
        Action<int, int, string> progress,
        CancellationToken cancellationToken,
        bool forceRefreshExistingCovers,
        bool rebuildFullCacheAfterRefresh);

    internal delegate void LibraryMetadataScanInvoker(
        Window owner,
        string libraryRoot,
        string folderPath,
        bool forceRescan,
        Action<bool> setBusyState,
        Action onSuccess);

    /// <summary>
    /// Library-facing session: workspace caches, current root, scanner, file I/O seam, and game-index persistence for the active library root.
    /// Phase E2 — keeps hosts and large partials from reaching for unrelated <see cref="MainWindow"/> state.
    /// </summary>
    internal interface ILibrarySession
    {
        string LibraryRoot { get; }

        /// <summary>True when <see cref="LibraryRoot"/> is non-empty.</summary>
        bool HasLibraryRoot { get; }

        /// <summary>Throws if <see cref="LibraryRoot"/> is missing or not an existing directory (host uses the same rules as <c>EnsureDir</c>).</summary>
        void EnsureLibraryRootAccessible(string notFoundMessageLabel);
        LibraryWorkspaceContext Workspace { get; }
        ILibraryScanner Scanner { get; }
        IFileSystemService FileSystem { get; }

        /// <summary>Persist game index rows for <see cref="LibraryRoot"/> (clone + SQLite + filename-rule cache invalidate).</summary>
        void PersistGameIndexRows(IEnumerable<GameIndexEditorRow> rows);

        /// <summary>Load the library metadata index for <see cref="LibraryRoot"/> (empty dictionary when root is unset).</summary>
        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(bool forceDiskReload = false);

        /// <summary>Load <c>photo_index</c> entries only for the given absolute file paths (empty when root unset or no paths).</summary>
        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexForFilePaths(IEnumerable<string> filePaths);

        /// <summary>Upsert metadata index rows to SQLite and invalidate the host in-memory cache so the next full load is coherent.</summary>
        void MergePersistLibraryMetadataIndexEntries(IEnumerable<LibraryMetadataIndexEntry> entries);

        /// <summary>Load saved game index rows for <see cref="LibraryRoot"/> (empty list when root is unset).</summary>
        List<GameIndexEditorRow> LoadSavedGameIndexRows();

        /// <summary>Persist the library metadata index for <see cref="LibraryRoot"/> (no-op when root is unset).</summary>
        void SaveLibraryMetadataIndex(Dictionary<string, LibraryMetadataIndexEntry> index);

        /// <summary>Toggle starred flag in the photo index and embedded XMP for <paramref name="absoluteFilePath"/> (host no-op when inapplicable). <paramref name="onCompleted"/> is <c>true</c> when the index row was updated; <c>false</c> if skipped or failed. Runs on the UI thread.</summary>
        void RequestToggleCaptureStarred(string absoluteFilePath, Action<bool> onCompleted = null);

        /// <summary>Save embedded comment for <paramref name="absoluteFilePath"/> (host no-op when inapplicable). <paramref name="onCompleted"/> receives whether a write was performed; runs on UI thread.</summary>
        void RequestSaveCaptureComment(string absoluteFilePath, string comment, Action<bool> onCompleted = null);

        /// <summary>Load folder-cache snapshot lines for <see cref="LibraryRoot"/> if present; otherwise <c>null</c>.</summary>
        List<LibraryFolderInfo> LoadLibraryFolderCacheSnapshot();

        /// <summary>True when a folder-cache snapshot exists for <see cref="LibraryRoot"/>.</summary>
        bool HasLibraryFolderCacheSnapshot();

        /// <summary>Indexed capture date for <paramref name="file"/> under <see cref="LibraryRoot"/> (same rules as library scan host).</summary>
        DateTime ResolveIndexedLibraryDate(string file, Dictionary<string, LibraryMetadataIndexEntry> index);

        /// <summary>Build a resolved metadata index entry for <paramref name="file"/> under <see cref="LibraryRoot"/>.</summary>
        LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
            string file,
            string stamp,
            EmbeddedMetadataSnapshot snapshot,
            LibraryMetadataIndexEntry existingEntry,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows);

        /// <summary>Remove index rows for deleted files under <see cref="LibraryRoot"/> (no-op when root unset).</summary>
        void RemoveLibraryMetadataIndexEntries(IEnumerable<string> removedFiles);

        /// <summary>Merge updated paths into the library metadata index for <see cref="LibraryRoot"/> (no-op when root unset).</summary>
        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files);

        /// <summary>Merge manual-metadata targets into the library metadata index for <see cref="LibraryRoot"/> (no-op when root unset).</summary>
        void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items);

        /// <summary>Rebuild folder cache / stamps after game-index edits for <see cref="LibraryRoot"/> (no-op when root unset).</summary>
        void RefreshFolderCacheAfterGameIndexChange();

        /// <summary>Ensure non-empty folder cache for game-index editor flows (empty list when root unset).</summary>
        List<LibraryFolderInfo> EnsureGameIndexFolderContext(Action<string> setUiStatus);

        /// <summary>Load folder list from cache / scan for <see cref="LibraryRoot"/> (empty list when root unset).</summary>
        List<LibraryFolderInfo> LoadLibraryFoldersCached(bool forceRefresh);

        /// <summary>Resolve IDs and fetch covers for <see cref="LibraryRoot"/> (completed (0,0) when root unset).</summary>
        Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(
            List<LibraryFolderInfo> libraryFolders,
            List<LibraryFolderInfo> requestedFolders,
            Action<int, int, string> progress,
            CancellationToken cancellationToken,
            bool forceRefreshExistingCovers,
            bool rebuildFullCacheAfterRefresh);

        /// <summary>Show metadata scan progress for <see cref="LibraryRoot"/> (no-op when root unset).</summary>
        void RunLibraryMetadataScan(Window owner, string folderPath, bool forceRescan, Action<bool> setBusyState, Action onSuccess);

        /// <summary>Record Steam AppID on the saved game index for <paramref name="gameDisplayName"/> when first seen (active library root only).</summary>
        void EnsureSteamAppIdInActiveLibrary(string gameDisplayName, string steamAppId);

        /// <summary>Fingerprint map for Export Starred incremental copy (active library root + destination).</summary>
        Dictionary<string, string> LoadStarredExportFingerprints(string exportDestinationNormalized);

        void UpsertStarredExportFingerprint(string exportDestinationNormalized, string sourcePathNormalized, string fingerprint);

        void PruneStarredExportFingerprints(string exportDestinationNormalized, IReadOnlyCollection<string> activeSourcePathsNormalized);
    }
}
