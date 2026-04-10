using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    sealed class FilenameConventionRule
    {
        public string ConventionId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; }
        public string Pattern { get; set; }
        public string PatternText { get; set; }
        public string PlatformLabel { get; set; }
        public string PlatformTagsText { get; set; }
        public string SteamAppIdGroup { get; set; }
        public string TitleGroup { get; set; }
        public string TimestampGroup { get; set; }
        public string TimestampFormat { get; set; }
        public bool PreserveFileTimes { get; set; }
        public bool RoutesToManualWhenMissingSteamAppId { get; set; }
        public string ConfidenceLabel { get; set; }
        public bool IsBuiltIn { get; set; }
    }

    sealed class FilenameParseResult
    {
        public string ConventionId { get; set; } = string.Empty;
        public string ConventionName { get; set; } = string.Empty;
        public string ConfidenceLabel { get; set; } = string.Empty;
        public string PlatformLabel { get; set; } = "Other";
        public string[] PlatformTags { get; set; } = new string[0];
        public string SteamAppId { get; set; } = string.Empty;
        public string NonSteamId { get; set; } = string.Empty;
        public string GameTitleHint { get; set; } = string.Empty;
        public DateTime? CaptureTime { get; set; }
        public bool PreserveFileTimes { get; set; }
        public bool RoutesToManualWhenMissingSteamAppId { get; set; }
        public bool MatchedConvention { get; set; }
    }

    sealed class FilenameConventionSample
    {
        public long SampleId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string SuggestedPlatformLabel { get; set; } = string.Empty;
        public string SuggestedConventionId { get; set; } = string.Empty;
        public long FirstSeenUtcTicks { get; set; }
        public long LastSeenUtcTicks { get; set; }
        public int OccurrenceCount { get; set; }
        public string LastSeenUtcText
        {
            get
            {
                return LastSeenUtcTicks > 0
                    ? new DateTime(LastSeenUtcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss")
                    : string.Empty;
            }
        }
    }

    enum FilenameConventionBuilderComponentRole
    {
        Literal = 0,
        Title,
        Timestamp,
        SteamAppId,
        NonSteamId,
        Counter,
        Extension
    }

    sealed class FilenameConventionBuilderSegment
    {
        public string Text { get; set; } = string.Empty;
        public FilenameConventionBuilderComponentRole SuggestedRole { get; set; } = FilenameConventionBuilderComponentRole.Literal;
        public FilenameConventionBuilderComponentRole AssignedRole { get; set; } = FilenameConventionBuilderComponentRole.Literal;
        public string Hint { get; set; } = string.Empty;
        public bool Locked { get; set; }
    }

    sealed class FilenameConventionBuilderDraft
    {
        public string FileName { get; set; } = string.Empty;
        public string RuleName { get; set; } = string.Empty;
        public string ConventionId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; } = 1200;
        public string PlatformLabel { get; set; } = "Other";
        public string PlatformTagsText { get; set; } = string.Empty;
        public string TimestampFormat { get; set; } = string.Empty;
        public bool PreserveFileTimes { get; set; }
        public bool RoutesToManualWhenMissingSteamAppId { get; set; }
        public bool IsBuiltInTemplate { get; set; }
        public bool CanRoundTripInBuilder { get; set; } = true;
        public string FallbackReason { get; set; } = string.Empty;
        public string ShapePreview { get; set; } = string.Empty;
        public string CrossSampleHintText { get; set; } = string.Empty;
        public List<FilenameConventionBuilderSegment> Segments { get; set; } = new List<FilenameConventionBuilderSegment>();
    }
}
