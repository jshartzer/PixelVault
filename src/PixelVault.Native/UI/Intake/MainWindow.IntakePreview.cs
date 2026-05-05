using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LoadIntakePreviewSummaryAsync(bool recurseRename, CancellationToken cancellationToken, Action<IntakePreviewSummary> onSuccess, Action<Exception> onError)
        {
            Task.Factory.StartNew(delegate
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var summary = BuildIntakePreviewSummary(recurseRename, cancellationToken);
                stopwatch.Stop();
                LogPerformanceSample("IntakePreviewBuild", stopwatch, "recurseRename=" + recurseRename + "; topLevel=" + summary.TopLevelMediaCount + "; reviewItems=" + summary.MetadataCandidateCount + "; manualItems=" + summary.ManualItemCount + "; conflicts=" + summary.ConflictCount, 40);
                return summary;
            }, cancellationToken).ContinueWith(delegate(Task<IntakePreviewSummary> summaryTask)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (summaryTask.IsFaulted)
                    {
                        var flattened = summaryTask.Exception == null ? null : summaryTask.Exception.Flatten();
                        var error = flattened == null ? new Exception("Preview failed.") : flattened.InnerExceptions.First();
                        if (onError != null) onError(error);
                        return;
                    }
                    if (summaryTask.IsCanceled)
                    {
                        if (onError != null) onError(new OperationCanceledException("Preview refresh cancelled."));
                        return;
                    }
                    if (onSuccess != null) onSuccess(summaryTask.Result);
                }));
            }, TaskScheduler.Default);
        }

        IntakePreviewSummary BuildIntakePreviewSummary(bool recurseRename, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSourceFolders();
            var inventory = importService.BuildSourceInventory(recurseRename);
            cancellationToken.ThrowIfCancellationRequested();
            var rename = inventory.RenameScopeFiles;
            var move = inventory.TopLevelMediaFiles;
            var prep = BuildIntakePreparation(move, false, cancellationToken);
            var reviewItems = prep.ReviewItems;
            var manualItems = prep.ManualItems;
            cancellationToken.ThrowIfCancellationRequested();
            var moveCandidates = move.Where(f => !prep.ManualPaths.Contains(f)).ToList();
            return new IntakePreviewSummary
            {
                SourceRoots = GetSourceRoots(),
                RenameScopeCount = rename.Count,
                RenameCandidateCount = rename.Count(f => !string.IsNullOrWhiteSpace(GuessSteamAppIdFromFileName(f))),
                TopLevelMediaCount = move.Count,
                MetadataCandidateCount = reviewItems.Count,
                MoveCandidateCount = moveCandidates.Count,
                ManualItemCount = manualItems.Count,
                ConflictCount = Directory.Exists(destinationRoot) ? moveCandidates.Count(f => File.Exists(Path.Combine(destinationRoot, Path.GetFileName(f)))) : 0,
                ReviewItems = reviewItems,
                ManualItems = manualItems
            };
        }

        void LogIntakePreviewSummary(IntakePreviewSummary summary)
        {
            if (summary == null) return;
            Log("Intake preview refreshed. Sources=" + (summary.SourceRoots.Count == 0 ? "(none)" : string.Join(" | ", summary.SourceRoots.ToArray())) + "; RenameCandidates=" + summary.RenameCandidateCount + "; MetadataCandidates=" + summary.MetadataCandidateCount + "; MoveCandidates=" + summary.MoveCandidateCount + "; ManualCandidates=" + summary.ManualItemCount + ".");
        }

        void RefreshPreview()
        {
        }

        void ShowIntakePreviewWindow(bool recurseRename)
        {
            IntakePreviewWindow.Show(this, AppVersion, recurseRename, new IntakePreviewServices
            {
                LoadSummaryAsync = LoadIntakePreviewSummaryAsync,
                OpenSourceFolders = OpenSourceFolders,
                OpenManualIntake = OpenManualIntakeWindow,
                SyncSettingsDocument = null,
                SyncSettingsDocumentError = null,
                SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                Log = Log,
                LogSummary = LogIntakePreviewSummary,
                CreateButton = Btn,
                PreviewBadge = PreviewBadgeBrush,
                PlatformOrder = PlatformGroupOrder,
                FormatTimestamp = FormatFriendlyTimestamp,
                FilenameGuess = FilenameGuessLabel,
                NotifyUser = (msg, icon) => TryLibraryToast(msg, icon)
            });
        }

        Dictionary<string, IntakePreviewFileAnalysis> AnalyzeIntakePreviewFiles(IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default(CancellationToken))
        {
            return intakePipeline.Analysis.AnalyzeFiles(sourceFiles, cancellationToken);
        }

        IntakePreparationResult BuildIntakePreparation(IEnumerable<string> sourceFiles, bool includeImportEditRows, CancellationToken cancellationToken = default(CancellationToken))
        {
            return IntakePreparationBuilder.Build(
                sourceFiles,
                AnalyzeIntakePreviewFiles,
                includeImportEditRows,
                delegate(string fileName, FilenameParseResult parsed)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileName, parsed);
                },
                cancellationToken);
        }

        List<ReviewItem> BuildReviewItems()
        {
            return BuildReviewItems(importService.BuildSourceInventory(importSearchSubfoldersForRename).TopLevelMediaFiles);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default(CancellationToken))
        {
            return BuildReviewItems(sourceFiles, AnalyzeIntakePreviewFiles(sourceFiles, cancellationToken), cancellationToken);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            return IntakePreparationBuilder.BuildReviewItems(sourceFiles, analysis, cancellationToken);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(HashSet<string> recognizedPaths)
        {
            return BuildManualMetadataItems(importService.BuildSourceInventory(importSearchSubfoldersForRename).TopLevelMediaFiles, recognizedPaths);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths, CancellationToken cancellationToken = default(CancellationToken))
        {
            return BuildManualMetadataItems(sourceFiles, recognizedPaths, AnalyzeIntakePreviewFiles(sourceFiles, cancellationToken), cancellationToken);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            return IntakePreparationBuilder.BuildManualMetadataItems(
                sourceFiles,
                recognizedPaths,
                analysis,
                delegate(string fileName, FilenameParseResult parsed)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileName, parsed);
                },
                cancellationToken);
        }

        /// <summary>All top-level upload files as manual-editor rows (rule-matched and manual-intake).</summary>
        List<ManualMetadataItem> BuildImportAndEditMetadataItems(IEnumerable<string> sourceFiles, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            return IntakePreparationBuilder.BuildImportAndEditMetadataItems(
                sourceFiles,
                analysis,
                delegate(string fileName, FilenameParseResult parsed)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileName, parsed);
                },
                cancellationToken);
        }
    }
}
