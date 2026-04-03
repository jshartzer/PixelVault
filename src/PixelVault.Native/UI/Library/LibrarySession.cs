using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    internal sealed class LibrarySession : ILibrarySession
    {
        readonly LibraryWorkspaceContext _workspace;
        readonly ILibraryScanner _scanner;
        readonly IFileSystemService _fileSystem;
        readonly IGameIndexEditorAssignmentService _gameIndexAssignment;
        readonly Func<string, bool, Dictionary<string, LibraryMetadataIndexEntry>> _loadLibraryMetadataIndex;
        readonly Func<string, List<GameIndexEditorRow>> _loadSavedGameIndexRows;
        readonly Action<string, Dictionary<string, LibraryMetadataIndexEntry>> _saveLibraryMetadataIndex;
        readonly Func<string, List<LibraryFolderInfo>> _loadLibraryFolderCacheSnapshot;
        readonly Func<string, string, Dictionary<string, LibraryMetadataIndexEntry>, DateTime> _resolveIndexedLibraryDate;
        readonly Func<string, string, string, EmbeddedMetadataSnapshot, LibraryMetadataIndexEntry, Dictionary<string, LibraryMetadataIndexEntry>, List<GameIndexEditorRow>, LibraryMetadataIndexEntry> _buildResolvedLibraryMetadataIndexEntry;
        readonly LibraryCoverRefreshAsyncInvoker _refreshLibraryCovers;
        readonly LibraryMetadataScanInvoker _runLibraryMetadataScan;
        readonly Action<string, string> _ensureDirectoryAccessible;

        internal LibrarySession(
            LibraryWorkspaceContext workspace,
            ILibraryScanner scanner,
            IFileSystemService fileSystem,
            IGameIndexEditorAssignmentService gameIndexAssignment,
            Func<string, bool, Dictionary<string, LibraryMetadataIndexEntry>> loadLibraryMetadataIndex,
            Func<string, List<GameIndexEditorRow>> loadSavedGameIndexRows,
            Action<string, Dictionary<string, LibraryMetadataIndexEntry>> saveLibraryMetadataIndex,
            Func<string, List<LibraryFolderInfo>> loadLibraryFolderCacheSnapshot,
            Func<string, string, Dictionary<string, LibraryMetadataIndexEntry>, DateTime> resolveIndexedLibraryDate,
            Func<string, string, string, EmbeddedMetadataSnapshot, LibraryMetadataIndexEntry, Dictionary<string, LibraryMetadataIndexEntry>, List<GameIndexEditorRow>, LibraryMetadataIndexEntry> buildResolvedLibraryMetadataIndexEntry,
            LibraryCoverRefreshAsyncInvoker refreshLibraryCovers,
            LibraryMetadataScanInvoker runLibraryMetadataScan,
            Action<string, string> ensureDirectoryAccessible)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _gameIndexAssignment = gameIndexAssignment ?? throw new ArgumentNullException(nameof(gameIndexAssignment));
            _loadLibraryMetadataIndex = loadLibraryMetadataIndex ?? throw new ArgumentNullException(nameof(loadLibraryMetadataIndex));
            _loadSavedGameIndexRows = loadSavedGameIndexRows ?? throw new ArgumentNullException(nameof(loadSavedGameIndexRows));
            _saveLibraryMetadataIndex = saveLibraryMetadataIndex ?? throw new ArgumentNullException(nameof(saveLibraryMetadataIndex));
            _loadLibraryFolderCacheSnapshot = loadLibraryFolderCacheSnapshot ?? throw new ArgumentNullException(nameof(loadLibraryFolderCacheSnapshot));
            _resolveIndexedLibraryDate = resolveIndexedLibraryDate ?? throw new ArgumentNullException(nameof(resolveIndexedLibraryDate));
            _buildResolvedLibraryMetadataIndexEntry = buildResolvedLibraryMetadataIndexEntry ?? throw new ArgumentNullException(nameof(buildResolvedLibraryMetadataIndexEntry));
            _refreshLibraryCovers = refreshLibraryCovers ?? throw new ArgumentNullException(nameof(refreshLibraryCovers));
            _runLibraryMetadataScan = runLibraryMetadataScan ?? throw new ArgumentNullException(nameof(runLibraryMetadataScan));
            _ensureDirectoryAccessible = ensureDirectoryAccessible ?? throw new ArgumentNullException(nameof(ensureDirectoryAccessible));
        }

        public string LibraryRoot => _workspace.LibraryRoot;

        public bool HasLibraryRoot => !string.IsNullOrWhiteSpace(LibraryRoot);

        public void EnsureLibraryRootAccessible(string notFoundMessageLabel)
        {
            _ensureDirectoryAccessible(LibraryRoot ?? string.Empty, string.IsNullOrWhiteSpace(notFoundMessageLabel) ? "Library folder" : notFoundMessageLabel);
        }

        public LibraryWorkspaceContext Workspace => _workspace;

        public ILibraryScanner Scanner => _scanner;

        public IFileSystemService FileSystem => _fileSystem;

        public void PersistGameIndexRows(IEnumerable<GameIndexEditorRow> rows)
        {
            _gameIndexAssignment.SaveSavedGameIndexRows(LibraryRoot, rows);
        }

        public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndex(bool forceDiskReload = false)
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot))
            {
                return new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, LibraryMetadataIndexEntry>(_loadLibraryMetadataIndex(LibraryRoot, forceDiskReload), StringComparer.OrdinalIgnoreCase);
        }

        public List<GameIndexEditorRow> LoadSavedGameIndexRows()
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot))
            {
                return new List<GameIndexEditorRow>();
            }

            return _loadSavedGameIndexRows(LibraryRoot) ?? new List<GameIndexEditorRow>();
        }

        public void SaveLibraryMetadataIndex(Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot) || index == null) return;
            _saveLibraryMetadataIndex(LibraryRoot, index);
        }

        public List<LibraryFolderInfo> LoadLibraryFolderCacheSnapshot()
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot)) return null;
            return _loadLibraryFolderCacheSnapshot(LibraryRoot);
        }

        public bool HasLibraryFolderCacheSnapshot()
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot)) return false;
            return _loadLibraryFolderCacheSnapshot(LibraryRoot) != null;
        }

        public DateTime ResolveIndexedLibraryDate(string file, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            return _resolveIndexedLibraryDate(LibraryRoot ?? string.Empty, file, index);
        }

        public LibraryMetadataIndexEntry BuildResolvedLibraryMetadataIndexEntry(
            string file,
            string stamp,
            EmbeddedMetadataSnapshot snapshot,
            LibraryMetadataIndexEntry existingEntry,
            Dictionary<string, LibraryMetadataIndexEntry> index,
            List<GameIndexEditorRow> gameRows)
        {
            return _buildResolvedLibraryMetadataIndexEntry(LibraryRoot ?? string.Empty, file, stamp, snapshot, existingEntry, index, gameRows);
        }

        public void RemoveLibraryMetadataIndexEntries(IEnumerable<string> removedFiles)
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot) || removedFiles == null) return;
            _scanner.RemoveLibraryMetadataIndexEntries(removedFiles, LibraryRoot);
        }

        public List<LibraryFolderInfo> LoadLibraryFoldersCached(bool forceRefresh)
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot)) return new List<LibraryFolderInfo>();
            return _scanner.LoadLibraryFoldersCached(LibraryRoot, forceRefresh);
        }

        public Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(
            List<LibraryFolderInfo> libraryFolders,
            List<LibraryFolderInfo> requestedFolders,
            Action<int, int, string> progress,
            CancellationToken cancellationToken,
            bool forceRefreshExistingCovers,
            bool rebuildFullCacheAfterRefresh)
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot))
            {
                return Task.FromResult((0, 0));
            }

            return _refreshLibraryCovers(LibraryRoot, libraryFolders, requestedFolders, progress, cancellationToken, forceRefreshExistingCovers, rebuildFullCacheAfterRefresh);
        }

        public void RunLibraryMetadataScan(Window owner, string folderPath, bool forceRescan, Action<bool> setBusyState, Action onSuccess)
        {
            if (string.IsNullOrWhiteSpace(LibraryRoot)) return;
            _runLibraryMetadataScan(owner, LibraryRoot, folderPath, forceRescan, setBusyState, onSuccess);
        }
    }
}
