using System;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal const double LibraryBrowserFolderPaneSplitMinLeft = 300;
        internal const double LibraryBrowserFolderPaneSplitMinRight = 260;
        internal const double LibraryBrowserFolderPaneSplitterWidth = 12;
        internal const double LibraryBrowserPhotoRailWidth = 268;
        internal const double LibraryBrowserPhotoDividerStripWidth = 28;

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

            if (panes.LeftPane != null) panes.LeftPane.Visibility = isTimeline ? Visibility.Collapsed : Visibility.Visible;
            if (panes.Splitter != null) panes.Splitter.Visibility = Visibility.Collapsed;
            if (panes.PhotoWorkspaceDividerToggleButton != null)
                panes.PhotoWorkspaceDividerToggleButton.Visibility = isPhoto ? Visibility.Visible : Visibility.Collapsed;
            if (panes.PreviewFrame != null) panes.PreviewFrame.Visibility = hideGameChrome ? Visibility.Collapsed : Visibility.Visible;
            if (panes.OpenFolderButton != null) panes.OpenFolderButton.Visibility = hideGameChrome ? Visibility.Collapsed : Visibility.Visible;
            if (panes.RefreshThisFolderButton != null) panes.RefreshThisFolderButton.Visibility = hideGameChrome ? Visibility.Collapsed : Visibility.Visible;
            if (panes.ExitTimelineButton != null) panes.ExitTimelineButton.Visibility = isTimeline ? Visibility.Visible : Visibility.Collapsed;
            if (panes.ExitPhotoWorkspaceButton != null) panes.ExitPhotoWorkspaceButton.Visibility = isPhoto ? Visibility.Visible : Visibility.Collapsed;
            if (panes.TimelineFilterPanel != null) panes.TimelineFilterPanel.Visibility = isTimeline ? Visibility.Visible : Visibility.Collapsed;
            if (panes.PhotoCaptureLayoutButton != null)
                panes.PhotoCaptureLayoutButton.Visibility = isPhoto ? Visibility.Visible : Visibility.Collapsed;

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
            }

            LibraryBrowserSyncOpenCapturesToolbarButton(panes);
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
