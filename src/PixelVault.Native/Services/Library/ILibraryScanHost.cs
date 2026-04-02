using System;
using System.Collections.Generic;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>Callbacks into the app for library metadata scanning (Phase 4 split — keeps scanner free of direct UI/index field access).</summary>
    internal interface ILibraryScanHost
    {
        object LibraryMaintenanceSync { get; }

        void EnsureLibraryRootExists(string root);
        void EnsureExifTool();

        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root);
        void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index);

        List<GameIndexEditorRow> LoadSavedGameIndexRows(string root);

        bool IsLibraryMediaFile(string path);

        string BuildLibraryMetadataStamp(string file);

        Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(string[] files, CancellationToken cancellationToken);

        LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
            string root,
            string file,
            string stamp,
            EmbeddedMetadataSnapshot snapshot,
            LibraryMetadataIndexEntry existingEntry,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows);

        void SetCachedFileTagsForLibraryScan(string file, string[] tags, long stampTicks);

        long MetadataCacheStamp(string file);

        string[] ParseTagText(string text);

        int GetLibraryScanWorkerCount(int batchCount, string pathHint);

        void LogLibraryScan(string message);

        void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index);
    }
}
