using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    internal sealed class GameIndexService : IGameIndexService
    {
        readonly GameIndexServiceDependencies _d;

        public GameIndexService(GameIndexServiceDependencies dependencies)
        {
            _d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            if (_d.LibraryScanner == null) throw new ArgumentNullException(nameof(dependencies.LibraryScanner));
            if (_d.LibrarySession == null) throw new ArgumentNullException(nameof(dependencies.LibrarySession));
            if (_d.IndexPersistence == null) throw new ArgumentNullException(nameof(dependencies.IndexPersistence));
            if (_d.GameIndexEditorAssignment == null) throw new ArgumentNullException(nameof(dependencies.GameIndexEditorAssignment));
            if (_d.MergeGameIndexRows == null) throw new ArgumentNullException(nameof(dependencies.MergeGameIndexRows));
            if (_d.BuildGameIndexRowsFromFolders == null) throw new ArgumentNullException(nameof(dependencies.BuildGameIndexRowsFromFolders));
            if (_d.RefreshCachedLibraryFoldersFromGameIndex == null) throw new ArgumentNullException(nameof(dependencies.RefreshCachedLibraryFoldersFromGameIndex));
            if (_d.ApplyEditorSaveRowPolicies == null) throw new ArgumentNullException(nameof(dependencies.ApplyEditorSaveRowPolicies));
            if (_d.BuildGameIndexSaveAliasMap == null) throw new ArgumentNullException(nameof(dependencies.BuildGameIndexSaveAliasMap));
            if (_d.AlignLibraryFoldersToGameIndex == null) throw new ArgumentNullException(nameof(dependencies.AlignLibraryFoldersToGameIndex));
            if (_d.RewriteGameIdAliasesInLibraryFolderCacheFile == null) throw new ArgumentNullException(nameof(dependencies.RewriteGameIdAliasesInLibraryFolderCacheFile));
            if (_d.HostLibraryRoot == null) throw new ArgumentNullException(nameof(dependencies.HostLibraryRoot));
        }

        bool IsActiveLibraryRoot(string root)
        {
            return _d.LibrarySession != null
                && _d.LibrarySession.HasLibraryRoot
                && !string.IsNullOrWhiteSpace(root)
                && string.Equals(root, _d.LibrarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase);
        }

        public List<GameIndexEditorRow> LoadEditorRowsCore(string root, Action<string> setUiStatus)
        {
            var folders = IsActiveLibraryRoot(root)
                ? _d.LibrarySession.EnsureGameIndexFolderContext(setUiStatus)
                : _d.LibraryScanner.EnsureGameIndexFolderContext(root, setUiStatus);
            var liveRows = _d.BuildGameIndexRowsFromFolders(folders);
            var savedRows = _d.IndexPersistence.LoadSavedGameIndexRows(root) ?? new List<GameIndexEditorRow>();
            var rows = _d.MergeGameIndexRows(savedRows.Concat(liveRows));
            if (savedRows.Count == 0 || rows.Count != savedRows.Count)
            {
                _d.GameIndexEditorAssignment.SaveSavedGameIndexRows(root, rows);
                _d.RefreshCachedLibraryFoldersFromGameIndex(root);
            }
            return rows;
        }

        public void SaveEditorRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var rowList = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(entry => entry != null).ToList();
            var previousSaved = _d.IndexPersistence.LoadSavedGameIndexRows(root) ?? new List<GameIndexEditorRow>();
            _d.ApplyEditorSaveRowPolicies(rowList, previousSaved);
            var normalizedRows = _d.MergeGameIndexRows(rowList);
            var cachedFolders = LoadCachedFoldersForSave(root);

            var previousRows = _d.MergeGameIndexRows(
                (_d.IndexPersistence.LoadSavedGameIndexRows(root) ?? new List<GameIndexEditorRow>())
                .Concat(_d.BuildGameIndexRowsFromFolders(cachedFolders)));
            var aliasMap = _d.BuildGameIndexSaveAliasMap(previousRows, normalizedRows);
            _d.AlignLibraryFoldersToGameIndex(root, normalizedRows);
            _d.GameIndexEditorAssignment.SaveSavedGameIndexRows(root, normalizedRows);
            if (aliasMap.Count > 0)
            {
                _d.IndexPersistence.ApplyGameIdAliases(root, aliasMap);
                _d.RewriteGameIdAliasesInLibraryFolderCacheFile(root, aliasMap);
            }
            _d.RefreshCachedLibraryFoldersFromGameIndex(root);
        }

        List<LibraryFolderInfo> LoadCachedFoldersForSave(string root)
        {
            if (IsActiveLibraryRoot(root))
                return _d.LibrarySession.LoadLibraryFoldersCached(false);
            return _d.LibraryScanner.LoadLibraryFoldersCached(root, false) ?? new List<LibraryFolderInfo>();
        }

        public List<GameIndexEditorRow> GetSavedRowsForRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return new List<GameIndexEditorRow>();
            var hostRoot = _d.HostLibraryRoot();
            if (_d.LibrarySession != null
                && !string.IsNullOrWhiteSpace(hostRoot)
                && string.Equals(root, hostRoot, StringComparison.OrdinalIgnoreCase))
                return _d.LibrarySession.LoadSavedGameIndexRows();
            return _d.IndexPersistence.LoadSavedGameIndexRows(root) ?? new List<GameIndexEditorRow>();
        }
    }
}
