using System;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
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
