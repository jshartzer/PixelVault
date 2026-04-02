using System;
using System.Collections.Generic;
using System.Threading;

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

        void UpsertLibraryMetadataIndexEntries(IEnumerable<string> files, string root);

        void UpsertLibraryMetadataIndexEntries(IEnumerable<ManualMetadataItem> items, string root);

        void RemoveLibraryMetadataIndexEntries(IEnumerable<string> files, string root);

        void SavePhotoIndexEditorRows(string root, IEnumerable<PhotoIndexEditorRow> rows);

        List<PhotoIndexEditorRow> LoadPhotoIndexEditorRows(string root);

        /// <summary>Build folder cards from on-disk library layout and metadata index (may refresh index entries and saved game rows).</summary>
        List<LibraryFolderInfo> LoadLibraryFolders(string root, Dictionary<string, LibraryMetadataIndexEntry> index);

        /// <summary>Rebuild persisted folder-card cache from a metadata index snapshot (locks library maintenance).</summary>
        void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index);

        /// <summary>Load folder cards from disk cache when stamp matches; otherwise rebuild via <see cref="LoadLibraryFolders"/>.</summary>
        List<LibraryFolderInfo> LoadLibraryFoldersCached(string root, bool forceRefresh);
    }
}
