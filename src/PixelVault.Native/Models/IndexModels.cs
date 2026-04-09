using System;
using System.Globalization;

namespace PixelVaultNative
{
    internal static class LibraryIndexRecordDisplay
    {
        internal static string FormatIndexAddedUtcLocal(long ticks)
        {
            if (ticks <= 0) return string.Empty;
            try
            {
                return new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    sealed class LibraryFolderInfo
    {
        public string GameId;
        public string Name;
        public string FolderPath;
        public int FileCount;
        public string PreviewImagePath;
        public string PlatformLabel;
        public string[] FilePaths;
        public long NewestCaptureUtcTicks;
        /// <summary>Max over files of (index date added if set, else capture/reindexed date ticks); used for Recently Added folder sort.</summary>
        public long NewestRecentSortUtcTicks;
        public string SteamAppId;
        public string NonSteamId;
        public string SteamGridDbId;
        /// <summary>RetroAchievements.org game ID (numeric in their API; stored as text).</summary>
        public string RetroAchievementsGameId;
        public bool SuppressSteamAppIdAutoResolve;
        public bool SuppressSteamGridDbIdAutoResolve;
        public bool IsCompleted100Percent;
        public long CompletedUtcTicks;
        public bool IsFavorite;
        public bool IsShowcase;
        public string CollectionNotes;
        /// <summary>Photo index rows under <see cref="FolderPath"/> have no <c>GameId</c> yet (LIBST Step 1 unresolved bucket). Not a game title.</summary>
        public bool PendingGameAssignment;
        /// <summary>Shared storage bucket from game index (<c>PV-PLN-LIBST-001</c>); informational on browse rows until placement consumes it.</summary>
        public string StorageGroupId;
    }

    sealed class GameIndexEditorRow
    {
        public string GameId { get; set; }
        public string Name { get; set; }
        public string PlatformLabel { get; set; }
        public string SteamAppId { get; set; }
        public string NonSteamId { get; set; }
        public string SteamGridDbId { get; set; }
        /// <summary>RetroAchievements.org game ID.</summary>
        public string RetroAchievementsGameId { get; set; }
        public bool SuppressSteamAppIdAutoResolve { get; set; }
        public bool SuppressSteamGridDbIdAutoResolve { get; set; }
        public int FileCount { get; set; }
        public string FolderPath { get; set; }
        public string PreviewImagePath { get; set; }
        public string[] FilePaths { get; set; }
        public bool IsCompleted100Percent { get; set; }
        /// <summary>UTC ticks when the game was manually marked complete / 100% (0 = unknown / not set).</summary>
        public long CompletedUtcTicks { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsShowcase { get; set; }
        public string CollectionNotes { get; set; }
        /// <summary>UTC ticks when this game row was first added to the index (0 = unknown / predates field).</summary>
        public long IndexAddedUtcTicks { get; set; }
        public string IndexAddedAtLocal => LibraryIndexRecordDisplay.FormatIndexAddedUtcLocal(IndexAddedUtcTicks);
        /// <summary>Stable cross-platform storage bucket id (shared folder target); see PV-PLN-LIBST-001.</summary>
        public string StorageGroupId { get; set; }
    }

    sealed class PhotoIndexEditorRow
    {
        public string FilePath { get; set; }
        public string Stamp { get; set; }
        public string GameId { get; set; }
        /// <summary>Optional per-file RetroAchievements game ID (denormalized; game index is authoritative for the title).</summary>
        public string RetroAchievementsGameId { get; set; }
        public string ConsoleLabel { get; set; }
        public string TagText { get; set; }
        public bool Starred { get; set; }
        /// <summary>UTC ticks when this file row was first added to the photo index (0 = unknown / predates field).</summary>
        public long IndexAddedUtcTicks { get; set; }
        public string IndexAddedAtLocal => LibraryIndexRecordDisplay.FormatIndexAddedUtcLocal(IndexAddedUtcTicks);
    }

    sealed class LibraryMetadataIndexEntry
    {
        public string FilePath;
        public string Stamp;
        public string GameId;
        public string RetroAchievementsGameId;
        public string ConsoleLabel;
        public string TagText;
        public long CaptureUtcTicks;
        public bool Starred;
        /// <summary>UTC ticks when this file was first recorded in the photo index.</summary>
        public long IndexAddedUtcTicks;
    }

    sealed class VideoClipInfo
    {
        public double DurationSeconds;
        public int Width;
        public int Height;
        public double FrameRate;
        public bool HasAudio;
        public string VideoCodec;
    }
}
