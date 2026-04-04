using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Imperative Library browser open/show wiring (folder grid, detail pane, toolbar delegates).
        /// Keeps <see cref="ShowLibraryBrowserCore"/> thin and gives Library orchestration a named home (Phase E depth).
        /// </summary>
        sealed class LibraryBrowserShowOrchestration
        {
            readonly MainWindow _host;

            internal LibraryBrowserShowOrchestration(MainWindow host)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
            }

            internal void Run(bool reuseMainWindow)
            {
                _host.MarkLibraryBrowserSessionFirstPaintTracking();
                _host.librarySession.EnsureLibraryRootAccessible("Library folder");
                Action refreshIntakeReviewBadge = null;
                var libraryWindow = _host.GetOrCreateLibraryBrowserWindow(reuseMainWindow);
                var root = new Grid { Background = _host.Brush("#0F1519") };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var navChrome = _host.BuildLibraryBrowserNavChrome();
                root.Children.Add(navChrome.NavBar);

                var contentGrid = new Grid();
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star), MinWidth = 280 });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star), MinWidth = 260 });
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                var panes = _host.BuildLibraryBrowserContentPanes(contentGrid);
                libraryWindow.Content = root;

                var ws = new LibraryBrowserWorkingSet { Panes = panes };
                ws.AppliedLibrarySearchText = _host._libraryBrowserPersistedSearch ?? string.Empty;
                ws.PendingLibrarySearchText = ws.AppliedLibrarySearchText;
                panes.SearchBox.Text = ws.AppliedLibrarySearchText;
                if (_host._libraryBrowserPersistedFolderScroll > 0.1d)
                {
                    ws.PreserveFolderScrollOnNextRender = true;
                    ws.PreservedFolderScrollOffset = _host._libraryBrowserPersistedFolderScroll;
                }
                if (!string.IsNullOrWhiteSpace(_host._libraryBrowserPersistedLastViewKey))
                {
                    ws.PendingSessionRestore = true;
                    ws.PendingRestoreViewKey = _host._libraryBrowserPersistedLastViewKey;
                    ws.PendingRestoreDetailScrollAfterShow = Math.Max(0, _host._libraryBrowserPersistedDetailScroll);
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
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder = delegate(LibraryBrowserFolderView view)
                {
                    return _host.BuildLibraryBrowserDisplayFolder(view);
                };
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder = delegate(LibraryBrowserFolderView view)
                {
                    return _host.GetLibraryBrowserPrimaryFolder(view) ?? _host.BuildLibraryBrowserDisplayFolder(view);
                };

                Func<List<string>> getVisibleDetailFilesOrdered =
                    _host.LibraryBrowserCreateVisibleDetailFilesOrdered(ws, getDisplayFolder);

                Func<List<string>> getSelectedDetailFiles =
                    _host.LibraryBrowserCreateSelectedDetailFiles(ws, getVisibleDetailFilesOrdered);

                Action<string, ModifierKeys> updateDetailSelection = _host.LibraryBrowserCreateUpdateDetailSelection(
                    ws,
                    getVisibleDetailFilesOrdered,
                    delegate { if (refreshDetailSelectionUi != null) refreshDetailSelectionUi(); });

                refreshDetailSelectionUi = _host.LibraryBrowserCreateRefreshDetailSelectionUi(ws, panes, getSelectedDetailFiles);
                panes.DetailRows.BeforeVisibleRowsRebuilt = delegate
                {
                    ws.DetailTiles.Clear();
                };
                panes.DetailRows.AfterVisibleRowsRebuilt = delegate
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshIntakeReviewBadge = delegate { _host.LibraryBrowserScheduleIntakeReviewBadgeRefresh(libraryWindow, ws, navChrome); };

                refreshSortButtons = delegate
                {
                    var normalized = _host.NormalizeLibraryFolderSortMode(_host.libraryFolderSortMode);
                    _host.LibraryBrowserApplySortGroupPillState(panes.SortPlatformButton, string.Equals(normalized, "platform", StringComparison.OrdinalIgnoreCase));
                    _host.LibraryBrowserApplySortGroupPillState(panes.SortRecentButton, string.Equals(normalized, "recent", StringComparison.OrdinalIgnoreCase));
                    _host.LibraryBrowserApplySortGroupPillState(panes.SortPhotosButton, string.Equals(normalized, "photos", StringComparison.OrdinalIgnoreCase));
                };

                refreshGroupingButtons = delegate
                {
                    var normalized = _host.NormalizeLibraryGroupingMode(_host.libraryGroupingMode);
                    _host.LibraryBrowserApplySortGroupPillState(panes.GroupAllButton, string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase));
                    _host.LibraryBrowserApplySortGroupPillState(panes.GroupConsoleButton, string.Equals(normalized, "console", StringComparison.OrdinalIgnoreCase));
                };

                setLibrarySortMode = delegate(string mode)
                {
                    var normalized = _host.NormalizeLibraryFolderSortMode(mode);
                    if (string.Equals(normalized, "played", StringComparison.OrdinalIgnoreCase)) normalized = "recent";
                    if (!string.Equals(normalized, _host.NormalizeLibraryFolderSortMode(_host.libraryFolderSortMode), StringComparison.OrdinalIgnoreCase))
                    {
                        _host.libraryFolderSortMode = normalized;
                        _host.SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons();
                };

                setLibraryGroupingMode = delegate(string mode)
                {
                    var normalized = _host.NormalizeLibraryGroupingMode(mode);
                    if (!string.Equals(normalized, _host.NormalizeLibraryGroupingMode(_host.libraryGroupingMode), StringComparison.OrdinalIgnoreCase))
                    {
                        _host.libraryGroupingMode = normalized;
                        _host.SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshGroupingButtons();
                };

                openSingleFileMetadataEditor = delegate(string filePath)
                {
                    _host.LibraryBrowserOpenSingleFileMetadataEditor(ws, filePath, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                };

                openSelectedLibraryMetadataEditor = delegate
                {
                    _host.LibraryBrowserOpenSingleFileMetadataEditor(ws, null, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                };

                deleteSelectedLibraryFiles = delegate
                {
                    _host.LibraryBrowserDeleteSelectedCaptures(ws, getSelectedDetailFiles, renderTiles, renderSelectedFolder, refreshLibraryFoldersAsync);
                };

                renderSelectedFolder = delegate
                {
                    _host.LibraryBrowserRenderSelectedFolderDetail(ws, libraryWindow, openSingleFileMetadataEditor, updateDetailSelection, refreshDetailSelectionUi);
                    if (ws.Current == null) ws.DetailSelectionAnchorIndex = -1;
                };

                Func<LibraryBrowserFolderView, int, int, bool, Button> buildFolderTile = delegate(LibraryBrowserFolderView folder, int tileWidth, int tileHeight, bool showPlatformBadge)
                {
                    return _host.LibraryBrowserBuildFolderTile(
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
                    _host.LibraryBrowserShowSelectedFolder(ws, panes, libraryWindow, info, renderSelectedFolder);
                };

                Action renderFolderTilesCore = null;
                renderFolderTilesCore = delegate
                {
                    _host.LibraryBrowserRenderFolderList(ws, buildFolderTile, showFolder, renderSelectedFolder, renderFolderTilesCore);
                };
                renderTiles = renderFolderTilesCore;

                refreshLibraryFoldersAsync = delegate(bool forceRefresh)
                {
                    _host.LibraryBrowserRefreshFoldersAsync(libraryWindow, ws, forceRefresh, renderTiles);
                };
                _host.activeLibraryFolderRefresh = refreshLibraryFoldersAsync;
                if (!reuseMainWindow)
                {
                    libraryWindow.Closed += delegate
                    {
                        if (_host.activeLibraryFolderRefresh == refreshLibraryFoldersAsync) _host.activeLibraryFolderRefresh = null;
                        _host.activeSelectedLibraryFolder = null;
                    };
                }

                prefillLibraryFoldersFromSnapshotAsync = delegate
                {
                    _host.LibraryBrowserPrefillFoldersFromSnapshot(libraryWindow, ws, renderTiles);
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
                    _host.LibraryBrowserRunFolderMetadataScan(libraryWindow, ws, folderPath, forceRescan, setLibraryBusyState, refreshLibraryFoldersAsync);
                };

                runScopedCoverRefresh = delegate(List<LibraryFolderInfo> requestedFolders, string scopeLabel, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh)
                {
                    _host.RunLibraryBrowserScopedCoverRefresh(
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
                    _host.PersistLibraryBrowserCommittedSearch(ws.AppliedLibrarySearchText);
                    if (renderTiles != null) renderTiles();
                };
                _host.LibraryBrowserWirePaneEvents(libraryWindow, ws, panes, renderTiles, renderSelectedFolder, applySearchFilter);

                openLibraryMetadataEditor = delegate(LibraryBrowserFolderView focusFolder)
                {
                    _host.LibraryBrowserOpenLibraryMetadataForFolder(ws, focusFolder, showFolder, refreshDetailSelectionUi, delegate
                    {
                        _host.LibraryBrowserOpenSingleFileMetadataEditor(ws, null, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                    });
                };
                _host.LibraryBrowserWireNavChromeAndToolbar(
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
                var autoRefreshLibraryFoldersOnStartup = !_host.librarySession.HasLibraryFolderCacheSnapshot();
                if (!autoRefreshLibraryFoldersOnStartup && _host.status != null) _host.status.Text = "Loading cached library folders...";
                if (prefillLibraryFoldersFromSnapshotAsync != null) prefillLibraryFoldersFromSnapshotAsync();
                if (refreshLibraryFoldersAsync != null && autoRefreshLibraryFoldersOnStartup) refreshLibraryFoldersAsync(false);
            }
        }
    }
}
