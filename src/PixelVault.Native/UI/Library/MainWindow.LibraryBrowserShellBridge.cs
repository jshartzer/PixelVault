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

            public string LibraryFolderFilterMode
            {
                get => _m.libraryFolderFilterMode;
                set => _m.libraryFolderFilterMode = value;
            }

            public string LibraryGroupingMode
            {
                get => _m.libraryGroupingMode;
                set => _m.libraryGroupingMode = value;
            }

            public int LibraryFolderTileSize
            {
                get => _m.libraryFolderTileSize;
                set => _m.libraryFolderTileSize = value;
            }

            public int LibraryPhotoTileSize
            {
                get => _m.libraryPhotoTileSize;
                set => _m.libraryPhotoTileSize = value;
            }

            public int LibraryFolderGridColumnCount
            {
                get => _m.libraryFolderGridColumnCount;
                set => _m.libraryFolderGridColumnCount = value;
            }

            public int LibraryPhotoGridColumnCount
            {
                get => _m.libraryPhotoGridColumnCount;
                set => _m.libraryPhotoGridColumnCount = value;
            }

            public int LibraryPhotoRailFolderTileSize
            {
                get => _m.libraryPhotoRailFolderTileSize;
                set => _m.libraryPhotoRailFolderTileSize = value;
            }

            public string LibraryPhotoRailFolderSortMode
            {
                get => _m.libraryPhotoRailFolderSortMode;
                set => _m.libraryPhotoRailFolderSortMode = value;
            }

            public string LibraryPhotoRailFolderFilterMode
            {
                get => _m.libraryPhotoRailFolderFilterMode;
                set => _m.libraryPhotoRailFolderFilterMode = value;
            }

            public int LibraryPhotoRailFolderGridColumnCount
            {
                get => _m.libraryPhotoRailFolderGridColumnCount;
                set => _m.libraryPhotoRailFolderGridColumnCount = value;
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

            public string NormalizeLibraryFolderFilterMode(string value) => _m.NormalizeLibraryFolderFilterMode(value);

            public string LibraryFolderFilterModeLabel(string value) => _m.LibraryFolderFilterModeLabel(value);

            public string NormalizeLibraryGroupingMode(string value) => _m.NormalizeLibraryGroupingMode(value);

            public void LibraryBrowserApplySortGroupPillState(Button button, bool active) => _m.LibraryBrowserApplySortGroupPillState(button, active);

            public void SaveSettings() => _m.SaveSettings();

            public int NormalizeLibraryFolderTileSizeValue(int value) => _m.NormalizeLibraryFolderTileSize(value);

            public int NormalizeLibraryPhotoTileSizeValue(int value) => _m.NormalizeLibraryPhotoTileSize(value);

            public int NormalizeLibraryFolderGridColumnCountValue(int value) => _m.NormalizeLibraryFolderGridColumnCount(value);

            public int NormalizeLibraryPhotoGridColumnCountValue(int value) => _m.NormalizeLibraryPhotoGridColumnCount(value);

            public int NormalizeLibraryPhotoRailFolderGridColumnCountValue(int value) => _m.NormalizeLibraryPhotoRailFolderGridColumnCount(value);

            public List<LibraryFolderInfo> GetLibraryBrowserActionFolders(LibraryBrowserFolderView view) => _m.GetLibraryBrowserActionFolders(view);

            public string BuildLibraryBrowserActionScopeLabel(LibraryBrowserFolderView view) => _m.BuildLibraryBrowserActionScopeLabel(view);

            public void LibrarySaveCustomCover(LibraryFolderInfo folder, string sourcePath) => _m.SaveCustomCover(folder, sourcePath);

            public bool IsLibraryRasterImageFilePath(string path) => MainWindow.IsImage(path);

            public void LibraryBrowserMountToastHost(Grid rootGrid, LibraryBrowserWorkingSet ws) => _m.LibraryBrowserMountToastHost(rootGrid, ws);

            public void LibraryBrowserShowToast(LibraryBrowserWorkingSet ws, string message) => _m.ShowLibraryBrowserToast(ws, message);

            public void ShowLibraryBrowserKeyboardShortcutsHelp(Window owner) => _m.ShowLibraryBrowserKeyboardShortcutsHelp(owner);

            public void ShowLibraryCommandPalette(Window owner, LibraryBrowserPaletteContext context, string initialSearch) =>
                LibraryCommandPaletteWindow.Show(owner, context, initialSearch);

            public void LibraryBrowserPaletteOpenSettings() => _m.LibraryBrowserPaletteOpenSettings();

            public void LibraryBrowserPaletteOpenHealthDashboard(Window owner) => _m.LibraryBrowserPaletteOpenHealthDashboard(owner);

            public void LibraryBrowserPaletteOpenGameIndex() => _m.LibraryBrowserPaletteOpenGameIndex();

            public void LibraryBrowserPaletteOpenPhotoIndex() => _m.LibraryBrowserPaletteOpenPhotoIndex();

            public void LibraryBrowserPaletteOpenFilenameRules() => _m.LibraryBrowserPaletteOpenFilenameRules();

            public void LibraryBrowserPaletteOpenPhotographyGallery(Window owner) => _m.LibraryBrowserPaletteOpenPhotographyGallery(owner);

            public void LibraryBrowserPaletteOpenSavedCoversFolder() => _m.LibraryBrowserPaletteOpenSavedCoversFolder();

            public void LibraryBrowserPaletteRunImport(bool withReview) => _m.LibraryBrowserPaletteRunImport(withReview);

            public void LibraryBrowserPaletteOpenManualIntake() => _m.LibraryBrowserPaletteOpenManualIntake();

            public void LibraryBrowserPaletteShowIntakePreview() => _m.LibraryBrowserPaletteShowIntakePreview();

            public void LibraryBrowserPaletteExportStarred(Window owner) => _m.LibraryBrowserPaletteExportStarred(owner);

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
                Window owner,
                LibraryBrowserWorkingSet ws,
                Func<List<string>> getSelectedDetailFiles,
                Action renderTiles,
                Action renderSelectedFolder,
                Action<bool> refreshLibraryFoldersAsync) =>
                _m.LibraryBrowserDeleteSelectedCaptures(owner, ws, getSelectedDetailFiles, renderTiles, renderSelectedFolder, refreshLibraryFoldersAsync);

            public void LibraryBrowserRenderSelectedFolderDetail(
                LibraryBrowserWorkingSet ws,
                Window libraryWindow,
                Action<string> openSingleFileMetadataEditor,
                Action<string, ModifierKeys> updateDetailSelection,
                Action refreshDetailSelectionUi,
                Action redrawSelectedFolderDetail,
                Action renderFolderTiles) =>
                _m.LibraryBrowserRenderSelectedFolderDetail(ws, libraryWindow, openSingleFileMetadataEditor, updateDetailSelection, refreshDetailSelectionUi, redrawSelectedFolderDetail, renderFolderTiles);

            public FrameworkElement LibraryBrowserBuildFolderTile(
                LibraryBrowserFolderView folder,
                int tileWidth,
                int tileHeight,
                bool showPlatformBadge,
                Action<LibraryBrowserFolderView> showFolder,
                Action renderTiles,
                Action<bool> refreshLibraryFoldersAsync,
                Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh,
                Action<LibraryBrowserFolderView> openLibraryMetadataEditor,
                Action<string> libraryToast,
                LibraryBrowserWorkingSet ws) =>
                _m.LibraryBrowserBuildFolderTile(folder, tileWidth, tileHeight, showPlatformBadge, showFolder, renderTiles, refreshLibraryFoldersAsync, runScopedCoverRefresh, openLibraryMetadataEditor, libraryToast, ws);

            public void LibraryBrowserExitPhotoWorkspace(LibraryBrowserWorkingSet ws, Action renderTiles) =>
                _m.LibraryBrowserExitPhotoWorkspace(ws, renderTiles);

            public void LibraryBrowserEnterPhotoWorkspaceFromSelection(LibraryBrowserWorkingSet ws, Action<LibraryBrowserFolderView> showFolder) =>
                _m.LibraryBrowserEnterPhotoWorkspaceFromSelection(ws, showFolder);

            public void LibraryBrowserShowSelectedFolder(
                LibraryBrowserWorkingSet ws,
                LibraryBrowserPaneRefs panes,
                Window libraryWindow,
                LibraryBrowserFolderView info,
                Action renderSelectedFolder) =>
                _m.LibraryBrowserShowSelectedFolder(ws, panes, libraryWindow, info, renderSelectedFolder);

            public void LibraryBrowserRenderFolderList(
                LibraryBrowserWorkingSet ws,
                Func<LibraryBrowserFolderView, int, int, bool, FrameworkElement> buildFolderTile,
                Action<LibraryBrowserFolderView> showFolder,
                Action renderSelectedFolder,
                Action selfRerender,
                Action clearLibrarySearchAndRerender,
                Action refreshLibraryFoldersLoose) =>
                _m.LibraryBrowserRenderFolderList(ws, buildFolderTile, showFolder, renderSelectedFolder, selfRerender, clearLibrarySearchAndRerender, refreshLibraryFoldersLoose);

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
                bool reloadLibraryFolderListAfter,
                Action repaintLibraryBrowserChrome,
                Action<bool> refreshLibraryFoldersAsync,
                Action<bool> setLibraryBusyState) =>
                _m.RunLibraryBrowserScopedCoverRefresh(libraryWindow, ws, requestedFolders, scopeLabel, forceRefreshExistingCovers, rebuildFullCacheAfterRefresh, reloadLibraryFolderListAfter, repaintLibraryBrowserChrome, refreshLibraryFoldersAsync, setLibraryBusyState);

            public void PersistLibraryBrowserCommittedSearch(string text) => _m.PersistLibraryBrowserCommittedSearch(text);

            public void PrepareLibraryBrowserSessionForRebuild() => _m.PrepareLibraryBrowserSessionForRebuild();

            public void RegisterLibraryBrowserLiveWorkingSet(LibraryBrowserWorkingSet ws) => _m.RegisterLibraryBrowserLiveWorkingSet(ws);

            public void ScheduleDeferredGameIndexWarmup(Window libraryWindow) => _m.ScheduleDeferredGameIndexWarmup(libraryWindow);

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
                Action<string> setLibrarySortMode,
                Action<string> setLibraryFilterMode) =>
                _m.LibraryBrowserWireNavChromeAndToolbar(libraryWindow, ws, panes, navChrome, refreshIntakeReviewBadge, refreshLibraryFoldersAsync, runCoverRefresh, openSelectedLibraryMetadataEditor, deleteSelectedLibraryFiles, setLibraryGroupingMode, setLibrarySortMode, setLibraryFilterMode);

            public void LibraryBrowserMountQuickEditDrawer(Grid root, LibraryBrowserWorkingSet ws) =>
                _m.LibraryBrowserMountQuickEditDrawer(root, ws);

            public void LibraryBrowserSetQuickEditDrawerOpen(LibraryBrowserWorkingSet ws, bool open) =>
                _m.LibraryBrowserSetQuickEditDrawerOpen(ws, open);

            public void LibraryBrowserToggleQuickEditDrawer(LibraryBrowserWorkingSet ws) =>
                _m.LibraryBrowserToggleQuickEditDrawer(ws);
        }
    }
}
