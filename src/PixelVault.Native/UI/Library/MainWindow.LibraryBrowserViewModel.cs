using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        // PV-PLN-UI-001 Step 14: the library browser read-model is an owned non-partial class.
        // Projection/merge logic, the "All" cache, folder-view timeline assembly, troubleshooting
        // labels and the other ~30 instance helpers live in LibraryBrowserViewModel.cs and
        // collaborate with MainWindow only through the narrow ILibraryBrowserViewModelHost
        // surface. This partial keeps a single lazy instance and one-line instance forwarders
        // so the ~156 existing call sites across MainWindow.LibraryBrowser* partials resolve
        // unchanged. Tests can new up a LibraryBrowserViewModel against a stub host.
        //
        // Pass A (Step 13): LibraryBrowserFolderView promoted out of MainWindow. See
        // UI/Library/LibraryBrowserFolderView.cs.
        // Pass B (Step 13): pure-static math moved to LibraryBrowserViewModelMath.cs.
        // Pass C (Step 13): merged "All" cache moved to LibraryBrowserProjectionCache.cs.
        // Pass D (Step 14): the instance VM itself moved out; what remains here is forwarders.
        LibraryBrowserViewModel _libraryBrowserViewModel;

        LibraryBrowserViewModel LibraryBrowserVm
            => _libraryBrowserViewModel ?? (_libraryBrowserViewModel = new LibraryBrowserViewModel(new MainWindowLibraryBrowserVmHost(this)));

        /// <summary>
        /// Adapter that exposes MainWindow state + helper methods through the narrow host
        /// contract. Lives here (not a separate file) because it's a private implementation
        /// detail of how this MainWindow wires its read-model; tests should implement the
        /// interface directly against their own in-memory state.
        /// </summary>
        sealed class MainWindowLibraryBrowserVmHost : ILibraryBrowserViewModelHost
        {
            readonly MainWindow _owner;
            internal MainWindowLibraryBrowserVmHost(MainWindow owner) { _owner = owner; }

            public string LibraryRoot => _owner.libraryRoot;
            public string LibraryGroupingMode => _owner.libraryGroupingMode;

            public LibraryFolderInfo CloneLibraryFolderInfo(LibraryFolderInfo folder) => _owner.CloneLibraryFolderInfo(folder);
            public bool SameLibraryFolderSelection(LibraryFolderInfo left, LibraryFolderInfo right) => _owner.SameLibraryFolderSelection(left, right);
            public DateTime GetLibraryFolderNewestDate(LibraryFolderInfo folder) => _owner.GetLibraryFolderNewestDate(folder);

            public string NormalizeGameId(string value) => _owner.NormalizeGameId(value);
            public string NormalizeGameIndexName(string name, string folderPath = null) => _owner.NormalizeGameIndexName(name, folderPath);
            public string GuessGameIndexNameForFile(string file) => _owner.GuessGameIndexNameForFile(file);
            public string PrimaryPlatformLabel(string file) => _owner.PrimaryPlatformLabel(file);

            public DateTime ResolveIndexedLibraryDate(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null)
                => _owner.ResolveIndexedLibraryDate(libraryRoot, file, index);
            public LibraryMetadataIndexEntry TryGetLibraryMetadataIndexEntry(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index)
                => _owner.TryGetLibraryMetadataIndexEntry(libraryRoot, file, index);
            public long ResolveLibraryFileRecentSortUtcTicks(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null)
                => _owner.ResolveLibraryFileRecentSortUtcTicks(libraryRoot, file, index);

            public string FormatViewKeyForTroubleshooting(string viewKey) => _owner.FormatViewKeyForTroubleshooting(viewKey);
            public string FormatPathForTroubleshooting(string path) => _owner.FormatPathForTroubleshooting(path);
        }

        // ---- Instance forwarders (PV-PLN-UI-001 Step 14). Keep one-liners; don't grow. ----

        LibraryBrowserFolderView CloneLibraryBrowserFolderView(LibraryBrowserFolderView view)
            => LibraryBrowserVm.CloneLibraryBrowserFolderView(view);

        void PopulateLibraryBrowserFolderViewSearchBlob(LibraryBrowserFolderView view)
            => LibraryBrowserVm.PopulateLibraryBrowserFolderViewSearchBlob(view);

        LibraryFolderInfo GetLibraryBrowserPrimaryFolder(LibraryBrowserFolderView view)
            => LibraryBrowserVm.GetLibraryBrowserPrimaryFolder(view);

        LibraryFolderInfo BuildLibraryBrowserDisplayFolder(LibraryBrowserFolderView view)
            => LibraryBrowserVm.BuildLibraryBrowserDisplayFolder(view);

        bool SameLibraryBrowserSelection(LibraryBrowserFolderView left, LibraryBrowserFolderView right)
            => LibraryBrowserVm.SameLibraryBrowserSelection(left, right);

        string NormalizeLibraryGroupingMode(string value) => SettingsService.NormalizeLibraryGroupingMode(value);

        bool IsLibraryBrowserTimelineMode() => LibraryBrowserVm.IsLibraryBrowserTimelineMode();

        bool IsLibraryBrowserTimelineView(LibraryBrowserFolderView view) => LibraryBrowserVm.IsLibraryBrowserTimelineView(view);

        Dictionary<string, LibraryTimelineCaptureContext> BuildLibraryTimelineCaptureContextMap(
            IEnumerable<string> files,
            Dictionary<string, LibraryMetadataIndexEntry> metadataIndex,
            IEnumerable<GameIndexEditorRow> savedGameRows,
            Dictionary<string, EmbeddedMetadataSnapshot> metadataSnapshots = null)
            => LibraryBrowserVm.BuildLibraryTimelineCaptureContextMap(files, metadataIndex, savedGameRows, metadataSnapshots);

        string BuildLibraryBrowserViewKey(string groupingMode, string gameId, string name, string folderPath, string platformLabel)
            => LibraryBrowserVm.BuildLibraryBrowserViewKey(groupingMode, gameId, name, folderPath, platformLabel);

        string BuildLibraryBrowserPlatformSummary(IEnumerable<string> platformLabels)
            => LibraryBrowserVm.BuildLibraryBrowserPlatformSummary(platformLabels);

        string DetermineLibraryBrowserGroup(LibraryBrowserFolderView view)
            => LibraryBrowserVm.DetermineLibraryBrowserGroup(view);

        string BuildLibraryBrowserAllMergeKey(LibraryFolderInfo folder)
            => LibraryBrowserVm.BuildLibraryBrowserAllMergeKey(folder);

        int CountLibraryBrowserSourceFolders(LibraryBrowserFolderView view)
            => LibraryBrowserVm.CountLibraryBrowserSourceFolders(view);

        List<string> GetLibraryBrowserSourceFolderPaths(LibraryBrowserFolderView view)
            => LibraryBrowserVm.GetLibraryBrowserSourceFolderPaths(view);

        List<LibraryFolderInfo> GetLibraryBrowserActionFolders(LibraryBrowserFolderView view)
            => LibraryBrowserVm.GetLibraryBrowserActionFolders(view);

        bool ShouldShowLibraryBrowserPlatformContext()
            => LibraryBrowserVm.ShouldShowLibraryBrowserPlatformContext();

        string BuildLibraryBrowserFolderTileSubtitle(LibraryBrowserFolderView view)
            => LibraryBrowserVm.BuildLibraryBrowserFolderTileSubtitle(view);

        string BuildLibraryBrowserDetailMetaText(LibraryBrowserFolderView view, LibraryFolderInfo actionFolder)
            => LibraryBrowserVm.BuildLibraryBrowserDetailMetaText(view, actionFolder);

        string BuildLibraryBrowserScopeLabel(LibraryBrowserFolderView view)
            => LibraryBrowserVm.BuildLibraryBrowserScopeLabel(view);

        string BuildLibraryBrowserActionScopeLabel(LibraryBrowserFolderView view)
            => LibraryBrowserVm.BuildLibraryBrowserActionScopeLabel(view);

        string BuildLibraryBrowserOpenFoldersLabel(LibraryBrowserFolderView view)
            => LibraryBrowserVm.BuildLibraryBrowserOpenFoldersLabel(view);

        string BuildLibraryBrowserTroubleshootingLabel(LibraryBrowserFolderView view)
            => LibraryBrowserVm.BuildLibraryBrowserTroubleshootingLabel(view);

        LibraryBrowserFolderView BuildLibraryBrowserTimelineView(IEnumerable<LibraryBrowserFolderView> visibleFolders)
            => LibraryBrowserVm.BuildLibraryBrowserTimelineView(visibleFolders);

        void ApplyRemovedFilesToLibraryBrowserState(MainWindow.LibraryBrowserWorkingSet ws, IEnumerable<string> removedFiles)
            => LibraryBrowserVm.ApplyRemovedFilesToLibraryBrowserState(ws, removedFiles);

        LibraryBrowserFolderView FindMatchingLibraryBrowserView(LibraryBrowserFolderView current, IList<LibraryBrowserFolderView> candidates)
            => LibraryBrowserVm.FindMatchingLibraryBrowserView(current, candidates);

        LibraryBrowserFolderView FindLibraryBrowserViewByViewKey(IEnumerable<LibraryBrowserFolderView> candidates, string viewKey)
            => LibraryBrowserVm.FindLibraryBrowserViewByViewKey(candidates, viewKey);

        DateTime GetLibraryBrowserFolderViewSortNewest(LibraryBrowserFolderView view)
            => LibraryBrowserVm.GetLibraryBrowserFolderViewSortNewest(view);

        DateTime GetLibraryBrowserFolderViewSortRecentlyAdded(LibraryBrowserFolderView view)
            => LibraryBrowserVm.GetLibraryBrowserFolderViewSortRecentlyAdded(view);

        List<LibraryBrowserFolderView> GetOrBuildLibraryBrowserFolderViews(IReadOnlyList<LibraryFolderInfo> folders, string groupingMode)
            => LibraryBrowserVm.GetOrBuildLibraryBrowserFolderViews(folders, groupingMode);

        List<LibraryBrowserFolderView> BuildLibraryBrowserFolderViews(IEnumerable<LibraryFolderInfo> folders, string groupingMode)
            => LibraryBrowserVm.BuildLibraryBrowserFolderViews(folders, groupingMode);

        // ---- Static forwarders to LibraryBrowserViewModelMath (PV-PLN-UI-001 Step 13 Pass B). ----
        // These preserve the MainWindow.X surface that MainWindow.LibraryBrowserShowOrchestration,
        // LibraryTimelineModeTests, and LibraryBrowserCombinedMergeTests already reach for.
        internal static void NormalizeLibraryTimelineDateRange(ref DateTime startDate, ref DateTime endDate)
            => LibraryBrowserViewModelMath.NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);

        internal static void BuildLibraryTimelinePresetDateRange(string presetKey, DateTime referenceDate, out DateTime startDate, out DateTime endDate)
            => LibraryBrowserViewModelMath.BuildLibraryTimelinePresetDateRange(presetKey, referenceDate, out startDate, out endDate);

        internal static string DetectLibraryTimelinePresetKey(DateTime startDate, DateTime endDate, DateTime referenceDate)
            => LibraryBrowserViewModelMath.DetectLibraryTimelinePresetKey(startDate, endDate, referenceDate);

        internal static bool LibraryTimelineRangeContainsCapture(DateTime captureDate, DateTime startDate, DateTime endDate)
            => LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(captureDate, startDate, endDate);

        internal static bool TryAlignLibraryTimelineRollingPresetToToday(LibraryBrowserWorkingSet ws)
            => LibraryBrowserViewModelMath.TryAlignLibraryTimelineRollingPresetToToday(ws);

        internal static string BuildLibraryTimelineSummaryText(int captureCount, int gameCount, int platformCount, DateTime newestCapture, DateTime oldestCapture)
            => LibraryBrowserViewModelMath.BuildLibraryTimelineSummaryText(captureCount, gameCount, platformCount, newestCapture, oldestCapture);

        internal static string BuildLibraryTimelineCaptureTimeLabel(DateTime captureDate)
            => LibraryBrowserViewModelMath.BuildLibraryTimelineCaptureTimeLabel(captureDate);

        internal static string BuildLibraryTimelineDayCardTitle(DateTime captureDate, DateTime referenceDate)
            => LibraryBrowserViewModelMath.BuildLibraryTimelineDayCardTitle(captureDate, referenceDate);

        internal static int CalculateLibraryTimelinePackedTileSize(int detailTileSize, double availableWidth)
            => LibraryBrowserViewModelMath.CalculateLibraryTimelinePackedTileSize(detailTileSize, availableWidth);

        internal static int CalculateLibraryTimelinePackedCardColumns(int captureCount, double availableWidth)
            => LibraryBrowserViewModelMath.CalculateLibraryTimelinePackedCardColumns(captureCount, availableWidth);

        internal static double EstimateLibraryTimelinePackedCardWidth(int captureCount, int tileSize, double availableWidth)
            => LibraryBrowserViewModelMath.EstimateLibraryTimelinePackedCardWidth(captureCount, tileSize, availableWidth);

        internal static double EstimateLibraryTimelinePackedCardWidth(int captureCount, int tileSize, double availableWidth, int cardColumns)
            => LibraryBrowserViewModelMath.EstimateLibraryTimelinePackedCardWidth(captureCount, tileSize, availableWidth, cardColumns);

        internal static double EstimateLibraryTimelinePackedCardHeight(int captureCount, int tileSize, double availableWidth)
            => LibraryBrowserViewModelMath.EstimateLibraryTimelinePackedCardHeight(captureCount, tileSize, availableWidth);

        internal static double EstimateLibraryTimelinePackedCardHeight(int captureCount, int tileSize, int cardColumns)
            => LibraryBrowserViewModelMath.EstimateLibraryTimelinePackedCardHeight(captureCount, tileSize, cardColumns);

        internal static List<List<int>> BuildLibraryTimelinePackedRows(IReadOnlyList<double> cardWidths, double availableWidth, double interCardGap)
            => LibraryBrowserViewModelMath.BuildLibraryTimelinePackedRows(cardWidths, availableWidth, interCardGap);

        internal static double EstimateLibraryPackedDayCardDesiredWidth(int captureCount, double availableWidth, bool timelineView)
            => LibraryBrowserViewModelMath.EstimateLibraryPackedDayCardDesiredWidth(captureCount, availableWidth, timelineView);

        internal static double EstimateLibraryPackedDayCardDesiredWidth(int captureCount, double availableWidth, bool timelineView, int detailTileSize)
            => LibraryBrowserViewModelMath.EstimateLibraryPackedDayCardDesiredWidth(captureCount, availableWidth, timelineView, detailTileSize);

        internal static List<double> ExpandLibraryPackedRowWidths(IReadOnlyList<double> desiredWidths, double availableWidth, double interCardGap)
            => LibraryBrowserViewModelMath.ExpandLibraryPackedRowWidths(desiredWidths, availableWidth, interCardGap);

        internal static int LibraryDetailFileLayoutHash(string path)
            => LibraryBrowserViewModelMath.LibraryDetailFileLayoutHash(path);

        internal static int ResolveLibraryVariableDetailTileWidth(string file, int baseWidth, int minWidth, int maxWidth)
            => LibraryBrowserViewModelMath.ResolveLibraryVariableDetailTileWidth(file, baseWidth, minWidth, maxWidth);

        internal static List<List<(string File, int Width)>> PackLibraryDetailFilesIntoVariableRows(
            IReadOnlyList<string> files,
            double availableWidth,
            int gapPx,
            int baseWidth,
            int minWidth,
            int maxWidth)
            => LibraryBrowserViewModelMath.PackLibraryDetailFilesIntoVariableRows(files, availableWidth, gapPx, baseWidth, minWidth, maxWidth);

        internal static int EstimateLibraryVariableDetailRowHeight(IReadOnlyList<(string File, int Width)> row, bool includeTimelineFooter)
            => LibraryBrowserViewModelMath.EstimateLibraryVariableDetailRowHeight(row, includeTimelineFooter);

        internal static long ComputeLibraryBrowserFoldersMergeFingerprint(IReadOnlyList<LibraryFolderInfo> folders)
            => LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(folders);

        internal static string MergeLibraryBrowserExternalIdsForCombinedView(
            IReadOnlyList<LibraryFolderInfo> sourceFolders,
            Func<LibraryFolderInfo, string> pickId,
            Func<string, string> normalizeConsoleLabel)
            => LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(sourceFolders, pickId, normalizeConsoleLabel);

        internal static string MergeLibraryBrowserNonSteamIdForCombinedView(
            IReadOnlyList<LibraryFolderInfo> sourceFolders,
            Func<string, string> normalizeConsoleLabel)
            => LibraryBrowserViewModelMath.MergeLibraryBrowserNonSteamIdForCombinedView(sourceFolders, normalizeConsoleLabel);

        internal static string MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(IReadOnlyList<LibraryFolderInfo> sourceFolders)
            => LibraryBrowserViewModelMath.MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(sourceFolders);

        static string MergeLibraryBrowserCollectionNotesForCombinedView(IEnumerable<LibraryFolderInfo> folders)
            => LibraryBrowserViewModelMath.MergeLibraryBrowserCollectionNotesForCombinedView(folders);
    }
}
