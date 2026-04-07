using System;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal void LibraryBrowserEnterPhotoWorkspace(LibraryBrowserWorkingSet ws, LibraryBrowserFolderView folder, Action<LibraryBrowserFolderView> showFolder)
        {
            if (ws?.Panes == null || folder == null || IsLibraryBrowserTimelineView(folder)) return;
            if (IsLibraryBrowserTimelineMode()) return;
            if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo && SameLibraryBrowserSelection(ws.Current, folder))
                return;
            if (ws.Panes.TileScroll != null)
            {
                var y = ws.Panes.TileScroll.VerticalOffset;
                if (y > 0.1d)
                {
                    ws.PreservedFolderScrollOffset = Math.Max(0d, y);
                    ws.PreserveFolderScrollOnNextRender = true;
                }
            }
            ws.WorkspaceMode = LibraryWorkspaceMode.Photo;
            showFolder(folder);
            ApplyLibraryBrowserLayoutMode(ws.Panes, ws.WorkspaceMode);
        }

        internal void LibraryBrowserExitPhotoWorkspace(LibraryBrowserWorkingSet ws, Action renderTiles)
        {
            if (ws == null || ws.WorkspaceMode != LibraryWorkspaceMode.Photo) return;
            ws.WorkspaceMode = LibraryWorkspaceMode.Folder;
            ApplyLibraryBrowserLayoutMode(ws.Panes, ws.WorkspaceMode);
            renderTiles?.Invoke();
        }

        internal void LibraryBrowserEnterPhotoWorkspaceFromSelection(LibraryBrowserWorkingSet ws, Action<LibraryBrowserFolderView> showFolder)
        {
            if (ws?.Current == null || showFolder == null) return;
            LibraryBrowserEnterPhotoWorkspace(ws, ws.Current, showFolder);
        }

        /// <summary>
        /// Keeps <see cref="LibraryBrowserWorkingSet.WorkspaceMode"/> aligned with persisted grouping and future Photo entry.
        /// Timeline grouping always forces <see cref="LibraryWorkspaceMode.Timeline"/>. Photo is preserved when grouping changes among non-timeline modes.
        /// </summary>
        internal static void LibraryBrowserSyncWorkspaceModeWithGrouping(LibraryBrowserWorkingSet ws, string normalizedGroupingMode)
        {
            if (ws == null) return;
            if (string.Equals(SettingsService.NormalizeLibraryGroupingMode(normalizedGroupingMode), "timeline", StringComparison.OrdinalIgnoreCase))
            {
                ws.WorkspaceMode = LibraryWorkspaceMode.Timeline;
                return;
            }
            if (ws.WorkspaceMode == LibraryWorkspaceMode.Photo)
                return;
            ws.WorkspaceMode = LibraryWorkspaceMode.Folder;
        }
    }
}
