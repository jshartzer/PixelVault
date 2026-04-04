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
        string LibraryGroupingMode { get; set; }

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
        string NormalizeLibraryGroupingMode(string value);

        void LibraryBrowserApplySortGroupPillState(Button button, bool active);
        void SaveSettings();

        void LibraryBrowserOpenSingleFileMetadataEditor(
            MainWindow.LibraryBrowserWorkingSet ws,
            string filePath,
            Func<List<string>> getVisibleDetailFilesOrdered,
            Func<List<string>> getSelectedDetailFiles,
            Func<MainWindow.LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder,
            Func<MainWindow.LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder,
            Action<bool> refreshLibraryFoldersAsync);

        void LibraryBrowserDeleteSelectedCaptures(
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
            Action redrawSelectedFolderDetail);

        Button LibraryBrowserBuildFolderTile(
            MainWindow.LibraryBrowserFolderView folder,
            int tileWidth,
            int tileHeight,
            bool showPlatformBadge,
            Action<MainWindow.LibraryBrowserFolderView> showFolder,
            Action renderTiles,
            Action<bool> refreshLibraryFoldersAsync,
            Action<List<LibraryFolderInfo>, string, bool, bool> runScopedCoverRefresh,
            Action<MainWindow.LibraryBrowserFolderView> openLibraryMetadataEditor);

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
            Action selfRerender);

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
            Action<bool> refreshLibraryFoldersAsync,
            Action<bool> setLibraryBusyState);

        void PersistLibraryBrowserCommittedSearch(string text);

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
            Action<string> setLibrarySortMode);
    }
}
