using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
                // ~1/4 folder pane, ~3/4 detail by default (user-adjustable splitter); narrower than 1:2 so more room for captures.
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 260 });
                Grid.SetRow(contentGrid, 1);
                root.Children.Add(contentGrid);

                var panes = _shell.BuildLibraryBrowserContentPanes(contentGrid);
                var ws = new LibraryBrowserWorkingSet { Panes = panes };
                MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, _shell.LibraryGroupingMode);
                _shell.LibraryBrowserMountToastHost(root, ws);
                _shell.LibraryBrowserMountQuickEditDrawer(root, ws);
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
                if (ws.TimelineStartDate <= DateTime.MinValue || ws.TimelineEndDate <= DateTime.MinValue)
                {
                    MainWindow.BuildLibraryTimelinePresetDateRange("30d", DateTime.Today, out ws.TimelineStartDate, out ws.TimelineEndDate);
                    ws.TimelineDatePresetKey = "30d";
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
                Action<string> setLibraryFilterMode = null;
                Action<string> setLibraryGroupingMode = null;
                Action<LibraryBrowserFolderView> showFolder = null;
                Action<List<LibraryFolderInfo>, string, bool, bool, bool> runScopedCoverRefresh = null;
                Action refreshDetailSelectionUi = null;
                Action deleteSelectedLibraryFiles = null;
                Action openSelectedLibraryMetadataEditor = null;
                Action refreshTimelineRangeUi = null;
                Action<string> applyTimelinePreset = null;
                Action applyTimelineDatePickerRange = null;
                var lastFolderGroupingMode = _shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode);
                if (string.Equals(lastFolderGroupingMode, "timeline", StringComparison.OrdinalIgnoreCase)) lastFolderGroupingMode = "all";
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
                    RepopulateLibraryDetailTilesFromVisibleRows(ws, panes.DetailRows);
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                };

                refreshIntakeReviewBadge = delegate { _shell.LibraryBrowserScheduleIntakeReviewBadgeRefresh(libraryWindow, ws, navChrome); };

                refreshSortButtons = delegate
                {
                    var timelineMode = string.Equals(_shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode), "timeline", StringComparison.OrdinalIgnoreCase);
                    var sortMode = _shell.NormalizeLibraryFolderSortMode(_shell.LibraryFolderSortMode);
                    var filterMode = _shell.NormalizeLibraryFolderFilterMode(_shell.LibraryFolderFilterMode);
                    var sortActive = !timelineMode && !string.Equals(sortMode, "alpha", StringComparison.OrdinalIgnoreCase);
                    var filterActive = !string.Equals(filterMode, "all", StringComparison.OrdinalIgnoreCase);
                    _shell.LibraryBrowserApplySortGroupPillState(panes.SortFilterMenuButton, sortActive || filterActive);
                    panes.SortFilterMenuButton.IsEnabled = !timelineMode;
                    var sortLabel =
                        string.Equals(sortMode, "captured", StringComparison.OrdinalIgnoreCase) ? "Date Captured" :
                        string.Equals(sortMode, "added", StringComparison.OrdinalIgnoreCase) ? "Date Added" :
                        string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? "Most Photos" :
                        "Alphabetical";
                    var filterLabel = _shell.LibraryFolderFilterModeLabel(filterMode);
                    panes.SortFilterMenuButton.ToolTip = timelineMode
                        ? "Sort is fixed in Timeline view; filter: " + filterLabel
                        : "Sort: " + sortLabel + "; Filter: " + filterLabel;
                    if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo) ws.RefreshPhotoRailColumnPickerUi?.Invoke();
                };
                ws.RefreshSortFilterChrome = refreshSortButtons;
                ws.RefreshPhotoRailColumnPickerUi = delegate
                {
                    if (panes.PhotoRailColumnOneButton == null || panes.PhotoRailColumnTwoButton == null) return;
                    var raw = _shell.LibraryPhotoRailFolderGridColumnCount;
                    var norm = _shell.NormalizeLibraryPhotoRailFolderGridColumnCountValue(raw);
                    var effective = norm <= 0 ? 2 : norm;
                    _shell.LibraryBrowserApplySortGroupPillState(panes.PhotoRailColumnOneButton, effective == 1);
                    _shell.LibraryBrowserApplySortGroupPillState(panes.PhotoRailColumnTwoButton, effective == 2);
                };

                refreshGroupingButtons = delegate
                {
                    var normalized = _shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode);
                    _shell.LibraryBrowserApplySortGroupPillState(panes.GroupAllButton, string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.GroupConsoleButton, string.Equals(normalized, "console", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.GroupTimelineButton, string.Equals(normalized, "timeline", StringComparison.OrdinalIgnoreCase));
                    refreshTimelineRangeUi?.Invoke();
                };

                var suppressTimelineRangeSync = false;
                refreshTimelineRangeUi = delegate
                {
                    if (panes.TimelineFilterPanel == null) return;
                    var timelineMode = string.Equals(_shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode), "timeline", StringComparison.OrdinalIgnoreCase);
                    panes.TimelineFilterPanel.Visibility = timelineMode ? Visibility.Visible : Visibility.Collapsed;
                    var rangeStart = ws.TimelineStartDate;
                    var rangeEnd = ws.TimelineEndDate;
                    MainWindow.NormalizeLibraryTimelineDateRange(ref rangeStart, ref rangeEnd);
                    ws.TimelineStartDate = rangeStart;
                    ws.TimelineEndDate = rangeEnd;
                    ws.TimelineDatePresetKey = MainWindow.DetectLibraryTimelinePresetKey(rangeStart, rangeEnd, DateTime.Today);
                    _shell.LibraryBrowserApplySortGroupPillState(panes.TimelinePresetTodayButton, string.Equals(ws.TimelineDatePresetKey, "today", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.TimelinePresetMonthButton, string.Equals(ws.TimelineDatePresetKey, "month", StringComparison.OrdinalIgnoreCase));
                    _shell.LibraryBrowserApplySortGroupPillState(panes.TimelinePresetThirtyDaysButton, string.Equals(ws.TimelineDatePresetKey, "30d", StringComparison.OrdinalIgnoreCase));
                    suppressTimelineRangeSync = true;
                    if (panes.TimelineStartDatePicker != null) panes.TimelineStartDatePicker.SelectedDate = rangeStart;
                    if (panes.TimelineEndDatePicker != null) panes.TimelineEndDatePicker.SelectedDate = rangeEnd;
                    suppressTimelineRangeSync = false;
                };
                Action<DateTime, DateTime, bool> applyTimelineDateRange = delegate(DateTime startDate, DateTime endDate, bool rerender)
                {
                    MainWindow.NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);
                    ws.TimelineStartDate = startDate;
                    ws.TimelineEndDate = endDate;
                    ws.TimelineDatePresetKey = MainWindow.DetectLibraryTimelinePresetKey(startDate, endDate, DateTime.Today);
                    refreshTimelineRangeUi?.Invoke();
                    if (!rerender) return;
                    if (!string.Equals(_shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode), "timeline", StringComparison.OrdinalIgnoreCase)) return;
                    if (ws.Current == null || renderSelectedFolder == null) return;
                    ws.PreserveDetailScrollOnNextRender = false;
                    ws.PreservedDetailScrollOffset = 0;
                    renderSelectedFolder();
                };
                applyTimelinePreset = delegate(string presetKey)
                {
                    DateTime startDate;
                    DateTime endDate;
                    MainWindow.BuildLibraryTimelinePresetDateRange(presetKey, DateTime.Today, out startDate, out endDate);
                    applyTimelineDateRange(startDate, endDate, true);
                };
                applyTimelineDatePickerRange = delegate
                {
                    if (suppressTimelineRangeSync) return;
                    if (panes.TimelineStartDatePicker == null || panes.TimelineEndDatePicker == null) return;
                    if (!panes.TimelineStartDatePicker.SelectedDate.HasValue || !panes.TimelineEndDatePicker.SelectedDate.HasValue) return;
                    applyTimelineDateRange(panes.TimelineStartDatePicker.SelectedDate.Value, panes.TimelineEndDatePicker.SelectedDate.Value, true);
                };

                setLibrarySortMode = delegate(string mode)
                {
                    var normalized = _shell.NormalizeLibraryFolderSortMode(mode);
                    if (!string.Equals(normalized, _shell.NormalizeLibraryFolderSortMode(_shell.LibraryFolderSortMode), StringComparison.OrdinalIgnoreCase))
                    {
                        _shell.LibraryFolderSortMode = normalized;
                        _shell.SaveSettings();
                        if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo) ws.ScrollPhotoRailSelectionToTopPending = true;
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons();
                };

                setLibraryFilterMode = delegate(string mode)
                {
                    var normalized = _shell.NormalizeLibraryFolderFilterMode(mode);
                    if (!string.Equals(normalized, _shell.NormalizeLibraryFolderFilterMode(_shell.LibraryFolderFilterMode), StringComparison.OrdinalIgnoreCase))
                    {
                        _shell.LibraryFolderFilterMode = normalized;
                        _shell.SaveSettings();
                        if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo) ws.ScrollPhotoRailSelectionToTopPending = true;
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons();
                };

                setLibraryGroupingMode = delegate(string mode)
                {
                    var normalized = string.Equals((mode ?? string.Empty).Trim(), "folders", StringComparison.OrdinalIgnoreCase)
                        ? lastFolderGroupingMode
                        : _shell.NormalizeLibraryGroupingMode(mode);
                    if (!string.Equals(normalized, "timeline", StringComparison.OrdinalIgnoreCase))
                    {
                        lastFolderGroupingMode = normalized;
                    }
                    var previousGrouping = _shell.NormalizeLibraryGroupingMode(_shell.LibraryGroupingMode);
                    if (!string.Equals(normalized, previousGrouping, StringComparison.OrdinalIgnoreCase))
                    {
                        _shell.LibraryGroupingMode = normalized;
                        _shell.SaveSettings();
                    }
                    // Must run before render: ApplyLibraryBrowserLayoutMode uses ws.WorkspaceMode (PV-PLN-LIBWS-001).
                    MainWindow.LibraryBrowserSyncWorkspaceModeWithGrouping(ws, _shell.LibraryGroupingMode);
                    if (!string.Equals(normalized, previousGrouping, StringComparison.OrdinalIgnoreCase))
                    {
                        if (renderTiles != null) renderTiles();
                    }
                    refreshSortButtons?.Invoke();
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
                    _shell.LibraryBrowserDeleteSelectedCaptures(libraryWindow, ws, getSelectedDetailFiles, renderTiles, renderSelectedFolder, refreshLibraryFoldersAsync);
                };

                renderSelectedFolder = delegate
                {
                    _shell.LibraryBrowserRenderSelectedFolderDetail(ws, libraryWindow, openSingleFileMetadataEditor, updateDetailSelection, refreshDetailSelectionUi, renderSelectedFolder, renderTiles);
                    if (ws.Current == null) ws.DetailSelectionAnchorIndex = -1;
                    _shell.LibraryBrowserRefreshPhotoWorkspaceHeroBanner(ws, panes, libraryWindow, ws.Current);
                };

                Func<LibraryBrowserFolderView, int, int, bool, FrameworkElement> buildFolderTile = delegate(LibraryBrowserFolderView folder, int tileWidth, int tileHeight, bool showPlatformBadge)
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
                        msg => _shell.LibraryBrowserShowToast(ws, msg),
                        ws);
                };

                showFolder = delegate(LibraryBrowserFolderView info)
                {
                    _shell.LibraryBrowserShowSelectedFolder(ws, panes, libraryWindow, info, renderSelectedFolder);
                };

                Action renderFolderTilesCore = null;
                Action clearLibrarySearchAndRerender = delegate
                {
                    ws.PendingLibrarySearchText = string.Empty;
                    ws.AppliedLibrarySearchText = string.Empty;
                    if (panes.SearchBox != null) panes.SearchBox.Text = string.Empty;
                    _shell.PersistLibraryBrowserCommittedSearch(string.Empty);
                    if (renderFolderTilesCore != null) renderFolderTilesCore();
                };
                Action refreshLibraryFoldersLoose = delegate { refreshLibraryFoldersAsync(false); };
                renderFolderTilesCore = delegate
                {
                    _shell.LibraryBrowserRenderFolderList(ws, buildFolderTile, showFolder, renderSelectedFolder, renderFolderTilesCore, clearLibrarySearchAndRerender, refreshLibraryFoldersLoose);
                };
                renderTiles = renderFolderTilesCore;
                ws.RerenderFolderList = renderFolderTilesCore;
                ws.RefreshDetailPaneForPhotoFilters = renderSelectedFolder;

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
                    if (panes.OpenCapturesButton != null && isBusy) panes.OpenCapturesButton.IsEnabled = false;
                    if (panes.PhotoWorkspaceDividerToggleButton != null) panes.PhotoWorkspaceDividerToggleButton.IsEnabled = !isBusy;
                    panes.FolderCoverLayoutButton.IsEnabled = !isBusy;
                    if (panes.PhotoCaptureLayoutButton != null) panes.PhotoCaptureLayoutButton.IsEnabled = !isBusy;
                    if (panes.PhotoRailColumnOneButton != null) panes.PhotoRailColumnOneButton.IsEnabled = !isBusy;
                    if (panes.PhotoRailColumnTwoButton != null) panes.PhotoRailColumnTwoButton.IsEnabled = !isBusy;
                    panes.ShortcutsHelpButton.IsEnabled = !isBusy;
                    if (panes.CommandPaletteButton != null) panes.CommandPaletteButton.IsEnabled = !isBusy;
                    navChrome.ExportStarredButton.IsEnabled = !isBusy;
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
                _shell.ActiveLibraryFullCoverRefresh = runCoverRefresh;
                if (!reuseMainWindow)
                {
                    libraryWindow.Closed += delegate
                    {
                        if (_shell.ActiveLibraryFullCoverRefresh == runCoverRefresh) _shell.ActiveLibraryFullCoverRefresh = null;
                    };
                }
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
                _shell.LibraryBrowserWirePhotoWorkspaceHeroMenus(
                    ws,
                    panes,
                    libraryWindow,
                    showFolder,
                    renderTiles,
                    runScopedCoverRefresh,
                    msg => _shell.LibraryBrowserShowToast(ws, msg),
                    delegate
                    {
                        _shell.LibraryBrowserRefreshPhotoWorkspaceHeroBanner(ws, panes, libraryWindow, ws.Current);
                    });
                _shell.LibraryBrowserWireNavChromeAndToolbar(
                    libraryWindow,
                    ws,
                    panes,
                    navChrome,
                    refreshIntakeReviewBadge,
                    refreshLibraryFoldersAsync,
                    openSelectedLibraryMetadataEditor,
                    deleteSelectedLibraryFiles,
                    setLibraryGroupingMode,
                    setLibrarySortMode,
                    setLibraryFilterMode);
                panes.ShortcutsHelpButton.Click += delegate { _shell.ShowLibraryBrowserKeyboardShortcutsHelp(libraryWindow); };
                if (panes.PhotoWorkspaceDividerToggleButton != null)
                    panes.PhotoWorkspaceDividerToggleButton.Click += delegate { _shell.LibraryBrowserExitPhotoWorkspace(ws, renderTiles); };
                if (panes.OpenCapturesButton != null)
                    panes.OpenCapturesButton.Click += delegate { _shell.LibraryBrowserEnterPhotoWorkspaceFromSelection(ws, showFolder); };

                Action refreshAllCoversWithConfirm = delegate
                {
                    var choice = MessageBox.Show(
                        libraryWindow,
                        "Refresh cover art for the entire library?",
                        "PixelVault",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question);
                    if (choice != MessageBoxResult.OK) return;
                    runCoverRefresh();
                };
                var paletteCtx = new LibraryBrowserPaletteContext
                {
                    RefreshLibraryFolders = () => refreshLibraryFoldersAsync?.Invoke(false),
                    ClearLibrarySearch = clearLibrarySearchAndRerender,
                    OpenSettings = delegate
                    {
                        _shell.LibraryBrowserPaletteOpenSettings();
                        refreshIntakeReviewBadge?.Invoke();
                    },
                    OpenHealthDashboard = () => _shell.LibraryBrowserPaletteOpenHealthDashboard(libraryWindow),
                    OpenGameIndex = () => _shell.LibraryBrowserPaletteOpenGameIndex(),
                    OpenPhotoIndex = () => _shell.LibraryBrowserPaletteOpenPhotoIndex(),
                    OpenFilenameRules = () => _shell.LibraryBrowserPaletteOpenFilenameRules(),
                    OpenPhotographyGallery = () => _shell.LibraryBrowserPaletteOpenPhotographyGallery(libraryWindow),
                    OpenSavedCoversFolder = () => _shell.LibraryBrowserPaletteOpenSavedCoversFolder(),
                    RunImportQuick = delegate
                    {
                        _shell.LibraryBrowserPaletteRunImport(false);
                        refreshIntakeReviewBadge?.Invoke();
                    },
                    RunImportWithReview = delegate
                    {
                        _shell.LibraryBrowserPaletteRunImport(true);
                        refreshIntakeReviewBadge?.Invoke();
                    },
                    OpenManualIntake = delegate
                    {
                        _shell.LibraryBrowserPaletteOpenManualIntake();
                        refreshIntakeReviewBadge?.Invoke();
                    },
                    OpenIntakePreview = delegate
                    {
                        _shell.LibraryBrowserPaletteShowIntakePreview();
                        refreshIntakeReviewBadge?.Invoke();
                    },
                    ExportStarred = () => _shell.LibraryBrowserPaletteExportStarred(libraryWindow),
                    RefreshAllCovers = refreshAllCoversWithConfirm,
                    ShowKeyboardShortcuts = () => _shell.ShowLibraryBrowserKeyboardShortcutsHelp(libraryWindow),
                    SortFoldersAlpha = () => setLibrarySortMode("alpha"),
                    SortFoldersDateCaptured = () => setLibrarySortMode("captured"),
                    SortFoldersDateAdded = () => setLibrarySortMode("added"),
                    SortFoldersMostPhotos = () => setLibrarySortMode("photos"),
                    FilterFoldersAll = () => setLibraryFilterMode("all"),
                    FilterFolders100Percent = () => setLibraryFilterMode("completed"),
                    FilterFoldersCrossPlatform = () => setLibraryFilterMode("crossplatform"),
                    FilterFolders25PlusCaptures = () => setLibraryFilterMode("large"),
                    FilterFoldersMissingId = () => setLibraryFilterMode("missingid"),
                    FilterFoldersNoCover = () => setLibraryFilterMode("nocover"),
                    ToggleQuickEditDrawer = () => _shell.LibraryBrowserToggleQuickEditDrawer(ws),
                    GroupFoldersAllGames = () => setLibraryGroupingMode("all"),
                    GroupFoldersByConsole = () => setLibraryGroupingMode("console"),
                    GroupFoldersTimeline = () => setLibraryGroupingMode("timeline"),
                    GroupFoldersFolderGrid = () => setLibraryGroupingMode("folders"),
                    EnterPhotoWorkspace = () => _shell.LibraryBrowserEnterPhotoWorkspaceFromSelection(ws, showFolder),
                    ExitPhotoWorkspace = () => _shell.LibraryBrowserExitPhotoWorkspace(ws, renderTiles)
                };

                panes.CommandPaletteButton.Click += delegate { _shell.ShowLibraryCommandPalette(libraryWindow, paletteCtx, null); };

                libraryWindow.PreviewKeyDown += delegate(object _, KeyEventArgs e)
                {
                    if (e.Key == Key.Escape && ws.QuickEditDrawerOpen)
                    {
                        e.Handled = true;
                        _shell.LibraryBrowserSetQuickEditDrawerOpen(ws, false);
                        return;
                    }
                    if (e.Key == Key.Escape && ws.WorkspaceMode == LibraryWorkspaceMode.Photo)
                    {
                        e.Handled = true;
                        _shell.LibraryBrowserExitPhotoWorkspace(ws, renderTiles);
                        return;
                    }
                    var hideMiniChromeKeys = ws.WorkspaceMode == LibraryWorkspaceMode.Photo;
                    if (!hideMiniChromeKeys && e.Key == Key.F1)
                    {
                        e.Handled = true;
                        _shell.ShowLibraryBrowserKeyboardShortcutsHelp(libraryWindow);
                        return;
                    }
                    if (!hideMiniChromeKeys && e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                    {
                        e.Handled = true;
                        _shell.ShowLibraryCommandPalette(libraryWindow, paletteCtx, null);
                        return;
                    }
                    if (!hideMiniChromeKeys && e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                    {
                        e.Handled = true;
                        _shell.LibraryBrowserToggleQuickEditDrawer(ws);
                    }
                };
                var folderCoverLayoutMenu = new ContextMenu();
                panes.FolderCoverLayoutButton.Click += delegate
                {
                    if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo) return;
                    folderCoverLayoutMenu.Items.Clear();
                    void AddFolderCoverPreset(string label, int px)
                    {
                        var item = new MenuItem { Header = label };
                        item.Click += delegate
                        {
                            var norm = _shell.NormalizeLibraryFolderTileSizeValue(px);
                            _shell.LibraryFolderTileSize = norm;
                            _shell.SaveSettings();
                            if (renderTiles != null) renderTiles();
                            _shell.LibraryBrowserShowToast(ws, "Cover size: " + label + " (" + norm + " px)");
                        };
                        folderCoverLayoutMenu.Items.Add(item);
                    }
                    AddFolderCoverPreset("Compact", 52);
                    AddFolderCoverPreset("Medium", 220);
                    AddFolderCoverPreset("Comfortable", 380);
                    AddFolderCoverPreset("Roomy", 560);
                    AddFolderCoverPreset("Large (max)", 1000);
                    var folderColumnsMenu = new MenuItem { Header = "Columns" };
                    void AddFolderColumnChoice(string label, int cols)
                    {
                        var colItem = new MenuItem { Header = label };
                        colItem.Click += delegate
                        {
                            _shell.LibraryFolderGridColumnCount = _shell.NormalizeLibraryFolderGridColumnCountValue(cols);
                            _shell.SaveSettings();
                            if (renderTiles != null) renderTiles();
                            _shell.LibraryBrowserShowToast(ws, cols <= 0 ? "Cover columns: auto" : "Cover columns: " + cols);
                        };
                        folderColumnsMenu.Items.Add(colItem);
                    }
                    AddFolderColumnChoice("Auto", 0);
                    for (var colN = 1; colN <= 8; colN++)
                    {
                        var captured = colN;
                        AddFolderColumnChoice(captured.ToString(), captured);
                    }
                    folderCoverLayoutMenu.Items.Add(folderColumnsMenu);
                    folderCoverLayoutMenu.PlacementTarget = panes.FolderCoverLayoutButton;
                    folderCoverLayoutMenu.Placement = PlacementMode.Bottom;
                    folderCoverLayoutMenu.IsOpen = true;
                };
                if (panes.PhotoRailColumnOneButton != null)
                {
                    panes.PhotoRailColumnOneButton.Click += delegate
                    {
                        _shell.LibraryPhotoRailFolderGridColumnCount = 1;
                        _shell.SaveSettings();
                        if (renderTiles != null) renderTiles();
                        ws.RefreshPhotoRailColumnPickerUi?.Invoke();
                    };
                }
                if (panes.PhotoRailColumnTwoButton != null)
                {
                    panes.PhotoRailColumnTwoButton.Click += delegate
                    {
                        _shell.LibraryPhotoRailFolderGridColumnCount = 2;
                        _shell.SaveSettings();
                        if (renderTiles != null) renderTiles();
                        ws.RefreshPhotoRailColumnPickerUi?.Invoke();
                    };
                }
                if (panes.PhotoCaptureLayoutButton != null)
                {
                    var photoCaptureLayoutMenu = new ContextMenu();
                    void AddPhotoCapturePreset(string label, int px)
                    {
                        var item = new MenuItem { Header = label };
                        item.Click += delegate
                        {
                            _shell.LibraryPhotoTileSize = _shell.NormalizeLibraryPhotoTileSizeValue(px);
                            _shell.SaveSettings();
                            if (renderSelectedFolder != null) renderSelectedFolder();
                            _shell.LibraryBrowserShowToast(ws, "Photo size: " + label);
                        };
                        photoCaptureLayoutMenu.Items.Add(item);
                    }
                    AddPhotoCapturePreset("Compact", 220);
                    AddPhotoCapturePreset("Medium", 360);
                    AddPhotoCapturePreset("Comfortable", 460);
                    AddPhotoCapturePreset("Large (menu max)", SettingsService.LibraryPhotoTileMenuLargestPreset);
                    var photoColumnsMenu = new MenuItem { Header = "Columns" };
                    void AddPhotoColumnChoice(string label, int cols)
                    {
                        var colItem = new MenuItem { Header = label };
                        colItem.Click += delegate
                        {
                            _shell.LibraryPhotoGridColumnCount = _shell.NormalizeLibraryPhotoGridColumnCountValue(cols);
                            _shell.SaveSettings();
                            if (renderSelectedFolder != null) renderSelectedFolder();
                            _shell.LibraryBrowserShowToast(ws, cols <= 0 ? "Capture columns: auto" : "Capture columns: " + cols);
                        };
                        photoColumnsMenu.Items.Add(colItem);
                    }
                    AddPhotoColumnChoice("Auto", 0);
                    for (var pc = 1; pc <= 8; pc++)
                    {
                        var captured = pc;
                        AddPhotoColumnChoice(captured.ToString(), captured);
                    }
                    photoCaptureLayoutMenu.Items.Add(photoColumnsMenu);
                    panes.PhotoCaptureLayoutButton.Click += delegate
                    {
                        photoCaptureLayoutMenu.PlacementTarget = panes.PhotoCaptureLayoutButton;
                        photoCaptureLayoutMenu.Placement = PlacementMode.Bottom;
                        photoCaptureLayoutMenu.IsOpen = true;
                    };
                }
                panes.TimelinePresetTodayButton.Click += delegate { applyTimelinePreset("today"); };
                panes.TimelinePresetMonthButton.Click += delegate { applyTimelinePreset("month"); };
                panes.TimelinePresetThirtyDaysButton.Click += delegate { applyTimelinePreset("30d"); };
                panes.TimelineStartDatePicker.SelectedDateChanged += delegate { applyTimelineDatePickerRange(); };
                panes.TimelineEndDatePicker.SelectedDateChanged += delegate { applyTimelineDatePickerRange(); };
                panes.RefreshThisFolderButton.Click += delegate
                {
                    if (ws.Current == null) return;
                    var scopeFolders = _shell.GetLibraryBrowserActionFolders(ws.Current);
                    if (scopeFolders.Count == 0) return;
                    showFolder(ws.Current);
                    runScopedCoverRefresh(scopeFolders, _shell.BuildLibraryBrowserActionScopeLabel(ws.Current), true, false, false);
                };
                libraryWindow.Activated += delegate
                {
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };

                refreshSortButtons();
                refreshGroupingButtons();
                refreshTimelineRangeUi();
                if (reuseMainWindow) _shell.RegisterLibraryBrowserLiveWorkingSet(ws);
                if (!reuseMainWindow) libraryWindow.Show();
                if (renderTiles != null) renderTiles();
                if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                var autoRefreshLibraryFoldersOnStartup = !_shell.LibrarySession.HasLibraryFolderCacheSnapshot();
                if (!autoRefreshLibraryFoldersOnStartup && _shell.StatusLine != null) _shell.StatusLine.Text = "Loading cached library folders...";
                if (prefillLibraryFoldersFromSnapshotAsync != null) prefillLibraryFoldersFromSnapshotAsync();
                if (refreshLibraryFoldersAsync != null && autoRefreshLibraryFoldersOnStartup) refreshLibraryFoldersAsync(false);
                _shell.ScheduleDeferredGameIndexWarmup(libraryWindow);
            }
        }
    }
}
