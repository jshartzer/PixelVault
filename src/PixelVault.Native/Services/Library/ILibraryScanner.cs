using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    internal interface ILibraryScanner
    {
        /// <summary>Scan (or rescan) library metadata index for the whole library or one game folder.</summary>
        /// <returns>Count of entries updated from embedded reads.</returns>
        int ScanLibraryMetadataIndex(
            string root,
            string folderPath,
            bool forceRescan,
            Action<int, int, string> progress,
            CancellationToken cancellationToken = default);

        /// <summary>Runs <see cref="ScanLibraryMetadataIndex"/> on the thread pool; await from UI code instead of wrapping sync scan in <see cref="Task.Factory"/>.</summary>
        Task<int> ScanLibraryMetadataIndexAsync(
            string root,
            string folderPath,
            bool forceRescan,
            Action<int, int, string> progress,
            CancellationToken cancellationToken = default);

        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root);

        void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items, string root);

        void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root);

        void SavePhotoIndexEditorRows(string root, IEnumerable<PhotoIndexEditorRow> rows);

        List<PhotoIndexEditorRow> LoadPhotoIndexEditorRows(string root);

        /// <summary>Build folder cards from on-disk library layout and metadata index (may refresh index entries and saved game rows).</summary>
        List<LibraryFolderInfo> LoadLibraryFolders(string root, Dictionary<string, LibraryMetadataIndexEntry> index);

        /// <summary>Rebuild persisted folder-card cache from a metadata index snapshot (locks library maintenance).</summary>
        void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index);

        /// <summary>Reload metadata index from disk and rebuild folder-card cache (e.g. after game-index edits).</summary>
        void RefreshFolderCacheAfterGameIndexChange(string root);

        /// <summary>Load folder cards from disk cache when stamp matches; otherwise rebuild via <see cref="LoadLibraryFolders"/>.</summary>
        List<LibraryFolderInfo> LoadLibraryFoldersCached(string root, bool forceRefresh);
    }
}
