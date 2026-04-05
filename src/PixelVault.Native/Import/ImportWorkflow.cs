using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

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

    }
}
