using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal const double LibraryBrowserFolderPaneSplitMinLeft = 300;
        internal const double LibraryBrowserFolderPaneSplitMinRight = 260;
        internal const double LibraryBrowserFolderPaneSplitterWidth = 12;
        /// <summary>Default width for the Photo-workspace cover rail; sized so two columns + tile margins avoid horizontal scroll at typical DPI.</summary>
        internal const double LibraryBrowserPhotoRailWidth = 308;
        internal const double LibraryBrowserPhotoDividerStripWidth = 28;
        /// <summary>Fixed hero band height in captures view — matches Steam-style headers and avoids unbounded vertical scaling when the window grows.</summary>
        internal const double LibraryPhotoWorkspaceHeroBandHeight = 316;
        /// <summary>Negative top margin pulls title chrome up over the hero so the grid gains vertical space and art shows through the panel.</summary>
        internal const double LibraryPhotoWorkspaceChromeOverlapHeroPixels = 76;

        void ApplyLibraryBrowserLayoutMode(LibraryBrowserPaneRefs panes, LibraryWorkspaceMode workspaceMode)
        {
            var contentGrid = panes == null ? null : panes.LibrarySplitContentGrid;
            if (contentGrid?.ColumnDefinitions == null || contentGrid.ColumnDefinitions.Count < 3) return;

            var colLeft = contentGrid.ColumnDefinitions[0];
            var colSplitter = contentGrid.ColumnDefinitions[1];
            var colRight = contentGrid.ColumnDefinitions[2];
            var isTimeline = workspaceMode == LibraryWorkspaceMode.Timeline;
            var isPhoto = workspaceMode == LibraryWorkspaceMode.Photo;
            var isFolder = workspaceMode == LibraryWorkspaceMode.Folder;
            var hideGameChrome = isTimeline;
            var hideDetailCoverPreview = isTimeline || isPhoto;

            if (panes.LeftPane != null) panes.LeftPane.Visibility = isTimeline ? Visibility.Collapsed : Visibility.Visible;
            if (panes.Splitter != null) panes.Splitter.Visibility = Visibility.Collapsed;
            if (panes.PhotoWorkspaceDividerToggleButton != null)
                panes.PhotoWorkspaceDividerToggleButton.Visibility = isPhoto ? Visibility.Visible : Visibility.Collapsed;
            if (panes.PreviewFrame != null) panes.PreviewFrame.Visibility = hideDetailCoverPreview ? Visibility.Collapsed : Visibility.Visible;
            if (panes.OpenFolderButton != null) panes.OpenFolderButton.Visibility = hideGameChrome ? Visibility.Collapsed : Visibility.Visible;
            if (panes.RefreshThisFolderButton != null) panes.RefreshThisFolderButton.Visibility = hideGameChrome ? Visibility.Collapsed : Visibility.Visible;
            if (panes.PhotoAchievementsButton != null)
                panes.PhotoAchievementsButton.Visibility = hideGameChrome ? Visibility.Collapsed : (isPhoto ? Visibility.Visible : Visibility.Collapsed);
            if (panes.PhotoAchievementsSummary != null)
            {
                if (!isPhoto || hideGameChrome)
                    LibraryBrowserClearAchievementsSummary(panes);
                else if (panes.PhotoAchievementsButton != null && panes.PhotoAchievementsButton.Visibility != Visibility.Visible)
                    panes.PhotoAchievementsSummary.Visibility = Visibility.Collapsed;
            }
            if (panes.ExitTimelineButton != null) panes.ExitTimelineButton.Visibility = isTimeline ? Visibility.Visible : Visibility.Collapsed;
            if (panes.TimelineFilterPanel != null) panes.TimelineFilterPanel.Visibility = isTimeline ? Visibility.Visible : Visibility.Collapsed;
            if (panes.GroupAllButton != null) panes.GroupAllButton.Visibility = isPhoto ? Visibility.Collapsed : Visibility.Visible;
            if (panes.GroupConsoleButton != null) panes.GroupConsoleButton.Visibility = isPhoto ? Visibility.Collapsed : Visibility.Visible;
            if (panes.GroupTimelineButton != null)
                panes.GroupTimelineButton.Visibility = isPhoto ? Visibility.Collapsed : Visibility.Visible;
            if (panes.PhotoWorkspaceHeroBannerStrip != null)
                panes.PhotoWorkspaceHeroBannerStrip.Visibility = isPhoto ? Visibility.Visible : Visibility.Collapsed;
            if (panes.PhotoWorkspaceHeaderMenuHit != null)
                panes.PhotoWorkspaceHeaderMenuHit.Visibility = isPhoto ? Visibility.Visible : Visibility.Collapsed;

            if (isTimeline)
            {
                if (panes.RightPane != null) panes.RightPane.Visibility = Visibility.Visible;
                colLeft.MinWidth = 0;
                colLeft.Width = new GridLength(0);
                colLeft.ClearValue(ColumnDefinition.MaxWidthProperty);
                colSplitter.Width = new GridLength(0);
                colSplitter.MinWidth = 0;
                colRight.MinWidth = LibraryBrowserFolderPaneSplitMinRight;
                colRight.Width = new GridLength(1, GridUnitType.Star);
                ApplyLibraryPhotoDetailChromeLayout(panes, false);
                LibraryBrowserSyncOpenCapturesToolbarButton(panes);
                return;
            }

            if (isPhoto)
            {
                if (panes.RightPane != null) panes.RightPane.Visibility = Visibility.Visible;
                colLeft.Width = new GridLength(LibraryBrowserPhotoRailWidth, GridUnitType.Pixel);
                colLeft.MinWidth = 160;
                colLeft.MaxWidth = 400;
                colSplitter.Width = new GridLength(LibraryBrowserPhotoDividerStripWidth, GridUnitType.Pixel);
                colSplitter.MinWidth = LibraryBrowserPhotoDividerStripWidth;
                colRight.MinWidth = LibraryBrowserFolderPaneSplitMinRight;
                colRight.Width = new GridLength(1, GridUnitType.Star);
                if (panes.LibraryFooterCommandsPanel != null) panes.LibraryFooterCommandsPanel.Visibility = Visibility.Collapsed;
                if (panes.LibraryFooterStatusLine != null) panes.LibraryFooterStatusLine.Visibility = Visibility.Collapsed;
                if (panes.PhotoRailColumnPickerHost != null) panes.PhotoRailColumnPickerHost.Visibility = Visibility.Visible;
                _libraryBrowserLiveWorkingSet?.RefreshPhotoRailColumnPickerUi?.Invoke();
                if (panes.SortFilterMenuButton != null)
                {
                    Grid.SetColumn(panes.SortFilterMenuButton, 0);
                    Grid.SetColumnSpan(panes.SortFilterMenuButton, 6);
                    panes.SortFilterMenuButton.HorizontalAlignment = HorizontalAlignment.Center;
                    panes.SortFilterMenuButton.Margin = new Thickness(0, 0, 0, 0);
                }
                ApplyLibraryPhotoDetailChromeLayout(panes, true);
                LibraryBrowserSyncOpenCapturesToolbarButton(panes);
                return;
            }

            if (isFolder)
            {
                if (panes.RightPane != null) panes.RightPane.Visibility = Visibility.Collapsed;
                colLeft.MinWidth = LibraryBrowserFolderPaneSplitMinLeft;
                colLeft.Width = new GridLength(1, GridUnitType.Star);
                colLeft.ClearValue(ColumnDefinition.MaxWidthProperty);
                colSplitter.Width = new GridLength(0);
                colSplitter.MinWidth = 0;
                colRight.MinWidth = 0;
                colRight.Width = new GridLength(0);
                if (panes.LibraryFooterCommandsPanel != null) panes.LibraryFooterCommandsPanel.Visibility = Visibility.Visible;
                if (panes.LibraryFooterStatusLine != null) panes.LibraryFooterStatusLine.Visibility = Visibility.Visible;
                if (panes.PhotoRailColumnPickerHost != null) panes.PhotoRailColumnPickerHost.Visibility = Visibility.Collapsed;
                if (panes.SortFilterMenuButton != null)
                {
                    Grid.SetColumnSpan(panes.SortFilterMenuButton, 1);
                    Grid.SetColumn(panes.SortFilterMenuButton, 0);
                    panes.SortFilterMenuButton.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
                    panes.SortFilterMenuButton.Margin = new Thickness(0, 0, 8, 0);
                }
                ApplyLibraryPhotoDetailChromeLayout(panes, false);
                if (panes.GroupAllButton != null) panes.GroupAllButton.Visibility = Visibility.Visible;
                if (panes.GroupConsoleButton != null) panes.GroupConsoleButton.Visibility = Visibility.Visible;
            }

            LibraryBrowserSyncOpenCapturesToolbarButton(panes);
        }

        /// <summary>Tighter right-pane chrome in Photo workspace after removing the cover preview — more room for the capture grid.</summary>
        void ApplyLibraryPhotoDetailChromeLayout(LibraryBrowserPaneRefs panes, bool photoWorkspaceCompact)
        {
            if (panes?.RightPane == null) return;
            void ClearPhotoReadabilityChrome()
            {
                if (panes.PhotoWorkspaceTitleReadabilityBorder != null) panes.PhotoWorkspaceTitleReadabilityBorder.Background = Brushes.Transparent;
            }

            if (photoWorkspaceCompact)
            {
                panes.RightPane.Padding = new Thickness(16, 10, 16, 12);
                if (panes.LibraryDetailBanner != null) panes.LibraryDetailBanner.Margin = new Thickness(0, 0, 0, 4);
                if (panes.LibraryDetailControlsDock != null) panes.LibraryDetailControlsDock.Margin = new Thickness(0, 2, 0, 6);
                if (panes.DetailMeta != null) panes.DetailMeta.Margin = new Thickness(0, 4, 0, 6);
                if (panes.DetailTitle != null) panes.DetailTitle.FontSize = 22;
                if (panes.LibraryDetailBannerGrid != null && panes.LibraryDetailBannerGrid.ColumnDefinitions.Count > 1)
                {
                    var chromeCol = panes.LibraryDetailBannerGrid.ColumnDefinitions[1];
                    chromeCol.MinWidth = 0;
                }
                if (panes.PhotoWorkspaceTitleReadabilityBorder != null)
                {
                    var chrome = panes.PhotoWorkspaceTitleReadabilityBorder;
                    chrome.Background = new SolidColorBrush(Color.FromArgb(0xA8, 0x12, 0x1A, 0x22));
                    chrome.VerticalAlignment = VerticalAlignment.Bottom;
                    chrome.HorizontalAlignment = HorizontalAlignment.Stretch;
                    chrome.Margin = new Thickness(0, -LibraryPhotoWorkspaceChromeOverlapHeroPixels, 0, 0);
                    Grid.SetRow(chrome, 0);
                    Grid.SetColumn(chrome, 0);
                    Grid.SetColumnSpan(chrome, 2);
                    Panel.SetZIndex(chrome, 4);
                }
                if (panes.PreviewFrame != null)
                {
                    Grid.SetRow(panes.PreviewFrame, 0);
                    Grid.SetColumn(panes.PreviewFrame, 0);
                    Grid.SetColumnSpan(panes.PreviewFrame, 1);
                }
                if (panes.LibraryDetailBannerGrid != null && panes.LibraryDetailBannerGrid.ColumnDefinitions.Count > 0)
                {
                    var c0 = panes.LibraryDetailBannerGrid.ColumnDefinitions[0];
                    c0.MinWidth = 0;
                    c0.MaxWidth = 0;
                    c0.Width = new GridLength(0);
                }
            }
            else
            {
                ClearPhotoReadabilityChrome();
                panes.RightPane.Padding = new Thickness(26, 22, 26, 18);
                if (panes.LibraryDetailBanner != null) panes.LibraryDetailBanner.Margin = new Thickness(0, 0, 0, 18);
                if (panes.LibraryDetailControlsDock != null) panes.LibraryDetailControlsDock.Margin = new Thickness(0, 4, 0, 14);
                if (panes.DetailMeta != null) panes.DetailMeta.Margin = new Thickness(0, 8, 0, 14);
                if (panes.DetailTitle != null) panes.DetailTitle.FontSize = 28;
                if (panes.LibraryDetailBannerGrid != null && panes.LibraryDetailBannerGrid.ColumnDefinitions.Count > 1)
                {
                    var chromeCol = panes.LibraryDetailBannerGrid.ColumnDefinitions[1];
                    chromeCol.MinWidth = 140;
                }
                if (panes.PhotoWorkspaceTitleReadabilityBorder != null)
                {
                    var chrome = panes.PhotoWorkspaceTitleReadabilityBorder;
                    chrome.VerticalAlignment = VerticalAlignment.Top;
                    chrome.HorizontalAlignment = HorizontalAlignment.Stretch;
                    chrome.Margin = new Thickness(0, 10, 0, 0);
                    Grid.SetRow(chrome, 0);
                    Grid.SetColumn(chrome, 1);
                    Grid.SetColumnSpan(chrome, 1);
                    Panel.SetZIndex(chrome, 2);
                }
                if (panes.PreviewFrame != null)
                {
                    Grid.SetRow(panes.PreviewFrame, 0);
                    Grid.SetColumn(panes.PreviewFrame, 0);
                    Grid.SetColumnSpan(panes.PreviewFrame, 1);
                }
                if (panes.LibraryDetailBannerGrid != null && panes.LibraryDetailBannerGrid.ColumnDefinitions.Count > 0)
                {
                    var c0 = panes.LibraryDetailBannerGrid.ColumnDefinitions[0];
                    c0.MinWidth = 96;
                    c0.MaxWidth = 240;
                    c0.Width = GridLength.Auto;
                }
            }
        }

        void LibraryBrowserSyncOpenCapturesToolbarButton(LibraryBrowserPaneRefs panes)
        {
            if (panes?.OpenCapturesButton == null || _libraryBrowserLiveWorkingSet == null) return;
            var ws = _libraryBrowserLiveWorkingSet;
            var timelineGrouping = IsLibraryBrowserTimelineMode();
            var folderOnly = ws.WorkspaceMode == LibraryWorkspaceMode.Folder;
            panes.OpenCapturesButton.Visibility = folderOnly ? Visibility.Visible : Visibility.Collapsed;
            panes.OpenCapturesButton.IsEnabled = folderOnly
                && !timelineGrouping
                && ws.Current != null
                && !IsLibraryBrowserTimelineView(ws.Current)
                && !ws.LibraryFoldersLoading;
        }

        void ApplyLibraryBrowserFolderPaneSplit(Grid contentGrid)
        {
            if (contentGrid?.ColumnDefinitions == null || contentGrid.ColumnDefinitions.Count < 3) return;
            if (IsLibraryBrowserTimelineMode())
            {
                contentGrid.ColumnDefinitions[0].MinWidth = 0;
                contentGrid.ColumnDefinitions[0].Width = new GridLength(0);
                contentGrid.ColumnDefinitions[1].Width = new GridLength(0);
                contentGrid.ColumnDefinitions[2].MinWidth = LibraryBrowserFolderPaneSplitMinRight;
                contentGrid.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
                return;
            }
            if (_libraryBrowserLiveWorkingSet != null
                && (_libraryBrowserLiveWorkingSet.WorkspaceMode == LibraryWorkspaceMode.Folder
                    || _libraryBrowserLiveWorkingSet.WorkspaceMode == LibraryWorkspaceMode.Photo))
                return;
            var colLeft = contentGrid.ColumnDefinitions[0];
            var colRight = contentGrid.ColumnDefinitions[2];
            var saved = _libraryBrowserPersistedFolderPaneWidth;
            if (saved <= 0.5)
            {
                colLeft.Width = new GridLength(1, GridUnitType.Star);
                colLeft.MinWidth = LibraryBrowserFolderPaneSplitMinLeft;
                colRight.Width = new GridLength(3, GridUnitType.Star);
                colRight.MinWidth = LibraryBrowserFolderPaneSplitMinRight;
                return;
            }
            var total = contentGrid.ActualWidth;
            if (total <= LibraryBrowserFolderPaneSplitterWidth + LibraryBrowserFolderPaneSplitMinLeft + LibraryBrowserFolderPaneSplitMinRight)
                return;
            var clamped = ClampLibraryBrowserFolderPaneWidth(contentGrid, saved);
            colLeft.Width = new GridLength(clamped, GridUnitType.Pixel);
            colLeft.MinWidth = LibraryBrowserFolderPaneSplitMinLeft;
            colRight.Width = new GridLength(1, GridUnitType.Star);
            colRight.MinWidth = LibraryBrowserFolderPaneSplitMinRight;
        }

        double ClampLibraryBrowserFolderPaneWidth(Grid grid, double requested)
        {
            var total = grid == null ? 0 : grid.ActualWidth;
            if (total <= LibraryBrowserFolderPaneSplitterWidth + LibraryBrowserFolderPaneSplitMinLeft + LibraryBrowserFolderPaneSplitMinRight)
                return Math.Max(LibraryBrowserFolderPaneSplitMinLeft, requested);
            var maxLeft = total - LibraryBrowserFolderPaneSplitterWidth - LibraryBrowserFolderPaneSplitMinRight;
            return Math.Max(LibraryBrowserFolderPaneSplitMinLeft, Math.Min(maxLeft, requested));
        }

        void PersistLibraryBrowserFolderPaneWidthFromGrid(Grid contentGrid)
        {
            if (contentGrid?.ColumnDefinitions == null || contentGrid.ColumnDefinitions.Count < 3) return;
            if (IsLibraryBrowserTimelineMode()) return;
            if (_libraryBrowserLiveWorkingSet != null
                && (_libraryBrowserLiveWorkingSet.WorkspaceMode == LibraryWorkspaceMode.Folder
                    || _libraryBrowserLiveWorkingSet.WorkspaceMode == LibraryWorkspaceMode.Photo))
                return;
            var colLeft = contentGrid.ColumnDefinitions[0];
            double w;
            if (colLeft.Width.IsAbsolute)
                w = colLeft.Width.Value;
            else
            {
                FrameworkElement leftChild = null;
                foreach (UIElement c in contentGrid.Children)
                {
                    if (Grid.GetColumn(c) != 0 || !(c is FrameworkElement fe)) continue;
                    leftChild = fe;
                    break;
                }
                w = leftChild == null ? 0 : leftChild.ActualWidth;
            }
            if (w < LibraryBrowserFolderPaneSplitMinLeft - 1) return;
            var clamped = ClampLibraryBrowserFolderPaneWidth(contentGrid, w);
            _libraryBrowserPersistedFolderPaneWidth = clamped;
            SaveSettings();
        }

        void LibraryBrowserFolderSplitClampAfterResize(Grid contentGrid)
        {
            if (contentGrid == null || _libraryBrowserPersistedFolderPaneWidth <= 0.5) return;
            if (IsLibraryBrowserTimelineMode()) return;
            if (_libraryBrowserLiveWorkingSet != null
                && (_libraryBrowserLiveWorkingSet.WorkspaceMode == LibraryWorkspaceMode.Folder
                    || _libraryBrowserLiveWorkingSet.WorkspaceMode == LibraryWorkspaceMode.Photo))
                return;
            var col0 = contentGrid.ColumnDefinitions[0];
            if (!col0.Width.IsAbsolute) return;
            var clamped = ClampLibraryBrowserFolderPaneWidth(contentGrid, col0.Width.Value);
            if (Math.Abs(clamped - col0.Width.Value) < 1) return;
            col0.Width = new GridLength(clamped, GridUnitType.Pixel);
            _libraryBrowserPersistedFolderPaneWidth = clamped;
            SaveSettings();
        }
    }
}
