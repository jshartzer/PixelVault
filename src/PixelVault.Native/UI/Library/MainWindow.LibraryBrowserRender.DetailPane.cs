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
        sealed class LibraryBrowserDetailPaneContext
        {
            internal LibraryBrowserPaneRefs Panes;
            internal LibraryFolderInfo Current;
            internal int DetailRenderSequence;
            internal bool PreserveDetailScrollOnNextRender;
            internal double PreservedDetailScrollOffset;
            internal List<Border> DetailTiles;
            internal HashSet<string> SelectedDetailFiles;
            internal List<string> DetailFilesDisplayOrder;
            internal int LastDetailColumns;
            internal int LastDetailTileSize;
            internal int EstimatedDetailRowHeight;
        }

        void LibraryBrowserRenderSelectedFolderDetail(
            LibraryBrowserDetailPaneContext ctx,
            Window libraryWindow,
            Action<string> openSingleFileMetadataEditor,
            Action<string, ModifierKeys> updateDetailSelection,
            Action refreshDetailSelectionUi)
        {
            var panes = ctx.Panes;
            var renderStopwatch = Stopwatch.StartNew();
            var renderVersion = ++ctx.DetailRenderSequence;
            if (!ctx.PreserveDetailScrollOnNextRender) ctx.PreservedDetailScrollOffset = 0;
            ctx.DetailTiles.Clear();
            if (ctx.Current == null)
            {
                ctx.SelectedDetailFiles.Clear();
                ctx.DetailFilesDisplayOrder.Clear();
                SetVirtualizedRows(panes.DetailRows, new List<VirtualizedRowDefinition>(), true, null);
                if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=(none); rows=0; files=0", 40);
                return;
            }
            var detailLayout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll);
            var targetDetailColumns = detailLayout.Columns;
            var size = detailLayout.TileSize;
            ctx.LastDetailColumns = targetDetailColumns;
            ctx.LastDetailTileSize = size;
            ctx.EstimatedDetailRowHeight = Math.Max(200, size + 96);
            var shouldRestoreDetailScroll = ctx.PreserveDetailScrollOnNextRender && ctx.PreservedDetailScrollOffset > 0.1d;
            var restoreDetailScrollOffset = shouldRestoreDetailScroll ? (double?)ctx.PreservedDetailScrollOffset : null;
            var restoreDetailScrollPending = shouldRestoreDetailScroll;
            ctx.PreserveDetailScrollOnNextRender = false;
            var renderFolder = ctx.Current;
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
            if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
            Task.Run(async delegate
            {
                Action<LibraryDetailRenderSnapshot, bool> applyDetailSnapshot = null;
                applyDetailSnapshot = delegate(LibraryDetailRenderSnapshot snapshot, bool logCompletion)
                {
                    if (renderVersion != ctx.DetailRenderSequence) return;
                    if (!SameLibraryFolderSelection(ctx.Current, renderFolder)) return;
                    var visibleFiles = snapshot == null ? new List<string>() : (snapshot.VisibleFiles ?? new List<string>());
                    var visibleSet = new HashSet<string>(visibleFiles, StringComparer.OrdinalIgnoreCase);
                    foreach (var stale in ctx.SelectedDetailFiles.Where(path => !visibleSet.Contains(path)).ToList()) ctx.SelectedDetailFiles.Remove(stale);
                    if (SameLibraryFolderSelection(ctx.Current, renderFolder))
                    {
                        ctx.DetailFilesDisplayOrder.Clear();
                        ctx.DetailFilesDisplayOrder.AddRange(visibleFiles);
                    }
                    ctx.DetailTiles.Clear();
                    if (snapshot == null || snapshot.Groups == null || snapshot.Groups.Count == 0)
                    {
                        ctx.DetailFilesDisplayOrder.Clear();
                        SetVirtualizedRows(panes.DetailRows, new[]
                        {
                            new VirtualizedRowDefinition
                            {
                                Height = 44,
                                Build = delegate
                                {
                                    return new TextBlock { Text = "No captures found in this folder.", Foreground = Brush("#A7B5BD") };
                                }
                            }
                        }, !restoreDetailScrollPending, restoreDetailScrollPending ? restoreDetailScrollOffset : null);
                        restoreDetailScrollPending = false;
                        if (refreshDetailSelectionUi != null) refreshDetailSelectionUi();
                        if (logCompletion)
                        {
                            renderStopwatch.Stop();
                            LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + "; rows=1; files=0; size=" + size, 40);
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
                                Height = ctx.EstimatedDetailRowHeight,
                                Build = delegate
                                {
                                    var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, detailTileGap) };
                                    for (int fileIndex = 0; fileIndex < rowFiles.Count; fileIndex++)
                                    {
                                        var file = rowFiles[fileIndex];
                                        var tile = CreateLibraryDetailTile(
                                            file,
                                            size,
                                            delegate { return SameLibraryFolderSelection(ctx.Current, renderFolder); },
                                            openSingleFileMetadataEditor,
                                            updateDetailSelection,
                                            ctx.SelectedDetailFiles,
                                            refreshDetailSelectionUi);
                                        tile.Margin = new Thickness(0, 0, fileIndex < rowFiles.Count - 1 ? detailTileGap : 0, 0);
                                        ctx.DetailTiles.Add(tile);
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
                    if (logCompletion)
                    {
                        renderStopwatch.Stop();
                        LogPerformanceSample("LibraryDetailRender", renderStopwatch, "folder=" + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + "; groups=" + snapshot.Groups.Count + "; files=" + visibleFiles.Count + "; rows=" + virtualRows.Count + "; columns=" + detailColumns + "; size=" + size, 40);
                    }
                };

                try
                {
                    var metadataIndex = librarySession.LoadLibraryMetadataIndex(false);
                    var detailFiles = GetFilesForLibraryFolderEntry(renderFolder, false)
                        .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
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

                    Func<LibraryDetailRenderSnapshot> buildSnapshot = delegate
                    {
                        var datedFiles = detailFiles
                            .Select(file => new { FilePath = file, CaptureDate = librarySession.ResolveIndexedLibraryDate(file, metadataIndex) })
                            .OrderByDescending(entry => entry.CaptureDate)
                            .ThenBy(entry => entry.FilePath, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var snapshot = new LibraryDetailRenderSnapshot
                        {
                            VisibleFiles = datedFiles.Select(entry => entry.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        };
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

                    var quickSnapshot = buildSnapshot();
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(quickSnapshot, true); }));

                    if (filesMissingCaptureTicks.Count > 0)
                    {
                        try
                        {
                            var savedGameRows = librarySession.LoadSavedGameIndexRows();
                            var metadataByFile = await metadataService.ReadEmbeddedMetadataBatchAsync(filesMissingCaptureTicks, CancellationToken.None).ConfigureAwait(false);
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
                        }
                        catch (Exception repairEx)
                        {
                            Log("Library detail metadata repair failed for " + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + ". " + repairEx.Message);
                        }

                        var refinedSnapshot = buildSnapshot();
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
                        if (!layoutUnchanged)
                        {
                            await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate { applyDetailSnapshot(refinedSnapshot, false); }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    await libraryWindow.Dispatcher.InvokeAsync((Action)(delegate
                    {
                        if (renderVersion != ctx.DetailRenderSequence) return;
                        if (!SameLibraryFolderSelection(ctx.Current, renderFolder)) return;
                        ctx.DetailFilesDisplayOrder.Clear();
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
                        Log("Library detail render failed for " + (renderFolder.Name ?? renderFolder.FolderPath ?? "(unknown)") + ". " + ex.Message);
                        renderStopwatch.Stop();
                    }));
                }
            });
        }
    }
}
