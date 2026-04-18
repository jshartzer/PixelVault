using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 13 Pass A: promoted from a nested <c>MainWindow.LibraryBrowserFolderView</c>
    /// to a top-level <c>internal sealed</c> type so the library browser view-model can be consumed
    /// by plain classes (tests, <see cref="LibraryBrowseFolderSummary"/>, <see cref="ILibraryBrowserShell"/>,
    /// and future iOS/backend projections) without reaching through <c>MainWindow</c>.
    ///
    /// iOS alignment: contract-shaped. This record is the library browser's read model — every field
    /// is a plain value, no WPF types, no <c>MainWindow</c> state. <see cref="LibraryBrowseFolderSummary"/>
    /// already knows how to trim it down to a portable summary (no <see cref="SearchBlob"/>).
    /// </summary>
    internal sealed class LibraryBrowserFolderView
    {
        internal string ViewKey;
        internal string GameId;
        internal string Name;
        internal string PrimaryFolderPath;
        internal LibraryFolderInfo PrimaryFolder;
        internal readonly List<LibraryFolderInfo> SourceFolders = new List<LibraryFolderInfo>();
        internal string PrimaryPlatformLabel;
        internal string[] PlatformLabels = new string[0];
        internal string PlatformSummaryText;
        internal int FileCount;
        internal string PreviewImagePath;
        internal string[] FilePaths = new string[0];
        internal long NewestCaptureUtcTicks;
        internal long NewestRecentSortUtcTicks;
        internal string SteamAppId;
        internal string NonSteamId;
        internal string SteamGridDbId;
        internal string RetroAchievementsGameId;
        internal string CollectionNotes;
        internal bool SuppressSteamAppIdAutoResolve;
        internal bool SuppressSteamGridDbIdAutoResolve;
        internal bool IsCompleted100Percent;
        internal long CompletedUtcTicks;
        internal bool IsMergedAcrossPlatforms;
        internal bool IsTimelineProjection;
        internal bool PendingGameAssignment;
        /// <summary>Lowercase, newline-separated tokens for library search (name, paths, ids, platforms).</summary>
        internal string SearchBlob;
    }
}
