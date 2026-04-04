using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        sealed class ImportWorkflowExecutionResult
        {
            public RenameStepResult RenameResult;
            public DeleteStepResult DeleteResult;
            public MetadataStepResult MetadataResult;
            public MoveStepResult MoveResult;
            public SortStepResult SortResult;
            public int ManualItemsLeft;
            public bool ManualItemsLeftAreUploadSkips;
        }

        sealed class ManualIntakeExecutionResult
        {
            public RenameStepResult RenameResult;
            public MetadataStepResult MetadataResult;
            public MoveStepResult MoveResult;
            public SortStepResult SortResult;
        }

        int GetMetadataWorkerCount(int workItems)
        {
            if (workItems <= 1) return 1;
            return Math.Max(1, Math.Min(Math.Min(Environment.ProcessorCount, 4), workItems));
        }

        void RunWorkflow(bool withReview)
        {
            try
            {
                EnsureSourceFolders();
                EnsureExifTool();
                Directory.CreateDirectory(destinationRoot);
                var prepStopwatch = Stopwatch.StartNew();
                var renameInventory = importService.BuildSourceInventory(importSearchSubfoldersForRename);
                var inventory = importService.BuildSourceInventory(false);
                var reviewItems = BuildReviewItems(inventory.TopLevelMediaFiles);
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                prepStopwatch.Stop();
                List<ManualMetadataItem> unifiedImportBatch = null;
                if (withReview)
                {
                    var analysis = AnalyzeIntakePreviewFiles(inventory.TopLevelMediaFiles);
                    var importEditItems = BuildImportAndEditMetadataItems(inventory.TopLevelMediaFiles, analysis);
                    if (importEditItems.Count > 0)
                    {
                        status.Text = "Import and edit";
                        Log("Opening import and edit window for " + importEditItems.Count + " upload file(s).");
                        if (!ShowManualMetadataWindow(importEditItems, false, "Import and comment", true))
                        {
                            status.Text = "Import canceled";
                            Log("Import canceled from import and edit window.");
                            RefreshPreview();
                            return;
                        }
                        unifiedImportBatch = importEditItems;
                    }
                    else
                    {
                        Log("No upload files for import and edit. Continuing import.");
                    }
                }
                var useUnifiedImportBatch = unifiedImportBatch != null;
                var topAtStart = inventory.TopLevelMediaFiles.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var manualLeftOverride = useUnifiedImportBatch
                    ? (int?)Math.Max(0, topAtStart.Count - unifiedImportBatch.Count)
                    : null;
                LogPerformanceSample("ImportPreparation", prepStopwatch, "workflow=" + (withReview ? "import+comment" : "import") + "; renameScope=" + renameInventory.RenameScopeFiles.Count + "; topLevel=" + inventory.TopLevelMediaFiles.Count + "; reviewItems=" + reviewItems.Count + "; manualItems=" + manualItems.Count + "; unifiedImport=" + useUnifiedImportBatch, 40);
                RunImportWorkflowWithProgress(withReview, useUnifiedImportBatch, renameInventory, inventory, reviewItems, useUnifiedImportBatch ? unifiedImportBatch : manualItems, manualPaths, manualLeftOverride);
            }
            catch (Exception ex)
            {
                status.Text = "Workflow failed";
                LogException("Import workflow", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void OpenManualIntakeWindow()
        {
            try
            {
                EnsureSourceFolders();
                EnsureExifTool();
                Directory.CreateDirectory(destinationRoot);
                var prepStopwatch = Stopwatch.StartNew();
                var inventory = importService.BuildSourceInventory(false);
                var recognizedPaths = new HashSet<string>(BuildReviewItems(inventory.TopLevelMediaFiles).Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                prepStopwatch.Stop();
                LogPerformanceSample("ManualIntakePreparation", prepStopwatch, "topLevel=" + inventory.TopLevelMediaFiles.Count + "; manualItems=" + manualItems.Count, 40);
                if (manualItems.Count == 0)
                {
                    status.Text = "No manual intake items";
                    Log("Manual intake opened, but no unmatched image files were found.");
                    MessageBox.Show("There are no unmatched intake images waiting for manual metadata.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshPreview();
                    return;
                }

                status.Text = "Manual intake review";
                Log("Opening manual intake window for " + manualItems.Count + " unmatched image(s).");
                if (!ShowManualMetadataWindow(manualItems, false, string.Empty))
                {
                    status.Text = "Manual intake unchanged";
                    Log("Manual intake window closed. Left " + manualItems.Count + " unmatched image(s) unchanged.");
                    RefreshPreview();
                    return;
                }

                RunManualIntakeWorkflowWithProgress(manualItems);
            }
            catch (Exception ex)
            {
                status.Text = "Manual intake failed";
                LogException("Import workflow", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ThrowIfWorkflowCancellationRequested(CancellationToken cancellationToken, string operationLabel)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException((operationLabel ?? "Workflow") + " cancelled.", cancellationToken);
            }
        }

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

        void RunImportWorkflowWithProgress(bool withReview, bool useUnifiedManualImportBatch, SourceInventory renameInventory, SourceInventory inventory, List<ReviewItem> reviewItems, List<ManualMetadataItem> manualItems, HashSet<string> manualPaths, int? manualItemsLeftOverride = null)
        {
            var batch = manualItems ?? new List<ManualMetadataItem>();
            int renameTotal;
            int deleteTotal;
            int metadataTotal;
            int moveTotal;
            int totalWork;
            if (useUnifiedManualImportBatch)
            {
                renameTotal = batch.Count;
                var manualRenameTotal = batch.Count;
                deleteTotal = batch.Count(item => item != null && item.DeleteBeforeProcessing);
                metadataTotal = batch.Count;
                moveTotal = batch.Count(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath));
                totalWork = renameTotal + manualRenameTotal + deleteTotal + metadataTotal + moveTotal + 1;
            }
            else
            {
                renameTotal = renameInventory == null || renameInventory.RenameScopeFiles == null ? 0 : renameInventory.RenameScopeFiles.Count;
                deleteTotal = reviewItems == null ? 0 : reviewItems.Count(item => item != null && item.DeleteBeforeProcessing);
                metadataTotal = reviewItems == null ? 0 : reviewItems.Count;
                moveTotal = inventory == null || inventory.TopLevelMediaFiles == null
                    ? 0
                    : inventory.TopLevelMediaFiles.Count(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file) && (manualPaths == null || !manualPaths.Contains(file)));
                totalWork = renameTotal + deleteTotal + metadataTotal + moveTotal + 1;
            }
            var workflowLabel = withReview ? "import and comment" : "import";

            RunBackgroundWorkflowWithProgress(
                "PixelVault " + AppVersion + " Import Progress",
                withReview ? "Importing captures with review comments" : "Importing captures",
                "Preparing intake workflow...",
                withReview ? "Running import and comment workflow" : "Running import workflow",
                withReview ? "Import and comment canceled" : "Import canceled",
                "Starting " + workflowLabel + " workflow.",
                withReview ? "Import and comment failed" : "Import failed",
                totalWork,
                async (reportProgress, cancellationToken) =>
                {
                    if (useUnifiedManualImportBatch)
                    {
                        var steamRenameTotal = batch.Count;
                        var manualRenameTotal = batch.Count;
                        var delTotal = batch.Count(item => item != null && item.DeleteBeforeProcessing);
                        var metaTotal = batch.Count;
                        var mvTotal = batch.Count(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath));
                        var steamOff = 0;
                        var manualRenameOff = steamOff + steamRenameTotal;
                        var deleteOff = manualRenameOff + manualRenameTotal;
                        var metadataOff = deleteOff + delTotal;
                        var moveOff = metadataOff + metaTotal;
                        var sortOff = moveOff + mvTotal;

                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        var steamRenameResult = await importService.RunSteamRenameAsync(batch.Select(item => item.FilePath), delegate(int current, int total, string detail)
                        {
                            reportProgress(steamOff + current, detail);
                        }, cancellationToken).ConfigureAwait(false);
                        var steamMap = steamRenameResult == null ? null : steamRenameResult.OldPathToNewPath;
                        if (steamMap != null && steamMap.Count > 0) SteamImportRename.ApplySteamRenameMapToManualMetadataItems(batch, steamMap);
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        var manualRenameResult = RunManualRename(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(manualRenameOff + current, detail);
                        }, cancellationToken);
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        var uniDeleteResult = RunDeleteManualMetadata(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(deleteOff + current, detail);
                        }, cancellationToken);
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        var uniMetadataResult = RunManualMetadata(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(metadataOff + current, detail);
                        }, cancellationToken);
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        var uniMoveResult = RunMoveFiles(batch.Select(item => item.FilePath).Where(File.Exists), "Import move summary", delegate(int current, int total, string detail)
                        {
                            reportProgress(moveOff + current, detail);
                        }, cancellationToken);
                        SortStepResult uniSortResult = null;
                        if (uniMoveResult != null && uniMoveResult.Moved > 0)
                        {
                            ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                            importService.SaveUndoManifest(uniMoveResult.Entries);
                            reportProgress(sortOff, "Sorting imported captures into game folders...");
                            uniSortResult = SortDestinationFoldersCore(false, false, cancellationToken);
                        }
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        reportProgress(totalWork, "Import workflow complete.");
                        var combinedRename = new RenameStepResult
                        {
                            Renamed = (steamRenameResult == null ? 0 : steamRenameResult.Renamed) + (manualRenameResult == null ? 0 : manualRenameResult.Renamed),
                            Skipped = (steamRenameResult == null ? 0 : steamRenameResult.Skipped) + (manualRenameResult == null ? 0 : manualRenameResult.Skipped)
                        };
                        return new ImportWorkflowExecutionResult
                        {
                            RenameResult = combinedRename,
                            DeleteResult = uniDeleteResult,
                            MetadataResult = uniMetadataResult,
                            MoveResult = uniMoveResult,
                            SortResult = uniSortResult,
                            ManualItemsLeft = manualItemsLeftOverride ?? 0,
                            ManualItemsLeftAreUploadSkips = true
                        };
                    }

                    var renameOffset = 0;
                    var deleteOffset = renameOffset + renameTotal;
                    var metadataOffset = deleteOffset + deleteTotal;
                    var moveOffset = metadataOffset + metadataTotal;
                    var sortOffset = moveOffset + moveTotal;

                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                    var renameResult = await importService.RunSteamRenameAsync(renameInventory == null ? new List<string>() : renameInventory.RenameScopeFiles, delegate(int current, int total, string detail)
                    {
                        reportProgress(renameOffset + current, detail);
                    }, cancellationToken).ConfigureAwait(false);
                    var steamRenameMap = renameResult == null ? null : renameResult.OldPathToNewPath;
                    if (steamRenameMap != null && steamRenameMap.Count > 0) SteamImportRename.ApplySteamRenameMapToReviewItems(reviewItems, steamRenameMap);
                    var moveSourcePathsAfterRename = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(inventory == null ? null : inventory.TopLevelMediaFiles, steamRenameMap);
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                    var deleteResult = RunDelete(reviewItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(deleteOffset + current, detail);
                    }, cancellationToken);
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                    var metadataResult = RunMetadata(reviewItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(metadataOffset + current, detail);
                    }, cancellationToken);
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                    var moveResult = RunMove(moveSourcePathsAfterRename, manualPaths, delegate(int current, int total, string detail)
                    {
                        reportProgress(moveOffset + current, detail);
                    }, cancellationToken);
                    SortStepResult sortResult = null;
                    if (moveResult != null && moveResult.Moved > 0)
                    {
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                        importService.SaveUndoManifest(moveResult.Entries);
                        reportProgress(sortOffset, "Sorting imported captures into game folders...");
                        sortResult = SortDestinationFoldersCore(false, false, cancellationToken);
                    }
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                    reportProgress(totalWork, "Import workflow complete.");
                    return new ImportWorkflowExecutionResult
                    {
                        RenameResult = renameResult,
                        DeleteResult = deleteResult,
                        MetadataResult = metadataResult,
                        MoveResult = moveResult,
                        SortResult = sortResult,
                        ManualItemsLeft = manualItemsLeftOverride ?? (manualItems == null ? 0 : manualItems.Count),
                        ManualItemsLeftAreUploadSkips = false
                    };
                },
                delegate(ImportWorkflowExecutionResult result)
                {
                    if (result.ManualItemsLeft > 0)
                    {
                        Log(result.ManualItemsLeftAreUploadSkips
                            ? "Left " + result.ManualItemsLeft + " upload file(s) not selected for this import."
                            : "Left " + result.ManualItemsLeft + " unmatched intake image(s) untouched. Use Manual Intake when you want to add missing data.");
                    }
                    RefreshPreview();
                    status.Text = "Workflow complete";
                    Log("Workflow complete.");
                    var summaryLines = BuildImportSummaryLines("Import", withReview, result.RenameResult, result.DeleteResult, result.MetadataResult, result.MoveResult, result.SortResult, result.ManualItemsLeft, result.ManualItemsLeftAreUploadSkips);
                    var movedCount = result.MoveResult == null ? 0 : result.MoveResult.Moved;
                    var metadataUpdated = result.MetadataResult == null ? 0 : result.MetadataResult.Updated;
                    var leftSuffix = result.ManualItemsLeft > 0
                        ? (result.ManualItemsLeftAreUploadSkips ? " | " + result.ManualItemsLeft + " not selected (still in upload)" : " | " + result.ManualItemsLeft + " unmatched left")
                        : string.Empty;
                    var summaryMeta = movedCount + " file(s) imported | " + metadataUpdated + " metadata update(s)" + leftSuffix;
                    ShowImportSummaryWindow(withReview ? "Import and Comment Summary" : "Import Summary", summaryMeta, summaryLines);
                },
                delegate
                {
                    RefreshPreview();
                    Log("Import workflow canceled.");
                });
        }

        void RunManualIntakeWorkflowWithProgress(List<ManualMetadataItem> manualItems)
        {
            var renameTotal = manualItems == null ? 0 : manualItems.Count;
            var metadataTotal = manualItems == null ? 0 : manualItems.Count;
            var moveTotal = manualItems == null ? 0 : manualItems.Count(item => item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath));
            var totalWork = renameTotal + metadataTotal + moveTotal + 1;

            RunBackgroundWorkflowWithProgress(
                "PixelVault " + AppVersion + " Manual Intake Progress",
                "Importing manual intake items",
                "Preparing manual intake workflow...",
                "Running manual intake workflow",
                "Manual intake canceled",
                "Starting manual intake workflow.",
                "Manual intake failed",
                totalWork,
                async (reportProgress, cancellationToken) =>
                {
                    var renameOffset = 0;
                    var metadataOffset = renameOffset + renameTotal;
                    var moveOffset = metadataOffset + metadataTotal;
                    var sortOffset = moveOffset + moveTotal;

                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Manual intake workflow");
                    var renameResult = RunManualRename(manualItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(renameOffset + current, detail);
                    }, cancellationToken);
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Manual intake workflow");
                    var metadataResult = RunManualMetadata(manualItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(metadataOffset + current, detail);
                    }, cancellationToken);
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Manual intake workflow");
                    var moveResult = RunMoveFiles(manualItems.Select(item => item.FilePath), "Manual move summary", delegate(int current, int total, string detail)
                    {
                        reportProgress(moveOffset + current, detail);
                    }, cancellationToken);
                    SortStepResult sortResult = null;
                    if (moveResult != null && moveResult.Moved > 0)
                    {
                        ThrowIfWorkflowCancellationRequested(cancellationToken, "Manual intake workflow");
                        importService.SaveUndoManifest(moveResult.Entries);
                        reportProgress(sortOffset, "Sorting imported captures into game folders...");
                        sortResult = SortDestinationFoldersCore(false, false, cancellationToken);
                    }
                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Manual intake workflow");
                    reportProgress(totalWork, "Manual intake workflow complete.");
                    return new ManualIntakeExecutionResult
                    {
                        RenameResult = renameResult,
                        MetadataResult = metadataResult,
                        MoveResult = moveResult,
                        SortResult = sortResult
                    };
                },
                delegate(ManualIntakeExecutionResult result)
                {
                    RefreshPreview();
                    status.Text = "Manual intake complete";
                    Log("Manual intake workflow complete.");
                    var summaryLines = BuildImportSummaryLines("Manual Intake", false, result.RenameResult, null, result.MetadataResult, result.MoveResult, result.SortResult, 0);
                    var movedCount = result.MoveResult == null ? 0 : result.MoveResult.Moved;
                    var metadataUpdated = result.MetadataResult == null ? 0 : result.MetadataResult.Updated;
                    var summaryMeta = movedCount + " file(s) imported | " + metadataUpdated + " metadata update(s)";
                    ShowImportSummaryWindow("Manual Intake Summary", summaryMeta, summaryLines);
                },
                delegate
                {
                    RefreshPreview();
                    Log("Manual intake workflow canceled.");
                });
        }

        RenameStepResult RunRename()
        {
            return RunRename(importService.BuildSourceInventory(importSearchSubfoldersForRename).RenameScopeFiles);
        }

        RenameStepResult RunRename(IEnumerable<string> sourceFiles, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return importService.RunSteamRename(sourceFiles, progress, cancellationToken);
        }

        RenameStepResult RunManualRename(List<ManualMetadataItem> items, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return importService.RunManualRename(items, progress, cancellationToken);
        }

        MetadataStepResult RunManualMetadata(List<ManualMetadataItem> items, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            int updated = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            var requests = new List<ExifWriteRequest>();
            var itemsToReset = new List<ManualMetadataItem>();
            if (progress != null) progress(0, total, "Starting metadata step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                var file = item.FilePath;
                var remaining = total - (i + 1);
                if (!File.Exists(file))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var effectiveTime = item.UseCustomCaptureTime ? item.CaptureTime : GetLibraryDate(file);
                var preserveFileTimes = !item.UseCustomCaptureTime;
                var writeDateMetadata = ManualMetadataTouchesCaptureTime(item);
                var writeCommentMetadata = ManualMetadataTouchesComment(item);
                var writeTagMetadata = item.ForceTagMetadataWrite || ManualMetadataTouchesTags(item);
                if (!writeDateMetadata && !writeCommentMetadata && !writeTagMetadata)
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | unchanged | " + item.FileName);
                    continue;
                }
                var extraTags = BuildManualMetadataExtraTags(item);
                var changeNotes = new List<string>();
                if (writeDateMetadata) changeNotes.Add("date/time");
                if (writeCommentMetadata) changeNotes.Add("comment");
                if (writeTagMetadata) changeNotes.Add("tags");
                var metadataTarget = effectiveTime.ToString("yyyy-MM-dd HH:mm:ss") + (preserveFileTimes ? " (using filesystem timestamp)" : " (custom)");
                Log("Updating manual metadata: " + item.FileName + " -> " + metadataTarget + " [" + string.Join(", ", changeNotes.ToArray()) + "]");
                var originalCreate = DateTime.MinValue;
                var originalWrite = DateTime.MinValue;
                var restoreFileTimes = writeDateMetadata && preserveFileTimes;
                if (restoreFileTimes)
                {
                    originalCreate = File.GetCreationTime(file);
                    originalWrite = File.GetLastWriteTime(file);
                }
                requests.Add(new ExifWriteRequest
                {
                    FilePath = file,
                    FileName = item.FileName,
                    Arguments = BuildExifArgs(file, effectiveTime, new string[0], extraTags, preserveFileTimes, item.Comment, item.AddPhotographyTag, writeDateMetadata, writeCommentMetadata, writeTagMetadata),
                    RestoreFileTimes = restoreFileTimes,
                    OriginalCreateTime = originalCreate,
                    OriginalWriteTime = originalWrite,
                    SuccessDetail = item.FileName + " [" + string.Join(", ", changeNotes.ToArray()) + "]"
                });
                itemsToReset.Add(item);
            }
            updated = RunExifWriteRequests(requests, total, skipped, progress, cancellationToken);
            foreach (var item in itemsToReset) item.ForceTagMetadataWrite = false;
            if (progress != null) progress(total, total, "Metadata step complete: updated " + updated + ", skipped " + skipped + ".");
            Log("Manual metadata summary: updated " + updated + ", skipped " + skipped + ".");
            return new MetadataStepResult { Updated = updated, Skipped = skipped };
        }

        DeleteStepResult RunDelete(List<ReviewItem> reviewItems, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var paths = (reviewItems ?? new List<ReviewItem>()).Where(i => i != null && i.DeleteBeforeProcessing).Select(i => i.FilePath);
            return importService.DeleteSourceFiles(paths, progress, cancellationToken);
        }

        DeleteStepResult RunDeleteManualMetadata(List<ManualMetadataItem> items, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var paths = (items ?? new List<ManualMetadataItem>()).Where(i => i != null && i.DeleteBeforeProcessing).Select(i => i.FilePath);
            return importService.DeleteSourceFiles(paths, progress, cancellationToken);
        }

        int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return metadataService.RunExifWriteRequests(requests, totalCount, alreadyCompleted, progress, cancellationToken);
        }

        MetadataStepResult RunMetadata(List<ReviewItem> reviewItems, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return importService.WriteMetadataForReviewItems(reviewItems, progress, cancellationToken);
        }

        MoveStepResult RunMove()
        {
            return RunMove(importService.BuildSourceInventory(false).TopLevelMediaFiles, null);
        }

        MoveStepResult RunMove(HashSet<string> skipFiles)
        {
            return RunMove(importService.BuildSourceInventory(false).TopLevelMediaFiles, skipFiles);
        }

        MoveStepResult RunMove(IEnumerable<string> sourceFiles, HashSet<string> skipFiles, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var files = (sourceFiles ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Where(file => skipFiles == null || !skipFiles.Contains(file));
            return RunMoveFiles(files, "Move summary", progress, cancellationToken);
        }

        MoveStepResult RunMoveFiles(IEnumerable<string> files, string summaryLabel, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return importService.MoveFilesToLibraryDestination(files, summaryLabel, progress, cancellationToken);
        }

        string CurrentConflictMode()
        {
            return string.IsNullOrWhiteSpace(importMoveConflictMode) ? "Rename" : importMoveConflictMode;
        }

        void SortDestinationFolders()
        {
            try
            {
                SortDestinationFoldersCore(true);
            }
            catch (Exception ex)
            {
                status.Text = "Sort failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        SortStepResult SortDestinationFoldersCore(bool interactive, bool updateUi = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = importService.SortDestinationRootIntoGameFolders(destinationRoot, libraryRoot, cancellationToken);
            if (result.Sorted == 0 && result.FoldersCreated == 0)
            {
                if (updateUi) status.Text = "Nothing to sort";
                if (interactive) MessageBox.Show("There are no root-level media files in the destination folder to sort right now.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return result;
            }
            if (updateUi) status.Text = "Destination sorted";
            if (updateUi) RefreshPreview();
            return result;
        }

        void UndoLastImport()
        {
            try
            {
                var entries = importService.LoadUndoManifest();
                if (entries.Count == 0)
                {
                    status.Text = "Nothing to undo";
                    Log("Undo requested, but there is no saved import manifest.");
                    MessageBox.Show("There is no saved import to undo yet.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(entries.Count + " imported item(s) will be moved back to their source folders. Embedded metadata changes and comments will stay in the files.\n\nContinue?", "Undo Last Import", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.OK) return;

                var undoResult = importService.ExecuteUndoImportMoves(entries);
                libraryScanner.RemoveLibraryMetadataIndexEntries(undoResult.RemovedFromLibraryPaths, libraryRoot);
                importService.SaveUndoManifest(undoResult.RemainingEntries);
                status.Text = undoResult.Moved > 0 ? "Last import undone" : "Undo incomplete";
                Log("Undo summary: moved back " + undoResult.Moved + ", skipped " + undoResult.Skipped + ".");
                RefreshPreview();
            }
            catch (Exception ex)
            {
                status.Text = "Undo failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
