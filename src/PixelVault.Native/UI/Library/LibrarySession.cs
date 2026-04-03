using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    internal sealed class LibrarySession : ILibrarySession
    {
        readonly LibraryWorkspaceContext _workspace;
        readonly ILibraryScanner _scanner;
        readonly IFileSystemService _fileSystem;
        readonly IGameIndexEditorAssignmentService _gameIndexAssignment;
        readonly Func<string, bool, Dictionary<string, LibraryMetadataIndexEntry>> _loadLibraryMetadataIndex;

        internal LibrarySession(
            LibraryWorkspaceContext workspace,
            ILibraryScanner scanner,
            IFileSystemService fileSystem,
            IGameIndexEditorAssignmentService gameIndexAssignment,
            Func<string, bool, Dictionary<string, LibraryMetadataIndexEntry>> loadLibraryMetadataIndex)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _gameIndexAssignment = gameIndexAssignment ?? throw new ArgumentNullException(nameof(gameIndexAssignment));
            _loadLibraryMetadataIndex = loadLibraryMetadataIndex ?? throw new ArgumentNullException(nameof(loadLibraryMetadataIndex));
        }

        public string LibraryRoot => _workspace.LibraryRoot;

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
    }
}
