using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    /// <summary>
    /// Stable surface for <see cref="MainWindow.LibraryBrowserShowOrchestration"/> so Library show wiring does not depend on <see cref="MainWindow"/> as a type.
    /// </summary>
    internal interface ILibraryBrowserShell
    {
        ILibrarySession LibrarySession { get; }

        string LibraryBrowserPersistedSearch { get; }
        double LibraryBrowserPersistedFolderScroll { get; }
        string LibraryBrowserPersistedLastViewKey { get; }
        double LibraryBrowserPersistedDetailScroll { get; }

        string LibraryFolderSortMode { get; set; }
        string LibraryFolderFilterMode { get; set; }
        string LibraryGroupingMode { get; set; }
        int LibraryFolderTileSize { get; set; }
        int LibraryPhotoTileSize { get; set; }

        Action<bool> ActiveLibraryFolderRefresh { get; set; }
        LibraryFolderInfo ActiveSelectedLibraryFolder { get; set; }

        TextBlock StatusLine { get; }

        void MarkLibraryBrowserSessionFirstPaintTracking();
        Window GetOrCreateLibraryBrowserWindow(bool reuseMainWindow);
        SolidColorBrush BrushFromHex(string hex);

        MainWindow.LibraryBrowserNavChrome BuildLibraryBrowserNavChrome();
        MainWindow.LibraryBrowserPaneRefs BuildLibraryBrowserContentPanes(Grid contentGrid);

        LibraryFolderInfo BuildLibraryBrowserDisplayFolder(MainWindow.LibraryBrowserFolderView view);
        LibraryFolderInfo GetLibraryBrowserPrimaryFolder(MainWindow.LibraryBrowserFolderView view);

        Func<List<string>> LibraryBrowserCreateVisibleDetailFilesOrdered(
            MainWindow.LibraryBrowserWorkingSet ws,
            Func<MainWindow.LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder);

        Func<List<string>> LibraryBrowserCreateSelectedDetailFiles(
            MainWindow.LibraryBrowserWorkingSet ws,
            Func<List<string>> getVisibleDetailFilesOrdered);

        Action<string, ModifierKeys> LibraryBrowserCreateUpdateDetailSelection(
            MainWindow.LibraryBrowserWorkingSet ws,
            Func<List<string>> getVisibleDetailFilesOrdered,
            Action refreshDetailSelectionUi);

        Action LibraryBrowserCreateRefreshDetailSelectionUi(
            MainWindow.LibraryBrowserWorkingSet ws,
            MainWindow.LibraryBrowserPaneRefs panes,
            Func<List<string>> getSelectedDetailFiles);

        void LibraryBrowserScheduleIntakeReviewBadgeRefresh(
            Window libraryWindow,
            MainWindow.LibraryBrowserWorkingSet ws,
            MainWindow.LibraryBrowserNavChrome navChrome);

        string NormalizeLibraryFolderSortMode(string value);
        string NormalizeLibraryFolderFilterMode(string value);
        string LibraryFolderFilterModeLabel(string value);
        string NormalizeLibraryGroupingMode(string value);

        void LibraryBrowserApplySortGroupPillState(Button button, bool active);
        void SaveSettings();
        int NormalizeLibraryFolderTileSizeValue(int value);
        int NormalizeLibraryPhotoTileSizeValue(int value);
        List<LibraryFolderInfo> GetLibraryBrowserActionFolders(MainWindow.LibraryBrowserFolderView view);
        string BuildLibraryBrowserActionScopeLabel(MainWindow.LibraryBrowserFolderView view);
        void LibrarySaveCustomCover(LibraryFolderInfo folder, string sourcePath);
        bool IsLibraryRasterImageFilePath(string path);
        void LibraryBrowserMountToastHost(Grid rootGrid, MainWindow.LibraryBrowserWorkingSet ws);
        void LibraryBrowserShowToast(MainWindow.LibraryBrowserWorkingSet ws, string message);
        void ShowLibraryBrowserKeyboardShortcutsHelp(Window owner);
        void ShowLibraryCommandPalette(Window owner, LibraryBrowserPaletteContext context, string initialSearch);

        void LibraryBrowserPaletteOpenSettings();
        void LibraryBrowserPaletteOpenHealthDashboard(Window owner);
        void LibraryBrowserPaletteOpenGameIndex();
        void LibraryBrowserPaletteOpenPhotoIndex();
        void LibraryBrowserPaletteOpenFilenameRules();
        void LibraryBrowserPaletteOpenPhotographyGallery(Window owner);
        void LibraryBrowserPaletteOpenSavedCoversFolder();
        void LibraryBrowserPaletteRunImport(bool withReview);
        void LibraryBrowserPaletteOpenManualIntake();
        void LibraryBrowserPaletteShowIntakePreview();
        void LibraryBrowserPaletteExportStarred(Window owner);

        void LibraryBrowserOpenSingleFileMetadataEditor(
            MainWindow.LibraryBrowserWorkingSet ws,
            string filePath,
            Func<List<string>> getVisibleDetailFilesOrdered,
            Func<List<string>> getSelectedDetailFiles,
            Func<MainWindow.LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder,
            Func<MainWindow.LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder,
            Action<bool> refreshLibraryFoldersAsync);

        void LibraryBrowserDeleteSelectedCaptures(
            Window owner,
            MainWindow.LibraryBrowserWorkingSet ws,
            Func<List<string>> getSelectedDetailFiles,
            Action renderTiles,
            Action renderSelectedFolder,
            Action<bool> refreshLibraryFoldersAsync);

        void LibraryBrowserRenderSelectedFolderDetail(
            MainWindow.LibraryBrowserWorkingSet ws,
            Window libraryWindow,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail,
            Action renderFolderTiles);

        Button LibraryBrowserBuildFolderTile(
            MainWindow.LibraryBrowserFolderView folder,
            int tileWidth,
            int tileHeight,
            bool showPlatformBadge,
            Action<MainWindow.LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<bool> refreshLibraryFoldersAsync,
            Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh,
            Action<MainWindow.LibraryBrowserFolderView> openLibraryMetadataEditor,
            Action<string> libraryToast,
            MainWindow.LibraryBrowserWorkingSet ws);

        void LibraryBrowserExitPhotoWorkspace(MainWindow.LibraryBrowserWorkingSet ws, Action renderTiles);

        /// <summary>Enter Photo workspace using <see cref="MainWindow.LibraryBrowserWorkingSet.Current"/> if valid.</summary>
        void LibraryBrowserEnterPhotoWorkspaceFromSelection(MainWindow.LibraryBrowserWorkingSet ws, Action<MainWindow.LibraryBrowserFolderView> showFolder);

        void LibraryBrowserShowSelectedFolder(
            MainWindow.LibraryBrowserWorkingSet ws,
            MainWindow.LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            MainWindow.LibraryBrowserFolderView info,
            Action renderSelectedFolder);

        void LibraryBrowserRenderFolderList(
            MainWindow.LibraryBrowserWorkingSet ws,
            Func<MainWindow.LibraryBrowserFolderView, int, int, bool, Button> buildFolderTile,
            Action<MainWindow.LibraryBrowserFolderView> showFolder,
            Action renderSelectedFolder,
            Action selfRerender,
            Action clearLibrarySearchAndRerender,
            Action refreshLibraryFoldersLoose);

        void LibraryBrowserRefreshFoldersAsync(Window libraryWindow, MainWindow.LibraryBrowserWorkingSet ws, bool forceRefresh, Action renderTiles);
        void LibraryBrowserPrefillFoldersFromSnapshot(Window libraryWindow, MainWindow.LibraryBrowserWorkingSet ws, Action renderTiles);

        void LibraryBrowserRunFolderMetadataScan(
            Window libraryWindow,
            MainWindow.LibraryBrowserWorkingSet ws,
            string folderPath,
            bool forceRescan,
            Action<bool> setLibraryBusyState,
            Action<bool> refreshLibraryFoldersAsync);

        void RunLibraryBrowserScopedCoverRefresh(
            Window libraryWindow,
            MainWindow.LibraryBrowserWorkingSet ws,
            List<LibraryFolderInfo> requestedFolders,
            string scopeLabel,
            bool forceRefreshExistingCovers,
            bool rebuildFullCacheAfterRefresh,
            bool reloadLibraryFolderListAfter,
            Action repaintLibraryBrowserChrome,
            Action<bool> refreshLibraryFoldersAsync,
            Action<bool> setLibraryBusyState);

        void PersistLibraryBrowserCommittedSearch(string text);
        void PrepareLibraryBrowserSessionForRebuild();
        void RegisterLibraryBrowserLiveWorkingSet(MainWindow.LibraryBrowserWorkingSet ws);

        void ScheduleDeferredGameIndexWarmup(Window libraryWindow);

        void LibraryBrowserWirePaneEvents(
            Window libraryWindow,
            MainWindow.LibraryBrowserWorkingSet ws,
            MainWindow.LibraryBrowserPaneRefs panes,
            Action renderTiles,
            Action renderSelectedFolder,
            Action applySearchFilter);

        void LibraryBrowserOpenLibraryMetadataForFolder(
            MainWindow.LibraryBrowserWorkingSet ws,
            MainWindow.LibraryBrowserFolderView focusFolder,
            Action<MainWindow.LibraryBrowserFolderView> showFolder,
            Action refreshDetailSelectionUi,
            Action openMetadataForCurrentSelection);

        void LibraryBrowserWireNavChromeAndToolbar(
            Window libraryWindow,
            MainWindow.LibraryBrowserWorkingSet ws,
            MainWindow.LibraryBrowserPaneRefs panes,
            MainWindow.LibraryBrowserNavChrome navChrome,
            Action refreshIntakeReviewBadge,
            Action<bool> refreshLibraryFoldersAsync,
            Action runCoverRefresh,
            Action openSelectedLibraryMetadataEditor,
            Action deleteSelectedLibraryFiles,
            Action<string> setLibraryGroupingMode,
            Action<string> setLibrarySortMode,
            Action<string> setLibraryFilterMode);

        void LibraryBrowserMountQuickEditDrawer(Grid root, MainWindow.LibraryBrowserWorkingSet ws);

        void LibraryBrowserSetQuickEditDrawerOpen(MainWindow.LibraryBrowserWorkingSet ws, bool open);

        void LibraryBrowserToggleQuickEditDrawer(MainWindow.LibraryBrowserWorkingSet ws);
    }
}
