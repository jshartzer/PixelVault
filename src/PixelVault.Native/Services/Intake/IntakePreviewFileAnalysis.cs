using System;

namespace PixelVaultNative
{
    /// <summary>Per-file intake preview classification (parser output + automatic metadata eligibility).</summary>
    internal sealed class IntakePreviewFileAnalysis
    {
        public string FilePath = string.Empty;
        public string FileName = string.Empty;
        public FilenameParseResult Parsed = new FilenameParseResult();
        public bool CanUpdateMetadata;
        public bool PreserveFileTimes;
        public DateTime CaptureTime;
    }
}
