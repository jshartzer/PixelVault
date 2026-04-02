using System;
using System.Collections.Generic;
using System.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        sealed class LibraryScanHost : ILibraryScanHost
        {
            readonly MainWindow window;

            public LibraryScanHost(MainWindow window)
            {
                this.window = window ?? throw new ArgumentNullException(nameof(window));
            }

            public object LibraryMaintenanceSync => window.libraryMaintenanceSync;

            public void EnsureLibraryRootExists(string root) => EnsureDir(root, "Library folder");

            public void EnsureExifTool() => window.EnsureExifTool();

            public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(string root) => window.LoadLibraryMetadataIndex(root);

            public void SaveLibraryMetadataIndex(string root, Dictionary<string, LibraryMetadataIndexEntry> index) => window.SaveLibraryMetadataIndex(root, index);

            public List<GameIndexEditorRow> LoadSavedGameIndexRows(string root) => window.LoadSavedGameIndexRows(root);

            public bool IsLibraryMediaFile(string path) => IsMedia(path);

            public string BuildLibraryMetadataStamp(string file) => window.BuildLibraryMetadataStamp(file);

            public Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(string[] files, CancellationToken cancellationToken) =>
                window.ReadEmbeddedMetadataBatch(files, cancellationToken);

            public LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
                string root,
                string file,
                string stamp,
                EmbeddedMetadataSnapshot snapshot,
                LibraryMetadataIndexEntry existingEntry,
                Dictionary<string, LibraryMetadataIndexEntry> index,
                List<GameIndexEditorRow> gameRows) =>
                window.BuildResolvedLibraryMetadataIndexEntry(root, file, stamp, snapshot, existingEntry, index, gameRows);

            public void SetCachedFileTagsForLibraryScan(string file, string[] tags, long stampTicks) => window.SetCachedFileTags(file, tags, stampTicks);

            public long MetadataCacheStamp(string file) => window.MetadataCacheStamp(file);

            public string[] ParseTagText(string text) => MainWindow.ParseTagText(text);

            public int GetLibraryScanWorkerCount(int batchCount, string pathHint) => window.GetLibraryScanWorkerCount(batchCount, pathHint);

            public void LogLibraryScan(string message) => window.Log(message);

            public void RebuildLibraryFolderCache(string root, Dictionary<string, LibraryMetadataIndexEntry> index) => window.RebuildLibraryFolderCache(root, index);
        }
    }
}
