using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Forms = System.Windows.Forms;

namespace PixelVaultNative
{
    // Library browser UI orchestration (folder/detail panes, toolbar). Entry: LibraryBrowserHost → ShowLibraryBrowserCore.
    public sealed partial class MainWindow
    {
        internal void ShowLibraryBrowserCore(bool reuseMainWindow = false)
        {
            try
            {
                MarkLibraryBrowserSessionFirstPaintTracking();
                librarySession.EnsureLibraryRootAccessible("Library folder");
                Action refreshIntakeReviewBadge = null;
                var libraryWindow = GetOrCreateLibraryBrowserWindow(reuseMainWindow);
                var root = new Grid { Background = Brush("#0F1519") };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var navChrome = BuildLibraryBrowserNavChrome();
                root.Children.Add(navChrome.NavBar);

                var contentGrid = new Grid();
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star), MinWidth = 280 });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star), MinWidth = 260 });
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                var panes = BuildLibraryBrowserContentPanes(contentGrid);
                libraryWindow.Content = root;

                var ws = new LibraryBrowserWorkingSet { Panes = panes };
                ws.AppliedLibrarySearchText = _libraryBrowserPersistedSearch ?? string.Empty;
                ws.PendingLibrarySearchText = ws.AppliedLibrarySearchText;
                panes.SearchBox.Text = ws.AppliedLibrarySearchText;
                if (_libraryBrowserPersistedFolderScroll > 0.1d)
                {
                    ws.PreserveFolderScrollOnNextRender = true;
                    ws.PreservedFolderScrollOffset = _libraryBrowserPersistedFolderScroll;
                }
                if (!string.IsNullOrWhiteSpace(_libraryBrowserPersistedLastViewKey))
                {
                    ws.PendingSessionRestore = true;
                    ws.PendingRestoreViewKey = _libraryBrowserPersistedLastViewKey;
                    ws.PendingRestoreDetailScrollAfterShow = Math.Max(0, _libraryBrowserPersistedDetailScroll);
                }

                Action<string, bool> runLibraryScan = null;
                Action<bool> setLibraryBusyState = null;
                Action<LibraryBrowserFolderView> openLibraryMetadataEditor = null;
                Action<string> openSingleFileMetadataEditor = null;
                Action renderTiles = null;
                Action renderSelectedFolder = null;
                Action<bool> refreshLibraryFoldersAsync = null;
                Action prefillLibraryFoldersFromSnapshotAsync = null;
                Action applySearchFilter = null;
                Action refreshSortButtons = null;
                Action refreshGroupingButtons = null;
                Action<string> setLibrarySortMode = null;
                Action<string> setLibraryGroupingMode = null;
                Action<LibraryBrowserFolderView> showFolder = null;
                Action<List<LibraryFolderInfo>, string, bool, bool> runScopedCoverRefresh = null;
                Action refreshDetailSelectionUi = null;
                Action deleteSelectedLibraryFiles = null;
                Action openSelectedLibraryMetadataEditor = null;
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder = delegate(LibraryBrowserFolderView view) { return BuildLibraryBrowserDisplayFolder(view); };
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder = delegate(LibraryBrowserFolderView view)
                {
                    return GetLibraryBrowserPrimaryFolder(view) ?? BuildLibraryBrowserDisplayFolder(view);
                };

                Func<List<string>> getVisibleDetailFilesOrdered =
                    LibraryBrowserCreateVisibleDetailFilesOrdered(ws, getDisplayFolder);

                Func<List<string>> getSelectedDetailFiles =
                    LibraryBrowserCreateSelectedDetailFiles(ws, getVisibleDetailFilesOrdered);

                Action<string, ModifierKeys> updateDetailSelection = LibraryBrowserCreateUpdateDetailSelection(
                    ws,
                    getVisibleDetailFilesOrdered,
                    delegate { if (refreshDetailSelectionUi != null) refreshDetailSelectionUi(); });

                refreshDetailSelectionUi = LibraryBrowserCreateRefreshDetailSelectionUi(ws, panes, getSelectedDetailFiles);
                panes.DetailRows.BeforeVisibleRowsRebuilt = delegate
                {
                    ws.DetailTiles.Clear();
                };
                panes.DetailRows.AfterVisibleRowsRebuilt = delegate
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshIntakeReviewBadge = delegate { LibraryBrowserScheduleIntakeReviewBadgeRefresh(libraryWindow, ws, navChrome); };

                refreshSortButtons = delegate
                {
                    var normalized = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
                    LibraryBrowserApplySortGroupPillState(panes.SortPlatformButton, string.Equals(normalized, "platform", StringComparison.OrdinalIgnoreCase));
                    LibraryBrowserApplySortGroupPillState(panes.SortRecentButton, string.Equals(normalized, "recent", StringComparison.OrdinalIgnoreCase));
                    LibraryBrowserApplySortGroupPillState(panes.SortPhotosButton, string.Equals(normalized, "photos", StringComparison.OrdinalIgnoreCase));
                };

                refreshGroupingButtons = delegate
                {
                    var normalized = NormalizeLibraryGroupingMode(libraryGroupingMode);
                    LibraryBrowserApplySortGroupPillState(panes.GroupAllButton, string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase));
                    LibraryBrowserApplySortGroupPillState(panes.GroupConsoleButton, string.Equals(normalized, "console", StringComparison.OrdinalIgnoreCase));
                };

                setLibrarySortMode = delegate(string mode)
                {
                    var normalized = NormalizeLibraryFolderSortMode(mode);
                    if (string.Equals(normalized, "played", StringComparison.OrdinalIgnoreCase)) normalized = "recent";
                    if (!string.Equals(normalized, NormalizeLibraryFolderSortMode(libraryFolderSortMode), StringComparison.OrdinalIgnoreCase))
                    {
                        libraryFolderSortMode = normalized;
                        SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons();
                };

                setLibraryGroupingMode = delegate(string mode)
                {
                    var normalized = NormalizeLibraryGroupingMode(mode);
                    if (!string.Equals(normalized, NormalizeLibraryGroupingMode(libraryGroupingMode), StringComparison.OrdinalIgnoreCase))
                    {
                        libraryGroupingMode = normalized;
                        SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshGroupingButtons();
                };

                openSingleFileMetadataEditor = delegate(string filePath)
                {
                    LibraryBrowserOpenSingleFileMetadataEditor(ws, filePath, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                };

                openSelectedLibraryMetadataEditor = delegate
                {
                    LibraryBrowserOpenSingleFileMetadataEditor(ws, null, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                };

                deleteSelectedLibraryFiles = delegate
                {
                    LibraryBrowserDeleteSelectedCaptures(ws, getSelectedDetailFiles, renderTiles, renderSelectedFolder, refreshLibraryFoldersAsync);
                };

                renderSelectedFolder = delegate
                {
                    LibraryBrowserRenderSelectedFolderDetail(ws, libraryWindow, openSingleFileMetadataEditor, updateDetailSelection, refreshDetailSelectionUi);
                    if (ws.Current == null) ws.DetailSelectionAnchorIndex = -1;
                };

                Func<LibraryBrowserFolderView, int, int, bool, Button> buildFolderTile = delegate(LibraryBrowserFolderView folder, int tileWidth, int tileHeight, bool showPlatformBadge)
                {
                    return LibraryBrowserBuildFolderTile(
                        folder,
                        tileWidth,
                        tileHeight,
                        showPlatformBadge,
                        showFolder,
                        renderTiles,
                        refreshLibraryFoldersAsync,
                        runScopedCoverRefresh,
                        openLibraryMetadataEditor);
                };

                showFolder = delegate(LibraryBrowserFolderView info)
                {
                    LibraryBrowserShowSelectedFolder(ws, panes, libraryWindow, info, renderSelectedFolder);
                };

                Action renderFolderTilesCore = null;
                renderFolderTilesCore = delegate
                {
                    LibraryBrowserRenderFolderList(ws, buildFolderTile, showFolder, renderSelectedFolder, renderFolderTilesCore);
                };
                renderTiles = renderFolderTilesCore;

                refreshLibraryFoldersAsync = delegate(bool forceRefresh)
                {
                    LibraryBrowserRefreshFoldersAsync(libraryWindow, ws, forceRefresh, renderTiles);
                };
                activeLibraryFolderRefresh = refreshLibraryFoldersAsync;
                if (!reuseMainWindow)
                {
                    libraryWindow.Closed += delegate
                    {
                        if (activeLibraryFolderRefresh == refreshLibraryFoldersAsync) activeLibraryFolderRefresh = null;
                        activeSelectedLibraryFolder = null;
                    };
                }

                prefillLibraryFoldersFromSnapshotAsync = delegate
                {
                    LibraryBrowserPrefillFoldersFromSnapshot(libraryWindow, ws, renderTiles);
                };
                setLibraryBusyState = delegate(bool isBusy)
                {
                    navChrome.RefreshButton.IsEnabled = !isBusy;
                    panes.EditMetadataButton.IsEnabled = !isBusy;
                    navChrome.FetchButton.IsEnabled = !isBusy;
                    navChrome.ImportButton.IsEnabled = !isBusy;
                    navChrome.ImportCommentsButton.IsEnabled = !isBusy;
                    navChrome.ManualImportButton.IsEnabled = !isBusy;
                    if (navChrome.IntakeReviewButton != null) navChrome.IntakeReviewButton.IsEnabled = !isBusy;
                };

                runLibraryScan = delegate(string folderPath, bool forceRescan)
                {
                    LibraryBrowserRunFolderMetadataScan(libraryWindow, ws, folderPath, forceRescan, setLibraryBusyState, refreshLibraryFoldersAsync);
                };

                runScopedCoverRefresh = delegate(List<LibraryFolderInfo> requestedFolders, string scopeLabel, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh)
                {
                    RunLibraryBrowserScopedCoverRefresh(
                        libraryWindow,
                        ws,
                        requestedFolders,
                        scopeLabel,
                        forceRefreshExistingCovers,
                        rebuildFullCacheAfterRefresh,
                        refreshLibraryFoldersAsync,
                        setLibraryBusyState);
                };

                Action runCoverRefresh = delegate
                {
                    runScopedCoverRefresh(ws.Folders, "library", false, false);
                };
                applySearchFilter = delegate
                {
                    panes.SearchDebounceTimer.Stop();
                    if (string.Equals(ws.AppliedLibrarySearchText, ws.PendingLibrarySearchText, StringComparison.OrdinalIgnoreCase)) return;
                    ws.AppliedLibrarySearchText = ws.PendingLibrarySearchText;
                    PersistLibraryBrowserCommittedSearch(ws.AppliedLibrarySearchText);
                    if (renderTiles != null) renderTiles();
                };
                LibraryBrowserWirePaneEvents(libraryWindow, ws, panes, renderTiles, renderSelectedFolder, applySearchFilter);

                openLibraryMetadataEditor = delegate(LibraryBrowserFolderView focusFolder)
                {
                    LibraryBrowserOpenLibraryMetadataForFolder(ws, focusFolder, showFolder, refreshDetailSelectionUi, delegate
                    {
                        LibraryBrowserOpenSingleFileMetadataEditor(ws, null, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                    });
                };
                LibraryBrowserWireNavChromeAndToolbar(
                    libraryWindow,
                    ws,
                    panes,
                    navChrome,
                    refreshIntakeReviewBadge,
                    refreshLibraryFoldersAsync,
                    runCoverRefresh,
                    openSelectedLibraryMetadataEditor,
                    deleteSelectedLibraryFiles,
                    setLibraryGroupingMode,
                    setLibrarySortMode);
                libraryWindow.Activated += delegate
                {
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };

                refreshSortButtons();
                refreshGroupingButtons();
                if (!reuseMainWindow) libraryWindow.Show();
                if (renderTiles != null) renderTiles();
                if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                var autoRefreshLibraryFoldersOnStartup = !librarySession.HasLibraryFolderCacheSnapshot();
                if (!autoRefreshLibraryFoldersOnStartup && status != null) status.Text = "Loading cached library folders...";
                if (prefillLibraryFoldersFromSnapshotAsync != null) prefillLibraryFoldersFromSnapshotAsync();
                if (refreshLibraryFoldersAsync != null && autoRefreshLibraryFoldersOnStartup) refreshLibraryFoldersAsync(false);
            }
            catch (Exception ex)
            {
                LogException("ShowLibraryBrowserCore", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
