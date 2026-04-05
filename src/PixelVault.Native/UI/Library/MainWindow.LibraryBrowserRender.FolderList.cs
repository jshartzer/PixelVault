using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserRenderFolderList(
            LibraryBrowserWorkingSet ws,
            Func<LibraryBrowserFolderView, int, int, bool, Button> buildFolderTile,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderSelectedFolder,
            Action selfRerender)
        {
            var panes = ws.Panes;
            var folders = ws.Folders;
            var renderStopwatch = Stopwatch.StartNew();
            var groupingMode = NormalizeLibraryGroupingMode(libraryGroupingMode);
            var timelineMode = string.Equals(groupingMode, "timeline", StringComparison.OrdinalIgnoreCase);
            if (!timelineMode && panes?.TileScroll != null && !ws.PreserveFolderScrollOnNextRender)
            {
                var liveFolderScroll = panes.TileScroll.VerticalOffset;
                if (liveFolderScroll > 0.1d)
                {
                    ws.PreservedFolderScrollOffset = Math.Max(0d, liveFolderScroll);
                    ws.PreserveFolderScrollOnNextRender = true;
                }
            }
            var restoreFolderScrollOffset = ws.PreserveFolderScrollOnNextRender ? Math.Max(0d, ws.PreservedFolderScrollOffset) : 0d;
            var shouldRestoreFolderScroll = ws.PreserveFolderScrollOnNextRender && restoreFolderScrollOffset > 0.1d;
            ws.PreserveFolderScrollOnNextRender = false;
            var sortMode = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
            var flattenGroups = !string.Equals(groupingMode, "console", StringComparison.OrdinalIgnoreCase);
            var searchText = ws.AppliedLibrarySearchText;
            var searchNormalized = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim().ToLowerInvariant();
            var projectionStopwatch = Stopwatch.StartNew();
            var browserFolders = GetOrBuildLibraryBrowserFolderViews(folders, timelineMode ? "all" : groupingMode);
            projectionStopwatch.Stop();
            ws.ViewFolders.Clear();
            ws.ViewFolders.AddRange(browserFolders);
            ApplyLibraryBrowserLayoutMode(panes);
            var filterSortStopwatch = Stopwatch.StartNew();
            var visibleFolders = searchNormalized == null
                ? browserFolders
                : browserFolders.Where(folder =>
                    !string.IsNullOrEmpty(folder.SearchBlob)
                    && folder.SearchBlob.IndexOf(searchNormalized, StringComparison.Ordinal) >= 0)
                .ToList();
            filterSortStopwatch.Stop();
            if (timelineMode)
            {
                var timelineView = BuildLibraryBrowserTimelineView(visibleFolders);
                ws.PendingSessionRestore = false;
                ws.PendingRestoreViewKey = null;
                ws.PendingRestoreDetailScrollAfterShow = 0;
                SetVirtualizedRows(panes.TileRows, new List<VirtualizedRowDefinition>(), true, null);
                showFolder(timelineView);
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=timeline; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=" + visibleFolders.Count + "; files=" + timelineView.FileCount + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                LogLibraryBrowserFirstFolderListPaintOnce("mode=timeline; visible=" + visibleFolders.Count + "; files=" + timelineView.FileCount);
                return;
            }
            var orderedVisibleFolders = visibleFolders
                .OrderByDescending(folder => string.Equals(sortMode, "recent", StringComparison.OrdinalIgnoreCase) ? GetLibraryBrowserFolderViewSortRecentlyAdded(folder) : DateTime.MinValue)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? folder.FileCount : 0)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? GetLibraryBrowserFolderViewSortNewest(folder) : DateTime.MinValue)
                .ThenBy(folder => string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase) ? PlatformGroupOrder(DetermineLibraryBrowserGroup(folder)) : 0)
                .ThenBy(folder => DetermineLibraryBrowserGroup(folder))
                .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var folderLayout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll);
            var targetFolderColumns = folderLayout.Columns;
            var tileWidth = folderLayout.TileSize;
            var folderPaneWidth = ResolveScrollViewerLayoutWidth(panes.TileScroll);
            ws.LastFolderColumns = targetFolderColumns;
            ws.LastFolderTileSize = tileWidth;
            ws.LastFolderViewportWidth = folderPaneWidth;
            var tileHeight = (int)Math.Round(tileWidth * 1.5d);
            var selectedFolder = FindMatchingLibraryBrowserView(ws.Current, browserFolders);
            if (selectedFolder != null)
            {
                if (!SameLibraryBrowserSelection(ws.Current, selectedFolder)) showFolder(selectedFolder);
                else ws.Current = selectedFolder;
            }
            else if (!ws.PendingSessionRestore)
            {
                ws.Current = null;
                activeSelectedLibraryFolder = null;
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.DetailFilesDisplayOrder.Clear();
                panes.DetailTitle.Text = "Select a folder";
                panes.DetailMeta.Text = "Browse the library you chose in Settings.";
                if (panes.DetailTitleBadgePanel != null)
                {
                    panes.DetailTitleBadgePanel.Children.Clear();
                    panes.DetailTitleBadgePanel.Visibility = Visibility.Collapsed;
                }
                panes.OpenFolderButton.Content = BuildToolbarButtonContent("\uE8B7", "Open Folder");
                panes.PreviewImage.Source = null;
                panes.PreviewImage.Visibility = Visibility.Collapsed;
                renderSelectedFolder();
                PersistLibraryBrowserLastSelection(null);
            }

            var folderColumns = targetFolderColumns;
            var virtualRows = new List<VirtualizedRowDefinition>();
            int ResolveFolderRowTileWidth(int rowCount)
            {
                if (rowCount <= 0) return tileWidth;
                var availableWidth = Math.Max(tileWidth, folderPaneWidth - 4);
                var rawFill = (int)Math.Floor((availableWidth - ((rowCount - 1) * 14d)) / Math.Max(1d, rowCount));
                var roundedFill = Math.Max(tileWidth, (int)(Math.Round(rawFill / 16d) * 16));
                var maxExpandedTile = Math.Max(tileWidth, Math.Min(1000, NormalizeLibraryFolderTileSize(libraryFolderTileSize) + 176));
                return Math.Max(tileWidth, Math.Min(maxExpandedTile, roundedFill));
            }
            if (orderedVisibleFolders.Count == 0)
            {
                if (ws.PendingSessionRestore)
                {
                    ws.PendingSessionRestore = false;
                    ws.PendingRestoreViewKey = null;
                    ws.PendingRestoreDetailScrollAfterShow = 0;
                }
                virtualRows.Add(new VirtualizedRowDefinition
                {
                    Height = 44,
                    Build = delegate
                    {
                        return new TextBlock
                        {
                            Text = ws.LibraryFoldersLoading
                                ? "Loading library folders..."
                                : (string.IsNullOrWhiteSpace(searchText) ? "No library folders found." : "No folders match the current search."),
                            Foreground = Brush("#A7B5BD"),
                            Margin = new Thickness(0, 12, 0, 0)
                        };
                    }
                });
                SetVirtualizedRows(panes.TileRows, virtualRows, true, null);
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=" + (ws.LibraryFoldersLoading ? "loading" : "empty") + "; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=0; grouping=" + groupingMode + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                return;
            }
            if (flattenGroups)
            {
                for (int rowStart = 0; rowStart < orderedVisibleFolders.Count; rowStart += folderColumns)
                {
                    var rowFolders = orderedVisibleFolders.Skip(rowStart).Take(folderColumns).ToList();
                    var rowTileWidth = ResolveFolderRowTileWidth(rowFolders.Count);
                    var rowTileHeight = (int)Math.Round(rowTileWidth * 1.5d);
                    var rowCardHeight = rowTileHeight + 16;
                    var rowHeight = rowCardHeight + 14;
                    virtualRows.Add(new VirtualizedRowDefinition
                    {
                        Height = rowHeight,
                        Build = delegate
                        {
                            var flatWrap = new WrapPanel();
                            foreach (var folder in rowFolders) flatWrap.Children.Add(buildFolderTile(folder, rowTileWidth, rowTileHeight, true));
                            return new Border { Height = rowHeight, Background = Brushes.Transparent, Child = flatWrap };
                        }
                    });
                }
                SetVirtualizedRows(panes.TileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
                LibraryBrowserTryRestoreSessionSelection(ws, browserFolders, orderedVisibleFolders, showFolder);
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=flat; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; grouping=" + groupingMode + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                LogLibraryBrowserFirstFolderListPaintOnce("mode=flat; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count);
                return;
            }

            var folderGroups = orderedVisibleFolders
                .GroupBy(folder => DetermineLibraryBrowserGroup(folder))
                .OrderBy(group => PlatformGroupOrder(group.Key))
                .ThenBy(group => group.Key)
                .ToList();
            foreach (var folderGroup in folderGroups)
            {
                var groupFolders = folderGroup.ToList();
                var groupLabel = folderGroup.Key;
                var sectionCollapsed = ws.CollapsedPlatformSections.Contains(groupLabel);
                virtualRows.Add(new VirtualizedRowDefinition
                {
                    Height = 82,
                    Build = delegate
                    {
                        var gl = groupLabel;
                        return new Border
                        {
                            Height = 82,
                            Background = Brush("#161F24"),
                            BorderBrush = Brush("#26363F"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(10),
                            Padding = new Thickness(10, 10, 14, 12),
                            Child = BuildLibrarySectionHeader(gl, groupFolders.Count, ws.CollapsedPlatformSections.Contains(gl), delegate
                            {
                                if (ws.CollapsedPlatformSections.Contains(gl)) ws.CollapsedPlatformSections.Remove(gl);
                                else ws.CollapsedPlatformSections.Add(gl);
                                if (selfRerender != null) selfRerender();
                            })
                        };
                    }
                });
                if (!sectionCollapsed)
                {
                    for (int rowStart = 0; rowStart < groupFolders.Count; rowStart += folderColumns)
                    {
                        var rowFolders = groupFolders.Skip(rowStart).Take(folderColumns).ToList();
                        var rowTileWidth = ResolveFolderRowTileWidth(rowFolders.Count);
                        var rowTileHeight = (int)Math.Round(rowTileWidth * 1.5d);
                        var rowCardHeight = rowTileHeight + 16;
                        var rowHeight = rowCardHeight + 14;
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = rowHeight,
                            Build = delegate
                            {
                                var groupWrap = new WrapPanel();
                                foreach (var folder in rowFolders) groupWrap.Children.Add(buildFolderTile(folder, rowTileWidth, rowTileHeight, false));
                                return new Border { Height = rowHeight, Background = Brushes.Transparent, Child = groupWrap };
                            }
                        });
                    }
                }
            }
            SetVirtualizedRows(panes.TileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
            LibraryBrowserTryRestoreSessionSelection(ws, browserFolders, orderedVisibleFolders, showFolder);
            renderStopwatch.Stop();
            LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=grouped; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; grouping=" + groupingMode + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
            LogLibraryBrowserFirstFolderListPaintOnce("mode=grouped; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count);
        }

        void LibraryBrowserTryRestoreSessionSelection(
            LibraryBrowserWorkingSet ws,
            List<LibraryBrowserFolderView> browserFolders,
            List<LibraryBrowserFolderView> orderedVisibleFolders,
            Action<LibraryBrowserFolderView> showFolder)
        {
            if (ws == null || !ws.PendingSessionRestore || string.IsNullOrWhiteSpace(ws.PendingRestoreViewKey) || showFolder == null) return;
            var match = FindLibraryBrowserViewByViewKey(orderedVisibleFolders, ws.PendingRestoreViewKey)
                ?? FindLibraryBrowserViewByViewKey(browserFolders, ws.PendingRestoreViewKey);
            ws.PendingSessionRestore = false;
            ws.PendingRestoreViewKey = null;
            if (match == null)
            {
                ws.PendingRestoreDetailScrollAfterShow = 0;
                return;
            }
            showFolder(match);
        }
    }
}
