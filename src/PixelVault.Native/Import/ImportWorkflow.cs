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
            public HdrFallbackMoveResult HdrFallbackResult;
            public int ManualItemsLeft;
            public bool ManualItemsLeftAreUploadSkips;
        }

        sealed class ManualIntakeExecutionResult
        {
            public RenameStepResult RenameResult;
            public MetadataStepResult MetadataResult;
            public MoveStepResult MoveResult;
            public SortStepResult SortResult;
            public HdrFallbackMoveResult HdrFallbackResult;
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
                var prep = BuildIntakePreparation(inventory.TopLevelMediaFiles, withReview);
                var reviewItems = prep.ReviewItems;
                var manualItems = prep.ManualItems;
                var manualPaths = prep.ManualPaths;
                prepStopwatch.Stop();
                List<ManualMetadataItem> unifiedImportBatch = null;
                if (withReview)
                {
                    var importEditItems = prep.ImportEditItems;
                    ApplyHdrPairAlternatesToManualMetadataItems(importEditItems, inventory.HdrPairs);
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

        void ApplyHdrPairAlternatesToManualMetadataItems(List<ManualMetadataItem> items, IEnumerable<HdrCapturePair> pairs)
        {
            if (items == null) return;
            var alternateBySelected = (pairs ?? Enumerable.Empty<HdrCapturePair>())
                .Where(pair => pair != null && !string.IsNullOrWhiteSpace(pair.SelectedFilePath) && !string.IsNullOrWhiteSpace(pair.AlternateFilePath))
                .GroupBy(pair => pair.SelectedFilePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().AlternateFilePath, StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) continue;
                string alternate;
                if (alternateBySelected.TryGetValue(item.FilePath, out alternate))
                    item.HdrAlternateFilePath = alternate;
            }
        }

        HdrFallbackMoveResult MoveHdrFallbacksForManualBatch(IEnumerable<ManualMetadataItem> batch, CancellationToken cancellationToken)
        {
            var fallbackFiles = (batch ?? Enumerable.Empty<ManualMetadataItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.HdrAlternateFilePath))
                .Select(item => item.HdrAlternateFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return importService.MoveHdrPairFallbackFiles(fallbackFiles, null, cancellationToken);
        }

        HdrFallbackMoveResult MoveHdrFallbacksForInventory(SourceInventory inventory, CancellationToken cancellationToken)
        {
            var fallbackFiles = (inventory == null ? Enumerable.Empty<HdrCapturePair>() : inventory.HdrPairs ?? new List<HdrCapturePair>())
                .Where(pair => pair != null && !string.IsNullOrWhiteSpace(pair.AlternateFilePath))
                .Select(pair => pair.AlternateFilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return importService.MoveHdrPairFallbackFiles(fallbackFiles, null, cancellationToken);
        }

        T MeasureImportWorkflowStep<T>(string workflow, string step, int itemCount, Func<T> action, Func<T, string> resultDetail = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = action();
                stopwatch.Stop();
                LogPerformanceSample("ImportWorkflowStep", stopwatch, BuildImportWorkflowStepPerfDetail(workflow, step, itemCount, resultDetail == null ? string.Empty : resultDetail(result)), 0);
                return result;
            }
            catch
            {
                stopwatch.Stop();
                LogPerformanceSample("ImportWorkflowStep", stopwatch, BuildImportWorkflowStepPerfDetail(workflow, step, itemCount, "status=failed"), 0);
                throw;
            }
        }

        async Task<T> MeasureImportWorkflowStepAsync<T>(string workflow, string step, int itemCount, Func<Task<T>> action, Func<T, string> resultDetail = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await action().ConfigureAwait(false);
                stopwatch.Stop();
                LogPerformanceSample("ImportWorkflowStep", stopwatch, BuildImportWorkflowStepPerfDetail(workflow, step, itemCount, resultDetail == null ? string.Empty : resultDetail(result)), 0);
                return result;
            }
            catch
            {
                stopwatch.Stop();
                LogPerformanceSample("ImportWorkflowStep", stopwatch, BuildImportWorkflowStepPerfDetail(workflow, step, itemCount, "status=failed"), 0);
                throw;
            }
        }

        static string BuildImportWorkflowStepPerfDetail(string workflow, string step, int itemCount, string resultDetail)
        {
            var detail = "workflow=" + (workflow ?? string.Empty) + "; step=" + (step ?? string.Empty) + "; items=" + Math.Max(0, itemCount);
            return string.IsNullOrWhiteSpace(resultDetail) ? detail : detail + "; " + resultDetail;
        }

        static string BuildRenamePerfDetail(RenameStepResult result)
        {
            return "renamed=" + (result == null ? 0 : result.Renamed) + "; skipped=" + (result == null ? 0 : result.Skipped);
        }

        static string BuildDeletePerfDetail(DeleteStepResult result)
        {
            return "deleted=" + (result == null ? 0 : result.Deleted) + "; skipped=" + (result == null ? 0 : result.Skipped);
        }

        static string BuildMetadataPerfDetail(MetadataStepResult result)
        {
            return "updated=" + (result == null ? 0 : result.Updated) + "; skipped=" + (result == null ? 0 : result.Skipped) + "; failures=" + (result == null ? 0 : result.FailedRelocatedToErrors);
        }

        static string BuildMovePerfDetail(MoveStepResult result)
        {
            return "moved=" + (result == null ? 0 : result.Moved) + "; skipped=" + (result == null ? 0 : result.Skipped) + "; renamedOnConflict=" + (result == null ? 0 : result.RenamedOnConflict);
        }

        static string BuildSortPerfDetail(SortStepResult result)
        {
            return "sorted=" + (result == null ? 0 : result.Sorted) + "; foldersCreated=" + (result == null ? 0 : result.FoldersCreated) + "; renamedOnConflict=" + (result == null ? 0 : result.RenamedOnConflict);
        }

        static string BuildHdrFallbackPerfDetail(HdrFallbackMoveResult result)
        {
            return "moved=" + (result == null ? 0 : result.Moved) + "; skipped=" + (result == null ? 0 : result.Skipped);
        }

        void LogImportWorkflowRun(Stopwatch stopwatch, string workflow, string mode, int totalWork, SourceInventory inventory, RenameStepResult renameResult, DeleteStepResult deleteResult, MetadataStepResult metadataResult, MoveStepResult moveResult, SortStepResult sortResult, HdrFallbackMoveResult hdrFallbackResult, int manualItemsLeft, bool manualItemsLeftAreUploadSkips)
        {
            if (stopwatch == null) return;
            stopwatch.Stop();
            var importCandidates = inventory == null || inventory.TopLevelMediaFiles == null ? 0 : inventory.TopLevelMediaFiles.Count;
            var renameScope = inventory == null || inventory.RenameScopeFiles == null ? 0 : inventory.RenameScopeFiles.Count;
            var hdrPairs = inventory == null || inventory.HdrPairs == null ? 0 : inventory.HdrPairs.Count;
            LogPerformanceSample(
                "ImportWorkflowRun",
                stopwatch,
                "workflow=" + (workflow ?? string.Empty)
                + "; mode=" + (mode ?? string.Empty)
                + "; totalWork=" + totalWork
                + "; importCandidates=" + importCandidates
                + "; renameScope=" + renameScope
                + "; hdrPairs=" + hdrPairs
                + "; renamed=" + (renameResult == null ? 0 : renameResult.Renamed)
                + "; deleted=" + (deleteResult == null ? 0 : deleteResult.Deleted)
                + "; metadataUpdated=" + (metadataResult == null ? 0 : metadataResult.Updated)
                + "; moved=" + (moveResult == null ? 0 : moveResult.Moved)
                + "; sorted=" + (sortResult == null ? 0 : sortResult.Sorted)
                + "; hdrMoved=" + (hdrFallbackResult == null ? 0 : hdrFallbackResult.Moved)
                + "; manualItemsLeft=" + manualItemsLeft
                + "; manualLeftAreUploadSkips=" + manualItemsLeftAreUploadSkips,
                0);
        }

        void LogManualIntakeWorkflowRun(Stopwatch stopwatch, int totalWork, int manualItemCount, RenameStepResult renameResult, MetadataStepResult metadataResult, MoveStepResult moveResult, SortStepResult sortResult, HdrFallbackMoveResult hdrFallbackResult)
        {
            if (stopwatch == null) return;
            stopwatch.Stop();
            LogPerformanceSample(
                "ImportWorkflowRun",
                stopwatch,
                "workflow=manual-intake"
                + "; mode=manual"
                + "; totalWork=" + totalWork
                + "; importCandidates=" + Math.Max(0, manualItemCount)
                + "; renamed=" + (renameResult == null ? 0 : renameResult.Renamed)
                + "; metadataUpdated=" + (metadataResult == null ? 0 : metadataResult.Updated)
                + "; moved=" + (moveResult == null ? 0 : moveResult.Moved)
                + "; sorted=" + (sortResult == null ? 0 : sortResult.Sorted)
                + "; hdrMoved=" + (hdrFallbackResult == null ? 0 : hdrFallbackResult.Moved),
                0);
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
                var prep = BuildIntakePreparation(inventory.TopLevelMediaFiles, false);
                var manualItems = prep.ManualItems;
                ApplyHdrPairAlternatesToManualMetadataItems(manualItems, inventory.HdrPairs);
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
            var workflowPerfLabel = withReview ? "import+comment" : "import";

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
                    var workflowStopwatch = Stopwatch.StartNew();
                    if (useUnifiedManualImportBatch)
                    {
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var unifiedHdrFallbackCount = batch
                            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.HdrAlternateFilePath))
                            .Select(item => item.HdrAlternateFilePath)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count();
                        var hdrFallbackResult = MeasureImportWorkflowStep(workflowPerfLabel, "hdrFallback", unifiedHdrFallbackCount, () => MoveHdrFallbacksForManualBatch(batch, cancellationToken), BuildHdrFallbackPerfDetail);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var steamRenameResult = await MeasureImportWorkflowStepAsync(workflowPerfLabel, "steamRename", batch.Count, () => importService.RunSteamRenameAsync(batch.Select(item => item.FilePath), delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.SteamOff + current, detail);
                        }, cancellationToken), BuildRenamePerfDetail).ConfigureAwait(false);
                        var steamMap = steamRenameResult == null ? null : steamRenameResult.OldPathToNewPath;
                        if (steamMap != null && steamMap.Count > 0) SteamImportRename.ApplySteamRenameMapToManualMetadataItems(batch, steamMap);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var manualRenameResult = MeasureImportWorkflowStep(workflowPerfLabel, "manualRename", batch.Count, () => RunManualRename(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.ManualRenameOff + current, detail);
                        }, cancellationToken), BuildRenamePerfDetail);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var uniDeleteResult = MeasureImportWorkflowStep(workflowPerfLabel, "delete", batch.Count, () => RunDeleteManualMetadata(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.DeleteOff + current, detail);
                        }, cancellationToken), BuildDeletePerfDetail);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var uniMetadataResult = MeasureImportWorkflowStep(workflowPerfLabel, "metadata", batch.Count, () => RunManualMetadata(batch, delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.MetadataOff + current, detail);
                        }, cancellationToken), BuildMetadataPerfDetail);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        var unifiedMoveFiles = batch.Select(item => item.FilePath).Where(fileSystemService.FileExists).ToList();
                        var uniMoveResult = MeasureImportWorkflowStep(workflowPerfLabel, "move", unifiedMoveFiles.Count, () => RunMoveFiles(unifiedMoveFiles, "Import move summary", delegate(int current, int total, string detail)
                        {
                            reportProgress(unifiedPlan.MoveOff + current, detail);
                        }, cancellationToken), BuildMovePerfDetail);
                        var uniSortResult = SaveUndoAndSortAfterImportMoveIfNeeded(
                            uniMoveResult,
                            unifiedPlan.SortOff,
                            workflowPerfLabel,
                            reportProgress,
                            cancellationToken);
                        ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                        reportProgress(totalWork, "Import workflow complete.");
                        var combinedRename = ImportWorkflowOrchestration.CombineRenameStepResults(steamRenameResult, manualRenameResult);
                        var unifiedResult = new ImportWorkflowExecutionResult
                        {
                            RenameResult = combinedRename,
                            DeleteResult = uniDeleteResult,
                            MetadataResult = uniMetadataResult,
                            MoveResult = uniMoveResult,
                            SortResult = uniSortResult,
                            HdrFallbackResult = hdrFallbackResult,
                            ManualItemsLeft = manualItemsLeftOverride ?? 0,
                            ManualItemsLeftAreUploadSkips = true
                        };
                        LogImportWorkflowRun(workflowStopwatch, workflowPerfLabel, "unified", totalWork, inventory, unifiedResult.RenameResult, unifiedResult.DeleteResult, unifiedResult.MetadataResult, unifiedResult.MoveResult, unifiedResult.SortResult, unifiedResult.HdrFallbackResult, unifiedResult.ManualItemsLeft, unifiedResult.ManualItemsLeftAreUploadSkips);
                        return unifiedResult;
                    }

                    var renameOffset = standardTotals.RenameOffset;
                    var deleteOffset = standardTotals.DeleteOffset;
                    var metadataOffset = standardTotals.MetadataOffset;
                    var moveOffset = standardTotals.MoveOffset;
                    var sortOffset = standardTotals.SortOffset;

                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var standardHdrFallbackCount = inventory == null || inventory.HdrPairs == null
                        ? 0
                        : inventory.HdrPairs.Where(pair => pair != null && !string.IsNullOrWhiteSpace(pair.AlternateFilePath)).Select(pair => pair.AlternateFilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    var standardHdrFallbackResult = MeasureImportWorkflowStep(workflowPerfLabel, "hdrFallback", standardHdrFallbackCount, () => MoveHdrFallbacksForInventory(inventory, cancellationToken), BuildHdrFallbackPerfDetail);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var renameScopeCount = renameInventory == null || renameInventory.RenameScopeFiles == null ? 0 : renameInventory.RenameScopeFiles.Count;
                    var renameResult = await MeasureImportWorkflowStepAsync(workflowPerfLabel, "steamRename", renameScopeCount, () => importService.RunSteamRenameAsync(renameInventory == null ? new List<string>() : renameInventory.RenameScopeFiles, delegate(int current, int total, string detail)
                    {
                        reportProgress(renameOffset + current, detail);
                    }, cancellationToken), BuildRenamePerfDetail).ConfigureAwait(false);
                    var steamRenameMap = renameResult == null ? null : renameResult.OldPathToNewPath;
                    if (steamRenameMap != null && steamRenameMap.Count > 0) SteamImportRename.ApplySteamRenameMapToReviewItems(reviewItems, steamRenameMap);
                    var moveSourcePathsAfterRename = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(inventory == null ? null : inventory.TopLevelMediaFiles, steamRenameMap);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var reviewCount = reviewItems == null ? 0 : reviewItems.Count;
                    var deleteResult = MeasureImportWorkflowStep(workflowPerfLabel, "delete", reviewCount, () => RunDelete(reviewItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(deleteOffset + current, detail);
                    }, cancellationToken), BuildDeletePerfDetail);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var metadataResult = MeasureImportWorkflowStep(workflowPerfLabel, "metadata", reviewCount, () => RunMetadata(reviewItems, delegate(int current, int total, string detail)
                    {
                        reportProgress(metadataOffset + current, detail);
                    }, cancellationToken), BuildMetadataPerfDetail);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    var moveResult = MeasureImportWorkflowStep(workflowPerfLabel, "move", moveSourcePathsAfterRename == null ? 0 : moveSourcePathsAfterRename.Count, () => RunMove(moveSourcePathsAfterRename, manualPaths, delegate(int current, int total, string detail)
                    {
                        reportProgress(moveOffset + current, detail);
                    }, cancellationToken), BuildMovePerfDetail);
                    var sortResult = SaveUndoAndSortAfterImportMoveIfNeeded(
                        moveResult,
                        sortOffset,
                        workflowPerfLabel,
                        reportProgress,
                        cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Import workflow");
                    reportProgress(totalWork, "Import workflow complete.");
                    var standardResult = new ImportWorkflowExecutionResult
                    {
                        RenameResult = renameResult,
                        DeleteResult = deleteResult,
                        MetadataResult = metadataResult,
                        MoveResult = moveResult,
                        SortResult = sortResult,
                        HdrFallbackResult = standardHdrFallbackResult,
                        ManualItemsLeft = manualItemsLeftOverride ?? (manualItems == null ? 0 : manualItems.Count),
                        ManualItemsLeftAreUploadSkips = false
                    };
                    LogImportWorkflowRun(workflowStopwatch, workflowPerfLabel, "standard", totalWork, inventory, standardResult.RenameResult, standardResult.DeleteResult, standardResult.MetadataResult, standardResult.MoveResult, standardResult.SortResult, standardResult.HdrFallbackResult, standardResult.ManualItemsLeft, standardResult.ManualItemsLeftAreUploadSkips);
                    return standardResult;
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
                    var summaryLines = BuildImportSummaryLines("Import", withReview, result.RenameResult, result.DeleteResult, result.MetadataResult, result.MoveResult, result.SortResult, result.ManualItemsLeft, result.ManualItemsLeftAreUploadSkips, result.HdrFallbackResult);
                    var movedCount = result.MoveResult == null ? 0 : result.MoveResult.Moved;
                    var metadataUpdated = result.MetadataResult == null ? 0 : result.MetadataResult.Updated;
                    var hdrMoved = result.HdrFallbackResult == null ? 0 : result.HdrFallbackResult.Moved;
                    var leftSuffix = result.ManualItemsLeft > 0
                        ? (result.ManualItemsLeftAreUploadSkips ? " | " + result.ManualItemsLeft + " not selected (still in upload)" : " | " + result.ManualItemsLeft + " unmatched left")
                        : string.Empty;
                    var hdrSuffix = hdrMoved > 0 ? " | " + hdrMoved + " HDR duplicate(s) parked" : string.Empty;
                    var summaryMeta = movedCount + " file(s) imported | " + metadataUpdated + " metadata update(s)" + hdrSuffix + leftSuffix;
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
            var intakeBatch = manualItems ?? new List<ManualMetadataItem>();
            var intakePlan = importService.ComputeManualIntakeProgressPlan(intakeBatch);
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
                    var workflowStopwatch = Stopwatch.StartNew();
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var hdrFallbackCount = intakeBatch
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.HdrAlternateFilePath))
                        .Select(item => item.HdrAlternateFilePath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    var hdrFallbackResult = MeasureImportWorkflowStep("manual-intake", "hdrFallback", hdrFallbackCount, () => MoveHdrFallbacksForManualBatch(intakeBatch, cancellationToken), BuildHdrFallbackPerfDetail);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var renameResult = MeasureImportWorkflowStep("manual-intake", "manualRename", intakeBatch.Count, () => RunManualRename(intakeBatch, delegate(int current, int total, string detail)
                    {
                        reportProgress(intakePlan.RenameOffset + current, detail);
                    }, cancellationToken), BuildRenamePerfDetail);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var metadataResult = MeasureImportWorkflowStep("manual-intake", "metadata", intakeBatch.Count, () => RunManualMetadata(intakeBatch, delegate(int current, int total, string detail)
                    {
                        reportProgress(intakePlan.MetadataOffset + current, detail);
                    }, cancellationToken), BuildMetadataPerfDetail);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    var manualMoveFiles = intakeBatch.Select(item => item.FilePath).ToList();
                    var moveResult = MeasureImportWorkflowStep("manual-intake", "move", manualMoveFiles.Count, () => RunMoveFiles(manualMoveFiles, "Manual move summary", delegate(int current, int total, string detail)
                    {
                        reportProgress(intakePlan.MoveOffset + current, detail);
                    }, cancellationToken), BuildMovePerfDetail);
                    var sortResult = SaveUndoAndSortAfterImportMoveIfNeeded(
                        moveResult,
                        intakePlan.SortOffset,
                        "manual-intake",
                        reportProgress,
                        cancellationToken);
                    ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, "Manual intake workflow");
                    reportProgress(totalWork, "Manual intake workflow complete.");
                    var manualResult = new ManualIntakeExecutionResult
                    {
                        RenameResult = renameResult,
                        MetadataResult = metadataResult,
                        MoveResult = moveResult,
                        SortResult = sortResult,
                        HdrFallbackResult = hdrFallbackResult
                    };
                    LogManualIntakeWorkflowRun(workflowStopwatch, totalWork, intakeBatch.Count, manualResult.RenameResult, manualResult.MetadataResult, manualResult.MoveResult, manualResult.SortResult, manualResult.HdrFallbackResult);
                    return manualResult;
                },
                delegate(ManualIntakeExecutionResult result)
                {
                    RefreshPreview();
                    status.Text = "Manual intake complete";
                    Log("Manual intake workflow complete.");
                    var summaryLines = BuildImportSummaryLines("Manual Intake", false, result.RenameResult, null, result.MetadataResult, result.MoveResult, result.SortResult, 0, false, result.HdrFallbackResult);
                    var movedCount = result.MoveResult == null ? 0 : result.MoveResult.Moved;
                    var metadataUpdated = result.MetadataResult == null ? 0 : result.MetadataResult.Updated;
                    var hdrMoved = result.HdrFallbackResult == null ? 0 : result.HdrFallbackResult.Moved;
                    var hdrSuffix = hdrMoved > 0 ? " | " + hdrMoved + " HDR duplicate(s) parked" : string.Empty;
                    var summaryMeta = movedCount + " file(s) imported | " + metadataUpdated + " metadata update(s)" + hdrSuffix;
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
            var workflowPerfLabel = string.IsNullOrWhiteSpace(canceledOperationLabel) ? "import" : canceledOperationLabel;
            var cancellationLabel = string.Equals(workflowPerfLabel, "manual-intake", StringComparison.OrdinalIgnoreCase)
                ? "Manual intake workflow"
                : "Import workflow";
            ImportWorkflowOrchestration.ThrowIfCancellationRequested(cancellationToken, cancellationLabel);
            return MeasureImportWorkflowStep(workflowPerfLabel, "sort", moveResult.Moved, delegate
            {
                importService.SaveUndoManifest(moveResult.Entries);
                reportProgress(sortProgressSlot, "Sorting imported captures into game folders...");
                return importService.SortDestinationRootIntoGameFolders(destinationRoot, libraryRoot, cancellationToken);
            }, BuildSortPerfDetail);
        }
    }
}
