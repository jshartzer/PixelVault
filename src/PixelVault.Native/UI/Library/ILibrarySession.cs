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
    }
}
