using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
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
            var batch = metadataService.RunExifWriteRequests(requests, total, skipped, progress, cancellationToken);
            importService.RelocateExifFailuresToUploadErrors(batch.Failures);
            updated = batch.SuccessCount;
            var relocated = batch.Failures == null ? 0 : batch.Failures.Count;
            foreach (var item in itemsToReset) item.ForceTagMetadataWrite = false;
            if (progress != null) progress(total, total, "Metadata step complete: updated " + updated + ", skipped " + skipped + (relocated > 0 ? ", " + relocated + " moved to Errors" : string.Empty) + ".");
            Log("Manual metadata summary: updated " + updated + ", skipped " + skipped + (relocated > 0 ? ", " + relocated + " moved to Errors folder" : string.Empty) + ".");
            return new MetadataStepResult { Updated = updated, Skipped = skipped, FailedRelocatedToErrors = relocated };
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
