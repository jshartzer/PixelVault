using System;

namespace PixelVaultNative
{
    /// <summary>
    /// Immutable browse-row snapshot for a library game/folder row (PV-PLN-UI-001 Step 4).
    /// No WPF types — suitable for future <c>GameSummary</c>-style APIs per <c>docs/ios_foundation_guide.md</c>.
    /// Built from <see cref="MainWindow.LibraryBrowserFolderView"/> after folder-cache projection.
    /// </summary>
    internal sealed class LibraryBrowseFolderSummary
    {
        public string ViewKey { get; }
        public string GameId { get; }
        public string Name { get; }
        public string PrimaryFolderPath { get; }
        public string PrimaryPlatformLabel { get; }
        public string[] PlatformLabels { get; }
        public string PlatformSummaryText { get; }
        public int FileCount { get; }
        public string PreviewImagePath { get; }
        public long NewestCaptureUtcTicks { get; }
        public long NewestRecentSortUtcTicks { get; }
        public string SteamAppId { get; }
        public string SteamGridDbId { get; }
        public bool IsCompleted100Percent { get; }
        public long CompletedUtcTicks { get; }
        public bool IsMergedAcrossPlatforms { get; }
        public bool IsTimelineProjection { get; }

        LibraryBrowseFolderSummary(
            string viewKey,
            string gameId,
            string name,
            string primaryFolderPath,
            string primaryPlatformLabel,
            string[] platformLabels,
            string platformSummaryText,
            int fileCount,
            string previewImagePath,
            long newestCaptureUtcTicks,
            long newestRecentSortUtcTicks,
            string steamAppId,
            string steamGridDbId,
            bool isCompleted100Percent,
            long completedUtcTicks,
            bool isMergedAcrossPlatforms,
            bool isTimelineProjection)
        {
            ViewKey = viewKey ?? string.Empty;
            GameId = gameId ?? string.Empty;
            Name = name ?? string.Empty;
            PrimaryFolderPath = primaryFolderPath ?? string.Empty;
            PrimaryPlatformLabel = primaryPlatformLabel ?? string.Empty;
            PlatformLabels = platformLabels ?? Array.Empty<string>();
            PlatformSummaryText = platformSummaryText ?? string.Empty;
            FileCount = fileCount;
            PreviewImagePath = previewImagePath ?? string.Empty;
            NewestCaptureUtcTicks = newestCaptureUtcTicks;
            NewestRecentSortUtcTicks = newestRecentSortUtcTicks;
            SteamAppId = steamAppId ?? string.Empty;
            SteamGridDbId = steamGridDbId ?? string.Empty;
            IsCompleted100Percent = isCompleted100Percent;
            CompletedUtcTicks = completedUtcTicks;
            IsMergedAcrossPlatforms = isMergedAcrossPlatforms;
            IsTimelineProjection = isTimelineProjection;
        }

        /// <summary>Maps a projected folder row to a portable summary (no <see cref="MainWindow.LibraryBrowserFolderView.SearchBlob"/> — search stays client-side).</summary>
        public static LibraryBrowseFolderSummary FromFolderView(MainWindow.LibraryBrowserFolderView view)
        {
            if (view == null) return null;
            var labels = view.PlatformLabels == null ? Array.Empty<string>() : (string[])view.PlatformLabels.Clone();
            return new LibraryBrowseFolderSummary(
                view.ViewKey,
                view.GameId,
                view.Name,
                view.PrimaryFolderPath,
                view.PrimaryPlatformLabel,
                labels,
                view.PlatformSummaryText,
                view.FileCount,
                view.PreviewImagePath,
                view.NewestCaptureUtcTicks,
                view.NewestRecentSortUtcTicks,
                view.SteamAppId,
                view.SteamGridDbId,
                view.IsCompleted100Percent,
                view.CompletedUtcTicks,
                view.IsMergedAcrossPlatforms,
                view.IsTimelineProjection);
        }
    }
}
