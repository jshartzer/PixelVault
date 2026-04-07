using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>Callbacks and services for <see cref="GameIndexService"/> — keeps filesystem/metadata alignment on the host until a later slice.</summary>
    internal sealed class GameIndexServiceDependencies
    {
        public ILibraryScanner LibraryScanner { get; set; }
        public ILibrarySession LibrarySession { get; set; }
        public IIndexPersistenceService IndexPersistence { get; set; }
        public IGameIndexEditorAssignmentService GameIndexEditorAssignment { get; set; }

        /// <summary>Host’s configured library root (e.g. <c>MainWindow.libraryRoot</c>) for <see cref="IGameIndexService.GetSavedRowsForRoot"/> routing.</summary>
        public Func<string> HostLibraryRoot { get; set; }

        public Func<IEnumerable<GameIndexEditorRow>, List<GameIndexEditorRow>> MergeGameIndexRows { get; set; }
        public Func<IEnumerable<LibraryFolderInfo>, List<GameIndexEditorRow>> BuildGameIndexRowsFromFolders { get; set; }
        public Action<string> RefreshCachedLibraryFoldersFromGameIndex { get; set; }

        /// <summary>Mutates <paramref name="rows"/> in place (suppress flags, cleaned external IDs).</summary>
        public Action<List<GameIndexEditorRow>, List<GameIndexEditorRow>> ApplyEditorSaveRowPolicies { get; set; }

        public Func<IEnumerable<GameIndexEditorRow>, IEnumerable<GameIndexEditorRow>, Dictionary<string, string>> BuildGameIndexSaveAliasMap { get; set; }
        public Action<string, List<GameIndexEditorRow>> AlignLibraryFoldersToGameIndex { get; set; }
        public Action<string, Dictionary<string, string>> RewriteGameIdAliasesInLibraryFolderCacheFile { get; set; }
    }
}
