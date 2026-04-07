using System;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void FlushLibraryBrowserWorkingSetToSettings(LibraryBrowserWorkingSet ws)
        {
            if (ws?.Panes == null) return;
            ws.PendingLibrarySearchText = string.IsNullOrWhiteSpace(ws.Panes.SearchBox.Text) ? string.Empty : ws.Panes.SearchBox.Text.Trim();
            if (!string.Equals(ws.AppliedLibrarySearchText, ws.PendingLibrarySearchText, StringComparison.OrdinalIgnoreCase))
            {
                ws.AppliedLibrarySearchText = ws.PendingLibrarySearchText;
                PersistLibraryBrowserCommittedSearch(ws.AppliedLibrarySearchText);
            }
            PersistLibraryBrowserScrollFromWorkingSet(ws);
            if (ws.Panes.LibrarySplitContentGrid != null)
                PersistLibraryBrowserFolderPaneWidthFromGrid(ws.Panes.LibrarySplitContentGrid);
            PersistLibraryBrowserLastSelection(ws.Current);
        }

        void PrepareLibraryBrowserSessionForRebuild()
        {
            if (_libraryBrowserLiveWorkingSet != null)
            {
                FlushLibraryBrowserWorkingSetToSettings(_libraryBrowserLiveWorkingSet);
                _libraryBrowserLiveWorkingSet = null;
            }
        }

        void RegisterLibraryBrowserLiveWorkingSet(LibraryBrowserWorkingSet ws)
        {
            _libraryBrowserLiveWorkingSet = ws;
        }

        void PersistLibraryBrowserScrollFromWorkingSet(LibraryBrowserWorkingSet ws)
        {
            if (ws?.Panes?.TileScroll == null) return;
            _libraryBrowserPersistedFolderScroll = Math.Max(0, ws.Panes.TileScroll.VerticalOffset);
            if (ws.Panes.ThumbScroll != null)
                _libraryBrowserPersistedDetailScroll = Math.Max(0, ws.Panes.ThumbScroll.VerticalOffset);
            SaveSettings();
        }

        void PersistLibraryBrowserCommittedSearch(string text)
        {
            _libraryBrowserPersistedSearch = text ?? string.Empty;
            SaveSettings();
        }

        void PersistLibraryBrowserLastSelection(LibraryBrowserFolderView info)
        {
            _libraryBrowserPersistedLastViewKey = info == null ? string.Empty : (info.ViewKey ?? string.Empty);
            SaveSettings();
        }
    }
}
