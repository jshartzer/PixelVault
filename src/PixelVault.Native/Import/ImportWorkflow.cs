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
        }

        sealed class ManualIntakeExecutionResult
        {
            public RenameStepResult RenameResult;
            public MetadataStepResult MetadataResult;
            public MoveStepResult MoveResult;
            public SortStepResult SortResult;
        }

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
                var prepStopwatch = Stopwatch.StartNew();
                var renameInventory = BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true);
                var inventory = BuildSourceInventory(false);
                var reviewItems = BuildReviewItems(inventory.TopLevelMediaFiles);
                var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                var manualItems = BuildManualMetadataItems(inventory.TopLevelMediaFiles, recognizedPaths);
                var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
                prepStopwatch.Stop();
                LogPerformanceSample("ImportPreparation", prepStopwatch, "workflow=" + (withReview ? "import+comment" : "import") + "; renameScope=" + renameInventory.RenameScopeFiles.Count + "; topLevel=" + inventory.TopLevelMediaFiles.Count + "; reviewItems=" + reviewItems.Count + "; manualItems=" + manualItems.Count, 40);
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
                RunImportWorkflowWithProgress(withReview, renameInventory, inventory, reviewItems, manualItems, manualPaths);
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
                var prepStopwatch = Stopwatch.StartNew();
                var inventory = BuildSourceInventory(false);
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
                Log(ex.ToString());
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

        void RunBackgroundWorkflowWithProgress<TResult>(string windowTitle, string progressTitleText, string initialMetaText, string startStatusText, string canceledStatusText, string startLogLine, string failureStatusText, int totalWork, Func<Action<int, string>, CancellationToken, TResult> backgroundWork, Action<TResult> onSuccess, Action onCanceled = null)
        {
            var progressWindow = new Window
            {
                Title = windowTitle,
                Width = 900,
                Height = 580,
                MinWidth = 780,
                MinHeight = 520,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };
            var progressRoot = new Grid { Margin = new Thickness(18) };
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            progressRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var progressTitle = new TextBlock { Text = progressTitleText, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
            var progressMeta = new TextBlock { Text = initialMetaText, Foreground = Brush("#B7C6C0"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            var effectiveTotalWork = Math.Max(totalWork, 1);
            var progressBar = new ProgressBar { Height = 18, Minimum = 0, Maximum = effectiveTotalWork, Value = 0, IsIndeterminate = totalWork <= 0, Margin = new Thickness(0, 0, 0, 14) };
            var progressLog = new TextBox { IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Background = Brush("#12191E"), Foreground = Brush("#F1E9DA"), BorderBrush = Brush("#2B3A44"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Cascadia Mono") };
            var closeButton = Btn("Cancel", null, "#334249", Brushes.White);
            closeButton.Margin = new Thickness(0);
            closeButton.HorizontalAlignment = HorizontalAlignment.Right;
            var progressLines = new List<string>();
            bool progressFinished = false;
            bool cancellationRequested = false;
            var workflowCancellation = new CancellationTokenSource();
            Action<string> appendProgress = delegate(string line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                progressLines.Add(line);
                while (progressLines.Count > 200) progressLines.RemoveAt(0);
                progressLog.Text = string.Join(Environment.NewLine, progressLines.ToArray());
                progressLog.ScrollToEnd();
            };
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
            progressRoot.Children.Add(progressTitle);
            Grid.SetRow(progressMeta, 1);
            progressRoot.Children.Add(progressMeta);
            var centerPanel = new Grid();
            centerPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            centerPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            centerPanel.Children.Add(progressBar);
            var logBorder = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), BorderBrush = Brush("#26363F"), BorderThickness = new Thickness(1), Child = progressLog, Margin = new Thickness(0, 14, 0, 0) };
            Grid.SetRow(logBorder, 1);
            centerPanel.Children.Add(logBorder);
            Grid.SetRow(centerPanel, 2);
            progressRoot.Children.Add(centerPanel);
            Grid.SetRow(closeButton, 3);
            progressRoot.Children.Add(closeButton);
            progressWindow.Content = progressRoot;

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

            Task.Factory.StartNew(delegate
            {
                return backgroundWork(reportProgress, workflowCancellation.Token);
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
                        Log(error.ToString());
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
                        Log(ex.ToString());
                        MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }));
            }, TaskScheduler.Default);

            progressWindow.ShowDialog();
        }

        void RunImportWorkflowWithProgress(bool withReview, SourceInventory renameInventory, SourceInventory inventory, List<ReviewItem> reviewItems, List<ManualMetadataItem> manualItems, HashSet<string> manualPaths)
        {
            var renameTotal = renameInventory == null || renameInventory.RenameScopeFiles == null ? 0 : renameInventory.RenameScopeFiles.Count;
            var deleteTotal = reviewItems == null ? 0 : reviewItems.Count(item => item != null && item.DeleteBeforeProcessing);
            var metadataTotal = reviewItems == null ? 0 : reviewItems.Count;
            var moveTotal = inventory == null || inventory.TopLevelMediaFiles == null
                ? 0
                : inventory.TopLevelMediaFiles.Count(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file) && (manualPaths == null || !manualPaths.Contains(file)));
            var totalWork = renameTotal + deleteTotal + metadataTotal + moveTotal + 1;
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
                delegate(Action<int, string> reportProgress, CancellationToken cancellationToken)
                {
                    var renameOffset = 0;
                    var deleteOffset = renameOffset + renameTotal;
                    var metadataOffset = deleteOffset + deleteTotal;
                    var moveOffset = metadataOffset + metadataTotal;
                    var sortOffset = moveOffset + moveTotal;

                    ThrowIfWorkflowCancellationRequested(cancellationToken, "Import workflow");
                    var renameResult = RunRename(renameInventory == null ? new List<string>() : renameInventory.RenameScopeFiles, delegate(int current, int total, string detail)
                    {
                        reportProgress(renameOffset + current, detail);
                    }, cancellationToken);
                    var steamRenameMap = renameResult == null ? null : renameResult.OldPathToNewPath;
                    if (steamRenameMap != null && steamRenameMap.Count > 0) ApplySteamRenameMapToReviewItems(reviewItems, steamRenameMap);
                    var moveSourcePathsAfterRename = ResolveTopLevelPathsAfterSteamRename(inventory == null ? null : inventory.TopLevelMediaFiles, steamRenameMap);
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
                        SaveUndoManifest(moveResult.Entries);
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
                        ManualItemsLeft = manualItems == null ? 0 : manualItems.Count
                    };
                },
                delegate(ImportWorkflowExecutionResult result)
                {
                    if (result.ManualItemsLeft > 0)
                    {
                        Log("Left " + result.ManualItemsLeft + " unmatched intake image(s) untouched. Use Manual Intake when you want to add missing data.");
                    }
                    RefreshPreview();
                    status.Text = "Workflow complete";
                    Log("Workflow complete.");
                    var summaryLines = BuildImportSummaryLines("Import", withReview, result.RenameResult, result.DeleteResult, result.MetadataResult, result.MoveResult, result.SortResult, result.ManualItemsLeft);
                    var movedCount = result.MoveResult == null ? 0 : result.MoveResult.Moved;
                    var metadataUpdated = result.MetadataResult == null ? 0 : result.MetadataResult.Updated;
                    var summaryMeta = movedCount + " file(s) imported | " + metadataUpdated + " metadata update(s)" + (result.ManualItemsLeft > 0 ? " | " + result.ManualItemsLeft + " unmatched left" : string.Empty);
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
                delegate(Action<int, string> reportProgress, CancellationToken cancellationToken)
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
                        SaveUndoManifest(moveResult.Entries);
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
            return RunRename(BuildSourceInventory(recurseBox != null && recurseBox.IsChecked == true).RenameScopeFiles);
        }

        static void ApplySteamRenameMapToReviewItems(List<ReviewItem> items, Dictionary<string, string> oldToNew)
        {
            if (items == null || oldToNew == null || oldToNew.Count == 0) return;
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) continue;
                string newPath;
                if (!oldToNew.TryGetValue(item.FilePath, out newPath) || string.IsNullOrWhiteSpace(newPath)) continue;
                item.FilePath = newPath;
                item.FileName = Path.GetFileName(newPath);
            }
        }

        static List<string> ResolveTopLevelPathsAfterSteamRename(IEnumerable<string> topLevelBeforeRename, Dictionary<string, string> oldToNew)
        {
            var list = new List<string>();
            if (topLevelBeforeRename == null) return list;
            foreach (var path in topLevelBeforeRename)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string newPath;
                if (oldToNew != null && oldToNew.TryGetValue(path, out newPath) && !string.IsNullOrWhiteSpace(newPath)) list.Add(newPath);
                else list.Add(path);
            }
            return list;
        }

        static bool SteamAppIdLooksLikeFilenamePrefix(string appId, string baseName)
        {
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(baseName)) return false;
            if (!appId.All(char.IsDigit)) return false;
            return baseName.StartsWith(appId, StringComparison.OrdinalIgnoreCase);
        }

        static bool TryBuildSteamRenameBase(string baseName, string appId, string canonicalGameTitle, string gameTitleHint, out string newBase)
        {
            newBase = null;
            if (string.IsNullOrWhiteSpace(baseName) || string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(canonicalGameTitle)) return false;
            if (SteamAppIdLooksLikeFilenamePrefix(appId, baseName))
            {
                newBase = canonicalGameTitle + baseName.Substring(appId.Length);
                return true;
            }
            if (!string.IsNullOrWhiteSpace(gameTitleHint)
                && baseName.Length > gameTitleHint.Length
                && baseName.StartsWith(gameTitleHint, StringComparison.OrdinalIgnoreCase)
                && baseName[gameTitleHint.Length] == '_')
            {
                newBase = canonicalGameTitle + baseName.Substring(gameTitleHint.Length);
                return true;
            }
            if (baseName.StartsWith(canonicalGameTitle + "_", StringComparison.OrdinalIgnoreCase))
            {
                newBase = baseName;
                return true;
            }
            return false;
        }

        RenameStepResult RunRename(IEnumerable<string> sourceFiles, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            int renamed = 0, skipped = 0;
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var recordedSteamAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).ToList();
            var total = files.Count;
            if (progress != null) progress(0, total, "Starting rename step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                var remaining = total - (i + 1);
                var parsed = ParseFilename(file);
                var appId = parsed.SteamAppId;
                if (string.IsNullOrWhiteSpace(appId))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no Steam AppID in filename");
                    continue;
                }
                var game = SteamName(appId);
                if (string.IsNullOrWhiteSpace(game))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no Steam title match");
                    continue;
                }
                if (recordedSteamAppIds.Add(appId)) EnsureSteamAppIdInGameIndex(libraryRoot, game, appId);
                var baseName = Path.GetFileNameWithoutExtension(file);
                string newBase;
                if (!TryBuildSteamRenameBase(baseName, appId, game, parsed.GameTitleHint, out newBase))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | not AppID-prefixed or title_timestamp form | " + Path.GetFileName(file));
                    continue;
                }
                if (string.Equals(newBase, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | already canonical name | " + Path.GetFileName(file));
                    continue;
                }
                var target = Unique(Path.Combine(Path.GetDirectoryName(file), newBase + Path.GetExtension(file)));
                File.Move(file, target);
                pathMap[file] = target;
                MoveMetadataSidecarIfPresent(file, target);
                renamed++;
                Log("Renamed: " + Path.GetFileName(file) + " -> " + Path.GetFileName(target));
                if (progress != null) progress(i + 1, total, "Renamed " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + Path.GetFileName(target));
            }
            if (progress != null) progress(total, total, "Rename step complete: renamed " + renamed + ", skipped " + skipped + ".");
            Log("Rename summary: renamed " + renamed + ", skipped " + skipped + ".");
            return new RenameStepResult { Renamed = renamed, Skipped = skipped, OldPathToNewPath = pathMap };
        }

        RenameStepResult RunManualRename(List<ManualMetadataItem> items, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            int renamed = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            if (progress != null) progress(0, total, "Starting rename step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            int deleted = 0, skipped = 0;
            var targets = (reviewItems ?? new List<ReviewItem>()).Where(i => i != null && i.DeleteBeforeProcessing).ToList();
            var total = targets.Count;
            if (progress != null) progress(0, total, "Starting delete step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = targets[i];
                var remaining = total - (i + 1);
                if (!File.Exists(item.FilePath))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped delete " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                File.Delete(item.FilePath);
                deleted++;
                Log("Deleted before processing: " + item.FileName);
                if (progress != null) progress(i + 1, total, "Deleted " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
            }
            if (progress != null) progress(total, total, "Delete step complete: deleted " + deleted + ", skipped " + skipped + ".");
            if (deleted > 0 || skipped > 0) Log("Delete summary: deleted " + deleted + ", skipped " + skipped + ".");
            return new DeleteStepResult { Deleted = deleted, Skipped = skipped };
        }

        int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            return metadataService.RunExifWriteRequests(requests, totalCount, alreadyCompleted, progress, cancellationToken);
        }

        MetadataStepResult RunMetadata(List<ReviewItem> reviewItems, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            int updated = 0, skipped = 0;
            var requests = new List<ExifWriteRequest>();
            var items = reviewItems ?? new List<ReviewItem>();
            var total = items.Count;
            if (progress != null) progress(0, total, "Starting metadata step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                var remaining = total - (i + 1);
                if (item.DeleteBeforeProcessing)
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | file marked for delete");
                    continue;
                }
                var file = item.FilePath;
                if (!File.Exists(file))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
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
            updated = RunExifWriteRequests(requests, requests.Count + skipped, skipped, progress, cancellationToken);
            if (progress != null) progress(total, total, "Metadata step complete: updated " + updated + ", skipped " + skipped + ".");
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

        MoveStepResult RunMove(IEnumerable<string> sourceFiles, HashSet<string> skipFiles, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var files = (sourceFiles ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Where(file => skipFiles == null || !skipFiles.Contains(file));
            return RunMoveFiles(files, "Move summary", progress, cancellationToken);
        }

        MoveStepResult RunMoveFiles(IEnumerable<string> files, string summaryLabel, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            int moved = 0, skipped = 0, renamedConflict = 0;
            var entries = new List<UndoImportEntry>();
            var fileList = (files ?? Enumerable.Empty<string>()).Where(File.Exists).ToList();
            var total = fileList.Count;
            if (progress != null) progress(0, total, "Starting move step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = fileList[i];
                var remaining = total - (i + 1);
                var sourceDirectory = Path.GetDirectoryName(file);
                var target = Path.Combine(destinationRoot, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    var mode = CurrentConflictMode();
                    if (mode == "Skip")
                    {
                        skipped++;
                        if (progress != null) progress(i + 1, total, "Skipped move " + (i + 1) + " of " + total + " | " + remaining + " remaining | conflict | " + Path.GetFileName(file));
                        continue;
                    }
                    if (mode == "Rename") { target = Unique(target); renamedConflict++; }
                    if (mode == "Overwrite") File.Delete(target);
                }
                File.Move(file, target);
                MoveMetadataSidecarIfPresent(file, target);
                moved++;
                entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(target), CurrentPath = target });
                AddSidecarUndoEntryIfPresent(target, sourceDirectory, entries);
                Log("Moved: " + Path.GetFileName(file) + " -> " + target);
                if (progress != null) progress(i + 1, total, "Moved " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + Path.GetFileName(target));
            }
            if (progress != null) progress(total, total, summaryLabel + ": moved " + moved + ", skipped " + skipped + ", renamed-on-conflict " + renamedConflict + ".");
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

        SortStepResult SortDestinationFoldersCore(bool interactive, bool updateUi = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureDir(destinationRoot, "Destination folder");
            var files = Directory.EnumerateFiles(destinationRoot, "*", SearchOption.TopDirectoryOnly).Where(IsMedia).ToList();
            if (files.Count == 0)
            {
                if (updateUi) status.Text = "Nothing to sort";
                Log("Sort destination found no root-level media files to organize.");
                if (interactive) MessageBox.Show("There are no root-level media files in the destination folder to sort right now.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return new SortStepResult();
            }

            int moved = 0, created = 0, renamedConflict = 0;
            var indexedTargets = new List<string>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            if (updateUi) status.Text = "Destination sorted";
            Log("Sort summary: sorted " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ".");
            if (updateUi) RefreshPreview();
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
