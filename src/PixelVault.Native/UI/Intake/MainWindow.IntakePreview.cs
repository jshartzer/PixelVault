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
            var previewAnalysis = AnalyzeIntakePreviewFiles(move, cancellationToken);
            var reviewItems = BuildReviewItems(move, previewAnalysis, cancellationToken);
            var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var manualItems = BuildManualMetadataItems(move, recognizedPaths, previewAnalysis, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var manualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var moveCandidates = move.Where(f => !manualPaths.Contains(f)).ToList();
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
                FilenameGuess = FilenameGuessLabel
            });
        }

        sealed class IntakePreviewFileAnalysis
        {
            public string FilePath = string.Empty;
            public string FileName = string.Empty;
            public FilenameParseResult Parsed = new FilenameParseResult();
            public bool CanUpdateMetadata;
            public bool PreserveFileTimes;
            public DateTime CaptureTime;
        }

        Dictionary<string, IntakePreviewFileAnalysis> AnalyzeIntakePreviewFiles(IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default(CancellationToken))
        {
            var analysis = new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var parsed = ParseFilename(fileName);
                var platformTags = parsed.PlatformTags ?? new string[0];
                var isVideo = IsVideo(file);
                // Respect the convention’s PreserveFileTimes (Steam-style: console captures update embedded + file dates from the filename). Do not force Xbox to use filesystem mtime as the capture instant.
                var preserveFileTimes = parsed.PreserveFileTimes || isVideo;
                var canUpdateMetadata = !(parsed.RoutesToManualWhenMissingSteamAppId && string.IsNullOrWhiteSpace(parsed.SteamAppId))
                    && (isVideo || platformTags.Contains("Xbox") || parsed.CaptureTime.HasValue);
                analysis[file] = new IntakePreviewFileAnalysis
                {
                    FilePath = file,
                    FileName = fileName,
                    Parsed = parsed,
                    CanUpdateMetadata = canUpdateMetadata,
                    PreserveFileTimes = preserveFileTimes,
                    CaptureTime = parsed.CaptureTime ?? GetLibraryDate(file)
                };
            }
            return analysis;
        }

        List<ReviewItem> BuildReviewItems()
        {
            return BuildReviewItems(importService.BuildSourceInventory(false).TopLevelMediaFiles);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles, CancellationToken cancellationToken = default(CancellationToken))
        {
            return BuildReviewItems(sourceFiles, AnalyzeIntakePreviewFiles(sourceFiles, cancellationToken), cancellationToken);
        }

        List<ReviewItem> BuildReviewItems(IEnumerable<string> sourceFiles, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ReviewItem>();
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null || !fileAnalysis.CanUpdateMetadata) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var platformTags = parsed.PlatformTags ?? new string[0];
                items.Add(new ReviewItem
                {
                    FilePath = file,
                    FileName = fileAnalysis.FileName,
                    PlatformLabel = parsed.PlatformLabel,
                    PlatformTags = platformTags,
                    CaptureTime = fileAnalysis.CaptureTime,
                    PreserveFileTimes = fileAnalysis.PreserveFileTimes,
                    Comment = string.Empty,
                    AddPhotographyTag = false,
                    DeleteBeforeProcessing = false
                });
            }
            return items
                .OrderBy(i => PlatformGroupOrder(i.PlatformLabel))
                .ThenBy(i => i.CaptureTime)
                .ThenBy(i => i.FileName)
                .ToList();
        }

        List<ManualMetadataItem> BuildManualMetadataItems(HashSet<string> recognizedPaths)
        {
            return BuildManualMetadataItems(importService.BuildSourceInventory(false).TopLevelMediaFiles, recognizedPaths);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths, CancellationToken cancellationToken = default(CancellationToken))
        {
            return BuildManualMetadataItems(sourceFiles, recognizedPaths, AnalyzeIntakePreviewFiles(sourceFiles, cancellationToken), cancellationToken);
        }

        List<ManualMetadataItem> BuildManualMetadataItems(IEnumerable<string> sourceFiles, HashSet<string> recognizedPaths, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ManualMetadataItem>();
            var known = recognizedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (known.Contains(file)) continue;
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null || fileAnalysis.CanUpdateMetadata) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var captureTime = fileAnalysis.CaptureTime;
                if (!parsed.MatchedConvention)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileAnalysis.FileName, parsed);
                }
                var titleHint = parsed.GameTitleHint ?? string.Empty;
                bool tagSteam, tagPc, tagPs5, tagXbox, tagOther;
                string customPlatformTag;
                ApplyFilenameParseResultToManualPlatformFlags(parsed, out tagSteam, out tagPc, out tagPs5, out tagXbox, out tagOther, out customPlatformTag);
                items.Add(new ManualMetadataItem
                {
                    GameId = string.Empty,
                    SteamAppId = parsed.SteamAppId,
                    FilePath = file,
                    FileName = fileAnalysis.FileName,
                    OriginalFileName = fileAnalysis.FileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = titleHint,
                    Comment = string.Empty,
                    TagText = string.Empty,
                    AddPhotographyTag = false,
                    TagSteam = tagSteam,
                    TagPs5 = tagPs5,
                    TagXbox = tagXbox,
                    TagPc = tagPc,
                    TagOther = tagOther,
                    CustomPlatformTag = customPlatformTag,
                    OriginalGameId = string.Empty,
                    OriginalSteamAppId = parsed.SteamAppId,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = titleHint,
                    OriginalComment = string.Empty,
                    OriginalTagText = string.Empty,
                    OriginalAddPhotographyTag = false,
                    OriginalTagSteam = tagSteam,
                    OriginalTagPc = tagPc,
                    OriginalTagPs5 = tagPs5,
                    OriginalTagXbox = tagXbox,
                    OriginalTagOther = tagOther,
                    OriginalCustomPlatformTag = customPlatformTag
                });
            }
            return items.OrderBy(i => i.CaptureTime).ThenBy(i => i.FileName).ToList();
        }

        /// <summary>All top-level upload files as manual-editor rows (rule-matched and manual-intake).</summary>
        List<ManualMetadataItem> BuildImportAndEditMetadataItems(IEnumerable<string> sourceFiles, Dictionary<string, IntakePreviewFileAnalysis> analysis, CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ManualMetadataItem>();
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var captureTime = fileAnalysis.CaptureTime;
                if (!parsed.MatchedConvention)
                {
                    indexPersistenceService.RecordFilenameConventionSample(libraryRoot, fileAnalysis.FileName, parsed);
                }
                var titleHint = parsed.GameTitleHint ?? string.Empty;
                bool tagSteam, tagPc, tagPs5, tagXbox, tagOther;
                string customPlatformTag;
                ApplyFilenameParseResultToManualPlatformFlags(parsed, out tagSteam, out tagPc, out tagPs5, out tagXbox, out tagOther, out customPlatformTag);
                var ruleMatched = fileAnalysis.CanUpdateMetadata;
                items.Add(new ManualMetadataItem
                {
                    GameId = string.Empty,
                    SteamAppId = parsed.SteamAppId,
                    FilePath = file,
                    FileName = fileAnalysis.FileName,
                    OriginalFileName = fileAnalysis.FileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = titleHint,
                    Comment = string.Empty,
                    TagText = string.Empty,
                    AddPhotographyTag = false,
                    TagSteam = tagSteam,
                    TagPs5 = tagPs5,
                    TagXbox = tagXbox,
                    TagPc = tagPc,
                    TagOther = tagOther,
                    CustomPlatformTag = customPlatformTag,
                    OriginalGameId = string.Empty,
                    OriginalSteamAppId = parsed.SteamAppId,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = titleHint,
                    OriginalComment = string.Empty,
                    OriginalTagText = string.Empty,
                    OriginalAddPhotographyTag = false,
                    OriginalTagSteam = tagSteam,
                    OriginalTagPc = tagPc,
                    OriginalTagPs5 = tagPs5,
                    OriginalTagXbox = tagXbox,
                    OriginalTagOther = tagOther,
                    OriginalCustomPlatformTag = customPlatformTag,
                    IntakeRuleMatched = ruleMatched,
                    DeleteBeforeProcessing = false
                });
            }
            return items
                .OrderBy(i => PlatformGroupOrder(DetermineManualMetadataPlatformLabel(i)))
                .ThenBy(i => i.CaptureTime)
                .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
