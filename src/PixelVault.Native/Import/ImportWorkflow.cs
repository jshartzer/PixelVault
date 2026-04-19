using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        void RunWorkflow(bool withReview)
        {
            var importEditModalForegroundBusy = false;
            try
            {
                EnsureSourceFolders();
                EnsureExifTool();
                fileSystemService.CreateDirectory(destinationRoot);
                var prepStopwatch = Stopwatch.StartNew();
                var inventory = importService.BuildSourceInventory(importSearchSubfoldersForRename);
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
                        BeginForegroundIntakeBusy();
                        importEditModalForegroundBusy = true;
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
                var topAtStart = inventory.TopLevelMediaFiles.Where(fileSystemService.FileExists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var manualLeftOverride = useUnifiedImportBatch
                    ? (int?)Math.Max(0, topAtStart.Count - unifiedImportBatch.Count)
                    : null;
                LogPerformanceSample("ImportPreparation", prepStopwatch, "workflow=" + (withReview ? "import+comment" : "import") + "; includeSubfolders=" + importSearchSubfoldersForRename + "; renameScope=" + inventory.RenameScopeFiles.Count + "; importCandidates=" + inventory.TopLevelMediaFiles.Count + "; reviewItems=" + reviewItems.Count + "; manualItems=" + manualItems.Count + "; unifiedImport=" + useUnifiedImportBatch, 40);
                RunImportWorkflowWithProgress(withReview, useUnifiedImportBatch, inventory, inventory, reviewItems, useUnifiedImportBatch ? unifiedImportBatch : manualItems, manualPaths, manualLeftOverride);
            }
            catch (Exception ex)
            {
                status.Text = "Workflow failed";
                LogException("Import workflow", ex);
                TryLibraryToast(ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                if (importEditModalForegroundBusy) EndForegroundIntakeBusy();
            }
        }

        void OpenManualIntakeWindow()
        {
            var manualIntakeModalForegroundBusy = false;
            try
            {
                EnsureSourceFolders();
                EnsureExifTool();
                fileSystemService.CreateDirectory(destinationRoot);
                var prepStopwatch = Stopwatch.StartNew();
                var inventory = importService.BuildSourceInventory(importSearchSubfoldersForRename);
                var recognizedPaths = new HashSet<string>(BuildReviewItems(inventory.TopLevelMediaFiles).Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                prepStopwatch.Stop();
                LogPerformanceSample("ManualIntakePreparation", prepStopwatch, "includeSubfolders=" + importSearchSubfoldersForRename + "; importCandidates=" + inventory.TopLevelMediaFiles.Count + "; manualItems=" + manualItems.Count, 40);
                if (manualItems.Count == 0)
                {
                    status.Text = "No manual intake items";
                    Log("Manual intake opened, but no unmatched image files were found.");
                    TryLibraryToast("There are no unmatched intake images waiting for manual metadata.");
                    RefreshPreview();
                    return;
                }

                BeginForegroundIntakeBusy();
                manualIntakeModalForegroundBusy = true;
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
                TryLibraryToast(ex.Message, MessageBoxImage.Error);
            }
            finally
            {
                if (manualIntakeModalForegroundBusy) EndForegroundIntakeBusy();
            }
        }

        void RunImportWorkflowWithProgress(bool withReview, bool useUnifiedManualImportBatch, SourceInventory renameInventory, SourceInventory inventory, List<ReviewItem> reviewItems, List<ManualMetadataItem> manualItems, HashSet<string> manualPaths, int? manualItemsLeftOverride = null)
        {
            var batch = manualItems ?? new List<ManualMetadataItem>();
            var unifiedPlan = importService.ComputeUnifiedImportProgressPlan(batch);
            var standardTotals = importService.ComputeStandardImportWorkTotals(renameInventory, reviewItems, inventory, manualPaths);
            var totalWork = useUnifiedManualImportBatch ? unifiedPlan.TotalWork : standardTotals.TotalWork;
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
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var steamRenameResult = await importService.RunSteamRenameAsync(batch.Select(item => item.FilePath), delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.SteamOff + current, detail);
                        }, cancellationToken).ConfigureAwait(false);
                        var steamMap = steamRenameResult == null ? null : steamRenameResult.OldPathToNewPath;
                        if (steamMap != null && steamMap.Count > 0) SteamImportRename.ApplySteamRenameMapToManualMetadataItems(batch, steamMap);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var manualRenameResult = RunManualRename(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.ManualRenameOff + current, detail);
                        }, cancellationToken);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var uniDeleteResult = RunDeleteManualMetadata(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.DeleteOff + current, detail);
                        }, cancellationToken);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var uniMetadataResult = RunManualMetadata(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.MetadataOff + current, detail);
                        }, cancellationToken);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var uniMoveResult = RunMoveFiles(batch.Select(item => item.FilePath).Where(fileSystemService.FileExists), "Import move summary", delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.MoveOff + current, detail);
                        }, cancellationToken);
                        var uniSortResult = SaveUndoAndSortAfterImportMoveIfNeeded(
                            uniMoveResult,
                            unifiedPlan.SortOff,
                            "Import workflow",
                            reportProgress,
                            cancellationToken);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        reportProgress(totalWork, "Import workflow complete.");
                        var combinedRename = ImportWorkflowOrchestration.CombineRenameStepResults(steamRenameResult, manualRenameResult);
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

                    var renameOffset = standardTotals.RenameOffset;
                    var deleteOffset = standardTotals.DeleteOffset;
                    var metadataOffset = standardTotals.MetadataOffset;
                    var moveOffset = standardTotals.MoveOffset;
                    var sortOffset = standardTotals.SortOffset;

                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var renameResult = await importService.RunSteamRenameAsync(renameInventory == null ? new List<string>() : renameInventory.RenameScopeFiles, delegate(int current, int total, string detail)
                    {
                        reportProgress(renameOffset + current, detail);
                    }, cancellationToken).ConfigureAwait(false);
                    var steamRenameMap = renameResult == null ? null : renameResult.OldPathToNewPath;
                    if (steamRenameMap != null && steamRenameMap.Count > 0) SteamImportRename.ApplySteamRenameMapToReviewItems(reviewItems, steamRenameMap);
                    var moveSourcePathsAfterRename = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(inventory == null ? null : inventory.TopLevelMediaFiles, steamRenameMap);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var deleteResult = RunDelete(reviewItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(deleteOffset + current, detail);
                    }, cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var metadataResult = RunMetadata(reviewItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(metadataOffset + current, detail);
                    }, cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var moveResult = RunMove(moveSourcePathsAfterRename, manualPaths, delegate(int current, int total, string detail)
                    {
                        reportProgress(moveOffset + current, detail);
                    }, cancellationToken);
                    var sortResult = SaveUndoAndSortAfterImportMoveIfNeeded(
                        moveResult,
                        sortOffset,
                        "Import workflow",
                        reportProgress,
                        cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
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
            var intakePlan = importService.ComputeManualIntakeProgressPlan(manualItems);
            var totalWork = intakePlan.TotalWork;

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
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var renameResult = RunManualRename(manualItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(intakePlan.RenameOffset + current, detail);
                    }, cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var metadataResult = RunManualMetadata(manualItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(intakePlan.MetadataOffset + current, detail);
                    }, cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var moveResult = RunMoveFiles(manualItems.Select(item => item.FilePath), "Manual move summary", delegate(int current, int total, string detail)
                    {
                        reportProgress(intakePlan.MoveOffset + current, detail);
                    }, cancellationToken);
                    var sortResult = SaveUndoAndSortAfterImportMoveIfNeeded(
                        moveResult,
                        intakePlan.SortOffset,
                        "Manual intake workflow",
                        reportProgress,
                        cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
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

        /// <summary>
        /// After import moves: append undo manifest and sort destination root into game folders when anything moved.
        /// Shared by standard import, unified import-and-comment, and manual intake.
        /// Uses <see cref="IImportService.SortDestinationRootIntoGameFolders"/> directly (no <c>SortDestinationFoldersCore</c> UI side effects).
        /// </summary>
        SortStepResult SaveUndoAndSortAfterImportMoveIfNeeded(
            MoveStepResult moveResult,
            int sortProgressSlot,
            string canceledOperationLabel,
            Action<int, string> reportProgress,
            CancellationToken cancellationToken)
        {
            if (moveResult == null || moveResult.Moved <= 0) return null;
            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, canceledOperationLabel);
            importService.SaveUndoManifest(moveResult.Entries);
            reportProgress(sortProgressSlot, "Sorting imported captures into game folders...");
            return importService.SortDestinationRootIntoGameFolders(destinationRoot, libraryRoot, cancellationToken);
        }
    }
}
