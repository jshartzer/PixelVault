using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserWirePaneEvents(
            Window libraryWindow,
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Action renderTiles,
            Action renderSelectedFolder,
            Action applySearchFilter)
        {
            panes.SearchDebounceTimer.Tick += delegate
            {
                applySearchFilter();
            };

                panes.DetailResizeDebounceTimer.Tick += delegate
                {
                    panes.DetailResizeDebounceTimer.Stop();
                    if (ws.Current == null) return;
                    var timelineView = IsLibraryBrowserTimelineView(ws.Current);
                    var layout = CalculateResponsiveLibraryDetailLayout(panes.ThumbScroll, true, timelineView);
                    var viewportWidth = ResolveScrollViewerLayoutWidth(panes.ThumbScroll);
                    if (layout.Columns == ws.LastDetailColumns
                        && layout.TileSize == ws.LastDetailTileSize
                        && Math.Abs(viewportWidth - ws.LastDetailViewportWidth) < 24d) return;
                    ws.PreservedDetailScrollOffset = panes.ThumbScroll.VerticalOffset;
                    ws.PreserveDetailScrollOnNextRender = ws.PreservedDetailScrollOffset > 0.1d;
                    renderSelectedFolder();
                };
                panes.FolderResizeDebounceTimer.Tick += delegate
                {
                    panes.FolderResizeDebounceTimer.Stop();
                    var photoRail = ws.WorkspaceMode == LibraryWorkspaceMode.Photo;
                    var layout = CalculateResponsiveLibraryFolderLayout(panes.TileScroll, photoRail);
                    var viewportWidth = ResolveScrollViewerLayoutWidth(panes.TileScroll);
                    if (layout.Columns == ws.LastFolderColumns
                        && layout.TileSize == ws.LastFolderTileSize
                        && Math.Abs(viewportWidth - ws.LastFolderViewportWidth) < 24d) return;
                    ws.PreservedFolderScrollOffset = panes.TileScroll.VerticalOffset;
                    ws.PreserveFolderScrollOnNextRender = ws.PreservedFolderScrollOffset > 0.1d;
                    if (renderTiles != null) renderTiles();
                };
            panes.ThumbScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
            {
                if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1)
                {
                    if (ws.Current != null)
                    {
                        panes.DetailResizeDebounceTimer.Stop();
                        panes.DetailResizeDebounceTimer.Start();
                    }
                }
            };
            panes.TileScroll.SizeChanged += delegate(object sender, SizeChangedEventArgs e)
            {
                if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 1)
                {
                    panes.FolderResizeDebounceTimer.Stop();
                    panes.FolderResizeDebounceTimer.Start();
                }
            };
            panes.TileScroll.PreviewMouseWheel += delegate(object _, MouseWheelEventArgs e)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                e.Handled = true;
                if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo) return;
                libraryFolderTileSize = NormalizeLibraryFolderTileSize(libraryFolderTileSize + (e.Delta > 0 ? 16 : -16));
                SaveSettings();
                if (renderTiles != null) renderTiles();
                ShowLibraryBrowserToast(ws, "Covers: " + libraryFolderTileSize + " px");
            };
            if (panes.ScrollPersistDebounceTimer != null)
            {
                panes.ScrollPersistDebounceTimer.Tick += delegate
                {
                    panes.ScrollPersistDebounceTimer.Stop();
                    PersistLibraryBrowserScrollFromWorkingSet(ws);
                };
                Action scheduleScrollPersist = delegate
                {
                    panes.ScrollPersistDebounceTimer.Stop();
                    panes.ScrollPersistDebounceTimer.Start();
                };
                panes.TileScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
                {
                    if (Math.Abs(e.VerticalChange) > 0.5) scheduleScrollPersist();
                };
                panes.ThumbScroll.ScrollChanged += delegate(object sender, ScrollChangedEventArgs e)
                {
                    if (Math.Abs(e.VerticalChange) > 0.5) scheduleScrollPersist();
                };
            }
            libraryWindow.Closing += delegate
            {
                FlushLibraryBrowserWorkingSetToSettings(ws);
            };
            panes.SearchBox.TextChanged += delegate
            {
                ws.PendingLibrarySearchText = string.IsNullOrWhiteSpace(panes.SearchBox.Text) ? string.Empty : panes.SearchBox.Text.Trim();
                panes.SearchDebounceTimer.Stop();
                if (string.Equals(ws.PendingLibrarySearchText, ws.AppliedLibrarySearchText, StringComparison.OrdinalIgnoreCase)) return;
                panes.SearchDebounceTimer.Start();
            };
            panes.SearchBox.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key != System.Windows.Input.Key.Enter) return;
                ws.PendingLibrarySearchText = string.IsNullOrWhiteSpace(panes.SearchBox.Text) ? string.Empty : panes.SearchBox.Text.Trim();
                if (!string.Equals(ws.PendingLibrarySearchText, ws.AppliedLibrarySearchText, StringComparison.OrdinalIgnoreCase)) applySearchFilter();
                e.Handled = true;
            };
            panes.SearchBox.LostKeyboardFocus += delegate
            {
                ws.PendingLibrarySearchText = string.IsNullOrWhiteSpace(panes.SearchBox.Text) ? string.Empty : panes.SearchBox.Text.Trim();
                if (panes.SearchDebounceTimer.IsEnabled || !string.Equals(ws.PendingLibrarySearchText, ws.AppliedLibrarySearchText, StringComparison.OrdinalIgnoreCase)) applySearchFilter();
            };
        }
    }
}
