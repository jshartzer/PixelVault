using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>Shared intake file analysis for Intake Preview, import workflow, and future background auto-intake.</summary>
    internal sealed class IntakeAnalysisService
    {
        readonly Func<string, FilenameParseResult> _parseFilename;
        readonly Func<string, bool> _isVideo;
        readonly Func<string, DateTime> _getLibraryDate;
        readonly Func<IEnumerable<string>, CancellationToken, Dictionary<string, EmbeddedMetadataSnapshot>> _readEmbeddedMetadataBatch;

        public IntakeAnalysisService(
            Func<string, FilenameParseResult> parseFilename,
            Func<string, bool> isVideo,
            Func<string, DateTime> getLibraryDate,
            Func<IEnumerable<string>, CancellationToken, Dictionary<string, EmbeddedMetadataSnapshot>> readEmbeddedMetadataBatch = null)
        {
            _parseFilename = parseFilename ?? throw new ArgumentNullException(nameof(parseFilename));
            _isVideo = isVideo ?? throw new ArgumentNullException(nameof(isVideo));
            _getLibraryDate = getLibraryDate ?? throw new ArgumentNullException(nameof(getLibraryDate));
            _readEmbeddedMetadataBatch = readEmbeddedMetadataBatch;
        }

        public Dictionary<string, IntakePreviewFileAnalysis> AnalyzeFiles(
            IEnumerable<string> sourceFiles,
            CancellationToken cancellationToken = default)
        {
            var analysis = new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase);
            var fileList = (sourceFiles ?? Enumerable.Empty<string>())
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var parsedByFile = new Dictionary<string, FilenameParseResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                parsedByFile[file] = _parseFilename(Path.GetFileName(file)) ?? new FilenameParseResult();
            }

            var metadataByFile = new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
            if (_readEmbeddedMetadataBatch != null)
            {
                var metadataFallbackCandidates = fileList
                    .Where(file => !ParsedHasConsolePlatformHint(parsedByFile[file]))
                    .ToList();
                if (metadataFallbackCandidates.Count > 0)
                {
                    try
                    {
                        metadataByFile = _readEmbeddedMetadataBatch(metadataFallbackCandidates, cancellationToken)
                            ?? new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        metadataByFile = new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            foreach (var file in fileList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var parsed = parsedByFile[file];
                EmbeddedMetadataSnapshot metadata;
                if (metadataByFile.TryGetValue(file, out metadata))
                    parsed = ApplyEmbeddedMetadataConsoleHint(parsed, metadata);
                var platformTags = parsed.PlatformTags ?? new string[0];
                var isVideo = _isVideo(file);
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
                    CaptureTime = parsed.CaptureTime ?? _getLibraryDate(file)
                };
            }
            return analysis;
        }

        static bool ParsedHasConsolePlatformHint(FilenameParseResult parsed)
        {
            if (parsed == null) return false;
            var label = MainWindow.NormalizeConsoleLabel(parsed.PlatformLabel);
            if (MainWindow.ConsoleLabelBlocksFilenameFallback(label)) return true;
            var tagsLabel = MainWindow.NormalizeConsoleLabel(MainWindow.DetermineConsoleLabelFromTags(parsed.PlatformTags ?? new string[0]));
            return MainWindow.ConsoleLabelBlocksFilenameFallback(tagsLabel);
        }

        static FilenameParseResult ApplyEmbeddedMetadataConsoleHint(FilenameParseResult parsed, EmbeddedMetadataSnapshot metadata)
        {
            if (parsed == null) parsed = new FilenameParseResult();
            if (ParsedHasConsolePlatformHint(parsed)) return parsed;
            string platformLabel;
            string[] platformTags;
            if (!MainWindow.TryResolveConsolePlatformFromEmbeddedMetadata(metadata, out platformLabel, out platformTags)) return parsed;
            return new FilenameParseResult
            {
                ConventionId = parsed.ConventionId ?? string.Empty,
                ConventionName = parsed.ConventionName ?? string.Empty,
                ConfidenceLabel = parsed.ConfidenceLabel ?? string.Empty,
                PlatformLabel = platformLabel,
                PlatformTags = (parsed.PlatformTags ?? new string[0])
                    .Concat(platformTags ?? new string[0])
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                SteamAppId = parsed.SteamAppId ?? string.Empty,
                NonSteamId = parsed.NonSteamId ?? string.Empty,
                GameTitleHint = parsed.GameTitleHint ?? string.Empty,
                CaptureTime = parsed.CaptureTime,
                PreserveFileTimes = parsed.PreserveFileTimes,
                RoutesToManualWhenMissingSteamAppId = parsed.RoutesToManualWhenMissingSteamAppId,
                MatchedConvention = parsed.MatchedConvention
            };
        }
    }
}
