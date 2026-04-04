using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Scoped cover refresh with progress UI; used from Library browser show orchestration.</summary>
        void RunLibraryBrowserScopedCoverRefresh(
            Window libraryWindow,
            LibraryBrowserWorkingSet ws,
            List<LibraryFolderInfo> requestedFolders,
            string scopeLabel,
            bool forceRefreshExistingCovers,
            bool rebuildFullCacheAfterRefresh,
            bool reloadLibraryFolderListAfter,
            Action repaintLibraryBrowserChrome,
            Action<bool> refreshLibraryFoldersAsync,
            Action<bool> setLibraryBusyState)
        {
            var targetFolders = (requestedFolders ?? new List<LibraryFolderInfo>()).Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.FolderPath)).ToList();
            if (targetFolders.Count == 0)
            {
                MessageBox.Show("No library folder is available for cover refresh.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var resolvedScopeLabel = string.IsNullOrWhiteSpace(scopeLabel) ? (targetFolders.Count == 1 ? "selected folder" : "library") : scopeLabel.Trim();
            Window progressWindow = null;
            TextBlock progressMeta = null;
            ProgressBar progressBar = null;
            Action<string> appendProgress = null;
            Button actionButton = null;
            var refreshFinished = false;
            CancellationTokenSource refreshCancellation = null;
            Action finishButtons = delegate
            {
                if (setLibraryBusyState != null) setLibraryBusyState(false);
                System.Windows.Input.Mouse.OverrideCursor = null;
            };
            try
            {
                actionButton = Btn("Cancel Refresh", null, "#7A2F2F", Brushes.White);
                var coverRefreshView = WorkflowProgressWindow.Create(
                    libraryWindow,
                    "PixelVault Cover Refresh",
                    "Resolving IDs and fetching cover art",
                    "Preparing library entries...",
                    0,
                    1,
                    0,
                    true,
                    actionButton,
                    WorkflowProgressWindow.ScanStyleMaxLogLines);
                progressWindow = coverRefreshView.Window;
                progressMeta = coverRefreshView.MetaText;
                progressBar = coverRefreshView.ProgressBar;
                appendProgress = coverRefreshView.AppendLogLine;
                actionButton.Click += delegate
                {
                    if (!refreshFinished)
                    {
                        if (refreshCancellation != null && !refreshCancellation.IsCancellationRequested) refreshCancellation.Cancel();
                        actionButton.IsEnabled = false;
                        if (progressMeta != null) progressMeta.Text = "Cancel requested. Stopping the current lookup or download...";
                        appendProgress("Cancel requested. Stopping the current lookup or download.");
                    }
                    else if (progressWindow != null)
                    {
                        progressWindow.Close();
                    }
                };
                progressWindow.Show();
                appendProgress("Starting cover refresh for " + resolvedScopeLabel + ".");
                status.Text = targetFolders.Count == 1 ? "Resolving IDs and fetching folder cover art" : "Resolving IDs and fetching cover art";
                if (setLibraryBusyState != null) setLibraryBusyState(true);
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                refreshCancellation = new CancellationTokenSource();
                Task.Run(async () =>
                {
                    var result = await librarySession.RefreshLibraryCoversAsync(ws.Folders, targetFolders, delegate(int currentCount, int totalCount, string detail)
                    {
                        if (progressWindow == null) return;
                        progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (progressBar == null || progressMeta == null) return;
                            progressBar.IsIndeterminate = totalCount <= 0;
                            if (totalCount > 0)
                            {
                                progressBar.Maximum = totalCount;
                                progressBar.Value = Math.Min(currentCount, totalCount);
                                var remaining = Math.Max(totalCount - currentCount, 0);
                                progressMeta.Text = currentCount + " of " + totalCount + " steps complete | " + remaining + " remaining";
                            }
                            else
                            {
                                progressMeta.Text = detail;
                            }
                            appendProgress(detail);
                        }));
                    }, refreshCancellation.Token, forceRefreshExistingCovers, rebuildFullCacheAfterRefresh).ConfigureAwait(false);
                    return new[] { result.resolvedIds, result.coversReady };
                }, refreshCancellation.Token).ContinueWith(delegate(Task<int[]> refreshTask)
                {
                    libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        refreshFinished = true;
                        if (refreshCancellation != null)
                        {
                            refreshCancellation.Dispose();
                            refreshCancellation = null;
                        }
                        finishButtons();
                        if (refreshTask.IsCanceled || (refreshTask.IsFaulted && refreshTask.Exception != null && refreshTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                        {
                            status.Text = targetFolders.Count == 1 ? "Folder cover refresh cancelled" : "Cover refresh cancelled";
                            if (progressMeta != null) progressMeta.Text = "Cover refresh cancelled before completion.";
                            appendProgress("Cover refresh cancelled.");
                        }
                        else if (refreshTask.IsFaulted)
                        {
                            status.Text = targetFolders.Count == 1 ? "Folder cover refresh failed" : "Cover refresh failed";
                            var flattened = refreshTask.Exception == null ? null : refreshTask.Exception.Flatten();
                            var refreshError = flattened == null ? new Exception("Cover refresh failed.") : flattened.InnerExceptions.First();
                            if (progressMeta != null) progressMeta.Text = refreshError.Message;
                            appendProgress("ERROR: " + refreshError.Message);
                            LogException("Library cover refresh", refreshError);
                            MessageBox.Show(refreshError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            var resolved = refreshTask.Result == null || refreshTask.Result.Length < 1 ? 0 : refreshTask.Result[0];
                            var coversReady = refreshTask.Result == null || refreshTask.Result.Length < 2 ? 0 : refreshTask.Result[1];
                            status.Text = targetFolders.Count == 1 ? "Folder cover refresh complete" : "Cover refresh complete";
                            if (progressMeta != null) progressMeta.Text += " | complete";
                            appendProgress("Cover refresh finished successfully.");
                            if (reloadLibraryFolderListAfter)
                            {
                                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
                            }
                            else if (repaintLibraryBrowserChrome != null) repaintLibraryBrowserChrome();
                            ShowLibraryBrowserToast(ws, targetFolders.Count == 1 ? "Cover refreshed" : "Covers refreshed");
                            Log((targetFolders.Count == 1 ? "Folder" : "Library") + " cover art refresh complete for " + resolvedScopeLabel + ". Resolved " + resolved + " external ID entr" + (resolved == 1 ? "y" : "ies") + "; " + coversReady + " title" + (coversReady == 1 ? " has" : "s have") + " cover art ready.");
                        }
                        if (actionButton != null)
                        {
                            actionButton.IsEnabled = true;
                            actionButton.Content = "Close";
                        }
                    }));
                });
            }
            catch (Exception ex)
            {
                refreshFinished = true;
                if (refreshCancellation != null)
                {
                    refreshCancellation.Dispose();
                    refreshCancellation = null;
                }
                finishButtons();
                status.Text = "Cover refresh failed";
                LogException("Library cover refresh", ex);
                if (progressMeta != null) progressMeta.Text = ex.Message;
                if (appendProgress != null) appendProgress("ERROR: " + ex.Message);
                if (actionButton != null)
                {
                    actionButton.IsEnabled = true;
                    actionButton.Content = "Close";
                }
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
