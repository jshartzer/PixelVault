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

        public IntakeAnalysisService(
            Func<string, FilenameParseResult> parseFilename,
            Func<string, bool> isVideo,
            Func<string, DateTime> getLibraryDate)
        {
            _parseFilename = parseFilename ?? throw new ArgumentNullException(nameof(parseFilename));
            _isVideo = isVideo ?? throw new ArgumentNullException(nameof(isVideo));
            _getLibraryDate = getLibraryDate ?? throw new ArgumentNullException(nameof(getLibraryDate));
        }

        public Dictionary<string, IntakePreviewFileAnalysis> AnalyzeFiles(
            IEnumerable<string> sourceFiles,
            CancellationToken cancellationToken = default)
        {
            var analysis = new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var parsed = _parseFilename(fileName);
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
    }
}
