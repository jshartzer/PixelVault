using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void ShowLibraryMetadataScanWindow(Window owner, string root, string folderPath, bool forceRescan, Action<bool> setBusyState = null, Action onSuccess = null)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show("Library folder not found. Check Settings before running a metadata rebuild.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Window progressWindow = null;
            TextBlock progressMeta = null;
            ProgressBar progressBar = null;
            Action<string> appendProgress = null;
            Button actionButton = null;
            bool scanFinished = false;
            CancellationTokenSource scanCancellation = null;
            Action finishButtons = delegate
            {
                if (setBusyState != null) setBusyState(false);
                Mouse.OverrideCursor = null;
            };

            try
            {
                var resolvedOwner = owner != null && owner.IsVisible ? owner : ResolveStatusWindowOwner();
                var scopeLabel = string.IsNullOrWhiteSpace(folderPath)
                    ? (forceRescan ? "full library rebuild" : "full library refresh")
                    : ((Path.GetFileName(folderPath) ?? "selected folder") + (forceRescan ? " rebuild" : " refresh"));
                var scanHeading = string.IsNullOrWhiteSpace(folderPath)
                    ? (forceRescan ? "Rebuilding library metadata index" : "Refreshing library metadata index")
                    : (forceRescan ? "Rebuilding folder metadata index" : "Refreshing folder metadata index");
                actionButton = Btn("Cancel Scan", null, "#7A2F2F", Brushes.White);
                var scanProgressView = WorkflowProgressWindow.Create(
                    resolvedOwner,
                    "PixelVault Scan Monitor",
                    scanHeading,
                    "Building file list...",
                    0,
                    1,
                    0,
                    true,
                    actionButton,
                    WorkflowProgressWindow.ScanStyleMaxLogLines);
                progressWindow = scanProgressView.Window;
                progressMeta = scanProgressView.MetaText;
                progressBar = scanProgressView.ProgressBar;
                appendProgress = scanProgressView.AppendLogLine;
                actionButton.Click += delegate
                {
                    if (!scanFinished)
                    {
                        if (scanCancellation != null && !scanCancellation.IsCancellationRequested) scanCancellation.Cancel();
                        actionButton.IsEnabled = false;
                        if (progressMeta != null) progressMeta.Text = "Cancel requested. Stopping the current metadata read...";
                        appendProgress("Cancel requested. Stopping the current metadata read.");
                    }
                    else if (progressWindow != null)
                    {
                        progressWindow.Close();
                    }
                };
                progressWindow.Show();

                appendProgress("Starting scan for " + scopeLabel + ".");
                if (status != null)
                {
                    status.Text = string.IsNullOrWhiteSpace(folderPath)
                        ? (forceRescan ? "Rebuilding library metadata index" : "Refreshing library metadata index")
                        : (forceRescan ? "Rebuilding folder metadata index" : "Refreshing folder metadata index");
                }
                if (setBusyState != null) setBusyState(true);
                Mouse.OverrideCursor = Cursors.Wait;

                var capturedFolderPath = folderPath;
                var capturedForceRescan = forceRescan;
                scanCancellation = new CancellationTokenSource();
                libraryScanner.ScanLibraryMetadataIndexAsync(root, capturedFolderPath, capturedForceRescan, delegate(int currentCount, int totalCount, string detail)
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
                            progressMeta.Text = currentCount + " of " + totalCount + " processed | " + remaining + " remaining";
                        }
                        else
                        {
                            progressMeta.Text = detail;
                        }
                        appendProgress(detail);
                    }));
                }, scanCancellation.Token).ContinueWith(delegate(Task<int> scanTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        scanFinished = true;
                        if (scanCancellation != null)
                        {
                            scanCancellation.Dispose();
                            scanCancellation = null;
                        }
                        finishButtons();
                        if (scanTask.IsCanceled || (scanTask.IsFaulted && scanTask.Exception != null && scanTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                        {
                            if (status != null) status.Text = "Library scan cancelled";
                            if (progressMeta != null) progressMeta.Text = "Scan cancelled before completion.";
                            appendProgress("Scan cancelled.");
                        }
                        else if (scanTask.IsFaulted)
                        {
                            if (status != null) status.Text = "Library scan failed";
                            var flattened = scanTask.Exception == null ? null : scanTask.Exception.Flatten();
                            var scanError = flattened == null ? new Exception("Library scan failed.") : flattened.InnerExceptions.First();
                            if (progressMeta != null) progressMeta.Text = scanError.Message;
                            appendProgress("ERROR: " + scanError.Message);
                            Log(scanError.ToString());
                            MessageBox.Show(scanError.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        else
                        {
                            if (status != null)
                            {
                                status.Text = string.IsNullOrWhiteSpace(folderPath)
                                    ? (forceRescan ? "Library metadata index rebuilt" : "Library metadata index refreshed")
                                    : (forceRescan ? "Folder metadata index rebuilt" : "Folder metadata index refreshed");
                            }
                            if (progressMeta != null) progressMeta.Text += " | complete";
                            appendProgress("Scan finished successfully.");
                            if (onSuccess != null) onSuccess();
                        }
                        if (actionButton != null)
                        {
                            actionButton.IsEnabled = true;
                            actionButton.Content = "Close";
                        }
                    }));
                }, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                scanFinished = true;
                if (scanCancellation != null)
                {
                    scanCancellation.Dispose();
                    scanCancellation = null;
                }
                finishButtons();
                if (status != null) status.Text = "Library scan failed";
                Log(ex.ToString());
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
