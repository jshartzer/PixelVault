using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        SourceInventory BuildSourceInventory(bool recurseRename)
        {
            var topLevelMediaFiles = EnumerateSourceFiles(SearchOption.TopDirectoryOnly, IsMedia).ToList();
            return new SourceInventory
            {
                TopLevelMediaFiles = topLevelMediaFiles,
                RenameScopeFiles = recurseRename
                    ? EnumerateSourceFiles(SearchOption.AllDirectories, IsMedia).ToList()
                    : topLevelMediaFiles.ToList()
            };
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
                var renameInventory = BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true);
                var renameResult = RunRename(renameInventory.RenameScopeFiles);
                var inventory = BuildSourceInventory(false);
                var reviewItems = BuildReviewItems(inventory.TopLevelMediaFiles);
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                if (withReview && reviewItems.Count > 0)
                {
                    status.Text = "Reviewing captures";
                    Log("Opening review window for " + reviewItems.Count + " metadata candidate(s).");
                    if (!ShowMetadataReviewWindow(reviewItems))
                    {
                        status.Text = "Import canceled";
                        Log("Import canceled from review window.");
                        RefreshPreview();
                        return;
                    }
                }
                else if (withReview)
                {
                    Log("No metadata review items found. Continuing without review comments.");
                }
                var deleteResult = RunDelete(reviewItems);
                var metadataResult = RunMetadata(reviewItems);
                var moveResult = RunMove(inventory.TopLevelMediaFiles, manualPaths);
                SortStepResult sortResult = null;
                if (moveResult.Moved > 0)
                {
                    SaveUndoManifest(moveResult.Entries);
                    sortResult = SortDestinationFoldersCore(false);
                }
                if (manualItems.Count > 0)
                {
                    Log("Left " + manualItems.Count + " unmatched intake image(s) untouched. Use Manual Intake when you want to add missing data.");
                }
                RefreshPreview();
                status.Text = "Workflow complete";
                Log("Workflow complete.");
                var summaryLines = BuildImportSummaryLines("Import", withReview, renameResult, deleteResult, metadataResult, moveResult, sortResult, manualItems.Count);
                var summaryMeta = moveResult.Moved + " file(s) imported | " + metadataResult.Updated + " metadata update(s)" + (manualItems.Count > 0 ? " | " + manualItems.Count + " unmatched left" : string.Empty);
                ShowImportSummaryWindow(withReview ? "Import and Comment Summary" : "Import Summary", summaryMeta, summaryLines);
            }
            catch (Exception ex)
            {
                status.Text = "Workflow failed";
                Log(ex.ToString());
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
                var inventory = BuildSourceInventory(false);
                var recognizedPaths = new HashSet<string>(BuildReviewItems(inventory.TopLevelMediaFiles).Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
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

                var renameResult = RunManualRename(manualItems);
                var metadataResult = RunManualMetadata(manualItems);
                var moveResult = RunMoveFiles(manualItems.Select(i => i.FilePath), "Manual move summary");
                SortStepResult sortResult = null;
                if (moveResult.Moved > 0)
                {
                    SaveUndoManifest(moveResult.Entries);
                    sortResult = SortDestinationFoldersCore(false);
                }
                RefreshPreview();
                status.Text = "Manual intake complete";
                Log("Manual intake workflow complete.");
                var summaryLines = BuildImportSummaryLines("Manual Intake", false, renameResult, null, metadataResult, moveResult, sortResult, 0);
                var summaryMeta = moveResult.Moved + " file(s) imported | " + metadataResult.Updated + " metadata update(s)";
                ShowImportSummaryWindow("Manual Intake Summary", summaryMeta, summaryLines);
            }
            catch (Exception ex)
            {
                status.Text = "Manual intake failed";
                Log(ex.ToString());
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        RenameStepResult RunRename()
        {
            return RunRename(BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true).RenameScopeFiles);
        }

        RenameStepResult RunRename(IEnumerable<string> sourceFiles)
        {
            int renamed = 0, skipped = 0;
            var recordedSteamAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists))
            {
                var appId = GuessSteamAppIdFromFileName(file);
                if (string.IsNullOrWhiteSpace(appId)) { skipped++; continue; }
                var game = SteamName(appId);
                if (string.IsNullOrWhiteSpace(game)) { skipped++; continue; }
                if (recordedSteamAppIds.Add(appId)) EnsureSteamAppIdInGameIndex(libraryRoot, game, appId);
                var baseName = Path.GetFileNameWithoutExtension(file);
                var newBase = game + baseName.Substring(appId.Length);
                var target = Unique(Path.Combine(Path.GetDirectoryName(file), newBase + Path.GetExtension(file)));
                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                renamed++;
                Log("Renamed: " + Path.GetFileName(file) + " -> " + Path.GetFileName(target));
            }
            Log("Rename summary: renamed " + renamed + ", skipped " + skipped + ".");
            return new RenameStepResult { Renamed = renamed, Skipped = skipped };
        }

        RenameStepResult RunManualRename(List<ManualMetadataItem> items, Action<int, int, string> progress = null)
        {
            int renamed = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            if (progress != null) progress(0, total, "Starting rename step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                var item = items[i];
                var remaining = total - (i + 1);
                if (!File.Exists(item.FilePath))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var gameName = Sanitize(item.GameName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(gameName))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no game title");
                    continue;
                }
                var currentBase = Path.GetFileNameWithoutExtension(item.FilePath);
                var normalizedCurrent = NormalizeTitle(currentBase);
                var normalizedGame = NormalizeTitle(gameName);
                if (currentBase.StartsWith(gameName + "_", StringComparison.OrdinalIgnoreCase) || normalizedCurrent == normalizedGame || normalizedCurrent.StartsWith(normalizedGame + " "))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
                    continue;
                }
                var oldName = item.FileName;
                var target = Unique(Path.Combine(Path.GetDirectoryName(item.FilePath), gameName + "_" + currentBase + Path.GetExtension(item.FilePath)));
                var originalPath = item.FilePath;
                File.Move(item.FilePath, target);
                MoveMetadataSidecarIfPresent(originalPath, target);
                Log("Manual rename: " + oldName + " -> " + Path.GetFileName(target));
                item.FilePath = target;
                item.FileName = Path.GetFileName(target);
                renamed++;
                if (progress != null) progress(i + 1, total, "Renamed " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
            }
            if (progress != null) progress(total, total, "Rename step complete: renamed " + renamed + ", skipped " + skipped + ".");
            if (renamed > 0 || skipped > 0) Log("Manual rename summary: renamed " + renamed + ", skipped " + skipped + ".");
            return new RenameStepResult { Renamed = renamed, Skipped = skipped };
        }

        MetadataStepResult RunManualMetadata(List<ManualMetadataItem> items, Action<int, int, string> progress = null)
        {
            int updated = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            var requests = new List<ExifWriteRequest>();
            var itemsToReset = new List<ManualMetadataItem>();
            if (progress != null) progress(0, total, "Starting metadata step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
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
            updated = RunExifWriteRequests(requests, total, skipped, progress);
            foreach (var item in itemsToReset) item.ForceTagMetadataWrite = false;
            if (progress != null) progress(total, total, "Metadata step complete: updated " + updated + ", skipped " + skipped + ".");
            Log("Manual metadata summary: updated " + updated + ", skipped " + skipped + ".");
            return new MetadataStepResult { Updated = updated, Skipped = skipped };
        }

        DeleteStepResult RunDelete(List<ReviewItem> reviewItems)
        {
            int deleted = 0, skipped = 0;
            foreach (var item in reviewItems.Where(i => i.DeleteBeforeProcessing))
            {
                if (!File.Exists(item.FilePath)) { skipped++; continue; }
                File.Delete(item.FilePath);
                deleted++;
                Log("Deleted before processing: " + item.FileName);
            }
            if (deleted > 0 || skipped > 0) Log("Delete summary: deleted " + deleted + ", skipped " + skipped + ".");
            return new DeleteStepResult { Deleted = deleted, Skipped = skipped };
        }

        int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null)
        {
            var workItems = requests ?? new List<ExifWriteRequest>();
            if (workItems.Count == 0) return 0;

            var completed = alreadyCompleted;
            var failures = new ConcurrentQueue<Exception>();
            var workerCount = GetMetadataWorkerCount(workItems.Count);
            Log("Running metadata writes with " + workerCount + " worker(s) for " + workItems.Count + " file(s).");

            Parallel.ForEach(workItems, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, delegate(ExifWriteRequest request)
            {
                try
                {
                    RunExe(exifToolPath, request.Arguments, Path.GetDirectoryName(exifToolPath), false);
                    if (request.RestoreFileTimes)
                    {
                        if (request.OriginalCreateTime != DateTime.MinValue) File.SetCreationTime(request.FilePath, request.OriginalCreateTime);
                        if (request.OriginalWriteTime != DateTime.MinValue) File.SetLastWriteTime(request.FilePath, request.OriginalWriteTime);
                    }
                    if (progress != null)
                    {
                        var current = Interlocked.Increment(ref completed);
                        var remaining = Math.Max(totalCount - current, 0);
                        progress(current, totalCount, "Updated metadata " + current + " of " + totalCount + " | " + remaining + " remaining | " + request.SuccessDetail);
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(new InvalidOperationException("Metadata update failed for " + request.FileName + ". " + ex.Message, ex));
                }
            });

            if (!failures.IsEmpty) throw new AggregateException(failures);
            return workItems.Count;
        }

        MetadataStepResult RunMetadata(List<ReviewItem> reviewItems)
        {
            int updated = 0, skipped = 0;
            var requests = new List<ExifWriteRequest>();
            foreach (var item in reviewItems)
            {
                if (item.DeleteBeforeProcessing) { skipped++; continue; }
                var file = item.FilePath;
                if (!File.Exists(file)) { skipped++; continue; }
                var selectedPlatformTags = new List<string>();
                if (item.TagSteam)
                {
                    selectedPlatformTags.Add("Steam");
                }
                if (item.TagPs5)
                {
                    selectedPlatformTags.Add("PS5");
                    selectedPlatformTags.Add("PlayStation");
                }
                if (item.TagXbox) selectedPlatformTags.Add("Xbox");
                if (selectedPlatformTags.Count == 0 && item.PlatformTags != null) selectedPlatformTags.AddRange(item.PlatformTags);
                var platformTags = selectedPlatformTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var metadataTarget = item.CaptureTime.ToString("yyyy-MM-dd HH:mm:ss") + (item.PreserveFileTimes ? " (preserving file timestamps)" : string.Empty);
                var notes = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.Comment)) notes.Add("comment added");
                if (item.AddPhotographyTag) notes.Add(GamePhotographyTag + " tag added");
                var noteSuffix = notes.Count > 0 ? " [" + string.Join(", ", notes.ToArray()) + "]" : string.Empty;
                Log("Updating metadata: " + item.FileName + " -> " + metadataTarget + (platformTags.Length > 0 ? " [" + string.Join(", ", platformTags) + "]" : " [no platform tag]") + noteSuffix);
                var originalCreate = DateTime.MinValue;
                var originalWrite = DateTime.MinValue;
                if (item.PreserveFileTimes)
                {
                    originalCreate = File.GetCreationTime(file);
                    originalWrite = File.GetLastWriteTime(file);
                }
                requests.Add(new ExifWriteRequest
                {
                    FilePath = file,
                    FileName = item.FileName,
                    Arguments = BuildExifArgs(file, item.CaptureTime, platformTags, item.PreserveFileTimes, item.Comment, item.AddPhotographyTag),
                    RestoreFileTimes = item.PreserveFileTimes,
                    OriginalCreateTime = originalCreate,
                    OriginalWriteTime = originalWrite,
                    SuccessDetail = item.FileName
                });
            }
            updated = RunExifWriteRequests(requests, requests.Count + skipped, skipped, null);
            Log("Metadata summary: updated " + updated + ", skipped " + skipped + ".");
            return new MetadataStepResult { Updated = updated, Skipped = skipped };
        }

        MoveStepResult RunMove()
        {
            return RunMove(BuildSourceInventory(false).TopLevelMediaFiles, null);
        }

        MoveStepResult RunMove(HashSet<string> skipFiles)
        {
            return RunMove(BuildSourceInventory(false).TopLevelMediaFiles, skipFiles);
        }

        MoveStepResult RunMove(IEnumerable<string> sourceFiles, HashSet<string> skipFiles)
        {
            var files = (sourceFiles ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Where(file => skipFiles == null || !skipFiles.Contains(file));
            return RunMoveFiles(files, "Move summary");
        }

        MoveStepResult RunMoveFiles(IEnumerable<string> files, string summaryLabel)
        {
            int moved = 0, skipped = 0, renamedConflict = 0;
            var entries = new List<UndoImportEntry>();
            foreach (var file in files.Where(File.Exists))
            {
                var sourceDirectory = Path.GetDirectoryName(file);
                var target = Path.Combine(destinationRoot, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    var mode = CurrentConflictMode();
                    if (mode == "Skip") { skipped++; continue; }
                    if (mode == "Rename") { target = Unique(target); renamedConflict++; }
                    if (mode == "Overwrite") File.Delete(target);
                }
                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                moved++;
                entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(target), CurrentPath = target });
                AddSidecarUndoEntryIfPresent(target, sourceDirectory, entries);
                Log("Moved: " + Path.GetFileName(file) + " -> " + target);
            }
            Log(summaryLabel + ": moved " + moved + ", skipped " + skipped + ", renamed-on-conflict " + renamedConflict + ".");
            return new MoveStepResult { Moved = moved, Skipped = skipped, RenamedOnConflict = renamedConflict, Entries = entries };
        }

        string CurrentConflictMode()
        {
            var selected = conflictBox == null ? null : Convert.ToString(conflictBox.SelectedItem);
            return string.IsNullOrWhiteSpace(selected) ? "Rename" : selected;
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

        SortStepResult SortDestinationFoldersCore(bool interactive)
        {
            EnsureDir(destinationRoot, "Destination folder");
            var files = Directory.EnumerateFiles(destinationRoot, "*", SearchOption.TopDirectoryOnly).Where(IsMedia).ToList();
            if (files.Count == 0)
            {
                status.Text = "Nothing to sort";
                Log("Sort destination found no root-level media files to organize.");
                if (interactive) MessageBox.Show("There are no root-level media files in the destination folder to sort right now.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return new SortStepResult();
            }

            int moved = 0, created = 0, renamedConflict = 0;
            var indexedTargets = new List<string>();
            foreach (var file in files)
            {
                var folderName = GetSafeGameFolderName(GetGameNameFromFileName(Path.GetFileNameWithoutExtension(file)));
                var targetDirectory = Path.Combine(destinationRoot, folderName);
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    created++;
                }

                var target = Path.Combine(targetDirectory, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    target = Unique(target);
                    renamedConflict++;
                }

                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                moved++;
                indexedTargets.Add(target);
                Log("Sorted: " + Path.GetFileName(file) + " -> " + target);
            }

            UpsertLibraryMetadataIndexEntries(indexedTargets, libraryRoot);
            status.Text = "Destination sorted";
            Log("Sort summary: sorted " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ".");
            RefreshPreview();
            return new SortStepResult { Sorted = moved, FoldersCreated = created, RenamedOnConflict = renamedConflict };
        }

        void UndoLastImport()
        {
            try
            {
                var entries = LoadUndoManifest();
                if (entries.Count == 0)
                {
                    status.Text = "Nothing to undo";
                    Log("Undo requested, but there is no saved import manifest.");
                    MessageBox.Show("There is no saved import to undo yet.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var confirm = MessageBox.Show(entries.Count + " imported item(s) will be moved back to their source folders. Embedded metadata changes and comments will stay in the files.\n\nContinue?", "Undo Last Import", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.OK) return;

                int moved = 0, skipped = 0;
                var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var remaining = new List<UndoImportEntry>();
                var removedFromLibrary = new List<string>();
                foreach (var entry in entries)
                {
                    var currentPath = ResolveUndoCurrentPath(entry, usedPaths);
                    if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
                    {
                        skipped++;
                        remaining.Add(entry);
                        Log("Undo skipped: could not find " + entry.ImportedFileName + " in the destination/library folders.");
                        continue;
                    }

                    Directory.CreateDirectory(entry.SourceDirectory);
                    var target = Unique(Path.Combine(entry.SourceDirectory, Path.GetFileName(currentPath)));
                    File.Move(currentPath, target);
                    moved++;
                    removedFromLibrary.Add(currentPath);
                    Log("Undo move: " + currentPath + " -> " + target);
                }

                RemoveLibraryMetadataIndexEntries(removedFromLibrary, libraryRoot);
                SaveUndoManifest(remaining);
                status.Text = moved > 0 ? "Last import undone" : "Undo incomplete";
                Log("Undo summary: moved back " + moved + ", skipped " + skipped + ".");
                RefreshPreview();
            }
            catch (Exception ex)
            {
                status.Text = "Undo failed";
                Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        string ResolveUndoCurrentPath(UndoImportEntry entry, HashSet<string> usedPaths)
        {
            if (entry == null) return null;
            if (!string.IsNullOrWhiteSpace(entry.CurrentPath) && File.Exists(entry.CurrentPath))
            {
                var fullCurrent = Path.GetFullPath(entry.CurrentPath);
                if (usedPaths.Add(fullCurrent)) return fullCurrent;
            }

            foreach (var root in new[] { destinationRoot, libraryRoot }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var candidate in Directory.EnumerateFiles(root, entry.ImportedFileName, SearchOption.AllDirectories)
                    .OrderByDescending(path => File.GetLastWriteTime(path)))
                {
                    var fullCandidate = Path.GetFullPath(candidate);
                    if (usedPaths.Add(fullCandidate)) return fullCandidate;
                }
            }
            return null;
        }

        List<UndoImportEntry> LoadUndoManifest()
        {
            var entries = new List<UndoImportEntry>();
            if (!File.Exists(undoManifestPath)) return entries;
            foreach (var line in File.ReadAllLines(undoManifestPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 3) continue;
                entries.Add(new UndoImportEntry { SourceDirectory = parts[0], ImportedFileName = parts[1], CurrentPath = parts[2] });
            }
            return entries;
        }

        void SaveUndoManifest(List<UndoImportEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                if (File.Exists(undoManifestPath)) File.Delete(undoManifestPath);
                return;
            }

            File.WriteAllLines(undoManifestPath, entries.Select(entry => string.Join("\t", new[]
            {
                entry.SourceDirectory ?? string.Empty,
                entry.ImportedFileName ?? string.Empty,
                entry.CurrentPath ?? string.Empty
            })).ToArray());
        }
    }
}
