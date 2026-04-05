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
                SetVirtualizedRows(panes.DetailRows, new List<VirtualizedRowDefinition>(), true, null);
                if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=(none); rows=0; files=0", 40);
                return;
            }
            const int detailTileGap = 8;
            var detailLayout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll);
            var size = detailLayout.TileSize;
            ws.LastDetailViewportWidth = ResolveScrollViewerLayoutWidth(panes == null ? null : panes.ThumbScroll);
            var shouldRestoreDetailScroll = ws.PreserveDetailScrollOnNextRender && ws.PreservedDetailScrollOffset > 0.1d;
            var restoreDetailScrollOffset = shouldRestoreDetailScroll ? (double?)ws.PreservedDetailScrollOffset : null;
            var restoreDetailScrollPending = shouldRestoreDetailScroll;
            ws.PreserveDetailScrollOnNextRender = false;
            var resetRowsToLoading = ws.ResetDetailRowsToLoadingOnNextRender;
            ws.ResetDetailRowsToLoadingOnNextRender = false;
            var renderFolder = ws.Current;
            var timelineView = IsLibraryBrowserTimelineView(renderFolder);
            var detailViewportWidth = ws.LastDetailViewportWidth;
            int ResolveAdaptiveDetailMaxTileSize(double viewportWidth)
            {
                var width = viewportWidth <= 0d ? 1280d : viewportWidth;
                if (width >= 1900d) return 336;
                if (width >= 1500d) return 312;
                if (width >= 1100d) return 288;
                if (width >= 840d) return 264;
                return 232;
            }
            var adaptiveMaxTileSize = ResolveAdaptiveDetailMaxTileSize(detailViewportWidth);
            var effectiveTileSize = Math.Min(size, adaptiveMaxTileSize);
            var targetDetailColumns = Math.Max(1, CalculateVirtualizedTileColumns(panes == null ? null : panes.ThumbScroll, effectiveTileSize, detailTileGap, 6));
            ws.LastDetailColumns = targetDetailColumns;
            ws.LastDetailTileSize = effectiveTileSize;
            ws.EstimatedDetailRowHeight = EstimateLibraryVariableDetailRowHeight(
                new List<(string File, int Width)> { (string.Empty, effectiveTileSize) },
                timelineView);
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
                        Height = 44,
                        Build = delegate
                        {
                            return new TextBlock { Text = "Loading captures...", Foreground = Brush("#A7B5BD") };
                        }
                    }
                }, true, null);
                LogTroubleshooting("LibraryDetailRenderLoadingState",
                    "renderVersion=" + renderVersion
                    + "; reason=" + (resetRowsToLoading ? "selection-change" : "empty-pane"));
            }
            if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
            Task.Run(async delegate
            {
                Action<string, string> traceStep = delegate(string area, string details)
                {
                    LogTroubleshooting(area,
                        "renderVersion=" + renderVersion
                        + "; elapsedMs=" + renderStopwatch.ElapsedMilliseconds
                        + "; " + (details ?? string.Empty)
                        + (string.IsNullOrWhiteSpace(details) ? string.Empty : "; ")
                        + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                };
                Action<LibraryDetailRenderSnapshot, bool> applyDetailSnapshot = null;
                applyDetailSnapshot = delegate(LibraryDetailRenderSnapshot snapshot, bool logCompletion)
                {
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
                                Height = 44,
                                Build = delegate
                                {
                                    return new TextBlock { Text = timelineView ? "No captures found in the selected timeline range." : "No captures found in this folder.", Foreground = Brush("#A7B5BD") };
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
                    var dpiScale = ResolveLibraryDpiScale(panes?.ThumbScroll);
                    var packAvailableW = Math.Max((double)effectiveTileSize, detailViewportWidth - 6d);
                    var packMinW = Math.Max(140, (int)Math.Floor(effectiveTileSize * 0.62));
                    var virtualRows = new List<VirtualizedRowDefinition>();

                    virtualRows = BuildLibraryPackedDayCardRowDefinitions(
                        ws,
                        renderFolder,
                        snapshot.Groups,
                        timelineContexts,
                        mediaLayoutByFile,
                        detailViewportWidth,
                        effectiveTileSize,
                        dpiScale,
                        timelineView,
                        openSingleFileMetadataEditor,
                        updateDetailSelection,
                        refreshDetailSelectionUi,
                        redrawSelectedFolderDetail,
                        renderFolderTiles);
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
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; groups=" + snapshot.Groups.Count + "; files=" + visibleFiles.Count + "; rows=" + virtualRows.Count + "; columns=" + detailColumns + "; size=" + effectiveTileSize, 40);
                        LogLibraryBrowserFirstDetailPaintOnce("folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; files=" + visibleFiles.Count + "; groups=" + snapshot.Groups.Count);
                    }
                };

                try
                {
                    traceStep("LibraryDetailBackgroundStart", "thread=" + Environment.CurrentManagedThreadId);
                    var metadataIndex = librarySession.LoadLibraryMetadataIndex(false);
                    traceStep("LibraryDetailMetadataIndexLoaded", "entries=" + metadataIndex.Count);
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

                    Func<Dictionary<string, EmbeddedMetadataSnapshot>, LibraryDetailRenderSnapshot> buildSnapshot = delegate(Dictionary<string, EmbeddedMetadataSnapshot> timelineMetadataSnapshots)
                    {
                        var datedFiles = detailFiles
                            .Select(file => new { FilePath = file, CaptureDate = librarySession.ResolveIndexedLibraryDate(file, metadataIndex) })
                            .Where(entry => !timelineView || LibraryTimelineRangeContainsCapture(entry.CaptureDate, timelineRangeStart, timelineRangeEnd))
                            .OrderByDescending(entry => entry.CaptureDate)
                            .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var snapshot = new LibraryDetailRenderSnapshot
                        {
                            VisibleFiles = datedFiles.Select(entry => entry.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        };
                        snapshot.MediaLayoutByFile = BuildLibraryDetailMediaLayoutInfoMap(snapshot.VisibleFiles);
                        if (timelineView)
                        {
                            snapshot.TimelineContextByFile = BuildLibraryTimelineCaptureContextMap(snapshot.VisibleFiles, metadataIndex, savedGameRows, timelineMetadataSnapshots);
                        }
                        foreach (var group in datedFiles
                            .GroupBy(entry => entry.CaptureDate.Date)
                            .OrderByDescending(group => group.Key))
                        {
                            snapshot.Groups.Add(new LibraryDetailRenderGroup
                            {
                                CaptureDate = group.Key,
                                Files = group.Select(entry => entry.FilePath).ToList()
                            });
                        }
                        return snapshot;
                    };

                    traceStep("LibraryDetailQuickSnapshotBuildStart", "files=" + detailFiles.Count);
                    var quickSnapshot = buildSnapshot(null);
                    traceStep("LibraryDetailQuickSnapshotBuilt",
                        "groups=" + quickSnapshot.Groups.Count
                        + "; visibleFiles=" + quickSnapshot.VisibleFiles.Count);
                    traceStep("LibraryDetailQuickSnapshotDispatchStart", "stage=initial");
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(quickSnapshot, true); }));
                    traceStep("LibraryDetailQuickSnapshotDispatchComplete", "stage=initial");
                    Dictionary<string, EmbeddedMetadataSnapshot> timelineMetadataSnapshots = null;
                    if (timelineView && quickSnapshot.VisibleFiles.Count > 0)
                    {
                        try
                        {
                            traceStep("LibraryDetailTimelineMetadataReadStart", "files=" + quickSnapshot.VisibleFiles.Count);
                            timelineMetadataSnapshots = await metadataService.ReadEmbeddedMetadataBatchAsync(quickSnapshot.VisibleFiles, CancellationToken.None).ConfigureAwait(false);
                            traceStep("LibraryDetailTimelineMetadataReadComplete", "metadataResults=" + timelineMetadataSnapshots.Count);
                            var commentSnapshot = buildSnapshot(timelineMetadataSnapshots);
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
                            if (commentsChanged)
                            {
                                traceStep("LibraryDetailTimelineMetadataDispatchStart", "stage=comment-refresh");
                                await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(commentSnapshot, false); }));
                                traceStep("LibraryDetailTimelineMetadataDispatchComplete", "stage=comment-refresh");
                                quickSnapshot = commentSnapshot;
                            }
                        }
                        catch (Exception timelineMetadataEx)
                        {
                            LogException("Library detail timeline metadata read | " + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)"), timelineMetadataEx);
                            LogTroubleshooting("LibraryDetailTimelineMetadataReadFail",
                                "renderVersion=" + renderVersion
                                + "; type=" + timelineMetadataEx.GetType().FullName
                                + "; message=" + timelineMetadataEx.Message
                                + "; exception=" + FormatExceptionForTroubleshooting(timelineMetadataEx)
                                + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        }
                    }

                    if (filesMissingCaptureTicks.Count > 0)
                    {
                        LogTroubleshooting("LibraryDetailMetadataRepairStart",
                            "renderVersion=" + renderVersion
                            + "; files=" + filesMissingCaptureTicks.Count
                            + "; " + BuildLibraryBrowserTroubleshootingLabel(renderFolder));
                        try
                        {
                            if (savedGameRows == null) savedGameRows = librarySession.LoadSavedGameIndexRows();
                            traceStep("LibraryDetailMetadataRepairRowsLoaded", "savedGameRows=" + savedGameRows.Count);
                            var metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(filesMissingCaptureTicks, CancellationToken.None).ConfigureAwait(false);
                            traceStep("LibraryDetailMetadataRepairBatchRead", "metadataResults=" + metadataByFile.Count);
                            var indexChanged = false;
                            var gameRowsChanged = false;
                            foreach (var file in filesMissingCaptureTicks)
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
                            if (indexChanged) librarySession.SaveLibraryMetadataIndex(metadataIndex);
                            LogTroubleshooting("LibraryDetailMetadataRepairComplete",
                                "renderVersion=" + renderVersion
                                + "; files=" + filesMissingCaptureTicks.Count
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
                        var refinedSnapshot = buildSnapshot(timelineMetadataSnapshots);
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
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(refinedSnapshot, false); }));
                            traceStep("LibraryDetailRefinedSnapshotDispatchComplete", "stage=refined");
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
            var cardGap = timelineView ? 18d : 14d;
            var safeGroups = (groups ?? new List<LibraryDetailRenderGroup>())
                .Where(group => group != null && (group.Files ?? new List<string>()).Count > 0)
                .ToList();
            if (safeGroups.Count == 0) return new List<VirtualizedRowDefinition>();

            var availableWidth = viewportWidth <= 0d ? 1100d : Math.Max(320d, viewportWidth - 6d);
            var desiredWidths = safeGroups
                .Select(group => EstimateLibraryPackedDayCardDesiredWidth((group.Files ?? new List<string>()).Count, availableWidth, timelineView))
                .ToList();
            var packedRows = BuildLibraryTimelinePackedRows(desiredWidths, availableWidth, cardGap);
            var rowDefinitions = new List<VirtualizedRowDefinition>();
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
                    .DefaultIfEmpty(ws == null ? 420d : ws.EstimatedDetailRowHeight)
                    .Max();
                rowDefinitions.Add(new VirtualizedRowDefinition
                {
                    Height = Math.Max(ws == null ? 420 : ws.EstimatedDetailRowHeight, (int)Math.Ceiling(estimatedHeight + cardGap)),
                    Build = delegate
                    {
                        var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, cardGap) };
                        for (var i = 0; i < rowCards.Count; i++)
                        {
                            var card = BuildLibraryPackedDayCard(
                                ws,
                                renderFolder,
                                rowCards[i],
                                timelineContexts,
                                dpiScale,
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
            const double packedTileSizeScale = 1.25d;
            var innerPackWidth = Math.Max(240d, cardWidth);
            var targetTileWidth = timelineView
                ? (int)Math.Round((innerPackWidth >= 1280d ? 620 : (innerPackWidth >= 980d ? 520 : (innerPackWidth >= 760d ? 440 : 360))) * packedTileSizeScale)
                : (int)Math.Round((innerPackWidth >= 980d ? 500 : (innerPackWidth >= 760d ? 420 : 330)) * packedTileSizeScale);
            var minTileWidth = timelineView
                ? Math.Max((int)Math.Round(360 * packedTileSizeScale), Math.Min(targetTileWidth, (int)Math.Floor(innerPackWidth * 0.5d)))
                : Math.Max((int)Math.Round(280 * packedTileSizeScale), Math.Min(targetTileWidth, (int)Math.Floor(innerPackWidth * 0.44d)));
            var maxTileWidth = groupFiles.Count == 1
                ? (int)Math.Floor(innerPackWidth)
                : Math.Min((int)Math.Floor(innerPackWidth), targetTileWidth + (int)Math.Round((timelineView ? 180 : 120) * packedTileSizeScale));
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
            var headerHeight = group.CaptureDate <= DateTime.MinValue ? 0d : 24d;
            var chunkHeights = chunks.Sum(chunk => chunk == null ? 0d : chunk.CanvasHeight);
            var chunkGaps = Math.Max(0, chunks.Count - 1) * masonryTileGap;
            return new LibraryPackedDayCardLayout
            {
                Group = group,
                Width = Math.Max(timelineView ? 520d * 1.75d : 360d * 1.75d, Math.Ceiling(cardWidth)),
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
            var stack = new StackPanel();
            if (!string.IsNullOrWhiteSpace(labelText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = labelText,
                    Foreground = Brush("#8FA1AD"),
                    FontSize = timelineView ? 12.5 : 11.5,
                    FontWeight = FontWeights.Medium,
                    Margin = new Thickness(2, 0, 0, 6)
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
                        timelineView ? timelineContext : null);
                    Canvas.SetLeft(tile, placement.X);
                    Canvas.SetTop(tile, placement.Y);
                    if (ws != null) ws.DetailTiles.Add(tile);
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
    }
}
