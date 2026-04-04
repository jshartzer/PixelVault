using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal sealed class LibraryBrowserShellBridge : ILibraryBrowserShell
        {
            readonly MainWindow _m;

            internal LibraryBrowserShellBridge(MainWindow mainWindow)
            {
                _m = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            }

            public ILibrarySession LibrarySession => _m.librarySession;

            public string LibraryBrowserPersistedSearch => _m._libraryBrowserPersistedSearch ?? string.Empty;

            public double LibraryBrowserPersistedFolderScroll => _m._libraryBrowserPersistedFolderScroll;

            public string LibraryBrowserPersistedLastViewKey => _m._libraryBrowserPersistedLastViewKey ?? string.Empty;

            public double LibraryBrowserPersistedDetailScroll => _m._libraryBrowserPersistedDetailScroll;

            public string LibraryFolderSortMode
            {
                get => _m.libraryFolderSortMode;
                set => _m.libraryFolderSortMode = value;
            }

            public string LibraryGroupingMode
            {
                get => _m.libraryGroupingMode;
                set => _m.libraryGroupingMode = value;
            }

            public Action<bool> ActiveLibraryFolderRefresh
            {
                get => _m.activeLibraryFolderRefresh;
                set => _m.activeLibraryFolderRefresh = value;
            }

            public LibraryFolderInfo ActiveSelectedLibraryFolder
            {
                get => _m.activeSelectedLibraryFolder;
                set => _m.activeSelectedLibraryFolder = value;
            }

            public TextBlock StatusLine => _m.status;

            public void MarkLibraryBrowserSessionFirstPaintTracking() => _m.MarkLibraryBrowserSessionFirstPaintTracking();

            public Window GetOrCreateLibraryBrowserWindow(bool reuseMainWindow) => _m.GetOrCreateLibraryBrowserWindow(reuseMainWindow);

            public SolidColorBrush BrushFromHex(string hex) => _m.Brush(hex);

            public LibraryBrowserNavChrome BuildLibraryBrowserNavChrome() => _m.BuildLibraryBrowserNavChrome();

            public LibraryBrowserPaneRefs BuildLibraryBrowserContentPanes(Grid contentGrid) => _m.BuildLibraryBrowserContentPanes(contentGrid);

            public LibraryFolderInfo BuildLibraryBrowserDisplayFolder(LibraryBrowserFolderView view) => _m.BuildLibraryBrowserDisplayFolder(view);

            public LibraryFolderInfo GetLibraryBrowserPrimaryFolder(LibraryBrowserFolderView view) => _m.GetLibraryBrowserPrimaryFolder(view);

            public Func<List<string>> LibraryBrowserCreateVisibleDetailFilesOrdered(
                LibraryBrowserWorkingSet ws,
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder) =>
                _m.LibraryBrowserCreateVisibleDetailFilesOrdered(ws, getDisplayFolder);

            public Func<List<string>> LibraryBrowserCreateSelectedDetailFiles(
                LibraryBrowserWorkingSet ws,
                Func<List<string>> getVisibleDetailFilesOrdered) =>
                _m.LibraryBrowserCreateSelectedDetailFiles(ws, getVisibleDetailFilesOrdered);

            public Action<string, ModifierKeys> LibraryBrowserCreateUpdateDetailSelection(
                LibraryBrowserWorkingSet ws,
                Func<List<string>> getVisibleDetailFilesOrdered,
                Action refreshDetailSelectionUi) =>
                _m.LibraryBrowserCreateUpdateDetailSelection(ws, getVisibleDetailFilesOrdered, refreshDetailSelectionUi);

            public Action LibraryBrowserCreateRefreshDetailSelectionUi(
                LibraryBrowserWorkingSet ws,
                LibraryBrowserPaneRefs panes,
                Func<List<string>> getSelectedDetailFiles) =>
                _m.LibraryBrowserCreateRefreshDetailSelectionUi(ws, panes, getSelectedDetailFiles);

            public void LibraryBrowserScheduleIntakeReviewBadgeRefresh(
                Window libraryWindow,
                LibraryBrowserWorkingSet ws,
                LibraryBrowserNavChrome navChrome) =>
                _m.LibraryBrowserScheduleIntakeReviewBadgeRefresh(libraryWindow, ws, navChrome);

            public string NormalizeLibraryFolderSortMode(string value) => _m.NormalizeLibraryFolderSortMode(value);

            public string NormalizeLibraryGroupingMode(string value) => _m.NormalizeLibraryGroupingMode(value);

            public void LibraryBrowserApplySortGroupPillState(Button button, bool active) => _m.LibraryBrowserApplySortGroupPillState(button, active);

            public void SaveSettings() => _m.SaveSettings();

            public void LibraryBrowserOpenSingleFileMetadataEditor(
                LibraryBrowserWorkingSet ws,
                string filePath,
                Func<List<string>> getVisibleDetailFilesOrdered,
                Func<List<string>> getSelectedDetailFiles,
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder,
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder,
                Action<bool> refreshLibraryFoldersAsync) =>
                _m.LibraryBrowserOpenSingleFileMetadataEditor(ws, filePath, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);

            public void LibraryBrowserDeleteSelectedCaptures(
                LibraryBrowserWorkingSet ws,
                Func<List<string>> getSelectedDetailFiles,
                Action renderTiles,
                Action renderSelectedFolder,
                Action<bool> refreshLibraryFoldersAsync) =>
                _m.LibraryBrowserDeleteSelectedCaptures(ws, getSelectedDetailFiles, renderTiles, renderSelectedFolder, refreshLibraryFoldersAsync);

            public void LibraryBrowserRenderSelectedFolderDetail(
                LibraryBrowserWorkingSet ws,
                Window libraryWindow,
                Action<string> openSingleFileMetadataEditor,
                Action<string, ModifierKeys> updateDetailSelection,
                Action refreshDetailSelectionUi,
                Action redrawSelectedFolderDetail) =>
                _m.LibraryBrowserRenderSelectedFolderDetail(ws, libraryWindow, openSingleFileMetadataEditor, updateDetailSelection, refreshDetailSelectionUi, redrawSelectedFolderDetail);

            public Button LibraryBrowserBuildFolderTile(
                LibraryBrowserFolderView folder,
                int tileWidth,
                int tileHeight,
                bool showPlatformBadge,
                Action<LibraryBrowserFolderView> showFolder,
                Action renderTiles,
                Action<bool> refreshLibraryFoldersAsync,
                Action<List<LibraryFolderInfo>, string, bool, bool> runScopedCoverRefresh,
                Action<LibraryBrowserFolderView> openLibraryMetadataEditor) =>
                _m.LibraryBrowserBuildFolderTile(folder, tileWidth, tileHeight, showPlatformBadge, showFolder, renderTiles, refreshLibraryFoldersAsync, runScopedCoverRefresh, openLibraryMetadataEditor);

            public void LibraryBrowserShowSelectedFolder(
                LibraryBrowserWorkingSet ws,
                LibraryBrowserPaneRefs panes,
                Window libraryWindow,
                LibraryBrowserFolderView info,
                Action renderSelectedFolder) =>
                _m.LibraryBrowserShowSelectedFolder(ws, panes, libraryWindow, info, renderSelectedFolder);

            public void LibraryBrowserRenderFolderList(
                LibraryBrowserWorkingSet ws,
                Func<LibraryBrowserFolderView, int, int, bool, Button> buildFolderTile,
                Action<LibraryBrowserFolderView> showFolder,
                Action renderSelectedFolder,
                Action selfRerender) =>
                _m.LibraryBrowserRenderFolderList(ws, buildFolderTile, showFolder, renderSelectedFolder, selfRerender);

            public void LibraryBrowserRefreshFoldersAsync(Window libraryWindow, LibraryBrowserWorkingSet ws, bool forceRefresh, Action renderTiles) =>
                _m.LibraryBrowserRefreshFoldersAsync(libraryWindow, ws, forceRefresh, renderTiles);

            public void LibraryBrowserPrefillFoldersFromSnapshot(Window libraryWindow, LibraryBrowserWorkingSet ws, Action renderTiles) =>
                _m.LibraryBrowserPrefillFoldersFromSnapshot(libraryWindow, ws, renderTiles);

            public void LibraryBrowserRunFolderMetadataScan(
                Window libraryWindow,
                LibraryBrowserWorkingSet ws,
                string folderPath,
                bool forceRescan,
                Action<bool> setLibraryBusyState,
                Action<bool> refreshLibraryFoldersAsync) =>
                _m.LibraryBrowserRunFolderMetadataScan(libraryWindow, ws, folderPath, forceRescan, setLibraryBusyState, refreshLibraryFoldersAsync);

            public void RunLibraryBrowserScopedCoverRefresh(
                Window libraryWindow,
                LibraryBrowserWorkingSet ws,
                List<LibraryFolderInfo> requestedFolders,
                string scopeLabel,
                bool forceRefreshExistingCovers,
                bool rebuildFullCacheAfterRefresh,
                Action<bool> refreshLibraryFoldersAsync,
                Action<bool> setLibraryBusyState) =>
                _m.RunLibraryBrowserScopedCoverRefresh(libraryWindow, ws, requestedFolders, scopeLabel, forceRefreshExistingCovers, rebuildFullCacheAfterRefresh, refreshLibraryFoldersAsync, setLibraryBusyState);

            public void PersistLibraryBrowserCommittedSearch(string text) => _m.PersistLibraryBrowserCommittedSearch(text);

            public void PrepareLibraryBrowserSessionForRebuild() => _m.PrepareLibraryBrowserSessionForRebuild();

            public void RegisterLibraryBrowserLiveWorkingSet(LibraryBrowserWorkingSet ws) => _m.RegisterLibraryBrowserLiveWorkingSet(ws);

            public void LibraryBrowserWirePaneEvents(
                Window libraryWindow,
                LibraryBrowserWorkingSet ws,
                LibraryBrowserPaneRefs panes,
                Action renderTiles,
                Action renderSelectedFolder,
                Action applySearchFilter) =>
                _m.LibraryBrowserWirePaneEvents(libraryWindow, ws, panes, renderTiles, renderSelectedFolder, applySearchFilter);

            public void LibraryBrowserOpenLibraryMetadataForFolder(
                LibraryBrowserWorkingSet ws,
                LibraryBrowserFolderView focusFolder,
                Action<LibraryBrowserFolderView> showFolder,
                Action refreshDetailSelectionUi,
                Action openMetadataForCurrentSelection) =>
                _m.LibraryBrowserOpenLibraryMetadataForFolder(ws, focusFolder, showFolder, refreshDetailSelectionUi, openMetadataForCurrentSelection);

            public void LibraryBrowserWireNavChromeAndToolbar(
                Window libraryWindow,
                LibraryBrowserWorkingSet ws,
                LibraryBrowserPaneRefs panes,
                LibraryBrowserNavChrome navChrome,
                Action refreshIntakeReviewBadge,
                Action<bool> refreshLibraryFoldersAsync,
                Action runCoverRefresh,
                Action openSelectedLibraryMetadataEditor,
                Action deleteSelectedLibraryFiles,
                Action<string> setLibraryGroupingMode,
                Action<string> setLibrarySortMode) =>
                _m.LibraryBrowserWireNavChromeAndToolbar(libraryWindow, ws, panes, navChrome, refreshIntakeReviewBadge, refreshLibraryFoldersAsync, runCoverRefresh, openSelectedLibraryMetadataEditor, deleteSelectedLibraryFiles, setLibraryGroupingMode, setLibrarySortMode);
        }
    }
}
