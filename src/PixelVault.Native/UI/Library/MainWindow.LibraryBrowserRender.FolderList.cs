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
            var restoreFolderScrollOffset = ws.PreserveFolderScrollOnNextRender ? Math.Max(0, ws.PreservedFolderScrollOffset) : 0;
            var shouldRestoreFolderScroll = ws.PreserveFolderScrollOnNextRender && restoreFolderScrollOffset > 0.1d;
            ws.PreserveFolderScrollOnNextRender = false;
            var groupingMode = NormalizeLibraryGroupingMode(libraryGroupingMode);
            var sortMode = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
            var flattenGroups = !string.Equals(groupingMode, "console", StringComparison.OrdinalIgnoreCase);
            var searchText = ws.AppliedLibrarySearchText;
            var projectionStopwatch = Stopwatch.StartNew();
            var browserFolders = BuildLibraryBrowserFolderViews(folders, groupingMode);
            projectionStopwatch.Stop();
            ws.ViewFolders.Clear();
            ws.ViewFolders.AddRange(browserFolders);
            var filterSortStopwatch = Stopwatch.StartNew();
            var visibleFolders = string.IsNullOrWhiteSpace(searchText)
                ? browserFolders
                : browserFolders.Where(folder =>
                    (!string.IsNullOrWhiteSpace(folder.Name) && folder.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(folder.PlatformSummaryText) && folder.PlatformSummaryText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(folder.PrimaryFolderPath) && folder.PrimaryFolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(folder.GameId) && folder.GameId.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    folder.SourceFolders.Any(source => !string.IsNullOrWhiteSpace(source.FolderPath) && source.FolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            var orderedVisibleFolders = visibleFolders
                .OrderByDescending(folder => string.Equals(sortMode, "recent", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(BuildLibraryBrowserDisplayFolder(folder)) : DateTime.MinValue)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? folder.FileCount : 0)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(BuildLibraryBrowserDisplayFolder(folder)) : DateTime.MinValue)
                .ThenBy(folder => string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase) ? PlatformGroupOrder(DetermineLibraryBrowserGroup(folder)) : 0)
                .ThenBy(folder => DetermineLibraryBrowserGroup(folder))
                .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            filterSortStopwatch.Stop();
            var folderLayout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll);
            var targetFolderColumns = folderLayout.Columns;
            var tileWidth = folderLayout.TileSize;
            ws.LastFolderColumns = targetFolderColumns;
            ws.LastFolderTileSize = tileWidth;
            var tileHeight = (int)Math.Round(tileWidth * 1.5d);
            var selectedFolder = FindMatchingLibraryBrowserView(ws.Current, browserFolders);
            if (selectedFolder != null)
            {
                if (!SameLibraryBrowserSelection(ws.Current, selectedFolder)) showFolder(selectedFolder);
                else ws.Current = selectedFolder;
            }
            else
            {
                ws.Current = null;
                activeSelectedLibraryFolder = null;
                ws.SelectedDetailFiles.Clear();
                ws.DetailSelectionAnchorIndex = -1;
                ws.DetailFilesDisplayOrder.Clear();
                panes.DetailTitle.Text = "Select a folder";
                panes.DetailMeta.Text = "Browse the library you chose in Settings.";
                if (panes.PreviewPlatformBadgeHost != null)
                {
                    panes.PreviewPlatformBadgeHost.Content = null;
                    panes.PreviewPlatformBadgeHost.Visibility = Visibility.Collapsed;
                }
                panes.OpenFolderButton.Content = BuildToolbarButtonContent("\uE8B7", "Open Folder");
                panes.PreviewImage.Source = null;
                panes.PreviewImage.Visibility = Visibility.Collapsed;
                renderSelectedFolder();
            }

            var folderCardHeight = tileHeight + 82;
            var folderRowHeight = folderCardHeight + 14;
            var folderColumns = targetFolderColumns;
            var virtualRows = new List<VirtualizedRowDefinition>();
            if (orderedVisibleFolders.Count == 0)
            {
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
                    virtualRows.Add(new VirtualizedRowDefinition
                    {
                        Height = folderRowHeight,
                        Build = delegate
                        {
                            var flatWrap = new WrapPanel();
                            foreach (var folder in rowFolders) flatWrap.Children.Add(buildFolderTile(folder, tileWidth, tileHeight, true));
                            return new Border { Height = folderRowHeight, Background = Brushes.Transparent, Child = flatWrap };
                        }
                    });
                }
                SetVirtualizedRows(panes.TileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=flat; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; grouping=" + groupingMode + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
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
                        virtualRows.Add(new VirtualizedRowDefinition
                        {
                            Height = folderRowHeight,
                            Build = delegate
                            {
                                var groupWrap = new WrapPanel();
                                foreach (var folder in rowFolders) groupWrap.Children.Add(buildFolderTile(folder, tileWidth, tileHeight, false));
                                return new Border { Height = folderRowHeight, Background = Brushes.Transparent, Child = groupWrap };
                            }
                        });
                    }
                }
            }
            SetVirtualizedRows(panes.TileRows, virtualRows, !shouldRestoreFolderScroll, shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null);
            renderStopwatch.Stop();
            LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=grouped; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; grouping=" + groupingMode + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
        }
    }
}
