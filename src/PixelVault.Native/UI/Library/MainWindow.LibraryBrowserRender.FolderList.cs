using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserRenderFolderList(
            LibraryBrowserWorkingSet ws,
            Func<LibraryBrowserFolderView, int, int, bool, FrameworkElement> buildFolderTile,
            Action<LibraryBrowserFolderView> showFolder,
            Action renderSelectedFolder,
            Action selfRerender,
            Action clearLibrarySearchAndRerender,
            Action refreshLibraryFoldersLoose)
        {
            var panes = ws.Panes;
            var folders = ws.Folders;
            var renderStopwatch = Stopwatch.StartNew();
            var groupingMode = NormalizeLibraryGroupingMode(libraryGroupingMode);
            var timelineMode = string.Equals(groupingMode, "timeline", StringComparison.OrdinalIgnoreCase);
            var photoWorkspaceRail = ws.WorkspaceMode == LibraryWorkspaceMode.Photo;
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
            var filterMode = NormalizeLibraryFolderFilterMode(libraryFolderFilterMode);
            var flattenGroups = !string.Equals(groupingMode, "console", StringComparison.OrdinalIgnoreCase);
            var searchText = ws.AppliedLibrarySearchText;
            var searchNormalized = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim().ToLowerInvariant();
            var projectionStopwatch = Stopwatch.StartNew();
            var browserFolders = GetOrBuildLibraryBrowserFolderViews(folders, timelineMode ? "all" : groupingMode);
            projectionStopwatch.Stop();
            ws.ViewFolders.Clear();
            ws.ViewFolders.AddRange(browserFolders);
            ApplyLibraryBrowserLayoutMode(panes, ws.WorkspaceMode);
            var filterSortStopwatch = Stopwatch.StartNew();
            var visibleFolders = searchNormalized == null
                ? browserFolders
                : browserFolders.Where(folder =>
                    !string.IsNullOrEmpty(folder.SearchBlob)
                    && folder.SearchBlob.IndexOf(searchNormalized, StringComparison.Ordinal) >= 0)
                .ToList();
            visibleFolders = visibleFolders
                .Where(folder =>
                    LibraryBrowseFolderSummary.MatchesFilter(
                        filterMode,
                        LibraryBrowseFolderSummary.FromFolderView(folder),
                        NormalizeConsoleLabel))
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
                .OrderByDescending(folder => string.Equals(sortMode, "added", StringComparison.OrdinalIgnoreCase) ? GetLibraryBrowserFolderViewSortRecentlyAdded(folder) : DateTime.MinValue)
                .ThenByDescending(folder => string.Equals(sortMode, "captured", StringComparison.OrdinalIgnoreCase) ? GetLibraryBrowserFolderViewSortNewest(folder) : DateTime.MinValue)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? folder.FileCount : 0)
                .ThenByDescending(folder => string.Equals(sortMode, "photos", StringComparison.OrdinalIgnoreCase) ? GetLibraryBrowserFolderViewSortNewest(folder) : DateTime.MinValue)
                .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(folder => DetermineLibraryBrowserGroup(folder))
                .ToList();
            var folderLayout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll, photoWorkspaceRail);
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
                var rawFill = (int)Math.Floor((availableWidth - ((rowCount - 1) * 12d)) / Math.Max(1d, rowCount));
                var roundedFill = Math.Max(tileWidth, (int)(Math.Round(rawFill / 16d) * 16));
                var userFolderTileCap = photoWorkspaceRail
                    ? 1000
                    : NormalizeLibraryFolderTileSize(libraryFolderTileSize);
                var maxExpandedTile = Math.Max(tileWidth, Math.Min(1000, userFolderTileCap + 176));
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
                var placeholderHeight = ws.LibraryFoldersLoading ? 220d : 300d;
                virtualRows.Add(new VirtualizedRowDefinition
                {
                    Height = placeholderHeight,
                    Build = delegate
                    {
                        return BuildLibraryFolderPanePlaceholder(
                            ws.LibraryFoldersLoading,
                            folders.Count,
                            browserFolders.Count,
                            filterMode,
                            searchText,
                            selfRerender,
                            clearLibrarySearchAndRerender,
                            refreshLibraryFoldersLoose);
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
                var resetFlatScroll = !shouldRestoreFolderScroll;
                double? flatScrollRestore = shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null;
                if (photoWorkspaceRail && ws.ScrollPhotoRailSelectionToTopPending && ws.Current != null)
                {
                    var selIdx = orderedVisibleFolders.FindIndex(f => SameLibraryBrowserSelection(ws.Current, f));
                    if (selIdx >= 0)
                    {
                        var targetRow = selIdx / folderColumns;
                        double y = 0;
                        for (var r = 0; r < targetRow; r++)
                        {
                            var rowFolders = orderedVisibleFolders.Skip(r * folderColumns).Take(folderColumns).ToList();
                            if (rowFolders.Count == 0) break;
                            var rtw = ResolveFolderRowTileWidth(rowFolders.Count);
                            var rth = (int)Math.Round(rtw * 1.5d);
                            var rowCardHeight = rth + 16;
                            y += rowCardHeight + 14;
                        }
                        flatScrollRestore = y;
                        resetFlatScroll = false;
                    }
                    ws.ScrollPhotoRailSelectionToTopPending = false;
                }
                else if (ws.ScrollPhotoRailSelectionToTopPending) ws.ScrollPhotoRailSelectionToTopPending = false;
                SetVirtualizedRows(panes.TileRows, virtualRows, resetFlatScroll, flatScrollRestore);
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
                        var rowCardHeight = rowTileHeight + 14;
                        var rowHeight = rowCardHeight + 12;
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
            double? groupedTopScroll = null;
            if (photoWorkspaceRail && ws.ScrollPhotoRailSelectionToTopPending && ws.Current != null)
            {
                double yAcc = 0;
                foreach (var folderGroup in folderGroups)
                {
                    var groupLabel = folderGroup.Key;
                    var sectionCollapsed = ws.CollapsedPlatformSections.Contains(groupLabel);
                    yAcc += 82;
                    if (sectionCollapsed) continue;
                    var groupFolders = folderGroup.ToList();
                    for (var rowStart = 0; rowStart < groupFolders.Count; rowStart += folderColumns)
                    {
                        var rowFolders = groupFolders.Skip(rowStart).Take(folderColumns).ToList();
                        if (rowFolders.Any(f => SameLibraryBrowserSelection(ws.Current, f)))
                        {
                            groupedTopScroll = yAcc;
                            break;
                        }
                        var grw = ResolveFolderRowTileWidth(rowFolders.Count);
                        var grh = (int)Math.Round(grw * 1.5d);
                        var growCard = grh + 14;
                        yAcc += growCard + 12;
                    }
                    if (groupedTopScroll.HasValue) break;
                }
                ws.ScrollPhotoRailSelectionToTopPending = false;
            }
            else if (ws.ScrollPhotoRailSelectionToTopPending) ws.ScrollPhotoRailSelectionToTopPending = false;
            var resetGroupedScroll = !shouldRestoreFolderScroll;
            double? groupedScrollRestore = shouldRestoreFolderScroll ? (double?)restoreFolderScrollOffset : null;
            if (groupedTopScroll.HasValue)
            {
                resetGroupedScroll = false;
                groupedScrollRestore = groupedTopScroll;
            }
            SetVirtualizedRows(panes.TileRows, virtualRows, resetGroupedScroll, groupedScrollRestore);
            LibraryBrowserTryRestoreSessionSelection(ws, browserFolders, orderedVisibleFolders, showFolder);
            renderStopwatch.Stop();
            LogPerformanceSample("LibraryFolderRender", renderStopwatch, "mode=grouped; foldersLoaded=" + folders.Count + "; views=" + browserFolders.Count + "; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count + "; columns=" + folderColumns + "; grouping=" + groupingMode + "; search=" + (string.IsNullOrWhiteSpace(searchText) ? "(none)" : searchText) + "; sort=" + sortMode + "; projectMs=" + projectionStopwatch.ElapsedMilliseconds + "; filterMs=" + filterSortStopwatch.ElapsedMilliseconds, 40);
            LogLibraryBrowserFirstFolderListPaintOnce("mode=grouped; visible=" + orderedVisibleFolders.Count + "; rows=" + virtualRows.Count);
        }

        Button BuildLibraryFolderCtaButton(string label, RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = label,
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 4, 10, 0),
                FontSize = 13,
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
                Background = Brush(DesignTokens.ActionSecondaryFill),
                BorderBrush = Brush(DesignTokens.BorderDefault),
                BorderThickness = new Thickness(1)
            };
            b.Click += onClick;
            return b;
        }

        FrameworkElement BuildLibraryFolderPanePlaceholder(
            bool loading,
            int rawFolderCount,
            int projectedViewCount,
            string filterMode,
            string searchText,
            Action selfRerender,
            Action clearLibrarySearchAndRerender,
            Action refreshLibraryFoldersLoose)
        {
            var root = new StackPanel { Margin = new Thickness(4, 8, 12, 16), MaxWidth = 520 };
            if (loading)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "Loading your library",
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush(DesignTokens.TextOnInput)
                });
                root.Children.Add(new TextBlock
                {
                    Text = "Reading folders from disk and applying your filters. This should finish in a moment.",
                    Foreground = Brush(DesignTokens.TextLabelMuted),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 8, 0, 16)
                });
                var skRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                for (var i = 0; i < 4; i++)
                {
                    skRow.Children.Add(new Border
                    {
                        Width = 72,
                        Height = 96,
                        Margin = new Thickness(0, 0, 10, 0),
                        CornerRadius = new CornerRadius(10),
                        Background = Brush(DesignTokens.PanelElevated),
                        BorderBrush = Brush(DesignTokens.BorderDefault),
                        BorderThickness = new Thickness(1)
                    });
                }
                root.Children.Add(skRow);
                return root;
            }

            var normFilter = NormalizeLibraryFolderFilterMode(filterMode);
            var filterRestrictive = !string.Equals(normFilter, "all", StringComparison.OrdinalIgnoreCase);
            var hasSearch = !string.IsNullOrWhiteSpace(searchText);
            string title;
            string body;

            if (rawFolderCount == 0)
            {
                title = "No games in this library yet";
                body = "Point PixelVault at capture folders in Path Settings (Settings → Path Settings), then refresh. Imports also appear after you move files into the library destination.";
            }
            else if (projectedViewCount == 0)
            {
                title = "Still preparing folders";
                body = "The library has data but no browse rows yet. Try a refresh.";
            }
            else if (hasSearch)
            {
                title = "No folders match your search";
                body = "Try another phrase, clear the search box, or check spelling.";
            }
            else if (filterRestrictive)
            {
                title = "No folders match this filter";
                body = "Filter is set to “" + LibraryFolderFilterModeLabel(filterMode) + "”. Reset to All Games or pick another filter.";
            }
            else
            {
                title = "Nothing to show here";
                body = "Try refreshing the library list or adjusting search.";
            }

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
                Foreground = Brush(DesignTokens.TextLabelMuted),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 12)
            });

            var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            if (hasSearch && clearLibrarySearchAndRerender != null)
                actions.Children.Add(BuildLibraryFolderCtaButton("Clear search", delegate { clearLibrarySearchAndRerender(); }));
            if (filterRestrictive)
                actions.Children.Add(BuildLibraryFolderCtaButton("Show all games", delegate
                {
                    libraryFolderFilterMode = SettingsService.NormalizeLibraryFolderFilterMode("all");
                    SaveSettings();
                    selfRerender?.Invoke();
                }));
            if (refreshLibraryFoldersLoose != null)
                actions.Children.Add(BuildLibraryFolderCtaButton("Refresh folders", delegate { refreshLibraryFoldersLoose(); }));
            actions.Children.Add(BuildLibraryFolderCtaButton("Settings…", delegate { ShowSettingsWindow(); }));

            root.Children.Add(actions);
            return root;
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
