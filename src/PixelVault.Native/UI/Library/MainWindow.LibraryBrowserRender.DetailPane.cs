using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserRenderSelectedFolderDetail(
            LibraryBrowserWorkingSet ws,
            Window libraryWindow,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail,
            Action renderFolderTiles)
        {
            var panes = ws.Panes;
            var renderStopwatch = Stopwatch.StartNew();
            var renderVersion = ++ws.DetailRenderSequence;
            if (ws.Current != null && panes?.ThumbScroll != null && !ws.PreserveDetailScrollOnNextRender)
            {
                var liveDetailScroll = panes.ThumbScroll.VerticalOffset;
                if (liveDetailScroll > 0.1d)
                {
                    ws.PreservedDetailScrollOffset = Math.Max(0d, liveDetailScroll);
                    ws.PreserveDetailScrollOnNextRender = true;
                }
            }
            else if (!ws.PreserveDetailScrollOnNextRender)
            {
                ws.PreservedDetailScrollOffset = 0;
            }
            ws.DetailTiles.Clear();
            if (ws.Current == null)
            {
                ws.SelectedDetailFiles.Clear();
                ws.DetailFilesDisplayOrder.Clear();
                SetVirtualizedRows(panes.DetailRows, new List<VirtualizedRowDefinition>
                {
                    new VirtualizedRowDefinition
                    {
                        Height = 200,
                        Build = delegate { return BuildLibraryDetailNoFolderSelectedPlaceholder(); }
                    }
                }, true, null);
                if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=(none); rows=1; files=0", 40);
                return;
            }
            var renderFolder = ws.Current;
            var timelineView = IsLibraryBrowserTimelineView(renderFolder);
            var detailLayout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll, true, timelineView);
            var size = detailLayout.TileSize;
            ws.LastDetailViewportWidth = ResolveScrollViewerLayoutWidth(panes == null ? null : panes.ThumbScroll);
            if (panes?.PhotoCaptureLayoutButton != null)
            {
                var densityLabel = DescribeResponsiveLibraryDetailDensity(size);
                panes.PhotoCaptureLayoutButton.Content = "Density: " + densityLabel;
                panes.PhotoCaptureLayoutButton.ToolTip = "Capture density follows the detail pane width automatically. Wide panes use Roomy; narrower panes use Compact.";
            }
            var shouldRestoreDetailScroll = ws.PreserveDetailScrollOnNextRender && ws.PreservedDetailScrollOffset > 0.1d;
            var restoreDetailScrollOffset = shouldRestoreDetailScroll ? (double?)ws.PreservedDetailScrollOffset : null;
            var restoreDetailScrollPending = shouldRestoreDetailScroll;
            ws.PreserveDetailScrollOnNextRender = false;
            var resetRowsToLoading = ws.ResetDetailRowsToLoadingOnNextRender;
            ws.ResetDetailRowsToLoadingOnNextRender = false;
            var detailViewportWidth = ws.LastDetailViewportWidth;
            var effectiveTileSize = timelineView
                ? CalculateLibraryTimelinePackedTileSize(size, detailViewportWidth)
                : size;
            var targetDetailColumns = Math.Max(1, detailLayout.Columns);
            ws.LastDetailColumns = targetDetailColumns;
            ws.LastDetailTileSize = effectiveTileSize;
            ws.EstimatedDetailRowHeight = EstimateLibraryVariableDetailRowHeight(
                new List<(string File, int Width)> { (string.Empty, effectiveTileSize) },
                timelineView);
            if (timelineView && TryAlignLibraryTimelineRollingPresetToToday(ws))
            {
                if (panes.TimelineStartDatePicker != null) panes.TimelineStartDatePicker.SelectedDate = ws.TimelineStartDate;
                if (panes.TimelineEndDatePicker != null) panes.TimelineEndDatePicker.SelectedDate = ws.TimelineEndDate;
            }
            var timelineRangeStart = ws.TimelineStartDate;
            var timelineRangeEnd = ws.TimelineEndDate;
            NormalizeLibraryTimelineDateRange(ref timelineRangeStart, ref timelineRangeEnd);
            LogTroubleshooting("LibraryDetailRenderStart",
                "renderVersion=" + renderVersion
                + "; resetToLoading=" + resetRowsToLoading
                + "; restoreScroll=" + shouldRestoreDetailScroll
                + "; detailColumns=" + targetDetailColumns
                + "; detailSize=" + effectiveTileSize
                + (timelineView ? "; timelineRange=" + timelineRangeStart.ToString("yyyy-MM-dd") + ".." + timelineRangeEnd.ToString("yyyy-MM-dd") : string.Empty)
                + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
            var displayFolder = BuildLibraryBrowserDisplayFolder(renderFolder);
            if (resetRowsToLoading || panes.DetailRows.Rows == null || panes.DetailRows.Rows.Count == 0)
            {
                SetVirtualizedRows(panes.DetailRows, new[]
                {
                    new VirtualizedRowDefinition
                    {
                        Height = 120,
                        Build = delegate
                        {
                            return BuildLibraryDetailLoadingPlaceholder();
                        }
                    }
                }, true, null);
                LogTroubleshooting("LibraryDetailRenderLoadingState",
                    "renderVersion=" + renderVersion
                    + "; reason=" + (resetRowsToLoading ? "selection-change" : "empty-pane"));
            }
            if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
            const int LibraryDetailMetadataRepairMaxFilesPerPass = 140;
            var detailDpiScaleForBackground = ResolveLibraryDpiScale(panes?.ThumbScroll);
            Task.Run(async delegate
            {
                LibraryDetailQuickSnapshotPerf? quickSnapshotPerf = null;
                Action<string, string> traceStep = delegate(string area, string details)
                {
                    LogTroubleshooting(area,
                        "renderVersion=" + renderVersion
                        + "; elapsedMs=" + renderStopwatch.ElapsedMilliseconds
                        + "; " + (details ?? string.Empty)
                        + (string.IsNullOrWhiteSpace(details) ? string.Empty : "; ")
                        + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                };
                Func<LibraryDetailRenderSnapshot, List<VirtualizedRowDefinition>> buildVirtualRowsForSnapshot = delegate(LibraryDetailRenderSnapshot snapshot)
                {
                    if (snapshot == null || snapshot.Groups == null || snapshot.Groups.Count == 0) return null;
                    var timelineCtx = snapshot.TimelineContextByFile ?? new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase);
                    var mediaMap = snapshot.MediaLayoutByFile ?? new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase);
                    return BuildLibraryContinuousMosaicRowDefinitions(
                        ws,
                        renderFolder,
                        snapshot.Groups,
                        timelineCtx,
                        mediaMap,
                        panes == null ? null : panes.ThumbScroll,
                        detailViewportWidth,
                        effectiveTileSize,
                        detailDpiScaleForBackground,
                        timelineView,
                        openSingleFileMetadataEditor,
                        updateDetailSelection,
                        refreshDetailSelectionUi,
                        redrawSelectedFolderDetail,
                        renderFolderTiles);
                };

                Action<LibraryDetailRenderSnapshot, bool, List<VirtualizedRowDefinition>> applyDetailSnapshot = null;
                applyDetailSnapshot = delegate(LibraryDetailRenderSnapshot snapshot, bool logCompletion, List<VirtualizedRowDefinition> prebuiltVirtualRows)
                {
                    Stopwatch uiApplySw = null;
                    if (logCompletion) uiApplySw = Stopwatch.StartNew();
                    Action<IEnumerable<VirtualizedRowDefinition>> commitDetailVirtualRows = delegate(IEnumerable<VirtualizedRowDefinition> rowEnum)
                    {
                        double? scrollRestore = restoreDetailScrollPending ? restoreDetailScrollOffset : null;
                        if (!restoreDetailScrollPending && panes != null && panes.ThumbScroll != null)
                        {
                            var live = panes.ThumbScroll.VerticalOffset;
                            if (live > 0.1d) scrollRestore = live;
                        }
                        var resetDetailScroll = !scrollRestore.HasValue;
                        SetVirtualizedRows(panes.DetailRows, rowEnum, resetDetailScroll, scrollRestore);
                        restoreDetailScrollPending = false;
                    };
                    var snapshotStage = logCompletion ? "initial" : "refined";
                    if (renderVersion != ws.DetailRenderSequence)
                    {
                        LogTroubleshooting("LibraryDetailRenderSkipped",
                            "renderVersion=" + renderVersion
                            + "; activeVersion=" + ws.DetailRenderSequence
                            + "; stage=" + snapshotStage
                            + "; reason=stale-render");
                        return;
                    }
                    if (!SameLibraryBrowserSelection(ws.Current, renderFolder))
                    {
                        LogTroubleshooting("LibraryDetailRenderSkipped",
                            "renderVersion=" + renderVersion
                            + "; stage=" + snapshotStage
                            + "; reason=selection-changed"
                            + "; active=" + BuildLibraryBrowserTroubleshootingLabel(ws.Current)
                            + "; expected=" + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        return;
                    }
                    var visibleFiles = snapshot == null ? new List<string>() : (snapshot.VisibleFiles ?? new List<string>());
                    var timelineContexts = snapshot == null
                        ? new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase)
                        : (snapshot.TimelineContextByFile ?? new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase));
                    var mediaLayoutByFile = snapshot == null
                        ? new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase)
                        : (snapshot.MediaLayoutByFile ?? new Dictionary<string, LibraryDetailMediaLayoutInfo>(StringComparer.OrdinalIgnoreCase));
                    var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var stale in ws.SelectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) ws.SelectedDetailFiles.Remove(stale);
                    if (SameLibraryBrowserSelection(ws.Current, renderFolder))
                    {
                        ws.DetailFilesDisplayOrder.Clear();
                        ws.DetailFilesDisplayOrder.AddRange(visibleFiles);
                    }
                    if (timelineView)
                    {
                        var distinctGames = timelineContexts.Values
                            .Select(context => NormalizeGameIndexName(context == null ? string.Empty : context.GameTitle))
                            .Where(title => !string.IsNullOrWhiteSpace(title))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();
                        var distinctPlatforms = timelineContexts.Values
                            .Select(context => NormalizeConsoleLabel(context == null ? string.Empty : context.PlatformLabel))
                            .Where(label => !string.IsNullOrWhiteSpace(label) && !string.Equals(label, "Other", StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();
                        var captureDates = timelineContexts.Values
                            .Select(context => context == null ? DateTime.MinValue : context.CaptureDate)
                            .Where(date => date > DateTime.MinValue)
                            .ToList();
                        var newestCapture = captureDates.Count == 0 ? DateTime.MinValue : captureDates.Max();
                        var oldestCapture = captureDates.Count == 0 ? DateTime.MinValue : captureDates.Min();
                        panes.DetailMeta.Text = BuildLibraryTimelineSummaryText(visibleFiles.Count, distinctGames, distinctPlatforms, newestCapture, oldestCapture);
                    }
                    ws.DetailTiles.Clear();
                    if (snapshot == null || snapshot.Groups == null || snapshot.Groups.Count == 0)
                    {
                        ws.DetailFilesDisplayOrder.Clear();
                        commitDetailVirtualRows(new[]
                        {
                            new VirtualizedRowDefinition
                            {
                                Height = 200,
                                Build = delegate
                                {
                                    return BuildLibraryDetailEmptyCapturesPlaceholder(
                                        timelineView,
                                        timelineRangeStart,
                                        timelineRangeEnd,
                                        redrawSelectedFolderDetail);
                                }
                            }
                        });
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        LogTroubleshooting("LibraryDetailRenderApplied",
                            "renderVersion=" + renderVersion
                            + "; stage=" + snapshotStage
                            + "; groups=0; files=0; rows=1; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        if (logCompletion)
                        {
                            renderStopwatch.Stop();
                            LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; rows=1; files=0; size=" + effectiveTileSize, 40);
                            LogLibraryBrowserFirstDetailPaintOnce("folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; files=0");
                        }
                        return;
                    }

                    var detailColumns = targetDetailColumns;
                    var virtualRows = prebuiltVirtualRows ?? buildVirtualRowsForSnapshot(snapshot);
                    if (virtualRows == null) virtualRows = new List<VirtualizedRowDefinition>();
                    commitDetailVirtualRows(virtualRows);
                    if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                    LogTroubleshooting("LibraryDetailRenderApplied",
                        "renderVersion=" + renderVersion
                        + "; stage=" + snapshotStage
                        + "; groups=" + snapshot.Groups.Count
                        + "; files=" + visibleFiles.Count
                        + "; rows=" + virtualRows.Count
                        + "; columns=" + detailColumns
                        + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                    if (logCompletion)
                    {
                        var uiApplyMs = uiApplySw == null ? 0L : uiApplySw.ElapsedMilliseconds;
                        renderStopwatch.Stop();
                        var quickPerfSeg = string.Empty;
                        if (quickSnapshotPerf.HasValue)
                        {
                            var q = quickSnapshotPerf.Value;
                            quickPerfSeg = "; quickPrepMs=" + q.LayoutPrepMs
                                + "; quickMediaMapMs=" + q.MediaLayoutMs
                                + "; quickTailMs=" + q.TimelineAndGroupsMs
                                + "; quickMediaReused=" + q.MediaLayoutReused;
                        }

                        LogPerformanceSample("LibraryDetailRender", renderStopwatch,
                            "folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)")
                            + "; groups=" + snapshot.Groups.Count
                            + "; files=" + visibleFiles.Count
                            + "; rows=" + virtualRows.Count
                            + "; columns=" + detailColumns
                            + "; size=" + effectiveTileSize
                            + "; uiApplyMs=" + uiApplyMs
                            + quickPerfSeg,
                            40);
                        LogLibraryBrowserFirstDetailPaintOnce("folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; files=" + visibleFiles.Count + "; groups=" + snapshot.Groups.Count);
                    }
                };

                try
                {
                    traceStep("LibraryDetailBackgroundStart", "thread=" + Environment.CurrentManagedThreadId);
                    List<GameIndexEditorRow> savedGameRows = null;
                    if (timelineView)
                    {
                        savedGameRows = librarySession.LoadSavedGameIndexRows();
                        traceStep("LibraryDetailTimelineGameRowsLoaded", "savedGameRows=" + savedGameRows.Count);
                    }
                    var detailFiles = GetFilesForLibraryFolderEntry(displayFolder, false)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    traceStep("LibraryDetailFilesEnumerated", "files=" + detailFiles.Count);
                    var metadataIndex = librarySession.LoadLibraryMetadataIndexForFilePaths(detailFiles);
                    traceStep("LibraryDetailMetadataIndexLoaded", "entries=" + metadataIndex.Count + "; scope=detailFiles");
                    var filesMissingCaptureTicks = new List<string>();
                    if (librarySession.HasLibraryRoot && detailFiles.Count > 0)
                    {
                        filesMissingCaptureTicks = detailFiles
                            .Where(file =>
                            {
                                LibraryMetadataIndexEntry entry;
                                if (!metadataIndex.TryGetValue(file, out entry) || entry == null || entry.CaptureUtcTicks <= 0) return true;
                                return !string.Equals(entry.Stamp ?? string.Empty, BuildLibraryMetadataStamp(file), StringComparison.Ordinal);
                            })
                            .ToList();
                    }
                    traceStep("LibraryDetailFilesClassified",
                        "files=" + detailFiles.Count
                        + "; missingCaptureTicks=" + filesMissingCaptureTicks.Count
                        + "; hasLibraryRoot=" + librarySession.HasLibraryRoot);

                    bool PhotoWorkspaceShouldHideCapture(string path)
                    {
                        if (timelineView) return false;
                        if (ws.WorkspaceMode != LibraryWorkspaceMode.Photo) return false;
                        if (ws.PhotoRailExcludedConsoleLabels == null || ws.PhotoRailExcludedConsoleLabels.Count == 0) return false;
                        var plat = NormalizeConsoleLabel(DetermineFolderPlatform(new List<string> { path }, metadataIndex, null));
                        return ws.PhotoRailExcludedConsoleLabels.Contains(plat);
                    }

                    Func<Dictionary<string, EmbeddedMetadataSnapshot>, LibraryDetailRenderSnapshot, LibraryDetailRenderSnapshot> buildSnapshot = delegate(Dictionary<string, EmbeddedMetadataSnapshot> timelineMetadataSnapshots, LibraryDetailRenderSnapshot reuseMediaFrom)
                    {
                        var segmentSw = Stopwatch.StartNew();
                        var datedFiles = detailFiles
                            .Select(file =>
                            {
                                var captureDate = librarySession.ResolveIndexedLibraryDate(file, metadataIndex);
                                // Timeline merges many folders; index/mtime-only dates often collapse into one day until
                                // embedded EXIF is read. Prefer CaptureTime from the batch read when present so day groups
                                // (and per-day badges) match real capture dates.
                                if (timelineView && timelineMetadataSnapshots != null)
                                {
                                    EmbeddedMetadataSnapshot embedded;
                                    if (timelineMetadataSnapshots.TryGetValue(file, out embedded)
                                        && embedded != null
                                        && embedded.CaptureTime.HasValue)
                                    {
                                        captureDate = embedded.CaptureTime.Value;
                                    }
                                }
                                return new { FilePath = file, CaptureDate = captureDate };
                            })
                            .Where(entry => !timelineView || LibraryTimelineRangeContainsCapture(entry.CaptureDate, timelineRangeStart, timelineRangeEnd))
                            .Where(entry => !PhotoWorkspaceShouldHideCapture(entry.FilePath))
                            .OrderByDescending(entry => entry.CaptureDate)
                            .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var visiblePaths = datedFiles.Select(entry => entry.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var layoutPrepMs = segmentSw.ElapsedMilliseconds;
                        segmentSw.Restart();
                        var snapshot = new LibraryDetailRenderSnapshot { VisibleFiles = visiblePaths };
                        bool mediaReused;
                        if (reuseMediaFrom != null
                            && reuseMediaFrom.MediaLayoutByFile != null
                            && reuseMediaFrom.VisibleFiles != null
                            && visiblePaths.Count == reuseMediaFrom.VisibleFiles.Count)
                        {
                            var visSet = new HashSet<string>(visiblePaths, StringComparer.OrdinalIgnoreCase);
                            if (visSet.Count == visiblePaths.Count && visSet.SetEquals(reuseMediaFrom.VisibleFiles))
                            {
                                snapshot.MediaLayoutByFile = new Dictionary<string, LibraryDetailMediaLayoutInfo>(reuseMediaFrom.MediaLayoutByFile, StringComparer.OrdinalIgnoreCase);
                                mediaReused = true;
                            }
                            else
                            {
                                snapshot.MediaLayoutByFile = BuildLibraryDetailMediaLayoutInfoMap(visiblePaths);
                                mediaReused = false;
                            }
                        }
                        else
                        {
                            snapshot.MediaLayoutByFile = BuildLibraryDetailMediaLayoutInfoMap(visiblePaths);
                            mediaReused = false;
                        }

                        var mediaLayoutMs = segmentSw.ElapsedMilliseconds;
                        segmentSw.Restart();
                        snapshot.TimelineContextByFile = BuildLibraryTimelineCaptureContextMap(snapshot.VisibleFiles, metadataIndex, savedGameRows, timelineMetadataSnapshots);
                        foreach (var group in datedFiles
                            .GroupBy(entry => entry.CaptureDate.Date)
                            .OrderByDescending(group => group.Key))
                        {
                            // Per calendar day: newest capture first so index 0 is always the chronologically
                            // last shot that day (the day-badge anchor), regardless of GroupBy iteration quirks.
                            var dayFilesOrdered = group
                                .OrderByDescending(entry => entry.CaptureDate)
                                .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                                .Select(entry => entry.FilePath)
                                .ToList();
                            snapshot.Groups.Add(new LibraryDetailRenderGroup
                            {
                                CaptureDate = group.Key,
                                Files = dayFilesOrdered
                            });
                        }

                        var tailMs = segmentSw.ElapsedMilliseconds;
                        if (timelineMetadataSnapshots == null && reuseMediaFrom == null)
                        {
                            quickSnapshotPerf = new LibraryDetailQuickSnapshotPerf(layoutPrepMs, mediaLayoutMs, tailMs, mediaReused);
                        }

                        return snapshot;
                    };

                    traceStep("LibraryDetailQuickSnapshotBuildStart", "files=" + detailFiles.Count);
                    var quickSnapshot = buildSnapshot(null, null);
                    traceStep("LibraryDetailQuickSnapshotBuilt",
                        "groups=" + quickSnapshot.Groups.Count
                        + "; visibleFiles=" + quickSnapshot.VisibleFiles.Count);
                    traceStep("LibraryDetailQuickSnapshotDispatchStart", "stage=initial");
                    var dispatcherWallSw = Stopwatch.StartNew();
                    var quickVirtualRows = buildVirtualRowsForSnapshot(quickSnapshot);
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(quickSnapshot, true, quickVirtualRows); }));
                    traceStep("LibraryDetailQuickSnapshotDispatchComplete", "stage=initial; dispatcherWallMs=" + dispatcherWallSw.ElapsedMilliseconds);
                    Dictionary<string, EmbeddedMetadataSnapshot> timelineMetadataSnapshots = null;
                    if (quickSnapshot.VisibleFiles.Count > 0)
                    {
                        try
                        {
                            traceStep("LibraryDetailMetadataReadStart", "files=" + quickSnapshot.VisibleFiles.Count);
                            timelineMetadataSnapshots = await metadataService.ReadEmbeddedMetadataBatchAsync(quickSnapshot.VisibleFiles, CancellationToken.None).ConfigureAwait(false);
                            traceStep("LibraryDetailMetadataReadComplete", "metadataResults=" + timelineMetadataSnapshots.Count);
                            var commentSnapshot = buildSnapshot(timelineMetadataSnapshots, quickSnapshot);
                            var commentsChanged = commentSnapshot.TimelineContextByFile.Count != quickSnapshot.TimelineContextByFile.Count;
                            if (!commentsChanged)
                            {
                                foreach (var pair in commentSnapshot.TimelineContextByFile)
                                {
                                    LibraryTimelineCaptureContext quickContext;
                                    if (!quickSnapshot.TimelineContextByFile.TryGetValue(pair.Key, out quickContext))
                                    {
                                        commentsChanged = true;
                                        break;
                                    }
                                    var nextComment = pair.Value == null ? string.Empty : pair.Value.Comment ?? string.Empty;
                                    var quickComment = quickContext == null ? string.Empty : quickContext.Comment ?? string.Empty;
                                    if (!string.Equals(nextComment, quickComment, StringComparison.Ordinal))
                                    {
                                        commentsChanged = true;
                                        break;
                                    }
                                }
                            }
                            var dayGroupingChanged = LibraryTimelineDetailGroupingFingerprint(quickSnapshot.Groups)
                                != LibraryTimelineDetailGroupingFingerprint(commentSnapshot.Groups);
                            if (commentsChanged || dayGroupingChanged)
                            {
                                traceStep("LibraryDetailMetadataDispatchStart",
                                    "stage=metadata-refresh; commentsChanged=" + commentsChanged + "; dayGroupingChanged=" + dayGroupingChanged);
                                var commentVirtualRows = buildVirtualRowsForSnapshot(commentSnapshot);
                                await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(commentSnapshot, false, commentVirtualRows); }));
                                traceStep("LibraryDetailMetadataDispatchComplete", "stage=metadata-refresh");
                                quickSnapshot = commentSnapshot;
                            }
                        }
                        catch (Exception timelineMetadataEx)
                        {
                            LogException("Library detail metadata read | " + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)"), timelineMetadataEx);
                            LogTroubleshooting("LibraryDetailMetadataReadFail",
                                "renderVersion=" + renderVersion
                                + "; type=" + timelineMetadataEx.GetType().FullName
                                + "; message=" + timelineMetadataEx.Message
                                + "; exception=" + FormatExceptionForTroubleshooting(timelineMetadataEx)
                                + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        }
                    }

                    if (filesMissingCaptureTicks.Count > 0)
                    {
                        var repairTargets = filesMissingCaptureTicks.Count <= LibraryDetailMetadataRepairMaxFilesPerPass
                            ? filesMissingCaptureTicks
                            : filesMissingCaptureTicks.Take(LibraryDetailMetadataRepairMaxFilesPerPass).ToList();
                        List<string> deferredMetadataRepairFiles = null;
                        if (repairTargets.Count < filesMissingCaptureTicks.Count)
                        {
                            deferredMetadataRepairFiles = filesMissingCaptureTicks.Skip(repairTargets.Count).ToList();
                            LogTroubleshooting("LibraryDetailMetadataRepairCapped",
                                "renderVersion=" + renderVersion
                                + "; repairNow=" + repairTargets.Count
                                + "; repairDeferred=" + deferredMetadataRepairFiles.Count
                                + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        }
                        LogTroubleshooting("LibraryDetailMetadataRepairStart",
                            "renderVersion=" + renderVersion
                            + "; files=" + repairTargets.Count
                            + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        try
                        {
                            if (savedGameRows == null) savedGameRows = librarySession.LoadSavedGameIndexRows();
                            traceStep("LibraryDetailMetadataRepairRowsLoaded", "savedGameRows=" + savedGameRows.Count);
                            var metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(repairTargets, CancellationToken.None).ConfigureAwait(false);
                            traceStep("LibraryDetailMetadataRepairBatchRead", "metadataResults=" + metadataByFile.Count);
                            var indexChanged = false;
                            var gameRowsChanged = false;
                            foreach (var file in repairTargets)
                            {
                                EmbeddedMetadataSnapshot metadataSnapshot;
                                if (!metadataByFile.TryGetValue(file, out metadataSnapshot) || metadataSnapshot == null) metadataSnapshot = new EmbeddedMetadataSnapshot();
                                LibraryMetadataIndexEntry existingEntry;
                                if (!metadataIndex.TryGetValue(file, out existingEntry)) existingEntry = null;
                                var stamp = BuildLibraryMetadataStamp(file);
                                var previousGameId = existingEntry == null ? string.Empty : NormalizeGameId(existingEntry.GameId);
                                var previousConsole = existingEntry == null ? string.Empty : NormalizeConsoleLabel(existingEntry.ConsoleLabel);
                                var rebuiltEntry = librarySession.BuildResolvedLibraryMetadataIndexEntry(file, stamp, metadataSnapshot, existingEntry, metadataIndex, savedGameRows);
                                metadataIndex[file] = rebuiltEntry;
                                SetCachedFileTags(file, ParseTagText(rebuiltEntry.TagText), MetadataCacheStamp(file));
                                indexChanged = true;
                                if (!string.Equals(previousGameId, NormalizeGameId(rebuiltEntry.GameId), StringComparison.OrdinalIgnoreCase)
                                    || !string.Equals(previousConsole, NormalizeConsoleLabel(rebuiltEntry.ConsoleLabel), StringComparison.OrdinalIgnoreCase))
                                {
                                    gameRowsChanged = true;
                                }
                            }
                            if (gameRowsChanged) librarySession.PersistGameIndexRows(savedGameRows);
                            if (indexChanged)
                            {
                                var repaired = new List<LibraryMetadataIndexEntry>();
                                foreach (var file in repairTargets)
                                {
                                    LibraryMetadataIndexEntry e;
                                    if (metadataIndex.TryGetValue(file, out e) && e != null) repaired.Add(e);
                                }

                                librarySession.MergePersistLibraryMetadataIndexEntries(repaired);
                            }
                            LogTroubleshooting("LibraryDetailMetadataRepairComplete",
                                "renderVersion=" + renderVersion
                                + "; files=" + repairTargets.Count
                                + "; indexChanged=" + indexChanged
                                + "; gameRowsChanged=" + gameRowsChanged);
                        }
                        catch (Exception repairEx)
                        {
                            LogException("Library detail metadata repair | " + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)"), repairEx);
                            LogTroubleshooting("LibraryDetailMetadataRepairFail",
                                "renderVersion=" + renderVersion
                                + "; type=" + repairEx.GetType().FullName
                                + "; message=" + repairEx.Message
                                + "; exception=" + FormatExceptionForTroubleshooting(repairEx)
                                + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        }

                        traceStep("LibraryDetailRefinedSnapshotBuildStart", "files=" + detailFiles.Count);
                        var refinedSnapshot = buildSnapshot(timelineMetadataSnapshots, quickSnapshot);
                        traceStep("LibraryDetailRefinedSnapshotBuilt",
                            "groups=" + refinedSnapshot.Groups.Count
                            + "; visibleFiles=" + refinedSnapshot.VisibleFiles.Count);
                        var layoutUnchanged = quickSnapshot.Groups.Count == refinedSnapshot.Groups.Count;
                        if (layoutUnchanged)
                        {
                            for (int gi = 0; gi < quickSnapshot.Groups.Count && layoutUnchanged; gi++)
                            {
                                var ag = quickSnapshot.Groups[gi];
                                var bg = refinedSnapshot.Groups[gi];
                                if (ag.CaptureDate.Date != bg.CaptureDate.Date) layoutUnchanged = false;
                                else
                                {
                                    var af = ag.Files ?? new List<string>();
                                    var bf = bg.Files ?? new List<string>();
                                    if (af.Count != bf.Count) layoutUnchanged = false;
                                    else
                                    {
                                        for (int j = 0; j < af.Count && layoutUnchanged; j++)
                                        {
                                            if (!string.Equals(af[j], bf[j], StringComparison.OrdinalIgnoreCase)) layoutUnchanged = false;
                                        }
                                    }
                                }
                            }
                        }
                        LogTroubleshooting("LibraryDetailMetadataRepairDiff",
                            "renderVersion=" + renderVersion
                            + "; layoutUnchanged=" + layoutUnchanged
                            + "; quickGroups=" + quickSnapshot.Groups.Count
                            + "; refinedGroups=" + refinedSnapshot.Groups.Count);
                        if (!layoutUnchanged)
                        {
                            traceStep("LibraryDetailRefinedSnapshotDispatchStart", "stage=refined");
                            var refinedVirtualRows = buildVirtualRowsForSnapshot(refinedSnapshot);
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(refinedSnapshot, false, refinedVirtualRows); }));
                            traceStep("LibraryDetailRefinedSnapshotDispatchComplete", "stage=refined");
                        }

                        if (deferredMetadataRepairFiles != null && deferredMetadataRepairFiles.Count > 0)
                        {
                            ScheduleDeferredLibraryDetailMetadataRepair(
                                deferredMetadataRepairFiles,
                                ws,
                                renderFolder,
                                libraryWindow,
                                redrawSelectedFolderDetail);
                        }
                    }
                    traceStep("LibraryDetailBackgroundComplete", "done=true");
                }
                catch (Exception ex)
                {
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                    {
                        if (renderVersion != ws.DetailRenderSequence) return;
                        if (!SameLibraryBrowserSelection(ws.Current, renderFolder)) return;
                        ws.DetailFilesDisplayOrder.Clear();
                        SetVirtualizedRows(panes.DetailRows, new[]
                        {
                            new VirtualizedRowDefinition
                            {
                                Height = 44,
                                Build = delegate
                                {
                                    return new TextBlock { Text = "Failed to load captures.", Foreground = Brush("#D9A3A3") };
                                }
                            }
                        }, true, null);
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        LogException("Library detail render | " + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)"), ex);
                        LogTroubleshooting("LibraryDetailRenderFail",
                            "renderVersion=" + renderVersion
                            + "; type=" + ex.GetType().FullName
                            + "; message=" + ex.Message
                            + "; exception=" + FormatExceptionForTroubleshooting(ex)
                            + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        renderStopwatch.Stop();
                    }));
                }
            });
        }

        List<VirtualizedRowDefinition> BuildLibraryContinuousMosaicRowDefinitions(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            IList<LibraryDetailRenderGroup> groups,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile,
            ScrollViewer detailScroll,
            double viewportWidth,
            int detailTileSize,
            double dpiScale,
            bool timelineView,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail,
            Action renderFolderTiles)
        {
            var safeGroups = (groups ?? new List<LibraryDetailRenderGroup>())
                .Where(group => group != null && (group.Files ?? new List<string>()).Count > 0)
                .ToList();
            if (safeGroups.Count == 0) return new List<VirtualizedRowDefinition>();

            const int masonryTileGap = 4;
            const int rowGap = 8;
            var availableWidth = viewportWidth <= 0d ? 1100d : Math.Max(320d, viewportWidth - 6d);
            var rowDefinitions = new List<VirtualizedRowDefinition>();
            var nextRowDocumentTop = 0d;
            var orderedFiles = new List<string>();
            foreach (var group in safeGroups)
            {
                var groupFiles = (group.Files ?? new List<string>())
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .ToList();
                if (groupFiles.Count == 0) continue;
                orderedFiles.AddRange(groupFiles);
            }

            if (orderedFiles.Count == 0) return rowDefinitions;

            var requestedTileSize = Math.Max(160, detailTileSize);
            var minTileWidth = Math.Max(120, (int)Math.Round(requestedTileSize * 0.72d));
            var maxTileWidth = Math.Max(minTileWidth, (int)Math.Round(requestedTileSize * 1.35d));
            var chunks = BuildLibraryDetailMasonryChunks(
                orderedFiles,
                availableWidth,
                masonryTileGap,
                requestedTileSize,
                minTileWidth,
                maxTileWidth,
                timelineView,
                mediaLayoutByFile);
            var captureDateLabels = BuildLibraryCaptureDateLabelMapForPlacements(safeGroups, chunks, DateTime.Today);

            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                var chunk = chunks[chunkIndex];
                if (chunk == null) continue;
                var bottomMargin = chunkIndex == chunks.Count - 1 ? 0d : rowGap;
                var capturedChunk = chunk;
                var capturedDocTop = nextRowDocumentTop;
                var rowVirtualHeight = (int)Math.Ceiling(capturedChunk.CanvasHeight + bottomMargin);
                nextRowDocumentTop += rowVirtualHeight;
                rowDefinitions.Add(new VirtualizedRowDefinition
                {
                    Height = rowVirtualHeight,
                    Build = delegate
                    {
                        var prioritizeDecodes = LibraryDetailTileRowIntersectsViewport(detailScroll, capturedDocTop, rowVirtualHeight);
                        var canvas = new Canvas
                        {
                            Width = capturedChunk.CanvasWidth,
                            Height = capturedChunk.CanvasHeight,
                            Margin = new Thickness(0, 0, 0, bottomMargin)
                        };
                        foreach (var placement in capturedChunk.Placements)
                        {
                            var decodeWidth = CalculateLibraryDetailTileDecodeWidth(placement.Width, dpiScale);
                            LibraryTimelineCaptureContext timelineContext;
                            if (timelineContexts == null || !timelineContexts.TryGetValue(placement.File, out timelineContext)) timelineContext = null;
                            Action<string> useFileAsFolderCover = null;
                            if (!timelineView)
                            {
                                useFileAsFolderCover = delegate(string imagePath)
                                {
                                    var folder = activeSelectedLibraryFolder;
                                    if (folder == null || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) || !IsImage(imagePath)) return;
                                    SaveCustomCover(folder, imagePath);
                                    renderFolderTiles?.Invoke();
                                    redrawSelectedFolderDetail?.Invoke();
                                    ShowLibraryBrowserToast(ws, "Cover saved");
                                };
                            }
                            string captureDateLabel = null;
                            captureDateLabels.TryGetValue(placement.File, out captureDateLabel);
                            var tile = CreateLibraryDetailTile(
                                placement.File,
                                placement.Width,
                                decodeWidth,
                                delegate { return ws != null && SameLibraryBrowserSelection(ws.Current, renderFolder); },
                                openSingleFileMetadataEditor,
                                updateDetailSelection,
                                ws == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : ws.SelectedDetailFiles,
                                refreshDetailSelectionUi,
                                redrawSelectedFolderDetail,
                                useFileAsFolderCover,
                                placement.Height,
                                timelineContext,
                                prioritizeDecodes,
                                captureDateLabel,
                                path => OpenLibraryCaptureViewer(this, ws, path),
                                timelineView);
                            Canvas.SetLeft(tile, placement.X);
                            Canvas.SetTop(tile, placement.Y);
                            canvas.Children.Add(tile);
                        }
                        return canvas;
                    }
                });
            }

            return rowDefinitions;
        }

        internal static Dictionary<string, string> BuildLibraryCaptureDateLabelMap(
            IEnumerable<LibraryDetailRenderGroup> groups,
            DateTime referenceDate,
            bool attachToLastFileOnly)
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (groups == null) return labels;
            foreach (var group in groups)
            {
                var groupFiles = (group?.Files ?? new List<string>())
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .ToList();
                if (groupFiles.Count == 0) continue;
                var label = BuildLibraryTimelineDayCardTitle(group.CaptureDate, referenceDate);
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (attachToLastFileOnly)
                {
                    labels[groupFiles[groupFiles.Count - 1]] = label;
                    continue;
                }

                foreach (var file in groupFiles)
                    labels[file] = label;
            }

            return labels;
        }

        /// <summary>Stable signature of calendar-day buckets for deciding whether embedded metadata changed timeline grouping.</summary>
        static string LibraryTimelineDetailGroupingFingerprint(IList<LibraryDetailRenderGroup> groups)
        {
            if (groups == null || groups.Count == 0) return string.Empty;
            var parts = new string[groups.Count];
            for (var i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                if (g == null)
                {
                    parts[i] = "x";
                    continue;
                }
                var files = g.Files ?? new List<string>();
                var anchor = files.Count == 0 ? string.Empty : files[0];
                parts[i] = g.CaptureDate.Ticks + ":" + files.Count + ":" + anchor;
            }
            return string.Join("|", parts);
        }

        internal static Dictionary<string, string> BuildLibraryCaptureDateLabelMapForPlacements(
            IEnumerable<LibraryDetailRenderGroup> groups,
            IEnumerable<LibraryDetailMasonryChunk> chunks,
            DateTime referenceDate)
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (groups == null) return labels;
            // One day stamp per calendar day: anchor is the chronologically last capture that day (latest
            // timestamp). Snapshot Files are ordered newest-first per day; Distinct keeps first occurrence.
            // The chunks argument is unused; retained for call-site compatibility.

            foreach (var renderGroup in groups)
            {
                var groupFiles = (renderGroup?.Files ?? new List<string>())
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (groupFiles.Count == 0) continue;

                var label = BuildLibraryTimelineDayCardTitle(renderGroup.CaptureDate, referenceDate);
                if (string.IsNullOrWhiteSpace(label)) continue;

                labels[groupFiles[0]] = label;
            }

            return labels;
        }

        internal static Dictionary<string, string> BuildLibraryTimelineDayLabelMap(
            IEnumerable<LibraryDetailRenderGroup> groups,
            DateTime referenceDate)
        {
            return BuildLibraryCaptureDateLabelMap(groups, referenceDate, true);
        }

        void ScheduleDeferredLibraryDetailMetadataRepair(
            List<string> deferredFiles,
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            Window libraryWindow,
            Action redrawSelectedFolderDetail)
        {
            if (deferredFiles == null || deferredFiles.Count == 0) return;
            var root = libraryRoot;
            if (string.IsNullOrWhiteSpace(root) || librarySession == null || !librarySession.HasLibraryRoot) return;
            if (!string.Equals(root, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase)) return;

            var sessionGen = Interlocked.Increment(ref _libraryDeferredMetadataRepairGeneration);
            var filesCopy = deferredFiles.ToList();
            var folderLabel = BuildLibraryBrowserTroubleshootingLabel(renderFolder);

            LogTroubleshooting("LibraryDetailMetadataRepairDeferredScheduled",
                "gen=" + sessionGen + "; files=" + filesCopy.Count + "; " + folderLabel);

            _ = Task.Run(async delegate
            {
                try
                {
                    await RunDeferredLibraryDetailMetadataRepairCoreAsync(
                        sessionGen,
                        root,
                        filesCopy,
                        ws,
                        renderFolder,
                        libraryWindow,
                        redrawSelectedFolderDetail,
                        folderLabel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogException("Deferred library metadata repair | " + folderLabel, ex);
                }
            });
        }

        async Task RunDeferredLibraryDetailMetadataRepairCoreAsync(
            int sessionGen,
            string root,
            List<string> deferredFiles,
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            Window libraryWindow,
            Action redrawSelectedFolderDetail,
            string folderLabelForLog)
        {
            const int deferredChunkSize = 36;
            if (string.IsNullOrWhiteSpace(root) || deferredFiles == null || deferredFiles.Count == 0) return;

            var metadataIndex = librarySession.LoadLibraryMetadataIndexForFilePaths(deferredFiles);
            var savedGameRows = librarySession.LoadSavedGameIndexRows();

            for (var i = 0; i < deferredFiles.Count; i += deferredChunkSize)
            {
                if (Volatile.Read(ref _libraryDeferredMetadataRepairGeneration) != sessionGen)
                {
                    LogTroubleshooting("LibraryDetailMetadataRepairDeferredCancelled", "expectedGen=" + sessionGen + "; " + folderLabelForLog);
                    return;
                }

                var take = Math.Min(deferredChunkSize, deferredFiles.Count - i);
                var chunk = new List<string>(take);
                for (var j = 0; j < take; j++)
                {
                    var path = deferredFiles[i + j];
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) chunk.Add(path);
                }

                if (chunk.Count == 0) continue;

                Dictionary<string, EmbeddedMetadataSnapshot> metadataByFile;
                try
                {
                    metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(chunk, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogException("Deferred library metadata repair batch | " + folderLabelForLog, ex);
                    await Task.Delay(120).ConfigureAwait(false);
                    continue;
                }

                var indexChanged = false;
                var gameRowsChanged = false;
                foreach (var file in chunk)
                {
                    EmbeddedMetadataSnapshot metadataSnapshot;
                    if (!metadataByFile.TryGetValue(file, out metadataSnapshot) || metadataSnapshot == null) metadataSnapshot = new EmbeddedMetadataSnapshot();
                    LibraryMetadataIndexEntry existingEntry;
                    if (!metadataIndex.TryGetValue(file, out existingEntry)) existingEntry = null;
                    var stamp = BuildLibraryMetadataStamp(file);
                    var previousGameId = existingEntry == null ? string.Empty : NormalizeGameId(existingEntry.GameId);
                    var previousConsole = existingEntry == null ? string.Empty : NormalizeConsoleLabel(existingEntry.ConsoleLabel);
                    var rebuiltEntry = librarySession.BuildResolvedLibraryMetadataIndexEntry(file, stamp, metadataSnapshot, existingEntry, metadataIndex, savedGameRows);
                    metadataIndex[file] = rebuiltEntry;
                    SetCachedFileTags(file, ParseTagText(rebuiltEntry.TagText), MetadataCacheStamp(file));
                    indexChanged = true;
                    if (!string.Equals(previousGameId, NormalizeGameId(rebuiltEntry.GameId), StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(previousConsole, NormalizeConsoleLabel(rebuiltEntry.ConsoleLabel), StringComparison.OrdinalIgnoreCase))
                    {
                        gameRowsChanged = true;
                    }
                }

                if (gameRowsChanged) librarySession.PersistGameIndexRows(savedGameRows);
                if (indexChanged)
                {
                    var repaired = new List<LibraryMetadataIndexEntry>();
                    foreach (var file in chunk)
                    {
                        LibraryMetadataIndexEntry e;
                        if (metadataIndex.TryGetValue(file, out e) && e != null) repaired.Add(e);
                    }

                    if (repaired.Count > 0) librarySession.MergePersistLibraryMetadataIndexEntries(repaired);
                }

                await Task.Delay(100).ConfigureAwait(false);
            }

            if (Volatile.Read(ref _libraryDeferredMetadataRepairGeneration) != sessionGen) return;

            LogTroubleshooting("LibraryDetailMetadataRepairDeferredComplete",
                "gen=" + sessionGen + "; files=" + deferredFiles.Count + "; " + folderLabelForLog);

            if (libraryWindow == null || redrawSelectedFolderDetail == null) return;

            await libraryWindow.Dispatcher.InvokeAsync((Action)delegate
            {
                if (Volatile.Read(ref _libraryDeferredMetadataRepairGeneration) != sessionGen) return;
                if (ws == null || !SameLibraryBrowserSelection(ws.Current, renderFolder)) return;
                redrawSelectedFolderDetail();
            }, DispatcherPriority.ApplicationIdle);
        }

        readonly struct LibraryDetailQuickSnapshotPerf
        {
            public readonly long LayoutPrepMs;
            public readonly long MediaLayoutMs;
            public readonly long TimelineAndGroupsMs;
            public readonly bool MediaLayoutReused;

            public LibraryDetailQuickSnapshotPerf(long layoutPrepMs, long mediaLayoutMs, long timelineAndGroupsMs, bool mediaLayoutReused)
            {
                LayoutPrepMs = layoutPrepMs;
                MediaLayoutMs = mediaLayoutMs;
                TimelineAndGroupsMs = timelineAndGroupsMs;
                MediaLayoutReused = mediaLayoutReused;
            }
        }

        sealed class LibraryPackedDayCardLayout
        {
            public LibraryDetailRenderGroup Group;
            public double Width;
            public double Height;
            public bool TimelineView;
            public List<LibraryDetailMasonryChunk> Chunks = new List<LibraryDetailMasonryChunk>();
        }

        List<VirtualizedRowDefinition> BuildLibraryPackedDayCardRowDefinitions(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            IList<LibraryDetailRenderGroup> groups,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile,
            ScrollViewer detailScroll,
            double viewportWidth,
            int detailTileSize,
            int detailColumns,
            double dpiScale,
            bool timelineView,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail,
            Action renderFolderTiles)
        {
            var cardGap = timelineView ? 18d : 14d;
            var safeGroups = (groups ?? new List<LibraryDetailRenderGroup>())
                .Where(group => group != null && (group.Files ?? new List<string>()).Count > 0)
                .ToList();
            if (safeGroups.Count == 0) return new List<VirtualizedRowDefinition>();

            var availableWidth = viewportWidth <= 0d ? 1100d : Math.Max(320d, viewportWidth - 6d);
            var desiredWidths = safeGroups
                .Select(group => EstimateLibraryPackedDayCardDesiredWidth((group.Files ?? new List<string>()).Count, availableWidth, timelineView, detailTileSize))
                .ToList();
            var packedRows = BuildLibraryTimelinePackedRows(desiredWidths, availableWidth, cardGap);
            var rowDefinitions = new List<VirtualizedRowDefinition>();
            var nextRowDocumentTop = 0d;
            foreach (var row in packedRows)
            {
                var rowIndexes = row == null ? new List<int>() : row.ToList();
                if (rowIndexes.Count == 0) continue;
                var rowDesiredWidths = rowIndexes.Select(index => desiredWidths[index]).ToList();
                var rowActualWidths = ExpandLibraryPackedRowWidths(rowDesiredWidths, availableWidth, cardGap);
                var rowCards = new List<LibraryPackedDayCardLayout>();
                for (var i = 0; i < rowIndexes.Count; i++)
                {
                    var cardLayout = BuildLibraryPackedDayCardLayout(
                        safeGroups[rowIndexes[i]],
                        rowActualWidths[i],
                        detailTileSize,
                        timelineView,
                        mediaLayoutByFile);
                    if (cardLayout != null) rowCards.Add(cardLayout);
                }
                var estimatedHeight = rowCards
                    .Select(card => card.Height)
                    .DefaultIfEmpty(420d)
                    .Max();
                var rowVirtualHeight = (int)Math.Ceiling(estimatedHeight + cardGap);
                var capturedDocTop = nextRowDocumentTop;
                nextRowDocumentTop += rowVirtualHeight;
                rowDefinitions.Add(new VirtualizedRowDefinition
                {
                    Height = rowVirtualHeight,
                    Build = delegate
                    {
                        var prioritizeDecodes = LibraryDetailTileRowIntersectsViewport(detailScroll, capturedDocTop, rowVirtualHeight);
                        var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, cardGap) };
                        for (var i = 0; i < rowCards.Count; i++)
                        {
                            var card = BuildLibraryPackedDayCard(
                                ws,
                                renderFolder,
                                rowCards[i],
                                timelineContexts,
                                dpiScale,
                                prioritizeDecodes,
                                openSingleFileMetadataEditor,
                                updateDetailSelection,
                                refreshDetailSelectionUi,
                                redrawSelectedFolderDetail,
                                renderFolderTiles);
                            if (card != null)
                            {
                                card.Margin = new Thickness(0, 0, i < rowCards.Count - 1 ? cardGap : 0, 0);
                                rowPanel.Children.Add(card);
                            }
                        }
                        return rowPanel;
                    }
                });
            }
            return rowDefinitions;
        }

        LibraryPackedDayCardLayout BuildLibraryPackedDayCardLayout(
            LibraryDetailRenderGroup group,
            double cardWidth,
            int detailTileSize,
            bool timelineView,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile)
        {
            if (group == null) return null;
            var groupFiles = (group.Files ?? new List<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .ToList();
            if (groupFiles.Count == 0) return null;

            const int masonryTileGap = 4;
            var innerPackWidth = Math.Max(240d, cardWidth);
            int targetTileWidth;
            int minTileWidth;
            int maxTileWidth;
            if (timelineView)
            {
                var packedTileSize = CalculateLibraryTimelinePackedTileSize(detailTileSize, innerPackWidth);
                targetTileWidth = Math.Max(180, packedTileSize);
                minTileWidth = Math.Max(140, Math.Min(targetTileWidth, (int)Math.Round(targetTileWidth * 0.58d)));
                maxTileWidth = groupFiles.Count == 1
                    ? (int)Math.Floor(innerPackWidth)
                    : Math.Min((int)Math.Floor(innerPackWidth), (int)Math.Round(targetTileWidth * 1.55d));
            }
            else
            {
                var userBase = Math.Max(160, detailTileSize);
                targetTileWidth = Math.Max(180, (int)Math.Round(Math.Min(userBase * 1.05d, Math.Max(220d, innerPackWidth * 0.72d))));
                minTileWidth = Math.Max(120, Math.Min(targetTileWidth, (int)Math.Round(targetTileWidth * 0.58d)));
                maxTileWidth = groupFiles.Count == 1
                    ? (int)Math.Floor(innerPackWidth)
                    : Math.Min((int)Math.Floor(innerPackWidth), (int)Math.Round(targetTileWidth * 1.55d));
            }
            maxTileWidth = Math.Max(minTileWidth, maxTileWidth);
            var chunks = BuildLibraryDetailMasonryChunks(
                groupFiles,
                innerPackWidth,
                masonryTileGap,
                targetTileWidth,
                minTileWidth,
                maxTileWidth,
                timelineView,
                mediaLayoutByFile);
            // Timeline day headers use a larger type ramp; keep Height in sync for virtualization row sizing.
            var headerHeight = group.CaptureDate <= DateTime.MinValue
                ? 0d
                : (timelineView ? 36d : 24d);
            var chunkHeights = chunks.Sum(chunk => chunk == null ? 0d : chunk.CanvasHeight);
            var chunkGaps = Math.Max(0, chunks.Count - 1) * masonryTileGap;
            return new LibraryPackedDayCardLayout
            {
                Group = group,
                Width = Math.Max(timelineView ? 320d : 360d * 1.75d, Math.Ceiling(cardWidth)),
                Height = headerHeight + chunkHeights + chunkGaps,
                TimelineView = timelineView,
                Chunks = chunks
            };
        }

        FrameworkElement BuildLibraryPackedDayCard(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            LibraryPackedDayCardLayout cardLayout,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            double dpiScale,
            bool prioritizeRowDecodes,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail,
            Action renderFolderTiles)
        {
            if (cardLayout == null || cardLayout.Group == null) return null;
            var group = cardLayout.Group;
            var timelineView = cardLayout.TimelineView;
            const int masonryTileGap = 4;
            var groupFiles = (group.Files ?? new List<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .ToList();
            if (groupFiles.Count == 0) return null;
            var labelText = group.CaptureDate <= DateTime.MinValue
                ? string.Empty
                : (group.CaptureDate.Year == DateTime.Today.Year
                    ? group.CaptureDate.ToString("ddd, MMM d")
                    : group.CaptureDate.ToString("ddd, MMM d, yyyy"));
            var captureDateLabels = BuildLibraryCaptureDateLabelMapForPlacements(
                new[] { group },
                cardLayout.Chunks,
                DateTime.Today);
            var stack = new StackPanel();
            if (!string.IsNullOrWhiteSpace(labelText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = labelText,
                    Foreground = Brush(timelineView ? DesignTokens.TextLabelMuted : "#8FA1AD"),
                    FontSize = timelineView ? 15.5 : 11.5,
                    FontWeight = timelineView ? FontWeights.SemiBold : FontWeights.Medium,
                    Margin = new Thickness(2, 0, 0, timelineView ? 10 : 6)
                });
            }

            foreach (var chunk in cardLayout.Chunks)
            {
                if (chunk == null) continue;
                var canvas = new Canvas
                {
                    Width = chunk.CanvasWidth,
                    Height = chunk.CanvasHeight,
                    Margin = new Thickness(0, 0, 0, chunk == cardLayout.Chunks.Last() ? 0 : masonryTileGap)
                };
                foreach (var placement in chunk.Placements)
                {
                    var decodeWidth = CalculateLibraryDetailTileDecodeWidth(placement.Width, dpiScale);
                    LibraryTimelineCaptureContext timelineContext;
                    if (timelineContexts == null || !timelineContexts.TryGetValue(placement.File, out timelineContext)) timelineContext = null;
                    Action<string> useFileAsFolderCover = null;
                    if (!timelineView)
                    {
                        useFileAsFolderCover = delegate(string imagePath)
                        {
                            var folder = activeSelectedLibraryFolder;
                            if (folder == null || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) || !IsImage(imagePath)) return;
                            SaveCustomCover(folder, imagePath);
                            renderFolderTiles?.Invoke();
                            redrawSelectedFolderDetail?.Invoke();
                            ShowLibraryBrowserToast(ws, "Cover saved");
                        };
                    }
                    string captureDateLabel = null;
                    captureDateLabels.TryGetValue(placement.File, out captureDateLabel);
                    var tile = CreateLibraryDetailTile(
                        placement.File,
                        placement.Width,
                        decodeWidth,
                        delegate { return ws != null && SameLibraryBrowserSelection(ws.Current, renderFolder); },
                        openSingleFileMetadataEditor,
                        updateDetailSelection,
                        ws == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : ws.SelectedDetailFiles,
                        refreshDetailSelectionUi,
                        redrawSelectedFolderDetail,
                        useFileAsFolderCover,
                        placement.Height,
                        timelineContext,
                        prioritizeRowDecodes,
                        captureDateLabel,
                        path => OpenLibraryCaptureViewer(this, ws, path),
                        timelineView);
                    Canvas.SetLeft(tile, placement.X);
                    Canvas.SetTop(tile, placement.Y);
                    canvas.Children.Add(tile);
                }
                stack.Children.Add(canvas);
            }

            return new Border
            {
                Width = cardLayout.Width,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Child = stack
            };
        }

        FrameworkElement BuildLibraryDetailNoFolderSelectedPlaceholder()
        {
            var root = new StackPanel { Margin = new Thickness(8, 12, 12, 16) };
            root.Children.Add(new TextBlock
            {
                Text = "Choose a game",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(DesignTokens.TextOnInput)
            });
            root.Children.Add(new TextBlock
            {
                Text = "Select a folder on the left to browse captures, covers, and metadata for that game.",
                Foreground = Brush(DesignTokens.TextLabelMuted),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return root;
        }

        FrameworkElement BuildLibraryDetailLoadingPlaceholder()
        {
            var root = new StackPanel { Margin = new Thickness(8, 12, 12, 0) };
            root.Children.Add(new TextBlock
            {
                Text = "Loading captures",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(DesignTokens.TextOnInput)
            });
            root.Children.Add(new TextBlock
            {
                Text = "Building thumbnails and layout for this folder.",
                Foreground = Brush(DesignTokens.TextLabelMuted),
                FontSize = 13,
                Margin = new Thickness(0, 6, 0, 14)
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < 5; i++)
            {
                row.Children.Add(new Border
                {
                    Width = 56,
                    Height = 56,
                    Margin = new Thickness(0, 0, 8, 0),
                    CornerRadius = new CornerRadius(8),
                    Background = Brush(DesignTokens.PanelElevated),
                    BorderBrush = Brush(DesignTokens.BorderDefault),
                    BorderThickness = new Thickness(1)
                });
            }
            root.Children.Add(row);
            return root;
        }

        FrameworkElement BuildLibraryDetailEmptyCapturesPlaceholder(bool timelineView, DateTime rangeStart, DateTime rangeEnd, Action redrawDetail)
        {
            var root = new StackPanel { Margin = new Thickness(8, 12, 12, 16), MaxWidth = 480 };
            var title = timelineView ? "No captures in this range" : "No captures in this folder";
            var body = timelineView
                ? "Nothing falls between " + (rangeStart > DateTime.MinValue ? rangeStart.ToString("yyyy-MM-dd") : "start") + " and " + (rangeEnd > DateTime.MinValue ? rangeEnd.ToString("yyyy-MM-dd") : "end") + ". Widen the range or switch grouping."
                : "This game folder has no screenshots or clips yet. Import captures or pick another folder.";
            root.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(DesignTokens.TextOnInput),
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = 13,
                Foreground = Brush(DesignTokens.TextLabelMuted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 12)
            });
            if (redrawDetail != null)
            {
                var b = new Button
                {
                    Content = "Refresh this view",
                    Padding = new Thickness(14, 8, 14, 8),
                    FontSize = 13,
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                    Background = Brush(DesignTokens.ActionSecondaryFill),
                    BorderBrush = Brush(DesignTokens.BorderDefault),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                b.Click += delegate { redrawDetail(); };
                root.Children.Add(b);
            }
            return root;
        }
    }
}
