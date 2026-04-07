using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void RunBackgroundWorkflowWithProgress<TResult>(string windowTitle, string progressTitleText, string initialMetaText, string startStatusText, string canceledStatusText, string startLogLine, string failureStatusText, int totalWork, Func<Action<int, string>, CancellationToken, Task<TResult>> backgroundWork, Action<TResult> onSuccess, Action onCanceled = null)
        {
            var effectiveTotalWork = Math.Max(totalWork, 1);
            var closeButton = Btn("Cancel", null, "#334249", Brushes.White);
            var view = WorkflowProgressWindow.Create(
                this,
                windowTitle,
                progressTitleText,
                initialMetaText,
                0,
                effectiveTotalWork,
                0,
                totalWork <= 0,
                closeButton,
                WorkflowProgressWindow.DefaultMaxLogLines);
            var progressWindow = view.Window;
            var progressMeta = view.MetaText;
            var progressBar = view.ProgressBar;
            bool progressFinished = false;
            bool cancellationRequested = false;
            var workflowCancellation = new CancellationTokenSource();
            Action<string> appendProgress = view.AppendLogLine;
            closeButton.Click += delegate
            {
                if (!progressFinished)
                {
                    if (cancellationRequested) return;
                    cancellationRequested = true;
                    workflowCancellation.Cancel();
                    closeButton.IsEnabled = false;
                    progressMeta.Text = "Cancelling workflow...";
                    appendProgress("Cancellation requested.");
                    return;
                }
                progressWindow.Close();
            };

            status.Text = startStatusText;
            appendProgress(startLogLine);
            Action<int, string> reportProgress = delegate(int completed, string detail)
            {
                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (totalWork > 0)
                    {
                        var safeCompleted = Math.Max(0, Math.Min(completed, effectiveTotalWork));
                        var remaining = Math.Max(effectiveTotalWork - safeCompleted, 0);
                        progressBar.IsIndeterminate = false;
                        progressBar.Maximum = effectiveTotalWork;
                        progressBar.Value = safeCompleted;
                        progressMeta.Text = safeCompleted + " of " + effectiveTotalWork + " steps complete | " + remaining + " remaining";
                    }
                    else
                    {
                        progressBar.IsIndeterminate = true;
                        progressMeta.Text = detail;
                    }
                    appendProgress(detail);
                }));
            };

            Task.Run(async () =>
            {
                return await backgroundWork(reportProgress, workflowCancellation.Token).ConfigureAwait(false);
            }, workflowCancellation.Token).ContinueWith(delegate(Task<TResult> workflowTask)
            {
                progressWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    progressFinished = true;
                    closeButton.Content = "Close";
                    closeButton.IsEnabled = true;
                    if (workflowTask.IsCanceled || (workflowTask.IsFaulted && workflowTask.Exception != null && workflowTask.Exception.Flatten().InnerExceptions.Any(ex => ex is OperationCanceledException)))
                    {
                        status.Text = canceledStatusText;
                        progressMeta.Text = "Workflow cancelled.";
                        appendProgress("Workflow cancelled.");
                        if (onCanceled != null) onCanceled();
                        return;
                    }
                    if (workflowTask.IsFaulted)
                    {
                        var flattened = workflowTask.Exception == null ? null : workflowTask.Exception.Flatten();
                        var error = flattened == null ? new Exception(failureStatusText + ".") : flattened.InnerExceptions.First();
                        status.Text = failureStatusText;
                        progressMeta.Text = error.Message;
                        appendProgress("ERROR: " + error.Message);
                        LogException("Import workflow", error);
                        MessageBox.Show(error.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    try
                    {
                        if (totalWork > 0)
                        {
                            progressBar.IsIndeterminate = false;
                            progressBar.Maximum = effectiveTotalWork;
                            progressBar.Value = effectiveTotalWork;
                            progressMeta.Text = effectiveTotalWork + " of " + effectiveTotalWork + " steps complete | 0 remaining";
                        }
                        onSuccess(workflowTask.Result);
                    }
                    catch (Exception ex)
                    {
                        status.Text = failureStatusText;
                        progressMeta.Text = ex.Message;
                        appendProgress("ERROR: " + ex.Message);
                        LogException("Import workflow", ex);
                        MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }));
            }, TaskScheduler.Default);

            progressWindow.ShowDialog();
        }
    }
}
