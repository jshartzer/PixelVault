using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>
    /// Library-facing session: workspace caches, current root, scanner, file I/O seam, and game-index persistence for the active library root.
    /// Phase E2 — keeps hosts and large partials from reaching for unrelated <see cref="MainWindow"/> state.
    /// </summary>
    internal interface ILibrarySession
    {
        string LibraryRoot { get; }
        LibraryWorkspaceContext Workspace { get; }
        ILibraryScanner Scanner { get; }
        IFileSystemService FileSystem { get; }

        /// <summary>Persist game index rows for <see cref="LibraryRoot"/> (clone + SQLite + filename-rule cache invalidate).</summary>
        void PersistGameIndexRows(IEnumerable<GameIndexEditorRow> rows);

        /// <summary>Load the library metadata index for <see cref="LibraryRoot"/> (empty dictionary when root is unset).</summary>
        Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(bool forceDiskReload = false);

        /// <summary>Load saved game index rows for <see cref="LibraryRoot"/> (empty list when root is unset).</summary>
        List<GameIndexEditorRow> LoadSavedGameIndexRows();

        /// <summary>Persist the library metadata index for <see cref="LibraryRoot"/> (no-op when root is unset).</summary>
        void SaveLibraryMetadataIndex(Dictionary<string, LibraryMetadataIndexEntry> index);

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
    }
}
