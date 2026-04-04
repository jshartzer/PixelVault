using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// Imperative Library browser open/show wiring (folder grid, detail pane, toolbar delegates). Invoked from <see cref="LibraryBrowserHost.Show"/>.
        /// </summary>
        internal sealed class LibraryBrowserShowOrchestration
        {
            readonly ILibraryBrowserShell _shell;

            internal LibraryBrowserShowOrchestration(ILibraryBrowserShell shell)
            {
                _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            }

            internal void Run(bool reuseMainWindow)
            {
                if (reuseMainWindow) _shell.PrepareLibraryBrowserSessionForRebuild();
                _shell.MarkLibraryBrowserSessionFirstPaintTracking();
                _shell.LibrarySession.EnsureLibraryRootAccessible("Library folder");
                Action refreshIntakeReviewBadge = null;
                var libraryWindow = _shell.GetOrCreateLibraryBrowserWindow(reuseMainWindow);
                var root = new Grid { Background = _shell.BrushFromHex("#0F1519") };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var navChrome = _shell.BuildLibraryBrowserNavChrome();
                root.Children.Add(navChrome.NavBar);

                var contentGrid = new Grid();
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star), MinWidth = 280 });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star), MinWidth = 260 });
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                var panes = _shell.BuildLibraryBrowserContentPanes(contentGrid);
                var ws = new LibraryBrowserWorkingSet { Panes = panes };
                _shell.LibraryBrowserMountToastHost(root, ws);
                libraryWindow.Content = root;
                ws.AppliedLibrarySearchText = _shell.LibraryBrowserPersistedSearch;
                ws.PendingLibrarySearchText = ws.AppliedLibrarySearchText;
                panes.SearchBox.Text = ws.AppliedLibrarySearchText;
                if (_shell.LibraryBrowserPersistedFolderScroll > 0.1d)
                {
                    ws.PreserveFolderScrollOnNextRender = true;
                    ws.PreservedFolderScrollOffset = _shell.LibraryBrowserPersistedFolderScroll;
                }
                if (!string.IsNullOrWhiteSpace(_shell.LibraryBrowserPersistedLastViewKey))
                {
                    ws.PendingSessionRestore = true;
                    ws.PendingRestoreViewKey = _shell.LibraryBrowserPersistedLastViewKey;
                    ws.PendingRestoreDetailScrollAfterShow = Math.Max(0, _shell.LibraryBrowserPersistedDetailScroll);
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
                Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh = null;
                Action refreshDetailSelectionUi = null;
                Action deleteSelectedLibraryFiles = null;
                Action openSelectedLibraryMetadataEditor = null;
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getDisplayFolder = delegate(LibraryBrowserFolderView view)
                {
                    return _shell.BuildLibraryBrowserDisplayFolder(view);
                };
                Func<LibraryBrowserFolderView, LibraryFolderInfo> getActionFolder = delegate(LibraryBrowserFolderView view)
                {
                    return _shell.GetLibraryBrowserPrimaryFolder(view) ?? _shell.BuildLibraryBrowserDisplayFolder(view);
                };

                Func<List<string>> getVisibleDetailFilesOrdered =
                    _shell.LibraryBrowserCreateVisibleDetailFilesOrdered(ws, getDisplayFolder);

                Func<List<string>> getSelectedDetailFiles =
                    _shell.LibraryBrowserCreateSelectedDetailFiles(ws, getVisibleDetailFilesOrdered);

                Action<string, ModifierKeys> updateDetailSelection = _shell.LibraryBrowserCreateUpdateDetailSelection(
                    ws,
                    getVisibleDetailFilesOrdered,
                    delegate { if (refreshDetailSelectionUi != null) refreshDetailSelectionUi(); });

                refreshDetailSelectionUi = _shell.LibraryBrowserCreateRefreshDetailSelectionUi(ws, panes, getSelectedDetailFiles);
                panes.DetailRows.BeforeVisibleRowsRebuilt = delegate
                {
                    ws.DetailTiles.Clear();
                };
                panes.DetailRows.AfterVisibleRowsRebuilt = delegate
                {
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshIntakeReviewBadge = delegate { _shell.LibraryBrowserScheduleIntakeReviewBadgeRefresh(libraryWindow, ws, navChrome); };

                refreshSortButtons = delegate
                {
                    var normalized = _shell.NormalizeLibraryFolderSortMode(_shell.LibraryFolderSortMode);
                    _shell.LibraryBrowserApplySortGroupPillState(panes.SortPlatformButton, string.Equals(normalized, "platform", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.SortRecentButton, string.Equals(normalized, "recent", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.SortPhotosButton, string.Equals(normalized, "photos", StringComparison.OrdinalIgnoreCase));
                };

                refreshGroupingButtons = delegate
                {
                    var normalized = _shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode);
                    _shell.LibraryBrowserApplySortGroupPillState(panes.GroupAllButton, string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.GroupConsoleButton, string.Equals(normalized, "console", StringComparison.OrdinalIgnoreCase));
                };

                setLibrarySortMode = delegate(string mode)
                {
                    var normalized = _shell.NormalizeLibraryFolderSortMode(mode);
                    if (string.Equals(normalized, "played", StringComparison.OrdinalIgnoreCase)) normalized = "recent";
                    if (!string.Equals(normalized, _shell.NormalizeLibraryFolderSortMode(_shell.LibraryFolderSortMode), StringComparison.OrdinalIgnoreCase))
                    {
                        _shell.LibraryFolderSortMode = normalized;
                        _shell.SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons();
                };

                setLibraryGroupingMode = delegate(string mode)
                {
                    var normalized = _shell.NormalizeLibraryGroupingMode(mode);
                    if (!string.Equals(normalized, _shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode), StringComparison.OrdinalIgnoreCase))
                    {
                        _shell.LibraryGroupingMode = normalized;
                        _shell.SaveSettings();
                        if (renderTiles != null) renderTiles();
                    }
                    refreshGroupingButtons();
                };

                openSingleFileMetadataEditor = delegate(string filePath)
                {
                    _shell.LibraryBrowserOpenSingleFileMetadataEditor(ws, filePath, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                };

                openSelectedLibraryMetadataEditor = delegate
                {
                    _shell.LibraryBrowserOpenSingleFileMetadataEditor(ws, null, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                };

                deleteSelectedLibraryFiles = delegate
                {
                    _shell.LibraryBrowserDeleteSelectedCaptures(ws, getSelectedDetailFiles, renderTiles, renderSelectedFolder, refreshLibraryFoldersAsync);
                };

                renderSelectedFolder = delegate
                {
                    _shell.LibraryBrowserRenderSelectedFolderDetail(ws, libraryWindow, openSingleFileMetadataEditor, updateDetailSelection, refreshDetailSelectionUi, renderSelectedFolder, renderTiles);
                    if (ws.Current == null) ws.DetailSelectionAnchorIndex = -1;
                };

                Func<LibraryBrowserFolderView, int, int, bool, Button> buildFolderTile = delegate(LibraryBrowserFolderView folder, int tileWidth, int tileHeight, bool showPlatformBadge)
                {
                    return _shell.LibraryBrowserBuildFolderTile(
                        folder,
                        tileWidth,
                        tileHeight,
                        showPlatformBadge,
                        showFolder,
                        renderTiles,
                        refreshLibraryFoldersAsync,
                        runScopedCoverRefresh,
                        openLibraryMetadataEditor,
                        msg => _shell.LibraryBrowserShowToast(ws, msg));
                };

                showFolder = delegate(LibraryBrowserFolderView info)
                {
                    _shell.LibraryBrowserShowSelectedFolder(ws, panes, libraryWindow, info, renderSelectedFolder);
                };

                Action renderFolderTilesCore = null;
                renderFolderTilesCore = delegate
                {
                    _shell.LibraryBrowserRenderFolderList(ws, buildFolderTile, showFolder, renderSelectedFolder, renderFolderTilesCore);
                };
                renderTiles = renderFolderTilesCore;

                refreshLibraryFoldersAsync = delegate(bool forceRefresh)
                {
                    _shell.LibraryBrowserRefreshFoldersAsync(libraryWindow, ws, forceRefresh, renderTiles);
                };
                _shell.ActiveLibraryFolderRefresh = refreshLibraryFoldersAsync;
                if (!reuseMainWindow)
                {
                    libraryWindow.Closed += delegate
                    {
                        if (_shell.ActiveLibraryFolderRefresh == refreshLibraryFoldersAsync) _shell.ActiveLibraryFolderRefresh = null;
                        _shell.ActiveSelectedLibraryFolder = null;
                    };
                }

                prefillLibraryFoldersFromSnapshotAsync = delegate
                {
                    _shell.LibraryBrowserPrefillFoldersFromSnapshot(libraryWindow, ws, renderTiles);
                };
                setLibraryBusyState = delegate(bool isBusy)
                {
                    navChrome.RefreshButton.IsEnabled = !isBusy;
                    panes.EditMetadataButton.IsEnabled = !isBusy;
                    panes.RefreshThisFolderButton.IsEnabled = !isBusy && ws.Current != null;
                    panes.FolderTileSmallerButton.IsEnabled = !isBusy;
                    panes.FolderTileLargerButton.IsEnabled = !isBusy;
                    panes.ShortcutsHelpButton.IsEnabled = !isBusy;
                    if (isBusy) panes.UseSelectionAsCoverButton.IsEnabled = false;
                    navChrome.FetchButton.IsEnabled = !isBusy;
                    navChrome.ImportButton.IsEnabled = !isBusy;
                    navChrome.ImportCommentsButton.IsEnabled = !isBusy;
                    navChrome.ManualImportButton.IsEnabled = !isBusy;
                    if (navChrome.IntakeReviewButton != null) navChrome.IntakeReviewButton.IsEnabled = !isBusy;
                    if (!isBusy && refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                runLibraryScan = delegate(string folderPath, bool forceRescan)
                {
                    _shell.LibraryBrowserRunFolderMetadataScan(libraryWindow, ws, folderPath, forceRescan, setLibraryBusyState, refreshLibraryFoldersAsync);
                };

                Action repaintLibraryChromeAfterScopedCover = delegate
                {
                    if (renderTiles != null) renderTiles();
                    if (renderSelectedFolder != null) renderSelectedFolder();
                };
                runScopedCoverRefresh = delegate(List<LibraryFolderInfo> requestedFolders, string scopeLabel, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh, bool reloadLibraryFolderListAfter)
                {
                    _shell.RunLibraryBrowserScopedCoverRefresh(
                        libraryWindow,
                        ws,
                        requestedFolders,
                        scopeLabel,
                        forceRefreshExistingCovers,
                        rebuildFullCacheAfterRefresh,
                        reloadLibraryFolderListAfter,
                        repaintLibraryChromeAfterScopedCover,
                        refreshLibraryFoldersAsync,
                        setLibraryBusyState);
                };

                Action runCoverRefresh = delegate
                {
                    runScopedCoverRefresh(ws.Folders, "library", false, false, true);
                };
                applySearchFilter = delegate
                {
                    panes.SearchDebounceTimer.Stop();
                    if (string.Equals(ws.AppliedLibrarySearchText, ws.PendingLibrarySearchText, StringComparison.OrdinalIgnoreCase)) return;
                    ws.AppliedLibrarySearchText = ws.PendingLibrarySearchText;
                    _shell.PersistLibraryBrowserCommittedSearch(ws.AppliedLibrarySearchText);
                    if (renderTiles != null) renderTiles();
                };
                _shell.LibraryBrowserWirePaneEvents(libraryWindow, ws, panes, renderTiles, renderSelectedFolder, applySearchFilter);

                openLibraryMetadataEditor = delegate(LibraryBrowserFolderView focusFolder)
                {
                    _shell.LibraryBrowserOpenLibraryMetadataForFolder(ws, focusFolder, showFolder, refreshDetailSelectionUi, delegate
                    {
                        _shell.LibraryBrowserOpenSingleFileMetadataEditor(ws, null, getVisibleDetailFilesOrdered, getSelectedDetailFiles, getDisplayFolder, getActionFolder, refreshLibraryFoldersAsync);
                    });
                };
                _shell.LibraryBrowserWireNavChromeAndToolbar(
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
                panes.ShortcutsHelpButton.Click += delegate { _shell.ShowLibraryBrowserKeyboardShortcutsHelp(libraryWindow); };
                libraryWindow.PreviewKeyDown += delegate(object _, KeyEventArgs e)
                {
                    if (e.Key != Key.F1) return;
                    e.Handled = true;
                    _shell.ShowLibraryBrowserKeyboardShortcutsHelp(libraryWindow);
                };
                panes.FolderTileSmallerButton.Click += delegate
                {
                    _shell.LibraryFolderTileSize = _shell.NormalizeLibraryFolderTileSizeValue(_shell.LibraryFolderTileSize - 20);
                    _shell.SaveSettings();
                    if (renderTiles != null) renderTiles();
                    _shell.LibraryBrowserShowToast(ws, "Folder tiles: " + _shell.LibraryFolderTileSize);
                };
                panes.FolderTileLargerButton.Click += delegate
                {
                    _shell.LibraryFolderTileSize = _shell.NormalizeLibraryFolderTileSizeValue(_shell.LibraryFolderTileSize + 20);
                    _shell.SaveSettings();
                    if (renderTiles != null) renderTiles();
                    _shell.LibraryBrowserShowToast(ws, "Folder tiles: " + _shell.LibraryFolderTileSize);
                };
                panes.RefreshThisFolderButton.Click += delegate
                {
                    if (ws.Current == null) return;
                    var scopeFolders = _shell.GetLibraryBrowserActionFolders(ws.Current);
                    if (scopeFolders.Count == 0) return;
                    showFolder(ws.Current);
                    runScopedCoverRefresh(scopeFolders, _shell.BuildLibraryBrowserActionScopeLabel(ws.Current), true, false, false);
                };
                panes.UseSelectionAsCoverButton.Click += delegate
                {
                    var paths = getSelectedDetailFiles();
                    if (paths.Count != 1) return;
                    var path = paths[0];
                    if (!_shell.IsLibraryRasterImageFilePath(path) || !File.Exists(path)) return;
                    var coverFolder = _shell.ActiveSelectedLibraryFolder;
                    if (coverFolder == null) return;
                    _shell.LibrarySaveCustomCover(coverFolder, path);
                    if (renderTiles != null) renderTiles();
                    if (ws.Current != null) showFolder(ws.Current);
                    _shell.LibraryBrowserShowToast(ws, "Cover saved");
                };
                libraryWindow.Activated += delegate
                {
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };

                refreshSortButtons();
                refreshGroupingButtons();
                if (reuseMainWindow) _shell.RegisterLibraryBrowserLiveWorkingSet(ws);
                if (!reuseMainWindow) libraryWindow.Show();
                if (renderTiles != null) renderTiles();
                if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                var autoRefreshLibraryFoldersOnStartup = !_shell.LibrarySession.HasLibraryFolderCacheSnapshot();
                if (!autoRefreshLibraryFoldersOnStartup && _shell.StatusLine != null) _shell.StatusLine.Text = "Loading cached library folders...";
                if (prefillLibraryFoldersFromSnapshotAsync != null) prefillLibraryFoldersFromSnapshotAsync();
                if (refreshLibraryFoldersAsync != null && autoRefreshLibraryFoldersOnStartup) refreshLibraryFoldersAsync(false);
            }
        }
    }
}
