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

        void ApplyLibraryBrowserFolderPaneSplit(Grid contentGrid)
        {
            if (contentGrid?.ColumnDefinitions == null || contentGrid.ColumnDefinitions.Count < 3) return;
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
