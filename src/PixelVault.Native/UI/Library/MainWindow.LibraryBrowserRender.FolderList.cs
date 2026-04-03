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
        sealed class LibraryBrowserFolderListContext
        {
            internal LibraryBrowserPaneRefs Panes;
            internal List<LibraryFolderInfo> Folders;
            internal HashSet<string> CollapsedPlatformSections;
            internal HashSet<string> SelectedDetailFiles;
            internal List<string> DetailFilesDisplayOrder;
            internal LibraryFolderInfo Current;
            internal bool PreserveFolderScrollOnNextRender;
            internal double PreservedFolderScrollOffset;
            internal string AppliedLibrarySearchText;
            internal bool LibraryFoldersLoading;
            internal int LastFolderColumns;
            internal int LastFolderTileSize;
            internal int DetailSelectionAnchorIndex;
        }

        void LibraryBrowserRenderFolderList(
            LibraryBrowserFolderListContext ctx,
            Func<LibraryFolderInfo, int, int, bool, Button> buildFolderTile,
            Action<LibraryFolderInfo> showFolder,
            Action renderSelectedFolder,
            Action selfRerender)
        {
            var panes = ctx.Panes;
            var folders = ctx.Folders;
            var renderStopwatch = Stopwatch.StartNew();
            var restoreFolderScrollOffset = ctx.PreserveFolderScrollOnNextRender ? Math.Max(0, ctx.PreservedFolderScrollOffset) : 0;
            var shouldRestoreFolderScroll = ctx.PreserveFolderScrollOnNextRender && restoreFolderScrollOffset > 0.1d;
            ctx.PreserveFolderScrollOnNextRender = false;
            var sortMode = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
            var flattenGroups = !string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase);
            var searchText = ctx.AppliedLibrarySearchText;
            var filterSortStopwatch = Stopwatch.StartNew();
            var visibleFolders = string.IsNullOrWhiteSpace(searchText)
                ? folders
                : folders.Where(folder =>
                    (!string.IsNullOrWhiteSpace(folder.Name) && folder.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(folder.PlatformLabel) && folder.PlatformLabel.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(folder.FolderPath) && folder.FolderPath.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(folder.GameId) && folder.GameId.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            var orderedVisibleFolders = visibleFolders
                .OrderByDescending(folder => string.Equals(sortMode, "recent", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(folder) : DateTime.MinValue)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? folder.FileCount : 0)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? GetLibraryFolderNewestDate(folder) : DateTime.MinValue)
                .ThenBy(folder => string.Equals(sortMode, "platform", StringComparison.OrdinalIgnoreCase) ? PlatformGroupOrder(DetermineLibraryFolderGroup(folder)) : 0)
                .ThenBy(folder => DetermineLibraryFolderGroup(folder))
                .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
            filterSortStopwatch.Stop();
            var folderLayout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll);
            var targetFolderColumns = folderLayout.Columns;
            var tileWidth = folderLayout.TileSize;
            ctx.LastFolderColumns = targetFolderColumns;
            ctx.LastFolderTileSize = tileWidth;
            var tileHeight = (int)Math.Round(tileWidth * 1.5d);
            LibraryFolderInfo selectedFolder = null;
            if (ctx.Current != null)
            {
                selectedFolder = folders.FirstOrDefault(f => f.FolderPath == ctx.Current.FolderPath && string.Equals(f.PlatformLabel, ctx.Current.PlatformLabel, StringComparison.OrdinalIgnoreCase));
                if (selectedFolder == null) selectedFolder = folders.FirstOrDefault(f => f.FolderPath == ctx.Current.FolderPath);
            }
            if (selectedFolder != null)
            {
                if (!SameLibraryFolderSelection(ctx.Current, selectedFolder)) showFolder(selectedFolder);
                else ctx.Current = selectedFolder;
            }
            else
            {
                ctx.Current = null;
                activeSelectedLibraryFolder = null;
                ctx.SelectedDetailFiles.Clear();
                ctx.DetailSelectionAnchorIndex = -1;
                ctx.DetailFilesDisplayOrder.Clear();
                panes.DetailTitle.Text = "Select a folder";
                panes.DetailMeta.Text = "Browse the library you chose in Settings.";
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
                            Text = ctx.LibraryFoldersLoading
                                ? "Loading library folders..."
                                : (string.IsNullOrWhiteSpace(searchText) ? "No library folders found." : "No folders match the current search."),
                            Foreground = Brush("#A7B5BD"),
                            Margin = new Thickness(0, 12, 0, 0)
                        };
                    }
                });
                SetVirtualizedRows(panes.TileRows, virtualRows, true, null);
                renderStopwatch.Stop();
                LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=" + (ctx.LibraryFoldersLoading ? "loading" : "empty") + "; foldersLoaded=" + folders.Count + "; visible=0; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=0; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
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
                LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=flat; foldersLoaded=" + folders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=0; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
                return;
            }

            var folderGroups = orderedVisibleFolders
                .GroupBy(folder => DetermineLibraryFolderGroup(folder))
                .OrderBy(group => PlatformGroupOrder(group.Key))
                .ThenBy(group => group.Key)
                .ToList();
            foreach (var folderGroup in folderGroups)
            {
                var groupFolders = folderGroup.ToList();
                var groupLabel = folderGroup.Key;
                var sectionCollapsed = ctx.CollapsedPlatformSections.Contains(groupLabel);
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
                            Child = BuildLibrarySectionHeader(gl, groupFolders.Count, ctx.CollapsedPlatformSections.Contains(gl), delegate
                            {
                                if (ctx.CollapsedPlatformSections.Contains(gl)) ctx.CollapsedPlatformSections.Remove(gl);
                                else ctx.CollapsedPlatformSections.Add(gl);
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
            LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=grouped; foldersLoaded=" + folders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; loadMs=0; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
        }
    }
}
