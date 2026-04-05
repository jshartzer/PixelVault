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
            const int masonryTileGap = 3;
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
                        SetVirtualizedRows(panes.DetailRows, new[]
                        {
                            new VirtualizedRowDefinition
                            {
                                Height = 44,
                                Build = delegate
                                {
                                    return new TextBlock { Text = timelineView ? "No captures found in the selected timeline range." : "No captures found in this folder.", Foreground = Brush("#A7B5BD") };
                                }
                            }
                        }, !restoreDetailScrollPending, restoreDetailScrollPending ? restoreDetailScrollOffset : null);
                        restoreDetailScrollPending = false;
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

                    if (timelineView)
                    {
                        foreach (var group in snapshot.Groups)
                        {
                            var groupDate = group.CaptureDate;
                            var groupFiles = group.Files ?? new List<string>();
                            var relativeTitle = BuildLibraryTimelineDayCardTitle(groupDate, DateTime.Today);
                            var absoluteTitle = groupDate <= DateTime.MinValue ? string.Empty : groupDate.ToString("MMMM d, yyyy");
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = string.IsNullOrWhiteSpace(absoluteTitle) || string.Equals(relativeTitle, absoluteTitle, StringComparison.Ordinal) ? 34 : 52,
                                Build = delegate
                                {
                                    var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                                    headerStack.Children.Add(new TextBlock
                                    {
                                        Text = string.IsNullOrWhiteSpace(relativeTitle) ? absoluteTitle : relativeTitle,
                                        FontSize = 16,
                                        FontWeight = FontWeights.SemiBold,
                                        Foreground = Brush("#F1E9DA")
                                    });
                                    if (!string.IsNullOrWhiteSpace(absoluteTitle) && !string.Equals(relativeTitle, absoluteTitle, StringComparison.Ordinal))
                                    {
                                        headerStack.Children.Add(new TextBlock
                                        {
                                            Text = absoluteTitle,
                                            FontSize = 12,
                                            Foreground = Brush("#8FA1AD"),
                                            Margin = new Thickness(0, 2, 0, 0)
                                        });
                                    }
                                    return headerStack;
                                }
                            });
                            var timelineMasonryChunks = BuildLibraryDetailMasonryChunks(
                                groupFiles,
                                packAvailableW,
                                masonryTileGap,
                                effectiveTileSize,
                                packMinW,
                                adaptiveMaxTileSize,
                                true);
                            foreach (var chunk in timelineMasonryChunks)
                            {
                                var chunkHeight = chunk.CanvasHeight + masonryTileGap;
                                var chunkCopy = chunk;
                                virtualRows.Add(new VirtualizedRowDefinition
                                {
                                    Height = chunkHeight,
                                    Build = delegate
                                    {
                                        var canvas = new Canvas
                                        {
                                            Width = chunkCopy.CanvasWidth,
                                            Height = chunkCopy.CanvasHeight,
                                            Margin = new Thickness(0, 0, 0, masonryTileGap)
                                        };
                                        foreach (var pl in chunkCopy.Placements)
                                        {
                                            var decodeW = CalculateLibraryDetailTileDecodeWidth(pl.Width, dpiScale);
                                            LibraryTimelineCaptureContext timelineContext;
                                            if (!timelineContexts.TryGetValue(pl.File, out timelineContext)) timelineContext = null;
                                            var tile = CreateLibraryDetailTile(
                                                pl.File,
                                                pl.Width,
                                                decodeW,
                                                delegate { return SameLibraryBrowserSelection(ws.Current, renderFolder); },
                                                openSingleFileMetadataEditor,
                                                updateDetailSelection,
                                                ws.SelectedDetailFiles,
                                                refreshDetailSelectionUi,
                                                redrawSelectedFolderDetail,
                                                null,
                                                pl.Height,
                                                timelineContext);
                                            Canvas.SetLeft(tile, pl.X);
                                            Canvas.SetTop(tile, pl.Y);
                                            ws.DetailTiles.Add(tile);
                                            canvas.Children.Add(tile);
                                        }
                                        return canvas;
                                    }
                                });
                            }
                        }
                    }
                    else
                    {
                        foreach (var group in snapshot.Groups)
                        {
                            var groupDate = group.CaptureDate;
                            var groupFiles = group.Files ?? new List<string>();
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = 34,
                                Build = delegate
                                {
                                    return new TextBlock
                                    {
                                        Text = groupDate.ToString("MMMM d, yyyy"),
                                        FontSize = 16,
                                        FontWeight = FontWeights.SemiBold,
                                        Foreground = Brush("#F1E9DA"),
                                        Margin = new Thickness(0, 0, 0, 10)
                                    };
                                }
                            });
                            var normalMasonryChunks = BuildLibraryDetailMasonryChunks(
                                groupFiles,
                                packAvailableW,
                                masonryTileGap,
                                effectiveTileSize,
                                packMinW,
                                adaptiveMaxTileSize,
                                false);
                            foreach (var chunk in normalMasonryChunks)
                            {
                                var chunkHeight = chunk.CanvasHeight + masonryTileGap;
                                var chunkCopy = chunk;
                                virtualRows.Add(new VirtualizedRowDefinition
                                {
                                    Height = chunkHeight,
                                    Build = delegate
                                    {
                                        var canvas = new Canvas
                                        {
                                            Width = chunkCopy.CanvasWidth,
                                            Height = chunkCopy.CanvasHeight,
                                            Margin = new Thickness(0, 0, 0, masonryTileGap)
                                        };
                                        foreach (var pl in chunkCopy.Placements)
                                        {
                                            var decodeW = CalculateLibraryDetailTileDecodeWidth(pl.Width, dpiScale);
                                            Action<string> useFileAsFolderCover = delegate(string imagePath)
                                            {
                                                var folder = activeSelectedLibraryFolder;
                                                if (folder == null || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) || !IsImage(imagePath)) return;
                                                SaveCustomCover(folder, imagePath);
                                                if (renderFolderTiles != null) renderFolderTiles();
                                                redrawSelectedFolderDetail?.Invoke();
                                                ShowLibraryBrowserToast(ws, "Cover saved");
                                            };
                                            var tile = CreateLibraryDetailTile(
                                                pl.File,
                                                pl.Width,
                                                decodeW,
                                                delegate { return SameLibraryBrowserSelection(ws.Current, renderFolder); },
                                                openSingleFileMetadataEditor,
                                                updateDetailSelection,
                                                ws.SelectedDetailFiles,
                                                refreshDetailSelectionUi,
                                                redrawSelectedFolderDetail,
                                                useFileAsFolderCover,
                                                pl.Height,
                                                null);
                                            Canvas.SetLeft(tile, pl.X);
                                            Canvas.SetTop(tile, pl.Y);
                                            ws.DetailTiles.Add(tile);
                                            canvas.Children.Add(tile);
                                        }
                                        return canvas;
                                    }
                                });
                            }
                        }
                    }
                    SetVirtualizedRows(panes.DetailRows, virtualRows, !restoreDetailScrollPending, restoreDetailScrollPending ? restoreDetailScrollOffset : null);
                    restoreDetailScrollPending = false;
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

        List<VirtualizedRowDefinition> BuildLibraryTimelinePackedRowDefinitions(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            IList<LibraryDetailRenderGroup> groups,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            double viewportWidth,
            int timelineTileSize,
            double dpiScale,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail)
        {
            const double cardGap = 12d;
            var safeGroups = (groups ?? new List<LibraryDetailRenderGroup>())
                .Where(group => group != null && (group.Files ?? new List<string>()).Count > 0)
                .ToList();
            if (safeGroups.Count == 0) return new List<VirtualizedRowDefinition>();

            var availableWidth = viewportWidth <= 0d ? 1100d : Math.Max(320d, viewportWidth - 6d);
            var cardColumnCounts = safeGroups
                .Select(group => CalculateLibraryTimelinePackedCardColumns((group.Files ?? new List<string>()).Count, availableWidth))
                .ToList();
            var cardWidths = safeGroups
                .Select((group, index) => EstimateLibraryTimelinePackedCardWidth(
                    (group.Files ?? new List<string>()).Count,
                    timelineTileSize,
                    availableWidth,
                    cardColumnCounts[index]))
                .ToList();
            var packedRows = BuildLibraryTimelinePackedRows(cardWidths, availableWidth, cardGap);
            var rowDefinitions = new List<VirtualizedRowDefinition>();
            foreach (var row in packedRows)
            {
                var rowIndexes = row == null ? new List<int>() : row.ToList();
                if (rowIndexes.Count == 0) continue;
                var estimatedHeight = rowIndexes
                    .Select(index => EstimateLibraryTimelinePackedCardHeight(
                        (safeGroups[index].Files ?? new List<string>()).Count,
                        timelineTileSize,
                        cardColumnCounts[index]))
                    .DefaultIfEmpty(ws == null ? 360d : ws.EstimatedDetailRowHeight)
                    .Max();
                rowDefinitions.Add(new VirtualizedRowDefinition
                {
                    Height = Math.Max(ws == null ? 360 : ws.EstimatedDetailRowHeight, (int)Math.Ceiling(estimatedHeight + cardGap)),
                    Build = delegate
                    {
                        var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, cardGap) };
                        for (var i = 0; i < rowIndexes.Count; i++)
                        {
                            var group = safeGroups[rowIndexes[i]];
                            var card = BuildLibraryTimelinePackedDayCard(
                                ws,
                                renderFolder,
                                group,
                                timelineContexts,
                                cardWidths[rowIndexes[i]],
                                timelineTileSize,
                                cardColumnCounts[rowIndexes[i]],
                                dpiScale,
                                openSingleFileMetadataEditor,
                                updateDetailSelection,
                                refreshDetailSelectionUi,
                                redrawSelectedFolderDetail);
                            if (card != null)
                            {
                                card.Margin = new Thickness(0, 0, i < rowIndexes.Count - 1 ? cardGap : 0, 0);
                                rowPanel.Children.Add(card);
                            }
                        }
                        return rowPanel;
                    }
                });
            }
            return rowDefinitions;
        }

        FrameworkElement BuildLibraryTimelinePackedDayCard(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserFolderView renderFolder,
            LibraryDetailRenderGroup group,
            IDictionary<string, LibraryTimelineCaptureContext> timelineContexts,
            double cardWidth,
            int timelineTileSize,
            int preferredCardColumns,
            double dpiScale,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi,
            Action redrawSelectedFolderDetail)
        {
            if (group == null) return null;
            var groupFiles = (group.Files ?? new List<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .ToList();
            if (groupFiles.Count == 0) return null;

            const int detailTileGap = 8;
            const int cardPadding = 12;
            var innerPackW = Math.Max(160d, cardWidth - (2 * cardPadding) - 8d);
            var packMinWTimeline = Math.Max(120, (int)Math.Floor(timelineTileSize * 0.62));
            var packMaxWTimeline = Math.Min(timelineTileSize + 72, (int)Math.Floor(innerPackW));
            var colCap = Math.Max(1, preferredCardColumns);
            packMaxWTimeline = Math.Min(
                packMaxWTimeline,
                (int)Math.Floor((innerPackW - (colCap - 1) * detailTileGap) / colCap));
            packMaxWTimeline = Math.Max(packMinWTimeline, packMaxWTimeline);
            var title = BuildLibraryTimelineDayCardTitle(group.CaptureDate, DateTime.Today);
            var absoluteTitle = group.CaptureDate <= DateTime.MinValue ? string.Empty : group.CaptureDate.ToString("MMMM d, yyyy");

            var card = new Border
            {
                Width = Math.Max(220d, Math.Ceiling(cardWidth)),
                Padding = new Thickness(cardPadding),
                Background = Brush("#121A20"),
                BorderBrush = Brush("#24323B"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16)
            };
            var stack = new StackPanel();

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10), LastChildFill = true };
            var countBadge = new Border
            {
                Background = Brush("#162028"),
                BorderBrush = Brush("#2E4551"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = groupFiles.Count + " photo" + (groupFiles.Count == 1 ? string.Empty : "s"),
                    Foreground = Brush("#CFE0E8"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold
                }
            };
            DockPanel.SetDock(countBadge, Dock.Right);
            header.Children.Add(countBadge);
            header.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(title) ? absoluteTitle : title,
                Foreground = Brush("#F1E9DA"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 12, 0)
            });
            stack.Children.Add(header);
            if (!string.IsNullOrWhiteSpace(absoluteTitle) && !string.Equals(title, absoluteTitle, StringComparison.Ordinal))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = absoluteTitle,
                    Foreground = Brush("#8FA1AD"),
                    FontSize = 11.5,
                    Margin = new Thickness(0, -4, 0, 12)
                });
            }

            var cardPackedRows = PackLibraryDetailFilesIntoVariableRows(
                groupFiles,
                innerPackW,
                detailTileGap,
                timelineTileSize,
                packMinWTimeline,
                packMaxWTimeline);
            for (var packRowIndex = 0; packRowIndex < cardPackedRows.Count; packRowIndex++)
            {
                var rowEntries = cardPackedRows[packRowIndex];
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, packRowIndex < cardPackedRows.Count - 1 ? detailTileGap : 0)
                };
                for (var fileIndex = 0; fileIndex < rowEntries.Count; fileIndex++)
                {
                    var file = rowEntries[fileIndex].File;
                    var tw = rowEntries[fileIndex].Width;
                    var decodeW = CalculateLibraryDetailTileDecodeWidth(tw, dpiScale);
                    LibraryTimelineCaptureContext timelineContext;
                    if (timelineContexts == null || !timelineContexts.TryGetValue(file, out timelineContext)) timelineContext = null;
                    var tile = CreateLibraryDetailTile(
                        file,
                        tw,
                        decodeW,
                        delegate { return ws != null && SameLibraryBrowserSelection(ws.Current, renderFolder); },
                        openSingleFileMetadataEditor,
                        updateDetailSelection,
                        ws == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : ws.SelectedDetailFiles,
                        refreshDetailSelectionUi,
                        redrawSelectedFolderDetail,
                        null,
                        null,
                        timelineContext);
                    tile.Margin = new Thickness(0, 0, fileIndex < rowEntries.Count - 1 ? detailTileGap : 0, 0);
                    if (ws != null) ws.DetailTiles.Add(tile);
                    rowPanel.Children.Add(tile);
                }
                stack.Children.Add(rowPanel);
            }

            card.Child = stack;
            return card;
        }
    }
}
