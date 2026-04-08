using System;

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
}
