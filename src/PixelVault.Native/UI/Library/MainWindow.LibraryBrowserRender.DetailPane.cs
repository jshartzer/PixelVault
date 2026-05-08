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
            var detailRenderCancellationToken = ws.DetailRenderCancellation.BeginRender(renderVersion);
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
            var calendarTimelineView = IsLibraryBrowserTimelineView(renderFolder);
            var sessionView = IsLibraryBrowserSessionView(renderFolder);
            var timelineView = calendarTimelineView || sessionView;
            var sessionThresholdMinutes = SettingsService.NormalizeLibrarySessionThresholdMinutes(renderFolder == null ? librarySessionThresholdMinutes : (renderFolder.SessionThresholdMinutes <= 0 ? librarySessionThresholdMinutes : renderFolder.SessionThresholdMinutes));
            var detailLayout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll, true, timelineView);
            var size = detailLayout.TileSize;
            ws.LastDetailViewportWidth = ResolveScrollViewerLayoutWidth(panes == null ? null : panes.ThumbScroll);
            var shouldRestoreDetailScroll = ws.PreserveDetailScrollOnNextRender && ws.PreservedDetailScrollOffset > 0.1d;
            var restoreDetailScrollOffset = shouldRestoreDetailScroll ? (double?)ws.PreservedDetailScrollOffset : null;
            var restoreDetailScrollPending = shouldRestoreDetailScroll;
            ws.PreserveDetailScrollOnNextRender = false;
            var detailMetadataScrollOffset = restoreDetailScrollOffset ?? 0d;
            var detailMetadataViewportHeight = 720d;
            if (panes != null && panes.ThumbScroll != null)
            {
                detailMetadataScrollOffset = restoreDetailScrollOffset ?? Math.Max(0d, panes.ThumbScroll.VerticalOffset);
                detailMetadataViewportHeight = panes.ThumbScroll.ViewportHeight;
                if (detailMetadataViewportHeight <= 0d) detailMetadataViewportHeight = panes.ThumbScroll.ActualHeight;
                if (detailMetadataViewportHeight <= 0d) detailMetadataViewportHeight = 720d;
            }
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
            if (calendarTimelineView && TryAlignLibraryTimelineRollingPresetToToday(ws))
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
                + (calendarTimelineView ? "; timelineRange=" + timelineRangeStart.ToString("yyyy-MM-dd") + ".." + timelineRangeEnd.ToString("yyyy-MM-dd") : string.Empty)
                + (sessionView ? "; sessionThresholdMin=" + sessionThresholdMinutes : string.Empty)
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
            const int LibraryDetailMetadataDeferredChunkSize = 36;
            var detailDpiScaleForBackground = ResolveLibraryDpiScale(panes?.ThumbScroll);
            Task.Run(async delegate
            {
                LibraryDetailQuickSnapshotPerf? quickSnapshotPerf = null;
                Action throwIfDetailRenderCancelled = delegate
                {
                    if (!ws.DetailRenderCancellation.IsCurrent(renderVersion, detailRenderCancellationToken))
                    {
                        throw new OperationCanceledException(detailRenderCancellationToken);
                    }
                };
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
                    if (sessionView)
                    {
                        return BuildLibrarySessionCardRowDefinitions(
                            ws,
                            renderFolder,
                            snapshot.Groups,
                            timelineCtx,
                            mediaMap,
                            panes == null ? null : panes.ThumbScroll,
                            detailViewportWidth,
                            effectiveTileSize,
                            detailDpiScaleForBackground,
                            libraryWindow,
                            openSingleFileMetadataEditor,
                            updateDetailSelection,
                            refreshDetailSelectionUi,
                            redrawSelectedFolderDetail);
                    }

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
                    if (!ws.DetailRenderCancellation.IsCurrent(renderVersion, detailRenderCancellationToken))
                    {
                        LogTroubleshooting("LibraryDetailRenderSkipped",
                            "renderVersion=" + renderVersion
                            + "; stage=" + snapshotStage
                            + "; reason=cancelled-render");
                        return;
                    }
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
                        panes.DetailMeta.Text = sessionView
                            ? BuildLibrarySessionSummaryText(visibleFiles.Count, snapshot.Groups == null ? 0 : snapshot.Groups.Count, distinctGames, distinctPlatforms, newestCapture, oldestCapture, sessionThresholdMinutes)
                            : BuildLibraryTimelineSummaryText(visibleFiles.Count, distinctGames, distinctPlatforms, newestCapture, oldestCapture);
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
                                        calendarTimelineView,
                                        sessionView,
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
                    throwIfDetailRenderCancelled();
                    traceStep("LibraryDetailBackgroundStart", "thread=" + Environment.CurrentManagedThreadId);
                    List<GameIndexEditorRow> savedGameRows = null;
                    if (timelineView)
                    {
                        savedGameRows = librarySession.LoadSavedGameIndexRows();
                        throwIfDetailRenderCancelled();
                        traceStep("LibraryDetailTimelineGameRowsLoaded", "savedGameRows=" + savedGameRows.Count);
                    }
                    var detailFiles = GetFilesForLibraryFolderEntry(displayFolder, false)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    throwIfDetailRenderCancelled();
                    traceStep("LibraryDetailFilesEnumerated", "files=" + detailFiles.Count);
                    var metadataIndex = librarySession.LoadLibraryMetadataIndexForFilePaths(detailFiles);
                    throwIfDetailRenderCancelled();
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
                    throwIfDetailRenderCancelled();
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
                                return (FilePath: file, CaptureDate: captureDate);
                            })
                            .Where(entry => !calendarTimelineView || LibraryTimelineRangeContainsCapture(entry.CaptureDate, timelineRangeStart, timelineRangeEnd))
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
                        if (sessionView)
                        {
                            var threshold = TimeSpan.FromMinutes(sessionThresholdMinutes);
                            var currentSession = new List<(string FilePath, DateTime CaptureDate)>();
                            DateTime? previousNewerCapture = null;
                            Action flushSession = delegate
                            {
                                if (currentSession.Count == 0) return;
                                var sessionNewest = currentSession
                                    .Select(entry => entry.CaptureDate)
                                    .DefaultIfEmpty(DateTime.MinValue)
                                    .Max();
                                var sessionOldest = currentSession
                                    .Select(entry => entry.CaptureDate)
                                    .DefaultIfEmpty(DateTime.MinValue)
                                    .Min();
                                var sessionFiles = currentSession
                                    .OrderByDescending(entry => entry.CaptureDate)
                                    .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                                    .Select(entry => entry.FilePath)
                                    .ToList();
                                snapshot.Groups.Add(new LibraryDetailRenderGroup
                                {
                                    CaptureDate = sessionNewest <= DateTime.MinValue ? DateTime.MinValue : sessionNewest.Date,
                                    HeaderText = BuildLibrarySessionCardTitle(sessionNewest, sessionOldest, DateTime.Today),
                                    SubtitleText = BuildLibrarySessionCardSubtitle(sessionFiles.Count, sessionNewest, sessionOldest),
                                    SessionStartDate = sessionOldest,
                                    SessionEndDate = sessionNewest,
                                    Files = sessionFiles
                                });
                                currentSession.Clear();
                            };
                            foreach (var entry in datedFiles)
                            {
                                var capture = entry.CaptureDate;
                                if (previousNewerCapture.HasValue
                                    && capture > DateTime.MinValue
                                    && previousNewerCapture.Value > DateTime.MinValue
                                    && previousNewerCapture.Value - capture > threshold)
                                {
                                    flushSession();
                                }
                                currentSession.Add(entry);
                                previousNewerCapture = capture;
                            }
                            flushSession();
                        }
                        else
                        {
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
                        }

                        var tailMs = segmentSw.ElapsedMilliseconds;
                        if (timelineMetadataSnapshots == null && reuseMediaFrom == null)
                        {
                            quickSnapshotPerf = new LibraryDetailQuickSnapshotPerf(layoutPrepMs, mediaLayoutMs, tailMs, mediaReused);
                        }

                        return snapshot;
                    };

                    traceStep("LibraryDetailQuickSnapshotBuildStart", "files=" + detailFiles.Count);
                    throwIfDetailRenderCancelled();
                    var quickSnapshot = buildSnapshot(null, null);
                    throwIfDetailRenderCancelled();
                    traceStep("LibraryDetailQuickSnapshotBuilt",
                        "groups=" + quickSnapshot.Groups.Count
                        + "; visibleFiles=" + quickSnapshot.VisibleFiles.Count);
                    traceStep("LibraryDetailQuickSnapshotDispatchStart", "stage=initial");
                    var dispatcherWallSw = Stopwatch.StartNew();
                    var quickVirtualRows = buildVirtualRowsForSnapshot(quickSnapshot);
                    throwIfDetailRenderCancelled();
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(quickSnapshot, true, quickVirtualRows); }));
                    throwIfDetailRenderCancelled();
                    traceStep("LibraryDetailQuickSnapshotDispatchComplete", "stage=initial; dispatcherWallMs=" + dispatcherWallSw.ElapsedMilliseconds);
                    var metadataFileOrder = BuildLibraryDetailViewportFileOrder(
                        quickVirtualRows,
                        quickSnapshot.VisibleFiles,
                        detailMetadataScrollOffset,
                        detailMetadataViewportHeight);
                    var initialMetadataFiles = metadataFileOrder.PrimaryFiles.Count > 0
                        ? metadataFileOrder.PrimaryFiles.ToList()
                        : metadataFileOrder.DeferredFiles.Take(LibraryDetailMetadataDeferredChunkSize).ToList();
                    var deferredTimelineMetadataFiles = metadataFileOrder.PrimaryFiles.Count > 0
                        ? metadataFileOrder.DeferredFiles.ToList()
                        : metadataFileOrder.DeferredFiles.Skip(initialMetadataFiles.Count).ToList();
                    traceStep("LibraryDetailMetadataViewportPlan",
                        "primaryFiles=" + initialMetadataFiles.Count
                        + "; deferredFiles=" + deferredTimelineMetadataFiles.Count
                        + "; totalFiles=" + quickSnapshot.VisibleFiles.Count
                        + "; scrollOffset=" + Math.Round(detailMetadataScrollOffset)
                        + "; viewportHeight=" + Math.Round(detailMetadataViewportHeight));

                    var timelineMetadataSnapshots = new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
                    Action<Dictionary<string, EmbeddedMetadataSnapshot>> mergeTimelineMetadata = delegate(Dictionary<string, EmbeddedMetadataSnapshot> source)
                    {
                        if (source == null || source.Count == 0) return;
                        foreach (var pair in source)
                        {
                            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                            timelineMetadataSnapshots[pair.Key] = pair.Value ?? new EmbeddedMetadataSnapshot();
                        }
                    };
                    Func<string, Task> applyTimelineMetadataSnapshotAsync = async delegate(string stage)
                    {
                        throwIfDetailRenderCancelled();
                        var commentSnapshot = buildSnapshot(timelineMetadataSnapshots, quickSnapshot);
                        throwIfDetailRenderCancelled();
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
                                "stage=" + stage
                                + "; commentsChanged=" + commentsChanged
                                + "; dayGroupingChanged=" + dayGroupingChanged);
                            var commentVirtualRows = buildVirtualRowsForSnapshot(commentSnapshot);
                            throwIfDetailRenderCancelled();
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(commentSnapshot, false, commentVirtualRows); }));
                            throwIfDetailRenderCancelled();
                            traceStep("LibraryDetailMetadataDispatchComplete", "stage=" + stage);
                            quickSnapshot = commentSnapshot;
                        }
                    };

                    if (initialMetadataFiles.Count > 0)
                    {
                        try
                        {
                            throwIfDetailRenderCancelled();
                            traceStep("LibraryDetailMetadataReadStart", "scope=viewport; files=" + initialMetadataFiles.Count);
                            var initialMetadataSnapshots = await metadataService.ReadEmbeddedMetadataBatchAsync(initialMetadataFiles, detailRenderCancellationToken).ConfigureAwait(false);
                            throwIfDetailRenderCancelled();
                            mergeTimelineMetadata(initialMetadataSnapshots);
                            traceStep("LibraryDetailMetadataReadComplete",
                                "scope=viewport; metadataResults=" + initialMetadataSnapshots.Count
                                + "; accumulatedMetadata=" + timelineMetadataSnapshots.Count);
                            await applyTimelineMetadataSnapshotAsync("metadata-refresh");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
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
                        var repairFileOrder = BuildLibraryDetailViewportFileOrder(
                            quickVirtualRows,
                            filesMissingCaptureTicks,
                            detailMetadataScrollOffset,
                            detailMetadataViewportHeight);
                        var repairPrimaryFiles = repairFileOrder.PrimaryFiles.Count > 0
                            ? repairFileOrder.PrimaryFiles.ToList()
                            : repairFileOrder.DeferredFiles.Take(LibraryDetailMetadataDeferredChunkSize).ToList();
                        var repairTargets = repairPrimaryFiles
                            .Take(LibraryDetailMetadataRepairMaxFilesPerPass)
                            .ToList();
                        var deferredMetadataRepairFiles = new List<string>();
                        if (repairFileOrder.PrimaryFiles.Count > LibraryDetailMetadataRepairMaxFilesPerPass)
                        {
                            deferredMetadataRepairFiles.AddRange(repairFileOrder.PrimaryFiles.Skip(LibraryDetailMetadataRepairMaxFilesPerPass));
                        }
                        if (repairFileOrder.PrimaryFiles.Count > 0)
                        {
                            deferredMetadataRepairFiles.AddRange(repairFileOrder.DeferredFiles);
                        }
                        else
                        {
                            deferredMetadataRepairFiles.AddRange(repairFileOrder.DeferredFiles.Skip(repairTargets.Count));
                        }
                        traceStep("LibraryDetailMetadataRepairViewportPlan",
                            "repairNow=" + repairTargets.Count
                            + "; repairDeferred=" + deferredMetadataRepairFiles.Count
                            + "; totalMissingCaptureTicks=" + filesMissingCaptureTicks.Count);
                        if (deferredMetadataRepairFiles.Count > 0)
                        {
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
                            throwIfDetailRenderCancelled();
                            if (savedGameRows == null) savedGameRows = librarySession.LoadSavedGameIndexRows();
                            traceStep("LibraryDetailMetadataRepairRowsLoaded", "savedGameRows=" + savedGameRows.Count);
                            throwIfDetailRenderCancelled();
                            var metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(repairTargets, detailRenderCancellationToken).ConfigureAwait(false);
                            throwIfDetailRenderCancelled();
                            mergeTimelineMetadata(metadataByFile);
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
                        catch (OperationCanceledException)
                        {
                            throw;
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
                        throwIfDetailRenderCancelled();
                        var refinedSnapshot = buildSnapshot(timelineMetadataSnapshots, quickSnapshot);
                        throwIfDetailRenderCancelled();
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
                            throwIfDetailRenderCancelled();
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(refinedSnapshot, false, refinedVirtualRows); }));
                            throwIfDetailRenderCancelled();
                            traceStep("LibraryDetailRefinedSnapshotDispatchComplete", "stage=refined");
                            quickSnapshot = refinedSnapshot;
                        }

                        if (deferredMetadataRepairFiles != null && deferredMetadataRepairFiles.Count > 0)
                        {
                            ScheduleDeferredLibraryDetailMetadataRepair(
                                deferredMetadataRepairFiles,
                                ws,
                                renderFolder,
                                renderVersion,
                                libraryWindow,
                                detailRenderCancellationToken,
                                redrawSelectedFolderDetail);
                        }
                    }
                    var pendingDeferredTimelineMetadataFiles = deferredTimelineMetadataFiles
                        .Where(file => !string.IsNullOrWhiteSpace(file) && !timelineMetadataSnapshots.ContainsKey(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (pendingDeferredTimelineMetadataFiles.Count > 0)
                    {
                        try
                        {
                            var deferredReadAny = false;
                            for (var i = 0; i < pendingDeferredTimelineMetadataFiles.Count; i += LibraryDetailMetadataDeferredChunkSize)
                            {
                                throwIfDetailRenderCancelled();
                                var chunk = pendingDeferredTimelineMetadataFiles
                                    .Skip(i)
                                    .Take(LibraryDetailMetadataDeferredChunkSize)
                                    .ToList();
                                if (chunk.Count == 0) continue;
                                traceStep("LibraryDetailMetadataDeferredReadStart",
                                    "chunk=" + ((i / LibraryDetailMetadataDeferredChunkSize) + 1)
                                    + "; files=" + chunk.Count
                                    + "; remaining=" + Math.Max(0, pendingDeferredTimelineMetadataFiles.Count - i - chunk.Count));
                                var metadataChunk = await metadataService.ReadEmbeddedMetadataBatchAsync(chunk, detailRenderCancellationToken).ConfigureAwait(false);
                                throwIfDetailRenderCancelled();
                                mergeTimelineMetadata(metadataChunk);
                                deferredReadAny = true;
                                traceStep("LibraryDetailMetadataDeferredReadComplete",
                                    "metadataResults=" + metadataChunk.Count
                                    + "; accumulatedMetadata=" + timelineMetadataSnapshots.Count);
                                await Task.Delay(50, detailRenderCancellationToken).ConfigureAwait(false);
                            }

                            if (deferredReadAny)
                            {
                                await applyTimelineMetadataSnapshotAsync("metadata-deferred");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception deferredMetadataEx)
                        {
                            LogException("Library detail deferred metadata read | " + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)"), deferredMetadataEx);
                            LogTroubleshooting("LibraryDetailMetadataDeferredReadFail",
                                "renderVersion=" + renderVersion
                                + "; type=" + deferredMetadataEx.GetType().FullName
                                + "; message=" + deferredMetadataEx.Message
                                + "; exception=" + FormatExceptionForTroubleshooting(deferredMetadataEx)
                                + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        }
                    }
                    traceStep("LibraryDetailBackgroundComplete", "done=true");
                }
                catch (OperationCanceledException) when (!LibraryDetailRenderGuard.CanApply(
                    ws.DetailRenderCancellation,
                    renderVersion,
                    detailRenderCancellationToken,
                    ws.DetailRenderSequence,
                    SameLibraryBrowserSelection(ws.Current, renderFolder)))
                {
                    traceStep("LibraryDetailBackgroundCancelled", "reason=detail-render-cancelled");
                }
                catch (Exception ex)
                {
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                    {
                        if (!LibraryDetailRenderGuard.CanApply(
                            ws.DetailRenderCancellation,
                            renderVersion,
                            detailRenderCancellationToken,
                            ws.DetailRenderSequence,
                            SameLibraryBrowserSelection(ws.Current, renderFolder))) return;
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
            }, detailRenderCancellationToken);
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
            Action renderFolderTiles,
            Action<string> openCaptureViewerOverride = null)
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
                    Files = capturedChunk.Placements == null
                        ? new List<string>()
                        : capturedChunk.Placements
                            .Select(placement => placement == null ? null : placement.File)
                            .Where(file => !string.IsNullOrWhiteSpace(file))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList(),
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
                                openCaptureViewerOverride ?? (path => OpenLibraryCaptureViewer(this, ws, path)),
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
                var label = string.IsNullOrWhiteSpace(group.HeaderText)
                    ? BuildLibraryTimelineDayCardTitle(group.CaptureDate, referenceDate)
                    : group.HeaderText;
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
                parts[i] = g.CaptureDate.Ticks
                    + ":" + g.SessionStartDate.Ticks
                    + ":" + g.SessionEndDate.Ticks
                    + ":" + (g.HeaderText ?? string.Empty)
                    + ":" + files.Count
                    + ":" + anchor;
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

                var label = string.IsNullOrWhiteSpace(renderGroup.HeaderText)
                    ? BuildLibraryTimelineDayCardTitle(renderGroup.CaptureDate, referenceDate)
                    : renderGroup.HeaderText;
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
            int renderVersion,
            Window libraryWindow,
            CancellationToken detailRenderCancellationToken,
            Action redrawSelectedFolderDetail)
        {
            if (deferredFiles == null || deferredFiles.Count == 0) return;
            if (detailRenderCancellationToken.IsCancellationRequested) return;
            if (ws == null) return;
            if (!LibraryDetailRenderGuard.CanApply(
                ws.DetailRenderCancellation,
                renderVersion,
                detailRenderCancellationToken,
                ws.DetailRenderSequence,
                SameLibraryBrowserSelection(ws.Current, renderFolder))) return;
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
                        renderVersion,
                        libraryWindow,
                        detailRenderCancellationToken,
                        redrawSelectedFolderDetail,
                        folderLabel).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!LibraryDetailRenderGuard.CanApply(
                    ws.DetailRenderCancellation,
                    renderVersion,
                    detailRenderCancellationToken,
                    ws.DetailRenderSequence,
                    SameLibraryBrowserSelection(ws.Current, renderFolder)))
                {
                    LogTroubleshooting("LibraryDetailMetadataRepairDeferredCancelled",
                        "expectedGen=" + sessionGen + "; reason=detail-render-cancelled; " + folderLabel);
                }
                catch (Exception ex)
                {
                    LogException("Deferred library metadata repair | " + folderLabel, ex);
                }
            }, detailRenderCancellationToken);
        }

        async Task RunDeferredLibraryDetailMetadataRepairCoreAsync(
            int sessionGen,
            string root,
            List<string> deferredFiles,
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            int renderVersion,
            Window libraryWindow,
            CancellationToken detailRenderCancellationToken,
            Action redrawSelectedFolderDetail,
            string folderLabelForLog)
        {
            const int deferredChunkSize = 36;
            if (string.IsNullOrWhiteSpace(root) || deferredFiles == null || deferredFiles.Count == 0) return;
            bool renderIsStillCurrent()
            {
                return LibraryDetailRenderGuard.CanApply(
                    ws == null ? null : ws.DetailRenderCancellation,
                    renderVersion,
                    detailRenderCancellationToken,
                    ws == null ? 0 : ws.DetailRenderSequence,
                    ws != null && SameLibraryBrowserSelection(ws.Current, renderFolder));
            }

            detailRenderCancellationToken.ThrowIfCancellationRequested();
            if (!renderIsStillCurrent()) return;

            var metadataIndex = librarySession.LoadLibraryMetadataIndexForFilePaths(deferredFiles);
            detailRenderCancellationToken.ThrowIfCancellationRequested();
            var savedGameRows = librarySession.LoadSavedGameIndexRows();

            for (var i = 0; i < deferredFiles.Count; i += deferredChunkSize)
            {
                detailRenderCancellationToken.ThrowIfCancellationRequested();
                if (!renderIsStillCurrent()) return;
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
                    metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(chunk, detailRenderCancellationToken).ConfigureAwait(false);
                    detailRenderCancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogException("Deferred library metadata repair batch | " + folderLabelForLog, ex);
                    await Task.Delay(120, detailRenderCancellationToken).ConfigureAwait(false);
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

                await Task.Delay(100, detailRenderCancellationToken).ConfigureAwait(false);
            }

            detailRenderCancellationToken.ThrowIfCancellationRequested();
            if (!renderIsStillCurrent()) return;
            if (Volatile.Read(ref _libraryDeferredMetadataRepairGeneration) != sessionGen) return;

            LogTroubleshooting("LibraryDetailMetadataRepairDeferredComplete",
                "gen=" + sessionGen + "; files=" + deferredFiles.Count + "; " + folderLabelForLog);

            if (libraryWindow == null || redrawSelectedFolderDetail == null) return;

            await libraryWindow.Dispatcher.InvokeAsync((Action)delegate
            {
                if (!renderIsStillCurrent()) return;
                if (Volatile.Read(ref _libraryDeferredMetadataRepairGeneration) != sessionGen) return;
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

        readonly object _librarySessionAchievementCacheLock = new object();
        readonly Dictionary<string, Task<GameAchievementsFetchService.FetchResult>> _librarySessionAchievementFetchTasks =
            new Dictionary<string, Task<GameAchievementsFetchService.FetchResult>>(StringComparer.OrdinalIgnoreCase);

        sealed class LibrarySessionAchievementTarget
        {
            public string SourceLabel;
            public string GameTitle;
            public string PlatformLabel;
            public string SteamAppId;
            public string RetroAchievementsGameId;

            public string CacheIdentity
            {
                get
                {
                    if (string.Equals(SourceLabel, "Steam", StringComparison.OrdinalIgnoreCase))
                        return "steam|" + (SteamAppId ?? string.Empty).Trim();
                    if (string.Equals(SourceLabel, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
                        return "ra|" + (RetroAchievementsGameId ?? string.Empty).Trim();
                    return (SourceLabel ?? string.Empty).Trim() + "|" + (GameTitle ?? string.Empty).Trim();
                }
            }
        }

        sealed class LibrarySessionAchievementMetricResult
        {
            public string Label;
            public string ToolTip;
        }

        sealed class LibrarySessionCardLayout
        {
            public LibraryDetailRenderGroup Group;
            public double Width;
            public double Height;
            public int PreviewColumns;
            public int PreviewRows;
            public int PreviewTileWidth;
            public int PreviewTileHeight;
            public List<string> PreviewFiles = new List<string>();
        }

        List<VirtualizedRowDefinition> BuildLibrarySessionCardRowDefinitions(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            IList<LibraryDetailRenderGroup> groups,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile,
            ScrollViewer detailScroll,
            double viewportWidth,
            int detailTileSize,
            double dpiScale,
            Window libraryWindow,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail)
        {
            var safeGroups = (groups ?? new List<LibraryDetailRenderGroup>())
                .Where(group => group != null && (group.Files ?? new List<string>()).Any(file => !string.IsNullOrWhiteSpace(file)))
                .ToList();
            if (safeGroups.Count == 0) return new List<VirtualizedRowDefinition>();

            const double cardGap = 18d;
            var availableWidth = viewportWidth <= 0d ? 1100d : Math.Max(320d, viewportWidth - 6d);
            var cardColumns = availableWidth >= 1500d ? 3 : (availableWidth >= 900d ? 2 : 1);
            while (cardColumns > 1 && (availableWidth - ((cardColumns - 1) * cardGap)) / cardColumns < 380d)
                cardColumns--;

            var cardWidth = Math.Floor((availableWidth - ((cardColumns - 1) * cardGap)) / cardColumns);
            cardWidth = Math.Max(320d, cardWidth);
            var layouts = safeGroups
                .Select(group => BuildLibrarySessionCardLayout(group, cardWidth, detailTileSize))
                .Where(layout => layout != null)
                .ToList();
            var rows = new List<VirtualizedRowDefinition>();
            var nextRowDocumentTop = 0d;
            for (var start = 0; start < layouts.Count; start += cardColumns)
            {
                var rowCards = layouts.Skip(start).Take(cardColumns).ToList();
                if (rowCards.Count == 0) continue;
                var rowHeight = (int)Math.Ceiling(rowCards.Select(card => card.Height).DefaultIfEmpty(420d).Max() + cardGap);
                var capturedDocTop = nextRowDocumentTop;
                nextRowDocumentTop += rowHeight;
                var rowFiles = rowCards
                    .SelectMany(card => card.PreviewFiles ?? new List<string>())
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                rows.Add(new VirtualizedRowDefinition
                {
                    Height = rowHeight,
                    Files = rowFiles,
                    Build = delegate
                    {
                        var prioritizeDecodes = LibraryDetailTileRowIntersectsViewport(detailScroll, capturedDocTop, rowHeight);
                        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, cardGap) };
                        for (var i = 0; i < rowCards.Count; i++)
                        {
                            var card = BuildLibrarySessionCard(
                                ws,
                                renderFolder,
                                rowCards[i],
                                timelineContexts,
                                mediaLayoutByFile,
                                dpiScale,
                                prioritizeDecodes,
                                libraryWindow,
                                openSingleFileMetadataEditor,
                                updateDetailSelection,
                                refreshDetailSelectionUi,
                                redrawSelectedFolderDetail);
                            if (card == null) continue;
                            card.Margin = new Thickness(0, 0, i < rowCards.Count - 1 ? cardGap : 0, 0);
                            panel.Children.Add(card);
                        }
                        return panel;
                    }
                });
            }

            return rows;
        }

        LibrarySessionCardLayout BuildLibrarySessionCardLayout(
            LibraryDetailRenderGroup group,
            double cardWidth,
            int detailTileSize)
        {
            if (group == null) return null;
            var groupFiles = (group.Files ?? new List<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupFiles.Count == 0) return null;

            const double horizontalPadding = 16d;
            const double tileGap = 6d;
            const int previewColumns = 3;
            const int previewRowsWanted = 2;
            const int previewLimit = previewColumns * previewRowsWanted;
            const double bannerHeight = 142d;
            var previewFiles = groupFiles.Take(previewLimit).ToList();
            var innerWidth = Math.Max(240d, cardWidth - (horizontalPadding * 2d));
            var previewTileWidth = (int)Math.Floor((innerWidth - ((previewColumns - 1) * tileGap)) / previewColumns);
            previewTileWidth = Math.Max(82, previewTileWidth);
            var previewTileHeight = Math.Max(70, (int)Math.Round(previewTileWidth * 0.62d));
            var thumbnailHeight = (previewRowsWanted * previewTileHeight) + ((previewRowsWanted - 1) * tileGap);
            var cardHeight = bannerHeight + thumbnailHeight + 30d;

            return new LibrarySessionCardLayout
            {
                Group = group,
                Width = Math.Ceiling(cardWidth),
                Height = Math.Ceiling(cardHeight),
                PreviewColumns = previewColumns,
                PreviewRows = previewRowsWanted,
                PreviewTileWidth = previewTileWidth,
                PreviewTileHeight = previewTileHeight,
                PreviewFiles = previewFiles
            };
        }

        FrameworkElement BuildLibrarySessionCard(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            LibrarySessionCardLayout layout,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile,
            double dpiScale,
            bool prioritizeRowDecodes,
            Window libraryWindow,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail)
        {
            if (layout == null || layout.Group == null) return null;
            var group = layout.Group;
            var groupFiles = (group.Files ?? new List<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (groupFiles.Count == 0) return null;

            var title = !string.IsNullOrWhiteSpace(group.HeaderText)
                ? group.HeaderText
                : BuildLibrarySessionCardTitle(group.SessionEndDate, group.SessionStartDate, DateTime.Today);
            var subtitle = !string.IsNullOrWhiteSpace(group.SubtitleText)
                ? group.SubtitleText
                : BuildLibrarySessionCardSubtitle(groupFiles.Count, group.SessionEndDate, group.SessionStartDate);
            var gameText = BuildLibrarySessionDistinctContextLabel(groupFiles, timelineContexts, true);
            var platformText = BuildLibrarySessionDistinctContextLabel(groupFiles, timelineContexts, false);
            var remainingCount = Math.Max(0, groupFiles.Count - (layout.PreviewFiles == null ? 0 : layout.PreviewFiles.Count));

            var root = new Border
            {
                Width = layout.Width,
                MinHeight = layout.Height,
                Background = Brush("#10191F"),
                BorderBrush = Brush("#2E414D"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                ClipToBounds = true,
                Cursor = Cursors.Hand,
                ToolTip = "Open this session"
            };
            root.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!LibrarySessionCardClickShouldOpen(e.OriginalSource as DependencyObject, root)) return;
                OpenLibrarySessionWindow(
                    libraryWindow,
                    renderFolder,
                    group,
                    timelineContexts,
                    mediaLayoutByFile,
                    dpiScale,
                    openSingleFileMetadataEditor,
                    redrawSelectedFolderDetail);
                e.Handled = true;
            };

            var stack = new StackPanel();
            var bannerBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            bannerBrush.GradientStops.Add(new GradientStop(Color.FromRgb(26, 45, 58), 0));
            bannerBrush.GradientStops.Add(new GradientStop(Color.FromRgb(16, 28, 36), 0.56));
            bannerBrush.GradientStops.Add(new GradientStop(Color.FromRgb(12, 20, 26), 1));

            var banner = new Border
            {
                Height = 142,
                Background = bannerBrush,
                BorderBrush = Brush("#375061"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(17, 17, 0, 0),
                Padding = new Thickness(16, 13, 16, 13)
            };
            var bannerDock = new DockPanel { LastChildFill = true };
            var openButton = Btn("Open session", null, "#213A49", Brushes.White);
            openButton.Padding = new Thickness(12, 7, 12, 7);
            openButton.FontSize = 12.5;
            openButton.Margin = new Thickness(12, 0, 0, 0);
            ApplyLibraryPillChrome(openButton, "#213A49", "#3D5D6D", "#294858", "#172B35", "#F2FBFF");
            openButton.Click += delegate
            {
                OpenLibrarySessionWindow(
                    libraryWindow,
                    renderFolder,
                    group,
                    timelineContexts,
                    mediaLayoutByFile,
                    dpiScale,
                    openSingleFileMetadataEditor,
                    redrawSelectedFolderDetail);
            };
            DockPanel.SetDock(openButton, Dock.Right);
            bannerDock.Children.Add(openButton);

            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush("#F4FAFF"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Brush("#A8BAC5"),
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var metrics = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            metrics.Children.Add(BuildLibrarySessionMetricPill("Photos", groupFiles.Count.ToString()));
            metrics.Children.Add(BuildLibrarySessionAchievementMetricPill(groupFiles, group, timelineContexts));
            metrics.Children.Add(BuildLibrarySessionMetricPill("Games", gameText));
            metrics.Children.Add(BuildLibrarySessionMetricPill("Platforms", platformText));
            if (remainingCount > 0) metrics.Children.Add(BuildLibrarySessionMetricPill("More", "+" + remainingCount));
            titleStack.Children.Add(metrics);
            bannerDock.Children.Add(titleStack);
            banner.Child = bannerDock;
            stack.Children.Add(banner);

            var previewPanel = new Grid
            {
                Width = Math.Max(240d, layout.Width - 32d),
                Height = (layout.PreviewRows * layout.PreviewTileHeight) + (Math.Max(0, layout.PreviewRows - 1) * 6d),
                Margin = new Thickness(16, 14, 16, 16)
            };
            for (var col = 0; col < layout.PreviewColumns; col++)
                previewPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(layout.PreviewTileWidth + (col < layout.PreviewColumns - 1 ? 6d : 0d)) });
            for (var row = 0; row < layout.PreviewRows; row++)
                previewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(layout.PreviewTileHeight + (row < layout.PreviewRows - 1 ? 6d : 0d)) });
            var previewFiles = layout.PreviewFiles ?? new List<string>();
            for (var i = 0; i < previewFiles.Count; i++)
            {
                var file = previewFiles[i];
                var row = i / layout.PreviewColumns;
                var column = i % layout.PreviewColumns;
                LibraryTimelineCaptureContext timelineContext;
                if (timelineContexts == null || !timelineContexts.TryGetValue(file, out timelineContext)) timelineContext = null;
                var tile = CreateLibraryDetailTile(
                    file,
                    layout.PreviewTileWidth,
                    CalculateLibraryDetailTileDecodeWidth(layout.PreviewTileWidth, dpiScale),
                    delegate { return ws != null && SameLibraryBrowserSelection(ws.Current, renderFolder); },
                    openSingleFileMetadataEditor,
                    updateDetailSelection,
                    ws == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : ws.SelectedDetailFiles,
                    refreshDetailSelectionUi,
                    redrawSelectedFolderDetail,
                    null,
                    layout.PreviewTileHeight,
                    timelineContext,
                    prioritizeRowDecodes,
                    null,
                    path => OpenLibrarySessionCaptureViewer(libraryWindow, groupFiles, path),
                    false);
                tile.Margin = new Thickness(0, 0, column < layout.PreviewColumns - 1 ? 6 : 0, row < layout.PreviewRows - 1 ? 6 : 0);
                FrameworkElement previewChild = tile;
                if (i == previewFiles.Count - 1 && remainingCount > 0)
                {
                    var overlayGrid = new Grid
                    {
                        Width = layout.PreviewTileWidth,
                        Height = layout.PreviewTileHeight,
                        Margin = tile.Margin
                    };
                    tile.Margin = new Thickness(0);
                    overlayGrid.Children.Add(tile);
                    overlayGrid.Children.Add(new Border
                    {
                        IsHitTestVisible = false,
                        Background = Brush("#AA071017"),
                        CornerRadius = new CornerRadius(10),
                        Child = new TextBlock
                        {
                            Text = "+" + remainingCount,
                            Foreground = Brushes.White,
                            FontSize = 24,
                            FontWeight = FontWeights.Bold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                    previewChild = overlayGrid;
                }
                Grid.SetColumn(previewChild, column);
                Grid.SetRow(previewChild, row);
                previewPanel.Children.Add(previewChild);
            }
            stack.Children.Add(previewPanel);
            root.Child = stack;
            return root;
        }

        Border BuildLibrarySessionMetricPill(string label, string value)
        {
            TextBlock valueBlock;
            return BuildLibrarySessionMetricPill(label, value, out valueBlock);
        }

        Border BuildLibrarySessionMetricPill(string label, string value, out TextBlock valueBlock)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = label + ": ",
                Foreground = Brush("#8FA6B3"),
                FontSize = 11.5,
                FontWeight = FontWeights.Medium
            });
            valueBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "Unknown" : value,
                Foreground = Brush("#E9F4FA"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 130
            };
            stack.Children.Add(valueBlock);
            return new Border
            {
                Background = Brush("#8020333E"),
                BorderBrush = Brush("#355464"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Child = stack
            };
        }

        Border BuildLibrarySessionAchievementMetricPill(
            IReadOnlyList<string> groupFiles,
            LibraryDetailRenderGroup group,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts)
        {
            var targets = BuildLibrarySessionAchievementTargets(groupFiles, timelineContexts);
            var initialLabel = targets.Count == 0 ? "Not tracked" : "Loading...";
            TextBlock valueBlock;
            var pill = BuildLibrarySessionMetricPill("Achievements", initialLabel, out valueBlock);
            if (targets.Count == 0)
            {
                pill.ToolTip = "No Steam App ID or RetroAchievements game ID was found for this session.";
                return pill;
            }

            pill.ToolTip = "Checking tracked achievement sources for this session.";
            ScheduleLibrarySessionAchievementMetricUpdate(valueBlock, pill, group, targets);
            return pill;
        }

        static bool LibrarySessionCardClickShouldOpen(DependencyObject source, Border cardRoot)
        {
            for (var node = source; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (ReferenceEquals(node, cardRoot)) return true;
                if (node is System.Windows.Controls.Primitives.ButtonBase) return false;
                if (node is TextBox) return false;
                var fe = node as FrameworkElement;
                var tag = fe == null ? null : fe.Tag as string;
                if (!string.IsNullOrWhiteSpace(tag)) return false;
            }
            return true;
        }

        string BuildLibrarySessionDistinctContextLabel(
            IReadOnlyList<string> groupFiles,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            bool gameTitle)
        {
            var values = new List<string>();
            foreach (var file in groupFiles ?? new List<string>())
            {
                LibraryTimelineCaptureContext context;
                if (timelineContexts == null || !timelineContexts.TryGetValue(file, out context) || context == null) continue;
                var value = gameTitle
                    ? NormalizeGameIndexName(context.GameTitle ?? string.Empty)
                    : NormalizeConsoleLabel(context.PlatformLabel ?? string.Empty);
                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Other", StringComparison.OrdinalIgnoreCase)) continue;
                values.Add(value);
            }
            var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count == 0) return "Unknown";
            if (distinct.Count <= 2) return string.Join(", ", distinct);
            return distinct.Count + (gameTitle ? " games" : " platforms");
        }

        List<LibrarySessionAchievementTarget> BuildLibrarySessionAchievementTargets(
            IReadOnlyList<string> groupFiles,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts)
        {
            var targets = new Dictionary<string, LibrarySessionAchievementTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in groupFiles ?? new List<string>())
            {
                LibraryTimelineCaptureContext context;
                if (timelineContexts == null || !timelineContexts.TryGetValue(file, out context) || context == null) continue;
                var gameTitle = NormalizeGameIndexName(context.GameTitle ?? string.Empty);
                if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = "Unknown Game";
                var platform = NormalizeConsoleLabel(context.PlatformLabel ?? string.Empty);
                var steamAppId = CleanTag(context.SteamAppId ?? string.Empty);
                if (LibrarySessionAchievementIdLooksNumeric(steamAppId))
                {
                    var target = new LibrarySessionAchievementTarget
                    {
                        SourceLabel = "Steam",
                        GameTitle = gameTitle,
                        PlatformLabel = string.IsNullOrWhiteSpace(platform) || string.Equals(platform, "Other", StringComparison.OrdinalIgnoreCase) ? "Steam" : platform,
                        SteamAppId = steamAppId
                    };
                    targets[target.CacheIdentity] = target;
                    continue;
                }

                var retroAchievementsGameId = CleanTag(context.RetroAchievementsGameId ?? string.Empty);
                if (LibrarySessionAchievementIdLooksNumeric(retroAchievementsGameId))
                {
                    var target = new LibrarySessionAchievementTarget
                    {
                        SourceLabel = "RetroAchievements",
                        GameTitle = gameTitle,
                        PlatformLabel = string.IsNullOrWhiteSpace(platform) || string.Equals(platform, "Other", StringComparison.OrdinalIgnoreCase) ? "Emulation" : platform,
                        RetroAchievementsGameId = retroAchievementsGameId
                    };
                    targets[target.CacheIdentity] = target;
                }
            }
            return targets.Values
                .OrderBy(target => target.GameTitle ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.SourceLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static bool LibrarySessionAchievementIdLooksNumeric(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value.Any(char.IsDigit);
        }

        void ScheduleLibrarySessionAchievementMetricUpdate(
            TextBlock valueBlock,
            Border pill,
            LibraryDetailRenderGroup group,
            List<LibrarySessionAchievementTarget> targets)
        {
            if (valueBlock == null || group == null || targets == null || targets.Count == 0) return;
            _ = ResolveLibrarySessionAchievementMetricAsync(group, targets)
                .ContinueWith(task =>
                {
                    LibrarySessionAchievementMetricResult metric;
                    if (task.IsFaulted)
                    {
                        metric = new LibrarySessionAchievementMetricResult
                        {
                            Label = "Unavailable",
                            ToolTip = task.Exception == null ? "Achievement lookup failed." : task.Exception.GetBaseException().Message
                        };
                    }
                    else
                    {
                        metric = task.Result ?? new LibrarySessionAchievementMetricResult { Label = "Unavailable", ToolTip = "Achievement lookup returned no result." };
                    }

                    try
                    {
                        valueBlock.Dispatcher.BeginInvoke((Action)delegate
                        {
                            valueBlock.Text = string.IsNullOrWhiteSpace(metric.Label) ? "Unavailable" : metric.Label;
                            valueBlock.ToolTip = metric.ToolTip;
                            if (pill != null) pill.ToolTip = metric.ToolTip;
                        }, DispatcherPriority.Background);
                    }
                    catch
                    {
                    }
                }, TaskScheduler.Default);
        }

        async Task<LibrarySessionAchievementMetricResult> ResolveLibrarySessionAchievementMetricAsync(
            LibraryDetailRenderGroup group,
            List<LibrarySessionAchievementTarget> targets)
        {
            var fetchTasks = targets
                .Select(target => GetLibrarySessionAchievementFetchTask(target))
                .ToArray();
            var results = await Task.WhenAll(fetchTasks).ConfigureAwait(false);
            return BuildLibrarySessionAchievementMetricResult(group, targets, results);
        }

        Task<GameAchievementsFetchService.FetchResult> GetLibrarySessionAchievementFetchTask(LibrarySessionAchievementTarget target)
        {
            if (target == null)
                return Task.FromResult(new GameAchievementsFetchService.FetchResult { ErrorMessage = "Missing achievement target." });

            var steamKey = CurrentSteamWebApiKey();
            var steamUser = CurrentSteamUserId64();
            var retroKey = CurrentRetroAchievementsApiKey();
            var retroUser = CurrentRetroAchievementsUsername();
            var source = target.SourceLabel ?? string.Empty;
            var credentialKey = string.Equals(source, "Steam", StringComparison.OrdinalIgnoreCase)
                ? "|steamUser=" + (steamUser ?? string.Empty).Trim() + "|steamKey=" + ((steamKey ?? string.Empty).Trim().GetHashCode()).ToString()
                : "|raUser=" + (retroUser ?? string.Empty).Trim() + "|raKey=" + ((retroKey ?? string.Empty).Trim().GetHashCode()).ToString();
            var cacheKey = target.CacheIdentity + credentialKey;

            lock (_librarySessionAchievementCacheLock)
            {
                Task<GameAchievementsFetchService.FetchResult> cachedTask;
                if (_librarySessionAchievementFetchTasks.TryGetValue(cacheKey, out cachedTask)) return cachedTask;
                if (_librarySessionAchievementFetchTasks.Count > 128) _librarySessionAchievementFetchTasks.Clear();

                var folder = new LibraryFolderInfo
                {
                    Name = target.GameTitle ?? string.Empty,
                    PlatformLabel = target.PlatformLabel ?? string.Empty,
                    SteamAppId = target.SteamAppId ?? string.Empty,
                    RetroAchievementsGameId = target.RetroAchievementsGameId ?? string.Empty
                };
                var task = GameAchievementsFetchService.FetchAsync(
                    target.PlatformLabel ?? string.Empty,
                    folder,
                    steamKey,
                    retroKey,
                    steamUser,
                    retroUser,
                    "PixelVault/" + AppVersion,
                    CancellationToken.None);
                _librarySessionAchievementFetchTasks[cacheKey] = task;
                return task;
            }
        }

        LibrarySessionAchievementMetricResult BuildLibrarySessionAchievementMetricResult(
            LibraryDetailRenderGroup group,
            List<LibrarySessionAchievementTarget> targets,
            GameAchievementsFetchService.FetchResult[] results)
        {
            if (targets == null || targets.Count == 0)
                return new LibrarySessionAchievementMetricResult { Label = "Not tracked", ToolTip = "No tracked achievement source was found for this session." };

            var startLocal = group.SessionStartDate;
            var endLocal = group.SessionEndDate;
            if (startLocal <= DateTime.MinValue || endLocal <= DateTime.MinValue)
            {
                startLocal = group.CaptureDate;
                endLocal = group.CaptureDate;
            }
            if (startLocal > endLocal)
            {
                var swap = startLocal;
                startLocal = endLocal;
                endLocal = swap;
            }
            var startUtcTicks = startLocal <= DateTime.MinValue ? 0L : startLocal.ToUniversalTime().Ticks;
            var endUtcTicks = endLocal <= DateTime.MinValue ? long.MaxValue : endLocal.ToUniversalTime().Ticks;
            if (startUtcTicks > 0 && endUtcTicks < long.MaxValue && startUtcTicks == endUtcTicks)
            {
                startUtcTicks = startLocal.AddMinutes(-5).ToUniversalTime().Ticks;
                endUtcTicks = endLocal.AddMinutes(5).ToUniversalTime().Ticks;
            }

            var earnedInSession = 0;
            var unlockedTotal = 0;
            var progressKnownGames = 0;
            var progressUnknownGames = 0;
            var unavailableGames = 0;
            var totalAchievements = 0;
            var tooltipLines = new List<string>();
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                var result = results != null && i < results.Length ? results[i] : null;
                if (result == null || result.IsError || result.Rows == null)
                {
                    unavailableGames++;
                    tooltipLines.Add((target.GameTitle ?? target.SourceLabel ?? "Game") + ": unavailable" + (result == null || string.IsNullOrWhiteSpace(result.ErrorMessage) ? string.Empty : " (" + result.ErrorMessage + ")"));
                    continue;
                }

                var rows = result.Rows ?? new List<GameAchievementsFetchService.AchievementRow>();
                totalAchievements += rows.Count;
                var progressKnown = rows.Any(row => row != null && row.ProgressKnown);
                if (!progressKnown)
                {
                    progressUnknownGames++;
                    tooltipLines.Add((result.GameTitle ?? target.GameTitle ?? "Game") + ": achievement list found, unlock progress unknown");
                    continue;
                }

                progressKnownGames++;
                var gameUnlocked = rows.Count(row => row != null && row.ProgressKnown && row.Unlocked);
                var gameEarnedInSession = rows.Count(row =>
                    row != null
                    && row.ProgressKnown
                    && row.Unlocked
                    && row.UnlockUtcTicks > 0
                    && row.UnlockUtcTicks >= startUtcTicks
                    && row.UnlockUtcTicks <= endUtcTicks);
                unlockedTotal += gameUnlocked;
                earnedInSession += gameEarnedInSession;
                tooltipLines.Add((result.GameTitle ?? target.GameTitle ?? "Game") + ": " + gameEarnedInSession + " earned in session, " + gameUnlocked + " unlocked total");
            }

            string label;
            if (progressKnownGames > 0)
            {
                label = earnedInSession + " earned";
                if (targets.Count > 1) label += " (" + progressKnownGames + "/" + targets.Count + ")";
            }
            else if (progressUnknownGames > 0)
            {
                label = "Progress unknown";
            }
            else
            {
                label = "Unavailable";
            }

            var toolTip = new List<string>
            {
                "Tracked achievement sources: " + targets.Count,
                "Session window: " + (startLocal <= DateTime.MinValue ? "Unknown" : startLocal.ToString("g")) + " - " + (endLocal <= DateTime.MinValue ? "Unknown" : endLocal.ToString("g"))
            };
            if (progressKnownGames > 0)
                toolTip.Add("Unlocked total across tracked games: " + unlockedTotal + " of " + totalAchievements);
            if (progressUnknownGames > 0)
                toolTip.Add(progressUnknownGames + " tracked game" + (progressUnknownGames == 1 ? string.Empty : "s") + " need unlock progress credentials/privacy.");
            if (unavailableGames > 0)
                toolTip.Add(unavailableGames + " tracked source" + (unavailableGames == 1 ? string.Empty : "s") + " could not be loaded.");
            toolTip.AddRange(tooltipLines.Take(8));
            return new LibrarySessionAchievementMetricResult
            {
                Label = label,
                ToolTip = string.Join(Environment.NewLine, toolTip.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray())
            };
        }

        void OpenLibrarySessionWindow(
            Window ownerWindow,
            LibraryBrowserFolderView renderFolder,
            LibraryDetailRenderGroup group,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            IReadOnlyDictionary<string, LibraryDetailMediaLayoutInfo> mediaLayoutByFile,
            double dpiScale,
            Action<string> openSingleFileMetadataEditor,
            Action redrawSelectedFolderDetail)
        {
            if (group == null) return;
            var sessionFiles = (group.Files ?? new List<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sessionFiles.Count == 0) return;

            var title = !string.IsNullOrWhiteSpace(group.HeaderText)
                ? group.HeaderText
                : BuildLibrarySessionCardTitle(group.SessionEndDate, group.SessionStartDate, DateTime.Today);
            var subtitle = !string.IsNullOrWhiteSpace(group.SubtitleText)
                ? group.SubtitleText
                : BuildLibrarySessionCardSubtitle(sessionFiles.Count, group.SessionEndDate, group.SessionStartDate);
            var gameText = BuildLibrarySessionDistinctContextLabel(sessionFiles, timelineContexts, true);
            var platformText = BuildLibrarySessionDistinctContextLabel(sessionFiles, timelineContexts, false);

            var workArea = SystemParameters.WorkArea;
            var initialWidth = Math.Min(workArea.Width - 96d, Math.Max(900d, ownerWindow == null ? 1120d : ownerWindow.ActualWidth * 0.88d));
            var initialHeight = Math.Min(workArea.Height - 96d, Math.Max(620d, ownerWindow == null ? 780d : ownerWindow.ActualHeight * 0.86d));
            var window = new Window
            {
                Title = title + " - PixelVault Session",
                Width = initialWidth,
                Height = initialHeight,
                MinWidth = 720,
                MinHeight = 520,
                Background = Brush("#0F151A"),
                WindowStartupLocation = ownerWindow == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Owner = ownerWindow,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = true
            };

            var root = new DockPanel { LastChildFill = true };
            var header = new Border
            {
                Background = Brush("#121F28"),
                BorderBrush = Brush("#2E414D"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 18, 22, 16)
            };
            DockPanel.SetDock(header, Dock.Top);
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Brush("#AFC0CA"),
                FontSize = 13.5,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            var metricRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            metricRow.Children.Add(BuildLibrarySessionMetricPill("Photos", sessionFiles.Count.ToString()));
            metricRow.Children.Add(BuildLibrarySessionAchievementMetricPill(sessionFiles, group, timelineContexts));
            metricRow.Children.Add(BuildLibrarySessionMetricPill("Games", gameText));
            metricRow.Children.Add(BuildLibrarySessionMetricPill("Platforms", platformText));
            headerStack.Children.Add(metricRow);
            header.Child = headerStack;
            root.Children.Add(header);

            var host = CreateVirtualizedRowHost(new Thickness(18, 16, 18, 18), Brush("#0F151A"));
            host.DiagnosticName = "SessionWindowRows";
            host.RecycleVisibleRowElements = true;
            root.Children.Add(host.ScrollViewer);
            window.Content = root;

            var sessionWs = new LibraryBrowserWorkingSet { Current = renderFolder };
            sessionWs.DetailFilesDisplayOrder.AddRange(sessionFiles.Where(file => IsImage(file)));
            Func<List<string>> visibleSessionFiles = delegate
            {
                return sessionFiles.Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file)).ToList();
            };
            Action refreshSessionSelectionUi = null;
            refreshSessionSelectionUi = delegate
            {
                foreach (var tile in sessionWs.DetailTiles)
                {
                    var file = tile == null ? string.Empty : tile.Tag as string;
                    var selected = !string.IsNullOrWhiteSpace(file) && sessionWs.SelectedDetailFiles.Contains(file);
                    tile.Background = selected ? Brush("#1D2730") : Brush("#10181D");
                    tile.BorderBrush = selected ? Brush("#D46C63") : Brush("#2B3A44");
                    tile.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
                }
            };
            Action<string, ModifierKeys> updateSessionSelection = delegate(string file, ModifierKeys mods)
            {
                LibraryBrowserApplyDetailSelectionChange(sessionWs, file, mods, visibleSessionFiles, refreshSessionSelectionUi);
            };
            host.BeforeVisibleRowsRebuilt = delegate
            {
                sessionWs.DetailTiles.Clear();
            };
            host.AfterVisibleRowsRebuilt = delegate
            {
                RepopulateLibraryDetailTilesFromVisibleRows(sessionWs, host);
                refreshSessionSelectionUi();
            };

            Action rebuildRows = delegate
            {
                var viewport = host.ScrollViewer == null ? 0d : host.ScrollViewer.ActualWidth;
                if (viewport <= 0d) viewport = window.ActualWidth - 42d;
                if (viewport <= 0d) viewport = initialWidth - 42d;
                viewport = Math.Max(420d, viewport - 8d);
                var tileSize = Math.Max(180, Math.Min(260, (int)Math.Round(viewport / 4.4d)));
                var rows = BuildLibraryContinuousMosaicRowDefinitions(
                    sessionWs,
                    renderFolder,
                    new[] { group },
                    timelineContexts,
                    mediaLayoutByFile,
                    host.ScrollViewer,
                    viewport,
                    tileSize,
                    dpiScale,
                    true,
                    openSingleFileMetadataEditor,
                    updateSessionSelection,
                    refreshSessionSelectionUi,
                    redrawSelectedFolderDetail,
                    null,
                    path => OpenLibrarySessionCaptureViewer(window, sessionFiles, path));
                SetVirtualizedRows(host, rows, true, null);
            };

            DispatcherTimer resizeTimer = null;
            resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            resizeTimer.Tick += delegate
            {
                resizeTimer.Stop();
                rebuildRows();
            };
            window.Loaded += delegate
            {
                rebuildRows();
            };
            window.SizeChanged += delegate
            {
                resizeTimer.Stop();
                resizeTimer.Start();
            };
            window.Closing += delegate
            {
                resizeTimer.Stop();
                sessionWs.DetailTiles.Clear();
            };
            window.Show();
            window.Activate();
        }

        void OpenLibrarySessionCaptureViewer(Window ownerWindow, IList<string> sessionFiles, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || !IsImage(filePath)) return;
            var paths = (sessionFiles ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsImage(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count == 0) paths.Add(filePath);
            if (!paths.Any(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase)))
                paths.Insert(0, filePath);
            var idx = paths.FindIndex(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            var viewer = new LibraryCaptureViewerWindow(this, ownerWindow, paths, idx);
            viewer.Show();
            viewer.Activate();
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
                var capturedRowFiles = rowCards
                    .Where(card => card != null && card.Group != null)
                    .SelectMany(card => card.Group.Files ?? new List<string>())
                    .Where(file => !string.IsNullOrWhiteSpace(file))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                rowDefinitions.Add(new VirtualizedRowDefinition
                {
                    Height = rowVirtualHeight,
                    Files = capturedRowFiles,
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
            var labelText = !string.IsNullOrWhiteSpace(group.HeaderText)
                ? group.HeaderText
                : (group.CaptureDate <= DateTime.MinValue
                    ? string.Empty
                    : (group.CaptureDate.Year == DateTime.Today.Year
                        ? group.CaptureDate.ToString("ddd, MMM d")
                        : group.CaptureDate.ToString("ddd, MMM d, yyyy")));
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
                if (!string.IsNullOrWhiteSpace(group.SubtitleText))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = group.SubtitleText,
                        Foreground = Brush(DesignTokens.TextLabelMuted),
                        FontSize = 11.5,
                        FontWeight = FontWeights.Medium,
                        Margin = new Thickness(2, -6, 0, timelineView ? 10 : 6)
                    });
                }
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

        FrameworkElement BuildLibraryDetailEmptyCapturesPlaceholder(bool timelineView, bool sessionView, DateTime rangeStart, DateTime rangeEnd, Action redrawDetail)
        {
            var root = new StackPanel { Margin = new Thickness(8, 12, 12, 16), MaxWidth = 480 };
            var title = sessionView ? "No captures to group into sessions" : (timelineView ? "No captures in this range" : "No captures in this folder");
            var body = sessionView
                ? "No captures match the current search/filter set. Clear search or switch grouping to browse folders."
                : (timelineView
                ? "Nothing falls between " + (rangeStart > DateTime.MinValue ? rangeStart.ToString("yyyy-MM-dd") : "start") + " and " + (rangeEnd > DateTime.MinValue ? rangeEnd.ToString("yyyy-MM-dd") : "end") + ". Widen the range or switch grouping."
                : "This game folder has no screenshots or clips yet. Import captures or pick another folder.");
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
