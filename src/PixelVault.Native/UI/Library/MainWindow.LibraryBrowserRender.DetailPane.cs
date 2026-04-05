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
            if (!ws.PreserveDetailScrollOnNextRender) ws.PreservedDetailScrollOffset = 0;
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
            var detailLayout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll);
            var targetDetailColumns = detailLayout.Columns;
            var size = detailLayout.TileSize;
            ws.LastDetailColumns = targetDetailColumns;
            ws.LastDetailTileSize = size;
            ws.EstimatedDetailRowHeight = Math.Max(220, size + (IsLibraryBrowserTimelineView(ws.Current) ? 176 : 96));
            var shouldRestoreDetailScroll = ws.PreserveDetailScrollOnNextRender && ws.PreservedDetailScrollOffset > 0.1d;
            var restoreDetailScrollOffset = shouldRestoreDetailScroll ? (double?)ws.PreservedDetailScrollOffset : null;
            var restoreDetailScrollPending = shouldRestoreDetailScroll;
            ws.PreserveDetailScrollOnNextRender = false;
            var resetRowsToLoading = ws.ResetDetailRowsToLoadingOnNextRender;
            ws.ResetDetailRowsToLoadingOnNextRender = false;
            var renderFolder = ws.Current;
            var timelineView = IsLibraryBrowserTimelineView(renderFolder);
            var timelineRangeStart = ws.TimelineStartDate;
            var timelineRangeEnd = ws.TimelineEndDate;
            NormalizeLibraryTimelineDateRange(ref timelineRangeStart, ref timelineRangeEnd);
            LogTroubleshooting("LibraryDetailRenderStart",
                "renderVersion=" + renderVersion
                + "; resetToLoading=" + resetRowsToLoading
                + "; restoreScroll=" + shouldRestoreDetailScroll
                + "; detailColumns=" + targetDetailColumns
                + "; detailSize=" + size
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
                            LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; rows=1; files=0; size=" + size, 40);
                            LogLibraryBrowserFirstDetailPaintOnce("folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; files=0");
                        }
                        return;
                    }

                    const int detailTileGap = 8;
                    var detailColumns = targetDetailColumns;
                    var virtualRows = new List<VirtualizedRowDefinition>();
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
                        for (int rowStart = 0; rowStart < groupFiles.Count; rowStart += detailColumns)
                        {
                            var rowFiles = groupFiles.Skip(rowStart).Take(detailColumns).ToList();
                            virtualRows.Add(new VirtualizedRowDefinition
                            {
                                Height = ws.EstimatedDetailRowHeight,
                                Build = delegate
                                {
                                    var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, detailTileGap) };
                                    for (int fileIndex = 0; fileIndex < rowFiles.Count; fileIndex++)
                                    {
                                        var file = rowFiles[fileIndex];
                                        LibraryTimelineCaptureContext timelineContext = null;
                                        if (timelineView) timelineContexts.TryGetValue(file, out timelineContext);
                                        Action<string> useFileAsFolderCover = null;
                                        if (!timelineView)
                                        {
                                            useFileAsFolderCover = delegate(string imagePath)
                                            {
                                                var folder = activeSelectedLibraryFolder;
                                                if (folder == null || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) || !IsImage(imagePath)) return;
                                                SaveCustomCover(folder, imagePath);
                                                if (renderFolderTiles != null) renderFolderTiles();
                                                redrawSelectedFolderDetail?.Invoke();
                                                ShowLibraryBrowserToast(ws, "Cover saved");
                                            };
                                        }
                                        var tile = CreateLibraryDetailTile(
                                            file,
                                            size,
                                            delegate { return SameLibraryBrowserSelection(ws.Current, renderFolder); },
                                            openSingleFileMetadataEditor,
                                            updateDetailSelection,
                                            ws.SelectedDetailFiles,
                                            refreshDetailSelectionUi,
                                            redrawSelectedFolderDetail,
                                            useFileAsFolderCover,
                                            timelineContext);
                                        tile.Margin = new Thickness(0, 0, fileIndex < rowFiles.Count - 1 ? detailTileGap : 0, 0);
                                        ws.DetailTiles.Add(tile);
                                        rowPanel.Children.Add(tile);
                                    }
                                    return rowPanel;
                                }
                            });
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
                        LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.PrimaryFolderPath ?? "(unknown)") + "; groups=" + snapshot.Groups.Count + "; files=" + visibleFiles.Count + "; rows=" + virtualRows.Count + "; columns=" + detailColumns + "; size=" + size, 40);
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
    }
}
