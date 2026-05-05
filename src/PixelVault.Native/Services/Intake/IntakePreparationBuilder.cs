using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PixelVaultNative
{
    internal sealed class IntakePreparationResult
    {
        public Dictionary<string, IntakePreviewFileAnalysis> Analysis = new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase);
        public List<ReviewItem> ReviewItems = new List<ReviewItem>();
        public HashSet<string> RecognizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<ManualMetadataItem> ManualItems = new List<ManualMetadataItem>();
        public HashSet<string> ManualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<ManualMetadataItem> ImportEditItems = new List<ManualMetadataItem>();
    }

    /// <summary>Builds import prep rows from one shared intake-analysis pass.</summary>
    internal static class IntakePreparationBuilder
    {
        public static IntakePreparationResult Build(
            IEnumerable<string> sourceFiles,
            Func<IEnumerable<string>, CancellationToken, Dictionary<string, IntakePreviewFileAnalysis>> analyzeFiles,
            bool includeImportEditRows,
            Action<string, FilenameParseResult> recordUnmatchedConventionSample = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (analyzeFiles == null) throw new ArgumentNullException(nameof(analyzeFiles));
            var files = ExistingDistinctFiles(sourceFiles);
            var analysis = analyzeFiles(files, cancellationToken) ?? new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase);
            var reviewItems = BuildReviewItems(files, analysis, cancellationToken);
            var recognizedPaths = new HashSet<string>(reviewItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase);
            var manualItems = BuildManualMetadataItems(files, recognizedPaths, analysis, recordUnmatchedConventionSample, cancellationToken);
            return new IntakePreparationResult
            {
                Analysis = analysis,
                ReviewItems = reviewItems,
                RecognizedPaths = recognizedPaths,
                ManualItems = manualItems,
                ManualPaths = new HashSet<string>(manualItems.Select(i => i.FilePath), StringComparer.OrdinalIgnoreCase),
                ImportEditItems = includeImportEditRows
                    ? BuildImportAndEditMetadataItems(files, analysis, recordUnmatchedConventionSample, cancellationToken)
                    : new List<ManualMetadataItem>()
            };
        }

        public static List<ReviewItem> BuildReviewItems(
            IEnumerable<string> sourceFiles,
            Dictionary<string, IntakePreviewFileAnalysis> analysis,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ReviewItem>();
            foreach (var file in ExistingDistinctFiles(sourceFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null || !fileAnalysis.CanUpdateMetadata) continue;
                var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
                var platformTags = parsed.PlatformTags ?? new string[0];
                var resolvedPlatforms = MainWindow.ExtractConsolePlatformFamilies(platformTags.Concat(new[] { parsed.PlatformLabel }));
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
                    TagSteam = resolvedPlatforms.Contains("Steam"),
                    TagPc = resolvedPlatforms.Contains("PC"),
                    TagEmulation = resolvedPlatforms.Contains("Emulation"),
                    TagSwitch = resolvedPlatforms.Contains("Switch"),
                    TagPs5 = resolvedPlatforms.Contains("PS5"),
                    TagXbox = resolvedPlatforms.Contains("Xbox"),
                    DeleteBeforeProcessing = false
                });
            }
            return items
                .OrderBy(i => LibraryPlatformLabels.PlatformGroupOrder(i.PlatformLabel))
                .ThenBy(i => i.CaptureTime)
                .ThenBy(i => i.FileName)
                .ToList();
        }

        public static List<ManualMetadataItem> BuildManualMetadataItems(
            IEnumerable<string> sourceFiles,
            HashSet<string> recognizedPaths,
            Dictionary<string, IntakePreviewFileAnalysis> analysis,
            Action<string, FilenameParseResult> recordUnmatchedConventionSample = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ManualMetadataItem>();
            var known = recognizedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in ExistingDistinctFiles(sourceFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (known.Contains(file)) continue;
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null || fileAnalysis.CanUpdateMetadata) continue;
                items.Add(BuildManualMetadataItem(file, fileAnalysis, recordUnmatchedConventionSample, false));
            }
            return items.OrderBy(i => i.CaptureTime).ThenBy(i => i.FileName).ToList();
        }

        public static List<ManualMetadataItem> BuildImportAndEditMetadataItems(
            IEnumerable<string> sourceFiles,
            Dictionary<string, IntakePreviewFileAnalysis> analysis,
            Action<string, FilenameParseResult> recordUnmatchedConventionSample = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var items = new List<ManualMetadataItem>();
            foreach (var file in ExistingDistinctFiles(sourceFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IntakePreviewFileAnalysis fileAnalysis;
                if (analysis == null || !analysis.TryGetValue(file, out fileAnalysis) || fileAnalysis == null) continue;
                items.Add(BuildManualMetadataItem(file, fileAnalysis, recordUnmatchedConventionSample, fileAnalysis.CanUpdateMetadata));
            }
            return items
                .OrderBy(i => LibraryPlatformLabels.PlatformGroupOrder(DetermineManualMetadataPlatformLabel(i)))
                .ThenBy(i => i.CaptureTime)
                .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static ManualMetadataItem BuildManualMetadataItem(
            string file,
            IntakePreviewFileAnalysis fileAnalysis,
            Action<string, FilenameParseResult> recordUnmatchedConventionSample,
            bool importRuleMatched)
        {
            var parsed = fileAnalysis.Parsed ?? new FilenameParseResult();
            if (!parsed.MatchedConvention && recordUnmatchedConventionSample != null)
            {
                recordUnmatchedConventionSample(fileAnalysis.FileName, parsed);
            }

            var captureTime = fileAnalysis.CaptureTime;
            var titleHint = parsed.GameTitleHint ?? string.Empty;
            bool tagSteam, tagPc, tagEmulation, tagPs5, tagSwitch, tagXbox, tagOther;
            string customPlatformTag;
            MainWindow.ApplyFilenameParseResultToManualPlatformFlags(parsed, out tagSteam, out tagPc, out tagEmulation, out tagPs5, out tagSwitch, out tagXbox, out tagOther, out customPlatformTag);
            return new ManualMetadataItem
            {
                GameId = string.Empty,
                SteamAppId = parsed.SteamAppId,
                NonSteamId = parsed.NonSteamId,
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
                TagSwitch = tagSwitch,
                TagXbox = tagXbox,
                TagPc = tagPc,
                TagEmulation = tagEmulation,
                TagOther = tagOther,
                CustomPlatformTag = customPlatformTag,
                OriginalGameId = string.Empty,
                OriginalSteamAppId = parsed.SteamAppId,
                OriginalNonSteamId = parsed.NonSteamId,
                OriginalCaptureTime = captureTime,
                OriginalUseCustomCaptureTime = false,
                OriginalGameName = titleHint,
                OriginalComment = string.Empty,
                OriginalTagText = string.Empty,
                OriginalAddPhotographyTag = false,
                OriginalTagSteam = tagSteam,
                OriginalTagPc = tagPc,
                OriginalTagPs5 = tagPs5,
                OriginalTagSwitch = tagSwitch,
                OriginalTagXbox = tagXbox,
                OriginalTagEmulation = tagEmulation,
                OriginalTagOther = tagOther,
                OriginalCustomPlatformTag = customPlatformTag,
                IntakeRuleMatched = importRuleMatched,
                DeleteBeforeProcessing = false
            };
        }

        static string DetermineManualMetadataPlatformLabel(ManualMetadataItem item)
        {
            if (item == null) return "Other";
            if (item.TagSteam) return "Steam";
            if (item.TagPc) return "PC";
            if (item.TagEmulation) return "Emulation";
            if (item.TagPs5) return "PS5";
            if (item.TagSwitch) return "Switch";
            if (item.TagXbox) return "Xbox";
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return MainWindow.NormalizeConsoleLabel(item.CustomPlatformTag);
            return "Other";
        }

        static List<string> ExistingDistinctFiles(IEnumerable<string> sourceFiles)
        {
            return (sourceFiles ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
