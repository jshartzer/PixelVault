#nullable enable
using System;
using System.Linq;

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
        public string NonSteamId { get; }
        public string SteamGridDbId { get; }
        public string RetroAchievementsGameId { get; }
        public bool IsCompleted100Percent { get; }
        public long CompletedUtcTicks { get; }
        public bool IsMergedAcrossPlatforms { get; }
        public bool IsTimelineProjection { get; }
        public bool PendingGameAssignment { get; }

        LibraryBrowseFolderSummary(
            string? viewKey,
            string? gameId,
            string? name,
            string? primaryFolderPath,
            string? primaryPlatformLabel,
            string[]? platformLabels,
            string? platformSummaryText,
            int fileCount,
            string? previewImagePath,
            long newestCaptureUtcTicks,
            long newestRecentSortUtcTicks,
            string? steamAppId,
            string? nonSteamId,
            string? steamGridDbId,
            string? retroAchievementsGameId,
            bool isCompleted100Percent,
            long completedUtcTicks,
            bool isMergedAcrossPlatforms,
            bool isTimelineProjection,
            bool pendingGameAssignment)
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
            NonSteamId = nonSteamId ?? string.Empty;
            SteamGridDbId = steamGridDbId ?? string.Empty;
            RetroAchievementsGameId = retroAchievementsGameId ?? string.Empty;
            IsCompleted100Percent = isCompleted100Percent;
            CompletedUtcTicks = completedUtcTicks;
            IsMergedAcrossPlatforms = isMergedAcrossPlatforms;
            IsTimelineProjection = isTimelineProjection;
            PendingGameAssignment = pendingGameAssignment;
        }

        /// <summary>Maps a projected folder row to a portable summary (no <see cref="MainWindow.LibraryBrowserFolderView.SearchBlob"/> — search stays client-side).</summary>
        public static LibraryBrowseFolderSummary? FromFolderView(MainWindow.LibraryBrowserFolderView? view)
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
                view.NonSteamId,
                view.SteamGridDbId,
                view.RetroAchievementsGameId,
                view.IsCompleted100Percent,
                view.CompletedUtcTicks,
                view.IsMergedAcrossPlatforms,
                view.IsTimelineProjection,
                view.PendingGameAssignment);
        }

        /// <summary>True when primary or any platform label normalizes to Steam (same rules as folder-list filters).</summary>
        public static bool IsSteamTagged(LibraryBrowseFolderSummary? folder, Func<string, string>? normalizeConsoleLabel)
        {
            if (folder == null) return false;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            if (string.Equals(normConsole(folder.PrimaryPlatformLabel ?? string.Empty), "Steam", StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var label in folder.PlatformLabels ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (string.Equals(normConsole(label), "Steam", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>True when primary or any platform label normalizes to Emulation (RetroAchievements context).</summary>
        public static bool IsEmulationTagged(LibraryBrowseFolderSummary? folder, Func<string, string>? normalizeConsoleLabel)
        {
            if (folder == null) return false;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            var normEmu = normConsole("Emulation");
            if (string.Equals(normConsole(folder.PrimaryPlatformLabel ?? string.Empty), normEmu, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var label in folder.PlatformLabels ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (string.Equals(normConsole(label), normEmu, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Folder-list filter predicate over a browse summary; keep in sync with <c>docs/SMART_VIEWS_LIBRARY.md</c>.</summary>
        public static bool MatchesFilter(string? normalizedFilterMode, LibraryBrowseFolderSummary? folder, Func<string, string>? normalizeConsoleLabel)
        {
            if (folder == null) return false;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            switch ((normalizedFilterMode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                    return folder.IsCompleted100Percent;
                case "crossplatform":
                {
                    var distinct = (folder.PlatformLabels ?? Array.Empty<string>())
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Select(l => normConsole(l))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    return distinct > 1 || folder.IsMergedAcrossPlatforms;
                }
                case "large":
                    return folder.FileCount >= 25;
                case "missingid":
                {
                    if (folder.PendingGameAssignment) return true;
                    if (string.IsNullOrWhiteSpace(folder.GameId)) return true;
                    if (string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return true;
                    if (IsSteamTagged(folder, normConsole) && string.IsNullOrWhiteSpace(folder.SteamAppId)) return true;
                    if (IsEmulationTagged(folder, normConsole) && string.IsNullOrWhiteSpace(folder.RetroAchievementsGameId)) return true;
                    return false;
                }
                case "missingnonsteamid":
                    return string.IsNullOrWhiteSpace(folder.GameId) || (IsEmulationTagged(folder, normConsole) && string.IsNullOrWhiteSpace(folder.NonSteamId));
                case "nocover":
                    return string.IsNullOrWhiteSpace(folder.PreviewImagePath);
                default:
                    return true;
            }
        }
    }
}
