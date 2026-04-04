using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserRefreshFoldersAsync(Window libraryWindow, LibraryBrowserWorkingSet ws, bool forceRefresh, Action renderTiles)
        {
            var refreshVersion = ++ws.LibraryFolderRefreshVersion;
            var loadingStatusText = forceRefresh || ws.Folders.Count > 0
                ? "Refreshing library folders..."
                : "Loading library folders...";
            ws.LibraryFoldersLoading = true;
            if (status != null) status.Text = loadingStatusText;
            if (renderTiles != null) renderTiles();
            Task.Factory.StartNew(delegate
            {
                return librarySession.LoadLibraryFoldersCached(forceRefresh);
            }).ContinueWith(delegate(Task<List<LibraryFolderInfo>> loadTask)
            {
                libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (refreshVersion != ws.LibraryFolderRefreshVersion) return;
                    ws.LibraryFoldersLoading = false;
                    if (loadTask.IsFaulted)
                    {
                        var flattened = loadTask.Exception == null ? null : loadTask.Exception.Flatten();
                        var loadError = flattened == null ? new Exception("Library load failed.") : flattened.InnerExceptions.First();
                        if (status != null && string.Equals(status.Text, loadingStatusText, StringComparison.Ordinal)) status.Text = "Library load failed";
                        Log(loadError.ToString());
                        if (renderTiles != null) renderTiles();
                        MessageBox.Show(loadError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    ws.Folders.Clear();
                    if (loadTask.Status == TaskStatus.RanToCompletion && loadTask.Result != null)
                        ws.Folders.AddRange(loadTask.Result);
                    if (status != null && string.Equals(status.Text, loadingStatusText, StringComparison.Ordinal)) status.Text = "Library ready";
                    if (renderTiles != null) renderTiles();
                }));
            }, TaskScheduler.Default);
        }

        void LibraryBrowserPrefillFoldersFromSnapshot(Window libraryWindow, LibraryBrowserWorkingSet ws, Action renderTiles)
        {
            Task.Factory.StartNew(delegate
            {
                return librarySession.LoadLibraryFolderCacheSnapshot();
            }).ContinueWith(delegate(Task<List<LibraryFolderInfo>> snapshotTask)
            {
                libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (snapshotTask.IsFaulted || ws.Folders.Count > 0) return;
                    var snapshotFolders = snapshotTask.Status == TaskStatus.RanToCompletion && snapshotTask.Result != null
                        ? snapshotTask.Result
                        : null;
                    if (snapshotFolders == null || snapshotFolders.Count == 0) return;
                    ws.Folders.Clear();
                    ws.Folders.AddRange(snapshotFolders);
                    if (status != null) status.Text = "Library ready";
                    if (renderTiles != null) renderTiles();
                }));
            }, TaskScheduler.Default);
        }

        void LibraryBrowserRunFolderMetadataScan(
            Window libraryWindow,
            LibraryBrowserWorkingSet ws,
            string folderPath,
            bool forceRescan,
            Action<bool> setLibraryBusyState,
            Action<bool> refreshLibraryFoldersAsync)
        {
            librarySession.RunLibraryMetadataScan(libraryWindow, folderPath, forceRescan, setLibraryBusyState, delegate
            {
                if (string.IsNullOrWhiteSpace(folderPath)) ws.Current = null;
                else
                {
                    var prev = CloneLibraryBrowserFolderView(ws.Current);
                    ws.Current = new LibraryBrowserFolderView
                    {
                        ViewKey = prev == null ? string.Empty : prev.ViewKey,
                        GameId = prev == null ? string.Empty : prev.GameId,
                        Name = prev == null ? string.Empty : prev.Name ?? string.Empty,
                        PrimaryFolderPath = folderPath,
                        PrimaryPlatformLabel = prev == null ? string.Empty : prev.PrimaryPlatformLabel ?? string.Empty,
                        PlatformLabels = prev == null || prev.PlatformLabels == null ? new string[0] : prev.PlatformLabels.ToArray(),
                        PlatformSummaryText = prev == null ? string.Empty : prev.PlatformSummaryText ?? string.Empty
                    };
                }
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            });
        }
    }
}
