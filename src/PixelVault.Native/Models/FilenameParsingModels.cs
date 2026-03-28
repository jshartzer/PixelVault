using System;

namespace PixelVaultNative
{
    sealed class FilenameConventionRule
    {
        public string ConventionId;
        public string Name;
        public bool Enabled = true;
        public int Priority;
        public string Pattern;
        public string PlatformLabel;
        public string PlatformTagsText;
        public string SteamAppIdGroup;
        public string TitleGroup;
        public string TimestampGroup;
        public string TimestampFormat;
        public bool PreserveFileTimes;
        public bool RoutesToManualWhenMissingSteamAppId;
        public string ConfidenceLabel;
        public bool IsBuiltIn;
    }

    sealed class FilenameParseResult
    {
        public string ConventionId = string.Empty;
        public string ConventionName = string.Empty;
        public string ConfidenceLabel = string.Empty;
        public string PlatformLabel = "Other";
        public string[] PlatformTags = new string[0];
        public string SteamAppId = string.Empty;
        public string GameTitleHint = string.Empty;
        public DateTime? CaptureTime;
        public bool PreserveFileTimes;
        public bool RoutesToManualWhenMissingSteamAppId;
        public bool MatchedConvention;
    }

    sealed class FilenameConventionSample
    {
        public long SampleId;
        public string FileName = string.Empty;
        public string SuggestedPlatformLabel = string.Empty;
        public string SuggestedConventionId = string.Empty;
        public long FirstSeenUtcTicks;
        public long LastSeenUtcTicks;
        public int OccurrenceCount;
    }
}
